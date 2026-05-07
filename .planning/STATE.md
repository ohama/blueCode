# Project State

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-05-06 starting v2.6 GSD self-planning milestone)

**Core value:** Mac 로컬 Qwen 3.5 122B를 strong-typed F# agent loop로 **empirically** 안정적으로 돌린다 (post-v2.4 verdict 96/100 KEEP). v2.5 shipped REPL ergonomics. **v2.6 introduces self-planning**: blueCode 자체가 사용자 task 를 분해하고 실행한다 — Plan generation + PlanOrchestrator + Deviation Rules + Atomic commit per task.
**Current focus:** **v2.6 in flight (roadmap drafted).** GSD-style self-planning Robust MVP (A + D + E from `.planning/docs/gsd/05-ADOPTION-BLUEPRINT.md`). Phase numbers 37–41 (5 phases, strict linear chain).

## Current Position

Phase: 42 of 42 (Qwen 122B OpenAI compatibility verification) — **✓ COMPLETE + VERIFIED**
Plan: 03 of 03 — COMPLETE (rendering + report + CLAUDE.md pointer + final post-flight bench gate)
Status: **Phase 42 ✓ COMPLETE + VERIFIED.** All 3 plans done; gsd-verifier passed 14/14 must-haves at 2026-05-07; 42-VERIFICATION.md committed in phase-completion bundle. Empirical conformance report at `documentation/qwen35-122b-openai-compat.md` (252 lines, HIGH=3/MED=2/LOW=5/PASS=15). FINAL post-flight `bash bench/run.sh --gate` = 7/7 PASS at 2026-05-07T05:07:41Z. Zero src/ diff preserved milestone-wide. Optional `/gsd:verify-work 42` for conversational UAT round; or pivot to `/gsd:plan-phase 37` for v2.6 implementation start.
Last activity: 2026-05-07 — Plan 42-03 complete. Renderer added to `bench/eval-openai-compat.py` (869 → 1450 LOC); argparse refactored to mutex `--output-dir XOR --render`. Report: HIGH=3 (response_format probes 06/07/08) / MEDIUM=2 / LOW=5 / PASS=15. CLAUDE.md Bench section gained one bullet pointing to the report. Three v2.7+ candidates surfaced for `/gsd:add-todo`: tools/tool_choice native (probe 15 PASS), wave-parallel exec (probe 22 N=2 PASS), capture_full_content flag (Plan 42-04+ infra).

**v2.6 progression:** v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 ✓ → v1.4 ✓ → v2.0 ✓ → v2.1 ✓ → v2.2 ✓ → v2.3 ✓ → v2.4 ✓ → v2.5 ✓ → **v2.6 (Phase 42 of 42 complete; Phases 37-41 implementation pending)**

Progress: ███░░░░░░░ 3/? plans (Phase 42: 3/3 plans done — DONE; Phases 37-41 plan counts still TBD)

Next: `/gsd:verify-work 42` for UAT, then optionally `/gsd:complete-phase 42` to archive Phase 42 artifacts. After that, `/gsd:plan-phase 37` to begin v2.6 implementation work.

**Next Phase:** Phase 42 done. Next user action = `/gsd:verify-work 42` then pivot to Phase 37 implementation.

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
**v2.5:** 6 phases (31-36; Phase 36 inserted mid-milestone for manual-test-round-1 fixes), 13 plans, 12 requirements (all 12 satisfied), 365 tests (+83 net delta), 71 files +19,833/-201 LOC, 68 commits, ~7 days wall-clock (2026-04-29 → 2026-05-05); 1 new NuGet (PrettyPrompt 4.1.1, MPL-2.0); bench gate 7/7 PASS preserved milestone-wide; **zero `src/BlueCode.Core/` diff** (Cli-only invariant held); port-template (IKeyReader v2.0 → IEditorLauncher Phase 34 → IPromptReader Phase 35) crystallized as load-bearing

Detailed per-plan history archived in `.planning/milestones/v{1.0,1.1,1.2,1.3,1.4,2.0,2.1,2.2,2.3,2.4,2.5}-phases/`.

## Accumulated Context

### Decisions

Cumulative log in `.planning/PROJECT.md` Key Decisions table. See PROJECT.md for outcomes through v2.5.

Stable patterns established across milestones (load-bearing for next session):

