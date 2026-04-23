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
      ModelUsed = Qwen32B
      StartedAt = DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero)
      EndedAt = DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero).AddMilliseconds(423.0)
      DurationMs = 423L }

let private finalStep: Step =
    { StepNumber = 1
      Thought = Thought "done"
      Action = FinalAnswer "The answer is 42."
      ToolResult = None
      Status = StepSuccess
      ModelUsed = Qwen32B
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
                    Model = Qwen32B }

              let out = renderResult r
              Expect.stringContains out "done" "answer shown"

          testCase "renderError produces user-readable messages (no stack trace)"
          <| fun _ ->
              Expect.stringContains (renderError MaxLoopsExceeded) "5 steps" "MaxLoops msg"
              Expect.stringContains (renderError (LoopGuardTripped "run_shell")) "run_shell" "guard msg names action"
              Expect.equal (renderError UserCancelled) "Cancelled." "cancel msg"
              let invalid = renderError (InvalidJsonOutput "some garbage that is short")
              Expect.stringContains invalid "invalid JSON" "invalid json msg" ]
