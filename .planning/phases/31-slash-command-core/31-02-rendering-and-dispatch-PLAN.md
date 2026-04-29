---
phase: 31-slash-command-core
plan: 02
type: execute
wave: 2
depends_on:
  - 31-01
files_modified:
  - src/BlueCode.Cli/Rendering.fs
  - src/BlueCode.Cli/Repl.fs
  - tests/BlueCode.Tests/RenderingTests.fs
  - tests/BlueCode.Tests/ReplTests.fs
autonomous: true

must_haves:
  truths:
    - "User typing /help in REPL prints the 9-command list to stdout; LLM stub receives 0 calls"
    - "User typing /status prints session id, model name, step count, accumulated chars, context %"
    - "User typing /clear creates a new SessionId, sets currentSession.Steps = [], prints confirmation; old session jsonl is untouched"
    - "User typing /exit or /quit exits the REPL loop with exit code 0; existing session save semantics preserved"
    - "User typing /sessions, /resume, /plan, /edit prints '(not yet implemented — coming in a future v2.5 phase)' without crashing"
    - "Bench gate (bash bench/run.sh --gate) shows 7/7 PASS — slash command additions cause zero regression on agent-loop / plan-mode invocations"
  artifacts:
    - path: "src/BlueCode.Cli/Rendering.fs"
      provides: "renderHelp string + renderStatus function"
      contains: "let renderHelp"
      contains_2: "let renderStatus"
    - path: "src/BlueCode.Cli/Repl.fs"
      provides: "Slash command dispatcher integrated into runMultiTurnWithSession"
      contains: "SlashCommand.parse"
      removes: "literal \"/exit\" string match (replaced with DU pattern match)"
    - path: "tests/BlueCode.Tests/RenderingTests.fs"
      provides: "Unit tests for renderHelp/renderStatus output (string-only, no Console.SetOut)"
    - path: "tests/BlueCode.Tests/ReplTests.fs"
      provides: "Integration tests for /help, /status, /clear, /quit dispatch (Console.SetIn/SetOut, already testSequenced)"
  key_links:
    - from: "src/BlueCode.Cli/Repl.fs"
      to: "src/BlueCode.Cli/SlashCommand.fs"
      via: "open BlueCode.Cli.SlashCommand + match SlashCommand.parse line with"
      pattern: "SlashCommand\\.parse"
    - from: "src/BlueCode.Cli/Repl.fs"
      to: "src/BlueCode.Cli/Rendering.fs"
      via: "Rendering.renderHelp + Rendering.renderStatus calls in dispatcher"
      pattern: "Rendering\\.(renderHelp|renderStatus)"
    - from: "src/BlueCode.Cli/Repl.fs"
      to: "src/BlueCode.Cli/Adapters/FileSessionStore.fs"
      via: "FileSessionStore.newSessionId () call inside /clear arm"
      pattern: "FileSessionStore\\.newSessionId"
---

<objective>
Phase 31 — Plan 02: Wire the slash command parser (from Plan 31-01) into `Repl.runMultiTurnWithSession`,
add `renderHelp` and `renderStatus` rendering functions, and add integration tests for all four
in-process commands. Verify bench gate 7/7 PASS preserved.

Purpose: Replace the existing literal `"/exit" -> running <- false` match in `Repl.fs` (line 185)
with a structured dispatcher driven by `SlashCommand.parse`. Future phases (32-35) need only add
new arms to the `Slash _ ->` match — they do NOT modify the parser, the rendering functions, or
the loop structure. After this plan, all Phase 31 success criteria are observable end-to-end.

The `/status` command surfaces v1.1's `MaxModelLen` floor (8192) — research § Q3 explicitly
labels this as the floor (not the live probed value) because awaiting the `Lazy<Task<ModelInfo>>`
probe inside a meta-control command could block for 300s on cold-start. This is by design;
the label "[floor; probed on first LLM call]" makes it user-visible.

Output:
- `Rendering.renderHelp : string` (constant 9-command help text)
- `Rendering.renderStatus : Session -> AppComponents -> string` (composes session id + model + steps + chars + context %)
- Modified `Repl.runMultiTurnWithSession` with `SlashCommand.parse` dispatcher (replacing the literal `"/exit"` arm)
- ~5 integration tests in `ReplTests.fs` covering /help, /status, /clear, /quit and the future-stub message
- ~3 rendering string tests in `RenderingTests.fs`
- Bench gate verified (7/7 PASS preserved)
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/STATE.md
@.planning/phases/31-slash-command-core/31-RESEARCH.md
@.planning/phases/31-slash-command-core/31-01-parser-PLAN.md
@CLAUDE.md
@src/BlueCode.Cli/Repl.fs
@src/BlueCode.Cli/Rendering.fs
@src/BlueCode.Cli/CompositionRoot.fs
@src/BlueCode.Cli/Adapters/FileSessionStore.fs
@tests/BlueCode.Tests/ReplTests.fs
@tests/BlueCode.Tests/RenderingTests.fs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add renderHelp and renderStatus to Rendering.fs + unit tests in RenderingTests.fs</name>
  <files>src/BlueCode.Cli/Rendering.fs, tests/BlueCode.Tests/RenderingTests.fs</files>
  <action>
