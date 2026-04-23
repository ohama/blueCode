# Phase 6: Dynamic Bootstrap - Research

**Researched:** 2026-04-23
**Domain:** F# adapter-layer wiring — QwenHttpClient / CompositionRoot seam
**Confidence:** HIGH (all findings from direct source reading; no external lookups needed)

---

## Executive Summary

Phase 6 has two tightly related but distinct changes: (1) removing the hardcoded
`/Users/ohama/llm-system/` path from `Router.modelToName` by fetching the model id
live from `/v1/models`, and (2) making that fetch lazy so `bootstrapAsync` completes
with zero HTTP calls. The key insight is that **both changes converge on the same
probe function** — the existing `getMaxModelLenAsync` hits `/v1/models` and already
reads `data[0]`; REF-01 is just extracting `id` from the same JSON payload.
The planner must treat REF-01 and REF-02 as **a single refactor of the probe
mechanism**, not two independent changes.

The critical design decision is where `modelToName` lives after REF-01. The
right answer is Option B — move the string id entirely to the Cli adapter layer
and replace `Router.modelToName` with a `string` parameter injected directly into
`buildRequestBody`. This is the minimal surgical change: `Router.fs` loses one
function, `buildRequestBody` gains one parameter (`modelId: string`), and
`CompleteAsync` resolves the id from a lazy per-port cache before calling
`buildRequestBody`. No new `AppComponents` fields needed, no Core changes, no
`Map<Model, string>` injected into Core.

The planner must not let the lazy-cache mechanism bleed into Core. The
`Lazy<Task<'T>>` pattern for the per-port cache belongs entirely in
`QwenHttpClient.fs` (Cli adapter layer). The `bootstrapAsync` function becomes
a thin sync wrapper again — identical to `bootstrap` — with no network I/O.

---

## Q1: Where does `Model -> "model-id-string"` live after REF-01?

**Recommendation: Option B — move id resolution entirely into QwenHttpClient.fs.**

### Why not Option A (inject `Map<Model, string>` into Core Router)

`Router.modelToName` is in `BlueCode.Core`. Core cannot do HTTP. Injecting a
`Map<Model, string>` into `Router.fs` at bootstrap time would require:
- Adding a mutable module-level `ref` to Core, or
- Changing `Router.modelToName` signature to `Map<Model, string> -> Model -> string`
  and threading the map through every Core call site (`AgentLoop.fs` → `callLlmWithRetry`
  → eventually `ILlmClient.CompleteAsync`).

