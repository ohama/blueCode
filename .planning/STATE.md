# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-04-22)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** Phase 5 — CLI Polish

## Current Position

Phase: 5 of 5 (CLI Polish)
Plan: 3 of 4 in Phase 5 — complete (05-03 done)
Status: 05-03 complete — /v1/models probe + bootstrapAsync + 80% context warning + Fantomas 7.0.5; 208 tests pass (1 ignored smoke)
Last activity: 2026-04-23 — Completed 05-03-PLAN.md (/v1/models probe + bootstrapAsync + AppComponents.MaxModelLen + 80% context warning + Fantomas)

Progress: [████████████████████] 100% autonomous plans (15 of 16 plans; 05-04 is human checkpoint)

## Performance Metrics

**Velocity:**
- Total plans completed: 7
- Average duration: 7 min
- Total execution time: ~1.0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01-foundation | 3/3 | 32 min | 11 min |
| 02-llm-client | 3/3 | 13 min | 4 min |
| 03-tool-executor | 3/3 | 27 min | 9 min |
| 05-cli-polish | 1/4 | 13 min | 13 min |

**Recent Trend:**
- Last 5 plans: 03-01 (5 min), 03-03 (15 min), 03-02 (7 min), 04-xx (various), 05-01 (13 min)
- Trend: stable ~7-13 min/plan

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
- 03-01: Domain.fs Tool DU amendment is additive only — ReadFile gains lineRange option, ListDir gains depth option. ToolResult DU frozen.
- 03-01: BashSecurity.fs validateCommand always Ok (stub). RunShell is Failure stub. Both are compensating controls — neither is live until 03-02 (run_shell) + 03-03 (validators) complete.
- 03-01: Trailing-separator fix: rootWithSep = projectRoot + Path.DirectorySeparatorChar prevents sibling-directory prefix attack (PITFALLS.md D-3)
- 03-01: FileToolsTests.fs compiled BEFORE RouterTests.fs (F# FS0433: [<EntryPoint>] must be in last compiled file)
- 03-01: Timeout DU name collision (Domain.Timeout type vs ToolResult.Timeout case) — resolve with BlueCode.Core.Domain.Timeout in test code
- 03-03: Python bash_security.py has 22 validate_* functions (not 18 as plan spec stated) — all 22 ported; plan count was inconsistent internal arithmetic
- 03-03: DenyDeferred DU case used for non-misparsing ASKs (validateNewlines + validateRedirections) to match Python deferred evaluation semantics
- 03-03: Fork bomb :(){ :|:& };: NOT blocked by validator chain (matches Python behavior — no destructive pattern covers it)
- 03-03: READ_ONLY_COMMANDS / is_command_read_only intentionally skipped — informational only in Python, never a deny gate
- 03-03: unicodeWsRe uses UTF-8 embedded chars in source (verified correct); test file uses char 0x00A0 / char 0xFEFF to avoid invisible-char hazard
- 03-02: 30s timeout hardcoded in SHELL_TIMEOUT_SECONDS — Tool.RunShell Timeout field deferred to Phase 5 --timeout flag; both ToolFailure sites carry inline comment
- 03-02: /bin/bash -c (not /bin/sh) — BashSecurity validators assume bash semantics
- 03-02: Process.Start wrapped in Ok/Error (not try/finally) — avoids F# task CE type constraint errors with nested try/finally inside match branches
- 03-02: use _ = proc for Dispose scope — idiomatic F# without try/finally in task CE
- 03-02: ToolResult.Timeout 30 (seconds) returned on timeout; SHELL_TIMEOUT_SECONDS * 1000 in ToolFailure for error fidelity
- 03-02: Concurrent stdout/stderr read via F# 10 let!/and! — sequential read deadlocks when stderr buffer fills (dotnet/runtime #98347)
- 04-01: AgentLoop.fs placed in BlueCode.Core (pure) — no Serilog/Spectre/Cli refs; ports-and-adapters invariant preserved
- 04-01: dispatchTool inlined in AgentLoop.fs — ToolRegistry.fs stays empty stub; no premature abstraction
- 04-01: System prompt lives in AgentConfig record (not inline constant) — injectable for tests and production CompositionRoot
- 04-01: onStep callback threaded through runSession/runLoop — enables 04-02 to wire JsonlSink for crash-safe per-step JSONL
- 04-01: Step.Thought = Thought "[not captured in v1]" — capturing real thought deferred to Phase 5+ (requires ILlmClient returning Thought * LlmOutput)
- 04-01: BlueCode.Core.Domain.Timeout qualifier needed in dispatchTool — Domain.Timeout (constructor) vs ToolResult.Timeout (case) name collision
- 04-03: JSONL uses stdlib StreamWriter with AutoFlush=true (not Serilog.Sinks.File) — deterministic flush semantics for SC-6 crash-safety
- 04-03: standardErrorFromLevel = Nullable LogEventLevel.Verbose routes ALL Serilog events to stderr (OBS-02 separation from Spectre.Console on stdout)
- 04-03: renderError exhaustively matches all AgentError cases with user-readable one-liners (no %A, no stack traces) — SC-5 + SC-2
- 04-03: RenderMode Compact | Verbose — Phase 5 CLI-07 --trace adds a separate Serilog sink, NOT a third RenderMode
- 04-02: CompositionRoot uses function injection — no DI container (research Pattern 8)
- 04-02: Default system prompt is `let private` constant in CompositionRoot.fs (not a file) — answers research Open Question 4
- 04-02: Repl.runSingleTurn defensive OCE catch present even though adapters already map OCE -> UserCancelled (belt-and-suspenders, research Pitfall 2)
- 04-02: Program.fs `use _jsonlSink = components.JsonlSink` pattern — JsonlSink disposed at entry-point scope exit guaranteeing final flush
- 04-02: Exit codes: 0 success, 1 agent error, 2 usage error, 130 SIGINT
- 04-02: No Argu yet — positional prompt only; CLI-06 deferred to Phase 5
- 04-02: RouterTests.fs rootTests assembles all test lists explicitly — new test modules must be added to rootTests list (Expecto [<Tests>] auto-discovery not used in this project)
- 05-01: CliArgs DU extracted to src/BlueCode.Cli/CliArgs.fs (not inline in Program.fs) — F# EntryPoint modules can't be opened by test projects; enables Argu parse testing
- 05-01: parseForcedModel raises generic exception; Program.fs catches with explicit exit 2 call (not ArguParseException; Argu doesn't know the model domain strings)
- 05-01: runMultiTurn must be defined AFTER runSingleTurn in Repl.fs (F# compile-order: called before defined is a compile error)
- 05-01: testSequenced wraps ReplTests testList — Expecto runs testList in parallel by default; Console.SetOut is global state so parallel execution causes test interference
- 05-01: Multi-turn REPL: 130 exit code (SIGINT per-turn cancel) translated to 0 for lastCode tally; REPL continues; process exits 130 only in single-turn mode
- 05-01: AgentConfig.ForcedModel is the only Core change in Phase 5 — no other src/BlueCode.Core/ file modified beyond AgentLoop.fs
- 05-02: LoggingLevelSwitch is a module-level let binding in Logging.fs (initialized before configure() runs); ControlledBy(levelSwitch) replaces MinimumLevel.Debug() for runtime level flip
- 05-02: CliArgs.Verbose ambiguity with RenderMode.Verbose fixed by qualifying all Argu case references as CliArgs.Prompt/Verbose/Trace/Model when both namespaces are open
- 05-02: Log.Debug always called in onStep regardless of levelSwitch — Serilog suppresses before formatting (zero-cost gate); visible only when --trace flips to Debug
- 05-02: Step.Thought remains '[not captured in v1]' placeholder — verbose mode displays it; thought capture deferred to Phase 6+ (Open Question 1 resolution)
- 05-02: withSpinner extended with CTS + background Task.Delay(500) ticker for live elapsed-second spinner label (CLI-05); fire-and-forget pattern; finally-block cancels
- 05-03: tryParseMaxModelLen extracted as PUBLIC pure helper in QwenHttpClient.fs for unit-testability (ModelsProbeTests can test JSON edge cases without HTTP mocking)
- 05-03: sync bootstrap retained alongside bootstrapAsync per Open Question 2 — CompositionRootTests stay fast and network-free; MaxModelLen defaults to 8192
- 05-03: printfn (Console.Out) used for 80% WARNING over AnsiConsole.MarkupLine — AnsiConsole bypasses Console.SetOut in test environments; printfn respects redirection (testability fix)
- 05-03: MaxModelLen=10 used in integration test (threshold=32 chars) — any ToolCall step repr exceeds 32 chars reliably; MaxModelLen=100 was too close to boundary
- 05-03: Integer-only 80% check: totalChars*5 >= maxModelLen*16 avoids float; derived from totalChars >= maxModelLen*4*0.8
- 05-03: Fantomas 7.0.5 installed as local dotnet tool (.config/dotnet-tools.json); 35 files reformatted; format committed as isolated style(05-03) commit
- 05-03: Cross-turn context accumulation is POST-V1 — totalChars/warnedThisTurn are mutable locals inside runSingleTurn; reset per turn naturally (Open Question 3)

### Pending Todos

- SC-01 manual verification: run `BLUECODE_SMOKE_TEST=1 dotnet test BlueCode.slnx --filter "Smoke"` with vLLM 32B serving on localhost:8000
- SC-01 manual verification (04-02): run `BLUECODE_AGENT_SMOKE=1 dotnet test BlueCode.slnx --filter "FullyQualifiedName~AgentLoop.Smoke"` with vLLM 32B/72B serving on localhost:8000/8001

### Blockers/Concerns

- Phase 5 implementation: /v1/models field name for max_model_len varies across vLLM releases — query at runtime and handle missing field gracefully.
- Phase 4 implementation: HttpClient singleton should move to CompositionRoot.fs; Spectre.Console + Serilog stream separation needed when both active. [SC-7 gap closed by 04-04]

## Session Continuity

Last session: 2026-04-23T05:48Z
Stopped at: Completed 05-03-PLAN.md — /v1/models probe + bootstrapAsync + 80% context warning + Fantomas 7.0.5. 208 total tests pass (1 ignored smoke). Phase 5 plan 3 of 4 complete. Next: 05-04 (human checkpoint for Python retirement).
Resume file: None
