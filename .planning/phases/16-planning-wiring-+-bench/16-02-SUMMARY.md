---
phase: 16-planning-wiring-+-bench
plan: "02"
subsystem: planning
tags: [plan-gate, argu, dispatch, spectre, live-smoke]
one-liner: "PlanGate UI (IKeyReader abstraction + Spectre table) + --plan Argu flag + Program.fs plan-mode loop with a/r/e/q dispatch and [PLAN REJECTED] re-prompt"
requires: ["16-01"]
provides: [plan-mode-cli, plan-approval-gate]
affects: ["16-03"]
tech-stack:
  added: []
  patterns: [plan-mode-loop, ikey-reader-abstraction, stdin-redirect-fallback]
key-files:
  created:
    - src/BlueCode.Cli/PlanGate.fs
    - tests/BlueCode.Tests/PlanGateTests.fs
  modified:
    - src/BlueCode.Cli/BlueCode.Cli.fsproj
    - src/BlueCode.Cli/CliArgs.fs
    - src/BlueCode.Cli/CompositionRoot.fs
    - src/BlueCode.Cli/Program.fs
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs
decisions:
  - "planSystemPromptSuffix uses OVERRIDE directive to make 122B emit action=plan (base prompt's tool-call instructions were overriding the suffix)"
  - "realKeyReader fallback: Console.ReadKey throws on redirected stdin; catch InvalidOperationException and read from Console.In instead (enables pipe-mode smoke)"
  - "maxUserRejects=3 defensive cap on user-reject loop, independent of runPlanTurn's internal 2-attempt retry"
  - "Reject/edit inject as prompt prefix in next runPlanTurn userInput (not buildMessages-level injection) — keeps execution single-turn-driven by user's original prompt"
  - "Accept dispatches to Repl.runSingleTurn with ORIGINAL prompt (not edited/rejected variants)"
metrics:
  duration: "9m 5s"
  completed: "2026-04-27"
---

# Phase 16 Plan 02: PlanGate UI + --plan Flag + Program Dispatch Summary

**One-liner:** PlanGate UI (IKeyReader abstraction + Spectre table) + --plan Argu flag + Program.fs plan-mode loop with a/r/e/q dispatch and [PLAN REJECTED] re-prompt

## What Was Built

### Task 1: PlanGate.fs (new)

`src/BlueCode.Cli/PlanGate.fs` — new Cli-only module (no Core dependencies beyond Domain.Plan):

**IKeyReader port:**
```fsharp
type IKeyReader =
    abstract member ReadKey : unit -> char
    abstract member ReadLine : unit -> string
```
- `realKeyReader`: production reader using `Console.ReadKey(intercept=true)` with fallback to `Console.In.ReadLine()` when stdin is redirected (pipe-mode smoke). Fallback added as a Rule 3 deviation (auto-fix to unblock smoke tests).
- Test injection: `scriptedReader (keys: char list) (lines: string list)` in PlanGateTests — no `Console.ReadKey` side effects.

**PlanGateDecision DU:**
```fsharp
type PlanGateDecision = Accept | Reject | Edit of comment: string | Quit
```

