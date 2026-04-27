# Phase 19: Qwen 2.5 Retirement + 122B Single-Model Default — Research

**Researched:** 2026-04-27
**Domain:** F# Argu CLI refactor, Domain DU extension, bash harness rewrite, operational disk + launchd retirement
**Confidence:** HIGH (all findings from direct codebase inspection; no external research needed)

---

## Summary

Phase 19 is a cleanup and consolidation phase following the DROP-35B verdict in Phase 18. The work
splits cleanly into two plans: a user-gated physical retirement (19-01: delete Qwen 2.5 model files
and launchd plists), and an autonomous code/bench/docs alignment (19-02: F# cleanup, bench rewrite,
docs update). All source of truth comes from direct file reads; there are no ambiguous external APIs
or library versions to resolve.

The core codebase change in 19-02 is a **rename** of the Domain DU and a **retirement guard** in the
CLI adapter: `Qwen32B`/`Qwen72B` in `Domain.fs` become `Qwen122B` (single case) with optional
`Qwen35B` for `--with-35b` mode; `parseForcedModel` in `CompositionRoot.fs` grows a `PathRetired`
branch; `Router.fs` collapses from 5 functions to 2; `bench/run.sh` is rewritten to be 122B-only;
`bench/baseline.json` is halved. The test count stays at 254/1/0 after updating test assertions to
the new DU shape.

**Primary recommendation:** Execute 19-01 (user checkpoint) and 19-02 (autonomous) in strict
sequence. 19-02 must not start until 19-01 confirms the retirement is complete, because 19-02's
test suite and bench gate must run against the post-retirement filesystem state.

---

## Files to Read/Touch

### 19-01: Retire Qwen 2.5 + Disk Reclamation

| File/Path | What Happens | Notes |
|-----------|-------------|-------|
| `~/Library/LaunchAgents/com.ohama.qwen32b.plist` | unload + rm | Confirmed exists |
| `~/Library/LaunchAgents/com.ohama.qwen72b.plist` | unload + rm | Confirmed exists |
| `~/llm-system/models/qwen32b/` | rm -rf (~17 GB) | Confirmed exists |
| `~/llm-system/models/qwen72b/` | rm -rf (~38 GB) | Confirmed exists |
| `~/llm-system/models/qwen72b.3bit/` | rm -rf (~30 GB) optional | Confirmed exists; old experiment variant |
| `~/llm-system/services/logs/32b.{log,err}` | rm (optional, post-unload) | No longer written after unload |
| `~/llm-system/services/logs/72b.{log,err}` | rm (optional, post-unload) | Same |
| `.planning/phases/19-qwen25-retirement/19-01-RETIREMENT.md` | CREATE | ≥ 40 lines; inventory + disk reclaim metrics |

**Disk reclamation:** qwen32b=17 GB + qwen72b=38 GB + qwen72b.3bit=30 GB = **85 GB** if all three
deleted. ROADMAP says ≥50 GB threshold. SC1 says `~/llm-system/models/` contains ONLY `qwen35b/` and
`qwen122b/` — so `qwen72b.3bit/` must also be removed (currently present; would violate SC1).

**Pre-condition checks for 19-01:**
- `launchctl list | grep ohama` → should show only `com.ohama.qwen122b` (35B already unloaded since Phase 18)
- `df -h ~/` before → baseline
- `curl -fsS http://127.0.0.1:8001/v1/models` → confirm 122B still alive after retirements

### 19-02: Code + Bench + Docs Alignment

