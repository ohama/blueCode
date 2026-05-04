---
phase: 33-slash-plan-toggle
plan: 02
type: execute
wave: 2
depends_on:
  - 33-01
files_modified:
  - tests/BlueCode.Tests/ReplTests.fs
autonomous: true

must_haves:
  truths:
    - "Test 'plan toggle on' confirms typing /plan once prints '[plan mode on]' and zero LLM calls happen during the toggle itself"
    - "Test 'plan toggle off' confirms typing /plan twice prints both '[plan mode on]' and '[plan mode off]' without crashing"
    - "Test 'plan-mode /status display' confirms typing /plan then /status renders 'plan-mode: on' line"
    - "Test 'plan-gate Accept executes turn + auto-disables' confirms that after Accept the LLM is called twice (runPlanTurn + runSingleTurn) and planModeActive turns off (subsequent /status shows no plan-mode line)"
    - "Test 'plan-gate Quit returns to REPL + auto-disables' confirms that pressing 'q' at the gate prints 'Quit.' and the REPL accepts a subsequent /exit (process does NOT exit on Quit)"
    - "Test 'plan-mode error path' confirms that when runPlanTurn returns Error, renderError output appears + planModeActive auto-disables + REPL stays alive"
    - "Bench gate (bash bench/run.sh --gate) shows 7/7 PASS — Phase 33 dispatcher additions cause zero regression on agent-loop / plan-mode invocations"
  artifacts:
    - path: "tests/BlueCode.Tests/ReplTests.fs"
      provides: "≥6 new integration tests covering plan-mode toggle, /status display, plan-gate Accept/Quit/Error paths, and auto-disable semantics"
      contains: "plan mode on"
      contains_2: "plan mode off"
      contains_3: "plan-mode: on"
      contains_4: "Accepted."
      contains_5: "Quit."
  key_links:
    - from: "tests/BlueCode.Tests/ReplTests.fs (plan-mode integration tests)"
      to: "src/BlueCode.Cli/Repl.fs (runMultiTurnWithSession)"
      via: "scripted stdin via Console.SetIn (StringReader) drives /plan, prompt, a/r/e/q, /exit through the REPL loop; capturing LLM stub asserts call counts"
      pattern: "Console\\.SetIn.*StringReader.*\"/plan"
    - from: "tests/BlueCode.Tests/ReplTests.fs"
      to: "src/BlueCode.Cli/PlanGate.fs (realKeyReader stdin fallback)"
      via: "When stdin is redirected, Console.ReadKey throws InvalidOperationException; realKeyReader falls back to Console.In.ReadLine — so 'a\\n' / 'q\\n' in scripted stdin work as plan-gate decisions"
      pattern: "InvalidOperationException.*Console\\.In\\.ReadLine"
    - from: "bench/run.sh (--gate mode)"
      to: "src/BlueCode.Cli/Program.fs (single-turn dispatch)"
      via: "Bench invokes single-turn (prompt as CLI arg); never enters runMultiTurnWithSession; Phase 33 changes confined to multi-turn dispatcher do not affect this code path"
      pattern: "dotnet run --project src/BlueCode.Cli"
---

<objective>
Phase 33 — Plan 02: Add integration tests covering the new plan-mode behavior shipped
in Plan 33-01 (toggle on/off, /status display, plan-gate Accept/Quit/Error paths,
auto-disable semantics), and verify the bench gate stays 7/7 PASS.

Purpose: Plan 33-01 made the source-code change and kept the build/tests green via
existing-test adaptations. Plan 33-02 EMPIRICALLY verifies the new behavior end-to-end
through scripted stdin into the REPL loop. The pattern is identical to Phase 32-02 — 5
new integration tests using Console.SetIn (StringReader) + Console.SetOut
(StringWriter) inside the existing testSequenced block. After Plan 33-02, Phase 33 is
fully done; ready for `/gsd:verify-work 33` UAT.

Roadmap success criterion 1 verification: `/status` after `/plan` toggles shows
`plan-mode: on` line — covered by `plan-mode /status display` test.

Roadmap success criterion 2 verification: planModeActive=true routes next prompt
through `runPlanTurn` + `PlanGate` — covered by `plan-gate Accept executes turn` test
(asserts LLM called twice: once for the plan, once for the execution).

