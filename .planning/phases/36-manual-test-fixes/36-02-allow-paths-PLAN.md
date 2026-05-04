---
phase: 36-manual-test-fixes
plan: 02
type: execute
wave: 2
depends_on: ["36-01"]
files_modified:
  - src/BlueCode.Cli/CliArgs.fs
  - src/BlueCode.Cli/CompositionRoot.fs
  - src/BlueCode.Cli/Adapters/FsToolExecutor.fs
  - src/BlueCode.Cli/Program.fs
  - tests/BlueCode.Tests/CliArgsTests.fs
  - tests/BlueCode.Tests/FileToolsTests.fs
autonomous: true

must_haves:
  truths:
    - "New CLI flag `--allow-paths <p1>[,<p2>,...]` parses via Argu and is captured into CliOptions.AllowPaths as a string list (empty when flag absent)"
    - "FsToolExecutor.create now takes (projectRoot: string) (extraAllowedPaths: string list) and canonicalizes both with Path.GetFullPath at construction"
    - "validatePath (renamed/extended to validatePathWithExtras) accepts a path if it resolves either inside projectRoot OR inside any canonicalized extra-root, using the trailing-separator prefix-attack guard for both"
    - "Without --allow-paths flag (default), all file-tool path-blocking behaviour is byte-identical to pre-Phase-36 (existing FileToolsTests pass unchanged)"
    - "With `--allow-paths /tmp/bc-test`, file tools (read_file, write_file, edit_file, list_dir, glob_search, grep_search) accept paths that resolve under /tmp/bc-test"
    - "Path canonicalization defeats `..` traversal: `--allow-paths /tmp/bc-test` does NOT permit `/tmp/bc-test/../etc/passwd` (resolves to /etc/passwd, neither in projectRoot nor in /tmp/bc-test)"
    - "Trailing-separator prefix guard: `--allow-paths /tmp/bc-test` does NOT permit `/tmp/bc-testing/evil` (sibling-prefix attack)"
    - "FsToolExecutor.create accepts an extraAllowedPaths: string list parameter; FileToolsTests prove allow-listed paths pass and non-allow-listed paths are blocked with PathEscapeBlocked"
    - "Zero changes to src/BlueCode.Core/** (`git diff master -- src/BlueCode.Core/` empty for this plan's commits)"
  artifacts:
    - path: "src/BlueCode.Cli/CliArgs.fs"
      provides: "AllowPaths case in CliArgs DU + IArgParserTemplate.Usage entry"
      contains: "AllowPaths of paths: string"
    - path: "src/BlueCode.Cli/CompositionRoot.fs"
      provides: "AllowPaths: string list field on CliOptions + bootstrap thread-through to FsToolExecutor.create"
      contains: "AllowPaths"
    - path: "src/BlueCode.Cli/Adapters/FsToolExecutor.fs"
      provides: "validatePathWithExtras + create signature change to (projectRoot) (extraAllowedPaths)"
      contains: "validatePathWithExtras"
      contains_2: "extraAllowedPaths"
    - path: "src/BlueCode.Cli/Program.fs"
      provides: "Argu --allow-paths parse + comma-split into CliOptions.AllowPaths"
      contains: "AllowPaths"
    - path: "tests/BlueCode.Tests/CliArgsTests.fs"
      provides: "≥2 tests for --allow-paths Argu parsing (single path, comma-separated)"
      contains: "AllowPaths"
    - path: "tests/BlueCode.Tests/FileToolsTests.fs"
      provides: "≥4 tests for FsToolExecutor allow-paths boundary (allowed-pass, prefix-sibling-block, traversal-block, empty-list-preserves-existing)"
      contains: "allow-paths"
  key_links:
    - from: "src/BlueCode.Cli/Program.fs (--allow-paths arg parsing)"
      to: "src/BlueCode.Cli/CompositionRoot.fs (CliOptions.AllowPaths)"
      via: "results.TryGetResult CliArgs.AllowPaths -> Option.map (split on ',') -> AllowPaths field"
      pattern: "AllowPaths"
    - from: "src/BlueCode.Cli/CompositionRoot.fs (bootstrap)"
      to: "src/BlueCode.Cli/Adapters/FsToolExecutor.fs (create)"
      via: "FsToolExecutor.create projectRoot opts.AllowPaths"
      pattern: "FsToolExecutor\\.create projectRoot opts\\.AllowPaths"
    - from: "src/BlueCode.Cli/Adapters/FsToolExecutor.fs (each *Impl function)"
      to: "validatePathWithExtras"
      via: "match validatePathWithExtras projectRoot extraAllowedPaths path with"
      pattern: "validatePathWithExtras"
---

