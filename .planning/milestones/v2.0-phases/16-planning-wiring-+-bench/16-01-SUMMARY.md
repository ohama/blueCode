---
phase: 16-planning-wiring-+-bench
plan: 01
subsystem: planning
tags: [plan-parse, agent-loop, retry, wire-types, json-schema]

# Dependency graph
requires:
  - phase: 14-domain-+-plan-validator
    provides: Domain.Plan, Domain.PlannedStep, Domain.LlmOutput.Plan, PlanValidator.validatePlan
  - phase: 20-qwen35-protocol-alignment
    provides: Role=User invariant for mid-conversation injections (Phase 20-03 probe REJECT verdict)
provides:
  - plan-mode-entry: runPlanTurn function in AgentLoop.fs with 2-attempt retry
  - plan-wire-parse: toLlmOutput Plan branch via PlanWire/PlannedStepWire intermediate records
  - json-schema-plan: llmStepSchema action enum extended to 9 values including "plan"
  - plan-parse-tests: PlanParseTests.fs covering wire parse round-trip + runPlanTurn retry paths
affects:
  - 16-02 (PlanGate.fs, --plan Argu flag — calls runPlanTurn as entry point)
  - 16-03 (bench MT_122b fixture — exercises plan-mode invocation)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - PlanWire/PlannedStepWire intermediate records (same rationale as LlmStep: plain records round-trip cleanly via System.Text.Json; DUs would serialize as {"Item":"..."})
    - deserializePlanWire private helper (structural-only at adapter layer; PlanValidator handles semantic rules in runPlanTurn)
    - runPlanTurn 2-attempt pattern mirrors callLlmWithRetry; correction messages use Role=User with text-marker authority ([PLAN PARSE ERROR] / [PLAN INVALID])
    - systemPromptSuffix parameterized so Core stays string-literal-free for plan-mode prompt content

key-files:
  created:
    - tests/BlueCode.Tests/PlanParseTests.fs
  modified:
    - src/BlueCode.Cli/Adapters/LlmWire.fs
    - src/BlueCode.Cli/Adapters/Json.fs
    - src/BlueCode.Cli/Adapters/QwenHttpClient.fs
    - src/BlueCode.Core/AgentLoop.fs
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs

key-decisions:
  - "_raw passthrough for plan-step inputs: consistent with existing ToolCall path (QwenHttpClient.fs:209-210); per-tool input shapes are dispatch-time concerns, not plan-parse-time concerns"
  - "2 attempts total (1 initial + 1 retry): matches callLlmWithRetry pattern; 3 attempts would add latency for a mode that is inherently interactive (user reviews plan before execution)"
  - "systemPromptSuffix is a parameter in runPlanTurn (not a hardcoded literal): Core stays string-literal-free for plan-mode prompts; Plan 16-02 wires the actual suffix from CompositionRoot"
  - "PlanValidator not called inside toLlmOutput: schema-invalid steps (SchemaViolation) and structural rule violations (PlanInvalid) have distinct error variants; separation enables per-error retry messaging in runPlanTurn"
  - "Mid-loop Plan guard at AgentLoop.fs:408 preserved: runLoop receiving Plan output outside plan-mode is still an error; runPlanTurn is the ONLY legitimate Plan-output entry point"
  - "buildMessages Plan _ placeholder at AgentLoop.fs:224 preserved: Plans do not go in past assistant turns in multi-turn context"
  - "Role=User for runPlanTurn correction messages: Phase 20-03 probe REJECT verdict (122B HTTP 404 on mid-conversation Role=System); authority signal is the [PLAN PARSE ERROR] / [PLAN INVALID] text marker"

patterns-established:
  - "Wire intermediate records (PlanWire, PlannedStepWire) for domain types that use single-case DUs: DUs do not round-trip via System.Text.Json; wire records map to domain in the adapter layer"
  - "2-attempt retry in runPlanTurn: retryable errors (InvalidJsonOutput, SchemaViolation, PlanInvalid); non-retryable (LlmUnreachable, UserCancelled, PathRetired) short-circuit immediately"

