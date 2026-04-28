# Requirements: blueCode v2.3 Comprehension Layer

**Defined:** 2026-04-28
**Core Value:** Mac 로컬 Qwen 3.5 122B를 strong-typed F# agent loop로 **empirically** 안정적으로 돌린다 (post-v2.2 verdict 87/100 KEEP; v2.3 unlocks multi-file refactor end-to-end via comprehension-layer intervention)
**Milestone goal:** Resolve the persistent extraction bias on shared-prefix function names that v2.2 audit surfaced as the new bottleneck (CORR-EVAL-02 FAIL x2 with textually identical step-5 thoughts across two completely different READMEs). Multi-prong intervention (P1 + P2 + P3) attacking comprehension layer at three angles. Verify by re-running CORR-EVAL-02 to PASS (orphan_count=0); flips Correctness 31/40 → 36/40 → Total 87 → 92.

## v2.3 Requirements (1 candidate, 6 requirements, 2 categories)

The single data-driven candidate (COMP-BIAS-01) is decomposed into 6 atomic requirements grouped by category. P1+P2 are prompt-level (Cli only); P3 is architectural (Core + Cli + tests). All three prongs ship in this milestone.

### Comprehension intervention (4 reqs)

The data-driven core of v2.3 — three independent prongs attacking the persistent extraction bias.

