module BlueCode.Cli.Adapters.Json

open System.Text.Json
open System.Text.Json.Serialization
open System.Text.RegularExpressions
open Json.Schema
open BlueCode.Core.Domain
open BlueCode.Cli.Adapters.LlmWire

/// Shared options for ALL JSON serialization/deserialization in the CLI
/// adapter layer. Built once at module-init; passed to every JsonSerializer
/// call. DO NOT create JsonSerializerOptions inline at call sites — it is
/// expensive (reflection-based converter discovery) and silently creates
/// freezable options that race with first use.
///
/// JsonFSharpConverter registered with WithUnionUnwrapFieldlessTags(true)
/// configures F#-idiomatic behavior:
///   - option types: None -> null, Some v -> v
///   - F# list -> JSON array
///   - F# Map -> JSON object
///   - Fieldless DU cases serialize as bare strings: System -> "System"
///     (NOT the default {"Case":"System"} adjacent-tag form)
///
/// WithUnionUnwrapFieldlessTags(true) is required for LLM-04: MessageRole
/// (System | User | Assistant) must round-trip as "System", "User",
/// "Assistant" — the bare-string form. Without this flag the default
/// adjacent-tag form {"Case":"System"} is produced, which is not
/// the F#-idiomatic form the QwenHttpClient wire protocol expects.
///
/// PUBLIC: used by QwenHttpClient (Plan 02-03) for buildRequestBody
/// serialization and reused for DU round-trip guarantees (LLM-04).
let jsonOptions: JsonSerializerOptions =
    let opts = JsonSerializerOptions()
    opts.Converters.Add(JsonFSharpConverter(JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags(true)))
    opts

// ── Stage 1: JSON object validity check ──────────────────────────────────────

/// Attempt to deserialize content directly as LlmStep without any pre-processing.
/// Returns Some step on success, None on any parse error (including malformed JSON,
/// missing fields, or wrong types). Used as a fast path for bare valid LlmStep JSON.
///
/// NOTE: This helper is intentionally separate from the extraction validity check.
/// Extraction uses tryParseJsonObject (which accepts ANY valid JSON object) while
/// tryBareParse additionally requires the JSON to match the LlmStep record shape.
/// Schema validation (validateAndDeserialize) is what enforces shape correctness
/// for the pipeline — tryBareParse is only used internally.
///
/// PRIVATE: internal helper.
let private tryBareParse (content: string) : LlmStep option =
    try
        JsonSerializer.Deserialize<LlmStep>(content, jsonOptions) |> Some
    with _ ->
        None

/// Check if the string is a valid JSON object (type = Object, any shape).
/// Used by the extraction stages to determine if we found a JSON object worth
/// passing to schema validation, without pre-filtering by LlmStep structure.
/// This is critical: a JSON object missing `action` must reach validateAndDeserialize
/// so it gets SchemaViolation (not InvalidJsonOutput).
///
/// PRIVATE: internal helper — used by extractLlmStep extraction stages.
let private tryParseJsonObject (s: string) : string option =
    try
        use doc = JsonDocument.Parse(s)

        if doc.RootElement.ValueKind = JsonValueKind.Object then
            Some s
        else
            None
    with _ ->
        None

// ── Stage 2: stack-based O(N) first-object extraction ────────────────────────

