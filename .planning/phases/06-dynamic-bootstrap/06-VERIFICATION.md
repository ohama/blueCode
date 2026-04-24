---
phase: 06-dynamic-bootstrap
verified: 2026-04-23T09:15:24Z
re_verified: 2026-04-24T11:15:00Z
status: gaps_found
score: 4/5 structural; live verification revealed behavioral regression
must_haves:
  truths:
    - "No absolute filesystem path in src/ (SC-1)"
    - "72B request on cold Mac does not probe port 8000 (SC-2)"
    - "32B request sends live model id from /v1/models to POST body (SC-3)"
    - "tryParseModelId unit tests wired into rootTests (SC-4)"
    - "bootstrap() completes with zero HTTP calls (SC-5)"
  artifacts:
    - path: "src/BlueCode.Cli/Adapters/QwenHttpClient.fs"
      provides: "ModelInfo type, tryParseModelId, probeModelInfoAsync, two Lazy<Task<ModelInfo>>, buildRequestBody(modelId)"
    - path: "src/BlueCode.Cli/CompositionRoot.fs"
      provides: "Sync bootstrap(), no HTTP"
    - path: "src/BlueCode.Cli/Program.fs"
      provides: "Sync call to bootstrap"
    - path: "tests/BlueCode.Tests/ModelsProbeTests.fs"
      provides: "8 tryParseModelId cases + closed-port probe test"
    - path: "src/BlueCode.Core/Router.fs"
      provides: "No modelToName, no absolute paths"
  key_links:
    - from: "CompleteAsync (create())"
      to: "probe8000 or probe8001 (Lazy)"
      via: "if model = Qwen32B then probe8000 else probe8001"
    - from: "Lazy.Value"
      to: "probeModelInfoAsync baseUrl CancellationToken.None"
      via: "Task<ModelInfo> — fires once per port on first call"
    - from: "info.ModelId"
      to: "buildRequestBody"
      via: "let body = buildRequestBody messages model info.ModelId (line 366)"
    - from: "ModelsProbeTests.tests"
      to: "rootTests"
      via: "RouterTests.fs line 108"
human_verification:
  - test: "72B cold-Mac probe isolation (SC-2)"
    steps: "With localhost:8001 up and localhost:8000 down, run: blueCode --model 72b 'hello'"
    expected: "Session completes; stderr contains no WARN or error referencing port 8000; probe8000 Lazy never materializes"
    why_human: "Requires live Qwen72B on port 8001 and port 8000 confirmed down; cannot simulate in CI"
  - test: "32B live model-id round-trip (SC-3)"
    steps: "With localhost:8000 up, run: blueCode --trace --model 32b 'hello' 2>&1 | grep POST"
    expected: "Stderr shows POST body with \"model\": \"<id>\" matching the id returned by GET localhost:8000/v1/models data[0].id"
    why_human: "Requires live Qwen32B on port 8000; the actual id string is runtime-only"
---

# Phase 6: Dynamic Bootstrap Verification Report

**Phase Goal:** blueCode resolves the serving model's actual id at runtime from `/v1/models`, and bootstrap completes without any network I/O — the probe fires only on the first real LLM call to each port.

**Verified:** 2026-04-23T09:15:24Z
**Status:** human_needed
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|---------|
| 1 | No absolute filesystem path in src/ (SC-1) | VERIFIED | `grep -rn "llm-system" src/` → 0 matches; `grep -rn "modelToName" src/` → 0 matches |
| 2 | 72B request does not probe port 8000 on cold Mac (SC-2) | HUMAN_NEEDED | Structurally sound: `CompleteAsync` line 362 gates probe selection on `model = Qwen32B`; `probe8001` fires for Qwen72B, `probe8000` never accessed |
| 3 | 32B request sends live model id from /v1/models in POST body (SC-3) | HUMAN_NEEDED | Structurally sound: `info.ModelId` flows from `probe8000.Value` → `probeModelInfoAsync` → `buildRequestBody messages model info.ModelId` (line 366) |
| 4 | `tryParseModelId` unit tests wired into rootTests (SC-4) | VERIFIED | 8 `testCase` entries in `modelIdTests` (ModelsProbeTests.fs lines 89-153) + 1 closed-port `probeModelInfoAsync` test (line 68); `ModelsProbeTests.tests` registered in `rootTests` (RouterTests.fs line 108); 216 tests pass |
| 5 | `bootstrap()` completes with zero HTTP calls (SC-5) | VERIFIED | CompositionRoot.fs contains no `HttpClient`, `SendAsync`, `GetAsync`, `PostAsync`; `bootstrap` is synchronous, returns immediately after constructing `QwenHttpClient.create()` which only allocates `Lazy` cells |

