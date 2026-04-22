module BlueCode.Cli.Adapters.QwenHttpClient

open System
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Spectre.Console
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Core.Router
open BlueCode.Cli.Adapters.Json
open BlueCode.Cli.Adapters.LlmWire

// ── HttpClient singleton (from Plan 02-01) ──────────────────────────────────

/// Single HttpClient instance for the CLI process. Created once;
/// reused across all requests. Phase 4 CompositionRoot.fs will own
/// this; Phase 2 keeps it module-scope private for the smoke test.
/// 180s timeout covers 72B worst case (~60s) + generous margin.
let private httpClient : HttpClient =
    let c = new HttpClient()
    c.Timeout <- TimeSpan.FromSeconds(180.0)
    c

let private roleString : MessageRole -> string = function
    | System    -> "system"
    | User      -> "user"
    | Assistant -> "assistant"

// ── Request build (from Plan 02-01, unchanged — stays `let private`) ────────

/// Build the vLLM OpenAI-compat request body. Pins stream=false,
/// max_tokens=1024, presence_penalty=1.5 (Qwen model-card), and the
/// per-model temperature from Router.modelToTemperature (LLM-05).
/// No response_format field — Phase 2 relies on the extraction
/// pipeline (Plan 02-02) as the primary defense; json_object mode is
/// a Phase 5 optimization (02-RESEARCH.md Finding 2).
///
/// PRIVATE: internal helper of QwenHttpClient. Only `create` and `toLlmOutput` are public.
let private buildRequestBody (messages: Message list) (model: Model) : string =
    let msgArr =
        messages
        |> List.map (fun m -> {| role = roleString m.Role; content = m.Content |})
        |> List.toArray
    let req =
        {| model            = modelToName model
           messages         = msgArr
           temperature      = modelToTemperature model
           max_tokens       = 1024
           presence_penalty = 1.5
           stream           = false |}
    JsonSerializer.Serialize(req, jsonOptions)

// ── Full error mapping on POST ──────────────────────────────────────────────

/// POST with complete error mapping. Catches everything the HTTP stack
/// can throw and returns a typed AgentError via Result. No exception
/// propagates out (LLM-06, SC-04).
///
/// Error mapping table (02-RESEARCH.md Finding 9):
///   HttpRequestException                      -> LlmUnreachable url ex.Message
///   TaskCanceledException (ex.CancellationToken = ct)  -> UserCancelled
///   TaskCanceledException (any other token)   -> LlmUnreachable url "request timed out after 180s"
///   HTTP 4xx/5xx                              -> LlmUnreachable url "HTTP {code}: {body-snippet}"
///
/// PRIVATE: internal helper called only from CompleteAsync via withSpinner.
let private postAsync
    (url: string)
    (body: string)
    (ct: CancellationToken)
    : Task<Result<string, AgentError>>
    =
    task {
        use req = new HttpRequestMessage(HttpMethod.Post, url)
        req.Content <- new StringContent(body, Encoding.UTF8, "application/json")
        try
            use! resp = httpClient.SendAsync(req, ct)
            if not resp.IsSuccessStatusCode then
                let! errorBody = resp.Content.ReadAsStringAsync(ct)
                let snippet =
                    if errorBody.Length > 200 then errorBody.Substring(0, 200)
                    else errorBody
                let detail = sprintf "HTTP %d: %s" (int resp.StatusCode) snippet
                return Error (LlmUnreachable (url, detail))
            else
                let! responseJson = resp.Content.ReadAsStringAsync(ct)
                return Ok responseJson
        with
        | :? HttpRequestException as ex ->
            return Error (LlmUnreachable (url, ex.Message))
        | :? TaskCanceledException as ex when ex.CancellationToken = ct ->
            // User pressed Ctrl+C; the cancellation token we received
            // is the one that fired.
            return Error UserCancelled
        | :? TaskCanceledException ->
            // HttpClient.Timeout fires TaskCanceledException with a
            // DIFFERENT (internal) token. Map to LlmUnreachable with
            // a timeout detail so the caller can distinguish "I cancelled"
            // from "network/model was too slow".
            return Error (LlmUnreachable (url, "request timed out after 180s"))
    }

// ── OpenAI envelope extraction ──────────────────────────────────────────────

/// Pull choices[0].message.content out of the vLLM response envelope.
/// Any structural mismatch (missing choice, missing message, non-string
/// content) maps to LlmUnreachable with 'malformed response' — this is a
/// transport-layer failure, NOT a JSON content parse failure (which would
/// be InvalidJsonOutput / SchemaViolation from parseLlmResponse).
///
/// PRIVATE: internal envelope helper. Tests cover the happy path via the
/// smoke test and the error path via CompleteAsync integration.
let private extractContent (url: string) (responseJson: string) : Result<string, AgentError> =
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
        Error (LlmUnreachable (url, sprintf "malformed response: %s" ex.Message))

