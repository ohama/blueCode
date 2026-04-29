---
phase: 28-f-coding-quality-measurement-harness-audit
plan: 02
subsystem: testing
tags: [fsharp, fixtures, bench, idiomatic, pipeline, discriminated-union, option, eval]

# Dependency graph
requires:
  - phase: 28-f-coding-quality-measurement-harness-audit
    provides: RESEARCH.md Q3 + Q12 — fixture design constraints, byte limits, step budget
provides:
  - bench/fixtures/fs_idiomatic/ directory with 3 fixture pairs
  - pipeline fixture (transform, int list -> int, |> pipeline)
  - dupatternmatch fixture (area, Shape -> float, exhaustive match over DU)
  - optionhandling fixture (safeDouble, string -> int, Option.map + Option.defaultValue)
  - FS-EVAL-01 requirement satisfied
affects:
  - 28-03 (--fs-idiomatic harness mode consumes these fixtures)
  - 28-04 (rubric review reads agent output against these canonical skeletons)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "F# fixture pair: .task.md (≤500 bytes, explicit idiomatic pattern mention) + .fs skeleton (module + failwith TODO + top-level printfn)"
    - "FSI standalone compile: dotnet fsi <file>.fs </dev/null — no error FS in output = PASS; FSI exit 1 on EOF from interactive prompt is expected, not a failure"
    - "Top-level printfn in .fs skeletons (no [<EntryPoint>]): FSI executes module-level code directly; try...with catches failwith at runtime"

key-files:
  created:
    - bench/fixtures/fs_idiomatic/pipeline.task.md
    - bench/fixtures/fs_idiomatic/pipeline.fs
    - bench/fixtures/fs_idiomatic/dupatternmatch.task.md
    - bench/fixtures/fs_idiomatic/dupatternmatch.fs
    - bench/fixtures/fs_idiomatic/optionhandling.task.md
    - bench/fixtures/fs_idiomatic/optionhandling.fs
  modified: []

key-decisions:
  - "Used top-level printfn (no [<EntryPoint>]) in .fs skeletons so FSI executes them as scripts without --exec flag"
  - "FSI exits 1 on /dev/null EOF (interactive mode); verification relies on absence of 'error FS' in output, not exit code"
  - "Triangle DU field named base' (F# keyword escape) with hypotenuse as unused decoy field"
  - "optionhandling.fs uses try...with _ -> -1 (not 0) in top-level loop to distinguish failwith from successful 0 result"

patterns-established:
  - "Fixture pair convention: <name>.task.md ≤500 bytes + <name>.fs compiles standalone via dotnet fsi"
  - "Skeleton pattern: module + doc comment + let <fn> ... = failwith 'TODO' + top-level try...with printfn"
  - "Fixture restored via git checkout between agent runs (same as refactor_multifile precedent)"

# Metrics
duration: 4min
completed: 2026-04-28
---

# Phase 28 Plan 02: F# Fixture Design Summary

**Three idiomatic F# fixture pairs under bench/fixtures/fs_idiomatic/ covering pipeline |>, exhaustive DU match, and Option.map/defaultValue chains — FS-EVAL-01 satisfied**

## Performance

- **Duration:** ~4 min
- **Started:** 2026-04-28T10:34:33Z
- **Completed:** 2026-04-28T10:38:15Z
- **Tasks:** 3
- **Files modified:** 6 (all created)

## Accomplishments

- Created `pipeline.fs` + `pipeline.task.md` (472 bytes): `transform : int list -> int` skeleton requiring `|>` pipeline of List.filter/map/sum
- Created `dupatternmatch.fs` + `dupatternmatch.task.md` (496 bytes): `area : Shape -> float` skeleton with `Shape` DU (Circle/Rectangle/Triangle); Triangle has hypotenuse decoy field to test full deconstruction
- Created `optionhandling.fs` + `optionhandling.task.md` (495 bytes): `safeDouble : string -> int` skeleton requiring Option.map + Option.defaultValue chain on Int32.TryParse result

## Task Commits

All three tasks committed atomically in a single feat commit:

1. **Task 1: pipeline fixture** - `25eb35d` (feat)
2. **Task 2: dupatternmatch fixture** - `25eb35d` (feat)
3. **Task 3: optionhandling fixture** - `25eb35d` (feat)

**Plan metadata:** (docs commit — see below)

## Files Created/Modified

