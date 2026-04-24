---
phase: 06-dynamic-bootstrap
plan: 03
subsystem: cli-adapter
tags: [fsharp, json-parsing, mlx-lm, tokenizer, ref-01, gap-closure]

# Dependency graph
requires:
  - phase: 06-dynamic-bootstrap
    plan: 01
    provides: "tryParseModelId function + ModelsProbeTests.modelIdTests list introduced in 06-01"
provides:
  - "tryParseModelId with local-path-preference heuristic: iterates data[], prefers id.StartsWith('/'), falls back to first usable id"
  - "2 new modelIdTests cases: multi-id mlx_lm.server shape (path at data[1] preferred) and multi-id HF-only fallback (data[0])"
  - "Phase 6 SC-3 behavioral regression closed — mlx_lm.server HF tokenizer refetch path extinguished"
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "StartsWith('/') heuristic to distinguish absolute filesystem paths from HF repo ids (Org/Name shape — slash never at start)"
    - "List.tryFind + List.tryHead fallback: prefer first path id, fall back to first id overall"

key-files:
  created: []
  modified:
    - src/BlueCode.Cli/Adapters/QwenHttpClient.fs
    - tests/BlueCode.Tests/ModelsProbeTests.fs

key-decisions:
  - "StartsWith('/') heuristic for path-id preference: HF repo ids are 'Org/Name' (slash in middle, never at start), so no false-positive collision with local absolute paths on macOS/Linux"
  - "Fallback to List.tryHead ids when no path id found: preserves v1.1 single-id behavior for vllm, llama.cpp, and other servers that report only one id"
  - "Iterate all data[] entries (not just data[0]): necessary because mlx_lm.server places the HF id at data[0] and the local path at data[1]"
  - "No change to function signature (string -> string option): all callers (probeModelInfoAsync) unchanged; backward compatible"

patterns-established:
  - "Prefer local filesystem ids in model selection: servers that report both HF repo id and local path should have local path selected to avoid HF Hub tokenizer fallback"

# Metrics
duration: 8min
completed: 2026-04-24
---

# Phase 06 Plan 03: tryParseModelId Path-Preference Gap Closure Summary

**tryParseModelId now iterates all data[] ids and picks the first absolute path (StartsWith '/'), eliminating the mlx_lm.server HF Hub tokenizer refetch that caused Base-mode reversion on every 32B request**

## Performance

- **Duration:** ~8 min
- **Started:** 2026-04-24T02:32:52Z
- **Completed:** 2026-04-24T02:40:44Z
- **Tasks:** 1 of 1
- **Files modified:** 2

## Accomplishments

- Rewrote `tryParseModelId` to iterate all `data[]` entries and prefer the first id starting with `/` (absolute filesystem path), falling back to the first non-empty id when no path id is present
- Added comprehensive docblock explaining the HF Hub fallback trap and the `/`-prefix heuristic rationale
- Extended `modelIdTests` in `ModelsProbeTests.fs` with 2 new cases: mlx_lm.server multi-id shape (path at data[1] preferred over HF id at data[0]), and multi-id HF-only fallback (data[0] selected when no path id)
- Test count: 216 → 218 passing. 0 failed, 0 errored. Build: 0 warnings, 0 errors.

## The Regression

Phase 6 REF-01 (`06-01`) correctly wired `info.ModelId → buildRequestBody` so blueCode sends the runtime-resolved model id in the POST body rather than a Core hardcode. However, `tryParseModelId` only looked at `data[0].id` — and mlx_lm.server reports TWO ids in `/v1/models`:

```json
{"data": [
  {"id": "Qwen/Qwen2.5-Coder-32B"},              // HF repo id  <- data[0] — triggers HF fallback!
  {"id": "/Users/ohama/llm-system/models/qwen32b"} // local path <- data[1] — safe, no HF fallback
]}
```

