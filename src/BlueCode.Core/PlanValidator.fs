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
open System
open System.Text.Json
open System.Text.RegularExpressions

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

// ── Rename target heuristic (COMP-03 / Phase 25 P3) ──────────────────────────

/// Conservative regex matching "rename X to Y" prose. Captures the source identifier (group 1).
/// Requires 2+ char identifier starting with letter or underscore (filters single-letter
/// English words like "rename a to b" from extracting "a"). Allows optional backtick
/// quoting around either identifier. Compiled at module load — cached, no per-call cost.
let private renamePattern =
    Regex(@"\brename\s+`?([A-Za-z_]\w+)`?\s+to\s+`?([A-Za-z_]\w+)`?",
          RegexOptions.IgnoreCase ||| RegexOptions.Compiled)

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

/// Returns true iff the given step is an edit_file whose old_string contains
/// `target` as a case-insensitive substring. Other tool names always return false.
/// JSON parse failures are conservatively treated as "not covered" (defensive — the
/// schema validator at JSON parse time should catch malformed _raw before we get here).
let private coversTarget (target: string) (step: PlannedStep) : bool =
    let (ToolName toolName) = step.Tool
    if toolName <> "edit_file" then false
    else
        let (ToolInput m) = step.Input
        let raw = m |> Map.tryFind "_raw" |> Option.defaultValue "{}"
        try
            use doc = JsonDocument.Parse(raw)
            match doc.RootElement.TryGetProperty("old_string") with
            | true, el when el.ValueKind = JsonValueKind.String ->
                el.GetString().IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0
            | _ -> false
        with _ -> false

/// Pre-flight semantic check (COMP-03 / Phase 25 P3): extract probable rename
/// targets from userPrompt via the renamePattern regex, then verify each is
/// covered by some edit_file step in the plan. Vacuous PASS if userPrompt
/// contains no "rename X to Y" pattern.
let private checkRenameTargetsEnumerated (userPrompt: string) (plan: Plan) : Result<Plan, AgentError> =
    let matches = renamePattern.Matches(userPrompt)
    if matches.Count = 0 then
        Ok plan   // vacuous PASS: no rename targets in prompt
    else
        let targets =
            [ for m in matches -> m.Groups.[1].Value ]
            |> List.distinct
        let missing =
            targets
            |> List.filter (fun t ->
                not (plan.Steps |> List.exists (coversTarget t)))
        if List.isEmpty missing then
            Ok plan
        else
            Error(PlanInvalid(sprintf "rename targets not enumerated: %s" (String.concat ", " missing)))

// ── Public entry point ───────────────────────────────────────────────────────

/// Validate a Plan against the structural rules. Returns Error on first rule violation;
/// short-circuits in priority order (length → tool registry → adjacent dups →
/// rename-targets-enumerated) so the LLM gets a stable error code for retry messaging.
///
/// userPrompt: the original user input for this plan turn. Used by the
/// checkRenameTargetsEnumerated semantic check (COMP-03/Phase 25 P3) to extract
/// probable rename targets via regex and verify plan coverage. Pass "" to opt out
/// (the heuristic returns empty list on empty input → vacuous PASS).
///
/// Note: schema-invalid input is checked at JSON parse time in the Cli adapter,
/// not here (see module docstring).
let validatePlan (userPrompt: string) (plan: Plan) : Result<Plan, AgentError> =
    plan
    |> checkLength
    |> Result.bind checkKnownTools
    |> Result.bind checkAdjacentDuplicates
    |> Result.bind (checkRenameTargetsEnumerated userPrompt)
