#!/usr/bin/env bash
# bench/eval-qwen35-122b.sh — Qwen 3.5 122B empirical evaluation harness (Phase 21, v2.1)
#
# Measures: throughput (tok/s), TTFT (ms), HumanEval+, multi-turn, refactor,
#           lang-coverage, schema-rate, needle, cold-start. All against HTTP
#           localhost:8001 — NEVER loads mlx_lm in-process (OOM risk, 45GB resident).
#
# Usage: bench/eval-qwen35-122b.sh [--setup|--throughput|--ttft|--multiturn|
#            --refactor|--langcoverage|--schema-rate|--humaneval|--needle|
#            --coldstart|--full]
#
# Handlers implemented in this file (21-01):
#   --setup, --throughput, --ttft
# Handlers implemented by 21-02:
#   --humaneval
# Handlers implemented by 21-03:
#   --refactor, --langcoverage
# Handlers implemented by 21-04:
#   --multiturn, --schema-rate, --needle, --coldstart, --full

set -euo pipefail
cd /Users/ohama/projs/blueCode

# ---------------------------------------------------------------------------
# Pre-conditions
# ---------------------------------------------------------------------------
command -v jq >/dev/null 2>&1 || { echo "jq required but not found"; exit 1; }

# ---------------------------------------------------------------------------
# Global constants
# ---------------------------------------------------------------------------
LOG_DIR="bench/runs/qwen35-eval-$(date +%Y%m%d-%H%M%S)"
VENV_DIR="bench/.venv-eval"
VENV_PY="$VENV_DIR/bin/python"
ENDPOINT="http://127.0.0.1:8001"
MODEL_PATH="/Users/ohama/llm-system/models/qwen122b"

# ---------------------------------------------------------------------------
# require_port_8001 — pre-condition: 122B service must be responsive
# (adapted from bench/run.sh:181-186)
# ---------------------------------------------------------------------------
require_port_8001() {
  if ! curl -fsS "$ENDPOINT/v1/models" > /dev/null 2>&1; then
    echo "ERROR: port 8001 is NOT responsive — 122B service unreachable. Aborting." >&2
    echo "       To start: launchctl kickstart -k gui/\$(id -u)/com.ohama.qwen122b" >&2
    exit 2
  fi
}

# ---------------------------------------------------------------------------
# curl_run <label> <prompt> <max_tokens>
# POSTs to /v1/chat/completions, captures usage.completion_tokens + wall-clock,
# emits one-line JSON {label, prompt, completion_tokens, elapsed_ms, tokens_per_sec}
# (adapted from bench/run.sh:30-46 run() for HTTP-direct timing)
# ---------------------------------------------------------------------------
curl_run() {
  local label="$1"
  local prompt="$2"
  local max_tokens="${3:-512}"
  local body
  body=$(jq -nc --arg p "$prompt" --arg m "$MODEL_PATH" --argjson mt "$max_tokens" \
    '{model: $m, messages: [{role: "user", content: $p}], max_tokens: $mt, temperature: 0.2, top_p: 0.8, top_k: 20}')
  local start_ns end_ns
  start_ns=$(date +%s%N)
  local resp
  resp=$(curl -fsS -X POST "$ENDPOINT/v1/chat/completions" \
    -H "Content-Type: application/json" \
    -d "$body")
  end_ns=$(date +%s%N)
  local elapsed_ms=$(( (end_ns - start_ns) / 1000000 ))
  local ct
  ct=$(echo "$resp" | jq -r '.usage.completion_tokens // 0')
  local tps
  if [ "$elapsed_ms" -gt 0 ] && [ "$ct" -gt 0 ]; then
    tps=$(awk -v c="$ct" -v ms="$elapsed_ms" 'BEGIN { printf "%.2f", c / (ms / 1000.0) }')
  else
    tps="0"
  fi
  jq -nc --arg label "$label" --arg p "$prompt" --argjson ct "$ct" \
        --argjson ms "$elapsed_ms" --arg tps "$tps" \
    '{label: $label, prompt: $p, completion_tokens: $ct, elapsed_ms: $ms, tokens_per_sec: $tps}'
}

