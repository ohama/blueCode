# Architecture Research: blueCode

**Domain:** F# local-LLM coding agent (Qwen 32B/72B, Mac-only)
**Researched:** 2026-04-22
**Confidence:** HIGH

---

## Standard Architecture

### System Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         CLI Host (BlueCode.Cli)                  │
│   Entry point, REPL, rendering, argument parsing                 │
├─────────────────────────────────────────────────────────────────┤
│                         Agent Core (BlueCode.Core)               │
│  ┌─────────────┐  ┌─────────────┐  ┌──────────┐  ┌──────────┐  │
│  │  AgentLoop  │  │  Router     │  │ ToolReg  │  │ Context  │  │
│  │  (DU state) │  │  (Intent→  │  │ (static  │  │ (ring    │  │
│  │             │  │   Model DU) │  │  map)    │  │ buffer)  │  │
│  └──────┬──────┘  └──────┬──────┘  └────┬─────┘  └─────┬────┘  │
│         │                │              │               │       │
│  ┌──────▼──────────────────────────────▼───────────────▼────┐   │
│                   Domain Types (DU spine)                    │   │
│      AgentState | Message | LlmOutput | Tool | AgentError    │   │
│  └────────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────────┤
│                         Ports (Interfaces)                       │
│  ┌──────────────────────┐   ┌────────────────────────────────┐  │
│  │  ILlmClient          │   │  IToolExecutor                 │  │
│  │  (chat→Task<Result>) │   │  (execute→Task<Result>)        │  │
│  └──────────┬───────────┘   └──────────────┬─────────────────┘  │
├─────────────┼───────────────────────────────┼───────────────────┤
│                         Adapters                                 │
│  ┌──────────▼───────────┐   ┌──────────────▼─────────────────┐  │
│  │ QwenHttpClient       │   │  FsToolExecutor                │  │
│  │ (SSE, HTTP, Task)    │   │  (ReadFile | WriteFile | ...)  │  │
│  └──────────────────────┘   └────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility | Communicates With |
|-----------|---------------|-------------------|
| `AgentLoop` | State machine: runs one session, tracks loop count, sequences steps | `ILlmClient`, `IToolExecutor`, `ContextBuffer`, `Router` |
| `Router` | Pure `Intent -> Model` function; `classifyIntent: string -> Intent` | `AgentLoop` (called before each LLM invocation) |
| `ToolRegistry` | Static map of `ToolName -> (ToolInput -> Task<Result<ToolOutput, ToolError>>)` | `AgentLoop` dispatches into it |
| `ContextBuffer` | Sliding window of last N `Step` records; serialized as `Message list` for LLM | `AgentLoop` reads/writes |
| `ILlmClient` | Port: send `Message list` → stream/complete → `Result<LlmOutput, AgentError>` | `AgentLoop` |
| `IToolExecutor` | Port: `Tool -> Task<Result<ToolOutput, AgentError>>` | `AgentLoop` |
| `QwenHttpClient` | Adapter: HTTP POST to localhost:8000/8001, SSE streaming, JSON parsing | Implements `ILlmClient` |
| `FsToolExecutor` | Adapter: real filesystem/shell operations | Implements `IToolExecutor` |
| `CLI` | REPL loop, prompt rendering, mode toggling (verbose/compact), signal handling | `AgentLoop` |

---

## Recommended Project Structure

Two-project solution. One `Core` library, one `Cli` host. This separates testable domain logic from IO entry point without the overhead of a full layered solution.

