module BlueCode.Cli.Adapters.FsToolExecutor

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open BlueCode.Core.Domain
open BlueCode.Core.Ports

// ── Constants ──────────────────────────────────────────────────────────────────

/// Message-history truncation cap. TOOL-06: every tool output is truncated
/// to 2000 chars before being appended to the LLM chat history. The raw
/// run_shell stdout/stderr caps (100KB / 10KB) are separate, applied
/// BEFORE this 2000-char cap (see Plan 03-02).
let private MESSAGE_HISTORY_CAP = 2000

/// Maximum list_dir recursion depth. Requests above this are silently
/// clamped to DEFAULT_LIST_DEPTH_MAX.
let private DEFAULT_LIST_DEPTH_MAX = 5

/// Default list_dir depth when Tool.ListDir carries None for depth.
let private DEFAULT_LIST_DEPTH = 1

// ── Output truncation (TOOL-06) ───────────────────────────────────────────────

/// Apply the 2000-char message-history cap with a human-readable marker.
/// Applied to EVERY Success/Failure output string before wrapping in ToolResult.
let private truncateOutput (raw: string) : string =
    if isNull raw then "" else
    if raw.Length <= MESSAGE_HISTORY_CAP then raw
    else
        let portion = raw.Substring(0, MESSAGE_HISTORY_CAP)
        sprintf
            "%s\n\n[truncated: showing first %d of %d chars]"
            portion MESSAGE_HISTORY_CAP raw.Length

// ── Path validation (TOOL-02) ─────────────────────────────────────────────────

/// Resolve inputPath relative to projectRoot, then require the resolved
/// path to stay inside projectRoot (with trailing-separator fix — see
/// 03-RESEARCH.md Pattern 2 and PITFALLS.md D-3). Returns Ok resolved
/// for in-scope paths, Error (PathEscapeBlocked inputPath) otherwise.
///
/// Paths starting with "~" are rejected — we do not expand home directories.
/// Absolute paths outside projectRoot fail the StartsWith check correctly
/// because Path.Combine(root, absPath) returns absPath unchanged on .NET
/// and Path.GetFullPath normalizes ".." traversal.
let private validatePath (projectRoot: string) (inputPath: string) : Result<string, ToolResult> =
    if String.IsNullOrWhiteSpace(inputPath) then
        Error (PathEscapeBlocked (inputPath |> Option.ofObj |> Option.defaultValue ""))
    elif inputPath.StartsWith("~") then
        Error (PathEscapeBlocked inputPath)
    else
        try
            let combined = Path.Combine(projectRoot, inputPath)
            let resolved = Path.GetFullPath(combined)
            // Trailing-separator fix: without it, `/a/project-evil` would
            // start-with `/a/project`. This is the prefix-attack defence
            // documented in 03-RESEARCH.md Pattern 2.
            let rootWithSep =
                if projectRoot.EndsWith(string Path.DirectorySeparatorChar)
                then projectRoot
                else projectRoot + string Path.DirectorySeparatorChar
            if resolved = projectRoot
               || resolved.StartsWith(rootWithSep, StringComparison.Ordinal) then
                Ok resolved
            else
                Error (PathEscapeBlocked inputPath)
        with
        | _ ->
            // Malformed path (e.g., invalid chars) -> treat as escape attempt.
            Error (PathEscapeBlocked inputPath)

// ── read_file (TOOL-01) ───────────────────────────────────────────────────────

/// Read a file with an optional 1-indexed inclusive line range.
/// None          -> return the whole file (truncated at 2000 chars)
/// Some (s, e)   -> return lines s..e (truncated at 2000 chars)
let private readFileImpl
    (projectRoot: string)
    (path: string)
    (lineRange: (int * int) option)
    (ct: CancellationToken)
    : Task<Result<ToolResult, AgentError>>
    =
    task {
        ct.ThrowIfCancellationRequested()
        match validatePath projectRoot path with
        | Error tr -> return Ok tr
        | Ok resolved ->
            try
                let content =
                    match lineRange with
                    | None ->
                        File.ReadAllText(resolved)
                    | Some (startLine, endLine) when startLine >= 1 && endLine >= startLine ->
                        let lines = File.ReadLines(resolved)
                        let selected =
                            lines
                            |> Seq.skip (startLine - 1)
                            |> Seq.truncate (endLine - startLine + 1)
                            |> Seq.toArray
                        String.Join("\n", selected)
                    | Some (s, e) ->
                        // Invalid range: report as Failure so the LLM can correct.
                        sprintf "[invalid line range: (%d, %d)]" s e
                return Ok (Success (truncateOutput content))
            with
            | :? FileNotFoundException as ex ->
                return Ok (Failure (1, ex.Message))
            | :? DirectoryNotFoundException as ex ->
                return Ok (Failure (1, ex.Message))
            | :? UnauthorizedAccessException as ex ->
                return Ok (Failure (1, ex.Message))
            | :? IOException as ex ->
                return Ok (Failure (1, ex.Message))
    }

