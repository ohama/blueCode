# Phase 33: SLASH plan toggle - Research

**Researched:** 2026-05-05
**Domain:** F# Cli layer — REPL mutable state extension, PlanGate in-REPL wiring, planSystemPromptSuffix gating
**Confidence:** HIGH (all findings from direct code inspection of shipped Phase 31/32 files)

## Summary

Phase 33 adds `/plan` toggle to the REPL. The SlashCommand parser already produces `Slash Plan`; the Repl dispatcher stubs it with `"(not yet implemented...)"`. Phase 33 replaces that stub with a real toggle and wires the next turn through `runPlanTurn` + `PlanGate` when plan-mode is active.

The work is entirely in the Cli layer — `Repl.fs`, `Rendering.fs`, `PlanGate.fs` (minor), and `CompositionRoot.fs` (no change; `planSystemPromptSuffix` is already public). Core (`AgentLoop.runPlanTurn`) is already shipped and takes `planSystemPromptSuffix` as a parameter. No changes to `SlashCommand.fs`, `CliArgs.fs`, `Program.fs` (single-turn plan path is separate and untouched), or any Core file.

The key architecture decision: `runMultiTurnWithSession` adds a `let mutable planModeActive = false` cell alongside the existing `mutable currentSession`. When `/plan` is typed, the flag toggles; when the next `Prompt` arrives and `planModeActive = true`, the dispatcher enters the plan-gate flow (`runPlanTurn` → `PlanGate.render` → `PlanGate.promptUser`) instead of `runSingleTurn`. After the plan gate resolves (Accept → execute, Reject → re-prompt, Quit → abandon), plan-mode turns off (`planModeActive <- false`) so the REPL returns to normal.

**Primary recommendation:** Two-plan split — Plan 33-01: Repl.fs toggle + planModeActive logic + Rendering updates; Plan 33-02: integration tests + renderHelp update.

---

## Q1: Current Plan-Mode Trigger Path (--plan flag)

**File:** `src/BlueCode.Cli/Program.fs` lines 172-255

The entire plan-mode loop lives in `Program.fs` (NOT in `Repl.fs`). It is entirely separate from `runMultiTurnWithSession`. The flow is:

```
isPlanMode && !List.isEmpty promptWords
  → while rejectCount < maxUserRejects:
      runPlanTurn config llmClient model session.Steps currentPrompt planSystemPromptSuffix ct
      → PlanGate.render plan + PlanGate.promptUser realKeyReader
         Accept → runSingleTurn prompt session.Steps components renderMode
         Reject → rejectCount++, rebuild currentPrompt with [PLAN REJECTED]
         Edit   → rejectCount++, rebuild currentPrompt with [PLAN EDIT NOTE: ...]
         Quit   → break
```

`planSystemPromptSuffix` is passed as a parameter to `runPlanTurn` from `CompositionRoot.planSystemPromptSuffix`. The suffix is public:
```fsharp
let planSystemPromptSuffix: string = """OVERRIDE — PLAN MODE ACTIVE..."""
```

**What `runMultiTurnWithSession` currently does NOT do:**
- Does NOT have a `planModeActive` bool
- Does NOT call `runPlanTurn`
- Does NOT pass `planSystemPromptSuffix` to any LLM call
- Does NOT use `PlanGate`

**Conclusion (HIGH):** Phase 33 must add all of these capabilities to `runMultiTurnWithSession`, following the same pattern as `Program.fs`'s plan-mode block.

---

## Q2: PlanGate in-REPL Compatibility

**File:** `src/BlueCode.Cli/PlanGate.fs`

`PlanGate.promptUser` calls `reader.ReadKey()` (blocking) then optionally `reader.ReadLine()` (for Edit). The `realKeyReader` falls back to `Console.In.ReadLine()` when stdin is redirected.

**Key finding:** The REPL's `Console.ReadLine()` call (for the main prompt loop) and PlanGate's `ReadKey()` call are NEVER concurrent — the REPL blocks on `Console.ReadLine()`, receives a non-slash user prompt, then enters the plan-mode branch. PlanGate then calls `ReadKey()` while the REPL loop body is still executing (before returning to the outer `while running do` iteration). There is NO stdin contention.

**PlanGate.render** uses `AnsiConsole.Write(table)` for the table (not testable via Console.SetOut) and `printfn` for the rationale line + approval prompt. This split is intentional and already documented in PlanGate.fs line 51.

**PlanGate `Quit` semantics in REPL context:** In `Program.fs`, `Quit` exits the plan loop and the process eventually exits 0. In the REPL context, `Quit` must abandon the plan for the current turn and return to the REPL prompt — NOT exit the REPL. The plan-mode turn dispatch block must handle `Quit` as "go back to prompt, do not set lastCode" (same as a cancelled turn). Plan-mode should also be turned off (`planModeActive <- false`) after `Quit` so the user isn't stuck in a mode they can't exit.

