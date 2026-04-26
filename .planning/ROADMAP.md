# Roadmap: blueCode v2.0 Persistence + Planning

**Status:** In Progress (started 2026-04-26)
**Phases:** 14 - 16
**Milestone goal:** Break the process-lifetime constraint that v1 deliberately accepted. Bundle cross-turn REPL memory + `--resume <id>` (PERSIST-01..04) with plan-then-execute mode with user approval gate (PLAN-01..04). The two features share an architectural root — state outside a single `runSession` — and ship together by design.

## Overview

v2.0 makes two architectural investments simultaneously: session state persists across turns and process restarts (`Session` record threaded through the REPL, JSONL on disk, `--resume`), and the agent can emit a typed plan before executing (new `LlmOutput.Plan` variant, user approval gate, pre-validation). Both features require extending `Domain.fs` with new types; Phase 14 captures that shared Domain work atomically so Phases 15 and 16 can focus purely on Cli adapter wiring without revisiting Core.

**Phase numbering:** Continues from v1.4's Phase 13. v1.0 used 1-5, v1.1 used 6-7, v1.2 used 8/9/9.1, v1.3 used 10-11, v1.4 used 12-13.

---

## Phases

- [x] **Phase 14: Domain Extensions** ✓ — Session record, Plan DU, ISessionStore port, plan validator (all Core types, no Cli wiring)
- [ ] **Phase 15: Persistence Wiring** — REPL session threading, JSONL adapter, --resume, --new-session
- [ ] **Phase 16: Planning Wiring + Bench** — --plan flag, approval gate UI, plan retry wiring, bench fixtures extended

---

## Phase Details

### Phase 14: Domain Extensions

**Goal:** All v2.0 types and their pure-Core logic compile and are tested in isolation — `Session`, `Plan`, `ISessionStore` port, and the plan validator — before any Cli adapter touches them. Domain.fs is the single commit that shifts the type-level foundation.

**Depends on:** Nothing (first phase of v2.0; v1.4 is the base)

**Requirements:** PERSIST-01 (Session shape), PLAN-01 (Plan DU), PLAN-04 (plan validator as pure Core function)

**Success Criteria** (what must be TRUE when Phase 14 completes):

1. `Domain.fs` contains `Session = { Id: SessionId; Steps: Step list; CreatedAt: DateTimeOffset; LastActivityAt: DateTimeOffset }` and `LlmOutput.Plan of Plan` where `Plan = { Steps: PlannedStep list; Rationale: string }` — both compile with exhaustive pattern match coverage verified by the compiler.
2. `ISessionStore` port is defined in Core (`Save: Session -> Task<Result<unit, AgentError>>` and `Load: SessionId -> Task<Result<Session, AgentError>>`), with `AgentError.SessionNotFound` and `AgentError.SessionCorrupt` variants added.
3. `AgentError.PlanInvalid of string` variant exists; the plan validator (pure function in Core) returns `PlanInvalid` for the THREE structural rules: unknown tool name, Steps.Length > 5, duplicate adjacent steps. **Schema-invalid input is deferred to Phase 16's JSON parse layer** (where `JsonSchema.Net` lives, in `BlueCode.Cli/Adapters/Json.fs` — Cli-side concern outside Core's purity boundary).
4. Unit tests cover the validator: 5 `testCase`s in `PlanValidatorTests.fs` covering happy-path + each of the 3 structural failure modes + 1 edge case. **Plan JSON parsing (and out-of-schema parse failure) is deferred to Phase 16** — Phase 14 ships the F# types + pure validator only; the LLM-wire-format layer is wired when `--plan` flag and system-prompt suffix arrive in Phase 16. `makePlanResponse` mock helper exists in `MockHelpers.fs` for future Phase 16 consumers.
5. Existing 243/1/0 test baseline is preserved on the `ToolCall`/`FinalAnswer` paths (extended to 248/1/0 with the +5 PlanValidator test cases).

**Plans:** 2 plans expected

Plans:
- [ ] 14-01: Add `Session` record, `LlmOutput.Plan of Plan`, `AgentError` new variants to `Domain.fs`; define `ISessionStore` port; compile + exhaustive-match CI green
- [ ] 14-02: Plan validator (pure Core function), `makePlanResponse` mock helper in `MockHelpers.fs`, unit tests for Plan parsing + all four `PlanInvalid` cases

---

### Phase 15: Persistence Wiring

**Goal:** The REPL maintains conversation history across turns within a session; every completed turn is written to `~/.bluecode/sessions/<id>.jsonl`; `--resume <id>` reconstructs prior context; `--new-session` forces a fresh id; conflicting flags are rejected at parse time.

**Depends on:** Phase 14 (Session record and ISessionStore port must exist)

**Requirements:** PERSIST-01, PERSIST-02, PERSIST-03, PERSIST-04

**Success Criteria** (what must be TRUE when Phase 15 completes):

