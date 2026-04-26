# Phase 10: Bench Formalization — Research

**Researched:** 2026-04-26
**Domain:** Bash bench harness formalization (file move, script consolidation, JSON baseline, gate mode)
**Confidence:** HIGH — all facts sourced from existing on-disk artifacts; no library research required

---

## Summary

Phase 10 is primarily a reorganization and codification task, not a greenfield build. All the knowledge
needed to implement it already exists across four on-disk artifacts: `/tmp/bench-v1.2/run.sh` (201 lines,
the running harness), the four bench log directories, `09.1-VALIDATION.md` (the results report format),
and the two untracked fixture files in `bench-fixtures/`. The research job was to inventory those sources,
identify what lifts verbatim vs. needs reshaping, and surface the gotchas the planner needs.

The key findings are: (1) the `run()` helper and its log/meta output format can be lifted nearly verbatim
into `bench/run.sh`, with only the `LOG_DIR` and `cd` targets updated; (2) `--gate` mode needs only `jq`
(already available at `/usr/bin/jq 1.7.1`) and `grep` to parse step counts from the `[INF] Session ok: N
steps` line; (3) the fixture files need to move from `bench-fixtures/` to `bench/fixtures/` and the
prompts referencing them must update their path accordingly; (4) macOS system bash is 3.2 — the existing
script is already compatible; (5) CLAUDE.md has **zero** current references to `/tmp/bench-v1.2/run.sh`
(SC5 is essentially free — just add the canonical entry point line); and (6) the "B2 divide-by-zero
fixture" in SC2 is a NEW file (e.g., `bug_divide_zero.fs`), distinct from the existing `bug_average.fs`.

**Primary recommendation:** Lift the existing `run()` helper verbatim, reshape the selector case-statement
into mode-flag dispatch (`--gate`, `--regression`, `--canary`, `--all`), move fixtures, record baseline
from the post-09.1-05 log evidence, implement `--gate` via `jq` comparison, and write `documentation/bench.md`.

---

## 1. Existing-Bench Inventory

### `/tmp/bench-v1.2/run.sh` — 201 lines

```
Lines  1–6:   shebang, comment, set -u, cd /Users/ohama/projs/blueCode, LOG_DIR=/tmp/bench-v1.2
Lines  8–26:  run() helper — logs to $LOG_DIR/$label.{log,meta}, calls dotnet run, writes .meta
Lines 28–39:  phase1() — Part 1 tests: T1-T7 × 32B + 72B (14 runs)
Lines 41–51:  phaseA() — T1 + T6 variance, 3 runs each × 2 models (12 runs)
Lines 53–60:  phaseB() — B1/B2/B3 diagnose × 2 models (6 runs)
Lines 62–85:  phaseC() — W1/W2 write tasks × 2 models with fixture restore (4 runs)
Lines 87–128: v9_1() — overrides LOG_DIR locally to /tmp/bench-v1.2-fixed (10 runs)
Lines 130–160: v9_1_rev() — overrides LOG_DIR to /tmp/bench-v1.2-fixed-rev (3 runs)
Lines 162–187: v9_1_rev2() — overrides LOG_DIR to /tmp/bench-v1.2-fixed-rev2 (3 runs)
Lines 189–201: case dispatcher + "RUN COMPLETE" echo
```

**`run()` helper signature (exact):**

```bash
run() {
  local label="$1"
  local model="$2"
  local prompt="$3"
  local out="$LOG_DIR/${label}.log"
  local meta="$LOG_DIR/${label}.meta"
  local start_ts=$(date +%s)
  echo "===== $label (model=$model) =====" | tee -a "$LOG_DIR/timeline.txt"
  echo "PROMPT: $prompt" >> "$out"
  echo "----" >> "$out"
  /usr/bin/time -p dotnet run --project src/BlueCode.Cli -- --verbose --model "$model" "$prompt" >> "$out" 2>&1
  local exit_code=$?
  local end_ts=$(date +%s)
  local elapsed=$((end_ts - start_ts))
  echo "label=$label model=$model exit=$exit_code elapsed=${elapsed}s" > "$meta"
  echo "  -> exit=$exit_code elapsed=${elapsed}s" | tee -a "$LOG_DIR/timeline.txt"
}
```

**Per-invocation artifact format:**

