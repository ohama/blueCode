module BlueCode.Tests.RouterTests

open Expecto
open BlueCode.Core.Domain
open BlueCode.Core.Router

let classifyTests = testList "Router.classifyIntent" [

    testCase "'fix the null check' -> Debug (Success Criterion 3)" <| fun () ->
        Expect.equal (classifyIntent "fix the null check") Debug
            "Debug keyword 'fix' should classify as Debug"

    testCase "'explain the traceback' -> Debug" <| fun () ->
        Expect.equal (classifyIntent "explain the traceback") Debug
            "'traceback' is a Debug keyword"

    testCase "'design the payment system' -> Design" <| fun () ->
        Expect.equal (classifyIntent "design the payment system") Design
            "'design' and 'system' are both Design keywords"

    testCase "'분석해줘 this diff' -> Analysis" <| fun () ->
        Expect.equal (classifyIntent "분석해줘 this diff") Analysis
            "Korean '분석' is an Analysis keyword"

    testCase "'write a function' -> Implementation" <| fun () ->
        Expect.equal (classifyIntent "write a function") Implementation
            "'write' is an Implementation keyword"

    testCase "'hello' (no keywords) -> General" <| fun () ->
        Expect.equal (classifyIntent "hello") General
            "input without any classified keyword falls through to General"

    testCase "case-insensitive: 'FIX the bug' -> Debug" <| fun () ->
        Expect.equal (classifyIntent "FIX the bug") Debug
            "classifyIntent lowercases before matching"
]

let intentToModelTests = testList "Router.intentToModel" [

    testCase "Debug -> Qwen72B (Success Criterion 3)" <| fun () ->
        Expect.equal (intentToModel Debug) Qwen72B
            "Debug intent routes to 72B model"

    testCase "Design -> Qwen72B" <| fun () ->
        Expect.equal (intentToModel Design) Qwen72B ""

    testCase "Analysis -> Qwen72B" <| fun () ->
        Expect.equal (intentToModel Analysis) Qwen72B ""

    testCase "Implementation -> Qwen32B" <| fun () ->
        Expect.equal (intentToModel Implementation) Qwen32B ""

    testCase "General -> Qwen32B" <| fun () ->
        Expect.equal (intentToModel General) Qwen32B ""
]

let modelToEndpointTests = testList "Router.modelToEndpoint" [

    testCase "Qwen32B -> Port8000" <| fun () ->
        Expect.equal (modelToEndpoint Qwen32B) Port8000
            "32B serves on port 8000"

    testCase "Qwen72B -> Port8001" <| fun () ->
        Expect.equal (modelToEndpoint Qwen72B) Port8001
            "72B serves on port 8001"
]

let endpointToUrlTests = testList "Router.endpointToUrl" [

    testCase "Port8000 -> localhost:8000 chat completions URL" <| fun () ->
        Expect.equal
            (endpointToUrl Port8000)
            "http://127.0.0.1:8000/v1/chat/completions"
            "32B endpoint URL"

    testCase "Port8001 -> localhost:8001 chat completions URL" <| fun () ->
        Expect.equal
            (endpointToUrl Port8001)
            "http://127.0.0.1:8001/v1/chat/completions"
            "72B endpoint URL"
]

let allTests = testList "Router" [
    classifyTests
    intentToModelTests
    modelToEndpointTests
    endpointToUrlTests
]

let rootTests = testList "all" [
    allTests
    BlueCode.Tests.LlmPipelineTests.allTests
    BlueCode.Tests.ToLlmOutputTests.toLlmOutputTests
    BlueCode.Tests.SmokeTests.smokeTests
    BlueCode.Tests.FileToolsTests.fileToolsTests
    BlueCode.Tests.BashSecurityTests.bashSecurityTests
]

[<EntryPoint>]
let main args = runTestsWithCLIArgs [] args rootTests
