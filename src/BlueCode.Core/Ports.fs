module BlueCode.Core.Ports

open System.Threading
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open BlueCode.Core.Domain

/// Contract Phase 2's QwenHttpClient implements.
/// Returns Task<Result<_, _>> so callers can compose with taskResult {}.
/// Uses task {} semantics; async CE is banned in Core — see scripts/check-no-async.sh.
type ILlmClient =
    abstract member CompleteAsync:
        messages: Message list -> model: Model -> ct: CancellationToken -> Task<Result<LlmOutput, AgentError>>

/// Contract Phase 3's FsToolExecutor implements.
/// Returns ToolResult (structured outcome) wrapped in Result so loop-level
/// errors (UnknownTool, ToolFailure) remain in AgentError.
type IToolExecutor =
    abstract member ExecuteAsync: tool: Tool -> ct: CancellationToken -> Task<Result<ToolResult, AgentError>>

/// Success Criterion 5 proof: taskResult {} CE from FsToolkit.ErrorHandling
/// compiles and runs inside BlueCode.Core. This binding is intentionally
/// private and trivial — its only job is to force the CE through the
/// compiler. Phase 4 (AgentLoop.fs) provides the real uses and this stub
/// can be deleted then, or simply ignored (F# does not warn on unused
/// private values bound at module scope by default).
let private _taskResultCompileProof: Task<Result<unit, AgentError>> =
    taskResult {
        let! value = Ok()
        return value
    }