1. Running two consecutive REPL turns in one invocation, the second turn's LLM call receives the first turn's steps as prior context — verified by a mocked-LLM multi-turn test where turn 2 sees turn 1's tool results without re-running them.
2. After any completed turn, `~/.bluecode/sessions/<id>.jsonl` exists, contains a `version: 2` header line, and at least one `TurnComplete` envelope; the session id is printed to stderr at startup and is grep-able.
3. `blueCode --resume <id> "follow-up"` loads the session correctly and runs the new turn with prior context; unknown id exits 1 with a `SessionNotFound` error on stderr (no exception, no stack trace); corrupt JSONL exits 1 with `SessionCorrupt` on stderr.
4. `blueCode --new-session` starts a fresh session (new id) regardless of any previously written session files; `blueCode --resume X --new-session` is rejected at Argu parse time with a "conflicting flags" error message.
5. `bench/run.sh --gate` stays 8/8 PASS — no regression on T1-T7, W1/W2, B2 baselines.

**Plans:** 3 plans expected

Plans:
- [ ] 15-01: `runSession` accepts `Session option` (prior context); REPL threads `Session` across turns; `FileSessionStore` adapter in `BlueCode.Cli/Adapters/` implements `ISessionStore.Save`
- [ ] 15-02: `ISessionStore.Load` wired into CompositionRoot; Argu gains `--resume <ID>` and `--new-session`; conflicting-flag validation; session id printed to stderr at startup
- [ ] 15-03: `SessionStoreTests.fs` round-trip serialization tests; live smoke: two-turn session written + resumed correctly; bench gate green

---

### Phase 16: Planning Wiring + Bench

**Goal:** `blueCode --plan "..."` triggers plan-then-execute mode — the LLM emits a typed plan, the plan validator runs before the user sees it, the user chooses accept/reject/edit/quit, and the agent executes (or retries) accordingly. New bench fixtures cover multi-turn and plan-mode scenarios; `bench/baseline.json` grows from 8 to ~12 entries.

**Depends on:** Phase 15 (REPL session threading must be stable; plan validator from Phase 14 used here for retry wiring)

**Requirements:** PLAN-02, PLAN-03, PLAN-04 (wiring), PERSIST-01 (verified end-to-end with planning)

**Note:** PLAN-01 (Plan DU) and PLAN-04 (pure validator covering 3 structural rules: unknown tool, length>5, duplicate adjacent) land in Phase 14. **This phase additionally wires the Plan JSON parse layer** in `src/BlueCode.Cli/Adapters/Json.fs` — extends the `llmStepSchema` enum with `"plan"`, adds a `Plan` branch to `toLlmOutput`, and handles the 4th `PlanInvalid` failure mode (schema-invalid input) at parse time before the validator runs. `makePlanResponse` (defined in Phase 14 `MockHelpers.fs`) becomes load-bearing for Phase 16's plan-mode tests.

**Success Criteria** (what must be TRUE when Phase 16 completes):

1. `blueCode --plan "list 3 files in src"` displays a rendered numbered plan table (step #, tool, input preview, rationale) and shows the `[a]ccept / [r]eject / [e]dit / [q]uit` prompt before any tool runs.
2. Typing `a` executes the plan steps in order; typing `r` sends a `[PLAN REJECTED]` message back to the LLM and re-prompts for a new plan; typing `q` exits with code 0 and no tool execution; typing `e` prompts for a comment that is appended to the next LLM message.
3. A malformed plan (unknown tool name, schema-invalid input, > 5 steps, or duplicate adjacent steps) never reaches the user's approval prompt — it is rejected silently, the LLM is asked to retry, and only a valid plan is shown; after 2 retries the error is surfaced to the user.
4. `--plan --resume <id>` is a valid combination — the agent loads prior context and enters plan mode for the next turn.
5. `bench/run.sh --gate` exits 0 with the extended baseline (~12 entries including multi-turn fixture and plan-mode fixture); no regression on the original 8 T1-T7/W1/W2/B2 entries.

**Plans:** 3 plans expected

Plans:
- [ ] 16-01: Plan-mode system-prompt suffix wired via `--plan` flag; `AgentLoop` dispatches `LlmOutput.Plan` to approval gate; plan validator retry path (2 retries, then surface error)
- [ ] 16-02: `PlanGate.fs` (or `Repl.fs` extension) — Spectre-rendered plan table, keystroke dispatch (a/r/e/q), reject re-prompt injection, edit comment capture
- [ ] 16-03: New bench fixtures (multi-turn fixture, plan-mode fixture); `bench/baseline.json` updated to ~12 entries; `bench/run.sh --gate` verified 12/12 PASS; documentation/bench.md updated

---

## Progress

| Phase | Milestone | Requirements | Plans Complete | Status | Completed |
|-------|-----------|--------------|----------------|--------|-----------|
| 14. Domain Extensions | v2.0 | PERSIST-01, PLAN-01, PLAN-04 | 2/2 | ✓ Complete | 2026-04-26 |
| 15. Persistence Wiring | v2.0 | PERSIST-01, PERSIST-02, PERSIST-03, PERSIST-04 | 0/3 | Not started | - |
| 16. Planning Wiring + Bench | v2.0 | PLAN-02, PLAN-03, PLAN-04 (wiring) | 0/3 | Not started | - |

---

*Roadmap created: 2026-04-26*
*Last updated: 2026-04-26 — initial roadmap for v2.0 Persistence + Planning*
