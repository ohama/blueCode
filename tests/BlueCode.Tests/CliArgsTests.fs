module BlueCode.Tests.CliArgsTests

open Expecto
open Argu
open BlueCode.Cli.CliArgs
open BlueCode.Cli.CompositionRoot
open BlueCode.Core.Domain

// ── Helpers ──────────────────────────────────────────────────────────────────

/// Create a parser for CliArgs. Argu auto-registers --help/-h.
let private parser = ArgumentParser.Create<CliArgs>(programName = "blueCode")

/// Parse argv with raiseOnUsage = true (same as Program.fs).
let private parse (argv: string array) =
    parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)

// ── Tests ─────────────────────────────────────────────────────────────────────

// Note: NO [<Tests>] attribute — this project uses explicit rootTests registration
// in RouterTests.fs. See STATE.md Accumulated Decisions (04-02).

let tests =
    testList
        "CliArgs"
        [

          // 1. Empty argv → no prompt → REPL mode trigger
          testCase "empty argv: TryGetResult Prompt = None (REPL mode)"
          <| fun () ->
              let results = parse [||]
              Expect.equal (results.TryGetResult Prompt) None "no positional args should yield None for Prompt"

          // 2. Single quoted word positional
          testCase "single positional: TryGetResult Prompt = Some [\"hello world\"]"
          <| fun () ->
              let results = parse [| "hello world" |]

              Expect.equal
                  (results.TryGetResult Prompt)
                  (Some [ "hello world" ])
                  "single positional token captured as list element"

          // 3. Multi-word unquoted positional (separate argv tokens → MainCommand; Last collects them)
          testCase "multi-word positional: Prompt = Some [\"list\"; \"files\"; \"in\"; \".\"]"
          <| fun () ->
              let results = parse [| "list"; "files"; "in"; "." |]

              Expect.equal
                  (results.TryGetResult Prompt)
                  (Some [ "list"; "files"; "in"; "." ])
                  "unquoted multi-word tokens all collected by MainCommand; Last"

          // 4. --verbose with positional
          testCase "--verbose with prompt: Contains Verbose = true AND Prompt = Some [\"hi\"]"
          <| fun () ->
              let results = parse [| "--verbose"; "hi" |]
              Expect.isTrue (results.Contains Verbose) "--verbose flag present"

              Expect.equal
                  (results.TryGetResult Prompt)
                  (Some [ "hi" ])
                  "positional prompt captured alongside --verbose"

          // 5. --trace with positional
          testCase "--trace with prompt: Contains Trace = true"
          <| fun () ->
              let results = parse [| "--trace"; "hi" |]
              Expect.isTrue (results.Contains Trace) "--trace flag present"

          // 6. --model 72b
          testCase "--model 72b: TryGetResult Model = Some \"72b\""
          <| fun () ->
              let results = parse [| "--model"; "72b"; "hi" |]
              Expect.equal (results.TryGetResult Model) (Some "72b") "--model value captured as string"

          // 7. -m alias for --model
          testCase "-m 32b alias: TryGetResult Model = Some \"32b\""
          <| fun () ->
              let results = parse [| "-m"; "32b"; "hi" |]
              Expect.equal (results.TryGetResult Model) (Some "32b") "-m is registered as AltCommandLine for --model"

          // 8. All flags together
          testCase "--verbose --trace --model 72b + prompt: all present"
          <| fun () ->
              let results = parse [| "--verbose"; "--trace"; "--model"; "72b"; "hi" |]
              Expect.isTrue (results.Contains Verbose) "--verbose present"
              Expect.isTrue (results.Contains Trace) "--trace present"
              Expect.equal (results.TryGetResult Model) (Some "72b") "--model 72b present"
              Expect.equal (results.TryGetResult Prompt) (Some [ "hi" ]) "Prompt = [\"hi\"]"

          // 9. parseForcedModel round-trips (Task 3 signature; Task 7 replaces these with retirement tests)
          testCase "parseForcedModel None defaults to Qwen122B"
          <| fun () -> Expect.equal (parseForcedModel None false) (Some Qwen122B) "None → Some Qwen122B (explicit single-model default)"

          testCase "parseForcedModel Some 122b returns Qwen122B"
          <| fun () -> Expect.equal (parseForcedModel (Some "122b") false) (Some Qwen122B) "\"122b\" string maps to Qwen122B"

          testCase "parseForcedModel Some 32b throws retirement error (temporary; Task 7 elaborates)"
          <| fun () ->
              Expect.throws
                  (fun () -> parseForcedModel (Some "32b") false |> ignore)
                  "32b is retired in Phase 19"

          testCase "parseForcedModel Some 72b throws retirement error (temporary; Task 7 elaborates)"
          <| fun () ->
              Expect.throws
                  (fun () -> parseForcedModel (Some "72b") false |> ignore)
                  "72b is retired in Phase 19"

          // 10. parseForcedModel on unknown raises
          testCase "parseForcedModel (Some \"unknown\") raises"
          <| fun () ->
              Expect.throws
                  (fun () -> parseForcedModel (Some "unknown") false |> ignore)
                  "invalid model string should raise an exception"

          // 11. --help raises ArguParseException (usage text in message)
          testCase "--help raises ArguParseException"
          <| fun () ->
              Expect.throws
                  (fun () -> parse [| "--help" |] |> ignore)
                  "--help should raise ArguParseException (caught in Program.fs → exit 2)"

          ]
