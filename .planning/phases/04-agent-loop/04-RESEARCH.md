# Phase 4: Agent Loop - Research

**Researched:** 2026-04-22
**Domain:** F# recursive task {} agent loop, Serilog stderr logging, JSONL step log, Ctrl+C cancellation, ContextBuffer wiring
**Confidence:** HIGH

---

## Summary

Phase 4 wires Phase 2's `ILlmClient` and Phase 3's `IToolExecutor` into a single recursive `task {}` loop that drives blueCode's first real end-to-end Qwen task. The research confirms all Phase 4 mechanics are well-defined and fully implementable from existing codebase state. No NuGet additions beyond Serilog are required for core loop logic; Serilog 4.3.1 + Serilog.Sinks.Console 6.1.1 (already identified in STACK.md) cover OBS-02.

The agent loop belongs in `BlueCode.Core` (not Cli) because it is pure domain logic depending only on Ports interfaces, `ContextBuffer`, `Domain`, and `Router`. This placement preserves the ports-and-adapters pattern. The actual signatures of `ILlmClient.CompleteAsync` and `IToolExecutor.ExecuteAsync` already carry `CancellationToken` (confirmed from Ports.fs), so no Ports.fs amendment is needed for Ctrl+C propagation.

The Step record extension for OBS-04 (`startedAt`, `endedAt`, `durationMs`) is a purely additive field addition to Domain.fs with zero impact on existing pattern matches (the Step record uses `{ }` construction, so adding fields with no default is a compile-error at old construction sites — these must be located and updated, but there are very few: ContextBuffer.fs builds no Steps, ToolRegistry.fs none, Ports.fs none; only tests build Steps).

**Primary recommendation:** Implement `AgentLoop.fs` in `BlueCode.Core`, use `task {}` recursion with `loopN` parameter, thread `ContextBuffer` immutably, thread loop-guard `Map` as parameter, and write JSONL by opening `StreamWriter` once per session with `AutoFlush = true`. Serilog goes to stderr via `standardErrorFromLevel = LogEventLevel.Verbose`.

---

## Standard Stack

No new core libraries needed beyond what STACK.md already identified and what is already in the fsproj files. The only new NuGet references are Serilog packages.

### Core (already in BlueCode.Core.fsproj)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| FsToolkit.ErrorHandling | 5.2.0 | `taskResult {}` CE for Result threading | Already referenced; `taskResult { let! ... }` binds `Task<Result<'T,'E>>` cleanly; eliminates manual match chains |
| FSharp.SystemTextJson | 1.4.36 | JSONL serialization of Step records | Already in BlueCode.Cli.fsproj; `jsonOptions` singleton from Json.fs is reused |

### New (add to BlueCode.Cli.fsproj only)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Serilog | 4.3.1 | Static `Log.Logger` structured events | Already verified in STACK.md; no host required |
| Serilog.Sinks.Console | 6.1.1 | Console sink with `standardErrorFromLevel` | Required for stderr separation from Spectre stdout |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `StreamWriter` (manual) for JSONL | `Serilog.Sinks.File` | Serilog.Sinks.File adds a dependency and NDJSON format may not match Step record shape exactly; `StreamWriter` gives total control over line format and keeps it simple |
| `standardErrorFromLevel = LogEventLevel.Verbose` | Custom `TextWriter` sink | `standardErrorFromLevel` is the documented API; custom sink is overkill |

**Installation (BlueCode.Cli.fsproj additions):**
```bash
dotnet add src/BlueCode.Cli/BlueCode.Cli.fsproj package Serilog --version 4.3.1
dotnet add src/BlueCode.Cli/BlueCode.Cli.fsproj package Serilog.Sinks.Console --version 6.1.1
```

---

## Architecture Patterns

### Recommended Project Structure (Phase 4 additions)

```
src/
├── BlueCode.Core/
│   ├── Domain.fs            # AMEND: add startedAt, endedAt, durationMs to Step record
│   ├── Router.fs            # unchanged
│   ├── ContextBuffer.fs     # unchanged (used by AgentLoop)
│   ├── ToolRegistry.fs      # unchanged (NOT used by AgentLoop — see below)
│   ├── Ports.fs             # unchanged (ILlmClient + IToolExecutor already have CT)
│   └── AgentLoop.fs         # NEW: recursive loop, loop guard, JSON retry, ContextBuffer threading
└── BlueCode.Cli/
    ├── Adapters/
    │   ├── LlmWire.fs       # unchanged
    │   ├── Json.fs          # unchanged (parseLlmResponse reused for retry)
    │   ├── QwenHttpClient.fs # unchanged
    │   ├── BashSecurity.fs  # unchanged
    │   └── FsToolExecutor.fs # unchanged
    ├── JsonlSink.fs         # NEW: session StreamWriter, write/flush per step
    ├── LoggingSetup.fs      # NEW: Serilog Log.Logger configuration (one-liner)
    ├── Repl.fs              # NEW: single-turn runner, Ctrl+C setup, exit code
    ├── CompositionRoot.fs   # NEW: wire ILlmClient + IToolExecutor + Serilog + JsonlSink
    └── Program.fs           # AMEND: call CompositionRoot + Repl, not stub `0`
```

**Compile order BlueCode.Core.fsproj:**
```xml
<Compile Include="Domain.fs" />
<Compile Include="Router.fs" />
<Compile Include="ContextBuffer.fs" />
<Compile Include="ToolRegistry.fs" />
<Compile Include="Ports.fs" />
<Compile Include="AgentLoop.fs" />   <!-- NEW — last; depends on all above -->
```

