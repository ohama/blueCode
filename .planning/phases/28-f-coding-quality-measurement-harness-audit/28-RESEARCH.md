# Phase 28: F# Coding Quality Measurement + Harness Audit — Research

**Researched:** 2026-04-29
**Domain:** Eval harness extension (bash) + F# idiomatic fixture design + eval doc editing
**Confidence:** HIGH

---

## Summary

Phase 28 is a measurement + documentation phase. No source code changes to `src/`. The work
lives in three files: `bench/eval-qwen35-122b.sh` (new `--fs-idiomatic` mode), a new
`bench/fixtures/fs_idiomatic/` directory (3-5 fixture pairs), and
`documentation/qwen35-122b-coding-eval.md` (§5 + §7 re-score).

The harness is a single-file bash script (~571 lines post-v2.3). Each eval mode is a
self-contained function (`run_refactor`, `run_langcoverage`, etc.) dispatched from a `case`
statement at line 556. `run_refactor` (line 259-314) is the canonical template for the new
`run_fs_idiomatic` function: it calls `require_port_8001`, uses `set +e` around the blueCode
invocation, writes a `.diff.txt` output file and a `.meta` file, and appends to
`$LOG_DIR/timeline.txt`.

The eval doc has precise edit sites. §5 Idiomatic F# row is at line 861; §7 grand total is at
line 881; final scorecard line is at line 1051. All three must be updated atomically.

**Primary recommendation:** Mirror `run_refactor()` line-for-line for the new mode handler;
use `git checkout bench/fixtures/fs_idiomatic/` between fixture runs (analogous to bench/run.sh
EXIT trap restoring refactor_multifile fixtures after gate runs).

---

## Q1: Current State of `bench/eval-qwen35-122b.sh`

**File:** `bench/eval-qwen35-122b.sh` (571 lines)

### Case dispatch (lines 556-570)

```bash
case "${1:-}" in
  --setup)        setup_venv ;;
  --throughput)   run_throughput ;;
  --ttft)         run_ttft ;;
  --multiturn)    run_multiturn ;;
  --refactor)     run_refactor ;;
  --langcoverage) run_langcoverage ;;
  --schema-rate)  run_schema_rate ;;
  --humaneval)    run_humaneval ;;
  --needle)       run_needle ;;
  --coldstart)    run_coldstart ;;
  --full)         run_full ;;
  -h|--help|"")   usage ;;
  *)              echo "Unknown flag: $1" >&2; usage; exit 1 ;;
esac
```

`--fs-idiomatic` is inserted as a new branch. The `run_full` function (lines 500-531) must also
gain a call to `run_fs_idiomatic` in its phase sequence if full-eval coverage is desired; however
per ROADMAP guardrails, `bench/run.sh` is NOT modified — only `bench/eval-qwen35-122b.sh`.

### `run_refactor()` — closest template for `run_fs_idiomatic` (lines 259-314)

This is the canonical agent-loop, per-fixture, git-checkout-aware handler. Key structure:

```bash
run_refactor() {
  require_port_8001
  mkdir -p "$LOG_DIR"
  local fixture_dir="bench/fixtures/refactor_multifile"
  local prompt
  prompt="Read $fixture_dir/README.md and perform the refactor task it describes. ..."
  local out="$LOG_DIR/refactor_multifile_diff.txt"
  local meta="$LOG_DIR/refactor_multifile.meta"
  echo "===== refactor_multifile (model=122b) =====" | tee -a "$LOG_DIR/timeline.txt"
  echo "PROMPT: $prompt" >> "$out"
  echo "----" >> "$out"
  local start_ts
  start_ts=$(date +%s)
  set +e
  /usr/bin/time -p dotnet run --project src/BlueCode.Cli -- --verbose --model 122b "$prompt" >> "$out" 2>&1
  local exit_code=$?
  set -e
  local end_ts
  end_ts=$(date +%s)
  local elapsed=$((end_ts - start_ts))
  # ... post-run file state capture + orphan check ...
  echo "label=refactor_multifile model=122b exit=$exit_code elapsed=${elapsed}s orphan_add_refs=$orphan_count" > "$meta"
  echo "  -> exit=$exit_code elapsed=${elapsed}s orphan_add_refs=$orphan_count" | tee -a "$LOG_DIR/timeline.txt"
}
```

Notable: `set +e` / `set -e` wraps the blueCode invocation (Pattern 1 below). The `.meta` file
records `label=`, `model=`, `exit=`, `elapsed=` on a single line. The `.diff.txt` file captures
full stdout+stderr from blueCode, prefixed with the prompt.

### 4 documented bash-strict-mode patches in the script

**Pattern 1: set-e/dotnet-exit (line 274)**
```bash
set +e
/usr/bin/time -p dotnet run ... >> "$out" 2>&1
local exit_code=$?
set -e
```
Comment at line 272: "Capture exit_code explicitly; use set +e to prevent set -e from aborting
on non-zero exit (blueCode exits 1 on MaxLoopsExceeded, which is data, not a harness failure)".
Present in `run_refactor` (line 274), `run_langcoverage` (line 329), `run_multiturn` (line 372
+ line 394), `run_schema_rate` (line 434).

**Pattern 2: grep-c/pipefail double-output guard (lines 295-300)**
```bash
orphan_count=$( (grep -cE '\b(let |Calculator\.)add\b' \
    "$fixture_dir/Calculator.fs" \
    "$fixture_dir/Main.fs" \
    "$fixture_dir/Tests.fs" 2>/dev/null || true) | awk -F: '{sum+=$2} END {print sum+0}')
```
Comment at line 295: "Use || true inside the subshell: grep exits 1 when no matches found (the
PASS case); without this guard, set -euo pipefail aborts the script before writing
refactor_orphan_count.txt." Also present in `run_schema_rate` lines 438-441 and 450-452.

**Pattern 3: mkdir-before-tee (throughout)**
Every function that writes to `$LOG_DIR/timeline.txt` calls `mkdir -p "$LOG_DIR"` BEFORE the
`tee` invocation. `run_coldstart` (line 467) is the specific fix from commit `4bcd8a4` (23-01):
`mkdir -p "$LOG_DIR"` appears at line 467, before `| tee -a "$LOG_DIR/timeline.txt"` at line 468.

