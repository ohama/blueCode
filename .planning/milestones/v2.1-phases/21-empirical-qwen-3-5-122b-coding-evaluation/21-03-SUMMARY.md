---
phase: 21-empirical-qwen-3-5-122b-coding-evaluation
plan: 03
subsystem: testing
tags: [bench, eval, fixtures, refactor, langcoverage, fsharp, python, typescript, dotnet]

# Dependency graph
requires:
  - phase: 21-01
    provides: eval harness scaffold, require_port_8001, LOG_DIR pattern, stub handlers
  - phase: 21-02
    provides: run_humaneval handler, set -e interaction patterns with dotnet exit codes
provides:
  - 7 fixture files (4 refactor_multifile + 3 standalone bug fixtures)
  - bench/run.sh EXIT trap extended to restore write-task fixtures
  - run_refactor() and run_langcoverage() handlers wired and executed live
  - CORR-EVAL-02 data: orphan_count=1 (partial refactor, 5-step limit hit)
  - CORR-EVAL-03 data: agent correctly diagnosed bug_binsearch.fs (PASS)
  - CORR-EVAL-04 data: agent correctly diagnosed Python + TypeScript bugs (PASS both)
affects: ["21-05 (scoring and verdict doc - reads refactor_orphan_count.txt and diagnose logs)"]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "set +e / set -e bracket around dotnet run invocations to capture exit_code while preserving set -euo pipefail for rest of harness"
    - "EXIT trap in bench/run.sh restores write-task fixtures only; diagnose-only fixtures excluded (B2 / bug_divide_zero.fs convention)"
    - "orphan_count captured before EXIT trap fires so 21-05 can score CORR-EVAL-02 from persisted file"

key-files:
  created:
    - bench/fixtures/refactor_multifile/Calculator.fs
    - bench/fixtures/refactor_multifile/Main.fs
    - bench/fixtures/refactor_multifile/Tests.fs
    - bench/fixtures/refactor_multifile/README.md
    - bench/fixtures/bug_binsearch.fs
    - bench/fixtures/bug_python_typeerror.py
    - bench/fixtures/bug_typescript_async.ts
    - bench/runs/qwen35-eval-20260428-093852/refactor_multifile_diff.txt
    - bench/runs/qwen35-eval-20260428-093852/refactor_orphan_count.txt
    - bench/runs/qwen35-eval-20260428-093852/refactor_multifile.meta
    - bench/runs/qwen35-eval-20260428-093933/bug_python_typeerror_diagnose.log
    - bench/runs/qwen35-eval-20260428-093933/bug_typescript_async_diagnose.log
    - bench/runs/qwen35-eval-20260428-093933/bug_binsearch_diagnose.log
  modified:
    - bench/run.sh (line 18 only — EXIT trap extended)
    - bench/eval-qwen35-122b.sh (run_refactor + run_langcoverage stubs replaced; set -e fix)

key-decisions:
  - "5-step loop limit caused agent to partially complete refactor (renamed add3->sum3 in Calculator.fs but couldn't update Main.fs and Tests.fs). CORR-EVAL-02 FAIL, orphan_count=1. This is DATA, not a harness failure — 21-05 applies 0/5 pts accordingly."
  - "set -euo pipefail + dotnet exit 1 (MaxLoopsExceeded): needed set +e / set -e bracket around dotnet run in both run_refactor() and run_langcoverage(). Same class of issue as 21-02 had with evalplus subprocesses."
  - "Agent correctly read README.md in step 1, then read all 3 fixture files (steps 2-4), then started editing Calculator.fs on step 5 — exhausted budget before touching Main.fs/Tests.fs. The task requires ~7 steps minimum for a thorough multi-file refactor."
  - "All 3 diagnose tasks completed in 2 steps (read + final answer), exit=0. CORR-EVAL-03 and CORR-EVAL-04 qualitatively PASS — agent named bugs precisely and provided correct triggering inputs."
  - "EXIT trap verified working: after bench/run.sh --gate completed, git diff bench/fixtures/refactor_multifile/ and git diff bench/fixtures/bug_binsearch.fs both empty."

patterns-established:
  - "write-task fixtures in EXIT trap; diagnose-only fixtures excluded"
  - "orphan_count.txt persisted inside run_refactor() before harness exits, since gate's EXIT trap will restore files later"

# Metrics
duration: 25min
completed: 2026-04-28
---

# Phase 21 Plan 03: Fixtures and Refactor/Langcoverage Harness Summary

**CORR-EVAL-02 data captured (orphan_count=1, FAIL due to 5-step limit); CORR-EVAL-03/04 PASS (agent correctly diagnosed all 3 bug fixtures in 2 steps each); bench gate 7/7 PASS with EXIT trap verified**

## Performance

