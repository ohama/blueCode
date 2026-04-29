---
phase: 28-f-coding-quality-measurement-harness-audit
plan: 01
subsystem: documentation
tags: [bash, strict-mode, harness, eval, howto, pipefail, grep]

# Dependency graph
requires:
  - phase: 27-f-correctness-phase2-remediation
    provides: bench/eval-qwen35-122b.sh with v2.3 orphan-grep fix (Pattern 4 precursor)
provides:
  - documentation/howto/macos-bash-strict-mode-patterns.md (5 patterns documented)
  - Pattern 5 fix: grep -oE session-id guard in run_multiturn (commit 94d905c)
affects:
  - Phase 28 plans 02-06 (all use bench/eval-qwen35-122b.sh)
  - Any future phase adding eval harness handlers

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "bash strict-mode guard: || true on all grep -c / grep -cE / grep -oE in command substitutions"
    - "bash strict-mode guard: set +e / set -e wrapper for dotnet run invocations"
    - "bash strict-mode guard: mkdir -p before first tee / redirect in each run_* function"

key-files:
  created:
    - documentation/howto/macos-bash-strict-mode-patterns.md
  modified:
    - bench/eval-qwen35-122b.sh (Pattern 5 fix: line 378 grep -oE guard)

key-decisions:
  - "Pattern 5 classified as real bug (not false alarm): grep -oE zero-match aborts script before if [ -z sid ] guard can run"
  - "5th pattern included in howto (not suppressed as optional) because it was a genuine unmitigated case"
  - "Commit 4bcd8a4 cited in plan does not exist in repo; used actual commit a6159c4 (fix(23-01): mkdir before tee) instead"

patterns-established:
  - "Pattern 5: unguarded grep -oE in pipeline command substitution aborts under pipefail before downstream guard is reached"

# Metrics
duration: ~45min
completed: 2026-04-28
---

# Phase 28 Plan 01: HARNESS-AUDIT-01 Summary

**5 macOS bash-strict-mode patterns codified in a 277-line howto; Pattern 5 (grep -oE session-id) confirmed real bug and fixed in bench/eval-qwen35-122b.sh; bench gate 7/7 PASS held**

## Performance

- **Duration:** ~45 min
- **Started:** 2026-04-28
- **Completed:** 2026-04-28
- **Tasks:** 3
- **Files modified:** 2 (1 new howto + 1 harness fix)

## Accomplishments

- Audited `bench/eval-qwen35-122b.sh` end-to-end: enumerated all `dotnet run`, `grep -c*`, `tee`, and `seq` call sites against the 4 known patterns checklist
- Discovered Pattern 5 (unguarded `grep -oE` session-id pipeline in `run_multiturn` line 378): real strict-mode bug, not false alarm; fixed and committed separately as `fix(28-01)`
- Wrote `documentation/howto/macos-bash-strict-mode-patterns.md` with 5 Pattern sections (each with Symptom, Root cause, Canonical fix, commit reference) plus Summary Table and Common Rule
- Bench gate 7/7 PASS confirmed after howto + fix commits

## Audit Findings (Task 1)

All call sites in `bench/eval-qwen35-122b.sh`:

| Line range | Call type | Classification |
|------------|-----------|---------------|
| 274-277 | `dotnet run` in `run_refactor` | known-mitigated (set +e/set -e) |
| 329-332 | `dotnet run` in `run_langcoverage` | known-mitigated |
| 372-375 | `dotnet run` turn 1 in `run_multiturn` | known-mitigated |
| 394-397 | `dotnet run` turns 2..N in `run_multiturn` | known-mitigated |
| 434-436 | `dotnet run` in `run_schema_rate` | known-mitigated |
| 297-300 | `grep -cE` multi-file to awk in `run_refactor` | known-mitigated (`|| true` inside subshell) |
| 402 | `grep -c` in `run_multiturn` | known-mitigated (`|| true`) |
| 404 | `grep -cE` in `run_multiturn` | known-mitigated (`|| true`) |
| 441 | `grep -c` in `run_schema_rate` | known-mitigated (`|| true`) |
| 452 | `grep -l` in `run_schema_rate` cross-check | known-mitigated (`|| true`) |
| **378** | **`grep -oE` session-id in `run_multiturn`** | **NEW BUG — Pattern 5** |
| 390 | `seq 2 "$n"` in `run_multiturn` | known-mitigated (guard `[ "$n" -ge 2 ]`) |
| 364 | `seq 1 "$trials"` | safe (hardcoded start ≤ end) |
| 426 | `seq 1 50` | safe (hardcoded) |
| 159 | `seq 1 10` | safe (hardcoded) |
| 267, 305, 323, 369, 433, 468 | `tee` / redirects | known-mitigated (`mkdir -p` before each) |

**Verdict:** No 6th pattern found. Pattern 5 was the only unmitigated case. `git diff bench/eval-qwen35-122b.sh` after fix shows exactly the one-line change at line 378.

**Pattern 5 bug analysis:** `run_multiturn` line 378 (`sid=$(grep -oE "Session:..." "$stderr_file" | head -1 | awk '{print $2}')`) — `grep -oE` exits 1 when `$stderr_file` contains no "Session: ..." line (e.g., blueCode crashed before emitting the session-ID log). Under `pipefail`, the pipeline exit code propagates through the command substitution and aborts the script before reaching the `if [ -z "$sid" ]` guard. The guard was designed to handle the empty-sid case via `continue`, but is unreachable when strict mode aborts first.

