# Phase 26 Verification: Re-Evaluation (CORR-EVAL-02 — BLOCKED)

**Status:** blocked
**Verified:** 2026-04-29
**Plans verified:** 26-01 (partial — Task 1 FAIL after 3 attempts)
**Verifier:** session

## Summary

Phase 26 BLOCKED. All 3 stochastic CORR-EVAL-02 re-run attempts produced FAIL (orphan_count=1) despite all 3 v2.3 prongs (P1+P2+P3) being in production. Eval doc is UNTOUCHED. Phase 24/25 source code is UNTOUCHED. Phase 26 Tasks 2 and 3 did not run.

**Key diagnostic finding:** The failure mode has qualitatively changed from v2.2. In v2.2, the agent correctly read the README and understood the rename task, but exhibited an extraction bias (renamed `add3`→`sum3` only, missed `add`→`sum`). In Phase 26, the agent misread/hallucinated the README content entirely — in all 3 attempts, step-5 thought was:

> "Based on the README instructions (which mentioned adding a `subtract` function and updating all references), I need to: 1. Add a `subtract` function to Calculator.fs"

The README contains NO mention of a `subtract` function. The README clearly states to rename `add`→`sum` AND `add3`→`sum3`. The agent's step-2 thought says "The README was truncated" (though the file is 2226 bytes and was successfully read as 2128 chars). This is task-hallucination, not extraction bias.

**Critical structural gap identified:** P3 (PlanValidator `checkRenameTargetsEnumerated`) is a plan-mode-only feature activated by `--plan` flag. The eval harness runs `blueCode --verbose --model 122b` WITHOUT `--plan`. P3 was never in play for CORR-EVAL-02. Only P1 (system prompt directive) and P2 (few-shot examples) could affect this eval path — and both are plan-mode-specific additions to `planSystemPromptSuffix`. The comprehension issue needs a solution that works in agent-loop mode (no --plan), not just plan-mode.

## Attempt Evidence

### Attempt 1
- **Run dir:** `bench/runs/qwen35-eval-20260429-072504/`
- **orphan_count:** 1
- **elapsed:** 52s
- **Step-5 thought:** "Based on the README instructions (which mentioned adding a `subtract` function...)"
- **Agent action:** Added `subtract` function to Calculator.fs; did not rename `add`/`add3`
- **Log:** `/tmp/26-01-attempt1.log`

### Attempt 2
- **Run dir:** `bench/runs/qwen35-eval-20260429-072616/`
- **orphan_count:** 1
- **elapsed:** 49s
- **Step-5 thought:** Textually identical to attempt 1 ("adding a `subtract` function...")
- **Log:** `/tmp/26-01-attempt2.log`

### Attempt 3
- **Run dir:** `bench/runs/qwen35-eval-20260429-072723/`
- **orphan_count:** 1
- **elapsed:** 50s
- **Step-5 thought:** Textually identical to attempts 1+2 ("adding a `subtract` function...")
- **Log:** `/tmp/26-01-attempt3.log`

### Collected fail-thoughts
`/tmp/26-01-fail-thoughts.log` — all 3 attempts show textually identical step-5 thought.

## Diagnostic Questions (for v2.4 investigation)

1. **Why does the agent hallucinate "subtract"?** The model read the README successfully (step-1 result: "Success (2128 chars)") but step-2 thought says "The README was truncated". Is the model confusing this session with a prior session or a training example? Is the session-start context window floor message (max_model_len=8192) affecting perception?

2. **P1 and P2 are plan-mode-only — do they affect agent-loop mode at all?** `planSystemPromptSuffix` is only used in plan-mode invocations. For `--verbose` without `--plan`, the system prompt is `defaultSystemPrompt` only. P1 enumeration directive and P2 few-shot example in `planSystemPromptSuffix` are NOT sent during the eval harness invocation.

3. **P3 is plan-mode-only by design.** `PlanValidator.checkRenameTargetsEnumerated` runs only after `runPlanTurn` produces a plan. Eval uses agent-loop path. P3 is architecturally not applicable to CORR-EVAL-02 as currently run.

4. **Does the task need `--plan` flag in the eval harness?** If so: (a) the eval harness needs updating (`--plan` added to the invocation), (b) the refactor prompt would need to go through plan-then-execute mode, (c) P3 would then be active, and (d) the 2-attempt retry with [PLAN INVALID] correction would fire if the plan missed `add`. This is a fundamental eval design question.

5. **Alternatively: should P1/P2 directive be in `defaultSystemPrompt`?** If P1 enumeration directive moved to `defaultSystemPrompt` (not `planSystemPromptSuffix`), it would apply to agent-loop mode too. Risk: bench gate regression on W1/W2/T6/etc. This was intentionally avoided in Phase 24 to confine the prompt change to plan-mode.

6. **Is the "subtract" hallucination new?** Compare with v2.2 transcripts: did v2.2 also hallucinate "subtract" or did v2.2 correctly read the README and then exhibit the extraction bias? If v2.2 correctly read the README but Phase 26 doesn't, something changed (model state, KV cache, launchd restart needed, context contamination?).

7. **Was a `launchctl kickstart` needed?** The 122B service was live (confirmed at pre-flight), but a long-running KV cache might be causing confusion. v2.2 runs were done on a fresh-loaded model instance.

8. **Should the eval harness be redesigned?** The fixture is an agent-loop task. P3 is plan-mode-only. The three prongs were designed for a plan-mode path. If CORR-EVAL-02 fundamentally needs agent-loop mode (not plan-mode), then P3 provides no benefit and P1/P2 don't reach the agent. v2.4 needs to decide: does the fixture use `--plan` (plan-mode), or should P1/P2 be moved to `defaultSystemPrompt`?

## Guardrails Held

- Eval doc UNTOUCHED (`documentation/qwen35-122b-coding-eval.md` not modified)
- Phase 24/25 source UNTOUCHED (`git diff src/` empty)
- REQUIREMENTS.md COMP-05/COMP-06 stay `[ ]` (not falsely marked complete)
- Bench gate NOT run (irrelevant if CORR-EVAL-02 itself FAILs; fixtures manually restored)
- Fixtures restored via `git checkout -- bench/fixtures/refactor_multifile/` after all 3 attempts

---

*Verification: 2026-04-29*
*Phase 26 BLOCKED. CORR-EVAL-02 FAIL x3 with hallucination failure mode (new vs v2.2 extraction-bias mode). v2.4 investigation required.*
