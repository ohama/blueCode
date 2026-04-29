---
created: 2026-04-28
description: Five macOS bash strict-mode patterns that silently corrupt eval harness output — symptoms, root causes, canonical fixes, commit refs
---

# macOS Bash Strict-Mode Patterns in Eval Harnesses

Under `set -euo pipefail`, macOS BSD utilities and child-process exit codes interact with
bash's strict mode in ways that turn harmless conditions into script-aborting failures. This
howto enumerates the 5 patterns hit across v2.1–v2.3 eval harness development, with the
canonical fix for each.

**Common rule:** Every `grep -c` / `grep -cE` / `grep -oE` call in a command substitution
needs a `|| true` guard. Every `dotnet run` invocation that may exit non-zero (data, not
harness error) needs `set +e` / `set -e` wrapping. Every `tee` or redirect needs a `mkdir -p`
for its parent directory first.

---

## Pattern 1: `set -e` aborts on `dotnet run` non-zero exit

**Symptom:** `bench/eval-qwen35-122b.sh` aborts immediately after `dotnet run`, producing
no harness output files. The blueCode process completed its task (possibly hitting
`MaxLoopsExceeded`, which exits 1), but the harness treats exit 1 as a fatal error and
terminates before writing `.meta` or `.diff.txt` output files.

**Root cause:** `set -e` treats ANY non-zero exit code from a child process as fatal. blueCode
exits 1 when the agent reaches `MaxLoopsExceeded` — this is observational data the harness
wants to capture (e.g., "agent needed more than 10 steps for this task"), not a script
failure. Without the guard, the harness can never record this data.

**Canonical fix:**

```bash
set +e
/usr/bin/time -p dotnet run --project src/BlueCode.Cli -- --verbose --model 122b "$prompt" >> "$out" 2>&1
local exit_code=$?
set -e
# exit_code is now available: 0=success, 1=MaxLoopsExceeded, 2=other error
```

Apply this pattern to every `dotnet run` invocation in the harness. The `local exit_code`
capture is critical — use it in the `.meta` file to distinguish MaxLoopsExceeded (data) from
genuine harness errors.

**Reference:**
- `bench/eval-qwen35-122b.sh`: lines 274-277 (`run_refactor`), 329-332 (`run_langcoverage`),
  372-375 and 394-397 (`run_multiturn` turns 1 and 2..N), 434-436 (`run_schema_rate`)
- Commit `4a2c3c6` (`fix(21-03): handle non-zero dotnet exit in run_refactor/run_langcoverage under set -e`)

---

## Pattern 2: `grep -c` pipe to `awk` aborts under `pipefail`

**Symptom:** Harness aborts while computing an aggregate count (e.g., total orphan references
across multiple files). The abort happens inside a command substitution that pipes `grep -c`
output into `awk`. The count variable is never written, and a downstream file that reports
the score reads a stale value from a previous run — producing a ghost PASS or ghost FAIL.

**Root cause:** `grep -c` emits per-file counts in `file:N` format and exits 1 when no
file contains a match. Under `set -euo pipefail`, the failure of any command in a pipeline
propagates as the pipeline's exit code. So even though `awk` succeeds (it receives EOF and
prints the correct sum), the pipeline exit code is 1 and the command substitution aborts.

This is the "double jeopardy" pattern: `grep -c` both emits the data AND signals failure
at the same time. The zero-match case is simultaneously the correct output (count=0) and a
non-zero exit code.

**Canonical fix:** Wrap the `grep` call inside a subshell with `|| true` so that the
subshell always exits 0, then pipe to `awk` outside:

```bash
# Pattern: ( grep ... || true ) | awk ...
orphan_count=$( (grep -cE '\b(let |Calculator\.)add\b' \
    "$fixture_dir/Calculator.fs" \
    "$fixture_dir/Main.fs" \
    "$fixture_dir/Tests.fs" 2>/dev/null || true) | awk -F: '{sum+=$2} END {print sum+0}')
```

The `|| true` must be INSIDE the subshell (before the `)`), not outside the `awk` pipe.
Outside placement would normalize awk's exit code, not grep's.

**Reference:**
- `bench/eval-qwen35-122b.sh`: lines 297-300 (`run_refactor` orphan count)
- Commit `9f0b43b` (`fix(21-04): fix grep -c under set -euo pipefail in run_schema_rate`)

---

## Pattern 3: `mkdir -p` must precede `tee` / output redirect

**Symptom:** Script aborts with `tee: bench/runs/qwen35-eval-<timestamp>/timeline.txt:
No such file or directory`. The abort occurs at the first `tee -a "$LOG_DIR/timeline.txt"`
call inside a function. No run output is produced.

