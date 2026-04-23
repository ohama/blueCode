# Phase 5: CLI Polish - Research

**Researched:** 2026-04-23
**Domain:** F# CLI (Argu 6.2.5), Spectre.Console 0.55.2, Serilog 4.3.1, vLLM /v1/models API
**Confidence:** HIGH (codebase read directly; libraries verified against official docs)

---

## Summary

Phase 5 extends an existing single-turn CLI binary (`blueCode "<prompt>"`) into a full daily-driver tool with multi-turn REPL, rendering mode flags, a `--trace` debug sink, a runtime context-window probe, and physical retirement of the Python predecessor. All five primary libraries are already locked (Serilog, Spectre.Console) or straightforward to add (Argu — NOT yet in the .fsproj, must be added). The codebase is in excellent shape: `Rendering.fs` already has `Compact | Verbose`, `Repl.runSingleTurn` has the cancellation seams, and `CompositionRoot.bootstrap` has the injection points Phase 5 needs.

The two highest-risk design choices are: (1) how `--model` flag flows through `runSession` without touching Core, and (2) Spectre.Console's Status spinner owning stdout during LLM inference — which conflicts with step output lines. Both have clean solutions documented below.

**Primary recommendation:** Build Phase 5 as three tightly-scoped plans — Argu wiring + multi-turn REPL (05-01), rendering/verbose/trace (05-02), and /v1/models probe + Fantomas + retirement (05-03) — matching the existing placeholder breakdown but with the Fantomas pass isolated to its own task within 05-03.

---

## Standard Stack

### Core (locked — already in use)

| Library | Version | Purpose | Notes |
|---------|---------|---------|-------|
| Serilog | 4.3.1 | Structured logging to stderr | Already configured in `Adapters/Logging.fs` |
| Serilog.Sinks.Console | 6.1.1 | Console sink with stderr routing | `standardErrorFromLevel = Verbose` is already set |
| Spectre.Console | 0.55.2 | Spinner + styled stdout output | Already used in `QwenHttpClient.withSpinner` |

### To Add (Phase 5 only addition)

| Library | Version | Purpose | Notes |
|---------|---------|---------|-------|
| Argu | 6.2.5 | Declarative CLI arg parsing | NOT in BlueCode.Cli.fsproj yet — must be added |

### Supporting (already present, no change needed)

| Library | Version | Purpose | Notes |
|---------|---------|---------|-------|
| FSharp.SystemTextJson | 1.4.36 | F# DU/record JSON serialization | Used in JsonlSink, QwenHttpClient |
| Fantomas | 7.x (local tool) | Code formatter | Not yet installed — 05-03 adds it as local dotnet tool |

**Installation for Argu:**
```bash
# Add to BlueCode.Cli.fsproj:
<PackageReference Include="Argu" Version="6.2.5" />

# Then restore:
dotnet restore src/BlueCode.Cli/BlueCode.Cli.fsproj
```

**Installation for Fantomas (local tool, 05-03 only):**
```bash
dotnet new tool-manifest   # creates .config/dotnet-tools.json if absent
dotnet tool install fantomas
dotnet fantomas src/ tests/   # formats recursively; overwrites only changed files
```

---

## Architecture Patterns

### Recommended Project Structure (Phase 5 changes)

```
src/BlueCode.Cli/
├── Adapters/
│   ├── Logging.fs         # MODIFY: accept LoggingLevelSwitch, export it
│   ├── QwenHttpClient.fs  # MODIFY: add getModelsAsync, accept forcedModel option
│   └── ...                # unchanged
├── Rendering.fs           # UNCHANGED (RenderMode already has Compact | Verbose)
├── CompositionRoot.fs     # MODIFY: accept CliOptions record, wire forced model
├── Repl.fs                # MODIFY: add runMultiTurn loop wrapping runSingleTurn
└── Program.fs             # REPLACE: Argu parser replaces manual argv handling
```

### Pattern 1: Argu CLI argument union

**What:** Declare a DU implementing `IArgParserTemplate`. Use `[<MainCommand>]` (no `ExactlyOnce`) for the optional positional prompt. Detect REPL mode by `TryGetResult Prompt = None`.

**Key gotcha:** `[<MainCommand>]` requires the case to carry a value. To make it optional (absent = REPL mode), do NOT add `[<Mandatory>]` or `[<ExactlyOnce>]`. Use `TryGetResult` — it returns `None` when the argument is absent.

