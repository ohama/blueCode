module Program

open System
open System.IO
open Argu
open Serilog
open BlueCode.Cli.Adapters.Logging
open BlueCode.Cli
open BlueCode.Cli.CliArgs
open BlueCode.Cli.Rendering
open BlueCode.Cli.CompositionRoot
open BlueCode.Core.Domain

/// Process entry point (Phase 5). Wires Argu parser for CLI-06 then dispatches
/// to single-turn (prompt present) or multi-turn REPL (no prompt) per CLI-01/CLI-02.
///
/// Exit codes:
///   0   -- success / REPL exited cleanly
///   1   -- agent error (also: session not found / session corrupt)
///   2   -- usage error (--help, --version, unknown model, unknown flag, conflicting flags)
///   130 -- user cancelled (SIGINT Ctrl+C) in single-turn mode
[<EntryPoint>]
let main (argv: string array) : int =
    // Step 1: configure logging FIRST (Serilog's default logger is a silent no-op).
    configure ()

    let parser = ArgumentParser.Create<CliArgs>(programName = "blueCode")

    try
        let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
        let promptWords = results.TryGetResult CliArgs.Prompt |> Option.defaultValue []
        let isVerbose = results.Contains CliArgs.Verbose
        let isTrace = results.Contains CliArgs.Trace
        let forcedStr = results.TryGetResult CliArgs.Model

        // NEW (15-02): --resume / --new-session parsing
        let resumeId = results.TryGetResult CliArgs.Resume   // string option
        let isNewSession = results.Contains CliArgs.NewSession
        // NEW (19-02): --with-35b / --withdual dual-mode flag
        let withDual = results.Contains CliArgs.WithDual
        // NEW (16-02): --plan flag
        let isPlanMode = results.Contains CliArgs.Plan
        // NEW (36-02): --allow-paths comma-separated path list. Empty list when absent.
        let allowPaths : string list =
            results.TryGetResult CliArgs.AllowPaths
            |> Option.map (fun s ->
                s.Split(',')
                |> Array.map (fun p -> p.Trim())
                |> Array.filter (fun p -> p.Length > 0)
                |> Array.toList)
            |> Option.defaultValue []

        // NEW (15-02): mutually-exclusive validation. Reject BOTH-set; either-or-neither is fine.
        // Done POST-parse BEFORE bootstrap so we don't waste bootstrap cycles.
        match resumeId, isNewSession with
        | Some _, true ->
            eprintfn "ERROR: conflicting flags: --resume and --new-session cannot be used together."
            Log.CloseAndFlush()
            exit 2
        | _ -> ()

        // Phase 16-02: --plan requires a prompt — REPL plan-mode is v2.1+.
        if isPlanMode && List.isEmpty promptWords then
            eprintfn "ERROR: --plan requires a prompt; REPL plan-mode is v2.1+ scope."
            Log.CloseAndFlush()
            exit 2

        // Phase 16-02 guardrail: --plan with --with-35b unsupported (35B is rollback-only).
        if isPlanMode && withDual then
            eprintfn "ERROR: --plan with --with-35b is not supported in v2.0; 35B service is rollback-only."
            Log.CloseAndFlush()
            exit 2

        // parseForcedModel raises on invalid model string; wrap as usage error (exit 2).
        // (B1 W4) Specific catch for Phase 19 retirement messages → exit 2 (not 1).
        // Generic exceptions (e.g. unknown model) also → exit 2 via the outer ArguParseException.
        let forcedModel =
            try
                parseForcedModel forcedStr withDual
            with
            | ex when ex.Message.Contains "retired in Phase 19" ->
                eprintfn "ERROR: %s" ex.Message
                Log.CloseAndFlush()
                exit 2
            | ex ->
                eprintfn "ERROR: %s" ex.Message
                Log.CloseAndFlush()
                exit 2

        // (B1) When --with-35b is set, fail fast if 35B service is not responding.
        // Without this guard, the user sees a generic LlmUnreachable on first CompleteAsync
        // after ~15-30s of probe latency. SC3 requires a clear '35B not loaded' message.
        // Exit 1 (not 2) — "service unhealthy" is distinct from the "retired alias" exit 2.
        if withDual then
            use httpClient = new System.Net.Http.HttpClient(Timeout = System.TimeSpan.FromSeconds(2.0))
            try
                let resp = httpClient.GetAsync("http://127.0.0.1:8000/v1/models").GetAwaiter().GetResult()
                if not resp.IsSuccessStatusCode then
                    eprintfn "ERROR: 35B service not loaded — run: launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen35b.plist"
                    Log.CloseAndFlush()
                    exit 1
            with _ ->
                eprintfn "ERROR: 35B service not loaded — run: launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen35b.plist"
                Log.CloseAndFlush()
                exit 1

        // Step 2: flip LoggingLevelSwitch AFTER parse, BEFORE bootstrap.
        // This gates all subsequent Log.Debug calls on the --trace flag (CLI-07).
        // Default is Information (suppresses Debug); --trace flips to Debug.
        if isTrace then
            levelSwitch.MinimumLevel <- Serilog.Events.LogEventLevel.Debug

        // Step 3: derive RenderMode from --verbose flag (CLI-03/CLI-04).
        let renderMode: RenderMode = if isVerbose then Verbose else Compact

        let opts =
            { ForcedModel = forcedModel
              Verbose = isVerbose
              Trace = isTrace
              ResumeSessionId = resumeId |> Option.map SessionId
              NewSession = isNewSession
              WithDual35b = withDual
              PlanMode = isPlanMode
              AllowPaths = allowPaths }                        // NEW (36-02)

        let projectRoot = Directory.GetCurrentDirectory()

        Log.Information(
            "blueCode starting: cwd={Root} mode={Mode}",
            projectRoot,
            (if List.isEmpty promptWords then "repl" else "single")
        )

        let components = bootstrap projectRoot opts
        Log.Information("Context window floor: max_model_len={MaxLen} (lazy per-port probe resolves actual)", components.MaxModelLen)
        use _jsonlSink = components.JsonlSink

        // NEW (15-02): resolve the Session source.
        //   - --resume <id> → Load from disk; on error, print to stderr (no stack trace) + exit 1.
        //   - --new-session OR no flag → fresh Session with newSessionId().
        let session : Session =
            match opts.ResumeSessionId with
            | Some sid ->
                let ct = System.Threading.CancellationToken.None
                let loadResult = (components.SessionStore.Load sid ct).GetAwaiter().GetResult()
                match loadResult with
                | Ok s -> s
                | Error (SessionNotFound (SessionId idStr)) ->
                    eprintfn "ERROR: session not found: %s" idStr
                    Log.CloseAndFlush()
                    exit 1
                | Error (SessionCorrupt detail) ->
                    eprintfn "ERROR: session corrupt: %s" detail
                    Log.CloseAndFlush()
                    exit 1
                | Error other ->
                    eprintfn "ERROR: session load failed: %A" other
                    Log.CloseAndFlush()
                    exit 1
            | None ->
                let now = DateTimeOffset.UtcNow
                { Id = BlueCode.Cli.Adapters.FileSessionStore.newSessionId ()
                  Steps = []
                  CreatedAt = now
                  LastActivityAt = now }

        // NEW (15-02): print session id to stderr at startup — SC2 (grep-able).
        let (SessionId idStr) = session.Id
        eprintfn "Session: %s" idStr

        let exitCode =
            if isPlanMode then
                // ─── Plan mode (Phase 16-02) ────────────────────────────────────────────
                // Single-turn plan-then-execute. priorSteps comes from session.Steps
                // (resumed or fresh — Program.fs session resolution above already handled).
                // Guards: --plan without prompt -> exit 2 (above); --plan --with-35b -> exit 2 (above).
                let prompt = String.concat " " promptWords
                let model =
                    opts.ForcedModel
                    |> Option.defaultValue BlueCode.Core.Domain.Qwen122B  // 122B canonical default

                let ct = System.Threading.CancellationToken.None

                // Reject loop: track edit-comment between attempts. Bounded by maxUserRejects=3
                // to avoid infinite re-prompting on flaky LLM output. The internal runPlanTurn
                // 2-attempt retry stacks ON TOP of this user-facing loop.
                let maxUserRejects = 3
                let mutable rejectCount = 0
                let mutable currentPrompt = prompt
                let mutable finalDecision : PlanGate.PlanGateDecision option = None
                let mutable lastError : BlueCode.Core.Domain.AgentError option = None

                while finalDecision = None && rejectCount < maxUserRejects do
                    let planResult =
                        (BlueCode.Core.AgentLoop.runPlanTurn
                            components.Config
                            components.LlmClient
                            model
                            session.Steps
                            currentPrompt
                            CompositionRoot.planSystemPromptSuffix
                            ct).GetAwaiter().GetResult()

                    match planResult with
                    | Error e ->
                        eprintfn "%s" (renderError e)
                        lastError <- Some e
                        finalDecision <- Some PlanGate.Quit  // exit non-zero below
                    | Ok plan ->
                        PlanGate.render plan
                        match PlanGate.promptUser PlanGate.realKeyReader with
                        | PlanGate.Accept ->
                            finalDecision <- Some PlanGate.Accept
                        | PlanGate.Quit ->
                            finalDecision <- Some PlanGate.Quit
                        | PlanGate.Reject ->
                            rejectCount <- rejectCount + 1
                            // [PLAN REJECTED] prefix embedded in next runPlanTurn user prompt.
                            // Role = User per Phase 20-03 probe (2026-04-27) — 122B HTTP 404 on
                            // mid-conversation Role=System. The text marker carries authority,
                            // not the role. The user-prompt position in buildMessages is always Role=User.
                            currentPrompt <- sprintf "[PLAN REJECTED] The previous plan was rejected by the user. Propose a different plan.\n\n%s" prompt
                        | PlanGate.Edit comment ->
                            rejectCount <- rejectCount + 1
                            currentPrompt <- sprintf "[PLAN EDIT NOTE: %s] Revise the previous plan accordingly.\n\n%s" comment prompt

                match finalDecision, lastError with
                | Some PlanGate.Accept, _ ->
                    // Execute the accepted plan by calling runSingleTurn with the ORIGINAL prompt.
                    // runSession re-invokes the LLM; the system prompt + priorSteps drive it toward
                    // the same actions the user approved. PLAN-04 semantics: user approved the SHAPE.
                    let (code, newSteps) =
                        (Repl.runSingleTurn prompt session.Steps components renderMode).GetAwaiter().GetResult()
                    // Save session — same shape as single-turn save below.
                    let updated =
                        { session with
                            Steps = session.Steps @ newSteps
                            LastActivityAt = DateTimeOffset.UtcNow }
                    let saveCt = System.Threading.CancellationToken.None
                    let saveRes = (components.SessionStore.Save updated saveCt).GetAwaiter().GetResult()
                    match saveRes with
                    | Ok () -> ()
                    | Error e ->
                        eprintfn "WARNING: session save failed: %A" e
                        Log.Warning("Session save failed: {Error}", sprintf "%A" e)
                    code
                | Some PlanGate.Quit, Some _ ->
                    // runPlanTurn returned Error (LLM unreachable / parse failure after 2 retries).
                    1
                | Some PlanGate.Quit, None ->
                    // User typed 'q'.
                    0
                | _ ->
                    // Reject loop exhausted (rejectCount >= maxUserRejects) without acceptance.
                    eprintfn "Plan-mode: %d rejections without acceptance — aborting." rejectCount
                    1
            else
                // ─── Existing dispatch (unchanged from current logic) ────────────────────
                match promptWords with
                | [] ->
                    // Multi-turn: pass the resolved Session and SessionStore to runMultiTurnWithSession.
                    (Repl.runMultiTurnWithSession components renderMode session components.SessionStore).GetAwaiter().GetResult()
                | words ->
                    // Single-turn: thread session.Steps as priorSteps; Save once after this single turn.
                    let prompt = String.concat " " words
                    let (code, newSteps) =
                        (Repl.runSingleTurn prompt session.Steps components renderMode).GetAwaiter().GetResult()
                    // Save the cumulative session even in single-turn mode so --resume <id> can pick up later.
                    let updated =
                        { session with
                            Steps = session.Steps @ newSteps
                            LastActivityAt = DateTimeOffset.UtcNow }
                    let saveCt = System.Threading.CancellationToken.None
                    let saveRes = (components.SessionStore.Save updated saveCt).GetAwaiter().GetResult()
                    match saveRes with
                    | Ok () -> ()
                    | Error e ->
                        eprintfn "WARNING: session save failed: %A" e
                        Log.Warning("Session save failed: {Error}", sprintf "%A" e)
                    code

        Log.CloseAndFlush()
        exitCode
    with
    | :? ArguParseException as e ->
        // --help, --version, and all usage errors (including unknown model via parseForcedModel raise)
        // go through this path. Eprintfn to stderr and exit 2 matches usage-error convention.
        eprintfn "%s" e.Message
        Log.CloseAndFlush()
        2
    | ex ->
        try
            Log.Fatal(ex, "Unhandled exception")
        with _ ->
            ()

        eprintfn "Fatal: %s" ex.Message
        Log.CloseAndFlush()
        1