// ── LlmStep -> LlmOutput mapping ────────────────────────────────────────────

/// Map the schema-validated wire record to the Core domain DU.
/// The schema already verified action is one of the 5 enum values.
/// For "final" we pull input.answer; for tool actions we pass the raw
/// JSON text as a single-entry ToolInput map keyed "_raw" — Phase 3
/// will replace the _raw passthrough with per-tool shape parsing.
///
/// On missing/non-string `answer` for the "final" action we return
/// SchemaViolation (NOT InvalidJsonOutput). Rationale:
///   - The JSON schema in Plan 02-02 (`llmStepSchema`) cannot validate
///     action-specific input shapes — the action→input mapping is open
///     (input is constrained only to be an object; per-action shape is
///     Phase 3 / v1.x tightening). So missing-`answer` is a schema GAP,
///     not a raw JSON parse failure. InvalidJsonOutput is reserved for
///     "no JSON could be extracted from the content string at all"
///     (parseLlmResponse Stage 4 failure); by the time we're inside
///     toLlmOutput, the JSON already parsed and passed base-schema
///     validation. Calling this SchemaViolation preserves the invariant
///     that InvalidJsonOutput means "couldn't parse raw content" and
///     SchemaViolation means "parsed but shape is wrong".
///
/// PUBLIC: exposed so ToLlmOutputTests can invoke it with hand-built
/// LlmStep values (see tests/BlueCode.Tests/ToLlmOutputTests.fs).
/// All other helpers (buildRequestBody, postAsync, extractContent,
/// withSpinner, roleString) stay `let private`.
let toLlmOutput (step: LlmStep) : Result<LlmOutput, AgentError> =
    match step.action with
    | "final" ->
        match step.input.TryGetProperty("answer") with
        | true, v when v.ValueKind = JsonValueKind.String ->
            Ok (FinalAnswer (v.GetString()))
        | _ ->
            // SchemaViolation (not InvalidJsonOutput): the JSON schema
            // cannot validate action-specific input shapes (action→input
            // schema is open in v1). Missing-answer is a schema gap,
            // not a parse failure. See docblock above for full rationale.
            Error (SchemaViolation "final action input missing string 'answer' field")
    | toolName ->
        let raw = step.input.GetRawText()
        let ti = ToolInput (Map.ofList [ ("_raw", raw) ])
        Ok (ToolCall (ToolName toolName, ti))

// ── Spectre.Console spinner ─────────────────────────────────────────────────

/// Wrap a task-returning work function in the Spectre status spinner.
/// MUST only wrap the HTTP call — NOT parse/validate (those are microseconds
/// and should not show a spinner). Spectre auto-detects non-TTY (CI) and
/// silently no-ops; no special handling needed.
///
/// PRIVATE: internal helper called only from CompleteAsync.
let private withSpinner<'a>
    (label: string)
    (work: unit -> Task<'a>)
    : Task<'a>
    =
    AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("cyan"))
        .StartAsync(label, fun _ctx -> work ())

// ── Public factory: ILlmClient implementation ───────────────────────────────

/// Build a QwenHttpClient that implements ILlmClient. Replace the Plan 02-01
/// stub (which returned SchemaViolation after POST) with the fully-wired
/// pipeline:
///    withSpinner(HTTP POST)
///      -> extractContent (OpenAI envelope)
///      -> parseLlmResponse (extraction + schema validation from Plan 02-02)
///      -> toLlmOutput (LlmStep -> LlmOutput DU)
///
/// Only the spinner wraps the HTTP call; parse+validate happen after.
///
/// PUBLIC: the primary factory. Alongside `toLlmOutput` (also public for
/// unit testing), these are the only public exports of this module.
let create () : ILlmClient =
    { new ILlmClient with
        member _.CompleteAsync messages model ct =
            task {
                let url  = model |> modelToEndpoint |> endpointToUrl
                let body = buildRequestBody messages model
                let modelLabel =
                    match model with
                    | Qwen32B -> "32B"
                    | Qwen72B -> "72B"
                let label = sprintf "Thinking... [%s]" modelLabel

                // Spinner wraps HTTP ONLY. Parse + validate run after.
                let! postResult =
                    withSpinner label (fun () -> postAsync url body ct)

                match postResult with
                | Error e -> return Error e
                | Ok responseJson ->
                    match extractContent url responseJson with
                    | Error e -> return Error e
                    | Ok content ->
                        match parseLlmResponse content with
                        | Error e -> return Error e
                        | Ok step -> return toLlmOutput step
            }
    }
