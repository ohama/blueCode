module BlueCode.Cli.CliArgs

open Argu

/// CLI argument surface for blueCode. Extracted from Program.fs so it can be
/// unit-tested via Argu.ArgumentParser.Create<CliArgs>().ParseCommandLine.
///
/// Prompt: optional positional; when absent → REPL mode (TryGetResult Prompt = None).
/// Verbose / Trace: flags, parsed here but honoured in Plan 05-02.
/// Model: "32b" or "72b"; invalid → usage error via parseForcedModel raise.
type CliArgs =
    | [<MainCommand; Last>] Prompt of prompt: string list
    | Verbose
    | Trace
    | [<AltCommandLine("-m")>] Model of model: string

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Prompt _ -> "Prompt to send (omit for interactive REPL mode)."
            | Verbose -> "Print thought/action/input/output/status per step (default: compact one-liner)."
            | Trace -> "Emit Serilog Debug JSON per step to stderr (independent of --verbose)."
            | Model _ -> "Force model: 32b or 72b (bypasses intent classification)."
