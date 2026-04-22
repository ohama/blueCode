---
phase: 03-tool-executor
plan: 02
subsystem: tool-executor
tags: [fsharp, process-management, bash-security, expecto, dotnet10, run-shell, cancellation-token]

# Dependency graph
requires:
  - phase: 03-01
    provides: FsToolExecutor.fs scaffold with runShellStub; file/write/list tools implemented; IToolExecutor port
  - phase: 03-03
    provides: BashSecurity.fs with 22 validators and validateCommand entry point

provides:
  - Full runShellImpl in FsToolExecutor.fs: BashSecurity gate + /bin/bash process lifecycle
  - RunShellTests.fs with 8 Expecto tests covering all ToolResult cases from run_shell
  - TOOL-07 ToolResult semantic contract complete (all 5 cases producible from real tool calls)
  - Phase 3 ROADMAP all 6 success criteria met and empirically verifiable

affects:
  - phase-04-agent-loop (run_shell is the primary tool for code-execution actions)
  - phase-05-cli (--timeout flag will promote _timeoutMs to real parameter in runShellImpl)

tech-stack:
  added: []
  patterns:
    - "Security gate before process spawn: validateCommand ALWAYS runs before Process.Start"
    - "Concurrent stdout/stderr drain via F# 10 let!/and! (dotnet/runtime #98347 deadlock avoidance)"
    - "Two-stage output cap: resource cap (100KB/10KB) before message-history cap (2000 chars TOOL-06)"
    - "Linked CancellationTokenSource for timeout + caller cancel disambiguation"
    - "Process.Kill(entireProcessTree=true) for zombie prevention on timeout and exception paths"
    - "Pattern-match start result (Ok/Error) then use-dispose in Ok branch — avoids nested try/finally type errors in F# task CE"

key-files:
  created:
    - tests/BlueCode.Tests/RunShellTests.fs
  modified:
    - src/BlueCode.Cli/Adapters/FsToolExecutor.fs
    - tests/BlueCode.Tests/FileToolsTests.fs
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs

key-decisions:
  - "30s timeout hardcoded in SHELL_TIMEOUT_SECONDS — Tool.RunShell Timeout field deferred to Phase 5 --timeout flag"
  - "/bin/bash -c (not /bin/sh) — BashSecurity validators assume bash semantics"
  - "Process.Start result wrapped in Ok/Error rather than try/finally to avoid F# task CE type constraint errors"
  - "use _ = proc for Dispose scope — cleaner than try/finally in task CE"
  - "ToolResult.Timeout 30 (seconds) returned on timeout; SHELL_TIMEOUT_SECONDS * 1000 carried in ToolFailure for error fidelity"
  - "RunShellTests.fs registered in compile order BEFORE RouterTests.fs (FS0433: EntryPoint must be in last compiled file)"
  - "runShellTests added to rootTests aggregator in RouterTests.fs (Expecto manual aggregation pattern)"

patterns-established:
  - "Security gate pattern: validate BEFORE spawn, never after"
  - "Disambiguation pattern: check ct.IsCancellationRequested to distinguish caller cancel vs timeout"

duration: 7min
completed: 2026-04-22
---

# Phase 3 Plan 02: run_shell Implementation Summary

**Full runShellImpl wired to BashSecurity gate, /bin/bash process lifecycle with concurrent streams, 30s timeout, two-stage output cap, and 8 Expecto tests covering all 5 ToolResult cases (TOOL-04/05/06/07 complete)**

## Performance

- **Duration:** 7 min
- **Started:** 2026-04-22T23:05:00Z
- **Completed:** 2026-04-22T23:12:00Z
- **Tasks:** 2
- **Files modified:** 5 (FsToolExecutor.fs, FileToolsTests.fs, BlueCode.Tests.fsproj, RouterTests.fs, RunShellTests.fs created)

## Accomplishments

- Replaced `runShellStub` with production `runShellImpl` — 7-step flow: validate → spawn → linked-CTS → concurrent read → await exit → cancel-dispatch → two-stage cap → ToolResult mapping
- Security gate (BashSecurity.validateCommand) is unconditionally the FIRST check; Process.Start is never reached if validateCommand returns Error
- All 5 ToolResult cases now producible from real tool invocations (TOOL-07 semantic contract closed)
- RunShellTests.fs: 8 real-process tests pass including 30-second sleep timeout and sentinel-file survival proof

