---
phase: 33-slash-plan-toggle
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/BlueCode.Cli/Repl.fs
  - src/BlueCode.Cli/Rendering.fs
  - tests/BlueCode.Tests/RenderingTests.fs
  - tests/BlueCode.Tests/ReplTests.fs
autonomous: true

must_haves:
  truths:
    - "User typing /plan in REPL toggles a mutable planModeActive bool; '[plan mode on]' / '[plan mode off]' is printed via printfn (NOT sent to LLM)"
    - "User typing /status while planModeActive=true sees a 'plan-mode: on' line in the status output; with planModeActive=false the line is absent (status stays quiet by default)"
    - "renderHelp shows /plan with a live one-line description (no '[coming in v2.5]' marker on /plan); only /edit retains the marker"
    - "renderStatus has a 4th parameter planModeActive: bool; all 5 existing call sites updated (1 in Repl.fs, 4 in RenderingTests.fs)"
    - "Existing future-stub ReplTests test now expects exactly 1 'not yet implemented' line (only /edit remains stubbed); test name updated"
    - "Existing RenderingTests '[coming in v2.5]' marker test now asserts exactly 1 occurrence (only /edit retains the marker); /plan line explicitly asserted to NOT contain the marker"
    - "Cli project builds clean (no warnings); Core/ untouched"
  artifacts:
    - path: "src/BlueCode.Cli/Repl.fs"
      provides: "mutable planModeActive cell + dispatcher arms (Slash Plan toggle, Slash Edit slim stub) + Prompt-arm guard for plan-mode turn"
      contains: "let mutable planModeActive"
      contains_2: "Slash Plan ->"
      contains_3: "when planModeActive"
    - path: "src/BlueCode.Cli/Rendering.fs"
      provides: "renderStatus 4-param signature (adds planModeActive: bool) + renderHelp updated /plan line"
      contains: "planModeActive: bool"
      contains_2: "toggle plan-mode on/off"
    - path: "tests/BlueCode.Tests/RenderingTests.fs"
      provides: "renderStatus tests updated for 4-param signature + new test for planModeActive=true; renderHelp marker test refined to expect 1 occurrence"
    - path: "tests/BlueCode.Tests/ReplTests.fs"
      provides: "future-stub test updated to expect 1 line (/edit only); /status existing test updated for new signature"
  key_links:
    - from: "src/BlueCode.Cli/Repl.fs"
      to: "src/BlueCode.Core/AgentLoop.fs (runPlanTurn)"
      via: "AgentLoop.runPlanTurn call inside the new Prompt+planModeActive arm"
      pattern: "AgentLoop\\.runPlanTurn"
    - from: "src/BlueCode.Cli/Repl.fs"
      to: "src/BlueCode.Cli/CompositionRoot.fs (planSystemPromptSuffix)"
      via: "CompositionRoot.planSystemPromptSuffix passed as systemPromptSuffix arg to runPlanTurn"
      pattern: "CompositionRoot\\.planSystemPromptSuffix"
    - from: "src/BlueCode.Cli/Repl.fs"
      to: "src/BlueCode.Cli/PlanGate.fs (render + promptUser + realKeyReader)"
      via: "PlanGate.render plan + PlanGate.promptUser PlanGate.realKeyReader call inside plan-gate inline loop"
      pattern: "PlanGate\\.(render|promptUser)"
    - from: "src/BlueCode.Cli/Repl.fs"
      to: "src/BlueCode.Cli/Rendering.fs (renderStatus)"
      via: "Rendering.renderStatus call updated to pass planModeActive 4th arg"
      pattern: "Rendering\\.renderStatus.*planModeActive"
---

<objective>
Phase 33 — Plan 01: Wire `/plan` toggle into `Repl.runMultiTurnWithSession`. Add a
`mutable planModeActive` cell, a `Slash Plan` toggle arm that flips it (with `printfn`
notification), a `Prompt prompt when planModeActive` arm that runs the full plan-gate
inline (mirroring `Program.fs` lines 172-256), and update `renderStatus` to accept a
4th `planModeActive: bool` parameter so `/status` can display the current toggle state.
Update `renderHelp` to drop the `[coming in v2.5]` marker on `/plan` (only `/edit` retains
it). Adjust 4 existing tests that pass through the affected signatures so the build
stays green; add 1 new `renderStatus` test covering the planModeActive=true display path.

Purpose: This plan is the surgical source-code change. The new behavior tests (toggle
on/off, /status display, plan-gate Accept/Quit/auto-disable) live in Plan 33-02 — keeping
this plan focused and reviewable. The seam is the same shape as Phase 32-02 (live handler
replaces stub + renderHelp marker drop + signature evolution). After this plan, the
Cli project builds, all existing tests pass (with adjustments), and `/plan` is functional;
Plan 33-02 verifies the new behavior end-to-end and gates the bench.

Roadmap success criterion 1: `planModeActive: bool` REPL state + `/plan` toggle + `/status`
display — implemented via mutable cell + `Slash Plan` arm + `renderStatus` 4th parameter.

Roadmap success criterion 2 (`runPlanTurn` path on next prompt when active) and 3
(`/plan` again toggles off) — both implemented by this plan via the dispatch branching.
The behavior is verified empirically in Plan 33-02.

Roadmap success criterion 4 (mid-turn `/plan` is invalid) — satisfied by architecture
(REPL ReadLine blocks the loop thread until the turn completes — research § Q4 confirms
no race possible). No special guard is added because the scenario cannot arise.

Roadmap success criterion 6 (Role=System invariant) — toggle notifications use `printfn`
only, never injected into the LLM message list. The plan-gate inline loop reuses
`runPlanTurn` (which already enforces Role=User mid-conversation per Phase 20-03 probe).

Open question resolutions adopted from research § Open Questions (planner decisions):
1. **After Accept+Execute → planModeActive auto-disables.** One-shot ergonomics; user
   re-types `/plan` for next plan-gated turn. Avoids "stuck in plan-review loop"
   surprise. Researcher recommended this; planner adopts.
2. **After Quit in plan-gate → planModeActive auto-disables.** User explicitly chose to
   abandon — staying in plan-mode would be surprising. Researcher recommended this;
   planner adopts.
