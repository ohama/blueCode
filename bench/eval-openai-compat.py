#!/usr/bin/env python3
"""Phase 42: empirical OpenAI compatibility probes for mlx_lm.server @ 8001.

Drives a fixed list of HTTP probes against the live 122B service and writes one
JSONL record per probe to ``<output-dir>/probes.jsonl`` (flushed per record so a
crash mid-suite still leaves the prior records on disk for inspection).

This file mirrors ``bench/eval-needle.py`` and ``bench/eval-humaneval-http.py``
in style: it NEVER imports ``mlx_lm`` (that would OOM the launchd-managed 122B
service @ 45GB resident). All probing is pure ``requests``.

Plan 42-01 covered Surfaces 1+2+3 (endpoint coverage, response_format, role
handling) — 10 probes. Plan 42-02 extends this with streaming, tools,
error-surface, schema-rate, multi-call coherence, and N=2 concurrency probes
(15 more entries → 25 total). Plan 42-03 will populate the ``--render`` branch
to produce ``report.md`` from probes.jsonl.

See ``.planning/phases/42-qwen-122b-openai-compat-test/42-RESEARCH.md`` for
preliminary probe results that the suite reproduces verbatim.
"""
import argparse
import json
import sys
import time
from pathlib import Path

import requests

ENDPOINT = "http://127.0.0.1:8001"
MODEL_PATH = "/Users/ohama/llm-system/models/qwen122b"


# ---------------------------------------------------------------------------
# probe — POST helper. Captures raw r.text (NOT r.json()) so malformed bodies
# do not crash the suite. Returns a dict that becomes one JSONL record.
# ---------------------------------------------------------------------------
def probe(label, category, body, expected, severity_hint,
          path="/v1/chat/completions", max_tokens=8, timeout=120):
    """POST ``body`` (merged with model + max_tokens) to ``path``; capture excerpt.

    The probe() helper deliberately avoids r.json(): some probes (malformed
    body, response_format echoes) may return non-JSON or partially-JSON
    bodies, and we want the RAW ``r.text[:300]`` excerpt for the report.

    A probe that crashes (network error, server kill mid-flight) returns an
    "error" record but does NOT abort the suite — Plan 42-01 explicit
    resumability requirement.
    """
    full = {"model": MODEL_PATH, "max_tokens": max_tokens, **body}
    request_excerpt = json.dumps(full)[:300]
    url = f"{ENDPOINT}{path}"
    t0 = time.time()
    try:
        r = requests.post(url, json=full, timeout=timeout)
        elapsed = time.time() - t0
        return {
            "label": label,
            "category": category,
            "method": "POST",
            "path": path,
            "http_code": r.status_code,
            "response_excerpt": r.text[:300],
            "elapsed_s": round(elapsed, 2),
            "expected": expected,
            "severity_hint": severity_hint,
            "request_excerpt": request_excerpt,
        }
    except Exception as e:
        elapsed = time.time() - t0
        return {
            "label": label,
            "category": category,
            "method": "POST",
            "path": path,
            "http_code": None,
            "response_excerpt": None,
            "elapsed_s": round(elapsed, 2),
            "expected": expected,
            "severity_hint": severity_hint,
            "request_excerpt": request_excerpt,
            "error": repr(e),
        }


# ---------------------------------------------------------------------------
# probe_get — GET helper for /v1/models and /health. Same record shape minus
# request_excerpt (no body to echo back).
# ---------------------------------------------------------------------------
def probe_get(label, category, path, expected, severity_hint, timeout=30):
    url = f"{ENDPOINT}{path}"
    t0 = time.time()
    try:
        r = requests.get(url, timeout=timeout)
        elapsed = time.time() - t0
        return {
            "label": label,
            "category": category,
            "method": "GET",
            "path": path,
            "http_code": r.status_code,
            "response_excerpt": r.text[:300],
            "elapsed_s": round(elapsed, 2),
            "expected": expected,
            "severity_hint": severity_hint,
        }
    except Exception as e:
        elapsed = time.time() - t0
        return {
            "label": label,
            "category": category,
            "method": "GET",
            "path": path,
            "http_code": None,
            "response_excerpt": None,
            "elapsed_s": round(elapsed, 2),
            "expected": expected,
            "severity_hint": severity_hint,
            "error": repr(e),
        }


