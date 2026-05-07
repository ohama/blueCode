---
phase: 42-qwen-122b-openai-compat-test
plan: 02
subsystem: testing
tags: [openai-compat, mlx-lm-server, sse-streaming, tool-calls, concurrency, http-probes, qwen122b, response_format, error-envelope, coherence]

# Dependency graph
requires:
  - phase: 42-qwen-122b-openai-compat-test
    plan: 01
    provides: bench/eval-openai-compat.py probe driver scaffolding (10 probes Surfaces 1+2+3) + run_openai_compat() shell handler
  - phase: 21-bench-eval-qwen35-122b
    provides: bench/eval-qwen35-122b.sh harness pattern, bench/.venv-eval, run.sh --gate authority
provides:
  - "bench/eval-openai-compat.py: extended to ~870 LOC with probe_stream() (SSE state-machine via requests.iter_lines), probe_concurrent_pair() (N=2 ThreadPoolExecutor), _dispatch_stat_n() (full-response N-repeat aggregator), and 4 probe() flags (raw_body, omit_model_field, body_model_override, plus default path)"
  - "PROBES list grown 10 → 25 covering all 8 RESEARCH.md surfaces + tools bonus"
  - "Empirical finding: response_format has zero effect at temp=0.0 (probes 20+21: 5/5 prose-wrapped 'Alice/28' identical with vs without response_format) → RESEARCH Open Question 2 answered"
  - "Empirical finding: BatchGenerator parallel decode confirmed at N=2 (probe 22: wall=0.39s ≈ max(0.38,0.38), sum=0.76s)"
  - "Empirical finding: tools/tool_choice 'auto' returns full OpenAI envelope with finish_reason=tool_calls and message.tool_calls[0].function.name=get_weather (probe 15) — v2.7+ candidate per RESEARCH preliminary 8"
  - "Empirical finding: omitting 'model' field triggers expensive default_model HF reload (~83s) but is non-contaminating — server snaps back to qwen122b on next probe (probe 18 elapsed=83.4s, probe 19 immediately after = 0.18s)"
  - "Deviation from RESEARCH preliminary 9: SSE [DONE] sentinel emitted EVEN WITHOUT stream_options.include_usage on current build (probe 11 saw_done=true; preliminary expected false). RESEARCH Pitfall 5 mitigation may need updating in Plan 42-03 docs."
affects: [42-03, v2.7-tool-calling-investigation]

# Tech tracking
tech-stack:
  added: []  # No new pip deps; concurrent.futures + requests already present
  patterns:
    - "SSE chunk classification state-machine: iter_lines() + per-line dispatch (keepalive : / data: [DONE] / data: {...} / other) with first/last chunk delta-keys captured for shape inspection"
    - "ThreadPoolExecutor(max_workers=2) for N=2 concurrency probe — avoids asyncio dependency and matches RESEARCH Pitfall 1 anti-disrupt-daily-driver bound"
    - "STAT_N aggregator: full r.json() (not 300-char excerpt) for content-shape classification at fixed N-repeat boundary"
    - "probe() flag-based body shaping: raw_body / omit_model_field / body_model_override compose orthogonally to support error-surface probes without spawning helper variants"

key-files:
  created:
    - ".planning/phases/42-qwen-122b-openai-compat-test/42-02-SUMMARY.md"
  modified:
    - "bench/eval-openai-compat.py"
    - ".planning/STATE.md"

key-decisions:
  - "STAT_N helper bypasses probe() entirely and fetches full response body directly — required because probe()'s 300-char excerpt cap truncated content before the JSON could be parsed (initial Task 2 draft hit this; refactored mid-Task 2 per Rule 1)"
  - "probe_concurrent_pair uses ThreadPoolExecutor not asyncio (one-shot pair; matches RESEARCH 'don't hand-roll' table)"
  - "STAT_N record stores content_excerpts (full content, 200 char each) NOT response_excerpts (full HTTP envelope) — Plan 42-03 rendering will benefit from human-readable content"
  - "Probe 18 (omit_model_field) accepts 83s elapsed as expected (HF default_model reload); probe 19 verifies non-contamination by hitting qwen122b path again"

