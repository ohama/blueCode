# Claude Code Context for blueCode

Short primer for Claude sessions working on this repo. For the full truth, read the files named below.

## Start Here (in order)

1. `.planning/PROJECT.md` — what this is, core value, validated / active requirements, out of scope
2. `.planning/STATE.md` — current milestone position, pending todos, session continuity
3. `.planning/MILESTONES.md` — shipped history (v1.0, v1.1)
4. `README.md` — user-facing overview (architecture diagram, tool list, CLI flags)

If working on a specific phase, also read:
- `.planning/ROADMAP.md` (current milestone phases)
- `.planning/REQUIREMENTS.md` (current requirements)
- `.planning/phases/<N>-<name>/` (PLAN / SUMMARY / RESEARCH / VERIFICATION for that phase)

Archived work in `.planning/milestones/v1.0-phases/`, `.planning/milestones/v1.1-phases/`.

## GSD Workflows

This repo uses the GSD (get-shit-done) workflow. Available via `/gsd:*` slash commands:

- `/gsd:help` — command reference
- `/gsd:new-milestone` — scope a new milestone (questioning → research → requirements → roadmap)
- `/gsd:plan-phase <N>` — create executable PLAN.md for a phase (research → planner → checker)
- `/gsd:execute-phase <N>` — run all plans in the phase (wave-based, autonomous executor agents)
- `/gsd:verify-work <N>` — UAT gate
- `/gsd:complete-milestone` — archive milestone to `.planning/milestones/v<X.Y>-*` + git tag

Non-GSD skills:
- `/howto` — capture reusable lessons to `documentation/howto/`
- `/commit`, `/push`, `/release`, etc.

## Conventions — Load-Bearing

These are invariants. Violating them breaks the build or hides real bugs.

### Core purity (absolute)

`src/BlueCode.Core/**` must NOT reference Serilog, Spectre, Argu, or any HTTP client. Only DUs, ports (interfaces), pure routing, agent loop. The CI grep script `scripts/check-no-async.sh` also bans `async {}` literal in Core (use `task {}` exclusively).

Ports: `ILlmClient` + `IToolExecutor` are the only boundaries Core crosses.

### Test discovery pattern

**Do not assume `[<Tests>]` auto-discovery works.** This project uses an explicit `rootTests` list in `tests/BlueCode.Tests/RouterTests.fs`. New test modules must be added to BOTH:

1. `tests/BlueCode.Tests/BlueCode.Tests.fsproj` in `<Compile Include="...">` order, BEFORE `RouterTests.fs` (which has `[<EntryPoint>]`)
2. The `rootTests` list in `RouterTests.fs` (e.g., `BlueCode.Tests.MyNewTests.tests`)

Four executors have hit this pitfall across v1.0 + v1.1. Check this first when "tests compile but don't run".

### Console.SetOut in tests

Any test module that touches `Console.SetOut` / `Console.SetError` globals must wrap its `testList` with `testSequenced`. Expecto runs testList items in parallel by default; without `testSequenced`, stdout capture races. See `documentation/howto/handle-expecto-console-redirection.md`.

### Stream separation

- Serilog → stderr (`standardErrorFromLevel = Verbose`)
- printfn / Spectre.Console → stdout

Do not print warnings via `AnsiConsole.MarkupLine` in code paths that tests capture via `Console.SetOut` — AnsiConsole bypasses the redirection. Use `printfn` for testable output; Spectre for pretty TTY-only output.

### Commit protocol

Per-task atomic commits, plan-metadata commits separate from code commits:

| Commit type | Format | Stages |
|-------------|--------|--------|
| Task commit | `{feat,fix,test,refactor,perf,chore}({phase}-{plan}): {name}` | individual files modified by task |
| Plan metadata | `docs({phase}-{plan}): complete {name} plan` | only PLAN.md + SUMMARY.md |
| Phase complete | `docs({phase}): complete {name} phase` | ROADMAP / STATE / REQUIREMENTS (+ VERIFICATION) |
| Milestone complete | `chore: complete v{X.Y} milestone` | archive + PROJECT / STATE updates |

**NEVER** use `git add .` or `git add -A`. Stage files individually. `.claude/` and `localLLM/` are intentionally untracked on this repo — `git add -A` would sweep them in.

## Key Seams (v1.1)

### Model id flow (don't break this)

```
/v1/models response
  └─ data[]: [{"id": "Qwen/Qwen2.5-Coder-32B"},        // HF id — triggers mlx_lm.server HF fallback!
              {"id": "/Users/ohama/llm-system/models/qwen32b"}]  // local path — safe

tryParseModelId (QwenHttpClient.fs)
  └─ Prefers id starting with "/" (local path); falls back to data[0].id

probeModelInfoAsync
  └─ ModelInfo { ModelId; MaxModelLen } — cached via Lazy<Task<ModelInfo>> per port

CompleteAsync messages model ct
  └─ awaits probe8000 or probe8001 based on `model`, passes info.ModelId to buildRequestBody
  └─ POST body "model" field = local path → mlx_lm.server keeps loaded Instruct tokenizer
```

**The path-preference heuristic is critical.** If blueCode sends the HF repo id in the request, `mlx_lm.server` fetches a Base Coder tokenizer from HuggingFace and overwrites the loaded Instruct template. Responses become FIM-mode continuations (echo system prompt, `<|fim_*|>` tokens). Symptoms include `InvalidJsonOutput` after ~290s per request. See `documentation/howto/debug-local-llm-server-responses.md` for the diagnostic approach.

### LLM thought flow (v1.1 OBS-05)

