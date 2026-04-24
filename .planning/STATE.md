# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-04-24 for v1.2 milestone start)

**Core value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Current focus:** v1.2 Tool Expansion — edit_file + glob_search + grep_search + read_file metadata

## Current Position

Milestone: v1.2 Tool Expansion (started 2026-04-24)
Phase: Not started (defining requirements → roadmap)
Plan: —
Status: PROJECT.md updated with v1.2 Current Milestone section (4 REQs: TLX-01..03, TOOL-08). Scope confirmed as "Minimum" per user — pain-evidence backed only. REQUIREMENTS.md + ROADMAP.md to be created next.
Last activity: 2026-04-24 — v1.2 milestone started; scope decision logged in PROJECT.md + STATE.md

Progress: v1.2 [░░░░░░░░░░░░░░░░░░░░] 0% (0 of 4 REQs)

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

**Rolled up into PROJECT.md Key Decisions table at v1.0 + v1.1 milestone completions.** See `.planning/PROJECT.md` → "Key Decisions" for cumulative log with outcomes (✓ Good / ⚠ Revisit / — Pending).

Notable items marked `⚠ Revisit` for v1.2:
- Expecto `[<Tests>]` auto-discovery disabled — documented convention, multiple executors hit rootTests registration pitfall across v1.0 + v1.1
- `makeMockResponse` test helper duplicated in 2 test files — v1.2 test infra pass candidate

### Pending Todos (v1.2 seed candidates)

Carried forward from v1.1 into v1.2 planning — not full requirements yet, but known gaps the next milestone should scope:

1. **Per-port `MaxModelLen` visibility** — `AppComponents.MaxModelLen` hardcoded `int = 8192` floor; 72B's actual max_model_len (e.g., 32K) not surfaced. Per-port cache exists inside `QwenHttpClient` lazy probe; plumbing to `AppComponents` is the remaining work.
2. **Shared test helper module** — consolidate `makeMockResponse` (currently in AgentLoopTests.fs + ReplTests.fs).
3. **Multi-platform `tryParseModelId`** — current `StartsWith("/")` heuristic is macOS/Linux only; Windows path detection (if ever in scope) needs different approach.
4. **Streaming output (STM-01)** — SSE token-by-token stdout, addresses "blank terminal UX" pitfall documented in v1.0 research.
5. **Tool extensions (TLX-01..03)** — `edit_file` for surgical edits (avoid full-file write diff noise), `glob_search` for file finding, `grep_search` for content search.
6. **Session persistence + `--resume <id>` (SES-01)** — context carry across process boundaries.
7. **Prompt cache hygiene** — long-running mlx_lm.server accumulates prompt cache (observed 0.70 → 0.83 GB in 3 runs; 1.51 GB was OOM threshold pre-fix). Periodic `launchctl kickstart` or explicit cache clear endpoint candidate.

**Added 2026-04-24 (from /gsd:new-milestone questioning + benchmark insights):**

8. **`edit_file` (surgical exact-string edit)** — current `write_file` replaces full content; 1-line fix on 1000-line file requires whole-file rewrite. Token cost + generation latency (observed 72B W2 write step = 13.8s, much of it content regeneration). Surgical edit via exact-string old/new matching (ref: v1.0 research TLX-01).
9. **`grep_search` (content pattern)** — currently workflows use `run_shell "grep -r ..."` with bash_security.py gating; native tool avoids gate overhead + provides structured output (file:line:content). Ref: v1.0 research TLX-03.
10. **`glob_search` (file finding)** — similar reasoning: `find` via run_shell works but security-gate friction. Native tool cleaner. Ref: v1.0 research TLX-02.
11. **`read_file` output enhancement** — optionally return total file length / line count in metadata so agent knows file bounds without trial-and-error. T6 benchmark failure (32B requesting `start_line=2001,4001,6001` on 150-line file) was partially due to missing "file size" signal.
12. **Auto-escalation on `MaxLoopsExceeded`** — when 32B hits 5-step limit (e.g., T6), automatic re-run with 72B within same invocation. Currently user must manually `--model 72b`. Research open: history re-use across model switch, or fresh start?
13. **Ctrl+C/cancellation indicator** — during long LLM inference, Ctrl+C ends turn silently. Consider brief "Cancelling..." message + incremental progress (e.g., partial token count) for better UX feedback.
14. **Default system prompt length reduction** — current ~1200 chars. Bench data shows it consumes ~300 tokens per request (KV cache overhead). Tightening to ~600 chars could cut per-request latency ~15-20%. Needs careful revision to preserve JSON schema clarity.

### Resolved post-milestone

(None yet — v1.1 just shipped.)

### Blockers/Concerns

(None — v1.1 shipped clean with live verification.)

## Session Continuity

Last session: 2026-04-24
Stopped at: v1.1 milestone complete + archived + git tag `milestone-v1.1`. Ready for v1.2 scoping.
Resume file: None — use `/gsd:new-milestone` to start v1.2.
