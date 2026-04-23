---
phase: 07-thought-capture
plan: 01
subsystem: llm-boundary
tags: [fsharp, ports-and-adapters, domain-types, ILlmClient, LlmResponse]

# Dependency graph
requires:
  - phase: 06-dynamic-bootstrap
    provides: ILlmClient with lazy ModelInfo probe, QwenHttpClient.create() factory
  - phase: 04-agent-loop
    provides: AgentLoop.runLoop, callLlmWithRetry, Step record with Thought field
provides:
  - LlmResponse record in Domain.fs (Thought + LlmOutput bundle)
  - ILlmClient.CompleteAsync returning Task<Result<LlmResponse, AgentError>>
  - callLlmWithRetry wired to pass LlmResponse through both retry arms
  - Both Step construction sites in runLoop use real Thought from LlmResponse
  - toLlmOutput returns Result<LlmResponse, AgentError> with Thought from LlmStep.thought
affects:
  - 07-02-thought-capture (test migration: mockLlm, stubLlm, toLlmOutput test patterns)

# Tech tracking
tech-stack:
  added: ["Domain.LlmResponse type (2-field record: Thought * LlmOutput)"]
  patterns:
    - "Result.map to lift inner success value into a richer bundle type"
    - "Big-bang F# type cascade: change interface, let compiler find every callsite"
    - "Destructure record in match arm: Ok { Thought = t; Output = FinalAnswer a }"

key-files:
  created: []
  modified:
    - src/BlueCode.Core/Domain.fs
    - src/BlueCode.Core/Ports.fs
    - src/BlueCode.Core/AgentLoop.fs
    - src/BlueCode.Cli/Adapters/QwenHttpClient.fs

key-decisions:
  - "Option C chosen: LlmResponse record (not tuple, not LlmStep, not LlmOutput extension)"
  - "Big-bang single commit: no transitional API; compiler enforces completeness"
  - "Retry semantics preserved: callLlmWithRetry still performs exactly 2 CompleteAsync calls"
  - "Schema unchanged: llmStepSchema already enforces thought minLength:1; no new validation needed"
  - "Stale Known v1 limitation comment removed from AgentLoop.fs header"
  - "Rendering.fs untouched: already reads step.Thought; now receives real content"

patterns-established:
  - "Core boundary type LlmResponse carries only standard F# types (no JsonElement)"
  - "toLlmOutput: Result.map to lift LlmOutput -> LlmResponse without restructuring match logic"

# Metrics
duration: 2min
completed: 2026-04-23
---

# Phase 7 Plan 01: Thought Capture — Production Wiring Summary

**LlmResponse record introduced as ILlmClient.CompleteAsync return type, threading real LLM reasoning text from QwenHttpClient.toLlmOutput through callLlmWithRetry into both Step.Thought construction sites in runLoop**

## Performance

- **Duration:** 2 min
- **Started:** 2026-04-23T09:34:02Z
- **Completed:** 2026-04-23T09:36:23Z
- **Tasks:** 1 (single big-bang task)
- **Files modified:** 4

## Accomplishments

- `LlmResponse = { Thought: Thought; Output: LlmOutput }` record added to Domain.fs after LlmOutput — Core-pure, no JsonElement
- `ILlmClient.CompleteAsync` signature changed from `Task<Result<LlmOutput, AgentError>>` to `Task<Result<LlmResponse, AgentError>>`
- `callLlmWithRetry` return type updated; both `Ok response` arms pass LlmResponse through unchanged — retry semantics (2 attempts) preserved
- Both Step construction sites in `runLoop` now destructure `Ok { Thought = thought; Output = ... }` and assign `Thought = thought`
- `toLlmOutput` in QwenHttpClient.fs wraps existing LlmOutput result via `Result.map (fun output -> { Thought = Thought step.thought; Output = output })`
- Stale "Known v1 limitation" 4-line comment block removed from AgentLoop.fs header

