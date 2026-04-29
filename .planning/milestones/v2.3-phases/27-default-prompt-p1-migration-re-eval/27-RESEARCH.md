# Phase 27: Default-Prompt P1 Migration + Re-Eval — Research

**Researched:** 2026-04-28
**Domain:** F# string literals in CompositionRoot.fs; system prompt migration; LLM eval re-run
**Confidence:** HIGH — all questions answered by direct source inspection; no external research needed

---

## Summary

Phase 27 closes the architectural gap exposed by Phase 26 BLOCKED: the v2.3 multi-prong
intervention (P1+P2+P3) was scoped to plan-mode only. The eval harness invokes blueCode
without `--plan`, so the agent-loop path never saw P1. The fix is a surgical migration:
move the 182-char P1 enumeration directive from `planSystemPromptSuffix` into
`defaultSystemPrompt`, then re-run CORR-EVAL-02 with mandatory kickstart pre-flight.

The migration involves exactly two `edit_file` operations on one file
(`src/BlueCode.Cli/CompositionRoot.fs`). Total text transferred: 182 chars + a `\n\n`
separator moves from suffix into default prompt. Combined plan-mode char count is invariant
(1968 chars before = 1968 chars after). All 7 bench gate fixtures lack "rename" or
"restructuring" keywords, so P1's conditional clause is dormant for gate fixtures — zero
regression risk from the migration itself. MT fixture contains "Refactor" (pytest context)
but not "rename/restructuring multiple symbols" — P1 conditional does not trigger.

**For eval doc edit sites, harness mechanics, STATE.md update pattern, and stochastic
variance policy:** reference `.planning/phases/26-re-evaluation/26-RESEARCH.md` directly —
those findings are unchanged and HIGH confidence.

**Primary recommendation:** 3-plan structure (27-01 migration + gate; 27-02 re-run with
kickstart; 27-03 eval doc + state + gate + close). Migration in 27-01 is two atomic
edit_file calls. P2 stays in plan-mode only (out-of-scope per ROADMAP guardrails).

---

## Q1: Current `defaultSystemPrompt` — Verbatim

**File:** `src/BlueCode.Cli/CompositionRoot.fs:68-83`

```fsharp
let private defaultSystemPrompt: string =
    """You are blueCode, a coding agent driven by an F# recursive loop.

Respond with strict JSON only: {"thought": "<reasoning>", "action": "<one of: read_file | write_file | list_dir | run_shell | edit_file | glob_search | grep_search | final>", "input": {...}}

Inputs by action:
- read_file:   {path, start_line?, end_line?}
- write_file:  {path, content}
- list_dir:    {path, depth?}
- run_shell:   {command, timeout_ms?}
- edit_file:   {path, old_string(non-empty exact file content), new_string}
- glob_search: {pattern, path?}
- grep_search: {pattern, path?, file_glob?}
- final:       {"answer": "<text>"}

Rules: One tool per response. Use grep_search to locate symbols before reading large files. When done, respond with action="final". No prose, no markdown — JSON object only."""
```

**Measured length: 783 chars** (verified by Python `len()` on extracted inner string).

**Structure (annotated):**
- Line 0: identity/role
- Line 1: blank
- Line 2: JSON schema overview with action enum
- Line 3: blank
- Line 4: "Inputs by action:" header
- Lines 5-12: 8 action input specs (bullet list)
- Line 13: blank
- Line 14: "Rules:" paragraph — single prose block, ends with `JSON object only.`

**`private` declaration:** `let private defaultSystemPrompt` — this binding is accessible
only within `CompositionRoot.fs`. Confirmed via grep: the string is consumed only at
`CompositionRoot.fs:120` (`SystemPrompt = defaultSystemPrompt`) and at `CompositionRoot.fs:85`
(XML doc comment reference). No test fixtures reference the full default prompt — test
modules at `AgentLoopTests.fs:60` and `PlanParseTests.fs:46` use the stub
`SystemPrompt = "You are blueCode."` — unaffected by the migration.

**Natural insertion point:** Append at end of existing "Rules:" paragraph, separated by
`\n\n`. The closing `"""` currently appears immediately after `only.` on line 83. The new
text goes between `only.` and `"""`.

