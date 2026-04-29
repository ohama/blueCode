# Roadmap: blueCode v2.3 Comprehension Layer

**Status:** In Progress (started 2026-04-28)
**Phases:** 24, 25, 26, 27 (4 phases — Phase 27 added 2026-04-29 to address Phase 26 BLOCKED architectural gap)
**Milestone goal:** Resolve the persistent extraction bias on shared-prefix function names that v2.2 audit surfaced as the new bottleneck (CORR-EVAL-02 FAIL x2 with textually identical step-5 thoughts across two completely different READMEs). Multi-prong intervention (P1+P2+P3); verify by re-running CORR-EVAL-02 to PASS (orphan_count=0); flips Correctness 31/40 → 36/40 → Total 87 → 92.

## Overview

v2.3 is a **focused mid-size milestone** — single data-driven candidate (COMP-BIAS-01) decomposed into 3 prongs each addressing the comprehension layer at a different angle:

- **P1 (system prompt enumeration guidance):** Cli-only prompt addition; lowest risk; addresses agent's planning step directly via natural-language directive
- **P2 (few-shot multi-file examples):** Cli-only prompt addition; demonstrates correct enumeration in concrete examples; LLMs respond strongly to few-shot
- **P3 (plan-mode pre-flight rename-target enumeration):** Architectural Core change; new validator pass extracts rename targets from user prompt and verifies plan covers all of them; new `RenameTargetsNotEnumerated` PlanInvalid; 2-attempt retry path

P1+P2 bundled in Phase 24 (same file, same regression watch). P3 separated in Phase 25 (architectural; needs Domain.fs + PlanValidator.fs + tests). Re-eval + verdict update in Phase 26.

Success criterion is pre-defined by v2.2 audit: CORR-EVAL-02 PASS (orphan_count=0). Verdict scorecard expected to flip Correctness 31/40 → 36/40 (Total 87 → 92).

**Approach:** Adapter-first (Phase 24 prompt) → Core (Phase 25 validator) → Verification (Phase 26 re-eval). Each phase has bench gate regression check at end. Up to 3 stochastic re-runs allowed for COMP-05 to account for sampling variance (temp=0.7 introduces some run-to-run variation).

**Phase numbering:** Continues from v2.2's Phase 23. v1.0: 1-5; v1.1: 6-7; v1.2: 8/9/9.1; v1.3: 10-11; v1.4: 12-13; v2.0: 14-20; v2.1: 21; v2.2: 22-23. v2.3 uses 24, 25, 26.

**Bench gate stability mandatory:** `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)` post-each-phase. Per-fixture step counts unchanged for W1/W2/B2/T1/T5/T6/MT vs v2.2 baseline.

---

## Phases

- [x] **Phase 24: Prompt-Level Intervention (P1+P2)** — 2 plans (system prompt enumeration guidance + few-shot multi-file examples; bench gate regression hold) ✓ 2026-04-28
- [x] **Phase 25: Plan-Mode Pre-Flight Enumeration (P3)** — 3 plans (PlanValidator.fs new pre-flight pass [Interpretation B — detail-string encoding within existing PlanInvalid case; no Domain.fs DU change; no Rendering.fs/buildCorrection cascade] + tests +3 + bench gate regression hold) ✓ 2026-04-29
- [ ] **Phase 26: Re-Evaluation (CORR-EVAL-02 PASS + verdict flip)** — 1 plan (re-run --refactor; eval doc §2.4/§7/§8/§9 + final scorecard line 87 → 92) **Status: blocked (2026-04-29) — CORR-EVAL-02 FAIL x3; P1/P2/P3 intervention did not reach agent-loop eval path; hallucination failure mode resolved by kickstart but extraction bias persists; superseded by Phase 27**
- [ ] **Phase 27: Default-Prompt P1 Migration + Re-Eval** — TBD plans (move P1 enumeration directive from `planSystemPromptSuffix` to `defaultSystemPrompt`; re-run CORR-EVAL-02 with kickstart pre-flight; on PASS, flip eval doc verdict 87→92)