```
blueCode.sln
├── src/
│   ├── BlueCode.Core/              # Pure domain + ports. No Console, no Main.
│   │   ├── BlueCode.Core.fsproj
│   │   ├── Domain.fs               # All DUs: AgentState, Message, LlmOutput, Tool, AgentError, Intent, Model
│   │   ├── Router.fs               # classifyIntent, intentToModel — pure functions
│   │   ├── ContextBuffer.fs        # RingBuffer<Step>, serialize to Message list
│   │   ├── Ports.fs                # ILlmClient, IToolExecutor interfaces
│   │   ├── ToolRegistry.fs         # Tool DU → handler map, static registry
│   │   ├── AgentLoop.fs            # runSession: config → ILlmClient → IToolExecutor → string → Task<Result<AgentResult, AgentError>>
│   │   └── Rendering.fs            # renderStep: Step -> RenderMode -> string  (pure, no Console)
│   └── BlueCode.Cli/               # Impure shell: Console, process lifecycle, DI wiring
│       ├── BlueCode.Cli.fsproj
│       ├── CompositionRoot.fs      # Wire QwenHttpClient + FsToolExecutor + config
│       ├── Repl.fs                 # Multi-turn REPL loop
│       ├── Adapters/
│       │   ├── QwenHttpClient.fs   # ILlmClient implementation (HTTP + SSE)
│       │   └── FsToolExecutor.fs   # IToolExecutor implementation (filesystem + Process)
│       └── Program.fs              # [<EntryPoint>] main
└── tests/
    └── BlueCode.Core.Tests/
        ├── BlueCode.Core.Tests.fsproj
        ├── RouterTests.fs
        ├── AgentLoopTests.fs       # Inject fake ILlmClient + IToolExecutor
        └── ContextBufferTests.fs
```

### Structure Rationale

- **Domain.fs first in Core:** F# compiles files in declaration order. All DUs defined first, consumed later. No circular deps.
- **No DI container:** Composition root in `CompositionRoot.fs` wires dependencies by calling constructors and passing interfaces as function arguments. F# functions-as-values eliminate the need for a DI framework.
- **Rendering.fs in Core (pure):** Takes a `Step` and returns a `string`. CLI calls it and then prints. Testable without Console.
- **Adapters/ under Cli:** Real I/O lives here. Tests mock the interfaces, never touch the adapters.

---

## Core F# Type Signatures

### Domain.fs — The DU Spine

```fsharp
// ── Model routing ────────────────────────────────────────────────
type Intent =
    | Debug          // "error", "bug", "fix", "traceback"
    | Design         // "architecture", "design", "구조"
    | Analysis       // "analyze", "compare", "tradeoff"
    | Implementation // "write", "implement", "code"
    | General

type Model =
    | Qwen32B   // port 8000 — fast, code generation
    | Qwen72B   // port 8001 — reasoning, design, debug

type Endpoint =
    | Port8000
    | Port8001

// ── Message protocol (OpenAI schema) ─────────────────────────────
type MessageRole =
    | System
    | User
    | Assistant
    | Tool of toolCallId: string   // role="tool", carries the call ID

type Message = {
    Role    : MessageRole
    Content : string
}

// ── LLM structured output ────────────────────────────────────────
type Thought = Thought of string
type ToolName = ToolName of string    // single-case DU: no raw strings
type ToolInput = ToolInput of Map<string, string>

type LlmOutput =
    | ToolCall of ToolName * ToolInput    // {"action": "read_file", "input": {...}}
    | FinalAnswer of string               // {"action": "final", "input": {"answer": "..."}}

// Raw JSON struct from Qwen:  { thought: string; action: string; input: obj }
// Parsed into LlmOutput via tryParseLlmOutput : string -> Result<LlmOutput, AgentError>

// ── Tool system ───────────────────────────────────────────────────
type FilePath = FilePath of string      // validated single-case DU
type Command  = Command  of string
type Timeout  = Timeout  of int         // seconds

type Tool =
    | ReadFile  of FilePath
    | WriteFile of FilePath * content: string
    | ListDir   of FilePath
    | RunShell  of Command * Timeout

type ToolOutput = ToolOutput of string  // raw text result

// ── Step record (one iteration of the loop) ──────────────────────
type StepStatus = Success | Failed of string | Aborted

type Step = {
    StepNumber : int          // 1..5
    Thought    : Thought
    Action     : LlmOutput
    ToolOutput : ToolOutput option
    Status     : StepStatus
}

// ── Agent state machine ───────────────────────────────────────────
type AgentState =
    | AwaitingUserInput
    | PromptingLlm      of loopCount: int     // 0..4
    | AwaitingApproval  of Tool               // future: tool approval gate
    | ExecutingTool     of Tool * loopCount: int
    | Observing         of Step * loopCount: int
    | Complete          of finalAnswer: string
    | MaxLoopsHit                              // loopCount = 5
    | Failed            of AgentError

// ── Error domain ─────────────────────────────────────────────────
type AgentError =
    | LlmUnreachable    of endpoint: string * detail: string
    | InvalidJsonOutput of raw: string         // Qwen returned non-JSON
    | UnknownTool       of ToolName
    | ToolFailure       of Tool * exn
    | MaxLoopsExceeded                         // structural, not runtime error
    | CancelledByUser

// ── Session result ───────────────────────────────────────────────
type AgentResult = {
    FinalAnswer : string
    Steps       : Step list
    LoopCount   : int
    Model       : Model
}
```

