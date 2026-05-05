---
phase: 34-edit-multi-line-input
plan: 02
type: execute
wave: 2
depends_on: ["34-01"]
files_modified:
  - tests/BlueCode.Tests/EditCommandTests.fs   # NEW: mock IEditorLauncher tests
  - tests/BlueCode.Tests/ReplTests.fs          # NEW testCases for /edit dispatch (mock launcher routed via Repl integration test pattern)
  - tests/BlueCode.Tests/BlueCode.Tests.fsproj # add EditCommandTests.fs Compile entry BEFORE RouterTests.fs
  - tests/BlueCode.Tests/RouterTests.fs        # add BlueCode.Tests.EditCommandTests.tests to rootTests list
autonomous: true

must_haves:
  truths:
    - "EditCommand.openEditorAsync called with a mock launcher that writes 'foo bar' returns Some \"foo bar\" (trimmed)."
    - "EditCommand.openEditorAsync called with a mock launcher that writes empty string returns None."
    - "EditCommand.openEditorAsync called with a mock launcher that writes whitespace-only ('   \\n\\t') returns None (whitespace-only treated as cancel)."
    - "EditCommand.openEditorAsync deletes the tmpfile after the launcher returns (File.Exists tmpPath = false in finally; verified by capturing tmpPath via mock)."
    - "Repl integration: /edit followed by mock launcher writing 'list files' followed by /exit -> the LLM stub receives exactly one prompt = 'list files' (proves /edit -> handlePromptTurn -> runSingleTurn dispatch path)."
    - "Repl integration: /edit followed by mock launcher writing empty string followed by /exit -> LLM stub receives 0 calls; stdout contains 'Edit cancelled.'."
    - "Bench gate (bash bench/run.sh --gate) reports 7/7 PASS post-Phase-34 with byte-equal baseline.json (REPL changes do not regress non-interactive bench runs)."
  artifacts:
    - path: "tests/BlueCode.Tests/EditCommandTests.fs"
      provides: "Mock IEditorLauncher unit tests for openEditorAsync (4-5 testCases inside testList \"EditCommand\")"
      exports: ["tests"]
      min_lines: 60
    - path: "tests/BlueCode.Tests/ReplTests.fs"
      provides: "2 NEW testCases inside the existing testSequenced testList \"Repl\" — /edit dispatch with mock launcher injected via overridable launcher seam"
      contains: "Slash Edit"
    - path: "tests/BlueCode.Tests/BlueCode.Tests.fsproj"
      provides: "<Compile Include=\"EditCommandTests.fs\" /> placed BEFORE RouterTests.fs"
      contains: "EditCommandTests.fs"
    - path: "tests/BlueCode.Tests/RouterTests.fs"
      provides: "BlueCode.Tests.EditCommandTests.tests appended to rootTests list"
      contains: "EditCommandTests.tests"
  key_links:
    - from: "tests/BlueCode.Tests/EditCommandTests.fs scriptedLauncher"
      to: "src/BlueCode.Cli/EditCommand.fs IEditorLauncher"
      via: "Object expression { new IEditorLauncher with member _.Launch tmpPath = File.WriteAllText(tmpPath, scriptedContent) }"
      pattern: "IEditorLauncher"
    - from: "tests/BlueCode.Tests/EditCommandTests.fs"
      to: "src/BlueCode.Cli/EditCommand.fs openEditorAsync"
      via: "let result = EditCommand.openEditorAsync mockLauncher |> fun t -> t.GetAwaiter().GetResult()"
      pattern: "openEditorAsync"
    - from: "tests/BlueCode.Tests/RouterTests.fs rootTests list"
      to: "tests/BlueCode.Tests/EditCommandTests.fs tests"
      via: "Append BlueCode.Tests.EditCommandTests.tests inside the testList \"all\" array"
      pattern: "EditCommandTests\\.tests"
    - from: "bench/run.sh --gate"
      to: "bench/baseline.json"
      via: "Regression authority — 7/7 labels (T6_122b W1_122b W2_122b T1_122b T5_122b B2_122b + 1) MUST PASS"
      pattern: "PASS"
---

<objective>
Validate the Phase 34 `/edit` implementation introduced by Plan 34-01 with three layers of evidence:
1. **Unit tests for `EditCommand.openEditorAsync`** — mock `IEditorLauncher` writing scripted content; assert non-empty/empty/whitespace-only/tmpfile-cleanup contracts (research § Test Mock Pattern, lines 400-439).
2. **REPL integration tests** — exercise the `Slash Edit` arm end-to-end with a mock launcher injected via a test-only seam; assert that non-empty content is dispatched to the LLM stub as the next prompt (proves the `handlePromptTurn` wiring) and that empty content prints `"Edit cancelled."` with zero LLM calls.
3. **Bench gate 7/7 PASS** — `bash bench/run.sh --gate` confirms the structural change (handlePromptTurn refactor + new EditCommand module + EditCommand.fs ProcessExit handler registration on every blueCode invocation) introduces no regression in the non-interactive 122B bench fixtures.