<objective>
Phase 36 — Plan 02: Add `--allow-paths <p1>[,<p2>,...]` CLI flag that lets the user
explicitly extend FsToolExecutor's path allowlist beyond projectRoot. Resolves manual test
findings T-16/17/18/19/100/101 (`/tmp/*` paths blocked by FsToolExecutor's hard-coded
projectRoot-only validation). bench/CI invokes with no flag → empty list → security
invariant preserved.

Purpose: Largest track in Phase 36. Must run AFTER Plan 36-01 because both edit
`src/BlueCode.Cli/Adapters/FsToolExecutor.fs` (different regions — 36-01 touches
`globSearchImpl`, this plan touches `validatePath` and `create`). Sequencing avoids
merge conflicts and keeps each plan auditable in isolation.

The fix wires through 4 source files + 2 test files:

  1. `CliArgs.fs` — add `AllowPaths of paths: string` DU case.
  2. `CompositionRoot.fs` — add `AllowPaths: string list` to `CliOptions`,
     update `defaultCliOptions`, thread through `bootstrap`.
  3. `FsToolExecutor.fs` — rename `validatePath` to `validatePathWithExtras` (or add new
     and keep old); extend `create` signature to take `extraAllowedPaths: string list`.
  4. `Program.fs` — parse `--allow-paths`, comma-split, populate `CliOptions.AllowPaths`.
  5. `CliArgsTests.fs` — ≥2 tests for Argu parsing.
  6. `FileToolsTests.fs` — ≥4 tests for allow-paths boundary semantics.

Output:
- New `--allow-paths` Argu flag, comma-separated path list, default empty.
- 4 file source diffs (Cli only — Core untouched).
- ~6 new unit tests.
- manual-test-guide.md updates for T-16/17/18/19 are in Plan 36-03 (combined with the
  prompt-suffix UX improvements + T-100/101 doc fix). This plan touches code only.
- Zero changes to: `src/BlueCode.Core/**`, `BlueCode.Cli.fsproj`, `BlueCode.Tests.fsproj`,
  `RouterTests.fs` (CliArgsTests.tests and FileToolsTests.fileToolsTests already in rootTests).
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/STATE.md
@.planning/ROADMAP.md
@.planning/phases/36-manual-test-fixes/36-RESEARCH.md
@.planning/phases/36-manual-test-fixes/36-01-glob-recursive-PLAN.md
@CLAUDE.md
@src/BlueCode.Cli/CliArgs.fs
@src/BlueCode.Cli/CompositionRoot.fs
@src/BlueCode.Cli/Adapters/FsToolExecutor.fs
@src/BlueCode.Cli/Program.fs
@tests/BlueCode.Tests/CliArgsTests.fs
@tests/BlueCode.Tests/FileToolsTests.fs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add AllowPaths to CliArgs DU + CliOptions + Program.fs parsing</name>
  <files>
src/BlueCode.Cli/CliArgs.fs
src/BlueCode.Cli/CompositionRoot.fs
src/BlueCode.Cli/Program.fs
  </files>
  <action>
**Step 1.1 — `src/BlueCode.Cli/CliArgs.fs`:**

Add a new case to the `CliArgs` DU (after `Plan` line ~23). The DU member shape: a string
holding a comma-separated path list. Argu will collect everything after `--allow-paths`
until the next flag or positional.

```fsharp
    | AllowPaths of paths: string                      // NEW (Phase 36-02): comma-separated extra-allowed path prefixes
```

Add a matching `IArgParserTemplate.Usage` case (just before the existing `Plan ->` case):

```fsharp
            | AllowPaths _ -> "Comma-separated extra paths the agent may read/write (canonicalized; trailing-separator prefix-attack guarded). Default: empty (project root only)."
```

DO NOT change the order of existing cases. DO NOT add `[<EqualsAssignment>]` — Argu's
default `--flag value` form is correct.

**Step 1.2 — `src/BlueCode.Cli/CompositionRoot.fs`:**

Add `AllowPaths: string list` to the `CliOptions` record. Place it AFTER `PlanMode: bool`
(record field order matters for any callers using positional construction; preserve
all existing fields):

```fsharp
type CliOptions =
    { ForcedModel: BlueCode.Core.Domain.Model option
      Verbose: bool
      Trace: bool
      ResumeSessionId: BlueCode.Core.Domain.SessionId option
      NewSession: bool
      WithDual35b: bool
      PlanMode: bool
      AllowPaths: string list }                        // NEW (Phase 36-02)
```

Update `defaultCliOptions` to include `AllowPaths = []`:

```fsharp
let defaultCliOptions: CliOptions =
    { ForcedModel = None
      Verbose = false
      Trace = false
      ResumeSessionId = None
      NewSession = false
      WithDual35b = false
      PlanMode = false
      AllowPaths = [] }                                // NEW (Phase 36-02)
```

Update `bootstrap` to pass `opts.AllowPaths` through to `FsToolExecutor.create`. Currently
line 114:

```fsharp
      ToolExecutor = Adapters.FsToolExecutor.create projectRoot
```

Change to:

```fsharp
      ToolExecutor = Adapters.FsToolExecutor.create projectRoot opts.AllowPaths
```

NOTE: This call-site change will be a compile error until Task 2 updates the
`FsToolExecutor.create` signature. Stage the file but expect the build to fail until both
edits land. Do NOT commit until the build passes.

**Step 1.3 — `src/BlueCode.Cli/Program.fs`:**

After existing flag parsing (around line 42 where `let isPlanMode = ...` lives), add:

```fsharp
        // NEW (Phase 36-02): --allow-paths comma-separated path list. Empty list when absent.
        let allowPaths : string list =
            results.TryGetResult CliArgs.AllowPaths
            |> Option.map (fun s ->
                s.Split(',')
                |> Array.map (fun p -> p.Trim())
                |> Array.filter (fun p -> p.Length > 0)
                |> Array.toList)
            |> Option.defaultValue []
```

Then in the `let opts = { ... }` block (currently lines ~107-114), append `AllowPaths = allowPaths`:

```fsharp
        let opts =
            { ForcedModel = forcedModel
              Verbose = isVerbose
              Trace = isTrace
              ResumeSessionId = resumeId |> Option.map SessionId
              NewSession = isNewSession
              WithDual35b = withDual
              PlanMode = isPlanMode
              AllowPaths = allowPaths }                // NEW (Phase 36-02)
```

DO NOT add validation of path existence — `Path.GetFullPath` will canonicalize whatever
strings are provided. Non-existent paths are not an error: they just won't match anything,
and the existing `validatePath` Error path covers them naturally.

**Verify all 3 files staged but DO NOT BUILD YET** (Task 2 changes the FsToolExecutor.create
signature; build will succeed only after both tasks land). Move to Task 2.
  </action>
  <verify>
1. `grep -n "AllowPaths" src/BlueCode.Cli/CliArgs.fs` shows ≥2 matches (DU case + Usage).
2. `grep -n "AllowPaths" src/BlueCode.Cli/CompositionRoot.fs` shows ≥3 matches (CliOptions field + defaultCliOptions + bootstrap pass-through).
3. `grep -n "AllowPaths\|allowPaths" src/BlueCode.Cli/Program.fs` shows ≥3 matches (parse, opts.AllowPaths field).
4. NO commit yet — build will fail until Task 2 lands.
  </verify>
  <done>
- [x] `CliArgs.fs` has `AllowPaths` case + Usage entry.
- [x] `CompositionRoot.fs` has `AllowPaths: string list` field, default `[]`, and `bootstrap` passes `opts.AllowPaths` to `FsToolExecutor.create`.
- [x] `Program.fs` parses `--allow-paths` via `TryGetResult` + comma split + Trim + filter empty.
- [x] No commit yet — files staged for combined commit after Task 2.
  </done>
</task>

<task type="auto">
  <name>Task 2: Extend FsToolExecutor.validatePath and create signature</name>
  <files>src/BlueCode.Cli/Adapters/FsToolExecutor.fs</files>
  <action>
**Step 2.1 — Replace `validatePath` with `validatePathWithExtras`:**

Locate `validatePath` (currently at lines 73-100). Replace the entire function with:

```fsharp
/// Resolve inputPath relative to projectRoot, then require the resolved path to stay
/// inside EITHER projectRoot OR one of the canonicalized extraRoots. Trailing-separator
/// prefix-attack guard applies to both projectRoot and each extraRoot (CLAUDE.md
/// 03-RESEARCH.md Pattern 2 / PITFALLS D-3 / Phase 36-02 Pitfall 6).
///
/// Phase 36-02 (T-16/17/18/19/100/101): extraRoots is the canonicalized form of
/// CliOptions.AllowPaths. With --allow-paths empty, behaviour is byte-identical to the
/// pre-Phase-36 single-root validation. `..` traversal is defeated by Path.GetFullPath
/// canonicalization at check-time AND at startup; cross-root sibling attacks are defeated
/// by the trailing-separator guard.
///
/// Paths starting with "~" are rejected — we do not expand home directories.
let private validatePathWithExtras
    (projectRoot: string)
    (extraRoots: string list)
    (inputPath: string)
    : Result<string, ToolResult> =
    if String.IsNullOrWhiteSpace(inputPath) then
        Error(PathEscapeBlocked(inputPath |> Option.ofObj |> Option.defaultValue ""))
    elif inputPath.StartsWith("~") then
        Error(PathEscapeBlocked inputPath)
    else
        try
            let combined = Path.Combine(projectRoot, inputPath)
            let resolved = Path.GetFullPath(combined)
            let withSep (r: string) =
                if r.EndsWith(string Path.DirectorySeparatorChar) then r
                else r + string Path.DirectorySeparatorChar
            let inRoot (r: string) =
                resolved = r || resolved.StartsWith(withSep r, StringComparison.Ordinal)
            if inRoot projectRoot || List.exists inRoot extraRoots then
                Ok resolved
            else
                Error(PathEscapeBlocked inputPath)
        with _ ->
            // Malformed path (e.g., invalid chars) -> treat as escape attempt.
            Error(PathEscapeBlocked inputPath)
```