## Task Commits

1. **Task 1: Wire LLM thought through LlmResponse record** - `16789f9` (feat)

**Plan metadata:** (docs commit follows below)

## Files Created/Modified

- `src/BlueCode.Core/Domain.fs` — Added `LlmResponse` type (+11 lines: section header + docblock + type body)
- `src/BlueCode.Core/Ports.fs` — Changed `CompleteAsync` return type annotation (1 line: LlmOutput → LlmResponse)
- `src/BlueCode.Core/AgentLoop.fs` — Removed stale comment block (-4 lines), updated callLlmWithRetry return type and both attempt arms, changed both runLoop match arms and both Thought = assignments (~18 lines net)
- `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` — Updated toLlmOutput signature, restructured body with outputResult + Result.map, updated section header + create() docblock comment (~10 lines net)

## Decisions Made

- **Option C (LlmResponse record) confirmed over Option B (tuple)**: Named fields prevent ordering drift; `callLlmWithRetry` return type annotation reads as first-class type; extensible without breaking existing LlmOutput consumers.
- **Big-bang commit**: Changing `ILlmClient` breaks all mocks at compile time — no transitional API. Let compiler point at every callsite.
- **Retry semantics preserved**: `callLlmWithRetry` passes `Ok response` straight through on both attempts. Second-attempt thought correctly reflects the corrected LLM turn's reasoning.
- **Schema unchanged**: `llmStepSchema` already enforces `thought` as `required` with `minLength:1`. No new validation needed; `Thought step.thought` is safe.
- **Stale comment removed**: The "Known v1 limitation" block in AgentLoop.fs was made false by this plan — removed rather than left as misleading history.

## Deviations from Plan

None — plan executed exactly as written. The `SmokeTests.fs` test file also failed compilation (pattern-matching `LlmResponse` as `ToolCall`/`FinalAnswer` directly), which the research document listed as "no change needed" for `AgentLoopSmokeTests.fs`. It does need a minor update in 07-02 — this is within the expected scope of 07-02 test migration work, not an unplanned scope leak.

## Verification Gates

All 6 verification gates passed:

1. **Compile gate (production):** BlueCode.Core succeeded (0 warnings, 0 errors). BlueCode.Cli succeeded (0 warnings, 0 errors). BlueCode.Tests FAILED — errors only in `AgentLoopTests.fs`, `ReplTests.fs`, `ToLlmOutputTests.fs`, `SmokeTests.fs` (expected; 07-02 scope).

2. **Core purity:** `bash scripts/check-no-async.sh` exit 0. `grep -rn "JsonElement\|LlmWire\|LlmStep" src/BlueCode.Core/` → 0 results.

3. **Placeholder elimination (SC-2):** `grep -rn '"not captured' src/` → 0 results. (Two `///` doc comment matches for "not captured" are historical comments, not runtime string literals.)

4. **Retry semantics preserved (LOOP-05):** `grep -c "CompleteAsync" src/BlueCode.Core/AgentLoop.fs` → `2`.

5. **LlmResponse wired in all 4 files:** `grep -l "LlmResponse" [...] | wc -l` → `4`.

6. **Stale comment removed:** `grep -rn "Known v1 limitation" src/` → 0 results.

## Issues Encountered

None. The F# compiler guided the cascade exactly as the research predicted.

## Next Phase Readiness

- Plan 07-02 must update test mocks (`mockLlm` in AgentLoopTests.fs, `stubLlm` in ReplTests.fs), pattern matches in ToLlmOutputTests.fs (5 cases), and the SmokeTests.fs output destructuring.
- After 07-02: all 216+ tests should pass and `blueCode --verbose` will show real LLM thought in step output.
- Rendering.fs is untouched and already reads `step.Thought` — it will display real thought content automatically once 07-02 test mocks are updated.

---
*Phase: 07-thought-capture*
*Completed: 2026-04-23*
