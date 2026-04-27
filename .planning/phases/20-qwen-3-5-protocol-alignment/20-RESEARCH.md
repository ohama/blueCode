# Phase 20: Qwen 3.5 Protocol Alignment — Research

**Researched:** 2026-04-27
**Domain:** F# HTTP client adapter; LLM sampling parameters; multi-role message injection; content extraction fallback
**Confidence:** HIGH — all findings are verified via direct file reads; no speculation

---

## Summary

Phase 20 updates three mechanical mismatches between blueCode's Qwen 2.5-era assumptions and Qwen 3.5's documented conventions. All changes are confined to `src/BlueCode.Cli/Adapters/QwenHttpClient.fs`, `src/BlueCode.Core/Router.fs`, and `src/BlueCode.Core/AgentLoop.fs`. The bench gate (`bench/run.sh --gate`, 6 invocations) is the regression authority after every plan.

**Primary recommendation:** Execute plans in order (20-01 → 20-02 → 20-03). Each plan is independently gate-verifiable. Do not combine plans into a single commit batch.

---

## Files to Read/Touch Per Plan

### Plan 20-01: Sampling parameters + timeout

**`src/BlueCode.Cli/Adapters/QwenHttpClient.fs`**
- Line 34: `c.Timeout <- TimeSpan.FromSeconds(180.0)` — change to 300.0. Comment on line 31 says "180s timeout covers 72B worst case (~60s)" — update comment to cite 122B cold-start 240s justification.
- Lines 65–71: `buildRequestBody` anonymous record. Current fields: `model`, `messages`, `temperature`, `max_tokens = 1024`, `presence_penalty = 1.5`, `stream = false`. Add `top_p = 0.8`, `top_k = 20`; change `presence_penalty = 1.5` → `0.0`. Temperature is injected via `modelToTemperature model` — that function also changes.
- Line 121: Error message hardcodes `"request timed out after 180s"` — change to 300s.
- Line 84: Docblock timeout comment also says `"request timed out after 180s"` — update.
- Line 46–47: Docblock for `buildRequestBody` mentions `presence_penalty=1.5 (Qwen model-card)` — update to cite Qwen 3.5 values.

**`src/BlueCode.Core/Router.fs`**
- Lines 64–68: `modelToTemperature` returns 0.2 (Qwen35B) / 0.4 (Qwen122B). Qwen 3.5 model card non-thinking coding: 0.7 for both. Function rename or replacement: ROADMAP says expose `modelToSamplingParams`.
- Lines 62–68: Docblock says "35B uses 0.2 (precise code edits); 122B uses 0.4" — update.

**`src/BlueCode.Core/Domain.fs`**
- Confirmed: NO existing `SamplingParams` type in Domain.fs (lines 1–230 read). If planner chooses a record route, it would slot after line 229 (end of `Message` record), before any future extensions.

**`documentation/qwen35-install.md`**
- Gotcha row 6 in Appendix A (line 973): `'sampling-parameter mismatch' → §6 + sampling params fix` — add "RESOLVED Phase 20-01" marker.
- Section §8 title: "콜드 스타트 + blueCode 180s timeout 회피" and §8.1 explanation reference the 180s limit and say "Timeout = 300s로 늘리는 코드 변경도 가능하나 본 Phase OOS" (line 780). Update §8.1 to reflect Phase 20-01 resolution.

**`CLAUDE.md`**
- §Common Gotchas "Connection refused or 180s timeout" — update 180s → 300s.

---

### Plan 20-02: `extractContent` reasoning_content fallback

**`src/BlueCode.Cli/Adapters/QwenHttpClient.fs`**
- Lines 134–143: `extractContent` currently reads `choices[0].message.content` only. When `content` is empty or null, the function returns `Ok ""` (empty string propagates to `parseLlmResponse` which will call `InvalidJsonOutput`). Need to add: if content is null/empty, try `choices[0].message.reasoning_content`; if that is non-empty, return `Ok reasoning_content`; otherwise return `Error(LlmUnreachable ...)`.
- The exact `extractContent` logic (verified line 134–143):
  ```fsharp
  let content =
      doc.RootElement.GetProperty("choices").[0].GetProperty("message").GetProperty("content").GetString()
  Ok content
  ```
  `GetString()` returns `null` on a JSON null value; returns `""` on an empty string. The catch-all maps structural exceptions to `LlmUnreachable`. The fix needs to handle both null and empty string before the `Ok content` path.

