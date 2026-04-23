# Phase 1: Foundation - Research

**Researched:** 2026-04-22
**Domain:** F# / .NET 10 solution scaffold, DU spine, pure-function routing
**Confidence:** HIGH

---

## Summary

This research answers ten specific questions the planner needs to produce task prompts for Phase 1 without guessing. Phase 1 covers the two-project solution scaffold, all eight domain DUs, the three routing functions, and the minimum entry-point skeleton. It depends on nothing and has no external I/O—every success criterion is verifiable with `dotnet build` or `dotnet test`.

The prior research in `.planning/research/` (STACK.md, ARCHITECTURE.md, PITFALLS.md, SUMMARY.md) is authoritative on stack choices and general architecture. This document does **not** re-litigate those decisions. It adds the scaffolding minutiae, file skeletons, DU field choices, enforcement strategies, and exact test invocations the planner requires.

**Primary recommendation:** Build the scaffold with `dotnet new` using explicit `--framework net10.0` flags, define all eight DUs in full in Phase 1 (not stubs), use a plain grep-in-CI approach to enforce the `async {}` ban, and make Phase 1's CLI entry point a **literal stub** (`[<EntryPoint>] let main _ = 0`)—Argu is Phase 5 scope.

---

## Standard Stack

Inherited from `.planning/research/STACK.md`. This phase uses only:

### Core (Phase 1 only)
| Library | Version | Purpose | Phase 1 Use |
|---------|---------|---------|-------------|
| F# / .NET 10 SDK | 10.0 (LTS) | Language + runtime | `dotnet new`, `dotnet build`, `dotnet run` |
| FsToolkit.ErrorHandling | 5.2.0 | `taskResult {}` CE | Prove Criterion 5 compiles in one module (Ports.fs stub) |
| Expecto | 10.2.1 | Test runner | Router unit tests (Criteria 3) |

### Not Needed in Phase 1
FSharp.SystemTextJson, JsonSchema.Net, Spectre.Console, Argu, Serilog, FSharp.Control.TaskSeq — all Phase 2 or later. Do not add them to .fsproj files in this phase.

**Installation commands (run from solution root after scaffold exists):**
```bash
dotnet add src/BlueCode.Core/BlueCode.Core.fsproj package FsToolkit.ErrorHandling --version 5.2.0
dotnet add tests/BlueCode.Tests/BlueCode.Tests.fsproj package Expecto --version 10.2.1
dotnet add tests/BlueCode.Tests/BlueCode.Tests.fsproj package FSharp.SystemTextJson --version 1.4.36
# FSharp.SystemTextJson is NOT needed in Core or Cli in Phase 1
```

---

## Architecture Patterns

### Recommended Project Structure (Phase 1 result)

```
blueCode.sln                     # OR blueCode.slnx (see note below)
├── src/
│   ├── BlueCode.Core/
│   │   ├── BlueCode.Core.fsproj
│   │   ├── Domain.fs            # All 8 DUs — FIRST in compile order
│   │   ├── Router.fs            # classifyIntent, intentToModel, modelToEndpoint
│   │   └── Ports.fs             # ILlmClient stub + one taskResult {} use to prove Criterion 5
│   └── BlueCode.Cli/
│       ├── BlueCode.Cli.fsproj
│       └── Program.fs           # [<EntryPoint>] let main _ = 0  (stub only)
└── tests/
    └── BlueCode.Tests/
        ├── BlueCode.Tests.fsproj
        └── RouterTests.fs       # Expecto tests for Criteria 3
```

**Note on .sln vs .slnx:** Starting with .NET 10 SDK, `dotnet new sln` defaults to the new `.slnx` format. Both formats build identically with `dotnet build`. Use `.slnx` unless the CI environment requires the old format.

### Pattern 1: F# Compile Order in .fsproj

F# files compile in the order listed in `<ItemGroup>`. Domain.fs must be first; every other module that references its types must appear after it. This is enforced by the compiler—a forward reference is a build error, not a warning.

```xml
<!-- BlueCode.Core.fsproj — ItemGroup order IS the dependency graph -->
<ItemGroup>
  <Compile Include="Domain.fs" />
  <Compile Include="Router.fs" />
  <Compile Include="Ports.fs" />
  <!-- Phase 2+ adds ContextBuffer.fs, ToolRegistry.fs, Rendering.fs, AgentLoop.fs here -->
</ItemGroup>
```

**How to keep this maintained:** There is no 2026 tooling that automatically manages F# compile order. Paket is a NuGet/Fake-script alternative to `dotnet add package` but does not reorder .fsproj files. Fantomas is a formatter and does not touch .fsproj. The discipline is manual. Convention: append new files to the end of the `<ItemGroup>`, never insert in the middle unless the new file's dependencies require it. The compiler's forward-reference error is the enforcement mechanism—you will know immediately if order is wrong.

### Pattern 2: Two-Project Library + Executable Layout

`BlueCode.Core` uses `classlib` template (no `<OutputType>`—default is library). `BlueCode.Cli` uses `console` template (`<OutputType>Exe</OutputType>`). The Cli project references Core via `<ProjectReference>`.

`dotnet run --project src/BlueCode.Cli` finds the entry point through `<OutputType>Exe</OutputType>` in BlueCode.Cli.fsproj combined with the `[<EntryPoint>]` attribute on the last `let main` function in the last compiled file (Program.fs). The `[<EntryPoint>]` function must be the **last declaration in the last file** in the compilation sequence—the F# compiler enforces this (FS0433 if violated).

### Pattern 3: Test Project as Console App with Expecto

