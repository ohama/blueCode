# Project Research Summary

**Project:** blueCode — F# rewrite of a Python local-LLM coding agent
**Domain:** Local CLI coding agent (F#/.NET 10, Qwen 32B/72B on localhost, Mac-only)
**Researched:** 2026-04-22
**Confidence:** HIGH

---

## Executive Summary

blueCode is a personal daily-driver coding agent built on F#/.NET 10 that routes tasks to locally-served Qwen 32B and 72B models over an OpenAI-compatible HTTP API. It replaces a Python claw-code-agent implementation while deliberately shedding 65+ Python modules: the v1 scope is a single agent loop, 4 tools, strict JSON output enforcement, and a max-5-iteration cap. The design philosophy is "strong typing over runtime checks" — illegal agent states, mis-dispatched tools, and malformed model output are caught at compile time or at the F# parse boundary, never silently swallowed.

The recommended approach, drawn from all four research domains, is a two-project F# solution (BlueCode.Core + BlueCode.Cli) built around a closed discriminated union spine. Domain types come first (Domain.fs), adapters come last (QwenHttpClient.fs, FsToolExecutor.fs), and the agent loop sits in the middle, injected with interfaces it never instantiates. The stack is deliberately minimal: no ASP.NET host, no DI container, no OpenAI SDK — just HttpClient, FSharp.SystemTextJson, FsToolkit.ErrorHandling, Spectre.Console, and Argu. All are .NET 10 compatible and verified current as of April 2026.

The dominant risks are Qwen-specific: models produce prose-wrapped JSON, drift toward repetition at wrong temperatures, and silently overflow their actual context window (often 8K–32K, not the advertised 128K). These are not theoretical concerns — each has a documented mitigation that must be built in Phase 1, not retrofitted. A secondary risk is rewrite scope creep: the Python agent's 70+ modules create gravitational pull to port things that are explicitly out of scope. A hard scope gate enforced by PROJECT.md and DEFERRED.md is the only structural protection.

---

## Key Findings

### Recommended Stack

The stack is F# 10 / .NET 10 (LTS through 2028) with zero-dep inbox libraries covering HTTP and SSE (System.Net.Http, System.Net.ServerSentEvents), plus five NuGet packages covering the gaps F# creates vs C#: FSharp.SystemTextJson for DU serialization, FsToolkit.ErrorHandling for taskResult {} CE, FSharp.Control.TaskSeq for SSE streaming, JsonSchema.Net for LLM output schema validation, and Spectre.Console for terminal rendering. CLI parsing uses Argu (DU-backed, F#-native) and structured logging uses Serilog (no host required). The OpenAI SDK, Newtonsoft.Json, async {} CE, and any DI container are explicitly excluded.

.NET 10 contributes two features that directly benefit this project: JsonSerializerOptions.Strict (rejects unknown fields and duplicate keys from LLM output without extra library code) and inbox SseParser (SSE token stream from IAsyncEnumerable without a third-party library). F# 10 contributes and! in task {} (concurrent LLM calls in future 32B+72B parallel routing) and tail-call optimization in CEs (agent loop will not stack-overflow).

**Core technologies:**
- F# 10 / .NET 10: Language + runtime — LTS, native task {} CE, and!, tail-call CEs
- System.Net.Http.HttpClient: HTTP to Qwen endpoints — stdlib, zero-dep, sufficient for 1-endpoint chat completions
- System.Net.ServerSentEvents: SSE streaming — inbox since .NET 9, SseParser.Create directly on response stream
- FSharp.SystemTextJson 1.4.36: DU + record JSON serialization — only maintained library for F# DUs with System.Text.Json
- System.Text.Json (inbox): JSON serialize/deserialize + Strict mode — .NET 10 Strict preset enforces LLM output schema
- JsonSchema.Net 9.2.0: Runtime JSON schema validation — validates {thought, action, input} before dispatching tools
- FsToolkit.ErrorHandling 5.2.0: taskResult {} CE — eliminates 100+ lines of manual Result bind chains in agent loop
- FSharp.Control.TaskSeq 1.1.1: IAsyncEnumerable operators — accumulates SSE token stream via TaskSeq.fold
- Spectre.Console 0.55.2: Terminal rendering — step panels, spinners, compact/verbose markup
- Argu 6.2.5: CLI arg parsing — DU-backed schema, auto-generates --help
- Serilog 4.3.1: Structured logging to stderr — separates log stream from Spectre UI on stdout
- Expecto 10.x: Test runner — F#-native, function-based tests, no [<Fact>] attribute friction

### Expected Features

The feature set is intentionally narrow. Every feature outside the v1 minimal list has an explicit SKIP or DEFER verdict backed by scope, Qwen-specific limitations, or the anti-feature analysis.

**Must have (v1 — locked by project scope):**
- Agent loop: prompt to LLM to tool to observe to loop (max 5 iterations)
- Structured JSON output enforcement ({thought, action, input}) with extraction pipeline and 2-attempt retry
- read_file — with line-range support to avoid context explosion
- write_file — full overwrite, path traversal validation
- list_dir — non-recursive, depth-limited
- run_shell — with shell security validator (ported from Python bash_security.py), 30s timeout, 100KB stdout cap
- CLI entry: blueCode "<prompt>" — Argu-backed, --verbose / --model flags
- Compact/verbose step log (thought / action / input / output / status visible in verbose)
- Context window guard: token preflight estimate + tool output truncation at 2000 chars
- Direct 32B/72B routing via intent DU (classifyIntent to Intent to Model to Endpoint)
- Spinner / elapsed-time display during LLM inference wait
- JSONL step log to ~/.bluecode/session_*.jsonl (crash post-mortem, not session resume)
- Loop guard: reject identical (action, input_hash) tuple appearing 3+ times per turn

**Should have (v1.x — after loop is validated):**
- Streaming token output (SSE from vLLM/Ollama) — trigger: blank terminal UX problem confirmed
- Session persistence + --resume <id> — trigger: context loss on long tasks confirmed
- edit_file (exact-string replace) — trigger: full-file writes cause diff noise
- glob_search / grep_search — trigger: user asks agent to locate symbols

**Defer (v2+):**
- Context compaction / auto-snip (needs stable context guard metrics first)
- Slash commands (/context, /compact) — needs stable multi-turn REPL
- Sub-agent delegation — flat loop must be proven across 50+ real sessions
- CLAUDE.md project memory discovery

**Permanently out of scope (anti-features):**
- MCP runtime, LSP, plugin system, GUI, Windows/Linux support, AOT binary, team/collaboration features, remote runtime, sub-agent recursion in v1

### Architecture Approach

The architecture follows a ports-and-adapters pattern with a closed DU spine. BlueCode.Core is a pure library (no Console, no Main): Domain types to Router to Ports (interfaces) to ContextBuffer to ToolRegistry to Rendering to AgentLoop, compiled in that order. BlueCode.Cli is the impure shell: adapters (QwenHttpClient, FsToolExecutor), composition root (function injection, no DI framework), REPL, and [<EntryPoint>]. The loop is tail-recursive, threading immutable ContextBuffer as a parameter — no mutable fields in the core loop. Errors flow as Task<Result<'T, AgentError>> throughout; all exception-to-Result conversion happens at adapter boundaries.

**Major components:**
1. Domain.fs — All DUs: AgentState, Intent, Model, Tool, LlmOutput, AgentError, Step. Compiled first; everything depends on this.
2. Router.fs — Pure classifyIntent: string -> Intent and intentToModel: Intent -> Model. Trivially testable; no IO.
3. Ports.fs — ILlmClient and IToolExecutor interfaces. These are the only boundaries the agent loop crosses.
4. ContextBuffer.fs — Immutable ring buffer of Step list; toMessages serializes to Message list lazily at LLM call time.
5. ToolRegistry.fs — Static map of ToolName to handler. DU-closed: adding a new tool case forces all match sites to update.
6. AgentLoop.fs — runSession: recursive task {} function threading loopN as parameter; MaxLoopsExceeded is structurally impossible to miss.
7. QwenHttpClient.fs (Cli adapter) — HTTP POST + optional SSE; maps HttpRequestException to Error (LlmUnreachable ...).
8. FsToolExecutor.fs (Cli adapter) — System.IO + System.Diagnostics.Process; maps exceptions to Error (ToolFailure ...).
9. CompositionRoot.fs (Cli) — Wires concrete adapters by function injection; no DI container.
10. Rendering.fs (Core, pure) — renderStep: Step -> RenderMode -> string; printed by Repl.fs, not by Rendering itself.

### Critical Pitfalls

All pitfalls below are Phase 1 blockers. Deferring any one of them creates a situation where the loop appears to work on synthetic inputs but fails in real use.

1. **Qwen JSON output drift (prose-wrapped / markdown-fenced JSON)** — Build a multi-stage extraction pipeline before any tool: (1) bare JSON parse, (2) regex brace-nesting extraction, (3) ```json fence strip, (4) ParseFailure with raw log. Use response_format: {"type": "json_object"} if backend supports it. Qwen 32B produces non-JSON in 5-15% of turns under load.

2. **DU serialization silent breakage** — Register JsonFSharpConverter in JsonSerializerOptions before writing a single serialize/deserialize call. Without it, DUs serialize as {"Case":"...", "Fields":[...]} — valid JSON, wrong schema, silent failure. Use plain F# records for the LLM wire format (LlmStep); use DUs for internal state only.

3. **run_shell security** — Port the security validator chain from bash_security.py. The chain covers command substitution ($(), backtick), IFS injection, destructive patterns (rm -rf), fork bombs, and redirect chains. run_shell must not be deployed before the validator is in place. Also enforce: 30s timeout, 100KB stdout cap, working directory locked to project root.

4. **Context window overflow (128K advertised vs 8K-32K actual)** — Truncate all tool outputs at 2000 characters before appending to message history. Query /v1/models at startup to read max_model_len. Warn (don't crash) when accumulated context approaches 6000 tokens on 32B or 12000 on 72B. Context overflow is an early failure mode on turn 4-5 of any file-heavy session.

5. **Temperature extremes and repetition loops** — Set temperature=0.2 for 32B and temperature=0.4 for 72B as hardcoded defaults. Set presence_penalty=1.5. Never expose raw temperature to the user in v1. Greedy decoding (temperature 0) causes Qwen to repeat the same tool call across all 5 steps; temperature above 0.9 causes malformed JSON.

6. **async {} vs task {} choice** — Commit to task {} exclusively at project start. HttpClient, System.IO, and System.Diagnostics.Process all return Task<T>. Using async {} requires Async.AwaitTask wrappers on every call, and async {} does not support try...finally for async cleanup, which breaks streaming HTTP disposal.

7. **Second-system scope creep** — The Python agent's 70+ modules are visible and tempting. Any module that does not map directly to: CLI entry, HTTP client, agent loop, 4 tools, JSON parser, or step renderer is out of scope. Implement a DEFERRED.md gate.

---

## Implications for Roadmap

Based on the combined research, the build order is dictated by three hard constraints: (1) F# compiles files in declaration order, so types must precede functions; (2) the agent loop cannot be tested until both the LLM client and tool executor exist; (3) the security and JSON-robustness layers must exist before any end-to-end test with real Qwen output.

### Phase 1: Foundation — Types, Routing, and Project Skeleton

**Rationale:** F#'s compile-order dependency means Domain.fs must exist before anything else compiles. This phase has zero external dependencies and is purely in-memory, making it the safest starting point and the fastest feedback loop for validating the DU design.

**Delivers:** BlueCode.Core.fsproj with Domain.fs, Router.fs, Ports.fs, ContextBuffer.fs, ToolRegistry.fs (empty handlers), Rendering.fs (stub). BlueCode.Cli.fsproj skeleton with CompositionRoot.fs and Program.fs entry point. Argu CLI arg parsing wired.

**Addresses features:** 32B/72B intent routing (pure function, testable immediately), DU spine for all agent states.

**Avoids:** B-6 (over-modeling — cap type design at 2 hours per session), A-3 (translation trap — no mutable state in core types), B-5 (Seq accumulation — use Step list from the start).

**Research flag:** Standard patterns. No deeper research needed.

---

### Phase 2: LLM Client and JSON Robustness Layer

**Rationale:** The HTTP client is the highest-risk technical component because it touches all Qwen-specific behaviors. Building it second, before tools exist, means failures are isolated to one adapter. The JSON extraction pipeline and schema validator belong here — not retrofitted after tools are integrated.

**Delivers:** QwenHttpClient.fs (non-streaming HTTP POST, correct disposal pattern), JSON extraction pipeline (bare to brace-nest to fence-strip to ParseFailure), JsonSchema.Net schema validator for {thought, action, input}, FSharp.SystemTextJson converter registration, temperature defaults (0.2 / 0.4), system prompt with bilingual language instruction, startup ping test for system message placement, Spectre.Console spinner for inference wait.

**Uses:** HttpClient, FSharp.SystemTextJson, JsonSchema.Net, FsToolkit.ErrorHandling (taskResult {}), Spectre.Console.

**Implements:** ILlmClient port.

**Avoids:** C-1 (JSON output drift), B-1 (DU serialization silent breakage), C-5 (temperature extremes), C-6 (role format differences), C-7 (tool call emulation — no tools param), B-3 (dispose across async), B-2 (async vs task).

**Research flag:** Qwen-specific JSON behaviors are fully documented in PITFALLS.md. Validate response_format and system prompt placement against actual local endpoints during implementation.

---

### Phase 3: Tool Executor and Security Layer

**Rationale:** Tools are the second high-risk component because they touch the real filesystem and shell. Security validation must be built before any real-input test of run_shell. Phase 3 delivers a complete IToolExecutor — the agent loop in Phase 4 can be wired immediately.

**Delivers:** FsToolExecutor.fs implementing IToolExecutor for all 4 tools. FilePath and Command smart constructors with validation. Shell security validator chain (ported from bash_security.py): control chars, command substitution, IFS injection, destructive patterns, redirects. write_file path traversal guard (Path.GetFullPath against project root). Tool output truncation at 2000 chars. run_shell timeout (30s) + stdout cap (100KB) + stderr cap (10KB). ToolResult DU: Success | Failure | SecurityDenied | PathEscapeBlocked | Timeout.

**Addresses features:** All 4 v1 tools at full quality. Context window guard (tool output truncation).

**Avoids:** D-2 (run_shell security — non-negotiable), D-3 (path escape), D-4 (tool errors swallowed — ToolResult.Failure as structured observation), C-2 (context overflow — tool output truncation here), A-2 (Chesterton's fence — bash_security.py validators exist for real reasons).

**Research flag:** Use bash_security.py from claw-code-agent as the reference. Port logic, not style. No additional research needed.

---

### Phase 4: Agent Loop Integration and End-to-End Validation

**Rationale:** With both ports satisfied (LLM client from Phase 2, tool executor from Phase 3), AgentLoop.fs can be implemented and immediately wired end-to-end. This is the first phase where a real Qwen task can run. Loop guard, retry policy, and error propagation all belong here.

**Delivers:** AgentLoop.fs: recursive task {} loop with loopN parameter, MaxLoopsExceeded structural invariant, 2-attempt JSON parse correction policy, LoopGuard (identical action+input rejected on 3rd occurrence). CompositionRoot.fs wiring real adapters. Repl.fs single-turn flow. Rendering.fs verbose step log. Serilog structured step events to stderr. JSONL step log to ~/.bluecode/session_*.jsonl. Graceful Ctrl+C (OperationCanceledException to step summary, not stack trace).

**Addresses features:** Core agent loop contract, max-iteration cap, step log / transparent trace, loop guard, crash post-mortem JSONL.

**Avoids:** A-1 (second-system scope creep — validate scope list before phase completion), D-1 (same-tool infinite loop — loop guard), D-5 (loss of state — JSONL step log), D-6 (JSON retry loop — 2-attempt bounded policy), D-7 (model switching mid-turn — model fixed at turn start), B-7 (exception in CE — AggregateException unwrapped), A-4 (deferred delivery — set first real task date as v1 success criterion).

**Research flag:** Standard agent loop patterns. taskResult {} CE and recursive task patterns are well-documented. No additional research needed.

---

### Phase 5: CLI Polish and Daily-Driver Switch

**Rationale:** This phase exists to complete the user-facing surface and force the transition from the Python agent to blueCode as the daily driver. Without a hard switch date, blueCode never gets the real-use feedback loop that surfaces edge cases.

**Delivers:** Multi-turn REPL loop (Repl.fs extended). Compact/verbose toggle (--verbose flag, default compact). Spectre.Console step panels, model-selected display, spinner with elapsed time. --model 72b override flag. /v1/models startup query to read actual max_model_len. Context usage warning at 80% of actual limit. Full --help from Argu. Fantomas formatting pass.

**Addresses features:** Compact/verbose output toggle, context window guard (UI warning), streaming UX workaround via spinner.

**Avoids:** C-4 (inference latency UX — spinner active during HTTP wait), C-2 (context overflow — warn at 80% of actual limit queried from server), A-4 (deferred delivery — set daily-driver switch date, move Python agent to retired/ directory).

**Research flag:** Standard patterns. Spectre.Console is well-documented.

---

### Phase Ordering Rationale

- **Types before everything else:** F# compile order is a hard constraint. Domain.fs must compile before any module that references its types.
- **LLM client before agent loop:** The agent loop cannot be meaningfully tested without a real HTTP call. Building JSON robustness in Phase 2 (before tools) isolates Qwen failures to one component.
- **Security before any real run:** run_shell with a live Qwen model will encounter LLM-generated shell commands on the first real task. The security validator must exist before that run.
- **Loop before REPL:** The single-turn runSession contract is simpler to validate than the multi-turn REPL. Phase 4 proves the core contract; Phase 5 wraps it in the interactive surface.
- **No streaming until loop is stable:** SSE streaming introduces backend-specific format differences (vLLM vs Ollama, missing [DONE], partial chunk boundaries). Defer to v1.x after the loop has proven stable on non-streaming responses.

### Research Flags

Phases needing deeper research during planning: None. All phases use well-documented patterns. Qwen-specific behaviors are fully covered by PITFALLS.md with specific vLLM/Ollama issue references.

Phases to validate against actual local environment during implementation:
- **Phase 2:** Validate response_format: {"type": "json_object"} support against local vLLM/Ollama version. Have the prose-extraction fallback ready regardless.
- **Phase 2:** Validate system prompt placement (system role vs prepended user message) via ping test.
- **Phase 5:** Query /v1/models to read actual max_model_len — field name has changed across vLLM releases.

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All packages verified on NuGet with .NET 10 compatibility. Official .NET 10 / F# 10 docs for new features. |
| Features | HIGH | Sourced directly from user's own design notes + Python reference implementation. Feature scope is set by the user, not inferred. |
| Architecture | HIGH | Ports-and-adapters with DU spine is the established F# pattern. File compilation order requirements verified against F# spec. |
| Pitfalls | HIGH | Qwen-specific pitfalls sourced from vLLM issue tracker, langchain4j issues, Qwen HuggingFace discussions, Qwen model cards, and Trail of Bits agent security research. |

**Overall confidence:** HIGH

### Gaps to Address

- **Streaming (v1.x):** SSE parsing is documented but exact behavior of the local vLLM/Ollama versions is not pre-validated. When streaming is added in v1.x, test against the specific backend version in use.
- **Ollama presence_penalty:** Ollama silently ignores presence_penalty (ollama/ollama #14493). If the local backend is Ollama, repetition loops may still occur. The loop guard (Phase 4) is the structural mitigation that works regardless of backend.
- **Intent classification quality:** The keyword-based classifyIntent is deterministic but brittle. A prompt that matches "write" maps to 32B but may need 72B reasoning. The --model 72b override is the v1 escape hatch; automatic per-turn routing is v2.
- **Context window actual limit:** The max_model_len value varies by backend version and startup flags. The 6000/12000 token warning thresholds are conservative starting points, not tuned values.

---

## Sources

### Primary (HIGH confidence)

- [What's new in .NET 10 Libraries](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries) — JsonSerializerOptions.Strict, SseParser, PipeReader deserialization
- [What's new in F# 10](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/fsharp-10) — and! in task CE, tail-call CEs, attribute enforcement
- [FSharp.SystemTextJson GitHub](https://github.com/Tarmil/FSharp.SystemTextJson) — v1.4.36, June 2025
- [FsToolkit.ErrorHandling NuGet](https://www.nuget.org/packages/FsToolkit.ErrorHandling/) — v5.2.0, Feb 2026
- [FSharp.Control.TaskSeq NuGet](https://www.nuget.org/packages/FSharp.Control.TaskSeq/) — v1.1.1, Apr 2026
- [JsonSchema.Net NuGet](https://www.nuget.org/packages/JsonSchema.Net/) — v9.2.0, Apr 2026
- [Spectre.Console NuGet](https://www.nuget.org/packages/Spectre.Console/) — v0.55.2, Apr 2026
- [Argu NuGet](https://www.nuget.org/packages/Argu/) — v6.2.5, Dec 2024
- [Serilog NuGet](https://www.nuget.org/packages/Serilog/) — v4.3.1, Feb 2026
- [Functional Architecture is Ports and Adapters — Mark Seemann](https://blog.ploeh.dk/2016/03/18/functional-architecture-is-ports-and-adapters/)
- [Designing with types: Single case union types — F# for Fun and Profit](https://fsharpforfunandprofit.com/posts/designing-with-types-single-case-dus/)
- [Against Railway-Oriented Programming — Scott Wlaschin](https://fsharpforfunandprofit.com/posts/against-railway-oriented-programming/)
- vLLM issue #16340 — Missing "type":"function" in streaming tool calls
- langchain4j issue #2680 — Qwen2.5 JSON parameter formatting in tool calls
- [Qwen/QwQ-32B-Preview HuggingFace discussion — Chinese characters in responses](https://huggingface.co/Qwen/QwQ-32B-Preview/discussions/16)
- [Trail of Bits — Prompt injection to RCE in AI agents (2025)](https://blog.trailofbits.com/2025/10/22/prompt-injection-to-rce-in-ai-agents/)
- ollama/ollama #14493 — Ollama silently discards presence_penalty
- [dotnet/runtime #98347 — Process deadlock / WaitForExitAsync](https://github.com/dotnet/runtime/issues/98347)

### Secondary (MEDIUM confidence)

- /Users/ohama/projs/blueCode/localLLM/qwen_claude_full_design.md — user's agent loop design (max 5 loops, strict JSON, minimal tools)
- /Users/ohama/projs/blueCode/localLLM/qwen_agent_rewrite.md — Qwen-oriented design principles
- /Users/ohama/projs/blueCode/localLLM/agent_32b_72b_codegpt.md — intent classification and routing logic
- /Users/ohama/projs/claw-code-agent/README.md + PARITY_CHECKLIST.md — Python reference implementation feature surface
- /Users/ohama/projs/claw-code-agent/src/bash_security.py — shell security validator reference (18 validator functions to port)

---
*Research completed: 2026-04-22*
*Ready for roadmap: yes*
