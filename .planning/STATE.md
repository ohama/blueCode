# Project State

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-04-26 after v1.4 milestone complete)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** 2-week observation window (started 2026-04-26). Daily-drive blueCode; capture pain via `/gsd:add-todo`. v1.5 scope is observation-driven, NOT deferred-list draining.

## Current Position

Milestone: v1.4 ✓ SHIPPED 2026-04-26
Phase: — (between milestones)
Plan: —
Status: Observation window in progress; next milestone scoping starts when `/gsd:add-todo` entries surface measurable pain
Last activity: 2026-04-26 — v1.4 milestone closed; archives in `.planning/milestones/v1.4-*`; tag `milestone-v1.4`

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 ✓ → v1.4 ✓ → v1.5 ○ (pending observation)

## Performance Metrics (cumulative, frozen)

**v1.0:** 5 phases, 17 plans, 208 tests, 5891 LOC F#, 85 commits, ~27h
**v1.1:** 2 phases, 5 plans, 218 tests (+10), +315/-124 LOC, 23 commits, ~19h
**v1.2:** 3 phases, 8 plans, 242 tests (+24), Core diff confined to AgentLoop.fs/Domain.fs, 43 commits, ~3 days
**v1.3:** 2 phases, 6 plans, 243 tests (+1), bench harness in repo + 54% prompt shrink + B2 recovery, 25 commits, ~1 day
**v1.4:** 2 phases, 2 plans, 243 tests (unchanged — pure refactor + bash trap), +37/-16 LOC across tests/ + bench/run.sh + documentation/bench.md, 7 commits, ~1 day

Detailed per-plan history archived in `.planning/milestones/v{1.0,1.1,1.2,1.3,1.4}-phases/`.

## Accumulated Context

### Decisions

Rolled up into `.planning/PROJECT.md` Key Decisions table at v1.0/v1.1/v1.2/v1.3/v1.4 milestone completions. See PROJECT.md for cumulative log with outcomes.

Stable patterns established across milestones (load-bearing for next session):

- Core purity: `src/BlueCode.Core/**` no Serilog/Spectre/Argu, `task {}` only (CI-enforced via `scripts/check-no-async.sh`).
- Test discovery: explicit `rootTests` list in `RouterTests.fs` + ordered `<Compile Include>` in `BlueCode.Tests.fsproj`. New helper modules (no testList) skip the rootTests step but still need fsproj registration BEFORE consumers.
- Canonical test runner: `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` (NOT `dotnet test`).
- Bench gate: `bash bench/run.sh --gate` is the structural answer — exit 0 means no regression vs `bench/baseline.json`. After v1.4 BENCH-06, fixtures auto-reset on exit so `git status` stays clean.
- Atomic commits: `{type}({phase}-{plan}): {name}` per task; plan-meta separate; phase-complete bundles ROADMAP/STATE/REQUIREMENTS/VERIFICATION. NEVER `git add -A` or `git add .`.
- Loop-injection primitive: `lastEditPath` + `lastReadHint` in `runLoop`; post-user System messages enforce tool-terminality at conversation-history layer (overrides user-prompt explicit tool naming). Established in v1.2 (TLX-01/9.1-05) and extended in v1.3 (PERF-02).

### Pending Todos

v1.5+ candidates (for awareness only — DO NOT auto-pull from this list; v1.5 scope comes from observation-window `/gsd:add-todo` entries):

- Streaming output (STM-01) — deferred 6x
- Session persistence + `--resume` (SES-01) — v2+ per scope
- Auto-escalation on MaxLoopsExceeded (ROU-05) — deprioritized
- Ctrl+C UX polish (CLI-08) — minor
- Per-port `MaxModelLen` visibility (OBS-06) — minor
- Prompt cache hygiene (OPS-01) — deprioritized; zero kickstarts in v1.3
- Multi-platform `tryParseModelId` (Windows OOS so likely permanent)

### Blockers/Concerns

None. v1.4 closed cleanly; bench gate green; tests 243/1/0; daily-driver running stable.

## Session Continuity

Last session: 2026-04-26 (v1.4 milestone close)
Stopped at: v1.4 archived to `.planning/milestones/v1.4-*`; tag `milestone-v1.4` created; observation window starts.
Resume file: None — observation window is ambient, not session-bound.
Next workflow trigger: `/gsd:new-milestone` (when observation surfaces v1.5 scope) OR `/gsd:add-todo` (during daily use).