1. Edit `src/BlueCode.Cli/Rendering.fs`. APPEND the following two functions at the end of the
file (after the existing `renderError` function on line 121). Do NOT modify any existing code
in this file — `renderStep`, `renderResult`, `renderError` and their helpers must remain
byte-identical.

```fsharp
/// 9-command help text shown by `/help`. Static string, no parameters.
/// Includes both `/exit` and `/quit` as separate entries (counted separately
/// per Phase 31 success criterion 1: "9 commands list — 7 in-milestone + future-stub").
/// Uses `printfn`-friendly plain text (NOT Spectre markup) so that tests capturing
/// Console.SetOut see the exact string. CLAUDE.md "Stream separation" + research § Pitfall 1.
let renderHelp : string =
    """slash commands:
  /help              show this help
  /status            session info: id, model, steps, context %
  /clear             reset session in-place (new session id, keep REPL running)
  /exit              save session and quit
  /quit              alias for /exit
  /sessions          list recent sessions [coming in v2.5]
  /resume <id>       switch to a saved session [coming in v2.5]
  /plan              toggle plan-mode for next turn [coming in v2.5]
  /edit              open $EDITOR for multi-line input [coming in v2.5]"""

/// Render the `/status` output. Pure: takes the current Session and AppComponents,
/// returns a multi-line string. NO Spectre markup (CLAUDE.md "Stream separation";
/// `/status` output is captured by Console.SetOut in tests).
///
/// Fields per Phase 31 success criterion 2:
///   - session id (32-char hex from SessionId)
///   - model name ("122b" / "35b" / "122b (default)")
///   - step count (currentSession.Steps.Length — accumulated across all turns in this session)
///   - accumulated char count (sum of "%A" Action + "%A" ToolResult per step — same heuristic as runSingleTurn)
///   - 32k context % (estimatedChars * 100 / (MaxModelLen * 4))
///
/// `MaxModelLen` is the v1.1 startup FLOOR (8192) — the real per-port value lives in the
/// QwenHttpClient lazy probe and is not surfaced to AppComponents. Awaiting that probe
/// here would block /status for up to 300s on cold-start. Label the output to make this
/// explicit (research § Q3, Pitfall 2 of research § Anti-Patterns).
///
/// Spectre escape note: model names in this codebase never contain '[', so no `[[...]]`
/// escape is needed in the printfn-rendered output. If a future phase adds a model name
/// like "[[blue]]", revisit research § Pitfall 1.
let renderStatus (session: Session) (components: BlueCode.Cli.CompositionRoot.AppComponents) : string =
    let (SessionId idStr) = session.Id
    let modelName =
        match components.Config.ForcedModel with
        | Some Qwen122B -> "122b"
        | Some Qwen35B  -> "35b"
        | None          -> "122b (default)"
    let steps = session.Steps.Length
    let accChars =
        session.Steps
        |> List.sumBy (fun s ->
            (sprintf "%A" s.Action).Length + (sprintf "%A" s.ToolResult).Length)
    let maxChars = components.MaxModelLen * 4   // tokens * ~4 chars/token
    let pct = if maxChars > 0 then accChars * 100 / maxChars else 0
    sprintf
        "session:  %s\nmodel:    %s\nsteps:    %d\nchars:    %d / ~%d (%d%%) [floor; probed on first LLM call]"
        idStr modelName steps accChars maxChars pct
```

NOTE: `renderStatus` references `BlueCode.Cli.CompositionRoot.AppComponents`. Currently
`Rendering.fs` does NOT depend on `CompositionRoot.fs` (Rendering is at compile position 9,
CompositionRoot at 10). This forward reference would create a circular dependency.

**RESOLUTION:** Move `renderStatus` from `Rendering.fs` into a NEW module file is one option,
but simpler: since the existing Rendering.fs already imports `BlueCode.Core.Domain` (line 4),
we keep the bare types — `Session`, `AppComponents` — handled by accepting the needed fields
as primitives. Refactor `renderStatus` signature to:

```fsharp
let renderStatus
    (session: Session)
    (forcedModel: Model option)
    (maxModelLen: int)
    : string =
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
    sprintf
        "session:  %s\nmodel:    %s\nsteps:    %d\nchars:    %d / ~%d (%d%%) [floor; probed on first LLM call]"
        idStr modelName steps accChars maxChars pct
```

This keeps `Rendering.fs` free of `CompositionRoot` references. The Repl.fs caller does the
field extraction at the call site: `Rendering.renderStatus currentSession components.Config.ForcedModel components.MaxModelLen`.

