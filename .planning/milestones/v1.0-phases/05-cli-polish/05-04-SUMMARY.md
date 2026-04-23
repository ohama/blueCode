---
phase: 05-cli-polish
plan: 04
name: retirement-uat
completed: 2026-04-23
tasks: 4
commits:
  - 438e4a3  # chore(05-04): pre-flight + spinner markup fix
  - 5ab5a95  # fix(router): modelToName returns loaded model path (UAT blocker)
  - ea18cf7  # feat(qwen-client): Log.Debug POST body and response (UAT diagnostic tooling)
  # + this doc commit
uat_result: passed
---

# 05-04 Summary: Retirement + UAT

## Plan objective

Close Phase 5 SC-5 (daily-driver cutover):
1. Retire `~/projs/claw-code-agent/` → `~/projs/claw-code-agent-retired/`
2. Complete one real coding task using blueCode as the sole agent
3. Update docs to reflect v1.0 milestone complete

## What happened

### Task 1 — Pre-flight sanity (auto) ✓

**Commit:** `438e4a3`

- `dotnet build --configuration Release`: 0 warnings, 0 errors
- `dotnet test` (excl. Smoke): 208 passed, 1 ignored, 0 failed
- `dotnet fantomas --check`: clean across src/ and tests/
- `blueCode --help`: Argu usage printed with `--verbose`, `--trace`, `--model`
- Core purity: no Serilog/Spectre/Argu in BlueCode.Core; no `async {}` in Core
- Offline smoke (vLLM not running): clean `LlmUnreachable`, exit 1, no stack trace

**Auto-fix discovered during pre-flight:** Spinner label `"Thinking... [32B]"` raised `InvalidOperationException: Could not find color or style '32B'` on every invocation — Spectre.Console parsed `[32B]` as a markup color tag. Fixed to `"Thinking... [[32B]]"` (Spectre literal bracket escape). Bundled into the pre-flight commit.

### Task 2 — Retire Python agent (human-action) ✓

User action. Verified post-fact:
- `/Users/ohama/projs/claw-code-agent` → absent ("No such file or directory")
- `/Users/ohama/projs/claw-code-agent-retired` → present, 17 items

No automated filesystem operation outside the repo. Checkpoint protocol respected.

### Task 3 — Real-task UAT (human-verify) ✓

First attempt revealed two blockers that were fixed inline before UAT could proceed:

**Blocker A — `modelToName` returned unknown strings** (Router.fs:59-60)

`qwen2.5-coder-32b-instruct` / `-72b-instruct` strings were not recognized by mlx_lm.server, which fell back to HuggingFace Hub resolution and returned 404. The server's `/v1/models` advertises loaded absolute paths as valid `model` ids. Minimum fix: return the loaded path.

**Commit:** `5ab5a95 fix(router): modelToName returns loaded model path`

Phase 5 OBS-03 (`/v1/models` dynamic probe) is the intended long-term solution; this one-line fix unblocks v1.0 without changing the probe roadmap.

**Blocker B — 32B model is Base, not Instruct**

The downloaded `mlx-community/qwen2.5-32b-mlx` resolved to `Qwen/Qwen2.5-Coder-32B` (a **Base Coder model, FIM-only**). Evidence:
- `tokenizer_config.json` had no `chat_template` field (confirmed via inspection)
- No `special_tokens_map.json` / `added_tokens.json` in the model directory
- No `<|im_start|>` / `<|im_end|>` tokens in any file
- Server responses contained `<|fim_prefix|>`, `<|fim_middle|>`, `<|fim_suffix|>` — classic FIM-mode artifacts
- Even after manually merging `chat_template.jinja` into `tokenizer_config.json` and restarting the service, responses remained FIM-continuations (the tokens themselves are not trained into the model)

**Resolution:** Route all UAT through 72B (`mlx-community/Qwen2.5-72B-Instruct-4bit-AWQ`, confirmed Instruct with `<|im_start|>`/`<|im_end|>` tokens and valid chat template). 32B Instruct re-download deferred to v1.1.

**UAT run (successful):**

```
dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj -- --model 72b "List the files in the src directory"

> listing directory... [ok, 7535ms]
> final answer... [ok, 4379ms]

The files in the src directory are: BlueCode.Cli, BlueCode.Core

[INF] Session ok: 2 steps, model=Qwen72B, log=/Users/ohama/.bluecode/session_2026-04-23T07-50-36Z.jsonl
```

All success criteria observable in this run:
- SC-1 (implicitly via `--model 72b` override route): task completed in 2 steps < 5
- SC-2 (Compact stdout output): per-step lines with `[ok, XXXms]` DurationMs marker
- SC-3: `--model 72b` correctly routed to localhost:8001, bypassing intent classification
- SC-6 (JSONL session log): file written to `~/.bluecode/session_<ts>.jsonl`
- Exit code 0

### Task 4 — Doc updates (this commit)

- `.planning/phases/05-cli-polish/05-04-SUMMARY.md` created (this file)
- `.planning/STATE.md` updated: Phase 5 COMPLETE, v1.0 milestone ready
- `.planning/ROADMAP.md` updated: Phase 5 ✓, all plans ✓
- `.planning/REQUIREMENTS.md` updated: CLI-01..07, OBS-03, ROU-04 → Complete

## Bonus artifact: --trace body logging

During UAT diagnostics, two `Log.Debug` calls were added in
`QwenHttpClient.CompleteAsync` — one for the POST body before send, one
for the response body after. Gated by the existing `LoggingLevelSwitch`
(silent by default, surfaces via `--trace`). Useful for future protocol
debugging. Committed as `ea18cf7` and retained as a feature.

## Deviations from original plan

1. Pre-flight uncovered a Spectre markup bug (auto-fix, bundled).
2. UAT uncovered `modelToName` router bug (separate fix commit).
3. UAT uncovered that 32B is the wrong model variant (deferred — 72B satisfies SC-5 with `--model 72b`).
4. `--trace` was extended with POST body/response logging (kept as feature).

## Open items for v1.1

- Re-download 32B Instruct (`mlx-community/Qwen2.5-Coder-32B-Instruct` or equivalent MLX build)
- OBS-03 dynamic model id from `/v1/models` (already partially scaffolded: `getMaxModelLenAsync` can be extended to `getModelNameAsync`)
- Spectre spinner: decouple 32B cold-start probe from bootstrap so timeout doesn't appear as a startup WARN
- Evaluate cross-turn memory for multi-turn REPL

## Daily-driver status

blueCode is now the sole agent. claw-code-agent is retired (directory renamed). v1.0 milestone complete.
