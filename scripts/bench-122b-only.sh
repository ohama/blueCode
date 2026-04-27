#!/bin/bash
# scripts/bench-122b-only.sh — Phase 18 122B-only bench harness
#
# Routes EVERY blueCode invocation to --model 72b (port 8001 = 122B alone).
# 35B must be unloaded BEFORE running this script (verify: launchctl list | grep ohama -> only qwen122b).
#
# Usage: scripts/bench-122b-only.sh <mode>
#   --all         Full bench: regression + variance + diagnose + write (~31 invocations, ~25-35 min)
#   --regression  T1-T7 (7 invocations, all to 122B)
#   --variance    T1 x 3 + T6 x 3 (6 invocations)
#   --diagnose    B1 + B2 (2 invocations)
#   --write       W1 + W2 (2 invocations)
#   --canary      T1 + T5 + T6x2 (4 invocations, ~3 min)
#   --b2          B2 only (1 invocation)
#   --help, -h    Show this message
#
# Outputs: bench/runs/122b-only-<timestamp>/<label>.{log,meta} + timeline.txt
# Phase 17 baseline reference: bench/baseline.json (122B columns: T6_122b, B2_122b, T5_122b).

set -u

# Auto-reset W1/W2 write-task fixtures on exit (success, failure, or Ctrl-C).
# bug_divide_zero.fs is read-only by design (B2 diagnose); do NOT include it here.
trap 'git checkout -- bench/fixtures/bug_lastchar.fs bench/fixtures/bug_average.fs 2>/dev/null || true' EXIT
cd /Users/ohama/projs/blueCode

command -v jq >/dev/null 2>&1 || { echo "jq required but not found (used for /v1/models check)"; exit 1; }

# Pre-condition check: port 8000 should be DEAD (35B unloaded), port 8001 should be ALIVE (122B).
echo "===== bench-122b-only.sh — pre-condition checks ====="
if curl -fsS http://127.0.0.1:8000/v1/models > /dev/null 2>&1; then
  echo "WARNING: port 8000 is responsive — is 35B still loaded? Phase 18 expects single-model 122B-only."
  echo "         Continuing anyway, but bench labels say '122b-only' which would be misleading."
  echo "         To unload 35B: launchctl unload ~/Library/LaunchAgents/com.ohama.qwen35b.plist"
fi
if ! curl -fsS http://127.0.0.1:8001/v1/models > /dev/null 2>&1; then
  echo "ERROR: port 8001 is NOT responsive — 122B is unreachable. Aborting."
  exit 2
fi
echo "Pre-conditions OK: port 8001 (122B) responsive."
echo

MODEL="72b"   # routes to port 8001 (122B alone) via Router.modelToEndpoint
LOG_DIR="bench/runs/122b-only-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$LOG_DIR"
echo "Output: $LOG_DIR"
echo "Estimated wall-clock: ~25-35 min for --all mode (122B is ~2-3x slower than 35B)."
echo "Avoid starting memory-heavy workloads (Chrome, Xcode) during the run."
echo

# ---------------------------------------------------------------------------
# run() — core invocation helper (lifted verbatim from bench/run.sh lines 30-46)
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
# regression_122b() — T1-T7 routed to 122B (mirrors bench/run.sh regression(), single-model)
# 7 invocations.
# ---------------------------------------------------------------------------
regression_122b() {
  echo "######## REGRESSION (122B-only): T1-T7 ########" | tee -a "$LOG_DIR/timeline.txt"
  run "regression_T1_122b" "$MODEL" "What is 2 to the power of 10? Answer with just the number."
  run "regression_T2_122b" "$MODEL" "In F#, what does the forward pipe operator |> do? One short sentence."
  run "regression_T3_122b" "$MODEL" "List the files in src/BlueCode.Core and count them."
  run "regression_T4_122b" "$MODEL" "Read src/BlueCode.Core/Router.fs and explain what classifyIntent does in one sentence."
  run "regression_T5_122b" "$MODEL" "Find BlueCode.slnx and tell me its size in bytes using wc."
  run "regression_T6_122b" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  run "regression_T7_122b" "$MODEL" "Read src/BlueCode.Core/ContextBuffer.fs and explain if the ring buffer has any edge case issue when capacity equals 1."
}

# ---------------------------------------------------------------------------
# variance_122b() — T1 + T6 x 3 each (6 invocations)
# ---------------------------------------------------------------------------
variance_122b() {
  echo "######## VARIANCE (122B-only): T1 + T6, 3 runs each ########" | tee -a "$LOG_DIR/timeline.txt"
  run "variance_T1_122b_run1" "$MODEL" "What is 2 to the power of 10? Answer with just the number."
  run "variance_T1_122b_run2" "$MODEL" "What is 2 to the power of 10? Answer with just the number."
  run "variance_T1_122b_run3" "$MODEL" "What is 2 to the power of 10? Answer with just the number."
  run "variance_T6_122b_run1" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  run "variance_T6_122b_run2" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  run "variance_T6_122b_run3" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
}

