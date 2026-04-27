#!/usr/bin/env python3
"""HumanEval+ adapter for blueCode 122B via HTTP.

NEVER imports mlx_lm — would OOM the launchd-managed 122B service (~45GB RSS).
All inference goes through HTTP to localhost:8001.

Adapted from mlx-runner/mlx_full_auto_runner.py:27-41 (iteration scaffold).
Replaces in-process generate() with requests.post().

Sampling: temp=0.2 per mlx-runner/mlx_llm_eval_guide.md §8 (eval-standard for coding).
This intentionally differs from blueCode runtime default of 0.7 (Phase 20-01
Router.modelToSamplingParams). Both are right for their context: eval needs stable
measurement; runtime benefits from creative variation. Documented in eval doc §1.
"""
import argparse
import json
import re
import sys
import time
from pathlib import Path

import requests
from evalplus.data import get_human_eval_plus

ENDPOINT = "http://127.0.0.1:8001"
MODEL_PATH = "/Users/ohama/llm-system/models/qwen122b"
TEMPERATURE = 0.2  # eval-standard per mlx-runner/mlx_llm_eval_guide.md §8
TOP_P = 0.8
TOP_K = 20
MAX_TOKENS = 1024  # generous upper bound; HumanEval solutions typically 50-200 tokens
TIMEOUT_S = 120    # per-problem network timeout; 122B can take 30-60s for harder problems

# ---------------------------------------------------------------------------
# Code extraction from chat-mode responses
# ---------------------------------------------------------------------------

CODE_FENCE_RE = re.compile(r"```(?:python)?\s*\n(.*?)```", re.DOTALL)


def extract_code(raw: str, entry_point: str) -> str:
    """Extract Python code from chat-mode response.

    Strategy:
      1. First ```python ... ``` block (or unlabeled ``` block).
      2. Fallback: substring starting from `def <entry_point>` to end of response
         or to next markdown fence, whichever comes first.
      3. If neither matches, return raw (evalplus will mark as failed).
    """
    m = CODE_FENCE_RE.search(raw)
    if m:
        return m.group(1).strip()
    sig_idx = raw.find(f"def {entry_point}")
    if sig_idx >= 0:
        # Take from def to end (or to ``` if any)
        tail = raw[sig_idx:]
        fence_idx = tail.find("```")
        if fence_idx > 0:
            return tail[:fence_idx].strip()
        return tail.strip()
    return raw.strip()


# ---------------------------------------------------------------------------
# Generation calls (the load-bearing replacement of mlx_lm.generate)
# ---------------------------------------------------------------------------

def generate_chat(prompt: str) -> tuple[str, dict]:
    """Mode A: wrap in user-role chat message. Mirrors blueCode runtime use.

    Uses /v1/chat/completions. Role = 'user' only (Role=system mid-conversation
    triggers HTTP 404 on mlx_lm.server; Phase 20-03 invariant — naturally
    satisfied here since HumanEval is single-turn).
    """
    body = {
        "model": MODEL_PATH,
        "messages": [
            {"role": "user", "content": f"Complete this Python function:\n\n{prompt}"}
        ],
        "max_tokens": MAX_TOKENS,
        "temperature": TEMPERATURE,
        "top_p": TOP_P,
        "top_k": TOP_K,
    }
    r = requests.post(f"{ENDPOINT}/v1/chat/completions", json=body, timeout=TIMEOUT_S)
    r.raise_for_status()
    j = r.json()
    text = j["choices"][0]["message"]["content"]
    usage = j.get("usage", {})
    return text, usage


def generate_completion(prompt: str) -> tuple[str, dict]:
    """Mode B: raw completion endpoint, directly comparable to published Qwen 3.5 numbers."""
    body = {
        "model": MODEL_PATH,
        "prompt": prompt,
        "max_tokens": MAX_TOKENS,
        "temperature": TEMPERATURE,
        "top_p": TOP_P,
        "top_k": TOP_K,
    }
    r = requests.post(f"{ENDPOINT}/v1/completions", json=body, timeout=TIMEOUT_S)
    r.raise_for_status()
    j = r.json()
    text = j["choices"][0]["text"]
    usage = j.get("usage", {})
    return text, usage


# ---------------------------------------------------------------------------
# Main loop (mirrors mlx_full_auto_runner.py:27-41 iteration scaffold)
# ---------------------------------------------------------------------------

