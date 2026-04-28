---
phase: "15"
plan: "02"
subsystem: persistence-cli
tags: [argu, session-store, file-io, jsonl, cli-flags]
requires: ["15-01"]
provides: ["--resume flag", "--new-session flag", "FileSessionStore.Load", "CompositionRoot SessionStore wiring", "Program.fs session dispatch"]
affects: ["15-03", "16-01"]
tech-stack:
  added: []
  patterns: ["post-parse mutually-exclusive flag validation", "last-envelope-wins JSONL Load"]
key-files:
  created: []
  modified:
    - src/BlueCode.Cli/CliArgs.fs
    - src/BlueCode.Cli/CompositionRoot.fs
    - src/BlueCode.Cli/Adapters/FileSessionStore.fs
    - src/BlueCode.Cli/Program.fs
    - tests/BlueCode.Tests/ReplTests.fs
decisions:
  - id: "D-15-02-01"
    what: "AltCommandLine for --new-session"
    why: "Argu converts NewSession DU case to --newsession (no hyphen). Added [<AltCommandLine(\"--new-session\")>] so both --newsession and --new-session work. The plan's smoke tests use --new-session."
    outcome: "Both forms accepted; --help shows both."
  - id: "D-15-02-02"
    what: "Post-parse mutually-exclusive validation instead of Argu attribute"
    why: "Argu F# version in this repo has no clean cross-case exclusive attribute. Post-parse match is explicit, testable via exit code, and the error message is exactly specified (SC4)."
    outcome: "match resumeId, isNewSession with | Some _, true -> eprintfn + exit 2"
  - id: "D-15-02-03"
    what: "Last-envelope-wins Load semantics"
    why: "Save writes full cumulative Steps each turn. Load only needs the final state. No delta concatenation needed; simpler and correct."
    outcome: "Load reads last non-blank line after header, deserializes TurnEnvelope, uses its .steps as Session.Steps."
metrics:
  duration: "~12 min"
  completed: "2026-04-27"
---

# Phase 15 Plan 02: Argu flags + Load + CompositionRoot wiring Summary

**One-liner:** `--resume`/`--new-session` Argu flags wired end-to-end: FileSessionStore.Load replaces 15-01 stub, CompositionRoot gains SessionStore field, Program.fs dispatches to runMultiTurnWithSession with loaded/fresh Session.

## What Was Done

### Task 1: CliArgs + CompositionRoot

**CliArgs.fs** gained two new DU cases:
```fsharp
| Resume of id: string
| [<AltCommandLine("--new-session")>] NewSession
```

Usage strings:
- `Resume`: `"Resume session by ID. Reads ~/.bluecode/sessions/<ID>.jsonl and continues with prior context."`
- `NewSession`: `"Force a fresh session id. Mutually exclusive with --resume."`

**CompositionRoot.fs** changes:
- `AppComponents` gained `SessionStore: BlueCode.Core.Ports.ISessionStore` field
- `CliOptions` gained `ResumeSessionId: BlueCode.Core.Domain.SessionId option` and `NewSession: bool`
- `defaultCliOptions` extended with `ResumeSessionId = None; NewSession = false`
- `bootstrap` now injects `FileSessionStore.FileSessionStore() :> ISessionStore`

**ReplTests.fs** (compile fix): All 5 `AppComponents` record literals updated to add:
```fsharp
SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
```
Test assertions unchanged.

### Task 2: FileSessionStore.Load

Replaced 15-01 stub with real JSONL parsing implementation:

1. `File.Exists` check → `Error (SessionNotFound id)` if missing
2. `File.ReadAllLinesAsync` → `Error (SessionCorrupt "empty session file")` if zero lines
3. Line 0 deserialized as `SessionHeader`, version checked (must be 2), sessionId verified to match requested id
4. Lines 1..N filtered for non-blank → last line deserialized as `TurnEnvelope`
5. Header-only (no envelopes) → `Ok` with empty Steps and `LastActivityAt = header.createdAt`
6. Last envelope's `steps` → `Session.Steps`; `writtenAt` → `Session.LastActivityAt`
7. Outer `try/with` catches all unexpected exceptions → `Error (SessionCorrupt "Load failed: ...")`

