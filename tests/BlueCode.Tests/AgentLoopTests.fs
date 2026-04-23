module BlueCode.Tests.AgentLoopTests

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Core.AgentLoop

// ── Mock helpers ──────────────────────────────────────────────────────────────

/// Build a fake ILlmClient that returns scripted responses per call.
/// Responses are dequeued FIFO; exhausted queue throws (test bug).
let private mockLlm (responses: Result<LlmOutput, AgentError> list) : ILlmClient =
    let queue = System.Collections.Generic.Queue<_>(responses)
    { new ILlmClient with
        member _.CompleteAsync _messages _model _ct =
            if queue.Count = 0 then
                failwith "mockLlm: response queue exhausted — test bug"
            Task.FromResult(queue.Dequeue()) }

/// Build a fake IToolExecutor that always returns Ok (Success "stub-output").
let private mockToolsOk : IToolExecutor =
    { new IToolExecutor with
        member _.ExecuteAsync _tool _ct = Task.FromResult(Ok (Success "stub-output")) }

/// Helper: build a ToolCall LlmOutput with raw JSON input.
let private toolCall (action: string) (rawJson: string) : LlmOutput =
    ToolCall (ToolName action, ToolInput (Map.ofList [ ("_raw", rawJson) ]))

let private testConfig : AgentConfig = {
    MaxLoops        = 5
    ContextCapacity = 3
    SystemPrompt    = "test-system-prompt"
}

/// No-op step callback.
let private discardStep : Step -> unit = fun _ -> ()

/// Capturing step callback (single-thread test runs — no lock needed).
let private captureSteps () =
    let captured = ResizeArray<Step>()
    let sink (s: Step) = captured.Add(s)
    sink, captured

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let agentLoopTests =
    testList "AgentLoop" [

        testCaseAsync "happy path: LLM returns FinalAnswer immediately" <| async {
            let llm = mockLlm [ Ok (FinalAnswer "done") ]
            let! result =
                runSession testConfig llm mockToolsOk discardStep "hello" CancellationToken.None
                |> Async.AwaitTask
            match result with
            | Ok r ->
                Expect.equal r.FinalAnswer "done" "FinalAnswer text"
                Expect.equal r.LoopCount 1 "LoopCount should be 1"
                Expect.equal r.Steps.Length 1 "should have exactly 1 step"
                match r.Steps.[0].Action with
                | FinalAnswer ans -> Expect.equal ans "done" "step action should be FinalAnswer done"
                | other -> failtestf "expected FinalAnswer action, got %A" other
            | Error e -> failtestf "expected Ok, got Error %A" e
        }

        testCaseAsync "max iter: 5 distinct ToolCalls without FinalAnswer -> MaxLoopsExceeded" <| async {
            let calls =
                [ "a.txt"; "b.txt"; "c.txt"; "d.txt"; "e.txt" ]
                |> List.map (fun f -> Ok (toolCall "read_file" (sprintf "{\"path\":\"%s\"}" f)))
            let llm = mockLlm calls
            let! result =
                runSession testConfig llm mockToolsOk discardStep "list files" CancellationToken.None
                |> Async.AwaitTask
            Expect.equal result (Error MaxLoopsExceeded) "should hit MaxLoopsExceeded after 5 ToolCalls"
        }

        testCaseAsync "loop guard: 3x same (action, input) trips LoopGuardTripped" <| async {
            // 3 identical ToolCalls with same action + same input — guard should trip on 3rd
            let calls = List.replicate 3 (Ok (toolCall "read_file" "{\"path\":\"same.txt\"}"))
            let llm = mockLlm calls
            let! result =
                runSession testConfig llm mockToolsOk discardStep "read same file" CancellationToken.None
                |> Async.AwaitTask
            match result with
            | Error (LoopGuardTripped action) ->
                Expect.equal action "read_file" "tripped action should be read_file"
            | other -> failtestf "expected Error (LoopGuardTripped _), got %A" other
        }

        testCaseAsync "JSON retry pass: malformed first attempt, valid second -> Ok" <| async {
            // First call returns InvalidJsonOutput; second returns Ok FinalAnswer
            let llm = mockLlm [
                Error (InvalidJsonOutput "garbage")
                Ok (FinalAnswer "recovered")
            ]
            let! result =
                runSession testConfig llm mockToolsOk discardStep "hello" CancellationToken.None
                |> Async.AwaitTask
            match result with
            | Ok r ->
                Expect.equal r.FinalAnswer "recovered" "FinalAnswer after retry"
                Expect.equal r.LoopCount 1 "LoopCount should still be 1 (retry is within same iteration)"
            | Error e -> failtestf "expected Ok after retry, got Error %A" e
        }

        testCaseAsync "JSON retry exhausted: both malformed -> InvalidJsonOutput with ORIGINAL raw" <| async {
            // Both attempts return InvalidJsonOutput — original raw should be preserved
            let llm = mockLlm [
                Error (InvalidJsonOutput "garbage-1")
                Error (InvalidJsonOutput "garbage-2")
            ]
            let! result =
                runSession testConfig llm mockToolsOk discardStep "hello" CancellationToken.None
                |> Async.AwaitTask
            match result with
            | Error (InvalidJsonOutput raw) ->
                Expect.equal raw "garbage-1" "original (first) raw should be preserved"
            | other -> failtestf "expected Error (InvalidJsonOutput _), got %A" other
        }

        testCaseAsync "step timing: all emitted Steps have populated StartedAt/EndedAt/DurationMs" <| async {
            let llm = mockLlm [ Ok (FinalAnswer "done") ]
            let sink, captured = captureSteps ()
            let! result =
                runSession testConfig llm mockToolsOk sink "hello" CancellationToken.None
                |> Async.AwaitTask
            match result with
            | Error e -> failtestf "expected Ok, got Error %A" e
            | Ok _ ->
                Expect.equal captured.Count 1 "should have captured exactly 1 step"
                let step = captured.[0]
                Expect.isTrue (step.StartedAt > DateTimeOffset.MinValue) "StartedAt should be populated"
                Expect.isTrue (step.EndedAt >= step.StartedAt) "EndedAt should be >= StartedAt"
                Expect.isTrue (step.DurationMs >= 0L) "DurationMs should be non-negative"
        }

    ]
