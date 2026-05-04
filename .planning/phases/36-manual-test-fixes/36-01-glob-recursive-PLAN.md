---
phase: 36-manual-test-fixes
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/BlueCode.Cli/Adapters/FsToolExecutor.fs
  - tests/BlueCode.Tests/ToolExpansionTests.fs
autonomous: true

must_haves:
  truths:
    - "Calling GlobSearch with a bare pattern like '*.fsproj' (no '/' separator and no '**' prefix) enumerates ALL matching files recursively under projectRoot — equivalent in behaviour to '**/*.fsproj'"
    - "GlobSearch with a pattern containing '/' or starting with '**' is unchanged — 'src/**/*.fs', '**/*.nonexistent', 'src/a.fs' all behave exactly as before"
    - "Manual T-14 reproduction (`*.fsproj` against the actual repo) returns exactly 3 paths: src/BlueCode.Cli/BlueCode.Cli.fsproj, src/BlueCode.Core/BlueCode.Core.fsproj, tests/BlueCode.Tests/BlueCode.Tests.fsproj"
    - "The 100-match cap, hidden-file inclusion, and PathEscapeBlocked behaviour of glob_search are preserved (existing tests in globSearchTests still pass)"
    - "Zero changes to src/BlueCode.Core/** (`git diff master -- src/BlueCode.Core/` empty for this plan's commits)"
  artifacts:
    - path: "src/BlueCode.Cli/Adapters/FsToolExecutor.fs"
      provides: "Bare-pattern auto-expansion in globSearchImpl (~line 508)"
      contains: "effectivePattern"
    - path: "tests/BlueCode.Tests/ToolExpansionTests.fs"
      provides: "≥2 new tests covering bare-pattern recursive matching + non-expansion of '**/' patterns"
      contains: "auto-expand"
  key_links:
    - from: "src/BlueCode.Cli/Adapters/FsToolExecutor.fs (globSearchImpl)"
      to: "src/BlueCode.Cli/Adapters/FsToolExecutor.fs (globToRegex, line 410)"
      via: "effectivePattern computed BEFORE globToRegex call"
      pattern: "globToRegex effectivePattern"
    - from: "tests/BlueCode.Tests/ToolExpansionTests.fs (new bare-pattern tests)"
      to: "BlueCode.Tests.RouterTests.fs rootTests"
      via: "ToolExpansionTests.tests already registered (line 98 of RouterTests.fs); no rootTests/fsproj edits needed"
      pattern: "BlueCode\\.Tests\\.ToolExpansionTests\\.tests"
---

<objective>
Phase 36 — Plan 01: Fix `glob_search` so a bare pattern like `*.fsproj` enumerates files
recursively (currently returns 0 matches because `globToRegex "*.fsproj"` produces
`^[^/]*\.fsproj$`, which cannot match relative paths containing `/`). Resolves manual test
finding T-14.

Purpose: Smallest, lowest-risk track in Phase 36. Fixing this in isolation lets us
ship/verify it before the larger Plan 36-02 (`--allow-paths`) touches the same file. The
fix is a 4-line auto-expansion in `globSearchImpl` (Cli adapter, not Core). LLM behaviour
benefits: model can use the natural pattern shape (`*.fsproj`) without needing to know the
`**/` recursive-glob convention.

Output:
- 4-line `effectivePattern` block added in `globSearchImpl` at the call-site of `globToRegex`
- 2 new test cases in `ToolExpansionTests.fs` (`globSearchTests`):
  (a) bare `*.fsproj` matches `a.fsproj` AND `nested/b.fsproj` AND `deeper/x/c.fsproj`
  (b) explicit `**/*.fsproj` continues to match (regression guard against double-expansion)
