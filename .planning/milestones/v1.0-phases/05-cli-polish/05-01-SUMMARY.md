---
phase: 05-cli-polish
plan: 01
subsystem: cli
tags: [argu, fsharp, repl, multi-turn, forced-model, cli-parsing]

requires:
  - phase: 04-agent-loop
    provides: AgentLoop.runSession, CompositionRoot.bootstrap, Repl.runSingleTurn, 174 passing tests

provides:
  - Argu 6.2.5 NuGet reference in BlueCode.Cli.fsproj
  - CliArgs DU (Prompt | Verbose | Trace | Model) in src/BlueCode.Cli/CliArgs.fs (testable module)
  - CliOptions record + defaultCliOptions + parseForcedModel in CompositionRoot.fs
  - AgentConfig.ForcedModel field (only Core change in Phase 5)
  - runSession honours ForcedModel via Option.defaultWith
  - Repl.runMultiTurn loop (CLI-02): /exit and EOF (null ReadLine) exit; SIGINT per-turn
  - Program.fs Argu parser: single-turn vs REPL dispatch; exit 2 on parse error
  - 13 CliArgsTests scenarios; 2 ReplTests (runSingleTurn + runMultiTurn)

affects:
  - 05-02 (verbose/trace rendering wiring; CliOptions.Verbose/Trace already parsed)
  - 05-03 (bootstrapAsync for /v1/models probe; CliOptions.ForcedModel already threading correctly)

