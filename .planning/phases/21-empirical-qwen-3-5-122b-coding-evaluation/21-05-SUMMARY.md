---
phase: 21-empirical-qwen-3-5-122b-coding-evaluation
plan: "05"
subsystem: documentation
tags: [eval, scorecard, verdict, humaneval, reliability, performance, qwen35, 122b]
requires:
  - phase: 21-01
    provides: throughput/TTFT measurements (throughput.json, ttft.json)
  - phase: 21-02
    provides: HumanEval+ pass@1 (humaneval_chat_score.txt, humaneval_completion_score.txt)
  - phase: 21-03
    provides: refactor/diagnose artifacts (refactor_orphan_count.txt, bug_*_diagnose.log)
  - phase: 21-04
    provides: reliability artifacts (schema_rate.txt, needle.json, multiturn_N*/meta)
provides:
  - documentation/qwen35-122b-coding-eval.md (983 lines, 10 sections, 100-pt scorecard verdict)
  - Observation note in .planning/STATE.md
  - Cross-reference in CLAUDE.md Bench section
  - Final bench gate GATE PASS (7/7) confirming milestone integrity
affects:
  - "/gsd:complete-milestone v2.1: eval doc is the deliverable; gate confirms no regression"
tech-stack:
  added: []
  patterns:
    - "Pure documentation aggregation; no new code written"
    - "Scorecard rubric: Correctness 40 + Performance 25 + Reliability 25 + Coding-quality 10"
key-files:
  created:
    - documentation/qwen35-122b-coding-eval.md
    - .planning/phases/21-empirical-qwen-3-5-122b-coding-evaluation/21-05-SUMMARY.md
  modified:
    - .planning/STATE.md (4 lines appended — Observation 21-05)
    - CLAUDE.md (2 lines appended — Bench section cross-reference)
decisions:
  - "CORR-EVAL-02 scored 0/5 (orphan_count=1; all-or-nothing rule; 5-step PLAN-04 limit is real)"
  - "Cold-start scored 0/5 (deferred per scope; honest to keep 100-point total integer)"
  - "Idiomatic F# scored 1/5 (only 1 of 3 reviewed transcripts contained F# code; the other 2 were Python multi-turn sessions — this is a transcript selection artifact, not a model deficiency)"
  - "Final verdict: KEEP (82/100; all dimensions ≥60% of max; multi-turn stable through N=7)"
metrics:
  duration: "~30 minutes (artifact aggregation + doc writing; no live LLM runs)"
  completed: "2026-04-28"
---

# Phase 21 Plan 05: Eval Verdict Document Summary

**One-liner:** Empirical 100-point scorecard for Qwen 3.5 122B coding capability (82/100, Recommendation: KEEP) — highest reliability dimension (25/25); correctness limited by 5-step PLAN-04 multi-file refactor failure.

## What Was Built

- `documentation/qwen35-122b-coding-eval.md` — 983 lines, 10 sections (§1 Methodology through §10 Reproduction), ending with the mandatory verdict line
- `.planning/STATE.md` — observation note appended confirming verdict and gate status
- `CLAUDE.md` — 2-line cross-reference appended under Bench section

## Final Scorecard

| Dimension | Sub-criterion | Score | Max |
|-----------|---------------|-------|-----|
| Correctness | HumanEval+ pass@1 chat (93.9% ≥ 75% top band) | 15 | 15 |
| Correctness | F# bug-fix (3/4 correct at 3.75 pts each) | 11 | 15 |
| Correctness | Language coverage (Python + TypeScript) | 5 | 5 |
| Correctness | Multi-file refactor (orphan_count=1 → FAIL) | 0 | 5 |
| **Correctness subtotal** | | **31** | **40** |
| Performance | Throughput (34.60 tok/s median ≥ 30) | 10 | 10 |
| Performance | TTFT (222 ms median ≤ 500) | 5 | 5 |
| Performance | Cold-start (deferred per scope) | 0 | 5 |
| Performance | End-to-end ±20% baseline (gate 7/7 PASS) | 5 | 5 |
| **Performance subtotal** | | **20** | **25** |
| Reliability | JSON schema rate (0/50 InvalidJsonOutput) | 10 | 10 |
| Reliability | Multi-turn stable through N=7 | 10 | 10 |
| Reliability | Needle 4/4 retrieved at 32k | 5 | 5 |
| **Reliability subtotal** | | **25** | **25** |
| Coding quality | Idiomatic F# (1 of 3 transcripts reviewed) | 1 | 5 |
| Coding quality | Tests compile + pass | 3 | 3 |
| Coding quality | Bug identification (4/4 known issues) | 2 | 2 |
| **Coding quality subtotal** | | **6** | **10** |

