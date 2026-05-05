---
phase: 32-slash-session-commands
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/BlueCode.Cli/Adapters/FileSessionStore.fs
  - src/BlueCode.Cli/Rendering.fs
  - tests/BlueCode.Tests/SessionStoreTests.fs
  - tests/BlueCode.Tests/RenderingTests.fs
autonomous: true

must_haves:
  truths:
    - "FileSessionStore module exposes a SessionMeta record with Id (SessionId), StartedAt (DateTimeOffset), TurnCount (int), FirstPromptExcerpt (string)"
    - "Calling listRecent n returns up to n SessionMeta values sorted by File.GetLastWriteTimeUtc descending; empty/missing dir returns []"
    - "listRecent silently skips files whose header fails to parse (no exception escapes)"
    - "Rendering.renderSessions returns 'no sessions found' when given an empty list"
    - "Rendering.renderSessions returns a multi-line string with one header row + one row per meta containing id, started timestamp, turn count, and first-thought excerpt"
    - "FirstPromptExcerpt is the first step's Thought text from the first envelope, truncated to ≤80 chars; empty string when no envelopes / no steps exist"
    - "ISessionStore interface in src/BlueCode.Core/Ports.fs is byte-identical to pre-Phase-32 (Save + Load only — listRecent is a Cli-layer module function, NOT a member)"
    - "All existing SessionStoreTests, RenderingTests, ReplTests pass; new unit tests added for listRecent (≥6) and renderSessions (≥4)"
  artifacts:
    - path: "src/BlueCode.Cli/Adapters/FileSessionStore.fs"
      provides: "SessionMeta record + listRecent module function"
      contains: "type SessionMeta"
      contains_2: "let listRecent"
    - path: "src/BlueCode.Cli/Rendering.fs"
      provides: "renderSessions function (open BlueCode.Cli.Adapters.FileSessionStore added at top)"
      contains: "let renderSessions"
      contains_2: "open BlueCode.Cli.Adapters.FileSessionStore"
    - path: "tests/BlueCode.Tests/SessionStoreTests.fs"
      provides: "≥6 unit tests for listRecent (empty dir, n cap, mtime sort, corrupt header skip, turn count, excerpt truncation)"
    - path: "tests/BlueCode.Tests/RenderingTests.fs"
      provides: "≥4 unit tests for renderSessions (empty list, single meta, multiple metas, excerpt truncation)"
  key_links:
    - from: "src/BlueCode.Cli/Rendering.fs"
      to: "src/BlueCode.Cli/Adapters/FileSessionStore.fs"
      via: "open BlueCode.Cli.Adapters.FileSessionStore + SessionMeta type reference"
      pattern: "open BlueCode\\.Cli\\.Adapters\\.FileSessionStore"
    - from: "src/BlueCode.Cli/Adapters/FileSessionStore.fs (listRecent)"
      to: "private SessionHeader / TurnEnvelope types"
      via: "JsonSerializer.Deserialize using existing private types in same module"
      pattern: "JsonSerializer\\.Deserialize<(SessionHeader|TurnEnvelope)>"
    - from: "src/BlueCode.Cli/Adapters/FileSessionStore.fs (listRecent)"
      to: "filesystem ~/.bluecode/sessions/*.jsonl"
      via: "Directory.GetFiles + File.GetLastWriteTimeUtc + File.ReadAllLines"
      pattern: "Directory\\.GetFiles"
---

<objective>
Phase 32 — Plan 01: Add the data layer (`SessionMeta` + `listRecent`) and the render layer
(`renderSessions`) needed for the upcoming `/sessions` slash command. NO Repl.fs wiring in
this plan — that lives in Plan 32-02.

Purpose: All work in this plan is pure-function, file I/O-safe (read-only), and unit-testable
in isolation. By splitting it from Plan 32-02 (Repl integration), we keep each plan well under
the ~50% context budget AND let Plan 32-02 import already-tested building blocks. This mirrors
the Phase 31-01 / 31-02 split (parser → dispatcher).