3. **`[plan mode on]` notification is immediate (printed on `/plan` keystroke), not
   deferred.** PlanGate UI itself serves as the "turn start" announcement; immediate
   printfn satisfies SC-6's "user-facing console only / not sent to LLM" constraint and
   matches the simplicity of all existing slash command notifications (`/clear`, `/resume`,
   etc.). Researcher recommended this; planner adopts.

Output:
- `Repl.runMultiTurnWithSession` gets `let mutable planModeActive = false` cell, a
  `Slash Plan` arm (toggle + printfn), a `Slash Edit` arm (slim stub),
  a `Prompt prompt when planModeActive` arm (full plan-gate inline loop with rejectCount,
  Accept/Reject/Edit/Quit handling, save semantics matching the standard Prompt arm).
- `renderStatus` signature gains 4th param `planModeActive: bool`; output appends
  `\nplan-mode: on (next prompt uses plan-gate)` only when `planModeActive=true`.
- `renderHelp` `/plan` line: `toggle plan-mode on/off; next prompt uses plan-gate when on`
  (no `[coming in v2.5]`); `/edit` line unchanged.
- All 5 `renderStatus` call sites updated to pass new 4th arg.
- 2 existing tests updated:
  - ReplTests.fs `runMultiTurn: remaining future-stub commands (/plan /edit) print 'not
    yet implemented' without crashing` → now `(/edit only)`, expects 1 line, stdin
    `/edit\n/exit\n` (no `/plan`).
  - ReplTests.fs `runMultiTurn: '/status' prints session id, model, steps, chars` → call
    site updated implicitly through Rendering.renderStatus signature change (no test code
    change needed because the test goes through `runMultiTurn` which calls the production
    Repl call site — but verify the test still passes after Repl.fs is updated).
- 2 RenderingTests.fs tests updated (4 renderStatus call sites use 3 args today; need
  `false` 4th arg) + 1 marker test refined to expect 1 occurrence + 1 NEW renderStatus
  test added covering planModeActive=true (asserts "plan-mode: on" appears).
- Bench-gate verification deferred to Plan 33-02 Task 3.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/STATE.md
@.planning/REQUIREMENTS.md
@.planning/phases/33-slash-plan-toggle/33-RESEARCH.md
@CLAUDE.md
@src/BlueCode.Cli/Repl.fs
@src/BlueCode.Cli/Rendering.fs
@src/BlueCode.Cli/SlashCommand.fs
@src/BlueCode.Cli/PlanGate.fs
@src/BlueCode.Cli/CompositionRoot.fs
@src/BlueCode.Cli/Program.fs
@src/BlueCode.Core/AgentLoop.fs
@src/BlueCode.Core/Domain.fs
@tests/BlueCode.Tests/ReplTests.fs
@tests/BlueCode.Tests/RenderingTests.fs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Wire planModeActive cell + /plan toggle + plan-gate Prompt arm into Repl.fs; update renderStatus signature + renderHelp in Rendering.fs</name>
  <files>src/BlueCode.Cli/Repl.fs, src/BlueCode.Cli/Rendering.fs</files>
  <action>
1. Edit `src/BlueCode.Cli/Rendering.fs` FIRST (the renderStatus signature change is the
upstream concern; Repl.fs depends on it).

(a) Update the `renderStatus` function (currently lines 156-172) to accept a 4th
parameter `planModeActive: bool`. Append a `plan-mode: on (next prompt uses plan-gate)`
line to the output WHEN `planModeActive = true`; emit nothing extra otherwise (keeps
default status output quiet — research § Q7).

CURRENT (Rendering.fs lines 156-172):

```fsharp
let renderStatus (session: Session) (forcedModel: Model option) (maxModelLen: int) : string =
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
    let maxChars = maxModelLen * 4   // tokens * ~4 chars/token
    let pct = if maxChars > 0 then accChars * 100 / maxChars else 0
    sprintf
        "session:  %s\nmodel:    %s\nsteps:    %d\nchars:    %d / ~%d (%d%%) [floor; probed on first LLM call]"
        idStr modelName steps accChars maxChars pct
```

REPLACE WITH:

```fsharp
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
    let maxChars = maxModelLen * 4   // tokens * ~4 chars/token
    let pct = if maxChars > 0 then accChars * 100 / maxChars else 0
    let planLine =
        if planModeActive then "\nplan-mode: on (next prompt uses plan-gate)"
        else ""
    sprintf
        "session:  %s\nmodel:    %s\nsteps:    %d\nchars:    %d / ~%d (%d%%) [floor; probed on first LLM call]%s"
        idStr modelName steps accChars maxChars pct planLine
```

(b) Update the `renderHelp` string constant (currently lines 129-139) — the `/plan`
line drops `[coming in v2.5]` and gains a live description. The `/edit` line is
UNCHANGED (Phase 34 will handle it).

CURRENT (Rendering.fs lines 129-139):

```fsharp
let renderHelp : string =
    """slash commands:
  /help              show this help
  /status            session info: id, model, steps, context %
  /clear             reset session in-place (new session id, keep REPL running)
  /exit              save session and quit
  /quit              alias for /exit
  /sessions          list 10 most-recent sessions
  /resume <id>       switch to a saved session in-place
  /plan              toggle plan-mode for next turn [coming in v2.5]
  /edit              open $EDITOR for multi-line input [coming in v2.5]"""
```

REPLACE WITH:

```fsharp
let renderHelp : string =
    """slash commands:
  /help              show this help
  /status            session info: id, model, steps, context %
  /clear             reset session in-place (new session id, keep REPL running)
  /exit              save session and quit
  /quit              alias for /exit
  /sessions          list 10 most-recent sessions
  /resume <id>       switch to a saved session in-place
  /plan              toggle plan-mode on/off; next prompt uses plan-gate when on
  /edit              open $EDITOR for multi-line input [coming in v2.5]"""
```

DO NOT modify `renderSessions`, `renderError`, `renderStep`, `renderResult`, or any
other function in Rendering.fs.

DO NOT add new `open` directives.

2. Edit `src/BlueCode.Cli/Repl.fs`. THREE structural changes inside
`runMultiTurnWithSession`:

(a) Add `let mutable planModeActive = false` AFTER the existing
`let mutable currentSession : Session = initialSession` (currently line 176). The new
declaration goes between `currentSession` and `lastCode`:

CURRENT (Repl.fs lines 176-178):

```fsharp
        let mutable currentSession : Session = initialSession
        let mutable lastCode = 0
        let mutable running = true
```

REPLACE WITH:

```fsharp
        let mutable currentSession : Session = initialSession
        let mutable planModeActive = false   // Phase 33: /plan toggle state; flips to true on /plan, false after Accept/Quit/exhausted-rejects
        let mutable lastCode = 0
        let mutable running = true
```

(b) Update the `Slash Status` arm (currently line 199) so the `Rendering.renderStatus`
call passes `planModeActive` as the 4th arg:

CURRENT (Repl.fs line 199):

```fsharp
                | Some (Slash Status) ->
                    printfn "%s" (Rendering.renderStatus currentSession components.Config.ForcedModel components.MaxModelLen)
```

REPLACE WITH:

```fsharp
                | Some (Slash Status) ->
                    printfn "%s" (Rendering.renderStatus currentSession components.Config.ForcedModel components.MaxModelLen planModeActive)
```

(c) Replace the existing combined `Slash (Plan | Edit)` future-stub arm (currently
lines 249-252) with TWO arms: live `Slash Plan` toggle + slimmed `Slash Edit` stub.
Then ADD the new `Prompt prompt when planModeActive` arm IMMEDIATELY BEFORE the
existing `Some (Prompt prompt) ->` arm (currently line 253).

CURRENT (Repl.fs lines 249-252 — to be REPLACED):

```fsharp
                | Some (Slash (Plan | Edit)) ->
                    // Phase 33 (Plan) and Phase 34 (Edit) future-stubs.
                    // Sessions and Resume have moved to real handlers above.
                    printfn "(not yet implemented — coming in a future v2.5 phase)"
```

REPLACE the above block (and inject the new plan-gate Prompt arm) with the following.
F# `match` arms are matched top-to-bottom, so the `when planModeActive` guarded arm
MUST come BEFORE the unguarded `Some (Prompt prompt) ->` arm. The `Slash Plan` and
`Slash Edit` arms can come in any order before that — keep the same order as below for
readability:

```fsharp
                | Some (Slash Plan) ->
                    // Phase 33 (SLASH-07): toggle plan-mode for the NEXT prompt turn.
                    // Idempotent flip — typing /plan twice toggles on then off.
                    // Notification is printfn only (SC-6: NOT injected into LLM messages —
                    // mid-conversation Role=System triggers HTTP 404 on Qwen 3.5 122B per
                    // Phase 20-03 probe; the notification is purely user-facing console).
                    planModeActive <- not planModeActive
                    if planModeActive then
                        printfn "[plan mode on] — next prompt will enter plan-gate before execution"
                    else
                        printfn "[plan mode off] — returning to direct agent-loop"
                | Some (Slash Edit) ->
                    // Phase 34 future-stub. Phase 33 promoted /plan to a real handler;
                    // /edit remains the sole "not yet implemented" command in v2.5.
                    printfn "(not yet implemented — coming in a future v2.5 phase)"
                | Some (Prompt prompt) when planModeActive ->
                    // Phase 33 (SLASH-07): plan-gated turn. Mirrors Program.fs lines 172-256
                    // (single-turn --plan mode) adapted for in-REPL context.
                    //
                    // Differences from Program.fs:
                    //   - Quit returns to REPL prompt (NOT process exit).
                    //   - planModeActive auto-disables on Accept (after execute), on Quit,
                    //     and on rejectCount-exhaustion. Open Question #1+#2 resolution:
                    //     one-shot semantics; user re-types /plan for next plan-gated turn.
                    //   - lastCode behaves identically to the standard Prompt arm
                    //     (130 → 0 mapping for graceful Ctrl+C; otherwise pass through).
                    let model =
                        components.Config.ForcedModel
                        |> Option.defaultValue Qwen122B   // 122B canonical default; matches Program.fs:180
                    let maxUserRejects = 3                // matches Program.fs:187 (research § Pitfall 7 — local constant OK)
                    let mutable rejectCount = 0
                    let mutable currentPrompt = prompt
                    let mutable turnDone = false

                    while not turnDone && rejectCount < maxUserRejects do
                        let! planResult =
                            BlueCode.Core.AgentLoop.runPlanTurn
                                components.Config
                                components.LlmClient
                                model
                                currentSession.Steps
                                currentPrompt
                                CompositionRoot.planSystemPromptSuffix
                                CancellationToken.None

                        match planResult with
                        | Error e ->
                            // runPlanTurn already retried internally (2 attempts).
                            // Surface the error and abandon this turn; keep REPL alive.
                            // planModeActive auto-disables — user can re-/plan to retry.
                            printfn "%s" (renderError e)
                            planModeActive <- false
                            turnDone <- true
                        | Ok plan ->
                            PlanGate.render plan
                            match PlanGate.promptUser PlanGate.realKeyReader with
                            | PlanGate.Accept ->
                                // Disable plan-mode BEFORE execute — one-shot semantics
                                // (Open Question #1 resolution). User re-types /plan for
                                // the next plan-gated turn.
                                planModeActive <- false
                                let! (code, newSteps) =
                                    runSingleTurn prompt currentSession.Steps components renderMode
                                let updated =
                                    { currentSession with
                                        Steps = currentSession.Steps @ newSteps
                                        LastActivityAt = DateTimeOffset.UtcNow }
                                currentSession <- updated
                                let! saveRes = sessionStore.Save updated CancellationToken.None
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
                                // User abandoned; return to REPL prompt (NOT process exit).
                                // planModeActive auto-disables (Open Question #2 resolution).
                                planModeActive <- false
                                turnDone <- true

                    if not turnDone then
                        // Loop exited via rejectCount >= maxUserRejects without acceptance.
                        printfn "Plan-mode: %d rejections without acceptance — abandoning." rejectCount
                        planModeActive <- false
```

Insert the above block as the replacement for the OLD combined `Slash (Plan | Edit)`
arm (lines 249-252 of current Repl.fs). The unguarded `Some (Prompt prompt) ->` arm at
the current line 253 onwards stays BYTE-IDENTICAL — only the `when planModeActive`
guarded arm is inserted IMMEDIATELY ABOVE it.

