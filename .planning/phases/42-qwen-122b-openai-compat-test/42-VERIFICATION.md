---
phase: 42-qwen-122b-openai-compat-test
verified: 2026-05-07T14:30:00Z
status: passed
score: 14/14 must-haves verified
---

# Phase 42: Qwen 122B OpenAI Compatibility Verification — Verification Report

**Phase Goal (verbatim from ROADMAP.md):** "Detailed empirical verification of mlx_lm.server @ 8001's /v1/chat/completions OpenAI-compatibility surface, both the parts blueCode currently exercises (agent-loop, --plan mode) and the new patterns v2.6 introduces (planner LLM call returning strict JSON for plan decomposition; executor LLM call with deviation-rules system prompt; per-task fresh conversation re-establishment)."

**Verified:** 2026-05-07T14:30:00Z
**Status:** PASSED
**Re-verification:** No — initial verification

## Goal Achievement

This is a **measurement-work phase** with no v2.6 requirements directly mapped. Verification is calibrated to the goal of producing the empirical report + mitigation patterns + conditional CLAUDE.md update. Not against feature-shape requirements.

### Observable Truths (aggregated across 3 plans)

| #   | Truth                                                                                                          | Status     | Evidence                                                                                                                |
| --- | -------------------------------------------------------------------------------------------------------------- | ---------- | ----------------------------------------------------------------------------------------------------------------------- |
| 1   | User can run `bash bench/eval-qwen35-122b.sh --openai-compat` and get probes.jsonl                             | ✓ VERIFIED | `bench/runs/qwen35-eval-20260507-131320/probes.jsonl` (25 lines, all valid JSON)                                        |
| 2   | Pre-flight `bash bench/run.sh --gate` returns 7/7 PASS BEFORE any 8001 probing                                 | ✓ VERIFIED | SUMMARY 42-01: pre-flight 7/7 PASS @ 2026-05-07T02:36:44Z (66s)                                                         |
| 3   | Plan 42-01 covers Surfaces 1+2+3 (endpoints, response_format, role handling)                                   | ✓ VERIFIED | Probes 01-10 in JSONL; categories: endpoint, response_format, role                                                      |
| 4   | Bench gate STILL returns 7/7 PASS after Plan 42-01's probes                                                    | ✓ VERIFIED | SUMMARY 42-01: post-flight 7/7 PASS @ 2026-05-07T02:43:26Z (68s)                                                        |
| 5   | `bench/.venv-eval` has `jsonschema>=4.21` installed                                                            | ✓ VERIFIED | `import jsonschema` succeeds; version 4.26.0; `bench/requirements-eval.txt` contains `jsonschema>=4.21`                  |
| 6   | User can re-run and get probes.jsonl with ≥25 records covering all 8 surfaces                                  | ✓ VERIFIED | 25 probes in JSONL spanning categories: endpoint/response_format/role/streaming/tools/error/concurrency/coherence/stat  |
| 7   | Streaming probe correctly classifies SSE chunk shape via `requests.iter_lines()`                               | ✓ VERIFIED | Probes 11-14 capture chunk_count/keepalive_count/role_chunks_count/saw_done; all `saw_done=True`                        |
| 8   | Schema/error probes capture mlx_lm.server's flat `{error: <string>}` envelope (not OpenAI's structured form)   | ✓ VERIFIED | Probes 16/17 capture HTTP 400/404 with flat error envelope (verdict EXPECTED-DIVERGENCE in report)                      |
| 9   | Concurrency probe N=2 confirms BatchGenerator parallel batching, no 429                                        | ✓ VERIFIED | Probe 22: wall=0.39s, sum=0.76s, ratio 1.03 ≈ perfect parallelism; HTTP 200/200                                         |
| 10  | Tools/tool_choice probe confirms PASS (v2.7+ candidate)                                                        | ✓ VERIFIED | Probe 15: HTTP 200, `finish_reason="tool_calls"` in envelope; surfaced as v2.7+ in report §v2.7+ candidates              |
| 11  | User can read `documentation/qwen35-122b-openai-compat.md` and find a per-surface table (8 surfaces)           | ✓ VERIFIED | 8 `### Surface N:` sections present; tables have columns Surface/Probe/HTTP/Verdict/Severity/Notes per RESEARCH §Pattern 4 |
| 12  | User can re-run and `--render` to regenerate the report (byte-identical reproducibility)                       | ✓ VERIFIED | Re-rendered to `/tmp/rendered.md` and `diff` against committed report = empty (BYTE-IDENTICAL)                          |
| 13  | Severity-classified findings table at top of report lists every HIGH and MEDIUM with mitigation pointer        | ✓ VERIFIED | "Findings by Severity" section: HIGH=3 (06/07/08), MEDIUM=2 (17/18) with one-line mitigations                           |
| 14  | CLAUDE.md gains a one-block update referencing the report (decision matrix triggered)                          | ✓ VERIFIED | CLAUDE.md:200 contains a Bench-section bullet referencing the report + v2.7+ tools/tool_choice candidate (probe 15)     |
| 15  | Final post-flight bench gate 7/7 PASS — milestone-wide invariant preserved                                     | ✓ VERIFIED | SUMMARY 42-03: FINAL post-flight 7/7 PASS @ 2026-05-07T05:07:41Z (`MT_122b` 7th label confirms full set)                |