**Step 2.2 — Update all call-sites within FsToolExecutor.fs:**

Use grep to find all callers:
```
grep -n "validatePath projectRoot" src/BlueCode.Cli/Adapters/FsToolExecutor.fs
```
Expected lines: 125, 200, 258, 451, 503, 558 (six callers across read/write/edit/list/glob/grep impls).

For EACH caller, replace `validatePath projectRoot path` with
`validatePathWithExtras projectRoot extraAllowedPaths path`.

Each `*Impl` function signature must also be updated to take an additional
`extraAllowedPaths: string list` parameter, threaded through to the validation call. The
`*Impl` signatures currently start with `(projectRoot: string)` as their first parameter;
add `(extraAllowedPaths: string list)` as the SECOND parameter (between projectRoot and
the function-specific parameters):

- `readFileImpl  (projectRoot) (extraAllowedPaths) (path) (lineRange) (ct)`
- `writeFileImpl (projectRoot) (extraAllowedPaths) (path) (content) (ct)`
- `listDirImpl   (projectRoot) (extraAllowedPaths) (path) (depth) (ct)`
- `editFileImpl  (projectRoot) (extraAllowedPaths) (path) (oldStr) (newStr) (ct)`
- `globSearchImpl (projectRoot) (extraAllowedPaths) (pattern) (searchPath) (ct)`
- `grepSearchImpl (projectRoot) (extraAllowedPaths) (pattern) (searchPath) (fileGlob) (ct)`

NOTE: `runShellImpl` does NOT call `validatePath` (it sandboxes via `bash -c` shell guard).
Leave `runShellImpl` signature UNCHANGED.

**Step 2.3 — Update `create` factory:**

Locate `create` at lines 627-642. Replace with:

```fsharp
/// Create an IToolExecutor bound to projectRoot plus a list of extra allowed-path
/// prefixes. All path validation runs against the union {projectRoot} ∪ extraAllowedPaths
/// after Path.GetFullPath canonicalization. Empty extraAllowedPaths preserves pre-Phase-36
/// behaviour exactly.
///
/// Exhaustive match over Tool DU — adding a case to Tool in Domain.fs is a compile error
/// here (Success Criterion 6 proof).
let create (projectRoot: string) (extraAllowedPaths: string list) : IToolExecutor =
    let rootNormalized = Path.GetFullPath(projectRoot)
    let allowedNormalized = extraAllowedPaths |> List.map Path.GetFullPath

    { new IToolExecutor with
        member _.ExecuteAsync (tool: Tool) (ct: CancellationToken) : Task<Result<ToolResult, AgentError>> =
            match tool with
            | ReadFile(FilePath path, lineRange) -> readFileImpl rootNormalized allowedNormalized path lineRange ct
            | WriteFile(FilePath path, content) -> writeFileImpl rootNormalized allowedNormalized path content ct
            | ListDir(FilePath path, depth) -> listDirImpl rootNormalized allowedNormalized path depth ct
            | RunShell(Command cmd, BlueCode.Core.Domain.Timeout _timeoutMs) -> runShellImpl rootNormalized cmd ct
            | EditFile(FilePath path, oldStr, newStr) -> editFileImpl rootNormalized allowedNormalized path oldStr newStr ct
            | GlobSearch(pattern, searchPath) ->
                globSearchImpl rootNormalized allowedNormalized pattern (searchPath |> Option.map (fun (FilePath p) -> p)) ct
            | GrepSearch(pattern, searchPath, fileGlob) ->
                grepSearchImpl rootNormalized allowedNormalized pattern (searchPath |> Option.map (fun (FilePath p) -> p)) fileGlob ct }
```

**Step 2.4 — Build & commit (combined commit for Task 1 + 2):**

Build the entire solution to catch every transitive call site:

```
dotnet build
```

If build succeeds, all signatures are consistent. If build fails, expected fix loci are:
- Existing test files calling `create root` → must call `create root []` (empty list).
  This is unavoidable but cheap: `grep -n "create root" tests/BlueCode.Tests/*.fs`
  finds every occurrence. Replace each with `create root []`. (Done as part of this
  task — the `[]` argument preserves existing semantics exactly.)

After build is green, commit Task 1 + Task 2 together (they are inseparable):

```
git add src/BlueCode.Cli/CliArgs.fs \
        src/BlueCode.Cli/CompositionRoot.fs \
        src/BlueCode.Cli/Program.fs \
        src/BlueCode.Cli/Adapters/FsToolExecutor.fs \
        tests/BlueCode.Tests/FileToolsTests.fs \
        tests/BlueCode.Tests/ToolExpansionTests.fs

git commit -m "feat(36-02): add --allow-paths CLI flag for explicit path allowlist (T-16..T-19/T-100/T-101)

Adds AllowPaths of paths: string to CliArgs DU, threads through CliOptions
to FsToolExecutor.create as extraAllowedPaths: string list. validatePath
becomes validatePathWithExtras and accepts a path that resolves under
projectRoot OR any canonicalized extra-root, with trailing-separator
prefix-attack guard for both. Path.GetFullPath defeats .. traversal at
construction (startup) and at validation (runtime).

Empty AllowPaths (default) preserves pre-Phase-36 behaviour byte-identically;
bench/CI invokes without the flag → security invariant unchanged.

All 6 *Impl functions threaded with new (extraAllowedPaths: string list)
parameter; runShellImpl unchanged (no path validation).

Existing FileToolsTests calls to 'create root' updated to 'create root []'
(no semantic change; empty list).

Core untouched: git diff master -- src/BlueCode.Core/ wc -l = 0."
```

NOTE: this same commit will include the test additions from Task 3 — DO NOT commit until
Task 3 is complete. The commit message above describes the source change only; Task 3
adds test files into the same commit.
  </action>
  <verify>
1. `grep -c "validatePathWithExtras" src/BlueCode.Cli/Adapters/FsToolExecutor.fs` shows ≥7 (1 def + 6 callers).
2. `grep -c "validatePath projectRoot" src/BlueCode.Cli/Adapters/FsToolExecutor.fs` shows `0` (all renamed).
3. `dotnet build 2>&1 | tail -5` shows "Build succeeded" with 0 errors and no NEW warnings (warnings present pre-Phase-36 are accepted).
4. `grep -c "create root \[\]" tests/BlueCode.Tests/*.fs | grep -v ":0"` shows non-zero matches (existing FileToolsTests + ToolExpansionTests calls updated).
5. `git diff master -- src/BlueCode.Core/ | wc -l` outputs `0`.
  </verify>
  <done>
- [x] `validatePathWithExtras` defined with the union-root logic.
- [x] All 6 path-validating `*Impl` functions threaded with `extraAllowedPaths`.
- [x] `runShellImpl` signature unchanged.
- [x] `create` signature is `(projectRoot: string) (extraAllowedPaths: string list)`.
- [x] All existing `create root` test call-sites updated to `create root []` (no semantic change).
- [x] Solution builds clean.
- [x] No commit yet — combined with Task 3's tests in a single feat(36-02) commit.
  </done>
</task>

<task type="auto">
  <name>Task 3: Add unit tests for --allow-paths Argu parsing + FsToolExecutor boundary semantics</name>
  <files>
tests/BlueCode.Tests/CliArgsTests.fs
tests/BlueCode.Tests/FileToolsTests.fs
  </files>
  <action>
**Step 3.1 — `tests/BlueCode.Tests/CliArgsTests.fs`:**

Open the file and locate the existing `tests` testList. Append 2 new test cases at the END
of the `[ ... ]` list (after the `--help` test, before the closing `]`):

```fsharp
          // 12. (Phase 36-02) --allow-paths single path
          testCase "--allow-paths /tmp/x with prompt: TryGetResult AllowPaths = Some \"/tmp/x\""
          <| fun () ->
              let results = parse [| "--allow-paths"; "/tmp/x"; "hi" |]
              Expect.equal (results.TryGetResult AllowPaths) (Some "/tmp/x") "single path captured as raw string"
              Expect.equal (results.TryGetResult Prompt) (Some [ "hi" ]) "prompt still captured"

          // 13. (Phase 36-02) --allow-paths comma-separated multi
          testCase "--allow-paths /tmp/x,/tmp/y: TryGetResult AllowPaths = Some \"/tmp/x,/tmp/y\""
          <| fun () ->
              let results = parse [| "--allow-paths"; "/tmp/x,/tmp/y"; "hi" |]
              Expect.equal (results.TryGetResult AllowPaths) (Some "/tmp/x,/tmp/y") "comma-separated raw string captured"
```

