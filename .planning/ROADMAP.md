# Roadmap: blueCode

## Overview

blueCode is a strong-typed F# agent loop that drives locally-served Qwen 32B/72B on a Mac. The build follows a single hard constraint: domain types compile before everything else, the LLM client and tool executor must both exist before the agent loop can run, and the security layer arrives before any live Qwen shell command executes. Five phases deliver a working daily-driver that replaces the Python claw-code-agent.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

- [x] **Phase 1: Foundation** - DU spine, routing pure functions, and project skeleton ✓ 2026-04-22
- [ ] **Phase 2: LLM Client** - HTTP client, JSON extraction pipeline, schema validation
- [ ] **Phase 3: Tool Executor** - 4 tools with security layer and output truncation
- [ ] **Phase 4: Agent Loop** - End-to-end loop, guards, JSONL step log, Ctrl+C
- [ ] **Phase 5: CLI Polish** - Multi-turn REPL, verbose/compact toggle, daily-driver switch

## Phase Details

### Phase 1: Foundation
**Goal**: The project compiles, domain types express every legal agent state, and routing from user input to model selection is a pure testable function.
**Depends on**: Nothing (first phase)
**Requirements**: FND-01, FND-02, FND-03, FND-04, ROU-01, ROU-02, ROU-03
**Success Criteria** (what must be TRUE):
  1. `dotnet run` in BlueCode.Cli builds and exits cleanly with no output (skeleton entry point).
  2. All DUs — `AgentState`, `Intent`, `Model`, `Tool`, `LlmOutput`, `AgentError`, `Step`, `ToolResult` — are defined in Domain.fs and a match on any of them produces a compile error if a case is missing.
  3. `classifyIntent "fix the null check"` returns `Intent.Debug` and `intentToModel Intent.Debug` returns `Model.Qwen72B` — verifiable in a unit test with no IO.
  4. `task {}` CE compiles throughout Core; any `async {}` expression in Core.fsproj is a build error.
  5. `taskResult {}` CE from FsToolkit.ErrorHandling compiles in at least one module.
**Plans**: 3 plans

Plans:
- [x] 01-01: Solution scaffold — two-project layout, NuGet refs, F# compile order in .fsproj files
- [x] 01-02: Domain.fs + Router.fs — all DU cases, classifyIntent, intentToModel, modelToEndpoint
- [x] 01-03: Ports.fs + ContextBuffer.fs + ToolRegistry stub + async-ban CI script (CLI entry stays literal stub until Phase 5)

---

### Phase 2: LLM Client
**Goal**: A POST to localhost:8000 with a prompt returns a validated `LlmStep` record; every failure mode maps to a typed `AgentError` before leaving the adapter.
**Depends on**: Phase 1
**Requirements**: LLM-01, LLM-02, LLM-03, LLM-04, LLM-05, LLM-06
**Success Criteria** (what must be TRUE):
  1. `POST /v1/chat/completions` to localhost:8000 with a minimal prompt returns a parsed `LlmStep { thought; action; input }` record with schema-validated fields.
  2. A response containing prose-wrapped or markdown-fenced JSON (e.g., `Here is the JSON: \`\`\`json {...} \`\`\``) is correctly extracted and parsed rather than failing.
  3. A response that is not recoverable JSON returns `AgentError.InvalidJsonOutput` (not an unhandled exception).
  4. An `HttpRequestException` (Qwen unreachable) maps to `AgentError.LlmUnreachable` — no exception propagates out of the adapter.
  5. A Spectre.Console spinner is visible in the terminal during the HTTP wait and disappears when the response arrives.
**Plans**: 3 plans

Plans:
- [ ] 02-01: QwenHttpClient.fs — HTTP POST, disposal pattern, temperature defaults, FSharp.SystemTextJson converter
- [ ] 02-02: JSON extraction pipeline — bare → brace-nest → fence-strip → ParseFailure; JsonSchema.Net validator
- [ ] 02-03: Error mapping + spinner integration (Spectre.Console); manual smoke test against live localhost:8000

---

### Phase 3: Tool Executor
**Goal**: All four tools execute safely; run_shell rejects dangerous commands before execution; every tool outcome is expressed as a typed `ToolResult` that callers cannot ignore.
**Depends on**: Phase 2 (NuGet + project structure; tool logic itself is independent)
**Requirements**: TOOL-01, TOOL-02, TOOL-03, TOOL-04, TOOL-05, TOOL-06, TOOL-07
**Success Criteria** (what must be TRUE):
  1. `read_file` with a valid path returns file content; with an optional line range it returns only those lines.
  2. `write_file` with a path outside the project root returns `ToolResult.PathEscapeBlocked`; a valid path writes the file.
  3. `run_shell "rm -rf /"` returns `ToolResult.SecurityDenied` without executing; a safe command executes and returns stdout truncated to 100KB.
  4. A `run_shell` command that runs longer than 30 seconds returns `ToolResult.Timeout`.
  5. Any tool whose output exceeds 2000 characters returns the first 2000 characters with a truncation marker before being appended to message history.
  6. A pattern match on `ToolResult` without all five cases (`Success | Failure | SecurityDenied | PathEscapeBlocked | Timeout`) is a compile error.
