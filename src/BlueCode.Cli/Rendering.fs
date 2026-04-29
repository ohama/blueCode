module BlueCode.Cli.Rendering

open System
open BlueCode.Core.Domain
open BlueCode.Cli.Adapters.FileSessionStore

/// Display mode toggle. Phase 4 ships Compact + Verbose only.
/// Phase 5 (CLI-07, --trace) will introduce a separate stderr JSON logging path,
/// NOT a third RenderMode here (trace is log output, not display output).
type RenderMode =
    | Compact
    | Verbose

/// Short one-word summary of a tool action for Compact mode.
let private toolSummary (action: LlmOutput) : string =
    match action with
    | FinalAnswer _ -> "final answer"
    | ToolCall(ToolName n, _) ->
        match n with
        | "read_file" -> "reading file"
        | "write_file" -> "editing code"
        | "list_dir" -> "listing directory"
        | "run_shell" -> "running shell"
        | other -> other
    | Plan _ -> "plan"

let private statusSymbol: StepStatus -> string =
    function
    | StepSuccess -> "ok"
    | StepFailed _ -> "fail"
    | StepAborted -> "aborted"

let private toolResultSummary (r: ToolResult option) : string =
    match r with
    | None -> "(final)"
    | Some(Success _) -> "success"
    | Some(Failure(code, _)) -> sprintf "exit %d" code
    | Some(SecurityDenied _) -> "security denied"
    | Some(PathEscapeBlocked _) -> "path blocked"
    | Some(ToolResult.Timeout secs) -> sprintf "timeout %ds" secs

/// One-line compact summary. Example:
///   > reading file... [ok, 423ms]
let private renderCompact (step: Step) : string =
    sprintf "> %s... [%s, %dms]" (toolSummary step.Action) (statusSymbol step.Status) step.DurationMs

/// Multi-line verbose output. Shows every field from the Step record including
/// OBS-04 timing. Example:
///   [Step 1] (ok, 423ms)
///     thought: [not captured in v1]
///     action:  read_file {"path":"README.md"}
///     result:  Success (3421 chars)
let private renderVerbose (step: Step) : string =
    let (Thought t) = step.Thought

    let actionLine =
        match step.Action with
        | FinalAnswer ans -> sprintf "final: %s" ans
        | ToolCall(ToolName n, ToolInput m) ->
            let raw = m |> Map.tryFind "_raw" |> Option.defaultValue "{}"
            sprintf "%s %s" n raw
        | Plan p -> sprintf "plan (%d steps)" p.Steps.Length

    let resultLine =
        match step.ToolResult with
        | None -> "(final answer — no tool)"
        | Some(Success output) -> sprintf "Success (%d chars)" output.Length
        | Some(Failure(code, stderr)) ->
            sprintf
                "Failure exit=%d stderr=%s"
                code
                (if stderr.Length > 80 then
                     stderr.Substring(0, 80) + "..."
                 else
                     stderr)
        | Some(SecurityDenied reason) -> sprintf "SecurityDenied: %s" reason
        | Some(PathEscapeBlocked path) -> sprintf "PathEscapeBlocked: %s" path
        | Some(ToolResult.Timeout secs) -> sprintf "Timeout after %ds" secs

    sprintf
        "[Step %d] (%s, %dms)\n  thought: %s\n  action:  %s\n  result:  %s"
        step.StepNumber
        (statusSymbol step.Status)
        step.DurationMs
        t
        actionLine
        resultLine

/// Produce a per-step display string in the requested mode.
let renderStep (mode: RenderMode) (step: Step) : string =
    match mode with
    | Compact -> renderCompact step
    | Verbose -> renderVerbose step

/// Final answer banner. Called once at end of successful turn.
let renderResult (result: AgentResult) : string = sprintf "\n%s\n" result.FinalAnswer

/// Convert an AgentError to a one-line, user-readable message. NO stack trace.
/// Phase 4 SC-5 ("Ctrl+C ... no OperationCanceledException stack trace") and
/// SC-2 ("MaxLoopsExceeded ... user-readable message") both depend on this.
let renderError (err: AgentError) : string =
    match err with
    | LlmUnreachable(url, detail) -> sprintf "LLM unreachable (%s): %s" url detail
    | InvalidJsonOutput raw ->
        let snippet =
            if raw.Length > 120 then
                raw.Substring(0, 120) + "..."
            else
                raw

        sprintf "LLM returned invalid JSON twice. Raw: %s" snippet
    | SchemaViolation detail -> sprintf "LLM output schema violation: %s" detail
    | UnknownTool(ToolName n) -> sprintf "Unknown tool: %s" n
    | ToolFailure(_, ex) -> sprintf "Tool execution failed: %s" ex.Message
    | MaxLoopsExceeded -> "Max loops exceeded (10 steps with no final answer)."
    | LoopGuardTripped action ->
        sprintf "Loop guard: action '%s' was called 3 times with the same input. Aborting." action
    | UserCancelled -> "Cancelled."
    | SessionNotFound (SessionId id) -> sprintf "Session not found: %s" id
    | SessionCorrupt detail -> sprintf "Session file corrupt: %s" detail
    | PlanInvalid detail -> sprintf "Plan invalid: %s" detail
    | PathRetired path -> sprintf "Path retired in Phase 19: %s. Re-run with --model 122b (or no flag)." path

