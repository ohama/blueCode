---
phase: 35-prettyprompt-readline-history
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/BlueCode.Cli/PromptReader.fs              # NEW: IPromptReader port + makeRealPromptReader (PrettyPrompt) + makeTestPromptReader + historyFilePath helper
  - src/BlueCode.Cli/BlueCode.Cli.fsproj          # add <PackageReference Include="PrettyPrompt" Version="4.1.1" /> + <Compile Include="PromptReader.fs" /> BEFORE Repl.fs
  - src/BlueCode.Cli/Repl.fs                      # add `mutable promptReaderOverride` seam + replace `printf "\nblueCode> "` + `Console.ReadLine()` with `reader.ReadLineAsync()`
  - .planning/PROJECT.md                          # Key Decisions table: flip "PrettyPrompt 4.1.1 (HIST-01..04)" row to "Verified" with version + .NET 10 compat outcome notes
autonomous: true

must_haves:
  truths:
    - "User typing a prompt at the REPL `blueCode> ` prefix is read via PrettyPrompt 4.1.1 (NOT `Console.ReadLine`), so the line buffer supports cursor editing keys."
    - "Up/Down arrow keys navigate the in-session history of prompts within the current `runMultiTurnWithSession` invocation (PrettyPrompt built-in)."
    - "Ctrl+R opens reverse-search through the history file (PrettyPrompt built-in)."
    - "On REPL start, prior prompts persisted at `~/.bluecode/history` are loaded into PrettyPrompt's history (PrettyPrompt's `persistentHistoryFilepath` constructor parameter)."
    - "On each prompt submit, the typed line is appended to `~/.bluecode/history` (PrettyPrompt internal `SavePersistentHistoryAsync`; base64-per-line format)."
    - "Slash commands (`/help`, `/status`, `/clear`, `/sessions`, `/resume`, `/plan`, `/edit`, `/exit`, `/quit`) parse identically post-PrettyPrompt — `SlashCommand.parse` operates on the string returned by the reader; PrettyPrompt is upstream of the parser."
    - "Single-turn mode (`bench/run.sh`) does NOT instantiate PrettyPrompt: `runSingleTurn` is reached via `Program.fs | words ->` branch which never enters `runMultiTurnWithSession`. Bench gate baseline.json byte-equal preserved."
    - "Test seam `BlueCode.Cli.Repl.promptReaderOverride : IPromptReader option` defaults to None; production never sets it; tests will set it in Plan 35-02 to inject scripted prompts without spawning a TTY-bound PrettyPrompt loop."
    - "PROJECT.md Key Decisions table row for PrettyPrompt is updated from `— Pending —` to a verified outcome line confirming version (4.1.1), .NET 10 forward-compat note, and that the dependency is added."
  artifacts:
    - path: "src/BlueCode.Cli/PromptReader.fs"
      provides: "IPromptReader port + makeRealPromptReader (PrettyPrompt-backed) + makeTestPromptReader (Queue<string>) + historyFilePath helper (`~/.bluecode/history`)"
      exports: ["IPromptReader", "makeRealPromptReader", "makeTestPromptReader", "historyFilePath"]
      min_lines: 40
    - path: "src/BlueCode.Cli/BlueCode.Cli.fsproj"
      provides: "PrettyPrompt 4.1.1 PackageReference + PromptReader.fs Compile entry placed AFTER EditCommand.fs and BEFORE Repl.fs"
      contains: "PrettyPrompt"
    - path: "src/BlueCode.Cli/Repl.fs"
      provides: "promptReaderOverride mutable seam + reader-based input loop in runMultiTurnWithSession; printf `blueCode> ` and Console.ReadLine() removed from inside the while loop"
      contains: "promptReaderOverride"
    - path: ".planning/PROJECT.md"
      provides: "Updated Key Decisions row for PrettyPrompt: verified-outcome notes (version 4.1.1, .NET 10 compat OK, MPL-2.0)"
      pattern: "PrettyPrompt"
  key_links:
    - from: "src/BlueCode.Cli/Repl.fs runMultiTurnWithSession while loop"
      to: "src/BlueCode.Cli/PromptReader.fs IPromptReader.ReadLineAsync"
      via: "let! lineOpt = reader.ReadLineAsync()"
      pattern: "reader\\.ReadLineAsync"
    - from: "src/BlueCode.Cli/PromptReader.fs makeRealPromptReader"
      to: "PrettyPrompt.Prompt constructor"
      via: "new Prompt(persistentHistoryFilepath = path, configuration = PromptConfiguration(prompt = \"blueCode> \"))"
      pattern: "persistentHistoryFilepath"
    - from: "src/BlueCode.Cli/PromptReader.fs historyFilePath"
      to: "~/.bluecode/history"
      via: "Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), \".bluecode\", \"history\") with Directory.CreateDirectory(.bluecode)"
      pattern: "\\.bluecode"
    - from: "src/BlueCode.Cli/Repl.fs Slash Edit arm + handlePromptTurn"
      to: "src/BlueCode.Cli/SlashCommand.fs parse"
      via: "PrettyPrompt-returned string flows into SlashCommand.parse exactly as Console.ReadLine() previously did — parser is downstream"
      pattern: "SlashCommand\\.parse"
    - from: "src/BlueCode.Cli/Repl.fs"
      to: "src/BlueCode.Cli/PromptReader.fs"
      via: "Module-level `let mutable promptReaderOverride : BlueCode.Cli.PromptReader.IPromptReader option = None` (mirrors editorLauncherOverride at line 39)"
      pattern: "promptReaderOverride"
---

