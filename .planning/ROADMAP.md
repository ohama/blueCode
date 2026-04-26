# Milestone v1.4: Test Hygiene + Bench Polish

**Status:** In Progress
**Phases:** 12 - 13
**Total Plans:** TBD (estimated 2 plans per phase, 4 total)

## Overview

v1.4 clears two pieces of cited tech debt that accumulated across the prior three milestones. Phase 12 consolidates the `makeMockResponse` test helper from 3 in-repo duplications into a single shared module — mechanical refactor with zero behavioral change. Phase 13 adds a bash `trap` to `bench/run.sh` so write-task fixtures auto-reset on exit, eliminating the `git status` drift that has accumulated cognitive overhead across every v1.3 commit cycle.

**Phase numbering note:** v1.0 used phases 1-5; v1.1 used 6-7; v1.2 used 8, 9, 9.1 (decimal insertion); v1.3 used 10-11. v1.4 starts at 12.

---

## Phases

### Phase 12: Test Helper Consolidation

**Goal**: `makeMockResponse` has exactly one definition in the test suite — in `tests/BlueCode.Tests/MockHelpers.fs` — and all consumer test files reference it via `open BlueCode.Tests.MockHelpers`. No test is lost or duplicated; 243/1/0 is preserved.

**Depends on**: Nothing — independent from Phase 13. No source-code changes; no interaction with bench fixtures.

**Requirements**: TST-01

**Success Criteria**:
1. `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` reports exactly `243 passed, 1 ignored, 0 failed`.
2. `grep -rn "let makeMockResponse" tests/` returns exactly 1 match (in `MockHelpers.fs`); zero matches in `AgentLoopTests.fs` or `ReplTests.fs`.
3. `BlueCode.Tests.fsproj` has a `<Compile Include="MockHelpers.fs" />` entry appearing before `AgentLoopTests.fs` and `ReplTests.fs` in compile order.
4. `git diff --name-only` for the phase shows only files under `tests/` — no `src/` file touched, no `bench/` file touched.

**Plans**:

- [ ] 12-01-PLAN.md — Create `MockHelpers.fs`, register it in `BlueCode.Tests.fsproj`, update `AgentLoopTests.fs` + `ReplTests.fs` consumers, verify 243/1/0

---

### Phase 13: Bench Fixture Cleanup

**Goal**: Running any `bench/run.sh` mode (`--gate`, `--canary`, `--all`, `--b2`) leaves `git status` clean for `bench/fixtures/bug_lastchar.fs` and `bench/fixtures/bug_average.fs`; the auto-reset is documented in `documentation/bench.md`.

**Depends on**: Nothing — independent from Phase 12. `bench/run.sh` is a pure bash script; no F# compilation involved. Phases 12 and 13 could run in either order.

**Requirements**: BENCH-06

**Success Criteria**:
1. `bash bench/run.sh --gate` exits 0 (no regression) AND `git status --short` afterwards shows no modified line containing `bench/fixtures/bug_lastchar.fs` or `bench/fixtures/bug_average.fs`.
2. Same clean `git status` result after `bash bench/run.sh --canary` and `bash bench/run.sh --b2`.
3. `grep -n "trap" bench/run.sh` shows a trap that targets `bug_lastchar.fs` and `bug_average.fs` but NOT `bug_divide_zero.fs`.
4. `documentation/bench.md` contains a paragraph or subsection describing the auto-reset behavior (searchable via `grep -i "trap\|auto-reset\|cleanup" documentation/bench.md`).

**Plans**:

- [ ] 13-01-PLAN.md — Add `trap` to `bench/run.sh`, update `documentation/bench.md`, verify SC1-4

---

## Progress

| Phase | Name | Requirements | Plans | Status |
|-------|------|--------------|-------|--------|
| 12 | Test Helper Consolidation | TST-01 | 1 | Not started |
| 13 | Bench Fixture Cleanup | BENCH-06 | 1 | Not started |

**Requirement coverage:** 2/2 (TST-01 → Phase 12, BENCH-06 → Phase 13)

---

*Roadmap created: 2026-04-26*
*For current project status, see `.planning/STATE.md`*
