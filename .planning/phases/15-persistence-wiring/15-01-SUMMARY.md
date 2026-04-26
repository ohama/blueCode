---
phase: 15-persistence-wiring
plan: 01
subsystem: persistence
tags: [fsharp, session, jsonl, file-io, repl, agent-loop, cancellation-token]

# Dependency graph
requires:
  - phase: 14-domain-extensions
    provides: Session record (Domain.fs:206), ISessionStore port (Ports.fs:27-29), SessionId DU, AgentError.SessionCorrupt/SessionNotFound variants
provides:
  - runSession accepts priorSteps: Step list — prior-turn steps replayed into ContextBuffer before loop starts
  - FileSessionStore adapter — Save writes v2 JSONL (header + TurnComplete envelope); Load stubbed for 15-02
  - runMultiTurnWithSession — explicit Session + ISessionStore params, accumulates steps across turns, saves per turn
  - runMultiTurn — legacy delegate to runMultiTurnWithSession with fresh Session + FileSessionStore
  - newSessionId / buildSessionPath helpers exposed at module scope
affects: [15-02, 15-03, phase-16]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "ContextBuffer fold-replay: prior steps primed via List.fold (fun b s -> ContextBuffer.add s b) ctx0 before runLoop"
    - "Last-envelope-wins JSONL: each Save appends full cumulative steps; Load takes the last TurnComplete envelope"
    - "Legacy delegate: runMultiTurn creates fresh Session+FileSessionStore then delegates to runMultiTurnWithSession (keeps Program.fs unchanged until 15-02)"
    - "Tuple return from runSingleTurn: (int * Step list) so callers accumulate steps without separate callback"

key-files:
  created:
    - src/BlueCode.Cli/Adapters/FileSessionStore.fs
  modified:
    - src/BlueCode.Core/AgentLoop.fs
    - src/BlueCode.Cli/Repl.fs
    - src/BlueCode.Cli/Program.fs
    - src/BlueCode.Cli/BlueCode.Cli.fsproj
    - tests/BlueCode.Tests/AgentLoopTests.fs
    - tests/BlueCode.Tests/AgentLoopSmokeTests.fs
    - tests/BlueCode.Tests/ReplTests.fs

key-decisions:
  - "priorSteps replayed into ContextBuffer via List.fold, NOT passed to runLoop steps accumulator — runLoop.steps collects only current-turn steps; Repl is responsible for concatenation"
  - "turnIndex computed from FinalAnswer step count (not from a passed-in counter) — self-contained Save, no caller bookkeeping"
  - "Session id printed to both stdout and stderr (eprintfn 'Session: %s') — stdout for interactive users, stderr for shell scripting/log grep"
  - "Load stubbed returning Error(SessionCorrupt 'Load not yet implemented in 15-01') — 15-02 replaces stub"
  - "FileSessionStore placed after JsonlSink.fs in fsproj — both logs coexist: ~/.bluecode/session_<ts>.jsonl (per-step) and ~/.bluecode/sessions/<id>.jsonl (per-turn)"

patterns-established:
  - "Session accumulation pattern: runMultiTurnWithSession appends AgentResult.Steps to currentSession.Steps each turn, updates LastActivityAt, calls Save before next turn"
  - "Mechanical compile cascade: new runSession parameter requires [] inserted at 9 call sites (8 AgentLoopTests + 1 AgentLoopSmokeTests + 3 ReplTests)"

# Metrics
duration: 35min
completed: 2026-04-26
---

# Phase 15 Plan 01: Persistence Wiring — Repl Threading + FileSessionStore.Save Summary

**runSession extended with priorSteps: Step list replayed into ContextBuffer; FileSessionStore writes v2 JSONL (header + TurnComplete envelope per turn); runMultiTurnWithSession threads Session + persists each turn**

## Performance

- **Duration:** ~35 min
- **Started:** 2026-04-26
- **Completed:** 2026-04-26
- **Tasks:** 3
- **Files modified:** 7 (+ 1 created)

