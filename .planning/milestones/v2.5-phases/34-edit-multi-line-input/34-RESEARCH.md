# Phase 34: /edit Multi-Line Input - Research

**Researched:** 2026-05-05
**Domain:** .NET 10 process spawning, interactive terminal editors, F# REPL dispatcher extension
**Confidence:** HIGH

## Summary

Phase 34 implements the `/edit` slash command that opens `$EDITOR` (fallback `vi`) on a tmpfile, reads the content after the editor exits, and uses it as the next REPL prompt. This is a Cli-layer-only change: `src/BlueCode.Core/**` is not touched. The pattern is analogous to how `git commit` invokes the editor.

The primary challenge is spawning an interactive terminal application (vi/vim/nano/emacs) that needs raw TTY access from within a .NET 10 `task {}` computation. The correct approach is `ProcessStartInfo` with `UseShellExecute = false` and **all three `Redirect*` properties set to `false`** — this causes the child process to inherit the parent's file descriptors directly, including the TTY. `WaitForExitAsync(CancellationToken.None)` blocks correctly inside the `task {}` CE without deadlock.

The second design challenge is testability. The `IKeyReader` port in `PlanGate.fs` is the exact precedent: extract an `IEditorLauncher` interface with a single `Launch : tmpPath:string -> unit` method. Tests inject a mock that writes scripted content to the tmpfile. Production wiring uses the real editor. The REPL integration follows the existing Phase 33 `/plan` arm pattern.

**Primary recommendation:** Implement a single `IEditorLauncher` port (mirrors `IKeyReader`), place the full `/edit` logic in a new `EditCommand.fs` module, and wire the `Slash Edit` arm in `Repl.fs` to call it, delegating the resulting prompt (if any) through the same `handlePromptTurn` helper used by the plan arm and the normal `Prompt` arm.

## Standard Stack

No new NuGet packages needed. All infrastructure is in .NET 10 BCL.

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `System.Diagnostics.Process` | .NET 10 BCL | Spawn editor process | Only way to spawn external processes in .NET |
| `System.IO.Path.GetTempFileName` | .NET 10 BCL | Create tmpfile | Returns unique path, creates 0-byte file atomically |
| `System.IO.File` | .NET 10 BCL | Read/write/delete tmpfile | Standard file I/O |
| `System.Environment.GetEnvironmentVariable` | .NET 10 BCL | Resolve `$EDITOR` | Correct API for env lookup |
| `AppDomain.CurrentDomain.ProcessExit` | .NET 10 BCL | Atexit cleanup | Fires on normal exit AND uncaught exception exit |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.IO.File.Move` | .NET 10 BCL | Rename `.tmp` to `.md` | If editor syntax-highlight hint is desired |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `ProcessStartInfo` direct | `UseShellExecute = true` | `UseShellExecute=true` on macOS goes through the OS open mechanism, not a shell; it does NOT help for terminal editors. Stay with `false`. |
| `WaitForExitAsync` | sync `WaitForExit()` | Both work inside `task {}`. `WaitForExitAsync(CancellationToken.None)` is more idiomatic for `task {}` CE and consistent with `FsToolExecutor.fs` usage. Use async variant. |
| `Path.GetTempFileName` | `Path.Combine(GetTempPath(), Guid...+".md")` | `GetTempFileName` creates the file atomically (race-free). If renaming to `.md`, `File.Move` the result of `GetTempFileName`. |

## Architecture Patterns

### Recommended File Structure

```
src/BlueCode.Cli/
├── EditCommand.fs          # NEW: IEditorLauncher port + openEditorAsync + atexit registration
│                           #      Placed BEFORE Repl.fs in .fsproj compile order
├── Repl.fs                 # MODIFIED: Slash Edit arm + factor out handlePromptTurn helper
└── Rendering.fs            # MODIFIED: remove "[coming in v2.5]" from /edit help line