**`--help` behavior:** Argu registers `--help` and `-h` automatically. Neither conflicts with anything in this project. Catch `ArguParseException` in `main` and print `e.Message` to stderr, then exit 2 (usage error — matches existing convention).

```fsharp
// Source: Argu tutorial https://fsprojects.github.io/Argu/tutorial.html
open Argu

type CliArgs =
    | [<MainCommand; Last>] Prompt of prompt: string list   // list allows multi-word without quotes
    | Verbose
    | Trace
    | [<AltCommandLine("-m")>] Model of model: string       // "32b" | "72b"
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Prompt _ -> "Prompt to send (omit for interactive REPL mode)."
            | Verbose  -> "Print full thought/action/input/output per step."
            | Trace    -> "Emit Serilog Debug JSON per step to stderr (independent of --verbose)."
            | Model _  -> "Force model: 32b or 72b (skips intent classification)."

// In Program.fs [<EntryPoint>]:
let parser = ArgumentParser.Create<CliArgs>(programName = "blueCode")
try
    let results = parser.ParseCommandLine(argv, raiseOnUsage = true)
    let promptWords = results.TryGetResult Prompt |> Option.defaultValue []
    let isVerbose   = results.Contains Verbose
    let isTrace     = results.Contains Trace
    let forcedModel = results.TryGetResult Model
    // promptWords = [] → multi-turn REPL mode
    // promptWords = ["..."] → single-turn mode
    ...
with :? ArguParseException as e ->
    eprintfn "%s" e.Message
    2
```

**Why `string list` not `string`:** A user typing `blueCode list the files` (without quotes) passes three positional args. `string list` with `[<MainCommand; Last>]` captures all trailing positional tokens into one case. Joined with `String.concat " "` to get the prompt.

**`--help` / `-h` conflict:** None. Argu auto-registers these. No existing CLI flag uses `-h`. Safe.

### Pattern 2: Multi-turn REPL loop in Repl.fs

**What:** Wrap `runSingleTurn` in a `task {}` loop that reads lines from stdin until `/exit` or EOF (`Console.ReadLine() = null`).

**Critical separation:** Ctrl+D (EOF) exits the REPL cleanly → detected by `ReadLine() = null`. Ctrl+C during LLM inference cancels the current turn via `CancellationTokenSource` → already handled by the existing `CancelKeyPress` handler in `runSingleTurn`. These are two different paths; do NOT collapse them.

**JSONL sink:** One `JsonlSink` per process run (one session file), not one per turn. The sink is opened in `Program.fs` via `use` and passed to all turns. Steps from all turns accumulate in one `.jsonl` file. This matches the existing architecture — `AppComponents.JsonlSink` already survives the full process lifetime.

