module BlueCode.Cli.Adapters.QwenHttpClient

open System
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Core.Router
open BlueCode.Cli.Adapters.Json
open BlueCode.Cli.Adapters.LlmWire

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

/// Build the vLLM OpenAI-compat request body. Pins stream=false,
/// max_tokens=1024, presence_penalty=1.5 (Qwen model-card), and the
/// per-model temperature from Router.modelToTemperature (LLM-05).
/// No response_format field — Phase 2 relies on the extraction
/// pipeline (Plan 02-02) as the primary defense; json_object mode is
/// a Phase 5 optimization (02-RESEARCH.md Finding 2).
///
/// PRIVATE: internal helper of QwenHttpClient. Only `create` is public.
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

/// Raw HTTP POST returning the response body string or an AgentError.
/// Full error-mapping (HTTP 4xx/5xx body snippet, TaskCanceledException
/// disambiguation, UserCancelled vs timeout) lands in Plan 02-03.
/// Phase 02-01 only needs a compiling skeleton that catches the obvious
/// exceptions so the adapter cannot leak them.
///
/// PRIVATE: internal helper of QwenHttpClient. Only `create` is public.
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
            // TODO Plan 02-03: inspect IsSuccessStatusCode and map 4xx/5xx to LlmUnreachable with body snippet.
            let! responseJson = resp.Content.ReadAsStringAsync(ct)
            return Ok responseJson
        with
        | :? HttpRequestException as ex ->
            return Error (LlmUnreachable (url, ex.Message))
        | :? TaskCanceledException ->
            // TODO Plan 02-03: disambiguate user cancellation (ct match)
            //                  vs HttpClient.Timeout firing its own token.
            return Error (LlmUnreachable (url, "request cancelled or timed out"))
    }

/// QwenHttpClient implements ILlmClient.
/// Phase 02-01: compiles and reaches the HTTP endpoint, but returns
/// a stub SchemaViolation after the POST so callers do not attempt
/// to use the response. Plans 02-02 and 02-03 replace the stub with
/// real content extraction, pipeline parsing, schema validation,
/// spinner, and full error mapping.
///
/// PUBLIC: this is the only public entry point of the QwenHttpClient module.
let create () : ILlmClient =
    { new ILlmClient with
        member _.CompleteAsync messages model ct =
            task {
                let url  = model |> modelToEndpoint |> endpointToUrl
                let body = buildRequestBody messages model
                let! postResult = postAsync url body ct
                match postResult with
                | Error e -> return Error e
                | Ok _responseJson ->
                    // TODO Plan 02-02: extract choices[0].message.content and run extraction pipeline.
                    // TODO Plan 02-03: wrap in Spectre.Console spinner + full error mapping.
                    return Error (SchemaViolation "QwenHttpClient stub: pipeline wiring lands in Plan 02-02/02-03")
            }
    }