### Router.fs — Pure Functions

```fsharp
module Router

// Pattern matching over keyword sets — no IO, trivially testable.
let classifyIntent (userInput: string) : Intent =
    let s = userInput.ToLowerInvariant()
    if   ["error";"bug";"fix";"debug";"traceback";"exception"] |> List.exists s.Contains then Debug
    elif ["design";"architecture";"system";"구조";"설계"]       |> List.exists s.Contains then Design
    elif ["analyze";"compare";"tradeoff";"difference";"분석"]   |> List.exists s.Contains then Analysis
    elif ["write";"implement";"code";"example"]                 |> List.exists s.Contains then Implementation
    else General

// Total, deterministic, one-liner invariant.
let intentToModel : Intent -> Model = function
    | Debug | Design | Analysis -> Qwen72B
    | Implementation | General  -> Qwen32B

// Endpoint follows model — closed to extension, changes in one place.
let modelToEndpoint : Model -> Endpoint = function
    | Qwen32B -> Port8000
    | Qwen72B -> Port8001

let endpointToUrl : Endpoint -> string = function
    | Port8000 -> "http://127.0.0.1:8000/v1/chat/completions"
    | Port8001 -> "http://127.0.0.1:8001/v1/chat/completions"
```

### Ports.fs — Interface Contracts

```fsharp
module Ports

// LLM port: decouples AgentLoop from HTTP transport.
// CancellationToken passed explicitly (task {} doesn't propagate implicitly).
type ILlmClient =
    abstract member CompleteAsync :
        messages: Message list
        -> model: Model
        -> ct: System.Threading.CancellationToken
        -> System.Threading.Tasks.Task<Result<LlmOutput, AgentError>>

// Tool execution port: decouples AgentLoop from real filesystem / shell.
type IToolExecutor =
    abstract member ExecuteAsync :
        tool: Tool
        -> ct: System.Threading.CancellationToken
        -> System.Threading.Tasks.Task<Result<ToolOutput, AgentError>>
```

### AgentLoop.fs — State Machine Core

```fsharp
module AgentLoop

// Config is a record, not a god-object class. No mutation after construction.
type AgentConfig = {
    MaxLoops    : int    // = 5, invariant enforced structurally
    Model       : Model  // resolved by Router before calling runSession
    SystemPrompt: string
}

// "Turn" = one user input → one final answer (possibly via N ≤ 5 tool steps).
// "Step" = one iteration within a turn (one LLM call + optional one tool call).

// The loop is a tail-recursive async function, not a while loop with mutable state.
// loopCount is threaded explicitly — makes MaxLoopsHit a type-level invariant.
val runSession :
    config  : AgentConfig
    -> client : ILlmClient
    -> tools  : IToolExecutor
    -> ctx    : ContextBuffer
    -> input  : string
    -> ct     : System.Threading.CancellationToken
    -> System.Threading.Tasks.Task<Result<AgentResult, AgentError>>

// Internal step function (not exported).
// Returns Result<Step, AgentError>; loop threads Result through steps.
val private runStep :
    config    : AgentConfig
    -> client : ILlmClient
    -> tools  : IToolExecutor
    -> ctx    : ContextBuffer
    -> loopN  : int          // 0-indexed; >= config.MaxLoops → MaxLoopsExceeded
    -> ct     : System.Threading.CancellationToken
    -> System.Threading.Tasks.Task<Result<Step, AgentError>>
```