### Task 3: Program.fs

Full wiring in order:
1. Parse `results.TryGetResult CliArgs.Resume` and `results.Contains CliArgs.NewSession`
2. Conflict check BEFORE bootstrap: `match resumeId, isNewSession with | Some _, true -> eprintfn "ERROR: conflicting flags..." + exit 2`
3. `CliOptions` populated with `ResumeSessionId` and `NewSession`
4. After bootstrap: session resolution via `opts.ResumeSessionId`
5. Session id printed to stderr: `eprintfn "Session: %s" idStr`
6. Multi-turn: `Repl.runMultiTurnWithSession components renderMode session components.SessionStore`
7. Single-turn: `Repl.runSingleTurn prompt session.Steps components renderMode` + `SessionStore.Save` after

## Exact Error Messages (for 15-03 test assertions)

| Condition | stderr message | exit code |
|-----------|---------------|-----------|
| `--resume X --new-session` | `ERROR: conflicting flags: --resume and --new-session cannot be used together.` | 2 |
| `--resume <missing-id>` | `ERROR: session not found: <id>` | 1 |
| Corrupt JSONL | `ERROR: session corrupt: <detail>` | 1 |
| Generic load failure | `ERROR: session load failed: <AgentError>` | 1 |
| Session id at startup | `Session: <32-char-hex-id>` (stderr) | — |

## Deviation

**Rule 1 (auto-fixed bug): Argu NewSession → --newsession without hyphen**

- **Found during**: Task 3 smoke test
- **Issue**: `NewSession` DU case generates `--newsession` (no hyphen). Plan specified `--new-session` in all examples and smoke tests.
- **Fix**: Added `[<AltCommandLine("--new-session")>]` attribute to `NewSession` case. Both `--newsession` and `--new-session` now accepted.
- **Files modified**: `src/BlueCode.Cli/CliArgs.fs`
- **Commit**:

## Live Smoke Results

```
# Conflicting flags → exit 2
dotnet run -- --resume X --new-session "test" 2>&1; echo "EXIT:$?"
  ERROR: conflicting flags: --resume and --new-session cannot be used together.
  EXIT:2

# Missing session id → exit 1, no stack trace
dotnet run -- --resume nonexistent_test_id_zzz "test" 2>&1; echo "EXIT:$?"
  [INF] blueCode starting: ...
  [INF] Context window floor: ...
  ERROR: session not found: nonexistent_test_id_zzz
  EXIT:1

# --help shows both flags
dotnet run -- --help 2>&1 | grep -E "--resume|--new-session"
  --resume <id>         Resume session by ID...
  --newsession, --new-session  Force a fresh session id...
```

## Test Count

248 passed, 1 ignored, 0 failed — **unchanged from 15-01 baseline**. No new tests added (15-03 adds them).

## Bench Gate

`bash bench/run.sh --gate` → **8/8 PASS**. No LLM/tool dispatch changes; all gate cases pass as before.

## Commits

| Hash | Type | Description |
|------|------|-------------|
| | feat | CliArgs adds --resume/--new-session; AppComponents adds SessionStore |
| | feat | FileSessionStore.Load parses version header + TurnComplete envelopes |
| | feat | Program.fs wires --resume/--new-session + conflict rejection + session id stderr |
| | fix | CliArgs adds AltCommandLine --new-session (Argu naming deviation) |

## Next Phase Readiness

15-03 can proceed. It needs:
- The exact error messages in the table above for `eprintfn` assertions
- The `AppComponents` compile-fix pattern for any new test using `AppComponents` literals (add `SessionStore = FileSessionStore() :> ISessionStore`)
- `runMultiTurnWithSession` is the live multi-turn path; `runMultiTurn` is still defined as legacy delegate