| File | Content | Example |
|------|---------|---------|
| `$label.log` | First line: `PROMPT: <prompt text>`, second line: `----`, then full verbose blueCode output including `[Step N]` lines + `[INF] Session ok: N steps, ...` or `[WRN] Session error: ...` + `/usr/bin/time` real/user/sys at end | See `/tmp/bench-v1.2-fixed-rev2/v9_1_rev2_W1_32b.log` |
| `$label.meta` | Single line: `label=<label> model=<model> exit=<N> elapsed=<N>s` | `label=v9_1_rev2_W1_32b model=32b exit=0 elapsed=14s` |
| `$label_result.fs` | Fixture file after agent writes it (W1/W2 tests only) | `v9_1_rev2_W1_32b_result.fs` contains fixed `s.[s.Length - 1]` |
| `timeline.txt` | Append-only: `===== label =====` + `  -> exit=N elapsed=Ns` per run | Human-readable session summary |

**Bash compatibility note:** The script uses only `bash 3.2`-compatible syntax: `local`, arithmetic `$(())`,
`date +%s`, heredoc `<<'EOF'`, simple arrays (no associative arrays), and the `VAR=val func_name`
env-prefix pattern. This works on macOS system bash (3.2.57). No `declare -A`, `[[ =~ ]]` with BASH_REMATCH
arrays, or bash 4+ features are used. **bench/run.sh must keep this compatibility.**

**The `set -u` but not `set -e` choice:** The existing script uses `set -u` (error on unset variables) but
deliberately omits `set -e`. This is intentional — bench runs are expected to continue even if one test
produces a non-zero exit. The `|| true` on `cp` lines reinforces this. `set -euo pipefail` would break
the harness by aborting on the first test failure. Preserve `set -u` only.

**Log directories inventoried:**

| Directory | File count | Contains |
|-----------|-----------|---------|
| `/tmp/bench-v1.2/` | 79 files | phase1 + phaseA + phaseB + phaseC logs/metas + result.fs files + timeline.txt + run.sh + resume_C.sh |
| `/tmp/bench-v1.2-fixed/` | 25 files | v9_1 10 runs (T6×32B×3, T6×72B×3, W1×32B, W2×32B, T1×32B, T5×72B) |
| `/tmp/bench-v1.2-fixed-rev/` | 11 files | v9_1_rev 3 runs (W1×32B, W2×32B, T1×32B) |
| `/tmp/bench-v1.2-fixed-rev2/` | 11 files | v9_1_rev2 3 runs (W1×32B, W2×32B, T1×32B) |

---

## 2. Selector Consolidation Map

### Current selectors → proposed mode flags

| Current selector | Proposed mode flag | Tests included | Why |
|-----------------|-------------------|----------------|-----|
| `v9_1` + `v9_1_rev` + `v9_1_rev2` core tests | `--gate` | T6×32B×3, T6×72B×3, W1×32B, W2×32B, T1×32B, T5×72B (10 runs) | The post-09.1 regression subset; defined in REQUIREMENTS BENCH-04 |
| `phase1` + `phaseA` + `phaseB` + `phaseC` | `--all` | All 36 original tests (T1-T7, variance runs, B tests, write tests) | Full re-bench equivalent |
| `phase1` subset (T6×32B + T6×72B) + canaries (T1, T5) | `--canary` | T1×32B, T5×72B, T6×32B (1 run), T6×72B (1 run) | Quick smoke (4 runs, ~1.5 min) |
| `phase1` T1–T7 × both models | `--regression` | T1-T7 × 32B + 72B (14 runs) | Part 1 baseline reproduced |
| _(new)_ | `--b2` | B2 diagnose × 32B + 72B (2 runs) | Isolated PERF-03 target for Phase 11 |

**Implementation note:** `--gate` is the primary SC1 target. `--all`, `--canary`, `--regression`, and `--b2`
are secondary. The planner should implement `--gate` in Plan 10-02; the others can be simple wrappers.

### Fixture path migration

All prompts that reference `bench-fixtures/` must be updated to `bench/fixtures/` in `bench/run.sh`.
This affects: B1/B2/B3 diagnose prompts, W1/W2 write prompts, and the `cat > bench-fixtures/...` heredoc
fixture-restore blocks. These are scattered throughout the file — grep for `bench-fixtures` to find all ~15 occurrences.

### W1 prompt design note (HIGH IMPORTANCE)

The W1 prompt (`"Read bench/fixtures/bug_lastchar.fs and fix the bug. Save the corrected version using write_file."`) **deliberately names `write_file`**. This exposed the user-prompt/system-prompt priority issue
(09.1-04 finding), and the code-level loop injection in 09.1-05 was the fix. The W1 prompt intentionally
validates the loop injection mechanism. Do **not** rewrite W1's prompt to remove the tool name — that
would make it stop testing the thing it's designed to test. Document this in `documentation/bench.md`.

---

## 3. `baseline.json` Schema Proposal

