---
phase: 04-agent-loop
plan: 01
subsystem: agent-loop
tags: [fsharp, agent-loop, domain, ports-and-adapters, expecto, task-ce, json-retry]

# Dependency graph
requires:
  - phase: 03-tool-executor
    provides: IToolExecutor, ToolResult DU, BashSecurity, runShellImpl
  - phase: 02-llm-client
    provides: ILlmClient, LlmOutput DU, Message list, parseLlmResponse
  - phase: 01-foundation
    provides: Domain.fs DUs, Router, ContextBuffer, Ports.fs

provides:
  - Step record with OBS-04 timing fields (StartedAt, EndedAt, DurationMs)
  - AgentLoop.fs — runSession entry point + pure recursive runLoop
  - AgentConfig record (MaxLoops, ContextCapacity, SystemPrompt)
  - dispatchTool, computeInputHash, checkLoopGuard, callLlmWithRetry, buildMessages (private helpers)
  - AgentLoopTests.fs — 6 tests covering LOOP-01..05 and OBS-04

affects:
  - 04-02 (CompositionRoot wiring, live Qwen smoke, onStep -> JsonlSink)
  - 04-03 (Logging.fs, JsonlSink.fs, OBS-01/02 JSONL + Serilog)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pure recursive agent loop via runLoop — no mutation, state threaded as parameters"
    - "Ports-and-adapters: AgentLoop.fs depends only on Domain/Router/ContextBuffer/Ports — zero Cli adapter refs"
    - "task {} throughout Core (async {} banned)"
    - "2-attempt LLM retry on InvalidJsonOutput; SchemaViolation NOT retried"
    - "Loop guard: (actionName, inputHash) -> count; trip on 3rd occurrence"
    - "onStep callback threaded through runSession for per-step crash-safe JSONL writing"
    - "Step.Thought = Thought \"[not captured in v1]\" (known v1 limitation — defer to Phase 5+)"

key-files:
  created:
    - src/BlueCode.Core/AgentLoop.fs
    - tests/BlueCode.Tests/AgentLoopTests.fs
  modified:
    - src/BlueCode.Core/Domain.fs
    - src/BlueCode.Core/BlueCode.Core.fsproj
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs

key-decisions:
  - "AgentLoop.fs placed in BlueCode.Core (pure) — no Serilog/Spectre/Cli references"
  - "dispatchTool inlined in AgentLoop.fs — ToolRegistry.fs stays empty stub for now"
  - "System prompt lives in AgentConfig record (not inline constant) — testable + injectable"
  - "onStep callback threaded through runSession — enables 04-02 to wire JsonlSink per-step"
  - "Step.Thought = Thought '[not captured in v1]' — capturing real thought deferred to Phase 5+"
  - "BlueCode.Core.Domain.Timeout qualifier needed in dispatchTool — Domain.Timeout DU vs ToolResult.Timeout case name collision"
  - "Timeout DU disambiguation: BlueCode.Core.Domain.Timeout — consistent with 03-01 decision"

patterns-established:
  - "runSession -> runLoop: model fixed at turn start (PITFALLS D-7)"
  - "ContextBuffer.toList |> List.rev gives chronological step order for buildMessages"
  - "callLlmWithRetry: first InvalidJsonOutput -> correction User message -> second attempt"

# Metrics
duration: 15min
completed: 2026-04-23
---

# Phase 4 Plan 01: AgentLoop.fs Summary

**Pure recursive agent loop in F# Core: runSession + 5-step max + (action,hash) loop guard + 2-attempt JSON retry + per-step OBS-04 timing; 159 tests pass (153 + 6 new)**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-04-23T09:25:00Z
- **Completed:** 2026-04-23T09:40:00Z
- **Tasks:** 3
- **Files modified:** 6

## Accomplishments

- Extended Step record additively with StartedAt/EndedAt/DurationMs (OBS-04) — zero call-site fixups needed
- Created AgentLoop.fs (~310 lines) with pure recursive loop satisfying LOOP-01..05
- Created AgentLoopTests.fs (6 tests, 142 lines) covering all LOOP and OBS-04 requirements

## Task Commits

1. **Task 1: Extend Step record with OBS-04 timing fields** — `89c4d6f` (feat)
2. **Task 2: Create AgentLoop.fs — recursive task loop** — `eec1596` (feat)
3. **Task 3: AgentLoopTests.fs — 6 tests** — `4e5b2c3` (test)

## Files Created/Modified