Expecto does not use `[<Fact>]` attributes or a test host. The test project is a plain console application that calls `Expecto.runTestsWithCLIArgs`. The `[<EntryPoint>]` in the test project is the Expecto runner entry point.

```xml
<!-- BlueCode.Tests.fsproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="RouterTests.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/BlueCode.Core/BlueCode.Core.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Expecto" Version="10.2.1" />
  </ItemGroup>
</Project>
```

Run tests with: `dotnet run --project tests/BlueCode.Tests`

**Why not `dotnet test`?** Expecto can integrate with `dotnet test` via `Expecto.TestResults` + `EnableExpectoTestingPlatformIntegration`, but this requires removing the `[<EntryPoint>]` attribute and adds complexity. For Phase 1, `dotnet run` is sufficient and requires zero extra configuration.

### Anti-Patterns to Avoid

- **Putting `async {}` anywhere in BlueCode.Core.fsproj** — see enforcement section below.
- **Adding NuGet packages not needed in Phase 1** — FSharp.SystemTextJson, Spectre.Console, Argu etc. should not appear in Phase 1 .fsproj files. Defer to the phase that actually uses them.
- **Defining DU cases as stubs** — Do not write `| Debug | Design (* TODO *)`. All 8 DUs must be fully defined in Phase 1 (see DU design section below). Partial definitions defeat Criterion 2.
- **Putting `[<EntryPoint>]` in any file other than Program.fs** — and Program.fs must be the last `<Compile Include>` entry in BlueCode.Cli.fsproj.
- **Using `dotnet new classlib` for BlueCode.Cli** — classlib omits `<OutputType>Exe</OutputType>`; the project will not produce an executable.

---

## Q1: Exact .fsproj File Layouts

### Scaffold Commands (run once, from project root)

```bash
# Create solution (net10 SDK defaults to .slnx format)
dotnet new sln -n blueCode -o .

# Create Core library
dotnet new classlib -lang F# -o src/BlueCode.Core --framework net10.0

# Create Cli executable
dotnet new console -lang F# -o src/BlueCode.Cli --framework net10.0

# Create test project (console for Expecto)
dotnet new console -lang F# -o tests/BlueCode.Tests --framework net10.0

# Register all three in solution
dotnet sln add src/BlueCode.Core/BlueCode.Core.fsproj
dotnet sln add src/BlueCode.Cli/BlueCode.Cli.fsproj
dotnet sln add tests/BlueCode.Tests/BlueCode.Tests.fsproj

# Wire project reference (Cli → Core)
dotnet add src/BlueCode.Cli/BlueCode.Cli.fsproj reference src/BlueCode.Core/BlueCode.Core.fsproj

# Wire project reference (Tests → Core)
dotnet add tests/BlueCode.Tests/BlueCode.Tests.fsproj reference src/BlueCode.Core/BlueCode.Core.fsproj

# Add FsToolkit to Core (for Criterion 5 compile-test in Ports.fs)
dotnet add src/BlueCode.Core/BlueCode.Core.fsproj package FsToolkit.ErrorHandling --version 5.2.0

# Add Expecto to Tests
dotnet add tests/BlueCode.Tests/BlueCode.Tests.fsproj package Expecto --version 10.2.1
```

**TFM string is `net10.0`** — confirmed from .NET SDK 10 docs. The `--framework net10.0` flag produces exactly this string in the generated .fsproj.

