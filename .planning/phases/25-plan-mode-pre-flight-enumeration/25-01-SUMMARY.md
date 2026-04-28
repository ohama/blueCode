---
phase: 25-plan-mode-pre-flight-enumeration
plan: 01
subsystem: validation
tags: [fsharp, plan-validator, regex, json, pre-flight, comp-03]

# Dependency graph
requires:
  - phase: 24-prompt-level-intervention
    provides: "P1+P2 system-prompt and few-shot enumeration directives already in planSystemPromptSuffix"
  - phase: 22-plan-mode-ceiling
    provides: "PlanValidator.fs with 3 structural rules (length, knownTools, adjacentDups) and validatePlan entry point"
provides:
  - "checkRenameTargetsEnumerated: fourth PlanValidator pass extracting rename targets from user prompt via regex"
  - "coversTarget: helper checking edit_file old_string for case-insensitive target coverage"
  - "validatePlan signature extended to accept userPrompt: string parameter"
  - "AgentLoop.extractAndValidate passes userInput to validatePlan"
  - "All 6 existing PlanValidatorTests updated to validatePlan \"\" plan (vacuous PASS for new check)"
affects:
  - "25-02-PLAN.md (new test cases for checkRenameTargetsEnumerated)"
  - "25-03-PLAN.md (bench gate verification)"
  - "any future phase modifying PlanValidator.fs or its callers"

# Tech tracking
tech-stack:
  added:
    - "System.Text.Json (JsonDocument.Parse for old_string extraction — already in .NET BCL, no new NuGet)"
    - "System.Text.RegularExpressions (Regex compiled at module load)"
  patterns:
    - "Interpretation B: PlanInvalid detail string carries structured sub-reason without new DU case"
    - "F# big-bang atomic commit pattern: 3 mechanically-coupled files, no valid intermediate build state"
    - "Vacuous PASS: empty userPrompt -> 0 regex matches -> Ok plan (opt-out via empty string)"
    - "Defensive JSON try/with: malformed _raw treated as 'not covered', not as exception"

key-files:
  created: []
  modified:
    - "src/BlueCode.Core/PlanValidator.fs"
    - "src/BlueCode.Core/AgentLoop.fs"
    - "tests/BlueCode.Tests/PlanValidatorTests.fs"

key-decisions:
  - "Interpretation B chosen: no Domain.fs DU change; PlanInvalid case carries structured detail string 'rename targets not enumerated: NAME1, NAME2'"
  - "New check runs LAST in chain (structural rules first, semantic check after)"
  - "Regex requires 2+ char identifier guard to filter single-letter English-prose matches"
  - "Coverage check: case-insensitive substring on edit_file old_string field only (not write_file or other tools)"

patterns-established:
  - "validatePlan userPrompt plan: callers pass userInput from runPlanTurn lexical scope; tests pass empty string for prompt-independent structural tests"
  - "checkRenameTargetsEnumerated uses renamePattern compiled Regex at module load (no per-call allocation)"

# Metrics
duration: ~15min
completed: 2026-04-29
---

# Phase 25 Plan 01: Validator Pre-Flight Pass Summary

