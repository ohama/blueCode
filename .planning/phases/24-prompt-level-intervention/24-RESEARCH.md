# Phase 24: Prompt-Level Intervention (P1+P2) - Research

**Researched:** 2026-04-28
**Domain:** F# string literals in CompositionRoot.fs; plan-mode system prompt; LLM few-shot prompting
**Confidence:** HIGH

## Summary

Phase 24 targets `src/BlueCode.Cli/CompositionRoot.fs` exclusively. Two constants define
the system prompt: `defaultSystemPrompt` (783 chars) used for every invocation, and
`planSystemPromptSuffix` (695 chars) appended only in plan-mode (`--plan` flag). The phase
adds two pieces of text to `planSystemPromptSuffix`:

- **P1:** A single-sentence enumeration directive ("list ALL targets explicitly...") — 182 chars
- **P2:** One compact few-shot example for the shared-prefix `add`/`add3` rename — ~302 chars

Combined suffix after both additions: ~1183 chars (well under the 1500-char hard cap and
under the 1100-char COMP-01 intermediate gate). All seven bench gate fixtures invoke
blueCode without `--plan`, so they receive only `defaultSystemPrompt` and are fully
isolated from the suffix change. Regression risk is effectively zero for the gate unless
the change accidentally modifies `defaultSystemPrompt`.

**Primary recommendation:** Put BOTH P1 and P2 inside `planSystemPromptSuffix` only.
Do not touch `defaultSystemPrompt`. Append P1 and P2 at the end of the existing suffix
string with a blank line separator. Use two sequential plans: 24-01 (P1 directive) then
24-02 (P2 few-shot example), each followed by a bench gate run.

---

## Q1: Current State of CompositionRoot.fs

**File:** `src/BlueCode.Cli/CompositionRoot.fs`

### `defaultSystemPrompt` — lines 68–83

```fsharp
let private defaultSystemPrompt: string =
    """You are blueCode, a coding agent driven by an F# recursive loop.

Respond with strict JSON only: {"thought": "<reasoning>", "action": "...", "input": {...}}

Inputs by action:
- read_file:   {path, start_line?, end_line?}
- write_file:  {path, content}
- list_dir:    {path, depth?}
- run_shell:   {command, timeout_ms?}
- edit_file:   {path, old_string(non-empty exact file content), new_string}
- glob_search: {pattern, path?}
- grep_search: {pattern, path?, file_glob?}
- final:       {"answer": "<text>"}

Rules: One tool per response. Use grep_search to locate symbols before reading large files.
When done, respond with action="final". No prose, no markdown — JSON object only."""
```

**Measured length: 783 chars** (verified by Python `len()` on exact source text).

### `planSystemPromptSuffix` — lines 93–98

```fsharp
let planSystemPromptSuffix: string =
    """OVERRIDE — PLAN MODE ACTIVE. Do NOT use read_file/write_file/...
Your ONLY valid response is action="plan". Respond with EXACTLY this JSON shape:
{"thought": "<reasoning>", "action": "plan", "input": {"steps": [...], "rationale": "..."}}
where each "tool" is one of: read_file|write_file|...
Constraints: 1-10 steps. Use the minimum steps needed; ... No two adjacent steps may be
identical. Do NOT execute — user will approve first."""
```

**Measured length: 695 chars** — this is the v2.2 baseline confirmed by the research.

### Key observations

- `defaultSystemPrompt` is declared `private`; `planSystemPromptSuffix` is public (passed
  from `Program.fs` to `runPlanTurn`).
- Both constants use F# triple-quoted strings (`"""`...`"""`). Double-quotes inside these
  strings are **literal** (no backslash escaping needed). Only three consecutive `"` would
  terminate the string, which the content avoids.
- The `bootstrap` function (`CompositionRoot.fs:104`) wires `defaultSystemPrompt` into
  `AgentConfig.SystemPrompt`. The suffix is passed separately from `Program.fs`.

---

## Q2: Plan-Mode Invocation Seam

Consumption path for `planSystemPromptSuffix`:

```
Program.fs:191
  CompositionRoot.planSystemPromptSuffix
    ↓ passed as 6th arg to
AgentLoop.runPlanTurn (AgentLoop.fs:467)
  combinedSystemPrompt = config.SystemPrompt + "\n\n" + systemPromptSuffix
    ↓ passed to
buildMessages combinedSystemPrompt userInput priorSteps None None
    ↓ → List<Message> with Role=System for the combined prompt
client.CompleteAsync messages model ct
```

**Key seam facts:**
- `runPlanTurn` (`AgentLoop.fs:477`): `let combinedSystemPrompt = config.SystemPrompt + "\n\n" + systemPromptSuffix`
- The suffix is concatenated with a `"\n\n"` separator, not in-source. The suffix string
  itself does not need leading or trailing newlines.
- `planSystemPromptSuffix` is only ever passed in plan-mode (the `if isPlanMode then` branch
  at `Program.fs:162`). The agent-loop path (`runSingleTurn`) never receives the suffix.
- Plan-mode is a separate code path: `runPlanTurn` → plan JSON → user approval →
  `runSingleTurn` for execution. The suffix influences only the planning step, not execution.

**Implication:** Changes to `planSystemPromptSuffix` cannot affect agent-loop bench fixtures
because those fixtures do not set `--plan`.

---

## Q3: Few-Shot Example Format

No existing few-shot examples appear anywhere in the codebase. The format must be invented
for Phase 24. Based on the char budget and the model's expected parsing:

### Recommended compact format

Use a prose header line + `Targets:` line + `Steps:` line — three lines, no JSON:

```
Example: rename add->sum AND add3->sum3 across Calculator.fs/Main.fs/Tests.fs
Targets: [add->sum (Calculator.fs def+body, Main.fs, Tests.fs); add3->sum3 (Calculator.fs def, Main.fs, Tests.fs)]
Steps: grep_search(add), grep_search(add3), edit_file(Calculator.fs), edit_file(Main.fs), edit_file(Tests.fs)
```

**Char count for this example: 302 chars**

### Why this format

- `grep -F "Example:" src/BlueCode.Cli/CompositionRoot.fs` must match (COMP-02 validation).
  The word `Example:` at line-start satisfies this.
- The `Targets:` list explicitly names BOTH shared-prefix functions (`add` and `add3`) with
  distinct scopes. This directly combats the extraction bias root cause.
- Three-line prose is readable by the model without JSON parsing; fits the compact budget.
- Avoids nested quotes or JSON to keep within the F# triple-quoted string safely.

### Full proposed P1+P2 additions (to append to existing suffix)

```
\n\nWhen the task requires renaming or restructuring multiple symbols, list ALL targets explicitly in your thought before editing. Do not start editing until the full list is enumerated.\n\nExample: rename add->sum AND add3->sum3 across Calculator.fs/Main.fs/Tests.fs\nTargets: [add->sum (Calculator.fs def+body, Main.fs, Tests.fs); add3->sum3 (Calculator.fs def, Main.fs, Tests.fs)]\nSteps: grep_search(add), grep_search(add3), edit_file(Calculator.fs), edit_file(Main.fs), edit_file(Tests.fs)
```

In F# triple-quoted source this looks like blank-line-separated paragraphs within the same
`"""..."""` block. No escaping needed.

---

## Q4: Bench Fixtures — Plan Mode Usage and Step Counts

From `bench/run.sh` and `bench/baseline.json`:

