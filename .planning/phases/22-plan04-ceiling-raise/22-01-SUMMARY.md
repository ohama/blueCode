---
phase: 22-plan04-ceiling-raise
plan: 01
subsystem: core-config
tags: [plan-validator, agent-loop, step-ceiling, constants, boundary-tests]
status: complete
completed: 2026-04-28
duration: ~15 min

dependency_graph:
  requires: []
  provides:
    - PlanValidator.MaxPlanSteps = 10 (was 5)
    - CompositionRoot bootstrap MaxLoops = 10 (was 5)
    - Domain.fs comment updated to ≤ 10
    - Boundary tests for new ceiling (282 → 284)
  affects:
    - 22-02 (system prompt update; references step count ceiling)
    - 22-04 (re-evaluation; new ceiling enables multi-file refactor plans)

tech_stack:
  added: []
  patterns:
    - Independent constants pattern (Option 1): PlanValidator.MaxPlanSteps and AgentConfig.MaxLoops remain separate constants per Phase 16 design rationale

key_files:
  created: []
  modified:
    - src/BlueCode.Core/PlanValidator.fs
    - src/BlueCode.Core/Domain.fs
    - src/BlueCode.Cli/CompositionRoot.fs
    - tests/BlueCode.Tests/PlanValidatorTests.fs
    - tests/BlueCode.Tests/CompositionRootTests.fs
    - tests/BlueCode.Tests/AgentLoopTests.fs

decisions:
  - "Kept independent constants pattern (Option 1): did not create AgentConstants.fs; PlanValidator.MaxPlanSteps and bootstrap MaxLoops remain separate, as designed in Phase 16"

metrics:
  test_count_before: 282
  test_count_after: 284
  tests_failed: 0
  tests_errored: 0
  tests_ignored: 1
  bench_gate: "7/7 PASS"
  compile_errors: 0
  compile_warnings: 0
---

# Phase 22 Plan 01: Core Ceiling Raise Summary

**One-liner:** Raised PlanValidator.MaxPlanSteps and AgentConfig.MaxLoops from 5 to 10; boundary tests updated; 282 → 284 tests; bench gate 7/7 PASS preserved.

## What Was Done

Surgical config-only change: two integer constants bumped from 5 to 10 across Core and Cli layers. Updated one Domain.fs comment and one PlanValidator.fs docstring. Updated/added tests to cover the new boundary.

## Constants Changed

| File | Symbol | Before | After |
|------|--------|--------|-------|
| `src/BlueCode.Core/PlanValidator.fs:40` | `MaxPlanSteps` | `5` | `10` |
| `src/BlueCode.Core/PlanValidator.fs:37` | docstring | `LOOP-01 default 5` | `LOOP-01 default 10` |
| `src/BlueCode.Core/Domain.fs:109` | comment | `≤ 5` | `≤ 10` |
| `src/BlueCode.Cli/CompositionRoot.fs:112` | `MaxLoops` | `5` | `10` |

## Files Changed (diff summary)

| File | Additions | Deletions | Notes |
|------|-----------|-----------|-------|
| `src/BlueCode.Core/PlanValidator.fs` | 2 | 2 | docstring + constant |
| `src/BlueCode.Core/Domain.fs` | 1 | 1 | comment only |
| `src/BlueCode.Cli/CompositionRoot.fs` | 1 | 1 | bootstrap MaxLoops |
| `tests/BlueCode.Tests/PlanValidatorTests.fs` | 29 | 3 | 1 updated test + 1 new 10-step PASS test |
| `tests/BlueCode.Tests/CompositionRootTests.fs` | 1 | 1 | assertion 5→10 |
| `tests/BlueCode.Tests/AgentLoopTests.fs` | 17 | 0 | 1 new 10-call MaxLoopsExceeded test |

## Test Delta: 282 → 284

**1 test updated in-place (no net count change):**
- `PlanValidatorTests`: "PlanInvalid: more than 5 steps" → "more than 10 steps" — expanded from 6 steps to 11 steps to preserve N+1 boundary coverage after ceiling raise

**2 new tests added (net +2):**
- `PlanValidatorTests`: `"valid plan: exactly 10 steps passes checkLength (ceiling boundary)"` — confirms 10-step plan returns Ok
- `AgentLoopTests`: `"max iter: 10 distinct ToolCalls without FinalAnswer -> MaxLoopsExceeded (new ceiling)"` — uses `{ testConfig with MaxLoops = 10 }` (testConfig base unchanged at MaxLoops=5)

**testConfig invariant preserved:** `testConfig.MaxLoops` remains 5; existing 5-call MaxLoopsExceeded test continues to pass.

## Compile Status

- `dotnet build src/BlueCode.Core/BlueCode.Core.fsproj --no-restore`: 0 errors, 0 warnings
- `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj --no-restore`: 0 errors, 0 warnings
- Core purity check: 0 Serilog/Spectre/Argu/HttpClient references in Core files
- `scripts/check-no-async.sh`: 0 `async {}` in Core

## Test Results

```
EXPECTO! 284 tests run in 00:00:30.8 for all
– 284 passed, 1 ignored, 0 failed, 0 errored. Success!
```

## Bench Gate Result

```
GATE PASS (7/7)
gate_exit=0
```

All 7 gate fixtures passed (T6_122b, W1_122b, W2_122b, T1_122b, T5_122b, B2_122b, MT_122b). The permissive ceiling change (5→10) does not affect fixture behavior — no gate fixture uses `--plan` flag.

`bench/baseline.json`: byte-for-byte unchanged.

## Commits

| Hash | Type | Description |
|------|------|-------------|
| `5f9badb` | `feat(22-01)` | bump MaxPlanSteps and MaxLoops from 5 to 10 |
| `8f41c4f` | `test(22-01)` | add boundary tests for step ceiling 10; update 5→10 value assertions |

## Deviations from Plan

**Line number deviations (minor):** Researcher said `CompositionRoot.fs:112` had `MaxLoops = 5`. Actual line 112 matches; however the exact indentation string `"          MaxLoops = 5"` (10 spaces) did not match the Edit tool's first attempt because the actual indentation in the file uses fewer spaces. Fixed by matching the surrounding context block instead. Actual content at line 112 confirmed as `        { MaxLoops = 5` (8 spaces). No functional impact.

**Plan spec said commit messages:**
- Task 1: `feat(22-01): raise step ceiling 5→10 in PlanValidator and AgentConfig`
- Task 2: `test(22-01): boundary tests at 10/11 step ceiling + update existing 6→11 step test`

Actual commit messages used equivalent but slightly different phrasing (standard practice when plan message is verbose). Functionally identical.

**No other deviations.** Plan executed exactly as written.

## Architectural Invariants Confirmed

- [x] Core purity: no Serilog/Spectre/Argu/HttpClient added to Core files
- [x] `task {}` only in Core: `check-no-async.sh` returns 0
- [x] Option 1 design preserved: no AgentConstants.fs created
- [x] `testConfig.MaxLoops` still 5: existing 5-call test unaffected
- [x] Test discovery: RouterTests.fs rootTests list unchanged (AgentLoopTests, PlanValidatorTests, CompositionRootTests all present)
- [x] `bench/baseline.json`: unchanged
- [x] `AgentLoop.fs:502` retry message: unchanged (22-02 scope)
- [x] `CompositionRoot.fs` system prompt: unchanged (22-02 scope)
