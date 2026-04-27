# Roadmap: blueCode v2.1 Empirical Qwen 3.5 122B Coding Evaluation

**Status:** In Progress (started 2026-04-27)
**Phases:** 21 (single phase, 5 plans)
**Milestone goal:** Produce empirical answers to "is Qwen 3.5 122B-A10B-4bit MoE useful for daily coding?" via comprehensive measurement across correctness, performance, reliability, and coding-specific quality dimensions. Outcome is `documentation/qwen35-122b-coding-eval.md` with 100-point scorecard verdict and KEEP / KEEP-WITH-CAVEATS / ESCALATE recommendation.

## Overview

v2.1 is a single-phase, 5-plan evaluation milestone. Original 3-phase scope was considered but rejected: ceremony cost exceeds value at this size. The 5 plans have a clear DAG (Plan 21-01 setup → Plans 21-02/21-03/21-04 measurements (sequential due to single 122B service contention) → Plan 21-05 aggregation + verdict doc). All plans `autonomous: true` (live LLM runs are deterministic; aggregation is mechanical scorecard application).

**Phase numbering:** Continues from v2.0's Phase 20. v1.0 used 1-5; v1.1: 6-7; v1.2: 8/9/9.1; v1.3: 10-11; v1.4: 12-13; v2.0: 14-20. v2.1 uses 21.

**Approach (per approved plan file):** Hybrid bash + Python(venv). Pure-bash for performance/reliability/refactoring (reuses `bench/run.sh` patterns, calls `dotnet run` for full agent loop). Python(venv) only for HumanEval+ scoring (`evalplus` library is non-negotiable) and long-context needle (mlx-runner template adapted to HTTP). mlx-runner's in-process `mlx_lm.load()` would OOM the launchd-managed 122B service — MUST adapt to HTTP.

**Bench gate stability mandatory:** `bash bench/run.sh --gate` exit 0 with 7/7 PASS must hold post-eval. Eval is purely external instrumentation; modifies fixtures (multi-file refactor) but EXIT trap restores them. NO `bench/baseline.json` or `src/` changes.

**Verdict format:** 100-point scorecard across 4 weighted dimensions (Correctness 40 / Performance 25 / Reliability 25 / Coding-quality 10) with explicit thresholds per sub-criterion.

---

## Phases

- [ ] **Phase 21: Empirical Qwen 3.5 122B Coding Evaluation** — 5 plans (harness scaffolding, HumanEval+, fixtures, multi-turn/needle/schema, aggregation+verdict doc)

---

## Phase Details

### Phase 21: Empirical Qwen 3.5 122B Coding Evaluation

**Goal:** Deliver `documentation/qwen35-122b-coding-eval.md` with empirically-measured 100-point scorecard verdict against the 9 measurement requirements (PERF-EVAL-01..02, CORR-EVAL-01..04, REL-EVAL-01..03) plus DOC-EVAL-01 doc-deliverable. Bench gate stays 7/7 PASS post-eval.

**Depends on:** v2.0 milestone (single-model 122B canonical; Role=User invariant; sampling params; reasoning_content fallback).

**Requirements:** PERF-EVAL-01, PERF-EVAL-02, CORR-EVAL-01, CORR-EVAL-02, CORR-EVAL-03, CORR-EVAL-04, REL-EVAL-01, REL-EVAL-02, REL-EVAL-03, DOC-EVAL-01

**Success Criteria** (what must be TRUE when Phase 21 completes):

1. `bench/eval-qwen35-122b.sh --setup` exits 0 with `bench/.venv-eval/` populated; `bench/.venv-eval/bin/python -c "import evalplus; print(evalplus.__version__)"` succeeds (Python 3.12 fallback documented if 3.14 incompatible with evalplus).

