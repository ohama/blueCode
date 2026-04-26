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
| `--b2` | 2 | ~30 s | B2 divide-by-zero diagnose only — useful for prompt-shrink hypothesis testing (originally Phase 11 PERF-03's iteration tool). |
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

## Auto-Reset of Write Fixtures

`bench/run.sh` installs a bash `trap` on `EXIT` (set near the top of the script,
right after `set -u`) that runs `git checkout -- bench/fixtures/bug_lastchar.fs
bench/fixtures/bug_average.fs` on every exit path — success, failure, or Ctrl-C.
This means **`git status` is always clean for those two fixtures after any
`bench/run.sh` invocation**, regardless of what the LLM wrote during the run.

The trap fires for every mode (`--gate`, `--regression`, `--canary`, `--all`,
`--b2`, `--help`). For modes that do not mutate the W1/W2 fixtures (`--canary`,
`--b2`, `--help`), the `git checkout` is a harmless no-op. The trap deliberately
does NOT touch `bench/fixtures/bug_divide_zero.fs` — that fixture is read-only
by every test that uses it (B2 diagnose), so resetting it would be a wasted
syscall at best and a footgun at worst.

The existing in-line heredoc-restore blocks (`cat <<'EOF' > bench/fixtures/...`
before each W1/W2 invocation in `gate()` and `phase_write()`) are preserved as
defense-in-depth: heredoc handles between-invocation reset within a single run;
the trap handles exit-time cleanup. Either alone is sufficient; together they
guarantee a clean working tree under every reasonable failure mode.

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

The B2 recovery in Phase 11 PERF-03 is the canonical worked example. The procedure
generalizes to any future intentional behavior change:

1. Apply the fix (e.g., source-code or prompt change).
2. Run the gate: `bash bench/run.sh --gate`. If the affected entry is currently marked
   `regression: true` in `baseline.json`, the gate treats it as PASSing regardless of
   answer quality (the gate cannot detect quality from logs alone — only step-count and
   exit-code drift). Verify recovery manually: open the most recent
   `bench/runs/gate-<timestamp>/gate_<test>.log` and read the model's diagnosis text.
3. Edit `bench/baseline.json`: change the entry's `pass` to `true`, remove the
   `regression` field, update the `note` to record the recovery (cite the verbatim
   model thought when relevant — see the v1.3 B2 entries for the canonical format).
4. Re-run the gate: `bash bench/run.sh --gate` should now exit 0 with `GATE PASS (8/8)`
   and the recovered entry validating against real step counts.
5. Commit the baseline update alongside the fix commit.

**v1.3 worked example:** Phase 11 PERF-03 flipped both `B2_32b` and `B2_72b` from
`pass: false, regression: true` to `pass: true` after the 54% prompt shrink (1689 →
783 chars) recovered correct empty-list diagnosis on both models. See
`documentation/benchmark-32b-vs-72b.md` Part 4 §21.3 for the diff and rationale.

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

(Step counts above are post-v1.3 actuals; T6 went from 4-5 steps to deterministic 3
after the prompt shrink moved the 32B/72B toward `grep_search → read_file → final` as
the canonical pattern.)

A failing gate prints `FAIL <key> ... — <reason>` lines, and ends with
`GATE FAIL (N/8 regressed)` with exit code 1.

## Known Regressions (Baseline State)

**Current state (post-v1.3 close, 2026-04-26):** zero entries marked `regression: true`
in `bench/baseline.json`. All 8 baseline entries (T6 × 32B/72B, W1/W2 × 32B, T1/T5
canaries, B2 × 32B/72B) validate against real step counts and pass states.

**Historical:** `B2_32b` and `B2_72b` were recorded as `pass: false, regression: true`
from Phase 10 close through Phase 11 mid-execution. Both models misdiagnosed the
divide-by-zero fixture as "integer truncation" instead of "empty list → DivideByZeroException"
— the v1.2 audit's prompt-length attention-shift hypothesis materialized. Phase 11 PERF-03
recovered correct diagnosis on both models after the 54% prompt shrink (1689 → 783 chars);
baseline entries were flipped to `pass: true` in commit `04b6f92`. The audit hypothesis
was confirmed: prompt length was the single cause.

If a future regression is identified, mark its baseline entry with `regression: true`
and follow the "How to Update Baseline" procedure above to track recovery.

## See Also

- [`bench/run.sh`](../bench/run.sh) — the harness itself
- [`bench/baseline.json`](../bench/baseline.json) — recorded ground truth
- [`documentation/v1.2-bench-followup.md`](v1.2-bench-followup.md) — design rationale
- [`.planning/milestones/v1.2-phases/09.1-bench-follow-up-fixes/09.1-VALIDATION.md`](../.planning/milestones/v1.2-phases/09.1-bench-follow-up-fixes/09.1-VALIDATION.md) — step-count evidence trail
- [`CLAUDE.md`](../CLAUDE.md) `## Bench` section — quick reference for Claude sessions
