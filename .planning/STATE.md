# Project State

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-04-28 after v2.1 milestone complete)

**Core value:** Mac 로컬 Qwen 3.5 122B를 strong-typed F# agent loop로 **empirically** 안정적으로 돌린다 (post-v2.1 verdict 82/100 KEEP confirmed; single-model canonical; 35B retained as cold rollback via `--with-35b`)
**Current focus:** Between milestones. v2.2 scoping pending — first data-driven candidate: PLAN-04 5-step ceiling raise (CORR-EVAL-02 FAIL constraint discovery).

## Current Position

Milestone: **between milestones** (v2.1 shipped 2026-04-28 with 82/100 KEEP)
Phase: None active
Plan: None active
Status: Ready to scope v2.2 via `/gsd:new-milestone` or daily-drive observation window before scoping
Last activity: 2026-04-28 — v2.1 milestone complete; archived to `.planning/milestones/v2.1-{ROADMAP,REQUIREMENTS,MILESTONE-AUDIT}.md` + `v2.1-phases/`

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 ✓ → v1.4 ✓ → v2.0 ✓ → v2.1 ✓ → v2.2 ○ (not started)

## Performance Metrics (cumulative, frozen)

**v1.0:** 5 phases, 17 plans, 208 tests, 5891 LOC F#, 85 commits, ~27h
**v1.1:** 2 phases, 5 plans, 218 tests (+10), +315/-124 LOC, 23 commits, ~19h
**v1.2:** 3 phases, 8 plans, 242 tests (+24), Core diff confined to AgentLoop.fs/Domain.fs, 43 commits, ~3 days
**v1.3:** 2 phases, 6 plans, 243 tests (+1), bench harness in repo + 54% prompt shrink + B2 recovery, 25 commits, ~1 day
**v1.4:** 2 phases, 2 plans, 243 tests (unchanged), zero src/ diff, 7 commits, ~1 day
**v2.0:** 7 phases, 19 plans, 282 tests (+39), 41 files +3993/-335 LOC, 106 commits, ~2 days, -85 GB disk (Qwen 2.5 retirement); bench gate trajectory 8/8→6/6→7/7
**v2.1:** 1 phase (single), 5 plans, 282 tests (unchanged — observational), 31 files +6463/-26 LOC, ~25 commits, ~1.5 days; verdict 82/100 KEEP; bench gate 7/7 PASS preserved; zero src/ diff

Detailed per-plan history archived in `.planning/milestones/v{1.0,1.1,1.2,1.3,1.4,2.0,2.1}-phases/`.

## Accumulated Context

### Decisions

Cumulative log in `.planning/PROJECT.md` Key Decisions table. See PROJECT.md for outcomes through v2.0.

Stable patterns established across milestones (load-bearing for next session):

- **Core purity** — `src/BlueCode.Core/**` no Serilog/Spectre/Argu/HttpClient/file I/O; `task {}` only (CI-enforced via `scripts/check-no-async.sh`).
- **Test discovery** — explicit `rootTests` list in `RouterTests.fs` + ordered `<Compile Include>` in `BlueCode.Tests.fsproj`. New test modules need BOTH places. Preserve all existing entries on each addition.
- **Canonical test runner** — `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` (NOT `dotnet test`).
- **Bench gate** — `bash bench/run.sh --gate` is the structural authority. Currently 7/7 PASS with single-model 122B baseline (T6_122b, T5_122b, B2_122b, T1_122b, W1_122b, W2_122b, MT_122b).
- **Atomic commits** — `{type}({phase}-{plan}): {name}` per task; plan-meta separate; phase-complete bundles ROADMAP/STATE/REQUIREMENTS/VERIFICATION. NEVER `git add -A` or `git add .`.
- **Loop-injection primitive** — `lastEditPath` + `lastReadHint` in `runLoop`; post-user `Role = User` System-style messages enforce tool-terminality at conversation-history layer. Established in v1.2 (TLX-01/9.1-05) and extended in v1.3 (PERF-02). Phase 16's `[PLAN REJECTED]` re-prompt follows the same pattern.
- **Role = User invariant** (Phase 17-02 + Phase 20-03) — Qwen 3.5 122B REJECTS mid-conversation `Role = System` with HTTP 404. ALL mid-conversation injections (POST-EDIT CONSTRAINT, POST-READ HINT, [PLAN REJECTED], [PARSE ERROR] correction) MUST be `Role = User`. Authority signal is in the bracketed text marker, NOT the role. Documented at AgentLoop.fs:249/260/266 + `scripts/probe-system-role.sh` + 3 howto files.
- **Single-model 122B canonical** (Phase 19) — `blueCode "..."` defaults to 122B with no flag. `--model 32b/72b` retired (exit 2 with retirement error). `--model 35b` requires `--with-35b` opt-in flag AND 35B service loaded. Default invocation never probes port 8000.
- **Persistence** (v2.0) — `~/.bluecode/sessions/<id>.jsonl` (per-turn TurnComplete envelopes; `version: 2` header). v1 per-step crash log at `~/.bluecode/session_<ts>.jsonl` coexists. `--resume <id>` and `--new-session` Argu flags; mutually-exclusive validation post-parse.
- **Plan-then-execute** (v2.0) — `--plan` flag enables plan-mode (single-turn only; `--plan --resume <id>` valid; `--plan --with-35b` rejected). `runPlanTurn` returns `Task<Result<Plan, AgentError>>` with 2-attempt retry. PlanGate.fs renders Spectre table + a/r/e/q dispatch via `IKeyReader` port. PlanValidator (3 structural rules in Core) + JSON parse layer (4th schema-invalid rule in Cli) catch all malformed plans before user sees approval prompt.
- **5-step max preserved** — `Plan.Steps.Length ≤ 5` (PLAN-04). Planning doesn't unlock more steps; just front-loads the decision.

