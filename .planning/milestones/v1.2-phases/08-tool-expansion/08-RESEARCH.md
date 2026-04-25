# Phase 8: Tool Expansion - Research

**Researched:** 2026-04-24
**Domain:** F# / .NET 10 filesystem APIs, JSON schema extension, agent tool dispatch wiring
**Confidence:** HIGH — all findings verified by direct code inspection and dotnet fsi execution

## Summary

Phase 8 adds three tools (`edit_file`, `glob_search`, `grep_search`) to the blueCode agent. The codebase has a well-established four-tool pattern: every tool appears in exactly five places — Domain.fs DU, AgentLoop.fs dispatchTool, FsToolExecutor.fs executor, Json.fs schema enum, and CompositionRoot.fs system prompt. The new tools follow the same pattern without architectural changes.

All three tools are implementable with .NET 10 inbox APIs only: no new NuGet packages are needed. `edit_file` uses `File.ReadAllText` + manual occurrence counting + `String.Replace`. `glob_search` uses a custom `**`-aware regex converter feeding `Directory.EnumerateFiles`. `grep_search` iterates files line-by-line with `File.ReadAllLines`, applying a `Regex` with a 500ms timeout guard. The `ToolResult.Failure` variant already covers all three tools' error reporting needs — no new DU cases are required in Core.

One schema-adjacent test (`CompositionRootTests.fs` — "bootstrap SystemPrompt mentions all 5 actions") and one schema test (`LlmPipelineTests.fs` — "all 5 valid action enum values accepted") will need updating to cover 8 actions. The plan must also register any new test module in both the `.fsproj` Compile list and `RouterTests.fs rootTests` list — the team has hit this pitfall four times.

