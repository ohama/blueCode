---
phase: 05-cli-polish
plan: 03
subsystem: cli
tags: [vllm, models-probe, context-warning, fantomas, fsharp, spectre, serilog, obs-03]

requires:
  - phase: 05-02
    provides: LoggingLevelSwitch, RenderMode threading, onStep Log.Debug, 192 tests pass

provides:
  - tryParseMaxModelLen pure JSON helper in QwenHttpClient.fs (PUBLIC for tests)
  - getMaxModelLenAsync: GET /v1/models on port 8000; fallback 8192 on all failure paths
  - AppComponents.MaxModelLen: int field (default 8192 in sync bootstrap)
  - bootstrapAsync in CompositionRoot.fs: probes /v1/models before returning enriched AppComponents
  - Program.fs switched to bootstrapAsync; logs resolved max_model_len at startup
  - shouldWarnContextWindow pure helper in Repl.fs (PUBLIC for testability)
  - onStep accumulates action+result char counts per turn; fires WARNING once via printfn when chars*5 >= maxModelLen*16
  - ModelsProbeTests.fs: 8 test scenarios for JSON parse fallback paths
  - ContextWarningTests.fs: 7 boundary tests for shouldWarnContextWindow
  - Integration test in ReplTests: MaxModelLen=10 confirms warning fires once per turn
  - Fantomas 7.0.5 local tool + whole-tree format pass (35 files reformatted)

affects:
  - 05-04 (human checkpoint for Python retirement; no code changes needed)

tech-stack:
  added:
    - Fantomas 7.0.5 (local dotnet tool, .config/dotnet-tools.json)
  patterns:
    - tryParseMaxModelLen as pure helper extracted from getMaxModelLenAsync (testability)
    - printfn (Console.Out) used for WARNING over AnsiConsole.MarkupLine (respects Console.SetOut in tests)
    - Per-turn mutable accumulator pattern: totalChars + warnedThisTurn local to runSingleTurn
    - Integer-only 80% check: totalChars*5 >= maxModelLen*16 (no float arithmetic)

key-files:
  created:
    - tests/BlueCode.Tests/ModelsProbeTests.fs
    - tests/BlueCode.Tests/ContextWarningTests.fs
    - .config/dotnet-tools.json
  modified:
    - src/BlueCode.Cli/Adapters/QwenHttpClient.fs
    - src/BlueCode.Cli/CompositionRoot.fs
    - src/BlueCode.Cli/Repl.fs
    - src/BlueCode.Cli/Program.fs
    - tests/BlueCode.Tests/ReplTests.fs
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs
    - "[35 files reformatted by Fantomas]"

key-decisions:
  - "tryParseMaxModelLen extracted as PUBLIC pure helper so ModelsProbeTests can test JSON fallback paths without HTTP mocking"
  - "sync bootstrap retained alongside bootstrapAsync per Open Question 2 — CompositionRootTests stay fast and network-free; MaxModelLen defaults to 8192"
  - "printfn (Console.Out) used for WARNING instead of AnsiConsole.MarkupLine — AnsiConsole bypasses Console.SetOut in non-TTY/test environments, preventing test capture (Rule 1 bug fix)"
  - "MaxModelLen=10 used in integration test (threshold=32 chars) — any ToolCall step's sprintf-repr exceeds 32 chars, reliably triggering the warning"
  - "80% check uses integer arithmetic: totalChars*5 >= maxModelLen*16 avoids float; derived from totalChars >= maxModelLen*4*0.8"
  - "Fantomas pass committed as isolated style(05-03) commit — keeps git blame tractable for the noisy 35-file diff"
  - "Cross-turn context accumulation is POST-V1 per Open Question 3 — totalChars/warnedThisTurn are local to runSingleTurn, reset per turn"

patterns-established:
  - "Fantomas 7.0.5 local tool: dotnet tool restore resolves it; dotnet fantomas --check exits 0"
  - "ModelsProbeTests pattern: pure helper exposed for testing + live fallback test with closed port"
  - "ContextWarningTests pattern: pure function + boundary-value analysis (below/at/above threshold)"

duration: 12min
completed: 2026-04-23
---

# Phase 5 Plan 03: /v1/models probe + 80% context warning + Fantomas Summary

**vLLM /v1/models probed at startup for real max_model_len (fallback 8192), per-turn 80% context warning added to onStep, and Fantomas 7.0.5 formatting applied to all 35 F# files in src/ and tests/**

## Performance

- **Duration:** 12 min
- **Started:** 2026-04-23T05:36:14Z
- **Completed:** 2026-04-23T05:48:26Z
- **Tasks:** 3
- **Files modified:** 40 (35 Fantomas + 5 feature files + 3 new files)

## Accomplishments

