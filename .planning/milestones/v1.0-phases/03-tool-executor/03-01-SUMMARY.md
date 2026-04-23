---
phase: 03-tool-executor
plan: 01
subsystem: tool-executor
tags: [fsharp, dotnet, filesystem, tool-executor, path-validation, expecto, IToolExecutor]

# Dependency graph
requires:
  - phase: 01-foundation
    provides: Domain.fs Tool DU + ToolResult DU + Ports.fs IToolExecutor contract
  - phase: 02-llm-client
    provides: BlueCode.Cli compile order (LlmWire -> Json -> QwenHttpClient -> Program)

provides:
  - Tool.ReadFile amended with lineRange: (int * int) option (TOOL-01)
  - Tool.ListDir amended with depth: int option (TOOL-03)
  - Adapters/BashSecurity.fs placeholder (validateCommand stub, Plan 03-03 fills)
  - Adapters/FsToolExecutor.fs with create: string -> IToolExecutor (read_file/write_file/list_dir + RunShell stub)
  - Path validation with trailing-separator fix (TOOL-02 / PITFALLS.md D-3)
  - 2000-char truncateOutput applied to all tool outputs (TOOL-06)
  - 15 new Expecto tests in FileToolsTests.fs (49 total passing)

affects:
  - 03-02 (wires real run_shell process into FsToolExecutor.fs RunShell branch)
  - 03-03 (replaces BashSecurity.fs validateCommand stub with 21 validators)
  - 04-agent-loop (CompositionRoot.fs calls FsToolExecutor.create)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "IToolExecutor object expression: { new IToolExecutor with member _.ExecuteAsync ... }"
    - "Path validation with trailing-separator fix: rootWithSep = projectRoot + Path.DirectorySeparatorChar"
    - "Private truncateOutput helper reused by all tool output paths (2000-char cap)"
    - "Task {} CE in Cli adapters for async IO (File.WriteAllTextAsync, CancellationToken)"
    - "Expecto tests use try/finally for temp-dir cleanup"

key-files:
  created:
    - src/BlueCode.Cli/Adapters/BashSecurity.fs
    - src/BlueCode.Cli/Adapters/FsToolExecutor.fs
    - tests/BlueCode.Tests/FileToolsTests.fs
  modified:
    - src/BlueCode.Core/Domain.fs
    - src/BlueCode.Cli/BlueCode.Cli.fsproj
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs

key-decisions:
  - "Domain.fs Tool DU amendment is additive: ReadFile lineRange + ListDir depth as option types. None = Phase 1/2 defaults preserved."
  - "ToolResult DU shape unchanged (frozen): Success | Failure | SecurityDenied | PathEscapeBlocked | Timeout"
  - "BashSecurity.fs is a placeholder: validateCommand always Ok. Compensating control is RunShell remaining a Failure stub until 03-03 fills validators AND 03-02 wires real process launch."
  - "Trailing-separator fix: rootWithSep = projectRoot + DirectorySeparatorChar prevents /a/project-evil from passing /a/project prefix check (PITFALLS.md D-3)"
  - "Tilde (~) prefix rejection: we do not expand home directories; treat as escape attempt."
  - "FileToolsTests.fs compiled BEFORE RouterTests.fs (not after) because F# requires [<EntryPoint>] to be in the last compiled file"
  - "rootTests aggregator in RouterTests.fs explicitly includes fileToolsTests (BlueCode.Tests.FileToolsTests.fileToolsTests)"
  - "Timeout DU ambiguity: Domain has type Timeout = Timeout of int; ToolResult has case | Timeout of seconds. In tests, use BlueCode.Core.Domain.Timeout 30000 for unambiguous construction."

patterns-established:
  - "Adapter compile order: BashSecurity.fs BEFORE FsToolExecutor.fs BEFORE Program.fs (FsToolExecutor will open BashSecurity in 03-02)"
  - "Tool executor uses exhaustive match over Tool DU — adding a Tool case without updating FsToolExecutor is a compile error"
  - "All tool outputs wrapped through truncateOutput before ToolResult construction"
  - "Path validation uses validatePath private helper returning Result<string, ToolResult> for clean match in each tool impl"

