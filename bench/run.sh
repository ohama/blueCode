#!/bin/bash
# bench/run.sh — blueCode regression harness
# Lifted from /tmp/bench-v1.2/run.sh (v1.2 ephemeral harness) and formalized.
# Each test produces bench/runs/<timestamp>/<label>.{log,meta} + timeline.txt
#
# Usage: bench/run.sh <mode>
#   --gate        Regression gate (8 invocations, ~2 min); exits non-zero on regression
#   --regression  Part 1 reproducibility: T1-T7 x 32B + 72B (14 runs, ~6 min)
#   --canary      Quick smoke: T1, T5, T6x2 (4 runs, ~1.5 min)
#   --b2          B2 divide-by-zero diagnose only (2 runs, ~30 s)
#   --all         Full re-bench: regression + variance + write tests (~25 min)
#   --help, -h    Show this message

set -u
cd /Users/ohama/projs/blueCode

command -v jq >/dev/null 2>&1 || { echo "jq required but not found"; exit 1; }

LOG_DIR="bench/runs/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$LOG_DIR"

# ---------------------------------------------------------------------------
# run() — core invocation helper (lifted verbatim from v1.2 run.sh lines 10-26)
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
bench/run.sh — blueCode regression harness

Usage: bench/run.sh <mode>

Modes:
  --gate        Regression gate (8 invocations, ~2 min); exits non-zero on regression
  --regression  Part 1 reproducibility: T1–T7 × 32B + 72B (14 runs, ~6 min)
  --canary      Quick smoke: T1, T5, T6×2 (4 runs, ~1.5 min)
  --b2          B2 divide-by-zero diagnose only (2 runs, ~30 s)
  --all         Full re-bench: regression + variance + write tests (~25 min)
  --help, -h    Show this message

Logs land in bench/runs/<timestamp>/ (gitignored). Baseline lives at bench/baseline.json.
HELP
}

# ---------------------------------------------------------------------------
# regression() — Part 1: T1–T7 × 32B + 72B (14 runs)
# Exact equivalent of v1.2 phase1()
# ---------------------------------------------------------------------------
regression() {
  echo "######## REGRESSION: 7 main tests x 2 models ########" | tee -a "$LOG_DIR/timeline.txt"
  for model in 32b 72b; do
    run "regression_T1_${model}" "$model" "What is 2 to the power of 10? Answer with just the number."
    run "regression_T2_${model}" "$model" "In F#, what does the forward pipe operator |> do? One short sentence."
    run "regression_T3_${model}" "$model" "List the files in src/BlueCode.Core and count them."
    run "regression_T4_${model}" "$model" "Read src/BlueCode.Core/Router.fs and explain what classifyIntent does in one sentence."
    run "regression_T5_${model}" "$model" "Find BlueCode.slnx and tell me its size in bytes using wc."
    run "regression_T6_${model}" "$model" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
    run "regression_T7_${model}" "$model" "Read src/BlueCode.Core/ContextBuffer.fs and explain if the ring buffer has any edge case issue when capacity equals 1."
  done
}

# ---------------------------------------------------------------------------
# canary() — Quick smoke: 4 invocations (~1.5 min)
# ---------------------------------------------------------------------------
canary() {
  echo "######## CANARY: 4-run smoke test ########" | tee -a "$LOG_DIR/timeline.txt"
  run "canary_T1_32b" "32b" "What is 2 to the power of 10? Answer with just the number."
  run "canary_T5_72b" "72b" "Find BlueCode.slnx and tell me its size in bytes using wc."
  run "canary_T6_32b" "32b" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  run "canary_T6_72b" "72b" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
}

# ---------------------------------------------------------------------------
# b2_mode() — B2 divide-by-zero diagnose only (2 runs)
# Uses the new bench/fixtures/bug_divide_zero.fs (separate from W2's bug_average.fs).
# Prompt does NOT name a tool (per BENCH-05 / 09.1-04 guidance).
# ---------------------------------------------------------------------------
b2_mode() {
  echo "######## B2: divide-by-zero diagnose (2 runs) ########" | tee -a "$LOG_DIR/timeline.txt"
  for model in 32b 72b; do
    run "b2_${model}" "$model" "Read bench/fixtures/bug_divide_zero.fs and identify the bug. Be specific about what input triggers it."
  done
}