| File/Path | What Changes | Scope |
|-----------|-------------|-------|
| `src/BlueCode.Core/Domain.fs` | `Model` DU: rename/collapse `Qwen32B`/`Qwen72B`; add `Qwen35B` (optional), `Qwen122B` | Core DU |
| `src/BlueCode.Core/Router.fs` | Collapse `intentToModel`, `modelToEndpoint`, `modelToTemperature` to single-model default; flag-gate 35B | Core |
| `src/BlueCode.Cli/CliArgs.fs` | Remove `--model 32b/72b` handling; add `--model 122b`; add `--with-35b` | CLI |
| `src/BlueCode.Cli/CompositionRoot.fs` | `parseForcedModel`: retire 32b/72b (exit 2 with message); add 35b guard (check `--with-35b` flag); add 122b; `CliOptions`: add `WithDual35b: bool` | CLI |
| `src/BlueCode.Cli/Program.fs` | Parse `--with-35b`; thread into `CliOptions`; mutually-exclusive with `--resume` (no, these can coexist); pass to bootstrap | CLI |
| `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` | `tryParseModelId`: add `PathRetired` check for `qwen32b`/`qwen72b` paths; update `probe8000`/`probe8001` logic for flag-gated dual mode; update spinner label | CLI adapter |
| `bench/run.sh` | Rewrite in-place: drop `32b`/`72b` loops; all invocations use `--model 122b`; absorb 122b-only logic; update mode help text; recompute gate logic for 7 `_122b` keys | Bench |
| `bench/baseline.json` | Halve: drop `T6_35b`, `W1_35b`, `W2_35b`, `T1_35b`, `B2_35b`; rename W1/W2 entries to `_122b`; keep `T6_122b`, `T5_122b`, `B2_122b`; add `T1_122b`, `W1_122b`, `W2_122b` | Bench |
| `scripts/bench-122b-only.sh` | DELETE after absorption | Bench |
| `CLAUDE.md` | `## Runtime Environment`: reflect 122B-only default; add dual-mode reactivation procedure; remove Qwen 2.5 references | Docs |
| `documentation/qwen35-install.md` | Reframe 35B as standby/rollback (status badge update in intro paragraph) | Docs |
| `documentation/single-model-eval.md` | Add §7 cross-reference to Phase 19 as execution of deferred follow-ups | Docs |
| `documentation/bench.md` | Update "Hang Contingency" section (references 32B); update sample gate output; update mode flags table | Docs |
| `tests/BlueCode.Tests/RouterTests.fs` | Update `intentToModel` tests, `modelToEndpoint` tests, `modelToTemperature` tests for new DU shape | Tests |
| `tests/BlueCode.Tests/CliArgsTests.fs` | Update `parseForcedModel` tests: remove 32b/72b cases, add 122b/35b cases, add retirement error test | Tests |
| `tests/BlueCode.Tests/RenderingTests.fs` | Update `ModelUsed = Qwen32B` → `Qwen122B` in step fixtures (3 occurrences) | Tests |
| `tests/BlueCode.Tests/ReplTests.fs` | Update `ForcedModel = Some Qwen32B` → `Some Qwen122B` (line 334) | Tests |
| `tests/BlueCode.Tests/SessionStoreTests.fs` | Update `ModelUsed = Qwen32B` → `Qwen122B` (line 27) | Tests |
| `tests/BlueCode.Tests/JsonlSinkTests.fs` | Update `ModelUsed = Qwen32B` → `Qwen122B` (line 28) | Tests |
| `tests/BlueCode.Tests/SmokeTests.fs` | Update `Qwen32B` arg to `Qwen122B` in `CompleteAsync` call (line 35) | Tests |
| `tests/BlueCode.Tests/ModelsProbeTests.fs` | No DU change needed; test values are strings like `"qwen32b"` — keep as JSON string fixtures for historical probe test coverage | Tests (no DU touch) |
| `tests/BlueCode.Tests/AgentLoopTests.fs` | Check `testConfig` for any model references → none found in first 100 lines; exhaustive pattern match will catch any missed cases | Tests |

---

## API / Code Shapes

### Domain.fs — `Model` DU Change

**Current (src/BlueCode.Core/Domain.fs line 17-19):**
```fsharp
type Model =
    | Qwen32B
    | Qwen72B
```

**Phase 19 target:**
```fsharp
type Model =
    | Qwen122B   // canonical single-model default
    | Qwen35B    // opt-in via --with-35b flag only
```

This is a compile-cascade change: every exhaustive match on `Model` must be updated.
Key match sites: `Router.intentToModel`, `Router.modelToEndpoint`, `Router.modelToTemperature`,
`QwenHttpClient.create()` (probe selection), `QwenHttpClient.CompleteAsync` (spinner label).

**Compile cascade map:**
1. `Domain.fs` — DU rename (one commit)
2. `Router.fs` — intentToModel, modelToEndpoint, modelToTemperature all update
3. `QwenHttpClient.fs` — `if model = Qwen32B then probe8000 else probe8001` and spinner label
4. Tests — all `Qwen32B`/`Qwen72B` references (7 test files, ~12 occurrences)

### Router.fs — Collapse to Single-Model Default

**Current (Router.fs):** `intentToModel` routes Debug/Design/Analysis → Qwen72B, Implementation/General → Qwen32B.

**Phase 19 target:** Single-model default — ALL intents route to Qwen122B. With `--with-35b` flag and
35B service loaded, high-complexity intents could route to Qwen35B. But ROADMAP §Phase 19 decision C
says: flag-gating is in effect. The planner must decide:

**Open Design Question (for planner):** Does `intentToModel` stay as-is but return `Qwen122B` for
everything when `--with-35b` is absent, or does it take the `withDual35b: bool` flag as a parameter?

Two options:
- **Option A (Simpler):** `Router.fs` stays pure Core (no flag). `CompositionRoot.bootstrap` sets
  `AgentConfig.ForcedModel = Some Qwen122B` when `--with-35b` is absent, overriding routing.
  All intents hit 122B. When `--with-35b` present, `ForcedModel = None` and existing router logic
  routes by intent (Debug/Design/Analysis → 35B, rest → 122B). Router shape: rename DU cases only.
