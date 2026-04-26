module BlueCode.Cli.CompositionRoot

open System
open BlueCode.Core.Domain
open BlueCode.Core.AgentLoop
open BlueCode.Cli.Adapters.JsonlSink

/// Wired application components for a single session. The caller owns the
/// JsonlSink lifetime via `use` (IDisposable). No DI container — explicit
/// function-injection wiring per ports-and-adapters pattern (research § Pattern 8).
type AppComponents =
    { LlmClient: BlueCode.Core.Ports.ILlmClient
      ToolExecutor: BlueCode.Core.Ports.IToolExecutor
      SessionStore: BlueCode.Core.Ports.ISessionStore  // NEW (15-02): JSONL session persistence
      JsonlSink: JsonlSink
      Config: AgentConfig
      ProjectRoot: string
      LogPath: string
      MaxModelLen: int } // OBS-03: resolved at startup via /v1/models probe (default 8192)

/// Parsed CLI arguments, consumed by CompositionRoot.bootstrap and threaded
/// into AgentConfig.ForcedModel and (in Plan 05-02) into Repl.runSingleTurn's
/// RenderMode parameter. TraceMode is recorded here but not acted upon until
/// Plan 05-02 flips the Serilog LoggingLevelSwitch.
type CliOptions =
    { ForcedModel: BlueCode.Core.Domain.Model option
      Verbose: bool
      Trace: bool
      ResumeSessionId: BlueCode.Core.Domain.SessionId option  // NEW (15-02): Some when --resume <ID> set
      NewSession: bool }                                       // NEW (15-02): true when --new-session set

/// Default options — equivalent to old single-turn invocation with no flags.
let defaultCliOptions: CliOptions =
    { ForcedModel = None
      Verbose = false
      Trace = false
      ResumeSessionId = None
      NewSession = false }

/// Convert the CLI string ("32b"|"72b") to a Model. Raises on invalid input
/// so Argu-level catch in Program.fs can surface it as a usage error (exit 2).
let parseForcedModel (s: string option) : BlueCode.Core.Domain.Model option =
    match s with
    | None -> None
    | Some "32b" -> Some BlueCode.Core.Domain.Qwen32B
    | Some "72b" -> Some BlueCode.Core.Domain.Qwen72B
    | Some other -> failwithf "Unknown model: %s (valid values: 32b, 72b)" other

/// Default system prompt for Phase 4. Tells Qwen to respond with strict JSON
/// matching the LLM step schema. Phase 5 may extend this (include CLAUDE.md
/// discovery, etc.) but Phase 4 keeps it minimal.
///
/// Matches the 8-action enum in Plan 08-01's llmStepSchema: "read_file",
/// "write_file", "list_dir", "run_shell", "edit_file", "glob_search",
/// "grep_search", "final".
let private defaultSystemPrompt: string =
    """You are blueCode, a coding agent driven by an F# recursive loop.

Respond with strict JSON only: {"thought": "<reasoning>", "action": "<one of: read_file | write_file | list_dir | run_shell | edit_file | glob_search | grep_search | final>", "input": {...}}

Inputs by action:
- read_file:   {path, start_line?, end_line?}
- write_file:  {path, content}
- list_dir:    {path, depth?}
- run_shell:   {command, timeout_ms?}
- edit_file:   {path, old_string(non-empty exact file content), new_string}
- glob_search: {pattern, path?}
- grep_search: {pattern, path?, file_glob?}
- final:       {"answer": "<text>"}

Rules: One tool per response. Use grep_search to locate symbols before reading large files. When done, respond with action="final". No prose, no markdown — JSON object only."""

/// Construct the component graph synchronously. No HTTP calls at startup — the
/// /v1/models probe is lazy and fires on the first LLM call to each port, owned
/// by QwenHttpClient.create. The caller (Program.fs) owns the returned
/// AppComponents.JsonlSink with 'use' to ensure Dispose flushes the session log.
let bootstrap (projectRoot: string) (opts: CliOptions) : AppComponents =
    let logPath = buildSessionLogPath ()

    { LlmClient = Adapters.QwenHttpClient.create ()
      ToolExecutor = Adapters.FsToolExecutor.create projectRoot
      SessionStore = BlueCode.Cli.Adapters.FileSessionStore.FileSessionStore() :> BlueCode.Core.Ports.ISessionStore
      JsonlSink = new JsonlSink(logPath)
      Config =
        { MaxLoops = 5
          ContextCapacity = 3
          SystemPrompt = defaultSystemPrompt
          ForcedModel = opts.ForcedModel }
      ProjectRoot = projectRoot
      LogPath = logPath
      MaxModelLen = 8192 // v1.1 REF-02: fixed floor. Per-port value lives inside QwenHttpClient's lazy probe; not surfaced to AppComponents (v1.2 candidate).
    }
