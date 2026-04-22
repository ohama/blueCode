module BlueCode.Core.ToolRegistry

open BlueCode.Core.Domain

/// Minimal stub for Phase 1 / Plan 01-03. The real registry in Phase 3
/// (TOOL-01 through TOOL-04) maps ToolName to a dispatcher function that
/// produces Tool values with validated parameters. This stub keeps the
/// module present in the compile graph so Phase 2 and Phase 3 can
/// extend it without restructuring the .fsproj compile order.
type ToolRegistry = private ToolRegistry of Map<ToolName, Tool>

/// An empty registry. Phase 3 replaces this with a populated one.
let empty : ToolRegistry = ToolRegistry Map.empty