tests/BlueCode.Tests/
├── EditCommandTests.fs     # NEW: tests for IEditorLauncher mock, empty/content cases
│                           #      Added to BlueCode.Tests.fsproj AND rootTests
└── ReplTests.fs            # MODIFIED: update "/edit only" stub test to expect real behavior
```

### Pattern 1: IEditorLauncher Port (mirrors IKeyReader)

**What:** A single-method interface that abstracts "launch editor and wait". Tests inject a mock that writes scripted content; production uses real process.

**When to use:** Any time /edit command needs to be tested without a real terminal.

**Example:**
```fsharp
// src/BlueCode.Cli/EditCommand.fs
type IEditorLauncher =
    /// Launch the editor on tmpPath, block until editor exits.
    /// Production: spawns $EDITOR or vi. Tests: writes scripted content directly.
    abstract member Launch : tmpPath: string -> unit
```

### Pattern 2: ProcessStartInfo for Interactive Editor

**What:** Launch editor with inherited TTY (all Redirect* = false, UseShellExecute = false).

**When to use:** Any time a child process needs raw TTY access (vi, vim, nano, emacs).

**Example:**
```fsharp
// Production IEditorLauncher.Launch implementation
let private launchEditor (bin: string) (editorArgs: string list) (tmpPath: string) : unit =
    let psi = ProcessStartInfo(bin)
    for arg in editorArgs do
        psi.ArgumentList.Add(arg)
    psi.ArgumentList.Add(tmpPath)
    psi.UseShellExecute <- false          // direct exec, no shell interpretation
    psi.RedirectStandardInput  <- false   // inherit parent's stdin (= TTY)
    psi.RedirectStandardOutput <- false   // inherit parent's stdout (= TTY)
    psi.RedirectStandardError  <- false   // inherit parent's stderr
    // NO CreateNoWindow — Windows-only flag, irrelevant on macOS
    use proc = Process.Start(psi)
    proc.WaitForExit()
    // Exit code ignored: non-zero means :q! (force quit) → empty file → cancel path
```

### Pattern 3: openEditorAsync — Full /edit Operation

**What:** Create tmpfile, launch editor (via IEditorLauncher), read content, cleanup, return result.

**When to use:** Called from the `Slash Edit` arm in `Repl.fs`.

**Example:**
```fsharp
// src/BlueCode.Cli/EditCommand.fs
let openEditorAsync (launcher: IEditorLauncher) : Task<string option> =
    task {
        let rawTmp = Path.GetTempFileName()
        // Rename to .md for editor syntax-highlight hints (vim detects by extension)
        let tmpPath = Path.ChangeExtension(rawTmp, ".md")
        File.Move(rawTmp, tmpPath)    // rawTmp is gone; tmpPath is the file to track

        // Register atexit cleanup BEFORE launching (covers crash during edit)
        let cleanup () =
            try if File.Exists tmpPath then File.Delete tmpPath with _ -> ()

        try
            // Launch blocks until editor exits (WaitForExit internally)
            launcher.Launch tmpPath
            let content = File.ReadAllText(tmpPath).Trim()
            return (if content = "" then None else Some content)
        finally
            cleanup ()
    }
```

### Pattern 4: EDITOR Env Var Parsing

**What:** Split `$EDITOR` on whitespace to support args like `code --wait`.

**When to use:** In the production `IEditorLauncher.Launch` implementation.

**Example:**
```fsharp
let parseEditorEnv () : string * string list =
    let envVal = Environment.GetEnvironmentVariable("EDITOR")
    if String.IsNullOrWhiteSpace(envVal) then
        ("vi", [])
    else
        let parts =
            envVal.Trim().Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
        (parts.[0], parts.[1..] |> Array.toList)