### BlueCode.Core.fsproj (final Phase 1 state)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <!-- No <OutputType> — classlib default is library -->
    <!-- Optional: parallel compilation (F# 10 preview feature) -->
    <!-- <ParallelCompilation>true</ParallelCompilation> -->
    <!-- <Deterministic>false</Deterministic> -->
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Domain.fs" />
    <Compile Include="Router.fs" />
    <Compile Include="Ports.fs" />
    <!-- Phase 2+ files appended here in dependency order -->
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="FsToolkit.ErrorHandling" Version="5.2.0" />
    <!-- Phase 2+ packages added here -->
  </ItemGroup>

</Project>
```

### BlueCode.Cli.fsproj (final Phase 1 state)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../BlueCode.Core/BlueCode.Core.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <Compile Include="Program.fs" />
    <!-- Phase 2+ adds CompositionRoot.fs, Adapters/*, Repl.fs — Program.fs stays LAST -->
  </ItemGroup>

  <!-- Phase 2+ NuGet packages:
    <PackageReference Include="Argu" Version="6.2.5" />
    <PackageReference Include="Serilog" Version="4.3.1" />
    <PackageReference Include="Spectre.Console" Version="0.55.2" />
    <PackageReference Include="FSharp.SystemTextJson" Version="1.4.36" />
  -->

</Project>
```

### BlueCode.Tests.fsproj (final Phase 1 state)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="RouterTests.fs" />
    <!-- Phase 2+ test files appended here -->
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/BlueCode.Core/BlueCode.Core.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Expecto" Version="10.2.1" />
  </ItemGroup>

</Project>
```

**dotnet new classlib generates `Library.fs`** — rename or replace with `Domain.fs` after scaffolding. The .fsproj `<Compile Include="Library.fs" />` entry must be updated to `<Compile Include="Domain.fs" />`.

**dotnet new console generates `Program.fs`** — for BlueCode.Cli, this file stays but its content is replaced with the stub. For BlueCode.Tests, it becomes `RouterTests.fs` (rename and update .fsproj).

---

## Q2: Minimum CLI Entry Point for Criterion 1

Phase 1's `Program.fs` for `BlueCode.Cli` should be a **literal stub**:

```fsharp
// src/BlueCode.Cli/Program.fs
module Program

[<EntryPoint>]
let main _ = 0
```

This is sufficient for Criterion 1: `dotnet run --project src/BlueCode.Cli` compiles, runs, and exits with code 0 and no output.

**Argu is NOT needed in Phase 1.** The Argu wiring (`--help`, `--model`, `--verbose`) is Phase 5 scope (requirement CLI-06, ROU-04). Adding a partial Argu stub in Phase 1 creates dead code that phases 2–4 must work around. Keep the entry point as a stub until Phase 5.

**The `module Program` declaration is required** because F# console projects in SDK style use explicit module/namespace declarations. Without it, the file compiles but may generate a warning; with it, the intent is clear.

---

## Q3: DU Design Specifics — All 8 DUs, Phase 1 Full Definitions

**All 8 DUs must be fully defined in Phase 1.** Partial definitions (commented-out cases, `TODO` placeholders) will not satisfy Criterion 2: "a match on any of them produces a compile error if a case is missing." The exhaustive-match guarantee only works when the DU has all its real cases.

The canonical definitions come from `.planning/research/ARCHITECTURE.md`. Below are the Phase 1-finalized versions with field decisions clarified:

### Intent (ROU-01)
```fsharp
// All 5 cases required by ROU-01. No fields — pure discriminator.
type Intent =
    | Debug
    | Design
    | Analysis
    | Implementation
    | General
```

### Model (ROU-02, ROU-03)
```fsharp
// Two cases. No fields — model identity is entirely encoded by case name.
// Do NOT add URL or port as fields; those belong to Endpoint/Router.
type Model =
    | Qwen32B
    | Qwen72B
```

### Endpoint
```fsharp
// Derived from Model by modelToEndpoint. Separate type keeps routing logic
// closed: adding a third model forces a new case here AND in endpointToUrl.
type Endpoint =
    | Port8000
    | Port8001
```

### AgentState
```fsharp
// Full state machine. All cases required now — AgentLoop uses exhaustive match.
// AwaitingApproval is included for future tool-approval gate (Phase 3+), but
// it IS a valid state in the type — not a TODO comment.
type AgentState =
    | AwaitingUserInput
    | PromptingLlm      of loopCount: int
    | AwaitingApproval  of Tool              // Tool is defined below — compile order: Tool before AgentState
    | ExecutingTool     of Tool * loopCount: int
    | Observing         of Step * loopCount: int
    | Complete          of finalAnswer: string
    | MaxLoopsHit
    | Failed            of AgentError       // AgentError is defined below
```

**Compile order implication:** `AgentState` references `Tool`, `Step`, and `AgentError`. All three must appear *before* `AgentState` in Domain.fs. The order in Domain.fs should be:
1. `Intent`, `Model`, `Endpoint`
2. `FilePath`, `Command`, `Timeout` (single-case DUs for tool params)
3. `Tool`
4. `ToolOutput`, `ToolResult`
5. `Thought`, `ToolName`, `ToolInput`
6. `LlmOutput`
7. `AgentError`
8. `StepStatus`, `Step`
9. `AgentState`
10. `AgentResult`

### Tool
```fsharp
// Carries typed parameters — no raw strings at dispatch time.
// FilePath, Command, Timeout are single-case DUs (defined before Tool).
type FilePath = FilePath of string   // validated by smart constructor in Phase 3
type Command  = Command  of string
type Timeout  = Timeout  of int      // milliseconds

type Tool =
    | ReadFile  of FilePath
    | WriteFile of FilePath * content: string
    | ListDir   of FilePath
    | RunShell  of Command * Timeout
```

### ToolOutput and ToolResult
```fsharp
// ToolOutput is the raw text result of a successful tool execution.
type ToolOutput = ToolOutput of string

// ToolResult is what IToolExecutor returns — structured, not raw string.
// PITFALLS.md D-4: tool errors must be structured observations, not empty strings.
type ToolResult =
    | Success         of output: string
    | Failure         of exitCode: int * stderr: string
    | SecurityDenied  of reason: string
    | PathEscapeBlocked of attempted: string
    | Timeout         of seconds: int
```

### LlmOutput
```fsharp
// Parsed once at LLM boundary; consumers never see raw JSON.
// ARCHITECTURE.md Win 3: typed everywhere after parse.
type Thought   = Thought  of string
type ToolName  = ToolName of string
type ToolInput = ToolInput of Map<string, string>

type LlmOutput =
    | ToolCall    of ToolName * ToolInput
    | FinalAnswer of string
```

**Design note:** `LlmOutput` does NOT have a `Thinking` case for Qwen's chain-of-thought `<think>` blocks. The `thought` field of the JSON schema is a string, not a separate LlmOutput case. Qwen's structured output is `{thought: string, action: string, input: {}}` — parsed into `Thought` (the field) and `LlmOutput` (the action discriminator) separately. Phase 2 handles this parsing.

### AgentError
```fsharp
// All cases needed for exhaustive match in AgentLoop and ILlmClient/IToolExecutor.
// LoopGuardTripped added per PITFALLS.md D-1 (loop guard).
type AgentError =
    | LlmUnreachable     of endpoint: string * detail: string
    | InvalidJsonOutput  of raw: string
    | SchemaViolation    of detail: string    // LLM JSON valid but schema mismatch
    | UnknownTool        of ToolName
    | ToolFailure        of Tool * exn
    | MaxLoopsExceeded
    | LoopGuardTripped   of action: string   // same (action,input) called 3x
    | UserCancelled
```

### Step
```fsharp
// Record, not DU — represents one completed iteration.
type StepStatus = StepSuccess | StepFailed of string | StepAborted

type Step = {
    StepNumber : int
    Thought    : Thought
    Action     : LlmOutput
    ToolResult : ToolResult option   // None if FinalAnswer (no tool called)
    Status     : StepStatus
    ModelUsed  : Model
}
```

**Field name:** `ToolResult` (not `ToolOutput`) to align with the `ToolResult` DU. The `ToolOutput` single-case DU is for raw execution output; `ToolResult` DU is the structured outcome.

### AgentResult
```fsharp
// Return type of runSession.
type AgentResult = {
    FinalAnswer : string
    Steps       : Step list
    LoopCount   : int
    Model       : Model
}
```

### Complete Domain.fs Skeleton (Phase 1)

```fsharp
// src/BlueCode.Core/Domain.fs
module BlueCode.Core.Domain

// ── Routing ──────────────────────────────────────────────────────────────────

type Intent =
    | Debug
    | Design
    | Analysis
    | Implementation
    | General

type Model =
    | Qwen32B
    | Qwen72B

type Endpoint =
    | Port8000
    | Port8001

// ── Tool parameter primitives (single-case DUs for type safety) ───────────────

type FilePath = FilePath of string
type Command  = Command  of string
type Timeout  = Timeout  of int      // milliseconds

// ── Tools ────────────────────────────────────────────────────────────────────

type Tool =
    | ReadFile  of FilePath
    | WriteFile of FilePath * content: string
    | ListDir   of FilePath
    | RunShell  of Command * Timeout

type ToolOutput = ToolOutput of string

type ToolResult =
    | Success           of output: string
    | Failure           of exitCode: int * stderr: string
    | SecurityDenied    of reason: string
    | PathEscapeBlocked of attempted: string
    | Timeout           of seconds: int

// ── LLM output ───────────────────────────────────────────────────────────────

type Thought   = Thought  of string
type ToolName  = ToolName of string
type ToolInput = ToolInput of Map<string, string>

type LlmOutput =
    | ToolCall    of ToolName * ToolInput
    | FinalAnswer of string

// ── Error domain ─────────────────────────────────────────────────────────────

type AgentError =
    | LlmUnreachable     of endpoint: string * detail: string
    | InvalidJsonOutput  of raw: string
    | SchemaViolation    of detail: string
    | UnknownTool        of ToolName
    | ToolFailure        of Tool * exn
    | MaxLoopsExceeded
    | LoopGuardTripped   of action: string
    | UserCancelled

// ── Step record ──────────────────────────────────────────────────────────────

type StepStatus = StepSuccess | StepFailed of string | StepAborted

type Step = {
    StepNumber : int
    Thought    : Thought
    Action     : LlmOutput
    ToolResult : ToolResult option
    Status     : StepStatus
    ModelUsed  : Model
}

// ── Agent state machine ───────────────────────────────────────────────────────

type AgentState =
    | AwaitingUserInput
    | PromptingLlm      of loopCount: int
    | AwaitingApproval  of Tool
    | ExecutingTool     of Tool * loopCount: int
    | Observing         of Step * loopCount: int
    | Complete          of finalAnswer: string
    | MaxLoopsHit
    | Failed            of AgentError

// ── Session result ────────────────────────────────────────────────────────────

type AgentResult = {
    FinalAnswer : string
    Steps       : Step list
    LoopCount   : int
    Model       : Model
}
```

---

## Q4: Enforcing the `async {}` Ban (Criterion 4)

There is no F# compiler flag or .editorconfig rule that forbids `async {}` expressions at compile time in 2026. Options evaluated:

| Approach | Overhead | Reliability | Verdict |
|----------|----------|-------------|---------|
| Custom F# analyzer (Roslyn-equivalent) | High — requires separate analyzer project | High | Overkill for Phase 1 |
| FSharpLint custom hint rule | Medium — requires `.fsharplint.json` config | Medium — FSharpLint does not deeply understand CE builders | Not reliable for CE detection |
| `grep -r "async {" src/BlueCode.Core` in CI | Zero — one line in a Makefile or CI script | High — detects any `async {` in Core source | **Recommended** |
| Convention only (code review) | Zero | Low | Not sufficient |

**Recommendation: grep in CI.** The pattern `async {` (with a space before the brace) covers 99.9% of F# async CE usage. Add this to a `Makefile` or GitHub Actions step:

```bash
# Fail if any Core file uses async {}
if grep -r --include="*.fs" "async {" src/BlueCode.Core/; then
  echo "ERROR: async {} found in BlueCode.Core — use task {} instead"
  exit 1
fi
```

**Note on false positives:** The grep will also match comments containing `async {`. The cost of investigating a false positive is much lower than the cost of an undetected `async {}` slip-through. Accept this tradeoff.

**For the test project:** Tests may legitimately use `Async.RunSynchronously` to test async functions from sync Expecto test cases. The ban applies only to `BlueCode.Core` source files. Do not apply the grep check to `tests/`.

**Criterion 4 verification:** The criterion says "any `async {}` expression in Core.fsproj is a build error." Since the compiler does not enforce this, the verification step is: run the grep check and show it returns exit code 1 when a test file containing `async { return 42 }` is placed in `src/BlueCode.Core/`.

---

## Q5: F# Compile-Order Discipline

No 2026 tooling automates F# compile order. The discipline is:

1. New files always append to the end of the `<ItemGroup>`.
2. If a new file depends on a later file, the later file must be moved up first.
3. The compiler's FS0010/FS0039 forward-reference error is the enforcement mechanism—it fires immediately on `dotnet build`.

**Paket** manages NuGet dependencies as an alternative to `dotnet add package` but does not reorder .fsproj `<Compile>` entries. **Fantomas** formats F# source files but does not touch .fsproj. **Neither helps with compile order.**

**Practical technique:** Keep a comment block at the top of each .fsproj explaining the ordering invariant:

```xml
<!-- BlueCode.Core.fsproj
     COMPILE ORDER IS LOAD-BEARING. F# compiles top-to-bottom.
     Rule: each file may only reference types/modules defined in earlier files.
     Domain.fs MUST be first. Program.fs (in Cli) MUST be last. -->
```

---

## Q6: Argu in Phase 1 vs Phase 5

**Phase 1: no Argu at all.** Program.fs is a literal `[<EntryPoint>] let main _ = 0`.

**Rationale:** Success Criterion 1 requires `dotnet run` to build and exit cleanly. A stub entry point satisfies this. The Argu wiring (DU definition, `ArgumentParser.Create`, `ParseCommandLine`, `--help` output, `--model 72b` flag) is requirement CLI-06 and ROU-04, both scoped to Phase 5. Adding partial Argu code in Phase 1 creates:
- A `PackageReference` for Argu in BlueCode.Cli.fsproj that has no function yet.
- An `ArgParser` DU and `CliArguments` type that Phases 2–4 must route around.
- A `main` function that does more than the phase requires.

**Phase 5 will add** to BlueCode.Cli.fsproj:
```xml
<PackageReference Include="Argu" Version="6.2.5" />
<PackageReference Include="Serilog" Version="4.3.1" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />
<PackageReference Include="Spectre.Console" Version="0.55.2" />
<PackageReference Include="FSharp.SystemTextJson" Version="1.4.36" />
```

And replace Program.fs with the full Argu-backed entry point.

---

## Q7: Test Project — Yes, Phase 1 Needs It

**Yes — create `BlueCode.Tests` in Phase 1.** Criterion 3 requires a unit test that calls `classifyIntent` and `intentToModel` and verifies return values. A test project must exist to verify this criterion.

The test project is `tests/BlueCode.Tests/BlueCode.Tests.fsproj` as described in Q1. It uses Expecto with `runTestsWithCLIArgs`.

**RouterTests.fs skeleton:**
```fsharp
// tests/BlueCode.Tests/RouterTests.fs
module BlueCode.Tests.RouterTests

open Expecto
open BlueCode.Core.Domain
open BlueCode.Core.Router

let routerTests = testList "Router" [

    testCase "classifyIntent 'fix the null check' returns Debug" <| fun () ->
        let result = classifyIntent "fix the null check"
        Expect.equal result Intent.Debug "Expected Debug intent for 'fix'"

    testCase "intentToModel Debug returns Qwen72B" <| fun () ->
        let result = intentToModel Intent.Debug
        Expect.equal result Model.Qwen72B "Debug should route to 72B"

    testCase "intentToModel Implementation returns Qwen32B" <| fun () ->
        let result = intentToModel Intent.Implementation
        Expect.equal result Model.Qwen32B "Implementation should route to 32B"

    testCase "intentToModel General returns Qwen32B" <| fun () ->
        let result = intentToModel Intent.General
        Expect.equal result Model.Qwen32B "General should route to 32B"

    testCase "modelToEndpoint Qwen72B returns Port8001" <| fun () ->
        let result = modelToEndpoint Model.Qwen72B
        Expect.equal result Endpoint.Port8001 "72B maps to port 8001"

    testCase "modelToEndpoint Qwen32B returns Port8000" <| fun () ->
        let result = modelToEndpoint Model.Qwen32B
        Expect.equal result Endpoint.Port8000 "32B maps to port 8000"
]

[<EntryPoint>]
let main args = runTestsWithCLIArgs [] args routerTests
```

Run: `dotnet run --project tests/BlueCode.Tests`

---

## Q8: FSharp.SystemTextJson in Phase 1

**Defer to Phase 2.** Do not add `FSharp.SystemTextJson` to `BlueCode.Core.fsproj` or `BlueCode.Cli.fsproj` in Phase 1. Reasons:

1. No JSON is serialized or deserialized in Phase 1 — Domain.fs, Router.fs, and Ports.fs contain no JSON calls.
2. The converter must be registered *before* any `JsonSerializer` call. Since Phase 2 is when `QwenHttpClient.fs` is written, that is the correct place to create and register `JsonSerializerOptions` with `JsonFSharpConverter`.
3. Adding unused packages to .fsproj increases restore time and complicates the minimal scaffold.

**Where to create the shared JsonConfig:** Phase 2 creates a `JsonConfig.fs` module in `BlueCode.Core` (before `Ports.fs` in compile order) with:
```fsharp
module BlueCode.Core.JsonConfig
open System.Text.Json
open System.Text.Json.Serialization
open FSharp.SystemTextJson

let defaultOptions =
    let o = JsonSerializerOptions(JsonSerializerDefaults.Web)
    o.Converters.Add(JsonFSharpConverter())
    o

let strictOptions =
    let o = JsonSerializerOptions.Strict  // .NET 10: rejects unknown fields, duplicates
    o.Converters.Add(JsonFSharpConverter())
    o
```

The planner for Phase 2 should note this. Phase 1 does NOT create `JsonConfig.fs`.

---

## Q9: Verifying Each Success Criterion

### Criterion 1: `dotnet run` exits cleanly

```bash
dotnet run --project src/BlueCode.Cli
# Expected: no output, exit code 0
echo $?  # should print: 0
```

### Criterion 2: Exhaustive match compile-error proof

The DU must be fully defined (all cases present). To prove the compile error:
```bash
# Temporarily add a test match to RouterTests.fs with a missing case:
# let _x = match Intent.Debug with | Debug -> () | Design -> ()
# (missing Analysis, Implementation, General)
dotnet build  # Expected: FS0025 — Incomplete pattern match on this expression
# Remove the temporary test match, restore build
```

In practice, Criterion 2 is satisfied by the fact that `Router.fs` contains an exhaustive `intentToModel` function that matches all five Intent cases. If a new case were added to `Intent` without updating `intentToModel`, the build would fail with FS0025. The planner should include a verification step: **add a new DU case to `Intent`, run `dotnet build`, confirm FS0025 fires, then revert.**

### Criterion 3: Router unit tests pass

```bash
dotnet run --project tests/BlueCode.Tests
# Expected output: "All 6 tests passed in ..."
```

Specific assertions (from RouterTests.fs above):
- `classifyIntent "fix the null check"` → `Intent.Debug`
- `intentToModel Intent.Debug` → `Model.Qwen72B`

### Criterion 4: `async {}` ban verification

```bash
# 1. Create a temporary file in Core with async {} content
echo 'module Temp\nlet x = async { return 42 }' > src/BlueCode.Core/Temp.fs
# 2. Add Temp.fs to BlueCode.Core.fsproj ItemGroup (or run grep test)
grep -r --include="*.fs" "async {" src/BlueCode.Core/
# Expected: prints the file path — grep exits with 0 (found)
# This is the ERROR state. CI script wraps this with:
if grep -r --include="*.fs" "async {" src/BlueCode.Core/; then exit 1; fi
# 3. Remove Temp.fs, verify grep exits with code 1 (not found)
rm src/BlueCode.Core/Temp.fs
grep -r --include="*.fs" "async {" src/BlueCode.Core/ ; echo "exit: $?"
# Expected: exit: 1 (nothing found = ban enforced)
```

### Criterion 5: `taskResult {}` compiles

Add a minimal use to `Ports.fs`:

```fsharp
// src/BlueCode.Core/Ports.fs
module BlueCode.Core.Ports

open System.Threading
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open BlueCode.Core.Domain

// ILlmClient — implemented in Phase 2 by QwenHttpClient
type ILlmClient =
    abstract member CompleteAsync :
        messages : string list
        -> model  : Model
        -> ct     : CancellationToken
        -> Task<Result<LlmOutput, AgentError>>

// IToolExecutor — implemented in Phase 3 by FsToolExecutor
type IToolExecutor =
    abstract member ExecuteAsync :
        tool : Tool
        -> ct  : CancellationToken
        -> Task<Result<ToolResult, AgentError>>

// Proof that taskResult {} CE compiles in BlueCode.Core (Criterion 5).
// This stub is deleted or replaced in Phase 4 when AgentLoop is written.
let private _taskResultCompileTest : Task<Result<unit, AgentError>> =
    taskResult {
        return ()
    }
```

```bash
dotnet build src/BlueCode.Core
# Expected: Build succeeded (no errors, no warnings about taskResult)
```

---

## Q10: .NET 10 / F# 10 Gotchas for 2026

### 1. TFM string: `net10.0`

The target framework moniker for .NET 10 is exactly `net10.0`. With the .NET 10 SDK installed, `dotnet new console -lang F# --framework net10.0` produces `<TargetFramework>net10.0</TargetFramework>`. This is confirmed in Microsoft docs. **Do not write `net10` (without `.0`)** — the SDK will reject it.

### 2. `dotnet new sln` defaults to `.slnx` format in .NET 10

Starting with the .NET 10 SDK, `dotnet new sln` creates a `.slnx` file (new XML-based solution format) rather than the old `.sln` format. Both work with `dotnet build` and `dotnet run`. If IDE compatibility requires the old format, use `dotnet new sln --format sln`. For blueCode, `.slnx` is fine.

### 3. `dotnet new classlib -lang F#` generates `Library.fs`, not `Domain.fs`

The generated file is `Library.fs` with `module Library`. Replace this file with `Domain.fs` and update the .fsproj `<Compile Include>`. The same applies to `BlueCode.Tests`: `dotnet new console -lang F#` generates `Program.fs`; rename it to `RouterTests.fs` and update the .fsproj.

### 4. F# 10 attribute target enforcement (FS0842)

F# 10 enforces `[<Fact>]` and similar attributes must be on `unit -> unit` functions. This applies to xUnit. Expecto tests are plain `unit -> unit` functions passed to `testCase` — they are not affected by FS0842. This is why Expecto is preferred over xUnit in this project.

### 5. `ParallelCompilation` is a preview feature in F# 10

The `<ParallelCompilation>true</ParallelCompilation>` flag in .fsproj enables parallel compilation (new in F# 10). It requires `<Deterministic>false</Deterministic>`. This is optional and not required for correctness — include it only if build times are a concern. It does not affect the Phase 1 functionality.

### 6. `UseAppHost` default is unchanged

`<UseAppHost>` (whether to produce a native app host binary alongside the DLL) defaults to `true` for executable projects in .NET 10. No change needed — the blueCode CLI will have a native host binary on macOS.

### 7. F# 10 `and!` in `task {}` is available but not needed in Phase 1

`and!` syntax for concurrent bindings in `task {}` CE is a F# 10 feature (stabilized from preview). Phase 1 does not use concurrent awaits. Phase 4+ may use it for parallel LLM calls. No action needed in Phase 1.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Result-chaining for task functions | Manual `match` on every `Task<Result>` | `taskResult {}` CE from FsToolkit.ErrorHandling 5.2.0 | 100+ lines of bind chains replaced by CE syntax |
| Test runner for pure F# functions | `[<Fact>]` + xUnit infrastructure | Expecto `testCase` + `runTestsWithCLIArgs` | No test host, no attribute friction, `dotnet run` to execute |
| Async ban enforcement | Custom F# analyzer project | `grep -r "async {" src/BlueCode.Core/` in CI | Zero overhead, reliable detection |

---

## Common Pitfalls

### Pitfall 1: `[<EntryPoint>]` Not in Last File of Last Project

**What goes wrong:** F# emits FS0433 if `[<EntryPoint>]` is not the last declaration in the last compiled file. If `Program.fs` is not the last `<Compile Include>` entry in BlueCode.Cli.fsproj, the build fails.

**How to avoid:** Keep `Program.fs` pinned as the last `<Compile Include>` in BlueCode.Cli.fsproj. When Phase 2+ adds `CompositionRoot.fs`, `Repl.fs`, etc., insert them *before* `Program.fs` in the `<ItemGroup>`.

**Warning signs:** FS0433 error during `dotnet build`.

### Pitfall 2: DU Case Added to Intent Without Updating `intentToModel`

**What goes wrong:** FS0025 (incomplete pattern match) fires on `intentToModel` if a new `Intent` case is added without a corresponding match arm.

**How to avoid:** This is the intended behavior — it's a safety feature, not a pitfall. Treat FS0025 as a signal to update the match, not as a build noise to suppress. Never add `| _ ->` wildcard to `intentToModel`.

**Warning signs:** FS0025 during `dotnet build` — find and fix the match, don't suppress with a wildcard.

### Pitfall 3: `async {}` Introduced by IDE Autocomplete

**What goes wrong:** Some F# IDE plugins (Ionide) autocomplete `async {` when typing `a`. A developer accepting the autocomplete without noticing the CE type adds banned code.

**How to avoid:** The CI grep check catches this before merge. In local development, `dotnet build` does not fail for `async {}` — only the grep check does. Add the grep as a pre-commit hook or run it manually before pushing.

### Pitfall 4: `taskResult {}` Requires Opening the Namespace

**What goes wrong:** `taskResult { return () }` fails to compile with "The value or constructor 'taskResult' is not defined" if `open FsToolkit.ErrorHandling` is missing.

**How to avoid:** Add `open FsToolkit.ErrorHandling` at the top of any file that uses `taskResult {}`. The CE builder is exposed by this single open.

### Pitfall 5: `dotnet new classlib` Generates `net9.0` if .NET 9 SDK Is Active

**What goes wrong:** If the machine has .NET 9 SDK as the active SDK (no `global.json`), `dotnet new classlib -lang F#` generates `<TargetFramework>net9.0</TargetFramework>`.

**How to avoid:** Always pass `--framework net10.0` explicitly. Or create a `global.json` at the solution root pinning the SDK:
```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

### Pitfall 6: AgentState References Tool Before Tool Is Defined

**What goes wrong:** If `AgentState` is placed before `Tool` in Domain.fs, the compiler emits FS0039 (value not defined).

**How to avoid:** Follow the compile order in Q3 above strictly. `Tool` must appear before `AgentState`. The ordering listed in Q3 is the authoritative sequence for Domain.fs.

---

## Code Examples

### Router.fs (complete Phase 1 implementation)

```fsharp
// src/BlueCode.Core/Router.fs
module BlueCode.Core.Router

open BlueCode.Core.Domain

// Pure — no IO, no side effects, deterministic.
// Keyword lists: extend in Phase 5 when more routing signals are available.
let classifyIntent (userInput: string) : Intent =
    let s = userInput.ToLowerInvariant()
    if   ["error"; "bug"; "fix"; "debug"; "traceback"; "exception"; "null"]
         |> List.exists s.Contains then Debug
    elif ["design"; "architecture"; "system"; "구조"; "설계"]
         |> List.exists s.Contains then Design
    elif ["analyze"; "compare"; "tradeoff"; "difference"; "분석"]
         |> List.exists s.Contains then Analysis
    elif ["write"; "implement"; "code"; "example"]
         |> List.exists s.Contains then Implementation
    else General

// Exhaustive match — adding a new Intent case without updating this function
// causes FS0025 (compile error). Never add a wildcard arm here.
let intentToModel : Intent -> Model = function
    | Debug | Design | Analysis -> Qwen72B
    | Implementation | General  -> Qwen32B

// Separate function so Endpoint is still a distinct type.
let modelToEndpoint : Model -> Endpoint = function
    | Qwen32B -> Port8000
    | Qwen72B -> Port8001

let endpointToUrl : Endpoint -> string = function
    | Port8000 -> "http://127.0.0.1:8000/v1/chat/completions"
    | Port8001 -> "http://127.0.0.1:8001/v1/chat/completions"
```

### Ports.fs (Phase 1 skeleton with Criterion 5 proof)

```fsharp
// src/BlueCode.Core/Ports.fs
module BlueCode.Core.Ports

open System.Threading
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open BlueCode.Core.Domain

type ILlmClient =
    abstract member CompleteAsync :
        messages : string list
        -> model  : Model
        -> ct     : CancellationToken
        -> Task<Result<LlmOutput, AgentError>>

type IToolExecutor =
    abstract member ExecuteAsync :
        tool : Tool
        -> ct  : CancellationToken
        -> Task<Result<ToolResult, AgentError>>

// Criterion 5: proves taskResult {} CE compiles in BlueCode.Core.
// Remove this stub in Phase 4 when AgentLoop.fs replaces it.
let private _taskResultProof : Task<Result<unit, AgentError>> =
    taskResult {
        return ()
    }
```

### RouterTests.fs with [<EntryPoint>]

See Q7 above for complete `RouterTests.fs` content.

---

## State of the Art

| Old Approach | Current Approach (2026) | Impact on Phase 1 |
|--------------|-------------------------|-------------------|
| `.sln` solution files | `.slnx` format (default in .NET 10 SDK) | `dotnet new sln` produces `.slnx`; both work |
| `[<Fact>]` + xUnit for F# tests | Expecto with `testCase` functions | Avoids FS0842 attribute target warnings in F# 10 |
| `net9.0` as default TFM | `net10.0` when using .NET 10 SDK + `--framework net10.0` | Must pass `--framework net10.0` explicitly or risk generating wrong TFM |
| `async {}` for all async F# | `task {}` throughout, `async {}` banned in Core | grep-based enforcement, no compiler support |

---

## Open Questions

1. **`.slnx` vs `.sln` for the solution file**
   - What we know: `.NET 10 SDK defaults to `.slnx`. `dotnet build` works with both.
   - What's unclear: Whether the user's IDE (if any) or CI environment has full `.slnx` support.
   - Recommendation: Use `.slnx` (the default). If IDE issues arise, `dotnet new sln --format sln` produces the old format.

2. **Whether `Timeout` DU in `Tool` should be milliseconds or seconds**
   - What we know: `Process.WaitForExitAsync` takes `TimeSpan`; most user-facing timeouts are expressed in seconds.
   - What's unclear: The original Python reference uses seconds.
   - Recommendation: Define `Timeout = Timeout of int` as **milliseconds** internally (matches .NET APIs) and document the convention. Phase 3 can expose seconds via a helper.

3. **Whether `ToolResult` should be the return type of `IToolExecutor` or just `ToolOutput`**
   - What we know: PITFALLS.md D-4 requires structured tool errors. ARCHITECTURE.md uses `Task<Result<ToolOutput, AgentError>>`.
   - Recommendation: Use `Task<Result<ToolResult, AgentError>>` where `ToolResult` is the DU defined in Q3. This makes `SecurityDenied` and `PathEscapeBlocked` first-class results rather than `AgentError` cases, keeping `AgentError` scoped to agent-loop failures. Phase 3 may revise if the type gets unwieldy.

---

## Sources

### Primary (HIGH confidence)
- [Get started with F# with command-line tools — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/fsharp/get-started/get-started-command-line) — multi-project scaffold commands, project reference setup, verified against .NET 10 SDK docs
- [.NET default templates for 'dotnet new' — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-sdk-templates) — SDK 10 defaults `net10.0`, `.slnx` default, `console` and `classlib` templates confirmed
- [Argu tutorial — fsprojects.github.io](https://fsprojects.github.io/Argu/tutorial.html) — minimal Argu IArgParserTemplate + ArgumentParser.Create + ParseCommandLine pattern
- [haf/expecto README — GitHub](https://github.com/haf/expecto) — `runTestsWithCLIArgs`, `testCase`, `testList`, `[<EntryPoint>]` placement, `dotnet run` invocation
- [FsToolkit.ErrorHandling taskResult/ce.md — GitHub](https://github.com/demystifyfp/FsToolkit.ErrorHandling/blob/master/gitbook/taskResult/ce.md) — `taskResult {}` CE, `open FsToolkit.ErrorHandling`, `let!` / `return!` patterns
- `.planning/research/ARCHITECTURE.md` — DU spine design (canonical source, not duplicated here)
- `.planning/research/STACK.md` — NuGet versions (canonical source)
- `.planning/research/PITFALLS.md` — ToolResult DU cases, agentError cases (canonical source)

### Secondary (MEDIUM confidence)
- [fsprojects/FSharpLint — GitHub](https://github.com/fsprojects/FSharpLint) — confirmed no built-in rule for banning async {} CE; grep approach validated as standard substitute
- [What's new in F# 10 — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/fsharp/whats-new/fsharp-10) — FS0842 attribute target enforcement, `and!` in task CE
- WebSearch results on .NET 10 SDK template defaults — confirmed `net10.0` TFM string, `.slnx` default

---

## Metadata

**Confidence breakdown:**
- .fsproj file layouts: HIGH — sourced from official Microsoft .NET 10 template docs and verified command sequences
- DU design: HIGH — all cases sourced from ARCHITECTURE.md + PITFALLS.md; field decisions explicitly justified
- `async {}` ban: HIGH — grep approach is the established convention; absence of compiler support verified against FSharpLint docs
- Compile order: HIGH — F# compile order is a language specification invariant; no tooling ambiguity
- Argu scope (Phase 1 = stub): HIGH — requirements CLI-06 and ROU-04 are clearly Phase 5 in SUMMARY.md
- Test project scope: HIGH — Criterion 3 requires it; Expecto setup is well-documented
- FSharp.SystemTextJson deferral: HIGH — no JSON in Phase 1 modules; Phase 2 is the correct registration point
- .NET 10 gotchas: HIGH — sourced from official SDK docs

**Research date:** 2026-04-22
**Valid until:** 2026-07-22 (stable — .NET 10 is GA, F# 10 is stable, template defaults are unlikely to change)
