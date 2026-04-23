# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-04-23 after v1.0 milestone)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** v1.1 planning (not yet started)

## Current Position

Milestone: v1.0 COMPLETE (shipped 2026-04-23)
Phase: N/A — awaiting `/gsd:new-milestone` for v1.1
Plan: N/A
Status: v1.0 milestone archived. Fresh ROADMAP.md / REQUIREMENTS.md to be created by `/gsd:new-milestone`. 3 open v1.1 items carried below as seed.
Last activity: 2026-04-23 — v1.0 milestone completion (archive, PROJECT.md evolution, git tag)

Progress: N/A (between milestones)

## Performance Metrics (v1.0 — final, frozen)

**v1.0 totals:**
- 5 phases, 17 plans (16 autonomous + 1 human-gated)
- 85 commits, 5891 LOC F#, 208 tests passing (1 ignored smoke)
- ~27 hours calendar time (2026-04-22 14:37 → 2026-04-23 17:18)

**By phase (v1.0 plans):**

| Phase | Plans | Key metric |
|-------|-------|------------|
| 01-foundation | 3/3 | 32 min avg 11 min/plan |
| 02-llm-client | 3/3 | 13 min avg 4 min/plan |
| 03-tool-executor | 3/3 | 27 min avg 9 min/plan |
| 04-agent-loop | 4/4 | incl. 04-04 SC-7 gap closure (2 task, ~5 min) |
| 05-cli-polish | 4/4 | incl. 05-04 human-gated UAT (retirement + real-task) |

Detailed per-plan history archived in `.planning/milestones/v1.0-phases/`.

## Accumulated Context

### Decisions

**Rolled up into PROJECT.md Key Decisions table at v1.0 milestone completion.** See `.planning/PROJECT.md` → "Key Decisions" for the cumulative log with outcomes (✓ Good / ⚠ Revisit / — Pending).

Notable items marked `⚠ Revisit` for v1.1 scoping:
- Expecto `[<Tests>]` auto-discovery disabled — 4 executors hit the rootTests registration pitfall
- `Router.modelToName` absolute-path hardcode (UAT hotfix) — replace with dynamic `/v1/models` query
- `Step.Thought = "[not captured in v1]"` placeholder — reconsider for `--verbose` quality

### Pending Todos (v1.1 seed)

Carried forward from v1.0 into v1.1 planning — these are not full requirements yet, but known gaps / deferred work the next milestone should scope:

1. **OBS-03 dynamic model id** — extend `QwenHttpClient.getMaxModelLenAsync` with `getModelIdAsync`; `Router.modelToName` queries server at startup instead of hardcoding path (removes portability break introduced by UAT hotfix `5ab5a95`)
2. **Decouple 32B cold-start probe** — `bootstrapAsync` currently blocks on localhost:8000 even when user targets 72B; lazy-probe on first actual LLM call instead
3. **Capture real LLM thought (optional)** — change `ILlmClient.CompleteAsync` signature to return `Thought * LlmOutput` or `LlmStep` so `--verbose` displays actual reasoning instead of `"[not captured in v1]"` placeholder

### Resolved post-milestone (v1.0 → v1.1 transition)

- **2026-04-23**: 32B Instruct re-download complete. Replaced Base Coder model at `~/llm-system/models/qwen32b/`. Verified: `special_tokens_map.json` + `added_tokens.json` present, chat smoke `finish: stop, content: 'OK'`, `dotnet run -- --model 32b "List files in src"` → 2 steps (3.5s + 3.3s) exit 0. Procedure documented in `documentations/qwen32b-base-to-instruct.md`.

### Blockers/Concerns

(None at milestone transition — v1.1 planning will surface new concerns as they arise.)

## Session Continuity

Last session: 2026-04-23
Stopped at: v1.0 milestone complete + archived + git tag `milestone-v1.0`. Ready for v1.1 scoping.
Resume file: None — use `/gsd:new-milestone` to start v1.1.
