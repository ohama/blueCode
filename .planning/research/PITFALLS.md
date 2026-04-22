# Pitfalls Research

**Domain:** F# local-LLM coding agent (Qwen 32B/72B rewrite)
**Researched:** 2026-04-22
**Confidence:** HIGH — project-specific, verified against Python source, Qwen docs, vLLM issues, and F# language references

---

## Category A: Rewrite Pitfalls

### A-1: Second-System Effect — Feature Scope Explosion

**What goes wrong:**
The F# rewrite starts clean, but the user's awareness of "everything claw-code-agent can do" creates pull to port things that were never part of the stated v1 scope. Session persistence, sub-agents, slash commands, context compaction, and MCP all exist in the Python version. Each one looks tempting to add "while we're building this anyway."

**Why it happens:**
The rewriter understands the old system in full now. That understanding feels like a mandate to reproduce it. The second system gets "improved" with every addition that felt like a limitation in v1. The design document says "Out of Scope" but implementation temptation is different from planning discipline.

**How to avoid:**
Enforce a hard scope gate on the PROJECT.md "Out of Scope (v1)" list. When any feature outside that list appears in a PR or design session, write it down in a DEFERRED.md file and close the issue. Do not implement it. The test of the rewrite is: does the agent take a user task, call 4 tools, respect max-5 loops, and produce output? That is the full v1 success criterion.

**Warning signs:**
- Any module that doesn't map directly to: CLI entry, HTTP client, agent loop, 4 tools, JSON parser, or step renderer
- A PR description containing the words "while we're at it" or "might as well add"
- The phrase "let's generalize this" in a code review

**Phase to address:**
v1 minimum — scope gate belongs in the initial project definition, which is already done. Verify at each phase that no new out-of-scope modules were introduced.

---

### A-2: Chesterton's Fence — Ignoring Why Python Did X

**What goes wrong:**
The Python bash_security.py has 1200 lines of validation logic — 18 separate validator functions, quote-level state machines, a split_command parser, and special handling for jq, git commit messages, IFS injection, brace expansion, unicode whitespace, and carriage return. This looks like accidental complexity. It is not. Each validator addresses a real bypass that was discovered through testing or use. Porting `run_shell` in F# without porting the security logic is a Chesterton's fence violation: the fence looks pointless until you remove it.

**Why it happens:**
The Python code looks large and intimidating. The user's design philosophy is "remove complexity." `run_shell` sounds like `Process.Start(cmd)`. The security validators read like paranoid boilerplate. The project description says "personal use only" which reduces perceived need.

**How to avoid:**
Before implementing `run_shell`, read bash_security.py in full. Port the `SecurityBehavior` DU and the chain of validators as F# functions. The Python implementation already did the hard reasoning work — port the logic, not just the surface API. Specifically, the validators that must be preserved: control characters, command substitution patterns (`$(...)`, backtick), IFS injection, redirections, and dangerous patterns. The "ASK" behavior maps to either "deny with message" or "prompt user" in the F# CLI.

**Warning signs:**
- `run_shell` implemented as a thin wrapper around `Process.Start` with zero pre-execution checks
- No `SecurityResult` or equivalent type in the shell tool module
- First test case for run_shell being `ls -la` (reads fine) rather than `cat /etc/passwd; echo $(whoami)`

**Phase to address:**
v1 minimum — `run_shell` is a v1 tool. Security validation must be present before any test of run_shell, not added later.

---

### A-3: Translation Trap — Thinking in Python While Writing F#

**What goes wrong:**
The Python agent uses mutable dicts for message history, appending to a list in a loop. Translating this directly produces `mutable history = []` and imperative mutation inside the agent loop. This is syntactically valid F# but fights the language — it loses the benefits of immutable state threading and makes the loop harder to reason about.

Similarly, Python's `dict` for JSON parsing becomes `Map` in F#, but Python dict ordering (insertion order since Python 3.7) is not replicated by F# `Map` (sorted by key). If JSON fields are expected in a specific order for prompts, this silently breaks output.

**Why it happens:**
The user knows the Python code. The path of least resistance when stuck on an F# pattern is to transliterate the Python. Mutable state "just works" in F# even though it defeats the purpose.

**How to avoid:**
Model the agent loop as a fold over steps, not an imperative mutation loop. The agent state is `AgentState` — an F# record — and each step produces a new state. Use `List.fold` or a recursive function pattern instead of `while mutable`. For JSON parsing, use `JsonDocument` or a typed DTO record, never `Map<string,obj>` with ordering assumptions.

**Warning signs:**
- `mutable` appearing in agent loop or state management code
- `Map.ofList` used to represent a JSON object where field order matters
- Copy-pasting Python comment blocks into F# files as "translation notes"

**Phase to address:**
v1 minimum — the loop design is the core of the project. Getting this right in v1 prevents a painful refactor later.

---

### A-4: Deferred Delivery — Rewriting Forever, Never Using

**What goes wrong:**
The Python agent works today. The F# rewrite is "almost ready" for months. The user keeps using the Python agent while building the F# one. The F# agent never gets daily use. Without daily use, bugs hide and the rewrite never gets the feedback loop that makes it stable. Eventually the project stalls or the user decides the Python one is fine.

**Why it happens:**
Personal tool rewrites have no external deadline. There is no user complaining. The Python agent handles edge cases that the F# one hasn't hit yet, so switching feels premature. "One more feature before I switch" is a common mental pattern.

**How to avoid:**
Set a date — not a feature gate — when you switch to using the F# agent exclusively for at least one real coding task per day. The date should be when the minimum loop (HTTP call → JSON parse → one tool → output) works end-to-end, even if imperfect. Use it broken. Fix it live. Put the Python agent in a "retired" directory rather than deleting it.

**Warning signs:**
- The Python agent is still being launched for real work after week 2 of the F# rewrite
- The F# agent has only been tested with synthetic inputs, never a real coding task
- The "daily driver" switch date keeps moving forward

**Phase to address:**
v1 minimum — define the switch date as a success criterion in v1, not a feature.

---

## Category B: F#-Specific Traps

