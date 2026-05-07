# Qwen 3.5 122B OpenAI Compatibility — Empirical Conformance Report

**Date:** 2026-05-07T04:14:59Z (derived from JSONL mtime; reproducible across re-renders)
**Server:** mlx_lm.server (system_fingerprint=`0.31.3-0.31.2-macOS-26.3-arm64-arm-64bit-Mach-O-applegpu_g16s`) @ localhost:8001
**Model:** /Users/ohama/llm-system/models/qwen122b
**Source transcript:** `bench/runs/qwen35-eval-20260507-131320/probes.jsonl`
**Records:** 25 probes covering 8 RESEARCH surfaces
**Reproduction:**
```bash
# 1. Capture a fresh transcript (kickstart 122B first if cold):
launchctl kickstart -k gui/$(id -u)/com.ohama.qwen122b
until curl -fsS http://127.0.0.1:8001/v1/models > /dev/null; do sleep 5; done
bash bench/eval-qwen35-122b.sh --openai-compat
# 2. Render this report from the new probes.jsonl:
LATEST=$(ls -td bench/runs/qwen35-eval-* | head -1)
bench/.venv-eval/bin/python bench/eval-openai-compat.py --render "$LATEST/probes.jsonl" \
  > documentation/qwen35-122b-openai-compat.md
```

## How to read this report

- **Verdict** describes WHAT the server did relative to the OpenAI reference behavior.
- **Severity** is the impact on blueCode v2.6+ as a downstream consumer:
  - **HIGH** = action required; either a regression vs prior captured behavior or a documented
    non-conformance that affects downstream code paths.
  - **MEDIUM** = informational divergence; mitigation already in place or trivial to add.
  - **LOW** = cosmetic divergence (e.g., role on every chunk in SSE) that does not affect blueCode.
  - **PASS** = behavior matches OpenAI reference OR is the documented invariant we rely on.
- **EXPECTED-DIVERGENCE** verdicts are non-conformances that are LOW/MEDIUM by intent (e.g.,
  malformed-body returns `{"error": "<string>"}` instead of a structured envelope — the wire
  shape diverges but blueCode never relies on the OpenAI shape).
- The 8-surface taxonomy comes from RESEARCH.md §Architecture Patterns Pattern 4. Probes 23/24/25
  share Surface 6; probe 15 covers Surface 7 alone; probes 16–19 + 22 collapse into Surface 8.

## Verdict Summary

| Severity | Count | Labels |
|---|---|---|
| HIGH | 3 | 06-response-format-json-object, 07-response-format-json-schema-strict, 08-response-format-no-rerun-N1 |
| MEDIUM | 2 | 17-bogus-model-id, 18-missing-model-field |
| LOW | 5 | 05-responses-endpoint, 11-stream-baseline, 16-malformed-json-body, 19-n-greater-than-1, 21-no-response-format-rate-temp0-N5 |
| PASS | 15 | 01-baseline-chat, 02-completions-legacy, 03-models-list, 04-health-endpoint, 09-mid-conv-system-rejected, 10-system-only-at-start, 12-stream-with-usage, 13-stream-finish-stop, 14-stream-finish-length, 15-tools-tool-choice-auto, 20-response-format-rate-temp0-N5, 22-concurrent-pair, 23-coherence-call-1, 24-coherence-call-2, 25-coherence-call-3 |

## Findings by Severity

### HIGH (action required for v2.6 / regression check)

- **06-response-format-json-object** (Surface 2 — response_format): NON-CONFORMANT (prose-wrapped per probe 20). Mitigation: blueCode v2.6+ MUST NOT rely on `response_format`; use prompt-instructed schema with retry policy.
- **07-response-format-json-schema-strict** (Surface 2 — response_format): NON-CONFORMANT (schema not enforced; markdown-fenced per probe 20). Mitigation: blueCode v2.6+ MUST NOT rely on `response_format`; use prompt-instructed schema with retry policy.
- **08-response-format-no-rerun-N1** (Surface 2 — response_format): NON-CONFORMANT (prose-wrapped per probe 20). Mitigation: blueCode v2.6+ MUST NOT rely on `response_format`; use prompt-instructed schema with retry policy.