## Accomplishments

- `runSession` now accepts `priorSteps: Step list` — prior-turn steps are replayed into the ContextBuffer via `List.fold` before the recursive loop starts, so the LLM sees cross-turn conversation history
- `FileSessionStore` adapter created at `src/BlueCode.Cli/Adapters/FileSessionStore.fs` — `Save` writes a version-2 header on first call and appends a `TurnComplete` envelope (with cumulative `Session.Steps`) on every subsequent call; `Load` is stubbed for 15-02
- `runMultiTurnWithSession` added to Repl.fs with explicit `Session` + `ISessionStore` parameters — accumulates steps across turns, updates `LastActivityAt`, calls `sessionStore.Save` after each turn, threads `currentSession.Steps` as `priorSteps` into the next turn's `runSession` call
- 9 call-site compile-cascade fixes (8 in `AgentLoopTests.fs`, 1 in `AgentLoopSmokeTests.fs`): `[]` inserted as `priorSteps` argument after `onStep` and before `userInput`; 3 `ReplTests.fs` call sites updated for `runSingleTurn` tuple-return and `[]` priorSteps
- Test baseline **248 passed / 1 ignored / 0 failed** preserved exactly — zero new tests in this plan (15-03 adds them)

## Task Commits

Each task was committed atomically:

1. **Task 1: Extend AgentLoop.runSession to accept prior Step list** - `2feac10` (refactor)
2. **Task 2: FileSessionStore adapter (Save working, Load stub)** - `84c0e91` (feat)
3. **Task 3: Repl threads Session across turns, calls SessionStore.Save per turn** - `915debf` (feat)

## Files Created/Modified

- `src/BlueCode.Core/AgentLoop.fs` — `runSession` gains `priorSteps: Step list` parameter (after `onStep`); `ContextBuffer` primed via `List.fold` before `runLoop`; doc-comment updated
- `src/BlueCode.Cli/Adapters/FileSessionStore.fs` (NEW) — `FileSessionStore` type implementing `ISessionStore`; `Save` writes JSONL header + TurnComplete envelope; `Load` stubbed; `newSessionId` and `buildSessionPath` exposed at module scope
- `src/BlueCode.Cli/BlueCode.Cli.fsproj` — `<Compile Include="Adapters/FileSessionStore.fs" />` inserted after `JsonlSink.fs`, before `Rendering.fs`
- `src/BlueCode.Cli/Repl.fs` — `runSingleTurn` gains `priorSteps: Step list` param and returns `Task<int * Step list>`; `runMultiTurnWithSession` added; `runMultiTurn` delegates to it with fresh Session + FileSessionStore
- `src/BlueCode.Cli/Program.fs` — single-turn call site updated: `let (code, _) = (Repl.runSingleTurn prompt [] components renderMode).GetAwaiter().GetResult()`
- `tests/BlueCode.Tests/AgentLoopTests.fs` — 8 `runSession` call sites updated with `[]` for `priorSteps`
- `tests/BlueCode.Tests/AgentLoopSmokeTests.fs` — 1 `runSession` call site updated with `[]` for `priorSteps`
- `tests/BlueCode.Tests/ReplTests.fs` — 3 `runSingleTurn` call sites updated with `[]` for `priorSteps` and tuple destructure `let! (code, _) = ...`

## On-disk JSONL Format (v2.0 PERSIST-02)

```
Line 1 (header):   {"version":2,"sessionId":"<32-char-hex>","createdAt":"<iso8601>"}
Line N (envelope): {"type":"TurnComplete","turnIndex":<int>,"writtenAt":"<iso8601>","steps":[...]}
```

- Header written once on first `Save` (when file does not yet exist)
- Each `Save` call appends one `TurnComplete` envelope
- `steps` field in envelope = **full cumulative `Session.Steps`** at time of Save (not delta)
- `turnIndex` = count of `FinalAnswer` steps in `session.Steps` (self-computed, no caller input)
- **Last-envelope-wins**: `Load` (15-02) reads the last envelope and uses its `steps` as `Session.Steps`
- Path: `~/.bluecode/sessions/<id>.jsonl` (distinct from existing `~/.bluecode/session_<ts>.jsonl` per-step log)