<objective>
Implement the structural change for PrettyPrompt readline + history (HIST-01..04 production wiring): introduce the `IPromptReader` port (mirrors `IEditorLauncher` from Phase 34), add the PrettyPrompt 4.1.1 NuGet dependency, wire its production implementation that reads via PrettyPrompt's persistent-history-backed `ReadLineAsync`, refactor `Repl.fs` to thread input through the new reader (replacing `printf "\nblueCode> "` + `Console.ReadLine()`), add a test-only `mutable promptReaderOverride` seam (mirrors `editorLauncherOverride`) so Plan 35-02 can migrate the 26 existing `Console.SetIn`-based ReplTests, and update `PROJECT.md` Key Decisions row to record the verified outcome for the new NuGet dependency.

Purpose: REPL daily-driver UX requires up/down history recall, Ctrl+R reverse search, persistent history across invocations, and standard line-editing keys (Home/End/Ctrl-W/etc). `Console.ReadLine` provides none of this. Hand-rolling readline (~300-400 LOC of fragile ANSI escape parsing across Terminal.app / iTerm2 / SSH variants) is exactly the kind of work the v2.5 Key Decision (PROJECT.md line 327) opted to avoid by adopting PrettyPrompt. This plan establishes the production wiring; Plan 35-02 then migrates the 26 existing ReplTests off `Console.SetIn` (which PrettyPrompt's `Console.ReadKey(intercept=true)` loop bypasses entirely), adds 2-3 new history-specific tests, and runs the bench gate.

Output: New `PromptReader.fs` module, updated `Repl.fs` (seam + reader-driven input loop), updated `.fsproj` (PrettyPrompt 4.1.1 PackageReference + Compile entry), updated `PROJECT.md` Key Decisions row. NO test changes in this plan — tests are Plan 35-02's scope. The build MUST be green; the test suite WILL be red after this plan (26 ReplTests now hang on PrettyPrompt's TTY-bound ReadKey loop) — Plan 35-02 fixes that. This is acceptable because Wave 1 → Wave 2 is sequential, NOT parallel.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/REQUIREMENTS.md
@.planning/phases/35-prettyprompt-readline-history/35-RESEARCH.md

# Source files this plan modifies or directly depends on:
@src/BlueCode.Cli/Repl.fs
@src/BlueCode.Cli/EditCommand.fs
@src/BlueCode.Cli/PlanGate.fs
@src/BlueCode.Cli/SlashCommand.fs
@src/BlueCode.Cli/BlueCode.Cli.fsproj
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add PrettyPrompt 4.1.1 NuGet PackageReference and create PromptReader.fs module (IPromptReader port + makeRealPromptReader + makeTestPromptReader + historyFilePath)</name>
  <files>src/BlueCode.Cli/PromptReader.fs (NEW), src/BlueCode.Cli/BlueCode.Cli.fsproj</files>
  <action>
**Step A — Add PrettyPrompt 4.1.1 PackageReference to `src/BlueCode.Cli/BlueCode.Cli.fsproj`.**

Add INSIDE the existing `<ItemGroup>` block that contains other `<PackageReference>` entries (currently lines 31-38; after `Spectre.Console`):

```xml
<PackageReference Include="PrettyPrompt" Version="4.1.1" />
```

The block becomes:
```xml
<ItemGroup>
  <PackageReference Include="Argu" Version="6.2.5" />
  <PackageReference Include="FSharp.SystemTextJson" Version="1.4.36" />
  <PackageReference Include="JsonSchema.Net" Version="9.2.0" />
  <PackageReference Include="Serilog" Version="4.3.1" />
  <PackageReference Include="Serilog.Sinks.Console" Version="6.1.1" />
  <PackageReference Include="Spectre.Console" Version="0.55.2" />
  <PackageReference Include="PrettyPrompt" Version="4.1.1" />     <!-- NEW (Phase 35; HIST-01..04) -->
</ItemGroup>
```

