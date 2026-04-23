---
phase: 04-agent-loop
plan: 04
subsystem: cli-rendering
gap_closure: true
tags: [fsharp, rendering, stdout, expecto, sc-7, obs-04, phase-4-gap-closure]

requires:
  - phase: 04-02
    provides: Repl.runSingleTurn with onStep callback (existing hook point)
  - phase: 04-03
    provides: Rendering.renderStep (Compact/Verbose) with DurationMs per OBS-04

provides:
  - src/BlueCode.Cli/Repl.fs:onStep: now emits renderStep Compact step to stdout (SC-7 clause 2 closed)
  - tests/BlueCode.Tests/ReplTests.fs: Expecto test capturing stdout from stubbed runSingleTurn

affects:
  - 05-02 (will replace hard-coded Compact with argv-parsed mode dispatch)

tech-stack:
  added: []
  patterns:
    - "Stdout step-progress emitted from Repl.onStep (between JsonlSink.WriteStep and Log.Debug) preserves SC-6 ordering + OBS-02 stream separation"
    - "Test pattern: redirect Console.Out to StringWriter around runSingleTurn call with stub ILlmClient/IToolExecutor; assert per-step 'ms]' marker count"

key-files:
  created:
    - tests/BlueCode.Tests/ReplTests.fs
  modified:
    - src/BlueCode.Cli/Repl.fs
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs

key-decisions:
  - "Compact mode hard-coded at the call site (Phase 4); argv flag dispatch deferred to Phase 5 plan 05-02 per ROADMAP Phase 5 SC-2"
  - "printfn to stdout (NOT eprintfn, NOT Serilog sink) — Logging.fs stderr-only routing preserved"

# Metrics
duration: 3 min
completed: 2026-04-23
---

# Phase 4 Plan 04: SC-7 Gap Closure — Wire renderStep Compact into Repl.fs onStep Summary

**One-liner:** `printfn "%s" (renderStep Compact step)` inserted in `Repl.onStep` makes renderStep live code; ReplTests captures stdout from stubbed runSingleTurn and asserts `ms]` per-step marker appears.

## Gap Closed

**SC-7 second clause:** "`--verbose` output also shows per-step elapsed time (OBS-04)"

The Phase 4 VERIFICATION.md identified this as the one remaining gap: `renderStep` in `Rendering.fs` was fully implemented and unit-tested but never called from the production Repl path. Every agent run produced only a final answer on stdout — no per-step progress.

This plan closed the gap by wiring `renderStep Compact step` into the existing `onStep` callback in `Repl.fs`, making every step emit one compact line to stdout with a `[ok|fail|aborted, Nms]` DurationMs marker.

## Production Change (Task 1)

**File:** `src/BlueCode.Cli/Repl.fs`

One line inserted in `onStep`, between the existing `JsonlSink.WriteStep` call and the existing `Log.Debug` call:

```fsharp
let onStep (step: Step) =
    components.JsonlSink.WriteStep step
    printfn "%s" (renderStep Compact step)   // <-- ADDED (SC-7 gap closure)
    Log.Debug("Step {Number}: action={Action} duration={DurationMs}ms",
              step.StepNumber, step.Action, step.DurationMs)
```

- `open BlueCode.Cli.Rendering` was already present at Repl.fs:9 — no new open needed.
- `printfn` (stdout) consistent with `renderResult`/`renderError` at lines 63, 68, 72.
- Ordering preserved: JSONL write first (SC-6 crash-safety) → stdout progress → stderr debug log (OBS-02 stream separation).
- `Compact` hard-coded. No argv parsing, no `--verbose` flag. Phase 5 plan 05-02 handles mode dispatch.

## Test (Task 2)

**File created:** `tests/BlueCode.Tests/ReplTests.fs`

One Expecto test (`testCaseAsync`) that:
1. Scripts `ILlmClient` stub to return `[ToolCall "list_dir"; FinalAnswer "done"]` — 2 Steps.
2. Uses `IToolExecutor` stub returning `Ok (Success "stub-output")` for any tool call.
3. Builds `AppComponents` directly (NOT via `CompositionRoot.bootstrap`) to avoid real `QwenHttpClient` network calls.
4. Redirects `Console.Out` to `StringWriter` before calling `runSingleTurn`; restores in `finally`.
5. Asserts: exit code = 0, `>= 2` stdout lines contain `ms]`, `>= 2` stdout lines start with `> ` and contain `ms]` (Compact format).

**Registration (per project convention — no `[<Tests>]` auto-discovery):**
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj`: `<Compile Include="ReplTests.fs" />` inserted before `RouterTests.fs`.
- `tests/BlueCode.Tests/RouterTests.fs`: `BlueCode.Tests.ReplTests.tests` appended to `rootTests` list.

## Verification Results

```
dotnet build BlueCode.slnx --nologo
Build succeeded. 0 Warning(s) 0 Error(s)

dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj -- --sequenced
174 tests run — 174 passed, 1 ignored, 0 failed, 0 errored. Success!
```

- Task 1 commit `e3272f8`: feat(04-04): wire renderStep Compact into Repl onStep
- Task 2 commit `4f1e6db`: test(04-04): add ReplTests stdout capture verifying per-step Compact output

## Scope Boundary Preserved

- No `--verbose`/Argu/argv flag parsing added to `Program.fs` or `Repl.fs`. Compact mode hard-coded. Phase 5 plan 05-02 handles argv dispatch.
- No Spectre.Console panels. No new `RenderMode` variant.
- No `BlueCode.Core.*` files touched (ports-and-adapters invariant).
- `Logging.fs` unchanged; Serilog still routes all events to stderr via `standardErrorFromLevel = Nullable LogEventLevel.Verbose` (OBS-02 preserved).

## Deviations from Plan

None — plan executed exactly as written. Single-line production change + test file + two registration edits. No unexpected issues.

## Phase 4 Status

With this plan complete, all Phase 4 automated success criteria are satisfied:
- SC-7 second clause (per-step elapsed time to stdout): CLOSED by this plan.
- All 7 Phase 4 observable truths now verified (automated). Two human-verification items remain (live Qwen smoke, Ctrl+C) but are structural — not blocked by code gaps.
- Test count: 174 passing, 1 ignored (env-gated smoke), 0 failed.