### B-1: System.Text.Json Cannot Serialize F# Discriminated Unions Without a Converter

**What goes wrong:**
F# DUs like `type ToolResult = Success of string | Error of string | ParseFailure of string` serialize to `{"Case":"Success","Fields":["output"]}` by default — the .NET runtime reflection format. This is valid JSON but not the schema the agent expects or produces. When the LLM output is parsed into a DU and then re-serialized for the message history, the round-trip produces malformed messages. Worse: this fails silently unless you inspect the actual serialized wire bytes.

**Why it happens:**
F# developers coming from Python expect `JsonSerializer.Serialize(myDu)` to "just work" the way Python dataclasses do. The .NET runtime has no native understanding of F# union types.

**How to avoid:**
Install `FSharp.SystemTextJson` (NuGet: `Tarmil.FSharp.SystemTextJson`). Configure it globally:
```fsharp
let options = JsonSerializerOptions()
options.Converters.Add(JsonFSharpConverter(JsonFSharpOptions.Default()))
```
Or use `[<JsonFSharpConverter>]` attribute on specific types. Do this before writing a single serialization call. Verify the wire format of DUs in a unit test — serialize a DU instance, deserialize it back, compare.

For the `{thought, action, input}` LLM output schema, use a plain F# record (not a DU) for the top-level parse target — records serialize cleanly without a converter. Use DUs for the internal `action` discriminator after parsing.

**Warning signs:**
- `"Case"` or `"Fields"` appearing in any serialized JSON output
- Deserialization of LLM response into a DU without an explicit converter registered
- Any `JsonSerializer.Deserialize<ToolAction>` call without `options` parameter

**Phase to address:**
v1 minimum — the JSON layer is the entire interface between F# and the LLM. Get this right first.

---

### B-2: task { } vs async { } — Wrong Choice Breaks .NET Interop

**What goes wrong:**
F# has two async computation expressions: `async { }` (F#-native, cold, `Async<T>`) and `task { }` (.NET-native, hot, `Task<T>`). Most .NET HTTP libraries (`HttpClient`, `System.Net.Http`) return `Task<T>`. Mixing them without explicit conversion creates a subtle bug: inside an `async { }` block, `let! response = httpClient.SendAsync(req)` will not compile without wrapping in `Async.AwaitTask`. Forgetting this causes a compilation error that leads developers to wrap everything in `Async.AwaitTask` everywhere, creating noise.

More dangerous: `async { }` does not support `try ... finally` for async cleanup. If the HTTP streaming connection needs cleanup in a finally block, `async { }` silently drops the finally on cancellation.

**How to avoid:**
Use `task { }` everywhere in blueCode. The project is .NET-first, interoperates with `HttpClient` and `System.IO`, and has no need for F#-specific async features like built-in cancellation token threading or tail-call async. `task { }` supports `try ... finally`, `use` bindings with `IAsyncDisposable`, and composes directly with .NET library returns. Use `Async.AwaitTask` only when calling legacy F# libraries that return `Async<T>`.

**Warning signs:**
- `Async.AwaitTask` appearing more than twice in the codebase (signals wrong async model)
- `async { }` used as the top-level computation in the agent loop
- `try ... finally` missing from streaming HTTP reader because it "didn't compile in async { }"

**Phase to address:**
v1 minimum — choose `task { }` at project start and document it as a convention. Do not mix.

---

### B-3: Dispose / IAsyncDisposable Across Async Boundaries

**What goes wrong:**
The agent makes HTTP calls to the local Qwen server with streaming (`response_format: stream`). `HttpResponseMessage` is `IDisposable`. `StreamReader` over the SSE stream is `IDisposable`. In a `task { }` block, `use` bindings dispose synchronously at the end of scope. If the scope exits early (exception, cancellation), the stream is disposed correctly. But if `HttpResponseMessage` is passed across function boundaries — stored in a record, returned from a helper — the disposal obligation is lost, and the connection stays open. Under the 72B model's 30-second inference latency, accumulated undisposed connections will cause connection pool exhaustion.

**How to avoid:**
Never return `HttpResponseMessage` from a function. Consume the response body inside the same `task { }` scope where it was created. Pattern:
```fsharp
task {
    use response = do! httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
    use stream = do! response.Content.ReadAsStreamAsync()
    use reader = new StreamReader(stream)
    // ... read SSE lines here
}
```
If you need to pass partial results out, collect them into a list or sequence inside the scope, then return the list — not the stream.

**Warning signs:**
- `HttpResponseMessage` appearing as a field in any record type
- Any function that returns `Task<HttpResponseMessage>` rather than consuming it
- Connection count to localhost:8000 growing over a session (check with `lsof | grep 8000`)

**Phase to address:**
v1 minimum — streaming is part of the HTTP client in v1. Get the disposal pattern right from the first implementation.

---

### B-4: F# Record/DU Equality and NuGet Libraries Expecting C# POCOs

**What goes wrong:**
F# records have structural equality by default. This is good for comparison, but some .NET serialization and validation libraries use reflection to discover properties and call constructors. They expect a parameterless constructor (which F# records do not have) or settable properties (which F# records do not have by default). The symptom is a `MissingMethodException` or `InvalidOperationException` at runtime in a library that "should work with any .NET type."

For this project, the risk surfaces if a JSON schema validation library is added, or if any library tries to construct DUs via reflection.

**How to avoid:**
Test every NuGet library that touches F# types with a quick smoke test on an F# record before integrating. For `System.Text.Json` with `FSharp.SystemTextJson`, this is already solved. Do not use libraries that require `[<CLIMutable>]` unless absolutely necessary. If `[<CLIMutable>]` is needed for interop, scope it to DTOs at the boundary layer only — never use it on core domain types.

**Warning signs:**
- `MissingMethodException` or `System.InvalidOperationException: Unable to find a suitable constructor` in stack traces
- `[<CLIMutable>]` appearing on core agent state types (AgentState, ToolResult, Step)
- A NuGet library working on a C# POCO test but failing on the equivalent F# record

**Phase to address:**
v1 minimum — verify at the time each NuGet dependency is added.

---

