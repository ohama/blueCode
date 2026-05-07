---
phase: 42-qwen-122b-openai-compat-test
plan: 01
subsystem: testing
tags: [openai-compat, mlx-lm-server, http-probes, jsonschema, bench-harness, qwen122b, response_format, role-handling]

# Dependency graph
requires:
  - phase: 21-bench-eval-qwen35-122b
    provides: bench/eval-qwen35-122b.sh harness pattern, bench/.venv-eval, run.sh --gate authority
  - phase: 17-prompt-template-mid-conversation
    provides: Role=System mid-conversation HTTP 404 invariant (mitigated upstream)
  - phase: 20-sampling-and-server
    provides: Role=System mid-conversation HTTP 404 invariant re-confirmation post-Qwen 3.5
provides:
  - "bench/eval-openai-compat.py: 318-LOC probe driver, argparse + probe()/probe_get() helpers + 10-entry PROBES list, JSONL emitter with per-record fp.flush() for crash-resumability"
  - "bench/eval-qwen35-122b.sh --openai-compat mode: dispatchable shell handler (run_openai_compat) wired between run_coldstart and run_full"
  - "bench/.venv-eval/jsonschema 4.26.0 installed (one new pip dep, MIT-licensed)"
  - "Empirical reproduction of RESEARCH.md preliminary probes 1, 2, 7, 11 against fresh-restarted 122B service (post --max-tokens 4096 plist update)"
  - "Bench-gate sandwich proven safe: 7/7 PASS pre + 7/7 PASS post for the Plan 42-01 probe set (no KV contamination, baseline preserved)"
affects: [42-02, 42-03, v2.7-tool-calling-investigation]

# Tech tracking
tech-stack:
  added: [jsonschema-4.26.0]
  patterns:
    - "Probe-driver pattern: fixed PROBES dict-list + dispatch helper + per-probe JSONL record + fp.flush() per record (crash-resumability)"
    - "raw r.text[:300] capture (NOT r.json()) — robust against malformed/non-JSON server responses"
    - "Bench-gate sandwich: --gate (pre) → exploratory probes → --gate (post), with sandwich PASS as commit gate"

key-files:
  created:
    - "bench/eval-openai-compat.py"
    - ".planning/phases/42-qwen-122b-openai-compat-test/42-01-SUMMARY.md"
  modified:
    - "bench/eval-qwen35-122b.sh"
    - "bench/requirements-eval.txt"
    - ".planning/STATE.md"

key-decisions:
  - "PROBES kept as dict-list (not tuple-list) so Plan 42-02 can add fields (e.g., n_repeats, stream, tools) non-breakingly"
  - "--full does NOT call run_openai_compat (RESEARCH anti-pattern: parallel probes contaminate KV)"
  - "Probe driver populated with all 10 probes in Task 1 (not skeleton-only) so Task 1 verify gate (≥10 records) passes via single smoke run; Task 2 contributes only the shell wiring + verification end-to-end run"
  - "Smoke + main run hit live 8001 with 10 short probes each; bench gate sandwich validates this is safe"

patterns-established:
  - "Phase 42 probe-driver convention: dict entry {label, category, method, path, body?, max_tokens?, expected, severity_hint}; default method=POST, default path=/v1/chat/completions, default max_tokens=8"
  - "Response excerpt cap at 300 chars (matches needle.py's verbose-printf style; protects JSONL line length)"

# Metrics
duration: 8m (gate-to-gate work; 71m wall-clock including documentation)
completed: 2026-05-07
---

# Phase 42 Plan 01: OpenAI-compat probe harness scaffolding Summary

**HTTP-probe harness for mlx_lm.server OpenAI-compat surface: 318-LOC Python driver + `bench/eval-qwen35-122b.sh --openai-compat` mode + 10 probes (Surfaces 1+2+3) reproducing RESEARCH preliminaries 1, 2, 7, 11 against fresh-restarted 122B; bench-gate sandwich 7/7 PASS pre + post.**

## Performance

- **Duration:** 8m 31s (gate-to-gate measurement work); 71m wall-clock (includes documentation/SUMMARY)
- **Started:** 2026-05-07T02:34:55Z
- **Completed:** 2026-05-07T03:45:56Z
- **Tasks:** 2
- **Files modified/created:** 3 code + 1 summary
- **Pre-flight gate:** 7/7 PASS at 2026-05-07T02:36:44Z (66s elapsed)
- **Post-flight gate:** 7/7 PASS at 2026-05-07T02:43:26Z (68s elapsed)
- **Probe-driver run:** 10/10 probes recorded successfully, ~3.7s total wall-clock