# Metrics
duration: ~25min
completed: 2026-04-27
---

# Phase 16 Plan 01: Plan JSON Parse + runPlanTurn + 2-Retry Summary

**End-to-end Plan wire parse layer: LlmWire PlanWire records, llmStepSchema extended to 9 actions, toLlmOutput Plan branch, and runPlanTurn entry point with 2-attempt retry returning Result<Plan, AgentError>**

## Performance

- **Duration:** ~25 min
- **Started:** 2026-04-27
- **Completed:** 2026-04-27
- **Tasks:** 3/3
- **Files modified:** 6 + 1 created

## Accomplishments

- Wire layer extended: `LlmWire.fs` adds `PlanWire` + `PlannedStepWire` records; `Json.fs` `llmStepSchema` action enum extended from 8 to 9 values (added `"plan"`); `QwenHttpClient.toLlmOutput` gains 3-branch match with Plan branch via `deserializePlanWire`
- `runPlanTurn` entry point in `AgentLoop.fs`: `AgentConfig -> ILlmClient -> Model -> Step list -> string -> string -> CancellationToken -> Task<Result<Plan, AgentError>>` — 2 attempts (1 initial + 1 retry) on `InvalidJsonOutput` / `SchemaViolation` / `PlanInvalid`; `LlmUnreachable` / `UserCancelled` / `PathRetired` short-circuit; correction messages use `Role = User` (Phase 20-03 invariant)
- `PlanParseTests.fs` (new): 8 test cases — 3 wire-layer round-trip + 5 runPlanTurn (happy path, PlanInvalid retry recovery, retry exhaustion, LlmUnreachable non-retry, FinalAnswer-in-plan-mode retry)
- Test count: 266/1/0 → **274/1/0** (+8); bench gate: **6/6 PASS**

## Task Commits

1. **Task 1: Extend wire layer + Json.fs schema** - `6f8cd57` (feat)
2. **Task 2: Add runPlanTurn entry point** - `d889c68` (feat)
3. **Task 3: PlanParseTests.fs** - `2ac9ac9` (test)

## Files Created/Modified

- `src/BlueCode.Cli/Adapters/LlmWire.fs` — Added `PlannedStepWire` + `PlanWire` records (public, for tests; System.Text.Json round-trip safe)
- `src/BlueCode.Cli/Adapters/Json.fs` — `llmStepSchema` action enum: 8 → 9 values (added `"plan"`)
- `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` — Added `deserializePlanWire` (private) + Plan branch in `toLlmOutput`
- `src/BlueCode.Core/AgentLoop.fs` — Added `open BlueCode.Core.PlanValidator` + `runPlanTurn` (module-level public, after `runSession`)
- `tests/BlueCode.Tests/PlanParseTests.fs` — NEW: 8 test cases, module `BlueCode.Tests.PlanParseTests`, public `tests` testList
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj` — `PlanParseTests.fs` added after `PlanValidatorTests.fs`, before `AgentLoopTests.fs` (22 `<Compile Include>` entries total)
- `tests/BlueCode.Tests/RouterTests.fs` — `PlanParseTests.tests` added to `rootTests` after `PlanValidatorTests.tests` (22 rootTests entries total)

## Decisions Made

- **`_raw` passthrough for plan-step inputs:** Consistent with existing `ToolCall` path (QwenHttpClient.fs line 209-210). Per-tool input shapes validated at dispatch time, not at plan-parse time. `PlannedStep.Input = ToolInput(Map.ofList [("_raw", s.input.GetRawText())])`.
- **2 attempts total (1 initial + 1 retry):** Mirrors `callLlmWithRetry` at AgentLoop.fs:153. 3 attempts not warranted for an interactive mode where the user reviews the plan before any execution happens.
- **`systemPromptSuffix` parameterized:** `runPlanTurn` takes the suffix as a `string` parameter. Core stays string-literal-free for plan-mode prompt content. Plan 16-02 wires the actual suffix from `CompositionRoot`.
- **`PlanValidator.validatePlan` not called inside `toLlmOutput`:** Separation ensures `SchemaViolation` (parse shape) and `PlanInvalid` (structural rules) remain distinct error variants for per-error retry messaging in `runPlanTurn`.
- **Mid-loop Plan guard preserved at line 408:** `| Ok { Output = Plan _ } -> Error(PlanInvalid "Plan output received outside plan-mode")` stays — `runLoop` should never see Plan output outside plan-mode.
- **`buildMessages` `| Plan _ -> "{}"` placeholder preserved at line 224:** Plans do not go in past assistant turns in multi-turn context.
- **`Role = User` for correction messages:** Phase 20-03 probe REJECT verdict (2026-04-27): 122B returns HTTP 404 on mid-conversation `Role = System`. Authority signal is the `[PLAN PARSE ERROR]` / `[PLAN INVALID]` text marker. Consistent with all other loop-injection messages in `buildMessages`.

## runPlanTurn Signature

```fsharp
let runPlanTurn
    (config: AgentConfig)
    (client: ILlmClient)
    (model: Model)
    (priorSteps: Step list)
    (userInput: string)
    (systemPromptSuffix: string)
    (ct: CancellationToken)
    : Task<Result<Plan, AgentError>>
