# Phase 42: Qwen 122B OpenAI compatibility verification — Research

**Researched:** 2026-05-06
**Domain:** Empirical OpenAI-API conformance testing of `mlx_lm.server` 0.31.3 @ localhost:8001
**Confidence:** HIGH (own-server probing) / MEDIUM (third-party-comparable reports)

## Summary

This phase is **measurement work**, not a feature build. The deliverable is an empirical
test transcript + per-finding severity table + CLAUDE.md mitigations, NOT new src/. The
investigation surface (8 items in ROADMAP §Phase 42) maps to ~30 individual probes
against the live 122B service, each producing one record (HTTP code + body excerpt +
expected vs observed shape).

**Key methodology decisions taken from preliminary probes during research** (full results
documented under "Code Examples" §preliminary-probes below — these are FACTS that should
be re-verified with the test harness, not assumed):

1. The server is live and responsive from a sandbox shell (`curl http://127.0.0.1:8001/v1/models` returns 200, ~5ms).
2. `mlx_lm.server` 0.31.3 source code (`mlx_lm/server.py`) routes 3 POST endpoints
   (`/v1/completions`, `/v1/chat/completions`, `/chat/completions`) and 2 GET endpoints
   (`/v1/models`, `/health`). The official `SERVER.md` documents only `/v1/chat/completions`
   + `/v1/models` — `/v1/completions` is undocumented but functional.
3. `response_format: {type: "json_object"}` and `response_format: {type: "json_schema", ...}`
   are SILENTLY IGNORED (HTTP 200, prose output emitted). This contradicts blog claims
   and the `mlx-openai-server` (a different fork) feature set.
4. Tools / `tool_choice: "auto"` / `tool_calls` ARE FULLY FUNCTIONAL — major upgrade from
   v1.0 SUMMARY.md "tool_calls absent" assumption. `finish_reason: "tool_calls"` and
   proper OpenAI envelope shape. **This may unlock a v2.7+ design simplification**, but
   v2.6 won't consume it (custom JSON schema plan well-validated; switching mid-milestone
   = scope creep).
5. Error envelope is `{"error": "<flat string>"}`, **NOT** OpenAI's `{error:{message,type,code}}`.
   `LlmUnreachable` mapping in QwenHttpClient.fs already handles this opaquely (200-char
   snippet) — no breakage, but error parsing for downstream tooling is non-portable.
6. `n: 3` is silently ignored (returns 1 choice). Missing `model` field silently maps to
   `"default_model"` and returns 200 against the loaded model. Missing-model-id when no
   model loaded → HTTP 404 with HuggingFace-fallback error string (the same trap
   documented in CLAUDE.md "Connection refused or 300s timeout").
7. SSE streaming repeats `"role": "assistant"` on EVERY chunk (not just the first as in
   OpenAI). Existing `bench/eval-qwen35-122b.sh run_ttft` awk filter `/"content":/ &&
   !/"content":""/` is correct against this behavior; OpenAI-strict clients that filter
   on `delta.role` presence would mis-read.
8. Concurrent requests batch via `BatchGenerator` (default `--decode-concurrency 32`,
   `--prompt-concurrency 8`) — both requests finished in 1.06s. **NO 429** is ever
   returned. Capacity overflow waits indefinitely on the internal Queue. This means
   wave-parallel exec (Phase F, deferred to v2.7+) is feasible from a server-capacity
   standpoint, contradicting the assumption that single-model = single-request.

**Primary recommendation:** EXTEND `bench/eval-qwen35-122b.sh` with a new `--openai-compat`
mode handler + `bench/eval-openai-compat.py` Python helper. Output transcript at
`bench/runs/openai-compat-<ts>/probes.jsonl` (one record per probe) + render markdown
report into `documentation/qwen35-122b-openai-compat.md`. Keep harness style consistent
with §1.5 of `documentation/qwen35-122b-coding-eval.md` (bash mode-flag dispatch + Python
venv for anything fancier than `curl + jq`). **DO NOT** introduce a new F# test harness
or third-party conformance suite — see "Don't Hand-Roll" §1.

## Standard Stack

The established libraries/tools for this domain (HTTP API conformance testing of a
local LLM server):

### Core

| Tool | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| `curl` | system | HTTP probes; SSE streaming via `-N`; status capture via `-w "%{http_code}"` | already used by `bench/eval-qwen35-122b.sh:curl_run`, no new dep |
| `jq` 1.7+ | system (brew) | JSON construction (`jq -nc --arg`) and extraction (`jq -r .field`) | already used by `bench/run.sh` and `eval-qwen35-122b.sh`; safe quoting |
| `python3` 3.12+ | `bench/.venv-eval` | SSE chunk reassembly, schema validation against transcript | bash + awk hits its limits at multi-line SSE state machines (existing `eval-needle.py` precedent) |
| `requests` 2.32+ | venv | Synchronous HTTP from Python helper | already in venv via `evalplus` transitive; simple POST + json |
| `jsonschema` 4.x | venv | Validate response shapes against OpenAI spec snapshots | one new dep; pinned in `bench/requirements-eval.txt` |

### Supporting

