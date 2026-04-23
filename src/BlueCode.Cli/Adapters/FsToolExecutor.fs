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
                let content =
                    match lineRange with
                    | None -> File.ReadAllText(resolved)
                    | Some(startLine, endLine) when startLine >= 1 && endLine >= startLine ->
                        let lines = File.ReadLines(resolved)

                        let selected =
                            lines
                            |> Seq.skip (startLine - 1)
                            |> Seq.truncate (endLine - startLine + 1)
                            |> Seq.toArray

                        String.Join("\n", selected)
                    | Some(s, e) ->
                        // Invalid range: report as Failure so the LLM can correct.
                        sprintf "[invalid line range: (%d, %d)]" s e

                return Ok(Success(truncateOutput content))
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
            | RunShell(Command cmd, BlueCode.Core.Domain.Timeout _timeoutMs) -> runShellImpl rootNormalized cmd ct }
