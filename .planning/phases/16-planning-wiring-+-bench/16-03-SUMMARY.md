---
phase: 16-planning-wiring-+-bench
plan: "03"
subsystem: bench
tags: [multi-turn, persist-01, gate, plan-mode-deferred, agent-loop-tests]

# Dependency graph
requires:
  - phase: 16-02
    provides: PlanGate.fs (IKeyReader + render + promptUser), --plan flag, Program.fs dispatch, PlanGateTests 6 cases
  - phase: 15-01
    provides: --resume flag, Session persistence via FileSessionStore, JSONL session format
  - phase: 16-01
    provides: runPlanTurn signature (AgentLoop.fs:467), PlanParseTests, MockHelpers.makePlanResponse
provides:
  - MT_122b multi-turn bench fixture (PERSIST-01 end-to-end at bench layer)
  - bench/run.sh gate extended 6→7 invocations with mt() helper
  - bench/baseline.json extended to 7 entries (empirical MT_122b values)
  - bench/fixtures/mt_followup.txt (turn-2 prompt referencing prior context)
  - AgentLoopTests.runPlanTurnTests (2 mocked plan-mode end-to-end tests)
  - documentation/bench.md MT_122b section + plan-mode bench DEFERRED rationale
affects:
  - v2.1-planning (plan-mode bench fixture when /plan slash command lands)
  - future phase docs (bench gate now 7/7)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "mt() bash function: 2-turn session via --resume <id>; captures session id from stderr; combined_exit=max(t1,t2)"
    - "Gate metric = turn-1 step count (head -1 on [INF] Session ok: N steps); consistent with single-turn semantics"
    - "AgentLoopTests nested testList: add sub-testList to aggregator without touching fsproj/RouterTests"

key-files:
  created:
    - bench/fixtures/mt_followup.txt
    - .planning/phases/16-planning-wiring-+-bench/16-03-SUMMARY.md
  modified:
    - bench/run.sh
    - bench/baseline.json
    - tests/BlueCode.Tests/AgentLoopTests.fs
    - documentation/bench.md

key-decisions:
  - "v2.0 Phase 16-03 MT_122b single fixture (not MT_32b+MT_72b) — Phase 19 retirement; 122B sole canonical"
  - "v2.0 Phase 16-03 gate metric = turn-1 step count — matches existing head -1 parser; no parser changes"
  - "v2.0 Phase 16-03 plan-mode bench DEFERRED to v2.1+ — keystroke UX intractable for autonomous gate; PlanGateTests+PlanParseTests+AgentLoopTests substitute"
  - "v2.0 Phase 16-03 AgentLoopTests in-place extension — add runPlanTurnTests sub-list to existing aggregator; no new module, no fsproj change"
  - "v2.0 Phase 16-03 MT_122b empirical baseline — step_count=2, step_count_max=4, elapsed_median_s=7 (observed on 122B)"

patterns-established:
  - "Multi-turn bench: mt() captures Session: <id> from turn-1 stderr via grep -oE 'Session: [a-zA-Z0-9_-]+'"
  - "exit 99 sentinel from mt() if session id capture fails — Phase 15-02 regression signal"

# Metrics
duration: 39min
completed: 2026-04-27
---

# Phase 16 Plan 03: Multi-turn bench fixture + plan-mode end-to-end test + docs Summary

**MT_122b 2-turn PERSIST-01 gate fixture (empirical: 2 steps, 7s, 7/7 PASS) + runPlanTurn mocked end-to-end test + plan-mode bench DEFERRED v2.1+ documented in bench.md**

## Performance

- **Duration:** 39 min
- **Started:** 2026-04-27T12:04:56Z
- **Completed:** 2026-04-27T12:43:49Z
- **Tasks:** 3
- **Files modified:** 5 (+ 1 created)

## Accomplishments

- Extended bench gate from 6 to 7 invocations: new MT_122b multi-turn fixture validates PERSIST-01 end-to-end (turn 1 `list_dir`→`final` 2 steps; turn 2 references prior session via `--resume <id>` answering correctly with 1 step; both exit 0; combined_exit=0)
- AgentLoopTests gains 2 mocked plan-mode tests (runPlanTurnTests sub-list): happy path (scripted Plan response → validated Plan, callCount=1) and priorSteps propagation (prior FinalAnswer surfaces in assistant message replay); test count 280→282
- documentation/bench.md fully updated: MT_122b section (shape, gate metric, baseline, failure modes), plan-mode bench DEFERRED section (4-point rationale), updated mode flags table (7 invocations), interpreting gate output (7/7 format), known regressions (7 entries)

## Task Commits

1. **Task 1: MT_122b multi-turn bench fixture** — `e86843f` (feat)
2. **Task 2: AgentLoopTests mocked plan-mode test** — `5023155` (test)
3. **Task 3: documentation/bench.md updates** — `3b324c3` (docs)

## Files Created/Modified

- `bench/fixtures/mt_followup.txt` (NEW) — turn-2 prompt: "What was the file I just listed? Just give me the file count."
- `bench/run.sh` — mt() helper added; gate() extended with MT_122b invocation + labels list 6→7 + total=7 + help text updated
- `bench/baseline.json` — MT_122b entry appended (original 6 entries byte-for-byte preserved via git diff ONLY additions)
- `tests/BlueCode.Tests/AgentLoopTests.fs` — runPlanTurnTests sub-list + 2 testCases added to agentLoopTests aggregator
- `documentation/bench.md` — MT_122b section, plan-mode DEFERRED section, mode flags table, gate output example, known regressions updated

## Decisions Made

