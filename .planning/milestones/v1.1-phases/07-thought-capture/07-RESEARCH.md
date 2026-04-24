# Phase 7: Thought Capture - Research

**Researched:** 2026-04-23
**Domain:** F# ports-and-adapters wiring — ILlmClient return-type signature change
**Confidence:** HIGH (all findings from direct source reading; no external lookups needed)

---

## Executive Summary

Phase 7 is a pure plumbing change: the `thought` string is already parsed and available
in `LlmStep.thought` (adapter layer, `LlmWire.fs`) but is dropped by `toLlmOutput` before
it crosses the `ILlmClient` boundary into Core. The only real decision is **which type
carries thought across the boundary**. Every other question — retry semantics, schema
validity, rendering — resolves automatically once that type is chosen.

The recommended choice is **Option C: a new Core record `LlmResponse = { Thought: Thought; Output: LlmOutput }`**.
It is the only option that is: (a) nameable so `callLlmWithRetry` has a clean return type
annotation, (b) extensible without breaking existing pattern matches on `LlmOutput`, and
(c) idiomatic F# (records for multi-field bundles, DUs for sum types). Option B (tuple)
is the close second and is acceptable if the planner prefers fewer new types; Option C is
preferred for readability in `callLlmWithRetry` and `runLoop`.

The change touches exactly **5 files in production** and **3 test files**. No new modules,
no new projects, no new schema validation required. The `llmStepSchema` already enforces
`thought` as a non-empty string (`minLength: 1`), so SC-5 "no schema changes required"
is confirmed correct.

**Primary recommendation:** Introduce `LlmResponse` record in `Domain.fs`, change
`CompleteAsync` to return `Task<Result<LlmResponse, AgentError>>`, update `toLlmOutput`
to return the same, wire thought through `callLlmWithRetry` and both `Step` construction
sites in `runLoop`. Do this as a **single big-bang commit per task** — no transitional API.

---

## Q1: Return Type for `ILlmClient.CompleteAsync`

### The four options examined against the actual call sites

**`callLlmWithRetry` is the primary constraint.** It calls `CompleteAsync` twice and
must pattern-match on `Ok output` to decide whether to retry. Its current signature is:

```fsharp
let private callLlmWithRetry ... : Task<Result<LlmOutput, AgentError>>
```

After the change it must carry `Thought` through both attempts. The caller `runLoop`
then uses `output` (the `LlmOutput` part) to branch on `FinalAnswer` vs `ToolCall`,
and uses `thought` only to populate `Step.Thought`.

**Option A — `Task<Result<LlmStep, AgentError>>`:**
Rejected. `LlmStep` is in `BlueCode.Cli.Adapters.LlmWire`. Moving it to Core would
violate the ports-and-adapters invariant (Core must have zero Cli references). Defining
a duplicate "LlmStepCore" type would be naming confusion. The `input: JsonElement` field
on `LlmStep` would also bring `System.Text.Json` into Core as a domain-level dependency,
which contradicts the v1.0 design principle that Core uses only standard F# types.

**Option B — `Task<Result<Thought * LlmOutput, AgentError>>`:**
Viable. No new type. The `callLlmWithRetry` return type becomes
`Task<Result<Thought * LlmOutput, AgentError>>` and `runLoop` destructures with
`| Ok(thought, output) ->`. Drawback: tuple ordering is implicit (nothing stops
`LlmOutput * Thought` at a future edit site), and annotating `callLlmWithRetry`'s
return type is slightly verbose. Acceptable for a v1.1 codebase of this size.

**Option C — New Core record `LlmResponse = { Thought: Thought; Output: LlmOutput }`:**
Recommended. Named fields eliminate ordering ambiguity. `callLlmWithRetry` returns
`Task<Result<LlmResponse, AgentError>>` — reads like a first-class type. `runLoop`
accesses `response.Thought` and `response.Output` with no destructuring syntax. Adding
`TokensUsed` or `FinishReason` later is a non-breaking record extension. The cost is
one 2-field record in `Domain.fs` — minimal.

**Option D — Add `Thought` into `LlmOutput` DU cases:**
Rejected immediately. `ToolCall(Thought, ToolName, ToolInput)` breaks every existing
pattern match in `AgentLoop.fs`, `Rendering.fs`, `RenderingTests.fs`, `ToLlmOutputTests.fs`,
`AgentLoopTests.fs`. Too invasive for the value delivered.