Roadmap success criterion 3 verification: `/plan` again toggles off — covered by
`plan toggle off` test (stdin: `/plan\n/plan\n/exit\n`; asserts both notifications
appear in stdout).

Roadmap success criterion 4: mid-turn `/plan` invalid — N/A (research § Q4: REPL
ReadLine blocks the loop thread; no race possible). No test covers an impossible
scenario.

Roadmap success criterion 5: bench gate 7/7 PASS — covered by Task 3 (bench gate
verification).

Roadmap success criterion 6: Role=System invariant — verified architecturally
(notifications use printfn, runPlanTurn uses Role=User per Phase 20-03 invariant).
The `plan toggle on` test additionally asserts zero LLM calls happen during toggle
itself (the LlmClient stub queue is empty and would throw on any call).

Output:
- 6 new integration testCases in `ReplTests.fs` (inside the existing testSequenced
  block):
  1. `runMultiTurn: '/plan' once toggles plan-mode on; prints '[plan mode on]'; zero
     LLM calls`
  2. `runMultiTurn: '/plan' twice toggles plan-mode off; prints both notifications`
  3. `runMultiTurn: '/status' after '/plan' shows 'plan-mode: on' line`
  4. `runMultiTurn: plan-mode prompt + Accept (a) executes turn via runSingleTurn and
     auto-disables plan-mode`
  5. `runMultiTurn: plan-mode prompt + Quit (q) returns to REPL prompt and
     auto-disables plan-mode (process does NOT exit)`
  6. `runMultiTurn: plan-mode prompt + runPlanTurn error path prints renderError and
     auto-disables plan-mode (REPL stays alive)`
- Bench gate verified 7/7 PASS preserved.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/STATE.md
@.planning/REQUIREMENTS.md
@.planning/phases/33-slash-plan-toggle/33-RESEARCH.md
@.planning/phases/33-slash-plan-toggle/33-01-toggle-and-wiring-PLAN.md
@CLAUDE.md
@src/BlueCode.Cli/Repl.fs
@src/BlueCode.Cli/PlanGate.fs
@src/BlueCode.Cli/Rendering.fs
@src/BlueCode.Cli/SlashCommand.fs
@src/BlueCode.Cli/CompositionRoot.fs
@src/BlueCode.Core/AgentLoop.fs
@src/BlueCode.Core/Domain.fs
@tests/BlueCode.Tests/ReplTests.fs
@tests/BlueCode.Tests/PlanGateTests.fs
@tests/BlueCode.Tests/MockHelpers.fs
@bench/run.sh
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add 6 new plan-mode integration testCases to ReplTests.fs</name>
  <files>tests/BlueCode.Tests/ReplTests.fs</files>
  <action>
Insert 6 new testCases inside the existing `testSequenced <| testList "Repl" [...]`
block in `ReplTests.fs`. Place them BEFORE the closing `]` of the testList (the same
position used by Phase 32-02 for /sessions and /resume tests — research § Q10 confirms
this is the pattern).

The PlanGate.realKeyReader has a stdin-redirect fallback: when `Console.ReadKey` throws
`InvalidOperationException` (which happens whenever stdin is a StringReader, not a
TTY), realKeyReader reads a line from `Console.In` and uses its first character as the
keystroke. So scripted stdin like `"a\n"` or `"q\n"` works as a plan-gate decision.

The plan-gate Accept-executes test needs the LLM to be called TWICE: once for
`runPlanTurn` (returns LlmOutput.Plan), once for `runSingleTurn` (returns
LlmOutput.FinalAnswer to terminate the agent loop). Use `MockHelpers.makePlanResponse`
(line 17 of MockHelpers.fs) to construct the Plan response and `MockHelpers.makeMockResponse`
(already used by existing tests) to construct the FinalAnswer.

For minimum viable Plan content, use a 1-step plan with a `read_file` action — this
satisfies PlanValidator without triggering placeholder/path-rewrite checks (the path
need not actually exist; the Plan is validated structurally, not executed during
runPlanTurn).

(a) Test 1 — `/plan` once toggles on, prints '[plan mode on]', zero LLM calls.

