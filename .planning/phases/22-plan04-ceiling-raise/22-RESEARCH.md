# Phase 22: PLAN-04 Ceiling Raise — Research

**Researched:** 2026-04-28
**Domain:** F# constant refactoring / agent loop configuration / system prompt tuning
**Confidence:** HIGH — all findings from direct code inspection; no speculation

---

## Summary

Phase 22 raises the PLAN-04 step ceiling from 5 to 10. The change is motivated by
CORR-EVAL-02 FAIL (orphan_count=1): the agent read 4 files coherently then exhausted
the 5-step budget on the first edit, never reaching Main.fs or Tests.fs. A genuine
4-file rename requires a minimum of 7 steps (4 reads + 3 edits). A ceiling of 10
gives comfortable headroom without permitting arbitrarily long sessions.

The standard approach is **Option 1: bump both constants independently** (not Option 2
unified constant). The two sites are intentionally decoupled: `PlanValidator` runs at
JSON parse time with no `AgentConfig` in scope; `AgentLoop` uses `config.MaxLoops` at
execution time. The docstring at `PlanValidator.fs:36-40` already explains why they are
separate. Option 2 introduces a new file or module to share a constant between Core
layers, which is extra complexity for zero behavioral gain in this milestone.

The most dangerous regression vector is the system prompt update. The existing gate
fixtures (W1/W2: 3 steps; T6: 4 steps; T1: 1 step) have `step_count_max` slack built
into `bench/baseline.json`. If the new prompt language drops the usage-guidance clause
("use full budget only for multi-step coherent work"), the model may expand step counts
on simple tasks, tripping the gate.

**Primary recommendation:** Bump `MaxPlanSteps` to 10 in `PlanValidator.fs` and
`MaxLoops` to 10 in `CompositionRoot.fs`. Add a usage-guidance clause to the plan-mode
suffix. Update the four secondary sites that hardcode the number 5 in user-visible
strings. Add 3 boundary tests. Re-run CORR-EVAL-02 to PASS.

---

## 1. Recommended Ceiling Value: 10

**Argument from task complexity:**

The 21-03 SUMMARY documents the minimum step budget for the refactor fixture:
- Step 1: read README.md
- Steps 2-4: read Calculator.fs, Main.fs, Tests.fs (3 reads)
- Steps 5-7: edit Calculator.fs, Main.fs, Tests.fs (3 edits)
- Step 8 (optional): verify with grep_search

Minimum = 7 steps; 8 with a verification step. A ceiling of 8 would pass the task but
leaves no slack for the agent to re-read a file on a failed edit attempt. 10 provides
3-step slack above the minimum without approaching the territory where unconstrained
sessions would be a risk.

**Argument from multi-turn N=10:**

The multi-turn eval (`run_multiturn`) tests sessions of N=1,3,5,7,10 turns. Each turn
has its own MaxLoops budget; they are independent. Raising MaxLoops per turn to 10 does
NOT interact with the multi-turn turn count. The MT_122b gate fixture (2 steps/turn,
step_count_max=4) is unaffected by the ceiling raise because simple tasks still resolve
in 1-3 steps.

**Argument from simple-task regression risk:**

Current baselines from `bench/baseline.json`:

| Fixture | step_count | step_count_max | Risk |
|---------|------------|----------------|------|
| T6_122b | 4          | 5              | Headroom = 1 step. With ceiling 10 and a permissive prompt, T6 could expand to 5-6. |
| W1_122b | 3          | 3              | Loop-injection enforces 3 steps at code level. No risk from ceiling raise. |
| W2_122b | 3          | 3              | Same as W1 — loop-injection enforces 3 steps. |
| T1_122b | 1          | 3              | 1-step trivial answer. No risk. |
| T5_122b | 3          | 4              | 3-step glob+shell pattern. Low risk. |
| B2_122b | 2          | 3              | 2-step read+diagnose. No risk. |
| MT_122b | 2          | 4              | 2-step turn 1. No risk. |

