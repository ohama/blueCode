---
phase: 33-slash-plan-toggle
plan: 02
subsystem: cli-repl-tests
tags: [fsharp, expecto, integration-tests, plan-mode, spectre-console, repl]

# Dependency graph
requires:
  - phase: 33-01
    provides: planModeActive mutable cell + plan-gate inline loop in runMultiTurnWithSession + renderStatus 4-arg signature
provides:
  - 6 new integration testCases in ReplTests.fs covering plan toggle on/off, /status plan-mode display, plan-gate Accept/Quit/Error paths, and auto-disable semantics
  - Bench gate 7/7 PASS verified (no regression from Phase 33-01 dispatcher additions)
  - End-to-end smoke test confirmed /plan toggle wiring from Program.fs through Repl.runMultiTurnWithSession
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Spectre.Console singleton reset pattern (AnsiConsole.Console <- AnsiConsole.Create(AnsiConsoleSettings())) before tests that invoke AnsiConsole.Write(table) through REPL; prevents ObjectDisposedException from cached writer stale after prior test disposal"
    - "Post-marker stdout-slice technique for auto-disable assertions: capture.Substring(markerIdx) + Expect.isFalse Contains 'plan-mode'"

key-files:
  created: []
  modified:
    - tests/BlueCode.Tests/ReplTests.fs

key-decisions:
  - "Spectre.Console AnsiConsole.Console reassignment before plan-gate tests: PlanGate.render calls AnsiConsole.Write(table) which caches Console.Out on first use; resetting it prevents ObjectDisposedException when prior test's StringWriter was disposed"

patterns-established:
  - "Spectre.Console reset pattern: tests that call AnsiConsole.Write through the REPL must reset AnsiConsole.Console after Console.SetOut redirect and restore in finally"

# Metrics
duration: 11min
completed: 2026-05-05
---

# Phase 33 Plan 02: Behavior Tests and Bench Summary

**6 plan-mode REPL integration tests (toggle on/off, /status display, Accept/Quit/Error auto-disable) + bench gate 7/7 PASS + Spectre.Console reset pattern discovered and applied**

## Performance

- **Duration:** 11 min
- **Started:** 2026-05-04T22:58:38Z
- **Completed:** 2026-05-05T23:09:31Z
- **Tasks:** 3 (Task 1: tests added + committed; Task 2: smoke verified; Task 3: bench gate verified)
- **Files modified:** 1

## Accomplishments

- 6 new integration testCases inserted into ReplTests.fs `testSequenced` block; all 352 tests pass
- End-to-end smoke test confirmed toggle wiring: `[plan mode on]`, `plan-mode: on`, `[plan mode off]`, second `/status` shows no plan-mode line
- Bench gate 7/7 PASS preserved — Phase 33 dispatcher additions cause zero regression on agent-loop/plan-mode invocations

## Task Commits

Each task was committed atomically:

1. **Task 1: Add 6 new plan-mode integration testCases to ReplTests.fs** - `a456858` (test)
2. **Task 2: Smoke-test the /plan toggle end-to-end** - no commit (verification only)
3. **Task 3: Verify bench gate 7/7 PASS** - no commit (verification only)

## Smoke Evidence (Task 2)

```
blueCode — multi-turn mode. Session: 3999727eee7e4458904bfa78983e1126. Type /exit or press Ctrl+D to quit.

blueCode> [plan mode on] — next prompt will enter plan-gate before execution

blueCode> session:  3999727eee7e4458904bfa78983e1126
model:    122b
steps:    0
chars:    0 / ~32768 (0%) [floor; probed on first LLM call]
plan-mode: on (next prompt uses plan-gate)

blueCode> [plan mode off] — returning to direct agent-loop

blueCode> session:  3999727eee7e4458904bfa78983e1126
model:    122b
steps:    0
chars:    0 / ~32768 (0%) [floor; probed on first LLM call]

blueCode> exit code: 0
```

All expected strings present. Exactly 1 `plan-mode: on` line (second /status shows no plan-mode line after toggle off).

## Bench Gate Result (Task 3)

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

7/7 PASS — no regression from Phase 33 additions (bench is single-turn and never enters runMultiTurnWithSession).

## Files Created/Modified

- `tests/BlueCode.Tests/ReplTests.fs` - Added 6 new integration testCases + Spectre.Console reset helper pattern in tests 4 and 5

## Test Count Delta