### B-5: List vs Seq vs Array — Hidden Allocation in the Agent Loop

**What goes wrong:**
The agent loop accumulates steps (last N steps in memory). Using `Seq` for this accumulation means every iteration re-evaluates the lazy sequence from scratch — O(N) traversals on every step append. Using F# `List` is correct here (it's a linked list; prepend is O(1)), but using `list.[N]` for random access is O(N) and kills performance on long histories. Using `Array` is the right choice when you need random access or need to pass data to .NET APIs that accept `IEnumerable<T>`.

The specific trap: using `Seq.append` to add a new step to the history, then `Seq.toList` on every render cycle. This causes O(N²) work over the session.

**How to avoid:**
Model the agent's step history as `Step list` (F# List). Prepend new steps with `::`. When rendering, iterate with `List.iter` — no random access needed. When the ring buffer window is applied (last 3 steps), use `List.truncate 3 history`. When passing to HttpClient as request body, `List.toArray` once at the HTTP call boundary.

**Warning signs:**
- `Seq.append` inside the agent loop body
- `history.[i]` (index access on a list)
- `Seq` used as the type annotation for persistent state in AgentState

**Phase to address:**
v1 minimum — model the memory type correctly at project start. Changing the collection type later requires touching every pattern match.

---

### B-6: Over-Modeling with Types — 20 Hours of Type Design Before Writing Functions

**What goes wrong:**
F#'s type system is expressive enough that a developer can model the entire problem domain before writing a single function. For blueCode, this manifests as: designing a fully exhaustive `ToolInput` DU with every possible argument combination type-safe, a `ModelRoute` DU for every routing scenario, a `SecurityDecision` DU for every validation result, and a `ParseState` DU for every JSON parsing state — before implementing the HTTP call. Three days pass. No agent has run yet.

**Why it happens:**
The user's design principle is "strong typing maximized." This is correct but can become perfectionist paralysis when applied to types before functions exist.

**How to avoid:**
Apply the "make it work, then make it typed" rule. The first version of a tool handler can use `string` parameters. Add DU wrappers after the function works end-to-end. A good heuristic: if you've been designing types for more than 2 hours without writing a function that calls an API, stop and write the function first with placeholder types. Refine types once the function runs.

The `{thought, action, input}` JSON schema is already specified. Start there — that is the type. Build outward.

**Warning signs:**
- A `Types.fs` file that is longer than `Agent.fs`
- DU cases that map 1:1 to string constants from the JSON schema without adding safety
- A type for "the result of deciding what to do with the result of parsing the action field"

**Phase to address:**
v1 minimum — set a rule: no new type module until at least one real HTTP call has succeeded.

---

### B-7: Exception Handling in Computation Expressions

**What goes wrong:**
Inside `task { }`, F# exceptions from `let!` bindings are caught by a `try ... with` inside the computation expression. But if an exception escapes the CE boundary (from a non-task expression, or a raw `Task` that faults), it appears as an `AggregateException` at the call site — the `.Exception.InnerException` pattern required by .NET tasks. F# developers expecting `try ... with ex -> ...` to see the original exception type will instead catch `AggregateException` and miss the actual error.

Additionally, F# `task { }` does not propagate `OperationCanceledException` automatically in the same way `async { }` does — cancellation must be explicitly checked or the token passed.

**How to avoid:**
Wrap every top-level `task { }` with:
```fsharp
task {
    try
        do! agentLoop()
    with
    | :? AggregateException as ae ->
        // unwrap: ae.InnerExceptions |> Seq.iter (eprintfn "%O")
    | ex ->
        eprintfn "Unhandled: %O" ex
}
```
Inside the loop, use `Result<'T, string>` for expected failures (JSON parse failure, tool error) and exceptions only for truly unexpected errors. Never let a tool execution throw — wrap `Process.Start` in `try ... with` and return a `ToolResult.Error` instead.

**Warning signs:**
- `AggregateException` appearing in crash logs without being unwrapped
- A `try ... with ex ->` that catches `Exception` but logs `ex.Message` — losing the AggregateException inner chain
- Tool execution errors causing the entire agent loop to exit instead of returning an error observation

**Phase to address:**
v1 minimum — error handling is part of the core loop design.

---

## Category C: Local Qwen-Specific Pitfalls

### C-1: Qwen Output Drift — Extra Text Before/After JSON

**What goes wrong:**
Qwen 32B/72B regularly produces output like:
```
Sure, I'll help you fix that bug!

```json
{
  "thought": "I need to read the file first",
  "action": "read_file",
  "input": {"path": "src/main.rs"}
}
```

The JSON is there but wrapped in prose and markdown fences. A strict `JsonSerializer.Deserialize<AgentResponse>(raw)` call throws `JsonException` on the first non-JSON character. Retry logic then sends the failed response back to the model, which sometimes produces more prose in response to the parse error, creating a spiral.

**Why it happens:**
Qwen is trained on conversational data where helpful framing text is normal. Even with a system prompt saying "respond only in JSON," Qwen models drift toward explanatory text, especially on the first turn of a session or after a tool error that looks like a "teaching moment." Qwen 2.5 Coder is better than the base 2.5 but not immune. The langchain4j issue #2680 documents this specifically for Qwen2.5 tool call formatting.

**How to avoid:**
Never use a raw JSON parse as the first attempt. Use a multi-stage extraction pipeline:
1. Check if the raw string is already valid JSON → parse directly.
2. If not, extract the first `{...}` block using a regex that respects brace nesting depth.
3. If not, extract content between ` ```json ` and ` ``` ` fences.
4. If none succeed → return `ParseFailure` and log the raw response.

Additionally, use `response_format: {"type": "json_object"}` in the vLLM request if vLLM/Ollama version supports it. This constrains the model to valid JSON output at the token sampling level — the most reliable prevention.

**Warning signs:**
- `JsonException: 'S' is an invalid start of a value` in logs (prose start)
- `JsonException: '`' is an invalid start of a value` in logs (code fence)
- Parse failures correlated with the first turn of a session or after tool errors

**Phase to address:**
v1 minimum — JSON parsing is the most critical component. Build the extraction pipeline before building any tool.

---

### C-2: Context Window Overflow — 128K Tokens vs Reality

**What goes wrong:**
Qwen 32B and 72B advertise 128K context windows. In practice, vLLM and Ollama often serve models with a much lower `max_model_len` setting determined at server startup, commonly 8K, 16K, or 32K depending on VRAM. When the conversation history (system prompt + all steps) exceeds the configured limit, vLLM returns a 400 error `This model's maximum context length is 8192 tokens`. The error happens mid-session, not at startup, because the history grows turn by turn.

