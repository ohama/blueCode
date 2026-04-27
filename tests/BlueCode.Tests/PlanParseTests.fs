module BlueCode.Tests.PlanParseTests

open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Expecto
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Core.AgentLoop
open BlueCode.Cli.Adapters.LlmWire
open BlueCode.Cli.Adapters.QwenHttpClient
open BlueCode.Tests.MockHelpers

// ── Scripted ILlmClient ──────────────────────────────────────────────────────

/// Minimal scripted ILlmClient for plan-mode tests. responses is consumed
/// front-to-back; the i-th CompleteAsync call returns responses[i].
/// If exhausted, returns Error LlmUnreachable to surface mis-scripted tests.
let private scriptedClient (responses: Result<LlmResponse, AgentError> list) : ILlmClient * (unit -> int) =
    let mutable queue = responses
    let mutable callCount = 0
    let client =
        { new ILlmClient with
            member _.CompleteAsync (_messages: Message list) (_model: Model) (_ct: CancellationToken) : Task<Result<LlmResponse, AgentError>> =
                callCount <- callCount + 1
                match queue with
                | [] -> Task.FromResult(Error (LlmUnreachable("scripted-client", "no more scripted responses")))
                | r :: rest ->
                    queue <- rest
                    Task.FromResult(r) }
    (client, fun () -> callCount)

// ── Test fixtures ────────────────────────────────────────────────────────────

let private makeStep (tool: string) (rawInput: string) (rationale: string) : PlannedStep =
    makePlannedStep tool rawInput rationale

let private validPlan : Plan =
    { Steps = [ makeStep "read_file" """{"path":"a.fs"}""" "read first"
                makeStep "list_dir" """{"path":"src"}""" "then list" ]
      Rationale = "explore then enumerate" }

let private cfg : AgentConfig =
    { MaxLoops = 5
      ContextCapacity = 3
      SystemPrompt = "You are blueCode."
      ForcedModel = Some Qwen122B }

let private suffix = """[PLAN MODE] Emit action="plan" with input={steps:[],rationale:""}."""

// ── Wire-layer parse round-trip tests ────────────────────────────────────────

let private wireParseTests =
    testList "PlanParse.wire" [
        testCase "toLlmOutput maps action=plan to LlmOutput.Plan" <| fun () ->
            let inputJson = """{"steps":[{"tool":"read_file","input":{"path":"a.fs"},"rationale":"r1"}],"rationale":"top"}"""
            use doc = JsonDocument.Parse(inputJson)
            let step : LlmStep = { thought = "I will plan"; action = "plan"; input = doc.RootElement.Clone() }
            match toLlmOutput step with
            | Ok { Output = Plan p } ->
                Expect.equal p.Steps.Length 1 "single step"
                Expect.equal p.Rationale "top" "rationale round-trips"
                let s0 = p.Steps.[0]
                let (ToolName n) = s0.Tool
                Expect.equal n "read_file" "tool name round-trips"
            | other -> failtestf "expected Ok Plan, got %A" other

        testCase "toLlmOutput rejects plan input missing 'steps' field as SchemaViolation" <| fun () ->
            let inputJson = """{"rationale":"missing steps"}"""
            use doc = JsonDocument.Parse(inputJson)
            let step : LlmStep = { thought = "bad"; action = "plan"; input = doc.RootElement.Clone() }
            match toLlmOutput step with
            | Error (SchemaViolation _) -> ()
            | other -> failtestf "expected SchemaViolation, got %A" other

        testCase "toLlmOutput rejects plan input with empty rationale as SchemaViolation" <| fun () ->
            let inputJson = """{"steps":[],"rationale":""}"""
            use doc = JsonDocument.Parse(inputJson)
            let step : LlmStep = { thought = "bad"; action = "plan"; input = doc.RootElement.Clone() }
            match toLlmOutput step with
            | Error (SchemaViolation _) -> ()
            | other -> failtestf "expected SchemaViolation, got %A" other
    ]

