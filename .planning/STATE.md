# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-04-22)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** Phase 2 — LLM Client

## Current Position

Phase: 2 of 5 (LLM Client) — In progress
Plan: 1 of 4 in Phase 2 — COMPLETE
Status: Phase 2 Plan 1 complete — adapter foundation scaffolded
Last activity: 2026-04-22 — Completed 02-01-PLAN.md (Message types, Router helpers, Adapters/ scaffold)

Progress: [████░░░░░░] 27% (4/15 plans)

## Performance Metrics

**Velocity:**
- Total plans completed: 4
- Average duration: 9 min
- Total execution time: 0.61 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01-foundation | 3/3 | 32 min | 11 min |
| 02-llm-client | 1/4 | 4 min | 4 min |

**Recent Trend:**
- Last 5 plans: 01-01 (15 min), 01-02 (7 min), 01-03 (10 min), 02-01 (4 min)
- Trend: stable ~9 min/plan

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
- 02-01: QwenHttpClient.CompleteAsync returns Error (SchemaViolation stub) after POST — deliberate placeholder until Plans 02-02/02-03 wire extraction pipeline and spinner

### Pending Todos

None.

### Blockers/Concerns

- Phase 2 implementation: Validate response_format JSON object support against local vLLM version at implementation time; have prose-extraction fallback ready regardless.
- Phase 5 implementation: /v1/models field name for max_model_len varies across vLLM releases — query at runtime and handle missing field gracefully.

## Session Continuity

Last session: 2026-04-22T09:06:44Z
Stopped at: Completed 02-01-PLAN.md — Message types, Router helpers, Adapters/ scaffold (LlmWire/Json/QwenHttpClient)
Resume file: None
