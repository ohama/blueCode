---
phase: 34-edit-multi-line-input
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/BlueCode.Cli/EditCommand.fs              # NEW
  - src/BlueCode.Cli/BlueCode.Cli.fsproj         # Compile-order: insert EditCommand.fs BEFORE Repl.fs
  - src/BlueCode.Cli/Repl.fs                     # extract handlePromptTurn + Slash Edit arm + Ctrl+C suppression
  - src/BlueCode.Cli/Rendering.fs                # renderHelp: drop "[coming in v2.5]" suffix from /edit line
  - tests/BlueCode.Tests/RenderingTests.fs       # adapt "[coming in v2.5]" testCase: occurrences 1→0; flip editLine assertion
  - tests/BlueCode.Tests/ReplTests.fs            # adapt "remaining future-stub command (/edit only)" testCase: re-purpose or remove
autonomous: true

must_haves:
  truths:
    - "User typing /edit in REPL launches $EDITOR (or vi) on a fresh empty tmpfile."
    - "After editor exits with non-empty content, that content is dispatched as the next prompt (single turn) reusing the same prompt-handling path as a typed prompt (including plan-mode branching when planModeActive=true)."
    - "After editor exits with empty content (or whitespace only), REPL prints 'Edit cancelled.' and returns to the prompt; current turn is NOT affected; no LLM call was made."
    - "Tmpfile is deleted after read (try/finally), and ALSO cleaned up on REPL exit via AppDomain.ProcessExit handler."
    - "Ctrl+C pressed while editor is running does NOT kill blueCode (REPL stays alive); editor handles its own SIGINT."
    - "/edit help line in renderHelp no longer carries the '[coming in v2.5]' marker (0 occurrences in renderHelp string)."
  artifacts:
    - path: "src/BlueCode.Cli/EditCommand.fs"
      provides: "IEditorLauncher port + realEditorLauncher + openEditorAsync + ProcessExit atexit registration"
      exports: ["IEditorLauncher", "realEditorLauncher", "openEditorAsync"]
      min_lines: 60
    - path: "src/BlueCode.Cli/Repl.fs"
      provides: "Slash Edit arm replaces 'not yet implemented' stub; handlePromptTurn helper factored out and shared by Slash Edit + Prompt arms (planModeActive branching reused)"
      contains: "openEditorAsync"
    - path: "src/BlueCode.Cli/Rendering.fs"
      provides: "renderHelp /edit line without '[coming in v2.5]' suffix"
      pattern: "/edit\\s+open \\$EDITOR for multi-line input"
    - path: "src/BlueCode.Cli/BlueCode.Cli.fsproj"
      provides: "EditCommand.fs Compile entry placed AFTER PlanGate.fs and BEFORE Repl.fs"
      contains: "EditCommand.fs"
  key_links:
    - from: "src/BlueCode.Cli/Repl.fs Slash Edit arm"
      to: "src/BlueCode.Cli/EditCommand.fs openEditorAsync"
      via: "let! contentOpt = EditCommand.openEditorAsync EditCommand.realEditorLauncher"
      pattern: "EditCommand\\.openEditorAsync"
    - from: "src/BlueCode.Cli/Repl.fs Slash Edit arm Some content branch"
      to: "src/BlueCode.Cli/Repl.fs handlePromptTurn helper"
      via: "do! handlePromptTurn content"
      pattern: "handlePromptTurn"
    - from: "src/BlueCode.Cli/Repl.fs Some (Prompt prompt) arms (both planModeActive and direct)"
      to: "src/BlueCode.Cli/Repl.fs handlePromptTurn helper"
      via: "Existing inline logic lifted into shared helper; both prompt arms now call do! handlePromptTurn"
      pattern: "handlePromptTurn"
    - from: "src/BlueCode.Cli/EditCommand.fs realEditorLauncher"
      to: "System.Diagnostics.Process"
      via: "ProcessStartInfo with UseShellExecute=false and ALL THREE Redirect*=false (TTY inheritance)"
      pattern: "RedirectStandardInput\\s*<-\\s*false"
---

<objective>
Implement the structural change for `/edit` multi-line input (EDIT-01): introduce the `IEditorLauncher` port (mirrors `IKeyReader` from PlanGate.fs), wire its production implementation that spawns `$EDITOR` (or `vi`) with proper TTY inheritance, refactor `Repl.fs` to factor out a shared `handlePromptTurn` helper so the new `Slash Edit` arm and the existing `Some (Prompt ...)` arms both flow through the same plan-mode-aware dispatch, and update the help text to drop the `[coming in v2.5]` marker.