**checkRenameTargetsEnumerated added as fourth PlanValidator pass: regex extracts rename targets from user prompt, verifies each covered by edit_file old_string; Interpretation B (no DU change) + F# big-bang atomic commit across 3 files**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-04-29
- **Completed:** 2026-04-29
- **Tasks:** 3 (all atomic — F# big-bang pattern)
- **Files modified:** 3

## Accomplishments

- Added `renamePattern` compiled Regex at module level (case-insensitive, 2+ char identifier guard, no per-call allocation)
- Implemented `coversTarget` helper parsing `_raw` JSON via `JsonDocument` to check `edit_file` `old_string` for case-insensitive target substring
- Implemented `checkRenameTargetsEnumerated` as private function chained LAST in `validatePlan` pipeline
- Extended `validatePlan` signature: `Plan -> Result<...>` to `string -> Plan -> Result<...>` (userPrompt first)
- Updated AgentLoop.fs line 484 call site to `validatePlan userInput p` (captures lexical scope of `runPlanTurn`)
- Updated all 6 existing `PlanValidatorTests` invocations from `validatePlan plan` to `validatePlan "" plan`
- All 284 tests still pass; no test count change (new tests come in 25-02)

## Task Commits

Tasks 1+2+3 are mechanically coupled (F# signature change spans 3 files; no valid intermediate build state). Single atomic commit:

1. **Tasks 1+2+3 (atomic):** `c933ffc` — `feat(25-01): add checkRenameTargetsEnumerated pre-flight pass to PlanValidator`

**Plan metadata:** (this commit — docs(25-01))

## Files Created/Modified

- `src/BlueCode.Core/PlanValidator.fs` — added `renamePattern`, `coversTarget`, `checkRenameTargetsEnumerated`; updated `validatePlan` signature and chain; added opens for `System`, `System.Text.Json`, `System.Text.RegularExpressions`
- `src/BlueCode.Core/AgentLoop.fs` — line 484 only: `validatePlan p` -> `validatePlan userInput p`
- `tests/BlueCode.Tests/PlanValidatorTests.fs` — 6 mechanical replacements of `validatePlan plan` -> `validatePlan "" plan`

## Decisions Made

**Interpretation B (no DU change):** `PlanInvalid of detail: string` in Domain.fs is NOT modified. The new failure reason is encoded as a structured detail string: `"rename targets not enumerated: add, add3"`. This flows through the existing `buildCorrection` arm `| PlanInvalid d -> sprintf "[PLAN INVALID] ... %s ..." d` at AgentLoop.fs:501 verbatim — no special-casing needed. Rationale: avoids compile cascade into Rendering.fs:120 and AgentLoop.fs:501 (both exhaustively match `PlanInvalid d`); smaller diff; same observable LLM-correction behavior.

**New check runs LAST:** Structural rules (length → knownTools → adjacentDups) run first as cheap guards; the semantic regex check runs after structural validation passes. This ensures the LLM gets stable error codes for retry messaging.

**2+ char identifier guard:** The regex `[A-Za-z_]\w+` requires 2+ chars (letter/underscore + at least one `\w`). This filters single-letter English-prose matches like "rename a to b" where "a" is a preposition, not an identifier.

**Vacuous PASS on empty prompt:** `renamePattern.Matches("").Count = 0` → `Ok plan` immediately. This is the correct opt-out path for the 6 existing structural tests (they test prompt-independent rules). Tests use `validatePlan "" plan`.

**Coverage check on edit_file only:** `coversTarget` returns `false` for any tool other than `edit_file`. Only edit operations can enumerate rename targets in `old_string`; write_file, read_file, and other tools are irrelevant to coverage.

## Deviations from Plan

None - plan executed exactly as written. The F# compiler confirmed the expected cascade (Core compiles after Task 1+2; tests compile after Task 3) which matches the plan's intermediate-state description precisely.

## Issues Encountered

None. Core purity check (`bash scripts/check-no-async.sh`) passes. No new NuGet packages required (`System.Text.Json` and `System.Text.RegularExpressions` are BCL). Pre-existing 2 warnings in FsToolExecutor.fs unchanged.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- **25-02 ready:** `validatePlan "" plan` signature is stable. New test cases for `checkRenameTargetsEnumerated` (happy path, missing targets, vacuous PASS edge cases) can be added directly to `PlanValidatorTests.fs` using `validatePlan "rename add to sum; rename add3 to sum3" plan` pattern.
- **25-03 bench gate:** No src/ changes that affect runtime behavior in normal (non-plan) mode. Bench gate expected to remain 7/7 PASS. Gate runs in 25-03 as final verification.
- **Interpretation B invariants preserved:** Domain.fs, Rendering.fs, AgentLoop.fs:501 `buildCorrection` all unchanged.

---
*Phase: 25-plan-mode-pre-flight-enumeration*
*Completed: 2026-04-29*