W1 and W2 are safe because the **loop-injection primitive** (09.1-05 / PERF-02) enforces
3 steps at the code level via `[POST-EDIT CONSTRAINT]` Role=User injection — these
fixtures are independent of the system prompt's step guidance. T6 is the only fixture
with a 1-step headroom, and its task (find field names in a record) should not expand
beyond 4-5 steps regardless of the ceiling.

**Verdict: 10 is the right ceiling.** High enough to permit genuine multi-file refactors
(7-8 steps); low enough to maintain a meaningful guard against runaway sessions.

---

## 2. Design: Option 1 (Bump Both) vs Option 2 (Unify)

**Option 1 — bump both constants independently:**
- `PlanValidator.fs:40` `let MaxPlanSteps = 5` → `let MaxPlanSteps = 10`
- `CompositionRoot.fs:112` `MaxLoops = 5` → `MaxLoops = 10`
- Update the docstring at `PlanValidator.fs:36-40` to reflect new value
- Four additional sites with hardcoded "5" in user-visible strings (see §6 below)

**Option 2 — shared constant (`AgentConstants.maxStepsPerTurn = 10`):**
- New module `src/BlueCode.Core/AgentConstants.fs`
- Both `PlanValidator.fs` and `AgentLoop.fs`/`CompositionRoot.fs` import it
- Eliminates future drift between the two sites

**Recommendation: Option 1.**

Rationale:

1. **v2.2 scope discipline.** ROADMAP.md explicitly states "bounded surgical change; no
   new dependencies; no refactor beyond what the task requires." Option 2 adds a new
   file, a new module, and requires updates to `BlueCode.Tests.fsproj` if any test
   imports it (unlikely, but introduces fsproj risk).

2. **The coupling is already acknowledged and documented.** `PlanValidator.fs:36-40`
   has a clear docstring explaining why the separation is intentional. After updating
   the docstring to say "default 10" the situation is identical to the current "default
   5" state — well-documented independent constants.

3. **v1.1 LlmResponse precedent favors big-bang compiles, not new abstraction layers.**
   The v1.1 LlmResponse refactor introduced a new Core record because the semantic was
   new (captured thought vs placeholder). Here the semantic is unchanged — only the
   value changes. A new module for a single integer is over-engineering.

4. **Option 2 deferred cleanly.** If v2.3 or later wants a `AgentConstants` module
   (e.g., to also centralize `ContextCapacity`, `LoopGuardThreshold`), that's a clean
   refactor with a clear motivation. v2.2 should not preemptively extract constants.

---

## 3. Bench Gate Regression Risk Analysis

**Mechanism of potential regression:**

If the system prompt no longer guides the agent to "minimize steps for simple tasks /
use full budget only for multi-step coherent work", the model may expand step counts
on T6. T6's `step_count_max=5` would still pass (4 typical + 1 headroom), but if T6
reaches 6+ steps the gate fails.

**Current fixture step counts vs new ceiling:**

| Fixture | Typical | Max | New ceiling | Slack above max | Risk |
|---------|---------|-----|-------------|-----------------|------|
| T6_122b | 4       | 5   | 10          | 5               | Medium — prompt-dependent |
| T5_122b | 3       | 4   | 10          | 6               | Low |
| B2_122b | 2       | 3   | 10          | 7               | Low |
| T1_122b | 1       | 3   | 10          | 7               | Low |
| W1_122b | 3       | 3   | 10          | 7               | None (code-enforced 3) |
| W2_122b | 3       | 3   | 10          | 7               | None (code-enforced 3) |
| MT_122b | 2       | 4   | 10          | 6               | Low |

**Key safety factor:** `bench/baseline.json` MUST NOT be modified. The gate is the
regression authority. If any fixture exceeds its `step_count_max` post-change, that is
a prompt regression requiring 22-02 iteration — not a baseline update.

**Mitigation strategy (system prompt guidance clause):**

The plan-mode suffix should include: "Use the minimum steps needed; reserve the full
budget only for tasks requiring reads across multiple files." This targets the model's
learned tendency to be efficient on simple tasks while communicating that multi-file
work has headroom.

---

## 4. System Prompt Update Strategy

**Current state (exact text, CompositionRoot.fs:94-98):**