---

## Q2: Current `planSystemPromptSuffix` — Verbatim and P1 Location

**File:** `src/BlueCode.Cli/CompositionRoot.fs:93-104`

```fsharp
let planSystemPromptSuffix: string =
    """OVERRIDE — PLAN MODE ACTIVE. Do NOT use read_file/write_file/list_dir/run_shell/edit_file/glob_search/grep_search/final actions.
Your ONLY valid response is action="plan". Respond with EXACTLY this JSON shape:
{"thought": "<reasoning>", "action": "plan", "input": {"steps": [{"tool": "<tool>", "input": {}, "rationale": "<why>"}], "rationale": "<overall why>"}}
where each "tool" is one of: read_file|write_file|list_dir|run_shell|edit_file|glob_search|grep_search.
Constraints: 1-10 steps. Use the minimum steps needed; reserve the full budget only for tasks requiring reads across multiple files before editing. No two adjacent steps may be identical. Do NOT execute — user will approve first.

When the task requires renaming or restructuring multiple symbols, list ALL targets explicitly in your thought before editing. Do not start editing until the full list is enumerated.

Example: rename add->sum AND add3->sum3 across Calculator.fs/Main.fs/Tests.fs
Targets: [add->sum (Calculator.fs def+body, Main.fs, Tests.fs); add3->sum3 (Calculator.fs def, Main.fs, Tests.fs)]
Steps: grep_search(add), grep_search(add3), edit_file(Calculator.fs), edit_file(Main.fs), edit_file(Tests.fs)"""
```

**Measured length: 1183 chars** (verified by Python `len()`).

**Suffix line structure (0-indexed):**
- Lines 0-4: OVERRIDE block + Constraints paragraph
- Line 5: blank
- Line 6: **P1 directive** — "When the task requires renaming or restructuring multiple symbols, list ALL targets explicitly in your thought before editing. Do not start editing until the full list is enumerated."
- Line 7: blank
- Lines 8-10: P2 few-shot example (Example: / Targets: / Steps:)

**P1 directive verbatim (182 chars):**
```
When the task requires renaming or restructuring multiple symbols, list ALL targets explicitly in your thought before editing. Do not start editing until the full list is enumerated.
```

**P1 position in suffix:** Starts at index 697 of the inner string. Preceded by `\n\n`
(blank line after the Constraints paragraph). Followed by `\n\n` (blank line before P2 Example).

**Removal block for P1** (the exact substring to delete from suffix): `\n\nWhen the task
requires renaming or restructuring multiple symbols, list ALL targets explicitly in your
thought before editing. Do not start editing until the full list is enumerated.`

That is: `\n\n` + P1_TEXT (total 184 chars). Verified unique in file (count = 1).

---

## Q3: Insertion Strategy — Option A Chosen

**Option A:** Append P1 at end of `defaultSystemPrompt`, after "Rules:" paragraph, separated
by blank line (`\n\n`). P1 ends with `.` before closing `"""`.

**Rationale:** P1's conditional phrasing ("When the task requires renaming or restructuring
multiple symbols...") is semantically self-contained. It does not depend on the "Rules:"
paragraph structure; it is a new behavioral rule. Appending at end:
- Requires minimum surgery: only the closing `"""` moves
- Does not disrupt the existing action-spec format or the "Rules:" line
- Keeps the conditional clause naturally trailing: the agent reads the schema, then reads
  the conditional rule as a final modifier

**Option B** (insert after "Rules:" line as inline extension) would split the Rules
paragraph and increase risk of F# indentation errors. Rejected.

**Option C** (insert in action list) is mechanically misaligned — the action list describes
JSON input shapes, not prose behavioral rules. Rejected.

---

## Q4: Character Budget After Migration — Verified Math

**Current state:**
- `defaultSystemPrompt` inner: **783 chars**
- `planSystemPromptSuffix` inner: **1183 chars**
- Combined in plan-mode (`config.SystemPrompt + "\n\n" + systemPromptSuffix`): 783 + 2 + 1183 = **1968 chars**

**P1 directive:** 182 chars

