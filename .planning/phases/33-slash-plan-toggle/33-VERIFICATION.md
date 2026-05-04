---
phase: 33-slash-plan-toggle
verified: 2026-05-04T23:16:07Z
status: human_needed
score: 7/7 must-haves verified
gaps: []
human_verification:
  - test: "In a real terminal, type: /plan, then enter a short prompt (e.g. 'hello'), observe plan-gate table render, press 'a' to Accept, confirm execution runs and plan-mode is off afterward (/status shows no plan-mode line)"
    expected: "PlanGate table renders via AnsiConsole.Write, 'Accepted.' printed, runSingleTurn executes against live 122B LLM, /status post-Accept has no plan-mode line"
    why_human: "Requires live TTY with 122B service running; Console.ReadKey works differently in real terminal vs redirected stdin; AnsiConsole table rendering needs real terminal for visual confirmation"
  - test: "In a real terminal, type: /plan, then enter a prompt, observe plan-gate, press 'q' to Quit, confirm REPL returns to prompt (not exit)"
    expected: "'Quit.' printed, REPL accepts subsequent /exit cleanly; process exit code 0"
    why_human: "Requires live TTY — realKeyReader's primary path is Console.ReadKey which only works on a real terminal"
---

# Phase 33: slash-plan-toggle Verification Report

**Phase Goal:** `/plan` mid-REPL on/off — next turn uses plan-mode. `--plan` flag 와 동등한 path 를 REPL 안에서 toggle 가능하게.
**Verified:** 2026-05-04T23:16:07Z
**Status:** human_needed (all automated checks pass; 2 items need live TTY testing)
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|---------|
| 1 | `/plan` toggles `planModeActive` bool; prints `[plan mode on]`/`[plan mode off]` via printfn (NOT LLM) | VERIFIED | `Repl.fs:250-260` — `Slash Plan ->` arm flips `planModeActive <- not planModeActive`, prints via `printfn`; ReplTests test 1 asserts `[plan mode on]` with `stubLlm []` (zero LLM calls) |
| 2 | `/status` while `planModeActive=true` shows `plan-mode: on` line; absent when false | VERIFIED | `Rendering.fs:156-175` — `renderStatus` 4th param `planModeActive: bool`; appends `\nplan-mode: on (next prompt uses plan-gate)` only when true; `Repl.fs:200` passes `planModeActive` as 4th arg; ReplTests test 3 asserts `plan-mode: on` after `/plan` |
| 3 | `renderHelp` shows `/plan` with live description; only `/edit` retains `[coming in v2.5]` | VERIFIED | `Rendering.fs:138` — `/plan toggle plan-mode on/off; next prompt uses plan-gate when on`; line 139 — `/edit ... [coming in v2.5]`; RenderingTests asserts exactly 1 marker occurrence |
| 4 | `planModeActive=true` routes next prompt through `runPlanTurn` + `PlanGate` | VERIFIED | `Repl.fs:265` — `Some (Prompt prompt) when planModeActive ->` arm calls `BlueCode.Core.AgentLoop.runPlanTurn` (line 286) with `CompositionRoot.planSystemPromptSuffix` (line 292), then `BlueCode.Cli.PlanGate.render plan` (line 304) and `BlueCode.Cli.PlanGate.promptUser BlueCode.Cli.PlanGate.realKeyReader` (line 305); ReplTests test 4 asserts LLM called twice (plan + execute) and `Accepted.` printed |
| 5 | `/plan` again toggles off (symmetric); plan-mode prompt `/plan` disables after Accept/Quit/error | VERIFIED | `Repl.fs:256` — toggle is `not planModeActive`; `Repl.fs:301,310,337,343` — `planModeActive <- false` on Error/Accept/Quit/exhausted-rejects; ReplTests test 2 asserts both notifications in order; tests 4/5/6 assert no `plan-mode` in post-action `/status` |
| 6 | Mid-turn `/plan` is not possible (REPL ReadLine blocks) | VERIFIED (architectural) | Research confirmed ReadLine blocks the loop thread; no guard needed; `Repl.fs:183` — `Console.ReadLine()` call blocks synchronously |
| 7 | Bench gate 7/7 PASS preserved; `bench/baseline.json` byte-identical | VERIFIED | `git diff master -- bench/baseline.json` is empty; SUMMARY claims 7/7 PASS from Plan 33-02 Task 3 (live LLM bench not re-run during verification — see Human Verification note) |

