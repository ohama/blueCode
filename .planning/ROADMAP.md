# Roadmap: blueCode v2.6 GSD self-planning

**Defined:** 2026-05-06
**Phases:** 37 → 41 (5 phases)
**Total requirements:** 22 across 5 categories — 100% mapped (no orphans)
**Depth:** standard (config.json) — 5 phases align with 5 natural delivery boundaries; no compression / no inflation

## Current Milestone: v2.6 GSD self-planning (Robust MVP)

blueCode 자체가 사용자 task 를 1–3 sub-task 로 분해하고 사용자 confirm 후 sequential 실행 + per-task atomic commit. Robust MVP scope = Phase A (planner+executor split) + Phase D (deviation rules + auth gate) + Phase E (atomic commit) from `.planning/docs/gsd/05-ADOPTION-BLUEPRINT.md`. Phase B (goal-backward verifier), Phase C (plan checker LLM), Phase F (wave-parallel) 모두 v2.7+ deferred.

**Architectural shape:** v2.6 가 Core 에 신규 모듈을 추가하는 milestone (v2.0 PlanGate 와 동일 pattern). `BlueCode.Core.Plan` + `BlueCode.Core.PlanOrchestrator` 가 Core 에, `BlueCode.Cli.GitCommit` 가 Cli adapter 로. v2.5 의 "Cli-only invariant" 는 v2.6 에서 의도적으로 풀림 (architectural milestone). 다만 Core 순도 invariant (Serilog/Spectre/Argu/HttpClient/file I/O 금지, `task {}` only) 는 절대 보존.

**Bench gate 7/7 PASS preserved milestone-wide** — v2.6 은 새로운 invocation mode (`--plan2` or rename) 추가; 기존 agent-loop / `--plan` mode 변동 없음. `bench/baseline.json` byte-identical.

### Phase 37: Plan generation foundation

**Goal:** User invocation with the v2.6 self-planning entrypoint produces a structured, schema-validated 1–3 task plan, with malformed planner output rejected via typed error and the user-facing flag/module naming decision recorded in PROJECT.md.

**Depends on:** None (Core foundation; v2.0 `JsonSchema.Net` infrastructure reused)

**Requirements:** PLANGEN-01, PLANGEN-02, PLANGEN-03, PLANGEN-04, UX-01

**Why UX-01 lives here:** The flag/module naming decision (rename `--plan` vs new `--plan2` / `--decompose`) must be settled BEFORE Phase 38 wires `PlanOrchestrator.runPlanMode` and BEFORE Phase 41 wires CLI / REPL surface. Locking the name in Phase 37 prevents downstream rework. Decision lives in PROJECT.md Key Decisions; module naming (`Plan.fs`, `PlanOrchestrator.fs`) and prompt constant naming (`plannerSystemPrompt`) flow from it.

**Success criteria** (what must be TRUE for the user when phase completes):
  1. User can invoke blueCode with the v2.6 self-planning entrypoint (CLI flag whose name is recorded in PROJECT.md Key Decisions per UX-01) and receive a structured `PlanRequest` with 1–3 `PlanTask` entries containing files / action / verify / done — all from a single planner LLM call
  2. When the planner returns malformed JSON, blueCode retries up to 2 times (mirroring v1.0 agent-loop policy); on second failure, user sees a typed `AgentError.PlanInvalid`-derived error rather than a raw exception
  3. When the user task is ambiguous, the planner returns `needs_clarification` (rather than fabricating a plan), and blueCode surfaces the clarification request to the user
  4. PROJECT.md Key Decisions table contains the v2.6 entry recording the flag-naming choice (rename `--plan` vs new flag) with tradeoff rationale, before any Phase 38+ code lands
  5. `src/BlueCode.Core/Plan.fs` exists with `PlanRequest` + `PlanTask` records carrying `JsonPropertyName` attributes; round-trips strict JSON (parse → serialize → parse byte-equal); compiles before any code that depends on it
  6. Bench gate `bash bench/run.sh --gate` 7/7 PASS preserved (Phase 37 adds Core types + planner prompt; bench `--plan` mode untouched)

**Plans:** TBD pending — 2-3 plans expected (Plan types & schema + planner prompt + UX-01 decision artifact; possibly split into pure-types wave + prompt+schema wave)

