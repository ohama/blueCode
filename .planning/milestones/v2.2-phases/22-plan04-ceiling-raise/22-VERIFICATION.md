---
phase: 22-plan04-ceiling-raise
verified: 2026-04-28T05:34:07Z
status: passed
score: 4/6 success criteria fully met; SC5 FAIL x2 (intentional, v2.3 candidate); SC6 partial (verdict not flipped, doc updated accurately)
re_verification: false
---

# Phase 22: Ceiling Raise — Verification Report

**Phase Goal:** Raise the step ceiling from 5 to 10 via a config-driven seam (single source of truth across `PlanValidator.validatePlan` + `AgentLoop.runLoop`); update system prompt; add boundary tests; verify 7/7 bench gate held; re-run CORR-EVAL-02 producing orphan_count=0 PASS; update eval doc verdict 82 → 87.

**Verified:** 2026-04-28T05:34:07Z
**Status:** passed (with SC5/SC6 partial closure accepted by user — Option C; see Final Verdict)
**Re-verification:** No — initial verification

---

## SC1: Single Source of Truth Ceiling

**Claim:** `PlanValidator.MaxPlanSteps` and `AgentConfig.MaxLoops` (CompositionRoot.fs bootstrap) both = 10; named/clearly documented.

**Verification:**

`grep -n "MaxPlanSteps = 10" src/BlueCode.Core/PlanValidator.fs`
```
40:let MaxPlanSteps = 10
```

`grep -n "LOOP-01" src/BlueCode.Core/PlanValidator.fs`
```
37:/// (LOOP-01 default 10). Hardcoded here because Plan validation is not
```

`grep -n "≤ 10" src/BlueCode.Core/Domain.fs`
```
109:///   - Steps.Length must be ≤ 10 (matches AgentConfig.MaxLoops)
```

`grep -n "MaxLoops = 10" src/BlueCode.Cli/CompositionRoot.fs`
```
112:        { MaxLoops = 10
```

**Result: SC1 VERIFIED**

- `PlanValidator.MaxPlanSteps` = 10 at line 40; docstring at line 37 names it LOOP-01 default 10.
- `AgentConfig.MaxLoops` = 10 at CompositionRoot.fs:112 in the `bootstrap` function.
- `Domain.fs:109` comment updated to "≤ 10 (matches AgentConfig.MaxLoops)".
- Both values are the single source of truth for their respective subsystems; they are consistent.

---

## SC2: System Prompt Updated

**Claim:** No remaining "5 step/action/iteration" language in CompositionRoot.fs; system prompt length ≤ 900 chars.

**Verification:**

`grep -En "1-5 steps|max 5|5 actions|five iter" src/BlueCode.Cli/CompositionRoot.fs`
— No output (0 matches).

`grep -En "1-10 steps|max 10|10 step" src/BlueCode.Cli/CompositionRoot.fs`
```
98:Constraints: 1-10 steps. Use the minimum steps needed; reserve the full budget only for tasks requiring reads across multiple files before editing. No two adjacent steps may be identical. Do NOT execute — user will approve first."""
```

`planSystemPromptSuffix` character count (measured via `echo -n ... | wc -c`): **699 characters**.

**Result: SC2 VERIFIED**

- Zero occurrences of old "5 step" language.
- New "1-10 steps" constraint present at line 98.
- Prompt suffix is 699 chars, well within the ≤ 900 char budget.

---

## SC3: Tests Added

**Claim:** Boundary tests at 10/11; AgentLoop MaxLoopsExceeded test at new boundary; test count 282 → 284.

**Verification:**

`grep -n "exactly 10 steps\|more than 10 steps" tests/BlueCode.Tests/PlanValidatorTests.fs`
```
41:          testCase "PlanInvalid: more than 10 steps (Steps.Length > MaxPlanSteps)"
68:          testCase "valid plan: exactly 10 steps passes checkLength (ceiling boundary)"
```

`grep -n "10 distinct ToolCalls\|new ceiling" tests/BlueCode.Tests/AgentLoopTests.fs`
```
186:          testCaseAsync "max iter: 10 distinct ToolCalls without FinalAnswer -> MaxLoopsExceeded (new ceiling)"
201:              Expect.equal result (Error MaxLoopsExceeded) "should hit MaxLoopsExceeded after 10 ToolCalls at new ceiling"
```

`dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | tail -3`
```
284 tests run in 00:00:30.7 for all – 284 passed, 1 ignored, 0 failed, 0 errored. Success!
```

