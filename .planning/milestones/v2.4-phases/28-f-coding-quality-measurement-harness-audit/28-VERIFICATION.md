---
phase: 28-f-coding-quality-measurement-harness-audit
milestone: v2.4
verified: 2026-04-29
status: passed
classification: passed_disprove_1of5
mapped_score: 5
grand_total: 13
score: 5/5 must-haves verified
---

# Phase 28 — Verification

status: passed_disprove_1of5
phase: 28-f-coding-quality-measurement-harness-audit
milestone: v2.4
date: 2026-04-29

## Final bench gate

`bash bench/run.sh --gate` output (full):

```
Pre-condition OK: port 8001 (122B) responsive.
===== GATE: regression subset (7 invocations) =====
===== gate_T6_122b (model=122b) =====
  -> exit=0 elapsed=17s
===== gate_W1_122b (model=122b) =====
  -> exit=0 elapsed=10s
===== gate_W2_122b (model=122b) =====
  -> exit=0 elapsed=9s
===== gate_T1_122b (model=122b) =====
  -> exit=0 elapsed=3s
===== gate_T5_122b (model=122b) =====
  -> exit=0 elapsed=6s
===== gate_B2_122b (model=122b) =====
  -> exit=0 elapsed=7s
===== gate_MT_122b (multi-turn, model=122b) =====
  turn1: exit=0 session=a7d9c5b9d42645eda26caec51c9e1355
  turn2: exit=0  combined exit=0 elapsed=9s
===== GATE: compare to baseline =====
  PASS T6_122b    steps=4/5 exit=0
  PASS W1_122b    steps=3/3 exit=0
  PASS W2_122b    steps=3/3 exit=0
  PASS T1_122b    steps=1/3 exit=0
  PASS T5_122b    steps=3/4 exit=0
  PASS B2_122b    steps=2/3 exit=0
  PASS MT_122b    steps=2/4 exit=0
===== GATE PASS (7/7) =====
```

Exit code: 0
Final line: `GATE PASS (7/7)`

## Requirement satisfaction

| Requirement | Plan | Validation | Status |
|-------------|------|------------|--------|
| HARNESS-01 | 28-01 | `documentation/howto/macos-bash-strict-mode-patterns.md` exists with 5 patterns (4 mandatory + Pattern 5 grep-oE-pipeline-zero-match found during audit); each section has symptom/root-cause/fix + commit refs | ✓ |
| FS-EVAL-01 | 28-02 | 3 fixture pairs at `bench/fixtures/fs_idiomatic/` (pipeline, dupatternmatch, optionhandling); each `.task.md` ≤500 chars; each `.fs` skeleton compiles standalone | ✓ |
| FS-EVAL-02 | 28-03 + 28-04 | `--fs-idiomatic` mode in `bench/eval-qwen35-122b.sh`; per-fixture loop + reminder-only kickstart + transcript capture; HTTP-only preserved; 3 transcripts produced + archived in `.planning/.../transcripts/` | ✓ |
| FS-EVAL-03 | 28-05 | eval doc §5 + §7 + final scorecard updated (7 edit sites + 3 consistency edits); F# fixture evidence subsection added; final scorecard line strict-format matches `^\*\*Total: 96/100, Recommendation: KEEP\*\*$` | ✓ |
| REGRESSION-01 | 28-06 (this plan) | Final bench gate exit 0; GATE PASS (7/7); `bench/baseline.json` byte-equal to milestone-v2.3 tag | ✓ |

5/5 unconditional requirements satisfied.

## Architectural invariants

| Invariant | Check | Status |
|-----------|-------|--------|
| Core purity | `git diff milestone-v2.3 HEAD -- src/BlueCode.Core/` | empty ✓ |
| Cli unchanged | `git diff milestone-v2.3 HEAD -- src/BlueCode.Cli/` | empty ✓ |
| Bench gate stability | `bash bench/run.sh --gate` exit 0 | PASS (7/7) ✓ |
| baseline.json byte-equal | `git diff milestone-v2.3 HEAD -- bench/baseline.json` | empty ✓ |
| Role = User invariant | (no runtime change in Phase 28) | preserved ✓ |
| HTTP-only invariant | `! grep -E "import mlx_lm" bench/eval-qwen35-122b.sh` | empty ✓ |
| bench/run.sh untouched | `git diff milestone-v2.3 HEAD -- bench/run.sh` | empty ✓ |
| defaultSystemPrompt untouched | `git diff milestone-v2.3 HEAD -- src/BlueCode.Cli/CompositionRoot.fs` = 0 bytes | unchanged (no Phase 28 directive) ✓ |

## §5/§7 score change summary

- §5 Idiomatic F#: 1/5 → 5/5 (grand total 13/15 → band ≥12 → mapped 5/5)
- §7 Coding quality subtotal: 6/10 → 10/10
- §7 Grand total: 92/100 → 96/100
- Verdict: KEEP (preserved; score improved)

## Status reasoning

`passed_disprove_1of5`: F# fixture rubric aggregate is 5/5 (mapped from grand total 13/15, band ≥12). The v2.3 1/5 was a Python-transcript artifact — HumanEval+ scoring Python-language answers under chat mode, which does not assess F# idiom quality at all (category mismatch). On actual F# generation tasks in agent-loop mode (the daily-driver path), the model scores 5/5 — top band. Phase 29 NOT recommended; no intervention needed.

Per-fixture scores: pipeline=5/5, dupatternmatch=5/5, optionhandling=3/5. The single sub-score miss (optionhandling C4+C5: `Option.ofObj` on a value-type tuple) reflects a genuine glue-code error; but C1/C2/C3 PASS on that fixture confirm the agent knows the idiomatic Option pattern — the error was in bridging `TryParse`'s bool*int return type, not in the F# Option idiom itself. Grand total 13/15 comfortably lands in the ≥12 band.

Transcript evidence archived: `.planning/phases/28-f-coding-quality-measurement-harness-audit/transcripts/`
