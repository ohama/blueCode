#!/bin/bash
# bench/run.sh — blueCode regression harness (single-model 122B canonical)
# Rewritten in Phase 19 (19-02) to absorb scripts/bench-122b-only.sh.
# All invocations use --model 122b. Dual-model loops removed.
#
# Usage: bench/run.sh <mode>
#   --gate        Regression gate (7 invocations, ~3-4 min); exits non-zero on regression
#   --regression  Part 1 reproducibility: T1-T7 x 122B (7 runs, ~6 min)
#   --canary      Quick smoke: T1, T5, T6x2 (4 runs, ~1.5 min)
#   --b2          B2 divide-by-zero diagnose only (1 run, ~30 s)
#   --all         Full re-bench: regression + variance + write tests (~25 min)
#   --help, -h    Show this message

set -u

# Auto-reset W1/W2 write-task fixtures on exit (success, failure, or Ctrl-C).
# bug_divide_zero.fs is read-only by design (B2 diagnose); do NOT include it here.
trap 'git checkout -- bench/fixtures/bug_lastchar.fs bench/fixtures/bug_average.fs bench/fixtures/bug_binsearch.fs bench/fixtures/refactor_multifile/Calculator.fs bench/fixtures/refactor_multifile/Main.fs bench/fixtures/refactor_multifile/Tests.fs 2>/dev/null || true' EXIT
cd /Users/ohama/projs/blueCode

command -v jq >/dev/null 2>&1 || { echo "jq required but not found"; exit 1; }

LOG_DIR="bench/runs/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$LOG_DIR"

# ---------------------------------------------------------------------------
# run() — core invocation helper
# Reads $LOG_DIR from the calling scope (bash dynamic scoping of locals).
# ---------------------------------------------------------------------------
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

# ---------------------------------------------------------------------------
# show_help() — print usage
# ---------------------------------------------------------------------------
show_help() {
  cat <<'HELP'
bench/run.sh — blueCode regression harness (single-model 122B canonical)

Usage: bench/run.sh <mode>

Modes:
  --gate        Regression gate (7 invocations, ~3-4 min); exits non-zero on regression
  --regression  Part 1 reproducibility: T1–T7 × 122B (7 runs, ~6 min)
  --canary      Quick smoke: T1, T5, T6×2 (4 runs, ~1.5 min)
  --b2          B2 divide-by-zero diagnose only (1 run, ~30 s)
  --all         Full re-bench: regression + variance + write tests (~25 min)
  --help, -h    Show this message

All invocations use --model 122b (single-model canonical default, Phase 19).
Logs land in bench/runs/<timestamp>/ (gitignored). Baseline lives at bench/baseline.json.
HELP
}

# ---------------------------------------------------------------------------
# regression() — Part 1: T1–T7 × 122B (7 runs)
# ---------------------------------------------------------------------------
regression() {
  echo "######## REGRESSION: 7 main tests x 122B ########" | tee -a "$LOG_DIR/timeline.txt"
  run "regression_T1_122b" "122b" "What is 2 to the power of 10? Answer with just the number."
  run "regression_T2_122b" "122b" "In F#, what does the forward pipe operator |> do? One short sentence."
  run "regression_T3_122b" "122b" "List the files in src/BlueCode.Core and count them."
  run "regression_T4_122b" "122b" "Read src/BlueCode.Core/Router.fs and explain what classifyIntent does in one sentence."
  run "regression_T5_122b" "122b" "Find BlueCode.slnx and tell me its size in bytes using wc."
  run "regression_T6_122b" "122b" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  run "regression_T7_122b" "122b" "Read src/BlueCode.Core/ContextBuffer.fs and explain if the ring buffer has any edge case issue when capacity equals 1."
}

# ---------------------------------------------------------------------------
# canary() — Quick smoke: 4 invocations (~1.5 min)
# ---------------------------------------------------------------------------
canary() {
  echo "######## CANARY: 4-run smoke test ########" | tee -a "$LOG_DIR/timeline.txt"
  run "canary_T1_122b" "122b" "What is 2 to the power of 10? Answer with just the number."
  run "canary_T5_122b" "122b" "Find BlueCode.slnx and tell me its size in bytes using wc."
  run "canary_T6a_122b" "122b" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  run "canary_T6b_122b" "122b" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
}

