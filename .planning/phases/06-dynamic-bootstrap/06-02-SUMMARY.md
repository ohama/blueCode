---
phase: 06-dynamic-bootstrap
plan: 02
subsystem: cli-composition
tags: [fsharp, bootstrap, compositionroot, sync, lazy, ref-02]

# Dependency graph
requires:
  - phase: 06-01
    provides: ModelInfo, tryParseModelId, probeModelInfoAsync, Lazy<Task<ModelInfo>> per-port probes
provides:
  - Single sync bootstrap function in CompositionRoot (bootstrapAsync deleted)
  - Program.fs sync call site (no .GetAwaiter().GetResult() on bootstrap)
  - QwenHttpClient without getMaxModelLenAsync (deleted; probeModelInfoAsync replaces it)
  - ModelsProbeTests: closed-port fallback test retargeted to probeModelInfoAsync
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    added:
      - "sync bootstrap with lazy adapter-owned network probes (replaces eager async bootstrap)"
    removed:
      - "eager /v1/models probe in bootstrapAsync"

key-files:
  created: []
  modified:
    - src/BlueCode.Cli/CompositionRoot.fs
    - src/BlueCode.Cli/Program.fs
    - src/BlueCode.Cli/Adapters/QwenHttpClient.fs
    - tests/BlueCode.Tests/ModelsProbeTests.fs

key-decisions:
  - "AppComponents.MaxModelLen stays int = 8192 (known regression; v1.2 candidate to make per-port accurate)"
  - "Program.fs Log.Information reworded from 'resolved' to 'floor' to reflect static default rather than probed value"
  - "getMaxModelLenAsync fully removed (no caller after bootstrapAsync deletion); probeModelInfoAsync is now the only HTTP probe"

patterns-established:
  - "bootstrap is pure composition: creates record of adapters, no side effects, no network"

# Metrics
duration: ~3 min
completed: 2026-04-23
---

# Phase 6 Plan 02: Dynamic Bootstrap — bootstrapAsync Deletion + Program.fs Sync Bootstrap Summary

**Deleted bootstrapAsync and getMaxModelLenAsync; Program.fs now calls sync bootstrap with zero network I/O at startup — the /v1/models probe fires lazily on first LLM call per port (REF-02, SC-2, SC-5)**

## Performance

- **Duration:** ~3 min
- **Started:** 2026-04-23T09:05:31Z
- **Completed:** 2026-04-23T09:08:47Z
- **Tasks:** 3 (2 edits + 1 verification)
- **Files modified:** 4

## Accomplishments

- Deleted `bootstrapAsync` from `CompositionRoot.fs` (21 lines of async probe code removed). `bootstrap` is now the sole factory, with an updated docblock accurately describing the lazy-probe architecture.
- Deleted `getMaxModelLenAsync` from `QwenHttpClient.fs` (41 lines removed). The function had a single caller (`bootstrapAsync`); with that caller gone it had no purpose.
- Updated `Program.fs` line 65: `.GetAwaiter().GetResult()` on bootstrap replaced with direct sync call `let components = bootstrap projectRoot opts`. Log message updated from "Context window resolved" to "Context window floor" (honest wording — value is a static default, not a probed result).
- Updated `MaxModelLen` comment in `bootstrap` to document v1.1 REF-02 rationale: fixed floor, per-port value inside QwenHttpClient's lazy probe, v1.2 candidate.
- Retargeted the single `getMaxModelLenAsync` test case in `ModelsProbeTests.fs` to `probeModelInfoAsync` using port 64321 (deterministically closed). New test asserts both `ModelId = ""` and `MaxModelLen = 8192` fallback — broader coverage than the old test (which only checked `> 0`).
- All 216 tests pass (208 baseline + 8 tryParseModelId cases from 06-01). Net change from 06-02: 0 (one test rewritten, same count).

## Task Commits

1. **Task 1: Delete bootstrapAsync + getMaxModelLenAsync; Program.fs sync bootstrap** — `2000f34` (refactor)
2. **Task 2: Rewrite closed-port fallback test against probeModelInfoAsync** — `c284f61` (test)
3. **Task 3: Structural verification** — no commit (verification-only task)

## Files Created/Modified

- `src/BlueCode.Cli/CompositionRoot.fs` — Deleted `bootstrapAsync` (lines 88-108), updated `bootstrap` docblock and `MaxModelLen` inline comment
- `src/BlueCode.Cli/Program.fs` — Line 65: sync `bootstrap` call; line 66: log message reworded
- `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` — Deleted `getMaxModelLenAsync` (lines 280-320)
- `tests/BlueCode.Tests/ModelsProbeTests.fs` — Replaced `getMaxModelLenAsync against closed port` test with `probeModelInfoAsync against closed port` test; comment cleaned to avoid stale function name reference

