---
phase: 32-slash-session-commands
plan: 02
type: execute
wave: 2
depends_on:
  - 32-01
files_modified:
  - src/BlueCode.Cli/Repl.fs
  - src/BlueCode.Cli/Rendering.fs
  - tests/BlueCode.Tests/ReplTests.fs
autonomous: true

must_haves:
  truths:
    - "User typing /sessions in REPL prints listRecent's output via renderSessions; LLM stub receives 0 calls; REPL keeps running"
    - "User typing /resume <known-id> swaps currentSession to the loaded one (priorSteps reload visible to next prompt's LLM); REPL keeps running"
    - "User typing /resume (no arg) prints 'usage: /resume <session-id>' without calling sessionStore.Load"
    - "User typing /resume <unknown-id> prints 'Session not found: <id>' (SessionNotFound friendly message); REPL keeps running"
    - "User typing /resume on a corrupt session prints 'Session file corrupt: ...' (SessionCorrupt friendly message); REPL keeps running"
    - "renderHelp no longer marks /sessions or /resume as '[coming in v2.5]' — both have new short descriptions"
    - "Existing future-stub test now expects exactly 2 'not yet implemented' lines (Plan + Edit only) instead of 4"
    - "Bench gate (bash bench/run.sh --gate) shows 7/7 PASS — slash command additions cause zero regression on agent-loop / plan-mode invocations"
  artifacts:
    - path: "src/BlueCode.Cli/Repl.fs"
      provides: "Dispatcher arms for Slash Sessions + Slash (Resume id) replacing the future-stub branch"
      contains: "Slash Sessions"
      contains_2: "Slash (Resume id)"
      contains_3: "sessionStore.Load (SessionId id)"
    - path: "src/BlueCode.Cli/Rendering.fs"
      provides: "renderHelp updated to show /sessions and /resume as live commands (no '[coming in v2.5]' markers on those two lines)"
    - path: "tests/BlueCode.Tests/ReplTests.fs"
      provides: "≥5 new integration tests for /sessions and /resume; existing future-stub test updated to expect 2 stubs"
  key_links:
    - from: "src/BlueCode.Cli/Repl.fs"
      to: "src/BlueCode.Cli/Adapters/FileSessionStore.fs"
      via: "BlueCode.Cli.Adapters.FileSessionStore.listRecent 10 call inside /sessions arm"
      pattern: "FileSessionStore\\.listRecent"
    - from: "src/BlueCode.Cli/Repl.fs"
      to: "src/BlueCode.Cli/Rendering.fs"
      via: "Rendering.renderSessions metas call inside /sessions arm"
      pattern: "Rendering\\.renderSessions"
    - from: "src/BlueCode.Cli/Repl.fs"
      to: "src/BlueCode.Core/Ports.fs (ISessionStore.Load)"
      via: "sessionStore.Load (SessionId id) CancellationToken.None inside /resume arm"
      pattern: "sessionStore\\.Load"
    - from: "/resume happy path"
      to: "currentSession mutable rebind"
      via: "currentSession <- loaded on Ok; visible to next Prompt's runSingleTurn priorSteps argument"
      pattern: "currentSession <- loaded"
---

<objective>
Phase 32 — Plan 02: Wire `/sessions` and `/resume <id>` real handlers into
`Repl.runMultiTurnWithSession`, update `renderHelp` to drop the `[coming in v2.5]` markers
on those two commands, and add integration tests covering the empty/known/unknown/corrupt
arg paths. Verify bench gate 7/7 PASS preserved.

Purpose: Replace the existing future-stub arm
`| Some (Slash (Sessions | Resume _ | Plan | Edit)) -> printfn "(not yet implemented...)"` in
Repl.fs with two real handlers (Sessions + Resume) plus a slimmer remaining stub for the
still-future Plan + Edit. This is the same shape Phase 31-02 used (replace literal `"/exit"`
match with structured DU dispatch); future phases (33 + 34) follow the same pattern.

Plan 32-01 has already shipped `SessionMeta`, `listRecent`, and `renderSessions`. This plan
imports them and consumes them. The interface contract is therefore stable when this plan
starts execution.

Roadmap success criterion 1: `/sessions` prints recent N (default 10) with id, started_at,
turns, first prompt 첫 80자 — implemented via `FileSessionStore.listRecent 10` →
`Rendering.renderSessions metas` (Plan 32-01 caps excerpt at 80 chars; renderSessions
displays the first 40 chars + "..." for narrow column).

Roadmap success criterion 2: `/resume <id>` unknown → friendly error, current session
preserved; known → in-place switch (currentSession mutable rebind, priorSteps reload).
Implemented via `sessionStore.Load (SessionId id) ct` + match on `Result<Session, AgentError>`.

Roadmap success criterion 3: corrupt jsonl → SessionCorrupt friendly error, REPL does NOT
exit. Already handled by existing `Load`'s defensive catch (FileSessionStore.fs lines 142-145
return `Error (SessionCorrupt ...)`); the `/resume` arm matches that error case and prints
via `renderError` reuse OR a dedicated printfn (chosen below).