**New test in existing test file** (`tests/BlueCode.Tests/LlmPipelineTests.fs` or `ModelsProbeTests.fs`)
- The cleanest home is `LlmPipelineTests.fs` (already covers extraction) or a new `QwenHttpClientTests.fs`. However `extractContent` is `private` — tests must either (a) test via the public `CompleteAsync` mock path, or (b) the function must be made `let internal` / extracted to a testable module, or (c) a new integration-style test feeds a mock response JSON and checks for `Ok content`.
- IMPORTANT: `extractContent` is `let private` (line 134). To unit-test it directly requires either exposing it (e.g., `let internal`) or testing indirectly. The existing pattern in this codebase (e.g., `toLlmOutput` was made public explicitly for testing) suggests making `extractContent` `let internal` for testability is acceptable.
- Alternatively, since `tryParseModelId` / `tryParseMaxModelLen` are already `PUBLIC` helpers in the same module (`QwenHttpClient.fs`, lines 270 and 305), a similar `let extractContentFromJson` pure function can be carved out as a public helper and tested directly — analogous to how `toLlmOutput` is public. The `extractContent` wrapper would call it.

**`documentation/qwen35-install.md`**
- §5.3 response table last row (line 521): `'빈 문자열 + reasoning_content 필드에 응답' → §6 + extractContent 패치 필요` — add "RESOLVED Phase 20-02".
- Appendix A gotcha row 5 (line 973): `'content 빈 문자열' → reasoning_content에 응답이 있고 content가 비어있음 → §6 + extractContent 패치 필요` — add "RESOLVED Phase 20-02".

---

### Plan 20-03: Role=System probe + conditional restore