### Verdict: Use Option C

Add to `Domain.fs` immediately before the `AgentError` type:

```fsharp
/// LLM response: the reasoning text and the structured action. Phase 7 OBS-05.
/// Replaces the bare LlmOutput return from ILlmClient.CompleteAsync so that
/// Step.Thought can be populated with real content instead of the v1 placeholder.
type LlmResponse =
    { Thought: Thought
      Output: LlmOutput }
```

Change `Ports.fs`:

```fsharp
type ILlmClient =
    abstract member CompleteAsync:
        messages: Message list -> model: Model -> ct: CancellationToken -> Task<Result<LlmResponse, AgentError>>
```

---

## Q2: Where Does `toLlmOutput` End Up?

**Current signature:** `toLlmOutput: LlmStep -> Result<LlmOutput, AgentError>`

**New signature:** `toLlmOutput: LlmStep -> Result<LlmResponse, AgentError>`

The function body gains one line — it constructs `{ Thought = Thought step.thought; Output = output }`:

```fsharp
let toLlmOutput (step: LlmStep) : Result<LlmResponse, AgentError> =
    let output =
        match step.action with
        | "final" ->
            match step.input.TryGetProperty("answer") with
            | true, v when v.ValueKind = JsonValueKind.String -> Ok(FinalAnswer(v.GetString()))
            | _ -> Error(SchemaViolation "final action input missing string 'answer' field")
        | toolName ->
            let raw = step.input.GetRawText()
            let ti = ToolInput(Map.ofList [ ("_raw", raw) ])
            Ok(ToolCall(ToolName toolName, ti))
    output |> Result.map (fun o -> { Thought = Thought step.thought; Output = o })
```

The `QwenHttpClient.create()` CompleteAsync pipeline end line changes from:

```fsharp
| Ok step -> return toLlmOutput step
```

No further changes in `QwenHttpClient.fs`.

**`ToLlmOutputTests.fs` impact:** The 5 test cases must update their pattern matches.
Where they currently match `Ok(FinalAnswer s)` or `Ok(ToolCall(...))` they must instead
match `Ok { Output = FinalAnswer s }` or `Ok { Output = ToolCall(...) }`.
The `thought` field is not currently verified in these tests; no new assertions needed
unless the planner wants to add a regression for SC-5.

---

## Q3: Retry Correction Prompt Handling (LOOP-05)

**No structural change required.** The retry semantics remain exactly 2 attempts.

`callLlmWithRetry`'s return type changes from `Task<Result<LlmOutput, AgentError>>` to
`Task<Result<LlmResponse, AgentError>>`. The internal structure is identical — `CompleteAsync`
returns `Result<LlmResponse, AgentError>` on both attempts, so `Ok response` simply passes
through on success.

The correction-prompt path retries only on `InvalidJsonOutput`. When the retry succeeds
it returns a fresh `LlmResponse` with the thought from the second call. This is correct:
the second LLM turn produces its own thought explaining why it's retrying. There is no
"stale thought" issue — the thought from attempt1 is never retained when attempt1 fails.

```fsharp
// After the change — callLlmWithRetry return type:
let private callLlmWithRetry ... : Task<Result<LlmResponse, AgentError>> =
    task {
        let! attempt1 = client.CompleteAsync messages model ct
        match attempt1 with
        | Ok response -> return Ok response          // LlmResponse passes through unchanged
        | Error(InvalidJsonOutput raw) ->
            // ... build correction, retry ...
            let! attempt2 = client.CompleteAsync messages2 model ct
            match attempt2 with
            | Ok response -> return Ok response      // fresh LlmResponse from retry
            | Error(InvalidJsonOutput _) -> return Error(InvalidJsonOutput raw)
            | Error other -> return Error other
        | Error other -> return Error other
    }
```

The pattern match in `runLoop` over `callLlmWithRetry`'s result changes from
`| Ok(FinalAnswer answer) ->` / `| Ok(ToolCall(...)) ->` to:

```fsharp
| Ok { Thought = thought; Output = FinalAnswer answer } -> ...
| Ok { Thought = thought; Output = ToolCall(ToolName actionName, toolInput) } -> ...
```

And both `Step` construction sites replace the `Thought = Thought "[not captured in v1]"`
literal with `Thought = thought`.

---

## Q4: JSON Schema `thought` Field