This is the simplest assertion. The LlmClient stub queue is empty (`stubLlm []`); if
the toggle accidentally routed through any LLM call, the queue would throw and fail
the test.

```fsharp
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
```

(b) Test 2 — `/plan` twice toggles off (symmetric); prints both notifications.

```fsharp
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
```

(c) Test 3 — `/status` after `/plan` shows 'plan-mode: on' line.

```fsharp
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
```

(d) Test 4 — plan-mode prompt + Accept (a): executes turn + auto-disables plan-mode.

This is the most involved test. The LLM stub provides TWO responses:
1. A Plan response (consumed by runPlanTurn).
2. A FinalAnswer response (consumed by runSingleTurn after Accept).

After Accept, the test sends `/status` to verify planModeActive auto-disabled — the
status output must NOT contain "plan-mode" (the line is hidden when off).

```fsharp
          testCase "runMultiTurn: plan-mode + Accept executes turn via runSingleTurn and auto-disables plan-mode" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              // Script: /plan → enable; "build feature X" → triggers plan-gate; "a\n" → Accept;
              //         /status → confirm plan-mode auto-disabled (no "plan-mode" line); /exit
              use stdinReader = new StringReader("/plan\nbuild feature X\na\n/status\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

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
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)
```

(e) Test 5 — plan-mode prompt + Quit (q): returns to REPL + auto-disables; process does
NOT exit.

The key assertion is that after Quit, the REPL accepts a subsequent `/exit` — proving
the process did not exit on Quit (research § Pitfall 4).

```fsharp
          testCase "runMultiTurn: plan-mode + Quit returns to REPL and auto-disables; process does NOT exit on Quit" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              // Script: /plan → enable; "tricky prompt" → triggers plan-gate; "q\n" → Quit;
              //         /status → if REPL is alive, this prints; /exit → graceful exit
              use stdinReader = new StringReader("/plan\ntricky prompt\nq\n/status\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

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
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)
```

(f) Test 6 — plan-mode prompt + runPlanTurn error: renderError printed + auto-disabled
+ REPL alive.

When the LLM returns garbage that fails parse twice, runPlanTurn returns Error
`InvalidJsonOutput`. The plan-gate inline loop catches the Error, prints renderError,
sets planModeActive=false + turnDone=true. REPL must keep running.

The simplest way to force runPlanTurn to fail: have the LlmClient return Error
LlmUnreachable on the first call. runPlanTurn pattern-matches `Error LlmUnreachable`
explicitly (AgentLoop.fs:510) and returns Error immediately (no retry attempt 2).

```fsharp
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
```

INSERT all 6 testCases together inside the existing `testSequenced <| testList "Repl"
[...]` block. Place them at the end of the testList — after the last existing /resume
test (the corrupt session test from Phase 32-02), but BEFORE the closing `]` that
terminates the testList.

CRITICAL test hygiene:
- All 6 tests use unique GUID-N suffix paths so they don't clash with each other or
  with prior test runs (research § Q10 + Pitfall 6).
- `Console.SetIn` + `Console.SetOut` inside testSequenced: the OUTER `testSequenced
  <| testList "Repl"` wrapper prevents stdout/stdin races between adjacent tests
  (CLAUDE.md "Console.SetOut in tests"). Do NOT add any nested testSequenced.
- Each test restores `Console.In` / `Console.Out` in `finally` — non-negotiable.
- `MockHelpers.makePlannedStep` and `MockHelpers.makePlanResponse` are public functions
  in MockHelpers.fs (line 13 and 23 respectively); already used by AgentLoopTests.fs
  for plan-related tests. Use the fully-qualified
  `BlueCode.Tests.MockHelpers.makePlannedStep` and
  `BlueCode.Tests.MockHelpers.makePlanResponse` calls (the `open
  BlueCode.Tests.MockHelpers` on line 13 of ReplTests.fs makes the unqualified names
  available, but using the fully-qualified form keeps the new code grep-able and
  unambiguous).
- Test 4's "post-Accept /status output" assertion is the key auto-disable proof. The
  technique (slice stdout AFTER a marker, assert absence of "plan-mode") generalizes
  to Tests 5 and 6 — same technique, different markers ("Quit." and "LLM unreachable").

DO NOT modify any other test in ReplTests.fs.
DO NOT add tests to RenderingTests.fs (Plan 33-01 owned the 1 new RenderingTests
addition).

