---
phase: 21-empirical-qwen-3-5-122b-coding-evaluation
plan: "01"
subsystem: bench
tags: [bash, evalplus, python, throughput, ttft, sse, venv]
requires: []
provides:
  - bench/eval-qwen35-122b.sh with --setup/--throughput/--ttft handlers
  - bench/.venv-eval/ (gitignored, evalplus 0.3.1 on Python 3.14.3)
  - bench/requirements-eval.txt
affects:
  - "21-02: --humaneval handler appends to eval-qwen35-122b.sh"
  - "21-03: --refactor/--langcoverage handlers append to eval-qwen35-122b.sh"
  - "21-04: --multiturn/--schema-rate/--needle/--coldstart/--full handlers"
  - "21-05: final eval doc cites throughput + TTFT medians from this plan"
tech-stack:
  added:
    - evalplus==0.3.1 (via pip into bench/.venv-eval/, Python 3.14.3)
  patterns:
    - mode-flag dispatch (case statement) mirroring bench/run.sh structure
    - curl_run() HTTP-direct timing helper adapted from bench/run.sh:30-46 run()
    - SSE awk filter with keepalive + broken-pipe suppression (|| true)
    - require_port_8001() pre-condition guard (adapted from bench/run.sh:181-186)
key-files:
  created:
    - bench/eval-qwen35-122b.sh
    - bench/requirements-eval.txt
  modified:
    - .gitignore (added bench/.venv-eval/)
decisions:
  - "temperature=0.2 for eval (mlx-runner eval standard), NOT blueCode's runtime 0.7"
  - "Python 3.14.3 pip succeeded; uv fallback not needed (evalplus 0.3.1 is 3.14-compatible)"
  - "curl exit 23 (broken pipe) must be suppressed with || true after awk exit in SSE TTFT capture"
  - "mlx_lm.server sends role + content combined on first chunk (not role-only); filter /content:/ with !~ /content:\"\"/ captures it correctly"
metrics:
  duration: "~10 minutes (607 seconds wall clock)"
  completed: "2026-04-28"
---

# Phase 21 Plan 01: Harness Scaffolding Summary

**One-liner:** Eval harness scaffold with throughput (34.60 tok/s median) + TTFT (224 ms median) against live 122B service; evalplus 0.3.1 on Python 3.14.3.

## What Was Built

- `bench/eval-qwen35-122b.sh` (250 lines, executable): mode-flag dispatch harness with `--setup`, `--throughput`, `--ttft` handlers fully implemented; 8 stub handlers for 21-02/21-03/21-04.
- `bench/requirements-eval.txt`: single-line `evalplus>=0.3.0` dependency manifest.
- `.gitignore`: `bench/.venv-eval/` exclusion added near existing `bench/runs/` entry.
- `bench/.venv-eval/`: live venv populated with evalplus 0.3.1 (Python 3.14.3, pip succeeded without uv fallback).

## Resolved Python Version

**Python 3.14.3** — pip install succeeded directly. evalplus 0.3.1 is compatible with Python 3.14 (numpy, tree-sitter, transformers all have 3.14 wheels). uv fallback was NOT needed.

## Throughput Results (PERF-EVAL-01)

- **Run:** 5 prompts x 3 trials = 15 entries, max_tokens=512, temperature=0.2
- **Median tok/s:** 34.60
- **Min tok/s:** 31.29
- **Max tok/s:** 34.88
- **Artifact:** `bench/runs/qwen35-eval-20260428-052719/throughput.json` (15 lines, all tokens_per_sec > 0)

## TTFT Results (PERF-EVAL-02)

- **Run:** 10 SSE streaming trials, max_tokens=64, temperature=0.2, prompt=factorial
- **Median TTFT:** 224 ms
- **Range:** 214–929 ms (trial 1 at 929ms likely cache-cold; trials 2-10 stable 214-230ms)
- **All 10 entries:** ttft_ms > 0 (10/10)
- **Artifact:** `bench/runs/qwen35-eval-20260428-053114/ttft.json` (10 lines)

## Bench Gate Post-Plan

**GATE PASS (7/7)** — exit 0.

| Label    | Steps    | Exit |
|----------|----------|------|
| T6_122b  | 5/5      | 0    |
| W1_122b  | 3/3      | 0    |
| W2_122b  | 3/3      | 0    |
| T1_122b  | 1/3      | 0    |
| T5_122b  | 3/4      | 0    |
| B2_122b  | 2/3      | 0    |
| MT_122b  | 2/4      | 0    |

No regressions. `git diff src/` and `git diff bench/baseline.json` both empty.

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| temperature=0.2 for eval | mlx-runner eval standard, distinct from blueCode runtime 0.7; documents sampling context in eval doc §1 |
| Python 3.14 (no uv fallback) | evalplus 0.3.1 pip install succeeded natively |
| curl exit 23 suppressed via `|| true` | set -euo pipefail aborts on broken pipe when awk exits early; subshell captures ttft_ms cleanly |
| SSE first-chunk filter | mlx_lm.server combines role+content on first chunk; `/"content":/ && !/"content":""/` correctly captures it |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed curl exit 23 (broken pipe) aborting run_ttft**

- **Found during:** Task 3, first --ttft run (exit code 23)
- **Issue:** Under `set -euo pipefail`, when awk exits early (after capturing first content chunk), curl gets a broken pipe and returns exit 23. This aborted the subshell capturing `ttft_ms`.
- **Fix:** Added `|| true` after the awk pipeline inside the `ttft_ms=$(...)` subshell assignment.
- **Files modified:** `bench/eval-qwen35-122b.sh`
- **Commit:**

**2. [Observation] SSE first chunk format differs from plan assumption**

- **Found during:** Task 3, raw SSE probe
- **Observation:** mlx_lm.server sends role AND content combined in the first `data:` chunk (e.g., `"delta": {"role": "assistant", "content": "```"}`). The plan assumed a separate role-only chunk. However, the existing awk filter `/"content":/ && !/"content":""/` already handles this correctly (it catches ANY chunk with non-empty content, regardless of whether role is present).
- **Impact:** None — filter works correctly. Documented for 21-05 eval doc §3.

## Next Phase Readiness

Plan 21-01 complete. Ready for 21-02 (`--humaneval` handler). The eval harness script is in place; 21-02 appends the `run_humaneval()` body and related Python eval scripts.

**Carry-forward notes for 21-02:**
- `VENV_PY="bench/.venv-eval/bin/python"` — evalplus available
- evalplus version confirmed 0.3.1
- MODEL_PATH="/Users/ohama/llm-system/models/qwen122b" (local path, not HF id)
- temperature=0.2 eval standard established
