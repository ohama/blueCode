---
phase: 27-default-prompt-p1-migration-re-eval
plan: 02
status: complete
date: 2026-04-29
files_changed: 1   # bench/eval-qwen35-122b.sh (harness bug fix); eval logs are gitignored
tests_added: 0
commits: 2   # fix(27-02) harness bug + docs(27-02) plan-meta
affects: ["27-03"]
subsystem: "eval-rerun"
requires: ["27-01"]
tech-stack:
  added: []
  removed: []
  unchanged: ["F#", ".NET 8", "mlx_lm.server", "Qwen 3.5 122B"]
decisions:
  - "launchctl kickstart pre-flight is mandatory (KV cache contamination is a real failure mode per Phase 26 Diagnostic D)"
  - "Stochastic re-run policy: up to 3 attempts; per-attempt git checkout to ensure clean fixture state"
  - "Run timestamp captured to /tmp/27-02-run-ts.txt for downstream consumption by Plan 27-03"
  - "Harness bug found and auto-fixed (Deviation Rule 1): grep -cE exits 1 on no-match PASS case, aborting set -euo pipefail script before writing refactor_orphan_count.txt. Fix: subshell with || true."
patterns: []
---

# Plan 27-02 Summary: CORR-EVAL-02 Re-Run with Kickstart Pre-Flight

One-liner: CORR-EVAL-02 PASS on Attempt 2 (orphan_count=0); harness set -e grep-exit bug auto-fixed.

## Outcome

PASS — orphan_count=0 confirmed on attempt 2 of 3 (attempt 1 produced PASS agent result but harness bug prevented artifact write; fixed before attempt 2).

## Pre-Flight Sequence

1. `launchctl kickstart -k gui/501/com.ohama.qwen122b` — KV cache cleared; exit 0; service ready in ~30s (2 iterations x 5s = 10s wait + ~20s for curl first success)
2. `until curl -fsS http://127.0.0.1:8001/v1/models` — service ready in ~30s wall clock
3. Warmup chat completion (`ping` → `Pong!`) — inference path verified live (`"content"` field in response; mlx_lm.server 0.31.3)
4. `git checkout -- bench/fixtures/refactor_multifile/` — fixtures at canonical state (README=2226 bytes, FIXTURES_CANONICAL=true)

## Stochastic Attempt Log

| Attempt | Outcome | Orphan Count | Run Dir | Notes |
|---------|---------|--------------|---------|-------|
| 1 | PASS (agent) / harness abort | 0 (confirmed via git diff) | bench/runs/qwen35-eval-20260429-105648/ | Harness bug: grep exits 1 on no-match, set -e aborted before writing orphan_count.txt. Auto-fixed. |
| 2 | PASS | 0 | bench/runs/qwen35-eval-20260429-105907/ | Fixed harness; all artifacts written correctly. |

PASSing run: `bench/runs/qwen35-eval-20260429-105907/` (run timestamp: `20260429-105907`).

## Empirical Evidence

- `bench/runs/qwen35-eval-20260429-105907/refactor_orphan_count.txt` = `0`
- `bench/runs/qwen35-eval-20260429-105907/refactor_multifile_diff.txt` contains:
  `CORR-EVAL-02 PASS: 0 orphan 'add' references remain`
- `bench/runs/qwen35-eval-20260429-105907/refactor_multifile.meta`:
  `label=refactor_multifile model=122b exit=0 elapsed=41s orphan_add_refs=0`

## Step Thoughts from PASSing Run (P1 Enumeration Evidence)

Step 2 thought: "I have read the README.md which describes the refactor task. I need to rename `add` to `sum` and `add3` to `sum3` across three files: `Calculator.fs`, `Main.fs`, and `Tests.fs`."

The agent enumerated BOTH targets (`add` and `add3`) in Step 2, BEFORE editing. This is the P1 effect — "list ALL targets explicitly in your thought before editing." In Phase 26 (P1 absent from agent-loop path), the agent hallucinated the task entirely ("subtract function"). In Phase 27 with P1 in `defaultSystemPrompt`, the agent correctly identified both rename targets.

