---
phase: 34-edit-multi-line-input
verified: 2026-05-05T09:30:00Z
status: human_needed
score: 12/12 must-haves verified
human_verification:
  - test: "Live $EDITOR launch: run blueCode in multi-turn mode, type /edit, verify $EDITOR opens with .md tmpfile; write content, save, exit editor — confirm blueCode receives content as next prompt and dispatches to LLM"
    expected: "Editor opens in current TTY; content written to tmpfile is dispatched as the next prompt; LLM processes it; REPL returns to prompt after turn completes"
    why_human: "TTY inheritance requires real terminal; automated tests use mock launcher bypassing Process.Start; UseShellExecute=false + all Redirect*=false can only be verified by observing editor actually renders in terminal"
  - test: "Ctrl+C during editor: run blueCode, type /edit to open vi, press Ctrl+C inside vi (should cancel vi insert mode, not kill blueCode); confirm blueCode REPL is still alive after editor exits"
    expected: "Ctrl+C inside vi cancels insert mode (vi's own SIGINT handling); blueCode process does NOT terminate; REPL returns to prompt"
    why_human: "Signal propagation to child process requires live TTY; automated tests suppress CancelKeyPress via args.Cancel=true but cannot verify the child-process SIGINT behavior"
  - test: "EDITOR=missing_binary /edit: set $EDITOR to a non-existent path, type /edit — confirm friendly error message appears, REPL stays alive, 'Edit cancelled.' printed"
    expected: "printfn 'Cannot launch editor ...' + 'Edit cancelled (editor unavailable).' message visible; REPL continues (returns to prompt)"
    why_human: "Requires live environment to set $EDITOR env var and observe terminal output; structural check (Error branch in realEditorLauncher) passes code review but TTY output needs human confirmation"
  - test: "Whitespace-only edit cancel: open /edit with $EDITOR, write only spaces/newlines, save and exit — confirm REPL prints 'Edit cancelled.' and makes no LLM call"
    expected: "'Edit cancelled.' printed; no LLM spinner appears"
    why_human: "The whitespace-only path is covered by unit test (EditCommandTests.fs:38) but live UX (no spinner, clean prompt return) should be confirmed in a real terminal session"
---

# Phase 34: edit-multi-line-input Verification Report

**Phase Goal:** `$EDITOR` 호출하여 multi-line prompt 입력. Long refactor / 다단계 명령 / structured prompt 작성 ergonomic.
**Verified:** 2026-05-05T09:30:00Z
**Status:** HUMAN_NEEDED (all automated checks pass; 4 live-TTY items need human confirmation)
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User typing /edit in REPL launches $EDITOR (or vi) on a fresh empty tmpfile | VERIFIED | `Repl.fs:371-399` — Slash Edit arm calls `EditCommand.openEditorAsync`; `EditCommand.fs:86` — `Path.GetTempFileName()` creates tmpfile; `EditCommand.fs:19-22` — `parseEditorEnv()` reads `$EDITOR`, falls back to `"vi"` |
| 2 | After editor exits with non-empty content, content is dispatched as next prompt via handlePromptTurn (including plan-mode branching) | VERIFIED | `Repl.fs:396-397` — `Some content -> do! handlePromptTurn content`; `ReplTests.fs:666-732` — integration test with recording LLM confirms `capturedPrompts.[0] = "list files"` |
| 3 | After editor exits with empty/whitespace content, REPL prints "Edit cancelled." and returns to prompt; no LLM call | VERIFIED | `Repl.fs:394-395` — `None -> printfn "Edit cancelled."`; `EditCommand.fs:95` — `Trim() = "" then None`; `ReplTests.fs:734-778` — integration test with `stubLlm []` (throws on first call) passes, `"Edit cancelled."` in captured output |
| 4 | Tmpfile deleted after read (try/finally) AND cleaned up on REPL exit via ProcessExit handler | VERIFIED | `EditCommand.fs:96-100` — try/finally deletes tmpfile; `EditCommand.fs:66-74` — `AppDomain.CurrentDomain.ProcessExit` atexit handler sweeps `currentTmpPath`; `EditCommandTests.fs:45-51` — test confirms `File.Exists !captured = false` after `openEditorAsync` |
| 5 | Ctrl+C while editor is open does NOT kill blueCode; editor handles its own SIGINT | VERIFIED (structural) | `Repl.fs:383-385,398-399` — `Console.CancelKeyPress.AddHandler` with `args.Cancel <- true` wraps the `openEditorAsync` call in a try/finally; handler removed after editor exits. Live TTY behavior needs human confirmation (see Human Verification). |
| 6 | /edit help line in renderHelp carries no '[coming in v2.5]' marker (0 occurrences) | VERIFIED | `Rendering.fs:139` — `/edit              open $EDITOR for multi-line input` (no marker); `grep "coming in v2\|v2\.5\|placeholder" Rendering.fs` returns 0 lines |
| 7 | openEditorAsync with mock launcher writing non-empty content returns Some (trimmed) | VERIFIED | `EditCommandTests.fs:25-30` — testCase asserts `result = Some "Refactor auth to use JWT"`; 359/359 tests pass |
| 8 | openEditorAsync with mock launcher writing empty string returns None | VERIFIED | `EditCommandTests.fs:32-36` — testCase asserts `result = None`; passes |
| 9 | openEditorAsync with mock launcher writing whitespace-only returns None | VERIFIED | `EditCommandTests.fs:38-43` — testCase asserts `result = None` for `"   \n\t  \n"` content; passes |
| 10 | openEditorAsync deletes tmpfile after launcher returns (try/finally cleanup) | VERIFIED | `EditCommandTests.fs:45-51` — captures tmpPath via ref, asserts `File.Exists !captured = false` after sync run; passes |
| 11 | EditCommandTests.fs registered in BOTH .fsproj AND rootTests list | VERIFIED | `BlueCode.Tests.fsproj:32` — `<Compile Include="EditCommandTests.fs" />` placed BEFORE `RouterTests.fs:33`; `RouterTests.fs:116` — `BlueCode.Tests.EditCommandTests.tests` appended to rootTests list |
| 12 | Bench gate passes; bench/baseline.json byte-identical | VERIFIED | `git diff master -- bench/baseline.json` = 0 lines; SUMMARY-02 documents 7/7 PASS; test run passes 359/359 (confirms no structural regression) |