USE THIS FINAL SIGNATURE (the one taking `Model option` and `int`, NOT `AppComponents`).

2. Edit `tests/BlueCode.Tests/RenderingTests.fs`. APPEND new test cases inside the existing
`testList "Rendering"` block (the file already uses `[<Tests>]` legacy attribute AND is
registered in `rootTests` via `BlueCode.Tests.RenderingTests.tests` — both registrations
exist; do not change them). Add these tests:

```fsharp
          // ── Phase 31-02: renderHelp + renderStatus ───────────────────────────
          testCase "renderHelp lists all 9 commands" <| fun _ ->
              let h = renderHelp
              Expect.stringContains h "/help" "must list /help"
              Expect.stringContains h "/status" "must list /status"
              Expect.stringContains h "/clear" "must list /clear"
              Expect.stringContains h "/exit" "must list /exit"
              Expect.stringContains h "/quit" "must list /quit"
              Expect.stringContains h "/sessions" "must list /sessions"
              Expect.stringContains h "/resume" "must list /resume"
              Expect.stringContains h "/plan" "must list /plan"
              Expect.stringContains h "/edit" "must list /edit"

          testCase "renderHelp marks future commands as [coming in v2.5]" <| fun _ ->
              let h = renderHelp
              Expect.stringContains h "[coming in v2.5]" "future commands flagged"

          testCase "renderHelp does NOT call LLM (it's a constant string)" <| fun _ ->
              // This test exists primarily to document the contract:
              // renderHelp is a string constant — no IO, no allocation per call.
              // If the implementation grows side effects, this test will need to change.
              let h1 = renderHelp
              let h2 = renderHelp
              Expect.equal h1 h2 "renderHelp is referentially transparent"

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

          testCase "renderStatus model name: None -> '122b (default)'" <| fun _ ->
              let session : Session =
                  { Id = SessionId "abc"
                    Steps = []
                    CreatedAt = DateTimeOffset.MinValue
                    LastActivityAt = DateTimeOffset.MinValue }
              let s = renderStatus session None 8192
              Expect.stringContains s "122b (default)" "None ForcedModel renders default label"

          testCase "renderStatus model name: Some Qwen35B -> '35b'" <| fun _ ->
              let session : Session =
                  { Id = SessionId "abc"
                    Steps = []
                    CreatedAt = DateTimeOffset.MinValue
                    LastActivityAt = DateTimeOffset.MinValue }
              let s = renderStatus session (Some Qwen35B) 8192
              Expect.stringContains s "35b" "Qwen35B renders as 35b"
              Expect.isFalse (s.Contains("122b")) "no spurious 122b token in 35b status"

          testCase "renderStatus reflects accumulated step count and chars" <| fun _ ->
              let step : Step =
                  { StepNumber = 1
                    Thought = Thought "x"
                    Action = ToolCall(ToolName "list_dir", ToolInput(Map.ofList [ ("_raw", "{\"path\":\".\"}") ]))
                    ToolResult = Some (Success "stub")
                    Status = StepSuccess
                    ModelUsed = Qwen122B
                    StartedAt = DateTimeOffset.MinValue
                    EndedAt = DateTimeOffset.MinValue
                    DurationMs = 1L }
              let session : Session =
                  { Id = SessionId "abc"
                    Steps = [ step; step; step ]
                    CreatedAt = DateTimeOffset.MinValue
                    LastActivityAt = DateTimeOffset.MinValue }
              let s = renderStatus session (Some Qwen122B) 8192
              Expect.stringContains s "steps:    3" "step count reflects List.length"
              // chars: each step contributes (sprintf "%A" Action) + (sprintf "%A" ToolResult)
              // Don't assert exact char count — just that it's > 0 (formula is testable in isolation).
              Expect.isFalse (s.Contains("chars:    0 ")) "non-zero chars for non-empty steps"
```

These tests do NOT touch `Console.SetOut` (renderHelp/renderStatus return strings, so callers
do the printing). They don't need `testSequenced`. The existing `testList` is NOT wrapped with
`testSequenced` — leave it alone; pure-string tests are safe in parallel.

3. Build and run only the Rendering testList to fail-fast:
   ```
   dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj -- --filter Rendering
   ```
   Expect all (existing 5 + new 7 = 12) tests passing.

4. Commit atomically:
   ```
   git add src/BlueCode.Cli/Rendering.fs tests/BlueCode.Tests/RenderingTests.fs
   git commit -m "feat(31-02): add renderHelp and renderStatus rendering functions"
   ```

DO NOT use `AnsiConsole.MarkupLine` anywhere in `renderHelp`/`renderStatus` — research §
Pitfall 1 + CLAUDE.md "Stream separation": Spectre bypasses `Console.SetOut`, breaking tests.
Use plain `printfn` at the call site (Repl.fs), and have these functions return `string`.

