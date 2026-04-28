---
phase: 08-tool-expansion
plan: 02
subsystem: tools
tags: [fsharp, dotnet, filesystem, regex, glob, edit, grep, expecto]

# Dependency graph
requires:
  - phase: 08-01
    provides: "Tool DU with EditFile/GlobSearch/GrepSearch cases, dispatchTool arms, FsToolExecutor failwith stubs, schema enum 8 values, system prompt 8 actions"
provides:
  - "editFileImpl: IndexOf-loop occurrence counting (0-match Failure, 1-match Success+write, N-match Failure file-unchanged)"
  - "globToRegex: **-aware glob-to-regex converter, IgnoreCase+Compiled"
  - "globSearchImpl: EnumerateFiles with AttributesToSkip=System, 100-match cap, relative paths"
  - "grepSearchImpl: Regex with 500ms per-line timeout, fileGlob path-separator rejection, 200-char line truncation, 100-match cap"
  - "ToolExpansionTests.fs: 18 test cases covering all three tools (6+5+7)"
  - "FsToolExecutor.create: all 7 match arms are real impls (no failwith stubs)"
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "IndexOf-loop occurrence counting: count-before-replace prevents String.Replace all-occurrence pitfall"
    - "Glob-to-regex converter: **→.*, *→[^/]*, ?→[^/], .→\\., literal→Escape; IgnoreCase for cross-platform"
    - "ReDoS guard: Regex constructed with TimeSpan.FromMilliseconds(500.0); RegexMatchTimeoutException caught per-line → false (non-match)"
    - "Hidden file policy: EnumerationOptions.AttributesToSkip = FileAttributes.System (NOT default Hidden|System) so .planning/, .gitignore visible to agents"
    - "fileGlob separator validation: reject '/' and '\\' in fileGlob before passing to EnumerateFiles searchPattern"

key-files:
  created:
    - tests/BlueCode.Tests/ToolExpansionTests.fs
  modified:
    - src/BlueCode.Cli/Adapters/FsToolExecutor.fs
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs

key-decisions:
  - "globToRegex uses System.Text.RegularExpressions.Regex fully-qualified inline (no open System.Text.RegularExpressions) to avoid namespace pollution — consistent with existing FsToolExecutor.fs style"
  - "grepSearchImpl catches :? System.ArgumentException (fully qualified) for invalid regex construction — avoids open clash"
  - "ToolExpansionTests ReDoS test uses task.Wait(TimeSpan.FromSeconds(3.0)) wall-clock guard in addition to the 500ms per-line timeout — belt-and-suspenders; tests the 'does not hang' guarantee directly"
  - "ToolExpansionTests.fs inserted after ContextWarningTests.fs (not after FileToolsTests.fs as suggested) — both are valid positions before RouterTests.fs; chosen end-of-list position avoids any .fsproj compile-order dependency issues"

patterns-established:
  - "All new FsToolExecutor impls follow the same structure: ct.ThrowIfCancellationRequested → validatePath → try/with IOException pattern"
  - "Test fixtures: newFixture (temp dir), cleanup (delete dir), exec (synchronous wrapper) — identical helpers across FileToolsTests.fs and ToolExpansionTests.fs"

# Metrics
duration: 5min
completed: 2026-04-25
---

# Phase 8 Plan 02: Tool Implementation Summary

**editFileImpl (IndexOf-loop count-then-replace), globSearchImpl (globToRegex + System-only AttributesToSkip), grepSearchImpl (500ms ReDoS timeout + separator validation) — FsToolExecutor now has 7 real impls; 18 new tests bring total to 236**

## Performance

- **Duration:** ~5 min
- **Started:** 2026-04-25T02:59:20Z
- **Completed:** 2026-04-25T03:04:01Z
- **Tasks:** 2
- **Files modified:** 4 (FsToolExecutor.fs, ToolExpansionTests.fs new, .fsproj, RouterTests.fs)

## Accomplishments

