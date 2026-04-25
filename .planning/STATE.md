# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-04-26 after starting v1.3 milestone)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** v1.3 Bench-Driven Quality Gates — formalize bench harness + system-prompt shrink with regression gate validation

## Current Position

Milestone: v1.3 Bench-Driven Quality Gates (started 2026-04-26)
Phase: Not started (defining requirements + roadmap)
Plan: —
Status: Defining requirements
Last activity: 2026-04-26 — v1.2 archived; v1.3 scope confirmed (8 REQs across 2 phases: BENCH-01..05 + PERF-01..03); PROJECT.md updated.

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 (◆ in progress, requirements defining)

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
Stopped at: v1.3 milestone scoped and PROJECT.md updated. Next: write REQUIREMENTS.md, then spawn `gsd-roadmapper` for Phase 10 + 11.
Resume file: None — milestone setup is in progress; the workflow continues with REQUIREMENTS + ROADMAP.