DO NOT modify any existing function in `Rendering.fs` — only append new ones.

DO NOT add `BlueCode.Cli.CompositionRoot` reference to `Rendering.fs` — that creates a circular
compile-order dependency. The renderStatus signature takes primitives (`Model option`, `int`).
  </action>
  <verify>
- `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.
- `grep -c "let renderHelp" src/BlueCode.Cli/Rendering.fs` returns 1.
- `grep -c "let renderStatus" src/BlueCode.Cli/Rendering.fs` returns 1.
- `grep -c "AnsiConsole" src/BlueCode.Cli/Rendering.fs` returns 0 (no Spectre markup in this file — confirmed pre-Phase 31; must remain 0 post-Phase 31).
- `grep -c "BlueCode.Cli.CompositionRoot" src/BlueCode.Cli/Rendering.fs` returns 0 (no circular dependency).
- `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj -- --filter Rendering` exits 0; output shows ≥12 tests passing under "Rendering".
- `git log -1 --oneline` contains `feat(31-02)` + `renderHelp`.
  </verify>
  <done>
- `renderHelp : string` is a constant containing all 9 command lines + `[coming in v2.5]` markers on the 4 future-stub commands.
- `renderStatus : Session -> Model option -> int -> string` returns a 4-line string with session id, model, steps, chars/context%.
- 7 new RenderingTests pass; 5 existing tests still pass.
- No Spectre markup in either function.
- No circular dependency (Rendering.fs does NOT import CompositionRoot).
- Atomic commit `feat(31-02): add renderHelp and renderStatus rendering functions`.
  </done>
</task>

<task type="auto">
  <name>Task 2: Integrate SlashCommand.parse dispatcher into Repl.runMultiTurnWithSession + add 5 ReplTests</name>
  <files>src/BlueCode.Cli/Repl.fs, tests/BlueCode.Tests/ReplTests.fs</files>
  <action>
1. Edit `src/BlueCode.Cli/Repl.fs`. Two changes:

(a) Add `open BlueCode.Cli.SlashCommand` to the open block at the top of the file. The current
opens (lines 3-12) are:
```fsharp
open System
open System.Threading
open System.Threading.Tasks
open Serilog
open Spectre.Console
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Core.AgentLoop
open BlueCode.Cli.Rendering
open BlueCode.Cli.CompositionRoot
```

Add ONE new line after `open BlueCode.Cli.Rendering`:
```fsharp
open BlueCode.Cli.SlashCommand
```

(b) Replace the existing `match line with ...` block in `runMultiTurnWithSession` (lines 183-203
of `Repl.fs` — exactly the block from `match line with` through `lastCode <- if code = 130 then 0 else code`).
The CURRENT block reads:

```fsharp
            match line with
            | null -> running <- false
            | "/exit" -> running <- false
            | s when s.Trim() = "" -> ()
            | prompt ->
                let! (code, newSteps) =
                    runSingleTurn prompt currentSession.Steps components renderMode
                // Always update Session.Steps with newSteps (even on failure — partial progress is informative).
                let updated =
                    { currentSession with
                        Steps = currentSession.Steps @ newSteps
                        LastActivityAt = DateTimeOffset.UtcNow }
                currentSession <- updated
                // Save AFTER each turn (whether success or error) so a crash mid-session is recoverable.
                let! saveRes = sessionStore.Save updated CancellationToken.None
                match saveRes with
                | Ok () -> ()
                | Error e ->
                    Log.Warning("Session save failed: {Error}", sprintf "%A" e)
                    eprintfn "WARNING: session save failed: %A" e
                lastCode <- if code = 130 then 0 else code
