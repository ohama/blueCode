---
phase: 01-foundation
plan: 02
subsystem: domain-types
tags: [fsharp, discriminated-unions, expecto, domain-modeling, routing, pure-functions]

# Dependency graph
requires:
  - phase: 01-01
    provides: BlueCode.Core/BlueCode.Tests project scaffold with FsToolkit.ErrorHandling and Expecto NuGet packages
provides:
  - All 8 Phase 1 DUs defined in Domain.fs (AgentState, Intent, Model, Tool, LlmOutput, AgentError, Step, ToolResult)
  - Supporting single-case DUs (FilePath, Command, Timeout, Thought, ToolName, ToolInput, ToolOutput, StepStatus)
  - AgentResult record
  - Endpoint DU (Port8000, Port8001)
  - Router pure functions: classifyIntent, intentToModel, modelToEndpoint, endpointToUrl
  - 16-case Expecto test suite for all routing logic
  - SC2 empirical proof (FS0025 fires on incomplete Intent match)
  - SC3 tests pass (classifyIntent "fix the null check" -> Debug, intentToModel Debug -> Qwen72B)
affects:
  - 01-03 (ContextBuffer.fs and ToolRegistry.fs reference Domain DUs; second SC2 proof against ToolResult)
  - 02-llm-client (consumes endpointToUrl, LlmOutput, AgentError)
  - 03-tools (consumes Tool, ToolResult, FilePath, Command, Timeout, ToolOutput DUs)
  - 04-agent-loop (consumes AgentState, Step, AgentResult, all DUs)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pure routing pipeline: classifyIntent -> intentToModel -> modelToEndpoint -> endpointToUrl (zero IO, zero mutation)"
    - "Exhaustive DU match enforced structurally — no wildcard arms anywhere in Router.fs"
    - "Domain compile order: Intent/Model/Endpoint -> primitives -> Tool/ToolResult -> LlmOutput -> AgentError -> Step -> AgentState -> AgentResult"
    - "Single-case DUs for primitive wrapping (FilePath, Command, Timeout) to prevent misshapen inputs"

key-files:
  created: []
  modified:
    - src/BlueCode.Core/Domain.fs
    - src/BlueCode.Core/Router.fs
    - tests/BlueCode.Tests/RouterTests.fs

key-decisions:
  - "ToolResult DU shape ships in Phase 1 (FND-02 expanded) alongside other 7 DUs so Tool is exhaustively matchable from day one; full semantic contract deferred to Phase 3 TOOL-07"
  - "Timeout (ms) in Tool.RunShell vs Timeout (seconds) in ToolResult intentionally different — former matches .NET APIs, latter is user-facing; not unified"
  - "classifyIntent keyword priority order: Debug > Design > Analysis > Implementation > General (first match wins)"
  - "Korean keywords included in classifyIntent: 구조/설계 for Design, 분석 for Analysis"
  - "FS0025 fires as warning (not hard error) in default .NET 10 build — the invariant is live and detectable; Plan 01-03 can add TreatWarningsAsErrors if needed"

patterns-established:
  - "DU-first domain modeling: every domain concept is a typed DU case, not a string or int"
  - "No wildcard arms in exhaustive matches — structural enforcement of case completeness"
  - "Bilingual keyword scanning (English + Korean) in classifyIntent"

# Metrics
duration: 7min
completed: 2026-04-22
---

# Phase 1 Plan 02: Domain DUs + Router Pure Functions + Expecto Tests Summary

**8 F# discriminated unions (including ToolResult shape) plus bilingual routing pipeline classifyIntent/intentToModel/modelToEndpoint/endpointToUrl with 16-case Expecto test suite; SC2 empirical proof (FS0025) and SC3 assertions pass**

## Performance

- **Duration:** ~7 min
- **Started:** 2026-04-22T07:27:42Z
- **Completed:** 2026-04-22T07:34:30Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments

- Domain.fs populated with all 8 Phase 1 DUs (AgentState, Intent, Model, Tool, LlmOutput, AgentError, Step, ToolResult) plus 9 supporting types; 132 lines, correct compile order
- Router.fs implemented with 4 pure functions in 39 lines: classifyIntent (bilingual keyword scan), intentToModel, modelToEndpoint, endpointToUrl — all exhaustive matches, no wildcards
- 16 Expecto tests pass (exit 0); SC3 verbatim assertions verified: `classifyIntent "fix the null check" = Debug`, `intentToModel Debug = Qwen72B`
- SC2 empirically proved: FS0025 fires on incomplete Intent match in temporary `_CompileCheck.fs`; file cleaned up and fsproj restored to 3-entry state

## Task Commits

Each task was committed atomically:

1. **Task 1: Populate Domain.fs with all 8 DUs** - `9d2e718` (feat)
2. **Task 2: Implement Router.fs and RouterTests.fs** - `e6e1f7a` (feat)
3. **Task 3: SC2 empirical proof** - (no permanent file changes; cleanup-only task)

## Files Created/Modified

- `src/BlueCode.Core/Domain.fs` - All 8 Phase 1 DUs and 9 supporting types (132 lines)
- `src/BlueCode.Core/Router.fs` - 4 pure routing functions (39 lines)
- `tests/BlueCode.Tests/RouterTests.fs` - 16 Expecto test cases for all routing claims (91 lines)

## Decisions Made

