module BlueCode.Cli.Repl

open System
open System.Threading
open System.Threading.Tasks
open Serilog
open BlueCode.Core.Domain
open BlueCode.Core.AgentLoop
open BlueCode.Cli.Rendering
open BlueCode.Cli.CompositionRoot

/// Single-turn REPL entry. Phase 4 scope: ONE prompt, ONE turn, exit.
/// Phase 5 (CLI-02) extends this to a multi-turn loop.
///
/// Responsibilities:
///   1. Register Ctrl+C handler -> cancel the CancellationTokenSource gracefully.
///   2. Invoke AgentLoop.runSession with:
///        onStep callback -> components.JsonlSink.WriteStep (per-step JSONL write, SC-6).
///   3. On Ok: write final answer to stdout via renderResult. Exit 0.
///   4. On Error: write renderError to stdout. Exit 1 for most errors, 130 for UserCancelled.
///   5. Defensive catch: any OperationCanceledException escaping runSession -> treat as UserCancelled.
///
/// Exit code convention:
///   0   - successful turn
///   1   - agent error (MaxLoopsExceeded, LoopGuardTripped, Llm*, Tool*, etc.)
///   130 - user-cancelled (SIGINT, Ctrl+C - POSIX 128+2)
let runSingleTurn (prompt: string) (components: AppComponents) : Task<int> =
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
            let onStep (step: Step) =
                components.JsonlSink.WriteStep step
                Log.Debug("Step {Number}: action={Action} duration={DurationMs}ms",
                          step.StepNumber, step.Action, step.DurationMs)

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
