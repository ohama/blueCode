---
phase: 10-bench-formalization
plan: "01"
subsystem: bench
tags: [bash, bench, fixtures, harness, gitignore]
requires:
  - v1.2 milestones (bench runs in /tmp/bench-v1.2/)
  - bench-fixtures/ untracked directory (now absorbed)
provides:
  - bench/run.sh (repo-tracked regression harness with mode-flag dispatch)
  - bench/fixtures/{bug_lastchar.fs,bug_average.fs,bug_divide_zero.fs} (broken-baseline fixtures)
  - CLAUDE.md ## Bench section (canonical entry point for future sessions)
  - bench/runs/ gitignored (artifact directory excluded)
affects:
  - Plan 10-02: builds on bench/run.sh by adding --gate mode + baseline.json
  - Plan 10-03: writes documentation/bench.md referencing bench/run.sh flags
tech-stack:
  added: []
  patterns:
    - mode-flag dispatch (case "${1:-}" in --flag) replacing positional selectors
    - bash 3.2 compatible (no declare -A, no ${VAR^^}, no BASH_REMATCH arrays)
    - fixture-restore heredoc pattern (cat > bench/fixtures/... <<'EOF') before W1/W2 runs
key-files:
  created:
    - bench/run.sh
    - bench/fixtures/bug_lastchar.fs
    - bench/fixtures/bug_average.fs
    - bench/fixtures/bug_divide_zero.fs
  modified:
    - CLAUDE.md (additive: new ## Bench section)
    - .gitignore (additive: bench/runs/ line)
decisions:
  - "Created separate bug_divide_zero.fs for B2 diagnose test (per Research §8 Pitfall 5) — distinct from bug_average.fs which is the W2 write-task fixture"
  - "mode-flag dispatcher uses ${1:-} to treat no-arg as --help (exits 0), avoiding set -u failure on unbound $1"
  - "b2_mode() prompt deliberately omits tool name per BENCH-05 / 09.1-04 guidance (contrast with W1 which names write_file to test loop injection)"
  - ".gitignore update done before --canary live smoke to ensure bench/runs/ logs never appear in git status"
metrics:
  duration: ~20 min
  completed: "2026-04-26"
---

# Phase 10 Plan 01: Bench Harness Move Summary

**One-liner:** Repo-tracked bench/run.sh with --regression/--canary/--b2/--all dispatch, three broken-baseline fixtures under bench/fixtures/, and CLAUDE.md ## Bench pointer — replaces ephemeral /tmp/bench-v1.2/run.sh.

---

## What Was Done

Three atomic tasks executed; zero source code touched.

### Task 1: Fixture Migration + New B2 Fixture

Moved two previously-untracked fixture files from `bench-fixtures/` to `bench/fixtures/` and restored both to their canonical broken-baseline state (the on-disk files were in post-run "fixed" state from prior 09.1 bench runs):

**bench/fixtures/bug_lastchar.fs** (restored to broken — off-by-one indexer):
```fsharp
module LastChar

/// Returns the last character of a string.
let getLastChar (s: string) : char =
    s.[s.Length]
```
The W1 write task fixes `s.[s.Length]` → `s.[s.Length - 1]`.

**bench/fixtures/bug_average.fs** (restored to broken — no averageSafe, just divide-by-zero on empty):
```fsharp
module Average

let average (xs: int list) : int =
    (List.sum xs) / (List.length xs)
```
The W2 write task adds `averageSafe : int list -> int option`.

**bench/fixtures/bug_divide_zero.fs** (new — B2 diagnose-only fixture):
```fsharp
module DivideZero

/// Computes the integer mean of a list. Raises DivideByZeroException on empty input.
/// Bug trigger: call with an empty list (e.g., average []) — List.length [] returns 0,
/// causing integer division by zero at runtime.
let average (xs: int list) : int =
    List.sum xs / List.length xs
```
Created as a clean, purpose-built B2 fixture separate from `bug_average.fs` (which doubles as the W2 write-task target). Separating them avoids the W1/W2 fixture-restore pattern contaminating B2 runs.

After Task 1: `bench-fixtures/` directory at repo root no longer exists.

### Task 2: bench/run.sh

Created `bench/run.sh` (mode 755, 192 lines) by lifting the `run()` helper verbatim from `/tmp/bench-v1.2/run.sh` and reshaping the bottom dispatcher into mode-flag dispatch.

**Mode-flag inventory (as of Plan 10-01; --gate added in Plan 10-02):**

| Flag | Function | Invocations | Wall-clock |
|------|----------|-------------|-----------|
| `--canary` | Quick smoke: T1/T5/T6×2 | 4 | ~1.5 min |
| `--regression` | Part 1 T1–T7 × 32B+72B | 14 | ~6 min |
| `--b2` | B2 divide-by-zero diagnose × 32B+72B | 2 | ~30 s |
| `--all` | regression + variance + diagnose + write | ~36 | ~25 min |
| `--help`/`-h`/no-arg | Print usage | — | instant |

**Structural changes from v1.2 source:**

- `LOG_DIR` changed from `/tmp/bench-v1.2` to `bench/runs/$(date +%Y%m%d-%H%M%S)` (gitignored)
- `cd /Users/ohama/projs/blueCode` preserved at top
- `command -v jq` guard added near top (Plan 10-02 needs jq for --gate)
- All `bench-fixtures/` path references migrated to `bench/fixtures/` (verified: `grep -c bench-fixtures bench/run.sh` = 0)
- v1.2's `v9_1`, `v9_1_rev`, `v9_1_rev2` functions dropped (those were one-time validation functions, not recurring modes)
- `LOG_DIR="$LOG_DIR" run ...` env-prefix pattern removed; mode functions use `local LOG_DIR` at their top instead (cleaner per Research §8 Pitfall 6)
- `phaseB`'s B3 call preserved as a TODO comment: `# TODO(v1.4): create bug_validate.fs fixture`

**bash 3.2 compatibility:** No `declare -A`, no `${VAR^^}`, no BASH_REMATCH arrays. `set -u` only (no `set -e`).

### Task 3: CLAUDE.md + .gitignore

Added `## Bench` section to CLAUDE.md (inserted between `## Don't Do` and `## When Stuck`):

```markdown
## Bench

`bench/run.sh` is the canonical regression harness — repo-tracked replacement for
v1.2's ephemeral `/tmp/bench-v1.2/run.sh`. Run `bench/run.sh --gate` (added in
Plan 10-02) to validate the current binary against `bench/baseline.json`.

- `--gate` — regression subset (~8 invocations, ~2 min); exits non-zero on regression
- `--canary` — quick smoke (4 invocations, ~1.5 min)
- `--regression` — full Part 1 reproducibility (14 invocations)
- `--all` — everything (~25 min)
- `--b2` — B2 divide-by-zero diagnose only

Fixtures live in `bench/fixtures/`. Logs land in `bench/runs/<timestamp>/`
(gitignored). See `documentation/bench.md` for full usage and fixture conventions.
```

Appended `bench/runs/` to `.gitignore`.

---

## Live --canary Smoke Result

Run timestamp: `bench/runs/20260426-040447/`

| Label | Model | Exit | Elapsed |
|-------|-------|------|---------|
| canary_T1_32b | 32b | 0 | 5s |
| canary_T5_72b | 72b | 0 | 18s |
| canary_T6_32b | 32b | 0 | 22s |
| canary_T6_72b | 72b | 0 | 45s |

All 4 invocations PASS. Total wall-clock: ~90s. `bench/runs/` correctly excluded from `git status` (gitignored). No anomalies; no hang events; no server kickstarts required.

---

## Deviations from Plan

### Auto-fixed Issues

None — plan executed exactly as written with one ordering note:

**Task 3 `.gitignore` update done before Task 2 live smoke:** The plan's critical constraint (constraint #5) required `git check-ignore -q bench/runs` to exit 0 BEFORE running `--canary`. Since Task 3 owns the `.gitignore` line, the `.gitignore` update was applied prior to the smoke run (within Task 2's verify block), then CLAUDE.md was added in the same Task 3 commit. The task boundary is logical (single commit for both files), so this is not a deviation — just execution ordering for correctness.