The roadmap success criterion 4 says: *"FileSessionStore 에 listRecent: int -> SessionMeta list
+ loadById: string -> Result<Session, AgentError> 메서드 추가 (load 는 v2.0 이미 존재; list 만 신규)"*.
Research § Q5 confirms `Load` IS the existing `loadById` — the contract already returns
`Result<Session, AgentError>`. So this plan adds ONLY `SessionMeta` + `listRecent`. The roadmap's
"loadById" wording refers to the existing `ISessionStore.Load`, which is reused as-is in Plan 32-02.

Output:
- New record type `SessionMeta` in `src/BlueCode.Cli/Adapters/FileSessionStore.fs`
- New module-level function `let listRecent (n: int) : SessionMeta list` in same file
- New function `let renderSessions (metas: SessionMeta list) : string` in `src/BlueCode.Cli/Rendering.fs`
- ~6 unit tests in `SessionStoreTests.fs` for listRecent
- ~4 unit tests in `RenderingTests.fs` for renderSessions
- Zero changes to: `src/BlueCode.Core/**`, `src/BlueCode.Cli/Repl.fs`, `src/BlueCode.Cli/SlashCommand.fs`,
  `src/BlueCode.Cli/CompositionRoot.fs`, `BlueCode.Cli.fsproj`, `BlueCode.Tests.fsproj`,
  `tests/BlueCode.Tests/RouterTests.fs` (rootTests already includes both updated test modules)
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/STATE.md
@.planning/phases/32-slash-session-commands/32-RESEARCH.md
@CLAUDE.md
@src/BlueCode.Cli/Adapters/FileSessionStore.fs
@src/BlueCode.Cli/Rendering.fs
@src/BlueCode.Core/Domain.fs
@tests/BlueCode.Tests/SessionStoreTests.fs
@tests/BlueCode.Tests/RenderingTests.fs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add SessionMeta type + listRecent module function to FileSessionStore.fs + unit tests</name>
  <files>src/BlueCode.Cli/Adapters/FileSessionStore.fs, tests/BlueCode.Tests/SessionStoreTests.fs</files>
  <action>
1. Edit `src/BlueCode.Cli/Adapters/FileSessionStore.fs`. Make TWO additive changes — do NOT
modify any existing code (the `[<CLIMutable>] private SessionHeader`, `private TurnEnvelope`,
`buildSessionPath`, `newSessionId`, `FileSessionStore` class, and `ISessionStore` interface
implementation must remain byte-identical):

(a) After the `private TurnEnvelope` type (currently ends at line 36 with `steps: Step list }`),
INSERT a new public `SessionMeta` record. Place it BEFORE `buildSessionPath` (line 40). Use
non-CLIMutable (this is a public domain type, not a JSON DTO):

```fsharp
/// Lightweight metadata for a persisted session, used by /sessions listing.
/// Cli-layer-only (Core purity invariant — see CLAUDE.md). Constructed by
/// listRecent below; consumed by Rendering.renderSessions.
///
/// FirstPromptExcerpt is a proxy: the user's prompt is NOT stored in the jsonl
/// (only LLM steps are). The best available signal is the FIRST envelope's
/// FIRST step's Thought text — the LLM's first reasoning trace. Truncated to
/// ≤80 chars; empty string for sessions with no completed turns. Research § Q10
/// + Open Question #1 (recommended resolution: "first thought" semantic).
type SessionMeta =
    { Id: SessionId
      StartedAt: DateTimeOffset
      TurnCount: int
      FirstPromptExcerpt: string }
```

(b) After the `FileSessionStore` class definition (currently ends at line 147 with the
defensive `with` of `Load`), APPEND a new module-level function `listRecent`. Module-level
(NOT a method on `FileSessionStore`) so it parallels `buildSessionPath` and `newSessionId`,
and so it can read the `private` `SessionHeader` / `TurnEnvelope` types from the same module:

```fsharp
/// List the most-recent N persisted sessions under ~/.bluecode/sessions/.
/// Sorted by File.GetLastWriteTimeUtc descending (newest first), truncated to N.
///
/// Returns [] if the sessions directory does not exist (e.g., user has never
/// run blueCode in multi-turn mode). Per-file parse failures are silently
/// skipped — research § Pitfall 1, "listRecent silently swallowing exceptions"
/// resolution: skip-and-continue instead of all-or-nothing failure, so one
/// corrupt session does not hide the other 533.
///
/// Performance: O(file_count) stat calls + O(N) ReadAllLines + O(N) JSON
/// deserializations. Research § Q9 confirms this is sub-millisecond on local
/// NVMe with 534 sessions.
///
/// Synchronous (research § Q15): every call site (Repl /sessions arm) is
/// already inside `task {}` and can call this without `let!`. Returning
/// `SessionMeta list` directly rather than `Task<>` keeps the API simple
/// and matches the existing buildSessionPath/newSessionId style.
let listRecent (n: int) : SessionMeta list =
    try
        let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        let dir = Path.Combine(home, ".bluecode", "sessions")
        if not (Directory.Exists dir) then []
        else
            Directory.GetFiles(dir, "*.jsonl")
            |> Array.sortByDescending (fun p -> File.GetLastWriteTimeUtc p)
            |> Array.truncate (max 0 n)
            |> Array.toList
            |> List.choose (fun path ->
                try
                    let lines = File.ReadAllLines(path)
                    if lines.Length = 0 then None
                    else
                        let header = JsonSerializer.Deserialize<SessionHeader>(lines.[0], jsonOptions)
                        if header.version <> 2 then None
                        else
                            // Turn count = number of non-empty envelope lines (skip header).
                            let envelopeLines =
                                lines
                                |> Array.skip 1
                                |> Array.filter (fun s -> not (System.String.IsNullOrWhiteSpace s))
                            let excerpt =
                                if envelopeLines.Length > 0 then
                                    try
                                        let env = JsonSerializer.Deserialize<TurnEnvelope>(envelopeLines.[0], jsonOptions)
                                        match env.steps with
                                        | step :: _ ->
                                            let (Thought t) = step.Thought
                                            if t.Length > 80 then t.Substring(0, 80) else t
                                        | [] -> ""
                                    with _ -> ""
                                else ""
                            Some
                                { Id = SessionId header.sessionId
                                  StartedAt = header.createdAt
                                  TurnCount = envelopeLines.Length
                                  FirstPromptExcerpt = excerpt }
                with _ -> None)
    with _ -> []
```

CRITICAL: do NOT add `listRecent` to the `ISessionStore` interface in `src/BlueCode.Core/Ports.fs`.
The interface stays frozen at Save + Load. listRecent is a Cli-layer concrete helper — Core
purity invariant (CLAUDE.md "Core purity (absolute)") forbids file I/O types in Core.

CRITICAL: do NOT make `SessionMeta` a `[<CLIMutable>]` type. CLIMutable is for JSON DTOs
(SessionHeader, TurnEnvelope) — it adds a parameterless constructor and exposes setters.
SessionMeta is a domain record consumed by F# code only.

CRITICAL: when N=0 or negative, listRecent returns [] (Array.truncate 0 = empty). When N is
larger than the file count, listRecent returns all files. Both behaviors must be tested.

2. Edit `tests/BlueCode.Tests/SessionStoreTests.fs`. Add a NEW testList suffix at the bottom.
The file currently ends at line 154 with `]`. The existing testList is wrapped in
`testSequenced` (line 58) — listRecent tests do NOT need testSequenced (no Console.SetOut
involvement, but we keep them inside the existing wrapper for consistency).

INSERT new test cases BEFORE the closing `]` (line 154). Each test uses `withTempSession` to
ensure cleanup. The directory `~/.bluecode/sessions/` may already contain 534 real session
files — DO NOT delete those. listRecent tests must create files with unique GUID-N IDs
that won't collide and must clean up only their own files.

