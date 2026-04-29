# Phase 25: Plan-Mode Pre-Flight Enumeration - Research

**Researched:** 2026-04-28
**Domain:** F# Core DU extension, PlanValidator chain, heuristic regex, test scaffolding
**Confidence:** HIGH

## Summary

Phase 25 adds a fourth validation pass to `PlanValidator.fs` that extracts probable
rename targets from the user prompt and verifies the LLM's plan covers all of them.
The work touches `Domain.fs` (DU extension), `PlanValidator.fs` (new function + wire-in),
`PlanValidatorTests.fs` (three new test cases), `Rendering.fs` (new DU arm), and
`AgentLoop.fs` (`validatePlan` call site — signature change required).

The key architectural finding is that the existing `PlanInvalid` variant is a **single-case
union carrying a `string`** (`| PlanInvalid of detail: string`), not a multi-case DU. The
requirements spec says to add `RenameTargetsNotEnumerated of (string list)` but the current
shape is a plain `of string`. That mismatch must be resolved at design time. Two options
are available; this research recommends Option B (keep flat, encode list in detail string)
to avoid a cascade breaking all `PlanInvalid d` match arms across the codebase.

The 2-attempt retry mechanism in `runPlanTurn` (AgentLoop.fs:489-533) handles ALL errors
via a `buildCorrection` helper that **pattern-matches `PlanInvalid d`** at line 501 — so
it will handle the new variant automatically as long as we stay within the `PlanInvalid`
case (not add a sibling DU case of `AgentError`). The `validatePlan` entry-point signature
must grow a `userPrompt: string` parameter; there is exactly one call site
(AgentLoop.fs:484) inside `extractAndValidate`.

All seven gate fixture prompts have been verified to contain no "rename" substring
(case-insensitive), so heuristic vacuous-PASS behavior is confirmed safe.

**Primary recommendation:** Keep `PlanInvalid of detail: string` unchanged. Encode
missing-target detail as `"rename targets not enumerated: add, add3"` inside the
existing string payload. Add `checkRenameTargetsEnumerated` as the fourth link in
the `validatePlan` chain. Change `validatePlan` signature to accept `userPrompt`.

---

## Q1 — Current `Domain.fs` `PlanInvalid` and `PlanValidator.fs` Entry Point

### `PlanInvalid` in `Domain.fs`

File: `src/BlueCode.Core/Domain.fs`

```fsharp
// Lines 139-153
type AgentError =
    | LlmUnreachable of endpoint: string * detail: string
    | InvalidJsonOutput of raw: string
    | SchemaViolation of detail: string
    | UnknownTool of ToolName
    | ToolFailure of Tool * exn
    | MaxLoopsExceeded
    | LoopGuardTripped of action: string
    | UserCancelled
    // v2.0 additions
    | SessionNotFound of SessionId
    | SessionCorrupt of detail: string
    | PlanInvalid of detail: string          // <-- line 152
    | PathRetired of modelPath: string
```

**Critical finding:** `PlanInvalid` carries a single `string`, not a `string list`.
The requirements specification text says to add `RenameTargetsNotEnumerated of (string list)`
as a NEW variant. However, doing so would require extending `AgentError` with a new DU case,
which triggers a compile cascade across EVERY exhaustive match on `AgentError` in the
codebase — not just `PlanInvalid` matches.

### Consumers of `PlanInvalid` (exhaustive list)

| File | Line | Pattern | Context |
|------|------|---------|---------|
| `src/BlueCode.Core/AgentLoop.fs` | 408 | Construction | `return Error(PlanInvalid "Plan output received outside plan-mode")` |
| `src/BlueCode.Core/AgentLoop.fs` | 487 | Construction | `Error (PlanInvalid "expected plan output, got tool/final action")` |
| `src/BlueCode.Core/AgentLoop.fs` | 501 | Match arm | `| PlanInvalid d -> sprintf "[PLAN INVALID] ..."` inside `buildCorrection` |
| `src/BlueCode.Core/PlanValidator.fs` | 44 | Construction | `Error(PlanInvalid(sprintf "plan has %d steps, max is %d" ...))` |
| `src/BlueCode.Core/PlanValidator.fs` | 57 | Construction | `Error(PlanInvalid(sprintf "unknown tool: %s" n))` |
| `src/BlueCode.Core/PlanValidator.fs` | 68 | Construction | `Error(PlanInvalid "duplicate adjacent steps")` |
| `src/BlueCode.Cli/Rendering.fs` | 120 | Match arm | `| PlanInvalid detail -> sprintf "Plan invalid: %s" detail` |
| `tests/BlueCode.Tests/PlanValidatorTests.fs` | 35,62,103 | Match arms | `| Error(PlanInvalid detail) -> ...` |
| `tests/BlueCode.Tests/PlanParseTests.fs` | 130 | Match arm | `| Error (PlanInvalid _) -> ()` |