- `getMaxModelLenAsync` probes GET `http://127.0.0.1:8000/v1/models` at startup; `tryParseMaxModelLen` pure helper handles all JSON edge cases (null, missing, empty array, invalid, zero); falls back to 8192 on any failure path
- `bootstrapAsync` added to CompositionRoot.fs; sync `bootstrap` retained for tests (no HTTP); Program.fs switched to `bootstrapAsync` and logs resolved max_model_len
- `shouldWarnContextWindow` pure helper + per-turn accumulator in `onStep`; warning fires once via `printfn` (Console.Out) when `totalChars * 5 >= maxModelLen * 16`; resets naturally per turn
- Fantomas 7.0.5 installed as local tool; 35 files reformatted; build and 208 tests still pass; `--check` exits 0; format commit is isolated in git history
- 208 total tests pass (was 192, +16 new: 8 probe + 7 threshold + 1 integration)

## Task Commits

Each task was committed atomically:

1. **Task 1: /v1/models probe + bootstrapAsync + AppComponents.MaxModelLen** - `7b4a631` (feat)
2. **Task 2: intra-turn 80% context warning + threshold tests** - `efd338e` (feat)
3. **Task 3: Fantomas format pass** - `fa09c98` (style — ISOLATED)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` - tryParseMaxModelLen (PUBLIC pure helper); getMaxModelLenAsync with all fallback paths
- `src/BlueCode.Cli/CompositionRoot.fs` - MaxModelLen field on AppComponents; sync bootstrap (MaxModelLen=8192 default); bootstrapAsync (probes real value)
- `src/BlueCode.Cli/Program.fs` - bootstrapAsync call via GetAwaiter().GetResult(); logs resolved max_model_len
- `src/BlueCode.Cli/Repl.fs` - open Spectre.Console added; shouldWarnContextWindow pure helper; onStep accumulates totalChars + warnedThisTurn; printfn WARNING
- `tests/BlueCode.Tests/ModelsProbeTests.fs` - NEW: 8 scenarios (7 pure parse + 1 live HTTP fallback)
- `tests/BlueCode.Tests/ContextWarningTests.fs` - NEW: 7 boundary tests (below/at/above threshold, suppression, 2K/8K/32K models)
- `tests/BlueCode.Tests/ReplTests.fs` - MaxModelLen field added to 4 AppComponents literals; integration test for 80% warning (MaxModelLen=10)
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj` - ModelsProbeTests.fs + ContextWarningTests.fs registered before RouterTests.fs
- `tests/BlueCode.Tests/RouterTests.fs` - ModelsProbeTests.tests + ContextWarningTests.tests added to rootTests
- `.config/dotnet-tools.json` - NEW: Fantomas 7.0.5 local tool manifest
- `[35 .fs files in src/ and tests/]` - Fantomas-reformatted (style commit, no semantic changes)

## getMaxModelLenAsync Fallback Paths (all produce fallback = 8192)

1. HTTP request throws `HttpRequestException` or `TaskCanceledException` — network error, server down, `ConnectionRefused`
2. HTTP response status is non-2xx — e.g., 404, 500
3. JSON parse fails (invalid JSON body)
4. `data` property missing or not an array
5. `data` array is empty (no models registered)
6. `data[0].max_model_len` property missing
7. `data[0].max_model_len` is null or not a `JsonValueKind.Number`
8. Parsed `int64` value is zero or negative (non-positive rejected)

## Decisions Made

1. **tryParseMaxModelLen as public pure helper**: Extracting the JSON parsing logic as a PUBLIC function (not buried in getMaxModelLenAsync) enables ModelsProbeTests to unit-test all edge cases without HTTP mocking. The alternative (live-fallback-only tests) was less comprehensive.

2. **sync bootstrap retained alongside bootstrapAsync**: Per Open Question 2 resolution — CompositionRootTests.fs remains fast and network-free. The sync bootstrap defaults MaxModelLen = 8192 (conservative floor). The async variant probes the real value at process startup.

3. **printfn over AnsiConsole.MarkupLine for WARNING**: AnsiConsole.MarkupLine routes through Spectre's internal stdout (not System.Console.Out). In test environments using `Console.SetOut` to redirect stdout, AnsiConsole output is not captured. Switching to `printfn` (which uses System.Console.Out) ensures the integration test can verify the warning fires exactly once. This is deviation Rule 1 (testability bug fix).

4. **MaxModelLen=10 in integration test**: With MaxModelLen=10, the threshold is 32 chars. Even the first step's `sprintf "%A" step.Action` (for a ToolCall LlmOutput) produces ~75 chars — reliably crossing the threshold. Using MaxModelLen=100 (threshold=320) proved too close to actual accumulated chars and the test was flaky on boundary.

5. **Integer-only 80% arithmetic**: `totalChars * 5 >= maxModelLen * 16` avoids float arithmetic. Derivation: `totalChars >= maxModelLen * 4 * 0.8 = maxModelLen * 16/5`, rearranged to integer: `totalChars * 5 >= maxModelLen * 16`.

