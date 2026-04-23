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

// ── /v1/models probe result (Phase 6 REF-01 + REF-02) ─────────────────────

/// Bundles both fields extracted from GET /v1/models data[0].
/// ModelId becomes the value sent in the POST chat/completions "model" field.
/// MaxModelLen is retained for future per-port warning accuracy; v1.1 does not
/// surface it to AppComponents (stays at 8192 floor).
type ModelInfo =
    { ModelId: string
      MaxModelLen: int }

// ── HttpClient singleton (from Plan 02-01) ──────────────────────────────────

/// Single HttpClient instance for the CLI process. Created once;
/// reused across all requests. Phase 4 CompositionRoot.fs will own
/// this; Phase 2 keeps it module-scope private for the smoke test.
/// 180s timeout covers 72B worst case (~60s) + generous margin.
let private httpClient: HttpClient =
    let c = new HttpClient()
    c.Timeout <- TimeSpan.FromSeconds(180.0)
    c

let private roleString: MessageRole -> string =
    function
    | System -> "system"
    | User -> "user"
    | Assistant -> "assistant"

// ── Request build (from Plan 02-01, unchanged — stays `let private`) ────────

/// Build the vLLM OpenAI-compat request body. Pins stream=false,
/// max_tokens=1024, presence_penalty=1.5 (Qwen model-card), and the
/// per-model temperature from Router.modelToTemperature (LLM-05).
/// No response_format field — Phase 2 relies on the extraction
/// pipeline (Plan 02-02) as the primary defense; json_object mode is
/// a Phase 5 optimization (02-RESEARCH.md Finding 2).
///
/// modelId is now injected by the caller from the lazy ModelInfo probe in
/// `create()` (REF-01); the `model` field value is resolved at runtime via
/// GET /v1/models (no longer a Core hardcode — see 06-RESEARCH.md Q1 Option B).
///
/// PRIVATE: internal helper of QwenHttpClient. Only `create` and `toLlmOutput` are public.
let private buildRequestBody (messages: Message list) (model: Model) (modelId: string) : string =
    let msgArr =
        messages
        |> List.map (fun m ->
            {| role = roleString m.Role
               content = m.Content |})
        |> List.toArray

    let req =
        {| model = modelId
           messages = msgArr
           temperature = modelToTemperature model
           max_tokens = 1024
           presence_penalty = 1.5
           stream = false |}

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
let private postAsync (url: string) (body: string) (ct: CancellationToken) : Task<Result<string, AgentError>> =
    task {
        use req = new HttpRequestMessage(HttpMethod.Post, url)
        req.Content <- new StringContent(body, Encoding.UTF8, "application/json")

        try
            use! resp = httpClient.SendAsync(req, ct)

            if not resp.IsSuccessStatusCode then
                let! errorBody = resp.Content.ReadAsStringAsync(ct)

                let snippet =
                    if errorBody.Length > 200 then
                        errorBody.Substring(0, 200)
                    else
                        errorBody

                let detail = sprintf "HTTP %d: %s" (int resp.StatusCode) snippet
                return Error(LlmUnreachable(url, detail))
            else
                let! responseJson = resp.Content.ReadAsStringAsync(ct)
                return Ok responseJson
        with
        | :? HttpRequestException as ex -> return Error(LlmUnreachable(url, ex.Message))
        | :? TaskCanceledException as ex when ex.CancellationToken = ct ->
            // User pressed Ctrl+C; the cancellation token we received
            // is the one that fired.
            return Error UserCancelled
        | :? TaskCanceledException ->
            // HttpClient.Timeout fires TaskCanceledException with a
            // DIFFERENT (internal) token. Map to LlmUnreachable with
            // a timeout detail so the caller can distinguish "I cancelled"
            // from "network/model was too slow".
            return Error(LlmUnreachable(url, "request timed out after 180s"))
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
            doc.RootElement.GetProperty("choices").[0].GetProperty("message").GetProperty("content").GetString()

        Ok content
    with ex ->
        Error(LlmUnreachable(url, sprintf "malformed response: %s" ex.Message))

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
        | true, v when v.ValueKind = JsonValueKind.String -> Ok(FinalAnswer(v.GetString()))
        | _ ->
            // SchemaViolation (not InvalidJsonOutput): the JSON schema
            // cannot validate action-specific input shapes (action→input
            // schema is open in v1). Missing-answer is a schema gap,
            // not a parse failure. See docblock above for full rationale.
            Error(SchemaViolation "final action input missing string 'answer' field")
    | toolName ->
        let raw = step.input.GetRawText()
        let ti = ToolInput(Map.ofList [ ("_raw", raw) ])
        Ok(ToolCall(ToolName toolName, ti))

// ── Spectre.Console spinner ─────────────────────────────────────────────────

/// Wrap a task-returning work function in the Spectre status spinner.
/// MUST only wrap the HTTP call — NOT parse/validate (those are microseconds
/// and should not show a spinner). Spectre auto-detects non-TTY (CI) and
/// silently no-ops; no special handling needed.
///
/// CLI-05: label is updated every 500ms with elapsed seconds so the user
/// sees the spinner ticking (e.g., "Thinking... [32B] 3s"). The ticker
/// fires and forgets on the thread pool; it checks a CancellationToken
/// so it exits cleanly when the HTTP call completes.
///
/// PRIVATE: internal helper called only from CompleteAsync.
let private withSpinner<'a> (label: string) (work: unit -> Task<'a>) : Task<'a> =
    AnsiConsole
        .Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("cyan"))
        .StartAsync(
            label,
            fun ctx ->
                task {
                    // Background ticker: update the spinner label with elapsed seconds.
                    // CLI-05: "spinner + elapsed time" requirement.
                    use cts = new System.Threading.CancellationTokenSource()
                    let sw = System.Diagnostics.Stopwatch.StartNew()

                    let _ticker =
                        task {
                            try
                                while not cts.Token.IsCancellationRequested do
                                    do! Task.Delay(500, cts.Token)
                                    ctx.Status <- sprintf "%s %ds" label (int sw.Elapsed.TotalSeconds)
                            with :? System.OperationCanceledException ->
                                ()
                        }

                    try
                        return! work ()
                    finally
                        cts.Cancel()
                }
        )

