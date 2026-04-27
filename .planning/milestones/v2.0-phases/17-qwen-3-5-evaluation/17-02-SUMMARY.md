---
phase: 17-qwen-3-5-evaluation
plan: 02
subsystem: infra
tags: [qwen3.5, mlx_lm, launchd, moe, thinking-mode, agentloop, canary-bench]

# Dependency graph
requires:
  - phase: 17-qwen-3-5-evaluation
    provides: "17-01-SUMMARY.md + documentation/qwen35-install.md runbook with plist templates and Path A/B decision framework"
provides:
  - "17-02-LOAD-TEST.md — empirical service-swap record: RSS snapshots, Path A confirmation, canary bench 4/4 PASS"
  - "Qwen 3.5 35B-A3B-4bit running at port 8000; 122B-A10B-4bit at port 8001"
  - "AgentLoop.fs mid-conversation System-role injection fixed (commit 54e54a9)"
  - "documentation/qwen35-install.md: §5.1.1 plist reload procedure + §5.5 load-test measurement procedures"
affects:
  - "17-03 — bench --all run against 35B/122B is now unblocked; SWITCH/STAY decision follows"
  - "Phase 16 — bench baseline.json still references 32B/72B; 17-03 decides whether to re-key"
  - "Future phases — AgentLoop System→User role fix is now merged; all future sessions use correct role injection"

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Path A (--chat-template-args server flag) confirmed working on mlx_lm 0.31.3 for both Qwen 3.5 sizes"
    - "MoE + mmap resident set: observed RSS significantly lower than total parameter weight (62 GB vs projected 89 GB); workload-dependent"
    - "Mid-conversation hint injection must use User role, not System — Qwen 3.5 35B chat template enforces position-0 System constraint"

key-files:
  created:
    - ".planning/phases/17-qwen-3-5-evaluation/17-02-LOAD-TEST.md (310 lines)"
  modified:
    - "src/BlueCode.Core/AgentLoop.fs (3 Role = System → Role = User in buildMessages, commit 54e54a9)"
    - "documentation/qwen35-install.md (§5.1.1 + §5.5 + §5.5.1 + §9.4 additions, commits cb11f88/c9f3786/56a06fc/b398991)"

key-decisions:
  - "Path A confirmed (--chat-template-args flag honored on mlx_lm 0.31.3); QwenHttpClient.fs untouched"
  - "AgentLoop POST-EDIT CONSTRAINT and POST-READ HINT injected as User role (was System); Qwen 3.5 35B rejects mid-conversation System messages with HTTP 404"
  - "Observed combined RSS (62.4 GB) is 27 GB below research projection (89.5 GB) — MoE sparse activation + mmap; no concern"
  - "Cold-start wall-clock not captured this iteration; doc estimates (30-60s / 120-240s) remain as-is"
  - "System memory compressor at 543 MB post-canary — acceptable for dual MoE operation; monitor during 17-03 --all"

patterns-established:
  - "Service swap via launchctl unload → fix plist → launchctl load -w (never kill -9; KeepAlive=true makes process-kill futile)"
  - "Load failed: 5: Input/output error means malformed ProgramArguments, not hardware I/O failure"
  - "AgentLoop hint injection must be User role; System role is only valid at conversation position 0 per Qwen 3.5 chat template"

# Metrics
duration: ~1 day (manual operator walkthrough, 2026-04-27)
completed: 2026-04-27
---

# Phase 17 Plan 02: Service-Swap Load Test Summary

**Qwen 3.5 35B/122B services swapped in (Path A confirmed), AgentLoop System-role injection fixed to unblock canary, 4/4 canary PASS, Phase 17-03 bench unblocked**

## Performance

- **Duration:** ~1 day (manual checkpoint walkthrough by operator)
- **Started:** 2026-04-27
- **Completed:** 2026-04-27
- **Tasks:** 4 checkpoints + Task 6 canary (Task 5 skipped — Path A confirmed)
- **Files modified:** 2 (AgentLoop.fs fix + LOAD-TEST.md) + qwen35-install.md additions