**Plans**: 3 plans

Plans:
- [ ] 03-01: FsToolExecutor.fs — read_file, write_file, list_dir with path validation and output truncation
- [ ] 03-02: run_shell — 30s timeout, stdout/stderr caps, working-directory lock
- [ ] 03-03: Shell security validator chain — port bash_security.py (command substitution, IFS, destructive patterns, fork bomb, redirect chain)

---

### Phase 4: Agent Loop
**Goal**: A single turn runs prompt → LLM → tool → observe up to 5 times and produces a final answer; every error and limit condition is a typed value the caller handles at compile time.
**Depends on**: Phase 2 (ILlmClient), Phase 3 (IToolExecutor)
**Requirements**: LOOP-01, LOOP-02, LOOP-03, LOOP-04, LOOP-05, LOOP-06, LOOP-07, OBS-01, OBS-02, OBS-04
**Success Criteria** (what must be TRUE):
  1. Running `blueCode "list files in the current directory"` against live Qwen completes in ≤5 tool steps and prints a final answer to stdout.
  2. When the 5-step limit is reached without a final answer, the program exits with `AgentError.MaxLoopsExceeded` rendered as a user-readable message — no stack trace.
  3. The same `(action, input_hash)` pair appearing for the third time in a turn is rejected by the loop guard and the turn ends with a descriptive error.
  4. A JSON parse failure retries once; if both attempts fail the turn exits with `AgentError.InvalidJsonOutput` — not a crash.
  5. Pressing Ctrl+C during LLM inference prints a one-line step summary and exits cleanly — no `OperationCanceledException` stack trace.
  6. Every step is written as a JSONL line to `~/.bluecode/session_<timestamp>.jsonl` and readable after the process exits.
  7. Every JSONL step record includes `startedAt`, `endedAt`, and `durationMs` fields (additive extension of the Step record from Phase 1); `--verbose` output also shows per-step elapsed time (OBS-04).
**Plans**: 3 plans

Plans:
- [ ] 04-01: AgentLoop.fs — recursive task {} loop, loopN parameter, MaxLoopsExceeded, loop guard, 2-attempt JSON retry
- [ ] 04-02: CompositionRoot.fs + single-turn Repl.fs — wire real adapters, run first real end-to-end task
- [ ] 04-03: Rendering.fs verbose step log + Serilog stderr + JSONL step logger + Ctrl+C handler

---

### Phase 5: CLI Polish
**Goal**: blueCode is the daily driver; the Python claw-code-agent is retired; multi-turn REPL, compact/verbose toggle, and context-window warning are in place.
**Depends on**: Phase 4
**Requirements**: CLI-01, CLI-02, CLI-03, CLI-04, CLI-05, CLI-06, CLI-07, OBS-03, ROU-04
**Success Criteria** (what must be TRUE):
  1. `blueCode "<prompt>"` (single-turn) and `blueCode` (multi-turn REPL with `/exit`) both work; `--help` prints correct usage from Argu.
  2. `blueCode --verbose "<prompt>"` prints thought/action/input/output/status for every step; default compact mode prints one summary line per step (e.g., `> reading file...`).
  3. `blueCode --model 72b "<prompt>"` routes to localhost:8001 regardless of intent classification.
  4. At startup, `/v1/models` is queried and the actual `max_model_len` is used; when accumulated context reaches 80% of that limit, a visible warning is printed before the next LLM call.
  5. `~/projs/claw-code-agent/` has been moved to `~/projs/claw-code-agent-retired/` and at least one real coding task has been completed using blueCode as the sole agent.
  6. `blueCode --trace "<prompt>"` emits Serilog Debug-level structured JSON (stderr) with each step's full untruncated input, output, and `elapsed_ms`; `--trace` is independent of `--verbose` and default OFF (CLI-07).
**Plans**: 3 plans

Plans:
- [ ] 05-01: Multi-turn REPL (Repl.fs extended) + `/exit` + Ctrl+D + `--model` override flag (ROU-04)
- [ ] 05-02: Compact/verbose rendering + Spectre.Console step panels + elapsed-time spinner
- [ ] 05-03: `/v1/models` startup query, 80% context warning, Fantomas pass, retire Python agent

---

## Progress

**Execution Order:** 1 → 2 → 3 → 4 → 5

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundation | 3/3 | ✓ Complete | 2026-04-22 |
| 2. LLM Client | 0/3 | Not started | - |
| 3. Tool Executor | 0/3 | Not started | - |
| 4. Agent Loop | 0/3 | Not started | - |
| 5. CLI Polish | 0/3 | Not started | - |
