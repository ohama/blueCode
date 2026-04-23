module BlueCode.Cli.Adapters.JsonlSink

open System
open System.IO
open System.Text
open System.Text.Json
open BlueCode.Core.Domain
open BlueCode.Cli.Adapters.Json  // jsonOptions singleton (Phase 2)

/// Compute the session JSONL path:
///   ~/.bluecode/session_<yyyy-MM-ddTHH-mm-ssZ>.jsonl
/// Colons are replaced with hyphens because Windows/macOS filesystems treat
/// ':' differently in filenames; avoiding it is cross-platform safe.
/// Creates the ~/.bluecode directory if it does not exist.
let buildSessionLogPath () : string =
    let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    let dir  = Path.Combine(home, ".bluecode")
    Directory.CreateDirectory(dir) |> ignore
    let ts   = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ")
    Path.Combine(dir, sprintf "session_%s.jsonl" ts)

/// Append-only JSONL writer. One line per completed Step. StreamWriter held open
/// for the lifetime of a session — disposed via `use` in CompositionRoot.
///
/// CRITICAL INVARIANTS (SC-6 — "readable after the process exits"):
///   - AutoFlush = true: every WriteLine is immediately flushed to the OS buffer.
///   - append = true: re-opening an existing file path does not truncate.
///   - UTF-8 encoding: explicit, not platform-default.
///
/// Usage:
///   use sink = new JsonlSink(buildSessionLogPath())
///   sink.WriteStep step
///   // ... process may crash; steps written so far are durable.
type JsonlSink(path: string) =
    let writer = new StreamWriter(path, append = true, encoding = Encoding.UTF8)
    do writer.AutoFlush <- true

    /// Path this sink is writing to. Exposed for logging/debugging.
    member _.Path : string = path

    /// Serialize `step` with the Phase 2 `jsonOptions` (FSharp.SystemTextJson
    /// converter registered — records, DUs, and DateTimeOffset fields serialize
    /// cleanly). Append one line.
    member _.WriteStep (step: Step) : unit =
        let line = JsonSerializer.Serialize(step, jsonOptions)
        writer.WriteLine(line)

    interface IDisposable with
        member _.Dispose() =
            writer.Flush()
            writer.Dispose()
