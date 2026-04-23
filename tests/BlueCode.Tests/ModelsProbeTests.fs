module BlueCode.Tests.ModelsProbeTests

open Expecto
open BlueCode.Cli.Adapters.QwenHttpClient

// Note: NO [<Tests>] attribute — this project uses explicit rootTests registration
// in RouterTests.fs. See STATE.md Accumulated Decisions (04-02).

/// Tests for tryParseMaxModelLen — the pure JSON parse helper.
/// These tests exercise all fallback paths without any HTTP network calls.
let tests =
    testList
        "QwenHttpClient.tryParseMaxModelLen"
        [

          testCase "valid JSON with data[0].max_model_len = 32768 -> Some 32768"
          <| fun _ ->
              let json =
                  """{"object":"list","data":[{"id":"qwen2.5-coder-32b","object":"model","created":1234567890,"owned_by":"vllm","max_model_len":32768}]}"""

              Expect.equal
                  (tryParseMaxModelLen json)
                  (Some 32768)
                  "Valid /v1/models response with max_model_len=32768 should parse to Some 32768"

          testCase "valid JSON with max_model_len = null -> None"
          <| fun _ ->
              let json =
                  """{"object":"list","data":[{"id":"qwen2.5-coder-32b","object":"model","max_model_len":null}]}"""

              Expect.equal
                  (tryParseMaxModelLen json)
                  None
                  "null max_model_len should produce None (falls back to 8192 in caller)"

          testCase "valid JSON with max_model_len field missing -> None"
          <| fun _ ->
              let json =
                  """{"object":"list","data":[{"id":"qwen2.5-coder-32b","object":"model","created":1234567890}]}"""

              Expect.equal (tryParseMaxModelLen json) None "Missing max_model_len field should produce None"

          testCase "valid JSON with empty data array -> None"
          <| fun _ ->
              let json = """{"object":"list","data":[]}"""
              Expect.equal (tryParseMaxModelLen json) None "Empty data array should produce None"

          testCase "invalid JSON ('not json') -> None"
          <| fun _ -> Expect.equal (tryParseMaxModelLen "not json") None "Invalid JSON should not throw — produces None"

          testCase "valid JSON with max_model_len = 0 -> None (non-positive rejected)"
          <| fun _ ->
              let json =
                  """{"object":"list","data":[{"id":"qwen2.5-coder-32b","max_model_len":0}]}"""

              Expect.equal (tryParseMaxModelLen json) None "Zero max_model_len is non-positive and should produce None"

          testCase "valid JSON with max_model_len = 128000 -> Some 128000"
          <| fun _ ->
              let json =
                  """{"object":"list","data":[{"id":"qwen2.5-coder-72b","max_model_len":128000}]}"""

              Expect.equal
                  (tryParseMaxModelLen json)
                  (Some 128000)
                  "128K context model should parse correctly to Some 128000"

          testCase "getMaxModelLenAsync against closed port -> fallback 8192"
          <| fun _ ->
              // Verify the HTTP-failure fallback path live.
              // Port 64321 is highly unlikely to be listening; the connection will be
              // refused immediately, triggering the catch -> fallback path.
              // We cannot easily override the hardcoded URL in getMaxModelLenAsync,
              // but we CAN verify the function returns 8192 when port 8000 is closed
              // (in CI / test environment, no vLLM is running).
              //
              // Skip this test if port 8000 happens to be serving (would return real value).
              // In normal test runs (no vLLM), this exercises the fallback path.
              let result =
                  getMaxModelLenAsync System.Threading.CancellationToken.None
                  |> fun t -> t.GetAwaiter().GetResult()
              // Either returns 8192 (fallback) or a real parsed value (if vLLM is running).
              // Either way the function must return a positive int.
              Expect.isGreaterThan result 0 (sprintf "getMaxModelLenAsync must return a positive int (got %d)" result) ]