## Decisions Made

- **AppComponents.MaxModelLen stays `int = 8192`:** Research (06-RESEARCH.md § Q4) accepted this as a known v1.1 regression. For 72B's actual 128k context, the 80% warning fires at ~26k chars (conservative but inaccurate). Surfacing the per-port `MaxModelLen` from `ModelInfo` to `AppComponents` is deferred to v1.2. Comment updated in both `AppComponents` record and `bootstrap` function.
- **Log.Information wording: "floor" not "resolved":** The v1.0 log said "Context window resolved" which implied the value came from a probe. Post-06-02, it's always 8192 (static). Reworded to "Context window floor" with parenthetical explanation of where the actual resolution happens.
- **getMaxModelLenAsync fully removed:** No caller remained after `bootstrapAsync` deletion. `probeModelInfoAsync` (which calls both `tryParseModelId` and `tryParseMaxModelLen` internally) supersedes it. The closed-port test coverage was preserved by retargeting to `probeModelInfoAsync`.
- **Test port 64321 (not 8000):** The old test used port 8000 with a comment acknowledging it might be live (making the assertion flaky). Port 64321 is deterministically closed on any standard machine; connection refusal is near-instant, giving reliable fallback-path coverage.

## Phase 6 Structural Verification Results (Task 3)

All structural checks passed:

| Check | Result |
|-------|--------|
| `grep -rn "bootstrapAsync" src/ tests/` | 0 matches |
| `grep -rn "getMaxModelLenAsync" src/ tests/` | 0 matches |
| `grep -rn "llm-system" src/` (SC-1) | 0 matches |
| `grep -n "GetAsync\|SendAsync\|PostAsync" src/BlueCode.Cli/CompositionRoot.fs` (SC-5) | 0 matches |
| `grep -n "BlueCode.Tests.ModelsProbeTests.tests" tests/...RouterTests.fs` (SC-4) | 1 match (present) |
| `dotnet build` | 0 errors, 0 warnings |
| `dotnet test` (run directly) | 216 passed, 1 ignored, 0 failed |

Core purity grep produced matches only in comments (docstrings mentioning `QwenHttpClient`, `Serilog`, `Spectre` by name to explain what Core does NOT use). No functional Core code references adapter-layer libraries.

## Deviations from Plan

**One minor deviation:** The plan's test case template comment text included the phrase "getMaxModelLenAsync had" which would have left a reference to the deleted function in a comment (causing the `grep -rn "getMaxModelLenAsync" tests/` verification to fail). The comment was paraphrased to "v1.0 eager probe" instead. This is a cosmetic deviation; behavior and coverage are identical to the plan's intent.

Otherwise plan executed exactly as written.

## Post-Merge Verification (Live Operator Checks)

These checks require local Qwen servers and cannot be run in CI. Execute after merging:

**SC-2: `--model 72b` with port 8000 down emits no port-8000 WARN**
```bash
# Stop 32B service:
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen32b.plist 2>/dev/null
# Run with only 8001 up:
cd /Users/ohama/projs/blueCode && dotnet run --project src/BlueCode.Cli -- --trace --model 72b "hello" 2>stderr.log
# Check for any 8000 reference:
grep -i "8000" stderr.log        # Expected: no matches
grep -iE "WARN|ERROR" stderr.log | grep -i "8000"  # Expected: no matches
# Re-enable 32B:
launchctl load ~/Library/LaunchAgents/com.ohama.qwen32b.plist
```
Expected: 72B responds, exit 0, zero port-8000 references in stderr before first prompt.

**SC-3: POST "model" field matches live data[0].id**
```bash
# Capture live id:
LIVE_ID=$(curl -s http://127.0.0.1:8000/v1/models | jq -r .data[0].id)
echo "$LIVE_ID"
# Run with --trace:
cd /Users/ohama/projs/blueCode && dotnet run --project src/BlueCode.Cli -- --trace --model 32b "hello" 2>trace.log
# Find POST body "model" field:
grep -oE '"model":"[^"]+"' trace.log | head -1
# Should print: "model":"<$LIVE_ID>"
```

## Issues Encountered

None.

## Next Phase Readiness

Phase 6 is complete. All 5 success criteria are structurally satisfied (SC-1, SC-4, SC-5 by grep; SC-2, SC-3 by architecture with live verification commands documented above).

Phase 7 (Thought Capture) is independent of Phase 6. It touches `ILlmClient.CompleteAsync` return type, `AgentLoop.fs` Step construction, and `LlmOutput` DU — none of which were modified in Phase 6.

---
*Phase: 06-dynamic-bootstrap*
*Completed: 2026-04-23*
