---
phase: 18-single-model-eval
verified: 2026-04-27T04:40:01Z
status: passed
score: 14/14 must-haves verified
---

# Phase 18: Single-Model 122B Evaluation — Verification Report

**Phase Goal:** Decide whether 35B can be dropped and 122B alone serves as the canonical model for
blueCode, with empirical evidence on latency, quality, and memory across the bench's task spectrum.

**Verified:** 2026-04-27T04:40:01Z
**Status:** PASSED
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | 35B unloaded via launchctl (not killed); port 8000 dead | VERIFIED | `launchctl list` shows only `com.ohama.qwen122b`; `lsof -iTCP:8000 -sTCP:LISTEN` empty; 18-01-MEMORY-PROFILE.md line 37 records `launchctl unload ~/Library/LaunchAgents/com.ohama.qwen35b.plist` |
| 2 | Pre/post memory snapshots captured with ≥5 GB PhysMem unused increase | VERIFIED | PhysMem unused: 1.58 GB → 21 GB (+19.42 GB); well above 5 GB threshold; 122B RSS stable at 45.42 GB (≤50 GB) |
| 3 | Bench harness script exists, parses clean, ≥31 invocations, routes only to 122B | VERIFIED | `scripts/bench-122b-only.sh` is `-rwxr-xr-x`, `bash -n` PASS, `MODEL="72b"` set, 54 `"$MODEL"` call sites, 31 meta files in bench run dir all with `exit=0` |
| 4 | ≥30 bench invocations ran; ≤1 LlmUnreachable failure; B2 DivByZero diagnosis preserved | VERIFIED | 31 `.meta` files; 31/31 `exit=0` (zero failures); DivByZero text found in b2_122b.log and diagnose_B2_122b.log |
| 5 | Decision document exists ≥150 lines with literal "Decision", per-test comparison table, 5-criterion matrix with explicit PASS/FAIL, named verdict | VERIFIED | `documentation/single-model-eval.md` is 265 lines; "Decision" appears 6×; "DROP-35B" appears 15×; 5-row decision matrix rows 141–145 each show explicit PASS; VERDICT section at line 154 |
| 6 | T1/T2 latency ≤6s; T6/W1/W2/B2 step counts ≤ baseline_max | VERIFIED | T1=3s, T2=3s (≤6s); T6=4 steps (baseline_max=5); W1=3, W2=3, B2=2 (all ≤ baseline_max) |
| 7 | No changes to CLAUDE.md, bench/run.sh, bench/baseline.json, or src/ in this phase | VERIFIED | `git diff 6b88084..HEAD` on each path = 0 diff lines |
| 8 | Conditional follow-ups enumerated but NOT executed (§SC5) | VERIFIED | `documentation/single-model-eval.md` §"Conditional follow-ups" lists 5 deferred items (Router collapse, baseline halve, CLAUDE.md update, script disposition, Phase 16 implications) with explicit "NOT executed in 18-03" |

**Score:** 8/8 truths verified (14/14 individual must-have checks)

---

## Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `.planning/phases/18-single-model-eval/18-01-MEMORY-PROFILE.md` | ≥60 lines, contains "PhysMem unused" | VERIFIED | 162 lines; "PhysMem unused" appears 10× |
| `.planning/phases/18-single-model-eval/18-02-BENCH-RESULTS.md` | ≥80 lines | VERIFIED | 202 lines |
| `scripts/bench-122b-only.sh` | Exists, executable, valid bash, `MODEL="72b"`, ≥31 invocation sites | VERIFIED | `-rwxr-xr-x`, `bash -n` PASS, `MODEL="72b"` present, 54 `"$MODEL"` call sites |
| `documentation/single-model-eval.md` | ≥150 lines, "Decision", "DROP-35B", 5-criterion matrix, named verdict | VERIFIED | 265 lines; "Decision"×6, "DROP-35B"×15; 5-row matrix with PASS on each row; VERDICT: DROP-35B |
| `bench/runs/122b-only-20260427-131515/` | ≥30 `.meta` files, all `exit=0` or ≤1 failure, DivByZero in b2 log | VERIFIED | 31 `.meta` files; 31/31 `exit=0`; DivByZero text confirmed in b2_122b.log |

---

## Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| `bench-122b-only.sh` `--all` invocation | port 8001 (122B) | `MODEL="72b"` → `--model "$model"` in `run()` | VERIFIED | All 31 meta files show `model=72b` |
| Memory profile (18-01) | Decision matrix (18-03) | SC4 criterion rows in `single-model-eval.md` §135–145 | VERIFIED | Rows 141–145 cite "from §18-01 + §18-02" with exact observed values matching 18-01 §3 table |
| Bench results (18-02) | Decision matrix (18-03) | SC4 latency + step-count rows | VERIFIED | T1=3s, T2=3s, T6=4, W1=3, W2=3, B2=2 all appear in both 18-02 and 18-03 decision matrix |
| Phase verdict (DROP-35B) | Service state | `launchctl unload` executed before bench; 35B stays unloaded | VERIFIED | Live: only `com.ohama.qwen122b` in launchctl list; port 8000 dark |
| Conditional follow-ups | Deferred (not executed) | Enumerated in `single-model-eval.md` §"Conditional follow-ups" | VERIFIED | 5 items listed, each marked NOT executed; git diff confirms no src/CLAUDE.md/bench changes |

---

## ROADMAP Success Criteria Coverage

| SC | Criterion | Status | Evidence |
|----|-----------|--------|----------|
| SC1 | `documentation/single-model-eval.md` ≥150 lines, "Decision", per-test table, decision matrix, named verdict | PASS | 265 lines; all structural elements present; verdict = DROP-35B |
| SC2 | 35B unloaded via `launchctl unload`; memory snapshot pre/post | PASS | Line 37 of 18-01 records exact command; pre/post tables in §1 and §3 |
| SC3 | Bench equivalent to `--all` against port 8001 only, ≥30 invocations | PASS | 31 invocations; all `model=72b`; `bench/runs/122b-only-20260427-131515/` |
| SC4 | Latency and step-count thresholds applied; decision criteria explicit | PASS | 5/5 criteria PASS; T1=3s, T2=3s ≤6s; all step counts ≤ baseline_max |
| SC5 | If DROP-35B: architectural changes enumerated but NOT executed | PASS | 5 deferred items named; git diff confirms no code/bench/CLAUDE.md changes |

---

## Anti-Patterns Found

None. This is an operations phase producing documentation and a bench harness. No placeholder patterns
detected. No TODO/FIXME in the produced artifacts. Decision matrix entries are concrete (not "TBD").

---

## Human Verification Required

None. All success criteria are empirically verifiable from artifacts on disk and live service state.

The bench results are pre-computed data in `.meta` files and logs — their correctness was established
during the bench run. The decision document records exact numbers matching the meta files. No
subjective assessment is required.

---

## Summary

Phase 18 achieved its goal. The decision document exists with 265 lines of empirical evidence,
a 5-criterion decision matrix (5/5 PASS), and a named DROP-35B verdict. The bench ran 31
invocations (all exit=0) against 122B alone, with B2 DivByZero diagnosis preserved. Memory freed
by 35B unload (+19.42 GB PhysMem unused) and 122B RSS stability (45.42 GB, unchanged) are
documented. No code, CLAUDE.md, bench/run.sh, or bench/baseline.json changes were made — all
architectural follow-ups are enumerated and explicitly deferred to a subsequent phase.

---

_Verified: 2026-04-27T04:40:01Z_
_Verifier: Claude (gsd-verifier)_