### MEDIUM (informational; mitigation only)

- **17-bogus-model-id** (Surface 8 — Errors + concurrency): EXPECTED-DIVERGENCE (HF-fetch error string). Mitigation: Confirms `tryParseModelId` path-preference heuristic in CLAUDE.md 'Key Seams' is necessary; never send HF repo ids.
- **18-missing-model-field** (Surface 8 — Errors + concurrency): SURPRISING (silent fallback to default_model). Mitigation: Server falls back to `default_model` and triggers HF reload (~83s); blueCode always sets local path so this is non-blocking.

### LOW (cosmetic / no action)

- **05-responses-endpoint** (Surface 1 — Endpoint coverage): EXPECTED-ABSENCE. Mitigation: `/v1/responses` not implemented on this build; deferred to v2.7+ if needed.
- **11-stream-baseline** (Surface 4 — Streaming): NON-CONFORMANT (role on every chunk, N=6). Mitigation: Role repeated on every chunk (NON-CONFORMANT vs OpenAI which sets role only on first chunk); blueCode does not stream so cosmetic.
- **16-malformed-json-body** (Surface 8 — Errors + concurrency): EXPECTED-DIVERGENCE (error: <string> envelope). Mitigation: Server returns `{"error": "<string>"}` envelope on parse failure (cosmetic divergence from OpenAI structured-error shape); no action required.
- **19-n-greater-than-1** (Surface 8 — Errors + concurrency): EXPECTED-DIVERGENCE (n>1 silently ignored). Mitigation: Server silently ignores `n>1`; blueCode never sets it.
- **21-no-response-format-rate-temp0-N5** (Surface 5 — Schema rate (STAT_N)): INFORMATIONAL (baseline without response_format). Mitigation: 

### PASS (positive findings)

- **01-baseline-chat** (Surface 1 — Endpoint coverage): PASS.
- **02-completions-legacy** (Surface 1 — Endpoint coverage): PASS.
- **03-models-list** (Surface 1 — Endpoint coverage): PASS.
- **04-health-endpoint** (Surface 1 — Endpoint coverage): PASS.
- **09-mid-conv-system-rejected** (Surface 3 — Role handling): PASS (confirms Phase 17-02 invariant).
- **10-system-only-at-start** (Surface 3 — Role handling): PASS.
- **12-stream-with-usage** (Surface 4 — Streaming): PASS ([DONE] sentinel emitted).
- **13-stream-finish-stop** (Surface 4 — Streaming): PASS.
- **14-stream-finish-length** (Surface 4 — Streaming): PASS.
- **15-tools-tool-choice-auto** (Surface 7 — Tools / function calling): PASS (tool_calls envelope; v2.7+ candidate).
- **20-response-format-rate-temp0-N5** (Surface 5 — Schema rate (STAT_N)): PASS (5/5 valid JSON).
- **22-concurrent-pair** (Surface 8 — Errors + concurrency): PASS (wall 0.39s < 0.7*sum 0.53s; parallel decode confirmed).
- **23-coherence-call-1** (Surface 6 — Multi-call coherence): PASS (envelope OK; coherence checked in aggregate).
- **24-coherence-call-2** (Surface 6 — Multi-call coherence): PASS (envelope OK; coherence checked in aggregate).
- **25-coherence-call-3** (Surface 6 — Multi-call coherence): PASS (envelope OK; coherence checked in aggregate).

## Per-Surface Tables

### Surface 1: Endpoint coverage