PROBLEM: listRecent reads ALL files in `~/.bluecode/sessions/` (production dir). With 534
existing sessions, asserting "listRecent 1 returns OUR file as the most-recent" depends on
mtime ordering — we'd need our test file to be the newest, which is true if we just wrote it,
but flaky if the test runs in parallel with another that writes a session.

SOLUTION: Each test writes ONE new session via `FileSessionStore.Save`, then ASSERTS that:
(a) `listRecent 1` returns exactly one element AND that element's Id matches OUR test session id
(because we just wrote it, mtime makes it newest), OR
(b) `listRecent 100` includes our session AND meta fields are populated correctly.

Test (a) is timing-fragile — if user runs `dotnet run --project src/BlueCode.Cli ...` in
parallel with the test, a different session could be newer. Use a per-test unique scratch
directory by EXPOSING a path-override OR by accepting the production path and only asserting
"our id appears in the list" rather than "our id is element 0".

DECISION: keep listRecent reading from production `~/.bluecode/sessions/` (matches Repl
behavior — research § Q9 also assumes production path). Tests assert presence, not exact
position:

```fsharp
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
                  let toolCall = ToolCall (ToolName "list_dir", ToolInput (Map.ofList [("_raw", "{\"path\":\".\"}")]))
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
```

Place these BEFORE the closing `]` on line 154 of `SessionStoreTests.fs`.

Notes on test hygiene:
- All tests use the `withTempSession` helper (already present, lines 47-53) for cleanup.
- All tests use unique GUID-N suffixes (`lr-fresh-...`, `lr-corrupt-...`, etc.) so they
  don't interfere with each other or with the user's real session corpus.
- The "freshly-saved session" test uses `mkSession idStr 3`, which builds a single envelope
  with 3 steps (step 1 = "thought 1", step 2 = "thought 2", step 3 = "thought 3" with FinalAnswer).
  The first envelope's first step's Thought is "thought 1" — that's the asserted excerpt.

DO NOT change `[<Tests>]` attribute or `rootTests` registration in RouterTests.fs:
SessionStoreTests is already registered at RouterTests.fs line 114; new testCases inherit
that registration.

DO NOT add a new test FILE — add tests to the existing SessionStoreTests.fs.

3. Build and run only the FileSessionStore testList:
   ```
   dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj -- --filter FileSessionStore
   ```
   Expect (existing 5 + new 7 = 12) tests passing.

4. Commit atomically:
   ```
   git add src/BlueCode.Cli/Adapters/FileSessionStore.fs tests/BlueCode.Tests/SessionStoreTests.fs
   git commit -m "feat(32-01): add SessionMeta + listRecent to FileSessionStore"
   ```

DO NOT use `git add -A` / `git add .`. CLAUDE.md "Commit protocol" §3 — `.claude/` and
`localLLM/` are intentionally untracked and `git add -A` would sweep them in.
  </action>
  <verify>
