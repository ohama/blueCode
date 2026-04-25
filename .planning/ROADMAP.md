# Roadmap: blueCode v1.2 Tool Expansion

## Milestones

- ✅ **v1.0 MVP** — Phases 1-5 (shipped 2026-04-23)
- ✅ **v1.1 Refinement** — Phases 6-7 (shipped 2026-04-24)
- 🚧 **v1.2 Tool Expansion** — Phases 8-9 + 9.1 (in progress)

## Overview

v1.2 adds three new tools to the agent's arsenal and enhances an existing one, eliminating the four workflow bottlenecks measured in the 2026-04-24 benchmark session. Phase 8 extends the Domain DU, schema, and system prompt to expose `edit_file`, `glob_search`, and `grep_search` — all sharing the same cross-cutting seams (Tool DU, `llmStepSchema` enum, `CompositionRoot` system prompt). Phase 9 is a targeted enhancement to `read_file` that prepends a one-line metadata header to every response, giving the agent the file-bounds signal it needs to avoid the out-of-range `start_line` loop observed in benchmark T6.

## Phase Numbering

- Integer phases (1, 2, 3...): Planned milestone work — continuous across milestones
- Decimal phases (8.1, 8.2): Urgent insertions created via `/gsd:insert-phase`, execute between integers in numeric order
- v1.0 used Phases 1-5; v1.1 used Phases 6-7; v1.2 starts at Phase 8

## Phases

- [x] **Phase 8: Tool Expansion** — Add `edit_file`, `glob_search`, `grep_search` to Domain DU, schema, executor, and system prompt
- [x] **Phase 9: Read File Metadata** — Prepend `[file: ..., lines X-Y of Z, truncated|not-truncated]` header to `read_file` output
- [x] **Phase 9.1: Bench Follow-up Fixes** (INSERTED) — Close v1.2 audit tech-debt: dispatcher default-window for T6 32B; system prompt strategy hint for `truncated`; code-level loop-injection enforcing `edit_file` terminality (overrides user-prompt tool naming)

---

## Phase Details

<details>
<summary>✅ v1.0 MVP (Phases 1-5) — SHIPPED 2026-04-23</summary>

See `.planning/milestones/v1.0-ROADMAP.md` for full phase details and plan history.

17 plans (16 autonomous + 1 human-gated UAT) across 5 phases. 208 tests. 5,891 LOC F#.

</details>

<details>
<summary>✅ v1.1 Refinement (Phases 6-7) — SHIPPED 2026-04-24</summary>

See `.planning/milestones/v1.1-ROADMAP.md` for full phase details and plan history.

5 plans (3 in Phase 6 incl. 06-03 gap closure, 2 in Phase 7) across 2 phases. 218 tests.

</details>

---

### 🚧 v1.2 Tool Expansion (In Progress)

**Milestone Goal:** Eliminate four measured agent workflow bottlenecks — surgical file edit, native file and content search, and read_file bounds awareness — by adding three tools and one metadata enhancement.

---

### Phase 8: Tool Expansion

**Goal**: The agent can use `edit_file`, `glob_search`, and `grep_search` as first-class tools — all three recognized by the JSON schema, dispatched by the agent loop, implemented in the executor, and described in the system prompt.

**Depends on**: Nothing (independent of Phase 9)

**Requirements**: TLX-01, TLX-02, TLX-03

**Success Criteria** (what must be TRUE):
1. `grep -c "EditFile\|GlobSearch\|GrepSearch" src/BlueCode.Core/Domain.fs` returns 3 — all three DU cases exist in Core with no Serilog/Spectre references introduced
2. A prompt that would previously require `run_shell "find . -name '*.fs'"` now causes the agent to emit `action: "glob_search"` — verified by a live run or a targeted AgentLoop test with a mock LLM response using the new action
3. `llmStepSchema` validation accepts `"edit_file"`, `"glob_search"`, and `"grep_search"` as valid `action` values, and rejects any string not in the 8-value enum (`read_file`, `write_file`, `list_dir`, `run_shell`, `edit_file`, `glob_search`, `grep_search`, `final`)
4. `edit_file` with `oldString` appearing exactly once produces a modified file on disk; `edit_file` with `oldString` appearing zero times returns `Failure "oldString not found"`; `edit_file` with `oldString` appearing 2+ times returns `Failure "oldString matches N times; refine to make unique"`
5. All 218 v1.1 tests still pass — no regression in `ReadFile`, `WriteFile`, `ListDir`, `RunShell` behavior or routing

**Plans**: 2 plans (complete)

Plans:
- [x] 08-01-shared-seam-PLAN.md — Tool DU + dispatchTool + schema enum + system prompt + FsToolExecutor stubs
- [x] 08-02-PLAN.md — editFileImpl + globSearchImpl + grepSearchImpl + ToolExpansionTests (18 cases)

---

### Phase 9: Read File Metadata

**Goal**: Every `read_file` response begins with a structured one-line header that tells the agent the file's total line count, the range returned, and whether the content was truncated — giving 32B the bounds signal it needs to avoid requesting `start_line` values beyond the file's end.

**Depends on**: Nothing (independent of Phase 8; no Domain DU change, no schema change)

**Requirements**: TOOL-08

