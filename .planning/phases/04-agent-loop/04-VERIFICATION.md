---
phase: 04-agent-loop
verified: 2026-04-23T02:13:18Z
status: gaps_found
score: 5/7 success criteria fully verified (6/7 automated; 1 human_needed; 1 gap)
must_haves:
  truths:
    - "prompt -> LLM -> tool -> observe loop runs up to 5 iterations"
    - "MaxLoopsExceeded renders as user-readable message, no stack trace"
    - "Same (action, inputHash) 3x trips LoopGuardTripped with descriptive error"
    - "JSON parse failure retries once; both fail -> InvalidJsonOutput, no crash"
    - "Ctrl+C exits cleanly with no OperationCanceledException stack trace"
    - "Every step written as JSONL to ~/.bluecode/session_<timestamp>.jsonl, readable after exit"
    - "JSONL records have startedAt/endedAt/durationMs; --verbose shows per-step elapsed time"
  artifacts:
    - path: "src/BlueCode.Core/AgentLoop.fs"
      provides: "runSession, runLoop, callLlmWithRetry, checkLoopGuard"
    - path: "src/BlueCode.Core/Domain.fs"
      provides: "Step record with StartedAt/EndedAt/DurationMs"
    - path: "src/BlueCode.Cli/CompositionRoot.fs"
      provides: "bootstrap — wires all adapters"
    - path: "src/BlueCode.Cli/Repl.fs"
      provides: "runSingleTurn — Ctrl+C handler, onStep callback, exit codes"
    - path: "src/BlueCode.Cli/Program.fs"
      provides: "entry point — configure, bootstrap, runSingleTurn"
    - path: "src/BlueCode.Cli/Rendering.fs"
      provides: "renderStep, renderResult, renderError"
    - path: "src/BlueCode.Cli/Adapters/JsonlSink.fs"
      provides: "JsonlSink — AutoFlush JSONL writer to ~/.bluecode/"
    - path: "src/BlueCode.Cli/Adapters/Logging.fs"
      provides: "Serilog stderr sink (OBS-02)"
  key_links:
    - from: "Repl.fs onStep"
      to: "JsonlSink.WriteStep"
      via: "components.JsonlSink.WriteStep step"
    - from: "Program.fs"
      to: "Repl.runSingleTurn"
      via: "Repl.runSingleTurn prompt components"
    - from: "AgentLoop.runLoop"
      to: "MaxLoopsExceeded"
      via: "if loopN >= config.MaxLoops then return Error MaxLoopsExceeded"
    - from: "AgentLoop.checkLoopGuard"
      to: "LoopGuardTripped"
      via: "if count >= 2 then Error (LoopGuardTripped actionName)"
    - from: "AgentLoop.callLlmWithRetry"
      to: "InvalidJsonOutput"
      via: "two-attempt retry; Error (InvalidJsonOutput raw) on second failure"
    - from: "Repl.fs"
      to: "renderStep Verbose"
      via: "NOT WIRED — renderStep never called from onStep or any Repl path"
gaps:
  - truth: "--verbose output shows per-step elapsed time (SC-7 second clause)"
    status: failed
    reason: "renderStep (Compact/Verbose) is implemented and tested in isolation but is never called from Repl.fs or Program.fs. The --verbose argv flag is not parsed anywhere in the CLI. No step progress is printed to stdout during a run; only the final answer or error message reaches stdout."
    artifacts:
      - path: "src/BlueCode.Cli/Repl.fs"
        issue: "onStep callback at line 40-43 calls JsonlSink.WriteStep and Log.Debug only — renderStep is absent"
      - path: "src/BlueCode.Cli/Program.fs"
        issue: "No --verbose / argv flag parsing; renderStep is never imported or called"
    missing:
      - "Parse --verbose flag from argv in Program.fs (or defer to Phase 5 Argu wiring)"
      - "Call renderStep mode step inside Repl.fs onStep callback to print per-step progress to stdout"
      - "Either wire --verbose -> Verbose mode or default to Compact and defer verbose to Phase 5"
    note: "The plans explicitly defer --verbose flag wiring to Phase 5 CLI-03 (ROADMAP.md line 106, 04-02-SUMMARY.md line 154). The ROADMAP.md SC-7 criterion includes this as a Phase 4 requirement. The mechanism (renderStep Verbose with DurationMs) is in place; only the argv-to-RenderMode dispatch is missing."
