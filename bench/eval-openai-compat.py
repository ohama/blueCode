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
import concurrent.futures
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
          path="/v1/chat/completions", max_tokens=8, timeout=120,
          raw_body=None, omit_model_field=False, body_model_override=None):
    """POST ``body`` (merged with model + max_tokens) to ``path``; capture excerpt.

    The probe() helper deliberately avoids r.json(): some probes (malformed
    body, response_format echoes) may return non-JSON or partially-JSON
    bodies, and we want the RAW ``r.text[:300]`` excerpt for the report.

    A probe that crashes (network error, server kill mid-flight) returns an
    "error" record but does NOT abort the suite — Plan 42-01 explicit
    resumability requirement.

    Plan 42-02 Task 2 extensions for the error-surface probes (16-19):
      * ``raw_body`` — when set (e.g., the literal string "NOT JSON"), POST it
        as ``data=raw_body`` (NOT JSON-encoded). Used by probe 16 to test
        malformed-body error envelope.
      * ``omit_model_field`` — when True, skip the default MODEL_PATH merge so
        the body has no "model" key. Used by probe 18 to confirm mlx_lm.server's
        silent fallback to ``"default_model"``.
      * ``body_model_override`` — when set, replaces MODEL_PATH with the supplied
        string (e.g., "BOGUS_MODEL"). Used by probe 17 to confirm HF-fetch error.
        SAFETY: never pass a real-but-incorrect HF id; that would swap the
        loaded Instruct tokenizer per CLAUDE.md "Key Seams" / RESEARCH.md
        Pitfall 3.
    """
    if raw_body is not None:
        request_excerpt = raw_body[:300]
    else:
        if omit_model_field:
            full = {"max_tokens": max_tokens, **body}
        elif body_model_override is not None:
            full = {"model": body_model_override, "max_tokens": max_tokens, **body}
        else:
            full = {"model": MODEL_PATH, "max_tokens": max_tokens, **body}
        request_excerpt = json.dumps(full)[:300]
    url = f"{ENDPOINT}{path}"
    t0 = time.time()
    try:
        if raw_body is not None:
            r = requests.post(
                url,
                data=raw_body,
                headers={"Content-Type": "application/json"},
                timeout=timeout,
            )
        else:
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
# probe_concurrent_pair — Surface 8 (concurrency). Submits two POSTs to
# /v1/chat/completions in parallel via ThreadPoolExecutor(max_workers=2).
# Captures both per-request elapsed plus pair-level wall_clock_s.
#
# RESEARCH.md preliminary 10: BatchGenerator is expected to merge the two
# decode streams (wall_clock ≈ max(elapsed_each)). If wall_clock ≈ sum →
# requests serialized → NON-CONFORMANT.
#
# N=2 ONLY per RESEARCH.md Pitfall 1 (don't disrupt daily driver). N>2 knee
# finding is v2.7+ work.
# ---------------------------------------------------------------------------
def probe_concurrent_pair(label, category, body_a, body_b, expected, severity_hint,
                          path="/v1/chat/completions", max_tokens=8, timeout=60):
    """Two simultaneous POSTs; record per-request + pair-wall timings."""
    full_a = {"model": MODEL_PATH, "max_tokens": max_tokens, **body_a}
    full_b = {"model": MODEL_PATH, "max_tokens": max_tokens, **body_b}
    url = f"{ENDPOINT}{path}"

    def _one(full):
        t0 = time.time()
        try:
            r = requests.post(url, json=full, timeout=timeout)
            return {
                "http_code": r.status_code,
                "elapsed_s": round(time.time() - t0, 2),
                "excerpt": r.text[:200],
                "error": None,
            }
        except Exception as e:
            return {
                "http_code": None,
                "elapsed_s": round(time.time() - t0, 2),
                "excerpt": None,
                "error": repr(e),
            }

    pair_t0 = time.time()
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as ex:
        fa = ex.submit(_one, full_a)
        fb = ex.submit(_one, full_b)
        ra = fa.result()
        rb = fb.result()
    wall_clock_s = round(time.time() - pair_t0, 2)
    elapsed_each = [ra["elapsed_s"], rb["elapsed_s"]]
    return {
        "label": label,
        "category": category,
        "method": "POST",
        "path": path,
        "http_codes": [ra["http_code"], rb["http_code"]],
        "elapsed_s_each": elapsed_each,
        "wall_clock_s": wall_clock_s,
        "elapsed_s_sum": round(sum(elapsed_each), 2),
        "elapsed_s_max": round(max(elapsed_each), 2),
        "excerpts": [ra["excerpt"], rb["excerpt"]],
        "errors": [ra["error"], rb["error"]],
        "expected": expected,
        "severity_hint": severity_hint,
        "request_excerpts": [json.dumps(full_a)[:200], json.dumps(full_b)[:200]],
    }