- `src/BlueCode.Core/Domain.fs` — Added `open System`; Step record amended with StartedAt/EndedAt/DurationMs
- `src/BlueCode.Core/AgentLoop.fs` — Created: AgentConfig, runSession, runLoop, dispatchTool, callLlmWithRetry, buildMessages, checkLoopGuard, computeInputHash
- `src/BlueCode.Core/BlueCode.Core.fsproj` — AgentLoop.fs appended as last Compile entry
- `tests/BlueCode.Tests/AgentLoopTests.fs` — Created: 6 Expecto tests in testList "AgentLoop"
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj` — AgentLoopTests.fs inserted before RouterTests.fs
- `tests/BlueCode.Tests/RouterTests.fs` — agentLoopTests added to rootTests list

## Decisions Made

- **AgentLoop.fs in BlueCode.Core (pure):** No Serilog/Spectre/Cli refs — ports-and-adapters invariant preserved.
- **dispatchTool inlined:** ToolRegistry.fs stays empty stub. Avoids premature abstraction.
- **AgentConfig.SystemPrompt:** System prompt injectable — tests use minimal "test-system-prompt"; production CompositionRoot provides full Qwen prompt.
- **onStep callback:** Threaded through runSession → runLoop. 04-02 wires this to JsonlSink for crash-safe per-step JSONL writing.
- **Thought placeholder:** `Step.Thought = Thought "[not captured in v1]"` — capturing real thought requires amending ILlmClient.CompleteAsync to return (Thought * LlmOutput); deferred to Phase 5+ per research § Open Question 2 Option (d).
- **BlueCode.Core.Domain.Timeout qualifier:** Needed in dispatchTool to disambiguate Domain.Timeout DU (constructor) from ToolResult.Timeout case — consistent with 03-01 precedent noted in STATE.md.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Timeout DU name collision in dispatchTool**
- **Found during:** Task 2 (AgentLoop.fs creation)
- **Issue:** `return RunShell (Command cmd, Timeout timeoutMs)` ambiguous — F# compiler cannot resolve whether `Timeout` refers to Domain.Timeout (the constructor) or ToolResult.Timeout (the case). Build error FS0001.
- **Fix:** Qualified with `BlueCode.Core.Domain.Timeout timeoutMs`.
- **Files modified:** src/BlueCode.Core/AgentLoop.fs
- **Verification:** `dotnet build BlueCode.slnx -c Debug --nologo` succeeded with 0 errors after fix.
- **Committed in:** eec1596 (Task 2 commit)

**2. [Rule 1 - Bug] Test used `AgentLoop.runSession` instead of `runSession`**
- **Found during:** Task 3 (AgentLoopTests.fs compilation)
- **Issue:** Module is opened with `open BlueCode.Core.AgentLoop` so `runSession` is available directly; using `AgentLoop.runSession` causes FS0039 (module name not defined).
- **Fix:** Replaced all `AgentLoop.runSession` with `runSession` in AgentLoopTests.fs.
- **Files modified:** tests/BlueCode.Tests/AgentLoopTests.fs
- **Verification:** Build succeeded; 159 tests pass.
- **Committed in:** 4e5b2c3 (Task 3 commit)

---

**Total deviations:** 2 auto-fixed (both Rule 1 bugs — compile errors caught immediately)
**Impact on plan:** Both fixes necessary for compilation. No scope creep.

## Known v1 Limitations

1. **Thought not captured:** `Step.Thought = Thought "[not captured in v1]"`. Capturing real thought requires amending `ILlmClient.CompleteAsync` to return `(Thought * LlmOutput)` or `LlmStep`; deferred to Phase 5+.
2. **Input hash is process-local:** `string.GetHashCode()` is deterministic WITHIN a process run only (sufficient for turn-scoped guard; acceptable per PITFALLS.md § 6).

## Requirements Closed

- **LOOP-01:** Max 5 iterations per turn (configurable via AgentConfig.MaxLoops)
- **LOOP-02:** MaxLoopsExceeded returned as AgentError when loopN >= MaxLoops
- **LOOP-03:** One tool per step (enforced by LlmOutput DU — ToolCall carries single ToolName)
- **LOOP-04:** Loop guard — (action, inputHash) trips on 3rd occurrence → LoopGuardTripped
- **LOOP-05:** 2-attempt JSON retry on InvalidJsonOutput; SchemaViolation NOT retried
- **LOOP-06:** ContextBuffer.create config.ContextCapacity (default 3) wired in runSession
- **LOOP-07:** CancellationToken threaded through runSession → runLoop → callLlmWithRetry → ILlmClient.CompleteAsync; handler wiring deferred to 04-02/04-03
- **OBS-04:** StartedAt/EndedAt/DurationMs on every Step emitted by runLoop

## Issues Encountered

None beyond the two auto-fixed compilation bugs documented in Deviations.

## User Setup Required

None — this plan is pure Core logic, no external services.

## Next Phase Readiness

- **04-02 (CompositionRoot wiring):** runSession signature is final. AgentConfig is injectable. onStep callback ready to wire to JsonlSink. Live Qwen smoke test (SC-1) can proceed.
- **04-03 (Logging):** onStep callback ready; JsonlSink.fs will implement `Step -> unit` contract.
- **Concern:** HttpClient singleton should move to CompositionRoot.fs (noted in STATE.md Phase 3); no blocker for 04-02.

---
*Phase: 04-agent-loop*
*Completed: 2026-04-23*
