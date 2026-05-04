module BlueCode.Cli.EditCommand

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks

/// Abstraction over editor launching so tests can inject scripted content
/// without needing a real terminal. Mirrors IKeyReader from PlanGate.fs.
type IEditorLauncher =
    /// Launch editor on tmpPath, block until editor exits.
    /// Production: spawns $EDITOR or vi with inherited TTY.
    /// Tests: writes scripted content directly to tmpPath then returns.
    abstract member Launch : tmpPath: string -> unit

/// Parse $EDITOR env var into (binary, extraArgs).
/// Supports "vi", "code --wait", "emacs -nw", etc.
/// Empty/whitespace -> ("vi", []) fallback.
let private parseEditorEnv () : string * string list =
    let envVal = Environment.GetEnvironmentVariable("EDITOR")
    if String.IsNullOrWhiteSpace(envVal) then
        ("vi", [])
    else
        let parts =
            envVal.Trim().Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
        (parts.[0], parts.[1..] |> Array.toList)

/// Production launcher: uses $EDITOR or vi fallback.
/// CRITICAL: all THREE Redirect* MUST be false so the child inherits the
/// parent's TTY file descriptors. UseShellExecute=false because on macOS
/// UseShellExecute=true uses /usr/bin/open (app bundle), not a shell —
/// terminal editors would open in a new window or fail silently.
/// Process.Start can throw (Win32Exception / IOException) if $EDITOR
/// points to a missing binary — wrap in try/with so the REPL never crashes.
let realEditorLauncher : IEditorLauncher =
    { new IEditorLauncher with
        member _.Launch tmpPath =
            let (bin, extraArgs) = parseEditorEnv ()
            let psi = ProcessStartInfo(bin)
            for arg in extraArgs do
                psi.ArgumentList.Add(arg)
            psi.ArgumentList.Add(tmpPath)
            psi.UseShellExecute <- false
            psi.RedirectStandardInput  <- false
            psi.RedirectStandardOutput <- false
            psi.RedirectStandardError  <- false
            // NO CreateNoWindow — Windows-only flag; do not set on macOS.
            let startResult =
                try Ok(Process.Start(psi))
                with ex -> Error(bin, ex)
            match startResult with
            | Error (b, ex) ->
                // Friendly error; tmpPath remains empty -> openEditorAsync returns None -> REPL prints "Edit cancelled.".
                printfn "Cannot launch editor '%s': %s" b ex.Message
                printfn "Edit cancelled (editor unavailable)."
            | Ok proc ->
                use _ = proc
                proc.WaitForExit()
                // Exit code intentionally ignored: content-based cancel
                // (research § Open Question #3, Pitfall — :q! returns 0 anyway).
    }

/// Tracks the most recently created tmpfile so AppDomain.ProcessExit can
/// sweep it if the process is killed mid-edit (covers the gap where
/// openEditorAsync's try/finally cleanup did not run).
let mutable private currentTmpPath : string option = None

// One-time atexit registration (module initializer; runs on first reference).
do
    AppDomain.CurrentDomain.ProcessExit.Add(fun _ ->
        match currentTmpPath with
        | Some path ->
            try if File.Exists path then File.Delete path with _ -> ()
        | None -> ())

/// Open tmpfile in editor, return Some content (trimmed) or None (empty/cancelled).
/// Creates tmpfile via Path.GetTempFileName (atomic 0-byte create), then
/// renames to .md so terminal editors with extension-based syntax detection
/// (vim/nano) get a markdown buffer. Cleans up tmpfile in `finally` regardless
/// of editor outcome (success / exception / cancel).
///
/// Empty check uses `Trim() = ""` so whitespace-only files are treated as
/// cancel (research § Pitfall 4 — degenerate "no real content").
let openEditorAsync (launcher: IEditorLauncher) : Task<string option> =
    task {
        let rawTmp = Path.GetTempFileName()
        let tmpPath = Path.ChangeExtension(rawTmp, ".md")
        File.Move(rawTmp, tmpPath)   // rawTmp gone; tmpPath is the live file
        currentTmpPath <- Some tmpPath
        try
            launcher.Launch tmpPath
            let content =
                if File.Exists tmpPath then File.ReadAllText(tmpPath).Trim()
                else ""
            return (if content = "" then None else Some content)
        finally
            try
                if File.Exists tmpPath then File.Delete tmpPath
            with _ -> ()
            currentTmpPath <- None
    }
