---
phase: 22-plan04-ceiling-raise
plan: 04
subsystem: eval
tags: [corr-eval-02, refactor, ceiling-raise, qwen122b, bench, orphan-count, readme-rewrite, comprehension-failure]

# Dependency graph
requires:
  - phase: 22-plan03
    provides: "Bench gate verified 7/7 PASS; tests 284/1/0; 22-04 cleared"
  - phase: 22-plan01
    provides: "PlanValidator.MaxPlanSteps=10; CompositionRoot MaxLoops=10"
provides:
  - "CORR-EVAL-02 re-run result (v2.2 attempt 1): FAIL (orphan_count=1) — comprehension failure, not ceiling issue"
  - "CORR-EVAL-02 re-run result (v2.2 attempt 2): FAIL (orphan_count=1) — comprehension failure PERSISTS despite README rewrite"
  - "Root-cause diagnosis confirmed: model has persistent add3→sum3 extraction bias; README clarity insufficient"
  - "README rewrite (Option A) attempted and FAILED: agent read new README (2128 chars) but produced identical step-5 miscomprehension"
  - "Bench gate 7/7 PASS preserved both times; fixtures restored by EXIT trap"
  - "STATE.md updated with double-FAIL diagnosis and Phase 22 completion note"
affects: [23-coldstart-optional, future-v2.2-candidates]

# Tech tracking
tech-stack:
  added: []
  patterns: []

key-files:
  created:
    - .planning/phases/22-plan04-ceiling-raise/22-04-SUMMARY.md
    - bench/fixtures/refactor_multifile/README.md (rewritten — committed, did not fix comprehension)
  modified:
    - .planning/STATE.md
    - .planning/phases/22-plan04-ceiling-raise/22-02-PLAN.md (recovered missing plan-meta file)

key-decisions:
  - "CORR-EVAL-02 FAIL (orphan_count=1) despite 10-step ceiling — do NOT update eval doc verdict"
  - "Root cause: model has a persistent add3→sum3 extraction bias that README rewriting did not fix"
  - "README Option A attempted: rewritten to enumerate both renames explicitly with numbered sections, checklist, and warning. Agent still produced identical step-5 miscomprehension."
  - "Agent used 8/10 steps both times, had budget; missed base add→sum rename despite explicit README instruction"
  - "Eval doc stays at 82/100 (KEEP verdict unchanged — CORR-EVAL-02 was 0/5 in v2.1, remains 0/5)"
  - "Bench gate 7/7 PASS confirmed both times; EXIT trap restored fixtures; baseline.json unchanged"
  - "Phase 22 delivers ceiling raise (22-01..22-03 complete) but CORR-EVAL-02 PASS is unachieved after two attempts"
  - "Next diagnostic path: fixture redesign (rename only add3, not add, to reduce fixture complexity) or system prompt multi-file refactor guidance"

patterns-established: []

# Metrics
duration: 7min (attempt 1) + ~10min (attempt 2 recovery)
completed: 2026-04-28
---

# Phase 22 Plan 04: Re-evaluation (CORR-EVAL-02) Summary

**CORR-EVAL-02 re-run with 10-step ceiling produced FAIL (orphan_count=1): root cause is model comprehension failure (agent mis-summarized README, planning add3→sum3 only, ignoring the base add→sum rename), not a ceiling constraint**

## Performance

- **Duration:** ~7 min
- **Started:** 2026-04-28T04:42:27Z
- **Completed:** 2026-04-28T04:49:30Z
- **Tasks:** 1 of 3 (Task 1 terminated on FAIL; Tasks 2+3 eval-doc updates not applied per plan spec)
- **Files modified:** 1 (.planning/STATE.md only)

## CORR-EVAL-02 Result

**Verdict: FAIL**

- **LOG_DIR:** `bench/runs/qwen35-eval-20260428-134438/`
- **orphan_count:** 1 (from `refactor_orphan_count.txt`)
- **Agent wall-clock:** 35s (8 steps, well within 10-step budget)
- **Harness exit:** 0 (agent completed cleanly; non-zero only on MaxLoopsExceeded — did not occur)

### Agent step trace

| Step | Action | Result |
|------|--------|--------|
| 1 | read_file README.md | Success (902 chars) |
| 2 | read_file Calculator.fs | Success (308 chars) |
| 3 | read_file Main.fs | Success (275 chars) |
| 4 | read_file Tests.fs | Success (495 chars) |
| 5 | edit_file Calculator.fs (add3→sum3 only) | Success |
| 6 | edit_file Main.fs (add3→sum3 refs) | Success |
| 7 | edit_file Tests.fs (add3→sum3 refs) | Success |
| 8 | final: "Refactor complete: Renamed function 'add3' to 'sum3'" | — |