- **Option B (Explicit):** `intentToModel` takes `withDual: bool` parameter. When `false`, always
  returns `Qwen122B`. When `true`, routes Debug/Design/Analysis → Qwen35B, rest → Qwen122B.
  Router.fs then has a function with IO-like parameter (not pure by functional DU standard, but still
  no side effects). This is a mild Core purity concern since `withDual` is a config value.

**Recommendation (HIGH confidence):** Option A. Keep Router.fs pure — just rename DU cases. Force
`Qwen122B` via `ForcedModel = Some Qwen122B` in `CompositionRoot.bootstrap` when `--with-35b` absent.
`ForcedModel = None` when `--with-35b` present (dual mode uses intent routing). This way Core stays
untouched by CLI flag logic, matching the ports-and-adapters invariant.

### CliArgs.fs — New/Removed Arguments

**Current (CliArgs.fs line 14-31):**
```fsharp
type CliArgs =
    | [<MainCommand; Last>] Prompt of prompt: string list
    | Verbose
    | Trace
    | [<AltCommandLine("-m")>] Model of model: string
    | Resume of id: string
    | [<AltCommandLine("--new-session")>] NewSession
```

**Phase 19 target:**
```fsharp
type CliArgs =
    | [<MainCommand; Last>] Prompt of prompt: string list
    | Verbose
    | Trace
    | [<AltCommandLine("-m")>] Model of model: string  // now accepts: "122b" (default), "35b" (--with-35b required)
    | Resume of id: string
    | [<AltCommandLine("--new-session")>] NewSession
    | [<AltCommandLine("--with-35b")>] WithDual        // opt-in BoolFlag-style; presence = true
```

Note: Argu does not have a BoolFlag DU case — flags are presence-based (a `NoCommandLine` member is
needed for true BoolFlag). The simpler approach is a plain flag case `| WithDual` (no parameter),
similar to `| Verbose` and `| Trace`. Presence = dual mode enabled.

The `-m` alias for `--model` stays. The Usage string for `Model` updates.

### CompositionRoot.fs — `parseForcedModel` Retirement Guard

**Current (CompositionRoot.fs line 43-47):**
```fsharp
let parseForcedModel (s: string option) : BlueCode.Core.Domain.Model option =
    match s with
    | None -> None
    | Some "32b" -> Some BlueCode.Core.Domain.Qwen32B
    | Some "72b" -> Some BlueCode.Core.Domain.Qwen72B
    | Some other -> failwithf "Unknown model: %s (valid values: 32b, 72b)" other
```

**Phase 19 target — retirement guard:**
```fsharp
let parseForcedModel (s: string option) (withDual: bool) : BlueCode.Core.Domain.Model option =
    match s with
    | None -> Some BlueCode.Core.Domain.Qwen122B   // default to 122B when no --model flag
    | Some "122b" -> Some BlueCode.Core.Domain.Qwen122B
    | Some "35b" when withDual ->
        Some BlueCode.Core.Domain.Qwen35B
    | Some "35b" ->
        failwithf "Model 35b requires --with-35b flag. Start with: launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen35b.plist"
    | Some "32b" ->
        failwithf "Model 32b retired in Phase 19. Use --model 122b (or no flag for default). See CLAUDE.md."
    | Some "72b" ->
        failwithf "Model 72b retired in Phase 19. Use --model 122b (or no flag for default). See CLAUDE.md."
    | Some other -> failwithf "Unknown model: %s (valid values: 122b; 35b requires --with-35b)" other
```

**NOTE:** `parseForcedModel` currently returns `Model option` (None = no override, use routing).
With single-model default, `None` input should return `Some Qwen122B` so routing always resolves.
Alternatively, keep `None` return and handle default in `AgentConfig.ForcedModel = Some Qwen122B`
in `bootstrap`. The planner should decide which is cleaner — see Open Decision below.

### QwenHttpClient.fs — `tryParseModelId` PathRetired + Probe Logic

**Current:** `tryParseModelId` returns `string option` — no error type, just `None` on parse failure.

**Phase 19 ROADMAP SC4:** "`tryParseModelId` rejects `qwen32b`/`qwen72b` paths as `PathRetired` error variant."

Two implementation paths:
- **Path 1 (Minimal):** Add a passive log warning when a retired path is detected, but keep return
  type as `string option`. The retirement is enforced at the CLI alias layer (exit 2 in parseForcedModel),
  not at the probe layer. `tryParseModelId` continues to be a pure parser.
- **Path 2 (Active):** Change `tryParseModelId` signature to `Result<string option, AgentError>` where
  `AgentError.PathRetired of string` is a new variant. This surfaces as `LlmUnreachable` or a new error
  case in the agent loop.

**Recommendation (MEDIUM confidence):** Path 1 is simpler and keeps `tryParseModelId` pure/testable.
The retirement message already fires at `parseForcedModel` before any probe happens. ROADMAP SC4 says
"rejects... as PathRetired error variant" which suggests Path 2, but the practical protection is at
the CLI alias layer. The planner should confirm whether SC4 literally requires the error variant or
just a clear rejection signal.

