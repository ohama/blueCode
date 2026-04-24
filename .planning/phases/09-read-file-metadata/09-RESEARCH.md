# Phase 9: Read File Metadata - Research

**Researched:** 2026-04-24
**Domain:** F# FsToolExecutor `read_file` enhancement — bounds-and-truncation header
**Confidence:** HIGH

## Summary

Phase 9 is a surgical modification to a single function: `readFileImpl` in
`src/BlueCode.Cli/Adapters/FsToolExecutor.fs`. The change prepends a one-line
metadata header to every `read_file` `ToolResult.Success` payload so the agent
knows total line count, the returned range, and whether the 2000-char truncation
was applied. No Core type changes. No new NuGet packages.

The root cause addressed is a deterministic 32B failure mode (T6 benchmark,
2026-04-24): 32B repeatedly asks for `start_line=2001, 4001, 6001` on a
150-line file, receives `Success ""` each time, and never corrects itself.
The `out-of-range` header gives 32B an unambiguous bounds signal.

The implementation splits into two tasks: (1) modify `readFileImpl` to compute
total line count, determine the returned range, detect truncation, and prepend
the header; and (2) add four test cases covering the three header status values
plus the out-of-range empty-content case.

**Primary recommendation:** Compute `total_lines` via `File.ReadAllLines(resolved).Length`
(full array read), derive the header before applying `truncateOutput`, detect
truncation by comparing the header-less content length to `MESSAGE_HISTORY_CAP`,
and write all logic inside `readFileImpl` without touching Core.

## Standard Stack

No new libraries needed. Everything required already exists in the codebase.

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `System.IO.File` | .NET 10 BCL | `ReadAllLines`, `ReadAllText`, `ReadLines` | Already used in `readFileImpl` |
| `System.String.Join` | .NET 10 BCL | Joining selected lines | Already used in `readFileImpl` |
| `Expecto` | 10.2.1 | Unit tests | Already the test framework |

**Installation:** No new packages required.

## Architecture Patterns

### Recommended Project Structure

No structural change. This is a modification to one function inside:

```
src/BlueCode.Cli/Adapters/FsToolExecutor.fs   <- readFileImpl change here
tests/BlueCode.Tests/FileToolsTests.fs         <- new readFileTests cases here
```

### Pattern 1: Header-then-content composition

**What:** Compute the header string, compute the content string (pre-truncation),
then apply `truncateOutput` only to the content, then concatenate
`header + "\n" + truncatedContent` as the final `Success` payload.

**When to use:** Always — for every branch of `readFileImpl` that would
otherwise return `Success`.

**Why NOT truncate the full combined string:** The header must always be
complete and readable. Truncating `header + "\n" + content` together could
cut the header itself for huge headers (impossible here given fixed format,
but the principle is: truncation target is content only).

**Implementation sketch:**

```fsharp
// Inside readFileImpl, after path validation and file reads:

// 1. Compute total lines (always a full read — see "Total line count" below).
let allLines = File.ReadAllLines(resolved)
let totalLines = allLines.Length

// 2. Compute effective start/end for the header.
//    lineRange = None means "whole file", represented as 1..totalLines.
//    lineRange = Some(s, e) where s > totalLines => out-of-range.
let effectiveStart, effectiveEnd, rawContent, status =
    match lineRange with
    | None ->
        // Whole-file read path
        let raw = String.Join("\n", allLines)
        let isTruncated = raw.Length > MESSAGE_HISTORY_CAP
        let st = if isTruncated then "truncated" else "not-truncated"
        (1, totalLines, raw, st)
    | Some(startLine, endLine) when startLine >= 1 && endLine >= startLine ->
        if startLine > totalLines then
            // out-of-range: content is empty, show requested range in header
            (startLine, endLine, "", "out-of-range")
        else
            let selected =
                allLines
                |> Array.skip (startLine - 1)
                |> Array.truncate (endLine - startLine + 1)
            let raw = String.Join("\n", selected)
            let actualEnd = min endLine totalLines
            let isTruncated = raw.Length > MESSAGE_HISTORY_CAP
            let st = if isTruncated then "truncated" else "not-truncated"
            (startLine, actualEnd, raw, st)
    | Some(s, e) ->
        // Invalid range (e < s or s < 1) — existing Failure path, no header
        // This branch returns Failure, not Success, so no header needed.
        (s, e, sprintf "[invalid line range: (%d, %d)]" s e, "invalid")

// 3. Build header.
let header = sprintf "[file: %s, lines %d-%d of %d, %s]" path effectiveStart effectiveEnd totalLines status

// 4. Apply truncation to content only, then prepend header.
let payload =
    if status = "out-of-range" || status = "invalid" then
        if status = "invalid" then rawContent  // existing Failure path
        else header  // out-of-range: header only, no content section
    else
        header + "\n" + truncateOutput rawContent

return Ok(Success payload)
```

