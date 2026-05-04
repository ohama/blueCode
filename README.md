# blueCode

A strong-typed F# agent loop that drives a locally-served Qwen 3.5 122B model on a Mac. Replaces a Python `claw-code-agent` as a single-user daily-driver coding assistant, with strict JSON output, a 10-step loop cap, plan-then-execute mode, persistent multi-turn sessions, and typed `AgentError` / `ToolResult` discriminated unions that force exhaustive matching at compile time.

**Status:** v2.4 shipped (2026-04-29) — 10 milestones tagged, ~345 Expecto tests, bench gate 7/7 PASS, empirical eval verdict **96/100 KEEP**. v2.5 (REPL ergonomics) in progress: Phases 31 + 32 + 36 shipped; Phases 33–35 planned.

## Quick Demo

With Qwen 3.5 122B-A10B-4bit served via `mlx_lm.server` on `localhost:8001` (see [setup](documentation/qwen35-install.md)):

```bash
$ dotnet run --project src/BlueCode.Cli -- --verbose "Say OK in 3 words"

[Step 1] (ok, 1422ms)
  thought: The user wants a simple three-word phrase confirming readiness.
  action:  final: OK
  result:  (final answer — no tool)

OK
```

Multi-turn REPL with slash commands:

```bash
$ dotnet run --project src/BlueCode.Cli
blueCode — multi-turn mode. Session: 4d1f… Type /exit or press Ctrl+D to quit.
blueCode> /help
slash commands:
  /help              show this help
  /status            session info: id, model, steps, context %
  /clear             reset session in-place (new session id, keep REPL running)
  /exit              save session and quit
  /quit              alias for /exit
  /sessions          list 10 most-recent sessions
  /resume <id>       switch to a saved session in-place
  /plan              toggle plan-mode for next turn [coming in v2.5]
  /edit              open $EDITOR for multi-line input [coming in v2.5]
blueCode> /exit
```

## Tool Set

Seven tools, one tool per step, max 10 iterations per turn (raised from 5 in v2.2):

| Tool | Purpose |
|------|---------|
| `read_file` | Path + optional line range; structured `[file:..., lines X-Y of Z, not-truncated\|truncated\|out-of-range]` header |
| `write_file` | Path traversal blocked; project-root locked (extend with `--allow-paths`) |
| `edit_file` | Exact-string find-and-replace; 0 / 1 / N match handling; terminal-tool wording (v1.2 + 9.1) |
| `list_dir` | Non-recursive, depth-limited |
| `glob_search` | Project-rooted pattern finder; bare patterns auto-expanded recursively (v1.2 + 36-01) |
| `grep_search` | Regex content search with ReDoS guard; structured `(path, line, content)` output |
| `run_shell` | 30s timeout, 22-validator bash security chain ported from `bash_security.py`, stdout 100KB cap |

`final` (return answer) and `plan` (emit numbered plan; `--plan` mode only) round out the JSON `action` enum.

## CLI Flags

```
blueCode [OPTIONS] [<prompt>]

  --model 122b|35b        Force model selection (default: 122b)
  --with-35b              Enable --model 35b (requires loaded launchd service)
  --resume <id>           Resume session by id from ~/.bluecode/sessions/<id>.jsonl
  --new-session           Force a fresh session id (mutually exclusive with --resume)
  --plan                  Plan-then-execute mode: LLM emits a numbered plan, user
                          approves a/r/e/q before any tool runs (single-turn only)
  --allow-paths <csv>     Extra path prefixes the agent may read/write
                          (canonicalized; trailing-separator prefix-attack guarded)
  --verbose               Per-step thought/action/input/output lines on stdout
  --trace                 Serilog Debug JSON to stderr (full untruncated per-step)
  --help                  Usage from Argu

Positional <prompt>: single-turn mode
No prompt:           multi-turn REPL (/exit or Ctrl+D to quit)
```

## REPL Slash Commands

When invoked without a positional prompt, blueCode enters a multi-turn REPL. Slash commands are intercepted in-process — no LLM call is made for any of the commands below.