**Conclusion (HIGH):** PlanGate works in-REPL with no changes. The only semantic difference is `Quit` behavior: in REPL it means "abandon this plan turn, return to prompt" not "exit process".

---

## Q3: planSystemPromptSuffix Gating

**File:** `src/BlueCode.Cli/AgentLoop.fs` lines 477-478

`runPlanTurn` combines system prompts internally:
```fsharp
let combinedSystemPrompt = config.SystemPrompt + "\n\n" + systemPromptSuffix
```

`config.SystemPrompt` is `defaultSystemPrompt` (967 chars). `planSystemPromptSuffix` (1577 chars) is passed as the `systemPromptSuffix` parameter. This happens inside `runPlanTurn` only.

`runSession` (used by `runSingleTurn`) uses only `config.SystemPrompt` — it has no suffix parameter. So the suffix is naturally scoped to plan-mode calls only.

**In REPL plan-mode:** When `planModeActive = true` and a Prompt arrives:
1. Call `runPlanTurn components.Config components.LlmClient model currentSession.Steps prompt CompositionRoot.planSystemPromptSuffix ct`
2. If the plan is accepted, call `runSingleTurn prompt currentSession.Steps components renderMode` (same as non-plan turns)
3. The execute step uses the regular system prompt — plan-mode suffix does NOT contaminate execution

**Conclusion (HIGH):** No changes to `AgentLoop.fs`, `CompositionRoot.fs`, or `buildMessages`. The suffix gating is handled by which entry point is called.

---

## Q4: Mid-Turn /plan Input — REPL ReadLine Blocks Until Turn Ends

**File:** `src/BlueCode.Cli/Repl.fs` lines 180-271

The REPL loop structure is:
```
while running do
    printf "\nblueCode> "
    let line = Console.ReadLine()   // BLOCKS here until Enter pressed
    match SlashCommand.parse line with
    | Some (Prompt prompt) ->
        // runSingleTurn is awaited synchronously via task {} CE (not in a thread pool)
        // Console.ReadLine() only resumes AFTER runSingleTurn completes
```

`Console.ReadLine()` blocks the current thread. The `task {}` CE awaiting `runSingleTurn` is asynchronous but the F# `task {}` computation eventually `.GetAwaiter().GetResult()` blocks the loop thread. In practice, the REPL is effectively single-threaded from the user's perspective: no input can be received while a turn is running.

**Conclusion (HIGH):** "Mid-turn /plan input" is not possible at the ReadLine layer. A turn must complete before the next `printf "\nblueCode> "` appears. Success criterion 4 ("현재 turn 진행 중 /plan 입력 = invalid") is satisfied by architecture — the scenario cannot arise. No special guard needed.

---

## Q5: `runMultiTurnWithSession` vs New Variant

**Decision: Extend the existing function, not create a new variant.**

The existing `mutable currentSession` pattern (Phase 32 pattern for `/clear`) is the right model:
```fsharp
let mutable planModeActive = false  // ADD THIS
let mutable currentSession : Session = initialSession
```

The `Prompt` dispatch arm branches on `planModeActive`:
```fsharp
| Some (Prompt prompt) when planModeActive ->
    // plan-mode turn
| Some (Prompt prompt) ->
    // regular turn (existing code)
```

F# `when` guards on match arms are clean and explicit.

**Why no new function:** `runMultiTurnWithSession` already has all context (`components`, `renderMode`, `currentSession`, `sessionStore`, `running`, `lastCode`). Duplicating it would create two diverging code paths. The Phase 32-02 decision was also to extend in-place.

**Conclusion (HIGH):** Add `let mutable planModeActive = false` to `runMultiTurnWithSession`, branch on it in the `Prompt` arm. No new REPL entry point.

---

## Q6: Plan-Gate Reject/Edit/Quit in REPL

When plan-mode fires inside the REPL's Prompt arm, the reject/edit loop from `Program.fs` can be replicated inline. The design:

```fsharp
| Some (Prompt prompt) when planModeActive ->
    let model = components.Config.ForcedModel |> Option.defaultValue Qwen122B
    let mutable rejectCount = 0
    let mutable currentPrompt = prompt
    let mutable turnDone = false

    while not turnDone && rejectCount < maxUserRejects do
        let! planResult =
            runPlanTurn components.Config components.LlmClient model
                        currentSession.Steps currentPrompt
                        CompositionRoot.planSystemPromptSuffix
                        CancellationToken.None
        match planResult with
        | Error e ->
            printfn "%s" (renderError e)
            turnDone <- true     // abandon, planModeActive stays true (user can retry)
        | Ok plan ->
            PlanGate.render plan
            match PlanGate.promptUser PlanGate.realKeyReader with
            | PlanGate.Accept ->
                let! (code, newSteps) = runSingleTurn prompt currentSession.Steps components renderMode
                // update currentSession, save, update lastCode
                planModeActive <- false   // SC-3: plan-mode turns off after execution
                turnDone <- true
            | PlanGate.Reject ->
                rejectCount <- rejectCount + 1
                currentPrompt <- sprintf "[PLAN REJECTED] The previous plan was rejected. Propose a different plan.\n\n%s" prompt
            | PlanGate.Edit comment ->
                rejectCount <- rejectCount + 1
                currentPrompt <- sprintf "[PLAN EDIT NOTE: %s] Revise the previous plan accordingly.\n\n%s" comment prompt
            | PlanGate.Quit ->
                planModeActive <- false   // abandon plan-mode for this turn
                turnDone <- true
    // if rejectCount >= maxUserRejects and turnDone = false:
    printfn "Plan-mode: %d rejections without acceptance — abandoning." rejectCount
    planModeActive <- false
```

**Key difference from Program.fs:** After Quit, REPL returns to prompt (not process exit). After Accept+Execute, `planModeActive <- false` (plan-mode is per-turn in REPL; SC-3 says toggle off after use or keep on? See Open Questions below).

**Success criterion 3 clarification:** "plan-mode 중 /plan 다시 입력 시 off" — this means typing `/plan` while `planModeActive = true` should toggle it off. This is handled by the `/plan` toggle arm (independent of the Prompt arm). The Prompt arm with plan-mode active runs plan-gate, then turns plan-mode off automatically after Accept (or Quit).

**Conclusion (MEDIUM):** The design above matches the requirements but the "when does planModeActive go back to false after Accept" is a design choice (see Open Questions).

---

## Q7: `/status` Display of planModeActive

**File:** `src/BlueCode.Cli/Rendering.fs` lines 156-172

`renderStatus` currently produces 5 lines. The signature takes `(session: Session) (forcedModel: Model option) (maxModelLen: int)`. The `planModeActive` bool must be added as a parameter:

```fsharp
let renderStatus (session: Session) (forcedModel: Model option) (maxModelLen: int) (planModeActive: bool) : string =
```

Add a line to the output:
```
plan-mode: on
```
or only show when active (cleaner):
```fsharp
if planModeActive then sprintf "plan-mode: on (next turn uses plan-gate)\n" else ""
```

**Call site in Repl.fs:** Currently `Rendering.renderStatus currentSession components.Config.ForcedModel components.MaxModelLen`. Must add `planModeActive` argument.

**Existing test dependency:** `ReplTests.fs` line 520-525 tests `/status` output including exact fields. The test must be updated to pass `planModeActive` (or the parameter must have a default). Adding a parameter is a breaking change to the test — the test must be updated to thread `false` (off by default).

**Conclusion (HIGH):** Add `planModeActive: bool` as 4th parameter to `renderStatus`. Update Repl.fs call site and all tests that call `renderStatus`.

---

## Q8: renderHelp Update

**File:** `src/BlueCode.Cli/Rendering.fs` lines 129-139

```fsharp
  /plan              toggle plan-mode for next turn [coming in v2.5]
```

Update to:
```fsharp
  /plan              toggle plan-mode on/off; next prompt uses plan-gate when on
```

**Existing test dependency:** `ReplTests.fs` line 483 asserts `"[coming in v2.5]"` appears in help output. This test must be updated after the help text is changed. The test also asserts `/plan` still appears in help — that stays.

**Conclusion (HIGH):** Update help line; update the asserting test (from `[coming in v2.5]` assertion to the new live description).

---

## Q9: Plan-Mode Console Output — Role=System Invariant (SC-6)

**From CLAUDE.md + AgentLoop.fs:**
- Mid-conversation Role=System messages are HTTP 404 on Qwen 3.5 122B
- All mid-conversation injections MUST be Role=User
- `buildMessages` in AgentLoop.fs enforces this: the system message is always first, mid-loop injections use `{ Role = User; Content = ... }`

**For plan-mode toggle notification:** SC-6 says "[PLAN MODE] toggle 알림은 다음 turn 시작 시 user-facing console only (LLM 으로 보내지 않음)". This means:
- Print `"[PLAN MODE on]"` or `"[PLAN MODE off]"` via `printfn` when user toggles
- This is purely a console message — it does NOT go into the LLM message list
- No changes to `buildMessages` or message construction

**Conclusion (HIGH):** Toggle notification is `printfn "[plan mode on]"` / `printfn "[plan mode off]"`. Never injected into LLM conversation.

---

## Q10: Test Strategy

**Pattern to follow:** `ReplTests.fs` `testSequenced` block with `Console.SetIn` + `Console.SetOut` redirect.

