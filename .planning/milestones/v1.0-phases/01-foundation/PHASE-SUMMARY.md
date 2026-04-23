---
phase: 01-foundation
plans: [01, 02, 03]
subsystem: domain-core
tags: [fsharp, dotnet10, discriminated-unions, routing, expecto, taskresult, fstoolkit-errorhandling, ports, interfaces, ci-grep]

# Dependency graph
requires: []
provides:
  - BlueCode.slnx: Three-project .NET 10 F# solution (Core/Cli/Tests)
  - Domain.fs: 8 Phase 1 DUs (AgentState, Intent, Model, Tool, LlmOutput, AgentError, Step, ToolResult) + 9 supporting types
  - Router.fs: 4 pure routing functions (classifyIntent, intentToModel, modelToEndpoint, endpointToUrl)
  - ContextBuffer.fs: Immutable bounded Step buffer (compile-order slot reserved for Phase 2/4 population)
  - ToolRegistry.fs: Stub ToolRegistry type (compile-order slot reserved for Phase 3 handler map)
  - Ports.fs: ILlmClient + IToolExecutor interfaces; taskResult {} CE compile-proof (SC5)
  - scripts/check-no-async.sh: CI enforcement of async {} ban in Core (SC4)
  - 16 Expecto router tests, all passing (SC3)
  - Phase 1 all 5 Success Criteria empirically satisfied
affects:
  - 02-llm-client (implements ILlmClient; consumes endpointToUrl, LlmOutput, AgentError)
  - 03-tools (implements IToolExecutor; consumes Tool, ToolResult, FilePath, Command, Timeout, ToolOutput)
  - 04-agent-loop (consumes all DUs, ILlmClient, IToolExecutor, ContextBuffer, AgentState, Step, AgentResult)
  - 05-cli (consumes AgentResult; adds Argu CLI interface)

# Tech tracking
tech-stack:
  added:
    - FsToolkit.ErrorHandling 5.2.0 (BlueCode.Core only)
    - Expecto 10.2.1 (BlueCode.Tests only)
    - FSharp.Core 10.1.203 (all projects, auto-resolved)
    - Mono.Cecil 0.11.4 (transitive via Expecto)
  patterns:
    - "Pure routing pipeline: classifyIntent -> intentToModel -> modelToEndpoint -> endpointToUrl (zero IO, zero mutation)"
    - "DU-first domain modeling: every domain concept is a typed DU case"
    - "Exhaustive match enforcement: FS0025 on any incomplete match (empirically proven twice)"
    - "Ports-last compile order: Domain.fs first, Ports.fs last in BlueCode.Core.fsproj"
    - "Compile-order slot reservation: stubs placed early so later phases extend without .fsproj restructuring"
    - "grep-in-CI async ban: F# compiler cannot enforce async {} ban; grep -rn --include='*.fs' is the mechanism"
    - "taskResult {} CE proof pattern: private trivial binding forces CE through compiler"

key-files:
  created:
    - BlueCode.slnx
    - src/BlueCode.Core/Domain.fs
    - src/BlueCode.Core/Router.fs
    - src/BlueCode.Core/ContextBuffer.fs
    - src/BlueCode.Core/ToolRegistry.fs
    - src/BlueCode.Core/Ports.fs
    - src/BlueCode.Cli/Program.fs
    - tests/BlueCode.Tests/RouterTests.fs
    - scripts/check-no-async.sh
  modified:
    - src/BlueCode.Core/BlueCode.Core.fsproj (updated compile order across plans)

# Metrics
duration: 32min total (01: 15min, 02: 7min, 03: 10min)
completed: 2026-04-22
---

# Phase 1: Foundation — Phase Summary

**Three-project .NET 10 F# solution with typed domain model (8 DUs), bilingual routing pipeline, ILlmClient/IToolExecutor ports, taskResult {} CE proof, and grep-enforced async {} ban; all 5 Phase 1 Success Criteria empirically verified**

