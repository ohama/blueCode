---
phase: 15-persistence-wiring
plan: "03"
subsystem: testing
tags: [session-store, round-trip, jsonl, multi-turn, bench-gate, expecto]

dependency-graph:
  requires: ["15-01", "15-02"]
  provides: ["SessionStoreTests (5 testCases)", "ReplTests multi-turn SC1 proof", "live smoke SC2/SC3/SC4", "bench gate SC5"]
  affects: ["Phase 16 (test baseline extended to 254)"]

tech-stack:
  added: []
  patterns: ["testSequenced for filesystem-mutating tests", "unique GUID session ids for test isolation", "buildSessionPath reuse in test cleanup"]

key-files:
  created:
    - tests/BlueCode.Tests/SessionStoreTests.fs
  modified:
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs
    - tests/BlueCode.Tests/ReplTests.fs

decisions:
  - id: D-15-03-1
    choice: "Use real ~/.bluecode/sessions/ with unique GUID-based session IDs instead of HOME env var redirect"
    rationale: "Environment.GetFolderPath(SpecialFolder.UserProfile) does NOT read $HOME on macOS — setting $HOME returned empty string for SpecialFolder.UserProfile, which would cause buildSessionPath to write to /.bluecode/sessions/ (permission denied). Using unique GUID ids with finally-block cleanup achieves proper test isolation without env var manipulation."
    alternatives: ["Plan's withTempHome approach (broken on macOS .NET)", "Inject path via constructor (requires FileSessionStore API change out of scope)"]

metrics:
  duration: "11 minutes"
  completed: "2026-04-26"
---

# Phase 15 Plan 03: Tests + Bench Gate Summary

**One-liner:** Round-trip + error-path tests for FileSessionStore (5 testCases), multi-turn cross-turn-history proof (1 testCase), live smoke 2-turn write+resume, bench gate 8/8 PASS. Test count: 248 → 254.

## Tasks Completed

### Task 1: SessionStoreTests.fs (NEW) + fsproj + RouterTests registration

**File:** `tests/BlueCode.Tests/SessionStoreTests.fs`

5 testCases under `testSequenced <| testList "FileSessionStore"`:

1. `round-trip: Save then Load returns equivalent Session.Steps` — builds a 3-step session with deterministic timestamps, Save + Load, asserts Steps deep-equal.
2. `Load on missing id returns SessionNotFound (not exception)` — unique ghost id, no file present, asserts `Error (SessionNotFound id)`.
3. `Load on corrupt JSONL returns SessionCorrupt (not exception)` — plants bad JSON lines at expected path, asserts `Error (SessionCorrupt _)` with non-empty detail.
4. `Load on header-mismatched id returns SessionCorrupt` — Save session A, rename file to B's path (header says A), Load B → `Error (SessionCorrupt _)`.
5. `Save twice in same session writes header once + two TurnComplete envelopes; Load returns latest` — verifies JSONL line structure (1 header + 2 envelopes = 3 lines), verifies `"version":2` header, verifies Load returns the second-turn Steps.

**Registration:**
- `BlueCode.Tests.fsproj`: `<Compile Include="SessionStoreTests.fs" />` inserted between `ContextWarningTests.fs` and `ToolExpansionTests.fs`, before `RouterTests.fs`.
- `RouterTests.fs rootTests`: `BlueCode.Tests.SessionStoreTests.tests` added at end of list. `BlueCode.Tests.ToolExpansionTests.tests` preserved (Phase 14 lesson verified at line 98).

**Commit:** `1fa6763` — `test(15-03): SessionStore round-trip + error-path tests`

### Task 2: ReplTests.fs — multi-turn cross-turn-history test

**File:** `tests/BlueCode.Tests/ReplTests.fs`

New testCase (inserted before the context-warning test):

`multi-turn: turn 2 sees turn 1 Steps as prior context (Phase 15 SC1)`

Strategy: custom `capturingLlm` that records all `messages` passed to each `CompleteAsync` call into a `List<Message list>`. Scripts 3 responses across 2 turns (turn 1: ToolCall list_dir → FinalAnswer; turn 2: FinalAnswer). Calls `runSingleTurn` twice, passing `stepsTurn1` as `priorSteps` on turn 2. Asserts:
- 3 LLM calls total
- Turn 2's messages (3rd batch) contain `"list_dir"` (turn 1's tool name via `buildMessages` assistant content)
- Turn 2's messages contain `"stub-output"` (turn 1's tool result via `[OBSERVATION]\nstub-output`)
- Turn 2's messages contain `"second prompt"` (the new user input)

The test inherits `testSequenced` from the surrounding `testList "Repl"` wrapper, satisfying the filesystem + global state race prevention requirement.

**Commit:** `5ae91fd` — `test(15-03): multi-turn cross-turn-history test (Phase 15 SC1)`

### Task 3: Live smoke + bench gate verification

**Pre-flight:** Both LLM endpoints live and responsive:
- `localhost:8000`: Qwen 32B at `/Users/ohama/llm-system/models/qwen32b` — UP (no restart needed)
- `localhost:8001`: Qwen 72B at `/Users/ohama/llm-system/models/qwen72b` — UP (no restart needed)

