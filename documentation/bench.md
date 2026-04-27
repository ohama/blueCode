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

All invocations use `--model 122b` (single-model canonical, Phase 19).

| Flag | Invocations | Wall-clock | Purpose |
|------|-------------|------------|---------|
| `--gate` | 7 | ~3-4 min | Regression gate (CI/pre-commit). Exits non-zero on regression. Labels: T6/W1/W2/T1/T5/B2/MT all _122b. |
| `--canary` | 4 | ~1.5 min | Quick smoke for ad-hoc development. |
| `--regression` | 7 | ~6 min | Part 1 reproducibility (T1–T7 × 122B). |
| `--b2` | 1 | ~30 s | B2 divide-by-zero diagnose only — useful for prompt-shrink hypothesis testing. |
| `--all` | 20+ | ~25 min | Full re-bench equivalent (122B only). |
| `--help` | 0 | — | Print usage. |

## Fixture Naming Convention

Fixtures live in `bench/fixtures/` and follow the pattern `bug_<short_symptom>.fs` for bug
fixtures, or `mt_<purpose>.txt` for multi-turn text prompts:

- `bug_lastchar.fs` — off-by-one indexer (`s.[s.Length]`); used by W1 write task.
- `bug_average.fs` — divide-by-zero in `average`; used by W2 write task (agent adds `averageSafe`).
- `bug_divide_zero.fs` — purpose-built diagnose-only fixture for B2 (kept separate from
  `bug_average.fs` so W2 and B2 don't share state).
- `mt_followup.txt` — turn-2 prompt for MT_122b multi-turn fixture (Phase 16-03); plain text
  referencing prior session context ("What was the file I just listed? Just give me the file count.").

Each fixture is a single F# module with one bug. The bug-trigger should be obvious from
a docstring or comment in the fixture itself, so the test is reproducible without
context. Fixtures are committed to git in their broken baseline state. The W1/W2 runs
mutate them in-place, then `bench/run.sh` restores via `cat <<'EOF'` heredoc before
each run.

### MT_122b — Multi-turn persistence fixture (Phase 16-03)

**Purpose:** Validates PERSIST-01 (cross-turn session memory via `--resume <id>`) at the bench
regression layer. Single-model 122B (Phase 19 canonical).

**Shape:**

1. **Turn 1** — `dotnet run --project src/BlueCode.Cli -- --verbose --model 122b "List the files in bench/fixtures and tell me the count."` Captures session id from stderr (`Session: <id>` line, Phase 15-02 deliverable).
2. **Turn 2** — `dotnet run --project src/BlueCode.Cli -- --verbose --model 122b --resume <id> "$(cat bench/fixtures/mt_followup.txt)"`. Follow-up prompt deliberately references prior context ("What was the file I just listed? Just give me the file count.") so it cannot be answered correctly without the resumed session.

**Both turns must exit 0** for MT_122b to PASS. The bench harness (`mt()` in `bench/run.sh`)
records `combined_exit = max(turn1_exit, turn2_exit)` to `${label}.meta` for the gate's
exit-code check.

**Gate metric:** Step count from turn 1 (parser uses `head -1` on `[INF] Session ok: N steps`
markers; turn 2's step count is documented in baseline `note` field but not gate-asserted).
This matches the existing single-turn parser semantics; no parser changes were made in 16-03.

**Baseline:** `step_count: 2` (typical: `list_dir` + `final` = 2 steps), `step_count_max: 4`,
`elapsed_median_s: 7` (full 2-turn cycle including session save/load round-trip, observed
empirically in Phase 16-03 smoke run on 122B).

**Failure modes:**

- **Missing session id from turn-1 stderr:** Phase 15-02 deliverable broken — `mt()` aborts with exit 99 before invoking turn 2.
- **Turn 2 exits non-zero:** Session not persisted, or `--resume` could not load it. PERSIST-01 regression.
- **Turn-1 step count exceeds 4:** Routing pattern shifted (LLM chose grep instead of list_dir, or added an extra read_file). Re-tune `step_count_max` once if observed and stable across 3+ runs.

**What is NOT tested by MT_122b:**

- Plan-mode interactive flow (deferred — see "Plan-mode bench" section below).
- Session corruption / `SessionCorrupt` recovery (covered by SessionStoreTests at unit level).
- 35B-targeted multi-turn (35B is rollback-only post-Phase-19; not bench-targeted).

**Why a single fixture, not multiple:** The original Phase 16 plan outlined both `MT_32b` and
`MT_72b` entries for a dual-model configuration. Phase 19 retired Qwen 2.5 entirely; 122B alone
is canonical. A single `MT_122b` fixture covers the persistence regression surface. Adding
`MT_35b` would test the rollback path against a service not loaded by default — out of scope
for the gate.

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

## Plan-mode bench fixture — DEFERRED to v2.1+

**Status:** Out of scope for v2.0 / Phase 16. Will be revisited when REPL plan-mode
(`/plan` slash command) and full multi-turn plan-mode interaction land in v2.1.

**Why deferred:**

1. **Keystroke-driven UX is intractable for an autonomous regression gate.** PlanGate's
   `[a]ccept / [r]eject / [e]dit / [q]uit` prompt requires `Console.ReadKey` interaction.
   Scripting this in a CI-friendly way demands either:
   - A `--plan-script <a|r|e|q>` non-interactive flag (CLI-surface change, not in 16-02 scope)
   - PTY emulation in shell (brittle; macOS launchd + bash + `expect` is fragile)
   - In-process IKeyReader injection (collapses the bench to unit testing — already covered by PlanGateTests in 16-02)

2. **Coverage substitute exists.** Phase 16-02 PlanGateTests covers all 4 keystroke paths
   plus unknown-key re-prompt. Phase 16-01 PlanParseTests covers parse + validation +
   retry exhaustion. Phase 16-03 AgentLoopTests covers runPlanTurn end-to-end with mocked
   LLM. The plan-mode pipeline is regression-protected at unit + integration boundaries
   without a bench fixture.

3. **Live smoke is documented.** Phase 16-02 SUMMARY captures live-smoke transcripts
   against 122B for SC1 (plan table), SC2 (a/r/e/q dispatch), SC4 (--plan --resume).
   These are point-in-time evidence; the bench gate's role is automated regression
   detection, which the unit/integration tests already provide.

4. **REPL plan-mode arrives in v2.1.** Once `/plan` slash command + multi-turn plan-mode
   exist, a plan-mode bench fixture becomes feasible: drive plan -> accept via stdin
   piping rather than keystroke interception. v2.1 planning revisits this.

**What v2.1 should add (placeholder for future planner):**

- A `PLAN_122b` fixture invoking `--plan` with stdin-piped accept (`yes a |` or similar)
- Baseline assertion: plan validates (no PlanInvalid surfaced), execution produces same
  step count as the equivalent non-plan-mode invocation
- Decision: should plan-mode and non-plan-mode share a baseline (plan should not change
  step count) or have distinct baselines (plan-mode may add 1 step for the plan-emission turn)?

## Hang Contingency for `mlx_lm.server` 122B

**Symptom:** A 122B run shows no console output progress for >90 s. The blueCode HTTP
client itself times out at 180 s, but the gate will appear "hung" earlier because the
spinner stops moving.

**Recovery:**

```bash
launchctl kickstart -k gui/$(id -u)/com.ohama.qwen122b
# wait ~60-90 s for weights to reload (~45 GB for 122B)
# manually re-run the failed test
bash bench/run.sh --gate    # or just the failed sub-test invocation
```

**Frequency:** ~1 occurrence in 60 v1.2 bench runs. Low but non-zero.

**Last resort (if `kickstart -k` itself hangs):**

```bash
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen122b.plist
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen122b.plist
```

See [`documentation/local-llm-services.md`](local-llm-services.md) §5 for the full
launchd protocol.

**Why isn't this automated?** The threshold (90 s) is close to the longest observed
clean run (T6 × 72B at 75 s). Auto-kickstart risks false-positive interruptions on
slow-but-valid runs. Manual contingency keeps the gate's behavior predictable. A v1.4+
plan can revisit this trade-off.

## Interpreting Gate Output

A passing gate looks like (Phase 16-03, single-model 122B, 7/7 format):

```
Pre-condition OK: port 8001 (122B) responsive.
===== GATE: regression subset (7 invocations) =====
... per-test run logs ...
===== gate_MT_122b (multi-turn, model=122b) =====
  turn1: exit=0 session=<id>
  turn2: exit=0  combined exit=0 elapsed=7s
===== GATE: compare to baseline =====
  PASS T6_122b    steps=4/5 exit=0
  PASS W1_122b    steps=3/3 exit=0
  PASS W2_122b    steps=3/3 exit=0
  PASS T1_122b    steps=1/3 exit=0
  PASS T5_122b    steps=3/4 exit=0
  PASS B2_122b    steps=2/3 exit=0
  PASS MT_122b    steps=2/4 exit=0
===== GATE PASS (7/7) =====
```

A failing gate prints `FAIL <key> ... — <reason>` lines, and ends with
`GATE FAIL (N/7 regressed)` with exit code 1.

## Known Regressions (Baseline State)

**Current state (post-Phase-16-03, 2026-04-27):** zero entries marked `regression: true`
in `bench/baseline.json`. All 7 baseline entries (T6/W1/W2/T1/T5/B2/MT × 122B) validate
against real step counts and pass states.

**Historical:** `B2_32b` and `B2_72b` were recorded as `pass: false, regression: true`
from Phase 10 close through Phase 11 mid-execution (historical — Qwen 2.5 retired in
Phase 19; current B2_122b preserves diagnose accuracy). Both models misdiagnosed the
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