# ---------------------------------------------------------------------------
# PROBES — Plan 42-01 (10) + Plan 42-02 (15) = 25 probes covering Surfaces 1-8.
# Each entry is a dict so adding fields stays non-breaking.
# Driver dispatches on entry["method"]:
#   GET    -> probe_get
#   STREAM -> probe_stream (Surface 4)
#   PAIR   -> probe_concurrent_pair (Surface 8; uses body_a + body_b)
#   STAT_N -> probe() called n_repeats times; aggregated record (Surface 5)
#   POST   -> probe (default; Surfaces 1,2,3,7, tools, coherence)
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
    # ----- Surface 7: error surface (4 probes) -----
    {
        "label": "16-malformed-json-body",
        "category": "error",
        "method": "POST",
        "path": "/v1/chat/completions",
        "body": {},
        "raw_body": "NOT JSON",
        "max_tokens": 8,
        "expected": "HTTP 400 + flat {error:'Invalid JSON in request body...'} envelope (RESEARCH preliminary 5)",
        "severity_hint": "LOW",
    },
    {
        "label": "17-bogus-model-id",
        "category": "error",
        "method": "POST",
        "path": "/v1/chat/completions",
        "body": {"messages": [{"role": "user", "content": "hi"}]},
        "body_model_override": "BOGUS_MODEL",
        "max_tokens": 4,
        "expected": "HTTP 404 + HF-fetch error string (RESEARCH preliminary 4); SAFE: BOGUS_MODEL fails at HF metadata-fetch step BEFORE tokenizer swap",
        "severity_hint": "MEDIUM",
    },
    {
        "label": "18-missing-model-field",
        "category": "error",
        "method": "POST",
        "path": "/v1/chat/completions",
        "body": {"messages": [{"role": "user", "content": "hi"}]},
        "omit_model_field": True,
        "max_tokens": 4,
        "expected": "HTTP 200 + response.model=='default_model' (RESEARCH preliminary 6: silent fallback)",
        "severity_hint": "MEDIUM",
    },
    {
        "label": "19-n-greater-than-1",
        "category": "error",
        "method": "POST",
        "path": "/v1/chat/completions",
        "body": {"messages": [{"role": "user", "content": "hi"}], "n": 3},
        "max_tokens": 4,
        "expected": "HTTP 200 + choices.length==1 (RESEARCH preliminary 3: n silently ignored)",
        "severity_hint": "LOW",
    },
    # ----- Surface 5: schema-rate at temp=0 with response_format (1 stat + 1 control) -----
    {
        "label": "20-response-format-rate-temp0-N5",
        "category": "response_format_stat",
        "method": "STAT_N",
        "n_repeats": 5,
        "path": "/v1/chat/completions",
        "body": {
            "messages": [{"role": "user", "content": "Return one user with name and age as JSON"}],
            "response_format": {"type": "json_object"},
            "temperature": 0.0,
        },
        "max_tokens": 80,
        "expected": "Aggregate valid_json_count vs prose_wrap_count over N=5 — informs response_format efficacy at temp=0",
        "severity_hint": "HIGH",
    },
    {
        "label": "21-no-response-format-rate-temp0-N5",
        "category": "response_format_stat",
        "method": "STAT_N",
        "n_repeats": 5,
        "path": "/v1/chat/completions",
        "body": {
            "messages": [{"role": "user", "content": "Return one user with name and age as JSON"}],
            "temperature": 0.0,
        },
        "max_tokens": 80,
        "expected": "Control: prompt-only JSON ask. Compare to probe 20 — informs RESEARCH Open Question 2",
        "severity_hint": "LOW",
    },
    # ----- Surface 8: concurrency (1 probe, N=2 ONLY per RESEARCH Pitfall 1) -----
    {
        "label": "22-concurrent-pair",
        "category": "concurrency",
        "method": "PAIR",
        "path": "/v1/chat/completions",
        "body_a": {"messages": [{"role": "user", "content": "say A"}]},
        "body_b": {"messages": [{"role": "user", "content": "say B"}]},
        "max_tokens": 8,
        "expected": "wall_clock ≈ max(elapsed_each), not sum — confirms BatchGenerator parallel decode (RESEARCH preliminary 10)",
        "severity_hint": "PASS",
    },
    # ----- Surface 6: multi-call coherence (3 sequential probes; Plan 42-03 joins them) -----
    {
        "label": "23-coherence-call-1",
        "category": "coherence",
        "method": "POST",
        "path": "/v1/chat/completions",
        "body": {"messages": [{"role": "user", "content": "What is 2+2?"}]},
        "max_tokens": 8,
        "expected": "response_excerpt contains '4'; no leakage from prior calls (none yet)",
        "severity_hint": "PASS",
    },
    {
        "label": "24-coherence-call-2",
        "category": "coherence",
        "method": "POST",
        "path": "/v1/chat/completions",
        "body": {"messages": [{"role": "user", "content": "What is 3+3?"}]},
        "max_tokens": 8,
        "expected": "response_excerpt contains '6'; no leakage from probe 23 (must NOT contain '4' as the answer)",
        "severity_hint": "PASS",
    },
    {
        "label": "25-coherence-call-3",
        "category": "coherence",
        "method": "POST",
        "path": "/v1/chat/completions",
        "body": {"messages": [{"role": "user", "content": "What is 5+5?"}]},
        "max_tokens": 8,
        "expected": "response_excerpt contains '10'; no leakage from probes 23/24 (must NOT contain '4' or '6' as the answer)",
        "severity_hint": "PASS",
    },
]


