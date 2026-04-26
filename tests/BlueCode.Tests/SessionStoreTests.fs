module BlueCode.Tests.SessionStoreTests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Expecto
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Cli.Adapters.FileSessionStore

// ── Test fixtures ─────────────────────────────────────────────────────────────

/// Build a Step record with deterministic timestamps for round-trip equality assertions.
let private mkStep (n: int) (action: LlmOutput) : Step =
    let t0 = DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero).AddMilliseconds(float (n * 100))
    let t1 = t0.AddMilliseconds(50.0)
    { StepNumber = n
      Thought = Thought (sprintf "thought %d" n)
      Action = action
      ToolResult =
        match action with
        | FinalAnswer _ -> None
        | ToolCall _ -> Some (Success (sprintf "stub output %d" n))
        | Plan _ -> None
      Status = StepSuccess
      ModelUsed = Qwen32B
      StartedAt = t0
      EndedAt = t1
      DurationMs = 50L }

/// Build a session with `stepCount` steps. Last step is a FinalAnswer; others are ToolCall.
let private mkSession (idStr: string) (stepCount: int) : Session =
    let toolCall = ToolCall (ToolName "list_dir", ToolInput (Map.ofList [("_raw", "{\"path\":\".\"}")]))
    let steps =
        [ for i in 1 .. stepCount - 1 -> mkStep i toolCall ]
        @ [ mkStep stepCount (FinalAnswer "done") ]
    { Id = SessionId idStr
      Steps = steps
      CreatedAt = DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)
      LastActivityAt = DateTimeOffset(2026, 4, 26, 12, 0, 5, TimeSpan.Zero) }

/// Run a test that writes to ~/.bluecode/sessions/ with a given session id,
/// then cleans up the file afterward. Unique GUID-based ids prevent interference
/// between tests. Note: `buildSessionPath` creates the directory if needed, so
/// cleanup only needs to remove the file itself.
let private withTempSession (action: unit -> unit) (path: string) : unit =
    try
        action ()
    finally
        try
            if File.Exists path then File.Delete path
        with _ -> ()

// ── Tests ─────────────────────────────────────────────────────────────────────