**Separator accounting:**
- In suffix: P1 is preceded by `\n\n` (already exists; part of removal block → 184 chars removed)
- In default: P1 appended with new `\n\n` separator → 2 + 182 = 184 chars added

**After migration:**
- `defaultSystemPrompt`: 783 + 2 + 182 = **967 chars** (verified by Python)
- `planSystemPromptSuffix`: 1183 − 184 = **999 chars** (verified by Python)
- Combined in plan-mode: 967 + 2 + 999 = **1968 chars** (invariant — no change)

**Validation commands:**

```bash
# Verify defaultSystemPrompt length after edit
grep -c "" /dev/stdin << 'EOF'
# Use dotnet fsi to measure
EOF
# Or use python3 inline measurement:
python3 -c "
import re, sys
c = open('src/BlueCode.Cli/CompositionRoot.fs').read()
m = re.search(r'let private defaultSystemPrompt.*?\"\"\"(.*?)\"\"\"', c, re.DOTALL)
print('defaultSystemPrompt len:', len(m.group(1)))
m2 = re.search(r'let planSystemPromptSuffix.*?\"\"\"(.*?)\"\"\"', c, re.DOTALL)
print('planSystemPromptSuffix len:', len(m2.group(1)))
"
```

Expected output after migration: `defaultSystemPrompt len: 967` and
`planSystemPromptSuffix len: 999`.

---

## Q5: Bench Gate Fixture Risk Assessment

All 7 gate fixture prompts examined for `rename` or `restructur` (case-insensitive):

| Label | Prompt (abbreviated) | rename? | restructur? | Risk |
|-------|----------------------|---------|-------------|------|
| T6 | "What are the field names in the Step record in Domain.fs?" | NO | NO | NONE |
| W1 | "Read bug_lastchar.fs and fix the bug. Save using write_file." | NO | NO | NONE |
| W2 | "Read bug_average.fs and add averageSafe... Save the updated file." | NO | NO | NONE |
| T1 | "What is 2 to the power of 10? Answer with just the number." | NO | NO | NONE |
| T5 | "Find BlueCode.slnx and tell me its size in bytes using wc." | NO | NO | NONE |
| B2 | "Read bug_divide_zero.fs and identify the bug..." | NO | NO | NONE |
| MT turn1 | "Write a Python function parse_csv..." + "Refactor the test cases to use pytest.mark.parametrize..." | NO | NO | LOW |
| MT followup | "What was the file I just listed? Just give me the file count." | NO | NO | NONE |

**MT fixture note:** Turn 1 of MT contains the word "Refactor" in the context of
`pytest.mark.parametrize`. However, P1's conditional clause is "When the task requires
**renaming or restructuring multiple symbols**" — this is specifically about symbol
rename tasks (functions, variables), not about test refactoring. The word "Refactor" in MT
refers to reorganizing test case structure, not renaming multiple symbols. P1 directive is
behaviorally dormant for MT. Risk: LOW (no functional trigger; conditional semantics clear).

**Conclusion:** Zero rename/restructure keywords across all 7 gate fixture prompts.
P1's conditional clause is dormant for all gate fixtures. Regression risk from migration
is effectively zero for the bench gate (same conclusion as Phase 24-RESEARCH.md Q4 for
the suffix addition; now extended to cover the default prompt addition).

**Highest-risk fixture:** MT (LOW risk, explained above). T6 asks about "field names" —
reading field names ≠ renaming symbols. No trigger.

---

## Q6: Plan Decomposition — 3 Plans (Option Slim)

**Recommendation: Option Slim (3 plans).**

```
27-01: P1 migration + bench gate
  - Edit CompositionRoot.fs (2 edit_file ops)
  - dotnet build (verify compilation)
  - bash bench/run.sh --gate (7/7 PASS required)
  - Commit: feat(27-01): migrate P1 enumeration directive to defaultSystemPrompt

27-02: CORR-EVAL-02 re-run (kickstart pre-flight)
  - launchctl kickstart -k gui/501/com.ohama.qwen122b (mandatory)
  - Wait for model ready
  - warmup probe
  - git checkout -- bench/fixtures/refactor_multifile/
  - bash bench/eval-qwen35-122b.sh --refactor (up to 3 stochastic attempts)
  - Read refactor_orphan_count.txt — must be 0
  - <failure_path> if 3 FAILs: stop, document, do NOT touch eval doc

27-03: Eval doc edits + state + gate + phase-close
  - 11 edit sites in documentation/qwen35-122b-coding-eval.md (see 26-RESEARCH.md Q1)
  - Validate: grep -E "^\*\*Total: 92/100, Recommendation: KEEP\*\*$"
  - Update STATE.md (see 26-RESEARCH.md Q6 for pattern)
  - bash bench/run.sh --gate (final regression check)
  - Phase-complete commit
```