**Pattern 4: grep-cE-zero-match-exit-1 (lines 402-404)**
```bash
invalid_json=$(grep -c "InvalidJsonOutput" "$out_file" 2>/dev/null || true)
step_count=$(grep -cE "Session ok: [0-9]+ steps" "$out_file" 2>/dev/null || true)
```
Comment at lines 438-440: "Use || true to suppress grep exit-1 (no matches) under set -euo
pipefail. grep -c outputs '0' on no match (exit 1); with || true, the substitution captures '0'."
This is the v2.3 27-02 pattern (commit `9f8e06e`).

**Optional Pattern 5: BSD seq countdown bug (line 390)**
```bash
for k in $([ "$n" -ge 2 ] && seq 2 "$n" || true); do
```
Comment at line 387: "Guard: seq 2 N on macOS BSD counts DOWN when N<2 (seq 2 1 = '2 1').
Explicit guard prevents spurious extra turns for N=1." This is the minor 5th pattern surfaced
in v2.1 21-04.

---

## Q2: Current State of `bench/fixtures/`

### Directory listing

```
bench/fixtures/
├── bug_average.fs
├── bug_binsearch.fs
├── bug_divide_zero.fs
├── bug_lastchar.fs
├── bug_python_typeerror.py
├── bug_typescript_async.ts
├── mt_followup.txt
├── multiturn_prompts.txt
└── refactor_multifile/
    ├── Calculator.fs
    ├── Main.fs
    ├── README.md
    └── Tests.fs
```

Top-level fixtures are single files (no subdirectory). `refactor_multifile/` is the only
subdirectory. Its README.md is the task description; Calculator.fs, Main.fs, Tests.fs are the
mutable working files. There is NO `.task.md` file in `refactor_multifile/` — the README.md
serves that role. The fixture files are mutable (agent edits them); the README.md is
immutable ("do NOT modify").

### `refactor_multifile/README.md` format (verbatim, lines 1-45)

```markdown
# Multi-file Refactor Task

The `Calculator` module exposes **two** functions that need to be renamed:
...
## Task
Apply BOTH renames across all three F# files. Preserve all behavior, including tests.
### Rename 1: `add` → `sum`
...
## Completion checklist
After your refactor, ALL of the following must be true:
- [ ] `Calculator.fs` defines `sum` and `sum3`; no remaining `let add` or `let add3`
...
## Files in this directory
- `Calculator.fs` — module to refactor (defines `add` and `add3`)
...
- `README.md` — this task statement (do NOT modify)
```

Key conventions to mirror for `fs_idiomatic/` fixtures:
- Task file is `README.md` (or `.task.md` per ROADMAP; adopt `.task.md` since ROADMAP specifies it)
- Mutable working files listed under "Files in this directory" section
- Completion checklist (observable, binary criteria)
- "do NOT modify" the task file

### `refactor_multifile/Calculator.fs` (canonical state, lines 1-9)

```fsharp
module Calculator

/// Adds two integers and returns the result.
let add (x: int) (y: int) : int =
    x + y

/// Adds three integers and returns the result.
let add3 (x: int) (y: int) (z: int) : int =
    add (add x y) z
```

Style conventions: explicit type annotations on all parameters and return type; XML doc comment
(`///`); module declaration. New `.fs` skeletons must follow same conventions.

---

## Q3: F# Fixture Design — Concrete Proposals

### Design constraints
- Each `.fs` must compile standalone before the agent fills holes
- `failwith "TODO"` is the idiomatic F# hole marker (compiles as `raise (System.Exception("TODO"))`)
- Keep types primitive (int, string, list, Option, Result) — no custom DUs in skeleton unless the DU is trivial and self-contained
- Stick to 5-7 agent steps (read .task.md + read .fs + possibly read one more + edit_file + verify + final). If more, rubric contaminated by step-budget pressure.
- Anti-pattern must be realistic: something an LLM trained on C# or Python could fall into

### Fixture 1: `pipeline` — pipeline operator `|>`

**Files:** `pipeline.task.md` + `pipeline.fs`

**task.md (≤500 chars):**
```
Read bench/fixtures/fs_idiomatic/pipeline.fs. Implement the body of `processNumbers` using F# pipeline operator `|>`. The function takes a list of ints, filters to keep only positives, doubles each element, and returns the sum. Use `List.filter`, `List.map`, and `List.sum` chained with `|>`. Do not use mutable variables or explicit loops.
```
(314 chars)

**pipeline.fs skeleton:**
```fsharp
module Pipeline

/// Filters to positives, doubles each, returns sum.
/// Idiomatic: use |> to chain List.filter >> List.map >> List.sum
let processNumbers (nums: int list) : int =
    failwith "TODO: implement using |> pipeline"
```

**Compiles standalone:** Yes — `failwith` compiles; return type is `int`.

**Anti-pattern temptation:** Agent uses a mutable accumulator with a `for` loop:
```fsharp
let mutable acc = 0
for n in nums do
    if n > 0 then acc <- acc + (n * 2)
acc
```
Or nested intermediate bindings without `|>`:
```fsharp
let pos = List.filter (fun n -> n > 0) nums
let doubled = List.map (fun n -> n * 2) pos
List.sum doubled
```
(This is correct but not idiomatic; idiomatic uses `|>`.)

---

### Fixture 2: `dupatternmatch` — discriminated union exhaustive pattern matching

**Files:** `dupatternmatch.task.md` + `dupatternmatch.fs`

**task.md (≤500 chars):**
```
Read bench/fixtures/fs_idiomatic/dupatternmatch.fs. Implement `describeShape` using exhaustive F# pattern matching on the `Shape` discriminated union. Return a string describing each case. Use `match ... with` — do not use if/elif chains or `.IsCircle`-style property checks.
```
(271 chars)

**dupatternmatch.fs skeleton:**
```fsharp
module DuPatternMatch

type Shape =
    | Circle of radius: float
    | Rectangle of width: float * height: float
    | Triangle of base_: float * height: float

/// Returns a description string for each shape variant.
/// Idiomatic: use exhaustive match expression, not if/elif or .IsCircle
let describeShape (shape: Shape) : string =
    failwith "TODO: implement using match expression"
```