**Confirmed: no schema changes required. SC-5 is correct.**

From `Json.fs` line 195-209, `llmStepSchema` already has:

```json
"thought": { "type": "string", "minLength": 1 }
```

This means:
- `thought` is in the `"required"` array
- It must be a `string`
- It must have at least 1 character (`minLength: 1`)

The `LlmStep` record (`LlmWire.fs`) maps this to `thought: string`. By the time
`parseLlmResponse` returns `Ok step`, `step.thought` is guaranteed to be a non-empty
string. The only plumbing required is passing `step.thought` through `toLlmOutput` to
`LlmResponse.Thought` — the value is already validated.

**Edge case for empty thought:** The schema prevents it at the adapter boundary. If by
any future path a `Thought ""` arrived at `renderVerbose`, the current render would
show `thought:  ` (two spaces, blank thought). The planner may want to add a defensive
rendering guard: if the unwrapped thought string is empty, render `thought: (none)`.
This is a display polish choice, not a correctness requirement for Phase 7.

---

## Q5: Test Mock Updates

### Files with `ILlmClient` mocks requiring update

**1. `tests/BlueCode.Tests/AgentLoopTests.fs`**

The `mockLlm` helper (lines 14-23) takes `Result<LlmOutput, AgentError> list` and the
queue returns `Task.FromResult(queue.Dequeue())`. After the change it must take
`Result<LlmResponse, AgentError> list`.

All 6 call sites that pass `Ok(...)` values:
- Line 60: `Ok(FinalAnswer "done")`
- Lines 82-84: 5x `Ok(toolCall "read_file" ...)`
- Line 96: 3x `Ok(toolCall "read_file" ...)`
- Line 112: `Ok(FinalAnswer "recovered")`
- Line 143: `Ok(FinalAnswer "done")`

Each `Ok(...)` becomes `Ok { Thought = Thought "test thought"; Output = ... }`.

`Error(InvalidJsonOutput ...)` values are unchanged (error cases don't carry thought).

A `makeMockResponse` helper reduces boilerplate:

```fsharp
let private makeMockResponse (thought: string) (output: LlmOutput) : Result<LlmResponse, AgentError> =
    Ok { Thought = Thought thought; Output = output }
```

This helper is worth adding — 9 mock response constructions in `AgentLoopTests.fs`
alone, plus 7 more in `ReplTests.fs`. Define it in `AgentLoopTests.fs` and duplicate
(or move to a shared helper module if the project grows).

**2. `tests/BlueCode.Tests/ReplTests.fs`**

The `stubLlm` helper (lines 16-25) is structurally identical to `mockLlm` in
AgentLoopTests. Same change: takes `Result<LlmResponse, AgentError> list`.

Call sites:
- Line 55: `Ok(toolCall "list_dir" ...)`, `Ok(FinalAnswer "done")` — 2 responses
- Line 140: `stubLlm []` — no change needed (empty list)
- Line 177: `Ok(FinalAnswer "verbose done")` — 1 response
- Line 237: `Ok(FinalAnswer "compact done")` — 1 response
- Lines 301-305: 3 responses with toolCall + FinalAnswer

Total: 7 Ok-wrapped values need wrapping in `{ Thought = ...; Output = ... }`.

**3. `tests/BlueCode.Tests/ToLlmOutputTests.fs`**

5 test cases, all pattern-matching on `toLlmOutput step`. Change pattern matches:
- `Ok(FinalAnswer s)` → `Ok { Output = FinalAnswer s }`
- `Ok(ToolCall(ToolName name, ToolInput map))` → `Ok { Output = ToolCall(ToolName name, ToolInput map) }`
- `Error(SchemaViolation ...)` cases are unchanged

**4. `tests/BlueCode.Tests/RenderingTests.fs`**

No ILlmClient mock. The `toolStep` and `finalStep` fixtures already use
`Thought = Thought "inspecting config"` and `Thought = Thought "done"` — these are
direct `Step` constructions, not touched by the return type change. **No change needed.**

**5. `tests/BlueCode.Tests/AgentLoopSmokeTests.fs`**

No ILlmClient mock (uses real `bootstrap`). **No change needed.** The smoke test
will automatically get real thought content once the pipeline is wired.

**6. `tests/BlueCode.Tests/CompositionRootTests.fs`**

Uses real `bootstrap` — no mock. **No change needed.**

### Summary table

| File | Change type | Approximate lines changed |
|------|-------------|--------------------------|
| `AgentLoopTests.fs` | Update `mockLlm` signature + 9 Ok values + add helper | ~20 lines |
| `ReplTests.fs` | Update `stubLlm` signature + 7 Ok values | ~15 lines |
| `ToLlmOutputTests.fs` | Update pattern matches in 5 test bodies | ~10 lines |
| `RenderingTests.fs` | None | 0 |
| `AgentLoopSmokeTests.fs` | None | 0 |
| `CompositionRootTests.fs` | None | 0 |

---

## Q6: `Rendering.fs` Verbose Rendering

**No code change required in `Rendering.fs`.**

`renderVerbose` (line 52) already does:

```fsharp
let (Thought t) = step.Thought
...
sprintf "... \n  thought: %s\n ..." t ...
```

Once `Step.Thought` carries a real string (from Phase 7 wiring), `renderVerbose`
renders it unchanged. The `thought:` label already exists. SC-3 is satisfied with
zero changes to `Rendering.fs`.

**Empty thought defensive behavior:** The schema enforces `minLength: 1`, so `t = ""`
should be impossible in production. For belt-and-suspenders the planner may add:

```fsharp
let thoughtText = if t = "" then "(none)" else t
```

This is optional polish, not required for Phase 7 success criteria.

**`ReplTests.fs` Verbose test (line 173):** The test checks that `"thought:"` appears
in verbose output. Since the mock response will now carry a real thought string, this
test continues to pass — it only verifies the label is present, not the content.

---

## Q7: Incremental vs Atomic Signature Change

**Recommendation: big-bang (Option X).**

The interface has exactly two callers that need updating: `callLlmWithRetry` in
`AgentLoop.fs` and `CompleteAsync` in `QwenHttpClient.fs`. There are no other
implementations of `ILlmClient` in production code. The "transitional API" approach
(Option Y — add `CompleteAsyncWithThought` alongside old method) would:
- Create a broken interface state that F# will warn about (both methods exist on the
  object expression in `create()`)