**Rationale for Option Slim over Option Compact:** 27-02 (empirical PASS) and 27-03
(documentation edit) have fundamentally different failure modes. Bundling them (Option
Compact) means a stochastic re-run in 27-02 would leave the eval doc half-edited. Keeping
them separate is cleaner: 27-02 gate-checks the empirical outcome; 27-03 records it.

**Rationale against Option Generous (4 plans):** The eval doc edits + STATE + gate are
all write-and-verify documentation work; splitting further (4 plans) adds overhead with
no benefit.

---

## Q7: Kickstart Pre-Flight — Mandatory Task, Not Footnote

Kickstart must be Task 1 of plan 27-02 (not optional, not a footnote). Phase 26 BLOCKED
confirmed two distinct failure modes: (a) KV cache contamination → hallucination, (b)
post-kickstart → original extraction bias. Kickstart addresses (a); P1 migration addresses
(b). Both are required.

**Exact kickstart sequence:**

```bash
# Step 1: kickstart the 122B service
launchctl kickstart -k gui/501/com.ohama.qwen122b

# Step 2: wait for model to be serving metadata
until curl -fsS http://127.0.0.1:8001/v1/models > /dev/null 2>&1; do sleep 5; done

# Step 3: warmup probe — verify inference is live, not just metadata
curl -s -X POST http://127.0.0.1:8001/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model":"/Users/ohama/llm-system/models/qwen122b","messages":[{"role":"user","content":"ping"}],"max_tokens":5}' \
  | grep -q '"content"' && echo "WARMUP OK" || echo "WARMUP FAILED"
```

**UID 501 confirmed:** `id -u` returns `501` on this machine. `gui/501/com.ohama.qwen122b`
is the correct launchctl target (Phase 26 diagnostic D used this exact command).

**Wall-clock for kickstart sequence:** `launchctl kickstart -k` sends SIGKILL + relaunches;
model weight load takes ~30-45s on cold cache (122B @ ~45 GB RSS). The `until` loop
catches the ready state. Warmup probe adds ~5-10s. Total: ~45-60s.

---

## Q8: Failure Path Mirror for Phase 27

Phase 27 has two distinct failure branches:

**Branch A: Bench gate fails after 27-01 migration (gate regression)**

This is unexpected given zero rename keywords in gate fixtures, but if it happens:
1. Read the failing fixture's log to identify which step count regressed
2. If T6/W1/W2/MT step count changed: P1 conditional triggered unexpectedly — iterate
   phrasing to make conditional more specific (iteration plan 27-01-fix)
3. Allow up to 2 phrasing iterations in 27-01-fix plans
4. If 3 phrasing iterations all regress: revert migration commit; escalate to v2.4
   architectural redesign

**Branch B: CORR-EVAL-02 FAIL in 27-02 (≥3 attempts)**

Same as Phase 26 failure path — per REQUIREMENTS.md COMP-05:
- Stop. Do NOT modify Phase 24/25/27 source.
- Do NOT touch eval doc (`documentation/qwen35-122b-coding-eval.md` stays at 87/100).
- Document in STATE.md: "Phase 27 FAIL — CORR-EVAL-02 still FAIL after 3 attempts post
  P1 migration to defaultSystemPrompt. Extraction bias deeper than prompt layer. v2.4+
  investigation required."
- Update ROADMAP.md §Phase 27 as "FAIL — blocked"
- Commit failure state (27-02-SUMMARY.md with status:blocked; 27-VERIFICATION.md partial)

