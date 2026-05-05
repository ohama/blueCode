---
phase: 34-edit-multi-line-input
plan: 02
status: complete
date: 2026-05-05
subsystem: cli-repl
tags: [fsharp, expecto, iedictorlauncher, mock-launcher, integration-test, bench-gate]

# Dependency graph
requires:
  - phase: 34-01-port-and-integration
    provides: IEditorLauncher port + openEditorAsync + Slash Edit arm wired in Repl.fs
provides:
  - 5 EditCommand unit tests (mock IEditorLauncher: non-empty/empty/whitespace/tmpfile-cleanup/.md extension)
  - editorLauncherOverride test seam in Repl.fs (module-level mutable cell, reset-in-finally)
  - 2 ReplTests integration tests for /edit dispatch (success + cancel paths via mock launcher)
  - Bench gate 7/7 PASS confirming zero regression from Phase 34 changes
affects:
  - Phase 35 (PrettyPrompt readline + history) - /edit seam pattern reusable for any future test-injectable override

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "editorLauncherOverride mutable cell: module-level test seam mirroring Console.SetIn/SetOut; reset-in-finally is mandatory"
    - "recordingLlm via tryFindBack Role.User: captures last User message content to assert dispatch path through messages list"
    - "testSequenced inheritance: new testCases inside existing testSequenced envelope inherit sequential execution; no nested wrapper needed"

key-files:
  created:
    - tests/BlueCode.Tests/EditCommandTests.fs
  modified:
    - tests/BlueCode.Tests/ReplTests.fs
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs
    - src/BlueCode.Cli/Repl.fs

key-decisions:
  - "editorLauncherOverride as test seam (module-level mutable cell) mirrors Console.SetIn/SetOut convention; testSequenced requirement inherited from existing ReplTests envelope; reset-in-finally mandatory to prevent cross-test leakage"
  - "recordingLlm uses tryFindBack on MessageRole.User to validate dispatch path (last User message = trimmed editor content); confirms handlePromptTurn wiring is correct"
  - "EditCommandTests uses plain testList (not testSequenced) because no Console globals are touched; Path.GetTempFileName produces unique paths so parallel execution is race-free"
  - "Task 3 is verification-only (no commit); bench gate is the structural authority per CLAUDE.md; 7/7 PASS = Phase 34 complete"

patterns-established:
  - "editorLauncherOverride seam: test-only mutable override with finally-reset pattern; any future process-level mutable used in Repl.fs follows same convention"
  - "makeMockResponse already returns Result<LlmResponse,AgentError>; never wrap in extra Ok"

# Metrics
duration: ~12min
completed: 2026-05-05
---

# Phase 34 Plan 02: Behavior Tests and Bench Summary

**Mock IEditorLauncher unit tests + editorLauncherOverride REPL seam + 2 /edit integration tests; bench gate 7/7 PASS; test count 352 -> 359**

## Performance

- **Duration:** ~12 min
- **Started:** 2026-05-05T00:08:35Z
- **Completed:** 2026-05-05T00:20:44Z
- **Tasks:** 3 (Task 1: EditCommandTests; Task 2: seam + ReplTests; Task 3: bench gate verification only)
- **Files modified:** 5 (1 new test module, 1 Repl.fs seam, 3 test infrastructure updates)

## Accomplishments

- Created `EditCommandTests.fs` with 5 mock-launcher unit tests asserting non-empty/empty/whitespace-only/tmpfile-cleanup/.md-extension contracts
- Added `editorLauncherOverride` test seam to `Repl.fs` (module-level mutable cell + match in Slash Edit arm); production behavior unchanged
- Added 2 ReplTests integration tests for /edit dispatch: success path (recording LLM asserts content = "list files") and cancel path (`stubLlm []` confirms 0 LLM calls, stdout contains "Edit cancelled.")
- Bench gate 7/7 PASS confirmed: T6/W1/W2/T1/T5/B2/MT all pass; `bench/baseline.json` byte-identical

## Task Commits

Each task was committed atomically:

1. **Task 1: EditCommandTests (5 mock-launcher unit tests + fsproj + rootTests registration)** - `d894788` (test)
2. **Task 2a: editorLauncherOverride seam in Repl.fs** - `20c00cf` (feat)
3. **Task 2b: 2 ReplTests integration tests for /edit dispatch** - `6d12ccb` (test)

Task 3 (bench gate): verification only — no commit.

## Files Created/Modified