## Call-Site Compile Cascade (9 sites updated)

All 9 existing `runSession` call sites had `[]` inserted as `priorSteps` (after `onStep`, before `userInput`):

| File | Sites | Change |
|------|-------|--------|
| `tests/BlueCode.Tests/AgentLoopTests.fs` | 8 | `runSession ... onStep [] "..."` |
| `tests/BlueCode.Tests/AgentLoopSmokeTests.fs` | 1 | `runSession ... onStep [] "..."` |

All tests check single-turn behavior — `[]` (no prior steps) is semantically correct.

`runSingleTurn` cascade (3 sites in `ReplTests.fs`): `let! (code, _) = runSingleTurn "..." [] components mode`

## Decisions Made

- `priorSteps` replayed into `ContextBuffer` via `List.fold` before `runLoop` — NOT threaded into `runLoop.steps` accumulator. `runLoop.steps` collects only current-turn steps; Repl concatenates `priorSteps + AgentResult.Steps` for next-turn `priorSteps`. Keeps `AgentResult.Steps` semantically "this turn only".
- `turnIndex` computed from `FinalAnswer` step count inside `Save` — no caller bookkeeping required.
- Session id printed to both stdout (`printfn`) and stderr (`eprintfn`) — stdout for interactive users, stderr for shell scripting/log capture.
- `Load` explicitly stubbed with `Error(SessionCorrupt "Load not yet implemented in 15-01")` — 15-02 replaces with real file-read implementation.
- `runMultiTurn` kept as legacy delegate to `runMultiTurnWithSession` — `Program.fs` unchanged until 15-02 wires `--resume`/`--new-session` flags.
- `[<CLIMutable>]` added to private `SessionHeader` and `TurnEnvelope` record types to satisfy `FSharp.SystemTextJson` deserialization constraints (needed for 15-02 Load).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Added `[<CLIMutable>]` attributes to private JSONL record types**

- **Found during:** Task 2 (FileSessionStore adapter implementation)
- **Issue:** `FSharp.SystemTextJson` requires `[<CLIMutable>]` on F# records to support round-trip deserialization. Without it, `Load` (15-02) would fail at runtime when deserializing the JSONL.
- **Fix:** Added `[<CLIMutable>]` to `SessionHeader` and `TurnEnvelope` private types in `FileSessionStore.fs`.
- **Files modified:** `src/BlueCode.Cli/Adapters/FileSessionStore.fs`
- **Verification:** Build clean; 15-03 round-trip test will confirm end-to-end.
- **Committed in:** `84c0e91` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 — proactive bug prevention for 15-02 Load)
**Impact on plan:** Minimal — single attribute addition. No scope creep. No behavioral change in 15-01 (Load is still stubbed).

## Issues Encountered

None — the three tasks executed cleanly. Build at 0 warnings, 0 errors. Test suite green at 248/1/0 baseline.

## Bench Gate

Not run in this plan — Phase 15 does not change LLM-call shaping, system prompt, or tool dispatch. Green by construction. 15-03 runs `bash bench/run.sh --gate` as the formal gate check.

## Next Phase Readiness

- **15-02** (`--resume`/`--new-session` Argu flags + `CompositionRoot` wiring + full `Load` implementation) can proceed immediately. The `runMultiTurnWithSession` entry point it needs is ready; `FileSessionStore.Load` stub is the only gap.
- **15-03** (round-trip tests + bench gate) can proceed after 15-02. `newSessionId`, `buildSessionPath`, and `FileSessionStore` are testable in isolation.
- **JsonlSink coexistence confirmed** — `git diff HEAD src/BlueCode.Cli/Adapters/JsonlSink.fs` produces no output; both log files coexist as designed.

---
*Phase: 15-persistence-wiring*
*Completed: 2026-04-26*
