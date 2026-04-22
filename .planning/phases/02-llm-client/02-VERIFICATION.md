---
phase: 02-llm-client
verified: 2026-04-22T18:28:00Z
status: passed
score: 13/13 must-haves verified
re_verification: false
---

# Phase 2: LLM Client Verification Report

**Phase Goal:** A POST to localhost:8000 with a prompt returns a validated `LlmStep` record; every failure mode maps to a typed `AgentError` before leaving the adapter.
**Verified:** 2026-04-22T18:28:00Z
**Status:** PASSED
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | POST returns a parsed `LlmStep { thought; action; input }` record with schema-validated fields | VERIFIED | `parseLlmResponse` pipeline (Json.fs 254 lines) + `LlmPipelineTests.fs` 13 tests all pass |
| 2 | Prose-wrapped or markdown-fenced JSON is correctly extracted rather than failing | VERIFIED | Stage 2 brace-scan + Stage 3 fence-strip in `extractLlmStep`; 4 extraction tests pass including both with/without `json` tag |
| 3 | Unrecoverable JSON returns `AgentError.InvalidJsonOutput` (no unhandled exception) | VERIFIED | Stage 4 of `extractLlmStep` returns `Error (InvalidJsonOutput content)`; test "unparseable prose returns InvalidJsonOutput" passes |
| 4 | `HttpRequestException` maps to `AgentError.LlmUnreachable`; no exception propagates | VERIFIED | `postAsync` catch block: `:? HttpRequestException as ex -> return Error (LlmUnreachable (url, ex.Message))`; no exception escapes adapter |
| 5 | Spectre.Console spinner visible during HTTP wait, disappears on response | VERIFIED (code) / MANUAL (visual) | `withSpinner` wrapping only `postAsync` confirmed at line 217 via `AnsiConsole.Status()`; visual confirmation requires live run |
| 6 | `ILlmClient` port uses `Message list` not `string list` | VERIFIED | Ports.fs line 13: `messages : Message list` |
| 7 | `MessageRole` DU and `Message` record added to Domain.fs | VERIFIED | Domain.fs lines 140-152; DU round-trip test in `LlmPipelineTests` passes |
| 8 | `modelToName` + `modelToTemperature` present in Router.fs with correct values | VERIFIED | Router.fs: Qwen32B->0.2, Qwen72B->0.4; names match vLLM model IDs |
| 9 | Three NuGet packages in BlueCode.Cli; none leaked into BlueCode.Core | VERIFIED | Cli.fsproj: 3 PackageReferences (FSharp.SystemTextJson 1.4.36, JsonSchema.Net 9.2.0, Spectre.Console 0.55.2); Core.fsproj: zero matches |
| 10 | `Json.fs` has 5+ private helpers; uses `.Evaluate()` not `.Validate()` | VERIFIED | 7 `let private` bindings; `llmStepSchema.Evaluate(doc.RootElement, opts)` confirmed; zero `.Validate(` matches |
| 11 | `toLlmOutput` is public; handles all 5 cases (FinalAnswer + 4 ToolCall paths) | VERIFIED | Declared `let toLlmOutput` (no `private`); 5 tests in `ToLlmOutputTests.fs` all pass |
| 12 | Compile order in Cli.fsproj: LlmWire -> Json -> QwenHttpClient -> Program | VERIFIED | BlueCode.Cli.fsproj ItemGroup order confirmed |
| 13 | Build: 0 warnings, 0 errors; 34 tests pass (1 smoke skipped) | VERIFIED | `dotnet build`: 0 warnings, 0 errors; `dotnet run --project tests/BlueCode.Tests`: 34 passed, 1 ignored, 0 failed |

**Score:** 13/13 truths verified