def _dispatch(entry):
    """Dispatch one PROBES entry to the right helper based on entry["method"].

    Supported methods (Plan 42-01 + Plan 42-02):
      * GET    -> probe_get
      * STREAM -> probe_stream (Surface 4)
      * PAIR   -> probe_concurrent_pair (Surface 8)
      * STAT_N -> probe() called n_repeats times; aggregated record (Surface 5)
      * POST   -> probe (default; Surfaces 1,2,3,7, tools, coherence)
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
    if method == "PAIR":
        body_a = entry.get("body_a", {})
        body_b = entry.get("body_b", {})
        max_tokens = entry.get("max_tokens", 8)
        return probe_concurrent_pair(label, category, body_a, body_b,
                                     expected, severity_hint,
                                     path=path, max_tokens=max_tokens)
    if method == "STAT_N":
        return _dispatch_stat_n(entry)
    body = entry.get("body", {})
    max_tokens = entry.get("max_tokens", 8)
    raw_body = entry.get("raw_body")
    omit_model_field = entry.get("omit_model_field", False)
    body_model_override = entry.get("body_model_override")
    return probe(label, category, body, expected, severity_hint,
                 path=path, max_tokens=max_tokens,
                 raw_body=raw_body,
                 omit_model_field=omit_model_field,
                 body_model_override=body_model_override)


def _dispatch_stat_n(entry):
    """Run a probe N times; aggregate JSON-content stats into one record.

    Used by Surface 5 schema-rate probes (20 + 21). Per record:
      * http_codes: list of N status codes
      * valid_json_count: how many .choices[0].message.content parsed as JSON
      * prose_wrap_count: how many content bodies begin with "```json"
      * content_excerpts: leading 200 chars of each repeat's content for
        human inspection in Plan 42-03
      * elapsed_s_total: total wall-clock across N requests
    Aggregate keeps the JSONL one-line-per-probe invariant intact.

    Unlike probe(), this helper fetches the FULL response body (no 300-char
    excerpt cap) so the JSON parse + ```json detection is reliable on
    response_format probes whose content can exceed the cap.
    """
    label = entry["label"]
    category = entry["category"]
    expected = entry["expected"]
    severity_hint = entry["severity_hint"]
    path = entry.get("path", "/v1/chat/completions")
    body = entry.get("body", {})
    max_tokens = entry.get("max_tokens", 80)
    n_repeats = entry.get("n_repeats", 5)
    http_codes = []
    valid_json_count = 0
    prose_wrap_count = 0
    content_excerpts = []
    elapsed_total = 0.0
    full = {"model": MODEL_PATH, "max_tokens": max_tokens, **body}
    request_excerpt = json.dumps(full)[:300]
    url = f"{ENDPOINT}{path}"
    for _ in range(n_repeats):
        t0 = time.time()
        try:
            r = requests.post(url, json=full, timeout=120)
            elapsed_total += time.time() - t0
            http_codes.append(r.status_code)
            try:
                obj = r.json()
                content = (
                    obj.get("choices", [{}])[0].get("message", {}).get("content") or ""
                )
            except Exception:
                content = ""
        except Exception:
            elapsed_total += time.time() - t0
            http_codes.append(None)
            content = ""
        content_excerpts.append(content[:200])
        stripped = content.strip()
        if stripped.startswith("```json"):
            prose_wrap_count += 1
            fenced = stripped[len("```json"):].strip()
            if fenced.endswith("```"):
                fenced = fenced[:-3].strip()
            try:
                json.loads(fenced)
                valid_json_count += 1
            except Exception:
                pass
        elif stripped.startswith("{") or stripped.startswith("["):
            try:
                json.loads(stripped)
                valid_json_count += 1
            except Exception:
                pass
    return {
        "label": label,
        "category": category,
        "method": "STAT_N",
        "path": path,
        "n_repeats": n_repeats,
        "http_codes": http_codes,
        "valid_json_count": valid_json_count,
        "prose_wrap_count": prose_wrap_count,
        "content_excerpts": content_excerpts,
        "elapsed_s_total": round(elapsed_total, 2),
        "expected": expected,
        "severity_hint": severity_hint,
        "request_excerpt": request_excerpt,
    }


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