| Tool | Version | Purpose | When to Use |
|------|---------|---------|-------------|
| `httpx` 0.27+ | venv (optional) | Async/concurrent probes with timeouts; `iter_lines()` for SSE | If concurrency-section probe needs >2 simultaneous calls. Not strictly required (asyncio + requests works for 2). |
| `awk` | system | First-content-chunk timestamp capture | Already wired in `run_ttft`. Reuse pattern. |
| `time` (zsh builtin) | system | Concurrent-request wall-clock comparison | Single fire-and-forget timing; precedent in §preliminary probe 8. |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Bash + curl + jq | F# Expecto test project under `tests/` | F# tests would tie into `dotnet test` and `RouterTests.fs rootTests` registration ceremony for a measurement-only artifact. No compile-time invariants protected. **Rejected.** |
| Bash + curl + jq | `openai-python` SDK with `base_url=` override | The SDK normalizes responses too aggressively — strips fields the server returns (e.g., `system_fingerprint`), masks the very quirks we're testing. **Rejected.** |
| Bash + curl + jq | Hurl (https://hurl.dev/) declarative HTTP DSL | Cleaner readability for golden-file probes, but adds a new dep + tooling onboarding for a one-shot phase. **Rejected** unless we find Hurl already on the dev's machine. |
| Bash + curl + jq | Postman / Insomnia collection | Not reproducible from CLI / git-tracked. **Rejected.** |
| `jsonschema` validation | hand-rolled `jq` field-existence checks | jq alone gives us `has("error.message")` style probes; OK for trivial assertions. Use `jsonschema` only if we want to validate full chunk shape vs OpenAI's spec snapshot. **Conditional: include if Phase 42 plan §3 (assertions) is non-trivial.** |

**Installation (extends existing `bench/requirements-eval.txt`):**
```bash
# Append to bench/requirements-eval.txt:
echo "jsonschema>=4.21" >> bench/requirements-eval.txt
bench/.venv-eval/bin/pip install -r bench/requirements-eval.txt
```

## Architecture Patterns

### Recommended Phase 42 layout

```
bench/
├── eval-qwen35-122b.sh         # +1 mode handler: --openai-compat
├── eval-openai-compat.py       # NEW: ~250-350 LOC, ~30 probe functions
├── requirements-eval.txt       # +1 line: jsonschema>=4.21
├── fixtures/
│   └── openai-compat/          # NEW: golden request bodies + expected-response snapshots
│       ├── 01-chat-baseline.json
│       ├── 02-response-format-json-object.json
│       ├── 03-response-format-json-schema.json
│       ├── 04-tools-tool-choice-auto.json
│       ├── 05-mid-conv-system.json
│       ├── 06-stream-true.json
│       ├── 07-n-greater-than-1.json
│       ├── 08-bad-model-field.json
│       └── ...
└── runs/
    └── openai-compat-<ts>/
        ├── probes.jsonl        # one record per probe: {label, http, body_excerpt, observed_shape, expected_shape, severity, notes}
        └── report.md           # rendered markdown for documentation/qwen35-122b-openai-compat.md

documentation/
└── qwen35-122b-openai-compat.md # NEW: human-readable conformance report

CLAUDE.md
└── Key Seams (§3 update if findings affect existing seams — e.g., error envelope shape)
```

### Pattern 1: Probe-as-record (one HTTP call → one JSONL line)

**What:** Each probe is a Python function that POSTs one request and emits one
`probes.jsonl` record. The shape of the record is fixed:
```json
{"label":"02-response-format-json-object","category":"response_format","http_code":200,
 "request_excerpt":"{...elided...}","response_excerpt":"{...first 300 chars...}",
 "expected":"server emits valid JSON object","observed":"prose 'Hi there!...'",
 "verdict":"NON-CONFORMANT","severity":"HIGH","mitigation":"prompt-instructed schema only"}
```

**When to use:** All §1 endpoint surface, §2 response_format, §3 role handling, §5 schema,
§7 error surface probes.

**Why JSONL:** Append-on-success ensures partial-run resumability if 122B crashes mid-suite
(see Pitfall §1). Mirrors `bench/eval-qwen35-122b.sh run_throughput` precedent.

### Pattern 2: Streaming probe via Python `requests.iter_lines()`

**What:** SSE probes (§4) need state-machine parsing across multiple lines. Bash awk works
for first-chunk timestamp (existing `run_ttft`) but fails for "is `[DONE]` always last?"
or "is `delta.role` always set?" assertions across N chunks.

**Pattern:** `requests.post(..., stream=True)` → `for line in r.iter_lines(decode_unicode=True)` →
classify each line as `keepalive | data-empty-content | data-content | data-done | other` →
emit single summary record with chunk counts + first-chunk shape + last-chunk shape.

**When to use:** §4 streaming. Probably the only place Python is strictly required
(everything else IS expressible in `curl + jq`, but Python is cleaner and we already have
the venv).

### Pattern 3: Pre-post bench gate sandwich

**What:** Phase 42 is heavy on `mlx_lm.server` interactions; KV cache contamination is
plausible. Wrap the entire probe suite with `bench/run.sh --gate` before AND after:

```bash
bash bench/run.sh --gate   # pre-flight
bash bench/eval-qwen35-122b.sh --openai-compat
bash bench/run.sh --gate   # post-flight
```

If post-flight fails while pre-flight passed → KV state was contaminated by our probes
(e.g., a malformed request put the server in a bad mid-conversation state). This
preserves the milestone-wide "Bench gate 7/7 PASS" invariant from ROADMAP.md §Architectural shape.

**When to use:** Mandatory at start AND end of the Phase 42 verification run.

### Pattern 4: Severity classification rubric

**What:** Each probe outputs a `severity` field. Apply this rubric (load-bearing for
"updated CLAUDE.md Key Seams" output expectation):

| Severity | Meaning | Action |
|----------|---------|--------|
| **HIGH** | Quirk affects v2.6 in-flight design (planner/executor LLM calls, role handling, JSON schema enforcement, error mapping) | Surface as new requirement via /gsd:add-todo; update CLAUDE.md Key Seams; mitigation lands in v2.6 |
| **MEDIUM** | Quirk does not affect v2.6 but affects future v2.7+ work (streaming, concurrency, tools/tool_choice if used) | Document mitigation pattern in `documentation/qwen35-122b-openai-compat.md`; no code change |
| **LOW** | Cosmetic deviation from OpenAI spec, no behavioral impact | Footnote in report; do NOT update CLAUDE.md |
| **PASS** | Field/behavior matches OpenAI spec | One-line "PASS" entry in report table |

### Pattern 5: Test-harness comment-block hygiene

**What:** Match the existing `bench/eval-qwen35-122b.sh` style — a 4-line ASCII-bordered
docblock above each `run_*` function describing intent + invariants. Plan executors
reading the file need this to know which sub-mode does what.

```bash
# ---------------------------------------------------------------------------
# run_openai_compat — Phase 42: empirical /v1/chat/completions conformance.
# Emits probes.jsonl + report.md to LOG_DIR. Uses Python venv (bench/.venv-eval)
# to drive bench/eval-openai-compat.py. Runs ~30 probes, ~3-5 min wall-clock
# (most probes max_tokens=8-32; only stream/concurrency probes longer).
# ---------------------------------------------------------------------------
```

### Anti-Patterns to Avoid

- **Anti-pattern: launching a sidecar `mlx_lm.server` on a different port for "safe
  testing."** Phase 19 retired multi-port; daily driver is 8001 only. A second instance
  would steal 45 GB RSS. **Instead:** test on 8001; use `bench/run.sh --gate` sandwich
  (Pattern 3) to detect contamination.
- **Anti-pattern: assuming OpenAI Python SDK normalizes the response back to spec.** It
  silently strips fields, hiding the very quirks we're testing. **Instead:** raw
  `requests.post` + `r.text` capture (no `r.json()` until AFTER the raw excerpt is logged).
- **Anti-pattern: re-implementing the eval harness in F# Expecto under `tests/`.** None of
  the existing eval suites (HumanEval, throughput, TTFT, multiturn) live in F#; they're
  bash + Python by design (see CLAUDE.md "blueCode runtime never imports mlx_lm").
- **Anti-pattern: parallelizing probes for speed.** Each probe should run sequentially
  (with explicit concurrency probes the only exception). Parallel mid-suite probes
  contaminate each other's KV state and make failures unreproducible.
- **Anti-pattern: relying on OpenAI's `{error:{message,type,code}}` shape in mitigation
  patterns.** Empirically the shape is `{"error": "<string>"}` (preliminary probe 4).
  Any code that consumes mlx_lm.server error JSON must treat it as an opaque string.

## Don't Hand-Roll

Problems that look simple but have existing solutions, OR conversely solutions that look
appealing but should be avoided in this measurement context:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| OpenAI conformance test suite | Custom HTTP runner from scratch | Extend `bench/eval-qwen35-122b.sh` with `--openai-compat` mode | Established style; reuses `curl_run`, `LOG_DIR`, mode-dispatch idioms; Python venv already present |
| SSE chunk parsing in bash | awk multi-line state machine for chunk-shape assertions | `requests.iter_lines()` in Python venv | Existing `run_ttft` awk handles only "first content chunk" — extending it to assert "every chunk has role/content/finish_reason" needs Python |
| OpenAI spec validation | Hand-coded `if "id" in response and "choices" in response and ...` checks | `jsonschema` against snapshot of OpenAI's spec for the field under test | One pip dep; declarative; gives clean failure messages |
| Conformance report rendering | F# / Spectre.Console renderer | Plain markdown table written by Python | Output is documentation, not interactive |
| Concurrency probe | full asyncio orchestrator | bash background subshells with `wait` | Two simultaneous requests is the maximum interesting case; bash precedent in preliminary probe 8 |
| Test-harness state cleanup | Custom `mlx_lm.server` restart logic | `bench/run.sh --gate` sandwich (Pattern 3) | Existing health authority; no new code |

**No existing OpenAI conformance test suites apply** — surveyed `openai-conformance-tests`,
`llm-evaluation-harness`, `vllm-conformance` searches: nothing project-agnostic that
covers `response_format`, `tools`, role-mid-conv quirks. Closest precedent is vLLM's
internal `tests/entrypoints/openai/` directory (Python pytest against a live vLLM
instance) — useful as a STYLE reference but not transplantable: too coupled to vLLM's
fixture setup.

**Key insight:** Phase 42 deliverable is a transcript + verdict, not a reusable suite. Don't
optimize for portability; optimize for "the planner can read each `run_*` function in 30
seconds and know exactly what it probes."

## Common Pitfalls

### Pitfall 1: KV cache contamination across probes

**What goes wrong:** A probe that submits malformed mid-conversation state (e.g.,
`role:system` mid-stream, or an unfinished SSE stream client-disconnected) leaves the
122B service in a degraded state. Subsequent probes return 200 but the response content
is corrupted (echoes prior context, FIM-mode tokens, etc.). Looks identical to "model
broken" but is purely client-induced.

**Why it happens:** `mlx_lm.server` 0.31.3 uses a shared `BatchGenerator` with mergeable
caches. State leakage between conversations was documented in v1.0 SUMMARY.md as a
"5-15% prose-wrap rate at higher temps" observation but the root cause was inconclusive.

**How to avoid:** (a) run probes sequentially, never in parallel except for the explicit
concurrency probe; (b) each probe is a fresh request — never reuse a prior `id` or
`messages` array; (c) wrap the suite with `bench/run.sh --gate` sandwich (Pattern 3) so
contamination is loud, not silent; (d) on `bench/run.sh --gate` post-flight failure,
`launchctl kickstart -k gui/$(id -u)/com.ohama.qwen122b` per `documentation/qwen35-install.md` §6.

**Warning signs:** Probe N+1 returns garbled output that includes content from probe N's
response; `finish_reason: null` followed by no further chunks; `usage.completion_tokens`
hits `max_tokens` for prompts that should yield short responses.

### Pitfall 2: `max_tokens` budget — 122B is slow under high `max_tokens`

**What goes wrong:** Conformance probes that don't care about response content but use
default `max_tokens=1024` waste 5-30s per probe (122B median 34.6 tok/s = 30s for full
1024 tokens). Suite balloons from 5 min to 2 hours, executors hit their default 300s
HttpClient timeout.

**Why it happens:** Most conformance probes only care about response SHAPE (does
`response_format` cause `finish_reason: "stop"` or "length"? does `tools` produce
`tool_calls`?). Token COUNT is irrelevant.

**How to avoid:** Default `max_tokens=8` for every probe; bump only for probes that
genuinely need to observe token-count behavior (e.g., one streaming probe with
`max_tokens=64` to assert chunk count > 1, one tools probe with `max_tokens=120` because
function-call argument JSON eats tokens).

**Warning signs:** Probe wall-clock >5s for a probe whose verdict is shape-only.

### Pitfall 3: HuggingFace fallback trap (CLAUDE.md known)

**What goes wrong:** A probe submits a `model` field that doesn't match the loaded model
path. mlx_lm.server fetches a tokenizer from HuggingFace, swaps the loaded Instruct
template for a Base Coder tokenizer, **all subsequent probes degrade**. Symptom:
prose responses become FIM-mode echoes.

**Why it happens:** This is THE seam documented in CLAUDE.md "Connection refused or 300s
timeout" + the `tryParseModelId` path-preference heuristic in `QwenHttpClient.fs`. A
naive Phase 42 probe might test "what does the server do with a wrong model field?" and
trigger the trap.

**How to avoid:** The probe for "wrong model field" submits a SPECIFIC bogus string
that's not a valid HF id (e.g., `"BOGUS_MODEL"` — preliminary probe 4 confirms this fails
with 404 + HF-error-string and DOES NOT swap the tokenizer; the failure is at the HF
metadata fetch step, before tokenizer swap). Document this in the probe's `notes` field.
Never submit a real-but-incorrect HF id like `"meta-llama/Llama-3"`.