For plan-mode REPL tests, the challenge is that `PlanGate.promptUser` calls `Console.ReadKey()` (or falls back to `Console.In.ReadLine()` when stdin is redirected). The fallback means tests can script `a\n` for Accept, `r\n` for Reject, etc. via `Console.SetIn`.

**Test for toggle on/off:**
```fsharp
use stdinReader = new StringReader("/plan\n/plan\n/exit\n")
// Expect: "[plan mode on]" then "[plan mode off]" in stdout
```

**Test for plan-mode turn (Accept path):**
```fsharp
// stdin: "/plan\n<prompt>\na\n/exit\n"
// LLM stub: scripted to return Plan response then FinalAnswer
// Expect: plan rationale in stdout, "Accepted." in stdout, turn output
```

**Test for plan-mode /status showing "plan-mode: on":**
```fsharp
// stdin: "/plan\n/status\n/exit\n"
// Expect: status output contains "plan-mode: on"
```

**Test for Quit in plan-gate stays in REPL:**
```fsharp
// stdin: "/plan\n<prompt>\nq\n/exit\n"
// LLM stub: scripted Plan response
// Expect: "Quit." in stdout, then REPL continues (/exit exits cleanly)
```

**Key constraint:** All new tests go inside the existing `testSequenced <| testList "Repl"` block in `ReplTests.fs`. No new test file needed (same module as /sessions and /resume tests added in Phase 32).

**Existing "future-stub" test (ReplTests.fs line 617):**
```fsharp
// After Phase 33, /plan is real — stub count drops from 2 to 1 (only /edit remains)
// Test must be updated: /plan\n/edit\n/exit\n → expect 1 "not yet implemented" line (just /edit)
```

**Conclusion (HIGH):** No new test file; all tests in existing `testSequenced` block in `ReplTests.fs`.

---

## Q11: Plan-Mode State Persistence

**Is `planModeActive` per-session or per-REPL-instance?**

`planModeActive` is a `mutable` local in `runMultiTurnWithSession`. It is NOT part of the `Session` domain type (Core purity — `Session` is in `Core/Domain.fs` and must not gain UI-layer concerns). It is NOT persisted to the JSONL session file.

If the user `/exit`s while plan-mode is on, plan-mode is lost. When the session is `/resume`d, plan-mode is off. This is correct and expected behavior for a transient REPL-state toggle.

**Conclusion (HIGH):** `planModeActive` is a pure REPL-instance bool, not persisted, not in Session type.

---

## Q12: Bench Gate Non-Regression

**`bench/run.sh` confirmed:** All 7 invocations use:
```bash
dotnet run --project src/BlueCode.Cli -- --verbose --model "$model" "$prompt"
```
Single-turn mode (prompt as CLI arg). The `--plan` flag is never passed. The `Program.fs` dispatch at line 257: `if isPlanMode then ... else ...` routes bench runs through the `else` branch (`Repl.runSingleTurn`). `runMultiTurnWithSession` is never entered.

**No Phase 33 changes affect the bench path:**
- `runSingleTurn` is unchanged
- `renderStep` / `renderResult` / `renderError` are unchanged
- `defaultSystemPrompt` and `planSystemPromptSuffix` char counts are unchanged
- `CompositionRoot.bootstrap` is unchanged

**Conclusion (HIGH):** Zero bench regression risk. The 7/7 PASS baseline is preserved by isolation of the multi-turn REPL path from single-turn bench invocations.

---

## Architecture Patterns

### Recommended Changes Per File

**`src/BlueCode.Cli/Repl.fs`** (Plan 33-01 — the core change):
- In `runMultiTurnWithSession`, add `let mutable planModeActive = false` alongside `let mutable currentSession`
- In the match arms, ABOVE the `| Some (Prompt prompt) ->` arm:
  - Replace `| Some (Slash (Plan | Edit)) ->` stub with two separate arms:
    ```fsharp
    | Some (Slash Plan) ->
        planModeActive <- not planModeActive
        if planModeActive then
            printfn "[plan mode on] — next prompt will enter plan-gate before execution"
        else
            printfn "[plan mode off] — returning to direct agent-loop"
    | Some (Slash Edit) ->
        printfn "(not yet implemented — coming in a future v2.5 phase)"
    ```
  - Add new arm BEFORE the standard `Prompt` arm:
    ```fsharp
    | Some (Prompt prompt) when planModeActive ->
        // plan-gate turn — see Q6 for full skeleton
    ```
- Pass `planModeActive` to `Rendering.renderStatus` call

**`src/BlueCode.Cli/Rendering.fs`** (Plan 33-01):
- Add `planModeActive: bool` as 4th parameter to `renderStatus`
- Add plan-mode line to `renderStatus` output (only when `planModeActive = true`, to keep status quiet by default)
- Update `renderHelp` string: `/plan` line — remove "[coming in v2.5]", add live description