NOTE: Argu just stores the raw string. The comma-splitting + Trim happens in `Program.fs`.
We DO NOT directly test Program.fs here — its behaviour is covered transitively by the
FsToolExecutor boundary tests, which use the canonicalized form directly.

**Step 3.2 — `tests/BlueCode.Tests/FileToolsTests.fs`:**

Locate the `fileToolsTests` aggregator at line ~406:

```fsharp
let fileToolsTests = testList "FsToolExecutor (TOOL-01..05)" [ readFileTests; writeFileTests; listDirTests; ... ]
```

Add a NEW test list `allowPathsTests` ABOVE this aggregator (and BEFORE the `let
fileToolsTests = ...` line), then append it to the aggregator's `[ ... ]` list.

```fsharp
// ── Phase 36-02: --allow-paths boundary tests ────────────────────────────────

let allowPathsTests =
    testList
        "FsToolExecutor.AllowPaths (Phase 36-02)"
        [

          testCase "empty extraAllowedPaths preserves projectRoot-only behaviour"
          <| fun () ->
              let root = newFixture ()
              let other = newFixture ()
              try
                  File.WriteAllText(Path.Combine(other, "outside.txt"), "secret")
                  let exe = create root []   // empty allow list
                  let result = exec exe (ReadFile(FilePath (Path.Combine(other, "outside.txt")), None))
                  match result with
                  | Ok(PathEscapeBlocked _) -> ()
                  | other -> failtestf "expected PathEscapeBlocked with empty allow list, got %A" other
              finally
                  cleanup root
                  cleanup other

          testCase "extraAllowedPath permits read of file inside that path"
          <| fun () ->
              let root = newFixture ()
              let extra = newFixture ()
              try
                  let target = Path.Combine(extra, "allowed.txt")
                  File.WriteAllText(target, "manual test passed")
                  let exe = create root [ extra ]
                  let result = exec exe (ReadFile(FilePath target, None))
                  match result with
                  | Ok(Success body) ->
                      Expect.stringContains body "manual test passed" "content should be readable through extra-allowed root"
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root
                  cleanup extra

          testCase "extraAllowedPath permits write_file inside that path"
          <| fun () ->
              let root = newFixture ()
              let extra = newFixture ()
              try
                  let target = Path.Combine(extra, "wrote.txt")
                  let exe = create root [ extra ]
                  let result = exec exe (WriteFile(FilePath target, "hello"))
                  match result with
                  | Ok(Success _) ->
                      Expect.equal (File.ReadAllText target) "hello" "file written via allow-listed path"
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root
                  cleanup extra

          testCase "trailing-separator guard: '/tmp/bc-test' does NOT permit '/tmp/bc-testing'"
          <| fun () ->
              // Use real /tmp under unique GUID prefixes to avoid collision
              let g = Guid.NewGuid().ToString("N")
              let allowed = Path.Combine(Path.GetTempPath(), "bc-" + g)
              let sibling = Path.Combine(Path.GetTempPath(), "bc-" + g + "-sibling")
              Directory.CreateDirectory(allowed) |> ignore
              Directory.CreateDirectory(sibling) |> ignore
              try
                  let target = Path.Combine(sibling, "evil.txt")
                  File.WriteAllText(target, "should-not-read")
                  let root = newFixture ()
                  let exe = create root [ allowed ]
                  let result = exec exe (ReadFile(FilePath target, None))
                  match result with
                  | Ok(PathEscapeBlocked _) -> ()
                  | other -> failtestf "expected PathEscapeBlocked (sibling-prefix attack), got %A" other
              finally
                  try Directory.Delete(allowed, true) with _ -> ()
                  try Directory.Delete(sibling, true) with _ -> ()

          testCase ".. traversal blocked even with broad allow list"
          <| fun () ->
              let root = newFixture ()
              let extra = newFixture ()
              try
                  // Use a path that is guaranteed-absent regardless of OS so file
                  // existence cannot mask a security bug. PathEscapeBlocked must
                  // fire BEFORE any file open is attempted; absence is irrelevant
                  // to the security decision but the explicit single-arm match
                  // ensures Ok(Failure _) (a legitimate file-op failure) never
                  // masquerades as a security block.
                  let traversal = Path.Combine(extra, "..", "etc", "definitely-not-real-file-12345")
                  let exe = create root [ extra ]
                  let result = exec exe (ReadFile(FilePath traversal, None))
                  match result with
                  | Ok(PathEscapeBlocked _) -> ()
                  | other -> failtestf "expected PathEscapeBlocked for .. traversal, got %A" other
              finally
                  cleanup root
                  cleanup extra

          testCase "non-allow-listed absolute path is blocked"
          <| fun () ->
              let root = newFixture ()
              let extra = newFixture ()
              let elsewhere = newFixture ()
              try
                  File.WriteAllText(Path.Combine(elsewhere, "x.txt"), "")
                  let exe = create root [ extra ]   // 'elsewhere' NOT in list
                  let result = exec exe (ReadFile(FilePath (Path.Combine(elsewhere, "x.txt")), None))
                  match result with
                  | Ok(PathEscapeBlocked _) -> ()
                  | other -> failtestf "expected PathEscapeBlocked, got %A" other
              finally
                  cleanup root
                  cleanup extra
                  cleanup elsewhere
        ]
```