## Task Commits

Each task was committed atomically:

1. **Task 1: Implement runShellImpl** - `cd66c74` (feat)
2. **Task 2: Add RunShellTests.fs; remove runShellStubTest** - `11edca7` (feat)

## runShellImpl Flow (7 Steps)

```
1. BashSecurity.validateCommand cmd
      Error reason  → return Ok (SecurityDenied reason)    [Process NEVER spawned]
      Ok ()         → continue to step 2

2. Build ProcessStartInfo:
      FileName = "/bin/bash", Args = ["-c"; cmd]
      RedirectStandardOutput/Error = true
      UseShellExecute = false, CreateNoWindow = true
      WorkingDirectory = projectRoot                       [working-dir lock]

3. Linked CancellationTokenSource:
      cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
      cts.CancelAfter(TimeSpan.FromSeconds 30.0)           [30s hardcoded]
      → fires on caller cancel (ct) OR timeout (30s)

4. Process.Start — wrapped in Ok/Error (not try/finally):
      Error ex → return Error (ToolFailure (RunShell ..., ex))
      Ok proc  → continue with use _ = proc

5. Concurrent stdout/stderr read (F# 10 let!/and!):
      let! stdout = proc.StandardOutput.ReadToEndAsync(cts.Token)
      and! stderr = proc.StandardError.ReadToEndAsync(cts.Token)
      → both streams drained concurrently (dotnet/runtime #98347 deadlock avoidance)

6. Wait for exit:
      do! proc.WaitForExitAsync(cts.Token)

7. On OperationCanceledException:
      ct.IsCancellationRequested = true  → Error UserCancelled
      else (timeout)                      → proc.Kill(entireProcessTree=true)
                                            return Ok (ToolResult.Timeout 30)
   On success:
      stdoutCapped = capOutput stdout SHELL_STDOUT_CAP |> truncateOutput   [100KB cap then 2000-char]
      stderrCapped = capOutput stderr SHELL_STDERR_CAP |> truncateOutput   [10KB cap then 2000-char]
      ExitCode = 0  → Ok (Success stdoutCapped)
      ExitCode ≠ 0  → Ok (Failure (exitCode, stderrCapped))
   On other exceptions:
      proc.Kill(entireProcessTree=true) → Error (ToolFailure ...)
```

## Why /bin/bash (not /bin/sh)

BashSecurity.validateCommand was ported from `bash_security.py` which explicitly targets bash semantics: brace expansion `{a,b,c}`, `$()` command substitution, `$((...))` arithmetic, and bash-specific quoting. Running commands via `/bin/sh` could bypass these validators if sh interprets shell constructs differently. `/bin/bash` is always available on macOS (primary target platform).

## Why Concurrent stdout/stderr Read

Sequential read (first drain stdout, then stderr) deadlocks when a process writes enough to fill the OS stderr pipe buffer but nobody is reading stderr — the process blocks on stderr write, we block on stdout read = permanent deadlock. This is documented as dotnet/runtime #98347 and PITFALLS.md C-2. The F# 10 `let!/and!` in task CE drains both streams concurrently.

## Why Process.Kill(entireProcessTree = true) on Both Paths

On macOS, a shell command like `sleep 35` spawns two processes: `/bin/bash` and `sleep`. Killing only the bash parent leaves `sleep` running as a zombie. `.NET 10` `Process.Kill(entireProcessTree: bool)` kills the entire process group. This call is made on both timeout AND unexpected exception paths.

## Two-Stage Cap Rationale

- **Stage 1 (capOutput)**: Resource limit. 100KB stdout / 10KB stderr prevents multi-MB process output from being held in memory or serialized into the LLM context window.
- **Stage 2 (truncateOutput)**: LLM context budget. 2000-char cap keeps tool output within the TOOL-06 message-history limit with a human-readable `[truncated: showing first 2000 of N chars]` marker.

## Why _timeoutMs is Ignored (Phase 5 Deferred)