**`tests/BlueCode.Tests/ReplTests.fs`** (Plan 33-01 + 33-02):
- Update all existing `renderStatus` call sites in tests to pass `false` (4th arg)
- Update the "future-stub" test: reduce expected "not yet implemented" count from 2 to 1 (only `/edit` remains); change stdin from `/plan\n/edit\n` to `/edit\n`
- Update the `/help` test: remove assertion on `"[coming in v2.5]"` for `/plan` line (or update to new description)
- Add new testCases inside existing `testSequenced` block for:
  - `/plan` toggle on → "[plan mode on]" in stdout
  - `/plan` toggle off → "[plan mode off]" after second toggle
  - `/status` with plan-mode on → shows "plan-mode: on"
  - plan-mode turn Accept → plan rationale + "Accepted." + turn result
  - plan-mode turn Quit → "Quit." + REPL continues (exit 0)
  - `/plan` when already on toggles off (SC-3 wording: "/plan 다시 입력 시 off")

**No changes to:** `SlashCommand.fs`, `CompositionRoot.fs`, `PlanGate.fs`, `AgentLoop.fs`, `CliArgs.fs`, `Program.fs`, `BlueCode.Cli.fsproj` (no new files), `RouterTests.fs` rootTests (no new test module).

### fsproj and rootTests: No Changes Needed

All new test code goes inside the existing `ReplTests.fs` `testSequenced` block. `ReplTests.tests` is already registered in `rootTests` (RouterTests.fs line 109). `BlueCode.Tests.fsproj` already includes `ReplTests.fs`. No new files, no rootTests additions.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Plan-gate approve loop | Custom input loop | `PlanGate.promptUser PlanGate.realKeyReader` | Already implemented + tested; realKeyReader has stdin-redirect fallback for tests |
| LLM plan call | Custom message builder | `AgentLoop.runPlanTurn` with `CompositionRoot.planSystemPromptSuffix` | Already handles retry, validation, PlanValidator |
| Plan-mode system prompt | Inline suffix string | `CompositionRoot.planSystemPromptSuffix` (public constant) | Already wired in Program.fs; same pattern for REPL |
| Plan notification to LLM | Mid-conversation system message | printfn only (never into LLM messages) | Role=System mid-conversation → HTTP 404 on 122B |

---

## Common Pitfalls

### Pitfall 1: renderStatus Signature Change Breaks Existing Tests
**What goes wrong:** `renderStatus` gains a 4th parameter `planModeActive: bool`; all existing test call sites pass only 3 args and fail to compile.
**How to avoid:** In the same plan that changes `renderStatus`, also update all call sites: `Repl.fs` (1 call site) and `ReplTests.fs` (the `/status` test, 1 direct call if any). Search for `renderStatus` across codebase before submitting.
**Warning signs:** Compiler error F# `FS0001` or `FS0003` on `renderStatus` callers.

### Pitfall 2: Future-Stub Test Count Wrong After Phase 33
**What goes wrong:** Test at ReplTests.fs line 617 (`"runMultiTurn: remaining future-stub commands (/plan /edit) print 'not yet implemented' without crashing"`) expects exactly 2 "not yet implemented" lines. After Phase 33, `/plan` is live; only `/edit` remains stubbed → the test sends `/plan\n/edit\n/exit\n` but `/plan` no longer prints "not yet implemented" — it prints "[plan mode on]". Count drops to 1, test fails.
**How to avoid:** Update the test in Plan 33-01: change stdin to `/edit\n/exit\n`; change assertion from `stubLines.Length = 2` to `stubLines.Length = 1`. Also update the test name.
**Warning signs:** Test "remaining future-stub commands" fails with `expected 2, got 1`.

### Pitfall 3: /help "[coming in v2.5]" Test Assertion Stale
**What goes wrong:** ReplTests.fs line 483 asserts `"[coming in v2.5]"` appears in help output. After renderHelp update for Phase 33, the `/plan` line no longer contains that marker. If `/edit` still has it, the assertion passes but tests the wrong command.
**How to avoid:** Update the test: either assert the new `/plan` description (e.g., "plan-gate") or assert that `/edit` still has "[coming in v2.5]" while `/plan` no longer does. Check both.
**Warning signs:** Test "'/help' prints 9-command help" fails with "expected '[coming in v2.5]' in stdout".

### Pitfall 4: PlanGate Quit Exits REPL Instead of Returning to Prompt
**What goes wrong:** Copying the `Program.fs` plan-mode loop verbatim: in `Program.fs`, `Quit` sets `finalDecision <- Some PlanGate.Quit` which breaks the while loop, then the code falls through to `exitCode = 0`. In REPL context, accidentally calling `running <- false` or `exit 0` on Quit would terminate the REPL.
**How to avoid:** In the REPL plan-gate branch, `Quit` must only set `turnDone <- true` and optionally `planModeActive <- false`. Never set `running <- false` inside the plan-gate block.
**Warning signs:** After typing `/plan`, entering a prompt, then pressing 'q', the REPL exits instead of showing the next prompt.