| Command | Behavior |
|---------|----------|
| `/help` | Print the 9-command listing above |
| `/status` | Show session id, model, accumulated step count, char count, % of `max_model_len` |
| `/clear` | Reset session in-place — new session id, prior steps cleared, REPL keeps running. Old session jsonl is left untouched on disk |
| `/exit`, `/quit` | Save the current session and exit cleanly (exit 0). `Environment.Exit` is **not** used — Serilog flush + .NET finalizers run normally |
| `/sessions` | List the 10 most-recent sessions under `~/.bluecode/sessions/` (id, started timestamp, turn count, first-thought excerpt). Sorted by mtime descending; corrupt files silently skipped |
| `/resume <id>` | Switch to a saved session in-place. Unknown id → friendly `Session not found:` error; corrupt jsonl → `Session file corrupt:` error; both keep the REPL alive. On success, the next turn's LLM call sees the loaded session's prior steps |
| `/plan` | (v2.5 stub) Toggle plan-mode for the next turn |
| `/edit` | (v2.5 stub) Open `$EDITOR` for multi-line prompt entry |

Unknown slash inputs (e.g. `/foobar`) safely fall back to `/help` rather than crashing.

## Requirements

- **macOS** (Mac-only by design; Windows/Linux out of scope)
- **.NET 10** SDK
- **Qwen 3.5 122B-A10B-4bit MoE** served OpenAI-compat at `localhost:8001` via `mlx_lm.server` + launchd — see [`documentation/qwen35-install.md`](documentation/qwen35-install.md) for the canonical setup
- **~45–62 GB free RAM** depending on whether the standby 35B service is also loaded (122B alone resident ~45 GB; OS/KV overhead ~17 GB)
- (Optional) **Qwen 3.5 35B-A3B-4bit** at `localhost:8000` — kept on disk as cold rollback; not loaded by default; opt-in via `launchctl load` + `--with-35b`

> Both launchd plists pass `--chat-template-args '{"enable_thinking": false}'` to suppress `<think>...</think>` tokens that would otherwise break the strict JSON schema. See `documentation/qwen35-install.md` §6 for the Path B fallback if a future mlx_lm version drops the flag.

## Getting Started

1. **Set up the local Qwen server** — follow [`documentation/qwen35-install.md`](documentation/qwen35-install.md). Verify `curl -fsS http://127.0.0.1:8001/v1/models` returns the 122B id.
2. **Clone and build:**
   ```bash
   git clone https://github.com/ohama/blueCode.git
   cd blueCode
   dotnet build BlueCode.slnx
   ```
3. **Test run:**
   ```bash
   dotnet run --project src/BlueCode.Cli -- "List the files in src"
   ```
4. **Run the regression gate** before committing changes that touch `src/`:
   ```bash
   bash bench/run.sh --gate
   ```
   Should report `7/7 PASS` against `bench/baseline.json`.

## Architecture

Two projects, ports-and-adapters, closed DU spine. **Core is pure** — no Serilog, Spectre, Argu, or HTTP references. Enforced by `scripts/check-no-async.sh` (CI bans `async {}` literal in Core) and a `git diff` invariant (`src/BlueCode.Core/**` empty across measurement-only milestones like v2.1 and v2.4).