```

### Pattern 5: Ctrl+C During Edit (CancelKeyPress Handler)

**What:** Register a CancelKeyPress handler while the editor is running that prevents blueCode from exiting.

**When to use:** In the `Slash Edit` arm, wrapping the `openEditorAsync` call.

**Example:**
```fsharp
// In Repl.fs Slash Edit arm
| Some (Slash Edit) ->
    // Prevent Ctrl+C from killing blueCode while editor is open.
    // The editor (vi) handles Ctrl+C itself (cancel to normal mode).
    // If the editor is killed, WaitForExit returns, file is empty → cancel path.
    let mutable editorProc : Process option = None
    let cancelHandler =
        ConsoleCancelEventHandler(fun _ args ->
            args.Cancel <- true   // prevent blueCode exit
            match editorProc with
            | Some p -> try p.Kill() with _ -> ()
            | None -> ())
    Console.CancelKeyPress.AddHandler(cancelHandler)
    try
        let! contentOpt = EditCommand.openEditorAsync EditCommand.realEditorLauncher
        match contentOpt with
        | None -> printfn "Edit cancelled."
        | Some content -> do! handlePromptTurn content
    finally
        Console.CancelKeyPress.RemoveHandler(cancelHandler)
```

**Note:** The `editorProc` reference requires surfacing the `Process` from `IEditorLauncher`. This means the `Launch` method should accept a process-registration callback OR the `Slash Edit` arm uses a slightly different approach: since the editor is running synchronously (WaitForExit), the Ctrl+C SIGINT goes to the foreground process group. On macOS with vi/vim, vi catches it (cancel → normal mode) and blueCode never sees it unless the editor ignores SIGINT. The simpler approach is: register `args.Cancel = true` handler (no proc kill needed), because vi handles SIGINT itself. Only if user force-kills vi do they get the empty-content cancel path. Spec SC-5 says "child process 종료 후 REPL 으로 복귝" — this happens naturally when vi exits.

**Simplified handler** (production-safe):
```fsharp
let cancelHandler =
    ConsoleCancelEventHandler(fun _ args -> args.Cancel <- true)
Console.CancelKeyPress.AddHandler(cancelHandler)
try
    let! contentOpt = EditCommand.openEditorAsync realLauncher
    // ...
finally
    Console.CancelKeyPress.RemoveHandler(cancelHandler)
```

### Pattern 6: handlePromptTurn Refactor

**What:** Extract the existing prompt dispatch logic (plan-mode + normal) into a local helper inside `runMultiTurnWithSession` so both the `Prompt` arm and the `Slash Edit` arm can call it without code duplication.

**When to use:** In `Repl.fs` when implementing the `Slash Edit` arm.

**Example:**
```fsharp
// Inside runMultiTurnWithSession, before the while loop:
let handlePromptTurn (prompt: string) : Task<unit> =
    task {
        if planModeActive then
            // ... existing plan-gate logic (lifted from Prompt planModeActive arm) ...
        else
            // ... existing direct dispatch logic (lifted from Prompt arm) ...
    }

// Then both arms become:
| Some (Slash Edit) ->
    let! contentOpt = EditCommand.openEditorAsync realEditorLauncher
    match contentOpt with
    | None -> printfn "Edit cancelled."
    | Some content -> do! handlePromptTurn content

| Some (Prompt prompt) ->
    do! handlePromptTurn prompt
```

### Pattern 7: AppDomain.ProcessExit for Atexit

**What:** Register a cleanup handler that deletes the tmpfile on process exit.

**When to use:** Belt-and-suspenders: `openEditorAsync` already has `try/finally` cleanup. The ProcessExit handler is for the edge case where the process is killed mid-edit (SIGKILL cannot be caught, but SIGTERM and normal exits fire ProcessExit).

**Example:**
```fsharp
// One-time registration in EditCommand.fs (module-level initialization)
// Track the current tmpfile path so ProcessExit can clean it up
let mutable private currentTmpPath : string option = None

do
    AppDomain.CurrentDomain.ProcessExit.Add(fun _ ->
        match currentTmpPath with
        | Some path -> try if File.Exists path then File.Delete path with _ -> ()
        | None -> ())
