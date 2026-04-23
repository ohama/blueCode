---
phase: 05-cli-polish
plan: 02
subsystem: cli
tags: [serilog, levelswitch, rendermode, verbose, trace, spinner, fsharp, spectre]

requires:
  - phase: 05-01
    provides: CliArgs DU (Verbose | Trace parsed), CliOptions record, runSingleTurn, runMultiTurn, 188 tests pass

provides:
  - LoggingLevelSwitch module-level binding in Adapters/Logging.fs with ControlledBy wiring
  - Logging.configure() uses MinimumLevel.ControlledBy(levelSwitch); default Information
  - Program.fs flips levelSwitch.MinimumLevel to Debug when --trace is set
  - RenderMode threaded through runSingleTurn and runMultiTurn as explicit parameter
  - onStep in Repl.fs uses renderStep renderMode (Compact or Verbose per flag)
  - onStep always emits Log.Debug with untruncated action+toolResult+elapsed_ms (gated by levelSwitch)
  - withSpinner in QwenHttpClient.fs updates label every 500ms with elapsed seconds (CLI-05)
  - LoggingTests.fs: 2 scenarios verifying levelSwitch suppress/reveal behavior
  - 2 new ReplTests: verbose-mode multi-line output + compact-mode negative test
  - 192 total tests pass (188 + 4 new)

affects:
  - 05-03 (bootstrapAsync for /v1/models probe; all CLI wiring complete)
  - 05-04 (retirement; no impact from this plan)

tech-stack:
  added: []
  patterns:
    - LoggingLevelSwitch module-level binding initialized before configure() call (Pitfall 7 avoidance)
    - ControlledBy(levelSwitch) replaces MinimumLevel.Debug() for runtime level control
    - RenderMode parameter threaded explicitly through function chain (not global state)
    - Log.Debug always emitted in onStep; Serilog switch gates visibility (zero-cost when Info)
    - withSpinner uses CancellationTokenSource + background ticker task for live elapsed label

key-files:
  created:
    - tests/BlueCode.Tests/LoggingTests.fs
  modified:
    - src/BlueCode.Cli/Adapters/Logging.fs
    - src/BlueCode.Cli/Repl.fs
    - src/BlueCode.Cli/Program.fs
    - src/BlueCode.Cli/Adapters/QwenHttpClient.fs
    - tests/BlueCode.Tests/ReplTests.fs
    - tests/BlueCode.Tests/RouterTests.fs
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj

key-decisions:
  - "LoggingLevelSwitch is a module-level let binding in Logging.fs (not a parameter to configure()) — initialized when module loads, before configure() runs; configure() references it via closure (Pitfall 7)"
  - "CliArgs.Verbose and CliArgs.Trace must be qualified in Program.fs because open BlueCode.Cli.Rendering introduces RenderMode.Verbose/Compact into scope, causing ambiguity with Argu DU cases"
  - "Spinner elapsed-time via background CancellationTokenSource + Task.Delay(500) ticker — fire-and-forget; finally-block cancels ticker when work() completes"
  - "Log.Debug always called in onStep regardless of levelSwitch — Serilog suppresses before formatting when level is insufficient (zero-cost gate)"
  - "Step.Thought remains '[not captured in v1]' placeholder — verbose mode displays it; thought capture is Phase 6+ scope (Open Question 1 resolution)"

patterns-established:
  - "CliArgs DU disambiguation: when both CliArgs and RenderMode are in scope, qualify CliArgs cases explicitly (results.Contains CliArgs.Verbose)"
  - "testSequenced wrapper already covers all ReplTests — new verbose/compact tests added inside existing testList, no new wrapper needed"
  - "LoggingTests use local LoggingLevelSwitch instances (NOT Logging.levelSwitch) to isolate test state from global logger"

duration: 7min
completed: 2026-04-23
---

# Phase 5 Plan 02: LoggingLevelSwitch + RenderMode threading + --trace/--verbose + spinner elapsed Summary

**Serilog LoggingLevelSwitch wired for runtime --trace gating, RenderMode threaded through Repl for --verbose, and Spectre spinner extended with live elapsed-second label**

## Performance

- **Duration:** 7 min
- **Started:** 2026-04-23T05:27:18Z
- **Completed:** 2026-04-23T05:34:22Z
- **Tasks:** 2
- **Files modified:** 8

## Accomplishments

- `Logging.fs` rewritten: module-level `LoggingLevelSwitch` initialized at Information; `configure()` uses `.MinimumLevel.ControlledBy(levelSwitch)`; `MinimumLevel.Debug()` removed
- `Repl.fs` updated: `runSingleTurn` and `runMultiTurn` gain explicit `RenderMode` parameter; `onStep` uses `renderStep renderMode`; always emits `Log.Debug` with untruncated action+toolResult+elapsed_ms (visible only when `--trace` flips levelSwitch to Debug)
- `Program.fs` updated: flips `levelSwitch.MinimumLevel` to Debug when `--trace`; derives `renderMode` from `isVerbose`; passes both through dispatch — CLI-03, CLI-04, CLI-07 wiring complete
- `withSpinner` in QwenHttpClient.fs extended with 500ms background ticker updating spinner label with elapsed seconds (CLI-05)
- 4 new tests: 2 LoggingTests (levelSwitch suppress/reveal) + 2 ReplTests (verbose multi-line + compact negative); 192 total pass

## Task Commits

Each task was committed atomically:

1. **Task 1: LoggingLevelSwitch + RenderMode threading + trace-mode Log.Debug** - `e3d8d5b` (feat)
2. **Task 2: LoggingTests + verbose/compact rendering tests + spinner elapsed** - `88cdc8f` (test)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `src/BlueCode.Cli/Adapters/Logging.fs` - LoggingLevelSwitch module-level; ControlledBy wiring; MinimumLevel.Debug() removed
- `src/BlueCode.Cli/Repl.fs` - runSingleTurn/runMultiTurn gain RenderMode parameter; onStep uses renderStep renderMode + Log.Debug always emits
- `src/BlueCode.Cli/Program.fs` - levelSwitch flip post-parse; renderMode derived from isVerbose; CliArgs qualification fix for ambiguity
- `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` - withSpinner extended with CTS + background ticker for elapsed-second label (CLI-05)
- `tests/BlueCode.Tests/LoggingTests.fs` - NEW: 2 levelSwitch scenarios with local CaptureSink
- `tests/BlueCode.Tests/ReplTests.fs` - open BlueCode.Cli.Rendering added; RenderMode.Compact supplied to existing tests; 2 new verbose/compact tests
- `tests/BlueCode.Tests/RouterTests.fs` - LoggingTests.tests added to rootTests list
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj` - LoggingTests.fs registered before RouterTests.fs

## Decisions Made

1. **LoggingLevelSwitch as module-level let**: Declared before `configure()` as a top-level binding so the logger creation captures it by reference (not by value). Mutating it post-`CreateLogger()` is the supported Serilog pattern (Research § Pattern 4, Pitfall 7).

2. **CliArgs disambiguation in Program.fs**: After adding `open BlueCode.Cli.Rendering`, `results.Contains Verbose` resolved to `RenderMode.Verbose` instead of `CliArgs.Verbose`. Fixed by qualifying all Argu case references as `CliArgs.Prompt`, `CliArgs.Verbose`, `CliArgs.Trace`, `CliArgs.Model`.

3. **Spinner elapsed-time approach**: Chose live ticker (background task with `CancellationTokenSource`) rather than the static-label fallback. The ticker fires-and-forgets; `finally` block cancels it when work completes. If ticker hits `OperationCanceledException` from Task.Delay cancellation, it exits silently. Non-TTY environments: Spectre no-ops the status callback entirely, so the ticker never fires — safe.

4. **Step.Thought remains placeholder**: Verbose mode prints `thought: [not captured in v1]` — accepted for Phase 5 per Open Question 1 decision. CLI-03 intent satisfied via action/input/output/status visibility.

5. **Log.Debug always called**: The `Log.Debug(...)` call in `onStep` always executes regardless of `levelSwitch`. Serilog internally checks the level before formatting the message template, so there is no string-formatting overhead when level is Information. This is the correct Serilog performance pattern.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] CliArgs.Verbose ambiguity with RenderMode.Verbose**

- **Found during:** Task 1 (build attempt after adding `open BlueCode.Cli.Rendering`)
- **Issue:** `results.Contains Verbose` failed to compile — F# resolved `Verbose` to `RenderMode.Verbose` (wrong type) after the new `open` was added; Argu's `Contains` expects the CliArgs DU case.
- **Fix:** Qualified all Argu case references as `CliArgs.Prompt`, `CliArgs.Verbose`, `CliArgs.Trace`, `CliArgs.Model` in Program.fs.
- **Files modified:** `src/BlueCode.Cli/Program.fs`
- **Verification:** `dotnet build BlueCode.slnx` exits 0 after qualification.
- **Committed in:** `e3d8d5b` (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 — type resolution bug due to new open statement)
**Impact on plan:** Essential for correctness. No scope creep.

## Spectre Spinner Pattern

The extended `withSpinner` uses:
```fsharp
.StartAsync(label, fun ctx ->
    task {
        use cts = new CancellationTokenSource()
        let sw = Stopwatch.StartNew()
        let _ticker = task {
            try
                while not cts.Token.IsCancellationRequested do
                    do! Task.Delay(500, cts.Token)
                    ctx.Status <- sprintf "%s %ds" label (int sw.Elapsed.TotalSeconds)
            with :? OperationCanceledException -> ()
        }
        try return! work ()
        finally cts.Cancel()
    })
```

`_ticker` is fire-and-forget (not awaited). The `finally` block cancels it when `work()` completes. `Task.Delay(500, cts.Token)` throws `OperationCanceledException` on cancellation, which the inner `with` catches silently. Non-TTY: Spectre no-ops the entire callback.

## Known Limitation: Step.Thought = "[not captured in v1]"

Verbose mode (`--verbose`) displays:
```
[Step 1] (ok, 423ms)
  thought: [not captured in v1]
  action:  read_file {"path":"README.md"}
  result:  Success (3421 chars)
```

Capturing the actual thought from the LLM requires `ILlmClient.CompleteAsync` to return `Thought * LlmOutput` instead of `LlmOutput`. This is Phase 6+ scope. CLI-03 is accepted as delivered for Phase 5 because action/input/output/status visibility is fully achieved.

## Next Phase Readiness

- 05-03 can add `getMaxModelLenAsync` to QwenHttpClient.fs and `/v1/models` probe in bootstrapAsync — all CLI flag wiring is now complete
- `blueCode --verbose "<prompt>"` prints multi-line step output; `blueCode "<prompt>"` prints compact one-liners; `blueCode --trace "<prompt>"` emits `[DBG]` lines to stderr; all three independently composable
- Core purity preserved: no Serilog references in src/BlueCode.Core/

---
*Phase: 05-cli-polish*
*Completed: 2026-04-23*