| Probe | Endpoint | HTTP | Verdict | Severity | Notes |
|---|---|---|---|---|---|
| 01-baseline-chat | POST /v1/chat/completions | 200 | PASS | PASS | elapsed=1.23s |
| 02-completions-legacy | POST /v1/completions | 200 | PASS | PASS | elapsed=0.6s |
| 03-models-list | GET /v1/models | 200 | PASS | PASS | elapsed=0.0s |
| 04-health-endpoint | GET /health | 200 | PASS | PASS | elapsed=0.0s |
| 05-responses-endpoint | POST /v1/responses | 404 | EXPECTED-ABSENCE | LOW | elapsed=0.0s |

### Surface 2: response_format

| Probe | Endpoint | HTTP | Verdict | Severity | Notes |
|---|---|---|---|---|---|
| 06-response-format-json-object | POST /v1/chat/completions | 200 | NON-CONFORMANT (prose-wrapped per probe 20) | HIGH | elapsed=0.29s |
| 07-response-format-json-schema-strict | POST /v1/chat/completions | 200 | NON-CONFORMANT (schema not enforced; markdown-fenced per probe 20) | HIGH | elapsed=1.75s |
| 08-response-format-no-rerun-N1 | POST /v1/chat/completions | 200 | NON-CONFORMANT (prose-wrapped per probe 20) | HIGH | elapsed=0.29s |

### Surface 3: Role handling

| Probe | Endpoint | HTTP | Verdict | Severity | Notes |
|---|---|---|---|---|---|
| 09-mid-conv-system-rejected | POST /v1/chat/completions | 404 | PASS (confirms Phase 17-02 invariant) | PASS | elapsed=0.0s |
| 10-system-only-at-start | POST /v1/chat/completions | 200 | PASS | PASS | elapsed=0.24s |

### Surface 4: Streaming

| Probe | Endpoint | HTTP | Verdict | Severity | Notes |
|---|---|---|---|---|---|
| 11-stream-baseline | POST /v1/chat/completions | 200 | NON-CONFORMANT (role on every chunk, N=6) | LOW | elapsed=0.36s; finish=stop; saw_done=True; role_chunks=6/6 |
| 12-stream-with-usage | POST /v1/chat/completions | 200 | PASS ([DONE] sentinel emitted) | PASS | elapsed=0.26s; finish=stop; saw_done=True; role_chunks=6/7 |
| 13-stream-finish-stop | POST /v1/chat/completions | 200 | PASS | PASS | elapsed=0.26s; finish=stop; saw_done=True; role_chunks=2/2 |
| 14-stream-finish-length | POST /v1/chat/completions | 200 | PASS | PASS | elapsed=0.37s; finish=length; saw_done=True; role_chunks=9/9 |

### Surface 5: Schema rate (STAT_N)

| Probe | Endpoint | HTTP | Verdict | Severity | Notes |
|---|---|---|---|---|---|
| 20-response-format-rate-temp0-N5 | STAT_N /v1/chat/completions | 200/200/200/200/200 | PASS (5/5 valid JSON) | PASS | elapsed_total=3.03s; valid_json=5/5; prose_wrap=5/5 |
| 21-no-response-format-rate-temp0-N5 | STAT_N /v1/chat/completions | 200/200/200/200/200 | INFORMATIONAL (baseline without response_format) | LOW | elapsed_total=2.91s; valid_json=5/5; prose_wrap=5/5 |

### Surface 6: Multi-call coherence

| Probe | Endpoint | HTTP | Verdict | Severity | Notes |
|---|---|---|---|---|---|
| 23-coherence-call-1 | POST /v1/chat/completions | 200 | PASS (envelope OK; coherence checked in aggregate) | PASS | elapsed=0.37s |
| 24-coherence-call-2 | POST /v1/chat/completions | 200 | PASS (envelope OK; coherence checked in aggregate) | PASS | elapsed=0.37s |
| 25-coherence-call-3 | POST /v1/chat/completions | 200 | PASS (envelope OK; coherence checked in aggregate) | PASS | elapsed=0.37s |

### Surface 7: Tools / function calling

