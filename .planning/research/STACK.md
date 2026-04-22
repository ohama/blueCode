# Stack Research

**Domain:** F# / .NET 10 local LLM coding agent (CLI, OpenAI-compat, strict JSON, DU-heavy)
**Researched:** 2026-04-22
**Confidence:** HIGH

---

## Recommended Stack

### Core Technologies

| Technology | NuGet / SDK | Version | Purpose | Why Recommended |
|------------|-------------|---------|---------|-----------------|
| F# 10 / .NET 10 | SDK | 10.0 (LTS) | Language + runtime | LTS until 2028. F# 10 ships with `and!` in `task {}`, tail-call CEs, typed bindings without parens — all relevant to agent loop. |
| System.Net.Http.HttpClient | inbox | net10.0 | HTTP to Qwen endpoints | Stdlib, zero-dep. Sufficient for chat completions + SSE streaming via `GetStreamAsync`. No additional HTTP lib needed. |
| System.Net.ServerSentEvents | inbox (net9+) | net10.0 | Parse `data:` SSE lines from streaming response | First-class client SSE support since .NET 9; ships inbox in net10. Use `SseParser.Create` to consume LLM token stream as `IAsyncEnumerable`. |
| FSharp.SystemTextJson | `FSharp.SystemTextJson` | 1.4.36 | DU + record ↔ JSON serialization | The only maintained library that handles F# discriminated unions cleanly with `System.Text.Json`. Last update June 2025, targets net10. |
| System.Text.Json | inbox | net10.0 | JSON serialize/deserialize + strict validation | .NET 10 adds `JsonSerializerOptions.Strict` preset (disallows duplicate props, unmapped members, preserves case sensitivity) — directly useful for enforcing `{thought, action, input}` schema. |
| JsonSchema.Net | `JsonSchema.Net` | 9.2.0 | Runtime JSON schema validation of LLM output | Validates parsed `JsonElement` against a JSON Schema draft-2020-12 object. Use after deserializing LLM output to hard-fail on schema violations before dispatching tools. |
| FsToolkit.ErrorHandling | `FsToolkit.ErrorHandling` | 5.2.0 | `result {}` / `taskResult {}` computation expressions | Standard F# error-handling library. `taskResult {}` CE is exactly right for the agent loop: each step is `Task<Result<StepOutput, AgentError>>`. |
| FSharp.Control.TaskSeq | `FSharp.Control.TaskSeq` | 1.1.1 | `taskSeq {}` + `IAsyncEnumerable` operators for SSE token stream | Best way to consume the SSE `IAsyncEnumerable<SseItem<string>>` from the streaming LLM response with `TaskSeq.fold` / `TaskSeq.toArrayAsync`. |
| Spectre.Console | `Spectre.Console` | 0.55.2 | Terminal rendering (step panels, status spinners, markup) | De facto standard for rich .NET CLI. `AnsiConsole.MarkupLine`, `LiveDisplay`, `Rule` panels map cleanly to the compact/verbose step rendering the design requires. |
| Argu | `Argu` | 6.2.5 | CLI arg parsing via F# DU | F#-native: CLI schema defined as a DU, auto-generates `--help`. Fits the "everything is a DU" philosophy. |
| Serilog | `Serilog` + `Serilog.Sinks.Console` | 4.3.1 | Structured agent-step logging | Write structured events (`{StepNumber}`, `{Action}`, `{ModelChoice}`) to stderr; separates log stream from Spectre UI on stdout. |

### Supporting Libraries

| Library | NuGet | Version | Purpose | When to Use |
|---------|-------|---------|---------|-------------|
| Serilog.Sinks.File | `Serilog.Sinks.File` | 6.x | Log agent steps to file | Enable when debugging agent loop failures; writes NDJSON by default. |
| Expecto | `Expecto` | 10.x | F#-native test runner | For unit tests of DU state machines, JSON parsing, tool dispatch. Prefer over xUnit for pure F# code. |
| FsUnit.Xunit | `FsUnit.Xunit` | 5.x | Assertion DSL on top of xUnit | Use only if you need xUnit integration (e.g., IDE test explorer). Otherwise Expecto has its own assertions. |

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| `dotnet watch run` | Hot-reload dev loop | Works for console apps in .NET 10; useful during agent loop iteration. |
| Fantomas | F# code formatter | Add as dotnet tool: `dotnet tool install fantomas`. Configure via `.editorconfig`. |
| `dotnet fsi` | F# interactive / REPL | Useful for prototyping JSON parsing and DU mapping logic before integrating into the agent loop. |

