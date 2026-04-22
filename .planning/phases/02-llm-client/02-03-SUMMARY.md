---
phase: 02-llm-client
plan: "03"
subsystem: llm-client
tags: [fsharp, http, spectre-console, expecto, qwen, vllm, openai-compat, json-schema]

requires:
  - phase: 02-01
    provides: QwenHttpClient scaffold with private httpClient, roleString, buildRequestBody, postAsync stub, ILlmClient object expression
  - phase: 02-02
    provides: parseLlmResponse full pipeline (extraction + schema validation), LlmWire.LlmStep, Json.jsonOptions, Json.llmStepSchema

provides:
  - QwenHttpClient.fs complete: extractContent (private), withSpinner (private), toLlmOutput (public), CompleteAsync fully wired
  - Complete error mapping: HttpRequestException, TaskCanceledException (ct disambiguation), HTTP 4xx/5xx, malformed envelope
  - Spectre.Console spinner scoped exclusively to HTTP call
  - ToLlmOutputTests.fs: 5 unit tests for toLlmOutput branches
  - SmokeTests.fs: env-gated live round-trip test (BLUECODE_SMOKE_TEST=1)
  - 34 tests total passing (16 Router + 13 Pipeline + 5 ToLlmOutput); 1 smoke skipped by default

affects:
  - phase-03-tool-handlers (ToolInput._raw passthrough is the seam; Phase 3 TOOL-01..04 replaces _raw with per-tool JsonElement parsing)
  - phase-04-agent-loop (HttpClient singleton should move to CompositionRoot.fs; Serilog replaces any eprintfn; Ctrl+C handler propagates ct)

tech-stack:
  added: []
  patterns:
    - "Result<T, AgentError> monad pipeline: postAsync -> extractContent -> parseLlmResponse -> toLlmOutput"
    - "Spectre.Console Status().StartAsync wraps HTTP-only; parse/validate outside spinner"
    - "TaskCanceledException disambiguation: ex.CancellationToken = ct -> UserCancelled; fallthrough -> LlmUnreachable (timeout)"
    - "Public toLlmOutput enables unit testing of DU mapping without mock HttpMessageHandler"
    - "SmokeTests env-gate pattern: skiptest when BLUECODE_SMOKE_TEST != '1'"

key-files:
  created:
    - tests/BlueCode.Tests/ToLlmOutputTests.fs
    - tests/BlueCode.Tests/SmokeTests.fs
  modified:
    - src/BlueCode.Cli/Adapters/QwenHttpClient.fs
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs

key-decisions:
  - "toLlmOutput is PUBLIC (not private) to enable unit testing from ToLlmOutputTests.fs without mock infrastructure"
  - "Missing/non-string 'answer' for final action -> SchemaViolation (NOT InvalidJsonOutput): by the time toLlmOutput is called, JSON has already parsed and passed base-schema; missing-answer is a schema gap at the action-specific level"
  - "withSpinner wraps ONLY postAsync; parseLlmResponse and toLlmOutput run outside the spinner scope"
  - "SmokeTests.fs uses skiptest (not pending) when BLUECODE_SMOKE_TEST env var not set — Expecto shows 1 ignored, not 1 failed"
  - "ToolInput._raw passthrough is Phase 2 placeholder; Phase 3 TOOL-01..04 refines per-action input parsing"

patterns-established:
  - "Env-gate pattern: if not (smokeEnabled ()) then skiptest ... — reusable for any integration test that requires external services"
  - "Error envelope pattern: extractContent handles transport-layer failures (LlmUnreachable); parseLlmResponse handles content-layer failures (InvalidJsonOutput/SchemaViolation)"

duration: 3min
completed: 2026-04-22
---

# Phase 2 Plan 03: Complete QwenHttpClient — full pipeline wiring, error mapping, spinner, and toLlmOutput unit tests

**QwenHttpClient.CompleteAsync fully wired: Spectre.Console spinner -> HTTP POST -> OpenAI envelope extraction -> JSON schema parse -> LlmStep-to-LlmOutput DU mapping, with typed AgentError for every failure mode (no exceptions escape)**

## Performance

- **Duration:** 3 min
- **Started:** 2026-04-22T09:19:59Z
- **Completed:** 2026-04-22T09:22:54Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments

- Completed QwenHttpClient.fs: replaced all Plan 02-01 stubs with full pipeline wiring
- Full error mapping: 4 distinct error cases (HttpRequestException, TaskCanceledException user-cancel vs timeout, HTTP 4xx/5xx with body snippet, malformed envelope)
- Spectre.Console spinner scoped exclusively to the HTTP call; parse/validate outside spinner
- toLlmOutput public function: maps LlmStep to LlmOutput DU (FinalAnswer / ToolCall); SchemaViolation for missing/non-string answer
- 5 new ToLlmOutput unit tests + 1 env-gated smoke test; total 34 tests pass, 1 skipped