```

REPLACE WITH:

```fsharp
            match line with
            | null -> running <- false
            | _ ->
                match SlashCommand.parse line with
                | None ->
                    // blank / whitespace-only line — skip silently (preserves prior behavior)
                    ()
                | Some (Slash Exit) ->
                    // /exit and /quit both map here. Auto-save semantic is preserved by
                    // the existing per-turn Save in the Prompt branch — last completed turn
                    // is already on disk. No flush needed (research § Q5).
                    running <- false
                | Some (Slash Help) ->
                    printfn "%s" Rendering.renderHelp
                | Some (Slash Status) ->
                    printfn "%s" (Rendering.renderStatus currentSession components.Config.ForcedModel components.MaxModelLen)
                | Some (Slash Clear) ->
                    // /clear: new session id, empty Steps, NEW jsonl created lazily on first
                    // future Save. Old session jsonl stays untouched (FileSessionStore.Save
                    // creates files lazily — see research § Q4). priorSteps reset is automatic
                    // because runSingleTurn reads currentSession.Steps every call.
                    let newId = BlueCode.Cli.Adapters.FileSessionStore.newSessionId ()
                    let now = DateTimeOffset.UtcNow
                    currentSession <-
                        { Id = newId; Steps = []; CreatedAt = now; LastActivityAt = now }
                    let (SessionId newIdStr) = newId
                    printfn "Session cleared. New session: %s" newIdStr
                | Some (Slash (Sessions | Resume _ | Plan | Edit)) ->
                    // Phase 32 (Sessions, Resume), Phase 33 (Plan), Phase 34 (Edit) stubs.
                    // Future-proofing: parser already accepts these so user input does not
                    // crash; dispatcher prints the future-phase notice. Each future phase
                    // replaces its arm here with a real handler.
                    printfn "(not yet implemented — coming in a future v2.5 phase)"
                | Some (Prompt prompt) ->
                    let! (code, newSteps) =
                        runSingleTurn prompt currentSession.Steps components renderMode
                    // Always update Session.Steps with newSteps (even on failure — partial progress is informative).
                    let updated =
                        { currentSession with
                            Steps = currentSession.Steps @ newSteps
                            LastActivityAt = DateTimeOffset.UtcNow }
                    currentSession <- updated
                    // Save AFTER each turn (whether success or error) so a crash mid-session is recoverable.
                    let! saveRes = sessionStore.Save updated CancellationToken.None
                    match saveRes with
                    | Ok () -> ()
                    | Error e ->
                        Log.Warning("Session save failed: {Error}", sprintf "%A" e)
                        eprintfn "WARNING: session save failed: %A" e
                    lastCode <- if code = 130 then 0 else code
```

CRITICAL preservation requirements:
- The `null -> running <- false` arm MUST stay (Ctrl+D / EOF behavior).
- The `Prompt` arm MUST contain the EXACT same body as the previous `prompt` arm — same
  `runSingleTurn` call, same currentSession update, same `sessionStore.Save`, same `lastCode`
  computation. Only the surrounding match structure changes.
- `Environment.Exit` is NEVER called (research § Q5 + research § Anti-Patterns: bypasses
  Serilog flush).
- `sessionStore.Save` is NOT called on `/clear` (research § Q4: empty new session has nothing
  to persist; first Save fires lazily on the next completed Prompt turn).

DO NOT touch `runSingleTurn`, `shouldWarnContextWindow`, or any other function in `Repl.fs` —
only `runMultiTurnWithSession`'s match block changes.

DO NOT wrap any new printfn in `AnsiConsole.MarkupLine` — research § Pitfall 1 + CLAUDE.md.

DO NOT introduce any `task {}` outside the existing `task {}` block (the dispatcher arms run
inside the existing `while running do` loop; the only `let!` is in the Prompt arm, unchanged
from before).

DO NOT add `async {}` — Repl.fs is in Cli (Core purity ban does not apply here), but the file
already uses `task {}` everywhere; consistency requires no change.

2. Edit `tests/BlueCode.Tests/ReplTests.fs`. The file is already wrapped in `testSequenced`
(line 43) — every test inside MAY use `Console.SetIn` / `Console.SetOut`. Add 5 new testCases
inside the existing `testList "Repl"` block, AFTER the existing `runMultiTurn: stdin '/exit' exits cleanly...`
test (line 119) and BEFORE the closing `]` of the testList. Each test follows the same pattern
as the existing `/exit` test (Console.SetIn for input, Console.SetOut for output capture):

```fsharp
          testCase "runMultiTurn: '/help' prints 9-command help without LLM call" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/help\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-help-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []   // 0 LLM calls expected — /help is in-process
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
                  Expect.equal exitCode 0 "exit code 0"
                  Expect.stringContains captured "/help" "help text mentions /help"
                  Expect.stringContains captured "/sessions" "help lists /sessions stub"
                  Expect.stringContains captured "[coming in v2.5]" "help marks future commands"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/status' prints session id, model, steps, chars" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/status\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-stat-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []
                    ToolExecutor = stubToolsOk
                    SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                    JsonlSink = sink
                    Config =
                      { MaxLoops = 5; ContextCapacity = 3; SystemPrompt = "test"; ForcedModel = Some Qwen122B }
                    ProjectRoot = tempRoot
                    LogPath = sinkPath
                    MaxModelLen = 8192 }

              try
                  let exitCode =
                      BlueCode.Cli.Repl.runMultiTurn components Compact
                      |> fun t -> t.GetAwaiter().GetResult()
                  Console.Out.Flush()
                  let captured = stdoutWriter.ToString()
                  Expect.equal exitCode 0 "exit code 0"
                  Expect.stringContains captured "session:" "status shows session label"
                  Expect.stringContains captured "model:" "status shows model label"
                  Expect.stringContains captured "steps:    0" "fresh session has 0 steps"
                  Expect.stringContains captured "122b" "model name printed"
                  Expect.stringContains captured "[floor; probed on first LLM call]" "MaxModelLen floor disclaimer"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/clear' creates new session id, prints confirmation, leaves old jsonl untouched" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/clear\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-clr-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []
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
                  Expect.equal exitCode 0 "exit code 0"
                  Expect.stringContains captured "Session cleared" "clear confirmation text"
                  Expect.stringContains captured "New session:" "new id label"
                  // The captured stdout contains TWO session ids: the banner's initial id, then the post-clear id.
                  // Assert that the second occurrence differs from the first (id rotation actually happened).
                  let lines = captured.Split([| '\n' |])
                  let bannerSessionLines = lines |> Array.filter (fun l -> l.Contains("Session: ") && not (l.Contains("New session")))
                  let clearSessionLines  = lines |> Array.filter (fun l -> l.Contains("New session:"))
                  Expect.isGreaterThan bannerSessionLines.Length 0 "banner session line present"
                  Expect.isGreaterThan clearSessionLines.Length 0 "post-clear session line present"
                  // Pull the IDs out and compare
                  let bannerId =
                      bannerSessionLines.[0].Substring(bannerSessionLines.[0].IndexOf("Session:") + "Session:".Length).Trim()
                  let clearId =
                      clearSessionLines.[0].Substring(clearSessionLines.[0].IndexOf("New session:") + "New session:".Length).Trim()
                  Expect.notEqual bannerId clearId "session id rotated by /clear"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/quit' exits cleanly with code 0 (alias of /exit)" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/quit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-quit-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []
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
                  Expect.equal exitCode 0 "/quit must exit with code 0 (graceful, alias of /exit)"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: future-stub commands (/sessions /resume /plan /edit) print 'not yet implemented' without crashing" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/sessions\n/resume xyz\n/plan\n/edit\n/exit\n")
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
                  Expect.equal exitCode 0 "exit code 0 — future-stub commands do not crash REPL"
                  // "not yet implemented" should appear at least 4 times (one per stub command)
                  let stubLines =
                      captured.Split([| '\n' |])
                      |> Array.filter (fun l -> l.Contains("not yet implemented"))
                  Expect.isGreaterThanOrEqual stubLines.Length 4
                      (sprintf "expected ≥4 'not yet implemented' lines (one per stub); captured:\n%s" captured)
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)
```

Place these BEFORE the `] // end testSequenced` closing on line 448.

