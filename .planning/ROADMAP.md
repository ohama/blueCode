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
- [x] **Phase 15: Persistence Wiring** ✓ — REPL session threading, JSONL adapter, --resume, --new-session
- [ ] **Phase 16: Planning Wiring + Bench** — --plan flag, approval gate UI, plan retry wiring, bench fixtures extended
- [x] **Phase 17: Qwen 3.5 Evaluation** ✓ — SWITCH verdict 2026-04-27; 35B/122B replaces 32B/72B as canonical pair (3.4× speedup, no regressions, 8/8 gate)
- [x] **Phase 18: Single-Model 122B Evaluation** ✓ — DROP-35B verdict 2026-04-27; all 5 §SC4 criteria PASS; 31/31 bench invocations exit=0; +19.42 GB PhysMem freed; B2 DivByZero preserved; Router collapse + baseline halve deferred to follow-up phase
- [x] **Phase 19: Qwen 2.5 Retirement + 122B Single-Model Default** ✓ — Retired Qwen 2.5 32B/72B + qwen72b.3bit (85 GB reclaimed); 122B canonical (single-model default); 35B preserved as cold rollback; `--model 32b/72b` aliases removed (exit 2 with retirement error); `--with-35b` opt-in flag added with eager port-8000 probe; `bench/run.sh` absorbed `scripts/bench-122b-only.sh`; `bench/baseline.json` halved to 6 `_122b` entries; gate 6/6 PASS; 262/1/0 tests (completed 2026-04-27)

**Note on phase ordering:** Phase 17 should run BEFORE Phase 16 if model swap is desired before bench fixtures are set. If 35B/122B replaces 32B/72B as canonical, Phase 16's bench baseline (T6_32b, W1_32b, B2_72b, etc.) needs new model ids. Phase 16 plans on disk remain valid for whichever model pair ends up canonical; user decides execution order after Phase 17 ships findings. **Phase 19 also runs BEFORE Phase 16** — once `bench/baseline.json` is halved to single-model `_122b` keys, Phase 16-03's multi-turn fixture additions (MT_*) must use the final canonical baseline shape.

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
4. `blueCode --new-session` starts a fresh session (new id) regardless of any previously written session files; `blueCode --resume X --new-session` is rejected at startup (post-parse validation, before bootstrap) with exit code 2 and a "conflicting flags" error message on stderr.
5. `bench/run.sh --gate` stays 8/8 PASS — no regression on T1-T7, W1/W2, B2 baselines.

**Plans:** 3 plans expected

Plans:
- [ ] 15-01: `runSession` accepts `Session option` (prior context); REPL threads `Session` across turns; `FileSessionStore` adapter in `BlueCode.Cli/Adapters/` implements `ISessionStore.Save`
- [ ] 15-02: `ISessionStore.Load` wired into CompositionRoot; Argu gains `--resume <ID>` and `--new-session`; conflicting-flag validation; session id printed to stderr at startup
- [ ] 15-03: `SessionStoreTests.fs` round-trip serialization tests; live smoke: two-turn session written + resumed correctly; bench gate green

---

### Phase 16: Planning Wiring + Bench

**Goal:** `blueCode --plan "..."` triggers plan-then-execute mode — the LLM emits a typed plan, the plan validator runs before the user sees it, the user chooses accept/reject/edit/quit, and the agent executes (or retries) accordingly. New bench fixtures cover multi-turn scenarios; `bench/baseline.json` grows from 8 to 10 entries (plan-mode bench fixture deferred to v2.1+ — keystroke-driven UX is intractable for a regression gate; unit-test coverage in PlanParseTests + PlanGateTests substitutes).

**Depends on:** Phase 15 (REPL session threading must be stable; plan validator from Phase 14 used here for retry wiring)

**Requirements:** PLAN-02, PLAN-03, PLAN-04 (wiring), PERSIST-01 (verified end-to-end with planning)

**Note:** PLAN-01 (Plan DU) and PLAN-04 (pure validator covering 3 structural rules: unknown tool, length>5, duplicate adjacent) land in Phase 14. **This phase additionally wires the Plan JSON parse layer** in `src/BlueCode.Cli/Adapters/Json.fs` — extends the `llmStepSchema` enum with `"plan"`, adds a `Plan` branch to `toLlmOutput`, and handles the 4th `PlanInvalid` failure mode (schema-invalid input) at parse time before the validator runs. `makePlanResponse` (defined in Phase 14 `MockHelpers.fs`) becomes load-bearing for Phase 16's plan-mode tests.

