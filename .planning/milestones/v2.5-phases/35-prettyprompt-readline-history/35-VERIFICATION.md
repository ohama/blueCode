---
phase: 35-prettyprompt-readline-history
verified: 2026-05-05T22:52:00Z
status: human_needed
score: 9/9 must-haves verified (automated); 4 items require live TTY testing
re_verification: false
human_verification:
  - id: HV-1
    sc: SC-3
    test: "Open Terminal.app, run blueCode (multi-turn), type two prompts, press Up arrow"
    expected: "Up arrow navigates to previous prompt; Down arrow cycles forward; all in-session prompts are recallable"
    why_human: "PrettyPrompt's ReadKey loop requires a real TTY; cannot exercise Up/Down in non-TTY test environment"
  - id: HV-2
    sc: SC-6
    test: "In the REPL, press Ctrl+R, type a substring of a prior prompt"
    expected: "PrettyPrompt reverse-search matches and highlights the prior prompt; Enter selects it"
    why_human: "Ctrl+R interactive search requires a real TTY and keyboard input; not exercisable through test seam"
  - id: HV-3
    sc: SC-8
    test: "Re-run HV-1 and HV-2 in both Terminal.app AND iTerm2"
    expected: "Identical behavior in both terminal emulators — up/down recall, Ctrl+R search, line editing keys (Home/End/Ctrl-W) work correctly"
    why_human: "Terminal compatibility (ANSI escape handling, alternate screen) differs by emulator; requires real TTY in each"
  - id: HV-4
    sc: HIST-03
    test: "Run blueCode, type 2+ prompts, /exit; then run: cat ~/.bluecode/history; re-launch blueCode, press Up arrow"
    expected: "~/.bluecode/history exists with base64-per-line entries; re-launched REPL's Up arrow recalls prior-session prompts"
    why_human: "PrettyPrompt's SavePersistentHistoryAsync and cross-session load require a real ReadLineAsync submit in a TTY; automated smoke test only covers constructor (not actual write/load)"
---

# Phase 35: PrettyPrompt Readline + History Verification Report

**Phase Goal:** Replace `Console.ReadLine` with PrettyPrompt library. Up/Down arrow recall + cross-session history persistence + Ctrl+R search + line editing.
**Verified:** 2026-05-05T22:52:00Z
**Status:** HUMAN VERIFICATION NEEDED
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User prompt input is read via PrettyPrompt (NOT Console.ReadLine); cursor editing keys work | VERIFIED | `Repl.fs:311` `let! lineOpt = reader.ReadLineAsync()` — `Console.ReadLine` absent from all Cli source files |
| 2 | Up/Down arrow navigates in-session prompt history | HUMAN NEEDED | PrettyPrompt built-in; wired at `PromptReader.fs:54` `new Prompt(persistentHistoryFilepath = path, ...)` — requires real TTY for functional verification (HV-1) |
| 3 | Ctrl+R opens reverse-search through history | HUMAN NEEDED | PrettyPrompt built-in; same wiring as T2 — requires real TTY (HV-2) |
| 4 | On REPL start, prior prompts at `~/.bluecode/history` are loaded | VERIFIED (struct) | `PromptReader.fs:49-54`: `historyFilePath()` → `Path.Combine(home, ".bluecode", "history")`; passed as `persistentHistoryFilepath` to PrettyPrompt constructor which loads history async on init; full cross-session test is HV-4 |
| 5 | Each prompt submit appends to `~/.bluecode/history` | VERIFIED (struct) | PrettyPrompt's internal `SavePersistentHistoryAsync` fires on every `ReadLineAsync` success (`result.IsSuccess`); `PromptReader.fs:59-62`; functional file-write is HV-4 |
| 6 | Slash commands parse identically post-PrettyPrompt | VERIFIED | `Repl.fs:317` `SlashCommand.parse line` — parser receives string from `reader.ReadLineAsync()` exactly as from old `Console.ReadLine()`; 19 migrated ReplTests integration tests + 17 SlashCommandParser unit tests all GREEN (365/365) |
| 7 | Single-turn mode (bench) does NOT instantiate PrettyPrompt | VERIFIED | `reader` instantiated inside `runMultiTurnWithSession` only (`Repl.fs:305-308`); bench path uses `runSingleTurn` — confirmed by byte-identical `bench/baseline.json` (MD5: `a3b2ba7c7d3da207a4cce51f67509461`) |
| 8 | `promptReaderOverride` test seam defaults None; tests use it to inject scripted prompts | VERIFIED | `Repl.fs:48` `let mutable promptReaderOverride : BlueCode.Cli.PromptReader.IPromptReader option = None`; 19 ReplTests migrated to seam injection; 4 real `Console.SetIn` calls remain for hybrid plan-gate tests (Accept/Quit keypresses) — matches PLAN.md explicit authorization |
| 9 | PROJECT.md Key Decisions row updated to Verified | VERIFIED | `PROJECT.md:327` — Outcome column flipped from `— Pending —` to `✓ Verified (Phase 35-01) — PrettyPrompt 4.1.1 added ...`; old Pending text absent |

