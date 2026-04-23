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
                  Expect.equal c.Config.MaxLoops 5 "MaxLoops = 5"
                  Expect.equal c.Config.ContextCapacity 3 "ContextCapacity = 3"
                  Expect.isNotEmpty c.Config.SystemPrompt "SystemPrompt non-empty"
                  Expect.isTrue (c.LogPath.EndsWith(".jsonl")) "LogPath ends with .jsonl"
                  Expect.isTrue (c.LogPath.Length > 0) "LogPath resolved to non-empty path"
              finally
                  (c.JsonlSink :> IDisposable).Dispose()

          testCase "bootstrap SystemPrompt mentions all 5 actions"
          <| fun _ ->
              let tempRoot = Path.GetTempPath()
              let c = bootstrap tempRoot defaultCliOptions

              try
                  let p = c.Config.SystemPrompt

                  for action in [ "read_file"; "write_file"; "list_dir"; "run_shell"; "final" ] do
                      Expect.stringContains p action (sprintf "system prompt mentions %s" action)
              finally
                  (c.JsonlSink :> IDisposable).Dispose() ]
