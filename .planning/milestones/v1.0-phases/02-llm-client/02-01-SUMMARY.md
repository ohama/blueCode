---
phase: 02-llm-client
plan: "01"
subsystem: llm-client
tags: [fsharp, systemtextjson, jsonschema, spectre-console, http-client, nuget, adapter-pattern]

requires:
  - phase: 01-foundation
    provides: Domain.fs (8 DUs + AgentResult), Router.fs (classifyIntent/intentToModel/modelToEndpoint/endpointToUrl), Ports.fs (ILlmClient/IToolExecutor stubs), ContextBuffer.fs, ToolRegistry.fs, async-ban script
provides:
  - MessageRole DU (System|User|Assistant) and Message record added to Domain.fs
  - ILlmClient.CompleteAsync signature amended to Message list (typed roles)
  - Router.modelToName and Router.modelToTemperature pure total functions
  - FSharp.SystemTextJson 1.4.36 / JsonSchema.Net 9.2.0 / Spectre.Console 0.55.2 wired to BlueCode.Cli
  - Adapters/LlmWire.fs: LlmStep wire record for JSON extraction pipeline
  - Adapters/Json.fs: module-scope jsonOptions singleton with JsonFSharpConverter
  - Adapters/QwenHttpClient.fs: compiling ILlmClient skeleton (stub CompleteAsync)
affects:
  - 02-02-PLAN (extends Json.fs + adds pipeline tests; relies on LlmWire.fs compile order)
  - 02-03-PLAN (replaces stub CompleteAsync with full HTTP round-trip + error mapping)
  - 03-tools (uses ILlmClient via CompositionRoot)
  - 04-agent-loop (AgentLoop.fs consumes ILlmClient with Message list parameter)

tech-stack:
  added:
    - FSharp.SystemTextJson 1.4.36 (BlueCode.Cli only — NOT in Core)
    - JsonSchema.Net 9.2.0 (BlueCode.Cli only)
    - Spectre.Console 0.55.2 (BlueCode.Cli only)
  patterns:
    - Module-scope singleton JsonSerializerOptions (jsonOptions in Json.fs) — never created inline at call sites
    - Private helpers + single public factory (create()) for adapter modules
    - Adapter compile order: LlmWire.fs -> Json.fs -> QwenHttpClient.fs -> Program.fs (load-bearing for Plan 02-02)
    - Stub-returning CompleteAsync with TODO comments as forward pointer to next plans

key-files:
  created:
    - src/BlueCode.Cli/Adapters/LlmWire.fs
    - src/BlueCode.Cli/Adapters/Json.fs
    - src/BlueCode.Cli/Adapters/QwenHttpClient.fs
  modified:
    - src/BlueCode.Core/Domain.fs (appended MessageRole + Message)
    - src/BlueCode.Core/Router.fs (appended modelToName + modelToTemperature)
    - src/BlueCode.Core/Ports.fs (changed string list to Message list)
    - src/BlueCode.Cli/BlueCode.Cli.fsproj (3 PackageReferences + Adapters/ compile entries)

key-decisions:
  - "JsonFSharpOptions.Default().ToJsonSerializerOptions() does not exist in FSharp.SystemTextJson 1.4.36 — correct API is JsonSerializerOptions() with opts.Converters.Add(JsonFSharpConverter())"
  - "Tasks 2 and 3 committed together because fsproj lists QwenHttpClient.fs before the file exists; build requires both changes atomically"
  - "MessageRole does not include Tool case (plan specifies System|User|Assistant only); Tool role can be added additively in Phase 3+ if needed"

patterns-established:
  - "Adapter pattern: private httpClient + private buildRequestBody + private postAsync + public create() — only create() is the public surface"
  - "Compile order constraint: wire-type file (LlmWire.fs) before JSON options (Json.fs) before HTTP client (QwenHttpClient.fs)"
  - "Stub placeholder pattern: return Error (SchemaViolation '...stub...') with TODO comment pointing to next plan"

duration: 4min
completed: 2026-04-22
---

# Phase 2 Plan 01: LLM Client Foundation Summary

**MessageRole/Message types, amended ILlmClient signature, Router temperature/name helpers, and QwenHttpClient skeleton with JsonFSharpConverter-based jsonOptions singleton**

## Performance

- **Duration:** 4 min
- **Started:** 2026-04-22T09:02:24Z
- **Completed:** 2026-04-22T09:06:44Z
- **Tasks:** 3 (Tasks 2+3 committed together due to fsproj ordering constraint)
- **Files modified:** 8

## Accomplishments

