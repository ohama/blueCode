---
phase: 21-empirical-qwen-3-5-122b-coding-evaluation
verified: 2026-04-28T11:00:00Z
status: passed
score: 10/10 must-haves verified
re_verification: false
---

# Phase 21: Empirical Qwen 3.5 122B Coding Evaluation — Verification Report

**Phase Goal:** Deliver `documentation/qwen35-122b-coding-eval.md` with empirically-measured 100-point scorecard verdict against the 9 measurement requirements (PERF-EVAL-01..02, CORR-EVAL-01..04, REL-EVAL-01..03) plus DOC-EVAL-01 doc-deliverable. Bench gate stays 7/7 PASS post-eval.
**Verified:** 2026-04-28T11:00:00Z
**Status:** PASSED
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | eval doc exists with ≥600 lines | VERIFIED | `wc -l documentation/qwen35-122b-coding-eval.md` = 983 lines |
| 2 | eval doc has all 10 sections with verdict lines | VERIFIED | 10 `## §` headings found; all sections contain verdict lines (grepped below) |
| 3 | eval doc ends with strict-format Total/Recommendation line | VERIFIED | Line 983: `**Total: 82/100, Recommendation: KEEP**` |
| 4 | --setup exits 0 with venv populated and evalplus importable | VERIFIED | `bash bench/eval-qwen35-122b.sh --setup` → exit 0; `evalplus ok` printed |
| 5 | throughput.json: 15 entries, tokens_per_sec > 0 | VERIFIED | `bench/runs/qwen35-eval-20260428-052719/throughput.json` = 15 JSONL lines; first entry `tokens_per_sec: "31.29"` |
| 6 | ttft.json: 10 entries, ttft_ms > 0 | VERIFIED | `bench/runs/qwen35-eval-20260428-053114/ttft.json` = 10 JSONL lines; entries `ttft_ms: 929, 224, 221, 224, 222, ...` |
| 7 | humaneval_results.json: 164×2 entries; pass@1 numeric | VERIFIED | 328 JSONL lines (164 × {chat, completion}); score files `humaneval_chat_score.txt` pass@1=0.939; `humaneval_completion_score.txt` pass@1=0.226 |
| 8 | multiturn_N{1,3,5,7,10}/ directories exist with per-turn data | VERIFIED | All 5 dirs in `bench/runs/qwen35-eval-20260428-100537/`; each N has trial subdirs with `meta` + `transcript.log`; meta format: `N=3 trial=1 session=... invalid_json=0 step_markers=3` |
| 9 | refactor_multifile_diff.txt exists non-empty | VERIFIED | `bench/runs/qwen35-eval-20260428-093721/refactor_multifile_diff.txt` = 53 lines; contains agent transcript |
| 10 | schema_rate.txt: "X/50 InvalidJsonOutput" | VERIFIED | `bench/runs/qwen35-eval-20260428-095606/schema_rate.txt` = `0/50 InvalidJsonOutput` |
| 11 | needle.json: 4 size entries with retrieval boolean + latency | VERIFIED | `bench/runs/qwen35-eval-20260428-100057/needle.json`: 4 entries; all `retrieved: true`; `elapsed_s: 10.88, 20.89, 44.9, 46.43` |
| 12 | bench gate 7/7 PASS post-eval | VERIFIED | `bash bench/run.sh --gate` → `GATE PASS (7/7)` (exit 0; all 7 labels PASS) |
| 13 | git diff src/ empty | VERIFIED | `git diff src/` produces no output |
| 14 | git diff bench/baseline.json empty | VERIFIED | `git diff bench/baseline.json` produces no output |
| 15 | test count 282/1/0 unchanged | VERIFIED | `dotnet run tests/BlueCode.Tests/` → "282 tests run ... 282 passed, 1 ignored, 0 failed" |

**Score:** 15/15 truths verified

---

## Success Criteria Mapping

### SC1: --setup exits 0; evalplus importable

**Status: VERIFIED**

- `bash bench/eval-qwen35-122b.sh --setup` → exit 0
- Output: "venv exists at bench/.venv-eval; skipping creation" + pip satisfaction messages + "evalplus version: 0.3.1" + "Setup complete: bench/.venv-eval"
- `bench/.venv-eval/bin/python3` → Python 3.14 (SC1 notes "Python 3.12 fallback documented if 3.14 incompatible"; 3.14 works fine)
- `bench/.venv-eval/bin/python3 -c "import evalplus; print('evalplus ok')"` → exits 0