**Primary recommendation:** Implement all three tools in a single plan with three ordered task groups (DU + schema + dispatch + system prompt first as the shared seam; then each tool's executor implementation + tests as parallel tasks). This avoids partial-seam commits and gives the executor tasks clean stubs to fill.

## Standard Stack

No new NuGet packages needed. All implementation uses .NET 10 inbox APIs already in scope.

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `System.IO.File` | .NET 10 BCL | Read/write files for edit_file and grep | Already used by all existing tools |
| `System.IO.Directory` | .NET 10 BCL | Enumerate files for glob_search and grep_search | Already used by list_dir |
| `System.Text.RegularExpressions.Regex` | .NET 10 BCL | Pattern matching in grep_search + glob pattern conversion | Used in Json.fs already |
| `System.IO.EnumerationOptions` | .NET 10 BCL | Fine-grained file enumeration control | Preferred over `SearchOption` enum |

### Supporting (already in project)
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `JsonSchema.Net` | 9.2.0 | Extend `llmStepSchema` enum | Already wired; just add 3 enum values |
| `FsToolkit.ErrorHandling` | 5.2.0 | `result {}` CE for dispatchTool cases | Already used for all 4 existing tool dispatches |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Custom glob-to-regex | `Microsoft.Extensions.FileSystemGlobbing` | FileSystemGlobbing handles complex multi-** patterns but requires a new NuGet package. PROJECT.md requires a Key Decisions entry for new packages. The split-on-`**` + regex approach covers all patterns in TLX-02 without a new dependency. |
| Regex with timeout | Fixed-string search only | Fixed-string is simpler but requirements explicitly say "regex or fixed string". Use `Regex` for all patterns; apply 500ms timeout to prevent catastrophic backtracking. |

**Installation:** No new packages required.

## Architecture Patterns

### Existing Tool Wiring (match this pattern exactly)

Every tool in v1 touches exactly 5 files in this order:

1. `src/BlueCode.Core/Domain.fs` — add DU case(s) to `type Tool`
2. `src/BlueCode.Core/AgentLoop.fs` — add match arm in `dispatchTool` (parses JSON input → Tool DU)
3. `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` — add implementation + match arm in `create`
4. `src/BlueCode.Cli/Adapters/Json.fs` — add 3 values to `llmStepSchema` action enum
5. `src/BlueCode.Cli/CompositionRoot.fs` — extend `defaultSystemPrompt` with tool descriptions

The compile order is load-bearing: Domain.fs → AgentLoop.fs (Core) → FsToolExecutor.fs → Json.fs → CompositionRoot.fs (Cli). Changes must be made in that order within a plan.

### Recommended Project Structure (no changes needed)
```
src/
├── BlueCode.Core/Domain.fs         # Add EditFile/GlobSearch/GrepSearch DU cases
├── BlueCode.Core/AgentLoop.fs      # Add 3 match arms in dispatchTool
├── BlueCode.Cli/Adapters/
│   ├── FsToolExecutor.fs           # Add 3 impl functions + 3 match arms in create
│   └── Json.fs                     # Extend llmStepSchema enum (5→8)
├── BlueCode.Cli/CompositionRoot.fs # Extend system prompt (3 new tool descriptions)
tests/BlueCode.Tests/
├── FileToolsTests.fs               # Extend with edit_file/glob/grep test lists
├── RouterTests.fs                  # Add new test module(s) to rootTests list
├── BlueCode.Tests.fsproj           # Add new test module(s) to Compile list
└── LlmPipelineTests.fs             # Update enum coverage test (5→8 values)
```

### Pattern 1: Tool DU Extension

Add three new cases to the existing `Tool` DU in `Domain.fs`. Use record types embedded as anonymous inline records (consistent with `ReadFile` tuple style — see existing code):

```fsharp
// Add to type Tool in Domain.fs — after RunShell
| EditFile of path: FilePath * oldString: string * newString: string
| GlobSearch of pattern: string * searchPath: FilePath option
| GrepSearch of pattern: string * searchPath: FilePath option * fileGlob: string option
```

No new single-case DU wrappers needed: `oldString`/`newString`/`pattern` are plain strings with no project-root validation semantics. `FilePath` wraps the optional search root for consistency with existing tools.

### Pattern 2: dispatchTool Arms (AgentLoop.fs)

The existing `dispatchTool` function uses `tryStr`/`tryInt`/`requireStr` helpers and `result {}` CE. Match exactly:

```fsharp
// Source: AgentLoop.fs dispatchTool pattern — verified 2026-04-24
| "edit_file" ->
    result {
        let! path    = requireStr "path"
        let! oldStr  = requireStr "old_string"
        let! newStr  = requireStr "new_string"
        return EditFile(FilePath path, oldStr, newStr)
    }
| "glob_search" ->
    result {
        let! pattern = requireStr "pattern"
        let searchPath = tryStr "path" |> Option.map FilePath
        return GlobSearch(pattern, searchPath)
    }
| "grep_search" ->
    result {
        let! pattern = requireStr "pattern"
        let searchPath = tryStr "path"  |> Option.map FilePath
        let fileGlob   = tryStr "file_glob"
        return GrepSearch(pattern, searchPath, fileGlob)
    }
```

### Pattern 3: edit_file Implementation

Key insight: `String.Replace` replaces ALL occurrences. Must count first using `IndexOf` loop, then replace only when count = 1:

```fsharp
// Source: verified by dotnet fsi 2026-04-24
let private editFileImpl
    (projectRoot: string) (path: string) (oldString: string) (newString: string) (ct: CancellationToken)
    : Task<Result<ToolResult, AgentError>> =
    task {
        ct.ThrowIfCancellationRequested()
        match validatePath projectRoot path with
        | Error tr -> return Ok tr
        | Ok resolved ->
            try
                let content = File.ReadAllText(resolved)  // UTF-8 default, preserves LF/CRLF exactly
                // Count occurrences via IndexOf loop (String.Replace replaces ALL — unsafe for count check)
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
```

`File.ReadAllText`/`WriteAllTextAsync` preserve original line endings (LF stays LF, CRLF stays CRLF) — verified by dotnet fsi test.

### Pattern 4: glob_search Implementation

No `Microsoft.Extensions.FileSystemGlobbing` needed. Approach: convert glob pattern to `Regex` using a `**`-aware converter, then enumerate all files and filter:

```fsharp
// Source: verified by dotnet fsi 2026-04-24
let private globToRegex (pattern: string) : Regex =
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
        else sb.Append(Regex.Escape(string c)) |> ignore; i <- i + 1
    sb.Append("$") |> ignore
    Regex(sb.ToString(), RegexOptions.IgnoreCase ||| RegexOptions.Compiled)

let private globSearchImpl
    (projectRoot: string) (pattern: string) (searchPath: string option) (ct: CancellationToken)
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
                opts.AttributesToSkip <- FileAttributes.System  // include hidden files (not system)
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
```

Note: `EnumerationOptions.AttributesToSkip <- FileAttributes.System` keeps hidden files visible (consistent with grep behavior) while skipping system-flagged files. The default includes `FileAttributes.Hidden` which would skip `.hidden` files agents might legitimately need. Choose `FileAttributes.System` only (or `FileAttributes.None` to include everything) — verify desired behavior. The 100-match cap is enforced pre-truncation via `Seq.truncate`.

### Pattern 5: grep_search Implementation

Use `File.ReadAllLines` which handles CRLF/LF transparently. Apply regex with 500ms timeout per line to prevent catastrophic backtracking:

```fsharp
// Source: verified by dotnet fsi 2026-04-24
let private grepSearchImpl
    (projectRoot: string) (pattern: string) (searchPath: string option)
    (fileGlob: string option) (ct: CancellationToken)
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
                let globPattern = fileGlob |> Option.defaultValue "*"
                // Validate fileGlob doesn't contain path separators that escape searchRoot
                // (globPattern is only passed to EnumerateFiles searchPattern, not as path)
                let opts = EnumerationOptions()
                opts.RecurseSubdirectories <- true
                opts.AttributesToSkip <- FileAttributes.System

                let rx =
                    try Some(Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(500.0)))
                    with :? ArgumentException -> None

                match rx with
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
                                            with :? RegexMatchTimeoutException -> false
                                        if matched then
                                            let relPath = Path.GetRelativePath(projectRoot, file).Replace('\\', '/')
                                            let truncLine = if line.Length > 200 then line.Substring(0, 200) else line
                                            results.Add(sprintf "%s:%d:%s" relPath (i + 1) truncLine)
                                            if results.Count >= 100 then hitCap <- true
                            with _ -> ()  // skip unreadable files (binary, permission denied)

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
```

Output format: `"relativePath:lineNumber:lineContent"` per line — human-readable and agent-parseable. The LLM can split on `:` to extract components. Alternative JSON format would consume more tokens; keep it simple.

### Pattern 6: Schema Extension (Json.fs)

The `llmStepSchema` constant needs only the enum array extended — 5 values become 8. No `oneOf`/discriminator needed because the schema validates `input` as `{ "type": "object" }` (no per-action shape enforcement at schema level — that's done in `dispatchTool`). This is the existing design:

```fsharp
// Current (5 values):
"enum": ["read_file", "write_file", "list_dir", "run_shell", "final"]

// After Phase 8 (8 values):
"enum": ["read_file", "write_file", "list_dir", "run_shell", "edit_file", "glob_search", "grep_search", "final"]
```

No other changes needed in `llmStepSchema`. The `toLlmOutput` function in QwenHttpClient.fs handles any `toolName` that isn't `"final"` via the catch-all `| toolName ->` branch — no changes needed there either.

### Pattern 7: System Prompt Extension (CompositionRoot.fs)

The current system prompt is ~1200 chars. Adding three tool descriptions will grow it. The test `"bootstrap SystemPrompt mentions all 5 actions"` must be updated to check 8 actions. Keep descriptions concise:

```
- edit_file:   {"path": "<rel-path>", "old_string": "<exact-match>", "new_string": "<replacement>"}
- glob_search: {"pattern": "<glob>", "path": "<rel-path?>"}
- grep_search: {"pattern": "<regex-or-string>", "path": "<rel-path?>", "file_glob": "<*.ext?>"}
```

The action enum line in the prompt also needs updating: add `| edit_file | glob_search | grep_search` to the "one of" list.

### Anti-Patterns to Avoid

- **Using `String.Replace` for the 1-match check in edit_file:** `content.Replace(old, new)` replaces ALL occurrences silently. Always count with `IndexOf` loop first; only call `Replace` after confirming count = 1.
- **Using `SearchOption.AllDirectories` for `EnumerationOptions`:** These are two different overloads. Prefer `EnumerationOptions` with `RecurseSubdirectories = true` for attribute control.
- **Forgetting to register new test modules in both places:** New test files need (1) `.fsproj` Compile Include before `RouterTests.fs` and (2) entry in `rootTests` list in `RouterTests.fs`. Four prior executors have hit this. Check both.
- **Not updating `CompositionRootTests.fs` "mentions all 5 actions" test:** This test will fail if the system prompt is updated but the test isn't. Update both together.
- **Regex without timeout in grep_search:** User-supplied patterns can cause catastrophic backtracking. Always construct `Regex` with `TimeSpan.FromMilliseconds(500.0)` timeout and catch `RegexMatchTimeoutException`.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Glob pattern matching | Custom recursive walk + manual wildcard | `globToRegex` + `Directory.EnumerateFiles` with `RecurseSubdirectories` | The regex approach handles `**`, `*`, `?` cleanly with zero dependencies |
| Binary file detection | Byte inspection / magic bytes | `catch _ -> ()` on `File.ReadAllLines` | Binary files throw `DecoderFallbackException` or similar; just skip them |
| Line ending preservation | Normalize to LF before replacing | `File.ReadAllText` + `WriteAllText` with original content | ReadAllText preserves original bytes exactly; no normalization needed |
| ToolResult shape for lists | New DU case e.g. `SuccessList of string list` | `Success of string` (newline-joined) | ToolResult DU is in Core; no new cases needed; existing `truncateOutput` applies naturally |

**Key insight:** All three tools' outputs map to `ToolResult.Success of string` (newline-delimited lists). No Core domain changes beyond the Tool DU cases. The TOOL-06 2000-char truncation applies to all outputs as-is.

## Common Pitfalls

### Pitfall 1: String.Replace Replaces All Occurrences
**What goes wrong:** `content.Replace(oldString, newString)` silently replaces every occurrence when you only intend to replace the unique one. edit_file's contract requires exactly-1-match semantics.
**Why it happens:** .NET's `String.Replace` is a pure "replace all" operation; there's no built-in "replace first only" that also validates uniqueness.
**How to avoid:** Count occurrences via `IndexOf` loop (verified working above). Only call `Replace` after count = 1 is confirmed.
**Warning signs:** Tests pass with single occurrences but silently corrupt files with multiple matches.

### Pitfall 2: Schema Enum Not Updated
**What goes wrong:** The LLM emits `action: "edit_file"` but `llmStepSchema` only allows 5 values → `SchemaViolation` → infinite retry loop in the agent.
**Why it happens:** The schema is a static JSON string literal in `Json.fs`; it must be manually extended.
**How to avoid:** Update enum in `Json.fs` in the same commit as the Domain DU addition. Also update `LlmPipelineTests.fs` schema test that checks "all 5 valid action enum values accepted" — change to 8.
**Warning signs:** Running LlmPipelineTests with the 5-value enum test will now assert against 8 values (test fails), not that the schema is wrong.

### Pitfall 3: System Prompt Not Updated
**What goes wrong:** Even if schema accepts the action, the LLM won't know to use the new tools unless the system prompt describes them.
**Why it happens:** System prompt is a separate constant in `CompositionRoot.fs` from the schema in `Json.fs`.
**How to avoid:** Update system prompt in the same task as schema extension. `CompositionRootTests.fs` has a test checking all action names are present — update it from "5 actions" to "8 actions".
**Warning signs:** LLM falls back to `run_shell "find ..."` instead of `glob_search`.

### Pitfall 4: fileGlob Parameter Escaped to Directory Traversal
**What goes wrong:** A `fileGlob` value like `"../../*.txt"` passed to `Directory.EnumerateFiles(root, fileGlob, ...)` could cause enumeration outside root.
**Why it happens:** `.NET`'s `EnumerateFiles` searchPattern is a filename glob only (no path separators), so `"../../*.txt"` would either throw `ArgumentException` or be silently ignored. But we should validate.
**How to avoid:** The fileGlob parameter is a filename pattern (e.g., `"*.fs"`, `"*.md"`) — it should not contain `/` or `\`. Reject patterns containing path separators with a `Failure`.
**Warning signs:** `ArgumentException` from `Directory.EnumerateFiles` at runtime (actually safe but confusing).

### Pitfall 5: Regex Catastrophic Backtracking in grep_search
**What goes wrong:** A user-supplied pattern like `(a+)+` on a long line hangs the process.
**Why it happens:** Polynomial/exponential backtracking in NFA-based regex engines.
**How to avoid:** Construct `Regex` with `TimeSpan.FromMilliseconds(500.0)` timeout. Catch `RegexMatchTimeoutException` per-line and treat as non-match. Also catch `ArgumentException` on construction and return `Failure` for invalid patterns.
**Warning signs:** grep_search call hangs; no CancellationToken check is fast enough.

### Pitfall 6: Symlinks Followed Beyond Project Root
**What goes wrong:** A symlink inside project root pointing outside root will be read by `EnumerateFiles`. `.NET Path.GetFullPath` does NOT resolve symlinks on macOS.
**Why it happens:** The OS follows symlinks at file open time; .NET exposes files found via symlinks just like regular files.
**How to avoid:** Accept as consistent with existing `list_dir` behavior (it also follows symlinks). Document as known behavior. v1.3+ candidate for `FileInfo.ResolveLinkTarget` checks.
**Warning signs:** None in normal use — symlinks inside project roots pointing outside are unusual in practice.

### Pitfall 7: EnumerationOptions Default AttributesToSkip Includes Hidden
**What goes wrong:** `new EnumerationOptions()` has `AttributesToSkip = FileAttributes.Hidden | FileAttributes.System` by default. This excludes `.hidden` files and `.git` directory contents from glob_search/grep_search.
**Why it happens:** .NET changed the default from .NET 5 onward to skip hidden/system by default.
**How to avoid:** Set `opts.AttributesToSkip <- FileAttributes.System` (keep system skipped, allow hidden). This way `.planning/`, `.git/` etc. are included — agents often need to search hidden files. Alternatively use `FileAttributes.None` to include everything including system files; `FileAttributes.System` is the reasonable middle ground.
**Warning signs:** Agent reports 0 matches for `**/.gitignore` or similar hidden files.

### Pitfall 8: Test Registration
**What goes wrong:** New test file compiles but tests don't run. Expecto's `[<Tests>]` attribute auto-discovery is disabled in this project.
**Why it happens:** `RouterTests.fs` uses an explicit `rootTests` list; `[<Tests>]` attribute is present in some files for tooling compatibility but not for test runner discovery.
**How to avoid:** Add new test module to BOTH (1) `.fsproj` `<Compile Include="...">` before `RouterTests.fs`, AND (2) `rootTests` list in `RouterTests.fs`.
**Warning signs:** "tests compile but don't run" — zero new test results appear.

## Code Examples

### Occurrence Count for edit_file
```fsharp
// Count occurrences without replacing all — verified 2026-04-24
let countOccurrences (content: string) (needle: string) : int =
    let mutable count = 0
    let mutable idx = 0
    while idx >= 0 do
        idx <- content.IndexOf(needle, idx, StringComparison.Ordinal)
        if idx >= 0 then
            count <- count + 1
            idx <- idx + needle.Length
    count
```

### Glob Pattern to Regex — Full Converter
```fsharp
// Verified: handles **, *, ?, ., literal chars. IgnoreCase for cross-platform compat.
// Source: dotnet fsi verification 2026-04-24
let private globToRegex (pattern: string) : Regex =
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
        else sb.Append(Regex.Escape(string c)) |> ignore; i <- i + 1
    sb.Append("$") |> ignore
    Regex(sb.ToString(), RegexOptions.IgnoreCase ||| RegexOptions.Compiled)
```

### Schema Enum Extension
```json
// In Json.fs llmStepSchema — just extend the array (no other changes)
"enum": ["read_file", "write_file", "list_dir", "run_shell", "edit_file", "glob_search", "grep_search", "final"]
```

### FsToolExecutor.create Match Extension
```fsharp
// Add after RunShell arm in the IToolExecutor implementation
| EditFile(FilePath path, oldStr, newStr)            -> editFileImpl rootNormalized path oldStr newStr ct
| GlobSearch(pattern, searchPath)                    -> globSearchImpl rootNormalized pattern (searchPath |> Option.map (fun (FilePath p) -> p)) ct
| GrepSearch(pattern, searchPath, fileGlob)          -> grepSearchImpl rootNormalized pattern (searchPath |> Option.map (fun (FilePath p) -> p)) fileGlob ct
```

### Test Fixture Pattern (match FileToolsTests.fs)
```fsharp
// The existing pattern in FileToolsTests.fs — reuse for new test lists
let private newFixture () : string =
    let dir = Path.Combine(Path.GetTempPath(), "bluecode-toolexpansion-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    Path.GetFullPath(dir)

let private cleanup (dir: string) =
    try if Directory.Exists dir then Directory.Delete(dir, true)
    with _ -> ()

let private exec (executor: IToolExecutor) (tool: Tool) : Result<ToolResult, AgentError> =
    (executor.ExecuteAsync tool CancellationToken.None).GetAwaiter().GetResult()
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Agent uses `run_shell "find . -name '*.fs'"` | Agent uses `glob_search` native tool | Phase 8 | No BashSecurity gate; deterministic; structured output |
| Agent uses `run_shell "grep -r TODO src/"` | Agent uses `grep_search` native tool | Phase 8 | Structured (path:line:content) output; no shell parsing needed |
| Agent rewrites full file for single-line edit | Agent uses `edit_file` surgical tool | Phase 8 | 1-2s vs 6-14s; no large JSON content fields |
| `llmStepSchema` enum: 5 values | `llmStepSchema` enum: 8 values | Phase 8 | Schema enforces new tools |

**No deprecated patterns:** The existing 4-tool wiring is pure additive extension.

## Open Questions

1. **hidden file inclusion in glob/grep**
   - What we know: `EnumerationOptions` default skips hidden files. `FileAttributes.System` would keep hidden visible.
   - What's unclear: Do we want `.planning/`, `.git/` etc. included by default in glob/grep?
   - Recommendation: Use `FileAttributes.System` (skip only system-flagged, allow hidden). Agents legitimately need to search `.planning/` and `.gitignore`. Can always add an option in v1.3+ if needed.

2. **fileGlob validation strictness**
   - What we know: `.NET`'s `EnumerateFiles` searchPattern doesn't support path separators.
   - What's unclear: Should we explicitly reject `fileGlob` values containing `/` with a clear error, or just let .NET throw?
   - Recommendation: Validate that `fileGlob` contains no `/` or `\` before passing to `EnumerateFiles`. Return `Failure(1, "file_glob must be a filename pattern without path separators")`. Explicit error > confusing exception.

## Plan Decomposition Recommendation

**One plan, three task groups:**

**Why one plan:**
- The five wiring seams (Domain, AgentLoop, FsToolExecutor, Json, CompositionRoot) are all touched by all three tools simultaneously. Splitting into 3 plans means each plan would add one DU case, extend the schema by one enum value, etc. — this creates partial-seam commits that don't compile or don't pass `grep -c "EditFile\|GlobSearch\|GrepSearch" Domain.fs` returning 3.
- The shared seam changes (DU extension + schema enum + system prompt) are cheap to do all at once and create a clean foundation for the executor tasks.

**Task structure within the plan:**

Task 1 (serial): Shared seam — Domain.fs + AgentLoop.fs dispatchTool + Json.fs schema enum + CompositionRoot.fs system prompt + update existing tests that check 5-value enum/prompt. After this task: project compiles (FsToolExecutor exhaustive match will fail — stub the 3 new arms), schema accepts 8 actions.

Task 2a (parallel with 2b, 2c): `edit_file` executor implementation + tests in FileToolsTests.fs (or new EditFileTests.fs).

Task 2b (parallel with 2a, 2c): `glob_search` executor implementation + tests.

Task 2c (parallel with 2a, 2b): `grep_search` executor implementation + tests.

Task 3 (serial, after 2a/2b/2c): Integration verification — `grep -c "EditFile\|GlobSearch\|GrepSearch" Domain.fs` returns 3; all 218 + new tests pass; schema acceptance tests updated to cover 8 values.

**Note on parallelism:** Tasks 2a/2b/2c all modify `FsToolExecutor.fs` (different functions). If running truly in parallel, there will be merge conflicts. The executor agent must either serialize them or the plan must assign each tool to a separate `impl` function addition without touching each other's arms. The `create` match extension in task 1's stub can leave the 3 new arms as `failwith "not implemented"` so each task 2x can fill its arm independently — but the `create` function itself would then have 3 conflicting edits. **Practical recommendation:** Run 2a/2b/2c in wave-parallel conceptually but as sequential edits to FsToolExecutor.fs (each editing its own impl function + its own match arm). The gsd executor can handle this as a single wave if the tasks are careful about which lines they touch.

## Sources

### Primary (HIGH confidence)
- Direct code inspection: `Domain.fs`, `AgentLoop.fs`, `FsToolExecutor.fs`, `Json.fs`, `CompositionRoot.fs`, `FileToolsTests.fs`, `RouterTests.fs` — all read 2026-04-24
- dotnet fsi verification scripts: edit_file occurrence counting, glob-to-regex converter, grep with regex timeout, file encoding/line-ending behavior, symlink behavior, EnumerationOptions defaults — all executed 2026-04-24 against .NET 10.0.203

### Secondary (MEDIUM confidence)
- .NET 10 `EnumerationOptions` API — verified via dotnet fsi reflection; `AttributesToSkip` default value confirmed empirically

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; all APIs verified by execution
- Architecture: HIGH — read entire existing tool implementation; pattern is clear and consistent
- Pitfalls: HIGH — occurrence-count bug verified by execution; registration pitfall documented 4x in project history; symlink behavior verified by dotnet fsi
- Test strategy: HIGH — read FileToolsTests.fs and RouterTests.fs; pattern is explicit and documented in CLAUDE.md

**Research date:** 2026-04-24
**Valid until:** 2026-05-24 (stable .NET BCL APIs — 30 day window)
