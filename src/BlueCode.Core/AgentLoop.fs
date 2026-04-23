/// AgentLoop — pure recursive agent loop for BlueCode.Core.
///
/// Entry point: runSession. No Serilog, Spectre, or Cli-layer references.
/// Depends only on Domain, Router, ContextBuffer, Ports, FsToolkit.ErrorHandling,
/// and System.Text.Json (inbox on net10.0).
///
/// Known v1 limitation: Step.Thought is populated with Thought "[not captured in v1]".
/// Capturing the real thought would require amending ILlmClient.CompleteAsync to return
/// (Thought * LlmOutput) or a LlmStep type. Deferred to Phase 5+ per research
/// § Open Question 2 Option (d).
module BlueCode.Core.AgentLoop

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Core.Router

// ── Configuration ─────────────────────────────────────────────────────────────

/// Agent loop configuration. System prompt lives here (not inline constant)
/// so tests can inject a minimal prompt and production CompositionRoot can
/// inject a full prompt without recompiling AgentLoop.fs.
type AgentConfig =
    { MaxLoops: int // LOOP-01: default 5
      ContextCapacity: int // LOOP-06: default 3
      SystemPrompt: string
      ForcedModel: BlueCode.Core.Domain.Model option } // ROU-04: None = use Router

/// Loop guard state: (actionName, inputHash) -> occurrence count.
/// Threaded immutably through recursive calls; reset between turns.
type private LoopGuardState = Map<string * int, int>

// ── Tool dispatch ─────────────────────────────────────────────────────────────

/// Map LLM's action string (e.g., "read_file") + ToolInput._raw JSON text to a
/// Tool DU case. Returns Result<Tool, AgentError>. Pure sync.
let private dispatchTool (actionName: string) (input: ToolInput) : Result<Tool, AgentError> =
    let (ToolInput m) = input
    let raw = m |> Map.tryFind "_raw" |> Option.defaultValue "{}"

    try
        use doc = JsonDocument.Parse(raw)
        let root = doc.RootElement

        let tryStr (key: string) : string option =
            match root.TryGetProperty(key) with
            | true, el when el.ValueKind = JsonValueKind.String -> Some(el.GetString())
            | _ -> None

        let tryInt (key: string) : int option =
            match root.TryGetProperty(key) with
            | true, el when el.ValueKind = JsonValueKind.Number ->
                let ok, v = el.TryGetInt32()
                if ok then Some v else None
            | _ -> None

        let requireStr (key: string) : Result<string, AgentError> =
            match tryStr key with
            | Some s -> Ok s
            | None ->
                Error(SchemaViolation(sprintf "Tool '%s' input missing required string field '%s'" actionName key))

        match actionName with
        | "read_file" ->
            result {
                let! path = requireStr "path"
                let startL = tryInt "start_line"
                let endL = tryInt "end_line"

                let lineRange =
                    match startL, endL with
                    | Some s, Some e when s > 0 && e >= s -> Some(s, e)
                    | _ -> None

                return ReadFile(FilePath path, lineRange)
            }
        | "write_file" ->
            result {
                let! path = requireStr "path"
                let! content = requireStr "content"
                return WriteFile(FilePath path, content)
            }
        | "list_dir" ->
            result {
                let! path = requireStr "path"
                return ListDir(FilePath path, tryInt "depth")
            }
        | "run_shell" ->
            result {
                let! cmd = requireStr "command"
                let timeoutMs = tryInt "timeout_ms" |> Option.defaultValue 30000
                return RunShell(Command cmd, BlueCode.Core.Domain.Timeout timeoutMs)
            }
        | other -> Error(UnknownTool(ToolName other))
    with ex ->
        Error(SchemaViolation(sprintf "Tool input parse failed for '%s': %s" actionName ex.Message))

// ── Input hash (LOOP-04) ──────────────────────────────────────────────────────

/// Hash the ToolInput._raw JSON text. F# string.GetHashCode() is deterministic
/// WITHIN A PROCESS (sufficient for a turn-scoped guard). Whitespace-sensitive;
/// acceptable per research § Pitfall 6.
let private computeInputHash (input: ToolInput) : int =
    let (ToolInput m) = input
    m |> Map.tryFind "_raw" |> Option.defaultValue "" |> (fun s -> s.GetHashCode())