CRITICAL preservation requirements (research § Pitfall 1, Pitfall 4, Pitfall 6):
- The unguarded `Some (Prompt prompt) ->` arm body (currently Repl.fs lines 253-269)
  MUST stay byte-identical. The `when planModeActive` guarded arm sits ABOVE it; F#
  matches arms top-to-bottom so the unguarded arm catches all non-plan-mode prompts.
- Plan-gate Quit must NEVER set `running <- false` and never call `exit` (research §
  Pitfall 4). Only `planModeActive <- false` + `turnDone <- true`.
- All notifications use `printfn` (NOT `AnsiConsole.MarkupLine`) — CLAUDE.md "Stream
  separation" + research § Pitfall 1.
- The notification text contains `[plan mode on]` / `[plan mode off]` (lowercase,
  square-bracketed) — Plan 33-02 tests assert these exact substrings.
- `let!` inside the plan-gate inline loop is allowed because the enclosing block is
  inside the `task {}` CE on line 171.
- `BlueCode.Core.AgentLoop.runPlanTurn` is fully-qualified (the module is opened at
  line 10 of Repl.fs but explicit qualification is clearer for grep-ability and
  matches Program.fs:195).
- `CompositionRoot.planSystemPromptSuffix` is referenced via the `open
  BlueCode.Cli.CompositionRoot` already on line 13 of Repl.fs — no new `open` needed.
- `PlanGate.render` and `PlanGate.promptUser PlanGate.realKeyReader` follow the same
  pattern as Program.fs:210-211 — but `BlueCode.Cli.PlanGate` is NOT yet opened in
  Repl.fs. ADD `open BlueCode.Cli.PlanGate` to the open block at the top of Repl.fs
  (insert AFTER line 13 `open BlueCode.Cli.CompositionRoot`). This mirrors how
  Program.fs imports it (Program.fs uses fully-qualified `PlanGate.render` because
  Program is `module Program`, not opened-namespace style).
  - WAIT — re-verify. Program.fs line 211 uses `PlanGate.render plan` and
    `PlanGate.promptUser PlanGate.realKeyReader`. That works because `PlanGate` is the
    last segment of `BlueCode.Cli.PlanGate` and F# resolves it via the namespace open
    at the top (Program.fs uses `open BlueCode.Cli` on line 8 — gives access to
    `PlanGate` as a sub-module of the `BlueCode.Cli` namespace).
  - For Repl.fs: the opens at the top do NOT include `open BlueCode.Cli` (only
    sub-module-level opens like `BlueCode.Cli.Rendering`). The cleanest fix is to ADD
    `open BlueCode.Cli.PlanGate` to the open block. After this `open`, `PlanGate.render
    plan` becomes invalid (the module-name qualifier conflicts with opening it).
  - **Correct approach:** Use the FULLY QUALIFIED name `BlueCode.Cli.PlanGate.render`
    and `BlueCode.Cli.PlanGate.promptUser BlueCode.Cli.PlanGate.realKeyReader` in the
    new arm. Do NOT add a new `open` directive (keeps Repl.fs's open block clean and
    matches the fully-qualified `BlueCode.Core.AgentLoop.runPlanTurn` style already
    used by the new code above).
  - REPLACE in the code block above:
    - `PlanGate.render plan` → `BlueCode.Cli.PlanGate.render plan`
    - `PlanGate.promptUser PlanGate.realKeyReader` →
      `BlueCode.Cli.PlanGate.promptUser BlueCode.Cli.PlanGate.realKeyReader`
    - `PlanGate.Accept`, `PlanGate.Reject`, `PlanGate.Edit comment`, `PlanGate.Quit`
      → `BlueCode.Cli.PlanGate.Accept`, `BlueCode.Cli.PlanGate.Reject`,
        `BlueCode.Cli.PlanGate.Edit comment`, `BlueCode.Cli.PlanGate.Quit`
- `Qwen122B` is in scope via `open BlueCode.Core.Domain` (Repl.fs line 8) — no
  qualification needed for the model default.
- `CancellationToken` is in scope via `open System.Threading` (Repl.fs line 4).
- `DateTimeOffset.UtcNow` and `Log.Warning` — both already imported (line 3, 6).
- `eprintfn` is built-in F# Core.
- Do NOT add `async {}` (project uses `task {}` exclusively in this file — CI-enforced).

Apply the fully-qualified `BlueCode.Cli.PlanGate.*` substitution to the code block
before writing it to the file.

3. Build the Cli project to verify compilation:

```
dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj
```

Expect exit 0. Warnings from the new code MUST be 0 (incomplete pattern matches,
unused variables, etc. are not acceptable). Other-source warnings are out of scope.

4. Commit atomically (TWO files, ONE commit — the Repl.fs change requires the
Rendering.fs signature change to compile; both are part of the same wiring):

```
git add src/BlueCode.Cli/Repl.fs src/BlueCode.Cli/Rendering.fs
git commit -m "feat(33-01): wire /plan toggle + plan-gate REPL dispatch (SLASH-07)"
```

DO NOT use `git add -A` / `git add .`.
DO NOT touch `runSingleTurn`, `shouldWarnContextWindow`, `runMultiTurn` (the legacy
entry on line 285), or any function other than `runMultiTurnWithSession` in Repl.fs.
DO NOT touch any function other than `renderStatus` and `renderHelp` in Rendering.fs.
  </action>
  <verify>
- `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0; ZERO warnings from
  Repl.fs or Rendering.fs (`grep -E "Repl\.fs|Rendering\.fs" build.log | grep -i
  warning` empty).
- `grep -c "let mutable planModeActive = false" src/BlueCode.Cli/Repl.fs` returns 1.
- `grep -c "Slash Plan ->" src/BlueCode.Cli/Repl.fs` returns 1 (the new live arm).
- `grep -c "Slash Edit ->" src/BlueCode.Cli/Repl.fs` returns 1 (the slim stub arm).
- `grep -c "Slash (Plan | Edit)" src/BlueCode.Cli/Repl.fs` returns 0 (old combined
  stub REMOVED).
- `grep -c "when planModeActive" src/BlueCode.Cli/Repl.fs` returns 1 (the guarded
  Prompt arm).
- `grep -c "BlueCode.Core.AgentLoop.runPlanTurn" src/BlueCode.Cli/Repl.fs` returns 1.
- `grep -c "CompositionRoot.planSystemPromptSuffix" src/BlueCode.Cli/Repl.fs` returns 1.
- `grep -c "BlueCode.Cli.PlanGate.render plan" src/BlueCode.Cli/Repl.fs` returns 1.
- `grep -c "BlueCode.Cli.PlanGate.promptUser" src/BlueCode.Cli/Repl.fs` returns 1.
- `grep -c "\[plan mode on\]" src/BlueCode.Cli/Repl.fs` returns 1.
- `grep -c "\[plan mode off\]" src/BlueCode.Cli/Repl.fs` returns 1.
- `grep -c "Rendering.renderStatus currentSession components.Config.ForcedModel
  components.MaxModelLen planModeActive" src/BlueCode.Cli/Repl.fs` returns 1 (the
  /status call site updated to pass the 4th arg).