### Pitfall 5: plan-mode LLM Response Requires Plan Output, Gets Tool Call
**What goes wrong:** When `runPlanTurn` sends the combined system prompt (defaultSystemPrompt + planSystemPromptSuffix), the LLM might occasionally return a tool call action instead of action="plan". `runPlanTurn` handles this via its internal 2-attempt retry + `extractAndValidate` — but the error surfaced is `PlanInvalid "expected plan output, got tool/final action"`. The REPL plan-gate branch gets `Error (PlanInvalid ...)` and should print `renderError e` and abandon the plan turn.
**How to avoid:** Treat all `Error e` from `runPlanTurn` as "plan failed, print error, abandon turn" (same as `Program.fs` line 207-209). Do NOT crash or exit REPL. Print via `printfn` (not Spectre).
**Warning signs:** `plan invalid: expected plan output, got tool/final action` appears; REPL should continue.

### Pitfall 6: Console.SetOut Tests Missing testSequenced Wrapper
**What goes wrong:** New plan-mode REPL tests use Console.SetIn + Console.SetOut. If added outside the existing `testSequenced` block, they race with other tests.
**How to avoid:** ALL new ReplTests go inside the existing `testSequenced <| testList "Repl" [...]` block (ReplTests.fs line 43-44).
**Warning signs:** Flaky test failures in parallel test runs.

### Pitfall 7: maxUserRejects Constant Duplication
**What goes wrong:** `Program.fs` hardcodes `let maxUserRejects = 3` for the plan-mode reject loop. Duplicating this as another local `3` in Repl.fs creates two independent constants that can drift.
**How to avoid:** Either define `maxUserRejects = 3` as a local in the Repl plan-mode branch (acceptable, given the constant is tiny and domain-local) or extract to a shared module-level constant. Simplest: local `let maxUserRejects = 3` in the Repl branch (same as Program.fs pattern — not a shared concern).

---

## Code Examples

### Toggle Handler in runMultiTurnWithSession
```fsharp
// Source: Repl.fs existing /clear handler (line 206) as pattern + Phase 33 new logic
// ABOVE the existing Prompt arm; BELOW the Resume arm
| Some (Slash Plan) ->
    planModeActive <- not planModeActive
    if planModeActive then
        printfn "[plan mode on] — next prompt will enter plan-gate before execution"
    else
        printfn "[plan mode off] — returning to direct agent-loop"
| Some (Slash Edit) ->
    printfn "(not yet implemented — coming in a future v2.5 phase)"
```

### Plan-Mode Prompt Dispatch (Repl.fs)
```fsharp
// Source: Program.fs plan-mode block (lines 172-256) adapted for REPL context
// Must appear BEFORE `| Some (Prompt prompt) ->` (F# matches top-to-bottom)
| Some (Prompt prompt) when planModeActive ->
    let model =
        components.Config.ForcedModel
        |> Option.defaultValue BlueCode.Core.Domain.Qwen122B
    let mutable rejectCount = 0
    let mutable currentPrompt = prompt
    let mutable turnDone = false
    let maxUserRejects = 3

    while not turnDone && rejectCount < maxUserRejects do
        let! planResult =
            BlueCode.Core.AgentLoop.runPlanTurn
                components.Config
                components.LlmClient
                model
                currentSession.Steps
                currentPrompt
                CompositionRoot.planSystemPromptSuffix
                System.Threading.CancellationToken.None

        match planResult with
        | Error e ->
            printfn "%s" (renderError e)
            turnDone <- true
        | Ok plan ->
            PlanGate.render plan
            match PlanGate.promptUser PlanGate.realKeyReader with
            | PlanGate.Accept ->
                planModeActive <- false
                let! (code, newSteps) =
                    runSingleTurn prompt currentSession.Steps components renderMode
                let updated =
                    { currentSession with
                        Steps = currentSession.Steps @ newSteps
                        LastActivityAt = DateTimeOffset.UtcNow }
                currentSession <- updated
                let! saveRes = sessionStore.Save updated System.Threading.CancellationToken.None
                match saveRes with
                | Ok () -> ()
                | Error e ->
                    Log.Warning("Session save failed: {Error}", sprintf "%A" e)
                    eprintfn "WARNING: session save failed: %A" e
                lastCode <- if code = 130 then 0 else code
                turnDone <- true
            | PlanGate.Reject ->
                rejectCount <- rejectCount + 1
                currentPrompt <-
                    sprintf "[PLAN REJECTED] The previous plan was rejected by the user. Propose a different plan.\n\n%s" prompt
            | PlanGate.Edit comment ->
                rejectCount <- rejectCount + 1
                currentPrompt <-
                    sprintf "[PLAN EDIT NOTE: %s] Revise the previous plan accordingly.\n\n%s" comment prompt
            | PlanGate.Quit ->
                planModeActive <- false
                turnDone <- true

    if not turnDone then
        printfn "Plan-mode: %d rejections without acceptance — abandoning." rejectCount
        planModeActive <- false
```