NOTE on stubLlm with empty queue: the existing helper `stubLlm []` (top of ReplTests.fs)
returns a client that throws on first call. Tests that expect 0 LLM calls (all 5 above) use
this — if a slash command incorrectly routes to the LLM, the test fails fast with
"queue exhausted — test bug".

NOTE on testSequenced: the existing testList wrapper (line 43) already provides serialization;
new tests inside it inherit testSequenced semantics automatically. Do NOT add a nested
testSequenced.

3. Build and run only the Repl testList:
   ```
   dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj -- --filter Repl
   ```
   Expect (existing 6 + new 5 = 11) tests passing.

4. Then run the FULL suite to catch any cross-file regression:
   ```
   dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
   ```
   Expect ALL tests passing; total count = pre-Phase-31 baseline + Phase 31-01 (17) + Phase 31-02 RenderingTests (7) + Phase 31-02 ReplTests (5) = baseline + 29.

5. Commit atomically:
   ```
   git add src/BlueCode.Cli/Repl.fs tests/BlueCode.Tests/ReplTests.fs
   git commit -m "feat(31-02): integrate slash command dispatcher into Repl"
   ```

DO NOT use `git add -A` / `git add .`.
  </action>
  <verify>
- `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0 with no warnings.
- `grep -c "open BlueCode.Cli.SlashCommand" src/BlueCode.Cli/Repl.fs` returns 1.
- `grep -c "SlashCommand.parse" src/BlueCode.Cli/Repl.fs` returns 1 (the dispatcher entry point).
- `grep -c "\"/exit\" -> running <- false" src/BlueCode.Cli/Repl.fs` returns 0 (literal "/exit" string match REMOVED — replaced by `Slash Exit` arm).
- `grep -c "Environment.Exit" src/BlueCode.Cli/Repl.fs` returns 0 (no abrupt exits introduced).
- `grep -c "Rendering.renderHelp" src/BlueCode.Cli/Repl.fs` returns 1.
- `grep -c "Rendering.renderStatus" src/BlueCode.Cli/Repl.fs` returns 1.
- `grep -c "FileSessionStore.newSessionId" src/BlueCode.Cli/Repl.fs` returns ≥2 (one in /clear arm + one in legacy `runMultiTurn` factory at line 222).
- `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj -- --filter Repl` exits 0; output contains all 11 Repl tests passing (6 existing + 5 new).
- Full suite: `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0; total test count = pre-Phase-31 baseline + 29 (17 from 31-01 + 12 from 31-02; 7 Rendering + 5 Repl).
- `git diff master -- src/BlueCode.Core/` is empty (Core untouched).
- `git log --oneline -2` shows two `(31-02)` commits.
  </verify>
  <done>