Purpose: This plan is the verification layer. Plan 34-01 added the production wiring; without behavior tests we cannot prove the contract, and without bench gate we cannot prove zero regression. CLAUDE.md enshrines `bench/run.sh --gate` as "the structural authority" — it MUST pass before declaring Phase 34 complete.

Output: One new test module (`EditCommandTests.fs`) registered in BOTH the .fsproj AND the `rootTests` list (per STATE.md "Test discovery" decision — the project does NOT use `[<Tests>]` auto-discovery), 2 new testCases in the existing `ReplTests` testSequenced list, and a green bench gate.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/REQUIREMENTS.md
@.planning/phases/34-edit-multi-line-input/34-RESEARCH.md
@.planning/phases/34-edit-multi-line-input/34-01-port-and-integration-PLAN.md

# Source files this plan tests against (Plan 34-01 outputs):
@src/BlueCode.Cli/EditCommand.fs
@src/BlueCode.Cli/Repl.fs

# Existing test patterns this plan mirrors:
@tests/BlueCode.Tests/PlanGateTests.fs
@tests/BlueCode.Tests/ReplTests.fs
@tests/BlueCode.Tests/BlueCode.Tests.fsproj
@tests/BlueCode.Tests/RouterTests.fs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create EditCommandTests.fs (mock IEditorLauncher unit tests) and register in fsproj + rootTests</name>
  <files>tests/BlueCode.Tests/EditCommandTests.fs (NEW), tests/BlueCode.Tests/BlueCode.Tests.fsproj, tests/BlueCode.Tests/RouterTests.fs</files>
  <action>
**Step A — Create `tests/BlueCode.Tests/EditCommandTests.fs`** with 5 testCases mirroring the PlanGateTests structure:

```fsharp
module BlueCode.Tests.EditCommandTests

open System
open System.IO
open Expecto
open BlueCode.Cli.EditCommand

/// Build a scripted IEditorLauncher that writes `content` to the tmpPath
/// when Launch is called. Captures the tmpPath via a ref so tests can
/// assert post-launch file state (e.g. deletion).
let private scriptedLauncher (content: string) (capturedPath: string ref) : IEditorLauncher =
    { new IEditorLauncher with
        member _.Launch tmpPath =
            capturedPath := tmpPath
            File.WriteAllText(tmpPath, content) }

/// Synchronous run helper: openEditorAsync returns Task<string option>;
/// tests are testCase (sync) so we drain the task here.
let private runSync (t: System.Threading.Tasks.Task<string option>) : string option =
    t.GetAwaiter().GetResult()

let tests =
    testList "EditCommand" [

        testCase "openEditorAsync: non-empty content -> Some (trimmed) content" <| fun () ->
            let captured = ref ""
            let launcher = scriptedLauncher "Refactor auth to use JWT\n" captured
            let result = openEditorAsync launcher |> runSync
            Expect.equal result (Some "Refactor auth to use JWT")
                "trimmed content returned (trailing newline stripped)"

        testCase "openEditorAsync: empty content -> None (cancel)" <| fun () ->
            let captured = ref ""
            let launcher = scriptedLauncher "" captured
            let result = openEditorAsync launcher |> runSync
            Expect.equal result None "empty file returns None (cancel path)"

        testCase "openEditorAsync: whitespace-only content -> None (cancel; Trim() = \"\")" <| fun () ->
            let captured = ref ""
            let launcher = scriptedLauncher "   \n\t  \n" captured
            let result = openEditorAsync launcher |> runSync
            Expect.equal result None
                "whitespace-only file returns None (research § Pitfall 4: Trim()=\"\" treats degenerate \"no real content\" as cancel)"

        testCase "openEditorAsync: tmpfile deleted after read (try/finally)" <| fun () ->
            let captured = ref ""
            let launcher = scriptedLauncher "some content" captured
            let _ = openEditorAsync launcher |> runSync
            Expect.notEqual !captured "" "launcher did capture a tmpPath"
            Expect.isFalse (File.Exists !captured)
                "tmpfile deleted after read (try/finally cleanup; research § Pattern 3)"

        testCase "openEditorAsync: tmpfile has .md extension (editor syntax-highlight hint)" <| fun () ->
            let captured = ref ""
            let launcher = scriptedLauncher "x" captured
            let _ = openEditorAsync launcher |> runSync
            Expect.stringEnds !captured ".md"
                "tmpfile renamed to .md so vim/nano apply markdown syntax highlighting"
    ]
```

