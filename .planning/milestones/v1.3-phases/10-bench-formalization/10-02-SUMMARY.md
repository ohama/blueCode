---
phase: 10-bench-formalization
plan: 02
subsystem: testing
tags: [bash, jq, bench, regression-gate, baseline, SC1, BENCH-03, BENCH-04]

# Dependency graph
requires:
  - phase: 10-01
    provides: bench/run.sh with --canary, --b2, --regression, --all modes + 3 fixtures + bench/runs/ gitignored
provides:
  - bench/baseline.json: 8-entry post-09.1-05 ground truth for the gate regression subset
  - bench/run.sh --gate: 8-invocation regression gate; exits 0 on pass, 1 on regression, 2 on setup error
  - SC1 verified empirically (positive + negative round-trip)
affects:
  - 10-03: documents bench/run.sh --gate and baseline.json schema
  - 11-system-prompt-shrink: PERF-01/PERF-02/PERF-03 will use --gate to validate prompt shrink doesn't regress

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "3-branch verdict logic: is_regression → PASS; actual_steps > max → FAIL; pass + exit!=0 → FAIL; else PASS"
    - "Known regressions (B2_32b, B2_72b) marked regression=true in baseline.json; gate always passes them until PERF-03 updates baseline"
    - "bash 3.2 compat: parallel string list (labels=...), ${var:-default} for unset-safe arithmetic"

key-files:
  created:
    - bench/baseline.json
    - .planning/phases/10-bench-formalization/10-02-SUMMARY.md
  modified:
    - bench/run.sh

key-decisions:
  - "T6_72b live step count is 4 (not 5 from research defaults); step_count_max set to 5 to allow +1 variance"
  - "W1/W2 step_count_max = 3 (exact, no slack) because 09.1-05 loop-injection enforces exactly 3 steps"
  - "B2 entries: pass=false, regression=true; gate always passes them (verdict branch 1) until PERF-03 manually verifies correct diagnosis and updates baseline.json"
  - "Gate emits console-only output; no JSON report file in Phase 10 (per orchestrator decision; v1.4+ if needed for CI dashboards)"
  - "Fixture files restored before W1/W2 gate runs; result .fs files copied to gate log dir but not committed"

patterns-established:
  - "gate() reads LOG_DIR from its own local scope (not the outer $LOG_DIR set at script top)"
  - "Verdict logic is 3-branch only — no fourth 'possible regression recovery' branch"
  - "All 8 gate invocations run unconditionally; comparison happens in a post-run loop"

# Metrics
duration: 26min
completed: 2026-04-26
---

# Phase 10 Plan 02: Baseline + Gate Summary

**bench/baseline.json (8-entry post-09.1-05 ground truth) + bench/run.sh --gate (8-invocation regression gate) with SC1 verified positive + negative**

## Performance

- **Duration:** 26 min
- **Started:** 2026-04-25T19:16:13Z
- **Completed:** 2026-04-25T19:42:30Z
- **Tasks:** 2/2
- **Files modified:** 2 (bench/baseline.json created, bench/run.sh modified)

## Accomplishments

- Captured all 8 gate step counts live against the post-09.1-05 binary
- Created bench/baseline.json with machine-parseable entries including B2 regression annotations
- Added gate() function to bench/run.sh with 3-branch verdict logic, jq-based baseline parsing, bash 3.2 compat
- SC1 verified empirically: positive (exit 0, GATE PASS 8/8) and negative round-trip (tightened baseline → exit 1 GATE FAIL → restored → exit 0 GATE PASS)

## Live-Confirmed Step Counts

All 8 invocations observed against the post-09.1-05 binary on 2026-04-26:

| Key     | Observed Steps | step_count_max | exit | elapsed | Note |
|---------|---------------|----------------|------|---------|------|
| T6_32b  | 4             | 5              | 0    | 22s     | read x3 + final |
| T6_72b  | 4             | 5              | 0    | 45s     | research default was 5; live = 4 |
| W1_32b  | 3             | 3              | 0    | 14s     | loop-injection exact |
| W2_32b  | 3             | 3              | 0    | 17s     | loop-injection exact |
| T1_32b  | 1             | 3              | 0    | 5s      | canary |
| T5_72b  | 3             | 4              | 0    | 18s     | glob+shell+final |
| B2_32b  | 2             | 3              | 0    | 13s     | KNOWN regression |
| B2_72b  | 2             | 3              | 0    | 20s     | KNOWN regression |

**Deviation from research defaults:** T6_72b observed 4 steps, not 5. step_count_max set to 5 to allow +1 variance.

## Gate Verdict Logic (3-branch form)