- **Core purity** — `src/BlueCode.Core/**` no Serilog/Spectre/Argu/HttpClient/file I/O/PrettyPrompt; `task {}` only (CI-enforced via `scripts/check-no-async.sh`).
- **Test discovery** — explicit `rootTests` list in `RouterTests.fs` + ordered `<Compile Include>` in `BlueCode.Tests.fsproj`. New test modules need BOTH places (4+ prior plans hit this pitfall — silent test skip).
- **Canonical test runner** — `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` (NOT `dotnet test`).
- **Bench gate** — `bash bench/run.sh --gate` is the structural authority. 7/7 PASS post-v2.5 with single-model 122B (T6/T5/B2/T1/W1/W2/MT). `bench/baseline.json` byte-identical milestone-wide.
- **Atomic commits** — `{type}({phase}-{plan}): {name}` per task; plan-meta separate; phase-complete bundles. NEVER `git add -A` or `git add .`.
- **Role = User invariant** (Phase 17-02 + Phase 20-03) — Qwen 3.5 122B REJECTS mid-conversation `Role = System` with HTTP 404. ALL mid-conversation injections are `Role = User`.
- **Single-model 122B canonical** (Phase 19) — `blueCode "..."` defaults to 122B; 35B requires `--with-35b` opt-in flag AND service loaded.
- **10-step PLAN-04 ceiling** (Phase 22-01) — `PlanValidator.MaxPlanSteps = 10` + `AgentConfig.MaxLoops = 10` (independent constants).
- **P1 enumeration directive in `defaultSystemPrompt`** (Phase 27-01) — Conditional clause "When the task requires renaming or restructuring multiple symbols..." applies to BOTH agent-loop and plan-mode invocations. Char count: 967.
- **P2 few-shot example in `planSystemPromptSuffix`** (Phase 24-02; plan-mode-only) — 3-line Example/Targets/Steps block. Suffix char count: 1577 (was 999 pre-Phase 36-03).
- **Combined plan-mode prompt char count INVARIANT post-Phase 36-03** — defaultSystemPrompt(967) + "\n\n" + planSystemPromptSuffix(1577) = 2546 chars (was 1968 pre-Phase 36-03; Phase 36-03 added MAXIMUM 10 HARD LIMIT + Path rules clauses).
- **PlanValidator pre-flight `checkRenameTargetsEnumerated`** (Phase 25-01) — Interpretation B detail-string encoding; PlanValidator.fs:108; bound at validatePlan:143; called from AgentLoop.fs:484. Plan-mode only.
- **Plan-mode vs agent-loop distinction is load-bearing** (v2.3 lesson; preserved through v2.4+v2.5) — `defaultSystemPrompt` for ALL invocations; `planSystemPromptSuffix` only when `--plan` (or REPL `/plan` toggle); `PlanValidator.checkRenameTargetsEnumerated` only in `runPlanTurn`.
- **Mandatory `launchctl kickstart` pre-flight for evaluations** (v2.3 Phase 26 Diagnostic D; reused in v2.4 Phase 28-04) — `launchctl kickstart -k gui/501/com.ohama.qwen122b` clears KV cache contamination.
- **Port-template pattern** (v2.0 IKeyReader + v2.5 Phase 34 IEditorLauncher + v2.5 Phase 35 IPromptReader) — Single-method interface in `Cli/X.fs`; `makeRealX` production impl; `makeTestX` mock for unit tests; `xOverride: IX option` mutable seam at Repl boundary for integration tests. Three-application precedent makes this load-bearing for any future TTY-bound Cli adapter.
- **REPL dispatcher extension pattern** (Phase 32-02 + 33-01 + 34-01) — Add new arms ABOVE Prompt arm; slim future-stub by removing handled commands; use `printfn` not `AnsiConsole.MarkupLine`; use `let!` inside `task {}` CE for async calls.
- **handlePromptTurn helper extraction** (Phase 34-01) — `Repl.runMultiTurnWithSession` factored out `handlePromptTurn: prompt -> Task<unit>` containing `if planModeActive then runPlanTurn-inline else runSingleTurn` branching. Both `Some (Prompt _)` arm AND `Some (Slash Edit)` Some-content branch call `do! handlePromptTurn` (exactly 2 sites). /edit content auto-inherits plan-mode.
- **One-shot plan-mode semantics** (Phase 33-01) — `planModeActive` auto-disables after Accept+Execute, Quit, AND rejectCount exhaustion. User must re-type `/plan` for next plan-gated turn. Plan-gate Quit sets `planModeActive <- false` + `turnDone <- true` only — NEVER `running <- false` (would exit REPL).
- **renderStatus 4-arg signature** (Phase 33-01) — `(Session, Model option, int, bool)` where the bool is `planModeActive`. When true, appends `\nplan-mode: on (next prompt uses plan-gate)` to output.
- **Spectre.Console singleton reset for plan-gate REPL tests** (Phase 33-02 auto-fix) — `AnsiConsole.Console <- AnsiConsole.Create(AnsiConsoleSettings())` after `Console.SetOut` redirect; restore in `finally`. Required only for tests that route through PlanGate.render.
- **listRecent NOT on ISessionStore** (Phase 32-01) — `listRecent : int -> SessionMeta list` is a Cli-layer module-level function in `FileSessionStore.fs`, NOT an ISessionStore member. ISessionStore stays frozen at Save + Load (Core purity invariant).
- **No Save on /clear or /resume** (Phase 31-02 + Phase 32-02) — `FileSessionStore.Save` creates `.jsonl` lazily on first write; empty new session has nothing to persist; loaded session is already on disk.
- **Tmpfile lifecycle in EditCommand.openEditorAsync** (Phase 34-01) — `Path.GetTempFileName()` → `File.Move` to `.md` (atomic; original `.tmp` disappears) → editor → read → `try/finally` delete; module-level `AppDomain.CurrentDomain.ProcessExit` handler for atexit (4 cleanup paths: read success, cancel, normal exit, atexit).
- **PrettyPrompt 4.1.1 — first new NuGet in v2.5** (Phase 35-01) — MPL-2.0; .NET 8 forward-compat to .NET 10; single transitive dep TextCopy. All 4 HIST reqs satisfied by `new Prompt(persistentHistoryFilepath = path)` constructor parameter alone (built-in HistoryLog: base64-per-line, 500-cap hardcoded, async append, dedup, Ctrl+R reverse-search). PromptConfiguration.prompt is `Nullable<FormattedString>` from `PrettyPrompt.Highlighting` namespace (NOT plain string from Configuration).
- **Hybrid Console.SetIn + promptReaderOverride for plan-gate tests** (Phase 35-02) — 4 ReplTests for plan-gate Accept/Quit retain `Console.SetIn` for `a`/`q` keypresses to `PlanGate.realKeyReader` while ALSO using `promptReaderOverride` for REPL prompt lines. Authorized hybrid; documented as expected non-zero `grep -c "Console.SetIn"` count post-v2.5.
- **PrettyPrompt history file 500-entry cap** (Phase 35-01 deviation-from-spec) — ROADMAP.md SC-5 specified "N=1000 default"; PrettyPrompt's HistoryLog cap is hard-coded at 500. Adopted 500 as deviation per planner judgment; bumping requires upstream PR (tracked as PRETTYPROMPT-HIST-1000 v2.6+ candidate).
- **macOS bash-strict-mode patterns documented** (5 patterns across v2.1-v2.4): set-e/dotnet-exit (v2.1 21-03); grep-c/pipefail double-output (v2.1 21-04); mkdir-before-tee (v2.2 23-01); orphan-grep-zero-match-exit-1 (v2.3 27-02); grep-oE-pipeline-zero-match (v2.4 28-01). Common rule: under `set -euo pipefail`, `grep -c`/`grep -cE`/`grep -oE` exits 1 on zero-match; guard with `|| true`. Full reference: `documentation/howto/macos-bash-strict-mode-patterns.md`.
- **F# fixture infrastructure** (v2.4 Phase 28) — `bench/fixtures/fs_idiomatic/` with 3 fixture pairs; `--fs-idiomatic` mode in `bench/eval-qwen35-122b.sh` lines 328-391. Reusable for future F# coding-quality work. Research Q4 verbatim binary 5-criterion rubric (C1-C5) + sum-then-scale band table aggregate.
- **FSI fixture skeleton pattern** (v2.4 Phase 28-02) — `.fs` skeletons use direct top-level `try...with` + printfn (NO `[<EntryPoint>]`). FSI executes module-level code directly. `dotnet fsi <file>.fs </dev/null` always exits 1 (interactive EOF); compile verification uses absence of `error FS` in output, not exit code.
- **Sum-then-scale band table aggregate (v2.4 Phase 28-04)** — For multi-fixture rubric scoring, band table beats round(average) for outlier robustness.
- **Conditional SKIP-by-design pattern (v2.4 Phase 29)** — When a conditional phase doesn't trigger, never create the phase directory (cleaner than BLOCKED-with-record). Use BLOCKED preservation only when partial-attempt produces diagnostic value (v2.3 Phase 26).
- **Zero-source-diff milestone pattern (v2.1 + v2.4)** — Eval/measurement work can ship meaningful capability without `src/` changes. **Cli-only milestone variant (v2.5)** — meaningful Cli expansion ships with `git diff milestone-v<prev> HEAD -- src/BlueCode.Core/` empty assertion as established invariant.
- **Measurement-first scoping (v2.4)** — Inverted v2.2/v2.3's intervention-first pattern. *"Is the score real, or is the measurement instrument wrong?"*

