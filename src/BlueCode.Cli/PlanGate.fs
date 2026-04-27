module BlueCode.Cli.PlanGate

open System
open Spectre.Console
open BlueCode.Core.Domain

/// Outcome of the user's a/r/e/q decision at the plan approval gate.
/// Edit carries the user's comment for inclusion in the next runPlanTurn invocation.
type PlanGateDecision =
    | Accept
    | Reject
    | Edit of comment: string
    | Quit

/// Abstraction over keystroke input so PlanGateTests can supply scripted
/// inputs without invoking the real Console. Production wiring uses
/// realKeyReader below; tests construct a record literal.
type IKeyReader =
    /// Returns the lower-case keystroke character ('a','r','e','q' or other).
    /// May block until a key is pressed.
    abstract member ReadKey : unit -> char
    /// Reads a full line (used after Edit keystroke to capture the comment).
    abstract member ReadLine : unit -> string

/// Production reader: blocking Console.ReadKey + Console.In.ReadLine.
/// CLAUDE.md note: ReadKey(intercept=true) prevents the typed char from
/// being echoed; we manually echo a newline via stdout for readability.
let realKeyReader : IKeyReader =
    { new IKeyReader with
        member _.ReadKey () =
            let info = Console.ReadKey(intercept = true)
            Char.ToLowerInvariant info.KeyChar
        member _.ReadLine () =
            // Use Console.In rather than Console.ReadLine() so SetIn redirection in tests works.
            Console.In.ReadLine() |> Option.ofObj |> Option.defaultValue "" }

/// Render a Plan as a Spectre.Console table to stdout. PUBLIC for testability.
/// Columns: # / tool / input (preview, max 60 chars) / rationale.
/// Top-level rationale prints separately above the table via printfn so that
/// tests capturing Console.SetOut can assert it (AnsiConsole.Write bypasses
/// Console.SetOut in non-TTY environments).
let render (plan: Plan) : unit =
    // Top rationale (printfn so tests that redirect Console.SetOut can capture it).
    printfn "Proposed plan: %s" plan.Rationale

    let table = Table()
    table.AddColumn("#") |> ignore
    table.AddColumn("tool") |> ignore
    table.AddColumn("input") |> ignore
    table.AddColumn("rationale") |> ignore

    plan.Steps
    |> List.iteri (fun i step ->
        let (ToolName name) = step.Tool
        let (ToolInput m) = step.Input
        let raw = m |> Map.tryFind "_raw" |> Option.defaultValue "{}"
        let preview =
            if raw.Length > 60 then raw.Substring(0, 60) + "..."
            else raw
        table.AddRow([| string (i + 1); name; preview; step.Rationale |]) |> ignore)

    AnsiConsole.Write(table)

    // The approval prompt is plain printfn for stdout-redirect testability.
    printfn ""
    printfn "[a]ccept / [r]eject / [e]dit / [q]uit"

/// Loop until a recognized keystroke is received, then return the decision.
/// Unknown keys re-prompt without exiting. PUBLIC entry point.
/// Reader is IKeyReader so tests can inject scripted keystrokes without
/// calling Console.ReadKey (see PlanGateTests.fs).
let rec promptUser (reader: IKeyReader) : PlanGateDecision =
    let key = reader.ReadKey ()
    printfn "" // newline after intercepted keystroke
    match key with
    | 'a' ->
        printfn "Accepted."
        Accept
    | 'r' ->
        printfn "Rejected — re-prompting LLM."
        Reject
    | 'q' ->
        printfn "Quit."
        Quit
    | 'e' ->
        printfn "Edit — type comment then press Enter:"
        let comment = reader.ReadLine ()
        Edit comment
    | _ ->
        printfn "Unrecognized keystroke. Press a/r/e/q."
        promptUser reader