```
for each key in {T6_32b, T6_72b, W1_32b, W2_32b, T1_32b, T5_72b, B2_32b, B2_72b}:
  is_regression = jq .tests.<key>.regression // false baseline.json
  actual_steps  = grep "[INF] Session (ok|error)" | grep -o "[0-9]* steps"
  actual_exit   = grep "exit=[0-9]*" from .meta file
  baseline_max  = jq .tests.<key>.step_count_max baseline.json
  baseline_pass = jq .tests.<key>.pass baseline.json

  Branch 1: is_regression = "true"  → PASS (known regression; B2_32b/B2_72b)
  Branch 2: actual_steps > baseline_max  → FAIL (step-count regression)
  Branch 3: baseline_pass = "true" AND actual_exit != 0  → FAIL (unexpected error exit)
  Default:   PASS
```

Known regressions (B2_32b, B2_72b) always pass Branch 1. The gate cannot detect answer-quality regression (both correct and wrong diagnosis produce exit=0, steps=2). PERF-03 (Phase 11) owns manual inspection and baseline.json update.

## SC1 Positive Verification

```
===== GATE: regression subset (8 invocations) =====
===== gate_T6_32b (model=32b) =====
  -> exit=0 elapsed=18s
===== gate_T6_72b (model=72b) =====
  -> exit=0 elapsed=26s
===== gate_W1_32b (model=32b) =====
  -> exit=0 elapsed=11s
===== gate_W2_32b (model=32b) =====
  -> exit=0 elapsed=16s
===== gate_T1_32b (model=32b) =====
  -> exit=0 elapsed=3s
===== gate_T5_72b (model=72b) =====
  -> exit=0 elapsed=15s
===== gate_B2_32b (model=32b) =====
  -> exit=0 elapsed=9s
===== gate_B2_72b (model=72b) =====
  -> exit=0 elapsed=18s
===== GATE: compare to baseline =====
  PASS T6_32b     steps=4/5 exit=0
  PASS T6_72b     steps=4/5 exit=0
  PASS W1_32b     steps=3/3 exit=0
  PASS W2_32b     steps=3/3 exit=0
  PASS T1_32b     steps=1/3 exit=0
  PASS T5_72b     steps=3/4 exit=0
  PASS B2_32b     steps=2/3 exit=0
  PASS B2_72b     steps=2/3 exit=0
===== GATE PASS (8/8) =====
exit=0 elapsed=116s
```

## SC1 Negative Verification (Tightened Baseline Round-Trip)

**Step 1: Tighten T6_32b.step_count_max = 0**
```
jq '.tests.T6_32b.step_count_max = 0' bench/baseline.json > /tmp/tight.json
mv /tmp/tight.json bench/baseline.json
bash bench/run.sh --gate
```

**Output (truncated to key lines):**
```
===== GATE: compare to baseline =====
  FAIL T6_32b     steps=4/0 exit=0 — steps=4 > baseline_max=0
  PASS T6_72b     steps=4/5 exit=0
  ...
===== GATE FAIL (1/8 regressed) =====
exit=1
```

**Step 2: Restore original baseline.json and verify round-trip clean:**
```
mv bench/baseline.json.orig bench/baseline.json
bash bench/run.sh --gate
```

**Output:**
```
===== GATE PASS (8/8) =====
exit=0
```

Round-trip clean. `git status --short bench/baseline.json` shows no diff (committed state restored).

## Task Commits

1. **Task 1: Run --canary live and write bench/baseline.json** - `56c5d1d` (feat)
2. **Task 2: Implement --gate mode in bench/run.sh** - `da30a46` (feat)

**Plan metadata:** (docs: complete baseline + gate plan)

## Files Created/Modified

- `bench/baseline.json` - 8-entry post-09.1-05 ground truth; B2 entries marked regression=true
- `bench/run.sh` - Added gate() function (~100 lines), updated show_help(), updated case dispatcher

## Decisions Made

- T6_72b step_count = 4 (not 5 per research); step_count_max = 5 (observed + 1 slack)
- W1/W2 step_count_max = 3 (exact, no slack); loop-injection enforces this
- 3-branch verdict logic only — no fourth "regression recovery" branch (removed during plan-checker iteration)
- Gate output is console-only; no JSON report file in Phase 10
- gate() creates its own LOG_DIR with "gate-" prefix to distinguish from canary/regression runs

## Deviations from Plan

None - plan executed exactly as written. T6_72b observed step count (4 not 5) was within the plan's guidance to "prefer live values" over research defaults.

## Issues Encountered

None. Fixture files (bug_lastchar.fs, bug_average.fs) were modified by gate runs (W1/W2 restore-and-fix cycle) and restored with `git checkout --` before each commit to keep them in their canonical broken state.

## Hang Retries

None. All 8 invocations completed within normal bounds. No kickstarts required.

## Next Phase Readiness

- Plan 10-03 can document bench/run.sh --gate and baseline.json schema in documentation/bench.md
- bench/run.sh --gate is the canonical quality gate for Phase 11 PERF-01/02/03
- B2_32b/B2_72b remain in known-regression state; PERF-03 owns the fix and baseline.json update

---
*Phase: 10-bench-formalization*
*Completed: 2026-04-26*