- `tests/BlueCode.Tests/EditCommandTests.fs` (NEW) - 5 testCases in `testList "EditCommand"`: non-empty/empty/whitespace/tmpfile-cleanup/.md-extension
- `tests/BlueCode.Tests/ReplTests.fs` - 2 new testCases inside existing `testSequenced (testList "Repl" [...])` for /edit dispatch success and cancel
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj` - `<Compile Include="EditCommandTests.fs" />` added before RouterTests.fs
- `tests/BlueCode.Tests/RouterTests.fs` - `BlueCode.Tests.EditCommandTests.tests` appended to `rootTests` list
- `src/BlueCode.Cli/Repl.fs` - `editorLauncherOverride` mutable cell + match-expression seam in Slash Edit arm

## Decisions Made

- **editorLauncherOverride seam over function parameter:** Module-level mutable cell matches the existing Console.SetIn/SetOut convention already used by all ReplTests. Adding a parameter to `runMultiTurn` would require touching all call sites and breaking the existing interface.
- **recordingLlm via tryFindBack MessageRole.User:** Rather than asserting call count only, capturing the last User message content directly validates the `handlePromptTurn` dispatch path - confirms `Trim()` was applied and the correct content reached the LLM.
- **makeMockResponse wrapping fix (deviation auto-fix):** `makeMockResponse` already returns `Result<LlmResponse, AgentError>`. The PLAN.md code showed `Task.FromResult(Ok (makeMockResponse ...))` which double-wraps. Fixed to `Task.FromResult(makeMockResponse ...)`.
- **EditCommandTests uses plain testList:** No Console globals touched; Path.GetTempFileName produces unique paths per call; parallel execution is race-free. `testSequenced` is not needed and would reduce throughput unnecessarily.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed double-Ok wrapping in recordingLlm CompleteAsync**

- **Found during:** Task 2 (ReplTests integration tests)
- **Issue:** PLAN.md showed `Task.FromResult(Ok (makeMockResponse "done" (FinalAnswer "done")))`. `makeMockResponse` already returns `Ok { Thought = ...; Output = ... }`, so wrapping in another `Ok` produced `Ok (Ok {...})` - type error `'LlmResponse' required but 'Result<LlmResponse,AgentError>' given`.
- **Fix:** Changed to `Task.FromResult(makeMockResponse "done" (FinalAnswer "done"))`.
- **Files modified:** `tests/BlueCode.Tests/ReplTests.fs`
- **Verification:** Build passed, all 359 tests pass.
- **Committed in:** `6d12ccb` (Task 2b commit)

**2. [Rule 1 - Bug] Fixed `BlueCode.Core.Domain.Role.User` -> `User` in recordingLlm**

- **Found during:** Task 2 - recognized during code authoring (would have been a compile error)
- **Issue:** PLAN.md used `BlueCode.Core.Domain.Role.User` in the match pattern. The `MessageRole` DU is defined as `| System | User | Assistant` at module level, not nested under `Role`. The qualified form would be `BlueCode.Core.Domain.User`. Since `open BlueCode.Core.Domain` is already at the top of ReplTests.fs, the correct form is just `User`.
- **Fix:** Used `| User -> true` in the match pattern (unqualified, using the existing open directive).
- **Files modified:** `tests/BlueCode.Tests/ReplTests.fs`
- **Verification:** Build passed on first attempt after fix.
- **Committed in:** `6d12ccb` (Task 2b commit)

---

**Total deviations:** 2 auto-fixed (both Rule 1 — bugs in PLAN.md code snippets)
**Impact on plan:** Both fixes essential for compilation. No scope creep; plan intent preserved exactly.

## Bench Gate Result

`bash bench/run.sh --gate` reports 7/7 PASS post-Phase-34:

```
PASS T6_122b    steps=5/5 exit=0
PASS W1_122b    steps=3/3 exit=0
PASS W2_122b    steps=3/3 exit=0
PASS T1_122b    steps=1/3 exit=0
PASS T5_122b    steps=3/4 exit=0
PASS B2_122b    steps=2/3 exit=0
PASS MT_122b    steps=2/4 exit=0
===== GATE PASS (7/7) =====
```

`bench/baseline.json` byte-identical (zero modifications). Phase 34 structural changes (EditCommand.fs ProcessExit handler registered at module init, Repl.fs `editorLauncherOverride=None` in production path) introduce zero observable bench impact.

## Manual Smoke Notes

Manual smoke was not performed in this automated execution (requires real $EDITOR + real TTY). The automated integration tests cover the core contract: mock launcher writing content dispatches to LLM (recordingLlm asserts prompt = "list files"), empty content produces "Edit cancelled." with 0 LLM calls. Open Question #1 from 34-RESEARCH.md (real-TTY gibberish on macOS .NET 10) remains informational only - not a blocking gate for this plan.

## Test Count Progression

- Phase 34 Plan 01 baseline: 352 tests
- Phase 34 Plan 02 complete: 359 tests (+7: 5 EditCommandTests + 2 ReplTests)

## EDIT-01 Requirement: COMPLETE

All 6 success criteria satisfied (or documented as informational-only):

- **SC-1** (`/edit` invokes Path.GetTempFileName): GREEN - `.md extension` testCase proves it
- **SC-2** ($EDITOR env var; vi fallback): GREEN - production path exercised; mock tests cover contract
- **SC-3** (non-empty -> prompt; empty -> cancel): GREEN - both integration testCases assert this end-to-end
- **SC-4** (tmpfile cleanup): GREEN - "tmpfile deleted after read" testCase asserts `File.Exists = false` post-call
- **SC-5** (Ctrl+C recovery): PARTIAL INFORMATIONAL - `args.Cancel=true` handler in place; cancel-path integration test covers the empty-file branch; real SIGINT to running $EDITOR is manual-only
- **SC-6** (bench gate 7/7 PASS): GREEN - `bash bench/run.sh --gate` exits 0, 7/7 PASS confirmed

## Issues Encountered

None beyond the two auto-fixed PLAN.md code bugs documented in Deviations.

## Next Phase Readiness

- Phase 34 complete (both plans done)
- Ready for `/gsd:verify-work 34`
- Phase 35 (PrettyPrompt readline + history) can proceed; `editorLauncherOverride` seam pattern is a reusable template for any future REPL test-injectable overrides

---
*Phase: 34-edit-multi-line-input*
*Completed: 2026-05-05*