**External probe** (curl, not F# code)
- Target: `http://127.0.0.1:8001/v1/chat/completions` (122B)
- Test: multi-turn conversation with messages array containing `role: "system"` mid-conversation (not just the first message), e.g. `[{system, base prompt}, {user, q1}, {assistant, a1}, {system, constraint}]`
- Expected: does mlx_lm.server 0.31.3 accept mid-turn system messages without error or stripping?
- Document result as ACCEPT or REJECT in a committed probe script.

**`src/BlueCode.Core/AgentLoop.fs`**
- Lines containing `Role = User` injection sites (verified by grep):
  - Line 172: `{ Role = User` — inside `callLlmWithRetry`, the PARSE ERROR correction message
  - Line 249: `{ Role = User` — POST-EDIT CONSTRAINT message (inside `buildMessages`)
  - Line 260: `{ Role = User` — POST-READ HINT "truncated" case
  - Line 266: `{ Role = User` — POST-READ HINT "out-of-range" case
- ROADMAP SC 4 cites "AgentLoop.fs:249,259,265" but exact line numbers from current file read are 249/260/266. The lines 260 and 266 are the `withEdit @` additions (post-read hints in the `match lastReadHint` block), and line 249 is the POST-EDIT CONSTRAINT.
- If probe verdict is ACCEPT: change `Role = User` → `Role = System` at lines 249, 260, 266 (the three injection sites in `buildMessages`; NOT line 172 which is a different site in `callLlmWithRetry`).
- Line 172 (PARSE ERROR correction) uses `Role = User` — this is a separate site not covered by ROADMAP SC4. Leave it as-is regardless of probe outcome (it is not a post-tool injection; it is an error correction turn).

**`documentation/howto/enforce-llm-tool-terminality-via-post-user-injection.md`**
- Lines 110–113 (code snippet in Step 2): shows `Role = System` — this was the pre-Phase 17-02 form that was changed to `User`. If probe says ACCEPT and we restore System, the doc is already correct.
- Lines 185–189 (Example section "Good" code snippet): also shows `Role = System`. Same as above.
- If probe verdict is REJECT (keep User role): the doc's F# snippets show `System` but current code uses `User`. Add a note: "Current code uses `Role = User` (Phase 17-02 compatibility with 35B chat template); 122B confirmed via Phase 20-03 probe to reject System role here as well."
- Either way the doc needs alignment to current code state.

**`documentation/qwen35-install.md`**
- Appendix A gotcha row for "thinking mode" / system role — add Phase 20-03 probe result as a reference note.

---

## API/Code Shapes

### Current `buildRequestBody` (QwenHttpClient.fs:57–73)

```fsharp
let private buildRequestBody (messages: Message list) (model: Model) (modelId: string) : string =
    let req =
        {| model = modelId
           messages = msgArr
           temperature = modelToTemperature model
           max_tokens = 1024
           presence_penalty = 1.5
           stream = false |}
```

### Target shape after 20-01 (two options; planner decides)

**Option A — inline SamplingParams tuple (no Domain.fs change):**

```fsharp
let private buildRequestBody (messages: Message list) (model: Model) (modelId: string) : string =
    let (temp, topP, topK, presP) = modelToSamplingParams model
    let req =
        {| model = modelId
           messages = msgArr
           temperature = temp
           top_p = topP
           top_k = topK
           max_tokens = 1024
           presence_penalty = presP
           stream = false |}
```

**Option B — SamplingParams record in Domain.fs:**

```fsharp
// Domain.fs (new, after line 229)
type SamplingParams =
    { Temperature: float
      TopP: float
      TopK: int
      PresencePenalty: float }

// Router.fs
let modelToSamplingParams: Model -> SamplingParams =
    function
    | Qwen35B  -> { Temperature = 0.7; TopP = 0.8; TopK = 20; PresencePenalty = 0.0 }
    | Qwen122B -> { Temperature = 0.7; TopP = 0.8; TopK = 20; PresencePenalty = 0.0 }

// QwenHttpClient.fs
let sp = modelToSamplingParams model
let req =
    {| model = modelId
       messages = msgArr
       temperature = sp.Temperature
       top_p = sp.TopP
       top_k = sp.TopK
       max_tokens = 1024
       presence_penalty = sp.PresencePenalty
       stream = false |}
```

**Note:** Both models use the same values (0.7/0.8/20/0.0) per Qwen 3.5 model card. The exhaustive pattern match still covers both cases — adding a new Model case is still a compile error.

### Current `extractContent` (QwenHttpClient.fs:134–143)

```fsharp
let private extractContent (url: string) (responseJson: string) : Result<string, AgentError> =
    try
        use doc = JsonDocument.Parse(responseJson)
        let content =
            doc.RootElement.GetProperty("choices").[0].GetProperty("message").GetProperty("content").GetString()
        Ok content
    with ex ->
        Error(LlmUnreachable(url, sprintf "malformed response: %s" ex.Message))
```

### Target shape after 20-02:

```fsharp
// New public helper (testable; analogous to tryParseModelId/tryParseMaxModelLen pattern)
let extractContentFromJson (responseJson: string) : string option =
    try
        use doc = JsonDocument.Parse(responseJson)
        let msg = doc.RootElement.GetProperty("choices").[0].GetProperty("message")
        let content =
            match msg.TryGetProperty("content") with
            | true, el when el.ValueKind = JsonValueKind.String ->
                let s = el.GetString()
                if not (System.String.IsNullOrEmpty(s)) then Some s else None
            | _ -> None
        match content with
        | Some s -> Some s
        | None ->
            match msg.TryGetProperty("reasoning_content") with
            | true, el when el.ValueKind = JsonValueKind.String ->
                let s = el.GetString()
                if not (System.String.IsNullOrEmpty(s)) then Some s else None
            | _ -> None
    with _ -> None

let private extractContent (url: string) (responseJson: string) : Result<string, AgentError> =
    match extractContentFromJson responseJson with
    | Some content -> Ok content
    | None -> Error(LlmUnreachable(url, "malformed response: no content or reasoning_content"))
```

### Current `modelToTemperature` (Router.fs:65–68)

```fsharp
let modelToTemperature: Model -> float =
    function
    | Qwen35B -> 0.2
    | Qwen122B -> 0.4
```

If planner chooses Option A (tuple), `modelToTemperature` is replaced by:

```fsharp
/// Per-model sampling parameters per Qwen 3.5 model card (non-thinking coding mode).
/// temperature=0.7, top_p=0.8, top_k=20, presence_penalty=0.0 for both models.
let modelToSamplingParams: Model -> float * float * int * float =
    function
    | Qwen35B  -> (0.7, 0.8, 20, 0.0)
    | Qwen122B -> (0.7, 0.8, 20, 0.0)
```

If planner chooses Option B (record), use the SamplingParams shape above.

---

## Risks & Pitfalls

### Pitfall 1: `top_k` field type — int vs float
**What goes wrong:** JSON serialization of `top_k = 20` via `System.Text.Json` produces `20` (integer). Some servers expect `20.0` (float). mlx_lm.server OpenAI-compat layer accepts integer for `top_k` per the sampling parameter docs, so this should be fine. However, the anonymous record field must be typed `int`, not `float`, to serialize as `20` not `20.0`.
**Mitigation:** Use `top_k = 20` (int literal) in the anonymous record — F# infers `int`. Verify with `--trace` that the POST body contains `"top_k":20` not `"top_k":20.0`.

### Pitfall 2: `extractContent` `GetString()` behavior with JSON null
**What goes wrong:** `JsonElement.GetString()` returns `null` (C# null, F# maps to `null` for string) when the JSON value is `null`, NOT an exception. The current code does `Ok content` which would return `Ok null` — then downstream `parseLlmResponse null` would throw or produce `InvalidJsonOutput`. After the fix, `System.String.IsNullOrEmpty(null)` returns `true`, so the null case is correctly routed to `reasoning_content`.
**Confidence:** HIGH — verified via .NET docs behavior of JsonElement.GetString on null.

### Pitfall 3: `postAsync` error message hardcode
**What goes wrong:** Line 121 of QwenHttpClient.fs says `"request timed out after 180s"` — must be updated to `"request timed out after 300s"` alongside the `Timeout` change on line 34. Missing this update creates confusing diagnostic output.
**Warning sign:** `grep "180s" src/` should return 0 after 20-01 is done.

### Pitfall 4: `modelToTemperature` function deletion breaks tests
**What goes wrong:** If `modelToTemperature` is deleted (rather than renamed), any tests or code that reference it will fail. There are no direct unit tests for `modelToTemperature` in RouterTests.fs (confirmed: RouterTests.fs tests `classifyIntent`, `intentToModel`, `modelToEndpoint`, `endpointToUrl` — NOT `modelToTemperature`). The function is referenced only in `QwenHttpClient.fs:68`. Safe to replace/rename.
**Mitigation:** Replace `modelToTemperature` with `modelToSamplingParams` in one atomic change.

### Pitfall 5: Test count baseline — must reach ≥263
**Current state:** Test suite passes 262/1/0 (verified). Adding ≥1 test in 20-02 brings it to ≥263. ROADMAP SC6 requires 263-265/1/0. If the new test module is a new file, it must be added to BOTH `BlueCode.Tests.fsproj` (before `RouterTests.fs`) AND `rootTests` list in `RouterTests.fs`. See CLAUDE.md "Test discovery pattern" gotcha.
**If tests added to existing file** (e.g., `LlmPipelineTests.fs`): no fsproj/rootTests change needed — just add testCase entries to an existing `testList`.

### Pitfall 6: `extractContent` is `let private` — cannot be tested directly
**What goes wrong:** Unit testing the fallback logic requires either (a) making the helper `let internal` / exposing a pure `extractContentFromJson`, or (b) testing via the full `CompleteAsync` pipeline with a mock server response. The codebase pattern (see `toLlmOutput` made public for `ToLlmOutputTests.fs`) strongly favors extracting a pure public helper. The recommended shape above (`extractContentFromJson : string -> string option`) follows this pattern.
**Confidence:** HIGH — consistent with codebase conventions.

### Pitfall 7: 122B cold-start during bench gate with new 300s timeout
**What goes wrong:** Raising the timeout to 300s means the bench gate pre-condition check (`curl -fsS http://127.0.0.1:8001/v1/models`) still needs to pass before running. The gate already includes this check (bench/run.sh:125–130). No change needed in the bench harness; the gate pre-condition is independent of the HttpClient timeout.
**What changes:** blueCode will now wait up to 300s instead of 180s before surfacing `LlmUnreachable`. This is the desired behavior.

### Pitfall 8: AgentLoop.fs line 172 (PARSE ERROR Role=User) is NOT a 20-03 target
**What goes wrong:** Grep shows 4 `Role = User` sites in AgentLoop.fs. Line 172 is the PARSE ERROR correction message in `callLlmWithRetry` — this is NOT one of the three post-tool injection sites. Changing it to System would be wrong (it is a user-visible error message, not a constraint). Only lines 249/260/266 are in scope for 20-03.

### Pitfall 9: Document state of howto doc (Role=System vs Role=User)
**What goes wrong:** The howto doc at `enforce-llm-tool-terminality-via-post-user-injection.md` lines 110–113 and 185–189 show `Role = System` in F# snippets, but the current code (AgentLoop.fs lines 249/260/266) uses `Role = User` (Phase 17-02 fix). The doc was not updated in v1.1. This is a stale doc issue — 20-03 must reconcile it regardless of probe verdict.

---

## Open Decisions for the Planner

### OD-1: SamplingParams shape — new Domain.fs record vs inline tuple vs anonymous record
**What we know:** Both Qwen 3.5 models use identical sampling parameters (0.7/0.8/20/0.0). Domain.fs has no existing `SamplingParams` type. The ROADMAP says `Router.fs exposes modelToSamplingParams` (SC1).
**Tradeoff:** A `SamplingParams` record in Domain.fs (Core) is a clean typed API but Core has no other HTTP knowledge (and shouldn't — the record would only be consumed by QwenHttpClient). A tuple `float * float * int * float` works but is positionally fragile. An anonymous record in Router.fs is inconsistent with Core patterns. The `modelToTemperature` precedent in Router.fs returns a plain `float` — a record or tuple would be a slightly different style.
**Recommendation:** Use a named tuple type alias or a simple record in Router.fs itself (not in Domain.fs) since it has no Core semantics — it is adapter configuration. However the ROADMAP's SC1 wording "Router.fs exposes modelToSamplingParams" is ambiguous on record vs tuple. This is the planner's decision.

### OD-2: `modelToSamplingParams` — identical values for both models; is exhaustive match still needed?
**What we know:** 0.7/0.8/20/0.0 is correct for both Qwen35B and Qwen122B per Qwen 3.5 model card. The function body could theoretically use a wildcard `| _ ->` to avoid repetition. But CLAUDE.md and existing code explicitly ban `| _ ->` in Router.fs functions to preserve compile-time exhaustiveness. Must use two explicit cases.
**Decision:** Keep two explicit cases — non-negotiable per codebase convention.

### OD-3: Probe script location and commit strategy for 20-03
**What we know:** The probe is a curl command against port 8001. Options: (a) `scripts/probe-system-role.sh` committed to git; (b) inline bash in the 20-03 plan task executed but not committed; (c) documented in 20-03-SUMMARY.md only.
**Consideration:** Committing the probe script provides permanent evidence of the test condition and is runnable by future maintainers. The `scripts/` directory exists (confirmed `scripts/check-no-async.sh` mentioned in CLAUDE.md). A committed script is strongly preferred.

### OD-4: If probe verdict is REJECT (User role stays) — value of remaining 20-03 work
**What we know:** Even if the Role=System probe fails (122B rejects mid-conversation System messages), 20-03 still has value: (a) howto doc sync (stale F# snippets), (b) explicit comment in AgentLoop.fs that User-role is verified for 122B, (c) qwen35-install.md gotcha row update.
**Decision:** 20-03 executes regardless of ACCEPT/REJECT. The scope differs: ACCEPT → code change + doc; REJECT → doc + comment only.

### OD-5: Temperature/sampling per-mode (bench vs REPL) differentiation
**What we know:** Currently `buildRequestBody` uses the same `modelToTemperature model` for all invocations. Bench uses `dotnet run ... --model 122b "prompt"` — same code path. No separate sampling for bench vs REPL modes exists. The SC1 requirement only mentions one set of params (0.7/0.8/20/0.0), consistent for both.
**Decision:** Single `modelToSamplingParams` applies to both bench and REPL. No differentiation needed.

### OD-6: Where to add the `reasoning_content` test — existing file vs new file
**What we know:** Adding to `LlmPipelineTests.fs` (`allTests` testList, which is already in `rootTests`) requires only adding testCase entries — no fsproj or rootTests change. Creating a new `QwenHttpClientTests.fs` requires both fsproj + rootTests registration. The simplest path (fewest risk points) is to add to `LlmPipelineTests.fs` or `ModelsProbeTests.fs`. If `extractContentFromJson` is made public, it could be tested from `ModelsProbeTests.fs` (same module pattern). The planner should decide based on logical grouping.

---

## External References

### Qwen 3.5 Model Card Sampling Parameters
- Qwen 3.5 (non-thinking mode, coding): temperature=0.7, top_p=0.8, top_k=20, presence_penalty=0.0
- Source: Qwen 3.5 official model card (https://huggingface.co/Qwen/Qwen3-235B-A22B — representative; all 3.5 variants share non-thinking coding params). Confirmed in `.planning/phases/17-qwen-3-5-evaluation/17-RESEARCH.md` and CLAUDE.md §Runtime Environment.
- Qwen 2.5 used presence_penalty=1.5 (referenced in QwenHttpClient.fs:46 docblock "Qwen model-card"). The 1.5 value was correct for Qwen 2.5 but is not the Qwen 3.5 recommendation.

### mlx_lm.server `reasoning_content` behavior
- When `--chat-template-args '{"enable_thinking": false}'` is active (confirmed on mlx-lm 0.31.3), the server populates `choices[0].message.content` normally.
- In some configurations (e.g., certain versions or Path B fallback scenarios), the response content appears in `choices[0].message.reasoning_content` with `content` being an empty string `""`. This is documented in `documentation/qwen35-install.md` §5.3 table row (line 521) and Appendix A (line 973).
- The `reasoning_content` fallback is a defensive measure — it handles the case if thinking mode leaks partial output or if a future mlx_lm version changes response shape.

### mlx-lm multi-turn JSON degradation (NOT in scope)
- mlx-lm issue #1011: 4-bit multi-turn degradation at ~5 tool calls. Bench fixtures max 4 steps — not triggered. Confirmed OOS for Phase 20.

---

## Verification Commands

These become `must_haves` in plan task checklists:

```bash
# 20-01: All four sampling fields present
grep -c "top_p\|top_k\|presence_penalty\|temperature" src/BlueCode.Cli/Adapters/QwenHttpClient.fs
# expect: ≥4

# 20-01: Timeout updated
grep "FromSeconds(300" src/BlueCode.Cli/Adapters/QwenHttpClient.fs
# expect: at least one match

# 20-01: No stale 180s references
grep -n "180s\|180\.0" src/BlueCode.Cli/Adapters/QwenHttpClient.fs
# expect: no matches

# 20-01: modelToSamplingParams exists in Router.fs
grep "modelToSamplingParams" src/BlueCode.Core/Router.fs
# expect: match

# 20-01: Temperature value 0.7 in Router.fs (not 0.2 or 0.4)
grep "0\.2\|0\.4" src/BlueCode.Core/Router.fs
# expect: no matches after change

# 20-01: presence_penalty is 0.0 not 1.5
grep "1\.5" src/BlueCode.Cli/Adapters/QwenHttpClient.fs
# expect: no matches after change

# 20-01: Bench gate
bench/run.sh --gate
# expect: exit 0

# 20-02: reasoning_content fallback in QwenHttpClient.fs
grep -c "reasoning_content" src/BlueCode.Cli/Adapters/QwenHttpClient.fs
# expect: ≥1

# 20-02: reasoning_content test in test suite
grep -rc "reasoning_content" tests/BlueCode.Tests/*.fs
# expect: ≥1

# 20-02: qwen35-install.md §5.3 resolved marker
grep "RESOLVED Phase 20" documentation/qwen35-install.md
# expect: ≥2 matches (one per gotcha row)

# 20-02: Test count ≥263
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj -- --summary 2>&1 | grep "Passed:"
# expect: 263 or higher

# 20-02: Bench gate
bench/run.sh --gate
# expect: exit 0

# 20-03: Probe script exists
ls scripts/probe-system-role.sh
# expect: file exists

# 20-03: howto doc updated
grep -n "Role = User\|Role = System" documentation/howto/enforce-llm-tool-terminality-via-post-user-injection.md
# expect: doc now matches current code state

# 20-03: Bench gate
bench/run.sh --gate
# expect: exit 0

# All plans: Core purity check (no Serilog/Spectre in Core)
grep -rn "Serilog\|Spectre\|Argu" src/BlueCode.Core/
# expect: no matches

# All plans: No absolute paths in Core
grep -rn "llm-system" src/
# expect: 0 matches
```

---

## Sources

### Primary (HIGH confidence)
- Direct file read: `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` (all 451 lines) — line numbers verified
- Direct file read: `src/BlueCode.Core/Router.fs` (all 69 lines) — line numbers verified
- Direct file read: `src/BlueCode.Core/Domain.fs` (all 230 lines) — confirmed no SamplingParams type
- Direct file read: `src/BlueCode.Core/AgentLoop.fs` lines 165–310 — confirmed injection site roles
- Direct file read: `documentation/qwen35-install.md` (all 1047 lines) — gotcha rows verified
- Direct file read: `documentation/howto/enforce-llm-tool-terminality-via-post-user-injection.md` (lines 1–212) — stale snippets confirmed
- Direct file read: `bench/run.sh` (all lines) — gate logic and 6-invocation set confirmed
- Direct file read: `bench/baseline.json` — 6 entries (T6/W1/W2/T1/T5/B2 _122b) confirmed
- Direct file read: `tests/BlueCode.Tests/RouterTests.fs` — rootTests list confirmed, no modelToTemperature test
- Live test run: `dotnet run --project tests/BlueCode.Tests/...` → 262 passed, 1 ignored, 0 failed

### Secondary (HIGH confidence)
- `documentation/qwen35-install.md` §10 Phase 17 SWITCH decision — sampling params referenced as "non-thinking coding" benchmark evidence
- CLAUDE.md §Key Seams — model id flow and sampling params tradeoff context
- `.planning/STATE.md` Accumulated Decisions (assumed consistent with file reads above; not re-read in this session)

---

## Metadata

**Confidence breakdown:**
- File locations and line numbers: HIGH — read directly
- Qwen 3.5 sampling parameters (0.7/0.8/20/0.0): HIGH — documented in qwen35-install.md + Phase 17 RESEARCH
- `extractContent` null behavior: HIGH — consistent with .NET JsonElement.GetString() semantics
- reasoning_content fallback behavior: MEDIUM — documented in qwen35-install.md but not empirically reproduced (Path A active, so reasoning_content path not triggered in normal operation)
- 122B Role=System probe verdict: UNKNOWN (by design — probe to be executed in 20-03)
- Test count impact: HIGH — current 262 verified, ≥1 new test = ≥263

**Research date:** 2026-04-27
**Valid until:** Until any of QwenHttpClient.fs, Router.fs, AgentLoop.fs, or qwen35-install.md are changed

---

## RESEARCH COMPLETE

**Phase:** 20 — Qwen 3.5 Protocol Alignment
**Confidence:** HIGH

### Key Findings

1. **Sampling parameters confirmed stale at three fields:** `presence_penalty = 1.5` (line 70), no `top_p`/`top_k` fields (lines 65–71), and `modelToTemperature` returning 0.2/0.4 (Router.fs:65–68). Qwen 3.5 non-thinking coding target: 0.7/0.8/20/0.0.

2. **Timeout hardcoded at 180s in two places:** `c.Timeout <- TimeSpan.FromSeconds(180.0)` (line 34) AND the error message string `"request timed out after 180s"` (line 121). Both must change to 300s.

3. **`extractContent` (line 134–143) returns `Ok ""` on empty content** — no `reasoning_content` fallback. The function is `let private`. To unit-test the fallback, extract a `let extractContentFromJson : string -> string option` pure helper (analogous to `tryParseModelId`/`tryParseMaxModelLen` pattern in same file).

4. **AgentLoop.fs injection sites are lines 249/260/266 (not 259/265 as cited in ROADMAP)** — exact verified line numbers from current file state. Line 172 (PARSE ERROR message) is a distinct site not in 20-03 scope.

5. **262 tests currently passing.** Adding ≥1 test in 20-02 reaches the SC6 target of ≥263. New test file requires dual registration; adding to existing file (e.g., `LlmPipelineTests.fs`) is simpler.

6. **`modelToTemperature` has no unit test** — safe to replace with `modelToSamplingParams` without breaking existing tests. No test covers the old 0.2/0.4 values by name.

### Confidence Assessment

| Area | Level | Reason |
|------|-------|--------|
| File locations & line numbers | HIGH | All read directly |
| Sampling params target values | HIGH | Documented in multiple sources |
| extractContent fallback shape | HIGH | .NET JSON API behavior confirmed |
| AgentLoop injection role | HIGH | grep + read confirmed |
| 20-03 probe outcome | UNKNOWN | Requires live test |
| Test count post-change | HIGH | Arithmetic on verified baseline |

### Open Questions (for planner)

1. SamplingParams: record in Domain.fs vs tuple in Router.fs vs anonymous record
2. Test file: add to `LlmPipelineTests.fs` vs new `QwenHttpClientTests.fs`
3. Probe script: commit to `scripts/` vs inline task documentation
4. If probe verdict = REJECT: comment-only vs no-op for AgentLoop.fs

### Ready for Planning

Research complete. Planner can now create PLAN.md files for 20-01, 20-02, 20-03.