Sending the HF repo id (`Qwen/Qwen2.5-Coder-32B`) back in the POST body caused mlx_lm.server to refetch the Base Coder 32B tokenizer from HuggingFace Hub on every request, overwriting the loaded Instruct tokenizer. This destroyed the chat template, reverted the model to Base continuation mode, and made every blueCode 32B session produce `InvalidJsonOutput` after ~290s.

## The Fix (3 lines of logic)

```fsharp
let ids = seq { ... } |> List.ofSeq   // collect all non-empty string ids in order
match ids |> List.tryFind (fun s -> s.StartsWith("/")) with
| Some pathId -> Some pathId           // prefer absolute path — no HF Hub fallback
| None -> List.tryHead ids             // fallback: first id (single-id server compat)
```

HF repo ids are shaped `"Org/Name"` — slash in the middle, never at the start — so `StartsWith("/")` has zero false-positive risk.

## Task Commits

1. **Task 1: Prefer local-path id in tryParseModelId + add multi-id test coverage** - `a794b42` (fix)

**Plan metadata:** (docs commit below)

## Files Created/Modified

- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/Adapters/QwenHttpClient.fs` — `tryParseModelId` rewritten with path-preference heuristic; docblock updated; function signature unchanged
- `/Users/ohama/projs/blueCode/tests/BlueCode.Tests/ModelsProbeTests.fs` — 2 new `testCase` entries appended to `modelIdTests`; no new module, no `rootTests` edit

## Decisions Made

- `StartsWith("/")` as the path heuristic: sufficient for macOS/Linux absolute paths; no Windows consideration needed (macOS only per project scope)
- Fallback to `List.tryHead` (not `List.tryFind` first): ensures single-id servers (vllm, llama.cpp) are not broken by the new logic
- Collect ids across all `data[]` entries in original array order: mlx_lm.server places path at data[1], not data[0], so iterating all is required

## Deviations from Plan

None — plan executed exactly as written. Reference implementation from plan applied verbatim.

## Issues Encountered

None. The `dotnet test BlueCode.slnx --filter "FullyQualifiedName~multi-id"` filter produced no output (Expecto uses its own filter format, not MSTest FullyQualifiedName). Verified tests via `dotnet run --project tests/BlueCode.Tests --no-build -- --list-tests` (confirmed both entries present) and full suite run (218 passed).

## Live-Operator Check

Not run during this autonomous plan (GPU not available at execution time). Documented for post-merge:

```bash
# Confirm /v1/models shape:
curl -s http://127.0.0.1:8000/v1/models | jq '.data[].id'
# Expected: "Qwen/Qwen2.5-Coder-32B" and "/Users/ohama/llm-system/models/qwen32b"

# Verify POST body uses path id:
dotnet run --project src/BlueCode.Cli -- --verbose --trace --model 32b "Say OK in 3 words" 2>&1 | grep '"model"'
# Expected: "model":"/Users/ohama/llm-system/models/qwen32b"

# Verify no HF fetch in server err log during the call window:
tail -f /path/to/mlx_lm.server.err.log | grep "huggingface.co"
```

## Phase 7 Side-Effect

Phase 7 SC-3 ("live Thought: observation with real LLM output") is now unblocked. Phase 7's structural wiring was verified in `07-02`; the only blocker was upstream: the Base Coder tokenizer reversion made the LLM produce non-JSON. With the Instruct tokenizer preserved by the path-id fix, the LLM will produce well-formed JSON containing a populated `thought` field. No Phase 7 code changes needed.

## Next Phase Readiness

- Phase 6 is now fully closed (gap resolved). All 3 v1.1 REQs covered: REF-01 (06-01), REF-02 (06-02), OBS-05 (07-01/07-02).
- Phase 7 SC-3 live verification is the only remaining v1.1 item — can be run once GPU is free.
- v1.2 scoping can begin; `AppComponents.MaxModelLen` static 8192 floor is a known v1.2 candidate.

---
*Phase: 06-dynamic-bootstrap*
*Completed: 2026-04-24*
