module BlueCode.Tests.LlmPipelineTests

open System.Text.Json
open Expecto
open BlueCode.Core.Domain
open BlueCode.Cli.Adapters.Json

// ── Extraction tests ──────────────────────────────────────────────────────────
// Cover all 4 extraction stages: bare JSON (S1), prose-wrapped (S2),
// fenced with/without lang tag (S3), and full ParseFailure (S4).

let extractionTests =
    testList
        "LlmPipeline.extract"
        [

          testCase "bare JSON parses directly (Stage 1)"
          <| fun () ->
              let content = """{"thought":"t","action":"list_dir","input":{"path":"."}}"""

              match parseLlmResponse content with
              | Ok step ->
                  Expect.equal step.action "list_dir" "action should be list_dir"
                  Expect.equal step.thought "t" "thought should round-trip"
              | Error e -> failtestf "Expected Ok but got Error: %A" e

          testCase "prose-wrapped JSON extracted via brace scan (Stage 2)"
          <| fun () ->
              let content =
                  """Sure! Here is the JSON: {"thought":"t","action":"list_dir","input":{"path":"."}} Hope this helps!"""

              match parseLlmResponse content with
              | Ok step -> Expect.equal step.action "list_dir" "stage 2 brace extraction must recover JSON from prose"
              | Error e -> failtestf "Expected Ok but got Error: %A" e

          testCase "markdown-fenced JSON with 'json' tag extracted (Stage 3)"
          <| fun () ->
              let content =
                  "Here you go:\n```json\n{\"thought\":\"t\",\"action\":\"read_file\",\"input\":{\"path\":\"main.fs\"}}\n```\nDone."

              match parseLlmResponse content with
              | Ok step ->
                  Expect.equal step.action "read_file" "stage 3 fence strip must recover JSON from fenced block"
              | Error e -> failtestf "Expected Ok but got Error: %A" e

          testCase "markdown-fenced JSON WITHOUT 'json' tag extracted (Stage 3)"
          <| fun () ->
              let content =
                  "```\n{\"thought\":\"t\",\"action\":\"final\",\"input\":{\"answer\":\"ok\"}}\n```"

              match parseLlmResponse content with
              | Ok step -> Expect.equal step.action "final" "stage 3 must work without 'json' language tag"
              | Error e -> failtestf "Expected Ok but got Error: %A" e

          testCase "nested object inside input parses correctly (brace depth tracking)"
          <| fun () ->
              let content =
                  """{"thought":"t","action":"write_file","input":{"path":"a.txt","meta":{"nested":{"deep":"v"}}}}"""

              match parseLlmResponse content with
              | Ok step ->
                  Expect.equal step.action "write_file" "nested objects in input must not break brace extractor"
              | Error e -> failtestf "Expected Ok but got Error: %A" e

          testCase "unparseable prose returns InvalidJsonOutput (Stage 4)"
          <| fun () ->
              let content = "I cannot help with that request — it violates policy."

              match parseLlmResponse content with
              | Error(InvalidJsonOutput raw) ->
                  Expect.equal raw content "InvalidJsonOutput should carry the original raw content for logging"
              | other -> failtestf "Expected InvalidJsonOutput but got: %A" other ]

// ── Schema validation tests ───────────────────────────────────────────────────
// Cover all schema violation modes: missing required field, unknown enum,
// empty thought, wrong input type, extra field, and a happy-path for all
// 5 valid action values.

let schemaTests =
    testList
        "LlmPipeline.schema"
        [

          testCase "missing required 'action' field -> SchemaViolation"
          <| fun () ->
              let content = """{"thought":"t","input":{"path":"."}}"""

              match parseLlmResponse content with
              | Error(SchemaViolation _) -> ()
              | other -> failtestf "Expected SchemaViolation but got: %A" other

          testCase "unknown action enum value -> SchemaViolation"
          <| fun () ->
              let content = """{"thought":"t","action":"unknown_tool","input":{}}"""

              match parseLlmResponse content with
              | Error(SchemaViolation _) -> ()
              | other -> failtestf "Expected SchemaViolation but got: %A" other

          testCase "empty thought string -> SchemaViolation (minLength:1)"
          <| fun () ->
              let content = """{"thought":"","action":"final","input":{"answer":"x"}}"""

              match parseLlmResponse content with
              | Error(SchemaViolation _) -> ()
              | other -> failtestf "Expected SchemaViolation but got: %A" other

          testCase "input is a string not an object -> SchemaViolation"
          <| fun () ->
              let content = """{"thought":"t","action":"list_dir","input":"not-an-object"}"""

              match parseLlmResponse content with
              | Error(SchemaViolation _) -> ()
              | other -> failtestf "Expected SchemaViolation but got: %A" other

          testCase "extra field (additionalProperties:false) -> SchemaViolation"
          <| fun () ->
              let content =
                  """{"thought":"t","action":"final","input":{"answer":"x"},"confidence":0.9}"""

              match parseLlmResponse content with
              | Error(SchemaViolation _) -> ()
              | other -> failtestf "Expected SchemaViolation but got: %A" other

          testCase "all 5 valid action enum values accepted"
          <| fun () ->
              for action in [ "read_file"; "write_file"; "list_dir"; "run_shell"; "final" ] do
                  let content = sprintf """{"thought":"t","action":"%s","input":{}}""" action

                  match parseLlmResponse content with
                  | Ok step -> Expect.equal step.action action (sprintf "action %s should round-trip" action)
                  | Error e -> failtestf "Valid action %s rejected: %A" action e ]

// ── LLM-04 coverage: FSharp.SystemTextJson converter handles DU round-trip ────
//
// Rationale: LLM-04 asserts JsonFSharpConverter is registered AND works for
// DU serialization. LlmStep is a record (not a DU), so the pipeline tests
// above don't exercise DU serialization. This test closes that gap by
// round-tripping MessageRole (System | User | Assistant) through jsonOptions,
// proving the converter is wired correctly. If UnionUnwrapFieldlessTags were
// ever dropped from jsonOptions, this test fails — the broken state is
// {"Case":"System"} instead of "System".

let duRoundTripTests =
    testList
        "LlmPipeline.du-roundtrip (LLM-04)"
        [

          testCase "MessageRole round-trips through jsonOptions (proves JsonFSharpConverter registered)"
          <| fun () ->
              let original = System // MessageRole.System — a DU case
              let serialized = JsonSerializer.Serialize(original, jsonOptions)
              let roundTripped = JsonSerializer.Deserialize<MessageRole>(serialized, jsonOptions)

              Expect.equal
                  roundTripped
                  original
                  (sprintf "MessageRole DU must round-trip via jsonOptions (serialized as: %s)" serialized)
              // Also verify the serialized form is NOT the default System.Text.Json
              // DU encoding ({"Case":"System"}). If JsonFSharpConverter is missing or
              // UnionUnwrapFieldlessTags is not set, this assertion catches the regression.
              Expect.isFalse
                  (serialized.Contains("\"Case\""))
                  "FSharp.SystemTextJson should produce F#-idiomatic DU form, not the Case/Fields shape" ]

let allTests =
    testList "LlmPipeline" [ extractionTests; schemaTests; duRoundTripTests ]
