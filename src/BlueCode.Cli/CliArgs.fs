module BlueCode.Cli.CliArgs

open Argu

/// CLI argument surface for blueCode. Extracted from Program.fs so it can be
/// unit-tested via Argu.ArgumentParser.Create<CliArgs>().ParseCommandLine.
///
/// Prompt: optional positional; when absent → REPL mode (TryGetResult Prompt = None).
/// Verbose / Trace: flags, parsed here but honoured in Plan 05-02.
/// Model: "122b" (default) or "35b" (requires --with-35b); 32b/72b retired in Phase 19.
/// Resume: load a prior session by id from ~/.bluecode/sessions/<id>.jsonl.
/// NewSession: force a fresh session id (mutually exclusive with --resume;
///   checked post-parse in Program.fs — Argu has no clean cross-flag exclusion).
/// WithDual: enable dual-mode (--model 35b allowed; requires 35B service to be loaded).
type CliArgs =
    | [<MainCommand; Last>] Prompt of prompt: string list
    | Verbose
    | Trace
    | [<AltCommandLine("-m")>] Model of model: string
    | Resume of id: string              // --resume <ID>; loads ~/.bluecode/sessions/<ID>.jsonl
    | [<AltCommandLine("--new-session")>] NewSession  // --newsession / --new-session; forces a fresh session id
    | [<AltCommandLine("--with-35b")>] WithDual       // --withdual / --with-35b; enables --model 35b
    | Plan                                             // NEW (Phase 16-02): --plan flag; plan-then-execute mode
    | [<AltCommandLine("--allow-paths")>] AllowPaths of paths: string   // NEW (Phase 36-02): comma-separated extra-allowed path prefixes

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Prompt _ -> "Prompt to send (omit for interactive REPL mode)."
            | Verbose -> "Print thought/action/input/output/status per step (default: compact one-liner)."
            | Trace -> "Emit Serilog Debug JSON per step to stderr (independent of --verbose)."
            | Model _ -> "Force model: 122b (default), 35b (requires --with-35b). 32b/72b retired in Phase 19."
            | Resume _ -> "Resume session by ID. Reads ~/.bluecode/sessions/<ID>.jsonl and continues with prior context."
            | NewSession -> "Force a fresh session id. Mutually exclusive with --resume."
            | WithDual -> "Enable dual-mode (--model 35b allowed; requires launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen35b.plist)"
            | AllowPaths _ -> "Comma-separated extra paths the agent may read/write (canonicalized; trailing-separator prefix-attack guarded). Default: empty (project root only)."
            | Plan -> "Plan-then-execute mode: LLM emits a plan; user approves a/r/e/q before any tool runs. Single-turn only (REPL plan-mode is v2.1+)."