**Loop sketch (not full implementation — signatures only):**

```fsharp
// The recursive loop expressed cleanly with task {} + Result binding.
// F# 10 and! allows concurrent awaits where useful.
let rec private loop config client tools ctx loopN ct =
    task {
        if loopN >= config.MaxLoops then
            return Error MaxLoopsExceeded
        else
            let messages = ContextBuffer.toMessages ctx
            let! llmResult = client.CompleteAsync messages config.Model ct
            match llmResult with
            | Error e -> return Error e
            | Ok (FinalAnswer ans) ->
                return Ok (AgentResult { FinalAnswer = ans; ... })
            | Ok (ToolCall (name, input)) ->
                let! toolResult =
                    name
                    |> ToolRegistry.resolve  // Result<Tool, AgentError>
                    |> Result.mapError id
                    |> Result.map (fun tool -> tools.ExecuteAsync tool ct)
                    |> ...
                // append step, recurse
                return! loop config client tools ctx' (loopN + 1) ct
    }
```

### ContextBuffer.fs — Ring Buffer

```fsharp
module ContextBuffer

// Fixed-capacity sliding window. Immutable — returns new buffer on append.
type RingBuffer<'a> = private {
    Capacity : int
    Items    : 'a list   // newest-first, trimmed to capacity
}

type ContextBuffer = RingBuffer<Step>

val create  : capacity: int -> ContextBuffer
val append  : step: Step -> buf: ContextBuffer -> ContextBuffer
val toMessages : buf: ContextBuffer -> Message list
// Converts Steps into Message list for LLM context window.
// System prompt is prepended by AgentLoop, not ContextBuffer.
```

---

## Architectural Patterns

### Pattern 1: DU as Closed State Machine

**What:** Represent every valid agent state as a DU case. Illegal states are not representable.

**When to use:** Agent loop, turn lifecycle, tool dispatch.

**Trade-offs:** Exhaustive pattern matches are compile-checked. Adding a new state forces all match sites to update. This is the intended friction — the compiler catches gaps.

**Example:**
```fsharp
// Transition function is total: every state has a defined next state.
let transition (state: AgentState) (event: AgentEvent) : AgentState =
    match state, event with
    | PromptingLlm n, GotToolCall tool      -> ExecutingTool (tool, n)
    | PromptingLlm _, GotFinalAnswer ans    -> Complete ans
    | ExecutingTool (_, n), ToolSucceeded _ -> Observing (step, n)
    | Observing (_, n), Continue            -> PromptingLlm (n + 1)
    | Observing (_, 4), Continue            -> MaxLoopsHit   // n=4 means next would be 5
    | _,                UserCancelled       -> Failed CancelledByUser
    // ... all cases exhaustive
```

### Pattern 2: Single-Case DU for Primitive Obsession Elimination

**What:** Wrap primitives in single-case DUs so `FilePath "../../etc/passwd"` and `FilePath "/valid/path"` are structurally the same type but only the smart constructor's validation determines validity.

**When to use:** All tool parameters — `FilePath`, `Command`, `ToolName`.

