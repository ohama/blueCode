---
phase: 36-manual-test-fixes
plan: 03
type: execute
wave: 3
depends_on: ["36-02"]
files_modified:
  - src/BlueCode.Cli/CompositionRoot.fs
  - documentation/manual-test-guide.md
  - CLAUDE.md
autonomous: true

must_haves:
  truths:
    - "planSystemPromptSuffix in src/BlueCode.Cli/CompositionRoot.fs contains an explicit 'maximum 10 steps' constraint reinforcing the existing '1-10 steps' line"
    - "planSystemPromptSuffix contains an explicit 'no placeholder paths' constraint listing the placeholder forms (e.g., '<file>', '<discovered_file>', 'placeholder') that the model must NOT emit"
    - "Updated planSystemPromptSuffix is non-empty, syntactically valid F# triple-quoted string; build succeeds"
    - "CLAUDE.md prompt-length invariant comment reflects the new char count of planSystemPromptSuffix (e.g., update '999' and '1968' if those numbers changed)"
    - "manual-test-guide.md T-16/T-17/T-18/T-19 commands include `--allow-paths /tmp/bc-test` so the next manual round PASSes"
    - "manual-test-guide.md T-100/T-101 commands include `--allow-paths /tmp/bc-e2e`"
    - "manual-test-guide.md T-100 result section is updated to reflect the Phase 36 research finding: no code bug, FinalAnswer step is structurally StepSuccess [ok], 'hallucinated success' is model behaviour after path-block; the doc fix uses --allow-paths to make the path-block disappear in the first place"
    - "manual-test-guide.md has a top-level note (near §1 or top-of-file) explaining: 'tests using /tmp/* paths require --allow-paths flag (Phase 36)'"
    - "Bench gate `bash bench/run.sh --gate` exits 0 with 'GATE PASS (7/7)' (no regression from any of Plans 36-01, 36-02, 36-03)"
    - "Zero changes to src/BlueCode.Core/** across this plan AND across the entire phase (cumulative `git diff master -- src/BlueCode.Core/` empty after this plan ships)"
  artifacts:
    - path: "src/BlueCode.Cli/CompositionRoot.fs"
      provides: "Updated planSystemPromptSuffix with max-10-steps and no-placeholder constraints"
      contains: "placeholder"
      contains_2: "10 steps"
    - path: "documentation/manual-test-guide.md"
      provides: "Top-of-file --allow-paths note + T-16/17/18/19/100/101 command updates + T-100 result re-interpretation"
      contains: "--allow-paths"
      contains_2: "Phase 36"
    - path: "CLAUDE.md"
      provides: "Updated prompt-length invariant numbers (if changed)"
      contains: "planSystemPromptSuffix"
  key_links:
    - from: "src/BlueCode.Cli/CompositionRoot.fs (planSystemPromptSuffix)"
      to: "src/BlueCode.Cli/Program.fs (line 191 — passes suffix to runPlanTurn)"
      via: "open BlueCode.Cli.CompositionRoot; runPlanTurn ... CompositionRoot.planSystemPromptSuffix ..."
      pattern: "CompositionRoot\\.planSystemPromptSuffix"
    - from: "documentation/manual-test-guide.md (T-16..T-19, T-100, T-101)"
      to: "Plan 36-02 (--allow-paths flag)"
      via: "command-line flag enabled by Plan 36-02"
      pattern: "--allow-paths"
    - from: "CLAUDE.md (prompt-length invariant comment)"
      to: "src/BlueCode.Cli/CompositionRoot.fs (planSystemPromptSuffix.Length)"
      via: "manual char-count update"
      pattern: "planSystemPromptSuffix"
---

<objective>
Phase 36 — Plan 03: Tighten the plan-mode prompt suffix to discourage 11+ step plans and
placeholder paths (T-75/T-76 root-cause mitigation), then update manual-test-guide.md to
use the new `--allow-paths` flag from Plan 36-02 and re-interpret T-100's "hallucinated
success" finding (research confirmed: no code bug, model behaviour). This is the closing
plan of Phase 36 and runs the bench gate to verify zero regression.

Purpose: Track 2 (PlanValidator UX) and Track 4 (hallucinated-success investigation) from
the phase scope. Both are prompt-suffix and documentation work — no Core changes, minimal
Cli code change, doc-only finishing touches. Combining them keeps Phase 36 at 3 plans
(matching CLAUDE.md aggressive-atomicity; each plan ≤50% context).

Why this comes LAST in the wave chain: it modifies `documentation/manual-test-guide.md`
which references `--allow-paths` (added by Plan 36-02), and it runs the bench gate as
the phase-completion gate (must run AFTER all source changes from Plans 36-01 and 36-02
land).

