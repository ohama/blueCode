---
phase: 28-f-coding-quality-measurement-harness-audit
plan: 06
subsystem: testing
tags: [fsharp, eval, rubric, scoring, phase-close, verification, bench, milestone]

# Dependency graph
requires:
  - phase: 28-f-coding-quality-measurement-harness-audit
    provides: 28-05 eval doc update (a36cc35); classification passed_disprove_1of5
provides:
  - 28-VERIFICATION.md (status=passed_disprove_1of5; bench gate 7/7 PASS; 5/5 reqs satisfied)
  - Phase 28 closed
  - Phase 29 SKIP decision (explicit; user-gated)
  - STATE/ROADMAP/REQUIREMENTS updated
affects:
  - Phase 30 (proceed directly; milestone close)
  - ROADMAP.md Phase 28 checkbox + Progress table
  - REQUIREMENTS.md traceability (5 unconditional complete; 2 conditional SKIPPED)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Phase close pattern: VERIFICATION.md + SUMMARY.md + STATE/ROADMAP/REQUIREMENTS + phase-complete bundle commit"

key-files:
  created:
    - .planning/phases/28-f-coding-quality-measurement-harness-audit/28-VERIFICATION.md
    - .planning/phases/28-f-coding-quality-measurement-harness-audit/28-06-SUMMARY.md
  modified:
    - .planning/STATE.md
    - .planning/ROADMAP.md
    - .planning/REQUIREMENTS.md

key-decisions:
  - "passed_disprove_1of5: grand total 13/15 >= 12 band -> 5/5 -> Phase 29 SKIP"
  - "Phase 29 SKIP is definitive (not deferred): FS-INTERVENE-01..02 marked SKIPPED in REQUIREMENTS.md"
  - "Proceed directly to /gsd:plan-phase 30 (Milestone Close)"

# Metrics
duration: 10min
completed: 2026-04-29
---

# 28-06 SUMMARY — Phase 28 close (F# Coding Quality Measurement + Harness Audit)

**Phase 28 closed: classification passed_disprove_1of5 (mapped_score 5/5; grand_total 13/15). Phase 29 SKIPPED. Bench gate 7/7 PASS preserved. Total 92/100 → 96/100.**

## Plans completed in Phase 28

| Plan | Title | Status | Atomic commits |
|------|-------|--------|----------------|
| 28-01 | HARNESS-AUDIT-01 (macOS bash-strict-mode howto) | ✓ | `94d905c` fix(28-01): guard grep-oE pipeline with \|\| true; `280677a` docs(28-01): write macOS bash-strict-mode howto |
| 28-02 | F# fixture design | ✓ | `25eb35d` feat(28-02): add F# idiomatic fixtures |
| 28-03 | --fs-idiomatic harness mode | ✓ | `b7611a8` feat(28-03): add --fs-idiomatic eval harness mode |
| 28-04 | Run + score fixtures | ✓ | `1413111` docs(28-04): score F# fixtures against rubric (13/15 → 5/5 → passed_disprove_1of5) |
| 28-05 | Eval doc §5 + §7 update | ✓ | `a36cc35` docs(28-05): update eval doc verdict 92→96 |
| 28-06 | Gate hold + decision (this plan) | ✓ | docs(28): complete F# coding quality measurement phase |

## Final scorecard