- Domain.fs gains MessageRole DU (System|User|Assistant) and Message record — additive, Phase 1 types untouched
- ILlmClient.CompleteAsync parameter changed from `string list` to `Message list` carrying typed role+content
- Router.fs exports modelToName (vLLM model string) and modelToTemperature (0.2/0.4 hardcoded) as pure total functions
- BlueCode.Cli.fsproj acquires FSharp.SystemTextJson 1.4.36, JsonSchema.Net 9.2.0, Spectre.Console 0.55.2
- Adapters/ directory scaffolded with LlmWire.fs (LlmStep), Json.fs (jsonOptions singleton), QwenHttpClient.fs (compiling ILlmClient skeleton)
- All 16 RouterTests still pass; check-no-async exits 0; dotnet build: 0 errors, 0 warnings

## Task Commits

Each task was committed atomically:

1. **Task 1: Amend Core — Message types, Ports signature, Router helpers** - `01b48f5` (feat)
2. **Tasks 2+3: Wire NuGet packages and scaffold Adapters/ with LlmWire, Json, QwenHttpClient** - `14fd169` (feat)

_Note: Tasks 2 and 3 were committed together because the fsproj listed QwenHttpClient.fs (Task 3) before the file existed; dotnet build requires both changes in the same commit to produce a clean build._

## Files Created/Modified

- `src/BlueCode.Core/Domain.fs` - MessageRole DU and Message record appended (additive)
- `src/BlueCode.Core/Router.fs` - modelToName and modelToTemperature appended
- `src/BlueCode.Core/Ports.fs` - ILlmClient.CompleteAsync: string list -> Message list
- `src/BlueCode.Cli/BlueCode.Cli.fsproj` - 3 PackageReferences added; Adapters/ compile entries added
- `src/BlueCode.Cli/Adapters/LlmWire.fs` - LlmStep wire record (thought/action/input: JsonElement)
- `src/BlueCode.Cli/Adapters/Json.fs` - jsonOptions singleton with JsonFSharpConverter registered
- `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` - private httpClient/buildRequestBody/postAsync, public create()

## Decisions Made

1. **JsonFSharpOptions API mismatch (auto-fixed):** The plan specified `JsonFSharpOptions.Default().ToJsonSerializerOptions()` but FSharp.SystemTextJson 1.4.36 does not have a `ToJsonSerializerOptions()` method. The `JsonFSharpOptions` class is in `System.Text.Json.Serialization` and the correct registration pattern is `JsonSerializerOptions()` with `opts.Converters.Add(JsonFSharpConverter())`. Fixed silently.

2. **Tasks 2+3 committed together:** The plan's Task 2 instructions add QwenHttpClient.fs to the fsproj `<Compile>` entries before Task 3 creates the file. This means the intermediate state after Task 2 alone produces a build error (missing file). The plan explicitly noted this ("Build is intentionally broken at this point pending Task 3"). Tasks 2 and 3 were therefore bundled into a single atomic commit that leaves the repo in a building state.

3. **No `open FSharp.SystemTextJson` namespace:** FSharp.SystemTextJson's public API lives in `System.Text.Json.Serialization`, not a `FSharp.SystemTextJson` namespace. The correct open is `open System.Text.Json.Serialization`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] JsonFSharpOptions.ToJsonSerializerOptions() method does not exist**
- **Found during:** Task 2 (creating Json.fs)
- **Issue:** Plan template specified `JsonFSharpOptions.Default().ToJsonSerializerOptions()` as the constructor pattern, but FSharp.SystemTextJson 1.4.36 does not expose this method. Build failed with FS0039 on `JsonFSharpOptions`.
- **Fix:** Used `JsonSerializerOptions()` with `opts.Converters.Add(JsonFSharpConverter())`. Also corrected the `open` statement from `open FSharp.SystemTextJson` to `open System.Text.Json.Serialization`. Verified via `dotnet fsi` test script before fixing.
- **Files modified:** `src/BlueCode.Cli/Adapters/Json.fs`
- **Verification:** `dotnet build BlueCode.slnx` → 0 errors after fix
- **Committed in:** `14fd169` (Task 2+3 commit)

---

**Total deviations:** 1 auto-fixed (1 bug — API method not present in pinned version)
**Impact on plan:** Fix is semantically equivalent — JsonFSharpConverter registered either way. jsonOptions singleton still provides the same F#-idiomatic JSON behavior. No scope creep.

## Issues Encountered

None beyond the API mismatch documented above.

## User Setup Required

None — no external service configuration required. vLLM server integration is Plan 02-03 scope.

## Next Phase Readiness

- Plan 02-02 can extend Json.fs (add schema loading) and QwenHttpClient.fs (add content extraction) without any fsproj restructuring — compile order is already correct
- Plan 02-03 replaces the stub `Error (SchemaViolation "...stub...")` in `CompleteAsync` with real HTTP round-trip, spinner, and full error mapping
- Core stays pure throughout — check-no-async passes, no FSharp.SystemTextJson/JsonSchema.Net/Spectre.Console in BlueCode.Core.fsproj
- `response_format: json_object` deliberately excluded from request body per 02-RESEARCH.md Finding 2 (deferred to Phase 5)

---
*Phase: 02-llm-client*
*Completed: 2026-04-22*