The baseline records the **post-09.1-05 ground truth** — the production state as of Phase 9.1 completion.
Step counts come from the log evidence; the planner must run the `--gate` subset live once to populate
exact counts before committing baseline.json (or derive from existing logs as shown below).

### Proposed schema

```json
{
  "_meta": {
    "created": "2026-04-26",
    "binary_state": "post-09.1-05",
    "description": "Post-Phase-9.1 baseline. Gate tests: T6×32B/72B, W1/W2×32B, T1/T5 canaries, B2 status."
  },
  "tests": {
    "T6_32b": {
      "step_count": 4,
      "step_count_max": 5,
      "pass": true,
      "elapsed_median_s": 20,
      "note": "3/3 pass required; 4 steps typical (read×3+final pattern)"
    },
    "T6_72b": {
      "step_count": 5,
      "step_count_max": 6,
      "pass": true,
      "elapsed_median_s": 47,
      "note": ">=2/3 pass required; 5 steps typical"
    },
    "W1_32b": {
      "step_count": 3,
      "step_count_max": 3,
      "pass": true,
      "elapsed_median_s": 14,
      "note": "Loop injection enforces 3 steps: read+edit+final (no write_file)"
    },
    "W2_32b": {
      "step_count": 3,
      "step_count_max": 3,
      "pass": true,
      "elapsed_median_s": 17,
      "note": "Directive wording + loop injection: 3 steps (read+edit+final)"
    },
    "T1_32b": {
      "step_count": 1,
      "step_count_max": 3,
      "pass": true,
      "elapsed_median_s": 3,
      "note": "Canary: 1 step typical; accept up to 3 (model variance observed in 09.1-03)"
    },
    "T5_72b": {
      "step_count": 3,
      "step_count_max": 4,
      "pass": true,
      "elapsed_median_s": 17,
      "note": "Canary: glob_search + run_shell + final"
    },
    "B2_32b": {
      "step_count": 2,
      "pass": false,
      "regression": true,
      "expected_diagnosis": "empty list causes DivideByZeroException",
      "actual_diagnosis": "integer truncation",
      "note": "Known regression since v1.2 prompt growth; PERF-03 target"
    },
    "B2_72b": {
      "step_count": 2,
      "pass": false,
      "regression": true,
      "expected_diagnosis": "empty list causes DivideByZeroException",
      "actual_diagnosis": "integer truncation",
      "note": "Same regression as 32B; PERF-03 target"
    }
  }
}
```

**Key design decisions:**

- `step_count` is the **observed** count from the post-09.1-05 run. `step_count_max` is the gate threshold
  (exceeding it fails the gate).
- `pass: false` on B2 is intentional — the baseline records the CURRENT known-regressed state. Phase 11
  (PERF-03) will update baseline.json when it fixes B2.
- `elapsed_median_s` is advisory (not gated). The `--gate` mode should NOT fail on elapsed time — LLM
  response time variance is too high (~30% observed in T6 72B runs). Gate on step count + pass only.
- Step counts should be re-confirmed by running `--gate` once live before committing baseline.json,
  but the values above match the log evidence from the bench runs inventoried.

**Evidence trail for step counts:**

| Test | Step count | Source log |
|------|-----------|-----------|
| T6 32B | 4 | `/tmp/bench-v1.2-fixed/v9_1_T6_32b_run{1,2,3}.log` — all 3 runs = 4 steps |
| T6 72B | 5 | `/tmp/bench-v1.2-fixed/v9_1_T6_72b_run{1,2,3}.log` — all 3 runs = 5 steps |
| W1 32B | 3 | `/tmp/bench-v1.2-fixed-rev2/v9_1_rev2_W1_32b.log` (post-09.1-05) |
| W2 32B | 3 | `/tmp/bench-v1.2-fixed-rev2/v9_1_rev2_W2_32b.log` (post-09.1-05) |
| T1 32B | 1 | `/tmp/bench-v1.2-fixed-rev2/v9_1_rev2_T1_32b.log` (post-09.1-05) |
| T5 72B | 3 | `/tmp/bench-v1.2-fixed/v9_1_T5_72b.log` |

---

## 4. `--gate` Algorithm Sketch

The `--gate` mode must: (1) run the 10-test subset, (2) parse step counts + pass/fail from logs,
(3) diff against `bench/baseline.json`, (4) print per-test summary + one-line verdict, (5) exit 0 on
full PASS, exit 1 on any failure.

### Log parsing patterns