**Compiles standalone:** Yes — the DU is defined in the same file; `failwith` compiles.

**Anti-pattern temptation:**
```fsharp
if shape.IsCircle then
    sprintf "Circle with radius %.1f" (shape |> function Circle r -> r | _ -> 0.0)
elif ...
```
Or accessing union case fields via `.Value` after `IsSome`-style property checks.

---

### Fixture 3: `optionhandling` — `Option.map` / `Option.defaultValue` chain

**Files:** `optionhandling.task.md` + `optionhandling.fs`

**task.md (≤500 chars):**
```
Read bench/fixtures/fs_idiomatic/optionhandling.fs. Implement `safeDivide` and `formatResult`. `safeDivide x y` returns `Some (x/y)` if `y <> 0`, else `None`. `formatResult` takes the Option from `safeDivide`, maps it to a formatted string using `Option.map`, and returns a default "division by zero" message using `Option.defaultValue`. Do not use if/else inside `formatResult`.
```
(390 chars)

**optionhandling.fs skeleton:**
```fsharp
module OptionHandling

/// Returns Some (x/y) if y is non-zero, None otherwise.
let safeDivide (x: int) (y: int) : int option =
    failwith "TODO: implement safeDivide"

/// Maps the option to a string or returns default message.
/// Idiomatic: use Option.map and Option.defaultValue, not if/else
let formatResult (result: int option) : string =
    failwith "TODO: implement using Option.map |> Option.defaultValue"
```

**Compiles standalone:** Yes.

**Anti-pattern temptation:**
```fsharp
let formatResult result =
    if result.IsSome then
        sprintf "Result: %d" result.Value
    else
        "division by zero"
```

---

### Fixture 4: `resultbind` — `Result.bind` chain (optional, use if wanting 4 fixtures)

**Files:** `resultbind.task.md` + `resultbind.fs`

**task.md (≤500 chars):**
```
Read bench/fixtures/fs_idiomatic/resultbind.fs. Implement `validateAge` and `validateName` returning `Result<int,string>` and `Result<string,string>`. Then implement `validatePerson` using `Result.bind` to chain both validations without nested match expressions or early returns.
```
(270 chars)

**resultbind.fs skeleton:**
```fsharp
module ResultBind

/// Returns Ok age if 0 < age <= 150, else Error "invalid age"
let validateAge (age: int) : Result<int, string> =
    failwith "TODO"

/// Returns Ok name if non-empty, else Error "empty name"
let validateName (name: string) : Result<string, string> =
    failwith "TODO"

/// Chains both validations using Result.bind.
/// Idiomatic: use Result.bind, not nested match expressions
let validatePerson (name: string) (age: int) : Result<string, string> =
    failwith "TODO: use Result.bind chain"
```

**Compiles standalone:** Yes.

**Anti-pattern temptation:**
```fsharp
let validatePerson name age =
    match validateAge age with
    | Error e -> Error e
    | Ok validAge ->
        match validateName name with
        | Error e -> Error e
        | Ok validName -> Ok (sprintf "%s (age %d)" validName validAge)
```
(Correct but not idiomatic; idiomatic uses `Result.bind`.)

---

### Compile-standalone strategy

Each `.fs` skeleton contains a `module` declaration and uses `failwith "TODO"` for holes.
`failwith` is of type `'a` in F# (generic), so it satisfies any return type annotation. A
standalone `dotnet fsi` or `dotnet build` of the skeleton file will compile without error.

**Verification command for each skeleton:**
```bash
dotnet fsi bench/fixtures/fs_idiomatic/pipeline.fs
```
If it outputs "Unhandled exception: System.Exception: TODO", the skeleton compiles (the exception
is from runtime evaluation, not a compile error). For build-based verification:
```bash
# Wrap in minimal .fsx for standalone check:
dotnet fsi --exec bench/fixtures/fs_idiomatic/pipeline.fs 2>&1 | grep -v "TODO"
```
Alternatively, since fixtures don't need a project file, `dotnet fsi` is the right tool.

---

## Q4: Scoring Rubric — Concrete Proposal

### Per-fixture rubric (5 binary criteria = 5 points max)

| Criterion | Points | How to assess |
|-----------|--------|---------------|
| **C1: Idiomatic pattern present** | 0 or 1 | `|>` appears in pipeline fixture; `match` appears in DU fixture; `Option.map`+`Option.defaultValue` appears in option fixture; `Result.bind` in result fixture |
| **C2: Anti-pattern absent** | 0 or 1 | No mutable accumulator (`let mutable`/`<-`); no `if shape.IsCircle`; no `.Value` after `.IsSome`; no nested `match` where `bind` was requested |
| **C3: Type signatures preserved** | 0 or 1 | Function signatures exactly match skeleton (parameter names, types, return type annotation) |
| **C4: Code structurally valid F#** | 0 or 1 | No obvious syntax errors visible in transcript; agent's edit_file content parses as F# (can verify by reading transcript) |
| **C5: Task goal met** | 0 or 1 | The implementation does what the task description says (pipeline: filters positives, doubles, sums; DU: all 3 cases handled) |

**Concrete disambiguation examples:**

C1 (pipeline fixture):
- PASS: `nums |> List.filter (fun n -> n > 0) |> List.map (fun n -> n * 2) |> List.sum`
- FAIL: sequential `let pos = List.filter ...` without `|>`; mutable for-loop

C1 (DU fixture):
- PASS: `match shape with | Circle r -> ... | Rectangle (w, h) -> ... | Triangle (b, h) -> ...`
- FAIL: `if shape.IsCircle then ...` or missing cases

C2 (option fixture):
- PASS: `result |> Option.map (sprintf "Result: %d") |> Option.defaultValue "division by zero"`
- FAIL: `if result.IsSome then sprintf ... result.Value else "division by zero"`

C3: If agent changes `(nums: int list) : int` to `nums` without annotation, score 0.

**Aggregate strategy:** Sum across fixtures, divide by fixture_count × 5, map to 0-5 scale.

