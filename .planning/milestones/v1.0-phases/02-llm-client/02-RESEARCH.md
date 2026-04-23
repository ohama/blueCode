# Phase 2: LLM Client — Research

**Researched:** 2026-04-22
**Domain:** F# HTTP client for OpenAI-compat Qwen endpoint; multi-stage JSON extraction; JsonSchema.Net runtime validation; FSharp.SystemTextJson bootstrap; Spectre.Console spinner
**Confidence:** HIGH

---

## Summary

Phase 2 adds `QwenHttpClient.fs` (adapter for `ILlmClient`) and a JSON parsing pipeline to `BlueCode.Cli`. It is the highest-risk phase because it is where all Qwen-specific failure modes first appear. Prior research in `.planning/research/PITFALLS.md` covers these failure modes exhaustively (C-1 through C-8); this document does not duplicate them but does extend them with exact F# implementation patterns.

The phase introduces five new concerns: (1) bootstrapping `FSharp.SystemTextJson` with correct options so F# types round-trip through `System.Text.Json`; (2) the multi-stage JSON extraction pipeline that handles Qwen's prose-wrapped output; (3) `JsonSchema.Net 9.2.0` schema validation of the parsed `LlmStep` wire record; (4) mapping all failure modes to the typed `AgentError` DU already in `Domain.fs`; and (5) the `Spectre.Console` spinner around the HTTP wait only (not parse/validate).

**Primary recommendation:** Define `LlmStep` as a plain F# record in a new `LlmWire.fs` file in `BlueCode.Cli/Adapters/`. Create a shared `Json.fs` module in `BlueCode.Cli` that owns the `JsonSerializerOptions` singleton. Build and test the extraction pipeline as a pure function before wiring HTTP.

---

## Standard Stack — Phase 2 Additions

All packages below are added to `BlueCode.Cli` only (not Core). Core already has `FsToolkit.ErrorHandling 5.2.0`.

### New Packages Added in Phase 2

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| FSharp.SystemTextJson | 1.4.36 | F# DU + option + record JSON serialization | Only maintained library bridging F# types with `System.Text.Json`. Without it, DUs serialize as `{"Case":"...", "Fields":[...]}`. |
| JsonSchema.Net | 9.2.0 | Runtime JSON schema validation of LLM output | Pure `System.Text.Json`-native; validates `JsonElement` against draft-2020-12 schemas. No Newtonsoft dependency. |
| Spectre.Console | 0.55.2 | Terminal spinner during HTTP wait | De facto standard for .NET CLI rendering. `AnsiConsole.Status().StartAsync()` wraps async HTTP work cleanly. |

### Inbox (no NuGet needed)
- `System.Text.Json` — already inbox in net10.0
- `System.Net.Http.HttpClient` — already inbox in net10.0

**Installation (from solution root):**
```bash
dotnet add src/BlueCode.Cli/BlueCode.Cli.fsproj package FSharp.SystemTextJson --version 1.4.36
dotnet add src/BlueCode.Cli/BlueCode.Cli.fsproj package JsonSchema.Net --version 9.2.0
dotnet add src/BlueCode.Cli/BlueCode.Cli.fsproj package Spectre.Console --version 0.55.2
```

### Alternatives Considered

| Recommended | Alternative | Tradeoff |
|-------------|-------------|----------|
| `FSharp.SystemTextJson` + plain records for wire type | `[<CLIMutable>]` on records | `[<CLIMutable>]` adds parameterless ctor needed by some libs; correct for non-core types but confusing if applied to domain types |
| `JsonSchema.Net` | NJsonSchema | NJsonSchema pulls in Newtonsoft.Json; incompatible with pure STJ stack |
| `Spectre.Console` status spinner | Background `Task.Run` + `Console.Write` elapsed | Hand-rolling a spinner has ANSI code problems; Spectre handles terminal width, CI detection, and graceful non-TTY fallback |

---

## Architecture Patterns

### File Layout for Phase 2

Phase 2 adds files to `BlueCode.Cli` only:

```
src/BlueCode.Cli/
├── BlueCode.Cli.fsproj       # updated: add 3 packages + new compile entries
├── Adapters/
│   ├── Json.fs               # NEW: shared JsonSerializerOptions singleton + extraction pipeline
│   ├── LlmWire.fs            # NEW: LlmStep record (wire format from Qwen)
│   └── QwenHttpClient.fs     # NEW: ILlmClient implementation (HTTP POST + error mapping)
└── Program.fs                # untouched until Phase 4
```

F# compile order in `BlueCode.Cli.fsproj` must be:
```xml
<ItemGroup>
  <Compile Include="Adapters/Json.fs" />
  <Compile Include="Adapters/LlmWire.fs" />
  <Compile Include="Adapters/QwenHttpClient.fs" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

`Json.fs` must be first because `LlmWire.fs` references the options it defines.

---

## Research Finding 1: LlmStep Wire Record

### Definition

`LlmStep` is the intermediate parsed type for the LLM wire format `{thought, action, input}`. It is **not** a domain type — it lives in `BlueCode.Cli/Adapters/LlmWire.fs`, not `Domain.fs`.

```fsharp
// src/BlueCode.Cli/Adapters/LlmWire.fs
module BlueCode.Cli.Adapters.LlmWire

open System.Text.Json

/// Wire-format record parsed from the LLM's JSON response.
/// Intermediate type: not a domain type. Lives in the adapter layer.
///
/// Use plain F# record (not DU) because:
///   - DUs serialize as {"Case":"...", "Fields":[...]} by default
///   - The LLM emits {"thought":"...","action":"...","input":{...}}
///   - Records serialize/deserialize cleanly with FSharp.SystemTextJson
///
/// `input` is JsonElement (raw passthrough) because its shape varies per tool:
///   - read_file:  {"path": "..."}
///   - write_file: {"path": "...", "content": "..."}
///   - final:      {"answer": "..."}
///   Phase 2 does not parse `input` further — that is Phase 3 (tool dispatch).
type LlmStep = {
    thought : string
    action  : string        // "read_file" | "write_file" | "list_dir" | "run_shell" | "final"
    input   : JsonElement   // raw passthrough; validated by schema (must be object)
}
```

**Field type rationale:**
- `thought: string` — always a string; schema enforces this
- `action: string` — kept as string in the wire record; mapped to `LlmOutput` DU (toolcall vs final) in `QwenHttpClient` AFTER schema validation confirms it is one of the known enum values
- `input: JsonElement` — raw passthrough prevents premature parsing; the schema validates it is an object; Phase 3 tool dispatch re-parses it using the `action` value

### Mapping from LlmStep to LlmOutput

After `LlmStep` is parsed and schema-validated, `QwenHttpClient` converts it to the domain type `LlmOutput` (from `Domain.fs`):

```fsharp
// In QwenHttpClient.fs
let toLlmOutput (step: LlmStep) : Result<LlmOutput, AgentError> =
    match step.action with
    | "final" ->
        // Extract answer from input: {"answer": "..."}
        match step.input.TryGetProperty("answer") with
        | true, v -> Ok (FinalAnswer (v.GetString()))
        | false, _ -> Error (InvalidJsonOutput "final action missing 'answer' in input")
    | toolName ->
        // ToolInput is Map<string,string> — serialize input back to string map
        // (Phase 3 will do the real parsing; for now pass raw JSON string)
        let toolInput = ToolInput (Map.ofList [("_raw", step.input.GetRawText())])
        Ok (ToolCall (ToolName toolName, toolInput))
