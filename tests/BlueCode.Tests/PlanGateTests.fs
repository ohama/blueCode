module BlueCode.Tests.PlanGateTests

open System
open System.IO
open Expecto
open BlueCode.Core.Domain
open BlueCode.Cli.PlanGate
open BlueCode.Tests.MockHelpers

/// Build a scripted IKeyReader that returns chars from `keys` in order
/// for ReadKey, and lines from `lines` in order for ReadLine.
/// Exhaustion of either list is a programming error in the test (returns
/// space char / empty string and the test should fail by assertion).
let private scriptedReader (keys: char list) (lines: string list) : IKeyReader =
    let mutable kq = keys
    let mutable lq = lines
    { new IKeyReader with
        member _.ReadKey () =
            match kq with
            | [] -> ' '
            | k :: rest -> kq <- rest; k
        member _.ReadLine () =
            match lq with
            | [] -> ""
            | l :: rest -> lq <- rest; l }

let private samplePlan : Plan =
    { Steps = [
        makePlannedStep "read_file" "{\"path\":\"a.fs\"}" "read first"
        makePlannedStep "list_dir" "{\"path\":\"src\"}" "then list"
      ]
      Rationale = "explore then enumerate" }

/// Redirect Console.Out to a StringWriter, run f(), restore, return (result, captured).
/// Must be used inside testSequenced (CLAUDE.md: Console.SetOut redirection races in parallel).
let private withCapturedStdout (f: unit -> 'a) : 'a * string =
    let prev = Console.Out
    use sw = new StringWriter()
    Console.SetOut(sw)
    try
        let result = f ()
        Console.Out.Flush()
        (result, sw.ToString())
    finally
        Console.SetOut(prev)

let tests =
    testSequenced (testList "PlanGateTests" [

        testCase "promptUser: 'a' -> Accept" <| fun () ->
            let reader = scriptedReader [ 'a' ] []
            let (decision, stdout) = withCapturedStdout (fun () -> promptUser reader)
            Expect.equal decision Accept "decision is Accept"
            Expect.stringContains stdout "Accepted." "stdout reports Accepted"

        testCase "promptUser: 'r' -> Reject" <| fun () ->
            let reader = scriptedReader [ 'r' ] []
            let (decision, stdout) = withCapturedStdout (fun () -> promptUser reader)
            Expect.equal decision Reject "decision is Reject"
            Expect.stringContains stdout "Rejected" "stdout reports Rejected"

        testCase "promptUser: 'q' -> Quit" <| fun () ->
            let reader = scriptedReader [ 'q' ] []
            let (decision, _) = withCapturedStdout (fun () -> promptUser reader)
            Expect.equal decision Quit "decision is Quit"

        testCase "promptUser: 'e' captures comment via ReadLine" <| fun () ->
            let reader = scriptedReader [ 'e' ] [ "use grep_search instead of read_file" ]
            let (decision, _) = withCapturedStdout (fun () -> promptUser reader)
            match decision with
            | Edit comment ->
                Expect.equal comment "use grep_search instead of read_file" "comment captured verbatim"
            | other -> failtestf "expected Edit, got %A" other

        testCase "promptUser: unknown key 'x' re-prompts; then 'a' -> Accept" <| fun () ->
            let reader = scriptedReader [ 'x'; 'a' ] []
            let (decision, stdout) = withCapturedStdout (fun () -> promptUser reader)
            Expect.equal decision Accept "second key 'a' wins"
            Expect.stringContains stdout "Unrecognized" "warning emitted for 'x'"
            Expect.stringContains stdout "Accepted" "final 'a' confirms"

        testCase "render: emits rationale top-line + a/r/e/q prompt" <| fun () ->
            let (_, stdout) = withCapturedStdout (fun () -> render samplePlan)
            Expect.stringContains stdout "Proposed plan: explore then enumerate" "top rationale visible"
            Expect.stringContains stdout "[a]ccept" "approval prompt visible"
            Expect.stringContains stdout "[q]uit" "quit option visible"

    ])
