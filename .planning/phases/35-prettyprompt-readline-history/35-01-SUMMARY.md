---
phase: 35-prettyprompt-readline-history
plan: 01
status: complete
date: 2026-05-05
subsystem: cli-repl
tags: [prettyprompt, readline, history, port, seam]
requires:
  - 34-edit-multi-line-input
provides:
  - IPromptReader port (PromptReader.fs)
  - makeRealPromptReader (PrettyPrompt 4.1.1 backed)
  - makeTestPromptReader (Queue<string> mock)
  - historyFilePath helper (~/.bluecode/history)
  - promptReaderOverride test seam in Repl.fs
affects:
  - src/BlueCode.Cli/PromptReader.fs (NEW)
  - src/BlueCode.Cli/Repl.fs
  - src/BlueCode.Cli/BlueCode.Cli.fsproj
  - .planning/PROJECT.md
tests:
  added: 0
  modified: 0
  deleted: 0
  state_after_plan: RED-for-26-ReplTests-expected-Plan-35-02-fixes
tech-stack:
  added:
    - PrettyPrompt 4.1.1 (MPL-2.0; net8.0 → net10.0 forward compat)
    - TextCopy 6.2.1 (transitive, auto-resolved)
  patterns:
    - IPromptReader port (mirrors IEditorLauncher from Phase 34-01)
    - promptReaderOverride mutable seam (mirrors editorLauncherOverride)
    - fully-qualified BlueCode.Cli.PromptReader.* in Repl.fs (Phase 33-01 convention)
key-files:
  created:
    - src/BlueCode.Cli/PromptReader.fs
  modified:
    - src/BlueCode.Cli/Repl.fs (promptReaderOverride seam + reader-driven while loop)
    - src/BlueCode.Cli/BlueCode.Cli.fsproj (PrettyPrompt PackageReference + PromptReader.fs Compile)
    - .planning/PROJECT.md (Key Decisions PrettyPrompt row: Pending → Verified)
commits:
  - "52be0e0 feat(35-01): add PrettyPrompt 4.1.1 dep + PromptReader.fs (IPromptReader port + makeRealPromptReader + makeTestPromptReader + historyFilePath)"
  - "14e3b47 feat(35-01): wire Repl input loop to PromptReader (replace Console.ReadLine; add promptReaderOverride seam)"
  - "3eb74b1 docs(35-01): mark PrettyPrompt 4.1.1 NuGet decision as Verified in Key Decisions"
loc_delta:
  added: ~85
  removed: ~5
core_diff: empty
new_nuget: "PrettyPrompt 4.1.1 (+ TextCopy 6.2.1 transitive)"
decisions:
  - id: promptreader-no-open
    summary: "No `open BlueCode.Cli.PromptReader` in Repl.fs — use fully-qualified names per Phase 33-01 convention"
  - id: promptconfiguration-namespace
    summary: "PromptConfiguration is in PrettyPrompt namespace (NOT PrettyPrompt.Configuration); FormattedString is in PrettyPrompt.Highlighting; requires explicit Nullable<FormattedString> construction in F# (no C# implicit conversion)"
  - id: history-500-cap
    summary: "PrettyPrompt's HistoryLog.MaxHistoryEntries is hard-coded 500; ROADMAP SC-5 said 1000 default but that was a placeholder; adopted PrettyPrompt's 500-entry cap"
  - id: no-prettyprompt-env-var
    summary: "No BLUECODE_NO_PRETTYPROMPT env var; promptReaderOverride seam covers test injection needs without untested code paths"
  - id: history-all-inputs
    summary: "History includes ALL inputs (slash commands recallable via Up arrow); /edit content does NOT enter history (arrives via tmpfile, not ReadLineAsync)"
metrics:
  duration: "~22 minutes (13093 seconds wall clock)"
  completed: "2026-05-05"
---

# Phase 35 Plan 01: Port-and-Integration Summary

**One-liner:** PrettyPrompt 4.1.1 wired as `IPromptReader` port into `Repl.fs` input loop; `promptReaderOverride` test seam added; HIST-01..04 production path established.

## What Shipped

- **PromptReader.fs (NEW):** `IPromptReader` interface + `makeRealPromptReader` (PrettyPrompt 4.1.1 backed, `~/.bluecode/history` persistence) + `makeTestPromptReader` (Queue<string> mock) + `historyFilePath` helper.
- **Repl.fs refactor:** `promptReaderOverride : BlueCode.Cli.PromptReader.IPromptReader option` mutable seam added immediately after `editorLauncherOverride`; `printf "\nblueCode> "` and `Console.ReadLine()` replaced with `let! lineOpt = reader.ReadLineAsync()` + `let line = lineOpt |> Option.toObj`; reader instantiated inside `runMultiTurnWithSession` before the `while` loop (not at module level, per Pitfall 2).
- **BlueCode.Cli.fsproj:** `<PackageReference Include="PrettyPrompt" Version="4.1.1" />` added; `PromptReader.fs` Compile entry placed AFTER `EditCommand.fs` BEFORE `Repl.fs`.
- **PROJECT.md Key Decisions:** PrettyPrompt row Outcome column flipped from `— Pending —` to `✓ Verified (Phase 35-01)` with version, .NET 10 compat, MPL-2.0, and TextCopy transitive dep notes.