- `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0 with no warnings.
- `grep -c "type SessionMeta" src/BlueCode.Cli/Adapters/FileSessionStore.fs` returns 1.
- `grep -c "let listRecent" src/BlueCode.Cli/Adapters/FileSessionStore.fs` returns 1.
- `grep -c "CLIMutable" src/BlueCode.Cli/Adapters/FileSessionStore.fs` returns 2 (only the existing private SessionHeader and TurnEnvelope — SessionMeta is NOT CLIMutable).
- `git diff master -- src/BlueCode.Core/` is empty (Core untouched — listRecent did NOT leak into ISessionStore).
- `grep -c "listRecent" src/BlueCode.Core/Ports.fs` returns 0 (interface stays Save + Load only).
- `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj -- --filter FileSessionStore` exits 0; output shows ≥12 tests passing under "FileSessionStore".
- `git log -1 --oneline` contains `feat(32-01)` + `listRecent`.
  </verify>
  <done>
- `SessionMeta` record type defined in FileSessionStore.fs (4 fields: Id, StartedAt, TurnCount, FirstPromptExcerpt).
- `listRecent : int -> SessionMeta list` module-level function defined in FileSessionStore.fs.
- listRecent silently skips corrupt files; returns [] when sessions dir missing; truncates excerpt to ≤80 chars; counts envelopes (not header) for TurnCount.
- 7 new SessionStoreTests pass; 5 existing tests still pass.
- ISessionStore interface in Core/Ports.fs unchanged.
- Atomic commit `feat(32-01): add SessionMeta + listRecent to FileSessionStore`.
  </done>
</task>

<task type="auto">
  <name>Task 2: Add renderSessions to Rendering.fs + unit tests in RenderingTests.fs</name>
  <files>src/BlueCode.Cli/Rendering.fs, tests/BlueCode.Tests/RenderingTests.fs</files>
  <action>
1. Edit `src/BlueCode.Cli/Rendering.fs`. Two additive changes — do NOT modify any existing
function (`renderStep`, `renderResult`, `renderError`, `renderHelp`, `renderStatus` must remain
byte-identical):

(a) After the existing `open BlueCode.Core.Domain` (line 4), ADD ONE new open line:

```fsharp
open BlueCode.Cli.Adapters.FileSessionStore
```

This brings `SessionMeta` into scope. Compile order in BlueCode.Cli.fsproj already places
FileSessionStore.fs BEFORE Rendering.fs (research § Architecture Patterns "fsproj Compile
Order"); no fsproj changes required.

(b) APPEND a new function `renderSessions` at the end of the file (after `renderStatus`,
which currently ends at line 171). NO Spectre markup (CLAUDE.md "Stream separation" — output
captured by Console.SetOut in tests):

```fsharp
/// Render the `/sessions` listing. Pure: takes a SessionMeta list, returns a multi-line
/// string. NO Spectre markup (CLAUDE.md "Stream separation"; tests capture via Console.SetOut).
///
/// Empty list → "no sessions found" (single line).
/// Non-empty → header row + one row per meta, with columns:
///   - session id (32-char hex, %-34s padding)
///   - started timestamp (%-25s, ISO-ish "yyyy-MM-dd HH:mm:ss")
///   - turn count (%-6d)
///   - first thought (≤40 chars displayed in this row, with "..." suffix if SessionMeta excerpt
///     was the full 80-char-truncated value — visual narrow column, the SessionMeta excerpt
///     itself is already capped at 80 chars by listRecent so this is a presentation detail).
///
/// Column header reads "first thought" NOT "first prompt" — research § Open Question #1:
/// the user's prompt is not stored in the jsonl, so the LLM's first reasoning step is the
/// best available proxy. Calling it "first prompt" would be misleading.
let renderSessions (metas: SessionMeta list) : string =
    if metas.IsEmpty then
        "no sessions found"
    else
        let header = sprintf "%-34s %-25s %-6s %s" "session id" "started" "turns" "first thought"
        let rows =
            metas
            |> List.map (fun m ->
                let (SessionId idStr) = m.Id
                let dateStr = m.StartedAt.ToString("yyyy-MM-dd HH:mm:ss")
                let displayExcerpt =
                    if m.FirstPromptExcerpt.Length > 40 then
                        m.FirstPromptExcerpt.Substring(0, 40) + "..."
                    else
                        m.FirstPromptExcerpt
                sprintf "%-34s %-25s %-6d %s" idStr dateStr m.TurnCount displayExcerpt)
        header :: rows |> String.concat "\n"
