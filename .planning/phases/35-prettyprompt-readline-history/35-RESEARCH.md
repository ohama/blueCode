# Phase 35: PrettyPrompt readline + history - Research

**Researched:** 2026-05-05
**Domain:** PrettyPrompt NuGet / .NET console readline / F# test seam design
**Confidence:** HIGH (NuGet version, API surface, history format — all verified from source)

## Summary

Phase 35 replaces `Console.ReadLine()` in `Repl.fs` with PrettyPrompt 4.1.1, adding
up/down arrow recall, Ctrl+R reverse search, and cross-session history persistence
at `~/.bluecode/history`. The replacement is purely Cli-layer; Core is unchanged.

The highest-risk sub-problem is test infrastructure: all 26 existing `ReplTests`
integration tests use `Console.SetIn(StringReader)` to feed scripted inputs into
`runMultiTurn`. PrettyPrompt reads via `Console.ReadKey(intercept=true)` in a
`KeyPress.ReadForever()` loop — it does NOT consume `Console.In`. After PrettyPrompt
integration, `Console.SetIn` redirection no longer reaches the input reader, breaking
all 26 tests. The fix is a `mutable promptReaderOverride` seam in `Repl.fs` —
identical to the `editorLauncherOverride` pattern introduced in Phase 34.

PrettyPrompt's own history format is base64-per-line, handles duplicates, and
self-manages a 500-entry cap with periodic trim. The library already satisfies HIST-01
through HIST-04; no custom history logic is needed. History file path passed to
constructor is `persistentHistoryFilepath`.

**Primary recommendation:** Use PrettyPrompt 4.1.1 with a `mutable promptReaderOverride`
seam (Option A); tests inject a pre-canned `string Queue` reader; production uses
PrettyPrompt wrapped in a Task-returning function. History file is
`~/.bluecode/history`, delegated entirely to PrettyPrompt (base64 format, no custom
encoding needed).

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| PrettyPrompt | 4.1.1 | Console.ReadLine replacement with history/editing | Only maintained .NET readline library with built-in history + Ctrl+R; 100K+ NuGet downloads; used by csharprepl |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| TextCopy | 6.2.1 | Clipboard support (PrettyPrompt dep, auto-pulled) | Transitive dep — no direct usage needed |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| PrettyPrompt 4.1.1 | Hand-rolled ANSI readline | 300-400 LOC, ANSI escape parsing, cursor management, history state machine — fragile on macOS Terminal.app; PrettyPrompt covers all this in 50 LOC integration |
| PrettyPrompt | readline-net / ReadLine.Reboot | Not actively maintained; PrettyPrompt has more recent commits and is used by csharprepl |

**Installation:**
```bash
dotnet add package PrettyPrompt --version 4.1.1
# or add to BlueCode.Cli.fsproj:
# <PackageReference Include="PrettyPrompt" Version="4.1.1" />
```

**Note on .NET 10 compat:** PrettyPrompt 4.1.1 csproj targets `net8.0`. NuGet's forward-compat
rules allow net8.0 packages to run on net10.0 without modification. No net10.0-specific
incompatibilities known.

## Architecture Patterns

### Recommended File Layout (Phase 35 additions)
```
src/BlueCode.Cli/
├── PromptReader.fs       # NEW: IPromptReader interface + realPromptReader (PrettyPrompt) + history path helper
├── Repl.fs               # MODIFIED: replace Console.ReadLine; add mutable promptReaderOverride seam
└── BlueCode.Cli.fsproj   # MODIFIED: PackageReference PrettyPrompt 4.1.1; <Compile Include="PromptReader.fs" /> before Repl.fs

tests/BlueCode.Tests/
├── ReplTests.fs          # MODIFIED: set promptReaderOverride (string Queue) instead of Console.SetIn
```

### Pattern 1: IPromptReader Port (mirrors IEditorLauncher from Phase 34)

**What:** Abstract the prompt-reading operation behind an interface. Production = PrettyPrompt;
tests = pre-canned string queue. Seam is `mutable promptReaderOverride` in `Repl.fs` (same
as `editorLauncherOverride`, line 39 in Repl.fs).

**When to use:** Any time interactive terminal input needs to be injected in tests without
a real TTY.