def run_mode(mode: str, output_path: Path, limit: int | None = None) -> None:
    """Iterate all 164 HumanEval+ problems in `mode` ('chat' or 'completion').

    Writes one JSON object per problem (jsonl) to output_path.
    Problems run sequentially — single 122B service, no parallelism (~10s/problem).

    Time-budget abort aid: pass --limit 10 to check first-10 timing before full run.
    """
    problems = get_human_eval_plus()
    items = list(problems.items())
    if limit is not None:
        items = items[:limit]
    print(f"[{mode}] processing {len(items)} problems", flush=True)
    started = time.time()

    # Track extraction fallback frequency (chat mode only)
    fence_hits = 0
    sig_hits = 0
    raw_fallbacks = 0

    with output_path.open("w") as fp:
        for i, (task_id, problem) in enumerate(items):
            prompt = problem["prompt"]
            entry_point = problem["entry_point"]
            t0 = time.time()
            extraction_method = None
            try:
                if mode == "chat":
                    raw, usage = generate_chat(prompt)
                    # Track extraction method for edge case reporting
                    if CODE_FENCE_RE.search(raw):
                        fence_hits += 1
                        extraction_method = "fence"
                    elif f"def {entry_point}" in raw:
                        sig_hits += 1
                        extraction_method = "sig"
                    else:
                        raw_fallbacks += 1
                        extraction_method = "raw"
                    completion = extract_code(raw, entry_point)
                else:
                    raw, usage = generate_completion(prompt)
                    completion = raw  # raw mode: no extraction needed
                    extraction_method = "raw_completion"
                err = None
            except Exception as e:
                raw = ""
                completion = ""
                usage = {}
                err = repr(e)
                extraction_method = "error"
            elapsed = time.time() - t0
            rec = {
                "task_id": task_id,
                "mode": mode,
                "completion": completion,
                "raw": raw,
                "elapsed_s": round(elapsed, 2),
                "usage": usage,
                "extraction_method": extraction_method,
                "error": err,
            }
            fp.write(json.dumps(rec) + "\n")
            fp.flush()
            if (i + 1) % 10 == 0:
                elapsed_so_far = time.time() - started
                eta_min = elapsed_so_far / (i + 1) * (len(items) - i - 1) / 60
                avg_s = elapsed_so_far / (i + 1)
                print(
                    f"[{mode}] {i+1}/{len(items)} done; avg {avg_s:.1f}s/problem; ETA {eta_min:.1f} min",
                    flush=True,
                )

    total_min = (time.time() - started) / 60
    print(f"[{mode}] complete in {total_min:.1f} min", flush=True)
    if mode == "chat":
        print(
            f"[{mode}] extraction: fence={fence_hits} sig={sig_hits} raw_fallback={raw_fallbacks}",
            flush=True,
        )


def main() -> int:
    p = argparse.ArgumentParser(
        description="HumanEval+ HTTP adapter for blueCode 122B. "
                    "NEVER loads mlx_lm in-process (would OOM 45GB RSS service)."
    )
    p.add_argument(
        "--mode",
        choices=["chat", "completion", "both"],
        default="both",
        help="chat=Mode A (user-role, /v1/chat/completions); "
             "completion=Mode B (raw, /v1/completions); both=run sequentially",
    )
    p.add_argument("--output-dir", required=True, help="bench/runs/qwen35-eval-<ts>")
    p.add_argument(
        "--limit",
        type=int,
        default=None,
        help="Limit problems for time-budget abort check (debug aid; omit for full 164)",
    )
    args = p.parse_args()
    out_dir = Path(args.output_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    modes = ["chat", "completion"] if args.mode == "both" else [args.mode]
    for m in modes:
        out_file = out_dir / f"humaneval_{m}.jsonl"
        run_mode(m, out_file, limit=args.limit)

    # Combine into single humaneval_results.json (164 × len(modes) entries)
    combined = out_dir / "humaneval_results.json"
    with combined.open("w") as fp:
        for m in modes:
            jsonl_path = out_dir / f"humaneval_{m}.jsonl"
            if jsonl_path.exists():
                with jsonl_path.open() as src:
                    for line in src:
                        fp.write(line)
    print(f"combined results: {combined}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