- Require 3 commits (add new, migrate, delete old) for what is semantically one change
- Leave the `[not captured in v1]` literal alive during transition

With Option X the F# compiler enforces completeness: change `ILlmClient`, and every
object expression implementing it (`QwenHttpClient.create()` + all test mocks) produces
a compile error until updated. This is the compiler-as-guide approach standard in F#
ports-and-adapters codebases. Total diff is ~40-50 lines across 5 production files
and 3 test files — well within single-PR scope.

---

## Q8: Scope Pitfalls

| Pitfall | What to watch for | Verification |
|---------|-------------------|--------------|
| Touching Phase 6 seams | Don't edit `probeModelInfoAsync`, `Lazy<Task<ModelInfo>>`, or `bootstrapAsync`. They are not involved. | `grep -n "probeModel\|Lazy<Task\|bootstrapAsync" src/` — count should be unchanged |
| Changing retry semantics | `callLlmWithRetry` must remain 2 attempts. Only the return type changes. | `grep -c "CompleteAsync" src/BlueCode.Core/AgentLoop.fs` must still be 2 |
| Dropping Step timing fields | `StartedAt`, `EndedAt`, `DurationMs` must stay populated exactly as before. Only `Thought = ` assignment changes. | `grep "StartedAt\|EndedAt\|DurationMs" src/BlueCode.Core/AgentLoop.fs` — verify all 3 remain present at both Step construction sites |
| Introducing `async {}` | All new code in Core must use `task {}` only. The change is 2-line `Thought = thought` substitutions — no new CE needed. | `scripts/check-no-async.sh` must pass |
| LlmStep moving to Core | Do NOT move `LlmStep` (with `JsonElement`) to Core. `LlmResponse` is a clean Core type using only `Thought` and `LlmOutput`. | `grep "JsonElement\|LlmWire" src/BlueCode.Core/` must return no results |
| Breaking `buildMessages` | `buildMessages` destructures `step.Thought` to build history. It is not on the hot path of Phase 7 changes but must continue to compile. Verify the `(Thought t) = step.Thought` destructure still works. | Compile only. No semantic change. |

---

## Q9: Plan Decomposition

### Recommended: 2 plans

**Plan 07-01: Core type + adapter wiring (production code only)**