human_verification:
  - test: "SC-1 live Qwen smoke: run blueCode against real Qwen endpoint"
    expected: "'list files in the current directory' completes in ≤5 tool steps and prints a final answer to stdout"
    why_human: "Requires live Qwen (vLLM at localhost:8000). Env-gated smoke test exists at tests/BlueCode.Tests/AgentLoopSmokeTests.fs — enable with BLUECODE_AGENT_SMOKE=1"
    command: "BLUECODE_AGENT_SMOKE=1 dotnet test BlueCode.slnx --filter \"FullyQualifiedName~AgentLoop.Smoke\" --nologo"
  - test: "SC-5 Ctrl+C clean exit: press Ctrl+C during LLM inference"
    expected: "Process exits with code 130, prints 'Cancelled.' to stdout, NO OperationCanceledException stack trace on stderr"
    why_human: "Requires live Qwen to create an in-progress LLM call to cancel. Structural checks pass (CancellationTokenSource + ConsoleKeyPress handler wired; OCE caught defensively)."
    command: "dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj -- \"explain the full codebase in detail\" # then Ctrl+C"
---

# Phase 4: Agent Loop — Verification Report

**Phase Goal:** A single turn runs prompt → LLM → tool → observe up to 5 times and produces a final answer; every error and limit condition is a typed value the caller handles at compile time.
**Verified:** 2026-04-23T02:13:18Z
**Status:** gaps_found
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Loop runs prompt→LLM→tool→observe up to 5 iterations | VERIFIED | `AgentLoop.fs:217` `if loopN >= config.MaxLoops then return Error MaxLoopsExceeded`; test: "max iter: 5 distinct ToolCalls without FinalAnswer -> MaxLoopsExceeded" passes |
| 2 | MaxLoopsExceeded renders as user-readable message, no stack trace | VERIFIED | `Rendering.fs:93` `MaxLoopsExceeded -> "Max loops exceeded (5 steps with no final answer)."`; `Repl.fs:72` `printfn "%s" (renderError e)`; RenderingTests "renderError produces user-readable messages" passes |
| 3 | Same (action, inputHash) 3x trips LoopGuardTripped with descriptive error | VERIFIED | `AgentLoop.fs:117-122` `checkLoopGuard` with `if count >= 2`; `Rendering.fs:94` produces named action in message; AgentLoopTests "loop guard: 3x same (action, input) trips LoopGuardTripped" passes |
| 4 | JSON parse failure retries once; both fail → InvalidJsonOutput, no crash | VERIFIED | `AgentLoop.fs:137-157` `callLlmWithRetry` two-attempt pattern with correction message; original raw preserved on double failure; AgentLoopTests "JSON retry exhausted" passes |
| 5 | Ctrl+C exits cleanly, no OperationCanceledException stack trace | HUMAN_NEEDED | `Repl.fs:30-32` Console.CancelKeyPress wired; `args.Cancel <- true` prevents kill; OCE caught at line 55; `renderError UserCancelled = "Cancelled."` (single line). Structural check passes; live run needed |
| 6 | Every step written as JSONL to ~/.bluecode/session_<timestamp>.jsonl | VERIFIED | `JsonlSink.fs:34-51` AutoFlush StreamWriter; `Repl.fs:41` `components.JsonlSink.WriteStep step`; JsonlSinkTests "AutoFlush: file readable mid-session" and "WriteStep appends one JSONL line" pass |
| 7 | JSONL records have startedAt/endedAt/durationMs; --verbose shows elapsed time | PARTIAL | JSONL field presence verified (JsonlSinkTests "JSONL step record contains startedAt, endedAt, durationMs" passes). --verbose flag not parsed; renderStep never called from Repl.fs |

**Score:** 5/7 truths fully verified (1 human_needed, 1 partial/gap)

