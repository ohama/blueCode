# Project State

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-04-27 after v2.0 milestone complete)

**Core value:** Mac 로컬 Qwen 3.5 122B를 strong-typed F# agent loop로 안정적으로 돌린다 (single-model canonical post-v2.0; 35B retained as cold rollback via `--with-35b`)
**Current focus:** v2.1 Empirical Qwen 3.5 122B Coding Evaluation — Phase 21, 5 plans, ~2hr eval + ~2hr analysis. Multi-dimensional measurement (Performance / Correctness / Reliability / Documentation) producing `documentation/qwen35-122b-coding-eval.md` with 100-point scorecard verdict.

## Current Position

Milestone: v2.1 Empirical Qwen 3.5 122B Coding Evaluation (started 2026-04-27)
Phase: 21 (single phase) — in progress; Wave 2 complete
Plan: 2 of 5 complete (21-01: Harness Scaffolding ✓, 21-02: HumanEval+ ✓)
Status: HumanEval+ chat pass@1 = 0.939 / pass@1+ = 0.902 (headline); completion pass@1 = 0.226; bench gate 7/7 PASS
Last activity: 2026-04-28 — Completed 21-02-PLAN.md (HumanEval+ HTTP adapter; 328 inferences, ~61 min; two macOS scoring bugs found and fixed: evalplus.sanitize + EVALPLUS_MAX_MEMORY_BYTES=-1)

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 ✓ → v1.4 ✓ → v2.0 ✓ → v2.1 ◆ (Phase 21: 2/5 plans complete)

## Performance Metrics (cumulative, frozen)

**v1.0:** 5 phases, 17 plans, 208 tests, 5891 LOC F#, 85 commits, ~27h
**v1.1:** 2 phases, 5 plans, 218 tests (+10), +315/-124 LOC, 23 commits, ~19h
**v1.2:** 3 phases, 8 plans, 242 tests (+24), Core diff confined to AgentLoop.fs/Domain.fs, 43 commits, ~3 days
**v1.3:** 2 phases, 6 plans, 243 tests (+1), bench harness in repo + 54% prompt shrink + B2 recovery, 25 commits, ~1 day
**v1.4:** 2 phases, 2 plans, 243 tests (unchanged), zero src/ diff, 7 commits, ~1 day
**v2.0:** 7 phases, 19 plans, 282 tests (+39), 41 files +3993/-335 LOC, 106 commits, ~2 days, -85 GB disk (Qwen 2.5 retirement); bench gate trajectory 8/8→6/6→7/7

Detailed per-plan history archived in `.planning/milestones/v{1.0,1.1,1.2,1.3,1.4,2.0}-phases/`.

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

Last session: 2026-04-28 (Phase 21 Wave 2 wrap-up)
Stopped at: Completed 21-02-PLAN.md — HumanEval+ chat 93.9% / 90.2%; completion 22.6% / 21.3%; two macOS scoring bugs (sanitize + RLIMIT_AS) diagnosed and fixed in handler; bench gate 7/7 PASS
Resume file: None
Next workflow trigger: `/gsd:execute-phase 21` Wave 3 → 21-03 (--refactor / --langcoverage handlers)

### New Decisions (21-02)

