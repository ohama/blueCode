# Project State

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-04-29 after v2.3 milestone shipped)

**Core value:** Mac 로컬 Qwen 3.5 122B를 strong-typed F# agent loop로 **empirically** 안정적으로 돌린다 (post-v2.3 verdict 92/100 KEEP; v2.4 measurement-first: F# fixture scoring 13/15 → 5/5 → passed_disprove_1of5; §5 1/5→5/5; total 92→96)
**Current focus:** v2.4 Coding Quality milestone (started 2026-04-29) — Phase 28 COMPLETE (passed_disprove_1of5; Phase 29 SKIPPED). Proceed to Phase 30 milestone close via `/gsd:plan-phase 30`.

## Current Position

Milestone: v2.4 Coding Quality (started 2026-04-29; data-driven from v2.2/v2.3 audit's Coding-quality 1/5 hedge)
Phase: 28 complete (passed_disprove_1of5; Phase 29 SKIP)
Plan: 06 of 06 complete (all plans done)
Status: Phase 28 complete; Phase 29 SKIPPED; Phase 30 defining
Last activity: 2026-04-29 — Phase 28 verified; F# fixture rescore disproves 1/5 hedge; total 92→96; Phase 29 SKIP per disprove path (mapped_score 5/5 ≥ 3)

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 ✓ → v1.4 ✓ → v2.0 ✓ → v2.1 ✓ → v2.2 ✓ → v2.3 ✓ → v2.4 ◆ (Phase 28: 6/6 done ✓; Phase 29 SKIPPED; Phase 30: defining)

## Performance Metrics (cumulative, frozen)

**v1.0:** 5 phases, 17 plans, 208 tests, 5891 LOC F#, 85 commits, ~27h
**v1.1:** 2 phases, 5 plans, 218 tests (+10), +315/-124 LOC, 23 commits, ~19h
**v1.2:** 3 phases, 8 plans, 242 tests (+24), Core diff confined to AgentLoop.fs/Domain.fs, 43 commits, ~3 days
**v1.3:** 2 phases, 6 plans, 243 tests (+1), bench harness in repo + 54% prompt shrink + B2 recovery, 25 commits, ~1 day
**v1.4:** 2 phases, 2 plans, 243 tests (unchanged), zero src/ diff, 7 commits, ~1 day
**v2.0:** 7 phases, 19 plans, 282 tests (+39), 41 files +3993/-335 LOC, 106 commits, ~2 days, -85 GB disk (Qwen 2.5 retirement); bench gate trajectory 8/8→6/6→7/7
**v2.1:** 1 phase (single), 5 plans, 282 tests (unchanged — observational), 31 files +6463/-26 LOC, ~25 commits, ~1.5 days; verdict 82/100 KEEP; bench gate 7/7 PASS preserved; zero src/ diff
**v2.2:** 2 phases (22, 23), 5 plans (4+1), 284 tests (+2 boundary), 27 files +3569/-81 LOC, 19 commits, ~1 day; verdict 82→87/100 KEEP (Performance 20→25 via cold-start +5; Correctness stays 31/40 because CORR-EVAL-02 FAIL x2); bench gate 7/7 PASS preserved; src/ diff confined to 5 files (PlanValidator/AgentLoop/Domain/CompositionRoot/Rendering)
**v2.3:** 4 phases (24, 25, 26 BLOCKED-historical, 27), 9 plans (8+1 blocked-by-design), 287 tests (+3 PlanValidator boundary), 34 files +9152/-54 LOC (heavily docs/eval/research-driven), 31 commits, ~2 days; verdict 87→92/100 KEEP (Correctness 31→36 via CORR-EVAL-02 PASS; aggregate KEEP preserved by wider margin); bench gate 7/7 PASS preserved; src/ diff confined to 4 files (CompositionRoot, PlanValidator, AgentLoop, PlanValidatorTests)

Detailed per-plan history archived in `.planning/milestones/v{1.0,1.1,1.2,1.3,1.4,2.0,2.1,2.2,2.3}-phases/`.

## Accumulated Context

### Decisions

Cumulative log in `.planning/PROJECT.md` Key Decisions table. See PROJECT.md for outcomes through v2.3.

Stable patterns established across milestones (load-bearing for next session):

- **Core purity** — `src/BlueCode.Core/**` no Serilog/Spectre/Argu/HttpClient/file I/O; `task {}` only (CI-enforced via `scripts/check-no-async.sh`).
- **Test discovery** — explicit `rootTests` list in `RouterTests.fs` + ordered `<Compile Include>` in `BlueCode.Tests.fsproj`. New test modules need BOTH places.
- **Canonical test runner** — `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` (NOT `dotnet test`).
- **Bench gate** — `bash bench/run.sh --gate` is the structural authority. 7/7 PASS post-v2.3 with single-model 122B (T6/T5/B2/T1/W1/W2/MT).
- **Atomic commits** — `{type}({phase}-{plan}): {name}` per task; plan-meta separate; phase-complete bundles. NEVER `git add -A` or `git add .`.
- **Role = User invariant** (Phase 17-02 + Phase 20-03) — Qwen 3.5 122B REJECTS mid-conversation `Role = System` with HTTP 404. ALL mid-conversation injections are `Role = User`.
- **Single-model 122B canonical** (Phase 19) — `blueCode "..."` defaults to 122B; 35B requires `--with-35b` opt-in flag AND service loaded.
- **10-step PLAN-04 ceiling** (Phase 22-01) — `PlanValidator.MaxPlanSteps = 10` + `AgentConfig.MaxLoops = 10` (independent constants).
- **P1 enumeration directive in `defaultSystemPrompt`** (Phase 27-01) — "When the task requires renaming or restructuring multiple symbols, list ALL targets explicitly in your thought before editing. Do not start editing until the full list is enumerated." Applies to BOTH agent-loop and plan-mode invocations. Char count: 967 (was 783).
- **P2 few-shot example in `planSystemPromptSuffix`** (Phase 24-02; stays plan-mode-only post-Phase-27) — 3-line Example/Targets/Steps block demonstrating shared-prefix `add`/`add3` rename. Suffix char count: 999 (was 1183 pre-Phase-27 P1 removal).
- **Combined plan-mode prompt char count INVARIANT post-v2.3** — defaultSystemPrompt(967) + "\n\n" + planSystemPromptSuffix(999) = 1968 chars; same as before Phase 27 migration.
- **PlanValidator pre-flight `checkRenameTargetsEnumerated`** (Phase 25-01) — heuristic regex `\brename\s+\`?([A-Za-z_]\w+)\`?\s+to\s+\`?([A-Za-z_]\w+)\`?` extracts targets from user prompt; coverage check via `_raw` JSON `old_string` field on `edit_file` steps. Interpretation B: missing-target list encoded as structured detail string within existing `PlanInvalid of detail: string` case (no DU change). PlanValidator.fs:108; bound at validatePlan:143; called from AgentLoop.fs:484. Plan-mode only.
- **Plan-mode vs agent-loop distinction is load-bearing** (v2.3 lesson) — `defaultSystemPrompt` is sent for ALL invocations; `planSystemPromptSuffix` only when `--plan`; `PlanValidator.checkRenameTargetsEnumerated` only in `runPlanTurn`. Future prompt or validator changes must explicitly state which mode(s) they target. Bench gate fixtures use agent-loop (no `--plan`); CORR-EVAL-02 eval harness uses agent-loop (no `--plan`).
- **Mandatory `launchctl kickstart` pre-flight for evaluations** (v2.3 Phase 26 Diagnostic D) — `launchctl kickstart -k gui/501/com.ohama.qwen122b` clears KV cache contamination. Long-running 122B sessions accumulate contamination that produces hallucination failure modes. Confirmed empirically.
- **macOS bash-strict-mode patterns documented** (5 patterns across v2.1-v2.4): set-e/dotnet-exit (v2.1 21-03); grep-c/pipefail double-output (v2.1 21-04); mkdir-before-tee (v2.2 23-01); orphan-grep-zero-match-exit-1 (v2.3 27-02); grep-oE-pipeline-zero-match (v2.4 28-01, commit `94d905c`). Common rule: under `set -euo pipefail`, `grep -c`/`grep -cE`/`grep -oE` exits 1 on zero-match; guard with `|| true`. Full reference: `documentation/howto/macos-bash-strict-mode-patterns.md`.
- **FSI fixture skeleton pattern** (Phase 28-02) — `.fs` skeletons use `module` decl + top-level `try...with` + printfn (NO `[<EntryPoint>]`). FSI executes module-level code directly. `dotnet fsi <file>.fs </dev/null` always exits 1 (interactive EOF); compile verification uses absence of `error FS` in output, not exit code. `dotnet fsi --exec` gives exit 0 when required.
- **Phase 28 classification passed_disprove_1of5** (v2.4 Phase 28-04) — grand total 13/15 (band ≥12) → mapped_score 5/5 → passed_disprove_1of5. Phase 29 SKIP. F# fixture infrastructure reusable via `bench/eval-qwen35-122b.sh --fs-idiomatic`; transcripts archived under `.planning/phases/28-.../transcripts/`. Band-table (sum-then-scale) overrides arithmetic mean for robust multi-fixture scoring.
- **Phase 29 SKIP is definitive** (v2.4 Phase 28-06) — FS-INTERVENE-01..02 marked SKIPPED in REQUIREMENTS.md. Classification `passed_disprove_1of5` (M ≥ 3) triggers ROADMAP §Phase 28 SC #6 path (b). Next: `/gsd:plan-phase 30` (Milestone Close).

### Pending Todos

None pending. v2.4 scoping comes from observation window via `/gsd:new-milestone`.

### Blockers/Concerns

None active. All v2.2/v2.3 surfaced bottlenecks resolved:
- ~~CORR-EVAL-02 FAIL (v2.2 + Phase 26)~~ — RESOLVED in v2.3 Phase 27 (orphan_count=0; PASS verdict; RUN_TS=20260429-105907)
- ~~Persistent extraction bias on shared-prefix function names~~ — RESOLVED via P1 directive in `defaultSystemPrompt`
- ~~Architectural scope mismatch (P1+P2+P3 plan-mode-only vs agent-loop eval)~~ — RESOLVED via Phase 27 P1 migration

Documentation drift items flagged in v2.0 audit (non-blocking; carried-forward; eligible for v2.4 cleanup):
- Phase 20 missing formal `20-VERIFICATION.md` (per-plan SUMMARYs substitute)
- CLAUDE.md Key Seams has 1 stale "72B" sentence (historical)
- `documentation/` retains legacy 32B/72B install docs (kept as historical reference)

## Session Continuity

Last session: 2026-04-29
Stopped at: Completed 28-06-PLAN.md — Phase 28 closed (passed_disprove_1of5; Phase 29 SKIP; bench gate 7/7 PASS; 28-VERIFICATION.md written; STATE/ROADMAP/REQUIREMENTS updated)
Resume file: None
Next workflow trigger: `/gsd:plan-phase 30` (Milestone Close)

## Empirical Baselines (post-v2.3)

Load-bearing measurements for v2.4+ scoping:

- **HumanEval+ chat pass@1 = 0.939 / pass@1+ = 0.902** (v2.1) — re-evaluation trigger if mlx_lm.server major version, Qwen 3.5 model card update / YaRN config change, or sampling defaults change
- **Throughput median 34.6 tok/s; TTFT median 222 ms warm** (v2.1) — interactive UX baseline
- **Schema rate 0/50 InvalidJsonOutput** (v2.1) — perfect compliance; v2.0 architecture decisions validated
- **Multi-turn coherence stable through N=7** (v2.1) — refutes mlx-lm#1011 "approximately 5 rounds" community claim in our environment
- **Needle 4/4 at max_model_len=32768** (v2.1) — mlx_lm.server does not expose YaRN-extended ceiling; 32k is conservative working assumption
- **Cold-start 37s warm-OS-cache** (v2.2 Phase 23) — kernel preserves model weight pages even when launchctl kickstart kills the process; v2.0's "up to 240s" estimate stays valid for first-boot pristine case (v2.4 candidate COLDSTART-PRISTINE-01)
- **10-step PLAN-04 ceiling** (Phase 22-01) — sufficient for genuine multi-file refactor (CORR-EVAL-02 used 8/10 steps in PASSing run); ceiling no longer the constraint
- **CORR-EVAL-02 PASS** (Phase 27-02; 2026-04-29) — orphan_count=0 with all 3 v2.3 prongs in production INCLUDING P1 in `defaultSystemPrompt`. Step-2 thought from PASSing run: "I need to rename `add` to `sum` and `add3` to `sum3` across three files" — both targets enumerated explicitly. P1 effect directly observable.
- **Coding-quality 10/10 (idiomatic F# 5/5)** (v2.4 Phase 28; UPDATED from 6/10 in v2.1/v2.3) — 28-04 formal F# fixture scoring: 13/15 grand total → 5/5 mapped → passed_disprove_1of5. Prior 1/5 was Python-transcript artifact (category mismatch). v2.3 score 1/5 revised to 5/5 in 28-05 (commit a36cc35).
- **Aggregate verdict 96/100 KEEP** (post-v2.4 Phase 28-05; UPDATED from 92/100 post-v2.3) — Correctness 36/40, Performance 25/25, Reliability 25/25, Coding-quality 10/10. Empirically useful for daily F# coding via blueCode (≥80 threshold; all dimensions ≥80%; multi-file refactor PASS post-v2.3 intervention).

For full per-section results, see `documentation/qwen35-122b-coding-eval.md`. Per-plan execution history archived in `.planning/milestones/v{1.0..2.3}-phases/`.
