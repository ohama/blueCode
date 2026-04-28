# Benchmark: Qwen 3.5 (35B/122B) vs Qwen 2.5 (32B/72B) — Phase 17 candidate evaluation

**Phase:** 17 (Qwen 3.5 Evaluation)
**Run date:** 2026-04-27
**Run dir:** `bench/runs/20260427-085025/` (gitignored; data below is authoritative record)
**Build:** Debug (matches v1.4 baseline conventions)
**Services at run time:**
- port 8000: `mlx-community/Qwen3.5-35B-A3B-4bit` (35B MoE, ~3B activated/token)
- port 8001: `mlx-community/Qwen3.5-122B-A10B-4bit` (122B MoE, ~10B activated/token)

**Path A or Path B (thinking-mode mitigation):** Path A — `--chat-template-args '{"enable_thinking": false}'`
passed to `mlx_lm.server` in launchd plists for both 35B and 122B. `QwenHttpClient.fs` was not
modified (confirmed in Phase 17-02; carried forward to this run).

**AgentLoop prerequisite:** commit (Phase 17-02) changed mid-conversation hint injection
from `Role = System` to `Role = User`. Qwen 3.5 chat template rejects mid-conversation System
messages. Without this fix, T6 fails deterministically. This fix was in place before the `--all` run.

---

## §1 Methodology

All 34 invocations were run via `bench/run.sh --all` against the 35B/122B pair. The blueCode CLI
alias `--model 32b` routes to port 8000 and `--model 72b` routes to port 8001. These are semantic
labels for "small slot" and "large slot" — the Router DU cases `Qwen32B`/`Qwen72B` are intentional
stable names (research §Architectural Impact §Component analysis confirms; do NOT rename).

**Test suite composition:**

| Suite | Invocations | Labels |
|-------|-------------|--------|
| Regression | 14 | T1–T7 × 32b/72b |
| Variance | 12 | T1 × 32b (×3), T1 × 72b (×3), T6 × 32b (×3), T6 × 72b (×3) |
| Diagnose | 4 | B1/B2 × 32b/72b |
| Write | 4 | W1/W2 × 32b/72b |
| **Total** | **34** | all exit=0 |

**Step count extraction method:** `grep -cE "^thought:|action:" <logfile>` against each
`bench/runs/20260427-085025/*.log`. This counts thought+action pairs (each step contributes one of
each). Verified against manual log inspection; count matches `[Step N]` markers in log output.

**Elapsed extraction method:** `elapsed=Xs` field from each `.meta` file alongside the log.

---

## §2 v1.4 baseline (pre-Phase-17, source of truth: bench/baseline.json)

Baseline captured post-Phase-9.1-05 (prompt shrink + B2 recovery):

| Key | step_count | step_count_max | elapsed_median_s | Note |
|-----|-----------|---------------|-----------------|------|
| T6_32b | 4 | 5 | 22 | 4 steps typical: read_file × 3 + final |
| T6_72b | 4 | 5 | 45 | same pattern, 72B slower |
| W1_32b | 3 | 3 | 14 | loop-injection holds: read + write + final |
| W2_32b | 3 | 3 | 17 | same as W1 |
| T1_32b | 1 | 3 | 5 | canary; 1-step typical |
| T5_72b | 3 | 4 | 18 | glob_search + run_shell + final |
| B2_32b | 2 | 3 | n/a | diagnose-only; expected: empty list → DivideByZeroException |
| B2_72b | 2 | 3 | n/a | diagnose-only; same expected |

---

## §3 Phase 17 candidate run (35B/122B) — raw data

All 34 invocations: exit=0.

**Regression suite (T1–T7, both models):**

| Label | steps | elapsed_s |
|-------|-------|----------|
| regression_T1_32b | 1 | 5 |
| regression_T1_72b | 1 | 4 |
| regression_T2_32b | 1 | 3 |
| regression_T2_72b | 1 | 3 |
| regression_T3_32b | 2 | 4 |
| regression_T3_72b | 2 | 6 |
| regression_T4_32b | 2 | 4 |
| regression_T4_72b | 2 | 6 |
| regression_T5_32b | 3 | 4 |
| regression_T5_72b | 3 | 6 |
| regression_T6_32b | 4 | 7 |
| regression_T6_72b | 4 | 12 |
| regression_T7_32b | 2 | 6 |
| regression_T7_72b | 2 | 15 |

**Variance suite (T1 × 3 + T6 × 3, both models):**

