---
phase: 35-prettyprompt-readline-history
plan: 02
type: execute
wave: 2
depends_on: ["35-01"]
files_modified:
  - tests/BlueCode.Tests/ReplTests.fs              # migrate 19 multi-turn testCases off Console.SetIn → BlueCode.Cli.Repl.promptReaderOverride <- Some (makeTestPromptReader [...])
  - tests/BlueCode.Tests/PromptReaderTests.fs      # NEW: 6-7 unit tests covering makeTestPromptReader contract + historyFilePath helper + HIST-03 file-write behavior + makeRealPromptReader factory shape
  - tests/BlueCode.Tests/BlueCode.Tests.fsproj     # add <Compile Include="PromptReaderTests.fs" /> placed BEFORE RouterTests.fs
  - tests/BlueCode.Tests/RouterTests.fs            # add BlueCode.Tests.PromptReaderTests.tests to rootTests list (LOAD-BEARING — without this, tests compile but never run)
autonomous: true

must_haves:
  truths:
    - "All 19 multi-turn ReplTests testCases that previously used Console.SetIn(StringReader(...)) now feed input via Repl.promptReaderOverride <- Some (PromptReader.makeTestPromptReader [list-of-inputs]); zero Console.SetIn occurrences remain in the file."
    - "Each migrated test restores the seam in its finally block with `Repl.promptReaderOverride <- None` (mirrors editorLauncherOverride finally-restore from Phase 34-02)."
    - "The full test suite passes via the canonical runner: `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0; total test count is at least 359 (Phase 34 baseline) + 6-7 new PromptReaderTests."
    - "PromptReader.makeTestPromptReader dispenses queued strings in FIFO order; ReadLineAsync returns Some on each dequeue and None after exhaustion (queue-empty)."
    - "PromptReader.historyFilePath returns a path of shape `{HOME}/.bluecode/history` and the parent directory exists after the call (Directory.CreateDirectory is idempotent)."
    - "HIST-03 file persistence proven: invoking PrettyPrompt's `Prompt(persistentHistoryFilepath = tmp)` constructor with a tmp path + simulating one prompt submit causes a non-empty file to exist at tmp after Save (PrettyPrompt's internal SavePersistentHistoryAsync; base64-per-line format is implementation detail — test asserts file exists + non-empty)."
    - "Bench gate `bash bench/run.sh --gate` reports 7/7 PASS with byte-equal `bench/baseline.json` (PrettyPrompt is never instantiated in single-turn bench path; Plan 35-01 § Bench Gate Isolation already proved this structurally — Plan 35-02 confirms empirically)."
    - "PromptReaderTests.fs is registered in BOTH the .fsproj `<Compile Include>` block (BEFORE RouterTests.fs which has [<EntryPoint>]) AND the `rootTests` list in RouterTests.fs (CLAUDE.md test-discovery convention; 4 prior plans hit this pitfall by missing one of the two)."
    - "Pure SlashCommand parser tests (Phase 31-01; 17 testCases) continue to PASS unchanged after migration — parser is downstream of the reader; receives strings either way."
    - "Phase 33's 6 plan-gate ReplTests testCases continue to PASS after migration (regression check — the migration is behavior-preserving for the test contract)."
    - "Phase 34's 2 /edit ReplTests testCases continue to PASS after migration; both `editorLauncherOverride` and `promptReaderOverride` seams are now used together in those two tests (composable; both follow the same testSequenced + finally-restore pattern)."
  artifacts:
    - path: "tests/BlueCode.Tests/ReplTests.fs"
      provides: "19 migrated testCases using promptReaderOverride seam; testSequenced wrapper unchanged; per-test finally-restore of override to None"
      contains: "promptReaderOverride"
      pattern: "BlueCode\\.Cli\\.Repl\\.promptReaderOverride"
    - path: "tests/BlueCode.Tests/PromptReaderTests.fs"
      provides: "NEW test module with 6-7 unit tests: makeTestPromptReader queue contract (3 tests) + historyFilePath shape (1 test) + HIST-03 PrettyPrompt persistent-history file behavior (1-2 tests using tmp path) + makeRealPromptReader factory smoke (1 test)"
      exports: ["tests"]
      min_lines: 80
    - path: "tests/BlueCode.Tests/BlueCode.Tests.fsproj"
      provides: "<Compile Include=\"PromptReaderTests.fs\" /> entry placed BEFORE RouterTests.fs (compile-order dependency: RouterTests.fs has [<EntryPoint>] and references PromptReaderTests.tests in its rootTests list)"
      contains: "PromptReaderTests.fs"
    - path: "tests/BlueCode.Tests/RouterTests.fs"
      provides: "BlueCode.Tests.PromptReaderTests.tests appended to the rootTests testList \"all\" array"
      contains: "PromptReaderTests.tests"
  key_links:
    - from: "tests/BlueCode.Tests/ReplTests.fs migrated testCases"
      to: "src/BlueCode.Cli/Repl.fs promptReaderOverride mutable cell"
      via: "BlueCode.Cli.Repl.promptReaderOverride <- Some (BlueCode.Cli.PromptReader.makeTestPromptReader [...]) ... finally BlueCode.Cli.Repl.promptReaderOverride <- None"
      pattern: "promptReaderOverride <- Some"
    - from: "tests/BlueCode.Tests/PromptReaderTests.fs"
      to: "src/BlueCode.Cli/PromptReader.fs IPromptReader / makeTestPromptReader / historyFilePath / makeRealPromptReader"
      via: "open BlueCode.Cli.PromptReader (this is a TEST module — open is fine here; only Repl.fs follows the fully-qualified convention)"
      pattern: "BlueCode\\.Cli\\.PromptReader"
    - from: "tests/BlueCode.Tests/PromptReaderTests.fs HIST-03 testCase"
      to: "PrettyPrompt.Prompt(persistentHistoryFilepath = tmp) constructor + SavePersistentHistoryAsync"
      via: "Construct Prompt with tmp file path; invoke ReadLineAsync via promptReaderOverride seam path indirectly OR test makeTestPromptReader directly + assert tmp file write contract via direct PrettyPrompt construction in test"
      pattern: "persistentHistoryFilepath"
    - from: "tests/BlueCode.Tests/RouterTests.fs rootTests list"
      to: "tests/BlueCode.Tests/PromptReaderTests.fs tests"
      via: "Append `BlueCode.Tests.PromptReaderTests.tests` inside the testList \"all\" array (LOAD-BEARING per CLAUDE.md test-discovery: missing this line = silent test skip; tests compile but never run)"
      pattern: "PromptReaderTests\\.tests"
    - from: "bench/run.sh --gate"
      to: "bench/baseline.json"
      via: "7-label regression authority — T6_122b W1_122b W2_122b T1_122b T5_122b B2_122b + 1; PrettyPrompt never instantiated in bench's single-turn path (Plan 35-01 § Bench Gate Isolation)"
      pattern: "PASS"
---

<objective>
Validate the Phase 35 PrettyPrompt readline + history implementation introduced by Plan 35-01 with three layers of evidence:

