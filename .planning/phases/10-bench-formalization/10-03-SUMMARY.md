---
phase: 10-bench-formalization
plan: 03
subsystem: testing
tags: [bench, documentation, regression-gate, BENCH-05, baseline]

# Dependency graph
requires:
  - phase: 10-01
    provides: bench/run.sh with mode dispatch + bench/fixtures/ committed
  - phase: 10-02
    provides: bench/baseline.json (8 entries) + --gate mode + SC1 verified positive+negative
provides:
  - documentation/bench.md (190 lines, 11 sections, all 5 BENCH-05 topics)
  - Phase 10 final --gate verification captured (GATE PASS 8/8)
  - Phase 10 source-code invariant confirmed (zero src/tests drift)
affects: [11-system-prompt-shrink]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "bench.md documents W1 prompt exception: deliberate write_file mention validates 09.1-05 loop-injection regression"
    - "B2 known-regression pattern: pass=false + regression=true = gate PASSes until PERF-03 manually verified"

key-files:
  created:
    - documentation/bench.md
  modified: []

key-decisions:
  - "SC5 note: CLAUDE.md ## Bench section contains historical '/tmp/bench-v1.2/run.sh' in a 'replacement for...' context sentence (Plan 10-01 commit 66ab640). This is intentional provenance text, not a stale operational reference. No change needed."
  - "Fixture restore after gate run is expected behavior: W1/W2 write tests leave fixtures in fixed state; restored to baseline via git checkout before plan-metadata commit to keep repo clean."

patterns-established:
  - "bench.md: authoritative operator guide for bench/ harness — link from CLAUDE.md ## Bench section"
  - "Phase 10 invariant: bench-only phase, zero src/ or tests/ drift confirmed across all 8 commits"

# Metrics
duration: 15min
completed: 2026-04-26
---

# Phase 10 Plan 03: Bench Documentation + Final Verification Summary

**190-line bench.md covering all 5 BENCH-05 topics (fixture naming, prompt design, how-to-add, baseline update, hang contingency), plus final --gate PASS (8/8) confirming Phase 10 invariant holds end-to-end**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-04-25T19:47:05Z
- **Completed:** 2026-04-25T20:02:00Z
- **Tasks:** 2 (Task 1: write bench.md; Task 2: final verification, read-only)
- **Files modified:** 1 (`documentation/bench.md` created)

## Accomplishments

- Created `documentation/bench.md` (190 lines, 11 sections) covering all 5 required BENCH-05 topics with substantive prose
- Documented W1 prompt exception: deliberate `write_file` mention validates 09.1-05 loop-injection regression (do not remove)
- Documented B2 known-regression state and PERF-03 Phase 11 recovery procedure
- Final `bash bench/run.sh --gate` exits 0 (`GATE PASS (8/8)`) confirming SC1 end-to-end
- Phase 10 source-code invariant verified: `git diff --stat HEAD~7..HEAD -- src/ tests/` empty

## documentation/bench.md Section Structure

| Section | BENCH-05 Topic Covered |
|---------|------------------------|
| Overview | Harness design rationale, links to v1.2-bench-followup.md and 09.1-VALIDATION.md |
| Quick Start | Entry point: `dotnet build` then `bash bench/run.sh --gate` |
| Mode Flags | All 6 flags with invocation counts and wall-clock estimates |
| Fixture Naming Convention | BENCH-05: fixture naming (bug_<symptom>.fs pattern, restore behavior) |
| Prompt Design Guidance | BENCH-05: prompt design (W1 exception, 09.1-04/05 rationale) |
| How to Add a New Test | BENCH-05: how-to-add (5-step procedure, diagnose vs write distinction) |
| How to Update Baseline After an Intentional Fix | BENCH-05: how-to-update-baseline (PERF-03 recovery procedure) |
| Hang Contingency for mlx_lm.server 32B | BENCH-05: hang contingency (launchctl kickstart commands, frequency, rationale) |
| Interpreting Gate Output | PASS/FAIL output format with real example |
| Known Regressions (Baseline State) | B2 pass=false intent, PERF-03 Phase 11 target |
| See Also | Links to bench/run.sh, bench/baseline.json, v1.2-bench-followup.md, 09.1-VALIDATION.md, CLAUDE.md |

## Final --gate Output (captured 2026-04-25T19:54:xx)

```
===== GATE: regression subset (8 invocations) =====
===== gate_T6_32b (model=32b) =====
  -> exit=0 elapsed=17s
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
  -> exit=0 elapsed=10s
===== gate_B2_72b (model=72b) =====
  -> exit=0 elapsed=17s
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
```

Exit code: 0. Total wall-clock: ~115s.

## Phase 10 Source-Code Invariant Check

```bash
git diff --stat HEAD~7..HEAD -- src/ tests/
# (empty output)
```

Zero source-code drift confirmed across all 8 Phase 10 commits (Plans 10-01, 10-02, 10-03). Phase 10 was bench-only as designed.