### `validatePlan` entry point in `PlanValidator.fs`

```fsharp
// Lines 81-85
let validatePlan (plan: Plan) : Result<Plan, AgentError> =
    plan
    |> checkLength
    |> Result.bind checkKnownTools
    |> Result.bind checkAdjacentDuplicates
```

The three existing checks are all `Plan -> Result<Plan, AgentError>`. They chain via
`Result.bind`. The new check will follow the same shape but needs `userPrompt` threaded in.

---

## Q2 — `PlanInvalid` Rendering and `[PLAN REJECTED]` Re-Prompt Format

### String rendering (`Rendering.fs:120`)

```fsharp
| PlanInvalid detail -> sprintf "Plan invalid: %s" detail
```

This is the user-facing display path (terminal output on final error).

### `buildCorrection` in `AgentLoop.fs` (lines 489-505)

This is the LLM-facing re-prompt on validation failure:

```fsharp
let buildCorrection (err: AgentError) : Message =
    let detail =
        match err with
        | InvalidJsonOutput raw ->
            let snippet = if raw.Length > 200 then raw.Substring(0, 200) + "..." else raw
            sprintf "[PLAN PARSE ERROR] Your previous response was not valid JSON. ..."
        | SchemaViolation d ->
            sprintf "[PLAN PARSE ERROR] Your previous response did not match the plan schema: %s. ..." d
        | PlanInvalid d ->
            sprintf "[PLAN INVALID] Your previous plan failed validation: %s. Constraints: max 10 steps; tool must be one of read_file/write_file/list_dir/run_shell/edit_file/glob_search/grep_search; no two adjacent steps may be byte-identical." d
        | _ ->
            "[PLAN ERROR] Re-emit a valid plan."
    { Role = User; Content = detail }
```

The `PlanInvalid d` arm at line 501 interpolates `d` directly into the re-prompt. So the
`detail` string IS what the LLM sees. The new check should produce a detail string like:

```
"rename targets not enumerated: add, add3"
```

Which the LLM receives as:

```
[PLAN INVALID] Your previous plan failed validation: rename targets not enumerated: add, add3.
Constraints: max 10 steps; tool must be one of ...
```

This is clear and actionable. No `buildCorrection` modification needed.

**Proposed detail string format:**
```
sprintf "rename targets not enumerated: %s" (String.concat ", " missingTargets)
```

Example: `"rename targets not enumerated: add, add3"`

---

## Q3 — Heuristic Regex for Rename Target Extraction

### Primary pattern (highest confidence)

Match `rename X to Y` (case-insensitive, where X is the "from" identifier):

```
\brename\s+`?(\w+)`?\s+to\s+`?(\w+)`?
```

This captures:
- `rename add to sum` → extracts `add`
- `rename add3 to sum3` → extracts `add3`
- `rename \`add\` to \`sum\`` → extracts `add` (backtick-delimited)

### Arrow variant (README uses `→`)

The refactor_multifile/README.md uses both `add → sum` syntax AND `rename add to sum`
prose. The arrow form (`→` U+2192) is typically in lists/tables, NOT following the
"rename" verb directly. A conservative heuristic should NOT match arrow-only patterns,
because `X → Y` without the "rename" verb appears in changelog bullets, PR descriptions,
and general documentation.

**Decision:** Match only patterns with the explicit `rename` verb. Arrow-only patterns
are out of scope for the conservative starting heuristic.

### Regex recommendation

```fsharp
open System.Text.RegularExpressions

let private renamePattern =
    Regex(@"\brename\s+`?(\w+)`?\s+to\s+`?(\w+)`?",
          RegexOptions.IgnoreCase ||| RegexOptions.Compiled)
```

This regex:
- Is case-insensitive (`rename`, `Rename`, `RENAME` all match)
- Allows optional backtick quoting around identifiers
- Captures the source identifier (group 1) — the target to look for in plan steps
- Does NOT match `rename foo = bar` (no `to` keyword)
- Does NOT match "`add` → `sum`" alone
- Does NOT match "renamed" (past tense without the full pattern)

