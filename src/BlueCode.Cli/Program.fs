module Program

open System
open System.IO
open Serilog
open BlueCode.Cli.Adapters.Logging
open BlueCode.Cli

/// Process entry point (Phase 4). Wires the full single-turn pipeline:
///   1. Logging.configure (MUST be first, before any adapter creation -- research Pitfall 4).
///   2. Read prompt from argv (joined with spaces). No Argu yet -- that's Phase 5 CLI-06.
///   3. Bootstrap components at the current working directory as project root.
///   4. Invoke Repl.runSingleTurn. Unwrap Task<int> synchronously.
///   5. Dispose JsonlSink (via `use _jsonlSink` -- ensures final flush).
///   6. Log.CloseAndFlush before returning.
///
/// Exit codes (from Repl.runSingleTurn):
///   0   -- success
///   1   -- agent error
///   130 -- user cancelled
///
/// Usage error (no args): prints to stderr and exits 2.
[<EntryPoint>]
let main (argv: string array) : int =
    // Step 1 -- configure logging FIRST. Serilog's default logger is a silent
    // no-op; if configure() is skipped, every Log.* call below is discarded.
    configure()

    try
        // Step 2 -- prompt from argv. Phase 4 is single-turn positional only.
        if argv.Length < 1 then
            eprintfn "Usage: blueCode \"<prompt>\""
            eprintfn "Example: blueCode \"list files in the current directory\""
            Log.CloseAndFlush()
            2
        else
            let prompt = String.concat " " argv
            let projectRoot = Directory.GetCurrentDirectory()

            Log.Information("blueCode starting: cwd={Root} prompt.length={Len}",
                            projectRoot, prompt.Length)

            // Step 3-5 -- bootstrap, runSingleTurn, dispose JsonlSink.
            // `use _jsonlSink = components.JsonlSink` binds the JsonlSink value
            // (implements IDisposable) so F#'s `use` invokes Dispose at scope
            // exit. This guarantees the JSONL file is flushed and closed before
            // the process exits, even on exception paths below it.
            let components = CompositionRoot.bootstrap projectRoot
            use _jsonlSink = components.JsonlSink
            Log.Information("Session log: {LogPath}", components.LogPath)

            let exitCode =
                (Repl.runSingleTurn prompt components)
                    .GetAwaiter().GetResult()

            // Step 6 -- flush logger before exit.
            Log.CloseAndFlush()
            exitCode
    with ex ->
        // Any exception outside runSingleTurn's try/with -- log and exit 1.
        // NOTE: runSingleTurn's own try/with already converts OperationCanceledException
        // to Error UserCancelled. This outer catch covers pathological cases
        // (e.g., Logging.configure failure, invalid cwd).
        try
            Log.Fatal(ex, "Unhandled exception before or after runSingleTurn")
        with _ -> ()
        eprintfn "Fatal: %s" ex.Message
        Log.CloseAndFlush()
        1