tech-stack:
  added:
    - Argu 6.2.5 (CLI argument parsing, declarative DU-based)
  patterns:
    - CliArgs DU extracted into separate module (not in Program.fs) for testability
    - testSequenced wrapping tests that share Console.SetOut global state
    - parseForcedModel raises; Program.fs catches with exit 2 (usage error convention)
    - runMultiTurn placed AFTER runSingleTurn in Repl.fs (F# compile order)

key-files:
  created:
    - src/BlueCode.Cli/CliArgs.fs
    - tests/BlueCode.Tests/CliArgsTests.fs
  modified:
    - src/BlueCode.Cli/BlueCode.Cli.fsproj (Argu 6.2.5 ref, CliArgs.fs compile entry)
    - src/BlueCode.Core/AgentLoop.fs (ForcedModel field + runSession Option.defaultWith)
    - src/BlueCode.Cli/CompositionRoot.fs (CliOptions, defaultCliOptions, parseForcedModel, bootstrap signature)
    - src/BlueCode.Cli/Repl.fs (runMultiTurn added after runSingleTurn)
    - src/BlueCode.Cli/Program.fs (Argu parser replaces manual argv handling)
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj (CliArgsTests.fs registered)
    - tests/BlueCode.Tests/RouterTests.fs (CliArgsTests.tests added to rootTests)
    - tests/BlueCode.Tests/ReplTests.fs (runMultiTurn test + testSequenced wrapper)
    - tests/BlueCode.Tests/CompositionRootTests.fs (bootstrap signature update)
    - tests/BlueCode.Tests/AgentLoopSmokeTests.fs (bootstrap signature update)
    - tests/BlueCode.Tests/AgentLoopTests.fs (AgentConfig ForcedModel = None added)

key-decisions:
  - "CliArgs DU extracted to src/BlueCode.Cli/CliArgs.fs (not inline in Program.fs) so tests can import and exercise Argu parser without referencing an EntryPoint module"
  - "parseForcedModel raises on invalid model; Program.fs wraps in try/with -> exit 2 (not ArguParseException path, which requires Argu to know the model strings)"
  - "runMultiTurn must be defined AFTER runSingleTurn in Repl.fs due to F# compile-order semantics"
  - "testSequenced wraps ReplTests testList because Console.SetOut is global mutable state; Expecto runs testList items in parallel by default"
  - "Multi-turn REPL uses no cross-turn message history (POST-V1 explicit scope)"
  - "130 exit code from per-turn SIGINT is translated to 0 for REPL's lastCode tally; process only exits 130 in single-turn mode"

patterns-established:
  - "Argu parsing: ParseCommandLine(inputs, raiseOnUsage=true) + catch ArguParseException -> exit 2"
  - "Console-redirecting tests: wrap in testSequenced to prevent parallel execution interference"
  - "parseForcedModel: string option -> Model option pattern (CLI-to-Domain mapping in CompositionRoot, not Core)"

duration: 13min
completed: 2026-04-23
---

# Phase 5 Plan 01: Argu + Multi-turn REPL + --model override + AgentConfig.ForcedModel Summary

**Argu 6.2.5 CLI parser wired with CliArgs DU, multi-turn REPL loop via Repl.runMultiTurn, and AgentConfig.ForcedModel routing --model flag through Core without touching any other Core file**

## Performance

- **Duration:** 13 min
- **Started:** 2026-04-23T05:10:13Z
- **Completed:** 2026-04-23T05:23:08Z
- **Tasks:** 3
- **Files modified:** 11

## Accomplishments

- Argu 6.2.5 added; Program.fs completely replaced with declarative Argu parser (CliArgs DU: Prompt | Verbose | Trace | Model)
- `AgentConfig.ForcedModel: Model option` added as the single Core change for Phase 5; `runSession` resolves model via `Option.defaultWith (fun () -> classifyIntent |> intentToModel)`
- `Repl.runMultiTurn` implemented: reads stdin in a loop, exits on `/exit` or `Console.ReadLine() = null` (EOF/Ctrl+D), continues REPL after per-turn SIGINT (exit 130 translated to 0 for tally)
- 13 CliArgsTests covering all parse scenarios; 1 runMultiTurn test; test count up 174 → 188

## Task Commits

Each task was committed atomically:

1. **Task 1: Add Argu NuGet + AgentConfig.ForcedModel + compile-clean baseline** - `efcb3ff` (feat)
2. **Task 2: CliArgs DU + CliOptions record + runMultiTurn + Program.fs Argu wiring** - `bfc4afd` (feat)
3. **Task 3: CliArgsTests + ReplTests multi-turn + CompositionRootTests fix + rootTests registration** - `367ca38` (test)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `src/BlueCode.Cli/CliArgs.fs` - CliArgs DU extracted for testability; IArgParserTemplate impl
- `src/BlueCode.Cli/BlueCode.Cli.fsproj` - Argu 6.2.5 PackageReference; CliArgs.fs compile order before Program.fs
- `src/BlueCode.Core/AgentLoop.fs` - ForcedModel field on AgentConfig; runSession model selection via Option.defaultWith
- `src/BlueCode.Cli/CompositionRoot.fs` - CliOptions record, defaultCliOptions, parseForcedModel, bootstrap(projectRoot, opts) signature
- `src/BlueCode.Cli/Repl.fs` - runMultiTurn added after runSingleTurn (F# order constraint)
- `src/BlueCode.Cli/Program.fs` - Full Argu rewrite; single-turn vs REPL dispatch; exit 2 for usage errors
- `tests/BlueCode.Tests/CliArgsTests.fs` - 13 parse scenarios for CliArgs DU + parseForcedModel
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj` - CliArgsTests.fs registered before RouterTests.fs
- `tests/BlueCode.Tests/RouterTests.fs` - BlueCode.Tests.CliArgsTests.tests added to rootTests list
- `tests/BlueCode.Tests/ReplTests.fs` - runMultiTurn test; testSequenced wrapper; testCase (not testCaseAsync)
- `tests/BlueCode.Tests/CompositionRootTests.fs` - bootstrap calls updated to pass defaultCliOptions
- `tests/BlueCode.Tests/AgentLoopSmokeTests.fs` - bootstrap call updated to pass defaultCliOptions
- `tests/BlueCode.Tests/AgentLoopTests.fs` - testConfig AgentConfig gains ForcedModel = None

## Decisions Made

1. **CliArgs in separate module**: Extracted to `src/BlueCode.Cli/CliArgs.fs` (not inline in `Program.fs`) because F# EntryPoint modules can't be opened by test projects. This matches the plan's "Option A".

2. **parseForcedModel exit 2 path**: `parseForcedModel` raises a generic exception on invalid model strings. Program.fs wraps the call in a `try/with` that calls `exit 2`, not an `ArguParseException`. This gives a clean `ERROR: Unknown model: unknown...` message and exits 2 as required.

3. **runMultiTurn after runSingleTurn**: F# requires functions to be defined before they're used in the same file. `runMultiTurn` calls `runSingleTurn`, so it must appear AFTER it.

4. **testSequenced for ReplTests**: Expecto runs `testList` items in parallel by default. Both Repl tests redirect `Console.SetOut`, which is global process state. Without `testSequenced`, they intermittently capture each other's output. The wrapper ensures sequential execution.

5. **130 exit code in REPL**: `runMultiTurn` translates exit 130 (SIGINT per-turn cancel) to 0 for the running tally. The REPL continues. The final exit code reflects the last non-cancelled turn result.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] testCaseAsync -> testCase + testSequenced for Console-redirecting tests**
- **Found during:** Task 3 (test execution)
- **Issue:** Expecto runs `testList` items in parallel by default. Both Repl tests use `Console.SetOut` (global state). Using `testCaseAsync` caused interleaving: each test captured the other's stdout, causing both to fail.
- **Fix:** Changed to `testCase` (synchronous `.GetAwaiter().GetResult()`) and wrapped the `testList` in `testSequenced` to force sequential execution.
- **Files modified:** `tests/BlueCode.Tests/ReplTests.fs`
- **Verification:** Both Repl tests pass individually and together in the full suite.
- **Committed in:** `367ca38` (Task 3 commit)

**2. [Rule 1 - Bug] runMultiTurn placement: must be AFTER runSingleTurn**
- **Found during:** Task 2 (compile attempt)
- **Issue:** Plan showed `runMultiTurn` added before `runSingleTurn` in Repl.fs. F# compile-order means forward references are errors. `runMultiTurn` calls `runSingleTurn`, so the placement must be reversed.
- **Fix:** Wrote `runMultiTurn` after `runSingleTurn` in Repl.fs.
- **Files modified:** `src/BlueCode.Cli/Repl.fs`
- **Verification:** Build clean; Repl.runMultiTurn call in Program.fs resolves.
- **Committed in:** `bfc4afd` (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (both Rule 1 — bugs in plan ordering/test strategy)
**Impact on plan:** Both fixes essential for correctness. No scope creep.

## Issues Encountered

- `AgentLoopSmokeTests.fs` and `CompositionRootTests.fs` also needed `bootstrap` signature update (had `open BlueCode.Cli.CompositionRoot` already, just needed `defaultCliOptions` added to call site). Minor, handled in Task 2.

## Scope Boundary Reinforcement

- `--verbose` and `--trace` are PARSED into `CliOptions.Verbose/Trace` fields but NO rendering behaviour changes. `Repl.onStep` still calls `renderStep Compact` hardcoded. `LoggingLevelSwitch` is NOT added. Both are Plan 05-02's scope.
- Cross-turn conversation memory: NOT added. Each `runMultiTurn` iteration calls `runSession` fresh with no accumulated messages. This is the POST-V1 explicit deferral.

## Next Phase Readiness

- 05-02 can immediately wire `CliOptions.Verbose` → `renderStep` mode and `CliOptions.Trace` → `LoggingLevelSwitch.MinimumLevel`.
- 05-03 can add `bootstrapAsync` (for `/v1/models` probe) alongside existing `bootstrap` — the `CliOptions` record already threads through.
- `blueCode --model 72b "..."` routes to localhost:8001 (proven by LlmUnreachable URL showing port 8001 when server is down).
- All 188 tests pass; 1 smoke ignored.

---
*Phase: 05-cli-polish*
*Completed: 2026-04-23*
