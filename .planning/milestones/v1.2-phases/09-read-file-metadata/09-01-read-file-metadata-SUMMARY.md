---
phase: 09-read-file-metadata
plan: 01
subsystem: tools
tags: [read_file, metadata-header, fsharp, fstoolexecutor, tool-08, system-prompt]

# Dependency graph
requires: []  # independent of Phase 8; touches only readFileImpl + system prompt
provides:
  - "TOOL-08 metadata header on every read_file Success payload"
  - "Out-of-range bounds signal (status='out-of-range', requested range preserved verbatim) so 32B can self-correct after start_line > totalLines"
  - "System-prompt documentation of the header format under the read_file schema entry"
affects:
  - "Future tool-result rendering (Phase 9+ if header surfacing in TUI is added)"
  - "T6 32B benchmark — eliminates the deterministic infinite-retry loop on out-of-bounds start_line"

# Tech tracking
tech-stack:
  added: []  # no new NuGet packages
  patterns:
    - "metadata-header-prepend: Cli adapter prepends a fixed-format string header to ToolResult.Success payloads; truncation applies to content only, header is never truncated"
    - "raw-range preservation for out-of-range: do NOT clamp endLine to totalLines on the out-of-range branch; preserve the LLM's requested values so the bounds-violation signal is unambiguous"

key-files:
  created: []
  modified:
    - "src/BlueCode.Cli/Adapters/FsToolExecutor.fs - readFileImpl rewritten to compute totalLines, derive header, branch on out-of-range, prepend header to Success payloads"
    - "tests/BlueCode.Tests/FileToolsTests.fs - 4 new TOOL-08 testCases inside readFileTests; 1 existing test patched to anchor body assertions with '\\n' (header collision fix)"
    - "src/BlueCode.Cli/CompositionRoot.fs - 2 lines added under read_file in defaultSystemPrompt"

key-decisions:
  - "Unify on File.ReadAllLines (eager array) instead of mixing ReadAllText + ReadLines; trade negligible memory for single-pass total-line count"
  - "Header uses input 'path' (relative), never 'resolved' (absolute) — preserves CLAUDE.md no-absolute-paths invariant"
  - "Invalid range (e<s or s<1) keeps existing Failure return; no header on Failure paths (consistent with all other Failure callers in FsToolExecutor)"
  - "Out-of-range payload is header-only (no trailing newline, no content); content section truly empty rather than 'header + \\n + \"\"'"
  - "Body-substring test assertions anchored with '\\n' to avoid collision with header words like 'truncated' (contains 'a') and 'lines'/'file' (contain 'e') — generalizable pattern for any future test that asserts single-letter line absence"

patterns-established:
  - "metadata-header-prepend: applies only to Success payloads, never Failure / SecurityDenied / PathEscapeBlocked / Timeout"
  - "test substring anchoring: when a tool prepends fixed-format header text, single-letter content assertions must anchor with '\\n' or use multi-character distinct words"

# Metrics
duration: 6min
completed: 2026-04-25
---

# Phase 9 Plan 01: read_file Metadata Header Summary

**Every read_file Success now begins with `[file: <path>, lines X-Y of Z, <not-truncated|truncated|out-of-range>]\n` so 32B can detect bounds violations and stop infinite-retrying out-of-range start_line on small files.**

## Performance

- **Duration:** ~6 min (continuous execution, no checkpoints)
- **Started:** 2026-04-25T03:19:30Z (first read of plan)
- **Completed:** 2026-04-25T03:25:30Z (Task 3 commit)
- **Tasks:** 3 (all auto, all passed verify on first try after one auto-fix in Task 1)
- **Files modified:** 3 (FsToolExecutor.fs, FileToolsTests.fs, CompositionRoot.fs)

## Accomplishments

- **TOOL-08 closed** — last v1.2 milestone requirement satisfied
- 4 new TOOL-08 testCases in `readFileTests` covering all three header status values (`not-truncated`, `truncated`, `out-of-range`) plus the empty-content out-of-range case
- 240/240 tests pass (1 ignored — `BLUECODE_AGENT_SMOKE`-gated live Qwen test, intentionally disabled). Previous baseline 236 → 240 (+4)
- Zero changes to `src/BlueCode.Core/` — entire feature lives in the Cli adapter + system prompt + tests
- System prompt extended by 2 lines (~135 chars) so the LLM understands the header semantics — bounded prompt growth, well below the PERF-01 deferred-optimization threshold