**Why `testList` not `testSequenced`:** None of these testCases redirect `Console.SetOut` / `Console.SetIn`. `Path.GetTempFileName` produces unique paths every call, so concurrent execution does not race. CLAUDE.md `testSequenced` rule applies only to tests touching console globals; these are pure file I/O against unique paths. Parallel execution is safe and faster.

**Step B — Update `tests/BlueCode.Tests/BlueCode.Tests.fsproj`** to add `EditCommandTests.fs` to the `<ItemGroup>` Compile list. Per CLAUDE.md "Test discovery pattern": new test modules MUST be added BEFORE `RouterTests.fs` (which has `[<EntryPoint>]`).

Insert immediately after `SlashCommandTests.fs` (alphabetical-ish, or just before the `RouterTests.fs` line — both work; choose adjacency to other slash/REPL tests for findability):

```xml
<Compile Include="SlashCommandTests.fs" />
<Compile Include="ToolExpansionTests.fs" />
<Compile Include="EditCommandTests.fs" />     <!-- NEW: must precede RouterTests.fs -->
<Compile Include="RouterTests.fs" />
```

**Step C — Update `tests/BlueCode.Tests/RouterTests.fs`** `rootTests` list (line ~90-115) to register the new test module. Append to the testList "all" array:

```fsharp
let rootTests =
    testList
        "all"
        [ allTests
          BlueCode.Tests.LlmPipelineTests.allTests
          // ... existing entries unchanged ...
          BlueCode.Tests.SessionStoreTests.tests
          BlueCode.Tests.SlashCommandTests.tests          // (Phase 31-01)
          BlueCode.Tests.EditCommandTests.tests ]         // NEW (Phase 34-02)
```

The closing `]` MUST be repositioned to after the new entry. Verify by `grep -n "EditCommandTests" tests/BlueCode.Tests/RouterTests.fs` returns 1 line and `grep -n "^let rootTests" -A 30 tests/BlueCode.Tests/RouterTests.fs` shows the new entry inside the brackets.

**Critical:** STATE.md "Test discovery — explicit rootTests list" is load-bearing. Forgetting EITHER the `.fsproj` entry OR the `rootTests` registration causes the new tests to compile but produce 0 test runs (silent failure — research § Pitfall 7).

**What NOT to do:**
- Do NOT add `[<Tests>]` attribute on `let tests` — auto-discovery is NOT used in this project (CLAUDE.md "Test discovery pattern").
- Do NOT use `dotnet test` — use `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` (CLAUDE.md "Canonical test runner").
- Do NOT redirect Console.SetOut in EditCommandTests — these tests don't touch console globals; redirection would force testSequenced for no gain.
- Do NOT use Spectre.Console.Console reset — these tests don't exercise PlanGate.render (Phase 33-02 reset pattern is specific to that surface).
- Do NOT reference Core.AgentLoop or Core.Domain — EditCommand is a Cli adapter; tests only need `BlueCode.Cli.EditCommand`.

**Run the test suite to confirm new tests are discovered + pass:**
```bash
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
```
Expected: test count INCREASES by exactly 5 vs Plan 34-01's baseline (4 unit testCases for the 4 contracts + 1 .md extension testCase).

**Atomic commit (Task 1):**
```bash
git add tests/BlueCode.Tests/EditCommandTests.fs \
        tests/BlueCode.Tests/BlueCode.Tests.fsproj \
        tests/BlueCode.Tests/RouterTests.fs
git commit -m "test(34-02): add EditCommandTests with 5 mock-launcher unit tests"
```
NEVER `git add -A`.
  </action>
  <verify>