- `grep -c "running <- false" src/BlueCode.Cli/Repl.fs` returns 1 (only the existing
  Slash Exit arm — research § Pitfall 4: plan-gate Quit must NOT set running false).
- `grep -c "planModeActive: bool" src/BlueCode.Cli/Rendering.fs` returns 1 (renderStatus
  signature).
- `grep -c "plan-mode: on (next prompt uses plan-gate)" src/BlueCode.Cli/Rendering.fs`
  returns 1.
- `grep -c "toggle plan-mode on/off" src/BlueCode.Cli/Rendering.fs` returns 1
  (renderHelp updated /plan line).
- `grep -c "\[coming in v2.5\]" src/BlueCode.Cli/Rendering.fs` returns 1 (only /edit
  line retains the marker).
- `git diff master -- src/BlueCode.Core/` is empty (Core untouched).
- `bash scripts/check-no-async.sh` exits 0 (no `async {}` literal in Core).
- `git log -1 --oneline` contains `feat(33-01)` + `wire /plan toggle`.
  </verify>
  <done>
- `runMultiTurnWithSession` has `let mutable planModeActive = false` cell + four new
  arms (`Slash Plan` toggle, `Slash Edit` slim stub, `Prompt prompt when planModeActive`
  plan-gate inline loop, plus the existing unguarded `Some (Prompt prompt) ->` arm
  unchanged below).
- The plan-gate inline loop respects open-question resolutions: Accept → planModeActive
  off → execute → save → 130-mapping for lastCode; Quit → planModeActive off, return to
  REPL; Reject/Edit → rejectCount++ with [PLAN REJECTED]/[PLAN EDIT NOTE] prefix; max-3
  rejections → "abandoning" message + planModeActive off.
- `renderStatus` 4-param signature; appends "plan-mode: on (next prompt uses plan-gate)"
  ONLY when active.
- `renderHelp` /plan line has live description; /edit retains [coming in v2.5].
- Cli project builds clean; no warnings from modified files.
- Atomic commit `feat(33-01): wire /plan toggle + plan-gate REPL dispatch (SLASH-07)`.
- Tests do NOT yet pass — Task 2 updates them. Task 1 is intentionally a "build green,
  tests red" milestone.
  </done>
</task>

<task type="auto">
  <name>Task 2: Update existing tests for renderStatus 4-param signature + future-stub count delta + renderHelp marker delta; add new RenderingTests testCase for planModeActive=true display</name>
  <files>tests/BlueCode.Tests/RenderingTests.fs, tests/BlueCode.Tests/ReplTests.fs</files>
  <action>
After Task 1 commits, the build is green but tests will FAIL on the old 3-arg
renderStatus call sites (RenderingTests.fs lines 132, 145, 154, 174 — 4 sites) and on
the future-stub assertion (ReplTests.fs line 656) and on the renderHelp marker
assertion (RenderingTests.fs line 106). This task fixes all of them and adds 1 new
RenderingTests testCase covering the new planModeActive=true display path.

1. Edit `tests/BlueCode.Tests/RenderingTests.fs`. FOUR modifications:

(a) Update the existing `renderStatus shows session id, model name, step count, chars,
context %` test (line 126-137). The renderStatus call gains a 4th arg `false`. Add a
single assertion that no "plan-mode" line is present (planModeActive=false hides it).

LOCATE (RenderingTests.fs lines 126-137):

```fsharp
          testCase "renderStatus shows session id, model name, step count, chars, context %" <| fun _ ->
              let session : Session =
                  { Id = SessionId "deadbeef0123456789abcdef01234567"
                    Steps = []
                    CreatedAt = DateTimeOffset.MinValue
                    LastActivityAt = DateTimeOffset.MinValue }
              let s = renderStatus session (Some Qwen122B) 8192
              Expect.stringContains s "deadbeef0123456789abcdef01234567" "session id present"
              Expect.stringContains s "122b" "model name 122b present"
              Expect.stringContains s "steps:    0" "step count zero"
              Expect.stringContains s "0%" "context % is 0 with empty session"
              Expect.stringContains s "[floor; probed on first LLM call]" "floor disclaimer present"
```

REPLACE WITH:

```fsharp
          testCase "renderStatus shows session id, model name, step count, chars, context %" <| fun _ ->
              let session : Session =
                  { Id = SessionId "deadbeef0123456789abcdef01234567"
                    Steps = []
                    CreatedAt = DateTimeOffset.MinValue
                    LastActivityAt = DateTimeOffset.MinValue }
              let s = renderStatus session (Some Qwen122B) 8192 false   // Phase 33: planModeActive=false (default)
              Expect.stringContains s "deadbeef0123456789abcdef01234567" "session id present"
              Expect.stringContains s "122b" "model name 122b present"
              Expect.stringContains s "steps:    0" "step count zero"
              Expect.stringContains s "0%" "context % is 0 with empty session"
              Expect.stringContains s "[floor; probed on first LLM call]" "floor disclaimer present"
              Expect.isFalse (s.Contains("plan-mode")) "no plan-mode line when planModeActive=false"
```

(b) Update the `renderStatus model name: None -> '122b (default)'` test (line 139-146):

LOCATE line 145 `let s = renderStatus session None 8192`:

REPLACE:
```fsharp
              let s = renderStatus session None 8192 false
```

(c) Update the `renderStatus model name: Some Qwen35B -> '35b'` test (line 148-156):

