---
phase: 02-llm-client
plan: "02"
subsystem: llm-client
tags: [fsharp, json-extraction, jsonschema-net, fsharp-systemtextjson, expecto, pipeline, schema-validation]

# Dependency graph
requires:
  - phase: 02-01
    provides: LlmWire.fs (LlmStep record), Json.fs (jsonOptions singleton), QwenHttpClient scaffold, compile order
  - phase: 01-foundation
    provides: Domain.fs (AgentError DU, MessageRole DU, LlmOutput, LlmStep-related types)
provides:
  - "4-stage JSON extraction pipeline (tryBareParse, tryParseJsonObject, extractFirstJsonObject, tryFenceExtract, extractLlmStep) — all private"
  - "JsonSchema.Net runtime validator (validateAndDeserialize, llmStepSchema) — validator private, schema public"
  - "parseLlmResponse: string -> Result<LlmStep, AgentError> — the single public pipeline entry point"
  - "LlmPipelineTests.fs with 13 tests covering extraction stages, schema violations, and MessageRole DU round-trip"
  - "BlueCode.Tests now references BlueCode.Cli for adapter-layer unit tests"
affects: ["02-03", "02-04", "phase-05"]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Extract-then-validate: extraction stages use JSON validity (tryParseJsonObject) not LlmStep shape, so schema validator sees original JSON including extra/missing fields"
    - "Private-by-default pipeline: all intermediate helpers are let private; single public entry point enforces schema validation cannot be bypassed"
    - "JsonFSharpConverter with WithUnionUnwrapFieldlessTags(true) produces F#-idiomatic bare-string DU encoding"

key-files:
  created:
    - "tests/BlueCode.Tests/LlmPipelineTests.fs"
  modified:
    - "src/BlueCode.Cli/Adapters/Json.fs"
    - "tests/BlueCode.Tests/BlueCode.Tests.fsproj"
    - "tests/BlueCode.Tests/RouterTests.fs"

key-decisions:
  - "extractLlmStep returns Result<string, AgentError> (JSON string, not LlmStep) so schema validator sees original JSON including extra/missing fields"
  - "jsonOptions uses JsonFSharpConverter(JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags(true)) — default JsonFSharpConverter() produces adjacent-tag {Case/Fields} form which fails LLM-04 invariant"
  - "tryParseJsonObject (JsonDocument.Parse + ValueKind check) used for extraction validity, NOT Deserialize<LlmStep> — separation of JSON validity from shape validity"
  - "fencePattern uses non-greedy [\\s\\S]*? — greedy match would span multiple code blocks"

patterns-established:
  - "Pipeline stage separation: extraction = find any JSON object; validation = enforce schema. Not combined."
  - "Round-trip through original JSON string: parseLlmResponse passes extracted JSON string directly to validateAndDeserialize, never re-serializes a partially-deserialized record"

# Metrics
duration: 6min
completed: 2026-04-22
---

# Phase 2 Plan 2: LLM Pipeline Summary

**4-stage JSON extraction pipeline + JsonSchema.Net validator delivering `parseLlmResponse: string -> Result<LlmStep, AgentError>` with 13 Expecto tests, closing LLM-02, LLM-03, and LLM-04**

## Performance

- **Duration:** 6 min
- **Started:** 2026-04-22T09:09:27Z
- **Completed:** 2026-04-22T09:16:15Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Pure 4-stage extraction pipeline covering bare JSON, prose-wrapped, markdown-fenced (with/without lang tag), and unrecoverable garbage
- JsonSchema.Net 9.2.0 runtime validation using `Evaluate()` with `OutputFormat.List` (not obsolete `Validate()`) — produces SchemaViolation with populated error detail
- 13 Expecto tests: 6 extraction, 6 schema, 1 DU round-trip (MessageRole round-trips as `"System"` not `{"Case":"System"}`)
- Proved LLM-04 (JsonFSharpConverter registered with UnionUnwrapFieldlessTags) via explicit assertion on serialized form

## Task Commits

Each task was committed atomically:

1. **Task 1: Extend Json.fs with extraction pipeline + schema validator** - `9d0cc41` (feat)
2. **Task 2: Wire test project and add LlmPipelineTests** - `3bbb282` (feat)

**Plan metadata:** `(pending docs commit)` (docs: complete plan)

## Files Created/Modified

- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/Adapters/Json.fs` — full extraction pipeline + schema; jsonOptions updated for DU encoding
- `/Users/ohama/projs/blueCode/tests/BlueCode.Tests/LlmPipelineTests.fs` — 13 new Expecto tests
- `/Users/ohama/projs/blueCode/tests/BlueCode.Tests/BlueCode.Tests.fsproj` — added BlueCode.Cli ProjectReference; LlmPipelineTests.fs added before RouterTests.fs
- `/Users/ohama/projs/blueCode/tests/BlueCode.Tests/RouterTests.fs` — added rootTests composition + updated entry point

## Json.fs Final Shape

### Public bindings (3)
- `jsonOptions : JsonSerializerOptions` — module-scope singleton with `JsonFSharpConverter(JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags(true))`
- `llmStepSchema : JsonSchema` — draft-2020-12 schema requiring `{thought (minLength:1), action (enum 5 values), input (object)}`, `additionalProperties: false`
- `parseLlmResponse : string -> Result<LlmStep, AgentError>` — single public entry

### Private helpers (7, all `let private`)
- `tryBareParse : string -> LlmStep option` — fast path: Deserialize<LlmStep> in try/with
- `tryParseJsonObject : string -> string option` — JSON validity check: JsonDocument.Parse + ValueKind = Object
- `extractFirstJsonObject : string -> string option` — stack-based O(N) brace extractor with `inString`/`escape` flags
- `fencePattern : Regex` — compiled non-greedy fence pattern
- `tryFenceExtract : string -> string option` — fence strip using fencePattern
- `extractLlmStep : string -> Result<string, AgentError>` — 3-stage composition returning JSON string
- `validateAndDeserialize : string -> Result<LlmStep, AgentError>` — schema evaluation + deserialization

### JSON Schema Literal (committed)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["thought", "action", "input"],
  "properties": {
    "thought": { "type": "string", "minLength": 1 },
    "action": {
      "type": "string",
      "enum": ["read_file", "write_file", "list_dir", "run_shell", "final"]
    },
    "input": { "type": "object" }
  },
  "additionalProperties": false
}
```

## Test Matrix

| Input | Expected Result |
|-------|----------------|
| `{"thought":"t","action":"list_dir","input":{"path":"."}}` | `Ok step` (Stage 1 bare) |
| `Sure! Here is the JSON: {...} Hope this helps!` | `Ok step` (Stage 2 brace scan) |
| ` ```json\n{...}\n``` ` | `Ok step` (Stage 3 fence with lang tag) |
| ` ```\n{...}\n``` ` | `Ok step` (Stage 3 fence without lang tag) |
| Nested `{"input":{"meta":{"deep":"v"}}}` | `Ok step` (brace depth tracking) |
| `I cannot help with that request.` | `Error (InvalidJsonOutput raw)` |
| Missing `action` field | `Error (SchemaViolation _)` |
| `action: "unknown_tool"` | `Error (SchemaViolation _)` |
| `thought: ""` | `Error (SchemaViolation _)` (minLength:1) |
| `input: "not-an-object"` | `Error (SchemaViolation _)` |
| Extra `confidence: 0.9` field | `Error (SchemaViolation _)` (additionalProperties) |
| All 5 valid actions | `Ok step` each |
| `MessageRole.System` via jsonOptions | `"System"` (not `{"Case":"System"}`) |

## LLM-04 Closure

`MessageRole.System` serialized through `jsonOptions` produces `"System"` (bare string) not `{"Case":"System"}` (adjacent-tag form). Assertion in test confirms `not (serialized.Contains("\"Case\""))`. This proves `JsonFSharpConverter(JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags(true))` is correctly registered.

**Observed serialized form:** `"System"` ✓

## Decisions Made

1. **extractLlmStep returns `Result<string, AgentError>` (JSON string) not `Result<LlmStep, AgentError>`**: Using `Deserialize<LlmStep>` for extraction validity would reject JSON with missing required fields before schema validation could see it, producing `InvalidJsonOutput` instead of `SchemaViolation`. The extraction stage's job is to find a JSON object; the schema stage enforces field correctness. Separation is critical.

2. **`jsonOptions` updated to use `WithUnionUnwrapFieldlessTags(true)`**: Default `JsonFSharpConverter()` produces `{"Case":"System"}` for fieldless DU cases. This fails the LLM-04 invariant. Updated to `JsonFSharpConverter(JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags(true))` to produce bare-string form. No impact on `LlmStep` record serialization (records are unaffected by this option).

