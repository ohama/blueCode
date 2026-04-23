---
phase: 06-dynamic-bootstrap
plan: 01
subsystem: cli-adapter
tags: [fsharp, httpclient, lazy, task, json, vllm, qwen, ref-01]

# Dependency graph
requires:
  - phase: 05-cli-polish
    provides: QwenHttpClient.fs with getMaxModelLenAsync, Router.fs with modelToName (now deleted)
provides:
  - ModelInfo record in QwenHttpClient (ModelId + MaxModelLen bundled from single probe)
  - tryParseModelId pure parser (data[0].id from /v1/models JSON)
  - probeModelInfoAsync (GET /v1/models -> ModelInfo, graceful fallback)
  - Two Lazy<Task<ModelInfo>> per port in create() closure (probe8000, probe8001)
  - buildRequestBody accepting modelId: string (injected from lazy probe)
  - 8 new tryParseModelId test cases (ModelsProbeTests.fs)
affects:
  - 06-02

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Lazy<Task<T>> per-port cache in adapter factory (single-probe guarantee)"
    - "Pure parser beside async IO (testable without HttpClient injection)"

key-files:
  created: []
  modified:
    - src/BlueCode.Core/Router.fs
    - src/BlueCode.Cli/Adapters/QwenHttpClient.fs
    - tests/BlueCode.Tests/ModelsProbeTests.fs

key-decisions:
  - "Delete modelToName from Core (Option B from 06-RESEARCH.md § Q1); adapter owns wire value"
  - "Two explicit Lazy<Task<ModelInfo>> per port, not a Map<Model, Lazy> — simpler for two cases"
  - "Probe uses CancellationToken.None so shared task is not cancelled by one caller's Ctrl+C"
  - "Empty/null/non-string id -> None in parser; probe logs WARN and returns ModelId='' which surfaces as LlmUnreachable on POST (no silent-failure)"
  - "getMaxModelLenAsync intentionally left in place; plan 06-02 decides its fate"

patterns-established:
  - "Lazy<Task<T>>: factory-captured lazy for single-init async resources"
  - "Pure parser + IO function separation: tryParseX (pure, testable) beside xAsync (IO, not directly unit-tested)"

# Metrics
duration: ~4min
completed: 2026-04-23
---

# Phase 6 Plan 01: Dynamic Bootstrap — Router.modelToName Delete + QwenHttpClient Lazy Probe Summary

**Deleted Router.modelToName hardcode (REF-01) and replaced with Lazy<Task<ModelInfo>> per-port probe in QwenHttpClient.create(), sending live data[0].id as the POST "model" field**

## Performance

- **Duration:** ~4 min
- **Started:** 2026-04-23T08:58:18Z
- **Completed:** 2026-04-23T09:02:41Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- Killed the `/Users/ohama/llm-system/` absolute-path hardcode from Core (SC-1 contribution). Router.fs is now path-free; only adapter layer holds wire values.
- Added `ModelInfo` record, `tryParseModelId` pure parser, `probeModelInfoAsync`, and two `Lazy<Task<ModelInfo>>` instances (probe8000/probe8001) in QwenHttpClient.fs. CompleteAsync now awaits the right probe and passes `info.ModelId` to `buildRequestBody`.
- Extended ModelsProbeTests.fs with 8 tryParseModelId cases (valid id, absolute-path id SC-4 fixture, null, missing, empty string, empty data array, invalid JSON, non-string number). Total tests: 216 (208 baseline + 8 new), 0 failures.

## Task Commits

Each task was committed atomically:

1. **Task 1: Delete Router.modelToName and add ModelInfo/tryParseModelId/probeModelInfoAsync to QwenHttpClient** - `33b8545` (refactor)
2. **Task 2: Extend ModelsProbeTests with tryParseModelId coverage** - `0eac891` (test)

**Plan metadata:** TBD (docs commit)

## Files Created/Modified

- `src/BlueCode.Core/Router.fs` — Deleted modelToName function (lines 53-60), added REF-01 comment
- `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` — Added ModelInfo record, tryParseModelId, probeModelInfoAsync, two Lazy<Task<ModelInfo>> probes in create(), updated buildRequestBody signature
- `tests/BlueCode.Tests/ModelsProbeTests.fs` — Renamed to maxModelLenTests (private), added modelIdTests (private), combined under "QwenHttpClient probes" top-level testList

## Decisions Made

- **Option B (adapter owns wire value):** modelToName deleted from Core entirely. `buildRequestBody` receives `modelId: string` from caller. No Core changes beyond the deletion. Follows 06-RESEARCH.md § Q1 recommendation.
- **Two explicit Lazy<Task<ModelInfo>> (not Map):** With only two Model cases (32B/72B), two named bindings (probe8000, probe8001) are clearer than a `Map<Model, Lazy>`. If a third Model is added, the `if model = Qwen32B` line becomes a correctness concern but that's out of scope.
- **CancellationToken.None in Lazy factory:** The probe task is shared across all CompleteAsync callers to the same port. If the user's ct were baked in, one Ctrl+C would cancel a task that other callers are awaiting. Using CancellationToken.None avoids this (see 06-RESEARCH.md § Pitfall 6). The main POST in postAsync still respects the user's ct.
- **Empty ModelId -> POST 4xx surface (no silent failure):** On probe miss (network down, missing data[0].id), `probeModelInfoAsync` returns `ModelId = ""`. vLLM will return HTTP 4xx for the empty model field, which postAsync maps to `LlmUnreachable`. This surfaces the error at the user-visible call site rather than silently sending garbage.
- **getMaxModelLenAsync left in place:** CompositionRoot.bootstrapAsync still calls it. Plan 06-02 decides whether to delete bootstrapAsync. Leaving it avoids a cross-file compile break in this plan.

## Deviations from Plan

None — plan executed exactly as written.

The only judgment call: comment references to "modelToName" in docstrings were removed (reworded) to satisfy the strict `grep -rn "modelToName" src/` zero-match requirement. Both occurrences were in code comments, not functional calls — but strictness was applied as specified.

## Issues Encountered

None.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- `probeModelInfoAsync` and `Lazy<Task<ModelInfo>>` infrastructure is in place. Plan 06-02 can delete `bootstrapAsync` / collapse it into `bootstrap` and update `Program.fs` to call `bootstrap` (sync, no I/O at startup).
- `getMaxModelLenAsync` intentionally left for plan 06-02. Removing it now would break `CompositionRoot.bootstrapAsync` before that plan is ready.
- Known limitation (documented): `AppComponents.MaxModelLen` stays at 8192 floor. For 72B (128k context), the 80% context warning fires at ~26k chars instead of ~410k chars — conservative but inaccurate. This is accepted for v1.1 scope. v1.2 candidate.
- Probe failure semantics when both ports down: if intent routing picks 32B but port 8000 is down, `probeModelInfoAsync` logs WARN and returns `ModelId = ""`. The subsequent POST to vLLM will return HTTP 4xx, surfacing as `LlmUnreachable`. This is the intended behavior (fail visibly, not silently). Plan 06-02's SC-2 addresses the case where 8000 is down but only 72B is needed (no 8000 probe fires at all with lazy approach).

---
*Phase: 06-dynamic-bootstrap*
*Completed: 2026-04-23*
