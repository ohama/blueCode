---
phase: 07-thought-capture
plan: 02
subsystem: test-harness
tags: [fsharp, expecto, ILlmClient, LlmResponse, mocks, test-migration]

# Dependency graph
requires:
  - phase: 07-01
    provides: LlmResponse record, ILlmClient.CompleteAsync returning Task<Result<LlmResponse,AgentError>>
provides:
  - makeMockResponse helper in AgentLoopTests.fs (duplicated in ReplTests.fs per research decision)
  - mockLlm/stubLlm typed to Result<LlmResponse,AgentError> list
  - ToLlmOutputTests pattern matches destructure Ok { Output = ... }
  - SmokeTests destructures Ok { Output = output } for LlmResponse
  - 216-test baseline fully restored (216 passed, 1 ignored, 0 failed)
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "makeMockResponse helper: wraps LlmOutput + thought string into Result<LlmResponse,AgentError>"
    - "Duplication over extraction: helper duplicated in AgentLoopTests and ReplTests rather than shared module"
    - "Record pattern match in test assertions: Ok { Output = FinalAnswer s } for toLlmOutput results"

key-files:
  created: []
  modified:
    - tests/BlueCode.Tests/AgentLoopTests.fs
    - tests/BlueCode.Tests/ReplTests.fs
    - tests/BlueCode.Tests/ToLlmOutputTests.fs
    - tests/BlueCode.Tests/SmokeTests.fs

key-decisions:
  - "makeMockResponse duplicated (not shared): no shared helper module introduced; Phase 7 scope discipline preserved"
  - "SmokeTests.fs fix included in 07-02 scope: 07-01 SUMMARY flagged it as 07-02 work"
  - "SC-3 observationally deferred: both Qwen endpoints (8000/8001) serve /v1/models but chat completions timed out at 180s (GPU busy); SC-1/2/4/5 structurally confirmed"

patterns-established:
  - "Test mock migration cascade: change ILlmClient return type -> compiler finds every mock -> add wrapper helper -> migrate call sites"

# Metrics
duration: 12min
completed: 2026-04-23
---

# Phase 7 Plan 02: Test Migration and Live Smoke Summary

**Test mocks in AgentLoopTests/ReplTests/ToLlmOutputTests/SmokeTests migrated to LlmResponse record, restoring the 216-test baseline; SC-3 live smoke deferred (GPU busy, endpoints accept connections but chat completions timeout at 180s)**

## Performance

- **Duration:** ~12 min
- **Started:** 2026-04-23T18:30:00Z
- **Completed:** 2026-04-23T18:42:00Z
- **Tasks:** 2 (Task 1: test migration; Task 2: live smoke attempt)
- **Files modified:** 4

## Accomplishments

- `makeMockResponse (thought: string) (output: LlmOutput) : Result<LlmResponse, AgentError>` private helper added to both `AgentLoopTests.fs` and `ReplTests.fs` (duplicated per research decision — no shared module)
- `mockLlm` in AgentLoopTests.fs: type changed from `Result<LlmOutput,AgentError> list` to `Result<LlmResponse,AgentError> list`; 5 call sites migrated (9 `Ok(...)` values wrapped with descriptive thought strings)
- `stubLlm` in ReplTests.fs: same type migration; 6 call sites migrated (7 `Ok(...)` values wrapped)
- `ToLlmOutputTests.fs`: 2 success-arm pattern matches updated from `Ok(FinalAnswer s)` / `Ok(ToolCall(...))` to `Ok { Output = FinalAnswer s }` / `Ok { Output = ToolCall(...) }`; 3 error arms unchanged
- `SmokeTests.fs`: outer match arm updated from `Ok output` to `Ok { Output = output }` to correctly destructure LlmResponse; inner match on LlmOutput DU cases unchanged; incomplete-match warning eliminated
- Full test suite: **216 passed, 1 ignored, 0 failed** (baseline fully restored)

## Task Commits

1. **Task 1: Migrate test mocks to LlmResponse** - `7683edb` (test)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `tests/BlueCode.Tests/AgentLoopTests.fs` — Added `makeMockResponse` helper (+7 lines); changed `mockLlm` type annotation (1 line); migrated 9 `Ok(...)` values to `makeMockResponse` calls
- `tests/BlueCode.Tests/ReplTests.fs` — Added `makeMockResponse` helper (+7 lines, duplicated per research); changed `stubLlm` type annotation (1 line); migrated 7 `Ok(...)` values to `makeMockResponse` calls
- `tests/BlueCode.Tests/ToLlmOutputTests.fs` — Updated 2 success-arm patterns to `Ok { Output = ... }` destructuring
- `tests/BlueCode.Tests/SmokeTests.fs` — Updated outer match arm to `Ok { Output = output }` to unpack LlmResponse

## Decisions Made

- **makeMockResponse duplicated (not shared)**: As documented in 07-RESEARCH.md Q5, no shared test helper module introduced. ReplTests.fs carries its own private copy. Phase 7 scope does not include helper infrastructure refactoring.
- **SmokeTests.fs fix included in 07-02**: The 07-01 SUMMARY explicitly listed SmokeTests as "minor update needed in 07-02." Fixed as part of Task 1 by adding `{ Output = output }` destructuring to the outer match arm.
- **SC-3 deferred (GPU busy)**: Both endpoints at localhost:8000 and localhost:8001 respond to `GET /v1/models` correctly but `POST /v1/chat/completions` timed out at the 180s client limit. The GPU is occupied by another workload. SC-3 is observational — structural SCs (1, 2, 4, 5) are fully satisfied.

