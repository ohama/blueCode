# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-04-23 for v1.1 milestone start)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** v1.1 Refinement — tech debt cleanup (REF-01, REF-02, OBS-05)

## Current Position

Milestone: v1.1 Refinement (started 2026-04-23)
Phase: Phase 6 — Dynamic Bootstrap (complete)
Plan: 2 of 2 complete
Status: Phase 6 complete. bootstrapAsync deleted, Program.fs sync, 216 tests pass. Ready for Phase 7.
Last activity: 2026-04-23 — Completed 06-02-PLAN.md (bootstrapAsync deletion + Program.fs sync bootstrap)

Progress: v1.1 [████░░░░░░░░░░░░░░░░] ~50% (REF-01 done, REF-02 done, OBS-05 pending in Phase 7)

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
- `Router.modelToName` absolute-path hardcode (UAT hotfix) — **RESOLVED in 06-01**: deleted from Core; adapter now resolves via Lazy<Task<ModelInfo>> probe
- `Step.Thought = "[not captured in v1]"` placeholder — reconsider for `--verbose` quality

**v1.1 decisions (Phase 6):**

| Decision | Plan | Rationale |
|----------|------|-----------|
| Option B: delete modelToName from Core; adapter owns wire value | 06-01 | Core purity preserved; buildRequestBody gets modelId: string injected from lazy probe |
| Two explicit Lazy<Task<ModelInfo>> (not Map) | 06-01 | Simpler for two Model cases; named bindings probe8000/probe8001 are clearer |
| CancellationToken.None in Lazy factory | 06-01 | Shared task must not be cancelled by one caller's Ctrl+C (06-RESEARCH Pitfall 6) |
| Empty ModelId -> POST 4xx (no silent failure) | 06-01 | Surfaces probe miss as LlmUnreachable at user-visible call site |
| AppComponents.MaxModelLen stays 8192 floor | 06-01 | Known regression for 72B warning accuracy; v1.2 candidate |
| getMaxModelLenAsync left in place | 06-01 | CompositionRoot.bootstrapAsync still needs it; plan 06-02 decides fate |
| bootstrapAsync deleted; bootstrap is sole factory | 06-02 | REF-02 satisfied: zero HTTP calls at startup; lazy probe fires on first LLM call per port |
| Log.Information reworded 'resolved' -> 'floor' | 06-02 | MaxModelLen is static 8192 default; 'resolved' was misleading (implied live probe result) |
| getMaxModelLenAsync fully removed | 06-02 | No caller after bootstrapAsync deletion; probeModelInfoAsync supersedes it |
| Test port 64321 instead of 8000 for closed-port test | 06-02 | 8000 may be live (flaky); 64321 is deterministically closed on any standard machine |

### Pending Todos (v1.1 seed)

All three items converted to requirements REF-01, REF-02, OBS-05. See `.planning/REQUIREMENTS.md`.

### Resolved post-milestone (v1.0 → v1.1 transition)

- **2026-04-23**: 32B Instruct re-download complete. Replaced Base Coder model at `~/llm-system/models/qwen32b/`. Verified: `special_tokens_map.json` + `added_tokens.json` present, chat smoke `finish: stop, content: 'OK'`, `dotnet run -- --model 32b "List files in src"` → 2 steps (3.5s + 3.3s) exit 0. Procedure documented in `documentations/qwen32b-base-to-instruct.md`.

### Blockers/Concerns

(None — roadmap created, planning can begin.)

## Session Continuity

Last session: 2026-04-23T09:08:47Z
Stopped at: Completed 06-02-PLAN.md. bootstrapAsync deleted; Program.fs sync; 216 tests pass. Phase 6 structurally complete.
Resume file: None — next action is execute Phase 7 (Thought Capture, OBS-05).