**Probe logic change (QwenHttpClient.fs line 382-392):**

Current:
```fsharp
let probe8000: Lazy<Task<ModelInfo>> = ...
let probe8001: Lazy<Task<ModelInfo>> = ...
// ...
let probe = if model = Qwen32B then probe8000 else probe8001
```

Phase 19 (single-model default, probe8000 never allocated unless --with-35b):
```fsharp
let probe8001: Lazy<Task<ModelInfo>> = ...
// probe8000 only if WithDual35b=true; inject via closure capture from create() parameter
let probe = match model with
            | Qwen122B -> probe8001
            | Qwen35B -> probe8000  // only reachable when --with-35b
```

**Spinner label update (line 405-406):**

Current:
```fsharp
let modelLabel =
    match model with
    | Qwen32B -> "32B"
    | Qwen72B -> "72B"
```

Phase 19:
```fsharp
let modelLabel =
    match model with
    | Qwen122B -> "122B"
    | Qwen35B -> "35B"
```

### AgentError DU — PathRetired Variant

**Current (Domain.fs line 139-151):** `AgentError` has 11 cases; no `PathRetired`.

**Phase 19 addition (if Path 2 above is chosen):**
```fsharp
type AgentError =
    | ...existing cases...
    | PathRetired of modelPath: string  // qwen32b or qwen72b path detected post-retirement
```

**Where it lives:** `Domain.fs` in the `// Error domain` section, after `UserCancelled`.

**Rendering:** `Rendering.fs` likely has a match on `AgentError` for display — check for exhaustive
match. Grep confirmed no `Rendering.fs` mentions of individual AgentError cases in current searches,
but the exhaustive match compiler check will flag any missed case.

### bench/run.sh — In-Place Rewrite

**Current structure (bench/run.sh):**
- `regression()`: `for model in 32b 72b` → 14 invocations
- `canary()`: hardcoded `32b`/`72b` 4 invocations
- `b2_mode()`: `for model in 32b 72b` → 2 invocations
- `gate()`: hardcoded `32b`/`72b` 8 invocations
- `phase_variance()`: `for model in 32b 72b` → 12 invocations
- `phase_diagnose()`, `phase_write()`: same dual loops

**Phase 19 target (absorbed from `scripts/bench-122b-only.sh`):**
- All `run()` calls use `--model 122b` exclusively
- `gate()`: 7 invocations (T6_122b, W1_122b, W2_122b, T1_122b, T5_122b, B2_122b + one spare)
  Wait — current gate has 8 invocations using labels `T6_32b T6_72b W1_32b W2_32b T1_32b T5_72b B2_32b B2_72b`.
  Single-model gate shrinks: `T6_122b W1_122b W2_122b T1_122b T5_122b B2_122b` = 6 entries.
  ROADMAP §SC2: "bench/run.sh --gate exits 0". Gate function needs label set updated.
- Pre-condition check from `bench-122b-only.sh` (curl port check) can be absorbed into gate()

**Key: bench/run.sh gate() change:**
- `labels` variable changes from `"T6_32b T6_72b W1_32b W2_32b T1_32b T5_72b B2_32b B2_72b"` →
  `"T6_122b W1_122b W2_122b T1_122b T5_122b B2_122b"`
- `local total=8` → `local total=6`
- Gate `GATE PASS (6/6)` format
- Hardcoded `run "gate_T6_32b" "32b"` etc. → `run "gate_T6_122b" "122b"`
- W1/W2 run as `--model 122b` (currently gate runs W1/W2 with `--model 32b`)

### bench/baseline.json — Halve + Recompute

**Current 8 entries:**
- `T6_35b`, `T6_122b`, `W1_35b`, `W2_35b`, `T1_35b`, `T5_122b`, `B2_35b`, `B2_122b`

**Phase 19 target (ROADMAP SC2: "only _122b keys"):**
- DROP: `T6_35b`, `W1_35b`, `W2_35b`, `T1_35b`, `B2_35b` (5 entries removed)
- KEEP: `T6_122b`, `T5_122b`, `B2_122b`
- ADD: `W1_122b`, `W2_122b`, `T1_122b` (need actual step counts from Phase 18 bench data)

**Phase 18 bench data available (from `documentation/single-model-eval.md` §18-02 table):**
- `T1_122b`: step_count=1, elapsed=4s (consistent with baseline_max=3 from T1_35b)
- `T6_122b`: step_count=4, step_count_max=5 (already in baseline)
- `W1_122b` (122B): step_count=3, elapsed=8s (from §per-test comparison table W1 35b→122b)
- `W2_122b` (122B): step_count=3, elapsed=9s (from §per-test comparison table W2 35b→122b)
- `B2_122b`: step_count=2, step_count_max=3, pass=true (already in baseline)
- `T5_122b`: step_count=3, step_count_max=4 (already in baseline)