Build and run all tests:

```
dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj
dotnet build tests/BlueCode.Tests/BlueCode.Tests.fsproj
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
```

Expect ALL tests passing. Test count delta from this task: +6 ReplTests integration
tests. Cumulative Phase 33 delta from Plan 33-01 (+1) + Plan 33-02 (+6) = +7 tests
total.

Commit atomically:

```
git add tests/BlueCode.Tests/ReplTests.fs
git commit -m "test(33-02): add /plan toggle + plan-gate REPL integration tests"
```

DO NOT use `git add -A` / `git add .`.
  </action>
  <verify>
- `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.
- `dotnet build tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0.
- `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0; ALL tests
  pass; ReplTests count increased by 6 from Plan 33-01 baseline.
- `grep -c "/plan\\\\n/exit\\\\n" tests/BlueCode.Tests/ReplTests.fs` returns 1 (Test 1
  stdin script).
- `grep -c "/plan\\\\n/plan\\\\n/exit\\\\n" tests/BlueCode.Tests/ReplTests.fs` returns 1
  (Test 2 stdin script).
- `grep -c "/plan\\\\n/status\\\\n/exit\\\\n" tests/BlueCode.Tests/ReplTests.fs`
  returns 1 (Test 3 stdin script).
- `grep -c "build feature X" tests/BlueCode.Tests/ReplTests.fs` returns 2 (Test 4 stdin
  prompt + final answer assertion).
- `grep -c "tricky prompt" tests/BlueCode.Tests/ReplTests.fs` returns 1 (Test 5 stdin
  prompt).
- `grep -c "broken prompt" tests/BlueCode.Tests/ReplTests.fs` returns 1 (Test 6 stdin
  prompt).
- `grep -c "MockHelpers.makePlannedStep" tests/BlueCode.Tests/ReplTests.fs` returns 2
  (Tests 4 and 5).
- `grep -c "MockHelpers.makePlanResponse" tests/BlueCode.Tests/ReplTests.fs` returns 2
  (Tests 4 and 5).
- `grep -c "test-induced failure" tests/BlueCode.Tests/ReplTests.fs` returns 2 (Test 6
  stub + assertion).
- `grep -c "planModeActive auto-disabled" tests/BlueCode.Tests/ReplTests.fs` returns 3
  (Tests 4, 5, 6).
- `git log -1 --oneline` contains `test(33-02)` + `plan toggle`.
- `git diff master -- src/BlueCode.Core/` is empty (Core untouched).
- `git diff HEAD~1 -- src/` is empty for this commit (test-only commit; no production
  source modified).
  </verify>
  <done>
- 6 new integration testCases inserted into ReplTests.fs testSequenced block:
  Test 1 (plan toggle on, 0 LLM calls), Test 2 (plan toggle off, both notifications
  in order), Test 3 (/status shows plan-mode line), Test 4 (Accept executes turn +
  auto-disables), Test 5 (Quit returns to REPL + auto-disables, process alive),
  Test 6 (runPlanTurn error path + auto-disables + REPL alive).
- All 6 tests use unique GUID-N temp paths and clean up in finally.
- Tests 4-6 use the post-marker stdout-slice technique to assert auto-disable
  semantics (no "plan-mode" string in stdout AFTER the relevant marker).
- All tests pass; full suite green; net delta +6.
- Atomic commit `test(33-02): add /plan toggle + plan-gate REPL integration tests`.
  </done>
</task>

<task type="auto">
  <name>Task 2: Smoke-test the /plan toggle end-to-end via piped stdin (manual sanity check before bench)</name>
  <files>(no source files modified — verification only)</files>
  <action>
This task is a quick non-bench sanity check that the /plan toggle works end-to-end
when invoked from the actual binary (as opposed to via the in-test runMultiTurn entry
point). It verifies the wiring from Program.fs → Repl.runMultiTurnWithSession is
intact and that the realKeyReader stdin fallback works in the production binary.

This task does NOT call the LLM (only toggle on/off + /status + /exit), so it does
NOT depend on the 122B service being loaded. Cheap (~10 seconds), no flakiness.

1. Build the Cli (Debug is fine for this smoke):

```
dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj
```

Expect exit 0.

2. Pipe a toggle-on + status + toggle-off + exit script into the binary:

```
echo -e "/plan\n/status\n/plan\n/status\n/exit" | dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj 2>/dev/null
```

(stderr discarded so only the REPL stdout is visible — keeps the smoke output
focused.)

3. Verify the captured stdout contains:
   - `[plan mode on] — next prompt will enter plan-gate before execution`
   - `plan-mode: on (next prompt uses plan-gate)`  (in the FIRST /status output)
   - `[plan mode off] — returning to direct agent-loop`
   - In the SECOND /status output, the `plan-mode` line is ABSENT (only the standard
     session/model/steps/chars block appears).

4. If any expected line is missing, the wiring is broken — STOP and report which line
   is missing. Do not proceed to bench.

5. Smoke test exit code MUST be 0. Capture exit code via:

```
echo -e "/plan\n/status\n/plan\n/status\n/exit" | dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj 2>/dev/null
echo "exit code: $?"
```

Expect `exit code: 0`.

6. NO commit (this task is verification only — no source modified).

DO NOT modify any source files in this task.
  </action>
  <verify>
- `echo -e "/plan\n/status\n/plan\n/status\n/exit" | dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj 2>/dev/null` exits 0.
- The captured stdout contains the literal substring `[plan mode on]`.
- The captured stdout contains the literal substring `plan-mode: on (next prompt uses plan-gate)`.
- The captured stdout contains the literal substring `[plan mode off]`.
- The captured stdout contains EXACTLY ONE `plan-mode: on` line (the second /status
  output does not include it because the second /plan toggled off).
- `git status` shows no unstaged modifications under `src/` after this task.
  </verify>
  <done>
- End-to-end /plan toggle wiring verified via piped stdin into the production binary.
- Smoke output evidence captured (paste into the SUMMARY.md if convenient).
- No source code changes; no commit.
- Phase 33 success criteria 1 + 3 + 6 empirically observable from this smoke (1: REPL
  state has planModeActive + /status displays it; 3: /plan again toggles off; 6: the
  notification text appears via printfn in console only).
  </done>
</task>

<task type="auto">
  <name>Task 3: Verify bench gate 7/7 PASS preserved (Phase 33 success criterion 5)</name>
  <files>(no source files modified — verification only)</files>
  <action>
This task is the regression gate for Phase 33 success criterion 5 ("Bench gate 7/7
PASS preserved"). Research § Q12 confirmed zero regression risk in theory because
bench is single-turn (Program.fs → runSingleTurn) and never enters
runMultiTurnWithSession's dispatcher. Empirical verification still required.

1. Pre-flight: ensure the 122B service is loaded and warm:

```
curl -fsS http://127.0.0.1:8001/v1/models
```

If this fails, the service is not running. Hand back to user with: "122B
mlx_lm.server not responding on port 8001. Run: `launchctl kickstart -k
gui/501/com.ohama.qwen122b` then retry this task." Do NOT proceed with bench until
122B is reachable.

2. Build the binary in Release config (bench/run.sh executes against the published
Cli):

```
dotnet build -c Release src/BlueCode.Cli/BlueCode.Cli.fsproj
```

Expect exit 0.

3. Run the bench gate:

```
bash /Users/ohama/projs/blueCode/bench/run.sh --gate
```

This runs the regression subset (~2 min) covering T6_122b, W1_122b, W2_122b, T1_122b,
T5_122b, B2_122b (+ MT if present in baseline). The gate compares against
`bench/baseline.json` and exits non-zero on any regression.

4. Verify gate output:
   - Last line MUST contain "PASS" (e.g., `[run.sh] gate result: 7/7 PASS`).
   - Exit code MUST be 0.
   - No fixture should have status "REGRESSED" or "FAIL".

5. If gate FAILS (regression detected):
   - Do NOT modify `bench/baseline.json` — that is the structural authority (CLAUDE.md
     "Bench").
   - Do NOT modify the bench fixtures.
   - The dispatcher additions in Plan 33-01 should NOT regress agent-loop behavior
     (research § Q12: bench is single-turn; runMultiTurnWithSession is unreachable
     from bench).
   - If regression observed, the most likely cause is an accidental modification of
     the unguarded `Prompt` arm body inside the dispatcher. Re-read `git diff master
     -- src/BlueCode.Cli/Repl.fs` and confirm:
     - The unguarded `| Some (Prompt prompt) ->` arm body (the existing code below the
       new `when planModeActive` arm) is byte-identical to its prior state.
     - `runSingleTurn` itself was not modified.
     - `shouldWarnContextWindow` was not modified.
   - Also confirm `git diff master -- src/BlueCode.Cli/Program.fs` is empty (Phase 33
     does not touch Program.fs at all — the single-turn `--plan` path is independent
     of the REPL plan-mode toggle).
   - If the diff is clean and bench still regresses, this is a real regression — STOP
     plan execution and report findings.

6. Once gate is green, no commit is needed (no source modified by this task). The
   bench logs land in `bench/runs/<timestamp>/` (gitignored).

DO NOT skip this task even if Tasks 1 and 2 passed unit + smoke tests. Bench is a
different gate (real-network, real-LLM, structural) that protects production behavior.
  </action>
  <verify>
- `curl -fsS http://127.0.0.1:8001/v1/models` exits 0.
- `dotnet build -c Release src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.
- `bash bench/run.sh --gate` exits 0 with output containing `PASS` (`7/7 PASS`).
- `git status` shows no unstaged modifications under `src/` after this task.
- `git diff master -- bench/baseline.json` is empty (baseline.json byte-equal —
  CLAUDE.md invariant).
- `git diff master -- src/BlueCode.Cli/Program.fs` is empty (Program.fs untouched —
  REPL plan-mode is independent of Program.fs single-turn --plan path).
  </verify>
  <done>
- Bench gate runs to completion with all fixtures PASS (matching pre-Phase-33
  baseline).
- Phase 33 success criterion 5 satisfied.
- No source code changes from this task; no commit.
- `bench/baseline.json` byte-identical to pre-Phase-33 (no false regressions
  induced).
  </done>
</task>

</tasks>

<verification>
After all 3 tasks complete, run these final phase-level gates:

1. **Build gate (release):** `dotnet build -c Release src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.

