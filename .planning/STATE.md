# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-04-26 after v1.3 milestone complete)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** Between milestones — v1.3 archived 2026-04-26; v1.4 scope pending (Path B candidate: TST-01 + bench fixture cleanup automation)

## Current Position

Milestone: v1.3 archived. Four milestones shipped: v1.0 MVP (2026-04-23), v1.1 Refinement (2026-04-24), v1.2 Tool Expansion (2026-04-26), v1.3 Bench-Driven Quality Gates (2026-04-26).
Phase: None active — `/gsd:new-milestone` will scope v1.4.
Plan: None.
Status: Ready to plan v1.4 OR enter observation window. Recommendation per Path B (v1.3 close discussion): small tactical milestone (~3 days, 2 phases — TST-01 + bench fixture cleanup automation) followed by 2-week observation window using `/gsd:add-todo` to capture real-use pain signals.
Last activity: 2026-04-26 — v1.3 milestone complete and archived. Tag `milestone-v1.3` cut. 6 plans across 2 phases (10, 11) shipped; 243 tests passing; 25 commits since `6133abc` (v1.2 close). All 8 v1.3 REQs satisfied. B2 audit hypothesis confirmed.

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 ✓ → v1.4 (next, TBD)

## Performance Metrics (cumulative, frozen)

**v1.0 totals:**
- 5 phases, 17 plans (16 autonomous + 1 human-gated UAT), 208 tests
- 85 commits, 5891 LOC F#
- ~27 hours (2026-04-22 14:37 → 2026-04-23 17:18)

**v1.1 totals:**
- 2 phases, 5 plans (3 in Phase 6 incl. 06-03 gap closure, 2 in Phase 7)
- 218 tests (208 v1.0 baseline + 10 v1.1 additions)
- 23 commits, +315 / -124 LOC F# delta
- ~19 hours (2026-04-23 17:32 → 2026-04-24 12:21)

**v1.2 totals:**
- 3 phases (8, 9, 9.1 inserted), 8 plans (2 + 1 + 5 across 9.1's two gap-closure rounds)
- 242 tests (218 v1.1 baseline + 18 from Phase 8 + 4 from Phase 9 + 1 from 09.1-02 + 1 from 09.1-05); 1 ignored
- 43 commits since v1.1 close
- ~3 days wall-clock (2026-04-24 → 2026-04-26)

**v1.3 totals:**
- 2 phases (10, 11), 6 plans (3 + 3)
- 243 tests (242 v1.2 baseline + 1 from 11-01); 1 ignored
- 25 commits since v1.2 close
- ~1 day wall-clock (2026-04-26)
- Source delta: `src/BlueCode.Core/AgentLoop.fs` (+lastReadHint), `src/BlueCode.Cli/{CompositionRoot.fs (1689→783 chars prompt), Adapters/FsToolExecutor.fs (2 Rule 3 fixes)}`, 1 new testCase
- New infrastructure: `bench/` directory tree (run.sh + 3 fixtures + baseline.json), `documentation/bench.md` (190 lines), 5 new howtos

Detailed per-plan history archived in `.planning/milestones/v{1.0,1.1,1.2,1.3}-phases/`.

## Accumulated Context

### Decisions

Rolled up into PROJECT.md Key Decisions table at v1.0/v1.1/v1.2/v1.3 milestone completions. See `.planning/PROJECT.md` for cumulative log with outcomes.

### Pending Todos (v1.4+ candidates)

**Path B priority candidates** (cited in v1.3 close discussion as small-tactical-milestone scope):

- **TST-01** — shared `makeMockResponse` test helper (3-milestone-old debt; now 3 instances across `AgentLoopTests.fs` and `ReplTests.fs`)
- **bench fixture cleanup automation** — `--gate` runs leave `bug_lastchar.fs`/`bug_average.fs` in LLM-edited state; gitignore drift in `git status` after every gate run

**Other deferred items** (no measured pain — defer to v1.5+ scoped from observation window):

- **STM-01** SSE streaming output (deferred 5x; UX win but no complaint after 5 milestones)
- **SES-01** session persistence + `--resume` (XL, v2+ per scope)
- **ROU-05** auto-escalation on MaxLoopsExceeded (less urgent post-9.1)
- **OPS-01** prompt cache hygiene (zero kickstarts needed in 9.1 + Phase 11)
- **OBS-06** per-port `MaxModelLen` visibility (minor)
- **CLI-08** Ctrl+C UX polish (minor)
- **Multi-platform `tryParseModelId`** (Windows OOS so likely permanent)

### Blockers/Concerns

None. v1.3 ships clean: 243 tests passing, all 8 REQs Validated, bench gate green (8/8), audit hypothesis confirmed, daily-driver use ongoing.

## Session Continuity

Last session: 2026-04-26
Stopped at: v1.3 milestone archived. PROJECT/MILESTONES updated; ROADMAP and REQUIREMENTS deleted; phase directories moved to `.planning/milestones/v1.3-phases/`. Tag `milestone-v1.3` cut.
Resume file: None — run `/gsd:new-milestone` to scope v1.4 (Path B recommended) OR daily-drive blueCode for 2 weeks and capture pain via `/gsd:add-todo` before scoping v1.5.