---

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/BlueCode.Core/AgentLoop.fs` | runSession, loop guard, JSON retry | VERIFIED | 306 lines; exports `runSession`, `callLlmWithRetry`, `checkLoopGuard`, `dispatchTool`; all substantive |
| `src/BlueCode.Core/Domain.fs` | Step with StartedAt/EndedAt/DurationMs | VERIFIED | Lines 115-125: Step record has all 3 timing fields typed correctly (DateTimeOffset, int64) |
| `src/BlueCode.Cli/CompositionRoot.fs` | bootstrap function | VERIFIED | 60 lines; `bootstrap` wires QwenHttpClient + FsToolExecutor + JsonlSink + AgentConfig; MaxLoops=5, ContextCapacity=3 |
| `src/BlueCode.Cli/Repl.fs` | runSingleTurn with Ctrl+C | VERIFIED | 78 lines; CancellationTokenSource + ConsoleKeyPress; onStep writes JSONL; renderResult/renderError on exit; exit codes 0/1/130 |
| `src/BlueCode.Cli/Program.fs` | entry point | VERIFIED | 70 lines; Logging.configure first; bootstrap + use _jsonlSink; runSingleTurn; CloseAndFlush |
| `src/BlueCode.Cli/Rendering.fs` | renderStep, renderResult, renderError | VERIFIED (partial) | renderStep(Compact/Verbose) implemented with DurationMs; renderError exhaustive over all 8 AgentError cases; **NOT called from Repl.fs** |
| `src/BlueCode.Cli/Adapters/JsonlSink.fs` | JSONL writer at ~/.bluecode/ | VERIFIED | AutoFlush=true; append=true; UTF-8; path format `session_<ts>.jsonl`; IDisposable with Flush+Dispose |
| `src/BlueCode.Cli/Adapters/Logging.fs` | Serilog stderr sink | VERIFIED | `standardErrorFromLevel = Nullable LogEventLevel.Verbose` routes all log levels to stderr; format `[LVL] {Message}` |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `AgentLoop.runLoop` | `Error MaxLoopsExceeded` | `if loopN >= config.MaxLoops` | WIRED | `AgentLoop.fs:217` |
| `AgentLoop.checkLoopGuard` | `Error (LoopGuardTripped)` | `if count >= 2` | WIRED | `AgentLoop.fs:117-122` |
| `AgentLoop.callLlmWithRetry` | `Error (InvalidJsonOutput raw)` | second attempt also fails | WIRED | `AgentLoop.fs:154`; original raw preserved |
| `Repl.fs onStep` | `JsonlSink.WriteStep` | `components.JsonlSink.WriteStep step` | WIRED | `Repl.fs:41` |
| `Repl.fs` | `renderError` | `printfn "%s" (renderError e)` | WIRED | `Repl.fs:68,72` |
| `Program.fs` | `Logging.configure` | `configure()` at line 27 | WIRED | `Program.fs:27` |
| `Program.fs` | `use _jsonlSink` | `use _jsonlSink = components.JsonlSink` | WIRED | `Program.fs:49` — IDisposable guarantee |
| `Repl.fs onStep` | `renderStep mode step` | — | NOT WIRED | `renderStep` only appears in `Rendering.fs` (definition) and `RenderingTests.fs` (tests); never called from `Repl.fs` or `Program.fs` |
| argv `--verbose` | `RenderMode.Verbose` | — | NOT WIRED | `Program.fs` has no flag parsing; `RenderMode` DU exists but no dispatch from argv |

---

### Requirements Coverage

| Requirement | Status | Evidence / Blocking Issue |
|-------------|--------|--------------------------|
| LOOP-01 (max 5 iteration loop) | SATISFIED | `AgentLoop.fs:217` + MaxLoops=5 in config; AgentLoopTests test 2 |
| LOOP-02 (MaxLoopsExceeded as AgentError case) | SATISFIED | `Domain.fs:99`; `Rendering.fs:93`; compile-time exhaustive match enforced |
| LOOP-03 (one tool per response — schema enforces single action) | SATISFIED | JSON schema validated in Phase 2 LLM pipeline; `dispatchTool` handles one action per response |
| LOOP-04 (loop guard: 3x same (action, hash) blocked) | SATISFIED | `AgentLoop.fs:109-122`; AgentLoopTests "loop guard" test |
| LOOP-05 (JSON retry up to 2 attempts, then InvalidJsonOutput) | SATISFIED | `AgentLoop.fs:129-157`; AgentLoopTests "JSON retry exhausted" test |
| LOOP-06 (message history: immutable ring buffer, last N=3 steps) | SATISFIED | `ContextBuffer.fs` append-and-truncate; `AgentConfig.ContextCapacity = 3` |
| LOOP-07 (Ctrl+C: graceful exit with step summary, no stack trace) | PARTIAL | Exit clean — VERIFIED; "step summary" is "Cancelled." (one line); no step progress output because renderStep is not called. LOOP-07 Korean text says "step summary" — the Cancelled. line satisfies no-crash; the progress display does not exist |
| OBS-01 (JSONL per-step to ~/.bluecode/session_<ts>.jsonl) | SATISFIED | `JsonlSink.fs`; `Repl.fs:41`; JsonlSinkTests pass |
| OBS-02 (Serilog structured log to stderr) | SATISFIED | `Logging.fs:23` standardErrorFromLevel=Verbose routes all to stderr |
| OBS-04 (startedAt/endedAt/durationMs in Step + JSONL + --verbose) | PARTIAL | Step record fields: VERIFIED. JSONL serialization: VERIFIED. --verbose display: NOT WIRED (Phase 5 CLI-03) |

---

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `AgentLoop.fs` | 8 | `/// Known v1 limitation: Step.Thought is populated with "[not captured in v1]"` | Info | Thought field always "[not captured in v1]" — by design, documented, not a bug |
| `Repl.fs` | — | `renderStep` not called from onStep | Warning | No per-step progress displayed to stdout during a run; only final answer or error is printed |