Why Plan 36-02 dependency: file overlap on `CompositionRoot.fs` (36-02 added the
`AllowPaths` field; this plan modifies `planSystemPromptSuffix`). Sequencing prevents
merge conflict.

Output:
- 2-4 sentences added to `planSystemPromptSuffix` in `CompositionRoot.fs` (additive — no
  removed text; existing few-shot example preserved).
- CLAUDE.md prompt-length invariant comment updated to reflect new char counts.
- 6 command updates in `manual-test-guide.md` (T-16, T-17, T-18, T-19, T-100, T-101).
- 1 top-of-file note in `manual-test-guide.md` explaining the `--allow-paths` requirement.
- T-100 result section re-interpreted with the research finding.
- Bench gate verification (`bash bench/run.sh --gate`) — phase-completion gate.
- Optional: 1 unit test in `CompositionRootTests.fs` asserting the suffix contains the
  new constraint substrings (defensive — catches regression of the tuning).
- Zero changes to: `src/BlueCode.Core/**`, `BlueCode.Cli.fsproj`, `BlueCode.Tests.fsproj`,
  `RouterTests.fs`.

Open question handling (from research §Open Questions):
- Q1 (1968-char invariant): grep confirmed NO test asserts the literal numbers 1968/967/999.
  Updating the suffix is safe; just update the CLAUDE.md comment.
- Q2 (planSystemPromptSuffix in tests): grep confirmed the only callsite outside source is
  `Program.fs:191` (passes the value to `runPlanTurn`), and tests don't assert its content
  verbatim. Adding text is safe.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/STATE.md
@.planning/ROADMAP.md
@.planning/phases/36-manual-test-fixes/36-RESEARCH.md
@.planning/phases/36-manual-test-fixes/36-01-glob-recursive-PLAN.md
@.planning/phases/36-manual-test-fixes/36-02-allow-paths-PLAN.md
@CLAUDE.md
@src/BlueCode.Cli/CompositionRoot.fs
@documentation/manual-test-guide.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Pre-flight verification — ensure no test asserts prompt-suffix verbatim</name>
  <files>(read-only — verification step)</files>
  <action>
Before modifying `planSystemPromptSuffix`, RE-VERIFY the research finding that no test
hardcodes its content. Run:

```bash
grep -rn "1968\|OVERRIDE — PLAN MODE\|Constraints: 1-10\|Targets: \[" tests/ src/BlueCode.Cli/ 2>/dev/null
grep -rn "planSystemPromptSuffix" tests/ 2>/dev/null
```

Acceptable output:
- `src/BlueCode.Cli/CompositionRoot.fs` matches (the source itself)
- `src/BlueCode.Cli/Program.fs:191` match (the consumer)
- `tests/BlueCode.Tests/CompositionRootTests.fs` MAY match if a length/content test exists
- ZERO matches in any other test file

If a test asserts the suffix's exact string content, it must be UPDATED in this plan.
Pause and add an extra task to update those tests before proceeding.

Also verify the current char count of the existing suffix (informational baseline):

```bash
dotnet fsi --use:- <<'EOF'
let s = """OVERRIDE — PLAN MODE ACTIVE. Do NOT use read_file/write_file/list_dir/run_shell/edit_file/glob_search/grep_search/final actions.
Your ONLY valid response is action="plan". Respond with EXACTLY this JSON shape:
{"thought": "<reasoning>", "action": "plan", "input": {"steps": [{"tool": "<tool>", "input": {}, "rationale": "<why>"}], "rationale": "<overall why>"}}
where each "tool" is one of: read_file|write_file|list_dir|run_shell|edit_file|glob_search|grep_search.
Constraints: 1-10 steps. Use the minimum steps needed; reserve the full budget only for tasks requiring reads across multiple files before editing. No two adjacent steps may be identical. Do NOT execute — user will approve first.

Example: rename add->sum AND add3->sum3 across Calculator.fs/Main.fs/Tests.fs
Targets: [add->sum (Calculator.fs def+body, Main.fs, Tests.fs); add3->sum3 (Calculator.fs def, Main.fs, Tests.fs)]
Steps: grep_search(add), grep_search(add3), edit_file(Calculator.fs), edit_file(Main.fs), edit_file(Tests.fs)"""
printfn "current suffix len = %d" s.Length
EOF
```

Expected: `current suffix len = 999` (matches CLAUDE.md invariant).

Record the baseline for use in CLAUDE.md update later. NO commit, NO source change in
this task — purely defensive verification.
  </action>
  <verify>
