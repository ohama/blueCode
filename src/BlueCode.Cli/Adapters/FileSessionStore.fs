module BlueCode.Cli.Adapters.FileSessionStore

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Cli.Adapters.Json   // jsonOptions singleton (FSharp.SystemTextJson registered)

// ── On-disk JSONL format (v2.0 PERSIST-02) ───────────────────────────────────
// Line 1 (header): {"version":2,"sessionId":"<id>","createdAt":"<iso8601>"}
// Line N (envelope): {"type":"TurnComplete","turnIndex":<int>,"writtenAt":"<iso8601>","steps":[<Step>...]}
// Steps in each envelope are the FULL cumulative Session.Steps at the moment
// of the Save call (not just the delta). Round-trip stability: Load reads the
// last envelope and uses its Steps as the canonical Session.Steps.
//
// Why "last envelope wins" instead of "concatenate deltas": simpler Load,
// no double-counting, and Save remains atomic-per-turn. File grows linearly
// with turn count but turns are bounded (typical session: <50 turns; 50 turns
// of 5 steps each ≈ 250 step records ≈ <1MB JSONL).

[<CLIMutable>]
type private SessionHeader =
    { version: int
      sessionId: string
      createdAt: DateTimeOffset }

[<CLIMutable>]
type private TurnEnvelope =
    { ``type``: string         // always "TurnComplete"
      turnIndex: int           // 0-based turn count at moment of Save
      writtenAt: DateTimeOffset
      steps: Step list }       // cumulative session.Steps

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

/// Compute the per-session JSONL path: ~/.bluecode/sessions/<id>.jsonl.
/// Creates ~/.bluecode/sessions/ if it does not exist.
let buildSessionPath (SessionId id) : string =
    let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    let dir = Path.Combine(home, ".bluecode", "sessions")
    Directory.CreateDirectory(dir) |> ignore
    Path.Combine(dir, sprintf "%s.jsonl" id)

/// Generate a fresh SessionId. 32-char hex (Guid "N" format).
let newSessionId () : SessionId =
    SessionId (Guid.NewGuid().ToString("N"))

/// FileSessionStore: ISessionStore implementation backed by JSONL files under
/// ~/.bluecode/sessions/. Save is idempotent within a turn — calling twice
/// writes two envelopes; the last one wins on Load.
type FileSessionStore() =
    interface ISessionStore with
        member _.Save (session: Session) (ct: CancellationToken) : Task<Result<unit, AgentError>> =
            task {
                try
                    ct.ThrowIfCancellationRequested()
                    let path = buildSessionPath session.Id
                    let isNew = not (File.Exists path)
                    use writer = new StreamWriter(path, append = true, encoding = Encoding.UTF8)
                    writer.AutoFlush <- true
                    if isNew then
                        let (SessionId idStr) = session.Id
                        let header = { version = 2; sessionId = idStr; createdAt = session.CreatedAt }
                        writer.WriteLine(JsonSerializer.Serialize(header, jsonOptions))
                    // turnIndex: count of completed final-answer steps in session.Steps
                    let turnCount =
                        session.Steps
                        |> List.filter (fun s -> match s.Action with FinalAnswer _ -> true | _ -> false)
                        |> List.length
                    let envelope =
                        { ``type`` = "TurnComplete"
                          turnIndex = turnCount
                          writtenAt = DateTimeOffset.UtcNow
                          steps = session.Steps }
                    writer.WriteLine(JsonSerializer.Serialize(envelope, jsonOptions))
                    return Ok ()
                with
                | :? OperationCanceledException -> return Error UserCancelled
                | ex -> return Error (SessionCorrupt (sprintf "Save failed: %s" ex.Message))
            }

        member _.Load (id: SessionId) (ct: CancellationToken) : Task<Result<Session, AgentError>> =
            task {
                try
                    ct.ThrowIfCancellationRequested()
                    let path = buildSessionPath id

                    if not (File.Exists path) then
                        return Error (SessionNotFound id)
                    else
                        let! lines = File.ReadAllLinesAsync(path, ct)
                        if lines.Length = 0 then
                            return Error (SessionCorrupt "empty session file")
                        else
                            // Line 1 MUST be the version header.
                            let headerLine = lines.[0]
                            let header =
                                try
                                    JsonSerializer.Deserialize<SessionHeader>(headerLine, jsonOptions)
                                with ex ->
                                    failwithf "header parse failed: %s" ex.Message

                            if header.version <> 2 then
                                return Error (SessionCorrupt (sprintf "unsupported version %d (expected 2)" header.version))
                            else
                                let (SessionId expectedId) = id
                                if header.sessionId <> expectedId then
                                    return Error (SessionCorrupt (sprintf "header sessionId '%s' does not match requested '%s'" header.sessionId expectedId))
                                else
                                    // Lines 2..N are TurnComplete envelopes. Last envelope wins.
                                    let envelopeLines =
                                        lines
                                        |> Array.skip 1
                                        |> Array.filter (fun s -> not (String.IsNullOrWhiteSpace s))

                                    if envelopeLines.Length = 0 then
                                        // Header but no completed turns yet — valid state (crash-after-header-before-first-turn).
                                        return Ok
                                            { Id = id
                                              Steps = []
                                              CreatedAt = header.createdAt
                                              LastActivityAt = header.createdAt }
                                    else
                                        let lastLine = envelopeLines.[envelopeLines.Length - 1]
                                        let envelope =
                                            try
                                                JsonSerializer.Deserialize<TurnEnvelope>(lastLine, jsonOptions)
                                            with ex ->
                                                failwithf "envelope parse failed: %s" ex.Message

                                        if envelope.``type`` <> "TurnComplete" then
                                            return Error (SessionCorrupt (sprintf "unexpected envelope type '%s'" envelope.``type``))
                                        else
                                            return Ok
                                                { Id = id
                                                  Steps = envelope.steps
                                                  CreatedAt = header.createdAt
                                                  LastActivityAt = envelope.writtenAt }
                with
                | :? OperationCanceledException -> return Error UserCancelled
                | ex ->
                    // Defensive catch — surface as SessionCorrupt with detail (SC3: no stack traces).
                    return Error (SessionCorrupt (sprintf "Load failed: %s" ex.Message))
            }

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
                                |> Array.filter (fun s -> not (String.IsNullOrWhiteSpace s))
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
