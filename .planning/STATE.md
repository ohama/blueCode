# Project State

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-04-28 after v2.3 milestone scoped)

**Core value:** Mac 로컬 Qwen 3.5 122B를 strong-typed F# agent loop로 **empirically** 안정적으로 돌린다 (post-v2.2 verdict 87/100 KEEP; v2.3 unlocks multi-file refactor end-to-end via comprehension-layer intervention; single-model canonical; 35B retained as cold rollback via `--with-35b`)
**Current focus:** v2.3 Comprehension Layer — COMPLETE. 4 phases (24/25/26/27) delivered multi-prong intervention (P1 system prompt + P2 few-shot + P3 plan-mode pre-flight enumeration + Phase 27 P1 migration to defaultSystemPrompt). CORR-EVAL-02 PASS empirically → Total 87 → 92/100 KEEP. v2.3 milestone close-ready.

## Current Position

Milestone: v2.3 Comprehension Layer (started 2026-04-28; scoped from v2.2 audit's COMP-BIAS-01 data-driven first candidate)
Phase: 27 complete (v2.3 milestone close-ready)
Plan: 3 of 3 complete (Phase 27)
Status: Phase 27 complete + verified. CORR-EVAL-02 re-run PASS (orphan_count=0) after Plan 27-01 migrated P1 enumeration directive from planSystemPromptSuffix into defaultSystemPrompt (closing the architectural gap exposed by Phase 26 BLOCKED) + Plan 27-02 launchctl kickstart pre-flight cleared KV cache contamination. v2.3 multi-prong intervention (P1+P2+P3) empirically validated. Eval doc verdict 87→92 across 11 edit sites. Bench gate 7/7 PASS post-eval (EXIT trap restored fixtures). v2.3 milestone close-ready.
Last activity: 2026-04-28 — Phase 27 verified (CORR-EVAL-02 PASS orphan_count=0; bench gate 7/7 PASS; eval doc 87→92). v2.3 milestone close-ready.

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 ✓ → v1.4 ✓ → v2.0 ✓ → v2.1 ✓ → v2.2 ✓ → v2.3 ✓ (Phase 24: 2/2 done ✓; Phase 25: 3/3 done ✓; Phase 26: BLOCKED → superseded by Phase 27; Phase 27: 3/3 done ✓)

## Performance Metrics (cumulative, frozen)

**v1.0:** 5 phases, 17 plans, 208 tests, 5891 LOC F#, 85 commits, ~27h
**v1.1:** 2 phases, 5 plans, 218 tests (+10), +315/-124 LOC, 23 commits, ~19h
**v1.2:** 3 phases, 8 plans, 242 tests (+24), Core diff confined to AgentLoop.fs/Domain.fs, 43 commits, ~3 days
**v1.3:** 2 phases, 6 plans, 243 tests (+1), bench harness in repo + 54% prompt shrink + B2 recovery, 25 commits, ~1 day
**v1.4:** 2 phases, 2 plans, 243 tests (unchanged), zero src/ diff, 7 commits, ~1 day
**v2.0:** 7 phases, 19 plans, 282 tests (+39), 41 files +3993/-335 LOC, 106 commits, ~2 days, -85 GB disk (Qwen 2.5 retirement); bench gate trajectory 8/8→6/6→7/7
**v2.1:** 1 phase (single), 5 plans, 282 tests (unchanged — observational), 31 files +6463/-26 LOC, ~25 commits, ~1.5 days; verdict 82/100 KEEP; bench gate 7/7 PASS preserved; zero src/ diff
**v2.2:** 2 phases (22, 23), 5 plans (4+1), 284 tests (+2 boundary), 27 files +3569/-81 LOC, 19 commits, ~1 day; verdict 82→87/100 KEEP (Performance 20→25 via cold-start +5; Correctness stays 31/40 because CORR-EVAL-02 FAIL x2); bench gate 7/7 PASS preserved; src/ diff confined to 5 files (PlanValidator/AgentLoop/Domain/CompositionRoot/Rendering)

Detailed per-plan history archived in `.planning/milestones/v{1.0,1.1,1.2,1.3,1.4,2.0,2.1,2.2}-phases/`.

## Accumulated Context

### Decisions

Cumulative log in `.planning/PROJECT.md` Key Decisions table. See PROJECT.md for outcomes through v2.0.

Stable patterns established across milestones (load-bearing for next session):

- **Core purity** — `src/BlueCode.Core/**` no Serilog/Spectre/Argu/HttpClient/file I/O; `task {}` only (CI-enforced via `scripts/check-no-async.sh`).
- **Test discovery** — explicit `rootTests` list in `RouterTests.fs` + ordered `<Compile Include>` in `BlueCode.Tests.fsproj`. New test modules need BOTH places. Preserve all existing entries on each addition.
- **Canonical test runner** — `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` (NOT `dotnet test`).
- **Bench gate** — `bash bench/run.sh --gate` is the structural authority. Currently 7/7 PASS with single-model 122B baseline (T6_122b, T5_122b, B2_122b, T1_122b, W1_122b, W2_122b, MT_122b).
- **Atomic commits** — `{type}({phase}-{plan}): {name}` per task; plan-meta separate; phase-complete bundles ROADMAP/STATE/REQUIREMENTS/VERIFICATION. NEVER `git add -A` or `git add .`.
- **Loop-injection primitive** — `lastEditPath` + `lastReadHint` in `runLoop`; post-user `Role = User` System-style messages enforce tool-terminality at conversation-history layer. Established in v1.2 (TLX-01/9.1-05) and extended in v1.3 (PERF-02). Phase 16's `[PLAN REJECTED]` re-prompt follows the same pattern.
- **Role = User invariant** (Phase 17-02 + Phase 20-03) — Qwen 3.5 122B REJECTS mid-conversation `Role = System` with HTTP 404. ALL mid-conversation injections (POST-EDIT CONSTRAINT, POST-READ HINT, [PLAN REJECTED], [PARSE ERROR] correction) MUST be `Role = User`. Authority signal is in the bracketed text marker, NOT the role. Documented at AgentLoop.fs:249/260/266 + `scripts/probe-system-role.sh` + 3 howto files.
- **Single-model 122B canonical** (Phase 19) — `blueCode "..."` defaults to 122B with no flag. `--model 32b/72b` retired (exit 2 with retirement error). `--model 35b` requires `--with-35b` opt-in flag AND 35B service loaded. Default invocation never probes port 8000.
- **Persistence** (v2.0) — `~/.bluecode/sessions/<id>.jsonl` (per-turn TurnComplete envelopes; `version: 2` header). v1 per-step crash log at `~/.bluecode/session_<ts>.jsonl` coexists. `--resume <id>` and `--new-session` Argu flags; mutually-exclusive validation post-parse.
- **Plan-then-execute** (v2.0) — `--plan` flag enables plan-mode (single-turn only; `--plan --resume <id>` valid; `--plan --with-35b` rejected). `runPlanTurn` returns `Task<Result<Plan, AgentError>>` with 2-attempt retry. PlanGate.fs renders Spectre table + a/r/e/q dispatch via `IKeyReader` port. PlanValidator (3 structural rules in Core) + JSON parse layer (4th schema-invalid rule in Cli) catch all malformed plans before user sees approval prompt.
- **10-step ceiling (22-01)** — `Plan.Steps.Length ≤ 10` (PLAN-04 raised from 5). PlanValidator.MaxPlanSteps=10 and CompositionRoot bootstrap MaxLoops=10 are independent constants (Option 1 preserved). The 5-step structural block on multi-file refactor is removed.
- **Independent constants pattern confirmed** — PlanValidator.MaxPlanSteps and AgentConfig.MaxLoops remain separate values (not merged into AgentConstants.fs). Rationale: PlanValidator is invoked from QwenHttpClient parse layer without AgentConfig in scope (Phase 16 design invariant).
- **Usage guidance clause (22-02)** — `planSystemPromptSuffix` updated to "1-10 steps. Use the minimum steps needed; reserve the full budget only for tasks requiring reads across multiple files before editing." First variant held without iteration. T6 used 5/5 steps (at baseline_max; no regression). Suffix char count: 695 chars (≤ 900 budget). `defaultSystemPrompt` and `Role = User` invariant unchanged.
- **CORR-EVAL-02 persistent bias (22-04 double-FAIL)** — Two independent CORR-EVAL-02 runs both FAIL with orphan_count=1. Agent has a persistent extraction bias toward `add3→sum3`, ignoring `add→sum`. README rewrite (Option A, 2026-04-28) did not fix the bias — step-5 thought was textually identical across both runs. Next resolution path: system prompt guidance for multi-file refactors or fixture redesign. Eval doc remains 82/100 KEEP.
- **P1 enumeration directive (24-01)** — `planSystemPromptSuffix` extended from 695→879 chars with blank-line-separated paragraph: "When the task requires renaming or restructuring multiple symbols, list ALL targets explicitly in your thought before editing. Do not start editing until the full list is enumerated." Directive is plan-mode-only (no impact on agent-loop path). Bench gate 7/7 PASS preserved. COMP-01 DONE.
- **P2 few-shot example (24-02)** — `planSystemPromptSuffix` extended from 879→1183 chars with 3-line Example/Targets/Steps block demonstrating the exact `add->sum` AND `add3->sum3` shared-prefix rename failure pattern. One blank line after P1 directive; all three lines left-flush (column 0). Bench gate 7/7 PASS preserved. COMP-02 DONE. Phase 24 complete.
- **Interpretation B (25-01)** — `PlanInvalid of detail: string` in Domain.fs is NOT modified. Fourth pre-flight pass `checkRenameTargetsEnumerated` encodes missing-target failures as structured detail string `"rename targets not enumerated: NAME1, NAME2"`. Avoids compile cascade into Rendering.fs + AgentLoop.fs:501 `buildCorrection`. Smaller diff; same observable LLM-correction behavior. `validatePlan` signature extended to `userPrompt: string -> Plan -> Result<Plan, AgentError>`. COMP-03 P3 prong in place.
- **Phase 25 / Interpretation B confirmed (25-03)** — `PlanInvalid of detail: string` extended via structured sub-reason "rename targets not enumerated: ..." rather than new DU case. Domain.fs/Rendering.fs/buildCorrection all unchanged across entire Phase 25 (git diff empty, verified 2026-04-29). Avoids compile cascade across Rendering.fs and AgentLoop.buildCorrection. Same observable behavior at LLM correction boundary; smaller diff. Bench gate 7/7 PASS; test count 287. COMP-03 + COMP-04 Phase 25 portion complete.
- **F# big-bang atomic commit (25-01)** — Tasks 1+2+3 committed as one atomic unit (PlanValidator.fs + AgentLoop.fs:484 + PlanValidatorTests.fs 6 mechanical updates). No valid intermediate build state; mirrors v1.1 LlmResponse Phase 7 pattern.
- **Phase 26 BLOCKED — CORR-EVAL-02 FAIL x3 with hallucination failure mode (26-01, 2026-04-29)** — Re-run with all 3 v2.3 prongs in production produced FAIL on all 3 stochastic attempts. New failure mode vs v2.2: agent misread README as "subtract function task" (hallucination), never attempted rename. In v2.2, extraction bias produced partial rename (add3→sum3 only). In Phase 26, complete task hallucination (no rename whatsoever). Critical structural gap exposed: P1 and P2 are confined to `planSystemPromptSuffix` which is ONLY sent in plan-mode (`--plan` flag). P3 (PlanValidator) is also plan-mode-only. Eval harness runs `blueCode --verbose --model 122b` WITHOUT `--plan`. Neither P1 nor P2 nor P3 were in the effective system prompt during the eval. v2.4 must choose: (a) move P1/P2 enumeration guidance to `defaultSystemPrompt` (applies to agent-loop path; regression risk), OR (b) redesign eval harness to use `--plan` mode (P3 then active; different evaluation semantics), OR (c) redesign fixture to be unambiguous even without guidance. Extraction bias may also warrant `launchctl kickstart` before eval to clear KV cache contamination. Eval doc remains 87/100 KEEP. v2.4 investigation required.
- **Phase 27 P1 migration to defaultSystemPrompt + CORR-EVAL-02 PASS empirical (27-01 + 27-02 + 27-03)** — Closed the architectural gap surfaced by Phase 26 BLOCKED: P1+P2+P3 were all plan-mode-only (planSystemPromptSuffix is `--plan`-only; PlanValidator runs only in runPlanTurn) but the eval harness invokes blueCode without `--plan`. Plan 27-01 migrated the 182-char P1 enumeration directive from planSystemPromptSuffix → defaultSystemPrompt (defaultSystemPrompt 783→967 chars; suffix 1183→999 chars; combined plan-mode invariant 1968 chars). Plan 27-02 ran `launchctl kickstart -k gui/501/com.ohama.qwen122b` pre-flight (cleared KV cache contamination, the second failure mode discovered in Phase 26 Diagnostic D) + warmup probe + CORR-EVAL-02 re-run; PASS (orphan_count=0; RUN_TS=20260429-105907). Plan 27-03 applied the 11 eval doc edit sites flipping verdict 87→92, updated STATE/ROADMAP/REQUIREMENTS, and ran the mandatory final bench gate 7/7 PASS. v2.3 multi-prong intervention (P1+P2+P3) empirically validated end-to-end. Eval doc 87→92 KEEP preserved by wider margin (Correctness 31/40→36/40). v2.3 milestone close-ready.

### Roadmap Evolution

- **2026-04-29:** Phase 27 added (Default-Prompt P1 Migration + Re-Eval) inside v2.3 milestone. Closes architectural gap from Phase 26 BLOCKED. COMP-05/06 reassigned from Phase 26 to Phase 27. Phase 26 retained as historical BLOCKED record.
- **2026-04-28:** Phase 27 complete. v2.3 milestone close-ready (3/3 phases ✓ ignoring Phase 26 superseded; 6/6 requirements ✓; bench gate 7/7 PASS preserved through all 4 phase attempts).

### Pending Todos (v2.1 candidates)

For awareness only — DO NOT auto-pull. v2.1 scope comes from observation window `/gsd:add-todo` entries, not this list:

- **Compaction** — PERSIST-02 saves full session JSONL; long sessions hit 80% context warning faster
- **Slash commands** (`/sessions`, `/plan`, `/clear`) — UX layer over CLI flags
- **Sub-agent delegation** — Now meaningful since memory + planning land
- **Plan-mode bench fixture** — Deferred from Phase 16-03; mocked-IKeyReader pattern would substitute
- **Thinking-mode-on** — Consume `<think>` blocks; requires `max_tokens` 1024→2048-4096 + re-bench
- **Native OpenAI `tool_calls`** — Replaces custom JSON schema; rewrites `toLlmOutput` + all bench fixtures
- **Streaming output (STM-01)** — Deferred 7th cycle
- **Session listing UI** (`--list-sessions` or `/sessions`) — `ls ~/.bluecode/sessions/` sufficient for v2.0
- **Branching/forking sessions** — Single-resume only in v2.0
- **Session compaction / pruning** — Manual `rm -rf` for now

### Blockers/Concerns

**~~CORR-EVAL-02 FAIL x2 (confirmed 2026-04-28); Phase 26 BLOCKED (2026-04-29):~~** **RESOLVED in Phase 27 (2026-04-28)** — see Empirical Baselines below. Original FAIL evidence + Phase 26 BLOCKED diagnostic preserved as historical context.

- **Attempt 1 (v2.2, 10-step ceiling):** Agent used 8/10 steps. Step-5 thought: "Rename 'add3' to 'sum3'" — missed `add → sum`. Same as v2.1 FAIL.
- **Attempt 2 (README rewrite Option A):** README rewritten to 2128 chars with explicit numbered rename sections, checklist, and warning. Agent read the new README (confirmed: 2128 chars). Step-5 thought: IDENTICAL to attempt 1. Extraction bias persists through README changes.

**Phase 26 re-run evidence (2026-04-29) — NEW FAILURE MODE:** 3 more attempts, all FAIL. But the failure mode has qualitatively changed: agent now HALLUCINATED the task entirely. Step-5 thought (all 3 attempts, textually identical): "Based on the README instructions (which mentioned adding a `subtract` function...)" — README does NOT mention subtract. Agent added a subtract function instead of renaming add→sum and add3→sum3. Complete task hallucination, not extraction bias.

**Critical structural gap (Phase 26 diagnosis):** P1 enumeration directive and P2 few-shot examples are in `planSystemPromptSuffix`, which is ONLY sent during `--plan` mode invocations. P3 PlanValidator is also plan-mode-only. The eval harness runs `blueCode --verbose --model 122b` WITHOUT `--plan`. NONE of the three v2.3 prongs reached the agent during CORR-EVAL-02.

**Root cause (updated for Phase 26):** Multi-prong intervention (P1+P2+P3) was architecturally scoped to plan-mode. CORR-EVAL-02 eval harness uses agent-loop mode. The intervention did not reach the eval path. Additionally, a possible KV cache contamination issue may have changed the failure mode from extraction-bias to full hallucination.

**Options for resolution (v2.4+):**
1. ~~Rewrite README~~ — ATTEMPTED AND FAILED (2026-04-28, orphan_count=1 on second attempt)
2. ~~P1+P2+P3 plan-mode intervention~~ — ATTEMPTED AND FAILED (2026-04-29, Phase 26 — intervention never reached eval path; agent-loop mode bypasses planSystemPromptSuffix entirely)
3. ~~**v2.4 candidate A**: Move P1/P2 enumeration guidance to `defaultSystemPrompt`~~ — **DELIVERED in Phase 27** (Plan 27-01 migrated P1 only; P2 stays plan-mode per Phase 27 ROADMAP guardrails; bench gate 7/7 PASS preserved post-migration; CORR-EVAL-02 PASS confirmed in Plan 27-02)
4. **v2.4 candidate B**: Redesign eval harness to use `--plan` mode (P3 then active; different semantics; `--plan` requires interactive approval gate — eval harness would need non-interactive plan-mode)
5. **v2.4 candidate C**: Redesign fixture to avoid shared-prefix ambiguity entirely
6. **v2.4 candidate D**: `launchctl kickstart` before eval to clear KV cache; test if hallucination is session-contamination artifact
7. ~~Accept 87/100~~ — still at pre-v2.3 88; user may reconsider scope

Documentation drift items flagged in v2.0 audit (non-blocking, archived):
- Phase 20 missing formal `20-VERIFICATION.md` (per-plan SUMMARYs substitute)
- CLAUDE.md Key Seams has 1 stale "72B" sentence (historical)
- `documentation/` retains legacy 32B/72B install docs (kept as historical reference)
- eval doc §2.4 FAIL description says "5-step budget exhausted" — partially inaccurate for v2.2 run (10-step budget, 8 steps used); correct FAIL verdict and 0/5 score

## Session Continuity

Last session: 2026-04-28 (Phase 27 complete + verified; v2.3 milestone close-ready)
Stopped at: Phase 27 complete. 27-01 migrated P1 to defaultSystemPrompt; 27-02 CORR-EVAL-02 PASS empirically (orphan_count=0; RUN_TS=20260429-105907); 27-03 eval doc 87→92 + bench gate 7/7 PASS + STATE/ROADMAP/REQUIREMENTS updated + 27-VERIFICATION.md + 27-03-SUMMARY.md + phase-complete commit.
Resume file: None
Next workflow trigger: `/gsd:complete-milestone v2.3` — v2.3 milestone close-ready. All 6/6 requirements ✓; bench gate 7/7 PASS; eval doc 92/100 KEEP.

## Empirical Baselines (post-v2.1, load-bearing for v2.2 scoping)

These are the measured baselines from v2.1. Use them as input when scoping v2.2 candidates.

- **HumanEval+ chat pass@1 = 0.939 / pass@1+ = 0.902** — re-evaluation trigger if mlx_lm.server major version, Qwen 3.5 model card update / YaRN config change, or sampling defaults change
- **Throughput median 34.6 tok/s; TTFT median 222 ms warm** — interactive UX baseline
- **Schema rate 0/50 InvalidJsonOutput** — perfect compliance; v2.0 architecture decisions (strict JSON schema + 2-attempt retry + 5-step loop guard + thinking-mode-off) validated under stricter stress
- **Multi-turn coherence stable through N=7** — refutes mlx-lm#1011 "approximately 5 rounds" community claim in our environment
- **Needle 4/4 at max_model_len=32768** — mlx_lm.server does not expose YaRN-extended ceiling in /v1/models; 32k is the conservative working assumption
- **10-step PLAN-04 ceiling (was 5)** — raised in 22-01; CORR-EVAL-02 FAIL structural block resolved (ceiling no longer the constraint). v2.2 re-run attempt 1 (22-04, 2026-04-28): FAIL, orphan_count=1, agent used 8/10 steps — comprehension failure. v2.2 re-run attempt 2 (README Option A rewrite): FAIL, orphan_count=1, agent read new 2128-char README but produced identical step-5 miscomprehension. Persistent extraction bias toward add3→sum3; base add→sum rename ignored. **Phase 26 (2026-04-29): BLOCKED → superseded by Phase 27.** Phase 26 BLOCKED diagnosis surfaced architectural gap: P1/P2/P3 plan-mode-only; eval harness uses agent-loop. **Phase 27 (2026-04-28): RESOLVED.** Plan 27-01 migrated P1 from planSystemPromptSuffix into defaultSystemPrompt (defaultSystemPrompt 783→967 chars; suffix 1183→999; combined plan-mode invariant 1968 chars; bench gate 7/7 PASS preserved). Plan 27-02 ran `launchctl kickstart -k gui/501/com.ohama.qwen122b` pre-flight (cleared KV cache contamination — second failure mode discovered in Phase 26 Diagnostic D) + warmup probe + CORR-EVAL-02 re-run; PASS (orphan_count=0; RUN_TS=20260429-105907). Plan 27-03 applied 11 eval doc edit sites (87→92) + final bench gate 7/7 PASS. Eval doc 92/100 KEEP. v2.3 multi-prong intervention empirically validated end-to-end.
- **Coding-quality 6/10 (idiomatic F# 1/5)** — generated F# is correct but procedural; pipelines / DU / pattern matching usage is low. Observation window will determine if this becomes a v2.2 candidate (system prompt F# style hint? few-shot?)

For full per-section results, see `documentation/qwen35-122b-coding-eval.md`. Per-plan execution history archived in `.planning/milestones/v2.1-phases/`.