**Warning signs:** Post-suite probes return prose containing `<|fim_*|>` tokens or echo
the system prompt verbatim.

### Pitfall 4: Thinking-mode flag must remain OFF

**What goes wrong:** A probe attempts `--chat-template-args '{"enable_thinking": true}'`
to test thinking-mode behavior. The server now emits `<think>...</think>` tokens.
Subsequent probes inherit the new template state. blueCode's strict JSON schema
validation breaks for the rest of the day.

**Why it happens:** `--chat-template-args` is set at server-launch via the launchd plist
(CLAUDE.md "Thinking-mode mitigation"); it is NOT a per-request field. There's no way
to test thinking-mode "in the suite" without restarting the server with a different
plist.

**How to avoid:** Phase 42 makes NO attempt to test thinking-mode behavior. If the
investigation surface §2 (response_format) leads to "what about thinking-mode JSON
output?" — defer to a separate investigation. Document in the report's "Out of scope"
section.

**Warning signs:** Any probe response containing `<think>` substring → ABORT and reload
launchd service (`launchctl kickstart -k gui/$(id -u)/com.ohama.qwen122b`).

### Pitfall 5: Streaming SSE `[DONE]` not always last

**What goes wrong:** Test asserts that the LAST line received is `data: [DONE]`. Probe
returns false-fail because mlx_lm.server emits keepalive comments interleaved with data
lines, AND when the stream finishes naturally (`finish_reason: "stop"`), the final usage
chunk is followed by `[DONE]` only if `stream_options.include_usage: true`.

