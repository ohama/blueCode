---
phase: 08-tool-expansion
plan: 01
subsystem: tools
tags: [fsharp, domain-model, tool-dispatch, json-schema, system-prompt]

# Dependency graph
requires: []
provides:
  - "Tool DU extended with EditFile, GlobSearch, GrepSearch cases"
  - "dispatchTool match arms for edit_file, glob_search, grep_search parsing JSON input"
  - "llmStepSchema enum: 8 values (was 5)"
  - "defaultSystemPrompt: describes 8 actions with input schemas"
  - "FsToolExecutor.create: 7 match arms (4 real impls + 3 failwith stubs)"
  - "LlmPipelineTests and CompositionRootTests updated to cover 8-action enum"
affects:
  - "08-02"

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Stub strategy: failwith placeholders in FsToolExecutor close exhaustive-match compile error while deferring real impl to plan 08-02"
    - "Shared-seam batching: all 5 wiring points (Domain, AgentLoop, FsToolExecutor, Json, CompositionRoot) updated in one plan to keep project compilable at every commit"

key-files:
  created: []
  modified:
    - src/BlueCode.Core/Domain.fs
    - src/BlueCode.Core/AgentLoop.fs
    - src/BlueCode.Cli/Adapters/Json.fs
    - src/BlueCode.Cli/Adapters/FsToolExecutor.fs
    - src/BlueCode.Cli/CompositionRoot.fs
    - tests/BlueCode.Tests/LlmPipelineTests.fs
    - tests/BlueCode.Tests/CompositionRootTests.fs

key-decisions:
  - "Used failwith stubs (not real impls) in FsToolExecutor for the 3 new tools — plan 08-02 fills them, this plan establishes the compiling baseline"
  - "Batched all shared-seam changes (DU + dispatch + schema + prompt + tests) into one plan to prevent partial-seam commits where Domain.fs has new cases but Json.fs still rejects them"
  - "No new NuGet packages: all 3 new tools are implementable with .NET 10 BCL APIs only"
  - "oldString/newString/pattern are plain strings (not single-case DUs) — no project-root validation semantics, consistent with existing design"

patterns-established:
  - "Stub strategy: failwith stubs in FsToolExecutor match arms close exhaustive-match warning and give plan 08-02 explicit named insertion points"
  - "Shared-seam batching: when adding a Tool DU case requires touching 5 files, do them all in one plan so every commit compiles"

# Metrics
duration: 3min
completed: 2026-04-25
---

# Phase 8 Plan 01: Shared-Seam Foundation Summary

**Tool DU extended to 7 cases (EditFile/GlobSearch/GrepSearch added), schema enum updated 5→8 values, system prompt describes all 8 actions, FsToolExecutor stubs keep the build clean — plan 08-02 fills the stubs with real implementations**

## Performance

- **Duration:** 3 min
- **Started:** 2026-04-25T02:53:26Z
- **Completed:** 2026-04-25T02:56:34Z
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments

- Domain.fs Tool DU has 3 new cases: EditFile, GlobSearch, GrepSearch — giving plan 08-02 the F# types to implement against
- AgentLoop.fs dispatchTool parses JSON input for all 3 new tool action names into the correct DU cases
- Json.fs llmStepSchema enum accepts 8 values; LlmPipelineTests verifies all 8 round-trip through the schema validator
- defaultSystemPrompt describes all 8 actions with their input field schemas so the LLM knows to use new tools
- FsToolExecutor.create has 7 match arms: 4 real impls (unchanged) + 3 failwith stubs labeled "plan 08-02"; build is clean, zero warnings

## Task Commits

1. **Task 1: Extend Tool DU + dispatchTool + schema enum** - (feat)
2. **Task 2: Close shared seam with FsToolExecutor stubs + 8-action prompt + test updates** - (feat)

**Plan metadata:** (docs commit follows this summary creation)

## Files Created/Modified

- `src/BlueCode.Core/Domain.fs` - Added EditFile, GlobSearch, GrepSearch cases to Tool DU
- `src/BlueCode.Core/AgentLoop.fs` - Added 3 dispatchTool match arms (edit_file, glob_search, grep_search)
- `src/BlueCode.Cli/Adapters/Json.fs` - Extended llmStepSchema action enum from 5 to 8 values
- `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` - Added 3 failwith stub match arms; create now has 7 arms total
- `src/BlueCode.Cli/CompositionRoot.fs` - Extended defaultSystemPrompt to describe 8 actions with input schemas
- `tests/BlueCode.Tests/LlmPipelineTests.fs` - Updated "all 5 valid action enum values accepted" → "all 8 valid action enum values accepted"
- `tests/BlueCode.Tests/CompositionRootTests.fs` - Updated "bootstrap SystemPrompt mentions all 5 actions" → "all 8 actions"

## Decisions Made

- Used `failwith "EditFile/GlobSearch/GrepSearch impl not yet wired (plan 08-02)"` stubs — the message explicitly names plan 08-02 as the filler so the next executor has a clear signal
- Batched all 5 shared-seam files into a single plan rather than splitting across 3 plans, avoiding partial-seam commits where the DU has new cases but the schema rejects them (classic pitfall documented 4x in project history)
- FsToolExecutor stub return type `task { return failwith "..." }` satisfies `Task<Result<ToolResult, AgentError>>` because `failwith` returns `'a`; no type annotation needed

## Stub Strategy (for plan 08-02 executor)

The three new arms in `FsToolExecutor.create` are named placeholders, not dead code:

```fsharp
| EditFile(FilePath _, _, _) ->
    task { return failwith "EditFile impl not yet wired (plan 08-02)" }
| GlobSearch(_, _) ->
    task { return failwith "GlobSearch impl not yet wired (plan 08-02)" }
| GrepSearch(_, _, _) ->
    task { return failwith "GrepSearch impl not yet wired (plan 08-02)" }
```

Plan 08-02 replaces each stub arm with a real implementation function. The stub patterns are already correctly shaped — plan 08-02 only needs to change the body (`task { return failwith ... }` → `editFileImpl rootNormalized path oldStr newStr ct`).

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None. The Cli build after Task 1 produced the expected FS0025 exhaustive-match warning (not an error), as documented in the plan. Task 2 closed it.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Plan 08-02 starts from a compiling, schema-consistent baseline:
- Domain.fs Tool DU has all 7 cases
- AgentLoop.fs routes edit_file, glob_search, grep_search to their DU cases
- FsToolExecutor.create has 3 named stub arms ready for real impls
- System prompt already advertises the 3 new tools to the LLM
- All 218 v1.1 tests pass

Plan 08-02 work: implement editFileImpl, globSearchImpl, grepSearchImpl in FsToolExecutor.fs; add FileToolsTests extensions for all 3 new tools; register new test lists in .fsproj and RouterTests.fs rootTests.

---
*Phase: 08-tool-expansion*
*Completed: 2026-04-25*