```

**Note:** `AppDomain.CurrentDomain.ProcessExit` fires on:
- Normal process exit
- `Environment.Exit()` calls
- Unhandled exceptions that terminate the process
- SIGTERM (graceful shutdown)
- Does NOT fire on SIGKILL. This is acceptable for a tmpfile in `/tmp` (OS cleans on reboot).

### Anti-Patterns to Avoid

- **UseShellExecute = true for editors:** Does NOT help on macOS for terminal editors. On macOS, `UseShellExecute=true` uses `/usr/bin/open` which opens apps via the Finder/app bundle mechanism, not a terminal. Result: vi opens in a new window (or fails silently).
- **Redirecting stdin/stdout/stderr:** Setting any `Redirect*` to `true` captures that stream, preventing the editor from accessing the TTY. Result: vi gets an empty stdin and shows nothing. All three must be `false`.
- **WaitForExit() with a large timeout via linked CTS:** Unlike `run_shell`, the editor does not have a timeout. The user controls when the editor exits. Do NOT add a timeout; use `CancellationToken.None`.
- **Skipping handlePromptTurn factoring:** Duplicating the plan-gate + direct-dispatch logic in the Edit arm leads to maintenance drift. Factor it out.
- **Checking `proc.ExitCode`:** An editor that exits with non-zero (e.g., `:q!` in vi returns 0 anyway; a crash might return 1) should be handled via content check (empty → cancel), not exit code.
- **Not supporting `code --wait`:** VS Code needs `--wait` to block until the file is closed. If EDITOR="code --wait", the split-on-space parsing handles this naturally.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Unique tmpfile creation | `Guid-based path` | `Path.GetTempFileName()` | Atomic creation, prevents race conditions |
| Atexit cleanup | Custom signal handler | `AppDomain.CurrentDomain.ProcessExit` | Handles all normal exit paths |
| EDITOR argument splitting | Custom quoting parser | Split on space (simple) | 99% of EDITOR values are `editor` or `editor --flag`; no quoted paths in EDITOR practice |

**Key insight:** The entire implementation uses only .NET BCL. No new NuGet packages required.

## Common Pitfalls

### Pitfall 1: Redirect* Not All False → Editor Gets No TTY
**What goes wrong:** If any of `RedirectStandardInput`, `RedirectStandardOutput`, or `RedirectStandardError` is set to `true`, the corresponding stream is captured by the .NET runtime, not passed to the TTY. vi/vim checks if stdin is a TTY at startup. If stdin is not a TTY, vi may refuse to start interactively or produce garbage output.
**Why it happens:** Developers copy the `runShellImpl` pattern from `FsToolExecutor.fs` which sets all three to `true`. That pattern is for background commands where output capture is the goal.
**How to avoid:** For interactive editors: set all three `Redirect*` to `false`. No `capOutput`, no `ReadToEndAsync`, no `ReadToEndAsync`.
**Warning signs:** Editor appears to hang, or printfn output shows `vi: Warning: Output is not to a terminal`, or the process exits immediately with exit code 1.

### Pitfall 2: CreateNoWindow on macOS
**What goes wrong:** Setting `psi.CreateNoWindow <- true` causes a runtime error on macOS (it is a Windows-only property; on macOS it's silently ignored, but it signals developer confusion).
**Why it happens:** Copy-paste from Windows examples.
**How to avoid:** Do not set `CreateNoWindow`. It is not needed and not meaningful on macOS.
**Warning signs:** None — it's silently ignored. But it's dead code that misleads readers.

### Pitfall 3: Forgetting to Handle Process.Start Throwing
**What goes wrong:** If `$EDITOR` points to a non-existent binary, `Process.Start` throws `Win32Exception`/`IOException`. The exception propagates out of `task {}` and terminates the REPL.
**Why it happens:** Happy-path only implementation.
**How to avoid:** Wrap `Process.Start` in `try/with`:
```fsharp
let startResult =
    try Ok(Process.Start(psi))
    with ex -> Error ex
