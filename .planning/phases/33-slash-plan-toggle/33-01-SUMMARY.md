---
phase: 33-slash-plan-toggle
plan: "01"
subsystem: cli-repl
tags: [fsharp, repl, slash-commands, plan-mode, plan-gate, rendering]

# Dependency graph
requires:
  - phase: 31-02
    provides: slash dispatcher infrastructure (runMultiTurnWithSession, Slash DU, renderStatus 3-arg)
  - phase: 32-02
    provides: /resume in-place rebind pattern; Slash dispatcher extension pattern (new arms above Prompt arm)
provides:
  - mutable planModeActive cell in runMultiTurnWithSession
  - Slash Plan arm (toggle + printfn notification)
  - Slash Edit slim stub (sole remaining future-stub)
  - Prompt arm guarded by `when planModeActive` (full plan-gate inline loop mirroring Program.fs)
  - renderStatus 4-param signature (adds planModeActive: bool)
  - renderHelp /plan live description (drops [coming in v2.5])
  - 1 new RenderingTests testCase (planModeActive=true display)
  - 4 existing renderStatus tests updated to 4-param; 1 marker test updated; 1 ReplTests future-stub test updated
affects:
  - 33-02  # behavior integration tests + bench gate consume the new code

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "plan-gate inline loop in REPL: mirrors Program.fs plan-mode single-turn path adapted for multi-turn context (Quit → REPL, not exit; planModeActive auto-disables on Accept/Quit/exhausted-rejects)"
    - "renderStatus 4-param signature: planLine appended conditionally (planModeActive=true only); quiet by default"
    - "Fully-qualified BlueCode.Cli.PlanGate.* names in Repl.fs (no new open directive)"

key-files:
  created: []
  modified:
    - src/BlueCode.Cli/Repl.fs
    - src/BlueCode.Cli/Rendering.fs
    - tests/BlueCode.Tests/RenderingTests.fs
    - tests/BlueCode.Tests/ReplTests.fs

key-decisions:
  - "planModeActive auto-disables after Accept+Execute (one-shot semantics; user re-types /plan for next plan-gated turn)"
  - "planModeActive auto-disables after Quit (user explicitly abandoned; staying in plan-mode would be surprising)"
  - "[plan mode on/off] notification is immediate printfn only — SC-6 Role=System invariant (never injected into LLM messages)"
  - "BlueCode.Cli.PlanGate.* fully-qualified (avoids open directive conflict; matches AgentLoop.runPlanTurn style)"
  - "renderStatus 4-arg; plan-mode line absent by default (planModeActive=false) — /status stays quiet unless toggle is on"

patterns-established:
  - "One-shot plan-mode semantics: Accept/Quit/exhausted-rejects all disable planModeActive; user must re-type /plan"
  - "REPL plan-gate Quit: planModeActive <- false + turnDone <- true; NEVER sets running <- false"

# Metrics
duration: 6min
completed: 2026-05-05
---

# Phase 33 Plan 01: Toggle and Wiring Summary

**`/plan` toggle wired into REPL with full plan-gate inline loop, `renderStatus` 4-arg signature adding plan-mode display, and renderHelp /plan promoted from stub to live description**

## Performance

- **Duration:** ~6 min
- **Started:** 2026-05-04T22:49:12Z
- **Completed:** 2026-05-04T22:55:25Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- `runMultiTurnWithSession` gains `let mutable planModeActive = false` cell + `Slash Plan` toggle arm (flip + immediate printfn notification) + `Slash Edit` slim stub + `Prompt prompt when planModeActive` plan-gate inline loop (mirrors Program.fs:172-256)
- `renderStatus` 4-param signature: appends `\nplan-mode: on (next prompt uses plan-gate)` only when `planModeActive=true`; silent by default
- `renderHelp` /plan line promoted from `[coming in v2.5]` stub to live description (`toggle plan-mode on/off; next prompt uses plan-gate when on`); /edit retains marker
- 346 tests pass (345 baseline + 1 new RenderingTests planModeActive=true testCase); 0 warnings

## Task Commits