---

## Installation

```bash
# Create project
dotnet new console -lang F# -n blueCode --framework net10.0

# Core NuGet packages
dotnet add package FSharp.SystemTextJson --version 1.4.36
dotnet add package FsToolkit.ErrorHandling --version 5.2.0
dotnet add package FSharp.Control.TaskSeq --version 1.1.1
dotnet add package JsonSchema.Net --version 9.2.0
dotnet add package Spectre.Console --version 0.55.2
dotnet add package Argu --version 6.2.5
dotnet add package Serilog --version 4.3.1
dotnet add package Serilog.Sinks.Console --version 6.0.0

# Dev/test
dotnet add package Expecto --version 10.2.1
dotnet tool install --global fantomas
```

**Note:** `System.Net.Http`, `System.Net.ServerSentEvents`, and `System.Text.Json` ship inbox with net10.0 — no NuGet reference needed.

---

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative | Why NOT for blueCode |
|-------------|-------------|------------------------|----------------------|
| `System.Net.Http.HttpClient` | Flurl / RestSharp | Flurl: better fluent syntax for complex REST surfaces | Flurl adds 2 deps for zero actual benefit on 1-endpoint chat completions. Stdlib is enough. |
| `FSharp.SystemTextJson` | Thoth.Json.Net | Thoth.Json: more control over decode errors, Elm-style | Thoth.Json.Net 12.0.0 (May 2024) targets netstandard2.0 only and is less maintained. FSharp.SystemTextJson is more actively developed and integrates directly with `System.Text.Json` infrastructure including the new .NET 10 `Strict` options. |
| `FSharp.SystemTextJson` | Newtonsoft.Json | Projects already on Newtonsoft with large DU serialization needs | Newtonsoft is legacy. Zero advantage vs System.Text.Json on net10. Avoid. |
| `task {}` CE | `async {}` CE | `async {}`: implicit cancellation propagation, F#-idiomatic for pure F# code | For this project, `task {}` is correct because HttpClient, Process, and TaskSeq all return `Task<T>`. Mixing CE styles adds friction. Use `task {}` throughout. |
| `FsToolkit.ErrorHandling` | Manual `Result<_,_>` chains | Tiny projects where the dependency feels heavy | blueCode has 5+ error domains (HTTP, JSON parse, schema validation, tool execution, loop limit). The `taskResult {}` CE eliminates 100+ lines of manual bind chains. Worth the dependency. |
| `JsonSchema.Net` | NJsonSchema | Large codegen / Swagger use cases | NJsonSchema pulls in Newtonsoft. JsonSchema.Net is pure System.Text.Json. |
| `Spectre.Console` | Raw `printfn` + ANSI escapes | Minimal output requirements | The design doc explicitly calls for compact vs verbose step rendering, status spinners, and structured step panels. Spectre covers all of this at 0.55.2 with .NET 10 support. |
| `Argu` | `System.CommandLine` | Complex multi-verb CLI with DI integration | System.CommandLine is C#-first and verbose in F#. Argu defines the CLI schema as a DU — exactly what this project does for everything else. |
| `Expecto` | xUnit | CI systems requiring VSTest adapter, or shared test project with C# | For a personal F# project with pure agent logic, Expecto's function-based tests require less ceremony. xUnit's `[<Fact>]` attributes trip F# 10's new attribute-target enforcement (`FS0842`) unless applied to `unit -> unit` functions. |
| Serilog | `Microsoft.Extensions.Logging` | ASP.NET Core apps with DI container | MEL requires a DI container and host. For a `dotnet run` CLI with no host, Serilog with `Log.Logger` is simpler. |