- Final scorecard line (verbatim): `**Total: 96/100, Recommendation: KEEP**`
- Score delta: 92/100 → 96/100 (+4 from §7 Coding-quality gain: 6→10; §5 Idiomatic F# 1/5 → 5/5)
- Verdict: KEEP (preserved; improved margin)

## Bench gate

`bash bench/run.sh --gate`: exit 0; `GATE PASS (7/7)` — full transcript in 28-VERIFICATION.md.

All 7 fixtures within v2.3 baseline_max: T6(4/5), W1(3/3), W2(3/3), T1(1/3), T5(3/4), B2(2/3), MT(2/4).

## Phase 29 trigger decision

**Verdict from 28-04-SUMMARY.md:** `passed_disprove_1of5`

### Path A — passed_disprove_1of5 (M >= 3) — ACTIVE

**Recommendation: SKIP Phase 29; proceed directly to Phase 30 milestone close.**

Rationale: F# fixture aggregate score is 5/5 (mapped). The v2.3 1/5 was a Python-transcript artifact — HumanEval+ scores Python-language answers, which cannot assess F# idiomatic quality (category mismatch per v2.2 audit Caveat 6). On actual F# generation in agent-loop mode (the daily-driver path), the model is at 5/5 — top band. Grand total 13/15 is solidly in the ≥12 band. No directive intervention needed.

Evidence:
- pipeline fixture: 5/5 (|> chain; no mutable; correct output 112)
- dupatternmatch fixture: 5/5 (exhaustive match; _ for decoy field; correct areas)
- optionhandling fixture: 3/5 (C1/C2/C3 PASS; C4/C5 FAIL — glue error on TryParse bridge, not idiom error)

User next steps:
1. `/gsd:plan-phase 30` (Milestone Close)
2. NOT `/gsd:add-phase 29` — skipped; FS-INTERVENE-01..02 marked SKIPPED in REQUIREMENTS.md

## Architectural invariants (final)

All 8 invariants confirmed (see 28-VERIFICATION.md for full table):
- `git diff milestone-v2.3 HEAD -- src/` empty ✓
- `git diff milestone-v2.3 HEAD -- bench/baseline.json` empty ✓
- `git diff milestone-v2.3 HEAD -- bench/run.sh` empty ✓
- `git diff bench/fixtures/fs_idiomatic/` empty ✓
- HTTP-only invariant: `! grep -E "import mlx_lm" bench/eval-qwen35-122b.sh` ✓
- defaultSystemPrompt untouched ✓
- Role = User invariant preserved ✓
- Bench gate 7/7 PASS ✓

## Lessons captured

- **HARNESS-AUDIT-01 codified:** Future bash handler authors have a single discoverable howto for macOS bash-strict-mode patterns (`documentation/howto/macos-bash-strict-mode-patterns.md`) — 5 patterns with symptom/root-cause/fix + commit refs. Common rule: under `set -euo pipefail`, `grep -c`/`grep -cE`/`grep -oE` exits 1 on zero-match; guard with `|| true`.
- **F# fixture infrastructure reusable:** Future milestones can extend `bench/fixtures/fs_idiomatic/` with new fixtures + re-run via `bash bench/eval-qwen35-122b.sh --fs-idiomatic` (no harness changes needed; pattern is documented).
- **Measurement-first discipline preserved:** §5 score change is evidence-based, not speculative. Prior 1/5 was a methodology artifact (Python-only transcripts) — identified and corrected without code change. Transcripts archived under `.planning/phases/28-.../transcripts/` for audit reproducibility.
- **Band table vs arithmetic mean:** Grand total 13/15 → ≥12 band → 5/5; arithmetic mean would give 4.33/5 → 4/5. Band table overrides — correct for "even with one imperfect fixture, aggregate quality is top-band" cases.
- **Phase 29 SKIP is definitive:** Classification `passed_disprove_1of5` (M ≥ 3 per ROADMAP §Phase 28 SC #6 path (b)) closes the conditional path permanently. FS-INTERVENE-01..02 marked SKIPPED in REQUIREMENTS.md traceability table.

## Files created or modified in Phase 28

Created:
- `documentation/howto/macos-bash-strict-mode-patterns.md` — 5 patterns (28-01)
- `bench/fixtures/fs_idiomatic/pipeline.task.md`, `pipeline.fs` — pipeline fixture (28-02)
- `bench/fixtures/fs_idiomatic/dupatternmatch.task.md`, `dupatternmatch.fs` — DU fixture (28-02)
- `bench/fixtures/fs_idiomatic/optionhandling.task.md`, `optionhandling.fs` — Option fixture (28-02)
- `.planning/phases/28-.../transcripts/fs_idiomatic_pipeline.transcript.txt` (28-04)
- `.planning/phases/28-.../transcripts/fs_idiomatic_dupatternmatch.transcript.txt` (28-04)
- `.planning/phases/28-.../transcripts/fs_idiomatic_optionhandling.transcript.txt` (28-04)
- `.planning/phases/28-.../transcripts/fs_idiomatic_pipeline.diff` (28-04)
- `.planning/phases/28-.../transcripts/fs_idiomatic_dupatternmatch.diff` (28-04)
- `.planning/phases/28-.../transcripts/fs_idiomatic_optionhandling.diff` (28-04)
- `.planning/phases/28-.../transcripts/meta.txt` (28-04)
- `.planning/phases/28-.../28-VERIFICATION.md` (28-06 this plan)

Modified:
- `bench/eval-qwen35-122b.sh` — grep-oE guard fix + `--fs-idiomatic` handler added (28-01 + 28-03)
- `documentation/qwen35-122b-coding-eval.md` — §5 + §7 + final scorecard + Caveat 7 + "exactly 60%" qualifier (28-05)

## Performance

- **Duration:** ~10 min
- **Tasks:** 3
- **Files created:** 2 (VERIFICATION.md + SUMMARY.md); 3 planning docs updated (STATE + ROADMAP + REQUIREMENTS)

## Deviations from Plan

None — plan executed exactly as written.
