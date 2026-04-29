# Project Milestones: blueCode

## v2.4 Coding Quality (Shipped: 2026-04-29)

**Delivered:** Third **measurement-first data-driven** milestone (v2.2 + v2.3 set the data-driven precedent; v2.4 inverted the pattern to measure-first vs intervene-first). Scoped from v2.2/v2.3 audits' carried-forward IDIOMATIC-FS-01 candidate (Coding-quality 1/5 sub-score may be Python-transcript artifact). **Path A (1/5 disproved) won** — Phase 28's F# fixture rescore yielded grand_total 13/15 → mapped_score 5/5 → `passed_disprove_1of5`. Phase 29 SKIPPED-by-design (never created — cleaner than v2.3's BLOCKED-with-record). Phase 30 closed with aggregate verdict **92 → 96/100, KEEP** (Coding-quality 6/10 → 10/10; +4 free points without code change). All 4 dimensions now ≥80%; widest KEEP margin in project history.

**Phases completed:** 28, 30 (2 actual phases — Phase 29 was conditional and never created; never planned, never executed)

**Plans completed:** 7 total (6 in Phase 28; 1 in Phase 30; Phase 29 had 0 plans)

**Key accomplishments:**

- **Idiomatic F# 1/5 disproved as Python-transcript artifact (Phase 28; FS-EVAL-01..03):** v2.1-era 1/5 was scored against HumanEval+ Python answers (chat-mode wrapping artifacts). v2.4 built proper F# fixtures (`bench/fixtures/fs_idiomatic/{pipeline,dupatternmatch,optionhandling}.{task.md,fs}`); rescored via research Q4 verbatim binary 5-criterion rubric (C1 Idiomatic pattern present / C2 Anti-pattern absent / C3 Type signatures preserved / C4 Code structurally valid F# / C5 Task goal met) with sum-then-scale band table aggregate. Per-fixture: pipeline 5/5, dupatternmatch 5/5, optionhandling 3/5 (compile error on `Option.ofObj` for value-type tuple from `Int32.TryParse`); grand_total 13/15 → band ≥12 → mapped_score 5/5 → `passed_disprove_1of5`. Free +4 points without modifying `src/`, `defaultSystemPrompt`, or `planSystemPromptSuffix`.

- **F# fixture infrastructure shipped reusably (Phase 28-02 + 28-03):** `bench/fixtures/fs_idiomatic/` with 3 fixture pairs (each `.task.md` ≤500 bytes; each `.fs` skeleton compiles standalone via `failwith "TODO"` body). New `--fs-idiomatic` mode added to `bench/eval-qwen35-122b.sh` at line 328 mirroring `run_refactor()` template (require_port_8001 + reminder-only kickstart + per-fixture `git checkout` + agent-loop run + transcript/diff/meta capture). HTTP-only invariant preserved. Reusable for future F# coding-quality work.

- **HARNESS-AUDIT-01 howto with 5 patterns (Phase 28-01; HARNESS-01):** `documentation/howto/macos-bash-strict-mode-patterns.md` codifies all 4 carried-forward patterns (set-e/dotnet-exit; grep-c/pipefail double-output; mkdir-before-tee; grep-cE-zero-match-exit-1) PLUS Pattern 5 found during the audit pass — `grep -oE` session-id capture at line 378 of `bench/eval-qwen35-122b.sh` lacked `|| true` guard under `set -euo pipefail`. Real bug auto-fixed in commit `94d905c`. Each pattern has symptom/cause/canonical-fix/commit-ref structure.

- **Conditional Phase 29 SKIP-by-design pattern established:** v2.4's Path-A SKIP is structurally cleaner than v2.3's Phase 26 BLOCKED-with-record. Phase 26 had a partial run (3 stochastic FAIL attempts) producing diagnostic value, so it was preserved. v2.4 Phase 29 was never planned, never executed — it's a **conditional that didn't trigger**, not a phase that failed. The directory wasn't created. This is the cleanest possible skip pattern; reusable for future conditional-on-data milestones.

