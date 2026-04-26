module BlueCode.Tests.MockHelpers

open BlueCode.Core.Domain

/// Phase 7: wraps an LlmOutput with a non-empty Thought into the new LlmResponse
/// success-payload expected by ILlmClient.CompleteAsync. Consolidated in v1.4 (TST-01)
/// from prior duplications in AgentLoopTests.fs and ReplTests.fs.
let makeMockResponse (thought: string) (output: LlmOutput) : Result<LlmResponse, AgentError> =
    Ok { Thought = Thought thought; Output = output }

/// Phase 14 (v2.0 PLAN-01): builder for PlannedStep test fixtures. Keeps
/// Plan-construction terse so tests focus on the validator behaviour, not
/// on record literal noise.
let makePlannedStep (toolName: string) (rawJson: string) (rationale: string) : PlannedStep =
    { Tool = ToolName toolName
      Input = ToolInput(Map.ofList [ ("_raw", rawJson) ])
      Rationale = rationale }

/// Phase 14 (v2.0 PLAN-01): wraps a Plan into the new LlmOutput.Plan variant
/// for tests that script the LLM into emitting a plan.
let makePlanResponse (thought: string) (plan: Plan) : Result<LlmResponse, AgentError> =
    Ok { Thought = Thought thought; Output = Plan plan }