Purpose: Long refactor / multi-step / structured prompt entry must not require pasting a single mega-line into `Console.ReadLine`. `$EDITOR` invocation is the well-known UNIX convention (`git commit`, `crontab -e`); replicating it inside the REPL closes the last "ergonomic gap" of multi-line input and unblocks the v2.5 milestone's daily-driver value. This plan establishes the production wiring that Plan 34-02 then validates with mock-launcher behavior tests + bench gate.

Output: New `EditCommand.fs` module, updated `Repl.fs` (Slash Edit arm + handlePromptTurn refactor), updated `Rendering.fs` (help text), updated `.fsproj` compile order, and 2 existing tests adapted so the suite continues to compile and pass after the structural change.
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
@.planning/phases/34-edit-multi-line-input/34-RESEARCH.md

# Source files this plan modifies or directly depends on:
@src/BlueCode.Cli/Repl.fs
@src/BlueCode.Cli/Rendering.fs
@src/BlueCode.Cli/SlashCommand.fs
@src/BlueCode.Cli/PlanGate.fs
@src/BlueCode.Cli/BlueCode.Cli.fsproj
@tests/BlueCode.Tests/RenderingTests.fs
@tests/BlueCode.Tests/ReplTests.fs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create EditCommand.fs module (IEditorLauncher port + realEditorLauncher + openEditorAsync + atexit registration)</name>
  <files>src/BlueCode.Cli/EditCommand.fs (NEW), src/BlueCode.Cli/BlueCode.Cli.fsproj</files>
  <action>
**Step A — Create `src/BlueCode.Cli/EditCommand.fs`** with the following content (verbatim from research § Code Examples + atexit Pattern 7, with one minor addition — module-level `currentTmpPath` mutable cell tracked by `openEditorAsync` so `ProcessExit` handler can sweep on abnormal termination):

```fsharp
module BlueCode.Cli.EditCommand

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks

/// Abstraction over editor launching so tests can inject scripted content
/// without needing a real terminal. Mirrors IKeyReader from PlanGate.fs.
type IEditorLauncher =
    /// Launch editor on tmpPath, block until editor exits.
    /// Production: spawns $EDITOR or vi with inherited TTY.
    /// Tests: writes scripted content directly to tmpPath then returns.
    abstract member Launch : tmpPath: string -> unit

/// Parse $EDITOR env var into (binary, extraArgs).
/// Supports "vi", "code --wait", "emacs -nw", etc.
/// Empty/whitespace -> ("vi", []) fallback.
let private parseEditorEnv () : string * string list =
    let envVal = Environment.GetEnvironmentVariable("EDITOR")
    if String.IsNullOrWhiteSpace(envVal) then
        ("vi", [])
    else
        let parts =
            envVal.Trim().Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
        (parts.[0], parts.[1..] |> Array.toList)

/// Production launcher: uses $EDITOR or vi fallback.
/// CRITICAL: all THREE Redirect* MUST be false so the child inherits the
/// parent's TTY file descriptors. UseShellExecute=false because on macOS
/// UseShellExecute=true uses /usr/bin/open (app bundle), not a shell —
/// terminal editors would open in a new window or fail silently.
/// Process.Start can throw (Win32Exception / IOException) if $EDITOR
/// points to a missing binary — wrap in try/with so the REPL never crashes.
let realEditorLauncher : IEditorLauncher =
    { new IEditorLauncher with
        member _.Launch tmpPath =
            let (bin, extraArgs) = parseEditorEnv ()
            let psi = ProcessStartInfo(bin)
            for arg in extraArgs do
                psi.ArgumentList.Add(arg)
            psi.ArgumentList.Add(tmpPath)
            psi.UseShellExecute <- false
            psi.RedirectStandardInput  <- false
            psi.RedirectStandardOutput <- false
            psi.RedirectStandardError  <- false
            // NO CreateNoWindow — Windows-only flag; do not set on macOS.
            let startResult =
                try Ok(Process.Start(psi))
                with ex -> Error(bin, ex)
            match startResult with
            | Error (b, ex) ->
                // Friendly error; tmpPath remains empty -> openEditorAsync returns None -> REPL prints "Edit cancelled.".
                printfn "Cannot launch editor '%s': %s" b ex.Message
                printfn "Edit cancelled (editor unavailable)."
            | Ok proc ->
                use _ = proc
                proc.WaitForExit()
                // Exit code intentionally ignored: content-based cancel
                // (research § Open Question #3, Pitfall — :q! returns 0 anyway).
    }

/// Tracks the most recently created tmpfile so AppDomain.ProcessExit can
/// sweep it if the process is killed mid-edit (covers the gap where
/// openEditorAsync's try/finally cleanup did not run).
let mutable private currentTmpPath : string option = None

// One-time atexit registration (module initializer; runs on first reference).
do
    AppDomain.CurrentDomain.ProcessExit.Add(fun _ ->
        match currentTmpPath with
        | Some path ->
            try if File.Exists path then File.Delete path with _ -> ()
        | None -> ())

/// Open tmpfile in editor, return Some content (trimmed) or None (empty/cancelled).
/// Creates tmpfile via Path.GetTempFileName (atomic 0-byte create), then
/// renames to .md so terminal editors with extension-based syntax detection
/// (vim/nano) get a markdown buffer. Cleans up tmpfile in `finally` regardless
/// of editor outcome (success / exception / cancel).
///
/// Empty check uses `Trim() = ""` so whitespace-only files are treated as
/// cancel (research § Pitfall 4 — degenerate "no real content").
let openEditorAsync (launcher: IEditorLauncher) : Task<string option> =
    task {
        let rawTmp = Path.GetTempFileName()
        let tmpPath = Path.ChangeExtension(rawTmp, ".md")
        File.Move(rawTmp, tmpPath)   // rawTmp gone; tmpPath is the live file
        currentTmpPath <- Some tmpPath
        try
            launcher.Launch tmpPath
            let content =
                if File.Exists tmpPath then File.ReadAllText(tmpPath).Trim()
                else ""
            return (if content = "" then None else Some content)
        finally
            try
                if File.Exists tmpPath then File.Delete tmpPath
            with _ -> ()
            currentTmpPath <- None
    }
```