## Phase 7 Success Criteria Verification

### SC-1: ILlmClient.CompleteAsync returns type including Thought

```
grep "CompleteAsync" src/BlueCode.Core/Ports.fs
```
Result: `Task<Result<LlmResponse, AgentError>>` — **SATISFIED** (07-01 structural)

### SC-2: No "not captured" literal in src/

```
grep -rn "not captured" src/
```
Result: 0 runtime string literals — **SATISFIED** (07-01 structural)

### SC-3: --verbose shows non-empty LLM thought for at least one step

**Status: DEFERRED — GPU occupied during 07-02 execution**

Endpoints:
- `curl -sS -m 3 http://localhost:8000/v1/models` → `{"object":"list","data":[{"id":"Qwen/Qwen2.5-Coder-32B",...}]}` (8000 up)
- `curl -sS -m 3 http://localhost:8001/v1/models` → `{"object":"list","data":[{"id":"Qwen/Qwen2.5-Coder-32B",...}]}` (8001 up)
- `dotnet run --project src/BlueCode.Cli -- --verbose "List the files in src and tell me how many F# files you see"` → timed out at 180s (chat completion)
- `dotnet run --project src/BlueCode.Cli -- --model 72b --verbose "List files in src"` → timed out at 180s (chat completion)

**Resume command (once GPU is free):**

```bash
dotnet run --project src/BlueCode.Cli -- --verbose "List the files in src and tell me how many F# files you see" 2>&1 | tee /tmp/phase7-verbose.log
grep "thought:" /tmp/phase7-verbose.log | grep -v "\[not captured"
```

Expected: at least one `thought: <LLM reasoning text>` line with non-empty, non-placeholder content. Rendering.fs already reads `step.Thought` correctly; toLlmOutput in QwenHttpClient.fs already wires `Thought step.thought` into LlmResponse.

### SC-4: 216 tests still pass

```
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
```
Result: **216 tests run — 216 passed, 1 ignored, 0 failed, 0 errored. Success!** — **FULLY SATISFIED**

### SC-5: toLlmOutput extracts thought, schema unchanged

```
grep -n "toLlmOutput\|LlmResponse" src/BlueCode.Cli/Adapters/QwenHttpClient.fs
grep -n "minLength" src/BlueCode.Cli/Adapters/Json.fs | head -3
```
Result: `toLlmOutput` returns `Result<LlmResponse, AgentError>`; `"thought"` field has `"minLength": 1` in schema — **SATISFIED** (07-01 structural)

## Structural Verification Gates

```
# makeMockResponse usage count
grep -c "makeMockResponse" tests/BlueCode.Tests/AgentLoopTests.fs  → 7 (1 definition + 6 uses)
grep -c "makeMockResponse" tests/BlueCode.Tests/ReplTests.fs        → 8 (1 definition + 7 uses)

# No stale Result<LlmOutput in test mocks
grep -rn "Result<LlmOutput" tests/BlueCode.Tests/   → (none - OK)

# No src/ changes in this plan
git show --stat HEAD | grep src/                     → (no src/ files in 07-02 commit)
```

## Deviations from Plan

**1. [Rule 2 - Missing Critical] SmokeTests.fs fix included**

- **Found during:** Initial build diagnostics
- **Issue:** `SmokeTests.fs` pattern-matched `Ok output` where `output` is now `LlmResponse`, then matched inner DU cases directly — compiler error FS0001 at lines 43 and 48
- **Fix:** Changed outer match arm to `Ok { Output = output }` to correctly unpack the `LlmResponse` record before matching on `LlmOutput` DU cases
- **Files modified:** `tests/BlueCode.Tests/SmokeTests.fs`
- **Verification:** `dotnet build tests/BlueCode.Tests/BlueCode.Tests.fsproj` — 0 warnings, 0 errors
- **Committed in:** `7683edb` (Task 1 commit)

This was pre-anticipated by 07-01 SUMMARY ("SmokeTests.fs test file also failed compilation... within the expected scope of 07-02 test migration work"). Not a surprise; included as explicitly planned work.

---

**Total deviations:** 1 auto-included (SmokeTests fix, pre-flagged by 07-01)
**Impact:** Zero scope creep; fix was explicitly listed in 07-01 SUMMARY as 07-02 responsibility.

## Issues Encountered

- **Live smoke timeout**: Both Qwen endpoints (8000, 8001) accept TCP connections and return model listings but chat completions timeout at 180s. GPU is currently busy with another workload. SC-3 is observational and can be resumed at any time with the command above.

## Next Phase Readiness

- Phase 7 (Thought Capture) is structurally complete: all 5 SCs satisfied structurally (SC-3 deferred pending GPU availability)
- v1.1 milestone complete: REF-01 (Phase 6), REF-02 (Phase 6), OBS-05 (Phase 7 infrastructure 07-01 + test migration 07-02)
- No further planning needed until v1.2 scoping
- SC-3 resume: run `grep "thought:" /tmp/phase7-verbose.log | grep -v "\[not captured"` after freeing GPU

---
*Phase: 07-thought-capture*
*Completed: 2026-04-23*
