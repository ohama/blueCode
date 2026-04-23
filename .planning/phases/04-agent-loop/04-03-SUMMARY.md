---
phase: 04-agent-loop
plan: 03
subsystem: observability
tags: [fsharp, serilog, jsonl, rendering, cli-adapters, expecto, crash-safety, spectre-console]

# Dependency graph
requires:
  - phase: 04-01
    provides: Step record with StartedAt/EndedAt/DurationMs (OBS-04 timing fields)
  - phase: 02-llm-client
    provides: jsonOptions singleton in Adapters/Json.fs (reused by JsonlSink)

provides:
  - Adapters/Logging.fs: configure() Serilog setup writing to stderr at Verbose+ (OBS-02)
  - Adapters/JsonlSink.fs: buildSessionLogPath + JsonlSink type with WriteStep + IDisposable (OBS-01 + SC-6)
  - Rendering.fs: renderStep (Compact/Verbose) + renderResult + renderError for all AgentError cases (OBS-04 + SC-5 + SC-7)
  - Serilog 4.3.1 + Serilog.Sinks.Console 6.1.1 NuGet references (BlueCode.Cli only)
  - 11 new Expecto tests (6 JsonlSink + 5 Rendering) — 170 total tests pass

affects:
  - 04-02 (CompositionRoot wires Logging.configure, constructs JsonlSink, Repl calls renderStep/renderError)

# Tech tracking
tech-stack:
  added:
    - "Serilog 4.3.1"
    - "Serilog.Sinks.Console 6.1.1"
  patterns:
    - "JSONL via stdlib StreamWriter with AutoFlush=true (not Serilog.Sinks.File) — deterministic flush semantics"
    - "standardErrorFromLevel = Nullable LogEventLevel.Verbose routes all Serilog events to stderr (OBS-02)"
    - "JsonlSink(path) constructor takes explicit path — tests inject temp paths; buildSessionLogPath() is the default"
    - "renderError exhaustive match over AgentError — no %A pretty-print, no stack traces (SC-5)"
    - "RenderMode Compact | Verbose — Phase 5 CLI-07 --trace adds separate Serilog sink, NOT a third RenderMode"

key-files:
  created:
    - src/BlueCode.Cli/Adapters/Logging.fs
    - src/BlueCode.Cli/Adapters/JsonlSink.fs
    - src/BlueCode.Cli/Rendering.fs
    - tests/BlueCode.Tests/JsonlSinkTests.fs
    - tests/BlueCode.Tests/RenderingTests.fs
  modified:
    - src/BlueCode.Cli/BlueCode.Cli.fsproj
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs

key-decisions:
  - "JSONL uses stdlib StreamWriter (not Serilog.Sinks.File) — deterministic AutoFlush without Serilog buffering"
  - "standardErrorFromLevel = Nullable LogEventLevel.Verbose sends ALL levels to stderr (OBS-02 separation)"
  - "renderError exhaustively matches all AgentError cases with user-readable messages (no %A, no stack traces)"
  - "RenderMode Compact/Verbose — Phase 5 CLI-07 --trace adds a separate Serilog output path, not a third RenderMode"

# Metrics
duration: 4min
completed: 2026-04-23
---

# Phase 4 Plan 03: Rendering + Logging + JSONL + Ctrl+C Infrastructure Summary

**Serilog stderr sink (OBS-02) + AutoFlush JSONL session log (OBS-01 + SC-6) + Compact/Verbose step renderer (SC-7 + SC-5) in 3 new Cli-layer files + 11 tests; 170 tests pass**

## Performance

- **Duration:** ~4 min
- **Started:** 2026-04-23T00:38:07Z
- **Completed:** 2026-04-23T00:42:16Z
- **Tasks:** 3
- **Files created:** 5 / modified: 3

## Accomplishments

- Added Serilog 4.3.1 + Serilog.Sinks.Console 6.1.1 to BlueCode.Cli only (Core stays pure)
- Created Adapters/Logging.fs: `configure()` initializes Log.Logger writing all events to stderr via `standardErrorFromLevel = Nullable LogEventLevel.Verbose` (OBS-02); `shutdown()` calls `Log.CloseAndFlush()`
- Created Adapters/JsonlSink.fs: `buildSessionLogPath()` produces `~/.bluecode/session_<ts>.jsonl` with no colons in filename; `JsonlSink` type with `WriteStep` (uses `jsonOptions` singleton), `AutoFlush=true` (SC-6 crash-safe), `append=true`, `IDisposable`
- Created Rendering.fs: `RenderMode` DU (Compact | Verbose); `renderStep` shows DurationMs in both modes (SC-7); `renderResult` echoes FinalAnswer; `renderError` produces one-line user-readable strings for all 8 `AgentError` cases (SC-5, SC-2)
- Created JsonlSinkTests.fs: 6 tests covering path layout, colon exclusion, 3-line emission, AutoFlush mid-session, OBS-04 timing fields in JSON, IDisposable
- Created RenderingTests.fs: 5 tests covering Compact/Verbose shape, FinalAnswer in both modes, renderResult, renderError user messages

## Task Commits

