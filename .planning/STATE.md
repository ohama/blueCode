# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-04-26 after starting v1.3 milestone)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** v1.3 Bench-Driven Quality Gates — formalize bench harness + system-prompt shrink with regression gate validation

## Current Position

Milestone: v1.3 Bench-Driven Quality Gates (started 2026-04-26)
Phase: 10 — Bench Formalization (2/3 plans complete)
Plan: 10-02 complete; ready for 10-03
Status: In progress — wave 2 done
Last activity: 2026-04-26 — Completed 10-02-PLAN.md (baseline.json + --gate mode; SC1 verified positive + negative)

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 (Phase 10: ██░ 2/3 plans · Phase 11: ░░░ 0/3 plans)

## Performance Metrics (v1.0 + v1.1 + v1.2 — cumulative, frozen)

**v1.0:** 5 phases, 17 plans, 208 tests, 5891 LOC F#, 85 commits, ~27h
**v1.1:** 2 phases, 5 plans, 218 tests (+10), +315/-124 LOC, 23 commits, ~19h
**v1.2:** 3 phases, 8 plans, 242 tests (+24), Core diff confined to AgentLoop.fs/Domain.fs, 43 commits, ~3 days

Detailed per-plan history archived in `.planning/milestones/v{1.0,1.1,1.2}-phases/`.

## Accumulated Context

### Decisions

Rolled up into PROJECT.md Key Decisions table at v1.0/v1.1/v1.2 milestone completions. See `.planning/PROJECT.md` for cumulative log with outcomes.

Notable items marked `⚠ Revisit` carried into v1.3:

- v1.0 Expecto explicit `rootTests` list — 4 executors hit registration pitfall; CLAUDE.md "Test discovery pattern" documents the pitfall but `[<Tests>]` auto-discovery transition still pending
- v1.1 `makeMockResponse` test helper duplicated in `AgentLoopTests.fs` + `ReplTests.fs` — TST-01 deferred candidate; possible bundle into Phase 10 bench/test cleanup
- v1.2 Mid-milestone audit caught structural-vs-behavioral gap — phase verifiers should require probe-style behavioral tests for any spec citing a specific failure trace; v1.3 BENCH-04 `--gate` mode is the structural answer

Plan 10-02 decisions (2026-04-26):

- T6_72b live step count = 4 (not 5 from research); step_count_max = 5 (+1 slack); T6_72b does NOT need the research-default of 5 steps post-09.1
- W1/W2 step_count_max = 3 exact (no slack); 09.1-05 loop-injection enforces exactly 3 steps for write tasks
- B2 entries: pass=false, regression=true; gate verdict branch 1 always passes them until PERF-03 manually verifies correct diagnosis ("empty list" not "integer truncation") and updates baseline.json
- 3-branch verdict logic only (no 4th "regression recovery" branch); clean, testable, no false positives
- Gate output is console-only; no JSON report in Phase 10

New v1.3 architectural input from v1.2 close:

- 09.1-05 loop-injection primitive (`lastEditPath` + post-user `[POST-EDIT CONSTRAINT]` System message) is reusable; PERF-02 extends it to post-`read_file`-truncated and optionally post-`write_file` to move contextual hints out of base prompt
- B2 regression (divide-by-zero misdiagnosis on both 32B + 72B) hypothesized as prompt-length attention shift; PERF-03 validates by re-running B2 after PERF-01 shrink

### Pending Todos

v1.4+ seed candidates (not v1.3 scope):

- Streaming output (STM-01)
- Session persistence + `--resume` (SES-01)
- Auto-escalation on MaxLoopsExceeded (ROU-05)
- Ctrl+C UX polish (CLI-08)
- Per-port `MaxModelLen` visibility (OBS-06)
- Prompt cache hygiene (OPS-01)
- Multi-platform `tryParseModelId` (Windows OOS so likely permanent)

### Blockers/Concerns

None at v1.3 start. Daily-driver use of blueCode ongoing; v1.3 work should not break daily flow.

## Session Continuity

Last session: 2026-04-26
Stopped at: Completed 10-02-PLAN.md. bench/baseline.json (8 entries) + bench/run.sh --gate committed (2 task commits + metadata). SC1 positive: GATE PASS 8/8 exit=0 (~116s). SC1 negative: tightened T6_32b.step_count_max=0 → GATE FAIL exit=1; restored → GATE PASS exit=0 round-trip clean. Next: Plan 10-03 (documentation/bench.md + Phase 10 final verification).
Resume file: None