**Compile order BlueCode.Cli.fsproj:**
```xml
<Compile Include="Adapters/LlmWire.fs" />
<Compile Include="Adapters/Json.fs" />
<Compile Include="Adapters/QwenHttpClient.fs" />
<Compile Include="Adapters/BashSecurity.fs" />
<Compile Include="Adapters/FsToolExecutor.fs" />
<Compile Include="JsonlSink.fs" />       <!-- NEW — before CompositionRoot -->
<Compile Include="LoggingSetup.fs" />    <!-- NEW — before CompositionRoot -->
<Compile Include="Repl.fs" />            <!-- NEW — before CompositionRoot -->
<Compile Include="CompositionRoot.fs" /> <!-- NEW — wires everything -->
<Compile Include="Program.fs" />         <!-- ALWAYS last -->
```

---

### Pattern 1: Recursive `task {}` Agent Loop with `loopN` Parameter

**What:** The loop is a `let rec` function inside a module. Every recursive call passes an incremented `loopN` as a plain `int` parameter. No mutable state anywhere in the loop body. `ContextBuffer` is passed by value and replaced on each recursion. The loop-guard `Map` is also threaded as a parameter.

**When to use:** The entire agent loop. `while` loops and mutable refs are forbidden in Core (async-ban check covers `async {}` but mutability is also a project convention).

**Exact signature:**

```fsharp
// In BlueCode.Core.AgentLoop

open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Core.ContextBuffer
open BlueCode.Core.Router
open FsToolkit.ErrorHandling
open System.Threading
open System.Threading.Tasks

type AgentConfig = {
    MaxLoops   : int           // = 5
    SystemPrompt : string
}

// Guard state: (actionName, inputHash) -> occurrenceCount
// Threaded immutably through recursive calls; reset between turns
type LoopGuardState = Map<string * int, int>

val runSession :
    config  : AgentConfig
    -> model   : Model              // fixed at turn start by Router, not re-evaluated per step
    -> client  : ILlmClient
    -> tools   : IToolExecutor
    -> input   : string
    -> ct      : CancellationToken
    -> Task<Result<AgentResult, AgentError>>

// Internal — not exported from module
val private runLoop :
    config    : AgentConfig
    -> model  : Model
    -> client : ILlmClient
    -> tools  : IToolExecutor
    -> ctx    : ContextBuffer
    -> guard  : LoopGuardState
    -> loopN  : int                 // 0-indexed; >= config.MaxLoops -> MaxLoopsExceeded
    -> steps  : Step list           // accumulated, prepended (newest first)
    -> ct     : CancellationToken
    -> Task<Result<AgentResult, AgentError>>
```

**Loop body sketch:**

```fsharp
let rec private runLoop config model client tools ctx guard loopN steps ct =
    task {
        // Guard: max loops
        if loopN >= config.MaxLoops then
            return Error MaxLoopsExceeded
        else
            // Build message list for this iteration
            let history = ContextBuffer.toList ctx |> List.rev  // chronological
            let messages = buildMessages config.SystemPrompt input history

            // Step start time (OBS-04)
            let startedAt = DateTimeOffset.UtcNow

            // Call LLM with JSON retry (LOOP-05)
            let! llmResult = callLlmWithRetry client messages model ct

            match llmResult with
            | Error e -> return Error e
            | Ok (FinalAnswer answer) ->
                // Record final step timing and return
                let endedAt = DateTimeOffset.UtcNow
                return Ok { FinalAnswer = answer; Steps = List.rev steps; LoopCount = loopN + 1; Model = model }
            | Ok (ToolCall (ToolName actionName, ToolInput inputMap)) ->
                // Dispatch tool name -> Tool DU (via ToolDispatch, not ToolRegistry)
                match dispatchTool actionName inputMap with
                | Error e -> return Error e
                | Ok tool ->
                    // Loop guard check (LOOP-04)
                    let inputHash = computeInputHash inputMap
                    let guardKey = (actionName, inputHash)
                    let count = guard |> Map.tryFind guardKey |> Option.defaultValue 0
                    if count >= 2 then
                        return Error (LoopGuardTripped actionName)
                    else
                        let guard' = guard |> Map.add guardKey (count + 1)

                        // Execute tool
                        let! toolResult = tools.ExecuteAsync tool ct
                        let endedAt = DateTimeOffset.UtcNow
                        let durationMs = int64 (endedAt - startedAt).TotalMilliseconds

                        // Build Step with OBS-04 timing fields
                        match toolResult with
                        | Error e -> return Error e
                        | Ok tr ->
                            let step = {
                                StepNumber = loopN + 1
                                Thought    = Thought "..."   // captured from LlmStep thought field
                                Action     = ToolCall (ToolName actionName, ToolInput inputMap)
                                ToolResult = Some tr
                                Status     = StepSuccess
                                ModelUsed  = model
                                StartedAt  = startedAt
                                EndedAt    = endedAt
                                DurationMs = durationMs
                            }
                            let ctx'   = ContextBuffer.add step ctx
                            let steps' = step :: steps
                            return! runLoop config model client tools ctx' guard' (loopN + 1) steps' ct
    }
```

**Key invariant:** `loopN` starts at 0. When `loopN >= config.MaxLoops` (i.e., >= 5), the check fires BEFORE the next LLM call. So the loop can execute at most 5 tool steps (loopN 0..4) and returns `MaxLoopsExceeded` when loopN reaches 5.

---

### Pattern 2: Step Record Extension for OBS-04

**What:** Domain.fs `Step` record gains three timing fields. These are populated by the agent loop at step start/end.

**Amendment to Domain.fs:**

```fsharp
// Current Step record (from Domain.fs as-read):
type Step = {
    StepNumber : int
    Thought    : Thought
    Action     : LlmOutput
    ToolResult : ToolResult option
    Status     : StepStatus
    ModelUsed  : Model
    // ADD (OBS-04):
    StartedAt  : DateTimeOffset
    EndedAt    : DateTimeOffset
    DurationMs : int64
}
```

**Where measured:** The agent loop captures `DateTimeOffset.UtcNow` immediately before `client.CompleteAsync` (step start) and immediately after `tools.ExecuteAsync` returns (step end). Duration = `(endedAt - startedAt).TotalMilliseconds |> int64`.