# ---------------------------------------------------------------------------
# probe_stream — Surface 4 (SSE streaming). Iterates ``r.iter_lines()`` and
# classifies each line as keepalive / data-content / data-done / other.
# Emits ONE summary record (chunk counts + first/last chunk shape excerpts +
# elapsed); does not emit per-chunk records. RESEARCH.md Pattern 2 + Pitfall 5.
# ---------------------------------------------------------------------------
def probe_stream(label, category, body, expected, severity_hint,
                 path="/v1/chat/completions", max_tokens=64, timeout=120):
    """POST with stream=True; classify SSE lines; return one summary record.

    Per RESEARCH.md preliminary 9 + Pitfall 5:
      * Each ``data: {...}`` JSON chunk has ``choices[0].delta`` with role
        repeated on EVERY chunk (NON-CONFORMANT vs OpenAI which sets role
        only on the first chunk's delta).
      * ``data: [DONE]`` only appears when ``stream_options.include_usage``
        is true; otherwise the stream just ends after the last content chunk.
      * Keepalive comments (``: keepalive N/M``) interleave with data lines.
    """
    full = {"model": MODEL_PATH, "max_tokens": max_tokens, "stream": True, **body}
    request_excerpt = json.dumps(full)[:300]
    url = f"{ENDPOINT}{path}"
    t0 = time.time()
    chunk_count = 0
    keepalive_count = 0
    role_chunks_count = 0
    content_chunks_count = 0
    tool_calls_chunks_count = 0
    finish_reason_seen = None
    saw_done = False
    other_count = 0
    first_chunk_excerpt = None
    last_chunk_excerpt = None
    first_chunk_keys = None
    last_chunk_keys = None
    try:
        r = requests.post(url, json=full, stream=True, timeout=timeout)
        http_code = r.status_code
        for raw in r.iter_lines(decode_unicode=True):
            if raw is None:
                continue
            line = raw.strip()
            if line == "":
                # SSE event separator (blank line). Skip.
                continue
            if line.startswith(":"):
                # SSE keepalive comment.
                keepalive_count += 1
                continue
            if line.startswith("data: [DONE]"):
                saw_done = True
                continue
            if line.startswith("data: "):
                payload = line[len("data: "):]
                try:
                    obj = json.loads(payload)
                except json.JSONDecodeError:
                    other_count += 1
                    if first_chunk_excerpt is None:
                        first_chunk_excerpt = payload[:200]
                    last_chunk_excerpt = payload[:200]
                    continue
                chunk_count += 1
                if first_chunk_excerpt is None:
                    first_chunk_excerpt = payload[:200]
                    try:
                        first_chunk_keys = list(obj.get("choices", [{}])[0].get("delta", {}).keys())
                    except Exception:
                        first_chunk_keys = None
                last_chunk_excerpt = payload[:200]
                try:
                    delta = obj.get("choices", [{}])[0].get("delta", {}) or {}
                    last_chunk_keys = list(delta.keys())
                    if "role" in delta:
                        role_chunks_count += 1
                    if "content" in delta and delta.get("content"):
                        content_chunks_count += 1
                    if "tool_calls" in delta:
                        tool_calls_chunks_count += 1
                    fr = obj.get("choices", [{}])[0].get("finish_reason")
                    if fr is not None:
                        finish_reason_seen = fr
                except Exception:
                    pass
                continue
            # Anything else.
            other_count += 1
            if first_chunk_excerpt is None:
                first_chunk_excerpt = line[:200]
            last_chunk_excerpt = line[:200]
        elapsed = time.time() - t0
        return {
            "label": label,
            "category": category,
            "method": "POST",
            "path": path,
            "http_code": http_code,
            "chunk_count": chunk_count,
            "keepalive_count": keepalive_count,
            "role_chunks_count": role_chunks_count,
            "content_chunks_count": content_chunks_count,
            "tool_calls_chunks_count": tool_calls_chunks_count,
            "finish_reason_seen": finish_reason_seen,
            "saw_done": saw_done,
            "other_count": other_count,
            "first_chunk_keys": first_chunk_keys,
            "last_chunk_keys": last_chunk_keys,
            "first_chunk_excerpt": first_chunk_excerpt,
            "last_chunk_excerpt": last_chunk_excerpt,
            "elapsed_s": round(elapsed, 2),
            "expected": expected,
            "severity_hint": severity_hint,
            "request_excerpt": request_excerpt,
        }
    except Exception as e:
        elapsed = time.time() - t0
        return {
            "label": label,
            "category": category,
            "method": "POST",
            "path": path,
            "http_code": None,
            "chunk_count": chunk_count,
            "keepalive_count": keepalive_count,
            "role_chunks_count": role_chunks_count,
            "content_chunks_count": content_chunks_count,
            "tool_calls_chunks_count": tool_calls_chunks_count,
            "finish_reason_seen": finish_reason_seen,
            "saw_done": saw_done,
            "other_count": other_count,
            "first_chunk_keys": first_chunk_keys,
            "last_chunk_keys": last_chunk_keys,
            "first_chunk_excerpt": first_chunk_excerpt,
            "last_chunk_excerpt": last_chunk_excerpt,
            "elapsed_s": round(elapsed, 2),
            "expected": expected,
            "severity_hint": severity_hint,
            "request_excerpt": request_excerpt,
            "error": repr(e),
        }


