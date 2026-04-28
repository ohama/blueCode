/// AgentLoop — pure recursive agent loop for BlueCode.Core.
///
/// Entry point: runSession. No Serilog, Spectre, or Cli-layer references.
/// Depends only on Domain, Router, ContextBuffer, Ports, FsToolkit.ErrorHandling,
/// and System.Text.Json (inbox on net10.0).
module BlueCode.Core.AgentLoop

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Core.Router
open BlueCode.Core.PlanValidator

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
                    | Some s, None when s > 0 -> Some(s, s + 99)   // 100-line default window; Int32 overflow falls through readFileImpl invalid-range guard (RESEARCH.md F#-note 3)
                    | None, Some e when e > 0 -> Some(1, e)        // bounded read from start
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
        | "edit_file" ->
            result {
                let! path   = requireStr "path"
                let! oldStr = requireStr "old_string"
                let! newStr = requireStr "new_string"
                return EditFile(FilePath path, oldStr, newStr)
            }
        | "glob_search" ->
            result {
                let! pattern = requireStr "pattern"
                let searchPath = tryStr "path" |> Option.map FilePath
                return GlobSearch(pattern, searchPath)
            }
        | "grep_search" ->
            result {
                let! pattern = requireStr "pattern"
                let searchPath = tryStr "path" |> Option.map FilePath
                let fileGlob   = tryStr "file_glob"
                return GrepSearch(pattern, searchPath, fileGlob)
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
    : Task<Result<LlmResponse, AgentError>> =
    task {
        let! attempt1 = client.CompleteAsync messages model ct

        match attempt1 with
        | Ok response -> return Ok response
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
            | Ok response -> return Ok response
            | Error(InvalidJsonOutput _) -> return Error(InvalidJsonOutput raw)
            | Error other -> return Error other
        | Error other -> return Error other
    }

// ── Message building ──────────────────────────────────────────────────────────

/// Translates recentSteps (chronological) + system prompt + user input into
/// Message list per research § Pattern 10. FinalAnswer step emits only one
/// assistant message (no observation). ToolCall step emits an assistant +
/// observation pair.
///
/// lastEditPath: when Some path, appends a User-role constraint message at the
/// END of the message list (after the user prompt and step history) directing the
/// model away from write_file/read_file on the already-edited path. This message
/// appears AFTER the user turn so it post-dates and overrides any user-prompt tool
/// instruction (loop-injection Option A, Plan 09.1-05).
///
/// lastReadHint: when Some (path, status) where status is "truncated" or
/// "out-of-range", appends a User-role [POST-READ HINT] message at the END
/// of the message list guiding the model toward a smaller window or a valid
/// start_line. Extends the 09.1-05 loop-injection primitive (Plan 11-01).
let private buildMessages (systemPrompt: string) (userInput: string) (recentSteps: Step list) (lastEditPath: string option) (lastReadHint: (string * string) option) : Message list =
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
                | Plan _ ->
                    // Plan steps are not historicized as past assistant turns.
                    // Phase 16 wires plan-mode display + approval gate; for now,
                    // emit an empty assistant message so message-list shape is preserved.
                    "{}"

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

    let baseMsgs = systemMsg :: userMsg :: stepMsgs

    let withEdit =
        match lastEditPath with
        | Some path ->
            let constraintMsg =
                // Role = User per Phase 17-02 + Phase 20-03 probe (2026-04-27) — 122B rejected
                // mid-conversation System messages with HTTP 404 ("System message must be at
                // the beginning."). The authority signal is carried by the [POST-EDIT CONSTRAINT]
                // text marker, not the role. See 20-03-PROBE-OUTPUT.md for evidence.
                { Role = User
                  Content =
                    sprintf
                        "[POST-EDIT CONSTRAINT] You just successfully edited %s. The edit is already persisted. Your next action MUST be either `final` (preferred) or `edit_file` on a different concern. Do NOT call `write_file` on `%s` — it is redundant. Do NOT call `read_file` on `%s` to verify — `edit_file` already confirmed the change. This constraint is mandatory regardless of any earlier user instruction."
                        path path path }
            baseMsgs @ [ constraintMsg ]
        | None -> baseMsgs

    match lastReadHint with
    | Some (path, "truncated") ->
        withEdit @ [
            // Role = User per Phase 17-02 + Phase 20-03 probe (2026-04-27) — 122B rejected
            // mid-conversation System messages with HTTP 404. The authority signal is carried
            // by the [POST-READ HINT] text marker, not the role. See 20-03-PROBE-OUTPUT.md.
            { Role = User
              Content =
                sprintf "[POST-READ HINT] The previous read_file on %s returned truncated content (clipped to 2000 chars). Pick a smaller window — set end_line - start_line < 50 — and read again to get unclipped content." path }
        ]
    | Some (path, "out-of-range") ->
        withEdit @ [
            // Role = User per Phase 17-02 + Phase 20-03 probe (2026-04-27) — 122B rejected
            // mid-conversation System messages with HTTP 404. The authority signal is carried
            // by the [POST-READ HINT] text marker, not the role. See 20-03-PROBE-OUTPUT.md.
            { Role = User
              Content =
                sprintf "[POST-READ HINT] The previous read_file on %s returned out-of-range (start_line > total_lines). The header reported total_lines; choose a start_line <= total_lines and read again." path }
        ]
    | _ -> withEdit

// ── Recursive agent loop (LOOP-01..05, OBS-04) ───────────────────────────────

/// State threaded as parameters. No mutation. loopN starts at 0; when
/// loopN >= config.MaxLoops return Error MaxLoopsExceeded (LOOP-02).
/// onStep callback invoked after every completed Step — enables 04-02/04-03
/// to write JSONL per-step (OBS-01).
/// lastEditPath: Some path when the immediately preceding step was a successful
/// EditFile on that path; None otherwise. Used by buildMessages to inject a
/// post-edit constraint message (Plan 09.1-05 loop-injection Option A).
/// lastReadHint: Some (path, status) when the immediately preceding step was a
/// read_file whose Success header reported "truncated" or "out-of-range"; None
/// otherwise. Used by buildMessages to inject a post-read hint message
/// (Plan 11-01, extending 09.1-05's loop-injection primitive).
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
    (lastEditPath: string option)
    (lastReadHint: (string * string) option)
    (onStep: Step -> unit)
    (ct: CancellationToken)
    : Task<Result<AgentResult, AgentError>> =
    task {
        if loopN >= config.MaxLoops then
            return Error MaxLoopsExceeded
        else
            let history = ContextBuffer.toList ctx |> List.rev // chronological
            let messages = buildMessages config.SystemPrompt userInput history lastEditPath lastReadHint
            let startedAt = DateTimeOffset.UtcNow

            let! llmResult = callLlmWithRetry client messages model ct

            match llmResult with
            | Error e -> return Error e
            | Ok { Thought = thought; Output = FinalAnswer answer } ->
                let endedAt = DateTimeOffset.UtcNow
                let durationMs = int64 (endedAt - startedAt).TotalMilliseconds

                let finalStep =
                    { StepNumber = loopN + 1
                      Thought = thought
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
            | Ok { Thought = thought; Output = ToolCall(ToolName actionName, toolInput) } ->
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
                                  Thought = thought
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
                            let lastEditPath' =
                                match tool, tr with
                                | EditFile (FilePath p, _, _), Success _ -> Some p
                                | _ -> None
                            let lastReadHint' =
                                match tool, tr with
                                | ReadFile (FilePath p, _), Success payload ->
                                    // payload starts with "[file: <path>, lines X-Y of Z, <status>]\n..."
                                    // Cheapest reliable check: substring search on the FIRST line only.
                                    let firstLine =
                                        let nl = payload.IndexOf('\n')
                                        if nl > 0 then payload.Substring(0, nl) else payload
                                    if firstLine.Contains(", truncated]") then Some (p, "truncated")
                                    elif firstLine.Contains(", out-of-range]") then Some (p, "out-of-range")
                                    else None
                                | _ -> None
                            return! runLoop config model client tools userInput ctx' guard' (loopN + 1) steps' lastEditPath' lastReadHint' onStep ct
            | Ok { Output = Plan _ } ->
                // Phase 14: Plan variant exists in Domain.fs but is NOT a valid
                // mid-loop output. Phase 16 wires plan-mode handling at the runSession
                // entry point; the Cli adapter only emits Plan when --plan is set.
                // Receiving Plan here means the LLM emitted plan JSON without the
                // plan-mode flag — surface as PlanInvalid.
                return Error(PlanInvalid "Plan output received outside plan-mode")
    }

// ── Public entry point ────────────────────────────────────────────────────────

/// Drive a full agent turn. Creates an empty ContextBuffer and LoopGuardState,
/// routes the input through Router to pick a Model, and kicks off runLoop.
/// onStep is invoked exactly once per completed Step (both ToolCall and FinalAnswer).
/// The model is fixed at turn start (PITFALLS D-7 — no mid-turn switching).
///
/// priorSteps: chronological steps from earlier turns in the same Session.
/// They are replayed into the ContextBuffer before the loop begins so
/// buildMessages emits them as assistant+observation message pairs ahead
/// of the current user prompt. The caller (Repl.runMultiTurn) accumulates
/// returned AgentResult.Steps onto its Session.Steps for the next turn.
let runSession
    (config: AgentConfig)
    (client: ILlmClient)
    (tools: IToolExecutor)
    (onStep: Step -> unit)
    (priorSteps: Step list)   // NEW: chronological steps from prior turns in the same session
    (userInput: string)
    (ct: CancellationToken)
    : Task<Result<AgentResult, AgentError>> =
    let model =
        config.ForcedModel
        |> Option.defaultWith (fun () -> userInput |> classifyIntent |> intentToModel)

    let ctx0 = ContextBuffer.create config.ContextCapacity
    // Replay prior steps so buildMessages emits them as assistant/observation pairs.
    // ContextBuffer.add returns a new buffer; fold to apply each prior step in order.
    let ctx = priorSteps |> List.fold (fun b s -> ContextBuffer.add s b) ctx0
    let guard = Map.empty: LoopGuardState
    runLoop config model client tools userInput ctx guard 0 [] None None onStep ct

// ── Plan-mode entry point (Phase 16-01) ──────────────────────────────────────

/// Plan-mode entry point. Drives ONE LLM call (with up to 1 retry on parse/validation failure)
/// to obtain a validated Plan. Does NOT execute tool steps — the caller (PlanGate in Phase 16-02)
/// presents the plan to the user for accept/reject/edit/quit before any execution.
///
/// Retry path:
///   Attempt 1: build messages -> call LLM -> parse -> validate.
///     On Ok plan: return Ok plan.
///     On Error (InvalidJsonOutput | SchemaViolation _ | PlanInvalid _): build correction
///       message ([PLAN PARSE ERROR] or [PLAN INVALID] with truncated detail), append, retry.
///     On any other Error: return immediately (LlmUnreachable etc. — not retryable here).
///   Attempt 2: same parse/validate path.
///     On Ok plan: return Ok plan.
///     On Error: return that error to caller.
///
/// priorSteps: chronological steps from earlier turns in the same Session (mirrors
/// runSession parameter at line 427). Replayed via buildMessages so the LLM sees
/// conversation history when entering plan mode mid-session (SC4: --plan --resume).
///
/// systemPromptSuffix: appended to config.SystemPrompt so the LLM is instructed
/// to emit action="plan" instead of a tool call. Plan 16-02 supplies the actual
/// suffix string from CompositionRoot; this function takes it as a parameter so
/// Core stays string-literal-free for plan-mode prompting.
let runPlanTurn
    (config: AgentConfig)
    (client: ILlmClient)
    (model: Model)
    (priorSteps: Step list)
    (userInput: string)
    (systemPromptSuffix: string)
    (ct: CancellationToken)
    : Task<Result<Plan, AgentError>> =
    task {
        let combinedSystemPrompt = config.SystemPrompt + "\n\n" + systemPromptSuffix
        let baseMessages = buildMessages combinedSystemPrompt userInput priorSteps None None

        // Inner helper: extract Plan from LlmResponse + run validator.
        // Returns Ok plan, or Error with the parse/validation cause.
        let extractAndValidate (response: LlmResponse) : Result<Plan, AgentError> =
            match response.Output with
            | LlmOutput.Plan p -> validatePlan userInput p
            | LlmOutput.ToolCall _
            | LlmOutput.FinalAnswer _ ->
                Error (PlanInvalid "expected plan output, got tool/final action")

        let buildCorrection (err: AgentError) : Message =
            // Role = User per Phase 20-03 probe (2026-04-27, REJECT verdict). 122B HTTP 404
            // on mid-conversation Role=System ("System message must be at the beginning.").
            // The authority signal is the [PLAN ...] text marker, not the role.
            // See scripts/probe-system-role.sh + documentation/howto/enforce-llm-tool-terminality-via-post-user-injection.md.
            let detail =
                match err with
                | InvalidJsonOutput raw ->
                    let snippet = if raw.Length > 200 then raw.Substring(0, 200) + "..." else raw
                    sprintf "[PLAN PARSE ERROR] Your previous response was not valid JSON. Required: {\"thought\":\"...\",\"action\":\"plan\",\"input\":{\"steps\":[{\"tool\":\"...\",\"input\":{...},\"rationale\":\"...\"}],\"rationale\":\"...\"}}. Raw: %s" snippet
                | SchemaViolation d ->
                    sprintf "[PLAN PARSE ERROR] Your previous response did not match the plan schema: %s. Emit action=\"plan\" with input={steps:[...], rationale:\"...\"}." d
                | PlanInvalid d ->
                    sprintf "[PLAN INVALID] Your previous plan failed validation: %s. Constraints: max 10 steps; tool must be one of read_file/write_file/list_dir/run_shell/edit_file/glob_search/grep_search; no two adjacent steps may be byte-identical." d
                | _ ->
                    "[PLAN ERROR] Re-emit a valid plan."
            { Role = User; Content = detail }

        // Attempt 1
        let! attempt1 = client.CompleteAsync baseMessages model ct
        match attempt1 with
        | Error (LlmUnreachable _ as e) -> return Error e
        | Error (UserCancelled as e) -> return Error e
        | Error (PathRetired _ as e) -> return Error e
        | Ok response ->
            match extractAndValidate response with
            | Ok plan -> return Ok plan
            | Error e1 ->
                let correction = buildCorrection e1
                let messages2 = baseMessages @ [ correction ]
                let! attempt2 = client.CompleteAsync messages2 model ct
                match attempt2 with
                | Error e -> return Error e
                | Ok response2 ->
                    return extractAndValidate response2
        | Error other ->
            // InvalidJsonOutput / SchemaViolation come back as Error from CompleteAsync
            // (parsing happens inside QwenHttpClient before returning). Treat them as
            // retryable here — same correction-and-retry shape.
            let correction = buildCorrection other
            let messages2 = baseMessages @ [ correction ]
            let! attempt2 = client.CompleteAsync messages2 model ct
            match attempt2 with
            | Error e -> return Error e
            | Ok response2 -> return extractAndValidate response2
    }
