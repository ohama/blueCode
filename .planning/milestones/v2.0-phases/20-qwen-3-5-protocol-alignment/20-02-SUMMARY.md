---
phase: 20
plan: 20-02
name: extract-content-fallback
subsystem: llm-adapter
tags: [fsharp, json, reasoning_content, fallback, testability, qwen35]

dependency-graph:
  requires: [20-01]
  provides: [extractContentFromJson public helper, reasoning_content fallback, 4 new tests]
  affects: [20-03]

tech-stack:
  added: []
  patterns: [pure-helper extraction, string-option fallback ladder, testability via public module helper]

key-files:
  created: []
  modified:
    - src/BlueCode.Cli/Adapters/QwenHttpClient.fs
    - tests/BlueCode.Tests/LlmPipelineTests.fs
    - documentation/qwen35-install.md

decisions:
  - extractContentFromJson public (not private) mirrors tryParseModelId / tryParseMaxModelLen precedent
  - reasoning_content fallback ladder: content (non-empty string) → reasoning_content (non-empty string) → None
  - JsonValueKind.String guard rejects JSON null literals and non-string types
  - test file: existing LlmPipelineTests.fs (no .fsproj / rootTests dual-registration change required)
  - 4 test cases (3 required + 1 optional null-content guard)

metrics:
  duration: ~7 minutes
  completed: 2026-04-27
---

# Phase 20 Plan 02: extract-content-fallback Summary

**One-liner:** Public `extractContentFromJson` helper with `reasoning_content` fallback; 4 new tests; qwen35-install.md §5.3 + Appendix A rows marked RESOLVED.

## What Was Built

Phase 20-02 carved a public pure helper `extractContentFromJson : string -> string option` out of `QwenHttpClient.extractContent` (analogous to the `tryParseModelId` / `tryParseMaxModelLen` precedent in the same file) and added a `reasoning_content` fallback rung. When `choices[0].message.content` is null, missing, or empty, the helper falls back to `choices[0].message.reasoning_content` before returning `None`. The `extractContent` private wrapper maps `None → Error(LlmUnreachable(url, "malformed response: no content or reasoning_content"))`. Four new test cases in `tests/BlueCode.Tests/LlmPipelineTests.fs` cover: (a) content present → uses content; (b) content empty + reasoning_content present → uses reasoning_content; (c) both empty → None; (d) JSON null literal content + reasoning_content present → uses reasoning_content (validates the `ValueKind = JsonValueKind.String` guard). Tests were added to the existing file to sidestep the dual-registration pitfall (`.fsproj` + `rootTests` — flagged in CLAUDE.md, has bitten four executors). The `qwen35-install.md` §5.3 response table and Appendix A `content` 빈 문자열 rows were marked RESOLVED Phase 20-02.

## Commits

| # | Commit | Message |
|---|--------|---------|
| 1 | | `refactor(20-02): extract public extractContentFromJson helper for testability` |
| 2 | | `feat(20-02): add reasoning_content fallback to extractContentFromJson` |
| 3 | | `test(20-02): cover content / reasoning_content fallback ladder` |
| 4 | | `docs(20-02): mark reasoning_content gotcha RESOLVED in qwen35-install.md` |

## Test Count Delta

262/1/0 → 266/1/0 (net +4: 3 required + 1 optional null-content guard)

## Bench Gate

`bench/run.sh --gate` exit 0 — **GATE PASS (6/6)**

```
PASS T6_122b    steps=5/5 exit=0
PASS W1_122b    steps=3/3 exit=0
PASS W2_122b    steps=3/3 exit=0
PASS T1_122b    steps=1/3 exit=0
PASS T5_122b    steps=3/4 exit=0
PASS B2_122b    steps=2/3 exit=0
```

## Decisions Made

See frontmatter `decisions` block above. Key calls:

- **extractContent refactor to public helper** — `extractContentFromJson` is public (not private) for direct unit test access, mirroring `tryParseModelId` pattern. `extractContent` stays private and delegates.
- **reasoning_content fallback semantics** — `pickStringField` inner function used for both fields; `JsonValueKind.String` guard ensures JSON null literals and non-string types are correctly skipped.
- **test count delta** — 262 → 266 (+4); plan minimum was ≥263; target was ≥265; 266 delivered.
- **qwen35-install.md row 6 RESOLVED** — Appendix A `content` 빈 문자열 row (line 978) and §5.3 response table row both marked `RESOLVED Phase 20-02`.

## Deviations from Plan

None — plan executed exactly as written, with the optional 4th test case (null content literal) included per plan's recommendation.

## Files Modified

- `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` — Task 1 (refactor) + Task 2 (fallback)
- `tests/BlueCode.Tests/LlmPipelineTests.fs` — Task 3 (4 new test cases)
- `documentation/qwen35-install.md` — Task 4 (RESOLVED markers)

## Next

20-03: 122B mid-conversation `Role = System` probe + conditional restore (Phase 17-02 fix may be unnecessary for 122B alone).