## Phase Overview

Phase 1 produced the typed skeleton that all subsequent phases build on. Three plans completed sequentially:

- **01-01** (15 min): Solution scaffold — `BlueCode.slnx`, three projects, compile-order invariant, stub entry points
- **01-02** (7 min): Domain DUs + Router pure functions + 16 Expecto tests; SC2 first proof (Intent FS0025)
- **01-03** (10 min): Ports interfaces + ContextBuffer/ToolRegistry stubs + async-ban script; SC2 second proof (ToolResult FS0025)

Total: 32 min.

## Phase Commits

| SHA | Plan | Description |
|-----|------|-------------|
| `ac41dbf` | 01-01 | feat: scaffold .NET 10 solution with Core/Cli/Tests projects |
| `90016a1` | 01-01 | docs: complete solution scaffold plan |
| `9d2e718` | 01-02 | feat: populate Domain.fs with all 8 Phase 1 DUs and supporting types |
| `e6e1f7a` | 01-02 | feat: implement Router pure functions and 16-case Expecto test suite |
| `5768501` | 01-02 | docs: complete Domain DUs + Router + Tests plan |
| `ac0569c` | 01-03 | feat: add ContextBuffer/ToolRegistry stubs and populate Ports.fs |
| `bfd5872` | 01-03 | feat: add async-ban CI script and empirically verify enforcement |

---

## Success Criteria Verification

### SC1: CLI stub exits 0 silently

```bash
dotnet build src/BlueCode.Cli --configuration Debug >/dev/null 2>&1
OUT=$(dotnet run --project src/BlueCode.Cli --no-build 2>&1); CODE=$?
echo "exit=$CODE, output_bytes=${#OUT}"
```

**Result:** `exit=0, output_bytes=0`

The BlueCode.Cli entry point is `[<EntryPoint>] let main _ = 0` — no output, no side effects. SC1 satisfied throughout Phase 1 and confirmed at end of Plan 01-03.

---

### SC2: Incomplete pattern match triggers FS0025 (proven TWICE — complementary DUs)

**Proof 1 — Intent DU (Plan 01-02, Task 3)**

Temporary file `src/BlueCode.Core/_CompileCheck.fs` with incomplete match on `Intent` (missing Analysis/Implementation/General):

**Exact FS0025 message (Korean locale):**
```
/Users/ohama/projs/blueCode/src/BlueCode.Core/_CompileCheck.fs(8,11): warning FS0025: 이 식의 패턴 일치가 완전하지 않습니다. 예를 들어, 값 'Analysis'은(는) 패턴에 포함되지 않은 케이스를 나타낼 수 있습니다.
```

**English equivalent:**
```
_CompileCheck.fs(8,11): warning FS0025: Incomplete pattern matches on this expression. For example, the value 'Analysis' may indicate a case not covered by the pattern(s).
```

DU exercised: `Intent` (routing-layer DU consumed by `intentToModel`).
File cleaned up after proof; build returned to 0/0 (3 compile entries at that point).

---

**Proof 2 — ToolResult DU (Plan 01-03, Task 3)**

Temporary file `src/BlueCode.Core/_Crit2Check.fs` with incomplete match on `ToolResult` (missing SecurityDenied/PathEscapeBlocked/Timeout):

```fsharp
let _broken (r: ToolResult) =
    match r with
    | Success _ -> 1
    | Failure _ -> 2
    // SecurityDenied, PathEscapeBlocked, Timeout deliberately missing
```

**Exact FS0025 message (Korean locale):**
```
/Users/ohama/projs/blueCode/src/BlueCode.Core/_Crit2Check.fs(4,11): warning FS0025: 이 식의 패턴 일치가 완전하지 않습니다. 예를 들어, 값 'PathEscapeBlocked (_)'은(는) 패턴에 포함되지 않은 케이스를 나타낼 수 있습니다.
```