**Root cause:** `tee` and shell redirect `>` do not auto-create parent directories. If
`$LOG_DIR` (which includes a timestamp component computed at script startup) does not exist
when `tee` is called, `tee` returns non-zero. Under `set -e`, this aborts the script. The
issue is timing: `$LOG_DIR` is declared as a global variable at the top of the script, but
the actual `mkdir -p "$LOG_DIR"` call must happen in each function before the first write,
because different functions may be called standalone (not just via `--full`).

**Canonical fix:** Every `run_*` function must call `mkdir -p "$LOG_DIR"` as its FIRST
substantive statement, before any `tee` or append-redirect targeting that directory:

```bash
run_coldstart() {
  mkdir -p "$LOG_DIR"                                                   # <-- first line
  echo "===== COLDSTART =====" | tee -a "$LOG_DIR/timeline.txt"        # <-- safe now
  ...
}
```

For functions that write to subdirectories (e.g., `$LOG_DIR/schema_logs/`), add a separate
`mkdir -p` for the subdirectory before the first write to it:

```bash
run_schema_rate() {
  require_port_8001
  mkdir -p "$LOG_DIR"
  local logs_dir="$LOG_DIR/schema_logs"
  mkdir -p "$logs_dir"                    # <-- subdirectory before per-iter writes
  ...
  echo "===== $label =====" > "$iter_log" # <-- safe now
}
```

**Reference:**
- `bench/eval-qwen35-122b.sh`: line 467 (`run_coldstart`), line 415 (`run_schema_rate`
  subdirectory), line 261 (`run_refactor`), line 317 (`run_langcoverage`)
- Commit `a6159c4` (`fix(23-01): move mkdir before tee in run_coldstart to satisfy set -euo pipefail`)

---

## Pattern 4: `grep -cE` zero-match exit-1 in command substitution

**Symptom:** The PASS branch — where the rubric criterion is "zero occurrences of the
problematic pattern" — silently aborts the script. The output file that records the count
(e.g., `refactor_orphan_count.txt`) is never written. A subsequent scoring step reads a
stale value from a previous run and reports the wrong result. The harness log shows no error
message; it simply stops mid-run.

**Root cause:** `grep -cE 'pattern' file` exits 1 when the file contains zero matches.
Under `set -euo pipefail`, even when used in a simple command substitution (not a pipe),
a non-zero exit code from the command inside `$(...)` propagates and aborts the script.

Critically, `2>/dev/null` does NOT prevent the abort — it only redirects stderr. The exit
code is independent of stderr suppression:

```bash
# WRONG: 2>/dev/null does not guard against exit-1
block_invalid=$(grep -c "InvalidJsonOutput" "$iter_log" 2>/dev/null)
# ^ aborts if zero matches
```

**Canonical fix:** Append `|| true` to normalize the exit code:

```bash
# CORRECT: || true ensures exit code is always 0
block_invalid=$(grep -c "InvalidJsonOutput" "$iter_log" 2>/dev/null || true)
# block_invalid is now "0" (not empty) when there are no matches
```

Apply `|| true` to ALL `grep -c` / `grep -cE` calls in command substitutions, even those
where you "know" there will always be matches. A single regression adding `|| true` where
it should have been is far cheaper than debugging a silent abort.

**Reference:**
- `bench/eval-qwen35-122b.sh`: lines 438-441 (`run_schema_rate` per-iteration count),
  lines 402-404 (`run_multiturn` InvalidJsonOutput + step count), line 452
  (`run_schema_rate` cross-check via `grep -l`)
- Commit `9f8e06e` (`fix(27-02): guard orphan grep against set -e abort on PASS case`)

---

## Pattern 5: unguarded `grep -oE` in command substitution (zero-match case)

**Symptom:** Harness aborts inside a multi-turn session loop when blueCode fails to emit
a session ID (e.g., the process crashes before reaching the session-ID log line). The
error-handling branch (`if [ -z "$sid" ]`) that would safely `continue` to the next
trial is never reached. The script exits non-zero with no diagnostic output.

**Root cause:** `grep -oE 'pattern' file | head -1 | awk '{print $2}'` — `grep -oE`
exits 1 when the file contains zero matches. Under `set -euo pipefail`, `pipefail` applies
to the entire pipeline: even though `head` and `awk` both succeed (they receive EOF and
produce empty output), the pipeline exit code is 1 (from `grep`). This propagates through
the command substitution and aborts the script before the downstream guard can run.

This is the same root cause as Pattern 4, but applied to `grep -oE` (extract-matching-part)
rather than `grep -c` (count). The fix is identical.

**Canonical fix:**

