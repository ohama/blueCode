# Bench Harness (`bench/run.sh`)

`bench/run.sh` is the canonical regression harness for blueCode. It replaced v1.2's
ephemeral `/tmp/bench-v1.2/run.sh` in Phase 10 and is now repo-tracked. Logs land in
`bench/runs/<timestamp>/` (gitignored). The recorded ground truth lives in
`bench/baseline.json`.

## Overview

The harness wraps `dotnet run --project src/BlueCode.Cli -- --verbose --model <m> "<prompt>"`
in a `run()` helper that captures stdout/stderr to `<label>.log`, records exit code +
elapsed time to `<label>.meta`, and appends a one-liner per invocation to `timeline.txt`.
The `--gate` mode parses these logs via `grep` + `jq`, diffs against `bench/baseline.json`,
and exits non-zero on any regression.

Design rationale: see [`documentation/v1.2-bench-followup.md`](v1.2-bench-followup.md) for
the v1.2 audit findings that motivated each gate test, and
[`.planning/milestones/v1.2-phases/09.1-bench-follow-up-fixes/09.1-VALIDATION.md`](../.planning/milestones/v1.2-phases/09.1-bench-follow-up-fixes/09.1-VALIDATION.md)
for the post-09.1-05 step counts that the baseline records.

## Quick Start

```bash
# Build first (gate runs against the dotnet build artifacts)
dotnet build src/BlueCode.Cli

# Run the gate — ~2 min wall-clock; exits 0 on PASS, non-zero on regression
bash bench/run.sh --gate
```

If the gate fails, inspect the per-test diff lines in the console output and the
`gate_<key>.log` files under `bench/runs/gate-<timestamp>/`.

## Mode Flags

| Flag | Invocations | Wall-clock | Purpose |
|------|-------------|------------|---------|
| `--gate` | 8 | ~2 min | Regression gate (CI/pre-commit). Exits non-zero on regression. |
| `--canary` | 4 | ~1.5 min | Quick smoke for ad-hoc development. |
| `--regression` | 14 | ~6 min | Part 1 reproducibility (T1–T7 × both models). |
| `--b2` | 2 | ~30 s | B2 divide-by-zero diagnose only — Phase 11 PERF-03 target. |
| `--all` | 30+ | ~25 min | Full re-bench equivalent. |
| `--help` | 0 | — | Print usage. |

## Fixture Naming Convention

Fixtures live in `bench/fixtures/` and follow the pattern `bug_<short_symptom>.fs`:

- `bug_lastchar.fs` — off-by-one indexer (`s.[s.Length]`); used by W1 write task.
- `bug_average.fs` — divide-by-zero in `average`; used by W2 write task (agent adds `averageSafe`).
- `bug_divide_zero.fs` — purpose-built diagnose-only fixture for B2 (kept separate from
  `bug_average.fs` so W2 and B2 don't share state).

Each fixture is a single F# module with one bug. The bug-trigger should be obvious from
a docstring or comment in the fixture itself, so the test is reproducible without
context. Fixtures are committed to git in their broken baseline state. The W1/W2 runs
mutate them in-place, then `bench/run.sh` restores via `cat <<'EOF'` heredoc before
each run.

## Prompt Design Guidance

**General rule:** Do NOT name a specific tool in fixture prompts. Phrases like "using
write_file" or "using edit_file" expose a user-prompt vs system-prompt priority issue
(discovered in Phase 09.1-04: user-level instructions can override system-prompt
directives, leading the agent to ignore strategy hints). The fix in 09.1-05 was a
code-level loop injection that re-asserts the directive after every tool call.

**Exception:** The W1 prompt
(`"Read bench/fixtures/bug_lastchar.fs and fix the bug. Save the corrected version using write_file."`)
deliberately retains "using write_file" — this is intentional. W1 validates that the
09.1-05 loop injection holds even when the user explicitly names the tool the directive
forbids. **Do not "fix" W1's prompt by removing the tool name.** That would silently
remove the test for the regression 09.1-05 was built to prevent.

**For new fixtures (B2 and beyond):** Phrase prompts in terms of the task, not the tool.
Example: `"Read bench/fixtures/bug_divide_zero.fs and identify the bug. Be specific
about what input triggers it."` — no tool naming.

## How to Add a New Test

1. Create the fixture file: `bench/fixtures/bug_<symptom>.fs`. Include a docstring
   explaining what input triggers the bug.
2. Decide if the test is a **diagnose** (the agent reads + explains the bug) or a
   **write** (the agent fixes the bug). Diagnose tests don't need fixture restore;
   write tests do.
3. Add an invocation to the appropriate mode function in `bench/run.sh`. For diagnose
   tests, add to `b2_mode()` or a new mode function. For write tests, add to `all_mode()`'s
   phaseC-equivalent block, including the heredoc fixture restore before the `run` call.
4. Run the test once: `bash bench/run.sh --canary` (or whichever mode includes it) and
   inspect the resulting log under `bench/runs/<ts>/`. Record the observed step count.
5. If this is a gate-tier test, add an entry to `bench/baseline.json` with the observed
   `step_count`, a `step_count_max` ceiling (typically observed + 1 for variance),
   `pass: true|false`, and a `note` field describing the test's purpose.

## How to Update Baseline After an Intentional Fix

Phase 11 PERF-03 will be the first such update: when the prompt shrink restores B2 to
correct diagnosis, the B2 entries in `bench/baseline.json` need updating from
`pass: false, regression: true` to `pass: true`. The procedure:

1. Apply the fix (Phase 11 commits in `src/BlueCode.Cli/CompositionRoot.fs`).
2. Run the gate: `bash bench/run.sh --gate`. It will exit 0 because B2 is still marked
   `regression: true` in `baseline.json` (the gate treats known regressions as PASSing
   — it cannot detect answer-quality recovery from logs alone, only step-count and
   exit-code drift). To verify the recovery: open the most recent
   `bench/runs/gate-<timestamp>/gate_B2_32b.log` and look for the model's diagnosis
   text. If the model now says "empty list" or similar instead of "integer truncation",
   PERF-03 has succeeded — proceed to step 3.
3. Edit `bench/baseline.json`: change `B2_32b.pass` to `true`, remove the `regression`
   field, update the `note` to record the recovery (e.g., "Recovered post-PERF-03;
   prompt shrink to ≤800 chars"). Same for `B2_72b` if both recovered.
4. Re-run the gate: `bash bench/run.sh --gate` should now exit 0 with `GATE PASS (8/8)`.
5. Commit the baseline update alongside the fix commit.

## Hang Contingency for `mlx_lm.server` 32B

**Symptom:** A 32B run shows no console output progress for >90 s. The blueCode HTTP
client itself times out at 180 s, but the gate will appear "hung" earlier because the
spinner stops moving.

**Recovery:**

```bash
launchctl kickstart -k gui/$(id -u)/com.ohama.qwen32b
# wait ~30 s for weights to reload (~17 GB)
# manually re-run the failed test
bash bench/run.sh --gate    # or just the failed sub-test invocation
```

**Frequency:** ~1 occurrence in 60 v1.2 bench runs. Low but non-zero.

**Last resort (if `kickstart -k` itself hangs):**

```bash
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen32b.plist
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen32b.plist
```

See [`documentation/local-llm-services.md`](local-llm-services.md) §5 for the full
launchd protocol.

**Why isn't this automated?** The threshold (90 s) is close to the longest observed
clean run (T6 × 72B at 75 s). Auto-kickstart risks false-positive interruptions on
slow-but-valid runs. Manual contingency keeps the gate's behavior predictable. A v1.4+
plan can revisit this trade-off.

## Interpreting Gate Output

A passing gate looks like:

```
===== GATE: regression subset (8 invocations) =====
... per-test run logs ...
===== GATE: compare to baseline =====
  PASS T6_32b     steps=4/5 exit=0
  PASS T6_72b     steps=5/6 exit=0
  PASS W1_32b     steps=3/3 exit=0
  PASS W2_32b     steps=3/3 exit=0
  PASS T1_32b     steps=1/3 exit=0
  PASS T5_72b     steps=3/4 exit=0
  PASS B2_32b     steps=2/3 exit=0
  PASS B2_72b     steps=2/3 exit=0
===== GATE PASS (8/8) =====
```

A failing gate prints `FAIL <key> ... — <reason>` lines, and ends with
`GATE FAIL (N/8 regressed)` with exit code 1.

## Known Regressions (Baseline State)

`B2_32b` and `B2_72b` are recorded as `pass: false, regression: true` in
`bench/baseline.json`. Both models currently misdiagnose the divide-by-zero fixture
as "integer truncation" instead of "empty list → DivideByZeroException." This is the
v1.2 audit's prompt-length attention-shift hypothesis materialized.

Phase 11 PERF-03 targets recovery via the system-prompt shrink. Until then, the gate
treats the regressed state as the baseline and PASSes. PERF-03's recovery is manual:
the operator inspects the most recent `bench/runs/gate-<timestamp>/gate_B2_32b.log`
for correct diagnosis text ("empty list" vs the regressed "integer truncation"), then
updates `bench/baseline.json` (set `pass: true`, remove the `regression` field). The
gate cannot detect answer-quality recovery from logs alone — only step-count and
exit-code drift.

## See Also

- [`bench/run.sh`](../bench/run.sh) — the harness itself
- [`bench/baseline.json`](../bench/baseline.json) — recorded ground truth
- [`documentation/v1.2-bench-followup.md`](v1.2-bench-followup.md) — design rationale
- [`.planning/milestones/v1.2-phases/09.1-bench-follow-up-fixes/09.1-VALIDATION.md`](../.planning/milestones/v1.2-phases/09.1-bench-follow-up-fixes/09.1-VALIDATION.md) — step-count evidence trail
- [`CLAUDE.md`](../CLAUDE.md) `## Bench` section — quick reference for Claude sessions
