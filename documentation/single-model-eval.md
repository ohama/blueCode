# Single-Model 122B Evaluation (Phase 18)

**Phase:** 18 (Single-Model 122B Evaluation)
**Date:** 2026-04-27
**Decision:** **DROP-35B**
**Status:** Phase 18 shipped — evaluation complete; architectural follow-ups deferred per ROADMAP §SC5.

---

## Overview

Phase 17 (Qwen 3.5 Evaluation, complete 2026-04-27) made Qwen 3.5 35B/122B the canonical model
pair, replacing 32B/72B. Phase 18 asks the next question: can 35B be dropped, leaving 122B alone
to serve all blueCode invocations? If yes, the dual-model `Router` collapses to dead code,
`bench/baseline.json` halves, ~17 GB of RSS frees up, and operational surface reduces to one
launchd plist.

Phase 18 is data-gathering + decision: **NO permanent code changes are made in this phase.** The
architectural changes (Router collapse, baseline halve, CLAUDE.md update, bench script promotion)
are deferred to a follow-up phase, gated on the verdict here.

The decision is mechanical: 5 criteria from ROADMAP §SC4, each with an explicit threshold. The
verdict is the conjunction.

## Methodology

Three evaluation steps:

1. **18-01 (memory profile, checkpoint):** User unloaded 35B via `launchctl unload`. Memory snapshots
   captured before and after (PhysMem used/unused, Compressor, 35B + 122B RSS). 122B health verified
   via two smoke tests post-unload (thinking-mode disabled smoke; real blueCode `--model 72b` invocation).
   Output: `.planning/phases/18-single-model-eval/18-01-MEMORY-PROFILE.md`.

2. **18-02 (122B-only bench):** Created `scripts/bench-122b-only.sh` (mirrors `bench/run.sh` but uses
   `--model 72b` everywhere, routing to port 8001 = 122B). Ran `--all` mode (31 invocations across
   regression/variance/diagnose/write/canary/b2). Per-test elapsed + step counts + B2 diagnosis quote
   captured. Output: `.planning/phases/18-single-model-eval/18-02-BENCH-RESULTS.md`.

3. **18-03 (this doc):** Decision matrix + verdict + reversibility procedure + deferred follow-ups.

