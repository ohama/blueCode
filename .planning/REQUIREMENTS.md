# Requirements: blueCode v2.6 GSD self-planning

**Defined:** 2026-05-06
**Core Value:** Mac 로컬 Qwen 3.5 122B를 strong-typed F# agent loop로 안정적으로 돌린다 — v2.6 layers self-planning capability on top of v2.5 daily-driver REPL surface.
**Source materials:** `.planning/docs/gsd/05-ADOPTION-BLUEPRINT.md` (Phase A + D + E scope)
**Phase numbering:** Continues from v2.5 final (Phase 36) → v2.6 starts at Phase 37.

## v2.6 Requirements (Robust MVP)

### Plan generation (PLANGEN-*)

Decompose user task into 1–3 sub-tasks with structured output.

- [ ] **PLANGEN-01**: New module `BlueCode.Core.Plan` defines `PlanRequest` and `PlanTask` F# records with `JsonPropertyName` attributes; serializable to/from strict JSON
- [ ] **PLANGEN-02**: New `plannerSystemPrompt` constant in `CompositionRoot.fs` (or dedicated `Prompts.fs`) enforces decomposition rules: ≤3 tasks, vertical-slice preference, specific files+action+verify+done per task, return `needs_clarification` when ambiguous
- [ ] **PLANGEN-03**: Planner LLM output validated against JSON schema (reuse `JsonSchema.Net` infrastructure from `LLM-04` v1.0); 2-attempt retry on parse failure mirroring agent-loop policy
- [ ] **PLANGEN-04**: Plan generation rejects malformed plans at parse boundary with typed `AgentError.PlanInvalid` (extends or distinguishes from existing v2.0 `PlanInvalid` for `--plan` mode)

### Plan orchestration (PLANORCH-*)

Sequential execution of decomposed plan with session tracking.

- [ ] **PLANORCH-01**: New module `BlueCode.Core.PlanOrchestrator` exposes `runPlanMode : ILlmClient -> IToolExecutor -> userTask -> ct -> Task<Result<unit, AgentError>>`; pure routing logic in Core, no IO
- [ ] **PLANORCH-02**: Each invocation creates `.planning/quick/NNN-<slug>/` session directory with monotonic `NNN` (001, 002, ...) and slug derived from task description (lowercase, hyphens, ≤40 chars)
- [ ] **PLANORCH-03**: Plan persisted as `NNN-PLAN.md` in session dir; SUMMARY persisted as `NNN-SUMMARY.md` after all tasks complete; both follow GSD frontmatter conventions from `.planning/docs/gsd/04-FILE-PROTOCOL.md`
- [ ] **PLANORCH-04**: Per-task executor invocation uses fresh conversation (new system prompt mode) per `.planning/docs/gsd/05-ADOPTION-BLUEPRINT.md` Phase A; current task's `<files>`, `<action>`, `<verify>`, `<done>` inlined into executor prompt
- [ ] **PLANORCH-05**: STATE.md "Quick Tasks Completed" table row appended on plan completion (description, date, final commit hash, session dir relative path); table created on first row if missing

### Deviation rules + auth gates (DEV-*)

Encode "what to do when plan-not-foreseen-work-found" policy at system-prompt level.

- [ ] **DEV-01**: Executor system prompt includes Rules 1–3 auto-fix policy (bug found / missing critical / blocker) verbatim from `.planning/docs/gsd/02-EXECUTION-PIPELINE.md`; deviations recorded in SUMMARY.md "Deviations from Plan" section
- [ ] **DEV-02**: Executor system prompt includes Rule 4 stop-and-return policy for architectural changes; structured return as `architectural_decision_needed` status
- [ ] **DEV-03**: Authentication gate detection: executor recognizes "Not authenticated" / "401" / "403" / "Please run X login" patterns and returns `auth_required` status (NOT failure); structured prompt for user to authenticate then re-invoke
- [ ] **DEV-04**: Executor return structure documented in F# DU: `ExecutorOutcome = Completed | AuthRequired of cmd | ArchitecturalDecisionNeeded of explanation`; PlanOrchestrator dispatches on this DU

### Atomic commit (COMMIT-*)

Per-task atomic commits with type heuristic and rollback.

