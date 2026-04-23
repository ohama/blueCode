module BlueCode.Tests.ModelsProbeTests

open Expecto
open BlueCode.Cli.Adapters.QwenHttpClient

// Note: NO [<Tests>] attribute — this project uses explicit rootTests registration
// in RouterTests.fs. See STATE.md Accumulated Decisions (04-02).

/// Tests for tryParseMaxModelLen — the pure JSON parse helper.
/// These tests exercise all fallback paths without any HTTP network calls.
let private maxModelLenTests =
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

          testCase "probeModelInfoAsync against closed port -> ModelId='' MaxModelLen=8192 fallback"
          <| fun _ ->
              // Port 64321 is highly unlikely to be listening; the connection will be refused,
              // triggering the catch path in probeModelInfoAsync which returns the fallback
              // record. This exercises the same WARN-and-fallback semantics that v1.0's
              // getMaxModelLenAsync had, now applied to the combined ModelInfo probe.
              let result =
                  probeModelInfoAsync "http://127.0.0.1:64321" System.Threading.CancellationToken.None
                  |> fun t -> t.GetAwaiter().GetResult()

              Expect.equal result.ModelId "" "closed-port fallback ModelId must be empty string"
              Expect.equal result.MaxModelLen 8192 "closed-port fallback MaxModelLen must be 8192 floor" ]

/// Tests for tryParseModelId — the pure JSON parse helper for data[0].id.
/// These tests exercise all fallback paths without any HTTP network calls.
/// Satisfies SC-4: unit test with injected id string wired into rootTests.
let private modelIdTests =
    testList
        "QwenHttpClient.tryParseModelId"
        [

          testCase "valid JSON with data[0].id = 'qwen2.5-coder-32b' -> Some 'qwen2.5-coder-32b'"
          <| fun _ ->
              let json =
                  """{"object":"list","data":[{"id":"qwen2.5-coder-32b","object":"model","max_model_len":32768}]}"""

              Expect.equal
                  (tryParseModelId json)
                  (Some "qwen2.5-coder-32b")
                  "Valid /v1/models response should extract data[0].id as Some string"

          testCase "valid JSON with absolute-path id (SC-3 wire shape) -> Some that path"
          <| fun _ ->
              // This fixture proves SC-4: the parser correctly surfaces whatever id the
              // server returns — including the exact absolute-path shape the v1.0 hardcode
              // used. The injected id string case from SC-4.
              let json =
                  """{"object":"list","data":[{"id":"/Users/ohama/llm-system/models/qwen32b","object":"model"}]}"""

              Expect.equal
                  (tryParseModelId json)
                  (Some "/Users/ohama/llm-system/models/qwen32b")
                  "Absolute-path id from server must be returned as-is (parser is not path-aware)"

          testCase "valid JSON with id = null -> None"
          <| fun _ ->
              let json =
                  """{"object":"list","data":[{"id":null,"object":"model"}]}"""

              Expect.equal (tryParseModelId json) None "null id field should produce None"

          testCase "valid JSON with id field missing -> None"
          <| fun _ ->
              let json =
                  """{"object":"list","data":[{"object":"model","created":1234567890}]}"""

              Expect.equal (tryParseModelId json) None "Missing id field should produce None"

          testCase "valid JSON with id = empty string -> None"
          <| fun _ ->
              // Empty id is functionally unusable; rejecting it here makes the probe
              // fallback path log the WARN visibly instead of silently succeeding.
              let json =
                  """{"object":"list","data":[{"id":"","object":"model"}]}"""

              Expect.equal (tryParseModelId json) None "Empty string id should produce None (unusable)"

          testCase "valid JSON with empty data array -> None"
          <| fun _ ->
              let json = """{"object":"list","data":[]}"""
              Expect.equal (tryParseModelId json) None "Empty data array should produce None"

          testCase "invalid JSON ('not json') -> None"
          <| fun _ ->
              Expect.equal (tryParseModelId "not json") None "Invalid JSON should not throw — produces None"

          testCase "valid JSON with id = 42 (non-string number) -> None"
          <| fun _ ->
              // Schema defense: vLLM spec says id is string, but we defensively
              // reject non-string values instead of coercing.
              let json =
                  """{"object":"list","data":[{"id":42,"object":"model"}]}"""

              Expect.equal
                  (tryParseModelId json)
                  None
                  "Non-string id (number 42) should produce None (schema defense)" ]

let tests =
    testList
        "QwenHttpClient probes"
        [ maxModelLenTests
          modelIdTests ]
