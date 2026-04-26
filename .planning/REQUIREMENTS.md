# Requirements: blueCode v1.4 Test Hygiene + Bench Polish

**Defined:** 2026-04-26
**Core Value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Milestone goal:** Clear two pieces of cited tech debt from prior milestones (3-milestone-old shared test helper + bench fixture working-tree drift surfaced in v1.3 Part 4), then enter a 2-week observation window to capture real-use pain signals via `/gsd:add-todo` for v1.5 scoping.

## v1.4 Requirements (2 requirements, 2 categories)

Path B from the v1.3 close discussion: ship-from-cited-pain only. Both requirements have explicit citation chains.

### Test Infrastructure

- [x] **TST-01**: Shared `makeMockResponse` test helper module ✓ (Phase 12, 2026-04-26)
  - **Goal:** Consolidate 3 in-repo duplications of the `makeMockResponse` helper into a single shared module so future test additions don't fork the implementation.
  - **Current state (citations):**
    - `tests/BlueCode.Tests/AgentLoopTests.fs` — 2 instances (added in v1.2 Phase 9.1-05 and v1.3 Phase 11-01 mocked-LLM tests)
    - `tests/BlueCode.Tests/ReplTests.fs` — 1 instance (carried since v1.0 / v1.1)
    - `documentation/howto/handle-expecto-console-redirection.md` — pre-existing F# Expecto pitfall related to test infrastructure
  - **Behavior:**
    - Create `tests/BlueCode.Tests/MockHelpers.fs` with the canonical `makeMockResponse` signature (and any other duplicated test scaffolding worth consolidating — but scope is *only* `makeMockResponse`; resist scope creep into "let's also factor X").
    - Update `AgentLoopTests.fs` and `ReplTests.fs` to consume the shared module via `open BlueCode.Tests.MockHelpers`.
    - Register `MockHelpers.fs` in `BlueCode.Tests.fsproj` `<Compile Include>` order BEFORE the consumer test files (F# compile order). NO new entry needed in `RouterTests.fs:rootTests` because `MockHelpers.fs` defines no testList — it's a pure helper module.
  - **Test count must remain 243** (242 baseline + 1 from v1.3 11-01). Refactor must not drop or duplicate any test.
  - **Validation:** `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` reports `243 passed, 1 ignored, 0 failed`. `git diff` shows the 3 duplicate `makeMockResponse` definitions removed and replaced with `open` references.
  - **Cited as:** `⚠ Revisit` row in PROJECT.md Key Decisions since v1.1 close (3 milestones); same row updated by v1.2 close (`v1.3+ test infrastructure 패스에서 통합 고려 (TST-01)`); 4 executors over 3 milestones hit related dual-registration pitfalls when adding tests because the duplication makes the canonical pattern unclear.
  - **근거:** 3-milestone-old debt with citation chain back to v1.1 close. Refactoring is mechanical, low-risk (all consumers in `tests/`, no Core diff, no new dependencies). Bench gate stays green by construction (no source-code change).

### Bench Hygiene

- [ ] **BENCH-06**: `bench/run.sh` auto-resets write-task fixtures on exit
  - **Goal:** `git status` is clean after `bash bench/run.sh --gate` (and `--canary`, `--all`, `--b2`) regardless of what the LLM did to the fixtures during the run.
  - **Current state (citation):** v1.3 Part 4 §23 ("Discoveries") in `documentation/benchmark-32b-vs-72b.md`:
    > **bench fixture 의 working tree drift** — `--gate` 실행 후 W1/W2 fixture 들이 LLM 의 수정 결과로 left-on-disk 상태가 됨. 다음 실행에서 자동으로 heredoc-restore 되지만, `git status` 가 더러워 보임. 향후 cleanup 자동화 후보
  - **Behavior:**
    - Add a bash `trap` near the top of `bench/run.sh` (after `set -u`) that, on script exit (any reason — success, failure, Ctrl-C), runs `git checkout -- bench/fixtures/bug_lastchar.fs bench/fixtures/bug_average.fs 2>/dev/null || true`. The `|| true` guard ensures the trap doesn't crash if `git` is unavailable or if the files are not tracked yet.
    - The trap fires for ALL modes (`--gate`, `--regression`, `--canary`, `--all`, `--b2`, `--help`) — not just `--gate`. (`--help` exits before any fixture mutation, so the trap is a no-op there; that's fine.)
    - The trap MUST NOT touch `bug_divide_zero.fs` (which is read-only by every test that uses it; never modified) — only the W1/W2 write-task fixtures need reset.
    - Existing in-line `cat <<'EOF' > bench/fixtures/...` heredoc-restore blocks before each W1/W2 invocation are preserved (defense in depth: trap handles exit-time cleanup, heredoc handles between-invocation reset).
  - **Validation:** Run `bash bench/run.sh --gate` then `git status --short`; output must NOT include `bench/fixtures/bug_lastchar.fs` or `bench/fixtures/bug_average.fs` as modified. Repeat with `--canary`, `--b2`, and `--all` (or at least one mode that runs W1/W2 — `--all` is the only one beyond `--gate`).
  - **근거:** Quality-of-life improvement with bounded scope. Single bash trap; no behavioral change to LLM invocations; bench gate's verdict logic untouched. Resolves a small but persistent cognitive overhead noted in every v1.3 commit cycle.

## Out of Scope

v1 boundaries unchanged. v1.4 adds no new exclusions; deferred items are "v1.5+ via observation window," not "out of scope."

| Feature | Reason |
|---------|--------|
| MCP / LSP / Plugin / hook / GUI / Windows·Linux / AOT / multi-tool chaining / >5 step chains / LLM-based compaction / vision / voice / background daemon / team runtime / cost tracking / notebook editing / telemetry | v1.0 OOS unchanged across v1.1, v1.2, v1.3, v1.4 |
| Cross-turn memory in multi-turn REPL | v2+ scope |
| Multi-platform `tryParseModelId` | Windows OOS — `StartsWith("/")` heuristic Mac/Linux only |

## Future Requirements (v1.5+)

**Critical scoping note:** v1.5 must NOT auto-pull from this list. Per the v1.4 exit criterion, v1.5 is scoped from `/gsd:add-todo` entries captured during the post-v1.4 observation window. The list below is for awareness only, not a backlog to drain.

### Streaming & Persistence

- **STM-01** (v1.5+ deferred 5x; revisit only if observation surfaces complaint) — SSE token streaming
- **SES-01** (v2+) — Session persistence + `--resume <id>`

### Escalation & UX

- **ROU-05** (deprioritized) — Auto 32B→72B escalation on MaxLoopsExceeded
- **CLI-08** (minor) — Ctrl+C "Cancelling…" display

### Hygiene

- **OPS-01** (deprioritized) — launchd-based prompt cache kickstart
- **OBS-06** (minor) — Per-port `MaxModelLen` visibility

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| TST-01      | 12    | Complete |
| BENCH-06    | 13    | Pending |

**Coverage:**
- v1.4 requirements: 2 total
- Mapped to phases: 2
- Unmapped: 0 ✓

---
*Requirements defined: 2026-04-26*
*Last updated: 2026-04-26 — traceability filled after roadmap creation (TST-01 → Phase 12, BENCH-06 → Phase 13)*
