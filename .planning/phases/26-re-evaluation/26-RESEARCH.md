# Phase 26: Re-Evaluation — Research

**Researched:** 2026-04-29
**Domain:** Empirical re-evaluation (eval harness, eval doc editing, STATE.md close pattern)
**Confidence:** HIGH — all 9 questions answered from direct source inspection; no external research needed

---

## Summary

Phase 26 is a verification-and-documentation phase, not a code change phase. The single plan (26-01) has three concerns: (1) run `bash bench/eval-qwen35-122b.sh --refactor` with all 3 prongs in place and confirm `refactor_orphan_count.txt` = 0, (2) edit 9 specific sites in `documentation/qwen35-122b-coding-eval.md` to flip the verdict from 87 to 92, and (3) update STATE.md + run the mandatory bench gate.

All mechanics are fully wired in existing infrastructure. The `--refactor` handler already produces `refactor_orphan_count.txt` and `refactor_multifile_diff.txt` with the exact PASS/FAIL verdict line. The README fixture is confirmed at the 2128-char v2.2 enumerated version. The 9 edit sites in the eval doc are mapped to exact line numbers. The STATE.md update pattern matches the phase-complete pattern used in Phase 25.

**Primary recommendation:** Execute 26-01 as a single plan: pre-flight git checkout, --refactor run (up to 3 stochastic attempts allowed), 9-site eval doc edit, STATE.md update, bench gate. No code changes; `git diff src/` must be empty after Phase 26.

---

## Q1: Exact line numbers for the 9 eval doc edit sites

File: `documentation/qwen35-122b-coding-eval.md` (1033 lines total)

### Edit site 1 — §2.4 verdict line (line 282)

```
Line 282: §2.4 verdict: FAIL — orphan_count=1 (persistent extraction bias on shared-prefix function names; v2.2 ceiling raise revealed comprehension layer as new bottleneck). Score: **0/5**.
```

Replace with PASS verdict citing orphan_count=0, v2.3 multi-prong intervention, run timestamp, new score 5/5. The entire §2.4 body (lines 245-284) needs updating: artifact reference switches from `1` to `0`, post-refactor file state becomes all-renamed, the two-stage failure history stays (historical context) but the final verdict line flips.

Minimum change: replace line 282 (verdict) + lines 249-252 (artifact block showing `1`) + lines 254-257 (post-refactor file state showing failures) + line 286 (`§2 total: 15 + 11 + 5 + 0 = 31/40`).

### Edit site 2 — §2.4 artifact block (lines 249-252)

```
Lines 249-252:
`bench/runs/qwen35-eval-20260428-093852/refactor_orphan_count.txt`:
```
1
```
```

Replace with the new run timestamp and `0`.

### Edit site 3 — §2.4 post-refactor file state (lines 254-257)

```
Lines 254-257 describe Calculator.fs `add3` renamed but `add` remaining; Main.fs/Tests.fs unchanged.
```

Replace with all-renamed description: Calculator.fs defines `sum` and `sum3`, Main.fs and Tests.fs updated.

### Edit site 4 — §2 total line (line 286)

```
Line 286: **§2 total: 15 + 11 + 5 + 0 = 31/40**
```

Replace with: `**§2 total: 15 + 11 + 5 + 5 = 36/40**`

### Edit site 5 — §7 Multi-file refactor row (line 850)

```
Line 850: | Correctness | Multi-file refactor (all-or-nothing) | 0 | 5 |
```

Replace with: `| Correctness | Multi-file refactor (all-or-nothing) | 5 | 5 |`

### Edit site 6 — §7 Correctness subtotal row (line 851)

```
Line 851: | **Correctness subtotal** | | **31** | **40** |
```

Replace with: `| **Correctness subtotal** | | **36** | **40** |`

### Edit site 7 — §7 Dimension coverage check table Correctness row (line 870)

```
Line 870: | Correctness | 31/40 | 77.5% | YES |
```

