# Project State

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-04-28 after v2.3 milestone scoped)

**Core value:** Mac 로컬 Qwen 3.5 122B를 strong-typed F# agent loop로 **empirically** 안정적으로 돌린다 (post-v2.2 verdict 87/100 KEEP; v2.3 unlocks multi-file refactor end-to-end via comprehension-layer intervention; single-model canonical; 35B retained as cold rollback via `--with-35b`)
**Current focus:** v2.3 Comprehension Layer — 3 phases (24/25/26) covering multi-prong intervention (P1 system prompt + P2 few-shot + P3 plan-mode pre-flight enumeration). Goal: CORR-EVAL-02 PASS → Total 87 → 92/100.

## Current Position

Milestone: v2.3 Comprehension Layer (started 2026-04-28; scoped from v2.2 audit's COMP-BIAS-01 data-driven first candidate)
Phase: 25 (Plan-Mode Pre-Flight Enumeration P3 architectural) — in progress
Plan: 1 of 3 complete (Phase 25); Plans 25-02 + 25-03 follow
Status: Phase 24 complete + verified. Phase 25 Plan 1 complete (25-01): checkRenameTargetsEnumerated added to PlanValidator; validatePlan signature extended; 284 tests passing; atomic commit c933ffc. Ready for 25-02 (new test cases).
Last activity: 2026-04-29 — Completed 25-01-PLAN.md: checkRenameTargetsEnumerated pre-flight pass; Interpretation B (no DU change); F# big-bang atomic commit c933ffc (3 files); 284/284 tests pass; Core purity + async check pass.

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 ✓ → v1.4 ✓ → v2.0 ✓ → v2.1 ✓ → v2.2 ✓ → v2.3 ◆ (Phase 24: 2/2 done; Phase 25: 1/3 done; Phase 26 follows)

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
- **F# big-bang atomic commit (25-01)** — Tasks 1+2+3 committed as one atomic unit (PlanValidator.fs + AgentLoop.fs:484 + PlanValidatorTests.fs 6 mechanical updates). No valid intermediate build state; mirrors v1.1 LlmResponse Phase 7 pattern.

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

**CORR-EVAL-02 FAIL x2 (confirmed 2026-04-28):** Two independent attempts, both FAIL with orphan_count=1.

- **Attempt 1 (v2.2, 10-step ceiling):** Agent used 8/10 steps. Step-5 thought: "Rename 'add3' to 'sum3'" — missed `add → sum`. Same as v2.1 FAIL.
- **Attempt 2 (README rewrite Option A):** README rewritten to 2128 chars with explicit numbered rename sections, checklist, and warning. Agent read the new README (confirmed: 2128 chars). Step-5 thought: IDENTICAL to attempt 1. Extraction bias persists through README changes.

**Root cause (updated):** Model has a **persistent extraction bias** toward `add3 → sum3`. The base `add` function is treated as canonical and not flagged as a rename target despite explicit README instruction. This is a model knowledge/attention pattern, not a README clarity gap. Option A (README rewrite) is now closed as FAILED.

**Options for resolution:**
1. ~~Rewrite README~~ — ATTEMPTED AND FAILED (2026-04-28, orphan_count=1 on second attempt)
2. **IN PROGRESS (v2.3)** — P1 system prompt enumeration directive DONE (24-01). P2 few-shot examples next (24-02). Full re-eval at COMP-05 (Phase 26).
3. Redesign fixture to avoid ambiguity (fallback if P1+P2+P3 all fail)
4. ~~Accept 82/100~~ — superseded by user decision to pursue v2.3

Documentation drift items flagged in v2.0 audit (non-blocking, archived):
- Phase 20 missing formal `20-VERIFICATION.md` (per-plan SUMMARYs substitute)
- CLAUDE.md Key Seams has 1 stale "72B" sentence (historical)
- `documentation/` retains legacy 32B/72B install docs (kept as historical reference)
- eval doc §2.4 FAIL description says "5-step budget exhausted" — partially inaccurate for v2.2 run (10-step budget, 8 steps used); correct FAIL verdict and 0/5 score

## Session Continuity

Last session: 2026-04-29 (Phase 25 Plan 1 complete)
Stopped at: Completed 25-01-PLAN.md. Atomic commit c933ffc + plan-meta 0768e43. 284 tests passing.
Resume file: None
Next workflow trigger: `/gsd:execute-phase 25` for 25-02 (new PlanValidator test cases for checkRenameTargetsEnumerated)

## Empirical Baselines (post-v2.1, load-bearing for v2.2 scoping)

These are the measured baselines from v2.1. Use them as input when scoping v2.2 candidates.

- **HumanEval+ chat pass@1 = 0.939 / pass@1+ = 0.902** — re-evaluation trigger if mlx_lm.server major version, Qwen 3.5 model card update / YaRN config change, or sampling defaults change
- **Throughput median 34.6 tok/s; TTFT median 222 ms warm** — interactive UX baseline
- **Schema rate 0/50 InvalidJsonOutput** — perfect compliance; v2.0 architecture decisions (strict JSON schema + 2-attempt retry + 5-step loop guard + thinking-mode-off) validated under stricter stress
- **Multi-turn coherence stable through N=7** — refutes mlx-lm#1011 "approximately 5 rounds" community claim in our environment
- **Needle 4/4 at max_model_len=32768** — mlx_lm.server does not expose YaRN-extended ceiling in /v1/models; 32k is the conservative working assumption
- **10-step PLAN-04 ceiling (was 5)** — raised in 22-01; CORR-EVAL-02 FAIL structural block resolved (ceiling no longer the constraint). v2.2 re-run attempt 1 (22-04, 2026-04-28): FAIL, orphan_count=1, agent used 8/10 steps — comprehension failure. v2.2 re-run attempt 2 (README Option A rewrite): FAIL, orphan_count=1, agent read new 2128-char README but produced identical step-5 miscomprehension. Persistent extraction bias toward add3→sum3; base add→sum rename ignored. Eval doc stays 82/100.
- **Coding-quality 6/10 (idiomatic F# 1/5)** — generated F# is correct but procedural; pipelines / DU / pattern matching usage is low. Observation window will determine if this becomes a v2.2 candidate (system prompt F# style hint? few-shot?)

For full per-section results, see `documentation/qwen35-122b-coding-eval.md`. Per-plan execution history archived in `.planning/milestones/v2.1-phases/`.