- Zero changes to: `src/BlueCode.Core/**`, `BlueCode.Cli/CompositionRoot.fs`,
  `BlueCode.Cli/CliArgs.fs`, `BlueCode.Cli/Program.fs`, `BlueCode.Tests.fsproj`,
  `RouterTests.fs` (ToolExpansionTests.tests already in rootTests)
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
@CLAUDE.md
@src/BlueCode.Cli/Adapters/FsToolExecutor.fs
@tests/BlueCode.Tests/ToolExpansionTests.fs
@tests/BlueCode.Tests/RouterTests.fs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add bare-pattern auto-expansion in globSearchImpl</name>
  <files>src/BlueCode.Cli/Adapters/FsToolExecutor.fs</files>
  <action>
Open `src/BlueCode.Cli/Adapters/FsToolExecutor.fs`. Locate `globSearchImpl` (~line 492-527).

Within the `Ok root ->` branch (currently starts ~line 506-507), the existing code is:

```fsharp
            try
                let rx = globToRegex pattern
                let opts = EnumerationOptions()
```

Replace the `let rx = globToRegex pattern` line with the auto-expansion block:

```fsharp
            try
                // Phase 36-01 (T-14 fix): bare patterns without '/' and not already
                // starting with '**' are auto-expanded to '**/'+pattern. Without this,
                // globToRegex "*.fsproj" produces "^[^/]*\\.fsproj$" which cannot match
                // relative paths like "src/X/Y.fsproj" because [^/]* does not cross '/'.
                // Patterns with explicit path structure ("src/**/*.fs", "**/*.fs",
                // "Cargo.toml") are left untouched.
                let effectivePattern =
                    if not (pattern.Contains('/')) && not (pattern.StartsWith("**"))
                    then "**/" + pattern
                    else pattern
                let rx = globToRegex effectivePattern
                let opts = EnumerationOptions()
```

DO NOT change anything else in `globSearchImpl` (the EnumerationOptions block,
filtering, truncation, return shape, error handlers all remain identical).

Verify the file compiles by running `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj`
from the repo root.

After the build succeeds, commit:
```
git add src/BlueCode.Cli/Adapters/FsToolExecutor.fs
git commit -m "fix(36-01): auto-expand bare glob patterns to recursive (T-14)

globToRegex \"*.fsproj\" produces ^[^/]*\\.fsproj\$ which never matches
relative paths containing /. globSearchImpl now prepends **/ when the
pattern lacks a / separator AND does not already start with **. Patterns
with explicit path structure (src/**/*.fs, **/*.nonexistent) are unchanged."
```
  </action>
  <verify>
1. `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.
2. `grep -n "effectivePattern" src/BlueCode.Cli/Adapters/FsToolExecutor.fs` shows ≥2 matches (the let-binding and the use-site).
3. `git diff master -- src/BlueCode.Core/ | wc -l` outputs `0` (Core untouched).
4. The diff against master for this single file is ≤15 added lines, 1 changed line.
  </verify>
  <done>
- [x] `globSearchImpl` contains the `effectivePattern` block before the `globToRegex` call.
- [x] Build succeeds with no warnings introduced.
- [x] Single atomic commit `fix(36-01): auto-expand bare glob patterns to recursive (T-14)`.
- [x] Core untouched.
  </done>
</task>

<task type="auto">
  <name>Task 2: Add 3 unit tests for bare-pattern auto-expansion in ToolExpansionTests.fs</name>
  <files>tests/BlueCode.Tests/ToolExpansionTests.fs</files>
  <action>
Open `tests/BlueCode.Tests/ToolExpansionTests.fs`. Locate the existing `globSearchTests`
testList (`let globSearchTests = testList "FsToolExecutor.GlobSearch (TLX-02)" [...]`,
starts ~line 123).

Add 3 new test cases as the LAST elements inside the `[ ... ]` list (after the existing
"hidden files are included" test). Use the EXACT same fixture pattern as the surrounding
tests (`newFixture ()`, `cleanup`, `try/finally`).

Test 1 — bare pattern matches recursively:
```fsharp
          testCase "Phase 36-01: bare pattern auto-expands to **/ recursive (T-14 fix)"
          <| fun () ->
              let root = newFixture ()
              try
                  // Top-level + 1 nested + 2 nested file with .fsproj extension
                  File.WriteAllText(Path.Combine(root, "top.fsproj"), "")
                  Directory.CreateDirectory(Path.Combine(root, "src", "Inner")) |> ignore
                  File.WriteAllText(Path.Combine(root, "src", "mid.fsproj"), "")
                  File.WriteAllText(Path.Combine(root, "src", "Inner", "deep.fsproj"), "")
                  // Distractor: same name in non-matching extension
                  File.WriteAllText(Path.Combine(root, "src", "Inner", "deep.fs"), "")
                  let exe = create root
                  let result = exec exe (GlobSearch("*.fsproj", None))
                  match result with
                  | Ok(Success body) ->
                      Expect.stringContains body "top.fsproj" "top-level .fsproj must match"
                      Expect.stringContains body "src/mid.fsproj" "1-level-deep .fsproj must match"
                      Expect.stringContains body "src/Inner/deep.fsproj" "2-level-deep .fsproj must match"
                      Expect.isFalse (body.Contains("deep.fs\n") || body.EndsWith("deep.fs")) ".fs must NOT match .fsproj pattern"
                  | other -> failtestf "expected Success with 3 matches, got %A" other
              finally
                  cleanup root
