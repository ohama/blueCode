---
phase: 11-system-prompt-shrink
plan: 01
subsystem: agent-loop
tags: [fsharp, agent-loop, loop-injection, system-prompt, post-read-hint, perf]

# Dependency graph
requires:
  - phase: 09.1-system-prompt-tightening
    provides: "lastEditPath / [POST-EDIT CONSTRAINT] loop-injection primitive (Plan 09.1-05)"
provides:
  - "lastReadHint: (string * string) option parameter threaded through runLoop and buildMessages"
  - "[POST-READ HINT] System message injection for truncated and out-of-range read_file results"
  - "PERF-02 architectural lever: contextual hints moved from base prompt to per-iteration injection"
affects:
  - "11-02-PLAN.md (prompt shrink): can now safely remove truncated/out-of-range sentences from base prompt"
  - "Any future plan extending the loop-injection pattern"

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Loop-injection via function parameter threading: (string * string) option carries (path, status) without Domain.fs churn"
    - "First-line substring detection: parse header status from payload without regex or full-parse"

key-files:
  created: []
  modified:
    - src/BlueCode.Core/AgentLoop.fs
    - tests/BlueCode.Tests/AgentLoopTests.fs

key-decisions:
  - "(string * string) option shape chosen over a new DU to keep Domain.fs untouched — mirrors 09.1-05 string option discipline"
  - "Two-arm hint logic (truncated vs out-of-range) chosen over a single generic message because corrective actions differ"
  - "First-line-only substring match ('`, truncated]`' / '`, out-of-range]`') avoids false positives from file content"

patterns-established:
  - "Loop-injection Option A (post-user System message) extended to read_file tool results"
  - "Parameter shape: (string * string) option for (path, status) pairs, threaded from runSession through runLoop to buildMessages"

# Metrics
duration: 8min
completed: 2026-04-26
---

# Phase 11 Plan 01: Post-read_file [POST-READ HINT] injection extending 09.1-05 loop-injection primitive (PERF-02 architectural lever)

**Post-read_file [POST-READ HINT] injection extending 09.1-05's lastEditPath loop-injection primitive (PERF-02 architectural lever)**

## Performance

- **Duration:** ~8 min
- **Started:** 2026-04-26T07:51:00Z
- **Completed:** 2026-04-26T07:59:00Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Threaded `lastReadHint: (string * string) option` from `runSession` through `runLoop` into `buildMessages` — no Domain.fs change
- Two-arm `[POST-READ HINT]` System message injection: truncated path gets "pick smaller window" hint; out-of-range path gets "choose start_line <= total_lines" hint
- New `testCaseAsync` inside existing `agentLoopTests` testList proves injection fires on truncated header and NOT on first call; test count 242 → 243

## Task Commits

Each task was committed atomically:

1. **Task 1: Thread lastReadHint through runLoop and inject [POST-READ HINT] in buildMessages** - `61b24f6` (feat)
2. **Task 2: Add testCaseAsync proving the post-read_file injection fires on a truncated header** - `38409dc` (test)

**Plan metadata:** (docs(11-01): complete post-read_file injection plan — committed after SUMMARY/STATE)

## Files Created/Modified

- `src/BlueCode.Core/AgentLoop.fs` — Added `lastReadHint: (string * string) option` to `buildMessages` (new 5th param) and `runLoop` (new 11th param); `buildMessages` extended with two-arm match appending `[POST-READ HINT]` System messages; `runLoop` computes `lastReadHint'` from `ReadFile (FilePath p, _), Success payload` by checking first-line substrings; `runSession` passes second `None` for initial value. +50/-12 lines.
- `tests/BlueCode.Tests/AgentLoopTests.fs` — Added `testCaseAsync "post-read_file injection: truncated header triggers [POST-READ HINT] on next call"` inside the existing `agentLoopTests` testList. No new module, no fsproj change, no RouterTests.fs change. +49 lines.

## Injection Text (verbatim — Plan 11-02 reference)

**Truncated arm:**
```
[POST-READ HINT] The previous read_file on {path} returned truncated content (clipped to 2000 chars). Pick a smaller window — set end_line - start_line < 50 — and read again to get unclipped content.
```

**Out-of-range arm:**
```
[POST-READ HINT] The previous read_file on {path} returned out-of-range (start_line > total_lines). The header reported total_lines; choose a start_line <= total_lines and read again.
```

These are the sentences Plan 11-02 can safely remove from the base system prompt (PERF-02).

## Decisions Made

- `(string * string) option` shape chosen over a new DU to keep Domain.fs untouched — mirrors 09.1-05's `string option` for `lastEditPath`. If future plans need more variant arms, a DU refactor is the appropriate upgrade path.
- Two distinct hint messages (truncated vs out-of-range) rather than a single generic message: corrective actions differ ("smaller window" vs "valid start_line"), and matching the diagnostic specificity of the base-prompt sentences being replaced matters for T6 and read-heavy fixtures.
- First-line-only substring match (`", truncated]"` / `", out-of-range]"`) used instead of full-line parsing or regex: the FsToolExecutor header format is fixed (`[file: %s, lines %d-%d of %d, %s]`), the trailing `]` with leading `, ` makes false positives from file content essentially impossible.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- `lastReadHint` injection is in place and test-verified (243/1/0).
- Plan 11-02 (system prompt shrink) can now safely remove the two ~290-char truncated/out-of-range sentences from `defaultSystemPrompt` without regressing T6 or read-heavy bench fixtures — the injection fires post-tool-result instead.
- Core purity confirmed: `check-no-async.sh` exit 0, `llm-system` grep 0 lines, no Serilog/Spectre/Argu/HttpClient opens in Core.
- Domain.fs untouched (confirmed by `git diff src/BlueCode.Core/Domain.fs` empty).
- Wave 2 (Plan 11-02 — iteratively shrink `defaultSystemPrompt` to ≤800 chars, gate-validated) is unblocked.

---
*Phase: 11-system-prompt-shrink*
*Completed: 2026-04-26*
