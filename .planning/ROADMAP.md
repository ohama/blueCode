# Roadmap: blueCode v1.2 Tool Expansion

## Milestones

- ✅ **v1.0 MVP** — Phases 1-5 (shipped 2026-04-23)
- ✅ **v1.1 Refinement** — Phases 6-7 (shipped 2026-04-24)
- 🚧 **v1.2 Tool Expansion** — Phases 8-9 (in progress)

## Overview

v1.2 adds three new tools to the agent's arsenal and enhances an existing one, eliminating the four workflow bottlenecks measured in the 2026-04-24 benchmark session. Phase 8 extends the Domain DU, schema, and system prompt to expose `edit_file`, `glob_search`, and `grep_search` — all sharing the same cross-cutting seams (Tool DU, `llmStepSchema` enum, `CompositionRoot` system prompt). Phase 9 is a targeted enhancement to `read_file` that prepends a one-line metadata header to every response, giving the agent the file-bounds signal it needs to avoid the out-of-range `start_line` loop observed in benchmark T6.

## Phase Numbering

- Integer phases (1, 2, 3...): Planned milestone work — continuous across milestones
- Decimal phases (8.1, 8.2): Urgent insertions created via `/gsd:insert-phase`, execute between integers in numeric order
- v1.0 used Phases 1-5; v1.1 used Phases 6-7; v1.2 starts at Phase 8

## Phases

- [x] **Phase 8: Tool Expansion** — Add `edit_file`, `glob_search`, `grep_search` to Domain DU, schema, executor, and system prompt
- [ ] **Phase 9: Read File Metadata** — Prepend `[file: ..., lines X-Y of Z, truncated|not-truncated]` header to `read_file` output

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

**Plans**: 1 plan

Plans:
- [ ] 09-01-read-file-metadata-PLAN.md — Prepend bounds/truncation header to read_file (3 tasks: impl + tests + system prompt)

---

## Progress

**Execution Order:** Phases execute in numeric order: 8 → 9 (but phases are independent and could execute in either order)

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1-5. MVP Phases | v1.0 | 17/17 | Complete | 2026-04-23 |
| 6. Dynamic Bootstrap | v1.1 | 3/3 | Complete | 2026-04-24 |
| 7. Thought Capture | v1.1 | 2/2 | Complete | 2026-04-24 |
| 8. Tool Expansion | v1.2 | 2/2 | Complete | 2026-04-25 |
| 9. Read File Metadata | v1.2 | 0/1 | Planned (awaiting execute) | - |
