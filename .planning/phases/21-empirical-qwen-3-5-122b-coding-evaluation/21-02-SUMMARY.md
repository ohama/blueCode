---
phase: 21-empirical-qwen-3-5-122b-coding-evaluation
plan: "02"
subsystem: bench
tags: [humaneval, evalplus, python, http-adapter, sanitize, scoring]
requires: ["21-01"]
provides:
  - bench/eval-humaneval-http.py (HTTP adapter; chat + completion modes)
  - bench/eval-qwen35-122b.sh --humaneval handler with sanitize + RLIMIT_AS workaround
  - bench/runs/qwen35-eval-20260428-055057/humaneval_results.json (164 × 2 = 328 entries)
  - HumanEval+ pass@1 numbers (chat 0.939 / completion 0.226) for 21-05 §2.1, §6.1
affects:
  - "21-03..04: any future --humaneval re-runs will score correctly out-of-the-box"
  - "21-05: chat-mode 0.939 is headline number for §2.1 Correctness verdict"
tech-stack:
  added: []
  patterns:
    - HTTP adapter via requests.post (NEVER mlx_lm.load — would OOM 122B service)
    - evalplus.sanitize step inserted between eval_input transform and evaluate
    - EVALPLUS_MAX_MEMORY_BYTES=-1 env var disables macOS-incompatible RLIMIT_AS setrlimit
key-files:
  created:
    - path: bench/eval-humaneval-http.py
      lines: 159
      why: HumanEval+ HTTP adapter; chat (Mode A) + completion (Mode B) modes; no in-process model load
    - path: bench/runs/qwen35-eval-20260428-055057/
      contents: humaneval_chat.jsonl (164), humaneval_completion.jsonl (164), humaneval_results.json (328), humaneval_*-sanitized.jsonl, humaneval_*_score.txt, *_eval_results.json
      gitignored: yes (per .gitignore bench/runs/)
  modified:
    - path: bench/eval-qwen35-122b.sh
      change: run_humaneval handler now wires real adapter + sanitize + EVALPLUS_MAX_MEMORY_BYTES=-1
commits:
  - hash: 4c6e5af
    type: chore
    msg: "add HumanEval+ HTTP adapter"
  - hash: af817ea
    type: chore
    msg: "wire run_humaneval handler with evalplus scoring"
  - hash: c1e97fb
    type: fix
    msg: "sanitize completions and disable RLIMIT_AS for macOS evalplus"
verify:
  pre:
    - "bench/.venv-eval/bin/python --version" → "Python 3.14.3"
    - "curl localhost:8001/v1/models" alive (122B service PID 44880)
    - "tests 282/1/0; bench gate 7/7 PASS (post-21-01)"
  post:
    - "bash -n bench/eval-qwen35-122b.sh" → syntax OK
    - "bash bench/run.sh --gate" → GATE PASS (7/7)
    - "git diff src/ bench/baseline.json" → empty (zero drift)
    - "ls bench/runs/qwen35-eval-20260428-055057/humaneval_*.{jsonl,json,txt}" → 11 files
gotchas:
  - title: "evalplus.evaluate doubles signatures without sanitize step"
    detail: |
      The chat-mode adapter returns full function definitions (signature + docstring + body)
      because Qwen 3.5 122B's chat output is naturally a complete code block. evalplus.evaluate
      stitches the prompt (which already has the signature + docstring opening) onto the
      completion field — producing a doubled signature/docstring that is unparseable Python.
      Every test fails silently with pass@1=0.000.

      Fix: insert `python -m evalplus.sanitize <eval_input>.jsonl` BEFORE evalplus.evaluate.
      sanitize uses tree-sitter to extract the function body and produces a `<input>-sanitized.jsonl`
      that scores correctly.

      This applies to BOTH chat mode and completion mode — the model often hallucinates the
      signature even on the raw `/v1/completions` endpoint.

      Lesson: ANY future eval against /v1/chat/completions with HumanEval+ MUST sanitize.
      Lesson: completion-mode raw output also needs sanitize (defensive).

  - title: "evalplus RLIMIT_AS setrlimit fails on macOS"
    detail: |
      evalplus.eval.utils.reliability_guard (line 118) calls resource.setrlimit(RLIMIT_AS, ...)
      with maximum_memory_bytes from query_maximum_memory_bytes(). On macOS, the requested
      limit (4 GiB default) exceeds the per-process hard limit, so setrlimit raises
      ValueError("current limit exceeds maximum limit"). Every test subprocess crashes
      pre-execution → pass@1=0.000.

      Fix: set env var EVALPLUS_MAX_MEMORY_BYTES=-1. query_maximum_memory_bytes() returns None
      (line 106-107: if maximum_memory_bytes == -1: return None), and reliability_guard skips
      the setrlimit block (line 115-127 is gated on `if maximum_memory_bytes is not None`).

      Documented in run_humaneval handler comment block. Applies platform-wide; lesson is
      "set this env var for ANY evalplus.evaluate invocation on macOS."

  - title: "evalplus.evaluate caches results across invocations"
    detail: |
      evalplus.evaluate writes a `<samples>_eval_results.json` cache file. On subsequent runs
      against the same samples file, evalplus loads from cache rather than re-evaluating.
      During the diagnostic round-trip, this caused a misleading 0.000 result on the SECOND
      sanitize run because the cache was stale (computed pre-sanitize). Delete the cache
      file before re-scoring to force fresh evaluation.

      This is not a regression-gate concern (each fresh eval run gets a new LOG_DIR), but
      noted for future debugging.