### Roadmap Evolution

Mid-milestone phase additions/changes for v2.6:

- **2026-05-06: Phase 42 added** — "Qwen 122B OpenAI compatibility verification" via `/gsd:add-phase`. Investigation/validation work to empirically verify `mlx_lm.server` @ 8001's `/v1/chat/completions` OpenAI-compat surface (endpoint coverage, `response_format` field, role handling, streaming, schema enforcement, multi-call coherence, error surface, concurrency). Default `depends_on: Phase 41`; reassessable at `/gsd:plan-phase 42` time. No requirements mapped yet (Phase 42 may surface new v2.6 requirements via `/gsd:add-todo` if HIGH-severity findings emerge). Pre-existing knowledge: Role=System mid-conversation REJECTED (Phase 17-02 + 20-03), thinking-mode `<think>` token mitigation, schema rate 0/50 perfect under current setup (v2.1).
- **2026-05-07: Plan 42-01 complete** — Probe harness scaffolding shipped. `bench/eval-openai-compat.py` (318 LOC, dict-list PROBES + probe()/probe_get() helpers + per-record fp.flush()), `bench/eval-qwen35-122b.sh --openai-compat` mode wired (intentionally NOT in `--full`), `jsonschema>=4.21` added to venv. 10 probes (Surface 1: 5 endpoint + Surface 2: 3 response_format + Surface 3: 2 role) ran end-to-end; all HTTP codes match RESEARCH preliminaries 1-7+11 verbatim. Bench-gate sandwich 7/7 PASS pre (02:36:44Z) + 7/7 PASS post (02:43:26Z). Zero `src/` diff. LOG_DIR: `bench/runs/qwen35-eval-20260507-114034/probes.jsonl`. Decisions: dict-list shape (vs RESEARCH skeleton's tuples) for forward compat; PROBES populated in Task 1 not Task 2 (Task 1 verify required ≥10 records); `--full` intentionally NOT calling `run_openai_compat` per RESEARCH anti-pattern (parallel probes contaminate KV).
- **2026-05-07: Plan 42-02 complete** — Probe surface extended from 10 → 25 records covering all 8 RESEARCH surfaces + tools bonus. `bench/eval-openai-compat.py` extended 318 → 870 LOC with three new helpers: `probe_stream` (SSE state-machine via `requests.iter_lines()` classifying keepalive / data:[DONE] / data:{json}); `probe_concurrent_pair` (N=2 `ThreadPoolExecutor`, captures wall_clock vs sum vs max); `_dispatch_stat_n` (full-response N-repeat aggregator bypassing the 300-char excerpt cap). `probe()` extended with three orthogonal optional flags (raw_body / omit_model_field / body_model_override) for the error-surface probes. **Empirical findings:** probe 22 wall_clock=0.39s ≈ max(0.38,0.38) sum=0.76s → BatchGenerator parallel decode confirmed at N=2 (matches RESEARCH preliminary 10); probes 20+21 5/5 valid_json + 5/5 prose_wrap with IDENTICAL "Alice/28" output at temp=0.0 → **response_format has zero effect at temp=0.0** (answers RESEARCH Open Question 2); probe 15 tool_choice=auto returns full OpenAI envelope with finish_reason=tool_calls (CONFORMANT, v2.7+ candidate per RESEARCH preliminary 8); probe 18 (omit model field) elapsed 83.4s — server falls back to default_model via HF reload but probe 19 immediately after returns 0.18s with qwen122b path → **non-contaminating**; mid-flight bench gate 7/7 PASS confirms invariant. **Deviation from RESEARCH preliminary 9:** SSE `[DONE]` sentinel emitted even WITHOUT `stream_options.include_usage` on current build (`system_fingerprint=0.31.3-0.31.2-...`) — RESEARCH Pitfall 5 mitigation needs updating in Plan 42-03 docs. Bench-gate sandwich 7/7 PASS pre + mid + post. Zero `src/` diff. LOG_DIR: `bench/runs/qwen35-eval-20260507-131320/probes.jsonl`. **Auto-fixes:** Rule 1 STAT_N excerpt-truncation regression caught Task 2 mid-verify (probe()'s 300-char cap consumed entire response envelope before content; refactored STAT_N to fetch full r.json() directly; same commit `a02dfd3`). Decisions: STAT_N stores `content_excerpts` (not `excerpts`) — JSONL contract baseline for Plan 42-03 renderer.
- **2026-05-07: Plan 42-03 complete (Phase 42 closed pending UAT)** — Renderer shipped + empirical report rendered + CLAUDE.md pointer + FINAL post-flight bench gate 7/7 PASS. `bench/eval-openai-compat.py` extended 869 → 1450 LOC with `classify_verdict()`, `render_report(jsonl_path)`, `_coherence_aggregate()`, `_server_fingerprint()`. Argparse refactored to `add_mutually_exclusive_group(required=True)` for `--output-dir XOR --render`. Report at `documentation/qwen35-122b-openai-compat.md` (252 lines): HIGH=3 (probes 06/07/08 response_format silent-ignore) / MEDIUM=2 / LOW=5 / PASS=15. Sections: Date+source attribution / How to read this report / Verdict Summary / Findings by Severity / 8 Per-Surface tables / Empirical highlights (response_format silent-ignore + N=2 parallel + error-surface timing) / Multi-call coherence detail (with 300-char-excerpt truncation Limitation note) / Out of scope / Implications for blueCode (v2.6 mitigations CONFIRMED + v2.7+ candidates) / Sources. **Reproducibility verified:** Date field uses JSONL mtime; re-running --render twice produces byte-identical output. CLAUDE.md gained one bullet under Bench section (Case C+D fused per plan decision matrix; no Key Seams structural change because the response_format MUST-NOT rule is documentation-only — blueCode already follows prompt-instructed schema in QwenHttpClient.fs). **Auto-fixes (3 Rule-1 spec-tension/cosmetic):** probe 15 PASS rule relaxed (excerpt cap obscures `get_weather` arg; finish_reason=tool_calls alone is OpenAI marker); coherence verdict envelope-only (semantic correctness deferred — excerpt cap; transparently documented in report Limitation block); double-slash path display normalized. **v2.7+ candidates surfaced** (file via `/gsd:add-todo`): (1) replace custom JSON-schema action DU with native OpenAI tool calls (probe 15); (2) wave-parallel exec Phase F (probe 22 N=2 confirmed); (3) extend probe() with capture_full_content flag (Plan 42-04+ infra). FINAL post-flight bench gate 7/7 PASS at 2026-05-07T05:07:41Z. `git diff master -- src/` = 0 lines + `git diff master -- bench/baseline.json` = 0 lines (Phase 42 zero-src-diff + byte-identical-baseline invariants preserved milestone-wide).

### Pending Todos

None pending.

v2.6 scoping is observation-driven (start daily-driver use of new REPL ergonomics; capture pain via `/gsd:add-todo`).

### Blockers/Concerns

None active.

Documentation drift items flagged in v2.0 audit (still non-blocking; carried-forward to v2.6 cleanup):
- Phase 20 missing formal `20-VERIFICATION.md` (per-plan SUMMARYs substitute)
- CLAUDE.md Key Seams has 1 stale "72B" sentence (historical)
- `documentation/` retains legacy 32B/72B install docs (kept as historical reference)

Plus v2.5 tech debt aggregated in `.planning/milestones/v2.5-MILESTONE-AUDIT.md` (6 items, all deferred-by-design or external).

## Session Continuity

Last session: 2026-05-07 (Plan 42-03 executed by gsd-executor agent; FINAL post-flight bench-gate 7/7 PASS at 05:07:41Z confirming Phase 42 close-out invariant)
Stopped at: **Plan 42-03 complete — Phase 42 ALL plans done (3/3); awaiting `/gsd:verify-work 42` UAT**. Empirical conformance report shipped at `documentation/qwen35-122b-openai-compat.md` (252 lines, HIGH=3 / MEDIUM=2 / LOW=5 / PASS=15). 3-plan bench-gate sandwich 7/7 PASS preserved milestone-wide. Zero src/ diff. NEXT: `/gsd:verify-work 42` for UAT; then `/gsd:complete-phase 42` to archive; then pivot to `/gsd:plan-phase 37` for v2.6 implementation start.
Resume file: None
Next workflow trigger: `/gsd:verify-work 42` to gate Phase 42 UAT, OR pivot directly to `/gsd:plan-phase 37` for v2.6 implementation (Phase 42 closed; pending only UAT verify).

**v2.6 inputs (load-bearing for downstream phases):**
- `.planning/docs/gsd/00-OVERVIEW.md` — 5 invariants (Plans Are Prompts, Quality Curve, Goal-Backward, File-as-State, Atomic+Wave)
- `.planning/docs/gsd/01-PLANNING-PIPELINE.md` — research → plan → check → revise loop algorithm
- `.planning/docs/gsd/02-EXECUTION-PIPELINE.md` — wave-parallel + deviation rules + atomic commit + verifier (Rules 1–4 verbatim text source for DEV-01/DEV-02 prompts)
- `.planning/docs/gsd/03-AGENT-CONTRACTS.md` — 5 agent input/output contracts
- `.planning/docs/gsd/04-FILE-PROTOCOL.md` — file system as state machine (PLAN.md / SUMMARY.md frontmatter conventions for PLANORCH-03)
- `.planning/docs/gsd/05-ADOPTION-BLUEPRINT.md` — 6 phase adoption plan with F# code sketches; v2.6 = Phase A + D + E (load-bearing for phase planning)

## Empirical Baselines (post-v2.5)

Load-bearing measurements for v2.6+ scoping:

- **HumanEval+ chat pass@1 = 0.939 / pass@1+ = 0.902** (v2.1) — re-evaluation trigger if mlx_lm.server major version, Qwen 3.5 model card update / YaRN config change, or sampling defaults change
- **Throughput median 34.6 tok/s; TTFT median 222 ms warm** (v2.1) — interactive UX baseline
- **Schema rate 0/50 InvalidJsonOutput** (v2.1) — perfect compliance
- **Multi-turn coherence stable through N=7** (v2.1)
- **Needle 4/4 at max_model_len=32768** (v2.1)
- **Cold-start 37s warm-OS-cache** (v2.2 Phase 23)
- **10-step PLAN-04 ceiling** (Phase 22-01)
- **CORR-EVAL-02 PASS** (Phase 27-02; 2026-04-29) — orphan_count=0 with all 3 v2.3 prongs in production
- **Idiomatic F# 5/5** (v2.4 Phase 28-04; UPDATED from 1/5 in v2.1)
- **Coding-quality 10/10** (v2.4 Phase 28-05; UPDATED from 6/10 in v2.1/v2.3)
- **Aggregate verdict 96/100 KEEP** (post-v2.4) — Correctness 36/40, Performance 25/25, Reliability 25/25, Coding-quality 10/10. **All 4 dimensions ≥80%.** Widest KEEP margin in project history.
- **REPL test count 365/365 GREEN** (post-v2.5) — +83 net delta from v2.4 close (282); bench 7/7 PASS preserved milestone-wide
- **PrettyPrompt 4.1.1 NuGet** (post-v2.5) — first new dep in v2.5; MPL-2.0; .NET 8 forward-compat to .NET 10; transitive TextCopy
- **9-command REPL slash surface** (post-v2.5) — /help /status /clear /exit /quit /sessions /resume /plan /edit; all in-process (zero LLM calls); printfn-only output (testable via Console.SetOut)

For full per-section results, see `documentation/qwen35-122b-coding-eval.md`. Per-plan execution history archived in `.planning/milestones/v{1.0..2.5}-phases/`.