Run `dotnet restore src/BlueCode.Cli/BlueCode.Cli.fsproj` immediately to pull the package + transitive `TextCopy` dep before writing PromptReader.fs (otherwise the F# compiler can't resolve `PrettyPrompt.Prompt`).

**Why version 4.1.1 specifically:** Verified by 35-RESEARCH.md from PrettyPrompt's csproj + NuGet page. Library targets `net8.0`; NuGet forward-compat permits net8.0 packages on net10.0. License MPL-2.0 (compatible with this project's licensing). Transitive dep `TextCopy` is auto-pulled (clipboard support; not directly used by blueCode but required by PrettyPrompt's internals).

**Step B — Create `src/BlueCode.Cli/PromptReader.fs`** (NEW file) with the verbatim content below. This is the recommended Option A from 35-RESEARCH.md § Code Examples (lines 327-375), with locked open-question resolutions baked in:

```fsharp
module BlueCode.Cli.PromptReader

open System
open System.IO
open System.Threading.Tasks
open PrettyPrompt
open PrettyPrompt.Configuration   // PromptConfiguration lives in this sub-namespace

/// Abstraction over interactive line reading. Production = PrettyPrompt with
/// persistent history; tests = pre-canned string queue. Mirrors IEditorLauncher
/// from EditCommand.fs (Phase 34-01).
///
/// Returns Task<string option>:
///   Some text -> user submitted a line (text may be empty if user pressed Enter on blank input)
///   None      -> Ctrl+C / Ctrl+D / EOF (caller maps to REPL exit)
///
/// NOTE: The prompt prefix string ("blueCode> ") is baked into construction
/// (PromptConfiguration.Prompt), not passed per-call. PrettyPrompt renders the
/// prefix automatically; do NOT also `printf` the prefix from Repl.fs.
type IPromptReader =
    abstract member ReadLineAsync : unit -> Task<string option>

/// Returns the persistent history file path: ~/.bluecode/history
/// Creates ~/.bluecode/ dir if absent (idempotent — Directory.CreateDirectory
/// no-op if exists; FileSessionStore already creates this directory on first
/// session save).
let historyFilePath () : string =
    let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    let dir  = Path.Combine(home, ".bluecode")
    Directory.CreateDirectory(dir) |> ignore
    Path.Combine(dir, "history")

/// Production reader: PrettyPrompt 4.1.1 with persistent history.
/// Instantiate ONCE per REPL session (inside runMultiTurnWithSession; before
/// the while loop). PrettyPrompt's constructor kicks off an async file-read
/// of `historyPath` — calling it eagerly at module init or in CompositionRoot
/// would issue a disk read before the user has even seen the banner.
///
/// Open question resolutions (35-RESEARCH.md § Open Questions; locked here):
///   #1 PromptConfiguration.Prompt = "blueCode> " (no leading "\n"; PrettyPrompt
///      handles its own line management).
///   #2 History includes ALL inputs (slash commands recallable via Up arrow).
///   #3 No BLUECODE_NO_PRETTYPROMPT env var (seam covers test injection needs).
///   #4 PrettyPrompt has a built-in 500-entry hard cap inside HistoryLog;
///      ROADMAP SC-5 said "N = 1000 default" but PrettyPrompt's internal
///      HistoryLog.MaxHistoryEntries is hard-coded; cap remains 500. SUMMARY
///      will document the trade-off (Plan 35-02).
let makeRealPromptReader () : IPromptReader =
    let path = historyFilePath ()
    let config = PromptConfiguration(prompt = "blueCode> ")
    let pp = new Prompt(persistentHistoryFilepath = path, configuration = config)
    { new IPromptReader with
        member _.ReadLineAsync() =
            task {
                let! result = pp.ReadLineAsync()   // Task<PromptResult>
                if result.IsSuccess then
                    return Some result.Text        // submitted line (may be empty string)
                else
                    return None                    // Ctrl+C, Ctrl+D, or any non-success
            } }

/// Test reader: dequeue from pre-canned list; None on exhaustion.
/// Used by Plan 35-02 ReplTests to inject scripted prompts WITHOUT spawning
/// PrettyPrompt's TTY-bound ReadKey loop (which would hang in a non-TTY
/// test environment).
let makeTestPromptReader (lines: string list) : IPromptReader =
    let q = Collections.Generic.Queue<string>(lines)
    { new IPromptReader with
        member _.ReadLineAsync() =
            task {
                if q.Count > 0 then return Some (q.Dequeue())
                else return None
            } }
```

**Step C — Update `src/BlueCode.Cli/BlueCode.Cli.fsproj`** to add `PromptReader.fs` to the `<Compile Include="...">` ItemGroup. Insert AFTER `EditCommand.fs` and BEFORE `Repl.fs`:

```xml
<Compile Include="PlanGate.fs" />
<Compile Include="EditCommand.fs" />
<Compile Include="PromptReader.fs" />          <!-- NEW: must precede Repl.fs (Phase 35) -->
<Compile Include="Repl.fs" />
```

**Why this exact order:** F# compile order is significant; `Repl.fs` will reference `BlueCode.Cli.PromptReader.IPromptReader` in its module-level `let mutable promptReaderOverride` declaration (Task 2), so `PromptReader.fs` must compile first. Adjacent placement to `EditCommand.fs` keeps all v2.5 port modules together.

**Build immediately to catch resolution + compile errors:**
```bash
dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj
```

Expected: Build succeeds with no warnings about `PrettyPrompt`. If you see `error FS0039: The namespace or module 'PrettyPrompt' is not defined`, the `dotnet restore` from Step A did not complete; re-run it.

**What NOT to do:**
- Do NOT touch `src/BlueCode.Core/**` — Core purity invariant (CLAUDE.md "Core purity (absolute)"). PromptReader.fs is Cli-only; uses `System.IO`, `System.Threading.Tasks`, `PrettyPrompt` — all forbidden in Core.
- Do NOT add `PrettyPrompt` to `BlueCode.Core.fsproj` — Core has no NuGet deps beyond F# core; this would break the Core purity invariant immediately.
- Do NOT add `[<EntryPoint>]` to PromptReader.fs — it is a library module, not an executable.
- Do NOT use `async {}` — use `task {}` (CLAUDE.md "Core purity"; Cli convention is `task {}` for consistency with QwenHttpClient/FsToolExecutor/EditCommand).
- Do NOT pre-create the `~/.bluecode/history` file — PrettyPrompt's `File.AppendAllLinesAsync` creates it on first write (35-RESEARCH.md § Pitfall 5).
- Do NOT write to `~/.bluecode/history` from blueCode code — PrettyPrompt OWNS the file (base64 format, dedup, 500-cap). Writing separately would corrupt the format (35-RESEARCH.md § "Don't Hand-Roll" + Pitfall 7).
- Do NOT instantiate the `Prompt` constructor at module load (e.g., a top-level `let pp = new Prompt(...)` outside `makeRealPromptReader`) — eager init issues a disk read before the user sees the banner (35-RESEARCH.md § Pitfall 2).
- Do NOT add `open BlueCode.Cli.PromptReader` to `Repl.fs` — Phase 33-01 + Phase 34-01 established fully-qualified `BlueCode.Cli.{Module}.*` references in Repl.fs as the convention (STATE.md "Fully-qualified BlueCode.Cli.* in Repl.fs"); Task 2 follows this.
- Do NOT implement `IPromptCallbacks` for autocomplete or syntax highlighting — Phase 35 scope is readline + history only (35-RESEARCH.md § Anti-Patterns; PROJECT.md "Out of Scope: Auto-completion of slash commands").

**Atomic commit (Task 1):**
```bash
git add src/BlueCode.Cli/PromptReader.fs src/BlueCode.Cli/BlueCode.Cli.fsproj
git commit -m "feat(35-01): add PrettyPrompt 4.1.1 dep + PromptReader.fs (IPromptReader port + makeRealPromptReader + makeTestPromptReader + historyFilePath)"
```

NEVER `git add -A` or `git add .` — `.claude/`, `localLLM/`, and any `~/.bluecode/history` test artifacts are intentionally untracked; `-A` would sweep them in (CLAUDE.md "Commit protocol").
  </action>
  <verify>
1. `dotnet restore src/BlueCode.Cli/BlueCode.Cli.fsproj 2>&1 | grep -i "PrettyPrompt\|TextCopy"` shows the package + transitive `TextCopy` dep installed.
2. `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0 with no warnings about `PrettyPrompt` or `PromptReader.fs`.
3. `grep -n 'PrettyPrompt' src/BlueCode.Cli/BlueCode.Cli.fsproj` returns 1 line: the `<PackageReference Include="PrettyPrompt" Version="4.1.1" />` entry.
4. `grep -n 'PromptReader.fs' src/BlueCode.Cli/BlueCode.Cli.fsproj` shows the entry sandwiched between `EditCommand.fs` and `Repl.fs` (verify by line numbers).
5. `grep -n 'IPromptReader\|makeRealPromptReader\|makeTestPromptReader\|historyFilePath' src/BlueCode.Cli/PromptReader.fs` returns at least 4 matches (one per public symbol).
6. `grep -n 'persistentHistoryFilepath' src/BlueCode.Cli/PromptReader.fs` returns 1 match (the constructor argument — proves PrettyPrompt is wired with history file path).
7. `grep -n '\.bluecode' src/BlueCode.Cli/PromptReader.fs` returns 1 match (the `~/.bluecode/` dir path inside `historyFilePath`).
8. `git diff master -- src/BlueCode.Core/` is empty (Core purity preserved).
9. `bash scripts/check-no-async.sh` exits 0 (no `async {}` literal added).
  </verify>
  <done>
- `src/BlueCode.Cli/PromptReader.fs` exists with `IPromptReader` interface, `makeRealPromptReader` (PrettyPrompt-backed), `makeTestPromptReader` (Queue<string>), and `historyFilePath` helper.
- `BlueCode.Cli.fsproj` has the `PrettyPrompt 4.1.1` PackageReference + `PromptReader.fs` Compile entry between `EditCommand.fs` and `Repl.fs`.
- Build succeeds; Core diff empty; `dotnet restore` completed; commit `feat(35-01): add PrettyPrompt 4.1.1 dep + PromptReader.fs ...` recorded.
  </done>
</task>

<task type="auto">
  <name>Task 2: Wire Repl.fs to use PromptReader (add promptReaderOverride seam, replace printf+Console.ReadLine with reader.ReadLineAsync)</name>
  <files>src/BlueCode.Cli/Repl.fs</files>
  <action>
**Step A — Add the `promptReaderOverride` seam at the top of `src/BlueCode.Cli/Repl.fs`.**

Place the new `let mutable` declaration IMMEDIATELY AFTER the existing `editorLauncherOverride` declaration (currently at lines 34-39 of Repl.fs; right after the doc-comment block). The two seams should be siblings:

```fsharp
/// Test-only seam: when set to Some, the Slash Edit arm uses this launcher
/// instead of EditCommand.realEditorLauncher. Production never sets this.
/// Mirrors Console.SetIn/SetOut redirection for stdio.
/// Concurrent tests must use testSequenced (CLAUDE.md "Console.SetOut in tests"
/// rule generalizes to any process-level mutable cell).
let mutable editorLauncherOverride : BlueCode.Cli.EditCommand.IEditorLauncher option = None

/// Test-only seam: when set to Some, runMultiTurnWithSession uses this reader
/// instead of PromptReader.makeRealPromptReader (). Production never sets this.
/// PrettyPrompt's `Console.ReadKey(intercept=true)` loop bypasses Console.SetIn,
/// so legacy Console.SetIn(StringReader(...)) test inputs no longer reach the
/// input layer; this seam is the replacement (Plan 35-02 migrates all 26
/// existing ReplTests to use it).
/// Concurrent tests must use testSequenced (same reason as editorLauncherOverride).
let mutable promptReaderOverride : BlueCode.Cli.PromptReader.IPromptReader option = None
```

**Step B — Refactor the input loop inside `runMultiTurnWithSession`.**

Currently (Repl.fs lines 291-296):
```fsharp
while running do
    printf "\nblueCode> "
    let line = Console.ReadLine()  // null on Ctrl+D / EOF

    match line with
    | null -> running <- false
    | _ ->
        match SlashCommand.parse line with
        ...
```

Make TWO changes:

**(B1) Instantiate the reader BEFORE the `while` loop** (after the `let mutable running = true` line, after `handlePromptTurn` is defined; immediately before `while running do`):

```fsharp
// Phase 35 (HIST-01..04): instantiate the prompt reader once per REPL session.
// Production: PrettyPrompt-backed with ~/.bluecode/history persistence.
// Tests: Plan 35-02 sets `promptReaderOverride` before invocation to inject a
// pre-canned string queue (avoids PrettyPrompt's TTY-bound ReadKey loop).
let reader =
    match promptReaderOverride with
    | Some r -> r
    | None -> BlueCode.Cli.PromptReader.makeRealPromptReader ()

while running do
    let! lineOpt = reader.ReadLineAsync()
    let line = lineOpt |> Option.toObj   // null on None = Ctrl+C / Ctrl+D / EOF
    ...
```

**(B2) Remove the `printf "\nblueCode> "` line and replace `let line = Console.ReadLine()` with the `let! lineOpt = reader.ReadLineAsync()` + `let line = lineOpt |> Option.toObj` lines shown above.**

The existing `match line with | null -> running <- false | _ -> ...` block stays UNCHANGED — it correctly maps `null` (from `Option.toObj` on `None`) to REPL exit, exactly as `Console.ReadLine`'s `null` did.

The full replacement region (existing lines 291-296 → new):
```fsharp
        // Phase 35 (HIST-01..04): instantiate the prompt reader once per REPL session.
        // Production: PrettyPrompt-backed with ~/.bluecode/history persistence
        // (up/down arrow recall, Ctrl+R reverse search, line editing keys).
        // Tests: Plan 35-02 sets `promptReaderOverride` before invocation to inject
        // a pre-canned string queue (avoids PrettyPrompt's TTY-bound ReadKey loop).
        let reader =
            match promptReaderOverride with
            | Some r -> r
            | None -> BlueCode.Cli.PromptReader.makeRealPromptReader ()

        while running do
            let! lineOpt = reader.ReadLineAsync()
            let line = lineOpt |> Option.toObj  // null on None = Ctrl+C / Ctrl+D / EOF

            match line with
            | null -> running <- false
            | _ ->
                match SlashCommand.parse line with
                ...  // (entire match body UNCHANGED — same arms, same handlers)
```

**Critical correctness notes (avoid breaking Phase 31/32/33/34 behavior):**

- The `let reader = ...` line MUST be inside `runMultiTurnWithSession`'s `task {}` body (NOT at module level). PrettyPrompt's constructor reads from disk; module-level instantiation would issue a disk read at process startup BEFORE single-turn mode (bench) is even dispatched (35-RESEARCH.md § Pitfall 2; § Bench Gate Isolation).
- The `Some r -> r | None -> makeRealPromptReader ()` pattern matches the `editorLauncherOverride` precedent at the Slash Edit arm (Repl.fs lines 387-390, Phase 34-01). DO NOT cache the reader at module level.
- The banner line `printfn "blueCode — multi-turn mode. Session: %s. Type /exit or press Ctrl+D to quit." idStr` (Repl.fs line 180) STAYS — it prints once at REPL start. PrettyPrompt renders the per-iteration prompt prefix (`blueCode> `), not the banner.
- The `eprintfn "Session: %s" idStr` (line 182) STAYS — stderr session-id echo for shell scripts.
- The `Some (Slash Edit) ->` arm (lines 371-399) uses `editorLauncherOverride` for its own seam — UNCHANGED. PrettyPrompt does NOT replace the editor; `/edit` still spawns `$EDITOR` via `EditCommand.openEditorAsync`.
- The `handlePromptTurn` helper (Phase 34-01; lines 191-289) UNCHANGED. It receives a `prompt: string` from the caller; the source of that string (typed at PrettyPrompt vs. read from `/edit` tmpfile) is opaque to it.

**Step C — Verify the test suite is RED (this is expected and correct for this plan).**

Run:
```bash
dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj
```

The Cli project MUST build clean. Then:
```bash
dotnet build tests/BlueCode.Tests/BlueCode.Tests.fsproj
```

Tests project MUST also build clean (no signature changes; only behavioral ones). Then:
```bash
timeout 60 dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | tail -40
```

EXPECTED: The 26 ReplTests integration tests will HANG or fail because they use `Console.SetIn(StringReader(...))` which PrettyPrompt's `Console.ReadKey(intercept=true)` loop does NOT consume. The test runner will likely time out on the first ReplTest. This is the documented Plan 35-02 entry condition (35-RESEARCH.md § Pitfall 1 — HIGHEST RISK).

DO NOT attempt to fix the tests in this plan. Plan 35-02 owns the migration. Document the test-suite RED state in the SUMMARY's "Open items handed to Plan 35-02" section.

If the build itself fails (compile errors), STOP and fix — those are wiring errors, not the expected RED test state. Common causes:
- Forgot `let!` (the `reader.ReadLineAsync()` returns `Task<string option>`; needs `let!` inside the `task {}` body)
- Forgot to import `PrettyPrompt.Configuration` in PromptReader.fs (Task 1) → `PromptConfiguration` unresolved
- `let reader = ...` placed OUTSIDE the `task {}` body → can't reference `promptReaderOverride` mutable from inside

**Atomic commit (Task 2):**
```bash
git add src/BlueCode.Cli/Repl.fs
git commit -m "feat(35-01): wire Repl input loop to PromptReader (replace Console.ReadLine; add promptReaderOverride seam)"
```

NEVER `git add -A`.

**What NOT to do:**
- Do NOT add `open BlueCode.Cli.PromptReader` at the top of Repl.fs — fully-qualify per Phase 33/34 convention (STATE.md "Fully-qualified BlueCode.Cli.* in Repl.fs").
- Do NOT change `runSingleTurn`'s signature or behavior — this plan ONLY touches `runMultiTurnWithSession`'s input acquisition path. Single-turn (bench) is unaffected.
- Do NOT remove the existing `Console.CancelKeyPress` handler in `runSingleTurn` (lines 64-68) — it covers in-turn LLM cancellation; PrettyPrompt's Ctrl+C handling is at the read phase only (35-RESEARCH.md § Pitfall 3 — they don't conflict).
- Do NOT add a try/catch around `reader.ReadLineAsync()` for `OperationCanceledException` — PrettyPrompt maps Ctrl+C to `IsSuccess = false` (returned as `None`), not an exception. The existing `null` check handles it via `Option.toObj`.
- Do NOT add a `printfn ""` or `Console.Out.Flush()` between iterations — PrettyPrompt manages its own line clearing and rendering; injecting writes between iterations would corrupt its display state.
- Do NOT pass a `console: IConsole` argument to the `Prompt` constructor — PrettyPrompt's default is `SystemConsole` which works on macOS Terminal.app + iTerm2 (35-RESEARCH.md § macOS Verification). Passing a custom IConsole is a test-only path; tests use the higher-level `promptReaderOverride` seam instead.
  </action>
  <verify>
1. `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0 with no errors.
2. `dotnet build tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0 (test project still compiles even though tests will hang at runtime — that's Plan 35-02's problem).
3. `grep -c 'promptReaderOverride' src/BlueCode.Cli/Repl.fs` returns at least 2 (1 mutable cell decl + 1 match site inside `runMultiTurnWithSession`).
4. `grep -n 'reader\.ReadLineAsync' src/BlueCode.Cli/Repl.fs` returns 1 match (the new input call inside `while running do`).
5. `grep -n 'Console\.ReadLine()' src/BlueCode.Cli/Repl.fs` returns 0 matches (the original `Console.ReadLine` is GONE from `runMultiTurnWithSession`).
6. `grep -n 'printf "\\\\nblueCode> "' src/BlueCode.Cli/Repl.fs` returns 0 matches (the manual prompt prefix is GONE — PrettyPrompt renders it).
7. `grep -n 'BlueCode.Cli.PromptReader' src/BlueCode.Cli/Repl.fs` returns at least 2 matches (the seam type annotation + the `makeRealPromptReader ()` call).
8. `git diff master -- src/BlueCode.Core/` is empty (Core purity preserved).
9. `bash scripts/check-no-async.sh` exits 0.
10. **Expected RED test state (informational; not a blocker for this plan):** `timeout 60 dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | tail -10` either hangs at first ReplTest (timeout fires) or shows ReplTests failing with `InvalidOperationException` from `Console.ReadKey` in non-TTY env. Tests other than the 26 ReplTests (e.g., SlashCommandTests, EditCommandTests, AgentLoopTests, etc.) should still pass — they don't enter `runMultiTurnWithSession`.
  </verify>
  <done>
- `Repl.fs` declares `let mutable promptReaderOverride : BlueCode.Cli.PromptReader.IPromptReader option = None` immediately after the `editorLauncherOverride` declaration.
- `Repl.fs` `runMultiTurnWithSession` instantiates `reader` (via override or `makeRealPromptReader ()`) BEFORE the `while running do` loop.
- `Repl.fs` `while running do` body uses `let! lineOpt = reader.ReadLineAsync()` + `let line = lineOpt |> Option.toObj`; the `printf "\nblueCode> "` and `Console.ReadLine()` lines are removed.
- The `match line with | null -> ...` block and entire slash-command dispatch UNCHANGED (parser is downstream of the reader).
- `dotnet build` green for both Cli and Tests projects.
- Test suite is RED for the 26 ReplTests (expected; Plan 35-02 fixes); non-ReplTests pass.
- Core purity preserved; no `async {}` introduced.
- Commit `feat(35-01): wire Repl input loop to PromptReader (replace Console.ReadLine; add promptReaderOverride seam)` recorded.
  </done>
</task>

<task type="auto">
  <name>Task 3: Update PROJECT.md Key Decisions row for PrettyPrompt to "Verified" outcome</name>
  <files>.planning/PROJECT.md</files>
  <action>
**Update `.planning/PROJECT.md` Key Decisions table.**

Locate the existing row (currently line 327):
```
| v2.5: Adopt PrettyPrompt NuGet for readline/history (HIST-01..04) | Self-implementing readline (~300-400 LOC, ANSI escape handling, cursor mgmt) is fragile; slash-only history misses up-arrow muscle-memory. PrettyPrompt is well-maintained .NET library providing up/down recall + Ctrl+R search + line editing in ~50 LOC integration. Trade: new NuGet vs custom code. Pattern matches v1.0's deliberate dependency choices (FsToolkit.ErrorHandling, Spectre.Console, JsonSchema.Net) — preferred established libraries over reinventing. PROJECT.md "no new packages without decision" satisfied via this entry. | — Pending — verify version + .NET 10 compat during Phase 35 research; outcome assessed at milestone close |
```

REPLACE the third column (the rightmost cell — currently `— Pending — verify version + .NET 10 compat during Phase 35 research; outcome assessed at milestone close`) with the verified outcome:

```
✓ Verified (Phase 35-01) — PrettyPrompt 4.1.1 added to BlueCode.Cli.fsproj; targets net8.0 (NuGet forward-compat permits net10.0 host); MPL-2.0 license; transitive `TextCopy` dep auto-resolved. Built clean against .NET 10. ~50 LOC integration as predicted (PromptReader.fs port + Repl.fs reader-driven input loop). HIST-01..04 production wiring complete; behavior validation + ReplTests migration in Plan 35-02.
```

The first two columns (Decision + Why) are UNCHANGED — only the Outcome column flips from `— Pending —` to `✓ Verified (...)`.

**Why this matters:** CLAUDE.md "Don't Do" list includes: *"Don't add new NuGet packages without a corresponding decision in `.planning/PROJECT.md` Key Decisions"*. The table entry already exists (added when v2.5 was scoped). This task closes the loop by transitioning Pending → Verified now that the package is on disk + building. PrettyPrompt is the FIRST and ONLY new NuGet dependency in v2.5; the convention demands the outcome update.

**What NOT to do:**
- Do NOT add a NEW row — the row already exists at line 327; just update the Outcome column.
- Do NOT alter the "Decision" or "Why" columns — they were locked at scoping time (2026-04-29).
- Do NOT change other table rows — they belong to other phases / milestones.
- Do NOT update the file's `*Last updated*` footer line — that's a milestone-close concern (v2.5 complete-milestone workflow).
- Do NOT touch any source code in this task — it's a doc-only update.

**Build / test impact:** None. PROJECT.md is documentation; no compiler / test runner sees it.

**Atomic commit (Task 3):**
```bash
git add .planning/PROJECT.md
git commit -m "docs(35-01): mark PrettyPrompt 4.1.1 NuGet decision as Verified in Key Decisions"
```

NEVER `git add -A`.
  </action>
  <verify>
1. `grep -n 'PrettyPrompt' .planning/PROJECT.md` returns at least 2 matches (the Decision row + the existing milestone-context mention at line 31).
2. `grep -n '— Pending — verify version + .NET 10 compat during Phase 35 research' .planning/PROJECT.md` returns 0 matches (the old Pending text is gone).
3. `grep -n '✓ Verified (Phase 35-01)' .planning/PROJECT.md` returns 1 match (the new outcome text).
4. `grep -n 'PrettyPrompt 4.1.1' .planning/PROJECT.md` returns 1 match (the version pin in the new outcome cell).
5. `git diff master -- .planning/PROJECT.md` shows ONLY the third-column change for the PrettyPrompt row — no unrelated edits.
6. `git log --oneline -1` shows `docs(35-01): mark PrettyPrompt 4.1.1 NuGet decision as Verified in Key Decisions`.
  </verify>
  <done>
- `.planning/PROJECT.md` Key Decisions row for PrettyPrompt has Outcome column flipped from `— Pending —` to `✓ Verified (Phase 35-01) — ...` with version + license + .NET 10 compat notes.
- No other rows or columns altered; doc footer untouched.
- Commit `docs(35-01): mark PrettyPrompt 4.1.1 NuGet decision as Verified in Key Decisions` recorded.
- CLAUDE.md "Don't Do — new NuGet without decision" invariant satisfied.
  </done>
</task>

</tasks>

<verification>
**Plan-level verification gates (run AFTER all 3 tasks complete):**

1. **Build green for src and tests projects:**
   ```bash
   dotnet build
   ```
   Both `BlueCode.Cli` and `BlueCode.Tests` compile with no errors. (Test runtime will be RED for ReplTests — expected; Plan 35-02 fixes.)

2. **NuGet package resolved + on disk:**
   ```bash
   dotnet restore src/BlueCode.Cli/BlueCode.Cli.fsproj 2>&1 | grep -i "PrettyPrompt"
   ```
   Shows PrettyPrompt 4.1.1 (and TextCopy transitive dep) installed.

3. **PromptReader.fs symbols defined:**
   ```bash
   grep -E '^(type IPromptReader|let (makeRealPromptReader|makeTestPromptReader|historyFilePath))' src/BlueCode.Cli/PromptReader.fs
   ```
   Returns at least 4 lines (the 4 public symbols).

4. **Repl.fs uses reader (NOT Console.ReadLine) inside runMultiTurnWithSession:**
   ```bash
   grep -n 'reader\.ReadLineAsync\|promptReaderOverride\|Console\.ReadLine' src/BlueCode.Cli/Repl.fs
   ```
   Shows `reader.ReadLineAsync` and `promptReaderOverride` matches; ZERO `Console.ReadLine` matches inside the file (note: this also confirms `Console.ReadLine` does NOT appear in `runSingleTurn` either — which is correct; runSingleTurn never reads from stdin).

5. **fsproj compile order correct:**
   ```bash
   grep -n "EditCommand.fs\|PromptReader.fs\|Repl.fs" src/BlueCode.Cli/BlueCode.Cli.fsproj
   ```
   Shows `EditCommand.fs` < `PromptReader.fs` < `Repl.fs` line numbers.

6. **Core purity preserved:**
   ```bash
   git diff master -- src/BlueCode.Core/
   ```
   Empty output.

7. **No `async {}` literal added:**
   ```bash
   bash scripts/check-no-async.sh
   ```
   Exits 0.

8. **PROJECT.md Key Decision flipped:**
   ```bash
   grep -c '✓ Verified (Phase 35-01)' .planning/PROJECT.md
   ```
   Returns 1.

9. **Bench gate IS NOT RUN in this plan.** Plan 35-02 owns SC-7 verification. Reason: bench non-regression gate proves zero impact; without ReplTests passing, we can't yet validate that the seam works correctly in test environments. Running bench here would be premature. (Bench MUST still pass — but that's Plan 35-02's responsibility.)

**Note on RED test state:** This plan deliberately leaves the test suite in a RED state for the 26 ReplTests. This is acceptable because:
- Wave structure (35-01 → 35-02) is sequential, NOT parallel.
- The structural change (PrettyPrompt replaces Console.ReadLine) MUST happen before the tests can be migrated (you can't migrate tests for a seam that doesn't exist).
- Verifier (gsd-verifier) running between Plan 35-01 and 35-02 should accept the structural state and defer behavior verification to Plan 35-02. The PLAN.md `must_haves.truths` for this plan are about WIRING, not about TESTS.
</verification>

<success_criteria>
This plan satisfies the following Phase 35 ROADMAP success criteria (partial coverage; Plan 35-02 covers the rest):

- **SC-1 (`BlueCode.Cli.fsproj` PrettyPrompt PackageReference; version + Key Decision update):** GREEN — Task 1 adds `<PackageReference Include="PrettyPrompt" Version="4.1.1" />`; Task 3 flips PROJECT.md Key Decisions outcome to Verified with version + .NET 10 compat notes.
- **SC-2 (`Repl.fs` Console.ReadLine path replaced with PrettyPrompt-based reader; slash command parser unaffected):** GREEN — Task 2 replaces the input acquisition path. SlashCommand.parse runs on the string returned from `reader.ReadLineAsync()`, unchanged from how it ran on `Console.ReadLine()` output.
- **SC-3 (Up/Down arrow recall in current REPL session):** GREEN via PrettyPrompt built-in (no application code needed; provided by `Prompt(persistentHistoryFilepath = ...)` constructor). Functional verification deferred to Plan 35-02 manual checkpoint (SC-8) since up/down can't be exercised through the test seam.
- **SC-4 (`~/.bluecode/history` append per submit; spec resolution: include slash commands; /edit content does NOT enter history):** GREEN — `historyFilePath()` returns `~/.bluecode/history`; PrettyPrompt's `SavePersistentHistoryAsync` appends per `ReadLineAsync` success. The /edit non-issue is by design (research § Pitfall 8: /edit content arrives via tmpfile, NOT through `ReadLineAsync`).
- **SC-5 (REPL load history on start; cap):** GREEN-with-trade-off — PrettyPrompt's `Prompt` constructor loads `persistentHistoryFilepath` async on instantiation. ROADMAP said "N = 1000 default"; PrettyPrompt's internal `HistoryLog.MaxHistoryEntries` is hard-coded at 500. Adopted PrettyPrompt's 500-entry cap (ROADMAP "1000" was a placeholder; SUMMARY documents the trade-off in Plan 35-02).
- **SC-6 (Ctrl+R reverse-search):** GREEN via PrettyPrompt built-in (no application code needed). Functional verification deferred to Plan 35-02 manual checkpoint (SC-8).
- **SC-7 (Bench gate 7/7 PASS preserved):** Plan 35-02's responsibility (gate is run there).
- **SC-8 (macOS Terminal.app + iTerm2 manual verification):** Plan 35-02's responsibility (human-verify checkpoint).
- **SC-9 (SlashCommand parser tests still pass post-PrettyPrompt):** PARTIAL — pure SlashCommand parser tests (Phase 31-01) take strings as input and don't touch any I/O; they will continue to pass unchanged. The 26 ReplTests integration tests will FAIL until Plan 35-02 migrates them off Console.SetIn — that is the Plan 35-02 entry condition.

This plan establishes production wiring + records the Key Decision; Plan 35-02 validates with test migration + new HIST tests + bench gate + manual verification.
</success_criteria>

<output>
After completion, create `.planning/phases/35-prettyprompt-readline-history/35-01-SUMMARY.md` with the following frontmatter and body:

```yaml
---
phase: 35-prettyprompt-readline-history
plan: 01
status: complete
date: <YYYY-MM-DD>
subsystem: cli-repl
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
commits:
  - feat(35-01): add PrettyPrompt 4.1.1 dep + PromptReader.fs (IPromptReader port + makeRealPromptReader + makeTestPromptReader + historyFilePath)
  - feat(35-01): wire Repl input loop to PromptReader (replace Console.ReadLine; add promptReaderOverride seam)
  - docs(35-01): mark PrettyPrompt 4.1.1 NuGet decision as Verified in Key Decisions
loc_delta:
  added: ~80
  removed: ~5
core_diff: empty
new_nuget: PrettyPrompt 4.1.1 (+ TextCopy 6.2.1 transitive)
---
```

Body sections (recommended):
- **What shipped** — IPromptReader port + makeRealPromptReader (PrettyPrompt 4.1.1) + makeTestPromptReader (Queue<string>) + historyFilePath helper (`~/.bluecode/history`); promptReaderOverride seam in Repl.fs; reader-driven input loop replaces printf+Console.ReadLine; PROJECT.md Key Decisions Verified outcome row.
- **NuGet outcome** — PrettyPrompt 4.1.1 (net8.0 → net10.0 forward compat); MPL-2.0; TextCopy 6.2.1 transitive (clipboard support, unused directly); ~50 LOC integration (PromptReader.fs ~50 + Repl.fs delta ~5 lines net).
- **Key decisions captured (locked open questions from 35-RESEARCH.md):** (#1) PromptConfiguration.Prompt = `"blueCode> "` (no leading newline); (#2) history includes ALL inputs incl. slash commands; (#3) no BLUECODE_NO_PRETTYPROMPT env var (seam covers test needs); (#4) PrettyPrompt's 500-entry hard cap accepted (HistoryLog.MaxHistoryEntries internal constant; ROADMAP "1000" was placeholder).
- **Test-suite RED state — handed to Plan 35-02** — 26 existing ReplTests integration tests use `Console.SetIn(StringReader(...))` which PrettyPrompt's `Console.ReadKey(intercept=true)` loop bypasses. Plan 35-02 migrates each to use `BlueCode.Cli.Repl.promptReaderOverride <- Some (BlueCode.Cli.PromptReader.makeTestPromptReader [...])`. Non-Repl tests (SlashCommandTests, EditCommandTests, AgentLoopTests, etc.) remain GREEN.
- **Bench gate isolation** — bench/run.sh --gate is single-turn (Program.fs `| words ->` branch → runSingleTurn → never enters runMultiTurnWithSession). PrettyPrompt is never instantiated in the bench path. Bench gate run is Plan 35-02's responsibility.
- **Open items handed to Plan 35-02** — (1) migrate 26 ReplTests off Console.SetIn → promptReaderOverride; (2) add new HIST-specific tests (history file write/load, makeTestPromptReader queue exhaustion); (3) bench gate 7/7 PASS verification; (4) manual SC-8 Terminal.app + iTerm2 verification (human-verify checkpoint).
- **Pitfalls dodged** — eager Prompt construction at module load (would issue disk read pre-banner; instead inside runMultiTurnWithSession); custom history file format (PrettyPrompt owns the file; base64-per-line internal); `open BlueCode.Cli.PromptReader` directive (fully-qualified per Phase 33-01 convention); BLUECODE_NO_PRETTYPROMPT env var (seam covers test needs without untested code path); `git add -A` (.claude/ + ~/.bluecode/ test artifacts intentionally untracked).
</output>
</content>
</invoke>