Output:
- `Repl.runMultiTurnWithSession` dispatcher gets two new arms (Sessions, Resume) + slimmed
  future-stub arm (Plan | Edit only)
- `renderHelp` updated: `/sessions` and `/resume <id>` lines drop `[coming in v2.5]`,
  showing live one-line descriptions instead
- 5 new integration tests in `ReplTests.fs` (testSequenced wrapper)
- 1 existing test updated: future-stub test now expects 2 'not yet implemented' lines (Plan + Edit)
- 1 existing test updated: renderHelp '[coming in v2.5]' assertion adjusted (still present
  for /plan and /edit; no longer present for /sessions and /resume specifically)
- Bench gate verified 7/7 PASS preserved
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/STATE.md
@.planning/phases/32-slash-session-commands/32-RESEARCH.md
@.planning/phases/32-slash-session-commands/32-01-data-and-rendering-PLAN.md
@CLAUDE.md
@src/BlueCode.Cli/Repl.fs
@src/BlueCode.Cli/Rendering.fs
@src/BlueCode.Cli/SlashCommand.fs
@src/BlueCode.Cli/Adapters/FileSessionStore.fs
@src/BlueCode.Core/Domain.fs
@src/BlueCode.Core/Ports.fs
@tests/BlueCode.Tests/ReplTests.fs
@tests/BlueCode.Tests/RenderingTests.fs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Wire /sessions and /resume dispatcher arms into Repl.fs + update renderHelp</name>
  <files>src/BlueCode.Cli/Repl.fs, src/BlueCode.Cli/Rendering.fs</files>
  <action>
1. Edit `src/BlueCode.Cli/Repl.fs`. ONE structural change to the dispatcher in
`runMultiTurnWithSession` — replace the existing future-stub arm (currently lines 211-216)
with three new arms: `Sessions`, `Resume ""`, `Resume id` (non-empty), and a slimmed
remaining stub for Plan + Edit only.

CURRENT block (lines 211-216 — to be REPLACED):

```fsharp
                | Some (Slash (Sessions | Resume _ | Plan | Edit)) ->
                    // Phase 32 (Sessions, Resume), Phase 33 (Plan), Phase 34 (Edit) stubs.
                    // Future-proofing: parser already accepts these so user input does not
                    // crash; dispatcher prints the future-phase notice. Each future phase
                    // replaces its arm here with a real handler.
                    printfn "(not yet implemented — coming in a future v2.5 phase)"
```

REPLACE WITH (insert these arms in this exact order, BEFORE the `| Some (Prompt prompt) ->`
arm at line 217):

```fsharp
                | Some (Slash Sessions) ->
                    // Phase 32 (SLASH-05): list the 10 most-recent sessions on disk.
                    // listRecent returns SessionMeta list (sorted mtime-desc, capped at 10);
                    // renderSessions formats it as a multi-line plain string.
                    // No LLM call (in-process meta-control).
                    let metas = BlueCode.Cli.Adapters.FileSessionStore.listRecent 10
                    printfn "%s" (Rendering.renderSessions metas)
                | Some (Slash (Resume "")) ->
                    // Phase 32 (SLASH-06): empty-arg guard. The parser produces
                    // `Resume ""` when the user typed `/resume` alone. Match this
                    // case BEFORE the general `Resume id` so we don't call
                    // sessionStore.Load with an empty SessionId (research § Pitfall 4).
                    printfn "usage: /resume <session-id>"
                | Some (Slash (Resume id)) ->
                    // Phase 32 (SLASH-06): in-place session switch.
                    // sessionStore.Load returns Result<Session, AgentError>:
                    //   Ok loaded             → currentSession <- loaded; print confirmation
                    //   Error SessionNotFound → friendly "Session not found: <id>" message
                    //   Error SessionCorrupt  → friendly "Session file corrupt: <detail>" message
                    //   Error other           → defensive fallback (research § Q6: any other
                    //                           AgentError shouldn't reach here from Load,
                    //                           but match `_` for total compile-time coverage)
                    // REPL stays alive on every error variant (roadmap SC-3).
                    let! loadResult = sessionStore.Load (SessionId id) CancellationToken.None
                    match loadResult with
                    | Ok loaded ->
                        currentSession <- loaded
                        let (SessionId newIdStr) = loaded.Id
                        printfn "Resumed session: %s (%d steps)" newIdStr loaded.Steps.Length
                    | Error (SessionNotFound _) ->
                        printfn "Session not found: %s" id
                    | Error (SessionCorrupt detail) ->
                        printfn "Session file corrupt: %s" detail
                    | Error other ->
                        // Defensive — Load doesn't return other variants in current
                        // FileSessionStore impl (lines 142-145 catch all to SessionCorrupt),
                        // but ISessionStore is an interface and a future store could.
                        printfn "Resume failed: %A" other
                | Some (Slash (Plan | Edit)) ->
                    // Phase 33 (Plan) and Phase 34 (Edit) future-stubs.
                    // Sessions and Resume have moved to real handlers above.
                    printfn "(not yet implemented — coming in a future v2.5 phase)"
```