```fsharp
let planSystemPromptSuffix: string =
    """OVERRIDE — PLAN MODE ACTIVE. Do NOT use read_file/write_file/list_dir/run_shell/edit_file/glob_search/grep_search/final actions.
Your ONLY valid response is action="plan". Respond with EXACTLY this JSON shape:
{"thought": "<reasoning>", "action": "plan", "input": {"steps": [{"tool": "<tool>", "input": {}, "rationale": "<why>"}], "rationale": "<overall why>"}}
where each "tool" is one of: read_file|write_file|list_dir|run_shell|edit_file|glob_search|grep_search.
Constraints: 1-5 steps. No two adjacent steps may be identical. Do NOT execute — user will approve first."""
```

**Site to change:** Line 98 `Constraints: 1-5 steps.` → new wording.

**The `defaultSystemPrompt` (lines 69-83) does NOT contain step-count language.** Only
the `planSystemPromptSuffix` mentions "1-5 steps". The `defaultSystemPrompt` is for
execution mode; it is not affected by the ceiling change.

**Rendering.fs:114 also has a hardcoded "5":**
```fsharp
| MaxLoopsExceeded -> "Max loops exceeded (5 steps with no final answer)."
```
This is a user-facing error message. It should be updated to "10" to match the new
ceiling. This is in `BlueCode.Cli/Rendering.fs`, not Core — acceptable to update.

**AgentLoop.fs:502 has a hardcoded "max 5 steps" in the [PLAN INVALID] retry message:**
```fsharp
sprintf "[PLAN INVALID] Your previous plan failed validation: %s. Constraints: max 5 steps; ..."
```
This must change to "max 10 steps" to keep the retry message aligned with the actual
constraint.

**Domain.fs:109 has a comment "≤ 5 (matches AgentConfig.MaxLoops)":**
```fsharp
///   - Steps.Length must be ≤ 5 (matches AgentConfig.MaxLoops)
```
This comment must be updated to "≤ 10".

**RenderingTests.fs:73 asserts "5 steps" string:**
```fsharp
Expect.stringContains (renderError MaxLoopsExceeded) "5 steps" "MaxLoops msg"
```
This test will fail when `Rendering.fs:114` is updated. Update assertion to "10 steps".

**CompositionRootTests.fs:25 asserts MaxLoops = 5:**
```fsharp
Expect.equal c.Config.MaxLoops 5 "MaxLoops = 5"
```
This test will fail when `CompositionRoot.fs:112` is updated. Update assertion to 10.

**Recommended prompt language** — drop the "1-5" verbatim count and use named guidance:

```
Constraints: 1-10 steps. Use the minimum steps needed; reserve the full budget only for tasks requiring reads across multiple files before editing. No two adjacent steps may be identical. Do NOT execute — user will approve first.
```

This survives future ceiling tuning better than a hardcoded number, and the explicit
usage guidance is the key regression guard for simple fixtures.

**Char count check:** The v1.3 PERF-01 target was ≤800 chars for `defaultSystemPrompt`
(achieved 783). The ROADMAP specifies ≤900 chars for the post-v2.2 system prompt.
`planSystemPromptSuffix` is currently ~360 chars; the proposed change adds ~60 chars
(usage guidance clause), keeping total well below 900.

---

## 5. Test Additions Strategy

**Existing test modules (from RouterTests.fs rootTests):**

`PlanValidatorTests.tests` exists at `/tests/BlueCode.Tests/PlanValidatorTests.fs` and
is already in `rootTests`. `AgentLoopTests.agentLoopTests` exists at
`/tests/BlueCode.Tests/AgentLoopTests.fs` and is also in `rootTests`.

No new test module needed — extend both existing files. This avoids the `BlueCode.Tests.fsproj`
`<Compile Include>` + `rootTests` double-registration pitfall entirely.

**Required test additions:**

PlanValidatorTests.fs — add 2 cases:

1. "valid plan: exactly 10 steps passes checkLength" — construct a plan with 10 distinct
   steps (10 different `list_dir` paths), verify `validatePlan` returns `Ok`.