---

## Phase Details

### Phase 24: Prompt-Level Intervention (P1+P2)

**Goal:** Add system prompt enumeration guidance (P1) and few-shot multi-file examples (P2) to `CompositionRoot.fs`. Both prongs are Cli-only (no Core changes); bundled because same file + same regression watch (T6/W1/W2/MT step counts must hold). Char budget: ≤1500 chars total `planSystemPromptSuffix` (vs v2.2's 695 baseline).

**Depends on:** v2.2 milestone (eval doc + bench gate 7/7 baseline + system prompt structure from 22-02)

**Requirements:** COMP-01, COMP-02, COMP-04 (regression hold for this phase)

**Success Criteria:**

1. **System prompt enumeration directive added** — `defaultSystemPrompt` or `planSystemPromptSuffix` includes a directive instructing the agent to enumerate ALL rename targets before editing. `grep -F "list ALL targets explicitly" src/BlueCode.Cli/CompositionRoot.fs` matches.
2. **Few-shot multi-file examples added** — Plan-mode prompt includes 1-2 worked examples of correct multi-file refactor plans, at least one explicitly handling shared-prefix case (`add` and `add3`). `grep -F "Example:" src/BlueCode.Cli/CompositionRoot.fs` shows ≥1 plan-mode example.
3. **Char budget held** — `planSystemPromptSuffix` total length ≤1500 chars. Quantify before/after.
4. **Bench gate regression hold** — `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)`. Per-fixture step counts unchanged for ALL 7 fixtures vs v2.2 baseline (T6=4-5/5, W1=3/3, W2=3/3, T1=1/3, T5=3/4, B2=2/3, MT=2/4). If T6 or any fixture regresses (uses more steps): iterate prompt — try alternate phrasing or shorter directives. Do NOT modify `bench/baseline.json`.
5. **No `src/BlueCode.Core/`, `bench/baseline.json`, or `bench/run.sh` body modifications** — Phase 24 changes are confined to `src/BlueCode.Cli/CompositionRoot.fs`.

**Plans:** 2 plans (24-03 optional, only if regression hits)

Plans:
- [ ] 24-01: System prompt enumeration directive (P1) — Cli-only edit; single-line directive added to `planSystemPromptSuffix`. Bench gate verification at end. (COMP-01)
- [ ] 24-02: Few-shot multi-file examples (P2) — Cli-only edit; 1-2 worked examples added inline to `planSystemPromptSuffix`; at least one shared-prefix example. Bench gate verification at end. (COMP-02)
- [ ] 24-03 (optional, only if regression hits): Iteration on prompt phrasing if T6/W1/W2 regresses. Format: `fix(24-XX): iterate enumeration directive to preserve T6 baseline` etc.

**Plan dependencies:** 24-01 → 24-02 (sequential; system prompt baseline established before few-shot extension)

**Architectural invariants:**
1. **Core purity** — `src/BlueCode.Core/**` UNCHANGED in Phase 24 (P1 and P2 are Cli-only)
2. **Bench gate stability** — 7/7 PASS held after each plan
3. **Char budget** — `planSystemPromptSuffix` ≤1500 chars total
4. **`bench/baseline.json` byte-for-byte preserved**
5. **`Role = User` invariant** — unchanged
6. **Atomic commits** — `feat(24-XX): {name}` per task; plan-meta separate

**Out-of-scope guardrails:**
- DO NOT modify `defaultSystemPrompt` (the base prompt) unless P1 directive specifically belongs there
- DO NOT add P3 (Core validator) — that's Phase 25
- DO NOT modify `bench/baseline.json`
- DO NOT add unrelated prompt content (out-of-scope creep)

---

### Phase 25: Plan-Mode Pre-Flight Enumeration (P3)

**Goal:** Architectural intervention — extend Plan validator with a new pre-flight pass that extracts probable rename targets from the user prompt (heuristic regex) and verifies the LLM's plan covers all of them. New `RenameTargetsNotEnumerated` PlanInvalid reason; 2-attempt retry path uses existing PLAN-04 mechanism.

**Depends on:** Phase 24 (P1+P2 prompt-level changes baseline; gate held). Also: v2.0 Phase 14 PlanValidator architecture preserved.

**Requirements:** COMP-03, COMP-04 (regression hold for this phase)

**Success Criteria:**

1. **New PlanInvalid reason** — `Domain.fs` `PlanInvalid` DU extended with `RenameTargetsNotEnumerated of (string list)` variant carrying the missing target names.
2. **New validator pass** — `PlanValidator.fs` exports `checkRenameTargetsEnumerated: userPrompt: string -> Plan -> Result<Plan, AgentError>`. Heuristic extracts rename targets from user prompt (start conservative — match "rename X to Y" patterns; expand if too brittle). Plan steps' `edit_file` calls inspected to confirm coverage.
3. **2-attempt retry wires through** — Existing PLAN-04 retry mechanism handles new `RenameTargetsNotEnumerated` reason without code changes (the retry path already covers all `PlanInvalid` variants).
4. **Tests added** — PlanValidatorTests covers (a) plan covering all targets PASSES; (b) plan missing one target FAILS with named reason; (c) user prompt with no rename targets PASSES (vacuous truth — heuristic returns empty list). Test count grows ≥3.
5. **Bench gate regression hold** — `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)`. The new validator pass must not block existing fixtures (W1/W2/B2/T1/T5/T6/MT use prompts that don't mention "rename"; heuristic returns empty list; vacuous PASS).
6. **Core purity preserved** — No Serilog/Spectre/Argu/HttpClient/file I/O creep into Domain.fs/PlanValidator.fs/AgentLoop.fs. `task {}` only.

**Plans:** 3 plans (Interpretation B chosen — see 25-01 PLAN; original 4-plan ROADMAP split collapsed because Domain.fs has no meaningful change with Interpretation B)

Plans:
- [x] 25-01-PLAN.md — PlanValidator.fs new pre-flight pass + validatePlan signature change + AgentLoop.fs:484 call site + 6 existing PlanValidatorTests calls fixed (atomic F# big-bang commit; 3 files, no valid intermediate build state). Interpretation B: PlanInvalid stays single-string; missing-target list encoded as structured detail string. (COMP-03)
- [x] 25-02-PLAN.md — Three new boundary tests in PlanValidatorTests.fs: PASS (plan covers all targets), FAIL (plan missing one target), vacuous PASS (no rename in prompt). Test count 284 → 287. (COMP-03 / COMP-04 test-count portion)
- [x] 25-03-PLAN.md — Bench gate 7/7 PASS verification + Interpretation B invariant checks (Domain.fs / Rendering.fs / buildCorrection unchanged) + 25-VERIFICATION.md + Phase-complete docs commit. (COMP-04 regression-hold portion)

**Plan dependencies:** 25-01 → 25-02 → 25-03 (sequential; signature change must compile before tests can use it; gate verification must come after all code+test changes land)

**Architectural invariants:**
1. **Core purity (absolute)** — `src/BlueCode.Core/{Domain,PlanValidator,AgentLoop}.fs` no Serilog/Spectre/Argu/HttpClient/file I/O creep
2. **`task {}` only** in Core (no `async {}` literal)
3. **Independent constants pattern preserved** — PlanValidator stays independent of AgentConfig (Phase 22 invariant)
4. **2-attempt retry mechanism unchanged** — new PlanInvalid reason should flow through existing retry path with no special-casing
5. **Heuristic conservatism** — Start with simple "rename X to Y" pattern; expand only if FAIL data shows it's too narrow. Avoid over-engineered heuristics that break on edge cases.
6. **Bench gate stability** — 7/7 PASS held after Phase 25; existing fixtures' prompts don't mention "rename" so heuristic returns empty list (vacuous PASS)
7. **Test count** — 284 → ≥287 (Phase 25 adds ≥3 new tests)
8. **Atomic commits** — `feat(25-XX): {name}` for code; `test(25-XX): {name}` for tests; plan-meta separate

**Out-of-scope guardrails:**
- DO NOT modify `defaultSystemPrompt` or `planSystemPromptSuffix` (Phase 24 territory)
- DO NOT bump max_tokens
- DO NOT enable thinking-mode
- DO NOT modify `bench/baseline.json`
- DO NOT modify existing PlanInvalid variants (extend, don't refactor)
- DO NOT make heuristic too aggressive (false positives = bench gate regressions)

---

### Phase 26: Re-Evaluation (CORR-EVAL-02 PASS + verdict flip)

**Goal:** Empirically validate the multi-prong intervention works — re-run CORR-EVAL-02 with all 3 prongs in place; verify orphan_count=0; update eval doc verdict 87 → 92.

**Depends on:** Phase 24 (P1+P2 shipped) AND Phase 25 (P3 shipped)

**Requirements:** COMP-05, COMP-06

**Success Criteria:**

1. **Pre-flight clean state** — `git checkout -- bench/fixtures/refactor_multifile/` confirms fixtures at canonical state. README is the v2.2 22-04 rewritten 2128-char enumerated version (kept on disk per v2.2 milestone close).
2. **CORR-EVAL-02 PASS empirically** — `bash bench/eval-qwen35-122b.sh --refactor` produces `bench/runs/qwen35-eval-<ts>/refactor_orphan_count.txt` containing `0`; `refactor_multifile_diff.txt` contains `CORR-EVAL-02 PASS:` line. Up to 3 stochastic re-runs allowed for variance.
3. **Eval doc updated** — `documentation/qwen35-122b-coding-eval.md` §2.4 PASS, §7 Verdict scorecard re-aggregated (Multi-file refactor row 0/5 → 5/5; Correctness subtotal 31 → 36; Total 87 → 92), §8 Caveats — multi-file caveat replaced with v2.2 → v2.3 progression note, §9 Re-evaluation — "Comprehension layer fix attempts (v2.3 candidate)" item marked **resolved**, final line: `**Total: 92/100, Recommendation: KEEP**` (strict format match).
4. **Bench gate post-recovery** — `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)` post-eval. EXIT trap restores fixtures cleanly.
5. **STATE.md observation note** — Phase 26 + v2.3 milestone close-readiness recorded.

**Plans:** 1 plan

Plans:
- [ ] 26-01: Re-run CORR-EVAL-02 + flip eval doc verdict 87→92 — pre-flight + `--refactor` invocation (up to 3 stochastic attempts) + 11 eval doc edit sites in 9 logical groups + STATE.md/ROADMAP.md/REQUIREMENTS.md update + 26-VERIFICATION.md + mandatory final bench gate + phase-complete docs commit. (COMP-05, COMP-06) **BLOCKED — CORR-EVAL-02 FAIL x3 (all attempts; new hallucination mode). Partial VERIFICATION written.**

**Plan dependencies:** None internal (single plan); depends on Phase 24 + Phase 25 completion.

**Architectural invariants:**
1. **No code changes** — Phase 26 is verification + documentation only; `git diff src/` empty post-Phase-26
2. **No `bench/baseline.json` modifications**
3. **Bench gate 7/7 PASS post-recovery** — EXIT trap restores fixtures
4. **Strict-format scorecard line** — `**Total: 92/100, Recommendation: KEEP**` regex match required
5. **Test count unchanged** — Phase 26 adds no tests
6. **§3.3 cold-start preserved** — stays at 5/5 from v2.2 Phase 23 (don't accidentally bump or revert)
7. **§6.3 cloud non-goal preserved** — boundary maintained

**Out-of-scope guardrails:**
- DO NOT modify `bench/baseline.json` even if a fixture step count drifts
- DO NOT re-run other CORR-EVAL or REL-EVAL or PERF-EVAL (those dimensions stay at v2.2 baselines)
- DO NOT modify Phase 24/25 code mid-Phase-26 (if FAIL persists, document and pause for diagnosis)

---

### Phase 27: Default-Prompt P1 Migration + Re-Eval

**Goal:** Close the architectural gap surfaced by Phase 26 BLOCKED. The v2.3 multi-prong intervention (P1+P2+P3) was scoped to plan-mode (`planSystemPromptSuffix` is `--plan`-only; PlanValidator runs only in `runPlanTurn`). The CORR-EVAL-02 eval harness invokes `blueCode --verbose --model 122b "<prompt>"` without `--plan`, so none of the v2.3 prongs reached the agent during the eval. Phase 27 moves the P1 enumeration directive into `defaultSystemPrompt` so it reaches agent-loop mode, then re-runs CORR-EVAL-02 (with kickstart pre-flight to clear KV cache) and on PASS flips the eval doc verdict 87 → 92.

**Depends on:** Phase 24 (P1 directive text exists in `planSystemPromptSuffix`) AND Phase 25 (P3 PlanValidator landed; not directly used here but Phase 27 must not regress it). Phase 26 is the BLOCKED diagnostic that motivated this phase.

**Requirements:** COMP-05, COMP-06 (originally mapped to Phase 26; reassigned to Phase 27 since Phase 26 is BLOCKED and superseded). Possibly new COMP-07 if the P1 migration deserves its own requirement record.

**Success Criteria:**

1. **P1 directive moved to `defaultSystemPrompt`** — The 182-char enumeration directive ("When the task requires renaming or restructuring multiple symbols, list ALL targets explicitly...") is removed from `planSystemPromptSuffix` and added to `defaultSystemPrompt`. Net effect: `planSystemPromptSuffix` 1183 → ~1001 chars; `defaultSystemPrompt` 783 → ~965 chars. P2 few-shot example stays in plan-mode (its `Targets:`/`Steps:` format is plan-mode-specific).
2. **Bench gate regression hold** — `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)`. Per-fixture step counts stay within v2.2 baseline_max for all 7 fixtures (T6, W1, W2, T1, T5, B2, MT). The conditional phrasing of the directive ("When the task requires renaming...") is the mitigation — should be no-op on non-rename fixtures, but this is empirical, not provable a priori. If gate regresses, iterate the directive phrasing (similar to v2.2's 22-02 "usage guidance clause" pattern).
3. **CORR-EVAL-02 PASS empirically** — `bash bench/eval-qwen35-122b.sh --refactor` produces `bench/runs/qwen35-eval-<ts>/refactor_orphan_count.txt` containing `0`. Pre-flight `launchctl kickstart -k gui/501/com.ohama.qwen122b` (clears KV cache contamination — discovered as a real failure mode in Phase 26). Up to 3 stochastic re-runs allowed.
4. **Eval doc updated** — `documentation/qwen35-122b-coding-eval.md` 11 edit sites applied (line numbers in 26-RESEARCH.md Q1; may have drifted ±1 if other edits land); strict-format final scorecard line `**Total: 92/100, Recommendation: KEEP**` regex match required.
5. **Mandatory final bench gate post-eval** — `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)` post-eval. EXIT trap restores fixtures cleanly.
6. **STATE.md observation note** — Phase 27 + v2.3 milestone close-readiness recorded.

**Plans:** TBD (run `/gsd:plan-phase 27` to break down)

Plans:
- [ ] TBD (run /gsd:plan-phase 27 to break down)

**Plan dependencies:** TBD

**Architectural invariants:**

1. **Core purity preserved** — `src/BlueCode.Core/` UNCHANGED in Phase 27 (the migration is Cli-only: `CompositionRoot.fs` only)
2. **Bench gate stability MUST hold** — 7/7 PASS preserved post-migration AND post-eval
3. **`bench/baseline.json` byte-for-byte preserved**
4. **`Role = User` invariant unchanged**
5. **No `git add -A` / `git add .`** — explicit file staging only
6. **Strict-format scorecard line** — `^\*\*Total: 92/100, Recommendation: KEEP\*\*$` regex match required (when eval doc is updated)
7. **§3.3 cold-start preserved at 5/5; §6.3 cloud non-goal preserved**
8. **Phase 24/25 source code untouched if Phase 27 FAILs** — same protection pattern as Phase 26 BLOCKED branch

**Out-of-scope guardrails:**

- DO NOT modify P2 few-shot example unless an iteration phase is explicitly added
- DO NOT modify P3 PlanValidator (Phase 25 territory)
- DO NOT modify `bench/baseline.json` even if a fixture step count drifts (iterate the directive instead)
- DO NOT modify `bench/run.sh` body or `bench/eval-qwen35-122b.sh` body
- DO NOT modify Phase 26 BLOCKED commit / VERIFICATION.md — Phase 26 stays as historical record
- DO NOT skip the kickstart pre-flight (KV cache contamination is a real failure mode, not theoretical)

**Phase 26 supersession note:** Phase 26 stays in the roadmap as historical record (BLOCKED). Phase 27 takes over delivery of COMP-05 + COMP-06. The 11-site eval doc edit list from 26-RESEARCH.md is reusable verbatim (line numbers may need re-confirmation if doc has drifted).

---

## Progress

| Phase | Milestone | Requirements | Plans Complete | Status | Completed |
|-------|-----------|--------------|----------------|--------|-----------|
| 24. Prompt-Level Intervention (P1+P2) | v2.3 | COMP-01, COMP-02, COMP-04 (3 reqs) | 2/2 | ✓ Complete | 2026-04-28 |
| 25. Plan-Mode Pre-Flight Enumeration (P3) | v2.3 | COMP-03, COMP-04 (2 reqs) | 3/3 | ✓ Complete | 2026-04-29 |
| 26. Re-Evaluation | v2.3 | COMP-05, COMP-06 (2 reqs) | 0/1 | Blocked (2026-04-29) | - |
| 27. Default-Prompt P1 Migration + Re-Eval | v2.3 | COMP-05, COMP-06 (reassigned from Phase 26) | 0/TBD | Not started | - |

---

## Aggregate Verdict Scorecard (target post-v2.3)

| Dimension | Score | Max | Pct | Δ from v2.2 |
|-----------|-------|-----|-----|-------------|
| Correctness | 36 | 40 | 90.0% | **+5** (CORR-EVAL-02 0→5) |
| Performance | 25 | 25 | 100.0% | unchanged |
| Reliability | 25 | 25 | 100.0% | unchanged |
| Coding quality | 6 | 10 | 60.0% | unchanged |
| **Total** | **92** | **100** | **92%** | **+5** |

**Aggregate verdict: KEEP** — empirically useful for daily F# coding via blueCode (≥80 threshold; all dimensions ≥60%; multi-turn stable through N=7; HumanEval+ chat 93.9%; multi-file refactor PASS post-v2.3 intervention).

---

*Roadmap created: 2026-04-28*
*Last updated: 2026-04-29 — Phase 27 added (Default-Prompt P1 Migration + Re-Eval) to close the architectural gap exposed by Phase 26 BLOCKED. Diagnostic D (kickstart) confirmed hallucination was KV cache contamination but extraction bias is real and reproducible. P1 directive moves from planSystemPromptSuffix → defaultSystemPrompt to reach agent-loop path. COMP-05 + COMP-06 reassigned from Phase 26 to Phase 27. Phase 26 stays as historical BLOCKED record. v2.3 milestone alive; closes when Phase 27 delivers CORR-EVAL-02 PASS + verdict flip 87→92.*