```

Test 2 — '**/'-prefixed pattern is NOT double-expanded (regression guard):
```fsharp
          testCase "Phase 36-01: '**/*.ext' pattern is NOT double-expanded"
          <| fun () ->
              let root = newFixture ()
              try
                  File.WriteAllText(Path.Combine(root, "a.txt"), "")
                  Directory.CreateDirectory(Path.Combine(root, "sub")) |> ignore
                  File.WriteAllText(Path.Combine(root, "sub", "b.txt"), "")
                  let exe = create root
                  let result = exec exe (GlobSearch("**/*.txt", None))
                  match result with
                  | Ok(Success body) ->
                      Expect.stringContains body "a.txt" "top-level .txt matches"
                      Expect.stringContains body "sub/b.txt" "nested .txt matches"
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root
```

Test 3 — pattern with explicit '/' is unchanged:
```fsharp
          testCase "Phase 36-01: pattern containing '/' is NOT auto-expanded"
          <| fun () ->
              let root = newFixture ()
              try
                  // Files at top-level should NOT match "src/*.fs"
                  File.WriteAllText(Path.Combine(root, "topLevel.fs"), "")
                  Directory.CreateDirectory(Path.Combine(root, "src")) |> ignore
                  File.WriteAllText(Path.Combine(root, "src", "inSrc.fs"), "")
                  let exe = create root
                  let result = exec exe (GlobSearch("src/*.fs", None))
                  match result with
                  | Ok(Success body) ->
                      Expect.stringContains body "src/inSrc.fs" "src/inSrc.fs matches src/*.fs"
                      Expect.isFalse (body.Contains("topLevel.fs")) "top-level topLevel.fs must NOT match src/*.fs"
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root
```

NO changes to `BlueCode.Tests.fsproj` (ToolExpansionTests.fs already in `<Compile Include>`).
NO changes to `RouterTests.fs` (`BlueCode.Tests.ToolExpansionTests.tests` already in `rootTests`).

Run the test suite:
```
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
```
Expected: all tests pass; total count = previous_total + 3 (was 333 at end of Phase 32; now 336 for this plan only).

Commit:
```
git add tests/BlueCode.Tests/ToolExpansionTests.fs
git commit -m "test(36-01): add 3 tests for bare-pattern auto-expansion