1. The greps above show ONLY the source-file and consumer matches; tests do not assert verbatim content (or the matches found in tests are non-content assertions like length-only or contains-only that survive additive changes).
2. The current suffix length is `999` (informational; if different, the CLAUDE.md invariant is already stale and this plan must reconcile it).
  </verify>
  <done>
- [x] Confirmed no test will be broken by additive prompt-suffix changes.
- [x] Baseline suffix length recorded (expected: 999).
- [x] Zero file changes in this task.
  </done>
</task>

<task type="auto">
  <name>Task 2: Tighten planSystemPromptSuffix with explicit max-10-steps and no-placeholder constraints</name>
  <files>
src/BlueCode.Cli/CompositionRoot.fs
CLAUDE.md
  </files>
  <action>
**Step 2.1 — `src/BlueCode.Cli/CompositionRoot.fs`:**

Locate `planSystemPromptSuffix` (currently lines 95-104). Modify the existing
`Constraints:` line to be more emphatic and add a new `Path rules:` line BEFORE the
`Example:` block. Keep the existing few-shot example block (Phase 24-02 P2) verbatim.

REPLACE the line:

```
Constraints: 1-10 steps. Use the minimum steps needed; reserve the full budget only for tasks requiring reads across multiple files before editing. No two adjacent steps may be identical. Do NOT execute — user will approve first.
```

WITH:

```
Constraints: MAXIMUM 10 steps (HARD LIMIT — plans with 11+ steps are auto-rejected and your turn is wasted). Use the minimum steps needed; reserve the full budget only for tasks requiring reads across multiple files before editing. No two adjacent steps may be identical. Do NOT execute — user will approve first.

Path rules: Every step's input.path (or pattern, or command argument) MUST be a literal file path or filename you have determined from the prompt or from a prior grep_search/glob_search/list_dir step. Do NOT emit placeholder forms such as "<file>", "<discovered_file_X>", "<placeholder>", "filename", "path/to/file". If you do not yet know the exact path, your FIRST step must be the discovery tool (grep_search / glob_search / list_dir) — never a write_file or edit_file with a guessed path.
```

DO NOT delete the few-shot Example block (the 3-line Example/Targets/Steps below); it is
the Phase 24-02 P2 directive and is still load-bearing.

The rest of the suffix (the OVERRIDE preamble, the JSON shape line, the tool-name list,
the Example block) is UNCHANGED.

**Step 2.2 — Verify build:**

```
dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj
```

Expected: 0 errors. The triple-quoted string accepts the new content directly.

**Step 2.3 — Compute new suffix length and update CLAUDE.md invariant:**

Run a quick fsi verification to compute the NEW length:

```
dotnet fsi --exec - <<'EOF'
open BlueCode.Cli.CompositionRoot
printfn "new suffix len = %d" planSystemPromptSuffix.Length
EOF
```

(If fsi loading the project module is awkward, just `wc -c` the suffix substring inside
the source file, or trust the manual character count you can compute from the diff.)

Record the new length (call it N).

Open `CLAUDE.md` and locate the invariant line(s) — currently:

> ```
> defaultSystemPrompt(967) + "\n\n" + planSystemPromptSuffix(999) = 1968
> ```

This appears in the §Critical conventions / planSystemPromptSuffix subsection, AND
referenced in §Stable patterns of `.planning/STATE.md` (read-only — DO NOT edit STATE.md
in this plan; the next /gsd:complete-milestone will refresh STATE.md). For CLAUDE.md only,
update the number `999` to `N` and update `1968` to `967 + 2 + N` = `(969 + N)`.

EXAMPLE: if N = 1340, the new line reads:
> `defaultSystemPrompt(967) + "\n\n" + planSystemPromptSuffix(1340) = 2309`

Also add a brief note in the same paragraph:
> Phase 36-03: planSystemPromptSuffix expanded with explicit max-10-steps and no-placeholder constraints (T-75/T-76 mitigation).

DO NOT edit `.planning/STATE.md` directly — it's the project history record; let the next
session-end procedure update it.

**Step 2.4 — Commit:**

```
git add src/BlueCode.Cli/CompositionRoot.fs CLAUDE.md
git commit -m "feat(36-03): tighten planSystemPromptSuffix with explicit max-10 + no-placeholder constraints (T-75/T-76)

T-75 root cause: model emits 11+ step plans then second retry returns
non-JSON, surfacing as 'invalid JSON twice' instead of 'plan invalid'.
T-76 root cause: model emits placeholder paths like '<discovered_file>'
that PlanValidator's checkRenameTargetsEnumerated heuristic does not catch.

Both are model-behaviour issues fixable only via prompt tuning since
PlanValidator and AgentLoop.buildCorrection live in src/BlueCode.Core/
(read-only per phase invariant).

Adds two clarifying clauses to planSystemPromptSuffix:
  Constraints: HARD LIMIT max 10 steps; 11+ rejected.
  Path rules: no placeholder paths; first step must be discovery (grep_search /
              glob_search / list_dir) when path unknown.

Existing Constraints sentence + Example/Targets/Steps few-shot (Phase 24-02
P2) preserved verbatim.

CLAUDE.md prompt-length invariant updated: planSystemPromptSuffix(999) ->
planSystemPromptSuffix(<NEW>); combined invariant 1968 -> 967+2+<NEW>.

Core untouched: git diff master -- src/BlueCode.Core/ wc -l = 0."
```
  </action>
  <verify>