3. **`tryParseJsonObject` added as 6th private helper**: Required to separate "is this a valid JSON object?" from "is this a valid LlmStep?". The plan listed 5 required private helpers; adding a 6th supporting one is within the spirit of the design. All 5 plan-specified helpers remain present.

4. **`parseLlmResponse` passes original extracted JSON to `validateAndDeserialize`**: Do not re-serialize a partially-deserialized record. Re-serialization would drop extra fields, causing `additionalProperties: false` violations to silently pass. The original JSON string preserves all fields for schema evaluation.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] extractLlmStep used LlmStep deserialization for extraction validity check**
- **Found during:** Task 2 (test execution — 2 of 13 tests failing)
- **Issue:** Plan's `tryBareParse` (Deserialize<LlmStep>) was used as the extraction stage validity check. JSON with missing required fields (e.g., no `action`) failed deserialization, producing `InvalidJsonOutput` instead of reaching schema validation and producing `SchemaViolation`. JSON with extra fields passed extraction but re-serialization dropped extra fields before schema validation.
- **Fix:** Added `tryParseJsonObject` (JsonDocument.Parse + ValueKind.Object check) as the extraction validity test. `extractLlmStep` now returns `Result<string, AgentError>` (raw JSON string). `parseLlmResponse` passes the original JSON string to `validateAndDeserialize`. `tryBareParse` retained as a private helper per plan spec.
- **Files modified:** `src/BlueCode.Cli/Adapters/Json.fs`
- **Verification:** All 29 tests pass including "missing required 'action' field -> SchemaViolation" and "extra field -> SchemaViolation"
- **Committed in:** 3bbb282 (Task 2 commit, bundled with test addition)

**2. [Rule 1 - Bug] jsonOptions used default JsonFSharpConverter() producing adjacent-tag DU encoding**
- **Found during:** Task 2 (would have failed LLM-04 round-trip test)
- **Issue:** Default `JsonFSharpConverter()` produces `{"Case":"System"}` for fieldless DUs. The test asserts `not (serialized.Contains("\"Case\""))` — this would fail.
- **Fix:** Changed to `JsonFSharpConverter(JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags(true))` to produce `"System"`.
- **Files modified:** `src/BlueCode.Cli/Adapters/Json.fs`
- **Verification:** `MessageRole.System` serializes as `"System"` — DU round-trip test passes
- **Committed in:** 9d0cc41 (Task 1 commit)

---

**Total deviations:** 2 auto-fixed (2 Rule 1 bugs)
**Impact on plan:** Both fixes necessary for correctness. The extraction bug meant structurally-invalid JSON would produce wrong error type. The DU encoding bug would fail the LLM-04 guarantee. No scope creep.

## Known Limitations

- **Unclosed fences**: A markdown fence without closing ` ``` ` does not match `fencePattern` and falls through to `InvalidJsonOutput`. This is intentional — an unclosed fence signals max_tokens truncation, which is not recoverable by the parser. The agent loop's retry policy (PITFALLS D-6) handles this at a higher level.
- **JSON arrays as top-level**: The extraction pipeline only handles top-level JSON objects (`{...}`). A top-level array response from the LLM will produce `InvalidJsonOutput`. This is correct — the `LlmStep` schema requires an object.
- **Round-trip optimization**: `parseLlmResponse` passes the extracted JSON string directly to `validateAndDeserialize`. A future optimization could skip re-parsing by threading the `JsonDocument.RootElement` from extraction through to schema validation (02-RESEARCH Finding 4 note).

## Issues Encountered

None beyond the 2 auto-fixed bugs documented above.

## Next Phase Readiness

**Plan 02-03 (QwenHttpClient wiring)** is ready to proceed. It needs to:
- Wire `parseLlmResponse` into `QwenHttpClient.CompleteAsync` (replace the stub)
- Add Spectre.Console spinner around HTTP call only
- Add full HTTP error mapping (4xx/5xx, TaskCanceledException disambiguation)
- Add `toLlmOutput: LlmStep -> Result<LlmOutput, AgentError>` for final step

`parseLlmResponse` is now the stable, tested entry point. `jsonOptions` and `llmStepSchema` are both public and accessible from `QwenHttpClient.fs` which already `open`s `BlueCode.Cli.Adapters.Json`.

---
*Phase: 02-llm-client*
*Completed: 2026-04-22*