LOCATE line 154 `let s = renderStatus session (Some Qwen35B) 8192`:

REPLACE:
```fsharp
              let s = renderStatus session (Some Qwen35B) 8192 false
```

(d) Update the `renderStatus reflects accumulated step count and chars` test
(line 158-178):

LOCATE line 174 `let s = renderStatus session (Some Qwen122B) 8192`:

REPLACE:
```fsharp
              let s = renderStatus session (Some Qwen122B) 8192 false
```

(e) Update the `renderHelp marks future commands as [coming in v2.5]` test (line 93-116).
After Phase 33-01, only `/edit` retains the marker. The test must assert exactly 1
occurrence and explicitly verify `/plan` no longer carries the marker.

LOCATE (RenderingTests.fs lines 93-116):

```fsharp
          testCase "renderHelp marks future commands as [coming in v2.5] (Phase 32-02: 2 stubs remaining — /plan + /edit)" <| fun _ ->
              let h = renderHelp
              // After Phase 32-02, /sessions and /resume are live. Only /plan and /edit
              // retain the [coming in v2.5] marker. Phase 33 will reduce this to 1; Phase 34 to 0.
              let occurrences =
                  let mutable count = 0
                  let mutable i = 0
                  while i >= 0 do
                      i <- h.IndexOf("[coming in v2.5]", i)
                      if i >= 0 then
                          count <- count + 1
                          i <- i + "[coming in v2.5]".Length
                  count
              Expect.equal occurrences 2 "exactly 2 [coming in v2.5] markers (/plan + /edit)"
              // Confirm the live commands no longer carry the marker — find the line for each.
              let lines = h.Split([| '\n' |])
              let sessionsLine = lines |> Array.find (fun l -> l.TrimStart().StartsWith("/sessions"))
              let resumeLine   = lines |> Array.find (fun l -> l.TrimStart().StartsWith("/resume"))
              let planLine     = lines |> Array.find (fun l -> l.TrimStart().StartsWith("/plan"))
              let editLine     = lines |> Array.find (fun l -> l.TrimStart().StartsWith("/edit"))
              Expect.isFalse (sessionsLine.Contains("[coming in v2.5]")) "/sessions has no [coming in v2.5]"
              Expect.isFalse (resumeLine.Contains("[coming in v2.5]"))   "/resume has no [coming in v2.5]"
              Expect.isTrue  (planLine.Contains("[coming in v2.5]"))     "/plan still has [coming in v2.5]"
              Expect.isTrue  (editLine.Contains("[coming in v2.5]"))     "/edit still has [coming in v2.5]"
```

REPLACE WITH:

```fsharp
          testCase "renderHelp marks future commands as [coming in v2.5] (Phase 33: 1 stub remaining — /edit only)" <| fun _ ->
              let h = renderHelp
              // After Phase 33, /plan is live (toggle). Only /edit retains the
              // [coming in v2.5] marker. Phase 34 will reduce this to 0.
              let occurrences =
                  let mutable count = 0
                  let mutable i = 0
                  while i >= 0 do
                      i <- h.IndexOf("[coming in v2.5]", i)
                      if i >= 0 then
                          count <- count + 1
                          i <- i + "[coming in v2.5]".Length
                  count
              Expect.equal occurrences 1 "exactly 1 [coming in v2.5] marker (/edit only)"
              // Confirm the live commands no longer carry the marker — find the line for each.
              let lines = h.Split([| '\n' |])
              let sessionsLine = lines |> Array.find (fun l -> l.TrimStart().StartsWith("/sessions"))
              let resumeLine   = lines |> Array.find (fun l -> l.TrimStart().StartsWith("/resume"))
              let planLine     = lines |> Array.find (fun l -> l.TrimStart().StartsWith("/plan"))
              let editLine     = lines |> Array.find (fun l -> l.TrimStart().StartsWith("/edit"))
              Expect.isFalse (sessionsLine.Contains("[coming in v2.5]")) "/sessions has no [coming in v2.5]"
              Expect.isFalse (resumeLine.Contains("[coming in v2.5]"))   "/resume has no [coming in v2.5]"
              Expect.isFalse (planLine.Contains("[coming in v2.5]"))     "/plan no longer has [coming in v2.5] (Phase 33 promoted to live)"
              Expect.isTrue  (editLine.Contains("[coming in v2.5]"))     "/edit still has [coming in v2.5]"
              // Live /plan line carries the new toggle description (regression fence).
              Expect.isTrue  (planLine.Contains("toggle plan-mode on/off")) "/plan line has live toggle description"
```

(f) ADD a new testCase covering the planModeActive=true display path. Insert it
IMMEDIATELY AFTER the existing `renderStatus reflects accumulated step count and chars`
test (after line 178 in the original file; account for the offset added by edits b/c/d
above which add `false` args inline — the test ordering is unchanged). The new test
asserts that with planModeActive=true, the output contains "plan-mode: on (next prompt
uses plan-gate)".

INSERT immediately AFTER the `renderStatus reflects accumulated step count and chars`
test's closing `Expect.isFalse (s.Contains("chars:    0 ")) "non-zero chars for non-empty steps"`
line, BEFORE the existing `// ── Phase 32-01: renderSessions ──...` divider on line 180:

```fsharp
          testCase "renderStatus shows 'plan-mode: on' line when planModeActive=true (Phase 33)" <| fun _ ->
              let session : Session =
                  { Id = SessionId "abc"
                    Steps = []
                    CreatedAt = DateTimeOffset.MinValue
                    LastActivityAt = DateTimeOffset.MinValue }
              let sOff = renderStatus session (Some Qwen122B) 8192 false
              let sOn  = renderStatus session (Some Qwen122B) 8192 true
              Expect.isFalse (sOff.Contains("plan-mode")) "planModeActive=false hides plan-mode line"
              Expect.stringContains sOn "plan-mode: on (next prompt uses plan-gate)"
                  "planModeActive=true appends plan-mode line with descriptive suffix"
              // Other fields unchanged regardless of toggle state.
              Expect.stringContains sOn "session:" "session label still present"
              Expect.stringContains sOn "steps:" "steps label still present"
```