**Steps used:** 8/10. Agent had 2 steps of unused budget.

**MaxLoopsExceeded:** NO — agent declared completion at step 8.

### Root cause analysis

The agent's step-5 thought reveals a comprehension failure:

```
thought: Now I have all the information needed. The README.md asks to:
1. Rename 'add3' to 'sum3' in Calculator.fs
2. Update all references to 'add3' to 'sum3' in Main.fs and Tests.fs
```

The README actually says: **"Rename `add` to `sum` everywhere"** — both the base `add` function AND `add3`. The spec lists in the README:

- `Calculator.fs` defines `sum` (and `add3` should now be `sum3`, calling `sum`).
- `Main.fs` calls `Calculator.sum` and `Calculator.sum3`.
- `Tests.fs` calls `sum` / `sum3` and prints `testSum: PASS` / `testSum3: PASS`.

The agent correctly understood only the `add3` → `sum3` portion. It missed:
1. `let add` → `let sum` in Calculator.fs
2. `add 2 3` → `sum 2 3` in Main.fs
3. `let actual = add 2 3` → `let actual = sum 2 3` in Tests.fs
4. `testAdd` → `testSum` function rename in Tests.fs

**Post-refactor file state (captured before gate restored fixtures):**

- Calculator.fs: `let add` still present (1 orphan); `let sum3` renamed (correct)
- Main.fs: `add 2 3` still present; `sum3 1 2 3` renamed (correct)
- Tests.fs: `let testAdd ()` still present; `sum3 1 2 3` renamed (correct)

**orphan_count=1** (the harness checks `\b(let |Calculator\.)add\b` — finds `let add` in Calculator.fs)

### Comparison to v2.1 FAIL

The v2.1 FAIL transcript (5-step budget exhausted) and v2.2 FAIL transcript (8-step budget used) show an identical step-5 thought — the same miscomprehension of the README. This confirms:

- **v2.1 hypothesis was wrong:** The RESEARCH.md diagnosed the v2.1 FAIL as "agent exhausted 5-step budget before reaching Main.fs and Tests.fs." This was partially true (the edits didn't land in v2.1), but the underlying cause was always comprehension — the agent was planning to do `add3`→`sum3` only.
- **Ceiling raise was necessary but not sufficient:** With 5 steps, the task was physically impossible (4 reads + 3 edits = 7 minimum). With 10 steps, the task is possible, but the agent's mis-reading of the README means it still fails.
- **The agent needs either:** (a) a clearer README prompt that makes `add`→`sum` the prominent first point, OR (b) a system prompt hint for multi-file refactors to enumerate all rename targets explicitly, OR (c) a fixture README rewrite that leads with `add`→`sum` before mentioning `add3`→`sum3`.

## Pre-flight and Gate Results

**Pre-flight fixture reset:** PASS — `git checkout -- bench/fixtures/refactor_multifile/` confirmed clean; `let add` and `let add3` present in canonical state.

**Service:** 122B responsive at localhost:8001.

**Bench gate (mandatory final):** GATE PASS (7/7)

| Label | Steps | Exit |
|-------|-------|------|
| T6_122b | 5/5 | 0 |
| W1_122b | 3/3 | 0 |
| W2_122b | 3/3 | 0 |
| T1_122b | 1/3 | 0 |
| T5_122b | 3/4 | 0 |
| B2_122b | 2/3 | 0 |
| MT_122b | 2/4 | 0 |

**Fixture restore:** Confirmed — `git diff bench/fixtures/refactor_multifile/` empty after gate. `let add` references present (canonical state restored by EXIT trap).

**baseline.json:** Unchanged (`git diff bench/baseline.json` empty).

**src/:** Unchanged (`git diff src/` empty).

## Eval Doc Status

Per plan: eval doc NOT updated (CORR-EVAL-02 FAIL; do not fake a PASS verdict).

`documentation/qwen35-122b-coding-eval.md` **remains at 82/100, KEEP**.

The §2.4 verdict (`FAIL — orphan_count=1 (5-step budget exhausted)`) is now factually partially inaccurate (the 5-step budget text is wrong for v2.2 — it was a 10-step run), but the FAIL verdict and 0/5 score are correct. A future plan could update §2.4 to document the v2.2 re-run as a second data point while preserving the 0/5 score.

## Decisions Made

1. **Do NOT update eval doc:** CORR-EVAL-02 FAIL confirmed — orphan_count=1. Plan spec is explicit: "do NOT update eval doc to PASS."
2. **Root cause is comprehension, not ceiling:** The ceiling raise (5→10) was necessary to make the task physically possible, but not sufficient to make the agent complete it correctly. Two separate issues were always present.
3. **Phase 22 closes with 22-01..22-03 delivered:** The ceiling raise itself (MaxPlanSteps=10, MaxLoops=10, prompt update, test coverage) is correct and valuable. The CORR-EVAL-02 PASS was the hoped-for confirmation but the data does not support it.
4. **Next diagnostic path:** README fixture rewrite or system prompt addition that explicitly lists all rename targets. This is out of scope for Phase 22 but is a clear v2.2 / Phase 23 candidate if pursued.

## Deviations from Plan

### Deviation: CORR-EVAL-02 FAIL — early stop per plan spec

- **Found during:** Task 1 (CORR-EVAL-02 run)
- **Result:** orphan_count=1, same as v2.1
- **Action:** Stopped per plan spec: "If `--refactor` produces orphan_count > 0 (CORR-EVAL-02 FAIL despite ceiling raise), STOP. Do NOT update eval doc to PASS."
- **Tasks skipped:** Task 2 (eval doc update), Task 3 (STATE.md Phase 22 complete + gate — gate still run for fixture restore)
- **Impact:** eval doc remains at 82/100; STATE.md updated with FAIL diagnosis instead of PASS confirmation

The plan anticipated this outcome and provided explicit instructions. This is not an error in execution.

## Issues Encountered

**Unexpected:** The RESEARCH.md hypothesis ("ceiling was the sole constraint") was shown to be incomplete. The agent had 2 unused steps (8/10) when it declared success — proving ceiling was not the limiting factor. The v2.1 and v2.2 agent thoughts are textually identical at step 5, confirming the comprehension failure predates the ceiling raise.

## Phase 22 Summary

Plans 22-01, 22-02, 22-03 all delivered as designed:
- PlanValidator.MaxPlanSteps raised from 5 to 10
- CompositionRoot bootstrap MaxLoops raised from 5 to 10
- System prompt updated with 10-step guidance clause
- User-visible strings updated (retry msg, render error, plan suffix)
- 3 boundary tests added (284/1/0 total)
- Bench gate held at 7/7 PASS throughout

Plan 22-04 (CORR-EVAL-02 re-run): FAIL. The ceiling raise was a necessary prerequisite but the multi-file refactor PASS requires additionally fixing the README prompt comprehension gap (or adjusting the fixture/prompt).

**Phase 22 SC5 (CORR-EVAL-02 PASS; orphan_count=0):** NOT MET.
**Phase 22 SC6 (eval doc updated to PASS):** NOT MET.
**Phase 22 SC1-SC4 (ceiling constants, prompt, tests, gate):** MET.

## Next Phase Readiness

- **Phase 23 (optional cold-start):** Separate phase, user opt-in required. Unaffected by CORR-EVAL-02 result.
- **CORR-EVAL-02 follow-up options (post-double-FAIL):**
  1. ~~Rewrite `bench/fixtures/refactor_multifile/README.md`~~ — **ATTEMPTED AND FAILED** (Option A, 2026-04-28 recovery run). README was rewritten to enumerate both renames with numbered sections, checklist, and explicit warning. Agent still produced identical step-5 miscomprehension.
  2. Add system prompt guidance for multi-file refactors (enumerate all rename targets explicitly) — NOT yet tried
  3. Redesign the fixture to make the task unambiguous at the code level (e.g., rename only `add3`; or use a fixture where the README structure prevents misreading)
  4. Accept 82/100 verdict as final and close v2.2 without CORR-EVAL-02 PASS
- User opt-in required before proceeding with any remaining options.

---

## Recovery Section: README Rewrite Attempt (2026-04-28)

**Option A: README rewrite. Result: FAIL (orphan_count=1). Second consecutive FAIL.**

### What was attempted

User chose Option A from the CORR-EVAL-02 FAIL options above. The orchestrator rewrote `bench/fixtures/refactor_multifile/README.md` to:
- Lead with "**two** functions that need to be renamed" with explicit numbered list
- Add separate `### Rename 1: add → sum` and `### Rename 2: add3 → sum3` sections with file-by-file detail
- Add explicit completion checklist with grep verification commands
- Close with: "Both renames are required. Completing only one (e.g., only add3 → sum3) leaves orphan references and is a FAIL."

The new README grew from 902 chars (old) to 2128 chars (new).

### Recovery run result

**LOG_DIR:** `bench/runs/qwen35-eval-20260428-140714/`
**orphan_count:** 1 (from `refactor_orphan_count.txt`)
**Agent wall-clock:** 35s (8 steps, same as attempt 1)
**Harness exit:** 0

### Agent step trace (second attempt)

| Step | Action | Result |
|------|--------|--------|
| 1 | read_file README.md | Success (2128 chars — new README) |
| 2 | read_file Calculator.fs | Success (308 chars) |
| 3 | read_file Main.fs | Success (275 chars) |
| 4 | read_file Tests.fs | Success (495 chars) |
| 5 | edit_file Calculator.fs (add3→sum3 only — same miscomprehension) | Success |
| 6 | edit_file Main.fs (add3→sum3 refs only) | Success |
| 7 | edit_file Tests.fs (add3→sum3 refs only) | Success |
| 8 | final: "Refactor complete: Renamed function 'add3' to 'sum3'" | — |

**Steps used:** 8/10. Agent had 2 steps of unused budget. Identical pattern to attempt 1.

### Step-5 thought (second attempt — verbatim)

```
thought: Now I have all the information needed. Based on the README.md, I need to:
1. Rename `add3` to `sum3` in Calculator.fs
2. Update all references to `add3` to `sum3` in Main.fs and Tests.fs

Let me start by editing Calculator.fs to rename the function.
```

**This is textually identical to the attempt-1 step-5 thought.** The agent read the new README (confirmed by "2128 chars" result token) but produced the same extraction error — omitting the `add → sum` rename entirely despite the explicit Rename 1 section.

### Post-refactor file state (attempt 2)

- `Calculator.fs`: `let add` still present (orphan); `let sum3` renamed (correct); `sum3` body still calls `add` (also correct for the partial rename, but `add` is the orphan)
- `Main.fs`: `add 2 3` still present; `sum3 1 2 3` renamed (correct)
- `Tests.fs`: `testAdd ()` and `add 2 3` still present; `sum3 1 2 3` renamed (correct)

### Hypothesis update: persistent extraction bias

The model appears to have a **persistent extraction bias** toward `add3 → sum3`. After two independent runs with different README content (attempt 1: prose README; attempt 2: explicit enumerated README with warning), the step-5 thought is textually identical. This suggests:

1. The bias is not a reading comprehension failure that better README prose can fix.
2. The model may be pattern-matching to `add3` as the "obviously renamed" function (its name contains `add` + a number suffix, the classic rename signal).
3. The base `add` function name may be treated by the model as "canonical" (it appears everywhere and its name is simple) and not a rename target.
4. Future diagnostic path: system prompt guidance specifying "when performing multi-file refactors, enumerate ALL rename targets from the spec before editing any file" — which would force explicit planning of both renames before action.

### Bench gate (post-recovery run)

**Bench gate result:** GATE PASS (7/7)

| Label | Steps | Exit |
|-------|-------|------|
| T6_122b | 5/5 | 0 |
| W1_122b | 3/3 | 0 |
| W2_122b | 3/3 | 0 |
| T1_122b | 1/3 | 0 |
| T5_122b | 3/4 | 0 |
| B2_122b | 2/3 | 0 |
| MT_122b | 2/4 | 0 |

Fixtures restored by EXIT trap. `git status bench/fixtures/refactor_multifile/` is clean.

### Recovery commits

| Hash | Message |
|------|---------|
| b420f92 | chore(22-04): rewrite refactor_multifile README to enumerate both rename targets explicitly |
| 09d2b4c | docs(22-02): commit missing 22-02-PLAN.md (orchestrator correction) |

Eval doc NOT updated (CORR-EVAL-02 FAIL). State updated with double-FAIL diagnosis.

### Final Phase 22 status

**CORR-EVAL-02:** FAIL (two attempts). Eval doc remains at 82/100, KEEP.
**Ceiling raise (22-01..22-03):** Complete and correct — MaxPlanSteps=10, MaxLoops=10, prompt updated, tests 284/1/0.
**v2.2 milestone:** Ceiling raise delivered; CORR-EVAL-02 unresolved after Option A attempt. Next resolution path is system prompt guidance or fixture redesign.

## Commits

| Hash | Message |
|------|---------|
| (none — no code changes; eval run artifacts gitignored) | — |
| (STATE.md + SUMMARY.md only — plan-meta commit) | docs(22-04): complete re-evaluation plan — CORR-EVAL-02 FAIL, comprehension root cause |
| b420f92 | chore(22-04): rewrite refactor_multifile README to enumerate both rename targets explicitly |
| 09d2b4c | docs(22-02): commit missing 22-02-PLAN.md (orchestrator correction) |

---
*Phase: 22-plan04-ceiling-raise*
*Completed: 2026-04-28*
