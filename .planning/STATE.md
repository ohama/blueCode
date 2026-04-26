# Project State

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-04-26 after starting v2.0 milestone)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** v2.0 Persistence + Planning — major version step. Domain extensions (Phase 14) → Persistence wiring (Phase 15) → Planning wiring + bench (Phase 16).

## Current Position

Milestone: v2.0 Persistence + Planning (started 2026-04-26)
Phase: Phase 15 COMPLETE (15-01 ✓ 15-02 ✓ 15-03 ✓)
Plan: 15-03 complete; Phase 16 next
Status: 254/1/0 tests; bench gate 8/8 PASS; all Phase 15 SCs verified
Last activity: 2026-04-26 — Completed 15-03-PLAN.md (SessionStoreTests + ReplTests multi-turn + live smoke + bench gate)

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 ✓ → v1.4 ✓ → v2.0 ◆ Phase 14 ✓ Phase 15 ✓ Phase 16 ░

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

- **v1.0 ports-and-adapters** — Core has no Console/Serilog/Spectre/Argu. Persistence adapters (file I/O for session JSONL) live in `BlueCode.Cli/Adapters/`, NOT in Core. New port: `ISessionStore` with Save/Load operations.
- **v1.0 single `runSession`** — Each REPL turn calls `runSession` independently with no carry. v2.0 PERSIST-01 changes this. Shape: `runSession` accepts prior `Session option` as input (additive, no mutation), REPL threads it through.
- **v1.0 5-step max + loop guard** — Stay. v2.0 PLAN-04 keeps `Plan.Steps.Length ≤ 5`. Plan validation runs BEFORE user approval.
- **v1.1 `LlmResponse` Core record** — v2.0 extends `LlmOutput` DU with `LlmOutput.Plan of Plan` variant. Domain.fs touch is unavoidable; Phase 14 does it atomically (mirrors v1.1 "big-bang compile cascade" pattern).
- **v1.2 loop-injection primitive** — Pattern reusable for plan-rejection hint injection (post-plan-rejection `[PLAN REJECTED]` System message follows same position discipline as `lastEditPath`/`lastReadHint`).
- **v1.3 bench gate** — `bench/run.sh --gate` is regression authority. Phase 16 extends baseline.json 8 → ~12 entries (multi-turn fixture + plan-mode fixture).
- **v1.4 MockHelpers.fs** — `makeMockResponse` is the canonical helper. Phase 14 adds sibling `makePlanResponse` in same module.
- **v2.0 Phase 14-01 compile cascade** — SessionId (line 91) → PlannedStep + Plan (lines 100-121) → LlmOutput.Plan of Plan (line 121) → Session (line 206). Type ordering constraint: Plan must precede LlmOutput; Session must follow Step. Transitional `| Plan _ -> Error(PlanInvalid ...)` in runLoop is intentional — Phase 16 replaces it.
- **v2.0 Phase 14-02 PlanValidator** — Pure `validatePlan : Plan -> Result<Plan, AgentError>` in Core. MaxPlanSteps=5 hardcoded (not AgentConfig-aware); knownTools set mirrors AgentLoop.dispatchTool. Priority order: length → tool registry → adjacent dups. Schema validation deferred to Phase 16 Cli adapter JSON parse layer.
- **v2.0 Phase 15-01 priorSteps** — `runSession` extended with `priorSteps: Step list` parameter (position: after `onStep`, before `userInput`). Steps replayed into ContextBuffer via `List.fold` before `runLoop`; `runLoop.steps` accumulator stays current-turn-only. Repl concatenates on each turn.
- **v2.0 Phase 15-01 JSONL format** — v2 header `{"version":2,"sessionId":"...","createdAt":"..."}` + per-turn `TurnComplete` envelope with cumulative `steps`. Last-envelope-wins on Load. Path: `~/.bluecode/sessions/<id>.jsonl` (distinct from existing per-step `session_<ts>.jsonl`).
- **v2.0 Phase 15-01 runMultiTurnWithSession** — New entry point in Repl.fs with explicit `Session` + `ISessionStore` params. Legacy `runMultiTurn` delegates to it with fresh Session + FileSessionStore. Program.fs now calls `runMultiTurnWithSession` directly with loaded/fresh Session.
- **v2.0 Phase 15-02 Argu --new-session flag** — Argu converts `NewSession` DU case to `--newsession` (no hyphen). Added `[<AltCommandLine("--new-session")>]` so both `--newsession` and `--new-session` work. Post-parse conflict check: `match resumeId, isNewSession with | Some _, true -> eprintfn "ERROR: conflicting flags..." + exit 2`.
- **v2.0 Phase 15-02 exact error messages** — `session not found: <id>` (exit 1), `session corrupt: <detail>` (exit 1), `conflicting flags: --resume and --new-session cannot be used together.` (exit 2). 15-03 assertions must match exactly.
- **v2.0 Phase 15-02 single-turn Save** — Program.fs now calls `SessionStore.Save` after single-turn completion so `--resume <id>` works across single-turn invocations too.

### Pending Todos

v2.1+ candidates (after v2.0 ships):

- LLM-aware context compaction (auto token-aware snip when session approaches context limit)
- Slash commands (`/sessions`, `/plan`, `/clear`)
- Sub-agent delegation (only meaningful once memory + planning land)
- Streaming output (STM-01) — deferred 7th cycle; revisit if observation surfaces complaint

### Blockers/Concerns

- **Compaction risk**: PERSIST-02 saves the full session JSONL but does not yet compact. Long sessions will hit the 80% context warning faster. v2.0 ships without compaction; v2.1 candidate.
- **Plan-mode + REPL interaction**: PLAN-02's `--plan` flag is per-invocation. If user wants plan mode every turn, they pass it every turn (or `/plan` slash command in v2.1).
- **Backwards compat**: v1.x JSONL session log format needs versioned schema upgrade. PERSIST-02 writes a `version: 2` header so old logs are recognizable.

## Session Continuity

Last session: 2026-04-26T20:25Z
Stopped at: Completed 15-03-PLAN.md (SessionStoreTests 5 testCases + ReplTests multi-turn SC1 + live smoke SC2/SC3/SC4 + bench gate SC5, 254/1/0, bench 8/8)
Resume file: None — Phase 15 complete; run Phase 16 (Planning wiring + bench extension) next

### New Decision (15-03)

- **v2.0 Phase 15-03 test isolation** — `withTempHome` / `$HOME` redirect does NOT work for `FileSessionStore` tests on macOS .NET because `Environment.GetFolderPath(SpecialFolder.UserProfile)` reads native OS APIs, not `$HOME` env var (setting `$HOME` to temp returns empty string). Use unique GUID-prefixed session IDs in real `~/.bluecode/sessions/` with `finally`-block cleanup + `testSequenced` instead.