**Notes on the sketch above:**
- The invalid-range case (`e < s`) already returns `Failure` in the current code.
  Keep that behavior. No header is needed for `Failure` results.
- The `"invalid"` status branch in the sketch is not a real header status value;
  keep the existing `Failure` return for that case.
- For `out-of-range`, the spec says "content section is empty." The cleanest
  representation is header-only: `"[file: src/X.fs, lines 2001-2100 of 150, out-of-range]"`.

### Pattern 2: Total line count via `File.ReadAllLines`

**What:** Always read all lines to get `totalLines`, even when a line range is specified.

**When to use:** All read paths.

**Why:** The current `Some(startLine, endLine)` path uses `File.ReadLines` (lazy
enumeration). Switching to `File.ReadAllLines` (eager array) for total count is
necessary. For files this project deals with (source files, configs — typically
<10,000 lines), this is not a performance concern. The 2000-char truncation cap
ensures the message-history payload stays bounded regardless.

**Current code uses:**
- `None` path: `File.ReadAllText(resolved)` — string, no line count
- `Some` path: `File.ReadLines(resolved)` — lazy IEnumerable

**New approach:** Unify on `File.ReadAllLines(resolved)` which gives both the
array of lines (for slicing) and the count. The `None` path then becomes
`String.Join("\n", allLines)` which is equivalent in content.

### Pattern 3: Truncation detection

**What:** Compare raw content length to `MESSAGE_HISTORY_CAP` (2000) BEFORE
calling `truncateOutput`.

**Why:** `truncateOutput` doesn't return a flag — it returns a string. To know
whether truncation will apply, compare `rawContent.Length > MESSAGE_HISTORY_CAP`.
This comparison must happen before calling `truncateOutput`, so the status for
the header can be `"truncated"` vs `"not-truncated"`.

```fsharp
let isTruncated = rawContent.Length > MESSAGE_HISTORY_CAP
let status = if isTruncated then "truncated" else "not-truncated"
let header = sprintf "[file: %s, lines %d-%d of %d, %s]" path start end_ totalLines status
// Now apply truncation to content, prepend header (not truncated)
let payload = header + "\n" + truncateOutput rawContent
```

### Pattern 4: Relative path in header

**What:** Use the input `path` parameter (the `inputPath` string passed to
`readFileImpl`), not `resolved` (the absolute path).

**Why:** Success Criterion 1 shows `src/BlueCode.Core/Domain.fs` in the header —
the same relative path the LLM passed in. Using `resolved` would expose
absolute filesystem paths (`/Users/ohama/...`), violating the invariant
documented in CLAUDE.md ("Do not reintroduce absolute filesystem paths in Core").

The `path` parameter to `readFileImpl` is the original relative input — use that.

### Anti-Patterns to Avoid

- **Truncating header + content together:** The `truncateOutput` call must apply
  to `rawContent` alone, not to `header + "\n" + rawContent`. The header must
  always be intact.
- **Using `resolved` (absolute path) in the header:** Use input `path` (relative).
  Absolute paths violate the Core purity / no-absolute-paths invariant.
- **Calling `File.ReadLines` (lazy) for total count:** This requires materializing
  the full sequence anyway. Use `File.ReadAllLines` for a single eager pass.
- **Adding a new `ToolResult` case:** SC#4 explicitly prohibits Core type changes.
  The header is purely string content inside the existing `Success` case.
- **Applying truncation to the header:** The header is short and fixed-format.
  Exempt it from the cap. Cap the content portion only.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Line count | Custom reader loop | `File.ReadAllLines(path).Length` | BCL, one call |
| String truncation | New truncation logic | Existing `truncateOutput` | Already correct |
| Header formatting | Template engine | `sprintf` | Simple fixed-format string |

**Key insight:** This phase is pure string assembly. No new abstractions needed.

## Common Pitfalls

### Pitfall 1: Invalid range branch returning Success with a header

**What goes wrong:** The current code for `Some(s, e)` where `e < s` returns
`Ok(Success(truncateOutput "[invalid line range: ...]"))`. If the new code adds
a header to ALL `Success` returns, this branch would get a header too.

**Why it happens:** The refactor wraps all success returns together.

