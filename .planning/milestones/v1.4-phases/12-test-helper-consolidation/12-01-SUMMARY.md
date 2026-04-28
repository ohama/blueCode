---
phase: 12-test-helper-consolidation
plan: 01
subsystem: testing
tags: [fsharp, expecto, test-helpers, refactor, mock]

# Dependency graph
requires:
  - phase: 09-agent-loop-v2
    provides: LlmResponse/LlmOutput domain types used by makeMockResponse
  - phase: 11-system-prompt-shrink
    provides: post-read_file injection test (latest consumer of makeMockResponse in AgentLoopTests)
provides:
  - Shared BlueCode.Tests.MockHelpers module with module-public makeMockResponse
  - Single authoritative definition of makeMockResponse for all test modules
affects: [13-bench-polish, future test modules needing LlmResponse mocks]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Shared test helper module (MockHelpers.fs) placed before consumers in F# compile order"
    - "module-public (not private) helper functions for cross-module open exposure"

key-files:
  created:
    - tests/BlueCode.Tests/MockHelpers.fs
  modified:
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/AgentLoopTests.fs
    - tests/BlueCode.Tests/ReplTests.fs

key-decisions:
  - "Single combined refactor commit for 4 mechanically-coupled edits (no valid intermediate build state)"
  - "let (not let private) — module-public visibility required for cross-module open exposure"
  - "Scope limited to makeMockResponse only — toolCall, mockLlm/stubLlm, mockToolsOk/stubToolsOk, discardStep NOT factored (TST-01 discipline)"
  - "Actual duplication was 2 definitions (AgentLoopTests + ReplTests), not 3 as REQUIREMENTS.md TST-01 stated"

patterns-established:
  - "Test helper modules: no testList, no RouterTests.fs rootTests entry; only fsproj Compile Include needed"
  - "New shared test helpers go in MockHelpers.fs, registered before first consumer in fsproj"

# Metrics
duration: 4min
completed: 2026-04-26
---

# Phase 12 Plan 01: Test Helper Consolidation (TST-01) Summary

**Consolidated `makeMockResponse` from 2 duplications (AgentLoopTests + ReplTests) into shared `BlueCode.Tests.MockHelpers` module; 243/1/0 preserved; TST-01 closed**

## Performance

- **Duration:** 4 min
- **Started:** 2026-04-26T12:16:51Z
- **Completed:** 2026-04-26T12:20:32Z
- **Tasks:** 1 (4 mechanically-coupled file edits)
- **Files modified:** 4 (1 created, 3 modified)

## Accomplishments

- Created `BlueCode.Tests.MockHelpers` with module-public `makeMockResponse` helper
- Removed 2 private duplicate definitions (one from AgentLoopTests.fs, one from ReplTests.fs)
- Both consumers updated with `open BlueCode.Tests.MockHelpers`; all 15 call sites resolve without change
- 243/1/0 test counts preserved; bench gate 8/8 PASS

## Task Commits

1. **Task 1: Consolidate makeMockResponse into MockHelpers.fs** - (refactor)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `tests/BlueCode.Tests/MockHelpers.fs` — new shared helper module; single authoritative `makeMockResponse`
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj` — added `<Compile Include="MockHelpers.fs" />` between RunShellTests.fs and AgentLoopTests.fs
- `tests/BlueCode.Tests/AgentLoopTests.fs` — removed 7-line `makeMockResponse` block + divider; added `open BlueCode.Tests.MockHelpers`
- `tests/BlueCode.Tests/ReplTests.fs` — removed 7-line `makeMockResponse` block + divider; added `open BlueCode.Tests.MockHelpers`

## Decisions Made

- **Single combined commit:** 4 edits are mechanically coupled — no valid intermediate build state exists between any subset. CLAUDE.md commit protocol requires atomic per-task commits, not per-file. Single `refactor` commit is cleaner than 4 broken-build states.
- **`let` not `let private`:** Module-public visibility required for cross-module `open` exposure. Private helpers are invisible outside their declaring module in F#.
- **Scope discipline:** `toolCall`, `mockLlm`/`stubLlm`, `mockToolsOk`/`stubToolsOk`, `discardStep` were NOT factored. TST-01 is scoped to `makeMockResponse` only. This is intentional; 3 prior milestones deferred TST-01 partly due to scope creep temptation.
- **Duplication count correction:** REQUIREMENTS.md TST-01 states "3 instances" but the actual definition count was 2 (one per file). The "3" appears to have conflated definition sites with the fact that AgentLoopTests.fs had the helper appear twice across two phases of edits — but in the final file there was only 1 definition per file.

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- TST-01 closed. MockHelpers.fs is the canonical home for future shared test helpers.
- Phase 13 (BENCH-06: fixture cleanup automation) is unblocked; no test infrastructure dependency.
- Any future test module needing `makeMockResponse` can `open BlueCode.Tests.MockHelpers` directly.

---
*Phase: 12-test-helper-consolidation*
*Completed: 2026-04-26*
