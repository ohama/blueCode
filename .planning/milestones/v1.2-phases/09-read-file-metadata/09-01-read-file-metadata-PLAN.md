---
phase: 09-read-file-metadata
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/BlueCode.Cli/Adapters/FsToolExecutor.fs
  - tests/BlueCode.Tests/FileToolsTests.fs
  - src/BlueCode.Cli/CompositionRoot.fs
autonomous: true
gap_closure: false

must_haves:
  truths:
    - "Every successful read_file result begins with a one-line metadata header matching the format '[file: <path>, lines X-Y of Z, <status>]'"
    - "A bounded read with a valid in-range window returns header lines X-Y of Z with status 'not-truncated' (where X, Y are the requested range clamped to totalLines for Y) followed by the selected lines"
    - "A read whose raw content exceeds 2000 chars produces a header with status 'truncated' AND the existing [truncated: ...] marker appears in the content section"
    - "An out-of-range read (start_line > totalLines) produces a header with status 'out-of-range' preserving the requested start_line/end_line values in the header AND an empty content section (no file content after the header)"
    - "All 218 pre-existing tests still pass; no Core/Domain type changes; no new NuGet packages"
    - "The system prompt now includes a one- or two-line description of the read_file header format so the LLM knows to read the header"

  artifacts:
    - path: "src/BlueCode.Cli/Adapters/FsToolExecutor.fs"
      provides: "readFileImpl with metadata header prepended to every Success payload"
      contains: "[file: "
      min_lines: 140
    - path: "tests/BlueCode.Tests/FileToolsTests.fs"
      provides: "Four new testCases inside readFileTests covering header format for normal/bounded/truncated/out-of-range reads"
      contains: "TOOL-08"
    - path: "src/BlueCode.Cli/CompositionRoot.fs"
      provides: "defaultSystemPrompt updated with read_file header format description"
      contains: "lines X-Y of Z"

  key_links:
    - from: "readFileImpl Success branch"
      to: "header string"
      via: "sprintf '[file: %s, lines %d-%d of %d, %s]' path start end_ totalLines status"
      pattern: "\\[file: .*, lines .* of .*, (not-truncated|truncated|out-of-range)\\]"
    - from: "readFileImpl"
      to: "totalLines count"
      via: "File.ReadAllLines(resolved).Length"
      pattern: "File\\.ReadAllLines"
    - from: "readFileImpl truncation detection"
      to: "status field"
      via: "comparing raw content length to MESSAGE_HISTORY_CAP (2000) BEFORE calling truncateOutput"
      pattern: "> MESSAGE_HISTORY_CAP"
    - from: "defaultSystemPrompt"
      to: "LLM read_file understanding"
      via: "one- or two-line header format description appended to the read_file input schema description"
      pattern: "lines X-Y of Z"
---

<objective>
Prepend a one-line metadata header to every `read_file` tool `Success` payload so the agent (particularly 32B) knows the file's total line count, the range it actually received, and whether the 2000-char truncation cap fired. Eliminates the T6 benchmark infinite-retry loop where 32B repeatedly requests `start_line=2001, 4001, 6001` on a 150-line file without ever realising it is out of bounds.

Purpose: Close TOOL-08 (the sole requirement in Phase 9). Gives the LLM an unambiguous bounds signal without changing any Core types, schemas, NuGet packages, or the tool routing. Fully in `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` plus a minor system-prompt edit.

Output:
- `readFileImpl` rewritten (still inside FsToolExecutor.fs, same signature) to compute `totalLines`, compute the effective range, detect truncation pre-`truncateOutput`, and prepend `[file: <relPath>, lines X-Y of Z, <status>]\n` to the content.
- Four new testCases inside the existing `readFileTests` testList in FileToolsTests.fs.
- Two new lines under `read_file` in `defaultSystemPrompt` describing the header format.
- All 218 v1.1 tests + any Phase 8 tests + the 4 new tests pass.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/REQUIREMENTS.md
@.planning/phases/09-read-file-metadata/09-RESEARCH.md

# Source to modify
@src/BlueCode.Cli/Adapters/FsToolExecutor.fs
@src/BlueCode.Cli/CompositionRoot.fs
@tests/BlueCode.Tests/FileToolsTests.fs

# Test registration reference (only if creating new test module — NOT needed here,
# since we extend the existing readFileTests testList)
# @tests/BlueCode.Tests/RouterTests.fs
# @tests/BlueCode.Tests/BlueCode.Tests.fsproj