**English equivalent:**
```
_Crit2Check.fs(4,11): warning FS0025: Incomplete pattern matches on this expression. For example, the value 'PathEscapeBlocked (_)' may indicate a case not covered by the pattern(s).
```

DU exercised: `ToolResult` (tool-outcome DU defined in Phase 1 FND-02, consumed primarily in Phase 3 TOOL-07).
File cleaned up after proof; build returned to 0/0 (5 compile entries final state).

---

**Combined SC2 conclusion:** FS0025 fires reliably on incomplete DU matches across both routing-layer and tool-outcome DUs. The F# exhaustive match guarantee is mechanically enforced — no wildcard arms needed in production code.

---

### SC3: Router tests pass

```bash
dotnet run --project tests/BlueCode.Tests
```

**Result:**
```
16 tests run in 00:00:00.10 for Router.Router – 16 passed, 0 ignored, 0 failed, 0 errored. Success!
```

**SC3 verbatim assertions verified:**
- `classifyIntent "fix the null check" = Intent.Debug` — PASS
- `intentToModel Intent.Debug = Model.Qwen72B` — PASS

All 16 routing assertions pass throughout Phase 1. Test suite covers: classifyIntent (9 cases including bilingual), intentToModel (2 cases), modelToEndpoint (2 cases), endpointToUrl (2 cases), precedence (1 case).

---

### SC4: async {} ban enforced mechanically

**Script location:** `scripts/check-no-async.sh` (chmod +x)

**Clean run (no violation):**
```
OK: no async {} expressions in src/BlueCode.Core
exit=0
```

**Violation injected** (`src/BlueCode.Core/_AsyncBanCheck.fs` with `let _bad = async { return 42 }`):
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

Cleanup sequence: rm disk (6a) → remove fsproj entry (6b) → re-run script (exit 0) → dotnet build BlueCode.slnx (0/0). The temp file was fully purged BEFORE the final build, so the build verifies the canonical 5-file Core, not a policy-violating state.

---

### SC5: taskResult {} CE compiles in BlueCode.Core

**Binding in Ports.fs (line 34):**
```fsharp
let private _taskResultCompileProof : Task<Result<unit, AgentError>> =
    taskResult {
        let! value = Ok ()
        return value
    }
```

Requires: `open FsToolkit.ErrorHandling` (at top of Ports.fs). The `let!` + `return` form exercises both bind and return machinery of the CE, not just the trivial `return` path.

**Build verification:**
```
dotnet build src/BlueCode.Core --configuration Debug
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

SC5 satisfied. Phase 4 (AgentLoop.fs) will contain the production uses of `taskResult {}`.

---

## Final Build Status

```
dotnet build BlueCode.slnx --configuration Debug
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Final Compile List (BlueCode.Core.fsproj)

```xml
<ItemGroup>
    <Compile Include="Domain.fs" />
    <Compile Include="Router.fs" />
    <Compile Include="ContextBuffer.fs" />
    <Compile Include="ToolRegistry.fs" />
    <Compile Include="Ports.fs" />
</ItemGroup>
```

Compile entry count: **5** (verified: `grep -c "<Compile Include" src/BlueCode.Core/BlueCode.Core.fsproj` → 5)

## Final Repository Tree

```
scripts/check-no-async.sh
src/BlueCode.Cli/BlueCode.Cli.fsproj
src/BlueCode.Cli/Program.fs
src/BlueCode.Core/BlueCode.Core.fsproj
src/BlueCode.Core/ContextBuffer.fs
src/BlueCode.Core/Domain.fs
src/BlueCode.Core/Ports.fs
src/BlueCode.Core/Router.fs
src/BlueCode.Core/ToolRegistry.fs
tests/BlueCode.Tests/BlueCode.Tests.fsproj
tests/BlueCode.Tests/RouterTests.fs
```

## NuGet Package List