**Step B — Update `src/BlueCode.Cli/BlueCode.Cli.fsproj`** to add `EditCommand.fs` to the `<ItemGroup>` Compile list. Insert it AFTER `PlanGate.fs` and BEFORE `Repl.fs` (Repl.fs references `EditCommand.openEditorAsync`):

```xml
<Compile Include="PlanGate.fs" />
<Compile Include="EditCommand.fs" />          <!-- NEW: must precede Repl.fs -->
<Compile Include="Repl.fs" />
```

**Why this exact order:** F# compile order is significant; `Repl.fs` opens or fully-qualifies `BlueCode.Cli.EditCommand`, so `EditCommand.fs` must be compiled first. PlanGate.fs is the closest sibling (same `IXxxReader/IXxxLauncher` port pattern) so placing EditCommand.fs adjacent keeps related modules together.

**What NOT to do:**
- Do NOT touch `src/BlueCode.Core/**` — Core purity invariant (CLAUDE.md: "Core purity (absolute)"). EditCommand.fs is Cli-only; uses System.Diagnostics, System.IO, System.Threading.Tasks — all forbidden in Core.
- Do NOT use `async {}` — use `task {}` (CLAUDE.md). Note: EditCommand.fs is in Cli, where `async {}` is not banned, but consistent style with QwenHttpClient/FsToolExecutor is `task {}`.
- Do NOT add `psi.CreateNoWindow <- true` — Windows-only; signals confusion (research § Pitfall 2).
- Do NOT redirect any of stdin/stdout/stderr — vi/vim refuses interactive mode if stdin is not a TTY (research § Pitfall 1).
- Do NOT add a timeout / linked CTS to `WaitForExit` — the user controls when the editor exits.
- Do NOT check `proc.ExitCode` — content-based cancel (`:q!` returns 0 anyway).

**Build immediately after writing** to catch compile errors before moving on. Use:
```bash
dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj
```

**Atomic commit (Task 1):**
```bash
git add src/BlueCode.Cli/EditCommand.fs src/BlueCode.Cli/BlueCode.Cli.fsproj
git commit -m "feat(34-01): add IEditorLauncher port + openEditorAsync (EditCommand.fs)"
```
NEVER `git add -A` or `git add .` — `.claude/` and `localLLM/` are intentionally untracked (CLAUDE.md "Commit protocol").
  </action>
  <verify>