`Tool.RunShell` carries a `Timeout` field in milliseconds. In Phase 3, the implementation hardcodes 30 seconds via `SHELL_TIMEOUT_SECONDS`. The `_timeoutMs` value is pattern-matched but intentionally ignored — the leading underscore communicates this at the call site. A Phase 5 `--timeout` CLI flag will promote it to an actual parameter. Both `ToolFailure` construction sites carry `Timeout (SHELL_TIMEOUT_SECONDS * 1000)` to keep the error payload aligned with actual runtime behavior (30s = 30000ms). Inline comment at each site: `// 30s hardcoded — _timeoutMs reserved for Phase 5 --timeout flag (see plan objective)`.

## Files Created/Modified

- `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` — runShellStub → runShellImpl; added SHELL_STDOUT_CAP, SHELL_STDERR_CAP, SHELL_TIMEOUT_SECONDS, capOutput; RunShell branch updated
- `tests/BlueCode.Tests/RunShellTests.fs` — 8 Expecto tests (created)
- `tests/BlueCode.Tests/FileToolsTests.fs` — removed runShellStubTest binding and aggregator reference
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj` — added RunShellTests.fs before RouterTests.fs
- `tests/BlueCode.Tests/RouterTests.fs` — added runShellTests to rootTests aggregator

## RunShellTests Coverage Table

| Test | ToolResult case | Plan SC |
|------|-----------------|---------|
| echo hello returns Success with 'hello' | Success | SC 1 (happy path) |
| pwd returns projectRoot (working-dir lock) | Success | working-dir lock |
| rm -rf / → SecurityDenied + sentinel survives | SecurityDenied | SC 3 |
| $(whoami) blocked | SecurityDenied | SC 3 |
| sleep 35 → Timeout 30 in ~30s | Timeout | SC 4 |
| yes \| head -3000 → truncation marker | Success | SC 5 |
| false (exit 1) → Failure code 1 | Failure | TOOL-04 |
| exit 7 → Failure code 7 | Failure | TOOL-04 |

## ROADMAP Success Criterion Coverage Map

| SC | Requirement | Evidence |
|----|-------------|---------|
| SC 1 | read_file valid + line range | FileToolsTests.readFileTests (5 cases) |
| SC 2 | write_file path escape vs valid | FileToolsTests.writeFileTests (4 cases) |
| SC 3 | run_shell dangerous cmd → SecurityDenied WITHOUT executing | RunShellTests.securityTests + sentinel-file assertion |
| SC 4 | run_shell long sleep → Timeout in <31s | RunShellTests.timeoutTests (30.17s elapsed) |
| SC 5 | 2000-char truncation with [truncated: marker | RunShellTests.truncationTests + FileToolsTests large-file test |
| SC 6 | Exhaustive ToolResult match (compile-time proof) | FsToolExecutor.create match tool with; adding a case = compile error |

## Phase 3 Requirement Closure (TOOL-01..07)

- TOOL-01: read_file + optional line range → Plan 03-01
- TOOL-02: write_file + path escape gate → Plan 03-01
- TOOL-03: list_dir + depth limit → Plan 03-01
- TOOL-04: run_shell + 30s timeout + 100KB/10KB caps → **Plan 03-02** (this plan)
- TOOL-05: BashSecurity validator chain (22 validators) → Plan 03-03; consumed by Plan 03-02
- TOOL-06: 2000-char truncation with marker → Plan 03-01 (helper); applied to run_shell in Plan 03-02
- TOOL-07: ToolResult semantic contract — all 5 cases producible → **Plan 03-02** (close-out)

## Decisions Made

- `30s timeout hardcoded` — Tool.RunShell.Timeout field deferred to Phase 5 `--timeout` flag; both ToolFailure sites carry inline comment.
- `/bin/bash` not `/bin/sh` — matches BashSecurity validator assumptions.
- `Process.Start wrapped in Ok/Error` — avoids F# task CE type errors that arise from try/finally inside a match branch inside task {} where the outer return type changes.
- `use _ = proc` — idiomatic Dispose scope management in F# without try/finally.
- `ToolResult.Timeout 30` (seconds) returned — matches ROADMAP language "Timeout in 30s"; not milliseconds.
- RunShellTests registered BEFORE RouterTests.fs (F# FS0433: EntryPoint must be in last compiled file).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] F# task CE type errors with nested try/with inside try/finally**

- **Found during:** Task 1 (runShellImpl implementation)
- **Issue:** The plan's template used `let mutable proc = null` followed by a `try proc <- Process.Start(psi) with ex -> return Error ...` then an outer `try ... finally proc.Dispose()`. In F# task CE, the type of the `try proc <- ... with ex -> return Error ...` block is inferred as `unit` (because Process.Start returns Process and we immediately mutate proc). The subsequent `try/finally` block must then also be unit, conflicting with the `return Ok (...)` expressions inside it.
- **Fix:** Wrapped Process.Start in a `let startResult = try Ok (Process.Start psi) with ex -> Error ex`, then matched on `startResult`: on Error branch return early, on Ok branch use `use _ = proc` for disposal scope. This avoids all nested try/finally complications.
- **Files modified:** src/BlueCode.Cli/Adapters/FsToolExecutor.fs
- **Verification:** `dotnet build BlueCode.slnx` → 0 errors, 0 warnings
- **Committed in:** cd66c74 (Task 1 commit)

**2. [Rule 1 - Bug] Timeout DU name collision with ToolResult.Timeout**

- **Found during:** Task 1 (RunShell branch in create)
- **Issue:** Both `Domain.Timeout` (single-case DU for Tool.RunShell parameter) and `ToolResult.Timeout` (ToolResult case) are in scope when `open BlueCode.Core.Domain` is used. In a pattern match `| RunShell (Command cmd, Timeout _timeoutMs)`, F# resolves `Timeout` to `ToolResult.Timeout` (last defined in the module), causing FS0001 type mismatch.
- **Fix:** Use fully-qualified `BlueCode.Core.Domain.Timeout _timeoutMs` in the RunShell pattern match. Use `ToolResult.Timeout` explicitly when constructing the timeout return value.
- **Files modified:** src/BlueCode.Cli/Adapters/FsToolExecutor.fs
- **Verification:** Build passes with 0 warnings.
- **Committed in:** cd66c74 (Task 1 commit)

**3. [Rule 2 - Missing Critical] rootTests aggregator in RouterTests.fs not mentioned in plan**

- **Found during:** Task 2 (RunShellTests registration)
- **Issue:** The plan mentioned registering RunShellTests.fs in BlueCode.Tests.fsproj compile order, but did not mention adding `BlueCode.Tests.RunShellTests.runShellTests` to the `rootTests` aggregator in RouterTests.fs. Without this, the tests compiled but showed 0 RunShell tests when running via `dotnet run`.
- **Fix:** Added `BlueCode.Tests.RunShellTests.runShellTests` to the rootTests aggregator list in RouterTests.fs.
- **Files modified:** tests/BlueCode.Tests/RouterTests.fs
- **Verification:** `dotnet run --project tests/BlueCode.Tests -- --list-tests` shows 8 RunShell tests; all 153 tests pass.
- **Committed in:** 11edca7 (Task 2 commit)

---

**Total deviations:** 3 auto-fixed (2 Rule 1 - bug fixes, 1 Rule 2 - missing critical)
**Impact on plan:** All auto-fixes necessary for build correctness and test discovery. No scope creep.

## Issues Encountered

None beyond the 3 auto-fixed deviations above.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 3 complete: all 6 ROADMAP success criteria provable from test suite.
- Phase 4 (Agent Loop) can call `FsToolExecutor.create (Directory.GetCurrentDirectory())` at CompositionRoot.fs to build the IToolExecutor instance — this was the planned Phase 4 hand-off.
- Concern (from STATE.md): HttpClient singleton should move to CompositionRoot.fs in Phase 4; Spectre.Console + Serilog stream separation needed when both active.
- run_shell is fully functional; Phase 4 will add Serilog logging wrapping tool invocations.
- Phase 5 `--timeout` flag: promote `_timeoutMs` parameter in runShellImpl from ignored to real parameter.

---
*Phase: 03-tool-executor*
*Completed: 2026-04-22*
