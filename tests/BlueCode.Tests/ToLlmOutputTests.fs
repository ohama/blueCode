module BlueCode.Tests.ToLlmOutputTests

open System.Text.Json
open Expecto
open BlueCode.Core.Domain
open BlueCode.Cli.Adapters.LlmWire
open BlueCode.Cli.Adapters.QwenHttpClient

/// Helper: build a JsonElement from a JSON literal string.
let private jsonElement (src: string) : JsonElement =
    use doc = JsonDocument.Parse(src)
    // Clone so the element survives after the JsonDocument is disposed.
    doc.RootElement.Clone()

let toLlmOutputTests = testList "ToLlmOutput" [

    testCase "final action with string answer -> FinalAnswer" <| fun () ->
        let step = {
            thought = "done"
            action  = "final"
            input   = jsonElement """{"answer":"the result"}"""
        }
        match toLlmOutput step with
        | Ok (FinalAnswer s) ->
            Expect.equal s "the result" "FinalAnswer payload should be the answer string"
        | other ->
            failtestf "Expected Ok (FinalAnswer ...) but got: %A" other

    testCase "final action with missing 'answer' -> SchemaViolation" <| fun () ->
        let step = {
            thought = "done"
            action  = "final"
            input   = jsonElement """{}"""
        }
        match toLlmOutput step with
        | Error (SchemaViolation detail) ->
            Expect.isTrue (detail.Contains("answer"))
                "SchemaViolation detail should mention the missing 'answer' field"
        | other ->
            failtestf "Expected Error (SchemaViolation ...) but got: %A" other

    testCase "final action with non-string 'answer' (number) -> SchemaViolation" <| fun () ->
        let step = {
            thought = "done"
            action  = "final"
            input   = jsonElement """{"answer": 42}"""
        }
        match toLlmOutput step with
        | Error (SchemaViolation _) -> ()
        | other ->
            failtestf "Expected Error (SchemaViolation ...) but got: %A" other

    testCase "final action with non-string 'answer' (object) -> SchemaViolation" <| fun () ->
        let step = {
            thought = "done"
            action  = "final"
            input   = jsonElement """{"answer": {"nested": "x"}}"""
        }
        match toLlmOutput step with
        | Error (SchemaViolation _) -> ()
        | other ->
            failtestf "Expected Error (SchemaViolation ...) but got: %A" other

    testCase "tool action -> ToolCall with _raw passthrough" <| fun () ->
        let step = {
            thought = "reading"
            action  = "read_file"
            input   = jsonElement """{"path":"src/main.fs"}"""
        }
        match toLlmOutput step with
        | Ok (ToolCall (ToolName name, ToolInput map)) ->
            Expect.equal name "read_file" "tool name should round-trip"
            Expect.isTrue (Map.containsKey "_raw" map)
                "Phase 2 tool input passes raw JSON string under _raw key (Phase 3 refines)"
        | other ->
            failtestf "Expected Ok (ToolCall ...) but got: %A" other
]
