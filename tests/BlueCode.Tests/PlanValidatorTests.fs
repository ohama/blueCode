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

              match validatePlan plan with
              | Ok p -> Expect.equal p.Steps.Length 3 "valid plan should round-trip unchanged"
              | Error e -> failtestf "Expected Ok, got Error %A" e

          testCase "PlanInvalid: unknown tool name"
          <| fun () ->
              let plan =
                  { Steps =
                      [ makePlannedStep "read_file" """{"path":"a"}""" "ok"
                        makePlannedStep "fabricate_function" """{"name":"foo"}""" "made-up tool" ]
                    Rationale = "test unknown tool path" }

              match validatePlan plan with
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

              match validatePlan plan with
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

              match validatePlan plan with
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

              match validatePlan plan with
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

              match validatePlan plan with
              | Error(PlanInvalid detail) ->
                  Expect.isTrue
                      (detail.Contains("summon_demon") || detail.ToLower().Contains("unknown"))
                      "single-step plan with unknown tool should be PlanInvalid"
              | other -> failtestf "Expected Error(PlanInvalid ...), got %A" other ]
