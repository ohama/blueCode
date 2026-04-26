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

        // NEW (15-02): mutually-exclusive validation. Reject BOTH-set; either-or-neither is fine.
        // Done POST-parse BEFORE bootstrap so we don't waste bootstrap cycles.
        match resumeId, isNewSession with
        | Some _, true ->
            eprintfn "ERROR: conflicting flags: --resume and --new-session cannot be used together."
            Log.CloseAndFlush()
            exit 2
        | _ -> ()

        // parseForcedModel raises on invalid model string; wrap as usage error (exit 2).
        let forcedModel =
            try
                parseForcedModel forcedStr
            with ex ->
                eprintfn "ERROR: %s" ex.Message
                Log.CloseAndFlush()
                exit 2

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
              NewSession = isNewSession }

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