### Pending Todos (v2.1 candidates)

For awareness only — DO NOT auto-pull. v2.1 scope comes from observation window `/gsd:add-todo` entries, not this list:

- **Compaction** — PERSIST-02 saves full session JSONL; long sessions hit 80% context warning faster
- **Slash commands** (`/sessions`, `/plan`, `/clear`) — UX layer over CLI flags
- **Sub-agent delegation** — Now meaningful since memory + planning land
- **Plan-mode bench fixture** — Deferred from Phase 16-03; mocked-IKeyReader pattern would substitute
- **Thinking-mode-on** — Consume `<think>` blocks; requires `max_tokens` 1024→2048-4096 + re-bench
- **Native OpenAI `tool_calls`** — Replaces custom JSON schema; rewrites `toLlmOutput` + all bench fixtures
- **Streaming output (STM-01)** — Deferred 7th cycle
- **Session listing UI** (`--list-sessions` or `/sessions`) — `ls ~/.bluecode/sessions/` sufficient for v2.0
- **Branching/forking sessions** — Single-resume only in v2.0
- **Session compaction / pruning** — Manual `rm -rf` for now

### Blockers/Concerns

None. v2.0 closed cleanly; bench gate 7/7 PASS; tests 282/1/0; daily-driver running stable on Qwen 3.5 122B canonical.

Documentation drift items flagged in v2.0 audit (non-blocking, archived):
- Phase 20 missing formal `20-VERIFICATION.md` (per-plan SUMMARYs substitute)
- CLAUDE.md Key Seams has 1 stale "72B" sentence (historical)
- `documentation/` retains legacy 32B/72B install docs (kept as historical reference)

## Session Continuity

Last session: 2026-04-28 (v2.1 milestone closeout)
Stopped at: v2.1 archived; ready to scope v2.2 or open observation window
Resume file: None
Next workflow trigger: `/gsd:new-milestone` (or daily-drive observation before scoping)

## Empirical Baselines (post-v2.1, load-bearing for v2.2 scoping)

These are the measured baselines from v2.1. Use them as input when scoping v2.2 candidates.

- **HumanEval+ chat pass@1 = 0.939 / pass@1+ = 0.902** — re-evaluation trigger if mlx_lm.server major version, Qwen 3.5 model card update / YaRN config change, or sampling defaults change
- **Throughput median 34.6 tok/s; TTFT median 222 ms warm** — interactive UX baseline
- **Schema rate 0/50 InvalidJsonOutput** — perfect compliance; v2.0 architecture decisions (strict JSON schema + 2-attempt retry + 5-step loop guard + thinking-mode-off) validated under stricter stress
- **Multi-turn coherence stable through N=7** — refutes mlx-lm#1011 "approximately 5 rounds" community claim in our environment
- **Needle 4/4 at max_model_len=32768** — mlx_lm.server does not expose YaRN-extended ceiling in /v1/models; 32k is the conservative working assumption
- **5-step PLAN-04 ceiling structurally blocks multi-file refactor** — CORR-EVAL-02 FAIL with orphan_count=1; first v2.2 candidate, data-driven; needs Core change (Domain.fs Plan validator)
- **Coding-quality 6/10 (idiomatic F# 1/5)** — generated F# is correct but procedural; pipelines / DU / pattern matching usage is low. Observation window will determine if this becomes a v2.2 candidate (system prompt F# style hint? few-shot?)

For full per-section results, see `documentation/qwen35-122b-coding-eval.md`. Per-plan execution history archived in `.planning/milestones/v2.1-phases/`.