1. `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0 with no warnings about `EditCommand.fs`.
2. `grep -n "EditCommand.fs" src/BlueCode.Cli/BlueCode.Cli.fsproj` shows the entry sandwiched between `PlanGate.fs` and `Repl.fs`.
3. `grep -n "RedirectStandardInput\s*<-\s*false" src/BlueCode.Cli/EditCommand.fs` returns at least one match (TTY inheritance invariant).
4. `grep -n "AppDomain.CurrentDomain.ProcessExit" src/BlueCode.Cli/EditCommand.fs` returns 1 match (atexit registration).
5. `git diff master -- src/BlueCode.Core/` is empty (Core purity preserved).
6. `bash scripts/check-no-async.sh` passes (no `async {}` literal added).
  </verify>
  <done>
- `src/BlueCode.Cli/EditCommand.fs` exists with `IEditorLauncher` interface, `realEditorLauncher`, `openEditorAsync`, and module-level `do` block registering `AppDomain.CurrentDomain.ProcessExit` cleanup.
- `BlueCode.Cli.fsproj` compiles `EditCommand.fs` between `PlanGate.fs` and `Repl.fs`.
- Build succeeds; Core diff empty; commit `feat(34-01): add IEditorLauncher port + openEditorAsync (EditCommand.fs)` recorded.
  </done>
</task>

<task type="auto">
  <name>Task 2: Refactor Repl.fs (extract handlePromptTurn + Slash Edit arm + Ctrl+C suppression) and update renderHelp</name>
  <files>src/BlueCode.Cli/Repl.fs, src/BlueCode.Cli/Rendering.fs</files>
  <action>
**Step A — Refactor `src/BlueCode.Cli/Repl.fs` `runMultiTurnWithSession`:**

Inside `runMultiTurnWithSession`, BEFORE the `while running do` loop and AFTER the `let mutable running = true` line, define a local helper function `handlePromptTurn` that LIFTS the inline body of BOTH the `Some (Prompt prompt) when planModeActive ->` arm (lines 265-343 in current Repl.fs) AND the `Some (Prompt prompt) ->` arm (lines 344-360 in current Repl.fs) so the same dispatch logic is callable from the Slash Edit arm.

The helper signature:
```fsharp
let handlePromptTurn (prompt: string) : Task<unit> =
    task {
        if planModeActive then
            // ... lift the existing planModeActive plan-gate body verbatim ...
            // (uses currentSession, components, renderMode, sessionStore, lastCode,
            //  rejectCount/currentPrompt/turnDone locals declared INSIDE this arm)
            ()
        else
            // ... lift the existing direct dispatch body verbatim ...
            ()
    }
```

The lifted bodies use `prompt` (the function parameter) wherever the original arm used the `prompt` from its own pattern match. Mutable cells `currentSession`, `planModeActive`, `lastCode` are CAPTURED from the enclosing `runMultiTurnWithSession` scope (they remain `let mutable` at the same scope level — closure captures the cell, not a snapshot).

**Critical correctness notes (avoid breaking Phase 33 plan-mode behavior):**
- `runSingleTurn prompt currentSession.Steps components renderMode` MUST pass the ORIGINAL `prompt` parameter (the one the user typed / produced via /edit), NOT the per-iteration `currentPrompt` mutable that the plan-mode loop uses for retry feedback. This matches the existing line 312 (`runSingleTurn prompt ...` not `runSingleTurn currentPrompt ...`) — preserve that EXACTLY.
- `planModeActive <- false` assignments inside the plan-mode branches (Accept/Quit/error/exhausted-rejects) STILL flip the outer cell — the closure captures the mutable cell. Verify by tracing: each of those 4 sites in current Repl.fs (lines 301, 310, 337, 343) must remain in the lifted body.
- `lastCode <- if code = 130 then 0 else code` (line 324 + line 360) must remain in the lifted body so REPL exit code propagates.
- `eprintfn "WARNING: session save failed: %A" e` (line 323 + line 359) must remain.

After defining `handlePromptTurn`, REPLACE the two `Some (Prompt ...)` arms with:
```fsharp
| Some (Prompt prompt) ->
    do! handlePromptTurn prompt
```
(One arm, no `when` guard — the planModeActive branching now lives INSIDE handlePromptTurn.)

REPLACE the existing `Some (Slash Edit) ->` arm (currently lines 261-264, the "not yet implemented" stub) with:

```fsharp
| Some (Slash Edit) ->
    // Phase 34 (EDIT-01): /edit opens $EDITOR (or vi) on a tmpfile, reads
    // content after the editor exits. Non-empty content is dispatched as
    // the next prompt through the same handlePromptTurn used for typed
    // prompts (so plan-mode branching is preserved if planModeActive=true
    // when /edit is invoked). Empty/whitespace-only content -> "Edit cancelled."
    //
    // Ctrl+C while editor is open: register a CancelKeyPress handler that
    // sets args.Cancel=true so SIGINT does NOT kill blueCode. The editor
    // (vi/vim) handles its own SIGINT (cancel -> normal mode); if user
    // force-kills the editor, WaitForExit returns and the empty-file path
    // produces "Edit cancelled." (research § Pattern 5 simplified handler).
    let cancelHandler =
        System.ConsoleCancelEventHandler(fun _ args -> args.Cancel <- true)
    Console.CancelKeyPress.AddHandler(cancelHandler)
    try
        let! contentOpt =
            BlueCode.Cli.EditCommand.openEditorAsync
                BlueCode.Cli.EditCommand.realEditorLauncher
        match contentOpt with
        | None ->
            printfn "Edit cancelled."
        | Some content ->
            do! handlePromptTurn content
    finally
        Console.CancelKeyPress.RemoveHandler(cancelHandler)
