---
phase: 36-manual-test-fixes
plan: 01
plan_name: glob-recursive
status: complete
completed_at: 2026-05-04T09:00:14Z
test_count_delta: 3
files_modified:
  - src/BlueCode.Cli/Adapters/FsToolExecutor.fs
  - tests/BlueCode.Tests/ToolExpansionTests.fs
core_diff_lines: 0
commits:
  - "fix(36-01): auto-expand bare glob patterns to recursive (T-14)"
  - "test(36-01): add 3 tests for bare-pattern auto-expansion"
subsystem: cli-adapter
affects: [36-02, 36-03]
requires: []
---

# Phase 36 Plan 01: glob-recursive Summary

**One-liner:** Auto-expand bare glob patterns (no `/`, no `**` prefix) to `**/pattern` in `globSearchImpl` — resolves T-14 where `*.fsproj` returned 0 results.

## Outcome

T-14 invariant achieved. A bare pattern like `*.fsproj` now matches all fsproj files recursively under `projectRoot`. The 4-line `effectivePattern` block is the sole code change, prepended before the `globToRegex` call in `globSearchImpl`.

Patterns with explicit path structure (`src/**/*.fs`, `**/*.nonexistent`, `src/a.fs`) are left untouched — the guard requires BOTH `not (pattern.Contains('/'))` AND `not (pattern.StartsWith("**"))` before expansion triggers.

## Code Change

**`src/BlueCode.Cli/Adapters/FsToolExecutor.fs` (~line 508):**

Added 10 lines (comment + `let effectivePattern` block + changed `let rx` call-site):

```fsharp
// Phase 36-01 (T-14 fix): bare patterns without '/' and not already
// starting with '**' are auto-expanded to '**/'+pattern. ...
let effectivePattern =
    if not (pattern.Contains('/')) && not (pattern.StartsWith("**"))
    then "**/" + pattern
    else pattern
let rx = globToRegex effectivePattern
```

No other changes to `globSearchImpl` (EnumerationOptions, filtering, truncation, return shape, error handlers are all identical to before).

**`tests/BlueCode.Tests/ToolExpansionTests.fs`:**

3 new `testCase` entries added as the last elements inside `globSearchTests`:

| # | Label | Assertion |
|---|-------|-----------|
| 1 | Phase 36-01: bare pattern auto-expands to `**/` recursive (T-14 fix) | `*.fsproj` matches top-level, 1-deep, and 2-deep `.fsproj` files; `.fs` distractor excluded |
| 2 | Phase 36-01: `**/*.ext` pattern is NOT double-expanded | `**/*.txt` still matches top-level + nested `.txt` files |
| 3 | Phase 36-01: pattern containing `/` is NOT auto-expanded | `src/*.fs` matches only files inside `src/`; top-level `.fs` excluded |

No changes to `BlueCode.Tests.fsproj` or `RouterTests.fs` — `ToolExpansionTests.tests` was already registered.

## Verification

| Check | Result |
|-------|--------|
| `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` | 0 warnings, 0 errors |
| `dotnet run --project tests/BlueCode.Tests/...` | 336 passed, 1 ignored, 0 failed, 0 errored |
| Test count delta | +3 (333 → 336) |
| `git diff master -- src/BlueCode.Core/` | 0 lines (Core untouched) |
| `bash scripts/check-no-async.sh` | OK |
| `grep -n "effectivePattern" FsToolExecutor.fs` | 2 matches (let-binding + use-site) |
| `grep -c "Phase 36-01" ToolExpansionTests.fs` | 3 |

## Deviations from Plan

None — plan executed exactly as written.

## Open Follow-Ups

- Plan 36-02 (`--allow-paths`) also touches `FsToolExecutor.fs` but in a different region (`validatePath` / `create`). Merge mechanics are straightforward — no conflict with the `globSearchImpl` hunk from this plan.
- Plan 36-03 is the final plan in the wave chain; bench gate (`bash bench/run.sh --gate`) deferred to that plan's quality gate.