Replace with: `| Correctness | 36/40 | 90.0% | YES |`

### Edit site 8 — §7 Applying rules Grand total line (line 881)

```
Line 881: - Grand total: 31 + 25 + 25 + 6 = **87/100** → ≥80 band
```

Replace with: `- Grand total: 36 + 25 + 25 + 6 = **92/100** → ≥80 band`

### Edit site 9 — §8 Caveat 6 (lines 913-919)

```
Lines 913-919: "Multi-file refactor: two-stage finding (v2.1 + v2.2). v2.1 hypothesized ...
comprehension layer is the new bottleneck. v2.3 candidate."
```

Replace this caveat with a v2.2 → v2.3 progression note: the two-stage finding is historical context; v2.3 multi-prong intervention (P1 system prompt enumeration + P2 few-shot + P3 PlanValidator pre-flight) resolved the bias; CORR-EVAL-02 PASS verified in Phase 26.

### Edit site 10 — §9 Item 8 (lines 953-957)

```
Lines 953-957: "8. **Comprehension layer fix attempts (v2.3 candidate)** — if a multi-prong
intervention is shipped...the persistent extraction bias on shared-prefix function names is
the load-bearing measurement target for any such intervention."
```

Mark this item **resolved**: the multi-prong intervention shipped in v2.3 (Phase 24 P1+P2, Phase 25 P3), CORR-EVAL-02 re-run PASS, orphan_count=0. Note: this item is numbered 8 but appears after item 7 in the doc (ordering anomaly: items run 1,2,3,4,5,6,8,7 — the "8" in line 953 is out of sequence). Preserve the numbering to avoid churn.

### Edit site 11 — Final scorecard line (line 1032)

```
Line 1032: **Total: 87/100, Recommendation: KEEP**
```

Replace with: `**Total: 92/100, Recommendation: KEEP**`

This is the strict-format validation target. The regex `grep -E "^\*\*Total: 92/100, Recommendation: KEEP\*\*$" documentation/qwen35-122b-coding-eval.md` must match.

**Summary table:**

| # | Line(s) | What changes |
|---|---------|-------------|
| 1 | 282 | §2.4 verdict line: FAIL → PASS; Score 0/5 → 5/5 |
| 2 | 249-252 | §2.4 artifact block: run timestamp + `1` → new run + `0` |
| 3 | 254-257 | §2.4 post-refactor file state: failures → all-renamed |
| 4 | 286 | §2 total: `0 = 31/40` → `5 = 36/40` |
| 5 | 850 | §7 Multi-file refactor score: `0` → `5` |
| 6 | 851 | §7 Correctness subtotal: `**31**` → `**36**` |
| 7 | 870 | §7 Dimension coverage Correctness: `31/40 | 77.5%` → `36/40 | 90.0%` |
| 8 | 881 | §7 Grand total: `31 + 25 + 25 + 6 = **87/100**` → `36 + 25 + 25 + 6 = **92/100**` |
| 9 | 913-919 | §8 Caveat 6: two-stage-finding v2.3-candidate → v2.3 resolution note |
| 10 | 953-957 | §9 Item 8: v2.3 candidate → **resolved** |
| 11 | 1032 | Final line: `87/100` → `92/100` |

Note: the ROADMAP and context say "9 edit sites" — the 11 items above collapse to 9 logical groups: §2.4 body (3 sub-edits count as 1 section), §2 total, §7 row, §7 subtotal, §7 coverage table, §7 grand total, §8 caveat 6, §9 item 8, final line.

---

## Q2: `--refactor` flag handling and output files

Source: `bench/eval-qwen35-122b.sh` lines 259-312 (`run_refactor` function).

