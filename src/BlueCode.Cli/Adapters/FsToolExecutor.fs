module BlueCode.Cli.Adapters.FsToolExecutor

open System
open System.IO
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Cli.Adapters.BashSecurity

// ── Constants ──────────────────────────────────────────────────────────────────

/// Message-history truncation cap. TOOL-06: every tool output is truncated
/// to 2000 chars before being appended to the LLM chat history. The raw
/// run_shell stdout/stderr caps (100KB / 10KB) are separate, applied
/// BEFORE this 2000-char cap (see Plan 03-02).
let private MESSAGE_HISTORY_CAP = 2000

/// Maximum list_dir recursion depth. Requests above this are silently
/// clamped to DEFAULT_LIST_DEPTH_MAX.
let private DEFAULT_LIST_DEPTH_MAX = 5

/// TOOL-04: raw stdout cap for run_shell. 100KB. Applied BEFORE TOOL-06
/// message-history truncation.
let private SHELL_STDOUT_CAP = 100 * 1024

/// TOOL-04: raw stderr cap for run_shell. 10KB.
let private SHELL_STDERR_CAP = 10 * 1024

/// TOOL-04: shell timeout in seconds. Hardcoded per requirement.
/// Tool.RunShell carries Timeout in MILLISECONDS; we take that value if
/// it is less than or equal to the global cap, otherwise clamp to 30s.
let private SHELL_TIMEOUT_SECONDS = 30

/// Cap a raw string to maxBytes characters. (Characters, not bytes —
/// F# strings are UTF-16 .NET strings. For this resource-limit layer
/// we treat one character as one "byte" of budget; this is an
/// approximation that is conservative for ASCII and slightly under-caps
/// for multi-byte UTF-8. Acceptable for a 100KB cap.)
let private capOutput (raw: string) (maxChars: int) : string =
    if isNull raw then ""
    else if raw.Length <= maxChars then raw
    else raw.Substring(0, maxChars)

/// Default list_dir depth when Tool.ListDir carries None for depth.
let private DEFAULT_LIST_DEPTH = 1

// ── Output truncation (TOOL-06) ───────────────────────────────────────────────

/// Apply the 2000-char message-history cap with a human-readable marker.
/// Applied to EVERY Success/Failure output string before wrapping in ToolResult.
let private truncateOutput (raw: string) : string =
    if isNull raw then
        ""
    else if raw.Length <= MESSAGE_HISTORY_CAP then
        raw
    else
        let portion = raw.Substring(0, MESSAGE_HISTORY_CAP)
        sprintf "%s\n\n[truncated: showing first %d of %d chars]" portion MESSAGE_HISTORY_CAP raw.Length

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
        Error(PathEscapeBlocked(inputPath |> Option.ofObj |> Option.defaultValue ""))
    elif inputPath.StartsWith("~") then
        Error(PathEscapeBlocked inputPath)
    else
        try
            let combined = Path.Combine(projectRoot, inputPath)
            let resolved = Path.GetFullPath(combined)
            // Trailing-separator fix: without it, `/a/project-evil` would
            // start-with `/a/project`. This is the prefix-attack defence
            // documented in 03-RESEARCH.md Pattern 2.
            let rootWithSep =
                if projectRoot.EndsWith(string Path.DirectorySeparatorChar) then
                    projectRoot
                else
                    projectRoot + string Path.DirectorySeparatorChar

            if
                resolved = projectRoot
                || resolved.StartsWith(rootWithSep, StringComparison.Ordinal)
            then
                Ok resolved
            else
                Error(PathEscapeBlocked inputPath)
        with _ ->
            // Malformed path (e.g., invalid chars) -> treat as escape attempt.
            Error(PathEscapeBlocked inputPath)

// ── read_file (TOOL-01) ───────────────────────────────────────────────────────