Tasks in order:
1. Add `LlmResponse` record to `Domain.fs` (5 lines)
2. Update `ILlmClient.CompleteAsync` in `Ports.fs` (1 line signature change)
3. Update `callLlmWithRetry` return type + pattern matches in `AgentLoop.fs` (~10 lines)
4. Update both `Step` construction sites in `runLoop` in `AgentLoop.fs` (2 lines each)
5. Update `toLlmOutput` in `QwenHttpClient.fs` (~5 lines)
6. Update `CompleteAsync` object expression in `QwenHttpClient.create()` (1 line — pipeline end)
7. Remove `Known v1 limitation` comment block from `AgentLoop.fs` header

This plan does not compile until tasks 1-6 are all done (type change cascades through
the type checker). Implement all in one commit so CI never sees a broken build.

**Plan 07-02: Test updates + verification**

Tasks in order:
1. Update `mockLlm` in `AgentLoopTests.fs` + all 9 `Ok(...)` call sites + add `makeMockResponse` helper
2. Update `stubLlm` in `ReplTests.fs` + all 7 `Ok(...)` call sites
3. Update pattern matches in `ToLlmOutputTests.fs` (5 test cases)
4. Run full test suite (`dotnet test`) — assert all 216+ tests pass
5. Run `blueCode --verbose "list files"` to verify SC-3 (thought line shows real content)