CRITICAL preservation requirements:
- The `| null -> running <- false` arm MUST stay (Ctrl+D / EOF behavior — Repl.fs line 185).
- The `Prompt` arm body MUST remain byte-identical to its current state (Repl.fs lines 217-233).
  Only the surrounding match structure changes — adding new arms above the Prompt arm.
- All new arms must use `printfn` (not `AnsiConsole.MarkupLine`) — CLAUDE.md "Stream separation"
  + research § Pitfall 1 (Spectre bypasses Console.SetOut, breaks tests).
- The new `Resume id` arm uses `let!` for the Task return — this is allowed because the
  enclosing block is already inside the `task {}` CE on line 171.
- Do NOT call `sessionStore.Save` inside `/resume` — the loaded session is already on disk;
  saving on resume would write a duplicate envelope. The existing per-Prompt-turn Save
  (line 227 of current Repl.fs, now nested inside the `Prompt` arm) handles persistence
  starting on the next user prompt.
- The `currentSession <- loaded` rebind is the ONLY mutation needed for in-place switch.
  `priorSteps` is not a separate variable; the next iteration's `runSingleTurn` reads
  `currentSession.Steps` directly — research § Q4 confirms this.
- `Environment.Exit` is NEVER called.
- Do NOT add `async {}` (project uses `task {}` exclusively in this file).

2. Edit `src/BlueCode.Cli/Rendering.fs`. Update the `renderHelp` string constant (lines
128-138) to remove the `[coming in v2.5]` markers from `/sessions` and `/resume` and replace
them with live one-line descriptions. The other two future-stub commands (`/plan`, `/edit`)
keep their `[coming in v2.5]` markers.

CURRENT renderHelp (Rendering.fs lines 128-138):

```fsharp
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
  /plan              toggle plan-mode for next turn [coming in v2.5]
  /edit              open $EDITOR for multi-line input [coming in v2.5]"""
```

Only TWO lines change: `/sessions` and `/resume <id>` lose `[coming in v2.5]` and gain
real descriptions. The character count of renderHelp is irrelevant for any invariant —
it's a UI string, not a system prompt.

The string still contains EXACTLY TWO `[coming in v2.5]` substrings (now on `/plan` and
`/edit` only). The existing test `"renderHelp marks future commands as [coming in v2.5]"`
(RenderingTests.fs line 92) still passes because it only asserts the marker EXISTS, not
how many times.

DO NOT change `renderStatus` or any other function in Rendering.fs.

DO NOT add new `open` directives to Rendering.fs in this plan — Plan 32-01 already added
`open BlueCode.Cli.Adapters.FileSessionStore`.

3. Build the Cli project to verify compilation:
   ```
   dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj
   ```
   Expect exit 0, no warnings.

4. Commit atomically (TWO files, ONE commit — both changes wire the same feature live):
   ```
   git add src/BlueCode.Cli/Repl.fs src/BlueCode.Cli/Rendering.fs
   git commit -m "feat(32-02): wire /sessions and /resume dispatcher arms"
   ```

DO NOT use `git add -A` / `git add .`.
DO NOT touch `runSingleTurn`, `shouldWarnContextWindow`, or any function other than
`runMultiTurnWithSession` in Repl.fs.
DO NOT touch any function other than `renderHelp` in Rendering.fs.
  </action>
  <verify>
