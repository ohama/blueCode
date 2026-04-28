---
phase: 11-system-prompt-shrink
verified: 2026-04-26T09:40:00Z
status: passed
score: 4/4
must_haves:
  - sc: 1
    status: pass
    evidence: "python3 regex extraction of defaultSystemPrompt triple-quoted literal → 783 chars (≤800 threshold)"
  - sc: 2
    status: pass
    evidence: "bench/run.sh --gate live run: PASS T6_32b(3/5) T6_72b(3/5) W1_32b(3/3) W2_32b(3/3) T1_32b(3/3) T5_72b(3/4) B2_32b(2/3) B2_72b(2/3) → GATE PASS (8/8)"
  - sc: 3
    status: pass
    evidence: "grep -n POST-READ HINT AgentLoop.fs → 3 matches (doc comment + 2 injection sites); grep -n lastReadHint → 8 matches (decl, threading, injection, runLoop params); check-no-async.sh → OK; grep Serilog|Spectre|Argu|HttpClient Core → 0 functional opens (comments only); git diff ae11c64..HEAD --name-only src/BlueCode.Core/ → AgentLoop.fs only"
  - sc: 4
    status: pass
    evidence: "jq .tests.B2_32b baseline.json → pass:true, no regression field; jq .tests.B2_72b → pass:true, no regression field; gate_B2_32b.log: 'empty list, leading to a division by zero error'; gate_B2_72b.log: 'empty list. This causes a division by zero'"
gaps: []
human_verification: []
---

# Phase 11: System Prompt Shrink — Verification Report

**Phase Goal:** `defaultSystemPrompt` is cut from ~1500 chars to ≤800 chars without regressing any `bench/run.sh --gate` test, and the B2 fixture returns to correct divide-by-zero diagnosis, validating the prompt-length attention-shift hypothesis from the v1.2 audit.

**Verified:** 2026-04-26T09:40:00Z  
**Status:** PASSED  
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| #  | Truth                                                        | Status     | Evidence                                                  |
|----|--------------------------------------------------------------|------------|-----------------------------------------------------------|
| 1  | defaultSystemPrompt ≤800 chars                               | ✓ VERIFIED | 783 chars (python3 regex extraction, CompositionRoot.fs:52)|
| 2  | bench/run.sh --gate exits 0, all 8 cases PASS                | ✓ VERIFIED | Live run: GATE PASS (8/8), exit 0 confirmed               |
| 3  | AgentLoop.fs has post-read_file-truncated injection; Core pure | ✓ VERIFIED | grep + check-no-async.sh + git diff all clean             |
| 4  | B2 fixture: "empty list" diagnosis; baseline.json updated    | ✓ VERIFIED | Both B2_32b + B2_72b pass:true in baseline; live logs confirm|

**Score:** 4/4 truths verified

---

## SC1: defaultSystemPrompt ≤800 chars

**Check:** python3 regex extraction of triple-quoted literal in `src/BlueCode.Cli/CompositionRoot.fs`

```
python3 -c 'import re; m=re.search(r"defaultSystemPrompt:\s*string\s*=\s*\"\"\"(.*?)\"\"\"", open("src/BlueCode.Cli/CompositionRoot.fs").read(), re.DOTALL); print(len(m.group(1)))'
783
```

**Result:** 783 chars. Threshold is ≤800. PASS by 17 chars.

**What was removed**:
- Two read_file truncated/out-of-range hint sentences → moved to POST-READ HINT loop injection (Plan 11-01)
- edit_file defense-in-depth prose → covered by POST-EDIT CONSTRAINT injection (Plan 09.1-05)
- Verbose JSON schema per action → replaced with terse shorthand notation
- Multi-line Rules block → compressed to single line

**What was added/improved:**
- `grep_search` usage hint in Rules ("Use grep_search to locate symbols before reading large files") — directly fixes T6 navigation efficiency
- `edit_file` inline annotation `old_string(non-empty exact file content)` — documents semantic constraint in-prompt
- `final` input rendered as explicit JSON `{"answer": "<text>"}` — prevents model omitting answer key

