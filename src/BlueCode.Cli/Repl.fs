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
        let mutable lastCode = 0
        let mutable running = true

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
                    printfn "%s" (Rendering.renderStatus currentSession components.Config.ForcedModel components.MaxModelLen)
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
                | Some (Slash (Plan | Edit)) ->
                    // Phase 33 (Plan) and Phase 34 (Edit) future-stubs.
                    // Sessions and Resume have moved to real handlers above.
                    printfn "(not yet implemented — coming in a future v2.5 phase)"
                | Some (Prompt prompt) ->
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