Step 5 thought: "I have read all three files. The task is to rename `add` to `sum` and `add3` to `sum3` in Calculator.fs, Main.fs, and Tests.fs. I will now edit Calculator.fs first..."

All 8 steps completed cleanly (8 steps used of 10 max; exit=0; 41s wall clock).

## Deviation: Harness Bug Auto-Fixed (Rule 1)

**Found during:** Attempt 1 execution

**Issue:** `run_refactor()` in `bench/eval-qwen35-122b.sh` uses `set -euo pipefail` (line 21). The orphan-count grep at line 295:
```bash
orphan_count=$(grep -cE '\b(let |Calculator\.)add\b' ... 2>/dev/null | awk ...)
```
`grep -c` exits 1 when no matches are found — exactly the PASS case. With `set -e` active, this silently aborted the script before `echo "$orphan_count" > refactor_orphan_count.txt` ran. The diff file ended at `===== ORPHAN-add CHECK =====` with no verdict line and no artifact file.

**Why undetected until now:** Phase 22/26 runs all FAIL'd (orphan_count=1) — grep found matches and exited 0. The bug only manifests on a true PASS.

**Fix applied:** Wrapped grep in a subshell with `|| true`:
```bash
orphan_count=$( (grep -cE '\b(let |Calculator\.)add\b' \
    "$fixture_dir/Calculator.fs" \
    "$fixture_dir/Main.fs" \
    "$fixture_dir/Tests.fs" 2>/dev/null || true) | awk -F: '{sum+=$2} END {print sum+0}')
```
Also added `$out` as second tee destination on the PASS branch (verdict line now appears in both timeline.txt AND refactor_multifile_diff.txt).

**Files modified:** `bench/eval-qwen35-122b.sh`
**Commit:** `fix(27-02): guard orphan grep against set -e abort on PASS case` (9f8e06e)

**Empirical validation:** Attempt 1 fixtures confirmed via `git diff --stat` (all 3 files modified; 0 orphan add references). Attempt 2 with fixed harness produced all expected artifacts.

## v2.3 Multi-Prong Effectiveness Verified

The v2.3 multi-prong intervention (P1+P2+P3) has now been empirically validated:
- **P1 enumeration directive** (Phase 24-01; migrated to `defaultSystemPrompt` by Plan 27-01) — CRITICAL PATH: agent-loop mode now receives P1; agent enumerated both targets in Step 2 (direct P1 evidence)
- **P2 few-shot example** (Phase 24-02; stays in `planSystemPromptSuffix`) — reinforces plan-mode path
- **P3 PlanValidator pre-flight** (Phase 25-01; plan-mode only) — applies during `--plan` invocations

CORR-EVAL-02 PASS confirms that P1 reaching the agent-loop path was the missing piece. Phase 26 BLOCKED diagnosis was correct: the intervention was architecturally scoped to plan-mode, but the eval harness uses agent-loop mode.

## Out-of-Scope Guardrails Held

- `git diff src/` empty (no source code modified by this plan)
- `git diff bench/baseline.json` empty
- `git diff bench/run.sh` empty
- Plan 27-01 commits intact: `fbb9c55 feat(27-01)` + `2accb7a docs(27-01)`
- Phase 26 BLOCKED commit intact: `7837ad5 docs(26): block Phase 26`

## Commits

1. `fix(27-02): guard orphan grep against set -e abort on PASS case` (9f8e06e) — harness bug auto-fix
2. `docs(27-02): complete CORR-EVAL-02 re-run plan` — plan-meta SUMMARY commit

## Next

Plan 27-03: Update `documentation/qwen35-122b-coding-eval.md` (11 edit sites; verdict 87→92) + STATE.md / ROADMAP.md / REQUIREMENTS.md / 27-VERIFICATION.md / final bench gate / phase-complete commit.

Run timestamp for downstream: `/tmp/27-02-run-ts.txt` = `20260429-105907`.
PASSing run dir: `bench/runs/qwen35-eval-20260429-105907/`.