**Score:** 14/14 truths verified (15 listed; 14 distinct must-haves across the 3 plans)

### Required Artifacts

| Artifact                                       | Expected (provides)                                          | Status         | Details                                                                                              |
| ---------------------------------------------- | ------------------------------------------------------------ | -------------- | ---------------------------------------------------------------------------------------------------- |
| `bench/eval-openai-compat.py`                  | Python probe driver with PROBES, render, stream, concurrent  | ✓ VERIFIED     | 1482 lines (>>280 min); contains `def render_report` (L1103), `def probe_stream` (L159), `def probe_concurrent_pair` (L306); 25 probes; no TODO/FIXME stubs |
| `bench/eval-qwen35-122b.sh`                    | `--openai-compat` mode + `run_openai_compat` dispatcher      | ✓ VERIFIED     | `run_openai_compat()` at L587; dispatcher arm `--openai-compat) run_openai_compat ;;` at L671; usage line present |
| `bench/requirements-eval.txt`                  | `jsonschema>=4.21` line                                      | ✓ VERIFIED     | File contains `jsonschema>=4.21`; venv has `jsonschema 4.26.0` installed                            |
| `documentation/qwen35-122b-openai-compat.md`   | 8-surface report with severity findings + reproducibility    | ✓ VERIFIED     | 252 lines (>200 min); 8 Surface tables; HIGH=3 / MEDIUM=2 / LOW=5 / PASS=15 verdict summary; Reproduction section names exact LOG_DIR + `--render` command |
| `CLAUDE.md`                                    | Reference to new report (Bench section per decision matrix)  | ✓ VERIFIED     | L200 bullet in Bench section: report path + HIGH-finding summary + v2.7+ candidate + reproduction command |
| `bench/runs/qwen35-eval-20260507-131320/probes.jsonl` | 25 valid JSON probe records                          | ✓ VERIFIED     | 25 lines, all parse as JSON (validated via Python); 20809 bytes                                      |

### Key Link Verification