| Label | steps | elapsed_s |
|-------|-------|----------|
| variance_T1_32b_run1 | 1 | 3 |
| variance_T1_32b_run2 | 1 | 2 |
| variance_T1_32b_run3 | 1 | 2 |
| variance_T1_72b_run1 | 1 | 4 |
| variance_T1_72b_run2 | 1 | 3 |
| variance_T1_72b_run3 | 1 | 3 |
| variance_T6_32b_run1 | 4 | 7 |
| variance_T6_32b_run2 | 4 | 6 |
| variance_T6_32b_run3 | 4 | 6 |
| variance_T6_72b_run1 | 4 | 13 |
| variance_T6_72b_run2 | 4 | 10 |
| variance_T6_72b_run3 | 4 | 11 |

T6_32b elapsed: range 6–7s, median **6s**. T6_72b elapsed: range 10–13s, median **11s**.
T1_32b elapsed: range 2–3s, median **2s**. Step counts: perfectly deterministic (1 for T1, 4 for T6).

**Diagnose suite (B1/B2, both models):**

| Label | steps | elapsed_s |
|-------|-------|----------|
| diagnose_B1_32b | 2 | 5 |
| diagnose_B1_72b | 2 | 9 |
| diagnose_B2_32b | 2 | 4 |
| diagnose_B2_72b | 2 | 7 |

**Write suite (W1/W2, both models):**

| Label | steps | elapsed_s |
|-------|-------|----------|
| write_W1_32b | 3 | 5 |
| write_W1_72b | 3 | 9 |
| write_W2_32b | 3 | 6 |
| write_W2_72b | 3 | 9 |

**Post-bench RSS:**

| Model | RSS (KB) | RSS (GB) |
|-------|---------|---------|
| 35B (port 8000) | 17,749,552 | 16.9 GB |
| 122B (port 8001) | 47,615,664 | 45.4 GB |
| Combined | — | **62.4 GB** |

System: `PhysMem: 126G used (3531M wired, 541M compressor), 1584M unused`.

---

## §4 Per-test comparison (Phase 17 vs v1.4 baseline)

Verdict legend:
- **improve** — elapsed < baseline by ≥ 20% AND step count ≤ step_count_max
- **pass** — step count within step_count_max AND elapsed within ±50% of baseline
- **regress** — step count > step_count_max OR elapsed > 2× baseline
- **n/a** — diagnose tests; verdict is qualitative (see §5.1)

| Test | Baseline steps | New steps | Δ steps | Baseline elapsed (s) | New elapsed (s) | Δ elapsed (s) | Δ % | Verdict |
|------|--------------|----------|---------|--------------------|--------------|--------------|----|---------|
| T1_32b | 1 | 1 | 0 | 5 | 2 (median) | -3 | -60% | **improve** |
| T5_72b | 3 | 3 | 0 | 18 | 6 | -12 | -67% | **improve** |
| T6_32b | 4 | 4 | 0 | 22 | 6 (median) | -16 | -73% | **improve** |
| T6_72b | 4 | 4 | 0 | 45 | 11 (median) | -34 | -76% | **improve** |
| W1_32b | 3 | 3 | 0 | 14 | 5 | -9 | -64% | **improve** |
| W2_32b | 3 | 3 | 0 | 17 | 6 | -11 | -65% | **improve** |
| B2_32b | 2 | 2 | 0 | n/a | 4 | n/a | n/a | **n/a** (see §5.1) |
| B2_72b | 2 | 2 | 0 | n/a | 7 | n/a | n/a | **n/a** (see §5.1) |

**Tests outside baseline (no v1.4 entry — informational):**

| Test | New steps | New elapsed (s) | Notes |
|------|----------|----------------|-------|
| T2_32b | 1 | 3 | single-step, fast |
| T2_72b | 1 | 3 | same |
| T3_32b | 2 | 4 | 2-step tool use |
| T3_72b | 2 | 6 | same |
| T4_32b | 2 | 4 | 2-step tool use |
| T4_72b | 2 | 6 | same |
| T7_32b | 2 | 6 | 2-step tool use |
| T7_72b | 2 | 15 | T7_72b is the slowest single test |
| W1_72b | 3 | 9 | 3-step convergence (no baseline, but correct pattern) |
| W2_72b | 3 | 9 | same |
| B1_32b | 2 | 5 | diagnose-only |
| B1_72b | 2 | 9 | diagnose-only |

**Summary speedup across baseline-anchored tests:**

| Test | Speedup |
|------|---------|
| T6_32b | 3.7× (22s → 6s) |
| T6_72b | 4.1× (45s → 11s) |
| W1_32b | 2.8× (14s → 5s) |
| W2_32b | 2.8× (17s → 6s) |
| T5_72b | 3.0× (18s → 6s) |
| T1_32b | 2.5× (5s → 2s) |