## Task Commits

Each task committed atomically per CLAUDE.md convention (`{type}({phase}-{plan}): {name}`):

1. **Task 1: Implement metadata header in readFileImpl** — `bf6cfce` (feat)
2. **Task 2: Add four metadata-header testCases to readFileTests** — `ab14cd0` (test)
3. **Task 3: Update system prompt to document read_file header format** — `32f5376` (feat)

(Plan-metadata commit follows after this SUMMARY is staged.)

## Files Created/Modified

- `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` — `readFileImpl` rewritten end-to-end: unified eager `File.ReadAllLines` read, `totalLines` computation, three-branch effective-range/status calculation (None / in-range Some / out-of-range Some), `sprintf "[file: %s, lines %d-%d of %d, %s]"` header, header+content payload assembly with `truncateOutput` applied to content only. Invalid-range (`e<s`) branch returns `Failure(1, ...)` without a header. Exception handlers unchanged. +63 lines net.
- `tests/BlueCode.Tests/FileToolsTests.fs` — 4 new `testCase "TOOL-08: ..."` entries appended inside the existing `readFileTests` testList (no new module, no `rootTests` change). 1 existing test (`line range (2, 3) returns middle lines`) updated to anchor single-letter `Contains` assertions with `\n` so header-word collisions cannot false-match. +98 lines net (94 new + 4 modified-context).
- `src/BlueCode.Cli/CompositionRoot.fs` — 2 lines added under `read_file` in `defaultSystemPrompt` describing the header format and the out-of-range remediation hint. +2 lines.

## Decisions Made

- **Unified on `File.ReadAllLines`** — replaces the previous mix of `File.ReadAllText` (None branch) and `File.ReadLines` (Some branch). Single eager read gives both the array (for slicing) and the count (for the header). Trailing-newline behavior shifts slightly (`String.Join("\n", ReadAllLines(...))` drops the trailing newline that `ReadAllText` would preserve), but no test depended on exact trailing-newline preservation — all `readFileTests` use `stringContains`.
- **Header uses input `path` (relative), never `resolved` (absolute)** — load-bearing per CLAUDE.md "no absolute paths in src/" invariant. Verified post-commit: `grep -rn "/Users/" src/BlueCode.Core/ --include="*.fs"` returns zero matches.
- **Out-of-range preserves the RAW requested `endLine`** — do NOT clamp to `totalLines` on this branch (RESEARCH Pitfall 3). Header reads `lines 2001-2100 of 150, out-of-range` so the LLM sees both the violation AND the bound (Z=150) it needs to respect.
- **Invalid range stays as `Failure`** — RESEARCH Pitfall 1. Headers only attach to `Success` payloads; mixing them onto `Failure` would violate the schema convention used by every other call site in `FsToolExecutor`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Existing test `line range (2, 3) returns middle lines` regressed on header letter collision**