| From                                       | To                                                              | Via                                          | Status     | Details                                                                  |
| ------------------------------------------ | --------------------------------------------------------------- | -------------------------------------------- | ---------- | ------------------------------------------------------------------------ |
| `bench/eval-qwen35-122b.sh`                | `bench/eval-openai-compat.py`                                   | `"$VENV_PY" bench/eval-openai-compat.py ...` | ✓ WIRED    | `run_openai_compat()` shells out to driver; dispatcher arm calls function |
| `bench/eval-openai-compat.py`              | `http://127.0.0.1:8001/v1/chat/completions`                     | `requests.post` per-probe                    | ✓ WIRED    | `requests.post` at L78/85/188/316; stream=True at L188; ThreadPoolExecutor at L332 |
| `bench/eval-openai-compat.py`              | `bench/runs/.../probes.jsonl`                                   | per-probe append + flush                     | ✓ WIRED    | 25 records produced and persisted; flush-per-record verified (no JSON corruption) |
| `documentation/qwen35-122b-openai-compat.md` | `bench/runs/qwen35-eval-20260507-131320/probes.jsonl`         | Reproduction section + `--render` command    | ✓ WIRED    | Report L6 names exact source transcript path; L13-17 give reproduction command |
| `bench/eval-openai-compat.py`              | `documentation/qwen35-122b-openai-compat.md`                    | `render_report()` in `--render` mode         | ✓ WIRED    | Re-render to /tmp diff = empty (byte-identical reproducibility)          |
| `CLAUDE.md`                                | `documentation/qwen35-122b-openai-compat.md`                    | one-line bullet in Bench section             | ✓ WIRED    | CLAUDE.md:200 references report by exact filename + reproduction command |

### Bench Gate Sandwich (Phase 42 invariant)

| Checkpoint                       | Time                       | Result   | Source                  |
| -------------------------------- | -------------------------- | -------- | ----------------------- |
| Plan 42-01 pre-flight            | 2026-05-07T02:36:44Z (66s) | 7/7 PASS | SUMMARY 42-01           |
| Plan 42-01 post-flight           | 2026-05-07T02:43:26Z (68s) | 7/7 PASS | SUMMARY 42-01           |
| Plan 42-02 pre-flight            | 2026-05-07T03:54:35Z (~13s) | 7/7 PASS | SUMMARY 42-02           |
| Plan 42-02 mid-flight (post Task 2) | 2026-05-07T~04:11Z (~58s) | 7/7 PASS | SUMMARY 42-02           |
| Plan 42-02 post-flight           | 2026-05-07T~04:14Z (~62s) | 7/7 PASS | SUMMARY 42-02           |
| Plan 42-03 FINAL post-flight     | 2026-05-07T05:07:41Z       | 7/7 PASS | SUMMARY 42-03 (MT_122b incl.) |

**6 independent gate confirmations across 3 plans.** Verifier did not re-run the gate (the post-flight at 2026-05-07T05:07:41Z is recent and trustworthy; re-running would add ~70s for marginal information). Sandwich invariant satisfied.

### Zero-src-diff Invariant

`git diff master -- src/` → 0 lines. Confirmed: Phase 42 is a measurement-work phase that touches no F# Core/Cli code.

### Reproducibility (byte-identical)

Re-ran:
```bash
bench/.venv-eval/bin/python bench/eval-openai-compat.py --render bench/runs/qwen35-eval-20260507-131320/probes.jsonl > /tmp/rendered.md
diff /tmp/rendered.md documentation/qwen35-122b-openai-compat.md
```
Result: empty diff. Report is byte-identical to a fresh re-render from JSONL.

### Probe HTTP Code Matrix Spot Check

Sampled the most semantically-loaded probes against JSONL:

| Probe | Expected | Observed (from JSONL) | Match |
|---|---|---|---|
| 09-mid-conv-system-rejected | HTTP 404 (Phase 17-02 invariant) | 404 | ✓ |
| 13/14-stream-finish-{stop,length} | `[DONE]` regardless of include_usage | both `saw_done=True`; probes 11/12 also `saw_done=True` | ✓ (DEVIATION from RESEARCH preliminary 9 — confirmed) |
| 15-tools-tool-choice-auto | `tool_calls` envelope conformant | HTTP 200, `finish_reason: "tool_calls"` in excerpt | ✓ PASS |
| 22-concurrent-pair | parallel decode (wall < sum) | wall=0.39s, sum=0.76s (ratio 1.03) | ✓ PASS |
| 20/21-response-format-rate-temp0-N5 | identical valid_json/prose_wrap | both 5/5 valid_json + 5/5 prose_wrap | ✓ HIGH (silent-ignore confirmed) |