**Result: SC3 VERIFIED**

- PlanValidatorTests.fs:41 — "PlanInvalid: more than 10 steps" (updated from 6-step to 11-step plan; boundary at MaxPlanSteps).
- PlanValidatorTests.fs:68 — "valid plan: exactly 10 steps passes checkLength (ceiling boundary)" (new test).
- AgentLoopTests.fs:186 — "max iter: 10 distinct ToolCalls without FinalAnswer -> MaxLoopsExceeded (new ceiling)" (new test; uses `{ testConfig with MaxLoops = 10 }`).
- Test suite: 284 passed, 1 ignored, 0 failed. Count increased from 282 → 284 as planned.

---

## SC4: Bench Gate Regression Hold

**Claim:** `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)`; step counts unchanged for all 7 fixtures.

**Verification:**

`bash bench/run.sh --gate` (run live during verification):

```
PASS T6_122b    steps=5/5 exit=0
PASS W1_122b    steps=3/3 exit=0
PASS W2_122b    steps=3/3 exit=0
PASS T1_122b    steps=1/3 exit=0
PASS T5_122b    steps=3/4 exit=0
PASS B2_122b    steps=2/3 exit=0
PASS MT_122b    steps=2/4 exit=0
===== GATE PASS (7/7) =====
```

Exit code: 0.

**Result: SC4 VERIFIED**

- All 7 gate fixtures pass.
- Step counts match baseline tolerances: T6=5 (max 5), W1=3 (max 3), W2=3 (max 3), T1=1 (max 3), T5=3 (max 4), B2=2 (max 3), MT=2 (max 4).
- No regressions from the ceiling raise. Architectural change is non-behavioral: raising the cap from 5 to 10 does not cause the model to use more steps when it was already completing tasks in fewer steps.

---

## SC5: CORR-EVAL-02 PASS (FAIL — Intentional, v2.3 Candidate)

**Claim:** `bash bench/eval-qwen35-122b.sh --refactor` produces `refactor_orphan_count.txt` = 0; `CORR-EVAL-02 PASS:` line in transcript.

**Verification — Attempt 1 (original README):**

Run directory: `bench/runs/qwen35-eval-20260428-134438/`

`refactor_orphan_count.txt`: contains `1`

`refactor_multifile_diff.txt` final verdict line:
```
CORR-EVAL-02 FAIL: 1 orphan 'add' references remain after refactor
```

Agent used 8/10 steps (no MaxLoopsExceeded; 2-step budget slack unused).

Step-5 thought (the decisive extraction step):
> "Now I have all the information needed. The README.md asks to: 1. Rename 'add3' to 'sum3' in Calculator.fs 2. Update all references to 'add3' to 'sum3' in Main.fs and Tests.fs. I'll start by editing Calculator.fs to rename the function."

Agent correctly renamed all `add3` call sites but did not rename `add → sum`. The orphan is the `let add` function definition still present in `Calculator.fs` — the harness regex `\b(let |Calculator\.)add\b` matches `let add (x: int) (y: int) : int =` directly (verified empirically post-audit). The earlier framing in this report incorrectly attributed the orphan to a printf format-string substring; the printf string `"add3 1 2 3 = %d"` does NOT match the regex (no word-boundary after `add` due to `3`). The actual mechanism is the unrenamed function definition.

**Verification — Attempt 2 (rewritten README with explicit enumeration):**

Run directory: `bench/runs/qwen35-eval-20260428-140714/`

`refactor_orphan_count.txt`: contains `1`

`refactor_multifile_diff.txt` final verdict line:
```
CORR-EVAL-02 FAIL: 1 orphan 'add' references remain after refactor
```

Agent used 8/10 steps (identical step count). Step-5 thought (textually near-identical):
> "Now I have all the information needed. Based on the README.md, I need to: 1. Rename `add3` to `sum3` in Calculator.fs 2. Update all references to `add3` to `sum3` in Main.fs and Tests.fs. Let me start by editing Calculator.fs to rename the function."

Despite a rewritten README that explicitly enumerated BOTH rename targets (`add → sum` AND `add3 → sum3`) with a completion checklist and an explicit warning, the model produced textually identical step-5 thoughts in both attempts. This confirms the failure is a comprehension-layer extraction bias, not a budget constraint or specification clarity issue.

**Result: SC5 FAIL (intentional; accepted by user as Option C)**