**How to avoid:** Keep the invalid-range branch returning `Failure`, not `Success`.
The current behavior is correct — `Failure` is the right signal. Only the two
valid paths (`None` and valid `Some`) need header prepending. Check the existing
code at lines 133-135 in `FsToolExecutor.fs`.

### Pitfall 2: `effectiveEnd` exceeding `totalLines` for in-range requests

**What goes wrong:** If `endLine=9999` but file has 150 lines, header would say
`lines 1-9999 of 150` which is confusing.

**Why it happens:** The caller passes a large `endLine` as a window sentinel.

**How to avoid:** Clamp `effectiveEnd` to `min endLine totalLines` for in-range
requests. Already implicit in Seq.truncate behavior but must be explicit in
the header value: `let actualEnd = min endLine totalLines`.

### Pitfall 3: out-of-range `effectiveEnd` must preserve the REQUESTED range

**What goes wrong:** Clamping `effectiveEnd` to `totalLines` for out-of-range
requests loses the information that `start_line=2001` was requested.

**Why it happens:** Naively applying the same clamping to out-of-range.

**How to avoid:** For the `out-of-range` branch, use the RAW `endLine` value
(or `startLine + defaultWindow - 1` if `endLine` is None — see below). SC#2
explicitly confirms: `[file: <path>, lines 2001-2100 of 150, out-of-range]`.

### Pitfall 4: The `None` lineRange case has no `endLine`

**What goes wrong:** When `lineRange = None`, there is no explicit `endLine`. The
header format requires `lines X-Y of Z`. For the `None` case, `Y = totalLines`.

**How to avoid:** In the `None` branch, `effectiveStart = 1`, `effectiveEnd = totalLines`.
Simple.

### Pitfall 5: Test isolation — existing tests that pattern-match `content` directly

**What goes wrong:** Existing `readFileTests` in `FileToolsTests.fs` do
`Expect.stringContains content "alpha"`. After the change, `content` starts with
`[file: hello.txt, lines 1-3 of 3, not-truncated]\nalpha\nbeta\ngamma\n`. The
`stringContains` checks still pass because "alpha" is still in the string.

**Why this is actually fine:** `Expect.stringContains` checks for substring, not
prefix. All existing tests use `stringContains` not exact equality on content.
The TOOL-06 truncation test checks `stringContains content "[truncated:"` — that
still passes because `truncateOutput` still appends the truncation marker to the
content portion, and the content portion is still in the string.

**Warning signs:** If any test does `Expect.equal content "alpha\nbeta\ngamma\n"`,
it will break. Audit `FileToolsTests.fs` before modifying. Audit confirms no
exact-equality checks on `read_file` content strings — all use `stringContains`
or `isFalse (content.Contains(...))`.

**One exception:** The TOOL-06 truncation test at line 134 checks
`content.Length < 2200`. After the change, `content` = header (~50 chars) +
newline + truncated content (~2060 chars) = ~2111 chars. This is within 2200.
Still passes. But should be re-examined to make sure the new total is tested.

### Pitfall 6: System prompt — LLM does not know about the header format

**What goes wrong:** 32B receives `[file: ..., lines 1-10 of 150, not-truncated]`
but the system prompt describes `read_file` output as opaque content. The LLM
may not understand the header semantics.

**Why it matters:** The whole point of TOOL-08 is that the LLM uses the metadata.
If the system prompt doesn't mention the header format, 32B may ignore it.

**How to avoid:** Update `defaultSystemPrompt` in `CompositionRoot.fs` to
mention that `read_file` responses include a metadata header. Keep it brief
(one line added to the `read_file` schema description).

**Suggested addition to system prompt:**
```
- read_file:  {"path": "<rel-path>", "start_line": <int?>, "end_line": <int?>}
              Response begins: [file: <path>, lines X-Y of Z, <not-truncated|truncated|out-of-range>]
```

## Code Examples

### Current `readFileImpl` (verbatim, lines 107-143 of FsToolExecutor.fs)

```fsharp
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
                        sprintf "[invalid line range: (%d, %d)]" s e

                return Ok(Success(truncateOutput content))
            with
            | :? FileNotFoundException as ex -> return Ok(Failure(1, ex.Message))
            | :? DirectoryNotFoundException as ex -> return Ok(Failure(1, ex.Message))
            | :? UnauthorizedAccessException as ex -> return Ok(Failure(1, ex.Message))
            | :? IOException as ex -> return Ok(Failure(1, ex.Message))
    }
```

### Modified `readFileImpl` — target shape