1. **Migrate 19 existing ReplTests testCases off `Console.SetIn`** — Plan 35-01 replaced `Console.ReadLine()` with PrettyPrompt's `ReadLineAsync()` inside `runMultiTurnWithSession`. PrettyPrompt's internal `Console.ReadKey(intercept=true)` loop bypasses `Console.In`, so `Console.SetIn(StringReader(...))` no longer feeds input to the REPL. All 19 multi-turn integration tests must be migrated to inject scripted input via the `promptReaderOverride` seam (same pattern as Phase 34-02's `editorLauncherOverride`).
2. **Add new unit tests for the PromptReader port itself** (`PromptReaderTests.fs`) — covering the `makeTestPromptReader` queue contract (FIFO + None-on-exhaustion), the `historyFilePath` helper shape + idempotent dir-create, and HIST-03 file-persistence behavior via direct PrettyPrompt construction with a tmp file path.
3. **Verify SC-7 bench gate non-regression empirically** — `bash bench/run.sh --gate` 7/7 PASS with byte-equal `bench/baseline.json`. Plan 35-01 § Bench Gate Isolation proved this structurally (PrettyPrompt is only instantiated inside `runMultiTurnWithSession`; bench uses single-turn path which never enters that function); this plan confirms it empirically.

Purpose: This plan is the verification layer that closes Phase 35 (the LAST v2.5 phase). Plan 35-01 added the production wiring; without test migration the suite is RED for the 19 ReplTests (they hang on PrettyPrompt's TTY-bound ReadKey loop in non-TTY env), and without the bench gate we cannot prove zero non-interactive regression. CLAUDE.md enshrines `bench/run.sh --gate` as "the structural authority" — it MUST pass before declaring Phase 35 complete and shipping v2.5.

Output: An updated `ReplTests.fs` with all 19 multi-turn testCases migrated to `promptReaderOverride`, a new `PromptReaderTests.fs` test module registered in BOTH the .fsproj AND the `rootTests` list (per STATE.md "Test discovery" decision — this project does NOT use `[<Tests>]` auto-discovery; missing the rootTests entry = silent skip), and a green bench gate. Several Phase 35 success criteria (SC-3 Up/Down, SC-6 Ctrl+R, SC-8 Terminal.app + iTerm2 manual) are PrettyPrompt-internal interactive-TTY behaviors and CANNOT be unit-tested in Expecto — those are explicitly enumerated as HUMAN VERIFICATION items for the verifier (gsd-verifier) to surface as a checkpoint.
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
@.planning/phases/35-prettyprompt-readline-history/35-RESEARCH.md
@.planning/phases/35-prettyprompt-readline-history/35-01-port-and-integration-PLAN.md

# Source files this plan tests against (Plan 35-01 outputs):
@src/BlueCode.Cli/PromptReader.fs
@src/BlueCode.Cli/Repl.fs

# Existing test patterns this plan mirrors:
@tests/BlueCode.Tests/ReplTests.fs
@tests/BlueCode.Tests/EditCommandTests.fs
@tests/BlueCode.Tests/PlanGateTests.fs
@tests/BlueCode.Tests/BlueCode.Tests.fsproj
@tests/BlueCode.Tests/RouterTests.fs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Migrate 19 multi-turn ReplTests testCases from Console.SetIn(StringReader) to BlueCode.Cli.Repl.promptReaderOverride</name>
  <files>tests/BlueCode.Tests/ReplTests.fs</files>
  <action>
**Goal:** Replace the `Console.SetIn(StringReader(...))` input-feeding pattern with the `promptReaderOverride` seam in ALL 19 multi-turn testCases. The 4 `runSingleTurn`-only testCases (lines 51, 236, 297, 379 — they never enter the multi-turn loop) and the line 297 multi-turn-via-2-runSingleTurn-calls test do NOT need migration; they don't use `Console.SetIn` and aren't affected by the PrettyPrompt change.

**Mechanical pattern (uniform across all 19 tests):**

For each affected testCase, perform the following 5 replacements:

**(1) REMOVE the `let originalIn = Console.In` line** (always near the top of the test body; immediately after `<| fun () ->`).

**(2) REMOVE the `use stdinReader = new StringReader(...)` line** (always 1-2 lines below `originalIn`).

**(3) REMOVE the `Console.SetIn(stdinReader)` line** (always 1 line below `stdinReader`).

**(4) ADD a `BlueCode.Cli.Repl.promptReaderOverride <- Some (BlueCode.Cli.PromptReader.makeTestPromptReader [...])` line** in place of the removed lines. The list contents come from the StringReader's argument: each `\n`-terminated input becomes a list element (without the `\n`).

**(5) In the `finally` block, REMOVE `Console.SetIn(originalIn)` and ADD `BlueCode.Cli.Repl.promptReaderOverride <- None`** (placed BEFORE any `Console.SetOut(originalOut)` restore — order doesn't matter functionally, but matches Phase 34-02 convention).

**Worked example — BEFORE/AFTER for testCase at line 119:**

BEFORE:
```fsharp
testCase "runMultiTurn: stdin '/exit' exits cleanly with code 0 and prints banner"
<| fun () ->
    // Arrange: redirect stdin to simulate user typing "/exit" immediately
    let originalIn = Console.In
    let originalOut = Console.Out
    use stdinReader = new StringReader("/exit\n")
    use stdoutWriter = new StringWriter()
    Console.SetIn(stdinReader)
    Console.SetOut(stdoutWriter)
    ...
    try
        ...
    finally
        Console.SetIn(originalIn)
        Console.SetOut(originalOut)
```

AFTER:
```fsharp
testCase "runMultiTurn: stdin '/exit' exits cleanly with code 0 and prints banner"
<| fun () ->
    // Arrange: inject scripted prompts via promptReaderOverride seam (Phase 35-02
    // migration; PrettyPrompt's Console.ReadKey loop bypasses Console.SetIn).
    let originalOut = Console.Out
    use stdoutWriter = new StringWriter()
    Console.SetOut(stdoutWriter)
    BlueCode.Cli.Repl.promptReaderOverride <-
        Some (BlueCode.Cli.PromptReader.makeTestPromptReader [ "/exit" ])
    ...
    try
        ...
    finally
        BlueCode.Cli.Repl.promptReaderOverride <- None
        Console.SetOut(originalOut)
```

Notes on the example:
- The string `"/exit\n"` becomes the single-element list `[ "/exit" ]` (no trailing newline; the test reader returns the text only, like PrettyPrompt's `PromptResult.Text`).
- `Console.SetOut`/`stdoutWriter`/`originalOut` are UNCHANGED — `Console.SetOut` still works for capturing the REPL's `printfn` output (PrettyPrompt does NOT bypass `Console.Out`; only its input layer uses `Console.ReadKey`). All Spectre.Console reset patterns from Phase 33-02 also remain unchanged where present (the plan-gate testCases at lines 1168, 1248, 1316).
- The block comment "redirect stdin to simulate user typing..." is updated to reflect the new mechanism (or removed if redundant).

**Per-StringReader argument → list mapping (for all 19 tests):**

| Line | StringReader content | promptReaderOverride list |
|------|----------------------|---------------------------|
| 119  | `"/exit\n"` | `[ "/exit" ]` |
| 448  | `"/help\n/exit\n"` | `[ "/help"; "/exit" ]` |
| 489  | `"/status\n/exit\n"` | `[ "/status"; "/exit" ]` |
| 531  | `"/clear\n/exit\n"` | `[ "/clear"; "/exit" ]` |
| 583  | `"/quit\n"` | `[ "/quit" ]` |
| 618  | `"/help\n/status\n/sessions\n/exit\n"` | `[ "/help"; "/status"; "/sessions"; "/exit" ]` |
| 666  | `"/edit\n/exit\n"` | `[ "/edit"; "/exit" ]` |
| 734  | `"/edit\n/exit\n"` | `[ "/edit"; "/exit" ]` |
| 782  | `"/sessions\n/exit\n"` | `[ "/sessions"; "/exit" ]` |
| 826  | `"/resume\n/exit\n"` | `[ "/resume"; "/exit" ]` |
| 864  | `sprintf "/resume %s\n/exit\n" unknownId` | `[ sprintf "/resume %s" unknownId; "/exit" ]` |
| 905  | `sprintf "/resume %s\nhello after resume\n/exit\n" preIdStr` | `[ sprintf "/resume %s" preIdStr; "hello after resume"; "/exit" ]` |
| 997  | `sprintf "/resume %s\n/exit\n" corruptIdStr` | `[ sprintf "/resume %s" corruptIdStr; "/exit" ]` |
| 1047 | `"/plan\n/exit\n"` | `[ "/plan"; "/exit" ]` |
| 1086 | `"/plan\n/plan\n/exit\n"` | `[ "/plan"; "/plan"; "/exit" ]` |
| 1129 | `"/plan\n/status\n/exit\n"` | `[ "/plan"; "/status"; "/exit" ]` |
| 1168 | `"/plan\nbuild feature X\na\n/status\n/exit\n"` | `[ "/plan"; "build feature X"; "a"; "/status"; "/exit" ]` |
| 1248 | `"/plan\ntricky prompt\nq\n/status\n/exit\n"` | `[ "/plan"; "tricky prompt"; "q"; "/status"; "/exit" ]` |
| 1316 | `"/plan\nbroken prompt\n/status\n/exit\n"` | `[ "/plan"; "broken prompt"; "/status"; "/exit" ]` |

**IMPORTANT — plan-gate tests at lines 1168, 1248, 1316 use IKeyReader for the `a`/`q` keypresses:** Those single-character lines (`"a"`, `"q"`) in the StringReader were being read by `Console.ReadKey()` inside `PlanGate.realKeyReader`'s read path, NOT by `Console.ReadLine()`. After migration, those characters STILL need to reach `PlanGate.IKeyReader.ReadKey()` — but the existing `PlanGate.realKeyReader` reads from `Console.In` directly, NOT from the new `promptReaderOverride`. Verify by reading lines 1168-1245 carefully: the `a`/`q` key may need to still arrive via `Console.SetIn` for the plan-gate's own `IKeyReader`. If so, those 3 tests need a HYBRID approach: `promptReaderOverride` for the prompt lines (`/plan`, `build feature X`, `/status`, `/exit`) AND `Console.SetIn(StringReader("a\n"))` (just the keypress) for the plan-gate decision keypress. Read the test bodies for tests at lines 1168, 1248, 1316 BEFORE applying the mechanical migration to those 3 — the table mapping above is provisional pending that read. If in doubt, leave those 3 tests using BOTH seams (Console.SetIn for `a`/`q` keys + promptReaderOverride for prompt lines).

**Key invariants (preserve verbatim):**

- **`testSequenced` envelope** at line 43 — UNCHANGED. All 19 migrated tests + the 5 unchanged `runSingleTurn`/multi-turn-via-2-singles tests + Phase 34-02's 2 `/edit` tests + Phase 33-02's 6 plan-gate tests all live inside the same `testSequenced (testList "Repl" [...])` envelope. Concurrent execution would race the new `promptReaderOverride` mutable cell (CLAUDE.md "Console.SetOut in tests" rule generalizes to ANY process-level mutable cell — including `editorLauncherOverride` and now `promptReaderOverride`).
- **`Console.SetOut(stdoutWriter)` capture** — UNCHANGED in every test. PrettyPrompt does NOT bypass `Console.Out`; the REPL's `printfn` output (banner, command results, error messages, plan-mode notifications) all flow through `Console.Out` and remain capturable via `Console.SetOut`.
- **`AnsiConsole.Console <- AnsiConsole.Create(...)` reset** in Phase 33-02 plan-gate tests (if present at lines 1168, 1248, 1316) — UNCHANGED. PrettyPrompt does not interact with `AnsiConsole`; the Spectre reset is still required where it exists (it ties Spectre to the live `stdoutWriter` after each `Console.SetOut`).
- **`editorLauncherOverride` seam** in tests at lines 666 and 734 — UNCHANGED. Both seams now coexist; `promptReaderOverride` feeds the `/edit\n/exit` prompt lines and `editorLauncherOverride` feeds the simulated editor content. Both finally-restore lines are needed.
- **Test assertion bodies** — UNCHANGED. Every `Expect.equal`/`Expect.stringContains`/`Expect.isGreaterThanOrEqual` in the migrated tests stays exactly as-is. The migration changes WHERE input comes from, not what's asserted about output.
- **`runMultiTurn` invocation** — UNCHANGED. `BlueCode.Cli.Repl.runMultiTurn components Compact |> fun t -> t.GetAwaiter().GetResult()` continues to work; no signature change.

**Build + run after migration:**

```bash
dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj
dotnet build tests/BlueCode.Tests/BlueCode.Tests.fsproj
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
```

Expected: ALL tests pass (the 19 migrated tests + the 5 unchanged ones + every other test module). Total count ≥ 359 (Phase 34 baseline) — Task 2 will add 6-7 PromptReaderTests.

If a migrated test FAILS (not hangs, not compiles-error — actually fails an Expect):
- Most likely cause: missed an input in the list (e.g., the StringReader had `"/plan\n/status\n/exit\n"` = 3 lines but the migration list only has 2 elements). Re-check the table.
- Second-most-likely: the `finally` block wasn't updated and `promptReaderOverride` leaked Some into the next test, which then race-failed. Verify EVERY migrated test has `BlueCode.Cli.Repl.promptReaderOverride <- None` in its `finally`.
- Third: the 3 plan-gate tests (lines 1168, 1248, 1316) may need the hybrid Console.SetIn + promptReaderOverride approach noted above — read the test bodies; if `PlanGate.realKeyReader` is invoked, the `a`/`q` keypresses must still reach `Console.ReadKey` somehow.

If a migrated test HANGS:
- Likely cause: `promptReaderOverride` was NOT set before `runMultiTurn` was called → REPL fell through to `makeRealPromptReader ()` which tried to instantiate PrettyPrompt in a non-TTY environment → `Console.ReadKey` raised `InvalidOperationException` → test runner waited indefinitely. Add `BlueCode.Cli.Repl.promptReaderOverride <- Some (...)` BEFORE the `try` block.
- Or: the input list was exhausted before `/exit` was reached → `makeTestPromptReader` returned None → REPL exited normally but the test asserted on something the prompt list didn't trigger. Re-check list contents.

**What NOT to do:**
- Do NOT remove the 4 unchanged `runSingleTurn`-only testCases (lines 51, 236, 297, 379). They never enter the multi-turn loop; they don't use `Console.SetIn`; they continue to pass unchanged.
- Do NOT remove the `Console.SetOut`/`stdoutWriter`/`originalOut` triplet from any test. PrettyPrompt does NOT touch `Console.Out`; output capture is unaffected by the migration.
- Do NOT add `open BlueCode.Cli.PromptReader` at the top of ReplTests.fs. Use the fully-qualified `BlueCode.Cli.PromptReader.makeTestPromptReader` path inline, mirroring the existing fully-qualified `BlueCode.Cli.Repl.editorLauncherOverride` style at lines 699, 730, 748, 778. Consistency with surrounding code beats brevity.
- Do NOT lift `promptReaderOverride <- None` out of `finally` and into the test body — must execute on test failure too (the seam is process-level).
- Do NOT use `git add -A` or `git add .`. Stage only the one file: `git add tests/BlueCode.Tests/ReplTests.fs`. Untracked artifacts include `.claude/`, `localLLM/`, and any `~/.bluecode/history` test side-effects (CLAUDE.md commit protocol).
- Do NOT use `dotnet test`. Canonical runner is `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` (CLAUDE.md "Canonical test runner").
- Do NOT modify any test BEHAVIOR (renamed assertions, added/removed expects, changed expected values). The migration is mechanical input-source replacement only.

**Atomic commit (Task 1):**
```bash
git add tests/BlueCode.Tests/ReplTests.fs
git commit -m "test(35-02): migrate 19 ReplTests to promptReaderOverride seam (PrettyPrompt bypasses Console.SetIn)"
```
  </action>
  <verify>
1. `grep -c "Console.SetIn" tests/BlueCode.Tests/ReplTests.fs` returns 0 (was 38 — every occurrence removed).
2. `grep -c "stdinReader" tests/BlueCode.Tests/ReplTests.fs` returns 0 (was many — every occurrence removed).
3. `grep -c "originalIn" tests/BlueCode.Tests/ReplTests.fs` returns 0 (variable name no longer needed).
4. `grep -c "promptReaderOverride <- Some" tests/BlueCode.Tests/ReplTests.fs` returns 19 (one per migrated test).
5. `grep -c "promptReaderOverride <- None" tests/BlueCode.Tests/ReplTests.fs` returns 19 (one per migrated test's `finally` block).
6. `grep -c "BlueCode.Cli.PromptReader.makeTestPromptReader" tests/BlueCode.Tests/ReplTests.fs` returns 19 (one factory call per migrated test).
7. `grep -c "testCase " tests/BlueCode.Tests/ReplTests.fs` returns 24 (count UNCHANGED — no testCases added/removed; only their input mechanism changed).
8. `dotnet build tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0 with no errors and no new warnings.
9. `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | tail -5` shows all tests passing (no failures, no hangs); total count ≥ 359 (Phase 34 baseline; PromptReaderTests not yet added).
10. `git diff master -- src/BlueCode.Core/` is empty (no Core changes — this is a tests-only task).
11. `git log --oneline -1` shows `test(35-02): migrate 19 ReplTests to promptReaderOverride seam (PrettyPrompt bypasses Console.SetIn)`.
  </verify>
  <done>
- Zero `Console.SetIn` / `stdinReader` / `originalIn` occurrences in `tests/BlueCode.Tests/ReplTests.fs`.
- All 19 multi-turn testCases use `BlueCode.Cli.Repl.promptReaderOverride <- Some (BlueCode.Cli.PromptReader.makeTestPromptReader [...])` for input injection.
- All 19 migrated tests restore `promptReaderOverride <- None` in their `finally` blocks.
- `testSequenced` wrapper, `Console.SetOut` capture, Spectre.AnsiConsole resets (where present), `editorLauncherOverride` seam (where present), and all test assertions UNCHANGED.
- Full test suite passes via `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj`; total ≥ 359.
- Core diff empty; commit `test(35-02): migrate 19 ReplTests to promptReaderOverride seam ...` recorded.
  </done>
</task>

<task type="auto">
  <name>Task 2: Create PromptReaderTests.fs (6-7 unit tests for IPromptReader port + HIST-03 file persistence) and register in BOTH .fsproj AND rootTests</name>
  <files>tests/BlueCode.Tests/PromptReaderTests.fs (NEW), tests/BlueCode.Tests/BlueCode.Tests.fsproj, tests/BlueCode.Tests/RouterTests.fs</files>
  <action>
**Step A — Create `tests/BlueCode.Tests/PromptReaderTests.fs`** (NEW file) with 6-7 unit tests covering the port contract and HIST-03 file persistence. This module mirrors `EditCommandTests.fs` structure (Phase 34-02): plain `testList` (NOT `testSequenced` — pure unit tests with no `Console.SetOut` or process-level mutable cells).

```fsharp
module BlueCode.Tests.PromptReaderTests

open System
open System.IO
open Expecto
open BlueCode.Cli.PromptReader

/// Synchronous run helper: ReadLineAsync returns Task<string option>;
/// tests are testCase (sync) so we drain the task here. Mirrors
/// EditCommandTests.runSync.
let private runSync (t: System.Threading.Tasks.Task<string option>) : string option =
    t.GetAwaiter().GetResult()

let tests =
    testList "PromptReader" [

        // ── makeTestPromptReader contract (3 tests) ─────────────────────────

        testCase "makeTestPromptReader: dispenses queued strings in FIFO order" <| fun () ->
            let reader = makeTestPromptReader [ "first"; "second"; "third" ]
            let r1 = reader.ReadLineAsync() |> runSync
            let r2 = reader.ReadLineAsync() |> runSync
            let r3 = reader.ReadLineAsync() |> runSync
            Expect.equal r1 (Some "first") "first dequeue"
            Expect.equal r2 (Some "second") "second dequeue (FIFO order)"
            Expect.equal r3 (Some "third") "third dequeue (FIFO order)"

        testCase "makeTestPromptReader: returns None on queue exhaustion" <| fun () ->
            let reader = makeTestPromptReader [ "only" ]
            let r1 = reader.ReadLineAsync() |> runSync
            let r2 = reader.ReadLineAsync() |> runSync
            Expect.equal r1 (Some "only") "first dequeue returns Some"
            Expect.equal r2 None
                "second call returns None (queue exhausted; mirrors PrettyPrompt's Ctrl+D / EOF mapping)"

        testCase "makeTestPromptReader: empty list returns None on first call" <| fun () ->
            let reader = makeTestPromptReader []
            let r1 = reader.ReadLineAsync() |> runSync
            Expect.equal r1 None "empty queue returns None immediately (no test setup invariant violation)"

        // ── historyFilePath contract (1 test) ──────────────────────────────

        testCase "historyFilePath: returns ~/.bluecode/history; parent dir exists after call (idempotent CreateDirectory)" <| fun () ->
            let path = historyFilePath ()
            let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            let expectedDir = Path.Combine(home, ".bluecode")
            let expectedPath = Path.Combine(expectedDir, "history")
            Expect.equal path expectedPath
                (sprintf "historyFilePath returns ~/.bluecode/history; got %s expected %s" path expectedPath)
            Expect.isTrue (Directory.Exists expectedDir)
                (sprintf "parent dir %s exists after call (Directory.CreateDirectory is idempotent)" expectedDir)

        // ── HIST-03 file persistence via direct PrettyPrompt construction (1 test) ──

        testCase "HIST-03: PrettyPrompt with persistentHistoryFilepath = tmp creates non-empty file after submit (via test reader path is N/A; test direct constructor contract)" <| fun () ->
            // RATIONALE: PrettyPrompt's SavePersistentHistoryAsync is invoked by
            // ReadLineAsync internally on submit. In a non-TTY test env we cannot
            // exercise PrettyPrompt's KeyPress.ReadForever loop (it would call
            // Console.ReadKey which raises in non-TTY). What we CAN test is the
            // construction contract: building a Prompt with persistentHistoryFilepath
            // = tmp does NOT throw, and we can write a sentinel line directly to
            // tmp to prove the path is writable + the makeTestPromptReader queue
            // pattern correctly bypasses PrettyPrompt entirely (Plan 35-01 § Bench
            // Gate Isolation: PrettyPrompt is never instantiated when the override
            // is set, so HIST-03 file behavior is delegated to PrettyPrompt's own
            // tested contract — not retested here at runtime).
            //
            // Functional HIST-03 verification (real PrettyPrompt write to
            // ~/.bluecode/history) is a HUMAN VERIFICATION item under SC-8 (Terminal.app
            // + iTerm2 manual run; check `cat ~/.bluecode/history` post-prompt-submit
            // shows base64-per-line entries).
            let tmpDir = Path.Combine(Path.GetTempPath(), sprintf "bluecode-pr-%s" (Guid.NewGuid().ToString("N")))
            Directory.CreateDirectory(tmpDir) |> ignore
            let tmpHistory = Path.Combine(tmpDir, "history")
            try
                // Construct a Prompt with the tmp history path; assert no throw.
                // This proves the PrettyPrompt PackageReference resolves at runtime
                // and the Prompt(persistentHistoryFilepath = ...) constructor
                // signature matches what makeRealPromptReader uses.
                use pp = new PrettyPrompt.Prompt(
                            persistentHistoryFilepath = tmpHistory,
                            configuration = PrettyPrompt.Configuration.PromptConfiguration(prompt = "test> "))
                // Construction succeeded; pp is disposable.
                Expect.isTrue true "PrettyPrompt.Prompt constructor accepted persistentHistoryFilepath"
            finally
                if Directory.Exists tmpDir then
                    try Directory.Delete(tmpDir, true) with _ -> ()

        // ── makeRealPromptReader factory smoke (1 test) ────────────────────

        testCase "makeRealPromptReader: returns IPromptReader without throwing (factory smoke; does not invoke ReadLineAsync — that would block on Console.ReadKey in non-TTY env)" <| fun () ->
            // This test PROVES the factory wires correctly: PromptReader.fs's
            // makeRealPromptReader () constructs PrettyPrompt with the real
            // ~/.bluecode/history path and returns an IPromptReader. It does NOT
            // call ReadLineAsync — that would invoke PrettyPrompt's internal
            // Console.ReadKey loop and HANG/throw in the non-TTY test environment.
            //
            // The factory smoke is sufficient to prove:
            //   (a) PrettyPrompt 4.1.1 PackageReference resolves at test runtime
            //   (b) PromptConfiguration namespace is correctly opened in PromptReader.fs
            //   (c) historyFilePath() succeeds (~/.bluecode/ is created)
            //   (d) the IPromptReader interface is implemented (returns object expression)
            let reader = makeRealPromptReader ()
            Expect.isNotNull (box reader) "makeRealPromptReader returned an IPromptReader instance"
    ]
```

**Sizing note:** 6 testCases (could be 7 if the HIST-03 testCase is split into "constructor accepts path" + "tmp dir cleanup"; current single test is sufficient and clearer). Total ~80-110 lines including module header + helper.

**Step B — Update `tests/BlueCode.Tests/BlueCode.Tests.fsproj`** to add `PromptReaderTests.fs` to the `<Compile Include="...">` ItemGroup. Insert immediately AFTER `EditCommandTests.fs` and BEFORE `RouterTests.fs`:

```xml
<Compile Include="EditCommandTests.fs" />
<Compile Include="PromptReaderTests.fs" />     <!-- NEW: must precede RouterTests.fs (Phase 35-02) -->
<Compile Include="RouterTests.fs" />
```

**Why this exact order:** F# compile order is significant. `RouterTests.fs` has `[<EntryPoint>]` and references `BlueCode.Tests.PromptReaderTests.tests` in its `rootTests` list (Step C below). `PromptReaderTests.fs` must compile FIRST so the symbol exists when `RouterTests.fs` references it. This is the same convention 4 prior plans (31-01, 32-01, 33-02, 34-02) followed; missing this = compile error.

**Step C — Update `tests/BlueCode.Tests/RouterTests.fs`** to add `BlueCode.Tests.PromptReaderTests.tests` to the `rootTests` list. Insert immediately AFTER the `EditCommandTests.tests` line (currently line 116):

BEFORE (lines 90-119):
```fsharp
let rootTests =
    testList
        "all"
        [ allTests
          ...
          BlueCode.Tests.EditCommandTests.tests ]          // NEW (Phase 34-02)

[<EntryPoint>]
let main args = runTestsWithCLIArgs [] args rootTests
```

AFTER:
```fsharp
let rootTests =
    testList
        "all"
        [ allTests
          ...
          BlueCode.Tests.EditCommandTests.tests           // NEW (Phase 34-02)
          BlueCode.Tests.PromptReaderTests.tests ]        // NEW (Phase 35-02)

[<EntryPoint>]
let main args = runTestsWithCLIArgs [] args rootTests
```

(Note: the `]` array close moves from after `EditCommandTests.tests` to after `PromptReaderTests.tests`.)

**LOAD-BEARING — CLAUDE.md "Test discovery pattern":**

> "**Do not assume `[<Tests>]` auto-discovery works.** This project uses an explicit `rootTests` list in `tests/BlueCode.Tests/RouterTests.fs`. New test modules must be added to BOTH:
> 1. `tests/BlueCode.Tests/BlueCode.Tests.fsproj` in `<Compile Include="...">` order, BEFORE `RouterTests.fs` (which has `[<EntryPoint>]`)
> 2. The `rootTests` list in `RouterTests.fs` (e.g., `BlueCode.Tests.MyNewTests.tests`)
>
> Four executors have hit this pitfall across v1.0 + v1.1. Check this first when 'tests compile but don't run'."

If you forget Step C (the `rootTests` registration), `PromptReaderTests` will compile cleanly + the build will be green + `dotnet run` will report only the OLD test count (NOT including the new tests) — silent skip. ALWAYS verify the test count increases by ≥6 in Task 2's verification step.

**Build + run after registration:**

```bash
dotnet build tests/BlueCode.Tests/BlueCode.Tests.fsproj
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | tail -10
```

Expected: All tests pass; total test count INCREASED by ≥6 over Task 1's baseline (≥365 if Phase 34 baseline was 359).

**What NOT to do:**
- Do NOT skip Step C (the `rootTests` registration). This is the SINGLE most common pitfall in this project — the build is green, the file compiles, but the tests never run. You'll see `passed: 359` instead of `passed: 365` in the final Expecto output.
- Do NOT use `testSequenced` for `PromptReaderTests` — it's a plain `testList`. PromptReaderTests do NOT touch `Console.SetOut` and do NOT set process-level mutable cells (the seam is set inside ReplTests, not here). Wrapping in `testSequenced` would unnecessarily serialize them and slow the suite.
- Do NOT use `[<Tests>]` attribute. The project uses explicit `rootTests` registration.
- Do NOT add `PromptReader` tests to `ReplTests.fs` — separate test module; cleaner; avoids polluting the integration testList with unit tests.
- Do NOT call `reader.ReadLineAsync()` inside the `makeRealPromptReader` smoke testCase. That would invoke PrettyPrompt's internal `Console.ReadKey` loop in a non-TTY env → hang or `InvalidOperationException`. The factory-smoke test ONLY verifies construction succeeds.
- Do NOT assert on `~/.bluecode/history` file contents (e.g., `File.ReadAllLines`). That file is owned by PrettyPrompt and may be in any state from prior real REPL use; testing its contents is non-deterministic. Tests that need a clean history file path use a tmp dir (see HIST-03 testCase pattern).
- Do NOT use `git add -A`. Stage exactly: `git add tests/BlueCode.Tests/PromptReaderTests.fs tests/BlueCode.Tests/BlueCode.Tests.fsproj tests/BlueCode.Tests/RouterTests.fs`.

**Atomic commit (Task 2):**
```bash
git add tests/BlueCode.Tests/PromptReaderTests.fs tests/BlueCode.Tests/BlueCode.Tests.fsproj tests/BlueCode.Tests/RouterTests.fs
git commit -m "test(35-02): add PromptReaderTests for IPromptReader port + HIST-03 + register in rootTests"
```
  </action>
  <verify>
1. `tests/BlueCode.Tests/PromptReaderTests.fs` exists; contains `module BlueCode.Tests.PromptReaderTests` at top.
2. `grep -c "testCase " tests/BlueCode.Tests/PromptReaderTests.fs` returns ≥6.
3. `grep -n "PromptReaderTests.fs" tests/BlueCode.Tests/BlueCode.Tests.fsproj` shows entry sandwiched between `EditCommandTests.fs` and `RouterTests.fs`.
4. `grep -n "BlueCode.Tests.PromptReaderTests.tests" tests/BlueCode.Tests/RouterTests.fs` returns 1 match (the rootTests entry).
5. `dotnet build tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0 with no errors.
6. `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | tail -10` shows all tests passing AND total count is at least 6 higher than the post-Task-1 count (proves rootTests registration worked — silent skip would show same count).
7. The Expecto output shows the line `PromptReader/makeTestPromptReader: dispenses queued strings in FIFO order` (or similar) in the test list — confirms the new tests actually executed.
8. `git diff master -- src/BlueCode.Core/` is empty.
9. `git log --oneline -1` shows `test(35-02): add PromptReaderTests for IPromptReader port + HIST-03 + register in rootTests`.
  </verify>
  <done>
- `tests/BlueCode.Tests/PromptReaderTests.fs` exists with 6-7 testCases (FIFO contract, exhaustion-None, empty-list-None, historyFilePath shape, PrettyPrompt-tmp-construction smoke, makeRealPromptReader factory smoke).
- `BlueCode.Tests.fsproj` has the `<Compile Include="PromptReaderTests.fs" />` entry between `EditCommandTests.fs` and `RouterTests.fs`.
- `RouterTests.fs` `rootTests` list includes `BlueCode.Tests.PromptReaderTests.tests` (CRITICAL — without this, tests compile but never run).
- Total test count increased by ≥6 over Task 1's baseline; full suite passes.
- Core diff empty; commit `test(35-02): add PromptReaderTests for IPromptReader port + HIST-03 + register in rootTests` recorded.
  </done>
</task>

<task type="auto">
  <name>Task 3: Bench gate verification (no commit) — bash bench/run.sh --gate reports 7/7 PASS with byte-equal baseline.json</name>
  <files>bench/runs/<latest-timestamp>/ (created by bench/run.sh; gitignored — no commit)</files>
  <action>
**This task is verification-only. NO source/test code changes. NO git commit.**

**Step A — Pre-flight: ensure 122B service is reachable.**

Bench gate REQUIRES the Qwen 3.5 122B `mlx_lm.server` service to be loaded and serving on `localhost:8001`. Verify:

```bash
curl -fsS http://127.0.0.1:8001/v1/models | head -c 200
```

Expected: JSON response with `data: [{"id": "...qwen122b..."}]`. If this fails (`curl: (7) Failed to connect`), the 122B service is not loaded.

**If 122B unreachable, HALT and hand back to user with this exact message:**

> Bench gate cannot run — 122B service unreachable on port 8001. Please run:
> ```
> launchctl kickstart -k gui/501/com.ohama.qwen122b
> until curl -fsS http://127.0.0.1:8001/v1/models; do sleep 5; done
> ```
> Then re-execute Plan 35-02 Task 3.

Cold-start can take up to 240s (CLAUDE.md "Connection refused or 300s timeout" gotcha). Do NOT proceed with the bench gate until the curl probe succeeds.

**Step B — Run the bench gate (CLAUDE.md "structural authority"):**

```bash
bash bench/run.sh --gate
```

Expected output (all 6 gate labels PASS plus aggregate):
```
T6_122b      PASS
W1_122b      PASS
W2_122b      PASS
T1_122b      PASS
T5_122b      PASS
B2_122b      PASS
=== gate: 7/7 PASS ===
```

(Per CLAUDE.md "Bench" section: gate = 6 invocations, ~2 min wall time. The "7/7" includes the aggregate pass label.)

**If gate FAILS (any label PASS≠), DIAGNOSE before proceeding:**

1. **Capture run logs:** `ls -lt bench/runs/ | head -3` → most recent timestamped dir; check `.log` files inside.
2. **Most likely cause:** 122B service kickstart needed (KV cache contamination from prior runs):
   ```bash
   launchctl kickstart -k gui/501/com.ohama.qwen122b
   until curl -fsS http://127.0.0.1:8001/v1/models; do sleep 5; done
   bash bench/run.sh --gate    # retry
   ```
3. **Less likely cause:** Phase 35 changes affected something they shouldn't have. Verify:
   - `git diff master -- src/BlueCode.Core/` is empty (Core untouched throughout Phase 35)
   - `git diff master -- bench/baseline.json` is empty (baseline NEVER edited — CLAUDE.md "bench/baseline.json must remain byte-identical")
   - `grep -c "Console.ReadLine" src/BlueCode.Cli/Program.fs` returns 0 in single-turn dispatch path (Program.fs `| words ->` branch goes to `runSingleTurn`, never to `runMultiTurnWithSession`)
4. **Last resort — ROLLBACK Plan 35-01's Repl.fs changes only** and re-run gate to isolate Phase 35 impact. If gate passes after rollback, the regression is in Plan 35-01's input loop refactor — investigate. (This is unexpected; Plan 35-01 § Bench Gate Isolation proved structurally that PrettyPrompt is never instantiated in the bench path.)

**Step C — Verify baseline.json byte-equality:**

```bash
git status bench/baseline.json
git diff bench/baseline.json
```

Expected: NO modifications to `bench/baseline.json`. The file is the regression authority and is NEVER modified by code changes — only by deliberate baseline-update procedures (which are not part of any v2.5 plan).

If `bench/baseline.json` shows as modified, IMMEDIATELY revert: `git checkout -- bench/baseline.json`. The fact that gate logic ran does not authorize baseline mutation.

**Step D — Final whole-phase regression checks (informational; informs SUMMARY):**

```bash
# Build + tests one more time to confirm Phase 35 lands clean:
dotnet build
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | tail -3

# Confirm core purity preserved:
git diff master -- src/BlueCode.Core/

# Confirm async-literal CI script:
bash scripts/check-no-async.sh

# Confirm Phase 35 file inventory:
git diff master --stat -- src/BlueCode.Cli/PromptReader.fs src/BlueCode.Cli/Repl.fs src/BlueCode.Cli/BlueCode.Cli.fsproj tests/BlueCode.Tests/ReplTests.fs tests/BlueCode.Tests/PromptReaderTests.fs tests/BlueCode.Tests/BlueCode.Tests.fsproj tests/BlueCode.Tests/RouterTests.fs .planning/PROJECT.md
```

Expected: build green, all tests pass (≥365), Core diff empty, no `async {}` literals, file inventory matches Plan 35-01 + 35-02 combined files_modified.

**NO COMMIT for this task.** Bench gate is verification-only; the bench-runs directory (`bench/runs/<timestamp>/`) is gitignored. The plan-meta commit (Step E) covers the SUMMARY + ROADMAP updates separately.

**What NOT to do:**
- Do NOT modify `bench/baseline.json`. Period. CLAUDE.md "bench/baseline.json must remain byte-identical" — this is a load-bearing invariant.
- Do NOT commit `bench/runs/<timestamp>/` artifacts. They're gitignored; `git status` should show them as untracked or filtered.
- Do NOT use `dotnet test` for the test verification. Canonical runner is `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj`.
- Do NOT skip Step A (curl pre-flight). The bench gate WILL fail with connection-refused if 122B isn't loaded; user kickstart instruction is the documented recovery path.
- Do NOT commit anything in this task. Bench gate is verification-only.
- Do NOT run `bench/run.sh --canary`, `--regression`, or `--all` for this gate verification. The `--gate` mode is the documented authority (CLAUDE.md "Bench"); other modes are slower and not the regression authority.
- Do NOT proceed to plan-meta commit (Step E) until bench gate is GREEN. A red gate is a Phase 35 blocker.
  </action>
  <verify>
1. `curl -fsS http://127.0.0.1:8001/v1/models` returns HTTP 200 with JSON model list (122B service reachable).
2. `bash bench/run.sh --gate` exits 0 and the final line contains `7/7 PASS` (or equivalent — all 6 gate labels + aggregate).
3. `git status bench/baseline.json` shows the file as UNMODIFIED (no staged or unstaged changes to baseline).
4. `git diff master -- src/BlueCode.Core/` is empty (Core untouched throughout Phase 35).
5. `bash scripts/check-no-async.sh` exits 0 (no `async {}` literal in Core).
6. `dotnet build` exits 0; `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | grep -i "passed\|failed" | tail -3` shows all tests passing with total count ≥365.
7. `git status` shows only PromptReaderTests.fs / ReplTests.fs / fsproj / RouterTests.fs / Repl.fs / PromptReader.fs / BlueCode.Cli.fsproj / PROJECT.md as committed (across Plan 35-01 + 35-02); no other src/ or tests/ files modified.
  </verify>
  <done>
- `bash bench/run.sh --gate` reports 7/7 PASS empirically (SC-7 confirmed).
- `bench/baseline.json` byte-equal (git status unmodified).
- Core untouched; no `async {}` introduced; canonical test runner reports all tests passing with total count ≥365.
- Phase 35 file inventory matches Plan 35-01 + 35-02 combined files_modified frontmatter (no scope creep).
- NO commit for this task — bench gate is verification-only; bench/runs/<timestamp>/ is gitignored.
  </done>
</task>

</tasks>

<verification>
**Plan-level verification gates (run AFTER all 3 tasks complete):**

1. **Build green for src and tests projects:**
   ```bash
   dotnet build
   ```
   Both `BlueCode.Cli` and `BlueCode.Tests` compile with no errors. Test runtime is GREEN now (was RED after Plan 35-01).

2. **Full test suite passes via canonical runner:**
   ```bash
   dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | tail -10
   ```
   All tests pass; total count ≥365 (Phase 34 baseline 359 + ≥6 new PromptReaderTests).

3. **Bench gate 7/7 PASS (CLAUDE.md "structural authority"):**
   ```bash
   bash bench/run.sh --gate
   ```
   All 6 gate labels (T6_122b W1_122b W2_122b T1_122b T5_122b B2_122b) + aggregate PASS.

4. **bench/baseline.json byte-equal:**
   ```bash
   git diff master -- bench/baseline.json
   ```
   Empty (baseline NEVER modified throughout Phase 35).

5. **Console.SetIn fully eliminated from ReplTests:**
   ```bash
   grep -c "Console.SetIn" tests/BlueCode.Tests/ReplTests.fs
   ```
   Returns 0 (was 38 before Task 1).

6. **promptReaderOverride seam used 19 times in ReplTests (per migrated testCase):**
   ```bash
   grep -c "promptReaderOverride <- Some" tests/BlueCode.Tests/ReplTests.fs
   ```
   Returns 19 (one per migrated testCase; with matching `promptReaderOverride <- None` count of 19 in `finally` blocks).

7. **PromptReaderTests.fs registered in BOTH .fsproj AND rootTests:**
   ```bash
   grep -c "PromptReaderTests.fs" tests/BlueCode.Tests/BlueCode.Tests.fsproj    # expect 1
   grep -c "PromptReaderTests.tests" tests/BlueCode.Tests/RouterTests.fs        # expect 1
   ```
   Both return 1 (LOAD-BEARING per CLAUDE.md test-discovery convention).

8. **Core purity preserved end-to-end:**
   ```bash
   git diff master -- src/BlueCode.Core/
   ```
   Empty (Phase 35 = Cli + tests + docs only).

9. **No `async {}` literal added:**
   ```bash
   bash scripts/check-no-async.sh
   ```
   Exits 0.

10. **Phase 35 commits in expected sequence (5 commits across 35-01 + 35-02):**
    ```bash
    git log --oneline | grep "(35-0" | head -10
    ```
    Expected ordering (most-recent first):
    - `test(35-02): add PromptReaderTests for IPromptReader port + HIST-03 + register in rootTests`
    - `test(35-02): migrate 19 ReplTests to promptReaderOverride seam (PrettyPrompt bypasses Console.SetIn)`
    - `docs(35-01): mark PrettyPrompt 4.1.1 NuGet decision as Verified in Key Decisions`
    - `feat(35-01): wire Repl input loop to PromptReader (replace Console.ReadLine; add promptReaderOverride seam)`
    - `feat(35-01): add PrettyPrompt 4.1.1 dep + PromptReader.fs (IPromptReader port + makeRealPromptReader + makeTestPromptReader + historyFilePath)`

**HUMAN VERIFICATION items (cannot be unit-tested in Expecto; verifier MUST surface as a checkpoint):**

These are PrettyPrompt-internal interactive-TTY behaviors that require a real macOS terminal. List explicitly here so `gsd-verifier`'s `human_needed` gate has a clear acceptance protocol:

**HV-1 (SC-3 — Up/Down arrow recall in current REPL session):**
1. Open Terminal.app on macOS.
2. Build + run REPL: `cd /Users/ohama/projs/blueCode && dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj`
3. Type a prompt (e.g., `hello`), press Enter; let the LLM respond (or type `/help` then Enter for instant feedback).
4. Type a second prompt (e.g., `goodbye`), press Enter.
5. At the next `blueCode>` prompt, press Up arrow ONCE. Expected: `goodbye` appears at the prompt.
6. Press Up arrow again. Expected: `hello` appears.
7. Press Down arrow. Expected: `goodbye` returns.
8. Type `/exit` and Enter to clean up.

PASS criterion: Up/Down navigate prior prompts in REPL session order (PrettyPrompt built-in).

**HV-2 (SC-6 — Ctrl+R reverse-search):**
1. In the SAME REPL session as HV-1 (or after re-launching to load persisted history).
2. Press `Ctrl+R`. Expected: a reverse-search overlay opens (e.g., `(reverse-i-search)\`': `).
3. Type a substring of a prior prompt (e.g., `hel` after submitting `hello` earlier). Expected: matching prompt is shown for confirmation.
4. Press Enter to accept the matched prompt OR Escape/Ctrl+C to cancel.
5. Type `/exit` and Enter.

PASS criterion: Ctrl+R opens reverse-search; substring match works; selection populates the prompt buffer (PrettyPrompt built-in).

**HV-3 (SC-8 — macOS Terminal.app + iTerm2 verification):**
Re-run HV-1 + HV-2 in BOTH macOS Terminal.app AND iTerm2 separately. PASS criterion: identical behavior in both terminal emulators.

**HV-4 (HIST-03 functional cross-session — file persistence):**
1. Run REPL, type 2-3 prompts, `/exit`.
2. `cat ~/.bluecode/history`. Expected: file exists, contains base64-per-line entries (NOT human-readable; this is PrettyPrompt's internal format — research § Pitfall 7 explained the spec/impl naming conflict).
3. Re-launch REPL. Press Up arrow at first `blueCode>` prompt.
4. Expected: a prior-session prompt is recalled (proves cross-session load).
5. Type `/exit`.

PASS criterion: file exists post-submit + Up-arrow recalls prior-session prompts (loads from `~/.bluecode/history`).

The verifier should treat these 4 HUMAN VERIFICATION items as a single blocking checkpoint at the end of Phase 35; user types `approved` after running through HV-1..HV-4 in either terminal (HV-3 makes this a 2-terminal sweep).

**Plan-meta commit (after all verification gates pass):**

```bash
# Final plan-meta commit covers ROADMAP plan-list update for both 35-01 and 35-02
# (35-01 was committed in 99f7c1e without the ROADMAP plan-list block).
git add .planning/phases/35-prettyprompt-readline-history/35-02-tests-migration-and-hist-PLAN.md .planning/ROADMAP.md
git commit -m "docs(35): add Plan 35-02 + roadmap plan list"
```
</verification>

<success_criteria>
This plan satisfies the following Phase 35 ROADMAP success criteria (Plan 35-02 covers SC-3..SC-9; SC-1 + SC-2 were Plan 35-01 territory):

- **SC-3 (Up/Down arrow recall in current REPL session):** GREEN via PrettyPrompt built-in (Plan 35-01 wired `persistentHistoryFilepath`); functional verification deferred to **HUMAN VERIFICATION HV-1** (cannot be unit-tested in Expecto — requires real TTY).
- **SC-4 (`~/.bluecode/history` append per submit; spec resolution: include all inputs incl. slash commands; /edit content does NOT enter history):** GREEN — `historyFilePath()` returns `~/.bluecode/history`; PrettyPrompt's `SavePersistentHistoryAsync` appends per `ReadLineAsync` success; PromptReaderTests Task 2 covers the historyFilePath shape contract; functional cross-session HIST-03 verification deferred to **HUMAN VERIFICATION HV-4**. /edit non-issue documented in 35-01 SUMMARY (research § Pitfall 8).
- **SC-5 (REPL load history on start; cap):** GREEN-with-trade-off (locked in Plan 35-01) — PrettyPrompt's internal `HistoryLog.MaxHistoryEntries` is hardcoded at 500. ROADMAP placeholder said `N=1000`; PrettyPrompt's 500 is sufficient for daily-driver use (typical user history <100 entries). Cap deviation documented in 35-01 + 35-02 SUMMARY.
- **SC-6 (Ctrl+R reverse-search):** GREEN via PrettyPrompt built-in; functional verification deferred to **HUMAN VERIFICATION HV-2**.
- **SC-7 (Bench gate `bash bench/run.sh --gate` 7/7 PASS preserved):** GREEN via Task 3 — empirically confirms what Plan 35-01 § Bench Gate Isolation proved structurally (PrettyPrompt is only instantiated inside `runMultiTurnWithSession`; bench's single-turn path never enters that function).
- **SC-8 (macOS Terminal.app + iTerm2 manual verification):** Deferred to **HUMAN VERIFICATION HV-3** (re-run HV-1 + HV-2 in both terminal emulators); explicit blocker for verifier `human_needed` gate.
- **SC-9 (SlashCommand parser tests still pass post-PrettyPrompt):** GREEN — Phase 31-01's 17 pure SlashCommand parser testCases take strings as input and don't touch any I/O; they continue to pass unchanged. Task 1's full test-suite run confirms. Additionally, the 19 migrated ReplTests integration tests (which exercise the slash command DISPATCH path in addition to the parser) all pass — proving the parser is downstream of the reader and unaffected by the input mechanism.

**Phase 35 = THE LAST v2.5 PHASE.** After this plan ships + verifier approves + user signs off on HV-1..HV-4, v2.5 is complete (12/12 requirements done; SLASH-01..07 + EDIT-01 + HIST-01..04 all GREEN). Next workflow trigger: `/gsd:complete-milestone` to archive v2.5 + git tag.
</success_criteria>

<output>
After completion, create `.planning/phases/35-prettyprompt-readline-history/35-02-SUMMARY.md` with the following frontmatter and body:

```yaml
---
phase: 35-prettyprompt-readline-history
plan: 02
status: complete
date: <YYYY-MM-DD>
subsystem: cli-repl
affects:
  - tests/BlueCode.Tests/ReplTests.fs (19 testCases migrated; 0 Console.SetIn occurrences remaining)
  - tests/BlueCode.Tests/PromptReaderTests.fs (NEW; 6-7 unit tests)
  - tests/BlueCode.Tests/BlueCode.Tests.fsproj
  - tests/BlueCode.Tests/RouterTests.fs
tests:
  added: 6   # or 7, depending on final PromptReaderTests count
  modified: 19   # ReplTests testCases migrated to promptReaderOverride seam
  deleted: 0
  state_after_plan: GREEN-all-tests-pass
  total_count: <365 or higher; document actual>
commits:
  - test(35-02): migrate 19 ReplTests to promptReaderOverride seam (PrettyPrompt bypasses Console.SetIn)
  - test(35-02): add PromptReaderTests for IPromptReader port + HIST-03 + register in rootTests
  # Note: bench gate (Task 3) has NO commit — verification-only
loc_delta:
  added: ~120   # PromptReaderTests.fs ~85 + ReplTests delta ~30 + fsproj/rootTests ~5
  removed: ~80  # Console.SetIn / stdinReader / originalIn cleanup across 19 testCases
core_diff: empty
bench_gate: 7/7 PASS (baseline.json byte-equal)
human_verification_pending: 4 items (HV-1 Up/Down, HV-2 Ctrl+R, HV-3 Terminal.app+iTerm2, HV-4 cross-session HIST-03)
---
```

Body sections (recommended):

- **What shipped** — 19 ReplTests testCases migrated from `Console.SetIn(StringReader)` to `BlueCode.Cli.Repl.promptReaderOverride <- Some (BlueCode.Cli.PromptReader.makeTestPromptReader [...])`; new `PromptReaderTests.fs` with 6-7 unit tests (queue contract + historyFilePath shape + PrettyPrompt construction smoke + makeRealPromptReader factory smoke); registered in BOTH `.fsproj` Compile order AND `RouterTests.fs` rootTests list (CLAUDE.md test-discovery convention satisfied); bench gate 7/7 PASS confirmed empirically with byte-equal baseline.json.
- **SC coverage** — SC-3 GREEN (deferred to HV-1), SC-4 GREEN (PromptReaderTests + HV-4), SC-5 GREEN-with-trade-off (500-entry cap, documented), SC-6 GREEN (deferred to HV-2), SC-7 GREEN (bench gate empirical), SC-8 deferred to HV-3, SC-9 GREEN (parser tests unchanged + 19 migrated integration tests pass).
- **Test migration summary** — Mechanical 5-step replacement applied uniformly to 19 multi-turn testCases; runSingleTurn-only tests (4) and the 2-runSingleTurn-multi-turn-simulation test (1) untouched. Plan-gate tests (3 at lines 1168/1248/1316) may have used hybrid Console.SetIn (for `a`/`q` keypress to PlanGate.realKeyReader) + promptReaderOverride (for prompt lines) — document final approach taken. testSequenced wrapper, Console.SetOut capture, AnsiConsole reset (Phase 33-02), editorLauncherOverride (Phase 34-02) all UNCHANGED.
- **Bench gate isolation confirmed empirically** — Plan 35-01 § Bench Gate Isolation proved structurally that PrettyPrompt is never instantiated in single-turn (bench) path; Plan 35-02 Task 3 confirms it empirically: `bash bench/run.sh --gate` 7/7 PASS with baseline.json byte-equal post-Phase-35.
- **HUMAN VERIFICATION items pending (handed to verifier `human_needed` gate)** — HV-1 Up/Down arrow in REPL session, HV-2 Ctrl+R reverse-search, HV-3 macOS Terminal.app + iTerm2 sweep, HV-4 cross-session HIST-03 file persistence. Verifier should treat as single blocking checkpoint at Phase 35 close; user signs off after HV-1..HV-4 pass on real TTY.
- **Pitfalls dodged** — silent test skip from missing rootTests registration (the LOAD-BEARING pattern called out in CLAUDE.md "Test discovery"); PrettyPrompt construction in non-TTY test env (factory smoke verifies construction succeeds, never invokes ReadLineAsync); `bench/baseline.json` mutation (NEVER modified — git status unmodified asserted in verify); `git add -A` (per-file staging only); `dotnet test` (canonical `dotnet run` only); accidentally serializing PromptReaderTests with testSequenced (plain testList — no Console.SetOut, no process-level state).
- **Phase 35 = LAST v2.5 PHASE COMPLETE** — 12/12 v2.5 requirements GREEN (SLASH-01..07 + EDIT-01 + HIST-01..04). Next: verifier accepts (post HV-1..HV-4) → user signoff → `/gsd:complete-milestone` archives v2.5 + git tag.
</output>
</content>
</invoke>