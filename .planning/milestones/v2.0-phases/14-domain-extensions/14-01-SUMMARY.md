---
phase: 14-domain-extensions
plan: 01
subsystem: domain
tags: [fsharp, domain-types, ports-and-adapters, compile-cascade, session, planning]

# Dependency graph
requires:
  - phase: v1.1-phases/07-llm-observability
    provides: LlmResponse record and LlmOutput DU pattern (big-bang compile cascade precedent)
  - phase: v1.2-phases/09-loop-control
    provides: AgentLoop.fs runLoop structure (two LlmOutput match arms pre-extension)
provides:
  - SessionId newtype (v2.0 PERSIST-01 type-only)
  - PlannedStep record (v2.0 PLAN-01 type-only)
  - Plan record (v2.0 PLAN-01 type-only)
  - Session record (v2.0 PERSIST-01 type-only)
  - LlmOutput.Plan of Plan variant (3rd variant added to existing DU)
  - AgentError.SessionNotFound/SessionCorrupt/PlanInvalid (3 new variants)
  - ISessionStore port in Ports.fs (Save + Load, Task<Result<_,AgentError>>)
affects:
  - Phase 15 (FileSessionStore adapter uses ISessionStore + Session + SessionId)
  - Phase 16 (plan-mode wiring replaces transitional Plan _ arms with real dispatch)
  - Plan 14-02 (PlanValidator uses PlannedStep, Plan, PlanInvalid)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Big-bang compile cascade: single Domain.fs commit with exhaustive match-site propagation (mirrors v1.1 LlmResponse precedent)"
    - "Ports-and-adapters: ISessionStore in Core/Ports.fs; implementation deferred to Cli/Adapters (Phase 15)"
    - "Transitional arm pattern: | Plan _ -> Error(PlanInvalid ...) keeps runLoop semantically correct without Phase 16 wiring"

key-files:
  created: []
  modified:
    - src/BlueCode.Core/Domain.fs
    - src/BlueCode.Core/Ports.fs
    - src/BlueCode.Core/AgentLoop.fs
    - src/BlueCode.Cli/Rendering.fs
    - tests/BlueCode.Tests/SmokeTests.fs

key-decisions:
  - "SessionId placed BEFORE LlmOutput (line 91) so ISessionStore.Load can reference it; Session placed AFTER AgentResult (line 206) because Session.Steps references Step"
  - "Plan type placed BEFORE LlmOutput (lines 111-121) because LlmOutput.Plan of Plan requires Plan to exist at compile time"
  - "Transitional runLoop arm returns Error(PlanInvalid 'Plan output received outside plan-mode') — Phase 16 replaces this with real plan-mode dispatch"
  - "buildMessages Plan _ arm emits '{}' empty assistant message — shape-preserving placeholder; Phase 16 replaces with plan display logic"
  - "ISessionStore uses CancellationToken last param style, matching ILlmClient and IToolExecutor conventions"

patterns-established:
  - "Session/Plan type ordering: SessionId -> PlannedStep -> Plan -> LlmOutput -> LlmResponse -> AgentError -> Step -> AgentState -> AgentResult -> Session -> MessageRole -> Message"
  - "All new AgentError variants follow labeled-field style (SessionNotFound of SessionId, SessionCorrupt of detail: string)"

# Metrics
duration: 4min
completed: 2026-04-26
---

# Phase 14 Plan 01: Domain Extensions Summary

**4 new Domain types (SessionId, PlannedStep, Plan, Session), 3 new AgentError variants, ISessionStore port, and LlmOutput.Plan variant — all compile-cascade propagated to 0 FS0025 warnings, 243/1/0 baseline preserved.**

## Performance

- **Duration:** 4 minutes
- **Started:** 2026-04-26T23:01:56Z
- **Completed:** 2026-04-26T23:06:25Z
- **Tasks:** 3/3
- **Files modified:** 5

## Accomplishments

- Added 4 new Core domain types (SessionId, PlannedStep, Plan, Session) and ISessionStore port with zero I/O imports
- Extended LlmOutput DU with 3rd variant (Plan of Plan) and propagated the F# big-bang compile cascade: all 5 src/ and 1 test/ match sites updated to exhaustive arms
- Preserved 243/1/0 test baseline exactly (0 new tests added; Plan 14-02 adds 5 new tests)

## Task Commits

1. **Task 1: Extend Domain.fs with v2.0 types and AgentError variants** - (feat)
2. **Task 2: Add ISessionStore port + propagate compile cascade** - (feat)
3. **Task 3: Update test-side match sites and verify 243/1/0** - (test)

**Plan metadata:** `docs(14-01): complete domain extension plan` (this commit)

## Files Created/Modified