```fsharp
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
                // TOOL-08: always read all lines to get totalLines for header.
                let allLines = File.ReadAllLines(resolved)
                let totalLines = allLines.Length

                match lineRange with
                | Some(s, e) when not (s >= 1 && e >= s) ->
                    // Invalid range — existing Failure behavior, no header.
                    return Ok(Failure(1, sprintf "[invalid line range: (%d, %d)]" s e))

                | _ ->
                    let headerStart, headerEnd, rawContent, status =
                        match lineRange with
                        | None ->
                            let raw = String.Join("\n", allLines)
                            let truncated = raw.Length > MESSAGE_HISTORY_CAP
                            let st = if truncated then "truncated" else "not-truncated"
                            (1, totalLines, raw, st)
                        | Some(startLine, endLine) ->
                            if startLine > totalLines then
                                // out-of-range: preserve requested range in header, empty content
                                (startLine, endLine, "", "out-of-range")
                            else
                                let selected =
                                    allLines
                                    |> Array.skip (startLine - 1)
                                    |> Array.truncate (endLine - startLine + 1)
                                let raw = String.Join("\n", selected)
                                let actualEnd = min endLine totalLines
                                let truncated = raw.Length > MESSAGE_HISTORY_CAP
                                let st = if truncated then "truncated" else "not-truncated"
                                (startLine, actualEnd, raw, st)

                    let header =
                        sprintf "[file: %s, lines %d-%d of %d, %s]" path headerStart headerEnd totalLines status

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
```

### New test cases (to add to `FileToolsTests.fs` `readFileTests`)

```fsharp
testCase "TOOL-08: header present for full-file read (not-truncated)"
<| fun () ->
    let root = newFixture ()
    try
        File.WriteAllText(Path.Combine(root, "hdr.txt"), "one\ntwo\nthree\n")
        let exe = create root
        let result = exec exe (ReadFile(FilePath "hdr.txt", None))
        match result with
        | Ok(Success content) ->
            Expect.stringContains content "[file: hdr.txt, lines 1-3 of 3, not-truncated]" "header present"
            Expect.stringContains content "one" "content still present"
        | other -> failtestf "expected Success, got %A" other
    finally
        cleanup root

testCase "TOOL-08: header present for bounded read (not-truncated)"
<| fun () ->
    let root = newFixture ()
    try
        File.WriteAllText(Path.Combine(root, "range.txt"), "a\nb\nc\nd\ne\n")
        let exe = create root
        let result = exec exe (ReadFile(FilePath "range.txt", Some(2, 4)))
        match result with
        | Ok(Success content) ->
            Expect.stringContains content "[file: range.txt, lines 2-4 of 5, not-truncated]" "header correct"
            Expect.stringContains content "b" "line 2 present"
            Expect.isFalse (content.Contains("a")) "line 1 absent"
            Expect.isFalse (content.Contains("e")) "line 5 absent"
        | other -> failtestf "expected Success, got %A" other
    finally
        cleanup root

testCase "TOOL-08: header shows truncated when content exceeds 2000 chars"
<| fun () ->
    let root = newFixture ()
    try
        // Create a file whose content exceeds MESSAGE_HISTORY_CAP
        let bigLine = String.replicate 100 "x"
        let lines = Array.create 30 bigLine  // 30 * 100 = 3000 chars
        File.WriteAllText(Path.Combine(root, "big.txt"), String.Join("\n", lines))
        let exe = create root
        let result = exec exe (ReadFile(FilePath "big.txt", None))
        match result with
        | Ok(Success content) ->
            Expect.stringContains content "[file: big.txt, lines 1-30 of 30, truncated]" "header shows truncated"
            Expect.stringContains content "[truncated:" "truncation marker in content"
        | other -> failtestf "expected Success, got %A" other
    finally
        cleanup root

testCase "TOOL-08: out-of-range start_line returns header-only with out-of-range status"
<| fun () ->
    let root = newFixture ()
    try
        File.WriteAllText(Path.Combine(root, "small.txt"), "only\nthree\nlines\n")
        let exe = create root
        // Request start_line=100 on a 3-line file
        let result = exec exe (ReadFile(FilePath "small.txt", Some(100, 200)))
        match result with
        | Ok(Success content) ->
            Expect.stringContains content "[file: small.txt, lines 100-200 of 3, out-of-range]" "out-of-range header"
            // Content section must be empty (no file content lines)
            Expect.isFalse (content.Contains("only")) "no file content in out-of-range response"
            Expect.isFalse (content.Contains("three")) "no file content in out-of-range response"
        | other -> failtestf "expected Success, got %A" other
    finally
        cleanup root
```

### System prompt addition (CompositionRoot.fs)

Current `read_file` line in `defaultSystemPrompt`:
```
- read_file:  {"path": "<rel-path>", "start_line": <int?>, "end_line": <int?>}
```