**Trade-offs:** Slightly more verbose at call sites. Eliminates entire class of type confusion bugs (passing a `Command` where a `FilePath` is expected won't compile).

**Example:**
```fsharp
// Smart constructor — validation at boundary, not at use site.
module FilePath =
    let tryCreate (raw: string) : Result<FilePath, string> =
        if raw |> System.IO.Path.IsPathRooted || raw.Contains ".." then
            Error $"Unsafe path: {raw}"
        else
            Ok (FilePath raw)

    let value (FilePath p) = p
```

### Pattern 3: Function Injection over Interface Implementation

**What:** At the composition root, wire dependencies by passing functions, not by implementing heavyweight interfaces. Interfaces (`ILlmClient`, `IToolExecutor`) exist for the formal boundary but are composed from plain functions.

**When to use:** Anywhere AgentLoop needs external behavior. Especially useful for tests.

**Trade-offs:** Less ceremony than DI containers. Object expressions in F# make interface implementation lightweight.

**Example:**
```fsharp
// In tests: fake client as object expression, no mocking framework needed.
let fakeLlmClient (responses: LlmOutput list) : ILlmClient =
    let mutable remaining = responses
    { new ILlmClient with
        member _.CompleteAsync _ _ _ =
            task {
                match remaining with
                | [] -> return Error (LlmUnreachable ("fake", "no more responses"))
                | h :: t ->
                    remaining <- t
                    return Ok h
            } }
```

### Pattern 4: task {} with Explicit CancellationToken

**What:** Use `task { }` (not `async { }`) throughout because the entire stack interoperates with .NET `Task<T>`. Pass `CancellationToken` explicitly as a parameter — `task {}` does not propagate cancellation implicitly.

**When to use:** All I/O operations (LLM HTTP call, file read/write, shell process).

**Trade-offs:** More verbose than `async {}` cancellation (which is implicit). Gains: better .NET interop, `and!` for concurrent awaits in F# 10, better debugger support, no unbounded tail-call stack (use explicit loop, not `return!`).

**Example:**
```fsharp
// Explicit CancellationToken threading — required for long-running shell calls.
member _.ExecuteAsync (tool: Tool) (ct: CancellationToken) =
    task {
        match tool with
        | RunShell (Command cmd, Timeout secs) ->
            use cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
            cts.CancelAfter(TimeSpan.FromSeconds(float secs))
            let! output = runProcessAsync cmd cts.Token
            return Ok (ToolOutput output)
        | ReadFile (FilePath p) ->
            let! text = File.ReadAllTextAsync(p, ct)
            return Ok (ToolOutput text)
        ...
    }
```

### Pattern 5: Result Flow — When to Use, When Not

**Use Result for:**
- LLM response parsing (`tryParseLlmOutput`)
- Tool parameter validation (`FilePath.tryCreate`)
- HTTP response interpretation (non-2xx, malformed JSON)
- The entire `runSession` return type

**Use exceptions for:**
- True programmer errors (index out of bounds, unmatched case that "can't happen")
- .NET interop where the caller expects exceptions
- Within `task {}` — exceptions propagate naturally and surface as faulted tasks; convert at the boundary to `Result` via `task { try return! ... with ex -> return Error (...) }`

**Do NOT railway every internal helper.** The "Against ROP" principle applies: use `Result` where the caller needs to branch on success/failure. Internal implementation details use exceptions that get caught once at the loop boundary.

---

## Data Flow

### Request Flow (One Turn)

```
[CLI: user types input]
         ↓
[Router.classifyIntent → Intent]
         ↓
[Router.intentToModel → Model]
         ↓
[AgentLoop.runSession (loopCount=0)]
         ↓
[ContextBuffer.toMessages → Message list]
         ↓
[ILlmClient.CompleteAsync → Result<LlmOutput, AgentError>]
         ↓
    match LlmOutput with
    ├── FinalAnswer → return Ok AgentResult
    └── ToolCall    ↓
[ToolRegistry.resolve Tool DU]
         ↓
[IToolExecutor.ExecuteAsync → Result<ToolOutput, AgentError>]
         ↓
[build Step record]
         ↓
[ContextBuffer.append step → new buffer]
         ↓
[AgentLoop.runSession (loopCount+1)]   ← recurse (max 5 times)
         ↓
[CLI: render Step via Rendering.renderStep]
```

### Key Data Flow Properties

1. **Immutable state threading:** `ContextBuffer` is immutable. Each iteration passes a new buffer. No mutable fields in `AgentLoop`.
2. **Result short-circuits:** Any `Error` from LLM or Tool stops the loop — no silent swallowing.
3. **loopCount as parameter, not field:** Makes `MaxLoopsHit` structurally impossible to miss. The recursive call `loop (loopN + 1)` is the only place the count increments.
4. **Message serialization is lazy:** `ContextBuffer.toMessages` is called once per loop iteration, not on every step append. The buffer stores typed `Step` records; serialization to `Message list` is deferred to LLM call time.

---

## Component Build Order

F# within a project compiles in file order (top to bottom in `.fsproj`). Between projects, `Core` must compile before `Cli`.

### Build Order Implications

```
Phase 1 (must exist first — no dependencies):
  Domain.fs          ← All DUs. Everything depends on this.
  Router.fs          ← Depends only on Domain.fs.

Phase 2 (depends on Domain):
  Ports.fs           ← ILlmClient, IToolExecutor interfaces.
  ContextBuffer.fs   ← Depends on Domain.fs (Step, Message).
  ToolRegistry.fs    ← Depends on Domain.fs (Tool DU).
  Rendering.fs       ← Depends on Domain.fs (Step, RenderMode).

Phase 3 (depends on Phase 1 + 2):
  AgentLoop.fs       ← Depends on Ports, ContextBuffer, ToolRegistry, Router.

Phase 4 (Cli project — depends on Core):
  QwenHttpClient.fs  ← Implements ILlmClient. Depends on Core.Ports, Core.Domain.
  FsToolExecutor.fs  ← Implements IToolExecutor. Depends on Core.Ports, Core.Domain.
  CompositionRoot.fs ← Wires everything together.
  Repl.fs            ← Depends on AgentLoop, Rendering, CompositionRoot.
  Program.fs         ← [<EntryPoint>] — last.
```

### What Can Be Built in Parallel (by developer, not compiler)

| Parallel stream A | Parallel stream B |
|-------------------|-------------------|
| `ContextBuffer.fs` | `Router.fs` |
| `QwenHttpClient.fs` | `FsToolExecutor.fs` |
| `Rendering.fs` | `ToolRegistry.fs` |

These pairs share no inter-module dependency. `AgentLoop.fs` and `Repl.fs` are the serialization points — they integrate all other modules.

---

## Top 3 Type-Safety Wins

### Win 1: The Loop Count as Invariant

In Python (`claw-code-agent`), `max_turns` is a runtime check inside a `while` loop. A missed `continue` or `break` can silently violate the invariant.

In blueCode, `loopN` is a parameter to the recursive function. Reaching `config.MaxLoops` returns `Error MaxLoopsExceeded` — the type system guarantees this path is handled at the match site. No silent infinite loops.

```fsharp
// Compiler forces the caller to handle MaxLoopsExceeded.
match! runSession config client tools ctx input ct with
| Ok result         -> renderResult result
| Error MaxLoopsExceeded -> eprintfn "Max loops hit"
| Error (LlmUnreachable _) -> eprintfn "Model unreachable"
// Exhaustive match — missing a case is a compile error, not a runtime surprise.
```

### Win 2: Tool Dispatch Without Stringly-Typed Dispatch

In Python, tools are dispatched by string name: `execute_tool("read_file", {"path": "foo"})`. A typo silently falls through to an error handler.

In blueCode, `Tool` is a DU. `ToolRegistry.resolve` returns `Result<Tool, AgentError>` keyed on `ToolName`. Once resolved, the match inside `IToolExecutor.ExecuteAsync` is exhaustive — adding a new tool case forces all match sites to handle it.

```fsharp
// Every case must be handled. New tool → compile error until handled.
match tool with
| ReadFile  path          -> ...
| WriteFile (path, text)  -> ...
| ListDir   path          -> ...
| RunShell  (cmd, timeout)-> ...
// No default "unknown tool" branch needed — Tool DU is closed.
```

### Win 3: LLM Output Parsed Once, Typed Everywhere

In Python, the raw JSON dict is passed around. Consumers re-check keys at use sites.

In blueCode, `tryParseLlmOutput : string -> Result<LlmOutput, AgentError>` happens once, at the LLM boundary. After that, the loop only sees `LlmOutput` — either `ToolCall (ToolName * ToolInput)` or `FinalAnswer string`. No downstream consumer can accidentally treat a `FinalAnswer` as a tool call.

```fsharp
type LlmOutput =
    | ToolCall    of ToolName * ToolInput
    | FinalAnswer of string
// If Qwen returns invalid JSON: Error (InvalidJsonOutput raw) — loop aborts cleanly.
// If Qwen returns valid JSON with "action": "final": Ok (FinalAnswer answer).
// Consumers can never access tool name without going through LlmOutput match.
```

---

## Anti-Patterns

### Anti-Pattern 1: Mutable Agent State

**What people do:** Use a mutable `agentState` field or `ref` to track loop iteration, append to a mutable message list.

**Why it's wrong:** Makes reasoning about the loop non-local. A concurrent access or missed reset causes phantom state from prior turns. Testing requires resetting state between runs.

**Do this instead:** Thread immutable state as function parameters. `ContextBuffer` is a value that the caller replaces, not a reference the callee mutates.

### Anti-Pattern 2: Stringly-Typed Tool Dispatch

**What people do:** `toolRegistry["read_file"](args)` where both key and args are untyped strings/dicts (as in the Python reference).

**Why it's wrong:** Typos in tool names are silent runtime errors. Parameters are not validated at dispatch time. New tools can be added without updating all consumers.

**Do this instead:** `Tool` DU as the dispatch key. `ToolRegistry.resolve : ToolName -> Result<Tool, AgentError>` validates the name. The `Tool` DU carries typed parameters — the type checker enforces them.

### Anti-Pattern 3: Using `async {}` for HTTP Calls

**What people do:** Use `async { }` + `Async.AwaitTask` wrappers everywhere to keep the F# async model.

**Why it's wrong:** `System.Net.Http.HttpClient` and `System.IO.File` are `Task`-returning. Every `Async.AwaitTask` wrapper is boilerplate and an allocation. `async {}` tail-call support is not needed here (no recursive async chains — the loop is explicit `task {}` recursion avoided via explicit `for`/`while`).

**Do this instead:** `task { }` throughout the adapter layer. Convert to `Async` only if a consumer explicitly needs it (none in v1).

### Anti-Pattern 4: Transliterated Python Class Hierarchy

**What people do:** Port Python's `LocalCodingAgent` class with 30 mutable fields as an F# record or class, preserving the mutation pattern.

**Why it's wrong:** Python's `__post_init__` with 15 `if self.X is None: self.X = default()` becomes a dangerous initialization ordering problem in F#. Mutable fields defeat the type safety wins.

**Do this instead:** Composition root wires concrete implementations. `AgentConfig` is a small, immutable record. `ILlmClient` and `IToolExecutor` are injected, not constructed inside the agent. No `__post_init__` equivalent needed.

### Anti-Pattern 5: Exception-Based Error Propagation Inside the Loop

**What people do:** Let exceptions from JSON parsing or HTTP errors propagate up to a top-level `try/catch`.

**Why it's wrong:** Exceptions bypass the `Result` flow. The loop can't tell the difference between "model returned invalid JSON" (retry?) and "disk full" (abort). The caller gets no structured error information.

**Do this instead:** Convert at the boundary. `QwenHttpClient` catches `HttpRequestException` and maps it to `Error (LlmUnreachable (...))`. `FsToolExecutor` catches `IOException` and maps it to `Error (ToolFailure (tool, ex))`. The loop sees only `Result<..., AgentError>`.

---

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| Qwen 32B (port 8000) | HTTP POST `application/json`, optional SSE | `QwenHttpClient` handles both streaming and non-streaming. v1: non-streaming is simpler. |
| Qwen 72B (port 8001) | Same as above, different URL | Same client, different `Endpoint` DU case |
| Filesystem | `System.IO.File.*Async` + `Directory.GetFiles` | `FsToolExecutor` — all calls explicit `CancellationToken` |
| Shell | `System.Diagnostics.Process` with timeout | `RunShell` case — `CancellationTokenSource.CreateLinkedTokenSource` enforces per-call timeout |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| `AgentLoop` ↔ `ILlmClient` | `Task<Result<LlmOutput, AgentError>>` | Only interface call — testable |
| `AgentLoop` ↔ `IToolExecutor` | `Task<Result<ToolOutput, AgentError>>` | Only interface call — testable |
| `AgentLoop` ↔ `ContextBuffer` | Pure function call (returns new `ContextBuffer`) | No IO |
| `AgentLoop` ↔ `Router` | Pure function call (`string -> Model`) | No IO |
| `Cli.Repl` ↔ `AgentLoop` | `Task<Result<AgentResult, AgentError>>` | Boundary between impure REPL and pure-ish loop |
| `Cli.Repl` ↔ `Rendering` | Pure function call (`Step -> string`) | Print happens in Repl, not in Rendering |

---

## Scaling Considerations

This is a single-user, single-machine tool. Traditional scaling axes do not apply. The relevant "scaling" is complexity scaling as features are added.

| Scale | Architecture Adjustments |
|-------|--------------------------|
| v1 (4 tools, 1 user) | Single project viable; 2-project chosen for testability, not performance |
| v2 (session persistence) | Add `SessionStore.fs` to Core; `ContextBuffer` serialization already decoupled |
| v2 (slash commands) | Add `SlashCommand` DU to Domain.fs; CLI parses before dispatching to AgentLoop |
| v2 (sub-agents) | `AgentLoop.runSession` is already a pure function — compose multiple calls; no architectural change |
| v3 (MCP/LSP) | Add new adapter implementing `IToolExecutor`; AgentLoop unchanged |

### First bottleneck

Context window size. The `ContextBuffer` ring-buffer is the knob. If 5-step windows with verbose tool outputs fill Qwen's context, reduce capacity or add step summarization. The buffer's typed `Step list` makes summarization pluggable without touching the loop.

---

## Suggested F# Source File Order (BlueCode.Core.fsproj)

```xml
<ItemGroup>
  <Compile Include="Domain.fs" />
  <Compile Include="Router.fs" />
  <Compile Include="Ports.fs" />
  <Compile Include="ContextBuffer.fs" />
  <Compile Include="ToolRegistry.fs" />
  <Compile Include="Rendering.fs" />
  <Compile Include="AgentLoop.fs" />
</ItemGroup>
```

```xml
<!-- BlueCode.Cli.fsproj — references Core -->
<ItemGroup>
  <ProjectReference Include="../BlueCode.Core/BlueCode.Core.fsproj" />
  <Compile Include="Adapters/QwenHttpClient.fs" />
  <Compile Include="Adapters/FsToolExecutor.fs" />
  <Compile Include="CompositionRoot.fs" />
  <Compile Include="Repl.fs" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

---

## Sources

- [F# Task Expressions — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/task-expressions) — `task {}` vs `async {}`, `and!` for concurrent awaits (F# 10), explicit cancellation token guidance
- [Functional Architecture is Ports and Adapters — Mark Seemann](https://blog.ploeh.dk/2016/03/18/functional-architecture-is-ports-and-adapters/) — function injection over DI containers, pure core + impure shell
- [Designing with types: Single case union types — F# for Fun and Profit](https://fsharpforfunandprofit.com/posts/designing-with-types-single-case-dus/) — smart constructors, primitive obsession elimination
- [Against Railway-Oriented Programming — Scott Wlaschin](https://fsharpforfunandprofit.com/posts/against-railway-oriented-programming/) — when Result is appropriate vs. exceptions
- [claw-code-agent reference: agent_types.py, agent_runtime.py, openai_compat.py, permissions.py] — Python reference architecture, translated to F# idioms (not ported)
- [blueCode localLLM design notes: qwen_agent_rewrite.md, qwen_claude_full_design.md, agent_32b_72b_codegpt.md] — user's existing intent classification and routing logic

---
*Architecture research for: blueCode F# local-LLM coding agent*
*Researched: 2026-04-22*
