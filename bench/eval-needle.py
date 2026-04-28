#!/usr/bin/env python3
"""Long-context needle-in-haystack for blueCode 122B via HTTP.

Adapted from mlx-runner/mlx_full_auto_runner.py:43-68. Keeps make_context() verbatim;
replaces in-process generate() with requests.post() to localhost:8001.

NEVER imports mlx_lm — would OOM the launchd-managed 122B service.
"""
import argparse
import json
import random
import sys
import time
from pathlib import Path

import requests

ENDPOINT = "http://127.0.0.1:8001"
MODEL_PATH = "/Users/ohama/llm-system/models/qwen122b"
SECRET = "SECRET_KEY=abc123xyz"
SECRET_QUESTION = "What is the value of SECRET_KEY in the text above? Answer with just the value (e.g., abc123xyz)."

# Filler text — repeat to reach desired token count
FILLER = (
    "The quick brown fox jumps over the lazy dog. "
    "Software engineering is the practice of building reliable systems. "
    "Lorem ipsum dolor sit amet, consectetur adipiscing elit. "
)


def get_max_model_len() -> int:
    """Probe /v1/models for max_model_len (mirrors QwenHttpClient.fs probeModelInfoAsync)."""
    r = requests.get(f"{ENDPOINT}/v1/models", timeout=10)
    r.raise_for_status()
    j = r.json()
    # mlx_lm.server may surface this in different fields; try common ones
    for entry in j.get("data", []):
        for key in ("max_model_len", "max_position_embeddings", "context_window"):
            if key in entry and isinstance(entry[key], int):
                return entry[key]
    # If not exposed, default to a conservative 32k
    return 32768


def make_context(target_tokens: int, secret: str = SECRET) -> tuple:
    """Build a haystack with the secret at a random position.

    Returns (context_text, secret_position_chars).
    Token count is approximated as 0.75 words/token; we generate slightly more text
    than needed and let the model truncate naturally if needed.
    """
    # Approximate: 1 token ≈ 4 chars for English text
    target_chars = target_tokens * 4
    filler_unit = FILLER
    repeats = (target_chars // len(filler_unit)) + 1
    haystack = filler_unit * repeats
    # Inject secret at a random position (avoiding very start/end)
    pos = random.randint(target_chars // 4, (target_chars * 3) // 4)
    haystack = haystack[:pos] + " " + secret + " " + haystack[pos:]
    return haystack, pos


def query_secret(haystack: str) -> tuple:
    """POST haystack + question; return (answer_text, elapsed_s, completion_tokens)."""
    body = {
        "model": MODEL_PATH,
        "messages": [
            {"role": "user", "content": haystack + "\n\n" + SECRET_QUESTION},
        ],
        "max_tokens": 64,
        "temperature": 0.2,
        "top_p": 0.8,
        "top_k": 20,
    }
    t0 = time.time()
    r = requests.post(f"{ENDPOINT}/v1/chat/completions", json=body, timeout=300)
    r.raise_for_status()
    elapsed = time.time() - t0
    j = r.json()
    text = j["choices"][0]["message"]["content"]
    ct = j.get("usage", {}).get("completion_tokens", 0)
    return text, elapsed, ct


def main() -> int:
    p = argparse.ArgumentParser(description="Long-context needle for blueCode 122B")
    p.add_argument("--output", required=True, help="bench/runs/qwen35-eval-<ts>/needle.json")
    p.add_argument("--sizes", default="8000,16000,32000,65536",
                   help="comma-separated target token counts (capped at MaxModelLen, deduped)")
    args = p.parse_args()

    max_len = get_max_model_len()
    print(f"server max_model_len={max_len}", flush=True)
    raw_sizes = [int(s) for s in args.sizes.split(",")]
    # Cap each size at MaxModelLen, then dedup while preserving order. Rationale: ROADMAP SC2
    # and REQUIREMENTS.md REL-EVAL-03 target up to 4 size entries. On a 32k-ceiling system,
    # the 4th entry (65536) caps to 32k which duplicates the 3rd entry (32000); we drop the dup
    # so the result has 3 unique entries. On 128k+ ceiling systems, all 4 unique sizes materialize.
    capped = [min(s, max_len) for s in raw_sizes]
    sizes = []
    for s in capped:
        if s not in sizes:
            sizes.append(s)
    if not sizes:
        print(f"ERROR: all requested sizes exceed server ceiling {max_len}", file=sys.stderr)
        return 2
    print(f"resolved sizes (capped, deduped): {sizes}", flush=True)

    results = []
    for size in sizes:
        haystack, pos = make_context(size)
        print(f"size={size} tokens (~{len(haystack)} chars), secret at char {pos}", flush=True)
        try:
            answer, elapsed, ct = query_secret(haystack)
            retrieved = "abc123xyz" in answer
            print(f"  retrieved={retrieved} elapsed={elapsed:.1f}s answer={answer[:80]!r}", flush=True)
            results.append({
                "size_tokens": size,
                "secret_position_chars": pos,
                "haystack_chars": len(haystack),
                "answer": answer,
                "retrieved": retrieved,
                "elapsed_s": round(elapsed, 2),
                "completion_tokens": ct,
                "error": None,
            })
        except Exception as e:
            results.append({
                "size_tokens": size,
                "secret_position_chars": pos,
                "haystack_chars": len(haystack),
                "answer": None,
                "retrieved": False,
                "elapsed_s": None,
                "completion_tokens": 0,
                "error": repr(e),
            })

    # Also include max_len for cross-reference (not a result row, but useful)
    out_path = Path(args.output)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with out_path.open("w") as fp:
        json.dump({"max_model_len": max_len, "results": results}, fp, indent=2)
    print(f"wrote {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