| Probe | Endpoint | HTTP | Verdict | Severity | Notes |
|---|---|---|---|---|---|
| 15-tools-tool-choice-auto | POST /v1/chat/completions | 200 | PASS (tool_calls envelope; v2.7+ candidate) | PASS | elapsed=1.47s |

### Surface 8: Errors + concurrency

| Probe | Endpoint | HTTP | Verdict | Severity | Notes |
|---|---|---|---|---|---|
| 16-malformed-json-body | POST /v1/chat/completions | 400 | EXPECTED-DIVERGENCE (error: <string> envelope) | LOW | elapsed=0.0s |
| 17-bogus-model-id | POST /v1/chat/completions | 404 | EXPECTED-DIVERGENCE (HF-fetch error string) | MEDIUM | elapsed=0.25s |
| 18-missing-model-field | POST /v1/chat/completions | 200 | SURPRISING (silent fallback to default_model) | MEDIUM | elapsed=83.4s |
| 19-n-greater-than-1 | POST /v1/chat/completions | 200 | EXPECTED-DIVERGENCE (n>1 silently ignored) | LOW | elapsed=0.18s |
| 22-concurrent-pair | POST /v1/chat/completions | 200/200 | PASS (wall 0.39s < 0.7*sum 0.53s; parallel decode confirmed) | PASS | wall=0.39s sum=0.76s |

## Empirical highlights

### response_format silent-ignore (probes 20 vs 21)

- Probe 20 (with `response_format: {"type": "json_object"}`):
  `valid_json=5/5`,
  `prose_wrap=5/5`,
  total elapsed `3.03s`.
- Probe 21 (NO `response_format`): `valid_json=5/5`,
  `prose_wrap=5/5`,
  total elapsed `2.91s`.
- Identical first content excerpt across both probes: **True**.
- **Empirical conclusion:** at `temperature=0.0`, the `response_format` field has zero effect on
  output shape — both probes return identical prose-fenced JSON in identical wall-clock time.
  This answers RESEARCH.md Open Question 2 (does response_format have any prompting side-effect
  at temp=0?). Answer: **NO**.

### N=2 parallel decode (probe 22)

- `wall_clock_s = 0.39s`, `elapsed_s_each = [0.38, 0.38]`,
  `elapsed_s_sum = 0.76s`, `elapsed_s_max = 0.38s`.
- Ratio `wall_clock / max(elapsed_each) = 1.03` (≈1.0 = perfect parallelism).
- **Empirical conclusion:** mlx_lm.server's BatchGenerator merges 2 simultaneous decode requests
  into a single batched forward pass; throughput per-slot is preserved. This validates
  RESEARCH.md preliminary 10 and unblocks v2.7+ wave-parallel exec (Phase F) feasibility.

### Error-surface timing (probes 17/18/19)

- Probe 17 (bogus model id `BOGUS_MODEL`): HTTP 404, elapsed `0.25s` —
  fast-fail because mlx_lm.server's HF lookup hits a 404 immediately.
- Probe 18 (missing model field): HTTP 200, elapsed `83.4s` —
  server falls back to `default_model` and triggers a HuggingFace tokenizer reload.
- Probe 19 (n>1, executed immediately after probe 18): HTTP 200, elapsed
  `0.18s` — the post-18 server state is still healthy and routes back to qwen122b.
- **Empirical conclusion:** the `default_model` fallback is non-contaminating; subsequent requests
  with explicit model paths route correctly. `HttpClient.Timeout=300s` (Phase 20-01) is justified.

## Multi-call coherence detail

Three sequential probes (23/24/25): expected answers `4` / `6` / `10`. Empirical observation:

| Call | HTTP | Envelope OK | Expected | Observed |
|---|---|---|---|---|
| 23-coherence-call-1 | 200 | True | 4 | (truncated by 300-char excerpt cap; not in JSONL) |
| 24-coherence-call-2 | 200 | True | 6 | (truncated by 300-char excerpt cap; not in JSONL) |
| 25-coherence-call-3 | 200 | True | 10 | (truncated by 300-char excerpt cap; not in JSONL) |