1. `dotnet build tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0.
2. `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | grep -E "EditCommand|Tests run"` shows the EditCommand tests execute and total count is `baseline + 5`.
3. `grep -c "testCase" tests/BlueCode.Tests/EditCommandTests.fs` returns at least 5.
4. `grep -n "EditCommandTests.fs" tests/BlueCode.Tests/BlueCode.Tests.fsproj` shows the entry placed BEFORE the line containing `RouterTests.fs`.
5. `grep -n "EditCommandTests.tests" tests/BlueCode.Tests/RouterTests.fs` returns exactly 1 line, inside the `rootTests` testList.
6. Specifically verify `Path.GetTempFileName` rename worked: the `.md extension` testCase passes (proves Plan 34-01's `Path.ChangeExtension(rawTmp, ".md") + File.Move(rawTmp, tmpPath)` is correct).
7. `git diff master -- src/BlueCode.Core/` is empty.
  </verify>
  <done>
- `EditCommandTests.fs` exists with 5 mock-launcher testCases inside `testList "EditCommand"`.
- `BlueCode.Tests.fsproj` Compile order: `EditCommandTests.fs` BEFORE `RouterTests.fs`.
- `RouterTests.fs` `rootTests` list includes `BlueCode.Tests.EditCommandTests.tests`.
- All 5 new tests discovered + passing; total suite count = baseline + 5.
- Commit `test(34-02): add EditCommandTests with 5 mock-launcher unit tests` recorded.
  </done>
</task>

<task type="auto">
  <name>Task 2: Add 2 ReplTests integration tests for /edit dispatch via mock launcher seam</name>
  <files>tests/BlueCode.Tests/ReplTests.fs, src/BlueCode.Cli/Repl.fs</files>
  <action>
**Step A — Add an injectable launcher seam to `src/BlueCode.Cli/Repl.fs`:**

The current `Some (Slash Edit) ->` arm (added in Plan 34-01) hard-codes `BlueCode.Cli.EditCommand.realEditorLauncher`. Tests cannot inject a mock launcher without spawning a real `$EDITOR`.

Pattern: introduce a module-level mutable cell `editorLauncherOverride : IEditorLauncher option` that defaults to `None` (production reads `realEditorLauncher`); tests set it before calling `runMultiTurn` and reset it in `finally`. Mirrors how `Console.SetIn` / `Console.SetOut` work for stdio redirection.

In `src/BlueCode.Cli/Repl.fs`, BEFORE `runSingleTurn`, add:

```fsharp
/// Test-only seam: when set to Some, the Slash Edit arm uses this launcher
/// instead of EditCommand.realEditorLauncher. Production never sets this.
/// Mirrors Console.SetIn/SetOut redirection for stdio.
/// Concurrent tests must use testSequenced (CLAUDE.md "Console.SetOut in tests"
/// rule generalizes to any process-level mutable cell).
let mutable editorLauncherOverride : BlueCode.Cli.EditCommand.IEditorLauncher option = None
```

In the `Some (Slash Edit) ->` arm body (added by Plan 34-01), replace the `BlueCode.Cli.EditCommand.realEditorLauncher` reference with:

```fsharp
let launcher =
    match editorLauncherOverride with
    | Some l -> l
    | None -> BlueCode.Cli.EditCommand.realEditorLauncher
let! contentOpt = BlueCode.Cli.EditCommand.openEditorAsync launcher
```

This is the MINIMUM seam — one mutable cell, one match expression. Production behavior identical to Plan 34-01 (override is `None`).

**Step B — Add 2 testCases to `tests/BlueCode.Tests/ReplTests.fs`** inside the existing `testSequenced (testList "Repl" [ ... ])` envelope. Place them adjacent to the "remaining future-stub command" testCase that Plan 34-01 just adapted (search for the line `testCase "runMultiTurn: 0 future-stub commands remaining` and add these AFTER it, BEFORE the `/sessions` testCase):

