module BlueCode.Tests.CompositionRootTests

open System
open System.IO
open Expecto
open BlueCode.Cli.CompositionRoot

[<Tests>]
let tests =
    testList
        "CompositionRoot"
        [ testCase "bootstrap returns non-null LlmClient, ToolExecutor, JsonlSink, Config"
          <| fun _ ->
              let tempRoot =
                  Path.Combine(Path.GetTempPath(), sprintf "bluecode-ct-%s" (Guid.NewGuid().ToString("N")))

              Directory.CreateDirectory(tempRoot) |> ignore
              let c = bootstrap tempRoot defaultCliOptions

              try
                  Expect.isNotNull (box c.LlmClient) "LlmClient set"
                  Expect.isNotNull (box c.ToolExecutor) "ToolExecutor set"
                  Expect.isNotNull (box c.JsonlSink) "JsonlSink set"
                  Expect.equal c.ProjectRoot tempRoot "ProjectRoot threaded"
                  Expect.equal c.Config.MaxLoops 10 "MaxLoops = 10"
                  Expect.equal c.Config.ContextCapacity 3 "ContextCapacity = 3"
                  Expect.isNotEmpty c.Config.SystemPrompt "SystemPrompt non-empty"
                  Expect.isTrue (c.LogPath.EndsWith(".jsonl")) "LogPath ends with .jsonl"
                  Expect.isTrue (c.LogPath.Length > 0) "LogPath resolved to non-empty path"
              finally
                  (c.JsonlSink :> IDisposable).Dispose()

          testCase "bootstrap SystemPrompt mentions all 8 actions"
          <| fun _ ->
              let tempRoot = Path.GetTempPath()
              let c = bootstrap tempRoot defaultCliOptions

              try
                  let p = c.Config.SystemPrompt

                  for action in [ "read_file"; "write_file"; "list_dir"; "run_shell"; "edit_file"; "glob_search"; "grep_search"; "final" ] do
                      Expect.stringContains p action (sprintf "system prompt mentions %s" action)
              finally
                  (c.JsonlSink :> IDisposable).Dispose()

          testCase "planSystemPromptSuffix contains Phase 36-03 max-10 + no-placeholder regression markers"
          <| fun _ ->
              // Phase 36-03 (T-75/T-76 mitigation): the suffix MUST retain these literal
              // strings. AgentLoop.buildCorrection (Core, immutable) cannot enforce them; this
              // assertion is the only gate against silent removal during future prompt tuning.
              let s = planSystemPromptSuffix
              Expect.stringContains s "MAXIMUM 10" "T-75 mitigation: HARD LIMIT max-10-steps clause must remain"
              Expect.stringContains s "placeholder" "T-76 mitigation: no-placeholder clause must remain" ]