---

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/BlueCode.Core/Domain.fs` | MessageRole DU + Message record added | VERIFIED | Lines 140-152; 8 existing DUs unchanged |
| `src/BlueCode.Core/Router.fs` | modelToName + modelToTemperature with 0.2/0.4 | VERIFIED | Functions present; Qwen32B->0.2, Qwen72B->0.4 |
| `src/BlueCode.Core/Ports.fs` | ILlmClient uses `Message list` | VERIFIED | Line 13: `messages : Message list` |
| `src/BlueCode.Cli/BlueCode.Cli.fsproj` | 3 packages, correct compile order | VERIFIED | 3 PackageReferences; 4-file compile order matches spec |
| `src/BlueCode.Cli/Adapters/LlmWire.fs` | LlmStep { thought; action; input } | VERIFIED | 26 lines; record with correct fields |
| `src/BlueCode.Cli/Adapters/Json.fs` | jsonOptions singleton; parseLlmResponse public; 5+ private helpers; .Evaluate() | VERIFIED | 262 lines; 7 private helpers; .Evaluate() confirmed; parseLlmResponse public |
| `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` | withSpinner around postAsync only; 4-branch error mapping; toLlmOutput public | VERIFIED | 229 lines; spinner wraps postAsync at line 217; 4 error branches in postAsync; toLlmOutput public |
| `tests/BlueCode.Tests/LlmPipelineTests.fs` | 13 tests: 6 extraction + 6 schema + 1 DU round-trip | VERIFIED | 6 extraction tests (including Stage 1-4 + nested + fence variants), 6 schema tests, 1 DU round-trip = 13 total |
| `tests/BlueCode.Tests/ToLlmOutputTests.fs` | 5 tests for toLlmOutput branches | VERIFIED | 5 testCase instances covering FinalAnswer, 3 SchemaViolation cases, ToolCall passthrough |
| `tests/BlueCode.Tests/SmokeTests.fs` | env-gated on BLUECODE_SMOKE_TEST=1 | VERIFIED | `smokeEnabled()` checks env var; skips cleanly (1 ignored in default run) |
| `tests/BlueCode.Tests/BlueCode.Tests.fsproj` | ProjectReference to BlueCode.Cli | VERIFIED | ProjectReference to `..\..\src\BlueCode.Cli\BlueCode.Cli.fsproj` present |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `QwenHttpClient.create()` | `ILlmClient` | `{ new ILlmClient with ... }` | WIRED | Interface implementation in `create()` function |
| `CompleteAsync` | `postAsync` | `withSpinner label (fun () -> postAsync url body ct)` | WIRED | Spinner wraps HTTP call only |
| `CompleteAsync` | `extractContent` | `match extractContent url responseJson with` | WIRED | Envelope parsing after HTTP success |
| `CompleteAsync` | `parseLlmResponse` | `match parseLlmResponse content with` | WIRED | Pipeline called with choices[0].message.content |
| `CompleteAsync` | `toLlmOutput` | `return toLlmOutput step` | WIRED | LlmStep -> LlmOutput DU mapping |
| `parseLlmResponse` | `validateAndDeserialize` | `validateAndDeserialize json` | WIRED | Schema validation layered on extraction |
| `withSpinner` | `AnsiConsole.Status()` | `.StartAsync(label, fun _ctx -> work ())` | WIRED | Spectre spinner wraps the task |
| `buildRequestBody` | `modelToName` + `modelToTemperature` | `Router.modelToName model`, `Router.modelToTemperature model` | WIRED | Per-model values from Router |
| `QwenHttpClient.fs` | `Json.fs` (jsonOptions) | `open BlueCode.Cli.Adapters.Json` | WIRED | jsonOptions used in buildRequestBody serialization |

---

### Success Criterion Verdict

| SC | Description | Status | Evidence |
|----|-------------|--------|----------|
| SC-1 | POST returns parsed `LlmStep { thought; action; input }` with schema-validated fields | MANUAL GATE | SmokeTests.fs exists as env-gated live test (`BLUECODE_SMOKE_TEST=1`). Code inspection confirms full pipeline: HTTP -> extractContent -> parseLlmResponse -> toLlmOutput. Cannot automate without live vLLM. |
| SC-2 | Prose-wrapped or markdown-fenced JSON correctly extracted | PASSED | LlmPipelineTests: "prose-wrapped JSON extracted via brace scan", "markdown-fenced JSON with 'json' tag", "markdown-fenced JSON WITHOUT 'json' tag" — all pass |
| SC-3 | Unrecoverable JSON -> `AgentError.InvalidJsonOutput` (not exception) | PASSED | LlmPipelineTests: "unparseable prose returns InvalidJsonOutput" asserts `Error (InvalidJsonOutput raw)` where raw = original content |
| SC-4 | `HttpRequestException` -> `AgentError.LlmUnreachable`; no exception propagates | PASSED (code inspection) | QwenHttpClient.fs line 91-92: `:? HttpRequestException as ex -> return Error (LlmUnreachable (url, ex.Message))`. All 4 error branches return `Error` values; no re-throw. Full live test requires manual gate. |
| SC-5 | Spectre.Console spinner visible during HTTP wait | PASSED (code) / MANUAL (visual) | `withSpinner` at line 179 uses `AnsiConsole.Status().Spinner(Spinner.Known.Dots)`. Wraps `postAsync` only (line 217). Visual confirmation requires running against live endpoint. |

**SC-1 is a manual gate:** Set `BLUECODE_SMOKE_TEST=1` and ensure `localhost:8000` (32B) is serving before running to verify the live round-trip.

---

### Requirements Coverage

| Requirement | Status | Verification |
|-------------|--------|--------------|
| LLM-01 | SATISFIED | `ILlmClient.CompleteAsync` accepts `Message list`; `Message`/`MessageRole` in Domain.fs |
| LLM-02 | SATISFIED | Multi-stage extraction (bare, brace-scan, fence-strip) in `extractLlmStep`; SC-2 tests pass |
| LLM-03 | SATISFIED | `validateAndDeserialize` uses JsonSchema.Net `.Evaluate()` with draft-2020-12 schema enforcing thought/action/input shapes |
| LLM-04 | SATISFIED | `jsonOptions` uses `JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags(true)`; DU round-trip test passes — MessageRole serializes as `"System"` not `{"Case":"System"}` |
| LLM-05 | SATISFIED | `modelToTemperature` in Router.fs: Qwen32B->0.2, Qwen72B->0.4; consumed in `buildRequestBody` (private, not CLI-exposed) |
| LLM-06 | SATISFIED | All error paths in `postAsync` and `extractContent` return typed `AgentError` via `Result`; no exception escapes the adapter |

---

### Anti-Patterns Found

| File | Pattern | Severity | Finding |
|------|---------|----------|---------|
| All Phase 2 files | Agent loop code (Phase 4) | Check | None found — no `runSession`, `AgentLoop`, `Serilog`, `retry`, `JSONL`, `stepLog` |
| All Phase 2 files | Tool handler implementations (Phase 3) | Check | None found — no `FsToolExecutor`, `executeReadFile` |
| All Phase 2 files | CLI arg parsing (Phase 5) | Check | None found — no `Argu`, `ArgParser` |
| QwenHttpClient.fs | `response_format` field | Check | Appears only in comment (line 37); not in the request body object — correct |

No blocker or warning anti-patterns found.

---

### Runnable Verification Results

**1. Build — 0 warnings, 0 errors**
```
dotnet build BlueCode.slnx
→ BlueCode.Core, BlueCode.Cli, BlueCode.Tests: built
→ 0 warnings, 0 errors
```

**2. Test suite — 34 passed, 1 ignored (smoke), 0 failed**
```
dotnet run --project tests/BlueCode.Tests
→ 34 tests run in 00:00:00.185
→ 34 passed, 1 ignored, 0 failed, 0 errored. Success!
  - LlmPipeline: 13 tests (6 extraction + 6 schema + 1 DU round-trip)
  - ToLlmOutput: 5 tests
  - Router: 16 tests
  - Smoke: 1 skipped (BLUECODE_SMOKE_TEST not set)