```bash
# WRONG: grep -oE exits 1 on no match; pipefail propagates through | head | awk
sid=$(grep -oE "Session: [a-zA-Z0-9_-]+" "$stderr_file" | head -1 | awk '{print $2}')

# CORRECT: || true at the end of the pipeline normalizes exit code
sid=$(grep -oE "Session: [a-zA-Z0-9_-]+" "$stderr_file" 2>/dev/null | head -1 | awk '{print $2}' || true)
if [ -z "$sid" ]; then
  echo "  ERROR: no session id captured" | tee -a "$LOG_DIR/timeline.txt"
  echo "exit=99 reason=no-session-id" > "$trial_dir/meta"
  continue
fi
```

The `|| true` placement here is at the END of the full pipeline (after `awk`), because it's
the pipeline as a whole that must normalize to zero, not just `grep` in isolation.

**Reference:**
- `bench/eval-qwen35-122b.sh`: lines 378-383 (`run_multiturn` session-id capture)
- Commit `94d905c` (`fix(28-01): guard grep -oE session-id capture against pipefail abort`)
  — discovered during Phase 28 harness audit; all other call sites were guarded, this one
  was missed because it was followed by a `if [ -z "$sid" ]` guard that appeared to handle
  the empty-result case (but was unreachable under pipefail)

---

## Summary Table

| Pattern | Trigger | Zero-match outcome | Canonical fix |
|---------|---------|-------------------|---------------|
| 1: `dotnet run` exit | blueCode MaxLoopsExceeded → exit 1 | Script aborts; no .meta written | `set +e` / `set -e` wrapper |
| 2: `grep -c` pipe to `awk` | zero matches across multiple files | Pipeline exit 1; awk sum never written | `( grep ... \|\| true ) \| awk` |
| 3: `tee` missing directory | `$LOG_DIR` not yet created | tee exits non-zero; no timeline | `mkdir -p "$LOG_DIR"` first line |
| 4: `grep -c` / `grep -cE` scalar | zero matches in single file | Substitution exits 1; count never assigned | `$(grep -c ... \|\| true)` |
| 5: `grep -oE` in pipeline | no match in file | Pipeline exits 1; downstream guard unreachable | `$(... \|\| true)` at pipeline end |

---

## Common Rule

Under `set -euo pipefail` on macOS bash 3.2:

1. **Every `dotnet run`** that may exit non-zero for non-fatal reasons: wrap with `set +e` /
   `set -e` and capture `local exit_code=$?`
2. **Every `grep -c` / `grep -cE` / `grep -oE`** in a command substitution: append `|| true`
   (or wrap with `( ... || true )` when piping to a secondary command)
3. **Every `tee` / redirect** to `$LOG_DIR/...`: call `mkdir -p "$LOG_DIR"` before it in
   the same function
4. **Every `seq M N`** where M and N could invert (M > N): guard with
   `[ "$M" -le "$N" ] && seq "$M" "$N" || true`

When in doubt: if a command can exit non-zero and the non-zero case is "everything is fine"
for the harness, add `|| true`.

---

## When to Add a New Pattern

Any harness PR that adds `|| true` to fix a silent abort is documenting a new instance of
one of these patterns (or a 6th pattern). When that happens:

1. Identify which of the 5 patterns it matches (or add a new section here)
2. Add the line-number reference to the relevant Pattern section
3. Add the commit hash

The canonical signal: "script aborted with no error message, and the downstream file that
should have been written is missing."

---

## Cross-References

- `bench/eval-qwen35-122b.sh` — all 5 patterns live in this file; it is the canonical
  example of a correctly-guarded `set -euo pipefail` eval harness
- Commit `4a2c3c6` (Pattern 1 first introduction: `fix(21-03): handle non-zero dotnet exit`)
- Commit `9f0b43b` (Pattern 2 + 4: `fix(21-04): fix grep -c under set -euo pipefail`)
- Commit `eab900c` (BSD seq: `fix(21-04): fix BSD seq countdown bug in run_multiturn`)
- Commit `a6159c4` (Pattern 3: `fix(23-01): move mkdir before tee in run_coldstart`)
- Commit `9f8e06e` (Pattern 4 extension: `fix(27-02): guard orphan grep against set -e abort`)
- Commit `94d905c` (Pattern 5: `fix(28-01): guard grep -oE session-id capture`)
- Plan summaries: `.planning/milestones/v2.1-phases/21-04-SUMMARY.md` (P1+P2+P5 initial
  discoveries), `.planning/milestones/v2.2-phases/23-01-SUMMARY.md` (P3 coldstart fix),
  `.planning/milestones/v2.3-phases/27-02-SUMMARY.md` (P4 orphan grep fix)
- `documentation/howto/iterate-llm-prompts-with-bench-driven-validation.md` — when to use
  the bench gate to verify harness changes don't regress existing eval runs
- `documentation/bench.md` — harness usage overview and `bench/run.sh --gate` reference