**Baseline note:** Executor documentation cited ~1689 chars as the pre-shrink baseline (not ~1500 as ROADMAP estimated; the discrepancy was a measurement-script bug in an early planning session that compared raw F# source bytes including whitespace/quotes). The 1689→783 reduction is 54%, and the final 783 chars satisfies SC1's ≤800 threshold regardless of baseline estimate.

---

## SC2: bench/run.sh --gate exits 0

**Live run (2026-04-26, during this verification session):**

```
===== GATE: compare to baseline =====
  PASS T6_32b     steps=3/5 exit=0
  PASS T6_72b     steps=3/5 exit=0
  PASS W1_32b     steps=3/3 exit=0
  PASS W2_32b     steps=3/3 exit=0
  PASS T1_32b     steps=3/3 exit=0
  PASS T5_72b     steps=3/4 exit=0
  PASS B2_32b     steps=2/3 exit=0
  PASS B2_72b     steps=2/3 exit=0
===== GATE PASS (8/8) =====
```

All 8 required test entries (T6 × 32B/72B, W1/W2 × 32B, T1/T5 canaries, B2 × 32B/72B) report PASS. Exit code 0 confirmed. This matches the executor-reported gate evidence from commit.

---

## SC3: AgentLoop.fs injection path + Core purity

### POST-READ HINT injection

```
grep -n 'POST-READ HINT' src/BlueCode.Core/AgentLoop.fs
202: doc comment (/// ... [POST-READ HINT] message at the END)
257: truncated injection: sprintf "[POST-READ HINT] The previous read_file on %s returned truncated content..."
263: out-of-range injection: sprintf "[POST-READ HINT] The previous read_file on %s returned out-of-range..."
```

### lastReadHint threading

```
grep -n 'lastReadHint' src/BlueCode.Core/AgentLoop.fs
201: doc comment
205: buildMessages signature parameter
252: match lastReadHint with (injection dispatch)
276: doc comment for runLoop
291: runLoop signature parameter
300: passed to buildMessages call
373: lastReadHint' assignment (ReadFile success payload check)
385: passed to recursive runLoop call
```

8 occurrences — parameter declaration, threading through recursive call, injection sites, documentation. Implementation is substantive: the payload-header parsing uses `payload.IndexOf('\n')` to isolate the first line and checks for `, truncated]` or `, out-of-range]` substrings.

### Core purity checks

| Check                              | Result |
|------------------------------------|--------|
| `check-no-async.sh`                | OK: no async {} expressions in src/BlueCode.Core |
| `grep -rn 'Serilog\|Spectre\|Argu\|HttpClient' src/BlueCode.Core/` | 0 functional opens (4 matches are all comments) |
| `grep -rn 'llm-system' src/`      | 0 matches |
| `git diff ae11c64..HEAD --name-only -- src/BlueCode.Core/` | `AgentLoop.fs` only |

Domain.fs, Router.fs, Ports.fs, ContextBuffer.fs — all unchanged across Phase 11.

### Plan 11-02 scope expansion (FsToolExecutor.fs — informational)

SC3 is scoped to Core: "no async{} literals introduced, no Serilog/Spectre/Argu references added to Core." The following Cli-layer fixes are out-of-scope for SC3 but are documented here for regression context.

Commit (feat(11-02)) included two Rule 3 auto-fixes in `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` that were blocking gate-pass during iterative shrink:

1. **edit_file empty old_string guard** — An empty `old_string` caused `String.IndexOf("")` to always return 0 and `Substring(0, 0)` to return empty string, resulting in an infinite replacement loop. Fix: early return `Failure(1, "oldString must be non-empty")` before the resolution path.

2. **grep_search file-path support** — When the caller passed a file path instead of a directory, `Directory.EnumerateFiles(root, ...)` would throw `DirectoryNotFoundException`. Fix: check `File.Exists(root)` first; if true, yield just `root` as the single file to search.

Both are genuine bug fixes (not refactors): they close cases where valid tool calls produced crashes or infinite loops, directly causing W2_32b and T6_32b gate failures during iterative shrink. These fixes are in the Cli adapter layer (not Core) and do not violate SC3. They are relevant for future regression analysis: if `FsToolExecutor.fs` is ever rewritten, these edge cases should be preserved.

---

## SC4: B2 recovery + baseline updated

### baseline.json entries

```json
"B2_32b": {
  "step_count": 2,
  "step_count_max": 3,
  "pass": true,
  "expected_diagnosis": "empty list causes DivideByZeroException",
  "actual_diagnosis": "empty list causes DivideByZeroException",
  "note": "Recovered post-PERF-01 prompt shrink (Plan 11-02, 1689→783 chars)..."
}
"B2_72b": {
  "step_count": 2,
  "step_count_max": 3,
  "pass": true,
  ...
}
```

Both entries: `pass: true`, no `regression` field. Updated by Plan 11-03 on 2026-04-26.

### Live gate logs (gate-20260426-182738)

**B2_32b:** "The bug is identified. It occurs when the function `average` is called with an empty list, leading to a division by zero error."

**B2_72b:** "The bug is triggered when the function `average` is called with an empty list. This causes a division by zero because `List.length xs` returns 0 for an empty list."

Both models correctly identify "empty list" as the trigger. SC4 requires "at least one model" — both recovered, exceeding the criterion.

**Hypothesis validation:** The v1.2 audit's prompt-length attention-shift hypothesis is confirmed. Pre-shrink, the 1689-char prompt contained redundant tool-usage guidance and repetitive rules that displaced attention from the actual reasoning task. At 783 chars, both models diagnose the bug correctly in 2 steps (within the 3-step max).

---

## Phase Invariant Checks

| Invariant                                          | Result |
|----------------------------------------------------|--------|
| Only AgentLoop.fs changed in Core (Phase 11)       | PASS   |
| Domain.fs unchanged                                | PASS   |
| 7 commits since ae11c64 (Phase 10 close)           | PASS (7 commits confirmed — the "8 commits" in execution evidence was inclusive of ae11c64 itself) |
| Tests: 243 passed, 1 ignored, 0 failed             | PASS   |
| check-no-async.sh exits 0                          | PASS   |
| No llm-system paths in src/                        | PASS   |

---

## Anti-Patterns Found

None. No TODO/FIXME/placeholder patterns in Phase 11 modified files. The two FsToolExecutor.fs Rule 3 fixes are complete implementations, not stubs.

---

## Overall Verdict

**Status: PASSED**

All 4 success criteria verified against the actual codebase:

- SC1: 783 chars (≤800)
- SC2: GATE PASS (8/8) — live confirmed this session
- SC3: POST-READ HINT injection wired correctly; Core purity intact; AgentLoop.fs is the only Core file modified
- SC4: Both B2 models recover; baseline.json updated; v1.2 attention-shift hypothesis validated

Phase 11 is the v1.3 milestone capstone. All gate tests pass and the B2 regression is closed. v1.3 is ready for `/gsd:complete-milestone`.

---

_Verified: 2026-04-26T09:40:00Z_  
_Verifier: Claude (gsd-verifier)_