So **the Phase 18 data is sufficient to write new baseline entries for W1_122b and W2_122b**
without a fresh bench run. `T1_122b` can be inferred from `T1_35b` (step_count=1, typical).

**Can baselines be reused from Phase 18?** YES — Phase 18 bench-122b-only.sh ran all the
needed tests. `18-02-BENCH-RESULTS.md` contains the step counts. No re-bench required to
populate baseline.json. However, `--gate` must still be run post-code-change to verify the
new binary passes (SC6 requires 254/1/0 tests + gate 0 exit).

---

## Risks and Pitfalls

### Pitfall 1: Test Compilation Cascade from Domain DU Rename

**What goes wrong:** Renaming `Qwen32B`/`Qwen72B` to `Qwen35B`/`Qwen122B` in `Domain.fs` causes
a compile error cascade across ALL files that pattern-match on `Model`. Any missed site is a compiler
error (good — caught at compile time, not runtime).

**Affected files and line counts (from grep results):**

| File | References | Action |
|------|-----------|--------|
| `src/BlueCode.Core/Router.fs` | Lines 40,42,48,49,62,63 (6 occurrences) | Update all 6 |
| `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` | Lines 392,405,406 (3 occurrences) | Update all 3 |
| `src/BlueCode.Cli/CompositionRoot.fs` | Lines 45,46 (2 occurrences) | Update both |
| `tests/BlueCode.Tests/RouterTests.fs` | Lines 49,52,55,58,61,69,72 (7 occurrences) | Update all 7 |
| `tests/BlueCode.Tests/RenderingTests.fs` | Lines 14,25,66 (3 occurrences) | Update all 3 |
| `tests/BlueCode.Tests/ReplTests.fs` | Line 334 (1 occurrence) | Update |
| `tests/BlueCode.Tests/SessionStoreTests.fs` | Line 27 (1 occurrence) | Update |
| `tests/BlueCode.Tests/JsonlSinkTests.fs` | Line 28 (1 occurrence) | Update |
| `tests/BlueCode.Tests/SmokeTests.fs` | Line 35 (1 occurrence) | Update |

**Total occurrences in src/:** 11 (Router.fs=6, QwenHttpClient.fs=3, CompositionRoot.fs=2)
**Total occurrences in tests/:** ~15 (RouterTests.fs=7, RenderingTests.fs=3, others=~5)

ModelsProbeTests.fs references `"qwen32b"` as a STRING in JSON payloads (not DU cases) — these do
NOT need updating for compilation, but they reference old model paths. Leave as historical test
fixtures (they test the parser with a retired path string, which is valid — the parser doesn't care
what the path names).

### Pitfall 2: CliArgsTests.fs Tests That Assert on `--model 32b`/`--model 72b`

**What goes wrong:** CliArgsTests.fs lines 71-100 test `parseForcedModel (Some "32b")` = `Some Qwen32B`
and `parseForcedModel (Some "72b")` = `Some Qwen72B`. After Phase 19, these must become:
- `parseForcedModel (Some "32b") raises` (retirement error, exit 2 message)
- `parseForcedModel (Some "72b") raises` (retirement error, exit 2 message)
- `parseForcedModel (Some "122b")` = `Some Qwen122B` (new test)
- `parseForcedModel (Some "35b") when withDual=false raises` (new test)
- `parseForcedModel (Some "35b") when withDual=true` = `Some Qwen35B` (new test)
- `parseForcedModel None` = depends on chosen design (see Open Decision below)

Also: test cases at lines 72-89 in CliArgsTests.fs that parse `--model 72b` as a string value will
still succeed (Argu captures the string `"72b"` without validation) — but `parseForcedModel`
downstream raises. The test at line 74-75 (`--model 72b: TryGetResult Model = Some "72b"`) still
works and should stay — it tests Argu parsing, not model validation.

### Pitfall 3: bench/run.sh Gate Label Mismatch

**What goes wrong:** `bench/baseline.json` uses keys like `T6_122b`, but `gate()` in `bench/run.sh`
builds label `"gate_${key}"` and looks for `"$LOG_DIR/gate_${key}.log"`. If baseline.json keys don't
match the run labels exactly, `jq` returns `null` and the gate logic breaks silently.

**Current pattern (run.sh line 173):** `local labels="T6_32b T6_72b W1_32b W2_32b T1_32b T5_72b B2_32b B2_72b"`

After Phase 19, this must be `labels="T6_122b W1_122b W2_122b T1_122b T5_122b B2_122b"` and the
corresponding `run "gate_T6_122b" "122b" "..."` calls in gate() must use matching labels.
The bench-122b-only.sh uses different log dirs (`122b-only-<ts>/`) — after absorption, the
rewritten `gate()` must use `gate-<ts>/` to match existing log parsing logic.

### Pitfall 4: Spectre.Console Markup in Retirement Error Messages