# ---------------------------------------------------------------------------
# setup_venv — one-time: create bench/.venv-eval and install evalplus
# Falls back to uv + Python 3.12 if pip install fails (Python 3.14 compat risk)
# ---------------------------------------------------------------------------
setup_venv() {
  if [ -d "$VENV_DIR" ] && [ -x "$VENV_PY" ]; then
    echo "venv exists at $VENV_DIR; skipping creation"
  else
    # Try system Python 3 first, fall back to uv+3.12 if pip install fails
    python3 -m venv "$VENV_DIR"
  fi
  "$VENV_PY" -m pip install --upgrade pip >/dev/null
  if ! "$VENV_PY" -m pip install -r bench/requirements-eval.txt; then
    echo "pip install failed on Python $("$VENV_PY" --version); attempting uv + Python 3.12 fallback"
    rm -rf "$VENV_DIR"
    if ! command -v uv >/dev/null 2>&1; then
      echo "uv not installed; install via 'brew install uv' then re-run --setup" >&2
      exit 3
    fi
    uv venv --python 3.12 "$VENV_DIR"
    "$VENV_PY" -m pip install -r bench/requirements-eval.txt
  fi
  "$VENV_PY" -c "import evalplus; print('evalplus version:', evalplus.__version__)"
  echo "Setup complete: $VENV_DIR"
}

# ---------------------------------------------------------------------------
# run_throughput — PERF-EVAL-01: 5 prompts × 3 trials = 15 entries, max_tokens=512
# Emits throughput.json to LOG_DIR, prints median tok/s
# ---------------------------------------------------------------------------
run_throughput() {
  require_port_8001
  mkdir -p "$LOG_DIR"
  local out="$LOG_DIR/throughput.json"
  : > "$out"
  local prompts=(
    "Write a Python function that returns the nth Fibonacci number iteratively."
    "Implement a binary search function in F# that returns Some index or None."
    "Write a TypeScript function that debounces another function with a configurable delay."
    "Implement a quicksort algorithm in Python with in-place partitioning."
    "Write an F# function that reverses a linked list using pattern matching."
  )
  local trial prompt
  for trial in 1 2 3; do
    for prompt in "${prompts[@]}"; do
      local label="trial${trial}_$(echo "$prompt" | head -c 30 | tr -c 'a-zA-Z0-9' '_')"
      curl_run "$label" "$prompt" 512 >> "$out"
    done
  done
  local count
  count=$(wc -l < "$out" | tr -d ' ')
  echo "throughput: $count entries written to $out"
  local median
  median=$(jq -s 'map(.tokens_per_sec | tonumber) | sort | .[length/2 | floor]' "$out")
  echo "throughput: median tokens_per_sec = $median"
}

