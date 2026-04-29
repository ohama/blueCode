module BlueCode.Tests.RenderingTests

open System
open Expecto
open BlueCode.Core.Domain
open BlueCode.Cli.Rendering
open BlueCode.Cli.Adapters.FileSessionStore

let private toolStep: Step =
    { StepNumber = 2
      Thought = Thought "inspecting config"
      Action = ToolCall(ToolName "read_file", ToolInput(Map.ofList [ ("_raw", "{\"path\":\"README.md\"}") ]))
      ToolResult = Some(Success "hello world")
      Status = StepSuccess
      ModelUsed = Qwen122B
      StartedAt = DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero)
      EndedAt = DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero).AddMilliseconds(423.0)
      DurationMs = 423L }

let private finalStep: Step =
    { StepNumber = 1
      Thought = Thought "done"
      Action = FinalAnswer "The answer is 42."
      ToolResult = None
      Status = StepSuccess
      ModelUsed = Qwen122B
      StartedAt = DateTimeOffset.MinValue
      EndedAt = DateTimeOffset.MinValue
      DurationMs = 0L }

[<Tests>]
let tests =
    testList
        "Rendering"
        [ testCase "Compact renders single line with DurationMs"
          <| fun _ ->
              let out = renderStep Compact toolStep
              Expect.stringContains out "reading file" "tool name mapped"
              Expect.stringContains out "ok" "status"
              Expect.stringContains out "423ms" "duration formatted"
              Expect.isFalse (out.Contains("\n")) "Compact must be single-line"

          testCase "Verbose renders multi-line with thought, action, result, timing"
          <| fun _ ->
              let out = renderStep Verbose toolStep
              Expect.stringContains out "Step 2" "step number"
              Expect.stringContains out "inspecting config" "thought field"
              Expect.stringContains out "read_file" "action name"
              Expect.stringContains out "README.md" "raw input echoed"
              Expect.stringContains out "Success" "result case"
              Expect.stringContains out "423ms" "duration displayed"
              Expect.isTrue (out.Contains("\n")) "Verbose must be multi-line"

          testCase "FinalAnswer step renders in both modes"
          <| fun _ ->
              let compact = renderStep Compact finalStep
              let verbose = renderStep Verbose finalStep
              Expect.stringContains compact "final answer" "compact marks final"
              Expect.stringContains verbose "42" "verbose echoes final text"

          testCase "renderResult echoes final answer"
          <| fun _ ->
              let r =
                  { FinalAnswer = "done"
                    Steps = []
                    LoopCount = 1
                    Model = Qwen122B }

              let out = renderResult r
              Expect.stringContains out "done" "answer shown"

          testCase "renderError produces user-readable messages (no stack trace)"
          <| fun _ ->
              Expect.stringContains (renderError MaxLoopsExceeded) "10 steps" "MaxLoops msg"
              Expect.stringContains (renderError (LoopGuardTripped "run_shell")) "run_shell" "guard msg names action"
              Expect.equal (renderError UserCancelled) "Cancelled." "cancel msg"
              let invalid = renderError (InvalidJsonOutput "some garbage that is short")
              Expect.stringContains invalid "invalid JSON" "invalid json msg"

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

          // ── Phase 32-01: renderSessions ──────────────────────────────────────
          testCase "renderSessions empty list returns 'no sessions found'" <| fun _ ->
              let s = renderSessions []
              Expect.equal s "no sessions found" "empty list yields exact phrase"

          testCase "renderSessions single meta shows id, started date, turns, excerpt" <| fun _ ->
              let meta : SessionMeta =
                  { Id = SessionId "deadbeef0123456789abcdef01234567"
                    StartedAt = DateTimeOffset(2026, 4, 29, 14, 30, 5, TimeSpan.Zero)
                    TurnCount = 7
                    FirstPromptExcerpt = "inspecting README" }
              let s = renderSessions [ meta ]
              Expect.stringContains s "deadbeef0123456789abcdef01234567" "id appears"
              Expect.stringContains s "2026-04-29 14:30:05" "started timestamp formatted yyyy-MM-dd HH:mm:ss"
              Expect.stringContains s "7" "turn count appears"
              Expect.stringContains s "inspecting README" "excerpt appears"
              // Header row must include the column labels.
              Expect.stringContains s "session id" "header row column: session id"
              Expect.stringContains s "started" "header row column: started"
              Expect.stringContains s "turns" "header row column: turns"
              Expect.stringContains s "first thought" "header row column label is 'first thought' (not 'first prompt')"

          testCase "renderSessions truncates excerpt longer than 40 chars with '...' suffix" <| fun _ ->
              // SessionMeta.FirstPromptExcerpt is already capped at 80 chars by listRecent;
              // renderSessions further truncates the DISPLAY column to 40 chars + '...'.
              let longExcerpt = String.replicate 60 "x"   // 60 chars (within SessionMeta's 80 cap)
              let meta : SessionMeta =
                  { Id = SessionId "abc"
                    StartedAt = DateTimeOffset.MinValue
                    TurnCount = 1
                    FirstPromptExcerpt = longExcerpt }
              let s = renderSessions [ meta ]
              Expect.stringContains s "..." "long excerpt receives ellipsis"
              Expect.stringContains s (String.replicate 40 "x") "first 40 chars displayed verbatim"
              // The full 60-char excerpt should NOT appear (we truncated to 40).
              Expect.isFalse (s.Contains(String.replicate 50 "x")) "excerpt truncated before reaching 50 'x's"

          testCase "renderSessions multiple metas yields header + N rows" <| fun _ ->
              let mk i =
                  { Id = SessionId (sprintf "session-%d" i)
                    StartedAt = DateTimeOffset(2026, 4, 29 - i, 12, 0, 0, TimeSpan.Zero)
                    TurnCount = i
                    FirstPromptExcerpt = sprintf "thought %d" i }
              let metas = [ mk 1; mk 2; mk 3 ]
              let s = renderSessions metas
              let lines = s.Split([| '\n' |])
              // 1 header + 3 data rows = 4 lines
              Expect.equal lines.Length 4 "header + 3 rows"
              Expect.stringContains s "session-1" "first id appears"
              Expect.stringContains s "session-2" "second id appears"
              Expect.stringContains s "session-3" "third id appears"
              Expect.stringContains s "thought 1" "first excerpt appears"
              Expect.stringContains s "thought 2" "second excerpt appears"
              Expect.stringContains s "thought 3" "third excerpt appears"

          testCase "renderSessions empty excerpt renders cleanly (no trailing junk)" <| fun _ ->
              // Header-only sessions have FirstPromptExcerpt = "". The row should still
              // render without crashing (no NullReferenceException, no malformed output).
              let meta : SessionMeta =
                  { Id = SessionId "abc"
                    StartedAt = DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero)
                    TurnCount = 0
                    FirstPromptExcerpt = "" }
              let s = renderSessions [ meta ]
              Expect.stringContains s "abc" "id present"
              Expect.stringContains s "0" "turn count 0 displayed"
              Expect.isFalse (s.Contains("...")) "no '...' for empty excerpt" ]
