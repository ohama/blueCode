# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-04-26 after v1.2 milestone complete)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** Between milestones — v1.2 archived 2026-04-26; v1.3 scope pending

## Current Position

Milestone: v1.2 archived. Three milestones shipped to date: v1.0 MVP (2026-04-23), v1.1 Refinement (2026-04-24), v1.2 Tool Expansion (2026-04-26).
Phase: None active — `/gsd:new-milestone` will scope v1.3 (questioning → research → requirements → roadmap).
Plan: None.
Status: Ready to plan v1.3. Daily-driver use of blueCode ongoing as primary Mac coding agent.
Last activity: 2026-04-26 — v1.2 milestone complete and archived. Tag `milestone-v1.2` cut. 8 plans across 3 phases (8, 9, 9.1) shipped; 242 tests passing; 43 commits since `c4f087e` (v1.1 close).

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 (next, TBD)

## Performance Metrics (cumulative, frozen)

**v1.0 totals:**
- 5 phases, 17 plans (16 autonomous + 1 human-gated), 208 tests
- 85 commits, 5891 LOC F#
- ~27 hours (2026-04-22 14:37 → 2026-04-23 17:18)

**v1.1 totals:**
- 2 phases, 5 plans (3 in Phase 6 incl. 06-03 gap closure, 2 in Phase 7)
- 218 tests (208 v1.0 baseline + 10 v1.1 additions)
- 23 commits, +315 / -124 LOC F# delta
- ~19 hours (2026-04-23 17:32 → 2026-04-24 12:21)

**v1.2 totals:**
- 3 phases, 8 plans (2 in Phase 8 + 1 in Phase 9 + 5 in inserted Phase 9.1 across two gap-closure rounds)
- 242 tests (218 v1.1 baseline + 18 from Phase 8 + 4 from Phase 9 + 1 from 09.1-02 + 1 from 09.1-05); 1 ignored
- 43 commits since v1.1 close
- ~3 days wall-clock (2026-04-24 → 2026-04-26)
- Source delta: `src/BlueCode.Core/{AgentLoop.fs, Domain.fs}`, `src/BlueCode.Cli/{CompositionRoot.fs, Adapters/Json.fs, Adapters/FsToolExecutor.fs}`, `tests/BlueCode.Tests/{ToolExpansionTests.fs (new), FileToolsTests.fs, AgentLoopTests.fs}`

Detailed per-plan history archived in `.planning/milestones/v{1.0,1.1,1.2}-phases/`.

## Accumulated Context

### Decisions

Rolled up into PROJECT.md Key Decisions table at v1.0/v1.1/v1.2 milestone completions. See `.planning/PROJECT.md` for cumulative log with outcomes.

### Pending Todos (v1.3+ candidates)

Strongest signals from v1.2 audit + Phase 9.1 discoveries:

- **Bench harness formalization** — move `/tmp/bench-v1.2/run.sh` and `bench-fixtures/` to repo-tracked `bench/` with `--gate` mode. v1.2 ran two audit-rebench cycles; locks the 16-log baseline as a regression suite.
- **PERF-01** system prompt shrink (~1500 → ~700 chars) — B2 regression provides concrete signal; could leverage 9.1-05 loop-injection primitive to move contextual hints to post-tool injections instead of base prompt
- **STM-01** SSE streaming output — UX win, no measured pain
- **SES-01** session persistence + `--resume` — XL, no measured pain
- **ROU-05** auto-escalation on MaxLoopsExceeded — less urgent post-9.1
- **OPS-01** prompt cache kickstart schedule — 9.1-05 needed zero kickstarts
- **OBS-06** per-port `MaxModelLen` visibility
- **TST-01** shared `makeMockResponse` test helper

### Blockers/Concerns

None. v1.2 ships clean: 242 tests passing, Core diff confined, all 4 v1.2 REQs Validated, all 3 audit tech-debt items closed by Phase 9.1.

## Session Continuity

Last session: 2026-04-26
Stopped at: v1.2 milestone archived. PROJECT/MILESTONES updated; ROADMAP and REQUIREMENTS deleted; phase directories moved to `.planning/milestones/v1.2-phases/`. Tag `milestone-v1.2` cut.
Resume file: None — run `/gsd:new-milestone` to scope v1.3.
