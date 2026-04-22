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
let jsonOptions : JsonSerializerOptions =
    let opts = JsonSerializerOptions()
    opts.Converters.Add(JsonFSharpConverter(JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags(true)))
    opts

// ── Stage 1: bare parse ──────────────────────────────────────────────────────

/// Attempt to deserialize content directly as LlmStep without any pre-processing.
/// Returns Some step on success, None on any parse error (including malformed JSON,
/// missing fields, or wrong types).
///
/// PRIVATE: internal helper — only parseLlmResponse is the public API.
let private tryBareParse (content: string) : LlmStep option =
    try
        JsonSerializer.Deserialize<LlmStep>(content, jsonOptions) |> Some
    with _ -> None

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
    let mutable depth  = 0
    let mutable start  = -1
    let mutable found  : string option = None
    let mutable inString = false
    let mutable escape   = false
    let mutable i = 0
    while i < s.Length && found.IsNone do
        let c = s.[i]
        if escape then
            escape <- false
        elif inString then
            if   c = '\\' then escape <- true
            elif c = '"'  then inString <- false
        else
            match c with
            | '"' -> inString <- true
            | '{' ->
                if depth = 0 then start <- i
                depth <- depth + 1
            | '}' ->
                depth <- depth - 1
                if depth = 0 && start >= 0 then
                    found <- Some (s.Substring(start, i - start + 1))
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
///   1. Bare parse: content is already a valid LlmStep JSON string
///   2. Brace scan: extract first balanced JSON object from prose-wrapped content
///   3. Fence strip then re-parse: extract from markdown code block
///   4. ParseFailure: no JSON recoverable → Error (InvalidJsonOutput content)
///
/// Returns Ok step on the first stage that succeeds. Returns
/// Error (InvalidJsonOutput raw) only if all 3 extraction stages fail.
///
/// PRIVATE: callers MUST use parseLlmResponse (which layers schema
/// validation on top). Exposing extractLlmStep would let callers bypass
/// schema enforcement — exactly the drift we are defending against.
let private extractLlmStep (content: string) : Result<LlmStep, AgentError> =
    // Stage 1: direct bare parse
    match tryBareParse content with
    | Some step -> Ok step
    | None ->
    // Stage 2: brace-scan extract + parse
    match extractFirstJsonObject content |> Option.bind tryBareParse with
    | Some step -> Ok step
    | None ->
    // Stage 3: fence strip + brace-scan (fence content may itself be prose-wrapped)
    match tryFenceExtract content with
    | Some fenced ->
        match tryBareParse fenced with
        | Some step -> Ok step
        | None ->
            match extractFirstJsonObject fenced |> Option.bind tryBareParse with
            | Some step -> Ok step
            | None -> Error (InvalidJsonOutput content)
    | None -> Error (InvalidJsonOutput content)

// ── Schema validation (JsonSchema.Net 9.2.0) ─────────────────────────────────

/// draft-2020-12 schema for the LLM step wire format {thought, action, input}.
/// Enforced constraints:
///   - thought: non-empty string (minLength: 1)
///   - action:  one of the 5 known action values (enum check prevents drift)
///   - input:   MUST be an object (per-tool shape validated in Phase 3)
///   - additionalProperties: false (prevents Qwen confidence/reasoning fields)
///
/// PUBLIC: module-scope constant. No invariants to protect (immutable data).
let llmStepSchema : JsonSchema =
    JsonSchema.FromText("""
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
    """)

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
            Ok (JsonSerializer.Deserialize<LlmStep>(json, jsonOptions))
        with ex ->
            Error (SchemaViolation $"deserialization failed after schema pass: {ex.Message}")
    else
        let errors =
            results.Details
            |> Seq.filter (fun d -> not d.IsValid && d.Errors <> null)
            |> Seq.collect (fun d ->
                d.Errors
                |> Seq.map (fun kvp ->
                    $"{d.InstanceLocation}: {kvp.Value}"))
            |> String.concat "; "
        let detail =
            if System.String.IsNullOrWhiteSpace(errors)
            then "schema validation failed (no detail)"
            else errors
        Error (SchemaViolation detail)

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
/// The extraction happens BEFORE schema validation: schema runs on the
/// final extracted JSON string (no per-stage validation — see 02-RESEARCH
/// Finding 4). This matches LLM-02 + LLM-03 in REQUIREMENTS.md.
///
/// PUBLIC: the ONLY public entry point of the pipeline. All internal
/// helpers (tryBareParse, extractFirstJsonObject, tryFenceExtract,
/// extractLlmStep, validateAndDeserialize) are `let private` so callers
/// cannot bypass schema validation by invoking the extractor directly.
let parseLlmResponse (content: string) : Result<LlmStep, AgentError> =
    match extractLlmStep content with
    | Error e -> Error e
    | Ok step ->
        // Round-trip through JsonSerializer so the schema validator gets a string
        // it can parse. Slight overhead; acceptable for Phase 2. A future
        // optimization validates against the JsonElement from each stage
        // directly (02-RESEARCH.md Finding 4 optimization note).
        let json = JsonSerializer.Serialize(step, jsonOptions)
        validateAndDeserialize json