**Grand total: 82/100 — Recommendation: KEEP**

## Headline Empirical Numbers

| Measurement | Result | Artifact |
|-------------|--------|----------|
| HumanEval+ pass@1 (chat, headline) | 0.939 | qwen35-eval-20260428-055057/humaneval_chat_score.txt |
| HumanEval+ pass@1+ (chat, strict) | 0.902 | qwen35-eval-20260428-055057/humaneval_chat_score.txt |
| HumanEval+ pass@1 (completion, info) | 0.226 | qwen35-eval-20260428-055057/humaneval_completion_score.txt |
| Throughput median | 34.60 tok/s | qwen35-eval-20260428-052719/throughput.json (15 entries) |
| TTFT median (all 10 trials) | 222 ms | qwen35-eval-20260428-053114/ttft.json |
| TTFT trial 1 (cold) | 929 ms | qwen35-eval-20260428-053114/ttft.json |
| JSON schema compliance | 0/50 failures | qwen35-eval-20260428-095606/schema_rate.txt |
| Multi-turn stability | Stable through N=7 | qwen35-eval-20260428-100537/multiturn_N*/meta |
| Needle retrieval | 4/4 (8k/16k/32k/32768) | qwen35-eval-20260428-100057/needle.json |
| Multi-file refactor | FAIL (orphan_count=1) | qwen35-eval-20260428-093852/refactor_orphan_count.txt |
| Bug diagnose (3 fixtures) | PASS all 3 | qwen35-eval-20260428-093933/*.log |

## Bench Gate Confirmation

**GATE PASS (7/7)** — final regression run at end of Task 3:

```
PASS T6_122b    steps=5/5 exit=0
PASS W1_122b    steps=3/3 exit=0
PASS W2_122b    steps=3/3 exit=0
PASS T1_122b    steps=1/3 exit=0
PASS T5_122b    steps=3/4 exit=0
PASS B2_122b    steps=2/3 exit=0
PASS MT_122b    steps=2/4 exit=0
GATE PASS (7/7)
```

## Source / Baseline Diff Confirmation

- `git diff src/` — empty (no source changes in Phase 21)
- `git diff bench/baseline.json` — empty (gate baseline preserved byte-for-byte)
- `git diff bench/run.sh` — empty (line-18 EXIT trap edit already committed in 21-03)

## Test Count

**282/1/0** (Passed/Ignored/Failed) — unchanged from Phase 21 entry state.

## Eval Doc Path

`documentation/qwen35-122b-coding-eval.md` (983 lines, 10 sections, verdict line on final line)

## Task Commits

| Commit | Hash | Description |
|--------|------|-------------|
| Task 1 | f964419 | docs(21-05): write coding eval verdict doc |
| Task 2a | 673040e | docs(21-05): update STATE.md observation note |
| Task 2b | 412d2a9 | docs(21-05): cross-reference eval doc in CLAUDE.md |
| Plan meta | (this commit) | docs(21-05): complete eval verdict plan |

## Deviations from Plan

None. Plan executed exactly as written. All 3 tasks completed in sequence; artifacts were on disk
as specified; scorecard values matched the expectations in the plan's "Headline empirical numbers"
section. No unexpected failures or surprises.

The scorecard outcome (82/100, KEEP) matches the plan's pre-analysis estimate that the verdict
would likely be KEEP (based on HumanEval+ 93.9% and multi-turn stability through N=7 putting the
total above 80).

## Next Phase

Phase 21 is the only phase in the v2.1 milestone. The orchestrator's responsibility is:
- Phase-meta commit: `docs(21): complete v2.1 empirical Qwen 3.5 122B coding evaluation phase`
  staging ROADMAP.md + STATE.md + REQUIREMENTS.md final updates
- `/gsd:complete-milestone` to archive Phase 21 to `.planning/milestones/v2.1-phases/` and tag

---
*Phase: 21-empirical-qwen-3-5-122b-coding-evaluation*
*Completed: 2026-04-28*