- **Eval doc verdict 92→96 KEEP (Phase 28-05; FS-EVAL-03):** 7 edit sites applied (§5.1 verdict line 686; §5 total line 766; §7 Idiomatic F# row line 877; §7 Coding-quality subtotal line 880; §7 Dimension coverage row line 889 — drop "exactly at threshold" qualifier; §7 Grand total line 897; final scorecard line 1064). New "F# fixture evidence (v2.4 Phase 28)" subsection added in §5. Strict-format final scorecard line `**Total: 96/100, Recommendation: KEEP**` regex match confirmed.

- **Bench gate stability preserved milestone-wide:** `bash bench/run.sh --gate` 7/7 PASS held across all 7 plans of Phase 28 + Phase 30. Per-fixture step counts unchanged vs v2.3 baseline (= v2.2 baseline; preserved through v2.3). `bench/baseline.json` byte-for-byte preserved (`git diff milestone-v2.3 HEAD -- bench/baseline.json` empty). `git diff milestone-v2.3 HEAD -- src/` empty (zero source-code change milestone). Eval harness diff confined to `--fs-idiomatic` mode + Pattern 5 fix.

- **FSI fixture skeleton pattern discovered (Phase 28-02):** `dotnet fsi <file>.fs </dev/null` always exits 1 on interactive EOF; compile verification uses absence of `error FS` in output, not exit code. Fixtures use direct top-level `try...with` (no `[<EntryPoint>]` since FSI doesn't invoke it). Documented in STATE.md as load-bearing for future F# fixture work.

**Stats:**

- 2 actual phases (28, 30; Phase 29 SKIPPED-by-design — never created), 7 plans, 7 requirements (5 unconditional ✓ + 2 conditional SKIPPED)
- 287 tests (unchanged — Phase 28 added bench fixtures, not Expecto tests)
- 37 files changed (+6,816 / -38 LOC; heavily docs/eval/research-driven)
- 21 commits in milestone range
- Bench gate 7/7 PASS preserved throughout
- Wall-clock: ~3 hours same-day (2026-04-29 13:07 → 15:59)
- **Zero `src/` diff** across entire milestone

**Git range:** `8ce7b2f docs: start milestone v2.4 Coding Quality` → `3ab6102 fix(30): correct stale commit hash in REQUIREMENTS.md HARNESS-01 validation`

**What's next:**

v2.5 candidates (observation-driven; not pulled into v2.4):
- **AGENT-LOOP-FEW-SHOT-01** (carried-forward from v2.3) — P2 migration to `defaultSystemPrompt` if observation surfaces value
- **COLDSTART-PRISTINE-01** (low; carried-forward) — Post-reboot pristine cold-start measurement
- **SLASH-01, COMPACT-01, SUBAG-01, PLAN-MODE-BENCH-01** — observation-driven (no daily-driver pain signal); v2.5+ candidates
- **STM-01** — deferred 10th cycle as of v2.4 close
- **F# fixture expansion** — if v2.5+ surfaces signal that warrants more fixtures or tighter rubric

**Audit note:** v2.4 ran `/gsd:audit-milestone` post-Phase-30 (status: passed; 5/5 unconditional + 2/2 conditional SKIPPED-by-design; 12/12 integration checks; 1/1 E2E flow; **zero tech debt**). One audit-time orchestrator correction (REQUIREMENTS.md HARNESS-01 validation clause: `4bcd8a4` → `a6159c4` per actual commit hash; commit `3ab6102`). 6 lessons recorded in `.planning/milestones/v2.4-MILESTONE-AUDIT.md`. Notable repo-level lessons: **measurement-first scoping yields free score improvements when prior measurements are based on category-mismatched evidence** (v2.4 added +4 points without modifying `src/`); **conditional SKIP-by-design is structurally cleaner than BLOCKED-with-record** (Phase 29 never-created vs v2.3 Phase 26 partial-attempt-preserved); **zero-source-diff milestones are real** (v2.1 + v2.4 both shipped meaningful capability without touching `src/`).

---

## v2.3 Comprehension Layer (Shipped: 2026-04-29)

**Delivered:** Second **data-driven** milestone, scoped from v2.2 audit's CORR-EVAL-02 FAIL constraint discovery #2 (persistent extraction bias on shared-prefix function names — `add`/`add3` shared prefix where the model renamed `add3→sum3` but ignored `add→sum`, identical step-5 thoughts across two different READMEs). Multi-prong intervention (P1 system prompt enumeration directive + P2 few-shot multi-file examples + P3 plan-mode pre-flight rename-target enumeration) with mid-flight architectural pivot (Phase 26 BLOCKED → Phase 27 added) when prongs proved scoped to plan-mode while eval harness uses agent-loop. Final verdict **87 → 92/100, KEEP** (Correctness 31/40 → 36/40 via CORR-EVAL-02 PASS).

**Phases completed:** 24-27 (4 phases, 9 plans total — 2 in Phase 24; 3 in Phase 25; 1 BLOCKED in Phase 26 [historical diagnostic]; 3 in Phase 27)

**Key accomplishments:**

- **Persistent extraction bias resolved (Phases 24+25+27; COMP-01..06):** CORR-EVAL-02 PASS empirically (orphan_count=0; RUN_TS=20260429-105907) on first attempt with kickstart pre-flight. Step-2 thought from PASSing run: "I need to rename `add` to `sum` and `add3` to `sum3` across three files" — both targets enumerated explicitly before any edits. P1 directive now reaches agent-loop mode via `defaultSystemPrompt` (post-Phase-27 migration). Eval doc verdict 87→92; 11 edit sites applied; final scorecard `**Total: 92/100, Recommendation: KEEP**` strict-format match.

- **Architectural scope discovery via Phase 26 BLOCKED (constructive failure):** Phase 26's 3 stochastic CORR-EVAL-02 attempts ALL FAIL despite all 3 v2.3 prongs being in production. Diagnosis exposed that `planSystemPromptSuffix` is `--plan`-only and `PlanValidator.checkRenameTargetsEnumerated` runs only in `runPlanTurn` — the eval harness invokes blueCode WITHOUT `--plan` (agent-loop), so none of the v2.3 prongs reached the agent during the eval. Plus pre-kickstart attempts surfaced KV cache contamination as a separate failure mode (agent hallucinated "subtract function" task). Phase 26 stays as historical BLOCKED record (commit `7837ad5`); Phase 27 was added mid-flight via `/gsd:add-phase` to close the architectural gap.

- **P1 migration with byte-invariant char count (Phase 27-01):** P1 directive moved from `planSystemPromptSuffix` to `defaultSystemPrompt`. Combined plan-mode prompt char count INVARIANT (1968 = 1968). Conditional clause "When the task requires renaming or restructuring multiple symbols..." dormant for all 7 bench gate fixtures (verified by 27-RESEARCH.md Q5: zero `rename`/`restructur` keywords in any prompt). Bench gate 7/7 PASS preserved on first attempt; zero phrasing iterations needed.

- **PlanValidator pre-flight enumeration (Phase 25; COMP-03 Interpretation B):** New `checkRenameTargetsEnumerated: userPrompt: string -> Plan -> Result<Plan, AgentError>` heuristic added to `PlanValidator.fs:108`; bound at `validatePlan:143`; called from `AgentLoop.fs:484` inside `runPlanTurn`. Interpretation B chosen: missing-target list encoded as structured detail string within existing `PlanInvalid of detail: string` case; no `Domain.fs` DU change; no compile cascade across `Rendering.fs`/`buildCorrection`. Smaller diff; same observable LLM-correction behavior. F# big-bang atomic commit (PlanValidator + AgentLoop + 6 PlanValidatorTests callers in single commit; mirrors v1.1 LlmResponse pattern). Test count 284 → 287.

- **Bench gate stability preserved through entire milestone:** `bash bench/run.sh --gate` 7/7 PASS held across all 4 phases. Per-fixture step counts unchanged vs v2.2 baseline (T6=4-5/5, W1=3/3, W2=3/3, T1=1/3, T5=3/4, B2=2/3, MT=2/4). `bench/baseline.json` byte-for-byte preserved (`git diff milestone-v2.2 HEAD -- bench/baseline.json` empty). `git diff src/` confined to 4 source files (CompositionRoot.fs, PlanValidator.fs, AgentLoop.fs, PlanValidatorTests.fs). Core purity clean.

- **Plan-mode vs agent-loop distinction now load-bearing repo knowledge:** This milestone made the distinction explicit and consequential. `defaultSystemPrompt` is sent for ALL invocations (agent-loop AND plan-mode); `planSystemPromptSuffix` is sent ONLY when `--plan` is used; `PlanValidator.checkRenameTargetsEnumerated` runs ONLY in `runPlanTurn`. Future prompt or validator changes must explicitly state which mode(s) they target. Repo lesson recorded in v2.3-MILESTONE-AUDIT.md.

- **KV cache contamination empirically established (Phase 26 Diagnostic D):** `launchctl kickstart -k gui/501/com.ohama.qwen122b` is now mandatory pre-flight for evaluation runs. Long-running 122B sessions accumulate contamination that produces hallucination failure modes (e.g., reading "rename" task as "add subtract function").

- **Fourth macOS bash-strict-mode pattern documented (Phase 27-02; commit `9f8e06e`):** `bench/eval-qwen35-122b.sh` had `grep -cE` exits 1 on zero-match (which is the SUCCESS case for orphan count check); `set -euo pipefail` aborted before writing `refactor_orphan_count.txt`. Auto-fixed with `|| true` guard per Rule 3 (auto-fix-blockers). Fourth pattern after v2.1's set-e/dotnet-exit + grep-c pipefail double-output and v2.2's mkdir-before-tee.

**Stats:**

- 4 phases (24, 25, 26 BLOCKED-historical, 27), 9 plans (8 completed + 1 blocked-by-design), 6 requirements (6/6 ✓)
- 284 → 287 tests (+3 new boundary tests for `checkRenameTargetsEnumerated` in Phase 25-02)
- 34 files changed (+9152 / -54 LOC; heavily docs/eval/research-driven)
- 31 commits in milestone range
- Bench gate 7/7 PASS preserved throughout
- Wall-clock: ~2 days (2026-04-28 → 2026-04-29)
- Core source diff confined to 4 files (CompositionRoot.fs, PlanValidator.fs, AgentLoop.fs, PlanValidatorTests.fs)

**Git range:** `cbf6e5a docs: start milestone v2.3 Comprehension Layer` → `b304c5c docs(27): complete default-prompt-p1-migration-re-eval phase`

**What's next:**

v2.4 candidates (observation-driven; not pulled into v2.3):
- **AGENT-LOOP-FEW-SHOT-01** (newly-surfaced from v2.3) — Migrate or adapt P2 (currently plan-mode-only) for agent-loop mode if observation surfaces value. Phase 27 deliberately left P2 in plan-mode (its `Targets:`/`Steps:` format is plan-mode-specific).
- **HARNESS-AUDIT-01** (low; newly-surfaced) — `bench/eval-qwen35-122b.sh` accumulated four macOS-bash-strict-mode patches across v2.1-v2.3. Worth a one-pass audit + howto entry codifying the pattern.
- **IDIOMATIC-FS-01** (medium; carried-forward from v2.2 audit) — Coding-quality 1/5 sub-score may be Python-transcript artifact; needs F# task fixtures + transcript review
- **COLDSTART-PRISTINE-01** (low; carried-forward) — post-reboot pristine cold-start untested
- v2.0/v2.1 deferred candidates (SLASH-01, COMPACT-01, SUBAG-01, PLAN-MODE-BENCH-01, STM-01) — observation-window dependent

**Audit note:** v2.3 ran `/gsd:audit-milestone` post-Phase-27 (status: passed; 6/6 requirements; 12/12 integration checks; 3/3 E2E flows). One in-scope deviation (harness `|| true` fix in commit `9f8e06e` per Rule 3 auto-fix-blockers; documented). Phase 26 BLOCKED preserved as intentional historical record per ROADMAP supersession pattern. 6 lessons recorded in `.planning/milestones/v2.3-MILESTONE-AUDIT.md`. Notable repo-level lesson: **for each intervention, explicitly check which code paths exercise it and which paths bypass it** — the architectural-scope-mismatch pattern surfaced for the second milestone in a row (v2.2: ceiling raise was necessary-but-insufficient; v2.3: prongs were plan-mode-scoped while eval is agent-loop).

---

## v2.2 Multi-file Capability (Shipped: 2026-04-28)

**Delivered:** First **data-driven** v2.2 candidate (vs deferred-list draining) — scoped from v2.1 audit's CORR-EVAL-02 FAIL constraint discovery. Two-phase milestone: Phase 22 architectural ceiling raise (PLAN-04 5→10 across 4 source files) + Phase 23 cold-start empirical measurement (deferred from v2.1). Final verdict **82 → 87/100, KEEP** (Performance 20/25 → 25/25 via cold-start +5).

**Phases completed:** 22-23 (2 phases, 5 plans total — 4 in Phase 22; 1 in Phase 23)

**Key accomplishments:**

- **Architectural ceiling raise (Phase 22; PLAN-CAP-01..03):** `PlanValidator.MaxPlanSteps` 5→10 + `AgentConfig.MaxLoops` default 5→10 (Option 1 design — independent constants preserved per existing PlanValidator.fs:36-40 docstring rationale). System prompt "1-5 steps" → "1-10 steps" + 1-sentence usage guidance ("Use the minimum steps needed; reserve the full budget only for tasks requiring reads across multiple files before editing"). Plus `[PLAN INVALID]` retry message + `Rendering.fs` MaxLoopsExceeded user message + `RenderingTests.fs` assertion all updated. T6 baseline held first try (no system-prompt iteration needed). Tests 282 → 284 (+2 boundary cases at 10 PASS / 11 FAIL boundaries; AgentLoopTests new MaxLoopsExceeded-at-10 case using `{ testConfig with MaxLoops = 10 }` invariant).

- **Constraint discovery #2: persistent extraction bias (Phase 22; PLAN-CAP-04..05 partial-by-design):** CORR-EVAL-02 re-run produced FAIL twice with textually identical step-5 thoughts ("Rename add3 to sum3 only"; ignored `add → sum`) across two completely different READMEs (902-char prose + 2128-char enumerated rewrite with explicit warning). Agent used 8/10 steps in both attempts (no MaxLoopsExceeded; ceiling sufficient). Disproves v2.1 hypothesis that ceiling alone was the constraint; surfaces comprehension layer as new bottleneck. User-accepted Option C: surface as v2.3 first candidate (COMP-BIAS-01) rather than fight symptom. Eval doc §2.4/§8/§9 updated with two-stage finding documentation.

- **Cold-start empirical measurement (Phase 23; COLD-EVAL-01):** `--coldstart` produced 37s to model-ready (PID 44880 → 10536 confirms genuine process replacement; 1s first-generation post-recovery confirms full load). ≤180s top band → 5/5 pts. Performance dimension flipped 20/25 → 25/25 (100%); total **82 → 87/100, KEEP**. Surprising finding: v2.0 SUMMARY's "up to 240s" estimate was ~6× pessimistic for the common case; actual 37s reflects warm OS file cache (kernel preserves model weight pages even when kickstart kills the process). v2.0 estimate stays valid for first-boot pristine case.

- **Bench gate stability preserved (mandatory invariant):** `bash bench/run.sh --gate` 7/7 PASS held across all 4 plans of Phase 22 + Phase 23. Per-fixture step counts unchanged vs v2.1 baseline (T6=4/5, W1=3/3, W2=3/3, T1=1/3, T5=3/4, B2=2/3, MT=2/4). `bench/baseline.json` byte-for-byte preserved. `git diff src/` confined to 5 Phase 22 source files (Domain.fs comment + PlanValidator.fs + AgentLoop.fs + CompositionRoot.fs + Rendering.fs). Core purity clean.

- **Third macOS bash-strict-mode pattern documented:** `4bcd8a4 fix(23-01): move mkdir before tee in run_coldstart to satisfy set -euo pipefail`. After 21-04's set-e/dotnet-exit and grep-c-pipefail bugs, this is the third pattern. Common rule: under `set -euo pipefail`, **any I/O command must verify its target directory exists first**.

- **Verdict change pathway:** PLAN-CAP-05 originally scoped to flip 82→87 via CORR-EVAL-02 PASS path. With CAP-04 partial closure, the same numeric end state was reached via Phase 23 cold-start path instead. Different mechanism; same result. Eval doc verdict line `**Total: 87/100, Recommendation: KEEP**` confirmed strict-format match. Eval doc preserves both stages (§2.4 documents two-stage finding for v2.3 input).

**Stats:**

- 2 phases (22, 23), 5 plans, 6 requirements (4 fully + 2 partial-by-design + 1 cold-start full)
- 282 → 284 tests (+2 new + 1 in-place updated)
- 27 files changed (+3,569 / -81 LOC; mostly plan/summary docs)
- 19 commits in milestone range
- Bench gate 7/7 PASS preserved throughout
- Wall-clock: ~1 day (2026-04-28 same-day milestone)
- Core source diff confined to 5 files (PlanValidator.fs, AgentLoop.fs, Domain.fs, CompositionRoot.fs, Rendering.fs)

**Git range:** (v2.2 milestone start) → (Phase 23 phase-completion)

**What's next:**

v2.3 first candidate is **COMP-BIAS-01** (data-driven from v2.2 Phase 22 finding):
- Persistent extraction bias on shared-prefix function names — empirically reproducible across two different READMEs
- Candidate interventions: system prompt enumeration guidance for multi-file refactors, few-shot multi-file examples in plan-mode prompt, plan-mode pre-flight rename-target enumeration
- Re-evaluation criterion: CORR-EVAL-02 PASS (orphan_count=0); flips Correctness 31/40 → 36/40; total 87 → 92/100

Plus carried-forward candidates from v2.2 audit:
- IDIOMATIC-FS-01 (medium) — Coding-quality 1/5 may be Python-transcript artifact
- COLDSTART-PRISTINE-01 (low) — post-reboot case untested
- v2.0/v2.1 deferred candidates (SLASH-01, COMPACT-01, SUBAG-01, PLAN-MODE-BENCH-01, STM-01) — observation-window dependent

**Audit note:** v2.2 ran `/gsd:audit-milestone` post-Phase-23 (status: passed with 2 non-blocking caveats: CAP-04/05 partial-by-design per Option C; Phase 23 missing formal VERIFICATION.md per orchestrator decision — same pattern as v2.0 Phase 20). 4/6 requirements fully + 2 partial-by-design. 9/9 plan-integration edges PASS. 4/4 E2E flows. 12/12 documentation cross-references. Tech debt aggregated into `.planning/milestones/v2.2-MILESTONE-AUDIT.md`.

---

## v2.1 Empirical Qwen 3.5 122B Coding Evaluation (Shipped: 2026-04-28)

**Delivered:** Closed v2.0's empirical measurement gap with a single-phase observation-only milestone. Produced `documentation/qwen35-122b-coding-eval.md` (983 lines, 10 sections) ending with **Total: 82/100, Recommendation: KEEP**. HumanEval+ chat pass@1 = 0.939 / pass@1+ = 0.902 places Qwen 3.5 122B-A10B-4bit MoE in the upper tier of open-weight coding models. Schema 0/50 perfect; multi-turn coherence through N=7 (refutes mlx-lm#1011 "5 rounds" community claim); needle 4/4 retrieved at 32k. Bench gate 7/7 PASS preserved post-eval; zero `src/` diff; tests 282/1/0 unchanged.

**Phases completed:** 21 (1 phase, 5 plans) — single-phase milestone

**Key accomplishments:**

- **Empirical 100-point scorecard verdict (DOC-EVAL-01):** `documentation/qwen35-122b-coding-eval.md` (983 lines) applies the rubric Correctness 40 / Performance 25 / Reliability 25 / Coding-quality 10 with explicit thresholds per sub-criterion. Headline: **Total: 82/100, KEEP** — empirically useful for daily F# coding via blueCode. Each section ends with a verdict line; §3.3 documents cold-start deferral; §6.3 documents cloud comparison non-goal as deliberate boundary; §10 provides self-contained reproduction (`--setup` then `--full`). Companion `documentation/phase21-evaluation-narrative.md` (372 lines) provides the why/what/how/result narrative explanation.

- **Anchor benchmarks measured (CORR-EVAL-01, PERF-EVAL-01..02, REL-EVAL-03):** HumanEval+ chat pass@1 0.939 / pass@1+ 0.902 (154/164; eval-standard temp=0.2 sampling); throughput median 34.6 tok/s (≥30 threshold); TTFT median 222 ms warm (≤500 threshold); long-context needle 4/4 retrieved at 8k/16k/32k (max_model_len=32768). The 3.4× speedup claim from Phase 17 SWITCH now has real tok/s baseline.

- **Reliability dimension 100% (REL-EVAL-01..03):** Schema compliance 0/50 InvalidJsonOutput (perfect; stricter than Phase 18-02's 0/31). Multi-turn coherence stable through N=7 (first failures at N=10: invalid_json=2 at turns 7-10), refuting community claim from `ml-explore/mlx-lm#1011` about "approximately 5 rounds" degradation. Needle retrieval correct at server ceiling (32k via mlx_lm.server fallback).

- **Real eval signal: 5-step PLAN-04 ceiling = constraint discovery (CORR-EVAL-02 FAIL):** Multi-file refactor measured FAIL with orphan_count=1. Agent read all 4 fixture files coherently then started rename on Calculator.fs, exhausted 5-step budget before touching Main.fs/Tests.fs. Not a model failure — architectural constraint surfacing. Eval doc §9 lists "if 5-step cap is raised, re-run CORR-EVAL-02" as v2.2 re-evaluation trigger. **First v2.2 candidate, data-driven.**

- **Hybrid bash + Python(venv) HTTP-only harness (~1,115 LOC):** `bench/eval-qwen35-122b.sh` (~700 lines, 9 mode flags) + `bench/eval-humaneval-http.py` (159 lines, no `mlx_lm` import) + `bench/eval-needle.py` (~80 lines, no `mlx_lm` import). HTTP-only adaptation preserves the load-bearing constraint that mlx_lm.load() in-process would OOM the launchd-managed 122B service (~45 GB resident). 7 new fixtures (`refactor_multifile/{Calculator,Main,Tests}.fs`+README, `bug_binsearch.fs`, `bug_python_typeerror.py`, `bug_typescript_async.ts`, `multiturn_prompts.txt`).

- **macOS evalplus silent-failure traps diagnosed and fixed:** `evalplus.evaluate` was returning silent pass@1=0 in chat mode due to (1) doubled signature when prompt+completion stitched (chat answers are full function defs); (2) `RLIMIT_AS` exceeding macOS per-process hard limit. Fixes: `python -m evalplus.sanitize` pre-pass + `EVALPLUS_MAX_MEMORY_BYTES=-1` env var. Both baked into `run_humaneval()`. Without these, the HumanEval+ verdict would have been entirely wrong.

- **Three bash-strict-mode + macOS deviations auto-fixed:** `set -euo pipefail` interaction with `dotnet run` non-zero exit; `grep -c \|\| echo 0` under pipefail double-output; BSD `seq 2 1` countdown bug. Patterns documented in plan SUMMARYs for future bash handler authors.

**Stats:**

- 1 phase (21), 5 plans, 10 requirements (100% closure; 9/10 met threshold; CORR-EVAL-02 FAIL = constraint discovery)
- 282/1/0 tests (unchanged from v2.0; eval is observational only)
- 31 files changed (+6,463/-26 LOC) — 12 new + 3 modified + 0 source-code changes
- ~25 commits in milestone range
- Bench gate 7/7 PASS preserved throughout
- Wall-clock: ~1.5 days (2026-04-27 milestone start → 2026-04-28 eval doc shipped)
- Disk: +0 GB tracked (`bench/.venv-eval/` gitignored)

**Git range:** (v2.1 milestone start) → (Phase 21 phase-completion)

**What's next:**

v2.2 first candidate is **data-driven** from this eval (not from the deferred-list backlog):

- **5-step PLAN-04 ceiling raise** — CORR-EVAL-02 FAIL surfaced this as the structural blocker for genuine multi-file refactor. Needs Core change (Domain.fs Plan validator). Re-evaluation trigger documented in eval doc §9.
- **Cold-start measurement** — `--coldstart` handler ready; awaits scheduled disruption window
- **Idiomatic F# generation improvement** — Coding-quality 6/10 (idiomatic F# 1/5 sub-score) surfaces gap; system prompt F# style hint or few-shot? observation window will determine

Plus continuing v2.0-deferred candidates (compaction, slash commands, sub-agents, thinking-mode-on, native tool_calls, streaming) — these did not directly surface as pain in this eval; observation window determines priority.

**Audit note:** v2.1 ran `/gsd:audit-milestone` post-Phase-21 (status: passed). 10/10 requirements satisfied; 5/5 plan-integration edges PASS; 1/1 E2E flows reproducible; 4/4 documentation cross-references PASS; verdict scorecard arithmetic verified (31+20+25+6 = 82). Tech debt aggregated into `.planning/milestones/v2.1-MILESTONE-AUDIT.md` (3 v2.2 candidates + 6 accepted items).

---

## v2.0 Persistence + Planning (Shipped: 2026-04-27)

**Delivered:** Broke the process-lifetime constraint that v1 deliberately accepted. Bundled cross-turn REPL memory + `--resume <id>` (PERSIST-01..04) with plan-then-execute mode + user approval gate (PLAN-01..04). Mid-milestone scope expansion: shipped Qwen 3.5 122B as single-model canonical (32B/72B retired; 35B preserved as cold rollback) + Qwen 3.5 protocol alignment (sampling params, 300s timeout, reasoning_content fallback, Role=User probe verdict).

**Phases completed:** 14 - 20 (7 phases, 19 plans total — 3 added mid-milestone via `/gsd:add-phase`)

**Key accomplishments:**

- **Persistence shipped (PERSIST-01..04):** Multi-turn REPL maintains conversation history within a session; every turn writes to `~/.bluecode/sessions/<id>.jsonl` (version: 2 header + per-turn TurnComplete envelopes); `--resume <id>` reconstructs prior context; `--new-session` forces fresh; conflicting flags rejected at startup with exit 2. `runSession` extended with `priorSteps: Step list` parameter; v1 per-step crash log coexists with new per-turn session log.

- **Planning shipped (PLAN-01..04):** `Plan` DU added to `LlmOutput` (Phase 14); pure `validatePlan` covers 3 structural rules (length≤5, unknown tool, duplicate adjacent); `--plan` CLI flag triggers plan-then-execute mode (Phase 16); PlanGate.fs renders Spectre numbered table + a/r/e/q dispatch via `IKeyReader` port abstraction; `[PLAN REJECTED]` re-prompt is `Role = User` (Phase 20-03 invariant); 2-attempt retry path on either schema-invalid (parse-time) or structural-invalid (validator-time) PlanInvalid; `--plan --resume <id>` valid combination.

- **Qwen 3.5 122B canonical (Phases 17-19):** SWITCH from Qwen 2.5 32B/72B to 35B/122B (3.4× speedup, no regressions). DROP-35B verdict (122B alone is viable; PhysMem +19.42 GB freed; T1/T2 median 3s). Qwen 2.5 fully retired (-85 GB disk; 32B/72B + qwen72b.3bit deleted; launchd plists removed). 35B preserved as cold rollback via `--with-35b` opt-in flag. `bench/run.sh` absorbed `scripts/bench-122b-only.sh`; `bench/baseline.json` halved 8→6 entries; gate 6/6 PASS.

- **Qwen 3.5 protocol alignment (Phase 20):** Sampling params per Qwen 3.5 model card (temp=0.7, top_p=0.8, top_k=20, presence_penalty=0.0). HttpClient timeout 180→300s for 122B cold-start. `extractContent` `reasoning_content` fallback (defensive against future thinking-mode-on). `Role = User` invariant probed and confirmed for 122B (HTTP 404 on mid-conversation Role=System; AgentLoop.fs:249/260/266 stay Role=User; documented via `scripts/probe-system-role.sh` + 3-line in-code comments).

- **Phase 16 replan-from-scratch:** Original Phase 16 plans (pre-Phase-17) were too stale to surgically re-key after Phases 17-20 shifted premises (32B/72B → 122B; 8→6 baseline; sampling params; Role=User invariant). Replanned cleanly. Stale `.stale` siblings preserved in phase dir as forensic reference. New plans add MT_122b multi-turn bench fixture (PERSIST-01 end-to-end via `--resume`); baseline 6→7 entries.

- **Critical mid-execution discovery:** Qwen 3.5 chat template REJECTS mid-conversation `Role = System` with HTTP 404 ("System message must be at the beginning"). Phase 17-02 flipped all mid-conversation injections (POST-EDIT CONSTRAINT, POST-READ HINT, [PLAN REJECTED]) to `Role = User`. Phase 20-03 re-probed 122B alone and confirmed REJECT verdict — invariant is load-bearing for v2.0+ work.

**Stats:**

- 7 phases (14-20), 19 plans, 8 requirements (100% closure)
- 282/1/0 tests (was 243 pre-v2.0; +39 across milestone)
- 41 files changed (+3,993/-335 LOC)
- 106 commits in milestone range
- Bench gate trajectory: 8/8 (Phase 17) → 6/6 (Phase 19 single-model) → 7/7 (Phase 16 MT_122b added)
- Disk: -85 GB (Qwen 2.5 retirement)
- Wall-clock: ~2 days (2026-04-26 milestone start → 2026-04-27 Phase 16 verified)

**Git range:** (v2.0 milestone start) → (Phase 16 phase-completion)

**What's next:**

v2.1 candidates well-stocked from observation + v2.0 mid-milestone signals:
- Compaction (PERSIST-02 saves full session JSONL; long sessions hit 80% context warning faster)
- Slash commands (`/sessions`, `/plan`, `/clear`) — UX layer over CLI flags
- Sub-agent delegation — meaningful now that memory + planning land
- Plan-mode bench fixture (deferred from Phase 16-03; mocked-IKeyReader pattern)
- Thinking-mode-on + `<think>` consumption (requires `max_tokens` 1024→2048-4096 + re-bench)
- Native OpenAI `tool_calls` replacing custom JSON schema
- Streaming output (STM-01) — deferred 7th cycle

**Audit note:** v2.0 ran `/gsd:audit-milestone` post-Phase-16 (status: passed). 8/8 requirements satisfied; 6/6 E2E flows verified; cross-phase wiring confirmed. Documentation drift noted but non-blocking: Phase 20 missing formal `20-VERIFICATION.md` (per-plan SUMMARYs + bench gate substitute); ROADMAP Phase 20 row stale (functional state was complete). Tech debt aggregated into v2.0-MILESTONE-AUDIT.md.

---

## v1.4 Test Hygiene + Bench Polish (Shipped: 2026-04-26)

**Delivered:** Two pieces of cited tech debt cleared (3-milestone-old shared `makeMockResponse` helper + bench fixture working-tree drift surfaced in v1.3 Part 4), then opens a 2-week observation window for v1.5 scoping. Path B from the v1.3 close — discipline preserved, small wins shipped, observation-driven scoping (not deferred-list draining).

**Phases completed:** 12 - 13 (2 plans total — one per phase)

**Key accomplishments:**

- **TST-01 closed (Phase 12):** Consolidated `makeMockResponse` from 2 in-repo definitions (one each in `AgentLoopTests.fs` and `ReplTests.fs`) into shared `tests/BlueCode.Tests/MockHelpers.fs`. Both consumers updated with `open BlueCode.Tests.MockHelpers`; 15 call sites continue to work without source change. Test count 243/1/0 preserved (zero behavioral change). Single combined `refactor` commit for 4 mechanically-coupled file edits. Other duplicated helpers (`toolCall`, `mockLlm`/`stubLlm`, `mockToolsOk`/`stubToolsOk`, `discardStep`) deliberately NOT factored — TST-01 scope discipline closed 3 prior milestones' worth of scope-creep deferral. Discovered REQUIREMENTS.md spec count discrepancy: actual was 2 definitions, not 3 (the spec conflated definition sites with use sites).
- **BENCH-06 closed (Phase 13):** Added EXIT trap to `bench/run.sh` line 18 (between `set -u` and `cd`). Trap body: `git checkout -- bench/fixtures/bug_lastchar.fs bench/fixtures/bug_average.fs 2>/dev/null || true`. Single-quoted, no `exit N`, exit-code preserving, bash 3.2 compatible. Targets W1/W2 write fixtures only — `bug_divide_zero.fs` (B2 read-only diagnose fixture) deliberately excluded. Defense-in-depth preserved: existing in-line heredoc-restore blocks at lines 137-153 and 278-296 (post-shift) handle between-invocation reset; trap handles exit-time cleanup. SC1 verified empirically with full `--gate` run (8/8 PASS, fixtures clean post-trap). New `## Auto-Reset of Write Fixtures` subsection in `documentation/bench.md` (lines 60-79).
- **Patterns established for future use:** "Test helper modules: no testList, no `RouterTests.fs:rootTests` entry; only fsproj `<Compile Include>` registration needed." "EXIT trap pattern: `trap 'git checkout -- <files> 2>/dev/null || true' EXIT` — reliable, bash 3.2 compatible, exit-code preserving."
- **Audit confirmed clean (status: passed):** No critical gaps, no cross-phase wiring issues (Phase 12 = `tests/`, Phase 13 = `bench/` + `documentation/` — fully independent), no E2E flow changes, zero `src/` diff. 1 pre-existing low-severity tech debt item deferred unchanged (other duplicated test helpers).

**Stats:**

- 2 phases (12, 13), 2 plans, 2 requirements (100% closure)
- Source code delta: zero `src/` change. `tests/`: 4 files changed (1 new `MockHelpers.fs`, 3 modified). `bench/run.sh`: +4 lines. `documentation/bench.md`: +21 lines. Total: 6 files, +37/-16 LOC.
- 243/1/0 tests passing; 1 env-gated smoke ignored (unchanged from v1.3)
- Bench gate 8/8 PASS throughout
- 7 commits in milestone range (v1.4 setup + execution + closes)
- ~1 day wall-clock (2026-04-26 milestone start → 2026-04-26 Phase 13 verified)

**Git range:** (v1.4 milestone start) → (Phase 13 complete)

**What's next:**

**2-week observation window opens 2026-04-26 (load-bearing exit criterion).** Daily-drive blueCode as primary Mac coding agent; use `/gsd:add-todo` from real coding sessions to capture pain signals. v1.5 scope is built from those todos — NOT from the deferred-list backlog. The bench gate now catches structural regressions automatically, so observation has zero structural cost. STM-01 (streaming) deferred 6th cycle; defer-pattern is itself the load-bearing signal.

---

## v1.3 Bench-Driven Quality Gates (Shipped: 2026-04-26)

**Delivered:** v1.2's behavioral wins formalized as a repo-tracked regression suite (`bench/run.sh --gate`), then a 54% system-prompt shrink (1689 → 783 chars) validated by that suite — recovering correct B2 divide-by-zero diagnosis on both 32B and 72B and empirically confirming the v1.2 audit's prompt-length attention-shift hypothesis.

**Phases completed:** 10 - 11 (6 plans total — 3 in Phase 10 + 3 in Phase 11)

**Key accomplishments:**

- **Bench harness in repo with gate mode**: moved `/tmp/bench-v1.2/run.sh` to repo-tracked `bench/run.sh` with mode-flag dispatcher (`--gate`, `--regression`, `--canary`, `--all`, `--b2`, `--help`); `bench/baseline.json` records 8-test baseline; `bench/fixtures/` versions the bug fixtures (added new `bug_divide_zero.fs` for B2). `--gate` mode runs 8 invocations in ~115s, jq-based JSON diff vs baseline, exits non-zero on regression. SC1 verified positive (current binary → exit 0) AND negative (tightened baseline → exit 1, restored → exit 0 round-trip clean).
- **System prompt shrink to 783 chars** (54% reduction from 1689) via 13-cycle iterative protocol: edit → `bench/run.sh --gate` → bisect on FAIL → repeat. Path C target (≤800) achieved without invoking Path D escape hatch. Source code delta confined to `src/BlueCode.Cli/CompositionRoot.fs` for the prompt itself plus 2 Rule 3 auto-fixes in `Adapters/FsToolExecutor.fs` (`edit_file` empty-`old_string` infinite-loop guard + `grep_search` file-path support).
- **Post-`read_file` injection extending 09.1-05's loop-injection primitive**: `lastReadHint: (string * string) option` parameter threaded through `runLoop`; on successful `read_file`, `buildMessages` appends a System-role `[POST-READ HINT]` message at END of message list (post-user position) when the header status is `truncated` or `out-of-range`. Two-arm hint logic with kind-specific corrective actions. New `testCaseAsync` in existing `AgentLoopTests.fs` (242 → 243 tests). Domain.fs untouched (parameter discipline mirrors `lastEditPath`).
- **B2 recovery on both 32B and 72B**: `bench/baseline.json` B2 entries flipped from `pass: false, regression: true` to `pass: true`. Verbatim model thoughts (32B): *"The bug is identified. It occurs when the function `average` is called with an empty list, leading to a division by zero error."* (72B): *"The bug is triggered when the function `average` is called with an empty list. This causes a division by zero because `List.length xs` returns 0 for an empty list."* The audit's prompt-length attention-shift hypothesis is confirmed: 54% prompt reduction was sufficient on its own; no surgical B2 hint needed.
- **5 new howtos capturing v1.2 + v1.3 reusable lessons** in `documentation/howto/` — post-user-injection pattern, bench-driven prompt iteration, agent fixture design, header-substring collision pitfall, jq-based regression gate. Each howto passed the 4-criteria quality gate (Non-Googleable + Hard-Won + Actionable + Reusable).
- **T6 step count deterministic at 3** for both 32B and 72B (was 4-5 with occasional MaxLoops at v1.2 close); the new `grep_search → read_file → final` pattern is reliable post-shrink.

**Stats:**

- 2 phases (10, 11), 6 plans, 243 tests passing (242 v1.2 baseline + 1 from 11-01); 1 env-gated smoke ignored
- Source code delta: `src/BlueCode.Core/AgentLoop.fs` (+lastReadHint threading + injection), `src/BlueCode.Cli/{CompositionRoot.fs (prompt shrink), Adapters/FsToolExecutor.fs (2 Rule 3 fixes)}`, `tests/BlueCode.Tests/AgentLoopTests.fs` (1 new testCase)
- New infrastructure: `bench/` directory tree (run.sh + 3 fixtures + baseline.json), `documentation/bench.md` (190 lines), 5 new howtos
- 25 commits since v1.2 close
- ~1 day wall-clock (2026-04-26 milestone start → 2026-04-26 Phase 11 verified)

**Git range:** (v1.2 close) → (howto additions)

**What's next:**

v1.4 candidate: "Test Hygiene + Bench Polish" — small tactical milestone (~3 days, 2 phases) for TST-01 (shared `makeMockResponse` helper, 3-milestone-old debt) + bench fixture cleanup automation (gitignore drift after `--gate` runs). Explicit exit criterion: 2-week observation window using `/gsd:add-todo` to capture real-use pain signals from daily-driver use, scoping v1.5 from that signal pool rather than the existing deferred-list backlog. Defer STM-01 (streaming) one more cycle — five deferrals is a strong signal that observation, not feature work, is the right next move.

**Audit note:** v1.3 did NOT run `/gsd:audit-milestone` (Phase 10 + Phase 11 verifiers both passed independently with 5/5 and 4/4 must-haves; the bench gate's deterministic verdict logic provides cross-phase E2E validation by construction). This is the same shipping discipline as v1.0 and v1.1 (both shipped without formal audits, justified by other validation gates). The bench gate is now the project's primary regression-detection mechanism.

---

## v1.2 Tool Expansion (Shipped: 2026-04-26)

**Delivered:** Three new agent tools (`edit_file`, `glob_search`, `grep_search`) plus a `read_file` metadata header (TOOL-08), with a post-audit Phase 9.1 that hardened TOOL-08's actual behavioral effectiveness and shipped a reusable code-level loop-injection mechanism for tool-terminality enforcement (`[POST-EDIT CONSTRAINT]` System-role message, post-user-prompt position).

**Phases completed:** 8 - 9 + 9.1 (decimal insertion) — 8 plans total (3 original + 5 in inserted Phase 9.1 across two gap-closure rounds)

**Key accomplishments:**

- Three new tools across the full stack (Tool DU + 8-value schema enum + executor + system prompt + 18 unit tests in `ToolExpansionTests.fs`) — `edit_file` exact-string surgical edit with 0/1/N match handling, `glob_search` project-rooted pattern finder, `grep_search` regex content search with ReDoS guard
- `read_file` structured metadata header `[file: <path>, lines X-Y of Z, not-truncated|truncated|out-of-range]` prepended to every Success payload, with raw-`endLine` preservation on out-of-range to give the LLM an unambiguous bounds-violation signal
- Phase 9.1 closed three audit-discovered effectiveness gaps via a multi-layered fix: dispatcher default-window for partial line bounds (`Some s, None → Some(s, s+99)`), system-prompt `truncated` strategy hint (restored 72B's narrow-window heuristic), directive `edit_file` terminal wording, and — when wording-only intervention proved insufficient against user-prompt explicit tool naming (W1's "using write_file") — a code-level `[POST-EDIT CONSTRAINT]` System-role message threaded through `runLoop` via `lastEditPath: string option` and appended at the END of `buildMessages`, leveraging conversation-history position-as-authority to override prior user instructions
- Bench evidence: T6 × 32B 0/3 FAIL → 3/3 PASS; T6 × 72B 0/3 FAIL → 3/3 PASS (exceeded ≥2/3 prediction); W1 × 32B 4 → 3 steps PASS; W2 × 32B 4 → 3 steps PASS; T1/T5 canaries unchanged. Smoking-gun thought diff on W1 (post-09.1-05): `"Now, I will save the corrected version"` → `"The bug has been fixed and the change is confirmed. No further action is needed on this file."`
- New architectural primitive — the loop-injection mechanism is reusable for any future tool-terminality concern (post-`read_file`-truncated hint, post-`write_file` redundancy guard, etc.); could let v1.3's PERF-01 prompt-shrink work move contextual hints out of the base system prompt and into post-tool-result injections

**Stats:**

- 3 phases (8, 9, 9.1), 8 plans, 242 tests passing (1 env-gated smoke ignored)
- Source code delta confined to `src/BlueCode.Core/{AgentLoop.fs, Domain.fs}` and `src/BlueCode.Cli/{CompositionRoot.fs, Adapters/Json.fs, Adapters/FsToolExecutor.fs}`, plus `ToolExpansionTests.fs` (new) and `FileToolsTests.fs` / `AgentLoopTests.fs` additions
- 43 commits since v1.1 close
- ~3 days wall-clock (2026-04-24 milestone start → 2026-04-26 Phase 9.1 verifier passed)

**Git range:** `c4f087e` (v1.1 close) → (Phase 9.1 phase-completion)

**What's next:**

v1.3 candidate: "Bench-Driven Quality Gates" — formalize the bench harness (move from `/tmp/bench-v1.2/` to repo-tracked `bench/` with regression-gate mode) and execute PERF-01 system-prompt shrink (~1500 → ~700 chars) with the formalized bench as the validation gate. The 09.1-05 loop-injection mechanism is a primitive that could let some prompt content move to post-tool-result injections instead of always living in the base prompt. Alternatives: tactical "just lock the bench" 1-phase milestone, or "+ STM-01 streaming" 3-phase milestone.

**Audit note:** v1.2 ran `/gsd:audit-milestone` mid-milestone (post-Phase 9 completion). Initial verdict was `passed`; revised to `tech_debt` after a same-day 36-run re-bench surfaced TOOL-08 dispatcher gap, 72B `truncated` regression, and 32B edit_file+write_file redundancy. The audit's `tech_debt[]` items mapped 1:1 to Phase 9.1's deliverables, which closed all three. Final phase verifier returned `passed` 5/5 on 2026-04-26; v1.2 ships clean. **Bench discipline lesson:** structural verification (line numbers, schema enum, test pass) is necessary but insufficient — requirements citing specific failure traces need probe-style behavioral tests at phase verification time.

---

## v1.1 Refinement (Shipped: 2026-04-24)

**Delivered:** Three pieces of v1.0 technical debt cleaned up — portable model id resolution, lazy bootstrap probe, and real LLM thought capture in `--verbose` output. Mid-milestone gap closure (06-03) fixed a live regression where mlx_lm.server's HF Hub fallback overwrote the Instruct tokenizer with Base Coder weights.

**Phases completed:** 6-7 (5 plans total — 3 in Phase 6 including 06-03 gap closure, 2 in Phase 7)

**Key accomplishments:**

- `Router.modelToName` absolute-path hardcode removed; `QwenHttpClient` adapter now resolves model id at runtime via `ModelInfo` record + `probeModelInfoAsync` + per-port `Lazy<Task<ModelInfo>>` cache, preferring local paths over HF ids to prevent tokenizer swap (REF-01 + 06-03 gap closure)
- `bootstrapAsync` deleted; `Program.fs` calls sync `bootstrap` only; zero HTTP activity at startup, probe fires lazily on first LLM call per port (REF-02)
- New Core record `LlmResponse = { Thought; Output }` carries LLM reasoning through `ILlmClient.CompleteAsync` into `Step.Thought`; `--verbose` now shows real LLM text instead of `"[not captured in v1]"` placeholder (OBS-05)
- Live end-to-end verified: `blueCode --verbose --model 32b "Say OK in 3 words"` → 8s, `thought: "The user wants a simple phrase."`, `final: OK`, exit 0, no HF fetch in server log

**Stats:**

- 2 phases, 5 plans, 218 tests passing (208 v1.0 baseline + 10 v1.1 additions; 1 env-gated smoke still ignored)
- 12 F# files changed, +315 / -124 LOC
- 23 git commits
- ~19 hours from v1.1 start to v1.1 tag (2026-04-23 17:32 → 2026-04-24 12:21)

**Git range:** `a03ff8e` (milestone start) → `4b61819` (Phase 6+7 complete, pre-archive)

**Dual-service stress test:** 3 back-to-back `--model 32b` runs with both 32B + 72B services loaded @ 126GB memory utilization — each run 2s, no OOM, prompt cache grew 0.70 → 0.83 GB (far below the 1.51 GB crash threshold observed pre-06-03 when Base-mode generation was running 1024-token continuations).

**What's next:**

v1.2 candidates (not scoped yet): per-port `MaxModelLen` visibility, streaming output, `edit_file`/`glob_search`/`grep_search` tools, session persistence, `makeMockResponse` shared test helper, multi-platform `tryParseModelId` path detection.

**Audit note:** v1.1 shipped without a formal `/gsd:audit-milestone` pass (same pattern as v1.0). Live verification on 2026-04-24 through real-Qwen `--verbose` run + dual-service stress test served as the practical validation gate.

---

## v1.0 MVP (Shipped: 2026-04-23)

**Delivered:** A strong-typed F# agent loop that drives locally-served Qwen 32B/72B on Mac, replacing the Python `claw-code-agent` as the daily driver.

**Phases completed:** 1-5 (17 plans total — 16 autonomous + 1 human-gated UAT)

**Key accomplishments:**

- 5-loop, one-tool-per-step agent with strict `{thought, action, input}` JSON contract and typed `AgentError`/`ToolResult` DUs that force compile-time exhaustive matching
- Qwen chat completions adapter with 3-stage JSON extraction (bare → brace-nest → fence-strip), runtime schema validation, full error mapping to `AgentError` (no exception leakage), and Spectre spinner
- 4-tool executor (`read_file`, `write_file`, `list_dir`, `run_shell`) with 22-validator bash security chain ported from the Python reference implementation — SecurityDenied gate verified to run before `Process.Start`
- Argu-based CLI with `--model 32b|72b` forced routing, `--verbose` multi-line / `--trace` Serilog Debug JSON mode, multi-turn REPL with `/exit` + Ctrl+D, Ctrl+C cancels-current-turn semantics, and JSONL session log at `~/.bluecode/session_<ts>.jsonl`
- `/v1/models` startup probe with 80%-of-max_model_len intra-turn warning and `bootstrapAsync` pattern alongside sync `bootstrap` for network-free testing
- Daily-driver cutover complete: `claw-code-agent/` retired → UAT real coding task passed end-to-end against live Qwen (72B Instruct, 2 steps, exit 0)

**Stats:**

- 5 phases, 17 plans (16 autonomous + 1 human-gated UAT), 208 tests passing (1 env-gated smoke ignored)
- 5,891 LOC F# across `src/` and `tests/`
- 85 git commits
- ~27 hours from first commit to v1.0 tag (2026-04-22 14:37 → 2026-04-23 17:18)

**Git range:** `c692774` (initial research) → `06f3bd5` (v1.1 todo #1 closed post-milestone)

**What's next:**

v1.1 — OBS-03 dynamic `/v1/models` model id query (replace `Router.modelToName` absolute-path hardcode), decouple 32B cold-start probe from bootstrap, and evaluate `--verbose` thought capture via adjusted `ILlmClient.CompleteAsync` signature.

**Audit note:** v1.0 shipped without a formal `/gsd:audit-milestone` pass. UAT through 05-04 (pre-flight 208-test suite, Fantomas clean, real-task run against live Qwen) served as the practical validation gate. Formal audit practice should start with v1.1.

---
