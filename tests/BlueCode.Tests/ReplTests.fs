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
open BlueCode.Tests.MockHelpers

/// Build a fake ILlmClient that returns scripted responses per call (FIFO queue).
let private stubLlm (responses: Result<LlmResponse, AgentError> list) : ILlmClient =
    let q = System.Collections.Generic.Queue<_>(responses)

    { new ILlmClient with
        member _.CompleteAsync _messages _model _ct =
            if q.Count = 0 then
                failwith "stubLlm: response queue exhausted — test bug"

            Task.FromResult(q.Dequeue()) }

/// Build a fake IToolExecutor that always returns Ok (Success "stub-output").
let private stubToolsOk: IToolExecutor =
    { new IToolExecutor with
        member _.ExecuteAsync _tool _ct =
            Task.FromResult(Ok(Success "stub-output")) }

/// Helper: build a ToolCall LlmOutput with raw JSON input.
let private toolCall (action: string) (rawJson: string) : LlmOutput =
    ToolCall(ToolName action, ToolInput(Map.ofList [ ("_raw", rawJson) ]))

// ── Tests ─────────────────────────────────────────────────────────────────────

// Note: NO [<Tests>] attribute — this project uses explicit rootTests registration
// in RouterTests.fs (STATE.md Accumulated Decisions, 04-02). [<Tests>] auto-discovery
// is NOT used. Test registration is done in RouterTests.fs rootTests list.

