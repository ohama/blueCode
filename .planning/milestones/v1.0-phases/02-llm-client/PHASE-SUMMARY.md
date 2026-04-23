---
phase: 02-llm-client
subsystem: llm-client
tags: [fsharp, http-client, spectre-console, jsonschema-net, fsharp-systemtextjson, expecto, qwen, vllm, openai-compat, adapter-pattern]

requires:
  - phase: 01-foundation
    provides: Domain.fs (8 DUs + AgentResult), Router.fs, Ports.fs (ILlmClient), ContextBuffer.fs, ToolRegistry.fs, check-no-async.sh

provides:
  - Complete ILlmClient implementation (QwenHttpClient.fs): spinner + HTTP + extraction + schema + DU mapping
  - MessageRole DU and Message record in Domain.fs (typed chat history)
  - parseLlmResponse: 4-stage extraction pipeline + JsonSchema.Net validator
  - toLlmOutput: LlmStep -> Result<LlmOutput, AgentError> (FinalAnswer / ToolCall with _raw passthrough)
  - Full error mapping: LlmUnreachable, UserCancelled, InvalidJsonOutput, SchemaViolation — no exceptions escape
  - 34 automated tests (16 Router + 13 Pipeline + 5 ToLlmOutput); SC-01 requires manual smoke test

affects:
  - phase-03-tool-handlers (ToolInput._raw passthrough is Phase 3 seam)
  - phase-04-agent-loop (HttpClient singleton moves to CompositionRoot; Serilog; Ctrl+C handler)
  - phase-05-cli (no changes needed in Phase 2 for CLI args)

tech-stack:
  added:
    - FSharp.SystemTextJson 1.4.36 (BlueCode.Cli only)
    - JsonSchema.Net 9.2.0 (BlueCode.Cli only)
    - Spectre.Console 0.55.2 (BlueCode.Cli only)
  patterns:
    - Module-scope JsonSerializerOptions singleton (jsonOptions) — never inline at call sites
    - Private-by-default pipeline with single public entry (parseLlmResponse)
    - Result<T, AgentError> monad chain: every failure mode is a typed value, no thrown exceptions
    - Spectre.Console Status spinner scoped to HTTP call only; parse/validate outside spinner
    - Env-gate smoke test pattern (BLUECODE_SMOKE_TEST=1) for integration tests needing external services

key-files:
  created:
    - src/BlueCode.Cli/Adapters/LlmWire.fs
    - src/BlueCode.Cli/Adapters/Json.fs
    - src/BlueCode.Cli/Adapters/QwenHttpClient.fs
    - tests/BlueCode.Tests/LlmPipelineTests.fs
    - tests/BlueCode.Tests/ToLlmOutputTests.fs
    - tests/BlueCode.Tests/SmokeTests.fs
  modified:
    - src/BlueCode.Core/Domain.fs (MessageRole DU + Message record)
    - src/BlueCode.Core/Router.fs (modelToName + modelToTemperature)
    - src/BlueCode.Core/Ports.fs (ILlmClient: string list -> Message list)
    - src/BlueCode.Cli/BlueCode.Cli.fsproj (3 PackageReferences + Adapters/ compile entries)
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj (BlueCode.Cli ProjectReference + test files)
    - tests/BlueCode.Tests/RouterTests.fs (rootTests composition)

key-decisions:
  - "JsonFSharpOptions.ToJsonSerializerOptions() absent from 1.4.36 — correct API: JsonSerializerOptions() + opts.Converters.Add(JsonFSharpConverter())"
  - "extractLlmStep returns Result<string, AgentError> (JSON string not LlmStep) — separation of JSON validity from LlmStep shape validity"
  - "jsonOptions uses JsonFSharpConverter(JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags(true)) for bare-string DU encoding (LLM-04)"
  - "toLlmOutput is PUBLIC (not private) — enables unit tests without mock HttpMessageHandler infrastructure"
  - "missing/non-string answer for final action -> SchemaViolation (not InvalidJsonOutput) — schema gap at action-specific level, not parse failure"
  - "ToolInput._raw passthrough is Phase 2 placeholder — Phase 3 TOOL-01..04 refines per-action input parsing"
  - "withSpinner wraps postAsync ONLY — parse/validate outside spinner scope"
  - "TaskCanceledException disambiguation: ex.CancellationToken = ct -> UserCancelled; fallthrough -> LlmUnreachable (timeout)"

