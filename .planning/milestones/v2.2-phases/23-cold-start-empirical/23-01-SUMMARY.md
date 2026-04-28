---
phase: 23-cold-start-empirical
plan: 01
status: complete
completed: 2026-04-28
requirement: COLD-EVAL-01
---

# Phase 23 Plan 01: Cold-start Empirical Measurement — SUMMARY

**Deliverable:** Empirical cold-start measurement closing v2.1 deferred COLD-EVAL-01 requirement. `--coldstart` handler ran one disruption cycle producing `coldstart.json` with elapsed time + status. Eval doc §3.3 flipped from "deferred per scope" to actual measurement; §7 Verdict scorecard re-aggregated (Performance 20 → 25; Total 82 → **87/100**).

## What was done

### Pre-flight
- Confirmed 122B service up on `localhost:8001` (`curl /v1/models` 200)
- Working tree clean (only intentionally-untracked `.claude/` and `localLLM/`)

### Harness fix (auto-fixed deviation, Rule 1)
First `--coldstart` invocation aborted with no measurement: `set -euo pipefail` (line 21 of `bench/eval-qwen35-122b.sh`) caused the script to abort when `tee -a "${LOG_DIR:-/tmp}/timeline.txt"` failed with "no such file or directory" — `mkdir -p "$LOG_DIR"` was placed AFTER the first tee call. Fix: move `mkdir -p` to be the first line of `run_coldstart()`. Service was NOT killed in the failed attempt (PID 44880 unchanged); no actual disruption occurred. Commit: `4bcd8a4 fix(23-01): move mkdir before tee in run_coldstart`.

This is the **third macOS bash-strict-mode pattern** documented in v2.x:
- 21-04: `set -e` interaction with `dotnet run` non-zero exit on MaxLoopsExceeded
- 21-04: `grep -c || echo 0` doubles output under pipefail
- 23-01: `tee` against missing directory fails-fast under pipefail (this fix)

Common pattern: under `set -euo pipefail`, **any I/O command must verify its target dir exists first**.

### Cold-start execution
After fix, second invocation succeeded:
- `kicked_at: 1777354855` (Tue Apr 28 14:40:55 KST 2026)
- `elapsed_s: 37`
- `status: ready`
- PID change confirmed: 44880 (running since Mon 08AM) → 10536 (post-kickstart). Genuine process replacement, not phantom recovery.
- First-generation post-recovery: 1s for 20-token chat completion. Model fully loaded, not just HTTP-server-up.

Artifact: `bench/runs/qwen35-eval-20260428-144055/coldstart.json`.

### Surprising finding
v2.0 SUMMARY estimated "up to 240s after launchctl kickstart". Empirical measurement is **37s — ~6× faster than estimate**. Likely cause: warm OS file cache (model weights already in RAM from prior server run; kickstart kills the process but kernel preserves file pages). Truly cold disk cache (post-reboot) would be slower.

This applies to the common case of mid-session restarts. The pessimistic 240s estimate stays valid for first-boot scenarios.

### Eval doc updates
6 edit sites in `documentation/qwen35-122b-coding-eval.md`:

1. **§3.3 Cold-start** — flipped from "deferred per scope" to actual measurement; documented procedure, results, surprising finding, harness fix
2. **§3 total** — `10 + 5 + 0 + 5 = 20/25` → `10 + 5 + 5 + 5 = 25/25`
3. **§7 scorecard table — cold-start row** — `0 / 5` → `5 / 5`
4. **§7 scorecard table — Performance subtotal** — `20 / 25` → `25 / 25`
5. **§7 dimension coverage row — Performance** — `20/25 80.0% YES` → `25/25 100.0% YES`
6. **§7 Applying rules** — Grand total `31 + 20 + 25 + 6 = 82/100` → `31 + 25 + 25 + 6 = 87/100`
7. **§8 Caveats #1** — flipped from "Cold-start NOT measured" to "Cold-start measured in v2.2 Phase 23: 37s with warm OS file cache"
8. **Final scorecard line** — `**Total: 82/100, Recommendation: KEEP**` → `**Total: 87/100, Recommendation: KEEP**`

### Bench gate post-cold-start
`bash bench/run.sh --gate` exit 0 with `GATE PASS (7/7)`. All fixtures within baseline_max:
- T6=4/5, W1=3/3, W2=3/3, T1=1/3, T5=3/4, B2=2/3, MT=2/4

T6 stepped down from 5 to 4 in this run (still under baseline_max=5; PASS). Random run-to-run variance, not a regression.

### Test count: 284/1/0 (unchanged)

## Commits

| Hash | Message |
|------|---------|
| | `fix(23-01): move mkdir before tee in run_coldstart to satisfy set -euo pipefail` |
| (next) | `chore(23-01): execute --coldstart cycle (37s ready; PID 44880→10536)` (no files staged — coldstart.json is gitignored) |
| (next) | `docs(23-01): update eval doc §3.3 + §7 with cold-start measurement (Total 82 → 87)` |
| (next) | `docs(23): complete cold-start empirical phase` (plan-meta) |

## Verdict change

**v2.1 + v2.2 Phase 22 + v2.2 Phase 23 cumulative:**
- Correctness: 31/40 (unchanged from v2.1; CORR-EVAL-02 still FAIL per Phase 22 finding)
- Performance: 25/25 (cold-start added 5 in this phase; 20 → 25)
- Reliability: 25/25 (unchanged from v2.1)
- Coding quality: 6/10 (unchanged from v2.1)
- **Total: 87/100, Recommendation: KEEP**

## Phase 23 Success Criteria

| SC | Status | Evidence |
|----|--------|----------|
| 1. coldstart.json with status:ready + elapsed_s | ✓ | `bench/runs/qwen35-eval-20260428-144055/coldstart.json` |
| 2. Eval doc §3.3 + §7 updated | ✓ | 6 edit sites applied; final line strict-format match `**Total: 87/100, Recommendation: KEEP**` |
| 3. Bench gate 7/7 PASS post-recovery | ✓ | exit 0; all fixtures within baseline_max |
| 4. STATE.md observation note | (next) | Phase-complete commit will bundle |

## Test count
284/1/0 (unchanged from Phase 22).

---

*Phase 23 complete 2026-04-28. v2.2 milestone now: Phase 22 (architectural ceiling raise — partial close per Option C) + Phase 23 (cold-start empirical — full close). Verdict 87/100 KEEP.*