## Accomplishments

- Swapped Qwen 2.5 32B/72B → Qwen 3.5 35B-A3B-4bit/122B-A10B-4bit via launchctl; both services responsive
- Confirmed Path A (`--chat-template-args '{"enable_thinking": false}'`) works on mlx_lm 0.31.3 for both models — no F# code change needed
- Captured RSS snapshots: 16.9 GB (35B) + 45.4 GB (122B) = 62.4 GB combined (27 GB below projected — MoE sparsity)
- Fixed AgentLoop.fs mid-conversation System-role injection; resolved canary T6 HTTP 404 failure on 35B
- Canary bench: 4/4 PASS after fix; Phase 17-03 `bench/run.sh --all` is unblocked
- Added §5.1.1 plist reload procedure + §5.5 load-test measurement procedures to qwen35-install.md

## Task Commits

Tasks were executed manually (autonomous: false plan); documentation additions and fix committed atomically:

1. **Plist flag name correction** — `7b8cbc0` (fix) — `--chat-template-args` not `--chat-template-kwargs`
2. **Entry-point script in plists** — `b1d644d` (docs) — use `qwen-env/bin/python3 -m mlx_lm.server` form
3. **§9.4 uninstall procedure** — `b398991` (docs) — 58.8 GB recovery instructions for legacy 32B/72B
4. **§5.1.1 plist reload procedure** — `cb11f88` (docs) — `Load failed: 5` fix + 3 gotcha rows
5. **§5.5 load-test procedures** — `c9f3786` (docs) — RSS sampling + canary bench steps
6. **§5.5.1 RSS expectations** — `56a06fc` (docs) — MoE + mmap workload-dependent RSS discussion
7. **AgentLoop fix** — `54e54a9` (fix) — System → User role for mid-conversation hint injection

**Plan metadata:** (this commit — `docs(17-02): complete service-swap-load-test plan`)

## Files Created/Modified

- `.planning/phases/17-qwen-3-5-evaluation/17-02-LOAD-TEST.md` — 310-line empirical record of the swap
- `src/BlueCode.Core/AgentLoop.fs` — 3-char × 3 places: `Role = System` → `Role = User` in `buildMessages` (POST-EDIT CONSTRAINT + POST-READ HINT injection points)
- `documentation/qwen35-install.md` — §5.1.1 plist reload, §5.5 measurement procedures, §5.5.1 RSS expectations, §9.4 uninstall (multiple doc commits)

## Decisions Made

1. **Path A confirmed, Task 5 skipped**: `--chat-template-args '{"enable_thinking": false}'` is honored by
   mlx_lm 0.31.3 on both Qwen 3.5 35B and 122B. `QwenHttpClient.fs` was not modified.

2. **User role for mid-conversation hints**: The `[POST-EDIT CONSTRAINT]` and `[POST-READ HINT]` injections
   in `AgentLoop.fs` were changed from `Role = System` to `Role = User`. The authority signal is carried by
   the text marker, not the role. The System role must appear only at position 0 per Qwen 3.5 35B's chat
   template. This also removes blueCode's implicit dependence on lenient tokenizer behavior.

3. **RSS delta observation**: The 27 GB gap between observed (62.4 GB) and projected (89.5 GB) combined RSS
   is attributable to MoE sparse activation and mmap-based weight loading. Only activated expert slices
   are resident at any instant. This is expected and documented in §5.5.1 of the install doc.

4. **Cold-start not captured**: Services were already running when measurements began. Estimates from doc
   §8 (30-60s / 120-240s) remain. Flag for capture on next restart cycle.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] AgentLoop System-role mid-conversation injection blocked canary T6 on 35B**

