#!/usr/bin/env bash
# scripts/check-no-async.sh
# -----------------------------------------------------------------------------
# Enforces Success Criterion 4 of Phase 1: no `async {}` expressions inside
# BlueCode.Core. The ban does NOT apply to BlueCode.Cli (which may use
# async interop bridges) or BlueCode.Tests (which may use Async.RunSynchronously
# to call task-returning code synchronously).
#
# Exits 0 when Core is clean; exits 1 on any match.
# Intended to run in CI and as a pre-commit hook.
#
# The pattern matches 'async {' — the standard way to open the F# async CE.
# False positives on comments are accepted (see 01-RESEARCH.md Q4).
# -----------------------------------------------------------------------------

set -euo pipefail

CORE_DIR="src/BlueCode.Core"

if [ ! -d "$CORE_DIR" ]; then
    echo "ERROR: $CORE_DIR does not exist (run from repository root)" >&2
    exit 2
fi

# grep -r returns 0 if any match, 1 if none, 2 on error.
# We invert the meaning: match => ban violated => exit 1.
if grep -rn --include='*.fs' 'async {' "$CORE_DIR" ; then
    echo "" >&2
    echo "ERROR: async {} found in $CORE_DIR — use task {} CE instead." >&2
    echo "       See ROADMAP.md Phase 1 Success Criterion 4 and" >&2
    echo "       .planning/phases/01-foundation/01-RESEARCH.md Q4." >&2
    exit 1
fi

echo "OK: no async {} expressions in $CORE_DIR"
exit 0
