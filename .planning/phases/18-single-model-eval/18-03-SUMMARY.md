---
phase: 18-single-model-eval
plan: "03"
subsystem: infra
tags: [decision-matrix, single-model, qwen122b, mlx_lm, memory-profiling, bench]

requires:
  - phase: 18-02
    provides: "122B-only bench results: 31/31 exit=0, T1/T2 median 3s, T6 4 steps, W1/W2 3 steps, B2 PASS, RSS 45.43 GB post-bench"
  - phase: 18-01
    provides: "35B unloaded; PhysMem unused +19.42 GB; Compressor 454 MB; 122B RSS 45.42 GB stable"

provides:
  - "documentation/single-model-eval.md — 265-line eval doc: decision matrix (5/5 PASS), verdict DROP-35B, per-test comparison vs Phase 17 dual-loaded, reversibility procedure, deferred follow-ups"
  - "Verdict: DROP-35B — all 5 ROADMAP §SC4 criteria PASS"
  - "STATE.md updated: Phase 18 ✓, New Decisions (18-03) 4 entries, Session Continuity updated"
  - "Phase 18 complete and shippable (SC1-SC5 all PASS)"

affects:
  - follow-up architectural phase (Router collapse, baseline halve, CLAUDE.md update)
  - 16-planning-wiring-bench (should run AFTER follow-up architectural phase)

tech-stack:
  added: []
  patterns:
    - "Decision matrix pattern: 5 mechanical PASS/FAIL criteria with explicit thresholds, conjunction determines verdict — no judgment required"
    - "Conditional checkpoint dispatch: verdict-driven (DROP-35B short-circuits; KEEP-DUAL/CONDITIONAL engages user action)"

key-files:
  created:
    - documentation/single-model-eval.md
  modified:
    - .planning/STATE.md

key-decisions:
  - "VERDICT: DROP-35B — 5/5 ROADMAP §SC4 criteria PASS. 122B alone is viable canonical configuration."
  - "Task 3 checkpoint short-circuited autonomously on DROP-35B verdict — system stays single-model"
  - "Architectural follow-ups (Router collapse, baseline halve, CLAUDE.md update, bench script promotion) deferred to follow-up phase per ROADMAP §SC5 — zero src/bench/CLAUDE.md changes in 18-03"
  - "122B RSS hypothesis CONFIRMED: 45.42 GB post-unload → 45.43 GB post-bench (+0.01 GB / negligible)"

patterns-established:
  - "Eval doc structure: Overview / Methodology / 18-01 memory profile / 18-02 bench results / Per-test comparison table / Decision matrix / Verdict / Reversibility / Conditional follow-ups / Phase disposition — reusable for future model swap evaluations"
  - "Tasks 1+2+3 committed as single atomic doc commit followed by separate STATE commit — minor deviation from plan's expected 3-task commit chain; acceptable since doc content was complete in one pass"

duration: 5min
completed: "2026-04-27"
---

# Phase 18 Plan 03: Decision Matrix + Evaluation Doc Summary

**Verdict DROP-35B: 5/5 ROADMAP §SC4 criteria PASS — 122B alone is viable canonical configuration; architectural follow-ups deferred to follow-up phase per §SC5**

## Performance

- **Duration:** ~5 min (doc synthesis + verification; data lifted directly from 18-01 + 18-02)
- **Started:** 2026-04-27T04:30:01Z
- **Completed:** 2026-04-27T04:35:03Z
- **Tasks:** 4 (Tasks 1-4 complete; Task 3 short-circuited on DROP-35B verdict)
- **Files modified:** 2

## Accomplishments

- Applied 5 ROADMAP §SC4 criteria mechanically: all 5 PASS → verdict DROP-35B (no judgment invoked)
- Wrote `documentation/single-model-eval.md` (265 lines, all 7 required sections present)
- Task 3 conditional checkpoint short-circuited autonomously per DROP-35B dispatch
- Enumerated 5 architectural follow-ups (all DEFERRED per ROADMAP §SC5 — zero src/bench changes)
- Updated `.planning/STATE.md` with Phase 18 ✓ and 4 New Decisions (18-03) entries

## Task Commits

Each task was committed atomically:

1. **Tasks 1+2+3: single-model eval doc** - `da7c184` (docs) — full doc written in one pass; includes decision matrix, verdict, per-test comparison, reversibility, Task 3 SKIPPED outcome, conditional follow-ups, Phase 18 disposition
2. **Task 4: STATE.md update** - `7405756` (docs)

**Plan metadata:** (this commit) (docs: complete plan)

## Files Created/Modified

- `documentation/single-model-eval.md` — 265-line evaluation document with all 7 required sections
- `.planning/STATE.md` — Phase 18 ✓ marker, New Decisions (18-03) section with 4 entries, Session Continuity updated

## Decisions Made

