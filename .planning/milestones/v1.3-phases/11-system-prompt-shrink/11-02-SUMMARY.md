---
phase: 11-system-prompt-shrink
plan: 02
subsystem: cli
tags: [system-prompt, bench, gate, FsToolExecutor, grep_search, edit_file, PERF-01]

requires:
  - phase: 11-01
    provides: "POST-READ HINT injection for truncated/out-of-range read_file results (PERF-02 lever)"

provides:
  - "defaultSystemPrompt shrunk from 1689 to 783 chars (54% reduction) — PERF-01 achieved"
  - "edit_file empty old_string bug fixed (infinite IndexOf loop → Failure with error message)"
  - "grep_search file-path support: path=file now searches that file, not just directories"

affects: ["11-03", "bench/baseline.json update"]

tech-stack:
  added: []
  patterns:
    - "Schema annotations in prompt action lines: old_string(non-empty exact file content) educates LLM on tool semantics"
    - "grep_search accepts both directory path and file path — file path triggers single-file search"
    - "edit_file guards against empty old_string before IndexOf loop to prevent infinite loop"

key-files:
  created: []
  modified:
    - src/BlueCode.Cli/CompositionRoot.fs
    - src/BlueCode.Cli/Adapters/FsToolExecutor.fs

key-decisions:
  - "Path C achieved (≤800 chars, final 783 chars)"
  - "Two Rule 3 auto-fixes in FsToolExecutor.fs were required to pass the gate (blocking bugs)"
  - "grep_search file-path fix chosen over changing the prompt annotation — model behavior is too deeply ingrained"
  - "edit_file empty old_string returns Failure(1, 'oldString must be non-empty') rather than silently appending"
  - "old_string annotation changed to '(non-empty exact file content)' — prevents 32B model from using empty string for append"

patterns-established:
  - "Action schema annotations in parens: {path, old_string(qualifier), new_string} — teaches LLM tool semantics without extra prose"
  - "Verify prompt semantics with live gate runs, not just char count — model behavior changes with shorter prompts"

duration: 75min
completed: 2026-04-26
---

# Phase 11 Plan 02: Prompt Shrink Summary

**defaultSystemPrompt shrunk 54% (1689→783 chars, Path C) with two blocking FsToolExecutor bug fixes enabling GATE PASS (8/8)**

## Performance

- **Duration:** 75 min
- **Started:** 2026-04-26T08:05:37Z
- **Completed:** 2026-04-26T09:20:00Z
- **Tasks:** 1 (iterative, 13 prompt cycles + 2 bug fix cycles)
- **Files modified:** 2

## Accomplishments

- Shrunk `defaultSystemPrompt` from 1689 chars (committed baseline) to 783 chars — 54% reduction, Path C target (≤800) achieved
- Fixed infinite loop bug in `edit_file` when `old_string=""` (`.IndexOf("", 0)` returns 0, idx never advances)
- Fixed `grep_search` to accept file paths (not just directory paths) — 72B model passes file path when file is mentioned in prompt
- Discovered and resolved that the awk measurement in the plan was incorrect for inline-closing-`"""` strings (actual size was 1689, not 2660 as claimed by the awk script)

## Shrink Iteration Log

| Cycle | Change | Chars | Gate Result |
|-------|--------|-------|-------------|
| 0 | Baseline (committed) | 1689 | (not run — discovered pre-existing T6_32b failures due to model variance) |
| 1 | Planner's "suggested first edit" — remove verbose schemas | 679 | FAIL: T6_32b MaxLoops (no grep_search guidance) |
| 2 | Add grep_search hint to Rules line | 747 | FAIL: T6_32b grep_search with file path → "path not found" |
| 3 | Add `(dir)` hint to grep_search schema | 763 | FAIL: T6_32b final schema violation (missing `answer` key) |
| 4 | Fix `final: {answer}` → `final: {"answer": "<text>"}` | 775 | T6_32b PASS (3 steps). W2_32b HANG — 32B server frozen, kickstarted ×2 |
| 5 | Investigate W2_32b hang — root cause: `edit_file` with `old_string=""` → IndexOf infinite loop | — | Applied Rule 3 fix: `FsToolExecutor.fs` empty old_string guard |
| 6 | Annotations on edit_file schema (many variants tried) | 771–815 | W2_32b: agent still generates empty old_string despite annotations |
| 7 | Changed annotation to `(non-empty exact file content)` | 788 | W2_32b PASS (3 steps). GATE: T6_72b FAIL (grep_search with file path) |
| 8 | Applied Rule 3 fix: grep_search accepts file path | — | FsToolExecutor: detect if path is file, search that file only |
| 9 | Simplified prompt to 783 chars (removed dir hint from Rules) | 783 | GATE PASS (8/8) |

## Final Prompt Content (783 chars)

```
You are blueCode, a coding agent driven by an F# recursive loop.

Respond with strict JSON only: {"thought": "<reasoning>", "action": "<one of: read_file | write_file | list_dir | run_shell | edit_file | glob_search | grep_search | final>", "input": {...}}

Inputs by action:
- read_file:   {path, start_line?, end_line?}
- write_file:  {path, content}
- list_dir:    {path, depth?}
- run_shell:   {command, timeout_ms?}
- edit_file:   {path, old_string(non-empty exact file content), new_string}
- glob_search: {pattern, path?}
- grep_search: {pattern, path?, file_glob?}
- final:       {"answer": "<text>"}

Rules: One tool per response. Use grep_search to locate symbols before reading large files. When done, respond with action="final". No prose, no markdown — JSON object only.
```