# ---------------------------------------------------------------------------
# gate() — Regression gate: 8-invocation subset, diff against bench/baseline.json
# Added by Plan 10-02 (BENCH-04). Exits 0 on pass, 1 on regression, 2 on setup error.
# Verdict logic (3-branch):
#   1. is_regression = true  → PASS (known regression: B2_32b, B2_72b; PERF-03 target)
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

  echo "===== GATE: regression subset (8 invocations) =====" | tee -a "$LOG_DIR/timeline.txt"

  # 1. T6 x 32B (single run)
  run "gate_T6_32b" "32b" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"

  # 2. T6 x 72B (single run)
  run "gate_T6_72b" "72b" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"

  # 3. W1 x 32B (with fixture restore)
  cat > bench/fixtures/bug_lastchar.fs <<'EOF'
module LastChar

/// Returns the last character of a string.
let getLastChar (s: string) : char =
    s.[s.Length]
EOF
  run "gate_W1_32b" "32b" "Read bench/fixtures/bug_lastchar.fs and fix the bug. Save the corrected version using write_file."
  cp bench/fixtures/bug_lastchar.fs "$LOG_DIR/gate_W1_32b_result.fs" 2>/dev/null || true

  # 4. W2 x 32B (with fixture restore)
  cat > bench/fixtures/bug_average.fs <<'EOF'
module Average

let average (xs: int list) : int =
    (List.sum xs) / (List.length xs)
EOF
  run "gate_W2_32b" "32b" "Read bench/fixtures/bug_average.fs and add a new function averageSafe that returns int option (None for empty list). Save the updated file."
  cp bench/fixtures/bug_average.fs "$LOG_DIR/gate_W2_32b_result.fs" 2>/dev/null || true

  # 5. T1 canary x 32B
  run "gate_T1_32b" "32b" "What is 2 to the power of 10? Answer with just the number."

  # 6. T5 canary x 72B
  run "gate_T5_72b" "72b" "Find BlueCode.slnx and tell me its size in bytes using wc."

  # 7. B2 x 32B (diagnose-only, regression-tracked)
  run "gate_B2_32b" "32b" "Read bench/fixtures/bug_divide_zero.fs and identify the bug. Be specific about what input triggers it."

  # 8. B2 x 72B (diagnose-only, regression-tracked)
  run "gate_B2_72b" "72b" "Read bench/fixtures/bug_divide_zero.fs and identify the bug. Be specific about what input triggers it."

  # ----- Compare each invocation against baseline.json -----
  echo "===== GATE: compare to baseline =====" | tee -a "$LOG_DIR/timeline.txt"
  local fail_count=0
  local pass_count=0
  local labels="T6_32b T6_72b W1_32b W2_32b T1_32b T5_72b B2_32b B2_72b"

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

    # Pull baseline thresholds via jq
    local baseline_max
    baseline_max=$(jq -r ".tests.${key}.step_count_max" "$BASELINE")
    local baseline_pass
    baseline_pass=$(jq -r ".tests.${key}.pass" "$BASELINE")
    local is_regression
    is_regression=$(jq -r ".tests.${key}.regression // false" "$BASELINE")

    # Decision: verdict logic (3-branch form per Plan 10-02)
    #   - is_regression = true: known regression (B2_32b, B2_72b). Always PASS.
    #     The gate cannot detect *quality* of an answer from logs alone — only step-count
    #     and exit-code drift. PERF-03 will inspect B2 log output and update baseline.json.
    #   - actual_steps > baseline_max: FAIL (step-count regression).
    #   - baseline.pass = true AND actual_exit != 0: FAIL (unexpected error).
    #   - default: PASS.
    local verdict="PASS"
    local reason=""
    if [ "$is_regression" = "true" ]; then
      # Known regression marked in baseline.json — gate treats as PASS until
      # PERF-03 updates baseline.json. Operator manually inspects answer quality.
      verdict="PASS"
      reason="known regression (PERF-03 target)"
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
  local total=8
  if [ "$fail_count" -eq 0 ]; then
    echo "===== GATE PASS (${pass_count}/${total}) =====" | tee -a "$LOG_DIR/timeline.txt"
    exit 0
  else
    echo "===== GATE FAIL (${fail_count}/${total} regressed) =====" | tee -a "$LOG_DIR/timeline.txt"
    exit 1
  fi
}