- Three `failwith "not yet wired (plan 08-02)"` stubs in FsToolExecutor.create replaced with real implementations
- editFileImpl enforces 0/1/N-match contract: file is NEVER written unless count = 1; multi-match returns the count in the error message
- globSearchImpl uses custom globToRegex (no new NuGet package) with `AttributesToSkip = FileAttributes.System` so hidden files like `.planning/`, `.gitignore` are visible to agents
- grepSearchImpl guards against ReDoS via `Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(500.0))`; per-line timeout exception caught as non-match (returns Ok, not hang)
- 18 new ToolExpansionTests covering all three tools' contracts (6+5+7); total test count 218→236

## Requirements Traceability

| Requirement | Status | Evidence |
|-------------|--------|---------|
| TLX-01: edit_file 0/1/N-match semantics | IMPLEMENTATION COMPLETE | editFileTests cases 1-3; IndexOf-loop count in editFileImpl |
| TLX-02: glob_search with 100-match cap, hidden file inclusion | IMPLEMENTATION COMPLETE | globSearchTests cases 1-5; AttributesToSkip=System verified by hidden-file test |
| TLX-03: grep_search with ReDoS guard, fileGlob validation | IMPLEMENTATION COMPLETE | grepSearchTests cases 1-7; 500ms timeout, separator rejection verified by tests |

## Task Commits

Each task was committed atomically:

1. **Task 1: editFileImpl + globToRegex + globSearchImpl + grepSearchImpl + wire match arms** - (feat)
2. **Task 2: ToolExpansionTests.fs + .fsproj + RouterTests.fs registration** - (test)

**Plan metadata:** (docs commit follows this summary creation)

## Files Created/Modified

- `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` - Added globToRegex helper, editFileImpl, globSearchImpl, grepSearchImpl; replaced 3 failwith stubs in create() with real calls; 387→592 lines
- `tests/BlueCode.Tests/ToolExpansionTests.fs` - New: 18 test cases across editFileTests/globSearchTests/grepSearchTests; fixture pattern from FileToolsTests.fs
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj` - Added `<Compile Include="ToolExpansionTests.fs" />` at line 25 (before RouterTests.fs at 26)
- `tests/BlueCode.Tests/RouterTests.fs` - Added `BlueCode.Tests.ToolExpansionTests.tests` to rootTests list after FileToolsTests.fileToolsTests

## Pitfalls Navigated (08-RESEARCH.md)

| Pitfall | How Handled |
|---------|-------------|
| Pitfall 1: String.Replace replaces ALL | Used IndexOf loop to count first; Replace called only when count = 1 |
| Pitfall 4: fileGlob path separator escape | Validated `g.Contains('/') \|\| g.Contains('\\')` before EnumerateFiles; returns Failure with clear message |
| Pitfall 5: Regex catastrophic backtracking | `Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(500.0))` per-line timeout; RegexMatchTimeoutException → false |
| Pitfall 7: EnumerationOptions default skips Hidden | Set `opts.AttributesToSkip <- FileAttributes.System` in both globSearchImpl and grepSearchImpl |
| Pitfall 8: Test registration miss | Registered in BOTH .fsproj (line 25) AND rootTests list; 236 tests confirm discovery |

## Deviations from Plan

None — plan executed exactly as written. All patterns from 08-RESEARCH.md (Patterns 3, 4, 5) used verbatim. The one minor structural choice (inserting ToolExpansionTests.fs after ContextWarningTests.fs rather than after FileToolsTests.fs as the plan suggested) is equivalent — both positions are before RouterTests.fs.

## Issues Encountered

None. Build and all 236 tests (18 new) passed on first run.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

Phase 8 complete. All 4 requirements satisfied:
- TLX-01/02/03: implementation complete (this plan)
- TST-04 (Phase 8 tests added): 18 new tests covering all three tools

Phase 9 (read_file metadata header) starts from:
- Domain.fs Tool DU is stable (7 cases); Phase 9 adds ReadFile metadata header to the existing ReadFile case
- All 236 v1.2 Phase 8 tests pass as baseline

---
*Phase: 08-tool-expansion*
*Completed: 2026-04-25*
