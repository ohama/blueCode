module BlueCode.Cli.PromptReader

open System
open System.IO
open System.Threading.Tasks
open PrettyPrompt
open PrettyPrompt.Highlighting   // FormattedString lives here (prompt parameter type)

/// Abstraction over interactive line reading. Production = PrettyPrompt with
/// persistent history; tests = pre-canned string queue. Mirrors IEditorLauncher
/// from EditCommand.fs (Phase 34-01).
///
/// Returns Task<string option>:
///   Some text -> user submitted a line (text may be empty if user pressed Enter on blank input)
///   None      -> Ctrl+C / Ctrl+D / EOF (caller maps to REPL exit)
///
/// NOTE: The prompt prefix string ("blueCode> ") is baked into construction
/// (PromptConfiguration.Prompt), not passed per-call. PrettyPrompt renders the
/// prefix automatically; do NOT also `printf` the prefix from Repl.fs.
type IPromptReader =
    abstract member ReadLineAsync : unit -> Task<string option>

/// Returns the persistent history file path: ~/.bluecode/history
/// Creates ~/.bluecode/ dir if absent (idempotent — Directory.CreateDirectory
/// no-op if exists; FileSessionStore already creates this directory on first
/// session save).
let historyFilePath () : string =
    let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    let dir  = Path.Combine(home, ".bluecode")
    Directory.CreateDirectory(dir) |> ignore
    Path.Combine(dir, "history")

/// Production reader: PrettyPrompt 4.1.1 with persistent history.
/// Instantiate ONCE per REPL session (inside runMultiTurnWithSession; before
/// the while loop). PrettyPrompt's constructor kicks off an async file-read
/// of `historyPath` — calling it eagerly at module init or in CompositionRoot
/// would issue a disk read before the user has even seen the banner.
///
/// Open question resolutions (35-RESEARCH.md § Open Questions; locked here):
///   #1 PromptConfiguration.Prompt = "blueCode> " (no leading "\n"; PrettyPrompt
///      handles its own line management).
///   #2 History includes ALL inputs (slash commands recallable via Up arrow).
///   #3 No BLUECODE_NO_PRETTYPROMPT env var (seam covers test injection needs).
///   #4 PrettyPrompt has a built-in 500-entry hard cap inside HistoryLog;
///      ROADMAP SC-5 said "N = 1000 default" but PrettyPrompt's internal
///      HistoryLog.MaxHistoryEntries is hard-coded; cap remains 500. SUMMARY
///      will document the trade-off (Plan 35-02).
let makeRealPromptReader () : IPromptReader =
    let path = historyFilePath ()
    // PromptConfiguration.prompt takes Nullable<FormattedString>; FormattedString has an
    // implicit conversion from string in C# but F# requires explicit construction.
    let promptFs = FormattedString("blueCode> ")
    let config = PromptConfiguration(prompt = System.Nullable(promptFs))
    let pp = new Prompt(persistentHistoryFilepath = path, configuration = config)
    { new IPromptReader with
        member _.ReadLineAsync() =
            task {
                let! result = pp.ReadLineAsync()   // Task<PromptResult>
                if result.IsSuccess then
                    return Some result.Text        // submitted line (may be empty string)
                else
                    return None                    // Ctrl+C, Ctrl+D, or any non-success
            } }

/// Test reader: dequeue from pre-canned list; None on exhaustion.
/// Used by Plan 35-02 ReplTests to inject scripted prompts WITHOUT spawning
/// PrettyPrompt's TTY-bound ReadKey loop (which would hang in a non-TTY
/// test environment).
let makeTestPromptReader (lines: string list) : IPromptReader =
    let q = Collections.Generic.Queue<string>(lines)
    { new IPromptReader with
        member _.ReadLineAsync() =
            task {
                if q.Count > 0 then return Some (q.Dequeue())
                else return None
            } }
