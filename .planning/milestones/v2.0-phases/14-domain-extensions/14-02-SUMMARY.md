---
phase: 14-domain-extensions
plan: 02
subsystem: testing
tags: [fsharp, plan-validation, pure-function, expecto, domain, PLAN-04]

# Dependency graph
requires:
  - phase: 14-01
    provides: Domain.fs Plan/PlannedStep/SessionId types + LlmOutput.Plan variant + AgentError.PlanInvalid + ISessionStore port
provides:
  - PlanValidator.validatePlan: Plan -> Result<Plan, AgentError> — pure function enforcing 3 structural rules
  - MockHelpers.makePlannedStep + makePlanResponse — test fixture builders for Plan-typed LLM responses
  - PlanValidatorTests: 5 test cases covering all failure modes + happy path
affects:
  - Phase 15 (Persistence wiring) — may use Plan types indirectly via Session
  - Phase 16 (Planning wiring) — calls validatePlan from QwenHttpClient parse layer + retry path

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Pure Core validator pattern: validation logic lives in Core with no I/O, no Serilog/Spectre/Argu
    - Result.bind chain for priority-ordered short-circuit validation (length first, then tool registry, then adjacent dups)
    - makePlannedStep / makePlanResponse builder pattern in MockHelpers for terse Plan fixture construction

key-files:
  created:
    - src/BlueCode.Core/PlanValidator.fs
    - tests/BlueCode.Tests/PlanValidatorTests.fs
  modified:
    - src/BlueCode.Core/BlueCode.Core.fsproj
    - tests/BlueCode.Tests/MockHelpers.fs
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs

key-decisions:
  - "validatePlan is pure: no I/O, no AgentConfig reference — MaxPlanSteps=5 hardcoded in PlanValidator.fs"
  - "Priority order is fixed: length check first (cheap), then tool registry, then adjacent duplicates — stable error code for retry"
  - "knownTools set hardcoded in PlanValidator mirrors AgentLoop.dispatchTool cases; future cycle can derive from richer ToolRegistry"
  - "Schema-invalid input (per-tool JSON schema) is NOT validated here — deferred to JSON parse time in Cli adapter (Phase 16)"

patterns-established:
  - "PlanValidator.fs position in fsproj: after ToolRegistry.fs, before Ports.fs (Domain first, Ports last invariant)"
  - "Test discovery: BOTH fsproj <Compile Include> AND RouterTests.fs:rootTests required for Expecto to pick up new module"

# Metrics
duration: 15min
completed: 2026-04-26
---

# Phase 14 Plan 02: PlanValidator + Tests Summary

**Pure validatePlan function enforcing 3 structural rules (length, known tools, adjacent dups) with 5 Expecto test cases; test count grows 243 -> 248**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-04-26T23:10Z
- **Completed:** 2026-04-26T23:25Z
- **Tasks:** 3 (2 code tasks + 1 verification/gate)
- **Files modified:** 6

## Accomplishments

- `PlanValidator.fs`: pure Core module with `validatePlan : Plan -> Result<Plan, AgentError>` — 3 rules, priority-ordered, Result.bind chain
- `MockHelpers.fs` extended: `makePlannedStep` + `makePlanResponse` builders reduce fixture noise in Plan tests
- `PlanValidatorTests.fs`: 5 test cases covering full validator surface (1 happy path + 4 PlanInvalid modes)
- Test count: 243/1/0 → 248/1/0 (exactly +5, zero regressions)
- Bench gate: 8/8 PASS (no behavioral regression from Phase 14 domain cascade)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create PlanValidator.fs with pure validatePlan function** - (feat)
2. **Task 2: Extend MockHelpers and create PlanValidatorTests with 5 test cases** - (test)
3. **Task 3: Verify bench gate green and finalize phase** - verification only (no code changes)

**Plan metadata:** (this commit) (docs: complete plan validator plan)

## Files Created/Modified