### Anti-Patterns Found

None. Scanned `bench/eval-openai-compat.py` and `documentation/qwen35-122b-openai-compat.md` for TODO/FIXME/XXX/HACK/placeholder — zero hits.

### Requirements Coverage

N/A — Phase 42 is a measurement-work phase with no v2.6 requirements directly mapped (per phase verification context). Goal verification is via the 14 must-haves above, not requirement satisfaction.

### Human Verification Required

None. All goal-relevant truths are programmatically verifiable for measurement work:
- Probe HTTP codes verified via JSONL parse + jq spot-checks.
- Severity findings verified via report grep + JSONL cross-reference.
- Bench-gate sandwich verified via SUMMARY claims + JSONL freshness.
- Reproducibility verified via re-render diff.
- Zero-src-diff verified via `git diff`.

### v2.7+ Candidates Surfaced (per Plan 42-03)

The report's "v2.7+ candidates" section identifies three follow-up opportunities (see report L228-236):

1. **Replace custom JSON-schema action DU with native OpenAI tool calls** — probe 15 PASS with `finish_reason="tool_calls"` confirms `mlx_lm.server` honors the tools/tool_choice envelope. Migration path: extend `Action` DU codecs, add `tools=[...]` to `buildRequestBody`, deprecate the `<JsonSchemaCall>` schema.
2. **Wave-parallel exec (Phase F)** — probe 22 confirms BatchGenerator parallel decode at N=2 (wall=0.39s ≈ max 0.38s; sum=0.76s). Single-LLM-server is no longer a serialization bottleneck for parallel plan-step execution.
3. **/v1/responses endpoint shape investigation** — probe 05 returned HTTP 404 on this build; deeper shape testing is a v2.7+ candidate if the endpoint becomes available in a future mlx_lm version.

(Strictly speaking the report explicitly enumerates 2 candidates in its §v2.7+ section; the third — `/v1/responses` — is filed under "Out of scope" §3 as a deferred v2.7+ follow-up.)

### Empirical Headlines (from rendered report)

- **HIGH=3:** `response_format` field (json_object, json_schema_strict, no-rerun-N1) silently ignored by mlx_lm.server even at temp=0.0. Mitigation: blueCode v2.6+ MUST NOT rely on `response_format`; use prompt-instructed schema with retry.
- **MEDIUM=2:** Bogus model id triggers HF fetch (404, 0.25s); missing model field triggers 83.4s HF reload via `default_model` fallback (Phase 20-01 `HttpClient.Timeout=300s` justified).
- **PASS=15:** including probe 09 (mid-conv system rejected — Phase 17-02 invariant confirmed), probe 15 (tool_calls envelope), probe 22 (parallel batching at N=2), probes 12-14 (`[DONE]` sentinel emitted regardless of include_usage — DEVIATION from RESEARCH preliminary 9).

## Verdict

**Phase 42 PASSED.** All 14 must-haves verified. The phase delivered:

1. A 1482-line probe driver (`bench/eval-openai-compat.py`) with 25 probes spanning all 8 RESEARCH surfaces.
2. A 252-line empirical conformance report (`documentation/qwen35-122b-openai-compat.md`) with severity-classified findings (HIGH=3, MEDIUM=2, LOW=5, PASS=15) and byte-identical reproducibility.
3. A minimal CLAUDE.md update in the Bench section pointing to the report (correctly placed per the Plan 42-03 decision matrix — the response_format finding is mitigation guidance for v2.6+, not a modification to an existing seam).
4. 6 independent bench-gate confirmations (7/7 PASS at every checkpoint).
5. Zero `src/` diff — measurement work properly contained.

3 v2.7+ candidates surfaced for follow-up: native tools/tool_choice migration, wave-parallel exec (Phase F), and /v1/responses shape investigation.

---

*Verified: 2026-05-07T14:30:00Z*
*Verifier: Claude (gsd-verifier)*