**Why it happens:** Per source-code WebFetch finding §6: `"data: [DONE]\n\n"` is gated
on `stream=True AND stream_options["include_usage"]`. Without `include_usage`, stream
ends with the last content chunk (no terminator).

**How to avoid:** SSE probe explicitly sets `stream_options: {"include_usage": true}`
and asserts `[DONE]` presence in the final non-comment line. A second probe omits
`include_usage` and asserts `[DONE]` ABSENCE — both observations are conformance data.

**Warning signs:** Probe hangs waiting for `[DONE]` that never arrives → `iter_lines()`
loop times out, no record emitted, suite stalls.

### Pitfall 6: 5-15% prose-wrap rate (v1.0 known) creates flaky probes

**What goes wrong:** A probe runs once and observes "response_format: json_object" emits
prose 1 time in 20. Single-run conclusion: "prose ALWAYS observed, NON-CONFORMANT."
Other reviewers run the suite and see clean JSON 19 times in 20 → confused.

**Why it happens:** Qwen 3.5 122B at temp=0.7 has documented prose-wrap drift (v1.0
SUMMARY.md). For boolean-shape probes this is irrelevant; for content-shape probes
("did the model emit valid JSON?") the answer is statistical.

**How to avoid:** Probes that test CONTENT shape (not API shape) run N=10 with `temp=0.0`
(deterministic eval-standard) and report rate (e.g., "10/10 prose-wrapped under
response_format: json_object → fully NON-CONFORMANT"). Probes that test API shape
(`finish_reason`, `choices[0].message` structure) run N=1 because the API behavior is
deterministic regardless of model output.

**Warning signs:** Two consecutive runs of the same probe yield different verdicts.

### Pitfall 7: Test discovery for any future F# regression test

**What goes wrong:** If Phase 42 surfaces a HIGH-severity quirk that demands a
regression test in `tests/BlueCode.Tests/`, executor adds a new module but forgets to
register it in BOTH `BlueCode.Tests.fsproj` `<Compile Include>` and `RouterTests.fs`
`rootTests` list. CLAUDE.md flags this as the recurring v1.0 + v1.1 + v2.5 trap.

**How to avoid:** If a Phase 42 plan adds an F# test (UNLIKELY — most quirks → docs +
prompt updates, not new asserts), the plan task explicitly includes both registration
sites in its action steps.