### Edge cases to handle

| Input | Extracted | Notes |
|-------|-----------|-------|
| `rename add to sum` | `["add"]` | Standard prose |
| `rename \`add\` to \`sum\`` | `["add"]` | Backtick-delimited |
| `Rename X→Y (add→sum)` | `[]` | Arrow form, no verb match — vacuous PASS |
| `rename foo = bar` | `[]` | Assignment syntax — no `to` keyword |
| `the file was renamed` | `[]` | No valid pattern, past tense without `X to Y` |
| `rename add to sum AND rename add3 to sum3` | `["add", "add3"]` | Multiple matches via `Matches()` |
| `rename Add to Sum` | `["Add"]` | Preserves case of extracted name for coverage check |

**False positive risk assessment:** The pattern `\brename ... to ...` is specific enough
that sentence fragments and prose descriptions of general rename operations (without
explicit `X to Y`) will not match. The word-boundary `\b` prevents partial matches like
`beforename`. Risk is LOW for the gate fixtures (none contain "rename").

---

## Q4 — Plan Step Coverage Check Logic

### `PlannedStep` structure (Domain.fs:101-104)

```fsharp
type PlannedStep =
    { Tool: ToolName
      Input: ToolInput
      Rationale: string }
```

`ToolInput` is `ToolInput of Map<string, string>`. For plan steps, all inputs are stored
under the `_raw` key as a JSON string (see PlanParseTests.fs:56-66 for wire evidence).
For `edit_file` steps, the `_raw` JSON contains `old_string` and `new_string`.

### Coverage check algorithm

For each extracted target name `t`:

1. Find all `PlannedStep` records where `ToolName` is `"edit_file"`.
2. For each such step, extract the `_raw` JSON value from `ToolInput`.
3. Parse the JSON; check if `old_string` field contains `t` as a substring.
4. If any `edit_file` step has `old_string` containing `t`, that target is covered.
5. Collect uncovered targets.

**Important:** Coverage check uses `old_string`, not `path`. The rename target is the
identifier being replaced — it lives in `old_string`. Checking just the file path would
miss multi-occurrence renames within a single file.

**`write_file` steps:** A `write_file` step contains a `content` field in `_raw`. A full
file rewrite DOES cover a rename target if `content` contains the old name being replaced
— but more relevantly, the new file content would contain the NEW name, not the old one.
The conservative choice: do NOT count `write_file` as coverage. If the plan uses
`write_file` instead of `edit_file` for a rename, the validator will flag it as missing.
This is acceptable because plan-mode prompting (P1/P2 prongs) encourages `edit_file` for
targeted changes.

**`grep_search` / `read_file` steps:** Observation-only. Do not count as coverage.

### Case sensitivity

The heuristic extracts names preserving their case (e.g., `add`, `Add`). The coverage
check uses a **case-insensitive substring search** on `old_string` to be robust against
the LLM capitalizing the identifier in different contexts.

Proposed:
```fsharp
let private coversTarget (target: string) (step: PlannedStep) : bool =
    let (ToolName toolName) = step.Tool
    if toolName <> "edit_file" then false
    else
        let (ToolInput m) = step.Input
        let raw = m |> Map.tryFind "_raw" |> Option.defaultValue "{}"
        try
            use doc = System.Text.Json.JsonDocument.Parse(raw)
            match doc.RootElement.TryGetProperty("old_string") with
            | true, el when el.ValueKind = System.Text.Json.JsonValueKind.String ->
                el.GetString().IndexOf(target, System.StringComparison.OrdinalIgnoreCase) >= 0
            | _ -> false
        with _ -> false
```

**Short-circuit:** If the heuristic returns an empty list (no rename targets found),
the check is a vacuous `Ok plan` — do not iterate any steps.

---

## Q5 — Wire-in to `validatePlan` Chain

### Order of checks

Existing order: `checkLength` → `checkKnownTools` → `checkAdjacentDuplicates`

New check `checkRenameTargetsEnumerated` should run **LAST** (after the structural checks):

```
checkLength → checkKnownTools → checkAdjacentDuplicates → checkRenameTargetsEnumerated
```