# ---------------------------------------------------------------------------
# diagnose_122b() — B1 + B2 (2 invocations)
# ---------------------------------------------------------------------------
diagnose_122b() {
  echo "######## DIAGNOSE (122B-only): B1 + B2 ########" | tee -a "$LOG_DIR/timeline.txt"
  run "diagnose_B1_122b" "$MODEL" "Read bench/fixtures/bug_lastchar.fs and identify the bug. Be specific about what triggers it."
  run "diagnose_B2_122b" "$MODEL" "Read bench/fixtures/bug_divide_zero.fs and identify the bug. Be specific about what input triggers it."
}

# ---------------------------------------------------------------------------
# write_122b() — W1 + W2 with fixture restore (2 invocations)
# Same fixture-restore heredocs as bench/run.sh phase_write().
# ---------------------------------------------------------------------------
write_122b() {
  echo "######## WRITE (122B-only): W1 + W2 ########" | tee -a "$LOG_DIR/timeline.txt"
  cat > bench/fixtures/bug_lastchar.fs <<'EOF'
module LastChar

/// Returns the last character of a string.
let getLastChar (s: string) : char =
    s.[s.Length]
EOF
  run "write_W1_122b" "$MODEL" "Read bench/fixtures/bug_lastchar.fs and fix the bug. Save the corrected version using write_file."
  cp bench/fixtures/bug_lastchar.fs "$LOG_DIR/write_W1_122b_result.fs" 2>/dev/null || true

  cat > bench/fixtures/bug_average.fs <<'EOF'
module Average

let average (xs: int list) : int =
    (List.sum xs) / (List.length xs)
EOF
  run "write_W2_122b" "$MODEL" "Read bench/fixtures/bug_average.fs and add a new function averageSafe that returns int option (None for empty list). Save the updated file."
  cp bench/fixtures/bug_average.fs "$LOG_DIR/write_W2_122b_result.fs" 2>/dev/null || true
}

# ---------------------------------------------------------------------------
# canary_122b() — Quick smoke (4 invocations, ~3 min)
# ---------------------------------------------------------------------------
canary_122b() {
  echo "######## CANARY (122B-only): 4-run smoke ########" | tee -a "$LOG_DIR/timeline.txt"
  run "canary_T1_122b" "$MODEL" "What is 2 to the power of 10? Answer with just the number."
  run "canary_T5_122b" "$MODEL" "Find BlueCode.slnx and tell me its size in bytes using wc."
  run "canary_T6a_122b" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  run "canary_T6b_122b" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
}

# ---------------------------------------------------------------------------
# b2_only() — Single B2 invocation (~30 s)
# ---------------------------------------------------------------------------
b2_only() {
  echo "######## B2 (122B-only) ########" | tee -a "$LOG_DIR/timeline.txt"
  run "b2_122b" "$MODEL" "Read bench/fixtures/bug_divide_zero.fs and identify the bug. Be specific about what input triggers it."
}

