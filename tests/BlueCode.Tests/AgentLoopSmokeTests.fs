module BlueCode.Tests.AgentLoopSmokeTests

open System
open System.IO
open System.Threading
open Expecto
open BlueCode.Core.Domain
open BlueCode.Core.AgentLoop
open BlueCode.Cli.CompositionRoot

/// Gate: only runs when BLUECODE_AGENT_SMOKE=1 is set and a Qwen server is
/// serving at localhost:8000. Proves SC-1 end-to-end:
///   blueCode "list files in the current directory" → ≤5 steps → final answer.
let private smokeEnabled () : bool =
    match Environment.GetEnvironmentVariable("BLUECODE_AGENT_SMOKE") with
    | "1" -> true
    | _   -> false

let private smokeTest () =
    testCaseAsync "live Qwen: 'list files' completes ≤5 steps with final answer" <| async {
        let tempRoot = Path.Combine(Path.GetTempPath(), sprintf "bluecode-smoke-%s" (Guid.NewGuid().ToString("N")))
        Directory.CreateDirectory(tempRoot) |> ignore
        // Drop a known file so list_dir has something to return.
        File.WriteAllText(Path.Combine(tempRoot, "README.md"), "hello")

        let c = bootstrap tempRoot defaultCliOptions
        try
            let captured = ResizeArray<Step>()
            let onStep (s: Step) =
                c.JsonlSink.WriteStep s
                captured.Add(s)

            use cts = new CancellationTokenSource(TimeSpan.FromMinutes(3.0))
            let! result =
                runSession c.Config c.LlmClient c.ToolExecutor
                    onStep "list files in the current directory" cts.Token
                |> Async.AwaitTask

            match result with
            | Ok r ->
                Expect.isTrue (r.LoopCount <= 5) (sprintf "LoopCount=%d must be ≤5" r.LoopCount)
                Expect.isNotEmpty r.FinalAnswer "final answer non-empty"
                Expect.isGreaterThanOrEqual r.Steps.Length 1 "at least one step recorded"
                // JSONL file exists and has N lines matching N steps
                let lines = File.ReadAllLines(c.LogPath)
                Expect.equal lines.Length captured.Count "JSONL lines == captured steps"
            | Error e ->
                failtestf "expected Ok result, got: %A (log: %s)" e c.LogPath
        finally
            (c.JsonlSink :> IDisposable).Dispose()
    }

let private disabledStub () =
    testCase "live Qwen smoke: disabled (set BLUECODE_AGENT_SMOKE=1 to enable)" <| fun _ ->
        ()

[<Tests>]
let tests =
    testList "AgentLoop.Smoke" [
        if smokeEnabled() then smokeTest() else disabledStub()
    ]