## Task Commits

1. **Task 1: Complete QwenHttpClient.fs** - `28e8e91` (feat)
2. **Task 2: Add ToLlmOutputTests.fs + SmokeTests.fs** - `38d19df` (feat)

## Files Created/Modified

- `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` - Complete ILlmClient implementation: extractContent, toLlmOutput, withSpinner, full postAsync error mapping, wired CompleteAsync
- `tests/BlueCode.Tests/ToLlmOutputTests.fs` - 5 unit tests covering all toLlmOutput branches
- `tests/BlueCode.Tests/SmokeTests.fs` - Env-gated live round-trip test (BLUECODE_SMOKE_TEST=1)
- `tests/BlueCode.Tests/BlueCode.Tests.fsproj` - Compile order: LlmPipelineTests -> ToLlmOutputTests -> SmokeTests -> RouterTests
- `tests/BlueCode.Tests/RouterTests.fs` - rootTests composed with toLlmOutputTests + smokeTests

## CompleteAsync Data Flow

```
CompleteAsync messages model ct
  |> buildRequestBody (private)
  |> withSpinner label (private) [spinner active here]
      |> postAsync url body ct (private) [HTTP POST, full error mapping]
  [spinner stops]
  |> extractContent url responseJson (private) [OpenAI envelope -> content string]
  |> parseLlmResponse content [extraction pipeline + schema validation from Plan 02-02]
  |> toLlmOutput step [LlmStep -> LlmOutput DU]
  -> Result<LlmOutput, AgentError>
```

## Error Mapping Matrix

| Input Condition | AgentError Case | Detail Content |
|----------------|-----------------|----------------|
| HttpRequestException | LlmUnreachable(url, detail) | ex.Message |
| TaskCanceledException where ex.CancellationToken = ct | UserCancelled | (no detail) |
| TaskCanceledException (any other token = timeout) | LlmUnreachable(url, detail) | "request timed out after 180s" |
| HTTP 4xx/5xx response | LlmUnreachable(url, detail) | "HTTP {code}: {body-snippet first 200 chars}" |
| OpenAI envelope malformed (missing choices[0].message.content) | LlmUnreachable(url, detail) | "malformed response: {ex.Message}" |
| No JSON extractable from content | InvalidJsonOutput(raw) | raw content string |
| JSON found but fails llmStepSchema | SchemaViolation(detail) | schema error details |
| Final action with missing/non-string 'answer' | SchemaViolation(detail) | "final action input missing string 'answer' field" |

## toLlmOutput Rationale: SchemaViolation vs InvalidJsonOutput

From the code docblock:
```fsharp
// SchemaViolation (not InvalidJsonOutput): the JSON schema
// cannot validate action-specific input shapes (action→input
// schema is open in v1). Missing-answer is a schema gap,
// not a parse failure. See docblock above for full rationale.
Error (SchemaViolation "final action input missing string 'answer' field")
```

By the time `toLlmOutput` is called, the JSON has already parsed AND passed `llmStepSchema` validation. The `final` action's `input.answer` shape is not enforced by the base schema (input is only constrained to be an object). Missing-answer is a post-schema semantic gap = SchemaViolation. `InvalidJsonOutput` is reserved for Stage 4 parse failure in `extractLlmStep`.

## Spinner Scope Confirmation

`AnsiConsole.Status().StartAsync` appears exactly once (in `withSpinner`), and `withSpinner` is called exactly once in `CompleteAsync` — wrapping `postAsync` only. `parseLlmResponse` and `toLlmOutput` execute outside the spinner closure.

Verification: `grep -c "AnsiConsole.Status" src/BlueCode.Cli/Adapters/QwenHttpClient.fs` → 1

## Visibility Summary

| Binding | Visibility | Reason |
|---------|------------|--------|
| `httpClient` | `let private` | Singleton, no external access needed |
| `roleString` | `let private` | Internal serialization helper |
| `buildRequestBody` | `let private` | Internal request construction |
| `postAsync` | `let private` | Internal HTTP helper |
| `extractContent` | `let private` | Internal envelope extraction |
| `withSpinner` | `let private` | Internal spinner wrapper |
| `toLlmOutput` | `let` (public) | PUBLIC for unit testing from ToLlmOutputTests.fs |
| `create` | `let` (public) | PUBLIC factory — primary module entry point |