/// Read a file with an optional 1-indexed inclusive line range.
/// None          -> return the whole file (truncated at 2000 chars)
/// Some (s, e)   -> return lines s..e (truncated at 2000 chars)
///
/// TOOL-08: every Success payload begins with a one-line metadata header:
///   [file: <relPath>, lines X-Y of Z, <not-truncated|truncated|out-of-range>]
/// followed by '\n' + the (possibly truncated) content. For out-of-range
/// requests (start_line > totalLines), the payload is the header alone — no
/// trailing newline, no content. The header itself is NEVER truncated; only
/// the content portion is fed through `truncateOutput`. Use the input `path`
/// (relative) in the header — never `resolved` (absolute) — to avoid leaking
/// host paths through the LLM message history (CLAUDE.md invariant).
let private readFileImpl
    (projectRoot: string)
    (path: string)
    (lineRange: (int * int) option)
    (ct: CancellationToken)
    : Task<Result<ToolResult, AgentError>> =
    task {
        ct.ThrowIfCancellationRequested()

        match validatePath projectRoot path with
        | Error tr -> return Ok tr
        | Ok resolved ->
            try
                // TOOL-08: unify on ReadAllLines so we have totalLines for the header.
                let allLines = File.ReadAllLines(resolved)
                let totalLines = allLines.Length

                match lineRange with
                | Some(s, e) when not (s >= 1 && e >= s) ->
                    // Invalid range — keep the existing Failure path; no header.
                    return Ok(Failure(1, sprintf "[invalid line range: (%d, %d)]" s e))
                | _ ->
                    let headerStart, headerEnd, rawContent, status =
                        match lineRange with
                        | None ->
                            let raw = String.Join("\n", allLines)
                            let st =
                                if raw.Length > MESSAGE_HISTORY_CAP then "truncated"
                                else "not-truncated"
                            (1, totalLines, raw, st)
                        | Some(startLine, endLine) ->
                            if startLine > totalLines then
                                // out-of-range: preserve the RAW requested range in the header,
                                // empty content. Do NOT clamp endLine here (RESEARCH Pitfall 3).
                                (startLine, endLine, "", "out-of-range")
                            else
                                let selected =
                                    allLines
                                    |> Array.skip (startLine - 1)
                                    |> Array.truncate (endLine - startLine + 1)
                                let raw = String.Join("\n", selected)
                                let actualEnd = min endLine totalLines
                                let st =
                                    if raw.Length > MESSAGE_HISTORY_CAP then "truncated"
                                    else "not-truncated"
                                (startLine, actualEnd, raw, st)

                    let header =
                        sprintf
                            "[file: %s, lines %d-%d of %d, %s]"
                            path
                            headerStart
                            headerEnd
                            totalLines
                            status

                    let payload =
                        if status = "out-of-range" then
                            header
                        else
                            header + "\n" + truncateOutput rawContent

                    return Ok(Success payload)
            with
            | :? FileNotFoundException as ex -> return Ok(Failure(1, ex.Message))
            | :? DirectoryNotFoundException as ex -> return Ok(Failure(1, ex.Message))
            | :? UnauthorizedAccessException as ex -> return Ok(Failure(1, ex.Message))
            | :? IOException as ex -> return Ok(Failure(1, ex.Message))
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
    : Task<Result<ToolResult, AgentError>> =
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
                return Ok(Success(truncateOutput ""))
            with
            | :? UnauthorizedAccessException as ex -> return Ok(Failure(1, ex.Message))
            | :? IOException as ex -> return Ok(Failure(1, ex.Message))
    }

// ── list_dir (TOOL-03) ────────────────────────────────────────────────────────