Then update the `fileToolsTests` aggregator to include the new sub-list:

```fsharp
let fileToolsTests = testList "FsToolExecutor (TOOL-01..05)" [ readFileTests; writeFileTests; listDirTests; allowPathsTests ]
```

NO changes to `BlueCode.Tests.fsproj` (FileToolsTests.fs and CliArgsTests.fs already in
`<Compile Include>`).
NO changes to `RouterTests.fs` (FileToolsTests.fileToolsTests and CliArgsTests.tests
already in `rootTests`).

**Step 3.3 — Run tests:**

```
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | tail -15
```

Expected:
- 0 failures.
- Total count = (336 after Plan 36-01) + 8 = 344 (2 CliArgs + 6 FileTools).

**Step 3.4 — Combined commit (Task 1 + 2 + 3 source + tests):**

This is a single feat(36-02) commit because the source changes and tests are inseparable
(test compilation depends on the new `create` signature; existing test compilation depends
on the `create root []` migration).

```
git add src/BlueCode.Cli/CliArgs.fs \
        src/BlueCode.Cli/CompositionRoot.fs \
        src/BlueCode.Cli/Program.fs \
        src/BlueCode.Cli/Adapters/FsToolExecutor.fs \
        tests/BlueCode.Tests/CliArgsTests.fs \
        tests/BlueCode.Tests/FileToolsTests.fs \
        tests/BlueCode.Tests/ToolExpansionTests.fs

git commit -m "feat(36-02): add --allow-paths CLI flag (T-16..T-19/T-100/T-101)

Adds 'AllowPaths of paths: string' to CliArgs DU + 'AllowPaths: string list'
to CliOptions, threads through CompositionRoot.bootstrap to
FsToolExecutor.create as extraAllowedPaths.

validatePath -> validatePathWithExtras: accepts path resolving under
projectRoot OR any canonicalized extra-root; trailing-sep guard prevents
sibling-prefix attacks (\"/tmp/bc-test\" does NOT match \"/tmp/bc-testing\").
Path.GetFullPath at construction AND at validation defeats .. traversal.

All 6 path-validating *Impl functions take extraAllowedPaths as new 2nd
param; runShellImpl unchanged. Existing 'create root' test sites updated
to 'create root []' (semantic-preserving).

Empty AllowPaths default = byte-identical pre-Phase-36 security invariant;
bench/CI invokes without flag => unchanged.

Tests: +2 CliArgs (parse), +6 FileTools (allow-paths boundary). Total
test count delta from this plan: +8.

Core untouched: git diff master -- src/BlueCode.Core/ wc -l = 0."
```

NOTE: ToolExpansionTests.fs is included in the staging because its `create root` calls (if
any exist) need updating to `create root []`. If no such calls exist, do not stage it.
Verify before staging:
```
grep -n "create root" tests/BlueCode.Tests/ToolExpansionTests.fs
```
If matches found and they were updated, stage the file. If no matches, omit.
  </action>
  <verify>
1. `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | tail -10` shows 0 failures, total count = pre-plan + 8.
2. `grep -c "Phase 36-02" tests/BlueCode.Tests/*.fs` shows ≥8 (test labels mentioning Phase 36-02 + section comment + 6 testCases).
3. `git log --oneline -1` shows `feat(36-02): add --allow-paths CLI flag (T-16..T-19/T-100/T-101)`.
4. `git diff master -- src/BlueCode.Core/ | wc -l` outputs `0`.
5. `git diff master --stat` shows changes only in `src/BlueCode.Cli/` and `tests/BlueCode.Tests/`.
  </verify>
  <done>