1. **Task 1: Add Serilog NuGets + Adapters/Logging.fs** — `b5e5249` (feat)
2. **Task 2: JsonlSink.fs + Rendering.fs** — `71a6da1` (feat)
3. **Task 3: JsonlSinkTests.fs + RenderingTests.fs** — `b3cf005` (test)

## Files Created/Modified

- `src/BlueCode.Cli/BlueCode.Cli.fsproj` — Serilog 4.3.1 + Serilog.Sinks.Console 6.1.1 NuGet refs; compile order: FsToolExecutor.fs → Logging.fs → JsonlSink.fs → Rendering.fs → Program.fs
- `src/BlueCode.Cli/Adapters/Logging.fs` — configure() + shutdown(); `standardErrorFromLevel = Nullable<LogEventLevel>(LogEventLevel.Verbose)` (OBS-02)
- `src/BlueCode.Cli/Adapters/JsonlSink.fs` — buildSessionLogPath(), JsonlSink type (WriteStep, AutoFlush, IDisposable) (OBS-01 + SC-6)
- `src/BlueCode.Cli/Rendering.fs` — RenderMode DU, renderStep, renderResult, renderError (OBS-04 + SC-5 + SC-7)
- `tests/BlueCode.Tests/JsonlSinkTests.fs` — 6 Expecto tests
- `tests/BlueCode.Tests/RenderingTests.fs` — 5 Expecto tests
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj` — JsonlSinkTests.fs + RenderingTests.fs inserted before RouterTests.fs
- `tests/BlueCode.Tests/RouterTests.fs` — JsonlSinkTests.tests + RenderingTests.tests added to rootTests

## Decisions Made

- **JSONL uses stdlib StreamWriter (not Serilog.Sinks.File):** Stdlib gives deterministic AutoFlush semantics. Serilog file sink has buffering and background-thread flush that complicates the SC-6 crash-safety contract (research § PITFALLS.md). StreamWriter.AutoFlush=true guarantees each line reaches the OS buffer before the next step starts.
- **`standardErrorFromLevel = Nullable LogEventLevel.Verbose`:** Routes ALL Serilog log levels (Verbose through Fatal) to stderr. This is the minimal configuration that prevents any Serilog event from appearing on stdout, where Spectre.Console renders the spinner + panels (OBS-02 separation).
- **renderError exhaustive match:** Every `AgentError` case produces a user-readable one-liner. No `%A` format, no exception `.ToString()` dump, no stack traces. This satisfies SC-5 (Ctrl+C → "Cancelled." one-liner) and SC-2 (MaxLoopsExceeded message) with no wiring needed in 04-02 beyond calling `renderError`.
- **RenderMode Compact | Verbose (not a third --trace mode):** Phase 5 CLI-07 `--trace` is a diagnostic log output path — it should be a Serilog sink writing structured JSON to a separate file, not a display mode. Adding a third `RenderMode` case would conflate logging infrastructure with display infrastructure.

## Deviations from Plan

None — plan executed exactly as written.

## Requirements Closed

- **OBS-01** (JSONL session log per step): WriteStep implementation + file-path semantics verified by 4 JsonlSink tests.
- **OBS-02** (Serilog writes to stderr, not stdout): `standardErrorFromLevel = Nullable LogEventLevel.Verbose` in Logging.fs.
- **OBS-04** (timing fields in step output): JSONL test verifies `startedAt`/`endedAt`/`durationMs` serialization; RenderingTests verify `423ms` in Compact and Verbose display.
- **SC-6** (JSONL readable after process exits): AutoFlush + append pattern tested with mid-session read before Dispose. Full SC-6 (readable after a real crashed session) will be proven in 04-02 with a wired session.
- **SC-7 part 2** (`--verbose` shows per-step elapsed time): `renderStep Verbose` includes `%dms` field; test asserts "423ms" present.

## Partial Requirements (mechanism in place, wiring deferred)

- **SC-5** (Ctrl+C → "Cancelled." no stack trace): `renderError UserCancelled = "Cancelled."` is in place and tested. 04-02 wires the `Console.CancelKeyPress` handler that produces `UserCancelled` and calls `renderError`.
- **SC-6** (crash durability): AutoFlush contract is verified; 04-02 constructs JsonlSink and wires it to `onStep`.

## Deferred to 04-02

- `Program.fs` calls `Logging.configure()` at startup
- `CompositionRoot` constructs `JsonlSink` and passes `sink.WriteStep` as the `onStep` callback to `runSession`
- `Repl.fs` calls `renderStep mode step` on each step callback; calls `renderResult` / `renderError` on session end
- `Console.CancelKeyPress` handler that triggers `CancellationTokenSource.Cancel()` (producing `UserCancelled`)

## Known Limitations

None introduced in this plan.

## Next Phase Readiness

- **04-02 (CompositionRoot wiring):** All three modules are ready to wire. Logging.configure(), JsonlSink(buildSessionLogPath()), and renderStep/renderResult/renderError signatures are final.
- **Concern (from STATE.md):** Serilog + Spectre.Console stream separation (Serilog → stderr, Spectre → stdout) — architecture confirmed in Logging.fs; 04-02 must not add any Spectre writes to stderr or Serilog writes to stdout.

---
*Phase: 04-agent-loop*
*Completed: 2026-04-23*
