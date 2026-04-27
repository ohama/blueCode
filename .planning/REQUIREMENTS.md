# Requirements: blueCode v2.1 Empirical Qwen 3.5 122B Coding Evaluation

**Defined:** 2026-04-27
**Core Value:** Mac 로컬 Qwen 3.5 122B를 strong-typed F# agent loop로 안정적으로 돌린다 (single-model canonical post-v2.0)
**Milestone goal:** Produce empirical answers to "is Qwen 3.5 122B-A10B-4bit MoE useful for daily coding?" via comprehensive measurement across correctness, performance, reliability, and coding-specific quality dimensions. Deliver `documentation/qwen35-122b-coding-eval.md` with 100-point scorecard verdict and KEEP / KEEP-WITH-CAVEATS / ESCALATE recommendation.

## v2.1 Requirements (9 requirements, 4 categories — ALL Pending)

Each requirement is empirical, measurable, and produces a verdict line in the final eval doc. v2.0 measured step counts + elapsed times only; v2.1 closes that gap with anchor benchmarks (HumanEval+), throughput, TTFT, multi-turn degradation, long-context, multi-file refactoring, and language coverage.

### Performance

Direct measurement of inference characteristics — validates the 3.4× wall-clock-derived speedup claim with proper tokens/sec + TTFT numbers.

- [ ] **PERF-EVAL-01**: Tokens/sec throughput
  - **Goal:** Empirical median throughput from `/v1/chat/completions` with `usage.completion_tokens` numerator and wall-clock seconds denominator
  - **Behavior:** 5 distinct prompts (T1-style short, T6-style mid, B2-style with file context, multi-step reasoning, code generation) × 3 trials each. `max_tokens=512`, sampling per Qwen 3.5 model card. Report median + range per prompt and aggregate median.
  - **Validation:** `bench/runs/qwen35-eval-<ts>/throughput.json` contains 15 entries (5 prompts × 3 trials), each with `tokens_per_sec` numeric > 0.
  - **Threshold:** ≥30 tok/s = 10 pts; 20-30 = 7 pts; 15-20 = 5 pts; <15 = 0 pts.

- [ ] **PERF-EVAL-02**: Time-to-first-token (TTFT)
  - **Goal:** Median time from POST request to first content chunk on SSE-streaming response
  - **Behavior:** Fixed 200-token prompt, `stream: true`, 10 trials. awk-filter `^data: ` lines; record `date +%s%N` on first line containing non-empty `delta.content` (skip initial `delta.role` chunk and `: keepalive N/14` SSE comments).
  - **Validation:** `bench/runs/qwen35-eval-<ts>/ttft.json` contains 10 entries with `ttft_ms` numeric > 0.
  - **Threshold:** Median ≤500ms = 5 pts; 500-1500 = 3 pts; >1500 = 0 pts.

### Correctness

Anchor benchmarks (HumanEval+) plus multi-file + algorithm-level + cross-language fixtures — tests beyond v2.0's single-file off-by-one trivia.