let tests =
    testSequenced
    <| testList
        "FileSessionStore"
        [
          testCase "round-trip: Save then Load returns equivalent Session.Steps"
          <| fun () ->
              let idStr = sprintf "sst-rt-%s" (Guid.NewGuid().ToString("N"))
              let session = mkSession idStr 3
              let path = buildSessionPath session.Id
              withTempSession (fun () ->
                  let store = FileSessionStore() :> ISessionStore
                  let ct = CancellationToken.None
                  let saveRes = (store.Save session ct).GetAwaiter().GetResult()
                  Expect.equal saveRes (Ok ()) "Save should succeed"
                  let loadRes = (store.Load session.Id ct).GetAwaiter().GetResult()
                  match loadRes with
                  | Ok loaded ->
                      Expect.equal loaded.Id session.Id "Loaded Id matches"
                      Expect.equal loaded.Steps.Length session.Steps.Length "Step count round-trips"
                      Expect.equal loaded.Steps session.Steps "Steps deep-equal after round-trip"
                  | Error e -> failtestf "Load should succeed but returned %A" e) path

          testCase "Load on missing id returns SessionNotFound (not exception)"
          <| fun () ->
              let ghostId = SessionId (sprintf "sst-ghost-%s" (Guid.NewGuid().ToString("N")))
              let path = buildSessionPath ghostId
              // Ensure no stale file exists from a prior run.
              if File.Exists path then File.Delete path
              let store = FileSessionStore() :> ISessionStore
              let loadRes = (store.Load ghostId CancellationToken.None).GetAwaiter().GetResult()
              match loadRes with
              | Error (SessionNotFound id) -> Expect.equal id ghostId "SessionNotFound carries the requested id"
              | other -> failtestf "Expected SessionNotFound, got %A" other

          testCase "Load on corrupt JSONL returns SessionCorrupt (not exception)"
          <| fun () ->
              let idStr = sprintf "sst-corrupt-%s" (Guid.NewGuid().ToString("N"))
              let corruptId = SessionId idStr
              let path = buildSessionPath corruptId
              withTempSession (fun () ->
                  // Plant a corrupt file at the expected path.
                  File.WriteAllText(path, "not valid json at all\n{also not}\n")
                  let store = FileSessionStore() :> ISessionStore
                  let loadRes = (store.Load corruptId CancellationToken.None).GetAwaiter().GetResult()
                  match loadRes with
                  | Error (SessionCorrupt detail) ->
                      Expect.isTrue (detail.Length > 0) "SessionCorrupt has a non-empty detail message"
                  | other -> failtestf "Expected SessionCorrupt, got %A" other) path

          testCase "Load on header-mismatched id returns SessionCorrupt"
          <| fun () ->
              // Save session A, rename file to id B's path (header still says A).
              let idStrA = sprintf "sst-hdr-a-%s" (Guid.NewGuid().ToString("N"))
              let idStrB = sprintf "sst-hdr-b-%s" (Guid.NewGuid().ToString("N"))
              let sessionA = mkSession idStrA 2
              let pathA = buildSessionPath sessionA.Id
              let pathB = buildSessionPath (SessionId idStrB)
              // Ensure B doesn't exist from a prior run.
              if File.Exists pathB then File.Delete pathB
              withTempSession (fun () ->
                  let store = FileSessionStore() :> ISessionStore
                  (store.Save sessionA CancellationToken.None).GetAwaiter().GetResult() |> ignore
                  File.Move(pathA, pathB)
                  // Load B → header says A → SessionCorrupt.
                  let loadRes = (store.Load (SessionId idStrB) CancellationToken.None).GetAwaiter().GetResult()
                  match loadRes with
                  | Error (SessionCorrupt _) -> ()  // expected
                  | other -> failtestf "Expected SessionCorrupt for header mismatch, got %A" other) pathB

          testCase "Save twice in same session writes header once + two TurnComplete envelopes; Load returns latest"
          <| fun () ->
              let idStr = sprintf "sst-two-%s" (Guid.NewGuid().ToString("N"))
              let session1 = mkSession idStr 1
              let path = buildSessionPath session1.Id
              withTempSession (fun () ->
                  let store = FileSessionStore() :> ISessionStore
                  (store.Save session1 CancellationToken.None).GetAwaiter().GetResult() |> ignore
                  // Build a 2nd "turn" by extending Steps.
                  let session2 =
                      { session1 with
                          Steps = session1.Steps @ [ mkStep 2 (FinalAnswer "done2") ]
                          LastActivityAt = DateTimeOffset(2026, 4, 26, 12, 0, 10, TimeSpan.Zero) }
                  (store.Save session2 CancellationToken.None).GetAwaiter().GetResult() |> ignore
                  // Inspect raw file: line count = 1 header + 2 envelopes = 3 lines.
                  let lines = File.ReadAllLines(path)
                  Expect.equal lines.Length 3 "header + 2 envelopes"
                  Expect.stringContains lines.[0] "\"version\":2" "header line has version 2"
                  Expect.stringContains lines.[1] "TurnComplete" "first envelope is TurnComplete"
                  Expect.stringContains lines.[2] "TurnComplete" "second envelope is TurnComplete"
                  // Load returns the latest (session2's Steps).
                  let loadRes = (store.Load session2.Id CancellationToken.None).GetAwaiter().GetResult()
                  match loadRes with
                  | Ok loaded ->
                      Expect.equal loaded.Steps.Length 2 "Latest envelope has 2 Steps"
                      Expect.equal loaded.Steps session2.Steps "Latest Steps round-trip exactly"
                  | Error e -> failtestf "Load should succeed, got %A" e) path
        ]