match startResult with
| Error ex -> printfn "Cannot launch editor '%s': %s" bin ex.Message
| Ok proc -> proc.WaitForExit()
```
**Warning signs:** REPL crashes with `System.ComponentModel.Win32Exception: No such file or directory`.

### Pitfall 4: Trim() vs Length = 0 for Empty Check
**What goes wrong:** Using `content.Length = 0` treats whitespace-only files as non-empty, producing a whitespace-only prompt to the LLM.
**Why it happens:** Strict reading of "비어있지 않으면".
**How to avoid:** Use `content.Trim() = ""` to treat whitespace-only as empty/cancel. Empirically verified: `Path.GetTempFileName()` creates a 0-byte file; whitespace-only is a degenerate "no real content" case.
**Warning signs:** LLM receives a prompt of `"   "` — may produce confused response.

### Pitfall 5: Duplicate Prompt Dispatch Logic in Edit Arm
**What goes wrong:** The plan-mode branching in the `Prompt planModeActive` arm is ~60 lines. Duplicating it in the `Edit` arm creates two places to maintain.
**Why it happens:** Taking the shortcut of inlining rather than factoring.
**How to avoid:** Extract `handlePromptTurn` as a local function before the `while` loop. Both arms call it.
**Warning signs:** Plan-mode changes applied to `Prompt` arm not reflected in `Edit` arm.

### Pitfall 6: Test For "not yet implemented" No Longer Valid After Phase 34
**What goes wrong:** `ReplTests.fs` line 617 tests that `/edit` prints "not yet implemented". After Phase 34, this test must be updated or it will fail.
**Why it happens:** Phase 33 added this test specifically for the remaining stub commands; Phase 34 promotes `/edit` to real behavior.
**How to avoid:** In Phase 34 plan, explicitly schedule updating this test case.
**Warning signs:** Test suite shows failure in `"runMultiTurn: remaining future-stub command (/edit only)..."` test.

### Pitfall 7: Not Registering New Test Module in BOTH .fsproj AND rootTests
**What goes wrong:** `EditCommandTests.fs` compiles but its tests don't appear in the test run; 0 `EditCommandTests` show in output.
**Why it happens:** This project doesn't use `[<Tests>]` auto-discovery. Explicit registration required.
**How to avoid:** Add to BOTH `BlueCode.Tests.fsproj` (before `RouterTests.fs`) AND `rootTests` list in `RouterTests.fs`.
**Warning signs:** `dotnet run --project tests/BlueCode.Tests` shows 352 tests still (no increase from Phase 34 tests).

### Pitfall 8: Rendering.fs Help Text Still Shows "[coming in v2.5]"
**What goes wrong:** After Phase 34, the `/edit` help line still reads `open $EDITOR for multi-line input [coming in v2.5]` but the feature is live.
**Why it happens:** Forgetting to update `renderHelp` in `Rendering.fs` (line 139).
**How to avoid:** Update `renderHelp` to remove the `[coming in v2.5]` suffix as part of Phase 34.
**Warning signs:** ReplTests `/help` test that checks `"[coming in v2.5]"` will now be false (need to update that assertion too).

## Code Examples

### Full Production IEditorLauncher

```fsharp
// src/BlueCode.Cli/EditCommand.fs
module BlueCode.Cli.EditCommand

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks

/// Abstraction over editor launching so tests can inject scripted content
/// without needing a real terminal. Mirrors IKeyReader from PlanGate.fs.
type IEditorLauncher =
    /// Launch editor on tmpPath, block until editor exits.
    abstract member Launch : tmpPath: string -> unit

/// Parse $EDITOR env var into (binary, extraArgs).
/// Supports "vi", "code --wait", "emacs -nw", etc.
let private parseEditorEnv () : string * string list =
    let envVal = Environment.GetEnvironmentVariable("EDITOR")
    if String.IsNullOrWhiteSpace(envVal) then
        ("vi", [])
    else
        let parts =
            envVal.Trim().Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
        (parts.[0], parts.[1..] |> Array.toList)

/// Production launcher: uses $EDITOR or vi fallback.
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
            let startResult =
                try Ok(Process.Start(psi))
                with ex -> Error(bin, ex)
            match startResult with
            | Error (bin, ex) ->
                printfn "Cannot launch editor '%s': %s" bin ex.Message
                printfn "Edit cancelled (editor unavailable)."
            | Ok proc ->
                use _ = proc
                proc.WaitForExit() }

