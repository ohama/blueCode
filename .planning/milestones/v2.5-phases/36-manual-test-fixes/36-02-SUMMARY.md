---
phase: 36-manual-test-fixes
plan: 02
plan_name: allow-paths
status: complete
completed_at: 2026-05-04T18:09:30Z
test_count_delta: 8
files_modified:
  - src/BlueCode.Cli/CliArgs.fs
  - src/BlueCode.Cli/CompositionRoot.fs
  - src/BlueCode.Cli/Program.fs
  - src/BlueCode.Cli/Adapters/FsToolExecutor.fs
  - tests/BlueCode.Tests/CliArgsTests.fs
  - tests/BlueCode.Tests/FileToolsTests.fs
  - tests/BlueCode.Tests/RunShellTests.fs
  - tests/BlueCode.Tests/ToolExpansionTests.fs
core_diff_lines: 0
commits:
  - "feat(36-02): add --allow-paths CLI flag (T-16..T-19/T-100/T-101)"
subsystem: cli-adapter
affects: [36-03]
requires: [36-01]
---

# Phase 36 Plan 02: allow-paths Summary

**One-liner:** `--allow-paths /tmp/bc-test` wired end-to-end via Argu → CliOptions → FsToolExecutor.validatePathWithExtras with Path.GetFullPath canonicalization and trailing-separator prefix-attack guard.

## Outcome

T-16/17/18/19/100/101 unblocked: file tools now accept paths that resolve under any `--allow-paths`-listed directory. Default (no flag) preserves the pre-Phase-36 projectRoot-only security invariant byte-identically. Bench/CI invokes without the flag; no regression.

## Code Change Summary

### CliArgs.fs

Added `[<AltCommandLine("--allow-paths")>] AllowPaths of paths: string` to the `CliArgs` DU. The `AltCommandLine` attribute is required because Argu strips hyphens from DU case names by default (`AllowPaths` → `--allowpaths`); without the alias the plan's `--allow-paths` form would not be recognized.

Added matching `IArgParserTemplate.Usage` entry.

### CompositionRoot.fs

Added `AllowPaths: string list` field to `CliOptions` record (after `PlanMode`). Added `AllowPaths = []` to `defaultCliOptions`. Updated `bootstrap` to pass `opts.AllowPaths` to `FsToolExecutor.create`.

### Program.fs

After `isPlanMode` parse, added `allowPaths` binding: `TryGetResult CliArgs.AllowPaths |> Option.map (split on ',' + Trim + filter empty) |> Option.defaultValue []`. Added `AllowPaths = allowPaths` to the `opts` record.

### FsToolExecutor.fs

**`validatePath` replaced by `validatePathWithExtras`:**
- Parameters: `(projectRoot: string) (extraRoots: string list) (inputPath: string)`
- Same `~` rejection and whitespace guard.
- Same `Path.Combine + Path.GetFullPath` canonicalization (defeats `..` traversal).
- `withSep` helper applies trailing-separator guard to BOTH `projectRoot` and each `extraRoot`.
- `inRoot` predicate: `resolved = r || resolved.StartsWith(withSep r, StringComparison.Ordinal)`.
- Returns `Ok resolved` if `inRoot projectRoot || List.exists inRoot extraRoots`.

**All 6 path-validating `*Impl` functions updated:**
- `readFileImpl`, `writeFileImpl`, `listDirImpl`, `editFileImpl`, `globSearchImpl`, `grepSearchImpl` each gained `(extraAllowedPaths: string list)` as the second parameter.
- Each function's internal `validatePath projectRoot path` call changed to `validatePathWithExtras projectRoot extraAllowedPaths path` (or `... p` for searchPath arms).
- `runShellImpl` left unchanged (no path validation).

**`create` factory updated:**
- Signature: `(projectRoot: string) (extraAllowedPaths: string list) : IToolExecutor`
- Normalizes at construction: `rootNormalized = Path.GetFullPath(projectRoot)`, `allowedNormalized = extraAllowedPaths |> List.map Path.GetFullPath`.
- Passes `allowedNormalized` to all 6 `*Impl` calls; `runShellImpl` call unchanged.

**Existing test call-site migration (`create root` → `create root []`):**
- FileToolsTests.fs: 19 occurrences updated.
- RunShellTests.fs: 8 occurrences updated.
- ToolExpansionTests.fs: 21 occurrences updated.
- Semantic: empty list → identical behaviour to old single-arg `create`.