- `Repl.runMultiTurnWithSession` dispatches via `SlashCommand.parse`; literal "/exit" string match removed.
- All 4 in-process commands (/help, /status, /clear, /exit + /quit alias) produce expected stdout output and behavior.
- 4 future-stub commands (/sessions, /resume, /plan, /edit) print "not yet implemented" without crashing or routing to LLM.
- 5 new `ReplTests` integration tests pass + 6 existing pass (full Repl testList green).
- Full test suite (all modules) green with +29 tests vs. pre-Phase-31 baseline.
- No file under `src/BlueCode.Core/**` modified.
- Atomic commit `feat(31-02): integrate slash command dispatcher into Repl`.
  </done>
</task>

<task type="auto">
  <name>Task 3: Verify bench gate 7/7 PASS preserved (Phase 31 success criterion 5)</name>
  <files>(no source files modified — verification only)</files>
  <action>
This task is the regression gate for Phase 31 success criterion 5: "Bench gate 7/7 PASS
preserved (REPL 은 bench 영역 밖이지만 변경 회귀 방지)". It does NOT modify any source.

1. Pre-flight: ensure the 122B service is loaded and warm:
   ```
   curl -fsS http://127.0.0.1:8001/v1/models > /dev/null
   ```
   If this fails, the service is not running. Hand back to user with: "122B mlx_lm.server
   not responding on port 8001. Run: `launchctl kickstart -k gui/501/com.ohama.qwen122b`
   then retry this task." Do NOT proceed with bench until 122B is reachable.

2. Build the binary (bench/run.sh executes `dotnet run -c Release` against the published Cli):
   ```
   dotnet build -c Release src/BlueCode.Cli/BlueCode.Cli.fsproj
   ```
   Expect exit 0.

3. Run the bench gate:
   ```
   bash /Users/ohama/projs/blueCode/bench/run.sh --gate
   ```

   This runs the 6-invocation regression subset (~2 min) covering T6_122b, W1_122b, W2_122b,
   T1_122b, T5_122b, B2_122b. The gate compares against `bench/baseline.json` and exits non-zero
   on regression.

   Note: research mentions "bench gate 7/7" but the gate harness has 6 entries in baseline
   (commit `bench/baseline.json` at HEAD); STATE.md describes "bench gate 7/7 PASS" as the
   structural authority — this counts the gate's 6 fixtures plus the MT (multi-turn) fixture
   that has been added. Confirm the actual count by inspecting `bench/baseline.json` and
   asserting the gate runs ALL of them. If the gate output shows "6/6 PASS" or "7/7 PASS",
   either meets the success criterion (whichever is the current authority).

4. Verify gate output:
   - Last line MUST contain "PASS" (e.g., `[run.sh] gate result: 6/6 PASS` or `7/7 PASS`).
   - Exit code MUST be 0.
   - No fixture should have status "REGRESSED" or "FAIL".

5. If gate FAILS (regression detected):
   - Do NOT modify `bench/baseline.json` — that is the structural authority.
   - Do NOT modify the bench fixtures.
   - The slash command dispatcher addition should NOT regress agent-loop behavior. If it does,
     this signals a bug in Plan 31-02 Task 2 — most likely an accidental modification of the
     `Prompt` arm body. Re-read `git diff` for `src/BlueCode.Cli/Repl.fs` and confirm the
     `Prompt` branch body is byte-identical to the prior `prompt ->` body (only wrapped in a
     new match arm).
   - If the diff is clean and bench still regresses, this is a real regression — investigate
     and stop the plan execution.

6. Once gate is green, no commit is needed (no source modified by this task). The bench logs
   land in `bench/runs/<timestamp>/` (gitignored).

DO NOT skip this task even if Tasks 1 and 2 passed unit tests. Bench is a different gate
(real-network, real-LLM, structural). Phase 31 success criterion 5 explicitly requires this
verification.
  </action>
  <verify>
- `curl -fsS http://127.0.0.1:8001/v1/models` exits 0.
- `dotnet build -c Release src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.
- `bash bench/run.sh --gate` exits 0 with output containing `PASS` (either `6/6 PASS` or `7/7 PASS`).
- `git status` shows no unstaged modifications under `src/` after this task.
  </verify>
  <done>
- Bench gate runs to completion with all fixtures PASS (matching pre-Phase-31 baseline).
- Phase 31 success criterion 5 ("Bench gate 7/7 PASS preserved") satisfied.
- No source code changes from this task; no commit needed.
  </done>
</task>

</tasks>

<verification>
After all 3 tasks complete, run these final phase-level gates:

1. **Build gate (release):** `dotnet build -c Release src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.

