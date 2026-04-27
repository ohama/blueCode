---
phase: 18-single-model-eval
plan: "01"
subsystem: infra
tags: [mlx_lm, qwen122b, launchctl, memory-profiling, single-model]

requires:
  - phase: 17-qwen-3-5-evaluation
    provides: "Dual-loaded 35B+122B baseline (62.35 GB combined RSS, Phase 17 canonical state)"

provides:
  - "Empirical pre/post-unload memory profile: PhysMem unused +19.42 GB, Compressor 454 MB, 122B RSS stable at 45.42 GB"
  - "35B service cleanly unloaded via launchctl; system in single-122B state"
  - "122B health verified post-unload: thinking-mode smoke PASS + blueCode JSON-schema smoke PASS"
  - "ROADMAP §SC4 memory criteria evaluated: both PASS"
  - "18-02 bench preconditions confirmed READY"

affects:
  - 18-02-single-model-bench
  - 18-03-decision-matrix

tech-stack:
  added: []
  patterns:
    - "launchctl unload (not kill) for clean service removal — KeepAlive does not auto-restart"
    - "30s settle wait after mmap-backed process exit before measuring page reclaim"
    - "top -l 1 + vm_stat + ps -o pid,rss for memory snapshot trifecta"

key-files:
  created:
    - .planning/phases/18-single-model-eval/18-01-MEMORY-PROFILE.md
  modified: []

key-decisions:
  - "PhysMem unused increased by +19.42 GB post-35B-unload (well above 5 GB SC4 threshold) — freed pages returned to pool, not claimed by 122B"
  - "122B RSS held at exactly 45.42 GB post-unload — MoE expert routing is prompt-driven, not memory-availability-driven"
  - "Compressor barely changed (463 → 454 MB) — 35B pages were file-backed (mmap), returned directly to free pool"
  - "18-02 readiness: READY — test bed clean, proceed to single-122B bench"

patterns-established:
  - "Pre/post unload memory profiling: snapshot both states with top + vm_stat + ps; compute explicit delta column"
  - "SC4 threshold evaluation: mechanical PASS/FAIL in §5.1 table, lifted directly by 18-03 decision matrix"

duration: 10min
completed: "2026-04-27"
---

# Phase 18 Plan 01: 35B Unload + Memory Profile Summary

**35B cleanly unloaded via launchctl; PhysMem unused freed +19.42 GB (4× SC4 threshold), 122B RSS stable at 45.42 GB, 122B health smokes PASS — single-model test bed ready for 18-02 bench**

## Performance

- **Duration:** ~10 min (including 30s settle wait + 2 health smokes)
- **Started:** 2026-04-27T03:00:00Z (continuation from Task 1 checkpoint at 2026-04-27T02:58:00Z)
- **Completed:** 2026-04-27T03:10:00Z
- **Tasks:** 5 (Tasks 1-5 complete; Task 2 was checkpoint — user ran launchctl unload)
- **Files modified:** 1

## Accomplishments

- 35B service unloaded CLEAN via `launchctl unload` — verified by launchctl/lsof/pgrep/curl triple-check
- Post-unload memory profile captured (+30s settle): PhysMem unused jumped from 1.58 GB to 21 GB (+19.42 GB)
- ROADMAP §SC4 memory criteria both PASS: PhysMem unused ≥ 5 GB (+19.42 GB), Compressor < 1 GB (454 MB)
- 122B RSS hypothesis CONFIRMED: RSS held at 45.42 GB post-unload (0 GB expansion into freed pages)
- 122B health verified: thinking-mode smoke PASS (1s, no `<think>` tokens) + blueCode `--model 72b` invocation PASS (7s, exit 0, clean single-step FinalAnswer)

## Task Commits

Each task was committed atomically:

1. **Task 1: Capture pre-unload memory baseline** - `7f15735` (chore)
2. **Tasks 2-5: 35B unload verify + post-snapshot + health smokes + observations** - `ff4b3ea` (chore)

**Plan metadata:** (this commit) (docs: complete plan)

## Files Created/Modified

- `.planning/phases/18-single-model-eval/18-01-MEMORY-PROFILE.md` — 162-line empirical record with 5 sections (§1 pre-unload table, §2 checkpoint outcome, §3 post-unload delta table, §4 health smokes, §5 observations + hypothesis verdicts)

## Decisions Made

- **PhysMem freed entirely to pool:** The 16.93 GB released by 35B's exit went to PhysMem unused (1.58 GB → 21 GB), not to 122B mmap. Compressor delta was only -9 MB because 35B used file-backed mmap pages (not anonymous compressed memory).
- **122B MoE RSS is prompt-driven:** RSS held at exactly 45.42 GB (same KB count pre and post). Expert activation pattern is stable regardless of available memory headroom.
- **18-02 is READY:** Both SC4 criteria pass with wide margin; 122B health is confirmed healthy. No blockers.

## Deviations from Plan

None — plan executed exactly as written. The checkpoint (Task 2) was handled by user as designed. All verification criteria passed on first attempt.

## Issues Encountered

None. 35B unload was clean (no bootout/bootstrap fallback required). 122B stayed fully responsive throughout. Both health smokes passed on first invocation.

## Memory Profile Summary (for 18-03 decision matrix)

| Metric                          | Pre-unload  | Post-unload | Delta      | SC4 Verdict |
|---------------------------------|-------------|-------------|------------|-------------|
| PhysMem used                    | 126 GB      | 106 GB      | -20 GB     | —           |
| PhysMem unused                  | 1.58 GB     | 21 GB       | +19.42 GB  | PASS (≥5GB) |
| Compressor                      | 463 MB      | 454 MB      | -9 MB      | PASS (<1GB) |
| 35B RSS                         | 16.93 GB    | (gone)      | -16.93 GB  | —           |
| 122B RSS                        | 45.42 GB    | 45.42 GB    | 0 GB       | CONFIRMED   |

## Next Phase Readiness

- 18-02 bench preconditions: READY. Port 8000 dead; port 8001 healthy; PhysMem has 21 GB headroom.
- 122B health is confirmed; `bench-122b-only.sh` may proceed without pre-flight.
- Reference: `18-01-MEMORY-PROFILE.md` §5.5 Disposition = READY.

---
*Phase: 18-single-model-eval*
*Completed: 2026-04-27*