Additionally, the 5-step ring buffer in v1 (last 3 steps) mitigates but does not eliminate this. A single `read_file` result on a large source file can be 4K tokens on its own.

**How to avoid:**
Set an explicit token budget for tool outputs. Truncate tool results at a configurable `maxOutputTokens` (default 2000 characters ≈ ~500 tokens) before appending to history. Include a token estimation function (characters / 4 as a rough heuristic) that warns when the accumulated context approaches 6000 tokens on 32B or 12000 on 72B. On overflow warning, truncate the oldest non-system steps from history.

At the HTTP request level, always send `max_tokens` and respect the server's `max_model_len`. Query the server's `/v1/models` endpoint at startup to read the actual context limit.

**Warning signs:**
- HTTP 400 with `maximum context length` in the error body
- Tool outputs for large files appearing in full in the message history
- Session working fine for 3 turns then failing on turn 4 with a context error

**Phase to address:**
v1 minimum — tool output truncation is part of the initial tool design. Context overflow is an early failure mode, not a scaling concern.

---

### C-3: SSE Streaming Format Differences Between vLLM and Ollama

**What goes wrong:**
Both vLLM and Ollama claim OpenAI-compatible streaming via SSE. In practice:
- vLLM sends `data: {json}\n\n` chunks following the OpenAI spec, but with a documented bug: streaming tool call chunks are missing `"type":"function"` in the first chunk (vLLM issue #16340).
- Ollama sends `data: {json}\n\n` but may batch multiple chunks per SSE message on fast hardware.
- LM Studio does not support streaming tool calls at all.
- The final `data: [DONE]` message is present in vLLM but absent in some Ollama versions.

An SSE reader that assumes one JSON object per line will silently drop partial chunks on fast Ollama responses.

**How to avoid:**
For v1, do not use streaming — use non-streaming `POST /v1/chat/completions` without `stream: true`. Wait for the full response. This eliminates all SSE format differences. The 30-second latency for 72B is acceptable for a personal dev tool. Only add streaming in v2 when the UX specifically needs it.

If streaming is added later, implement a proper SSE parser that buffers lines until `\n\n`, handles `data: [DONE]` explicitly, and discards `data: ` prefix before JSON parsing.

**Warning signs:**
- Partial JSON parse errors that appear intermittently (chunk boundary issue)
- Empty responses on fast hardware but correct responses on slow hardware
- `[DONE]` appearing in a parse error log

**Phase to address:**
v1 — avoid streaming entirely. Add streaming in v2 with a proper SSE parser.

---

### C-4: Inference Latency — 72B Takes 30 Seconds and UX Falls Apart

**What goes wrong:**
On a Mac with a fast M-series chip, Qwen 32B produces tokens at ~15-30 tokens/second. Qwen 72B produces at ~5-10 tokens/second. A response of 200 tokens from 72B takes 20-40 seconds. During this time, the CLI shows nothing. The user assumes the agent crashed. They press Ctrl+C. The HTTP connection is abandoned. The vLLM server continues inference and wastes resources.

**How to avoid:**
Show a progress indicator before the HTTP call starts (not after — there is no streaming in v1). A simple spinner with elapsed time is sufficient:
```
[Step 2] Running read_file... (72B model selected)
Thinking... 3s
```
Use a background thread or async task for the spinner while the main thread awaits the HTTP response. On Ctrl+C, catch `OperationCanceledException` and print `Aborted.` cleanly — do not print a stack trace.

Also display the model selected (32B vs 72B) prominently at each step so the user knows whether to expect a 3-second or 30-second wait.

**Warning signs:**
- User-reported "it hangs" during first 72B test
- No output between "Step N" header and the tool result
- Ctrl+C during inference causing unhandled exception instead of graceful abort

**Phase to address:**
v1 minimum — spinner and model display are part of the verbose renderer, which is v1 scope.

---

### C-5: Temperature Extremes — Invalid JSON vs Infinite Repetition

**What goes wrong:**
- High temperature (>0.9): Qwen produces malformed JSON — unclosed brackets, hallucinated field names, prose mixed into JSON values. The parse pipeline fails and retries, which at high temperature produces another malformed response. Retry budget exhausted.
- Low temperature (≤0.0, greedy decoding): Qwen enters repetition loops — the same "thought" or "action" repeated across all 5 agent steps. The max-5 cap saves the loop from being infinite but produces 5 identical tool calls with no progress. Qwen model cards explicitly warn against greedy decoding.
- For Qwen thinking-mode variants (if used): temperature must be set to 0.6, TopP=0.95, TopK=20 per model card. Deviating from these causes degradation specific to the thinking-mode RLVR training.

**How to avoid:**
Default temperature: `0.2` for 32B (stable JSON, minimal drift), `0.4` for 72B (more reasoning variation). Expose as a config parameter but set these as hardcoded defaults. Never expose raw temperature to the user in v1. Set `presence_penalty=1.5` (Qwen model card recommendation) to prevent repetition loops. Note: Ollama silently ignores `presence_penalty` — if using Ollama backend, this must be documented.

**Warning signs:**
- Same tool call appearing on step 2 and step 3 with identical arguments
- JSON parse failure rate above 10% (implies temperature too high)
- 5/5 steps completing with the same action (implies greedy decoding / temperature 0)

**Phase to address:**
v1 minimum — set temperature defaults in the HTTP client configuration at project start.

---

### C-6: Role/Content Format Differences Between Inference Backends

**What goes wrong:**
The OpenAI chat completions format uses `{"role": "system", "content": "..."}` for the system prompt. vLLM, Ollama, and LM Studio all claim compatibility, but:
- Some Ollama versions ignore the `system` role and treat it as a `user` turn.
- vLLM with Qwen requires the system prompt to be set in the chat template's `system` slot to take effect — passing it as a user message produces different behavior.
- The `role: "tool"` message type for tool results is not supported by all backends — some require emulating it with `role: "user"` and a prefixed observation string.

Since blueCode skips OpenAI function calling and uses JSON output (the correct design choice for local Qwen), the `tool` role is not needed for tool calls. But the system message placement still matters.

**How to avoid:**
Test the system prompt placement on the actual backend at startup with a "ping" call: send `[{role: "system", content: "Reply: PONG"}, {role: "user", content: "ping"}]` and verify the response starts with "PONG" (or is parseable JSON containing it). If not, switch to prepending system content into the first user message as `[SYSTEM]\n{systemPrompt}\n\n[USER]\n{userMessage}`.

Tool results should be passed as `role: "user"` with a clearly prefixed format: `[OBSERVATION]\n{toolOutput}`. This works across all backends.

**Warning signs:**
- System prompt instructions being ignored on first turn (e.g., model responds in plain text despite JSON instruction)
- `role: "tool"` appearing in `messages` array (not needed for this architecture, flag it)
- Backend-specific behavior where the 32B endpoint follows instructions but 72B does not (different Ollama vs vLLM backends)

**Phase to address:**
v1 minimum — the system message format is part of the initial HTTP client design.

---

### C-7: Tool Call Emulation vs OpenAI Function Calling

**What goes wrong:**
OpenAI function calling sends tool calls in a structured `tool_calls` array on the assistant message, requiring the `tools` parameter in the request. Qwen 2.5 supports this on cloud API but behavior via local vLLM is inconsistent — specifically, Qwen2.5-Coder does not use the Hermes `<tool_call>` format that vLLM expects, causing silent tool call failures. The vLLM tool parser must be explicitly set to `hermes` for base Qwen2.5, and a custom parser exists for Coder variants.

The user's design decision to use strict JSON output (`{thought, action, input}`) in the response body, bypassing OpenAI function calling entirely, is correct. It avoids all tool parser configuration complexity at the cost of requiring JSON extraction from free text.

**How to avoid:**
Do not use the `tools` parameter in requests. Do not use `tool_calls` in response parsing. Parse the LLM response body for the JSON schema `{thought, action, input}` using the extraction pipeline from C-1. This is robust, backend-agnostic, and works identically on vLLM, Ollama, and LM Studio.

**Warning signs:**
- `tools` parameter appearing in the HTTP request body
- `tool_calls` appearing in response parsing code
- Any reference to "function calling" or `tool_choice` in the HTTP client module

**Phase to address:**
v1 — enforce this as a design constraint from the start. The JSON-in-content approach is the right choice; do not revisit it.

---

### C-8: Chinese/Korean Text in English Outputs

**What goes wrong:**
QwQ-32B-Preview and some Qwen2.5 variants exhibit "language mixing" — producing Chinese characters in English-language responses, especially in chain-of-thought reasoning. This is documented on Hugging Face (Qwen/QwQ-32B-Preview discussions) and confirmed in research showing it is caused by RLVR training on mixed-language data. For blueCode, this appears as Chinese characters in the `"thought"` field of the JSON output, which is harmless (it is never displayed to the user in compact mode) but looks alarming in verbose mode and could confuse downstream log parsing.

**How to avoid:**
In the system prompt, include an explicit instruction in both English and (ironically) Chinese: `Always respond in English. 请用英文回复。` This reduces but does not eliminate the behavior. In the verbose renderer, filter or escape non-ASCII characters in the `thought` field display. Do not attempt to block Chinese output at the JSON parsing level — the output is still valid JSON.

For thinking-mode Qwen models (if adopted later), this behavior is more pronounced during the internal reasoning phase and cannot be reliably prevented.

**Warning signs:**
- `\u4e` or similar Unicode escape sequences in logged thought fields
- Verbose output showing mixed-script thought text
- Parse errors caused by incorrectly assuming `thought` contains only ASCII

**Phase to address:**
v1 — add the bilingual language instruction to the system prompt from the start. The renderer should display raw Unicode correctly (Spectre.Console handles this).

---

## Category D: Agent Loop and Tool Design Pitfalls

### D-1: Infinite Loop Despite Max-5 Cap — Same Tool on Step 5 Output

**What goes wrong:**
The max-5 loop cap prevents infinite loops, but it does not prevent the agent from calling the same tool with the same arguments on every step. If the tool returns an error, and the LLM interprets the error as "try again with the same call," the agent burns 5 steps on identical failed tool calls and returns nothing useful. The loop terminates correctly at step 5 but the output is garbage.

A subtler variant: the LLM calls `read_file` on step 1, gets the content, then calls `read_file` again on step 3 with the same path (it "forgot" it already read it). The 3-step memory window is supposed to prevent this, but if the file read output is large and gets truncated, the LLM may not register it as a completed action.

**How to avoid:**
Implement a `LoopGuard` that tracks `(action, input_hash)` pairs across steps. If the same `(action, input)` tuple appears twice in the current session, return a `LoopError` before executing the tool on the third occurrence. Specifically:
```fsharp
type LoopGuard = { Seen: Map<string * string, int> }
// key = (action, SHA256(input JSON))
// if count >= 2, return Error "Loop detected: {action} called with same args twice"
```
The loop guard output goes into the observation and forces the LLM to try something different.

**Warning signs:**
- Step logs showing identical `action` and `input` values across consecutive steps
- 5/5 steps executing, all returning the same tool result
- Tool error followed by 4 identical retries of the same tool

**Phase to address:**
v1 minimum — the loop guard is a 20-line function. Build it before testing the full agent loop.

---

### D-2: run_shell Security — Command Injection, Fork Bombs, Unbounded Output

**What goes wrong:**
The LLM may generate shell commands that are dangerous or resource-exhausting. Examples from real-world LLM agent security research (Trail of Bits, 2025; arxiv 2601.17548):
- `rm -rf /` or `rm -rf --no-preserve-root`
- `:(){:|:&};:` (fork bomb)
- `cat /etc/passwd; echo $(whoami)` (injection via command substitution)
- `find / -name "*.env" | xargs cat` (credential sweep)
- `python3 -c "import os; os.system('...')"` (bypass via interpreter)
- Unbounded output: `cat /dev/urandom | base64` — fills memory and crashes the agent process

The Python `bash_security.py` addresses 18 of these attack vectors through validators for command substitution, redirections, IFS injection, brace expansion, unicode whitespace, and destructive commands. These validators must be ported to F#.

**How to avoid:**
Port the security validator chain from `claw-code-agent/src/bash_security.py` to F#. The F# implementation should be a function `checkShellSecurity: string -> SecurityResult` that returns `Allow`, `Deny of reason`, or `AskUser of reason`. The validator chain in priority order:
1. Control characters / null bytes
2. Command substitution: `$()`, backtick, `${}`
3. Process substitution: `<()`, `>()`
4. IFS injection: `$IFS`
5. Destructive patterns: `rm -rf`, fork bomb, `/proc/*/environ`
6. Redirections: `>`, `<` (require ASK)
7. Newlines in commands (potential multi-command injection)

Additionally, set hard limits on `run_shell`:
- Timeout: 30 seconds (configurable)
- Stdout capture: max 100KB (truncate beyond)
- Stderr capture: max 10KB
- Working directory: locked to project root — refuse any command containing `..` in an absolute path argument

**Warning signs:**
- `run_shell` implementation without pre-execution security checks
- Missing process timeout on `Process.WaitForExit`
- Tool output containing `>100KB` being appended to message history
- The LLM successfully executing `cat /etc/passwd` in a test

**Phase to address:**
v1 minimum — `run_shell` must not be deployed without security validation. This is non-negotiable.

---

### D-3: File Path Escape — Writing Outside Project Root

**What goes wrong:**
The `write_file` tool takes a path as input. The LLM might generate paths like:
- `../../.ssh/authorized_keys` (path traversal)
- `/tmp/exploit.sh` (writing outside project)
- `~/.bashrc` (home directory poisoning)
- Absolute paths that happen to exist on the developer's Mac

Since blueCode is personal use only, the risk is self-harm rather than external attack, but accidental writes to the wrong location are a real developer experience problem.

**How to avoid:**
`write_file` must validate that the resolved absolute path starts with the project root. The validation:
```fsharp
let safeWritePath (projectRoot: string) (inputPath: string) : Result<string, string> =
    let resolved = Path.GetFullPath(Path.Combine(projectRoot, inputPath))
    if resolved.StartsWith(projectRoot, StringComparison.Ordinal) then
        Ok resolved
    else
        Error $"Path escape rejected: {inputPath} resolves to {resolved}"
```
`Path.GetFullPath` resolves `..` sequences, making this traversal-safe. Apply the same check to `read_file` for consistency (reading `/etc/passwd` is lower risk but still surprising).

**Warning signs:**
- `write_file` accepting absolute paths without checking against project root
- `Path.Combine(root, input)` without `Path.GetFullPath` normalization (doesn't catch `..`)
- Any test of `write_file` with a `..` path that succeeds

**Phase to address:**
v1 minimum — add path validation to the tool definition, not as an afterthought.

---

### D-4: Tool Errors Swallowed — LLM Never Learns What Went Wrong

**What goes wrong:**
When a tool fails (shell returns exit code 1, file not found, JSON parse error), the naive implementation either: (a) returns an empty string to the LLM ("output: ''"), or (b) throws an exception that crashes the loop. In case (a), the LLM sees no evidence of failure and may assume the tool succeeded, continuing with an incorrect assumption. In case (b), the entire session is lost.

The correct behavior is to return the error as a structured observation that the LLM can reason about.

**How to avoid:**
Every tool returns a `ToolResult` DU:
```fsharp
type ToolResult =
    | Success of output: string
    | Failure of exitCode: int * stderr: string
    | SecurityDenied of reason: string
    | PathEscapeBlocked of attempted: string
    | Timeout of seconds: int
```
This is serialized into the observation message as:
```
[TOOL ERROR]
Exit code: 1
Stderr: command not found: git
```
The LLM receives this as a user-role observation. Its next step should be to try a different approach, not retry the same command. The observation format must be explicit enough that the LLM can parse what went wrong.

**Warning signs:**
- Empty string returned as tool output on tool failure
- Tool failure causing `System.Exception` to propagate to the loop level
- Log showing "Step 3: Success" when the underlying tool returned exit code 1

**Phase to address:**
v1 minimum — the `ToolResult` type and observation format must be defined before any tool is implemented.

---

### D-5: Loss of State on Crash — No Persistence in v1

**What goes wrong:**
The user starts a complex 5-step refactoring task. On step 4, the agent crashes (OOM, exception, Ctrl+C). All conversation history, step results, and the partial file edits are lost from the agent's memory. The user must restart from scratch, re-explain the task, and hope the agent makes the same decisions again. The file system changes from steps 1-3 are permanent (if `write_file` was used), but the agent's knowledge of those changes is gone.

**Why it happens:**
v1 explicitly does not implement session persistence (documented in PROJECT.md). This is a correct scoping decision. But the consequence is that mid-session crashes are unrecoverable.

**How to avoid:**
While full persistence is v2, implement a lightweight crash log: at each step completion, append the step to a JSONL file `~/.bluecode/session_YYYYMMDD_HHMMSS.jsonl`. This is not "session persistence" — it does not enable resume. It is a post-mortem log that lets the user see what happened. The JSONL file also serves as the primary debug artifact.

Additionally, make Ctrl+C graceful: catch `OperationCanceledException` at the top level and print a summary of completed steps before exiting.

**Warning signs:**
- No structured log of completed steps persisted to disk
- Ctrl+C producing a raw stack trace as the final output
- "What did the agent do?" being unanswerable after a crash

**Phase to address:**
v1 — step logging to JSONL is a 30-line addition to the step renderer. Include it in v1.

---

### D-6: JSON Parse Failure Handling — Retry Loop vs Give Up vs Ask User

**What goes wrong:**
The LLM produces non-parseable output. The agent must decide: retry the same prompt, send a correction prompt, or give up and ask the user. Naive retry (send the same messages again) often produces the same malformed output. Sending a correction prompt ("Your output was not valid JSON, please respond again in the required format") sometimes works but adds a turn. Giving up immediately on first failure abandons too easily.

The risk is building a retry loop that never terminates if the LLM is in a bad state (e.g., overloaded, wrong temperature, context overflow).

**How to avoid:**
Use a 2-attempt policy with an explicit correction turn, then fail:
1. Attempt 1: Parse the raw output. If it succeeds, continue.
2. If parse fails, send a correction message: `[PARSE ERROR] Your response was not valid JSON. Required format: {"thought":"...","action":"...","input":{...}}. Please respond in JSON only.`
3. Attempt 2: Parse the corrected output. If it succeeds, continue.
4. If parse fails again: return `AgentError "Model failed to produce valid JSON after correction"`. Display the raw output to the user so they can diagnose.

This is bounded (maximum 2 parse attempts per step), explicit (the user sees the error), and gives the LLM one chance to self-correct.

**Warning signs:**
- A `while not parsed do retry` loop without an attempt counter
- JSON parse failure causing the step to silently return a default/empty result
- Correction message being sent but attempt 2 using the same raw output as attempt 1 (logic bug)

**Phase to address:**
v1 minimum — build the 2-attempt correction policy into the JSON parsing layer from the start.

---

### D-7: Model Switching Mid-Turn Breaking Conversational State

**What goes wrong:**
The user's design routes simple tasks to 32B and complex tasks to 72B. If the routing decision changes between steps within the same turn — e.g., step 1 uses 32B, step 2 uses 72B because the tool output looks "complex" — the conversation history is sent to a different model. This is technically fine (the history is model-agnostic) but produces inconsistency: the 72B model may interpret the 32B's previous "thought" field differently, or produce a different JSON schema variant, breaking the parser.

The larger risk: routing changes between turns (turn 1 is 32B, turn 2 is 72B based on re-classified intent) mean the system prompt's instruction tuning may differ between models. 32B and 72B may have slightly different instruction-following behavior for the same system prompt.

**How to avoid:**
Fix the model for the entire duration of a turn (a turn = user input → agent loop completion). Evaluate intent once at the start of a turn and do not re-evaluate mid-loop. Store the selected model in the `AgentState` record. In v1, it is acceptable to always use 32B and select 72B only as a user-specified flag (`--model 72b`), deferring automatic intent-based routing to v2.

**Warning signs:**
- `selectModel(step)` called inside the agent loop (per-step model selection)
- Model selection logging showing 32B on step 1 and 72B on step 3 of the same turn
- JSON parse failures correlated with step-to-step model changes

**Phase to address:**
v1 — decide the model once at turn start. Automatic per-turn routing is v2.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Raw `JsonSerializer.Deserialize` without FSharp.SystemTextJson | Simpler setup | DU serialization breaks; silent data corruption | Never for DU types |
| No shell security validator | Faster v1 | LLM generates `rm -rf` and user runs it | Never |
| Streaming from day 1 | Real-time output | SSE parser bugs, backend-specific format issues | v2 only |
| `mutable` state in agent loop | Easy to understand for Python dev | Loses immutability guarantees; harder to test | Never in core loop |
| `Seq` for step history | Lazy, elegant | O(N²) on accumulation | Never for accumulated history |
| Skip path validation in write_file | Fewer lines of code | Accidental writes to wrong location | Never |
| Per-step model routing | Feels "smarter" | Inconsistent behavior, parse failures | v2 only, with testing |
| No step JSONL log | Simpler v1 | No crash post-mortems | Never — 30 lines to add |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| vLLM chat completions | Using streaming by default | Use non-streaming in v1; streaming is v2 |
| Ollama chat completions | Trusting `presence_penalty` takes effect | Verify via Ollama changelog; it silently discards in many versions |
| vLLM tool calls | Using `tools` parameter for Qwen 2.5 Coder | Use JSON-in-content instead; Coder's tool parser format differs from base 2.5 |
| System.Text.Json + F# DUs | `JsonSerializer.Serialize(myDu)` directly | Register `JsonFSharpConverter` in `JsonSerializerOptions` |
| `HttpClient` + streaming | Not disposing `HttpResponseMessage` | Use `use` binding inside the same `task { }` scope |
| Process execution | No timeout on `WaitForExit` | Always pass `TimeSpan` to `Process.WaitForExitAsync` |

---

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| run_shell without command validation | Fork bomb, rm -rf, credential extraction | Port bash_security.py validator chain to F# |
| write_file without path boundary check | Overwrite ~/.ssh/authorized_keys or ~/.bashrc | Validate `Path.GetFullPath` starts with project root |
| Unbounded stdout capture | OOM crash from `cat /dev/urandom` | Hard limit stdout at 100KB, truncate with warning |
| No process timeout | Infinite hang on `sleep infinity` or stalled process | 30-second timeout on all shell executions |
| Logging LLM response verbatim | PII or secrets in prompt being logged to disk | Scrub tool outputs in JSONL log if they contain secret patterns |

---

## "Looks Done But Isn't" Checklist

- [ ] **JSON parser:** Handles prose-wrapped and markdown-fenced JSON, not just bare JSON objects — verify with `Sure, here's the JSON: \`\`\`json {...}\`\`\``
- [ ] **run_shell:** Has pre-execution security validation, not just execution — run `echo $(whoami)` through the validator and verify it is rejected
- [ ] **write_file:** Path traversal rejected — test `write_file("../../etc/cron.d/evil", "content")` and verify it fails with an error, not a write
- [ ] **Max-5 cap:** Enforced as a hard stop, not just a loop condition — verify it terminates even when the LLM returns `action: "read_file"` on step 5
- [ ] **Loop guard:** Duplicate tool calls rejected — send a prompt that causes read_file twice with same path and verify the second is blocked
- [ ] **Context overflow:** Tool output truncated — test with a file that produces >100KB output and verify the agent continues
- [ ] **Dispose:** No open connections after agent turn — verify with `lsof | grep 8000` after a full turn completes

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| DU serialization broken | HIGH | Add FSharp.SystemTextJson, audit all serialize/deserialize call sites |
| Async model wrong (async vs task) | MEDIUM | Refactor async { } to task { } — usually mechanical but touches all async functions |
| Shell security missing | HIGH | Port Python validator chain; test with injection inputs before re-enabling run_shell |
| Context overflow in production | LOW | Add truncation to tool output pipeline; adjust ring buffer window size |
| Second-system scope creep | HIGH | Revert to scope document; delete out-of-scope modules; reset to last milestone |
| JSON parse failures from Qwen | LOW | Add extraction pipeline (prose strip, fence extraction) around parse call |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| A-1: Second-system scope creep | v1 project init | Review scope list before each phase completion |
| A-2: Chesterton's fence (shell security) | v1 run_shell implementation | `echo $(whoami)` rejected by validator |
| A-3: Translation trap (mutable state) | v1 agent loop design | No `mutable` in Agent.fs core loop |
| A-4: Deferred delivery | v1 milestone definition | Set first real task date in success criteria |
| B-1: DU serialization | v1 JSON layer | Round-trip test for every DU used in messages |
| B-2: async vs task | v1 project init | `task { }` convention documented; no `async { }` in new code |
| B-3: Dispose across async | v1 HTTP client | `lsof | grep 8000` after test session shows 0 open connections |
| B-4: NuGet reflection on F# types | Each new NuGet addition | Smoke test with F# record before integrating |
| B-5: Seq accumulation | v1 memory model | `Step list` type annotation in AgentState |
| B-6: Over-modeling | v1 ongoing | Time-box type design sessions to 2 hours |
| B-7: Exception in CE | v1 error handling | AggregateException unwrapped in crash logs |
| C-1: Qwen output drift | v1 JSON parser | Parse test with `Sure, here is JSON: ```json {...}``` ` |
| C-2: Context overflow | v1 tool execution | Tool output truncation at 100KB in tool runner |
| C-3: SSE format differences | v1 — avoid streaming | Non-streaming only in v1 |
| C-4: Inference latency UX | v1 renderer | Spinner active during HTTP wait |
| C-5: Temperature extremes | v1 HTTP client config | Default temperature 0.2/0.4, presence_penalty 1.5 |
| C-6: Role format differences | v1 HTTP client | Ping test at startup verifies system prompt |
| C-7: Tool call emulation | v1 design constraint | No `tools` param in HTTP request |
| C-8: Chinese text in output | v1 system prompt | Bilingual language instruction in system prompt |
| D-1: Same-tool infinite loop | v1 loop guard | LoopGuard rejects third identical call |
| D-2: run_shell security | v1 tool implementation | Security validator present before run_shell test |
| D-3: Path escape | v1 file tools | `../../` path rejected; test in CI |
| D-4: Tool errors swallowed | v1 tool result type | ToolResult.Failure visible in step log |
| D-5: No crash recovery | v1 step logger | JSONL step log written after each step |
| D-6: JSON retry loop | v1 parse retry policy | Max 2 parse attempts per step, hardcoded |
| D-7: Model switching mid-turn | v1 routing design | Model fixed at turn start, stored in AgentState |

---

## Sources

- [FSharp.SystemTextJson — Customizing serialization format](https://github.com/Tarmil/FSharp.SystemTextJson/blob/master/docs/Customizing.md)
- [dotnet/runtime #55744 — F# DU serialization not natively supported](https://github.com/dotnet/runtime/issues/55744)
- [Microsoft Learn — Task expressions (F#)](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/task-expressions)
- [Microsoft Learn — Async expressions (F#)](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/async-expressions)
- [vLLM issue #16340 — Missing "type":"function" in streaming tool calls](https://github.com/vllm-project/vllm/issues/16340)
- [langchain4j issue #2680 — Qwen2.5 incorrectly formats JSON parameters in tool calls](https://github.com/langchain4j/langchain4j/issues/2680)
- [Qwen function calling docs](https://qwen.readthedocs.io/en/latest/framework/function_call.html)
- [vLLM tool calling docs](https://docs.vllm.ai/en/stable/features/tool_calling/)
- [Ollama OpenAI compatibility docs](https://docs.ollama.com/api/openai-compatibility)
- [Qwen/QwQ-32B-Preview HuggingFace discussion — Chinese characters in responses](https://huggingface.co/Qwen/QwQ-32B-Preview/discussions/16)
- [The second-system effect — Medium](https://medium.com/@juanhander/the-second-system-effect-when-experience-becomes-a-trap-d6d71b89c51c)
- [Agent keeps calling same tool — MatrixTrak](https://matrixtrak.com/blog/agents-loop-forever-how-to-stop)
- [Prompt injection to RCE in AI agents — Trail of Bits Blog](https://blog.trailofbits.com/2025/10/22/prompt-injection-to-rce-in-ai-agents/)
- [Security: Shell injection, path traversal in LLM agent — nanobot PR #77](https://github.com/HKUDS/nanobot/pull/77)
- [Qwen repetition loop issue — QwenLM/Qwen3-VL #1611](https://github.com/QwenLM/Qwen3-VL/issues/1611)
- [Ollama silently discards presence_penalty — ollama/ollama #14493](https://github.com/ollama/ollama/issues/14493)
- [claw-code-agent/src/bash_security.py — Python shell security reference](../../../claw-code-agent/src/bash_security.py)
- [Microsoft Learn — Resource management: use keyword (F#)](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/resource-management-the-use-keyword)
- [F# collection performance — IT Nota](https://www.itnota.com/fsharp-seq-list-array-map-set-which-one-to-use/)
- [Structured output for Qwen — Alibaba Cloud](https://www.alibabacloud.com/help/en/model-studio/qwen-structured-output)

---
*Pitfalls research for: F# local-LLM coding agent (blueCode — Qwen 32B/72B rewrite)*
*Researched: 2026-04-22*
