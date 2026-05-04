module BlueCode.Tests.ToolExpansionTests

open System
open System.IO
open System.Threading
open Expecto
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Cli.Adapters.FsToolExecutor

// ── Fixture helpers (same pattern as FileToolsTests.fs) ──────────────────────

let private newFixture () : string =
    let dir =
        Path.Combine(Path.GetTempPath(), "bluecode-toolexp-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    Path.GetFullPath(dir)

let private cleanup (dir: string) =
    try
        if Directory.Exists dir then Directory.Delete(dir, true)
    with _ -> ()

let private exec (executor: IToolExecutor) (tool: Tool) : Result<ToolResult, AgentError> =
    (executor.ExecuteAsync tool CancellationToken.None).GetAwaiter().GetResult()

// ── edit_file tests (TLX-01) ──────────────────────────────────────────────────

let editFileTests =
    testList
        "FsToolExecutor.EditFile (TLX-01)"
        [

          testCase "1 occurrence → replaces and writes file"
          <| fun () ->
              let root = newFixture ()
              try
                  File.WriteAllText(Path.Combine(root, "edit1.txt"), "alpha\nbeta\ngamma\n")
                  let exe = create root []
                  let result = exec exe (EditFile(FilePath "edit1.txt", "beta", "BETA"))
                  match result with
                  | Ok(Success _) ->
                      let on_disk = File.ReadAllText(Path.Combine(root, "edit1.txt"))
                      Expect.equal on_disk "alpha\nBETA\ngamma\n" "file should contain replaced content"
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root

          testCase "0 occurrences → Failure 'oldString not found'"
          <| fun () ->
              let root = newFixture ()
              try
                  File.WriteAllText(Path.Combine(root, "edit2.txt"), "alpha\nbeta\ngamma\n")
                  let exe = create root []
                  let result = exec exe (EditFile(FilePath "edit2.txt", "notpresent", "X"))
                  match result with
                  | Ok(Failure(1, msg)) ->
                      Expect.stringContains msg "oldString not found" "message should say not found"
                  | other -> failtestf "expected Failure(1, ...), got %A" other
              finally
                  cleanup root

          testCase "2+ occurrences → Failure with count N and file UNCHANGED"
          <| fun () ->
              let root = newFixture ()
              try
                  let original = "dup dup dup"
                  File.WriteAllText(Path.Combine(root, "edit3.txt"), original)
                  let exe = create root []
                  let result = exec exe (EditFile(FilePath "edit3.txt", "dup", "X"))
                  match result with
                  | Ok(Failure(1, msg)) ->
                      Expect.stringContains msg "matches 3 times" "message should include count"
                      // Verify file is UNCHANGED — multi-match must not modify
                      let on_disk = File.ReadAllText(Path.Combine(root, "edit3.txt"))
                      Expect.equal on_disk original "file must be unchanged on multi-match"
                  | other -> failtestf "expected Failure(1, ...), got %A" other
              finally
                  cleanup root

          testCase "file outside projectRoot → PathEscapeBlocked"
          <| fun () ->
              let root = newFixture ()
              try
                  let exe = create root []
                  let result = exec exe (EditFile(FilePath "../escape.txt", "x", "y"))
                  match result with
                  | Ok(PathEscapeBlocked _) -> ()
                  | other -> failtestf "expected PathEscapeBlocked, got %A" other
              finally
                  cleanup root

          testCase "file not found → Failure"
          <| fun () ->
              let root = newFixture ()
              try
                  let exe = create root []
                  let result = exec exe (EditFile(FilePath "does-not-exist.txt", "x", "y"))
                  match result with
                  | Ok(Failure(1, _)) -> ()
                  | other -> failtestf "expected Failure(1, ...), got %A" other
              finally
                  cleanup root

          testCase "preserves CRLF line endings"
          <| fun () ->
              let root = newFixture ()
              try
                  let crlf_content = "a\r\nb\r\nc\r\n"
                  File.WriteAllText(Path.Combine(root, "crlf.txt"), crlf_content)
                  let exe = create root []
                  let result = exec exe (EditFile(FilePath "crlf.txt", "b", "B"))
                  match result with
                  | Ok(Success _) ->
                      let on_disk = File.ReadAllText(Path.Combine(root, "crlf.txt"))
                      Expect.equal on_disk "a\r\nB\r\nc\r\n" "CRLF line endings must be preserved"
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root ]

// ── glob_search tests (TLX-02) ────────────────────────────────────────────────

let globSearchTests =
    testList
        "FsToolExecutor.GlobSearch (TLX-02)"
        [

          testCase "matches *.fs files in src/"
          <| fun () ->
              let root = newFixture ()
              try
                  Directory.CreateDirectory(Path.Combine(root, "src", "nested")) |> ignore
                  File.WriteAllText(Path.Combine(root, "src", "a.fs"), "")
                  File.WriteAllText(Path.Combine(root, "src", "nested", "b.fs"), "")
                  File.WriteAllText(Path.Combine(root, "doc.md"), "")
                  let exe = create root []
                  let result = exec exe (GlobSearch("src/**/*.fs", None))
                  match result with
                  | Ok(Success body) ->
                      Expect.stringContains body "src/a.fs" "should include src/a.fs"
                      Expect.stringContains body "src/nested/b.fs" "should include src/nested/b.fs"
                      Expect.isFalse (body.Contains("doc.md")) "should NOT include doc.md"
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root

          testCase "no matches → Success with empty body"
          <| fun () ->
              let root = newFixture ()
              try
                  let exe = create root []
                  let result = exec exe (GlobSearch("**/*.nonexistent", None))
                  match result with
                  | Ok(Success body) ->
                      Expect.equal body "" "no matches should return empty string"
                  | other -> failtestf "expected Success(\"\"), got %A" other
              finally
                  cleanup root

          testCase "path outside projectRoot → PathEscapeBlocked"
          <| fun () ->
              let root = newFixture ()
              try
                  let exe = create root []
                  let result = exec exe (GlobSearch("**/*", Some(FilePath "../..")))
                  match result with
                  | Ok(PathEscapeBlocked _) -> ()
                  | other -> failtestf "expected PathEscapeBlocked, got %A" other
              finally
                  cleanup root

          testCase "100-match cap emits truncation marker"
          <| fun () ->
              let root = newFixture ()
              try
                  // Create 150 files
                  for i in 1..150 do
                      File.WriteAllText(Path.Combine(root, sprintf "file%03d.txt" i), "")
                  let exe = create root []
                  let result = exec exe (GlobSearch("**/*", None))
                  match result with
                  | Ok(Success body) ->
                      Expect.stringContains body "[truncated: showing first 100 matches]" "truncation marker must appear at 100 matches"
                  | other -> failtestf "expected Success with truncation marker, got %A" other
              finally
                  cleanup root

          testCase "hidden files are included (.hidden.txt, .subdir/x.txt)"
          <| fun () ->
              let root = newFixture ()
              try
                  File.WriteAllText(Path.Combine(root, ".hidden.txt"), "")
                  Directory.CreateDirectory(Path.Combine(root, ".subdir")) |> ignore
                  File.WriteAllText(Path.Combine(root, ".subdir", "x.txt"), "")
                  let exe = create root []
                  let result = exec exe (GlobSearch("**/*", None))
                  match result with
                  | Ok(Success body) ->
                      Expect.stringContains body ".hidden.txt" ".hidden.txt must appear (AttributesToSkip=System only, not Hidden)"
                  | other -> failtestf "expected Success with hidden files, got %A" other
              finally
                  cleanup root

          testCase "Phase 36-01: bare pattern auto-expands to **/ recursive (T-14 fix)"
          <| fun () ->
              let root = newFixture ()
              try
                  // Top-level + 1 nested + 2 nested file with .fsproj extension
                  File.WriteAllText(Path.Combine(root, "top.fsproj"), "")
                  Directory.CreateDirectory(Path.Combine(root, "src", "Inner")) |> ignore
                  File.WriteAllText(Path.Combine(root, "src", "mid.fsproj"), "")
                  File.WriteAllText(Path.Combine(root, "src", "Inner", "deep.fsproj"), "")
                  // Distractor: same name in non-matching extension
                  File.WriteAllText(Path.Combine(root, "src", "Inner", "deep.fs"), "")
                  let exe = create root []
                  let result = exec exe (GlobSearch("*.fsproj", None))
                  match result with
                  | Ok(Success body) ->
                      Expect.stringContains body "top.fsproj" "top-level .fsproj must match"
                      Expect.stringContains body "src/mid.fsproj" "1-level-deep .fsproj must match"
                      Expect.stringContains body "src/Inner/deep.fsproj" "2-level-deep .fsproj must match"
                      Expect.isFalse (body.Contains("deep.fs\n") || body.EndsWith("deep.fs")) ".fs must NOT match .fsproj pattern"
                  | other -> failtestf "expected Success with 3 matches, got %A" other
              finally
                  cleanup root

          testCase "Phase 36-01: '**/*.ext' pattern is NOT double-expanded"
          <| fun () ->
              let root = newFixture ()
              try
                  File.WriteAllText(Path.Combine(root, "a.txt"), "")
                  Directory.CreateDirectory(Path.Combine(root, "sub")) |> ignore
                  File.WriteAllText(Path.Combine(root, "sub", "b.txt"), "")
                  let exe = create root []
                  let result = exec exe (GlobSearch("**/*.txt", None))
                  match result with
                  | Ok(Success body) ->
                      Expect.stringContains body "a.txt" "top-level .txt matches"
                      Expect.stringContains body "sub/b.txt" "nested .txt matches"
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root

          testCase "Phase 36-01: pattern containing '/' is NOT auto-expanded"
          <| fun () ->
              let root = newFixture ()
              try
                  // Files at top-level should NOT match "src/*.fs"
                  File.WriteAllText(Path.Combine(root, "topLevel.fs"), "")
                  Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
                  File.WriteAllText(Path.Combine(root, "src", "inSrc.fs"), "")
                  let exe = create root []
                  let result = exec exe (GlobSearch("src/*.fs", None))
                  match result with
                  | Ok(Success body) ->
                      Expect.stringContains body "src/inSrc.fs" "src/inSrc.fs matches src/*.fs"
                      Expect.isFalse (body.Contains("topLevel.fs")) "top-level topLevel.fs must NOT match src/*.fs"
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root ]

// ── grep_search tests (TLX-03) ────────────────────────────────────────────────

let grepSearchTests =
    testList
        "FsToolExecutor.GrepSearch (TLX-03)"
        [

          testCase "matches pattern in file content → returns path:line:content"
          <| fun () ->
              let root = newFixture ()
              try
                  File.WriteAllText(Path.Combine(root, "x.fs"), "line1\nTODO: fix\nline3")
                  let exe = create root []
                  let result = exec exe (GrepSearch("TODO", None, None))
                  match result with
                  | Ok(Success body) ->
                      Expect.stringContains body "x.fs:2:TODO: fix" "output must contain path:line:content"
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root

          testCase "no matches → Success with empty body"
          <| fun () ->
              let root = newFixture ()
              try
                  File.WriteAllText(Path.Combine(root, "y.txt"), "nothing here")
                  let exe = create root []
                  let result = exec exe (GrepSearch("zzzzz", None, None))
                  match result with
                  | Ok(Success body) ->
                      Expect.equal body "" "no matches should return empty string"
                  | other -> failtestf "expected Success(\"\"), got %A" other
              finally
                  cleanup root

          testCase "invalid regex → Failure with 'Invalid regex pattern'"
          <| fun () ->
              let root = newFixture ()
              try
                  let exe = create root []
                  // Unclosed character class is an invalid regex
                  let result = exec exe (GrepSearch("[", None, None))
                  match result with
                  | Ok(Failure(1, msg)) ->
                      Expect.stringContains msg "Invalid regex pattern" "message should say invalid regex"
                  | other -> failtestf "expected Failure(1, ...), got %A" other
              finally
                  cleanup root

          testCase "fileGlob with path separator → Failure"
          <| fun () ->
              let root = newFixture ()
              try
                  let exe = create root []
                  let result = exec exe (GrepSearch("foo", None, Some "src/*.fs"))
                  match result with
                  | Ok(Failure(1, msg)) ->
                      Expect.stringContains msg "without path separators" "message must explain path separator restriction"
                  | other -> failtestf "expected Failure(1, ...), got %A" other
              finally
                  cleanup root

          testCase "fileGlob filter restricts search to matching file types"
          <| fun () ->
              let root = newFixture ()
              try
                  File.WriteAllText(Path.Combine(root, "x.fs"), "TODO in fsharp")
                  File.WriteAllText(Path.Combine(root, "y.md"), "TODO in markdown")
                  let exe = create root []
                  let result = exec exe (GrepSearch("TODO", None, Some "*.fs"))
                  match result with
                  | Ok(Success body) ->
                      Expect.stringContains body "x.fs" "should find match in .fs file"
                      Expect.isFalse (body.Contains("y.md")) "should NOT find match in .md file (filtered by fileGlob)"
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root

          testCase "line content truncated at 200 chars"
          <| fun () ->
              let root = newFixture ()
              try
                  // Create a line that is 300 chars and contains MATCH
                  let long_line = "MATCH" + String.replicate 295 "x"
                  Expect.equal long_line.Length 300 "long line must be 300 chars for test setup"
                  File.WriteAllText(Path.Combine(root, "long.txt"), long_line)
                  let exe = create root []
                  let result = exec exe (GrepSearch("MATCH", None, None))
                  match result with
                  | Ok(Success body) ->
                      // body format: "long.txt:1:<lineContent>"
                      // extract the lineContent part (after second colon)
                      let parts = body.Split(':', 3)
                      Expect.isGreaterThanOrEqual parts.Length 3 "body must have at least 3 colon-separated parts"
                      let line_content = parts.[2]
                      Expect.isLessThanOrEqual line_content.Length 200 "line content must be truncated to at most 200 chars"
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root

          testCase "catastrophic-backtracking pattern does not hang (500ms timeout)"
          <| fun () ->
              let root = newFixture ()
              try
                  // Classic ReDoS pattern: (a+)+b on a string of a's
                  let redos_line = String.replicate 100 "a"
                  File.WriteAllText(Path.Combine(root, "redos.txt"), redos_line)
                  let exe = create root []
                  // Use a 3s wall-clock CancellationTokenSource as a safety net
                  use cts = new CancellationTokenSource(TimeSpan.FromSeconds(3.0))
                  let task = exe.ExecuteAsync (GrepSearch("(a+)+b", None, None)) cts.Token
                  // Must complete before the 3s wall-clock fires
                  let completed = task.Wait(TimeSpan.FromSeconds(3.0))
                  Expect.isTrue completed "grepSearch must complete within 3s (not hang on ReDoS pattern)"
                  // Result must be Ok (either Success or Failure) — not a hang
                  let result = task.Result
                  match result with
                  | Ok _ -> ()  // Success "" (no match) or Failure — both acceptable; just not a hang
                  | Error _ -> ()  // UserCancelled if CT fired — acceptable
              finally
                  cleanup root ]

[<Tests>]
let tests =
    testList
        "ToolExpansion (Phase 8)"
        [ editFileTests; globSearchTests; grepSearchTests ]
