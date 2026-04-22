---
phase: 01-foundation
plan: 03
subsystem: ports-and-infra
tags: [fsharp, dotnet10, ports, interfaces, context-buffer, tool-registry, ci-grep, taskresult, fstoolkit-errorhandling]

# Dependency graph
requires:
  - phase: 01-02
    provides: All 8 Phase 1 DUs (Domain.fs), Router.fs pure functions, 16 Expecto tests, SC2 first proof (Intent FS0025)
provides:
  - Ports.fs: ILlmClient and IToolExecutor interface definitions with task {} semantics
  - Ports.fs: taskResult {} CE compile-proof (Success Criterion 5)
  - ContextBuffer.fs: Immutable bounded buffer of Steps with create/add/toList/length/capacity API
  - ToolRegistry.fs: Stub ToolRegistry type and empty value (Phase 3 slot reservation)
  - scripts/check-no-async.sh: grep-based CI enforcement of async {} ban in Core (Success Criterion 4)
  - BlueCode.Core.fsproj: Final 5-entry compile order Domain -> Router -> ContextBuffer -> ToolRegistry -> Ports
  - SC2 second empirical proof: FS0025 on incomplete ToolResult match (complements Intent proof from 01-02)
  - Phase 1 complete: all 5 Success Criteria empirically satisfied
affects:
  - 02-llm-client (implements ILlmClient; ContextBuffer.fs slot reserved for Phase 2 population)
  - 03-tools (implements IToolExecutor; ToolRegistry.fs slot reserved for Phase 3 handler map)
  - 04-agent-loop (consumes ILlmClient + IToolExecutor + ContextBuffer in agent loop)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Ports-last compile order: ILlmClient/IToolExecutor defined after all domain types — mirrors hexagonal architecture"
    - "Compile-order slot reservation: stub files placed in Phase 1 fsproj so later phases extend without restructuring"
    - "grep-in-CI async ban: F# compiler cannot ban async {}; grep -rn --include='*.fs' 'async {' is the enforcement mechanism"
    - "taskResult {} CE proof pattern: private trivial binding forces CE machinery through compiler without shipping real implementation"

key-files:
  created:
    - src/BlueCode.Core/ContextBuffer.fs
    - src/BlueCode.Core/ToolRegistry.fs
    - scripts/check-no-async.sh
  modified:
    - src/BlueCode.Core/Ports.fs
    - src/BlueCode.Core/BlueCode.Core.fsproj

key-decisions:
  - "ContextBuffer placed in Phase 1 (ARCHITECTURE.md labels it Phase 2) to reserve compile-order slot — pure upside, avoids future FS0433/FS0039 restructuring"
  - "ToolRegistry placed in Phase 1 (ARCHITECTURE.md labels it Phase 4) for same compile-order-slot reason"
  - "Ports.fs comment mentioning 'async {}' removed (replaced with 'async CE') to avoid false-positive in check-no-async.sh grep — comments are code targets of the ban script"
  - "Task 3 verification-only: all real file changes committed atomically in Tasks 1 and 2; Task 3's specified mega-commit skipped per GSD atomic-commit protocol"

patterns-established:
  - "Compile-order slot reservation: stubs placed early so later phases populate without .fsproj restructuring"
  - "CE compile-proof pattern: private trivial taskResult binding to validate CE availability in module"
  - "Empirical policy proof: both ban script (async {}) and exhaustive match (FS0025) verified by injection-and-detection test"

# Metrics
duration: 10min
completed: 2026-04-22
---

# Phase 1 Plan 03: Ports, ContextBuffer, ToolRegistry stubs + async-ban CI script Summary

**ILlmClient/IToolExecutor interfaces with taskResult {} compile proof; immutable ContextBuffer and ToolRegistry stubs; check-no-async.sh CI script empirically verified; Phase 1 all 5 Success Criteria satisfied**

## Performance

- **Duration:** ~10 min
- **Started:** 2026-04-22T07:36:55Z
- **Completed:** 2026-04-22T07:46:55Z
- **Tasks:** 3
- **Files created/modified:** 5

## Accomplishments