Weighted sum of baselines: 5+18+22+45+14+17 = **121s** (excluding B2 n/a entries).
Weighted sum of new: 2+6+6+11+5+6 = **36s**.
Overall speedup: **3.4× across measurable gate tests**.

---

## §5 Qualitative findings

### 5.1 B2 diagnose accuracy (32B vs 35B; 72B vs 122B)

The B2 fixture (`bench/fixtures/bug_divide_zero.fs`) requires the agent to identify the divide-by-zero
bug triggered by an empty list. The v1.4 baseline expected diagnosis: "empty list causes
DivideByZeroException".

**35B actual diagnosis (verbatim from B2_32b.log step 2 thought):**
> "The bug is in the 'average' function which performs integer division by List.length xs without
> checking if the list is empty. When an empty list is passed, List.length returns 0, causing a
> DivideByZeroException at runtime."

**35B final answer:** "The bug is in the 'average' function which performs integer division by
List.length xs without checking if the list is empty. When an empty list is passed, List.length
returns 0, causing a DivideByZeroException at runtime. The specific input that triggers this bug
is an empty list, e.g., average []"

**122B actual diagnosis (verbatim from B2_72b.log step 2 thought):**
> "The file contains a function `average` that computes the integer mean of a list. The bug is a
> division by zero when the input list is empty, because `List.length []` returns 0, and dividing
> by zero raises a `DivideByZeroException`."

**122B final answer:** "The bug is in the `average` function at line 7: `List.sum xs / List.length xs`.
When called with an empty list (e.g., `average []`), `List.length xs` returns 0, causing a division
by zero at runtime. This triggers a `DivideByZeroException`."

**Diagnosis verdict:** Both 35B and 122B correctly identify the exact bug (empty list → division by
zero → DivideByZeroException). 35B: **CORRECT**. 122B: **CORRECT**. No degradation from v1.4.

### 5.2 T6 dispatcher pattern (32B/72B and 35B/122B)

T6 fixture: "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"

Expected pattern: read_file (may need multiple reads or grep + targeted read) → final. Baseline: 4 steps.

**35B observed (T6_32b.log):**
1. thought: read Domain.fs → action: read_file (2114 chars, truncated)
2. thought: file truncated, need grep → action: grep_search for "type Step"
3. thought: found at line 165 → action: read_file (start_line=165, end_line=180)
4. thought: fields found → action: final

Sequence: read_file → grep_search → read_file (targeted) → final. 4 steps. Correct answer given.

**122B observed (T6_72b.log):**
1. thought: locate Step record → action: grep_search "type Step = \{"
2. thought: empty result, broaden → action: grep_search "Step"
3. thought: found at line 165 → action: read_file (start_line=165, end_line=175)
4. thought: fields found → action: final

Sequence: grep_search (narrow) → grep_search (broad) → read_file (targeted) → final. 4 steps.
Correct answer given.