/// 9-command help text shown by `/help`. Static string, no parameters.
/// Includes both `/exit` and `/quit` as separate entries (counted separately
/// per Phase 31 success criterion 1: "9 commands list — 7 in-milestone + future-stub").
/// Uses `printfn`-friendly plain text (NOT Spectre markup) so that tests capturing
/// Console.SetOut see the exact string. CLAUDE.md "Stream separation" + research § Pitfall 1.
let renderHelp : string =
    """slash commands:
  /help              show this help
  /status            session info: id, model, steps, context %
  /clear             reset session in-place (new session id, keep REPL running)
  /exit              save session and quit
  /quit              alias for /exit
  /sessions          list 10 most-recent sessions
  /resume <id>       switch to a saved session in-place
  /plan              toggle plan-mode for next turn [coming in v2.5]
  /edit              open $EDITOR for multi-line input [coming in v2.5]"""

/// Render the `/status` output. Pure: takes the current Session, ForcedModel option, and MaxModelLen,
/// returns a multi-line string. NO Spectre markup (CLAUDE.md "Stream separation";
/// `/status` output is captured by Console.SetOut in tests).
///
/// Fields per Phase 31 success criterion 2:
///   - session id (32-char hex from SessionId)
///   - model name ("122b" / "35b" / "122b (default)")
///   - step count (currentSession.Steps.Length — accumulated across all turns in this session)
///   - accumulated char count (sum of "%A" Action + "%A" ToolResult per step — same heuristic as runSingleTurn)
///   - context % (estimatedChars * 100 / (MaxModelLen * 4))
///
/// `MaxModelLen` is the v1.1 startup FLOOR (8192) — the real per-port value lives in the
/// QwenHttpClient lazy probe and is not surfaced to AppComponents. Awaiting that probe
/// here would block /status for up to 300s on cold-start. Label the output to make this
/// explicit (research § Q3, Pitfall 2 of research § Anti-Patterns).
let renderStatus (session: Session) (forcedModel: Model option) (maxModelLen: int) : string =
    let (SessionId idStr) = session.Id
    let modelName =
        match forcedModel with
        | Some Qwen122B -> "122b"
        | Some Qwen35B  -> "35b"
        | None          -> "122b (default)"
    let steps = session.Steps.Length
    let accChars =
        session.Steps
        |> List.sumBy (fun s ->
            (sprintf "%A" s.Action).Length + (sprintf "%A" s.ToolResult).Length)
    let maxChars = maxModelLen * 4   // tokens * ~4 chars/token
    let pct = if maxChars > 0 then accChars * 100 / maxChars else 0
    sprintf
        "session:  %s\nmodel:    %s\nsteps:    %d\nchars:    %d / ~%d (%d%%) [floor; probed on first LLM call]"
        idStr modelName steps accChars maxChars pct

/// Render the `/sessions` listing. Pure: takes a SessionMeta list, returns a multi-line
/// string. NO Spectre markup (CLAUDE.md "Stream separation"; tests capture via Console.SetOut).
///
/// Empty list → "no sessions found" (single line).
/// Non-empty → header row + one row per meta, with columns:
///   - session id (32-char hex, %-34s padding)
///   - started timestamp (%-25s, ISO-ish "yyyy-MM-dd HH:mm:ss")
///   - turn count (%-6d)
///   - first thought (≤40 chars displayed in this row, with "..." suffix if SessionMeta excerpt
///     was the full 80-char-truncated value — visual narrow column, the SessionMeta excerpt
///     itself is already capped at 80 chars by listRecent so this is a presentation detail).
///
/// Column header reads "first thought" NOT "first prompt" — research § Open Question #1:
/// the user's prompt is not stored in the jsonl, so the LLM's first reasoning step is the
/// best available proxy. Calling it "first prompt" would be misleading.
let renderSessions (metas: SessionMeta list) : string =
    if metas.IsEmpty then
        "no sessions found"
    else
        let header = sprintf "%-34s %-25s %-6s %s" "session id" "started" "turns" "first thought"
        let rows =
            metas
            |> List.map (fun m ->
                let (SessionId idStr) = m.Id
                let dateStr = m.StartedAt.ToString("yyyy-MM-dd HH:mm:ss")
                let displayExcerpt =
                    if m.FirstPromptExcerpt.Length > 40 then
                        m.FirstPromptExcerpt.Substring(0, 40) + "..."
                    else
                        m.FirstPromptExcerpt
                sprintf "%-34s %-25s %-6d %s" idStr dateStr m.TurnCount displayExcerpt)
        header :: rows |> String.concat "\n"