# ---------------------------------------------------------------------------
# PROBES — Plan 42-01 (10) + Plan 42-02 Task 1 (5: streaming + tools) = 15.
# Plan 42-02 Task 2 will append 10 more (Surfaces 5+6+7+8) → 25 total.
# Each entry is a dict so adding fields stays non-breaking.
# Driver dispatches on entry["method"]:
#   GET    -> probe_get
#   STREAM -> probe_stream (Surface 4)
#   POST   -> probe (default; Surfaces 1,2,3, tools)
# Task 2 will add: STAT_N (Surface 5), PAIR (Surface 8).
# ---------------------------------------------------------------------------
PROBES = [
    # ----- Surface 1: endpoint coverage (5 probes) -----
    {
        "label": "01-baseline-chat",
        "category": "endpoint",
        "method": "POST",
        "path": "/v1/chat/completions",
        "body": {"messages": [{"role": "user", "content": "Say hi"}]},
        "max_tokens": 8,
        "expected": "HTTP 200 + content non-empty",
        "severity_hint": "PASS",
    },
    {
        "label": "02-completions-legacy",
        "category": "endpoint",
        "method": "POST",
        "path": "/v1/completions",
        "body": {"prompt": "def fib(n):", "temperature": 0.2},
        "max_tokens": 20,
        "expected": "HTTP 200 + object=text_completion (RESEARCH preliminary 11)",
        "severity_hint": "PASS",
    },
    {
        "label": "03-models-list",
        "category": "endpoint",
        "method": "GET",
        "path": "/v1/models",
        "expected": "HTTP 200 + data[0].id contains 'qwen122b'",
        "severity_hint": "PASS",
    },
    {
        "label": "04-health-endpoint",
        "category": "endpoint",
        "method": "GET",
        "path": "/health",
        "expected": "HTTP 200",
        "severity_hint": "PASS",
    },
    {
        "label": "05-responses-endpoint",
        "category": "endpoint",
        "method": "POST",
        "path": "/v1/responses",
        "body": {"messages": [{"role": "user", "content": "hi"}]},
        "max_tokens": 8,
        "expected": "HTTP 404 (OpenAI 2024 stateful API not implemented)",
        "severity_hint": "LOW",
    },
    # ----- Surface 2: response_format (3 probes) -----
    {
        "label": "06-response-format-json-object",
        "category": "response_format",
        "method": "POST",
        "path": "/v1/chat/completions",
        "body": {
            "messages": [{"role": "user", "content": "Say hi"}],
            "response_format": {"type": "json_object"},
        },
        "max_tokens": 8,
        "expected": "HTTP 200 + prose content (RESEARCH preliminary 1: NON-CONFORMANT, silently ignored)",
        "severity_hint": "HIGH",
    },
    {
        "label": "07-response-format-json-schema-strict",
        "category": "response_format",
        "method": "POST",
        "path": "/v1/chat/completions",
        "body": {
            "messages": [{"role": "user", "content": "Return one user object"}],
            "response_format": {
                "type": "json_schema",
                "json_schema": {
                    "name": "user",
                    "strict": True,
                    "schema": {
                        "type": "object",
                        "properties": {
                            "name": {"type": "string"},
                            "age": {"type": "integer"},
                        },
                        "required": ["name", "age"],
                        "additionalProperties": False,
                    },
                },
            },
        },
        "max_tokens": 80,
        "expected": "HTTP 200 + prose/markdown JSON ignoring strict (RESEARCH preliminary 2: NON-CONFORMANT)",
        "severity_hint": "HIGH",
    },
    {
        "label": "08-response-format-no-rerun-N1",
        "category": "response_format",
        "method": "POST",
        "path": "/v1/chat/completions",
        "body": {
            "messages": [{"role": "user", "content": "Say hi"}],
            "response_format": {"type": "json_object"},
        },
        "max_tokens": 8,
        "expected": "HTTP 200 (one-shot rerun of probe 06; Plan 42-02 may extend to N=10 statistics)",
        "severity_hint": "HIGH",
    },
    # ----- Surface 3: role handling (2 probes) -----
    {
        "label": "09-mid-conv-system-rejected",
        "category": "role",
        "method": "POST",
        "path": "/v1/chat/completions",
        "body": {
            "messages": [
                {"role": "system", "content": "You are helpful"},
                {"role": "user", "content": "hi"},
                {"role": "assistant", "content": "Hello!"},
                {"role": "system", "content": "Now be terse"},
                {"role": "user", "content": "explain"},
            ]
        },
        "max_tokens": 8,
        "expected": "HTTP 404 + 'System message must be at the beginning.' (RESEARCH preliminary 7; confirms blueCode Phase 17-02+20-03 invariant)",
        "severity_hint": "HIGH",
    },
    {
        "label": "10-system-only-at-start",
        "category": "role",
        "method": "POST",
        "path": "/v1/chat/completions",
        "body": {
            "messages": [
                {"role": "system", "content": "You are concise."},
                {"role": "user", "content": "Say hi"},
            ]
        },
        "max_tokens": 8,
        "expected": "HTTP 200 (control case: system only at start is the OpenAI-compliant placement)",
        "severity_hint": "PASS",
    },
    # ----- Surface 4: streaming SSE (4 probes) -----
    {
        "label": "11-stream-baseline",
        "category": "streaming",
        "method": "STREAM",
        "path": "/v1/chat/completions",
        "body": {"messages": [{"role": "user", "content": "count to 3"}]},
        "max_tokens": 12,
        "expected": "role on every content chunk; multiple data chunks; no [DONE] without include_usage (RESEARCH preliminary 9)",
        "severity_hint": "LOW",
    },
    {
        "label": "12-stream-with-usage",
        "category": "streaming",
        "method": "STREAM",
        "path": "/v1/chat/completions",
        "body": {
            "messages": [{"role": "user", "content": "count to 3"}],
            "stream_options": {"include_usage": True},
        },
        "max_tokens": 12,
        "expected": "saw_done=True; final usage chunk emitted (RESEARCH Pitfall 5: [DONE] gated on include_usage)",
        "severity_hint": "LOW",
    },
    {
        "label": "13-stream-finish-stop",
        "category": "streaming",
        "method": "STREAM",
        "path": "/v1/chat/completions",
        "body": {"messages": [{"role": "user", "content": "Say only the word OK and nothing more."}]},
        "max_tokens": 8,
        "expected": "finish_reason='stop' observed before stream end",
        "severity_hint": "LOW",
    },
    {
        "label": "14-stream-finish-length",
        "category": "streaming",
        "method": "STREAM",
        "path": "/v1/chat/completions",
        "body": {"messages": [{"role": "user", "content": "Tell me a long story about a dragon"}]},
        "max_tokens": 8,
        "expected": "finish_reason='length' observed (max_tokens hit)",
        "severity_hint": "LOW",
    },
    # ----- BONUS: tools/tool_choice (1 probe; RESEARCH preliminary 8) -----
    {
        "label": "15-tools-tool-choice-auto",
        "category": "tools",
        "method": "POST",
        "path": "/v1/chat/completions",
        "body": {
            "messages": [{"role": "user", "content": "What is the weather in Paris?"}],
            "tools": [
                {
                    "type": "function",
                    "function": {
                        "name": "get_weather",
                        "description": "Get weather",
                        "parameters": {
                            "type": "object",
                            "properties": {"city": {"type": "string"}},
                            "required": ["city"],
                        },
                    },
                }
            ],
            "tool_choice": "auto",
        },
        "max_tokens": 120,
        "expected": "finish_reason=tool_calls; message.tool_calls[0].function.name=get_weather (RESEARCH preliminary 8 CONFORMANT)",
        "severity_hint": "PASS",
    },
    # NOTE: Task 2 of Plan 42-02 will append probes 16-25 (Surfaces 5+6+7+8).
]