```

**Note:** Phase 3 replaces the `_raw` passthrough with proper per-tool parsing. Phase 2 only needs to verify the pipeline compiles and the `ILlmClient` interface is satisfied.

---

## Research Finding 2: OpenAI Chat Completions Wire Format

### Request Body (minimum required for vLLM)

```json
{
  "model": "Qwen/Qwen2.5-Coder-32B-Instruct",
  "messages": [
    { "role": "system", "content": "<system prompt>" },
    { "role": "user",   "content": "<user input>" }
  ],
  "temperature": 0.2,
  "max_tokens": 1024,
  "presence_penalty": 1.5,
  "stream": false
}
```

**Field notes:**
- `model` — vLLM DOES require the exact model name as served (visible via `GET /v1/models`). Phase 2 uses a placeholder string; the composition root in Phase 4 will wire the actual model name. `Router.modelToEndpoint` gives the URL; the model name string is an additional concern.
- `messages` — array of `{role, content}` objects. `ILlmClient.CompleteAsync` takes `messages: string list` per `Ports.fs`; Phase 2 must assemble these into the OpenAI `{role, content}` shape. **The exact shape of `messages` in `Ports.fs`** is `string list` (raw strings), meaning `QwenHttpClient` is responsible for wrapping them. Recommend: recheck Ports.fs and if `string list` is too loose, the Phase 2 task should propose a `Message list` (using the `Message` record from Domain.fs) for better type safety. See Open Question 1.
- `temperature` — required (see Finding 6 on temperature defaults)
- `max_tokens` — set to `1024` for Phase 2 smoke test; Phase 5 exposes this as config
- `presence_penalty` — vLLM **does** respect this (unlike Ollama which silently ignores it per PITFALLS C-5); set to `1.5` per Qwen model card recommendation
- `stream: false` — explicit; do not use streaming in v1 (PITFALLS C-3)

### Response Body

```json
{
  "id": "chatcmpl-...",
  "object": "chat.completion",
  "created": 1745000000,
  "model": "Qwen/Qwen2.5-Coder-32B-Instruct",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "<LLM output string — may be JSON or prose-wrapped JSON>"
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 150,
    "completion_tokens": 80,
    "total_tokens": 230
  }
}
```

**Extraction target:** `choices[0].message.content` — this is the string fed to the extraction pipeline.

**Response DTOs** (anonymous-record approach avoids a heavyweight class):

```fsharp
// In Json.fs or inline in QwenHttpClient.fs
// Use JsonDocument-based extraction rather than typed DTOs to avoid over-engineering
let extractContent (responseJson: string) : Result<string, AgentError> =
    try
        use doc = JsonDocument.Parse(responseJson)
        let content =
            doc.RootElement
                .GetProperty("choices").[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
        Ok content
    with ex ->
        Error (LlmUnreachable ("response", $"malformed response structure: {ex.Message}"))
```

Using `JsonDocument` property access rather than typed DTOs avoids the need to model the full OpenAI response schema. Avoids an extra pair of F# types for something that changes between vLLM versions.

### response_format: json_object Status

**Finding:** `response_format: {"type": "json_object"}` is supported by vLLM for recent Qwen models, but has had bugs in some versions (vLLM issue #11828). The current vLLM 0.8.x line supports it. However, it is NOT safe to rely on exclusively because:
1. Older vLLM versions had engine crashes with this option
2. Qwen 3.x structured output with reasoning mode requires explicit flag `--structured-outputs-config.enable_in_reasoning=True`
3. Even when supported, models can still produce valid JSON that fails schema validation

**Decision:** Do NOT include `response_format` in the Phase 2 request. The extraction pipeline is the primary defense. If the model endpoint supports it, it can be added as an optimization in Phase 5 after the pipeline is proven. This is consistent with PITFALLS C-1.

---

## Research Finding 3: Multi-Stage JSON Extraction Pipeline

The pipeline is a pure function: `string -> Result<LlmStep, AgentError>`. It has no HTTP dependency and must be in `Json.fs` (not in `QwenHttpClient.fs`) so it is testable in isolation.

### Stage 1: Bare Parse

```fsharp
let tryBareParse (content: string) : LlmStep option =
    try
        JsonSerializer.Deserialize<LlmStep>(content, jsonOptions) |> Some
    with _ -> None
```

Works when Qwen returns clean JSON with no prose framing.

### Stage 2: Brace Extraction (Stack-Based)

Regex cannot reliably extract nested JSON (regex can't count depth). Use a stack-based O(N) scan:

```fsharp
let extractFirstJsonObject (s: string) : string option =
    let mutable depth = 0
    let mutable start = -1
    let mutable i = 0
    let mutable found = None
    while i < s.Length && found.IsNone do
        match s.[i] with
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
```

**Edge cases handled:** nested objects, prose before/after the JSON, partial JSON at string boundaries. Does NOT handle JSON arrays as top-level — only objects (correct for our schema).

After extraction, attempt `tryBareParse` on the extracted substring.

### Stage 3: Fence Strip

```fsharp
open System.Text.RegularExpressions

// Matches: ```json\n{...}\n``` or ```\n{...}\n``` (with/without json tag)
let fencePattern = Regex(@"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.Compiled)

let tryFenceExtract (content: string) : string option =
    let m = fencePattern.Match(content)
    if m.Success then Some m.Groups.[1].Value
    else None
```

**Edge cases:** The pattern uses `[\s\S]*?` (non-greedy) to handle the first fence block only. If Qwen produces multiple code blocks, the first one is used — the agent loop's 2-attempt correction policy (PITFALLS D-6) handles the case where the first block is wrong.

**Unclosed fence:** The pattern requires a closing ` ``` ` — an unclosed fence falls through to ParseFailure. Do not attempt to recover from unclosed fences; it signals a badly truncated response (max_tokens too low).

After extraction, attempt `tryBareParse` on the extracted substring, then `extractFirstJsonObject` if bare fails.

### Stage 4: ParseFailure

```fsharp
Error (InvalidJsonOutput content)
```

`AgentError.InvalidJsonOutput of raw: string` is already defined in `Domain.fs`. The raw content is stored for logging. Phase 2 logs the raw content at DEBUG level via Serilog (Phase 4 will add Serilog; in Phase 2, `eprintfn` is acceptable for the smoke test).

### Full Pipeline

```fsharp
// src/BlueCode.Cli/Adapters/Json.fs
let extractLlmStep (content: string) : Result<LlmStep, AgentError> =
    // Stage 1: bare
    match tryBareParse content with
    | Some step -> Ok step
    | None ->
    // Stage 2: brace extraction
    match extractFirstJsonObject content |> Option.bind (fun s -> tryBareParse s) with
    | Some step -> Ok step
    | None ->
    // Stage 3: fence strip → brace extract → bare
    match tryFenceExtract content with
    | Some fenced ->
        match tryBareParse fenced with
        | Some step -> Ok step
        | None ->
            match extractFirstJsonObject fenced |> Option.bind (fun s -> tryBareParse s) with
            | Some step -> Ok step
            | None -> Error (InvalidJsonOutput content)
    | None -> Error (InvalidJsonOutput content)
```

**Schema validation placement:** Schema validation happens AFTER a successful parse, not after each stage. The pipeline's job is to find valid JSON; schema validation confirms the JSON has the right shape. See Finding 4.

---

## Research Finding 4: JsonSchema.Net 9.2.0 Integration

### Key API Facts for v9.2.0

1. The method is `schema.Evaluate()` (renamed from `Validate` in v9; `Validate` is now obsolete extension method — use `Evaluate`)
2. Default `OutputFormat.Flag` returns only `IsValid` — no error details
3. Use `OutputFormat.List` to get flat error collection with location info
4. `FromText(string)` is the correct way to build from inline JSON string literal

### Schema Definition

```fsharp
// In Json.fs — module-level let, built once at startup
open Json.Schema

let llmStepSchema =
    JsonSchema.FromText("""
    {
      "$schema": "https://json-schema.org/draft/2020-12",
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
```

**Why `additionalProperties: false`:** Prevents Qwen from emitting extra fields (e.g., `"confidence"`, `"reasoning"`) that would pass the required-fields check but indicate model drift.

### Validate-then-Deserialize Flow

```fsharp
let validateAndDeserialize (json: string) : Result<LlmStep, AgentError> =
    let options = EvaluationOptions(OutputFormat = OutputFormat.List)
    use doc = JsonDocument.Parse(json)
    let results = llmStepSchema.Evaluate(doc.RootElement, options)
    if results.IsValid then
        try
            Ok (JsonSerializer.Deserialize<LlmStep>(json, jsonOptions))
        with ex ->
            Error (SchemaViolation $"deserialization failed after schema pass: {ex.Message}")
    else
        // Collect all error messages into a single string for AgentError
        let errors =
            results.Details
            |> Seq.filter (fun d -> not d.IsValid && d.Errors <> null)
            |> Seq.collect (fun d -> d.Errors |> Seq.map (fun kvp -> $"{d.InstanceLocation}: {kvp.Value}"))
            |> String.concat "; "
        Error (SchemaViolation $"LLM output failed schema validation: {errors}")
```

**Error mapping:** `AgentError.SchemaViolation of detail: string` is already in `Domain.fs`. Use it for schema failures; use `AgentError.InvalidJsonOutput of raw: string` for extraction pipeline failures (no valid JSON found at all).

### Full Parse + Validate Chain

```fsharp
let parseLlmResponse (content: string) : Result<LlmStep, AgentError> =
    match extractLlmStep content with
    | Error e -> Error e
    | Ok step ->
        // Re-serialize to string for schema validation (we need a JSON string, not the record)
        // Alternative: validate the JsonElement from the extraction step
        // Better: integrate schema validation INTO the extraction stages
        validateAndDeserialize (JsonSerializer.Serialize(step, jsonOptions))
```

**Optimization note:** Round-tripping through serialize is slightly wasteful. A cleaner approach for Phase 2: in each extraction stage, attempt `JsonDocument.Parse` first (for the `JsonElement`), validate against schema, THEN deserialize. This avoids the double parse. However, the simpler approach (serialize after parse) is correct and clearer — optimize in Phase 5 if profiling shows it matters.

---

## Research Finding 5: FSharp.SystemTextJson Bootstrap

### Exact Setup Pattern (v1.4.36)

```fsharp
// src/BlueCode.Cli/Adapters/Json.fs
module BlueCode.Cli.Adapters.Json

open System.Text.Json
open System.Text.Json.Serialization
open FSharp.SystemTextJson  // provides JsonFSharpConverter, JsonFSharpOptions

/// Shared options for ALL JSON operations in the CLI adapter layer.
/// Built once; passed to every JsonSerializer call.
/// DO NOT create JsonSerializerOptions inline at call sites — it is expensive.
let jsonOptions : JsonSerializerOptions =
    JsonFSharpOptions.Default()
        .ToJsonSerializerOptions()
```

**Why `JsonFSharpOptions.Default().ToJsonSerializerOptions()` not `JsonSerializerOptions()` + `Converters.Add`:**

`JsonFSharpOptions.Default().ToJsonSerializerOptions()` builds options configured for F#-idiomatic behavior including:
- F# `option` types serialize as `null` / value (not `{"Case":"Some", "Fields":[value]}`)
- F# `list` serializes as JSON array
- F# `Map` serializes as JSON object
- DU cases get F#-friendly naming conventions

The `Converters.Add(JsonFSharpConverter())` approach also works but requires more manual configuration to get the same behavior.

### .NET 10 Strict Mode Interaction

`JsonSerializerOptions.Strict` (new in .NET 10) enables:
- `UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow` — rejects unknown fields
- `AllowDuplicateProperties = false` — rejects duplicate JSON keys
- `PropertyNameCaseInsensitive = false` — exact case match
- `RespectNullableAnnotations = true`
- `RespectRequiredConstructorParameters = true`

**The conflict:** `JsonFSharpOptions.Default().ToJsonSerializerOptions()` and `JsonSerializerOptions.Strict` produce separate options objects. They cannot be directly combined because `JsonSerializerOptions.Strict` is a static factory property returning a pre-configured, frozen-on-first-use options object.

**Recommendation for Phase 2:** Do NOT combine with `Strict`. The schema validation via `JsonSchema.Net` already enforces `additionalProperties: false` (rejects extra fields) and the required-field check. `Strict`'s case-sensitivity is a concern but Qwen consistently returns lowercase field names matching the schema. Adding `Strict` complexity is deferred to Phase 5 when we have a working end-to-end loop to test against.

If Strict behavior is desired: add `UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow` to the options manually after `ToJsonSerializerOptions()`:

```fsharp
let jsonOptions : JsonSerializerOptions =
    let o = JsonFSharpOptions.Default().ToJsonSerializerOptions()
    o.UnmappedMemberHandling <- JsonUnmappedMemberHandling.Disallow
    o  // NOT frozen; this mutation is safe before first use
```

### Expected Pitfalls with FSharp.SystemTextJson

- **`JsonElement` field:** `LlmStep.input: JsonElement` — `JsonElement` is a struct value type; it serializes/deserializes correctly without a converter. No special handling needed.
- **DU field in wire record:** There are NO DU fields in `LlmStep`. The `action: string` is kept as string intentionally. If a DU is added to the wire type, FSharp.SystemTextJson will serialize it with DU-specific format — the LLM won't produce that format. Keep wire types as records with primitive fields.
- **Options module scope:** Place `jsonOptions` in `Json.fs` as a module-level `let`. Never create it inside a function or per-request — `JsonSerializerOptions` construction is expensive (reflection-based converter discovery).

---

## Research Finding 6: Temperature Defaults (LLM-05)

### Where to Wire

Temperature belongs in `QwenHttpClient.buildRequest` where the HTTP request body is assembled. Pattern:

```fsharp
// In QwenHttpClient.fs
let private modelToTemperature : Model -> float = function
    | Qwen32B -> 0.2
    | Qwen72B -> 0.4
```

This is a private function in the adapter — not exported, not in `Domain.fs`, not in `Router.fs`. The temperature is a Qwen-specific implementation detail of the HTTP adapter, not a domain routing concept.

### Additional Parameters to Pin

Beyond temperature, pin these parameters in `buildRequest`:
- `presence_penalty: 1.5` — Qwen model card recommendation to prevent repetition loops (PITFALLS C-5). vLLM respects this; Ollama silently ignores it.
- `stream: false` — explicit non-streaming for v1 (PITFALLS C-3)
- `max_tokens: 1024` — prevent runaway generation; configurable in Phase 5

### LLM-05 Constraint

Per LLM-05: temperature is hardcoded and MUST NOT be exposed to the user. The Phase 5 Argu CLI `--temperature` flag must not be added. The `--model 72b` flag (changing model) is allowed because it affects routing, not temperature.

---

## Research Finding 7: Spectre.Console Spinner

### Exact F# Pattern

The C# signature is:
```csharp
Task<T> StartAsync(string status, Func<StatusContext, Task<T>> func)
```

In F# with `task {}` CE:

```fsharp
open Spectre.Console

let withSpinner (statusText: string) (work: unit -> Task<'a>) : Task<'a> =
    AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("cyan"))
        .StartAsync(statusText, fun _ctx -> work())
```

Usage in `QwenHttpClient`:
```fsharp
let! result =
    withSpinner "Thinking..." (fun () ->
        task { return! httpPostAsync messages model ct })
```

### CancellationToken Propagation

`StartAsync` does NOT accept a `CancellationToken` directly. Pass the token to the inner `work` function. When the token is cancelled, the inner task faults with `OperationCanceledException`, which propagates out of `StartAsync` normally.

```fsharp
let! result =
    withSpinner "Thinking..." (fun () ->
        task { return! httpPostAsync messages model ct })  // ct passed through closure
```

### Stream Separation: Spectre vs Serilog

Spectre.Console writes to **stdout** (`Console.Out`). Serilog (added in Phase 4) is configured to write to **stderr** (`Console.Error`). These are different file descriptors — they do not interfere.

**The only conflict** arises if Serilog is also configured to write to the same `Console.Out` stream that Spectre uses for the spinner. Prevention: configure `Serilog.Sinks.Console` to write to `stderr` using `standardErrorFromLevel: LogEventLevel.Verbose`.

Phase 2 does not add Serilog (that is Phase 4). In Phase 2, use `eprintfn` for debug output — it writes to stderr and does not interfere with the Spectre spinner on stdout.

### Spinner Scope

Spinner MUST wrap only the HTTP call, not the parse + validate step. Parse and validate are CPU-bound microseconds. The spinner is for the 3–30 second HTTP wait:

```fsharp
// CORRECT: spinner around HTTP only
let! rawContent =
    withSpinner $"Thinking... [{modelName}]" (fun () -> postToQwen request ct)
// parse + validate happen AFTER spinner stops (no spinner here)
let step = parseLlmResponse rawContent
```

### CI / Non-TTY Terminals

Spectre.Console auto-detects non-TTY environments (redirected stdout in CI) and suppresses ANSI codes. The `withSpinner` wrapper works correctly in CI — it just does nothing visual. No special handling needed.

---

## Research Finding 8: HttpClient Lifecycle, Timeout, and Disposal

### HttpClient as Singleton

For a CLI tool with no DI container, the correct pattern is a **single `HttpClient` instance per endpoint**, created at startup and reused for all requests. Using `new HttpClient()` per request causes port exhaustion.

```fsharp
// In CompositionRoot.fs (Phase 4) — created once at startup
let httpClient =
    let client = new HttpClient()
    client.Timeout <- TimeSpan.FromSeconds(180.0)  // 72B can take 30-60s; 180s safety margin
    client
```

For Phase 2 (smoke test only), create the `HttpClient` in the smoke test and dispose it after. For production wiring (Phase 4), it is created in the composition root and never disposed (CLR finalizer handles it on process exit).

**Alternative:** `SocketsHttpHandler` with `PooledConnectionLifetime` for DNS change resilience:
```fsharp
let handler = new SocketsHttpHandler(PooledConnectionLifetime = TimeSpan.FromMinutes(5.0))
let httpClient = new HttpClient(handler)
```
For localhost connections, DNS change resilience is irrelevant. Use plain `new HttpClient()`.

### Request Disposal

```fsharp
// In QwenHttpClient.fs — correct disposal pattern (PITFALLS B-3)
let postToQwen (url: string) (body: string) (ct: CancellationToken) : Task<Result<string, AgentError>> =
    task {
        use req = new HttpRequestMessage(HttpMethod.Post, url)
        req.Content <- new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        try
            use! resp = httpClient.SendAsync(req, ct)
            resp.EnsureSuccessStatusCode() |> ignore
            let! content = resp.Content.ReadAsStringAsync(ct)
            return Ok content
        with
        | :? HttpRequestException as ex ->
            return Error (LlmUnreachable (url, ex.Message))
        | :? TaskCanceledException as ex when ex.CancellationToken = ct ->
            return Error (UserCancelled)
        | :? TaskCanceledException ->
            // TaskCanceledException from HttpClient.Timeout fires with a different token
            return Error (LlmUnreachable (url, "request timed out"))
    }
```

**Key:** `use!` on `HttpResponseMessage` disposes the response (and releases the connection) when the `task {}` block exits — even on exception.

### Timeout Strategy

Set `HttpClient.Timeout` to 180 seconds (3 minutes). This covers:
- Qwen 72B worst case: ~60s for 200-token response on M-series chip
- Network hiccup to localhost (should be <1ms)
- vLLM startup queue time if model is cold

The `TimeoutPolicy` is on the client, not per-request. Per-request timeout requires `CancellationTokenSource.CreateLinkedTokenSource(ct, perRequestCts.Token)` — too complex for Phase 2; defer to Phase 5.

---

## Research Finding 9: Error Mapping Surface (LLM-06)

All exceptions must be caught inside `QwenHttpClient` and returned as `Result` values. No exception may escape into the agent loop.

| Exception / Condition | AgentError Case | Detail Carried |
|----------------------|-----------------|----------------|
| `HttpRequestException` (connection refused, unreachable) | `LlmUnreachable of endpoint: string * detail: string` | URL + exception message |
| `TaskCanceledException` with `CancellationToken = ct` (user pressed Ctrl+C) | `UserCancelled` | (no detail) |
| `TaskCanceledException` NOT from ct (HttpClient.Timeout fired) | `LlmUnreachable of endpoint * "request timed out"` | URL + "request timed out" |
| HTTP 4xx from vLLM (e.g., 400 context too long, 422 invalid model) | `LlmUnreachable of endpoint * "HTTP {statusCode}: {body}"` | status code + body snippet |
| HTTP 5xx from vLLM | `LlmUnreachable of endpoint * "HTTP {statusCode}"` | status code |
| JSON extraction pipeline: all 3 stages fail | `InvalidJsonOutput of raw: string` | raw LLM content for logging |
| Schema validation fails | `SchemaViolation of detail: string` | joined schema error messages |
| Malformed response structure (missing `choices[0].message.content`) | `LlmUnreachable of endpoint * "malformed response: ..."` | structural error message |

**Recommendation: Do NOT add a new `ExtractionStage` case to AgentError.** The existing `InvalidJsonOutput of raw: string` is sufficient for Phase 2. Which extraction stage failed is a debugging concern — include it as a prefix in the `raw` string: `$"[stage2-brace-extract-failed] {rawContent}"`. This avoids expanding the `AgentError` DU for diagnostic information that the agent loop doesn't need to branch on.

**SchemaViolation is already in Domain.fs** (added in Phase 1's expanded `AgentError`). Use it. Do not add a new `LlmSchemaError` case.

**HTTP status code handling:**

```fsharp
// After SendAsync, before reading content
if not resp.IsSuccessStatusCode then
    let! errorBody = resp.Content.ReadAsStringAsync(ct)
    let snippet = if errorBody.Length > 200 then errorBody.[..199] else errorBody
    return Error (LlmUnreachable (url, $"HTTP {int resp.StatusCode}: {snippet}"))
```

---

## Research Finding 10: Manual Smoke Test (Plan 02-03)

### Structure

The smoke test is NOT a unit test. It is a manual integration test that requires a live localhost:8000. It lives in `tests/BlueCode.Tests/SmokeTest.fs` (alongside `RouterTests.fs`), gated by an environment variable so it does not run in normal `dotnet test`.

```fsharp
// tests/BlueCode.Tests/SmokeTest.fs
module SmokeTest

open Expecto

[<Tests>]
let smokeTests =
    testList "Smoke" [
        testCase "QwenHttpClient round-trip" <| fun () ->
            // Guard: skip if endpoint not reachable
            let skipMsg = System.Environment.GetEnvironmentVariable("BLUECODE_SMOKE_TEST")
            if skipMsg <> "1" then
                Tests.skiptest "Set BLUECODE_SMOKE_TEST=1 to run smoke test against live localhost:8000"
            // ... actual test
    ]
```

Run with: `BLUECODE_SMOKE_TEST=1 dotnet run --project tests/BlueCode.Tests`

### Test Prompt

```json
{
  "messages": [
    {"role": "system", "content": "Respond ONLY in JSON with fields: thought, action, input. No prose. No markdown."},
    {"role": "user", "content": "List the files in the current directory."}
  ]
}
```

Expected: Qwen returns JSON with `action: "list_dir"` and `input: {"path": "."}`.

The smoke test verifies:
1. HTTP POST succeeds (no `LlmUnreachable`)
2. Content extraction succeeds (no `InvalidJsonOutput`)
3. Schema validation passes (no `SchemaViolation`)
4. Parsed `LlmStep.action` is a known action string
5. Spinner was visible (manual observation, not assertion)

### Endpoint Down Behavior

If localhost:8000 is unreachable, `HttpRequestException` maps to `LlmUnreachable`. The smoke test should `fail` with a clear message (not `skip`) when `BLUECODE_SMOKE_TEST=1` is set but the endpoint is down, because the user explicitly requested the smoke run. Skipping silently when the endpoint is down hides infrastructure problems.

---

## Research Finding 11: Unit Test Strategy

Per the ports-and-adapters pattern, `QwenHttpClient` is an adapter (impure). Do not mock `HttpMessageHandler` for unit tests — that is excessive ceremony for a CLI tool.

**Test what is pure; skip elaborate mocking of what is impure:**

| Component | Test Type | Why |
|-----------|-----------|-----|
| `extractFirstJsonObject` | Pure unit test | No IO; pure function |
| `tryFenceExtract` | Pure unit test | Pure regex; test edge cases (no fence, unclosed fence, multiple fences) |
| `extractLlmStep` (pipeline) | Pure unit test | Compose stages; test prose-wrapped, fence-wrapped, bare, failure inputs |
| `validateAndDeserialize` | Pure unit test (no network) | `JsonSchema.Net` works in-process; no IO needed |
| `toLlmOutput` | Pure unit test | Pure match function |
| `QwenHttpClient.CompleteAsync` | Manual smoke test only | Requires live endpoint |

The extraction pipeline tests are high-value and should cover at minimum:
1. Bare valid JSON → `Ok step`
2. Prose before JSON → `Ok step` (stage 2)
3. ` ```json...``` ` fenced JSON → `Ok step` (stage 3)
4. Completely unparseable content → `Error (InvalidJsonOutput _)`
5. Valid JSON but wrong schema (missing `action`) → `Error (SchemaViolation _)`
6. Valid JSON, valid schema, `action: "final"` → `Ok` step with correct action

These tests live in `tests/BlueCode.Tests/LlmPipelineTests.fs` (new file, added to test project compile order).

---

## Research Finding 12: ILlmClient Port — Clarification

Current `Ports.fs` defines:
```fsharp
type ILlmClient =
    abstract member CompleteAsync :
        messages : string list
     -> model    : Model
     -> ct       : CancellationToken
     -> Task<Result<LlmOutput, AgentError>>
```

**`messages: string list`** — this is a list of raw content strings, not `{role, content}` objects. The `QwenHttpClient` implementation must decide how to wrap them into the `[{role, content}]` array.

**Phase 2 decision:** `QwenHttpClient.CompleteAsync` should interpret `messages` as alternating `user`/`assistant` pairs, with the first element as the system prompt if the list has an odd number of items. This is the standard pattern for OpenAI chat history.

However, this is loose. A better type would be `Message list` using the `Message` record from `Domain.fs` (which has `Role: MessageRole` and `Content: string`). The `ILlmClient` interface in `Ports.fs` currently uses `string list` which loses role information.

**Open Question 1** (see below) asks the planner to decide: keep `string list` or change to `Message list`. If keeping `string list`, document the role interpretation convention in `Ports.fs`. If changing to `Message list`, that is a Phase 2 `Ports.fs` amendment — allowed since Phase 2 is implementing the interface for the first time.

**For Phase 2 planning:** assume `string list` interpretation is `[systemPrompt; userMessage]` in the simplest case. The implementation wraps them as:
```fsharp
[| {| role = "system"; content = messages.[0] |}
   {| role = "user";   content = messages.[1] |} |]
```

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| DU/option/list JSON serialization | Custom JsonConverter per type | `FSharp.SystemTextJson` | 50+ edge cases; already solved |
| JSON schema validation | Hand-rolled field checks | `JsonSchema.Net` | Schema language is expressive; hand-checks miss nested type checks |
| Terminal spinner | `Console.Write("\r")` + sleep loop | `Spectre.Console.Status` | ANSI code complexity; non-TTY detection; CI compatibility |
| LLM content extraction | Single `JsonSerializer.Deserialize` call | Multi-stage pipeline | Qwen produces prose-wrapped JSON in 5-15% of turns |
| HTTP status code retry | Retry loop inside `QwenHttpClient` | Return `Error` immediately | Agent loop in Phase 4 owns retry policy (D-6 in PITFALLS) |

---

## Common Pitfalls

### Pitfall 1: EvaluationResults Errors are Null in Flag Output Mode

**What goes wrong:** Call `schema.Evaluate(element)` without `EvaluationOptions`. Default output format is `OutputFormat.Flag` — `IsValid` is set correctly but `results.Details` is empty and `results.Errors` is null. Accessing `results.Details` for error messages produces nothing.

**How to avoid:** Always use `OutputFormat.List` when error details are needed:
```fsharp
let options = EvaluationOptions(OutputFormat = OutputFormat.List)
schema.Evaluate(element, options)
```

**Warning signs:** `results.Details` is an empty sequence even when `results.IsValid = false`.

---

### Pitfall 2: `use!` vs `use` on HttpResponseMessage

**What goes wrong:** Writing `use resp = do! ...` instead of `use! resp = ...`. The `use` keyword on a `Task<HttpResponseMessage>` creates a disposable wrapper around the Task object, not the response. The response is never disposed.

**How to avoid:** Inside `task {}` CE, use `use! resp = httpClient.SendAsync(req, ct)` (note the `!`).

---

### Pitfall 3: JsonFSharpConverter Creates Options Shared State

**What goes wrong:** `JsonFSharpOptions.Default().ToJsonSerializerOptions()` creates a new `JsonSerializerOptions` with converters and then freezes it on first use. Calling this inside a `task {}` per-request creates a new options object per call — expensive and leaks memory.

**How to avoid:** Create `jsonOptions` once at module scope in `Json.fs`. Never call `ToJsonSerializerOptions()` inside a function.

---

### Pitfall 4: Regex Fence Pattern with Greedy Match Spans Multiple Fences

**What goes wrong:** Pattern ` ```json(.*)``` ` with greedy `.` matches from the first ` ``` ` to the LAST ` ``` ` in the content, swallowing everything between multiple code blocks.

**How to avoid:** Use `[\s\S]*?` (non-greedy) and anchor to match the first fence pair only.

---

### Pitfall 5: TaskCanceledException vs HttpRequestException for Timeout

**What goes wrong:** `HttpClient.Timeout` fires as a `TaskCanceledException` (NOT `HttpRequestException`). Code that catches only `HttpRequestException` misses timeouts — the exception propagates as an unhandled fault.

**How to avoid:** Catch both:
```fsharp
| :? HttpRequestException as ex -> Error (LlmUnreachable (url, ex.Message))
| :? TaskCanceledException as ex when ex.CancellationToken = ct -> Error UserCancelled
| :? TaskCanceledException -> Error (LlmUnreachable (url, "request timed out"))
```

The `ex.CancellationToken = ct` check distinguishes user cancellation from timeout.

---

### Pitfall 6: JsonSchema.Net v9 uses Evaluate not Validate

**What goes wrong:** Calling `schema.Validate(element)` — this is the obsolete extension method from pre-v9. It still compiles (marked `[<Obsolete>]`) but silently calls `Evaluate` internally with default options. Using the obsolete API is not wrong but creates confusion in code review.

**How to avoid:** Always use `schema.Evaluate(element)` or `schema.Evaluate(element, options)` in new code.

---

## Code Examples

### Complete QwenHttpClient module skeleton

```fsharp
// src/BlueCode.Cli/Adapters/QwenHttpClient.fs
module BlueCode.Cli.Adapters.QwenHttpClient

open System
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Spectre.Console
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Cli.Adapters.Json
open BlueCode.Cli.Adapters.LlmWire

/// Single HttpClient instance — created once, reused for all requests.
/// Phase 4 moves this to CompositionRoot.fs. Phase 2 holds it here for smoke test.
let private httpClient =
    let c = new HttpClient()
    c.Timeout <- TimeSpan.FromSeconds(180.0)
    c

let private modelToTemperature : Model -> float = function
    | Qwen32B -> 0.2
    | Qwen72B -> 0.4

let private buildRequestBody (messages: string list) (model: Model) : string =
    // Simplest interpretation: messages = [systemPrompt; userContent]
    // Phase 4 will refine based on actual ContextBuffer.toMessages shape
    let messagesArr =
        messages
        |> List.mapi (fun i content ->
            let role = if i % 2 = 0 then "system" else "user"
            {| role = role; content = content |})
    let req =
        {| model = "qwen-model"   // Phase 4: wire actual model name from Router
           messages = messagesArr
           temperature = modelToTemperature model
           max_tokens = 1024
           presence_penalty = 1.5
           stream = false |}
    JsonSerializer.Serialize(req)

let private postAsync (url: string) (body: string) (ct: CancellationToken) : Task<Result<string, AgentError>> =
    task {
        use req = new HttpRequestMessage(HttpMethod.Post, url)
        req.Content <- new StringContent(body, Encoding.UTF8, "application/json")
        try
            use! resp = httpClient.SendAsync(req, ct)
            if not resp.IsSuccessStatusCode then
                let! errorBody = resp.Content.ReadAsStringAsync(ct)
                let snippet = if errorBody.Length > 200 then errorBody.[..199] else errorBody
                return Error (LlmUnreachable (url, $"HTTP {int resp.StatusCode}: {snippet}"))
            else
                let! responseJson = resp.Content.ReadAsStringAsync(ct)
                return Ok responseJson
        with
        | :? HttpRequestException as ex ->
            return Error (LlmUnreachable (url, ex.Message))
        | :? TaskCanceledException as ex when ex.CancellationToken = ct ->
            return Error UserCancelled
        | :? TaskCanceledException ->
            return Error (LlmUnreachable (url, "request timed out after 180s"))
    }

let private extractContent (responseJson: string) : Result<string, AgentError> =
    try
        use doc = JsonDocument.Parse(responseJson)
        let content =
            doc.RootElement
                .GetProperty("choices").[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
        Ok content
    with ex ->
        Error (LlmUnreachable ("response", $"malformed response: {ex.Message}"))

let private withSpinner (label: string) (work: unit -> Task<'a>) : Task<'a> =
    AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("cyan"))
        .StartAsync(label, fun _ -> work())

/// QwenHttpClient implements ILlmClient
let create () : ILlmClient =
    { new ILlmClient with
        member _.CompleteAsync messages model ct =
            taskResult {
                let url =
                    model |> BlueCode.Core.Router.modelToEndpoint
                          |> BlueCode.Core.Router.endpointToUrl
                let body = buildRequestBody messages model
                let modelName = match model with Qwen32B -> "32B" | Qwen72B -> "72B"
                let! responseJson =
                    withSpinner $"[{modelName}] Thinking..." (fun () -> postAsync url body ct)
                let! content = extractContent responseJson
                let! step = parseLlmResponse content
                return! toLlmOutput step |> Task.FromResult
            }
    }
```

### Extraction pipeline unit test skeleton

```fsharp
// tests/BlueCode.Tests/LlmPipelineTests.fs
module LlmPipelineTests

open Expecto
open BlueCode.Cli.Adapters.Json
open BlueCode.Core.Domain

[<Tests>]
let pipelineTests =
    testList "LlmPipeline" [

        test "bare JSON parses directly" {
            let json = """{"thought":"t","action":"list_dir","input":{"path":"."}}"""
            match extractLlmStep json with
            | Ok step -> Expect.equal step.action "list_dir" "action should be list_dir"
            | Error e -> failwithf "Expected Ok but got Error: %A" e
        }

        test "prose-wrapped JSON extracted via brace scan" {
            let content = """Sure! Here is the JSON response: {"thought":"t","action":"list_dir","input":{"path":"."}} Hope this helps!"""
            match extractLlmStep content with
            | Ok step -> Expect.equal step.action "list_dir" "should extract JSON from prose"
            | Error _ -> failtest "Should have extracted JSON from prose"
        }

        test "markdown-fenced JSON extracted via fence strip" {
            let content = "Here is the JSON:\n```json\n{\"thought\":\"t\",\"action\":\"read_file\",\"input\":{\"path\":\"main.fs\"}}\n```"
            match extractLlmStep content with
            | Ok step -> Expect.equal step.action "read_file" "should extract from fence"
            | Error _ -> failtest "Should have extracted JSON from fence"
        }

        test "completely unparseable content returns InvalidJsonOutput" {
            let content = "I cannot help with that request."
            match extractLlmStep content with
            | Error (InvalidJsonOutput _) -> ()
            | other -> failwithf "Expected InvalidJsonOutput but got: %A" other
        }

        test "valid JSON but invalid schema returns SchemaViolation" {
            // Missing required 'action' field
            let content = """{"thought":"t","input":{"path":"."}}"""
            match parseLlmResponse content with
            | Error (SchemaViolation _) -> ()
            | other -> failwithf "Expected SchemaViolation but got: %A" other
        }

        test "unknown action string passes schema but toLlmOutput makes ToolCall" {
            // Schema allows any string in action — enum check is the schema validator's job
            // But schema enum list is ["read_file","write_file","list_dir","run_shell","final"]
            // So unknown action should fail schema validation
            let content = """{"thought":"t","action":"unknown_tool","input":{}}"""
            match parseLlmResponse content with
            | Error (SchemaViolation _) -> ()  // expected: enum check fails
            | other -> failwithf "Expected SchemaViolation but got: %A" other
        }
    ]
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `schema.Validate()` | `schema.Evaluate()` | JsonSchema.Net v9 (2024) | `Validate` still compiles but is `[<Obsolete>]` |
| Manual `JsonFSharpConverter()` constructor | `JsonFSharpOptions.Default().ToJsonSerializerOptions()` | FSharp.SystemTextJson v1.x | New API is more ergonomic and configurable |
| `async {}` CE for HTTP | `task {}` CE | F# 6 / .NET 6 (2021) | `task {}` is idiomatic for .NET-interop code; `async {}` requires bridge adapters |
| Streaming response by default | Non-streaming for v1 | Architecture decision | Avoids SSE parser complexity; spinner covers UX gap |
| `response_format: json_object` | Extraction pipeline as primary defense | Qwen-specific | `json_object` unreliable with Qwen 3.x + reasoning mode |

**Deprecated/outdated:**
- `JsonSchema.Validate()` extension method: obsolete in v9, use `Evaluate()`
- Per-request `new JsonSerializerOptions()`: creates expensive objects; use module-level singleton
- `OpenAI` NuGet package for local endpoints: tied to cloud API; adds auth/retry that conflicts with localhost

---

## Open Questions

1. **`messages: string list` in ILlmClient interface**
   - What we know: Current `Ports.fs` takes `messages: string list`. This is too loose — it loses role information.
   - What's unclear: Should Phase 2 amend `Ports.fs` to use `Message list` (the `Message` record already in `Domain.fs`), or keep `string list` and document an interpretation convention?
   - Recommendation: **Change to `Message list`**. The `Message` record (`Role: MessageRole; Content: string`) is already defined. Phase 2 is the first consumer of this interface; changing it now costs nothing and prevents ambiguity in Phase 4's agent loop. The planner should include a task in Plan 02-01 to update `Ports.fs` before implementing `QwenHttpClient`.

2. **Model name string for vLLM request body**
   - What we know: vLLM requires the exact model name (e.g., `"Qwen/Qwen2.5-Coder-32B-Instruct"`) in the request body. The `Model` DU in `Domain.fs` has `Qwen32B | Qwen72B` but no model name string.
   - What's unclear: Where does the model name string live? Options: (a) hardcode in `QwenHttpClient.fs` as a private lookup, (b) add to `Router.fs` as `modelToName: Model -> string`, (c) make it a config value.
   - Recommendation: **Add `modelToName: Model -> string` to `Router.fs`** as a private or internal function. This keeps model-routing metadata in one place (Router.fs already has `modelToEndpoint` and `endpointToUrl`). Phase 4 can promote it to public if the composition root needs it.

3. **Serilog logging in Phase 2 smoke test**
   - What we know: Serilog is Phase 4 scope. Phase 2 needs some debug output during smoke test.
   - Recommendation: Use `eprintfn` for Phase 2 debug output. Add a `TODO Phase 4: replace eprintfn with Serilog` comment. This avoids adding Serilog to the Cli project in Phase 2 when Phase 4 will do it properly.

---

## Sources

### Primary (HIGH confidence)
- `.planning/research/PITFALLS.md` — C-1 (Qwen output drift), C-3 (SSE format), C-4 (latency UX), C-5 (temperature), B-1 (DU serialization), B-2 (task vs async), B-3 (disposal), B-7 (exception in CE), D-6 (parse retry policy)
- `.planning/research/STACK.md` — FSharp.SystemTextJson 1.4.36, JsonSchema.Net 9.2.0, Spectre.Console 0.55.2 versions and usage patterns
- `.planning/research/ARCHITECTURE.md` — QwenHttpClient adapter role, ILlmClient port shape, disposal patterns
- `/Users/ohama/projs/blueCode/src/BlueCode.Core/Domain.fs` — exact `AgentError` cases, `LlmOutput` DU, `Model` DU, `Message` record — read directly from Phase 1 implementation
- `/Users/ohama/projs/blueCode/src/BlueCode.Core/Ports.fs` — exact `ILlmClient` signature (`messages: string list`)
- `/Users/ohama/projs/blueCode/src/BlueCode.Core/Router.fs` — `endpointToUrl` function for URL resolution
- [FSharp.SystemTextJson docs/Using.md](https://github.com/Tarmil/FSharp.SystemTextJson/blob/master/docs/Using.md) — `JsonFSharpOptions.Default().ToJsonSerializerOptions()` pattern (HIGH)
- [JsonSchema.Net basics](https://docs.json-everything.net/schema/basics/) — `Evaluate()`, `EvaluationOptions`, `OutputFormat.List` (HIGH)
- [JsonSchema.Net EvaluationResults API](https://docs.json-everything.net/api/JsonSchema.Net/EvaluationResults/) — `IsValid`, `Details`, `Errors` properties (HIGH)
- [Spectre.Console Status docs](https://spectreconsole.net/console/live/status) — `StartAsync(string, Func<StatusContext, Task<T>>)` signature (HIGH)

### Secondary (MEDIUM confidence)
- [JsonSchema.Net v9.x API rename: Validate → Evaluate](https://docs.json-everything.net/rn-json-schema/) — confirmed via changelog search (MEDIUM)
- [vLLM structured outputs docs](https://docs.vllm.ai/en/latest/features/structured_outputs/) — `response_format: json_object` status and Qwen 3.x caveats (MEDIUM — version-specific, validate against local vLLM version at smoke test time)
- [HttpClient guidelines for .NET](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines) — singleton HttpClient pattern for non-DI-container apps (HIGH)
- [JsonSchema.Net validation in F# gist](https://gist.github.com/laat/f91ec60d20544d86738c0e668f102c83) — F#-specific example confirming `FromText` and `Validate`/`Evaluate` patterns (MEDIUM)

### Tertiary (LOW confidence)
- [vLLM OpenAI-compatible server docs](https://docs.vllm.ai/en/stable/serving/openai_compatible_server/) — request/response schema shape; exact fields confirmed via OpenAI API spec cross-reference (MEDIUM)

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all packages verified; versions pinned in prior STACK.md
- Architecture: HIGH — builds directly on Phase 1 output; ILlmClient interface inspected
- Extraction pipeline: HIGH — algorithm is deterministic and well-understood; tested patterns
- JsonSchema.Net v9 API: HIGH — confirmed `Evaluate()` rename; OutputFormat.List behavior
- vLLM wire format: MEDIUM — response format confirmed via OpenAI spec; local vLLM behavior may vary slightly by version
- Spectre.Console spinner: HIGH — `StartAsync` API confirmed from official docs

**Research date:** 2026-04-22
**Valid until:** 2026-05-22 (stable libraries; Spectre and JsonSchema.Net had April 2026 releases)
