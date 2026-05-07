#!/usr/bin/env python3
"""Phase 42: empirical OpenAI compatibility probes for mlx_lm.server @ 8001.

Drives a fixed list of HTTP probes against the live 122B service and writes one
JSONL record per probe to ``<output-dir>/probes.jsonl`` (flushed per record so a
crash mid-suite still leaves the prior records on disk for inspection).

This file mirrors ``bench/eval-needle.py`` and ``bench/eval-humaneval-http.py``
in style: it NEVER imports ``mlx_lm`` (that would OOM the launchd-managed 122B
service @ 45GB resident). All probing is pure ``requests``.

Plan 42-01 covers Surfaces 1+2+3 (endpoint coverage, response_format, role
handling) — 10 probes total. Plan 42-02 will extend this with streaming,
schema enforcement, error handling, and concurrency probes (~15 more entries
to PROBES). Plan 42-03 will populate the ``--render`` rendering branch to
produce ``report.md`` from probes.jsonl.

See ``.planning/phases/42-qwen-122b-openai-compat-test/42-RESEARCH.md`` for
preliminary probe results that the Plan 42-01 suite reproduces verbatim.
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
# PROBES — Plan 42-01 fixed suite. 10 entries covering Surfaces 1+2+3.
# Each entry is a dict so adding fields in Plan 42-02 is non-breaking.
# Driver dispatches on entry["method"] (POST default, "GET" routes to probe_get).
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
]


def _dispatch(entry):
    """Dispatch one PROBES entry to probe() or probe_get() based on method."""
    label = entry["label"]
    category = entry["category"]
    expected = entry["expected"]
    severity_hint = entry["severity_hint"]
    method = entry.get("method", "POST")
    path = entry.get("path", "/v1/chat/completions")
    if method == "GET":
        return probe_get(label, category, path, expected, severity_hint)
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
            code = rec.get("http_code")
            code_s = str(code) if code is not None else "ERR"
            elapsed = rec.get("elapsed_s", 0.0)
            print(f"[{code_s}] {rec['label']} ({elapsed}s)", flush=True)
    print(f"wrote {out_file}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