### SC2: --full artifacts exist (spread across multiple LOG_DIRs per 21-04 Option B decision)

**Status: VERIFIED** (with format note below)

| Artifact | Location | Content |
|----------|----------|---------|
| `throughput.json` | `bench/runs/qwen35-eval-20260428-052719/` | 15 JSONL entries; `tokens_per_sec` all numeric strings > 0 |
| `ttft.json` | `bench/runs/qwen35-eval-20260428-053114/` | 10 JSONL entries; `ttft_ms` all > 0 (929, 224, 221...) |
| `humaneval_results.json` | `bench/runs/qwen35-eval-20260428-055057/` | 328 JSONL entries (164 × 2 modes); score files have numeric pass@1 |
| `multiturn_N{1,3,5,7,10}/` | `bench/runs/qwen35-eval-20260428-100537/` | All 5 N-dirs present; per-trial `meta` + `transcript.log` |
| `refactor_multifile_diff.txt` | `bench/runs/qwen35-eval-20260428-093721/` | 53 lines; agent transcript |
| `schema_rate.txt` | `bench/runs/qwen35-eval-20260428-095606/` | "0/50 InvalidJsonOutput" |
| `needle.json` | `bench/runs/qwen35-eval-20260428-100057/` | 4 entries; `retrieved: true` all; `elapsed_s` all > 0 |

**Format note (multiturn):** ROADMAP SC2 says "per-N session JSONLs" but the implemented format is `meta` + `transcript.log` per trial (not `.jsonl` files). The 21-04 SUMMARY documents this as a deliberate implementation choice. The meta files capture the critical data (N, trial, session_id, invalid_json count, step_markers) in a structured one-liner format. This is a minor presentation deviation from the ROADMAP wording but the data content is fully present and the 21-04 plan itself specifies this exact format in its task scaffolding.

**Format note (needle latency):** ROADMAP SC2 says "retrieval boolean + latency" — the artifact uses `elapsed_s` (not `latency_ms`); this satisfies the latency requirement (unit differs from the SC2 wording but a latency field is present and > 0 for all entries).

### SC3: eval doc structure and verdict

**Status: VERIFIED**

- File: `/Users/ohama/projs/blueCode/documentation/qwen35-122b-coding-eval.md`
- Line count: **983 lines** (requirement: ≥600)
- Sections (10): §1 Methodology, §2 Correctness, §3 Performance, §4 Reliability, §5 Coding quality, §6 Comparison anchors, §7 Verdict scorecard, §8 Caveats and known limitations, §9 Recommended thresholds for re-evaluation, §10 Reproduction instructions
- Verdict line (strict regex match): **`**Total: 82/100, Recommendation: KEEP**`** at line 983
- Regex `^\*\*Total: [0-9]+/100, Recommendation: (KEEP|KEEP-WITH-CAVEATS|ESCALATE)\*\*$` matches

**Per-section verdicts (grepped):**

| Section | Verdict line (abridged) |
|---------|------------------------|
| §2.1 | PASS — chat pass@1 93.9% ≥ 75% top band. Score: **15/15** |
| §2.2 | 3 of 4 fixtures correct. Score: **11/15** |
| §2.3 | PASS — both language fixtures diagnosed correctly. Score: **5/5** |
| §2.4 | FAIL — orphan_count=1. Score: **0/5** |
| §3.1 | PASS — median 34.60 tok/s ≥ 30 tok/s. Score: **10/10** |
| §3.2 | PASS — median TTFT 222 ms ≤ 500 ms. Score: **5/5** |
| §3.3 | N/A — deferred. Score: **0/5** |
| §3.4 | PASS — gate 7/7 PASS. Score: **5/5** |
| §4.1 | PASS — 0/50 InvalidJsonOutput. Score: **10/10** |
| §4.2 | PASS — multi-turn stable through N=7. Score: **10/10** |
| §4.3 | PASS — 4/4 needle retrieved at 32k. Score: **5/5** |
| §5.1 | 1 of 3 transcripts F#. Score: **1/5** |
| §5.2 | PASS — generated tests correct. Score: **3/3** |
| §5.3 | PASS — 4/4 known issues identified. Score: **2/2** |