**Score:** 7/7 truths verified (automated); 2 truths need live TTY confirmation for real-terminal PlanGate UX

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/BlueCode.Cli/Repl.fs` | `planModeActive` cell + `Slash Plan` arm + `when planModeActive` arm | VERIFIED | `let mutable planModeActive = false` at line 177; `Slash Plan ->` arm at line 250; `Some (Prompt prompt) when planModeActive ->` at line 265; `Slash Edit ->` stub at line 261; old `Slash (Plan \| Edit)` combined arm ABSENT |
| `src/BlueCode.Cli/Rendering.fs` | `renderStatus` 4-param signature; `renderHelp` live `/plan` line | VERIFIED | `renderStatus` signature at line 156 with `planModeActive: bool`; `planLine` logic at lines 170-172; `renderHelp` at lines 129-139 with live `/plan` description |
| `tests/BlueCode.Tests/RenderingTests.fs` | 4 renderStatus call sites with `false`; 1 new `true` testCase; marker test updated to 1 | VERIFIED | `grep -c "renderStatus session.*8192 false"` = 5 (4 pre-existing + 1 in new test that uses `false`); `grep -c "renderStatus session.*8192 true"` = 1; marker test at line 93 asserts `exactly 1` occurrence |
| `tests/BlueCode.Tests/ReplTests.fs` | 6 new plan-mode integration tests; future-stub test updated to 1 | VERIFIED | 6 testCases at lines 927/966/1009/1048/1128/1196; future-stub test at line 618 (`/edit only`) with `StringReader("/edit\n/exit\n")`; all 6 tests inside `testSequenced` block |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `Repl.fs` | `AgentLoop.runPlanTurn` | `BlueCode.Core.AgentLoop.runPlanTurn` call at line 286 | WIRED | Pattern found at `Repl.fs:286`; fully-qualified name as required by PLAN |
| `Repl.fs` | `CompositionRoot.planSystemPromptSuffix` | passed as `systemPromptSuffix` arg at line 292 | WIRED | `CompositionRoot.planSystemPromptSuffix` at `Repl.fs:292` |
| `Repl.fs` | `PlanGate.render` + `PlanGate.promptUser` | `BlueCode.Cli.PlanGate.render plan` at line 304; `BlueCode.Cli.PlanGate.promptUser BlueCode.Cli.PlanGate.realKeyReader` at line 305 | WIRED | Both fully-qualified as required |
| `Repl.fs` | `Rendering.renderStatus` | 4th arg `planModeActive` at `Repl.fs:200` | WIRED | `Rendering.renderStatus currentSession components.Config.ForcedModel components.MaxModelLen planModeActive` confirmed |
| `ReplTests.fs` | `Repl.runMultiTurnWithSession` | `Console.SetIn(StringReader(..."/plan..."))` drives `/plan` through REPL loop | WIRED | All 6 tests use `StringReader` with `/plan\n...` stdin scripts |
| `ReplTests.fs` | `PlanGate.realKeyReader` (stdin fallback) | `a\n`/`q\n` in scripted stdin; `realKeyReader` falls back to `Console.In.ReadLine` when `Console.ReadKey` throws `InvalidOperationException` | WIRED | Tests 4 and 5 use `a\n`/`q\n` in `StringReader`; the 352-test run passing confirms fallback works |

### Requirements Coverage

Phase 33 ROADMAP success criteria:

| Criterion | Status | Evidence |
|-----------|--------|---------|
| SC-1: REPL state `planModeActive: bool`; `/plan` toggles; `/status` displays | SATISFIED | `Repl.fs:177` cell; `Repl.fs:250-260` toggle arm; `Repl.fs:200` status call; ReplTests tests 1,2,3 |
| SC-2: `planModeActive=true` → next prompt uses `runPlanTurn` path (PlanGate display) | SATISFIED | `Repl.fs:265-343` `when planModeActive` arm; ReplTests test 4 verifies LLM called twice |
| SC-3: plan-mode 중 `/plan` 다시 입력 시 off | SATISFIED | `Repl.fs:256` `planModeActive <- not planModeActive`; ReplTests test 2 verifies both notifications in order |
| SC-4: mid-turn `/plan` invalid (architectural) | SATISFIED (N/A) | ReadLine blocks loop thread; no race possible; no test needed |
| SC-5: Bench gate 7/7 PASS | SATISFIED (SUMMARY claim) | `git diff master -- bench/baseline.json` empty; SUMMARY records 7/7 PASS; live re-run not performed during verification |
| SC-6: Role=System invariant; toggle notification user-facing console only | SATISFIED | All notifications use `printfn` (never `AnsiConsole.MarkupLine` or LLM message injection); ReplTests test 1 asserts zero LLM calls on toggle |

### Anti-Patterns Found

No blockers or warnings:

- `src/BlueCode.Cli/Repl.fs`: `AnsiConsole` referenced in comment on line 100 only (explaining why NOT to use it in test paths). No `AnsiConsole` calls in the new plan-mode notification code. All toggle notifications use `printfn`.
- `src/BlueCode.Cli/Repl.fs`: One legitimate stub remains: `/edit` at line 261-264 (`"not yet implemented — coming in a future v2.5 phase"`). This is expected; Phase 34 will implement it.
- No `TODO`/`FIXME`/`placeholder` in modified files.
- `running <- false` appears exactly twice: line 186 (null/Ctrl+D) and line 196 (`Slash Exit`). The plan-gate `Quit` arm uses only `planModeActive <- false` + `turnDone <- true` — correct (research Pitfall 4 respected).
- No `async {}` in Core: confirmed by `bash scripts/check-no-async.sh` → `OK`.

### Spectre.Console Singleton Reset Pattern

The plan-mode integration tests (Accept and Quit) call `PlanGate.render` which invokes `AnsiConsole.Write(table)`. Tests 4 and 5 both apply the reset pattern:

```fsharp
let originalSpectreConsole = Spectre.Console.AnsiConsole.Console
Spectre.Console.AnsiConsole.Console <- Spectre.Console.AnsiConsole.Create(AnsiConsoleSettings())
// ... in finally:
Spectre.Console.AnsiConsole.Console <- originalSpectreConsole
```

Tests 1, 2, 3 do not call `PlanGate.render` (toggle only / slash commands only) — no reset needed. Test 6 returns `Error` before reaching `PlanGate.render` — no reset needed. Pattern is consistently applied to exactly the tests that need it.

### Human Verification Required

Two items require a live terminal with the 122B service running. These cannot be verified programmatically because `PlanGate.realKeyReader`'s primary path uses `Console.ReadKey` which requires a real TTY.

#### 1. Plan-gate Accept in real terminal

**Test:** Build binary (`dotnet build -c Release src/BlueCode.Cli/BlueCode.Cli.fsproj`), run `blueCode`, type `/plan`, enter a short prompt (e.g. `list files in the current directory`), observe the PlanGate table render with rationale, press `a` + Enter.
**Expected:** PlanGate table renders visually; `Accepted.` prints; the agent executes the turn via 122B; after completion, type `/status` and confirm no `plan-mode:` line appears.
**Why human:** Requires live TTY + 122B service; `Console.ReadKey` is the primary path in `realKeyReader` and requires a real terminal handle.

#### 2. Plan-gate Quit in real terminal

**Test:** Same setup; type `/plan`, enter a prompt, observe plan-gate, press `q` + Enter.
**Expected:** `Quit.` prints; REPL returns to `blueCode>` prompt (NOT exit); subsequent `/exit` produces clean exit code 0.
**Why human:** Same as above — TTY required for `Console.ReadKey`.

### Core Purity Invariant

| Check | Result |
|-------|--------|
| `git diff master -- src/BlueCode.Core/` | Empty (no diff) |
| `git diff master -- bench/baseline.json` | Empty (byte-identical) |
| `git diff master -- src/BlueCode.Cli/Program.fs` | Empty (untouched) |
| `bash scripts/check-no-async.sh` | `OK: no async {} expressions in src/BlueCode.Core` |

### Test Count Delta

| Source | Before Phase 33 | After Phase 33 | Delta |
|--------|----------------|----------------|-------|
| RenderingTests | 17 | 18 | +1 (new planModeActive=true testCase) |
| ReplTests | ~18 (pre-33) | 24 | +6 (plan-mode integration tests) |
| **Total** | **345** | **352** | **+7** |

Confirmed by test runner output: `352 tests run ... 352 passed, 1 ignored, 0 failed, 0 errored. Success!`

### Roadmap Success Criteria Mapping

| Criterion | Evidence | Verdict |
|-----------|----------|---------|
| 1. `planModeActive: bool` REPL state + `/plan` toggle + `/status` display | `Repl.fs:177,250-260,200`; `Rendering.fs:156-175`; tests 1,2,3 passing | SATISFIED |
| 2. `planModeActive=true` → next prompt via `runPlanTurn` (PlanGate display) | `Repl.fs:265-343`; test 4 passing (LLM called twice) | SATISFIED |
| 3. plan-mode 중 `/plan` 재입력 시 off | `Repl.fs:256`; test 2 passing (both notifications in order) | SATISFIED |
| 4. mid-turn `/plan` = invalid (architecture; ReadLine blocks) | Architectural; `Repl.fs:183`; no guard needed | SATISFIED (N/A) |
| 5. Bench gate 7/7 PASS preserved | `bench/baseline.json` empty diff; SUMMARY records 7/7 | SATISFIED (SUMMARY) |
| 6. Role=System invariant; `[PLAN MODE]` notification user-facing console only | `printfn` only at `Repl.fs:258,260`; test 1 zero-LLM assertion | SATISFIED |

---

_Verified: 2026-05-04T23:16:07Z_
_Verifier: Claude (gsd-verifier)_
