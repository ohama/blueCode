---
phase: 34-edit-multi-line-input
plan: 01
status: complete
date: 2026-05-05
subsystem: cli-repl
affects:
  - src/BlueCode.Cli/EditCommand.fs (NEW)
  - src/BlueCode.Cli/Repl.fs
  - src/BlueCode.Cli/Rendering.fs
  - src/BlueCode.Cli/BlueCode.Cli.fsproj
  - tests/BlueCode.Tests/RenderingTests.fs
  - tests/BlueCode.Tests/ReplTests.fs
tests:
  added: 0
  modified: 3
  deleted: 0
commits:
  - "b2f2f1b feat(34-01): add IEditorLauncher port + openEditorAsync (EditCommand.fs)"
  - "6b18725 feat(34-01): wire /edit to EditCommand.openEditorAsync via handlePromptTurn refactor"
  - "2625de4 test(34-01): adapt 2 existing tests for /edit live promotion"
loc_delta:
  added: ~118
  removed: ~87
core_diff: empty
duration: ~17 minutes
---

# Phase 34 Plan 01: Port and Integration Summary

**One-liner:** IEditorLauncher port + openEditorAsync + handlePromptTurn refactor wires /edit to $EDITOR via TTY-inheriting ProcessStartInfo; renderHelp [coming in v2.5] marker eliminated.

## What Shipped

- **`EditCommand.fs` (NEW):** `IEditorLauncher` interface (mirrors `IKeyReader` from PlanGate.fs); `realEditorLauncher` production implementation (UseShellExecute=false, all three Redirect*=false for TTY inheritance); `openEditorAsync` (Path.GetTempFileName → .md rename → launch → read → try/finally delete); module-level `AppDomain.CurrentDomain.ProcessExit` atexit registration via mutable `currentTmpPath` cell for abnormal-termination cleanup. Registered in BlueCode.Cli.fsproj between PlanGate.fs and Repl.fs.

- **`Repl.fs` refactor:** Factored out `handlePromptTurn` local helper containing the full plan-mode-aware dispatch (planModeActive branch + direct branch), shared by both `Some (Prompt prompt)` arm and the new `Some (Slash Edit)` arm. The Slash Edit arm wraps `openEditorAsync` with a `Console.CancelKeyPress` add/remove handler (args.Cancel=true) to suppress SIGINT killing blueCode while the editor is open; empty/whitespace content → "Edit cancelled."; non-empty → `do! handlePromptTurn content`.

- **`Rendering.fs` cleanup:** `/edit` help line drops the `[coming in v2.5]` suffix. 0 occurrences of that marker in the final renderHelp string.

- **Test adaptations (3, not 2):** RenderingTests: inverted the "1 marker" assertion to "0 markers" regression fence. ReplTests `/help` test: flipped `[coming in v2.5]` presence assertion to absence assertion. ReplTests future-stub test: replaced `/edit\n/exit\n` driver (would block on real editor) with `/help\n/status\n/sessions\n/exit\n` driver; asserts 0 "not yet implemented" lines.

## Key Decisions Captured

- **Fully-qualified BlueCode.Cli.EditCommand.* in Repl.fs** — No new `open` directive; mirrors Phase 33-01 decision for PlanGate.* references.
- **Content-based cancel, not exit-code-based** — `:q!` in vi returns 0; exit code is meaningless. Empty/whitespace file is the cancel signal.
- **.md tmpfile extension** — Rename via Path.ChangeExtension after GetTempFileName; gives vim/nano markdown syntax highlighting.
- **Module-level ProcessExit registration with mutable currentTmpPath** — The atexit handler can sweep the tmpfile on abnormal termination even if openEditorAsync's try/finally didn't run.
- **3 tests adapted (not 2)** — The ReplTests `/help` test also asserted `[coming in v2.5]` in captured help output; adapted alongside the two planned tests as a Rule 1 auto-fix.

## Behavior Unchanged for Plan-Mode

Phase 33's 6 plan-gate ReplTests integration tests all pass. The handlePromptTurn lift was strictly behavior-preserving: the planModeActive branching logic, the `runSingleTurn prompt` (not currentPrompt) invariant, all 4 planModeActive<-false sites, and the lastCode/session-save paths were lifted verbatim into the shared helper.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Third test also checked `[coming in v2.5]`**

- **Found during:** Task 3 (first test run revealed 3 failures, not 2)
- **Issue:** `ReplTests "runMultiTurn: '/help' prints 9-command help"` at line 483 asserted `Expect.stringContains captured "[coming in v2.5]" "help marks future commands"` — directly contradicted by the Task 2 Rendering.fs edit.
- **Fix:** Flipped to `Expect.isFalse (captured.Contains("[coming in v2.5]"))` and replaced the `/sessions stub` assertion with `/edit (live)` assertion.
- **Files modified:** `tests/BlueCode.Tests/ReplTests.fs`
- **Commit:** 2625de4 (included in Task 3 commit)

## Open Items Handed to Plan 34-02

- Mock-launcher behavior tests (IEditorLauncher injection pattern)
- ReplTests `/edit` integration tests with scripted content
- Bench gate 7/7 PASS verification
- Manual smoke: REPL `$EDITOR` invocation with real vi

## Pitfalls Dodged

- UseShellExecute=true on macOS would use /usr/bin/open (app bundle, wrong)
- CreateNoWindow is Windows-only; omitted entirely
- proc.ExitCode check is wrong (`:q!` returns 0)
- `async {}` avoided in favor of `task {}` (Cli convention)
- `git add -A` avoided (untracked .claude/ + localLLM/ safety)