# ---------------------------------------------------------------------------
# b2_mode() — B2 divide-by-zero diagnose only (1 run)
# Prompt does NOT name a tool (per BENCH-05 / 09.1-04 guidance).
# ---------------------------------------------------------------------------
b2_mode() {
  echo "######## B2: divide-by-zero diagnose (1 run, 122B) ########" | tee -a "$LOG_DIR/timeline.txt"
  run "b2_122b" "122b" "Read bench/fixtures/bug_divide_zero.fs and identify the bug. Be specific about what input triggers it."
}

# ---------------------------------------------------------------------------
# mt() — Multi-turn fixture: 2 turns sharing a session via --resume <id>
# Validates PERSIST-01 end-to-end at the bench layer (Phase 16-03).
# Turn 1 establishes context (lists files); turn 2 references prior context.
# Both turns must exit 0; turn-1 step count is the gate metric (parser uses
# head -1 on '[INF] Session ok: N steps' markers — matches single-turn semantics).
# ---------------------------------------------------------------------------
mt() {
  local label="$1"
  local out="$LOG_DIR/${label}.log"
  local meta="$LOG_DIR/${label}.meta"
  local fixture_dir="bench/fixtures"
  local followup_prompt
  followup_prompt=$(cat "$fixture_dir/mt_followup.txt")

  echo "===== $label (multi-turn, model=122b) =====" | tee -a "$LOG_DIR/timeline.txt"
  echo "TURN1_PROMPT: List the files in bench/fixtures and tell me the count." >> "$out"
  echo "TURN2_PROMPT: $followup_prompt" >> "$out"
  echo "----" >> "$out"

  local start_ts=$(date +%s)

  # Turn 1: capture session id from stderr (Phase 15-02 deliverable: 'Session: <id>' on stderr)
  local turn1_stderr="$LOG_DIR/${label}_turn1.stderr"
  /usr/bin/time -p dotnet run --project src/BlueCode.Cli -- --verbose --model 122b "List the files in bench/fixtures and tell me the count." >> "$out" 2>"$turn1_stderr"
  local turn1_exit=$?
  cat "$turn1_stderr" >> "$out"
  local sid=$(grep -oE "Session: [a-zA-Z0-9_-]+" "$turn1_stderr" | head -1 | awk '{print $2}')

  if [ -z "$sid" ]; then
    echo "  -> ERROR: session id not captured from turn 1 stderr" | tee -a "$LOG_DIR/timeline.txt"
    echo "label=$label exit=99 elapsed=0s reason=missing-session-id" > "$meta"
    return 99
  fi

  echo "  turn1: exit=$turn1_exit session=$sid" | tee -a "$LOG_DIR/timeline.txt"
  echo "----" >> "$out"
  echo "SESSION_ID: $sid" >> "$out"

  # Turn 2: resume the session and ask the follow-up
  /usr/bin/time -p dotnet run --project src/BlueCode.Cli -- --verbose --model 122b --resume "$sid" "$followup_prompt" >> "$out" 2>&1
  local turn2_exit=$?
  local end_ts=$(date +%s)
  local elapsed=$((end_ts - start_ts))

  # Combined exit code: max of the two turns (worst-case)
  local combined_exit=$turn1_exit
  if [ "$turn2_exit" -gt "$combined_exit" ]; then
    combined_exit=$turn2_exit
  fi

  echo "label=$label model=122b exit=$combined_exit elapsed=${elapsed}s turn1_exit=$turn1_exit turn2_exit=$turn2_exit session=$sid" > "$meta"
  echo "  turn2: exit=$turn2_exit  combined exit=$combined_exit elapsed=${elapsed}s" | tee -a "$LOG_DIR/timeline.txt"
}