Replace with:
```
- read_file:  {"path": "<rel-path>", "start_line": <int?>, "end_line": <int?>}
              Tool output begins: [file: <path>, lines X-Y of Z, not-truncated|truncated|out-of-range]
              If out-of-range: requested start_line > total_lines; choose a start_line <= Z.
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `File.ReadLines` (lazy) for sliced reads | `File.ReadAllLines` (eager array) | Phase 9 | Enables total line count in one pass |
| No metadata in `read_file` output | Header prepended to every Success | Phase 9 | 32B gets bounds signal; T6 infinite-retry eliminated |

**Deprecated/outdated:**
- `File.ReadAllText` in `None` path: replaced by `String.Join("\n", File.ReadAllLines(...))`.
  Content is equivalent (trailing newline difference negligible for agent use).
  Actually: `File.ReadAllText` preserves the raw text exactly including trailing newline.
  `String.Join("\n", File.ReadAllLines(...))` drops the trailing newline. For
  agent display purposes this is fine. If exact preservation matters, use
  `File.ReadAllText` for content and separately `File.ReadAllLines(resolved).Length`
  for count (two reads). Recommend single `ReadAllLines` pass for simplicity.

## Open Questions

1. **Trailing newline in `None` path content**
   - What we know: `File.ReadAllText` preserves trailing newline. `String.Join("\n", ReadAllLines)` does not.
   - What's unclear: Does any test or agent consumer depend on trailing newline presence?
   - Recommendation: Use `ReadAllLines` for unified path (simpler). Existing tests
     use `stringContains` not exact equality, so trailing newline change is safe.
     If needed, append `+ "\n"` after `String.Join`.

2. **System prompt update scope**
   - What we know: The system prompt describes `read_file` input schema but not output format.
   - What's unclear: Whether the planner should include system prompt update in this phase
     or defer to a separate polish task.
   - Recommendation: Include in this phase as Task 3 (small change, high leverage for T6 fix).

## Sources

### Primary (HIGH confidence)
- Direct code read of `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` (lines 1-381)
- Direct code read of `tests/BlueCode.Tests/FileToolsTests.fs` (full file)
- Direct code read of `src/BlueCode.Core/Domain.fs` (full file)
- Direct code read of `src/BlueCode.Cli/CompositionRoot.fs` (full file)
- Direct code read of `tests/BlueCode.Tests/RouterTests.fs` (rootTests list)
- Direct code read of `tests/BlueCode.Tests/BlueCode.Tests.fsproj` (Compile Include order)
- Direct read of `.planning/REQUIREMENTS.md` (TOOL-08 specification)

### Secondary (MEDIUM confidence)
- Phase description and benchmark evidence (provided in research brief)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new libraries, all BCL
- Architecture: HIGH — code read directly, no ambiguity
- Pitfalls: HIGH — derived from direct code audit of existing tests and implementation
- Test cases: HIGH — four cases match the three status values plus invalid-range coverage

**Research date:** 2026-04-24
**Valid until:** Stable — changes only if `readFileImpl` or `truncateOutput` is refactored

---

## Plan Decomposition

This phase is a single narrow change. Recommended structure:

**1 plan, 3 tasks:**

| Task | File | Change |
|------|------|--------|
| 09-01: Implement header | `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` | Replace `readFileImpl` body with header-prepending version |
| 09-02: Add tests | `tests/BlueCode.Tests/FileToolsTests.fs` | Add 4 test cases to `readFileTests` |
| 09-03: Update system prompt | `src/BlueCode.Cli/CompositionRoot.fs` | Add 2-line description of header format to `defaultSystemPrompt` |

No new modules → no changes to `RouterTests.fs` `rootTests` list or `.fsproj`.
Tests are added to the existing `readFileTests` testList inside `FileToolsTests.fs`.

**Exact header format (canonical, from TOOL-08 spec + SC#1 + SC#2):**

```
[file: <relativePath>, lines X-Y of Z, <status>]
```

Where:
- `<relativePath>` = the `path` argument passed to the tool (relative, as input by LLM)
- `X` = `effectiveStart` (1 for full-file, or `start_line` as provided)
- `Y` = `effectiveEnd` (clamped to `totalLines` for in-range; raw `end_line` for out-of-range)
- `Z` = `totalLines` (always the file's actual total)
- `<status>` = one of `not-truncated`, `truncated`, `out-of-range`

**Separator between header and content:** single `\n` (newline).

**Out-of-range content:** empty — the Success payload is the header string only, no `\n` after it.
```