```

**3. check-no-async.sh — exit 0**
```
bash scripts/check-no-async.sh
→ OK: no async {} expressions in src/BlueCode.Core
→ exit code: 0
```

**4. Package count — 3 (meets ≥3)**
```
grep -c "<PackageReference" src/BlueCode.Cli/BlueCode.Cli.fsproj
→ 3
```

**5. Core purity — 0 matches (packages not in Core)**
```
grep "FSharp.SystemTextJson|JsonSchema.Net|Spectre.Console" src/BlueCode.Core/BlueCode.Core.fsproj
→ (no output — correct)
```

**6. Private let count — 7 (meets ≥5)**
```
grep -c "let private " src/BlueCode.Cli/Adapters/Json.fs
→ 7
```

**7. .Validate() — 0 matches (correct)**
```
grep "\.Validate(" src/BlueCode.Cli/Adapters/Json.fs
→ (no output — correct; .Evaluate() is used)
```

**8. string list — 0 matches (correct)**
```
grep "messages: string list" src/BlueCode.Core/Ports.fs
→ (no output — correct; Message list is used)
```

**9. buildRequestBody + postAsync — 2 matches**
```
grep "let private buildRequestBody|let private postAsync" src/BlueCode.Cli/Adapters/QwenHttpClient.fs
→ let private buildRequestBody (messages: Message list) (model: Model) : string =
→ let private postAsync
```

**10. AnsiConsole.Status — 1 match**
```
grep "AnsiConsole.Status" src/BlueCode.Cli/Adapters/QwenHttpClient.fs
→     AnsiConsole.Status()
```

**11. response_format in src/BlueCode.Cli — comment only**
```
grep -r "response_format" src/BlueCode.Cli/
→ QwenHttpClient.fs:37: /// No response_format field — comment only, not in request body
```

**12. toLlmOutput is public**
```
grep "^let toLlmOutput|^let private toLlmOutput" src/BlueCode.Cli/Adapters/QwenHttpClient.fs
→ let toLlmOutput (step: LlmStep) : Result<LlmOutput, AgentError> =  (no "private")
```

---

### Human Verification Required

**SC-1: Live POST round-trip**
- Test: Set `BLUECODE_SMOKE_TEST=1`, ensure `localhost:8000` (Qwen32B) is serving, run `dotnet run --project tests/BlueCode.Tests`
- Expected: Smoke test passes — `CompleteAsync` returns `Ok (ToolCall ...)` or `Ok (FinalAnswer ...)`
- Why human: Requires live vLLM endpoint; cannot mock within automated test framework

**SC-5: Spinner visual confirmation**
- Test: With BLUECODE_SMOKE_TEST=1 and live endpoint, observe terminal during `CompleteAsync` call
- Expected: Cyan Dots spinner labeled "Thinking... [32B]" visible during HTTP wait; disappears when response arrives
- Why human: TTY rendering cannot be asserted programmatically

---

## Summary

Phase 2 goal is fully achieved by the actual codebase. All 13 must-haves pass automated verification. The pipeline is correctly wired end-to-end: `CompleteAsync` -> `withSpinner(postAsync)` -> `extractContent` -> `parseLlmResponse` -> `toLlmOutput`. All 5 failure modes map to typed `AgentError` values with no exception propagation. Two items (SC-1 live round-trip, SC-5 spinner visual) require a human with a running vLLM instance but the code structure is fully correct for both.

---
_Verified: 2026-04-22T18:28:00Z_
_Verifier: Claude (gsd-verifier)_