2. **Full test suite:** `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj`
   exits 0; all tests pass; total count = pre-Phase-33 baseline + 7 (Plan 33-01: +1
   RenderingTests; Plan 33-02: +6 ReplTests; existing 5 tests modified in place across
   both plans, not new). The 5 modified existing tests are: 4 renderStatus (3-arg →
   4-arg signature in RenderingTests.fs), 1 marker count (RenderingTests.fs), and 1
   future-stub (ReplTests.fs). The /status ReplTests test passes without modification
   because its assertions are insensitive to the new optional plan-mode line.

3. **Bench gate:** `bash bench/run.sh --gate` exits 0 with PASS verdict (Task 3 covers
   this; do not re-run if just completed).

4. **Core purity:** `git diff master -- src/BlueCode.Core/` is empty.

5. **No-async (Cli + Core):** `bash scripts/check-no-async.sh` exits 0.

6. **Program.fs untouched:** `git diff master -- src/BlueCode.Cli/Program.fs` is
   empty (Phase 33 is REPL-only; Program.fs single-turn --plan path is independent).

7. **End-to-end smoke:** Task 2's smoke test passes (echoed stdin produces expected
   notifications + /status displays).

8. **Atomic commit count:** `git log --oneline master..HEAD` shows exactly 3 commits
   with `(33-` scope: 2 from Plan 33-01 (`feat(33-01)` + `test(33-01)`) + 1 from Plan
   33-02 (`test(33-02)`). No amends, no `git add -A`.