| Package | Version | Project |
|---------|---------|---------|
| FSharp.Core | 10.1.203 | All (auto-resolved) |
| FsToolkit.ErrorHandling | 5.2.0 | BlueCode.Core only |
| Expecto | 10.2.1 | BlueCode.Tests only |
| Mono.Cecil | 0.11.4 | BlueCode.Tests (transitive via Expecto) |

**NuGet discipline confirmed:** `grep -rE "FSharp\.SystemTextJson|Spectre\.Console|Argu|Serilog|JsonSchema\.Net" src/ tests/` → no output. All Phase 2+ packages absent from Phase 1 codebase.

## Domain DU Inventory

| DU | Cases | Count |
|----|-------|-------|
| Intent | Debug, Design, Analysis, Implementation, General | 5 |
| Model | Qwen32B, Qwen72B | 2 |
| Endpoint | Port8000, Port8001 | 2 |
| Tool | ReadFile, WriteFile, ListDir, RunShell | 4 |
| ToolResult | Success, Failure, SecurityDenied, PathEscapeBlocked, Timeout | 5 |
| LlmOutput | ToolCall, FinalAnswer | 2 |
| AgentError | LlmUnreachable, InvalidJsonOutput, SchemaViolation, UnknownTool, ToolFailure, MaxLoopsExceeded, LoopGuardTripped, UserCancelled | 8 |
| AgentState | AwaitingUserInput, PromptingLlm, AwaitingApproval, ExecutingTool, Observing, Complete, MaxLoopsHit, Failed | 8 |
| StepStatus | StepSuccess, StepFailed, StepAborted | 3 |

Supporting single-case DUs: FilePath, Command, Timeout, Thought, ToolName, ToolInput, ToolOutput

Records: Step, AgentResult

## Phase 1 Decisions (Carried to STATE.md)

| Decision | Rationale |
|----------|-----------|
| task {} exclusively; async {} banned in Core | FND-03; F# async interop pattern replaced with task {} throughout Phase 1+ |
| NuGet packages locked to exact versions | Reproducibility; Phase 2+ packages absent from Phase 1 |
| F# compile order enforced: Domain first, Ports last | FS0433/FS0039 prevention; hexagonal architecture principle |
| .slnx format | dotnet new sln on .NET 10.0.203 produces .slnx by default |
| ToolResult DU shape in Phase 1 | FND-02 expanded so Tool is exhaustively matchable from day one |
| Timeout ms (Tool.RunShell) vs seconds (ToolResult) | .NET API alignment vs user-facing semantics; intentionally different |
| classifyIntent priority: Debug > Design > Analysis > Implementation > General | First-match-wins; Debug catches "fix/null/error"; Design catches "design/architect/structure" |
| FS0025 fires as warning (not hard error) in .NET 10 default build | Invariant is live and detectable; TreatWarningsAsErrors optional for Phase 2+ |
| Compile-order slot reservation for ContextBuffer/ToolRegistry in Phase 1 | ARCHITECTURE.md labels them Phase 2/Phase 4; early placement avoids future .fsproj restructuring |

## Ready for Phase 2

**Phase 2 (LLM Client) can begin immediately:**
- `ILlmClient` interface ready at `src/BlueCode.Core/Ports.fs`
- `endpointToUrl` ready at `src/BlueCode.Core/Router.fs`
- `LlmOutput`, `AgentError`, `Model`, `Endpoint` DUs ready at `src/BlueCode.Core/Domain.fs`
- `FsToolkit.ErrorHandling 5.2.0` already a dependency of BlueCode.Core
- No blockers

**Phase 3 (Tools) can begin after Phase 2:**
- `IToolExecutor` interface ready
- `Tool`, `ToolResult`, `FilePath`, `Command`, `Timeout`, `ToolOutput` DUs ready
- `ToolRegistry.fs` slot reserved

---
*Phase: 01-foundation*
*Completed: 2026-04-22*