```

**Why fully-qualified names** (`BlueCode.Cli.EditCommand.openEditorAsync` rather than `open BlueCode.Cli.EditCommand`): Phase 33-01 established this convention (STATE.md "Fully-qualified BlueCode.Cli.PlanGate.* in Repl.fs" decision) — avoids module-alias conflicts and matches `BlueCode.Core.AgentLoop.runPlanTurn` style. Do not add a new `open` directive at the top of Repl.fs.

**Step B — Update `src/BlueCode.Cli/Rendering.fs` `renderHelp`:**

In the `renderHelp` triple-quoted string (currently line 129-139), change the `/edit` line from:
```
  /edit              open $EDITOR for multi-line input [coming in v2.5]
```
to:
```
  /edit              open $EDITOR for multi-line input
```

Drop ONLY the `[coming in v2.5]` suffix (and any preceding whitespace immediately before it). All other lines unchanged. The 9-command count MUST remain 9 (count is preserved by the existing tests).

**What NOT to do:**
- Do NOT add a new `open BlueCode.Cli.EditCommand` directive at the top of `Repl.fs` — fully-qualify per Phase 33-01 precedent.
- Do NOT remove the `/plan` `Some (Slash Plan)` arm or any other slash arm — Phase 34 only promotes `/edit` from stub to live; all other arms are untouched.
- Do NOT change `runSingleTurn`'s signature or behavior — the plan integrates with it identically to how Phase 33's plan-gate arm did.
- Do NOT redirect `Console.SetIn` or `Console.SetOut` from inside the handlers — the editor inherits the parent's TTY directly, so console redirection would actively break TTY inheritance.
- Do NOT remove the existing `Console.CancelKeyPress` handler in `runSingleTurn` (lines 64-68) — that handler covers in-turn cancellation; the new handler in the Slash Edit arm covers the `/edit` invocation specifically.
- Do NOT touch `Some (Prompt prompt) when planModeActive` in a way that changes its observable behavior — Plan 33-02's 6 ReplTests integration tests still need to pass; the lifting must be a behavior-preserving refactor.

**Build + run tests immediately to catch the refactor:**
```bash
dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj
# If the build succeeds, run the existing test suite to confirm the lift is behavior-preserving:
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
```
Expected: existing 352 tests still pass (renderHelp/[coming in v2.5] + remaining-stub assertions in Task 3 will fail; that's fine, Task 3 fixes them). Plan-mode tests (Phase 33-02) MUST still pass — if any fail, the lift broke behavior.

**Atomic commit (Task 2):**
```bash
git add src/BlueCode.Cli/Repl.fs src/BlueCode.Cli/Rendering.fs
git commit -m "feat(34-01): wire /edit to EditCommand.openEditorAsync via handlePromptTurn refactor"
```
NEVER `git add -A`.
  </action>
  <verify>
1. `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.
2. `grep -n "handlePromptTurn" src/BlueCode.Cli/Repl.fs` shows AT LEAST 3 occurrences (1 def + 2 call sites: `do! handlePromptTurn prompt` in the Prompt arm, and `do! handlePromptTurn content` in the Slash Edit arm).
3. `grep -n "EditCommand.openEditorAsync\|EditCommand.realEditorLauncher" src/BlueCode.Cli/Repl.fs` shows 2 matches (one each).
4. `grep -n "not yet implemented" src/BlueCode.Cli/Repl.fs` shows 0 matches (the stub is gone).
5. `grep -n '"\[coming in v2.5\]"' src/BlueCode.Cli/Rendering.fs` shows 0 matches.
6. `grep -c "/" src/BlueCode.Cli/Rendering.fs` shows the renderHelp body still lists all 9 commands (verify by `grep -n "^  /" src/BlueCode.Cli/Rendering.fs` returns 9 lines).
7. `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` runs to completion. Most tests pass; the 2 known-failing tests are exactly: (a) `renderHelp marks future commands as [coming in v2.5]` (RenderingTests, asserts 1 occurrence; now 0) and (b) `runMultiTurn: remaining future-stub command (/edit only)` (ReplTests, asserts "not yet implemented" line; now 0). Phase 33's 6 ReplTests integration tests (plan-gate Accept/Quit/Error/etc.) MUST PASS — if any of them fail, the handlePromptTurn lift broke behavior; revisit Step A.
  </verify>
  <done>
- `Repl.fs` defines `handlePromptTurn` once and calls it from BOTH the `Some (Prompt prompt) ->` arm AND the `Some (Slash Edit) ->` Some-content branch.
- `Repl.fs` `Some (Slash Edit)` arm wraps the `openEditorAsync` call with `Console.CancelKeyPress` add/remove handler.
- `Repl.fs` no longer contains the string `"not yet implemented"`.
- `Rendering.fs` `renderHelp` no longer contains `"[coming in v2.5]"`.
- Build green; all Phase 33 tests still pass; only the 2 expected-to-be-adapted tests fail (those are fixed in Task 3).
- Commit `feat(34-01): wire /edit to EditCommand.openEditorAsync via handlePromptTurn refactor` recorded.
  </done>