## Final Gate Output (gate-20260426-181718)

```
===== GATE: regression subset (8 invocations) =====
===== GATE: compare to baseline =====
  PASS T6_32b     steps=3/5 exit=0
  PASS T6_72b     steps=3/5 exit=0
  PASS W1_32b     steps=3/3 exit=0
  PASS W2_32b     steps=3/3 exit=0
  PASS T1_32b     steps=3/3 exit=0
  PASS T5_72b     steps=3/4 exit=0
  PASS B2_32b     steps=2/3 exit=0
  PASS B2_72b     steps=2/3 exit=0
===== GATE PASS (8/8) =====
```

## Task Commits

1. **Task 1: Shrink defaultSystemPrompt + fix FsToolExecutor bugs** — (feat)

**Plan metadata:** (docs commit to follow)

## Files Created/Modified

- `src/BlueCode.Cli/CompositionRoot.fs` — `defaultSystemPrompt` reduced 1689→783 chars with behavioral annotations
- `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` — two Rule 3 auto-fixes: empty old_string guard + grep_search file-path support

## Decisions Made

1. **Path C achieved (≤800)** — final size 783 chars, well within the 800-char target
2. **Two FsToolExecutor fixes required** — blocking bugs discovered during iteration that prevented gate pass; documented as Rule 3 deviations
3. **grep_search file-path support** — chosen over prompt annotation because 72B model's behavior of passing file path is deeply ingrained; accepting both directory and file paths is more robust
4. **edit_file annotation `(non-empty exact file content)`** — critical wording; variants like `(unique-match)`, `(exact match)`, `(exact substring to replace)` all failed to prevent empty old_string; only `(non-empty exact file content)` worked
5. **awk measurement was wrong** — the plan's awk script counted past the closing `"""` on inline strings; actual baseline was 1689 chars, not 2660 as claimed; Python regex measurement used instead

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] edit_file empty old_string causes infinite IndexOf loop**
- **Found during:** Task 1, Cycle 5 — W2_32b agent generated `"old_string": ""` for append, freezing the process for 13+ minutes
- **Issue:** `.NET String.IndexOf("", idx)` returns 0 for empty string; advancing idx by `oldString.Length = 0` means idx never advances → infinite loop
- **Fix:** Added `if oldString.Length = 0 then return Ok(Failure(1, "oldString must be non-empty"))` before the IndexOf loop
- **Files modified:** `src/BlueCode.Cli/Adapters/FsToolExecutor.fs`
- **Verification:** W2_32b no longer hangs; error message returned to LLM which recovers correctly
- **Committed in:**

**2. [Rule 3 - Blocking] grep_search with file path returns "path not found"**
- **Found during:** Task 1, Cycle 7 — T6_72b model consistently passed file path `src/BlueCode.Core/Domain.fs` to grep_search; tool calls `Directory.EnumerateFiles(filePath)` which fails because it's a file not a directory
- **Issue:** `grepSearchImpl` only handles directory paths; when user mentions a specific file in the prompt, 72B model uses that path directly
- **Fix:** Before calling `Directory.EnumerateFiles`, check if root is a file with `File.Exists(root)` → use `seq { root }` to search that single file
- **Files modified:** `src/BlueCode.Cli/Adapters/FsToolExecutor.fs`
- **Verification:** T6_72b now succeeds in 3 steps (grep_search finds Step at line 130, read 130-140, final)
- **Committed in:**

---

**Total deviations:** 2 auto-fixed (both Rule 3 - blocking)
**Impact on plan:** Both fixes were essential for gate pass. The edit_file fix prevents server hangs. The grep_search fix enables file-specific search which matches 72B model behavior. Both are improvements to tool robustness.

## Issues Encountered

1. **32B model server hung twice** — During W2_32b testing, the edit_file infinite loop caused the server to stop responding for 13+ min; required `launchctl kickstart -k gui/.../com.ohama.qwen32b` twice. Root cause was the empty old_string bug.

2. **awk measurement script in plan is broken** — The plan's awk measurement one-liner counts past the closing `"""` when it's inline (not on its own line). Actual baseline was 1689 chars, not 2660. Switched to Python regex measurement throughout.

3. **Prompt annotation effects are model-specific** — The `(dir)` annotation on grep_search worked for 32B (used directory path) but not 72B (used file path). The 72B model's strong prior of using the file mentioned in the user prompt overrides schema hints. Fix required changing the tool behavior instead.

4. **LLM nondeterminism threshold** — T6_32b was non-deterministic at the 1689-char baseline (PASS once, FAIL 2 times in recent history). The new 783-char prompt + grep_search guidance makes T6 deterministic (3 steps, every run tested).

## Hang Retries

2 × `launchctl kickstart -k gui/$(id -u)/com.ohama.qwen32b` (both caused by the empty old_string infinite loop)

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Wave 3 (Plan 11-03) is unlocked: B2 recovery validation — run `--b2` with 783-char prompt, verify diagnosis improves, update baseline.json B2 entries
- The grep_search file-path fix may affect existing tests if any test relies on file-path grep_search returning an error — verified 243/1/0 (no regressions)
- Phase 11-03 should also update `bench/baseline.json` T6_32b/72B notes to reflect the new 3-step pattern with grep_search

---
*Phase: 11-system-prompt-shrink*
*Completed: 2026-04-26*