- [ ] 37-01-TBD: TBD pending /gsd:plan-phase 37
- [ ] 37-02-TBD: TBD pending /gsd:plan-phase 37

### Phase 38: Plan orchestration — sequential executor

**Goal:** A confirmed plan executes sequentially in Core via `PlanOrchestrator.runPlanMode`, with each task running in a fresh executor conversation, plan + summary persisted to `.planning/quick/NNN-<slug>/`, and STATE.md "Quick Tasks Completed" table updated on completion.

**Depends on:** Phase 37 (uses `PlanRequest` + `PlanTask` types from `Plan.fs`; planner prompt invoked from orchestrator's plan-generation step)

**Requirements:** PLANORCH-01, PLANORCH-02, PLANORCH-03, PLANORCH-04, PLANORCH-05

**Architecture note:** `PlanOrchestrator` lives in Core (pure routing logic + DUs over `ILlmClient` + `IToolExecutor` ports — no Serilog / Spectre / file I/O). Session-dir creation (`.planning/quick/NNN-<slug>/`) and PLAN.md / SUMMARY.md / STATE.md writes happen via `IToolExecutor.RunCommand` shell-out OR via a new minimal port if compile-order analysis at plan-phase time finds shell-out insufficient (decision deferred to /gsd:plan-phase 38 research).

**Success criteria** (what must be TRUE for the user when phase completes):
  1. After user confirm (gate UX itself ships in Phase 41 — Phase 38 confirm path may stub via test override), a 1–3 task plan executes sequentially: task 1 finishes → task 2 starts; each task uses a fresh conversation (no priorSteps bleed-through between tasks)
  2. Each invocation creates `.planning/quick/NNN-<slug>/` with monotonic `NNN` (001, 002, ...) and a slug derived from the user task (lowercase, hyphens, ≤40 chars); concurrent invocations do not collide on `NNN`
  3. `NNN-PLAN.md` is written before task 1 starts (so user can inspect mid-execution); `NNN-SUMMARY.md` is written after the final task completes; both files use the GSD frontmatter conventions from `.planning/docs/gsd/04-FILE-PROTOCOL.md`
  4. After all tasks complete, STATE.md gains a row in the "Quick Tasks Completed" table (description, date, final commit hash, session dir relative path); table is created on first row if absent
  5. `BlueCode.Core.PlanOrchestrator` is in Core: `grep -rn "Serilog\|Spectre\|System\.IO\.File\|HttpClient\|PrettyPrompt\|Argu" src/BlueCode.Core/PlanOrchestrator.fs` returns 0 hits; `scripts/check-no-async.sh` passes
  6. Bench gate `bash bench/run.sh --gate` 7/7 PASS preserved

**Plans:** TBD pending — 2-3 plans expected (orchestrator skeleton + session-dir + persistence + STATE.md row; per-task fresh-conversation may need its own plan)

- [ ] 38-01-TBD: TBD pending /gsd:plan-phase 38
- [ ] 38-02-TBD: TBD pending /gsd:plan-phase 38

### Phase 39: Deviation rules + auth gate detection

**Goal:** Per-task executor invocations carry a system prompt that encodes Rules 1–4 (3 auto-fix policies + 1 stop-and-return for architectural changes) and authentication-gate detection; the executor returns a structured `ExecutorOutcome` DU that `PlanOrchestrator` dispatches on.

**Depends on:** Phase 38 (extends per-task prompt assembly in `PlanOrchestrator.runPlanMode`; outcome DU consumed by orchestrator's per-task loop)

**Requirements:** DEV-01, DEV-02, DEV-03, DEV-04

**Note on DU placement:** `ExecutorOutcome = Completed | AuthRequired of cmd | ArchitecturalDecisionNeeded of explanation` lives in Core (likely `Plan.fs` or a new `ExecutorOutcome.fs`). Phase 38's stub return type gets replaced by this DU in Phase 39; orchestrator dispatch arms expand from "complete or fail" to 3-way.

**Success criteria** (what must be TRUE for the user when phase completes):
  1. When the executor finds an unrelated bug while doing its task (Rule 1), it auto-fixes and the deviation appears in `NNN-SUMMARY.md` "Deviations from Plan" section — verifiable by the user reading the summary
  2. When the executor encounters an architectural decision (Rule 4) — e.g., need to introduce a new module / change a public API not in the plan — it stops the current task without partial commit, returns `ArchitecturalDecisionNeeded` with explanation, and `PlanOrchestrator` surfaces this to the user (does NOT auto-proceed; does NOT commit)
  3. When a task hits an authentication gate (e.g., shell command returns 401, "Not authenticated", "Please run gh auth login"), executor returns `AuthRequired of cmd` (NOT failure); orchestrator surfaces "please run `<cmd>` and re-invoke" to the user — verifiable by manually breaking auth and observing the message
  4. The executor system prompt verbatim contains the Rule 1–3 + Rule 4 policy text from `.planning/docs/gsd/02-EXECUTION-PIPELINE.md`; trace mode (`--trace`) shows the constructed prompt
  5. `ExecutorOutcome` DU is exhaustively dispatched in `PlanOrchestrator` (compile-time exhaustiveness check via `match`)
  6. Bench gate `bash bench/run.sh --gate` 7/7 PASS preserved (deviation policy is prompt-only; existing `--plan` and agent-loop bench fixtures untouched)

**Plans:** TBD pending — 2 plans expected (DU + dispatch + prompt construction in one plan; auth-gate pattern detection in second)

- [ ] 39-01-TBD: TBD pending /gsd:plan-phase 39
- [ ] 39-02-TBD: TBD pending /gsd:plan-phase 39

### Phase 40: Atomic commit per task

**Goal:** Each successfully completed task in a v2.6 self-plan produces its own git commit via `BlueCode.Cli.GitCommit`, with type inferred from task name + file extensions, message format `{type}(quick-{NNN}): {task name}`, commit hash captured for SUMMARY.md, and `git add .` / `git add -A` rejected at the API level. A separate plan-metadata commit closes the plan with PLAN.md + SUMMARY.md + STATE.md staged.

**Depends on:** Phase 38 (uses session id `NNN`, modifiedFiles list per task), Phase 39 (only commits when `ExecutorOutcome = Completed`; AuthRequired and ArchitecturalDecisionNeeded do NOT trigger commit)

**Requirements:** COMMIT-01, COMMIT-02, COMMIT-03, COMMIT-04

**Layer note:** `GitCommit` is a Cli adapter (uses `IToolExecutor.RunCommand` for `git` shell-out; not Core). Forbidden-flag rejection is compile-time (F# list literal `["."; "-A"; "--all"]` checked at API boundary), not runtime regex.

**Success criteria** (what must be TRUE for the user when phase completes):
  1. After a successful 3-task plan, `git log` shows exactly 4 new commits: 3 task commits with format `{type}(quick-NNN): {task name}` (where `{type}` is one of feat/fix/test/refactor/perf/docs/style/chore inferred from task name + file extensions) followed by 1 plan-metadata commit `docs(quick-NNN): {plan description}` staging only PLAN.md + SUMMARY.md + STATE.md
  2. Each per-task commit's hash is captured in `NNN-SUMMARY.md` "Tasks Completed" section — verifiable by `grep` of the hash from `git log` against the SUMMARY contents
  3. If the plan task carries an explicit `commit_type` field in the JSON, that value is used (overrides heuristic); if absent, the heuristic table from CLAUDE.md commit protocol is consulted
  4. Calling `commitTask` with files containing `"."` or `"-A"` or `"--all"` raises a typed error at the API boundary BEFORE any `git` invocation — verifiable by unit test that asserts no `git` shell-out occurred
  5. Per-task commit stages ONLY the files modified by that task (NOT plan/summary metadata); the plan-metadata commit stages ONLY PLAN.md + SUMMARY.md + STATE.md (NOT source files) — verifiable by `git show --stat <hash>` matching the task's modifiedFiles list
  6. Bench gate `bash bench/run.sh --gate` 7/7 PASS preserved (commit work is per-invocation; bench is non-interactive smoke that does not exercise self-planning path)

**Plans:** TBD pending — 2 plans expected (GitCommit module + heuristic + forbidden-flag guard in plan 1; orchestrator wiring + plan-metadata commit + SUMMARY hash capture in plan 2)

- [ ] 40-01-TBD: TBD pending /gsd:plan-phase 40
- [ ] 40-02-TBD: TBD pending /gsd:plan-phase 40

### Phase 41: CLI / REPL surface + UX polish

**Goal:** The user-facing entry points are wired end-to-end: CLI flag (chosen per UX-01) and matching REPL slash command both invoke `PlanOrchestrator.runPlanMode`; user sees a Spectre.Console plan table BEFORE confirm, presses y/n via `IKeyReader`, and gets per-task progress indicators on stdout during execution.

**Depends on:** Phase 37 (UX-01 flag-name decision committed), Phase 38 (orchestrator entry point exists), Phase 39 (executor outcome dispatch — progress indicators differ for AuthRequired vs ArchitecturalDecisionNeeded vs success), Phase 40 (`✓ committed {hash}` indicator references commit hash from Phase 40)

**Requirements:** UX-02, UX-03, UX-04, UX-05

**Note on test surface:** Phase 41 retains the v2.5 stream-separation invariant (Serilog → stderr; printfn / Spectre.Console → stdout). Per-task progress (`▶ Task N/M: {name}`, `✓ committed {hash}`, `✗ {reason}`) goes to stdout via `printfn` so existing `Console.SetOut`-based test infrastructure works without `AnsiConsole.Console` singleton-reset gymnastics. Spectre tables (UX-03 plan display) are TTY-only output — gated by an `AnsiConsole.Console` injection seam pattern matching v2.5 Phase 33-02 if needed.

**Success criteria** (what must be TRUE for the user when phase completes):
  1. User runs `blueCode <flag-from-UX-01> "<task description>"` from the shell and sees: (a) "Generating plan..." indicator, (b) Spectre table with columns Task # / Name / Files, (c) confirm prompt `Execute this plan? [y/n]`, (d) on `y`: per-task progress lines, (e) on `n`: graceful abort with no commits, no session dir created
  2. From inside the REPL, user types `<slash-from-UX-01>` (e.g., `/plan2`) followed by a task on the next line and gets the SAME flow as the CLI path (table + confirm + sequential exec); slash dispatcher contains NO LLM calls (planner LLM call is invoked by `PlanOrchestrator`, dispatched into from the slash arm)
  3. During execution, stdout shows `▶ Task N/M: {name}` on each task start and `✓ committed {hash}` on each task success (or `✗ {reason}` on AuthRequired / ArchitecturalDecisionNeeded — where `{reason}` is human-readable per Phase 39 outcome variant)
  4. Aborting via `n` at the confirm gate produces no `.planning/quick/NNN-<slug>/` directory and no commits — verifiable by `git status` clean and `ls .planning/quick/` unchanged
  5. ReplTests for the new slash command and CLI integration use `Console.SetOut` redirection compatible with `documentation/howto/handle-expecto-console-redirection.md` patterns; new test modules registered in BOTH `BlueCode.Tests.fsproj` `<Compile Include>` order AND `rootTests` list in `RouterTests.fs`
  6. Bench gate `bash bench/run.sh --gate` 7/7 PASS preserved milestone-wide (final phase verification)

**Plans:** TBD pending — 2-3 plans expected (CLI flag wiring + Program.fs in plan 1; REPL slash command + dispatcher arm + plan table render in plan 2; integration tests + bench gate in plan 3)

- [ ] 41-01-TBD: TBD pending /gsd:plan-phase 41
- [ ] 41-02-TBD: TBD pending /gsd:plan-phase 41

## Phase Dependencies

```
Phase 37 (Plan generation foundation + UX-01 decision)
  └─→ Phase 38 (PlanOrchestrator + sequential exec)
        └─→ Phase 39 (Deviation rules + auth gate)
              └─→ Phase 40 (Atomic commit per task)
                    └─→ Phase 41 (CLI / REPL surface)

Phase 37 = root (Plan types + planner prompt + flag-naming decision).
Phase 38-41 chain linearly — each consumes prior phase's contract.
No interleavable phases this milestone (each requires the prior's exports).
```

**Note on linearity:** Unlike v2.5 (Phases 32-35 all stacked independently on Phase 31), v2.6 phases form a strict chain. Phase 38 imports `PlanRequest` from Phase 37; Phase 39 expands the `ExecutorOutcome` DU consumed by Phase 38's orchestrator dispatch; Phase 40 commits only when Phase 39's `Completed` variant returns; Phase 41 wires user-facing flow through Phases 38-40 outputs. `/gsd:execute-phase` waves should still parallelize within each phase, but phases themselves run sequentially.

## Coverage Validation

| Category | Count | Phase Mapping |
|----------|-------|---------------|
| PLANGEN-* | 4 | Phase 37 (PLANGEN-01..04) |
| PLANORCH-* | 5 | Phase 38 (PLANORCH-01..05) |
| DEV-* | 4 | Phase 39 (DEV-01..04) |
| COMMIT-* | 4 | Phase 40 (COMMIT-01..04) |
| UX-* | 5 | Phase 37 (UX-01) + Phase 41 (UX-02..05) |
| **Total** | **22** | **22 mapped — 0 orphans** ✓ |

UX-01 placement rationale: flag-naming decision must precede module/prompt naming (Phase 37) and CLI/REPL wiring (Phase 41). Splitting UX-* across two phases is correct — UX-01 is a decision artifact (PROJECT.md Key Decision); UX-02..05 are user-facing surface code.

## Out-of-scope (preserved from REQUIREMENTS.md "v2.7+")

Explicitly deferred — re-list here for roadmap-level visibility:

- **VERIFY-01..04** (Goal-backward verifier — Phase B) → v2.7
- **CHECK-01..03** (Plan checker LLM — Phase C) → v2.7
- **WAVE-01** (Wave-based parallel — Phase F) → indefinite (single-LLM-server ROI)
- **Multi-plan / multi-phase orchestration** — v2.6 = single-plan-per-invocation
- **Auto-classification "needs plan?"** — v2.6 = explicit opt-in via flag/slash
- **Replacing v2.0 `--plan`** — UX-01 architecture choice; either-way v2.0 mode keeps working through milestone close
- **Cross-platform commits** — Mac-only invariant from v1.0 holds
- **`.planning/quick/` git inclusion policy** — out-of-band; respects user `.gitignore`

## Phase Numbering

Continues at 37. Project phase history:
- v1.0: 1-5
- v1.1: 6-7
- v1.2: 8, 9, 9.1
- v1.3: 10-11
- v1.4: 12-13
- v2.0: 14-20 (Phase 16 replan; 17-20 added mid-milestone)
- v2.1: 21
- v2.2: 22-23
- v2.3: 24-27 (26 BLOCKED; 27 added mid-milestone)
- v2.4: 28, 30 (29 SKIPPED-by-design)
- v2.5: 31-36 (36 added mid-milestone)
- **v2.6: 37-41** (5 phases planned; mid-milestone insertion possible per v2.0/v2.3/v2.5 precedent)

## Stats Target

- 5 phases, ~10-13 plans (2-3 per phase), ~30-50 tests added (Plan parser/schema + orchestrator unit + deviation prompt + GitCommit boundary + REPL integration)
- LOC estimate: ~600-900 (Core: Plan.fs + PlanOrchestrator.fs + ExecutorOutcome ≈ 250-350; Cli: GitCommit.fs + Program.fs / SlashCommand.fs deltas + Spectre table render ≈ 250-400; tests ≈ 200-300)
- Bench gate 7/7 PASS preserved milestone-wide (`bench/baseline.json` byte-identical)
- Core purity preserved: new `Plan.fs` + `PlanOrchestrator.fs` use only `task {}`, F# DUs, `ILlmClient` + `IToolExecutor` ports — zero Serilog/Spectre/Argu/HttpClient/file I/O imports
- New NuGets: 0 expected (reuses `JsonSchema.Net` + `FSharp.SystemTextJson` from v1.0+v2.0)

## Progress

**Execution Order:** 37 → 38 → 39 → 40 → 41 (strict linear chain)

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 37. Plan generation foundation | 0/TBD | Not started | - |
| 38. Plan orchestration | 0/TBD | Not started | - |
| 39. Deviation rules + auth gate | 0/TBD | Not started | - |
| 40. Atomic commit per task | 0/TBD | Not started | - |
| 41. CLI / REPL surface + UX polish | 0/TBD | Not started | - |

---
*Roadmap created: 2026-05-06 by gsd-roadmapper agent*
*Source: REQUIREMENTS.md (22 reqs) + `.planning/docs/gsd/05-ADOPTION-BLUEPRINT.md` Phase A+D+E scope*
*Next: `/gsd:plan-phase 37` after milestone-init commit + `/clear`*