// ── Loop guard (LOOP-04) ──────────────────────────────────────────────────────

/// On the 3rd occurrence of the same (action, inputHash) return LoopGuardTripped.
/// Returns updated guard map on success.
let private checkLoopGuard
    (guard: LoopGuardState)
    (actionName: string)
    (inputHash: int)
    : Result<LoopGuardState, AgentError> =
    let key = (actionName, inputHash)
    let count = guard |> Map.tryFind key |> Option.defaultValue 0

    if count >= 2 then
        // count is 0-indexed occurrence number; count>=2 means this would be
        // the 3rd time — trip the guard.
        Error(LoopGuardTripped actionName)
    else
        Ok(guard |> Map.add key (count + 1))

// ── LLM call with retry (LOOP-05) ────────────────────────────────────────────

/// Two attempts. On first InvalidJsonOutput, build a correction User message
/// (truncate raw to 300 chars), append to messages, call LLM once more. If second
/// attempt also fails with InvalidJsonOutput, surface the ORIGINAL raw.
/// SchemaViolation is NOT retried (extractable but wrong shape).
let private callLlmWithRetry
    (client: ILlmClient)
    (messages: Message list)
    (model: Model)
    (ct: CancellationToken)
    : Task<Result<LlmOutput, AgentError>> =
    task {
        let! attempt1 = client.CompleteAsync messages model ct

        match attempt1 with
        | Ok output -> return Ok output
        | Error(InvalidJsonOutput raw) ->
            let snippet =
                if raw.Length > 300 then
                    raw.Substring(0, 300) + "..."
                else
                    raw

            let correction =
                { Role = User
                  Content =
                    sprintf
                        "[PARSE ERROR] Your previous response was not valid JSON. Required format: {\"thought\":\"...\",\"action\":\"...\",\"input\":{...}}. Raw response received: %s\n\nRespond with strict JSON only."
                        snippet }

            let messages2 = messages @ [ correction ]
            let! attempt2 = client.CompleteAsync messages2 model ct

            match attempt2 with
            | Ok output -> return Ok output
            | Error(InvalidJsonOutput _) -> return Error(InvalidJsonOutput raw)
            | Error other -> return Error other
        | Error other -> return Error other
    }

// ── Message building ──────────────────────────────────────────────────────────

/// Translates recentSteps (chronological) + system prompt + user input into
/// Message list per research § Pattern 10. FinalAnswer step emits only one
/// assistant message (no observation). ToolCall step emits an assistant +
/// observation pair.
let private buildMessages (systemPrompt: string) (userInput: string) (recentSteps: Step list) : Message list =
    let systemMsg =
        { Role = System
          Content = systemPrompt }

    let userMsg = { Role = User; Content = userInput }

    let stepMsgs =
        recentSteps
        |> List.collect (fun step ->
            let (Thought t) = step.Thought

            let assistantContent =
                match step.Action with
                | ToolCall(ToolName n, ToolInput m) ->
                    let raw = m |> Map.tryFind "_raw" |> Option.defaultValue "{}"
                    sprintf "{\"thought\":\"%s\",\"action\":\"%s\",\"input\":%s}" t n raw
                | FinalAnswer ans ->
                    sprintf "{\"thought\":\"%s\",\"action\":\"final\",\"input\":{\"answer\":\"%s\"}}" t ans

            let observation =
                match step.ToolResult with
                | None -> "[OBSERVATION]\nFinal answer produced."
                | Some(Success output) -> sprintf "[OBSERVATION]\n%s" output
                | Some(Failure(code, stderr)) -> sprintf "[TOOL ERROR]\nExit code: %d\nStderr: %s" code stderr
                | Some(SecurityDenied reason) -> sprintf "[TOOL DENIED]\n%s" reason
                | Some(PathEscapeBlocked path) -> sprintf "[PATH BLOCKED]\nAttempted: %s" path
                | Some(ToolResult.Timeout secs) -> sprintf "[TIMEOUT]\nShell timed out after %d seconds" secs

            [ { Role = Assistant
                Content = assistantContent }
              { Role = User; Content = observation } ])

    systemMsg :: userMsg :: stepMsgs

// ── Recursive agent loop (LOOP-01..05, OBS-04) ───────────────────────────────