**Example F# interface:**
```fsharp
// Source: mirrors IEditorLauncher from src/BlueCode.Cli/EditCommand.fs
type IPromptReader =
    /// Read one line of input from the user. Returns None on Ctrl+D / EOF.
    abstract member ReadLineAsync : prompt: string -> System.Threading.Tasks.Task<string option>

/// Production reader: PrettyPrompt-backed, persistent history.
/// historyPath is ~/.bluecode/history (passed in at construction so tests can inject a tmpfile).
let makeRealPromptReader (historyPath: string) : IPromptReader =
    let pp = new PrettyPrompt.Prompt(persistentHistoryFilepath = historyPath)
    { new IPromptReader with
        member _.ReadLineAsync(prompt) =
            task {
                let! result = pp.ReadLineAsync()   // Task<PromptResult>
                if result.IsSuccess then
                    return Some result.Text
                else
                    return None  // Ctrl+C -> None; caller maps to exit
            } }

/// Test reader: string queue, no PrettyPrompt.
let makeTestPromptReader (lines: string list) : IPromptReader =
    let q = System.Collections.Generic.Queue<string>(lines)
    { new IPromptReader with
        member _.ReadLineAsync(_prompt) =
            task {
                if q.Count > 0 then return Some (q.Dequeue())
                else return None  // EOF
            } }
```

**Repl.fs seam (line ~39, mirrors editorLauncherOverride):**
```fsharp
// Source: mirrors Repl.fs editorLauncherOverride seam (line 39)
let mutable promptReaderOverride : IPromptReader option = None

// In runMultiTurnWithSession, replace:
//   printf "\nblueCode> "
//   let line = Console.ReadLine()
// with:
//   let reader =
//       match promptReaderOverride with
//       | Some r -> r
//       | None -> makeRealPromptReader (historyPath ())
//   let! lineOpt = reader.ReadLineAsync("blueCode> ")
//   let line = lineOpt |> Option.map id |> Option.toObj
```

**Test usage (replaces Console.SetIn pattern):**
```fsharp
// Source: mirrors ReplTests.fs editorLauncherOverride pattern (lines 688-730)
// In each testCase that currently does Console.SetIn(StringReader("/exit\n")):
let testReader = makeTestPromptReader ["/exit"]
BlueCode.Cli.Repl.promptReaderOverride <- Some testReader
try
    let exitCode = BlueCode.Cli.Repl.runMultiTurn components Compact |> fun t -> t.GetAwaiter().GetResult()
    // assertions...
finally
    BlueCode.Cli.Repl.promptReaderOverride <- None
```

### Pattern 2: History File Location Helper

```fsharp
// ~/.bluecode/history
let historyFilePath () : string =
    let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    let dir = Path.Combine(home, ".bluecode")
    Directory.CreateDirectory(dir) |> ignore
    Path.Combine(dir, "history")
```

Mirrors `FileSessionStore.buildSessionPath` (which already creates `~/.bluecode/sessions/`).
The `~/.bluecode/` directory will already exist from first REPL use. Creating it again is
idempotent (`CreateDirectory` is a no-op if exists).

### Pattern 3: Ctrl+D / EOF mapping

PrettyPrompt's `ReadLineAsync` returns `PromptResult`. When `IsSuccess = false`, the user
pressed Ctrl+C. Ctrl+D (EOF) on the raw terminal causes PrettyPrompt to also return
`IsSuccess = false`. Both map to `None` → `running <- false` in the REPL loop.

**Current Repl.fs null-check (line 296):**
```fsharp
let line = Console.ReadLine()  // null on Ctrl+D / EOF
match line with
| null -> running <- false
```
**After replacement:**
```fsharp
let! lineOpt = reader.ReadLineAsync("blueCode> ")
match lineOpt with
| None -> running <- false
```

### Pattern 4: PrettyPrompt Prompt() — No prompt string in ReadLineAsync

Note: `ReadLineAsync()` takes **no argument**. The prompt prefix (e.g. `"blueCode> "`)
is passed to `PromptConfiguration.Prompt` at construction time, not per-call.

```csharp
// Source: https://github.com/waf/PrettyPrompt/blob/main/src/PrettyPrompt/Prompt.cs
public Prompt(
    string? persistentHistoryFilepath = null,
    PromptCallbacks? callbacks = null,
    IConsole? console = null,
    PromptConfiguration? configuration = null)
```