## Test Additions

### CliArgsTests.fs (+2)

1. `--allow-paths /tmp/x with prompt: TryGetResult AllowPaths = Some "/tmp/x"` — verifies single path captured as raw string, prompt still captured.
2. `--allow-paths /tmp/x,/tmp/y: TryGetResult AllowPaths = Some "/tmp/x,/tmp/y"` — verifies comma-separated raw string stored verbatim (splitting happens in Program.fs, not Argu).

### FileToolsTests.fs (+6) — `allowPathsTests` sub-list

1. `empty extraAllowedPaths preserves projectRoot-only behaviour` — two distinct temp dirs; `create root []`; read from outside root → `PathEscapeBlocked`.
2. `extraAllowedPath permits read of file inside that path` — `create root [extra]`; read file in `extra` → `Success` with expected content.
3. `extraAllowedPath permits write_file inside that path` — `create root [extra]`; write to `extra/wrote.txt` → `Success`; file content verified.
4. `trailing-separator guard: '/tmp/bc-test' does NOT permit '/tmp/bc-testing'` — GUID-prefixed dirs; `allowed = /tmp/bc-<g>`, `sibling = /tmp/bc-<g>-sibling`; `create root [allowed]`; read from sibling → `PathEscapeBlocked`.
5. `.. traversal blocked even with broad allow list` — `create root [extra]`; read `Path.Combine(extra, "..", "etc", "definitely-not-real-file-12345")` → `PathEscapeBlocked` (canonicalization resolves traversal before security check; file existence irrelevant).
6. `non-allow-listed absolute path is blocked` — three distinct temp dirs; `create root [extra]`; read from `elsewhere` (not listed) → `PathEscapeBlocked`.

## Deviations from Plan

### Auto-fixed: AltCommandLine for --allow-paths (Rule 1 — Bug during implementation)

**Found during:** Task 3 test run (CliArgs tests failed with actual=None, expected=Some "/tmp/x").

**Issue:** Argu converts DU case names to flag names by stripping hyphens and lowercasing. `AllowPaths` became `--allowpaths`, not `--allow-paths`. The parse test used `[| "--allow-paths"; "/tmp/x"; "hi" |]` and got `None`.

**Fix:** Added `[<AltCommandLine("--allow-paths")>]` attribute to the `AllowPaths` DU case. This registers `--allow-paths` as a recognized alias while `--allowpaths` also continues to work.

**Files modified:** `src/BlueCode.Cli/CliArgs.fs` (1-line change to DU case attribute list).

**Commit:** `23d4edb` (same feat commit, single cohesive fix).

### Additional test files migrated: RunShellTests.fs (not in plan)

**Found during:** `grep -l "create root"` sweep revealed RunShellTests.fs (8 occurrences) in addition to the plan-listed FileToolsTests.fs and ToolExpansionTests.fs.

**Fix:** Applied `create root []` migration to RunShellTests.fs. Staged and committed with the same feat commit.

**Semantic:** Pure signature migration; no behaviour change. RunShellTests.fs already in SUMMARY `files_modified` list.

## Verification

1. `dotnet build` — 0 errors, 0 warnings.
2. `dotnet run --project tests/BlueCode.Tests/` — 344/344 passed (0 failed, 0 errored). Pre-plan 336 + 8 = 344.
3. `git diff master -- src/BlueCode.Core/ | wc -l` — `0`.
4. `bash scripts/check-no-async.sh` — exits 0 (no `async {}` in Core).
5. `git diff master --stat -- src/ tests/` — changes only in `src/BlueCode.Cli/` and `tests/BlueCode.Tests/`.

## Open Follow-Ups

- **Bench gate verification:** `bash bench/run.sh --gate` — deferred to Plan 36-03 (wave 3 gate).
- **manual-test-guide.md updates for T-16/17/18/19/100/101:** deferred to Plan 36-03 (combined with PlanValidator UX improvements).
- **Glob/wildcard patterns in --allow-paths** (e.g. `--allow-paths /tmp/bc-*`): out of scope for Phase 36 per ROADMAP; would require pattern matching rather than prefix-string comparison.
- **Auto/default /tmp/* allowlist:** out of scope by design; only explicit `--allow-paths` opt-in is supported (preserves security invariant for bench/CI).
