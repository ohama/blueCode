# Roadmap: blueCode v2.4 Coding Quality

**Status:** In Progress (started 2026-04-29)
**Phases:** 28, 29 (conditional), 30 (3 phases planned; Phase 29 added mid-flight via `/gsd:add-phase` only if Phase 28 data justifies)
**Milestone goal:** Resolve the Coding-quality 6/10 (Idiomatic F# 1/5) sub-score on the eval scorecard via measurement-first discipline. v2.2/v2.3 audit hedged that the 1/5 may be a Python-transcript artifact (HumanEval+ scoring Python answers under chat mode); the real F# generation quality during daily-driver use is not directly measured. Phase 28 designs F# fixtures + measures; Phase 29 (conditional) intervenes if data justifies; Phase 30 closes.

## Overview

v2.4 is a **focused small-to-mid milestone** with conditional structure — two paths from Phase 28 measurement:

- **Path A (1/5 disproved):** Phase 28 measurement shows F# generation is already 3-5/5 on F# fixtures (Python-transcript artifact). Score updated; Phase 29 SKIPPED; Phase 30 closes milestone with the new score (free +2~4 points without code change).
- **Path B (1/5 confirmed):** Phase 28 measurement confirms 1/5 is real on F# fixtures. Phase 29 added via `/gsd:add-phase`: F# style hint added to `defaultSystemPrompt` (mirrors v2.3 P1 pattern); fixtures re-run; score updated. Phase 30 closes.

Either path produces useful empirical data. The conditional structure reflects v2.2/v2.3's data-driven discipline + v2.3's mid-flight `/gsd:add-phase` supersession pattern (now reusable repo knowledge).

**Approach:** Measurement-first → Conditional intervention → Close. Each phase has bench gate regression check at end. HARNESS-AUDIT-01 bundled into Phase 28 (small standalone task; touches the same eval harness file).

**Phase numbering:** Continues from v2.3's Phase 27. v1.0: 1-5; v1.1: 6-7; v1.2: 8/9/9.1; v1.3: 10-11; v1.4: 12-13; v2.0: 14-20; v2.1: 21; v2.2: 22-23; v2.3: 24-27. v2.4 uses 28, (29 conditional), 30.

**Bench gate stability mandatory:** `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)` post-each-phase. Per-fixture step counts unchanged for W1/W2/B2/T1/T5/T6/MT vs v2.3 baseline (which equals v2.2 baseline; preserved through v2.3).

---

## Phases

- [x] **Phase 28: F# Coding Quality Measurement + Harness Audit** — 6 plans (F# fixture design + new `--fs-idiomatic` mode + run + score + eval doc update + HARNESS-AUDIT-01 codification + decision-point at end on Phase 29 trigger) ✓ 2026-04-29
- [ ] **Phase 29 (conditional): F# Style Intervention** — SKIPPED — Phase 28 disprove path triggered (mapped_score 5/5 >= 3; passed_disprove_1of5)
- [x] **Phase 30: Milestone Close** — 1 plan (audit prep; final bench gate; phase-complete commit; v2.4 close-ready) ✓ 2026-04-29

---

## Phase Details

### Phase 28: F# Coding Quality Measurement + Harness Audit

**Goal:** Build proper F# task fixtures, measure idiomatic-F# generation in agent-loop mode (the daily-driver path; no `--plan`), score against rubric, update eval doc §5 honestly. Bundle HARNESS-AUDIT-01 (codify 4 macOS bash-strict-mode patterns into howto) since it touches the same eval harness file. Phase ends with a **decision point**: data confirms 1/5 (Phase 29 triggered) or disproves (close milestone).

**Depends on:** v2.3 milestone (eval doc 92/100 baseline + bench gate 7/7 baseline + `bench/eval-qwen35-122b.sh` v2.3 state with `|| true` PASS-path guard from commit 9f8e06e).

**Requirements:** FS-EVAL-01, FS-EVAL-02, FS-EVAL-03, HARNESS-01, REGRESSION-01

**Success Criteria:**

1. **F# fixtures created** — `bench/fixtures/fs_idiomatic/` contains ≥3 task fixtures, each with `.task.md` (≤500 chars task description) + `.fs` (skeleton with type signatures + holes). Each fixture's `.task.md` mentions ≥1 idiomatic F# pattern (pipeline `|>`, DU pattern matching, `Result.bind`, `Option.map`, record `with`). `.fs` skeletons compile standalone before agent fills holes.
2. **`--fs-idiomatic` mode added to harness** — `bash bench/eval-qwen35-122b.sh --fs-idiomatic` exits 0; produces ≥3 transcript files at `bench/runs/qwen35-eval-<ts>/fs_idiomatic_<fixture>.{diff,transcript.txt}`. Mandatory `launchctl kickstart` pre-flight (v2.3 KV-cache lesson). Per-fixture `git checkout` between fixtures. HTTP-only (no `mlx_lm.load` import).
3. **Eval doc §5 + §7 re-scored** — Each fixture transcript scored on rubric (pipeline used vs imperative; DU pattern matching vs nested if-else; Result/Option chains vs throw-style; idiomatic naming/organization). §5 Idiomatic F# row updated with new score (1/5 if confirmed, 3-5/5 if disproved); §7 Coding-quality subtotal re-aggregated; Total updated; final scorecard line strict-format match `**Total: <N>/100, Recommendation: KEEP**`.
4. **HARNESS-AUDIT-01 codified** — `documentation/howto/macos-bash-strict-mode-patterns.md` exists with 4 sections (one per pattern: set-e/dotnet-exit; grep-c/pipefail; mkdir-before-tee; grep-cE-zero-match-exit-1). Each section has symptom, root cause, canonical fix, commit reference. Optional 5th section if 5th pattern surfaces during the harness audit pass (e.g., `seq M N` countdown noted in v2.1 21-04).
5. **Bench gate 7/7 PASS held** — `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)` after Phase 28 lands. Per-fixture step counts within v2.3 baseline_max for ALL 7 fixtures.
6. **Decision point at phase end** — Phase 28 SUMMARY.md records the §5 score outcome and explicitly states: (a) if 1/5 confirmed → trigger `/gsd:add-phase 29` for F# Style Intervention; (b) if 1/5 disproved (≥3/5) → skip Phase 29; proceed directly to Phase 30 milestone close with new total.

**Plans:** 6 plans

Plans:
- [x] 28-01: HARNESS-AUDIT-01 — write `documentation/howto/macos-bash-strict-mode-patterns.md` capturing 4 patterns with commit refs; light audit pass on `bench/eval-qwen35-122b.sh` for any 5th pattern; bench gate 7/7 PASS hold. (HARNESS-01) ✓ 2026-04-29
- [x] 28-02: F# fixture design — create `bench/fixtures/fs_idiomatic/` with 3 fixtures: `pipeline.fs` + `pipeline.task.md` for `|>` use; `dupatternmatch.fs` for DU exhaustive match; `optionhandling.fs` for `Option.map`/`Option.defaultValue`. Each `.fs` compiles standalone; each `.task.md` ≤500 chars. (FS-EVAL-01) ✓ 2026-04-29
- [x] 28-03: `--fs-idiomatic` mode added to `bench/eval-qwen35-122b.sh` — kickstart pre-flight + per-fixture `git checkout` + agent-loop run + transcript capture; idempotent across runs. (FS-EVAL-02) ✓ 2026-04-29
- [x] 28-04: Run fixtures + score against rubric — `bash bench/eval-qwen35-122b.sh --fs-idiomatic` produces transcripts; manual review per rubric (pipeline / DU pattern matching / Result/Option / idiomatic naming); per-fixture score documented in plan SUMMARY. (FS-EVAL-02) ✓ 2026-04-29
- [x] 28-05: Eval doc §5 + §7 update — apply new score; §5 expanded with F# fixture evidence section; §7 Coding-quality subtotal re-aggregated; Total updated; final scorecard line strict-format match. Atomic commit `docs(28-05): update eval doc with F# fixture evidence`. (FS-EVAL-03) ✓ 2026-04-29
- [x] 28-06: Bench gate 7/7 hold + decision point — `bash bench/run.sh --gate` exit 0; record §5 outcome in 28-VERIFICATION.md + Phase 28 SUMMARY.md; explicit Phase 29 trigger decision (confirmed 1/5 → trigger; disproved → skip). (REGRESSION-01) ✓ 2026-04-29

**Plan dependencies:** 28-01 ↔ 28-02 (independent; can parallelize) → 28-03 → 28-04 → 28-05 → 28-06 (sequential post-fixtures)

**Architectural invariants:**

1. **Core purity** — `src/BlueCode.Core/` UNCHANGED in Phase 28 (Phase 28 is fixtures + harness + docs; no source code edits)
2. **Cli unchanged** — `src/BlueCode.Cli/` UNCHANGED in Phase 28 (Phase 29-conditional is where directive lands if needed)
3. **Bench gate stability** — 7/7 PASS held after each plan
4. **`bench/baseline.json` byte-for-byte preserved**
5. **`Role = User` invariant** — unchanged
6. **Atomic commits** — `feat(28-XX): {name}` for new harness mode + fixtures; `docs(28-XX): {name}` for eval doc + howto; plan-meta separate
7. **HTTP-only invariant** — `bench/eval-*.{sh,py}` MUST NOT import `mlx_lm` (would OOM 122B service); `grep -E "import mlx_lm" bench/` empty post-Phase-28

**Out-of-scope guardrails:**

- DO NOT modify `defaultSystemPrompt` or `planSystemPromptSuffix` (Phase 29 territory if conditional triggers)
- DO NOT modify `bench/baseline.json`
- DO NOT modify `bench/run.sh` (only `bench/eval-qwen35-122b.sh` may gain `--fs-idiomatic` mode)
- DO NOT add fixtures requiring HumanEval+ Python evaluation infrastructure (F#-specific only)
- DO NOT score fixtures by compile-pass / run-pass (idiomatic ≠ correct; rubric is qualitative)
- DO NOT skip kickstart pre-flight for `--fs-idiomatic` runs (KV cache contamination is a real failure mode)

---

### Phase 29 (CONDITIONAL): F# Style Intervention

**Status:** **SKIPPED — Phase 28 disprove path triggered.** mapped_score 5/5 (≥ 3) → passed_disprove_1of5 → Phase 29 not needed. FS-INTERVENE-01..02 marked SKIPPED in REQUIREMENTS.md traceability.

**Goal (proposed):** Add F# style directive to `defaultSystemPrompt` with conditional clause. Re-run F# fixtures from Phase 28; re-score. Mirror v2.3 P1 pattern (Phase 27-01) — directive in `defaultSystemPrompt` (reaches agent-loop), conditional clause keeps it dormant for non-F# tasks.

**Depends on:** Phase 28 complete with confirmed 1/5 score

**Requirements (proposed):** FS-INTERVENE-01, FS-INTERVENE-02, REGRESSION-01 (Phase 29 portion)

**Success Criteria (proposed):**

1. F# style directive added to `defaultSystemPrompt` — conditional clause "When generating F# code, prefer pipelines `|>`, DU pattern matching, `Result.bind`/`Option` chains over imperative if-else"; ≤200 chars including separator; `defaultSystemPrompt` 967 → ≤1167 chars.
2. Bench gate 7/7 PASS hold post-migration — conditional clause dormant for non-F# fixtures (W1/W2 are F# bug-fix, not generation; should not trigger). If gate regresses, ≤2 phrasing iterations allowed; on 3rd phrasing failure, escalate (revert + pause for v2.5 reconsideration).
3. F# fixtures re-run with kickstart pre-flight — score change documented; pre/post transcripts preserved.
4. Eval doc §5 + §7 + final scorecard updated — score change recorded honestly (could be 1/5 → 3/5; could be unchanged if directive insufficient).

**Plans (proposed; finalized via `/gsd:plan-phase 29` if triggered):** 2-3 plans

Plans (proposed):
- [ ] 29-01: F# style directive added to `defaultSystemPrompt`; bench gate 7/7 hold; ≤2 phrasing iteration allowance. (FS-INTERVENE-01)
- [ ] 29-02: Re-run F# fixtures with kickstart pre-flight; score change documented; eval doc §5 + §7 updated. (FS-INTERVENE-02)
- [ ] 29-03 (optional): Phase complete + final bench gate + 29-VERIFICATION.md.

**Architectural invariants (proposed):** mirrors v2.3 Phase 27 — Cli-only edit (`CompositionRoot.fs`); Core unchanged; bench gate hold; `bench/baseline.json` preserved; F# style hint conditional clause is dormant for non-F# bench fixtures.

**Out-of-scope guardrails (proposed):**

- DO NOT modify P1/P2 directives from v2.3 (they remain at v2.3 final positions)
- DO NOT modify `bench/baseline.json`
- DO NOT modify Phase 28 fixtures (re-run only; fixture text invariant)

---

### Phase 30: Milestone Close

**Goal:** Audit + final bench gate + phase-complete docs commit + (if Phase 29 ran) final verdict update.

**Depends on:** Phase 28 (and Phase 29 if conditional triggered)

**Requirements:** REGRESSION-01 (final bench gate verification)

**Success Criteria:**

1. **Final bench gate 7/7 PASS** — `bash bench/run.sh --gate` exits 0 with per-fixture step counts within v2.3 baseline_max.
2. **STATE.md observation note** — Phase 30 + v2.4 milestone close-readiness recorded; Phase 29 outcome (skipped or completed) noted.
3. **Final scorecard line strict-format** — eval doc final line matches `^\*\*Total: \d+/100, Recommendation: KEEP\*\*$` (Total may be 92 unchanged if Phase 29 skipped + score artifact-disproved at same value, or 92→94/96/etc. if score actually moved).
4. **Audit completed** — `/gsd:audit-milestone` returns status:passed; tech debt aggregated; lessons recorded.

**Plans:** 1 plan expected

Plans:
- [x] 30-01: Phase 30 close — final bench gate + STATE/ROADMAP/REQUIREMENTS update + 30-VERIFICATION.md + 30-01-SUMMARY.md + phase-complete docs commit. Triggers `/gsd:audit-milestone` then `/gsd:complete-milestone v2.4`. (REGRESSION-01) ✓ 2026-04-29

**Architectural invariants:**

1. **No source code changes** — Phase 30 is verification + docs only; `git diff src/` empty post-Phase-30
2. **No `bench/baseline.json` modifications**
3. **Bench gate 7/7 PASS post-recovery** — EXIT trap restores fixtures
4. **Strict-format scorecard line** — regex match required
5. **§3.3 cold-start preserved at 5/5; §6.3 cloud non-goal preserved**

---

## Progress

| Phase | Milestone | Requirements | Plans Complete | Status | Completed |
|-------|-----------|--------------|----------------|--------|-----------|
| 28. F# Coding Quality Measurement + Harness Audit | v2.4 | FS-EVAL-01..03, HARNESS-01, REGRESSION-01 (5 reqs) | 6/6 | Complete | 2026-04-29 |
| 29. F# Style Intervention (conditional) | v2.4 | FS-INTERVENE-01..02, REGRESSION-01 (Phase 29 portion) | 0/0 | SKIPPED (passed_disprove_1of5) | 2026-04-29 |
| 30. Milestone Close | v2.4 | REGRESSION-01 (final) | 1/1 | Complete | 2026-04-29 |

---

## Aggregate Verdict Scorecard (target post-v2.4)

| Dimension | v2.3 score | v2.4 target (Path A: 1/5 disproved) | v2.4 target (Path B: 1/5 confirmed + intervention) |
|-----------|-----------|----|----|
| Correctness | 36/40 | 36 (unchanged) | 36 (unchanged) |
| Performance | 25/25 | 25 (unchanged) | 25 (unchanged) |
| Reliability | 25/25 | 25 (unchanged) | 25 (unchanged) |
| Coding quality | 6/10 | **8-10** (idiomatic F# rescored 3-5/5 on F# fixtures) | **8-10** (intervention raises score) |
| **Total** | 92/100 | **94-96** | **94-96** (if intervention works) or 92 (if intervention insufficient) |

**Aggregate verdict: KEEP** preserved in all paths. Coding quality dimension exits the 60% threshold band into solid territory.

---

*Roadmap created: 2026-04-29*
*Last updated: 2026-04-29 — Phase 30 complete (milestone-close gate; final bench gate 7/7 PASS; v2.4 close-ready). All v2.4 phases shipped: 28 ✓, 29 SKIPPED (Path A disprove), 30 ✓. Aggregate verdict 92→96 KEEP. Run `/gsd:audit-milestone` then `/gsd:complete-milestone v2.4`.*