**Warning signs:** "tests compile but don't run" — exactly the symptom CLAUDE.md flags.

## Code Examples

### Preliminary probes (FACT-LEVEL, gathered during research 2026-05-06)

These were run from the sandbox shell during research to ground the planning. The exact
commands are reproducible — bake into `bench/eval-openai-compat.py` test cases. **Note:
the planner should re-run each as a probe in the Phase 42 suite to commit to file-level
evidence; this section is informational ground-truth.**

#### Preliminary probe 1: response_format: json_object — IGNORED

```bash
curl -s -X POST http://127.0.0.1:8001/v1/chat/completions \
  -H 'Content-Type: application/json' \
  -d '{"model":"/Users/ohama/llm-system/models/qwen122b",
       "messages":[{"role":"user","content":"Say hi"}],
       "max_tokens":8,
       "response_format":{"type":"json_object"}}'
```
**HTTP:** 200
**Response excerpt:** `"content": "Hi there! How can I help you"`
**Verdict:** NON-CONFORMANT. Server returns prose. **Severity: HIGH**
(was a hoped-for v2.6 simplification; rejected). **Mitigation: blueCode v2.6 planner
must keep prompt-instructed JSON schema; do NOT rely on response_format.**

#### Preliminary probe 2: response_format: json_schema with strict:true — IGNORED

```bash
curl -s -X POST http://127.0.0.1:8001/v1/chat/completions \
  -H 'Content-Type: application/json' \
  -d '{"model":"/Users/ohama/llm-system/models/qwen122b",
       "messages":[{"role":"user","content":"Return one user object"}],
       "max_tokens":80,
       "response_format":{"type":"json_schema","json_schema":{"name":"user",
         "strict":true,
         "schema":{"type":"object","properties":{"name":{"type":"string"},"age":{"type":"integer"}},
                   "required":["name","age"],"additionalProperties":false}}}}'
```
**HTTP:** 200
**Response excerpt:** ` "content": "```json\n{\n  \"id\": 1,\n  \"username\": \"johndoe\",..."`
**Verdict:** NON-CONFORMANT. Output is markdown-fenced prose JSON, ignores `strict`,
hallucinates fields not in schema (id, username), drops required fields (name, age).
**Severity: HIGH.** Mitigation same as probe 1.

#### Preliminary probe 3: n>1 — IGNORED, returns 1 choice

```bash
curl -s -X POST http://127.0.0.1:8001/v1/chat/completions \
  -H 'Content-Type: application/json' \
  -d '{"model":"/Users/ohama/llm-system/models/qwen122b",
       "messages":[{"role":"user","content":"hi"}],"max_tokens":4,"n":3}'
```
**HTTP:** 200, `choices.length === 1`
**Verdict:** NON-CONFORMANT (silently). **Severity: LOW** (blueCode never sets n).

#### Preliminary probe 4: invalid model field — HuggingFace fallback trap

```bash
curl -s -X POST http://127.0.0.1:8001/v1/chat/completions \
  -H 'Content-Type: application/json' \
  -d '{"model":"BOGUS_MODEL","messages":[{"role":"user","content":"hi"}],"max_tokens":4}'
```
**HTTP:** 404
**Body:** `{"error": "404 Client Error...Repository Not Found for url: https://huggingface.co/api/models/BOGUS_MODEL/revision/main..."}`
**Verdict:** NON-CONFORMANT vs OpenAI's `{error:{message,type,code}}`. **Severity: MEDIUM.**
**Confirmation:** the path-preference heuristic in `tryParseModelId` (CLAUDE.md "Key Seams")
is the right defense — a probe submitting a real-but-wrong HF id (e.g.,
`"Qwen/Qwen2.5-Coder-32B"`) WOULD swap the tokenizer per CLAUDE.md. Phase 42 plan must
NOT submit real HF ids.

#### Preliminary probe 5: malformed JSON body — flat error string

```bash
curl -s -X POST http://127.0.0.1:8001/v1/chat/completions \
  -H 'Content-Type: application/json' -d 'NOT JSON'
```
**HTTP:** 400, `{"error": "Invalid JSON in request body: Expecting value: line 1 column 1 (char 0)"}`
**Verdict:** Status code matches OpenAI; envelope shape diverges. **Severity: LOW.**

#### Preliminary probe 6: missing model field — silently uses default

```bash
curl -s -X POST http://127.0.0.1:8001/v1/chat/completions \
  -H 'Content-Type: application/json' \
  -d '{"messages":[{"role":"user","content":"hi"}],"max_tokens":4}'
