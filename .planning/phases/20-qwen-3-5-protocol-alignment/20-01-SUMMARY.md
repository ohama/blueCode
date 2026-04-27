---
phase: 20
plan: 20-01
name: sampling-params-and-timeout
subsystem: llm-adapter
tags: [sampling-params, qwen-3-5, http-timeout, domain, router, refactor]
requires: [19-02]
provides: [SamplingParams-record, modelToSamplingParams, timeout-300s]
affects: [20-02, 20-03]
tech-stack:
  added: []
  patterns: [domain-record-for-config, router-to-adapter-injection]
key-files:
  created: []
  modified:
    - src/BlueCode.Core/Domain.fs
    - src/BlueCode.Core/Router.fs
    - src/BlueCode.Cli/Adapters/QwenHttpClient.fs
    - documentation/qwen35-install.md
    - CLAUDE.md
decisions:
  - SamplingParams as Domain record (not DU; consistent with LlmRequest/Step/Plan pattern)
  - modelToSamplingParams uses explicit two-case match (no wildcard) for compile-time exhaustiveness
  - modelToTemperature deleted entirely (no tests reference it; single call site rewired in Task 3)
  - HttpClient.Timeout 300s (covers 122B cold-start observed at 240s after launchctl kickstart)
  - Appendix A row added to qwen35-install.md for sampling-parameter mismatch gotcha
metrics:
  duration: ~12 minutes
  completed: 2026-04-27
---

# Phase 20 Plan 01: Sampling Params and Timeout Summary

**One-liner:** SamplingParams domain record + modelToSamplingParams wires Qwen 3.5 model-card values (temp=0.7, top_p=0.8, top_k=20, presence_penalty=0.0) into buildRequestBody; HttpClient timeout raised 180s→300s.

## What Was Built

Phase 20-01 introduced a `SamplingParams` record in `Domain.fs` and a
`modelToSamplingParams : Model -> SamplingParams` function in `Router.fs`, replacing the
v1.0-era `modelToTemperature` (which returned 0.2/0.4 for the retired Qwen 2.5 models).
`QwenHttpClient.buildRequestBody` now emits all four sampling fields per the Qwen 3.5 model
card (temperature=0.7, top_p=0.8, top_k=20, presence_penalty=0.0) for both Qwen35B and
Qwen122B. The HttpClient timeout was raised from 180s to 300s to cover 122B cold-start
scenarios (240s observed after `launchctl kickstart`); the corresponding error message string
and docblocks were updated. `CLAUDE.md` and `documentation/qwen35-install.md` were updated to
reflect the new timeout and to mark the sampling-parameter mismatch gotcha as RESOLVED in
Appendix A.

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | 345f6a0 | feat(20-01): add SamplingParams record to Domain.fs |
| 2 | ba35855 | feat(20-01): add modelToSamplingParams to Router.fs |
| 3 | 2a87b4f | refactor(20-01): rewire buildRequestBody to SamplingParams; raise timeout 180s→300s |
| 4 | 8c8c4e1 | docs(20-01): update CLAUDE.md + qwen35-install.md for sampling/timeout changes |

## Test Count Delta

262/1/0 → 262/1/0 (no change; pure refactor — no new tests in 20-01).

## Bench Gate Result

`bench/run.sh --gate` exit 0, 6/6 PASS post-change:

```
  PASS T6_122b    steps=5/5 exit=0  (elapsed 19s)
  PASS W1_122b    steps=3/3 exit=0  (elapsed 10s)
  PASS W2_122b    steps=3/3 exit=0  (elapsed 11s)
  PASS T1_122b    steps=1/3 exit=0  (elapsed  3s)
  PASS T5_122b    steps=3/4 exit=0  (elapsed  6s)
  PASS B2_122b    steps=2/3 exit=0  (elapsed  7s)
===== GATE PASS (6/6) =====
```

## Files Modified

- `src/BlueCode.Core/Domain.fs` — added `SamplingParams` record (4 fields)
- `src/BlueCode.Core/Router.fs` — replaced `modelToTemperature` with `modelToSamplingParams`
- `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` — rewired `buildRequestBody`; timeout 180→300s; error string updated
- `documentation/qwen35-install.md` — §8 RESOLVED note; Appendix A sampling-param row added
- `CLAUDE.md` — Common Gotchas 180→300s; Runtime Environment sampling param note

## Decisions Made

See `<decisions>` section in `20-01-PLAN.md`:
- **SamplingParams shape:** Domain record (not DU), consistent with existing Domain types.
- **modelToSamplingParams arity:** Explicit two-case match (not wildcard) preserves compile-time exhaustiveness.
- **modelToTemperature deletion:** Confirmed no tests reference it; single call site rewired in Task 3.
- **Timeout rationale:** 300s covers 122B cold-start observed at 240s after `launchctl kickstart`.

## Deviations from Plan

**1. [Rule 1 - Bug/Polish] modelToTemperature reference remained in Router.fs docblock**

- **Found during:** Task 3 verify step (`grep -rn "modelToTemperature" src/`)
- **Issue:** After deleting the shim, the Task 2 commit docblock still said "Replaces v1.0-era modelToTemperature" — causing the plan's strict grep check to return 1 match
- **Fix:** Rewrote the comment phrase to "Replaces the v1.0-era per-model temperature function" — no behavior change
- **Files modified:** `src/BlueCode.Core/Router.fs` (docblock only)
- **Commit:** included in 2a87b4f (Task 3 commit)

## Next Phase

- **20-02** — `extractContent` `reasoning_content` fallback (latent qwen35-install §5.3 gotcha)
- **20-03** — 122B mid-conversation `Role = System` probe + conditional restore