/// Extract the first top-level JSON object from a string. Handles prose
/// before/after, nested objects, escapes inside strings. Regex CANNOT do
/// this (no depth counting). Returns None if no balanced object found.
///
/// String-literal awareness: tracks `inString` and `escape` flags so that
/// '{' and '}' inside a JSON string value (e.g. "he said \"{...}\"") do
/// not affect depth counting. This is a correctness requirement — Qwen
/// prose responses frequently quote JSON snippets in explanation text.
///
/// PRIVATE: internal helper — composed into extractLlmStep.
let private extractFirstJsonObject (s: string) : string option =
    let mutable depth = 0
    let mutable start = -1
    let mutable found: string option = None
    let mutable inString = false
    let mutable escape = false
    let mutable i = 0

    while i < s.Length && found.IsNone do
        let c = s.[i]

        if escape then
            escape <- false
        elif inString then
            if c = '\\' then
                escape <- true
            elif c = '"' then
                inString <- false
        else
            match c with
            | '"' -> inString <- true
            | '{' ->
                if depth = 0 then
                    start <- i

                depth <- depth + 1
            | '}' ->
                depth <- depth - 1

                if depth = 0 && start >= 0 then
                    found <- Some(s.Substring(start, i - start + 1))
            | _ -> ()

        i <- i + 1

    found

// ── Stage 3: markdown fence strip ────────────────────────────────────────────