```
BlueCode.Core (pure)
  ├── Domain.fs         All DUs: AgentState, Intent, Model, Tool, LlmOutput, Plan,
  │                              AgentError, Step, ToolResult, LlmResponse, Session
  ├── Router.fs         Pure classifyIntent / intentToModel / modelToSamplingParams
  ├── Ports.fs          ILlmClient, IToolExecutor, ISessionStore, IKeyReader
  ├── ContextBuffer.fs  Immutable ring buffer (last N=3 steps)
  ├── PlanValidator.fs  Pure validatePlan (length, unknown tool, dup adjacent,
  │                     rename-target enumeration heuristic — v2.3 25-01)
  ├── ToolRegistry.fs   ToolName → Tool dispatcher map
  └── AgentLoop.fs      runSession + recursive runLoop, MaxLoops / LoopGuard /
                        2-retry / lastEditPath + lastReadHint loop-injection /
                        runPlanTurn (--plan path)

BlueCode.Cli (all impure)
  ├── Adapters/
  │   ├── QwenHttpClient.fs    ILlmClient via mlx_lm.server chat completions;
  │   │                        per-port Lazy<ModelInfo>; local-path id preference
  │   ├── FsToolExecutor.fs    IToolExecutor over System.IO / Process; --allow-paths gate
  │   ├── BashSecurity.fs      22-validator chain (ported bash_security.py)
  │   ├── FileSessionStore.fs  ISessionStore over ~/.bluecode/sessions/<id>.jsonl
  │   │                        + listRecent for /sessions (Cli-only, NOT on interface)
  │   ├── JsonlSink.fs         Per-step JSONL crash log to ~/.bluecode/session_*.jsonl
  │   ├── LlmWire.fs / Json.fs JSON extraction + schema validator (9-value action enum)
  │   └── Logging.fs           Serilog stderr + LoggingLevelSwitch for --trace
  ├── CliArgs.fs               Argu DU schema (8 flags)
  ├── SlashCommand.fs          Pure parser: string → ParsedInput option (8-variant DU)
  ├── PlanGate.fs              Spectre numbered table + a/r/e/q approval dispatch
  ├── CompositionRoot.fs       Sync bootstrap; system prompt + planSystemPromptSuffix
  ├── Repl.fs                  Single-turn + multi-turn REPL with slash dispatcher
  ├── Rendering.fs             Compact / Verbose step rendering + renderHelp/Status/Sessions
  └── Program.fs               [<EntryPoint>]
```

**Key invariants enforced:**

- `task {}` CE only in Core (CI grep blocks `async {}`)
- Ports-and-adapters: `BlueCode.Core` has zero refs to Serilog / Spectre / Argu / HTTP / file I/O
- `ISessionStore` interface frozen at `Save + Load` only — `listRecent` lives in the Cli adapter module, never on the interface
- Stream separation: Serilog → stderr, `printfn` / Spectre → stdout. Slash command output uses `printfn` only (Spectre would bypass `Console.SetOut` capture in tests)
- Test discovery: explicit `rootTests` list in `RouterTests.fs` (no `[<Tests>]` auto-discovery). New test modules need entries in BOTH `BlueCode.Tests.fsproj` `<Compile>` order AND the `rootTests` list
- `Role = User` for all mid-conversation message injections — Qwen 3.5 122B chat template rejects mid-stream `Role = System` with HTTP 404 (Phase 17-02 + 20-03)
- `bench/baseline.json` byte-equal across milestones unless a baseline-changing phase explicitly updates it

## Project Layout

```
blueCode/
├── src/BlueCode.Core/    Pure domain + routing + agent loop + plan validator
├── src/BlueCode.Cli/     Adapters + CLI + REPL + composition root
├── tests/BlueCode.Tests/ Expecto suite (~345 tests; canonical runner =
│                         `dotnet run --project tests/BlueCode.Tests`, NOT `dotnet test`)
├── bench/                Regression harness — run.sh modes (--gate, --regression,
│   ├── run.sh                --canary, --all, --b2); 7-fixture baseline.json;
│   ├── baseline.json         eval-qwen35-122b.sh (~700 lines, 9 modes); F# fixture
│   ├── eval-qwen35-122b.sh   pairs under fixtures/fs_idiomatic/
│   └── fixtures/
├── documentation/        Operations + how-to + eval reference
│   ├── qwen35-install.md             Canonical 122B/35B launchd setup
│   ├── qwen35-122b-coding-eval.md    100-point empirical scorecard (96/100 KEEP)
│   ├── bench.md                      bench/run.sh user guide
│   ├── manual-test-guide.md          ~100 manual REPL acceptance tests
│   └── howto/                        Reusable lessons (debug-llm-server,
│                                     handle-expecto-console, macos-bash-strict-mode, …)
├── .planning/            GSD workflow state — PROJECT.md, STATE.md, ROADMAP.md,
│                         REQUIREMENTS.md, MILESTONES.md, milestones/v*-archives,
│                         phases/<N>-<name>/{RESEARCH,PLAN,SUMMARY,VERIFICATION}.md
├── scripts/              CI scripts (check-no-async.sh, bc / preflight launchers)
├── BlueCode.slnx         Solution file
├── README.md             (this file)
└── CLAUDE.md             Developer context for AI sessions — load-bearing conventions
```