- **Single MT_122b fixture (not dual-model):** Phase 19 retired Qwen 2.5 entirely; 122B is the sole canonical model. No MT_32b/MT_72b.
- **Gate metric = turn-1 step count:** The existing gate parser uses `head -1` on `[INF] Session ok: N steps` markers, so it naturally picks up turn 1. Turn 2's step count is documented in the baseline `note` field but not gate-asserted. No parser changes needed.
- **Plan-mode bench DEFERRED to v2.1+:** PlanGate's `[a]ccept/[r]eject/[e]dit/[q]uit` prompt requires `Console.ReadKey` — intractable for autonomous regression gate. PlanGateTests (16-02) + PlanParseTests (16-01) + runPlanTurnTests (16-03) provide equivalent regression coverage. Documented with 4-point rationale in bench.md.
- **AgentLoopTests in-place extension:** Added `runPlanTurnTests` nested testList to existing `agentLoopTests` aggregator. No new module, no fsproj/RouterTests.fs change. RouterTests.fs already references `BlueCode.Tests.AgentLoopTests.agentLoopTests`.
- **Empirical MT_122b baseline:** smoke-ran the 2-turn cycle against live 122B before writing baseline.json. Observed: turn 1 = 2 steps (list_dir + final), turn 2 = 1 step (direct final referencing prior context), full cycle = 7s. Values: step_count=2, step_count_max=4 (headroom for routing variance), elapsed_median_s=7.

## Bench Gate Live Result

```
Pre-condition OK: port 8001 (122B) responsive.
===== GATE: regression subset (7 invocations) =====
===== gate_T6_122b (model=122b) =====   -> exit=0 elapsed=16s
===== gate_W1_122b (model=122b) =====   -> exit=0 elapsed=10s
===== gate_W2_122b (model=122b) =====   -> exit=0 elapsed=11s
===== gate_T1_122b (model=122b) =====   -> exit=0 elapsed=3s
===== gate_T5_122b (model=122b) =====   -> exit=0 elapsed=5s
===== gate_B2_122b (model=122b) =====   -> exit=0 elapsed=8s
===== gate_MT_122b (multi-turn, model=122b) =====
  turn1: exit=0 session=edc845c4620a43dbb17283eea0a698f0
  turn2: exit=0  combined exit=0 elapsed=7s
===== GATE: compare to baseline =====
  PASS T6_122b    steps=5/5 exit=0
  PASS W1_122b    steps=3/3 exit=0
  PASS W2_122b    steps=3/3 exit=0
  PASS T1_122b    steps=1/3 exit=0
  PASS T5_122b    steps=3/4 exit=0
  PASS B2_122b    steps=2/3 exit=0
  PASS MT_122b    steps=2/4 exit=0
===== GATE PASS (7/7) =====
```

**Exit code: 0**

## Test Count

280/1/0 (post-16-02) → **282/1/0** (post-16-03, +2 runPlanTurnTests)

## Original 6 Baseline Entries Preserved

`git diff bench/baseline.json` shows ONLY additions — no modifications to existing entries:
- T6_122b (step_count:4, step_count_max:5) — unchanged
- T5_122b (step_count:3, step_count_max:4) — unchanged
- B2_122b (step_count:2, step_count_max:3, actual_diagnosis preserved) — unchanged
- T1_122b (step_count:1, step_count_max:3) — unchanged
- W1_122b (step_count:3, step_count_max:3) — unchanged
- W2_122b (step_count:3, step_count_max:3) — unchanged

## Phase 16 Closure (SC Verification)

All 5 Phase 16 success criteria verified end-to-end:

| SC | Description | Evidence |
|----|-------------|---------|
| SC1 | `--plan` renders numbered plan table | 16-02 live smoke PASS |
| SC2 | `[a]ccept/[r]eject/[e]dit/[q]uit` dispatch | 16-02 PlanGateTests + live smoke PASS |
| SC3 | Plan parse/validation retry path | 16-01 PlanParseTests PASS |
| SC4 | `--plan --resume <id>` restores prior context | 16-02 live smoke SC4 PASS |
| SC5 | `bench/run.sh --gate` exits 0 with extended baseline | 16-03 live: **7/7 PASS** |

## Deviations from Plan

None — plan executed exactly as written.

The plan indicated `elapsed_median_s: 25` as a conservative estimate for the MT_122b baseline. Empirical measurement showed 7s (the 122B model was warm from prior runs). Used empirical value as specified ("populate baseline.json with EMPIRICAL values, not placeholder estimates"). This is execution of the plan, not a deviation.

## Issues Encountered

None — all tasks completed cleanly on first attempt.

## User Setup Required

None — no external service configuration required. The 122B service was already running on port 8001.

## Next Phase Readiness

Phase 16 is complete (16-01 ✓ 16-02 ✓ 16-03 ✓). All v2.0 Phase 16 deliverables shipped:
- `runPlanTurn` (AgentLoop.fs:467) — plan-mode Core entry point
- `PlanGate.fs` (IKeyReader + render + promptUser) — approval UX
- `--plan` Argu flag + Program.fs dispatch
- MT_122b bench fixture + 7/7 gate baseline
- 282/1/0 tests

Ready for `/gsd:complete-phase 16` to close out Phase 16 and proceed to v2.0 milestone completion.

v2.1+ candidates (documented in STATE.md and bench.md):
- Plan-mode bench fixture (PLAN_122b) — when /plan slash command lands
- Slash commands (/sessions, /plan, /clear)
- Sub-agent delegation
- LLM-aware context compaction

---
*Phase: 16-planning-wiring-+-bench*
*Completed: 2026-04-27*
