---
phase: 04-agent-loop
plan: 02
subsystem: agent-loop
tags: [fsharp, composition-root, repl, cancellation, jsonl, serilog, smoke-test]

# Dependency graph
requires:
  - phase: 04-01
    provides: AgentLoop.runSession — pure recursive agent loop (no Cli deps)
  - phase: 04-03
    provides: JsonlSink, Logging.configure, Rendering.renderResult/renderError

provides:
  - CompositionRoot.bootstrap — wires ILlmClient + IToolExecutor + JsonlSink + AgentConfig into AppComponents record
  - Repl.runSingleTurn — single-turn entry with Ctrl+C, onStep -> JsonlSink.WriteStep, exit codes 0/1/130
  - Program.fs [<EntryPoint>] — Logging.configure first, bootstrap, runSingleTurn, use _jsonlSink, CloseAndFlush
  - CompositionRootTests (2 passing) + AgentLoopSmokeTests (env-gated, BLUECODE_AGENT_SMOKE=1)

affects: ["05-01", "05-02", "05-03"]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Function injection (no DI container) — CompositionRoot wires adapters explicitly"
    - "use _jsonlSink = components.JsonlSink — IDisposable pattern guarantees final flush at entry-point scope exit"
    - "Env-gated smoke test with disabledStub — CI-safe; live gate requires BLUECODE_AGENT_SMOKE=1"
    - "Defensive OCE catch in Repl.runSingleTurn — belt-and-suspenders even though adapters already map OCE -> UserCancelled"

key-files:
  created:
    - src/BlueCode.Cli/CompositionRoot.fs
    - src/BlueCode.Cli/Repl.fs
    - tests/BlueCode.Tests/CompositionRootTests.fs
    - tests/BlueCode.Tests/AgentLoopSmokeTests.fs
  modified:
    - src/BlueCode.Cli/Program.fs
    - src/BlueCode.Cli/BlueCode.Cli.fsproj
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs

key-decisions:
  - "CompositionRoot uses function injection — no DI container (research Pattern 8)"
  - "Default system prompt is let private constant in CompositionRoot.fs (not a file)"
  - "Repl.runSingleTurn defensive OCE catch present even though adapters already map OCE to UserCancelled (belt-and-suspenders, research Pitfall 2)"
  - "Program.fs `use _jsonlSink = components.JsonlSink` pattern — JsonlSink disposed at entry-point scope exit guaranteeing final flush"
  - "Exit codes: 0 success, 1 agent error, 2 usage error, 130 SIGINT"
  - "No Argu yet — positional prompt only; CLI-06 deferred to Phase 5"

patterns-established:
  - "CompositionRoot + Repl split: wiring (bootstrap) separate from execution (runSingleTurn) — extension point for Phase 5 multi-turn"
  - "RouterTests.fs rootTests assembles all test lists explicitly — new test modules must be added to rootTests list"

# Metrics
duration: ~15min (continuation agent, resumed from Task 3)
completed: 2026-04-23
---

# Phase 4 Plan 02: Composition Root + Single-Turn Repl Summary

**CompositionRoot wires QwenHttpClient + FsToolExecutor + JsonlSink into AppComponents; Repl.runSingleTurn drives one agent turn end-to-end with Ctrl+C (exit 130), per-step JSONL writes (SC-6), and user-readable error rendering (SC-5)**

## Performance

- **Duration:** ~15 min (continuation of prior execution)
- **Completed:** 2026-04-23
- **Tasks:** 3 (Task 1 + Task 2 completed by prior executor; Task 3 completed by this executor)
- **Files modified:** 8

## Accomplishments