# ---------------------------------------------------------------------------
# gate() — Regression gate: 7-invocation subset, diff against bench/baseline.json
# Added by Plan 10-02 (BENCH-04). Exits 0 on pass, 1 on regression, 2 on setup error.
# Phase 19 (19-02): gate set reduced from 8 to 6 — all _122b labels.
# Phase 16-03: gate extended from 6 to 7 — added MT_122b multi-turn fixture.
# Verdict logic (3-branch):
#   1. is_regression = true  → PASS (known regression; always PASS)
#   2. actual_steps > baseline_max → FAIL (step-count regression)
#   3. baseline.pass = true AND actual_exit != 0 → FAIL (unexpected error exit)
#   default: PASS
# ---------------------------------------------------------------------------
gate() {
  local LOG_DIR="bench/runs/gate-$(date +%Y%m%d-%H%M%S)"
  mkdir -p "$LOG_DIR"
  local BASELINE="$(dirname "$0")/baseline.json"

  if [ ! -f "$BASELINE" ]; then
    echo "ERROR: baseline missing at $BASELINE" >&2
    exit 2
  fi

  # Pre-condition check: port 8001 must be responsive (122B service).
  if ! curl -fsS http://127.0.0.1:8001/v1/models > /dev/null 2>&1; then
    echo "ERROR: port 8001 is NOT responsive — 122B service is unreachable. Aborting." >&2
    echo "       To start: launchctl kickstart -k gui/\$(id -u)/com.ohama.qwen122b" >&2
    exit 2
  fi
  echo "Pre-condition OK: port 8001 (122B) responsive." | tee -a "$LOG_DIR/timeline.txt"

  echo "===== GATE: regression subset (7 invocations) =====" | tee -a "$LOG_DIR/timeline.txt"

  # 1. T6 x 122B
  run "gate_T6_122b" "122b" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"

  # 2. W1 x 122B (with fixture restore)
  cat > bench/fixtures/bug_lastchar.fs <<'EOF'
module LastChar

/// Returns the last character of a string.
let getLastChar (s: string) : char =
    s.[s.Length]
EOF
  run "gate_W1_122b" "122b" "Read bench/fixtures/bug_lastchar.fs and fix the bug. Save the corrected version using write_file."
  cp bench/fixtures/bug_lastchar.fs "$LOG_DIR/gate_W1_122b_result.fs" 2>/dev/null || true

  # 3. W2 x 122B (with fixture restore)
  cat > bench/fixtures/bug_average.fs <<'EOF'
module Average

let average (xs: int list) : int =
    (List.sum xs) / (List.length xs)
EOF
  run "gate_W2_122b" "122b" "Read bench/fixtures/bug_average.fs and add a new function averageSafe that returns int option (None for empty list). Save the updated file."
  cp bench/fixtures/bug_average.fs "$LOG_DIR/gate_W2_122b_result.fs" 2>/dev/null || true

  # 4. T1 canary x 122B
  run "gate_T1_122b" "122b" "What is 2 to the power of 10? Answer with just the number."

  # 5. T5 canary x 122B
  run "gate_T5_122b" "122b" "Find BlueCode.slnx and tell me its size in bytes using wc."

  # 6. B2 x 122B (diagnose-only, regression-tracked)
  run "gate_B2_122b" "122b" "Read bench/fixtures/bug_divide_zero.fs and identify the bug. Be specific about what input triggers it."

  # 7. MT x 122B (multi-turn persistence fixture, Phase 16-03)
  mt "gate_MT_122b"

  # ----- Compare each invocation against baseline.json -----
  echo "===== GATE: compare to baseline =====" | tee -a "$LOG_DIR/timeline.txt"
  local fail_count=0
  local pass_count=0
  local labels="T6_122b W1_122b W2_122b T1_122b T5_122b B2_122b MT_122b"

  for key in $labels; do
    local logfile="$LOG_DIR/gate_${key}.log"
    local metafile="$LOG_DIR/gate_${key}.meta"

    # Parse step count (default to 0 if absent — protects against bash 3.2 arithmetic-on-empty)
    local actual_steps
    actual_steps=$(grep -E "\[INF\] Session (ok|error)" "$logfile" 2>/dev/null | grep -o "[0-9]* steps" | grep -o "[0-9]*" | head -1)
    actual_steps=${actual_steps:-0}

    # Parse exit code from meta
    local actual_exit
    actual_exit=$(grep -o "exit=[0-9]*" "$metafile" 2>/dev/null | grep -o "[0-9]*" | head -1)
    actual_exit=${actual_exit:-99}

    # Pull baseline thresholds via jq (flat top-level keys, no tests.* wrapper)
    local baseline_max
    baseline_max=$(jq -r ".${key}.step_count_max" "$BASELINE")
    local baseline_pass
    baseline_pass=$(jq -r ".${key}.pass" "$BASELINE")
    local is_regression
    is_regression=$(jq -r ".${key}.regression // false" "$BASELINE")

    # Decision: verdict logic (3-branch form per Plan 10-02)
    local verdict="PASS"
    local reason=""
    if [ "$is_regression" = "true" ]; then
      verdict="PASS"
      reason="known regression"
    elif [ "$actual_steps" -gt "$baseline_max" ]; then
      verdict="FAIL"
      reason="steps=$actual_steps > baseline_max=$baseline_max"
    elif [ "$baseline_pass" = "true" ] && [ "$actual_exit" -ne 0 ]; then
      verdict="FAIL"
      reason="exit=$actual_exit but baseline expects pass"
    fi

    if [ "$verdict" = "PASS" ]; then
      pass_count=$((pass_count + 1))
      printf "  PASS %-10s steps=%s/%s exit=%s\n" "$key" "$actual_steps" "$baseline_max" "$actual_exit" | tee -a "$LOG_DIR/timeline.txt"
    else
      fail_count=$((fail_count + 1))
      printf "  FAIL %-10s steps=%s/%s exit=%s — %s\n" "$key" "$actual_steps" "$baseline_max" "$actual_exit" "$reason" | tee -a "$LOG_DIR/timeline.txt"
    fi
  done

  # ----- Verdict line -----
  local total=7
  if [ "$fail_count" -eq 0 ]; then
    echo "===== GATE PASS (${pass_count}/${total}) =====" | tee -a "$LOG_DIR/timeline.txt"
    exit 0
  else
    echo "===== GATE FAIL (${fail_count}/${total} regressed) =====" | tee -a "$LOG_DIR/timeline.txt"
    exit 1
  fi
}