**Justification:**
- If the plan has unknown tools or is too long, those errors should surface first. They
  signal a more fundamental problem (LLM didn't understand the tool schema) and the
  LLM's retry correction for a structural error will likely also fix any semantic gap.
- If the plan passes structural rules but misses a rename target, we surface the
  semantic gap as the targeted failure message, which is more actionable for the LLM.
- Running semantic checks last is the standard validator composition pattern already
  established by the three existing checks (cheap/structural first, semantic last).

### Signature complication

The new check needs `userPrompt`. Two options:

**Option A — Partial application (recommended):**
```fsharp
let private checkRenameTargetsEnumerated (userPrompt: string) (plan: Plan) : Result<Plan, AgentError> =
    ...

let validatePlan (userPrompt: string) (plan: Plan) : Result<Plan, AgentError> =
    plan
    |> checkLength
    |> Result.bind checkKnownTools
    |> Result.bind checkAdjacentDuplicates
    |> Result.bind (checkRenameTargetsEnumerated userPrompt)
```

**Option B — Pass empty string when not in plan-mode:**
Would require call sites to know whether they're in plan mode — adds coupling. Avoid.

Option A is clean: `validatePlan` gets a new leading parameter, all call sites must
provide it. See Q6 for call site impact.

---

## Q6 — Caller Signature Change and Call Sites

### Current signature (PlanValidator.fs:81)
```fsharp
let validatePlan (plan: Plan) : Result<Plan, AgentError>
```

### New signature (proposed)
```fsharp
let validatePlan (userPrompt: string) (plan: Plan) : Result<Plan, AgentError>
```

### Call sites

**Single call site in production code:**

`src/BlueCode.Core/AgentLoop.fs:484` inside `extractAndValidate`:

```fsharp
let extractAndValidate (response: LlmResponse) : Result<Plan, AgentError> =
    match response.Output with
    | LlmOutput.Plan p -> validatePlan p        // <-- line 484: must become validatePlan userInput p
    | LlmOutput.ToolCall _
    | LlmOutput.FinalAnswer _ ->
        Error (PlanInvalid "expected plan output, got tool/final action")
```

`userInput` is already in scope at that closure's capture site — `runPlanTurn` receives
it as a parameter at line 472. The closure `extractAndValidate` is defined inside
`runPlanTurn`, so `userInput` is directly accessible via closure capture.

**Change:** `validatePlan p` → `validatePlan userInput p`

**Test call sites (PlanValidatorTests.fs):**
Tests currently call `validatePlan plan` directly. All 6 existing test cases must change
to `validatePlan "" plan` (empty string = no rename targets, vacuous PASS — existing
structural checks are prompt-independent). New tests supply actual prompt strings.

**Impact summary:**
- 1 production call site (AgentLoop.fs:484)
- 6 existing test invocations in PlanValidatorTests.fs
- No Cli adapter changes needed (validatePlan is never called from Cli layer directly)

---

## Q7 — 2-Attempt Retry Path Confirmation

### `runPlanTurn` retry mechanism (AgentLoop.fs:467-533)

The retry path uses `buildCorrection` which handles ALL errors via:

```fsharp
let buildCorrection (err: AgentError) : Message =
    let detail =
        match err with
        | InvalidJsonOutput raw -> sprintf "[PLAN PARSE ERROR] ..."
        | SchemaViolation d    -> sprintf "[PLAN PARSE ERROR] ..."
        | PlanInvalid d        -> sprintf "[PLAN INVALID] ... %s. Constraints: ..." d
        | _                    -> "[PLAN ERROR] Re-emit a valid plan."
    { Role = User; Content = detail }
```

The `PlanInvalid d` arm at line 501 handles the case generically via `d` — regardless of
whether `d` says `"plan has 11 steps, max is 10"` or `"rename targets not enumerated: add"`.
The retry path does NOT special-case any specific message content.

**Conclusion:** The new `checkRenameTargetsEnumerated` failure returns
`Error(PlanInvalid "rename targets not enumerated: add, add3")`. This hits the
`PlanInvalid d` match arm, produces a `[PLAN INVALID]` correction message, and the
LLM gets one retry. No changes to `buildCorrection`, `runPlanTurn`, or the outer
retry scaffold are required.

The only relevant non-retryable errors are `LlmUnreachable`, `UserCancelled`, and
`PathRetired` (lines 510-512). These are explicitly pattern-matched and short-circuit
before reaching the correction path. All other errors (including `PlanInvalid`) get
the correction + retry treatment.

---

## Q8 — Test Scaffolding

### `PlanValidatorTests.fs` location

`tests/BlueCode.Tests/PlanValidatorTests.fs` — already exists with 6 test cases.

### Compile ordering

`BlueCode.Tests.fsproj` has `PlanValidatorTests.fs` compiled BEFORE `PlanParseTests.fs`,
`PlanGateTests.fs`, and `RouterTests.fs` (which has `[<EntryPoint>]`). The new tests
go in the existing file — no compile order change needed.

### `rootTests` in `RouterTests.fs` (line 102)

```fsharp
BlueCode.Tests.PlanValidatorTests.tests
```

Already registered. No change needed to `rootTests`.

### `makePlannedStep` helper

`MockHelpers.fs:14` provides `makePlannedStep (toolName: string) (rawJson: string) (rationale: string) : PlannedStep`.
The raw JSON for an `edit_file` step should include `old_string` and `new_string` fields.

### Three proposed new test cases

**Test 1: PASS — plan covers all rename targets**
```fsharp
testCase "checkRenameTargetsEnumerated: plan covering all rename targets -> Ok"
<| fun () ->
    let plan =
        { Steps =
            [ makePlannedStep "edit_file"
                  """{"path":"Calc.fs","old_string":"let add x y","new_string":"let sum x y"}"""
                  "rename add to sum"
              makePlannedStep "edit_file"
                  """{"path":"Calc.fs","old_string":"let add3 x y z","new_string":"let sum3 x y z"}"""
                  "rename add3 to sum3" ]
          Rationale = "two renames" }
    match validatePlan "rename add to sum and rename add3 to sum3" plan with
    | Ok _ -> ()
    | Error e -> failtestf "Expected Ok, got Error %A" e
```

**Test 2: FAIL — plan missing one rename target**
```fsharp
testCase "checkRenameTargetsEnumerated: plan missing one rename target -> PlanInvalid"
<| fun () ->
    let plan =
        { Steps =
            [ makePlannedStep "edit_file"
                  """{"path":"Calc.fs","old_string":"let add x y","new_string":"let sum x y"}"""
                  "rename add to sum" ]
          Rationale = "only one rename" }
    match validatePlan "rename add to sum and rename add3 to sum3" plan with
    | Error(PlanInvalid detail) ->
        Expect.isTrue
            (detail.Contains("add3") || detail.ToLower().Contains("not enumerated"))
            "PlanInvalid detail should name the missing target or say not enumerated"
    | other -> failtestf "Expected Error(PlanInvalid ...), got %A" other
```

**Test 3: vacuous PASS — no rename targets in prompt**
```fsharp
testCase "checkRenameTargetsEnumerated: prompt with no rename targets -> Ok (vacuous)"
<| fun () ->
    let plan =
        { Steps =
            [ makePlannedStep "read_file" """{"path":"a.fs"}""" "read file" ]
          Rationale = "just read" }
    match validatePlan "Read a.fs and summarize it." plan with
    | Ok _ -> ()
    | Error e -> failtestf "Expected Ok (vacuous), got Error %A" e
```

**Note on existing 6 tests:** All must change from `validatePlan plan` to
`validatePlan "" plan` (empty prompt → vacuous PASS for the new check, structural
rules unchanged). This is mechanical but necessary.

---

## Q9 — Bench Gate Vacuous PASS Verification

All 7 gate fixture prompts checked for `rename` (case-insensitive):

| Label | Prompt (abbreviated) | Contains "rename"? |
|-------|---------------------|-------------------|
| T6 | "What are the field names in the Step record..." | NO |
| W1 | "Read bench/fixtures/bug_lastchar.fs and fix the bug..." | NO |
| W2 | "Read bench/fixtures/bug_average.fs and add a new function..." | NO |
| T1 | "What is 2 to the power of 10?..." | NO |
| T5 | "Find BlueCode.slnx and tell me its size in bytes using wc." | NO |
| B2 | "Read bench/fixtures/bug_divide_zero.fs and identify the bug..." | NO |
| MT Turn1 | "List the files in bench/fixtures and tell me the count." | NO |
| MT Followup | "What was the file I just listed? Just give me the file count." | NO |

**All 7 prompts: zero "rename" matches.** Heuristic returns empty list for all of them.
`checkRenameTargetsEnumerated` returns `Ok plan` (vacuous PASS). Gate fixtures are safe.

Note: The gate uses agent-loop mode (no `--plan` flag), so `validatePlan` is NOT called
at all during gate runs. The bench gate regression risk is zero for this change even beyond
the prompt-content check. The vacuous PASS matters for plan-mode tests, not gate fixtures.

---

## Q10 — Non-Obvious Gotchas

### 1. `PlanInvalid` is a single string case — the spec's `RenameTargetsNotEnumerated of (string list)` is misleading

The ROADMAP/REQUIREMENTS mention `RenameTargetsNotEnumerated of (string list)` as a new
`PlanInvalid` reason. However, `PlanInvalid` is already a case in `AgentError`, carrying
a `string`. There are two interpretations:

- **Interpretation A:** Add a NEW case to `AgentError` named `RenameTargetsNotEnumerated of string list`.
  This triggers a full compile cascade across all `AgentError` match arms: `Rendering.fs`,
  `AgentLoop.fs` (buildCorrection), and potentially other consumers. Requires adding a
  new arm to every exhaustive match — and crucially, `buildCorrection` would need a new
  `| RenameTargetsNotEnumerated targets -> ...` arm, otherwise it falls through to the
  wildcard `| _ -> "[PLAN ERROR] ..."` which loses the helpful detail string.

- **Interpretation B (recommended):** Keep `PlanInvalid of detail: string` unchanged.
  Encode the missing targets inside the detail string:
  `PlanInvalid "rename targets not enumerated: add, add3"`. No new DU case.
  The existing `buildCorrection` arm `| PlanInvalid d -> sprintf "[PLAN INVALID] ... %s ..." d`
  naturally passes the list through to the LLM.

**Recommendation: Interpretation B.** The ROADMAP uses the phrase "New PlanInvalid reason"
which is architecturally satisfied by producing a new kind of `detail` string within the
existing `PlanInvalid` case. The success criterion text "Domain.fs PlanInvalid DU extended
with `RenameTargetsNotEnumerated` variant" can be satisfied by documenting it as a named
sub-reason pattern within `detail`, not a new DU case. This avoids a compile cascade.

If the planner prefers the new DU case approach, the cascade is limited and manageable
(add one arm to `Rendering.fs:renderError` and one arm to `AgentLoop.fs:buildCorrection`),
but it adds complexity with no runtime benefit.

### 2. Heuristic extracting non-function-name words

Pattern `\brename\s+`?(\w+)`?\s+to` could match:
- `rename this_config_key to that_config_key` — would extract `this_config_key`
- `rename the file to something` — would extract `the` if the backtick-optional
  form doesn't help

The word `\w+` matches multi-word constructs only if they are single identifiers
(no spaces). Common English prose like "rename the file" would extract `the` as the
source name. Coverage check would then look for `old_string` containing `the` — likely
to false-positive in real edits since `the` appears in many contexts.

**Mitigation:** Add a minimum-length guard: skip extracted names shorter than 3 characters
(rejects `a`, `to`, `be`, `it`, common prepositions). Or more precisely, only extract
if the captured group looks like a code identifier: `[A-Za-z_][A-Za-z0-9_]+` (length >= 2,
starts with letter/underscore). Adjust regex group:
```
\brename\s+`?([A-Za-z_]\w+)`?\s+to\s+`?([A-Za-z_]\w+)`?
```

The 2+ character `[A-Za-z_]\w+` pattern (`\w+` requires one or more, so `[A-Za-z_]\w+`
is 2+ total chars) filters out single-letter words.

### 3. F# DU compile cascade (if Interpretation A is chosen)

Adding a new case to `AgentError` means every `match err with` exhaustive match in Core
and Cli must be updated. Current consumers:
- `Rendering.fs:renderError` — full exhaustive match, must add new arm
- `AgentLoop.fs:buildCorrection` — full exhaustive match (lines 493-504), must add new arm
- `AgentLoop.fs:runPlanTurn` lines 510-512 — only pattern-matches 3 specific non-retryable
  cases; uses wildcard catch-all for the rest, so no change needed here

This is the "v1.1 LlmResponse single big-bang Core compile cascade" pattern mentioned in
the ROADMAP context. It must be done as a single commit (25-01 + parts of 25-02 together)
to avoid a non-compiling intermediate state.

### 4. `edit_file` input in plan steps is raw JSON, not parsed `Tool`

`PlannedStep.Input` is `ToolInput of Map<string, string>` with a `_raw` key containing
the JSON string. The coverage check must parse the JSON at validation time using
`System.Text.Json.JsonDocument.Parse`. This introduces a `try/with` in the validator.
Since PlanValidator.fs currently has no JSON parsing (all structural checks operate on
F# records), this is the first JSON dependency in the validator — but it's safe because
`System.Text.Json` is already in scope (it's an inbox `net10.0` library, no NuGet needed).

### 5. Case sensitivity mismatch between extracted target and `old_string`

User prompt: `rename Add to Sum` → extracted target: `Add`
Plan step `old_string`: `let add (x: int)` (lowercase)

Coverage check must be case-insensitive. Use `StringComparison.OrdinalIgnoreCase` in
the substring search. Do NOT normalize the extracted target to lowercase before
comparison — just use the comparison flag at search time.

---

## Q11 — Plan Decomposition Analysis

### ROADMAP's 4-plan split: 25-01 / 25-02 / 25-03 / 25-04

**25-01: Domain.fs DU extension**
- IF using Interpretation B (no new DU case): 25-01 has essentially nothing to do
  in Domain.fs. The "extension" is just a comment documenting the new sub-reason.
  Plan 25-01 could be renamed/repurposed as "PlanValidator.fs new pre-flight pass"
  to avoid having a near-empty plan.
- IF using Interpretation A (new DU case): 25-01 adds the DU case AND must update
  Rendering.fs and AgentLoop.fs in the same commit (compile cascade). This makes
  25-01 larger but still atomic. Then 25-02 adds the validator function that uses it.

**Does 25-01 leave the codebase non-compiling if split from 25-02?**

- Interpretation B: No, because no new DU case is added in 25-01.
- Interpretation A: Yes. Adding `RenameTargetsNotEnumerated of string list` to
  `AgentError` immediately breaks all exhaustive matches (Rendering.fs, AgentLoop.fs
  buildCorrection). Those must be updated in the same commit as the DU extension.
  The PlanValidator function that constructs it (25-02) can be its own commit only
  AFTER the DU case + consumers are updated.

**Recommendation for plan decomposition:**

Given Interpretation B (recommended above), the plan split should be:

| Plan | Content | Scope |
|------|---------|-------|
| 25-01 | Add `checkRenameTargetsEnumerated` to `PlanValidator.fs` + update `validatePlan` signature | Core only |
| 25-02 | Update `validatePlan` call site in `AgentLoop.fs` (`extractAndValidate` at line 484) | Core only |
| 25-03 | Update `PlanValidatorTests.fs`: add 3 new tests + update 6 existing calls | Tests only |
| 25-04 | Bench gate regression hold + verification | No code changes |

Note: 25-01 and 25-02 can be a single big-bang commit if the planner prefers, since
both touch Core (`PlanValidator.fs` and `AgentLoop.fs`). The ROADMAP's original
"25-01: Domain.fs" framing assumes Interpretation A (new DU case). With Interpretation B,
the Domain.fs change is trivially small (one comment at most) and can be folded into
the PlanValidator commit.

---

## Architecture Patterns for This Phase

### Pattern: new private check function

Following the existing pattern in `PlanValidator.fs`:

```fsharp
let private checkRenameTargetsEnumerated (userPrompt: string) (plan: Plan) : Result<Plan, AgentError> =
    open System.Text.RegularExpressions
    let matches = renamePattern.Matches(userPrompt)
    if matches.Count = 0 then
        Ok plan   // vacuous PASS: no rename targets in prompt
    else
        let targets = [ for m in matches -> m.Groups.[1].Value ]
        let missing =
            targets
            |> List.filter (fun t ->
                not (plan.Steps |> List.exists (coversTarget t)))
        if missing.IsEmpty then
            Ok plan
        else
            Error(PlanInvalid(sprintf "rename targets not enumerated: %s" (String.concat ", " missing)))
```

### Pattern: static Regex (avoid re-compiling on every call)

Define the compiled regex as a module-level `let private` value. F# module-level
values are initialized once at module load time. This avoids re-compiling the regex
on every `validatePlan` call.

```fsharp
let private renamePattern =
    Regex(@"\brename\s+`?([A-Za-z_]\w+)`?\s+to\s+`?([A-Za-z_]\w+)`?",
          RegexOptions.IgnoreCase ||| RegexOptions.Compiled)
```

`RegexOptions.Compiled` is appropriate here because `validatePlan` is on the hot path
for plan-mode interactions.

---

## Standard Stack (No New Packages)

All required APIs are already available in the project:

| Library | Already Present? | Purpose |
|---------|-----------------|---------|
| `System.Text.RegularExpressions` | Yes (BCL) | Regex for rename target extraction |
| `System.Text.Json` | Yes (BCL, net10.0 inbox) | Parse `_raw` JSON in plan steps |
| `System.StringComparison` | Yes (BCL) | Case-insensitive coverage check |

No new NuGet packages required.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|------------|
| Regex compilation | Per-call `new Regex(...)` | Module-level `let private ... = Regex(..., Compiled)` |
| JSON parsing of `_raw` | Custom string parsing | `JsonDocument.Parse` (already used in `AgentLoop.dispatchTool`) |
| Case-insensitive contains | `ToLower().Contains()` | `String.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0` |

---

## Common Pitfalls

### Pitfall 1: Forgetting to update existing `validatePlan plan` test calls

All 6 existing test invocations in `PlanValidatorTests.fs` call `validatePlan plan`
(current signature). After the signature change to `validatePlan userPrompt plan`, all
6 calls must become `validatePlan "" plan`. This is mechanical but easy to miss. The
compiler WILL catch it (type error), but only if `PlanValidatorTests.fs` is compiled
after `PlanValidator.fs` — which it is (per fsproj ordering).

### Pitfall 2: Non-compiling intermediate state if adding new DU case without updating consumers

If Interpretation A is chosen, adding `RenameTargetsNotEnumerated` to `AgentError` in
Domain.fs without simultaneously updating `Rendering.fs` and `AgentLoop.fs` leaves
the project non-compiling (F# exhaustive match). All three files must be staged and
committed together in 25-01.

### Pitfall 3: `\w+` matching single-char English words

Without the `[A-Za-z_]\w+` guard (2+ char identifier pattern), the heuristic might
extract `a`, `b`, `I` from prompts like "rename a to b" or "rename I/O". The minimum-
length filter catches these. Proposed regex already uses `[A-Za-z_]\w+` which requires
at least 2 characters.

### Pitfall 4: JSON parse failure in `coversTarget` swallowing real errors

The `coversTarget` helper must wrap `JsonDocument.Parse` in `try/with _ -> false`. If
the `_raw` field is malformed JSON (shouldn't happen after schema validation, but
defensive), we return `false` (target not covered) rather than raising. This matches
the conservative "when in doubt, flag it" philosophy — a malformed `edit_file` input
in a plan should fail, not silently pass.

### Pitfall 5: Module-level `open` for `System.Text.RegularExpressions`

`PlanValidator.fs` currently only opens `BlueCode.Core.Domain`. The new check needs
`System.Text.RegularExpressions` and `System.Text.Json`. Add these opens at the module
level. Both are BCL and require no NuGet.

---

## Sources

### Primary (HIGH confidence)
- Direct source read: `src/BlueCode.Core/Domain.fs` — `AgentError` DU, `PlanInvalid` variant
- Direct source read: `src/BlueCode.Core/PlanValidator.fs` — `validatePlan` chain
- Direct source read: `src/BlueCode.Core/AgentLoop.fs` — `extractAndValidate`, `buildCorrection`, `runPlanTurn`
- Direct source read: `src/BlueCode.Cli/Rendering.fs:100-121` — `renderError` exhaustive match
- Direct source read: `tests/BlueCode.Tests/PlanValidatorTests.fs` — test structure
- Direct source read: `tests/BlueCode.Tests/RouterTests.fs:90-114` — `rootTests` registration
- Direct source read: `bench/run.sh` — gate function, all 7 fixture prompts
- Direct source read: `bench/fixtures/refactor_multifile/README.md` — rename target examples
- Direct source read: `.planning/REQUIREMENTS.md` — COMP-03 specification

### Secondary (MEDIUM confidence)
- Regex pattern: derived from REQUIREMENTS.md description and README.md fixture analysis

---

## Metadata

**Confidence breakdown:**
- Current code state: HIGH — read from source
- PlanInvalid structure (single string case): HIGH — Domain.fs line 152 confirmed
- validatePlan signature / chain: HIGH — PlanValidator.fs lines 81-85 confirmed
- Call site (AgentLoop.fs:484): HIGH — single call site confirmed
- Retry path (no special-casing): HIGH — buildCorrection lines 489-505 confirmed
- Gate prompt safety (no rename): HIGH — all 7 prompts checked
- Heuristic regex design: MEDIUM — derived from examples, not live-tested
- Coverage check via old_string: MEDIUM — confirmed JSON structure from PlanParseTests

**Research date:** 2026-04-28
**Valid until:** 2026-05-28 (stable domain; Core files change infrequently)