```

CRITICAL: `%-34s` for ID is 34 chars (32 hex + 2 spaces padding). The column is fixed-width
text, NOT Markdown — REPL terminal output, not a docs table.

CRITICAL: do NOT use `AnsiConsole.MarkupLine` or any Spectre call in this function. CLAUDE.md
"Stream separation": Spectre bypasses Console.SetOut, breaking integration tests in Plan 32-02.
The caller in Repl.fs will use plain `printfn "%s" (Rendering.renderSessions metas)`.

2. Edit `tests/BlueCode.Tests/RenderingTests.fs`. INSERT new testCase blocks at the END of
the existing `testList` (currently ends at line 156 with the `Expect.isFalse ... ]` close).

The file does NOT use `testSequenced` (line 32 is plain `testList`) — pure-string tests are
parallel-safe. Place new tests BEFORE the closing `]` on line 156:

```fsharp
          // ── Phase 32-01: renderSessions ──────────────────────────────────────
          testCase "renderSessions empty list returns 'no sessions found'" <| fun _ ->
              let s = renderSessions []
              Expect.equal s "no sessions found" "empty list yields exact phrase"

          testCase "renderSessions single meta shows id, started date, turns, excerpt" <| fun _ ->
              let meta : SessionMeta =
                  { Id = SessionId "deadbeef0123456789abcdef01234567"
                    StartedAt = DateTimeOffset(2026, 4, 29, 14, 30, 5, TimeSpan.Zero)
                    TurnCount = 7
                    FirstPromptExcerpt = "inspecting README" }
              let s = renderSessions [ meta ]
              Expect.stringContains s "deadbeef0123456789abcdef01234567" "id appears"
              Expect.stringContains s "2026-04-29 14:30:05" "started timestamp formatted yyyy-MM-dd HH:mm:ss"
              Expect.stringContains s "7" "turn count appears"
              Expect.stringContains s "inspecting README" "excerpt appears"
              // Header row must include the column labels.
              Expect.stringContains s "session id" "header row column: session id"
              Expect.stringContains s "started" "header row column: started"
              Expect.stringContains s "turns" "header row column: turns"
              Expect.stringContains s "first thought" "header row column label is 'first thought' (not 'first prompt')"

          testCase "renderSessions truncates excerpt longer than 40 chars with '...' suffix" <| fun _ ->
              // SessionMeta.FirstPromptExcerpt is already capped at 80 chars by listRecent;
              // renderSessions further truncates the DISPLAY column to 40 chars + '...'.
              let longExcerpt = String.replicate 60 "x"   // 60 chars (within SessionMeta's 80 cap)
              let meta : SessionMeta =
                  { Id = SessionId "abc"
                    StartedAt = DateTimeOffset.MinValue
                    TurnCount = 1
                    FirstPromptExcerpt = longExcerpt }
              let s = renderSessions [ meta ]
              Expect.stringContains s "..." "long excerpt receives ellipsis"
              Expect.stringContains s (String.replicate 40 "x") "first 40 chars displayed verbatim"
              // The full 60-char excerpt should NOT appear (we truncated to 40).
              Expect.isFalse (s.Contains(String.replicate 50 "x")) "excerpt truncated before reaching 50 'x's"

          testCase "renderSessions multiple metas yields header + N rows" <| fun _ ->
              let mk i =
                  { Id = SessionId (sprintf "session-%d" i)
                    StartedAt = DateTimeOffset(2026, 4, 29 - i, 12, 0, 0, TimeSpan.Zero)
                    TurnCount = i
                    FirstPromptExcerpt = sprintf "thought %d" i }
              let metas = [ mk 1; mk 2; mk 3 ]
              let s = renderSessions metas
              let lines = s.Split([| '\n' |])
              // 1 header + 3 data rows = 4 lines
              Expect.equal lines.Length 4 "header + 3 rows"
              Expect.stringContains s "session-1" "first id appears"
              Expect.stringContains s "session-2" "second id appears"
              Expect.stringContains s "session-3" "third id appears"
              Expect.stringContains s "thought 1" "first excerpt appears"
              Expect.stringContains s "thought 2" "second excerpt appears"
              Expect.stringContains s "thought 3" "third excerpt appears"

          testCase "renderSessions empty excerpt renders cleanly (no trailing junk)" <| fun _ ->
              // Header-only sessions have FirstPromptExcerpt = "". The row should still
              // render without crashing (no NullReferenceException, no malformed output).
              let meta : SessionMeta =
                  { Id = SessionId "abc"
                    StartedAt = DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero)
                    TurnCount = 0
                    FirstPromptExcerpt = "" }
              let s = renderSessions [ meta ]
              Expect.stringContains s "abc" "id present"
              Expect.stringContains s "0" "turn count 0 displayed"
              Expect.isFalse (s.Contains("...")) "no '...' for empty excerpt"