- Ports.fs populated with ILlmClient (CompleteAsync) and IToolExecutor (ExecuteAsync) interfaces using task {} semantics, plus private `_taskResultCompileProof` using taskResult {} CE (SC5)
- ContextBuffer.fs created as immutable bounded Step buffer with create/add/toList/length/capacity API; private record enforces append-only invariant
- ToolRegistry.fs created as minimal stub with empty ToolRegistry type (Phase 3 reserves slot)
- BlueCode.Core.fsproj updated to final 5-entry compile order: Domain, Router, ContextBuffer, ToolRegistry, Ports
- scripts/check-no-async.sh written, chmod +x, empirically verified: added _AsyncBanCheck.fs (async { return 42 }), script exited 1 with match output; removed file and fsproj entry; script returned exit 0
- SC2 second proof: _Crit2Check.fs with incomplete ToolResult match triggered FS0025; exact message captured; file cleaned up; build returns to 0/0
- All 5 Phase 1 Success Criteria verified end-to-end

## Task Commits

Each task was committed atomically:

1. **Task 1: Create stubs and populate Ports.fs** - `ac0569c` (feat)
2. **Task 2: Write check-no-async.sh and empirically verify** - `bfd5872` (feat)
3. **Task 3: Phase-final verification sweep** - (verification only; no new permanent files; no new commit needed — working tree clean)

## Files Created/Modified

- `src/BlueCode.Core/Ports.fs` - ILlmClient + IToolExecutor interfaces with task {} return types; taskResult {} CE proof (SC5)
- `src/BlueCode.Core/ContextBuffer.fs` - Immutable bounded buffer of Step values with 5-function module API
- `src/BlueCode.Core/ToolRegistry.fs` - Stub ToolRegistry type and empty value
- `src/BlueCode.Core/BlueCode.Core.fsproj` - 5-entry compile order: Domain -> Router -> ContextBuffer -> ToolRegistry -> Ports
- `scripts/check-no-async.sh` - grep-based async {} ban enforcement, chmod +x

## Ports.fs Interface Signatures

```fsharp
type ILlmClient =
    abstract member CompleteAsync :
        messages : string list
     -> model    : Model
     -> ct       : CancellationToken
     -> Task<Result<LlmOutput, AgentError>>

type IToolExecutor =
    abstract member ExecuteAsync :
        tool : Tool
     -> ct   : CancellationToken
     -> Task<Result<ToolResult, AgentError>>

let private _taskResultCompileProof : Task<Result<unit, AgentError>> =
    taskResult {
        let! value = Ok ()
        return value
    }
```

## ContextBuffer.fs Module API

```fsharp
val create   : capacity: int -> ContextBuffer
val add      : step: Step -> buffer: ContextBuffer -> ContextBuffer
val toList   : buffer: ContextBuffer -> Step list
val length   : buffer: ContextBuffer -> int
val capacity : buffer: ContextBuffer -> int
```

## ToolRegistry.fs Stub Shape

```fsharp
type ToolRegistry = private ToolRegistry of Map<ToolName, Tool>
let empty : ToolRegistry = ToolRegistry Map.empty
```

## scripts/check-no-async.sh Summary

```bash
#!/usr/bin/env bash
# Exits 0 when Core is clean; exits 1 on any match of 'async {' in *.fs files.
CORE_DIR="src/BlueCode.Core"
if grep -rn --include='*.fs' 'async {' "$CORE_DIR" ; then
    echo "ERROR: async {} found in $CORE_DIR — use task {} CE instead." >&2
    exit 1
fi
echo "OK: no async {} expressions in $CORE_DIR"
exit 0
```

## SC2 Second Empirical Proof — ToolResult DU

**Exact FS0025 message from build output (Korean locale):**

```
/Users/ohama/projs/blueCode/src/BlueCode.Core/_Crit2Check.fs(4,11): warning FS0025: 이 식의 패턴 일치가 완전하지 않습니다. 예를 들어, 값 'PathEscapeBlocked (_)'은(는) 패턴에 포함되지 않은 케이스를 나타낼 수 있습니다.
```

**English equivalent:**
```
_Crit2Check.fs(4,11): warning FS0025: Incomplete pattern matches on this expression. For example, the value 'PathEscapeBlocked (_)' may indicate a case not covered by the pattern(s).
```

**Proof method:** Created `src/BlueCode.Core/_Crit2Check.fs` with a match on `ToolResult` omitting SecurityDenied/PathEscapeBlocked/Timeout cases. Added `<Compile Include="_Crit2Check.fs" />` to fsproj. Built — FS0025 fired. Removed file and fsproj entry. Rebuilt — 0 warnings, 0 errors.

**Cleanup confirmed:** `test ! -f src/BlueCode.Core/_Crit2Check.fs` → true; `grep -c "<Compile Include" src/BlueCode.Core/BlueCode.Core.fsproj` → 5

**This is the SECOND of TWO SC2 proofs.** The first (Intent DU) is in 01-02-SUMMARY.md. Both messages are recorded in PHASE-SUMMARY.md.

