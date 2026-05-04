module BlueCode.Cli.Repl

open System
open System.Threading
open System.Threading.Tasks
open Serilog
open Spectre.Console
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Core.AgentLoop
open BlueCode.Cli.Rendering
open BlueCode.Cli.SlashCommand
open BlueCode.Cli.CompositionRoot

/// Determine whether a context-window warning should fire.
/// totalChars: accumulated character count of messages sent to LLM so far in this turn.
/// maxModelLen: resolved max_model_len from /v1/models (in tokens).
/// alreadyWarned: whether the warning has already been shown in this turn.
///
/// Heuristic: totalTokens ≈ totalChars / 4 (research § Pattern 5,
/// "Don't Hand-Roll"). Fire when totalChars >= maxModelLen * 4 * 0.80,
/// which simplifies to totalChars * 5 >= maxModelLen * 16
/// (integer-only, no floating-point).
///
/// PUBLIC for testability (ContextWarningTests imports this directly).
let shouldWarnContextWindow (totalChars: int) (maxModelLen: int) (alreadyWarned: bool) : bool =
    if alreadyWarned then
        false
    else
        // 80% of (maxModelLen * 4 chars) = maxModelLen * 16 / 5 chars
        // Equivalent: totalChars * 5 >= maxModelLen * 16
        int64 totalChars * 5L >= int64 maxModelLen * 16L