**Per-attempt diagnostic checklist (same as Phase 26-RESEARCH.md Q8):**
- Did agent thought enumerate both `add` and `add3` targets?
- Was P3 PlanValidator's `checkRenameTargetsEnumerated` triggered? (Check for `[PLAN INVALID]` — but note: 27-02 runs without `--plan`, so P3 does NOT apply; P3 is plan-mode only)
- How many steps? Was it MaxLoopsExceeded or clean exit?
- Which files were edited vs skipped?

---

## Q9: P2 Migration — Leave in Plan-Mode Only

**Decision: P2 stays in `planSystemPromptSuffix` for Phase 27.**

P2 is the few-shot example block (lines 8-10 of suffix):
```
Example: rename add->sum AND add3->sum3 across Calculator.fs/Main.fs/Tests.fs
Targets: [add->sum (Calculator.fs def+body, Main.fs, Tests.fs); add3->sum3 (Calculator.fs def, Main.fs, Tests.fs)]
Steps: grep_search(add), grep_search(add3), edit_file(Calculator.fs), edit_file(Main.fs), edit_file(Tests.fs)
```

The `Steps:` line references plan-mode JSON shape semantics (`Steps: grep_search(...),
edit_file(...)` is a plan-mode step-list notation). In agent-loop mode the agent executes
one tool at a time without pre-planning. Migrating P2 verbatim into `defaultSystemPrompt`
would add plan-mode-specific notation to the agent-loop system prompt, creating semantic
confusion.

