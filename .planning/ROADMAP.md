# Roadmap: blueCode v1.3 Bench-Driven Quality Gates

## Overview

v1.3 formalizes v1.2's behavioral wins as a repo-tracked regression suite, then uses that suite to validate a system-prompt shrink that addresses B2's divide-by-zero misdiagnosis and creates headroom for future tool additions. Phase 10 moves the bench harness from `/tmp/` into the repo with a `--gate` mode that exits non-zero on regression. Phase 11 executes and validates the system-prompt shrink, leveraging both the gate and the 09.1-05 loop-injection primitive to move contextual hints out of the base prompt.

## Milestones

- ✅ **v1.0 MVP** — Phases 1-5 (shipped 2026-04-23)
- ✅ **v1.1 Refinement** — Phases 6-7 (shipped 2026-04-24)
- ✅ **v1.2 Tool Expansion** — Phases 8, 9, 9.1 (shipped 2026-04-26)
- **v1.3 Bench-Driven Quality Gates** — Phases 10-11 (in progress)

**Phase Numbering:** Continues at 10. v1.0 used 1-5; v1.1 used 6-7; v1.2 used 8, 9, 9.1 (decimal insertion). Next available integer is 10.

## Phases

<details>
<summary>v1.0 MVP (Phases 1-5) — SHIPPED 2026-04-23</summary>

Archived in `.planning/milestones/v1.0-ROADMAP.md`.

</details>

<details>
<summary>v1.1 Refinement (Phases 6-7) — SHIPPED 2026-04-24</summary>

Archived in `.planning/milestones/v1.1-ROADMAP.md`.

</details>

<details>
<summary>v1.2 Tool Expansion (Phases 8, 9, 9.1) — SHIPPED 2026-04-26</summary>

Archived in `.planning/milestones/v1.2-ROADMAP.md`.

</details>

---

### v1.3 Bench-Driven Quality Gates (In Progress)

**Milestone Goal:** Lock in v1.2's behavioral wins as a regressable suite, then use that suite to validate a system-prompt shrink that closes the B2 regression and creates headroom for future tools.

---

#### Phase 10: Bench Formalization

**Goal**: The bench harness is repo-tracked, version-controlled, and gate-capable — `bench/run.sh --gate` runs the regression subset, compares to a recorded baseline, and exits non-zero on regression, making v1.2's behavioral wins formally regressable for the first time.

**Depends on**: Nothing (independent; builds on v1.2's behavioral state, not on Phase 11)

**Requirements**: BENCH-01, BENCH-02, BENCH-03, BENCH-04, BENCH-05

**Success Criteria** (what must be TRUE when Phase 10 completes):
1. `bench/run.sh --gate` exits 0 against the current blueCode binary and exits non-zero when given a deliberately-regressed binary or a modified baseline, verifiable by running the script manually
2. `bench/fixtures/` contains at least three versioned fixture files (bug_lastchar.fs, bug_average.fs, and the B2 divide-by-zero fixture), all committed to the repo
3. `bench/baseline.json` exists, is valid JSON, and records per-test step counts and pass/fail status for T6 × 32B/72B, W1/W2 × 32B, T1/T5 canaries, and B2 regression status
4. `documentation/bench.md` exists and covers: fixture naming convention, prompt-design guidance (avoid explicit tool naming per 09.1-04), how to add a test, how to update baseline after an intentional fix, and hang-contingency for `mlx_lm.server`
5. CLAUDE.md no longer references `/tmp/bench-v1.2/run.sh`; the canonical bench entry point is `bench/run.sh`

**Plans**: 3 plans in 3 waves (sequential — each builds on prior) ✓ All complete

Plans:
- [x] 10-01-PLAN.md — Move bench harness into repo (bench/run.sh + fixtures + CLAUDE.md ## Bench)
- [x] 10-02-PLAN.md — Add baseline.json + --gate mode (BENCH-03, BENCH-04, SC1 verified positive + negative)
- [x] 10-03-PLAN.md — Write documentation/bench.md + Phase 10 final verification (all 5 SCs PASS)

---

#### Phase 11: System Prompt Shrink

**Goal**: `defaultSystemPrompt` is cut from ~1500 chars to ≤800 chars without regressing any `bench/run.sh --gate` test, and the B2 fixture returns to correct divide-by-zero diagnosis, validating the prompt-length attention-shift hypothesis from the v1.2 audit.

**Depends on**: Phase 10 — PERF-01's gate-green validation criterion (`bench/run.sh --gate` passing throughout iteration) requires the gate to exist. PERF-03's pass criterion requires B2 to be formalized as a fixture (BENCH-02) with a baseline entry (BENCH-03).

**Requirements**: PERF-01, PERF-02, PERF-03

**Success Criteria** (what must be TRUE when Phase 11 completes):
1. `grep -c '' src/BlueCode.Cli/CompositionRoot.fs` on the `defaultSystemPrompt` string value reports ≤800 characters (verifiable with `wc -c` on the extracted string)
2. `bench/run.sh --gate` exits 0 on the shrunk prompt — T6 × 32B/72B, W1/W2 × 32B, T1/T5 canaries all PASS (same gate that Phase 10 establishes)
3. `AgentLoop.fs` contains a post-`read_file`-truncated injection path: when the last tool result was a truncated `read_file`, `buildMessages` appends a System-role hint message; no `async {}` literals introduced, no Serilog/Spectre/Argu references added to Core
4. B2 fixture run (divide-by-zero prompt) returns at least one model correctly identifying "empty list" as the failure cause rather than "integer truncation"; `bench/baseline.json` updated to reflect the post-fix state

**Plans**: 3 plans in 3 waves ✓ All complete + verified (4/4 SCs PASS, 2026-04-26)

Plans:
- [x] 11-01-PLAN.md — Post-read_file [POST-READ HINT] injection extending 09.1-05's lastEditPath primitive (PERF-02) — 242→243 tests
- [x] 11-02-PLAN.md — Shrink defaultSystemPrompt 1689→783 chars (54% reduction, ≤800 Path C achieved) + 2 Rule 3 FsToolExecutor fixes (PERF-01)
- [x] 11-03-PLAN.md — B2 recovery validated: BOTH 32B and 72B identify "empty list"; baseline.json B2 entries flipped to pass=true (PERF-03)

---

## Progress

**Execution Order:** 10 → 11

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1-5. MVP | v1.0 | 17/17 | Complete | 2026-04-23 |
| 6-7. Refinement | v1.1 | 5/5 | Complete | 2026-04-24 |
| 8-9.1. Tool Expansion | v1.2 | 8/8 | Complete | 2026-04-26 |
| 10. Bench Formalization | v1.3 | 3/3 | Complete | 2026-04-26 |
| 11. System Prompt Shrink | v1.3 | 3/3 | Complete | 2026-04-26 |

---

*Last updated: 2026-04-26 — Phase 11 plans created (3 plans in 3 sequential waves)*
