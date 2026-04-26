---
phase: 13-bench-fixture-cleanup
plan: 01
subsystem: bench
tags: [bash, bench, fixture, trap, git-checkout, BENCH-06]

# Dependency graph
requires:
  - phase: 10-bench-formalization
    provides: bench/run.sh harness with W1/W2 write-task fixtures and heredoc-restore blocks
  - phase: 11-system-prompt-shrink
    provides: B2 diagnose fixture (bug_divide_zero.fs), confirmed gate PASS (8/8)
provides:
  - EXIT trap in bench/run.sh that auto-resets W1/W2 fixtures on every invocation
  - documentation/bench.md subsection explaining trap behavior and defense-in-depth
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "bash trap on EXIT for fixture cleanup: 'trap ... EXIT' with 2>/dev/null || true guard; single-quoted body; no exit N inside trap"
    - "Defense-in-depth: heredoc-restore (between-invocation) + trap (exit-time) for write-task fixtures"

key-files:
  created: []
  modified:
    - bench/run.sh
    - documentation/bench.md

key-decisions:
  - "Single combined commit for bench/run.sh + documentation/bench.md (mechanically coupled: script behavior + its documentation describe the same change)"
  - "Used feat commit type (not chore): trap adds new runtime behavior (auto-reset on exit) where none existed before"
  - "bug_divide_zero.fs excluded from trap: read-only B2 diagnose fixture, never mutated by any mode"
  - "Existing heredoc-restore blocks at lines 133-151 and 274-292 preserved unchanged: defense-in-depth is intentional"
  - "trap body single-quoted: conventional for EXIT traps so $VAR expansion deferred to fire time (no variables here, but follows convention)"

patterns-established:
  - "EXIT trap pattern: trap 'git checkout -- <files> 2>/dev/null || true' EXIT — reliable, bash 3.2 compatible, exit-code preserving"

# Metrics
duration: 4min
completed: 2026-04-26
---

# Phase 13 Plan 01: Bench Fixture Auto-Reset Summary

**Added EXIT trap to bench/run.sh that auto-resets W1/W2 fixtures (bug_lastchar.fs + bug_average.fs); documented in bench.md; SC1 verified empirically with `--gate` run (8/8 PASS); BENCH-06 closed.**

## Performance

- **Duration:** 4 min
- **Started:** 2026-04-26T13:03:53Z
- **Completed:** 2026-04-26T13:07:53Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Trap inserted at bench/run.sh line 18 (between `set -u` at line 14 and `cd` at line 19), single-quoted body, `2>/dev/null || true` guard, no `exit N` inside trap — exit code preservation is automatic.
- `--gate` ran 8 invocations (W1_32b + W2_32b mutated fixtures mid-run); trap fired on exit and `git status --short` showed no modification to either fixture.
- New `## Auto-Reset of Write Fixtures` subsection in documentation/bench.md (lines 60-79) explains trap behavior, all-mode firing, bug_divide_zero.fs exclusion, and defense-in-depth relationship with heredoc blocks.

## Task Commits

Each task was committed atomically (combined per plan, mechanically coupled):

1. **Task 1 + Task 2 (combined): Add EXIT trap + documentation** - `65309b8` (feat)

**Plan metadata:** pending (this SUMMARY.md)

## Files Created/Modified

- `bench/run.sh` - EXIT trap inserted at line 18; trap targets bug_lastchar.fs and bug_average.fs; bug_divide_zero.fs excluded; heredoc-restore blocks at lines 137-151 and 278-296 (shifted +4 from original 133-151 and 274-292) are byte-identical to pre-edit content.
- `documentation/bench.md` - New `## Auto-Reset of Write Fixtures` subsection inserted between `## Fixture Naming Convention` and `## Prompt Design Guidance` (lines 60-79 post-edit).

## SC Verification Results

**SC1 — `--gate` clean:**
```
GATE PASS (8/8) — exit=0
git status --short bench/fixtures/bug_lastchar.fs bench/fixtures/bug_average.fs
(no output — both fixtures clean after trap fired on gate() exit)
```

**SC2 — `--canary` and `--b2` clean:**
```
canary-exit=0; status-after-canary: clean (no W1/W2 output)
b2-exit=0; status-after-b2: clean (no W1/W2 output)
```
Trap fires as a no-op for these modes since neither mutates W1/W2.

**SC3 — trap targets W1/W2 only:**
```
18:trap 'git checkout -- bench/fixtures/bug_lastchar.fs bench/fixtures/bug_average.fs 2>/dev/null || true' EXIT
(no bug_divide_zero.fs in trap line)
grep -c "bug_divide_zero" bench/run.sh = 6 (unchanged: b2_mode, gate x2, phase_diagnose x2, comment)
```

**SC4 — bench.md searchable:**
```
grep -i "trap|auto-reset|cleanup" documentation/bench.md
-> ## Auto-Reset of Write Fixtures
-> `bench/run.sh` installs a bash `trap` on `EXIT`...
-> The trap fires for every mode...
-> ...the `git checkout` is a harmless no-op. The trap deliberately
-> the trap handles exit-time cleanup.
```

**Additional checks:**
- `bash -n bench/run.sh` exits 0 (syntax clean).
- `grep -c "set -e" bench/run.sh` = 0 (set -e not added).
- `git diff --stat src/ tests/` produces no output (Phase 13 invariant holds).
- `bash bench/run.sh --help` exits 0.

## Decisions Made

- Single combined commit for bench/run.sh + documentation/bench.md: the two edits are mechanically coupled — the trap line in run.sh and the subsection in bench.md describe the same change. CLAUDE.md commit protocol (atomic per task) permits this when the task units are inseparable.
- `feat` commit type (not `chore`): the trap adds new observable runtime behavior — auto-reset of write-task fixtures on exit — where no such behavior existed before. `chore` would under-represent the change.
- Exit code preservation: `trap '...' EXIT` with a body that does not call `exit N` lets bash preserve the original exit code automatically. The `git checkout ... 2>/dev/null || true` pattern never calls exit, so gate()'s `exit 0` / `exit 1` flows through unchanged.
- bug_divide_zero.fs excluded from trap: it is the B2 diagnose read-only fixture; no test mode ever writes to it. Including it would be a wasted syscall and a misleading signal that it is a write-task fixture.
- SC1 wall-clock: ~2 min (8 invocations × ~13-20s each + compare step). Run completed in ~2 min as expected.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Phase 13 is the final phase of v1.4. Both phases (12 and 13) are now complete. The v1.4 milestone (Test Hygiene + Bench Polish) is ready for `/gsd:complete-milestone` when the 2-week observation window is satisfactory.

- BENCH-06 closed: fixture drift is fully automated away.
- TST-01 closed (Phase 12): MockHelpers.fs shared helper in place.
- No blockers for v1.4 close.

---
*Phase: 13-bench-fixture-cleanup*
*Completed: 2026-04-26*