1. **Task 1: Wire planModeActive cell + /plan toggle + plan-gate Prompt arm into Repl.fs; update renderStatus + renderHelp in Rendering.fs** — `b69d08b` (feat)
2. **Task 2: Adapt existing tests for renderStatus 4-arg + future-stub delta; add new RenderingTests planModeActive=true testCase** — `37b5979` (test)

## Files Created/Modified

- `src/BlueCode.Cli/Repl.fs` — +102/-8 LOC: planModeActive cell, Slash Plan toggle arm, Slash Edit stub, plan-gate Prompt arm with Accept/Reject/Edit/Quit/exhausted-reject handling
- `src/BlueCode.Cli/Rendering.fs` — renderStatus 4-param signature + planLine conditional; renderHelp /plan line updated
- `tests/BlueCode.Tests/RenderingTests.fs` — 4 renderStatus call sites updated (3-arg → 4-arg with `false`); renderHelp marker test updated (2 occurrences → 1, +plan live assertion); 1 new planModeActive=true testCase added
- `tests/BlueCode.Tests/ReplTests.fs` — future-stub test renamed/updated (expects 1 line, /edit only; stdin shrunk from `/plan\n/edit\n/exit\n` to `/edit\n/exit\n`)

## Decisions Made

- **One-shot plan-mode after Accept:** planModeActive auto-disables on Accept+Execute (Open Question #1 resolution: avoids "stuck in plan-review loop" surprise)
- **One-shot plan-mode after Quit:** planModeActive auto-disables on Quit (Open Question #2 resolution: user explicitly abandoned)
- **Immediate printfn notification:** `[plan mode on/off]` printed on `/plan` keystroke, not deferred (Open Question #3 resolution: PlanGate UI itself serves as "turn start" announcement)
- **Fully-qualified PlanGate.* names:** `BlueCode.Cli.PlanGate.render`, `BlueCode.Cli.PlanGate.promptUser`, etc. — no new `open` directive in Repl.fs; matches AgentLoop style

## Phase 33 Success Criteria Status (observable via code inspection)

1. **SC-1 (planModeActive cell + toggle):** `let mutable planModeActive = false` + `Slash Plan` arm + printfn notification ✓
2. **SC-2 (runPlanTurn on next prompt when active):** `Some (Prompt prompt) when planModeActive` arm calls `BlueCode.Core.AgentLoop.runPlanTurn` ✓
3. **SC-3 (toggle is symmetric):** `planModeActive <- not planModeActive`; typing `/plan` twice toggles on then off ✓
4. **SC-4 (mid-turn /plan invalid):** architectural; ReadLine blocks the loop thread — no race possible ✓ (N/A guard)
6. **SC-6 (Role=System invariant):** notifications via `printfn` only; never injected into LLM message list ✓

SC-5 (bench gate) is gated by Plan 33-02.

## Smoke Test Output

```
blueCode — multi-turn mode. Session: 24841a889b554a518594c45b89f6c157.

blueCode> slash commands:
  /help              show this help
  ...
  /plan              toggle plan-mode on/off; next prompt uses plan-gate when on
  /edit              open $EDITOR for multi-line input [coming in v2.5]

blueCode> [plan mode on] — next prompt will enter plan-gate before execution

blueCode> session:  24841a889b554a518594c45b89f6c157
model:    122b
steps:    0
chars:    0 / ~32768 (0%) [floor; probed on first LLM call]
plan-mode: on (next prompt uses plan-gate)

blueCode> [plan mode off] — returning to direct agent-loop

blueCode> Exit code: 0
```

## Deviations from Plan

None — plan executed exactly as written. Research was HIGH confidence; all 3 open-question resolutions adopted without modification.

## Issues Encountered

None.

## Next Phase Readiness

Plan 33-01 complete. Plan 33-02 (behavior tests + bench gate) is ready to execute:
- New `/plan` toggle code is committed and building clean
- 346 tests pass; Core untouched; no-async check passes
- Plan 33-02 will add integration tests for toggle on/off, /status display, plan-gate Accept/Quit/auto-disable, and run bench gate

---
*Phase: 33-slash-plan-toggle*
*Completed: 2026-05-05*