The Phase 17 dual-loaded baseline (35B + 122B both running, ports 8000 + 8001 active) is the
comparison anchor. Phase 17 numbers are sourced from `documentation/benchmark-qwen35-eval.md`
(the Phase 17 eval doc) and `bench/baseline.json` (the regression gate's 8-entry baseline).

## 18-01 Memory profile (summary)

Lifted from `18-01-MEMORY-PROFILE.md` §5.1 + §5.2:

| ROADMAP §SC4 criterion          | Threshold | Observed                | Verdict |
|--------------------------------|-----------|-------------------------|---------|
| PhysMem unused increase        | ≥ 5 GB    | +19.42 GB               | PASS    |
| Compressor (post-unload)       | < 1 GB    | 454 MB                  | PASS    |

122B RSS hypothesis (RESEARCH §Pitfall 5): CONFIRMED — RSS stable.
Phase 17 dual-loaded steady-state was 62.35 GB combined; post-unload single-model 122B RSS = 45.42 GB.

122B health post-unload: thinking-mode smoke PASS (1s, no `<think>` tokens), JSON-schema smoke PASS
(7s, exit 0, clean single-step FinalAnswer).

Pre-unload vs post-unload detail:

| Metric             | Pre-unload       | Post-unload (+30s) | Delta        |
|--------------------|------------------|--------------------|--------------|
| PhysMem used       | 126 GB           | 106 GB             | -20 GB       |
| PhysMem unused     | 1.58 GB (1618 MB)| 21 GB              | +19.42 GB    |
| Compressor         | 463 MB           | 454 MB             | -9 MB        |
| 35B RSS            | 16.93 GB         | (process gone)     | -16.93 GB    |
| 122B RSS           | 45.42 GB         | 45.42 GB           | 0 GB         |

## 18-02 Bench results (summary)

Lifted from `18-02-BENCH-RESULTS.md` §3 + §4 + §6:

Bench wall-clock: 252s (~4 min). Total invocations: 31. Non-zero exits: 0.

| ROADMAP §SC4 criterion              | Threshold        | Observed              | Verdict |
|------------------------------------|------------------|-----------------------|---------|
| T1 median elapsed (variance, 3 runs) | ≤ 6s           | 3s                    | PASS    |
| T2 median elapsed (variance, 3 runs) | ≤ 6s           | 3s                    | PASS    |
| T6_122b step count                 | ≤ 5 (baseline_max)| 4 (all 6 runs = 4)   | PASS    |
| W1_122b step count                 | ≤ 3 (baseline_max)| 3                    | PASS    |
| W2_122b step count                 | ≤ 3 (baseline_max)| 3                    | PASS    |
| B2_122b step count                 | ≤ 3 (baseline_max)| 2                    | PASS    |
| B2 actual_diagnosis preserved      | DivByZero on empty list | 3 grep matches | PASS    |

B2 diagnosis quote (verbatim from `bench/runs/122b-only-20260427-131515/diagnose_B2_122b.log`):

> The bug is a division by zero when the input list is empty, because `List.length []` returns 0,
> and dividing by zero raises a `DivideByZeroException`. The specific input that triggers this
> bug is an empty list, e.g., `average []`.

Phase 17 baseline `actual_diagnosis` (B2_122b from `bench/baseline.json`):

> empty list causes DivideByZeroException — 'The bug is a division by zero when the input list is
> empty, because List.length [] returns 0, and dividing by zero raises a DivideByZeroException.'

Semantic equivalence: YES — preserved. Both identify the same root cause (empty list →
`List.length xs = 0` → `DivideByZeroException`) with nearly identical wording.

Post-bench 122B RSS: 45.43 GB (Δ vs pre-bench 45.42 GB: +0.01 GB / +1.4 MB — negligible).
Phase 17 finding that RSS holds flat at steady-state: CONFIRMED in bench-mode operation.

## Per-test comparison (Phase 18 single-model 122B vs Phase 17 dual-loaded)

Note on measurement context: Phase 17 elapsed figures are from `bench/run.sh --all` sequential runs
(one `dotnet run` process per invocation with warm JIT reuse). Phase 18 elapsed figures are from
`scripts/bench-122b-only.sh --all` under identical methodology. Both represent warm sequential bench.
The Phase 17 `bench/baseline.json` `elapsed_median_s` values for T1_122b (11s) reflect a separate
cold-start measurement and are NOT directly comparable to this table.

For step-count comparison, Phase 17 baseline values come from `bench/baseline.json`. For elapsed
comparison on tests previously routed to 35B (W1/W2), the Phase 17 dual-loaded elapsed is used.

| Test           | Phase 17 elapsed (s) | Phase 18 elapsed (s) | Δ (s) | Δ %   | Ph17 steps | Ph18 steps | Δ steps |
|----------------|---------------------|---------------------|-------|-------|------------|------------|---------|
| T1_122b        | 4 (regression run)  | 4 (regression run)  | 0     | 0%    | 1          | 1          | 0       |
| T2_122b        | 3                   | 3                   | 0     | 0%    | 1          | 1          | 0       |
| T5_122b        | 6                   | 5                   | -1    | -17%  | 3          | 3          | 0       |
| T6_122b        | 11 (median)         | 11 (median)         | 0     | 0%    | 4          | 4          | 0       |
| T7_122b        | 15                  | 15 (median)         | 0     | 0%    | 2          | 2          | 0       |
| W1 (35b→122b)  | 5 (W1_35b, Ph17)    | 8                   | +3    | +60%  | 3          | 3          | 0       |
| W2 (35b→122b)  | 6 (W2_35b, Ph17)    | 9                   | +3    | +50%  | 3          | 3          | 0       |
| B2_122b        | 7 (diagnose run)    | 7                   | 0     | 0%    | 2          | 2          | 0       |

Interpretation:
- Tests previously on 122B (T1/T2/T5/T6/T7/B2): step counts and elapsed are essentially unchanged.
  Single-model operation does not degrade 122B's performance.
- Tests previously routed to 35B (W1/W2): now served by 122B alone. Elapsed increased by ~3s
  (+50-60%) but **step counts are IDENTICAL** (still exactly 3). The loop-injection constraint
  (read+write+final) holds single-model. Latency is higher but correctness is preserved.
- W1/W2 elapsed increase (+3s) is expected: 122B is slower than 35B for simple write tasks, but
  the 8-9s absolute figures remain well within a comfortable UX envelope (no user-visible stall).
- Step count drift (Δ steps): **zero across all tests** — confirms single-model has no quality regression.

## Decision matrix

The verdict applies the 5 ROADMAP §SC4 criteria. Each is PASS or FAIL.

| # | Criterion                              | Threshold               | Observed (from §18-01 + §18-02)           | Verdict |
|---|----------------------------------------|-------------------------|-------------------------------------------|---------|
| 1 | T1/T2 median elapsed                   | ≤ 6s                    | T1=3s, T2=3s                              | PASS    |
| 2 | T6/W1/W2/B2 step counts                | ≤ Phase 17 baseline_max | T6=4/5, W1=3/3, W2=3/3, B2=2/3           | PASS    |
| 3 | B2 actual_diagnosis preserved          | "DivideByZeroException" semantic equivalent | 3 grep matches; wording near-identical | PASS    |
| 4 | PhysMem unused increase post-unload    | ≥ 5 GB                  | +19.42 GB                                 | PASS    |
| 5 | Compressor post-unload                 | < 1 GB                  | 454 MB                                    | PASS    |

### Decision rule

- **5/5 PASS → DROP-35B**: 122B alone is fully viable; architectural follow-ups (Router collapse, baseline halve) are warranted.
- **2+ FAIL → KEEP-DUAL**: Dual model is correct; 35B is reloaded; no architectural changes; eval recorded as "evaluated, deferred".
- **Exactly 1 FAIL with mitigation → CONDITIONAL**: 122B-only viable for some workloads but with caveat; opt-in mechanism sketched in §Follow-ups; default stays dual until follow-up implements opt-in.
- **Exactly 1 FAIL without mitigation → KEEP-DUAL** (conservative).

## VERDICT: DROP-35B

PASS count: 5/5. FAIL count: 0/5.

All 5 ROADMAP §SC4 criteria PASS. 122B alone meets latency, step-count, diagnosis, memory-headroom,
and compressor thresholds. The +3s elapsed increase on W1/W2 (write tasks formerly routed to 35B)
is within a comfortable UX envelope and — critically — does not cause step-count regression (both
W1/W2 remain at exactly 3 steps). Single-model 122B is a viable canonical configuration. A follow-up
phase to execute the architectural changes (Router collapse, baseline halve, CLAUDE.md update) is
recommended.

## Reversibility — 35B reload procedure

Phase 18 made ZERO permanent code changes. The system can be returned to Phase 17 canonical state
(both 35B and 122B loaded) by reloading the 35B service.

Reload command (per `documentation/qwen35-install.md §5.1.1`):

```bash
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen35b.plist
until curl -fsS http://127.0.0.1:8000/v1/models > /dev/null 2>&1; do sleep 3; done
echo "35B reloaded"
launchctl list | grep ohama   # should show BOTH com.ohama.qwen35b and com.ohama.qwen122b
curl -fsS http://127.0.0.1:8000/v1/models > /dev/null && echo "8000 OK"
curl -fsS http://127.0.0.1:8001/v1/models > /dev/null && echo "8001 OK"
```

If reload fails with `Load failed: 5: Input/output error` (RESEARCH §Pitfall 2), use bootout/bootstrap:

```bash
launchctl bootout gui/$(id -u) ~/Library/LaunchAgents/com.ohama.qwen35b.plist
launchctl bootstrap gui/$(id -u) ~/Library/LaunchAgents/com.ohama.qwen35b.plist
```

### Reload disposition by verdict

- **VERDICT = KEEP-DUAL:** Reload is **MANDATORY** for reversibility. The system must be returned
  to Phase 17 canonical state (both services loaded). Performed via Task 3 user checkpoint
  (`launchctl load -w` requires user-initiated invocation per 18-RESEARCH.md — Claude cannot run
  launchctl autonomously on this Mac).

- **VERDICT = DROP-35B:** Reload is **RECOMMENDED** for the Phase 17 reversibility window (≥ 1 week
  of stable single-model operation before any cleanup of old plists / model files). Task 3 checkpoint
  is SKIPPED in this branch — system stays single-model. The user can reload at will via the command
  above; the follow-up architectural-changes phase decides whether to formally unload 35B.

- **VERDICT = CONDITIONAL:** Reload is **MANDATORY**. Performed via Task 3 user checkpoint. Until
  the follow-up phase implements the opt-in mechanism (env var or CLI flag), default behavior must
  remain dual-model.

### Reload performed in this plan?

- Verdict: DROP-35B
- Reload disposition: OPTIONAL (Task 3 skipped — system stays single-model)
- Reload outcome: SKIPPED — DROP-35B verdict, system stays single-model (122B alone, port 8001)
- Verification command outputs (if reload performed): N/A — Task 3 skipped per DROP-35B branch

### Task 3 outcome: SKIPPED (verdict = DROP-35B)

Reload was OPTIONAL per the verdict disposition. Task 3 checkpoint short-circuited.
System stays single-model (122B alone, port 8001). User can reload via:

```bash
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen35b.plist
```

Reversibility window: ≥ 1 week of stable single-model operation recommended before any
cleanup of 35B model files / plist.

## Conditional follow-ups (DEFERRED — not executed in 18-03)

Per ROADMAP §SC5, the architectural changes are gated on the verdict and DEFERRED to a follow-up
phase. They are NOT executed in 18-03. Enumerated here for the follow-up planner:

1. **Router collapse:** `src/BlueCode.Core/Router.fs` `modelToEndpoint` currently maps both `Qwen32B`
   and `Qwen72B` (semantic small/large labels) to ports 8000 and 8001. If DROP-35B, the small
   route becomes dead code or both routes collapse to port 8001. Decision belongs to the follow-up
   phase: rename DU cases? Delete one? Keep both for symmetry? Files: `src/BlueCode.Core/Router.fs`,
   possibly `src/BlueCode.Cli/CompositionRoot.fs`.

2. **bench/baseline.json halve:** Currently 8 entries (`T6_35b`, `T6_122b`, `W1_35b`, `W2_35b`,
   `T1_35b`, `T5_122b`, `B2_35b`, `B2_122b`). If DROP-35B, the `_35b` keys become unreachable; the
   gate would always fail with connection-refused on port 8000. The follow-up phase either re-keys
   all entries to `_122b` (single-model gate) or removes the small-slot entries entirely.
   Files: `bench/baseline.json`, `bench/run.sh`.

3. **CLAUDE.md `## Runtime Environment`:** Currently names BOTH 35B and 122B with their plist files.
   If DROP-35B, the small-slot description is removed. If CONDITIONAL, an opt-in note is added.
   Files: `CLAUDE.md`.

4. **scripts/bench-122b-only.sh disposition:** If DROP-35B, this script either replaces `bench/run.sh`
   or both scripts coexist (one for single-model, one for dual). The follow-up phase decides.
   Files: `scripts/bench-122b-only.sh`, `bench/run.sh`, `documentation/bench.md`.

5. **Phase 16 implications:** Phase 16 plans on disk (16-01..16-03) reference `_35b/_122b` bench
   keys. If DROP-35B and follow-up halves baseline.json, Phase 16-03 needs another mechanical
   re-key (similar to the 17-03 SWITCH re-key). Document this in the follow-up phase plan.
   Files: `.planning/phases/16-planning-wiring-bench/*.md`, `bench/baseline.json`.

The follow-up phase is OUT OF SCOPE for v2.0 unless verdict = DROP-35B. Since verdict IS DROP-35B,
the follow-up phase is **recommended** and should run BEFORE Phase 16 (same reasoning as Phase 17 ran
before Phase 16: bench fixtures should reflect the canonical model before Phase 16 baselined them).

## Phase 18 disposition

Phase 18 SC1: PASS — eval doc ≥ 150 lines, contains "Decision"
Phase 18 SC2: PASS — 35B unloaded via launchctl, memory snapshot captured (18-01 §1+§3)
Phase 18 SC3: PASS — bench-122b-only.sh ran 31 invocations (≥ 30 required), all routed to 122B (18-02 §1)
Phase 18 SC4: PASS — 5 criteria evaluated; verdict named (above) — all 5 PASS → DROP-35B
Phase 18 SC5: PASS — architectural follow-ups enumerated as deferred (above); no code changes in this plan

Phase 18 is shippable.

## §7 Phase 19 Execution (follow-up to SC5 deferred work)

The architectural follow-ups enumerated in §SC5 above were executed in Phase 19
(planned and executed 2026-04-27):

- **Router collapse** — `parseForcedModel None → Some Qwen122B` (explicit default, no
  intent routing indirection in single-model mode). Router.intentToModel retained structurally
  for future SHIP-BOTH evolution but dormant by default. See `19-02-PLAN.md` Task 3.
- **baseline.json halve** — `bench/baseline.json` reduced from 8 entries (_35b/_32b/_72b)
  to 6 entries (all _122b: T6/W1/W2/T1/T5/B2). See `19-02-PLAN.md` Task 6.
- **CLAUDE.md update** — §Runtime Environment reframed to 122B-only canonical + dual-mode
  reactivation procedure. See `19-02-PLAN.md` Task 8.
- **scripts/bench-122b-only.sh promotion** — absorbed into `bench/run.sh` in-place.
  See `19-02-PLAN.md` Task 5.
- **Retirement guard** — `parseForcedModel` now rejects `--model 32b`/`--model 72b` with
  exit 2 + Phase 19 reference. `validateModelPath` adds a probe-layer PathRetired guard.
  See `19-02-PLAN.md` Tasks 1–3.

Execution artifact: `.planning/phases/19-qwen25-retirement/19-02-PLAN.md`
Summary: `.planning/phases/19-qwen25-retirement/19-02-SUMMARY.md`