```

**Retry path semantics:**
- Attempt 1: `client.CompleteAsync baseMessages model ct`
  - `Ok response` → `extractAndValidate response` → if `Ok plan`, return immediately
  - `Ok response` → `extractAndValidate response` → if `Error e1`, build correction, retry
  - `Error LlmUnreachable/UserCancelled/PathRetired` → return immediately (non-retryable)
  - `Error InvalidJsonOutput/SchemaViolation` → build correction, retry
- Attempt 2: `client.CompleteAsync messages2 model ct`
  - `Ok response2` → `extractAndValidate response2` (returned regardless of Ok/Error)
  - `Error e` → return that error to caller
- Total: **2 LLM calls maximum** (1 initial + 1 retry on parse/validation failure)

## Preserved Invariants

- Mid-loop Plan guard at AgentLoop.fs:408: `Error(PlanInvalid "Plan output received outside plan-mode")` — unchanged
- `buildMessages` Plan placeholder at AgentLoop.fs:224: `| Plan _ -> "{}"` — unchanged
- `runSession` (lines 422-441) — unchanged
- Phase 14 files: `Domain.fs`, `PlanValidator.fs` — not modified
- Phase 19/20 files: `Router.fs`, sampling params, `extractContent` fallback, Role=User invariant — not modified

## Deviations from Plan

None — plan executed exactly as written.

One minor difference: test count landed at **274** (not 273 as predicted). The 8 test cases include 5 `runPlanTurn` tests vs. the plan's "≥4" estimate. Extra test: "FinalAnswer in plan-mode triggers retry + recovery" (distinct from the retry-exhaustion case). Within the ±2 tolerance specified in the plan.

## Issues Encountered

None. Build succeeded with 0 warnings on all three tasks. The `PlanWire` deserialization via `Unchecked.defaultof<PlanWire>` null check handles the case where `JsonSerializer.Deserialize<PlanWire>` returns the default struct/record on missing fields rather than throwing.

## Bench Gate Result

```
PASS T6_122b    steps=5/5 exit=0
PASS W1_122b    steps=3/3 exit=0
PASS W2_122b    steps=3/3 exit=0
PASS T1_122b    steps=1/3 exit=0
PASS T5_122b    steps=3/4 exit=0
PASS B2_122b    steps=2/3 exit=0
GATE PASS (6/6)
```

## Next Phase Readiness

- `runPlanTurn` is ready to call from `Program.fs` / `Repl.fs` (Phase 16-02)
- Phase 16-02 needs to: add `--plan` Argu flag, wire `runPlanTurn` with actual `systemPromptSuffix` string from `CompositionRoot`, implement `PlanGate.fs` for user accept/reject/edit/quit flow
- Phase 16-03 adds `MT_122b` bench fixture for multi-turn regression

---
*Phase: 16-planning-wiring-+-bench*
*Completed: 2026-04-27*