2. `bench/eval-qwen35-122b.sh --full` (everything except `--coldstart`) completes within ~2hr wall-clock and produces all expected artifacts in `bench/runs/qwen35-eval-<ts>/`:
   - `throughput.json` (15 entries, `tokens_per_sec` numeric > 0)
   - `ttft.json` (10 entries, `ttft_ms` numeric > 0)
   - `humaneval_results.json` (164 entries × 2 modes; evalplus pass@1 numeric)
   - `multiturn_N{1,3,5,7,10}/` (per-N session JSONLs)
   - `refactor_multifile_diff.txt` (agent's edits to 3 F# files)
   - `schema_rate.txt` ("X/50 InvalidJsonOutput")
   - `needle.json` (4 size entries with retrieval correctness boolean + latency)

3. `documentation/qwen35-122b-coding-eval.md` exists, ≥600 lines, contains all 10 sections, ends with `Total: NN/100, Recommendation: <KEEP|KEEP-WITH-CAVEATS|ESCALATE>` matching the scorecard rubric. Each section has a verdict line (e.g., "§3.1 Throughput verdict: PASS — median 42 tok/s ≥ 30 threshold").

4. `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)` post-eval. NO `bench/baseline.json` modifications. `git diff src/` shows no source code changes.

5. New artifacts on disk: `bench/eval-qwen35-122b.sh` + `bench/eval-humaneval-http.py` + `bench/eval-needle.py` + `bench/requirements-eval.txt` + `bench/fixtures/refactor_multifile/{Calculator,Main,Tests}.fs` + `README.md` + `bench/fixtures/bug_binsearch.fs` + `bench/fixtures/bug_python_typeerror.py` + `bench/fixtures/bug_typescript_async.ts` + `bench/fixtures/multiturn_prompts.txt` + `documentation/qwen35-122b-coding-eval.md`. Modified: `bench/run.sh:18` EXIT trap fixture-list extension; `.planning/STATE.md` observation note; `CLAUDE.md` 2-line cross-reference under "Bench" section. Gitignored: `bench/.venv-eval/`.

**Plans:** 5 plans expected

Plans:
- [ ] 21-01-PLAN.md — Harness scaffolding (`bench/eval-qwen35-122b.sh` ~250 lines bash with mode-flag dispatch) + venv setup + `--throughput` (PERF-EVAL-01) + `--ttft` (PERF-EVAL-02). Reuses `bench/run.sh:30-46` `run()`, `bench/run.sh:181-186` port precondition, `bench/run.sh:23-24` LOG_DIR convention. SSE TTFT awk filter must skip `: keepalive N/14` SSE comments and initial `delta.role` chunk.
- [ ] 21-02-PLAN.md — HumanEval+ HTTP adapter (`bench/eval-humaneval-http.py` ~150 lines). Adapted from `mlx-runner/mlx_full_auto_runner.py:27-41` — replace `mlx_lm.load`/`generate` with `requests.post` to `localhost:8001`. Two modes: chat (`/v1/chat/completions` wrapped) and completion (`/v1/completions` raw). Sampling temp=0.2 (eval-standard, differs from blueCode's 0.7 runtime default; documented in §1 Methodology). Code extraction: parse first ` ```python ... ``` ` block; fallback to function-signature substring. evalplus.evaluate post-hoc scoring. (CORR-EVAL-01)
- [ ] 21-03-PLAN.md — Multi-file refactoring + algorithm-level + language coverage fixtures. New: `bench/fixtures/refactor_multifile/` directory (4 files), `bug_binsearch.fs`, `bug_python_typeerror.py`, `bug_typescript_async.ts`. Extends `bench/run.sh:18` EXIT trap fixture list. Invokes `dotnet run --project src/BlueCode.Cli -- --verbose --model 122b "<prompt>"` for full agent-loop runs; transcripts to `bench/runs/qwen35-eval-<ts>/refactor_multifile_diff.txt` + per-fixture log. (CORR-EVAL-02, CORR-EVAL-03, CORR-EVAL-04)
- [ ] 21-04-PLAN.md — Multi-turn degradation curve + JSON schema rate + long-context needle. Multi-turn extends `bench/run.sh:111-157` `mt()` pattern, parameterized for N=1,3,5,7,10. Schema rate runs 50 single-turn invocations, greps `InvalidJsonOutput`. Needle adapts `mlx-runner/mlx_full_auto_runner.py:43-68` to HTTP; reads `MaxModelLen` from `/v1/models` first (probe pattern from `QwenHttpClient.fs:probeModelInfoAsync`). New: `bench/eval-needle.py` (~80 lines), `bench/fixtures/multiturn_prompts.txt` (10 coding-relevant prompts). (REL-EVAL-01, REL-EVAL-02, REL-EVAL-03)
- [ ] 21-05-PLAN.md — Aggregation + verdict + `documentation/qwen35-122b-coding-eval.md`. ~600 lines, 10 sections (§1 Methodology / §2 Correctness / §3 Performance / §4 Reliability / §5 Coding quality (qualitative) / §6 Comparison anchors with §6.3 cloud non-goal / §7 Verdict scorecard / §8 Caveats / §9 Re-evaluation thresholds / §10 Reproduction instructions). Each section ends with verdict line. Mirrors `documentation/benchmark-qwen35-eval.md` structure. Final scorecard: `Total: NN/100, Recommendation: <KEEP|KEEP-WITH-CAVEATS|ESCALATE>`. Aggregate verdict: ≥80 = KEEP; 60-79 OR any dimension <60% = KEEP-WITH-CAVEATS; <60 OR multi-turn degrades before turn 5 OR HumanEval+ <30% = ESCALATE. Final `bash bench/run.sh --gate` exit 0 verification (mandatory). Cross-reference added to `CLAUDE.md` under "Bench" section. Observation note in `.planning/STATE.md`. (DOC-EVAL-01)

**Plan dependencies:**
- 21-01 → 21-02 (humaneval needs venv from 21-01)
- 21-01 → 21-03 (fixtures use harness `curl_run` + invocation patterns)
- 21-02 + 21-03 → 21-04 (multi-turn + needle leverage harness; sequential 122B service access)
- 21-01..21-04 → 21-05 (aggregation needs all artifacts)

Wave structure: all 5 plans run sequentially (single 122B service, can't parallelize live runs).

**Architectural invariants (load-bearing):**

1. **mlx-runner constraint**: `mlx_lm.load()` in-process would OOM. MUST adapt to HTTP. Never load a second 122B instance.
2. **Bench gate stability**: `bash bench/run.sh --gate` exit 0 (7/7 PASS) before AND after eval. Mandatory final check in 21-05 verify.
3. **Role = User invariant** (Phase 20-03): all multi-turn injections (in 21-04) use `Role = User`. Never Role=System mid-conversation (HTTP 404 trap).
4. **No `src/` changes**: `git diff src/` empty post-eval. Eval is external instrumentation only.
5. **No `bench/baseline.json` changes**: 7-entry baseline preserved byte-for-byte.
6. **No new tests in `tests/BlueCode.Tests/`**: test count stays 282/1/0.
7. **Atomic commits per CLAUDE.md**: `chore(21-XX): {task-name}` for instrumentation; `docs(21-XX): write coding eval verdict doc` for the final doc; plan-meta separate.
8. **Cold-start gated behind `--coldstart` flag**: deferred from default `--full` per scope decision; reproducibility instructions in eval doc §10.
9. **Cloud comparison explicit non-goal**: documented in eval doc §6.3 as deliberate boundary.

**Out-of-scope guardrails (resist scope creep):**

- DO NOT modify `src/`, `bench/baseline.json`, `bench/run.sh` body (line 18 EXIT trap fixture-list extension only)
- DO NOT add new tests in `tests/BlueCode.Tests/`
- DO NOT add cold-start to default `--full` (gate behind `--coldstart` flag only)
- DO NOT add cloud comparison (Claude/GPT-4) calls (explicit non-goal; documented in §6.3)
- DO NOT touch Phase 20 deferred items (thinking-mode-on, native tool_calls, additionalProperties relaxation, max_tokens bump)
- DO NOT load second 122B instance via `mlx_lm.load()` (OOM constraint)
- DO NOT mock the LLM (this is empirical measurement against the real launchd-managed service)

**Verdict criteria (100-point scorecard, replicates approved plan file):**

| Dimension | Sub-criterion | Threshold | Points |
|-----------|---------------|-----------|--------|
| Correctness (40) | HumanEval+ pass@1 chat | ≥75% / 60-75% / 45-60% / <45% | 15/10/5/0 |
| Correctness (40) | F# bug-fix on 4 fixtures (B1, B2, bug_binsearch, refactor) | 3.75 each correct | up to 15 |
| Correctness (40) | Language coverage (Python + TypeScript) | 2.5 each correct | up to 5 |
| Correctness (40) | Multi-file refactor preserves test behavior | All-or-nothing | 5 |
| Performance (25) | Throughput median | ≥30 / 20-30 / 15-20 / <15 tok/s | 10/7/5/0 |
| Performance (25) | TTFT median (200-token prompt) | ≤500ms / 500-1500 / >1500 | 5/3/0 |
| Performance (25) | Cold-start (REFERENCE; deferred per scope) | ≤180s / 180-240 / >240 | 5/3/0 (or N/A) |
| Performance (25) | End-to-end task time within ±20% of `bench/baseline.json` | binary | 5 |
| Reliability (25) | JSON schema compliance | ≥49/50 / 47-49 / 45-47 / <45 | 10/7/3/0 |
| Reliability (25) | Multi-turn stable through N turns | 7+ / 5+ / <5 | 10/5/0 |
| Reliability (25) | Long-context needle at 32k | correct / degraded / failed | 5/2/0 |
| Coding quality (10) | Idiomatic F# (3 transcripts) | qualitative | 5 |
| Coding quality (10) | Generated tests compile + pass | binary | 3 |
| Coding quality (10) | Code review identifies ≥80% known issues | qualitative | 2 |

**Aggregate verdict:**
- ≥80/100: **KEEP** — empirically useful for daily F# coding via blueCode
- 60-79 OR any dimension <60%: **KEEP-WITH-CAVEATS** — document specific weaknesses
- <60 OR multi-turn degrades before turn 5 OR HumanEval+ <30%: **ESCALATE** — recommend cloud for non-trivial work

---

## Progress

| Phase | Milestone | Requirements | Plans Complete | Status | Completed |
|-------|-----------|--------------|----------------|--------|-----------|
| 21. Empirical Qwen 3.5 122B Coding Evaluation | v2.1 | PERF-EVAL-01..02, CORR-EVAL-01..04, REL-EVAL-01..03, DOC-EVAL-01 (10 reqs) | 0/5 | Not started | - |

---

*Roadmap created: 2026-04-27*
*Last updated: 2026-04-27 — initial roadmap from approved plan file (`/Users/ohama/.claude/plans/async-weaving-pnueli.md`); single phase with 5 plans (21-01..21-05); bench gate stability mandatory post-eval*