---

## Commits

| Hash | Type | Description |
|------|------|-------------|
| `ced7b66` | feat | Move bench fixtures into bench/fixtures/ and add bug_divide_zero.fs |
| `16157f1` | feat | Add bench/run.sh with mode-flag dispatch (lifted from v1.2 harness) |
| `66ab640` | chore | Document bench/run.sh in CLAUDE.md and gitignore bench/runs/ |

Plan-metadata commit: `docs(10-01): complete bench harness move plan` (added after SUMMARY + STATE).

---

## Zero Source-Code Changes Confirmation

`git diff --stat src/ tests/` returns empty. No `src/` or `tests/` files were modified. Phase 10 invariant holds.

---

## Downstream Notes for Plan 10-02

Plan 10-02 needs to:
1. Run `bench/run.sh --canary` to confirm step counts for T6/W1/W2/T1/T5 match log evidence
2. Populate `bench/baseline.json` with confirmed step counts
3. Add `--gate` mode to `bench/run.sh` (reads `bench/baseline.json` via `jq`)
4. The `jq` guard is already present in `bench/run.sh` (command -v jq check)
5. The `canary_T6_32b` run above shows 22s / exit=0 — consistent with expected 4-step pattern
6. The `canary_T6_72b` run shows 45s / exit=0 — consistent with expected 5-step pattern
