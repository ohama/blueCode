module BlueCode.Tests.PlanValidatorTests

open Expecto
open BlueCode.Core.Domain
open BlueCode.Core.PlanValidator
open BlueCode.Tests.MockHelpers

let tests =
    testList
        "PlanValidator.validatePlan"
        [

          testCase "valid plan: 3 distinct steps with known tools -> Ok plan"
          <| fun () ->
              let plan =
                  { Steps =
                      [ makePlannedStep "list_dir" """{"path":"."}""" "scan project root"
                        makePlannedStep "read_file" """{"path":"README.md"}""" "read overview"
                        makePlannedStep "grep_search" """{"pattern":"TODO"}""" "find todos" ]
                    Rationale = "explore then survey" }

              match validatePlan "" plan with
              | Ok p -> Expect.equal p.Steps.Length 3 "valid plan should round-trip unchanged"
              | Error e -> failtestf "Expected Ok, got Error %A" e

          testCase "PlanInvalid: unknown tool name"
          <| fun () ->
              let plan =
                  { Steps =
                      [ makePlannedStep "read_file" """{"path":"a"}""" "ok"
                        makePlannedStep "fabricate_function" """{"name":"foo"}""" "made-up tool" ]
                    Rationale = "test unknown tool path" }

              match validatePlan "" plan with
              | Error(PlanInvalid detail) ->
                  Expect.isTrue
                      (detail.Contains("fabricate_function") || detail.ToLower().Contains("unknown"))
                      "PlanInvalid detail should reference unknown tool name or be tagged 'unknown'"
              | other -> failtestf "Expected Error(PlanInvalid ...), got %A" other

          testCase "PlanInvalid: more than 10 steps (Steps.Length > MaxPlanSteps)"
          <| fun () ->
              // 11 valid steps; rule 'Steps.Length > 10' must trip BEFORE
              // adjacent-dup or unknown-tool checks (length is the cheap
              // guard, runs first per validatePlan composition).
              let plan =
                  { Steps =
                      [ makePlannedStep "list_dir" """{"path":"a"}""" "1"
                        makePlannedStep "list_dir" """{"path":"b"}""" "2"
                        makePlannedStep "list_dir" """{"path":"c"}""" "3"
                        makePlannedStep "list_dir" """{"path":"d"}""" "4"
                        makePlannedStep "list_dir" """{"path":"e"}""" "5"
                        makePlannedStep "list_dir" """{"path":"f"}""" "6"
                        makePlannedStep "list_dir" """{"path":"g"}""" "7"
                        makePlannedStep "list_dir" """{"path":"h"}""" "8"
                        makePlannedStep "list_dir" """{"path":"i"}""" "9"
                        makePlannedStep "list_dir" """{"path":"j"}""" "10"
                        makePlannedStep "list_dir" """{"path":"k"}""" "11" ]
                    Rationale = "test step-cap path" }

              match validatePlan "" plan with
              | Error(PlanInvalid detail) ->
                  Expect.isTrue
                      (detail.Contains("11") || detail.ToLower().Contains("max") || detail.ToLower().Contains("step"))
                      "PlanInvalid detail should mention step count / max"
              | other -> failtestf "Expected Error(PlanInvalid ...), got %A" other

          testCase "valid plan: exactly 10 steps passes checkLength (ceiling boundary)"
          <| fun () ->
              let plan =
                  { Steps =
                      [ makePlannedStep "list_dir" """{"path":"a"}""" "1"
                        makePlannedStep "list_dir" """{"path":"b"}""" "2"
                        makePlannedStep "list_dir" """{"path":"c"}""" "3"
                        makePlannedStep "list_dir" """{"path":"d"}""" "4"
                        makePlannedStep "list_dir" """{"path":"e"}""" "5"
                        makePlannedStep "list_dir" """{"path":"f"}""" "6"
                        makePlannedStep "list_dir" """{"path":"g"}""" "7"
                        makePlannedStep "list_dir" """{"path":"h"}""" "8"
                        makePlannedStep "list_dir" """{"path":"i"}""" "9"
                        makePlannedStep "list_dir" """{"path":"j"}""" "10" ]
                    Rationale = "test ceiling boundary pass" }

              match validatePlan "" plan with
              | Ok _ -> ()  // correct: 10 steps ≤ MaxPlanSteps (10)
              | Error e -> failtestf "Expected Ok for 10-step plan, got Error %A" e

          testCase "PlanInvalid: duplicate adjacent steps (byte-identical)"
          <| fun () ->
              // Two structurally-equal PlannedSteps adjacent in the list.
              // F# record equality (Tool + Input + Rationale fields) trips
              // the adjacent-dup detector.
              let dup = makePlannedStep "read_file" """{"path":"src/main.fs"}""" "read main"
              let plan =
                  { Steps =
                      [ makePlannedStep "list_dir" """{"path":"."}""" "scan"
                        dup
                        dup
                        makePlannedStep "grep_search" """{"pattern":"foo"}""" "search" ]
                    Rationale = "test duplicate-adjacent path" }

              match validatePlan "" plan with
              | Error(PlanInvalid detail) ->
                  Expect.isTrue
                      (detail.ToLower().Contains("duplicate") || detail.ToLower().Contains("adjacent"))
                      "PlanInvalid detail should mention duplicate / adjacent"
              | other -> failtestf "Expected Error(PlanInvalid ...), got %A" other

          testCase "PlanInvalid: empty plan with one step containing unknown tool"
          <| fun () ->
              // Single-step plan with an unknown tool — confirms the unknown-tool
              // check runs even when length and adjacency rules pass trivially.
              let plan =
                  { Steps =
                      [ makePlannedStep "summon_demon" """{"verse":"unholy"}""" "not a real tool" ]
                    Rationale = "edge case: 1 step, unknown tool" }

              match validatePlan "" plan with
              | Error(PlanInvalid detail) ->
                  Expect.isTrue
                      (detail.Contains("summon_demon") || detail.ToLower().Contains("unknown"))
                      "single-step plan with unknown tool should be PlanInvalid"
              | other -> failtestf "Expected Error(PlanInvalid ...), got %A" other

          testCase "checkRenameTargetsEnumerated: plan covering all rename targets -> Ok"
          <| fun () ->
              // Both `add` and `add3` covered via distinct edit_file steps.
              // old_string field contains the target name (case-insensitive substring).
              let plan =
                  { Steps =
                      [ makePlannedStep
                            "edit_file"
                            """{"path":"Calc.fs","old_string":"let add x y","new_string":"let sum x y"}"""
                            "rename add to sum"
                        makePlannedStep
                            "edit_file"
                            """{"path":"Calc.fs","old_string":"let add3 x y z","new_string":"let sum3 x y z"}"""
                            "rename add3 to sum3" ]
                    Rationale = "two renames, both covered" }

              match validatePlan "rename add to sum and rename add3 to sum3" plan with
              | Ok p -> Expect.equal p.Steps.Length 2 "valid plan should round-trip unchanged"
              | Error e -> failtestf "Expected Ok (both targets covered), got Error %A" e

          testCase "checkRenameTargetsEnumerated: plan missing one rename target -> PlanInvalid"
          <| fun () ->
              // Plan covers `add` but not `add3` — the exact CORR-EVAL-02 v2.2 audit FAIL pattern.
              // Validator must surface "add3" in the detail string so [PLAN INVALID] retry
              // gives the LLM specific guidance.
              let plan =
                  { Steps =
                      [ makePlannedStep
                            "edit_file"
                            """{"path":"Calc.fs","old_string":"let add x y","new_string":"let sum x y"}"""
                            "rename add to sum (only)" ]
                    Rationale = "missing add3 rename — the exact bias pattern" }

              match validatePlan "rename add to sum and rename add3 to sum3" plan with
              | Error(PlanInvalid detail) ->
                  Expect.isTrue
                      (detail.Contains("add3") || detail.ToLower().Contains("not enumerated"))
                      (sprintf
                          "PlanInvalid detail should name the missing target add3 or say 'not enumerated'; got: %s"
                          detail)
                  // Negative assertion: `add` is covered by the edit_file step (old_string contains
                  // "let add x y" → case-insensitive substring match). It must NOT appear in the
                  // missing list. This locks the coverage-check logic tightly: prevents a regression
                  // where coversTarget returns false for both targets (which would let the test pass
                  // via the OR branch above even when the heuristic is broken for `add` too).
                  Expect.isFalse
                      (detail.ToLower().Contains("not enumerated: add,")
                       || detail.ToLower() = "rename targets not enumerated: add")
                      (sprintf
                          "Plan covers `add` via edit_file step; `add` should not appear in missing list. detail=%s"
                          detail)
              | other ->
                  failtestf "Expected Error(PlanInvalid ...) for missing add3, got %A" other

          testCase "checkRenameTargetsEnumerated: prompt with no rename targets -> Ok (vacuous)"
          <| fun () ->
              // No "rename X to Y" pattern in prompt → heuristic returns empty list →
              // checkRenameTargetsEnumerated is vacuous PASS regardless of plan shape.
              // This is the gate-fixture safety case (W1/W2/B2/T1/T5/T6/MT prompts have
              // no "rename" word — confirmed by 25-RESEARCH.md Q9).
              let plan =
                  { Steps =
                      [ makePlannedStep "read_file" """{"path":"a.fs"}""" "read file" ]
                    Rationale = "non-rename task" }

              match validatePlan "Read a.fs and summarize it." plan with
              | Ok p -> Expect.equal p.Steps.Length 1 "vacuous PASS should round-trip plan unchanged"
              | Error e -> failtestf "Expected Ok (vacuous PASS — no rename in prompt), got Error %A" e ]