- **DROP-35B verdict**: Mechanical application of 5 ROADMAP §SC4 criteria. T1/T2 median 3s ≤ 6s; T6/W1/W2/B2 step counts within baseline_max (T6=4/5, W1=3/3, W2=3/3, B2=2/3); B2 DivideByZeroException preserved (3 grep matches, near-identical wording); PhysMem unused +19.42 GB ≥ 5 GB; Compressor 454 MB < 1 GB. All 5 PASS → DROP-35B.
- **Deferred follow-ups**: Per ROADMAP §SC5, architectural changes are deferred. The 5 enumerated items (Router collapse, baseline halve, CLAUDE.md update, bench script promotion, Phase 16 re-key implications) are documented with file-list scope but NOT executed.
- **Task 3 short-circuit**: DROP-35B verdict dispatches to the autonomous branch — no user checkpoint engaged. System stays single-model (qwen122b only, port 8001). Verified: `launchctl list | grep ohama` shows only `com.ohama.qwen122b`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Minor] Doc written in one pass instead of two separate append operations**

- **Found during:** Task 1 execution
- **Issue:** Plan expected Task 1 to write the decision matrix/verdict portion, then Task 2 to "append" the reversibility + follow-ups sections. Since all content was known upfront, writing the complete doc in one pass was more efficient and reduced risk of inconsistency.
- **Fix:** Wrote the full 265-line doc in Task 1 commit, satisfying all Task 1 and Task 2 verification checks from the first commit. Task 2 and Task 3 outcome are included in the single doc commit (da7c184).
- **Files modified:** `documentation/single-model-eval.md`
- **Verification:** All verify checks pass: wc -l=265 (≥150), VERDICT: DROP-35B present, 5/5 PASS criteria, reversibility section, conditional follow-ups, Phase 18 SCs, Task 3 SKIPPED outcome — all confirmed.
- **Committed in:** `da7c184`

---

**Total deviations:** 1 auto-fixed (Rule 1 — efficiency deviation; all correctness checks pass)
**Impact on plan:** No impact. All task verify checks satisfied. Commit chain has 2 commits instead of 3-4 (Tasks 1+2+3 combined, Task 4 separate) — still atomic per logical boundary.

## Issues Encountered

None. All 5 SC4 criteria were clear-cut PASS from 18-01 and 18-02 evidence. Verdict derivation was fully mechanical. Doc synthesis was straightforward (lifted directly from predecessor docs).

## Phase 18 SC Verification

| SC | Description | Status |
|----|-------------|--------|
| SC1 | `documentation/single-model-eval.md` ≥ 150 lines, contains "Decision", per-test comparison, decision matrix, named verdict | PASS (265 lines, 6× "Decision", all sections present) |
| SC2 | 35B unloaded via launchctl; memory snapshot captured | PASS (18-01 record on disk; `launchctl unload` run at 18-01 Task 2 checkpoint) |
| SC3 | bench-122b-only.sh ran ≥ 30 invocations all via 122B | PASS (31/31 exit=0; 18-02 record on disk) |
| SC4 | 5 criteria evaluated; verdict named mechanically | PASS (5/5 PASS → DROP-35B; decision matrix in §Decision matrix) |
| SC5 | Architectural changes deferred; no code changes in this plan | PASS (0 src/ diff; 5 follow-ups enumerated as DEFERRED; `git diff HEAD~4 HEAD -- src/ tests/ bench/run.sh bench/baseline.json CLAUDE.md` = 0 lines) |

## Out-of-scope guardrails verified

Zero changes in Phase 18 to:
- `src/**` — UNTOUCHED
- `tests/**` — UNTOUCHED
- `bench/run.sh` — UNTOUCHED
- `bench/baseline.json` — UNTOUCHED
- `CLAUDE.md` — UNTOUCHED
- `documentation/qwen35-install.md` — UNTOUCHED
- `documentation/benchmark-qwen35-eval.md` — UNTOUCHED

## Next Phase Readiness

- **Follow-up architectural phase (RECOMMENDED):** Router collapse, bench/baseline.json halve, CLAUDE.md Runtime Environment update, scripts/bench-122b-only.sh promotion. Should run BEFORE Phase 16 (bench fixtures should reflect final canonical model before Phase 16 baselined them).
- **Phase 16 (Planning Wiring + Bench):** Plans on disk are valid. Phase 16-03 bench fixture keys need re-key from `_35b/_122b` to `_122b` only (or equivalent) once the follow-up architectural phase executes. Phase 16 can run in parallel with the follow-up architectural phase if the re-key is scoped carefully.
- **Phase 18 is shippable:** All 5 SCs satisfied; eval doc is the definitive Phase 18 deliverable; 18-01 + 18-02 evidence on disk.

---
*Phase: 18-single-model-eval*
*Completed: 2026-04-27*