- `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0 with no warnings.
- `grep -c "Slash Sessions" src/BlueCode.Cli/Repl.fs` returns 1 (the new arm).
- `grep -c "Resume \"\"" src/BlueCode.Cli/Repl.fs` returns 1 (the empty-arg guard).
- `grep -c "Resume id" src/BlueCode.Cli/Repl.fs` returns 1 (the non-empty arm — note: regex `Resume id` would match the comment too, so use `grep -c "Slash (Resume id))" src/BlueCode.Cli/Repl.fs` for stricter count: 1).
- `grep -c "FileSessionStore.listRecent" src/BlueCode.Cli/Repl.fs` returns 1.
- `grep -c "Rendering.renderSessions" src/BlueCode.Cli/Repl.fs` returns 1.
- `grep -c "sessionStore.Load (SessionId" src/BlueCode.Cli/Repl.fs` returns 1.
- `grep -c "Slash (Sessions | Resume _ | Plan | Edit)" src/BlueCode.Cli/Repl.fs` returns 0 (old combined stub arm REMOVED).
- `grep -c "Slash (Plan | Edit)" src/BlueCode.Cli/Repl.fs` returns 1 (new slimmed stub arm).
- `grep -c "\[coming in v2.5\]" src/BlueCode.Cli/Rendering.fs` returns 2 (only /plan and /edit lines retain the marker).
- `grep -c "list 10 most-recent sessions" src/BlueCode.Cli/Rendering.fs` returns 1.
- `grep -c "switch to a saved session in-place" src/BlueCode.Cli/Rendering.fs` returns 1.
- `git diff master -- src/BlueCode.Core/` is empty (Core untouched).
- `git log -1 --oneline` contains `feat(32-02)` + `dispatcher`.
  </verify>
  <done>
- Three new dispatcher arms (Sessions, Resume "", Resume id) added to Repl.runMultiTurnWithSession.
- The Plan+Edit future-stub remains as a single arm (slimmed from previous 4-way combined stub).
- renderHelp shows /sessions and /resume as live commands; only /plan and /edit retain the [coming in v2.5] marker.
- No compilation warnings, no Core/ modifications.
- Atomic commit `feat(32-02): wire /sessions and /resume dispatcher arms`.
  </done>
</task>

<task type="auto">
  <name>Task 2: Add /sessions and /resume integration tests + update existing future-stub assertions</name>
  <files>tests/BlueCode.Tests/ReplTests.fs, tests/BlueCode.Tests/RenderingTests.fs</files>
  <action>
1. Edit `tests/BlueCode.Tests/ReplTests.fs`. TWO modifications:

(a) Update the existing `"future-stub commands (/sessions /resume /plan /edit) print 'not yet
implemented' without crashing"` test (line 617-658). Phase 31-02 wrote this expecting `≥4`
'not yet implemented' lines. After Phase 32-02, only `/plan` and `/edit` remain stubbed —
so the count drops to 2.

The fix is two-fold: (1) rename the test to reflect new reality, (2) update the assertion.

LOCATE the test starting at line 617:

```fsharp
          testCase "runMultiTurn: future-stub commands (/sessions /resume /plan /edit) print 'not yet implemented' without crashing" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/sessions\n/resume xyz\n/plan\n/edit\n/exit\n")
              ...
              Expect.isGreaterThanOrEqual stubLines.Length 4
                  (sprintf "expected ≥4 'not yet implemented' lines (one per stub); captured:\n%s" captured)
```

REPLACE the entire testCase with:

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

(b) ADD 5 new testCases for /sessions and /resume. Insert them BEFORE the closing `]
// end testSequenced` on line 660. Pattern matches the existing /clear /quit tests:
SetIn/SetOut, dotnet `runMultiTurn`, GetAwaiter().GetResult(), assert on captured stdout,
unwind in finally:

```fsharp
          testCase "runMultiTurn: '/sessions' lists header + zero or more rows; no LLM call" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/sessions\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-ls-%s" (Guid.NewGuid().ToString("N")))
              Directory.CreateDirectory(tempRoot) |> ignore
              let sinkPath =
                  Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
              use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

              let components: AppComponents =
                  { LlmClient = stubLlm []   // /sessions is in-process — 0 LLM calls
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
                  // Either "no sessions found" (empty dir) OR a header line + rows.
                  // The user's real ~/.bluecode/sessions/ may have files (research § Q9: 534 sessions
                  // observed); in the test environment, content varies. Assert one or the other:
                  let hasEmpty = captured.Contains("no sessions found")
                  let hasHeader = captured.Contains("session id") && captured.Contains("first thought")
                  Expect.isTrue (hasEmpty || hasHeader)
                      (sprintf "expected either 'no sessions found' or a header row; captured:\n%s" captured)
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/resume' (no arg) prints usage hint without crashing" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              use stdinReader = new StringReader("/resume\n/exit\n")
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-r0-%s" (Guid.NewGuid().ToString("N")))
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
                  Expect.equal exitCode 0 "exit code 0 — empty /resume arg does not crash"
                  Expect.stringContains captured "usage: /resume" "usage hint printed"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/resume <unknown>' prints SessionNotFound friendly error; REPL continues" <| fun () ->
              let originalIn = Console.In
              let originalOut = Console.Out
              // Use a guaranteed-unique unknown id (32-N hex prefix matches our pattern).
              let unknownId = sprintf "ghost-%s" (Guid.NewGuid().ToString("N"))
              use stdinReader = new StringReader(sprintf "/resume %s\n/exit\n" unknownId)
              use stdoutWriter = new StringWriter()
              Console.SetIn(stdinReader)
              Console.SetOut(stdoutWriter)

              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-runk-%s" (Guid.NewGuid().ToString("N")))
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
                  Expect.equal exitCode 0 "exit code 0 — unknown id does not exit REPL"
                  Expect.stringContains captured "Session not found:" "SessionNotFound friendly message printed"
                  Expect.stringContains captured unknownId "the unknown id is echoed in the error"
              finally
                  Console.SetIn(originalIn)
                  Console.SetOut(originalOut)

          testCase "runMultiTurn: '/resume <known>' swaps currentSession; subsequent prompt sees resumed steps" <| fun () ->
              // Pre-write a real session to disk via FileSessionStore.Save, then /resume it.
              // After resume, send a prompt — the LLM stub captures messages received.
              // Resumed session has 2 prior steps; the LLM should see those in priorSteps.
              let originalIn = Console.In
              let originalOut = Console.Out

              let preIdStr = sprintf "preset-%s" (Guid.NewGuid().ToString("N"))
              let preSession : Session =
                  let toolCall = ToolCall (ToolName "list_dir", ToolInput (Map.ofList [("_raw", "{\"path\":\".\"}")]))
                  let mkS n action =
                      { StepNumber = n
                        Thought = Thought (sprintf "preset thought %d" n)
                        Action = action
                        ToolResult = match action with FinalAnswer _ -> None | _ -> Some (Success "ok")
                        Status = StepSuccess
                        ModelUsed = Qwen122B
                        StartedAt = DateTimeOffset.MinValue
                        EndedAt = DateTimeOffset.MinValue
                        DurationMs = 1L }
                  { Id = SessionId preIdStr
                    Steps = [ mkS 1 toolCall; mkS 2 (FinalAnswer "preset done") ]
                    CreatedAt = DateTimeOffset.UtcNow
                    LastActivityAt = DateTimeOffset.UtcNow }
              let prePath = BlueCode.Cli.Adapters.FileSessionStore.buildSessionPath preSession.Id

              try
                  // Write the preset session to disk (cleanup in finally).
                  let preStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
                  (preStore.Save preSession CancellationToken.None).GetAwaiter().GetResult() |> ignore

                  // Capture the LLM messages so we can assert priorSteps were threaded.
                  let capturedMessages = ResizeArray<list<Message>>()
                  let capturingLlm =
                      let q = System.Collections.Generic.Queue<Result<LlmResponse, AgentError>>()
                      q.Enqueue (makeMockResponse "ok" (FinalAnswer "post-resume answer"))
                      { new ILlmClient with
                          member _.CompleteAsync messages _model _ct =
                              capturedMessages.Add messages
                              if q.Count = 0 then failwith "queue exhausted"
                              Task.FromResult(q.Dequeue()) }

                  use stdinReader = new StringReader(sprintf "/resume %s\nhello after resume\n/exit\n" preIdStr)
                  use stdoutWriter = new StringWriter()
                  Console.SetIn(stdinReader)
                  Console.SetOut(stdoutWriter)

                  let tempRoot =
                      Path.Combine(Path.GetTempPath(), sprintf "bluecode-rok-%s" (Guid.NewGuid().ToString("N")))
                  Directory.CreateDirectory(tempRoot) |> ignore
                  let sinkPath =
                      Path.Combine(tempRoot, sprintf "session_%s.jsonl" (Guid.NewGuid().ToString("N")))
                  use sink = new BlueCode.Cli.Adapters.JsonlSink.JsonlSink(sinkPath)

                  let components: AppComponents =
                      { LlmClient = capturingLlm
                        ToolExecutor = stubToolsOk
                        SessionStore = preStore
                        JsonlSink = sink
                        Config =
                          { MaxLoops = 5; ContextCapacity = 5; SystemPrompt = "test"; ForcedModel = None }
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
                      // Confirmation message visible:
                      Expect.stringContains captured "Resumed session:" "resume confirmation printed"
                      Expect.stringContains captured preIdStr "resumed session id echoed"
                      Expect.stringContains captured "(2 steps)" "step count from loaded session printed"
                      // The LLM was called once (for "hello after resume").
                      Expect.equal capturedMessages.Count 1 "LLM called exactly once after resume"
                      // The messages list should reflect the resumed session's prior steps —
                      // a non-trivial message count (system + prior turns + user prompt) > 2.
                      // We don't assert exact content (priorSteps formatting is AgentLoop's job),
                      // only that messages were threaded (count > 2 implies prior steps included).
                      let msgs = capturedMessages.[0]
                      Expect.isGreaterThan msgs.Length 2
                          (sprintf "expected >2 messages (system + prior steps + new user prompt); got %d" msgs.Length)
                  finally
                      Console.SetIn(originalIn)
                      Console.SetOut(originalOut)
              finally
                  // Cleanup the pre-written session jsonl.
                  try if File.Exists prePath then File.Delete prePath with _ -> ()

          testCase "runMultiTurn: '/resume <corrupt>' prints SessionCorrupt friendly error; REPL continues" <| fun () ->
              // Plant a corrupt session at a known path, /resume it, expect SessionCorrupt path.
              let originalIn = Console.In
              let originalOut = Console.Out
              let corruptIdStr = sprintf "corrupt-%s" (Guid.NewGuid().ToString("N"))
              let corruptPath = BlueCode.Cli.Adapters.FileSessionStore.buildSessionPath (SessionId corruptIdStr)

              try
                  // Plant garbage at the path.
                  File.WriteAllText(corruptPath, "this is not json\n{also garbage}\n")

                  use stdinReader = new StringReader(sprintf "/resume %s\n/exit\n" corruptIdStr)
                  use stdoutWriter = new StringWriter()
                  Console.SetIn(stdinReader)
                  Console.SetOut(stdoutWriter)

                  let tempRoot =
                      Path.Combine(Path.GetTempPath(), sprintf "bluecode-rcrp-%s" (Guid.NewGuid().ToString("N")))
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
                      Expect.equal exitCode 0 "exit code 0 — corrupt session does not exit REPL"
                      Expect.stringContains captured "Session file corrupt:" "SessionCorrupt friendly message printed"
                  finally
                      Console.SetIn(originalIn)
                      Console.SetOut(originalOut)
              finally
                  try if File.Exists corruptPath then File.Delete corruptPath with _ -> ()
```