```

CRITICAL: the new testCase block requires `SessionMeta` to be in scope. The existing
RenderingTests.fs already opens `BlueCode.Cli.Rendering` (line 6); `Rendering` will re-export
nothing — but with our new `open BlueCode.Cli.Adapters.FileSessionStore` inside Rendering.fs,
the type leaks via the Rendering module's body but is NOT auto-imported by tests.

Therefore: ADD a new `open` to RenderingTests.fs (line 7, after `open BlueCode.Cli.Rendering`):

```fsharp
open BlueCode.Cli.Adapters.FileSessionStore
```

This makes `SessionMeta` directly available in the test file.

DO NOT add `BlueCode.Cli.SlashCommand` or `BlueCode.Cli.CompositionRoot` opens — RenderingTests
should remain narrowly scoped.

3. Build and run only the Rendering testList:
   ```
   dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj -- --filter Rendering
   ```
   Expect (existing 12 + new 5 = 17) tests passing under "Rendering".

4. Run the FULL suite to catch any cross-file regression:
   ```
   dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
   ```
   Expect ALL tests passing.

5. Commit atomically:
   ```
   git add src/BlueCode.Cli/Rendering.fs tests/BlueCode.Tests/RenderingTests.fs
   git commit -m "feat(32-01): add renderSessions to Rendering"
   ```

DO NOT use `git add -A` / `git add .`.
DO NOT modify renderHelp's `[coming in v2.5]` markers in this plan — that's Plan 32-02's job
(coordinated with the Repl dispatcher arms going live).
  </action>
  <verify>
- `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0 with no warnings.
- `grep -c "let renderSessions" src/BlueCode.Cli/Rendering.fs` returns 1.
- `grep -c "open BlueCode.Cli.Adapters.FileSessionStore" src/BlueCode.Cli/Rendering.fs` returns 1.
- `grep -c "AnsiConsole" src/BlueCode.Cli/Rendering.fs` returns 0 (no Spectre — must remain 0 post-Phase-32).
- `grep -c "open BlueCode.Cli.Adapters.FileSessionStore" tests/BlueCode.Tests/RenderingTests.fs` returns 1.
- `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj -- --filter Rendering` exits 0; output shows ≥17 tests passing under "Rendering".
- Full suite: `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0; total test count = pre-Phase-32 baseline + 12 (7 SessionStore + 5 Rendering).
- `git diff master -- src/BlueCode.Core/` is empty (Core untouched).
- `git log --oneline -1` contains `feat(32-01)` + `renderSessions`.
  </verify>
  <done>
- `renderSessions : SessionMeta list -> string` function defined in Rendering.fs.
- Empty list returns "no sessions found"; non-empty returns header + rows with columns id/started/turns/first thought.
- Excerpts >40 chars are truncated with "..." suffix in display column.
- Column header label is "first thought" (research Open Question #1 resolution).
- 5 new RenderingTests pass; 12 existing tests still pass (7 from Phase 31-02 + 5 baseline).
- No Spectre markup; no Core/ modifications.
- Atomic commit `feat(32-01): add renderSessions to Rendering`.
  </done>
</task>

</tasks>

<verification>
After both tasks complete, run these final plan-level gates:

1. **Build gate (debug):** `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.

2. **Build gate (release):** `dotnet build -c Release src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.

3. **Full test suite:** `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0; ALL tests pass; total count = pre-Phase-32 baseline + 12 (7 SessionStore + 5 Rendering).

4. **Core purity:** `git diff master -- src/BlueCode.Core/` is empty.