```
LLM response JSON
  └─ step.thought (string, schema-enforced minLength:1)

toLlmOutput (QwenHttpClient.fs)
  └─ returns LlmResponse { Thought = Thought step.thought; Output = ... }

ILlmClient.CompleteAsync
  └─ returns Task<Result<LlmResponse, AgentError>>

AgentLoop.runLoop
  └─ destructures { Thought = thought; Output = ... }
  └─ Step { Thought = thought; ...; StartedAt; EndedAt; DurationMs }

Rendering.renderStep Verbose
  └─ reads step.Thought, prints "  thought: ..." line
```

`Step.Thought` is no longer the `"[not captured in v1]"` placeholder; it's real LLM reasoning, non-empty (schema-enforced).

### Bootstrap (v1.1 REF-02)

`CompositionRoot.bootstrap` is synchronous and touches no network. It returns `AppComponents` with pre-allocated `Lazy<Task<ModelInfo>>` cells. The `/v1/models` probe fires on the first call to `CompleteAsync` per port. If the user targets only 72B, port 8000 is never contacted.

## Runtime Environment

- macOS only (Mac `ohama`)
- Qwen 3.5 35B-A3B-4bit (MoE; ~3B activated/token) @ `localhost:8000` via `mlx_lm.server` + launchd (`com.ohama.qwen35b.plist`)
- Qwen 3.5 122B-A10B-4bit (MoE; ~10B activated/token) @ `localhost:8001` same pattern
- Model paths: `~/llm-system/models/qwen{35b,122b}/`
- Setup docs: `documentation/qwen35-install.md` (legacy 32B/72B docs still on disk: `documentation/local-llm-services.md`, `documentation/qwen32b-base-to-instruct.md`)

**Thinking-mode mitigation (critical):** Both launchd plists pass `--chat-template-args '{"enable_thinking": false}'` to `mlx_lm.server`. Without this flag, Qwen 3.5 emits `<think>...</think>` tokens that break blueCode's strict JSON schema validation. If this flag is unavailable in a future mlx_lm version, Path B fallback is documented in `documentation/qwen35-install.md` §6. See also: commit `54e54a9` (AgentLoop User role fix for mid-conversation hints — required for T6 to pass on Qwen 3.5).

Unified memory is tighter than v1: 35B (~17GB) + 122B (~45GB) + OS / KV cache combined ~62 GB resident at steady state (MoE sparse routing keeps RSS well below disk size; bench-all confirmed flat at 62.4 GB). Periodic `launchctl kickstart` recommended for long sessions (KV cache accumulates). Old 32B/72B model files preserved at `~/llm-system/models/qwen{32b,72b}/` for rollback.

## Common Gotchas

### "Connection refused" or 180s timeout

Server might be crashed (check `~/llm-system/services/logs/{35b,122b}.err` for `[METAL] Insufficient Memory`), still loading weights after kickstart (RSS climbing toward 17GB / 45GB), or the HF fallback trap firing. Use `--trace` to see actual POST body + response, diff against `curl localhost:8000/v1/models` to confirm id matching. Full protocol in `documentation/howto/debug-local-llm-server-responses.md`.

### Spectre.Console markup parsing

Spinner labels like `"Thinking... [32B]"` make Spectre parse `[32B]` as a color tag. Use double-bracket escape: `"Thinking... [[32B]]"`. Bit me once (commit `438e4a3`, pre-flight of 05-04).

### Port 48 `Address already in use` after kickstart

If `launchctl kickstart -k` is sent rapidly or mid-load, the new process may fail to bind while the old one releases. Prefer `unload + load -w` for clean restarts. See `documentation/local-llm-services.md` §5.

### `async {}` in Core

CI fails on any `async {}` literal in `src/BlueCode.Core/`. All Core CE use `task {}`. If you find yourself wanting `async`, it's probably the wrong layer — move the work to Cli adapters.

## Don't Do

- Don't reintroduce absolute filesystem paths in Core (`grep -rn "llm-system" src/` must stay at 0)
- Don't swap `Router.modelToName` back in — that function is deleted and should stay deleted
- Don't add new NuGet packages without a corresponding decision in `.planning/PROJECT.md` Key Decisions
- Don't assume Windows paths; `tryParseModelId`'s `StartsWith("/")` heuristic is Unix-only (v1 Mac-only permits this)
- Don't commit `.claude/` or `localLLM/` — they're intentionally untracked

## Bench

`bench/run.sh` is the canonical regression harness — repo-tracked replacement for
v1.2's ephemeral `/tmp/bench-v1.2/run.sh`. Run `bench/run.sh --gate` (added in
Plan 10-02) to validate the current binary against `bench/baseline.json`.

- `--gate` — regression subset (~8 invocations, ~2 min); exits non-zero on regression
- `--canary` — quick smoke (4 invocations, ~1.5 min)
- `--regression` — full Part 1 reproducibility (14 invocations)
- `--all` — everything (~25 min)
- `--b2` — B2 divide-by-zero diagnose only

Fixtures live in `bench/fixtures/`. Logs land in `bench/runs/<timestamp>/`
(gitignored). See `documentation/bench.md` for full usage and fixture conventions.

## When Stuck

1. Check `.planning/STATE.md` for the most recent Accumulated Decisions — v1.0 and v1.1 sessions captured dozens of subtle gotchas there
2. Check `documentation/howto/` for reusable diagnostics (Base vs Instruct, server debug, Expecto Console)
3. Check recent git log — commit messages explain design choices
4. `.claude/` folder contains GSD framework and prior session memory; read-only reference

---

*Last updated: 2026-04-24 after v1.1 milestone complete*