## Accomplishments

- `bench/eval-openai-compat.py` created (318 LOC, executable): probe()/probe_get() helpers + 10-entry PROBES dict-list + main() with argparse `--output-dir`/`--render` + per-record `fp.flush()` for crash resumability.
- `bench/eval-qwen35-122b.sh` extended with `run_openai_compat()` function + `--openai-compat` dispatcher arm + usage() entry. `--full` mode NOT extended (intentional separation per RESEARCH.md anti-pattern).
- `bench/requirements-eval.txt` adds `jsonschema>=4.21`; venv now has 4.26.0 installed.
- All 10 probes ran against live `localhost:8001` and emitted valid JSONL with HTTP codes matching RESEARCH preliminaries 1-7 + 11 verbatim.
- Bench-gate sandwich (pre + post) both 7/7 PASS — Plan 42-01 probe set proven safe to run alongside daily-driver workflows.
- Zero `src/` diff: `git diff master -- src/` empty (Phase 42 measurement-work invariant holds).

## Task Commits

Each task was committed atomically:

1. **Task 1: Pre-flight gate + jsonschema dep + eval-openai-compat.py skeleton** — `065bbf7` (chore)
2. **Task 2: Wire `--openai-compat` mode + verify 10 probes end-to-end** — `cedd116` (feat)

**Plan metadata:** TBD (this commit) (docs: complete plan)

## Files Created/Modified

- `bench/eval-openai-compat.py` (created, 318 LOC) — probe driver with 10 probes covering Surfaces 1+2+3
- `bench/eval-qwen35-122b.sh` (modified, +26/-2) — added `run_openai_compat` function + dispatcher arm + usage entry
- `bench/requirements-eval.txt` (modified, +1) — added `jsonschema>=4.21`
- `.planning/STATE.md` (modified) — Phase 42 Plan 01 completion entry
- `.planning/phases/42-qwen-122b-openai-compat-test/42-01-SUMMARY.md` (created, this file)

## Per-probe HTTP code matrix (Plan 42-01 actual run vs RESEARCH preliminaries)

LOG_DIR: `bench/runs/qwen35-eval-20260507-114034/`

