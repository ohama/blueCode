module BlueCode.Tests.ReplTests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Expecto
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Core.AgentLoop
open BlueCode.Cli.Rendering
open BlueCode.Cli.CompositionRoot

// ── Stub helpers ─────────────────────────────────────────────────────────────

/// Build a fake ILlmClient that returns scripted responses per call (FIFO queue).
let private stubLlm (responses: Result<LlmOutput, AgentError> list) : ILlmClient =
    let q = System.Collections.Generic.Queue<_>(responses)
    { new ILlmClient with
        member _.CompleteAsync _messages _model _ct =
            if q.Count = 0 then failwith "stubLlm: response queue exhausted — test bug"
            Task.FromResult(q.Dequeue()) }

/// Build a fake IToolExecutor that always returns Ok (Success "stub-output").
let private stubToolsOk : IToolExecutor =
    { new IToolExecutor with
        member _.ExecuteAsync _tool _ct = Task.FromResult(Ok (Success "stub-output")) }

/// Helper: build a ToolCall LlmOutput with raw JSON input.
let private toolCall (action: string) (rawJson: string) : LlmOutput =
    ToolCall (ToolName action, ToolInput (Map.ofList [ ("_raw", rawJson) ]))

// ── Tests ─────────────────────────────────────────────────────────────────────

// Note: NO [<Tests>] attribute — this project uses explicit rootTests registration
// in RouterTests.fs (STATE.md Accumulated Decisions, 04-02). [<Tests>] auto-discovery
// is NOT used. Test registration is done in RouterTests.fs rootTests list.

