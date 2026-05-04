# Project State

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-04-29 after v2.5 REPL ergonomics milestone started)

**Core value:** Mac 로컬 Qwen 3.5 122B를 strong-typed F# agent loop로 **empirically** 안정적으로 돌린다 (post-v2.4 verdict 96/100 KEEP; all 4 dimensions ≥80%; widest KEEP margin in project history)
**Current focus:** v2.5 REPL ergonomics — bundling slash commands (SLASH-01..07) + `/edit` multi-line input + PrettyPrompt readline + history + token visibility. Cli-layer only; Core purity preserved; bench/schema invariants untouched.

## Current Position

Milestone: v2.5 REPL ergonomics (started 2026-04-29)
Phase: 36 (manual-test-fixes) — COMPLETE (3/3 plans complete)
Plan: 3 of 3 complete (36-03 prompt-suffix-and-doc)
Status: Phase 36 complete; planSystemPromptSuffix tightened (T-75/T-76); --allow-paths docs updated; bench gate 7/7 PASS; 345 tests passing.
Last activity: 2026-05-04 — Completed 36-03-prompt-suffix-and-doc-PLAN.md (+1 test, +578 chars suffix, bench gate 7/7).

Next: /gsd:verify-work 36 UAT gate; then Phases 33-35 (REPL slash-cmd bundling etc.) pending.

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 ✓ → v1.4 ✓ → v2.0 ✓ → v2.1 ✓ → v2.2 ✓ → v2.3 ✓ → v2.4 ✓ → **v2.5 (in progress — Phases 31, 32, 36 COMPLETE; Phases 33-35 pending)**

## Performance Metrics (cumulative, frozen)