def _dispatch(entry):
    """Dispatch one PROBES entry to the right helper based on entry["method"].

    Supported methods after Plan 42-02 Task 1:
      * GET    -> probe_get
      * STREAM -> probe_stream (Surface 4)
      * POST   -> probe (default; Surfaces 1,2,3, tools)

    Task 2 will add PAIR (Surface 8) and STAT_N (Surface 5) arms.
    """
    label = entry["label"]
    category = entry["category"]
    expected = entry["expected"]
    severity_hint = entry["severity_hint"]
    method = entry.get("method", "POST")
    path = entry.get("path", "/v1/chat/completions")
    if method == "GET":
        return probe_get(label, category, path, expected, severity_hint)
    if method == "STREAM":
        body = entry.get("body", {})
        max_tokens = entry.get("max_tokens", 64)
        return probe_stream(label, category, body, expected, severity_hint,
                            path=path, max_tokens=max_tokens)
    body = entry.get("body", {})
    max_tokens = entry.get("max_tokens", 8)
    return probe(label, category, body, expected, severity_hint,
                 path=path, max_tokens=max_tokens)


def main() -> int:
    p = argparse.ArgumentParser(description="Phase 42 OpenAI-compat probes")
    p.add_argument("--output-dir", required=False,
                   help="directory to write probes.jsonl into (required unless --render)")
    p.add_argument("--render", default=None,
                   help="path to existing probes.jsonl; render markdown report (Plan 42-03 — currently deferred)")
    args = p.parse_args()

    if args.render is not None:
        # Plan 42-03 will populate this branch.
        print("rendering deferred to Plan 42-03", file=sys.stderr)
        return 0

    if not args.output_dir:
        print("ERROR: --output-dir is required (unless --render given)", file=sys.stderr)
        return 2

    out = Path(args.output_dir)
    out.mkdir(parents=True, exist_ok=True)
    out_file = out / "probes.jsonl"
    print(f"PROBES count={len(PROBES)} -> {out_file}", flush=True)
    with out_file.open("w") as fp:
        for entry in PROBES:
            rec = _dispatch(entry)
            fp.write(json.dumps(rec) + "\n")
            fp.flush()
            # Display: single http_code OR codes-list OR PAIR/STAT summary.
            if "http_code" in rec:
                code = rec.get("http_code")
                code_s = str(code) if code is not None else "ERR"
            elif "http_codes" in rec:
                codes = rec.get("http_codes") or []
                code_s = ",".join("ERR" if c is None else str(c) for c in codes)
            else:
                code_s = "?"
            elapsed = (rec.get("elapsed_s")
                       or rec.get("elapsed_s_total")
                       or rec.get("wall_clock_s")
                       or 0.0)
            print(f"[{code_s}] {rec['label']} ({elapsed}s)", flush=True)
    print(f"wrote {out_file}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