**Score:** 9/9 truths structurally verified; 4 require human TTY confirmation (HV-1..HV-4)

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/BlueCode.Cli/PromptReader.fs` | IPromptReader port + makeRealPromptReader + makeTestPromptReader + historyFilePath; min 40 lines | VERIFIED | Exists; 76 lines; exports all 4 symbols; no stubs; `persistentHistoryFilepath` wired at line 54; `.bluecode` path at line 29 |
| `src/BlueCode.Cli/BlueCode.Cli.fsproj` | PrettyPrompt 4.1.1 PackageReference + PromptReader.fs Compile between EditCommand.fs and Repl.fs | VERIFIED | `PackageReference Include="PrettyPrompt" Version="4.1.1"` at line 39; Compile order: EditCommand.fs(21) < PromptReader.fs(22) < Repl.fs(23) |
| `src/BlueCode.Cli/Repl.fs` | promptReaderOverride seam + reader-driven input loop; Console.ReadLine and printf blueCode> absent | VERIFIED | Seam at line 48; reader instantiation at lines 305-308; `reader.ReadLineAsync()` at line 311; zero `Console.ReadLine` matches in file; zero `printf.*blueCode>` matches |
| `.planning/PROJECT.md` | PrettyPrompt Key Decisions row with ✓ Verified (Phase 35-01) outcome | VERIFIED | Line 327 confirmed; `— Pending —` text absent |
| `tests/BlueCode.Tests/PromptReaderTests.fs` | 6 unit tests for IPromptReader port contract; HIST-03 smoke | VERIFIED | Exists; 107 lines; 6 testCases covering: makeTestPromptReader FIFO (3), historyFilePath shape (1), PrettyPrompt constructor smoke (1), makeRealPromptReader factory (1) |
| `tests/BlueCode.Tests/ReplTests.fs` | 19 tests migrated from Console.SetIn to promptReaderOverride; 4 real Console.SetIn calls remain (hybrid) | VERIFIED | 4 actual Console.SetIn code lines (1162, 1235, 1247, 1308); comment at line 122 explains migration; hybrid Accept/Quit tests use both seams as authorized |
| `tests/BlueCode.Tests/BlueCode.Tests.fsproj` | PromptReaderTests.fs Compile between EditCommandTests.fs and RouterTests.fs | VERIFIED | Line 33 confirms placement; EditCommandTests(32) < PromptReaderTests(33) < RouterTests(34) |
| `tests/BlueCode.Tests/RouterTests.fs` | BlueCode.Tests.PromptReaderTests.tests in rootTests list | VERIFIED | Line 117: `BlueCode.Tests.PromptReaderTests.tests` confirmed |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| `Repl.fs runMultiTurnWithSession while loop` | `PromptReader.fs IPromptReader.ReadLineAsync` | `let! lineOpt = reader.ReadLineAsync()` | WIRED | `Repl.fs:311` — `let!` inside `task {}` body; result flows to `Option.toObj` then `match line with \| null ->` |
| `PromptReader.fs makeRealPromptReader` | `PrettyPrompt.Prompt` constructor | `new Prompt(persistentHistoryFilepath = path, configuration = config)` | WIRED | `PromptReader.fs:54` — `persistentHistoryFilepath` bound to `historyFilePath()` return value |
| `PromptReader.fs historyFilePath` | `~/.bluecode/history` | `Path.Combine(home, ".bluecode", "history")` + `Directory.CreateDirectory` | WIRED | `PromptReader.fs:28-31` — idempotent dir creation; returns full path |
| `Repl.fs slash command dispatch` | `SlashCommand.parse` | `SlashCommand.parse line` where `line` comes from `reader.ReadLineAsync()` | WIRED | `Repl.fs:317` — parser receives string option unwrapped via `Option.toObj`; unchanged from prior behavior |
| `Repl.fs promptReaderOverride` | `PromptReader.fs IPromptReader` (type annotation) | `let mutable promptReaderOverride : BlueCode.Cli.PromptReader.IPromptReader option = None` | WIRED | `Repl.fs:48` — fully-qualified type; matches editorLauncherOverride pattern at line 39 |
| `ReplTests.fs (19 tests)` | `Repl.fs promptReaderOverride` | `BlueCode.Cli.Repl.promptReaderOverride <- Some (makeTestPromptReader [...])` | WIRED | Confirmed by 365/365 test pass (all 19 migrated integration tests run through promptReaderOverride path) |

### Core Purity Invariant Check

| Check | Command | Result |
|-------|---------|--------|
| Core diff empty | `git diff master -- src/BlueCode.Core/` | EMPTY — no Core changes |
| No async{} literal | `bash scripts/check-no-async.sh` | EXIT 0 — "OK: no async {} expressions in src/BlueCode.Core" |
| PromptReader.fs not in Core | grep PrettyPrompt src/BlueCode.Core/ | N/A — file not in Core |

### Requirements Coverage

| Requirement | Description | Status | Evidence |
|-------------|-------------|--------|---------|
| HIST-01 | Up/Down arrow recall in current REPL session | STRUCT + HV-1 | PrettyPrompt built-in wired; `PromptReader.fs:54`; HV-1 for functional confirmation |
| HIST-02 | Ctrl+R reverse-search through history | STRUCT + HV-2 | PrettyPrompt built-in wired; same entry point; HV-2 for functional confirmation |
| HIST-03 | Cross-session history persistence (`~/.bluecode/history`) | STRUCT + HV-4 | `historyFilePath()` + `persistentHistoryFilepath` constructor param; PromptReaderTests HIST-03 smoke; HV-4 for file-write/load functional confirmation |
| HIST-04 | Standard line editing keys (Home/End/Ctrl-W/etc) | STRUCT + HV-3 | PrettyPrompt built-in line editing; verified via Terminal.app + iTerm2 sweep (HV-3) |

### Anti-Patterns Found

No anti-patterns detected in any modified files. Scanned: `PromptReader.fs`, `Repl.fs`, `PromptReaderTests.fs`, `ReplTests.fs` for TODO/FIXME/XXX/HACK/placeholder/return null patterns. All clear.

### Roadmap Success Criteria Mapping

| SC | Description | Status | Evidence |
|----|-------------|--------|---------|
| SC-1 | PrettyPrompt PackageReference (4.1.1) + Key Decision verified in PROJECT.md | SATISFIED | `BlueCode.Cli.fsproj:39` PackageReference; `PROJECT.md:327` ✓ Verified row |
| SC-2 | Repl.fs Console.ReadLine replaced by PrettyPrompt reader; slash commands unaffected | SATISFIED | `Repl.fs:311` reader.ReadLineAsync; zero Console.ReadLine in file; SlashCommand.parse at :317 |
| SC-3 | Up/Down arrow recall (current REPL session) | HUMAN NEEDED (HV-1) | PrettyPrompt built-in wired at PromptReader.fs:54; requires real TTY |
| SC-4 | `~/.bluecode/history` append on each prompt submit | SATISFIED (struct) | PrettyPrompt SavePersistentHistoryAsync internal; historyFilePath() returns correct path; HV-4 for functional file-write |
| SC-5 | REPL loads history on start; cap | SATISFIED (500-cap trade-off) | PrettyPrompt loads `persistentHistoryFilepath` on constructor; internal cap is 500 (not ROADMAP placeholder 1000); documented in 35-01 SUMMARY §Key Decisions #4 |
| SC-6 | Ctrl+R reverse-search | HUMAN NEEDED (HV-2) | PrettyPrompt built-in wired; requires real TTY |
| SC-7 | Bench gate 7/7 PASS preserved | SATISFIED | `bench/baseline.json` byte-identical to master (MD5: a3b2ba7c7d3da207a4cce51f67509461); SUMMARY empirically confirmed bench gate PASS |
| SC-8 | macOS Terminal.app + iTerm2 manual verification | HUMAN NEEDED (HV-3) | Requires live TTY sweep in both emulators |
| SC-9 | SlashCommand parser tests pass post-PrettyPrompt | SATISFIED | 365/365 tests pass including 17 SlashCommandParser tests + 19 migrated ReplTests integration tests |

### Test Count Delta

| Metric | Before Phase 35 | After Phase 35 | Delta |
|--------|-----------------|----------------|-------|
| Total tests | 359 | 365 | +6 |
| PromptReaderTests | 0 | 6 | +6 (NEW: PromptReaderTests.fs) |
| ReplTests (migrated) | 19 used Console.SetIn | 19 use promptReaderOverride | 0 (count unchanged; seam swapped) |
| Test suite status | GREEN | GREEN (365 passed, 1 ignored, 0 failed) | All green |

### Human Verification Required

**HV-1 — SC-3: Up/Down Arrow History Recall (real TTY)**

Test: Open Terminal.app, run `blueCode`, type two distinct prompts (e.g., "list files" then "explain this code"), then press Up arrow.
Expected: Up arrow cycles back to "explain this code", then to "list files". Down arrow cycles forward. All prompts typed in the session are recallable.
Why human: PrettyPrompt's `Console.ReadKey(intercept=true)` loop requires a real TTY. The test seam (`makeTestPromptReader`) bypasses PrettyPrompt entirely; it cannot exercise the keyboard navigation path.

**HV-2 — SC-6: Ctrl+R Reverse-Search (real TTY)**

Test: In the same REPL session, press Ctrl+R. Type a few characters from a prior prompt.
Expected: PrettyPrompt displays a reverse-search interface; matching prior prompt is highlighted; Enter accepts and submits it.
Why human: Same TTY requirement as HV-1. Ctrl+R is handled inside PrettyPrompt's internal keyboard loop, not reachable via seam injection.

**HV-3 — SC-8: Terminal.app + iTerm2 Compatibility Sweep**

Test: Repeat HV-1 and HV-2 in both Terminal.app AND iTerm2.
Expected: Identical behavior in both — up/down recall works, Ctrl+R works, line editing keys (Home, End, Ctrl-W, Ctrl-A, Ctrl-E) work, prompt prefix "blueCode> " renders without ANSI corruption.
Why human: ANSI escape sequence handling differs between terminal emulators; PrettyPrompt's SystemConsole implementation must be verified against both.

**HV-4 — HIST-03: Cross-Session History Persistence**

Test: Run blueCode, type 2+ prompts, type `/exit`. Then: `cat ~/.bluecode/history` (should show base64-per-line entries). Re-launch `blueCode` and press Up arrow.
Expected: `~/.bluecode/history` file exists and contains base64-encoded prior prompts. On re-launch, Up arrow recalls the prompts from the previous session.
Why human: The HIST-03 smoke test in PromptReaderTests.fs only verifies PrettyPrompt constructor accepts the path without throwing. Actual file writes happen inside PrettyPrompt's `SavePersistentHistoryAsync` on `ReadLineAsync` submit — a path only reachable in a real TTY session. The automated test deliberately does NOT call `ReadLineAsync` to avoid hanging.

### Note: History Cap Trade-Off

SC-5 specifies "last N prompts" on REPL start. ROADMAP placeholder said "N = 1000 default." PrettyPrompt's `HistoryLog.MaxHistoryEntries` is an internal constant hard-coded to 500. This trade-off was identified during research (35-RESEARCH.md) and documented in 35-01-SUMMARY.md §Key Decisions #4. The cap is 500, not 1000. This is a known and accepted deviation from the ROADMAP placeholder.

### Gaps Summary

No automated gaps. All 9 must-have truths are structurally verified in the codebase. The 4 human verification items (HV-1..HV-4) are expected per PLAN.md design — PrettyPrompt's interactive TTY behaviors are by definition untestable in non-TTY test environments. This is the final v2.5 phase; HV-1..HV-4 sign-off unblocks `/gsd:complete-milestone`.

---

_Verified: 2026-05-05T22:52:00Z_
_Verifier: Claude (gsd-verifier)_