**Impact on existing code:** The Step record currently has 6 fields. Adding 3 more is a compile-time breaking change at any existing record construction site. The `{ }` record literal syntax requires all fields without defaults. Search and amend:
- `tests/BlueCode.Tests/*.fs` — any `{ StepNumber = ...; ... }` literals. Currently: tests likely use a helper or direct construction. Check and add dummy timing values.
- No other production code constructs `Step` records (confirmed: `ContextBuffer.fs` only stores them; `FsToolExecutor.fs` does not build Steps; `QwenHttpClient.fs` does not build Steps).

**JSONL serialization:** The `jsonOptions` singleton from `Json.fs` (FSharp.SystemTextJson) serializes records cleanly. `DateTimeOffset` serializes as ISO 8601 string by default with `System.Text.Json`. No converter needed.

---

### Pattern 3: Loop Guard (LOOP-04)

**What:** Track `(actionName: string, inputHash: int)` pairs seen in the current turn. On third occurrence, return `Error (LoopGuardTripped actionName)`.

**Data structure:**
```fsharp
type LoopGuardState = Map<string * int, int>
// key  = (actionName, inputHash)
// value = number of times this (action, input) pair has been seen
```

**Input hashing:**
```fsharp
// Use GetRawText() on the JsonElement from LlmStep.input.
// GetRawText() returns the original input bytes verbatim — it IS whitespace-sensitive.
// This is acceptable: the LLM producing identical JSON text means identical intent.
// Canonicalize by normalizing whitespace? Only if hash collisions cause false-positives.
// For v1, raw hash is sufficient.
let computeInputHash (inputMap: Map<string, string>) : int =
    // ToolInput from Phase 2 carries "_raw" as the single entry (raw JSON text)
    inputMap
    |> Map.tryFind "_raw"
    |> Option.defaultValue ""
    |> fun s -> s.GetHashCode()
    // Note: F# string.GetHashCode() is deterministic WITHIN A PROCESS RUN
    // (not across runs — process restarts reset the seed on .NET).
    // For a turn-scoped guard this is fine: the guard resets between turns.
```

**CRITICAL NOTE on ToolInput shape:** Phase 2's `toLlmOutput` stores the raw JSON text of the `input` object as `ToolInput (Map.ofList [("_raw", raw)])` where `raw = step.input.GetRawText()`. So `computeInputHash` should hash `inputMap |> Map.tryFind "_raw" |> Option.defaultValue ""`.

**Guard logic:**
```fsharp
let checkLoopGuard (guard: LoopGuardState) (actionName: string) (inputHash: int) : Result<LoopGuardState, AgentError> =
    let key = (actionName, inputHash)
    let count = guard |> Map.tryFind key |> Option.defaultValue 0
    if count >= 2 then
        Error (LoopGuardTripped actionName)
    else
        Ok (guard |> Map.add key (count + 1))
```

The guard resets by passing a fresh `Map.empty` to `runSession` at the start of each turn. It is NOT persisted between turns.

---

### Pattern 4: JSON Retry for LOOP-05

**What:** The LLM returns invalid JSON. Instead of immediately erroring, send one correction turn and retry. Two attempts total, then fail.

**Exact mechanism:**
```fsharp
let private callLlmWithRetry
    (client: ILlmClient)
    (messages: Message list)
    (model: Model)
    (ct: CancellationToken)
    : Task<Result<LlmOutput, AgentError>>
    =
    task {
        let! attempt1 = client.CompleteAsync messages model ct
        match attempt1 with
        | Ok output -> return Ok output
        | Error (InvalidJsonOutput raw) ->
            // Correction turn: append parse failure explanation to history
            let correctionMsg = {
                Role    = User
                Content = sprintf
                    "[PARSE ERROR] Your response was not valid JSON. Required format: \
                     {\"thought\":\"...\",\"action\":\"...\",\"input\":{...}}. \
                     Raw response received: %s\n\nPlease respond in strict JSON only."
                    (if raw.Length > 300 then raw.Substring(0, 300) + "..." else raw)
            }
            let messages2 = messages @ [correctionMsg]
            let! attempt2 = client.CompleteAsync messages2 model ct
            match attempt2 with
            | Ok output -> return Ok output
            | Error (InvalidJsonOutput _) ->
                return Error (InvalidJsonOutput raw)   // expose the original raw
            | Error other -> return Error other
        | Error other -> return Error other
    }
```

**Key design decisions:**
- The retry does NOT consume a main loop iteration (loopN is not incremented).
- The correction message is appended to `messages` for the retry call but is NOT added to `ContextBuffer` (not a real step).
- `SchemaViolation` is NOT retried — the JSON was extractable but the schema was wrong. This indicates a model structural problem, not a bare-parse failure. Only `InvalidJsonOutput` triggers retry.

---

### Pattern 5: JSONL Step Log (OBS-01)

**What:** Each completed step is serialized to one JSONL line. The file is opened once at session start and kept open for appending with auto-flush.

**File path logic:**
```fsharp
let buildSessionLogPath () : string =
    let home    = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    let dir     = Path.Combine(home, ".bluecode")
    Directory.CreateDirectory(dir) |> ignore  // no-op if exists
    // Colons replaced for filesystem safety on all OS
    let ts      = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ")
    Path.Combine(dir, sprintf "session_%s.jsonl" ts)
```

**StreamWriter approach (recommended over Serilog.Sinks.File):**
```fsharp
// In JsonlSink.fs (BlueCode.Cli)
type JsonlSink(path: string) =
    let writer = new StreamWriter(path, append = true, encoding = System.Text.Encoding.UTF8)
    do writer.AutoFlush <- true   // CRITICAL: crash-safety requires flush after every write

    member _.WriteStep (step: Step) =
        let line = JsonSerializer.Serialize(step, jsonOptions)
        writer.WriteLine(line)

    interface IDisposable with
        member _.Dispose() = writer.Dispose()
```