/// Single-turn REPL entry. Phase 4 scope: ONE prompt, ONE turn, exit.
/// Phase 5 (CLI-02) extends this to a multi-turn loop via runMultiTurn.
///
/// Responsibilities:
///   1. Register Ctrl+C handler -> cancel the CancellationTokenSource gracefully.
///   2. Invoke AgentLoop.runSession with:
///        onStep callback -> components.JsonlSink.WriteStep (per-step JSONL write, SC-6).
///   3. On Ok: write final answer to stdout via renderResult. Exit 0.
///   4. On Error: write renderError to stdout. Exit 1 for most errors, 130 for UserCancelled.
///   5. Defensive catch: any OperationCanceledException escaping runSession -> treat as UserCancelled.
///
/// renderMode: Compact (default) or Verbose (--verbose flag via CLI-03).
///
/// Exit code convention:
///   0   - successful turn
///   1   - agent error (MaxLoopsExceeded, LoopGuardTripped, Llm*, Tool*, etc.)
///   130 - user-cancelled (SIGINT, Ctrl+C - POSIX 128+2)
///
/// priorSteps: steps accumulated from prior turns in this session ([] for first turn).
/// Returns (exitCode, stepsProducedThisTurn) so callers can accumulate session steps.
let runSingleTurn
    (prompt: string)
    (priorSteps: Step list)
    (components: AppComponents)
    (renderMode: RenderMode)
    : Task<int * Step list> =
    task {
        use cts = new CancellationTokenSource()

        let cancelHandler =
            System.ConsoleCancelEventHandler(fun _ args ->
                args.Cancel <- true // REQUIRED: prevents immediate process kill
                cts.Cancel())

        Console.CancelKeyPress.AddHandler(cancelHandler)

        try
            // Wire AgentLoop's onStep callback to per-step JSONL write.
            // This satisfies SC-6: JSONL is readable after the process exits
            // (AutoFlush=true in JsonlSink ensures each write is durable
            // before runSession proceeds to the next iteration).

            // Per-turn intra-turn context accumulator (OBS-03 80% warning).
            // Both vars are LOCAL to this runSingleTurn call; they reset
            // naturally on each new turn (multi-turn REPL calls runSingleTurn
            // fresh each iteration). Cross-turn accumulation is POST-V1.
            let mutable totalChars = 0
            let mutable warnedThisTurn = false
            let mutable thisTurnSteps : Step list = []

            let onStep (step: Step) =
                components.JsonlSink.WriteStep step
                thisTurnSteps <- step :: thisTurnSteps
                printfn "%s" (renderStep renderMode step)

                // Accumulate char count of action + result representations.
                // These are the same strings we display / log — a reasonable
                // approximation of the tokens sent/received per step.
                let actionRepr = sprintf "%A" step.Action
                let resultRepr = sprintf "%A" step.ToolResult
                totalChars <- totalChars + actionRepr.Length + resultRepr.Length

                // 80% context warning: fires at most ONCE per turn (OBS-03).
                // shouldWarnContextWindow is a pure helper above (testable).
                if shouldWarnContextWindow totalChars components.MaxModelLen warnedThisTurn then
                    // Use printfn (Console.Out) so tests that redirect Console.SetOut capture it.
                    // AnsiConsole.MarkupLine bypasses Console.SetOut in non-TTY / test environments.
                    printfn
                        "WARNING: context at 80%% of model limit (%d chars accumulated, max_model_len=%d tokens ~= %d chars). Next step may truncate."
                        totalChars
                        components.MaxModelLen
                        (components.MaxModelLen * 4)

                    warnedThisTurn <- true

                // Always emit this Debug event — only visible when --trace flips
                // levelSwitch to Debug (CLI-07). sprintf "%A" produces untruncated
                // F# record display for the "full untruncated input/output" requirement.
                // This is log data on stderr; user asked for it via --trace so no
                // sensitive-data truncation applies.
                Log.Debug(
                    "Step {Number}: action={Action} elapsed_ms={DurationMs} input={Input} output={Output}",
                    step.StepNumber,
                    step.Action,
                    step.DurationMs,
                    actionRepr,
                    resultRepr
                )

            let! result =
                try
                    runSession components.Config components.LlmClient components.ToolExecutor onStep priorSteps prompt cts.Token
                with :? OperationCanceledException ->
                    // Defensive fallback. QwenHttpClient and FsToolExecutor already
                    // map cancellation to Error UserCancelled; this `with` is a
                    // belt-and-suspenders safety net (research § Pattern 7, Pitfall 2).
                    Task.FromResult(Error UserCancelled)

            let stepsProduced = List.rev thisTurnSteps

            match result with
            | Ok agentResult ->
                printfn "%s" (renderResult agentResult)

                Log.Information(
                    "Session ok: {Steps} steps, model={Model}, log={LogPath}",
                    agentResult.Steps.Length,
                    agentResult.Model,
                    components.LogPath
                )

                return (0, stepsProduced)
            | Error UserCancelled ->
                printfn "%s" (renderError UserCancelled)
                Log.Information("Session cancelled by user")
                return (130, stepsProduced)
            | Error e ->
                printfn "%s" (renderError e)
                Log.Warning("Session error: {Error}", sprintf "%A" e)
                return (1, stepsProduced)
        finally
            Console.CancelKeyPress.RemoveHandler(cancelHandler)
    }