- [ ] **COMMIT-01**: New module `BlueCode.Cli.GitCommit` (Cli adapter — uses `IToolExecutor.RunCommand`) exposes `commitTask : taskName -> sessionId -> taskIndex -> modifiedFiles -> ct -> Task<commitHash>`; rejects `git add .` and `git add -A` patterns at the API level (compile-time list of forbidden flags)
- [ ] **COMMIT-02**: Commit type inferred from task name + modified file extensions: feat/fix/test/refactor/perf/docs/style/chore (heuristic table per CLAUDE.md commit protocol); user-overridable via plan-task `commit_type` optional field
- [ ] **COMMIT-03**: Commit message format `{type}(quick-{NNN}): {task name}` followed by structured body; commit hash captured for SUMMARY.md
- [ ] **COMMIT-04**: Plan-metadata commit (separate from per-task commits) at plan end stages PLAN.md + SUMMARY.md + STATE.md only; format `docs(quick-{NNN}): {description}`

### CLI / REPL surface (UX-*)

User-facing entry points and progress display.

- [ ] **UX-01**: Architecture decision recorded in early phase: rename existing `--plan` flag (v2.0 plan-mode) OR introduce new flag (e.g., `--decompose`, `--plan2`) for self-planning mode; tradeoff documented in PROJECT.md Key Decisions
- [ ] **UX-02**: REPL slash command extension (likely `/plan2` or `/decompose` matching CLI flag choice from UX-01); follows v2.5 SlashCommand DU + dispatcher pattern; no LLM calls inside dispatcher itself (planner LLM call invoked by orchestrator)
- [ ] **UX-03**: Spectre.Console plan display table (Task # / Name / Files / Estimated effort if any); printed BEFORE user confirm prompt; uses `printfn`-style or Spectre Markup per stdout/stderr stream-separation invariant
- [ ] **UX-04**: User confirm gate via `IKeyReader` port (reuse v2.0 PlanGate pattern): y/n responses; n → abort with no changes; future-extensible to `e` (edit plan) without breaking semantics
- [ ] **UX-05**: Per-task progress indicator on stdout (`▶ Task N/M: {name}` on start, `✓ committed {hash}` on success, `✗ {reason}` on failure); plan-mode tests use `Console.SetOut` redirection (compatible with existing test infrastructure per `documentation/howto/handle-expecto-console-redirection.md`)

## v2.7+ Requirements (deferred from this milestone's scope)

Per `.planning/docs/gsd/05-ADOPTION-BLUEPRINT.md` recommended adoption order, these are intentionally NOT in v2.6:

### Phase B — Goal-backward verification (deferred to v2.7)

- **VERIFY-01**: Plan frontmatter `must_haves` schema (truths, artifacts, key_links) — F# DU
- **VERIFY-02**: F# `PlanVerifier` module — grep-based 3-level checks (exists / substantive / wired)
- **VERIFY-03**: VERIFICATION.md output schema with structured `gaps:` for re-planning loop
- **VERIFY-04**: Stub-pattern detection regex library (TODO/FIXME/return null/etc.)

### Phase C — Plan checker (deferred to v2.7)

- **CHECK-01**: F# `PlanChecker` module — 6 dimensions (requirement_coverage / task_completeness / dependency_correctness / key_links_planned / scope_sanity / verification_derivation); code-only checks first (cycle, scope count), LLM checks toggleable
- **CHECK-02**: Revision loop ≤3 iterations between planner ↔ checker before user fallback
- **CHECK-03**: `config.json` workflow toggles (`plan_check`, `verifier`) mirroring GSD config

### Phase F — Wave-based parallel (skip indefinitely)

- **WAVE-01**: PlanOrchestrator wave grouping — single-LLM-server architecture makes ROI low (queued at server); revisit only if multi-LLM port architecture introduced

### Carried-forward from PROJECT.md (still observation-driven)

- **MODEL-SWITCH-01**, **SLASH-COMP-01**, **HIST-SEARCH-01**, **COMPACT-01**, **PRETTYPROMPT-HIST-1000**, **PRIORSTEPS-MSG-ORDER-01**, **ALLOWPATHS-GLOB-01**, **AGENT-LOOP-FEW-SHOT-01**, **COLDSTART-PRISTINE-01**, **SUBAG-01**, **PLAN-MODE-BENCH-01**, **STM-01**, **THINK-01**, **TOOLCALLS-01**

## Out of Scope (v2.6)

Explicitly excluded. Documented to prevent scope creep mid-milestone.

| Feature | Reason |
|---------|--------|
| Goal-backward verifier (Phase B) | Deferred to v2.7; v2.6 is MVP — verify by user acceptance + per-task `<verify>` command, not whole-plan goal achievement |
| Plan checker with LLM (Phase C) | Deferred to v2.7; quality risk for v2.6 mitigated by user confirm gate (UX-04) and small task count (≤3) |
| Wave-based parallel execution (Phase F) | Single-LLM-server (Qwen 122B @ 8001) makes parallel HTTP requests queue at server — no real speedup. Revisit if 2nd model port becomes routine. |
| Multi-plan / multi-phase orchestration | v2.6 scope is single-plan-per-invocation. GSD-style phase/milestone hierarchy ≠ blueCode invocation hierarchy in this milestone. |
| Auto-classification of "needs plan?" vs "single turn" | Add later if user friction surfaces; v2.6 uses explicit opt-in (CLI flag / slash command) per UX-01/02 |
| Replacing v2.0 `--plan` plan-then-execute mode | Architectural decision per UX-01: may rename, may coexist; either way the v2.0 plan-then-execute mode keeps working until milestone close at minimum |
| Cross-platform (Windows/Linux) commit helpers | Mac-only invariant from v1.0 holds; `git` invocation paths assume Unix shell |
| `.planning/quick/` directory git inclusion policy decisions | Out-of-band: respects user's existing `.gitignore` setup; orchestrator does not opinionate |

## Traceability

Populated by gsd-roadmapper agent during ROADMAP.md generation (2026-05-06). Each requirement maps to exactly one phase. Source: ROADMAP.md.

| Requirement | Phase | Status |
|-------------|-------|--------|
| PLANGEN-01 | Phase 37 — Plan generation foundation | Pending |
| PLANGEN-02 | Phase 37 — Plan generation foundation | Pending |
| PLANGEN-03 | Phase 37 — Plan generation foundation | Pending |
| PLANGEN-04 | Phase 37 — Plan generation foundation | Pending |
| PLANORCH-01 | Phase 38 — Plan orchestration | Pending |
| PLANORCH-02 | Phase 38 — Plan orchestration | Pending |
| PLANORCH-03 | Phase 38 — Plan orchestration | Pending |
| PLANORCH-04 | Phase 38 — Plan orchestration | Pending |
| PLANORCH-05 | Phase 38 — Plan orchestration | Pending |
| DEV-01 | Phase 39 — Deviation rules + auth gate | Pending |
| DEV-02 | Phase 39 — Deviation rules + auth gate | Pending |
| DEV-03 | Phase 39 — Deviation rules + auth gate | Pending |
| DEV-04 | Phase 39 — Deviation rules + auth gate | Pending |
| COMMIT-01 | Phase 40 — Atomic commit per task | Pending |
| COMMIT-02 | Phase 40 — Atomic commit per task | Pending |
| COMMIT-03 | Phase 40 — Atomic commit per task | Pending |
| COMMIT-04 | Phase 40 — Atomic commit per task | Pending |
| UX-01 | Phase 37 — Plan generation foundation (decision artifact, gates downstream naming) | Pending |
| UX-02 | Phase 41 — CLI / REPL surface + UX polish | Pending |
| UX-03 | Phase 41 — CLI / REPL surface + UX polish | Pending |
| UX-04 | Phase 41 — CLI / REPL surface + UX polish | Pending |
| UX-05 | Phase 41 — CLI / REPL surface + UX polish | Pending |

**Coverage:**
- v2.6 requirements: 22 total
- Mapped to phases: 22 ✓
- Unmapped: 0
- Phase distribution: Phase 37 = 5 (PLANGEN-01..04 + UX-01) | Phase 38 = 5 (PLANORCH-01..05) | Phase 39 = 4 (DEV-01..04) | Phase 40 = 4 (COMMIT-01..04) | Phase 41 = 4 (UX-02..05)

**UX-01 placement note:** Flag-naming decision (rename `--plan` vs new `--plan2` / `--decompose`) is a PROJECT.md Key Decisions artifact that gates downstream module/prompt/CLI naming. Lives in Phase 37 (earliest possible) so Phase 38 can name `PlanOrchestrator.runPlanMode` consistently and Phase 41 can wire CLI surface with the locked-in flag. The remaining UX-* (02..05) are user-facing surface code in Phase 41.

---
*Requirements defined: 2026-05-06*
*Last updated: 2026-05-06 after gsd-roadmapper agent populated traceability table (22/22 mapped)*