1. `grep -c "MAXIMUM 10 steps\|HARD LIMIT" src/BlueCode.Cli/CompositionRoot.fs` shows ≥1.
2. `grep -c "Path rules:\|placeholder" src/BlueCode.Cli/CompositionRoot.fs` shows ≥2 (Path rules line + placeholder mentions).
3. `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0.
4. `grep -n "OVERRIDE — PLAN MODE\|Targets: \[" src/BlueCode.Cli/CompositionRoot.fs` confirms preamble + few-shot block still present (regression guard).
5. `grep -n "planSystemPromptSuffix" CLAUDE.md` shows the updated invariant numbers.
6. `git diff master -- src/BlueCode.Core/ | wc -l` outputs `0`.
  </verify>
  <done>
- [x] planSystemPromptSuffix has max-10-steps HARD LIMIT clause.
- [x] planSystemPromptSuffix has Path rules / no-placeholder clause.
- [x] Few-shot Example block preserved verbatim.
- [x] Build clean.
- [x] CLAUDE.md prompt-length invariant updated.
- [x] Single atomic commit `feat(36-03): tighten planSystemPromptSuffix ...`.
- [x] Core untouched.
  </done>
</task>

<task type="auto">
  <name>Task 3: Update manual-test-guide.md commands + T-100 reinterpretation + top-of-file note</name>
  <files>documentation/manual-test-guide.md</files>
  <action>
**Step 3.1 — Add a top-of-file note about --allow-paths.**

Locate the section just AFTER the "실행 요약" frontmatter table (around line 35-41,
between the bench-gate PASS section and the `>` blockquote about working directory). Add
a new blockquote:

```markdown
> **Phase 36 — `--allow-paths` 사용법:**
> 모든 `/tmp/*` 경로를 사용하는 테스트(T-16, T-17, T-18, T-19, T-100, T-101)는 `--allow-paths` 플래그를 prompt 앞에 추가해야 한다. 예: `bc --allow-paths /tmp/bc-test --verbose "..."`. 플래그 없이는 `FsToolExecutor` 가 project-root 외 경로를 차단한다 (security invariant). 본 가이드의 명령 예시는 모두 Phase 36 적용본이다 (2026-05-04 manual round 1 fix; 시행: Phase 36-02).
```

**Step 3.2 — Update each affected test command. T-16 (line ~218-222):**

REPLACE:

```bash
mkdir -p /tmp/bc-test
bc --verbose "Create a file at /tmp/bc-test/hello.txt with the content 'manual test passed'. Then confirm."
cat /tmp/bc-test/hello.txt
```

WITH:

```bash
mkdir -p /tmp/bc-test
bc --allow-paths /tmp/bc-test --verbose "Create a file at /tmp/bc-test/hello.txt with the content 'manual test passed'. Then confirm."
cat /tmp/bc-test/hello.txt
```

ALSO update the result block ("실행 결과 (2026-05-04, 21s): ✅ PASS via fallback...")
to add a "Phase 36 update:" line:

> **Phase 36 update:** With `--allow-paths /tmp/bc-test`, model can use `write_file` directly — no `run_shell` fallback expected.

**Step 3.3 — T-17 (line ~231-234):** REPLACE:

```bash
echo "let answer = 42" > /tmp/bc-test/edit-target.fs
bc --verbose "In /tmp/bc-test/edit-target.fs replace '42' with '43'. Use the edit_file tool."
cat /tmp/bc-test/edit-target.fs
```

WITH:

```bash
echo "let answer = 42" > /tmp/bc-test/edit-target.fs
bc --allow-paths /tmp/bc-test --verbose "In /tmp/bc-test/edit-target.fs replace '42' with '43'. Use the edit_file tool."
cat /tmp/bc-test/edit-target.fs
```

Add to the result block: "**Phase 36 update:** edit_file should now succeed via the
allow-paths flag."

**Step 3.4 — T-18 (line ~243-246):** REPLACE:

```bash
echo "let mistakes = 5" > /tmp/bc-test/multi.fs
bc --verbose "In /tmp/bc-test/multi.fs change the literal 5 to 0. Read the file first to confirm what's there, then edit."
cat /tmp/bc-test/multi.fs
```

WITH:

```bash
echo "let mistakes = 5" > /tmp/bc-test/multi.fs
bc --allow-paths /tmp/bc-test --verbose "In /tmp/bc-test/multi.fs change the literal 5 to 0. Read the file first to confirm what's there, then edit."
cat /tmp/bc-test/multi.fs
```

Add to result: "**Phase 36 update:** read_file/edit_file work with --allow-paths."

**Step 3.5 — T-19 (line ~255-260):** REPLACE:

```bash
mkdir -p /tmp/bc-test/multi
echo "let foo_bar = 1" > /tmp/bc-test/multi/a.fs
echo "let foo_bar = 2" > /tmp/bc-test/multi/b.fs
bc --verbose "Rename foo_bar to bar_baz in BOTH /tmp/bc-test/multi/a.fs and /tmp/bc-test/multi/b.fs."
grep -c "bar_baz" /tmp/bc-test/multi/*.fs
```

WITH:

```bash
mkdir -p /tmp/bc-test/multi
echo "let foo_bar = 1" > /tmp/bc-test/multi/a.fs
echo "let foo_bar = 2" > /tmp/bc-test/multi/b.fs
bc --allow-paths /tmp/bc-test --verbose "Rename foo_bar to bar_baz in BOTH /tmp/bc-test/multi/a.fs and /tmp/bc-test/multi/b.fs."
grep -c "bar_baz" /tmp/bc-test/multi/*.fs
```

Update the action-item callout block at line ~267 to remove "FsToolExecutor 의 path 잠금이
`/tmp/*` 절대경로를 모두 차단" (now obsolete — this is exactly what Phase 36-02 fixed).
Replace with a Phase 36 historical note:

> **T-16 ~ T-19 historical note:** 2026-05-04 round 1 에서는 path 잠금으로 모두 FAIL.
> Phase 36-02 에서 `--allow-paths` 플래그 추가 후 prompt 명령에 flag 포함하도록 가이드 업데이트.
> 다음 round 에서 PASS 예상. T-14 의 glob 패턴 문제는 Phase 36-01 에서 별도 수정 (bare pattern 자동 recursive 확장).

**Step 3.6 — T-100 (line ~1320-1336):** Update the command block AND result section.

REPLACE the command block:

```bash
# 단계 1: 새 세션에서 파일 생성
mkdir -p /tmp/bc-e2e
SID=$(bc --newsession "Create /tmp/bc-e2e/notes.md with the line 'project alpha kicked off'." 2>&1 | grep "^Session: " | awk '{print $2}' | head -1)
echo "captured SID=$SID"
cat /tmp/bc-e2e/notes.md

# 단계 2: 같은 세션 resume 으로 ammendment
bc --resume "$SID" "Append a second line 'milestone 1 complete' to that same notes.md."
cat /tmp/bc-e2e/notes.md

# 단계 3: 두 줄 모두 있는지 확인
grep -c "alpha\|milestone" /tmp/bc-e2e/notes.md
```

WITH:

```bash
# 단계 1: 새 세션에서 파일 생성 (Phase 36: --allow-paths 필수)
mkdir -p /tmp/bc-e2e
SID=$(bc --newsession --allow-paths /tmp/bc-e2e "Create /tmp/bc-e2e/notes.md with the line 'project alpha kicked off'." 2>&1 | grep "^Session: " | awk '{print $2}' | head -1)
echo "captured SID=$SID"
cat /tmp/bc-e2e/notes.md

# 단계 2: 같은 세션 resume 으로 amendment (Phase 36: --allow-paths 필수)
bc --resume "$SID" --allow-paths /tmp/bc-e2e "Append a second line 'milestone 1 complete' to that same notes.md."
cat /tmp/bc-e2e/notes.md

# 단계 3: 두 줄 모두 있는지 확인
grep -c "alpha\|milestone" /tmp/bc-e2e/notes.md
```

REPLACE the "실행 결과 (2026-05-04): ❌ FAIL ..." block with the Phase 36 re-interpretation:

```markdown
**실행 결과 (2026-05-04, round 1):** ❌ FAIL — file `/tmp/bc-e2e/notes.md` 미생성 (path 차단).
step 2 의 model 은 `[ok]` step 표시하면서 "Successfully appended..." 로 답했으나 디스크에는 파일 없음
(처음에는 hallucinated success 로 보고됨).

**Phase 36 research 결과 (2026-05-04, round 1.5):** **코드 버그 아님.**
- step 1 (write_file) 은 `PathEscapeBlocked` → `StepFailed "path escape blocked"` → `[fail]` 로 정상 표시.
- step 2 의 `[ok]` 은 **FinalAnswer step** 에서 나온 것 — `AgentLoop.fs` line 323 `Status = StepSuccess`
  로 FinalAnswer 는 항상 구조적으로 success. 도구 실패와 무관.
- "Successfully appended" 텍스트는 model 의 hallucination — 도구 실패 후 model 이 자유 텍스트로
  거짓 보고할 수 있음 (LLM 한계, blueCode layer 에서 fix 불가).

**Phase 36 fix (이 가이드 업데이트):**
- `--allow-paths /tmp/bc-e2e` 플래그 사용 → write_file 이 정상 작동 → `[fail]` step 자체가 안 나옴 →
  hallucination 트리거 사라짐.
- 추가 보강: `defaultSystemPrompt` 에 path-block-경고 라인 추가는 별도 phase (v2.6+) 후보 — 본 phase 는
  scope outside.

**round 2 기대:** PASS — 파일 두 줄 모두 생성.
```

**Step 3.7 — T-101 (line ~1340-1364):** Update the file-prep block and the REPL command list:

REPLACE:

```bash
# 임시 F# 파일
cat > /tmp/bc-e2e/sample.fsx << 'EOF'
let add a b = a + b
printfn "%d" (add 2 3)
EOF
```

WITH:

```bash
# 임시 F# 파일 (Phase 36: REPL 호출 시 --allow-paths 사용)
mkdir -p /tmp/bc-e2e
cat > /tmp/bc-e2e/sample.fsx << 'EOF'
let add a b = a + b
printfn "%d" (add 2 3)
EOF
```

REPLACE the "REPL 진입 후" command block:

```
Read /tmp/bc-e2e/sample.fsx and tell me what it computes.
Now use edit_file to change 'a + b' to 'a * b' in that file.
/status
/clear
What did we just compute? (testing /clear cleared priorSteps)
/exit
```

ADD a preamble line above it (NEW):

```
Note: REPL 진입 명령에 `bc --allow-paths /tmp/bc-e2e` 사용 (--allow-paths 는 REPL session 전체에 적용).
```

**Step 3.8 — Commit:**

```
git add documentation/manual-test-guide.md
git commit -m "docs(36-03): update manual-test-guide T-16/17/18/19/100/101 for --allow-paths + T-100 re-interp

Phase 36-02 added --allow-paths flag; this commit threads the flag into
the test command examples so the next manual round PASSes the previously-
FAIL tests.

T-100 result section re-written: research confirmed no code bug.
- step 1 (write_file path-block) -> StepFailed [fail] -- correct
- step 2 [ok] is FinalAnswer (always StepSuccess structurally)
- 'Successfully appended' is model hallucination (LLM-layer; not fixable
  in blueCode)
With --allow-paths the path-block disappears and the hallucination trigger
goes with it.

Adds top-of-file blockquote explaining --allow-paths requirement for /tmp
tests. Removes obsolete /tmp/* path-block action-item from T-19 callout."
```
  </action>
  <verify>
1. `grep -c "\-\-allow-paths" documentation/manual-test-guide.md` shows ≥7 (top-note + 6 test commands).
2. `grep -n "Phase 36" documentation/manual-test-guide.md` shows ≥6 references (top note + each updated test result).
3. `grep -c "FinalAnswer" documentation/manual-test-guide.md` shows ≥1 (T-100 re-interp).
4. `grep -c "hallucinated success\|hallucination" documentation/manual-test-guide.md` shows ≥2 (round 1 historical + round 1.5 explanation).
5. `git diff master -- src/BlueCode.Core/ | wc -l` outputs `0`.
6. Doc renders sanely (manual visual inspection of the diff).
  </verify>
  <done>
- [x] Top-of-file `--allow-paths` blockquote added.
- [x] T-16, T-17, T-18, T-19 commands include `--allow-paths /tmp/bc-test`.
- [x] T-100, T-101 commands include `--allow-paths /tmp/bc-e2e`.
- [x] T-100 result section re-interpreted with Phase 36 finding (no code bug).
- [x] Obsolete /tmp/* action-item callout updated/removed.
- [x] Single atomic commit `docs(36-03): update manual-test-guide ...`.
  </done>
</task>

<task type="auto">
  <name>Task 4: Bench gate verification + Core-purity final-check</name>
  <files>(verification only — no source changes)</files>
  <action>
This is the phase-completion gate. Run the bench gate to confirm the cumulative changes
across Plans 36-01, 36-02, 36-03 introduced no regression.

**Step 4.1 — Build the binary fresh:**

```
dotnet build -c Release src/BlueCode.Cli/BlueCode.Cli.fsproj
```

Bench scripts often expect a Release build. Verify the bench script's dotnet invocation
expectation:

```
grep -n "dotnet run\|dotnet build" bench/run.sh | head -5
```

If the bench script uses `dotnet run --project src/BlueCode.Cli`, that uses Debug by
default; both should work, but Release is the production target.

**Step 4.2 — Run bench gate:**

```
bash bench/run.sh --gate
```

Expected (per CLAUDE.md):
- Exit 0
- Last line: `GATE PASS (7/7)`
- All 6 fixtures pass: T6_122b, W1_122b, W2_122b, T1_122b, T5_122b, B2_122b (the 6 entries
  in baseline.json — though the result line says 7/7, accounting for the MT entry as the
  7th).

If gate fails:
- Investigate whichever fixture diverged. Failure modes:
  - `--allow-paths` accidentally affecting bench paths — the bench fixtures use the repo
    workdir, never /tmp, so the empty AllowPaths default should preserve identity.
  - Prompt-suffix change affecting plan-mode bench prompts — check whether bench fixtures
    use `--plan` mode (search `bench/fixtures/*.txt` for `--plan` invocation; usually not).
  - Glob auto-expansion affecting any fixture's expected glob output — unlikely; bench
    fixtures don't rely on glob_search.

If gate fails for any other reason, halt and diagnose before declaring phase complete.

**Step 4.3 — Final Core-purity check (cumulative across phase):**

```
git log --oneline master..HEAD | head -10
git diff master -- src/BlueCode.Core/
```

Expected:
- `git log` shows commits from all 3 plans: `fix(36-01)`, `test(36-01)`, `feat(36-02)`,
  `feat(36-03)`, `docs(36-03)`. Approximately 5 commits total for Phase 36.
- `git diff master -- src/BlueCode.Core/` is EMPTY (zero lines). This is the phase
  invariant from CLAUDE.md and ROADMAP.md success criterion 9.

**Step 4.4 — Run full test suite once more:**

```
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | tail -10
```

Expected: 0 failures; total test count = pre-Phase-36-baseline (333) + 3 (36-01) + 8 (36-02) + 0 (36-03 has no new tests) = 344.

NOTE: total test count delta = +11 (within phase target +7..+12, satisfying ROADMAP success
criterion 8).

**Step 4.5 — Commit any incidental files (if any):**

This task usually does NOT produce a commit; it's pure verification. If the bench gate
produces or modifies files in `bench/runs/` (timestamped run logs are gitignored — see
`bench/run.sh`), no commit is needed.

If a verification commit is desired (e.g., to capture a `36-VERIFICATION.md`), defer that
to the `/gsd:verify-work 36` step that follows phase-execute. This plan ends after the
gate run.
  </action>
  <verify>
1. `bash bench/run.sh --gate; echo $?` shows exit code `0` and final line `GATE PASS (7/7)`.
2. `git diff master -- src/BlueCode.Core/ | wc -l` outputs `0` (cumulative phase invariant).
3. `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | tail -5` shows 0 failures, total count delta +11 from pre-phase baseline.
4. `bash scripts/check-no-async.sh` exits 0 (no async {} introduced anywhere in Cli either).
5. `git log --oneline master..HEAD | wc -l` shows 5-7 commits (matching the per-plan commit estimates).
  </verify>
  <done>
- [x] Bench gate `bash bench/run.sh --gate` PASS (7/7).
- [x] Core diff cumulative for Phase 36 is empty (`git diff master -- src/BlueCode.Core/`).
- [x] Full test suite passes; total delta = +11.
- [x] No new commits in this task (verification only); `36-03-SUMMARY.md` to be written.
  </done>
</task>

</tasks>

<verification>
Final phase-level gate (after all 4 tasks):

1. `dotnet build` exits 0 with 0 errors.
2. `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0; cumulative delta +11 (was 333, now 344).
3. `bash bench/run.sh --gate` exits 0; output ends with `GATE PASS (7/7)`.
4. `git diff master -- src/BlueCode.Core/` produces ZERO lines (Core invariant — ROADMAP success criterion 9).
5. `bash scripts/check-no-async.sh` exits 0.
6. `grep -c "\-\-allow-paths" documentation/manual-test-guide.md` ≥ 7.
7. `grep -c "MAXIMUM 10 steps\|HARD LIMIT" src/BlueCode.Cli/CompositionRoot.fs` ≥ 1.
8. Manual smoke (optional but recommended):
   - T-14: `bc --verbose "List all *.fsproj files in this repository."` returns 3 fsproj paths.
   - T-16: `mkdir -p /tmp/bc-test; bc --allow-paths /tmp/bc-test --verbose "Create /tmp/bc-test/hello.txt with 'manual test passed'."` produces the file via `write_file` (no `run_shell` fallback).
</verification>

<success_criteria>
- [ ] planSystemPromptSuffix has explicit "MAXIMUM 10 steps" + "HARD LIMIT" + "no placeholder paths" constraints (additive — existing few-shot preserved).
- [ ] CLAUDE.md prompt-length invariant numbers updated.
- [ ] manual-test-guide.md T-16/17/18/19/100/101 commands include `--allow-paths` flag.
- [ ] manual-test-guide.md T-100 result section re-interpreted with Phase 36 research finding (no code bug; FinalAnswer always StepSuccess structurally; model hallucination after path-block).
- [ ] manual-test-guide.md top-of-file blockquote explains --allow-paths requirement for /tmp tests.
- [ ] Bench gate `bash bench/run.sh --gate` PASS 7/7.
- [ ] Cumulative `git diff master -- src/BlueCode.Core/` empty.
- [ ] Cumulative test count delta = +11 (within phase target +7~12).
- [ ] 2 atomic commits in this plan: `feat(36-03): tighten planSystemPromptSuffix ...`, `docs(36-03): update manual-test-guide ...`.

Phase-level success criteria (mirror ROADMAP §Phase 36 success criteria):
1. T-14 invariant satisfied — `*.fsproj` enumerates 3 fsproj files (Plan 36-01).
2. T-75 mitigation — `MAXIMUM 10 steps` HARD LIMIT in suffix; on retry with 11+ steps, validator rejects + suffix discourages further attempts.
3. T-76 mitigation — `Path rules: no placeholder paths` in suffix; model is told to do discovery first when path unknown.
4. ~~PlanValidator reject detail visible on retry~~ — RESEARCH-CONFIRMED Core read-only; SKIPPED per phase requirement.
5. T-16/17/18/19/100/101 unblock with `--allow-paths` (Plan 36-02 + doc updates here).
6. T-100 root cause identified — no code bug; documented in manual-test-guide.md.
7. Bench gate 7/7 PASS preserved.
8. Test count delta +11 (in [+7, +12] range).
9. `git diff master -- src/BlueCode.Core/` empty.
</success_criteria>

<output>
After completion, create `.planning/phases/36-manual-test-fixes/36-03-SUMMARY.md` with this
frontmatter:

```yaml
---
phase: 36-manual-test-fixes
plan: 03
plan_name: prompt-suffix-and-doc
status: complete
completed_at: <ISO-8601 UTC>
test_count_delta: 0   # this plan adds prompt-tuning + doc; no new unit tests (the planSystemPromptSuffix change is observed in integration / next manual round)
files_modified:
  - src/BlueCode.Cli/CompositionRoot.fs
  - documentation/manual-test-guide.md
  - CLAUDE.md
core_diff_lines: 0
commits:
  - feat(36-03): tighten planSystemPromptSuffix with explicit max-10 + no-placeholder constraints (T-75/T-76)
  - docs(36-03): update manual-test-guide T-16/17/18/19/100/101 for --allow-paths + T-100 re-interp
bench_gate: PASS (7/7)
phase_test_count_delta: 11  # cumulative (3 from 36-01 + 8 from 36-02 + 0 here)
subsystem: cli-prompt+docs
affects: []
requires: [36-02]
---
```

Body sections (≤250 lines):
- Outcome:
  - planSystemPromptSuffix tuned (max-10-steps HARD LIMIT, no-placeholder constraint).
  - manual-test-guide.md updated for new --allow-paths flag.
  - T-100 'hallucinated success' diagnosed (no code bug; FinalAnswer is always StepSuccess
    structurally; LLM hallucination after path-block).
  - Bench gate 7/7 PASS.
  - Phase 36 invariant: zero src/BlueCode.Core/ diff.
- Suffix change diff (~6 lines added).
- Doc update summary (count of test commands updated, T-100 re-interp paragraph).
- Bench gate transcript (last 10 lines of output).
- Phase-cumulative status: 3 plans, 5-6 commits, +11 tests, 0 Core diff, 7/7 bench gate.
- Open follow-ups (deferred per phase out-of-scope):
  - priorSteps message-ordering quirk (T-54/59/61) — model behaviour, v2.6+.
  - Auto/default `/tmp/*` allowlist — explicit opt-in only is correct.
  - Glob/wildcard patterns in --allow-paths — exact prefix only.
  - System prompt path-block warning to reduce hallucinated confirmation — v2.6+ scope.
- Next workflow: `/gsd:verify-work 36` UAT gate, then optionally re-run round 2 of manual
  test suite to confirm T-14/T-16..T-19/T-100/T-101 all PASS.
</output>