- **Found during:** Task 1 verify step (full test run after `readFileImpl` change)
- **Issue:** The existing test asserted `Expect.isFalse (content.Contains("a")) "should NOT include line 1"` on a file `"a\nb\nc\nd\ne\n"` read with range (2, 3). After Task 1 prepended the header, the payload became `[file: lines.txt, lines 2-3 of 5, not-truncated]\nb\nc` — and the word `truncated` contains the letter `a`. Same collision applied to the `d` assertion (the literal `d` appears in `truncated` and at end of `[file: ...]`). RESEARCH.md Pitfall 5 audited for `Expect.equal` mismatches but did not flag single-letter `Contains` brittleness.
- **Fix:** Switched the four single-letter `Contains`/`isFalse Contains` assertions to `\n`-anchored variants (`"\na\n"`, `"\nb"`, `"\nc"`, `"\nd"`). The header is on a single line with no embedded newlines, so `\n`-prefixed substrings can only match body content. Test intent preserved (lines 2-3 present, lines 1 & 4 absent), brittleness eliminated.
- **Files modified:** `tests/BlueCode.Tests/FileToolsTests.fs` (lines 86-89)
- **Verification:** Re-ran full Expecto suite after fix → 236/236 pass + 1 ignored. Then added Task 2's four new TOOL-08 cases → 240/240 pass.
- **Committed in:** `bf6cfce` (rolled into the Task 1 commit since the test fix is the direct consequence of Task 1's behavioral change)

**2. [Rule 1 - Bug] Plan-supplied Task 2 test `bounded read (not-truncated)` would have hit the same collision**

- **Found during:** Task 2 (while transcribing the plan-verbatim test code)
- **Issue:** The plan's Task 2 spec for the "bounded read" testCase asserted `Expect.isFalse (content.Contains("a")) "line 1 absent"` and `Expect.isFalse (content.Contains("e")) "line 5 absent"` on a file `"a\nb\nc\nd\ne\n"` read with range (2, 4). Same root cause as deviation #1: header `[file: range.txt, lines 2-4 of 5, not-truncated]` contains both `a` (in "truncated") and `e` (in "lines", "file", "truncated").
- **Fix:** Anchored the bounded-read body assertions with `\n`: `"\nb"`, `"\nc"`, `"\nd"` for presence; `"\na"`, `"\ne"` for absence. Same pattern as deviation #1.
- **Files modified:** `tests/BlueCode.Tests/FileToolsTests.fs` (lines 174-178 of new code)
- **Verification:** All 4 TOOL-08 testCases pass; full suite 240/240.
- **Committed in:** `ab14cd0` (Task 2 commit)

**3. [Rule 1 - Bug] Plan-supplied Task 2 test `out-of-range` had a self-defeating assertion**

- **Found during:** Task 2 (transcription audit)
- **Issue:** The plan's Task 2 spec asserted `Expect.isFalse (content.Contains("lines")) "no file content in out-of-range response"` on a file containing the line `"lines\n"`. But the header itself is `[file: small.txt, lines 100-200 of 3, out-of-range]` — the literal word `lines` appears in the header. The assertion would always fail by header-collision regardless of body content.
- **Fix:** Dropped the third absence assertion. Kept the two independent body markers `only` and `three` (neither word appears anywhere in the fixed header format) which are sufficient to prove the content section is empty. Added an inline comment explaining the omission.
- **Files modified:** `tests/BlueCode.Tests/FileToolsTests.fs` (out-of-range testCase, comment added)
- **Verification:** out-of-range testCase passes; full suite 240/240.
- **Committed in:** `ab14cd0` (Task 2 commit)

---

**Total deviations:** 3 auto-fixed (all Rule 1 — pre-existing test brittleness + plan-supplied test brittleness exposed by the new header). All three are facets of the same underlying issue: header-text vs body-text substring collision on single-letter or single-word checks. RESEARCH.md Pitfall 5 anticipated the *category* of risk but missed these three specific instances.

**Impact on plan:** Zero scope creep. All fixes are pure test-assertion robustness — no production code beyond what the plan specified. Net result: the test suite is now MORE robust against future header-format changes (any header word containing `a`, `b`, `c`, `d`, `e` is now safe).

## Issues Encountered

- **`dotnet test` doesn't actually run tests in this project** — confirmed the project-state warning. The plan's `<verify>` blocks reference `dotnet test ... --filter "FullyQualifiedName~ReadFile"` but Expecto with the explicit `rootTests` list pattern + `[<EntryPoint>]` requires `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj`. Used the run-based runner throughout. Worth flagging in any future Phase 9+ planning that uses Expecto patterns.
- **Header `out-of-range` substring count differs from plan verify** — Task 3 verify said `grep -c "out-of-range" src/BlueCode.Cli/CompositionRoot.fs` should return `1`, but the actual two-line addition the plan mandates contains the word twice (once in "Tool output begins: [file: ..., not-truncated|truncated|out-of-range]" and once in "If out-of-range: requested start_line ..."). The verify count is plan-internal inconsistency, not a deviation; the prompt content matches the plan's `<action>` block exactly.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- **TOOL-08 closed** — v1.2 Tool Expansion milestone now has 4/4 REQs satisfied (TLX-01/02/03 ✓ from Phase 8, TOOL-08 ✓ from Phase 9). Ready for `/gsd:verify-work 9` and then `/gsd:complete-milestone v1.2`.
- **No blockers.** Build green, 240/240 tests pass, Core untouched, no absolute-path leak in `src/`.
- **Live LLM verification still recommended** — the unit tests cover the Success payload shape behaviorally, but a brief manual T6-style probe (`blueCode "read lines 2001-2100 of <a 150-line file>"`) would confirm 32B uses the new header to self-correct rather than retry. This is a `/gsd:verify-work 9` UAT activity, not a plan-level checkpoint.

---
*Phase: 09-read-file-metadata*
*Completed: 2026-04-25*