// ── write_file (TOOL-02) ──────────────────────────────────────────────────────

/// Overwrite a file with the given content. Path must resolve inside
/// projectRoot or the call returns ToolResult.PathEscapeBlocked BEFORE
/// any filesystem IO happens.
let private writeFileImpl
    (projectRoot: string)
    (path: string)
    (content: string)
    (ct: CancellationToken)
    : Task<Result<ToolResult, AgentError>>
    =
    task {
        ct.ThrowIfCancellationRequested()
        match validatePath projectRoot path with
        | Error tr -> return Ok tr
        | Ok resolved ->
            try
                // Ensure parent directory exists; create if missing.
                let parent = Path.GetDirectoryName(resolved)
                if not (String.IsNullOrEmpty parent) && not (Directory.Exists parent) then
                    Directory.CreateDirectory(parent) |> ignore
                do! File.WriteAllTextAsync(resolved, content, ct)
                // TOOL-06 still applies to Success output even when empty.
                return Ok (Success (truncateOutput ""))
            with
            | :? UnauthorizedAccessException as ex ->
                return Ok (Failure (1, ex.Message))
            | :? IOException as ex ->
                return Ok (Failure (1, ex.Message))
    }

// ── list_dir (TOOL-03) ────────────────────────────────────────────────────────

/// Recursively enumerate directory entries up to maxDepth. Hidden files
/// (leading dot) are excluded. Directories are suffixed with `/`. Entries
/// are returned as relative paths joined by newlines.
let rec private enumDir (basePath: string) (current: string) (depth: int) (maxDepth: int) : string seq =
    seq {
        if depth > maxDepth then () else
        let entries =
            try
                Directory.EnumerateFileSystemEntries(current)
                |> Seq.sort
            with _ -> Seq.empty
        for entry in entries do
            let name = Path.GetFileName(entry)
            if not (name.StartsWith(".")) then
                let rel = Path.GetRelativePath(basePath, entry).Replace('\\', '/')
                if Directory.Exists(entry) then
                    yield rel + "/"
                    if depth < maxDepth then
                        yield! enumDir basePath entry (depth + 1) maxDepth
                else
                    yield rel
    }

let private listDirImpl
    (projectRoot: string)
    (path: string)
    (depth: int option)
    (ct: CancellationToken)
    : Task<Result<ToolResult, AgentError>>
    =
    task {
        ct.ThrowIfCancellationRequested()
        match validatePath projectRoot path with
        | Error tr -> return Ok tr
        | Ok resolved ->
            try
                if not (Directory.Exists resolved) then
                    return Ok (Failure (1, sprintf "Directory not found: %s" path))
                else
                    let requested = depth |> Option.defaultValue DEFAULT_LIST_DEPTH
                    let capped = min (max 1 requested) DEFAULT_LIST_DEPTH_MAX
                    let lines = enumDir resolved resolved 1 capped |> Seq.toArray
                    let body = String.Join("\n", lines)
                    return Ok (Success (truncateOutput body))
            with
            | :? UnauthorizedAccessException as ex ->
                return Ok (Failure (1, ex.Message))
            | :? IOException as ex ->
                return Ok (Failure (1, ex.Message))
    }

// ── run_shell stub (Plan 03-02 replaces) ──────────────────────────────────────

let private runShellStub () : Task<Result<ToolResult, AgentError>> =
    task {
        return Ok (Failure (-1, "run_shell not implemented in 03-01 — completed by Plan 03-02"))
    }

// ── Public factory ────────────────────────────────────────────────────────────

/// Create an IToolExecutor bound to projectRoot. All path validation runs
/// against this root. Typical callers: `FsToolExecutor.create (Directory.GetCurrentDirectory())`
/// at process start (Phase 4 CompositionRoot.fs).
///
/// Exhaustive match over Tool DU — adding a case to Tool in Domain.fs
/// is a compile error here (Success Criterion 6 proof).
let create (projectRoot: string) : IToolExecutor =
    let rootNormalized = Path.GetFullPath(projectRoot)
    { new IToolExecutor with
        member _.ExecuteAsync (tool: Tool) (ct: CancellationToken) : Task<Result<ToolResult, AgentError>> =
            match tool with
            | ReadFile  (FilePath path, lineRange)        -> readFileImpl  rootNormalized path lineRange ct
            | WriteFile (FilePath path, content)          -> writeFileImpl rootNormalized path content   ct
            | ListDir   (FilePath path, depth)            -> listDirImpl   rootNormalized path depth     ct
            | RunShell  (_, _)                            -> runShellStub ()
    }