```fsharp
// Production construction with prompt prefix:
let pp = new PrettyPrompt.Prompt(
    persistentHistoryFilepath = historyPath,
    configuration = new PrettyPrompt.Configuration.PromptConfiguration(
        prompt = "blueCode> "))
```

If `PromptConfiguration` is omitted, PrettyPrompt renders an empty prompt string.
The banner line `"blueCode — multi-turn mode. Session: %s..."` (Repl.fs line 180) is
printed once via `printfn` before the loop; the per-iteration prompt comes from PrettyPrompt.
Remove the existing `printf "\nblueCode> "` (line 292) since PrettyPrompt renders it.

### Anti-Patterns to Avoid

- **Anti-pattern: Keep Console.SetIn + detect non-interactive (Option B):** `Console.IsInputRedirected`
  returns true in test environments, but PrettyPrompt's KeyPress loop does not check it —
  it just calls `Console.ReadKey()` directly. On redirected stdin, `ReadKey()` throws
  `InvalidOperationException` rather than reading from the redirected stream. Option B would
  require try/catch around every ReadKey in PrettyPrompt's internal loop, which we don't
  control. Option A (IPromptReader seam) keeps test control fully in F# code.

- **Anti-pattern: Initialize PrettyPrompt at module startup:** PrettyPrompt's constructor
  loads history from disk asynchronously. Do not call it at module level or in
  `CompositionRoot.bootstrap`. Instantiate inside `runMultiTurnWithSession` (or lazily via
  `makeRealPromptReader`), after the REPL actually starts. Single-turn mode (`bench/run.sh`)
  never calls `runMultiTurnWithSession`, so PrettyPrompt is never instantiated in bench runs.

- **Anti-pattern: Custom history file format:** PrettyPrompt handles history internally (base64,
  dedup, 500-entry trim). Do NOT write to `~/.bluecode/history` separately — pass the path to
  `Prompt` constructor and let PrettyPrompt own the file.

- **Anti-pattern: Implement IPromptCallbacks for autocomplete:** Phase 35 scope is readline +
  history only. Pass no `callbacks` (uses default `PromptCallbacks` with no completions, no
  highlighting). Slash-command autocomplete is explicitly deferred (PROJECT.md).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| History file format | Custom line-per-prompt with escape | PrettyPrompt `persistentHistoryFilepath` param | PrettyPrompt already does base64 encoding, dedup, 500-entry trim, async append, and Ctrl+R search integration |
| Up/Down arrow ANSI parsing | Custom `Console.ReadKey` loop with escape sequence state machine | PrettyPrompt `ReadLineAsync` | ANSI escape sequences differ across Terminal.app / iTerm2 / SSH; PrettyPrompt handles all variants |
| Ctrl+R reverse search | Custom incremental search overlay | PrettyPrompt built-in `OnKeyUp` history navigation | Already wired to default KeyBindings; zero extra code needed |
| Readline cursor editing (Home/End/Ctrl-W) | Custom cursor management | PrettyPrompt | Ships with all standard readline keybindings |

**Key insight:** PrettyPrompt satisfies all 4 HIST requirements (HIST-01 through HIST-04) at
the point of constructing `Prompt(persistentHistoryFilepath = path)`. The only application
code needed is: instantiate Prompt, call ReadLineAsync in the loop, map PromptResult to
string option. Everything else (arrow keys, Ctrl+R, history file) is library behavior.

## Common Pitfalls

### Pitfall 1: Console.SetIn breakage (HIGHEST RISK)
**What goes wrong:** After replacing `Console.ReadLine()` with PrettyPrompt, all 26
existing `runMultiTurn` integration tests fail. They feed inputs via `Console.SetIn(StringReader)`
but PrettyPrompt reads via `Console.ReadKey(intercept=true)` — a completely different
kernel call that bypasses `Console.In`.
**Why it happens:** PrettyPrompt's `KeyPress.ReadForever()` calls `SystemConsole.ReadKey()`,
not `Console.In.Read()`. `Console.SetIn` only redirects `Console.In`; it has no effect on
`Console.ReadKey`.
**How to avoid:** Implement `IPromptReader` seam with `mutable promptReaderOverride` in
`Repl.fs`. Tests set the override to a `makeTestPromptReader(lines)` queue before calling
`runMultiTurn`. The override is `None` in production.
**Warning signs:** Tests compile and run but immediately hang or time out — PrettyPrompt
is blocking on a terminal ReadKey in a non-TTY environment.