**Usage pattern (in CompositionRoot or Repl):**
```fsharp
use jsonlSink = new JsonlSink(buildSessionLogPath())
// Pass jsonlSink.WriteStep as a callback into runSession, or write steps in Repl after each turn
```

**Key design decision:** Write steps AFTER each step completes (not at start). Each line is the complete Step record including timing. `AutoFlush = true` means the file is readable immediately after each write even if the process crashes mid-session.

**Contract:** This is post-mortem only. The JSONL file is never read by blueCode for session resume in v1.

---

### Pattern 6: Serilog OBS-02 — stderr Setup

**What:** Serilog writes to stderr so Spectre.Console output on stdout is not interleaved with log lines.

**Exact configuration:**
```fsharp
// In LoggingSetup.fs (BlueCode.Cli)
open Serilog
open Serilog.Events

let configure () =
    Log.Logger <-
        LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                standardErrorFromLevel = Nullable LogEventLevel.Verbose,
                outputTemplate = "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger()
```

`standardErrorFromLevel = Nullable LogEventLevel.Verbose` sends ALL log levels to stderr. The `Nullable<LogEventLevel>` wrapping is required because `standardErrorFromLevel` is typed `LogEventLevel?` (nullable value type) in the sink's method signature.

**CRITICAL: Stream separation rule.** Spectre.Console writes to stdout via `AnsiConsole` (default). Serilog writes to stderr. The golden rule: **never call `Log.Information(...)` while a Spectre spinner is active**. Log calls before `withSpinner` starts or after it completes. During the spinner, only Spectre touches stdout; Serilog calls during the spinner go to stderr and are safe.

**Logger disposal:**
```fsharp
// In Program.fs, after the main task completes:
Log.CloseAndFlush()
```

**Usage in AgentLoop context (from Cli layer, not Core):** The agent loop itself does not call `Log.Information`. The Repl layer or CompositionRoot logs step summaries after `runSession` returns or after each step if using a step callback. This keeps AgentLoop.fs pure of Serilog dependency (preserving it as a Core module with no Serilog ref).

---

### Pattern 7: Ctrl+C / OperationCanceledException (LOOP-07)

**What:** `Console.CancelKeyPress` is wired to a `CancellationTokenSource` in `Program.fs`. The token is propagated into `runSession`. Any `OperationCanceledException` caught at the top level prints a one-line summary and exits 130.

**Setup (in Repl.fs or Program.fs):**
```fsharp
use cts = new CancellationTokenSource()
Console.CancelKeyPress.Add(fun args ->
    args.Cancel <- true      // Suppress immediate process kill — allow graceful shutdown
    cts.Cancel())
```

**Catching in Repl.fs top level:**
```fsharp
let! result = AgentLoop.runSession config model client tools input cts.Token
match result with
| Ok r    -> renderResult r; return 0
| Error UserCancelled ->
    AnsiConsole.MarkupLine("[yellow]> Cancelled.[/]")
    return 130
| Error MaxLoopsExceeded ->
    AnsiConsole.MarkupLine("[red]> Max loops exceeded (5 steps).[/]")
    return 1
| Error e ->
    AnsiConsole.MarkupLine(sprintf "[red]> Error: %A[/]" e)
    return 1
```

**OperationCanceledException vs UserCancelled:** The adapters (`QwenHttpClient`, `FsToolExecutor`) already convert `OperationCanceledException` with the caller's token to `Error UserCancelled`. So `OperationCanceledException` should never escape `runSession`. Wrap the top-level call defensively anyway:
```fsharp
task {
    try
        return! AgentLoop.runSession config model client tools input cts.Token
    with
    | :? OperationCanceledException ->
        return Error UserCancelled  // fallback — shouldn't reach here
}
```

**Exit code 130:** SIGINT conventional exit code (128 + 2). Standard for Ctrl+C in Unix tools.

---

### Pattern 8: CompositionRoot.fs Wiring

**What:** A module-level function that creates all concrete instances and returns a wired record or a set of functions. No DI container.

```fsharp
// In CompositionRoot.fs
module BlueCode.Cli.CompositionRoot

open BlueCode.Core
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Core.AgentLoop

type AppComponents = {
    LlmClient   : ILlmClient
    ToolExecutor: IToolExecutor
    JsonlSink   : JsonlSink
    Config      : AgentConfig
}

let bootstrap (projectRoot: string) : AppComponents =
    let logPath = buildSessionLogPath()   // from JsonlSink module
    {
        LlmClient    = QwenHttpClient.create()
        ToolExecutor = FsToolExecutor.create projectRoot
        JsonlSink    = new JsonlSink(logPath)
        Config       = {
            MaxLoops     = 5
            SystemPrompt = systemPromptText  // defined in AgentLoop or a constants module
        }
    }
```

**Program.fs integration (minimal, no Argu yet — Phase 5):**
```fsharp
[<EntryPoint>]
let main argv =
    LoggingSetup.configure()
    let projectRoot = Directory.GetCurrentDirectory()
    use components  = components  // IDisposable if AppComponents wraps JsonlSink
    let prompt =
        if argv.Length > 0 then String.concat " " argv
        else
            eprintfn "Usage: blueCode \"<prompt>\""
            exit 1
    let exitCode =
        (Repl.runSingleTurn prompt components)
            .GetAwaiter().GetResult()
    Log.CloseAndFlush()
    exitCode
```

---

### Pattern 9: Tool Dispatch (AgentLoop vs ToolRegistry)

**Critical finding:** `ToolRegistry.fs` currently is an EMPTY stub (Phase 1 placeholder). The agent loop does NOT use `ToolRegistry` for dispatching. Instead, AgentLoop.fs contains its own `dispatchTool` function that maps the LLM's `action` string + raw `_raw` JSON input to a `Tool` DU value.