- Pre-Plan 33-02 baseline: 346 tests (post-Plan 33-01)
- Post-Plan 33-02: 352 tests (+6)
- Cumulative Phase 33 delta: 345 → 352 = +7 (Plan 33-01: +1 RenderingTests; Plan 33-02: +6 ReplTests)

## Production LOC Added

0 — this plan is test-only. All 6 new tests + the Spectre reset code live in ReplTests.fs.

## Test LOC Added

~340 lines across 6 new testCases (~55 LOC each including stdin/stdout setup, Spectre reset, components wiring, assertions, and finally cleanup).

## Decisions Made

- **Spectre.Console AnsiConsole.Console reset in plan-gate tests:** `PlanGate.render` calls `AnsiConsole.Write(table)` which Spectre caches at first use. When a prior test's `StringWriter` was set as `Console.Out` and then disposed (via `use` binding after the `finally` block), the cached `SyncTextWriter` pointed to a disposed writer. Fix: reset `AnsiConsole.Console <- AnsiConsole.Create(AnsiConsoleSettings())` after `Console.SetOut(stdoutWriter)` in tests 4 and 5, restore in `finally`. This is a test-only concern (production runs never dispose `Console.Out`).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Spectre.Console ObjectDisposedException in plan-gate tests**

- **Found during:** Task 1 (running tests after inserting the 6 testCases)
- **Issue:** Tests 4 (Accept) and 5 (Quit) failed with `System.ObjectDisposedException: Cannot write to a closed TextWriter` inside `Spectre.Console.LegacyConsoleBackend.Write`. Cause: `AnsiConsole` singleton lazily caches `Console.Out` on first `AnsiConsole.Write()` call. After a prior test redirected `Console.Out` to a `StringWriter` and then the test's `finally` block restored the original `Console.Out`, the `StringWriter` was disposed by its `use` binding. The subsequent test's call to `PlanGate.render` → `AnsiConsole.Write(table)` then tried to write through the stale cached `SyncTextWriter` wrapping the disposed `StringWriter`.
- **Fix:** Added `Spectre.Console.AnsiConsole.Console <- Spectre.Console.AnsiConsole.Create(Spectre.Console.AnsiConsoleSettings())` immediately after `Console.SetOut(stdoutWriter)` in tests 4 and 5. Saved original Spectre console before each test; restored it in `finally`. This re-ties Spectre to the fresh `stdoutWriter` for the duration of that test.
- **Files modified:** `tests/BlueCode.Tests/ReplTests.fs`
- **Verification:** All 352 tests pass. `ObjectDisposedException` gone.
- **Committed in:** `a456858` (included in Task 1 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 bug — test infrastructure issue, not production source)
**Impact on plan:** Bug fix was necessary for tests 4 and 5 to pass. No scope creep. Production source untouched.

## Phase 33 Success Criteria Verification

1. **SC-1 (planModeActive in REPL state + toggle + /status display):** Tests 1+3 + Task 2 smoke ✓
2. **SC-2 (runPlanTurn route on next prompt when active):** Test 4 (Accept: LLM called twice; plan rationale rendered; "Accepted." printed; final answer printed) ✓
3. **SC-3 (/plan again toggles off):** Test 2 (both `[plan mode on]` and `[plan mode off]` notifications appear in order) ✓
4. **SC-4 (mid-turn /plan invalid):** N/A — architectural (REPL ReadLine blocks the loop thread; no race possible per research § Q4). Not testable; documented ✓
5. **SC-5 (Bench gate 7/7 PASS):** Task 3 gate result: 7/7 PASS ✓
6. **SC-6 (Role=System invariant; toggle notification user-facing only):** Code inspection (notifications use `printfn`) + Test 1 zero-LLM assertion ✓
7. **SC-7 (auto-disable semantics):** Tests 4+5+6 (post-Accept/Quit/error /status does NOT contain "plan-mode") ✓
8. **SC-8 (process does NOT exit on plan-gate Quit):** Test 5 (post-Quit /status executes; /exit produces exit code 0) ✓

## Issues Encountered

- `ObjectDisposedException` from Spectre.Console `AnsiConsole.Write(table)` in tests 4 and 5 (described in Deviations). Resolved via Spectre console reset pattern.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- Phase 33 complete. All 8 success criteria satisfied.
- Ready for `/gsd:verify-work 33` UAT gate.
- Next phase: Phase 34 (`/edit` multi-line input) or Phase 35 (PrettyPrompt readline), per ROADMAP.md v2.5 remaining phases.

---
*Phase: 33-slash-plan-toggle*
*Completed: 2026-05-05*