# Metrics
duration: 5min
completed: 2026-04-22
---

# Phase 3 Plan 01: Tool Executor Foundation Summary

**FsToolExecutor.create wired to IToolExecutor with read_file/write_file/list_dir (path validation, trailing-separator fix, 2000-char truncation), BashSecurity.fs placeholder, and 15 new Expecto tests covering TOOL-01/02/03/06 (49 total passing)**

## Performance

- **Duration:** 5 min
- **Started:** 2026-04-22T22:37:30Z
- **Completed:** 2026-04-22T22:42:27Z
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments

- Domain.fs `Tool` DU additively amended: `ReadFile` gains `lineRange: (int * int) option` (TOOL-01), `ListDir` gains `depth: int option` (TOOL-03). `ToolResult` DU shape frozen/unchanged.
- `FsToolExecutor.create projectRoot -> IToolExecutor` implemented with exhaustive Tool DU match, path validation (trailing-separator fix per PITFALLS.md D-3), 2000-char `truncateOutput`, and RunShell Failure stub.
- `BashSecurity.fs` scaffolded as a placeholder with `validateCommand` always returning `Ok ()`, providing the frozen API surface for Plan 03-03.
- 15 new Expecto test cases across 4 testLists (ReadFile, WriteFile, ListDir, RunShell stub), all passing alongside the existing 34 Phase 2 tests (49 total, 1 smoke ignored).

## Task Commits

Each task was committed atomically:

1. **Task 1: Amend Domain.fs (additive Tool DU) and scaffold BashSecurity + FsToolExecutor** - `dd9fa28` (feat)
2. **Task 2: Add FileToolsTests.fs** - `b9d9e44` (test)

**Plan metadata:** (docs commit below)

## Files Created/Modified

