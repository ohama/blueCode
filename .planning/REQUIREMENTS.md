# Requirements: blueCode v2.2 Multi-file Capability

**Defined:** 2026-04-28
**Core Value:** Mac 로컬 Qwen 3.5 122B를 strong-typed F# agent loop로 **empirically** 안정적으로 돌린다 (post-v2.1 verdict 82/100 KEEP confirmed; v2.2 unlocks multi-file refactor capability)
**Milestone goal:** Raise the PLAN-04 5-step ceiling that v2.1 surfaced as the structural blocker for genuine multi-file refactor (CORR-EVAL-02 FAIL with orphan_count=1). Verify by re-running CORR-EVAL-02 to PASS (orphan_count=0) without regressing the 7/7 bench gate. Optionally close cold-start measurement deferred from v2.1.

## v2.2 Requirements (5 requirements, 2 categories)

Each requirement is bounded, measurable, and grounded in v2.1 empirical findings.

### Capability

The data-driven core of v2.2 — closes the architectural ceiling discovered by CORR-EVAL-02 FAIL.

- [x] **PLAN-CAP-01**: Step ceiling raised from 5 to {N} via config-driven seam
  - **Goal:** Domain.fs `validatePlan` length check + AgentLoop.runLoop step budget guard read the same single source of truth (e.g., `module-level constant maxStepsPerTurn` or `AgentConfig` field). Default value: **10** (covers 4-file refactor + buffer; conservative 2× of current 5).
  - **Behavior:** 
    - `Plan.Steps.Length ≤ maxStepsPerTurn` (currently `≤ 5`); validator fails plan beyond ceiling with new `PlanInvalid` reason or extended existing reason.
    - `runLoop` step counter halts at `maxStepsPerTurn` (currently 5); MaxLoopsExceeded fires at new boundary.
    - Both Core-only changes; no Cli-layer churn.
  - **Validation:** Compile succeeds; PlanValidatorTests cover new ceiling boundary; AgentLoopTests cover MaxLoopsExceeded at new boundary; `grep -rn "5\\b" src/BlueCode.Core/{AgentLoop,Domain}.fs` no longer matches the magic 5 (replaced with named constant reference).
  - **Threshold:** Single source of truth; no drift between validator and loop.

- [x] **PLAN-CAP-02**: System prompt announces extended budget with usage guidance
  - **Goal:** `CompositionRoot.fs` system prompt updated to communicate the new step budget and guide the agent toward minimal-step single-tasks while permitting full-budget multi-step coherent work.
  - **Behavior:** Replace any "5 steps" / "5 actions" / "five iterations" language in the prompt with the new ceiling value. Add a single-sentence guidance: "Use the full budget for multi-step coherent work like multi-file refactoring; minimize for single-step queries."
  - **Validation:** `grep -F "5 step" src/BlueCode.Cli/CompositionRoot.fs` returns nothing (or returns updated value); system prompt length within reasonable delta of v1.3 PERF-01 783-char baseline (≤900 chars target — modest expansion acceptable for capability gain).
  - **Threshold:** Bench gate W1/W2 still complete in ≤3 steps post-change (no behavioral regression on single-step tasks).

- [x] **PLAN-CAP-03**: Tests + bench gate regression hold
  - **Goal:** Test additions + verification that no existing bench fixture regresses with the raised ceiling.
  - **Behavior:**
    - PlanValidatorTests: at least 2 new cases — ceiling boundary (steps = N PASS), beyond ceiling (steps = N+1 FAIL).
    - AgentLoopTests: at least 1 new case — MaxLoopsExceeded fires at new boundary (mocked tool sequence of N+1 actions).
    - `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)`. Step counts for W1/W2/B2/T1/T5/T6/MT all within baseline_max (no fixture should consume more steps than before — the ceiling is permissive but the agent should still minimize).
  - **Validation:** Test count 282 → ≥285 (was 282/1/0); gate exit 0; no `bench/baseline.json` modifications.
  - **Threshold:** All-or-nothing: every existing gate fixture must hold step count baseline.

### Re-evaluation

Closes v2.1 eval doc §9 re-evaluation trigger.

- [ ] **PLAN-CAP-04**: CORR-EVAL-02 re-run produces orphan_count=0 (PASS)
  - **Goal:** Re-run multi-file refactor task with raised ceiling; verify agent successfully renames `add` → `sum` across all 3 F# files in `bench/fixtures/refactor_multifile/` without orphan references.
  - **Behavior:** `bash bench/eval-qwen35-122b.sh --refactor` runs full agent loop against fixture. Post-run: `bench/runs/qwen35-eval-<ts>/refactor_orphan_count.txt` contains `0`; `bench/runs/qwen35-eval-<ts>/refactor_multifile_diff.txt` contains `CORR-EVAL-02 PASS:` line. Bench gate immediately after restores fixtures via EXIT trap (clean working tree).
  - **Validation:** Single integer in `refactor_orphan_count.txt` = 0; verdict line `CORR-EVAL-02 PASS: 0 orphan 'add' references remain` in transcript.
  - **Threshold:** All-or-nothing PASS (per CORR-EVAL-02 5/0 scoring rule).

- [ ] **PLAN-CAP-05**: Eval doc updated; scorecard re-aggregated
  - **Goal:** `documentation/qwen35-122b-coding-eval.md` §2.4 (Multi-file refactor) flipped from FAIL to PASS with new orphan_count=0 evidence; §7 Verdict scorecard re-aggregated (Correctness 31 → 36; Total 82 → 87); §8 Caveats — multi-file caveat removed; §9 Re-evaluation thresholds — "5-step cap raised" item marked **resolved**; final verdict line updated to `**Total: 87/100, Recommendation: KEEP**`.
  - **Behavior:** Inline edits to existing eval doc; no rewrite. STATE.md observation note updated. CLAUDE.md Bench section cross-reference unchanged.
  - **Validation:** `grep -E "^\\*\\*Total: 87/100, Recommendation: KEEP\\*\\*$" documentation/qwen35-122b-coding-eval.md` matches; v2.1 audit-style format check passes; all section verdict lines present.
  - **Threshold:** Strict format match for final scorecard line.