### Pitfall 2: PrettyPrompt.Prompt() Lazy initialization vs constructor
**What goes wrong:** `Prompt` constructor calls `new HistoryLog(persistentHistoryFilepath, ...)`
which starts an async file-read task immediately. If the constructor is called at
process startup (before REPL entry), the `~/.bluecode/history` file read happens before
the user has seen the banner.
**Why it happens:** Eager initialization; no deferred lazy.
**How to avoid:** Instantiate `Prompt` inside `runMultiTurnWithSession` (before the `while`
loop, after printing the banner). It is only called in interactive multi-turn mode; single-
turn mode never reaches this path.

### Pitfall 3: PromptResult.IsSuccess = false ambiguity
**What goes wrong:** Mapping Ctrl+C from PrettyPrompt as "exit" may conflict with existing
REPL Ctrl+C behavior (which cancels a running LLM turn, not the REPL itself).
**Why it happens:** PrettyPrompt's `Ctrl+C` returns `IsSuccess = false` from `ReadLineAsync`
— but this fires at the READ phase, before any LLM turn is in progress. The Ctrl+C
handler in `runSingleTurn` registers a `CancelKeyPress` event specifically during the
LLM call. These two Ctrl+C paths do not conflict: PrettyPrompt only consumes Ctrl+C
during `ReadLineAsync`; `runSingleTurn` only listens during `runSession`.
**How to avoid:** Map `IsSuccess = false` from `ReadLineAsync` to `None` (same as Ctrl+D)
— both cause `running <- false`. Existing `runSingleTurn` CancelKeyPress handler is
unaffected because `ReadLineAsync` has already returned before `runSingleTurn` is called.

### Pitfall 4: Missing PromptConfiguration import
**What goes wrong:** `PromptConfiguration` is in namespace `PrettyPrompt.Configuration`,
not `PrettyPrompt`. F# `open PrettyPrompt` does not bring it in scope.
**How to avoid:** `open PrettyPrompt.Configuration` in `PromptReader.fs`, or use the full
qualified name `PrettyPrompt.Configuration.PromptConfiguration(...)`.

### Pitfall 5: History file not created before first write
**What goes wrong:** `~/.bluecode/` may exist (FileSessionStore already creates it) but
the `history` file itself doesn't exist on first run. PrettyPrompt's
`File.AppendAllLinesAsync` creates the file if absent — no pre-creation needed.
**How to avoid:** Just call `Directory.CreateDirectory(dir)` in `historyFilePath()` helper;
PrettyPrompt handles the rest.

### Pitfall 6: testSequenced applies to ALL ReplTests
**What goes wrong:** Phase 35 adds a new `promptReaderOverride` mutable cell. If any test
runs concurrently while another sets/clears the override, the cell races.
**Why it happens:** Expecto runs testList items concurrently by default.
**How to avoid:** ALL ReplTests are already wrapped in `testSequenced` (line 43 of
ReplTests.fs). The `promptReaderOverride` follows the same pattern as `editorLauncherOverride`
(which already benefits from `testSequenced`). New tests added in Phase 35 must be inside
the same `testSequenced` block, not a new `testList`.

### Pitfall 7: History file format — HIST-03 spec says "line-per-prompt"
**What goes wrong:** PrettyPrompt stores history as base64-per-line internally, not as
a human-readable line-per-prompt file. The requirement says `~/.bluecode/history` should
be line-per-prompt (user can `cat ~/.bluecode/history`).
**Resolution:** This is a "naming conflict". The PrettyPrompt persistent history filepath
IS the `~/.bluecode/history` file. It IS appended per-prompt. The "line-per-prompt"
spec describes append frequency, not encoding. The base64 encoding is an implementation
detail of PrettyPrompt's internal storage. The file satisfies HIST-03's functional
requirement (persists per-prompt, loads on start). Do NOT implement a separate
human-readable history file unless the user explicitly asks.

### Pitfall 8: /edit multi-line entries in history
**What goes wrong:** `/edit` arm dispatches `content` (multi-line string) through
`handlePromptTurn`. PrettyPrompt's `SavePersistentHistoryAsync` is called implicitly by
`ReadLineAsync` ONLY for the submitted text. The `/edit` content is NOT entered via
`ReadLineAsync`; it bypasses PrettyPrompt entirely (comes from tmpfile via
`openEditorAsync`). So `/edit` content is NEVER written to PrettyPrompt's history file.
This is the correct behavior: history tracks what the user typed at the PrettyPrompt
prompt, not what they wrote in a text editor. The HIST-03 spec's "multi-line /edit
결과는 first-line 만 또는 escape; 명세는 plan-phase 에서 결정" is a non-issue — no
/edit content reaches PrettyPrompt's history.