- `src/BlueCode.Core/Domain.fs` — Added SessionId, PlannedStep, Plan, Session types; LlmOutput.Plan variant; 3 new AgentError variants (SessionNotFound, SessionCorrupt, PlanInvalid)
- `src/BlueCode.Core/Ports.fs` — Added ISessionStore interface (Save + Load)
- `src/BlueCode.Core/AgentLoop.fs` — Added `| Plan _ -> "{}"` arm in buildMessages; `| Ok { Output = Plan _ } -> Error(PlanInvalid ...)` arm in runLoop
- `src/BlueCode.Cli/Rendering.fs` — Added `| Plan _ -> "plan"` in toolSummary; `| Plan p -> sprintf "plan (%d steps)"` in renderVerbose; SessionNotFound/SessionCorrupt/PlanInvalid arms in renderError
- `tests/BlueCode.Tests/SmokeTests.fs` — Added `| Plan _ -> failtest ...` arm to non-wildcard output match

## Match Sites Updated

### src/ sites (exhaustive match, 0 FS0025 warnings after Task 2)

| File | Function | Arm added | Value |
|------|----------|-----------|-------|
| AgentLoop.fs:224 | buildMessages stepMsgs | `\| Plan _ ->` | `"{}"` (empty assistant message placeholder) |
| AgentLoop.fs:391 | runLoop llmResult match | `\| Ok { Output = Plan _ } ->` | `return Error(PlanInvalid "Plan output received outside plan-mode")` |
| Rendering.fs:24 | toolSummary | `\| Plan _ ->` | `"plan"` |
| Rendering.fs:61 | renderVerbose actionLine | `\| Plan p ->` | `sprintf "plan (%d steps)" p.Steps.Length` |
| Rendering.fs:118-120 | renderError | `\| SessionNotFound/SessionCorrupt/PlanInvalid` | user-readable error strings |

### test/ sites

| File | Function | Change |
|------|----------|--------|
| SmokeTests.fs:51 | smoke round-trip match | Added `\| Plan _ -> failtest ...` (non-wildcard match was FS0025) |
| AgentLoopTests.fs:73 | happy path match | No change (wildcard `\| other -> ...` already covers Plan) |
| ToLlmOutputTests.fs:* | toLlmOutput test matches | No change (all matches use wildcards) |

## Decisions Made

1. **SessionId before LlmOutput** — SessionId must be defined at line 91 (before LlmOutput at line 118) so that `ISessionStore.Load: id: SessionId -> ...` in Ports.fs compiles. Session itself comes after AgentResult (line 206) because Session.Steps references Step.

2. **Plan before LlmOutput** — PlannedStep + Plan records defined at lines 100-121, immediately before `type LlmOutput`. F# requires all referenced types to be defined earlier in the same module; `LlmOutput.Plan of Plan` requires Plan to exist.

3. **Transitional runLoop arm** — `| Ok { Output = Plan _ } -> return Error(PlanInvalid "Plan output received outside plan-mode")` is intentional. The Cli adapter will only produce Plan output when `--plan` is set (Phase 16 wiring). Without the flag the LLM never produces it; if it somehow does, surfacing PlanInvalid is correct semantics.

4. **buildMessages Plan _ arm emits "{}"** — Phase 16 will add a proper plan display; the empty-string fallback preserves message-list shape (assistant message always emitted for every step) without breaking the existing step-historicization contract.

5. **No new test cases** — 243/1/0 unchanged. Plan 14-02 adds 5 new tests for PlanValidator.

## Test Count Evidence

```
243 tests run in 00:00:30.964 – 243 passed, 1 ignored, 0 failed, 0 errored. Success!
```

Build verification:
```
dotnet build BlueCode.slnx → 0 errors, 0 warnings (after Task 3)
./scripts/check-no-async.sh → OK: no async {} expressions in src/BlueCode.Core
```

## Out-of-Scope (Deferred)

- Plan validator function (Plan 14-02)
- Plan JSON parsing in QwenHttpClient (Phase 16)
- FileSessionStore adapter in BlueCode.Cli/Adapters/ (Phase 15)
- `--resume` / `--plan` Argu flags (Phase 15-16)
- bench/baseline.json extension (Phase 16)
- CompositionRoot.fs + Repl.fs wiring (Phase 15-16)
- Plan approval gate rendering (Phase 16)

## Deviations from Plan

None — plan executed exactly as written. The grep count check `grep -c "| Plan _" src/BlueCode.Core/AgentLoop.fs` returned 1 (not ≥ 2) because the second arm uses `| Ok { Output = Plan _ }` (record destructure form), which does not match the literal `| Plan _` grep pattern. Both arms are present and the build is exhaustive-match clean.