The ceiling raise was a necessary but insufficient intervention. The comprehension layer — specifically, extraction of shared-prefix function names (`add` vs `add3`) — is the new bottleneck. This is documented as a v2.3 candidate (system prompt enumeration guidance, plan-mode pre-flight rename-target enumeration, and/or few-shot multi-file refactor examples).

---

## SC6: Eval Doc Updated (PARTIAL — Intentional)

**Claim (original):** §2.4 PASS, §7 Correctness 31 → 36, Total 82 → 87, §8 caveat removed, §9 item resolved, final line `**Total: 87/100, Recommendation: KEEP**`.

**Actual state verified:**

Final verdict line:
```
grep -E "^\*\*Total: 82/100, Recommendation: KEEP\*\*$" documentation/qwen35-122b-coding-eval.md
```
Output: `**Total: 82/100, Recommendation: KEEP**` — MATCH. Verdict NOT flipped to 87.

§2.4 status: FAIL (not PASS). The section now documents the two-stage finding in full:
- Stage 1 (v2.1): 5-step cap was sole structural constraint; task physically impossible at N=7 steps needed.
- Stage 2 (v2.2): Ceiling raised to 10; CORR-EVAL-02 re-run twice; both produced identical orphan_count=1 with textually identical step-5 thoughts.
- §2.4 verdict: `FAIL — orphan_count=1 (persistent extraction bias on shared-prefix function names; v2.2 ceiling raise revealed comprehension layer as new bottleneck). Score: **0/5**`.

§7 scorecard: Correctness subtotal remains 31/40; Grand total 82/100. NOT updated to 87.

§8 caveat #6 (multi-file refactor): Updated from original single-sentence v2.1 hypothesis to full two-stage finding documentation including "persistent extraction bias on shared-prefix function names" and "v2.3 candidate" language.

§9 item 6: RESOLVED tag present — "~~if the 5-step cap is raised, the multi-file refactor should be re-run and CORR-EVAL-02 re-scored~~ **RESOLVED in v2.2**: cap raised 5→10; CORR-EVAL-02 re-run produced identical FAIL twice..."

§9 item 8 (new): "Comprehension layer fix attempts (v2.3 candidate)" documents the forward-looking intervention scope.

**Result: SC6 PARTIAL (intentional; accepted by user as Option C)**

The eval doc accurately reflects the empirical outcome. Verdict was not flipped to 87 because the SC5 PASS gate was not met. The doc update is substantive and honest — it replaces a speculative caveat with a confirmed two-stage finding and scopes the residual problem for v2.3.

---

## Observable Truths Summary

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Ceiling is 10, single source of truth in both subsystems | VERIFIED | PlanValidator.fs:40 `MaxPlanSteps = 10`; CompositionRoot.fs:112 `MaxLoops = 10` |
| 2 | System prompt uses "1-10 steps", no "5" language | VERIFIED | CompositionRoot.fs:98; 0 grep hits on old language; 699-char suffix |
| 3 | Test suite at 284 with new boundary tests | VERIFIED | 284 passed / 1 ignored / 0 failed; new tests at PlanValidatorTests:41,68 and AgentLoopTests:186 |
| 4 | Bench gate 7/7 PASS, step counts held | VERIFIED | Live run: all 7 fixtures PASS; exit 0 |
| 5 | CORR-EVAL-02 orphan_count=0 | FAIL (intentional) | Both runs: orphan_count=1; identical step-5 comprehension failure; v2.3 candidate |
| 6 | Eval doc verdict 82→87 | PARTIAL (intentional) | Verdict stays 82; doc updated with two-stage finding; §8/#9 accurately reflect SC5 empirical outcome |

**Score:** 4/6 success criteria fully met; SC5/SC6 partial closure user-accepted (Option C).

---

## Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/BlueCode.Core/PlanValidator.fs` | MaxPlanSteps = 10 | VERIFIED | Line 40; docstring LOOP-01 at line 37 |
| `src/BlueCode.Cli/CompositionRoot.fs` | MaxLoops = 10; "1-10 steps" in prompt | VERIFIED | Lines 112, 98 |
| `src/BlueCode.Core/Domain.fs` | Comment updated to "≤ 10" | VERIFIED | Line 109 |
| `tests/BlueCode.Tests/PlanValidatorTests.fs` | Boundary tests at 10/11 | VERIFIED | Lines 41, 68 |
| `tests/BlueCode.Tests/AgentLoopTests.fs` | MaxLoopsExceeded at 10 | VERIFIED | Lines 186-201 |
| `bench/runs/qwen35-eval-20260428-134438/` | CORR-EVAL-02 attempt 1 | FAIL (expected) | orphan_count=1 |
| `bench/runs/qwen35-eval-20260428-140714/` | CORR-EVAL-02 attempt 2 | FAIL (expected) | orphan_count=1 |
| `documentation/qwen35-122b-coding-eval.md` | Two-stage finding documented | PARTIAL | §2.4/§8/§9 updated; verdict 82 preserved |