let tests =
    testSequenced <| testList "Repl" [

        // Use testCase (synchronous) to avoid Console.SetOut interleaving between
        // adjacent tests. Both tests redirect Console.Out; running them as fully
        // synchronous testCases ensures no concurrent stdout capture.
        testCase "runSingleTurn: onStep prints per-step Compact line to stdout with 'ms]' DurationMs marker" <| fun () ->
            // Arrange: script LLM to return one ToolCall then a FinalAnswer = 2 Steps
            let llm = stubLlm [
                Ok (toolCall "list_dir" "{\"path\":\".\"}")
                Ok (FinalAnswer "done")
            ]
            let tempRoot = Path.Combine(Path.GetTempPath(), sprintf "bluecode-replt-%s" (Guid.NewGuid().ToString("N")))
            Directory.CreateDirectory(tempRoot) |> ignore
            let sinkPath = Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
            use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)
            let components : BlueCode.Cli.CompositionRoot.AppComponents = {
                LlmClient    = llm
                ToolExecutor = stubToolsOk
                JsonlSink    = sink
                Config       = { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test-prompt"; ForcedModel = None }
                ProjectRoot  = tempRoot
                LogPath      = sinkPath
            }

            // Act: capture stdout while runSingleTurn executes
            let originalOut = Console.Out
            use sw = new StringWriter()
            Console.SetOut(sw)
            try
                let exitCode =
                    BlueCode.Cli.Repl.runSingleTurn "stub prompt" components Compact
                    |> fun t -> t.GetAwaiter().GetResult()
                Console.Out.Flush()
                let captured = sw.ToString()

                // Assert: exit code
                Expect.equal exitCode 0 "runSingleTurn exit code on Ok result"

                // Assert: at least 2 stdout lines containing 'ms]' DurationMs marker
                // (one per Step — ToolCall step + FinalAnswer step = 2 steps)
                let msLines =
                    captured.Split([| '\n' |])
                    |> Array.filter (fun l -> l.Contains("ms]"))
                Expect.isGreaterThanOrEqual msLines.Length 2
                    (sprintf "expected at least 2 stdout lines with 'ms]' marker; captured:\n%s" captured)

                // Assert: lines match Compact format '> ... [..., Nms]'
                let compactLines =
                    captured.Split([| '\n' |])
                    |> Array.filter (fun l -> l.StartsWith("> ") && l.Contains("ms]"))
                Expect.isGreaterThanOrEqual compactLines.Length 2
                    (sprintf "expected at least 2 Compact format lines '> ... [..., Nms]'; captured:\n%s" captured)
            finally
                Console.SetOut(originalOut)

        testCase "runMultiTurn: stdin '/exit' exits cleanly with code 0 and prints banner" <| fun () ->
            // Arrange: redirect stdin to simulate user typing "/exit" immediately
            let originalIn  = Console.In
            let originalOut = Console.Out
            use stdinReader  = new StringReader("/exit\n")
            use stdoutWriter = new StringWriter()
            Console.SetIn(stdinReader)
            Console.SetOut(stdoutWriter)
            let tempRoot = Path.Combine(Path.GetTempPath(), sprintf "bluecode-replmt-%s" (Guid.NewGuid().ToString("N")))
            Directory.CreateDirectory(tempRoot) |> ignore
            let sinkPath = Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
            use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)
            let components : BlueCode.Cli.CompositionRoot.AppComponents = {
                LlmClient    = stubLlm []    // no LLM calls expected — /exit before any prompt
                ToolExecutor = stubToolsOk
                JsonlSink    = sink
                Config       = { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test-prompt"; ForcedModel = None }
                ProjectRoot  = tempRoot
                LogPath      = sinkPath
            }
            try
                // Act: run synchronously to avoid Console.Out interleaving
                let exitCode =
                    BlueCode.Cli.Repl.runMultiTurn components Compact
                    |> fun t -> t.GetAwaiter().GetResult()
                Console.Out.Flush()
                let captured = stdoutWriter.ToString()

                // Assert: exits cleanly with 0
                Expect.equal exitCode 0 "runMultiTurn exit code when /exit is first input"

                // Assert: prints the banner
                Expect.stringContains captured "blueCode — multi-turn mode"
                    "banner 'blueCode — multi-turn mode' should appear in stdout"
            finally
                Console.SetIn(originalIn)
                Console.SetOut(originalOut)

        testCase "runSingleTurn Verbose mode: onStep prints multi-line verbose output with [Step, thought:, action:, result: labels" <| fun () ->
            // Arrange: script LLM to return one FinalAnswer = 1 Step
            let llm = stubLlm [
                Ok (FinalAnswer "verbose done")
            ]
            let tempRoot = Path.Combine(Path.GetTempPath(), sprintf "bluecode-replv-%s" (Guid.NewGuid().ToString("N")))
            Directory.CreateDirectory(tempRoot) |> ignore
            let sinkPath = Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
            use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)
            let components : BlueCode.Cli.CompositionRoot.AppComponents = {
                LlmClient    = llm
                ToolExecutor = stubToolsOk
                JsonlSink    = sink
                Config       = { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test-prompt"; ForcedModel = None }
                ProjectRoot  = tempRoot
                LogPath      = sinkPath
            }
            let originalOut = Console.Out
            use sw = new StringWriter()
            Console.SetOut(sw)
            try
                let exitCode =
                    BlueCode.Cli.Repl.runSingleTurn "stub prompt" components Verbose
                    |> fun t -> t.GetAwaiter().GetResult()
                Console.Out.Flush()
                let captured = sw.ToString()

                Expect.equal exitCode 0 "Verbose runSingleTurn exit code on Ok result"

                // Verbose format: "[Step N] (status, Nms)\n  thought: ...\n  action: ...\n  result: ..."
                Expect.stringContains captured "[Step" "Verbose output should contain '[Step' banner"
                Expect.stringContains captured "thought:" "Verbose output should contain 'thought:' label"
                Expect.stringContains captured "action:" "Verbose output should contain 'action:' label"
                Expect.stringContains captured "result:" "Verbose output should contain 'result:' label"

                // Negative: Verbose should NOT show compact one-liner format ("> ... ms]")
                let compactLines =
                    captured.Split([| '\n' |])
                    |> Array.filter (fun l -> l.StartsWith("> ") && l.Contains("ms]"))
                Expect.equal compactLines.Length 0
                    (sprintf "Verbose mode should not produce compact '> ... ms]' lines; captured:\n%s" captured)
            finally
                Console.SetOut(originalOut)

        testCase "runSingleTurn Compact mode: onStep does NOT print thought: label" <| fun () ->
            // Arrange: script LLM to return one FinalAnswer = 1 Step
            let llm = stubLlm [
                Ok (FinalAnswer "compact done")
            ]
            let tempRoot = Path.Combine(Path.GetTempPath(), sprintf "bluecode-replc-%s" (Guid.NewGuid().ToString("N")))
            Directory.CreateDirectory(tempRoot) |> ignore
            let sinkPath = Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
            use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)
            let components : BlueCode.Cli.CompositionRoot.AppComponents = {
                LlmClient    = llm
                ToolExecutor = stubToolsOk
                JsonlSink    = sink
                Config       = { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test-prompt"; ForcedModel = None }
                ProjectRoot  = tempRoot
                LogPath      = sinkPath
            }
            let originalOut = Console.Out
            use sw = new StringWriter()
            Console.SetOut(sw)
            try
                let exitCode =
                    BlueCode.Cli.Repl.runSingleTurn "stub prompt" components Compact
                    |> fun t -> t.GetAwaiter().GetResult()
                Console.Out.Flush()
                let captured = sw.ToString()

                Expect.equal exitCode 0 "Compact runSingleTurn exit code on Ok result"

                // Compact mode MUST NOT contain verbose labels
                let hasThought = captured.Contains("thought:")
                Expect.isFalse hasThought
                    (sprintf "Compact mode should not contain 'thought:' label; captured:\n%s" captured)

                // Compact mode MUST contain the 'ms]' marker on step lines
                let msLines =
                    captured.Split([| '\n' |])
                    |> Array.filter (fun l -> l.Contains("ms]"))
                Expect.isGreaterThanOrEqual msLines.Length 1
                    (sprintf "Compact mode should have at least 1 line with 'ms]' marker; captured:\n%s" captured)
            finally
                Console.SetOut(originalOut)

    ]  // end testSequenced