## SC4 Async-Ban Empirical Proof

**Violation injected:**
```
src/BlueCode.Core/_AsyncBanCheck.fs:4:let _bad = async { return 42 }
```

**Script output (exit 1):**
```
src/BlueCode.Core/_AsyncBanCheck.fs:4:let _bad = async { return 42 }

ERROR: async {} found in src/BlueCode.Core — use task {} CE instead.
       See ROADMAP.md Phase 1 Success Criterion 4 and
       .planning/phases/01-foundation/01-RESEARCH.md Q4.
exit=1
```

**After cleanup (exit 0):**
```
OK: no async {} expressions in src/BlueCode.Core
exit=0
```

**Cleanup confirmed:** File removed from disk and fsproj entry removed before final `dotnet build BlueCode.slnx`.

## Build Verification

```
dotnet build BlueCode.slnx --configuration Debug
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Test Results

```
16 tests run in 00:00:00.10 for Router.Router – 16 passed, 0 ignored, 0 failed, 0 errored. Success!
```

## must_haves Truth Table

| Truth | Status |
|-------|--------|
| Ports.fs defines ILlmClient and IToolExecutor interfaces using task {} CE (not async {}) | PASS |
| Ports.fs contains at least one compiling use of FsToolkit.ErrorHandling's taskResult {} CE | PASS |
| ContextBuffer.fs implements an immutable ring buffer type that holds Step values with a bounded capacity | PASS |
| ToolRegistry.fs exists as a stub declaring a minimal type skeleton (real dispatch lives in Phase 3) | PASS |
| scripts/check-no-async.sh is executable and exits 1 when any `async {` token appears in src/BlueCode.Core/*.fs | PASS |
| scripts/check-no-async.sh exits 0 when src/BlueCode.Core/ contains no `async {` tokens | PASS |
| `grep -r 'async {' src/BlueCode.Core/` currently returns NO matches | PASS |
| The full solution (dotnet build BlueCode.slnx) continues to succeed with 0 warnings and 0 errors | PASS |
| All router tests from Plan 01-02 still pass | PASS (16/16) |
| `dotnet run --project src/BlueCode.Cli --no-build` still exits 0 with no output | PASS |

## Decisions Made

- **Ports.fs comment fix:** The plan's Ports.fs comment included the literal text `async {}` in backticks. This caused the check-no-async.sh script to flag it as a violation (the grep hits comments). Fixed by rewriting the comment to use "async CE" instead of the literal token. Rule 1 auto-fix — the comment content is not a real violation and the script behavior is correct.
- **Compile-order slot reservation:** ContextBuffer.fs and ToolRegistry.fs placed in Phase 1 despite ARCHITECTURE.md labeling them Phase 2 / Phase 4 respectively — prevents future FS0433/FS0039 from compile-order restructuring. Per plan objective (explicit deviation, pure upside).
- **Task 3 no additional commit:** GSD protocol specifies atomic per-task commits. Tasks 1 and 2 already committed all file changes. Task 3 is verification only; the plan's specified mega-commit is superseded by the two prior atomic commits.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Ports.fs comment contained literal `async {}` triggering false-positive in grep script**

- **Found during:** Task 1 verification (ran preview grep before Task 2)
- **Issue:** The plan's Ports.fs content had `/// Uses task {} semantics; no \`async {}\` in Core` — the backtick-wrapped `async {}` matched the script's grep pattern even though it is a comment
- **Fix:** Rewrote comment to `/// Uses task {} semantics; async CE is banned in Core`
- **Files modified:** src/BlueCode.Core/Ports.fs
- **Verification:** `grep -rn --include='*.fs' 'async {' src/BlueCode.Core/` returns no matches
- **Committed in:** ac0569c (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 bug in plan's Ports.fs comment content)
**Impact on plan:** Essential fix — without it the async-ban script would fail on clean Core (contradicting SC4). No scope creep.

## Issues Encountered

None. All builds clean. FS0025 and async {} script behavior exactly as expected.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- ILlmClient interface ready for Phase 2's QwenHttpClient implementation
- IToolExecutor interface ready for Phase 3's FsToolExecutor implementation
- ContextBuffer.fs slot reserved (Phase 2 or Phase 4 populates)
- ToolRegistry.fs slot reserved (Phase 3 populates handler map)
- All Phase 1 Success Criteria satisfied and recorded
- No blockers for Phase 2

---
*Phase: 01-foundation*
*Completed: 2026-04-22*