- **Found during:** Task 6 (canary bench)
- **Issue:** `AgentLoop.fs` `buildMessages` appended `Role = System` messages mid-conversation for the
  POST-EDIT CONSTRAINT (line ~249) and POST-READ HINT (lines ~260, 266) injections. Qwen 1.x mlx_lm
  tolerated this; mlx_lm 0.31.3 with the Qwen 3.5 35B chat template rejects mid-conversation System
  messages with HTTP 404 `{"error": "System message must be at the beginning."}`. Pre-fix canary: 3/4
  PASS (T6 on 32b route failed). The 122B chat template is more lenient — it passed T6 pre-fix,
  masking the issue until the 35B test ran.
- **Fix:** Changed `Role = System` to `Role = User` at all 3 injection sites in `buildMessages`. The
  text marker (`[POST-EDIT CONSTRAINT]` / `[POST-READ HINT]`) carries the authority signal; role
  assignment was incidental. This is a 3-char change × 3 places.
- **Files modified:** `src/BlueCode.Core/AgentLoop.fs`
- **Verification:** Post-fix canary 4/4 PASS. Existing `AgentLoopTests` assert on text content only
  (`stringContains "[POST-EDIT CONSTRAINT]"` etc.), no role assertions — tests preserved 254/1/0.
- **Committed in:** `54e54a9` (`fix(17-02): inject POST-EDIT/POST-READ hints as User role, not System`)

**2. [Rule 1 - Bug] Plist flag name was wrong (`--chat-template-kwargs` vs `--chat-template-args`)**

- **Found during:** CP2/CP4 (plist creation and reload)
- **Issue:** The runbook written in 17-01 used `--chat-template-kwargs` (the Python `**kwargs` form)
  but the actual mlx_lm.server CLI flag is `--chat-template-args`. This caused `Load failed: 5:
  Input/output error` on the first plist load attempt.
- **Fix:** Corrected the flag name in all 5 doc occurrences (commits `7b8cbc0`, `b1d644d`). Added
  §5.1.1 recovery procedure to qwen35-install.md for the "Load failed: 5" error pattern.
- **Files modified:** `documentation/qwen35-install.md`
- **Verification:** Services loaded cleanly after fix. `plutil -lint` OK on both plists.
- **Committed in:** `7b8cbc0`, `b1d644d`, `cb11f88`

---

**Total deviations:** 2 auto-fixed (1 blocking-canary-blocker, 1 doc-bug)
**Impact on plan:** Both fixes were essential. The AgentLoop fix (`54e54a9`) is a correctness improvement
that removes blueCode's silent dependence on lenient chat templates. The plist flag fix corrected a
doc error from 17-01. No scope creep — `plan.files_modified` lists `17-02-LOAD-TEST.md` as the primary
artifact; AgentLoop.fs is an unlisted but necessary blocker fix.

## Issues Encountered

- **Non-uniform Qwen 3.5 strictness**: The 122B chat template passed T6 pre-fix while 35B failed. This
  means canary failures on larger models can mask correctness issues that only surface on smaller models
  (or vice versa). The fix is model-agnostic (User role is always safe); no per-model workaround needed.

- **Memory pressure at 543 MB compressor**: After canary with both services running, the system compressor
  reached 543 MB. This is within the acceptable range for dual MoE operation but bears watching during the
  full `bench/run.sh --all` in 17-03 (longer prompts + more KV cache accumulation).

## User Setup Required

None for documentation finalization. The operational swap (launchctl, model download) was performed
manually by the operator during the checkpoint walkthrough on 2026-04-27.

## Next Phase Readiness

- **17-03 is GO**: Both services are running and canary-validated. `bench/run.sh --all` can proceed.
- **AgentLoop fix is merged**: `54e54a9` is on master; all future sessions have correct role injection.
- **Baseline reference**: `bench/baseline.json` still records 32B/72B results. 17-03 will produce
  35B/122B results for comparison and make the SWITCH/STAY decision.
- **Memory monitor**: Compressor was at 543 MB after canary; track during `--all` to confirm no OOM.
- **Cold-start gap**: Wall-clock cold-start times not captured; 17-03 may capture on next restart.

---
*Phase: 17-qwen-3-5-evaluation*
*Completed: 2026-04-27*