| Label | HTTP | Severity | RESEARCH preliminary | Verdict | Match? |
|-------|------|----------|----------------------|---------|--------|
| 01-baseline-chat | 200 | PASS | (baseline) | content non-empty | ✓ |
| 02-completions-legacy | 200 | PASS | #11 | object=text_completion confirmed | ✓ |
| 03-models-list | 200 | PASS | (baseline) | data[0].id="/Users/ohama/llm-system/models/qwen122b" | ✓ |
| 04-health-endpoint | 200 | PASS | (baseline) | /health returns OK | ✓ |
| 05-responses-endpoint | 404 | LOW | (open question 4) | /v1/responses NOT implemented | ✓ |
| 06-response-format-json-object | 200 | HIGH | #1 | silently ignored — content is prose | ✓ |
| 07-response-format-json-schema-strict | 200 | HIGH | #2 | silently ignored — markdown-fenced JSON | ✓ |
| 08-response-format-no-rerun-N1 | 200 | HIGH | (rerun of #1) | matches probe 06 | ✓ |
| 09-mid-conv-system-rejected | 404 | HIGH | #7 | exact error string `{"error": "System message must be at the beginning."}` | ✓ |
| 10-system-only-at-start | 200 | PASS | (control) | system-at-start accepted | ✓ |

**Match rate:** 10/10 (100%) — all probes reproduce RESEARCH.md 2026-05-06 preliminary findings against the fresh 2026-05-07T02:21Z server reload (post `--max-tokens 4096` plist update).

## Decisions Made

- **PROBES populated to 10 in Task 1, not Task 2.** Task 1 verify gate explicitly required `wc -l /tmp/_p42_01_smoke/probes.jsonl ≥ 10`, which forces full population. Task 2's probe content steps were therefore satisfied by Task 1's deliverable; Task 2 contributed shell wiring + end-to-end verification + post-flight gate. This kept commits atomic (Task 1 = "harness scaffolding ready", Task 2 = "wired into eval-qwen35-122b.sh + proven by post-gate").
- **dict-list shape for PROBES (not tuple-list).** Plan's RESEARCH skeleton showed tuples; opted for dict for Plan 42-02 forward compat (adding `n_repeats`, `stream`, `tools` fields will not break existing entries).
- **`--full` intentionally not extended to invoke `run_openai_compat`.** Per RESEARCH.md anti-pattern: parallelizing exploratory probes alongside the 7-stage --full eval contaminates KV state. `--openai-compat` stays opt-in.

## Deviations from Plan

None — plan executed exactly as written. The two minor execution sequencing notes (PROBES populated in Task 1; jsonschema not yet exercised by any imports) are not deviations: the plan's Task 1 verify line (≥10 records in /tmp smoke output) requires PROBES populated by end of Task 1, and jsonschema is staged as a dependency Plan 42-02/42-03 will activate.

### Deviations from RESEARCH.md preliminaries

None observed. The 2026-05-07T02:21Z server restart with `--max-tokens 4096` did not change probe behavior:
- Response_format json_object/json_schema still silently ignored (probes 06, 07, 08).
- Mid-conv role:system still rejected with HTTP 404 + exact error string (probe 09).
- /v1/completions still returns object="text_completion" (probe 02).
- /v1/responses still returns 404 (probe 05).

The new server-default `--max-tokens 4096` is a quiet enabler (only takes effect when client request omits the field; all Plan 42-01 probes specify max_tokens explicitly).

## Issues Encountered

None — all probes ran cleanly; both bench-gate sandwich runs passed; no syntax issues; no Python compile errors.

## User Setup Required

None — no external services configured; the venv update is automatic via the Task 1 step `bench/.venv-eval/bin/pip install -r bench/requirements-eval.txt`.

## Next Phase Readiness

**Plan 42-02 hand-off (streaming + concurrency + errors):**

- **Probe artifact:** `bench/runs/qwen35-eval-20260507-114034/probes.jsonl` — 10 records, valid JSONL, ready as input to a future `--render` branch implementation.
- **Extension points for Plan 42-02 (RESEARCH §Surfaces 4-7):**
  - **Surface 4 (streaming, SSE):** add probe with `stream:true`, capture chunk shape — re-validate RESEARCH preliminary 9 (`role` repeated every chunk).
  - **Surface 5 (tools):** add probe with `tools` + `tool_choice:"auto"` — re-validate RESEARCH preliminary 8 (FULL OpenAI envelope, `finish_reason:"tool_calls"`).
  - **Surface 6 (errors):** probes for malformed JSON body, BOGUS model id, missing model field — re-validate RESEARCH preliminaries 4, 5, 6.
  - **Surface 7 (concurrency):** N=2/4/8 parallel POSTs measuring batch behavior — extend RESEARCH preliminary 10.
- **Driver extension:** add streaming helper `probe_stream(label, body, ...)` using `requests.post(stream=True)` for line-by-line SSE consumption; extend `_dispatch` with `method=="STREAM"` arm.
- **PROBES list growth:** Plan 42-02 brings count from 10 → ~25-30. Each new probe entry should follow the same dict shape; the per-record `fp.flush()` invariant must be preserved.
- **`--render` branch:** Plan 42-03 will populate this argparse path; current implementation prints "rendering deferred to Plan 42-03" and exits 0.
- **Bench-gate sandwich enforcement:** Plan 42-02 must repeat the `bash bench/run.sh --gate` pre + post protocol. Streaming + concurrency probes are higher KV-cache risk than Plan 42-01's set; if post-gate fails, the streaming probe set may need to be split into its own `--openai-compat-stream` mode.

**Plan 42-03 hand-off (rendering + mitigation docs):**

- The 10 records in the LOG_DIR now constitute the fixture for `--render` development. Each record has fields: `label`, `category`, `method`, `path`, `http_code`, `response_excerpt`, `elapsed_s`, `expected`, `severity_hint`, `request_excerpt` (POST only). All renderable as a markdown table.

**Blockers/concerns:** None. Bench gate baseline preserved. No `src/` diff (measurement work invariant). Server known-stable post 11:21Z plist reload.

---
*Phase: 42-qwen-122b-openai-compat-test*
*Plan: 01*
*Completed: 2026-05-07*
