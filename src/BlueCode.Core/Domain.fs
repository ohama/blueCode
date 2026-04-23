module BlueCode.Core.Domain

open System

// ── Routing ──────────────────────────────────────────────────────────────────

/// User intent classified from input text (ROU-01).
/// All 5 cases are part of v1; no case is TODO/reserved.
type Intent =
    | Debug
    | Design
    | Analysis
    | Implementation
    | General

/// Qwen model identifier. No URL or port fields — those belong to Endpoint.
type Model =
    | Qwen32B
    | Qwen72B

/// Port-typed endpoint. Using a DU (instead of `int` or `string`) forces
/// modelToEndpoint to be exhaustive: adding a third model is a compile error
/// in modelToEndpoint until a third Endpoint case is added.
type Endpoint =
    | Port8000
    | Port8001

// ── Tool parameter primitives (single-case DUs for type safety) ───────────────

/// Filesystem path. Validation (e.g., project-root containment) is added by
/// a smart constructor in Phase 3 (TOOL-02). Phase 1 leaves it as a newtype.
type FilePath = FilePath of string

/// Shell command string. Security validation is Phase 3 (TOOL-05).
type Command  = Command  of string

/// Timeout in MILLISECONDS. Choice of ms (not seconds) matches .NET Process
/// and CancellationTokenSource APIs directly. Phase 3 can expose a
/// seconds-based helper for human inputs.
type Timeout  = Timeout  of int

// ── Tools ────────────────────────────────────────────────────────────────────

/// Tool dispatch value. Each case carries typed parameters, not raw strings,
/// so the executor in Phase 3 cannot dispatch with a misshapen input.
///
/// ReadFile.lineRange: optional 1-indexed inclusive (startLine, endLine). None = entire file.
/// ListDir.depth: optional recursion depth, default 1 (non-recursive), cap 5.
///
/// Phase 3 additive amendment: lineRange and depth were added to satisfy
/// TOOL-01 (line range) and TOOL-03 (depth-limit). The amendment is additive
/// in behaviour — None preserves Phase 1/2 defaults — but IS a pattern-match
/// change for any existing callers. Phase 2 code did not construct these cases,
/// so there is no call-site impact.
type Tool =
    | ReadFile  of FilePath * lineRange: (int * int) option
    | WriteFile of FilePath * content: string
    | ListDir   of FilePath * depth: int option
    | RunShell  of Command * Timeout

/// Raw text produced by a successful tool execution.
type ToolOutput = ToolOutput of string

/// Structured outcome of a tool call. Per REQUIREMENTS.md FND-02 (updated),
/// this DU's SHAPE ships in Phase 1 (alongside the other 7 FND-02 DUs) so
/// the Tool DU is exhaustively matchable. The full SEMANTIC contract
/// (how each case is produced, security chain ordering, timeout semantics)
/// is finalized in Phase 3 as TOOL-07. Exhaustive match is required here;
/// any consumer missing a case is a compile error (Success Criterion 2).
type ToolResult =
    | Success           of output: string
    | Failure           of exitCode: int * stderr: string
    | SecurityDenied    of reason: string
    | PathEscapeBlocked of attempted: string
    | Timeout           of seconds: int

// ── LLM output ───────────────────────────────────────────────────────────────

type Thought   = Thought  of string
type ToolName  = ToolName of string
type ToolInput = ToolInput of Map<string, string>

/// Parsed LLM action (the "action" field of the JSON schema). Phase 2
/// maps the raw string to this DU after JSON parsing + schema validation.
type LlmOutput =
    | ToolCall    of ToolName * ToolInput
    | FinalAnswer of string

// ── Error domain ─────────────────────────────────────────────────────────────

/// Agent-loop errors. Every case must be a first-class value — no throwing
/// exceptions out of adapters (LLM-06, PITFALLS.md D-2, D-4).
type AgentError =
    | LlmUnreachable     of endpoint: string * detail: string
    | InvalidJsonOutput  of raw: string
    | SchemaViolation    of detail: string
    | UnknownTool        of ToolName
    | ToolFailure        of Tool * exn
    | MaxLoopsExceeded
    | LoopGuardTripped   of action: string
    | UserCancelled

// ── Step record ──────────────────────────────────────────────────────────────

type StepStatus =
    | StepSuccess
    | StepFailed  of string
    | StepAborted

/// One completed iteration of the agent loop.
/// ToolResult is Option because FinalAnswer actions do not execute a tool.
/// OBS-04 (Phase 4): StartedAt captured immediately before ILlmClient.CompleteAsync,
/// EndedAt captured immediately after IToolExecutor.ExecuteAsync returns (or LLM
/// returns for a FinalAnswer step). DurationMs = (EndedAt - StartedAt).TotalMilliseconds.
type Step = {
    StepNumber : int
    Thought    : Thought
    Action     : LlmOutput
    ToolResult : ToolResult option
    Status     : StepStatus
    ModelUsed  : Model
    StartedAt  : DateTimeOffset
    EndedAt    : DateTimeOffset
    DurationMs : int64
}

// ── Agent state machine ───────────────────────────────────────────────────────

/// All legal agent states. AwaitingApproval is reserved for an optional
/// Phase 3+ tool-approval gate; it is a valid state, not a TODO.
type AgentState =
    | AwaitingUserInput
    | PromptingLlm      of loopCount: int
    | AwaitingApproval  of Tool
    | ExecutingTool     of Tool * loopCount: int
    | Observing         of Step * loopCount: int
    | Complete          of finalAnswer: string
    | MaxLoopsHit
    | Failed            of AgentError

// ── Session result ────────────────────────────────────────────────────────────

/// Return value of a full agent session (runSession in Phase 4).
type AgentResult = {
    FinalAnswer : string
    Steps       : Step list
    LoopCount   : int
    Model       : Model
}

// ── LLM wire message (chat history primitive) ────────────────────────────────

/// Role of a single chat message in LLM wire protocol.
/// Matches OpenAI chat-completions role enum; System = system prompt,
/// User = end-user turn, Assistant = prior LLM response echoed back
/// in multi-turn context (Phase 4+).
type MessageRole =
    | System
    | User
    | Assistant

/// One chat message. Adapter-wire type used by ILlmClient and consumed
/// by QwenHttpClient to assemble the OpenAI `{role, content}` array.
/// Phase 2 adds this as an additive Core type so the port signature
/// can replace `string list` with `Message list` (LLM-01 type safety).
type Message = {
    Role    : MessageRole
    Content : string
}