```
**HTTP:** 200, response `"model": "default_model"`, content was generated.
**Verdict:** SURPRISING (OpenAI requires `model`). **Severity: MEDIUM** — could mask bugs
where blueCode forgets to set the model field. blueCode mitigations: REF-01/REF-02 lazy
ModelInfo probe enforces non-empty modelId at the call site (CLAUDE.md "Key Seams").

#### Preliminary probe 7: mid-conversation role:system — HTTP 404 with helpful message

```bash
curl -s -X POST http://127.0.0.1:8001/v1/chat/completions \
  -H 'Content-Type: application/json' \
  -d '{"model":"/Users/ohama/llm-system/models/qwen122b","messages":[
       {"role":"system","content":"You are helpful"},
       {"role":"user","content":"hi"},
       {"role":"assistant","content":"Hello!"},
       {"role":"system","content":"Now be terse"},
       {"role":"user","content":"explain"}],"max_tokens":8}'
```
**HTTP:** 404, `{"error": "System message must be at the beginning."}`
**Verdict:** NON-CONFORMANT vs OpenAI (which accepts mid-conv system). **Severity: HIGH**
for v2.6 (already mitigated by Phase 17-02 + 20-03 invariants in CLAUDE.md). Confirmed
fresh against current server build.

#### Preliminary probe 8: tools + tool_choice: auto — FULL OpenAI envelope returned

```bash
curl -s -X POST http://127.0.0.1:8001/v1/chat/completions \
  -H 'Content-Type: application/json' \
  -d '{"model":"/Users/ohama/llm-system/models/qwen122b",
       "messages":[{"role":"user","content":"What is the weather in Paris?"}],
       "max_tokens":120,
       "tools":[{"type":"function","function":{"name":"get_weather","description":"Get weather",
         "parameters":{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}}}],
       "tool_choice":"auto"}'
```
**HTTP:** 200
**Response shape:** `"finish_reason": "tool_calls"`,
`"message": {"role": "assistant", "tool_calls": [{"function": {"name": "get_weather",
"arguments": "{\"city\": \"Paris\"}"}, "type": "function", "id": "..."}]}`
**Verdict:** **CONFORMANT** to OpenAI tool-calling spec. **Severity: PASS** (positive
finding). **Implication: a future v2.7+ blueCode design could replace its custom
JSON-schema action DU with OpenAI tool calls. Phase 42 documents this as a viable
path; Phase 42 does NOT implement the swap.** Recommendation: add a v2.7 todo via
`/gsd:add-todo`.

#### Preliminary probe 9: SSE chunk shape — `role` repeated every chunk

```bash
curl -N -s -X POST http://127.0.0.1:8001/v1/chat/completions \
  -H 'Content-Type: application/json' \
  -d '{"model":"/Users/ohama/llm-system/models/qwen122b",
       "messages":[{"role":"user","content":"count to 3"}],
       "max_tokens":12,"stream":true}'
```
**Observed:** Multiple keepalive comments (`: keepalive 12/16`), then chunks each shaped
`data: {"choices":[{"delta":{"role":"assistant","content":"1"}}]}` — `role` repeats on
EVERY chunk, not just first.
**Verdict:** NON-CONFORMANT vs OpenAI (which sets role only in first chunk's delta).
**Severity: LOW** for blueCode (does not consume streaming today; existing
`run_ttft` awk filter `/"content":/ && !/"content":""/` is correct against this behavior
because it filters by content, not role).

#### Preliminary probe 10: 2-way concurrent — parallel batched, no 429

```bash
( time curl -s ... 'say A' ) &
( time curl -s ... 'say B' ) &
wait
# Both completed in 1.06s wall — batched, NOT serialized.
```
**Verdict:** **CONFORMANT-PLUS** (better than single-instance assumption). **Severity:
PASS** with v2.7+ implication: Phase F (wave-parallel) is feasible from the server
side. Note: the `BatchGenerator` (mlx-lm 0.31.3 source) batches up to
`--decode-concurrency=32` simultaneous decoding streams. Phase 42 should probe N=4 and
N=8 to find the practical knee (out of scope for v2.6 plan; document for v2.7+).

#### Preliminary probe 11: /v1/completions endpoint exists (undocumented in SERVER.md)

```bash
curl -s -X POST http://127.0.0.1:8001/v1/completions \
  -H 'Content-Type: application/json' \
  -d '{"model":"/Users/ohama/llm-system/models/qwen122b",
       "prompt":"def fib(n):","max_tokens":20,"temperature":0.2}'
```
**HTTP:** 200, `"object": "text_completion", "choices": [{"text": "\n    if n == 0:..."}]`
**Verdict:** CONFORMANT to legacy OpenAI completions API. **Severity: PASS** (already
exercised by `bench/eval-humaneval-http.py generate_completion`).

### Probe-as-record JSONL example (target output of `bench/eval-openai-compat.py`)

```json
{"label": "02-response-format-json-object", "category": "response_format",
 "request_excerpt": {"response_format": {"type": "json_object"}, "max_tokens": 8},
 "http_code": 200, "response_excerpt": "Hi there! How can I help you",
 "expected": "valid JSON object as content",
 "observed": "prose 'Hi there!...' — response_format silently ignored",
 "verdict": "NON-CONFORMANT", "severity": "HIGH",
 "mitigation": "blueCode v2.6 planner: keep prompt-instructed JSON schema; do not depend on response_format",
 "openai_spec_url": "https://platform.openai.com/docs/api-reference/chat/create#chat-create-response_format"}
```

### Sample `eval-openai-compat.py` skeleton (Plan 42-01 reference)

```python
#!/usr/bin/env python3
"""Phase 42: empirical OpenAI compat probes for mlx_lm.server @ 8001.

NEVER imports mlx_lm — all probing via HTTP. Mirrors bench/eval-humaneval-http.py style."""
import argparse, json, time
from pathlib import Path
import requests

ENDPOINT = "http://127.0.0.1:8001"
MODEL_PATH = "/Users/ohama/llm-system/models/qwen122b"