/// State threaded as parameters. No mutation. loopN starts at 0; when
/// loopN >= config.MaxLoops return Error MaxLoopsExceeded (LOOP-02).
/// onStep callback invoked after every completed Step — enables 04-02/04-03
/// to write JSONL per-step (OBS-01).
let rec private runLoop
    (config: AgentConfig)
    (model: Model)
    (client: ILlmClient)
    (tools: IToolExecutor)
    (userInput: string)
    (ctx: ContextBuffer.ContextBuffer)
    (guard: LoopGuardState)
    (loopN: int)
    (steps: Step list)
    (onStep: Step -> unit)
    (ct: CancellationToken)
    : Task<Result<AgentResult, AgentError>> =
    task {
        if loopN >= config.MaxLoops then
            return Error MaxLoopsExceeded
        else
            let history = ContextBuffer.toList ctx |> List.rev // chronological
            let messages = buildMessages config.SystemPrompt userInput history
            let startedAt = DateTimeOffset.UtcNow

            let! llmResult = callLlmWithRetry client messages model ct

            match llmResult with
            | Error e -> return Error e
            | Ok(FinalAnswer answer) ->
                let endedAt = DateTimeOffset.UtcNow
                let durationMs = int64 (endedAt - startedAt).TotalMilliseconds

                let finalStep =
                    { StepNumber = loopN + 1
                      Thought = Thought "[not captured in v1]"
                      Action = FinalAnswer answer
                      ToolResult = None
                      Status = StepSuccess
                      ModelUsed = model
                      StartedAt = startedAt
                      EndedAt = endedAt
                      DurationMs = durationMs }

                onStep finalStep
                let allSteps = List.rev (finalStep :: steps)

                return
                    Ok
                        { FinalAnswer = answer
                          Steps = allSteps
                          LoopCount = loopN + 1
                          Model = model }
            | Ok(ToolCall(ToolName actionName, toolInput)) ->
                let inputHash = computeInputHash toolInput

                match checkLoopGuard guard actionName inputHash with
                | Error e -> return Error e
                | Ok guard' ->
                    match dispatchTool actionName toolInput with
                    | Error e -> return Error e
                    | Ok tool ->
                        let! toolRes = tools.ExecuteAsync tool ct
                        let endedAt = DateTimeOffset.UtcNow
                        let durationMs = int64 (endedAt - startedAt).TotalMilliseconds

                        match toolRes with
                        | Error e -> return Error e
                        | Ok tr ->
                            let status =
                                match tr with
                                | Success _ -> StepSuccess
                                | Failure _ -> StepFailed "tool failure"
                                | SecurityDenied _ -> StepFailed "security denied"
                                | PathEscapeBlocked _ -> StepFailed "path escape blocked"
                                | ToolResult.Timeout _ -> StepFailed "timeout"

                            let step =
                                { StepNumber = loopN + 1
                                  Thought = Thought "[not captured in v1]"
                                  Action = ToolCall(ToolName actionName, toolInput)
                                  ToolResult = Some tr
                                  Status = status
                                  ModelUsed = model
                                  StartedAt = startedAt
                                  EndedAt = endedAt
                                  DurationMs = durationMs }

                            onStep step
                            let ctx' = ContextBuffer.add step ctx
                            let steps' = step :: steps
                            return! runLoop config model client tools userInput ctx' guard' (loopN + 1) steps' onStep ct
    }

// ── Public entry point ────────────────────────────────────────────────────────

/// Drive a full agent turn. Creates an empty ContextBuffer and LoopGuardState,
/// routes the input through Router to pick a Model, and kicks off runLoop.
/// onStep is invoked exactly once per completed Step (both ToolCall and FinalAnswer).
/// The model is fixed at turn start (PITFALLS D-7 — no mid-turn switching).
let runSession
    (config: AgentConfig)
    (client: ILlmClient)
    (tools: IToolExecutor)
    (onStep: Step -> unit)
    (userInput: string)
    (ct: CancellationToken)
    : Task<Result<AgentResult, AgentError>> =
    let model =
        config.ForcedModel
        |> Option.defaultWith (fun () -> userInput |> classifyIntent |> intentToModel)

    let ctx = ContextBuffer.create config.ContextCapacity
    let guard = Map.empty: LoopGuardState
    runLoop config model client tools userInput ctx guard 0 [] onStep ct