**Success Criteria** (what must be TRUE):
1. `blueCode "read the first 10 lines of src/BlueCode.Core/Domain.fs"` (or equivalent `read_file` invocation) produces tool output that starts with `[file: src/BlueCode.Core/Domain.fs, lines 1-10 of <N>, not-truncated]` — the header appears before the file content in the string returned to the agent
2. When `start_line` exceeds `total_lines` (e.g., `start_line=2001` on a 150-line file), the header reads `[file: <path>, lines 2001-2100 of 150, out-of-range]` and the content section is empty — agent receives an unambiguous signal that the requested range is beyond the file, preventing the T6 benchmark infinite-retry loop
3. Normal `read_file` with no `start_line` on a file under 2000 chars returns `[file: <path>, lines 1-N of N, not-truncated]` — existing callers see header prepended but content unchanged
4. All 218 v1.1 tests (plus any new Phase 8 tests) still pass — `FsToolExecutor.executeAsync` change is backward-compatible; no Core type changes

**Plans**: 1 plan (complete)

Plans:
- [x] 09-01-read-file-metadata-PLAN.md — Prepend bounds/truncation header to read_file (3 tasks: impl + tests + system prompt)

---

### Phase 9.1: Bench Follow-up Fixes (INSERTED)

**Goal**: Close the three tech-debt items from the v1.2 milestone audit (revised verdict `tech_debt` after 36-run live re-bench) so TOOL-08 actually delivers its motivating value and v1.1 baselines are recovered before milestone archive.

**Depends on**: Phase 9 (read_file header semantics it builds on)

**Requirements**: TOOL-08 (effectiveness completion), TLX-01 (mental-model clarification — does not change spec)

**Why inserted**: Same-day live re-bench (Part 3 of `documentation/benchmark-32b-vs-72b.md`, 36 runs) revealed (a) TOOL-08's primary motivating scenario — T6 32B `start_line=2001`-only on a 179-line file — never reaches the out-of-range branch because `AgentLoop.dispatchTool` requires both bounds to construct `lineRange`; (b) the new `truncated` header keyword regresses 72B T6 from 4/4 ✓ → 0/4 ✗ (model interprets as "request full file" instead of "narrow window"); (c) 32B redundantly chains `write_file` after a successful `edit_file`. Audit verdict moved to `tech_debt` rather than `gaps_found` because the spec-vs-implementation contract is intact — these are bridge/semantics gaps. Treating as Phase 9.1 (Option A in `documentation/v1.2-bench-followup.md` §6) lets v1.2 ship with the actual T6 fix landed and the audit return to `passed`.

**Success Criteria** (what must be TRUE):
1. `AgentLoop.dispatchTool` produces a non-`None` `lineRange` when only `start_line` is present (default 100-line window: `Some(s, s+99)`), and when only `end_line` is present (`Some(1, e)`) — verified by a new unit test that mirrors the production T6 trace shape (`{"path": ..., "start_line": 2001}` on a 3-line file → header `[file: ..., lines 2001-2100 of 3, out-of-range]`).
2. System prompt `read_file` block adds a strategy line for `truncated` (e.g., "content was clipped to 2000 chars; pick a smaller window — `end_line - start_line < 50` — to fetch unclipped content") so 72B can pick a remediation strategy without relying on response-size inference.
3. System prompt `edit_file` block declares the action terminal: "edit_file persists the change. After a successful edit_file, do NOT call write_file with the same path."
4. Live bench validation (10 targeted runs per `documentation/v1.2-bench-followup.md` §4): T6 × 32B × 3 → 3/3 pass (was 0/3); T6 × 72B × 3 → 2-3/3 pass (was 0/3); W1 × 32B and W2 × 32B drop one redundant step each; T1 × 32B and T5 × 72B canaries unchanged.
5. All v1.2 tests still pass (240 baseline + 1 new dispatcher-shape test = 241), zero regressions in routing, loop, or tooling. Core diff is empty for system-prompt fixes (CompositionRoot lives in Cli); Core diff for Fix 1 is confined to `AgentLoop.dispatchTool`.

**Plans**: 5 plans (3 original + 2 gap-closure rounds)

Plans:
- [x] 09.1-01-PLAN.md — System prompt clarifications (Fix 3 + Fix 2: edit_file terminal hint + truncated narrow-window strategy) [Wave 1]
- [x] 09.1-02-PLAN.md — Dispatcher default-window arms + production-trace TOOL-08-bench test (Fix 1) [Wave 2]
- [x] 09.1-03-PLAN.md — Bench harness v9_1 selector + 10 targeted live runs + VALIDATION.md [Wave 3]
- [x] 09.1-04-PLAN.md — Wording iteration (gap closure round 1) — W2 closed (4→3 PASS), W1 user-prompt conflict diagnosed; Task 1 reverted [Wave 4, gap_closure]
- [x] 09.1-05-PLAN.md — Code-level loop-injection (gap closure round 2) — `lastEditPath` threaded through `runLoop`, post-user `[POST-EDIT CONSTRAINT]` System message; W1 4→3 PASS, W2 reinforced [Wave 5, gap_closure]

**Reference**: [documentation/v1.2-bench-followup.md](../documentation/v1.2-bench-followup.md), [.planning/v1.2-MILESTONE-AUDIT.md](v1.2-MILESTONE-AUDIT.md) `tech_debt[]`

---

## Progress

**Execution Order:** Phases execute in numeric order: 8 → 9 (but phases are independent and could execute in either order)

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1-5. MVP Phases | v1.0 | 17/17 | Complete | 2026-04-23 |
| 6. Dynamic Bootstrap | v1.1 | 3/3 | Complete | 2026-04-24 |
| 7. Thought Capture | v1.1 | 2/2 | Complete | 2026-04-24 |
| 8. Tool Expansion | v1.2 | 2/2 | Complete | 2026-04-25 |
| 9. Read File Metadata | v1.2 | 1/1 | Complete | 2026-04-25 |
| 9.1. Bench Follow-up Fixes (INSERTED) | v1.2 | 5/5 | Complete | 2026-04-26 |
