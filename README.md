# blueCode

A strong-typed F# agent loop that drives locally-served Qwen 32B / 72B on a Mac. Replaces a Python `claw-code-agent` as a single-user daily-driver coding assistant, with strict JSON output, max-5-iteration cap, and typed `AgentError` / `ToolResult` discriminated unions that force exhaustive matching at compile time.

**Status:** v1.1 shipped (2026-04-24) — 218 tests passing, 2 milestones archived.

## Quick Demo

With Qwen 32B / 72B served via `mlx_lm.server` (see [setup](documentation/local-llm-services.md)):

```bash
$ dotnet run --project src/BlueCode.Cli -- --verbose --model 32b "Say OK in 3 words"

[Step 1] (ok, 6473ms)
  thought: The user wants a simple phrase.
  action:  final: OK
  result:  (final answer — no tool)

OK
```

## Tool Set (v1)

Four tools, one tool per step, max 5 iterations per turn:

- `read_file` — path + optional line range
- `write_file` — path traversal blocked, project-root locked
- `list_dir` — non-recursive, depth-limited
- `run_shell` — 30s timeout, 22-validator bash security chain ported from `bash_security.py`, stdout 100KB cap

## CLI Flags

```
blueCode [OPTIONS] [<prompt>]

  --model 32b|72b    Force model selection (bypasses intent classification)
  --verbose          Per-step thought/action/input/output lines on stdout
  --trace            Serilog Debug JSON to stderr (full untruncated per-step)
  --help             Usage from Argu

Positional <prompt>: single-turn mode
No prompt:           multi-turn REPL (/exit or Ctrl+D to quit)
```

Intent classification (`Debug | Design | Analysis | Implementation | General`) auto-routes to 32B or 72B unless `--model` overrides.

## Requirements

- **macOS** (Mac-only by design; Windows/Linux out of scope)
- **.NET 10** SDK
- **Qwen 32B + 72B** served OpenAI-compat at `localhost:8000` / `localhost:8001` — see `documentation/local-llm-services.md` for launchd setup
- **~55GB free RAM** if running both models simultaneously (tested on 128GB Mac)

## Getting Started

1. **Set up local Qwen servers** — follow [`documentation/local-llm-services.md`](documentation/local-llm-services.md). You need the **Instruct** variant of each model; see [`documentation/qwen32b-base-to-instruct.md`](documentation/qwen32b-base-to-instruct.md) to verify your 32B is not accidentally the Base Coder.
2. **Clone and build**:
   ```bash
   git clone https://github.com/ohama/blueCode.git
   cd blueCode
   dotnet build BlueCode.slnx
   ```
3. **Test run** (expects both Qwen services up):
   ```bash
   dotnet run --project src/BlueCode.Cli -- --model 72b "List the files in src"
   ```

## Architecture

Two projects, ports-and-adapters, closed DU spine. **Core is pure** — no Serilog, Spectre, Argu, or HTTP references.

```
BlueCode.Core (pure)
  ├── Domain.fs        All DUs: AgentState, Intent, Model, Tool, LlmOutput,
  │                             AgentError, Step, ToolResult, LlmResponse
  ├── Router.fs        Pure classifyIntent / intentToModel / modelToEndpoint
  ├── Ports.fs         ILlmClient, IToolExecutor — the only boundaries
  ├── ContextBuffer.fs Immutable ring buffer (last N=3 steps)
  └── AgentLoop.fs     runSession + recursive runLoop, MaxLoops / LoopGuard / 2-retry

BlueCode.Cli (all impure)
  ├── Adapters/
  │   ├── QwenHttpClient.fs   ILlmClient via mlx_lm.server chat completions
  │   ├── FsToolExecutor.fs   IToolExecutor over System.IO / Process
  │   ├── BashSecurity.fs     22-validator chain (ported bash_security.py)
  │   ├── JsonlSink.fs        Per-step JSONL to ~/.bluecode/session_*.jsonl
  │   ├── Logging.fs          Serilog stderr + LoggingLevelSwitch for --trace
  │   ├── LlmWire.fs / Json.fs  JSON extraction pipeline + schema validator
  ├── CliArgs.fs              Argu DU schema
  ├── CompositionRoot.fs      Sync bootstrap, no DI container
  ├── Repl.fs                 Single-turn + multi-turn REPL
  ├── Rendering.fs            Compact / Verbose step rendering
  └── Program.fs              [<EntryPoint>]
```

**Key invariants enforced:**

- `task {}` CE only in Core (CI grep blocks `async {}`)
- Ports-and-adapters: `BlueCode.Core` has zero refs to Serilog / Spectre / Argu / HTTP
- Stream separation: Serilog → stderr, printfn / Spectre → stdout
- Test discovery: explicit `rootTests` list in `RouterTests.fs` (no `[<Tests>]` auto-discovery)

## Project Layout

```
blueCode/
├── src/BlueCode.Core/    Pure domain + routing + agent loop
├── src/BlueCode.Cli/     Adapters + CLI + composition root
├── tests/BlueCode.Tests/ Expecto test suite (218 tests)
├── documentation/        Operations guides
│   ├── local-llm-services.md     Qwen launchd services setup
│   ├── qwen32b-base-to-instruct.md  32B Instruct migration
│   └── howto/                    Reusable lessons from development
├── localLLM/             Original design notes (reference)
├── .planning/            Milestone archives + project state (GSD workflow)
│   ├── PROJECT.md        Living project context
│   ├── MILESTONES.md     Shipped milestones log
│   ├── STATE.md          Current position
│   └── milestones/       v1.0-* and v1.1-* archives
├── scripts/              CI scripts (e.g., async-ban check)
├── BlueCode.slnx         Solution file
└── README.md, CLAUDE.md
```

## Milestones

### v1.0 MVP (2026-04-23)
5 phases, 17 plans, 208 tests. Agent loop + 4 tools + JSON pipeline + CLI polish + daily-driver cutover. See `.planning/milestones/v1.0-ROADMAP.md`.

### v1.1 Refinement (2026-04-24)
2 phases, 5 plans (incl. 1 gap closure), 218 tests. Dynamic `/v1/models` resolution with local-path preference, lazy bootstrap probe, real LLM thought captured in `Step.Thought`. See `.planning/milestones/v1.1-ROADMAP.md`.

### v1.2 (unscoped)
Seed candidates in `.planning/STATE.md`: per-port `MaxModelLen`, tool extensions (`edit_file` / `grep_search` / `glob_search`), streaming output, session persistence.

## Design Origins

blueCode is an F# rewrite of the author's earlier Python `claw-code-agent`, shedding 65+ modules down to a minimal "simple → evolve" core. Claude Code's architecture is a reference but prompts are **not** reused (Qwen produces format errors on Claude-style prompts). Design notes live in [`localLLM/`](localLLM/).

## License

Private / personal. Not published as a general-purpose tool.

---

For developer context (conventions, seams, gotchas), see [CLAUDE.md](CLAUDE.md).
For milestone history, see [`.planning/MILESTONES.md`](.planning/MILESTONES.md).