**v1.0:** 5 phases, 17 plans, 208 tests, 5891 LOC F#, 85 commits, ~27h
**v1.1:** 2 phases, 5 plans, 218 tests (+10), +315/-124 LOC, 23 commits, ~19h
**v1.2:** 3 phases, 8 plans, 242 tests (+24), Core diff confined to AgentLoop.fs/Domain.fs, 43 commits, ~3 days
**v1.3:** 2 phases, 6 plans, 243 tests (+1), bench harness in repo + 54% prompt shrink + B2 recovery, 25 commits, ~1 day
**v1.4:** 2 phases, 2 plans, 243 tests (unchanged), zero src/ diff, 7 commits, ~1 day
**v2.0:** 7 phases, 19 plans, 282 tests (+39), 41 files +3993/-335 LOC, 106 commits, ~2 days, -85 GB disk; bench gate trajectory 8/8→6/6→7/7
**v2.1:** 1 phase (single), 5 plans, 282 tests (unchanged — observational), 31 files +6463/-26 LOC, ~25 commits, ~1.5 days; verdict 82/100 KEEP; zero src/ diff
**v2.2:** 2 phases (22, 23), 5 plans (4+1), 284 tests (+2 boundary), 27 files +3569/-81 LOC, 19 commits, ~1 day; verdict 82→87/100 KEEP
**v2.3:** 4 phases (24, 25, 26 BLOCKED-historical, 27), 9 plans (8+1 blocked-by-design), 287 tests (+3 PlanValidator boundary), 34 files +9152/-54 LOC, 31 commits, ~2 days; verdict 87→92/100 KEEP; bench gate 7/7 PASS preserved; src/ diff confined to 4 files
**v2.4:** 2 actual phases (28, 30; Phase 29 SKIPPED-by-design — never created), 7 plans, 287 tests (unchanged — F# bench fixtures, not Expecto), 37 files +6816/-38 LOC (heavily docs/eval/research-driven), 21 commits, ~3 hours same-day; verdict 92→96/100 KEEP (Coding-quality 6/10→10/10 via F# fixture Path A disprove); bench gate 7/7 PASS preserved; **zero src/ diff**

Detailed per-plan history archived in `.planning/milestones/v{1.0,1.1,1.2,1.3,1.4,2.0,2.1,2.2,2.3,2.4}-phases/`.

## Accumulated Context

### Decisions

Cumulative log in `.planning/PROJECT.md` Key Decisions table. See PROJECT.md for outcomes through v2.4.

Stable patterns established across milestones (load-bearing for next session):

- **Core purity** — `src/BlueCode.Core/**` no Serilog/Spectre/Argu/HttpClient/file I/O; `task {}` only (CI-enforced via `scripts/check-no-async.sh`).
- **Test discovery** — explicit `rootTests` list in `RouterTests.fs` + ordered `<Compile Include>` in `BlueCode.Tests.fsproj`. New test modules need BOTH places.
- **Canonical test runner** — `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` (NOT `dotnet test`).
- **Bench gate** — `bash bench/run.sh --gate` is the structural authority. 7/7 PASS post-v2.4 with single-model 122B (T6/T5/B2/T1/W1/W2/MT).
- **Atomic commits** — `{type}({phase}-{plan}): {name}` per task; plan-meta separate; phase-complete bundles. NEVER `git add -A` or `git add .`.
- **Role = User invariant** (Phase 17-02 + Phase 20-03) — Qwen 3.5 122B REJECTS mid-conversation `Role = System` with HTTP 404. ALL mid-conversation injections are `Role = User`.
- **Single-model 122B canonical** (Phase 19) — `blueCode "..."` defaults to 122B; 35B requires `--with-35b` opt-in flag AND service loaded.
- **10-step PLAN-04 ceiling** (Phase 22-01) — `PlanValidator.MaxPlanSteps = 10` + `AgentConfig.MaxLoops = 10` (independent constants).
- **P1 enumeration directive in `defaultSystemPrompt`** (Phase 27-01) — Conditional clause "When the task requires renaming or restructuring multiple symbols..." applies to BOTH agent-loop and plan-mode invocations. Char count: 967.
- **P2 few-shot example in `planSystemPromptSuffix`** (Phase 24-02; plan-mode-only) — 3-line Example/Targets/Steps block. Suffix char count: 1577 (was 999 pre-Phase 36-03).
- **Combined plan-mode prompt char count INVARIANT post-Phase 36-03** — defaultSystemPrompt(967) + "\n\n" + planSystemPromptSuffix(1577) = 2546 chars (was 1968 pre-Phase 36-03; Phase 36-03 added MAXIMUM 10 HARD LIMIT + Path rules clauses).
- **PlanValidator pre-flight `checkRenameTargetsEnumerated`** (Phase 25-01) — Interpretation B detail-string encoding; PlanValidator.fs:108; bound at validatePlan:143; called from AgentLoop.fs:484. Plan-mode only.
- **Plan-mode vs agent-loop distinction is load-bearing** (v2.3 lesson; preserved through v2.4) — `defaultSystemPrompt` for ALL invocations; `planSystemPromptSuffix` only when `--plan`; `PlanValidator.checkRenameTargetsEnumerated` only in `runPlanTurn`.
- **Mandatory `launchctl kickstart` pre-flight for evaluations** (v2.3 Phase 26 Diagnostic D; reused in v2.4 Phase 28-04) — `launchctl kickstart -k gui/501/com.ohama.qwen122b` clears KV cache contamination.
- **renderStatus primitive-arg signature** (Phase 31-02) — `renderStatus` takes `(Session, Model option, int)` not `AppComponents`; `Rendering.fs` compiles before `CompositionRoot.fs` so passing `AppComponents` would be a forward reference. Caller extracts fields at call site.
- **No Save on /clear** (Phase 31-02) — `FileSessionStore.Save` creates `.jsonl` lazily on first write; empty new session has nothing to persist; old session jsonl stays untouched by design.
- **Slash dispatcher test pattern** (Phase 31-02) — Repl integration tests use `stubLlm []` (throws on first call) to assert 0 LLM calls from in-process slash commands; any routing leak fails fast.
- **listRecent NOT on ISessionStore** (Phase 32-01) — `listRecent : int -> SessionMeta list` is a Cli-layer module-level function in `FileSessionStore.fs`, NOT an ISessionStore member. ISessionStore stays frozen at Save + Load (Core purity invariant).
- **SessionMeta NOT [<CLIMutable>]** (Phase 32-01) — Plain F# domain record (Id, StartedAt, TurnCount, FirstPromptExcerpt); CLIMutable is reserved for JSON DTOs (SessionHeader, TurnEnvelope). Two-level excerpt truncation: listRecent caps at 80 chars, renderSessions display column caps at 40 chars + "...".
- **"first thought" column label in renderSessions** (Phase 32-01) — User prompt not stored in jsonl; first envelope first step Thought is best proxy. Column labeled "first thought" not "first prompt". Research § Open Question #1 resolution.
- **No Save on /resume** (Phase 32-02) — `FileSessionStore.Save` creates `.jsonl` lazily on first write; loaded session is already on disk; saving on resume would write a duplicate envelope. Per-Prompt-turn Save (inside Prompt arm) handles persistence from next user prompt onward.
- **currentSession mutable rebind is the sole in-place switch** (Phase 32-02) — `currentSession <- loaded` is all that's needed; next iteration reads `currentSession.Steps` directly as priorSteps argument. No separate priorSteps variable exists.
- **Empty-arg guard (Resume "") matched before general (Resume id)** (Phase 32-02) — Prevents empty SessionId from reaching `sessionStore.Load`; produces "usage: /resume <session-id>" hint instead.
- **Slash dispatcher extension pattern** (Phase 32-02) — Add new arms ABOVE Prompt arm; slim future-stub by removing handled commands; use `printfn` not `AnsiConsole.MarkupLine`; use `let!` inside `task {}` CE for async calls. Phases 33+34 follow this same pattern.
- **macOS bash-strict-mode patterns documented** (5 patterns across v2.1-v2.4): set-e/dotnet-exit (v2.1 21-03); grep-c/pipefail double-output (v2.1 21-04); mkdir-before-tee (v2.2 23-01, commit `a6159c4`); orphan-grep-zero-match-exit-1 (v2.3 27-02, commit `9f8e06e`); grep-oE-pipeline-zero-match (v2.4 28-01, commit `94d905c`). Common rule: under `set -euo pipefail`, `grep -c`/`grep -cE`/`grep -oE` exits 1 on zero-match; guard with `|| true`. Full reference: `documentation/howto/macos-bash-strict-mode-patterns.md`.
- **F# fixture infrastructure** (v2.4 Phase 28) — `bench/fixtures/fs_idiomatic/` with 3 fixture pairs; `--fs-idiomatic` mode in `bench/eval-qwen35-122b.sh` lines 328-391. Reusable for future F# coding-quality work. Research Q4 verbatim binary 5-criterion rubric (C1-C5) + sum-then-scale band table aggregate.
- **FSI fixture skeleton pattern** (v2.4 Phase 28-02) — `.fs` skeletons use direct top-level `try...with` + printfn (NO `[<EntryPoint>]`). FSI executes module-level code directly. `dotnet fsi <file>.fs </dev/null` always exits 1 (interactive EOF); compile verification uses absence of `error FS` in output, not exit code.
- **Sum-then-scale band table aggregate (v2.4 Phase 28-04)** — For multi-fixture rubric scoring, band table beats round(average) for outlier robustness.
- **Conditional SKIP-by-design pattern (v2.4 Phase 29)** — When a conditional phase doesn't trigger, never create the phase directory (cleaner than BLOCKED-with-record). Use BLOCKED preservation only when partial-attempt produces diagnostic value (v2.3 Phase 26).
- **Zero-source-diff milestone pattern (v2.1 + v2.4)** — Eval/measurement work can ship meaningful capability without `src/` changes. `git diff milestone-v<prev> HEAD -- src/` empty assertion is an established invariant for these milestones.
- **Measurement-first scoping (v2.4)** — Inverted v2.2/v2.3's intervention-first pattern. When prior measurement evidence is category-mismatched (HumanEval+ Python answers vs Idiomatic F# scoring), proper category-matched fixtures may yield free score improvement. *"Is the score real, or is the measurement instrument wrong?"*

### Roadmap Evolution

- **Phase 36 added (2026-05-04):** "Manual test fixes" — independent, post-shipment hardening from manual test round 1. Scope: (1) glob_search recursive default, (2) PlanValidator UX rebuild (retry feedback + placeholder enum guard + PlanRejected friendly error), (3) **`--allow-paths` CLI flag for explicit scratch dir opt-in** (replaces manual-test-guide path-rewrite approach; user opt-in preserves security invariant), (4) hallucinated-success investigation. Out of scope (deferred): auto/default `/tmp/*` allowlist (only explicit `--allow-paths`), priorSteps message-ordering quirk, glob/wildcard patterns in --allow-paths. No new requirements; bench 7/7 PASS preserved invariant. Cli + doc only, Core untouched.

### Pending Todos

None pending. v2.5 scoping comes from observation window via `/gsd:new-milestone`.

### Blockers/Concerns

None active. All v2.2/v2.3/v2.4 surfaced bottlenecks resolved:
- ~~CORR-EVAL-02 FAIL (v2.2 + Phase 26)~~ — RESOLVED in v2.3 Phase 27
- ~~Persistent extraction bias on shared-prefix function names~~ — RESOLVED via P1 directive in `defaultSystemPrompt`
- ~~Architectural scope mismatch (P1+P2+P3 plan-mode-only vs agent-loop eval)~~ — RESOLVED via Phase 27 P1 migration
- ~~Idiomatic F# 1/5 sub-score (Python-transcript artifact)~~ — RESOLVED in v2.4 Phase 28 via F# fixture rescore (5/5)

Documentation drift items flagged in v2.0 audit (non-blocking; carried-forward; eligible for v2.5 cleanup):
- Phase 20 missing formal `20-VERIFICATION.md` (per-plan SUMMARYs substitute)
- CLAUDE.md Key Seams has 1 stale "72B" sentence (historical)
- `documentation/` retains legacy 32B/72B install docs (kept as historical reference)

## Session Continuity

Last session: 2026-05-04T18:09:30Z
Stopped at: Completed 36-02-allow-paths-PLAN.md (Plan 36-02 complete; --allow-paths flag; 344 tests)
Resume file: None
Next workflow trigger: `/gsd:execute-phase 36` (Plan 36-03 remaining)

## Empirical Baselines (post-v2.4)

Load-bearing measurements for v2.5+ scoping:

- **HumanEval+ chat pass@1 = 0.939 / pass@1+ = 0.902** (v2.1) — re-evaluation trigger if mlx_lm.server major version, Qwen 3.5 model card update / YaRN config change, or sampling defaults change
- **Throughput median 34.6 tok/s; TTFT median 222 ms warm** (v2.1) — interactive UX baseline
- **Schema rate 0/50 InvalidJsonOutput** (v2.1) — perfect compliance
- **Multi-turn coherence stable through N=7** (v2.1)
- **Needle 4/4 at max_model_len=32768** (v2.1)
- **Cold-start 37s warm-OS-cache** (v2.2 Phase 23)
- **10-step PLAN-04 ceiling** (Phase 22-01)
- **CORR-EVAL-02 PASS** (Phase 27-02; 2026-04-29) — orphan_count=0 with all 3 v2.3 prongs in production
- **Idiomatic F# 5/5** (v2.4 Phase 28-04; UPDATED from 1/5 in v2.1) — F# fixture rescore: grand_total 13/15 (band ≥12) → mapped 5/5 → `passed_disprove_1of5`. Prior 1/5 was Python-transcript artifact (category mismatch).
- **Coding-quality 10/10** (v2.4 Phase 28-05; UPDATED from 6/10 in v2.1/v2.3) — Idiomatic F# 5/5 + Tests compile+pass 3/3 + Bug identification 2/2 = 10/10 (100%).
- **Aggregate verdict 96/100 KEEP** (post-v2.4) — Correctness 36/40, Performance 25/25, Reliability 25/25, Coding-quality 10/10. **All 4 dimensions ≥80%.** Widest KEEP margin in project history.

For full per-section results, see `documentation/qwen35-122b-coding-eval.md`. Per-plan execution history archived in `.planning/milestones/v{1.0..2.4}-phases/`.
