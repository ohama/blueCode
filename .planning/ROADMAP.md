# Roadmap: blueCode v2.2 Multi-file Capability

**Status:** In Progress (started 2026-04-28)
**Phases:** 22 (and optional 23)
**Milestone goal:** Raise the PLAN-04 5-step ceiling that v2.1 surfaced as the structural blocker for genuine multi-file refactor (CORR-EVAL-02 FAIL with orphan_count=1 — agent burned 5-step budget reading 4 files before completing rename). Verify by re-running CORR-EVAL-02 to PASS (orphan_count=0) without regressing the 7/7 bench gate. Optionally close cold-start measurement deferred from v2.1.

## Overview

v2.2 is a **data-driven, focused milestone** — the first v2.2 candidate surfaced empirically by v2.1 audit (`milestones/v2.1-MILESTONE-AUDIT.md`). Unlike speculative deferred-list candidates (compaction, slash commands, sub-agents, etc.), this milestone closes a measured architectural ceiling that prevents a useful capability (multi-file refactor) from working at all.

The fix is bounded and surgical: change the magic constant `5` in two places (`Domain.fs` `validatePlan` length check + `AgentLoop.runLoop` step counter guard) into a single config-driven seam with default value `10`. Update the system prompt to communicate the new budget. Add tests at the new boundary. Verify the 7/7 bench gate holds (no regression on existing single-step fixtures). Re-run CORR-EVAL-02 to PASS.

Success criterion is pre-defined by v2.1 eval doc §9 ("if 5-step cap is raised, re-run CORR-EVAL-02"). Verdict scorecard expected to flip Correctness 31/40 → 36/40 (Total 82 → 87).

**Approach:** Core-first. v1.1 LlmResponse pattern (single big-bang Core compile cascade in 22-01) + Cli adapter touch in 22-02 + tests in 22-03 + re-eval in 22-04. No new dependencies; no `bench/baseline.json` modifications.

**Phase numbering:** Continues from v2.1's Phase 21. v1.0: 1-5; v1.1: 6-7; v1.2: 8/9/9.1; v1.3: 10-11; v1.4: 12-13; v2.0: 14-20; v2.1: 21. v2.2 uses 22 (and optional 23).

**Bench gate stability mandatory:** `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)` post-each-plan. The raised ceiling is permissive; existing fixtures should still complete in the same step count (W1/W2 in 3 steps, T6 in 3 steps, etc.). If any fixture regresses (consumes more steps post-change), that's a system-prompt issue requiring 22-02 iteration.

---

## Phases

- [ ] **Phase 22: PLAN-04 Ceiling Raise** — 4 plans (Core change → adapter+prompt → tests+gate → re-eval)
- [ ] **Phase 23: Cold-start Empirical** — 1 plan (optional; deferred from v2.1 per scope)

---

## Phase Details

### Phase 22: PLAN-04 Ceiling Raise

**Goal:** Raise the step ceiling from 5 to 10 via a config-driven seam (single source of truth across `validatePlan` + `runLoop`); update system prompt; add boundary tests; verify 7/7 bench gate held; re-run CORR-EVAL-02 producing orphan_count=0 PASS; update eval doc verdict 82 → 87.

**Depends on:** v2.1 milestone (eval doc + bench gate 7/7 baseline + `bench/eval-qwen35-122b.sh --refactor` harness).

**Requirements:** PLAN-CAP-01, PLAN-CAP-02, PLAN-CAP-03, PLAN-CAP-04, PLAN-CAP-05

**Success Criteria** (what must be TRUE when Phase 22 completes):

1. Single source of truth for step ceiling — `Domain.fs` `validatePlan` and `AgentLoop.runLoop` both reference same constant/config (`grep -rn "= 5\\b" src/BlueCode.Core/{AgentLoop,Domain}.fs` no longer matches the magic 5; replaced with named constant reference like `maxStepsPerTurn` or similar). Default value: 10.