**Score:** 5/5 truths (3 VERIFIED structurally, 2 require live Qwen for end-to-end confirmation)

---

## Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` | ModelInfo type, tryParseModelId, probeModelInfoAsync, two Lazy<Task<ModelInfo>>, buildRequestBody with modelId param | VERIFIED | All present: ModelInfo lines 22-24; tryParseModelId lines 239-255; tryParseMaxModelLen lines 258-278; probeModelInfoAsync lines 297-328; probe8000/probe8001 Lazy lines 352-356; buildRequestBody signature line 57 |
| `src/BlueCode.Core/Router.fs` | No modelToName, no absolute path literals | VERIFIED | `grep -n "modelToName" Router.fs` → 0 matches; file contains only classifyIntent, intentToModel, modelToEndpoint, endpointToUrl, modelToTemperature |
| `src/BlueCode.Cli/CompositionRoot.fs` | Sync bootstrap only, no HTTP | VERIFIED | `bootstrap` function line 72 is synchronous; no HttpClient/SendAsync/GetAsync/PostAsync anywhere in file; MaxModelLen hardcoded to 8192 floor with comment explaining lazy probe lives in QwenHttpClient |
| `src/BlueCode.Cli/Program.fs` | Sync call to bootstrap, no bootstrapAsync | VERIFIED | Line 65: `let components = bootstrap projectRoot opts` — synchronous, no `.GetAwaiter()` on bootstrap itself |
| `tests/BlueCode.Tests/ModelsProbeTests.fs` | 8+ tryParseModelId cases + closed-port probe test using port 64321 | VERIFIED | 8 tryParseModelId testCases (lines 89-153); probeModelInfoAsync closed-port test at line 68 using port 64321 |

---

## Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `CompleteAsync` | `probe8000` or `probe8001` | `if model = Qwen32B then probe8000 else probe8001` | WIRED | QwenHttpClient.fs line 362 — exhaustive branch, Qwen72B falls to `else probe8001`; port 8000 never touched for 72B |
| `Lazy.Value` | `probeModelInfoAsync "http://127.0.0.1:8000"` | `Lazy<Task<ModelInfo>>(fun () -> ...)` | WIRED | Lines 352-356; LazyThreadSafetyMode.ExecutionAndPublication default ensures single-probe semantics |
| `info.ModelId` | `buildRequestBody` | `let body = buildRequestBody messages model info.ModelId` | WIRED | Line 366; `info` is the `ModelInfo` awaited from `probe.Value`; modelId flows directly into POST body `"model"` field |
| `ModelsProbeTests.tests` | `rootTests` | explicit list entry | WIRED | RouterTests.fs line 108; 216 tests pass including all 16 ModelsProbeTests cases |

---

## Requirements Coverage

| Requirement | Status | Notes |
|-------------|--------|-------|
| REF-01: model id resolved at runtime from /v1/models | SATISFIED (structural) | `tryParseModelId` parses `data[0].id`; `probeModelInfoAsync` calls `GET /v1/models`; `info.ModelId` injected into POST body |
| REF-02: bootstrap completes without network I/O; probe fires on first real LLM call | SATISFIED (structural) | `bootstrap` is sync, allocates only `Lazy` cells; `probeModelInfoAsync` called lazily inside `CompleteAsync` on `.Value` access |

---

## Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| None | — | — | — | — |

No TODO/FIXME/placeholder patterns found in Phase 6 modified files. No empty handlers or stub returns.

---

## Build and Test Results

- `dotnet build BlueCode.slnx --nologo`: **0 warnings, 0 errors**
- `dotnet run --project tests/BlueCode.Tests`: **216 passed, 1 ignored (live smoke test), 0 failed, 0 errored**
- `grep -rn "llm-system|modelToName|bootstrapAsync|getMaxModelLenAsync" src/`: **0 matches**
- `grep -rn "HttpClient|SendAsync|GetAsync|PostAsync" src/BlueCode.Cli/CompositionRoot.fs`: **0 functional HTTP matches** (only comment referencing QwenHttpClient by name and the `QwenHttpClient.create()` call)
- `grep -rn "async {" src/BlueCode.Core/`: **0 matches** (Core purity preserved)

---

## Human Verification Required

Two success criteria require a live Qwen environment. Both are structurally sound — they are not gaps, just untestable without hardware.

### 1. 72B Cold-Mac Probe Isolation (SC-2)