- [x] 2 new CliArgs Argu-parse tests pass.
- [x] 6 new FileTools allow-paths boundary tests pass (empty list, allow read, allow write, sibling-prefix block, traversal block, non-allow block).
- [x] Test suite total = pre-plan (336) + 8 = 344.
- [x] Single combined commit `feat(36-02): add --allow-paths CLI flag (T-16..T-19/T-100/T-101)`.
- [x] Core untouched.
  </done>
</task>

</tasks>

<verification>
After all tasks:

1. `dotnet build` exits 0 with 0 errors.
2. `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0; total = pre-plan + 8.
3. `git diff master -- src/BlueCode.Core/ | wc -l` outputs `0` (Core purity preserved).
4. `bash scripts/check-no-async.sh` exits 0 (no `async {}` introduced — used `task {}` only via existing CE; new code is plain F# functions).
5. Optional manual smoke: build the binary and run T-16 with the new flag:
   ```
   mkdir -p /tmp/bc-test
   dotnet run --project src/BlueCode.Cli -- --allow-paths /tmp/bc-test --verbose "Create a file at /tmp/bc-test/hello.txt with the content 'manual test passed'."
   cat /tmp/bc-test/hello.txt
   ```
   Expected: file created with the requested content. The model SHOULD use `write_file` natively (no `run_shell` fallback needed).

6. Optional negative smoke: confirm absence of flag still blocks:
   ```
   dotnet run --project src/BlueCode.Cli -- --verbose "Read /tmp/bc-test/hello.txt"
   ```
   Expected: PathEscapeBlocked-style error (or model reporting failure).

NOTE: Bench gate (`bash bench/run.sh --gate`) verification is in Plan 36-03 (the wave-3 plan
that closes Phase 36).
</verification>

<success_criteria>
- [ ] `--allow-paths <p1>[,<p2>,...]` Argu flag parses; defaults to empty list when absent.
- [ ] `CliOptions.AllowPaths` is `string list`; `defaultCliOptions.AllowPaths = []`.
- [ ] `FsToolExecutor.create` takes `(projectRoot: string) (extraAllowedPaths: string list)`; `bootstrap` passes `opts.AllowPaths`.
- [ ] `validatePathWithExtras` accepts path resolving under projectRoot OR any canonicalized extra-root.
- [ ] Trailing-separator guard prevents sibling-prefix attack (`/tmp/bc-test` vs `/tmp/bc-testing`).
- [ ] `..` traversal blocked even within allowed roots.
- [ ] Empty `AllowPaths` preserves pre-Phase-36 behaviour byte-identically (existing FileToolsTests pass).
- [ ] T-16/17/18/19 invariant: with `--allow-paths /tmp/bc-test`, file tools work against `/tmp/bc-test/*` paths (verifiable manually or via the boundary tests).
- [ ] 8 new unit tests pass (2 CliArgs + 6 FileTools boundary).
- [ ] `git diff master -- src/BlueCode.Core/` is empty.
- [ ] 1 atomic commit `feat(36-02): add --allow-paths CLI flag (T-16..T-19/T-100/T-101)`.
- [ ] Test count delta from this plan: +8 (cumulative phase total: +3 (36-01) + 8 (36-02) = +11; within target +7~12).
</success_criteria>

<output>
After completion, create `.planning/phases/36-manual-test-fixes/36-02-SUMMARY.md` with this
frontmatter:

```yaml
---
phase: 36-manual-test-fixes
plan: 02
plan_name: allow-paths
status: complete
completed_at: <ISO-8601 UTC>
test_count_delta: 8
files_modified:
  - src/BlueCode.Cli/CliArgs.fs
  - src/BlueCode.Cli/CompositionRoot.fs
  - src/BlueCode.Cli/Program.fs
  - src/BlueCode.Cli/Adapters/FsToolExecutor.fs
  - tests/BlueCode.Tests/CliArgsTests.fs
  - tests/BlueCode.Tests/FileToolsTests.fs
  - tests/BlueCode.Tests/ToolExpansionTests.fs   # IF create root call sites were updated
core_diff_lines: 0
commits:
  - feat(36-02): add --allow-paths CLI flag (T-16..T-19/T-100/T-101)
subsystem: cli-adapter
affects: [36-03]
requires: [36-01]
---
```

Body sections (≤300 lines):
- Outcome: --allow-paths flag wired end-to-end. T-16..T-19/T-100/T-101 unblocked
  (verifiable in manual round 2 via the doc updates from Plan 36-03).
- Code change summary: signatures, validatePathWithExtras, threading.
- Test additions: list each new test case.
- Verification: build, test suite, Core diff, optional smoke.
- Open follow-ups: bench gate verification at Plan 36-03; manual-test-guide.md updates at
  Plan 36-03; glob-pattern allow-paths (deferred per phase out-of-scope).
</output>