**Success Criteria** (what must be TRUE when Phase 16 completes):

1. `blueCode --plan "list 3 files in src"` displays a rendered numbered plan table (step #, tool, input preview, rationale) and shows the `[a]ccept / [r]eject / [e]dit / [q]uit` prompt before any tool runs.
2. Typing `a` executes the plan steps in order; typing `r` sends a `[PLAN REJECTED]` message back to the LLM and re-prompts for a new plan; typing `q` exits with code 0 and no tool execution; typing `e` prompts for a comment that is appended to the next LLM message.
3. A malformed plan (unknown tool name, schema-invalid input, > 5 steps, or duplicate adjacent steps) never reaches the user's approval prompt — it is rejected silently, the LLM is asked to retry, and only a valid plan is shown; after 2 retries the error is surfaced to the user.
4. `--plan --resume <id>` is a valid combination — the agent loads prior context and enters plan mode for the next turn.
5. `bench/run.sh --gate` exits 0 with the extended baseline (10 entries: 8 original T1-T7/W1/W2/B2 + 2 multi-turn MT_32b/MT_72b); no regression on the original 8.

**Plans:** 3 plans expected

Plans:
- [ ] 16-01-PLAN.md — Plan JSON parse wiring (`llmStepSchema` "plan" enum + `toLlmOutput` Plan branch handling all 4 PlanInvalid failure modes); `runPlanTurn` plan-mode entry point in AgentLoop with 2-retry validator-or-parse-failure path; PlanParseTests (≥6 cases) covering happy path + each PlanInvalid mode + retry behavior
- [ ] 16-02-PLAN.md — `PlanGate.fs` (Spectre-rendered numbered plan table + keystroke dispatch a/r/e/q via Console.ReadKey); `--plan` flag in CliArgs/CliOptions/Program.fs dispatch; reject re-prompt injection (`[PLAN REJECTED]`) and edit comment capture (`[PLAN EDIT NOTE: ...]`) mirror 09.1-05 loop-injection primitive; `--plan --resume <id>` valid combo; PlanGateTests (≥4 cases); live smoke for SC1/SC2/SC4
- [ ] 16-03-PLAN.md — Multi-turn bench fixtures (MT_32b + MT_72b validate PERSIST-01 end-to-end); `bench/baseline.json` 8→10 entries (originals byte-for-byte preserved); `bench/run.sh --gate` extended + verified 10/10 PASS; `documentation/bench.md` updated; AgentLoopTests gains one mocked plan-mode end-to-end test; plan-mode bench DEFERRED to v2.1+ (keystroke UX intractable for gate, documented rationale)

---

### Phase 17: Qwen 3.5 Evaluation

**Goal:** Decide whether Qwen 3.5 35B/122B replaces the current Qwen 2.5 32B/72B pair as the daily-driver pair, with empirical bench evidence on correctness AND latency.

**Depends on:** Nothing (orthogonal to v2.0's Persistence + Planning theme; user-driven model upgrade evaluation). **Should run BEFORE Phase 16** if model swap is desired before bench fixtures are baselined — Phase 16's `bench/baseline.json` entries (T6_32b, W1_32b, B2_72b, etc.) reference current model ids and would need re-baselining if 35B/122B becomes canonical.

**Requirements:** None new (operations + docs + comparison; no v2.0 REQ-IDs)

**Success Criteria** (what must be TRUE when Phase 17 completes):

1. `documentation/qwen35-install.md` exists with: model download instructions (HuggingFace ids + local path conventions), `mlx_lm.server` launchd plist templates for both 35B and 122B (mirrors `documentation/local-llm-services.md` shape), Base-vs-Instruct verification protocol (mirrors `documentation/qwen32b-base-to-instruct.md`), unified-memory budget calculation (35B + 122B vs 32B + 72B on 128GB Mac).
2. Old services (32B @ 8000, 72B @ 8001) safely unloaded via `launchctl unload` (NOT killed); new services (35B @ 8000, 122B @ 8001) loaded and confirmed responsive via `curl localhost:8000/v1/models` + `localhost:8001/v1/models`. Load tests captured: model-load wall-clock, peak RSS during cold start, prompt-cache size after first inference. **User help required** for the launchctl swap + model download wait.
3. `bench/run.sh --all` executed against new 35B/122B pair (~25 min). Results captured in JSONL; comparison written to `documentation/benchmark-32b-vs-72b.md` (Part 5: "v2.0 Phase 17 — Qwen 3.5 candidate eval") OR new sibling doc `documentation/benchmark-qwen35-eval.md`. Comparison includes: per-test step count delta, elapsed median delta, B2 diagnose accuracy, T6 dispatcher pattern, W1/W2 write-task convergence, any new regressions surfaced. Decision documented: KEEP current pair / SWITCH to 3.5 pair / SHIP BOTH (per-task routing).
4. If switch decision: `bench/baseline.json` re-baselined with new model ids; `tryParseModelId` heuristic verified against new path conventions; CLAUDE.md `## Runtime Environment` section updated. If keep-current decision: findings documented as "evaluated, deferred"; no code changes; Phase 16 plans remain valid as-is.
5. `bench/run.sh --gate` exits 0 against whichever pair becomes canonical (whether unchanged 32B/72B or new 35B/122B).

**Plans:** 3 plans

Plans:
- [ ] 17-01-PLAN.md — `documentation/qwen35-install.md`: install/usage docs for Qwen 3.5 35B + 122B (mirrors `local-llm-services.md` + `qwen32b-base-to-instruct.md` shape); autonomous; no service changes yet
- [ ] 17-02-PLAN.md — Service swap + load test (CHECKPOINT — `autonomous: false`): launchctl unload old / load new, model download wait (with user help), load-test capture (RSS, prompt-cache, response shape), Base-vs-Instruct verification on both new models
- [ ] 17-03-PLAN.md — Bench comparison: `bench/run.sh --all` on new pair, append findings to `documentation/benchmark-32b-vs-72b.md` Part 5, document decision (keep / switch / ship both); if switch decision, re-baseline `bench/baseline.json` and update CLAUDE.md

**Important ordering note:** Phase 17 should run BEFORE Phase 16 if you want bench fixtures to reflect the canonical model pair on first try. Phase 16 plans on disk remain valid for whichever model pair ends up canonical; if 35B/122B is chosen, Phase 16 needs re-planning with new model ids.

---

### Phase 18: Single-Model 122B Evaluation

**Goal:** Decide whether 35B can be dropped and 122B alone serves as the canonical model for blueCode, with empirical evidence on latency, quality, and memory across the bench's task spectrum. Single-model would collapse the dual-model `Router` to dead code, halve `bench/baseline.json`, free ~17 GB RSS, and reduce operational surface to one launchd plist.

**Depends on:** Phase 17 SWITCH (35B/122B already canonical; Phase 18 evaluates whether dropping 35B is viable). Orthogonal to Phase 16's `--plan` work.

**Requirements:** None new (operations + decision phase; no v2.0 REQ-IDs)

**Success Criteria** (what must be TRUE when Phase 18 completes):

1. `documentation/single-model-eval.md` exists (≥ 150 lines), contains "Decision". Per-test comparison table covers latency + step counts + diagnose accuracy for 122B-only vs Phase 17 dual-loaded baseline. Decision matrix has explicit pass/fail criteria, not "use judgment". Verdict named: **DROP-35B** / **KEEP-DUAL** / **CONDITIONAL** (e.g., drop only with explicit user opt-in).

2. 35B service was unloaded via `launchctl unload` (NOT killed) prior to bench run. Memory snapshot captured before/after unload: PhysMem used/free, Compressor delta, 122B RSS (mmap may expand to fill freed pages). 35B unload verified via `launchctl list | grep ohama` showing only `qwen122b` registered.

3. `bench/run.sh` (or a derived script in `scripts/`) executes the equivalent of `--all` against port 8001 only — every test routed to 122B regardless of `32b`/`72b` alias. 30+ invocations across regression/variance/diagnose/write phases. Results captured to `bench/runs/<ts>/`. No `LlmUnreachable` storms; all tests complete with exit codes recorded.

4. Latency thresholds applied per Decision criteria: T1/T2 (simple tasks) median ≤ 6s on 122B-only — currently 3-4s on 35B; doubling is the documented UX threshold. T6/W1/W2/B2 step counts match Phase 17 baseline (no degradation in routing pattern). B2 actual_diagnosis text preserved (DivByZero on empty list).

5. If verdict is **DROP-35B**: `documentation/single-model-eval.md` enumerates the architectural changes needed (Router collapse, baseline.json halve, bench script simplify, CLAUDE.md update) but does NOT execute them in this phase. A separate follow-up plan handles the code changes. If **KEEP-DUAL**: 35B is reloaded post-bench; findings recorded as "evaluated, deferred". If **CONDITIONAL**: documented opt-in mechanism (e.g., env var or CLI flag) sketched; full implementation OOS.

**Plans:** 3 plans

Plans:
- [ ] 18-01-PLAN.md — Service unload + memory profile (Wave 1, `autonomous: false` — 1 user checkpoint for `launchctl unload`): pre/post PhysMem + Compressor + 35B/122B RSS snapshots; 122B health verified post-unload via thinking-mode + JSON-schema smokes; ROADMAP §SC4 memory criteria evaluated; produces `18-01-MEMORY-PROFILE.md` (≥ 60 lines)
- [ ] 18-02-PLAN.md — 122B-only bench (Wave 2, autonomous — depends on 18-01): create `scripts/bench-122b-only.sh` (Option A — additive, leaves `bench/run.sh` untouched; uses `--model 72b` exclusively to route every invocation to port 8001); run `--all` mode (31 invocations, ≥ 30 per ROADMAP §SC3, ~25-35 min); capture per-test elapsed + step counts + B2 diagnosis quote + post-bench RSS; produces `18-02-BENCH-RESULTS.md` (≥ 80 lines)
- [ ] 18-03-PLAN.md — Decision + write-up (Wave 3, `autonomous: false` — 1 conditional user checkpoint for `launchctl load -w` on KEEP-DUAL/CONDITIONAL verdicts; checkpoint short-circuits on DROP-35B; depends on 18-02): apply 5 ROADMAP §SC4 criteria mechanically; name verdict (DROP-35B/KEEP-DUAL/CONDITIONAL); document 35B reload procedure (mandatory user-checkpoint on KEEP-DUAL/CONDITIONAL, recommended-but-optional on DROP-35B); enumerate (but do NOT execute) architectural follow-ups (Router collapse, baseline halve, CLAUDE.md update); update STATE.md; produces `documentation/single-model-eval.md` (≥ 150 lines)

**Reversibility note:** Phase 18 makes ZERO permanent changes if verdict is KEEP-DUAL — just unload + bench + reload. The architectural changes (Router collapse etc.) are deferred to a follow-up phase regardless of verdict, so this phase is safe to run.

---

### Phase 19: Qwen 2.5 Retirement + 122B Single-Model Default

**Goal:** Make 122B the sole canonical runtime. Physically retire Qwen 2.5 32B/72B from disk + launchd (~55 GB reclaimed). Preserve Qwen 3.5 35B on disk as cold rollback asset (unloaded by default; reloadable for future dual mode if 122B latency becomes painful). Halve `bench/baseline.json` to single-model. Remove old CLI aliases entirely. Add explicit `--with-35b` opt-in flag for future dual mode.

**Depends on:** Phase 18 ✓ (DROP-35B verdict is the justification for permanent retirement)

**Requirements:** None new (operations + architectural cleanup; no v2.0 REQ-IDs)

**Key architectural decisions:**

- **A. 35B model files preserved** — `~/llm-system/models/qwen35b/` (~17 GB) stays on disk to support user's "future dual mode if 122B too slow" intent. Only Qwen 2.5 (32B + 72B, ~55 GB) is deleted.
- **B. CLI aliases `--model 32b` and `--model 72b` REMOVED entirely** (breaking change). Canonical: `--model 122b` (default with no flag). Dual-mode addressable: `--model 35b` (only valid when 35B is loaded AND `--with-35b` flag set). Old aliases produce a clear error pointing to migration.
- **C. Future dual-mode requires BOTH:** (1) `launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen35b.plist` to bring 35B service back, AND (2) `--with-35b` CLI flag to explicitly opt blueCode into dual routing. Service-load alone does NOT change blueCode behavior — flag is the explicit opt-in. Default behavior stays predictable even if 35B is loaded for unrelated reasons.
- **D. Bench harness consolidation:** `scripts/bench-122b-only.sh` is absorbed into `bench/run.sh` (in-place rewrite). The single-script becomes the canonical single-model bench. `bench/baseline.json` halves to single-model (`_122b` keys only). `scripts/bench-122b-only.sh` is deleted after absorption.

**Success Criteria** (what must be TRUE when Phase 19 completes):

1. `launchctl list | grep ohama` shows ONLY `com.ohama.qwen122b`. `~/llm-system/models/` contains ONLY `qwen35b/` and `qwen122b/`. `~/Library/LaunchAgents/` has ONLY `com.ohama.qwen{35b,122b}.plist`. Disk reclaimed ≥ 50 GB (verified by `df -h ~/` before/after).

2. `bench/run.sh --gate` exits 0 against single-model 122B baseline (recomputed in this phase). `scripts/bench-122b-only.sh` no longer exists. `bench/baseline.json` contains only `_122b` keys (no `_32b`, `_72b`, or `_35b` keys).

3. `blueCode --model 32b "..."` and `blueCode --model 72b "..."` produce a clear error referencing Phase 19 retirement and pointing to `--model 122b` as the canonical alias. `blueCode "..."` (no flag) defaults to 122B and works. `blueCode --model 122b "..."` works. `blueCode --model 35b "..."` errors with a clear "35B not loaded" message when 35B service is absent; works when both `launchctl load -w` is in effect AND `--with-35b` is passed.

4. `tryParseModelId` rejects `qwen32b`/`qwen72b` paths as `PathRetired` error (or equivalent named variant); recognizes `qwen35b`/`qwen122b` paths.

5. `CLAUDE.md` `## Runtime Environment` section reflects single-model 122B default; documents the dual-mode reactivation procedure (launchctl load + `--with-35b`); no Qwen 2.5 references remain. `documentation/qwen35-install.md` reframes 35B as standby/rollback asset (status badge updated). `documentation/single-model-eval.md` §7 cross-references this phase as the execution of the conditional follow-ups.

6. All 258-264/1/0 tests pass (range reflects +4-7 net new test cases added in 19-02 Tasks 2 + 7: 4 ModelsProbeTests for `validateModelPath`, 2-3 net CliArgsTests for retirement errors + `--with-35b` parsing); no regression on `bench/run.sh --gate`.

**Plans:** 2 plans expected

Plans:
- [ ] 19-01-PLAN.md — Retire Qwen 2.5 + disk reclamation (autonomous: false; user checkpoint required for `rm` of model files + launchd plists; cannot be automated). Captures `df -h ~/` before/after; verifies launchctl + filesystem state; produces `19-01-RETIREMENT.md` (≥ 40 lines, inventory + reclaim metrics + remaining-file map)
- [ ] 19-02-PLAN.md — Code + bench + docs alignment (autonomous: true). Argu CLI: remove `--model 32b`/`72b`, add canonical `--model 122b`, add `--with-35b` BoolFlag (opt-in for dual mode). `tryParseModelId` retirement guard (`PathRetired` error variant). `Router.fs` flag-gated dual probe (default 122B-only; with `--with-35b`, also probes port 8000). `bench/run.sh` rewrite (absorbs `scripts/bench-122b-only.sh`; uses `--model 122b` exclusively). `bench/baseline.json` halve + recompute single-model baselines. `CLAUDE.md` `## Runtime Environment` update. `documentation/qwen35-install.md` reframe (35B → standby). `documentation/single-model-eval.md` §7 cross-reference. Verifies 254/1/0 tests + `bench/run.sh --gate` 0 exit.

**Reversibility:** 35B is recoverable instantly (`launchctl load -w` + `--with-35b`) — model files preserved on disk. Qwen 2.5 retirement is irreversible without re-download (~50 GB), which is intended — the deletion is the point.

---

## Progress

| Phase | Milestone | Requirements | Plans Complete | Status | Completed |
|-------|-----------|--------------|----------------|--------|-----------|
| 14. Domain Extensions | v2.0 | PERSIST-01, PLAN-01, PLAN-04 | 2/2 | ✓ Complete | 2026-04-26 |
| 15. Persistence Wiring | v2.0 | PERSIST-01, PERSIST-02, PERSIST-03, PERSIST-04 | 3/3 | ✓ Complete | 2026-04-27 |
| 16. Planning Wiring + Bench | v2.0 | PLAN-02, PLAN-03, PLAN-04 (wiring) | 0/3 | Not started (plans on disk) | - |
| 17. Qwen 3.5 Evaluation | v2.0 | (none — operations) | 3/3 | ✓ Complete (SWITCH) | 2026-04-27 |
| 18. Single-Model 122B Eval | v2.0 | (none — operations + decision) | 3/3 | ✓ Complete (DROP-35B) | 2026-04-27 |
| 19. Qwen 2.5 Retirement + 122B Default | v2.0 | (none — operations + cleanup) | 2/2 | ✓ Complete | 2026-04-27 |

---

*Roadmap created: 2026-04-26*
*Last updated: 2026-04-27 — Phase 19 complete (85 GB reclaimed; single-model 122B canonical; 6/6 gate PASS; 262/1/0 tests; --with-35b opt-in flag added)*