---

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| Newtonsoft.Json (`Json.NET`) | Legacy, no native DU support, heavier than STJ on .NET 10 | `System.Text.Json` + `FSharp.SystemTextJson` |
| openai-dotnet NuGet (`OpenAI` package) | Targets official OpenAI API; the localhost Qwen endpoint is OpenAI-compat but the SDK adds auth/retry/model assumptions that fight local usage | Raw `HttpClient` with hand-rolled request/response types |
| `FSharp.Data.HttpProvider` | Type provider for REST; overkill and not streaming-capable | `HttpClient` + `System.Net.ServerSentEvents` |
| `Flurl.Http` | Redundant abstraction for 1 endpoint; adds 2 NuGet deps | `HttpClient` |
| `async {}` CE for HTTP calls | `HttpClient` returns `Task<T>`; every call needs `.AsTask()` or `Async.AwaitTask` bridge, adding noise | `task {}` CE throughout |
| Source-generated `JsonSerializerContext` (AOT path) | Requires annotating every type; blueCode is `dotnet run` dev-mode only, reflection-based STJ works fine and is less boilerplate | Standard `JsonSerializer` with `JsonSerializerOptions` + FSharp.SystemTextJson converter |
| `Newtonsoft.Json.Schema` (paid) | Paid beyond basic use; ties you to Newtonsoft | `JsonSchema.Net` (free, System.Text.Json native) |
| `Microsoft.Extensions.Hosting` / `IHost` | ASP.NET Core dependency host; completely unnecessary for a CLI agent | No host. Entry point is a plain `[<EntryPoint>] let main argv` with `task {}` top-level. |
| Sub-agent / MCP libraries | Out of scope for v1 per PROJECT.md | Not applicable for v1 |

---

## Key F# Patterns for This Project

### 1. HTTP + SSE streaming (chat completions)

```fsharp
// Use task {} CE with HttpClient + System.Net.ServerSentEvents
open System.Net.Http
open System.Net.ServerSentEvents
open FSharp.Control

let streamChatCompletion (client: HttpClient) (request: ChatRequest) = taskSeq {
    let body = JsonSerializer.Serialize(request, options)
    use content = new StringContent(body, Encoding.UTF8, "application/json")
    use! response = client.PostAsync("/v1/chat/completions", content)
    use! stream = response.Content.ReadAsStreamAsync()
    let parser = SseParser.Create(stream, fun _ data -> data)
    for item in parser.EnumerateAsync() do
        yield item.Data
}
// Collect full response: TaskSeq.fold (fun acc chunk -> acc + chunk) "" tokenStream
```

**SSE note:** `System.Net.ServerSentEvents.SseParser` (inbox since .NET 9) parses the `data:` lines. For a non-streaming endpoint, just `HttpClient.PostAsync` + `ReadAsStringAsync`.

### 2. DU ↔ JSON with FSharp.SystemTextJson

```fsharp
// Tool action as DU
type ToolAction =
    | ReadFile of path: string
    | WriteFile of path: string * content: string
    | ListDir of path: string
    | RunShell of command: string
    | Final of answer: string

// LLM response schema
type LlmStep = {
    thought: string
    action: string   // "read_file" | "write_file" | "list_dir" | "run_shell" | "final"
    input: JsonElement  // varies per tool; parse after action dispatch
}

// JsonSerializerOptions — configure once, reuse
let jsonOptions =
    let o = JsonSerializerOptions(JsonSerializerDefaults.Web)
    o.Converters.Add(JsonFSharpConverter())   // FSharp.SystemTextJson
    o

// Strict mode for LLM output validation (new in .NET 10)
let strictOptions =
    let o = JsonSerializerOptions.Strict      // .NET 10: disallows unknown props, duplicates, null violations
    o.Converters.Add(JsonFSharpConverter())
    o
```

**DU serialization gotcha:** `FSharp.SystemTextJson` serializes DU cases as `{"Case": "...", "Fields": [...]}` by default. For the LLM output record (`LlmStep`), use plain F# record types, not DUs — the LLM emits `{"thought":..., "action":..., "input":...}` which maps to a record cleanly.

Use DUs for *internal* state (parsed `ToolAction`, `ModelChoice`, `AgentState`) not for the wire format the LLM produces.

### 3. Schema validation of LLM output

```fsharp
open Json.Schema   // JsonSchema.Net

let llmOutputSchema = JsonSchema.FromText("""
{
  "type": "object",
  "required": ["thought", "action", "input"],
  "properties": {
    "thought": {"type": "string"},
    "action":  {"type": "string", "enum": ["read_file","write_file","list_dir","run_shell","final"]},
    "input":   {"type": "object"}
  },
  "additionalProperties": false
}
""")

let validateLlmOutput (json: string) : Result<LlmStep, string> =
    let doc = JsonDocument.Parse(json)
    let result = llmOutputSchema.Evaluate(doc.RootElement)
    if result.IsValid then
        JsonSerializer.Deserialize<LlmStep>(json, jsonOptions) |> Ok
    else
        Error $"Schema violation: {result.Details}"
```