**What goes wrong:** Error messages from `parseForcedModel` that mention model aliases like `[122b]`
or `[35b]` will be parsed by Spectre as color tags if passed to `AnsiConsole.MarkupLine`.

**Current pattern (Program.fs line 54):** `eprintfn "ERROR: %s" ex.Message` — uses `eprintfn`,
NOT AnsiConsole, so Spectre markup is not an issue for retirement error messages. Safe.

However, CLAUDE.md § Common Gotchas documents the Spectre markup pitfall for spinner labels:
`"Thinking... [32B]"` → must be `"[[32B]]"`. The Phase 19 spinner label update changes `"32B"` to
`"122B"` — both need the double-bracket escape. Confirmed: `QwenHttpClient.fs` line 409 already
uses `sprintf "Thinking... [[%s]]" modelLabel`. No new pitfall introduced.

### Pitfall 5: `qwen72b.3bit/` Must Also Be Deleted

**What goes wrong:** SC1 says `~/llm-system/models/` contains ONLY `qwen35b/` and `qwen122b/`.
Currently `~/llm-system/models/` contains: `qwen122b/`, `qwen32b/`, `qwen35b/`, `qwen72b/`,
`qwen72b.3bit/`. Phase 19 must remove `qwen32b`, `qwen72b`, AND `qwen72b.3bit` (~30 GB extra).
Total disk reclaim: 17+38+30 = **85 GB** (well above the ≥50 GB threshold).

### Pitfall 6: Domain.fs DU Rename Breaks `AgentResult.Model` Field

**What goes wrong:** `AgentResult` record (Domain.fs line 193-197) has `Model: Model` field.
All construction sites of `AgentResult` in AgentLoop.fs will break if `Model` DU cases change.

**Current:** `AgentResult { ...; Model = Qwen32B }` or `Model = Qwen72B` in agent loop results.
After rename: must use `Qwen122B` or `Qwen35B`. Check AgentLoop.fs for these assignments.

### Pitfall 7: Test Count Must Stay at 254/1/0 (SC6)

**What goes wrong:** Adding new tests without registering in `RouterTests.fs::rootTests` causes
them to compile but not run (the known pitfall). Any new tests for `--with-35b` or `PathRetired`
must follow the two-step registration: (1) `BlueCode.Tests.fsproj Compile Include` BEFORE
`RouterTests.fs`, (2) list in `rootTests`.

Current count: 254/1/0 tests. Phase 19 replaces old tests (32b/72b → 122b/35b assertions) without
adding net-new count. If new tests ARE added (e.g., for `--with-35b` behavior), they must be
registered.

### Pitfall 8: `documentation/bench.md` Outdated Sections

**What goes wrong:** `documentation/bench.md` has:
- Line 148: `launchctl kickstart -k gui/$(id -u)/com.ohama.qwen32b` in "Hang Contingency" section
- Lines 179-186: sample gate output shows `T6_32b T6_72b W1_32b W2_32b T1_32b T5_72b B2_32b B2_72b`
- Lines 156-160: `unload`/`load` commands referencing `com.ohama.qwen32b`
- Line 203: "B2_32b and B2_72b" in Known Regressions historical note

These are documentation-only regressions but ROADMAP SC5 requires "no Qwen 2.5 references remain"
in key docs. The "Hang Contingency" section should be updated (or removed if 122B-only makes the
hang scenario moot). The "Known Regressions historical note" can stay with a "(historical)"
annotation since it documents past behavior.

### Pitfall 9: bench/run.sh `for model in 32b 72b` Loop Change

**What goes wrong:** Several functions in bench/run.sh use `for model in 32b 72b; do`. After Phase 19,
all loops collapse to a single model `122b`. Functions like `regression()`, `canary()`,
`phase_variance()`, `phase_diagnose()`, `phase_write()` must be rewritten to drop the loop entirely
or use `for model in 122b; do` (semantically a no-op loop). The simpler approach is to inline the
single model value directly (no loop), matching the absorbed `bench-122b-only.sh` style.

### Pitfall 10: ForcedModel Default Behavior Change

**What goes wrong:** Currently `parseForcedModel None = None`, meaning no forced model → routing
via `intentToModel`. After Phase 19, with single-model, `None` should resolve to 122B. If
`parseForcedModel None = None` stays and intent routing stays active (all intents → Qwen122B via
Router.fs rename), it works. But if Router.fs is also simplified to remove `intentToModel`, then
`None` in `ForcedModel` would need a fallback. The planner must choose: keep `intentToModel`
(simplified to always return Qwen122B) or set `ForcedModel = Some Qwen122B` as default.

**Recommendation:** Keep `intentToModel` as-is structurally (still 5 Intent cases, 2 Model cases),
just rename the Model DU cases. `ForcedModel = None` continues to mean "route by intent." All intents
route to Qwen122B (since all cases return Qwen122B). When `--with-35b` is active, Debug/Design/Analysis
route to Qwen35B. This is the cleanest change and avoids API signature breakage.