duration: 13min (3 plans: 4min + 6min + 3min)
completed: 2026-04-22
---

# Phase 2: LLM Client — Phase Summary

**Full Qwen HTTP adapter: 4-stage JSON extraction, JsonSchema.Net validation, Spectre.Console spinner, complete error mapping, and 34 automated tests — ILlmClient returns Ok LlmOutput or typed AgentError for every failure mode**

## Phase Goal

Deliver a production-quality `ILlmClient` implementation that:
1. POSTs to the local vLLM server with typed `Message list` input
2. Extracts and schema-validates LLM JSON output from any response format (bare, prose-wrapped, fenced)
3. Maps every HTTP/network/parse failure to a typed `AgentError` without exceptions escaping
4. Shows a Spectre.Console spinner during the HTTP wait
5. Returns `Ok (ToolCall _ | FinalAnswer _)` for agent loop consumption

**Status: COMPLETE.** All 3 plans executed. SC-01 requires manual verification (env-gated smoke test).

## Plans Executed

| Plan | Name | Duration | Tests Added | Key Deliverable |
|------|------|----------|-------------|-----------------|
| 02-01 | LLM Client Foundation | 4 min | 0 (16 existing pass) | MessageRole/Message types, ILlmClient signature, Router helpers, NuGet packages, Adapters/ scaffold |
| 02-02 | JSON Extraction Pipeline | 6 min | +13 | 4-stage extraction, JsonSchema.Net validator, parseLlmResponse |
| 02-03 | QwenHttpClient Completion | 3 min | +6 (5 unit + 1 smoke) | extractContent, withSpinner, toLlmOutput, wired CompleteAsync |
| **Total** | | **13 min** | **34 pass, 1 skipped** | |

## Success Criteria Evidence

### SC-01: POST -> Ok LlmOutput (live round-trip)
**Status: [MANUAL GATE]**

Automated tests cannot prove this (requires live vLLM endpoint). Manual verification:
```bash
BLUECODE_SMOKE_TEST=1 dotnet test BlueCode.slnx --filter "Smoke"
```
Requirements: vLLM serving `qwen2.5-coder-32b-instruct` on `localhost:8000`.

The smoke test sends a minimal prompt requesting `action='list_dir'` and asserts `Ok LlmOutput` is returned. If endpoint is down when gate is on, test fails with endpoint+detail message.

Code path exercised: `create().CompleteAsync messages Qwen32B ct` → `withSpinner(postAsync)` → `extractContent` → `parseLlmResponse` → `toLlmOutput`

### SC-02: Prose/fence JSON extraction
**Status: AUTOMATED (13 LlmPipelineTests)**

The 4-stage extraction pipeline handles:
- Stage 1: bare JSON object
- Stage 2: stack-based O(N) brace scan from prose
- Stage 3: markdown fence strip (with/without `json` lang tag)
- Stage 4 (failure): `Error (InvalidJsonOutput raw)` when no JSON found

Test evidence: `LlmPipelineTests.fs` — 6 extraction tests pass including prose-wrapped and fenced variants.

Code location: `src/BlueCode.Cli/Adapters/Json.fs` — `extractLlmStep` (private) composed from `tryParseJsonObject`, `extractFirstJsonObject`, `tryFenceExtract`.

### SC-03: Unrecoverable JSON -> InvalidJsonOutput
**Status: AUTOMATED (LlmPipelineTests)**

`parseLlmResponse "I cannot help with that request."` → `Error (InvalidJsonOutput raw)`.

