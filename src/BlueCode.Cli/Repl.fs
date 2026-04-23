module BlueCode.Cli.Repl

open System
open System.Threading
open System.Threading.Tasks
open Serilog
open Spectre.Console
open BlueCode.Core.Domain
open BlueCode.Core.AgentLoop
open BlueCode.Cli.Rendering
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
let runSingleTurn (prompt: string) (components: AppComponents) (renderMode: RenderMode) : Task<int> =
    task {
        use cts = new CancellationTokenSource()
        let cancelHandler = System.ConsoleCancelEventHandler(fun _ args ->
            args.Cancel <- true           // REQUIRED: prevents immediate process kill
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

            let onStep (step: Step) =
                components.JsonlSink.WriteStep step
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
                    resultRepr)

            let! result =
                try
                    runSession
                        components.Config
                        components.LlmClient
                        components.ToolExecutor
                        onStep
                        prompt
                        cts.Token
                with
                | :? OperationCanceledException ->
                    // Defensive fallback. QwenHttpClient and FsToolExecutor already
                    // map cancellation to Error UserCancelled; this `with` is a
                    // belt-and-suspenders safety net (research § Pattern 7, Pitfall 2).
                    Task.FromResult(Error UserCancelled)

            match result with
            | Ok agentResult ->
                printfn "%s" (renderResult agentResult)
                Log.Information("Session ok: {Steps} steps, model={Model}, log={LogPath}",
                                agentResult.Steps.Length, agentResult.Model, components.LogPath)
                return 0
            | Error UserCancelled ->
                printfn "%s" (renderError UserCancelled)
                Log.Information("Session cancelled by user")
                return 130
            | Error e ->
                printfn "%s" (renderError e)
                Log.Warning("Session error: {Error}", sprintf "%A" e)
                return 1
        finally
            Console.CancelKeyPress.RemoveHandler(cancelHandler)
    }

/// Multi-turn REPL loop (CLI-02). Reads lines from stdin and dispatches each
/// to runSingleTurn. Ctrl+D (ReadLine() = null) and "/exit" both terminate.
/// Per-turn Ctrl+C (SIGINT) cancels the current turn via the existing
/// CancelKeyPress handler in runSingleTurn — after a 130 exit, the loop
/// continues. No cross-turn message history (explicit POST-V1 scope).
///
/// renderMode: threaded from Program.fs CLI flag (Compact or Verbose).
let runMultiTurn (components: AppComponents) (renderMode: RenderMode) : Task<int> =
    task {
        printfn "blueCode — multi-turn mode. Type /exit or press Ctrl+D to quit."
        let mutable lastCode = 0
        let mutable running  = true
        while running do
            printf "\nblueCode> "
            let line = Console.ReadLine()    // null on Ctrl+D / EOF
            match line with
            | null -> running <- false
            | "/exit" -> running <- false
            | s when s.Trim() = "" -> ()
            | prompt ->
                let! code = runSingleTurn prompt components renderMode
                // SIGINT cancels the current turn but keeps the REPL alive.
                // Translate 130 back to 0 for the running tally so the
                // final process exit uses the last "real" completion code.
                lastCode <- if code = 130 then 0 else code
        return lastCode
    }