patterns-established:
  - "Phase 42 method-dispatch surface (final post-42-02): GET / STREAM / PAIR / STAT_N / POST — all driven by entry['method'] in PROBES list"
  - "Mid-flight bench-gate sandwich (pre + mid + post): proven safe through riskiest probe set (concurrency + malformed body + bogus model + missing model field)"

# Metrics
duration: 22min
completed: 2026-05-07
---

# Phase 42 Plan 02: Streaming + concurrency + errors + coherence probes Summary

**Extended bench/eval-openai-compat.py from 318 LOC + 10 probes (Plan 42-01) to 870 LOC + 25 probes covering all 8 RESEARCH.md surfaces; new SSE state-machine helper, N=2 ThreadPoolExecutor concurrency helper, STAT_N full-response aggregator; 7/7 bench gate preserved through riskiest probe set; zero src/ diff; one auto-fixed Rule 1 bug (STAT_N excerpt-truncation regression caught and fixed mid-Task 2).**

## Performance

- **Duration:** ~22 min (plan start 03:54Z → final post-flight gate complete ~04:16Z)
- **Started:** 2026-05-07T03:54:22Z (pre-flight gate)
- **Completed:** 2026-05-07T04:15:00Z approximate (post-flight gate end)
- **Tasks:** 2
- **Files modified:** 1 (bench/eval-openai-compat.py)
- **Files created:** 1 (this summary)
- **Pre-flight gate:** 7/7 PASS at 2026-05-07T03:54:35Z (~13s)
- **Mid-flight gate (after Task 2):** 7/7 PASS at ~04:11Z (~58s)
- **Post-flight gate:** 7/7 PASS at ~04:14Z (~62s)
- **Probe-driver run (final):** 25 records, ~98s total wall-clock (probe 18 dominates at 83.4s; remaining 24 probes finish in <15s combined)

## Accomplishments

- `bench/eval-openai-compat.py` extended from 318 → 870 LOC. Three new helper functions:
  1. `probe_stream(label, category, body, ...)` — SSE state machine via `requests.iter_lines(decode_unicode=True)`. Classifies each line as keepalive (starts with ":") / data:[DONE] / data:{json}, then aggregates chunk_count, role_chunks_count, content_chunks_count, finish_reason_seen, saw_done, first/last chunk delta-keys + 200-char excerpts. RESEARCH.md Pattern 2.
  2. `probe_concurrent_pair(label, category, body_a, body_b, ...)` — `concurrent.futures.ThreadPoolExecutor(max_workers=2)` with two simultaneous POSTs. Records http_codes pair, elapsed_s_each, wall_clock_s, elapsed_s_sum, elapsed_s_max. N=2 only per RESEARCH Pitfall 1.
  3. `_dispatch_stat_n(entry)` — N-repeat aggregator that BYPASSES probe() (no 300-char excerpt cap) and parses full r.json() to count valid_json + prose_wrap reliably. Used by Surface 5 probes 20 + 21.
- `probe()` extended with three optional flags: `raw_body=None` (POST literal string for malformed-body probe 16), `omit_model_field=False` (skip MODEL_PATH merge for probe 18 default_model fallback), `body_model_override=None` (substitute bogus string for probe 17 HF-fetch error). Flags compose orthogonally; default behavior unchanged.
- PROBES list grown 10 → 25 entries covering all 8 RESEARCH surfaces (endpoint, response_format, role, streaming, tools, error, concurrency, coherence) + bonus response_format_stat aggregate category.
- All 25 probes ran end-to-end against live `localhost:8001` and emitted valid JSONL.
- Bench-gate sandwich pre-flight 7/7 PASS + mid-flight 7/7 PASS + post-flight 7/7 PASS — Plan 42-02's riskier probe set (concurrency, malformed JSON, bogus model id) proven non-contaminating.
- Zero `src/` diff: `git diff master -- src/` returns 0 lines (Phase 42 measurement-work invariant holds).

## Task Commits

Each task was committed atomically:

1. **Task 1: probe_stream + 5 probes (streaming + tools)** — `2954144` (feat)
2. **Task 2: probe_concurrent_pair + STAT_N + 10 probes (errors + schema-stat + concurrency + coherence)** — `a02dfd3` (feat)

**Plan metadata:** TBD (this commit) (docs: complete plan)

## Files Created/Modified