| Fixture | Prompt (summary) | Plan mode? | Baseline steps | step_count_max | Behavioral note |
|---------|-------------------|------------|---------------|----------------|-----------------|
| **T6_122b** | "What are the field names in the Step record in Domain.fs?" | No | 4 | 5 | grep_search x2 + read_file + final |
| **W1_122b** | "Read bug_lastchar.fs and fix the bug. Save using write_file." | No | 3 | 3 | read + write + final; loop-injection enforces 3 |
| **W2_122b** | "Read bug_average.fs and add averageSafe. Save the updated file." | No | 3 | 3 | same loop-injection pattern |
| **T1_122b** | "What is 2 to the power of 10?" | No | 1 | 3 | direct FinalAnswer, no tools |
| **T5_122b** | "Find BlueCode.slnx and tell me its size in bytes using wc." | No | 3 | 4 | glob_search + run_shell + final |
| **B2_122b** | "Read bug_divide_zero.fs and identify the bug." | No | 2 | 3 | read_file + final |
| **MT_122b** | Multi-turn: list files turn1; follow-up turn2 | No | 2 | 4 | turn-1 step count gated; `--resume` for turn 2 |

**Critical finding: ZERO bench gate fixtures use `--plan` flag.**

The `run()` helper in `bench/run.sh:30–46` invokes:
```bash
dotnet run --project src/BlueCode.Cli -- --verbose --model "$model" "$prompt"
```
No `--plan` flag. The `mt()` helper (`bench/run.sh:111`) similarly omits `--plan`.

This means `planSystemPromptSuffix` changes are **invisible** to every gate fixture. The
seven fixtures only ever see `defaultSystemPrompt` (783 chars, unchanged by Phase 24).

---

## Q5: Risk — Prompt Expansion Increasing T6/MT Step Counts

**Risk level: NONE for planSystemPromptSuffix changes.**

Since no gate fixture uses `--plan`, expanding `planSystemPromptSuffix` by 488 chars
(695 → 1183) has zero behavioral effect on any gated invocation.

The only risk to gate step counts would be:
1. Accidentally modifying `defaultSystemPrompt` (the `private` base prompt)
2. Accidentally changing `AgentConfig.MaxLoops`, `ContextCapacity`, or similar values in
   `bootstrap`

Both are avoidable by restricting edits to the `planSystemPromptSuffix` string literal only.

**If P1 directive were placed in `defaultSystemPrompt` instead:** the directive is
conditional ("When the task requires renaming or restructuring..."). T6 asks about field
names (no renaming); W1/W2 are bug-fix/write tasks (no renaming); T1/T5/B2/MT have no
renaming. The conditional phrasing should be safe. However, the ROADMAP explicitly says
"DO NOT modify `defaultSystemPrompt` unless P1 directive specifically belongs there" and
the phase invariants list "Core purity" plus "no modifications outside CompositionRoot.fs"
with an implicit preference for the suffix. The safer, lower-risk choice is the suffix.

---

## Q6: Test Coverage Gaps

**Current state:** No test asserts the literal content of `defaultSystemPrompt` or
`planSystemPromptSuffix`. Tests in `AgentLoopTests.fs:63` and `PlanParseTests.fs:49` use
synthetic short suffixes (`"[PLAN MODE] Emit action=plan."`) that are hardcoded in the
test module, not referenced from `CompositionRoot`.

**Requirements specify grep validation, not unit tests:**
- COMP-01: `grep -F "list ALL targets explicitly" src/BlueCode.Cli/CompositionRoot.fs`
- COMP-02: `grep -F "Example:" src/BlueCode.Cli/CompositionRoot.fs`

These grep commands are run at PLAN.md verification time, not as runtime tests.

**Recommendation:** No new unit test needed for Phase 24. The grep validations in
PLAN.md task verification are sufficient. Adding a unit test that asserts a literal string
in a prompt constant creates brittle coupling between tests and prompt phrasing — every
future prompt iteration (Phase 24-03 if regression hits) would require test updates.

The existing `runPlanTurn` tests (`AgentLoopTests.fs`) use mock suffixes and continue to
pass unchanged. The bench gate (`bash bench/run.sh --gate`) serves as the integration
regression check.

---

## Q7: Plan Decomposition Recommendation

**Recommended: Option A — two sequential plans, with bench gate at end of each.**

Per the ROADMAP (`Plan dependencies: 24-01 → 24-02`), the split is already decided:

| Plan | Scope | COMP requirement | Bench gate |
|------|-------|-----------------|------------|
| **24-01** | P1: Add enumeration directive to `planSystemPromptSuffix` | COMP-01 | Yes, after |
| **24-02** | P2: Add few-shot example(s) to `planSystemPromptSuffix` | COMP-02 | Yes, after |
| **24-03** (optional) | Prompt iteration if regression hits | — | Yes, after |

**Why Option A over alternatives:**

- **Option B (single plan):** Harder to isolate regression source. If T6 regresses (which
  can't happen from suffix changes, but in principle), we can't tell which prong caused it.
  Sequential plans allow attributing any issue to the specific change.
- **Option C (bench gate as separate plan):** Wastes a plan slot. Gate verification is
  a task step within each plan, not its own plan.
- **Option A:** Matches ROADMAP, clean rollback (revert 24-01 if needed before 24-02),
  easy attribution. Each plan is a single-file one-line-block edit = minimal blast radius.

**The "iterate prompt phrasing" workflow** (if regression hits) is served well by Option A:
if 24-01's P1 directive somehow regressed T6 (via a hypothetical future defaultSystemPrompt
placement), we revert to 24-00 state and try alternative phrasing in a new 24-03 commit.
The sequential structure keeps this rollback clean.

---

## Q8: Non-Obvious Gotchas

### F# triple-quoted string behavior

The existing `planSystemPromptSuffix` uses `"""`...`"""` (triple-quoted). Inside this form:
- **Double-quotes are literal** — no backslash escaping needed. The existing suffix has
  `action="plan"` and `{"thought": ...}` with bare double-quotes.
- **Newlines in source are newlines in the string** — indentation MATTERS. The string
  starts at column 4 (the `"""` is at col 4); any leading spaces on continuation lines are
  included in the string value.
- **End-of-string:** The closing `"""` must not be preceded by `""` within the content.
  The few-shot example prose uses no consecutive quotes, so this is safe.
- **No interpolation:** These are plain string literals, not interpolated (`$"..."`) strings.
  Curly braces `{` `}` are literal characters, not interpolation holes.

### Indentation trap

In F# source, a triple-quoted string that spans lines captures indentation from column 0:

```fsharp
// WRONG — extra leading spaces in string value:
let s = """first line
    second line"""   // "second line" has 4 spaces prefix

// CORRECT — continuation lines at column 0:
let s = """first line
second line"""
```

The existing suffix uses correct left-flush continuation. New appended lines must follow
the same convention. Easiest approach: extend the existing `"""..."""` block in place,
adding lines at the same left-flush column.

### Char measurement

Measure with `String.length planSystemPromptSuffix` in a test or just count chars in the
source editor. The COMP-01 validation says "system prompt ≤ 1100 chars" — this refers to
the `planSystemPromptSuffix` after adding P1 only (before P2). After P1 alone: ~879 chars
(passes). After P1+P2: ~1183 chars (passes the 1500-char COMP-02 limit). Both limits are
met comfortably.

### "System prompt ≤ 1100 chars" in COMP-01 — interpretation

The COMP-01 requirement says "system prompt ≤ 1100 chars (vs v2.2's 695-char baseline)".
The 695 baseline is `planSystemPromptSuffix`. So "system prompt" in COMP-01 means
`planSystemPromptSuffix` (not `defaultSystemPrompt`, which is at 783 chars). After adding
P1 only (plan 24-01), the suffix goes from 695 → ~879, well under 1100. This is the
intermediate checkpoint before adding P2 in 24-02.

### bench gate fixture reset

W1 and W2 fixtures modify files on disk (write tasks). The bench `run.sh` includes fixture
restore logic. Phase 24 edits `CompositionRoot.fs` only — no fixture files are touched —
so no restore logic concerns apply.

### No plan-mode bench fixture currently exists