# ---------------------------------------------------------------------------
# run_ttft — PERF-EVAL-02: 10 trials, SSE stream, awk filter for first content chunk
# mlx_lm.server SSE format:
#   ": keepalive N/14"    (comments — skip)
#   "data: {...delta:{role:...}}"   (initial role chunk — skip; no content key)
#   "data: {...delta:{content:"..."}}}"  (first content — capture timestamp)
#   "data: [DONE]"        (terminator)
# ---------------------------------------------------------------------------
run_ttft() {
  require_port_8001
  mkdir -p "$LOG_DIR"
  local out="$LOG_DIR/ttft.json"
  : > "$out"
  local prompt="Write a Python function that computes the factorial of n iteratively. Return only the code."
  local body
  body=$(jq -nc --arg p "$prompt" --arg m "$MODEL_PATH" \
    '{model: $m, messages: [{role: "user", content: $p}], max_tokens: 64, temperature: 0.2, top_p: 0.8, top_k: 20, stream: true}')
  local trial
  for trial in $(seq 1 10); do
    # Capture nanosecond timestamp on the FIRST SSE data chunk that contains
    # non-empty delta.content. Skip the initial delta.role chunk and keepalive comments.
    local start_ns
    start_ns=$(date +%s%N)
    local ttft_ms
    # curl exits 23 (write error / broken pipe) when awk exits early after
    # capturing the first content chunk. Suppress with || true since the
    # pipe result is what matters (captured in ttft_ms via subshell).
    ttft_ms=$(curl -N -fsS -X POST "$ENDPOINT/v1/chat/completions" \
      -H "Content-Type: application/json" \
      -d "$body" 2>/dev/null \
      | awk -v start="$start_ns" '
          /^: keepalive/ { next }
          /^data: \[DONE\]/ { exit }
          /^data: / {
            line = substr($0, 7)
            if (line ~ /"content":/ && line !~ /"content":""/) {
              cmd = "date +%s%N"
              cmd | getline now_ns_str
              close(cmd)
              ttft_ns = now_ns_str - start
              printf "%d", ttft_ns / 1000000
              exit
            }
          }
        ' || true)
    if [ -z "$ttft_ms" ] || [ "$ttft_ms" = "0" ]; then
      ttft_ms="-1"
    fi
    jq -nc --argjson trial "$trial" --argjson ms "$ttft_ms" \
      '{trial: $trial, ttft_ms: $ms}' >> "$out"
    sleep 1
  done
  local count
  count=$(wc -l < "$out" | tr -d ' ')
  echo "ttft: $count entries written to $out"
  local median
  median=$(jq -s 'map(.ttft_ms) | sort | .[length/2 | floor]' "$out")
  echo "ttft: median ttft_ms = $median"
}

# ---------------------------------------------------------------------------
# Stub handlers — bodies added in 21-02/21-03/21-04 by appending to this file
# ---------------------------------------------------------------------------
run_humaneval() {
  require_port_8001
  if [ ! -x "$VENV_PY" ]; then
    echo "ERROR: $VENV_PY not found. Run: bash $0 --setup" >&2
    exit 5
  fi
  mkdir -p "$LOG_DIR"
  echo "===== HumanEval+ (chat + completion modes, 164 × 2 = 328 inferences, ~55 min) ====="
  echo "  Sampling: temp=0.2 (eval-standard per mlx_llm_eval_guide.md §8)"
  echo "  Note: differs from blueCode runtime default 0.7 — intentional (see eval doc §1)"
  echo "  Output: $LOG_DIR/humaneval_results.json"
  echo "  Per-mode jsonl: $LOG_DIR/humaneval_{chat,completion}.jsonl"
  "$VENV_PY" bench/eval-humaneval-http.py --mode both --output-dir "$LOG_DIR"
  # Post-hoc scoring via evalplus CLI for each mode
  for mode in chat completion; do
    local jsonl="$LOG_DIR/humaneval_${mode}.jsonl"
    if [ ! -s "$jsonl" ]; then
      echo "WARN: $jsonl empty or missing; skipping evalplus score for $mode" >&2
      continue
    fi
    echo "----- evalplus score: mode=$mode -----"
    # evalplus.evaluate expects {"task_id":..., "completion":...} per line
    local eval_input="$LOG_DIR/humaneval_${mode}_eval_input.jsonl"
    "$VENV_PY" - <<PYEOF
import json
src = '${jsonl}'
dst = '${eval_input}'
count = 0
with open(src) as fin, open(dst, 'w') as fout:
    for line in fin:
        rec = json.loads(line)
        fout.write(json.dumps({'task_id': rec['task_id'], 'completion': rec['completion']}) + '\n')
        count += 1
print(f'wrote {count} entries to {dst}')
PYEOF
    # Sanitize completions BEFORE evaluate.
    #   Why: evalplus.evaluate stitches prompt + completion. Our chat-mode completions already
    #   contain full function definitions (signature + docstring + body). Without sanitize, the
    #   stitched solution has a doubled signature/docstring and is unparseable → all tests fail
    #   silently with pass@1=0.000. evalplus.sanitize extracts the body cleanly so the stitched
    #   solution parses. (Diagnosed and fixed in 21-02.)
    "$VENV_PY" -m evalplus.sanitize "$eval_input" \
      || { echo "evalplus.sanitize failed for $mode" >&2; continue; }
    local sanitized="${eval_input%.jsonl}-sanitized.jsonl"
    # Run evalplus.evaluate; tee to *_score.txt so 21-05 can grep pass@1 without re-running.
    #   EVALPLUS_MAX_MEMORY_BYTES=-1 disables RLIMIT_AS setrlimit which fails on macOS
    #   (current limit exceeds maximum limit). Without it, every test process crashes pre-test.
    EVALPLUS_MAX_MEMORY_BYTES=-1 "$VENV_PY" -m evalplus.evaluate humaneval \
      --samples "$sanitized" \
      | tee "$LOG_DIR/humaneval_${mode}_score.txt" \
      || echo "evalplus.evaluate exited non-zero for $mode (check output above)" >&2
  done
  echo "humaneval: results at $LOG_DIR/humaneval_results.json"
  echo "humaneval: scores at $LOG_DIR/humaneval_{chat,completion}_score.txt"
}
run_refactor() {
  require_port_8001
  mkdir -p "$LOG_DIR"
  local fixture_dir="bench/fixtures/refactor_multifile"
  local prompt
  prompt="Read $fixture_dir/README.md and perform the refactor task it describes. Modify the files in $fixture_dir as needed. Do not modify the README.md."
  local out="$LOG_DIR/refactor_multifile_diff.txt"
  local meta="$LOG_DIR/refactor_multifile.meta"
  echo "===== refactor_multifile (model=122b) =====" | tee -a "$LOG_DIR/timeline.txt"
  echo "PROMPT: $prompt" >> "$out"
  echo "----" >> "$out"
  local start_ts
  start_ts=$(date +%s)
  /usr/bin/time -p dotnet run --project src/BlueCode.Cli -- --verbose --model 122b "$prompt" >> "$out" 2>&1
  local exit_code=$?
  local end_ts
  end_ts=$(date +%s)
  local elapsed=$((end_ts - start_ts))
  echo "----" >> "$out"
  echo "===== POST-REFACTOR FILE STATES =====" >> "$out"
  for f in Calculator.fs Main.fs Tests.fs; do
    echo "--- $fixture_dir/$f ---" >> "$out"
    cat "$fixture_dir/$f" >> "$out"
    echo "" >> "$out"
  done
  echo "===== ORPHAN-add CHECK =====" >> "$out"
  # Pass criteria: no orphan references to `add` (allowing `add3` if it became `sum3` is also fine; agent task spec is rename `add` → `sum`)
  # IMPORTANT: This check runs INSIDE run_refactor (before eval-qwen35-122b.sh exits) and BEFORE
  # bench/run.sh's EXIT trap fires later during the gate run. Once the gate's trap restores fixtures,
  # they will contain `add` again — so we MUST capture the count NOW and persist it to a file
  # 21-05 reads (refactor_orphan_count.txt) for CORR-EVAL-02 scoring (5 pts if 0; 0 pts otherwise).
  local orphan_count
  orphan_count=$(grep -cE '\b(let |Calculator\.)add\b' \
      "$fixture_dir/Calculator.fs" \
      "$fixture_dir/Main.fs" \
      "$fixture_dir/Tests.fs" 2>/dev/null | awk -F: '{sum+=$2} END {print sum+0}')
  echo "$orphan_count" > "$LOG_DIR/refactor_orphan_count.txt"
  echo "orphan_add_references=$orphan_count" >> "$out"
  if [ "$orphan_count" -gt 0 ]; then
    echo "CORR-EVAL-02 FAIL: $orphan_count orphan 'add' references remain after refactor" \
      | tee -a "$LOG_DIR/timeline.txt" "$out"
    # Do NOT exit non-zero here — we want eval-qwen35-122b.sh to continue cleanly so the
    # subsequent bench gate run can restore fixtures via its EXIT trap. 21-05 reads
    # refactor_orphan_count.txt and applies pass/fail (5 pts if 0; 0 pts otherwise).
  else
    echo "CORR-EVAL-02 PASS: 0 orphan 'add' references remain" | tee -a "$LOG_DIR/timeline.txt"
  fi
  echo "label=refactor_multifile model=122b exit=$exit_code elapsed=${elapsed}s orphan_add_refs=$orphan_count" > "$meta"
  echo "  -> exit=$exit_code elapsed=${elapsed}s orphan_add_refs=$orphan_count" | tee -a "$LOG_DIR/timeline.txt"
}
run_langcoverage() {
  require_port_8001
  mkdir -p "$LOG_DIR"
  for fixture in "bug_python_typeerror.py" "bug_typescript_async.ts" "bug_binsearch.fs"; do
    local label="${fixture%.*}_diagnose"
    local out="$LOG_DIR/${label}.log"
    local meta="$LOG_DIR/${label}.meta"
    local prompt="Read bench/fixtures/$fixture and identify the bug. Describe the bug in 1-3 sentences and name a triggering input that demonstrates the failure. Do not modify the file."
    echo "===== $label (model=122b) =====" | tee -a "$LOG_DIR/timeline.txt"
    echo "PROMPT: $prompt" >> "$out"
    echo "----" >> "$out"
    local start_ts
    start_ts=$(date +%s)
    /usr/bin/time -p dotnet run --project src/BlueCode.Cli -- --verbose --model 122b "$prompt" >> "$out" 2>&1
    local exit_code=$?
    local end_ts
    end_ts=$(date +%s)
    local elapsed=$((end_ts - start_ts))
    echo "label=$label model=122b exit=$exit_code elapsed=${elapsed}s" > "$meta"
    echo "  -> exit=$exit_code elapsed=${elapsed}s" | tee -a "$LOG_DIR/timeline.txt"
  done
}
run_multiturn()    { echo "multiturn handler implemented in 21-04; not yet available" >&2; exit 4; }
run_schema_rate()  { echo "schema-rate handler implemented in 21-04; not yet available" >&2; exit 4; }
run_needle()       { echo "needle handler implemented in 21-04; not yet available" >&2; exit 4; }
run_coldstart()    { echo "coldstart handler implemented in 21-04 but DISRUPTIVE; gated; not yet available" >&2; exit 4; }
run_full()         { echo "full handler implemented in 21-04 (orchestrator over all sub-modes); not yet available" >&2; exit 4; }

# ---------------------------------------------------------------------------
# usage — print help text listing all 11 mode flags
# ---------------------------------------------------------------------------
usage() {
  cat <<EOF
Usage: $(basename "$0") [--setup|--throughput|--ttft|--multiturn|--refactor|--langcoverage|--schema-rate|--humaneval|--needle|--coldstart|--full]
  --setup        One-time: create bench/.venv-eval and pip install evalplus
  --throughput   tokens/sec via /v1/chat/completions (5 prompts x 3 trials = 15 entries)
  --ttft         time-to-first-token via SSE streaming (10 trials)
  --multiturn    1/3/5/7/10-turn degradation curve (implemented in 21-04)
  --refactor     multi-file F# refactoring (implemented in 21-03)
  --langcoverage Python + TypeScript bug-fix fixtures (implemented in 21-03)
  --schema-rate  50-invocation InvalidJsonOutput rate (implemented in 21-04)
  --humaneval    HumanEval+ pass@1 chat + completion modes (implemented in 21-02)
  --needle       long-context needle-in-haystack 8k/16k/32k (implemented in 21-04)
  --coldstart    DISRUPTIVE launchctl kickstart timing (implemented in 21-04, gated)
  --full         everything except --coldstart (~2hr; implemented in 21-04)
EOF
}

# ---------------------------------------------------------------------------
# Main dispatcher
# ---------------------------------------------------------------------------
case "${1:-}" in
  --setup)        setup_venv ;;
  --throughput)   run_throughput ;;
  --ttft)         run_ttft ;;
  --multiturn)    run_multiturn ;;
  --refactor)     run_refactor ;;
  --langcoverage) run_langcoverage ;;
  --schema-rate)  run_schema_rate ;;
  --humaneval)    run_humaneval ;;
  --needle)       run_needle ;;
  --coldstart)    run_coldstart ;;
  --full)         run_full ;;
  -h|--help|"")   usage ;;
  *)              echo "Unknown flag: $1" >&2; usage; exit 1 ;;
esac