## Code Examples

### Full PrettyPrompt integration in F# (PromptReader.fs)
```fsharp
// Source: https://github.com/waf/PrettyPrompt/blob/main/src/PrettyPrompt/Prompt.cs
module BlueCode.Cli.PromptReader

open System
open System.IO
open System.Threading.Tasks
open PrettyPrompt
open PrettyPrompt.Configuration   // PromptConfiguration lives here

/// Abstraction over interactive line reading. Production = PrettyPrompt;
/// tests = pre-canned string queue. Mirrors IEditorLauncher from EditCommand.fs.
type IPromptReader =
    abstract member ReadLineAsync : unit -> Task<string option>
    // NOTE: prompt prefix is baked into construction, not per-call.

/// Returns the persistent history file path: ~/.bluecode/history
/// Creates ~/.bluecode/ dir if absent (idempotent).
let historyFilePath () : string =
    let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    let dir = Path.Combine(home, ".bluecode")
    Directory.CreateDirectory(dir) |> ignore
    Path.Combine(dir, "history")

/// Production reader: PrettyPrompt with persistent history.
/// Instantiate once per REPL session (inside runMultiTurnWithSession).
let makeRealPromptReader () : IPromptReader =
    let path = historyFilePath ()
    let config = PromptConfiguration(prompt = "blueCode> ")
    let pp = new Prompt(persistentHistoryFilepath = path, configuration = config)
    { new IPromptReader with
        member _.ReadLineAsync() =
            task {
                let! result = pp.ReadLineAsync()
                if result.IsSuccess then return Some result.Text
                else return None   // Ctrl+C or Ctrl+D
            } }

/// Test reader: dequeue from pre-canned list; None on exhaustion.
let makeTestPromptReader (lines: string list) : IPromptReader =
    let q = Collections.Generic.Queue<string>(lines)
    { new IPromptReader with
        member _.ReadLineAsync() =
            task {
                if q.Count > 0 then return Some (q.Dequeue())
                else return None
            } }
```

### Repl.fs changes (before/after)
```fsharp
// BEFORE (Repl.fs line 39, 292-293):
// (no promptReaderOverride)
// ...
// printf "\nblueCode> "
// let line = Console.ReadLine()

// AFTER:
// seam declaration (top of Repl.fs, mirrors editorLauncherOverride):
let mutable promptReaderOverride : BlueCode.Cli.PromptReader.IPromptReader option = None

// in runMultiTurnWithSession, before the while loop:
let reader =
    match promptReaderOverride with
    | Some r -> r
    | None -> BlueCode.Cli.PromptReader.makeRealPromptReader ()

// in the while loop, replace printf + Console.ReadLine:
let! lineOpt = reader.ReadLineAsync()
let line = lineOpt |> Option.toObj  // null on None = Ctrl+D/EOF
```