**Test:** With `localhost:8001` up and `localhost:8000` confirmed down, run `blueCode --model 72b "hello"`.

**Expected:** Session completes successfully. Stderr contains no WARN or connection error referencing port 8000. The `probe8000` `Lazy` cell is never materialized.

**Why human:** Requires live Qwen72B on port 8001 and port 8000 confirmed absent. The structural guarantee is in place (`if model = Qwen32B then probe8000 else probe8001` — line 362), but a live run is the definitive confirmation.

### 2. 32B Live Model-Id Round-Trip (SC-3)

**Test:** With `localhost:8000` up, run `blueCode --trace --model 32b "hello" 2>&1 | grep "POST\|model"`.

**Expected:** `--trace` stderr shows a POST body containing `"model": "<id>"` where `<id>` matches the `data[0].id` field returned by `GET localhost:8000/v1/models`. The two values must be identical strings (the runtime id, not a hardcoded fallback).

**Why human:** The actual model id string is determined at runtime by the running vLLM server. The plumbing that passes it is verified (`info.ModelId → buildRequestBody → JSON body`), but the content match requires the live server.

---

## Gaps Summary

No structural gaps identified. All five success criteria are either fully verified structurally (SC-1, SC-4, SC-5) or structurally sound pending live-operator confirmation (SC-2, SC-3).

The phase goal is achieved in the codebase: `modelToName` and all absolute filesystem path hardcodes are absent from `src/`; `bootstrap` is synchronous with no HTTP; the per-port `Lazy<Task<ModelInfo>>` design provably prevents cross-port probes; `info.ModelId` flows end-to-end from `probeModelInfoAsync` through `buildRequestBody` to the POST body; 216 tests pass including 8 new `tryParseModelId` cases.

---

_Verified: 2026-04-23T09:15:24Z_
_Verifier: Claude (gsd-verifier)_

---

## Gap Found (2026-04-24 re-verification, live run)

**SC-3 behavioral regression — `tryParseModelId` picks HF id over local path.**

### Evidence

Re-verification with live Qwen revealed `blueCode` POST body contains `"model":"Qwen/Qwen2.5-Coder-32B"` (the HF repo id), not the local path. mlx_lm.server err log shows repeated HF fetches triggered by this id:

```
2026-04-24 11:07:37 - HTTP Request: GET huggingface.co/api/models/Qwen/Qwen2.5-Coder-32B → 200 OK
Fetching 21 files: 100%|██████████| 21/21
```

Each blueCode request triggers the server to re-fetch the Base Coder 32B tokenizer from HuggingFace and overwrite its in-memory Instruct tokenizer. Result: chat template is lost, model reverts to Base continuation mode, responses echo the system prompt as document continuation, no valid JSON output possible.

Behavioral symptom: 3 consecutive `--model 32b "Say OK in 3 words"` runs all produced `InvalidJsonOutput` after ~290s each (2-attempt LOOP-05 retry with max_tokens=1024 exhausting on Base continuation).

### Root Cause

`/v1/models` reports TWO ids:
```json
{"data": [
  {"id": "Qwen/Qwen2.5-Coder-32B"},              // HF repo id (Base Coder — triggers HF fallback!)
  {"id": "/Users/ohama/llm-system/models/qwen32b"} // local path (safe — no HF fallback)
]}
```

`tryParseModelId` returns `data[0].id` — the HF id. Sending this id to mlx_lm.server triggers its HF Hub fallback resolution, fetching the Base Coder 32B tokenizer. This is the same regression v1.0 UAT fixed by hardcoding the local path; Phase 6 REF-01 removed the hardcode but selected the wrong id from the server's own advertisement.

### Fix

`tryParseModelId` should prefer local absolute paths over HF repo ids. Iterate `data[]`, return first id that starts with `/`. Fall back to `data[0].id` only when no path id is present (covers non-mlx_lm servers that report a single id).

### Scope

- File: `src/BlueCode.Cli/Adapters/QwenHttpClient.fs`
- Function: `tryParseModelId`
- Tests: extend `ModelsProbeTests.fs` with local-path-preference cases (≥2 — multi-id mlx_lm case, single-id fallback case)
- Retry: after fix, re-run `blueCode --verbose --model 32b "Say OK in 3 words"` — expect valid JSON response + non-placeholder `Thought:` line within ~10s

### Phase 7 impact

Phase 7 (Thought Capture) structural verification is unaffected by this gap — the pipeline wiring is correct; the issue is upstream at the LLM producing unusable output due to Base-mode reversion. Once Phase 6 gap closes, Phase 7 SC-3 live observation unblocks.