def probe(label, category, body, expected, severity_if_nonconformant):
    """One probe -> one record. Body is partial; merged with model field."""
    full = {"model": MODEL_PATH, "max_tokens": 8, **body}
    t0 = time.time()
    try:
        r = requests.post(f"{ENDPOINT}/v1/chat/completions", json=full, timeout=120)
        elapsed = time.time() - t0
        excerpt = r.text[:300]
        return {
            "label": label, "category": category,
            "http_code": r.status_code,
            "response_excerpt": excerpt,
            "elapsed_s": round(elapsed, 2),
            "expected": expected,
            # verdict + severity filled by post-hoc analyzer based on category rules
        }
    except Exception as e:
        return {"label": label, "category": category, "error": repr(e)}

# Each probe is a one-line call:
PROBES = [
    ("01-baseline", "endpoint",
     {"messages": [{"role": "user", "content": "Say hi"}]},
     "200 + content non-empty", "PASS"),
    ("02-response-format-json-object", "response_format",
     {"messages": [{"role": "user", "content": "Say hi"}],
      "response_format": {"type": "json_object"}},
     "valid JSON object as content", "HIGH"),
    # ... ~30 probes total
]

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output-dir", required=True)
    args = p.parse_args()
    out = Path(args.output_dir); out.mkdir(parents=True, exist_ok=True)
    with (out / "probes.jsonl").open("w") as fp:
        for label, category, body, expected, sev in PROBES:
            rec = probe(label, category, body, expected, sev)
            fp.write(json.dumps(rec) + "\n"); fp.flush()
            print(f"[{rec.get('http_code', 'ERR')}] {label}")

if __name__ == "__main__":
    main()