---

## Open Decisions for the Planner

1. **PathRetired variant location:** Does `AgentError.PathRetired` go in `Domain.fs` (Core DU, visible
   everywhere) or should the retirement guard raise a simple `failwithf` exception caught at
   `parseForcedModel` (Cli-layer-only, no Core change)? Current rejection path for unknown models
   uses `failwithf` caught as `ArguParseException` in Program.fs. ROADMAP SC4 says "PathRetired error
   variant" — the planner must decide if this literally means a new Core DU case or a Cli-layer error.
   Recommendation: Cli-layer-only `failwithf` (no Core change) unless SC4 explicitly requires probe-
   layer detection.

2. **parseForcedModel default return:** Should `parseForcedModel None` return `None` (keep routing)
   or `Some Qwen122B` (explicit default)? Returning `None` preserves the existing architecture where
   `ForcedModel = None` means "use intent routing." Since intent routing will always return Qwen122B
   in single-model mode (all Model cases collapsed), either works. Prefer `None` for minimal change.

3. **`--model 35b` behavior when 35B service is absent (SC3 verbatim):** SC3 says `--model 35b`
   errors with "clear '35B not loaded' message when 35B service is absent." This implies a runtime
   check (probe port 8000 at startup). However, `probeModelInfoAsync` is lazy (fires on first
   CompleteAsync call). Should 19-02 add an eager pre-flight port check for 35B when `--with-35b` is
   set, or let the lazy probe fail naturally (LlmUnreachable on first call)? Recommendation: lazy probe
   is sufficient and consistent with existing architecture. The "clear error" is already provided by
   `LlmUnreachable` with meaningful message. But SC3 says "errors" (implying early, not mid-session).

4. **`--with-35b` + `--model 72b` combination:** Should `--with-35b --model 72b` produce a retirement
   error (72b retired) or a different error? Sequencing: `parseForcedModel` sees `"72b"` first and
   raises retirement error regardless of `--with-35b`. This is correct behavior — `--with-35b` only
   enables `--model 35b`, not any retired alias.

5. **`bench/run.sh --gate` invocation count:** Current gate runs 8 invocations. Single-model gate has
   6 natural entries (T6, W1, W2, T1, T5, B2 all _122b). Should the planner add a 7th to maintain the
   8-count (e.g., T7_122b or canary_T6b_122b), or is 6 sufficient? ROADMAP doesn't specify a minimum
   count for the new gate. 6/6 is cleaner than padding to 8/8.

6. **`documentation/bench.md` "Hang Contingency" fate:** The section documents 32B hang recovery
   with `com.ohama.qwen32b` kickstart commands. After retirement, this section is obsolete. Options:
   remove it (shorter doc), replace with a "122B hang contingency" (same kickstart pattern with 122B
   plist), or annotate as historical. Recommendation: replace with 122B kickstart equivalent.

---

## External References

No external research needed. All findings from direct codebase inspection.

Key cross-references within the repo:
- `documentation/single-model-eval.md` §7 enumerated deferred follow-ups — all 5 are in scope for 19-02
- `scripts/bench-122b-only.sh` — the 122B bench absorption source; structure maps directly to new `bench/run.sh`
- `bench/baseline.json` Phase 18 data — `18-02-BENCH-RESULTS.md` has W1/W2/T1 step counts for new entries
- `documentation/qwen35-install.md §9.4` — exact `launchctl unload` + `rm` commands needed for 19-01

---

## Verification Commands

The planner should use these as `must_haves` in PLAN.md tasks:

### 19-01 Verification

```bash
# Disk reclamation (≥50 GB per SC1)
df -h ~/

# No Qwen 2.5 plists remain
ls ~/Library/LaunchAgents/ | grep ohama
# Expected: com.ohama.qwen35b.plist, com.ohama.qwen122b.plist only

# No Qwen 2.5 model directories remain
ls -d ~/llm-system/models/*/
# Expected: qwen35b/ and qwen122b/ only

# launchd shows only 122b
launchctl list | grep ohama
# Expected: only com.ohama.qwen122b line

# 122B still alive
curl -fsS http://127.0.0.1:8001/v1/models | python3 -c "import sys,json; d=json.load(sys.stdin); print('OK:', d['data'][0]['id'])"
```

### 19-02 Verification