/// Non-greedy match of the FIRST fenced code block containing a JSON object.
/// Accepts optional "json" language tag. Unclosed fences do not match
/// (they fall through to ParseFailure — signals max_tokens truncation).
///
/// [\s\S]*? is non-greedy: matches the FIRST fence pair, not the last.
/// Without non-greedy, multiple code blocks would be merged into one.
let private fencePattern =
    Regex(@"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.Compiled)

/// Extract JSON object from a markdown fenced code block.
/// Returns Some json on match (outer braces included), None if no fence found.
///
/// PRIVATE: internal helper — composed into extractLlmStep.
let private tryFenceExtract (content: string) : string option =
    let m = fencePattern.Match(content)
    if m.Success then Some m.Groups.[1].Value else None

// ── Stage composition: extract JSON string from any LLM output ───────────────

/// Multi-stage extraction. Attempts stages in order:
///   1. Bare parse: content is a valid JSON object (any shape)
///   2. Brace scan: extract first balanced JSON object from prose-wrapped content
///   3. Fence strip then re-extract: extract from markdown code block
///   4. ParseFailure: no JSON recoverable → Error (InvalidJsonOutput content)
///
/// Returns Ok jsonString on the first stage that produces a valid JSON object.
/// Returns Error (InvalidJsonOutput raw) only if all stages find no JSON object.
///
/// IMPORTANT: extraction checks for JSON validity only — NOT LlmStep structure.
/// A JSON object missing required fields (e.g. no "action") still returns Ok.
/// Schema enforcement is the responsibility of validateAndDeserialize (called by
/// parseLlmResponse), not the extraction stage. This separation ensures that
/// structurally-invalid-but-extractable JSON reaches the schema validator and
/// gets Error (SchemaViolation) instead of Error (InvalidJsonOutput).
///
/// PRIVATE: callers MUST use parseLlmResponse (which layers schema
/// validation on top). Exposing extractLlmStep would let callers bypass
/// schema enforcement — exactly the drift we are defending against.
let private extractLlmStep (content: string) : Result<string, AgentError> =
    // Stage 1: direct bare JSON object check
    match tryParseJsonObject content with
    | Some json -> Ok json
    | None ->
        // Stage 2: brace-scan extract + JSON object check
        match extractFirstJsonObject content |> Option.bind tryParseJsonObject with
        | Some json -> Ok json
        | None ->
            // Stage 3: fence strip + JSON object check (fence content may itself be prose-wrapped)
            match tryFenceExtract content with
            | Some fenced ->
                match tryParseJsonObject fenced with
                | Some json -> Ok json
                | None ->
                    match extractFirstJsonObject fenced |> Option.bind tryParseJsonObject with
                    | Some json -> Ok json
                    | None -> Error(InvalidJsonOutput content)
            | None -> Error(InvalidJsonOutput content)

// ── Schema validation (JsonSchema.Net 9.2.0) ─────────────────────────────────

/// draft-2020-12 schema for the LLM step wire format {thought, action, input}.
/// Enforced constraints:
///   - thought: non-empty string (minLength: 1)
///   - action:  one of the 5 known action values (enum check prevents drift)
///   - input:   MUST be an object (per-tool shape validated in Phase 3)
///   - additionalProperties: false (prevents Qwen confidence/reasoning fields)
///
/// PUBLIC: module-scope constant. No invariants to protect (immutable data).
let llmStepSchema: JsonSchema =
    JsonSchema.FromText(
        """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["thought", "action", "input"],
      "properties": {
        "thought": { "type": "string", "minLength": 1 },
        "action": {
          "type": "string",
          "enum": ["read_file", "write_file", "list_dir", "run_shell", "final"]
        },
        "input": { "type": "object" }
      },
      "additionalProperties": false
    }
    """
    )

/// Validate a JSON string against llmStepSchema and deserialize to LlmStep.
/// Uses OutputFormat.List to populate Details so error messages carry
/// location + reason. Calling Evaluate without OutputFormat.List would
/// leave results.Details empty even on failure (Pitfall 1 in 02-RESEARCH).
///
/// Returns:
///   Ok step                     when schema passes and deserialization succeeds
///   Error (SchemaViolation msg) when schema rejects the JSON, with concatenated
///                               error details from EvaluationResults.Details
///
/// PRIVATE: internal helper called only from parseLlmResponse.
let private validateAndDeserialize (json: string) : Result<LlmStep, AgentError> =
    let opts = EvaluationOptions(OutputFormat = OutputFormat.List)
    use doc = JsonDocument.Parse(json)
    let results = llmStepSchema.Evaluate(doc.RootElement, opts)

    if results.IsValid then
        try
            Ok(JsonSerializer.Deserialize<LlmStep>(json, jsonOptions))
        with ex ->
            Error(SchemaViolation $"deserialization failed after schema pass: {ex.Message}")
    else
        let errors =
            results.Details
            |> Seq.filter (fun d -> not d.IsValid && d.Errors <> null)
            |> Seq.collect (fun d -> d.Errors |> Seq.map (fun kvp -> $"{d.InstanceLocation}: {kvp.Value}"))
            |> String.concat "; "

        let detail =
            if System.String.IsNullOrWhiteSpace(errors) then
                "schema validation failed (no detail)"
            else
                errors

        Error(SchemaViolation detail)

// ── Public entry: extract then validate ──────────────────────────────────────

/// Full parse pipeline. Extracts a JSON object from any LLM output format
/// (bare JSON, prose-wrapped JSON, markdown-fenced JSON) then validates
/// the extracted JSON against llmStepSchema.
///
/// Returns:
///   Ok LlmStep                      on success (extraction + schema validation passed)
///   Error (InvalidJsonOutput raw)   when no JSON found across all 3 extraction stages
///   Error (SchemaViolation detail)  when JSON found but schema rejects it
///
/// The extraction happens BEFORE schema validation: extraction finds the first
/// valid JSON object string (any shape); schema validation then enforces that the
/// found JSON has the correct {thought, action, input} shape. This means:
///   - JSON with missing required fields → SchemaViolation (not InvalidJsonOutput)
///   - JSON with extra fields (additionalProperties:false) → SchemaViolation
///   - Completely non-JSON content → InvalidJsonOutput
///
/// PUBLIC: the ONLY public entry point of the pipeline. All internal
/// helpers (tryBareParse, tryParseJsonObject, extractFirstJsonObject,
/// tryFenceExtract, extractLlmStep, validateAndDeserialize) are `let private`
/// so callers cannot bypass schema validation by invoking the extractor directly.
let parseLlmResponse (content: string) : Result<LlmStep, AgentError> =
    match extractLlmStep content with
    | Error e -> Error e
    | Ok json ->
        // Pass the original extracted JSON to the schema validator.
        // Do NOT re-serialize a partially-deserialized LlmStep here — that would
        // drop extra fields before schema validation, hiding additionalProperties
        // violations. The validator must see the raw extracted JSON.
        validateAndDeserialize json
