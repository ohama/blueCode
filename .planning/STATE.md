# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-04-26 after starting v1.4 milestone)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** v1.4 Test Hygiene + Bench Polish — small tactical milestone (Path B from v1.3 close); 2 phases (TST-01 shared mock helper + BENCH-06 fixture cleanup automation); exit criterion is a 2-week observation window for v1.5 scoping

## Current Position

Milestone: v1.4 Test Hygiene + Bench Polish (started 2026-04-26)
Phase: 12 of 2 (12-test-helper-consolidation) — complete
Plan: 12-01 of 1 — complete
Status: Phase 12 complete; Phase 13 (BENCH-06) is next
Last activity: 2026-04-26 — Completed 12-01-PLAN.md (TST-01: MockHelpers consolidation)

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 ✓ → v1.4 ◆ [Phase 12 ✓ | Phase 13 pending]

## Performance Metrics (cumulative, frozen)

**v1.0:** 5 phases, 17 plans, 208 tests, 5891 LOC F#, 85 commits, ~27h
**v1.1:** 2 phases, 5 plans, 218 tests (+10), +315/-124 LOC, 23 commits, ~19h
**v1.2:** 3 phases, 8 plans, 242 tests (+24), Core diff confined to AgentLoop.fs/Domain.fs, 43 commits, ~3 days
**v1.3:** 2 phases, 6 plans, 243 tests (+1), bench harness in repo + 54% prompt shrink + B2 recovery, 25 commits, ~1 day

Detailed per-plan history archived in `.planning/milestones/v{1.0,1.1,1.2,1.3}-phases/`.

## Accumulated Context

### Decisions

Rolled up into PROJECT.md Key Decisions table at v1.0/v1.1/v1.2/v1.3 milestone completions. See `.planning/PROJECT.md` for cumulative log with outcomes.

Notable items relevant to v1.4:

- v1.0 Expecto explicit `rootTests` list — 4 executors hit registration pitfall; CLAUDE.md "Test discovery pattern" documents the pitfall but `[<Tests>]` auto-discovery transition still pending. v1.4 TST-01 adds `MockHelpers.fs` as a pure helper module (no testList) — NO entry needed in `RouterTests.fs:rootTests`. Only `BlueCode.Tests.fsproj` `<Compile Include>` registration required, placed BEFORE `AgentLoopTests.fs` and `ReplTests.fs` in compile order.
- v1.1 `makeMockResponse` test helper duplicated — TST-01 CLOSED in v1.4 Phase 12. Actual duplication was 2 definitions (AgentLoopTests + ReplTests); REQUIREMENTS.md said "3 instances" but that conflated definition sites with call sites. Now in shared MockHelpers.fs; module-public (not private).
- v1.3 bench fixture working-tree drift — `--gate`, `--canary`, `--all`, `--b2` runs leave write-task fixtures in LLM-edited state. v1.4 BENCH-06 is the closure.
- bench/run.sh is bash 3.2 compatible (macOS default); uses `set -u` only (no `set -e`). The trap must be bash 3.2 compatible and must NOT interfere with exit codes.
- `dotnet test` does NOT run Expecto in this project. Canonical runner: `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj`.

v1.4-specific input from v1.3 close discussion:

- Path B chosen over Path A (streaming) and Path C (observation-only) — middle path threads "discipline preserved" with "small wins shipped"
- Exit criterion is the load-bearing element: 2-week observation window with `/gsd:add-todo` capture for v1.5 scoping (not deferred-list draining)

### Pending Todos

v1.5+ candidates (scope from observation, not from this list):

- Streaming output (STM-01)
- Session persistence + `--resume` (SES-01) [v2+ per scope]
- Auto-escalation on MaxLoopsExceeded (ROU-05)
- Ctrl+C UX polish (CLI-08)
- Per-port `MaxModelLen` visibility (OBS-06)
- Prompt cache hygiene (OPS-01)
- Multi-platform `tryParseModelId` (Windows OOS so likely permanent)

### Blockers/Concerns

None at v1.4 roadmap completion. Both phases are mechanical / bash-only; no Core diff expected.

## Session Continuity

Last session: 2026-04-26T12:20:32Z
Stopped at: Completed 12-01-PLAN.md (TST-01 closed). Phase 12 done; Phase 13 (BENCH-06) is next.
Resume file: None — ready for `/gsd:plan-phase 13` or `/gsd:execute-phase 13` if plan already exists.