## Quality Gates

- **Bench gate** (`bash bench/run.sh --gate`) — the structural authority. 7 fixtures (T6 multi-file refactor, W1 bug-fix-and-write, W2 list-bug-fix, T1 read+answer, T5 sequence, B2 divide-by-zero diagnose, MT multi-turn) under ~2 min. Compares against `bench/baseline.json` via jq diff; exits non-zero on regression. Run after any change touching `src/`, `defaultSystemPrompt`, `planSystemPromptSuffix`, sampling params, or `bench/fixtures/`.
- **Empirical eval** (`bash bench/eval-qwen35-122b.sh --full`) — observational 100-point scorecard. Reproduces the verdict in `documentation/qwen35-122b-coding-eval.md`. Run when changing model, sampling params, or major prompt edits.
- **Test suite** (`dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj`) — Expecto, ~345 tests, must be green before committing.
- **No-async check** (`bash scripts/check-no-async.sh`) — bans `async {}` literal in Core.

Mandatory pre-flight before evaluation runs: `launchctl kickstart -k gui/501/com.ohama.qwen122b` clears KV cache contamination accumulated over long sessions.

## Milestones

Detailed history in [`.planning/MILESTONES.md`](.planning/MILESTONES.md). Concise summary:

| Version | Shipped | Theme |
|---------|---------|-------|
| v1.0 | 2026-04-23 | MVP — agent loop, 4 tools, JSON pipeline, CLI |
| v1.1 | 2026-04-24 | Dynamic `/v1/models`, lazy probe, real LLM thought capture |
| v1.2 | 2026-04-26 | Tool expansion (`edit_file`, `glob_search`, `grep_search`); loop-injection primitive |
| v1.3 | 2026-04-26 | Bench harness in repo + 54% prompt shrink + B2 recovery |
| v1.4 | 2026-04-26 | Test hygiene + bench polish (EXIT trap, MockHelpers consolidation) |
| v2.0 | 2026-04-27 | Persistence (`--resume`) + plan-then-execute + Qwen 3.5 122B canonical (-85 GB disk) |
| v2.1 | 2026-04-28 | Empirical eval scorecard 82/100 KEEP (1051-line eval doc) |
| v2.2 | 2026-04-28 | Multi-file ceiling 5→10 + cold-start measurement; 87/100 |
| v2.3 | 2026-04-29 | Comprehension layer — CORR-EVAL-02 PASS via P1 enumeration directive; 92/100 |
| v2.4 | 2026-04-29 | Coding-quality measurement-first — F# fixtures disprove 1/5 artifact; **96/100 KEEP** (zero `src/` diff) |
| v2.5 | in progress | REPL ergonomics — slash commands (Phase 31+32 ✓), `/edit` + readline planned |

## Design Origins

blueCode is an F# rewrite of the author's earlier Python `claw-code-agent`, shedding 65+ modules down to a minimal "simple → evolve" core. Claude Code's architecture is a reference but its prompts are **not** reused (Qwen 3.5 produces format errors on Claude-style prompts; v1's 1689-char prompt was shrunk 54% to 783 chars in v1.3 specifically for Qwen). Design notes live in [`localLLM/`](localLLM/).

## License

Private / personal. Not published as a general-purpose tool.

---

For developer context (conventions, seams, gotchas), see [CLAUDE.md](CLAUDE.md).
For empirical model verdict, see [`documentation/qwen35-122b-coding-eval.md`](documentation/qwen35-122b-coding-eval.md).
For milestone history, see [`.planning/MILESTONES.md`](.planning/MILESTONES.md).