// ── /v1/models probe (OBS-03) ──────────────────────────────────────────────

/// Parse data[0].id from a GET /v1/models response body.
/// Returns Some non-empty string on success; None on any structural
/// mismatch (missing data array, empty array, missing/null id, non-string id,
/// empty string id, JSON parse error).
///
/// PUBLIC for ModelsProbeTests unit testing (pure, no IO).
let tryParseModelId (json: string) : string option =
    try
        use doc = JsonDocument.Parse(json)
        let root = doc.RootElement

        match root.TryGetProperty("data") with
        | true, data when data.ValueKind = JsonValueKind.Array && data.GetArrayLength() > 0 ->
            let model0 = data.[0]

            match model0.TryGetProperty("id") with
            | true, el when el.ValueKind = JsonValueKind.String ->
                let s = el.GetString()
                if System.String.IsNullOrEmpty(s) then None else Some s
            | _ -> None
        | _ -> None
    with _ ->
        None

/// Parse max_model_len from the /v1/models JSON response body.
/// Returns Some int on success, None on any structural mismatch
/// (missing data array, missing/null field, non-positive value, parse error).
///
/// PUBLIC for ModelsProbeTests unit testing (pure JSON parsing logic).
let tryParseMaxModelLen (json: string) : int option =
    try
        use doc = JsonDocument.Parse(json)
        let root = doc.RootElement

        match root.TryGetProperty("data") with
        | true, data when data.ValueKind = JsonValueKind.Array && data.GetArrayLength() > 0 ->
            let model0 = data.[0]

            match model0.TryGetProperty("max_model_len") with
            | true, el when el.ValueKind = JsonValueKind.Number ->
                let ok, v = el.TryGetInt64()
                if ok && v > 0L then Some(int v) else None
            | _ -> None
        | _ -> None
    with _ ->
        None

/// GET http://127.0.0.1:8000/v1/models and extract data[0].max_model_len.
/// Returns the integer value on success; falls back to 8192 (conservative
/// floor per research § Pattern 5) on any error: HTTP failure, JSON parse
/// failure, missing/null field, non-positive value.
///
/// Fallback path logs a Warning to Serilog (stderr). This is intentional —
/// the probe failing is not a fatal error; it means the warning threshold
/// is based on 8192 rather than the real limit.
///
/// PUBLIC: called from CompositionRoot.bootstrapAsync.
let getMaxModelLenAsync (ct: CancellationToken) : Task<int> =
    task {
        let fallback = 8192

        try
            use! resp = httpClient.GetAsync("http://127.0.0.1:8000/v1/models", ct)

            if not resp.IsSuccessStatusCode then
                Serilog.Log.Warning(
                    "GET /v1/models returned {Status}; using fallback {Fallback}",
                    int resp.StatusCode,
                    fallback
                )

                return fallback
            else
                let! json = resp.Content.ReadAsStringAsync(ct)

                match tryParseMaxModelLen json with
                | Some v -> return v
                | None ->
                    Serilog.Log.Warning(
                        "max_model_len missing/invalid in /v1/models response; using fallback {Fallback}",
                        fallback
                    )

                    return fallback
        with ex ->
            Serilog.Log.Warning(ex, "GET /v1/models failed; using fallback {Fallback}", fallback)
            return fallback
    }