### renderStatus Updated Signature (Rendering.fs)
```fsharp
// Source: existing renderStatus (Rendering.fs lines 156-172) + planModeActive param
let renderStatus (session: Session) (forcedModel: Model option) (maxModelLen: int) (planModeActive: bool) : string =
    let (SessionId idStr) = session.Id
    let modelName =
        match forcedModel with
        | Some Qwen122B -> "122b"
        | Some Qwen35B  -> "35b"
        | None          -> "122b (default)"
    let steps = session.Steps.Length
    let accChars =
        session.Steps
        |> List.sumBy (fun s ->
            (sprintf "%A" s.Action).Length + (sprintf "%A" s.ToolResult).Length)
    let maxChars = maxModelLen * 4
    let pct = if maxChars > 0 then accChars * 100 / maxChars else 0
    let planLine = if planModeActive then "\nplan-mode: on (next prompt uses plan-gate)" else ""
    sprintf
        "session:  %s\nmodel:    %s\nsteps:    %d\nchars:    %d / ~%d (%d%%) [floor; probed on first LLM call]%s"
        idStr modelName steps accChars maxChars pct planLine
```

### Test: plan-mode toggle (ReplTests.fs new testCase)
```fsharp
// Source: existing ReplTests.fs pattern (Console.SetIn + Console.SetOut + testSequenced)
testCase "runMultiTurn: '/plan' toggles plan-mode on; '/plan' again toggles off" <| fun () ->
    let originalIn = Console.In
    let originalOut = Console.Out
    use stdinReader = new StringReader("/plan\n/plan\n/exit\n")
    use stdoutWriter = new StringWriter()
    Console.SetIn(stdinReader)
    Console.SetOut(stdoutWriter)
    // ... components setup ...
    try
        let exitCode =
            BlueCode.Cli.Repl.runMultiTurn components Compact
            |> fun t -> t.GetAwaiter().GetResult()
        Console.Out.Flush()
        let captured = stdoutWriter.ToString()
        Expect.equal exitCode 0 "exit code 0"
        Expect.stringContains captured "[plan mode on]" "first /plan prints on message"
        Expect.stringContains captured "[plan mode off]" "second /plan prints off message"
    finally
        Console.SetIn(originalIn)
        Console.SetOut(originalOut)
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `/plan` stub ("not yet implemented") | Real toggle + plan-gate turn | Phase 33 | Replaces the `Plan | Edit` combined stub arm with separate `Plan` (live) and `Edit` (stub) arms |
| Plan-mode only in single-turn `Program.fs` | Plan-mode available in REPL via `/plan` toggle | Phase 33 | `runPlanTurn` + `PlanGate` now wired from REPL context |
| `renderStatus` has 3 params | `renderStatus` has 4 params (+ `planModeActive`) | Phase 33 | All call sites must be updated |

**The "coming in v2.5" placeholder:**
- `/plan` — Phase 33 makes it live; help text stub removed
- `/edit` — still "[coming in v2.5]" (Phase 34 scope, NOT this phase)

---

## Open Questions

1. **After Accept+Execute, does plan-mode stay on or turn off?**
   - What we know: SC-3 says "plan-mode 중 /plan 다시 입력 시 off" (toggling /plan off) and "다음 turn 부터 plan-mode 적용" (applies from next turn). It's ambiguous whether Accept should auto-disable plan-mode or leave it on for subsequent turns.
   - Recommendation: **Turn off after Accept** (i.e., each plan-gated turn is a one-shot; user must re-/plan if they want the next turn plan-gated too). This avoids the user being locked in a plan-review loop they forgot to disable. The code example above follows this: `planModeActive <- false` on Accept.
   - Alternative: Leave on after Accept (sticky mode). Requires explicit `/plan` to disable after each approved turn.
   - Planner decision: pick one; recommend "turn off after Accept" for ergonomic safety.

2. **After Quit in plan-gate, does plan-mode stay on or turn off?**
   - What we know: Quit abandons the current plan proposal. If plan-mode stays on, the user's next prompt would immediately try to plan again. If it turns off, the user must re-/plan.
   - Recommendation: **Turn off after Quit** (code example above follows this). User explicitly chose to abandon — staying in plan-mode would be surprising.

3. **Notification timing: "[plan mode on]" — immediately on /plan or before next turn?**
   - SC-6 says "toggle 알림은 다음 turn 시작 시 user-facing console only". But "[plan mode on]" printed immediately on `/plan` is simpler and clearer UX. "다음 turn 시작 시" may mean "the plan gate itself is the announcement of plan-mode being active".
   - Recommendation: Print `"[plan mode on]"` immediately on `/plan` keystroke (before next turn). The PlanGate UI itself (rationale + a/r/e/q prompt) serves as the "turn start" announcement. SC-6's main constraint is that the toggle notification is NOT sent to the LLM — printfn satisfies this.

4. **`--plan` startup flag + in-REPL `/plan` coexistence:**
   - `--plan` flag (Program.fs lines 63-65) rejects REPL mode: `if isPlanMode && List.isEmpty promptWords then exit 2`. So `--plan` without a prompt → exit 2; `--plan` with a prompt → single-turn plan flow (never enters REPL).
   - There is no startup scenario where `--plan` and REPL plan-mode interact. They are mutually exclusive at the Program.fs level.
   - Conclusion: No change to `--plan` flag behavior. REPL plan-mode via `/plan` is a new, independent path.

5. **IKeyReader stdin source inside REPL:**
   - In REPL, `Console.ReadLine()` is the REPL prompt reader. `PlanGate.realKeyReader.ReadKey()` uses `Console.ReadKey(intercept=true)`, which reads from the same `Console.In`. When the REPL REPL loop pauses for PlanGate, no contention occurs (REPL's while loop body is executing, not blocked on ReadLine). The two are sequential in the same thread. No contention.
   - In tests, `Console.SetIn` with a StringReader covers all reads (REPL ReadLine + PlanGate ReadKey fallback both read from `Console.In`). Script: `/plan\n<prompt>\na\n/exit\n` → `/plan` → ReadLine; `<prompt>` → ReadLine; `a` → ReadKey falls back to ReadLine since SetIn is StringReader; `/exit` → ReadLine.

---

## Sources

### Primary (HIGH confidence)
- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/Repl.fs` — full file read; `runMultiTurnWithSession` structure, existing mutable pattern, Prompt arm dispatch
- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/Program.fs` — full file read; `isPlanMode` block (lines 172-255), `runPlanTurn` call, `PlanGate.promptUser` call
- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/PlanGate.fs` — full file read; `IKeyReader`, `realKeyReader`, `promptUser`, `render`
- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/CompositionRoot.fs` — full file read; `planSystemPromptSuffix` (public), `AppComponents`, `bootstrap`
- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/Rendering.fs` — full file read; `renderStatus` signature, `renderHelp` string
- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/SlashCommand.fs` — full file read; `Plan` DU case, parse function
- `/Users/ohama/projs/blueCode/src/BlueCode.Core/AgentLoop.fs` — full file read; `runPlanTurn` signature and implementation
- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/BlueCode.Cli.fsproj` — compile order confirmed (FileSessionStore → Rendering → SlashCommand → CompositionRoot → PlanGate → Repl → CliArgs → Program)
- `/Users/ohama/projs/blueCode/tests/BlueCode.Tests/ReplTests.fs` — full file read; all existing REPL tests, future-stub test (line 617), /help test (line 448), /status test (line 488)
- `/Users/ohama/projs/blueCode/tests/BlueCode.Tests/PlanGateTests.fs` — full file read; scriptedReader pattern, withCapturedStdout helper
- `/Users/ohama/projs/blueCode/tests/BlueCode.Tests/AgentLoopTests.fs` — lines 49-141; `runPlanTurnTests` test structure
- `/Users/ohama/projs/blueCode/tests/BlueCode.Tests/RouterTests.fs` — rootTests list (no new test module needed)
- `/Users/ohama/projs/blueCode/tests/BlueCode.Tests/BlueCode.Tests.fsproj` — compile Include order (no new files needed)
- `/Users/ohama/projs/blueCode/bench/run.sh` — lines 1-46; confirmed `--plan` flag never used in bench

---

## Metadata

**Confidence breakdown:**
- Architecture (toggle pattern): HIGH — `mutable` cell pattern is identical to `/clear`'s session rotation in Repl.fs; no new primitives
- Architecture (plan-gate in REPL): HIGH — `runPlanTurn` + `PlanGate` are already shipped and tested; REPL call site mirrors Program.fs exactly
- `renderStatus` signature change: HIGH — straightforward parameter addition; all call sites found
- Test strategy: HIGH — same `testSequenced` + Console.SetIn/SetOut + scripted stdin pattern used by Phase 31/32
- Open Questions 1-2 (auto-disable semantics): MEDIUM — requirement is ambiguous; recommendation given
- Bench non-regression: HIGH — confirmed bench never enters REPL or plan-mode path

**Research date:** 2026-05-05
**Valid until:** 2026-06-04 (stable domain; all dependencies are local Cli-layer code)