/// Multi-turn REPL loop with explicit Session accumulation and persistence.
/// Accumulates steps from each turn into currentSession, calls sessionStore.Save
/// after every completed turn, and threads priorSteps into runSession so the LLM
/// sees conversation history from earlier turns.
///
/// This is the new entry point for 15-02's Program.fs (--resume / --new-session paths).
/// Legacy runMultiTurn delegates to this with a fresh Session + FileSessionStore.
let runMultiTurnWithSession
    (components: AppComponents)
    (renderMode: RenderMode)
    (initialSession: Session)
    (sessionStore: ISessionStore)
    : Task<int> =
    task {
        let (SessionId idStr) = initialSession.Id
        printfn "blueCode — multi-turn mode. Session: %s. Type /exit or press Ctrl+D to quit." idStr
        // Print session id to stderr so it's grep-able from shell scripts after process exit.
        eprintfn "Session: %s" idStr
        let mutable currentSession : Session = initialSession
        let mutable planModeActive = false   // Phase 33: /plan toggle state; flips to true on /plan, false after Accept/Quit/exhausted-rejects
        let mutable lastCode = 0
        let mutable running = true

        // Phase 34 (EDIT-01): shared prompt-dispatch helper. Factored out of the two
        // `Some (Prompt ...)` arms so the Slash Edit arm can reuse the same
        // plan-mode-aware dispatch path. Captures mutable cells via closure.
        let handlePromptTurn (prompt: string) : Task<unit> =
            task {
                if planModeActive then
                    // Phase 33 (SLASH-07): plan-gated turn. Mirrors Program.fs lines 172-256
                    // (single-turn --plan mode) adapted for in-REPL context.
                    //
                    // Differences from Program.fs:
                    //   - Quit returns to REPL prompt (NOT process exit).
                    //   - planModeActive auto-disables on Accept (after execute), on Quit,
                    //     and on rejectCount-exhaustion. Open Question #1+#2 resolution:
                    //     one-shot semantics; user re-types /plan for next plan-gated turn.
                    //   - lastCode behaves identically to the standard Prompt arm
                    //     (130 → 0 mapping for graceful Ctrl+C; otherwise pass through).
                    let model =
                        components.Config.ForcedModel
                        |> Option.defaultValue Qwen122B   // 122B canonical default; matches Program.fs:180
                    let maxUserRejects = 3                // matches Program.fs:187 (research § Pitfall 7 — local constant OK)
                    let mutable rejectCount = 0
                    let mutable currentPrompt = prompt
                    let mutable turnDone = false

                    while not turnDone && rejectCount < maxUserRejects do
                        let! planResult =
                            BlueCode.Core.AgentLoop.runPlanTurn
                                components.Config
                                components.LlmClient
                                model
                                currentSession.Steps
                                currentPrompt
                                CompositionRoot.planSystemPromptSuffix
                                CancellationToken.None

                        match planResult with
                        | Error e ->
                            // runPlanTurn already retried internally (2 attempts).
                            // Surface the error and abandon this turn; keep REPL alive.
                            // planModeActive auto-disables — user can re-/plan to retry.
                            printfn "%s" (renderError e)
                            planModeActive <- false
                            turnDone <- true
                        | Ok plan ->
                            BlueCode.Cli.PlanGate.render plan
                            match BlueCode.Cli.PlanGate.promptUser BlueCode.Cli.PlanGate.realKeyReader with
                            | BlueCode.Cli.PlanGate.Accept ->
                                // Disable plan-mode BEFORE execute — one-shot semantics
                                // (Open Question #1 resolution). User re-types /plan for
                                // the next plan-gated turn.
                                planModeActive <- false
                                let! (code, newSteps) =
                                    runSingleTurn prompt currentSession.Steps components renderMode
                                let updated =
                                    { currentSession with
                                        Steps = currentSession.Steps @ newSteps
                                        LastActivityAt = DateTimeOffset.UtcNow }
                                currentSession <- updated
                                let! saveRes = sessionStore.Save updated CancellationToken.None
                                match saveRes with
                                | Ok () -> ()
                                | Error e ->
                                    Log.Warning("Session save failed: {Error}", sprintf "%A" e)
                                    eprintfn "WARNING: session save failed: %A" e
                                lastCode <- if code = 130 then 0 else code
                                turnDone <- true
                            | BlueCode.Cli.PlanGate.Reject ->
                                rejectCount <- rejectCount + 1
                                currentPrompt <-
                                    sprintf "[PLAN REJECTED] The previous plan was rejected by the user. Propose a different plan.\n\n%s" prompt
                            | BlueCode.Cli.PlanGate.Edit comment ->
                                rejectCount <- rejectCount + 1
                                currentPrompt <-
                                    sprintf "[PLAN EDIT NOTE: %s] Revise the previous plan accordingly.\n\n%s" comment prompt
                            | BlueCode.Cli.PlanGate.Quit ->
                                // User abandoned; return to REPL prompt (NOT process exit).
                                // planModeActive auto-disables (Open Question #2 resolution).
                                planModeActive <- false
                                turnDone <- true

                    if not turnDone then
                        // Loop exited via rejectCount >= maxUserRejects without acceptance.
                        printfn "Plan-mode: %d rejections without acceptance — abandoning." rejectCount
                        planModeActive <- false
                else
                    let! (code, newSteps) =
                        runSingleTurn prompt currentSession.Steps components renderMode
                    // Always update Session.Steps with newSteps (even on failure — partial progress is informative).
                    let updated =
                        { currentSession with
                            Steps = currentSession.Steps @ newSteps
                            LastActivityAt = DateTimeOffset.UtcNow }
                    currentSession <- updated
                    // Save AFTER each turn (whether success or error) so a crash mid-session is recoverable.
                    let! saveRes = sessionStore.Save updated CancellationToken.None
                    match saveRes with
                    | Ok () -> ()
                    | Error e ->
                        Log.Warning("Session save failed: {Error}", sprintf "%A" e)
                        eprintfn "WARNING: session save failed: %A" e
                    lastCode <- if code = 130 then 0 else code
            }

        while running do
            printf "\nblueCode> "
            let line = Console.ReadLine()  // null on Ctrl+D / EOF

            match line with
            | null -> running <- false
            | _ ->
                match SlashCommand.parse line with
                | None ->
                    // blank / whitespace-only line — skip silently (preserves prior behavior)
                    ()
                | Some (Slash Exit) ->
                    // /exit and /quit both map here. Auto-save semantic is preserved by
                    // the existing per-turn Save in the Prompt branch — last completed turn
                    // is already on disk. No flush needed (research § Q5).
                    running <- false
                | Some (Slash Help) ->
                    printfn "%s" Rendering.renderHelp
                | Some (Slash Status) ->
                    printfn "%s" (Rendering.renderStatus currentSession components.Config.ForcedModel components.MaxModelLen planModeActive)
                | Some (Slash Clear) ->
                    // /clear: new session id, empty Steps, NEW jsonl created lazily on first
                    // future Save. Old session jsonl stays untouched (FileSessionStore.Save
                    // creates files lazily — see research § Q4). priorSteps reset is automatic
                    // because runSingleTurn reads currentSession.Steps every call.
                    let newId = BlueCode.Cli.Adapters.FileSessionStore.newSessionId ()
                    let now = DateTimeOffset.UtcNow
                    currentSession <-
                        { Id = newId; Steps = []; CreatedAt = now; LastActivityAt = now }
                    let (SessionId newIdStr) = newId
                    printfn "Session cleared. New session: %s" newIdStr
                | Some (Slash Sessions) ->
                    // Phase 32 (SLASH-05): list the 10 most-recent sessions on disk.
                    // listRecent returns SessionMeta list (sorted mtime-desc, capped at 10);
                    // renderSessions formats it as a multi-line plain string.
                    // No LLM call (in-process meta-control).
                    let metas = BlueCode.Cli.Adapters.FileSessionStore.listRecent 10
                    printfn "%s" (Rendering.renderSessions metas)
                | Some (Slash (Resume "")) ->
                    // Phase 32 (SLASH-06): empty-arg guard. The parser produces
                    // `Resume ""` when the user typed `/resume` alone. Match this
                    // case BEFORE the general `Resume id` so we don't call
                    // sessionStore.Load with an empty SessionId (research § Pitfall 4).
                    printfn "usage: /resume <session-id>"
                | Some (Slash (Resume id)) ->
                    // Phase 32 (SLASH-06): in-place session switch.
                    // sessionStore.Load returns Result<Session, AgentError>:
                    //   Ok loaded             → currentSession <- loaded; print confirmation
                    //   Error SessionNotFound → friendly "Session not found: <id>" message
                    //   Error SessionCorrupt  → friendly "Session file corrupt: <detail>" message
                    //   Error other           → defensive fallback (research § Q6: any other
                    //                           AgentError shouldn't reach here from Load,
                    //                           but match `_` for total compile-time coverage)
                    // REPL stays alive on every error variant (roadmap SC-3).
                    let! loadResult = sessionStore.Load (SessionId id) CancellationToken.None
                    match loadResult with
                    | Ok loaded ->
                        currentSession <- loaded
                        let (SessionId newIdStr) = loaded.Id
                        printfn "Resumed session: %s (%d steps)" newIdStr loaded.Steps.Length
                    | Error (SessionNotFound _) ->
                        printfn "Session not found: %s" id
                    | Error (SessionCorrupt detail) ->
                        printfn "Session file corrupt: %s" detail
                    | Error other ->
                        // Defensive — Load doesn't return other variants in current
                        // FileSessionStore impl (lines 142-145 catch all to SessionCorrupt),
                        // but ISessionStore is an interface and a future store could.
                        printfn "Resume failed: %A" other
                | Some (Slash Plan) ->
                    // Phase 33 (SLASH-07): toggle plan-mode for the NEXT prompt turn.
                    // Idempotent flip — typing /plan twice toggles on then off.
                    // Notification is printfn only (SC-6: NOT injected into LLM messages —
                    // mid-conversation Role=System triggers HTTP 404 on Qwen 3.5 122B per
                    // Phase 20-03 probe; the notification is purely user-facing console).
                    planModeActive <- not planModeActive
                    if planModeActive then
                        printfn "[plan mode on] — next prompt will enter plan-gate before execution"
                    else
                        printfn "[plan mode off] — returning to direct agent-loop"
                | Some (Slash Edit) ->
                    // Phase 34 (EDIT-01): /edit opens $EDITOR (or vi) on a tmpfile, reads
                    // content after the editor exits. Non-empty content is dispatched as
                    // the next prompt through the same handlePromptTurn used for typed
                    // prompts (so plan-mode branching is preserved if planModeActive=true
                    // when /edit is invoked). Empty/whitespace-only content -> "Edit cancelled."
                    //
                    // Ctrl+C while editor is open: register a CancelKeyPress handler that
                    // sets args.Cancel=true so SIGINT does NOT kill blueCode. The editor
                    // (vi/vim) handles its own SIGINT (cancel -> normal mode); if user
                    // force-kills the editor, WaitForExit returns and the empty-file path
                    // produces "Edit cancelled." (research § Pattern 5 simplified handler).
                    let cancelHandler =
                        System.ConsoleCancelEventHandler(fun _ args -> args.Cancel <- true)
                    Console.CancelKeyPress.AddHandler(cancelHandler)
                    try
                        let! contentOpt =
                            BlueCode.Cli.EditCommand.openEditorAsync
                                BlueCode.Cli.EditCommand.realEditorLauncher
                        match contentOpt with
                        | None ->
                            printfn "Edit cancelled."
                        | Some content ->
                            do! handlePromptTurn content
                    finally
                        Console.CancelKeyPress.RemoveHandler(cancelHandler)
                | Some (Prompt prompt) ->
                    do! handlePromptTurn prompt

        return lastCode
    }

/// Multi-turn REPL loop (CLI-02). Reads lines from stdin and dispatches each
/// to runSingleTurn. Ctrl+D (ReadLine() = null) and "/exit" both terminate.
/// Per-turn Ctrl+C (SIGINT) cancels the current turn via the existing
/// CancelKeyPress handler in runSingleTurn — after a 130 exit, the loop
/// continues. Session steps are threaded across turns for cross-turn LLM context.
///
/// Legacy entry point — creates a fresh Session + FileSessionStore then delegates
/// to runMultiTurnWithSession. 15-02 will change Program.fs to call
/// runMultiTurnWithSession directly with a session loaded from --resume.
///
/// renderMode: threaded from Program.fs CLI flag (Compact or Verbose).
let runMultiTurn (components: AppComponents) (renderMode: RenderMode) : Task<int> =
    let now = DateTimeOffset.UtcNow
    let session : Session =
        { Id = BlueCode.Cli.Adapters.FileSessionStore.newSessionId ()
          Steps = []
          CreatedAt = now
          LastActivityAt = now }
    let store = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> ISessionStore
    runMultiTurnWithSession components renderMode session store