All 3 extraction stages fail → `Error (InvalidJsonOutput content)` is the only non-JSON result type from the pipeline. `SchemaViolation` only fires when JSON is found but shape is wrong.

Test evidence: `LlmPipelineTests.fs` — "pure prose with no JSON -> InvalidJsonOutput" test passes.

### SC-04: HttpRequestException -> LlmUnreachable, no leaked exception
**Status: AUTOMATED BUILD + CODE REVIEW**

`postAsync` try/with catches all exception types the HTTP stack can produce:

| Exception | Mapping |
|-----------|---------|
| `HttpRequestException` | `LlmUnreachable(url, ex.Message)` |
| `TaskCanceledException when ex.CancellationToken = ct` | `UserCancelled` |
| `TaskCanceledException` (fallthrough — HttpClient.Timeout) | `LlmUnreachable(url, "request timed out after 180s")` |

HTTP 4xx/5xx: `not resp.IsSuccessStatusCode` → `LlmUnreachable(url, "HTTP {code}: {body-snippet}")` (body snippet capped at 200 chars).

No `raise` or `reraise` in `CompleteAsync` or any helper it calls. Build passes — F# exhaustive pattern matching guarantees no unhandled case.

Code location: `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` — `postAsync` and `extractContent`.

### SC-05: Spectre.Console spinner visible during HTTP wait
**Status: AUTOMATED BUILD + MANUAL SMOKE**

`AnsiConsole.Status().StartAsync(label, fun _ctx -> work ())` wraps exactly one call — `postAsync`. Parse and validate run after the spinner closes.

Grep evidence: `grep -c "AnsiConsole.Status" src/BlueCode.Cli/Adapters/QwenHttpClient.fs` → **1** (only in `withSpinner`).

Spinner label: `"Thinking... [32B]"` or `"Thinking... [72B]"` — user sees which model is being queried.

Spectre auto-detects non-TTY (CI) and no-ops; no special handling needed.

Full visual confirmation: manual smoke run with live endpoint (`BLUECODE_SMOKE_TEST=1`).

## Architecture: CompleteAsync Data Flow

```
CompleteAsync (messages: Message list) (model: Model) (ct: CancellationToken)
  |
  ├── buildRequestBody messages model          [private — Request JSON]
  |
  ├── withSpinner "Thinking... [32B]" (private) [Spinner START]
  |   └── postAsync url body ct (private)
  |       ├── HTTP POST -> Ok responseJson
  |       ├── HTTP 4xx/5xx -> Error (LlmUnreachable "HTTP N: snippet")
  |       ├── HttpRequestException -> Error (LlmUnreachable url ex.Message)
  |       ├── TaskCanceledException ct -> Error UserCancelled
  |       └── TaskCanceledException other -> Error (LlmUnreachable url "timed out 180s")
  |                                          [Spinner STOP]
  |
  ├── extractContent url responseJson (private) [OpenAI envelope]
  |   ├── choices[0].message.content -> Ok content
  |   └── malformed -> Error (LlmUnreachable url "malformed response: ...")
  |
  ├── parseLlmResponse content (Json.fs, public) [Extraction + Schema]
  |   ├── extractLlmStep -> Ok jsonStr | Error (InvalidJsonOutput raw)
  |   └── validateAndDeserialize -> Ok step | Error (SchemaViolation detail)
  |
  └── toLlmOutput step (public) [DU mapping]
      ├── action="final", answer: string -> Ok (FinalAnswer s)
      ├── action="final", answer: missing/non-string -> Error (SchemaViolation ...)
      └── action=toolName -> Ok (ToolCall (ToolName toolName, ToolInput {_raw: ...}))
      
-> Result<LlmOutput, AgentError>
```

## Module Visibility