</verification>

<success_criteria>
This plan succeeds, and Phase 33 is complete, when:

- [ ] **SC-1 (planModeActive in REPL state + toggle + /status display):** Verified by
  Plan 33-01's source code + Plan 33-02 Tests 1, 2, 3 + Task 2 smoke. `runMultiTurnWithSession`
  has `let mutable planModeActive`. `/plan` toggles. `/status` displays "plan-mode: on"
  when active.
- [ ] **SC-2 (runPlanTurn route on next prompt when active):** Verified by Plan 33-02
  Test 4 (Accept executes turn — LLM called twice: runPlanTurn + runSingleTurn; plan
  rationale rendered; "Accepted." printed; final answer printed).
- [ ] **SC-3 (/plan again toggles off):** Verified by Plan 33-02 Test 2 (both
  `[plan mode on]` and `[plan mode off]` notifications appear in order).
- [ ] **SC-4 (mid-turn /plan invalid):** N/A — architectural (REPL ReadLine blocks the
  loop thread; no race possible per research § Q4). Not testable; nothing to verify.
- [ ] **SC-5 (Bench gate 7/7 PASS):** Verified by Plan 33-02 Task 3.
- [ ] **SC-6 (Role=System invariant; toggle notification user-facing console only):**
  Verified by code inspection (Plan 33-01: notifications use `printfn` not LLM message
  injection) and empirically by Plan 33-02 Test 1 (zero LLM calls when only /plan is
  invoked).
