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

/// Process entry point (Phase 5). Wires Argu parser for CLI-06 then dispatches
/// to single-turn (prompt present) or multi-turn REPL (no prompt) per CLI-01/CLI-02.
///
/// Exit codes:
///   0   -- success / REPL exited cleanly
///   1   -- agent error
///   2   -- usage error (--help, --version, unknown model, unknown flag)
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
              Trace = isTrace }

        let projectRoot = Directory.GetCurrentDirectory()

        Log.Information(
            "blueCode starting: cwd={Root} mode={Mode}",
            projectRoot,
            (if List.isEmpty promptWords then "repl" else "single")
        )

        let components = bootstrap projectRoot opts
        Log.Information("Context window floor: max_model_len={MaxLen} (lazy per-port probe resolves actual)", components.MaxModelLen)
        use _jsonlSink = components.JsonlSink

        let exitCode =
            match promptWords with
            | [] -> (Repl.runMultiTurn components renderMode).GetAwaiter().GetResult()
            | words ->
                let prompt = String.concat " " words
                (Repl.runSingleTurn prompt components renderMode).GetAwaiter().GetResult()

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