**What `--refactor` does:**
1. Calls `require_port_8001` (aborts if 122B unreachable)
2. Creates `LOG_DIR="bench/runs/qwen35-eval-$(date +%Y%m%d-%H%M%S)"`
3. Builds prompt: `"Read bench/fixtures/refactor_multifile/README.md and perform the refactor task it describes. Modify the files in bench/fixtures/refactor_multifile as needed. Do not modify the README.md."`
4. Runs: `dotnet run --project src/BlueCode.Cli -- --verbose --model 122b "$prompt"` (with `set +e` so MaxLoopsExceeded exit=1 doesn't abort harness)
5. Captures post-refactor state of all 3 .fs files into `refactor_multifile_diff.txt`
6. Runs orphan check: `grep -cE '\b(let |Calculator\.)add\b'` across all 3 files; sums counts
7. Writes count to `$LOG_DIR/refactor_orphan_count.txt` (single integer, no newline trailing)
8. Emits PASS or FAIL verdict line to `$LOG_DIR/timeline.txt` + `$LOG_DIR/refactor_multifile_diff.txt`

**PASS verdict line (line 308):** `"CORR-EVAL-02 PASS: 0 orphan 'add' references remain"`
**FAIL verdict line (line 303):** `"CORR-EVAL-02 FAIL: $orphan_count orphan 'add' references remain after refactor"`

**Output files produced:**
- `bench/runs/qwen35-eval-<ts>/refactor_orphan_count.txt` — single integer `0` or `1`+
- `bench/runs/qwen35-eval-<ts>/refactor_multifile_diff.txt` — full transcript + file states + verdict line
- `bench/runs/qwen35-eval-<ts>/refactor_multifile.meta` — one-line summary with exit code, elapsed, orphan count
- `bench/runs/qwen35-eval-<ts>/timeline.txt` — running log (shared with any other modes)

**Critical detail:** The orphan check regex is `\b(let |Calculator\.)add\b` — this matches `let add` or `Calculator.add` as word-boundary-anchored patterns. It does NOT match `add3` (the `\b` after `add` prevents matching when followed immediately by `3`). So a PASS requires all of: `let add` gone from Calculator.fs, `Calculator.add` gone from Main.fs and Tests.fs.

**The check fires BEFORE bench/run.sh EXIT trap.** The harness captures orphan count while files are still in refactored state. The subsequent bench gate call restores fixtures via the EXIT trap — after which the orphan count would read nonzero again. This sequencing is already correct in the harness.

---

## Q3: 2128-char README confirmed on disk

Confirmed: `bench/fixtures/refactor_multifile/README.md` is at 2226 bytes (wc -c), 2128 characters (multi-line content). This is the v2.2 Phase 22-04 enumerated rewrite with:
- Two named rename sections (§Rename 1, §Rename 2)
- Completion checklist with grep-based self-verification commands
- Explicit warning: "Both renames are required. Completing only one... is a FAIL."

The 3 F# files (`Calculator.fs`, `Main.fs`, `Tests.fs`) are at canonical state (pre-refactor): Calculator.fs has `let add` and `let add3`; Main.fs calls `add 2 3` and `add3 1 2 3`; Tests.fs calls `add 2 3` and `add3 1 2 3`.

**Pre-flight command to confirm canonical state:**
```bash
git checkout -- bench/fixtures/refactor_multifile/
git diff bench/fixtures/refactor_multifile/  # must be empty
```

---

## Q4: Current eval doc final scorecard line format

Line 1032 (last line of file):
```
**Total: 87/100, Recommendation: KEEP**
```

No trailing newline visible. The replacement must be exactly:
```
**Total: 92/100, Recommendation: KEEP**
```

Validation command: `grep -E "^\*\*Total: 92/100, Recommendation: KEEP\*\*$" documentation/qwen35-122b-coding-eval.md` — must return 1 match.

---

## Q5: Fixture files confirmed (3 F# files)

`bench/fixtures/refactor_multifile/` contains exactly:
- `Calculator.fs` — module with `let add` (2-arg) and `let add3` (3-arg)
- `Main.fs` — entry point calling `add 2 3` and `add3 1 2 3`
- `Tests.fs` — test functions `testAdd` and `testAdd3`
- `README.md` — task description (do NOT modify)

All 3 F# files are in canonical pre-refactor state.

---

## Q6: STATE.md update pattern

From Phase 25-03-PLAN.md and the resulting STATE.md (confirmed via direct read), the Phase-complete STATE.md update has this structure:

**Fields to update:**
- `## Current Position` / Phase line: `26` → `26 complete` (or advance to "milestone close")
- `## Current Position` / Status: add "Phase 26 complete. CORR-EVAL-02 PASS (orphan_count=0). Eval doc 87→92/100. v2.3 milestone close-ready."
- `## Current Position` / Last activity: `2026-04-29 — Phase 26 ...`
- `## Accumulated Context` / Decisions: add Phase 26 empirical finding
- `## Performance Metrics` does NOT change for Phase 26 (this stays cumulative + frozen per phase at milestone close)

**v2.3 milestone close-readiness signals** (what `/gsd:complete-milestone` needs to see in STATE.md):
- Phase 26 marked complete in Current Position
- CORR-EVAL-02 PASS evidence noted
- Eval doc verdict 87→92 noted
- v2.3 milestone noted as close-ready (current milestone = v2.3)
- Progress line updated: `Phase 26 ✓` appended to v2.3 section

Pattern from Phase 25: the "Last activity" line gets a descriptive sentence; the "Status" line moves to "Phase 26 complete"; the Decisions block gets a one-liner capturing the empirical finding.

---

## Q7: Estimated wall-clock for `--refactor` invocation

From `documentation/qwen35-122b-coding-eval.md` §10:
- `--refactor` listed as "1 session; ~20 s"
- v2.1 actual: 17s for 5 steps (read 4 files + write 1 file)
- v2.2 actual: agent used 8/10 steps (FAIL); wall-clock not explicitly recorded but consistent with ~30-50s at 10 steps

With the 10-step ceiling and v2.3 intervention, a PASS requires minimum 7 steps (read README + read 3 files + write 3 files) = ~7 × 5-8s/step = ~35-56s. Budget 90s per attempt to be safe.

`dotnet run` cold compilation adds ~15-30s on first run if the project hasn't been built recently. After first invocation, subsequent runs in the same session skip compilation.

**Bench gate (`--gate`):** ~2 min wall-clock (7 invocations, ~15-17s each from Phase 25 VERIFICATION.md evidence: T6=17s, W1=9s, W2=10s, T1=3s, T5=7s, B2=7s, MT=9s → total ~62s).

**Total Phase 26 wall-clock estimate:**
- Pre-flight git checkout: <5s
- `dotnet build` (if needed) or first `--refactor` with compile: 15-30s startup overhead
- `--refactor` invocation: 60-90s
- Eval doc edits (11 sites): ~10 min manual editing
- Bench gate: ~2 min
- STATE.md + commit: ~5 min
- **Total: ~20-25 min for a clean PASS on first attempt**

If stochastic re-runs needed: add ~2 min per additional attempt (git checkout + rerun). Up to 3 allowed.

---

## Q8: Stochastic variance risk

**Eval sampling is temperature=0.2** (harness hardcoded, line 62 of eval-qwen35-122b.sh: `temperature: 0.2`). At temp=0.2 variance is low, but not zero. v2.1 FAIL was deterministic (same step-5 thought twice across completely different READMEs). With all 3 prongs now in place, PASS is expected but not guaranteed on first attempt.

**If FAIL on attempt 1:**
1. Capture `bench/runs/qwen35-eval-<ts>/refactor_multifile_diff.txt` — read step trace carefully
2. Record the step-5 thought verbatim in a note (did it enumerate both targets?)
3. Run `git checkout -- bench/fixtures/refactor_multifile/` to restore canonical state
4. Re-run `--refactor` (attempt 2)
5. If FAIL again: capture diff, restore, attempt 3

**If FAIL ≥3 times:**
- Stop. Do NOT modify Phase 24/25 code.
- Document in STATE.md: "Phase 26 FAIL — CORR-EVAL-02 still FAIL after 3 attempts with all prongs. Extraction bias deeper than prompt+validator can fix. v2.4+ investigation required."
- Update ROADMAP §Phase 26 as "FAIL — blocked"
- Do NOT update eval doc scores
- Per REQUIREMENTS.md COMP-05: "If FAIL ≥3 times: STOP and pause for diagnosis"

**Diagnostic info to capture per failed attempt:**
- Did agent's plan enumerate both `add` and `add3` as rename targets? (P1/P2 effect)
- Was PlanValidator's `checkRenameTargetsEnumerated` triggered? (P3 effect) — check for `[PLAN INVALID]` in transcript
- How many steps used? Was it MaxLoopsExceeded or clean exit?
- Which files were edited vs which were skipped?

---

## Q9: v2.3 milestone close-readiness signals for STATE.md

For `/gsd:complete-milestone` to work cleanly, STATE.md needs to signal:

1. **All 3 phases marked complete** in the Progress line: `v2.3 ◆ (Phase 24: 2/2 done ✓; Phase 25: 3/3 done ✓; Phase 26: 1/1 done ✓)`
2. **Current milestone** set to v2.3 (already set; don't change)
3. **Phase 26 empirical finding** in Decisions: "CORR-EVAL-02 PASS (orphan_count=0) verified in Phase 26 re-eval. v2.3 multi-prong intervention (P1 system prompt enumeration + P2 few-shot + P3 PlanValidator pre-flight) resolved shared-prefix extraction bias. Eval doc 87→92/100. v2.3 milestone close-ready."
4. **Status line** updated to: "Phase 26 complete. v2.3 milestone close-ready."
5. **Last activity** date updated: 2026-04-29

The `/gsd:complete-milestone` orchestrator reads STATE.md for current milestone and progress signal. The phrase "close-ready" or "milestone close-ready" in the Status line is the idiomatic signal (confirmed from prior milestones).

---

## Architecture Patterns (for 26-01 plan structure)

Phase 26 has one plan. The plan structure mirrors 25-03 (verification-only plan) but with the `--refactor` invocation as the primary action:

```
Task 1: Pre-flight + CORR-EVAL-02 run
  - git checkout -- bench/fixtures/refactor_multifile/
  - verify canonical state
  - bash bench/eval-qwen35-122b.sh --refactor
  - read refactor_orphan_count.txt
  - if != 0: up to 3 total attempts; stop if 3 FAILs

Task 2: Eval doc edits (11 sites across 9 logical groups)
  - §2.4 body: artifact, file state, verdict line
  - §2 total
  - §7: multi-file row, Correctness subtotal, dimension coverage, grand total
  - §8 caveat 6
  - §9 item 8
  - Final scorecard line
  - Validate: grep -E "^\*\*Total: 92/100, Recommendation: KEEP\*\*$"

Task 3: STATE.md update
  - Current Position / Phase / Status / Last activity
  - Decisions: empirical finding
  - Progress: Phase 26 ✓

Task 4: Bench gate
  - bash bench/run.sh --gate
  - Must exit 0 GATE PASS (7/7)
  - EXIT trap restores fixtures

Task 5: Phase-complete commit
  - git add: 26-01-SUMMARY.md, 26-VERIFICATION.md, ROADMAP.md, REQUIREMENTS.md, STATE.md, documentation/qwen35-122b-coding-eval.md
  - commit: docs(26): complete re-evaluation phase (CORR-EVAL-02 PASS, 87→92)
```

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| Orphan count check | Custom grep script | Existing harness: `bash bench/eval-qwen35-122b.sh --refactor` already does the check |
| Fixture restoration | Manual file restore | `git checkout -- bench/fixtures/refactor_multifile/` (pre-flight) AND bench gate EXIT trap (post-gate) |
| PASS/FAIL verdict line | Custom string format | Harness emits exact string; check `refactor_multifile_diff.txt` for `CORR-EVAL-02 PASS:` |

---

## Common Pitfalls

### Pitfall 1: Reading `refactor_orphan_count.txt` after bench gate fires

The bench gate EXIT trap restores `bench/fixtures/refactor_multifile/` to canonical state. If you run `--refactor` and then immediately run `--gate`, the fixtures are restored BEFORE you read `refactor_orphan_count.txt`. However, the harness writes the count BEFORE exiting, so the file at `bench/runs/qwen35-eval-<ts>/refactor_orphan_count.txt` is safe to read at any time. The issue is if you re-run the orphan grep manually on the fixture files AFTER the gate — they'll show the canonical `add` names again. Always read from the run directory's `.txt` file, not from re-grepping the fixtures.

### Pitfall 2: Forgetting pre-flight git checkout

If fixtures are in a dirty state from a previous failed `--refactor` run (e.g., partially renamed), the new run will start from that dirty state. Always `git checkout -- bench/fixtures/refactor_multifile/` before each `--refactor` invocation.

### Pitfall 3: Strict format on final scorecard line

The final line must be exactly `**Total: 92/100, Recommendation: KEEP**` with no leading/trailing spaces, no trailing newline artifact. Use the grep regex to validate:
```bash
grep -E "^\*\*Total: 92/100, Recommendation: KEEP\*\*$" documentation/qwen35-122b-coding-eval.md
```

### Pitfall 4: §8 caveat 6 note preservation

Caveat 6 contains historical evidence about the two-stage finding. The Phase 26 edit should not delete this history — it should replace the "v2.3 candidate" framing with a "resolved in v2.3" framing. The two-stage finding (v2.1 ceiling block + v2.2 comprehension bias) is still factually accurate and worth keeping as context.

### Pitfall 5: §3.3 cold-start score must not change

§3.3 is at 5/5 from v2.2 Phase 23. §7 Performance subtotal stays at 25/25. Phase 26 only touches the Correctness dimension. Do not accidentally re-aggregate Performance or touch §3.3.

### Pitfall 6: `git diff src/` must be empty

Phase 26 is documentation-only. Before the phase-complete commit, verify `git diff src/` is empty. If any src/ file appears in the diff, stop — something went wrong upstream.

---

## Sources

All findings from direct file inspection (HIGH confidence):

- `bench/eval-qwen35-122b.sh` lines 259-312 — `run_refactor` function, output files, orphan check regex
- `documentation/qwen35-122b-coding-eval.md` lines 245-284, 843-889, 891-960, 1032 — all 11 edit sites
- `bench/fixtures/refactor_multifile/` — README.md (2226 bytes confirmed), Calculator.fs, Main.fs, Tests.fs canonical state
- `.planning/STATE.md` — current state, decisions pattern, progress format
- `.planning/phases/25-plan-mode-pre-flight-enumeration/25-03-PLAN.md` — STATE.md update pattern
- `.planning/phases/25-plan-mode-pre-flight-enumeration/25-VERIFICATION.md` — bench gate evidence format
- `.planning/ROADMAP.md` Phase 26 section — success criteria
- `.planning/REQUIREMENTS.md` COMP-05, COMP-06 — validation commands

---

## Metadata

**Confidence breakdown:**
- Edit site line numbers: HIGH — read directly from file with grep -n + offset reads
- Harness mechanics: HIGH — read run_refactor() function in full
- Fixture canonical state: HIGH — files read directly
- STATE.md update pattern: HIGH — Phase 25 precedent read directly
- Wall-clock estimates: MEDIUM — based on Phase 25 VERIFICATION.md gate evidence + §10 doc comment

**Research date:** 2026-04-29
**Valid until:** Until any refactor fixture, eval harness, or eval doc structure changes (stable for this milestone)
