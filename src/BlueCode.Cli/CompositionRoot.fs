module BlueCode.Cli.CompositionRoot

open System
open BlueCode.Core.Domain
open BlueCode.Core.AgentLoop
open BlueCode.Cli.Adapters.JsonlSink

/// Wired application components for a single session. The caller owns the
/// JsonlSink lifetime via `use` (IDisposable). No DI container — explicit
/// function-injection wiring per ports-and-adapters pattern (research § Pattern 8).
type AppComponents = {
    LlmClient    : BlueCode.Core.Ports.ILlmClient
    ToolExecutor : BlueCode.Core.Ports.IToolExecutor
    JsonlSink    : JsonlSink
    Config       : AgentConfig
    ProjectRoot  : string
    LogPath      : string
}

/// Parsed CLI arguments, consumed by CompositionRoot.bootstrap and threaded
/// into AgentConfig.ForcedModel and (in Plan 05-02) into Repl.runSingleTurn's
/// RenderMode parameter. TraceMode is recorded here but not acted upon until
/// Plan 05-02 flips the Serilog LoggingLevelSwitch.
type CliOptions = {
    ForcedModel : BlueCode.Core.Domain.Model option
    Verbose     : bool
    Trace       : bool
}

/// Default options — equivalent to old single-turn invocation with no flags.
let defaultCliOptions : CliOptions = {
    ForcedModel = None
    Verbose     = false
    Trace       = false
}

/// Convert the CLI string ("32b"|"72b") to a Model. Raises on invalid input
/// so Argu-level catch in Program.fs can surface it as a usage error (exit 2).
let parseForcedModel (s: string option) : BlueCode.Core.Domain.Model option =
    match s with
    | None -> None
    | Some "32b" -> Some BlueCode.Core.Domain.Qwen32B
    | Some "72b" -> Some BlueCode.Core.Domain.Qwen72B
    | Some other ->
        failwithf "Unknown model: %s (valid values: 32b, 72b)" other

/// Default system prompt for Phase 4. Tells Qwen to respond with strict JSON
/// matching the LLM step schema. Phase 5 may extend this (include CLAUDE.md
/// discovery, etc.) but Phase 4 keeps it minimal.
///
/// Matches the 5-action enum in Plan 02-02's llmStepSchema: "read_file",
/// "write_file", "list_dir", "run_shell", "final".
let private defaultSystemPrompt : string =
    """You are blueCode, a coding agent driven by an F# recursive loop.

Every response MUST be strict JSON of this shape:
{"thought": "<your reasoning>", "action": "<one of: read_file | write_file | list_dir | run_shell | final>", "input": {<action-specific fields>}}

Action input schemas:
- read_file:  {"path": "<rel-path>", "start_line": <int?>, "end_line": <int?>}
- write_file: {"path": "<rel-path>", "content": "<full-new-content>"}
- list_dir:   {"path": "<rel-path>", "depth": <int?>}
- run_shell:  {"command": "<bash>", "timeout_ms": <int?>}
- final:      {"answer": "<your final answer to the user>"}

Rules:
- One tool per response. No chaining.
- When you have enough information, respond with action="final".
- No markdown, no prose around the JSON. Respond with the object only."""

/// Construct the component graph. The caller (Program.fs) owns the returned
/// AppComponents.JsonlSink with `use` to ensure Dispose flushes the session log.
let bootstrap (projectRoot: string) (opts: CliOptions) : AppComponents =
    let logPath = buildSessionLogPath()
    {
        LlmClient    = Adapters.QwenHttpClient.create()
        ToolExecutor = Adapters.FsToolExecutor.create projectRoot
        JsonlSink    = new JsonlSink(logPath)
        Config       = {
            MaxLoops        = 5
            ContextCapacity = 3
            SystemPrompt    = defaultSystemPrompt
            ForcedModel     = opts.ForcedModel
        }
        ProjectRoot  = projectRoot
        LogPath      = logPath
    }