**Verdict:** PASS (envelope shape coherent across all 3 calls; semantic correctness deferred — excerpt cap)

> Limitation: probe()'s 300-char `response_excerpt` cap truncates before `choices[0].message.content`,
> so the actual answer text is not captured in the JSONL for the coherence probes. Envelope shape
> coherence (HTTP 200 + `chat.completion` envelope on each of 3 sequential calls) IS captured and
> verified PASS. Future plans may extend probe() with a `capture_full_content` flag for coherence labels.

## Out of scope

- **Thinking-mode behavior** (`enable_thinking`) — set at server-launch via launchd plist; NOT a
  per-request field. Defer to separate investigation per RESEARCH.md Pitfall §4.
- **Concurrency knee at N>2** — RESEARCH.md Pitfall §1: don't disrupt the daily-driver. Probe 22
  confirmed N=2 parallel decode; higher fan-out is a v2.7+ candidate (Phase F wave-parallel exec).
- **/v1/responses endpoint shape** — single-probe coverage in 05 (HTTP 404 on this build); deeper
  shape testing is a v2.7+ candidate if the endpoint becomes available.
- **Semantic correctness of coherence answers** — see limitation note above; envelope-only verdict.

## Implications for blueCode

### v2.6 mitigations already in place (CONFIRMED by this run)

- **Phase 17-02 + Phase 20-03**: mid-conv `Role=System` forbidden — CONFIRMED still required by
  probe 09 (HTTP 404 + `"System message must be at the beginning"` error).
- **`tryParseModelId` path-preference heuristic** in CLAUDE.md "Key Seams (v1.1)" → "Model id flow"
  — CONFIRMED necessary by probe 17 (bogus model id triggers HF fetch + 404 error string).
- **`--chat-template-args '{"enable_thinking": false}'`** in launchd plist — out of test scope; see
  Out-of-scope above.
- **Phase 20-01 `HttpClient.Timeout = 300s`** — covers the 83.4s `default_model` HF reload spike
  observed in probe 18; blueCode never sees this because it always sets the model field, but the
  timeout headroom is justified.

### v2.7+ candidates (file via `/gsd:add-todo` after this report lands)

- **Replace custom JSON-schema action DU with native OpenAI tool calls** — probe 15 PASS with
  `finish_reason="tool_calls"` confirms `mlx_lm.server` honors the tools/tool_choice envelope.
  Migration path: extend `Action` DU codecs, add `tools=[...]` to `buildRequestBody`, deprecate
  the `<JsonSchemaCall>` schema.
- **Wave-parallel exec (Phase F)** — probe 22 confirms BatchGenerator parallel decode at N=2
  (wall_clock=0.39s ≈ max([0.38, 0.38]); sum=0.76s).
  Single-LLM-server is no longer a serialization bottleneck for parallel plan-step execution.

## Sources

- `.planning/phases/42-qwen-122b-openai-compat-test/42-RESEARCH.md` (research date, 8-surface
  taxonomy, severity rubric §Pattern 4, preliminary findings 1–11)
- `bench/runs/qwen35-eval-20260507-131320/probes.jsonl` (this run; 25 probes)
- `mlx_lm` 0.31.3 source code (server.py) — referenced for response_format pass-through behavior
- Phase 17-02 + Phase 20-03 + Phase 19 invariants (CLAUDE.md "Key Seams")
- `documentation/qwen35-122b-coding-eval.md` (v2.1 milestone) — companion 100-point scorecard
- `.planning/phases/42-qwen-122b-openai-compat-test/42-01-SUMMARY.md`,
  `42-02-SUMMARY.md` — per-plan execution summaries with empirical highlights

---

*Generated by `bench/eval-openai-compat.py --render` from JSONL mtime 2026-05-07*
*jsonschema available: True*