Neither is acceptable. Core is pure and stateless. The id string is an adapter concern
(it's the HTTP wire value that vLLM expects).

### Why not Option C (AppComponents.ModelId: Model -> string)

`AppComponents` is wired at bootstrap time. If we set `ModelId` at bootstrap, we need
to resolve model ids eagerly — which defeats REF-02. If we make it lazy, we're
duplicating the lazy-cache logic in `CompositionRoot.fs` instead of keeping it in
the adapter where it belongs.

### Option B: delete `Router.modelToName`, add `modelId: string` parameter to `buildRequestBody`

This is the surgical change:

```fsharp
// QwenHttpClient.fs — BEFORE
let private buildRequestBody (messages: Message list) (model: Model) : string =
    ...
    let req = {| model = modelToName model; ... |}

// QwenHttpClient.fs — AFTER
let private buildRequestBody (messages: Message list) (model: Model) (modelId: string) : string =
    ...
    let req = {| model = modelId; ... |}
```

The caller is `CompleteAsync` (inside `create()`), which already has `model` in scope.
Before calling `buildRequestBody`, `CompleteAsync` calls the per-port lazy probe to
get the `modelId` string. No other code calls `buildRequestBody` (it is `let private`).

The only external call site of `Router.modelToName` is `QwenHttpClient.fs:52`. There
are no other callers. Deleting it from `Router.fs` is a non-breaking change to all
other files.

**`Router.fs` after REF-01:** `modelToName` function is deleted entirely. The
`open BlueCode.Core.Router` in `QwenHttpClient.fs` remains (still uses
`modelToEndpoint`, `endpointToUrl`, `modelToTemperature`).

---

## Q2: Lazy probe mechanism — what and where?

**Recommendation: per-port `Lazy<Task<ModelInfo>>`, captured in `create()` closure.**

### The `ModelInfo` record (in QwenHttpClient.fs)

```fsharp
type ModelInfo =
    { ModelId: string   // data[0].id
      MaxModelLen: int  // data[0].max_model_len, fallback 8192
    }
```

Both `data[0].id` and `data[0].max_model_len` come from the same GET `/v1/models`
response, so one probe per port yields both values. This eliminates the need to make
two probe calls.

### The lazy-async pattern for F#

The standard F# approach is `Lazy<Task<'T>>`. `Lazy<T>` guarantees single-execution
semantics (thread-safe by default via `LazyThreadSafetyMode.ExecutionAndPublication`).

```fsharp
// Per-port lazy probe. Fires on first .Value access; cached thereafter.
let private makeLazyProbe (url: string) : Lazy<Task<ModelInfo>> =
    Lazy<Task<ModelInfo>>(fun () ->
        task {
            // GET url/v1/models, parse id + maxModelLen
            ...
        })
```

`Lazy<Task<T>>` is safe here: the first caller starts the `Task<T>` computation and
caches the `Task` object. Subsequent callers receive the same `Task` and await it;
they do not start a new probe. This handles the "two parallel CompleteAsync calls to
the same port" case correctly — both await the same task.

**Two probe instances, one per port:**

```fsharp
let create () : ILlmClient =
    let probe8000 = makeLazyProbe "http://127.0.0.1:8000"
    let probe8001 = makeLazyProbe "http://127.0.0.1:8001"

    { new ILlmClient with
        member _.CompleteAsync messages model ct =
            task {
                let url = model |> modelToEndpoint |> endpointToUrl
                let probe = if model = Qwen32B then probe8000 else probe8001
                let! info = probe.Value  // fires on first call, cached after
                let body = buildRequestBody messages model info.ModelId
                ...
            }
    }
```

The probe function must map a port base URL to the `/v1/models` endpoint (not
`/v1/chat/completions`). The existing `httpClient` singleton is reused.

### Concurrency note

In v1.0 the REPL is single-turn: one `CompleteAsync` at a time. The `Lazy<Task<T>>`
pattern is still correct for multi-turn and any hypothetical concurrent future use.
No lock required because `Lazy<T>` with default `ExecutionAndPublication` mode
already serializes initialization. Document this fact but it requires no code.

---

## Q3: Probe scope when intent routing is active

**Recommendation: lazy per-port is correct; no eager dual-probe.**

When no `--model` flag is given, `runSession` calls `classifyIntent |> intentToModel`
at turn start to pick a model. The chosen model is fixed for the entire turn. On the
first `CompleteAsync` call, the per-port probe fires for that model's port only.

If 32B is chosen, only probe8000 fires. If 72B is chosen, only probe8001 fires. The
other port is never touched — which is exactly what REF-02 requires.

Multi-turn REPL can switch ports across turns (e.g., turn 1 routes to 32B, turn 2
routes to 72B). Both probes fire lazily as their respective models are first used.
After each probe fires once, it is cached — no repeated probes on subsequent turns.

There is no meaningful case for eager dual-probe in v1.1 scope.

---

## Q4: MaxModelLen per-port vs per-process

**Recommendation: embed MaxModelLen inside the lazy probe result; keep `AppComponents.MaxModelLen`
as the first-probed-wins value from the active model's port.**

`AppComponents.MaxModelLen: int` is used by `Repl.fs` for the 80% context warning.
The warning is per-turn, and each turn uses a single fixed model. So the correct
`MaxModelLen` for any given turn is the one from that turn's model's port.

The minimal v1.1 shape: **`AppComponents.MaxModelLen` stays `int`**, but its value is
set lazily per the first-probed port. The simplest wiring: after `CompleteAsync`
resolves `info.MaxModelLen`, the client should have a way to surface it.

**Concrete approach**: add a `GetMaxModelLen: Model -> Task<int>` member to the
`QwenHttpClient` object (exposed as a method alongside `ILlmClient`). But that leaks
implementation detail. A cleaner alternative: accept a mutable callback or shared
ref.

**Simpler pragmatic choice**: since `Repl.fs` already reads `components.MaxModelLen`
at warning-check time (not at startup), we can make `AppComponents.MaxModelLen`
resolve lazily too, using a `unit -> int` thunk or a `Task<int>` stored in the
record. However, `AppComponents` is a plain record — adding a `Task<int>` or function
field is possible but ugly.

**Recommended minimal approach for v1.1**: Remove `MaxModelLen` from `AppComponents`
entirely and instead store a `GetMaxModelLen: Model -> Task<int>` field. The
`Repl.fs` call site awaits it inside `onStep`. However this is a larger change.

**Actually simplest approach**: Keep `AppComponents.MaxModelLen: int` with default
8192. The lazy probe in `QwenHttpClient` fires on first `CompleteAsync`, but there is
no clean way to update `AppComponents.MaxModelLen` retroactively (it's an immutable
record). So `MaxModelLen` stays at 8192 as the bootstrap default. The real per-port
value is available inside `QwenHttpClient` from the probe but is not surfaced to
`AppComponents`.

**Decision for planner**: The `MaxModelLen` field in `AppComponents` becomes
permanently 8192 (the fallback floor). The 80% warning becomes slightly less accurate
for 72B (128k tokens), but it was already only a heuristic, and the fallback is
conservative (warns earlier, not later). This is acceptable for v1.1 scope. If
per-port accuracy is needed, that's a v1.2 change.

**This means**: `bootstrapAsync` can be eliminated or collapsed into `bootstrap`.
`bootstrapAsync` existed only to probe `MaxModelLen`. If `MaxModelLen` is always
8192 in `AppComponents`, `bootstrapAsync` becomes identical to `bootstrap`. The
planner should collapse them: `Program.fs` calls `bootstrap` (sync, no I/O) and
`bootstrapAsync` is deleted (or kept as a thin alias for test backward-compat).

---

## Q5: Test strategy without live Qwen

**Recommendation: extract a pure `tryParseModelInfo` function alongside the existing `tryParseMaxModelLen`, and test it with inline JSON fixtures. For `CompleteAsync` end-to-end, use the existing `stubLlm` ILlmClient pattern — no HttpMessageHandler injection needed.**

### For REF-01 model id parsing (pure function tests)

Add a pure function `tryParseModelId (json: string) : string option` (or extend
`tryParseMaxModelLen` into a `tryParseModelInfo` returning both fields). This function
is directly testable with inline JSON — same pattern as the existing
`ModelsProbeTests.fs` which already tests `tryParseMaxModelLen` with 7 cases.

The new test module (e.g., `ModelIdTests.fs` or extending `ModelsProbeTests.fs`)
tests that `data[0].id` is extracted correctly.

### For the lazy probe (integration of id into request body)

`buildRequestBody` is `let private`, so it cannot be tested directly. The only test
that verifies the `"model"` field in the POST body would require a mock HTTP server,
which is overkill. The `--trace` success criterion (SC-3) is the acceptance test for
this: run `blueCode --trace --model 32b "hello"` and verify the logged POST body's
`model` field matches the live `/v1/models` response.

For unit coverage of the routing logic: the `RouterTests.fs` success criterion (SC-4)
asks for a test of `modelToName` with "injected id string". After REF-01, `modelToName`
is deleted from Core. The test should instead be a test of `tryParseModelId` (the
pure parser) with an injected id string in the JSON fixture. This satisfies the spirit
of SC-4 without any live HTTP.

### For bootstrapAsync / REF-02 (no HTTP calls at startup)

`CompositionRoot.bootstrap` (sync) already makes no HTTP calls — `CompositionRoot`
tests verify this implicitly. After REF-02, `bootstrapAsync` either disappears or
becomes identical to `bootstrap`. The existing `CompositionRootTests.fs` tests still
pass. SC-5 ("no HTTP calls recorded") is satisfied structurally — the lazy probe
fires on first `CompleteAsync`, not during bootstrap.

### HttpMessageHandler injection: not needed for this phase

The existing `getMaxModelLenAsync` test is a live-port test that passes in CI because
it has the "returns positive int" assertion that works for both 8192 fallback and real
value. We do not need to inject a mock `HttpMessageHandler` into `QwenHttpClient`.

The `httpClient` singleton in `QwenHttpClient.fs` is module-scope private. It cannot
be replaced from tests without refactoring it into a constructor parameter of `create`.
The comment in the code ("Phase 4 CompositionRoot.fs will own this") was never
acted upon — the singleton is still in `QwenHttpClient.fs`. This was noted as a
pitfall in v1.0. For Phase 6, we do NOT need to refactor the `HttpClient` singleton
for test injection — pure function tests cover the parsing logic, and the live
integration is verified by the `--trace` criterion.

---

## Q6: Startup ordering — what happens when neither port is up?

**Recommendation: on lazy probe failure, fall back to a dummy model id and log WARN — same fallback semantics as v1.0's `max_model_len=8192`.**

In v1.0, `getMaxModelLenAsync` failed gracefully with `return fallback` when the port
was down. The same pattern applies to the new combined probe:

```fsharp
let private probeModelInfo (baseUrl: string) (ct: CancellationToken) : Task<ModelInfo> =
    task {
        try
            use! resp = httpClient.GetAsync(baseUrl + "/v1/models", ct)
            if resp.IsSuccessStatusCode then
                let! json = resp.Content.ReadAsStringAsync(ct)
                match tryParseModelInfo json with
                | Some info -> return info
                | None ->
                    Serilog.Log.Warning(...)
                    return { ModelId = ""; MaxModelLen = 8192 }
            else
                Serilog.Log.Warning(...)
                return { ModelId = ""; MaxModelLen = 8192 }
        with ex ->
            Serilog.Log.Warning(ex, "GET /v1/models failed for {BaseUrl}; using fallback", baseUrl)
            return { ModelId = ""; MaxModelLen = 8192 }
    }
```

If `ModelId = ""` is sent in the POST body, vLLM will return HTTP 4xx, which
`postAsync` maps to `Error(LlmUnreachable(...))`. So the error surfaces at the
`CompleteAsync` call, not at startup — which is the correct lazy-error behavior.

The `REF-02` intent is that cold-start with port 8000 down should not produce port
8000 WARN logs. With lazy probe, the probe for 8000 never fires if `--model 72b` is
used. If 8000 is also used and is down, the probe fires on the first 32B call and
logs WARN then — appropriate.

---

## Q7: Pitfalls specific to this phase

### Pitfall 1: `buildRequestBody` is private — only one call site, but easy to miss

`buildRequestBody` at line 43 of `QwenHttpClient.fs` is `let private`. Its only
caller is inside the `create()` factory's `CompleteAsync` implementation at line 303.
Adding `modelId: string` as a third parameter is safe — no external callers. The
change is surgical.

### Pitfall 2: `open BlueCode.Core.Router` stays in `QwenHttpClient.fs`

After removing `modelToName`, the `open BlueCode.Core.Router` import still provides
`modelToEndpoint`, `endpointToUrl`, and `modelToTemperature`. Do not remove the open.

### Pitfall 3: `grep -r "llm-system" src/` must pass, but tests/ is safe

The `llm-system` paths only appear in `Router.fs`. Tests do not reference them (grep
confirms). After deleting `Router.modelToName`, the grep passes. Tests do not need
path-fixture updates.

### Pitfall 4: `RouterTests.fs` tests `Router.allTests` — no `modelToName` test exists

Checking `RouterTests.fs`: there are test lists for `classifyIntent`,
`intentToModel`, `modelToEndpoint`, and `endpointToUrl` — but no existing test for
`modelToName`. Deleting `modelToName` from `Router.fs` does not break any existing
test. The SC-4 requirement for a new test is satisfied by a new `ModelIdTests.fs`
testing `tryParseModelId`.

### Pitfall 5: `bootstrapAsync` in `Program.fs` — if removed, update Program.fs

`Program.fs` line 65 calls `(bootstrapAsync projectRoot opts).GetAwaiter().GetResult()`.
If `bootstrapAsync` is deleted and `bootstrap` returns `AppComponents` synchronously,
change line 65 to `bootstrap projectRoot opts`. Also remove the log line at 66 that
logs `max_model_len` if `MaxModelLen` is now always 8192 (or keep it as a debug note).

### Pitfall 6: `Lazy<Task<T>>` and CancellationToken

The lazy probe receives a `CancellationToken` parameter from `CompleteAsync`. The
`Lazy<Task<T>>` captures a `Task` — the first call's `ct` is baked into the captured
task. Subsequent callers share that task with a potentially different `ct`. For v1.0
single-turn use this is fine (one ct per turn). For v1.1 multi-turn REPL, the probe
fires on the first turn's `ct`; after that the cached result is returned regardless of
subsequent `ct` values. This is acceptable: if the first turn's ct is cancelled, the
probe task is cancelled and the `Lazy` will re-throw on next access (not retry). The
safer approach: pass `CancellationToken.None` to the probe so the probe is not
cancellable but the main `CompleteAsync` call still is. Document this choice.

### Pitfall 7: `MaxModelLen` context warning accuracy

After this phase, `AppComponents.MaxModelLen` is always 8192. For 72B (which has
128k token context), the warning fires at 80% of 8192*4 chars (≈26k chars) instead
of 80% of 128000*4 chars (≈410k chars). The warning fires much earlier than needed
for 72B. This is a **known acceptable regression** for v1.1. Log it in the PR as a
known limitation.

### Pitfall 8: Test registration discipline

New test modules must be added to BOTH:
1. `BlueCode.Tests.fsproj` `<Compile Include=...>` before `RouterTests.fs`
2. `RouterTests.rootTests` list in `RouterTests.fs`

Failure to register in one or both is the most common v1.0 pitfall (noted in
STATE.md: "4 executors hit the rootTests registration pitfall"). Do not use
`[<Tests>]` auto-discovery.

---

## Files to Modify

### Core (BlueCode.Core/)

| File | Change |
|------|--------|
| `Router.fs` | Delete `modelToName` function (lines 53-60). No other changes. |

### Cli (BlueCode.Cli/)

| File | Change |
|------|--------|
| `Adapters/QwenHttpClient.fs` | (1) Add `ModelInfo` record type. (2) Add `tryParseModelId` (or `tryParseModelInfo` combining id + maxModelLen). (3) Add `probeModelInfo` async function. (4) Create two `Lazy<Task<ModelInfo>>` probes in `create()`. (5) Add `modelId: string` parameter to `buildRequestBody`. (6) Update `CompleteAsync` to await probe and pass modelId. (7) Remove `open BlueCode.Core.Router` import of `modelToName` (keep the rest). |
| `CompositionRoot.fs` | Delete `bootstrapAsync` or collapse to alias of `bootstrap`. `bootstrap` needs no changes. `AppComponents.MaxModelLen` stays `int = 8192`. |
| `Program.fs` | Replace `bootstrapAsync` call with `bootstrap` (sync). Remove `GetAwaiter().GetResult()` wrapper. Remove the `Log.Information("Context window resolved...")` line or update to note "8192 (lazy probe per port)". |

### Tests (BlueCode.Tests/)

| File | Change |
|------|--------|
| `ModelsProbeTests.fs` | Add tests for new `tryParseModelId` (or `tryParseModelInfo`). Update existing `getMaxModelLenAsync` test if function is renamed/removed. |
| `RouterTests.fs` | Add new test module to `rootTests` list. No changes to existing test lists (no existing `modelToName` test to remove). |
| `BlueCode.Tests.fsproj` | Add new test file to `<Compile Include>` before `RouterTests.fs`. |
| New: `ModelIdTests.fs` (optional) | Tests for `tryParseModelId` with fixture JSON. Could instead extend `ModelsProbeTests.fs`. |

---

## Recommended Wave Structure

### Wave 1: Core — delete `modelToName`

Single change to `Router.fs`. Compile. Verify `QwenHttpClient.fs` fails to compile
(it references `modelToName`). This proves the dependency is broken and must be fixed
in Wave 2.

**Files:** `Router.fs` only.

### Wave 2: Adapter — implement lazy probe + new `buildRequestBody` signature

The bulk of the work. In `QwenHttpClient.fs`:
- Define `ModelInfo` record
- Add `tryParseModelInfo` (parses both id and maxModelLen from same JSON)
- Add `probeModelInfoAsync` (GET /v1/models for a given base URL, fallback on failure)
- Create two `Lazy<Task<ModelInfo>>` instances in `create()`
- Update `buildRequestBody` to take `modelId: string` instead of calling `modelToName`
- Update `CompleteAsync` to await the per-port probe and pass `modelId`

**Files:** `QwenHttpClient.fs` only.

Verify: `dotnet build` succeeds.

### Wave 3: Wiring — simplify bootstrap

- In `CompositionRoot.fs`: delete `bootstrapAsync` (or keep as sync alias of `bootstrap`)
- In `Program.fs`: replace `bootstrapAsync` call with `bootstrap`

**Files:** `CompositionRoot.fs`, `Program.fs`.

Verify: `dotnet build` succeeds.

### Wave 4: Tests

- Extend (or add) test for `tryParseModelInfo` covering: valid id, missing id, empty
  data array, invalid JSON
- Register new tests in `RouterTests.rootTests` and `BlueCode.Tests.fsproj`
- Run `dotnet test` — all 208 existing tests pass

**Files:** `ModelsProbeTests.fs` (extend) or new `ModelIdTests.fs`, `RouterTests.fs`,
`BlueCode.Tests.fsproj`.

### Wave 5: Verification

Run the success criteria checks:
1. `grep -r "llm-system" src/` — no output
2. `blueCode --model 72b "hello"` with port 8000 down — no 8000 WARN
3. `blueCode --trace --model 32b "hello"` — POST body `"model"` matches live id
4. `dotnet test` — 208+ tests pass
5. `bootstrapAsync` call removed from Program.fs (SC-5 structural proof)

---

## Open Questions

1. **`bootstrapAsync` backward-compat**
   - What we know: only `Program.fs` calls `bootstrapAsync`. No test calls it.
   - What's unclear: should it be kept as a deprecated alias or deleted?
   - Recommendation: delete it. If any test references it, `dotnet build` will catch
     the breakage immediately.

2. **`MaxModelLen` in `AppComponents` — future per-port accuracy**
   - What we know: keeping it at 8192 is a known regression for 72B warning accuracy.
   - What's unclear: whether v1.2 will address this.
   - Recommendation: add a `// TODO v1.2: per-port MaxModelLen from lazy probe` comment
     in `CompositionRoot.fs`. Do not fix in Phase 6.

3. **`probeModelInfoAsync` CancellationToken strategy**
   - What we know: `Lazy<Task<T>>` bakes in the first caller's ct.
   - Recommendation: pass `CancellationToken.None` to the probe so the HTTP GET
     is not cancellable (fine for a one-time startup probe), while the main POST
     in `postAsync` still respects the user's `ct`.

---

## Confidence Breakdown

| Area | Level | Reason |
|------|-------|--------|
| Files to modify | HIGH | Direct grep; only `Router.fs:57-60` has `modelToName`; only `QwenHttpClient.fs:52` calls it |
| Option B design | HIGH | Confirmed by code structure: `buildRequestBody` is private with one call site |
| `Lazy<Task<T>>` pattern | HIGH | Standard .NET pattern; thread-safe by default |
| MaxModelLen strategy (8192 floor) | HIGH | Pragmatic; existing tests use 8192 fixtures; minimal code change |
| Wave ordering | HIGH | Wave 1 causes compile failure that Wave 2 fixes — correct TDD order |
| Test mock strategy | HIGH | Pure function tests sufficient; no HttpMessageHandler injection needed |

**Research date:** 2026-04-23
**Valid until:** Stable (no external dependencies; all findings from codebase analysis)