The CORR-EVAL-02 task (multi-file refactor) is NOT in `bench/baseline.json` and NOT run by
`bench/run.sh --gate`. It exists only as `bench/fixtures/refactor_multifile/`. Phase 26
will re-run it for the COMP-05 verdict. For Phase 24, bench gate validation means only the
7 existing gate fixtures (none plan-mode). The COMP-02 requirement's "plan-mode bench
fixture (mocked) plays through new examples without breakage" is satisfied by the
existing mocked `runPlanTurn` tests in `AgentLoopTests.fs` — those tests use a synthetic
short suffix and exercise the parse/validate path without caring about suffix content.

---

## Budget Summary

| Constant | v2.2 baseline | After 24-01 (P1) | After 24-02 (P1+P2) | Hard cap |
|----------|--------------|------------------|----------------------|----------|
| `defaultSystemPrompt` | 783 chars | 783 (unchanged) | 783 (unchanged) | — |
| `planSystemPromptSuffix` | 695 chars | ~879 chars | ~1183 chars | 1500 chars |
| COMP-01 intermediate check | — | 879 ≤ 1100 ✓ | — | 1100 |
| COMP-02 final check | — | — | 1183 ≤ 1500 ✓ | 1500 |

---

## Proposed Suffix Text (Verbatim Reference)

For the planner's use when writing task actions. The final `planSystemPromptSuffix` after
both plans should be the existing 695-char content plus the following appended text (shown
as it would appear in F# source inside the `"""..."""` block, left-flush):

```
[existing 695-char content...]
When the task requires renaming or restructuring multiple symbols, list ALL targets explicitly in your thought before editing. Do not start editing until the full list is enumerated.
Example: rename add->sum AND add3->sum3 across Calculator.fs/Main.fs/Tests.fs
Targets: [add->sum (Calculator.fs def+body, Main.fs, Tests.fs); add3->sum3 (Calculator.fs def, Main.fs, Tests.fs)]
Steps: grep_search(add), grep_search(add3), edit_file(Calculator.fs), edit_file(Main.fs), edit_file(Tests.fs)
```

In actual F# source, the closing `"""` of the existing suffix moves to after the new lines,
with all new lines left-flush (no leading spaces). The P1 sentence appears before `Example:`.

Grep validations that must pass after both plans:
- `grep -F "list ALL targets explicitly" src/BlueCode.Cli/CompositionRoot.fs` — 1 match
- `grep -F "Example:" src/BlueCode.Cli/CompositionRoot.fs` — 1 match

---

## Sources

### Primary (HIGH confidence)
- `src/BlueCode.Cli/CompositionRoot.fs:68–98` — direct source read, char-counted
- `src/BlueCode.Core/AgentLoop.fs:467–477` — runPlanTurn signature and concatenation logic
- `src/BlueCode.Cli/Program.fs:162–192` — plan-mode branch, suffix pass-through
- `bench/run.sh:30–46` — run() helper (no --plan flag confirmed)
- `bench/run.sh:111–157` — mt() helper (no --plan flag confirmed)
- `bench/baseline.json:1–52` — 7-fixture baseline, step counts and maxes
- `.planning/REQUIREMENTS.md` — COMP-01/02/04 full text and validation conditions
- `.planning/ROADMAP.md` — Phase 24 plan split (24-01 → 24-02), architectural invariants

### Secondary (HIGH confidence)
- `tests/BlueCode.Tests/AgentLoopTests.fs:63` — planTurnSuffix mock value confirmed
- `tests/BlueCode.Tests/PlanParseTests.fs:49` — independent mock suffix confirmed
- Python `len()` measurements on exact source strings — char counts verified

## Metadata

**Confidence breakdown:**
- Current state of CompositionRoot.fs: HIGH — direct source read with char measurement
- Plan-mode invocation seam: HIGH — traced through three files
- Few-shot format: MEDIUM — format is novel (no prior art in codebase); char budget math is HIGH
- Bench fixture plan-mode isolation: HIGH — confirmed by grep on run.sh
- Risk assessment: HIGH — zero-risk conclusion is grounded in verified facts
- Plan split recommendation: HIGH — matches ROADMAP decision already documented

**Research date:** 2026-04-28
**Valid until:** Stable for this phase. Suffix content changes if prompt iteration (24-03) occurs.