</task>

<task type="auto">
  <name>Task 3: Adapt 2 existing tests to the post-Phase-34 reality</name>
  <files>tests/BlueCode.Tests/RenderingTests.fs, tests/BlueCode.Tests/ReplTests.fs</files>
  <action>
**Step A — Update `tests/BlueCode.Tests/RenderingTests.fs`:**

Locate the testCase at line 93: `"renderHelp marks future commands as [coming in v2.5] (Phase 33: 1 stub remaining — /edit only)"`. Replace its assertions to reflect Phase 34 reality (0 stubs remaining, /edit promoted to live):

Either: (a) DELETE the testCase entirely (preferred — its raison d'être was tracking the v2.5 stub-marker phase-out and Phase 34 completes the phase-out), OR (b) RENAME and INVERT the assertions:
```fsharp
testCase "renderHelp has 0 [coming in v2.5] markers (Phase 34 promoted /edit; all v2.5 commands live)" <| fun _ ->
    let h = renderHelp
    let occurrences =
        let mutable count = 0
        let mutable i = 0
        while i >= 0 do
            i <- h.IndexOf("[coming in v2.5]", i)
            if i >= 0 then
                count <- count + 1
                i <- i + "[coming in v2.5]".Length
        count
    Expect.equal occurrences 0 "0 [coming in v2.5] markers (all v2.5 commands live as of Phase 34)"
    let lines = h.Split([| '\n' |])
    let editLine = lines |> Array.find (fun l -> l.TrimStart().StartsWith("/edit"))
    Expect.isFalse (editLine.Contains("[coming in v2.5]")) "/edit no longer carries [coming in v2.5] (Phase 34 live)"
    Expect.isTrue (editLine.Contains("open $EDITOR")) "/edit line retains the descriptive text"
```

PREFER option (b) — keep the regression fence in the suite so future edits to `renderHelp` that re-introduce a `[coming in v2.5]` marker are caught immediately. Only choose (a) if (b) cannot be made to compile cleanly.

The OTHER renderHelp testCase at line 81 (`"renderHelp lists all 9 commands"`) is INVARIANT — it asserts `Expect.stringContains h "/edit" "must list /edit"` which still passes. Do NOT modify it.

**Step B — Update `tests/BlueCode.Tests/ReplTests.fs`:**

Locate the testCase at line 617: `"runMultiTurn: remaining future-stub command (/edit only) prints 'not yet implemented' without crashing"`. Replace its purpose: Phase 34 promotes `/edit` to live behavior, so the original assertion is invalid.

Replace with:
```fsharp
testCase "runMultiTurn: 0 future-stub commands remaining (Phase 34 promoted /edit; nothing prints 'not yet implemented')" <| fun () ->
    // Phase 34 update: /edit is now live (real handler — tested with mock launcher in Plan 34-02).
    // This test asserts the post-Phase-34 invariant: NO slash command in the 9-command set
    // prints "not yet implemented" any more. Stays as a regression fence so any
    // re-introduction of a stub command is caught immediately.
    let originalIn = Console.In
    let originalOut = Console.Out
    // Drive every slash command (skip /edit because real /edit would block on $EDITOR; in this
    // test we only validate the absence of the stub-marker for the in-process commands).
    use stdinReader = new StringReader("/help\n/status\n/sessions\n/exit\n")
    use stdoutWriter = new StringWriter()
    Console.SetIn(stdinReader)
    Console.SetOut(stdoutWriter)

    let tempRoot =
        Path.Combine(Path.GetTempPath(), sprintf "bluecode-stub-%s" (Guid.NewGuid().ToString("N")))
    Directory.CreateDirectory(tempRoot) |> ignore
    let sinkPath =
        Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
    use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

    let components: AppComponents =
        { LlmClient = stubLlm []   // in-process slash commands; LLM must not be called
          ToolExecutor = stubToolsOk
          SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
          JsonlSink = sink
          Config =
            { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = None }
          ProjectRoot = tempRoot
          LogPath = sinkPath
          MaxModelLen = 8192 }

    try
        let exitCode =
            BlueCode.Cli.Repl.runMultiTurn components Compact
            |> fun t -> t.GetAwaiter().GetResult()
        Console.Out.Flush()
        let captured = stdoutWriter.ToString()
        Expect.equal exitCode 0 "exit code 0 — no slash command crashes the REPL"
        let stubLines =
            captured.Split([| '\n' |])
            |> Array.filter (fun l -> l.Contains("not yet implemented"))
        Expect.equal stubLines.Length 0
            (sprintf "expected 0 'not yet implemented' lines (Phase 34 promoted /edit); captured:\n%s" captured)
    finally
        Console.SetIn(originalIn)
        Console.SetOut(originalOut)
```

**Why drive `/help`/`/status`/`/sessions` instead of `/edit`:** The point of this regression test is "no in-process slash command emits the stub marker". Driving `/edit` here would invoke `realEditorLauncher` which spawns `$EDITOR` — wrong abstraction layer for this fence test (and would block CI). Plan 34-02 covers the `/edit` mock-launcher integration test where the launcher is replaceable.

**What NOT to do:**
- Do NOT delete the test outright — keep the regression fence (otherwise a future stub re-introduction goes unnoticed).
- Do NOT add a new test module here — the new EditCommandTests.fs and new ReplTests integration tests are Plan 34-02's scope.
- Do NOT change `testSequenced` wrapping — both tests are already inside the existing `testSequenced (testList ...)` envelope; their `Console.SetOut` redirects depend on it (CLAUDE.md "Console.SetOut in tests" + STATE.md "Spectre.Console singleton reset for ReplTests").
- Do NOT add `Spectre.Console.AnsiConsole.Console <-` reset for these specific tests — they don't exercise PlanGate.render (which is the only path that triggers AnsiConsole's lazy cache, per STATE.md Phase 33-02 decision).