```fsharp
testCase "runMultiTurn: /edit with mock launcher writing 'list files' dispatches to LLM as next prompt (Phase 34 EDIT-01 SC-3)" <| fun () ->
    // Mock launcher writes scripted content; assert the LLM stub receives that
    // exact content as the prompt (proves /edit -> openEditorAsync -> handlePromptTurn -> runSingleTurn -> LLM dispatch).
    // Captures the messages sent to LLM via a recording stub (capturingLlm pattern).
    let capturedPrompts = System.Collections.Generic.List<string>()
    let recordingLlm : BlueCode.Core.Ports.ILlmClient =
        { new BlueCode.Core.Ports.ILlmClient with
            member _.CompleteAsync messages _model _ct =
                // The most-recent User message is the prompt being dispatched.
                let lastUser =
                    messages
                    |> List.tryFindBack (fun (m: BlueCode.Core.Domain.Message) ->
                        match m.Role with
                        | BlueCode.Core.Domain.Role.User -> true
                        | _ -> false)
                    |> Option.map (fun m -> m.Content)
                    |> Option.defaultValue ""
                capturedPrompts.Add(lastUser)
                // Return immediate FinalAnswer so the turn ends in 1 step.
                Task.FromResult(Ok (BlueCode.Tests.MockHelpers.makeMockResponse "done" (BlueCode.Core.Domain.FinalAnswer "done"))) }

    let mockLauncher : BlueCode.Cli.EditCommand.IEditorLauncher =
        { new BlueCode.Cli.EditCommand.IEditorLauncher with
            member _.Launch tmpPath =
                System.IO.File.WriteAllText(tmpPath, "list files\n") }

    let originalIn = Console.In
    let originalOut = Console.Out
    use stdinReader = new StringReader("/edit\n/exit\n")
    use stdoutWriter = new StringWriter()
    Console.SetIn(stdinReader)
    Console.SetOut(stdoutWriter)
    BlueCode.Cli.Repl.editorLauncherOverride <- Some mockLauncher

    let tempRoot =
        Path.Combine(Path.GetTempPath(), sprintf "bluecode-edit-%s" (Guid.NewGuid().ToString("N")))
    Directory.CreateDirectory(tempRoot) |> ignore
    let sinkPath =
        Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
    use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

    let components: AppComponents =
        { LlmClient = recordingLlm
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
        Expect.equal exitCode 0 "/edit dispatch + /exit -> exit code 0"
        Expect.equal capturedPrompts.Count 1
            (sprintf "exactly 1 LLM call made (one for the /edit-produced prompt); captured: %A" (capturedPrompts |> Seq.toList))
        Expect.equal capturedPrompts.[0] "list files"
            "LLM received the trimmed content from the mock launcher as the prompt"
    finally
        BlueCode.Cli.Repl.editorLauncherOverride <- None
        Console.SetIn(originalIn)
        Console.SetOut(originalOut)

testCase "runMultiTurn: /edit with mock launcher writing empty string -> 'Edit cancelled.' + 0 LLM calls (Phase 34 EDIT-01 SC-3)" <| fun () ->
    // Mock launcher writes empty string; assert REPL prints "Edit cancelled."
    // and the LLM stub is NEVER called (cancel path bypasses dispatch entirely).
    let mockLauncher : BlueCode.Cli.EditCommand.IEditorLauncher =
        { new BlueCode.Cli.EditCommand.IEditorLauncher with
            member _.Launch tmpPath =
                System.IO.File.WriteAllText(tmpPath, "") }

    let originalIn = Console.In
    let originalOut = Console.Out
    use stdinReader = new StringReader("/edit\n/exit\n")
    use stdoutWriter = new StringWriter()
    Console.SetIn(stdinReader)
    Console.SetOut(stdoutWriter)
    BlueCode.Cli.Repl.editorLauncherOverride <- Some mockLauncher

    let tempRoot =
        Path.Combine(Path.GetTempPath(), sprintf "bluecode-editc-%s" (Guid.NewGuid().ToString("N")))
    Directory.CreateDirectory(tempRoot) |> ignore
    let sinkPath =
        Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
    use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

    let components: AppComponents =
        { LlmClient = stubLlm []   // throws on first call — proves 0 LLM calls
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
        Expect.equal exitCode 0 "exit code 0 — empty content treated as cancel; REPL stays alive"
        Expect.stringContains captured "Edit cancelled."
            (sprintf "REPL printed cancel notice; captured:\n%s" captured)
    finally
        BlueCode.Cli.Repl.editorLauncherOverride <- None
        Console.SetIn(originalIn)
        Console.SetOut(originalOut)
```

**Verify the recordingLlm message-extraction logic against `BlueCode.Core.Domain.Message`:** Open `src/BlueCode.Core/Domain.fs` and confirm the `Message` type has `Role` and `Content` fields with `Role.User` discriminator. If field names differ, adjust the `tryFindBack` predicate accordingly. (This is the only "look up exact field name" item in this plan; everything else is locked.)

**What NOT to do:**
- Do NOT use the `realEditorLauncher` in either testCase — it would spawn `$EDITOR` and block CI. The injectable seam is the entire point.
- Do NOT use `Spectre.Console.AnsiConsole.Console <- ...` reset — these tests don't exercise PlanGate.render path (STATE.md Phase 33-02 decision: reset is required only for tests that exercise PlanGate through the REPL loop).
- Do NOT skip `BlueCode.Cli.Repl.editorLauncherOverride <- None` in `finally` — leakage to subsequent tests would cause cascade failures.
- Do NOT add the new tests OUTSIDE the existing `testSequenced (testList "Repl" [ ... ])` — they redirect `Console.SetIn`/`Console.SetOut` and MUST run sequentially with the other ReplTests (CLAUDE.md "Console.SetOut in tests").
- Do NOT add the new tests as a separate `let tests2 = testList ...` — they extend the existing `let tests` list. Just append two `testCase ...` items inside the existing brackets.

**Run the test suite:**
```bash
dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj   # because Repl.fs changed
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
```
Expected: test count INCREASES by 2 vs Task 1's baseline (so total this plan = +5 unit + 2 integration = +7 vs Plan 34-01 baseline).