Place these BEFORE the `] // end testSequenced` closing on line 660.

Notes on test hygiene:
- All 5 new tests use unique GUID-N suffix paths and clean up in `finally`.
- The "/resume known" test uses a `capturingLlm` that records every `messages` argument so
  we can assert priorSteps were threaded into the next prompt's LLM call (research § Q7).
- The corrupt test plants a garbage file, /resumes it, then cleans up. SessionCorrupt is
  produced by the existing FileSessionStore.Load defensive catch (lines 142-145 of
  FileSessionStore.fs).
- `MockHelpers.makeMockResponse` is already imported via `open BlueCode.Tests.MockHelpers`
  on line 13 of ReplTests.fs.

CRITICAL: every new test goes INSIDE the existing `testSequenced <| testList "Repl" [...]`
block — DO NOT add a nested `testSequenced`. Console.SetIn/SetOut races between tests are
prevented by the OUTER testSequenced (research § Pitfall 2 + CLAUDE.md "Console.SetOut in tests").

CRITICAL: the new tests use `BlueCode.Cli.Repl.runMultiTurn` (the legacy entry point that
internally creates a fresh Session and calls runMultiTurnWithSession — Repl.fs lines 249-257).
This matches the existing /clear, /quit, future-stub tests' invocation pattern.

2. Edit `tests/BlueCode.Tests/RenderingTests.fs`. The existing test
`"renderHelp marks future commands as [coming in v2.5]"` (line 92) currently asserts
`Expect.stringContains h "[coming in v2.5]"`. After Phase 32-02 it still passes (the marker
remains on /plan + /edit lines), but we should refine the test to assert the EXPECTED count
is 2, locking in the regression fence for Phase 33 + 34.

LOCATE the existing test (line 92-94 of RenderingTests.fs):

```fsharp
          testCase "renderHelp marks future commands as [coming in v2.5]" <| fun _ ->
              let h = renderHelp
              Expect.stringContains h "[coming in v2.5]" "future commands flagged"
```

REPLACE WITH:

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

3. Build and run all tests:
   ```
   dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj
   dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
   ```
   Expect ALL tests passing.
   Expected delta from Plan 32-01 baseline: +5 ReplTests + 0 net RenderingTests (1 modified,
   not new) = +5 tests. Cumulative Phase 32 delta: +12 (Plan 01) + +5 (Plan 02) = +17.

4. Commit atomically:
   ```
   git add tests/BlueCode.Tests/ReplTests.fs tests/BlueCode.Tests/RenderingTests.fs
   git commit -m "test(32-02): add /sessions and /resume integration tests"
   ```

DO NOT use `git add -A` / `git add .`.
  </action>
  <verify>
- `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.
- `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0; ALL tests pass.
- `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj -- --filter Repl` exits 0; output shows ≥16 Repl tests passing (11 from Phase 31 + 5 new from Phase 32-02 = 16; 1 existing test was modified, not removed, so total grows by 5).
- `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj -- --filter Rendering` exits 0; existing RenderingTests still pass (count unchanged — modified test, not new).
- `grep -c "Resumed session:" tests/BlueCode.Tests/ReplTests.fs` returns 1 (the new known-id integration test).
- `grep -c "Session not found:" tests/BlueCode.Tests/ReplTests.fs` returns 1 (the unknown-id test).
- `grep -c "Session file corrupt:" tests/BlueCode.Tests/ReplTests.fs` returns 1 (the corrupt test).
- `grep -c "usage: /resume" tests/BlueCode.Tests/ReplTests.fs` returns 1 (the empty-arg test).
- `grep -c "exactly 2 \[coming in v2.5\]" tests/BlueCode.Tests/RenderingTests.fs` returns 1.
- `git log --oneline -2` shows two `(32-02)` commits (`feat` + `test`).
  </verify>
  <done>
- 5 new ReplTests integration tests cover /sessions empty/non-empty, /resume empty/unknown/known/corrupt arg paths.
- /resume known-id test uses capturingLlm to assert priorSteps reload (msgs.Length > 2 confirms threading).
- Existing future-stub test renamed and updated to expect exactly 2 'not yet implemented' lines (/plan + /edit only).
- Existing renderHelp marker test refined to assert occurrence count = 2 + per-line presence/absence of marker.
- All tests pass; full suite green; Core/ untouched.
- Atomic commit `test(32-02): add /sessions and /resume integration tests`.
  </done>