No TODO/FIXME/placeholder patterns found in any Phase 4 source file. No empty return stubs. All critical handlers are substantive.

---

### Human Verification Required

#### 1. SC-1 Live Qwen End-to-End (smoke test)

**Test:** With vLLM serving Qwen 32B at localhost:8000, run:
```
BLUECODE_AGENT_SMOKE=1 dotnet test BlueCode.slnx --filter "FullyQualifiedName~AgentLoop.Smoke" --nologo
```
Or directly:
```
dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj -- "list files in the current directory"
```
**Expected:** Completes in ≤5 tool steps; final answer printed to stdout; JSONL file created at `~/.bluecode/session_*.jsonl` with N lines matching N steps.
**Why human:** Requires live Qwen vLLM endpoint at localhost:8000. Env-gated smoke test (AgentLoopSmokeTests.fs) is structurally correct.

#### 2. SC-5 Ctrl+C Clean Exit

**Test:**
```
dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj -- "explain the entire codebase in detail"
# Press Ctrl+C while LLM is responding
```
**Expected:** Process exits with code 130; stdout shows "Cancelled."; NO `OperationCanceledException` stack trace on stderr.
**Why human:** Requires in-progress LLM call to cancel. Structural mechanism (CancelKeyPress handler, OCE catch, renderError) is in place.

---

### Gaps Summary

**One gap blocks a complete Phase 4 sign-off:**

**SC-7 second clause — `--verbose` per-step elapsed time display:**
`renderStep` in `Rendering.fs` correctly formats per-step output including `DurationMs` in both Compact (`"> reading file... [ok, 423ms]"`) and Verbose (`"[Step 1] (ok, 423ms)"`) modes. Rendering tests pass. However, `renderStep` is never called anywhere in the production code path — not from `Repl.fs`, not from `Program.fs`. The `--verbose` argv flag is not parsed. No step progress reaches stdout during a normal run; only the final answer is printed.

The Phase 4 plans explicitly defer `--verbose` flag dispatch to Phase 5 CLI-03 (ROADMAP.md Phase 5 SC-2; 04-02-SUMMARY.md line 154). The ROADMAP.md Phase 4 SC-7 as written includes the `--verbose` clause in the Phase 4 gate. This creates a defined gap: the rendering infrastructure is Phase 4-complete but the CLI-side wiring is not.

**Suggested fix (minimal):** In `Repl.fs` `onStep`, add `printfn "%s" (renderStep Compact step)` (defaulting to Compact) to print one-line progress per step to stdout. Full `--verbose` dispatch can remain Phase 5. This satisfies SC-7's intent that "output shows per-step elapsed time" without requiring Argu.

---

## Test Run Summary

```
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
173 tests run — 173 passed, 1 ignored, 0 failed, 0 errored.
```

The 1 ignored test is the env-gated live Qwen smoke stub (expected; passes when `BLUECODE_AGENT_SMOKE=1`).

**Phase 4 relevant tests all pass:**
- AgentLoop: 6 tests (happy path, MaxLoopsExceeded, LoopGuardTripped, JSON retry pass, JSON retry exhausted, step timing)
- JsonlSink: 6 tests (path under ~/.bluecode, no colons in filename, WriteStep appends, AutoFlush, OBS-04 fields, IDisposable)
- Rendering: 5 tests (Compact, Verbose, FinalAnswer, renderResult, renderError user-readable messages)
- CompositionRoot: 2 tests (component wiring, system prompt 5 actions)
- AgentLoop.Smoke: 1 disabled stub (live gate documented above)

---

_Verified: 2026-04-23T02:13:18Z_
_Verifier: Claude (gsd-verifier)_
