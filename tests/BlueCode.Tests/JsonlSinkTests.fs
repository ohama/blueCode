module BlueCode.Tests.JsonlSinkTests

open System
open System.IO
open System.Text.Json
open Expecto
open BlueCode.Core.Domain
open BlueCode.Cli.Adapters.JsonlSink

// ── Helpers ──────────────────────────────────────────────────────────────────

let private tempPath () : string =
    let dir =
        Path.Combine(Path.GetTempPath(), sprintf "bluecode-test-%s" (Guid.NewGuid().ToString("N")))

    Directory.CreateDirectory(dir) |> ignore
    Path.Combine(dir, "session.jsonl")

let private sampleStep (n: int) : Step =
    let started = DateTimeOffset(2026, 4, 22, 12, 0, n, TimeSpan.Zero)
    let ended = started.AddMilliseconds(float (100 + n))

    { StepNumber = n
      Thought = Thought(sprintf "thinking-%d" n)
      Action = ToolCall(ToolName "read_file", ToolInput(Map.ofList [ ("_raw", "{\"path\":\"x.txt\"}") ]))
      ToolResult = Some(Success(sprintf "output-%d" n))
      Status = StepSuccess
      ModelUsed = Qwen32B
      StartedAt = started
      EndedAt = ended
      DurationMs = int64 (100 + n) }

[<Tests>]
let tests =
    testList
        "JsonlSink"
        [ testCase "buildSessionLogPath returns path under ~/.bluecode"
          <| fun _ ->
              let path = buildSessionLogPath ()
              let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
              let bluecodeDir = Path.Combine(home, ".bluecode")
              Expect.isTrue (path.StartsWith(bluecodeDir)) (sprintf "path %s should start with %s" path bluecodeDir)
              Expect.isTrue (path.EndsWith(".jsonl")) "path should end with .jsonl"
              Expect.isTrue (Directory.Exists(bluecodeDir)) "~/.bluecode should exist"

          testCase "buildSessionLogPath filename has no colons (cross-platform)"
          <| fun _ ->
              let path = buildSessionLogPath ()
              let filename = Path.GetFileName(path)
              Expect.isFalse (filename.Contains(":")) "filename must not contain ':'"

          testCase "WriteStep appends one JSONL line per step"
          <| fun _ ->
              let p = tempPath ()

              do
                  use sink = new JsonlSink(p)
                  sink.WriteStep(sampleStep 1)
                  sink.WriteStep(sampleStep 2)
                  sink.WriteStep(sampleStep 3)

              let lines = File.ReadAllLines(p)
              Expect.equal lines.Length 3 "3 lines expected"

              for line in lines do
                  // Each line must be valid JSON
                  use _ = JsonDocument.Parse(line)
                  ()

          testCase "WriteStep AutoFlush = true: file readable mid-session (before Dispose)"
          <| fun _ ->
              let p = tempPath ()
              let sink = new JsonlSink(p)

              try
                  sink.WriteStep(sampleStep 1)
                  // Do NOT Dispose yet. With AutoFlush=true the line is already on disk.
                  let linesBeforeDispose = File.ReadAllLines(p)
                  Expect.equal linesBeforeDispose.Length 1 "line visible before Dispose"
                  sink.WriteStep(sampleStep 2)
                  let linesAfterSecond = File.ReadAllLines(p)
                  Expect.equal linesAfterSecond.Length 2 "second line visible immediately"
              finally
                  (sink :> IDisposable).Dispose()

          testCase "JSONL step record contains startedAt, endedAt, durationMs fields (OBS-04)"
          <| fun _ ->
              let p = tempPath ()

              do
                  use sink = new JsonlSink(p)
                  sink.WriteStep(sampleStep 42)

              let line = File.ReadAllText(p).Trim()
              use doc = JsonDocument.Parse(line)
              let root = doc.RootElement
              // Property names follow F# record field casing — FSharp.SystemTextJson
              // does NOT lowercase by default. Accept either pascal or camel.
              let hasProp (names: string list) =
                  names
                  |> List.exists (fun n ->
                      let ok, _ = root.TryGetProperty(n)
                      ok)

              Expect.isTrue (hasProp [ "StartedAt"; "startedAt" ]) "startedAt present"
              Expect.isTrue (hasProp [ "EndedAt"; "endedAt" ]) "endedAt present"
              Expect.isTrue (hasProp [ "DurationMs"; "durationMs" ]) "durationMs present"

          testCase "JsonlSink implements IDisposable and closes writer"
          <| fun _ ->
              let p = tempPath ()
              let sink = new JsonlSink(p)
              sink.WriteStep(sampleStep 1)
              (sink :> IDisposable).Dispose()
              // After dispose, the file must still exist and be readable.
              Expect.isTrue (File.Exists(p)) "file exists after dispose"
              let content = File.ReadAllText(p)
              Expect.isNotEmpty content "file has content after dispose" ]