**Build + run the full test suite to confirm green:**
```bash
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
```
Expected: ALL 352 tests pass (no net delta in count — 2 modified, 0 added in this plan; new tests come in Plan 34-02).

**Atomic commit (Task 3):**
```bash
git add tests/BlueCode.Tests/RenderingTests.fs tests/BlueCode.Tests/ReplTests.fs
git commit -m "test(34-01): adapt 2 existing tests for /edit live promotion"
```
NEVER `git add -A`.

**Plan-meta commit (separate from code commits per CLAUDE.md):**
After all 3 tasks committed individually, this plan's PLAN.md is on disk. Do NOT commit it yet — the orchestrator's final git_commit step bundles `PLAN.md` + `ROADMAP.md` together.
  </action>
  <verify>
1. `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` reports `Tests run: 352, Errors: 0, Failures: 0` (or whatever the existing baseline is — count must NOT decrease, but no new tests added in this plan).
2. `grep -n '"\[coming in v2.5\]"' tests/BlueCode.Tests/RenderingTests.fs` shows the string only inside the new "0 markers" assertion (no `Expect.equal occurrences 1` survives).
3. `grep -n "not yet implemented" tests/BlueCode.Tests/ReplTests.fs` shows the string only inside an `Expect.equal stubLines.Length 0` context (no `Expect.equal stubLines.Length 1` survives).
4. `git log --oneline -3` shows three Task commits in order: `feat(34-01): add IEditorLauncher port...`, `feat(34-01): wire /edit to EditCommand.openEditorAsync...`, `test(34-01): adapt 2 existing tests for /edit live promotion`.
5. `git diff master -- src/BlueCode.Core/` is empty.
6. Smoke test with the binary (manual sanity — NOT a blocking gate): `dotnet build` succeeds; `dotnet run --project src/BlueCode.Cli` starts the REPL; typing `/help` shows the 9-command list with NO `[coming in v2.5]` marker on the `/edit` line.
  </verify>
  <done>