- [ ] **CORR-EVAL-01**: HumanEval+ pass@1 (chat + completion modes)
  - **Goal:** Standard benchmark anchor — both chat-wrapped (matches blueCode's runtime) and raw-completion (compares to published Qwen 3.5 numbers)
  - **Behavior:** All 164 HumanEval+ problems × 2 modes. Mode A wraps in `{"role":"user","content":"Complete this Python function:\n\n<problem>"}`. Mode B uses `/v1/completions` raw text endpoint. `temperature=0.2` per mlx_llm_eval_guide.md §8. Code extraction (chat): parse first ` ```python ... ``` ` block; fallback to function-signature substring match.
  - **Validation:** `bench/runs/qwen35-eval-<ts>/humaneval_results.json` contains 164 entries; `evalplus.evaluate --dataset humaneval` produces pass@1 + pass@1+ numerics for both modes.
  - **Threshold:** Chat mode pass@1 ≥75% = 15 pts; 60-75% = 10 pts; 45-60% = 5 pts; <45% = 0 pts.

- [ ] **CORR-EVAL-02**: Multi-file F# refactoring fixture
  - **Goal:** Cross-file context coherence — does 122B preserve test behavior across module boundaries when renaming?
  - **Behavior:** New directory fixture `bench/fixtures/refactor_multifile/` with `Calculator.fs` + `Main.fs` (calls `Calculator.add`) + `Tests.fs` (tests `Calculator.add`) + `README.md` (task description). Agent prompt: "Read the files in `bench/fixtures/refactor_multifile/`, then rename `Calculator.add` to `Calculator.sum` across all files. Preserve test behavior."
  - **Validation:** All 3 files updated coherently; no orphan references to `add`; transcript captured at `bench/runs/qwen35-eval-<ts>/refactor_multifile_diff.txt`.
  - **Threshold:** All-or-nothing pass = 5 pts.

- [ ] **CORR-EVAL-03**: Algorithm-level F# bug-fix
  - **Goal:** Tests reasoning depth beyond W1/W2's trivial off-by-one — does 122B think about loop invariants?
  - **Behavior:** New fixture `bench/fixtures/bug_binsearch.fs` — off-by-one in binary search upper bound (`hi = mid` causes infinite loop on certain inputs). Agent prompt: "Read bench/fixtures/bug_binsearch.fs, identify the bug, give a specific input that triggers it."
  - **Validation:** Identifies the boundary condition correctly; names a triggering input. Transcript review (qualitative; documented in eval doc §2.2).
  - **Threshold:** Correct diagnosis = 3.75 pts (one of 4 F# bug-fix slots, 15 pts total).

- [ ] **CORR-EVAL-04**: Language coverage (Python + TypeScript)
  - **Goal:** Tests generalization beyond F#/.NET — is 122B F#-biased due to blueCode training prompts, or genuinely multilingual?
  - **Behavior:** New fixtures `bench/fixtures/bug_python_typeerror.py` (function returns inconsistent types None/int) and `bench/fixtures/bug_typescript_async.ts` (missing `await` in Promise.all chain). Same diagnose-then-fix prompt pattern as W1.
  - **Validation:** Correct diagnosis text per fixture (qualitative; transcripts captured for eval doc §2.4).
  - **Threshold:** Each correct = 2.5 pts (5 pts total).

### Reliability

JSON contract compliance + multi-turn stability + long-context — validates v2.0's "stable" claims with stress tests.

- [ ] **REL-EVAL-01**: JSON schema compliance rate
  - **Goal:** Stricter version of Phase 18-02's 31-invocation 0-failure result — does 122B reliably emit valid JSON over many turns?
  - **Behavior:** 50 single-turn invocations (T1+T6 mix from `bench/run.sh`). Count `InvalidJsonOutput` errors via `grep -c` on per-invocation logs.
  - **Validation:** `bench/runs/qwen35-eval-<ts>/schema_rate.txt` contains "X/50 InvalidJsonOutput" line.
  - **Threshold:** ≥49/50 = 10 pts; 47-49 = 7 pts; 45-47 = 3 pts; <45 = 0 pts.

- [ ] **REL-EVAL-02**: Multi-turn degradation curve
  - **Goal:** Validates ml-explore/mlx-lm#1011 5-round degradation claim empirically — does 4-bit MoE quantization actually break around turn 5?
  - **Behavior:** N=1, 3, 5, 7, 10 turns via `dotnet run --new-session "..."` then chained `--resume <id>` calls. 3 trials at N=1,3,5; 1 trial at N=7,10. Per-turn metrics: step count, elapsed, JSON validity, qualitative final-answer correctness via stored transcripts. Prompts in `bench/fixtures/multiturn_prompts.txt` (10 coding-relevant prompts: write function → add error handling → write tests → refactor tests → ...).
  - **Validation:** 5 directories `bench/runs/qwen35-eval-<ts>/multiturn_N{1,3,5,7,10}/`, each with N session JSONLs in `~/.bluecode/sessions/`.
  - **Threshold:** Stable through 7+ turns = 10 pts; 5+ turns = 5 pts; <5 turns degrades = 0 pts. **Hard fail** (ESCALATE verdict) if degradation observed before turn 5.

- [ ] **REL-EVAL-03**: Long-context needle-in-haystack
  - **Goal:** Tests attention + KV cache pressure at scales relevant for larger codebases
  - **Behavior:** Read `MaxModelLen` from `/v1/models` first (probe pattern from `QwenHttpClient.fs:probeModelInfoAsync`); cap test grid at actual ceiling. Inject `SECRET_KEY=abc123xyz` at random position in 8k/16k/32k contexts; ask model to retrieve. 1 trial per size.
  - **Validation:** `bench/runs/qwen35-eval-<ts>/needle.json` contains 4 size entries with retrieval correctness boolean + latency.
  - **Threshold:** Correct at 32k = 5 pts; degraded = 2 pts; failed = 0 pts.

### Documentation

Single comprehensive doc with numeric scorecard verdict — the deliverable that translates raw measurements into a daily-driver decision.

- [ ] **DOC-EVAL-01**: `documentation/qwen35-122b-coding-eval.md`
  - **Goal:** ~600-line eval doc; 10 sections; ends with `Total: NN/100, Recommendation: <KEEP|KEEP-WITH-CAVEATS|ESCALATE>` per scorecard rubric (Correctness 40 / Performance 25 / Reliability 25 / Coding-quality 10)
  - **Behavior:** Sections — §1 Methodology / §2 Correctness / §3 Performance / §4 Reliability / §5 Coding quality (qualitative transcript review of idiomatic F#, test generation, code review) / §6 Comparison anchors (vs published / vs Qwen 2.5 archive / cloud non-goal §6.3) / §7 Verdict scorecard / §8 Caveats / §9 Re-evaluation thresholds / §10 Reproduction instructions. Each section ends with verdict line.
  - **Validation:** File exists at expected path; ≥600 lines; all 10 sections present; final scorecard line matches expected format.
  - **Threshold:** All-or-nothing pass for milestone close.

## Out of Scope

v1 + v2.0 boundaries unchanged. v2.1 explicitly excludes:

| Feature | Reason |
|---------|--------|
| Cold-start measurement | Disruptive (kills 122B for ~3min via `launchctl kickstart`). Gated behind `--coldstart` flag for ad-hoc use; not in default `--full` per scope decision 2026-04-27. |
| Cloud comparison (Claude/GPT-4) | Requires API key + cost; user has muscle memory from daily use; preserves reproducibility without external dependencies. Documented in eval doc §6.3 as deliberate boundary. |
| Plan-mode bench fixture | Already deferred Phase 16; keystroke UX intractable for autonomous gate. Mocked-IKeyReader pattern is v2.2+ candidate. |
| Code review/critique scoring | Subjective; deferred. v2.1 captures transcripts in §5 but doesn't score critique quality. |
| Streaming inference for blueCode runtime | STM-01 deferred 7th cycle; eval measures TTFT but does NOT add streaming to blueCode itself. |
| Thinking-mode-on (`<think>` consumption) | Phase 20 deferred; requires `max_tokens` 1024→2048-4096 + re-bench. Eval keeps thinking-mode OFF (per launchd plist `--chat-template-args '{"enable_thinking": false}'`). |
| Native OpenAI `tool_calls` | Phase 20 deferred; rewrites `toLlmOutput` + all bench fixtures. Out of v2.1 scope. |
| New `tests/BlueCode.Tests/` modules | Eval is observational; no new F# tests. Test count stays 282/1/0. |
| `bench/baseline.json` modifications | Eval observational; gate baseline preserved byte-for-byte. |
| `src/` modifications | Eval is external instrumentation only; `git diff src/` empty post-eval. |
| Multi-platform (Windows/Linux) eval | Mac-only ethos preserved. |

## Future Requirements (v2.2+ candidates)

Tracked for awareness; not pulled into v2.1. Observation-driven scoping after v2.1 ships.

- **COMPACT-01** (v2.2 candidate) — Auto-compaction when session approaches 80% of `max_model_len` (PERSIST-02 follow-up)
- **SLASH-01** (v2.2 candidate) — `/sessions`, `/plan`, `/clear` slash commands inside REPL
- **SUBAG-01** (v2.2+ candidate) — Sub-agent delegation via Agent tool (now meaningful since memory + planning landed)
- **PLAN-MODE-BENCH-01** (v2.2+ candidate) — Plan-mode bench fixture via mocked-IKeyReader
- **THINK-01** (v2.2+ candidate) — Thinking-mode-on; `max_tokens` 1024→2048-4096
- **TOOLCALLS-01** (v2.2+ candidate) — Native OpenAI `tool_calls` replacing custom JSON schema
- **STM-01** (v2.2+ deferred 7x) — SSE token streaming in blueCode runtime
- **COLDSTART-01** (v2.2 candidate) — Empirical cold-start measurement (deferred from v2.1; needs scheduled disruption window)

## Traceability

Filled by roadmap. Each requirement maps to exactly one phase.

| Requirement | Phase | Status |
|-------------|-------|--------|
| PERF-EVAL-01 | Phase 21 (21-01) | Pending |
| PERF-EVAL-02 | Phase 21 (21-01) | Pending |
| CORR-EVAL-01 | Phase 21 (21-02) | Pending |
| CORR-EVAL-02 | Phase 21 (21-03) | Pending |
| CORR-EVAL-03 | Phase 21 (21-03) | Pending |
| CORR-EVAL-04 | Phase 21 (21-03) | Pending |
| REL-EVAL-01  | Phase 21 (21-04) | Pending |
| REL-EVAL-02  | Phase 21 (21-04) | Pending |
| REL-EVAL-03  | Phase 21 (21-04) | Pending |
| DOC-EVAL-01  | Phase 21 (21-05) | Pending |

**Coverage:**
- v2.1 requirements: 10 total (9 evaluation reqs + 1 doc deliverable)
- Mapped to phases: 10/10 ✓
- Unmapped: 0

---
*Requirements defined: 2026-04-27*
*Last updated: 2026-04-27 — initial draft from approved plan file (`/Users/ohama/.claude/plans/async-weaving-pnueli.md`); traceability pending roadmap creation*
