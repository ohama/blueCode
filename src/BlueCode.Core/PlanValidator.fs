/// PlanValidator — pure-function validation for v2.0 Plan values (PLAN-04).
///
/// Lives in Core because the rules are pure: tool-name set, step count, and
/// adjacent-duplicate detection require no I/O. The Phase 16 Cli wiring
/// (--plan flag + retry path) calls validatePlan after parsing the LLM
/// response, before showing the plan to the user.
///
/// Out of scope for this validator (per planning_context):
///   - JSON-schema validation of step.Input against per-tool schemas.
///     That requires schema awareness which lives in the Cli adapter
///     (LlmWire.fs) and is enforced at JSON parse time, before a Plan
///     value is ever constructed. The Plan reaching validatePlan has
///     already passed structural JSON parse.
module BlueCode.Core.PlanValidator

open BlueCode.Core.Domain

// ── Known tool names (PLAN-04 rule 2) ────────────────────────────────────────

/// Set of tool names recognized by the agent loop. Mirrors the cases in
/// AgentLoop.dispatchTool — keep these in sync. Phase 14 hardcodes this
/// set; a future cycle could derive it from a richer ToolRegistry.
let private knownTools : Set<string> =
    Set.ofList [
        "read_file"
        "write_file"
        "list_dir"
        "run_shell"
        "edit_file"
        "glob_search"
        "grep_search"
    ]

// ── Validation primitives ────────────────────────────────────────────────────

/// Maximum number of steps a plan may contain. Mirrors AgentConfig.MaxLoops
/// (LOOP-01 default 10). Hardcoded here because Plan validation is not
/// AgentConfig-aware (it's invoked from QwenHttpClient parse layer in
/// Phase 16, which doesn't see AgentConfig).
let MaxPlanSteps = 10

let private checkLength (plan: Plan) : Result<Plan, AgentError> =
    if plan.Steps.Length > MaxPlanSteps then
        Error(PlanInvalid(sprintf "plan has %d steps, max is %d" plan.Steps.Length MaxPlanSteps))
    else
        Ok plan

let private checkKnownTools (plan: Plan) : Result<Plan, AgentError> =
    let unknown =
        plan.Steps
        |> List.tryFind (fun s ->
            let (ToolName n) = s.Tool
            not (Set.contains n knownTools))
    match unknown with
    | Some step ->
        let (ToolName n) = step.Tool
        Error(PlanInvalid(sprintf "unknown tool: %s" n))
    | None -> Ok plan

let private checkAdjacentDuplicates (plan: Plan) : Result<Plan, AgentError> =
    // Compare each step to its successor. PlannedStep is a record of
    // (ToolName, ToolInput, Rationale) — F# structural equality on
    // records compares all fields, including the Map<string,string>
    // wrapped in ToolInput. Adjacent equality is the rule per PLAN-04.
    let rec loop xs =
        match xs with
        | a :: b :: _ when a = b ->
            Error(PlanInvalid "duplicate adjacent steps")
        | _ :: rest -> loop rest
        | [] -> Ok plan
    loop plan.Steps

// ── Public entry point ───────────────────────────────────────────────────────

/// Validate a Plan against the structural rules. Returns Error on first rule violation;
/// short-circuits in priority order (length → tool registry → adjacent dups)
/// so the LLM gets a stable error code for retry messaging.
///
/// Note: schema-invalid input is checked at JSON parse time in the Cli adapter,
/// not here (see module docstring).
let validatePlan (plan: Plan) : Result<Plan, AgentError> =
    plan
    |> checkLength
    |> Result.bind checkKnownTools
    |> Result.bind checkAdjacentDuplicates