```

### Sample `bench/eval-qwen35-122b.sh` mode handler addition

```bash
# ---------------------------------------------------------------------------
# run_openai_compat — Phase 42: empirical OpenAI conformance probes.
# Drives bench/eval-openai-compat.py; renders markdown report from probes.jsonl.
# Pre-requisite: bench/run.sh --gate must pass (run BEFORE this).
# Post-requisite: bench/run.sh --gate must pass again (run AFTER this).
# ---------------------------------------------------------------------------
run_openai_compat() {
  require_port_8001
  if [ ! -x "$VENV_PY" ]; then
    echo "ERROR: $VENV_PY not found. Run: bash $0 --setup" >&2
    exit 5
  fi
  mkdir -p "$LOG_DIR"
  echo "===== OpenAI compat (~30 probes, ~3-5 min) ====="
  "$VENV_PY" bench/eval-openai-compat.py --output-dir "$LOG_DIR"
  local probes="$LOG_DIR/probes.jsonl"
  local count
  count=$(wc -l < "$probes" | tr -d ' ')
  echo "openai-compat: $count probes recorded in $probes"
  # Render markdown report
  "$VENV_PY" bench/eval-openai-compat.py --render "$probes" > "$LOG_DIR/report.md"
  echo "openai-compat: rendered report at $LOG_DIR/report.md"
}
```

## State of the Art

| Old assumption | Current empirical truth | Source |
|----------------|-------------------------|--------|
| "tool_calls absent in mlx_lm.server" (v1.0 SUMMARY.md) | Tools/tool_choice/tool_calls FULLY supported (mlx-lm 0.31.2-0.31.3) | Source-code WebFetch + preliminary probe 8 |
| "response_format json_schema works in MLX ecosystem" (LM Studio docs / mlx-openai-server fork) | mlx_lm.server (Apple's mainline) silently IGNORES response_format | Source-code WebFetch §3 + preliminary probes 1-2 |
| "Single-instance MLX server = serial requests" | BatchGenerator handles up to 32 parallel decoding streams; no 429 ever | Source-code WebFetch §7 + preliminary probe 10 |
| "OpenAI-style error envelope `{error:{message,type,code}}`" (OpenAI spec) | mlx_lm.server returns flat `{"error": "<string>"}` | Preliminary probes 4-5 + 7 |
| "SSE first chunk emits role-only delta, content chunks omit role" (OpenAI behavior) | mlx_lm.server emits role on EVERY content chunk | Preliminary probe 9 + source-code WebFetch §5 |

**Deprecated/outdated:**

- `bench/eval-qwen35-122b.sh` schema-rate test (`--schema-rate`, REL-EVAL-01) showed 0/50
  InvalidJsonOutput. That measures blueCode's PROMPT-INSTRUCTED schema. It is NOT
  evidence that response_format works server-side. The two are independent.

## Open Questions

1. **Streaming + tool_calls interaction**
   - What we know: tool_calls work in non-streaming (probe 8); streaming works in
     non-tool-calling (probe 9).
   - What's unclear: `stream=true` + `tools` + a tool-call-triggering prompt — does the
     server emit `delta.tool_calls` partial chunks (OpenAI's behavior) or buffer until
     `finish_reason: "tool_calls"` and emit one big chunk?
   - Recommendation: include as one probe in Phase 42 (~5s probe). MEDIUM severity if
     non-conformant; PASS if matches OpenAI.

2. **Prose-wrap rate under response_format=json_object at temp=0.0**
   - What we know: temp=0.7 default has 5-15% prose-wrap (v1.0 SUMMARY.md); temp=0.2 eval
     showed 0/50 (REL-EVAL-01) with prompt-instructed schema.
   - What's unclear: if `response_format: json_object` is ignored, does the model still
     ATTEMPT to emit JSON because it sees the field (in any internal representation)? Or
     is the field discarded at the parameter-parsing layer (per source-code WebFetch §3)?
   - Recommendation: probe N=10 at temp=0.0 with response_format vs without; compare
     prose-wrap rates. If identical → field is fully ignored (likely). If different → field
     has subtle prompting effect (interesting; LOW severity).

3. **Concurrency knee**
   - What we know: 2 parallel = 1.06s (both); `--decode-concurrency=32` is the cap.
   - What's unclear: where does latency degrade? At 4? 8? 16? Does TTFT regress under
     load?
   - Recommendation: out of scope for v2.6; capture as v2.7+ todo. Phase 42 probe is
     ONLY the N=2 baseline.

4. **`/v1/responses` endpoint (OpenAI 2024 stateful API)**
   - What we know: not in source-code WebFetch's enumerated endpoints.
   - What's unclear: does mlx_lm.server proxy or 404 it?
   - Recommendation: include as a single endpoint-probe in Phase 42; expected outcome 404.
     LOW severity regardless.

5. **`logprobs` / `top_logprobs` field behavior**
   - What we know: source-code WebFetch lists `logprobs` as supported parameter.
   - What's unclear: shape of the response; does it match OpenAI's spec?
   - Recommendation: include single probe; LOW severity (blueCode does not use logprobs).

## Sources

### Primary (HIGH confidence)
- **Live `mlx_lm.server` 0.31.3 @ localhost:8001** — direct empirical probes during research
  (preliminary probes 1-11 in §Code Examples). Server build: `system_fingerprint:
  "0.31.3-0.31.2-macOS-26.3-arm64-arm-64bit-Mach-O-applegpu_g16s"`.
- **`mlx_lm/server.py` source code** (https://github.com/ml-explore/mlx-lm/blob/main/mlx_lm/server.py
  via raw WebFetch) — endpoint routing, response_format handling (absent), tool calling
  state machine, SSE shape, error codes, BatchGenerator concurrency.
- **`mlx_lm/SERVER.md` documentation** (https://github.com/ml-explore/mlx-lm/blob/main/mlx_lm/SERVER.md
  via raw WebFetch) — official supported parameter list (only 17 fields enumerated;
  response_format / tools / tool_choice ABSENT from doc, despite being implemented).
- **mlx-lm releases page** (https://github.com/ml-explore/mlx-lm/releases) — v0.31.3
  released Apr 22 2024 (current shipping version). Recent tool-calling fixes confirm
  active development of that surface.
- **`bench/eval-qwen35-122b.sh`** + `bench/eval-humaneval-http.py` + `bench/eval-needle.py`
  — existing harness style + run_ttft SSE awk pattern + curl_run timing pattern.
- **`src/BlueCode.Cli/Adapters/QwenHttpClient.fs`** lines 44-78 (`buildRequestBody`)
  — exact set of fields blueCode currently sends; baseline for "what we already exercise."
- **`documentation/qwen35-122b-coding-eval.md`** §1.5 — eval doc structure precedent for
  the report deliverable.
- **CLAUDE.md** Key Seams + Common Gotchas — Phase 17-02 + 20-03 mid-conv role invariant
  (preliminary probe 7 confirmed); HuggingFace fallback trap (preliminary probe 4
  confirmed); thinking-mode flag invariant.

### Secondary (MEDIUM confidence)
- **OpenAI structured outputs official guide** — WebFetch returned 403 (auth wall);
  recommend planner cross-check live URL https://platform.openai.com/docs/guides/structured-outputs
  or the openapi spec at https://github.com/openai/openai-openapi for the canonical
  json_schema response_format shape (used in preliminary probe 2 — shape confirmed valid
  syntactically; rejected at semantic layer by mlx-lm).
- **vLLM OpenAI-compatible server docs** (https://docs.vllm.ai/en/stable/serving/openai_compatible_server/)
  — comparable conformance reference; their docs explicitly support response_format
  json_schema; useful as a "what other local servers do" benchmark for severity scoring.
- **`mlx-openai-server` (cubist38 fork)** — different project from `mlx_lm.server`; DOES
  support json_schema. Easy confusion point in WebSearch results — flag in report so
  readers don't expect mainline mlx-lm to behave like the fork.

### Tertiary (LOW confidence)
- WebSearch summaries (CraftRigs, Glukhov, dev.to) on Ollama/llama.cpp/LM Studio
  comparative compat — useful for severity calibration ("how unique is mlx-lm's
  silent-ignore-response_format behavior?"). Spot-check before quoting.
- `mlx-lm` issue #875 (gpt-oss tokens) — adjacent area; not directly relevant to Phase 42
  but indicates active server-side chat-template work.

## Metadata

**Confidence breakdown:**
- Standard stack: **HIGH** — extending established bench/eval-* harness, no new design
  decisions; single new pip dep (jsonschema) optional.
- Architecture: **HIGH** — probe-as-record pattern is bench-harness precedent; pre-post
  bench-gate sandwich is existing tooling; severity rubric is documentation work.
- Pitfalls: **HIGH** — KV contamination + thinking-mode flag + HF fallback trap are all
  documented seams in CLAUDE.md, just rephrased for measurement context. Pitfalls 1-4
  cite existing CLAUDE.md/v1.0 evidence.
- Empirical findings: **HIGH** for preliminary probes 1-11 (each has reproducible curl +
  observed body); **MEDIUM** for severity classification (HIGH/MEDIUM/LOW reflects best
  judgment on v2.6 impact, may be revised by planner).

**Research date:** 2026-05-06
**Valid until:** 2026-08-06 (3 months — `mlx_lm.server` is on a roughly monthly release
cadence; response_format support is the most-likely field to be added, which would
upgrade preliminary probes 1-2 from HIGH to PASS. Re-probe before any v2.7+ work that
might rely on findings.)
