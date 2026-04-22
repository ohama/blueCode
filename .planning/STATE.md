# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-04-22)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** Phase 3 — Tool Handlers

## Current Position

Phase: 2 of 5 (LLM Client) — COMPLETE
Plan: 3 of 3 in Phase 2 — COMPLETE
Status: Phase 2 complete — QwenHttpClient fully wired, 34 tests pass (1 smoke skipped), PHASE-SUMMARY.md written
Last activity: 2026-04-22 — Completed 02-03-PLAN.md (CompleteAsync wiring, spinner, error mapping, toLlmOutput, ToLlmOutputTests, SmokeTests)

Progress: [██████░░░░] 40% (6/15 plans)

## Performance Metrics

**Velocity:**
- Total plans completed: 6
- Average duration: 7 min
- Total execution time: 0.83 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01-foundation | 3/3 | 32 min | 11 min |
| 02-llm-client | 3/3 | 13 min | 4 min |

**Recent Trend:**
- Last 5 plans: 01-03 (10 min), 02-01 (4 min), 02-02 (6 min), 02-03 (3 min)
- Trend: accelerating ~4-6 min/plan

*Updated after each plan completion*

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- Init: task {} exclusively — async {} banned in Core.fsproj
- Init: NuGet packages locked: FSharp.SystemTextJson 1.4.36, FsToolkit.ErrorHandling 5.2.0, JsonSchema.Net 9.2.0, Spectre.Console 0.55.2, Argu 6.2.5, Serilog 4.3.1
- Init: F# compile order enforced — Domain.fs first, adapters last in .fsproj
- 01-01: .slnx format used (dotnet new sln on .NET 10 produces .slnx by default)
- 01-01: testList stub uses empty list [] — comment inside list brackets causes FS0058 off-side error in F# strict indentation mode
- 01-01: FSharp.SystemTextJson excluded from all Phase 1 projects (Phase 2 scope only)
- 01-02: ToolResult DU shape ships in Phase 1 (FND-02 expanded) alongside other 7 DUs so Tool is exhaustively matchable; full semantic contract in Phase 3 TOOL-07
- 01-02: Timeout (ms) in Tool.RunShell vs Timeout (seconds) in ToolResult intentionally different — not unified (former matches .NET APIs, latter is user-facing)
- 01-02: classifyIntent keyword priority: Debug > Design > Analysis > Implementation > General (first-match-wins)
- 01-02: FS0025 fires as warning (not hard error) in default .NET 10 build — invariant is live; Plan 01-03 can add TreatWarningsAsErrors if needed for strict SC2
- 01-02: SC2 first proof uses Intent DU; SC2 second proof (Plan 01-03 Task 3) uses ToolResult DU; both messages land in PHASE-SUMMARY.md
- 01-03: ContextBuffer.fs and ToolRegistry.fs placed in Phase 1 (ARCHITECTURE.md labels Phase 2/Phase 4) for compile-order slot reservation — avoids future FS0433/FS0039 restructuring
- 01-03: Ports.fs comment containing literal `async {}` token triggers false-positive in grep script — always rephrase to avoid the literal token in Core *.fs comments
- 01-03: Phase 1 all 5 Success Criteria verified empirically; PHASE-SUMMARY.md records both SC2 FS0025 messages (Intent + ToolResult)
- 02-01: FSharp.SystemTextJson 1.4.36 does NOT have JsonFSharpOptions.ToJsonSerializerOptions() — correct pattern is JsonSerializerOptions() + opts.Converters.Add(JsonFSharpConverter()); open System.Text.Json.Serialization (not open FSharp.SystemTextJson)
- 02-01: Adapter compile order LlmWire.fs -> Json.fs -> QwenHttpClient.fs -> Program.fs is load-bearing for Plan 02-02 Json.fs extension (Plan 02-02 opens BlueCode.Cli.Adapters.LlmWire for LlmStep deserialization)
- 02-02: extractLlmStep returns Result<string, AgentError> (raw JSON) not Result<LlmStep, AgentError> — extraction checks JSON validity (tryParseJsonObject), not LlmStep shape; schema validation enforces shape
- 02-02: jsonOptions uses JsonFSharpConverter(JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags(true)) — produces bare-string DU encoding ("System") required for LLM-04; default JsonFSharpConverter() produces {Case/Fields} form
- 02-02: 7 private helpers in Json.fs (tryBareParse + tryParseJsonObject + extractFirstJsonObject + fencePattern + tryFenceExtract + extractLlmStep + validateAndDeserialize); 3 public bindings (jsonOptions, llmStepSchema, parseLlmResponse)
- 02-03: toLlmOutput is PUBLIC (not private) to enable unit testing from ToLlmOutputTests.fs without mock HttpMessageHandler
- 02-03: missing/non-string 'answer' for final action -> SchemaViolation (not InvalidJsonOutput) — post-schema semantic gap at action-specific level
- 02-03: withSpinner wraps postAsync ONLY; parseLlmResponse + toLlmOutput outside spinner scope
- 02-03: ToolInput._raw passthrough is Phase 2 placeholder — Phase 3 TOOL-01..04 replaces with typed per-action input parsing
- 02-03: TaskCanceledException disambiguation — ex.CancellationToken = ct -> UserCancelled; fallthrough -> LlmUnreachable (timeout)

### Pending Todos

- SC-01 manual verification: run `BLUECODE_SMOKE_TEST=1 dotnet test BlueCode.slnx --filter "Smoke"` with vLLM 32B serving on localhost:8000

### Blockers/Concerns

- Phase 5 implementation: /v1/models field name for max_model_len varies across vLLM releases — query at runtime and handle missing field gracefully.
- Phase 4 implementation: HttpClient singleton should move to CompositionRoot.fs; Spectre.Console + Serilog stream separation needed when both active.

## Session Continuity

Last session: 2026-04-22T09:22:54Z
Stopped at: Completed 02-03-PLAN.md — QwenHttpClient completion, ToLlmOutputTests, SmokeTests, PHASE-SUMMARY.md (34 tests total)
Resume file: None