### 4. Agent loop with taskResult CE

```fsharp
open FsToolkit.ErrorHandling

type AgentError =
    | LlmHttpError of string
    | JsonParseError of string
    | SchemaError of string
    | ToolError of string
    | MaxLoopsReached

let runAgentLoop (ctx: AgentContext) : Task<Result<string, AgentError>> =
    taskResult {
        let mutable step = 0
        let mutable finished = false
        let mutable lastAnswer = ""
        while not finished && step < 5 do
            let! rawJson = callLlm ctx  // Task<Result<string, AgentError>>
            let! llmStep = validateLlmOutput rawJson |> Result.mapError SchemaError
            let! toolOutput = dispatchTool llmStep  // Task<Result<string, AgentError>>
            ctx.Memory.Add(llmStep, toolOutput)
            if llmStep.action = "final" then
                finished <- true
                lastAnswer <- string llmStep.input
            step <- step + 1
        if not finished then return! Error MaxLoopsReached
        return lastAnswer
    }
```

### 5. subprocess / run_shell

```fsharp
open System.Diagnostics

let runShell (command: string) (timeoutMs: int) : Task<Result<string, string>> = task {
    let psi = ProcessStartInfo("/bin/sh", $"-c \"{command}\"")
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    use proc = Process.Start(psi)
    use cts = new CancellationTokenSource(timeoutMs)
    try
        let! stdout = proc.StandardOutput.ReadToEndAsync(cts.Token)
        let! _ = proc.WaitForExitAsync(cts.Token)
        return Ok stdout
    with :? OperationCanceledException ->
        proc.Kill(entireProcessTree = true)
        return Error $"Timed out after {timeoutMs}ms"
}
```

**Gotcha:** Always read stdout/stderr *before* `WaitForExitAsync`, or use `BeginOutputReadLine`. Buffered stdout causes deadlocks on large output if you `WaitForExit` first. The pattern above (`ReadToEndAsync` concurrent with `WaitForExitAsync`) is safe.

---

## .NET 10 / F# 10 Specifics That Affect This Project