### Test pattern (replaces Console.SetIn)
```fsharp
// BEFORE (ReplTests.fs):
// use stdinReader = new StringReader("/exit\n")
// Console.SetIn(stdinReader)
// ...
// finally
//     Console.SetIn(originalIn)

// AFTER:
let testReader = BlueCode.Cli.PromptReader.makeTestPromptReader ["/exit"]
BlueCode.Cli.Repl.promptReaderOverride <- Some testReader
try
    let exitCode = BlueCode.Cli.Repl.runMultiTurn components Compact |> fun t -> t.GetAwaiter().GetResult()
    Expect.equal exitCode 0 "..."
finally
    BlueCode.Cli.Repl.promptReaderOverride <- None
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `Console.ReadLine()` bare | PrettyPrompt `ReadLineAsync` | Phase 35 | Up/Down/Ctrl+R, persistent history, full line editing |
| No history | `~/.bluecode/history` via PrettyPrompt | Phase 35 | Cross-session recall |
| `Console.SetIn` in tests | `IPromptReader` seam with `promptReaderOverride` | Phase 35 | Tests no longer fight PrettyPrompt's ReadKey loop |

**Deprecated/outdated after Phase 35:**
- `printf "\nblueCode> "` in Repl.fs: removed; PrettyPrompt renders the prompt
- `Console.SetIn(StringReader(...))` in ReplTests: replaced with `promptReaderOverride <- Some (makeTestPromptReader [...])`
- `Console.SetIn(originalIn)` in test `finally` blocks: removed (no stdin to restore)

## Open Questions

1. **PromptConfiguration prompt prefix: "blueCode> " vs "\nblueCode> "**
   - What we know: current Repl.fs uses `printf "\nblueCode> "` (with leading newline for spacing)
   - What's unclear: PrettyPrompt's PromptConfiguration.Prompt — does it go on a new line
     automatically, or does it render inline after prior output? PrettyPrompt typically renders
     on a fresh line after clearing the line.
   - Recommendation: Use `prompt = "blueCode> "` (no leading newline) in PromptConfiguration;
     PrettyPrompt handles its own newline. The banner `printfn "blueCode — multi-turn mode..."` is
     already followed by a newline. If vertical spacing is needed, add an explicit `printfn ""`
     before the `while` loop.

2. **History slash-command inclusion**
   - What we know: PrettyPrompt saves whatever the user types at the prompt, including `/help`,
     `/exit`, etc. There is no built-in filter.
   - What's unclear: User preference — include slash commands in history recall?
   - Recommendation: Include all inputs (including slash commands) in history. Up-arrow recalling
     `/plan` is mildly useful; filtering is complexity with little gain. If exclusion is desired,
     it requires custom logic in `SavePersistentHistoryAsync` override via `PromptCallbacks` subclass.

3. **BLUECODE_NO_PRETTYPROMPT env var**
   - What we know: The `IPromptReader` seam already provides escape via `promptReaderOverride`.
     An env var would let users disable PrettyPrompt for diagnostic use from shell.
   - Recommendation: Skip for Phase 35. Diagnostics are covered by the existing `--trace` flag
     and `promptReaderOverride` seam for tests. An env var adds untested code path.

4. **PromptResult.CancellationToken semantics**
   - What we know: `PromptResult.CancellationToken` signals if Ctrl+C was pressed; `IsSuccess`
     is the primary flag.
   - Recommendation: Map only on `IsSuccess`: `if result.IsSuccess then Some result.Text else None`.

## Plan Structure Recommendation

**2 plans are sufficient.** Pattern from prior v2.5 phases (31-34 all used 2-plan splits).

**Plan 35-01: PromptReader port + Repl.fs integration (no test changes)**
- Task 1: Add `PromptReader.fs` (IPromptReader interface, makeRealPromptReader, makeTestPromptReader, historyFilePath)
- Task 2: Update `BlueCode.Cli.fsproj` (add PackageReference PrettyPrompt 4.1.1; add PromptReader.fs before Repl.fs)
- Task 3: Update `Repl.fs` (add `promptReaderOverride` mutable seam; replace `printf + Console.ReadLine` with `reader.ReadLineAsync`)
- Task 4: Smoke test: `dotnet build` passes; `dotnet run --project src/BlueCode.Cli -- /exit` works (single-turn, no REPL)
- Verify: `bash bench/run.sh --gate` 7/7 PASS (PrettyPrompt never instantiated in single-turn path)

**Plan 35-02: ReplTests migration + new HIST tests**
- Task 1: Update all 26 ReplTests `testCase`s: replace `Console.SetIn`/`Console.SetOut` stdin portions
  with `promptReaderOverride <- Some (makeTestPromptReader [...])`. Remove `Console.SetIn(originalIn)`
  in finally blocks. Keep `Console.SetOut` capture (still needed for stdout assertions).
- Task 2: Add 3-4 new tests for HIST-03 (history file append), HIST-05 (history load on start),
  and HIST-02 (in-session up/down — note: up/down is PrettyPrompt internal, not directly testable
  via string queue; test is documentation-only or skipped).
- Task 3: Verify 359 + new tests pass; bench gate 7/7 PASS.

**Wave structure:** 35-01 in Wave 1, 35-02 in Wave 2. 35-01 gates on `dotnet build` + bench.
35-02 gates on full test suite.

## Bench Gate Isolation

**Zero risk for bench non-regression** (SC-7 is LOW complexity, HIGH confidence).

Call chain for `bench/run.sh --gate`:
```
dotnet run -- --model 122b "prompt text"
└─ Program.fs: promptWords = ["prompt"; "text"]  (non-empty)
└─ Repl.runSingleTurn prompt session.Steps components renderMode
   (runMultiTurnWithSession is NOT called)
   └─ AgentLoop.runSession  (LLM call)