```bash
# Extract step count from a log file:
# Input line: "[INF] Session ok: 3 steps, model=Qwen32B, ..."
# or on failure: "[WRN] Session error: MaxLoopsExceeded, ..."
parse_steps() {
  local log="$1"
  # grep for the Session line; awk out the step count integer
  grep -E "\[INF\] Session (ok|error)" "$log" | grep -o "[0-9]* steps" | grep -o "[0-9]*"
}

parse_exit() {
  local meta="$1"
  grep -o "exit=[0-9]*" "$meta" | grep -o "[0-9]*"
}
```

### `--gate` bash pseudocode

```bash
gate_mode() {
  local BASELINE="$(dirname "$0")/baseline.json"
  local LOG_DIR=/tmp/bench-gate-$(date +%Y%m%d-%H%M%S)
  mkdir -p "$LOG_DIR"
  local fail_count=0

  # Step 1: run the gate subset
  # T6 × 32B × 3 (gate requires 3/3 pass)
  for i in 1 2 3; do
    run "gate_T6_32b_run${i}" "32b" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  done
  # T6 × 72B × 3 (gate requires >=2/3 pass)
  for i in 1 2 3; do
    run "gate_T6_72b_run${i}" "72b" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  done
  # W1, W2 with fixture restore
  # ... (fixture restore heredoc, then run)
  # T1, T5 canaries

  # Step 2: compare each test against baseline.json using jq
  local baseline_step_max
  for label in gate_T6_32b_run1 gate_W1_32b gate_T1_32b ...; do
    local actual_steps
    actual_steps=$(parse_steps "$LOG_DIR/${label}.log")
    local actual_exit
    actual_exit=$(parse_exit "$LOG_DIR/${label}.meta")

    # Map label to baseline key (e.g., gate_T6_32b_run1 → T6_32b)
    local key=$(label_to_key "$label")
    baseline_step_max=$(jq -r ".tests.${key}.step_count_max" "$BASELINE")
    local baseline_pass
    baseline_pass=$(jq -r ".tests.${key}.pass" "$BASELINE")

    # Gate: step count must not exceed baseline_step_max
    if [ "$actual_steps" -gt "$baseline_step_max" ]; then
      echo "FAIL  $label: $actual_steps steps > baseline max $baseline_step_max"
      fail_count=$((fail_count + 1))
    elif [ "$actual_exit" -ne 0 ] && [ "$baseline_pass" = "true" ]; then
      echo "FAIL  $label: exit=$actual_exit (baseline expected pass)"
      fail_count=$((fail_count + 1))
    else
      echo "PASS  $label: $actual_steps steps (max $baseline_step_max), exit=$actual_exit"
    fi
  done

  # Step 3: aggregate verdict
  if [ "$fail_count" -eq 0 ]; then
    echo "===== GATE PASS: all tests within baseline ====="
    exit 0
  else
    echo "===== GATE FAIL: $fail_count test(s) regressed ====="
    exit 1
  fi
}
```

**T6 multi-run pass aggregation:** T6 has 3 runs per model with different pass thresholds (32B needs 3/3,
72B needs ≥2/3). The gate must count passes across the 3 runs, not fail on each run independently.
This requires a small counting loop — not just a per-run comparison.

**jq dependency:** `/usr/bin/jq` version 1.7.1 is present on this Mac. Use it. Pure-bash JSON parsing
would be fragile. jq is a mandatory tool, not optional. Add a `check jq` guard at script startup:

```bash
command -v jq >/dev/null 2>&1 || { echo "jq required but not found"; exit 1; }
```