# ---------------------------------------------------------------------------
# phase_variance() — T1 + T6 variance, 3 runs each × 2 models (12 runs)
# Equivalent to v1.2 phaseA()
# ---------------------------------------------------------------------------
phase_variance() {
  echo "######## VARIANCE: T1 + T6, 3 runs each x 2 models ########" | tee -a "$LOG_DIR/timeline.txt"
  for model in 32b 72b; do
    for i in 1 2 3; do
      run "variance_T1_${model}_run${i}" "$model" "What is 2 to the power of 10? Answer with just the number."
    done
    for i in 1 2 3; do
      run "variance_T6_${model}_run${i}" "$model" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
    done
  done
}

# ---------------------------------------------------------------------------
# phase_diagnose() — Bug diagnose tests: B1, B2 (B3 fixture not yet created)
# Equivalent to v1.2 phaseB() minus B3
# ---------------------------------------------------------------------------
phase_diagnose() {
  echo "######## DIAGNOSE: B1 + B2 x 2 models ########" | tee -a "$LOG_DIR/timeline.txt"
  for model in 32b 72b; do
    run "diagnose_B1_${model}" "$model" "Read bench/fixtures/bug_lastchar.fs and identify the bug. Be specific about what triggers it."
    run "diagnose_B2_${model}" "$model" "Read bench/fixtures/bug_divide_zero.fs and identify the bug. Be specific about what input triggers it."
    # TODO(v1.4): create bug_validate.fs fixture
    # run "diagnose_B3_${model}" "$model" "Read bench/fixtures/bug_validate.fs and identify the bug. Be specific about what triggers it."
  done
}

# ---------------------------------------------------------------------------
# phase_write() — Write tasks: W1 + W2 × 2 models with fixture restore (4 runs)
# Equivalent to v1.2 phaseC()
# W1 prompt deliberately names "write_file" to validate the 09.1-05 loop injection mechanism.
# ---------------------------------------------------------------------------
phase_write() {
  echo "######## WRITE TASKS: W1 + W2 x 2 models ########" | tee -a "$LOG_DIR/timeline.txt"
  for model in 32b 72b; do
    # Restore fresh W1 fixture before each run
    cat > bench/fixtures/bug_lastchar.fs <<'EOF'
module LastChar

/// Returns the last character of a string.
let getLastChar (s: string) : char =
    s.[s.Length]
EOF
    run "write_W1_${model}" "$model" "Read bench/fixtures/bug_lastchar.fs and fix the bug. Save the corrected version using write_file."
    cp bench/fixtures/bug_lastchar.fs "$LOG_DIR/write_W1_${model}_result.fs" 2>/dev/null || true

    # Restore fresh W2 fixture before each run
    cat > bench/fixtures/bug_average.fs <<'EOF'
module Average

let average (xs: int list) : int =
    (List.sum xs) / (List.length xs)
EOF
    run "write_W2_${model}" "$model" "Read bench/fixtures/bug_average.fs and add a new function averageSafe that returns int option (None for empty list). Save the updated file."
    cp bench/fixtures/bug_average.fs "$LOG_DIR/write_W2_${model}_result.fs" 2>/dev/null || true
  done
}

# ---------------------------------------------------------------------------
# all_mode() — Full re-bench: regression + variance + diagnose + write (~25 min)
# Equivalent to running all v1.2 phases together.
# ---------------------------------------------------------------------------
all_mode() {
  echo "######## ALL: full re-bench ########" | tee -a "$LOG_DIR/timeline.txt"
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