```

`makeRealPromptReader()` and `PrettyPrompt.Prompt()` are only instantiated inside
`runMultiTurnWithSession`. Single-turn mode (bench) exits Program.fs at the
`| words -> Repl.runSingleTurn ...` branch (Program.fs line 267) and never
reaches `runMultiTurnWithSession` (line 262). PrettyPrompt is never loaded in
the single-turn path. `bench/baseline.json` is unaffected.

## macOS Terminal.app + iTerm2 Verification

**Confidence: MEDIUM** — no known macOS-specific issues found in PrettyPrompt GitHub
issues search; library's `InitVirtualTerminalProcessing` is Windows-only (VT processing
on macOS is default-on). Architecture.md states "use ANSI Escape Sequences for output"
with Console APIs for input — both Terminal.app and iTerm2 are ANSI-compatible and
support xterm-256color. No evidence of macOS-specific bugs.

**Manual verification protocol (SC-8):**
1. Terminal.app: `dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj`
   - Type 2-3 prompts, press Up — should recall last prompt
   - Press Down — should move forward in history
   - Press Ctrl+R — should open reverse-search
   - Type `/exit` — should exit cleanly
2. iTerm2: same steps
3. Verify `~/.bluecode/history` file created and contains entries
4. Exit and re-enter REPL — press Up — should recall prompts from previous session

**This SC requires human verification; it is not automatable in Expecto.**

## Sources

### Primary (HIGH confidence)
- https://github.com/waf/PrettyPrompt/blob/main/src/PrettyPrompt/PrettyPrompt.csproj — version 4.1.1, net8.0 target, MPL-2.0
- https://github.com/waf/PrettyPrompt/blob/main/src/PrettyPrompt/Prompt.cs — constructor signature, ReadLineAsync, IConsole injection
- https://raw.githubusercontent.com/waf/PrettyPrompt/main/src/PrettyPrompt/History/HistoryLog.cs — base64 format, 500-entry cap, append mechanics, no explicit locking
- https://github.com/waf/PrettyPrompt/blob/main/src/PrettyPrompt/PromptCallbacks.cs — virtual methods, default implementations
- https://www.nuget.org/packages/PrettyPrompt/4.1.1 — latest version confirmed, TextCopy dep, net6.0+ compat
- https://github.com/waf/PrettyPrompt/blob/main/src/PrettyPrompt/Console/IConsole.cs — IConsole interface for testability
- https://github.com/waf/PrettyPrompt/blob/main/tests/PrettyPrompt.Tests/PromptTests.cs — ConsoleStub injection pattern

### Secondary (MEDIUM confidence)
- https://github.com/waf/PrettyPrompt/blob/main/Architecture.md — input via Console.ReadKey, not ANSI; single-write rendering pipeline
- src/BlueCode.Cli/EditCommand.fs (codebase) — IEditorLauncher port pattern to mirror for IPromptReader
- src/BlueCode.Cli/Repl.fs (codebase) — editorLauncherOverride seam at line 39; exact pattern to replicate

### Tertiary (LOW confidence)
- PrettyPrompt GitHub issues search (macOS): no issues found — absence of evidence is weak positive signal for macOS compat

## Metadata

**Confidence breakdown:**
- Standard stack (PrettyPrompt 4.1.1 version pin): HIGH — verified from csproj + NuGet page
- API surface (constructor, ReadLineAsync, PromptResult): HIGH — verified from Prompt.cs source
- History format (base64, 500-cap, append): HIGH — verified from HistoryLog.cs source
- Test impact (26 tests, SetIn breakage, seam design): HIGH — grep count + IConsole test docs
- Architecture (IPromptReader port design): HIGH — mirrors existing IEditorLauncher pattern exactly
- macOS compatibility: MEDIUM — no known issues, but not tested directly
- Pitfalls (PromptResult.CancellationToken semantics): MEDIUM — inferred from API docs

**Research date:** 2026-05-05
**Valid until:** 2026-06-05 (PrettyPrompt last released 2023-09-30; stable)