## NuGet Outcome

PrettyPrompt 4.1.1 (targets `net8.0`; NuGet forward-compat verified for `net10.0` host); MPL-2.0 license; single transitive dep TextCopy 6.2.1 (clipboard support; not directly used by blueCode); built clean against .NET 10. ~85 LOC added (PromptReader.fs ~75 + Repl.fs delta ~10 lines net).

**F# API discovery (deviation from PLAN.md research-recommended code):** The research sample used `open PrettyPrompt.Configuration` and `PromptConfiguration(prompt = "blueCode> ")`. In practice:
- `PromptConfiguration` lives in the `PrettyPrompt` namespace (not `PrettyPrompt.Configuration`), so `open PrettyPrompt.Highlighting` is used for `FormattedString`
- The `prompt` constructor parameter is `Nullable<FormattedString>`, not `string`; F# requires explicit `System.Nullable(FormattedString("blueCode> "))` (no implicit C# string→FormattedString coercion)

This was auto-fixed immediately at first build attempt (Rule 1 deviation) and does not affect the production behavior.

## Key Decisions Captured (locked open questions from 35-RESEARCH.md)

1. **PromptConfiguration.Prompt = `"blueCode> "`** — No leading `"\n"`; PrettyPrompt handles its own line management. Implemented as `FormattedString("blueCode> ")` wrapped in `System.Nullable`.
2. **History includes ALL inputs** — Slash commands are recallable via Up arrow. `/edit` content does NOT enter history (comes from tmpfile via `openEditorAsync`, never through `ReadLineAsync`).
3. **No BLUECODE_NO_PRETTYPROMPT env var** — `promptReaderOverride` seam covers test injection needs without an untested production code path.
4. **PrettyPrompt's 500-entry hard cap accepted** — `HistoryLog.MaxHistoryEntries` is internal to PrettyPrompt; ROADMAP SC-5 "N = 1000 default" was a placeholder estimate. Adopted PrettyPrompt's 500-entry cap.

## Test-Suite RED State — Handed to Plan 35-02

26 existing ReplTests integration tests use `Console.SetIn(StringReader(...))` which PrettyPrompt's `Console.ReadKey(intercept=true)` loop bypasses. After Task 2's changes, PrettyPrompt throws `InvalidOperationException: Cannot read keys when either application does not have a console or when console input has been redirected` in CI/test environments.

Plan 35-02 migrates each test to use `BlueCode.Cli.Repl.promptReaderOverride <- Some (BlueCode.Cli.PromptReader.makeTestPromptReader [...])`. Non-ReplTests (SlashCommandTests, EditCommandTests, AgentLoopTests, RouterTests, etc.) remain GREEN — they don't enter `runMultiTurnWithSession`.

This RED state is **expected and intentional** — the structural change (PrettyPrompt replaces Console.ReadLine) MUST land before tests can be migrated.

## Bench Gate Isolation

`bench/run.sh --gate` is single-turn (`Program.fs | words ->` branch → `runSingleTurn` → never enters `runMultiTurnWithSession`). PrettyPrompt is never instantiated in the bench path. Bench gate run is Plan 35-02's responsibility.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] PrettyPrompt API namespace mismatch**

- **Found during:** Task 1 (first build attempt)
- **Issue:** Research-recommended code used `open PrettyPrompt.Configuration` and `PromptConfiguration(prompt = "blueCode> ")` (string). Actual API: `PromptConfiguration` is in `PrettyPrompt` namespace; `prompt` parameter is `Nullable<FormattedString>`, not `string`.
- **Fix:** Changed `open PrettyPrompt.Configuration` to `open PrettyPrompt.Highlighting`; changed `PromptConfiguration(prompt = "blueCode> ")` to `PromptConfiguration(prompt = System.Nullable(FormattedString("blueCode> ")))`.
- **Files modified:** `src/BlueCode.Cli/PromptReader.fs`
- **Commit:** Included in `52be0e0` (fixed before commit)

## Pitfalls Dodged

- **Eager Prompt construction at module load** — `makeRealPromptReader ()` is called inside `runMultiTurnWithSession` before the `while` loop (not at module level). Single-turn bench path never reaches this code.
- **`open BlueCode.Cli.PromptReader` directive** — Not added to `Repl.fs`; fully-qualified `BlueCode.Cli.PromptReader.*` references per Phase 33-01 convention.
- **`git add -A`** — Never used; individual file staging only (`.claude/` + `localLLM/` + any `~/.bluecode/history` test artifacts stay untracked).
- **Custom history file format** — PrettyPrompt owns `~/.bluecode/history` (base64-per-line); no blueCode code writes to it directly.
- **BLUECODE_NO_PRETTYPROMPT env var** — Seam covers test needs; no env var fallback path added.

## Open Items Handed to Plan 35-02

1. **Migrate 26 ReplTests** off `Console.SetIn` → `BlueCode.Cli.Repl.promptReaderOverride`
2. **Add new HIST-specific tests** — history file write/load, `makeTestPromptReader` queue exhaustion (EOF → None → `running <- false`)
3. **Bench gate 7/7 PASS verification** — SC-7 gate run
4. **Manual SC-8 verification** — macOS Terminal.app + iTerm2 human-verify checkpoint (up/down recall, Ctrl+R search, history persistence across sessions)