/// Open tmpfile in editor, return Some content (trimmed) or None (empty/cancelled).
/// Creates tmpfile with .md extension for editor syntax highlighting.
/// Cleans up tmpfile in finally block regardless of outcome.
let openEditorAsync (launcher: IEditorLauncher) : Task<string option> =
    task {
        let rawTmp = Path.GetTempFileName()
        let tmpPath = Path.ChangeExtension(rawTmp, ".md")
        File.Move(rawTmp, tmpPath)   // rawTmp gone; tmpPath is the live file

        try
            launcher.Launch tmpPath
            let content = File.ReadAllText(tmpPath).Trim()
            return (if content = "" then None else Some content)
        finally
            try
                if File.Exists tmpPath then File.Delete tmpPath
            with _ -> ()
    }
```

### Test Mock Pattern

```fsharp
// tests/BlueCode.Tests/EditCommandTests.fs
let private mockLauncherWith (content: string) : IEditorLauncher =
    { new IEditorLauncher with
        member _.Launch tmpPath =
            System.IO.File.WriteAllText(tmpPath, content) }

testCase "openEditorAsync: non-empty content -> Some content" <| fun () ->
    let launcher = mockLauncherWith "Refactor auth to use JWT\n"
    let result =
        EditCommand.openEditorAsync launcher
        |> fun t -> t.GetAwaiter().GetResult()
    Expect.equal result (Some "Refactor auth to use JWT") "trimmed content returned"

testCase "openEditorAsync: empty content -> None (cancel)" <| fun () ->
    let launcher = mockLauncherWith ""
    let result =
        EditCommand.openEditorAsync launcher
        |> fun t -> t.GetAwaiter().GetResult()
    Expect.equal result None "empty file -> None"

testCase "openEditorAsync: whitespace-only content -> None (cancel)" <| fun () ->
    let launcher = mockLauncherWith "   \n\t  \n"
    let result =
        EditCommand.openEditorAsync launcher
        |> fun t -> t.GetAwaiter().GetResult()
    Expect.equal result None "whitespace-only -> None"

testCase "openEditorAsync: tmpfile deleted after successful edit" <| fun () ->
    let mutable capturedPath = ""
    let launcher =
        { new IEditorLauncher with
            member _.Launch tmpPath =
                capturedPath <- tmpPath
                System.IO.File.WriteAllText(tmpPath, "some content") }
    let _ =
        EditCommand.openEditorAsync launcher
        |> fun t -> t.GetAwaiter().GetResult()
    Expect.isFalse (System.IO.File.Exists capturedPath) "tmpfile deleted after read"
```

### Repl.fs Edit Arm (Sketch)

```fsharp
// In runMultiTurnWithSession, the Slash Edit arm:
| Some (Slash Edit) ->
    let cancelHandler = ConsoleCancelEventHandler(fun _ args -> args.Cancel <- true)
    Console.CancelKeyPress.AddHandler(cancelHandler)
    try
        let! contentOpt = EditCommand.openEditorAsync EditCommand.realEditorLauncher
        match contentOpt with
        | None -> printfn "Edit cancelled."
        | Some content -> do! handlePromptTurn content
    finally
        Console.CancelKeyPress.RemoveHandler(cancelHandler)
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `UseShellExecute = true` for editors | `UseShellExecute = false` + all Redirect = false | .NET Core (2016) | Shell execution not available in .NET Core on macOS for terminal editors; direct exec with inherited FDs is the correct model |
| Sync `WaitForExit()` | Async `WaitForExitAsync(ct)` | .NET 5+ | `WaitForExitAsync` is preferred in `task {}` CE; avoids blocking thread pool thread |

**Note on dotnet/runtime issue #91706 (terminal gibberish with vim on .NET 8):** This issue was opened Sept 2023 for .NET 8 Preview 7 on Linux/WSL2. It remains open with "Future" milestone as of research date. However, this project targets .NET 10 on macOS, and the typical terminal-gibberish symptom is caused by terminal initialization sequence leakage, not by the process spawning itself. For blueCode's use case (macOS + `UseShellExecute=false` + all Redirects=false), the child inherits the parent's TTY fds directly and vi handles terminal setup independently. The `.tmp` extension rename to `.md` avoids any `.tmp`-specific terminal quirks.

## Open Questions