(a) bare *.fsproj matches top-level + nested files
(b) **/*.txt is NOT double-expanded
(c) src/*.fs (with /) is NOT auto-expanded — top-level files do not match
Test count delta: +3."
```
  </action>
  <verify>
1. `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | tail -10` shows all tests passing.
2. The summary line includes a count ≥ previous + 3 (target: was 333, now 336 — but absolute number is informational; the must-pass guarantee is 0 failures).
3. `grep -c "Phase 36-01" tests/BlueCode.Tests/ToolExpansionTests.fs` outputs `3` (3 testCase labels).
4. `git diff master -- src/BlueCode.Core/ | wc -l` outputs `0`.
  </verify>
  <done>
- [x] 3 new testCase entries inside `globSearchTests`.
- [x] All tests pass (0 failures).
- [x] Test count delta verified +3.
- [x] Single atomic commit `test(36-01): add 3 tests for bare-pattern auto-expansion`.
  </done>
</task>

</tasks>

<verification>
After both tasks:

1. `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0 with no new warnings.
2. `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0; final line includes
   `0 failed, X errored` with X=0; total = pre-plan + 3.
3. `git diff master -- src/BlueCode.Core/ | wc -l` outputs `0` (Core purity).
4. `bash scripts/check-no-async.sh` exits 0 (CI invariant; no `async {}` introduced — we used no CE at all).
5. Smoke-check the actual repo: from repo root, run a manual reproduction by writing a tiny F# script that creates the executor and invokes it (or trust the test suite — preferred). Optional: actually run `bc --verbose "List all *.fsproj files in this repository."` once the binary is rebuilt and confirm 3 fsproj paths appear in the result. This optional step is the human-eyeball T-14 PASS confirmation; automated tests already cover the unit invariant.

NOTE: Bench gate (`bash bench/run.sh --gate`) verification is deferred to Plan 36-03 (the
last plan in the wave chain) per phase quality gate. Running it after 36-01 alone is
optional but cheap (~2 min) — recommended if any doubt.
</verification>

<success_criteria>
- [ ] `globSearchImpl` auto-expands bare patterns (without `/` and not starting with `**`) by prepending `**/`.
- [ ] T-14 invariant: `*.fsproj` matches all 3 fsproj files in actual repo (verifiable via 1 extra fsi script or via the binary).
- [ ] 3 new unit tests pass (bare pattern, `**/*` non-double-expansion, `src/*` non-expansion).
- [ ] Existing globSearchTests cases (`src/**/*.fs`, `**/*.nonexistent`, hidden files, 100-cap, PathEscapeBlocked) all still pass.
- [ ] `git diff master -- src/BlueCode.Core/` is empty.
- [ ] 2 atomic commits: `fix(36-01): ...`, `test(36-01): ...`.
- [ ] Test count delta = +3 (within phase target +7~12).
</success_criteria>

<output>
After completion, create `.planning/phases/36-manual-test-fixes/36-01-SUMMARY.md` with this
frontmatter:

```yaml
---
phase: 36-manual-test-fixes
plan: 01
plan_name: glob-recursive
status: complete
completed_at: <ISO-8601 UTC>
test_count_delta: 3
files_modified:
  - src/BlueCode.Cli/Adapters/FsToolExecutor.fs
  - tests/BlueCode.Tests/ToolExpansionTests.fs
core_diff_lines: 0
commits:
  - fix(36-01): auto-expand bare glob patterns to recursive (T-14)
  - test(36-01): add 3 tests for bare-pattern auto-expansion
subsystem: cli-adapter
affects: [36-02, 36-03]   # both follow this in the wave chain (file conflict on FsToolExecutor.fs for 36-02)
requires: []
---
```

Body sections (≤200 lines total):
- Outcome: T-14 invariant achieved (bare `*.fsproj` enumerates 3 fsproj files).
- Code change: 4-line `effectivePattern` block + 3 unit tests.
- Verification: build, test suite, Core diff (0 lines).
- Open follow-ups: none for this plan; Plan 36-02 will also touch FsToolExecutor.fs (different region — `validatePath` and `create`); merge mechanics straightforward.
</output>