- **ToolResult in Phase 1:** Per FND-02 (revised), ToolResult's DU shape (Success | Failure | SecurityDenied | PathEscapeBlocked | Timeout) ships here alongside the other 7 FND-02 DUs so the Tool DU is exhaustively matchable from day one. The semantic contract (case-generation rules, security chain ordering) completes in Phase 3 as TOOL-07.
- **Timeout naming duality:** `Timeout of int` in `Tool` (milliseconds, matches .NET APIs) vs `ToolResult.Timeout of seconds: int` (user-facing report). Intentionally different units, not unified. Documented in Domain.fs comments.
- **classifyIntent keyword priority:** Debug > Design > Analysis > Implementation > General (first-match-wins). "design the payment system" matches Design on "design" before "system" could trigger a later branch.
- **FS0025 as warning (not error):** Default .NET 10 F# build treats FS0025 as a warning. The invariant is live and detectable. Plan 01-03 can add `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` to BlueCode.Core.fsproj if a hard error is required by SC2 strictness requirements.

## SC2 Empirical Proof — First of Two (Intent DU)

**Exact FS0025 message from build output (Korean locale):**

```
/Users/ohama/projs/blueCode/src/BlueCode.Core/_CompileCheck.fs(8,11): warning FS0025: 이 식의 패턴 일치가 완전하지 않습니다. 예를 들어, 값 'Analysis'은(는) 패턴에 포함되지 않은 케이스를 나타낼 수 있습니다.
```

**English equivalent:**
```
_CompileCheck.fs(8,11): warning FS0025: Incomplete pattern matches on this expression. For example, the value 'Analysis' may indicate a case not covered by the pattern(s).
```

**Proof method:** Created `src/BlueCode.Core/_CompileCheck.fs` with a match expression on `Intent` omitting Analysis/Implementation/General cases. Added `<Compile Include="_CompileCheck.fs" />` to fsproj. Built — FS0025 fired. Removed file and fsproj entry. Rebuilt — 0 warnings, 0 errors.

**Cleanup confirmed:**
- `test ! -f src/BlueCode.Core/_CompileCheck.fs` → true
- `grep -c "<Compile Include" src/BlueCode.Core/BlueCode.Core.fsproj` → 3 (Domain, Router, Ports)

**Note:** This is the FIRST of TWO SC2 proofs. Plan 01-03 Task 3 will perform a second corroborating proof against the `ToolResult` DU. Both FS0025 messages should land in PHASE-SUMMARY.md under SC2 — this proof covers the routing DU (Intent); 01-03's proof covers the downstream tool DU (ToolResult).

## SC3 Test Results

```
16 tests run in 00:00:00.07 for Router.Router – 16 passed, 0 ignored, 0 failed, 0 errored. Success!
```

**Verbatim SC3 assertions verified:**
- `classifyIntent "fix the null check" = Intent.Debug` — PASS
- `intentToModel Intent.Debug = Model.Qwen72B` — PASS

## Domain.fs DU Count

| DU | Cases |
|----|-------|
| Intent | Debug, Design, Analysis, Implementation, General (5) |
| Model | Qwen32B, Qwen72B (2) |
| Endpoint | Port8000, Port8001 (2) |
| Tool | ReadFile, WriteFile, ListDir, RunShell (4) |
| ToolResult | Success, Failure, SecurityDenied, PathEscapeBlocked, Timeout (5) |
| LlmOutput | ToolCall, FinalAnswer (2) |
| AgentError | LlmUnreachable, InvalidJsonOutput, SchemaViolation, UnknownTool, ToolFailure, MaxLoopsExceeded, LoopGuardTripped, UserCancelled (8) |
| AgentState | AwaitingUserInput, PromptingLlm, AwaitingApproval, ExecutingTool, Observing, Complete, MaxLoopsHit, Failed (8) |
| StepStatus | StepSuccess, StepFailed, StepAborted (3) |

Supporting single-case DUs: FilePath, Command, Timeout, Thought, ToolName, ToolInput, ToolOutput

Records: Step, AgentResult

## Router.fs Function Signatures

```fsharp
val classifyIntent  : userInput: string -> Intent
val intentToModel   : Intent -> Model
val modelToEndpoint : Model -> Endpoint
val endpointToUrl   : Endpoint -> string
```

## Deviations from Plan

None - plan executed exactly as written.

The plan specified Tasks 1 and 2 to be committed separately and Task 3 to contain the combined commit. Since the GSD protocol specifies per-task atomic commits, Tasks 1 and 2 were each committed individually (both during execution), which is strictly better. Task 3 had no permanent file changes (SC2 proof was ephemeral).

## Issues Encountered

None. Build was clean throughout. FS0025 fired exactly as expected on the incomplete match.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Domain.fs provides all DU cases Plan 01-03 needs for ContextBuffer.fs (references AgentError, Step, LlmOutput) and ToolRegistry.fs (references Tool, ToolResult, ToolName, ToolOutput)
- Plan 01-03 will insert ContextBuffer.fs and ToolRegistry.fs between Router.fs and Ports.fs in BlueCode.Core.fsproj — the Domain-first, Ports-last invariant is preserved
- Plan 01-03 Task 3 will perform the second SC2 proof against ToolResult; both FS0025 messages go in PHASE-SUMMARY.md
- No blockers for Plan 01-03

---
*Phase: 01-foundation*
*Completed: 2026-04-22*