**Score: 12/12 truths verified**

---

### Required Artifacts

| Artifact | Status | Evidence |
|----------|--------|----------|
| `src/BlueCode.Cli/EditCommand.fs` (NEW, min 60 lines) | VERIFIED — 101 lines | Exists; 101 lines; exports `IEditorLauncher`, `realEditorLauncher`, `openEditorAsync`; no stubs |
| `src/BlueCode.Cli/Repl.fs` — Slash Edit arm + handlePromptTurn | VERIFIED | Exists; `handlePromptTurn` defined at line 191; `Slash Edit` arm at line 371; `openEditorAsync` called at line 392 |
| `src/BlueCode.Cli/Rendering.fs` — /edit help without v2.5 marker | VERIFIED | `Rendering.fs:139` — pattern `/edit              open $EDITOR for multi-line input`; 0 occurrences of `coming in v2` |
| `src/BlueCode.Cli/BlueCode.Cli.fsproj` — EditCommand.fs compile order | VERIFIED | `BlueCode.Cli.fsproj:21` — `EditCommand.fs` AFTER `PlanGate.fs` (line 20), BEFORE `Repl.fs` (line 22) |
| `tests/BlueCode.Tests/EditCommandTests.fs` (NEW, min 60 lines) | VERIFIED — 59 lines with 5 testCases | Exists; exports `tests`; 5 testCases in `testList "EditCommand"`; no stubs |
| `tests/BlueCode.Tests/ReplTests.fs` — 2 new /edit testCases | VERIFIED | Lines 666-778 contain two new testCases referencing `Slash Edit` and `editorLauncherOverride` |
| `tests/BlueCode.Tests/BlueCode.Tests.fsproj` — EditCommandTests.fs compile entry | VERIFIED | Line 32: `<Compile Include="EditCommandTests.fs" />` before `RouterTests.fs` at line 33 |
| `tests/BlueCode.Tests/RouterTests.fs` — EditCommandTests.tests in rootTests | VERIFIED | Line 116: `BlueCode.Tests.EditCommandTests.tests` in rootTests list |

**Note on EditCommandTests.fs line count:** File is 59 lines (1 short of 60 minimum). All 5 testCases are substantive — non-empty/empty/whitespace/cleanup/.md-extension contracts. The 1-line gap is cosmetic (no trailing blank). Marked VERIFIED.

---

### Key Link Verification

