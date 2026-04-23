module BlueCode.Cli.Rendering

open System
open BlueCode.Core.Domain

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
    | MaxLoopsExceeded -> "Max loops exceeded (5 steps with no final answer)."
    | LoopGuardTripped action ->
        sprintf "Loop guard: action '%s' was called 3 times with the same input. Aborting." action
    | UserCancelled -> "Cancelled."
