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
run_humaneval()    { echo "humaneval handler implemented in 21-02; not yet available" >&2; exit 4; }
run_refactor()     { echo "refactor handler implemented in 21-03; not yet available" >&2; exit 4; }
run_langcoverage() { echo "langcoverage handler implemented in 21-03; not yet available" >&2; exit 4; }
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