Both models: **4 steps, within step_count_max=5, correct answer**. 35B slightly more efficient
(read first, then grep vs 122B's double-grep). The AgentLoop User role fix was the
precondition — without it, 35B fails T6 deterministically (mid-conversation POST-READ HINT was
injected as System role, rejected by Qwen 3.5 chat template with HTTP 404).

### 5.3 W1/W2 write-task convergence (32B vs 35B)

W1 fixture: fix off-by-one bug in `bench/fixtures/bug_lastchar.fs`, save with write_file.
W2 fixture: add `averageSafe` function to `bench/fixtures/bug_average.fs`, save.

The loop-injection primitive forces convergence: once `write_file` succeeds, the next step gets
a `[POST-EDIT CONSTRAINT]` hint that closes the task.

**35B W1 observed:** read_file → write_file → final. 3 steps. Correct fix applied.
**35B W2 observed:** read_file → write_file → final. 3 steps. Correct `averageSafe` function added.

Both W1 and W2: **3 steps, exactly at step_count_max=3, loop-injection operating correctly**.
No regression from v1.4 (which also observed exactly 3 steps).

### 5.4 Multi-turn / tool-call degradation watch

Known risk from ml-explore/mlx-lm#1011: structured JSON degradation at ~5 multi-turn rounds in
4-bit MoE models. blueCode bench fixtures stay within the safe zone: T6=4 steps max, W1/W2=3 steps.

Observation across all 34 invocations: **zero tests exceeded their step_count_max**.
- T6 (max=5): observed 4 steps consistently across both models and all 6 variance runs.
- W1/W2 (max=3): observed exactly 3 steps in all 4 write invocations.
- All other tests: well within bounds.

No multi-turn degradation signal observed.

### 5.5 Thinking-mode artifacts in JSON output

Path A mitigation: `--chat-template-args '{"enable_thinking": false}'` passed to mlx_lm.server.

```
grep -rl "<think>" bench/runs/20260427-085025/*.log | wc -l
```

Result: **0** files contain `<think>` tokens. Zero thinking-mode leakage across all 34 invocations.
Path A is functioning correctly on both models (mlx_lm 0.31.3, confirmed in Phase 17-02).

---

## §6 Decision matrix (KEEP / SWITCH / SHIP-BOTH)

Three criteria, each with explicit thresholds. The verdict is the mechanical conjunction.

### 6.1 Correctness (mandatory)

A pair is correctness-acceptable iff ALL of the following hold:

1. All 8 gate tests (T6_32b, T6_72b, W1_32b, W2_32b, T1_32b, T5_72b, B2_32b, B2_72b) pass
   `bench/run.sh --gate` exit 0 (or would, after re-keying for 35B/122B).
2. B2 diagnose accuracy: both 35B and 122B correctly identify "empty list causes
   DivideByZeroException" (or semantically equivalent).
3. Zero `<think>` token leakage in any `--all` invocation log (§5.5).
4. Zero multi-turn degradation regressions (§5.4): no test exceeded its step_count_max.

If any of the above fails for the 35B/122B candidate → KEEP verdict regardless of latency.

**Evaluation:**
1. All 8 gate labels ran with exit=0, step counts within step_count_max. ✓
2. B2_32b: CORRECT. B2_72b: CORRECT. ✓
3. Zero `<think>` leakage. ✓
4. Zero step_count_max violations. ✓

**Correctness verdict: PASS**

### 6.2 Latency (advisory; tiebreaker if correctness equal)

A pair is latency-improved iff:
- Median elapsed across measurable gate tests improves by ≥ 20%.
- No single test regresses by > 100% (i.e., takes >2× longer than baseline).

**Evaluation:**

Sum of baseline elapsed (6 measurable gate tests): 5+18+22+45+14+17 = 121s
Sum of new elapsed: 2+6+6+11+5+6 = 36s
Ratio: 36/121 = **0.30** (i.e., 70% improvement, far exceeding the ≥20% threshold).

Worst per-test regression: none. Every measurable test improved.
- Best improvement: T6_72b (76% faster, 4.1×).
- Smallest improvement: T1_32b (60% faster, 2.5×) — still well above the 20% threshold.

**Latency verdict: improved** (no regression, 3.4× overall speedup)

### 6.3 Memory budget (advisory)

Threshold: combined RSS must not exceed 95 GB post-bench (the 89.5 GB projection + 6% tolerance
from research §Memory Budget). Exceeding 95 GB indicates compressor pressure and OOM risk.

**Evaluation:**

Post-bench RSS: 35B = 16.9 GB, 122B = 45.4 GB, **combined = 62.4 GB**.
Research projection (worst-case): ~84–93 GB post bench-all.

The RSS held flat at smoke-level (smoke: 16.9 + 45.4 = 62.3 GB; bench-all: 16.9 + 45.4 = 62.4 GB,
essentially unchanged). MoE routing converged on a stable expert subset across bench-all prompts —
the diverse prompt set did NOT drive RSS toward disk size as the projection had anticipated.
This is a new empirical finding; §5.5.1 of qwen35-install.md will be updated.

System compressor: 541 MB at bench completion (well below the 1 GB concern threshold).

**Memory verdict: acceptable** (62.4 GB combined vs 95 GB threshold; 32.6 GB headroom)

### 6.4 SHIP-BOTH (out of scope for this phase)

SHIP-BOTH (per-task routing based on task complexity) would require plumbing in `Router.fs` to
route by task type rather than model size — a non-trivial code change. It also requires running both
new and old pairs simultaneously, which exceeds memory budget.

Data observation: 35B and 122B are both fast and correct. There is no strong per-task split benefit
visible in this run — both models converge at the same step count on all tests. The MoE architecture
makes 122B comparable in speed to 35B on simple tasks (T1: 4s vs 2s), so routing complexity for
speed would have diminishing returns.

**SHIP-BOTH is OUT OF SCOPE for Phase 17.** If a future phase reveals a meaningful accuracy gap
on harder reasoning tasks, SHIP-BOTH should be re-evaluated as a v2.1+ roadmap candidate.
The user can re-open via a future phase.

### 6.5 Decision rule

| Correctness | Latency | Memory | Verdict |
|-------------|---------|--------|---------|
| FAIL | — | — | KEEP |
| PASS | improved (≥20%) | acceptable (combined RSS ≤ 95 GB) | **SWITCH** |
| PASS | improved (≥20%) | risky (RSS > 95 GB) | KEEP (memory risk) |
| PASS | neutral / regressed | — | KEEP |
| PASS, per-task split benefit | — | — | KEEP + record SHIP-BOTH as v2.1 candidate |

---

## §7 Verdict

Applying the §6.5 decision rule to the §4-§5 data:

- **Correctness:** PASS. B2 diagnose accuracy preserved on both models (§5.1); zero `<think>` leaks
  (§5.5); no multi-turn degradation (§5.4); all 8 gate tests ran exit=0 with step counts within
  step_count_max.
- **Latency:** improved. Sum of new elapsed / sum of baseline elapsed = 0.30 (3.4× aggregate
  speedup). Per-test worst regression: none. Best single improvement: T6_72b at 4.1×.
- **Memory:** acceptable. Combined post-bench RSS = 62.4 GB vs 95 GB threshold; 32.6 GB headroom.
  Compressor stable at 541 MB.

**VERDICT: SWITCH**

(SHIP-BOTH observation: data does not suggest a meaningful per-task split benefit — both models
converge at the same step counts. Deferred to v2.1+ per §6.4.)

### 7.1 Rationale

The 35B/122B MoE pair is 3.4× faster than the 32B/72B pair across all measurable gate tests, with
zero correctness regression, zero thinking-mode leakage, and a combined RSS of 62.4 GB — well within
the 128 GB unified memory budget. The memory footprint is notably better than the 89.5 GB projection,
because MoE sparse routing converges on a stable expert subset for blueCode's bench fixture pattern,
keeping RSS flat at the smoke-level value. The single blocking precondition (AgentLoop User role
fix for mid-conversation hints, commit) was applied in Phase 17-02 and confirmed effective
across all 34 invocations.

### 7.2 Follow-up actions

**VERDICT == SWITCH:**
- Task 4 will re-key `bench/baseline.json` (T6_32b → T6_35b, T6_72b → T6_122b, etc.) with
  new step_count + elapsed_median_s + actual_diagnosis values from this run.
- Task 4 will update `CLAUDE.md` `## Runtime Environment` section with new model names, paths,
  and the thinking-mode mitigation note.
- Task 4 will append a footer note to `documentation/qwen35-install.md` recording SWITCH decision.
- Task 4 will update §5.5.1 RSS table in `documentation/qwen35-install.md` with the post-bench
  flat-RSS finding.
- Task 5 will run `bench/run.sh --gate` against the re-keyed baseline; expects exit 0.

---

## §8 Final gate verification

`bench/run.sh --gate` exit code: **0**

Gate PASS. The canonical pair (35B/122B, re-keyed baseline) is shippable. All 8 regression tests
ran within their step_count_max thresholds; B2 diagnose accuracy preserved (both 35B and 122B
correctly identified the DivideByZeroException; regression status = expected-pass, confirmed).

Gate baseline keys used: T6_35b, T6_122b, W1_35b, W2_35b, T1_35b, T5_122b, B2_35b, B2_122b
(re-keyed from v1.4 T6_32b etc. per SWITCH decision).

---

## §9 Next steps

Phase 17 verdict: **SWITCH**.

- 35B/122B is now canonical. Old 32B/72B services (unloaded since Phase 17-02) and model files
  (at `~/llm-system/models/qwen{32b,72b}/`) are preserved on disk for rollback. Recommend keeping
  them for ≥1 week of stable 35B/122B operation before any cleanup.

- Phase 16 plans on disk reference `_32b`/`_72b` keys in bench fixture deltas (e.g., `T6_32b`,
  `MT_32b`). When Phase 16-03 executes, those references need a mechanical re-key to `_35b`/`_122b`.
  The structure of Phase 16 plans is otherwise unaffected by the model swap.

- v2.1 candidates:
  - Cleanup of old plists + model files after stable operation period.
  - SHIP-BOTH per-task routing (see §6.4 — data did not yet suggest a strong split benefit, but
    revisit if harder reasoning fixtures are added to the bench suite).
  - Timeout increase to 300s for 122B cold-start (currently 180s; documented in 17-01-SUMMARY.md
    as deferred).

- The blueCode `--model 32b` / `--model 72b` CLI aliases and Router DU case names (`Qwen32B`,
  `Qwen72B`) remain unchanged. These are stable semantic labels (small slot / large slot) and are
  explicitly NOT renamed per plan verification constraints.