// ── runPlanTurn tests ─────────────────────────────────────────────────────────

let private runPlanTurnTests =
    testList "PlanParse.runPlanTurn" [
        testCase "happy path: valid plan returned on attempt 1" <| fun () ->
            let (client, getCount) = scriptedClient [ makePlanResponse "thinking" validPlan ]
            let result =
                runPlanTurn cfg client Qwen122B [] "explore the repo" suffix CancellationToken.None
                |> (fun t -> t.GetAwaiter().GetResult())
            match result with
            | Ok p -> Expect.equal p.Steps.Length 2 "2 steps"
            | Error e -> failtestf "expected Ok plan, got %A" e
            Expect.equal (getCount ()) 1 "exactly 1 LLM call (no retry needed)"

        testCase "retry path: PlanInvalid on attempt 1 (6 steps), valid plan on attempt 2" <| fun () ->
            // Attempt 1: plan with 6 steps (length > 5 -> PlanInvalid via validator)
            let oversizedPlan : Plan =
                { Steps = List.replicate 6 (makeStep "read_file" "{}" "r")
                  Rationale = "too long" }
            let (client, getCount) =
                scriptedClient [
                    makePlanResponse "first try" oversizedPlan
                    makePlanResponse "corrected" validPlan
                ]
            let result =
                runPlanTurn cfg client Qwen122B [] "input" suffix CancellationToken.None
                |> (fun t -> t.GetAwaiter().GetResult())
            match result with
            | Ok p -> Expect.equal p.Steps.Length 2 "valid plan returned after retry"
            | Error e -> failtestf "expected Ok after retry, got %A" e
            Expect.equal (getCount ()) 2 "exactly 2 LLM calls (1 retry)"

        testCase "retry exhaustion: PlanInvalid both attempts -> Error PlanInvalid" <| fun () ->
            let unknownToolPlan : Plan =
                { Steps = [ makeStep "fake_tool" "{}" "bogus" ]
                  Rationale = "bad" }
            let (client, getCount) =
                scriptedClient [
                    makePlanResponse "try 1" unknownToolPlan
                    makePlanResponse "try 2" unknownToolPlan
                ]
            let result =
                runPlanTurn cfg client Qwen122B [] "input" suffix CancellationToken.None
                |> (fun t -> t.GetAwaiter().GetResult())
            match result with
            | Error (PlanInvalid _) -> ()
            | other -> failtestf "expected Error PlanInvalid, got %A" other
            Expect.equal (getCount ()) 2 "exactly 2 LLM calls (retry exhausted)"

        testCase "non-retryable error: LlmUnreachable returned immediately" <| fun () ->
            let (client, getCount) = scriptedClient [ Error (LlmUnreachable("x", "boom")) ]
            let result =
                runPlanTurn cfg client Qwen122B [] "input" suffix CancellationToken.None
                |> (fun t -> t.GetAwaiter().GetResult())
            match result with
            | Error (LlmUnreachable _) -> ()
            | other -> failtestf "expected LlmUnreachable, got %A" other
            Expect.equal (getCount ()) 1 "exactly 1 LLM call (no retry on transport error)"

        testCase "wrong output kind: FinalAnswer in plan-mode -> PlanInvalid (then retry recovers)" <| fun () ->
            let (client, getCount) =
                scriptedClient [
                    makeMockResponse "wrong" (FinalAnswer "I am done")
                    makePlanResponse "now correct" validPlan
                ]
            let result =
                runPlanTurn cfg client Qwen122B [] "input" suffix CancellationToken.None
                |> (fun t -> t.GetAwaiter().GetResult())
            match result with
            | Ok p -> Expect.equal p.Steps.Length 2 "recovered after retry"
            | Error e -> failtestf "expected Ok after retry, got %A" e
            Expect.equal (getCount ()) 2 "FinalAnswer in plan-mode triggers retry"
    ]

// ── Public test list ──────────────────────────────────────────────────────────

let tests = testList "PlanParseTests" [ wireParseTests; runPlanTurnTests ]