5. **No-async script:** `bash scripts/check-no-async.sh` exits 0 (Cli's task {} usage is fine; script bans `async {}` literal in Core only).

6. **ISessionStore frozen:** `git diff master -- src/BlueCode.Core/Ports.fs` is empty.

7. **Atomic commit count:** `git log --oneline master..HEAD` shows exactly 2 commits with `(32-01)` scope: `feat(32-01): add SessionMeta + listRecent to FileSessionStore` and `feat(32-01): add renderSessions to Rendering`.

8. **Bench gate is NOT required for Plan 32-01** — Plan 32-01 adds only library code; the dispatcher integration in Plan 32-02 is what would observe in production. Plan 32-02's Task 3 covers bench gate verification (research § Q14: zero regression risk because bench is single-turn, never enters multi-turn dispatch).
</verification>

<success_criteria>
This plan succeeds when:

- [ ] **SC-1 (SessionMeta type):** `type SessionMeta` exists in `src/BlueCode.Cli/Adapters/FileSessionStore.fs` with fields `Id : SessionId`, `StartedAt : DateTimeOffset`, `TurnCount : int`, `FirstPromptExcerpt : string`. NOT `[<CLIMutable>]`.
- [ ] **SC-2 (listRecent):** `let listRecent (n: int) : SessionMeta list` module-level function exists in same file. Returns `[]` when sessions dir missing. Sorts by mtime descending. Caps at N. Silently skips corrupt-header files. Excerpt truncated to ≤80 chars (sourced from `envelope.steps[0].Thought`).
- [ ] **SC-3 (renderSessions):** `let renderSessions (metas: SessionMeta list) : string` exists in `src/BlueCode.Cli/Rendering.fs`. Empty list → "no sessions found". Non-empty → header + rows. Column header label is "first thought" (NOT "first prompt").
- [ ] **SC-4 (Core untouched):** `git diff master -- src/BlueCode.Core/` is empty. ISessionStore interface unchanged. Phase 32 invariant 1 + 2 preserved (CLAUDE.md "Core purity"; ISessionStore frozen).
- [ ] **SC-5 (test coverage):** ≥6 listRecent unit tests + ≥4 renderSessions unit tests pass. Total +12 tests (7 SessionStore + 5 Rendering).
- [ ] **SC-6 (Phase 32 enabling):** Plan 32-02 can `open BlueCode.Cli.Adapters.FileSessionStore` and call `listRecent 10` + `Rendering.renderSessions metas` without circular dependencies (FileSessionStore.fs compiles before Rendering.fs which compiles before Repl.fs).
- [ ] **SC-7 (atomic commits):** Exactly 2 commits with `(32-01)` scope, both staged file-by-file (no `git add -A` violations).
- [ ] **SC-8 (no NuGet additions):** `grep -c "PackageReference" src/BlueCode.Cli/BlueCode.Cli.fsproj` unchanged from before Phase 32. Pure F# stdlib (`System.IO`, `System.Text.Json`) only.
</success_criteria>

<output>
After completion, create `.planning/phases/32-slash-session-commands/32-01-SUMMARY.md` documenting:

- Production LOC added (~50 in FileSessionStore.fs SessionMeta + listRecent + ~25 in Rendering.fs renderSessions = ~75)
- Test LOC added (~120 in SessionStoreTests.fs + ~80 in RenderingTests.fs = ~200)
- Test count delta (e.g., 316 → 328)
- Frontmatter to include:
  - `requires: []` (no inter-plan dependencies — Phase 31 already shipped infrastructure but this plan does not import it)
  - `affects: [32-02]` (Plan 32-02 imports SessionMeta and calls listRecent + renderSessions)
  - `subsystem: cli-session`
  - `tech_stack_added: []` (no new NuGet)
- Confirm Plan 32-01 success criteria SC-1 through SC-8 all met.
- Note any deviations (expected: none — research is HIGH confidence, all questions answered).
- Plan 32-02 status: ready to execute (Plan 32-01 unblocks it).
</output>