| Feature | Impact on blueCode |
|---------|--------------------|
| `JsonSerializerOptions.Strict` (new in .NET 10) | Use for LLM output deserialization — rejects unknown fields, duplicate keys, case mismatches. Zero extra library needed. |
| `and!` in `task {}` CE (new in F# 10 / .NET 10) | If you ever need to fire two LLM calls concurrently (future 32B + 72B parallel routing), `and!` replaces `Task.WhenAll` awkwardness. |
| `System.Net.ServerSentEvents` (inbox since .NET 9) | `SseParser.Create` directly on the HTTP response stream. No third-party SSE library needed. |
| Tail-call optimization in CEs (new in F# 10) | Agent loop written with recursive `taskResult {}` won't stack overflow at high iteration counts. |
| Parallel compilation preview (F# 10) | Add `<ParallelCompilation>true</ParallelCompilation>` + `<Deterministic>false</Deterministic>` to .fsproj for faster incremental builds. |
| Attribute target enforcement (F# 10) | `[<Fact>]` on non-function `let` bindings now warns (`FS0842`). Write Expecto tests as `unit -> unit` functions to avoid this. |
| `JsonSerializer.Deserialize` from `PipeReader` (new in .NET 10) | Not needed for v1 (string-based), but available for streaming JSON deserialization in future. |

---

## Version Compatibility

| Package | Version | Compatible .NET | Notes |
|---------|---------|-----------------|-------|
| FSharp.SystemTextJson | 1.4.36 | net10.0 (via netstandard2.0) | Last updated June 2025; F# 9 JsonNameAttribute fix included. |
| FsToolkit.ErrorHandling | 5.2.0 | net10.0 (computed) | Released Feb 2026. Targets net9.0 + netstandard2.0/2.1. |
| FSharp.Control.TaskSeq | 1.1.1 | net10.0 (via netstandard2.1) | Released Apr 2026. FSharp.Core >= 6.0.1 required. |
| JsonSchema.Net | 9.2.0 | net10.0 (computed, targets net8.0) | Released Apr 2026. |
| Spectre.Console | 0.55.2 | net10.0 (targets net8.0+) | Released Apr 2026. |
| Argu | 6.2.5 | net10.0 (via netstandard2.0) | Released Dec 2024. FSharp.Core >= 6.0.0 required. |
| Serilog | 4.3.1 | net10.0 (targets net6.0+) | Released Feb 2026. |

All packages verified compatible with .NET 10.0 via NuGet computed framework support.

---

## Stack Patterns by Scenario

**Strict JSON output enforcement (LLM → F# type):**
- Parse raw string → `JsonDocument.Parse`
- Validate with `JsonSchema.Net` schema (structural + enum check)
- Deserialize with `JsonSerializer.Deserialize<LlmStep>` + `FSharp.SystemTextJson` options
- Map `action` string → `ToolAction` DU in F# (not in JSON layer)

**Streaming token accumulation:**
- `HttpClient.PostAsync` with `stream` response
- `SseParser.Create(stream, ...)` → `IAsyncEnumerable<SseItem<string>>`
- `FSharp.Control.TaskSeq` operators (fold/toArray) to accumulate full response

**Model routing (32B vs 72B):**
- Define `type ModelChoice = Qwen32B | Qwen72B` DU
- `type ModelEndpoint = { baseUrl: string; model: string }`
- Pattern-match `ModelChoice` → endpoint; compile-time exhaustiveness check guarantees no routing gap

**Error propagation through agent loop:**
- All operations return `Task<Result<'T, AgentError>>`
- Compose with `taskResult {}` CE from `FsToolkit.ErrorHandling`
- Surface errors to UI layer only at top of loop; Spectre renders error panel

---

## Sources

- [What's new in .NET 10 Libraries](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries) — JSON Strict mode, PipeReader, AllowDuplicateProperties (official docs, HIGH confidence)
- [What's new in F# 10](https://learn.microsoft.com/en-us/dotnet/fsharp/whats-new/fsharp-10) — `and!` in task CE, tail-call CEs, attribute enforcement (official docs, HIGH confidence)
- [FSharp.SystemTextJson GitHub](https://github.com/Tarmil/FSharp.SystemTextJson) — v1.4.36, June 2025 (HIGH confidence)
- [FSharp.SystemTextJson NuGet](https://www.nuget.org/packages/FSharp.SystemTextJson/) — .NET 10 compatibility confirmed (HIGH confidence)
- [FsToolkit.ErrorHandling NuGet](https://www.nuget.org/packages/FsToolkit.ErrorHandling/) — v5.2.0, Feb 2026 (HIGH confidence)
- [FSharp.Control.TaskSeq NuGet](https://www.nuget.org/packages/FSharp.Control.TaskSeq/) — v1.1.1, Apr 2026 (HIGH confidence)
- [JsonSchema.Net NuGet](https://www.nuget.org/packages/JsonSchema.Net/) — v9.2.0, Apr 2026 (HIGH confidence)
- [Spectre.Console NuGet](https://www.nuget.org/packages/Spectre.Console/) — v0.55.2, Apr 2026 (HIGH confidence)
- [Argu NuGet](https://www.nuget.org/packages/Argu/) — v6.2.5, Dec 2024 (HIGH confidence)
- [Serilog NuGet](https://www.nuget.org/packages/Serilog/) — v4.3.1, Feb 2026 (HIGH confidence)
- [System.Net.ServerSentEvents — .NET 9/10 built-in](https://www.strathweb.com/2024/07/built-in-support-for-server-sent-events-in-net-9/) — inbox since .NET 9 (HIGH confidence)
- [task CE vs async CE recommendation](https://github.com/dotnet/docs/issues/34855) — dotnet/docs discussion (MEDIUM confidence — performance gap is small for IO-bound, but task CE is correct for interop)
- [Process deadlock / WaitForExitAsync](https://github.com/dotnet/runtime/issues/98347) — known gotcha confirmed in dotnet/runtime (HIGH confidence)

---

*Stack research for: F# / .NET 10 local LLM coding agent (blueCode)*
*Researched: 2026-04-22*