**Step A — single-turn write:**
```
dotnet run ... -- "what is 2 plus 2"
Session: 385c0f8aa7a44cd9835d5a0bff6045b7
~/.bluecode/sessions/385c0f8aa7a44cd9835d5a0bff6045b7.jsonl:
  line 1: {"version":2,"sessionId":"385c0f8aa7a44cd9835d5a0bff6045b7","createdAt":"..."}
  line 2: {"type":"TurnComplete","turnIndex":1,...}
PASS
```

**Step B — resume (2nd turn):**
```
dotnet run ... -- --resume 385c0f8aa7a44cd9835d5a0bff6045b7 "what was my previous question"
Session: 385c0f8aa7a44cd9835d5a0bff6045b7  ← same id echoed
Envelope count: 2  ← 2 TurnComplete envelopes after resume
PASS
```

**Step C — error paths:**
```
--resume nonexistent_session_xyz "p"  → exit 1, "ERROR: session not found: nonexistent_session_xyz" (no stack trace)
--resume X --new-session "p"          → exit 2, "ERROR: conflicting flags: --resume and --new-session cannot be used together."
--new-session "what is 1 plus 1"      → exit 0, Session: e53371068e17434a97795486571068cc (different id, file created)
ALL PASS
```

**Step D — bench gate (SC5):**
```
bash bench/run.sh --gate
  PASS T6_32b     steps=3/5 exit=0
  PASS T6_72b     steps=3/5 exit=0
  PASS W1_32b     steps=3/3 exit=0
  PASS W2_32b     steps=3/3 exit=0
  PASS T1_32b     steps=3/3 exit=0
  PASS T5_72b     steps=3/4 exit=0
  PASS B2_32b     steps=2/3 exit=0
  PASS B2_72b     steps=2/3 exit=0
===== GATE PASS (8/8) =====
Exit code: 0
```

## Final Test Count

| Baseline (15-02) | SessionStoreTests (Task 1) | ReplTests multi-turn (Task 2) | Final |
|:---:|:---:|:---:|:---:|
| 248 | +5 | +1 | **254** |

Test run: `254 passed, 1 ignored, 0 failed` — confirmed.

## Regression Checks

- `ToolExpansionTests.tests` in `RouterTests.fs rootTests`: PRESENT at line 98 (Phase 14 preservation lesson satisfied)
- `ToolExpansionTests.fs` in `BlueCode.Tests.fsproj`: PRESENT at line 28
- `bash scripts/check-no-async.sh`: exit 0 — no `async {}` in Core
- `grep -rn "llm-system" src/`: 0 matches — no absolute paths in Core

## Deviations from Plan

### Auto-fixed: withTempHome HOME-redirect approach not viable on macOS .NET

**Found during:** Task 1 implementation

**Issue:** The plan's `withTempHome` fixture redirected `$HOME` via `Environment.SetEnvironmentVariable("HOME", tempPath)`. On macOS .NET, `Environment.GetFolderPath(SpecialFolder.UserProfile)` reads from native OS APIs rather than `$HOME` env var — setting `$HOME` causes `SpecialFolder.UserProfile` to return an empty string, making `buildSessionPath` return `/.bluecode/sessions/<id>.jsonl` (root directory, permission denied).

**Fix:** Used unique GUID-prefixed session IDs (`sst-rt-<guid>`, `sst-corrupt-<guid>`, etc.) directly in `~/.bluecode/sessions/`. Tests call `buildSessionPath` to compute the expected path, then wrap in a `withTempSession` helper that deletes the file in a `finally` block. `testSequenced` wrapper prevents parallel test interference.

**Files modified:** `tests/BlueCode.Tests/SessionStoreTests.fs` (test fixture design only — no source code changes)

**Commit:** Part of `1fa6763`

## Phase 15 Success Criteria Evidence

| SC | Evidence |
|----|----------|
| SC1: turn 2 sees turn 1 Steps | ReplTests `multi-turn: turn 2 sees turn 1 Steps` — asserts `"list_dir"` and `"stub-output"` in turn 2's `CompleteAsync` message list |
| SC2: JSONL written, session id stderr grep-able | SessionStoreTests `Save twice` verifies header+envelope structure; live smoke Step A confirms `Session: <id>` on stderr, file at `~/.bluecode/sessions/<id>.jsonl` with `"version":2` + `TurnComplete` |
| SC3: --resume works; missing id → exit 1 no stack trace | SessionStoreTests `missing id` + `corrupt JSONL` unit tests; live smoke Step B (resume runs) + Step C (missing-id exit 1 + no "Stack trace" in stderr) |
| SC4: --new-session fresh id; conflicting flags exit 2 | Live smoke Step C: `--new-session` mints `e53371068e17434a97795486571068cc` (distinct from session A); `--resume X --new-session` exits 2 with "conflicting flags" |
| SC5: bench/run.sh --gate 8/8 PASS | Live smoke Step D: exit 0, all 8 fixtures PASS |
