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
      NewSession: bool                                         // NEW (15-02): true when --new-session set
      WithDual35b: bool                                        // NEW (19-02): true when --with-35b set
      PlanMode: bool }                                         // NEW (16-02): true when --plan set

/// Default options — equivalent to old single-turn invocation with no flags.
let defaultCliOptions: CliOptions =
    { ForcedModel = None
      Verbose = false
      Trace = false
      ResumeSessionId = None
      NewSession = false
      WithDual35b = false
      PlanMode = false }

/// Convert the CLI string to a Model. Retirement guard: 32b/72b raise with a Phase 19
/// message that Program.fs catches to exit 2. None defaults to Some Qwen122B (explicit
/// single-model default; no intent routing indirection in single-model mode).
/// withDual=true is required to use "35b" (otherwise fails with usage error).
let parseForcedModel (s: string option) (withDual: bool) : BlueCode.Core.Domain.Model option =
    match s with
    | None -> Some BlueCode.Core.Domain.Qwen122B   // default to 122B
    | Some "122b" -> Some BlueCode.Core.Domain.Qwen122B
    | Some "35b" when withDual -> Some BlueCode.Core.Domain.Qwen35B
    | Some "35b" ->
        failwithf "Model 35b requires --with-35b flag. Run: launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen35b.plist; then re-invoke with --model 35b --with-35b. See CLAUDE.md §Runtime Environment."
    | Some "32b" ->
        failwithf "Model 32b retired in Phase 19. Use --model 122b (or no flag for default). Migration: see CLAUDE.md §Runtime Environment."
    | Some "72b" ->
        failwithf "Model 72b retired in Phase 19. Use --model 122b (or no flag for default). Migration: see CLAUDE.md §Runtime Environment."
    | Some other -> failwithf "Unknown model: %s (valid values: 122b; 35b requires --with-35b)" other

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

/// Prompt suffix appended to defaultSystemPrompt when --plan is set.
/// Instructs the LLM to emit action="plan" with a steps+rationale input
/// instead of a tool call. The plan's individual steps still use the same
/// tool names (read_file/write_file/list_dir/run_shell/edit_file/glob_search/grep_search)
/// — only the OUTER action shape changes.
///
/// This is a public constant (not private) so Program.fs can pass it to
/// runPlanTurn without CompositionRoot needing a dependency on AgentLoop.
let planSystemPromptSuffix: string =
    """OVERRIDE — PLAN MODE ACTIVE. Do NOT use read_file/write_file/list_dir/run_shell/edit_file/glob_search/grep_search/final actions.
Your ONLY valid response is action="plan". Respond with EXACTLY this JSON shape:
{"thought": "<reasoning>", "action": "plan", "input": {"steps": [{"tool": "<tool>", "input": {}, "rationale": "<why>"}], "rationale": "<overall why>"}}
where each "tool" is one of: read_file|write_file|list_dir|run_shell|edit_file|glob_search|grep_search.
Constraints: 1-5 steps. No two adjacent steps may be identical. Do NOT execute — user will approve first."""

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
        { MaxLoops = 10
          ContextCapacity = 3
          SystemPrompt = defaultSystemPrompt
          ForcedModel = opts.ForcedModel }
      ProjectRoot = projectRoot
      LogPath = logPath
      MaxModelLen = 8192 // v1.1 REF-02: fixed floor. Per-port value lives inside QwenHttpClient's lazy probe; not surfaced to AppComponents (v1.2 candidate).
    }