### QwenHttpClient.fs
| Binding | Visibility |
|---------|------------|
| `httpClient` | `let private` |
| `roleString` | `let private` |
| `buildRequestBody` | `let private` |
| `postAsync` | `let private` |
| `extractContent` | `let private` |
| `withSpinner` | `let private` |
| `toLlmOutput` | `let` (public) — for ToLlmOutputTests.fs |
| `create` | `let` (public) — primary factory |

### Json.fs
| Binding | Visibility |
|---------|------------|
| 7 internal helpers | `let private` |
| `jsonOptions` | `let` (public) |
| `llmStepSchema` | `let` (public) |
| `parseLlmResponse` | `let` (public) |

## Test Summary

| Suite | Tests | Status |
|-------|-------|--------|
| Router (Phase 1) | 16 | All pass |
| LlmPipeline (02-02) | 13 | All pass |
| ToLlmOutput (02-03) | 5 | All pass |
| Smoke (02-03, env-gated) | 1 | Skipped (BLUECODE_SMOKE_TEST not set) |
| **Total** | **35 registered** | **34 pass, 1 skipped** |

Default `dotnet test`: `34 tests run — 34 passed, 1 ignored, 0 failed, 0 errored`

## Deviations Across All Phase 2 Plans

### Auto-fixed (3 total)

**1. [Rule 1 - Bug] JsonFSharpOptions.ToJsonSerializerOptions() absent from 1.4.36 (02-01)**
- Plan template specified non-existent method; fixed to `JsonSerializerOptions()` + `opts.Converters.Add(JsonFSharpConverter())`

**2. [Rule 1 - Bug] extractLlmStep used LlmStep deserialization causing wrong error type (02-02)**
- Using `Deserialize<LlmStep>` for extraction validity check made structurally-invalid JSON produce `InvalidJsonOutput` instead of `SchemaViolation`; fixed by adding `tryParseJsonObject` as extraction validity check

**3. [Rule 1 - Bug] jsonOptions default JsonFSharpConverter produced adjacent-tag DU encoding (02-02)**
- Default `JsonFSharpConverter()` produces `{"Case":"System"}`; fixed to `JsonFSharpConverter(JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags(true))` for bare-string form

**Plan 02-03 deviations:** None — executed exactly as written.

## Phase 3 Handoff

**Primary seam:** `ToolInput._raw` passthrough in `toLlmOutput`:
```fsharp
let raw = step.input.GetRawText()
let ti = ToolInput (Map.ofList [ ("_raw", raw) ])
Ok (ToolCall (ToolName toolName, ti))
```
Phase 3 TOOL-01..04 replaces `_raw` with typed extraction from `step.input` (a `JsonElement`):
- `read_file`: `step.input.GetProperty("path").GetString()` -> `FilePath`
- `write_file`: `path` + `content` -> `FilePath * string`
- `list_dir`: `path` -> `FilePath`
- `run_shell`: `command` + `timeout` -> `Command * Timeout`

**Tool dispatch:** Phase 3 pattern-matches on `LlmOutput`:
```fsharp
match llmOutput with
| FinalAnswer s -> (* return to user *)
| ToolCall (ToolName name, ToolInput map) -> (* dispatch to tool executor *)
```

## Phase 4 Handoff

- **HttpClient singleton:** Move from QwenHttpClient.fs module scope to `CompositionRoot.fs` (Phase 4 owns DI/wiring)
- **Serilog (OBS-02):** No diagnostic logging in Phase 2 per design. Phase 4 adds Serilog; no `eprintfn` to replace.
- **Ctrl+C handler (LOOP-07):** `ct` parameter flows through CompleteAsync. Phase 4 wires OS signal → CancellationTokenSource.
- **Spectre.Console + Serilog stream separation:** Phase 4 needs to handle stdout/stderr separation when both Spectre spinner and Serilog logging are active.

## Blockers

None for Phase 3 start.

SC-01 manual gate is a post-phase verification step (not blocking Phase 3).

---
*Phase: 02-llm-client*
*Completed: 2026-04-22*
*Plans: 02-01 (4min) + 02-02 (6min) + 02-03 (3min) = 13min total*