# Conventions (load-bearing)
@CLAUDE.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Implement metadata header in readFileImpl</name>
  <files>src/BlueCode.Cli/Adapters/FsToolExecutor.fs</files>
  <action>
    Modify `readFileImpl` (currently lines 107-143 in `src/BlueCode.Cli/Adapters/FsToolExecutor.fs`) to prepend a one-line metadata header to every `Success` payload. Target shape is documented in RESEARCH.md §"Modified readFileImpl — target shape" (lines 333-395). Re-read it verbatim before writing code.

    Required behavior:

    1. **Unify on `File.ReadAllLines`.** Replace both the `None` branch (currently `File.ReadAllText`) and the `Some(s, e)` branch (currently `File.ReadLines`) with a single eager read:
       ```fsharp
       let allLines = File.ReadAllLines(resolved)
       let totalLines = allLines.Length
       ```
       This happens once, after `validatePath` succeeds and inside the try block. Use `allLines` for both the full-file content (`String.Join("\n", allLines)`) and the sliced content (`Array.skip (startLine-1) |> Array.truncate (endLine - startLine + 1)`).

    2. **Compute effective range and status.** Three branches:
       - `None`: `headerStart=1`, `headerEnd=totalLines`, content = `String.Join("\n", allLines)`, status = `"truncated"` if `content.Length > MESSAGE_HISTORY_CAP` else `"not-truncated"`.
       - `Some(startLine, endLine)` where `startLine >= 1 && endLine >= startLine`:
         - If `startLine > totalLines`: this is **out-of-range**. `headerStart = startLine` (the RAW requested value), `headerEnd = endLine` (the RAW requested value, NOT clamped), content = `""`, status = `"out-of-range"`.
         - Otherwise: `headerStart = startLine`, `headerEnd = min endLine totalLines` (clamped so the header doesn't claim to return lines past EOF), content = `String.Join("\n", Array.skip (startLine-1) allLines |> Array.truncate (endLine - startLine + 1))`, status = `"truncated"` if content.Length > MESSAGE_HISTORY_CAP else `"not-truncated"`.
       - `Some(s, e)` where the guard fails (e < s or s < 1): **keep the existing `Failure` return**. Do NOT prepend a header. Current code returns `Ok(Success(truncateOutput "[invalid line range ...]"))` — change this to `Ok(Failure(1, sprintf "[invalid line range: (%d, %d)]" s e))` so `Failure` is used consistently for invalid input (RESEARCH.md Pitfall 1).

    3. **Header format.** Exact sprintf (matches SC#1, SC#2, SC#3 and the four test cases):
       ```fsharp
       let header = sprintf "[file: %s, lines %d-%d of %d, %s]" path headerStart headerEnd totalLines status
       ```
       Use the input `path` parameter (relative, as passed by the LLM), NOT `resolved` (absolute). This is load-bearing — CLAUDE.md "Don't reintroduce absolute filesystem paths" and RESEARCH.md Pattern 4.

    4. **Payload assembly.** Header is NEVER truncated. Only the content portion is truncated:
       ```fsharp
       let payload =
           if status = "out-of-range" then
               header                                // no trailing newline, no content
           else
               header + "\n" + truncateOutput rawContent
       return Ok(Success payload)
       ```

    5. **Status MUST be `"not-truncated"`, `"truncated"`, or `"out-of-range"`** — string-exact. Test cases assert these literal substrings.

    6. **Exception handling unchanged.** Keep the existing catch clauses for `FileNotFoundException`, `DirectoryNotFoundException`, `UnauthorizedAccessException`, `IOException` returning `Failure(1, ex.Message)`. No header on `Failure` paths.

    Avoid:
    - **Do NOT** use `resolved` in the header (absolute path leak; breaks CLAUDE.md invariant).
    - **Do NOT** apply `truncateOutput` to `header + "\n" + content` together (header must always be intact).
    - **Do NOT** add a new `ToolResult` case — SC#4 prohibits Core type changes. The header is pure string content inside the existing `Success` case (`src/BlueCode.Core/` must stay untouched).
    - **Do NOT** use `async {}` in Core (task {} only). This file is in Cli, not Core, but the project-wide convention still prefers `task {}`.
    - **Do NOT** clamp `headerEnd` to `totalLines` on the out-of-range branch — SC#2 requires the requested range be preserved verbatim (`lines 2001-2100 of 150`).

    After editing, run `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` to confirm the file still compiles before moving to Task 2.

    Commit (stage only this file; NEVER `git add .` or `git add -A` per CLAUDE.md):
    ```
    git add src/BlueCode.Cli/Adapters/FsToolExecutor.fs
    git commit -m "feat(09-01): prepend bounds/truncation header to read_file"
    ```
  </action>
  <verify>
    `dotnet build` succeeds with no warnings on `FsToolExecutor.fs`.
    `grep -c '\[file: ' src/BlueCode.Cli/Adapters/FsToolExecutor.fs` returns `>= 1`.
    `grep 'File.ReadAllLines' src/BlueCode.Cli/Adapters/FsToolExecutor.fs` returns a match (confirms unification).
    `grep 'out-of-range' src/BlueCode.Cli/Adapters/FsToolExecutor.fs` returns a match (confirms the status branch exists).
    `grep -n 'sprintf "\[file:' src/BlueCode.Cli/Adapters/FsToolExecutor.fs` returns a single line inside `readFileImpl`.
    Run `dotnet test tests/BlueCode.Tests/BlueCode.Tests.fsproj --filter "FullyQualifiedName~ReadFile"` — existing readFile tests still pass (they use `stringContains`, not exact equality, so header prefix is fine per RESEARCH.md Pitfall 5).
  </verify>
  <done>
    `readFileImpl` returns `Ok(Success payload)` where `payload` starts with `[file: <relPath>, lines X-Y of Z, <status>]\n` for normal reads and `payload = "[file: <relPath>, lines X-Y of Z, out-of-range]"` (header only, no trailing newline) for out-of-range reads. Invalid-range inputs (`e < s` or `s < 1`) return `Failure` (no header). Build is green, all existing readFile tests still pass, no Core type changes.
  </done>
</task>

<task type="auto">
  <name>Task 2: Add four metadata-header testCases to readFileTests</name>
  <files>tests/BlueCode.Tests/FileToolsTests.fs</files>
  <action>
    Append four new `testCase` entries to the `readFileTests` testList in `tests/BlueCode.Tests/FileToolsTests.fs`. The testList currently ends at line 138 (closing `]` after the TOOL-06 truncation test). Add the four cases BEFORE that closing bracket, using the same `newFixture ()` / `cleanup root` / `exec` pattern already established in the file (see lines 40-55 for the pattern).

    Do NOT create a new test module. Do NOT touch `RouterTests.fs` `rootTests` list or `BlueCode.Tests.fsproj` — the new tests live inside the already-registered `readFileTests` testList.

    The four testCases (copy verbatim from RESEARCH.md §"New test cases" lines 400-468, adapted as below):

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
                Expect.stringContains content "[file: hdr.txt, lines 1-3 of 3, not-truncated]" "header present for full-file read"
                Expect.stringContains content "one" "content still present"
                Expect.stringContains content "three" "last line still present"
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
                Expect.stringContains content "[file: range.txt, lines 2-4 of 5, not-truncated]" "header correct for bounded read"
                Expect.stringContains content "b" "line 2 present"
                Expect.stringContains content "c" "line 3 present"
                Expect.stringContains content "d" "line 4 present"
                Expect.isFalse (content.Contains("a")) "line 1 absent"
                Expect.isFalse (content.Contains("e")) "line 5 absent"
            | other -> failtestf "expected Success, got %A" other
        finally
            cleanup root

    testCase "TOOL-08: header shows truncated when content exceeds 2000 chars"
    <| fun () ->
        let root = newFixture ()
        try
            // 30 lines of 100 chars each = ~3029 chars raw (> 2000 cap).
            let bigLine = String.replicate 100 "x"
            let lines = Array.create 30 bigLine
            File.WriteAllText(Path.Combine(root, "big.txt"), String.Join("\n", lines))
            let exe = create root
            let result = exec exe (ReadFile(FilePath "big.txt", None))
            match result with
            | Ok(Success content) ->
                Expect.stringContains content "[file: big.txt, lines 1-30 of 30, truncated]" "header shows truncated status"
                Expect.stringContains content "[truncated:" "existing TOOL-06 truncation marker still in content"
            | other -> failtestf "expected Success, got %A" other
        finally
            cleanup root

    testCase "TOOL-08: out-of-range start_line returns header-only with out-of-range status"
    <| fun () ->
        let root = newFixture ()
        try
            File.WriteAllText(Path.Combine(root, "small.txt"), "only\nthree\nlines\n")
            let exe = create root
            // Request start_line=100 on a 3-line file — out-of-range
            let result = exec exe (ReadFile(FilePath "small.txt", Some(100, 200)))
            match result with
            | Ok(Success content) ->
                Expect.stringContains content "[file: small.txt, lines 100-200 of 3, out-of-range]" "out-of-range header preserves requested range"
                // Content section MUST be empty — no file lines appear after the header.
                Expect.isFalse (content.Contains("only")) "no file content in out-of-range response"
                Expect.isFalse (content.Contains("three")) "no file content in out-of-range response"
                Expect.isFalse (content.Contains("lines")) "no file content in out-of-range response"
            | other -> failtestf "expected Success, got %A" other
        finally
            cleanup root
    ```

    After inserting, verify the closing `]` of `readFileTests` is still in place and indentation matches the surrounding cases (see lines 40-138 for the existing indentation level).

    Run the full test suite:
    ```
    dotnet test tests/BlueCode.Tests/BlueCode.Tests.fsproj
    ```

    Expected: all 218 v1.1 tests still pass + 4 new tests pass = 222 tests green (plus any Phase 8 additions if Phase 8 executed first — Phase 8 is independent, so the actual test count may vary; what matters is ZERO regressions and the 4 new TOOL-08 tests pass).

    Commit:
    ```
    git add tests/BlueCode.Tests/FileToolsTests.fs
    git commit -m "test(09-01): add read_file metadata header tests"
    ```
  </action>
  <verify>
    `grep -c "TOOL-08:" tests/BlueCode.Tests/FileToolsTests.fs` returns `4` (four new test cases with the TOOL-08 label).
    `dotnet test tests/BlueCode.Tests/BlueCode.Tests.fsproj --filter "FullyQualifiedName~ReadFile"` shows 4 new passing tests matching `TOOL-08:*` names, AND all pre-existing readFile tests still pass.
    `dotnet test tests/BlueCode.Tests/BlueCode.Tests.fsproj` as a full run: every prior test (218+ baseline) is green — zero regressions.
  </verify>
  <done>
    Four TOOL-08 tests live inside `readFileTests` testList, each uses `stringContains` to assert the exact header format from SC#1/SC#2/SC#3, and all pass together with every pre-existing test. No new file created. `rootTests` list unchanged (not needed — extended existing testList).
  </done>
</task>

<task type="auto">
  <name>Task 3: Update system prompt to document read_file header format</name>
  <files>src/BlueCode.Cli/CompositionRoot.fs</files>
  <action>
    Edit `defaultSystemPrompt` in `src/BlueCode.Cli/CompositionRoot.fs` (currently lines 50-66) so the LLM — especially 32B — knows to read the metadata header. Keep the change MINIMAL (two lines added, nothing removed, total prompt length growth < ~180 chars — PERF-01 is a v1.3+ deferred optimization but we should avoid bloating the prompt now).

    Current `read_file` line (line 57):
    ```
    - read_file:  {"path": "<rel-path>", "start_line": <int?>, "end_line": <int?>}
    ```

    Replace with these THREE lines (the original plus two new, indented to align with the JSON schema column):
    ```
    - read_file:  {"path": "<rel-path>", "start_line": <int?>, "end_line": <int?>}
                  Tool output begins: [file: <path>, lines X-Y of Z, not-truncated|truncated|out-of-range]
                  If out-of-range: requested start_line > total_lines (Z); choose a start_line <= Z.
    ```

    Do NOT touch any other tool's schema line. Do NOT touch the rules block. The header of the prompt ("Every response MUST be strict JSON ...") stays verbatim.

    **Test-impact check:** `tests/BlueCode.Tests/CompositionRootTests.fs` asserts (around line 41) that the prompt contains the literal strings `"read_file"`, `"write_file"`, `"list_dir"`, `"run_shell"`, `"final"`. The added two lines still contain `read_file` (in the existing first line) so the assertion remains green. No change needed to `CompositionRootTests.fs`. Confirm by re-running the test after editing.

    If any OTHER test pattern-matches the prompt on exact prefix/length (highly unlikely — prior search showed only substring checks), update minimally to keep the spirit of the assertion. Do NOT weaken an assertion just to make it pass; prefer restoring the string the assertion expected.

    Commit:
    ```
    git add src/BlueCode.Cli/CompositionRoot.fs
    git commit -m "feat(09-01): document read_file header format in system prompt"
    ```
  </action>
  <verify>
    `grep -c "lines X-Y of Z" src/BlueCode.Cli/CompositionRoot.fs` returns `1` (single header-format mention).
    `grep -c "out-of-range" src/BlueCode.Cli/CompositionRoot.fs` returns `1` (status hint).
    `dotnet test tests/BlueCode.Tests/BlueCode.Tests.fsproj --filter "FullyQualifiedName~CompositionRoot"` passes — the "bootstrap SystemPrompt mentions all 5 actions" test still finds `read_file`, `write_file`, `list_dir`, `run_shell`, `final`.
    `dotnet test tests/BlueCode.Tests/BlueCode.Tests.fsproj` (full run) is green across all tests.
  </verify>
  <done>
    `defaultSystemPrompt` has two new lines under the `read_file` schema entry describing the metadata header format and out-of-range semantics. No other prompt content changed. All tests (including CompositionRootTests) still pass.
  </done>
</task>

</tasks>

<verification>
**Overall phase verification (run after all 3 tasks commit):**

1. Build and full test run:
   ```bash
   dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj
   dotnet test tests/BlueCode.Tests/BlueCode.Tests.fsproj
   ```
   Expected: build succeeds; all tests green (218 v1.1 baseline + any Phase 8 tests + 4 new TOOL-08 tests).

2. Core-purity guard (CLAUDE.md invariant):
   ```bash
   grep -rn "llm-system\|/Users/" src/BlueCode.Core/
   ```
   Expected: zero matches (no absolute paths introduced).

3. Async-in-Core guard:
   ```bash
   bash scripts/check-no-async.sh
   ```
   Expected: zero `async {}` literals in Core.

4. Live behavior check (manual or scripted):
   Create a sample file, invoke `readFileImpl` via the executor, confirm the `Success` string begins with `[file: <path>, lines X-Y of Z, <status>]`. The 4 unit tests cover this behaviorally — no live-run checkpoint needed.

5. Backward compatibility:
   ```bash
   dotnet test tests/BlueCode.Tests/BlueCode.Tests.fsproj --filter "FullyQualifiedName~ReadFile"
   ```
   Expected: all pre-existing ReadFile tests (6 in `readFileTests` testList) still pass — proves the header addition is backward-compatible per SC#4.
</verification>

<success_criteria>
All four ROADMAP Phase 9 success criteria measurably satisfied:

- **SC#1** — `blueCode "read the first 10 lines of src/BlueCode.Core/Domain.fs"` produces tool output starting with `[file: src/BlueCode.Core/Domain.fs, lines 1-10 of <N>, not-truncated]`. Provable via: Task 2's "header present for full-file read" testCase + "header present for bounded read" testCase + `grep '\[file: ' src/BlueCode.Cli/Adapters/FsToolExecutor.fs` returning the sprintf line.
- **SC#2** — `start_line=2001` on a 150-line file produces `[file: <path>, lines 2001-2100 of 150, out-of-range]` with empty content. Provable via: Task 2's "out-of-range start_line returns header-only" testCase.
- **SC#3** — Normal `read_file` with no `start_line` on a file under 2000 chars returns `[file: <path>, lines 1-N of N, not-truncated]`. Provable via: Task 2's "header present for full-file read (not-truncated)" testCase.
- **SC#4** — All 218 v1.1 tests still pass; no Core type changes. Provable via: full `dotnet test` green; `git diff src/BlueCode.Core/` shows ZERO changes.

Plus coverage of the RESEARCH recommendation:
- System prompt documents the header format (Task 3) so 32B can act on `out-of-range` status.
</success_criteria>

<output>
After all three tasks complete and all tests pass, create `.planning/phases/09-read-file-metadata/09-01-read-file-metadata-SUMMARY.md` following the template at `.claude/get-shit-done/templates/summary.md`. The SUMMARY should capture:
- Tasks completed and commit SHAs (3 task commits: feat impl, test, feat prompt)
- Tests added: 4 TOOL-08 testCases in readFileTests
- Test delta: baseline → baseline+4 (+4)
- Files modified: FsToolExecutor.fs, FileToolsTests.fs, CompositionRoot.fs (3 files)
- Frontmatter fields: `affects: [read_file tool, system prompt]`, `subsystem: tools`, `tech-stack.added: []` (no new packages), `requires: []` (independent of Phase 8), `patterns.added: [metadata-header-prepend]`
- Any gotchas encountered (especially around the `Array.skip` vs `Seq.skip` shift and whether the `None` branch's trailing newline change affected any test — RESEARCH.md Pitfall 5)
</output>