# ---------------------------------------------------------------------------
# phase_variance() — T1 + T6 variance, 3 runs each × 122B (6 runs)
# ---------------------------------------------------------------------------
phase_variance() {
  echo "######## VARIANCE: T1 + T6, 3 runs each x 122B ########" | tee -a "$LOG_DIR/timeline.txt"
  for i in 1 2 3; do
    run "variance_T1_122b_run${i}" "122b" "What is 2 to the power of 10? Answer with just the number."
  done
  for i in 1 2 3; do
    run "variance_T6_122b_run${i}" "122b" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  done
}

# ---------------------------------------------------------------------------
# phase_diagnose() — Bug diagnose tests: B1, B2
# ---------------------------------------------------------------------------
phase_diagnose() {
  echo "######## DIAGNOSE: B1 + B2 x 122B ########" | tee -a "$LOG_DIR/timeline.txt"
  run "diagnose_B1_122b" "122b" "Read bench/fixtures/bug_lastchar.fs and identify the bug. Be specific about what triggers it."
  run "diagnose_B2_122b" "122b" "Read bench/fixtures/bug_divide_zero.fs and identify the bug. Be specific about what input triggers it."
}

# ---------------------------------------------------------------------------
# phase_write() — Write tasks: W1 + W2 × 122B with fixture restore (2 runs)
# W1 prompt deliberately names "write_file" to validate the 09.1-05 loop injection mechanism.
# ---------------------------------------------------------------------------
phase_write() {
  echo "######## WRITE TASKS: W1 + W2 x 122B ########" | tee -a "$LOG_DIR/timeline.txt"
  # Restore fresh W1 fixture before run
  cat > bench/fixtures/bug_lastchar.fs <<'EOF'
module LastChar

/// Returns the last character of a string.
let getLastChar (s: string) : char =
    s.[s.Length]
EOF
  run "write_W1_122b" "122b" "Read bench/fixtures/bug_lastchar.fs and fix the bug. Save the corrected version using write_file."
  cp bench/fixtures/bug_lastchar.fs "$LOG_DIR/write_W1_122b_result.fs" 2>/dev/null || true

  # Restore fresh W2 fixture before run
  cat > bench/fixtures/bug_average.fs <<'EOF'
module Average

let average (xs: int list) : int =
    (List.sum xs) / (List.length xs)
EOF
  run "write_W2_122b" "122b" "Read bench/fixtures/bug_average.fs and add a new function averageSafe that returns int option (None for empty list). Save the updated file."
  cp bench/fixtures/bug_average.fs "$LOG_DIR/write_W2_122b_result.fs" 2>/dev/null || true
}

# ---------------------------------------------------------------------------
# all_mode() — Full re-bench: regression + variance + diagnose + write (~25 min)
# ---------------------------------------------------------------------------
all_mode() {
  echo "######## ALL: full re-bench (122B single-model) ########" | tee -a "$LOG_DIR/timeline.txt"
  regression
  phase_variance
  phase_diagnose
  phase_write
}

# ---------------------------------------------------------------------------
# Main dispatcher — mode-flag dispatch
# ---------------------------------------------------------------------------
case "${1:-}" in
  --gate)       gate ;;
  --regression) regression ;;
  --canary)     canary ;;
  --all)        all_mode ;;
  --b2)         b2_mode ;;
  --help|-h|"") show_help; exit 0 ;;
  *)            echo "error: unknown mode '$1'"; show_help; exit 1 ;;
esac

echo "===== RUN COMPLETE =====" | tee -a "$LOG_DIR/timeline.txt"