This function lives in `AgentLoop.fs` (Core) and parses the `ToolInput` map:

```fsharp
// Dispatch: LLM action string + raw input JSON -> Tool DU
let private dispatchTool (actionName: string) (inputMap: Map<string, string>) : Result<Tool, AgentError> =
    let raw = inputMap |> Map.tryFind "_raw" |> Option.defaultValue "{}"
    try
        use doc = System.Text.Json.JsonDocument.Parse(raw)
        let root = doc.RootElement
        let getStr key =
            match root.TryGetProperty(key) with
            | true, el when el.ValueKind = System.Text.Json.JsonValueKind.String -> Ok (el.GetString())
            | _ -> Error (SchemaViolation (sprintf "Tool '%s' input missing required string field '%s'" actionName key))
        let getInt key defaultVal =
            match root.TryGetProperty(key) with
            | true, el when el.ValueKind = System.Text.Json.JsonValueKind.Number ->
                Ok (el.GetInt32())
            | _ -> Ok defaultVal
        match actionName with
        | "read_file" ->
            result {
                let! path  = getStr "path"
                let startL = result { let! s = getInt "start_line" 0 in return s }
                let endL   = result { let! e = getInt "end_line" 0 in return e }
                // Build lineRange option
                let lineRange =
                    match startL, endL with
                    | Ok s, Ok e when s > 0 && e >= s -> Some (s, e)
                    | _ -> None
                return ReadFile (FilePath path, lineRange)
            }
        | "write_file" ->
            result {
                let! path    = getStr "path"
                let! content = getStr "content"
                return WriteFile (FilePath path, content)
            }
        | "list_dir" ->
            result {
                let! path  = getStr "path"
                let depth  = result { return! getInt "depth" 1 }
                let depthOpt = depth |> Result.toOption
                return ListDir (FilePath path, depthOpt)
            }
        | "run_shell" ->
            result {
                let! cmd     = getStr "command"
                let! timeout = getInt "timeout_ms" 30000
                return RunShell (Command cmd, Domain.Timeout timeout)
            }
        | other ->
            Error (UnknownTool (ToolName other))
    with ex ->
        Error (SchemaViolation (sprintf "Tool input parse failed for '%s': %s" actionName ex.Message))
```

Note: `result { }` CE (not `taskResult { }`) is used here since this is a pure synchronous function.

---

### Pattern 10: Context-to-Messages Serialization

**What:** `ContextBuffer` holds the last N `Step` records. The agent loop converts them to a `Message list` for the LLM call. This conversion is done once per loop iteration, just before `client.CompleteAsync`.

**`ContextBuffer.toList ctx`** returns steps in most-recent-first order. Reverse to get chronological for message construction.

```fsharp
let buildMessages (systemPrompt: string) (userInput: string) (recentSteps: Step list) : Message list =
    let systemMsg = { Role = System; Content = systemPrompt }
    let userMsg   = { Role = User;   Content = userInput }
    // Convert steps to assistant + observation message pairs
    let stepMsgs =
        recentSteps
        |> List.collect (fun step ->
            let assistantContent =
                match step.Action with
                | ToolCall (ToolName n, ToolInput m) ->
                    let raw = m |> Map.tryFind "_raw" |> Option.defaultValue "{}"
                    sprintf "{\"thought\":\"%s\",\"action\":\"%s\",\"input\":%s}"
                        (let (Thought t) = step.Thought in t)
                        n raw
                | FinalAnswer ans ->
                    sprintf "{\"thought\":\"%s\",\"action\":\"final\",\"input\":{\"answer\":\"%s\"}}"
                        (let (Thought t) = step.Thought in t)
                        ans
            let observationContent =
                match step.ToolResult with
                | None -> "[OBSERVATION]\nFinal answer produced."
                | Some (Success output)           -> sprintf "[OBSERVATION]\n%s" output
                | Some (Failure (code, stderr))   -> sprintf "[TOOL ERROR]\nExit code: %d\nStderr: %s" code stderr
                | Some (SecurityDenied reason)    -> sprintf "[TOOL DENIED]\n%s" reason
                | Some (PathEscapeBlocked path)   -> sprintf "[PATH BLOCKED]\nAttempted: %s" path
                | Some (ToolResult.Timeout secs)  -> sprintf "[TIMEOUT]\nShell timed out after %d seconds" secs
            [ { Role = Assistant; Content = assistantContent }
              { Role = User;      Content = observationContent } ])
    systemMsg :: userMsg :: stepMsgs
```

---

### Pattern 11: Single-Turn Repl.fs