If P1 alone is insufficient (Phase 27 FAILs), Phase 28 can add an agent-loop-friendly P2
variant (e.g., dropping the `Steps:` line and rephrasing as "then systematically edit each
file"). Per ROADMAP guardrails: "DO NOT modify P2 few-shot example unless an iteration
phase is explicitly added."

---

## Q10: Wall-Clock Estimate

| Plan | Activity | Estimate |
|------|----------|----------|
| 27-01 | Edit CompositionRoot.fs (2 ops) | ~2 min |
| 27-01 | `dotnet build` | ~15-30s |
| 27-01 | `bench/run.sh --gate` (7 fixtures) | ~2-3 min |
| 27-01 total | | ~5-7 min |
| 27-02 | kickstart + wait + warmup | ~60s |
| 27-02 | `git checkout --` pre-flight | <5s |
| 27-02 | `--refactor` attempt (1) | ~60-90s |
| 27-02 total (1 attempt) | | ~3-4 min |
| 27-02 total (3 attempts) | | ~8-12 min |
| 27-03 | 11 eval doc edit sites | ~10 min |
| 27-03 | STATE.md + ROADMAP + REQUIREMENTS update | ~5 min |
| 27-03 | `bench/run.sh --gate` (final) | ~2-3 min |
| 27-03 | Commits | ~2 min |
| 27-03 total | | ~20 min |
| **Phase 27 total (clean PASS, 1st attempt)** | | **~30-35 min** |
| **Phase 27 total (3 stochastic attempts)** | | **~45-55 min** |

---

## Q11: Non-Obvious Gotchas

### F# triple-quoted string indentation (load-bearing)

Covered fully in `24-RESEARCH.md Q8`. Summary:
- The closing `"""` for `defaultSystemPrompt` is currently at end of line 83 (`only."""`)
- The new P1 text must be left-flush (no leading spaces) when added before the closing `"""`
- The in-source appearance after migration:

```fsharp
Rules: One tool per response. Use grep_search to locate symbols before reading large files. When done, respond with action="final". No prose, no markdown — JSON object only.

When the task requires renaming or restructuring multiple symbols, list ALL targets explicitly in your thought before editing. Do not start editing until the full list is enumerated."""
```

The blank line (`\n\n` separator) and P1 text both start at column 0 (left-flush). The
closing `"""` immediately follows the `.` at end of P1 sentence.

### `private` declaration — no external references (confirmed)

`defaultSystemPrompt` is `private` to `CompositionRoot.fs`. Grep confirms:
- `src/BlueCode.Cli/CompositionRoot.fs:68` — declaration
- `src/BlueCode.Cli/CompositionRoot.fs:120` — sole usage (`SystemPrompt = defaultSystemPrompt`)
- `src/BlueCode.Cli/CompositionRoot.fs:85` — XML doc comment (does not reference content)
- No references in `tests/` — tests use stub `"You are blueCode."` string directly

Migration does NOT require changes to any test file.

### Exact `edit_file` operations (load-bearing — verified unique)

**Operation 1 — extend defaultSystemPrompt:**

```
old_string: "— JSON object only.\"\"\""
new_string: "— JSON object only.\n\nWhen the task requires renaming or restructuring multiple symbols, list ALL targets explicitly in your thought before editing. Do not start editing until the full list is enumerated.\"\"\""
```

Uniqueness: `"— JSON object only.\"\"\""` appears exactly **1 time** in the file.

**Operation 2 — remove P1 from planSystemPromptSuffix:**

```
old_string: "\n\nWhen the task requires renaming or restructuring multiple symbols, list ALL targets explicitly in your thought before editing. Do not start editing until the full list is enumerated.\n\nExample:"
new_string: "\n\nExample:"
```

Uniqueness: the old_string appears exactly **1 time** in the file.

Both operations can be sequential in one task (edit_file twice on same file) within a
single atomic commit.

### KV cache is real — kickstart is non-optional

Phase 26 Diagnostic D confirmed:
- Pre-kickstart attempt: hallucination (agent thought task was "add subtract function";
  no rename attempt; orphan_count=1) — KV cache contamination from prior blueCode session
- Post-kickstart: extraction bias (only `add3→sum3`; ignores `add→sum`) — comprehension
  failure targeted by P1 migration

Do not skip kickstart even if the model "seems fine." The symptom (hallucination) is
silent from outside and only visible in the step trace.

### P3 (PlanValidator) is NOT active for eval harness invocations

The eval harness command is `dotnet run --project src/BlueCode.Cli -- --verbose --model 122b "$prompt"` — no `--plan` flag. P3 (`checkRenameTargetsEnumerated`) only runs inside `runPlanTurn`, which is only called when `PlanMode = true`. Phase 27's fix (P1 in defaultSystemPrompt) is the only prong that reaches the eval harness. This is why Phase 26 BLOCKED despite all 3 prongs being shipped.

### `git diff src/` must be empty after Phase 27 (if Phase 27 FAILs)

If 27-02 produces 3 FAILs, the Phase 27 failure path requires that source code is left
untouched — meaning the 27-01 migration commit is already in git history and STAYS there
(it's the correct architectural fix regardless). What must NOT be done on FAIL: do not
modify Phase 24/25/27 source mid-flight, do not touch eval doc. The existing migration
commit is acceptable to keep.

---

## Architecture Pattern: Two-Op Migration Commit

```
Task 1 (27-01): P1 migration
  Action 1: edit_file CompositionRoot.fs — extend defaultSystemPrompt (op 1)
  Action 2: edit_file CompositionRoot.fs — remove P1 from planSystemPromptSuffix (op 2)
  Action 3: dotnet build (verify no compilation errors)
  Action 4: python3 char-count verification (967 + 999)
  Action 5: bash bench/run.sh --gate (7/7 PASS)
  Commit: feat(27-01): migrate P1 enumeration directive to defaultSystemPrompt
    - defaultSystemPrompt 783→967 chars
    - planSystemPromptSuffix 1183→999 chars
    - plan-mode combined invariant: 1968 chars unchanged
```

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| Orphan count check | Custom grep | Existing harness `bash bench/eval-qwen35-122b.sh --refactor` |
| Fixture restoration | Manual cp | `git checkout -- bench/fixtures/refactor_multifile/` (pre-flight) + bench gate EXIT trap (post-gate) |
| Char measurement | Count manually | `python3 -c "import re; ..."` pattern from Q4 above |
| Scorecard line validation | Visual check | `grep -E "^\*\*Total: 92/100, Recommendation: KEEP\*\*$"` |

---

## Common Pitfalls

### Pitfall 1: F# indentation on appended P1 text

The new P1 lines inside `defaultSystemPrompt` must be left-flush (column 0). Any leading
spaces would be included in the string value. Use the exact `edit_file` operation from Q11.
The closing `"""` attaches immediately after the final `.` of P1 with no trailing newline.

### Pitfall 2: Skipping kickstart pre-flight

KV cache contamination from prior sessions produces hallucination (Phase 26 Diagnostic D).
Kickstart is mandatory Task 1 of 27-02, not optional. Wait for the `until curl` loop to
exit AND run the warmup probe before proceeding to `--refactor`.

### Pitfall 3: Reading orphan count from re-grepped fixture files after gate

Same as 26-RESEARCH.md Pitfall 1. Always read from `bench/runs/qwen35-eval-<ts>/refactor_orphan_count.txt`, not by re-grepping fixture files (gate EXIT trap restores them to canonical state, making re-grep show nonzero).

### Pitfall 4: P3 absence in eval path

P3 (PlanValidator `checkRenameTargetsEnumerated`) does NOT run during `--refactor` eval
(no `--plan` flag). Do not expect or look for `[PLAN INVALID]` in the eval transcript.
P1 migration is the only prong that reaches this path.

### Pitfall 5: Strict scorecard format

Final line must be exactly `**Total: 92/100, Recommendation: KEEP**`. No trailing spaces.
Validate with:
```bash
grep -E "^\*\*Total: 92/100, Recommendation: KEEP\*\*$" documentation/qwen35-122b-coding-eval.md
```

---

## Deferred Concerns (Reference Phase 26 Research)

The following are fully covered in `.planning/phases/26-re-evaluation/26-RESEARCH.md` and
require no duplication here:

| Concern | Phase 26 Section |
|---------|-----------------|
| All 11 eval doc edit sites (exact line numbers) | Q1 (lines 19-138) |
| `--refactor` flag handling and output files | Q2 |
| Fixture canonical state (README 2128 chars, 3 F# files) | Q3, Q5 |
| STATE.md update pattern (fields, v2.3 close-ready signals) | Q6, Q9 |
| Wall-clock estimate for `--refactor` invocations | Q7 |
| Stochastic variance policy (temp=0.2, up to 3 attempts) | Q8 |
| Strict scorecard format validation | Q4 |

---

## Sources

All findings from direct file inspection (HIGH confidence):

- `src/BlueCode.Cli/CompositionRoot.fs:68-104` — both prompt constants, verbatim content, line numbers
- `bench/run.sh:191-224` — gate() function, all 7 fixture prompts verbatim
- `bench/fixtures/multiturn_prompts.txt` — MT turn prompts (confirmed "Refactor" = pytest context, not rename)
- `bench/fixtures/mt_followup.txt` — MT followup ("What was the file I just listed?")
- `tests/BlueCode.Tests/AgentLoopTests.fs:60` — stub SystemPrompt confirmed (`"You are blueCode."`)
- `tests/BlueCode.Tests/PlanParseTests.fs:46` — stub SystemPrompt confirmed
- Python3 measurement script — char counts verified (783, 1183, 967, 999, 182, 1968)
- `.planning/phases/26-re-evaluation/26-RESEARCH.md` — Phase 26 findings (shared concerns)
- `.planning/phases/24-prompt-level-intervention/24-RESEARCH.md` — F# triple-quoted string rules (Q8)
- `.planning/ROADMAP.md` Phase 27 section — Phase 27 scope, guardrails, out-of-scope items
- `.planning/REQUIREMENTS.md` COMP-05, COMP-06 — validation criteria

---

## Metadata

**Confidence breakdown:**
- P1 migration edit operations: HIGH — old_string uniqueness verified by Python; char counts measured
- Bench gate fixture risk: HIGH — all 7 prompts read directly; no rename/restructur keywords found
- Kickstart command: HIGH — UID 501 verified; Phase 26 diagnostic confirmed exact command
- Plan decomposition: HIGH — Option Slim reasoning matches Phase 26 precedent pattern
- Char budget math: HIGH — Python-measured, invariant confirmed (1968 = 1968)
- Wall-clock estimates: MEDIUM — based on Phase 25 VERIFICATION.md evidence + Phase 26 research Q7

**Research date:** 2026-04-28
**Valid until:** Until CompositionRoot.fs prompts change or eval harness changes (stable for this milestone)