let tests =
    testSequenced
    <| testList
        "Repl"
        [

          // Use testCase (synchronous) to avoid Console.SetOut interleaving between
          // adjacent tests. Both tests redirect Console.Out; running them as fully
          // synchronous testCases ensures no concurrent stdout capture.
          testCase "runSingleTurn: onStep prints per-step Compact line to stdout with 'ms]' DurationMs marker"
          <| fun () ->
              // Arrange: script LLM to return one ToolCall then a FinalAnswer = 2 Steps
              let llm =
                  stubLlm [ makeMockResponse "listing files" (toolCall "list_dir" "{\"path\":\".\"}"); makeMockResponse "finalizing" (FinalAnswer "done") ]

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-replt-%s" (Guid.NewGuid().ToString("N")))

              Directory.CreateDirectory(tempRoot) |> ignore

              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))

              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: BlueCode.Cli.CompositionRoot.AppComponents =
                  { LlmClient = llm
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5
                        ContextCapacity = 3
                        SystemPrompt = "test-prompt"
                        ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              // Act: capture stdout while runSingleTurn executes
              let originalOut = Console.Out
              use sw = new StringWriter()
              Console.SetOut(sw)

              try
                  let (exitCode, _) =
                      BlueCode.Cli.Repl.runSingleTurn "stub prompt" [] components Compact
                      |> fun t -> t.GetAwaiter().GetResult()

                  Console.Out.Flush()
                  let captured = sw.ToString()

                  // Assert: exit code
                  Expect.equal exitCode 0 "runSingleTurn exit code on Ok result"

                  // Assert: at least 2 stdout lines containing 'ms]' DurationMs marker
                  // (one per Step — ToolCall step + FinalAnswer step = 2 steps)
                  let msLines =
                      captured.Split([| '\n' |]) |> Array.filter (fun l -> l.Contains("ms]"))

                  Expect.isGreaterThanOrEqual
                      msLines.Length
                      2
                      (sprintf "expected at least 2 stdout lines with 'ms]' marker; captured:\n%s" captured)

                  // Assert: lines match Compact format '> ... [..., Nms]'
                  let compactLines =
                      captured.Split([| '\n' |])
                      |> Array.filter (fun l -> l.StartsWith("> ") && l.Contains("ms]"))

                  Expect.isGreaterThanOrEqual
                      compactLines.Length
                      2
                      (sprintf "expected at least 2 Compact format lines '> ... [..., Nms]'; captured:\n%s" captured)
              finally
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: stdin '/exit' exits cleanly with code 0 and prints banner"
          <| fun () ->
              // Arrange: redirect stdin to simulate user typing "/exit" immediately
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-replmt-%s" (Guid.NewGuid().ToString("N")))

              Directory.CreateDirectory(tempRoot) |> ignore

              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))

              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: BlueCode.Cli.CompositionRoot.AppComponents =
                  { LlmClient = stubLlm [] // no LLM calls expected — /exit before any prompt
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5
                        ContextCapacity = 3
                        SystemPrompt = "test-prompt"
                        ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

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
                  Expect.stringContains
                      captured
                      "blueCode — multi-turn mode"
                      "banner 'blueCode — multi-turn mode' should appear in stdout"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase
              "runSingleTurn Verbose mode: onStep prints multi-line verbose output with [Step, thought:, action:, result: labels"
          <| fun () ->
              // Arrange: script LLM to return one FinalAnswer = 1 Step
              let llm = stubLlm [ makeMockResponse "finalizing verbose" (FinalAnswer "verbose done") ]

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-replv-%s" (Guid.NewGuid().ToString("N")))

              Directory.CreateDirectory(tempRoot) |> ignore

              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))

              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: BlueCode.Cli.CompositionRoot.AppComponents =
                  { LlmClient = llm
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5
                        ContextCapacity = 3
                        SystemPrompt = "test-prompt"
                        ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              let originalOut = Console.Out
              use sw = new StringWriter()
              Console.SetOut(sw)

              try
                  let (exitCode, _) =
                      BlueCode.Cli.Repl.runSingleTurn "stub prompt" [] components Verbose
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

                  Expect.equal
                      compactLines.Length
                      0
                      (sprintf "Verbose mode should not produce compact '> ... ms]' lines; captured:\n%s" captured)
              finally
                  Console.SetOut(originalOut)

          testCase "runSingleTurn Compact mode: onStep does NOT print thought: label"
          <| fun () ->
              // Arrange: script LLM to return one FinalAnswer = 1 Step
              let llm = stubLlm [ makeMockResponse "finalizing compact" (FinalAnswer "compact done") ]

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-replc-%s" (Guid.NewGuid().ToString("N")))

              Directory.CreateDirectory(tempRoot) |> ignore

              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))

              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: BlueCode.Cli.CompositionRoot.AppComponents =
                  { LlmClient = llm
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5
                        ContextCapacity = 3
                        SystemPrompt = "test-prompt"
                        ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              let originalOut = Console.Out
              use sw = new StringWriter()
              Console.SetOut(sw)

              try
                  let (exitCode, _) =
                      BlueCode.Cli.Repl.runSingleTurn "stub prompt" [] components Compact
                      |> fun t -> t.GetAwaiter().GetResult()

                  Console.Out.Flush()
                  let captured = sw.ToString()

                  Expect.equal exitCode 0 "Compact runSingleTurn exit code on Ok result"

                  // Compact mode MUST NOT contain verbose labels
                  let hasThought = captured.Contains("thought:")

                  Expect.isFalse
                      hasThought
                      (sprintf "Compact mode should not contain 'thought:' label; captured:\n%s" captured)

                  // Compact mode MUST contain the 'ms]' marker on step lines
                  let msLines =
                      captured.Split([| '\n' |]) |> Array.filter (fun l -> l.Contains("ms]"))

                  Expect.isGreaterThanOrEqual
                      msLines.Length
                      1
                      (sprintf "Compact mode should have at least 1 line with 'ms]' marker; captured:\n%s" captured)
              finally
                  Console.SetOut(originalOut)

          testCase "multi-turn: turn 2 sees turn 1 Steps as prior context (Phase 15 SC1)"
          <| fun () ->
              // Capture messages passed to each LLM call.
              let capturedMessageBatches = System.Collections.Generic.List<Message list>()

              let scriptedResponses =
                  System.Collections.Generic.Queue<Result<LlmResponse, AgentError>>(
                      [ // Turn 1: ToolCall list_dir then FinalAnswer "turn1 done"
                        makeMockResponse "listing" (toolCall "list_dir" "{\"path\":\".\"}");
                        makeMockResponse "finishing turn 1" (FinalAnswer "turn1 done");
                        // Turn 2: directly FinalAnswer (no tool call).
                        makeMockResponse "finishing turn 2" (FinalAnswer "turn2 done") ])

              let capturingLlm =
                  { new ILlmClient with
                      member _.CompleteAsync messages _model _ct =
                          capturedMessageBatches.Add(messages)
                          if scriptedResponses.Count = 0 then
                              failwith "capturingLlm: queue exhausted"
                          Task.FromResult(scriptedResponses.Dequeue()) }

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bc-mtt-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let logPath = Path.Combine(tempRoot, "session.jsonl")

              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(logPath)

              let components: AppComponents =
                  { LlmClient = capturingLlm
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5
                        ContextCapacity = 3
                        SystemPrompt = "test system prompt"
                        ForcedModel = Some Qwen122B }
                    ProjectRoot = tempRoot
                    LogPath = logPath
                    MaxModelLen = 8192 }

              try
                  // Turn 1: priorSteps = []
                  let (code1, stepsTurn1) =
                      BlueCode.Cli.Repl.runSingleTurn "first prompt" [] components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Expect.equal code1 0 "turn 1 should exit 0"
                  Expect.isGreaterThan stepsTurn1.Length 0 "turn 1 must produce at least 1 step"

                  // Turn 2: priorSteps = stepsTurn1 — this is the wiring under test.
                  let (code2, _) =
                      BlueCode.Cli.Repl.runSingleTurn "second prompt" stepsTurn1 components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Expect.equal code2 0 "turn 2 should exit 0"

                  // Turn 1 made TWO LLM calls (ToolCall response, then FinalAnswer response).
                  // Turn 2 made ONE LLM call (FinalAnswer response).
                  // So capturedMessageBatches has 3 entries.
                  Expect.equal capturedMessageBatches.Count 3 "expected 3 LLM calls across both turns"

                  // The 3rd call is turn 2's first (and only) LLM call.
                  let turn2Messages = capturedMessageBatches.[2]
                  // turn2Messages MUST include an assistant message containing "list_dir"
                  // (echoing turn 1's tool action) AND a user/observation message containing
                  // "stub-output" (turn 1's tool result via stubToolsOk).
                  let turn2Concat =
                      turn2Messages
                      |> List.map (fun m -> m.Content)
                      |> String.concat "\n"

                  Expect.stringContains turn2Concat "list_dir"
                      "turn 2 messages must contain turn 1's tool name (priorSteps replay)"
                  Expect.stringContains turn2Concat "stub-output"
                      "turn 2 messages must contain turn 1's tool result (priorSteps replay)"
                  Expect.stringContains turn2Concat "second prompt"
                      "turn 2 messages contain the new user prompt"
              finally
                  try
                      if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)
                  with _ -> ()

          testCase "runSingleTurn: 80% context warning fires exactly once per turn when threshold crossed"
          <| fun () ->
              // Setup: MaxModelLen = 10 -> 80% threshold = 10 * 4 * 0.80 = 32 chars.
              // Even a single ToolCall step's action+result repr (~100 chars) crosses it.
              // After step 1 the accumulator exceeds 32 chars and the warning fires.
              // Third step is FinalAnswer to end the turn. Warning must fire only ONCE.
              let llm =
                  stubLlm
                      [ makeMockResponse "listing directory" (toolCall "list_dir" "{\"path\":\".\"}")
                        makeMockResponse "reading readme" (toolCall "read_file" "{\"path\":\"README.md\"}")
                        makeMockResponse "summarizing" (FinalAnswer "context warning test done") ]

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-replw-%s" (Guid.NewGuid().ToString("N")))

              Directory.CreateDirectory(tempRoot) |> ignore

              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))

              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: BlueCode.Cli.CompositionRoot.AppComponents =
                  { LlmClient = llm
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5
                        ContextCapacity = 3
                        SystemPrompt = "test-prompt"
                        ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 10 // tiny — threshold = 32 chars, crossed by first step's repr
                  }

              let originalOut = Console.Out
              use sw = new StringWriter()
              Console.SetOut(sw)

              try
                  let (exitCode, _) =
                      BlueCode.Cli.Repl.runSingleTurn "stub prompt" [] components Compact
                      |> fun t -> t.GetAwaiter().GetResult()

                  Console.Out.Flush()
                  let captured = sw.ToString()

                  Expect.equal exitCode 0 "runSingleTurn exit code on Ok result with warning"

                  // The WARNING line must appear in stdout
                  Expect.stringContains
                      captured
                      "WARNING: context at 80%"
                      (sprintf "Expected 80%% warning in stdout; captured:\n%s" captured)

                  // The WARNING must appear EXACTLY ONCE (gate prevents repeated firing)
                  let warningLines =
                      captured.Split([| '\n' |])
                      |> Array.filter (fun l -> l.Contains("WARNING: context at 80%"))

                  Expect.equal
                      warningLines.Length
                      1
                      (sprintf "WARNING should appear exactly once per turn; captured:\n%s" captured)
              finally
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/help' prints 9-command help without LLM call" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/help\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-help-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []   // 0 LLM calls expected — /help is in-process
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "exit code 0"
                  Expect.stringContains captured "/help" "help text mentions /help"
                  Expect.stringContains captured "/sessions" "help lists /sessions stub"
                  Expect.stringContains captured "[coming in v2.5]" "help marks future commands"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/status' prints session id, model, steps, chars" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/status\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-stat-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = Some Qwen122B }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "exit code 0"
                  Expect.stringContains captured "session:" "status shows session label"
                  Expect.stringContains captured "model:" "status shows model label"
                  Expect.stringContains captured "steps:    0" "fresh session has 0 steps"
                  Expect.stringContains captured "122b" "model name printed"
                  Expect.stringContains captured "[floor; probed on first LLM call]" "MaxModelLen floor disclaimer"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/clear' creates new session id, prints confirmation, leaves old jsonl untouched" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/clear\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-clr-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "exit code 0"
                  Expect.stringContains captured "Session cleared" "clear confirmation text"
                  Expect.stringContains captured "New session:" "new id label"
                  // The captured stdout contains TWO session ids: the banner's initial id, then the post-clear id.
                  // Assert that the second occurrence differs from the first (id rotation actually happened).
                  let lines = captured.Split([| '\n' |])
                  let bannerSessionLines = lines |> Array.filter (fun l -> l.Contains("Session: ") && not (l.Contains("New session")))
                  let clearSessionLines  = lines |> Array.filter (fun l -> l.Contains("New session:"))
                  Expect.isGreaterThan bannerSessionLines.Length 0 "banner session line present"
                  Expect.isGreaterThan clearSessionLines.Length 0 "post-clear session line present"
                  // Pull the IDs out and compare
                  let bannerId =
                      bannerSessionLines.[0].Substring(bannerSessionLines.[0].IndexOf("Session:") + "Session:".Length).Trim()
                  let clearId =
                      clearSessionLines.[0].Substring(clearSessionLines.[0].IndexOf("New session:") + "New session:".Length).Trim()
                  Expect.notEqual bannerId clearId "session id rotated by /clear"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/quit' exits cleanly with code 0 (alias of /exit)" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/quit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-quit-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Expect.equal exitCode 0 "/quit must exit with code 0 (graceful, alias of /exit)"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: remaining future-stub command (/edit only) prints 'not yet implemented' without crashing" <| fun () ->
              // Phase 33 update: /plan is now live (toggle handler — tested separately in Plan 33-02).
              // Only /edit (Phase 34) remains stubbed.
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/edit\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-stub-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []   // future stub must not call LLM
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "exit code 0 — remaining future-stub does not crash REPL"
                  // Exactly 1 'not yet implemented' line expected (only /edit).
                  let stubLines =
                      captured.Split([| '\n' |])
                      |> Array.filter (fun l -> l.Contains("not yet implemented"))
                  Expect.equal stubLines.Length 1
                      (sprintf "expected exactly 1 'not yet implemented' line (/edit only); captured:\n%s" captured)
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/sessions' lists header + zero or more rows; no LLM call" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/sessions\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-ls-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []   // /sessions is in-process — 0 LLM calls
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "exit code 0"
                  // Either "no sessions found" (empty dir) OR a header line + rows.
                  // The user's real ~/.bluecode/sessions/ may have files (research § Q9: 534 sessions
                  // observed); in the test environment, content varies. Assert one or the other:
                  let hasEmpty = captured.Contains("no sessions found")
                  let hasHeader = captured.Contains("session id") && captured.Contains("first thought")
                  Expect.isTrue (hasEmpty || hasHeader)
                      (sprintf "expected either 'no sessions found' or a header row; captured:\n%s" captured)
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/resume' (no arg) prints usage hint without crashing" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/resume\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-r0-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "exit code 0 — empty /resume arg does not crash"
                  Expect.stringContains captured "usage: /resume" "usage hint printed"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/resume <unknown>' prints SessionNotFound friendly error; REPL continues" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              // Use a guaranteed-unique unknown id (32-N hex prefix matches our pattern).
              let unknownId = sprintf "ghost-%s" (Guid.NewGuid().ToString("N"))
              use stdinReader = new StringReader(sprintf "/resume %s\n/exit\n" unknownId)
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-runk-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "exit code 0 — unknown id does not exit REPL"
                  Expect.stringContains captured "Session not found:" "SessionNotFound friendly message printed"
                  Expect.stringContains captured unknownId "the unknown id is echoed in the error"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/resume <known>' swaps currentSession; subsequent prompt sees resumed steps" <| fun () ->
              // Pre-write a real session to disk via FileSessionStore.Save, then /resume it.
              // After resume, send a prompt — the LLM stub captures messages received.
              // Resumed session has 2 prior steps; the LLM should see those in priorSteps.
              let originalIn = Console.In
              let originalOut = Console.Out

              let preIdStr = sprintf "preset-%s" (Guid.NewGuid().ToString("N"))
              let preSession : Session =
                  let toolCall = ToolCall (ToolName "list_dir", ToolInput (Map.ofList [("_raw", "{\"path\":\".\"}")]))
                  let mkS n action =
                      { StepNumber = n
                        Thought = Thought (sprintf "preset thought %d" n)
                        Action = action
                        ToolResult = match action with FinalAnswer _ -> None | _ -> Some (Success "ok")
                        Status = StepSuccess
                        ModelUsed = Qwen122B
                        StartedAt = DateTimeOffset.MinValue
                        EndedAt = DateTimeOffset.MinValue
                        DurationMs = 1L }
                  { Id = SessionId preIdStr
                    Steps = [ mkS 1 toolCall; mkS 2 (FinalAnswer "preset done") ]
                    CreatedAt = DateTimeOffset.UtcNow
                    LastActivityAt = DateTimeOffset.UtcNow }
              let prePath = BlueCode.Cli.Adapters.FileSessionStore.buildSessionPath preSession.Id

              try
                  // Write the preset session to disk (cleanup in finally).
                  let preStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                  (preStore.Save preSession CancellationToken.None).GetAwaiter().GetResult() |> ignore

                  // Capture the LLM messages so we can assert priorSteps were threaded.
                  let capturedMessages = ResizeArray<list<Message>>()
                  let capturingLlm =
                      let q = System.Collections.Generic.Queue<Result<LlmResponse, AgentError>>()
                      q.Enqueue (makeMockResponse "ok" (FinalAnswer "post-resume answer"))
                      { new ILlmClient with
                          member _.CompleteAsync messages _model _ct =
                              capturedMessages.Add messages
                              if q.Count = 0 then failwith "queue exhausted"
                              Task.FromResult(q.Dequeue()) }

                  use stdinReader = new StringReader(sprintf "/resume %s\nhello after resume\n/exit\n" preIdStr)
                  use stdoutWriter = new StringWriter()
                  Console.SetIn(stdinReader)
                  Console.SetOut(stdoutWriter)

                  let tempRoot =
                      Path.Combine(Path.GetTempPath(), sprintf "bluecode-rok-%s" (Guid.NewGuid().ToString("N")))
                  Directory.CreateDirectory(tempRoot) |> ignore
                  let sinkPath =
                      Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
                  use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

                  let components: AppComponents =
                      { LlmClient = capturingLlm
                        ToolExecutor = stubToolsOk
                        SessionStore = preStore
                        JsonlSink = sink
                        Config =
                          { MaxLoops = 5; ContextCapacity = 5; SystemPrompt = "test"; ForcedModel = None }
                        ProjectRoot = tempRoot
                        LogPath = sinkPath
                        MaxModelLen = 8192 }

                  try
                      let exitCode =
                          BlueCode.Cli.Repl.runMultiTurn components Compact
                          |> fun t -> t.GetAwaiter().GetResult()
                      Console.Out.Flush()
                      let captured = stdoutWriter.ToString()
                      Expect.equal exitCode 0 "exit code 0"
                      // Confirmation message visible:
                      Expect.stringContains captured "Resumed session:" "resume confirmation printed"
                      Expect.stringContains captured preIdStr "resumed session id echoed"
                      Expect.stringContains captured "(2 steps)" "step count from loaded session printed"
                      // The LLM was called once (for "hello after resume").
                      Expect.equal capturedMessages.Count 1 "LLM called exactly once after resume"
                      // The messages list should reflect the resumed session's prior steps —
                      // a non-trivial message count (system + prior turns + user prompt) > 2.
                      // We don't assert exact content (priorSteps formatting is AgentLoop's job),
                      // only that messages were threaded (count > 2 implies prior steps included).
                      let msgs = capturedMessages.[0]
                      Expect.isGreaterThan msgs.Length 2
                          (sprintf "expected >2 messages (system + prior steps + new user prompt); got %d" msgs.Length)
                  finally
                      Console.SetIn(originalIn)
                      Console.SetOut(originalOut)
              finally
                  // Cleanup the pre-written session jsonl.
                  try if File.Exists prePath then File.Delete prePath with _ -> ()

          testCase "runMultiTurn: '/resume <corrupt>' prints SessionCorrupt friendly error; REPL continues" <| fun () ->
              // Plant a corrupt session at a known path, /resume it, expect SessionCorrupt path.
              let originalIn = Console.In
              let originalOut = Console.Out
              let corruptIdStr = sprintf "corrupt-%s" (Guid.NewGuid().ToString("N"))
              let corruptPath = BlueCode.Cli.Adapters.FileSessionStore.buildSessionPath (SessionId corruptIdStr)

              try
                  // Plant garbage at the path.
                  File.WriteAllText(corruptPath, "this is not json\n{also garbage}\n")

                  use stdinReader = new StringReader(sprintf "/resume %s\n/exit\n" corruptIdStr)
                  use stdoutWriter = new StringWriter()
                  Console.SetIn(stdinReader)
                  Console.SetOut(stdoutWriter)

                  let tempRoot =
                      Path.Combine(Path.GetTempPath(), sprintf "bluecode-rcrp-%s" (Guid.NewGuid().ToString("N")))
                  Directory.CreateDirectory(tempRoot) |> ignore
                  let sinkPath =
                      Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
                  use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

                  let components: AppComponents =
                      { LlmClient = stubLlm []
                        ToolExecutor = stubToolsOk
                        SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                        JsonlSink = sink
                        Config =
                          { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = None }
                        ProjectRoot = tempRoot
                        LogPath = sinkPath
                        MaxModelLen = 8192 }

                  try
                      let exitCode =
                          BlueCode.Cli.Repl.runMultiTurn components Compact
                          |> fun t -> t.GetAwaiter().GetResult()
                      Console.Out.Flush()
                      let captured = stdoutWriter.ToString()
                      Expect.equal exitCode 0 "exit code 0 — corrupt session does not exit REPL"
                      Expect.stringContains captured "Session file corrupt:" "SessionCorrupt friendly message printed"
                  finally
                      Console.SetIn(originalIn)
                      Console.SetOut(originalOut)
              finally
                  try if File.Exists corruptPath then File.Delete corruptPath with _ -> ()

          // ── Phase 33-02: /plan toggle + plan-gate integration tests ─────────────

          testCase "runMultiTurn: '/plan' once toggles plan-mode on; prints '[plan mode on]'; zero LLM calls" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/plan\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-plan-on-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []   // 0 LLM calls expected — toggle is in-process
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "exit code 0"
                  Expect.stringContains captured "[plan mode on]" "/plan prints on notification"
                  Expect.isFalse (captured.Contains("[plan mode off]")) "single /plan does not also print off notification"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/plan' twice toggles plan-mode off; prints both notifications" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/plan\n/plan\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-plan-toggle-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []   // 0 LLM calls expected
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "exit code 0"
                  Expect.stringContains captured "[plan mode on]" "first /plan prints on notification"
                  Expect.stringContains captured "[plan mode off]" "second /plan prints off notification"
                  // Order matters: on must precede off.
                  let onIdx  = captured.IndexOf("[plan mode on]")
                  let offIdx = captured.IndexOf("[plan mode off]")
                  Expect.isLessThan onIdx offIdx "[plan mode on] precedes [plan mode off] in stdout"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/status' after '/plan' shows 'plan-mode: on' line" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/plan\n/status\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-plan-status-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = Some Qwen122B }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "exit code 0"
                  Expect.stringContains captured "plan-mode: on" "status shows plan-mode line when active"
                  Expect.stringContains captured "(next prompt uses plan-gate)" "descriptive suffix included"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: plan-mode + Accept executes turn via runSingleTurn and auto-disables plan-mode" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              // Script: /plan → enable; "build feature X" → triggers plan-gate; "a\n" → Accept;
              //         /status → confirm plan-mode auto-disabled (no "plan-mode" line); /exit
              use stdinReader = new StringReader("/plan\nbuild feature X\na\n/status\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)
              // Reset Spectre.Console's singleton after redirecting Console.Out.
              // AnsiConsole lazily caches Console.Out at first use; if a prior test's StringWriter
              // was already cached and then disposed, AnsiConsole.Write(table) would throw
              // ObjectDisposedException. Creating a fresh IAnsiConsole here re-ties it to the
              // current Console.Out (our stdoutWriter).
              let originalSpectreConsole = Spectre.Console.AnsiConsole.Console
              Spectre.Console.AnsiConsole.Console <- Spectre.Console.AnsiConsole.Create(Spectre.Console.AnsiConsoleSettings())

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-plan-accept-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              // Build a minimal valid 1-step Plan that PlanValidator will accept.
              // 1 step keeps the table render simple; read_file is a safe action that
              // PlanValidator does NOT execute (Plan validation is structural, not behavioral).
              let plannedStep =
                  BlueCode.Tests.MockHelpers.makePlannedStep
                      "read_file"
                      "{\"path\":\"README.md\"}"
                      "inspect README to understand the project"
              let plan : Plan =
                  { Steps = [ plannedStep ]
                    Rationale = "examine README first to scope the requested feature" }

              let llmResponses = [
                  BlueCode.Tests.MockHelpers.makePlanResponse "let me plan this" plan       // runPlanTurn consumes this
                  makeMockResponse "executing accepted plan" (FinalAnswer "feature X built") // runSingleTurn consumes this
              ]

              let components: AppComponents =
                  { LlmClient = stubLlm llmResponses
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 5; SystemPrompt = "test"; ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "exit code 0"
                  // Plan rationale was rendered (PlanGate.render uses printfn for the rationale).
                  Expect.stringContains captured "Proposed plan:" "PlanGate rendered the plan rationale"
                  Expect.stringContains captured "examine README" "plan rationale text echoed"
                  // Accept keystroke acknowledged.
                  Expect.stringContains captured "Accepted." "PlanGate.promptUser printed Accept confirmation"
                  // Final answer from the executed turn appears.
                  Expect.stringContains captured "feature X built" "FinalAnswer from runSingleTurn printed"
                  // After Accept, planModeActive auto-disabled — subsequent /status shows NO plan-mode line.
                  // The captured stdout has both /status and the usual fields. Check the LAST occurrence
                  // of the status block: the substring after "feature X built" represents post-Accept
                  // /status output and must NOT contain "plan-mode".
                  let finalAnswerIdx = captured.IndexOf("feature X built")
                  Expect.isGreaterThan finalAnswerIdx 0 "final answer found in captured stdout"
                  let postAccept = captured.Substring(finalAnswerIdx)
                  Expect.isFalse (postAccept.Contains("plan-mode"))
                      "post-Accept /status output does NOT include 'plan-mode' line (planModeActive auto-disabled)"
              finally
                  Spectre.Console.AnsiConsole.Console <- originalSpectreConsole
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: plan-mode + Quit returns to REPL and auto-disables; process does NOT exit on Quit" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              // Script: /plan → enable; "tricky prompt" → triggers plan-gate; "q\n" → Quit;
              //         /status → if REPL is alive, this prints; /exit → graceful exit
              use stdinReader = new StringReader("/plan\ntricky prompt\nq\n/status\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)
              // Reset Spectre.Console singleton so AnsiConsole.Write(table) writes to
              // the current redirected Console.Out (stdoutWriter) rather than a stale writer
              // that may have been cached and disposed by a prior test (see test 4 comment).
              let originalSpectreConsole = Spectre.Console.AnsiConsole.Console
              Spectre.Console.AnsiConsole.Console <- Spectre.Console.AnsiConsole.Create(Spectre.Console.AnsiConsoleSettings())

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-plan-quit-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              // Same minimal Plan as Test 4. Only one LLM call expected (no execute after Quit).
              let plannedStep =
                  BlueCode.Tests.MockHelpers.makePlannedStep
                      "read_file"
                      "{\"path\":\"README.md\"}"
                      "examine README"
              let plan : Plan =
                  { Steps = [ plannedStep ]
                    Rationale = "investigate the codebase before acting" }

              let components: AppComponents =
                  { LlmClient = stubLlm [ BlueCode.Tests.MockHelpers.makePlanResponse "thinking" plan ]
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 5; SystemPrompt = "test"; ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "REPL exited cleanly via /exit (NOT via plan-gate Quit)"
                  // Plan was rendered.
                  Expect.stringContains captured "Proposed plan:" "PlanGate rendered the plan"
                  // Quit keystroke acknowledged.
                  Expect.stringContains captured "Quit." "PlanGate.promptUser printed Quit confirmation"
                  // After Quit, the REPL accepted /status — confirming process did NOT exit.
                  // Locate the Quit line and assert "session:" appears AFTER it (status output post-Quit).
                  let quitIdx = captured.IndexOf("Quit.")
                  Expect.isGreaterThan quitIdx 0 "Quit. confirmation found"
                  let postQuit = captured.Substring(quitIdx)
                  Expect.stringContains postQuit "session:" "/status executed AFTER plan-gate Quit (REPL alive)"
                  // planModeActive auto-disabled: post-Quit /status has NO "plan-mode" line.
                  Expect.isFalse (postQuit.Contains("plan-mode"))
                      "post-Quit /status output does NOT include 'plan-mode' line (planModeActive auto-disabled)"
              finally
                  Spectre.Console.AnsiConsole.Console <- originalSpectreConsole
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: plan-mode + runPlanTurn error prints renderError; auto-disables; REPL stays alive" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              // Script: /plan → enable; "broken prompt" → runPlanTurn fails; /status → REPL alive;
              //         /exit → graceful
              use stdinReader = new StringReader("/plan\nbroken prompt\n/status\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-plan-err-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm [ Error (LlmUnreachable ("http://localhost:8001", "test-induced failure")) ]
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 5; SystemPrompt = "test"; ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "REPL exited cleanly via /exit (NOT via plan-gate error)"
                  // renderError(LlmUnreachable) appears (Rendering.fs:103: "LLM unreachable (...)").
                  Expect.stringContains captured "LLM unreachable" "renderError(LlmUnreachable) printed"
                  Expect.stringContains captured "test-induced failure" "error detail echoed"
                  // After the error, /status must execute — REPL alive.
                  let errIdx = captured.IndexOf("LLM unreachable")
                  Expect.isGreaterThan errIdx 0 "LLM unreachable line found"
                  let postErr = captured.Substring(errIdx)
                  Expect.stringContains postErr "session:" "/status executed AFTER plan-gate error (REPL alive)"
                  // planModeActive auto-disabled.
                  Expect.isFalse (postErr.Contains("plan-mode"))
                      "post-error /status does NOT include 'plan-mode' line (planModeActive auto-disabled)"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          ] // end testSequenced