2. **Full test suite:** `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0; all tests pass; total count = pre-Phase-31 baseline + 29 (17 parser + 7 rendering + 5 repl integration).

3. **Bench gate:** `bash bench/run.sh --gate` exits 0 with PASS verdict (Task 3 covers this; do not re-run if just completed).

4. **Core purity:** `git diff master -- src/BlueCode.Core/` is empty.

5. **No-async (Cli + Core):** `bash scripts/check-no-async.sh` exits 0.

6. **End-to-end smoke (manual or scripted):**
   ```
   echo "/help\n/status\n/clear\n/exit" | dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj
   ```
   Expected stdout includes:
   - the 9-command help list
   - the status block with session/model/steps/chars/context %
   - "Session cleared. New session: <hex>"
   - exits with code 0 (no LLM calls — all four are in-process slash commands)

7. **Future-proof check:** All four future-stub commands print the placeholder without crashing
   (covered by Task 2 test "future-stub commands (/sessions /resume /plan /edit) print 'not yet
   implemented' without crashing").

8. **Atomic commit count:** `git log --oneline master..HEAD` shows exactly 4 commits with `(31-`
   scope: `feat(31-01)`, `test(31-01)`, `feat(31-02)` (Rendering), `feat(31-02)` (Repl). No
   amends, no `git add -A`.
</verification>

<success_criteria>
This plan succeeds, and Phase 31 is complete, when:

- [ ] **SC-1 (/help):** Typing `/help` in REPL prints the 9-command list (Help/Status/Clear/Exit/Quit + 4 future stubs marked `[coming in v2.5]`); LLM stub receives 0 calls.
- [ ] **SC-2 (/status):** Typing `/status` prints session id (32-char hex), model name (`122b` / `35b` / `122b (default)`), step count (currentSession.Steps.Length), accumulated chars, and context % calculated from `MaxModelLen * 4`. The output is labeled `[floor; probed on first LLM call]` to clarify the v1.1 floor semantics.
- [ ] **SC-3 (/clear):** Typing `/clear` rotates `currentSession.Id` to a new GUID-N hex string, sets `currentSession.Steps = []`, prints "Session cleared. New session: <id>". Old session jsonl at `~/.bluecode/sessions/<old-id>.jsonl` is unmodified (verified by `FileSessionStore.Save` lazy-create semantics — no Save call on /clear). New session jsonl is created lazily on first future Prompt-driven turn save.
- [ ] **SC-4 (/exit, /quit):** Typing either `/exit` or `/quit` exits the REPL loop with exit code 0. The last completed-turn save is already on disk (auto-save preserved from existing per-turn `sessionStore.Save` in the Prompt arm).
- [ ] **SC-5 (bench gate):** `bash bench/run.sh --gate` exits 0 with PASS verdict. Slash command additions do not regress any of the 6-7 baseline fixtures.
- [ ] **SC-6 (artifacts):** `Cli/SlashCommand.fs` (Plan 31-01), `Cli/Rendering.fs` updated with `renderHelp` + `renderStatus`, `Cli/Repl.fs` updated with `SlashCommand.parse` dispatcher integration. All three files committed.
- [ ] No file under `src/BlueCode.Core/**` modified (CLAUDE.md Core purity invariant preserved).
- [ ] No new NuGet package added (`grep -c "PackageReference" src/BlueCode.Cli/BlueCode.Cli.fsproj` unchanged from before Phase 31).
- [ ] Future-proofing: all four future commands (`/sessions /resume /plan /edit`) parse cleanly and print placeholder without crashing — Phases 32-35 add only dispatcher arms, not parser changes.
- [ ] 4 atomic commits exist with `(31-01)` and `(31-02)` scopes, all staged file-by-file (no `git add -A` violations).
- [ ] Test count: 287 pre-Phase-31 → ~316 post-Phase-31 (+29 = 17 parser + 7 rendering + 5 integration).
</success_criteria>

<output>
After completion, create `.planning/phases/31-slash-command-core/31-02-SUMMARY.md` documenting:

- Production LOC added (~30 in Rendering.fs renderHelp/renderStatus + ~30 in Repl.fs dispatcher = ~60)
- Test LOC added (~80 in RenderingTests + ~250 in ReplTests = ~330)
- Test count delta (e.g., 304 -> 316)
- Bench gate result (e.g., "6/6 PASS — no regression")
- Frontmatter to include:
  - `requires: [31-01]` (depends on SlashCommand types)
  - `affects: [32, 33, 34, 35]` (downstream phases reuse the dispatcher infrastructure)
  - `tech_stack_added: []` (no new NuGet)
- Confirm Phase 31 success criteria 1-6 all observable from end-to-end smoke test.
- Note any deviations (expected: none — research is HIGH confidence).
- Phase 31 status: ready for `/gsd:verify-work 31` UAT.
</output>
