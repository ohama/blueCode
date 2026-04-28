---
phase: 22-plan04-ceiling-raise
plan: 02
subsystem: cli-prompt
tags: [system-prompt, rendering, agent-loop, bench-gate, usage-guidance]
status: complete
completed: 2026-04-28
duration: ~10 min

dependency_graph:
  requires:
    - 22-01 (MaxPlanSteps=10, MaxLoops=10 in place)
  provides:
    - planSystemPromptSuffix updated to "1-10 steps" + usage guidance clause
    - AgentLoop.fs [PLAN INVALID] retry says "max 10 steps"
    - Rendering.fs MaxLoopsExceeded says "10 steps"
    - RenderingTests.fs assertion updated to "10 steps"
  affects:
    - 22-03 (bench fixture additions, if any)
    - 22-04 (re-evaluation; new prompt with usage guidance now in production)

tech_stack:
  added: []
  patterns:
    - Usage guidance clause in system prompt as regression mitigation for T6 step-count stability

key_files:
  created: []
  modified:
    - src/BlueCode.Cli/CompositionRoot.fs
    - src/BlueCode.Core/AgentLoop.fs
    - src/BlueCode.Cli/Rendering.fs
    - tests/BlueCode.Tests/RenderingTests.fs

decisions:
  - "Usage guidance wording (first variant) held without iteration: T6 used 5/5 steps (within step_count_max=5); gate passed without prompt strengthening"
  - "planSystemPromptSuffix char count: 695 chars (target ≤ 900; well under budget)"

metrics:
  test_count_before: 284
  test_count_after: 284
  tests_failed: 0
  tests_errored: 0
  tests_ignored: 1
  bench_gate: "7/7 PASS"
  compile_errors: 0
  compile_warnings: 0
---

# Phase 22 Plan 02: System Prompt and Adapter Strings Update Summary

**One-liner:** Updated all "5 steps" user/LLM-visible strings to "10 steps" with usage guidance clause in planSystemPromptSuffix; bench gate 7/7 PASS; T6 at 5/5 steps (no prompt iteration needed).

## What Was Done

Three targeted string changes across the Cli and Core layers, plus one test assertion update. The key change is the usage guidance clause added to `planSystemPromptSuffix` to prevent the model from inflating step counts when the ceiling rises from 5 to 10.

## Final planSystemPromptSuffix (exact text)

```
OVERRIDE — PLAN MODE ACTIVE. Do NOT use read_file/write_file/list_dir/run_shell/edit_file/glob_search/grep_search/final actions.
Your ONLY valid response is action="plan". Respond with EXACTLY this JSON shape:
{"thought": "<reasoning>", "action": "plan", "input": {"steps": [{"tool": "<tool>", "input": {}, "rationale": "<why>"}], "rationale": "<overall why>"}}
where each "tool" is one of: read_file|write_file|list_dir|run_shell|edit_file|glob_search|grep_search.
Constraints: 1-10 steps. Use the minimum steps needed; reserve the full budget only for tasks requiring reads across multiple files before editing. No two adjacent steps may be identical. Do NOT execute — user will approve first.
```

**Char count: 695** (target ≤ 900; 205 chars of headroom remaining)

## Strings Changed

| File | Old | New |
|------|-----|-----|
| `src/BlueCode.Cli/CompositionRoot.fs` planSystemPromptSuffix | `1-5 steps. No two adjacent...` | `1-10 steps. Use the minimum steps needed; reserve the full budget only for tasks requiring reads across multiple files before editing. No two adjacent...` |
| `src/BlueCode.Core/AgentLoop.fs:502` | `max 5 steps` | `max 10 steps` |
| `src/BlueCode.Cli/Rendering.fs:114` | `5 steps with no final answer` | `10 steps with no final answer` |
| `tests/BlueCode.Tests/RenderingTests.fs:73` | `"5 steps"` | `"10 steps"` |

## Compile Status

- `dotnet build src/BlueCode.Core/BlueCode.Core.fsproj --no-restore`: 0 errors, 0 warnings
- `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj --no-restore`: 0 errors, 0 warnings
- Core purity: 0 Serilog/Spectre/Argu/HttpClient references in AgentLoop.fs
- `defaultSystemPrompt`: untouched (diff shows only suffix line changed)
- `Role = User` invariant: preserved (only string content changed, not Role field)

## Test Results

```
EXPECTO! 284 tests run in 00:00:30.7 for all
- 284 passed, 1 ignored, 0 failed, 0 errored. Success!
```

Test count stable at 284 (RenderingTests assertion content updated in-place, no count change).

## Bench Gate Result

```
===== GATE: compare to baseline =====
  PASS T6_122b    steps=5/5 exit=0
  PASS W1_122b    steps=3/3 exit=0
  PASS W2_122b    steps=3/3 exit=0
  PASS T1_122b    steps=1/3 exit=0
  PASS T5_122b    steps=3/4 exit=0
  PASS B2_122b    steps=2/3 exit=0
  PASS MT_122b    steps=2/4 exit=0
===== GATE PASS (7/7) =====
gate_exit=0
```

**bench/baseline.json**: byte-for-byte unchanged.

## T6 Step Count Analysis

T6 is the only fixture with step_count_max=5 (1-step headroom from baseline). After the prompt update:

- **T6 observed: 5/5 steps** — at the baseline_max; gate PASSED
- **No prompt iteration needed** — first variant of usage guidance held
- T6 uses its full allocation but does not exceed it; regression risk monitored

The usage guidance clause ("Use the minimum steps needed; reserve the full budget only for tasks requiring reads across multiple files before editing") is the mitigation. T6's task is a single-file question that the model naturally solves in ~5 steps regardless of ceiling.

## T6 Iteration History

None. First variant passed without regression.

## Commits

| Hash | Type | Description |
|------|------|-------------|
| `0d99cd7` | `feat(22-02)` | update 5→10 step references in prompt, retry message, and error string |
| `2c9e24c` | `test(22-02)` | update RenderingTests MaxLoopsExceeded assertion to 10 steps |

## Deviations from Plan

**Plan specified 285 tests; actual is 284.** The plan file said "Test count stays at 285" but the current baseline (confirmed in STATE.md and 22-01 SUMMARY) is 284 (284 passed, 1 ignored). This is not a regression — the plan's "285" was a stale count from an earlier draft. Actual test count is unchanged from 22-01 completion.

**No other deviations.** Plan executed exactly as written.

## Architectural Invariants Confirmed

- [x] Core purity: no Serilog/Spectre/Argu/HttpClient added to AgentLoop.fs
- [x] `task {}` only in Core: string literal change touches no CE
- [x] `Role = User` invariant: [PLAN INVALID] message Role field unchanged
- [x] `defaultSystemPrompt` untouched: diff shows only one suffix line changed
- [x] `bench/baseline.json`: byte-for-byte unchanged
- [x] `planSystemPromptSuffix` char count: 695 ≤ 900
- [x] Test discovery: RouterTests.fs rootTests list unchanged