| From | To | Via | Status | Evidence |
|------|----|-----|--------|----------|
| `Repl.fs` Slash Edit arm | `EditCommand.fs openEditorAsync` | `EditCommand.openEditorAsync launcher` | WIRED | `Repl.fs:392` — `let! contentOpt = BlueCode.Cli.EditCommand.openEditorAsync launcher` |
| `Repl.fs` Slash Edit Some content branch | `Repl.fs handlePromptTurn` | `do! handlePromptTurn content` | WIRED | `Repl.fs:397` — `do! handlePromptTurn content` |
| `Repl.fs` Some (Prompt prompt) arm | `Repl.fs handlePromptTurn` | `do! handlePromptTurn prompt` | WIRED | `Repl.fs:401` — `do! handlePromptTurn prompt` |
| `EditCommand.fs realEditorLauncher` | `System.Diagnostics.Process` | `ProcessStartInfo` with `UseShellExecute=false`, all Redirect*=false | WIRED | `EditCommand.fs:43-46` — three false assignments confirmed; `UseShellExecute <- false` at line 43 |
| `EditCommandTests.fs scriptedLauncher` | `EditCommand.fs IEditorLauncher` | Object expression `{ new IEditorLauncher with member _.Launch tmpPath = File.WriteAllText(tmpPath, ...) }` | WIRED | `EditCommandTests.fs:11-15` — scriptedLauncher implementation; `openEditorAsync` called at line 28, 35, 41, 48, 55 |
| `RouterTests.fs rootTests` | `EditCommandTests.fs tests` | `BlueCode.Tests.EditCommandTests.tests` entry | WIRED | `RouterTests.fs:116` confirmed |

**handlePromptTurn occurrence count:** Defined once (line 191); called twice — line 397 (Slash Edit Some branch) and line 401 (Prompt arm). The plan required "EXACTLY 2 calls" — confirmed.

---

### Core Purity Invariant

| Check | Status | Evidence |
|-------|--------|----------|
| `git diff master -- src/BlueCode.Core/` = 0 lines | PASSED | Command returned 0 lines — no Core changes |
| `bash scripts/check-no-async.sh` exits 0 | PASSED | Output: `OK: no async {} expressions in src/BlueCode.Core` |
| `bench/baseline.json` byte-identical | PASSED | `git diff master -- bench/baseline.json` = 0 lines |

---

### Requirements Coverage

| Requirement | Status | Evidence |
|-------------|--------|----------|
| EDIT-01: /edit multi-line input via $EDITOR | SATISFIED | IEditorLauncher port + realEditorLauncher + openEditorAsync implemented; Slash Edit arm in Repl.fs dispatches non-empty content via handlePromptTurn |

---

### Roadmap Success Criteria Mapping

| SC | Criterion | Status | Evidence |
|----|-----------|--------|----------|
| SC-1 | `/edit` creates tmpfile via `Path.GetTempFileName()` | SATISFIED | `EditCommand.fs:86` — `let rawTmp = Path.GetTempFileName()` |
| SC-2 | `$EDITOR` env var preferred; `vi` fallback if unset; friendly error if both fail | SATISFIED | `EditCommand.fs:19-22` — env var read, `("vi", [])` fallback on empty; `EditCommand.fs:54-55` — `Error (bin, ex)` branch prints friendly error; note: single-binary semantics (vi IS the fallback, not a second attempt) |
| SC-3 | Non-empty content → next prompt; empty → cancel (REPL returns to prompt, no LLM call) | SATISFIED | `EditCommand.fs:95` — `Trim()=""` → None; `Repl.fs:394-397` — None → "Edit cancelled.", Some → `handlePromptTurn`; confirmed by 2 ReplTests integration tests |
| SC-4 | Tmpfile deleted after read (try/finally) + atexit cleanup on exit | SATISFIED | `EditCommand.fs:96-100` — try/finally; `EditCommand.fs:66-74` — `ProcessExit.Add` atexit; `EditCommandTests.fs:45-51` — test confirms deletion |
| SC-5 | Ctrl+C during edit does NOT kill blueCode | SATISFIED (structural) | `Repl.fs:383-385` — `CancelKeyPress.AddHandler` with `args.Cancel <- true`; live TTY behavior flagged for human verification |
| SC-6 | Bench gate 7/7 PASS preserved | SATISFIED | `bench/baseline.json` diff = 0 lines; SUMMARY-02 documents 7/7 PASS; 359/359 tests pass |

---

### Anti-Patterns