/// Recursively enumerate directory entries up to maxDepth. Hidden files
/// (leading dot) are excluded. Directories are suffixed with `/`. Entries
/// are returned as relative paths joined by newlines.
let rec private enumDir (basePath: string) (current: string) (depth: int) (maxDepth: int) : string seq =
    seq {
        if depth > maxDepth then
            ()
        else
            let entries =
                try
                    Directory.EnumerateFileSystemEntries(current) |> Seq.sort
                with _ ->
                    Seq.empty

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
    : Task<Result<ToolResult, AgentError>> =
    task {
        ct.ThrowIfCancellationRequested()

        match validatePath projectRoot path with
        | Error tr -> return Ok tr
        | Ok resolved ->
            try
                if not (Directory.Exists resolved) then
                    return Ok(Failure(1, sprintf "Directory not found: %s" path))
                else
                    let requested = depth |> Option.defaultValue DEFAULT_LIST_DEPTH
                    let capped = min (max 1 requested) DEFAULT_LIST_DEPTH_MAX
                    let lines = enumDir resolved resolved 1 capped |> Seq.toArray
                    let body = String.Join("\n", lines)
                    return Ok(Success(truncateOutput body))
            with
            | :? UnauthorizedAccessException as ex -> return Ok(Failure(1, ex.Message))
            | :? IOException as ex -> return Ok(Failure(1, ex.Message))
    }

// ── run_shell (TOOL-04, TOOL-05 integration) ─────────────────────────────────
//
// Flow (sequentially, abort at first failure):
//   1. BashSecurity.validateCommand cmd
//        -> Error reason -> Ok (SecurityDenied reason). Process NEVER spawned.
//        -> Ok ()         -> continue.
//   2. Spawn /bin/bash -c cmd with:
//        ProcessStartInfo.WorkingDirectory = projectRoot   (working-dir lock)
//        RedirectStandardOutput = true
//        RedirectStandardError  = true
//        UseShellExecute        = false
//   3. Create a linked CancellationTokenSource:
//        cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
//        cts.CancelAfter(TimeSpan.FromSeconds SHELL_TIMEOUT_SECONDS)
//      The inner token fires either when the CALLER cancels (outer ct) OR
//      when the 30s timer elapses.
//   4. Read stdout AND stderr CONCURRENTLY (F# 10 `let! ... and! ...`) —
//      sequential read deadlocks when the process fills whichever buffer
//      is not being drained (dotnet/runtime #98347; PITFALLS.md C-2).
//   5. Await WaitForExitAsync(cts.Token).
//   6. On OperationCanceledException:
//        If outer ct was cancelled -> Error UserCancelled
//        Else (timeout fired)       -> kill entire process tree; Ok (Timeout 30)
//   7. On success: apply stdout 100KB cap, stderr 10KB cap (capOutput),
//      THEN apply 2000-char TOOL-06 truncation (truncateOutput) before
//      wrapping in ToolResult.Success or ToolResult.Failure.
//
// SHELL CHOICE: /bin/bash -c (NOT /bin/sh). The BashSecurity validators
// assume bash semantics (brace expansion, $() substitution, etc.).
// /bin/bash is always available on macOS (primary target). If bash is
// absent, Process.Start throws; caught as Error (ToolFailure ...).

let private runShellImpl
    (projectRoot: string)
    (cmd: string)
    (ct: CancellationToken)
    : Task<Result<ToolResult, AgentError>> =
    task {
        // Step 1: security gate — ALWAYS runs first; process is NEVER spawned
        //         if validateCommand returns Error.
        match validateCommand cmd with
        | Error reason -> return Ok(SecurityDenied reason)
        | Ok() ->
            // Step 2: process setup
            let psi = ProcessStartInfo("/bin/bash")
            psi.ArgumentList.Add("-c")
            psi.ArgumentList.Add(cmd)
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.UseShellExecute <- false
            psi.CreateNoWindow <- true
            psi.WorkingDirectory <- projectRoot

            // Step 3: linked CTS for 30s timeout + caller cancellation
            use cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
            cts.CancelAfter(TimeSpan.FromSeconds(float SHELL_TIMEOUT_SECONDS))

            // Attempt to start the process. Return early if Process.Start throws.
            let startResult =
                try
                    Ok(Process.Start(psi))
                with ex ->
                    Error ex

            match startResult with
            | Error ex ->
                // 30s hardcoded — _timeoutMs reserved for Phase 5 --timeout flag (see plan objective).
                // The Timeout field on Tool.RunShell is carried for error-reporting fidelity
                // only; runtime always uses SHELL_TIMEOUT_SECONDS in this phase.
                return
                    Error(
                        ToolFailure(
                            RunShell(Command cmd, BlueCode.Core.Domain.Timeout(SHELL_TIMEOUT_SECONDS * 1000)),
                            ex
                        )
                    )
            | Ok proc ->
                use _ = proc // Dispose proc when this scope exits.

                try
                    // Step 4: CONCURRENT read (deadlock avoidance — dotnet/runtime #98347)
                    let! stdout = proc.StandardOutput.ReadToEndAsync(cts.Token)
                    and! stderr = proc.StandardError.ReadToEndAsync(cts.Token)
                    // Step 5: wait for exit (both streams already closed at this point)
                    do! proc.WaitForExitAsync(cts.Token)

                    // Step 7: apply two-stage cap — raw resource cap first, then
                    //         TOOL-06 message-history cap.
                    let stdoutCapped = stdout |> fun s -> capOutput s SHELL_STDOUT_CAP |> truncateOutput
                    let stderrCapped = stderr |> fun s -> capOutput s SHELL_STDERR_CAP |> truncateOutput

                    if proc.ExitCode = 0 then
                        return Ok(Success stdoutCapped)
                    else
                        return Ok(Failure(proc.ExitCode, stderrCapped))
                with
                | :? OperationCanceledException ->
                    // Step 6: disambiguate caller cancel vs 30s timeout
                    if ct.IsCancellationRequested then
                        // Caller cancelled — kill tree, propagate as UserCancelled.
                        try
                            proc.Kill(entireProcessTree = true)
                        with _ ->
                            ()

                        return Error UserCancelled
                    else
                        // Timeout fired — kill tree, return ToolResult.Timeout.
                        try
                            proc.Kill(entireProcessTree = true)
                        with _ ->
                            ()

                        return Ok(ToolResult.Timeout SHELL_TIMEOUT_SECONDS)
                | ex ->
                    try
                        proc.Kill(entireProcessTree = true)
                    with _ ->
                        ()
                    // 30s hardcoded — _timeoutMs reserved for Phase 5 --timeout flag (see plan objective).
                    return
                        Error(
                            ToolFailure(
                                RunShell(Command cmd, BlueCode.Core.Domain.Timeout(SHELL_TIMEOUT_SECONDS * 1000)),
                                ex
                            )
                        )
    }

// ── glob pattern → regex converter (used by glob_search) ─────────────────────
//
// Handles **, *, ?, literal chars. IgnoreCase for cross-platform compat.
// `**` matches "any number of path segments"; `*` matches "any chars except /".
// `?` matches single non-/ char. Source: 08-RESEARCH.md Pattern 4, verified
// by dotnet fsi 2026-04-24.
let private globToRegex (pattern: string) : System.Text.RegularExpressions.Regex =
    let sb = System.Text.StringBuilder("^")
    let mutable i = 0
    while i < pattern.Length do
        let c = pattern.[i]
        if c = '*' && i + 1 < pattern.Length && pattern.[i+1] = '*' then
            sb.Append(".*") |> ignore
            i <- i + 2
            if i < pattern.Length && pattern.[i] = '/' then i <- i + 1
        elif c = '*' then sb.Append("[^/]*") |> ignore; i <- i + 1
        elif c = '?' then sb.Append("[^/]") |> ignore;  i <- i + 1
        elif c = '.' then sb.Append("\\.") |> ignore;   i <- i + 1
        else sb.Append(System.Text.RegularExpressions.Regex.Escape(string c)) |> ignore; i <- i + 1
    sb.Append("$") |> ignore
    System.Text.RegularExpressions.Regex(
        sb.ToString(),
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        ||| System.Text.RegularExpressions.RegexOptions.Compiled)

// ── edit_file (TLX-01) ───────────────────────────────────────────────────────
//
// Contract (REQUIREMENTS.md TLX-01):
//   oldString appears exactly 1 time → replace with newString, write file, Success ""
//   oldString appears 0 times        → Failure(1, "oldString not found")
//   oldString appears N≥2 times      → Failure(1, "oldString matches N times; refine to make unique")
//
// Critical pitfall (08-RESEARCH.md Pitfall 1): String.Replace replaces ALL
// occurrences silently. We count occurrences first via IndexOf loop and
// only call Replace once count = 1 is confirmed. File encoding (UTF-8
// default) and line endings (LF/CRLF preserved exactly) require no special
// handling — File.ReadAllText / File.WriteAllTextAsync preserve original bytes.

let private editFileImpl
    (projectRoot: string)
    (path: string)
    (oldString: string)
    (newString: string)
    (ct: CancellationToken)
    : Task<Result<ToolResult, AgentError>> =
    task {
        ct.ThrowIfCancellationRequested()
        match validatePath projectRoot path with
        | Error tr -> return Ok tr
        | Ok resolved ->
            try
                let content = File.ReadAllText(resolved)
                // Count occurrences (Ordinal comparison — exact bytes, not culture-dependent)
                let mutable count = 0
                let mutable idx = 0
                while idx >= 0 do
                    idx <- content.IndexOf(oldString, idx, StringComparison.Ordinal)
                    if idx >= 0 then
                        count <- count + 1
                        idx <- idx + oldString.Length
                match count with
                | 0 ->
                    return Ok(Failure(1, "oldString not found"))
                | 1 ->
                    let updated = content.Replace(oldString, newString, StringComparison.Ordinal)
                    do! File.WriteAllTextAsync(resolved, updated, ct)
                    return Ok(Success(truncateOutput ""))
                | n ->
                    return Ok(Failure(1, sprintf "oldString matches %d times; refine to make unique" n))
            with
            | :? FileNotFoundException as ex -> return Ok(Failure(1, ex.Message))
            | :? UnauthorizedAccessException as ex -> return Ok(Failure(1, ex.Message))
            | :? IOException as ex -> return Ok(Failure(1, ex.Message))
    }

// ── glob_search (TLX-02) ─────────────────────────────────────────────────────
//
// Contract (REQUIREMENTS.md TLX-02):
//   Input:  { pattern: string; path: FilePath option }
//   Output: newline-joined relative paths (from projectRoot), max 100,
//           with "[truncated: showing first 100 matches]" marker if capped.
//
// Hidden file policy (08-RESEARCH.md Pitfall 7): AttributesToSkip is set to
// FileAttributes.System only, NOT the default (Hidden | System). Agents
// legitimately need to find `.planning/X.md`, `.gitignore`, etc.

let private globSearchImpl
    (projectRoot: string)
    (pattern: string)
    (searchPath: string option)
    (ct: CancellationToken)
    : Task<Result<ToolResult, AgentError>> =
    task {
        ct.ThrowIfCancellationRequested()
        let searchRoot =
            match searchPath with
            | None -> Ok projectRoot
            | Some p -> validatePath projectRoot p
        match searchRoot with
        | Error tr -> return Ok tr
        | Ok root ->
            try
                let rx = globToRegex pattern
                let opts = EnumerationOptions()
                opts.RecurseSubdirectories <- true
                opts.AttributesToSkip <- FileAttributes.System
                let matches =
                    Directory.EnumerateFiles(root, "*", opts)
                    |> Seq.map (fun f -> Path.GetRelativePath(projectRoot, f).Replace('\\', '/'))
                    |> Seq.filter (fun rel -> rx.IsMatch(rel))
                    |> Seq.truncate 100
                    |> Seq.toArray
                let body =
                    if matches.Length = 100 then
                        String.Join("\n", matches) + "\n\n[truncated: showing first 100 matches]"
                    else
                        String.Join("\n", matches)
                return Ok(Success(truncateOutput body))
            with
            | :? UnauthorizedAccessException as ex -> return Ok(Failure(1, ex.Message))
            | :? IOException as ex -> return Ok(Failure(1, ex.Message))
    }

// ── grep_search (TLX-03) ─────────────────────────────────────────────────────
//
// Contract (REQUIREMENTS.md TLX-03):
//   Input:  { pattern: string; path: FilePath option; fileGlob: string option }
//   Output: newline-joined "relativePath:lineNumber:lineContent" lines, max 100.
//           lineContent truncated at 200 chars.
//
// Pitfalls addressed:
//   - 08-RESEARCH.md Pitfall 5 (catastrophic backtracking): 500ms per-line Regex timeout
//   - 08-RESEARCH.md Pitfall 4 (fileGlob with path separators): reject with Failure
//   - Binary files: File.ReadAllLines may throw on decoder errors; catch and skip

let private grepSearchImpl
    (projectRoot: string)
    (pattern: string)
    (searchPath: string option)
    (fileGlob: string option)
    (ct: CancellationToken)
    : Task<Result<ToolResult, AgentError>> =
    task {
        ct.ThrowIfCancellationRequested()
        // Reject fileGlob values containing path separators (see 08-RESEARCH.md Pitfall 4)
        match fileGlob with
        | Some g when g.Contains('/') || g.Contains('\\') ->
            return Ok(Failure(1, "file_glob must be a filename pattern without path separators"))
        | _ ->
            let searchRoot =
                match searchPath with
                | None -> Ok projectRoot
                | Some p -> validatePath projectRoot p
            match searchRoot with
            | Error tr -> return Ok tr
            | Ok root ->
                try
                    let globPattern = fileGlob |> Option.defaultValue "*"
                    let opts = EnumerationOptions()
                    opts.RecurseSubdirectories <- true
                    opts.AttributesToSkip <- FileAttributes.System

                    let rxOpt =
                        try
                            Some(System.Text.RegularExpressions.Regex(
                                pattern,
                                System.Text.RegularExpressions.RegexOptions.None,
                                TimeSpan.FromMilliseconds(500.0)))
                        with :? System.ArgumentException -> None

                    match rxOpt with
                    | None -> return Ok(Failure(1, sprintf "Invalid regex pattern: %s" pattern))
                    | Some regex ->
                        let results = System.Collections.Generic.List<string>()
                        let mutable hitCap = false

                        for file in Directory.EnumerateFiles(root, globPattern, opts) do
                            if not hitCap then
                                ct.ThrowIfCancellationRequested()
                                try
                                    let lines = File.ReadAllLines(file)
                                    for i in 0 .. lines.Length - 1 do
                                        if not hitCap then
                                            let line = lines.[i]
                                            let matched =
                                                try regex.IsMatch(line)
                                                with :? System.Text.RegularExpressions.RegexMatchTimeoutException -> false
                                            if matched then
                                                let relPath = Path.GetRelativePath(projectRoot, file).Replace('\\', '/')
                                                let truncLine =
                                                    if line.Length > 200 then line.Substring(0, 200)
                                                    else line
                                                results.Add(sprintf "%s:%d:%s" relPath (i + 1) truncLine)
                                                if results.Count >= 100 then hitCap <- true
                                with _ -> ()  // skip unreadable/binary files

                        let body =
                            if hitCap then
                                String.Join("\n", results) + "\n\n[truncated: showing first 100 matches]"
                            else
                                String.Join("\n", results)
                        return Ok(Success(truncateOutput body))
                with
                | :? UnauthorizedAccessException as ex -> return Ok(Failure(1, ex.Message))
                | :? IOException as ex -> return Ok(Failure(1, ex.Message))
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
            | ReadFile(FilePath path, lineRange) -> readFileImpl rootNormalized path lineRange ct
            | WriteFile(FilePath path, content) -> writeFileImpl rootNormalized path content ct
            | ListDir(FilePath path, depth) -> listDirImpl rootNormalized path depth ct
            | RunShell(Command cmd, BlueCode.Core.Domain.Timeout _timeoutMs) -> runShellImpl rootNormalized cmd ct
            | EditFile(FilePath path, oldStr, newStr) -> editFileImpl rootNormalized path oldStr newStr ct
            | GlobSearch(pattern, searchPath) ->
                globSearchImpl rootNormalized pattern (searchPath |> Option.map (fun (FilePath p) -> p)) ct
            | GrepSearch(pattern, searchPath, fileGlob) ->
                grepSearchImpl rootNormalized pattern (searchPath |> Option.map (fun (FilePath p) -> p)) fileGlob ct }