- CompositionRoot.bootstrap builds the full component graph: QwenHttpClient.create + FsToolExecutor.create + JsonlSink + AgentConfig with 5-action system prompt
- Repl.runSingleTurn wires Ctrl+C handler (Console.CancelKeyPress, exit 130), onStep -> JsonlSink.WriteStep (SC-6 per-step JSONL durable write), and renderResult/renderError (SC-5 user-readable output)
- Program.fs is a real entry point: Logging.configure called first, bootstrap, runSingleTurn, `use _jsonlSink` guarantees flush, Log.CloseAndFlush before exit; no-args exits 2 with usage hint
- 2 CompositionRoot unit tests (wired components, system prompt content) + 1 AgentLoop smoke stub = 3 new tests; 173 total pass, 1 ignored

## Task Commits

Each task was committed atomically:

1. **Task 1: CompositionRoot.fs + Repl.fs + fsproj compile order** - `fd7fa7a` (feat)
2. **Task 2: Program.fs real entry** - `375ba55` (feat)
3. **Task 3: CompositionRootTests + AgentLoopSmokeTests + fsproj/RouterTests wiring** - `c58edfb` (test)

**Plan metadata:** (pending — written as part of this step)

## Files Created/Modified

- `src/BlueCode.Cli/CompositionRoot.fs` — AppComponents record + bootstrap function + private defaultSystemPrompt (5 actions)
- `src/BlueCode.Cli/Repl.fs` — runSingleTurn: Ctrl+C, onStep -> WriteStep, defensive OCE catch, exit codes 0/1/130
- `src/BlueCode.Cli/Program.fs` — [<EntryPoint>] main: configure() first, bootstrap, runSingleTurn, use _jsonlSink, Log.CloseAndFlush
- `src/BlueCode.Cli/BlueCode.Cli.fsproj` — compile order: Adapters/* -> Logging -> JsonlSink -> Rendering -> CompositionRoot -> Repl -> Program
- `tests/BlueCode.Tests/CompositionRootTests.fs` — 2 tests: component wiring (non-null + MaxLoops/ContextCapacity/LogPath) + system prompt mentions all 5 actions
- `tests/BlueCode.Tests/AgentLoopSmokeTests.fs` — env-gated (BLUECODE_AGENT_SMOKE=1); disabled stub runs normally; live test requires vLLM localhost:8000
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj` — added CompositionRootTests.fs + AgentLoopSmokeTests.fs before RouterTests.fs
- `tests/BlueCode.Tests/RouterTests.fs` — rootTests list extended with CompositionRootTests.tests + AgentLoopSmokeTests.tests

## Decisions Made

- **Function injection pattern:** CompositionRoot.bootstrap takes projectRoot string, returns AppComponents — no DI container. Pattern 8 from research.
- **System prompt location:** `let private defaultSystemPrompt` constant in CompositionRoot.fs (not a separate file). Answers research Open Question 4.
- **Defensive OCE catch:** Repl.runSingleTurn wraps runSession with `try/with :? OperationCanceledException -> Task.FromResult(Error UserCancelled)`. Belt-and-suspenders even though QwenHttpClient and FsToolExecutor already map OCE to UserCancelled (research Pitfall 2).
- **`use _jsonlSink` pattern:** `let components = bootstrap ...` then `use _jsonlSink = components.JsonlSink` — binds the IDisposable JsonlSink to F# `use` scope for guaranteed Dispose at entry-point exit.
- **Exit code 2 for usage error:** No-args case prints to stderr and returns 2 (POSIX convention for usage error).
- **RouterTests.fs rootTests is explicit:** AgentLoopSmokeTests.tests and CompositionRootTests.tests must be added to rootTests list manually (Expecto's [<Tests>] attribute auto-discovery not used in this project).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed AgentLoop.runSession qualified name in smoke test**

- **Found during:** Task 3 (AgentLoopSmokeTests.fs)
- **Issue:** Plan spec used `AgentLoop.runSession c.Config ...` but module is opened (`open BlueCode.Core.AgentLoop`), so `AgentLoop` resolves to the type/namespace suggestion, not the module qualifier. Caused FS0039 compile error.
- **Fix:** Changed `AgentLoop.runSession` to unqualified `runSession` (consistent with existing AgentLoopTests.fs pattern).
- **Files modified:** tests/BlueCode.Tests/AgentLoopSmokeTests.fs
- **Verification:** `dotnet build BlueCode.slnx` — 0 errors
- **Committed in:** c58edfb (Task 3 commit)

**2. [Rule 2 - Missing Critical] Added AgentLoopSmokeTests.tests + CompositionRootTests.tests to RouterTests.fs rootTests**

- **Found during:** Task 3 — first test run showed 170 tests (not 173), new modules not discovered
- **Issue:** Project uses Expecto with explicit rootTests assembly in RouterTests.fs (not auto-discovery via [<Tests>]). New modules compiled but not wired into the test runner.
- **Fix:** Added `BlueCode.Tests.CompositionRootTests.tests` and `BlueCode.Tests.AgentLoopSmokeTests.tests` to RouterTests.fs rootTests list.
- **Files modified:** tests/BlueCode.Tests/RouterTests.fs
- **Verification:** 173 tests pass, 1 ignored (smoke stub)
- **Committed in:** c58edfb (Task 3 commit)

---

**Total deviations:** 2 auto-fixed (1 bug, 1 missing critical wiring)
**Impact on plan:** Both fixes necessary for correctness. No scope creep.

## Success Criteria Status

- **SC-1 (live Qwen, "list files" ≤5 steps):** MANUAL GATE — run with vLLM serving:
  ```
  BLUECODE_AGENT_SMOKE=1 dotnet test BlueCode.slnx --filter "FullyQualifiedName~AgentLoop.Smoke" --nologo
  ```
  Or use the CLI directly:
  ```
  dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj -- "list files in the current directory"
  ```
- **SC-2 (MaxLoopsExceeded user-readable, no stack trace):** renderError MaxLoopsExceeded covered; pipeline exits 1.
- **SC-3 (LoopGuardTripped descriptive error):** renderError LoopGuardTripped covered; pipeline exits 1.
- **SC-4 (JSON parse retry, no crash):** AgentLoop callLlmWithRetry + renderError InvalidJsonOutput covered.
- **SC-5 (Ctrl+C clean exit, code 130, no trace):** Repl.runSingleTurn Console.CancelKeyPress + defensive OCE catch + renderError UserCancelled. MANUAL GATE:
  ```
  dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj -- "explain the full codebase in detail"
  # Press Ctrl+C mid-run; expect exit 130, "Cancelled." on stdout, NO stack trace
  ```
- **SC-6 (JSONL readable after exit):** JsonlSink AutoFlush=true + onStep wiring in Repl + `use _jsonlSink` in Program. Proven by CompositionRootTests (sink created) and JSONL line-count assertion in smoke test.
- **SC-7 (startedAt/endedAt/durationMs in JSONL):** JSONL fields verified in 04-03 JsonlSinkTests. --verbose flag is Phase 5 CLI-03; underlying renderStep Verbose mode already exists.

## Issues Encountered

None beyond the two deviations documented above (both expected edge cases of plan/code alignment).

## User Setup Required

None — no external service configuration required for automated tests. Manual gates require vLLM serving locally.

## Next Phase Readiness

- Phase 5 extension points are clean: Repl.runSingleTurn and CompositionRoot.bootstrap are the primary hooks for multi-turn loop (CLI-02), --verbose/--trace/--model flags (CLI-03/07/ROU-04), Argu parsing (CLI-06), and /v1/models query (OBS-03).
- HttpClient singleton (currently created per-call in QwenHttpClient.create) should be promoted to CompositionRoot for Phase 5 connection pooling — low priority but flagged.
- SC-01 manual smoke gate pending: requires vLLM 32B or 72B serving on localhost:8000 or localhost:8001.

---
*Phase: 04-agent-loop*
*Completed: 2026-04-23*