- `bench/eval-openai-compat.py` (modified: 318 LOC → 870 LOC, +578 net) — extended with 3 helpers + 4 probe() flags + 15 new PROBES entries
- `.planning/phases/42-qwen-122b-openai-compat-test/42-02-SUMMARY.md` (created, this file)
- `.planning/STATE.md` (modified) — Phase 42 Plan 02 completion entry

## Final probe transcript (LOG_DIR)

`bench/runs/qwen35-eval-20260507-131320/probes.jsonl` — 25 records, valid JSONL, hand-off to Plan 42-03 rendering branch.

### Probe count breakdown by category (8 surfaces + 1 stat aggregate)

| Category               | Count | Surface | Severity profile           |
| ---------------------- | ----- | ------- | -------------------------- |
| endpoint               | 5     | 1       | 4 PASS + 1 LOW             |
| response_format        | 3     | 2       | 3 HIGH                     |
| response_format_stat   | 2     | 5       | 1 HIGH + 1 LOW             |
| role                   | 2     | 3       | 1 HIGH + 1 PASS            |
| streaming              | 4     | 4       | 4 LOW                      |
| tools                  | 1     | bonus   | 1 PASS                     |
| error                  | 4     | 7       | 2 LOW + 2 MEDIUM           |
| concurrency            | 1     | 8       | 1 PASS                     |
| coherence              | 3     | 6       | 3 PASS                     |
| **TOTAL**              | **25** |         | **5 HIGH / 2 MED / 8 LOW / 10 PASS** |

### Per-category one-line empirical verdicts

| Category               | Verdict |
| ---------------------- | ------- |
| endpoint               | /v1/chat/completions, /v1/completions, /v1/models, /health all PASS; /v1/responses 404 (LOW) |
| response_format        | json_object + json_schema strict both silently ignored — NON-CONFORMANT (HIGH; matches RESEARCH preliminaries 1+2) |
| response_format_stat   | At temp=0.0, response_format vs no-response_format produce IDENTICAL output (5/5 "Alice/28" prose-wrapped) — response_format has zero effect (HIGH; answers RESEARCH Open Question 2) |
| role                   | mid-conv system 404 (HIGH; matches RESEARCH preliminary 7); start-only system 200 (PASS control) |
| streaming              | role-on-every-chunk confirmed (probe 11: 6/6); finish_reason='stop'/'length' both observable; **deviation:** [DONE] emitted regardless of stream_options.include_usage on current build |
| tools                  | tool_choice 'auto' returns full OpenAI envelope finish_reason=tool_calls, message.tool_calls[0].function.name=get_weather — CONFORMANT (PASS; v2.7+ candidate per RESEARCH preliminary 8) |
| error                  | Malformed body 400 + flat `{"error":<string>}` (NOT OpenAI envelope) — LOW; Bogus model 404 + HF Repository Not Found (MED); missing model 200 + model='default_model' but **expensive 83.4s** (MED; expected per RESEARCH preliminary 6) |
| concurrency            | N=2 wall_clock=0.39s ≈ max(0.38,0.38), sum=0.76s — BatchGenerator parallel decode confirmed (PASS; matches RESEARCH preliminary 10) |
| coherence              | All 3 sequential calls returned correctly-shaped responses; full content inspection deferred to Plan 42-03 (no leakage from raw HTTP envelope inspection) |

## Decisions Made