- **Duration:** ~25 min total (17s refactor run + 25s langcoverage (3x) + 2 min gate + overhead)
- **Started:** 2026-04-28T09:37:21Z (first refactor attempt)
- **Completed:** 2026-04-28T09:42:00Z (after gate)
- **Tasks:** 3 (Task 1 already committed; Task 2 + Task 3 executed in this session)
- **Files modified:** 2 (bench/run.sh, bench/eval-qwen35-122b.sh) + 7 new fixture files

## Accomplishments

- 7 fixture files created: 4 in `bench/fixtures/refactor_multifile/` + 3 standalone bug fixtures
- `bench/run.sh:18` EXIT trap extended to restore `bug_binsearch.fs` + all 3 refactor_multifile .fs files
- `run_refactor()` and `run_langcoverage()` wired live; agent transcripts captured in `bench/runs/qwen35-eval-*/`
- Gate passes 7/7 after all eval runs; EXIT trap confirmed restoring fixtures

## Refactor Outcome (CORR-EVAL-02)

**Verdict: CORR-EVAL-02 FAIL — orphan_add_refs=1**

Run directory: `bench/runs/qwen35-eval-20260428-093852/`

Agent behavior (5 steps total):
1. read_file `README.md` — identified task correctly
2. read_file `Calculator.fs` — read source
3. read_file `Main.fs` — read source
4. read_file `Tests.fs` — read source
5. edit_file `Calculator.fs` — renamed `add3` → `sum3` only (partial refactor)

The agent correctly understood the multi-file scope but exhausted the 5-step budget after completing only Calculator.fs (partial: renamed `add3` but `add` still present). It never reached Main.fs or Tests.fs.

Post-run file states (captured before EXIT trap):
- `Calculator.fs`: `add3` renamed to `sum3` but `let add` remained (orphan)
- `Main.fs`: unchanged (still calls `add3`, `add`)
- `Tests.fs`: unchanged (still calls `add3`, `add`)

orphan_count=1 written to `refactor_orphan_count.txt` for 21-05 §2.4 scoring (0/5 pts).

Note: The agent THOUGHT it was going to "rename `add3` to `sum3` in Calculator.fs" and "update all references to `add3` to `sum3` in Main.fs and Tests.fs" — but in step 5 it only did Calculator.fs and ran out of steps. The task as written requires a minimum of ~7 steps for a thorough refactor. This is meaningful data for the eval doc §5 (coding quality / step budget).

## Diagnose Outcomes (CORR-EVAL-03 + CORR-EVAL-04)

Run directory: `bench/runs/qwen35-eval-20260428-093933/`

All 3 tasks completed in 2 steps (read_file + final) with exit=0 (~8-9s each).

### Python TypeError (CORR-EVAL-04a) — QUALITATIVE PASS
Agent correctly identified: `parse_age` silently returns `None` for invalid inputs instead of raising `ValueError`, causing `average_ages` to crash with `TypeError` when `sum()` encounters `None`. Triggering input: `['25', 'not_a_number', '30']`. Precise and complete.

### TypeScript Missing Await (CORR-EVAL-04b) — QUALITATIVE PASS
Agent correctly identified: `fetchAllUsers` returns unresolved Promises instead of awaiting `Promise.all(promises)`, causing callers to receive `Promise<User>[]` instead of `User[]`. Triggering input: any non-empty array of numbers (e.g., `[1, 2, 3]`). Precise and complete.

