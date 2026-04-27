module BlueCode.Cli.Adapters.LlmWire

open System.Text.Json

/// Wire-format record parsed from the LLM's JSON response content
/// (choices[0].message.content). Intermediate adapter-layer type;
/// not a Core domain type (lives in BlueCode.Cli, not Domain.fs).
///
/// Why a plain record (not a DU):
///   - DUs serialize as {"Case":"...","Fields":[...]} with FSharp.SystemTextJson
///   - Qwen emits {"thought":"...","action":"...","input":{...}}
///   - Records round-trip cleanly
///
/// Field rationale:
///   - thought: string — schema enforces minLength >= 1
///   - action: string — kept raw at this layer; mapped to LlmOutput DU
///     (ToolCall vs FinalAnswer) in QwenHttpClient AFTER schema validation
///     confirms it matches the enum ["read_file","write_file","list_dir","run_shell","final"]
///   - input: JsonElement — raw JSON passthrough; shape varies per tool
///     (e.g. {"path":"..."}, {"answer":"..."}). Schema validates input is an
///     object; Phase 3 tool dispatch re-parses it per action.
type LlmStep =
    { thought: string
      action: string
      input: JsonElement }

/// Wire-format record for one step inside a Plan input. Phase 16 wire
/// intermediate; converted to Domain.PlannedStep in QwenHttpClient.toLlmOutput.
/// Why a record (not Domain.PlannedStep directly): Domain.PlannedStep wraps
/// strings in single-case DUs (ToolName, ToolInput) which serialize as
/// {"Item":"..."} with FSharp.SystemTextJson. Wire stays plain.
type PlannedStepWire =
    { tool: string
      input: JsonElement
      rationale: string }

/// Wire-format record for the Plan that the LLM emits as the input
/// when action="plan". Schema (Phase 16 Json.fs additions) enforces:
///   - steps: array of PlannedStepWire
///   - rationale: non-empty string
type PlanWire =
    { steps: PlannedStepWire list
      rationale: string }
