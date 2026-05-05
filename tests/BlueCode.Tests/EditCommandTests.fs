module BlueCode.Tests.EditCommandTests

open System
open System.IO
open Expecto
open BlueCode.Cli.EditCommand

/// Build a scripted IEditorLauncher that writes `content` to the tmpPath
/// when Launch is called. Captures the tmpPath via a ref so tests can
/// assert post-launch file state (e.g. deletion).
let private scriptedLauncher (content: string) (capturedPath: string ref) : IEditorLauncher =
    { new IEditorLauncher with
        member _.Launch tmpPath =
            capturedPath := tmpPath
            File.WriteAllText(tmpPath, content) }

/// Synchronous run helper: openEditorAsync returns Task<string option>;
/// tests are testCase (sync) so we drain the task here.
let private runSync (t: System.Threading.Tasks.Task<string option>) : string option =
    t.GetAwaiter().GetResult()

let tests =
    testList "EditCommand" [

        testCase "openEditorAsync: non-empty content -> Some (trimmed) content" <| fun () ->
            let captured = ref ""
            let launcher = scriptedLauncher "Refactor auth to use JWT\n" captured
            let result = openEditorAsync launcher |> runSync
            Expect.equal result (Some "Refactor auth to use JWT")
                "trimmed content returned (trailing newline stripped)"

        testCase "openEditorAsync: empty content -> None (cancel)" <| fun () ->
            let captured = ref ""
            let launcher = scriptedLauncher "" captured
            let result = openEditorAsync launcher |> runSync
            Expect.equal result None "empty file returns None (cancel path)"

        testCase "openEditorAsync: whitespace-only content -> None (cancel; Trim() = \"\")" <| fun () ->
            let captured = ref ""
            let launcher = scriptedLauncher "   \n\t  \n" captured
            let result = openEditorAsync launcher |> runSync
            Expect.equal result None
                "whitespace-only file returns None (research § Pitfall 4: Trim()=\"\" treats degenerate \"no real content\" as cancel)"

        testCase "openEditorAsync: tmpfile deleted after read (try/finally)" <| fun () ->
            let captured = ref ""
            let launcher = scriptedLauncher "some content" captured
            let _ = openEditorAsync launcher |> runSync
            Expect.notEqual !captured "" "launcher did capture a tmpPath"
            Expect.isFalse (File.Exists !captured)
                "tmpfile deleted after read (try/finally cleanup; research § Pattern 3)"

        testCase "openEditorAsync: tmpfile has .md extension (editor syntax-highlight hint)" <| fun () ->
            let captured = ref ""
            let launcher = scriptedLauncher "x" captured
            let _ = openEditorAsync launcher |> runSync
            Expect.stringEnds !captured ".md"
                "tmpfile renamed to .md so vim/nano apply markdown syntax highlighting"
    ]