- **HumanEval+ chat pass@1 = 0.939 / pass@1+ = 0.902 (headline)** — Qwen 3.5 122B-A10B-4bit MoE in the upper tier of open-weight coding models. Completion mode 0.226/0.213 is informational; chat mode is what blueCode actually uses at runtime.
- **macOS evalplus scoring trap #1: doubled signature.** evalplus.evaluate stitches prompt + completion. Chat-mode completions are full function definitions (signature + docstring + body), producing doubled signatures that fail to parse → silent pass@1=0. Fix: `python -m evalplus.sanitize <input>.jsonl` BEFORE `evalplus.evaluate`. Now baked into `bench/eval-qwen35-122b.sh run_humaneval()`.
- **macOS evalplus scoring trap #2: RLIMIT_AS exceeds hard limit.** `evalplus.eval.utils.reliability_guard` calls `resource.setrlimit(RLIMIT_AS, ...)` with 4 GiB default; macOS per-process hard limit is lower → every test subprocess crashes pre-execution with `ValueError("current limit exceeds maximum limit")`. Fix: set env var `EVALPLUS_MAX_MEMORY_BYTES=-1` so `query_maximum_memory_bytes()` returns `None` and `reliability_guard` skips setrlimit. Now baked into `run_humaneval()`.
- **evalplus.evaluate result caching** — writes `<samples>_eval_results.json` cache; subsequent runs against the same samples file load from cache. Delete cache before re-scoring to force fresh evaluation. Not a regression-gate concern (each fresh eval LOG_DIR is unique).
- **Both adapter and harness preserved on disk** — `bench/eval-humaneval-http.py` (159 lines, no `mlx_lm` imports) + `bench/eval-qwen35-122b.sh run_humaneval()` (now sanitize-aware). 21-03 + 21-04 + 21-05 inherit a fully-working scoring pipeline.
- **Wall-clock**: chat ~28 min, completion ~33 min, total ~61 min for 328 inferences (within ~55 min plan estimate, slight overrun on completion mode).

## v2.1 Architectural Touch Points (load-bearing)

- **Plan file is source-of-truth for scope:** `/Users/ohama/.claude/plans/async-weaving-pnueli.md` — 5-task structure, file map, reuse map, risk register, 100-point scorecard rubric. Approved 2026-04-27.
- **Hybrid bash + Python(venv):** Pure-bash for performance/reliability/refactoring (reuses `bench/run.sh:30-46` `run()`, `bench/run.sh:111-157` `mt()`, `bench/run.sh:181-186` port precondition). Python (in `bench/.venv-eval/`) for HumanEval+ scoring (`evalplus` library) and long-context needle (mlx-runner template adapted to HTTP).
- **mlx-runner constraint:** Sibling project `/Users/ohama/projs/mlx-runner/` uses `mlx_lm.load()` in-process; would OOM the launchd-managed 122B service (~70GB resident). MUST adapt prompts/methodology to call `localhost:8001/v1/chat/completions` over HTTP — never load a second instance.
- **Bench gate stability mandatory post-eval:** `bash bench/run.sh --gate` exit 0 with 7/7 PASS must hold. Eval is purely external instrumentation; modifies fixtures (multi-file refactor) but EXIT trap restores them. NO `bench/baseline.json` or `src/` changes.
- **No new tests in `tests/BlueCode.Tests/`:** Eval is observational; harness lives in `bench/eval-qwen35-122b.sh` + `bench/eval-humaneval-http.py` + `bench/eval-needle.py`. Test count stays 282/1/0.
- **SSE streaming confirmed working** on mlx_lm.server (probed during 21-01 live run). First chunk combines role+content (NOT separate role-only chunk). awk filter `/"content":/ && !/"content":""/` captures it. curl exits 23 (broken pipe) when awk exits early — suppress with `|| true` in subshell. TTFT median 224 ms (trials 2-10 stable 214-230ms; trial 1 cold at 929ms).
- **Python 3.14 + evalplus compatibility RESOLVED (21-01):** evalplus 0.3.1 pip install succeeded on Python 3.14.3. uv fallback not needed. `bench/.venv-eval/` populated and stable.
- **Cold-start gated behind `--coldstart` flag** — disruptive (kills 122B for ~3min via `launchctl kickstart`). Per scope decision, deferred from default `--full`; reproducibility instructions in eval doc §10.
- **Cloud comparison (Claude/GPT-4) explicit non-goal** — documented in eval doc §6.3 as deliberate boundary.
- **Atomic commits per CLAUDE.md:** 5 task commits + plan-meta + final eval doc commit. Format: `chore(21-XX): {task-name}` for instrumentation; `docs(21-XX): write coding eval verdict doc` for the final doc.