Plan 07-02 depends on Plan 07-01 (tests won't compile until production types change).

---

## Files to Modify

### Core (`src/BlueCode.Core/`)

| File | Change | Approximate size |
|------|--------|-----------------|
| `Domain.fs` | Add `LlmResponse` record (~5 lines) after `LlmOutput` type | Small |
| `Ports.fs` | Change `CompleteAsync` return type in `ILlmClient` | 1 line |
| `AgentLoop.fs` | Update `callLlmWithRetry` signature + 2 pattern-match arms; update 2 `Step` construction sites; remove placeholder comment | ~15 lines |

### Cli (`src/BlueCode.Cli/`)

| File | Change | Approximate size |
|------|--------|-----------------|
| `Adapters/QwenHttpClient.fs` | Change `toLlmOutput` signature + body (~5 lines); change `CompleteAsync` object expression pipeline end (1 line) | Small |
| `Rendering.fs` | None required. Optional: add `if t = "" then "(none)"` guard | 0 (or 2 lines optional) |

### Tests (`tests/BlueCode.Tests/`)

| File | Change | Approximate size |
|------|--------|-----------------|
| `AgentLoopTests.fs` | Update `mockLlm` helper + 9 `Ok(...)` values + add `makeMockResponse` | ~20 lines |
| `ReplTests.fs` | Update `stubLlm` helper + 7 `Ok(...)` values | ~15 lines |
| `ToLlmOutputTests.fs` | Update 5 pattern matches | ~10 lines |

Total lines changed: ~65-70 across 6 files.

---

## Common Pitfalls

### Pitfall 1: Pattern-match explosion from Option D

**What goes wrong:** Adding `Thought` directly into `LlmOutput` DU cases forces updates
to every `match` on `LlmOutput` throughout `AgentLoop.fs`, `Rendering.fs`, all test
fixtures. The compiler will force every match arm to be updated — this is not subtle
but it is a large, error-prone diff.

**Why it happens:** `LlmOutput` is matched in 8+ places. Adding a field to a DU case
changes every pattern that destructures that case.

**How to avoid:** Use Option C (`LlmResponse` record wrapper). `LlmOutput` patterns
are completely unchanged. Only the outer `Ok response ->` arm in `callLlmWithRetry`
and `runLoop` changes.

### Pitfall 2: Forgetting `callLlmWithRetry` return type annotation

**What goes wrong:** The private helper `callLlmWithRetry` has an explicit return type
annotation on line 141: `: Task<Result<LlmOutput, AgentError>>`. If the body is changed
(returning `LlmResponse`) but the annotation is not, the compiler emits a type mismatch
pointing to the annotation, not the changed body — confusing error location.

**How to avoid:** Change the annotation first (`LlmOutput` → `LlmResponse`) then change
the body. The annotation change is the correct trigger for the cascade.

### Pitfall 3: `toLlmOutput` return type in docblock vs code

**What goes wrong:** `QwenHttpClient.fs` has a detailed docblock for `toLlmOutput`
mentioning `Result<LlmOutput, AgentError>` in the comment. If only the type signature
is changed and the docblock is not updated, future readers are confused.

**How to avoid:** Update both the signature and the comment that describes the return
type. Search: `grep -n "Result<LlmOutput" src/BlueCode.Cli/Adapters/QwenHttpClient.fs`.

### Pitfall 4: `ToLlmOutputTests.fs` partial pattern match

**What goes wrong:** The test `"tool action -> ToolCall with _raw passthrough"` matches
`Ok(ToolCall(ToolName name, ToolInput map))`. After the change this must be
`Ok { Output = ToolCall(ToolName name, ToolInput map) }`. If the `Ok(...)` wrapper
is not changed, the F# compiler will produce a type error that may look confusing
(type was `LlmResponse`, expected `LlmOutput`).

**How to avoid:** Use the compiler error trail — it will point to every pattern site
that needs updating.

### Pitfall 5: Mock thought value for test assertions

**What goes wrong:** If test thought values are set to `""` (empty string), they will
fail the schema `minLength: 1` constraint in production — but in unit tests the mock
bypasses schema validation entirely. Using `""` in mocks means the test thought
diverges from production invariants.

**How to avoid:** Use a non-empty placeholder like `"test thought"` or `"thinking..."` in
all `makeMockResponse` calls. This keeps mocks consistent with the production schema guarantee.

---

## Code Examples

### LlmResponse in Domain.fs (after `LlmOutput`)

```fsharp
// ── LLM response bundle (Phase 7 OBS-05) ─────────────────────────────────────

/// Bundles LLM reasoning text with the structured action. Replaces bare LlmOutput
/// as the success type of ILlmClient.CompleteAsync so Step.Thought receives real
/// content instead of the v1 placeholder.
type LlmResponse =
    { Thought: Thought
      Output: LlmOutput }
```

### ILlmClient in Ports.fs

```fsharp
type ILlmClient =
    abstract member CompleteAsync:
        messages: Message list -> model: Model -> ct: CancellationToken -> Task<Result<LlmResponse, AgentError>>
```

### callLlmWithRetry signature in AgentLoop.fs

```fsharp
let private callLlmWithRetry
    (client: ILlmClient)
    (messages: Message list)
    (model: Model)
    (ct: CancellationToken)
    : Task<Result<LlmResponse, AgentError>> =
```

### runLoop pattern match in AgentLoop.fs (FinalAnswer branch)

```fsharp
| Ok { Thought = thought; Output = FinalAnswer answer } ->
    let endedAt = DateTimeOffset.UtcNow
    let durationMs = int64 (endedAt - startedAt).TotalMilliseconds
    let finalStep =
        { StepNumber = loopN + 1
          Thought = thought                // was: Thought "[not captured in v1]"
          Action = FinalAnswer answer
          ...
        }
```

### runLoop pattern match in AgentLoop.fs (ToolCall branch)

```fsharp
| Ok { Thought = thought; Output = ToolCall(ToolName actionName, toolInput) } ->
    ...
    let step =
        { StepNumber = loopN + 1
          Thought = thought               // was: Thought "[not captured in v1]"
          Action = ToolCall(ToolName actionName, toolInput)
          ...
        }
```

### toLlmOutput in QwenHttpClient.fs

```fsharp
let toLlmOutput (step: LlmStep) : Result<LlmResponse, AgentError> =
    let outputResult =
        match step.action with
        | "final" ->
            match step.input.TryGetProperty("answer") with
            | true, v when v.ValueKind = JsonValueKind.String -> Ok(FinalAnswer(v.GetString()))
            | _ -> Error(SchemaViolation "final action input missing string 'answer' field")
        | toolName ->
            let raw = step.input.GetRawText()
            let ti = ToolInput(Map.ofList [ ("_raw", raw) ])
            Ok(ToolCall(ToolName toolName, ti))
    outputResult |> Result.map (fun output -> { Thought = Thought step.thought; Output = output })
```

### makeMockResponse helper in AgentLoopTests.fs

```fsharp
let private makeMockResponse (thought: string) (output: LlmOutput) : Result<LlmResponse, AgentError> =
    Ok { Thought = Thought thought; Output = output }
```

Usage:

```fsharp
// Before:
let llm = mockLlm [ Ok(FinalAnswer "done") ]

// After:
let llm = mockLlm [ makeMockResponse "thinking" (FinalAnswer "done") ]
```

---

## Verification Grep Commands

```bash
# Confirm placeholder is gone from production:
grep -rn "not captured in v1" src/
# Expected: zero results

# Confirm ILlmClient signature uses LlmResponse:
grep -n "CompleteAsync" src/BlueCode.Core/Ports.fs
# Expected: one line containing "LlmResponse"

# Confirm no LlmWire/JsonElement leaked into Core:
grep -rn "LlmWire\|JsonElement\|LlmStep" src/BlueCode.Core/
# Expected: zero results

# Confirm retry count unchanged (still 2 CompleteAsync calls):
grep -c "CompleteAsync" src/BlueCode.Core/AgentLoop.fs
# Expected: 2

# Confirm both Step construction sites use real thought:
grep -n "Thought = " src/BlueCode.Core/AgentLoop.fs
# Expected: 2 lines, neither containing the string "[not captured"

# Confirm toLlmOutput returns LlmResponse:
grep -n "toLlmOutput\|LlmResponse" src/BlueCode.Cli/Adapters/QwenHttpClient.fs
# Expected: toLlmOutput signature and pipeline end both reference LlmResponse

# Confirm async {} is still banned (check no new violations):
bash scripts/check-no-async.sh
# Expected: exit 0
```

---

## Open Questions

1. **Thought content for retry second attempt**
   - What we know: On retry (LOOP-05), the second `CompleteAsync` call returns a fresh
     `LlmResponse` with the second attempt's thought. This is kept (the retry thought
     is "I need to format as JSON").
   - What's unclear: Should the retry thought be appended to / replaced by the original
     thought for history reconstruction in `buildMessages`? Currently history is built
     from `Step.Thought` after the step completes — the retry thought reflects the
     corrected attempt.
   - Recommendation: Keep second-attempt thought as-is. It is semantically accurate
     (it is the thought that produced the valid output). No change to retry logic needed.