**§7 Verdict scorecard subtotals:**

| Dimension | Score | Max | Pct |
|-----------|-------|-----|-----|
| Correctness | 31 | 40 | 77.5% |
| Performance | 20 | 25 | 80.0% |
| Reliability | 25 | 25 | 100.0% |
| Coding quality | 6 | 10 | 60.0% |
| **Total** | **82** | **100** | **82%** |

Verdict rules applied: 82 ≥ 80 → KEEP; no dimension <60%; multi-turn degradation first at N=10 (not before turn 5); HumanEval+ chat 93.9% >> 30%.

### SC4: Bench gate 7/7 PASS; no src/ or baseline.json changes

**Status: VERIFIED**

- `bash bench/run.sh --gate` exit code: **0**
- Output: `GATE PASS (7/7)` with all 7 labels showing PASS (T6_122b, W1_122b, W2_122b, T1_122b, T5_122b, B2_122b, MT_122b)
- `git diff src/`: **empty** (no source code changes)
- `git diff bench/baseline.json`: **empty** (gate authority preserved byte-for-byte)

### SC5: New artifacts on disk

**Status: VERIFIED**

| Artifact | Exists | Notes |
|----------|--------|-------|
| `bench/eval-qwen35-122b.sh` | YES (executable) | |
| `bench/eval-humaneval-http.py` | YES (executable) | |
| `bench/eval-needle.py` | YES (executable) | |
| `bench/requirements-eval.txt` | YES | |
| `bench/fixtures/refactor_multifile/Calculator.fs` | YES | |
| `bench/fixtures/refactor_multifile/Main.fs` | YES | |
| `bench/fixtures/refactor_multifile/Tests.fs` | YES | |
| `bench/fixtures/refactor_multifile/README.md` | YES | |
| `bench/fixtures/bug_binsearch.fs` | YES | |
| `bench/fixtures/bug_python_typeerror.py` | YES | |
| `bench/fixtures/bug_typescript_async.ts` | YES | |
| `bench/fixtures/multiturn_prompts.txt` | YES | |
| `documentation/qwen35-122b-coding-eval.md` | YES (983 lines) | |
| `bench/run.sh:18` EXIT trap extended | YES | Contains `bug_binsearch.fs` + `refactor_multifile/{Calculator,Main,Tests}.fs` |
| `.planning/STATE.md` observation note | YES | 6 REL-EVAL/CORR-EVAL observations at lines ~100-113 |
| `CLAUDE.md` 2-line cross-reference under "Bench" | YES | Line 197: `documentation/qwen35-122b-coding-eval.md — empirical 100-point scorecard verdict...` |
| `bench/.venv-eval/` | YES (gitignored) | Python 3.14 venv; evalplus 0.3.1 |

---

## Architectural Invariants

| Invariant | Status | Evidence |
|-----------|--------|----------|
| `git diff src/` empty | VERIFIED | No output from `git diff src/` |
| `git diff bench/baseline.json` empty | VERIFIED | No output from `git diff bench/baseline.json` |
| `bash bench/run.sh --gate` → `GATE PASS (7/7)` exit 0 | VERIFIED | Run during verification; exit 0 confirmed |
| Test count 282/1/0 unchanged | VERIFIED | Expecto: "282 passed, 1 ignored, 0 failed" |
| No `mlx_lm` import in `bench/eval-needle.py` | VERIFIED | `grep -E "import mlx_lm|from mlx_lm" bench/eval-needle.py` → empty |
| Eval doc verdict line strict format | VERIFIED | Regex matches at line 983 |
| Eval doc ≥600 lines | VERIFIED | 983 lines |
| All 5 plan SUMMARYs exist | VERIFIED | 21-{01,02,03,04,05}-SUMMARY.md all present |

---

## Required Artifacts (Three-Level Check)

