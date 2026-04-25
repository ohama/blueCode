#!/bin/bash
# bench/run.sh — blueCode regression harness
# Lifted from /tmp/bench-v1.2/run.sh (v1.2 ephemeral harness) and formalized.
# Each test produces bench/runs/<timestamp>/<label>.{log,meta} + timeline.txt
#
# Usage: bench/run.sh <mode>
#   --regression  Part 1 reproducibility: T1-T7 x 32B + 72B (14 runs, ~6 min)
#   --canary      Quick smoke: T1, T5, T6x2 (4 runs, ~1.5 min)
#   --b2          B2 divide-by-zero diagnose only (2 runs, ~30 s)
#   --all         Full re-bench: regression + variance + write tests (~25 min)
#   --help, -h    Show this message
#
# Note: --gate mode is added by Plan 10-02.

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
  --regression  Part 1 reproducibility: T1–T7 × 32B + 72B (14 runs, ~6 min)
  --canary      Quick smoke: T1, T5, T6×2 (4 runs, ~1.5 min)
  --b2          B2 divide-by-zero diagnose only (2 runs, ~30 s)
  --all         Full re-bench: regression + variance + write tests (~25 min)
  --help, -h    Show this message

Note: --gate mode is added by Plan 10-02. It will be the canonical CI/pre-commit entry.

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
  --regression) regression ;;
  --canary)     canary ;;
  --all)        all_mode ;;
  --b2)         b2_mode ;;
  --help|-h|"") show_help; exit 0 ;;
  *)            echo "error: unknown mode '$1'"; show_help; exit 1 ;;
esac

echo "===== RUN COMPLETE =====" | tee -a "$LOG_DIR/timeline.txt"