</task>

<task type="auto">
  <name>Task 3: Verify bench gate 7/7 PASS preserved (Phase 32 success criterion 5)</name>
  <files>(no source files modified — verification only)</files>
  <action>
This task is the regression gate for Phase 32 success criterion 5 ("Bench gate 7/7 PASS
preserved"). Research § Q14 confirmed zero regression risk in theory because bench is
single-turn (Program.fs → runSingleTurn) and never enters runMultiTurnWithSession's
dispatcher. Empirical verification still required.

1. Pre-flight: ensure the 122B service is loaded and warm:
   ```
   curl -fsS http://127.0.0.1:8001/v1/models
   ```
   If this fails, the service is not running. Hand back to user with: "122B mlx_lm.server
   not responding on port 8001. Run: `launchctl kickstart -k gui/501/com.ohama.qwen122b`
   then retry this task." Do NOT proceed with bench until 122B is reachable.

2. Build the binary in Release config (bench/run.sh executes against published Cli):
   ```
   dotnet build -c Release src/BlueCode.Cli/BlueCode.Cli.fsproj
   ```
   Expect exit 0.

3. Run the bench gate:
   ```
   bash /Users/ohama/projs/blueCode/bench/run.sh --gate
   ```

   This runs the regression subset (~2 min) covering T6_122b, W1_122b, W2_122b, T1_122b,
   T5_122b, B2_122b (+ MT if present in baseline). The gate compares against
   `bench/baseline.json` and exits non-zero on any regression.

4. Verify gate output:
   - Last line MUST contain "PASS" (e.g., `[run.sh] gate result: 6/6 PASS` or `7/7 PASS`).
   - Exit code MUST be 0.
   - No fixture should have status "REGRESSED" or "FAIL".

5. If gate FAILS (regression detected):
   - Do NOT modify `bench/baseline.json` — that is the structural authority (CLAUDE.md "Bench").
   - Do NOT modify the bench fixtures.
   - The dispatcher additions in Plan 32-02 Task 1 should NOT regress agent-loop behavior
     (research § Q14: bench is single-turn; runMultiTurnWithSession is unreachable).
   - If regression observed, the most likely cause is an accidental modification of the
     `Prompt` arm body inside the dispatcher. Re-read `git diff master -- src/BlueCode.Cli/Repl.fs`
     and confirm:
     - The `Prompt` arm body (lines after `| Some (Prompt prompt) ->`) is byte-identical to
       its prior state (only the surrounding match arms changed, NOT the body).
     - `runSingleTurn` itself was not modified.
     - `shouldWarnContextWindow` was not modified.
   - If the diff is clean and bench still regresses, this is a real regression — STOP
     plan execution and report findings.

6. Once gate is green, no commit is needed (no source modified by this task). The bench logs
   land in `bench/runs/<timestamp>/` (gitignored).

DO NOT skip this task even if Tasks 1 and 2 passed unit tests. Bench is a different gate
(real-network, real-LLM, structural) that protects production behavior.
  </action>
  <verify>
- `curl -fsS http://127.0.0.1:8001/v1/models` exits 0.
- `dotnet build -c Release src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.
- `bash bench/run.sh --gate` exits 0 with output containing `PASS` (`6/6 PASS` or `7/7 PASS`).
- `git status` shows no unstaged modifications under `src/` after this task.
- `git diff master -- bench/baseline.json` is empty (baseline.json byte-equal — CLAUDE.md invariant).
  </verify>
  <done>
- Bench gate runs to completion with all fixtures PASS (matching pre-Phase-32 baseline).
- Phase 32 success criterion 5 satisfied.
- No source code changes from this task; no commit.
- `bench/baseline.json` byte-identical to pre-Phase-32 (no false regressions induced).
  </done>
</task>

</tasks>

<verification>
After all 3 tasks complete, run these final phase-level gates:

1. **Build gate (release):** `dotnet build -c Release src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.

2. **Full test suite:** `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0; all tests pass; total count = pre-Phase-32 baseline + 17 (Plan 32-01: +12 = 7 SessionStore + 5 Rendering; Plan 32-02: +5 ReplTests; existing 2 tests modified in place, not new).

3. **Bench gate:** `bash bench/run.sh --gate` exits 0 with PASS verdict (Task 3 covers this; do not re-run if just completed).

4. **Core purity:** `git diff master -- src/BlueCode.Core/` is empty.

5. **No-async (Cli + Core):** `bash scripts/check-no-async.sh` exits 0.

6. **ISessionStore frozen:** `git diff master -- src/BlueCode.Core/Ports.fs` is empty.

7. **End-to-end smoke (manual or scripted):**
   ```
   echo -e "/help\n/sessions\n/resume nonexistent\n/exit" | dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj
   ```
   Expected stdout includes:
   - the 9-command help list (now showing /sessions and /resume as live)
   - either "no sessions found" or a header + rows
   - "Session not found: nonexistent"
   - exits with code 0 (no LLM calls — all four are in-process slash commands)

8. **Atomic commit count:** `git log --oneline master..HEAD` shows exactly 4 commits with `(32-` scope: 2 from Plan 32-01 (`feat(32-01): add SessionMeta + listRecent...`, `feat(32-01): add renderSessions...`) + 2 from Plan 32-02 (`feat(32-02): wire /sessions and /resume dispatcher arms`, `test(32-02): add /sessions and /resume integration tests`). No amends, no `git add -A`.
</verification>

<success_criteria>
This plan succeeds, and Phase 32 is complete, when:

- [ ] **SC-1 (/sessions):** Typing `/sessions` in REPL prints `Rendering.renderSessions` of `FileSessionStore.listRecent 10`. Header + rows when sessions exist; "no sessions found" when directory empty/missing. LLM stub receives 0 calls.
- [ ] **SC-2 (/resume known):** Typing `/resume <known-id>` swaps `currentSession` to the loaded one. Confirmation message printed. Next prompt's LLM call sees the resumed session's prior steps in `messages` (count > 2 in capturingLlm assertion).
- [ ] **SC-3 (/resume errors):** Empty arg → "usage: /resume <session-id>" (no Load call). Unknown id → "Session not found: <id>" (SessionNotFound friendly). Corrupt session → "Session file corrupt: <detail>" (SessionCorrupt friendly). REPL stays alive in all three error paths.
- [ ] **SC-4 (renderHelp):** `/sessions` and `/resume <id>` lines have live one-line descriptions (no `[coming in v2.5]`). Only `/plan` and `/edit` retain the marker. Exact count = 2 verified by test.
- [ ] **SC-5 (bench gate):** `bash bench/run.sh --gate` exits 0 with PASS verdict. Slash command additions cause zero regression on agent-loop / plan-mode invocations.
- [ ] **SC-6 (artifacts):** `Cli/Repl.fs` updated with three new arms (Sessions, Resume "", Resume id) + slimmed Plan|Edit stub. `Cli/Rendering.fs` updated `renderHelp` (2 markers retained, not 4). `Cli/Adapters/FileSessionStore.fs` consumed via `BlueCode.Cli.Adapters.FileSessionStore.listRecent` call.
- [ ] No file under `src/BlueCode.Core/**` modified (CLAUDE.md Core purity invariant preserved).
- [ ] `ISessionStore` interface in `src/BlueCode.Core/Ports.fs` byte-identical to pre-Phase-32 (Save + Load only — `listRecent` is a Cli-layer module function, NOT a member).
- [ ] No new NuGet package added (`grep -c "PackageReference" src/BlueCode.Cli/BlueCode.Cli.fsproj` unchanged from before Phase 32).
- [ ] `bench/baseline.json` byte-identical (CLAUDE.md invariant).
- [ ] Test count delta: +5 (Plan 32-02) on top of Plan 32-01's +12 = +17 cumulative for Phase 32. 2 existing tests modified in place (future-stub assertion + renderHelp marker assertion).
- [ ] 2 atomic commits exist with `(32-02)` scope (`feat` + `test`), all staged file-by-file (no `git add -A` violations).
- [ ] Future-proofing: `/plan` and `/edit` still parse cleanly and print placeholder without crashing — Phases 33-34 add only dispatcher arms, not parser changes.
</success_criteria>

<output>
After completion, create `.planning/phases/32-slash-session-commands/32-02-SUMMARY.md` documenting:

- Production LOC added (~30 in Repl.fs new arms + ~2 in Rendering.fs renderHelp = ~32)
- Test LOC added (~250 in ReplTests.fs new tests + ~20 modified renderHelp test in RenderingTests.fs = ~270)
- Test count delta (e.g., 328 → 333; cumulative Phase 32 delta: 316 → 333 = +17)
- Bench gate result (e.g., "6/6 PASS — no regression" or "7/7 PASS")
- Frontmatter to include:
  - `requires: [32-01]` (depends on SessionMeta + listRecent + renderSessions)
  - `affects: [33, 34]` (downstream phases consume the same dispatcher integration pattern)
  - `subsystem: cli-session`
  - `tech_stack_added: []` (no new NuGet)
- Confirm Phase 32 success criteria 1-5 (from ROADMAP.md) all observable from end-to-end smoke test:
  1. `/sessions` shows recent N (10 default) ✓
  2. `/resume <id>` known/unknown handled correctly ✓
  3. corrupt jsonl handled, REPL stays alive ✓
  4. `FileSessionStore` has `listRecent`; `Load` (the existing "loadById") reused ✓
  5. Bench gate 7/7 PASS preserved ✓
- Note any deviations (expected: none — research is HIGH confidence).
- Phase 32 status: ready for `/gsd:verify-work 32` UAT.
</output>