| Artifact | Exists | Substantive | Wired | Status |
|----------|--------|-------------|-------|--------|
| `bench/eval-qwen35-122b.sh` | YES | YES (executable, implements all modes) | YES (invoked by --setup, --throughput, etc.) | VERIFIED |
| `bench/eval-humaneval-http.py` | YES | YES (159 lines, requests.post adapter) | YES (called by eval-qwen35-122b.sh --humaneval) | VERIFIED |
| `bench/eval-needle.py` | YES | YES (HTTP-only, no mlx_lm) | YES (called by eval-qwen35-122b.sh --needle) | VERIFIED |
| `bench/requirements-eval.txt` | YES | YES (evalplus>=0.3.0 etc.) | YES (read by --setup pip install) | VERIFIED |
| `documentation/qwen35-122b-coding-eval.md` | YES | YES (983 lines, 10 sections, all verdicts) | YES (standalone deliverable) | VERIFIED |
| `bench/runs/qwen35-eval-*/throughput.json` | YES | YES (15 entries, all tokens_per_sec > 0) | YES (read by §3.1 in eval doc) | VERIFIED |
| `bench/runs/qwen35-eval-*/ttft.json` | YES | YES (10 entries, all ttft_ms > 0) | YES (read by §3.2 in eval doc) | VERIFIED |
| `bench/runs/qwen35-eval-*/humaneval_results.json` | YES | YES (328 JSONL entries, 164×2 modes) | YES (scored by evalplus; results in _score.txt files) | VERIFIED |
| `bench/runs/qwen35-eval-*/multiturn_N{1,3,5,7,10}/` | YES | YES (meta files with N, trial, invalid_json, step_markers) | YES (read by §4.2 in eval doc) | VERIFIED |
| `bench/runs/qwen35-eval-*/refactor_multifile_diff.txt` | YES | YES (53 lines agent transcript) | YES (read by §2.2 in eval doc) | VERIFIED |
| `bench/runs/qwen35-eval-*/schema_rate.txt` | YES | YES ("0/50 InvalidJsonOutput") | YES (read by §4.1 in eval doc) | VERIFIED |
| `bench/runs/qwen35-eval-*/needle.json` | YES | YES (4 entries, retrieved+elapsed_s) | YES (read by §4.3 in eval doc) | VERIFIED |

---

## Anti-Patterns Scan

No blocker anti-patterns found. The eval harness files (`eval-qwen35-122b.sh`, `eval-humaneval-http.py`, `eval-needle.py`) were spot-checked: no TODO/FIXME comments, no placeholder returns, no stub patterns. The eval doc has real empirical data throughout (numeric pass@1, actual tok/s, actual ttft_ms values from live runs).

---

## Human Verification Required

### 1. Qualitative F# idiom assessment (§5.1)

**Test:** Review the 3 multiturn transcripts cited in §5.1 and assess whether F# idioms (pattern matching, discriminated unions, pipeline operators) appear in the F# task transcript.
**Expected:** The eval doc claims 1/3 transcripts contains idiomatic F#; the other 2 are Python tasks by construction. Score 1/5 seems correct.
**Why human:** Idiom judgment is qualitative; not automatable via grep.

### 2. Refactor transcript quality (§2.2)

**Test:** Review `bench/runs/qwen35-eval-20260428-093721/refactor_multifile_diff.txt` to confirm the blueCode agent made real edits (not trivial/random changes) and that the reported `orphan_count=1` is accurate.
**Expected:** Agent read all 4 fixture files and edited Calculator.fs (renamed `add3` → `sum3`) before exhausting 5-step budget; Main.fs and Tests.fs have orphan references → score 11/15 vs 15/15.
**Why human:** Verifying the correctness of the refactor attempt requires semantic understanding of F# code.

---

## Summary

Phase 21 goal is **fully achieved**. The `documentation/qwen35-122b-coding-eval.md` deliverable exists at 983 lines with all 10 required sections, per-section verdict lines, and the strict-format final verdict `**Total: 82/100, Recommendation: KEEP**`.

All 7 measurement artifacts are on disk across multiple `bench/runs/qwen35-eval-*/` directories (per the Option B decision documented in 21-04 SUMMARY — individual sub-mode runs rather than one consolidated `--full` LOG_DIR). The bench gate ran to `GATE PASS (7/7)` during verification. No `src/` or `bench/baseline.json` modifications exist. Test count holds at 282/1/0. All 5 plan SUMMARYs are committed.

Two minor format deviations from ROADMAP SC2 wording are noted but do not constitute gaps:
- Multiturn artifact format is `meta` + `transcript.log` per trial (not `.jsonl` files); data content is equivalent.
- Needle latency field is `elapsed_s` (not `latency_ms`); a numeric latency value is present.

---

_Verified: 2026-04-28T11:00:00Z_
_Verifier: Claude (gsd-verifier)_
