module BlueCode.Cli.Adapters.Logging

open System
open Serilog
open Serilog.Events

/// Initialize the static Serilog Log.Logger. Must be called ONCE at process
/// startup BEFORE any Log.* call — Serilog's default is a silent no-op logger.
///
/// Configuration (OBS-02):
///   - Minimum level: Debug (captures most useful info without verbose noise)
///   - Sink: Console, but stderr for ALL events (standardErrorFromLevel = Verbose)
///   - Rationale: Spectre.Console writes spinners and panels to stdout; keeping
///     Serilog on stderr prevents UI/log interleaving (research § Pattern 6).
///
/// Output format: `[LVL] <message>` — compact for terminal readability. Phase 5
/// may add a JSON output template for `--trace` (CLI-07) as a separate sink.
let configure () : unit =
    Log.Logger <-
        LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                standardErrorFromLevel = System.Nullable<LogEventLevel>(LogEventLevel.Verbose),
                outputTemplate = "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger()

/// Flush and dispose the global logger. Call in Program.fs before exit to
/// ensure all pending events are written (Serilog buffers by default).
let shutdown () : unit =
    Log.CloseAndFlush()