2. "PlanInvalid: 11 steps exceeds MaxPlanSteps" — construct a plan with 11 steps,
   verify `validatePlan` returns `Error(PlanInvalid ...)` with detail containing "11"
   or "max" or "step".

The existing test "PlanInvalid: more than 5 steps" (6 steps) still passes after the
ceiling raise (6 ≤ 10 → this test will FAIL because 6 steps is now valid). This
existing test MUST be updated: change it to use 11 steps or update the description to
"more than 10 steps". The test at line 41-61 currently uses 6 steps and expects failure
— after the change, 6 steps is valid and the test would incorrectly PASS (returning Ok
instead of Error). **This is the highest-risk test interaction.**

AgentLoopTests.fs — add 1 case:

3. "max iter: 10 distinct ToolCalls without FinalAnswer -> MaxLoopsExceeded" — mirror
   the existing "max iter: 5 distinct ToolCalls" test at lines 171-184 but with 10
   calls and a config of `{ testConfig with MaxLoops = 10 }`.

Note: `testConfig` at AgentLoopTests.fs:34-38 has `MaxLoops = 5`. The existing test
"max iter: 5 distinct ToolCalls" tests with `testConfig` (MaxLoops=5). After the
ceiling raise, this test still passes because `testConfig` still has `MaxLoops = 5`
(it's a local test constant, not reading from `CompositionRoot`). Do NOT change
`testConfig` — instead add a new config for the boundary test.

**Test count trajectory:** Current 282. PlanValidatorTests adds 2 (boundary pass +
boundary fail). AgentLoopTests adds 1 (10-call MaxLoops). Existing "more than 5 steps"
test is updated in-place (not added). Total: 282 + 3 = 285. Meets ≥285 target.

---

## 6. Complete Inventory of Sites Requiring Change

All 5-related hardcoded values found via `grep -rn`:

| File | Line | Current | Required Change | Plan |
|------|------|---------|-----------------|------|
| `src/BlueCode.Core/PlanValidator.fs` | 40 | `let MaxPlanSteps = 5` | `= 10` | 22-01 |
| `src/BlueCode.Core/PlanValidator.fs` | 37 | docstring "default 5" | "default 10" | 22-01 |
| `src/BlueCode.Core/Domain.fs` | 109 | comment "≤ 5 (matches AgentConfig.MaxLoops)" | "≤ 10" | 22-01 |
| `src/BlueCode.Cli/CompositionRoot.fs` | 112 | `MaxLoops = 5` | `= 10` | 22-01 |
| `src/BlueCode.Cli/CompositionRoot.fs` | 98 | `Constraints: 1-5 steps.` | new wording (see §4) | 22-02 |
| `src/BlueCode.Cli/Rendering.fs` | 114 | `"Max loops exceeded (5 steps..."` | `"...10 steps..."` | 22-02 |
| `src/BlueCode.Core/AgentLoop.fs` | 502 | `"...Constraints: max 5 steps;..."` | `"...max 10 steps;..."` | 22-02 |
| `tests/BlueCode.Tests/CompositionRootTests.fs` | 25 | `Expect.equal c.Config.MaxLoops 5` | `= 10` | 22-01 |
| `tests/BlueCode.Tests/RenderingTests.fs` | 73 | `"5 steps"` | `"10 steps"` | 22-02 |
| `tests/BlueCode.Tests/PlanValidatorTests.fs` | 41 | "more than 5 steps" (6 steps, expects Error) | update to 11 steps | 22-01 |

**Unexpected coupling found:** `AgentLoop.fs:502` contains the literal string
`"Constraints: max 5 steps"` in the `[PLAN INVALID]` retry message inside
`buildPlanRetryMessage`. This is in Core — not a test file, not Cli. It must change to
"max 10 steps" for correctness when the model gets a retry message.

Also found: `AgentLoopSmokeTests.fs:20` has the comment `"≤5 steps with final answer"`
— this is a comment only, not an assertion. Low-priority update for clarity.

---

## 7. CORR-EVAL-02 Re-run Logistics

**Handler:** `run_refactor()` in `bench/eval-qwen35-122b.sh` (lines 259-312).

**Fixture reset:** The handler does NOT reset fixtures before running. Fixtures are
restored by the `EXIT trap` in `bench/run.sh` (line 18). When running `--refactor`
standalone (not via `bench/run.sh --gate`), the trap does NOT fire. To guarantee a
clean fixture state before the re-run, the planner should include a `git checkout --`
on the refactor_multifile directory before invoking `--refactor`.

**orphan_count.txt path:** Written to `$LOG_DIR/refactor_orphan_count.txt` at line 299.
`LOG_DIR` is `bench/runs/qwen35-eval-$(date +%Y%m%d-%H%M%S)`. The file is written
before the handler returns, so the EXIT trap from a subsequent `bench/run.sh --gate`
call cannot corrupt it.

**Orphan check logic (lines 295-298):**
```bash
orphan_count=$(grep -cE '\b(let |Calculator\.)add\b' \
    "$fixture_dir/Calculator.fs" \
    "$fixture_dir/Main.fs" \
    "$fixture_dir/Tests.fs" 2>/dev/null | awk -F: '{sum+=$2} END {print sum+0}')
```
This checks for remaining `let add` or `Calculator.add` references. For PASS, the agent
must rename `add` → `sum` AND `add3` → `sum3` in all three files. With 10 steps:
steps 1-4 = reads, steps 5-7 = edits (Calculator.fs, Main.fs, Tests.fs), step 8 =
optional verify. The task is comfortably achievable.

**Expected wall-clock for re-run:** 21-03 SUMMARY documents 17s for the 5-step partial
run (which hit MaxLoopsExceeded). With 10 steps and a successful completion the run
should be 25-40s (7-8 steps × ~4-5s per step at 34.6 tok/s). No timeout risk.

**set +e / set -e bracket:** Already present at lines 276-278. blueCode exits 1 on
MaxLoopsExceeded; the bracket ensures the handler does not abort under `set -euo
pipefail` before writing `refactor_orphan_count.txt`.

---

## 8. Eval Doc Update Scope

All sections of `documentation/qwen35-122b-coding-eval.md` requiring change for 22-04:

**§2.4 (line 267):**
```
§2.4 verdict: FAIL — orphan_count=1 (5-step budget exhausted). Score: **0/5**.
```
→ `§2.4 verdict: PASS — orphan_count=0 (10-step budget sufficient). Score: **5/5**.`

Also update lines 245-266 body text to reference the successful run artifact.

**§7 Verdict scorecard (lines 808-830):**

Change:
```
| Correctness | Multi-file refactor (all-or-nothing) | 0 | 5 |
| **Correctness subtotal** | | **31** | **40** |
```
To:
```
| Correctness | Multi-file refactor (all-or-nothing) | 5 | 5 |
| **Correctness subtotal** | | **36** | **40** |
```

And the dimension coverage row:
```
| Correctness | 31/40 | 77.5% | YES |
```
→ `| Correctness | 36/40 | 90.0% | YES |`

And the aggregate verdict section:
```
- Grand total: 31 + 20 + 25 + 6 = **82/100** → ≥80 band
```
→ `- Grand total: 36 + 20 + 25 + 6 = **87/100** → ≥80 band`

**§8 Caveats (lines 874-877):**

Caveat 6:
```
6. **Multi-file refactor is a structural blueCode limit, not a model deficiency.** The 5-step PLAN-04
   hard cap makes any task requiring >5 steps impossible to complete in a single blueCode invocation.
```
→ Remove this caveat entirely (it is resolved by v2.2) or replace with: "Multi-file refactor
capability restored in v2.2 (ceiling raised to 10). CORR-EVAL-02 re-run confirms PASS."

**§9 Re-evaluation triggers (lines 906-908):**

Item 6:
```
6. **blueCode step limit change (PLAN-04)** — if the 5-step cap is raised, the multi-file refactor
   should be re-run and CORR-EVAL-02 re-scored.
```
→ Mark as **resolved**: `6. ~~blueCode step limit change (PLAN-04)~~ — RESOLVED in v2.2 (ceiling raised to 10; CORR-EVAL-02 re-run 2026-04-28 → PASS orphan_count=0).`

**Final line (line 983):**
```
**Total: 82/100, Recommendation: KEEP**
```
→ `**Total: 87/100, Recommendation: KEEP**`

**Header:** Update the `**blueCode commit:**` field on line 7 to the v2.2 commit hash.

**Date stamp:** Line 4 `**Date:** 2026-04-28` — update to re-run date (also 2026-04-28
since v2.2 is same day; update if different).

---

## 9. Architectural Invariants Preserved

| Invariant | Status after v2.2 |
|-----------|-------------------|
| Core purity (no Serilog/Spectre/Argu/HttpClient in Core) | PRESERVED — only PlanValidator.fs + Domain.fs + AgentLoop.fs comments touched |
| `task {}` only in Core | PRESERVED — no CE changes |
| `bench/baseline.json` byte-for-byte preserved | MANDATORY — the gate is the authority; no modifications allowed |
| Test count 282 → ≥285 | MET — 3 new cases + 1 updated case = 285 |
| `Role = User` invariant | UNCHANGED — no AgentLoop message-building changes |
| Atomic commits per CLAUDE.md | Per-plan atomic; plan-meta separate |
| No `.claude/` or `localLLM/` sweeping | Per CLAUDE.md — never `git add -A` |

---

## Sources

All findings from direct code inspection at exact file paths:

- `src/BlueCode.Core/PlanValidator.fs` — lines 36-44 (MaxPlanSteps constant + checkLength)
- `src/BlueCode.Core/AgentLoop.fs` — line 24 (MaxLoops field); line 312-313 (guard check); line 502 (retry message)
- `src/BlueCode.Cli/CompositionRoot.fs` — lines 94-98 (planSystemPromptSuffix); line 112 (MaxLoops = 5)
- `src/BlueCode.Cli/Rendering.fs` — line 114 (MaxLoopsExceeded user message)
- `src/BlueCode.Core/Domain.fs` — line 109 (comment "≤ 5")
- `tests/BlueCode.Tests/CompositionRootTests.fs` — line 25 (MaxLoops = 5 assertion)
- `tests/BlueCode.Tests/RenderingTests.fs` — line 73 ("5 steps" assertion)
- `tests/BlueCode.Tests/PlanValidatorTests.fs` — lines 41-61 (existing 6-step test that will break)
- `tests/BlueCode.Tests/AgentLoopTests.fs` — lines 34-38 (testConfig MaxLoops=5); lines 171-183 (5-call MaxLoops test)
- `tests/BlueCode.Tests/RouterTests.fs` — lines 90-114 (rootTests list; confirms no new module needed)
- `bench/baseline.json` — all 7 fixtures with step_count/step_count_max
- `bench/eval-qwen35-122b.sh` — lines 259-312 (run_refactor); lines 295-299 (orphan_count logic)
- `bench/run.sh` — line 18 (EXIT trap); gate() verdict logic
- `documentation/qwen35-122b-coding-eval.md` — §2.4, §7, §8, §9, final line
- `.planning/milestones/v2.1-phases/21-empirical-qwen-3-5-122b-coding-evaluation/21-03-SUMMARY.md` — CORR-EVAL-02 trace

**Confidence: HIGH** — all claims verifiable by re-reading cited lines.

---

## Metadata

**Confidence breakdown:**
- Ceiling value (10): HIGH — arithmetic from 21-03 trace (4 reads + 3 edits = 7 minimum)
- Design choice (Option 1): HIGH — scope discipline + intentional decoupling documented in existing code
- Regression risk: HIGH — baseline.json provides exact thresholds; W1/W2 code-enforced safe
- Prompt wording: MEDIUM — wording that prevents T6 expansion is the judgment call; 22-03 gate verifies it
- Test interactions: HIGH — existing "more than 5 steps" test will fail and must be updated
- Eval doc edits: HIGH — exact line numbers identified for all 5 edit sites

**Research date:** 2026-04-28
**Valid until:** 2026-05-28 (stable domain; no fast-moving dependencies)