**render**: Spectre.Console.Table (4 columns: #/tool/input/rationale) + `printfn "Proposed plan: ..."` top-line + `printfn "[a]ccept / ..."` prompt. Top-line and prompt use `printfn` (not AnsiConsole) so Console.SetOut-redirecting tests can assert them.

**promptUser**: recursive loop returning PlanGateDecision on a/r/e/q; unknown keys re-prompt.

Registered in `BlueCode.Cli.fsproj` after `CompositionRoot.fs` and before `Repl.fs`.

### Task 2: --plan flag + Program.fs dispatch

**CliArgs.Plan** — new boolean Argu flag, auto-derives `--plan`. Usage describes single-turn-only constraint (REPL plan-mode v2.1+).

**CliOptions.PlanMode: bool** added to CompositionRoot; `defaultCliOptions.PlanMode = false`.

**planSystemPromptSuffix** — public constant in CompositionRoot:
```
OVERRIDE — PLAN MODE ACTIVE. Do NOT use read_file/...
Your ONLY valid response is action="plan". Respond with EXACTLY this JSON shape:
{"thought": "...", "action": "plan", "input": {"steps": [...], "rationale": "..."}}
```
The OVERRIDE prefix was required (see Deviations). Final form passes through `runPlanTurn`'s `systemPromptSuffix` parameter to `config.SystemPrompt + "\n\n" + suffix`.

**Program.fs dispatch** — guards (after resume/new-session conflict check):
- `--plan` without prompt → exit 2 with "ERROR: --plan requires a prompt; REPL plan-mode is v2.1+ scope."
- `--plan --with-35b` → exit 2 with "ERROR: --plan with --with-35b is not supported in v2.0; 35B service is rollback-only."

Plan-mode loop:
```
maxUserRejects = 3
while decision = None && rejectCount < maxUserRejects:
  runPlanTurn (Config, LlmClient, model, session.Steps, currentPrompt, planSystemPromptSuffix, ct)
  | Error e -> eprintfn renderError; quit (exit 1)
  | Ok plan ->
    PlanGate.render plan
    PlanGate.promptUser realKeyReader
    | Accept -> finalDecision = Accept
    | Quit -> finalDecision = Quit (exit 0)
    | Reject -> rejectCount++; currentPrompt = "[PLAN REJECTED] ... \n\n<original>"
    | Edit c -> rejectCount++; currentPrompt = "[PLAN EDIT NOTE: c] ... \n\n<original>"
```

On Accept: `Repl.runSingleTurn prompt session.Steps components renderMode` with original prompt; save session.

### Task 3: PlanGateTests.fs + live smoke

**PlanGateTests.fs** — 6 testCases in `testSequenced` (CLAUDE.md Console.SetOut requirement):
1. `'a' -> Accept` — decision + "Accepted." in stdout
2. `'r' -> Reject` — decision + "Rejected" in stdout
3. `'q' -> Quit` — decision correct
4. `'e' + ReadLine "use grep_search..."` → `Edit "use grep_search..."` — comment captured verbatim
5. `'x'; 'a'` — "Unrecognized" warning then "Accepted" (re-prompt loop)
6. `render samplePlan` — "Proposed plan: ..." + "[a]ccept" + "[q]uit" in captured stdout

Registered in fsproj (position 18, after PlanParseTests) and RouterTests.fs rootTests (line 104, after PlanParseTests.tests).

## a/r/e/q Dispatch Table

| Key | PlanGateDecision | Program.fs handling |
|-----|-----------------|---------------------|
| `a` | Accept | Run `Repl.runSingleTurn` with ORIGINAL prompt; save session; exit with agent exit code |
| `r` | Reject | Prefix `[PLAN REJECTED] The previous plan was rejected...` to currentPrompt; loop (max 3) |
| `e` | Edit of comment | Prefix `[PLAN EDIT NOTE: <comment>] Revise...` to currentPrompt; loop (max 3) |
| `q` | Quit | Exit 0 (no execution, no session save) |
| other | (re-prompt) | Print "Unrecognized keystroke. Press a/r/e/q."; loop |

## [PLAN REJECTED] Re-Prompt Format

```fsharp
currentPrompt <- sprintf "[PLAN REJECTED] The previous plan was rejected by the user. Propose a different plan.\n\n%s" prompt
```

- Injected as `userInput` parameter to `runPlanTurn` (never as a Role=System message)
- User-prompt position in `buildMessages` is always `Role = User` by construction
- Phase 20-03 invariant satisfied: `[PLAN REJECTED]` text marker carries authority, not the role
- Code comment at Program.fs:209-211 cites: "Role = User per Phase 20-03 probe (2026-04-27) — 122B HTTP 404 on mid-conversation Role=System"

## Live Smoke Results

### Pre-condition
```
curl -fsS http://127.0.0.1:8001/v1/models > /dev/null && echo "122B OK"
# 122B OK
```

### SC1 — Plan table renders

```
printf 'q\n' | dotnet run --project src/BlueCode.Cli -- --plan "list 3 files in src/BlueCode.Core"
```
Output (abbreviated):
```
Session: 70e6e469...
Thinking... [122B]
Proposed plan: Use list_dir to retrieve the file listing from the specified directory.
┌───┬──────────┬────────────────────────────────┬──────────────────────────────┐
│ # │ tool     │ input                          │ rationale                    │
├───┼──────────┼────────────────────────────────┼──────────────────────────────┤
│ 1 │ list_dir │ {"path": "src/BlueCode.Core",  │ List the contents of the     │
│   │          │ "depth": 1}                    │ src/BlueCode.Core directory  │
│   │          │                                │ to identify available files. │
└───┴──────────┴────────────────────────────────┴──────────────────────────────┘

[a]ccept / [r]eject / [e]dit / [q]uit

Quit.
exit=0
```

### SC2 — Keystroke dispatch

**Accept arm** (`printf 'a\n' | ...`):
```
Accepted.
Thinking... [122B]
> listing directory... [ok, 1613ms]
> final answer... [ok, 4064ms]

The files in src/BlueCode.Core are:
- AgentLoop.fs, BlueCode.Core.fsproj, ContextBuffer.fs, Domain.fs, ...
exit=0
```

**Reject arm** (`printf 'r\nq\n' | ...`):
```
Rejected — re-prompting LLM.
Thinking... [122B]
Proposed plan: This plan directly addresses...
[table shown]
[a]ccept / [r]eject / [e]dit / [q]uit
Quit.
exit=0
```

**Quit arm** (`printf 'q\n' | ...`):
```
Quit.
exit=0
```

### SC4 — --plan --resume (prior context)

```bash
ID=$(dotnet run -- "What is 2+2?" 2>&1 | grep -oE 'Session: [a-zA-Z0-9]+' | head -1 | cut -d' ' -f2)
printf 'q\n' | dotnet run -- --plan --resume "$ID" "Now list files in src"
```
Output:
```
Session: a5e704196a8c4416bce7766ad3c2e7cf
Thinking... [122B]
Proposed plan: The user explicitly asked to list files in 'src', so a single list_dir command is sufficient...
┌───┬──────────┬────────────────┬──────────────────────────────────────────────┐
│ # │ tool     │ input          │ rationale                                    │
├───┼──────────┼────────────────┼──────────────────────────────────────────────┤
│ 1 │ list_dir │ {"path":"src"} │ To list all files and directories...         │
└───┴──────────┴────────────────┴──────────────────────────────────────────────┘
Quit.
exit=0
```
Prior session context loaded (session id preserved, priorSteps threaded into runPlanTurn).

### Guardrail checks
```
dotnet run -- --plan 2>&1; echo "exit=$?"
# ERROR: --plan requires a prompt; REPL plan-mode is v2.1+ scope.
# exit=2

dotnet run -- --plan --with-35b "test" 2>&1; echo "exit=$?"
# ERROR: --plan with --with-35b is not supported in v2.0; 35B service is rollback-only.
# exit=2
```

## Test Count

274/1/0 (post-16-01) → **280/1/0** (+6 PlanGateTests)

## Bench Gate

```
bash bench/run.sh --gate
```
```
PASS T6_122b    steps=5/5 exit=0
PASS W1_122b    steps=3/3 exit=0
PASS W2_122b    steps=3/3 exit=0
PASS T1_122b    steps=1/3 exit=0
PASS T5_122b    steps=3/4 exit=0
PASS B2_122b    steps=2/3 exit=0
===== GATE PASS (6/6) =====
```

## Commits

| Hash | Type | Description |
|------|------|-------------|
| 4269159 | feat(16-02) | add PlanGate render + keystroke dispatch |
| 2e94d2d | feat(16-02) | wire --plan flag + plan-mode dispatch in Program.fs |
| 16e059e | test(16-02) | PlanGateTests for keystroke dispatch + render |

## Deviations from Plan

### Auto-fixed: planSystemPromptSuffix OVERRIDE directive (Rule 1 — Bug)

**Found during:** Task 3 live smoke (SC1 first attempt)

**Issue:** Initial `planSystemPromptSuffix` used a gentle `[PLAN MODE]` prefix. The 122B model followed the base system prompt's tool-call instructions instead, emitting `action="list_dir"` despite the suffix saying "Emit action=plan".

**Trace evidence:** First attempt returned `"action": "list_dir"` → `PlanInvalid "expected plan output, got tool/final action"`. After 2 internal retries (runPlanTurn), same failure. LLM was ignoring the plan-mode instruction.

**Fix:** Rewrote the suffix with an `OVERRIDE — PLAN MODE ACTIVE. Do NOT use read_file/... actions. Your ONLY valid response is action="plan"` preamble. Second smoke attempt succeeded immediately.

**Files modified:** `src/BlueCode.Cli/CompositionRoot.fs` (planSystemPromptSuffix body only)

### Auto-fixed: realKeyReader stdin-redirect fallback (Rule 3 — Blocking Issue)

**Found during:** Task 3 live smoke (first pipe-mode test: `printf 'q\n' | dotnet run -- --plan ...`)

**Issue:** `Console.ReadKey(intercept=true)` throws `System.InvalidOperationException: Cannot read keys when either application does not have a console or when console input has been redirected.` Pipe-mode smoke (`printf 'q\n' |`) and test automation require stdin redirect.

**Fix:** Wrapped `Console.ReadKey` in `try/with :? System.InvalidOperationException`. Fallback reads from `Console.In.ReadLine()` and takes the first character (or 'q' for empty line). This is transparent at runtime (TTY uses ReadKey; pipe-mode uses ReadLine fallback).

**Files modified:** `src/BlueCode.Cli/PlanGate.fs` (realKeyReader only)

## Verification

1. `dotnet build BlueCode.slnx` — Build succeeded, 0 warnings, 0 errors
2. `bash scripts/check-no-async.sh` — OK: no async {} expressions in src/BlueCode.Core
3. Tests — 280/1/0
4. Module exports verified: `PlanGateDecision`, `IKeyReader`, `realKeyReader`, `render`, `promptUser` in PlanGate.fs
5. `CliArgs.Plan` — Argu derives `--plan`; usage: "Plan-then-execute mode... Single-turn only (REPL plan-mode is v2.1+)."
6. `PlanGate.fs` in Cli.fsproj (position 19, after CompositionRoot.fs, before Repl.fs)
7. `PlanGateTests` in both BlueCode.Tests.fsproj (position 18) and RouterTests.fs:rootTests (line 104)
8. All prior entries preserved: ToolExpansionTests, SessionStoreTests, PlanValidatorTests, PlanParseTests
9. `grep "PLAN REJECTED\|Role = User"` in Program.fs — reject re-prompt with Phase 20-03 invariant comment
10. Live smoke SC1/SC2/SC4 — all pass (see above)
11. Bench gate — 6/6 PASS

## Next

16-03: bench fixture for plan-mode (MT_122b multi-turn fixture + plan-mode fixture in baseline.json).