---

## Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `PlanValidator.validatePlan` | ceiling = 10 | `MaxPlanSteps` constant | WIRED | `if plan.Steps.Length > MaxPlanSteps` at line 43 |
| `AgentLoop.runLoop` | ceiling = 10 | `AgentConfig.MaxLoops` | WIRED | Config injected via `bootstrap`; `testConfig with MaxLoops = 10` in tests |
| `planSystemPromptSuffix` | "1-10 steps" | string literal in bootstrap | WIRED | Line 98 of CompositionRoot.fs |
| PlanValidatorTests "exactly 10" | `validatePlan` | direct call | WIRED | Returns `Ok _`; test at line 68 |
| PlanValidatorTests "more than 10" | `validatePlan` | direct call | WIRED | Returns `Error(PlanInvalid ...)`; test at line 41 |
| AgentLoopTests "10 calls" | `runSession` | `config10` (`MaxLoops = 10`) | WIRED | Returns `Error MaxLoopsExceeded`; test at line 186 |

---

## Anti-Patterns

No blocker anti-patterns found in Phase 22 modified files. The five source files modified (PlanValidator.fs, AgentLoop.fs, Domain.fs, CompositionRoot.fs, Rendering.fs) contain no TODO/FIXME comments, placeholder returns, or stub handlers related to the ceiling raise work.

---

## Plan Metadata Coverage

All four plan SUMMARYs verified present:

- `22-01-PLAN.md` + `22-01-SUMMARY.md` — ceiling seam architectural work
- `22-02-PLAN.md` + `22-02-SUMMARY.md` — system prompt update + tests
- `22-03-PLAN.md` + `22-03-SUMMARY.md` — bench gate re-run
- `22-04-PLAN.md` + `22-04-SUMMARY.md` — CORR-EVAL-02 re-run + eval doc update

---

## Final Verdict

**Phase 22 architectural deliverables shipped cleanly:**

SC1-SC4 represent the structural work of Phase 22 — raising the step ceiling from 5 to 10 via a named constant (`LOOP-01`), propagating it through the plan validator and agent loop, updating the system prompt, and adding boundary tests. All four are fully verified against the codebase. The bench gate held at 7/7 with no regressions, confirming the ceiling raise is behavioral no-op for tasks that fit within the previous 5-step budget.

**SC5/SC6 partial closure is a deliberate user-accepted outcome (Option C):**

CORR-EVAL-02 failed twice. The ceiling raise was necessary (task requires N=7 steps minimum; 5 < 7 = physically impossible) but not sufficient. The model exhibits a persistent comprehension-layer extraction bias when given multi-target rename tasks involving shared-prefix function names (`add` vs `add3`): it consistently extracts only the longer-suffix variant as the rename target, regardless of how the specification is worded. The eval doc verdict stays at 82/100 because this empirical reality was not overcome by the v2.2 interventions.

The finding is documented accurately and in full in `documentation/qwen35-122b-coding-eval.md` §2.4, §8 caveat #6, and §9 items 6 (RESOLVED: ceiling raise) + 8 (forward: comprehension layer fix as v2.3 candidate). No information is hidden; the two-stage finding increases the value of this eval by explaining exactly what the model can and cannot do with the current architecture.

**Options considered and rejected:**

- Option A (README rewrite with explicit enumeration): Attempted at `qwen35-eval-20260428-140714`; produced identical orphan_count=1 and textually identical step-5 thoughts. Ruled out.
- Option B (system prompt guidance for multi-target rename): Scope creep risk; changes runtime behavior across all tasks, not just eval; deferred to v2.3.
- Option C (accept verdict + document as v2.3 candidate): Chosen. Phase 22 closes with honest partial outcome.

**v2.3 candidate:** Persistent extraction bias on shared-prefix function names. Proposed interventions include system prompt enumeration guidance, plan-mode pre-flight rename-target enumeration, and/or few-shot multi-file refactor examples. See §9 item 8 of the eval doc for the load-bearing measurement target.

---

_Verified: 2026-04-28T05:34:07Z_
_Verifier: Claude (gsd-verifier)_