Fix: added `2>/dev/null` and `|| true` at the end of the pipeline.

## Howto Contents (Task 2)

**File:** `documentation/howto/macos-bash-strict-mode-patterns.md`
**Line count:** 277
**Pattern sections:** 5 (Patterns 1-5)
**Commit refs cited:** `4a2c3c6`, `9f0b43b`, `eab900c`, `a6159c4`, `9f8e06e`, `94d905c`
**bench/eval-qwen35-122b.sh references:** 7 occurrences

Each Pattern section covers:
- Symptom (observable failure)
- Root cause (why the strict-mode rule triggers)
- Canonical fix (code block, verbatim)
- Reference (file line numbers + commit hash)

**Pattern coverage in howto:**
- Pattern 1: `set -e` / dotnet non-zero exit (lines 274-277, commit `4a2c3c6`)
- Pattern 2: `grep -c` pipe to `awk` zero-match pipefail (lines 297-300, commit `9f0b43b`)
- Pattern 3: `mkdir -p` before `tee` / redirect (line 467, commit `a6159c4`)
- Pattern 4: `grep -cE` zero-match command substitution (lines 438-441, commit `9f8e06e`)
- Pattern 5: `grep -oE` zero-match pipeline (line 378, commit `94d905c`)

## Bench Gate (Task 3)

```
===== GATE PASS (7/7) =====
  PASS T6_122b    steps=4/5 exit=0
  PASS W1_122b    steps=3/3 exit=0
  PASS W2_122b    steps=3/3 exit=0
  PASS T1_122b    steps=1/3 exit=0
  PASS T5_122b    steps=3/4 exit=0
  PASS B2_122b    steps=2/3 exit=0
  PASS MT_122b    steps=2/4 exit=0
```

Gate exit code: 0.

## Task Commits

1. **Task 1: Pattern 5 fix** - `94d905c` (fix)
2. **Task 2: Write howto** - `280677a` (docs)
3. **Task 3: Gate verification** - (no commit; read-only)

**Plan metadata:** (this SUMMARY.md + PLAN.md)

## Files Created/Modified

- `/Users/ohama/projs/blueCode/documentation/howto/macos-bash-strict-mode-patterns.md` — 277-line howto with 5 macOS bash-strict-mode patterns
- `/Users/ohama/projs/blueCode/bench/eval-qwen35-122b.sh` — Pattern 5 fix: `grep -oE` session-id guard at line 378

## Decisions Made

- **Pattern 5 classified as real bug:** Verified empirically that `bash -c 'set -euo pipefail; sid=$(grep -oE "Pattern" /dev/null | head -1 | awk ...')` exits 1 (aborts). The `if [ -z "$sid" ]` guard was unreachable under strict mode.
- **Commit `4bcd8a4` cited in plan does not exist:** Plan listed it as the 23-01 mkdir-before-tee commit, but the actual commit is `a6159c4`. Used the real commit hash in the howto.
- **5th pattern included (not suppressed as optional):** RESEARCH.md Q7 listed Pattern 5 as "Optional". Since the audit confirmed it was a real unmitigated bug (not just documentation of an already-handled edge case), it was promoted to a full required Pattern section.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Pattern 5 confirmed real and fixed**

- **Found during:** Task 1 (audit pass)
- **Issue:** `run_multiturn` line 378 `grep -oE` pipeline lacked `|| true` guard; under pipefail, zero-match case aborts script before `if [ -z "$sid" ]` safe-continue path
- **Fix:** Added `2>/dev/null` + `|| true` at end of pipeline; added explanatory comment
- **Files modified:** `bench/eval-qwen35-122b.sh`
- **Verification:** `bash -c 'set -euo pipefail; ...'` test confirmed fix; gate 7/7 PASS
- **Committed in:** `94d905c` (separate `fix(28-01)` commit before howto commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - Bug)
**Impact on plan:** Bug fix was a prerequisite for documenting Pattern 5 in the howto. No scope creep; the plan explicitly anticipated a 5th pattern fix.

## HARNESS-01 Requirement Status

**SATISFIED.** Per REQUIREMENTS.md HARNESS-01 validation criteria:
- `documentation/howto/macos-bash-strict-mode-patterns.md` exists with ≥4 sections: YES (5 sections)
- Each section has symptom / root cause / canonical fix / commit ref: YES
- File contains `set +e`: YES
- File contains `bench/eval-qwen35-122b.sh` references: YES (7 occurrences)
- Commit hashes `9f8e06e` cited: YES

## Issues Encountered

- Plan referenced commit hash `4bcd8a4` for the 23-01 mkdir-before-tee fix, but this hash does not exist in the repository. The actual commit is `a6159c4`. Used the correct hash in the howto.

## Next Phase Readiness

- `bench/eval-qwen35-122b.sh` is fully guarded against known macOS bash-strict-mode pitfalls
- howto is in place for Phase 28 plan 03 author (adding `run_fs_idiomatic` function) — new run_* function authors can check the howto checklist before committing
- Bench gate 7/7 PASS; no regressions introduced

---
*Phase: 28-f-coding-quality-measurement-harness-audit*
*Completed: 2026-04-28*
