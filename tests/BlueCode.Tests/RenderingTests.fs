module BlueCode.Tests.RenderingTests

open System
open Expecto
open BlueCode.Core.Domain
open BlueCode.Cli.Rendering

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
              Expect.isFalse (s.Contains("chars:    0 ")) "non-zero chars for non-empty steps" ]