6. **Fantomas as isolated commit**: The 35-file diff is kept in its own `style(05-03)` commit so `git log --oneline` clearly identifies it and `git blame -L n,n <file>` can skip it for feature attribution.

7. **Cross-turn accumulation is POST-V1**: `totalChars` and `warnedThisTurn` are `mutable` locals inside `runSingleTurn`. Multi-turn REPL calls `runSingleTurn` fresh per turn — both reset naturally. Explicit cross-turn accumulation is deferred (Open Question 3 decision).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] AnsiConsole.MarkupLine bypasses Console.SetOut in test environments**

- **Found during:** Task 2 (integration test execution)
- **Issue:** The integration test redirects `Console.Out` using `Console.SetOut(sw)` to capture output. `AnsiConsole.MarkupLine` (as specified in the plan) writes through Spectre's internal stdout writer, which does NOT use `System.Console.Out`. The redirected `StringWriter` never received the WARNING line, causing the test assertion to fail.
- **Fix:** Replaced `AnsiConsole.MarkupLine(...)` with `printfn "WARNING: context at 80%% of model limit ..."`. The `printfn` function uses `System.Console.Out` which respects `Console.SetOut` redirection. In TTY production use, this produces a plain text warning (no yellow color); in tests it is correctly captured.
- **Files modified:** `src/BlueCode.Cli/Repl.fs`
- **Verification:** Integration test passes; `WARNING: context at 80%` appears exactly once in captured stdout.
- **Committed in:** `efd338e` (Task 2 commit)

**2. [Rule 1 - Bug] MaxModelLen=100 threshold too close to accumulated chars (flaky test)**

- **Found during:** Task 2 (integration test execution)
- **Issue:** With MaxModelLen=100, the 80% threshold is 320 chars. The actual accumulated `sprintf "%A"` chars from 2 ToolCall steps in the test was approximately 280-310 chars — close to the threshold but not reliably crossing it.
- **Fix:** Changed the integration test to use `MaxModelLen = 10` (threshold = 32 chars). Any `ToolCall` step's `sprintf "%A" step.Action` representation (~75 chars for `ToolCall (ToolName "list_dir", ToolInput ...)`) comfortably exceeds 32 chars on the first step.
- **Files modified:** `tests/BlueCode.Tests/ReplTests.fs`
- **Verification:** Test passes reliably (threshold exceeded on step 1; warning fires once; subsequent steps suppressed by `warnedThisTurn = true`).
- **Committed in:** `efd338e` (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (both Rule 1 — bugs in plan specification vs. runtime behavior)
**Impact on plan:** Both fixes essential for correctness and testability. No scope creep.

## Fantomas Tool Details

- **Version installed:** 7.0.5
- **Files reformatted:** 35 (all .fs files in src/ and tests/)
- **Files skipped:** 0 (no files reverted — Fantomas produced valid F# for all files)
- **Build after format:** Passes (`dotnet build BlueCode.slnx` exits 0)
- **Tests after format:** 208 pass, 1 ignored smoke (same count as before format)
- **Idempotency:** `dotnet fantomas --check src/ tests/` exits 0 after format pass

## Resolved max_model_len (First Live Run)

Not yet observed in live run (no vLLM server running during development). Expected value for qwen2.5-coder-32b-instruct: `max_model_len = 32768` (per vLLM protocol.py research and 05-RESEARCH.md Pattern 5). Actual value will appear in Serilog startup log as:
```
[INF] Context window resolved: max_model_len=32768
```

## OBS-03 Completion

- Startup probe: `getMaxModelLenAsync` calls GET `/v1/models` at process start via `bootstrapAsync`
- Fallback: 8 distinct failure paths all produce `MaxModelLen = 8192` with Serilog Warning to stderr
- 80% warning: `shouldWarnContextWindow` fires once per turn when `totalChars * 5 >= maxModelLen * 16`; warning text visible on stdout; `warnedThisTurn` gate prevents repetition
- ROADMAP SC-4 met: real `max_model_len` used when vLLM is running; conservative fallback when not

## Next Phase Readiness

- 05-04 (Python retirement checkpoint) is a human-action plan — no further code changes needed
- All v1 requirements in Phase 5 scope now implemented: CLI-01 through CLI-07, OBS-03, ROU-04
- 208 tests pass; Fantomas clean; build exits 0
- `blueCode "<prompt>"` — single turn
- `blueCode` — multi-turn REPL
- `blueCode --verbose "<prompt>"` — verbose rendering
- `blueCode --trace "<prompt>"` — Serilog Debug to stderr
- `blueCode --model 72b "<prompt>"` — forced model
- Startup: probes `/v1/models`, logs `max_model_len`, warns at 80% threshold per turn

---
*Phase: 05-cli-polish*
*Completed: 2026-04-23*
