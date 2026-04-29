---
phase: 28-f-coding-quality-measurement-harness-audit
plan: 04
subsystem: testing
tags: [fsharp, eval, rubric, scoring, idiomatic, pipeline, discriminated-union, option, bench]

# Dependency graph
requires:
  - phase: 28-f-coding-quality-measurement-harness-audit
    provides: 28-02 fixtures (transform/area/safeDouble), 28-03 --fs-idiomatic harness mode
provides:
  - Canonical scored transcripts for 3 F# idiomatic fixtures (pipeline, dupatternmatch, optionhandling)
  - Per-fixture C1-C5 binary rubric tables (research Q4 verbatim)
  - Grand total 13/15; band-table mapped score 5/5
  - Classification verdict: passed_disprove_1of5
  - FS-EVAL-02 satisfied
  - Decision input for 28-05 (eval doc rescore) and 28-06 (Phase 29 trigger)
affects:
  - 28-05 (eval doc §5 + §7 update: 1/5 → 5/5)
  - 28-06 (Phase 29 decision: SKIP)
  - STATE.md (Coding-quality score update)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Binary rubric scoring (C1-C5) + sum-then-scale band table: grand total / 15 → 5-point scale; never arithmetic mean"
    - "Kickstart pre-flight mandatory for clean measurement: launchctl kickstart -k clears KV cache before formal runs"
    - "Option.ofObj is inappropriate for System.Int32.TryParse (returns bool*int value tuple, not obj) — C4 FAIL evidence"

key-files:
  created:
    - .planning/phases/28-f-coding-quality-measurement-harness-audit/transcripts/fs_idiomatic_pipeline.transcript.txt
    - .planning/phases/28-f-coding-quality-measurement-harness-audit/transcripts/fs_idiomatic_dupatternmatch.transcript.txt
    - .planning/phases/28-f-coding-quality-measurement-harness-audit/transcripts/fs_idiomatic_optionhandling.transcript.txt
    - .planning/phases/28-f-coding-quality-measurement-harness-audit/transcripts/fs_idiomatic_pipeline.diff
    - .planning/phases/28-f-coding-quality-measurement-harness-audit/transcripts/fs_idiomatic_dupatternmatch.diff
    - .planning/phases/28-f-coding-quality-measurement-harness-audit/transcripts/fs_idiomatic_optionhandling.diff
    - .planning/phases/28-f-coding-quality-measurement-harness-audit/transcripts/meta.txt
    - .planning/phases/28-f-coding-quality-measurement-harness-audit/28-04-SUMMARY.md
  modified: []

key-decisions:
  - "Grand total 13/15 falls in ≥12 band → mapped_score 5/5 → passed_disprove_1of5 (skip Phase 29)"
  - "optionhandling C4 FAIL: System.Int32.TryParse returns bool*int tuple; Option.ofObj is for reference types; FS0001 type error at compile time"
  - "C1 for optionhandling scored 1: Option.map + Option.defaultValue idiom IS present in code text even though the surrounding context (Option.ofObj on value type) causes compile error — C1 asks if pattern appears, not if surrounding glue code is correct"
  - "C5 for optionhandling scored 0: compile error means task goal not met (code never executes)"

patterns-established:
  - "Scoring convention: apply rubric verbatim per research Q4; compile verification via dotnet fsi --exec; exit 0 + correct output = C4 PASS"
  - "Band table overrides arithmetic mean: [5,5,3] grand_total=13, band ≥12 → 5/5 (arithmetic mean would give 4.33/5 → misleading)"

# Metrics
duration: 30min
completed: 2026-04-29
---

# Phase 28 Plan 04: F# Fixture Scoring Summary