- Both target tests adapted; assertions inverted (0 occurrences instead of 1).
- Full test suite passes (no net count change vs baseline; this plan modifies tests, doesn't add).
- 3 atomic commits recorded for this plan (one per task).
- Phase 33 plan-mode tests still pass (handlePromptTurn refactor is behavior-preserving).
- Manual REPL smoke confirms `/help` shows clean output.
  </done>
</task>

</tasks>

<verification>
**Plan-level verification gates (run AFTER all 3 tasks complete):**

1. **Build green:**
   ```bash
   dotnet build
   ```
   Both `BlueCode.Cli` and `BlueCode.Tests` compile with no errors.

2. **All tests pass (no count regression):**
   ```bash
   dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
   ```
   Reports `Errors: 0, Failures: 0`. Test count delta = 0 (2 modified, 0 added).

3. **Core purity preserved:**
   ```bash
   git diff master -- src/BlueCode.Core/
   ```
   Empty output.

4. **No `async {}` literal added:**
   ```bash
   bash scripts/check-no-async.sh
   ```
   Exits 0.

5. **fsproj compile order correct:**
   ```bash
   grep -n "PlanGate.fs\|EditCommand.fs\|Repl.fs" src/BlueCode.Cli/BlueCode.Cli.fsproj
   ```
   Shows `PlanGate.fs` < `EditCommand.fs` < `Repl.fs` line numbers.

6. **handlePromptTurn called from both arms:**
   ```bash
   grep -c "do! handlePromptTurn" src/BlueCode.Cli/Repl.fs
   ```
   Returns 2 (Slash Edit Some-content branch + Some Prompt arm).

7. **Manual REPL smoke (not a blocking gate; sanity only):** `dotnet run --project src/BlueCode.Cli` then type `/help` — the help table shows `/edit              open $EDITOR for multi-line input` (no `[coming in v2.5]` suffix). Type `/exit`. Bench gate is Plan 34-02's responsibility.

**Note:** This plan is the structural change. Behavior validation (mock IEditorLauncher tests + ReplTests integration tests + bench gate 7/7) is Plan 34-02's responsibility. If verifier (gsd-verifier) runs after Plan 34-01 in isolation, it should accept the structural state without behavior tests existing yet — those are the WAVE 2 deliverable.
</verification>

<success_criteria>
This plan satisfies the following Phase 34 ROADMAP success criteria (partial coverage; Plan 34-02 covers the rest):

- **SC-1 (`/edit` invokes Path.GetTempFileName):** PARTIAL — `openEditorAsync` calls `Path.GetTempFileName()` in EditCommand.fs. Behavior validated by Plan 34-02 mock-launcher tests.
- **SC-2 ($EDITOR env var; vi fallback; friendly error):** PARTIAL — `parseEditorEnv` + `realEditorLauncher` Process.Start try/with covers all three paths. Behavior validated by Plan 34-02.
- **SC-3 (non-empty content -> next prompt; empty -> cancel):** PARTIAL — `openEditorAsync` returns `string option` (Some for non-empty trimmed content; None for empty/whitespace); Slash Edit arm dispatches to `handlePromptTurn` on Some, prints "Edit cancelled." on None. Behavior validated by Plan 34-02.
- **SC-4 (tmpfile read-then-delete; atexit cleanup):** PARTIAL — `try/finally` in `openEditorAsync` deletes after read; module-level `do AppDomain.CurrentDomain.ProcessExit.Add(...)` registers atexit cleanup. Behavior validated by Plan 34-02 (tmpfile-deleted-after-read testCase).
- **SC-5 (Ctrl+C during edit; child process exit; REPL recovers):** PARTIAL — `Console.CancelKeyPress` handler set to `args.Cancel <- true` in Slash Edit arm prevents blueCode from exiting; vi handles its own SIGINT. Behavior validated by Plan 34-02 indirectly (no test for SIGINT — it's a manual-verification item).
- **SC-6 (Bench gate 7/7 PASS preserved):** Plan 34-02's responsibility.

This plan establishes the production wiring; Plan 34-02 validates and gates.
</success_criteria>

<output>
After completion, create `.planning/phases/34-edit-multi-line-input/34-01-SUMMARY.md` with the following frontmatter and body:

```yaml
---
phase: 34-edit-multi-line-input
plan: 01
status: complete
date: <YYYY-MM-DD>
subsystem: cli-repl
affects:
  - src/BlueCode.Cli/EditCommand.fs (NEW)
  - src/BlueCode.Cli/Repl.fs
  - src/BlueCode.Cli/Rendering.fs
  - src/BlueCode.Cli/BlueCode.Cli.fsproj
  - tests/BlueCode.Tests/RenderingTests.fs
  - tests/BlueCode.Tests/ReplTests.fs
tests:
  added: 0
  modified: 2
  deleted: 0
commits:
  - feat(34-01): add IEditorLauncher port + openEditorAsync (EditCommand.fs)
  - feat(34-01): wire /edit to EditCommand.openEditorAsync via handlePromptTurn refactor
  - test(34-01): adapt 2 existing tests for /edit live promotion
loc_delta:
  added: ~110
  removed: ~25
core_diff: empty
---
```

Body sections (recommended):
- **What shipped** — IEditorLauncher port, realEditorLauncher (TTY-inheriting ProcessStartInfo), openEditorAsync (tmpfile lifecycle), handlePromptTurn refactor, Slash Edit arm, renderHelp cleanup
- **Key decisions captured** — Fully-qualified BlueCode.Cli.EditCommand.* in Repl.fs (mirrors Phase 33-01 decision); content-based cancel (not exit-code-based); .md tmpfile extension for editor syntax highlighting; module-level ProcessExit registration with mutable currentTmpPath cell
- **Behavior unchanged for plan-mode** — Phase 33's 6 plan-gate ReplTests still pass; handlePromptTurn lift was strictly behavior-preserving
- **Open items handed to Plan 34-02** — mock-launcher behavior tests, ReplTests `/edit` integration tests, bench gate 7/7
- **Pitfalls dodged** — UseShellExecute=true (macOS opens in app bundle); CreateNoWindow (Windows-only); checking proc.ExitCode (`:q!` returns 0); `async {}` (Cli convention is `task {}`); `git add -A` (untracked .claude/ + localLLM/)
</output>
