---
phase: 19-qwen25-retirement
plan: 19-01
subsystem: infra
tags: [launchd, mlx_lm, qwen25, disk-reclaim, model-retirement]

# Dependency graph
requires:
  - phase: 18-drop-35b-eval
    provides: DROP-35B verdict confirming 122B alone is viable; 35B already unloaded
provides:
  - Physical retirement of Qwen 2.5 (32B / 72B) from disk and launchd
  - 85 GiB disk reclaim (qwen32b 17G + qwen72b 38G + qwen72b.3bit 30G)
  - Canonical post-retirement state: only qwen35b/ and qwen122b/ remain on disk
  - 19-01-RETIREMENT.md evidence log with pre/post snapshots and SC1 verification
affects:
  - 19-02 (Wave 2 code/bench/docs alignment — depends_on: [19-01] now satisfied)
  - Phase 16 (bench baseline shape settled; 16-03 fixtures can reference 122B-only canonical state)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Checkpoint:human-action pattern for destructive rm -rf / launchctl unload (user executes, Claude verifies)"
    - "data[1].id health check pattern for mlx_lm.server (not data[0] which is misleading HF fallback)"

key-files:
  created:
    - .planning/phases/19-qwen25-retirement/19-01-RETIREMENT.md
    - .planning/phases/19-qwen25-retirement/19-01-SUMMARY.md
  modified: []

key-decisions:
  - "85 GiB reclaimed (277 GiB → 192 GiB used) — PASS (threshold >= 50 GB)"
  - "data[0] health check is misleading HF fallback id (Qwen/Qwen2.5-Coder-32B); data[1] is the truth (/Users/ohama/llm-system/models/qwen122b)"
  - "qwen35b/ preserved on disk as cold rollback asset per ROADMAP Decision A; plist kept but NOT loaded"
  - "qwen32b/, qwen72b/, qwen72b.3bit/ all deleted; qwen32b.plist + qwen72b.plist deleted"
  - "122B service (PID 44880, port 8001) unaffected throughout retirement"

patterns-established:
  - "Verification health check: use data[1].id not data[0].id for mlx_lm.server model confirmation"
  - "Pre/Post snapshot document pattern: RETIREMENT.md captures exact verbatim outputs with reclaim arithmetic and SC1 checkmarks"

# Metrics
duration: ~30min (including user-executed Task 3 checkpoint)
completed: 2026-04-27
---

# Phase 19 Plan 01: Retire Qwen 2.5 Disk Reclamation Summary

**Physically retired Qwen 2.5 32B and 72B from disk and launchd (3 model dirs + 2 plists deleted, 85 GiB reclaimed), leaving qwen35b/ as cold rollback and qwen122b/ as sole production model**

## Performance

- **Duration:** ~30 min (Tasks 1-2 automated; Task 3 user checkpoint; Task 4 automated)
- **Completed:** 2026-04-27
- **Tasks:** 4 (Tasks 1-2 in Wave 1 pre-checkpoint; Task 4 post-checkpoint continuation)
- **Files modified:** 1 (19-01-RETIREMENT.md, created in Task 1 and extended through Task 4)

## Accomplishments

- Captured verbatim pre-retirement state (df, du, ls, launchctl, curl) into 19-01-RETIREMENT.md
- Pre-flight safety check confirmed only 122B was loaded before destructive operations
- User executed Task 3 retirement block (launchctl unload + rm -rf + plist delete)
- Post-retirement verified: 85 GiB reclaimed, SC1 all three criteria PASS, 122B service unaffected
- Documented verification-script gotcha (data[0] HF fallback vs data[1] truth) in RETIREMENT.md

## Task Commits

1. **Task 1: Capture pre-retirement snapshot** - (docs)
2. **Task 2: Pre-flight safety check** - (docs)
3. **Task 3: User executes retirement commands** - (checkpoint:human-action — no commit)
4. **Task 4: Verify post-retirement state and reclaim metrics** - (docs)

**Plan metadata:** (this commit — docs(19-01): complete retire-qwen25-disk-reclamation plan)

## Files Created/Modified

- `.planning/phases/19-qwen25-retirement/19-01-RETIREMENT.md` - Pre/post snapshot, reclaim arithmetic, remaining-file map, SC1 verification, verification-script gotcha addendum

## Decisions Made

- **85 GiB reclaimed** — Pre-retirement Used 277 GiB, post-retirement 192 GiB. Delta 85 GiB (qwen32b 17G + qwen72b 38G + qwen72b.3bit 30G). Threshold >= 50 GB: PASS.
- **data[0] HF fallback gotcha** — mlx_lm.server returns hardcoded `Qwen/Qwen2.5-Coder-32B` in data[0] regardless of loaded model. data[1] returns the actual local path. Verification scripts must use data[1]. Mirrors the `tryParseModelId` path-preference heuristic in QwenHttpClient.fs (CLAUDE.md § Key Seams).
- **qwen35b/ preserved** — Per ROADMAP Decision A, remains as cold rollback asset. Plist (com.ohama.qwen35b.plist) retained but service not loaded.
- **qwen72b.3bit/ deleted** — Old 3-bit experiment variant. SC1 requires models/ contains ONLY qwen35b and qwen122b. Included in retirement scope per plan decision #1.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Used data[1] instead of data[0] for health check**

- **Found during:** Task 4 (post-retirement verification)
- **Issue:** Plan's Task 4 step 1 specified `d['data'][0]['id']` which returns misleading HF fallback id `Qwen/Qwen2.5-Coder-32B` — not the actual loaded model
- **Fix:** Changed health check to use `d['data'][1]['id']` which returns `/Users/ohama/llm-system/models/qwen122b` (the truth). Also added verification-script gotcha addendum to RETIREMENT.md
- **Files modified:** 19-01-RETIREMENT.md (post-retirement §5 capture)
- **Verification:** data[1].id confirmed as `/Users/ohama/llm-system/models/qwen122b`
- **Committed in:** (Task 4 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 — bug in verification script spec)
**Impact on plan:** Necessary correction. The plan's data[0] spec would have produced a misleading health-check capture. data[1] is the truth per CLAUDE.md Key Seams documentation.

## Issues Encountered

None beyond the data[0] vs data[1] verification script correction noted above.

## Next Phase Readiness

- **19-02 (Wave 2) is READY** — depends_on: [19-01] satisfied. Physical retirement is complete. The canonical post-retirement state (qwen35b/ + qwen122b/ only; launchd 122B only) is verified and documented in 19-01-RETIREMENT.md.
- **19-02 scope:** Code/bench/docs alignment to describe single-model 122B world (Argu cleanup, `--with-35b` flag, `tryParseModelId` retirement guard, bench/run.sh rewrite absorbing scripts/bench-122b-only.sh, baseline halve, CLAUDE.md update).
- **No blockers** — 122B service alive (PID 44880, port 8001), all verifications PASS.

---
*Phase: 19-qwen25-retirement*
*Completed: 2026-04-27*