| Aggregate % | §5 sub-score |
|-------------|-------------|
| 80-100% | 5/5 |
| 60-79% | 4/5 |
| 40-59% | 3/5 |
| 20-39% | 2/5 |
| 1-19% | 1/5 |
| 0% | 0/5 |

**Justification for sum-then-scale over median:** Median of 3 fixtures with scores
[0/5, 4/5, 5/5] = 4/5 (inflated by omitting the failure). Sum = 9/15 = 60% → 4/5 (still
possibly inflated). The sum-then-scale approach is more honest: each criterion counts once.
For 3 fixtures × 5 criteria = 15 total points: ≥12 → 5/5; 9-11 → 4/5; 6-8 → 3/5; 3-5 → 2/5;
1-2 → 1/5; 0 → 0/5.

---

## Q5: `--fs-idiomatic` Mode Shape

### Function structure (mirror of `run_refactor`)

```bash
run_fs_idiomatic() {
  require_port_8001
  # KV-cache pre-flight (v2.3 mandatory lesson — commit 9f8e06e)
  echo "===== fs_idiomatic pre-flight: kickstart 122B to clear KV cache =====" | tee -a "$LOG_DIR/timeline.txt"
  launchctl kickstart -k "gui/$(id -u)/com.ohama.qwen122b"
  echo "  Waiting for port 8001 to recover..."
  local waited=0
  while ! curl -fsS "$ENDPOINT/v1/models" >/dev/null 2>&1; do
    sleep 2
    waited=$((waited + 2))
    if [ "$waited" -ge 120 ]; then
      echo "ERROR: port 8001 did not recover within 120s after kickstart" >&2
      exit 2
    fi
  done
  echo "  port 8001 ready (waited ${waited}s)"
  mkdir -p "$LOG_DIR"

  local fixture_dir="bench/fixtures/fs_idiomatic"
  local fixtures=("pipeline" "dupatternmatch" "optionhandling")
  # Add "resultbind" here if 4th fixture created

  for fixture_name in "${fixtures[@]}"; do
    local task_file="$fixture_dir/${fixture_name}.task.md"
    local fs_file="$fixture_dir/${fixture_name}.fs"
    if [ ! -f "$task_file" ] || [ ! -f "$fs_file" ]; then
      echo "WARN: fixture $fixture_name missing ($task_file or $fs_file)" >&2
      continue
    fi

    local out="$LOG_DIR/fs_idiomatic_${fixture_name}.transcript.txt"
    local meta="$LOG_DIR/fs_idiomatic_${fixture_name}.meta"
    echo "===== fs_idiomatic: $fixture_name (model=122b) =====" | tee -a "$LOG_DIR/timeline.txt"

    local prompt
    prompt=$(cat "$task_file")
    echo "PROMPT: $prompt" >> "$out"
    echo "----" >> "$out"

    local start_ts
    start_ts=$(date +%s)
    set +e
    /usr/bin/time -p dotnet run --project src/BlueCode.Cli -- --verbose --model 122b "$prompt" >> "$out" 2>&1
    local exit_code=$?
    set -e
    local end_ts
    end_ts=$(date +%s)
    local elapsed=$((end_ts - start_ts))

    echo "----" >> "$out"
    echo "===== POST-RUN FILE STATE =====" >> "$out"
    echo "--- $fs_file ---" >> "$out"
    cat "$fs_file" >> "$out"

    # Capture diff vs skeleton canonical state
    local diff_file="$LOG_DIR/fs_idiomatic_${fixture_name}.diff"
    git diff "bench/fixtures/fs_idiomatic/${fixture_name}.fs" > "$diff_file" 2>/dev/null || true

    echo "label=fs_idiomatic_${fixture_name} model=122b exit=$exit_code elapsed=${elapsed}s" > "$meta"
    echo "  -> exit=$exit_code elapsed=${elapsed}s" | tee -a "$LOG_DIR/timeline.txt"

    # Restore fixture to canonical state between runs
    git checkout "bench/fixtures/fs_idiomatic/${fixture_name}.fs" 2>/dev/null || true
    echo "  restored $fs_file to canonical state"
  done

  echo "fs_idiomatic: transcripts at $LOG_DIR/fs_idiomatic_*.transcript.txt"
  echo "fs_idiomatic: diffs at $LOG_DIR/fs_idiomatic_*.diff"
}
```

**Key design decisions:**
- Kickstart happens ONCE at the start of `run_fs_idiomatic`, not between fixtures (one pre-flight
  suffices; between-fixture contamination is bounded by short elapsed time)