## SC-01 Manual Verification Gate

**SC-01 (live Qwen round-trip) cannot be automated in default `dotnet test`.** To verify:

```bash
BLUECODE_SMOKE_TEST=1 dotnet test BlueCode.slnx --filter "Smoke"
```

Requirements:
- vLLM serving Qwen 32B (qwen2.5-coder-32b-instruct) on localhost:8000
- 120s timeout in smoke test
- Expected: `Ok (ToolCall ("list_dir", ...))` or `Ok (FinalAnswer ...)` — any Ok LlmOutput

The smoke test sends: system prompt requesting JSON with `action='list_dir'`, user prompt "List the files in the current directory."

If server is down when gate is on: `Error (LlmUnreachable ...)` → test fails with endpoint/detail message (actionable).

## Phase 2 ROADMAP Success Criteria Mapping

| SC | Description | Evidence | Status |
|----|-------------|----------|--------|
| SC-01 | POST -> parsed LlmStep Ok | CompleteAsync pipeline: postAsync -> extractContent -> parseLlmResponse -> toLlmOutput; smoke test when gate on | [manual] |
| SC-02 | Prose/fence JSON extraction | LlmPipelineTests.fs 13 tests (Plan 02-02); reachable end-to-end via CompleteAsync | automated |
| SC-03 | Unrecoverable JSON -> InvalidJsonOutput | LlmPipelineTests.fs covers parseLlmResponse Stage 4 failure | automated |
| SC-04 | HttpRequestException -> LlmUnreachable, no leak | postAsync try/with: 3 catch branches cover all exception paths; no raise in CompleteAsync | automated build |
| SC-05 | Spectre.Console spinner visible during HTTP wait | withSpinner wraps postAsync; verified by grep (1 AnsiConsole.Status occurrence) | automated build |

## Decisions Made

- `toLlmOutput` is public (not private) to enable unit testing without mock HttpMessageHandler infrastructure
- `missing/non-string answer` for final action maps to SchemaViolation (not InvalidJsonOutput) — rationale in code docblock and error mapping matrix above
- SmokeTests.fs uses `skiptest` (not `pending`) so Expecto shows "1 ignored" under default `dotnet test`
- ToolInput `_raw` passthrough is Phase 2 placeholder; Phase 3 TOOL-01..04 will parse per-action input shapes

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None — no external service configuration required for the automated tests.

For SC-01 manual smoke run: ensure vLLM serving Qwen 32B on localhost:8000, then run `BLUECODE_SMOKE_TEST=1 dotnet test BlueCode.slnx --filter "Smoke"`.

## Phase 3 Handoff Notes

**ToolInput._raw seam:** Phase 2 `toLlmOutput` passes raw JSON text under the `_raw` key:
```fsharp
let ti = ToolInput (Map.ofList [ ("_raw", raw) ])
Ok (ToolCall (ToolName toolName, ti))
```
Phase 3 TOOL-01..04 replaces this: parse `step.input` as `JsonElement` and extract `path`, `content`, `command`, etc. per action. The `_raw` key should not appear in production Phase 3 code.

## Phase 4 Handoff Notes

- **HttpClient singleton:** Currently module-scope private in QwenHttpClient.fs. Phase 4 CompositionRoot.fs should own it (with proper disposal/DI).
- **Serilog:** No diagnostic logging in Phase 2 (per plan). Phase 4 OBS-02 adds Serilog; replace any `eprintfn` debug statements (none committed in Phase 2 per design).
- **Ctrl+C handler:** Phase 4 LOOP-07 propagates cancellation token from user. The `ct` parameter flows through the pipeline correctly; Phase 4 needs to wire the OS signal to a CancellationTokenSource.
- **Spectre.Console + Serilog stream separation:** When Serilog is added in Phase 4, Spectre Console Status spinner may conflict with stderr logging. Phase 4 needs to resolve stream separation (noted in plan).

## Next Phase Readiness

Phase 2 is complete. All 3 plans (02-01, 02-02, 02-03) executed and committed.

Phase 3 (Tool Handlers) can begin:
- ILlmClient returns Ok LlmOutput (ToolCall or FinalAnswer) — Phase 3 dispatches on LlmOutput DU
- ToolInput._raw passthrough is the Phase 3 seam for per-tool input parsing
- Domain types (Tool, ToolResult, FilePath, Command, Timeout) all ship from Phase 1

Blockers: None for Phase 3 start. SC-01 manual gate (live Qwen smoke test) is a post-phase verification step.

---
*Phase: 02-llm-client*
*Completed: 2026-04-22*
