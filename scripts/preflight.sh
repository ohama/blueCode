#!/usr/bin/env bash
# scripts/preflight.sh — automate T-00..T-04 from documentation/manual-test-guide.md.
# Runs all preflight checks before manual interactive testing. Exits non-zero
# if any check fails so the script can gate further work.
#
# Usage:
#   scripts/preflight.sh             # full preflight (~30-60s)
#   scripts/preflight.sh --quick     # skip T-03 (test suite, slowest)
#   scripts/preflight.sh --help

set -uo pipefail   # NOTE: not -e — we want to capture per-check failures and continue.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
cd "$REPO_ROOT"

QUICK=0
for arg in "$@"; do
  case "$arg" in
    --quick) QUICK=1 ;;
    -h|--help)
      sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *)
      echo "preflight: unknown arg: $arg" >&2
      exit 2
      ;;
  esac
done

# Result accumulator: each entry is "ID|status|note".
RESULTS=()

record() {
  local id="$1" status="$2" note="${3:-}"
  RESULTS+=("$id|$status|$note")
  case "$status" in
    PASS) printf '  [PASS] %s %s\n' "$id" "$note" ;;
    FAIL) printf '  [FAIL] %s %s\n' "$id" "$note" ;;
    SKIP) printf '  [SKIP] %s %s\n' "$id" "$note" ;;
  esac
}

# ---------------------------------------------------------------------------
# T-00 — 122B service health
# ---------------------------------------------------------------------------
echo "T-00: 122B service health check"
http_code=$(curl -fsS -o /tmp/preflight-models.json -w '%{http_code}' \
              --max-time 5 \
              http://127.0.0.1:8001/v1/models 2>/dev/null || echo "000")
if [[ "$http_code" == "200" ]]; then
  ids=$(jq -r '.data[].id' /tmp/preflight-models.json 2>/dev/null | tr '\n' ',' | sed 's/,$//')
  record T-00 PASS "ids=[$ids]"
else
  record T-00 FAIL "http=$http_code (try: launchctl kickstart -k gui/501/com.ohama.qwen122b)"
fi
rm -f /tmp/preflight-models.json

# ---------------------------------------------------------------------------
# T-01 — Debug build
# ---------------------------------------------------------------------------
echo "T-01: Debug build (BlueCode.slnx)"
build_log=$(mktemp -t preflight-debug-build.XXXXXX.log)
if dotnet build BlueCode.slnx >"$build_log" 2>&1; then
  warn_count=$(grep -cE 'warning [A-Z]+[0-9]+' "$build_log" || true)
  record T-01 PASS "warnings=$warn_count"
else
  err=$(grep -E 'error [A-Z]+[0-9]+' "$build_log" | head -1 || echo "(see $build_log)")
  record T-01 FAIL "$err"
fi

# ---------------------------------------------------------------------------
# T-02 — Release build of CLI
# ---------------------------------------------------------------------------
echo "T-02: Release build (src/BlueCode.Cli)"
rel_log=$(mktemp -t preflight-release-build.XXXXXX.log)
if dotnet build -c Release src/BlueCode.Cli/BlueCode.Cli.fsproj >"$rel_log" 2>&1; then
  record T-02 PASS ""
else
  err=$(grep -E 'error [A-Z]+[0-9]+' "$rel_log" | head -1 || echo "(see $rel_log)")
  record T-02 FAIL "$err"
fi

# ---------------------------------------------------------------------------
# T-03 — Full test suite (skippable via --quick)
# ---------------------------------------------------------------------------
if [[ "$QUICK" == "1" ]]; then
  echo "T-03: test suite — skipped (--quick)"
  record T-03 SKIP "--quick"
else
  echo "T-03: test suite (dotnet run tests/BlueCode.Tests)"
  test_log=$(mktemp -t preflight-tests.XXXXXX.log)
  if dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj >"$test_log" 2>&1; then
    summary=$(grep -E 'tests run' "$test_log" | tail -1)
    [[ -z "$summary" ]] && summary=$(tail -3 "$test_log" | tr '\n' ' ')
    record T-03 PASS "$summary"
  else
    failed=$(grep -E '^\s*\[FAIL\]|FAILED:|Failed!' "$test_log" | head -1 || echo "(see $test_log)")
    record T-03 FAIL "$failed"
  fi
fi

# ---------------------------------------------------------------------------
# T-04 — Core purity invariants (git diff master + check-no-async)
# ---------------------------------------------------------------------------
echo "T-04: Core purity invariants"

# T-04a — uncommitted Core changes (working-tree vs master)
if git rev-parse --verify master >/dev/null 2>&1; then
  diff_lines=$(git diff master -- src/BlueCode.Core/ | wc -l | tr -d ' ')
  if [[ "$diff_lines" == "0" ]]; then
    record T-04a PASS "git diff master -- src/BlueCode.Core/ empty"
  else
    record T-04a FAIL "Core has $diff_lines diff lines vs master"
  fi
else
  record T-04a SKIP "no master ref"
fi

# T-04b — banned forbidden references in Core (excludes comment lines)
# F# single-line comments start with `//` (after optional whitespace). We strip
# those before grepping to avoid false positives on historical NOTE comments
# like "// NOTE: model-id resolution moved to QwenHttpClient adapter".
forbidden=$(
  grep -rn --include='*.fs' -E 'Serilog|Spectre|Argu|HttpClient' src/BlueCode.Core/ 2>/dev/null \
    | grep -vE ':[[:space:]]*//' \
    | head -3 || true
)
if [[ -z "$forbidden" ]]; then
  record T-04b PASS "no Serilog/Spectre/Argu/HttpClient refs in Core"
else
  first=$(echo "$forbidden" | head -1 | cut -c1-80)
  record T-04b FAIL "$first"
fi

# T-04c — async {} literal ban (CI invariant)
async_log=$(mktemp -t preflight-noasync.XXXXXX.log)
if bash scripts/check-no-async.sh >"$async_log" 2>&1; then
  record T-04c PASS "no async {} in Core"
else
  first=$(head -1 "$async_log" | cut -c1-80)
  record T-04c FAIL "$first"
fi

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
echo ""
echo "═══════════════════════════════════════════════════════════"
echo "Preflight summary"
echo "═══════════════════════════════════════════════════════════"

pass=0; fail=0; skip=0
for r in "${RESULTS[@]}"; do
  IFS='|' read -r id status note <<<"$r"
  case "$status" in
    PASS) pass=$((pass+1)) ;;
    FAIL) fail=$((fail+1)) ;;
    SKIP) skip=$((skip+1)) ;;
  esac
  printf '  %-7s %-8s %s\n' "$id" "$status" "$note"
done

echo "─────────────────────────────────────────────────────────"
printf '  total: %d  pass: %d  fail: %d  skip: %d\n' \
  "${#RESULTS[@]}" "$pass" "$fail" "$skip"

if [[ "$fail" -gt 0 ]]; then
  echo ""
  echo "preflight: $fail check(s) failed — fix before continuing manual testing."
  exit 1
fi

echo ""
echo "preflight: ready. Proceed with manual-test-guide.md sections 1+."
exit 0