2. System prompt communicates new budget — `CompositionRoot.fs` system prompt has no remaining "5 steps" / "5 actions" / "five iterations" language; updated to reflect new ceiling; system prompt length ≤ 900 chars (modest expansion vs v1.3 PERF-01's 783-char baseline acceptable).

3. Test additions — PlanValidatorTests cover boundary (steps = 10 PASS, steps = 11 FAIL); AgentLoopTests cover MaxLoopsExceeded at new boundary (mocked tool sequence of 11 actions). Test count 282 → ≥285.

4. Bench gate regression hold — `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)`. **Critical:** every gate fixture (W1/W2/B2/T1/T5/T6/MT) must complete in the same step count as v2.1 baseline. The raised ceiling is permissive but the agent should still minimize for single-step tasks. If any fixture consumes more steps post-change, that's a system-prompt regression requiring 22-02 iteration.

5. CORR-EVAL-02 PASS — `bash bench/eval-qwen35-122b.sh --refactor` produces `bench/runs/qwen35-eval-<ts>/refactor_orphan_count.txt` containing `0`; `refactor_multifile_diff.txt` contains `CORR-EVAL-02 PASS:` line.

6. Eval doc updated — `documentation/qwen35-122b-coding-eval.md` §2.4 PASS, §7 Verdict scorecard re-aggregated (Correctness 31 → 36; Total 82 → 87), §8 Caveats — multi-file caveat removed, §9 Re-evaluation — "5-step cap raised" item marked **resolved**, final line: `**Total: 87/100, Recommendation: KEEP**`.

**Plans:** 4 plans expected

Plans:
- [ ] 22-01-PLAN.md — Core change (Domain.fs + AgentLoop.fs config-driven step ceiling). Single big-bang compile cascade per v1.1 LlmResponse pattern. Atomic commit. Tests in this plan cover the new boundary at the validator/loop layer (PlanValidatorTests + AgentLoopTests). (PLAN-CAP-01)
- [ ] 22-02-PLAN.md — Adapter change (CompositionRoot.fs system prompt update). Drop "5" language; add new ceiling reference + 1-sentence usage guidance. Test prompt content if any test inspects it. (PLAN-CAP-02)
- [ ] 22-03-PLAN.md — Bench gate regression verification. Run `bash bench/run.sh --gate`; assert exit 0 + 7/7 PASS + step counts unchanged for all 7 fixtures vs v2.1 baseline. If regression: iterate 22-02 prompt. Document final state. (PLAN-CAP-03)
- [ ] 22-04-PLAN.md — Re-run CORR-EVAL-02 + update eval doc + final scorecard. `bench/eval-qwen35-122b.sh --refactor` invoke, verify orphan_count=0, update `documentation/qwen35-122b-coding-eval.md` §2.4/§7/§8/§9 + final scorecard line. STATE.md observation note. (PLAN-CAP-04, PLAN-CAP-05)

**Plan dependencies:**
- 22-01 → 22-02 (system prompt mentions new value; needs 22-01 constant defined)
- 22-02 → 22-03 (gate regression check needs system prompt finalized)
- 22-03 → 22-04 (re-eval only after gate held)

Wave structure: sequential (each plan depends on prior). 4 sequential waves.

**Architectural invariants (load-bearing):**

1. **Core purity preserved**: `Domain.fs` + `AgentLoop.fs` changes only; no Serilog/Spectre/Argu/HttpClient creep. CI grep `scripts/check-no-async.sh` still 0.
2. **`task {}` only in Core** (no `async {}` literal).
3. **Bench gate stability**: `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)` after 22-01, 22-02, 22-03, AND 22-04. Mandatory check at end of each plan.
4. **No `bench/baseline.json` changes**: 7-entry baseline preserved byte-for-byte. The raised ceiling is permissive; existing fixtures should match prior step counts.
5. **`Role = User` invariant** (Phase 20-03): unchanged. Mid-conversation injections (POST-EDIT CONSTRAINT, POST-READ HINT, [PLAN REJECTED]) stay Role=User.
6. **Test count 282 → ≥285**: 3+ new test cases (PlanValidator boundary PASS/FAIL + AgentLoop MaxLoopsExceeded at new boundary). Per CLAUDE.md test discovery rule, ensure new test modules (if any) are added to BOTH `BlueCode.Tests.fsproj` `<Compile Include>` AND `RouterTests.fs` `rootTests` list.
7. **Atomic commits per CLAUDE.md**: 4-5 task commits (`{feat,test,docs}(22-XX): {name}`) plus per-plan plan-meta commits.
8. **Single source of truth**: validator and loop reference the same constant. Drift between them is the bug v2.0 PLAN-04 specifically prevented; preserve that invariant.

**Out-of-scope guardrails (resist scope creep):**

- DO NOT add slash commands (`/sessions`, `/plan`, `/clear`) — out of v2.2 scope; v2.3 candidate
- DO NOT add compaction — v2.3 candidate
- DO NOT add sub-agent delegation — v2.3 candidate
- DO NOT enable thinking-mode — v2.1 data says OFF is correct; ON regresses schema rate
- DO NOT bump `max_tokens` (1024 → 2048-4096) — couples with thinking-on; out of scope
- DO NOT rewrite tool dispatch (native OpenAI `tool_calls`) — v3.0 territory
- DO NOT add streaming output (STM-01) — deferred 8x; defer pattern is the signal
- DO NOT modify `bench/baseline.json` — gate is the regression authority
- DO NOT touch idiomatic F# generation (system prompt F# style hints, few-shot) — observation-driven; v2.3 candidate after observation confirms

**Verdict criteria (Phase 22 success):**

| Sub-criterion | Threshold | Verification |
|---------------|-----------|--------------|
| Single source of truth | grep returns named constant, not magic 5 | Code inspection |
| System prompt updated | No "5 step/action/iteration" language | grep + char count ≤ 900 |
| Tests added | 282 → ≥285; boundary cases cover N+1 FAIL | Test runner output |
| Bench gate held | Exit 0; 7/7 PASS; step counts unchanged | `bench/run.sh --gate` |
| CORR-EVAL-02 PASS | orphan_count=0; PASS line in transcript | `--refactor` artifact inspection |
| Eval doc updated | Total 87/100; final line strict-format | grep + section verdict lines |

---

### Phase 23 (optional): Cold-start Empirical

**Goal:** Execute `bash bench/eval-qwen35-122b.sh --coldstart` once during a scheduled disruption window; flip eval doc §3.3 from "deferred" to actual measurement; update §7 Performance subtotal (currently 0/5 honest deferral; expected 3-5/5 depending on measured cold-start time).

**Depends on:** Phase 22 (eval doc must already be re-aggregated to 87/100); user opt-in for ~3 min disruption window.

**Requirements:** COLD-EVAL-01

**Success Criteria:**

1. `bench/runs/qwen35-eval-<ts>/coldstart.json` exists with `status: "ready"` and `elapsed_s` numeric.
2. Eval doc §3.3 cold-start section flipped from "deferred per scope" to actual measurement; §7 Performance subtotal updated; final scorecard re-aggregated (Total 87 → 88-90 depending on measured time).
3. `bash bench/run.sh --gate` 7/7 PASS post-coldstart (122B service must come back ready and pass gate).
4. STATE.md observation note updated.

**Plans:** 1 plan expected

Plans:
- [ ] 23-01-PLAN.md — Cold-start single execution + eval doc §3.3/§7 update + gate verification post-recovery. (COLD-EVAL-01)

**Plan dependencies:** None internal; depends on Phase 22 completion.

**Architectural invariants:**
1. Service restart MUST recover within 240s (v2.1 timeout). If timeout fires, `coldstart.json` records `status: "timeout"` and Phase 23 documents the failure rather than papering over.
2. Bench gate after recovery confirms 122B is fully ready.
3. No `src/` changes; observational only.

---

## Progress

| Phase | Milestone | Requirements | Plans Complete | Status | Completed |
|-------|-----------|--------------|----------------|--------|-----------|
| 22. PLAN-04 Ceiling Raise | v2.2 | PLAN-CAP-01..05 (5 reqs) | 0/4 | Not started | - |
| 23. Cold-start Empirical (optional) | v2.2 | COLD-EVAL-01 (1 req) | 0/1 | Not started | - |

---

*Roadmap created: 2026-04-28*
*Last updated: 2026-04-28 — initial roadmap from v2.2 scope agreement (data-driven from v2.1 audit's CORR-EVAL-02 FAIL constraint discovery); Phase 22 = ceiling raise (4 plans); Phase 23 = optional cold-start (1 plan); bench gate stability mandatory; eval doc verdict expected to flip 82 → 87 post-Phase-22*