**Conversation history:** Keep Phase 4 `AgentLoop.runSession` completely untouched. Multi-turn conversation history is a REPL concern, not a loop concern. Each turn is an independent `runSession` call. The LLM receives a new, clean message history per turn. This matches the existing `ContextBuffer` ring-buffer design (it's intra-turn memory, not inter-turn memory). Phase 5 does NOT add cross-turn message accumulation — that's post-v1.

```fsharp
// Repl.fs — new runMultiTurn function
let runMultiTurn (components: AppComponents) (renderMode: RenderMode) : Task<int> =
    task {
        printfn "blueCode REPL — type /exit or press Ctrl+D to quit."
        let mutable lastCode = 0
        let mutable running  = true
        while running do
            printf "> "
            let line = Console.ReadLine()   // null on Ctrl+D / EOF
            match line with
            | null -> running <- false      // EOF: clean exit, code = lastCode
            | "/exit" -> running <- false
            | "" -> ()                      // empty line — ignore
            | prompt ->
                let! code = runSingleTurn prompt components renderMode
                lastCode <- code
                // After SIGINT mid-turn (exit 130), continue REPL loop
                // (the CancelKeyPress handler in runSingleTurn handles per-turn cancel)
        return lastCode
    }
```

**EOF detection:** `Console.ReadLine()` returns `null` on Unix Ctrl+D and Windows Ctrl+Z+Enter. This is the canonical .NET/F# pattern (confirmed in Microsoft official docs). Do NOT use `Console.In.Peek() = -1` — that races with buffering.

### Pattern 3: Forced model routing (ROU-04)

**What:** `--model 32b|72b` completely bypasses `classifyIntent`. The forced model is resolved in `Program.fs` after Argu parsing and passed through `CompositionRoot` into `AgentLoop.runSession` via a new `AgentConfig.ForcedModel: Model option` field.

**Design decision:** Add `ForcedModel: Model option` to `AgentConfig` in `AgentLoop.fs` (Core), then change `runSession` to use it: `let model = config.ForcedModel |> Option.defaultWith (fun () -> userInput |> classifyIntent |> intentToModel)`.

**Alternative rejected:** Passing `forcedModel` as a separate parameter to `runSession` avoids touching `AgentConfig` but breaks the existing clean record API. Adding it to `AgentConfig` is cleaner and more extensible.

**CLI model string → Domain.Model mapping** lives in `CompositionRoot.fs` (CLI layer), not in Core:
```fsharp
let parseForcedModel (s: string option) : Model option =
    match s with
    | Some "32b" -> Some Qwen32B
    | Some "72b" -> Some Qwen72B
    | Some other -> failwithf "Unknown model: %s (valid: 32b, 72b)" other
    | None -> None
```

### Pattern 4: Serilog LoggingLevelSwitch for --trace

**What:** Create a `LoggingLevelSwitch` before logger creation. Pass it to `Logging.configure`. After Argu parse, flip it to `Debug` if `--trace` was set; leave at `Information` otherwise.

**Why:** `Log.Logger` is set once at startup (call to `configure()` in `Program.fs`). Argu parsing happens after `configure()`. The switch allows post-creation level mutation without recreating the logger.

**Implementation:**
```fsharp
// Adapters/Logging.fs
open Serilog
open Serilog.Core
open Serilog.Events

// Exported so Program.fs can flip it after argv parse
let levelSwitch = LoggingLevelSwitch(LogEventLevel.Information)

let configure () : unit =
    Log.Logger <-
        LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.Console(
                standardErrorFromLevel = System.Nullable<LogEventLevel>(LogEventLevel.Verbose),
                outputTemplate = "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger()
```

```fsharp
// Program.fs — after Argu parse
if isTrace then
    Logging.levelSwitch.MinimumLevel <- LogEventLevel.Debug
```

**Trace event origin:** Trace events (`Log.Debug(...)`) are emitted from the `onStep` callback in `Repl.fs` — the CLI layer — NOT from inside `AgentLoop` (which must remain Core-pure with no Serilog). The existing `onStep` in `runSingleTurn` already calls `Log.Debug(...)`. For `--trace` mode, add full untruncated input/output to that debug log call. No Core changes needed.

```fsharp
// Repl.fs onStep — extended for trace
let onStep (step: Step) =
    components.JsonlSink.WriteStep step
    printfn "%s" (renderStep renderMode step)
    // Always emit this — only visible when --trace sets level to Debug
    Log.Debug(
        "Step {Number}: action={Action} elapsed={DurationMs}ms input={Input} output={Output}",
        step.StepNumber,
        step.Action,
        step.DurationMs,
        sprintf "%A" step.Action,   // full, untruncated
        sprintf "%A" step.ToolResult)
```

### Pattern 5: vLLM /v1/models probe (OBS-03)

**Field location:** `max_model_len` is directly on `ModelCard`, NOT nested. Response shape:
```json
{
  "object": "list",
  "data": [
    {
      "id": "qwen2.5-coder-32b-instruct",
      "object": "model",
      "created": 1234567890,
      "owned_by": "vllm",
      "max_model_len": 32768,
      "permission": [...]
    }
  ]
}
```

**Field name is stable** across vLLM releases (present since v0.5.x, confirmed in `protocol.py`). It is typed `Optional[int] = None` — so it CAN be null/missing in older deployments. Handle gracefully.

**Fallback strategy:** If `max_model_len` is missing or null, log a warning to stderr and fall back to `8192` (not Phase 4's `ContextCapacity=3`, which is a message-count not token-count). The 80% threshold of 8192 = 6553 tokens is a safe conservative floor.

**Token counting:** Do NOT implement a real tokenizer for v1. Approximate: `totalTokens ≈ totalCharacters / 4`. This is crude but acceptable. Phase 5 counts accumulated context characters (sum of all message `.Content` lengths) and compares against `maxModelLen * 4` (characters). The warning fires before the next LLM call when `accumulatedChars >= maxModelLen * 4 * 0.80`.

**Probe implementation:** Add `getMaxModelLenAsync` to `QwenHttpClient.fs` as a separate public function (not part of `ILlmClient` — that port stays minimal). Call it from `CompositionRoot.bootstrap` (or a new `bootstrapAsync`). Use port 8000 (32B server) as the probe target — or probe both and use the smaller value.

```fsharp
// QwenHttpClient.fs — new function
let getMaxModelLenAsync (ct: CancellationToken) : Task<int option> =
    task {
        try
            use! resp = httpClient.GetAsync("http://127.0.0.1:8000/v1/models", ct)
            if not resp.IsSuccessStatusCode then return None
            else
                let! json = resp.Content.ReadAsStringAsync(ct)
                use doc = JsonDocument.Parse(json)
                let data = doc.RootElement.GetProperty("data")
                if data.GetArrayLength() = 0 then return None
                else
                    match data.[0].TryGetProperty("max_model_len") with
                    | true, el when el.ValueKind = JsonValueKind.Number ->
                        let ok, v = el.TryGetInt64()
                        if ok then return Some (int v) else return None
                    | _ -> return None
        with _ -> return None
    }
```

**Warning emission:** Emitted via `printfn` to stdout (visible to user), NOT via Serilog (which goes to stderr). The warning text should be bold/colored using Spectre.Console `AnsiConsole.MarkupLine("[yellow]WARNING: context at 80% of model limit[/]")`.

**Context measurement:** The 80% warning applies to the context passed to the LLM per call. In `AgentLoop.buildMessages`, the messages list is already assembled per call. The check must happen BEFORE the LLM call — meaning the warning logic lives in the CLI layer's `onStep`-adjacent code, or is threaded through `AgentConfig` as a threshold.

Simplest approach: pass `maxModelLen: int` into `AppComponents`; in `Repl.fs`'s `onStep` callback, accumulate character count and check threshold. No Core changes.

### Pattern 6: Spectre.Console spinner + verbose output

**Stdout ownership:** `AnsiConsole.Status()` writes to stdout (confirmed: it's not thread-safe and "owns" the console during its execution). `printfn` also writes to stdout. Serilog goes to stderr. The existing `withSpinner` in `QwenHttpClient` wraps the HTTP call ONLY — step output via `printfn` happens AFTER the spinner ends, which is correct.

**Thread safety:** Do NOT call `printfn` or `AnsiConsole.MarkupLine` FROM INSIDE the Status callback. The callback is only for the spinner label update. Step-rendering output happens in `onStep`, which is called after `CompleteAsync` returns (after the spinner ends). This architecture is already correct in Phase 4.

**Non-TTY behavior:** Spectre auto-detects non-TTY (CI) and silently no-ops the spinner. No special handling needed.

**Verbose mode panels:** Do NOT use Spectre's `Panel` or `Tree` widgets for verbose mode. They add visual complexity (borders, padding) that clutters the output and is hard to pipe. Use multi-line `printfn` output as implemented in the existing `renderVerbose` function. The existing `Rendering.fs` `renderVerbose` is correct and sufficient for Phase 5.

**Elapsed time in spinner label:** The existing `withSpinner label ...` receives a static label "Thinking... [32B]". Phase 5 can make it dynamic by updating `ctx.Status <- sprintf "Thinking... [%s] %ds" modelLabel elapsed` inside the task, but this is a nice-to-have, not required by any requirement. Keep it simple for v1.

### Anti-Patterns to Avoid

- **Accumulating conversation history across turns in AgentLoop:** Multi-turn memory is a REPL concern. Keep `runSession` stateless per turn.
- **Calling `printfn` inside `AnsiConsole.Status` callback:** Corrupts terminal output. Print after spinner exits.
- **Re-calling `Logging.configure()` per turn:** One-shot at process start. The `LoggingLevelSwitch` handles runtime level changes.
- **Using `int` for token count vs. `int64`:** `max_model_len` can be large (128K+ for some models). Use `int64` or cast carefully.
- **Putting `/v1/models` probe URL in Core:** Core has no HTTP. The probe belongs in `QwenHttpClient.fs` (CLI/adapter layer).
- **Collapsing Ctrl+D and Ctrl+C into one handler:** EOF (`ReadLine() = null`) exits the REPL loop; SIGINT (`CancelKeyPress`) cancels the current turn. They are orthogonal.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| CLI arg parsing | Manual `argv` inspection | Argu 6.2.5 | Auto --help, type-safe, validated, already locked |
| Runtime log level flip | Recreate logger after parse | `LoggingLevelSwitch` | Serilog's designed mechanism; CreateLogger is one-shot |
| Code formatting | Manual style enforcement | Fantomas dotnet local tool | F# community standard; idempotent |
| Token counting | Implement tiktoken or similar | Character / 4 approximation | Acceptable for v1 80% threshold; real tokenizer is post-v1 |
| Spinner | Custom async console animation | `AnsiConsole.Status().StartAsync` | Already in use (QwenHttpClient); non-TTY safe |

**Key insight:** Every "could roll our own" item has a worse edge-case story than the library solution. The approval gate for Phase 5 is daily-driver usability, not perfection — approximations are explicitly acceptable.

---

## Common Pitfalls

### Pitfall 1: Argu `string list` MainCommand requires no `[<Mandatory>]`

**What goes wrong:** Adding `[<Mandatory>]` or `[<ExactlyOnce>]` to the `Prompt` case means `blueCode` with no args throws `ArguParseException` instead of entering REPL mode.

**How to avoid:** Use `[<MainCommand; Last>]` without Mandatory. Use `TryGetResult Prompt` — returns `None` when absent.

**Warning sign:** `blueCode --verbose` (no prompt) throws a parse exception instead of entering REPL.

### Pitfall 2: `--model` must be parsed BEFORE `bootstrap` but Argu parses AFTER `configure()`

**Ordering:** `configure()` must run first (Serilog). Then `parser.ParseCommandLine`. Then extract `forcedModel`. Then call `bootstrapAsync(projectRoot, opts)`. The `forcedModel` flows into `AgentConfig.ForcedModel` which `runSession` reads.

**What goes wrong:** If `bootstrap` is called before parse, the forced model never reaches `runSession`, and intent classification runs instead.

**How to avoid:** `Program.fs` must follow: configure() → parse → bootstrap(opts) — in that exact order.

### Pitfall 3: Spectre Status spinner holds stdout; verbose step output must happen after spinner exits

**What goes wrong:** If you emit a step summary line from inside `withSpinner`'s callback, it races with the spinner's ANSI escape sequence rewrites and corrupts the terminal.

**How to avoid:** `onStep` is called from `runSession` AFTER `CompleteAsync` returns, which is AFTER `withSpinner` exits. This is already correct in Phase 4 architecture. Don't change it.

### Pitfall 4: JsonlSink is one file per process, not per turn

**What goes wrong:** Opening a new `JsonlSink` per turn creates `~/.bluecode/session_<ts>.jsonl` files for each turn, breaking session continuity.

**How to avoid:** `JsonlSink` is owned by `Program.fs` via `use _sink = ...` and passed through `AppComponents` to all turns. The existing architecture is correct — don't re-open it per turn.

### Pitfall 5: `Console.ReadLine()` in REPL loop vs. Spectre spinner conflict

**What goes wrong:** If a `runSingleTurn` call is in-flight (spinner running on stdout) and the user presses Enter, stdin input buffering may cause the next ReadLine to return immediately with that buffered newline.

**How to avoid:** The multi-turn loop must `await` `runSingleTurn` to completion before calling `Console.ReadLine()` again. This is implicit in the `task {}` loop with `let! code = runSingleTurn ...`. Always `await` fully — no fire-and-forget.

### Pitfall 6: vLLM `max_model_len` may be null/missing on older versions

**What goes wrong:** `doc.[0].GetProperty("max_model_len").GetInt32()` throws if the field is absent or null.

**How to avoid:** Use `TryGetProperty` + `ValueKind = JsonValueKind.Number` check as shown in the code pattern above. Fall back to `8192` with a stderr warning on any failure path.

### Pitfall 7: `LoggingLevelSwitch` is mutable module-level state — initialize before `configure()`

**What goes wrong:** If `levelSwitch` is declared after `configure()` runs, the logger is created with a null switch reference.

**How to avoid:** Declare `let levelSwitch = LoggingLevelSwitch(LogEventLevel.Information)` as a module-level `let` in `Logging.fs` before the `configure` function. The switch is initialized when the module loads; `configure()` references it.

### Pitfall 8: Retirement task requires human checkpoint

**What goes wrong:** Automating `mv ~/projs/claw-code-agent ~/projs/claw-code-agent-retired` in an agent plan task destroys a directory outside the repo without user confirmation.

**How to avoid:** Plan 05-03 must mark the retirement task as a human checkpoint (`autonomous: false`). The task should show the user the exact command and ask them to run it manually, then confirm completion before the plan closes.

---

## Code Examples

### Argu complete wiring in Program.fs

```fsharp
// Source: Argu tutorial https://fsprojects.github.io/Argu/tutorial.html + this codebase
[<EntryPoint>]
let main (argv: string array) : int =
    Logging.configure()   // MUST be first

    let parser = ArgumentParser.Create<CliArgs>(programName = "blueCode")
    try
        let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
        let promptWords = results.TryGetResult Prompt |> Option.defaultValue []
        let isVerbose   = results.Contains Verbose
        let isTrace     = results.Contains Trace
        let forcedModel = results.TryGetResult Model

        if isTrace then
            Logging.levelSwitch.MinimumLevel <- Serilog.Events.LogEventLevel.Debug

        let opts = {
            ForcedModel = parseForcedModel forcedModel
            RenderMode  = if isVerbose then Verbose else Compact
            TraceMode   = isTrace
        }

        let projectRoot = System.IO.Directory.GetCurrentDirectory()
        let components  = CompositionRoot.bootstrapAsync projectRoot opts |> Async.AwaitTask |> Async.RunSynchronously
        use _sink = components.JsonlSink

        let exitCode =
            match promptWords with
            | [] -> Repl.runMultiTurn components opts.RenderMode
            | words ->
                let prompt = System.String.concat " " words
                Repl.runSingleTurn prompt components opts.RenderMode
            |> fun t -> t.GetAwaiter().GetResult()

        Logging.shutdown()
        exitCode

    with
    | :? ArguParseException as e ->
        eprintfn "%s" e.Message
        Logging.shutdown()
        2
    | ex ->
        try Serilog.Log.Fatal(ex, "Unhandled exception") with _ -> ()
        eprintfn "Fatal: %s" ex.Message
        Logging.shutdown()
        1
```

### LoggingLevelSwitch in Logging.fs

```fsharp
// Source: https://peterdaugaardrasmussen.com/2023/08/03/csharp-serilog-how-to-change-the-log-level-at-runtime/
open Serilog.Core
open Serilog.Events

let levelSwitch = LoggingLevelSwitch(LogEventLevel.Information)

let configure () : unit =
    Log.Logger <-
        LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.Console(
                standardErrorFromLevel = System.Nullable<LogEventLevel>(LogEventLevel.Verbose),
                outputTemplate = "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger()
```

### AgentConfig with ForcedModel (Core change — minimal)

```fsharp
// AgentLoop.fs — additive change only
type AgentConfig = {
    MaxLoops         : int
    ContextCapacity  : int
    SystemPrompt     : string
    ForcedModel      : BlueCode.Core.Domain.Model option   // None = use Router
}

// runSession — change model selection only
let model =
    config.ForcedModel
    |> Option.defaultWith (fun () -> userInput |> classifyIntent |> intentToModel)
```

### Multi-turn REPL with EOF + /exit detection

```fsharp
// Source: Microsoft docs Console.ReadLine + this codebase pattern
let runMultiTurn (components: AppComponents) (renderMode: RenderMode) : Task<int> =
    task {
        printfn "blueCode — multi-turn mode. Type /exit or Ctrl+D to quit."
        let mutable lastCode = 0
        let mutable running  = true
        while running do
            printf "\nblueCode> "
            let line = Console.ReadLine()    // null = EOF (Ctrl+D)
            match line with
            | null        -> running <- false
            | "/exit"     -> running <- false
            | s when s.Trim() = "" -> ()
            | prompt ->
                let! code = runSingleTurn prompt components renderMode
                lastCode <- if code = 130 then 0 else code  // SIGINT per-turn: continue REPL
        return lastCode
    }
```

### vLLM /v1/models probe

```fsharp
// QwenHttpClient.fs — verified against vllm/entrypoints/openai/protocol.py ModelCard
let getMaxModelLenAsync (ct: CancellationToken) : Task<int> =
    task {
        let fallback = 8192
        try
            use! resp = httpClient.GetAsync("http://127.0.0.1:8000/v1/models", ct)
            if not resp.IsSuccessStatusCode then
                Log.Warning("GET /v1/models returned {Status}; using fallback {Fallback}",
                            int resp.StatusCode, fallback)
                return fallback
            let! json = resp.Content.ReadAsStringAsync(ct)
            use doc = JsonDocument.Parse(json)
            let data = doc.RootElement.GetProperty("data")
            if data.GetArrayLength() = 0 then return fallback
            match data.[0].TryGetProperty("max_model_len") with
            | true, el when el.ValueKind = JsonValueKind.Number ->
                let ok, v = el.TryGetInt64()
                if ok && v > 0L then return int v
                else return fallback
            | _ ->
                Log.Warning("max_model_len not found in /v1/models response; using fallback {Fallback}", fallback)
                return fallback
        with ex ->
            Log.Warning(ex, "GET /v1/models failed; using fallback {Fallback}", fallback)
            return fallback
    }
```

---

## State of the Art

| Old Approach (Phase 4) | Phase 5 Change | Impact |
|------------------------|----------------|--------|
| Manual `argv[0]` prompt parsing | Argu DU parser | `--help`, typed flags, error messages for free |
| `runSingleTurn` only | `runMultiTurn` wrapping it | REPL loop with `/exit` + EOF |
| `Compact` hardcoded in `onStep` | `renderMode` threaded as parameter | `--verbose` flag now wires end-to-end |
| `Logging.MinimumLevel.Debug()` fixed | `LoggingLevelSwitch` | `--trace` toggles debug events at runtime |
| `ContextCapacity = 3` hardcoded | `max_model_len` from `/v1/models` | Context warning fires at real 80% threshold |
| No Python agent retirement | Physical `mv` + human checkpoint | Closes daily-driver migration |

**Not deprecated:** Nothing in the existing code becomes invalid. All Phase 5 changes are additive or localised replacements.

---

## Plan Wave Recommendation

The three placeholder plans are appropriately scoped. Recommended as-is with one clarification:

**05-01: Argu + multi-turn REPL + --model flag**
- Add Argu to .fsproj
- Define `CliArgs` DU + parser in `Program.fs`
- Add `ForcedModel` to `AgentConfig` (single Core field change)
- Add `runMultiTurn` to `Repl.fs`; wire `renderMode` param through `runSingleTurn` 
- Wire `CompositionRoot.bootstrapAsync` to accept `CliOptions`
- Tests: Argu parse matrix (no args, prompt only, --verbose, --model 72b, --help exits 2)

**05-02: Verbose rendering + --trace + spinner elapsed**
- Wire `renderMode` into `onStep` (currently hardcoded `Compact`)
- Add `LoggingLevelSwitch` to `Logging.fs`; wire `--trace` in `Program.fs`
- Extend `onStep` in `Repl.fs` with full untruncated `Log.Debug` for trace
- Optional: update spinner label to show elapsed seconds (nice-to-have)
- Tests: trace events only appear at Debug level; verbose output contains thought/result lines

**05-03: /v1/models probe + context warning + Fantomas + retirement**
- Add `getMaxModelLenAsync` to `QwenHttpClient.fs`
- Add context character accumulation + 80% warning in `Repl.fs`
- Fantomas: install local tool + format pass (separate git commit — noisy diff)
- Retirement: human checkpoint task (autonomous: false)
- Tests: 80% warning fires when char count exceeds threshold; probe fallback on HTTP error

**Fantomas note:** Run `dotnet fantomas src/ tests/` as an isolated commit BEFORE or AFTER feature work in 05-03, never mixed into a feature commit. The diff is large and noisy — it must be readable independently.

---

## Open Questions

1. **Thought capture still `"[not captured in v1]"` — does Phase 5 fix this?**
   - What we know: `AgentLoop.fs` hardcodes `Thought "[not captured in v1]"` because `ILlmClient.CompleteAsync` returns `LlmOutput` not `(Thought * LlmOutput)`.
   - What's unclear: Requirements CLI-03 says verbose prints "thought/action/input/output/status". If thought is always `[not captured]`, verbose mode is degraded.
   - Recommendation: Keep Phase 5 scope tight — verbose prints the existing `renderVerbose` output including the placeholder thought. Thought capture is post-v1. No planner action needed.

2. **Context window warning: per-turn or cumulative across turns?**
   - What we know: In multi-turn REPL, each `runSession` call is independent (no cross-turn history). So "accumulated context" is intra-turn only (the ContextBuffer ring window).
   - Recommendation: Warn when the rolling context window (Phase 4's 3-step ring buffer in char terms) approaches 80% of `maxModelLen`. In practice, measure the character count of the `buildMessages` output before the LLM call.

3. **Should `bootstrapAsync` become the public API or keep `bootstrap` sync?**
   - The `/v1/models` probe is async. `Program.fs` currently calls `bootstrap` synchronously (via `GetAwaiter().GetResult()`). This pattern is already used for `runSingleTurn`.
   - Recommendation: Add `bootstrapAsync` that returns `Task<AppComponents>` and call it with `.GetAwaiter().GetResult()` in `Program.fs`. Do not change the existing sync `bootstrap` signature — tests reference it.

4. **`--model` flag string validation: hard error or warning?**
   - If user passes `--model 8b` (invalid), should it be a parse error (exit 2) or a warning + fallback?
   - Recommendation: Hard error at parse time — `parseForcedModel` raises `ArguParseException` (or return `failwithf` caught by outer `with`). Consistent with "usage error = exit 2" convention.

---

## Sources

### Primary (HIGH confidence)
- Direct codebase read: `Program.fs`, `Repl.fs`, `CompositionRoot.fs`, `Rendering.fs`, `Adapters/Logging.fs`, `AgentLoop.fs`, `Router.fs`, `ContextBuffer.fs`, `QwenHttpClient.fs`, `JsonlSink.fs`, `Domain.fs`, `BlueCode.Cli.fsproj`
- [Argu tutorial](https://fsprojects.github.io/Argu/tutorial.html) — MainCommand, TryGetResult, IArgParserTemplate pattern
- [Argu attributes reference](https://fsprojects.github.io/Argu/reference/argu-arguattributes.html) — Mandatory, ExactlyOnce, MainCommand, AltCommandLine
- [Microsoft Docs Console.ReadLine](https://learn.microsoft.com/en-us/dotnet/api/system.console.readline) — confirmed null on Ctrl+D/EOF
- [vllm protocol.py v0.5.2](https://github.com/vllm-project/vllm/blob/v0.5.2/vllm/entrypoints/openai/protocol.py) — ModelCard fields confirmed, `max_model_len: Optional[int] = None` directly on ModelCard

### Secondary (MEDIUM confidence)
- [Serilog runtime log level change](https://peterdaugaardrasmussen.com/2023/08/03/csharp-serilog-how-to-change-the-log-level-at-runtime/) — LoggingLevelSwitch pattern; verified against Serilog GitHub wiki
- [Fantomas GettingStarted](https://fsprojects.github.io/fantomas/docs/end-users/GettingStarted.html) — confirmed `dotnet fantomas ./src` syntax; version 7.x on NuGet
- [Spectre.Console Status docs](https://spectreconsole.net/console/live/status) — not thread-safe, non-TTY no-op confirmed via search corroboration

### Tertiary (LOW confidence)
- WebSearch results re: Spectre stdout/stderr stream ownership — could not get authoritative source; confirmed via source code inspection logic and existing Phase 4 behaviour (Spectre writes to stdout, Serilog to stderr)

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — packages locked, versions confirmed in .fsproj
- Argu patterns: HIGH — official docs read directly
- Architecture (multi-turn, forced model): HIGH — based on direct codebase read
- vLLM /v1/models field: HIGH — confirmed in protocol.py source
- Serilog LoggingLevelSwitch: HIGH — documented API pattern
- Spectre thread safety: MEDIUM — docs say "not thread safe"; stdout vs stderr ownership inferred
- Fantomas: HIGH — official docs, stable tool

**Research date:** 2026-04-23
**Valid until:** 2026-05-23 (Argu/Serilog stable; Spectre.Console 0.55.2 locked)
