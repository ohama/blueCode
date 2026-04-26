# Project State

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-04-26 after starting v2.0 milestone)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** v2.0 Persistence + Planning — major version step. Bundle of cross-turn REPL memory + `--resume <id>` (PERSIST-01..04) and plan-then-execute mode with user approval gate (PLAN-01..04). Architectural shift: state lives outside a single `runSession`.

## Current Position

Milestone: v2.0 Persistence + Planning (started 2026-04-26)
Phase: Not started (defining requirements)
Plan: —
Status: Defining requirements; roadmap creation pending
Last activity: 2026-04-26 — v2.0 milestone started; PROJECT.md updated with Active reqs

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 ✓ → v1.4 ✓ → v2.0 ◆ (in progress)

## Performance Metrics (cumulative, frozen)

**v1.0:** 5 phases, 17 plans, 208 tests, 5891 LOC F#, 85 commits, ~27h
**v1.1:** 2 phases, 5 plans, 218 tests (+10), +315/-124 LOC, 23 commits, ~19h
**v1.2:** 3 phases, 8 plans, 242 tests (+24), Core diff confined to AgentLoop.fs/Domain.fs, 43 commits, ~3 days
**v1.3:** 2 phases, 6 plans, 243 tests (+1), bench harness in repo + 54% prompt shrink + B2 recovery, 25 commits, ~1 day
**v1.4:** 2 phases, 2 plans, 243 tests (unchanged), zero src/ diff, 7 commits, ~1 day

Detailed per-plan history archived in `.planning/milestones/v{1.0,1.1,1.2,1.3,1.4}-phases/`.

## Accumulated Context

### Decisions

Cumulative log in PROJECT.md Key Decisions table. See PROJECT.md for outcomes through v1.4.

Items relevant to v2.0 (architectural touch points):

- **v1.0 ports-and-adapters** — Core has no Console/Serilog/Spectre/Argu. v2.0 must keep this: persistence adapters (file I/O for session JSONL) live in `BlueCode.Cli/Adapters/`, NOT in Core. New port likely needed: `ISessionStore` with Save/Load/List operations.
- **v1.0 single `runSession`** — Each REPL turn calls `runSession` independently with no carry. v2.0 PERSIST-01 changes this. Likely shape: `runSession` accepts prior `Step list` as input (additive, no mutation), REPL threads it through.
- **v1.0 5-step max + `(action, input_hash)` loop guard** — Stay. v2.0 PLAN-04 keeps `Plan.Steps.Length ≤ 5`. Plan validation runs BEFORE user approval, not at runtime.
- **v1.1 `LlmResponse` Core record** — v2.0 likely extends `LlmOutput` DU with new variant (e.g., `LlmOutput.Plan of Plan`) or adds `Plan` as separate output mode. Domain.fs touch is unavoidable.
- **v1.2 loop-injection primitive** (`lastEditPath`, `lastReadHint`) — Pattern reusable for plan-mode if needed (e.g., post-plan-rejection hint to LLM).
- **v1.3 bench gate** — `bench/run.sh --gate` is the structural answer for regression detection. New v2.0 fixtures likely needed: a multi-turn test, a `--resume` test, a plan-mode test. Baseline grows from 8 → ~12.
- **v1.4 BENCH-06 EXIT trap** — Bench fixtures auto-reset; new v2.0 fixtures should follow same pattern.
- **Canonical test runner**: `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` (NOT `dotnet test`). Test discovery: explicit `rootTests` list in `RouterTests.fs` + ordered `<Compile Include>` in fsproj.

### Pending Todos

v2.1+ candidates (after v2.0 ships):

- LLM-aware context compaction (auto token-aware snip when session approaches context limit)
- Slash commands (`/sessions`, `/plan`, `/clear`)
- Sub-agent delegation (only meaningful once memory + planning land)
- Streaming output (STM-01) — deferred 7th cycle; revisit if observation surfaces complaint

### Blockers/Concerns

- **Compaction risk**: PERSIST-02 saves the full session JSONL but does not yet compact. Long sessions will hit the 80% context warning faster. v2.0 ships without compaction; v2.1 candidate.
- **Plan-mode + REPL interaction**: PLAN-02's `--plan` flag is per-invocation, not REPL-mode-toggle. If user wants plan mode every turn, they pass it every turn (or a new `/plan` slash command in v2.1).
- **Backwards compat**: v1.x JSONL session log format may need a versioned schema upgrade. PERSIST-02 should write a `version: 2` header so old logs are recognizable.

## Session Continuity

Last session: 2026-04-26 (v2.0 milestone start)
Stopped at: PROJECT.md updated with v2.0 Active reqs (PERSIST-01..04 + PLAN-01..04). Next: write REQUIREMENTS.md, spawn roadmapper.
Resume file: None — milestone is mid-setup, REQUIREMENTS.md and ROADMAP.md pending.