- `bench/fixtures/fs_idiomatic/pipeline.task.md` (472 bytes) — pipeline |> task description; mentions pipeline and |>
- `bench/fixtures/fs_idiomatic/pipeline.fs` — `let transform (xs: int list) : int = failwith "TODO"` + top-level try/printfn
- `bench/fixtures/fs_idiomatic/dupatternmatch.task.md` (496 bytes) — DU match task; mentions "exhaustive match", "discriminated union"
- `bench/fixtures/fs_idiomatic/dupatternmatch.fs` — `type Shape = Circle | Rectangle | Triangle` + `let area (s: Shape) : float = failwith "TODO"` + loop
- `bench/fixtures/fs_idiomatic/optionhandling.task.md` (495 bytes) — Option chain task; mentions Option.map and Option.defaultValue
- `bench/fixtures/fs_idiomatic/optionhandling.fs` — `let safeDouble (input: string) : int = failwith "TODO"` + top-level loop

## Decisions Made

**FSI script structure:** Used top-level printfn (no `[<EntryPoint>]`) so FSI executes the file as a script without needing `--exec`. This means `dotnet fsi <file>.fs </dev/null` compiles and runs the printfn (via try...with catching the failwith), producing observable output. FSI exits 1 on EOF from the interactive prompt — this is expected behavior, not a compile failure. Verification uses `grep -E 'error FS'` absence, not exit code.

**Triangle hypotenuse as decoy:** The `Triangle of base' * height * hypotenuse` shape intentionally includes a third field that the correct area formula does not use. This requires the agent to write `| Triangle (b, h, _)` or `| Triangle (base', height, _hyp)` — exercising full field deconstruction in the match pattern rather than accessing fields by position.

**safeDouble uses -1 sentinel:** The top-level loop uses `try safeDouble s with _ -> -1` (not 0) to distinguish the failwith exception from a successful parse of "0" → 0. This makes the compile-time output unambiguous: all five calls print -1 in the skeleton state.

## Deviations from Plan

**1. [Rule 1 - Bug] Removed [<EntryPoint>] from .fs skeletons**
- **Found during:** Task 1 (pipeline.fs compile check)
- **Issue:** `[<EntryPoint>]` in FSI context causes FSI to skip `main` (warning FS2304) and exit 1; the printf line never executes, so verification grep would fail
- **Fix:** Replaced `[<EntryPoint>] let main _ = ...` with direct top-level `let result = try ... with _ -> 0` + `printfn` — standard FSI script pattern
- **Files modified:** pipeline.fs (and pre-applied to dupatternmatch.fs, optionhandling.fs)
- **Verification:** `dotnet fsi pipeline.fs </dev/null` outputs `transform [1;2;3;4;5;6] = 0 (expected 112)` with no `error FS`
- **Committed in:** 25eb35d (fixture commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - bug in skeleton design)
**Impact on plan:** Fix was necessary for verification to pass. No scope change.

## Issues Encountered

FSI exit code behavior: `dotnet fsi <file>.fs </dev/null` always exits 1 when stdin hits EOF because FSI enters interactive mode after loading the file. This is FSI's design, not a compile error. The plan-level verification correctly guards against this by checking for `error FS` in output rather than exit code. The `dotnet fsi --exec` flag produces exit 0 but the plan calls for the positional form; both correctly compile the files.

## User Setup Required

None - no external service configuration required.

## Note for 28-03 Executor

Fixtures expect agent invocation as:
```
dotnet run --project src/BlueCode.Cli -- --verbose --model 122b \
  'Read bench/fixtures/fs_idiomatic/<name>.task.md and fill the holes in bench/fixtures/fs_idiomatic/<name>.fs.'
```

Restore canonical fixture state between runs:
```bash
git checkout bench/fixtures/fs_idiomatic/<name>.fs
```

## Next Phase Readiness

- All 3 fixture pairs under `bench/fixtures/fs_idiomatic/` are ready for 28-03 harness integration
- Each `.fs` skeleton compiles standalone; each `.task.md` is ≤500 bytes with explicit idiomatic pattern mention
- FS-EVAL-01 requirement in REQUIREMENTS.md is satisfied
- 28-03 must add `--fs-idiomatic` case to `bench/eval-qwen35-122b.sh` (using `run_refactor` as template per RESEARCH Q1)

---
*Phase: 28-f-coding-quality-measurement-harness-audit*
*Completed: 2026-04-28*