```bash
# Compile succeeds (exhaustive match cascade resolved)
dotnet build src/BlueCode.Cli 2>&1 | grep -E "error|warning" | grep -v "Warning"
# Expected: no errors

# Test suite: 254/1/0 unchanged
dotnet run --project tests/BlueCode.Tests/ --summary 2>&1 | grep -E "Passed:|Failed:|Errored:"
# Expected: Passed: 254, Failed: 1 (known SmokeTests.fs network failure), Errored: 0

# Retirement aliases produce error + exit 2
dotnet run --project src/BlueCode.Cli -- --model 32b "test" 2>&1; echo "Exit: $?"
# Expected: error message mentioning retirement + Phase 19 reference; exit 2

dotnet run --project src/BlueCode.Cli -- --model 72b "test" 2>&1; echo "Exit: $?"
# Expected: same

# Default model works (no flag)
dotnet run --project src/BlueCode.Cli -- "What is 2+2?" 2>&1 | grep -E "Thinking.*122B|final"
# Expected: spinner says [[122B]] and eventually FinalAnswer

# bench/baseline.json has no _35b keys
jq 'keys' bench/baseline.json | grep -v "122b"
# Expected: only _122b keys remain

# bench/run.sh --gate exits 0
bench/run.sh --gate 2>&1 | tail -5
# Expected: GATE PASS

# scripts/bench-122b-only.sh deleted
[ ! -f scripts/bench-122b-only.sh ] && echo "PASS: deleted" || echo "FAIL: still exists"

# No qwen32b/qwen72b references remain in src/
grep -rn "qwen32b\|qwen72b\|Qwen32B\|Qwen72B" src/ --include="*.fs" | grep -v obj/
# Expected: 0 results (comments in QwenHttpClient.fs docblock about "qwen32b" path format
# may remain as documentation strings — check if those need updating too)

# CLAUDE.md has no Qwen 2.5 references
grep -n "32B\|72B\|qwen32b\|qwen72b" CLAUDE.md
# Expected: 0 or only historical cross-references in archived docs section
```

---

## Metadata

**Confidence breakdown:**
- Physical retirement (19-01): HIGH — directory sizes confirmed, plists confirmed, launchctl commands from qwen35-install.md §9.4 are exact
- Domain DU change: HIGH — compile cascade is mechanical; compiler enforces exhaustiveness
- CliArgs/parseForcedModel changes: HIGH — Argu patterns match existing `NewSession` flag shape
- bench/run.sh rewrite: HIGH — target structure is exactly `scripts/bench-122b-only.sh` content
- baseline.json halve: HIGH — Phase 18 bench data provides W1/W2/T1 step counts for new entries
- PathRetired variant: MEDIUM — ROADMAP SC4 is slightly ambiguous about whether Core DU or Cli-layer guard

**Research date:** 2026-04-27
**Valid until:** Until Phase 19 begins execution (architectural state verified against live filesystem above)

---

## RESEARCH COMPLETE

**Phase:** 19 — Qwen 2.5 Retirement + 122B Single-Model Default
**Confidence:** HIGH

### Key Findings

- **Disk reclaim is 85 GB** (17 GB qwen32b + 38 GB qwen72b + 30 GB qwen72b.3bit), well above the ≥50 GB SC1 threshold. `qwen72b.3bit/` is unexpected but must be deleted to satisfy SC1.
- **Model DU rename is a 9-file compile cascade**: Domain.fs → Router.fs → QwenHttpClient.fs → CompositionRoot.fs → 7 test files. Compiler enforces exhaustiveness; no missed cases possible.
- **bench/baseline.json halve is data-sufficient from Phase 18**: W1_122b (step=3), W2_122b (step=3), T1_122b (step=1) step counts available in `documentation/single-model-eval.md`; no re-bench needed for baseline population, but `--gate` must still pass post-code-change.
- **CliArgsTests.fs has 4 test cases that assert on 32b/72b DU mapping** (lines 96-100) — these become retirement-error tests. CliArgs string parsing tests (lines 72-89) remain valid (Argu just captures the string; model validation is downstream).
- **`bench/run.sh` rewrite is mechanical absorption** of `scripts/bench-122b-only.sh`; gate label set shrinks from 8 to 6 entries.

### Files Created

`.planning/phases/19-qwen25-retirement/19-RESEARCH.md`

### Confidence Assessment

| Area | Level | Reason |
|------|-------|--------|
| Physical retirement commands | HIGH | Exact commands from qwen35-install.md §9.4; filesystem state verified |
| Domain DU rename cascade | HIGH | grep-confirmed all 9 files and ~26 occurrences |
| CliArgs/parseForcedModel refactor | HIGH | Exact current code read; Argu patterns understood |
| bench/run.sh rewrite | HIGH | bench-122b-only.sh is the target structure; gate logic traceable |
| PathRetired variant placement | MEDIUM | ROADMAP SC4 slightly ambiguous (Core DU vs Cli failwith) |

### Open Questions for Planner

1. Does SC4 "PathRetired error variant" require a new `AgentError` DU case (Core change) or is Cli-layer `failwithf` sufficient?
2. Should `parseForcedModel None` return `None` (keep intent routing to Qwen122B) or `Some Qwen122B` (explicit default)?
3. Should `--model 35b` with 35B service absent produce early startup error or lazy `LlmUnreachable` on first call?
4. New gate should be 6/6 or padded to 8/8 with additional invocations?

### Ready for Planning

Research complete. Planner can now create 19-01-PLAN.md and 19-02-PLAN.md.