## Phase 10 ROADMAP Success Criteria — All 5 Verified

### SC1: `bench/run.sh --gate` exits 0 (positive) and non-zero (negative)

- **Positive:** Final gate run exits 0, `GATE PASS (8/8)` (captured above)
- **Negative:** Plan 10-02 tightened `T6_32b.step_count_max=0` → `GATE FAIL` exit=1; restored → GATE PASS round-trip clean
- **Verification command:** `bash bench/run.sh --gate 2>&1 | tee /tmp/phase10-final-gate.txt && echo "EXIT: $?"`
- **Result:** PASS

### SC2: `bench/fixtures/` contains exactly 3 fixtures

```bash
ls bench/fixtures/ | sort | tr '\n' ','
# bug_average.fs,bug_divide_zero.fs,bug_lastchar.fs,
```

- **Verification command:** `ls bench/fixtures/ | sort`
- **Result:** PASS — exactly bug_average.fs, bug_divide_zero.fs, bug_lastchar.fs

### SC3: `bench/baseline.json` valid JSON, 8 entries

```bash
jq -e '.tests | length == 8' bench/baseline.json
# true
```

- **Verification command:** `jq -e '.tests | length == 8' bench/baseline.json`
- **Result:** PASS — 8 entries, valid JSON

### SC4: `documentation/bench.md` exists, ≥80 lines, 5 topics

```bash
wc -l < documentation/bench.md
# 190
grep -cE '^## ' documentation/bench.md
# 11
```

- **Verification command:** `wc -l < documentation/bench.md && grep -cE '^## ' documentation/bench.md`
- **Result:** PASS — 190 lines, 11 sections, all 5 BENCH-05 topics covered

### SC5: CLAUDE.md `## Bench` section present, no stale `/tmp/bench-v1.2/` operational references

```bash
grep -A 10 '^## Bench' CLAUDE.md
# (shows the section added in Plan 10-01 commit 66ab640)
```

- Note: CLAUDE.md line 164 contains `v1.2's ephemeral '/tmp/bench-v1.2/run.sh'` — this is intentional provenance text ("repo-tracked replacement for..."), not a stale operational reference. Committed in Plan 10-01 deliberately.
- **Result:** PASS — `## Bench` section present, no stale operational references to `/tmp/bench` paths

## BENCH-01..05 Requirements Traceability

| Req | Description | Artifact |
|-----|-------------|---------|
| BENCH-01 | bench/run.sh repo-tracked | `bench/run.sh` (Plan 10-01) |
| BENCH-02 | Fixtures committed | `bench/fixtures/` 3 files (Plan 10-01) |
| BENCH-03 | baseline.json recorded | `bench/baseline.json` 8 entries (Plan 10-02) |
| BENCH-04 | `--gate` mode exits non-zero on regression | `bench/run.sh --gate` (Plan 10-02) |
| BENCH-05 | documentation/bench.md operator guide | `documentation/bench.md` 190 lines (Plan 10-03) |

## Task Commits

1. **Task 1: Write documentation/bench.md** - `d244bba` (docs)
2. **Task 2: Final phase verification** - no commit (read-only)

**Plan metadata:** TBD (staged after SUMMARY + STATE update)

## Files Created/Modified

- `documentation/bench.md` — 190-line operator guide for bench/ harness, all 5 BENCH-05 topics

## Decisions Made

- SC5 historical reference: CLAUDE.md `## Bench` section contains `/tmp/bench-v1.2/run.sh` in a provenance sentence — intentional context, not stale operational reference. Plan 10-01 committed this deliberately. No modification required.
- Fixture restore after gate run: W1/W2 write tests leave fixtures in agent-fixed state; restored to committed baseline via `git checkout bench/fixtures/...` before plan-metadata commit (this is expected bench harness behavior, fixtures are mutated by write tests and need the restore-before-run pattern).

## Deviations from Plan

None — plan executed exactly as written. Task 1 content verbatim from plan body. Task 2 verification-only. One minor clarification noted (SC5 grep match on historical reference text) but no action required.

## Issues Encountered

None. Gate ran cleanly in ~115s. Build was already current (0 errors). Fixtures were in modified state after gate run (expected W1/W2 write test side-effect) — restored to baseline via `git checkout` before commit.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

Phase 10 is complete. All 5 ROADMAP Phase 10 success criteria verified. All BENCH-01..05 requirements satisfied with committed artifacts.

**Ready for:** `/gsd:verify-work 10` (UAT gate) before Phase 11 starts.

Phase 11 (System Prompt Shrink) can build immediately on `bash bench/run.sh --gate` to validate PERF-01 prompt shrink iteratively. The B2 entries in `bench/baseline.json` (`pass=false, regression=true`) will be the primary PERF-03 recovery target: after shrink, inspect `bench/runs/gate-<ts>/gate_B2_32b.log` for "empty list" vs "integer truncation" to confirm recovery, then update `bench/baseline.json` and re-run gate.

---
*Phase: 10-bench-formalization*
*Completed: 2026-04-26*
