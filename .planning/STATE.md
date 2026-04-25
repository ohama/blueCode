# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-04-24 for v1.2 milestone start)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** v1.2 Tool Expansion — Phase 9.1 inserted to close audit tech-debt (TOOL-08 dispatcher gap, 72B `truncated` regression, 32B edit_file+write_file redundancy) before milestone archive.

## Current Position

Milestone: v1.2 Tool Expansion (started 2026-04-24; Phases 8 + 9 verified 2026-04-25; **9.1 inserted 2026-04-25** post-audit re-bench)
Phase: 9.1 Bench Follow-up Fixes — 4/4 plans complete (ESCALATION: Plan 09.1-05 needed for W1)
Plan: 09.1-04 complete (Fix 3 wording iteration). W2 32B gate CLOSED (4→3 PASS). W1 32B gate still OPEN (root cause: user-prompt "using write_file" overrides system-prompt NEVER directive; code-level intervention required). T1 canary CANARY-OK (1 step, 1024). Plan 09.1-05 needed for W1 code-level fix.
Status: 241 tests passing (1 ignored); Fix 1 (dispatcher) + Fix 2 (truncated hint) + Fix 3 W2 validated. Fix 3 W1 still open — user-prompt/system-prompt conflict, not wording insufficiency. v1.2 milestone close blocked until 09.1-05 closes W1 gate.
Last activity: 2026-04-25 — Completed 09.1-04-PLAN.md. Directive wording (NEVER/ONLY/do-NOT) closed W2 but not W1. Task 1 commit (42728e1) reverted (f13c476) per rollback protocol. Root cause: W1 prompt "using write_file" explicitly names the tool. Next: Plan 09.1-05 (code-level intervention — loop-injection Option A or schema-level Option B).

Progress: v1.2 [██████████████████░░] structurally 100% (4/4 REQs marked Complete by spec) but **behaviorally ~90%** — T6 regression recovered; Fix 3 W2 closed; Fix 3 W1 still open (user-prompt conflict), pending Plan 09.1-05.

### Roadmap Evolution

- **2026-04-25** — Phase 9.1 (Bench Follow-up Fixes) inserted after Phase 9 via `/gsd:insert-phase`. URGENT — closes three tech-debt items from `.planning/v1.2-MILESTONE-AUDIT.md` discovered in same-day live re-bench (Part 3 of `documentation/benchmark-32b-vs-72b.md`, 36 runs). Reason: spec-vs-implementation contract was clean (audit `passed` initially), but T6 32B failure trace (`start_line` only, no `end_line`) never reaches the new out-of-range branch because `AgentLoop.dispatchTool` collapses partial bounds to `None`; and 72B's `truncated` semantics regressed v1.1 baseline 4/4 → 0/4. Implementation framing: Option A from `documentation/v1.2-bench-followup.md` §6 — ship v1.2 with the actual T6 fix landed rather than carrying the regression into v1.3.

## Performance Metrics (v1.0 + v1.1 — cumulative, frozen)

**v1.0 totals:**
- 5 phases, 17 plans (16 autonomous + 1 human-gated), 208 tests
- 85 commits, 5891 LOC F#
- ~27 hours (2026-04-22 14:37 → 2026-04-23 17:18)

**v1.1 totals:**
- 2 phases, 5 plans (3 in Phase 6 incl. 06-03 gap closure, 2 in Phase 7)
- 218 tests (208 v1.0 baseline + 10 v1.1 additions)
- 23 commits, +315 / -124 LOC F# delta
- ~19 hours (2026-04-23 17:32 → 2026-04-24 12:21)

Detailed per-plan history archived in `.planning/milestones/v1.0-phases/` and `.planning/milestones/v1.1-phases/`.

## Accumulated Context

### Decisions

Rolled up into PROJECT.md Key Decisions table at v1.0 + v1.1 milestone completions. See `.planning/PROJECT.md` for cumulative log with outcomes.

Notable items marked `⚠ Revisit` carried into v1.2:
- Expecto `[<Tests>]` auto-discovery disabled — multiple executors hit rootTests registration pitfall; document in each new test module
- `makeMockResponse` helper duplicated in AgentLoopTests.fs + ReplTests.fs — v1.2 test infra pass candidate (TST-01, deferred)

New decisions from Phase 9.1 plan 04 (rolled up at v1.2 close):
- W1 user-prompt/system-prompt conflict: user prompt explicitly naming a tool ("using write_file") overrides system-prompt NEVER directive; code-level enforcement (loop-injection or schema rejection) required; wording-only class exhausted
- W2 Fix 3 confirmed closed by directive no-verify-read clause (4→3 PASS); W2 prompt does not name a specific tool

New decisions from Phase 9 plan 01 (rolled up at v1.2 close):
- read_file metadata header uses input `path` (relative), never `resolved` (absolute) — preserves CLAUDE.md no-absolute-paths invariant
- Out-of-range branch preserves the RAW requested `endLine` (no clamp to totalLines) so the bounds-violation signal is unambiguous to the LLM
- Unified `readFileImpl` on `File.ReadAllLines` (eager array) replacing the prior mix of `ReadAllText` + `ReadLines`
- Test substring assertions for line content must anchor with `\n` to avoid collision with header words (e.g. `truncated` contains `a`, `lines` contains `e`) — generalizable pattern for any future tool that prepends a fixed-format header
- `dotnet test` does NOT run Expecto tests in this project — `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` is the canonical runner. Plan-supplied `dotnet test --filter` lines are non-functional with the explicit `rootTests` + `[<EntryPoint>]` pattern

### Pending Todos

v1.3+ seed candidates (not v1.2 scope):
- Per-port `MaxModelLen` visibility (OBS-06)
- Shared `makeMockResponse` test helper (TST-01)
- SSE streaming output (STM-01)
- Session persistence + `--resume` (SES-01)
- Auto-escalation on MaxLoopsExceeded (ROU-05)
- System prompt length reduction (PERF-01, needs research)
- Prompt cache hygiene / launchd kickstart (OPS-01)

### Blockers/Concerns

None blocking 9.1 planning. Pre-existing structural state: 240 tests passing, Core diff empty for Phase 9, all spec-level v1.2 REQs Complete. Behavioral concerns are the explicit subject of Phase 9.1 — see `.planning/v1.2-MILESTONE-AUDIT.md` `tech_debt[]` for the bench evidence and `documentation/v1.2-bench-followup.md` §1 for root-cause attribution.

## Session Continuity

Last session: 2026-04-25
Stopped at: Completed 09.1-04-PLAN.md (Fix 3 wording iteration — W2 closed; W1 still open; revert committed).
Resume file: None — execute Plan 09.1-05 (code-level W1 fix: loop-injection after edit_file success), then `/gsd:verify-work 9.1` → `/gsd:audit-milestone` → `/gsd:complete-milestone`.