**Atomic commits (Task 2 — split into 2 since both src/ and tests/ change for clarity):**
```bash
# Commit 2a: the src seam
git add src/BlueCode.Cli/Repl.fs
git commit -m "feat(34-02): add editorLauncherOverride test seam in Repl.fs"

# Commit 2b: the test cases
git add tests/BlueCode.Tests/ReplTests.fs
git commit -m "test(34-02): add 2 ReplTests integration tests for /edit dispatch (mock launcher)"
```
NEVER `git add -A`.
  </action>
  <verify>
1. `dotnet build` exits 0.
2. `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` reports `Errors: 0, Failures: 0` and total count = baseline (Plan 34-01 finish) + 7 (5 EditCommandTests + 2 ReplTests).
3. `grep -n "editorLauncherOverride" src/BlueCode.Cli/Repl.fs` returns 2 matches (1 mutable cell decl + 1 match site in Slash Edit arm).
4. `grep -c "editorLauncherOverride" tests/BlueCode.Tests/ReplTests.fs` returns at least 4 (2 sets to Some + 2 resets to None in finally).
5. `grep -n "Edit cancelled\." tests/BlueCode.Tests/ReplTests.fs` returns 1 match (the cancel-path testCase).
6. The recording-LLM testCase asserts `capturedPrompts.[0] = "list files"` (no trailing whitespace — proves the `Trim()` in `openEditorAsync`).
7. Phase 33's 6 plan-gate ReplTests testCases STILL pass (regression check — the Repl.fs seam addition is behavior-preserving for production).
8. `git log --oneline -2` shows `test(34-02): add 2 ReplTests integration tests...` and `feat(34-02): add editorLauncherOverride test seam...`.
  </verify>
  <done>
- `Repl.fs` has the `editorLauncherOverride` mutable cell + match-expression seam in the Slash Edit arm.
- `ReplTests.fs` contains 2 new testCases for /edit dispatch (success + cancel path), both inside the existing testSequenced envelope.
- Test count delta from Plan 34-02 = +7 (5 unit + 2 integration); cumulative Phase 34 delta = +7 (Plan 34-01 added 0).
- Both new ReplTests reset `editorLauncherOverride <- None` in `finally`.
- 2 atomic commits recorded for this task.
  </done>
</task>

<task type="auto">
  <name>Task 3: Run bench gate (7/7 PASS) and final phase checks</name>
  <files>(no files modified — verification only)</files>
  <action>
**Step A — Bench gate (the structural authority per CLAUDE.md):**

```bash
bash bench/run.sh --gate
```

Expected: `7/7 PASS` with byte-equal `bench/baseline.json`. The 7 labels are: `T6_122b W1_122b W2_122b T1_122b T5_122b B2_122b` + 1 (per CLAUDE.md "Bench" section).

The bench gate exercises NON-INTERACTIVE blueCode (no REPL, no slash commands, no /edit). Phase 34's changes are entirely additive in that path:
- `EditCommand.fs` ProcessExit handler registers on the first reference to the module — this happens during `BlueCode.Cli.fsproj` compilation, so the handler is in scope for every blueCode invocation. The handler does nothing (`currentTmpPath = None`) unless `/edit` was invoked.
- `Repl.fs` `handlePromptTurn` refactor preserves exact behavior of single-turn dispatch (Phase 33 plan-mode tests provide the regression fence).
- `Repl.fs` `editorLauncherOverride` is `None` in production — the Slash Edit arm reads `realEditorLauncher` exactly as it would have without the seam.

If the gate fails:
1. Capture `bench/runs/<latest-timestamp>/` logs and diff against `bench/baseline.json`.
2. Most likely cause: the `do AppDomain.CurrentDomain.ProcessExit.Add(...)` in `EditCommand.fs` runs at startup and somehow affects timing or stdout. Mitigation: confirm the handler body does nothing when `currentTmpPath = None` (it should already; verify the early return).
3. Less likely cause: the `handlePromptTurn` lift accidentally changed dispatch semantics. Verify by running `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` — Phase 33's 6 plan-gate tests should pass.
4. If neither — ROLLBACK Plan 34-02 changes (this task only) and re-investigate.

**Step B — Manual smoke (informational; not a blocking gate):**

Run the actual binary in a real terminal and verify the live `/edit` flow end-to-end:
```bash
dotnet run --project src/BlueCode.Cli
# At prompt:
> /help
# Verify /edit line shows: "/edit              open $EDITOR for multi-line input" (NO [coming in v2.5])

> /edit
# vi opens. Type ":q" + Enter (empty file). REPL should print "Edit cancelled." and return to prompt.

> /edit
# vi opens. Type "i", then "list files in current directory", then ESC ":wq" + Enter.
# REPL should dispatch "list files in current directory" to the LLM (real 122B call;
# expect a normal agent-loop response). Press Ctrl+C to interrupt if needed.

> /exit
```