- **STAT_N bypasses probe() entirely** — initial Task 2 draft routed STAT_N through probe() and parsed the 300-char `response_excerpt`, but the response envelope's preamble (`id` / `system_fingerprint` / etc.) consumed all 300 chars before the content body, making `valid_json_count=0/prose_wrap_count=0` noise rather than signal. Refactor: STAT_N now does its own `requests.post(...)` and parses full `r.json()`. Same JSONL-shape invariant preserved (one record per probe); STAT_N stores `content_excerpts` (200-char content body) instead of `excerpts` (truncated raw envelope).
- **No new pip deps** — `concurrent.futures` is stdlib; `requests` already present from Plan 42-01. RESEARCH.md "Don't Hand-Roll" table allows ThreadPoolExecutor for N=2 concurrency probe; no asyncio orchestrator needed.
- **probe() default `path="/v1/chat/completions"` already in place since Plan 42-01** — Task 2 added 3 NEW orthogonal flags (raw_body, omit_model_field, body_model_override) keeping the function backward-compatible. None of probes 01-15 needed to change.
- **STREAM probe `max_tokens` default 64 (vs probe()'s 8)** — chosen so probe 14 (finish_reason='length') reliably fires when prompt asks for "long story" (8 tokens too low to discriminate). Per-probe override still possible via PROBES entry.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] STAT_N excerpt-truncation regression in initial Task 2 draft**

- **Found during:** Task 2 STEP 3 verify (after first 25-record run)
- **Issue:** Initial `_dispatch_stat_n` reused `probe()` and parsed `response_excerpt` (300-char cap), but mlx_lm.server's response envelope preamble (`id`, `system_fingerprint`, `created`, etc.) consumed all 300 chars before the content body could be reached. Result: probes 20+21 reported `valid_json_count=0` AND `prose_wrap_count=0` for both — pure noise.
- **Fix:** Refactored `_dispatch_stat_n` to do its own `requests.post(...)` and parse full `r.json()`. Renamed result field `excerpts` → `content_excerpts` (200-char content body slice). Re-ran end-to-end suite: probes 20+21 now report 5/5 valid_json + 5/5 prose_wrap with content="```json\n{\"name\":\"Alice\",\"age\":28}\n```" identical for both — strong empirical signal.
- **Files modified:** bench/eval-openai-compat.py (only `_dispatch_stat_n` body)
- **Verification:** Probe 20 + 21 stats now report meaningful counts; identical "Alice/28" output across both confirms response_format has no effect at temp=0.0.
- **Committed in:** `a02dfd3` (Task 2 commit, single-commit fix)

**2. [Rule 2 - Missing-Critical] STAT_N storage contract update**

- **Found during:** Same refactor as #1
- **Issue:** Renaming `excerpts` → `content_excerpts` is a JSONL field-name change; Plan 42-03 rendering may have assumed the old name.
- **Fix:** Documented the field name explicitly in `_dispatch_stat_n` docstring; SUMMARY.md `patterns-established` notes the new contract.
- **Verification:** N/A — Plan 42-03 has not started; this is the contract baseline.
- **Committed in:** `a02dfd3` (same commit)

---

**Total deviations:** 2 auto-fixed (1 Rule 1 bug, 1 Rule 2 contract clarification).
**Impact on plan:** Both auto-fixes preserve the 25-record success criterion AND make probes 20+21 actually informative. No scope creep — refactor stayed within `_dispatch_stat_n`; no new probes added.

### Deviations from RESEARCH.md preliminaries

**1. SSE [DONE] sentinel always emitted (RESEARCH preliminary 9 expected gating on stream_options.include_usage)**

- **Probe affected:** 11-stream-baseline (no stream_options) AND 12-stream-with-usage
- **RESEARCH preliminary 9 expectation:** `[DONE]` only emitted when `stream_options.include_usage: true`
- **Empirical observation:** Probe 11 has `saw_done=true` (no stream_options); probe 12 also `saw_done=true` (with include_usage). Both show `chunk_count` 6 vs 7 (probe 12 has the extra usage chunk).
- **Interpretation:** Either mlx_lm.server build has changed since 2026-05-06 research (current `system_fingerprint` is `0.31.3-0.31.2-macOS-26.3-arm64-arm-64bit-Mach-O-applegpu_g16s` — the `.2` suffix may indicate a patch bump), OR RESEARCH preliminary 9's reading of "no [DONE] without include_usage" was incomplete (maybe the `[DONE]` comes only at `finish_reason: stop`, not the natural EOF).
- **Plan 42-03 implication:** RESEARCH Pitfall 5 mitigation pattern for downstream consumers ("assert [DONE] presence only when include_usage=true") may need updating. Doc should note: on current build, `[DONE]` appears for finish_reason='stop' regardless of stream_options.
- **Severity:** LOW (does not affect blueCode v2.6 — blueCode does not consume streaming today).
- **Action:** Document in Plan 42-03 markdown report; flag as "current-build observation" since it diverges from RESEARCH text.

**2. None other** — All other RESEARCH preliminaries reproduced verbatim (preliminaries 1, 2, 3, 4, 5, 6, 7, 8, 10, 11 all match; #9 deviation as above).

The system_fingerprint observed in all 25 probes is `0.31.3-0.31.2-macOS-26.3-arm64-arm-64bit-Mach-O-applegpu_g16s` — RESEARCH was authored at 0.31.3; the dual-version string suggests a 0.31.3 binary with 0.31.2 chat-template lib loaded. Plan 42-03 should call this out in its "Server build" section.

## Issues Encountered

- **Probe 18 takes 83.4s** when the `model` field is omitted. This is expected per RESEARCH preliminary 6 (server falls back to `default_model`, which triggers an HF tokenizer/weight resolution path). Probe 19 immediately afterwards completed in 0.18s with `model: /Users/ohama/llm-system/models/qwen122b` — server self-recovers. Bench gate post-flight 7/7 PASS confirms no contamination. **No action needed.**
- **STAT_N regression from excerpt truncation** — caught during Task 2 verify (auto-fixed; see Deviations §1).

## User Setup Required

None — no external service configuration required. All probe runs hit existing `localhost:8001` mlx_lm.server. No new pip deps. No launchd plist changes.

## Next Phase Readiness

**Plan 42-03 hand-off (rendering + mitigation docs):**

- **Probe artifact:** `bench/runs/qwen35-eval-20260507-131320/probes.jsonl` — 25 records, valid JSONL, all 8 RESEARCH surfaces represented + tools bonus.
- **JSONL field shapes available for renderer:**
  - POST records: `{label, category, method, path, http_code, response_excerpt, elapsed_s, expected, severity_hint, request_excerpt}`
  - GET records: same minus `request_excerpt`
  - STREAM records: `{label, category, method, path, http_code, chunk_count, keepalive_count, role_chunks_count, content_chunks_count, tool_calls_chunks_count, finish_reason_seen, saw_done, other_count, first_chunk_keys, last_chunk_keys, first_chunk_excerpt, last_chunk_excerpt, elapsed_s, expected, severity_hint, request_excerpt}`
  - PAIR records: `{label, category, method, path, http_codes:[a,b], elapsed_s_each:[a,b], wall_clock_s, elapsed_s_sum, elapsed_s_max, excerpts:[a,b], errors:[a,b], expected, severity_hint, request_excerpts:[a,b]}`
  - STAT_N records: `{label, category, method, path, n_repeats, http_codes:[N], valid_json_count, prose_wrap_count, content_excerpts:[N], elapsed_s_total, expected, severity_hint, request_excerpt}`
- **Renderer should populate the `--render` argparse branch** in `bench/eval-openai-compat.py` (currently emits "rendering deferred to Plan 42-03"). Output: markdown report at `documentation/qwen35-122b-openai-compat.md` per Phase 42 RESEARCH Recommended layout.
- **Sections the renderer should produce** (from RESEARCH §Severity rubric × 25 records):
  - Per-surface verdict table (8 surfaces; one row per category with severity pie + key finding)
  - Per-probe detail (label, expected, http_code observation, severity, response excerpt)
  - **HIGH severity → CLAUDE.md "Key Seams" update list** (probes 06, 07, 08, 09, 20 — all 3 response_format probes confirm the existing v2.6 invariant; mid-conv role:system reconfirms Phase 17-02+20-03)
  - **MEDIUM severity → mitigation doc only** (probes 17, 18 error envelope shape; documented as opaque-string consumer pattern)
  - **PASS positives → v2.7+ /gsd:add-todo candidates** (probe 15 tools, probe 22 concurrency)
- **Deviation from RESEARCH preliminary 9 to flag in renderer:** `[DONE]` sentinel observed even without `stream_options.include_usage` on current build (`0.31.3-0.31.2`). Mitigation pattern in any future blueCode SSE consumer should NOT assume the gating.
- **Tools (preliminary 8) confirmed CONFORMANT** — Plan 42-03 should generate `/gsd:add-todo` text for v2.7+ recommending the OpenAI tool-calling spec replace blueCode's custom JSON-schema action DU.

**Blockers/concerns:** None. Bench gate baseline preserved through all 25 probes including the riskiest set (concurrency + malformed body + bogus model + missing model). No `src/` diff (measurement work invariant). Server known-stable at `0.31.3-0.31.2`. Plan 42-03 is unblocked.

---
*Phase: 42-qwen-122b-openai-compat-test*
*Plan: 02*
*Completed: 2026-05-07*