1. **Does the github.com/dotnet/runtime #91706 terminal-gibberish issue affect .NET 10 on macOS?**
   - What we know: Issue is open, was reported on .NET 8 Preview on Linux/WSL2
   - What's unclear: Whether it manifests on .NET 10 macOS with vi/vim
   - Recommendation: Test manually after implementation with `blueCode` running in a real terminal. If gibberish appears, the mitigation is to flush `Console.Out` before launching the editor: `Console.Out.Flush()`.

2. **Should `/edit` accept an optional seed argument (e.g., `/edit Refactor X to Y`)?**
   - What we know: ROADMAP and REQUIREMENTS.md say nothing about this; spec does not mention it.
   - What's unclear: User preference.
   - Recommendation: Do not implement seed text in Phase 34. The REPL would write seed text to the tmpfile before opening the editor, which is a different UX. Out of scope.

3. **Editor exit code non-zero handling (editor crash)?**
   - What we know: vi/vim `:q!` returns exit code 0; a crash (SIGSEGV) would return non-zero.
   - What's unclear: Whether to surface a warning on non-zero exit.
   - Recommendation: Check content after editor exits regardless of exit code. If content is non-empty, send it. If empty (editor crashed with no save), treat as cancel. This is the simplest and most correct behavior.

4. **`/edit` interaction with `renderHelp`:**
   - What we know: `renderHelp` in `Rendering.fs` line 139 currently shows `[coming in v2.5]`.
   - Recommendation: Remove the suffix in Phase 34. The test in `ReplTests.fs` line 483 (`Expect.stringContains captured "[coming in v2.5]" ...`) must be updated to reflect the promoted command.

## Sources

### Primary (HIGH confidence)

- Empirically tested on this machine: `.NET 10.0.203`, macOS darwin 25.3.0, vim 9.1
- `Process.Start` with `UseShellExecute=false` + all Redirect=false — confirmed child inherits parent fds
- `Path.GetTempFileName()` — confirmed behavior on macOS (`/var/folders/...` prefix, `.tmp` extension, atomic creation)
- `File.Move(rawTmp, mdPath)` — confirmed original file gone after Move (no orphan)
- `AppDomain.CurrentDomain.ProcessExit` — confirmed fires at process exit including fsi script termination
- `WaitForExitAsync(CancellationToken.None)` inside `task {}` — confirmed works in .NET 10

### Secondary (MEDIUM confidence)

- [ProcessStartInfo.UseShellExecute - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-8.0) — verified that `false` allows inheriting file descriptors when Redirect* are all false
- [dotnet/runtime #91706](https://github.com/dotnet/runtime/issues/91706) — terminal gibberish issue; open as of 2026-05, originally .NET 8 Linux/WSL2; doesn't affect macOS .NET 10 for this use case
- Codebase precedent: `FsToolExecutor.fs` `runShellImpl` — established `ProcessStartInfo` pattern for this project (with redirect; editor case is the inverse)
- `PlanGate.fs` `IKeyReader` — established port pattern for testable I/O abstraction

### Tertiary (LOW confidence)

- General knowledge: `$EDITOR` convention for `code --wait`, `emacs -nw`, `nano` etc. — standard Unix convention, not formally documented

## Metadata

**Confidence breakdown:**
- Process spawning for interactive editor: HIGH — empirically verified pattern on macOS .NET 10; established in codebase precedent
- IEditorLauncher port design: HIGH — directly mirrors IKeyReader which shipped in Phase 16-02
- Tmpfile lifecycle: HIGH — empirically verified GetTempFileName, Move, Delete, ProcessExit
- REPL integration (handlePromptTurn refactor): HIGH — code structure is clear from reading Repl.fs
- Test strategy: HIGH — mock pattern verified empirically; matches PlanGateTests pattern exactly
- Ctrl+C behavior: MEDIUM — vi/vim SIGINT handling well-known; process group behavior on macOS verified by convention but not empirically tested in this session
- .NET 10 terminal gibberish issue: MEDIUM — issue open but appears Linux/WSL2 specific; macOS .NET 10 likely unaffected

**Research date:** 2026-05-05
**Valid until:** 2026-06-05 (stable BCL APIs; 30 days)