- `src/BlueCode.Core/PlanValidator.fs` — new module; `validatePlan`, `knownTools` set, `MaxPlanSteps`, 3 private check fns
- `src/BlueCode.Core/BlueCode.Core.fsproj` — added `<Compile Include="PlanValidator.fs" />` between ToolRegistry.fs and Ports.fs
- `tests/BlueCode.Tests/MockHelpers.fs` — added `makePlannedStep` and `makePlanResponse` builders
- `tests/BlueCode.Tests/PlanValidatorTests.fs` — new test module, 5 testCase entries
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj` — registered PlanValidatorTests.fs after MockHelpers.fs, before AgentLoopTests.fs
- `tests/BlueCode.Tests/RouterTests.fs` — added `BlueCode.Tests.PlanValidatorTests.tests` after agentLoopTests in rootTests list

## Validator Surface

Three rules, applied in priority order (short-circuits on first violation):

1. **Length check** (`checkLength`): `plan.Steps.Length > MaxPlanSteps (5)` → `Error(PlanInvalid "plan has N steps, max is 5")`
2. **Tool registry check** (`checkKnownTools`): any step with unknown tool name → `Error(PlanInvalid "unknown tool: <name>")`
3. **Adjacent duplicates** (`checkAdjacentDuplicates`): any two structurally-equal adjacent PlannedStep records → `Error(PlanInvalid "duplicate adjacent steps")`

Composition: `plan |> checkLength |> Result.bind checkKnownTools |> Result.bind checkAdjacentDuplicates`

## Schema-Validation Deferred

Input-schema validation (per-tool JSON schema for step.Input) is NOT in this validator. Per planning context, schema awareness lives in the Cli adapter (LlmWire.fs in Phase 16) and is enforced at JSON parse time. The Plan value reaching `validatePlan` has already passed structural JSON parse. This keeps PlanValidator pure and Core-safe.

## Test Cases Enumerated

| # | Name | Assertion shape |
|---|------|----------------|
| 1 | valid plan: 3 distinct steps with known tools | `Ok p` — `p.Steps.Length = 3` |
| 2 | PlanInvalid: unknown tool name | `Error(PlanInvalid detail)` — detail contains "fabricate_function" or "unknown" |
| 3 | PlanInvalid: more than 5 steps | `Error(PlanInvalid detail)` — detail contains "6" or "max" or "step" |
| 4 | PlanInvalid: duplicate adjacent steps (byte-identical) | `Error(PlanInvalid detail)` — detail contains "duplicate" or "adjacent" |
| 5 | PlanInvalid: 1-step unknown tool (edge case) | `Error(PlanInvalid detail)` — detail contains "summon_demon" or "unknown" |

## Test Discovery

Both required registrations confirmed (CLAUDE.md test-discovery pattern — four prior executors tripped this):

- `BlueCode.Tests.fsproj`: `<Compile Include="PlanValidatorTests.fs" />` after MockHelpers.fs, before AgentLoopTests.fs
- `RouterTests.fs:rootTests`: `BlueCode.Tests.PlanValidatorTests.tests` after agentLoopTests

ToolExpansionTests entries PRESERVED in both fsproj and rootTests (iteration-2 concern verified — not dropped).

## Test Count

- Baseline (14-01): 243/1/0
- This plan: **248/1/0** (+5 passed, 0 regressions)

## Bench Gate

`bench/run.sh --gate` exit code: **0** (8/8 PASS)

Fixtures T6_32b, T6_72b, W1_32b, W2_32b, T1_32b, T5_72b, B2_32b, B2_72b all passed. Phase 14 domain types introduced no behavioral regression.

## Iteration-2 Revision Lessons Applied

- **Docstring fix landed**: `validatePlan` docstring reads "Returns Error on first rule violation; short-circuits in priority order" (not the inverted wording from iteration-1).
- **ToolExpansionTests preserved**: The fsproj and rootTests edits were scoped to insert PlanValidatorTests entries; ToolExpansionTests entries at lines 27 (fsproj) and 98 (RouterTests.fs) remain intact.

## Decisions Made

- `MaxPlanSteps = 5` hardcoded in PlanValidator.fs rather than imported from AgentConfig — validator is invoked pre-AgentConfig in Phase 16 parse layer.
- `knownTools` set is a hardcoded `Set<string>` mirroring AgentLoop.dispatchTool match arms; future cycle can wire via ToolRegistry when that module gains a richer shape.
- Priority order (length → tools → adjacent) chosen to give LLM the most actionable error first: over-long plan is the most common failure; unknown tool is next; adjacent dup is rare edge case.

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

None. The Core purity grep in the plan spec (`grep -E "Serilog|Spectre|Argu|HttpClient|System.IO"`) matched a comment in the docstring referencing "QwenHttpClient" — no actual forbidden imports exist. All `open` statements in PlanValidator.fs are clean.

## Out-of-Scope (Deferred)

**Phase 16:**
- JSON parse layer for `Plan` kind in QwenHttpClient.fs / LlmWire.fs
- Plan retry path in AgentLoop.fs
- `--plan` Argu flag
- bench/baseline.json extension (~8 → ~12 entries)
- Plan table rendering in Spectre

**Phase 15:**
- FileSessionStore adapter (PERSIST-02)
- `--resume` / `--new-session` flags
- CompositionRoot.fs wiring

## Next Phase Readiness

Phase 14 complete. Both plans committed; 248/1/0 test baseline confirmed; bench gate green.

Ready for `/gsd:verify-phase 14` (5 phase Success Criteria green) then Phase 15 (Persistence wiring: ISessionStore adapter, REPL session threading, --resume flag).

---
*Phase: 14-domain-extensions*
*Completed: 2026-04-26*