- [ ] **COMP-01**: System prompt instructs agent to enumerate ALL rename targets from spec before editing (P1 prong)
  - **Goal:** Agent's planning step lists every function/symbol to rename BEFORE issuing any edit_file or write_file action. Forces explicit enumeration as part of the thought stream.
  - **Behavior:** `defaultSystemPrompt` or `planSystemPromptSuffix` in `CompositionRoot.fs` augmented with a single-sentence directive: "When the task requires renaming or restructuring multiple symbols, list ALL targets explicitly in your thought before editing. Do not start editing until the full list is enumerated."
  - **Validation:** `grep -F "list ALL targets explicitly" src/BlueCode.Cli/CompositionRoot.fs` matches; system prompt ≤ 1100 chars (vs v2.2's 695-char baseline; modest expansion acceptable for capability gain).
  - **Threshold:** Bench gate W1/W2/B2/T1/T5/T6/MT step counts unchanged (no behavioral regression on single-step tasks).

- [ ] **COMP-02**: Plan-mode prompt includes 1-2 inline few-shot examples of correct multi-file refactor plans (P2 prong)
  - **Goal:** Few-shot examples concretely demonstrate to the model what a correct multi-file rename plan looks like, with all rename targets enumerated. Especially powerful at disambiguating shared-prefix cases (`add` vs `add3`).
  - **Behavior:** `planSystemPromptSuffix` in `CompositionRoot.fs` extended with 1-2 worked examples in compact format. At least one example must explicitly handle a shared-prefix case (e.g., `add` and `add3` both renamed in distinct steps with explicit target lists).
  - **Validation:** `grep -F "Example:" src/BlueCode.Cli/CompositionRoot.fs` shows ≥1 plan-mode example; suffix length ≤ 1500 chars (further expansion acceptable for the few-shot block).
  - **Threshold:** Bench gate held (per COMP-04); plan-mode bench-style fixture (mocked) plays through new examples without breakage.

- [ ] **COMP-03**: Plan validator pre-flight pass checks user-prompt rename targets are enumerated as plan steps (P3 prong)
  - **Goal:** Architectural enforcement at the validator layer. After the LLM emits a plan, the validator extracts probable rename targets from the user prompt (e.g., names following "rename X to Y" or `\bX\b` patterns referenced in the prompt) and verifies the plan's edit_file steps cover all of them. If any target is missing, validator returns `RenameTargetsNotEnumerated` PlanInvalid; 2-attempt retry path handles correction.
  - **Behavior:** New function in `PlanValidator.fs`: `checkRenameTargetsEnumerated: userPrompt: string -> Plan -> Result<Plan, AgentError>`. Heuristic regex extraction of rename targets from user prompt (start conservative — match "rename X to Y" patterns; expand if too brittle). Plan steps' `edit_file` calls' `path` and `old_string`/`new_string` arguments inspected to confirm coverage. New `PlanInvalid` reason: `RenameTargetsNotEnumerated of (string list)`.
  - **Validation:** `grep -n "checkRenameTargetsEnumerated\|RenameTargetsNotEnumerated" src/BlueCode.Core/PlanValidator.fs src/BlueCode.Core/Domain.fs` matches both symbols. New PlanValidatorTests cover: (a) plan covering all targets PASSES; (b) plan missing one target FAILS with named reason; (c) user prompt with no rename targets PASSES (vacuous truth — heuristic returns empty list).
  - **Threshold:** 2-attempt retry path works (existing PLAN-04 retry mechanism extends to this new reason); validator runs in pure-function manner (no I/O in Core).

- [ ] **COMP-04**: Tests + bench gate regression hold across all three prongs
  - **Goal:** All bench fixtures (W1/W2/B2/T1/T5/T6/MT) complete in same step counts as v2.2 baseline; test count grows ≥4 (PlanValidator new cases for P3 + AgentLoop + possible Cli prompt-content tests); bench gate `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)` after each phase.
  - **Behavior:** Sequential verification: after Phase 24 (P1+P2 prompt changes), gate held; after Phase 25 (P3 architectural), gate held + new tests added. If any fixture regresses on step count: iterate the prompt or pre-flight heuristic — DO NOT modify `bench/baseline.json`.
  - **Validation:** Per-fixture step counts stay within baseline_max throughout all 3 phases. Test count 284 → ≥288. `git diff bench/baseline.json` empty post-milestone.
  - **Threshold:** All-or-nothing — every gate fixture must hold step count baseline; test count must increase.

### Re-evaluation (2 reqs)

Closes the v2.2 audit's COMP-BIAS-01 candidate by empirically re-running the same fixture that previously FAIL'd twice.

- [ ] **COMP-05**: CORR-EVAL-02 re-run produces orphan_count=0 (PASS)
  - **Goal:** Re-run multi-file refactor task with all 3 prongs in place; verify agent successfully renames `add` → `sum` AND `add3` → `sum3` across all 3 F# files in `bench/fixtures/refactor_multifile/` without orphan references. Same fixture as v2.2 attempts (rewritten 2128-char README).
  - **Behavior:** Pre-flight `git checkout -- bench/fixtures/refactor_multifile/` to ensure clean state. `bash bench/eval-qwen35-122b.sh --refactor` runs full agent loop. Post-run: `bench/runs/qwen35-eval-<ts>/refactor_orphan_count.txt` contains `0`; `refactor_multifile_diff.txt` contains `CORR-EVAL-02 PASS:` line. Bench gate immediately after restores fixtures via EXIT trap.
  - **Validation:** Single integer in `refactor_orphan_count.txt` = 0; verdict line `CORR-EVAL-02 PASS: 0 orphan 'add' references remain` in transcript.
  - **Threshold:** All-or-nothing PASS. If FAIL on first attempt, document agent's step trace; allow up to 3 re-runs to account for stochastic variance (sampling temp=0.7 introduces some run-to-run variation). If FAIL ≥3 times: STOP and pause for diagnosis (extraction bias may be deeper than prompt+validator can fix; would warrant v2.4+ further work).

- [ ] **COMP-06**: Eval doc updated; scorecard re-aggregated (Total 87 → 92)
  - **Goal:** `documentation/qwen35-122b-coding-eval.md` §2.4 (Multi-file refactor) flipped from FAIL to PASS with new orphan_count=0 evidence; §7 Verdict scorecard re-aggregated (Multi-file refactor row 0/5 → 5/5; Correctness subtotal 31 → 36; Total 87 → 92); §8 Caveats — multi-file caveat replaced with v2.2 → v2.3 progression note; §9 Re-evaluation — "Comprehension layer fix attempts" item marked **resolved**; final verdict line updated to `**Total: 92/100, Recommendation: KEEP**`.
  - **Behavior:** Inline edits to existing eval doc. STATE.md observation note updated. CLAUDE.md Bench section cross-reference unchanged.
  - **Validation:** `grep -E "^\\*\\*Total: 92/100, Recommendation: KEEP\\*\\*$" documentation/qwen35-122b-coding-eval.md` matches; v2.1 audit-style format check passes; all section verdict lines present.
  - **Threshold:** Strict format match for final scorecard line.

## Out of Scope

v1 + v2.0 + v2.1 + v2.2 boundaries unchanged. v2.3 explicitly excludes:

| Feature | Reason |
|---------|--------|
| IDIOMATIC-FS-01 (F# style hints + few-shot for idiomatic F# generation) | Coding-quality 1/5 sub-score may be Python-transcript artifact; needs F# task fixtures + transcript review before drawing conclusions. v2.4+ candidate after observation confirms. |
| COLDSTART-PRISTINE-01 (post-reboot pristine cold-start) | Warm-cache 37s already documented in v2.2; pristine measurement needs scheduled disruption window; low urgency. v2.4+ candidate. |
| Slash commands (`/sessions`, `/plan`, `/clear`) | No daily-driver pain signal; CLI flags work. v2.4+ candidate (observation-driven). |
| Compaction (auto-snip at 80% max_model_len) | No observed long-session pain. v2.4+ candidate. |
| Sub-agent delegation (Agent tool) | Speculative without observation. v2.4+ candidate. |
| Plan-mode bench fixture (mocked-IKeyReader) | Deferred from v2.0 Phase 16; complementary but not load-bearing for COMP-BIAS-01. v2.4+ candidate. |
| Thinking-mode-on (`<think>` consumption) | v2.1 measurement says thinking-OFF gives perfect schema 0/50; ON risks regression. **Defer indefinitely.** |
| Native OpenAI `tool_calls` | Custom JSON schema gives 0/50 perfect; no functional reason to rewrite. v3.0 territory. |
| Streaming inference (STM-01) | TTFT 222ms warm is already instant; deferred 8th cycle. **Defer pattern is the signal.** |
| Cloud comparison (Claude/GPT-4) | Deliberate non-goal preserved from v2.1 §6.3. |
| Re-eval beyond CORR-EVAL-02 | Other dimensions (HumanEval+, throughput, TTFT, schema, multi-turn, needle, cold-start) unchanged from v2.2 baseline; would re-measure only if their constraints shifted. |
| `bench/baseline.json` modifications | Eval observational; gate baseline preserved byte-for-byte. |
| Multi-platform (Windows/Linux) | Mac-only ethos preserved. |

## Future Requirements (v2.4+ candidates)

Tracked for awareness; not pulled into v2.3. Observation-driven scoping after v2.3 ships.

- **IDIOMATIC-FS-01** (medium) — F# style guide hints in system prompt; few-shot examples; re-run §5 of eval to verify Coding-quality 1/5 → 3-5/5
- **COLDSTART-PRISTINE-01** (low) — Post-reboot pristine cold-start measurement
- **SLASH-01** (v2.4+ candidate) — `/sessions`, `/plan`, `/clear` slash commands inside REPL
- **COMPACT-01** (v2.4+ candidate) — Auto-compaction when session approaches 80% of `max_model_len`
- **SUBAG-01** (v2.4+ candidate) — Sub-agent delegation via Agent tool
- **PLAN-MODE-BENCH-01** (v2.4+ candidate) — Plan-mode bench fixture via mocked-IKeyReader
- **STM-01** (deferred 9x) — SSE token streaming in blueCode runtime

## Traceability

Filled by roadmap. Each requirement maps to exactly one phase.

| Requirement | Phase | Status |
|-------------|-------|--------|
| COMP-01 | Phase 24 | Pending |
| COMP-02 | Phase 24 | Pending |
| COMP-03 | Phase 25 | Pending |
| COMP-04 | Phase 24 + 25 (regression hold per phase) | Pending |
| COMP-05 | Phase 26 | Pending |
| COMP-06 | Phase 26 | Pending |

**Coverage:**
- v2.3 requirements: 6 total
- Mapped to phases: 6/6 ✓
- Unmapped: 0

---
*Requirements defined: 2026-04-28*
*Last updated: 2026-04-28 — initial draft from v2.3 scope agreement (Comprehension Layer — multi-prong P1+P2+P3 intervention; data-driven from v2.2 audit's COMP-BIAS-01 first candidate)*
