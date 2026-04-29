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
      ModelUsed = Qwen122B
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

          // ── Phase 32-01: listRecent ──────────────────────────────────────────
          testCase "listRecent 0 returns empty list (negative cap edge case)" <| fun () ->
              let metas = listRecent 0
              Expect.equal metas [] "listRecent with N=0 returns []"

          testCase "listRecent 100 includes a freshly-saved session with correct metadata" <| fun () ->
              let idStr = sprintf "lr-fresh-%s" (Guid.NewGuid().ToString("N"))
              let session = mkSession idStr 3
              let path = buildSessionPath session.Id
              withTempSession (fun () ->
                  let store = FileSessionStore() :> ISessionStore
                  let saveRes = (store.Save session CancellationToken.None).GetAwaiter().GetResult()
                  Expect.equal saveRes (Ok ()) "Save should succeed"
                  let metas = listRecent 100
                  let mine = metas |> List.tryFind (fun m -> m.Id = session.Id)
                  match mine with
                  | None -> failtestf "freshly saved session id %s not present in listRecent 100" idStr
                  | Some m ->
                      Expect.equal m.Id session.Id "Id matches"
                      Expect.equal m.TurnCount 1 "single Save => one envelope => TurnCount = 1"
                      // CreatedAt round-trips through header.createdAt
                      Expect.equal m.StartedAt session.CreatedAt "StartedAt matches header.createdAt"
                      // FirstPromptExcerpt comes from envelope.steps[0].Thought, truncated.
                      // mkStep builds Thought = "thought 1" for step 1 — exactly 9 chars.
                      Expect.equal m.FirstPromptExcerpt "thought 1" "first step's Thought text echoed"
              ) path

          testCase "listRecent N caps the result list to ≤N elements" <| fun () ->
              // We cannot guarantee exactly N elements unless we control the dir,
              // but we CAN assert (length metas) ≤ N for any N.
              let metas5  = listRecent 5
              let metas50 = listRecent 50
              Expect.isLessThanOrEqual (List.length metas5) 5  "listRecent 5 returns at most 5"
              Expect.isLessThanOrEqual (List.length metas50) 50 "listRecent 50 returns at most 50"

          testCase "listRecent sort: result is in non-increasing mtime order" <| fun () ->
              // Property test: across whatever sessions exist, the file behind metas.[0]
              // must have mtime >= file behind metas.[1] >= ... etc.
              // We compute mtime via buildSessionPath + File.GetLastWriteTimeUtc.
              let metas = listRecent 50
              if List.length metas >= 2 then
                  let pairs =
                      metas
                      |> List.pairwise
                      |> List.map (fun (a, b) ->
                          let mtA = File.GetLastWriteTimeUtc(buildSessionPath a.Id)
                          let mtB = File.GetLastWriteTimeUtc(buildSessionPath b.Id)
                          (mtA, mtB))
                  pairs
                  |> List.iter (fun (a, b) ->
                      Expect.isTrue (a >= b)
                          (sprintf "expected mtime %A >= %A but got reverse" a b))
              // If <2 sessions, sort is trivially correct — no assertion needed.

          testCase "listRecent skips file with corrupt header (does not throw)" <| fun () ->
              // Plant a file at a fresh GUID-N path with garbage content — listRecent
              // should silently skip it AND return successfully (no exception).
              let idStr = sprintf "lr-corrupt-%s" (Guid.NewGuid().ToString("N"))
              let path = buildSessionPath (SessionId idStr)
              withTempSession (fun () ->
                  File.WriteAllText(path, "this is not json\n{also garbage}\n")
                  // listRecent should NOT throw — it should just skip our garbage file.
                  let metas = listRecent 200
                  // Our corrupt id MUST NOT appear in the result.
                  let mine = metas |> List.tryFind (fun m ->
                      let (SessionId mId) = m.Id
                      mId = idStr)
                  Expect.isNone mine "corrupt-header file is silently skipped"
              ) path

          testCase "listRecent FirstPromptExcerpt: long thought is truncated to ≤80 chars" <| fun () ->
              // Build a session whose first step's Thought is >80 chars; verify excerpt
              // is truncated. Step.Thought is `Thought string`.
              let idStr = sprintf "lr-trunc-%s" (Guid.NewGuid().ToString("N"))
              let longThought = String.replicate 200 "a"   // 200 'a's
              let path = buildSessionPath (SessionId idStr)
              withTempSession (fun () ->
                  // Hand-build a Step with a long Thought.
                  let toolCall = ToolCall (ToolName "list_dir", ToolInput (Map.ofList [("_raw", "{\"path\":\".\"}")]  ))
                  let longStep =
                      { StepNumber = 1
                        Thought = Thought longThought
                        Action = toolCall
                        ToolResult = Some (Success "ok")
                        Status = StepSuccess
                        ModelUsed = Qwen122B
                        StartedAt = DateTimeOffset.MinValue
                        EndedAt = DateTimeOffset.MinValue
                        DurationMs = 1L }
                  let session : Session =
                      { Id = SessionId idStr
                        Steps = [ longStep ]
                        CreatedAt = DateTimeOffset.UtcNow
                        LastActivityAt = DateTimeOffset.UtcNow }
                  let store = FileSessionStore() :> ISessionStore
                  (store.Save session CancellationToken.None).GetAwaiter().GetResult() |> ignore
                  let metas = listRecent 200
                  let mine = metas |> List.tryFind (fun m -> m.Id = session.Id)
                  match mine with
                  | Some m ->
                      Expect.equal m.FirstPromptExcerpt.Length 80 "excerpt truncated to exactly 80 chars"
                      Expect.equal m.FirstPromptExcerpt (String.replicate 80 "a") "excerpt is the first 80 chars of the thought"
                  | None -> failtest "session must be present in listRecent"
              ) path

          testCase "listRecent FirstPromptExcerpt: zero-step session yields empty excerpt" <| fun () ->
              // Some sessions have a header but no completed turns (crash mid-prompt).
              // Build such a session by writing only the header.
              let idStr = sprintf "lr-empty-%s" (Guid.NewGuid().ToString("N"))
              let path = buildSessionPath (SessionId idStr)
              withTempSession (fun () ->
                  // Manually write only a v2 header line — no envelope.
                  let header = sprintf "{\"version\":2,\"sessionId\":\"%s\",\"createdAt\":\"2026-04-29T12:00:00+00:00\"}" idStr
                  File.WriteAllText(path, header + "\n")
                  let metas = listRecent 200
                  let mine = metas |> List.tryFind (fun m ->
                      let (SessionId mId) = m.Id
                      mId = idStr)
                  match mine with
                  | Some m ->
                      Expect.equal m.TurnCount 0 "header-only session has 0 turns"
                      Expect.equal m.FirstPromptExcerpt "" "header-only session has empty excerpt"
                  | None -> failtest "header-only session must still be listed (it has a valid header)"
              ) path
        ]