DO NOT add any other tests to RenderingTests.fs in this task (plan-mode toggle behavior
tests live in Plan 33-02's ReplTests additions).

2. Edit `tests/BlueCode.Tests/ReplTests.fs`. ONE modification: update the existing
future-stub test (currently lines 617-660) so it expects exactly 1 'not yet implemented'
line (only `/edit` remains stubbed); update the test name and stdin script.

LOCATE (ReplTests.fs lines 617-660 — the entire `runMultiTurn: remaining future-stub
commands (/plan /edit) print 'not yet implemented' without crashing` testCase):

```fsharp
          testCase "runMultiTurn: remaining future-stub commands (/plan /edit) print 'not yet implemented' without crashing" <| fun () ->
              // Phase 32-02 update: /sessions and /resume are now live (handled by dedicated tests below).
              // Only /plan (Phase 33) and /edit (Phase 34) remain stubbed.
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/plan\n/edit\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-stub-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []   // future stubs must not call LLM
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "exit code 0 — remaining future-stub commands do not crash REPL"
                  // Exactly 2 'not yet implemented' lines expected (one per stub: /plan + /edit).
                  let stubLines =
                      captured.Split([| '\n' |])
                      |> Array.filter (fun l -> l.Contains("not yet implemented"))
                  Expect.equal stubLines.Length 2
                      (sprintf "expected exactly 2 'not yet implemented' lines (/plan + /edit); captured:\n%s" captured)
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)
```

REPLACE WITH:

```fsharp
          testCase "runMultiTurn: remaining future-stub command (/edit only) prints 'not yet implemented' without crashing" <| fun () ->
              // Phase 33 update: /plan is now live (toggle handler — tested separately in Plan 33-02).
              // Only /edit (Phase 34) remains stubbed.
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/edit\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-stub-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []   // future stub must not call LLM
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = None }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "exit code 0 — remaining future-stub does not crash REPL"
                  // Exactly 1 'not yet implemented' line expected (only /edit).
                  let stubLines =
                      captured.Split([| '\n' |])
                      |> Array.filter (fun l -> l.Contains("not yet implemented"))
                  Expect.equal stubLines.Length 1
                      (sprintf "expected exactly 1 'not yet implemented' line (/edit only); captured:\n%s" captured)
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)
```

DO NOT add any new testCases to ReplTests.fs in this task (plan-mode toggle/turn
behavior tests live in Plan 33-02).

3. Build and run all tests:

```
dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj
dotnet build tests/BlueCode.Tests/BlueCode.Tests.fsproj
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
```

Expect ALL tests passing. Test count delta from this task: +1 RenderingTests
(planModeActive=true display test); 5 existing tests modified (4 renderStatus
3-arg call sites + 1 marker test + 1 ReplTests future-stub test). Net change: +1
test. (Plan 33-02 will add the new ReplTests behavior tests.)

The /status existing test at ReplTests.fs lines 488-528 needs no test-code change —
its assertions still hold (session label, model label, "steps:    0", "122b", "[floor;
probed on first LLM call]"). The test goes through `runMultiTurn` which calls the
production Repl.fs `Slash Status` arm — the production code was updated in Task 1 to
pass `planModeActive` (initially false) so the test's assertions pass without
modification. **Verify this empirically:** run the suite after the edits above, the
/status test must still pass without test code changes.

4. Commit atomically (TWO files, ONE commit — both changes adapt the existing test
suite to the Task 1 signature/semantics changes):

```
git add tests/BlueCode.Tests/RenderingTests.fs tests/BlueCode.Tests/ReplTests.fs
git commit -m "test(33-01): adapt existing tests to renderStatus 4-arg + future-stub delta"
```

DO NOT use `git add -A` / `git add .`.
DO NOT add new ReplTests testCases (Plan 33-02 owns those).
DO NOT modify any other test in either file.
  </action>
  <verify>
- `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.
- `dotnet build tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0.
- `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0; ALL tests
  pass; total test count = pre-Phase-33 baseline + 1 (the new RenderingTests
  planModeActive=true testCase).
- `grep -c "renderStatus session.*8192 false" tests/BlueCode.Tests/RenderingTests.fs`
  returns 4 (the four pre-existing renderStatus tests now pass `false`).
- `grep -c "renderStatus session.*8192 true" tests/BlueCode.Tests/RenderingTests.fs`
  returns 1 (the new planModeActive=true testCase).
- `grep -c "exactly 1 \[coming in v2.5\]" tests/BlueCode.Tests/RenderingTests.fs`
  returns 1.
- `grep -c "Phase 33 promoted to live" tests/BlueCode.Tests/RenderingTests.fs`
  returns 1.
- `grep -c "remaining future-stub command (/edit only)" tests/BlueCode.Tests/ReplTests.fs`
  returns 1.
- `grep -c "/plan\\\\n/edit\\\\n/exit\\\\n" tests/BlueCode.Tests/ReplTests.fs` returns 0
  (the old 2-stub stdin script is REMOVED).
- `grep -cE "StringReader\(\"/edit\\\\n/exit\\\\n\"\)" tests/BlueCode.Tests/ReplTests.fs`
  returns 1 (the new 1-stub stdin script).
- `git log -1 --oneline` contains `test(33-01)` + `adapt existing tests`.
- `git diff master -- src/BlueCode.Core/` is empty (Core untouched).
  </verify>
  <done>
- All 4 pre-existing renderStatus tests in RenderingTests.fs updated to pass the new
  4th `planModeActive` arg as `false`.
- 1 new RenderingTests testCase added covering the planModeActive=true display path
  (asserts "plan-mode: on (next prompt uses plan-gate)" appears).
- The renderHelp marker test refined to expect exactly 1 occurrence (only /edit)
  with explicit per-line presence/absence assertions.
- 1 ReplTests future-stub test renamed and shrunk to expect exactly 1 'not yet
  implemented' line (/edit only).
- Existing /status ReplTests test passes without modification (its assertions are
  insensitive to the new line which is only added when planModeActive=true).
- All tests pass; full suite green; Core/ untouched; no warnings.
- Atomic commit `test(33-01): adapt existing tests to renderStatus 4-arg + future-stub delta`.
  </done>
</task>

</tasks>

<verification>
After both tasks complete, run these plan-level gates:

1. **Build gate (Cli + Tests):**
   - `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.
   - `dotnet build tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0.
   - No warnings from Repl.fs / Rendering.fs / RenderingTests.fs / ReplTests.fs.

2. **Full test suite:**
   - `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0; all
     tests pass.
   - Total count = pre-Phase-33 baseline + 1 (one new RenderingTests testCase added in
     Task 2).

3. **Core purity:** `git diff master -- src/BlueCode.Core/` is empty.

4. **No-async (Cli + Core):** `bash scripts/check-no-async.sh` exits 0.

5. **Smoke test (manual or scripted):**
   ```
   echo -e "/help\n/plan\n/status\n/plan\n/exit" | dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj
   ```
   Expected stdout includes:
   - 9-command help list (now showing /plan as live, /edit as `[coming in v2.5]`)
   - `[plan mode on] — next prompt will enter plan-gate before execution`
   - `/status` output containing `plan-mode: on (next prompt uses plan-gate)`
   - `[plan mode off] — returning to direct agent-loop`
   - exits with code 0 (no LLM calls — all are in-process slash commands)

6. **Atomic commit count:** `git log --oneline master..HEAD` shows exactly 2 commits
   with `(33-01)` scope: `feat(33-01): wire /plan toggle + plan-gate REPL dispatch
   (SLASH-07)` + `test(33-01): adapt existing tests to renderStatus 4-arg +
   future-stub delta`. No `git add -A` violations.

7. **Bench gate is NOT run in this plan** — Plan 33-02 Task 3 owns the bench
   verification. Plan 33-01 only ships the source code + test adaptations.
</verification>

<success_criteria>
This plan succeeds when:

- [ ] **SC-1 (planModeActive cell + toggle):** `runMultiTurnWithSession` has
  `let mutable planModeActive = false`. `Slash Plan` arm flips it via `planModeActive
  <- not planModeActive` and prints `[plan mode on]` / `[plan mode off]` via printfn.
  Notification text is NEVER injected into the LLM message list (Role=System
  invariant — SC-6 of ROADMAP).
- [ ] **SC-2 (plan-gate Prompt arm):** `Some (Prompt prompt) when planModeActive ->`
  arm exists ABOVE the unguarded Prompt arm; calls `BlueCode.Core.AgentLoop.runPlanTurn`
  with `CompositionRoot.planSystemPromptSuffix`; renders plan via
  `BlueCode.Cli.PlanGate.render`; reads decision via
  `BlueCode.Cli.PlanGate.promptUser BlueCode.Cli.PlanGate.realKeyReader`; handles
  Accept (planModeActive←false → execute via runSingleTurn → save → lastCode 130-map),
  Reject (rejectCount++, [PLAN REJECTED] prefix), Edit (rejectCount++, [PLAN EDIT NOTE]
  prefix), Quit (planModeActive←false, return to REPL — NEVER `running <- false`).
  rejectCount-exhaustion (>= 3) prints "abandoning" and disables plan-mode.
- [ ] **SC-3 (renderStatus 4-arg):** `renderStatus` signature has 4th param
  `planModeActive: bool`. Output appends `\nplan-mode: on (next prompt uses
  plan-gate)` ONLY when `planModeActive=true`. The `Slash Status` arm in Repl.fs
  passes `planModeActive` as the 4th arg. All 4 existing RenderingTests call sites
  updated to pass `false`.
- [ ] **SC-4 (renderHelp delta):** `renderHelp` `/plan` line shows `toggle plan-mode
  on/off; next prompt uses plan-gate when on` (no `[coming in v2.5]`); `/edit` line
  unchanged (still has marker). Marker count drops from 2 to 1.
- [ ] **SC-5 (test suite green):** Build clean (no warnings); full test suite passes;
  net delta +1 test (the new RenderingTests planModeActive=true testCase). Existing
  `/status` ReplTests test passes without modification.
- [ ] **SC-6 (Core purity):** `git diff master -- src/BlueCode.Core/` is empty;
  `bash scripts/check-no-async.sh` exits 0.
- [ ] **SC-7 (atomic commits):** Exactly 2 commits with `(33-01)` scope (`feat` +
  `test`); no `git add -A` / `git add .` used; per-file staging only.
- [ ] **SC-8 (open-question resolutions adopted):** After Accept+Execute,
  planModeActive auto-disables (one-shot semantics — Open Question #1). After Quit,
  planModeActive auto-disables (Open Question #2). Notification on `/plan` is
  immediate via printfn (Open Question #3). All three resolutions verified by code
  inspection (Plan 33-02 verifies them empirically with integration tests).
</success_criteria>

<output>
After completion, create `.planning/phases/33-slash-plan-toggle/33-01-SUMMARY.md`
documenting:

- Production LOC added (~85 in Repl.fs new code + ~5 in Rendering.fs renderStatus
  signature + ~1 in renderHelp = ~91)
- Test LOC added (~15 in RenderingTests.fs new testCase + ~6 inline `false` args
  across 4 existing tests + ~5 marker-test refinement + ~10 ReplTests future-stub
  rename/shrink = ~36)
- Test count delta (e.g., 345 → 346; +1 net)
- Smoke test result (capture echo-piped output as evidence — see Verification §5)
- Frontmatter to include:
  - `requires: [31-02, 32-02]` (depends on slash dispatcher infrastructure +
    /resume in-place rebind pattern)
  - `affects: [33-02]` (Plan 33-02 consumes the new code via integration tests +
    bench gate)
  - `subsystem: cli-repl`
  - `tech_stack_added: []` (no new NuGet)
  - `phase_progress: "33-01 of 33-02 plans complete"`
- Confirm Phase 33 success criteria 1, 2, 3, 4, 6 (from ROADMAP.md) all observable
  via code inspection (success criterion 5 — bench gate — is gated by Plan 33-02):
  1. planModeActive: bool added to REPL state; /plan toggles; /status displays ✓
  2. planModeActive=true → next prompt routes to plan-gate ✓
  3. plan-mode 중 /plan 다시 입력 시 off (toggle is symmetric) ✓
  4. mid-turn /plan invalid (architectural; ReadLine blocks) — N/A guard ✓
  6. Role=System invariant preserved (printfn only, never to LLM) ✓
- Note any deviations (expected: none — research is HIGH confidence).
- Plan 33-01 status: ready for Plan 33-02 (behavior tests + bench gate).
</output>