2. **Empty thought defensive render**
   - What we know: `llmStepSchema` enforces `minLength: 1`, making empty thought
     unreachable in production.
   - What's unclear: Whether the planner wants a `"(none)"` fallback in `renderVerbose`
     as belt-and-suspenders.
   - Recommendation: Omit for Phase 7. Add in a future polish phase if needed.

---

## Sources

All findings are from direct source reading of the blueCode codebase. No external
documentation or web search was required — this is an internal wiring change with
no new libraries.

### Files read

- `src/BlueCode.Core/Domain.fs` — type definitions, `LlmOutput`, `Thought`, `Step`
- `src/BlueCode.Core/Ports.fs` — `ILlmClient.CompleteAsync` current signature
- `src/BlueCode.Core/AgentLoop.fs` — `callLlmWithRetry`, `runLoop`, Step construction
- `src/BlueCode.Cli/Adapters/LlmWire.fs` — `LlmStep` record with `thought: string`
- `src/BlueCode.Cli/Adapters/Json.fs` — `llmStepSchema` with `thought` constraints
- `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` — `toLlmOutput`, `CompleteAsync` pipeline
- `src/BlueCode.Cli/Rendering.fs` — `renderVerbose` thought rendering
- `src/BlueCode.Cli/Repl.fs` — `onStep` callback, no ILlmClient reference
- `tests/BlueCode.Tests/AgentLoopTests.fs` — `mockLlm` + 6 test cases
- `tests/BlueCode.Tests/ReplTests.fs` — `stubLlm` + 5 test cases
- `tests/BlueCode.Tests/ToLlmOutputTests.fs` — 5 `toLlmOutput` test cases
- `tests/BlueCode.Tests/RenderingTests.fs` — Step fixtures, no mock impact
- `tests/BlueCode.Tests/AgentLoopSmokeTests.fs` — no mock impact
- `tests/BlueCode.Tests/RouterTests.fs` — `rootTests` registration list

---

## Metadata

**Confidence breakdown:**
- Return type choice (Option C): HIGH — examined all call sites directly
- `callLlmWithRetry` retry semantics: HIGH — full source read
- Schema `thought` field: HIGH — read `llmStepSchema` directly
- Test mock enumeration: HIGH — counted all `ILlmClient` mock sites
- `Rendering.fs` no-change: HIGH — read `renderVerbose` directly

**Research date:** 2026-04-23
**Valid until:** Stable (internal codebase change; no external library versions involved)