/// Probe http://<host>/v1/models once and extract both ModelId and MaxModelLen
/// from data[0]. Returns a ModelInfo record.
///
/// Fallback semantics (v1.0 parity for MaxModelLen; v1.1 new for ModelId):
///   - HTTP failure / non-2xx / JSON parse failure / missing fields -> MaxModelLen = 8192 fallback
///   - ModelId has NO graceful fallback: on parse miss we return ModelId = ""
///     which will cause vLLM to return HTTP 4xx on the subsequent POST, surfacing
///     as AgentError.LlmUnreachable. This is intentional — we do not want to
///     silently send an empty model id and pretend it works.
///
/// baseUrl MUST be the scheme+host+port prefix, e.g. "http://127.0.0.1:8000".
/// This function appends "/v1/models".
///
/// CancellationToken: pass CancellationToken.None from the Lazy-captured closure
/// so the probe is not user-cancellable (it is shared across all callers to the
/// same port and must not be cancelled by one caller's Ctrl+C — see 06-RESEARCH.md
/// § Pitfall 6).
let probeModelInfoAsync (baseUrl: string) (ct: CancellationToken) : Task<ModelInfo> =
    task {
        let fallback = { ModelId = ""; MaxModelLen = 8192 }

        try
            use! resp = httpClient.GetAsync(baseUrl + "/v1/models", ct)

            if not resp.IsSuccessStatusCode then
                Serilog.Log.Warning(
                    "GET {Url}/v1/models returned {Status}; using fallback ModelId='' MaxModelLen={MaxLen}",
                    baseUrl,
                    int resp.StatusCode,
                    fallback.MaxModelLen
                )

                return fallback
            else
                let! json = resp.Content.ReadAsStringAsync(ct)
                let id = tryParseModelId json |> Option.defaultValue ""
                let maxLen = tryParseMaxModelLen json |> Option.defaultValue 8192

                if id = "" then
                    Serilog.Log.Warning(
                        "GET {Url}/v1/models returned parseable JSON but data[0].id missing/empty; POST will likely 4xx",
                        baseUrl
                    )

                return { ModelId = id; MaxModelLen = maxLen }
        with ex ->
            Serilog.Log.Warning(ex, "GET {Url}/v1/models failed; using fallback", baseUrl)
            return fallback
    }

// ── Public factory: ILlmClient implementation ───────────────────────────────

/// Build a QwenHttpClient that implements ILlmClient. Replace the Plan 02-01
/// stub (which returned SchemaViolation after POST) with the fully-wired
/// pipeline:
///    probe (lazy, first call only per port) -> GET /v1/models -> ModelInfo
///    withSpinner(HTTP POST body using info.ModelId)
///      -> extractContent (OpenAI envelope)
///      -> parseLlmResponse (extraction + schema validation from Plan 02-02)
///      -> toLlmOutput (LlmStep -> LlmOutput DU)
///
/// Only the spinner wraps the HTTP call; parse+validate happen after.
///
/// PUBLIC: the primary factory. Alongside `toLlmOutput` (also public for
/// unit testing), these are the only public exports of this module.
let create () : ILlmClient =
    // Per-port lazy probe. Each Lazy fires its probeModelInfoAsync Task ONCE on
    // first .Value access; subsequent calls receive the same cached Task.
    // Default LazyThreadSafetyMode.ExecutionAndPublication guarantees single-probe
    // semantics under parallel CompleteAsync calls (future-proof; v1.0 REPL is
    // single-turn today). CancellationToken.None: probe is shared and MUST NOT be
    // cancelled by one caller's ct. See 06-RESEARCH.md § Pitfall 6.
    let probe8000: Lazy<Task<ModelInfo>> =
        Lazy<Task<ModelInfo>>(fun () -> probeModelInfoAsync "http://127.0.0.1:8000" CancellationToken.None)

    let probe8001: Lazy<Task<ModelInfo>> =
        Lazy<Task<ModelInfo>>(fun () -> probeModelInfoAsync "http://127.0.0.1:8001" CancellationToken.None)

    { new ILlmClient with
        member _.CompleteAsync messages model ct =
            task {
                let url = model |> modelToEndpoint |> endpointToUrl
                let probe = if model = Qwen32B then probe8000 else probe8001
                // First call to each port triggers GET /v1/models; subsequent calls
                // on the same port reuse the cached Task result.
                let! info = probe.Value
                let body = buildRequestBody messages model info.ModelId

                // --trace: log exact POST body before it leaves the adapter.
                // Gated by levelSwitch (default Information suppresses). Stays on
                // stderr (Serilog config), so no stdout contamination.
                Serilog.Log.Debug("POST {Url} body: {Body}", url, body)

                let modelLabel =
                    match model with
                    | Qwen32B -> "32B"
                    | Qwen72B -> "72B"

                // Escape brackets so Spectre does not parse "[32B]" as a markup
                // color tag. Spectre markup uses [[ / ]] as literal bracket escapes.
                let label = sprintf "Thinking... [[%s]]" modelLabel

                // Spinner wraps HTTP ONLY. Parse + validate run after.
                let! postResult = withSpinner label (fun () -> postAsync url body ct)

                match postResult with
                | Error e -> return Error e
                | Ok responseJson ->
                    Serilog.Log.Debug("Response {Url}: {Body}", url, responseJson)

                    match extractContent url responseJson with
                    | Error e -> return Error e
                    | Ok content ->
                        match parseLlmResponse content with
                        | Error e -> return Error e
                        | Ok step -> return toLlmOutput step
            } }
