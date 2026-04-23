module BlueCode.Tests.SmokeTests

open System
open System.Threading
open Expecto
open BlueCode.Core.Domain
open BlueCode.Cli.Adapters.QwenHttpClient

let private smokeEnabled () =
    Environment.GetEnvironmentVariable("BLUECODE_SMOKE_TEST") = "1"

let smokeTests =
    testList
        "Smoke (live localhost)"
        [

          testCase "QwenHttpClient.CompleteAsync round-trip against live 32B"
          <| fun () ->
              if not (smokeEnabled ()) then
                  skiptest "Set BLUECODE_SMOKE_TEST=1 and ensure localhost:8000 (32B) is serving to run."
              else
                  let client = create ()

                  let messages =
                      [ { Role = System
                          Content =
                            "Respond ONLY in JSON with fields: thought, action, input. \
                               Use action='list_dir' and input={\"path\":\".\"}. No prose. No markdown fences." }
                        { Role = User
                          Content = "List the files in the current directory." } ]

                  use cts = new CancellationTokenSource(TimeSpan.FromSeconds(120.0))

                  let result =
                      (client.CompleteAsync messages Qwen32B cts.Token).GetAwaiter().GetResult()

                  match result with
                  | Ok output ->
                      // Any Ok LlmOutput is acceptable — ToolCall or FinalAnswer.
                      // We only assert the adapter completed end-to-end without an
                      // AgentError. Structural assertions on output are Phase 3 concern.
                      match output with
                      | ToolCall(ToolName name, _) ->
                          Expect.stringHasLength
                              name
                              (name.Length)
                              (sprintf "Got ToolCall %s — adapter round-trip succeeded" name)
                      | FinalAnswer answer ->
                          Expect.stringHasLength
                              answer
                              (answer.Length)
                              (sprintf "Got FinalAnswer (%d chars) — adapter round-trip succeeded" answer.Length)
                  | Error(LlmUnreachable(endpoint, detail)) ->
                      failtestf
                          "Endpoint unreachable (gate was on): %s — %s. Is vLLM 32B serving on localhost:8000?"
                          endpoint
                          detail
                  | Error other -> failtestf "Smoke round-trip returned unexpected AgentError: %A" other ]