- `src/BlueCode.Core/Domain.fs` - Tool DU amended: ReadFile + lineRange option, ListDir + depth option. ToolResult untouched.
- `src/BlueCode.Cli/Adapters/BashSecurity.fs` - New: placeholder module, validateCommand always Ok (Plan 03-03 fills)
- `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` - New: create projectRoot -> IToolExecutor; read_file, write_file, list_dir, RunShell stub
- `src/BlueCode.Cli/BlueCode.Cli.fsproj` - Compile order extended: BashSecurity.fs + FsToolExecutor.fs inserted before Program.fs
- `tests/BlueCode.Tests/FileToolsTests.fs` - New: 15 Expecto test cases, 4 testLists
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj` - FileToolsTests.fs added to compile order (before RouterTests.fs)
- `tests/BlueCode.Tests/RouterTests.fs` - rootTests aggregator includes fileToolsTests

## Decisions Made

**1. Domain.fs amendment is additive, not structural**
Tool DU cases amended with optional trailing fields. `None` preserves Phase 1/2 defaults. ToolResult DU shape is frozen per FND-02 + TOOL-07 semantic contract.

**2. BashSecurity.fs + RunShell stub as compensating controls**
BashSecurity.fs `validateCommand` always returns `Ok ()` (all commands "safe"). RunShell returns `Failure (-1, "run_shell not implemented in 03-01...")`. The two stubs are interlocked: a live RunShell with a permissive validator would be a security hole. Plan 03-02 fills RunShell; Plan 03-03 fills the validators. Only when both are complete is run_shell safe to enable.

**3. Trailing-separator fix for path validation**
`rootWithSep = projectRoot + Path.DirectorySeparatorChar` ensures `/a/project-evil` does NOT pass the `/a/project` prefix check. Without this, a sibling-named directory would escape containment. See PITFALLS.md D-3 and 03-RESEARCH.md Pattern 2.

**4. Tilde rejection**
`inputPath.StartsWith("~")` is explicitly rejected before Path.Combine, because .NET does not expand `~` and `Path.Combine(root, "~/foo")` resolves to `root/~/foo` which would pass the StartsWith check. We reject it as a PathEscapeBlocked.

**5. FileToolsTests.fs compiled BEFORE RouterTests.fs**
F# requires `[<EntryPoint>]` to appear in the LAST compiled file. Adding FileToolsTests.fs after RouterTests.fs would move the EntryPoint away from the last position, causing FS0433. Solution: insert FileToolsTests.fs BEFORE RouterTests.fs in compile order, and explicitly include `fileToolsTests` in `rootTests` aggregator.

**6. Timeout DU name collision**
`Domain.Timeout` (type: `Timeout of int`) and `ToolResult.Timeout` (case: `| Timeout of seconds: int`) both open via `open BlueCode.Core.Domain`. F# resolves `Timeout 30000` as `ToolResult.Timeout`. In tests, use `BlueCode.Core.Domain.Timeout 30000` for unambiguous construction of the single-case DU wrapper.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] FileToolsTests.fs placed BEFORE RouterTests.fs (not after)**
- **Found during:** Task 2 (FileToolsTests.fs registration)
- **Issue:** Plan said "FileToolsTests.fs added after existing test files" but F# FS0433 error requires `[<EntryPoint>]` to be in the last file. Adding after RouterTests.fs breaks build.
- **Fix:** Placed FileToolsTests.fs BEFORE RouterTests.fs in BlueCode.Tests.fsproj. Added `BlueCode.Tests.FileToolsTests.fileToolsTests` to `rootTests` in RouterTests.fs explicitly.
- **Files modified:** tests/BlueCode.Tests/BlueCode.Tests.fsproj, tests/BlueCode.Tests/RouterTests.fs
- **Verification:** `dotnet build` 0 errors; 49 tests pass
- **Committed in:** b9d9e44

**2. [Rule 1 - Bug] Timeout DU case collision in FileToolsTests.fs**
- **Found during:** Task 2 (FileToolsTests.fs compilation)
- **Issue:** `Timeout 30000` in `RunShell (Command "echo hi", Timeout 30000)` resolved as `ToolResult.Timeout` (not `Domain.Timeout` wrapper type) due to F# open-module shadowing — FS0001 type mismatch.
- **Fix:** Used fully qualified `BlueCode.Core.Domain.Timeout 30000`.
- **Files modified:** tests/BlueCode.Tests/FileToolsTests.fs
- **Verification:** Build succeeds; RunShell stub test passes
- **Committed in:** b9d9e44

---

**Total deviations:** 2 auto-fixed (2 Rule 1 bugs — F# compile constraints)
**Impact on plan:** Both are F# language constraints, not scope changes. Plan intent fully preserved.

## Issues Encountered

- FS0433 (`[<EntryPoint>]` must be in last compiled file) — resolved by reordering test file compile sequence.
- FS0001 type mismatch on `Timeout` DU name collision — resolved by using fully qualified name.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

**Ready for Plan 03-02 (run_shell):**
- `FsToolExecutor.fs` RunShell branch is a Failure stub at `runShellStub ()` call site — Plan 03-02 replaces this with real `System.Diagnostics.Process` launch wired through `BashSecurity.validateCommand`.
- Hand-off contract: replace `runShellStub ()` with implementation accepting `(projectRoot, command, timeout, ct)`.

**Ready for Plan 03-03 (BashSecurity validators):**
- `BashSecurity.fs` `validateCommand` signature is frozen: `string -> Result<unit, string>`. Plan 03-03 replaces the stub body only — no fsproj changes needed.
- Hand-off contract: 21 validators ported from `claw-code-agent/src/bash_security.py`.

**No blockers for subsequent plans.** BlueCode.Cli.fsproj compile order is final for Phase 3 (BashSecurity before FsToolExecutor before Program). No new NuGet packages. All 49 tests green.

---
*Phase: 03-tool-executor*
*Completed: 2026-04-22*
