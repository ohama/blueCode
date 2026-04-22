module BlueCode.Tests.FileToolsTests

open System
open System.IO
open System.Threading
open Expecto
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Cli.Adapters.FsToolExecutor

// ── Fixture helpers ────────────────────────────────────────────────────────────

/// Create a fresh temp directory usable as a project root for a single test.
/// Caller is responsible for cleanup via `cleanup`.
let private newFixture () : string =
    let dir = Path.Combine(Path.GetTempPath(), "bluecode-filetools-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    Path.GetFullPath(dir)

let private cleanup (dir: string) =
    try if Directory.Exists dir then Directory.Delete(dir, true) with _ -> ()

/// Synchronously run an IToolExecutor call for test assertions.
let private exec (executor: IToolExecutor) (tool: Tool) : Result<ToolResult, AgentError> =
    (executor.ExecuteAsync tool CancellationToken.None).GetAwaiter().GetResult()

// ── Tests ──────────────────────────────────────────────────────────────────────

let readFileTests = testList "FsToolExecutor.ReadFile (TOOL-01)" [

    testCase "reads full file content" <| fun () ->
        let root = newFixture ()
        try
            File.WriteAllText(Path.Combine(root, "hello.txt"), "alpha\nbeta\ngamma\n")
            let exe = create root
            let result = exec exe (ReadFile (FilePath "hello.txt", None))
            match result with
            | Ok (Success content) ->
                Expect.stringContains content "alpha" "full content should include alpha"
                Expect.stringContains content "gamma" "full content should include gamma"
            | other -> failtestf "expected Success, got %A" other
        finally cleanup root

    testCase "line range (1, 2) returns first two lines only" <| fun () ->
        let root = newFixture ()
        try
            File.WriteAllText(Path.Combine(root, "lines.txt"), "one\ntwo\nthree\nfour\nfive\n")
            let exe = create root
            let result = exec exe (ReadFile (FilePath "lines.txt", Some (1, 2)))
            match result with
            | Ok (Success content) ->
                Expect.stringContains content "one"   "should include line 1"
                Expect.stringContains content "two"   "should include line 2"
                Expect.isFalse (content.Contains("three")) "should NOT include line 3"
            | other -> failtestf "expected Success, got %A" other
        finally cleanup root

    testCase "line range (2, 3) returns middle lines" <| fun () ->
        let root = newFixture ()
        try
            File.WriteAllText(Path.Combine(root, "lines.txt"), "a\nb\nc\nd\ne\n")
            let exe = create root
            let result = exec exe (ReadFile (FilePath "lines.txt", Some (2, 3)))
            match result with
            | Ok (Success content) ->
                Expect.isFalse (content.Contains("a")) "should NOT include line 1"
                Expect.stringContains content "b" "should include line 2"
                Expect.stringContains content "c" "should include line 3"
                Expect.isFalse (content.Contains("d")) "should NOT include line 4"
            | other -> failtestf "expected Success, got %A" other
        finally cleanup root

    testCase "missing file returns Failure" <| fun () ->
        let root = newFixture ()
        try
            let exe = create root
            let result = exec exe (ReadFile (FilePath "does-not-exist.txt", None))
            match result with
            | Ok (Failure (_, _)) -> ()
            | other -> failtestf "expected Failure, got %A" other
        finally cleanup root

    testCase "path outside project root returns PathEscapeBlocked" <| fun () ->
        let root = newFixture ()
        try
            let exe = create root
            let result = exec exe (ReadFile (FilePath "../escape.txt", None))
            match result with
            | Ok (PathEscapeBlocked _) -> ()
            | other -> failtestf "expected PathEscapeBlocked, got %A" other
        finally cleanup root

    testCase "output exceeding 2000 chars truncated with marker (TOOL-06)" <| fun () ->
        let root = newFixture ()
        try
            let bigContent = String.replicate 3000 "x"
            File.WriteAllText(Path.Combine(root, "big.txt"), bigContent)
            let exe = create root
            let result = exec exe (ReadFile (FilePath "big.txt", None))
            match result with
            | Ok (Success content) ->
                Expect.isLessThan content.Length 2200 "content should be truncated near 2000"
                Expect.stringContains content "[truncated:" "marker must be present"
            | other -> failtestf "expected Success, got %A" other
        finally cleanup root
]

let writeFileTests = testList "FsToolExecutor.WriteFile (TOOL-02)" [

    testCase "writes content to new file inside project" <| fun () ->
        let root = newFixture ()
        try
            let exe = create root
            let result = exec exe (WriteFile (FilePath "new.txt", "written-content"))
            match result with
            | Ok (Success _) ->
                let actual = File.ReadAllText(Path.Combine(root, "new.txt"))
                Expect.equal actual "written-content" "file should contain what was written"
            | other -> failtestf "expected Success, got %A" other
        finally cleanup root

    testCase "path outside project root returns PathEscapeBlocked WITHOUT writing" <| fun () ->
        let root = newFixture ()
        try
            let exe = create root
            let result = exec exe (WriteFile (FilePath "../../evil.txt", "should-not-land"))
            match result with
            | Ok (PathEscapeBlocked _) ->
                // Confirm no sibling `evil.txt` was created anywhere nearby.
                let parent = Directory.GetParent(root).FullName
                let evil  = Path.Combine(parent, "evil.txt")
                Expect.isFalse (File.Exists evil) "no file should have been created outside root"
            | other -> failtestf "expected PathEscapeBlocked, got %A" other
        finally cleanup root

    testCase "absolute path outside project root returns PathEscapeBlocked" <| fun () ->
        let root = newFixture ()
        try
            let exe = create root
            let result = exec exe (WriteFile (FilePath "/tmp/bluecode-should-not-write.txt", "x"))
            match result with
            | Ok (PathEscapeBlocked _) -> ()
            | other -> failtestf "expected PathEscapeBlocked, got %A" other
        finally cleanup root

    testCase "path starting with ~ returns PathEscapeBlocked" <| fun () ->
        let root = newFixture ()
        try
            let exe = create root
            let result = exec exe (WriteFile (FilePath "~/tilde.txt", "x"))
            match result with
            | Ok (PathEscapeBlocked _) -> ()
            | other -> failtestf "expected PathEscapeBlocked, got %A" other
        finally cleanup root
]

let listDirTests = testList "FsToolExecutor.ListDir (TOOL-03)" [

    testCase "default depth lists top-level entries only" <| fun () ->
        let root = newFixture ()
        try
            File.WriteAllText(Path.Combine(root, "a.txt"), "")
            Directory.CreateDirectory(Path.Combine(root, "sub")) |> ignore
            File.WriteAllText(Path.Combine(root, "sub", "nested.txt"), "")
            let exe = create root
            let result = exec exe (ListDir (FilePath ".", None))
            match result with
            | Ok (Success body) ->
                Expect.stringContains body "a.txt" "should list a.txt"
                Expect.stringContains body "sub/"  "should list sub/ directory"
                Expect.isFalse (body.Contains("nested.txt")) "depth 1 must not recurse into sub/"
            | other -> failtestf "expected Success, got %A" other
        finally cleanup root

    testCase "depth 2 recurses one level into subdirectory" <| fun () ->
        let root = newFixture ()
        try
            Directory.CreateDirectory(Path.Combine(root, "sub")) |> ignore
            File.WriteAllText(Path.Combine(root, "sub", "nested.txt"), "")
            let exe = create root
            let result = exec exe (ListDir (FilePath ".", Some 2))
            match result with
            | Ok (Success body) ->
                Expect.stringContains body "sub/"        "should list sub/"
                Expect.stringContains body "nested.txt"  "depth 2 should include nested.txt"
            | other -> failtestf "expected Success, got %A" other
        finally cleanup root

    testCase "hidden dotfiles excluded from listing" <| fun () ->
        let root = newFixture ()
        try
            File.WriteAllText(Path.Combine(root, ".hidden"), "")
            File.WriteAllText(Path.Combine(root, "visible.txt"), "")
            let exe = create root
            let result = exec exe (ListDir (FilePath ".", None))
            match result with
            | Ok (Success body) ->
                Expect.stringContains body "visible.txt" "visible.txt must appear"
                Expect.isFalse (body.Contains(".hidden")) ".hidden must NOT appear"
            | other -> failtestf "expected Success, got %A" other
        finally cleanup root

    testCase "path outside project root returns PathEscapeBlocked" <| fun () ->
        let root = newFixture ()
        try
            let exe = create root
            let result = exec exe (ListDir (FilePath "../..", None))
            match result with
            | Ok (PathEscapeBlocked _) -> ()
            | other -> failtestf "expected PathEscapeBlocked, got %A" other
        finally cleanup root
]

[<Tests>]
let fileToolsTests =
    testList "FileTools (Phase 3 Plan 03-01)" [
        readFileTests
        writeFileTests
        listDirTests
        // runShellStubTest removed — superseded by RunShellTests in Plan 03-02
    ]