- [ ] **SC-7 (auto-disable semantics — Open Question #1+#2 resolutions):** Verified by
  Plan 33-02 Tests 4, 5, 6 (post-Accept, post-Quit, post-error /status output does NOT
  contain "plan-mode" line).
- [ ] **SC-8 (process does NOT exit on plan-gate Quit):** Verified by Plan 33-02 Test
  5 (post-Quit /status executes — REPL alive — and /exit produces clean exit code 0).
- [ ] No file under `src/BlueCode.Core/**` modified (CLAUDE.md Core purity invariant
  preserved).
- [ ] `src/BlueCode.Cli/Program.fs` byte-identical to pre-Phase-33 (REPL plan-mode is
  independent of Program.fs single-turn --plan path).
- [ ] No new NuGet package added.
- [ ] `bench/baseline.json` byte-identical (CLAUDE.md invariant).
- [ ] Test count delta: +6 (Plan 33-02) on top of Plan 33-01's +1 = +7 cumulative for
  Phase 33. 5 existing tests modified in place across both plans (4 renderStatus
  signature + 1 marker count + 1 future-stub).
- [ ] 3 atomic commits with `(33-` scope (2 from 33-01: `feat(33-01)` + `test(33-01)`;
  1 from 33-02: `test(33-02)`); all staged file-by-file (no `git add -A` violations).
- [ ] Future-proofing: `/edit` still parses cleanly and prints placeholder without
  crashing — Phase 34 will add only its dispatcher arm, not parser changes.
</success_criteria>

<output>
After completion, create `.planning/phases/33-slash-plan-toggle/33-02-SUMMARY.md`
documenting:

- Production LOC added (0 — this plan is test-only)
- Test LOC added (~360 across 6 new ReplTests integration testCases averaging ~60 LOC
  each including stdin/stdout setup, components wiring, assertions, and finally cleanup)
- Test count delta (e.g., 346 → 352; cumulative Phase 33 delta: 345 → 352 = +7)
- Bench gate result (e.g., "7/7 PASS — no regression")
- Smoke evidence (Task 2 stdout snippet showing [plan mode on] / [plan mode off] /
  plan-mode: on display)
- Frontmatter to include:
  - `requires: [33-01]` (depends on planModeActive cell + plan-gate inline loop +
    renderStatus 4-arg signature)
  - `affects: []` (terminal — no downstream phase consumes Plan 33-02 directly)
  - `subsystem: cli-repl-tests`
  - `tech_stack_added: []` (no new NuGet)
  - `phase_progress: "33-02 of 33-02 plans complete; Phase 33 done"`
- Confirm Phase 33 success criteria 1-6 (from ROADMAP.md) all observable end-to-end:
  1. planModeActive in REPL state, /plan toggles, /status displays ✓ (Tests 1+3)
  2. runPlanTurn route on next prompt + PlanGate display ✓ (Test 4)
  3. /plan again toggles off ✓ (Test 2)
  4. mid-turn /plan invalid — N/A architectural ✓ (no test; documented)
  5. Bench gate 7/7 PASS ✓ (Task 3)
  6. Role=System invariant + toggle notification user-facing only ✓ (code inspection +
     Test 1's zero-LLM assertion)
- Note any deviations (expected: none — research is HIGH confidence; Plan 33-01 already
  shipped the source code clean).
- Phase 33 status: ready for `/gsd:verify-work 33` UAT gate.
</output>
