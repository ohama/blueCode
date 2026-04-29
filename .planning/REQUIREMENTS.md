# Requirements: blueCode v2.4 Coding Quality

**Defined:** 2026-04-29
**Core Value:** Mac 로컬 Qwen 3.5 122B를 strong-typed F# agent loop로 **empirically** 안정적으로 돌린다 (post-v2.3 verdict 92/100 KEEP; v2.4 closes the only ≤60%-threshold dimension via measurement-first F# fixture eval)
**Milestone goal:** Resolve the Coding-quality 6/10 (Idiomatic F# 1/5) sub-score on the eval scorecard. v2.2/v2.3 audit hedged that the 1/5 may be a Python-transcript artifact (HumanEval+ scoring Python answers under chat mode). This milestone is **measurement-first**: build F# task fixtures, measure idiomatic-F# generation in agent-loop mode, then intervene only if data justifies. Either path produces useful data.

## v2.4 Requirements (3 categories, 6 requirements; conditional structure)

The single data-driven candidate (IDIOMATIC-FS-01 carried-forward from v2.2 audit) is decomposed into 6 atomic requirements grouped by category. **FS-INTERVENE category is conditional** — Phase 29 is added via `/gsd:add-phase` only if Phase 28 measurement confirms 1/5. Mirrors v2.3's Phase 27 supersession pattern (the supersession-and-mid-flight-add discipline is now reusable repo knowledge).

### F# Coding-quality Measurement (3 reqs)

The data-driven core of v2.4 — proper F# fixtures replace Python-transcript-based scoring.

- [x] **FS-EVAL-01**: 3-5 F# task fixtures created under `bench/fixtures/fs_idiomatic/`, each requiring at least one idiomatic F# pattern — COMPLETE 2026-04-29 (28-02 commit `25eb35d`)
  - **Goal:** Replace HumanEval+ Python-answer transcripts as the basis for §5 Coding-quality scoring. Fixtures must intentionally require patterns where idiomatic F# matters: pipeline `|>`, DU pattern matching, `Result.bind` / `Option.map` chains, record `with`-update, currying for partial application.
  - **Behavior:** Each fixture is a `<name>.task.md` (task description, ≤500 chars) + `<name>.fs` (skeleton with type signatures + holes the agent must fill) under `bench/fixtures/fs_idiomatic/`. Pre-flight `git checkout -- bench/fixtures/fs_idiomatic/` restores canonical state (mirrors `refactor_multifile/` pattern from v2.2).
  - **Validation:** `ls bench/fixtures/fs_idiomatic/` shows ≥3 task fixtures; each fixture's `.task.md` mentions at least one idiomatic pattern explicitly; `.fs` skeleton compiles standalone before agent fills holes.
  - **Threshold:** All-or-nothing fixture set delivery. Below 3 fixtures = insufficient signal; above 5 = scope creep.

- [x] **FS-EVAL-02**: New `--fs-idiomatic` mode in `bench/eval-qwen35-122b.sh` runs fixtures through agent-loop mode + captures transcripts — COMPLETE 2026-04-29 (28-03 commit `b7611a8`; 28-04 commit `1413111`)
  - **Goal:** Reproducible measurement infrastructure for §5 Coding-quality. HTTP-only (no `mlx_lm.load` — preserves v2.1 invariant). Mandatory `launchctl kickstart` pre-flight (v2.3 KV-cache lesson). Per-fixture wall-clock + step count + transcript captured.
  - **Behavior:** `bash bench/eval-qwen35-122b.sh --fs-idiomatic` runs each fixture in agent-loop mode (no `--plan` — measures the daily-driver path); each fixture produces `bench/runs/qwen35-eval-<ts>/fs_idiomatic_<fixture>.diff` (post-fixture file state) + `fs_idiomatic_<fixture>.transcript.txt` (full agent loop output). Restore fixtures via `git checkout` between fixtures.
  - **Validation:** `bash bench/eval-qwen35-122b.sh --fs-idiomatic` exits 0; produces ≥3 transcript files; fixtures restored to canonical state post-run (`git diff bench/fixtures/fs_idiomatic/` empty).
  - **Threshold:** All fixtures run to completion (MaxLoopsExceeded permitted; not all need to compile-PASS — measurement is observational, not pass/fail).

- [x] **FS-EVAL-03**: Eval doc §5 + §7 re-scored with new F#-fixture evidence; verdict updated if score changes — COMPLETE 2026-04-29 (28-04 commit `1413111`; 28-05 commit `a36cc35`)
  - **Goal:** §5 Coding-quality (Idiomatic F# 1/5) re-scored against rubric using F# fixture transcripts. §7 scorecard re-aggregated. Final scorecard line updated if total changes.
  - **Behavior:** Read all fixture transcripts; score each on rubric (pipeline used vs imperative loops; DU pattern matching vs nested if-else; Result/Option chains vs throw-style error handling; idiomatic naming + organization). Average per-fixture scores. Update eval doc §5 with new score + evidence references; §7 Coding-quality subtotal re-aggregated; if `Total` changes, update final scorecard line + §8 Caveat 6 (or Caveat 7 if 6 was reframed in v2.3).
  - **Validation:** `grep -E "^\*\*Total: \d+/100, Recommendation: KEEP\*\*$" documentation/qwen35-122b-coding-eval.md` matches; score change documented in §5 with fixture transcript references; if no change, §5 explicitly notes the artifact-vs-real disambiguation result.
  - **Threshold:** Eval doc must reflect the new measurement honestly. If 1/5 confirmed real → score stays 1/5 (Phase 29 intervention triggered). If 1/5 disproved (e.g., 3-5/5 on F# fixtures) → score updated; total +2~4; milestone may close after Phase 28 alone.

### F# Style Intervention (2 reqs, **conditional**)

**Conditional category — only triggered if FS-EVAL-03 confirms 1/5.** Phase 29 is added via `/gsd:add-phase` post-Phase-28. Mirrors v2.3 P1 directive pattern in `defaultSystemPrompt`. If Phase 28 disproves 1/5, this entire category is skipped (closed-by-disprove, not deferred).

- [ ] **FS-INTERVENE-01** (conditional): F# style hint added to `defaultSystemPrompt` with conditional clause — **SKIPPED** (Phase 28 passed_disprove_1of5; conditional path not triggered)
  - **Goal:** Direct the agent to prefer idiomatic F# patterns when generating F# code. Conditional clause ("When generating F# code, prefer pipelines `|>`, DU pattern matching, `Result.bind`/`Option` chains over imperative if-else") makes the directive dormant for non-F# tasks.
  - **Behavior:** New paragraph appended to `defaultSystemPrompt` in `src/BlueCode.Cli/CompositionRoot.fs`. Char budget: directive + separator ≤ 200 chars (current `defaultSystemPrompt` 967 → ≤1167 chars). Cli-only edit (Core purity preserved); bench gate 7/7 PASS hold required (conditional clause is dormant for non-F# fixtures; W1/W2 are F# bug-fix tasks, not F# generation, so should not be affected — but bench gate is the empirical truth).
  - **Validation:** `grep -F "When generating F# code" src/BlueCode.Cli/CompositionRoot.fs` matches; `defaultSystemPrompt` length ≤1167 chars; bench gate 7/7 PASS preserved.
  - **Threshold:** Bench gate ALL fixtures within v2.2 baseline_max. If gate regresses on any fixture: iterate phrasing (≤2 iterations); on 3rd phrasing failure, escalate (revert + pause; same protection pattern as v2.3 Phase 26 BLOCKED branch).

- [ ] **FS-INTERVENE-02** (conditional): F# fixtures re-run post-intervention; score change recorded — **SKIPPED** (Phase 28 passed_disprove_1of5; conditional path not triggered)
  - **Goal:** Measure FS-INTERVENE-01's effect on idiomatic-F# generation. Compare pre/post transcripts.
  - **Behavior:** Re-run `bash bench/eval-qwen35-122b.sh --fs-idiomatic` post-intervention with kickstart pre-flight (mandatory; v2.3 KV-cache lesson). Score against same rubric. Eval doc §5 + §7 + final scorecard updated.
  - **Validation:** Score change documented with pre/post fixture transcripts; if score increased, final scorecard line reflects new total; if score unchanged, §5 documents that the directive was insufficient (informational; no further intervention attempted in v2.4).
  - **Threshold:** All fixtures re-run completes; score recorded honestly regardless of outcome.

### Harness Audit + Regression Hold (2 reqs)

- [x] **HARNESS-01**: `bench/eval-qwen35-122b.sh` audited; macOS bash-strict-mode patterns codified into `documentation/howto/` — COMPLETE 2026-04-29 (28-01 commits `94d905c` + `280677a`)
  - **Goal:** Codify the 4 macOS-bash-strict-mode patches accumulated across v2.1-v2.3 into a discoverable howto, so future bash handler authors don't re-discover. The patches: (1) v2.1 21-04 set-e/dotnet-exit (`set +e` around `dotnet run`); (2) v2.1 21-04 grep-c/pipefail double-output (`|| true` not `|| echo 0`); (3) v2.2 23-01 mkdir-before-tee (any I/O command must verify target dir first); (4) v2.3 27-02 grep-cE-zero-match-exit-1 (`|| true` guard on PASS path).
  - **Behavior:** New `documentation/howto/macos-bash-strict-mode-patterns.md` (~150-300 lines): each pattern with symptom, root cause, and the canonical fix. Cross-reference back to commits + plan summaries. Light audit pass on `bench/eval-qwen35-122b.sh` for any 5th unhandled case (e.g., `seq M N` countdown bug noted in v2.1 21-04); fix if found.
  - **Validation:** `documentation/howto/macos-bash-strict-mode-patterns.md` exists; each of the 4 patterns has its own section with symptom/cause/fix; commit refs to `4bcd8a4`, `9f8e06e`, etc.
  - **Threshold:** Howto written; bench gate 7/7 PASS hold; `bench/eval-qwen35-122b.sh` body either unchanged or has only minimal cleanup (any modification documented in milestone audit per v2.3 precedent).

- [x] **REGRESSION-01**: Bench gate 7/7 PASS preserved through entire milestone; `bench/baseline.json` byte-equal — Phase 28 portion COMPLETE 2026-04-29 (28-06 final gate; remaining: Phase 30 final gate)
  - **Goal:** Standard regression-hold invariant. After each phase, `bash bench/run.sh --gate` exits 0 with `GATE PASS (7/7)` and per-fixture step counts within v2.2 baseline_max.
  - **Behavior:** Bench gate runs at end of each phase. If any fixture regresses, iterate the directive (FS-INTERVENE-01) or the harness change (HARNESS-01). Do NOT modify `bench/baseline.json`.
  - **Validation:** `git diff milestone-v2.3 HEAD -- bench/baseline.json` empty post-milestone; bench gate exit 0 confirmed at end of each phase.
  - **Threshold:** All-or-nothing — every gate fixture holds step count baseline through the milestone.

## Out of Scope

v1 + v2.0 + v2.1 + v2.2 + v2.3 boundaries unchanged. v2.4 explicitly excludes:

| Feature | Reason |
|---------|--------|
| AGENT-LOOP-FEW-SHOT-01 (P2 migration to defaultSystemPrompt) | v2.3 closed CORR-EVAL-02 without it; Phase 27 deliberately left P2 in plan-mode for MVP scope discipline. v2.5+ candidate if observation surfaces value. |
| COLDSTART-PRISTINE-01 (post-reboot pristine cold-start) | Warm-cache 37s already documented in v2.2; pristine measurement needs scheduled disruption window; low urgency. v2.5+ candidate. |
| Slash commands (`/sessions`, `/plan`, `/clear`) | No daily-driver pain signal; CLI flags work. v2.5+ candidate (observation-driven). |
| Compaction (auto-snip at 80% max_model_len) | No observed long-session pain. v2.5+ candidate. |
| Sub-agent delegation (Agent tool) | Speculative without observation. v2.5+ candidate. |
| Plan-mode bench fixture (mocked-IKeyReader) | Deferred from v2.0 Phase 16; complementary not load-bearing for IDIOMATIC-FS-01. v2.5+ candidate. |
| Thinking-mode-on (`<think>` consumption) | v2.1 measurement says thinking-OFF gives perfect schema 0/50; ON risks regression. **Defer indefinitely.** |
| Native OpenAI `tool_calls` | Custom JSON schema gives 0/50 perfect; no functional reason to rewrite. v3.0 territory. |
| Streaming inference (STM-01) | TTFT 222ms warm is already instant; deferred 9th cycle. **Defer pattern is the signal.** |
| Cloud comparison (Claude/GPT-4) | Deliberate non-goal preserved from v2.1 §6.3. |
| Re-eval beyond F# fixtures | Other dimensions (HumanEval+ Python, Correctness, Performance, Reliability) unchanged from v2.3 baseline; would re-measure only if their constraints shifted. |
| `bench/baseline.json` modifications | Eval observational; gate baseline preserved byte-for-byte. |
| Multi-platform (Windows/Linux) | Mac-only ethos preserved. |
| F# fixture set above 5 | Scope discipline; if 5 doesn't surface signal, more fixtures won't either — re-think rubric design. |

## Future Requirements (v2.5+ candidates)

Tracked for awareness; not pulled into v2.4. Observation-driven scoping after v2.4 ships.

- **AGENT-LOOP-FEW-SHOT-01** — P2 few-shot migration to `defaultSystemPrompt` if observation surfaces value
- **COLDSTART-PRISTINE-01** (low) — Post-reboot pristine cold-start measurement
- **SLASH-01** — `/sessions`, `/plan`, `/clear` slash commands inside REPL
- **COMPACT-01** — Auto-compaction when session approaches 80% of `max_model_len`
- **SUBAG-01** — Sub-agent delegation via Agent tool
- **PLAN-MODE-BENCH-01** — Plan-mode bench fixture via mocked-IKeyReader
- **STM-01** — SSE token streaming in blueCode runtime (deferred 10x as of v2.4 close)
- **F# fixture expansion** — if v2.4 produces useful signal but is rubric-limited, additional fixtures or tighter rubric in v2.5+

## Traceability

Filled by roadmap. Each requirement maps to exactly one phase.

| Requirement | Phase | Status |
|-------------|-------|--------|
| FS-EVAL-01 | Phase 28 | Complete (2026-04-29; 28-02) |
| FS-EVAL-02 | Phase 28 | Complete (2026-04-29; 28-03 + 28-04) |
| FS-EVAL-03 | Phase 28 | Complete (2026-04-29; 28-04 + 28-05) |
| HARNESS-01 | Phase 28 | Complete (2026-04-29; 28-01) |
| REGRESSION-01 | Phase 28 (Phase 30 final gate remaining) | Phase 28 portion complete; Phase 30 pending |
| FS-INTERVENE-01 | Phase 29 (conditional; SKIPPED) | SKIPPED — passed_disprove_1of5; no intervention needed |
| FS-INTERVENE-02 | Phase 29 (conditional; SKIPPED) | SKIPPED — passed_disprove_1of5; no intervention needed |

**Coverage:**
- v2.4 unconditional requirements: 5 (FS-EVAL-01..03 + HARNESS-01 + REGRESSION-01)
- v2.4 conditional requirements: 2 (FS-INTERVENE-01..02; only fires if Phase 28 data justifies)
- Mapped to phases: 5/5 unconditional ✓; 2/2 conditional mapped to placeholder Phase 29 ✓

---
*Requirements defined: 2026-04-29*
*Last updated: 2026-04-29 — Phase 28 complete: 5/5 unconditional requirements satisfied (FS-EVAL-01..03, HARNESS-01, REGRESSION-01 Phase 28 portion). Phase 29 conditional requirements (FS-INTERVENE-01..02) SKIPPED per passed_disprove_1of5 classification. Phase 30 Milestone Close pending.*