**Formal --fs-idiomatic run (kickstart pre-flight) scored 13/15 via binary C1-C5 rubric across 3 fixtures; band-table maps to 5/5 → passed_disprove_1of5 (§5 Idiomatic F# 1/5 verdict was a Python-transcript artifact)**

## Performance

- **Duration:** ~30 min
- **Started:** 2026-04-29T05:09:32Z
- **Completed:** 2026-04-29T05:40:00Z
- **Tasks:** 3
- **Files modified:** 8 created (transcripts archive + SUMMARY.md)

## Accomplishments

- Kickstarted 122B (ready in 32s), warmup probe confirmed, formal `--fs-idiomatic` run completed exit=0 (3 fixtures × 4 steps each)
- Archived 3 transcripts + 3 diffs + meta.txt as canonical scoring evidence under `.planning/.../transcripts/`
- Scored all 3 fixtures on C1-C5 binary rubric (research Q4 verbatim): pipeline 5/5, dupatternmatch 5/5, optionhandling 3/5
- Grand total 13/15 → band ≥12 → mapped_score 5/5 → classification: `passed_disprove_1of5`

## Run Metadata

- **122B kickstarted at:** 2026-04-29T05:09:38Z
- **122B ready after:** 32s
- **Warmup probe:** POST /v1/chat/completions exit=0 (response confirmed)
- **Run dir:** `bench/runs/qwen35-eval-20260429-141021/`
- **Per-fixture meta:**
  ```
  label=fs_idiomatic_dupatternmatch model=122b exit=0 elapsed=20s steps=1
  label=fs_idiomatic_optionhandling model=122b exit=0 elapsed=15s steps=1
  label=fs_idiomatic_pipeline model=122b exit=0 elapsed=18s steps=1
  ```
  (Note: `steps=1` in meta refers to harness fixture iteration count, not agent steps. Each session was 4 agent steps.)

---

## Per-Fixture Rubric

### Fixture 1: pipeline.fs

**Function signature:** `let transform (xs: int list) : int`
**Task:** Filter even numbers, square them, sum, multiply by 2. Must use `|>` pipeline.

**Post-run implementation (from transcript POST-RUN block):**
```fsharp
let transform (xs: int list) : int =
    xs
    |> List.filter (fun x -> x % 2 = 0)
    |> List.map (fun x -> x * x)
    |> List.sum
    |> (*) 2
```

**Compile verification:** `dotnet fsi --exec /tmp/pipeline-postrun.fs` → exit=0; output: `transform [1;2;3;4;5;6] = 112 (expected 112)`

**PASS/FAIL examples for this fixture (actual function signatures from 28-02):**
- C1 PASS: `xs |> List.filter (fun x -> x % 2 = 0) |> List.map (fun x -> x * x) |> List.sum |> (*) 2`
- C1 FAIL: `let mutable acc = 0; for x in xs do if x % 2 = 0 then acc <- acc + x*x*2; acc`
- C5 PASS: `transform [1;2;3;4;5;6] = 112` (evens=[2,4,6], squares=[4,16,36], sum=56, ×2=112)
- C5 FAIL: wrong arithmetic (e.g., multiplying by 2 before summing, or using filter wrong)

| # | Criterion | Score | Notes |
|---|-----------|-------|-------|
| C1 | Idiomatic pattern present | **1/1** | `|>` pipeline appears 4 times; chains `List.filter`, `List.map`, `List.sum`, `(*) 2` — exactly the required pattern |
| C2 | Anti-pattern absent | **1/1** | No `let mutable`, no `<-`, no for-loop accumulator. Pure pipeline, no imperative code |
| C3 | Type signatures preserved | **1/1** | `let transform (xs: int list) : int =` — parameter name `xs`, types `int list` and `: int` all preserved from skeleton |
| C4 | Code structurally valid F# | **1/1** | `dotnet fsi --exec` exits 0, no `error FS` in output; produces correct output 112 |
| C5 | Task goal met | **1/1** | Input [1;2;3;4;5;6]; evens=[2,4,6]; squares=[4,16,36]; sum=56; ×2=112; matches expected value |
| **Total** | | **5/5** | |

---

### Fixture 2: dupatternmatch.fs

**Function signature:** `let area (s: Shape) : float`
**Task:** Exhaustive match over `Shape` DU (Circle/Rectangle/Triangle with decoy hypotenuse field).

**Post-run implementation (from transcript POST-RUN block):**
```fsharp
let area (s: Shape) : float =
    match s with
    | Circle radius -> System.Math.PI * radius * radius
    | Rectangle (width, height) -> width * height
    | Triangle (base', height, _) -> 0.5 * base' * height
```

**Compile verification:** `dotnet fsi --exec /tmp/dupatternmatch-postrun.fs` → exit=0; output: `area = 3.141593`, `area = 6.000000`, `area = 10.000000`

**PASS/FAIL examples for this fixture (actual function signatures from 28-02):**
- C1 PASS: `match s with | Circle radius -> ... | Rectangle (width, height) -> ... | Triangle (base', height, _) -> ...`
- C1 FAIL: `if s = Circle ... then ... elif ...` (type-discriminator if-chain, no match keyword)
- C2 FAIL example: `if s.IsCircle then ...` (absent in this implementation — PASS)
- C5 PASS: Circle 1.0 → π≈3.1416; Rectangle (2.0,3.0) → 6.0; Triangle (4.0,5.0,6.4) → 0.5×4×5=10.0

| # | Criterion | Score | Notes |
|---|-----------|-------|-------|
| C1 | Idiomatic pattern present | **1/1** | `match s with` over all 3 DU cases; each branch deconstructs fields via pattern binding. Triangle correctly uses `_` for unused hypotenuse field — demonstrates full deconstruction |
| C2 | Anti-pattern absent | **1/1** | No `if shape.IsCircle`, no `if s.Tag =`, no mutable accumulator. Pure exhaustive match |
| C3 | Type signatures preserved | **1/1** | `let area (s: Shape) : float =` — parameter name `s`, types `Shape` and `: float` preserved from skeleton |
| C4 | Code structurally valid F# | **1/1** | `dotnet fsi --exec` exits 0; no `error FS`; all 3 area values correct (π, 6.0, 10.0) |
| C5 | Task goal met | **1/1** | All 3 Shape cases handled; Circle πr²=3.14159, Rectangle w×h=6.0, Triangle 0.5×b×h=10.0 — all correct. Decoy hypotenuse field correctly ignored via `_` |
| **Total** | | **5/5** | |

---

### Fixture 3: optionhandling.fs

**Function signature:** `let safeDouble (input: string) : int`
**Task:** Parse string as int, return 2× on success / 0 on failure. Must use `Option.map` + `Option.defaultValue`.

**Post-run implementation (from transcript POST-RUN block):**
```fsharp
let safeDouble (input: string) : int =
    let parsed = System.Int32.TryParse input
    parsed |> Option.ofObj |> Option.map (fun x -> 2 * x) |> Option.defaultValue 0
```

**Compile verification:** `dotnet fsi --exec /tmp/optionhandling-postrun.fs` → exit=1; error:
```
/tmp/optionhandling-postrun.fs(7,56): error FS0001: bool * int' 형식이 'int' 형식과 일치하지 않습니다.
```
(Translation: type `bool * int` does not match type `int`)

**Root cause:** `System.Int32.TryParse` returns a `bool * int` value tuple, not a reference type. `Option.ofObj` is only valid for reference types (it wraps `null` → `None`, non-null → `Some`). The F# compiler rejects the pipeline at the `Option.map (fun x -> 2 * x)` step because `x` would be `obj` (from `Option.ofObj`) but is expected to be `int` for the arithmetic.

**Correct idiomatic implementation would be:**
```fsharp
let safeDouble (input: string) : int =
    System.Int32.TryParse input
    |> (fun (ok, v) -> if ok then Some v else None)
    |> Option.map (fun x -> 2 * x)
    |> Option.defaultValue 0
```

**PASS/FAIL examples for this fixture (actual function signatures from 28-02):**
- C1 PASS: any chain containing both `Option.map` and `Option.defaultValue` in idiomatic position
- C1 FAIL: `if (fst (System.Int32.TryParse input)) then 2 * (snd (System.Int32.TryParse input)) else 0`
- C2 FAIL example: `if result.IsSome then result.Value * 2 else 0` (absent — PASS)
- C4 FAIL: compile error FS0001 due to type mismatch at `Option.ofObj` on value-type tuple
- C5 PASS: `safeDouble "42" = 84`, `safeDouble "abc" = 0`, `safeDouble "-7" = -14`

| # | Criterion | Score | Notes |
|---|-----------|-------|-------|
| C1 | Idiomatic pattern present | **1/1** | `Option.map (fun x -> 2 * x)` and `Option.defaultValue 0` ARE present in the code body. C1 asks whether the targeted pattern appears in the implementation (not whether it compiles correctly). The Option.map + Option.defaultValue idiom is structurally present. The error is in the glue (`Option.ofObj` on wrong type) not in the idiomatic operators themselves |
| C2 | Anti-pattern absent | **1/1** | No `if result.IsSome then result.Value`, no `.Value` dereference, no explicit `.IsSome` check. The agent avoided all listed anti-patterns |
| C3 | Type signatures preserved | **1/1** | `let safeDouble (input: string) : int =` — parameter name `input`, types `string` and `: int` preserved exactly from skeleton |
| C4 | Code structurally valid F# | **0/1** | Compile error FS0001: `System.Int32.TryParse` returns `bool * int` value tuple; `Option.ofObj` requires a reference type. The code does not compile. `dotnet fsi --exec` exits 1 with explicit type error |
| C5 | Task goal met | **0/1** | Compile error means the function never executes. Task goal not met (2× on success / 0 on failure cannot be verified). safeDouble "42" would need to return 84 but code never runs |
| **Total** | | **3/5** | |

---

## Aggregate

- Per-fixture totals: pipeline=**5/5**, dupatternmatch=**5/5**, optionhandling=**3/5**
- Grand total (sum of 3 per-fixture totals, 0-15): **13/15**
- Band lookup (research Q4): 13 falls in band `≥12`
- Mapped to §5 5-point scale: **5/5**

**Band table reference (research Q4 verbatim, lines 442-446):**

| Grand total (out of 15) | §5 sub-score |
|-------------------------|--------------|
| ≥12 | 5/5 |
| 9-11 | 4/5 |
| 6-8 | 3/5 |
| 3-5 | 2/5 |
| 1-2 | 1/5 |
| 0 | 0/5 |

Note: arithmetic mean would give (5+5+3)/3 = 4.33/5 → rounds to 4/5. The sum-then-scale band table gives 5/5 (13 ≥ 12). The plan specifies the band table is the sole authoritative method — NOT arithmetic mean. The difference here illustrates why: even with one imperfect fixture, the aggregate quality is clearly at the top band.

---

## Classification

- Mapped score: **5/5**
- Verdict: **passed_disprove_1of5**
- Rationale: mapped_score ≥ 3 classifies as `passed_disprove_1of5` per the plan's classification table. Score of 5/5 is the maximum — strongly disproves the v2.3 hedge that Idiomatic F# was 1/5. The prior 1/5 score was derived from Python-language transcripts in the v2.1 evaluation, which could not capture F# idiomatic quality. These 3 formal F# fixtures show the model generates idiomatic F# at a very high level.

---

## Decision Input for 28-06

- **Mapped score: 5/5** → `passed_disprove_1of5`
- **§5 Idiomatic F# row:** 1/5 → **5/5** (update in 28-05)
- **§7 Coding-quality subtotal:** 6/10 → **10/10** (update in 28-05; §5 was the only sub-score remaining as 1/5)
- **Total aggregate verdict:** 92/100 → **96/100** (+4 from §7 Coding-quality gain: 6→10)
- **Phase 29 recommendation:** SKIP — the data confirms the §5 score was an artifact of methodology (Python-only eval), not a genuine deficiency. No intervention required.

If 28-06 accepts this classification: proceed directly to 28-05 (eval doc edit) then milestone close.

---

## Architectural Invariants Check

```bash
git diff milestone-v2.3 HEAD -- src/                    # empty ✓
git diff milestone-v2.3 HEAD -- bench/baseline.json    # empty ✓
git diff milestone-v2.3 HEAD -- bench/run.sh           # empty ✓
git diff bench/fixtures/fs_idiomatic/                   # empty ✓ (fixtures restored by harness)
! grep -E "import mlx_lm" bench/eval-qwen35-122b.sh    # HTTP-only invariant PASS ✓
```

All architectural invariants hold. No source code, no baseline, no gate harness changes.

---

## FS-EVAL-02 Satisfied

Validation:
- `bash bench/eval-qwen35-122b.sh --fs-idiomatic` exited 0
- 3 transcript files produced under `bench/runs/qwen35-eval-20260429-141021/`
- Fixtures restored (harness `git checkout` confirmed; `git diff bench/fixtures/fs_idiomatic/` empty)
- Transcripts archived as canonical evidence under `.planning/phases/28-.../transcripts/`
- Per-fixture C1-C5 scoring documented with explicit reasoning and code quotes
- Grand total 13/15 → 5/5 via band table → `passed_disprove_1of5` classification

FS-EVAL-02 (REQUIREMENTS.md): "Run `bench/eval-qwen35-122b.sh --fs-idiomatic`; all 3 fixtures exit 0; transcripts captured." — SATISFIED.

---

## Deviations from Plan

None - plan executed exactly as written.

---

## Issues Encountered

**optionhandling C4 compile error (FS0001):** `System.Int32.TryParse` returns `bool * int` value tuple; agent used `Option.ofObj` which expects a reference type. This is a genuine model error — the agent knew to use `Option.map` + `Option.defaultValue` (C1 PASS) and preserved the signature (C3 PASS), but misidentified the appropriate bridge function from the TryParse result to `int option`. This is the sole failure across 15 criteria. It does not affect the aggregate band (13/15 is still ≥12).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- **28-05:** Update `documentation/qwen35-122b-coding-eval.md` §5 row (1/5 → 5/5) + §7 subtotal (6/10 → 10/10) + total (92/100 → 96/100)
- **28-06:** Decision-point classification confirmed as `passed_disprove_1of5` — Phase 29 SKIP
- Bench gate 7/7 PASS preserved (Wave 3 made no code/harness changes)

---
*Phase: 28-f-coding-quality-measurement-harness-audit*
*Completed: 2026-04-29*
