module BlueCode.Tests.MockHelpers

open BlueCode.Core.Domain

/// Phase 7: wraps an LlmOutput with a non-empty Thought into the new LlmResponse
/// success-payload expected by ILlmClient.CompleteAsync. Consolidated in v1.4 (TST-01)
/// from prior duplications in AgentLoopTests.fs and ReplTests.fs.
let makeMockResponse (thought: string) (output: LlmOutput) : Result<LlmResponse, AgentError> =
    Ok { Thought = Thought thought; Output = output }