### F# BinSearch Off-by-One (CORR-EVAL-03) — QUALITATIVE PASS
Agent correctly identified: `hi <- mid` instead of `hi <- mid - 1` causes infinite loop when `lo == mid` (search window doesn't shrink). Triggering input: `[|1; 3; 5|]` with target `4`. Matches exactly the docstring explanation in the fixture. Precise and complete.

## Wall-Clock per Task

| Task | Description | Time |
|------|-------------|------|
| refactor_multifile | Full agent loop, 5 steps | 17s |
| bug_python_typeerror_diagnose | 2-step diagnose | 8s |
| bug_typescript_async_diagnose | 2-step diagnose | 8s |
| bug_binsearch_diagnose | 2-step diagnose | 9s |
| bench gate | --gate (7 invocations) | ~120s |

## EXIT Trap Verification

After `bench/run.sh --gate` completed:
- `git diff bench/fixtures/refactor_multifile/` — empty (Calculator.fs restored)
- `git diff bench/fixtures/bug_binsearch.fs` — empty

EXIT trap working correctly. The `|| true` suffix ensures trap fires even if `git checkout --` returns non-zero (e.g., file not modified).

## Bench Gate Status

```
GATE PASS (7/7)
  PASS T6_122b    steps=5/5 exit=0
  PASS W1_122b    steps=3/3 exit=0
  PASS W2_122b    steps=3/3 exit=0
  PASS T1_122b    steps=1/3 exit=0
  PASS T5_122b    steps=3/4 exit=0
  PASS B2_122b    steps=2/3 exit=0
  PASS MT_122b    steps=2/4 exit=0
```

## Task Commits

1. **Task 1: Add 7 fixture files** - (chore) — already committed before this session
2. **Task 2: Extend EXIT trap** - (chore)
3. **Task 3: Wire run_refactor + run_langcoverage handlers** - (chore)
4. **Fix: set -e interaction with dotnet non-zero exit** - (fix)
5. **Plan metadata** - (this commit)

## Files Created/Modified

- `bench/fixtures/refactor_multifile/Calculator.fs` — F# module with `add`/`add3` (target of rename refactor)
- `bench/fixtures/refactor_multifile/Main.fs` — Entry point calling `Calculator.add`/`add3`
- `bench/fixtures/refactor_multifile/Tests.fs` — Tests for `Calculator.add`/`add3`
- `bench/fixtures/refactor_multifile/README.md` — Task statement: rename `add` → `sum` everywhere
- `bench/fixtures/bug_binsearch.fs` — F# binary search with `hi <- mid` off-by-one (infinite loop)
- `bench/fixtures/bug_python_typeerror.py` — Python `parse_age` returning None instead of raising
- `bench/fixtures/bug_typescript_async.ts` — TypeScript missing `await Promise.all(promises)`
- `bench/run.sh` — Line 18 only: EXIT trap extended with 4 new fixture paths
- `bench/eval-qwen35-122b.sh` — `run_refactor()` + `run_langcoverage()` stubs replaced with full implementations; `set +e/set -e` brackets around `dotnet run`

## Decisions Made

1. **set +e / set -e bracket around dotnet run:** `blueCode` exits 1 on `MaxLoopsExceeded`. Under `set -euo pipefail`, this aborted `run_refactor()` before the orphan check and CORR-EVAL-02 verdict could fire. Fix: `set +e` before and `set -e` after the `dotnet run` invocation in both `run_refactor()` and `run_langcoverage()`. Same pattern needed as 21-02's evalplus subprocess handling.

2. **CORR-EVAL-02 as data, not failure:** The 5-step limit is a blueCode constraint (PLAN-04). The agent correctly understood the task scope and started the refactor but ran out of budget. orphan_count=1 recorded for 21-05 §2.4. This informs the eval doc's discussion of step-budget vs. task complexity tradeoff.

3. **First refactor attempt aborted:** First run of `--refactor` silently exited code 1 (set -e), producing no orphan check output. Second run (after fix) captured full data. Only second run's LOG_DIR (`qwen35-eval-20260428-093852`) is authoritative.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] set -euo pipefail aborts run_refactor before CORR-EVAL-02 verdict**

- **Found during:** Task 3 live execution (first `--refactor` run)
- **Issue:** `blueCode` exits 1 when `MaxLoopsExceeded`. `set -euo pipefail` at script top caused `run_refactor()` to abort immediately after `dotnet run` returned, before orphan check, CORR-EVAL-02 verdict line, or `refactor_orphan_count.txt` were written. First run produced only `refactor_multifile_diff.txt` with the transcript but no scoring data.
- **Fix:** Added `set +e` before and `set -e` after `dotnet run` in both `run_refactor()` and `run_langcoverage()`. This preserves strict error handling for all other commands while allowing blueCode's non-zero exit to be captured as `exit_code` variable.
- **Files modified:** `bench/eval-qwen35-122b.sh`
- **Verification:** Second run produced `CORR-EVAL-02 FAIL:` verdict line + `refactor_orphan_count.txt` with correct count.
- **Committed in:** (fix commit, separate from handler implementation commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - bug in set -e interaction)
**Impact on plan:** Required one extra commit beyond the 3 planned task commits. First refactor run result discarded (no scoring data). Second run authoritative. No scope creep; all planned outputs produced.

## Issues Encountered

- `MaxLoopsExceeded` on refactor task: The multi-file refactor requires reading 4 files (README + 3 .fs) + writing 3 files = 7 steps minimum. The 5-step budget forces the agent to start editing on step 5 without being able to complete all files. This is notable: the agent correctly planned the approach and started correctly but the constraint prevented completion. Qualitative behavior good; quantitative score 0/5 pts (CORR-EVAL-02 FAIL).

## Next Phase Readiness

- 21-04 (`--multiturn`, `--schema-rate`, `--needle`, `--coldstart`, `--full`) can proceed; no blockers
- 21-05 (scoring doc) has all data it needs from this plan:
  - `bench/runs/qwen35-eval-20260428-093852/refactor_orphan_count.txt` → CORR-EVAL-02 score
  - `bench/runs/qwen35-eval-20260428-093933/*.log` → CORR-EVAL-03/04 qualitative data
- Key concern for eval doc (21-05): step-budget vs. task complexity. The refactor fixture is intentionally harder than W1/W2 (requires 7+ steps), which reveals a concrete blueCode limitation for multi-file tasks.

---
*Phase: 21-empirical-qwen-3-5-122b-coding-evaluation*
*Completed: 2026-04-28*