# ---------------------------------------------------------------------------
# all_mode_122b() — Full re-bench, all invocations inlined (no loop wrappers)
# Invocation count (must hold >= 30 per ROADMAP §SC3):
#   Regression    T1..T7            =  7
#   Variance      T1 x3 + T6 x3    =  6
#   Extended var  T2 x3             =  3
#   Extended var  T7 x3             =  3
#   Extended var  T6 x3 (runs 4-6)  =  3
#   Diagnose      B1 + B2           =  2
#   Write         W1 + W2           =  2
#   Canary        T1+T5+T6a+T6b     =  4
#   B2-only                         =  1
#                                  ---
#   TOTAL                           = 31  (>= 30)
#
# All run() calls use "$MODEL" — grep -c '"$MODEL"' >= 31 static check passes.
# ---------------------------------------------------------------------------
all_mode_122b() {
  echo "######## ALL (122B-only): full bench ########" | tee -a "$LOG_DIR/timeline.txt"
  echo "Estimated 31 invocations, ~25-35 min wall-clock." | tee -a "$LOG_DIR/timeline.txt"

  # --- Regression: T1-T7 (7 invocations) ---
  echo "######## REGRESSION (122B-only): T1-T7 ########" | tee -a "$LOG_DIR/timeline.txt"
  run "regression_T1_122b" "$MODEL" "What is 2 to the power of 10? Answer with just the number."
  run "regression_T2_122b" "$MODEL" "In F#, what does the forward pipe operator |> do? One short sentence."
  run "regression_T3_122b" "$MODEL" "List the files in src/BlueCode.Core and count them."
  run "regression_T4_122b" "$MODEL" "Read src/BlueCode.Core/Router.fs and explain what classifyIntent does in one sentence."
  run "regression_T5_122b" "$MODEL" "Find BlueCode.slnx and tell me its size in bytes using wc."
  run "regression_T6_122b" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  run "regression_T7_122b" "$MODEL" "Read src/BlueCode.Core/ContextBuffer.fs and explain if the ring buffer has any edge case issue when capacity equals 1."

  # --- Variance: T1 x3 + T6 x3 (6 invocations) ---
  echo "######## VARIANCE (122B-only): T1 + T6, 3 runs each ########" | tee -a "$LOG_DIR/timeline.txt"
  run "variance_T1_122b_run1" "$MODEL" "What is 2 to the power of 10? Answer with just the number."
  run "variance_T1_122b_run2" "$MODEL" "What is 2 to the power of 10? Answer with just the number."
  run "variance_T1_122b_run3" "$MODEL" "What is 2 to the power of 10? Answer with just the number."
  run "variance_T6_122b_run1" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  run "variance_T6_122b_run2" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  run "variance_T6_122b_run3" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"

  # --- Extended variance: T2 x3 (3 invocations) ---
  run "variance_T2_122b_run1" "$MODEL" "In F#, what does the forward pipe operator |> do? One short sentence."
  run "variance_T2_122b_run2" "$MODEL" "In F#, what does the forward pipe operator |> do? One short sentence."
  run "variance_T2_122b_run3" "$MODEL" "In F#, what does the forward pipe operator |> do? One short sentence."

  # --- Extended variance: T7 x3 (3 invocations) ---
  run "variance_T7_122b_run1" "$MODEL" "Read src/BlueCode.Core/ContextBuffer.fs and explain if the ring buffer has any edge case issue when capacity equals 1."
  run "variance_T7_122b_run2" "$MODEL" "Read src/BlueCode.Core/ContextBuffer.fs and explain if the ring buffer has any edge case issue when capacity equals 1."
  run "variance_T7_122b_run3" "$MODEL" "Read src/BlueCode.Core/ContextBuffer.fs and explain if the ring buffer has any edge case issue when capacity equals 1."

  # --- Extended variance: T6 runs 4-6 (3 invocations — most-informative step-count signal) ---
  run "variance_T6_122b_run4" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  run "variance_T6_122b_run5" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  run "variance_T6_122b_run6" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"

  # --- Diagnose: B1 + B2 (2 invocations) ---
  echo "######## DIAGNOSE (122B-only): B1 + B2 ########" | tee -a "$LOG_DIR/timeline.txt"
  run "diagnose_B1_122b" "$MODEL" "Read bench/fixtures/bug_lastchar.fs and identify the bug. Be specific about what triggers it."
  run "diagnose_B2_122b" "$MODEL" "Read bench/fixtures/bug_divide_zero.fs and identify the bug. Be specific about what input triggers it."

  # --- Write: W1 + W2 with fixture restore (2 invocations) ---
  echo "######## WRITE (122B-only): W1 + W2 ########" | tee -a "$LOG_DIR/timeline.txt"
  cat > bench/fixtures/bug_lastchar.fs <<'EOF'
module LastChar

/// Returns the last character of a string.
let getLastChar (s: string) : char =
    s.[s.Length]
EOF
  run "write_W1_122b" "$MODEL" "Read bench/fixtures/bug_lastchar.fs and fix the bug. Save the corrected version using write_file."
  cp bench/fixtures/bug_lastchar.fs "$LOG_DIR/write_W1_122b_result.fs" 2>/dev/null || true

  cat > bench/fixtures/bug_average.fs <<'EOF'
module Average

let average (xs: int list) : int =
    (List.sum xs) / (List.length xs)
EOF
  run "write_W2_122b" "$MODEL" "Read bench/fixtures/bug_average.fs and add a new function averageSafe that returns int option (None for empty list). Save the updated file."
  cp bench/fixtures/bug_average.fs "$LOG_DIR/write_W2_122b_result.fs" 2>/dev/null || true

  # --- Canary: T1 + T5 + T6a + T6b (4 invocations) ---
  echo "######## CANARY (122B-only): 4-run smoke ########" | tee -a "$LOG_DIR/timeline.txt"
  run "canary_T1_122b" "$MODEL" "What is 2 to the power of 10? Answer with just the number."
  run "canary_T5_122b" "$MODEL" "Find BlueCode.slnx and tell me its size in bytes using wc."
  run "canary_T6a_122b" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"
  run "canary_T6b_122b" "$MODEL" "What are the field names in the Step record in src/BlueCode.Core/Domain.fs?"

  # --- B2-only (1 invocation) ---
  echo "######## B2 (122B-only) ########" | tee -a "$LOG_DIR/timeline.txt"
  run "b2_122b" "$MODEL" "Read bench/fixtures/bug_divide_zero.fs and identify the bug. Be specific about what input triggers it."
}

# ---------------------------------------------------------------------------
# Main dispatcher
# ---------------------------------------------------------------------------
case "${1:-}" in
  --all)        all_mode_122b ;;
  --regression) regression_122b ;;
  --variance)   variance_122b ;;
  --diagnose)   diagnose_122b ;;
  --write)      write_122b ;;
  --canary)     canary_122b ;;
  --b2)         b2_only ;;
  --help|-h|"") sed -n '2,17p' "$0"; exit 0 ;;
  *)            echo "error: unknown mode '$1'"; sed -n '2,17p' "$0"; exit 1 ;;
esac

echo "===== RUN COMPLETE — $LOG_DIR =====" | tee -a "$LOG_DIR/timeline.txt"