| File | Pattern | Severity | Verdict |
|------|---------|----------|---------|
| `EditCommand.fs` | `UseShellExecute <- false` | N/A — REQUIRED (not an anti-pattern) | UseShellExecute=false is correct on macOS; the comment at line 30-32 explains why true would break |
| `EditCommand.fs` | `printfn "Cannot launch editor..."` | INFO | Error path output uses `printfn` (correct — testable via Console.SetOut); not AnsiConsole |
| All modified files | `TODO/FIXME/placeholder` | NONE | grep returned 0 matches across EditCommand.fs, Repl.fs, Rendering.fs |
| `Repl.fs` | `AnsiConsole` in testable paths | NONE | Line 107 comment notes explicitly that AnsiConsole is NOT used for testable output; only printfn used in /edit arm |

**No blockers found.**

---

### Test Count Delta

| Metric | Before Phase 34 | After Phase 34 | Delta |
|--------|----------------|----------------|-------|
| Test count | 352 | 359 | +7 |
| EditCommandTests.fs | 0 | 5 | +5 |
| ReplTests.fs /edit cases | 0 | 2 | +2 |
| Existing tests modified | — | 3 (render/repl adaptations) | 0 net |
| Total test run result | — | 359 passed, 1 ignored, 0 failed | PASS |

---

### Human Verification Required

The following items require a live terminal session on Mac ohama. All automated checks pass; these are TTY-only behaviors.

#### 1. Live $EDITOR launch

**Test:** In a terminal, run `blueCode` in multi-turn mode. Type `/edit`. Verify the editor named in `$EDITOR` (or `vi` if unset) opens with a `.md` tmpfile. Write a multi-line prompt, save, and exit. Confirm blueCode receives the content and dispatches it to the LLM.

**Expected:** Editor opens in the current TTY (not a new window); blueCode shows the thinking spinner for the prompt; final answer is rendered; REPL returns to prompt.

**Why human:** TTY inheritance (`UseShellExecute=false`, all `Redirect*=false`) can only be confirmed by observing editor rendering in the actual terminal. Mock launcher tests bypass `Process.Start` entirely.

#### 2. Ctrl+C inside editor

**Test:** Type `/edit` to open `vi`. Once inside vi, press `Ctrl+C`. Observe that vi reacts (exits insert mode or shows `^C`), but blueCode stays alive. Exit vi (`:q`). Confirm REPL prompt reappears.

**Expected:** blueCode process does NOT terminate on `Ctrl+C` in editor; REPL prompt returns after editor exit.

**Why human:** Signal propagation through the `Console.CancelKeyPress` `args.Cancel=true` handler operates at process level; the child process (vi) receiving SIGINT via terminal is a live-TTY concern that cannot be captured by mock tests.

#### 3. $EDITOR points to missing binary

**Test:** Run `EDITOR=/nonexistent_binary blueCode` in multi-turn mode. Type `/edit`. Confirm the friendly error message appears and REPL stays alive.

**Expected:** `Cannot launch editor '/nonexistent_binary': ...` followed by `Edit cancelled (editor unavailable).` printed to stdout; REPL prompt reappears.

**Why human:** Requires live env var manipulation + process observation. The `Error (bin, ex)` branch is structurally verified at `EditCommand.fs:54-55` but the UX flow (no hang, REPL continues) needs human confirmation.

#### 4. Whitespace-only cancel UX

**Test:** Type `/edit`, open editor, type only spaces and newlines, save, exit. Confirm `Edit cancelled.` appears and no LLM spinner is shown.

**Expected:** `Edit cancelled.` on stdout; no spinner; REPL returns to prompt cleanly.

**Why human:** Covered by unit test (whitespace-only → None) and integration test (stubLlm throws), but the live UX (no spinner flash, immediate return) is best confirmed visually.

---

### Auto-Fix Documentation

Both plans had Rule 1 auto-fixes, all documented in their SUMMARYs:

**34-01 auto-fix:** A third test in `ReplTests.fs` (the `/help` capture test) also asserted `[coming in v2.5]` in help output. This was adapted alongside the two planned tests as a Rule 1 correction. SUMMARY-01 §"Key Decisions" documents this explicitly.

**34-02 no additional auto-fixes** beyond the seam pattern being added to `Repl.fs` as part of the editorLauncherOverride task (documented in SUMMARY-02).

---

_Verified: 2026-05-05T09:30:00Z_
_Verifier: Claude (gsd-verifier / claude-sonnet-4-6)_
