module BlueCode.Cli.Adapters.Json

open System.Text.Json
open System.Text.Json.Serialization

/// Shared options for ALL JSON serialization/deserialization in the CLI
/// adapter layer. Built once at module-init; passed to every JsonSerializer
/// call. DO NOT create JsonSerializerOptions inline at call sites — it is
/// expensive (reflection-based converter discovery) and silently creates
/// freezable options that race with first use.
///
/// JsonFSharpConverter registered with default JsonFSharpOptions configures
/// F#-idiomatic behavior:
///   - option types: None -> null, Some v -> v (not {"Case":"Some","Fields":[v]})
///   - F# list -> JSON array
///   - F# Map -> JSON object
///   - DU cases get F#-friendly naming
///
/// Do NOT create JsonSerializerOptions inline at call sites — use this
/// singleton instead. Do NOT combine with JsonSerializerOptions.Strict in
/// Phase 2; schema validation in Plan 02-02 enforces additionalProperties: false.
let jsonOptions : JsonSerializerOptions =
    let opts = JsonSerializerOptions()
    opts.Converters.Add(JsonFSharpConverter())
    opts