**What:** Phase 4 implements single-turn only. No interactive REPL loop (that's Phase 5, CLI-02).

```fsharp
// In Repl.fs
module BlueCode.Cli.Repl

let runSingleTurn (prompt: string) (components: AppComponents) : Task<int> =
    task {
        use cts = new CancellationTokenSource()
        Console.CancelKeyPress.Add(fun args ->
            args.Cancel <- true
            cts.Cancel())

        let model = Router.classifyIntent prompt |> Router.intentToModel

        try
            let! result = AgentLoop.runSession
                              components.Config
                              model
                              components.LlmClient
                              components.ToolExecutor
                              prompt
                              cts.Token

            match result with
            | Ok agentResult ->
                // Write all steps to JSONL
                for step in agentResult.Steps do
                    components.JsonlSink.WriteStep step
                // Render final answer
                AnsiConsole.MarkupLine(sprintf "[green]%s[/]" (Markup.Escape agentResult.FinalAnswer))
                return 0
            | Error UserCancelled ->
                AnsiConsole.MarkupLine("[yellow]Cancelled.[/]")
                return 130
            | Error MaxLoopsExceeded ->
                AnsiConsole.MarkupLine("[red]Max loops exceeded (5 steps with no final answer).[/]")
                return 1
            | Error (LoopGuardTripped action) ->
                AnsiConsole.MarkupLine(sprintf "[red]Loop guard: action '%s' called with same arguments 3 times. Aborting.[/]" action)
                return 1
            | Error e ->
                AnsiConsole.MarkupLine(sprintf "[red]Error: %A[/]" e)
                return 1
        with
        | :? OperationCanceledException ->
            AnsiConsole.MarkupLine("[yellow]Cancelled (uncaught OCE).[/]")
            return 130
    }
```

**JSONL write timing:** Steps are written to JSONL after the full turn completes. This is simpler than a per-step callback but means JSONL is not updated during a running turn. For crash-safety improvement, pass a `writeStep` callback into `runSession` so it writes immediately after each step. Either approach is acceptable for v1 — recommend the callback approach for SC6 (readable after process exits).

---

### Anti-Patterns to Avoid

- **ToolRegistry dispatch in agent loop:** `ToolRegistry` is an empty stub and stays that way. Do NOT try to wire it. AgentLoop has its own `dispatchTool` that maps action strings to Tool DU values directly.
- **Serilog calls inside `withSpinner`:** Spinner runs on a background task writing to Spectre (stdout). Log calls in the same scope would interleave stderr with the spinner line. Log BEFORE or AFTER `withSpinner`.
- **`let mutable` loop counter in agent loop:** Phase 4 forbids this. `loopN` is ALWAYS a function parameter.
- **`GetHashCode()` across process restarts:** Loop guard uses `string.GetHashCode()` which is non-deterministic across restarts. This is intentional — the guard only lives within a single turn within a single process run.
- **Adding `AgentLoop.fs` to BlueCode.Cli:** The loop is pure domain logic. It belongs in `BlueCode.Core`.
- **Serilog in BlueCode.Core:** `AgentLoop.fs` must NOT reference Serilog (keeps Core as a pure library). Logging of step events happens in the Cli layer (Repl.fs/CompositionRoot.fs).
- **Calling `ContextBuffer.toMessages` (if it existed):** `ContextBuffer` has no `toMessages` function. It has `toList`. The `buildMessages` logic belongs in `AgentLoop.fs` (or a helper called by it).

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Result threading in async | Manual `match` on every `Task<Result<_,_>>` | `taskResult {}` CE from FsToolkit.ErrorHandling | Already in fsproj; eliminates 3 lines per binding |
| Stderr log stream separation | Custom `TextWriter` sink or write to `Console.Error` directly | `Serilog.Sinks.Console` with `standardErrorFromLevel = Nullable LogEventLevel.Verbose` | `standardErrorFromLevel` is the documented, supported API; auto-handles theme, formatting |
| JSONL serialization | Custom JSON string builder | `JsonSerializer.Serialize(step, jsonOptions)` | `jsonOptions` singleton already handles DateTimeOffset, DU fieldless tags, records correctly |
| Path for session JSONL | Hard-coded path | `Environment.SpecialFolder.UserProfile` + `.bluecode/session_<ts>.jsonl` | Cross-user safe; matches requirement OBS-01 |
| Loop guard storage | Mutable dictionary | Immutable `Map<(string * int), int>` threaded as parameter | Keeps Core pure; no mutable state; reset-between-turns for free |
| Ctrl+C handling | SIGINT handler via Mono.Unix | `Console.CancelKeyPress` + `CancellationTokenSource` | Standard .NET API; works on macOS and Linux |

**Key insight:** All the "hard" problems in Phase 4 have been pre-solved by Phase 1-3 design decisions. The loop itself is about 100 lines of F# that threads existing types through existing interfaces.

---

## Common Pitfalls

### Pitfall 1: `Step` Record Construction Breaks at Existing Test Sites
**What goes wrong:** Adding `StartedAt`, `EndedAt`, `DurationMs` to `Step` breaks every existing `{ StepNumber = ...; Thought = ... }` record literal at compile time.
**Why it happens:** F# record construction requires all fields.
**How to avoid:** Before adding fields to Domain.fs, grep for `StepNumber` in test files and add dummy timing values. Search: `grep -r "StepNumber" tests/`. Use `DateTimeOffset.MinValue` and `0L` as placeholder values in tests that don't care about timing.
**Warning signs:** Build fails with `FS0764: The field 'StartedAt' was not given a value`.

### Pitfall 2: `OperationCanceledException` Escaping the Loop
**What goes wrong:** If `client.CompleteAsync` returns a cancelled token but the catch in `QwenHttpClient` only catches tokens matching `ct` exactly, and a different token fires (e.g., HttpClient internal timeout), `OperationCanceledException` could escape `runSession` as an unhandled exception rather than `Error UserCancelled`.
**Why it happens:** `QwenHttpClient.postAsync` distinguishes `ex.CancellationToken = ct` (user cancel) from other `TaskCanceledException` (timeout). The timeout path returns `Error (LlmUnreachable ...)`, not `Error UserCancelled`, which is correct — but any code path NOT in `postAsync` could still throw. Wrap `runSession` call defensively.
**How to avoid:** Add `try ... with :? OperationCanceledException -> Error UserCancelled` around the outer `runSession` call in Repl.fs.

### Pitfall 3: JSONL `DateTimeOffset` Serialization Format
**What goes wrong:** `System.Text.Json` serializes `DateTimeOffset` as `"2026-04-22T12:34:56+00:00"` by default. The field is a string, not a number. If a JSONL consumer expects epoch milliseconds, it will fail.
**Why it happens:** Default STJ behavior for `DateTimeOffset`.
**How to avoid:** Document that JSONL uses ISO 8601 string format. If epoch ms is needed, add `[<JsonPropertyName("startedAtMs")>]` and store `startedAt.ToUnixTimeMilliseconds()` separately. For v1, ISO 8601 is fine.

### Pitfall 4: Serilog Not Initialized Before First Log Call
**What goes wrong:** If `Log.Information(...)` is called before `LoggingSetup.configure()` runs, Serilog silently discards the event (or writes to the default no-op logger).
**Why it happens:** Serilog uses a static `Log.Logger` that defaults to `Logger.None`.
**How to avoid:** Call `LoggingSetup.configure()` as the FIRST line of `main`, before `bootstrap` or any adapter creation.

### Pitfall 5: `withSpinner` + Log Interleave
**What goes wrong:** Spectre's status spinner redraws the terminal line. If Serilog emits to stderr during the spinner, the terminal shows a mix of spinner frames and log lines, which looks broken.
**Why it happens:** stdout and stderr are separate streams but the terminal renders them to the same display.
**How to avoid:** Call `Log.Information` BEFORE `withSpinner` starts (e.g., "Step N: calling LLM...") and AFTER it completes (e.g., "Step N: LLM returned"). Never inside the spinner lambda. This is a convention to enforce in code review.

### Pitfall 6: Loop Guard Hash Collisions
**What goes wrong:** Two different tool inputs with different JSON content but the same `GetHashCode()` result block the loop guard incorrectly.
**Why it happens:** F# `string.GetHashCode()` has collisions like any hash function.
**How to avoid:** For a turn with max 5 steps, the collision probability is negligible. Accept the rare false positive. If it becomes a problem, switch to SHA256 substring, but this is over-engineering for v1.

### Pitfall 7: `AutoFlush = false` on JSONL StreamWriter
**What goes wrong:** If the process crashes between writes and `AutoFlush = false`, buffered step records are lost — exactly the crash-recovery scenario the JSONL log is meant to serve.
**Why it happens:** `StreamWriter` defaults to `AutoFlush = false` for performance.
**How to avoid:** Always set `writer.AutoFlush <- true` immediately after creating the `StreamWriter`. This is a one-liner but critical.

---

## Code Examples

### Verified: Serilog stderr-only configuration
```fsharp
// Source: github.com/serilog/serilog-sinks-console ConsoleLoggerConfigurationExtensions.cs
// standardErrorFromLevel = Verbose sends ALL events to stderr
open Serilog
open Serilog.Events

Log.Logger <-
    LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console(
            standardErrorFromLevel = System.Nullable<LogEventLevel>(LogEventLevel.Verbose),
            outputTemplate = "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger()
```

### Verified: CancelKeyPress -> CancellationTokenSource
```fsharp
// Source: .NET Console.CancelKeyPress docs + meziantou.net CancellationToken guide
use cts = new CancellationTokenSource()
Console.CancelKeyPress.Add(fun args ->
    args.Cancel <- true   // REQUIRED: prevents immediate process kill
    cts.Cancel())
// Pass cts.Token to all async operations
```

### Verified: JsonSerializer.Serialize with DateTimeOffset
```fsharp
// Source: System.Text.Json inbox on net10.0; DateTimeOffset serializes as ISO 8601 string
let line = JsonSerializer.Serialize(step, jsonOptions)
// Output: {"StepNumber":1,"Thought":{"Case":"Thought","Fields":["..."]},...,"StartedAt":"2026-04-22T12:34:56+00:00",...}
// Note: Thought is a single-case DU — needs WithUnionUnwrapFieldlessTags or similar if bare string preferred
// The jsonOptions from Json.fs already has JsonFSharpConverter registered with UnwrapFieldlessTags
```

### Verified: taskResult {} CE binding pattern (FsToolkit.ErrorHandling 5.2.0)
```fsharp
// Source: FsToolkit.ErrorHandling GitBook; taskResult in core package (not separate pkg)
open FsToolkit.ErrorHandling

// Bind Task<Result<'T,'E>> values cleanly
let example (client: ILlmClient) (ct: CancellationToken) : Task<Result<string, AgentError>> =
    taskResult {
        let! output = client.CompleteAsync messages model ct   // Task<Result<LlmOutput,AgentError>>
        let! tool   = dispatchTool "read_file" inputMap |> Task.FromResult  // Result -> Task<Result>
        return (sprintf "Got: %A and %A" output tool)
    }
```

### Verified: JSONL StreamWriter with AutoFlush
```fsharp
// Source: .NET StreamWriter docs; AutoFlush ensures per-write flush for crash safety
let writer = new StreamWriter(path, append = true, encoding = System.Text.Encoding.UTF8)
writer.AutoFlush <- true
// Use:
writer.WriteLine(JsonSerializer.Serialize(step, jsonOptions))
// No explicit flush needed — AutoFlush handles it per-WriteLine
```

### Verified: F# string.GetHashCode for same-process turn-scoped guard
```fsharp
// Source: .NET string.GetHashCode docs; non-deterministic across process restarts,
// deterministic within a single process run. Sufficient for turn-scoped loop guard.
let computeInputHash (raw: string) : int = raw.GetHashCode()
// Use: computeInputHash (inputMap |> Map.tryFind "_raw" |> Option.defaultValue "")
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `async { }` recursive loop | `task { }` recursive loop with `loopN` parameter | F# 9/10 (tail-call CEs) | Tail-call safe up to thousands of iterations; direct .NET Task interop |
| Mutable loop counter in `while` | Immutable `loopN` as recursive parameter | Project design decision | Loop count is a type-level invariant; `MaxLoopsExceeded` structurally impossible to miss |
| Serilog.Sinks.File for structured logs | Direct `StreamWriter` + `JsonSerializer` for JSONL | Pragmatic choice | Fewer deps, total format control, no Serilog.Sinks.File version compatibility surface |
| Separate `Serilog.Sinks.Console` + custom TextWriter | `standardErrorFromLevel = LogEventLevel.Verbose` | Serilog.Sinks.Console 5.x+ | One-parameter stderr redirect; no custom sink code |

**Deprecated/outdated:**
- `FsToolkit.ErrorHandling.TaskResult` (separate NuGet): merged into `FsToolkit.ErrorHandling` 5.x. Do NOT reference the separate package.

---

## Open Questions

1. **JSONL step write timing: end-of-turn vs per-step callback**
   - What we know: Writing at end of turn is simpler. Writing per-step (via callback) is crash-safer.
   - What's unclear: Does the planner prefer a callback design (adds a function parameter to `runSession`) or a post-turn write (simpler but JSONL not written on crash)?
   - Recommendation: Pass `onStepComplete: Step -> unit` callback into `runSession`. This is a one-parameter addition and matches SC6 requirement "readable after process exits." If the process crashes mid-step, the last-completed step is still written.

2. **Thought field capture for Step record**
   - What we know: `LlmStep.thought` is a string. `Step.Thought` is `Thought of string` (single-case DU). The agent loop needs the `LlmStep` thought field to populate Step.Thought.
   - What's unclear: `client.CompleteAsync` returns `Task<Result<LlmOutput, AgentError>>` — `LlmOutput` is either `ToolCall` or `FinalAnswer`, neither of which carries the `thought` field separately.
   - Recommendation: Either (a) change `ILlmClient.CompleteAsync` return type to include thought, or (b) expose `LlmStep` directly alongside `LlmOutput` (return a tuple), or (c) store thought in the `ToolInput` map (hacky), or (d) accept that `Step.Thought` is set to a placeholder like `Thought "[not captured]"` in v1 and that OBS-04 verbose output shows timing but not thought. Option (d) is the lowest-friction path for Phase 4.
   - IMPACT: This is a PLANNING DECISION. The planner must pick an approach. Option (d) avoids a Ports.fs amendment.

3. **AgentLoop.fs placement: Core vs Cli**
   - What we know: Loop logic is pure (only touches Ports interfaces, ContextBuffer, Router, Domain). Core is the correct home per ports-and-adapters.
   - What's unclear: `dispatchTool` parses JSON (using `System.Text.Json`), which is already available in Core's transitive dependencies. But `JsonDocument` is inbox — no extra package.
   - Recommendation: `AgentLoop.fs` goes in `BlueCode.Core`. `dispatchTool` can safely use `System.Text.Json` since it's inbox.

4. **System prompt text location**
   - What we know: A system prompt string is needed to instruct Qwen on JSON output format.
   - What's unclear: Where does the system prompt literal live? Options: `AgentLoop.fs` constant, `AgentConfig` field, external file.
   - Recommendation: Put system prompt text as a `let` binding in `AgentLoop.fs` or `AgentConfig`. Keep it inline (not a file) for v1.

---

## Sources

### Primary (HIGH confidence)
- `src/BlueCode.Core/Domain.fs` — actual Step record shape (6 fields), AgentError DU cases including `LoopGuardTripped`, `UserCancelled`, `MaxLoopsExceeded`
- `src/BlueCode.Core/Ports.fs` — confirmed `ILlmClient.CompleteAsync` and `IToolExecutor.ExecuteAsync` both already carry `CancellationToken`; no Ports.fs amendment needed
- `src/BlueCode.Core/ContextBuffer.fs` — confirmed API: `create`, `add`, `toList`, `length`, `capacity`; items most-recent-first; no `toMessages` function exists
- `src/BlueCode.Core/ToolRegistry.fs` — confirmed empty stub; NOT used by AgentLoop
- `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` — confirmed `withSpinner` wraps HTTP only; `parseLlmResponse` is in `Json.fs` (not `QwenHttpClient.fs`)
- `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` — confirmed `ToolResult.Timeout` uses `int` (seconds), `OperationCanceledException` already returns `Error UserCancelled`
- `src/BlueCode.Cli/Adapters/Json.fs` — confirmed `jsonOptions` singleton; `parseLlmResponse` is the public entry point
- `src/BlueCode.Cli/BlueCode.Cli.fsproj` — confirmed `FSharp.SystemTextJson 1.4.36`, `Spectre.Console 0.55.2` present; Serilog not yet present
- `src/BlueCode.Core/BlueCode.Core.fsproj` — confirmed `FsToolkit.ErrorHandling 5.2.0` present; compile order: Domain → Router → ContextBuffer → ToolRegistry → Ports
- `github.com/serilog/serilog-sinks-console ConsoleLoggerConfigurationExtensions.cs` — exact method signature for `WriteTo.Console(standardErrorFromLevel: LogEventLevel? ...)` confirmed; version 6.1.1 current
- `learn.microsoft.com/dotnet/api/system.text.json.jsonelement.getrawtext` — confirmed `GetRawText()` returns original input bytes verbatim (whitespace-sensitive, object-disposal-safe)
- `.planning/research/ARCHITECTURE.md`, `STACK.md`, `PITFALLS.md` — prior research confirmed; not duplicated

### Secondary (MEDIUM confidence)
- [FsToolkit.ErrorHandling GitBook](https://demystifyfp.gitbook.io/fstoolkit-errorhandling) — confirmed `taskResult {}` CE supports `let!`, `do!`, `return!` for `Task<Result<'T,'E>>` bindings; `TaskResult` merged into core package in 5.x
- [Serilog.Sinks.Console NuGet 6.1.1](https://www.nuget.org/packages/Serilog.Sinks.Console) — version 6.1.1 confirmed latest stable

### Tertiary (LOW confidence)
- WebSearch results for Ctrl+C / CancelKeyPress patterns — cross-referenced with prior PITFALLS.md findings; pattern well-established

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all packages verified in fsproj files; Serilog versions confirmed on NuGet
- Architecture: HIGH — all signatures confirmed from reading actual source files; no assumptions
- Pitfalls: HIGH — D-5 (JSONL), D-6 (JSON retry), D-1 (loop guard) all sourced from PITFALLS.md with implementation-specific additions from codebase inspection
- Step record extension: HIGH — field addition impact confirmed by reading Domain.fs and all modules that reference Step

**Research date:** 2026-04-22
**Valid until:** 2026-05-22 (stable domain; Serilog API changes rarely)