- `git checkout <file>` between fixtures restores `.fs` to skeleton state (analogous to `bench/run.sh`'s EXIT trap restoring `refactor_multifile/` files). Use specific file path, not `git checkout .`
- Exit code semantics: function exits 0 if all fixtures ran (even if blueCode exited 1 on MaxLoopsExceeded). Non-zero only on harness error (port not responsive, fixture files missing)
- Output naming: `fs_idiomatic_<name>.transcript.txt` (captures full blueCode output) + `fs_idiomatic_<name>.diff` (git diff of the .fs file)
- `.meta` format: `label= model= exit= elapsed=` (mirrors `refactor_multifile.meta` format exactly)
- `cat "$task_file"` for the prompt (task descriptions are single-paragraph ≤500 chars; safe for CLI argument)

**Usage line in `usage()` to add:**
```bash
  --fs-idiomatic F# idiomatic pattern fixtures (3-5 fixtures; ~5-15 min)
```

---

## Q6: Eval Doc §5 + §7 Edit Sites

**File:** `documentation/qwen35-122b-coding-eval.md` (1052 lines post-v2.3)

### §5 Idiomatic F# section

**§5.1 header:** line 643
```
### §5.1 Idiomatic F# in 3 transcripts
```

**§5.1 score verdict line:** line 686
```
§5.1 verdict: 1 of 3 transcripts contains idiomatic F# (correct for task; Python transcripts lack F# idioms by construction). Score: **1/5**.
```

**§5 total line:** line 766
```
**§5 total: 1 + 3 + 2 = 6/10**
```

**Historical transcripts referenced (lines 644-647):**
```
1. `bench/runs/qwen35-eval-20260428-093852/refactor_multifile_diff.txt` — F# refactor task
2. `bench/runs/qwen35-eval-20260428-100537/multiturn_N5/trial1/transcript.log` — Python parse_csv session
3. `bench/runs/qwen35-eval-20260428-100537/multiturn_N7/trial1/transcript.log` — Python parse_csv session
```
These are the v2.1-era "3 transcripts" scored 1/3 → 1/5. They are preserved as historical
evidence; the new F# fixtures produce v2.4-era evidence for §5.1 replacement/expansion.

### §7 Coding quality row

**§7 Idiomatic F# row:** line 861
```
| Coding quality | Idiomatic F# (1 of 3 transcripts) | 1 | 5 |
```
Update to: `| Coding quality | Idiomatic F# (<score> of 3 F# fixtures) | <new_score> | 5 |`

**§7 Coding quality subtotal:** line 864
```
| **Coding quality subtotal** | | **6** | **10** |
```
Update to `**<new_total>**` where new_total = new_idiomatic_score + 3 + 2.

**§7 grand total line (dimension coverage check):** line 873
```
| Coding quality | 6/10 | 60.0% | YES (exactly at threshold) |
```
Update to `| Coding quality | <new>/10 | <pct>% | YES |`

**§7 applying rules + grand total:** line 881
```
- Grand total: 36 + 25 + 25 + 6 = **92/100** → ≥80 band
```
Update arithmetic and bold value.

**Final scorecard line:** line 1051
```
**Total: 92/100, Recommendation: KEEP**
```
Update to `**Total: <N>/100, Recommendation: KEEP**`.
Note: regex for CI/verification check is `^\*\*Total: \d+/100, Recommendation: KEEP\*\*$`.

### Where to add F# fixture evidence subsection in §5

Insert a new `### §5.1a F# fixture evidence (v2.4 Phase 28)` subsection AFTER the current
§5.1 text (after line 686) and BEFORE the §5.2 header (which starts at line 688). The new
subsection lists run directory, per-fixture score breakdown (C1-C5 rubric), and aggregate.
The existing §5.1 text is updated to reference the new §5.1a results for the final score.

---

## Q7: HARNESS-AUDIT-01 Howto Structure

### Existing howto file conventions

From reading existing files in `documentation/howto/`:
- YAML front-matter header: `---\ncreated: <date>\ndescription: <one-line>\n---`
- English or Korean body — English preferred for new files (prior howtos in Korean reflect
  session preference; since Phase 28 docs are authored in English per ROADMAP conventions,
  the new howto should be English)
- Sections: "The Insight", "Why This Matters", "Recognition Pattern", "The Approach",
  "Example", checklist, related docs
- Code blocks use triple-backtick with language annotation

### Proposed structure for `documentation/howto/macos-bash-strict-mode-patterns.md`

```markdown
---
created: 2026-04-29
description: Four macOS bash strict-mode patterns that silently corrupt eval harness output — symptoms, root causes, canonical fixes, commit refs
---

# macOS Bash Strict-Mode Patterns in Eval Harnesses

Patterns discovered across v2.1–v2.3 eval harness development under `set -euo pipefail`.

## Pattern 1: set-e / dotnet-exit (21-04)

**Symptom:** `bench/eval-qwen35-122b.sh` aborts immediately after `dotnet run` with no output,
even when blueCode completed its task (MaxLoopsExceeded exits 1).

**Root cause:** `set -e` treats any non-zero exit as a fatal error. `dotnet run` exit code 1
(MaxLoopsExceeded) is data, not a harness failure.

**Canonical fix:**
```bash
set +e
dotnet run --project src/BlueCode.Cli -- ...
local exit_code=$?
set -e
```

**Commit:** v2.1 21-04 (present in `run_refactor`, `run_langcoverage`, `run_multiturn`,
`run_schema_rate`); line 274 in `bench/eval-qwen35-122b.sh`.

---

## Pattern 2: grep-c / pipefail double-output (21-04)

**Symptom:** `grep -c 'pattern' file1 file2 | awk '{sum+=$2}'` produces wrong count or aborts.
When `grep -c` finds zero matches, it exits 1 under `pipefail`. The awk receives no input.

**Root cause:** `grep -c` emits per-file counts (`file:N`), exits 1 if no file has a match.
Under `set -euo pipefail`, the pipeline aborts before awk can sum.

**Canonical fix:**
```bash
count=$( (grep -cE 'pattern' file1 file2 2>/dev/null || true) | awk -F: '{sum+=$2} END {print sum+0}')
```
`|| true` inside the subshell prevents the abort; awk still receives the per-file lines.

**Commit:** v2.1 21-04; lines 295-300 in `bench/eval-qwen35-122b.sh`.

---

## Pattern 3: mkdir-before-tee (23-01)

**Symptom:** Script aborts before kickstart fires; `tee: /path/to/LOG_DIR/timeline.txt:
No such file or directory`.

**Root cause:** `tee -a "$LOG_DIR/timeline.txt"` is called before `mkdir -p "$LOG_DIR"`. Under
`pipefail`, `tee`'s non-zero exit propagates and aborts the script.

**Canonical fix:** Every function must call `mkdir -p "$LOG_DIR"` as its first substantive
statement, before any `tee` or append-redirect targeting that directory.

**Commit:** `4bcd8a4` (`fix(23-01): move mkdir before tee in run_coldstart`); line 467 in
`bench/eval-qwen35-122b.sh`.

---

## Pattern 4: grep-cE zero-match exit-1 (27-02)

**Symptom:** The PASS branch (zero orphan references remaining) silently aborts when grep finds
no matches, so `refactor_orphan_count.txt` is never written, and 21-05 scoring reads a stale
value from a previous run.

**Root cause:** `grep -cE 'pattern' files` exits 1 when no files have a match. Under
`set -euo pipefail`, even a single-file `grep -c` in a command substitution aborts the script
when count = 0 (the success case for the rubric).

**Canonical fix:**
```bash
block_invalid=$(grep -c "pattern" "$file" 2>/dev/null || true)
```
Apply `|| true` to ALL `grep -c`/`grep -cE` calls in command substitutions, not just those
expected to fail.

**Commit:** `9f8e06e` (v2.3 27-02); lines 402-404 in `bench/eval-qwen35-122b.sh`.

---

## Pattern 5 (Optional): BSD seq countdown (21-04)

**Symptom:** Multi-turn loop adds extra spurious turns when N=1 (BSD `seq 2 1` emits "2 1",
not empty).

**Root cause:** macOS ships BSD `seq`, not GNU `seq`. `seq M N` where M > N counts DOWN on BSD,
unlike GNU which emits nothing.

**Canonical fix:**
```bash
for k in $([ "$n" -ge 2 ] && seq 2 "$n" || true); do
```

**Commit:** v2.1 21-04; line 390 in `bench/eval-qwen35-122b.sh`.

---

## Common Rule

Under `set -euo pipefail` on macOS:
1. Wrap all `dotnet run` invocations with `set +e` / `set -e`
2. Append `|| true` to all `grep -c` / `grep -cE` in command substitutions
3. Call `mkdir -p "$LOG_DIR"` before the first `tee` in each function
4. Guard `seq M N` with an explicit `[ "$M" -le "$N" ]` check

---

## Cross-References
- `bench/eval-qwen35-122b.sh` — all 4 patterns live in this file
- Plan summaries: v2.1 21-04-SUMMARY.md (P1+P2+P5); v2.2 23-01-SUMMARY.md (P3); v2.3 27-02-SUMMARY.md (P4)
- `documentation/bench.md` — harness usage overview
```

---

## Q8: Plan Ordering Decision

### Confirmed ordering from ROADMAP

```
28-01: HARNESS-AUDIT-01 (howto file)
28-02: F# fixture design
28-03: --fs-idiomatic mode added
28-04: Run fixtures + score
28-05: Eval doc §5 + §7 update
28-06: Bench gate 7/7 + decision point
```

### Parallelization analysis

**28-01 and 28-02 can parallelize.** They touch entirely different files:
- 28-01: creates `documentation/howto/macos-bash-strict-mode-patterns.md`; audits the script
- 28-02: creates `bench/fixtures/fs_idiomatic/*.{fs,task.md}`

No file overlap. An executor running both in the same wave (wave 1) is safe.

**28-03 after 28-02 is correct.** The mode handler is designed to iterate over the actual fixture
names. Stubbing with placeholder names and then updating when real fixtures arrive creates a
two-commit round-trip. Fixtures-first gives the mode handler the exact `fixtures=()` array.

**Do NOT merge 28-04 and 28-05.** 28-04 produces observational output (transcripts + scores)
that requires human-in-the-loop review. 28-05 consumes those scores and edits the eval doc.
Keeping them separate preserves the review step as an explicit gate.

**28-06 is terminal.** Bench gate + decision-point write. Should be its own plan to preserve the
atomic commit convention (`docs(28-06): phase complete + decision point`).

### Recommended structure

```
Wave 1 (parallel): 28-01 + 28-02
Wave 2 (sequential): 28-03 → 28-04 → 28-05 → 28-06
```

If executor runs waves sequentially by default, the PLAN.md for 28-01 and 28-02 should note
they are independent and can be created in either order. The planner can sequence them
28-01 then 28-02 (no harm) or truly parallel.

---

## Q9: Decision-Point Logic at End of Phase 28

### Proposed capture mechanism

**28-VERIFICATION.md** status field (one of three values):
```
status: passed_disprove_1of5     # §5 score ≥3/5 → skip Phase 29
status: passed_confirm_1of5      # §5 score ≤2/5 → trigger Phase 29
status: passed_inconclusive      # rubric ambiguous → re-design rubric before deciding
```

**28-SUMMARY.md** must have an explicit "Phase 29 Trigger Decision" section:
```markdown
## Phase 29 Trigger Decision

§5 Idiomatic F# score from F# fixtures: <X>/5

**Decision:** [TRIGGER Phase 29 | SKIP Phase 29 | INCONCLUSIVE — see open questions]

Rationale: <1-3 sentences>
```

**User-facing offer** (in phase complete report or SUMMARY.md conclusion):
```
Phase 28 measurement: §5 score X/5 on F# fixtures.
- If X ≤ 2: Phase 29 recommended (trigger via `/gsd:add-phase 29`)
- If X ≥ 3: Phase 29 not needed; proceed to Phase 30
```

**Automation boundary:** The decision is OBSERVATIONAL — the user reads the SUMMARY.md and
decides whether to run `/gsd:add-phase 29`. No automated triggering. This mirrors v2.3's
data-driven discipline where the human reviewed Phase 26's diagnostic output before deciding
to proceed with Phase 27.

**Inconclusive threshold:** If fixtures produce mixed signals (one fixture shows idiomatic use,
another does not, and the rubric criteria are genuinely ambiguous), record `passed_inconclusive`
and suggest re-running with a single clearest fixture rather than triggering Phase 29.

---

## Q10: Rubric Reproducibility

### Reproducibility mechanisms

**Binary criteria only (no 0-3 scales):** Each of C1-C5 is binary. Two reviewers reading the
same transcript either both see `|>` or they don't. Avoids "how much is idiomatic enough?"
debates.

**Concrete code examples in 28-04 SUMMARY.md per fixture:**

For C1 (pipeline fixture) — example from PASS response:
```fsharp
let processNumbers (nums: int list) : int =
    nums
    |> List.filter (fun n -> n > 0)
    |> List.map (fun n -> n * 2)
    |> List.sum
```
FAIL example:
```fsharp
let processNumbers (nums: int list) : int =
    let mutable acc = 0
    for n in nums do
        if n > 0 then acc <- acc + (n * 2)
    acc
```

For C2 (DU fixture) — PASS:
```fsharp
match shape with
| Circle r -> sprintf "Circle radius %.1f" r
| Rectangle (w, h) -> sprintf "Rectangle %.1f x %.1f" w h
| Triangle (b, h) -> sprintf "Triangle base %.1f height %.1f" b h
```
FAIL (anti-pattern):
```fsharp
if shape.IsCircle then ...
```

**Per-fixture rubric table in 28-04 SUMMARY.md:**
```
| Fixture | C1 | C2 | C3 | C4 | C5 | Total |
|---------|----|----|----|----|----|----|
| pipeline | 0/1 | 1/1 | 1/1 | 1/1 | 1/1 | 4/5 |
| dupatternmatch | ... | | | | | |
| optionhandling | ... | | | | | |
| Grand total | | | | | | X/15 |
```

**Preservation:** Actual transcript excerpts quoted verbatim in 28-04 SUMMARY.md with
`bench/runs/qwen35-eval-<ts>/fs_idiomatic_<fixture>.transcript.txt` artifact references.
Future re-reviewers can re-read the same transcripts.

---

## Q11: Wall-Clock Estimate

| Plan | Task | Estimate |
|------|------|----------|
| 28-01 | Write howto (4 patterns + script audit) | 30-45 min |
| 28-02 | Create 3-4 fixtures with standalone compile verify | 45-60 min |
| 28-03 | Add `--fs-idiomatic` mode handler to script | 30-45 min |
| 28-04 | Kickstart + run 3-4 fixtures + manual transcript review | 20-40 min runtime + 30-45 min review |
| 28-05 | Edit doc §5 + §7 at identified line numbers | 20-30 min |
| 28-06 | Gate run (~2 min) + SUMMARY.md decision write | 15-20 min |
| **Total** | | **~3-4.5 hr wall-clock** |

28-04 is the longest task because it has two sub-steps: harness execution (10-20 min for
3-4 fixtures × ~3-5 min each including kickstart wait) plus human transcript review. The
review step is the bottleneck.

---

## Q12: Non-Obvious Gotchas

### Gotcha 1: Agent-loop step budget pressure on fixture design

The 10-step PLAN-04 ceiling is fixed. F# fixtures should be 2-step completable:
1. Read `.task.md` (or `.fs` if agent is curious; optional)
2. Edit `.fs` with implementation
3. Final

A fixture that requires reading 3 files before editing will consume 4 steps for setup, leaving
only 6 for execution. Keep fixtures self-contained: `.task.md` contains the full specification
AND `.fs` has the skeleton. Agent should not need to read any other file.

**Test:** Each task.md must stand alone — no cross-file references, no "see also X".

### Gotcha 2: `cat "$task_file"` prompt size

blueCode receives the task description as a CLI argument via shell. Long prompts (>500 chars)
passed via `"$prompt"` can cause shell argument length issues on some environments. The ≤500
char constraint from ROADMAP success criteria is therefore both a rubric concern AND a harness
concern. Verify each `.task.md` is ≤500 chars before adding to fixtures array.

### Gotcha 3: Eval doc §5 v2.1 "1 of 3 transcripts" — what happens to historical evidence

The historical §5.1 text refers to three specific v2.1 transcript paths. After Phase 28, §5.1
is NOT deleted — it is preserved as historical evidence of the v2.1 measurement. A new subsection
`§5.1a F# fixture evidence (v2.4)` is added with the new score. The §5.1 score line changes
from `Score: **1/5**` to reference the new score. The final scorecard in §7 uses the v2.4 score.
This preserves audit trail while updating the verdict.

### Gotcha 4: `git checkout` between fixtures must target specific files

`git checkout bench/fixtures/fs_idiomatic/pipeline.fs` (specific file) vs `git checkout .`
(entire working tree). The harness must restore ONLY the fixture file, not any in-progress edits
to other files. Use specific file paths.

### Gotcha 5: Kickstart disrupts any active blueCode session

The mandatory kickstart pre-flight kills the 122B service for ~37s. If the user has an active
blueCode session in another terminal, it will fail during this window. Document in the function's
echo output: `"===== fs_idiomatic pre-flight: kickstart 122B (disrupts active sessions) ====="`.

### Gotcha 6: HTTP-only invariant check

After Phase 28, verify:
```bash
grep -E "import mlx_lm" bench/eval-qwen35-122b.sh bench/eval-humaneval-http.py bench/eval-needle.py
```
Must return empty. The new `run_fs_idiomatic` function uses HTTP only (curl + blueCode HTTP
client); no Python imports added.

### Gotcha 7: `.fs` skeletons with `failwith` — idiomatic hole markers

`failwith "TODO"` is the F# idiomatic way to express "not yet implemented." It is:
- Type-safe (type `'a`, satisfies any return type)
- Semantically clear
- Unambiguous to the LLM (it knows `failwith` is a placeholder)

Do NOT use `///TODO: implement` comments as the sole hole marker; those don't force a type-safe
placeholder. Do NOT leave the function body completely empty (F# requires a body).

### Gotcha 8: `bench/run.sh --gate` EXIT trap scope

`bench/run.sh`'s EXIT trap restores `bench/fixtures/refactor_multifile/` files. It does NOT
restore `bench/fixtures/fs_idiomatic/` files. The `run_fs_idiomatic` function must do its own
restoration via `git checkout` after each fixture. If the harness crashes mid-fixture, the `.fs`
file may be left in the agent-edited state. Add a note in the plan verification step: after any
`--fs-idiomatic` run, check `git status bench/fixtures/fs_idiomatic/` is clean.

---

## Architecture Patterns

### Adding a new eval mode to `bench/eval-qwen35-122b.sh`

1. Write `run_<modename>()` function (Pattern: mirror `run_refactor`, lines 259-314)
2. Add case branch to dispatcher (lines 556-570): `--<modename>) run_<modename> ;;`
3. Add usage line in `usage()` (lines 537-551)
4. Optionally add call in `run_full()` (line 500-531) if mode should be part of `--full`

### Fixture canonical state and restoration

`bench/run.sh`'s EXIT trap pattern (for reference, NOT modified in Phase 28):
```bash
trap 'git checkout bench/fixtures/refactor_multifile/Calculator.fs bench/fixtures/refactor_multifile/Main.fs bench/fixtures/refactor_multifile/Tests.fs 2>/dev/null || true' EXIT
```

The `run_fs_idiomatic` function performs in-loop restoration (after each fixture) rather than
relying on an EXIT trap. This is simpler and ensures restoration even if the loop continues to
the next fixture.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| Bash-strict-mode guard for grep-c | Custom count wrapper | `|| true` suffix on grep -c in command substitution |
| Hole markers in F# | `#TODO` comment | `failwith "TODO"` (type-safe, standard) |
| F# type checking in bash | Custom fsi wrapper | `dotnet fsi <file>` directly |
| Scoring automation | Parse transcript with sed/awk | Manual review against binary rubric checklist |

**Key insight:** Scoring F# idiom quality is inherently qualitative. Don't build a scoring
script — the rubric checkboxes are the automation. Any script that tries to detect `|>` usage
is insufficient (the agent might use it but in a non-idiomatic context).

---

## Common Pitfalls

### Pitfall 1: Fixture too complex → step budget pressure corrupts measurement
**What goes wrong:** Agent uses 8+ steps just reading/navigating before editing. Transcript
shows step-budget-influenced behavior (rushing to emit `final:` without completing the task).
**Why it happens:** Task description references other files; skeleton has external dependencies.
**How to avoid:** Each fixture is self-contained; `.task.md` has everything; `.fs` skeleton is
standalone-compilable.

### Pitfall 2: KV cache contamination between fixtures
**What goes wrong:** Second or third fixture transcript shows the agent "remembering" the first
fixture's code and confusing it with the current task.
**Why it happens:** mlx_lm.server accumulates KV cache across requests from the same session.
**How to avoid:** One kickstart at the start of `run_fs_idiomatic` clears the slate.
Between-fixture contamination is bounded because each fixture invokes a fresh blueCode process
(new session, no `--resume`).

### Pitfall 3: `git checkout <file>` fails silently if file path wrong
**What goes wrong:** The `.fs` file remains in the agent-edited state for the next fixture;
measurement is contaminated.
**Why it happens:** `git checkout` with a wrong path returns non-zero but `|| true` suppresses it.
**How to avoid:** After `git checkout`, verify with `git status bench/fixtures/fs_idiomatic/`;
the PLAN should include a verification step.

### Pitfall 4: Eval doc final scorecard line format violation
**What goes wrong:** `bench/run.sh` or verification scripts grep for the exact line
`**Total: 92/100, Recommendation: KEEP**`; if the edit changes spacing or adds newlines,
the regex match fails.
**How to avoid:** Edit only the integer value; preserve all surrounding markdown exactly.
The regex is `^\*\*Total: \d+/100, Recommendation: KEEP\*\*$` (from Phase 30 success criteria).

---

## State of the Art

| Old Approach | Current Approach | Changed | Impact |
|--------------|-----------------|---------|--------|
| "1 of 3 transcripts" score (v2.1) | F# fixtures with rubric | Phase 28 | Measures actual F# generation, not proxy via Python transcripts |
| Inline score judgment (qualitative) | Binary rubric checklist (C1-C5) | Phase 28 | Two reviewers reach same score |
| No howto for bash-strict-mode | 4-pattern reference doc | Phase 28 | Future harness authors avoid the same traps |

**Deprecated/outdated:**
- "1 of 3 transcripts" §5.1 evidence: superseded by F# fixture evidence from Phase 28.
  Historical evidence preserved in §5.1 body; new score in §5.1a.

---

## Open Questions

1. **Should `run_fs_idiomatic` be added to `run_full`?**
   - What we know: `run_full` calls all modes sequentially (~2hr total)
   - What's unclear: F# fixture runs take ~15-30 min including kickstart; adding to `--full` makes the full eval ~2.5hr
   - Recommendation: Add it to `run_full` for completeness; document the time addition in `usage()`

2. **4th fixture (`resultbind`) — include or not?**
   - What we know: 3 fixtures is the minimum (ROADMAP success criterion: ≥3); `Result.bind` is a high-value pattern
   - What's unclear: Adding a 4th fixture increases 28-04 run time by ~5 min and review time by ~15 min
   - Recommendation: Design all 4 skeletons in 28-02; decide during 28-03 whether to include all 4 in the harness. Start with 3 for the first run; add 4th if time permits.

3. **`cat "$task_file"` vs hardcoded prompt string in harness**
   - What we know: `run_refactor` uses a hardcoded prompt string; `run_langcoverage` uses a per-fixture string built in-loop
   - What's unclear: Whether `cat "$task_file"` introduces quoting issues when the content has special chars
   - Recommendation: Use `cat "$task_file"` but wrap in `"$(cat ...)"` double-quotes. Test with a fixture that contains parentheses and backticks.

---

## Sources

### Primary (HIGH confidence)
- `bench/eval-qwen35-122b.sh` (read in full, 571 lines) — all line number citations are verbatim
- `documentation/qwen35-122b-coding-eval.md` (read in full, 1052 lines) — all §5/§7 line numbers verified
- `bench/fixtures/refactor_multifile/README.md` (read in full) — format conventions
- `bench/fixtures/refactor_multifile/Calculator.fs` (read in full) — style conventions
- `.planning/ROADMAP.md` (read in full) — success criteria, out-of-scope guardrails, plan list
- `.planning/STATE.md` (read head) — accumulated decisions, bash-strict-mode pattern history
- `documentation/howto/` (read 5 files) — howto format conventions

### Secondary (MEDIUM confidence)
- F# idiom patterns (pipeline, DU match, Option.map, Result.bind) — from F# language knowledge; standard patterns well-established; no version drift risk for F# 6+

### Tertiary (LOW confidence)
- Wall-clock estimates (Q11) — based on observed timings from eval transcripts in eval doc; ±50% variance expected depending on 122B KV state at run time

---

## Metadata

**Confidence breakdown:**
- Harness structure (Q1): HIGH — file read verbatim; line numbers cited directly
- Fixture design (Q3): HIGH — informed by existing fixture conventions + F# idiom knowledge
- Rubric design (Q4): MEDIUM — reproducibility depends on careful binary criterion phrasing; one reviewer trial recommended before committing
- Edit sites (Q6): HIGH — line numbers read directly from eval doc
- Howto structure (Q7): HIGH — follows established conventions from existing howto files
- Plan ordering (Q8): HIGH — parallelization analysis based on file-level independence

**Research date:** 2026-04-29
**Valid until:** Stable (eval doc and harness are the source of truth; no external dependencies; line numbers valid until next eval doc edit)
