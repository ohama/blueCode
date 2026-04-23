# Milestone v1.1: blueCode Refinement

**Status:** In Progress (started 2026-04-23)
**Phases:** 6-7 (continuing v1.0 sequence; v1.0 ended at Phase 5)
**Requirements:** 3 (REF-01, REF-02, OBS-05)

## Overview

v1.1 cleans up three pieces of technical debt surfaced during v1.0 UAT: a hardcoded absolute-path in `Router.modelToName` that breaks portability, a bootstrap-time probe that fires even when the user targets a different model port, and a `Step.Thought` placeholder that makes `--verbose` output meaningless. The two phases track a clean separation: Phase 6 owns the `QwenHttpClient`/`CompositionRoot` seam (network + wiring) while Phase 7 owns the `ILlmClient`/`AgentLoop` seam (Core signature + data flow). No new features; every change is confined to removing a specific v1.0 shortcut.

## Phase Numbering

Integer phases 6-7 continue directly from v1.0 (Phases 1-5). No decimal phases were inserted during v1.0. If urgent work must be inserted between Phase 6 and 7, it receives Phase 6.1.

## Phases

- [ ] **Phase 6: Dynamic Bootstrap** — replace hardcoded model path in `Router.modelToName` with a live `/v1/models` query, and lazy-ify the bootstrap probe so `--model 72b` mode no longer touches port 8000 at startup.
- [ ] **Phase 7: Thought Capture** — extend `ILlmClient.CompleteAsync` to surface the LLM `thought` field through to `Step.Thought`, replacing the `"[not captured in v1]"` placeholder with real reasoning text in `--verbose` output.

---

## Phase Details

### Phase 6: Dynamic Bootstrap

**Goal:** blueCode resolves the serving model's actual id at runtime from `/v1/models`, and bootstrap completes without any network I/O — the probe fires only on the first real LLM call to each port.

**Depends on:** Nothing (independent of Phase 7)

**Requirements:** REF-01, REF-02

**Success Criteria:**
1. `Router.fs` contains no string literal matching `/Users/ohama/llm-system/` or any other absolute filesystem path — verified by `grep -r "llm-system" src/`.
2. `blueCode --model 72b "hello"` on a cold Mac (localhost:8000 down, localhost:8001 up) starts and completes without emitting any timeout WARN or connection error referencing port 8000.
3. `blueCode --model 32b "hello"` sends the correct model id (the `data[0].id` value returned by `GET localhost:8000/v1/models`) in the POST body — verifiable via `--trace` stderr showing `"model": "<live-id>"` matching the actual server response.
4. A unit test (or `RouterTests.fs` entry) covers `modelToName` when given a dynamically injected id string, and that test is wired into `rootTests`.
5. `CompositionRoot.bootstrapAsync` returns `AppComponents` with no HTTP calls recorded — confirmed by a mock or by observing that `--trace` emits no POST/GET until the first prompt is submitted.

**Plans:** 2 plans in 2 waves

Plans:
- [ ] 06-01-PLAN.md — Delete Router.modelToName; add ModelInfo + tryParseModelId + probeModelInfoAsync + per-port Lazy<Task<ModelInfo>> cache in QwenHttpClient; extend ModelsProbeTests with id-parser coverage (REF-01, SC-1/3/4)
- [ ] 06-02-PLAN.md — Delete bootstrapAsync + getMaxModelLenAsync; Program.fs calls sync bootstrap; retarget closed-port test to probeModelInfoAsync (REF-02, SC-2/5)

---

### Phase 7: Thought Capture

**Goal:** Every `Step` produced by the agent loop carries the actual LLM reasoning text in `Step.Thought`, so `--verbose` output shows real thought content rather than the `"[not captured in v1]"` placeholder.

**Depends on:** Nothing (independent of Phase 6; can be executed concurrently or after)

**Requirements:** OBS-05

**Success Criteria:**
1. `ILlmClient.CompleteAsync` signature in `Ports.fs` returns a type that includes a `Thought` string alongside `LlmOutput` — e.g., `Task<Result<LlmStep, AgentError>>` or `Task<Result<Thought * LlmOutput, AgentError>>` — and the old `Task<Result<LlmOutput, AgentError>>` signature is gone.
2. `AgentLoop.fs` constructs each `Step` with `Thought = <value from CompleteAsync>` — the literal string `"[not captured in v1]"` does not appear in the production code path (confirmed by `grep -r "not captured" src/`).
3. `blueCode --verbose "<prompt>"` produces step output where the `Thought:` line contains non-empty LLM-generated text (not the placeholder) for at least one step — observable in terminal output.
4. All existing tests still pass (208 baseline); any test that previously constructed a mock `ILlmClient` is updated to satisfy the new signature without reducing coverage.
5. `QwenHttpClient.toLlmOutput` (or its successor) correctly extracts the `thought` field already present in the JSON schema response and passes it through `CompleteAsync` to the loop — no schema changes required, only pipeline wiring.

**Plans:** _(to be filled by plan-phase)_

---

## Progress

| Phase | Goal | Requirements | Status |
|-------|------|--------------|--------|
| 6 — Dynamic Bootstrap | Runtime model-id resolution + lazy probe | REF-01, REF-02 | Planned (2 plans) |
| 7 — Thought Capture | Real LLM thought in Step.Thought | OBS-05 | Not started |

**Requirement coverage:** 3/3 (100%) — REF-01 → Phase 6, REF-02 → Phase 6, OBS-05 → Phase 7.

---

*Roadmap created: 2026-04-23*
*Phase 6 planned: 2026-04-23 (2 plans, sequential)*
*v1.0 archive: `.planning/milestones/v1.0-ROADMAP.md`*