This smoke is INFORMATIONAL — it cannot be automated (requires real $EDITOR + real 122B). Document any anomalies in the SUMMARY's "Manual verification notes" section. Real-TTY anomalies (e.g., terminal gibberish per research § Open Question #1) are surfaceable here; mitigation if observed: add `Console.Out.Flush()` before `launcher.Launch tmpPath` in `EditCommand.openEditorAsync`. If smoke succeeds clean, the open question is empirically closed.

**Step C — Final invariant checks:**

```bash
# Core purity: must be empty
git diff master -- src/BlueCode.Core/

# No async {} added: must exit 0
bash scripts/check-no-async.sh

# Test count: should be baseline + 7
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | grep "Tests run"

# All Phase 34 commits accounted for (run from the master-merge state):
git log --oneline master..HEAD
# Expected (Plan 34-01 + 34-02 = 6 commits, possibly +1 plan-meta from orchestrator):
#   test(34-02): add 2 ReplTests integration tests for /edit dispatch (mock launcher)
#   feat(34-02): add editorLauncherOverride test seam in Repl.fs
#   test(34-02): add EditCommandTests with 5 mock-launcher unit tests
#   test(34-01): adapt 2 existing tests for /edit live promotion
#   feat(34-01): wire /edit to EditCommand.openEditorAsync via handlePromptTurn refactor
#   feat(34-01): add IEditorLauncher port + openEditorAsync (EditCommand.fs)
```

**No commit for this task** — it's verification only. The phase-complete bundle commit (`docs(34): complete edit-multi-line-input phase`) is OUT of plan scope; that's an orchestrator concern after both plans complete.
  </action>
  <verify>
1. `bash bench/run.sh --gate` reports `7/7 PASS`. (Authoritative — if FAIL, plan is NOT done.)
2. `git diff master -- src/BlueCode.Core/` is empty (Core purity preserved).
3. `bash scripts/check-no-async.sh` exits 0.
4. Full test suite: `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` reports `Errors: 0, Failures: 0`.
5. Cumulative test count delta vs `master`: +7 (5 EditCommandTests + 2 ReplTests).
6. Manual smoke (informational): `/edit` opens an editor in real TTY; non-empty content dispatches to LLM; empty content prints "Edit cancelled." and returns to REPL.
7. `git log --oneline master..HEAD` shows the 6 Phase 34 commits in order.
  </verify>
  <done>
- Bench gate 7/7 PASS confirmed (the structural authority).
- Core purity preserved (`git diff master -- src/BlueCode.Core/` empty).
- All tests pass (count = baseline + 7).
- Manual smoke documented in SUMMARY (smoke is INFORMATIONAL; bench gate is the BLOCKING authority).
- Phase 34 ROADMAP success criteria SC-1..SC-6 all empirically validated by tests + bench.
  </done>
</task>

</tasks>

<verification>
**Plan-level verification gates (run AFTER all 3 tasks complete):**

1. **Build green for src + tests:**
   ```bash
   dotnet build
   ```

2. **Test suite green; +7 net delta:**
   ```bash
   dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
   ```
   Reports `Errors: 0, Failures: 0`; total count is baseline + 7.

3. **Bench gate 7/7 PASS (CLAUDE.md authority):**
   ```bash
   bash bench/run.sh --gate
   ```
   Exits 0; baseline.json byte-equal.

4. **Core purity preserved:**
   ```bash
   git diff master -- src/BlueCode.Core/
   ```
   Empty.

5. **Test discovery: EditCommandTests visible to runner:**
   ```bash
   dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | grep -c "EditCommand"
   ```
   Returns at least 5 (matches 5 testCases).

6. **No `async {}` added:**
   ```bash
   bash scripts/check-no-async.sh
   ```
   Exits 0.

7. **Editor seam properly isolated:**
   ```bash
   grep -c "editorLauncherOverride" src/BlueCode.Cli/Repl.fs
   ```
   Returns exactly 2 (1 decl + 1 match site).
</verification>

<success_criteria>
This plan completes Phase 34's ROADMAP success criteria (combined with Plan 34-01):