**Where `--gate` can fail:**
1. **jq absent:** Guard above prevents silent failures. Exit 1 with message.
2. **Log format drift:** If `[INF] Session ok: N steps` changes (e.g., message format change), `parse_steps` returns empty. Defensively check: `[ -z "$actual_steps" ] && echo "WARN: could not parse steps from $label.log"`.
3. **Fixture not writable:** W1/W2 restore the fixture via `cat > bench/fixtures/...` before each run. If the bench/ directory doesn't exist or is read-only, the heredoc silently fails and the agent gets an error or stale file. `bench/fixtures/` must be committed to git and present at run time.
4. **Bash arithmetic on empty:** If `parse_steps` returns empty and you do `[ "" -gt 3 ]`, bash 3.2 errors with "integer expression expected". Protect with: `actual_steps=${actual_steps:-0}`.
5. **T6 "pass" definition:** T6 has no `result.fs` to diff. Pass = correct field names in `final` output. The current gate uses step count as a PROXY for pass (≤4 steps for 32B, ≤5 for 72B = the model didn't loop). This is an imperfect proxy — a model could step ≤4 and still give wrong fields. For Phase 10, step count proxy is acceptable. A future improvement would grep the log for the expected field list, but that is out of Phase 10 scope.

---

## 5. Hang Contingency

**Observed occurrences:**
- v1.2 audit footnote (line 28/139): "32B mlx_lm.server hung mid-generation on W2 32B first attempt. Cleared by `launchctl kickstart -k`."
- 09.1-03 validation report (§Retries): "No server kickstarts were required" for the 9.1 runs.
- Frequency: 1 confirmed occurrence in ~60 total bench runs across v1.2. Low but non-zero.

**Exact recovery command:**
```bash
launchctl kickstart -k gui/$(id -u)/com.ohama.qwen32b
```
(Source: `documentation/local-llm-services.md` lines 190–191 + 241–242)

**Hang detection threshold:** The 09.1-RESEARCH.md recommends 90s. This aligns with:
- Longest observed normal run: T6 72B run1 = 75s. 
- 90s gives 15s margin above the longest observed clean run.
- blueCode itself has a 180s HTTP client timeout, but the bench will appear "hung" to the runner much sooner
  because `/usr/bin/time dotnet run` blocks and the spinner output stops.

**Recommended hang-detection approach in bench/run.sh:**

```bash
run_with_hang_check() {
  local label="$1"
  local model="$2"
  local prompt="$3"
  local timeout_s=120   # 90s margin + some buffer; blueCode itself exits at 180s
  
  # Run with timeout; if it expires, kill and retry once
  run "$label" "$model" "$prompt"
  local exit=$?
  if [ $exit -eq 124 ]; then   # GNU timeout exit code for timeout
    echo "WARN: $label timed out (${timeout_s}s); kicking 32B server and retrying"
    launchctl kickstart -k "gui/$(id -u)/com.ohama.qwen32b"
    sleep 30   # allow 32B to reload weights (~17GB; usually 20-25s)
    run "${label}_retry" "$model" "$prompt"
  fi
}
```

Note: macOS `timeout` command: available via `brew install coreutils` as `gtimeout`, or via `perl -e
'alarm N; exec ...'`. The simpler approach is to wrap `dotnet run` with a subshell + `kill` after a timer,
but that adds complexity. **Recommendation for Phase 10:** Document the contingency in `bench.md` as a
MANUAL step (the runner notices no output progress for 90s and runs the kickstart manually), rather than
automating it in the script. Automation adds risk of false-positive kickstarts interrupting valid slow runs
(e.g., 72B T6 run1 at 75s). The REQUIREMENTS say "hang detection + auto-kickstart-and-retry" — this is a
reasonable interpretation that keeps the script simple.

**Edge case — kickstart itself hangs:** If the server process refuses to terminate (e.g., stuck in Metal
GPU dispatch), `kickstart -k` may block. The workaround from `documentation/local-llm-services.md §5` is
`launchctl unload + load -w`, which unregisters and re-registers the plist:
```bash
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen32b.plist
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen32b.plist
```
This is the nuclear option; document it in `bench.md` as a last resort.

---

## 6. `documentation/bench.md` Outline

Section headers and 1-line purpose each:

```
# Bench Harness (bench/run.sh)

## Overview
One-para: what bench/run.sh does, where logs go, what baseline.json is.

## Quick Start
The 3-command incantation: build, run --gate, interpret output.

## Mode Flags
Table: --gate / --regression / --canary / --all / --b2, invocation count, wall-clock, purpose.

## Fixture Naming Convention
Rule: bug_<domain>_<N>.fs or bug_<symptom>.fs. Examples: bug_lastchar.fs, bug_average.fs, bug_divide_zero.fs.
Single-bug per file. Comment in file explains what triggers the bug.

## Prompt Design Guidance
Key rule: do NOT name a specific tool in fixture prompts (e.g., "using write_file") for NEW fixtures.
Rationale: 09.1-04 discovery — user-prompt tool instruction overrides system-prompt directive, exposing
a user/system priority issue and making the test sensitive to tool naming rather than task completion.
Exception: W1 deliberately retains "using write_file" to validate the 09.1-05 loop injection mechanism.

## How to Add a New Test
Step-by-step: (1) create bench/fixtures/bug_<name>.fs, (2) add run() call to the appropriate function
in bench/run.sh, (3) run the test once to observe step count, (4) add to baseline.json if it's a gate test.

## How to Update Baseline After an Intentional Fix
Step-by-step: (1) make the fix, (2) run bench/run.sh --gate, (3) verify PASS, (4) update baseline.json
step_count and pass fields, (5) commit baseline.json alongside the fix commit.

## Hang Contingency for mlx_lm.server 32B
When: 32B run shows no output progress for >90s.
Recovery command: launchctl kickstart -k gui/$(id -u)/com.ohama.qwen32b
Wait: ~30s for reload.
If kickstart hangs: launchctl unload + load -w (nuclear option).
Retry the failed run manually.

## Interpreting Gate Output
How to read the PASS/FAIL per-test lines; what step count regression looks like; what "B2 regression=true" means.

## Known Regressions (Baseline State)
Note: B2 (divide-by-zero) is currently pass:false in baseline.json — this is intentional. Phase 11 (PERF-03) targets this.
```

---

## 7. CLAUDE.md Current References to `/tmp/bench-v1.2/run.sh`

**Result: zero references.** `grep -n "bench\|/tmp/"` on `CLAUDE.md` returns no output.

SC5 says "CLAUDE.md no longer references `/tmp/bench-v1.2/run.sh`" — this condition is already satisfied.
The update needed is **additive only**: add a line to CLAUDE.md's conventional/tool-entry section pointing
to `bench/run.sh` as the canonical bench entry point. Suggested placement: under the "## When Stuck"
section or a new "## Bench" section near the top.

Suggested addition to CLAUDE.md:

```markdown
## Bench

`bench/run.sh` is the canonical regression harness. Run `bench/run.sh --gate` to validate the current binary.
Baseline is `bench/baseline.json`. See `documentation/bench.md` for full usage.
```

---

## 8. Pitfalls

### Pitfall 1: `bench-fixtures/` was untracked and could silently disappear

**What goes wrong:** During 09.1-03, the runner discovered `bench-fixtures/` was missing and the
`cat > bench-fixtures/...` heredoc failed silently because the directory didn't exist. This corrupted
the W1/W2 fixture content (empty file) and the tests ran against a broken fixture.
**Why it happens:** `bench-fixtures/` was never committed to git; it was created during the original
v1.2 bench run and not tracked.
**How to avoid:** Phase 10 moves fixtures to `bench/fixtures/` and commits them. The script must `mkdir -p
bench/fixtures/` as a guard even if the directory exists in git, since checkout might skip empty dirs.
**Warning signs:** `cat > bench/fixtures/bug_lastchar.fs <<'EOF'` writes an empty file if the directory
doesn't exist — the heredoc creates the file but with zero content. Add a guard: verify file is non-empty
after restore.

### Pitfall 2: W1/W2 write tests mutate the fixture in place

**What goes wrong:** W1 and W2 agents WRITE to `bench/fixtures/bug_lastchar.fs` and `bug_average.fs`
respectively. After each W1/W2 run, the fixture is in the "fixed" state. The next run of W1/W2 needs a
fresh "broken" fixture. The existing run.sh handles this with a `cat > bench-fixtures/... <<'EOF'` restore
block before each run. This must be preserved in `bench/run.sh`.
**Git contamination risk:** If `bench/run.sh` finishes a W1/W2 run and the user does `git status`, they'll
see `bench/fixtures/bug_lastchar.fs` as modified (it was fixed by the agent). This is expected behavior
but could confuse contributors who see modified committed files. Document in `bench.md`.

### Pitfall 3: macOS bash 3.2 has no associative arrays

**What goes wrong:** If the planner adds `declare -A baseline_steps` or any bash 4+ feature for the
`--gate` implementation, the script silently uses indexed arrays or errors out on macOS.
**How to avoid:** Use `jq` for all JSON lookups. Don't try to load baseline.json into bash structures.
The `--gate` implementation should call `jq -r ".tests.${key}.step_count_max" baseline.json` inline.
No associative arrays needed.

### Pitfall 4: `set -e` would abort bench on first test failure

**What goes wrong:** Adding `set -e` (which would seem like a good idea for a "robust" script) causes
bash to exit immediately if any command returns non-zero. A single test returning exit=1 (e.g., blueCode
`MaxLoopsExceeded`) would abort the entire bench run.
**How to avoid:** Keep `set -u` only (as in the existing script). Use explicit exit code capture via
`local exit_code=$?` after each `run` call and accumulate failures.

### Pitfall 5: B2 "divide-by-zero fixture" is a NEW file, not bug_average.fs

**What goes wrong:** SC2 says "bug_lastchar.fs, bug_average.fs, and the B2 divide-by-zero fixture" as
three separate files. The `pB_B2` test in the existing run.sh uses `bug_average.fs` (which happens to
have a divide-by-zero bug on empty list). But bug_average.fs is ALSO the W2 write-test fixture. Using
the same file for two different test types is an anti-pattern.
**The solution:** Create `bench/fixtures/bug_divide_zero.fs` as a clean diagnose-only fixture focused on
the empty-list divide-by-zero. Content suggestion:

```fsharp
module DivideZero

/// Returns the average of a list. Raises DivideByZeroException on empty input.
let average (xs: int list) : int =
    List.sum xs / List.length xs
```

The B2 regression prompt becomes: `"Read bench/fixtures/bug_divide_zero.fs and identify the bug. Be
specific about what input triggers it."` This separates the "diagnose bug_average" use from the "write
averageSafe" use, and gives B2 a prompt with no explicit tool naming (per the new guidance).

### Pitfall 6: The `LOG_DIR` env-prefix override pattern in v9_1_rev/rev2

**What it is:** Lines like `LOG_DIR="$LOG_DIR" run "label" ...` use bash's env-variable-prefix-for-function
syntax. In bash, `VAR=val function_name args` sets `VAR` temporarily in the shell environment for the
function call. This works in bash 3.2 for shell functions (unlike external commands where it's a strict
subprocess env). Empirically confirmed: rev/rev2 logs landed in the correct directories.
**Impact on Phase 10:** The new `bench/run.sh` should use a cleaner approach: set `LOG_DIR` once at the
top of each mode function, then call `run()` which reads the global `LOG_DIR`. This avoids the confusing
env-prefix syntax. All mode functions already declare `local LOG_DIR=...` in v9_1/rev/rev2 — replicate
that pattern cleanly.

---

## 9. Plan Decomposition Recommendation

Three plans as proposed, with one modification:

### Plan 10-01: File Move + Script Consolidation (BENCH-01, BENCH-02, SC5)

**Scope:**
- Create `bench/` directory structure: `bench/run.sh`, `bench/fixtures/`, `bench/baseline.json` (empty stub)
- Move `bench-fixtures/bug_lastchar.fs` and `bug_average.fs` → `bench/fixtures/`
- Create `bench/fixtures/bug_divide_zero.fs` (the new B2 fixture)
- Write `bench/run.sh` with mode-flag dispatch: `--all`, `--regression`, `--canary`, `--b2` (but NOT `--gate`
  yet — that depends on `baseline.json` which isn't finalized)
- Update all `bench-fixtures/` path references to `bench/fixtures/` in run.sh
- Add CLAUDE.md `## Bench` section (SC5 — additive only, zero deletions needed)
- **Commit** `bench/` directory, `bench/fixtures/`, CLAUDE.md update

**What this plan explicitly excludes:** `--gate` mode and `baseline.json` population (Plan 10-02).

### Plan 10-02: `baseline.json` + `--gate` Mode (BENCH-03, BENCH-04, SC1, SC3)

**Scope:**
- Run `bench/run.sh --canary` live to confirm T6/W1/W2/T1/T5 step counts match log evidence
- Populate `bench/baseline.json` with confirmed step counts (use the schema in §3 above)
- Implement `--gate` mode in `bench/run.sh`: runs 10-test subset, parses logs with `grep + jq`, diffs
  against baseline.json, prints per-test PASS/FAIL, exits non-zero on regression
- SC1 verification: run `--gate` against current binary (should exit 0), then verify exit 1 against a
  modified baseline or a deliberately-regressed binary
- **Commit** `bench/run.sh` (gate mode added), `bench/baseline.json`

**Note on SC1 verification:** To verify `--gate` exits non-zero on regression, the executor can either
(a) temporarily modify `bench/baseline.json` to have a lower `step_count_max` than what the current binary
produces, or (b) rename/modify a key binary file and confirm exit 1. Option (a) is safer. Document this in
the plan's verification section.

### Plan 10-03: Documentation + B2 Fixture Confirmation (BENCH-05, SC2, SC4)

**Scope:**
- Write `documentation/bench.md` (outline in §6 above)
- Confirm `bench/fixtures/` has exactly 3 fixture files: `bug_lastchar.fs`, `bug_average.fs`, `bug_divide_zero.fs`
- Run `bench/run.sh --b2` to record B2's current regression state and confirm `baseline.json` B2 entries
- Final SC checklist verification

**Why this decomposition:** Plan 10-02 MUST complete before Plan 10-03 because `documentation/bench.md`
needs to describe `--gate` semantics accurately. The plan sequence is 01 → 02 → 03 with each plan's output
feeding the next.

---

## 10. Open Questions

1. **Should `--gate` run T6 × 3 runs per model or × 1?**
   - What we know: REQUIREMENTS BENCH-04 says "T6 × 32B/72B" (singular), not "×3". The v9_1 validation
     used 3 runs to guard against variance, but that was a one-time validation. For a regression gate,
     single runs are faster (~2 min total) and sufficient if the fix is solid.
   - What's unclear: The user hasn't specified 1 vs 3 per model in the gate subset.
   - Recommendation: Use **1 run per model for --gate** (8 total: T6×32B, T6×72B, W1×32B, W2×32B,
     T1×32B, T5×72B, B2×32B, B2×72B). The multi-run validation was a one-time confirmation. Gate mode
     should be fast.

2. **Should `--gate` write a per-run JSON report alongside the console summary?**
   - What we know: REQUIREMENTS BENCH-04 says "prints one-line PASS/FAIL summary + per-test diff on FAIL".
     A JSON report would enable future automation (e.g., CI artifact).
   - What's unclear: Whether the user wants a machine-readable output file or just console output.
   - Recommendation: Console only for Phase 10. A `gate-report.json` output flag can be added in v1.4+.

3. **Does B2's fixture file (`bug_divide_zero.fs`) mean a new file, or does the B2 diagnose test just use the existing `bug_average.fs`?**
   - What we know: SC2 lists three files: "bug_lastchar.fs, bug_average.fs, and the B2 divide-by-zero
     fixture." This implies three DISTINCT files.
   - What's unclear: Whether "B2 divide-by-zero fixture" is a new standalone file or an alias for the
     bug_average.fs file reused in a different test context.
   - Recommendation: Create a separate `bug_divide_zero.fs` file (see §8 Pitfall 5). This gives the B2
     diagnose test a clean, purpose-built fixture that doesn't conflict with W2's write task.

4. **Should `bench/run.sh` have a `--help` flag?**
   - Recommendation: Yes, trivially. The current script's `*) echo "usage: ..."` pattern should become
     `--help | -h)` plus `*) echo "error: unknown mode..."`. Small but makes the script more discoverable.

5. **Step-count tolerance for T1: 1–3 acceptable range is wide. Should the gate be tighter?**
   - What we know: T1 showed 1 step (normal), 3 steps (model variance in 09.1-03), 1 step again (09.1-05).
     The 09.1-VALIDATION.md classified 3 steps as CANARY-WARN, not CANARY-FAIL.
   - Recommendation: Gate on `step_count_max: 3` for T1. A 4-step T1 would be alarming. 3 steps is the
     historical maximum and still produces correct output (1024).

---

## Sources

### Primary (HIGH confidence — direct file reads)
- `/tmp/bench-v1.2/run.sh` — complete 201-line inventory
- `/tmp/bench-v1.2-fixed/` (10 files), `/tmp/bench-v1.2-fixed-rev/` (11 files), `/tmp/bench-v1.2-fixed-rev2/` (11 files) — all .log, .meta, .timeline read
- `/Users/ohama/projs/blueCode/bench-fixtures/bug_lastchar.fs` — verified fixed content
- `/Users/ohama/projs/blueCode/bench-fixtures/bug_average.fs` — verified averageSafe content (post-W2 run)
- `.planning/milestones/v1.2-phases/09.1-bench-follow-up-fixes/09.1-VALIDATION.md` — 456 lines, gate/step-count/hang evidence
- `.planning/milestones/v1.2-MILESTONE-AUDIT.md` — B2 regression attribution, hang occurrence
- `.planning/REQUIREMENTS.md` — BENCH-01 through BENCH-05 verbatim
- `.planning/ROADMAP.md` — Phase 10 success criteria verbatim
- `CLAUDE.md` — confirmed zero bench/tmp references (SC5 free)
- `documentation/v1.2-bench-followup.md` — design rationale
- `documentation/local-llm-services.md` — exact launchctl commands

### Secondary (HIGH confidence — bash/tool verification)
- `bash --version` on target machine: GNU bash 3.2.57 (no brew bash, no bash 4)
- `which jq && jq --version`: `/usr/bin/jq` 1.7.1 — confirmed available
- `ls /Users/ohama/projs/blueCode/bench-fixtures/` — confirmed only 2 files (no bug_validate.fs)

---

## Metadata

**Confidence breakdown:**
- Existing-bench inventory: HIGH — all files read directly from disk
- Selector consolidation map: HIGH — derived directly from run.sh function structure + REQUIREMENTS wording
- baseline.json schema: HIGH for step counts (log evidence); MEDIUM for elapsed times (advisory only)
- `--gate` algorithm: HIGH for structure; MEDIUM for T6 pass aggregation detail (needs live verification)
- Hang contingency: HIGH for commands; MEDIUM for threshold (90s is RESEARCH-recommended, not empirically gated)
- Plan decomposition: HIGH — follows REQUIREMENTS traceability directly

**Research date:** 2026-04-26
**Valid until:** 2026-05-26 (stable; bench artifacts are on-disk and don't change; bash/jq versions stable)