metrics:
  test_count: "282/1/0 (unchanged)"
  bench_gate: "7/7 PASS"
  src_diff: "empty"
  baseline_diff: "empty"
  humaneval_chat_pass1: 0.939
  humaneval_chat_pass1_plus: 0.902
  humaneval_completion_pass1: 0.226
  humaneval_completion_pass1_plus: 0.213
  inferences_total: 328  # 164 chat + 164 completion
  wall_clock_chat_min: 28
  wall_clock_completion_min: 33
  wall_clock_total_min: 61
verdict_inputs_for_21_05:
  - "Chat mode is the headline (mirrors blueCode runtime which always uses /v1/chat/completions)"
  - "0.939 / 0.902 puts Qwen 3.5 122B in the upper tier of open-weight coding models"
  - "0.213 (chat pass@1+) → 90.2% accuracy on the strict (extra-tests) HumanEval+ suite"
  - "Mode B raw completion 0.226 is informational only; not load-bearing for verdict"
  - "Sampling temp=0.2 (eval-standard) deviates from runtime 0.7; eval doc §1 documents this"
---

# 21-02 Summary: HumanEval+ HTTP Adapter

## What was built

Plan 21-02 implements the HumanEval+ correctness measurement (CORR-EVAL-01) for Qwen 3.5 122B via an HTTP adapter that talks to the launchd-managed 122B service on `localhost:8001`. Two modes were measured:

- **Mode A (chat)**: each problem wrapped in `{"role":"user","content":"Complete this Python function:\n\n<problem>"}` and POST'd to `/v1/chat/completions`. Mirrors blueCode's actual runtime use.
- **Mode B (completion)**: raw problem text POST'd to `/v1/completions` for direct comparison to published Qwen 3.5 numbers.

All 164 HumanEval+ problems were measured in both modes (328 total inferences, ~61 min wall-clock). Sampling per `mlx-runner/mlx_llm_eval_guide.md §8` eval-standard: `temperature=0.2, top_p=0.8, top_k=20`. This deviates from blueCode's runtime default of `0.7` (Phase 20-01 in `Router.modelToSamplingParams`) — the deviation is intentional (eval = stable measurement; runtime = creative coding) and documented in the eval doc §1 by 21-05.

## Headline numbers

| Mode | pass@1 | pass@1+ |
|------|--------|---------|
| **Chat (Mode A)** | **0.939** | **0.902** |
| Completion (Mode B) | 0.226 | 0.213 |

The chat-mode score is the headline (mirrors blueCode's runtime use). 0.939 / 0.902 puts Qwen 3.5 122B-A10B-4bit MoE in the upper tier of open-weight coding models. The completion-mode score is informational only and confirms that the model relies on the chat template for proper coding behavior.

## Two scoring bugs found and fixed

The first scoring run produced `pass@1 = 0.000` for both modes. Investigation revealed two independent bugs (neither in the model or the adapter — both in the evalplus tooling on macOS Python 3.14):

1. **Doubled signature trap.** `evalplus.evaluate` stitches the original HumanEval prompt (which ends mid-function-signature with the docstring) onto the completion. Our chat-mode completions returned full function definitions (signature + docstring + body), so the stitched solution had two signatures and was unparseable. Fix: `evalplus.sanitize` step inserted before `evaluate` extracts the body cleanly via tree-sitter.

2. **macOS RLIMIT_AS crash.** `evalplus.eval.utils.reliability_guard` calls `resource.setrlimit(RLIMIT_AS, ...)` with a 4 GiB default that exceeds the macOS per-process hard limit, crashing every test subprocess pre-execution with `ValueError("current limit exceeds maximum limit")`. Fix: set env var `EVALPLUS_MAX_MEMORY_BYTES=-1` to make `query_maximum_memory_bytes()` return `None`, which gates out the setrlimit block in `reliability_guard`.

Both fixes are now baked into the `run_humaneval()` handler in `bench/eval-qwen35-122b.sh` (commit `c1e97fb`). Future re-runs score correctly out-of-the-box. See the gotchas section in this SUMMARY for details and lessons.

## Architectural invariants preserved

- **mlx-runner constraint**: zero `mlx_lm.load()` references in the adapter. All inference goes through HTTP. The launchd-managed 122B (PID 44880, ~45 GB RSS) was unaffected.
- **No `src/` changes**: `git diff src/` empty.
- **No `bench/baseline.json` changes**: `git diff bench/baseline.json` empty.
- **Bench gate stable**: `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)` post-plan.
- **Test count unchanged**: 282/1/0.
- **Role=User invariant**: chat mode uses `Role = "user"` exclusively. Single-turn HumanEval is naturally Role=User-only.

## What's next

Plan 21-03 implements the multi-file F# refactoring measurement (CORR-EVAL-02) and language-coverage micro-tests (CORR-EVAL-03). It depends on 21-02 only via the harness file (`bench/eval-qwen35-122b.sh`) — no data dependency.

Resume signal: `/gsd:execute-phase 21` Wave 3 → 21-03.