- **SC-1 (`/edit` invokes Path.GetTempFileName):** GREEN via `EditCommandTests` (.md extension testCase implicitly proves Path.GetTempFileName is called — `Path.ChangeExtension` requires it).
- **SC-2 ($EDITOR env var; vi fallback; friendly error):** GREEN — Plan 34-01 implementation; Plan 34-02 unit tests cover the launcher contract via mock; production fallback path is exercised by manual smoke (informational; deterministic mock test would require monkeypatching `Environment.GetEnvironmentVariable` which is out-of-scope ergonomic vs value).
- **SC-3 (non-empty -> next prompt; empty -> cancel):** GREEN — both ReplTests integration testCases assert this contract end-to-end (recordingLlm captures the dispatched prompt; empty path uses `stubLlm []` which throws on any call, proving 0 LLM calls).
- **SC-4 (tmpfile read-then-delete; atexit cleanup):** GREEN for try/finally cleanup (`EditCommandTests` "tmpfile deleted after read" testCase asserts `File.Exists !captured = false` post-call). Atexit ProcessExit handler is registered at module init (proven by `grep` in Plan 34-01 verify); cannot be unit-tested deterministically without forking the test process — covered by Plan 34-01's grep verify + manual smoke.
- **SC-5 (Ctrl+C during edit; child process exit; REPL recovers):** PARTIAL — `Repl.fs` `Some (Slash Edit)` arm wraps `openEditorAsync` with `Console.CancelKeyPress` handler that sets `args.Cancel <- true` (Plan 34-01 implementation). vi handles its own SIGINT; force-killing the editor produces the empty-file -> "Edit cancelled." path (covered by the Plan 34-02 cancel-path integration test). Manual-verification only for the actual SIGINT delivery to a running real $EDITOR (informational smoke item; not a blocking automated gate — noted in 34-02 SUMMARY's "Manual verification notes").
- **SC-6 (Bench gate 7/7 PASS preserved):** GREEN via Task 3 Step A — `bash bench/run.sh --gate` returns 0 with byte-equal baseline.json.

Phase 34 EDIT-01 requirement: COMPLETE (all 6 SCs satisfied or empirically informational with documented gap).
</success_criteria>

<output>
After completion, create `.planning/phases/34-edit-multi-line-input/34-02-SUMMARY.md` with the following frontmatter and body:

```yaml
---
phase: 34-edit-multi-line-input
plan: 02
status: complete
date: <YYYY-MM-DD>
subsystem: cli-repl
affects:
  - tests/BlueCode.Tests/EditCommandTests.fs (NEW)
  - tests/BlueCode.Tests/ReplTests.fs
  - tests/BlueCode.Tests/BlueCode.Tests.fsproj
  - tests/BlueCode.Tests/RouterTests.fs
  - src/BlueCode.Cli/Repl.fs   # editorLauncherOverride seam only
tests:
  added: 7   # 5 EditCommandTests + 2 ReplTests
  modified: 0
  deleted: 0
commits:
  - test(34-02): add EditCommandTests with 5 mock-launcher unit tests
  - feat(34-02): add editorLauncherOverride test seam in Repl.fs
  - test(34-02): add 2 ReplTests integration tests for /edit dispatch (mock launcher)
loc_delta:
  added: ~180
  removed: 0
core_diff: empty
bench_gate: PASS 7/7
test_count_delta: +7
---
```

Body sections (recommended):
- **What shipped** — 5 EditCommand unit tests (mock launcher: empty/non-empty/whitespace/tmpfile-cleanup/.md extension); editorLauncherOverride seam in Repl.fs (test-only mutable cell with finally reset); 2 ReplTests integration tests for /edit dispatch (success + cancel paths via mock launcher).
- **Bench gate result** — 7/7 PASS post-Phase-34; baseline.json byte-equal; ProcessExit handler registration at module init introduces zero observable bench impact.
- **Manual smoke notes** — record the actual $EDITOR used (vi / nvim / VS Code with --wait), whether real-TTY behavior was clean, whether the dotnet/runtime #91706 terminal gibberish issue manifested on macOS .NET 10 (Open Question #1 resolution).
- **Test count progression** — 352 (Plan 34-01 baseline) -> 359 (Phase 34 complete).
- **Key decisions captured** — editorLauncherOverride as test seam (mirrors Console.SetIn/SetOut convention; `testSequenced` requirement is inherited from existing testSequenced envelope; reset-in-finally is mandatory to prevent cross-test leakage); recording-LLM via `tryFindBack Role.User` (validates dispatch path through messages list, not just call count).
- **EDIT-01 requirement: COMPLETE** — all 6 SCs satisfied or documented as informational-only.
- **Pitfalls dodged** — testSequenced requirement (would race on console redirect); editorLauncherOverride leakage (every test resets in finally); EditCommandTests not registered (would silently produce 0 runs — required BOTH .fsproj entry AND rootTests append per STATE.md test-discovery decision).

After Plan 34-02 SUMMARY is written, the phase-complete bundle commit (`docs(34): complete edit-multi-line-input phase`) is the orchestrator's responsibility (CLAUDE.md "Commit protocol" — phase-complete commit bundles ROADMAP/STATE/REQUIREMENTS updates).
</output>
