---
phase: 28-f-coding-quality-measurement-harness-audit
plan: 03
subsystem: eval-harness
tags: [bash, eval-harness, fs-idiomatic, f#, strict-mode, FS-EVAL-02]

# Dependency graph
requires:
  - phase: 28-f-coding-quality-measurement-harness-audit/28-02
    provides: bench/fixtures/fs_idiomatic/*.{task.md,fs} — 3 fixture pairs created
provides:
  - run_fs_idiomatic() function in bench/eval-qwen35-122b.sh (agent-loop mode, no --plan)
  - --fs-idiomatic dispatch case and usage help text
  - Per-fixture transcript + diff + meta artifacts under bench/runs/qwen35-eval-<ts>/
  - FS-EVAL-02 measurement infrastructure satisfied
affects: [28-04-formal-scoring, future-harness-extensions]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - run_fs_idiomatic mirrors run_refactor() canonical template
    - Reminder-only kickstart (NOT auto) — user responsible for KV-cache hygiene
    - Per-fixture git checkout restore (idempotent across runs)
    - All 4 macOS bash-strict-mode patterns applied

key-files:
  created: []
  modified:
    - bench/eval-qwen35-122b.sh

key-decisions:
  - "Reminder-only kickstart (NOT auto-kickstart) — canonical-evidence boundary: 28-04 owns formal pre-flight"
  - "NOT added to --full mode — keep --full representative of v2.1 baseline (Phase 28 is observational)"
  - "Canonical-evidence boundary preserved: smoke artifacts stay under bench/runs/ (gitignored); .planning/.../transcripts/ reserved for 28-04"

patterns-established:
  - "run_fs_idiomatic: per-fixture loop with git checkout between iterations (analogous to bench/run.sh EXIT trap)"
  - "Pattern 1 (set-e/dotnet-exit) applied at line 365"
  - "Pattern 2/4 (grep-c zero-match guard) applied at line 381"
  - "Pattern 3 (mkdir-before-tee) applied at line 329"

# Metrics
duration: ~15min
completed: 2026-04-29
---

# Phase 28 Plan 03: --fs-idiomatic Harness Mode Summary

**`run_fs_idiomatic()` added to bench/eval-qwen35-122b.sh — per-fixture agent-loop runner with transcript/diff/meta output, smoke-tested 3/3 PASS, bench gate 7/7 PASS, canonical-evidence boundary preserved**

## Performance

- **Duration:** ~15 min
- **Completed:** 2026-04-29
- **Tasks:** 2 of 2
- **Files modified:** 1 (bench/eval-qwen35-122b.sh: +84/-5 lines)

## Accomplishments

- Added `run_fs_idiomatic()` function (lines 318-391) mirroring `run_refactor()` canonical template
- Reminder-only kickstart block — harness prints `REMINDER:` echo rather than auto-kickstarting (canonical-evidence boundary)
- Per-fixture loop over `bench/fixtures/fs_idiomatic/*.task.md` with `git checkout` restore between iterations
- All 4 macOS bash-strict-mode patterns applied inside handler body
- Case dispatch entry `--fs-idiomatic) run_fs_idiomatic ;;` inserted between `--refactor` and `--langcoverage`
- Usage heredoc and top-of-file comment updated
- Smoke test: all 3 fixtures ran exit=0; bench gate 7/7 PASS confirmed post-commit

## Task Commits

1. **Task 1+2: Add run_fs_idiomatic() + smoke-test** - `b7611a8` (feat)

**Plan metadata:** (this commit — docs)

## Files Modified

- `bench/eval-qwen35-122b.sh` — New `run_fs_idiomatic()` function (lines 318-391), updated top comment (lines 5-21), updated `usage()` heredoc (line 623), new case dispatch entry (line 642)

## Smoke-Run Output

Run dir: `bench/runs/qwen35-eval-20260429-140412/`

Meta files (verbatim):
```
label=fs_idiomatic_dupatternmatch model=122b exit=0 elapsed=16s steps=1
label=fs_idiomatic_optionhandling model=122b exit=0 elapsed=23s steps=1
label=fs_idiomatic_pipeline model=122b exit=0 elapsed=16s steps=1
```

## Bench Gate

```
===== GATE PASS (7/7) =====
```

## Strict-Mode Pattern Application Table

| Pattern | Description | Location in run_fs_idiomatic |
|---------|-------------|------------------------------|
| Pattern 1 | `set +e` / `set -e` around dotnet invocation (blueCode exits 1 on MaxLoopsExceeded — data, not failure) | Lines 365-369 |
| Pattern 2/4 | `grep -cE ... \|\| true` + `${step_count:-0}` default for zero-match guard | Lines 380-382 |
| Pattern 3 | `mkdir -p "$LOG_DIR"` as first line of handler | Line 329 |
| Pattern 4 (git diff) | `git diff -- "$fs_file" > "$diff_out" 2>/dev/null \|\| true` | Line 377 |
| Pattern 4 (git checkout) | `git checkout -- "$fs_file" 2>/dev/null \|\| true` (per-fixture restore) | Line 352 |
| Pattern 4 (final cleanup) | `git checkout -- "$fixture_dir"/*.fs 2>/dev/null \|\| true` | Line 388 |

(Pattern 5 N/A — no `seq M N` countdown in this handler.)

## Architectural Invariants

| Check | Result |
|-------|--------|
| `grep -E "import mlx_lm" bench/eval-qwen35-122b.sh` | empty (HTTP-only preserved) |
| `git diff milestone-v2.3 HEAD -- src/` | empty |
| `git diff milestone-v2.3 HEAD -- bench/baseline.json` | empty |
| `git diff milestone-v2.3 HEAD -- bench/run.sh` | empty |
| `git diff bench/fixtures/fs_idiomatic/` post-smoke | empty (fixtures restored) |
| `.planning/phases/28-.../transcripts/` | does not exist (canonical-evidence boundary preserved) |

## Decisions Made

- **Reminder-only kickstart:** Handler prints a `REMINDER:` echo block but does NOT auto-kickstart. Rationale: 28-04 owns the formal kickstart pre-flight as part of its canonical-evidence producing run. Auto-kickstarting here would contaminate 28-04's fresh-state guarantee.
- **NOT added to `--full` mode:** `run_full()` stays representative of the v2.1 baseline per quality_gate decision. `--fs-idiomatic` is invoked separately by 28-04.
- **Canonical-evidence boundary:** Smoke artifacts land under `bench/runs/qwen35-eval-*/` (gitignored) and are NOT copied to `.planning/phases/28-.../transcripts/`. That directory is reserved for 28-04's kickstart-preflight'd run.

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

None.

## FS-EVAL-02 Requirement

**Status: SATISFIED**

`FS-EVAL-02` requires a reproducible measurement harness for F# idiomatic-pattern fixtures. `run_fs_idiomatic()` provides:
- Per-fixture agent-loop invocation (no `--plan`)
- Transcript capture for manual rubric scoring
- Diff capture for automated change detection
- Meta capture for structured result aggregation
- Idempotent git-checkout restore between runs

## Note for 28-04 Executor

Run is observational only — 28-04 does its own kickstart-preflight'd run and archives those transcripts as scoring evidence. The 28-03 smoke run artifacts under `bench/runs/qwen35-eval-20260429-140412/` are gitignored and should be treated as throwaway confirmation only. Do NOT copy them to `.planning/phases/28-f-coding-quality-measurement-harness-audit/transcripts/`.

28-04 pre-flight: `launchctl kickstart -k gui/$(id -u)/com.ohama.qwen122b`, wait for `/v1/models` to respond, then invoke `bash bench/eval-qwen35-122b.sh --fs-idiomatic`.

## Next Phase Readiness

- `bench/eval-qwen35-122b.sh --fs-idiomatic` is production-ready for 28-04 formal scoring run
- No blockers
- 28-04 should add `--fs-idiomatic` to its own kickstart pre-flight steps

---
*Phase: 28-f-coding-quality-measurement-harness-audit*
*Completed: 2026-04-29*