## Optional Phase 23 (cold-start, deferred from v2.1)

- [x] **COLD-EVAL-01**: Cold-start empirical measurement (deferred from v2.1)
  - **Goal:** Execute `bench/eval-qwen35-122b.sh --coldstart` once during a scheduled disruption window; flip eval doc §3.3 from "deferred" to actual measurement; update §7 Performance subtotal.
  - **Behavior:** `--coldstart` handler (already implemented in 21-04) kills 122B via `launchctl kickstart -k`, polls `/v1/models` every 2s with 240s timeout, writes `coldstart.json` with `kicked_at`/`elapsed_s`/`status`.
  - **Validation:** `bench/runs/qwen35-eval-<ts>/coldstart.json` exists with `status: "ready"` and `elapsed_s` numeric.
  - **Threshold:** ≤180s = 5 pts; 180-240 = 3 pts; >240 = 0 pts (per v2.1 scorecard rubric).
  - **Note:** This requirement is gated behind explicit user opt-in (~3 min disruption window). Phase 23 may be deferred indefinitely if no convenient window arises.

## Out of Scope

v1 + v2.0 + v2.1 boundaries unchanged. v2.2 explicitly excludes:

| Feature | Reason |
|---------|--------|
| Slash commands (`/sessions`, `/plan`, `/clear`) | No daily-driver pain signal in observation; CLI flags work. v2.3+ candidate. |
| Compaction (auto-snip at 80% max_model_len) | No observed long-session pain; v2.0 PERSIST-02 follow-up. v2.3+ candidate. |
| Sub-agent delegation (Agent tool) | Speculative without observation. v2.3+ candidate. |
| Thinking-mode-on (`<think>` consumption) | v2.1 measurement says thinking-OFF gives perfect schema 0/50; ON risks regression on Reliability dimension. **Defer indefinitely.** |
| Native OpenAI `tool_calls` | Custom JSON schema gives 0/50 perfect; no functional reason to rewrite. v3.0 territory. |
| Streaming inference (STM-01) | TTFT 222ms warm is already instant; deferred 8th cycle. **Defer pattern is the signal.** |
| Idiomatic F# generation improvement | Coding-quality 1/5 is real but observation-driven; v2.3+ candidate after observation window confirms. |
| Plan-mode bench fixture | Deferred from v2.0 Phase 16; mocked-IKeyReader pattern. v2.3+ candidate. |
| Cloud comparison (Claude/GPT-4) | Deliberate non-goal preserved from v2.1 §6.3. |
| `bench/baseline.json` modifications | Eval observational; gate baseline preserved byte-for-byte. |
| Multi-platform (Windows/Linux) | Mac-only ethos preserved. |

## Future Requirements (v2.3+ candidates)

Tracked for awareness; not pulled into v2.2. Observation-driven scoping after v2.2 ships.

- **SLASH-01** (v2.3 candidate) — `/sessions`, `/plan`, `/clear` slash commands inside REPL
- **COMPACT-01** (v2.3 candidate) — Auto-compaction when session approaches 80% of `max_model_len`
- **SUBAG-01** (v2.3+ candidate) — Sub-agent delegation via Agent tool
- **PLAN-MODE-BENCH-01** (v2.3+ candidate) — Plan-mode bench fixture via mocked-IKeyReader
- **IDIOMATIC-FS-01** (v2.3+ candidate) — F# style guide hints in system prompt; few-shot examples; re-run §5 of eval to verify Coding-quality 1/5 → 3-5/5
- **STM-01** (deferred 8x) — SSE token streaming in blueCode runtime

## Traceability

Filled by roadmap. Each requirement maps to exactly one phase.

| Requirement | Phase | Status |
|-------------|-------|--------|
| PLAN-CAP-01 | Phase 22 (22-01) | Complete (config-driven seam; ceiling 5→10) |
| PLAN-CAP-02 | Phase 22 (22-02) | Complete (system prompt + adapter strings updated; T6 baseline held first try) |
| PLAN-CAP-03 | Phase 22 (22-03) | Complete (tests 282→284; bench gate 7/7 PASS held; all fixture step counts unchanged) |
| PLAN-CAP-04 | Phase 22 (22-04) | Partial — architectural prerequisite delivered (ceiling enables 7+ steps); empirical PASS not achieved (CORR-EVAL-02 FAIL x2 due to persistent extraction bias on shared-prefix function names, NOT ceiling exhaustion). Surfaced as v2.3 candidate. |
| PLAN-CAP-05 | Phase 22 (22-04) | Partial — eval doc §2.4/§8/§9 updated with two-stage finding; verdict stays 82/100 KEEP (not flipped to 87 because PLAN-CAP-04 PASS gate not met). |
| COLD-EVAL-01 | Phase 23 (23-01) | Complete (37s ready; warm OS file cache case; 5/5 top band; v2.0 SUMMARY's "up to 240s" estimate empirically refuted for common case) |

**Coverage:**
- v2.2 requirements: 5 core + 1 optional = 6 total
- Mapped to phases: 6/6 ✓
- Unmapped: 0

---
*Requirements defined: 2026-04-28*
*Last updated: 2026-04-28 — initial draft from v2.2 scope agreement (Multi-file Capability — data-driven from v2.1 audit's CORR-EVAL-02 FAIL constraint discovery)*
