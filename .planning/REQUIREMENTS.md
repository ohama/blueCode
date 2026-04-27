# Requirements: blueCode v2.0 Persistence + Planning

**Defined:** 2026-04-26
**Core Value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Milestone goal:** Break the process-lifetime constraint that v1 deliberately accepted. Bundle cross-turn memory (multi-turn REPL keeps context, `--resume <id>`) with plan-then-execute mode (typed plan DU, user approval gate, schema-validated execution). The two features share an architectural root (state outside a single `runSession`) and ship together by design.

## v2.0 Requirements (8 requirements, 2 categories)

Each requirement is user-centric, atomic, and testable. Success criteria for each map to a phase verifier check.

### Persistence

State outlives a single `runSession`. The REPL gains memory; sessions can resume across process restarts.

- [x] **PERSIST-01**: Multi-turn REPL maintains conversation history within a single session. ✓ (Phase 15, 2026-04-27)
  - **Goal:** When the user asks two consecutive questions in one REPL session, the second turn sees the first turn's `Step list` as prior context.
  - **Behavior:** REPL state extends from `unit` to `Session = { Id; Steps; CreatedAt; LastActivityAt }`. `runSession` accepts prior steps and appends new ones. New session = empty step list.
  - **Validation:** Multi-turn REPL test: turn 1 reads a file; turn 2 asks "what did I just read?" — agent answers correctly without re-reading. Mocked-LLM test mirrors the canonical flow.
  - **Out of scope:** Cross-process memory (covered by PERSIST-02/03). Compaction (v2.1 candidate).

- [x] **PERSIST-02**: Session state persists to `~/.bluecode/sessions/<id>.jsonl` between turns. ✓ (Phase 15, 2026-04-27)
  - **Goal:** After every completed turn (regardless of exit code), the full session state is on disk so a future `--resume <id>` can reconstruct it.
  - **Behavior:** Existing `~/.bluecode/session_<ts>.jsonl` (per-step crash log) is renamed/upgraded to `~/.bluecode/sessions/<id>.jsonl` with a `version: 2` header line. Each turn appends a `TurnComplete { steps; userPrompt; finalAnswer }` envelope. Session id is a stable ULID/UUID generated at session start.
  - **Validation:** Run `blueCode "..."`, verify `~/.bluecode/sessions/<id>.jsonl` contains version header + at least one turn envelope. Round-trip serialization test in `tests/BlueCode.Tests/SessionStoreTests.fs`.
  - **Out of scope:** Compaction (v2.1). Pruning old sessions (manual `rm -rf`).

- [x] **PERSIST-03**: `--resume <id>` flag loads a prior session and continues from its last step. ✓ (Phase 15, 2026-04-27)
  - **Goal:** `blueCode --resume 01J... "follow-up question"` reads `~/.bluecode/sessions/01J...jsonl`, reconstructs the `Session` record, and runs the new turn with prior context loaded.
  - **Behavior:** Argu accepts `--resume <ID>`. CompositionRoot wires `ISessionStore.Load` before invoking `runSession`. Unknown id → typed error (`AgentError.SessionNotFound id`) → exit 1 with stderr message. Corrupt JSONL → typed error → exit 1.
  - **Validation:** Live run: turn 1 with id captured, turn 2 with `--resume <id>` references turn 1 correctly. Test: load corrupt JSONL → returns `SessionNotFound` or `SessionCorrupt`, no exception.
  - **Out of scope:** Branching/forking sessions (v2.1+). Multi-resume merging.

- [x] **PERSIST-04**: New sessions get a fresh id automatically; `--new-session` forces a new session even when a resume might be implied. ✓ (Phase 15, 2026-04-27)
  - **Goal:** Every `blueCode` invocation without `--resume` starts a new session with a fresh id; `--new-session` is the explicit form for users who want guaranteed-fresh state.
  - **Behavior:** Argu accepts `--new-session` (boolean, default false). Without `--resume`, a new id is always generated. With both `--resume X --new-session`, error: "conflicting flags". Session id printed to stderr at startup so the user can grab it for later `--resume`.
  - **Validation:** Argu rejects conflicting `--resume X --new-session` at parse time. Sessionid line on stderr is grep-able. New-session flag covered by REPL test.
  - **Out of scope:** Listing sessions (v2.1 `/sessions` slash command).

### Planning

Agent emits a typed plan, user approves, agent executes. Plan validation catches malformed plans before they reach the user, not at runtime.

- [x] **PLAN-01**: Agent loop supports a `Plan` DU as a new `LlmOutput` variant (or sibling). ✓ (Phase 14, 2026-04-26; JSON parse wired Phase 16, 2026-04-27)
  - **Goal:** When invoked in plan mode, the LLM produces structured output `{ kind: "plan", steps: [{ tool, input, rationale }, ...], rationale: "..." }` validated against existing tool schema.
  - **Behavior:** `Domain.LlmOutput` extended with `LlmOutput.Plan of Plan` where `Plan = { Steps: PlannedStep list; Rationale: string }` and `PlannedStep = { Tool: ToolName; Input: ToolInput; Rationale: string }`. `JsonSchema.Net` validation extended for the plan kind.
  - **Validation:** Mocked-LLM test: plan-mode JSON parses to `LlmOutput.Plan` with correct structure; out-of-schema plan returns `ParseFailure`. Existing `LlmOutput.ToolCall` and `LlmOutput.FinalAnswer` paths unchanged (243/1/0 baseline preserved).
  - **Out of scope:** Plans with > 5 steps (rejected by PLAN-04). Recursive sub-plans (v2.1+ sub-agents).

- [x] **PLAN-02**: `--plan` CLI flag enables plan-then-execute mode for the next turn. ✓ (Phase 16, 2026-04-27)
  - **Goal:** `blueCode --plan "refactor X"` triggers the agent to emit a plan first, pause for user approval, then execute.
  - **Behavior:** Argu accepts `--plan` (boolean). Plan mode adds a system-prompt suffix that instructs the LLM to emit a plan before any tool call. CompositionRoot wires plan mode into `runSession`. `--plan` + `--resume <id>` is allowed (resume into plan mode for the next turn).
  - **Validation:** Live run: `blueCode --plan "list 3 files in src"` shows a plan, prompts for approval, executes on "yes". Test: with `--plan`, the first LLM call gets system-prompt suffix `[PLAN MODE]` (or equivalent marker).
  - **Out of scope:** Per-turn plan-mode toggle inside REPL (v2.1 `/plan` slash command). Default plan mode (always-on).

- [x] **PLAN-03**: User approval gate between plan emission and execution — accept / reject / edit-and-retry. ✓ (Phase 16, 2026-04-27)
  - **Goal:** After the LLM emits a valid plan, the user is shown a rendered plan and prompted: `[a]ccept / [r]eject / [e]dit / [q]uit`. Accept proceeds to execution; reject sends a "plan rejected, try a different approach" message back to LLM and re-prompts; edit drops user into a comment field that's appended to the next user message; quit exits with code 0.
  - **Behavior:** Spectre.Console renders the plan as a numbered table (step #, tool, input preview, rationale). `Repl.fs` (or a new `PlanGate.fs`) reads a single keystroke, dispatches accordingly. On reject, agent loop reruns the same turn with `[PLAN REJECTED: <reason>]` injection.
  - **Validation:** Live run with mocked stdin: typing `a` proceeds to execute; typing `r` re-prompts LLM; typing `q` exits 0. Test: PlanGate decodes keystrokes deterministically.
  - **Out of scope:** Per-step approval (whole-plan only). Editing individual plan steps (only the next-user-message hint).

- [x] **PLAN-04**: Plan validation runs BEFORE user sees approval prompt. ✓ (Phase 14 pure validator + Phase 16 JSON parse wiring, 2026-04-27)
  - **Goal:** A malformed plan (unknown tool, schema-invalid input, > 5 steps, duplicate identical steps) is rejected at parse time and the LLM is asked to retry. The user never sees a malformed plan.
  - **Behavior:** Plan validator: each `PlannedStep.Tool` exists in `ToolRegistry`; each `PlannedStep.Input` validates against the tool's `JsonSchema`; `Steps.Length ≤ 5`; no two adjacent steps are byte-identical (loop-guard analog). Validation failures map to `AgentError.PlanInvalid <reason>` and trigger the same 2-attempt retry as `LlmOutput` parse failures.
  - **Validation:** Mocked plans for each failure mode (unknown tool, schema-invalid input, 6 steps, duplicate steps) all return `PlanInvalid` and trigger retry. After 2 retries, agent surfaces the error to the user (not the malformed plan).
  - **Out of scope:** Static analysis ("this plan won't achieve the goal"). Cross-step dependency analysis.

## Out of Scope

v1 boundaries unchanged for permanent OOS items. v2.0 explicitly excludes from this milestone:

| Feature | Reason |
|---------|--------|
| Sub-agent delegation (`Agent` tool) | Useful only after memory + planning land; without them, sub-agents repeat v1's stateless pain. v2.1+ candidate. |
| Slash commands (`/sessions`, `/plan`, `/clear`, `/context`) | UX layer over the CLI flags. Defer until flags work. v2.1+ candidate. |
| Streaming output (STM-01) | Deferred 7th time. UX win but not architectural. Decoupled from v2.0's state-management focus. |
| LLM-aware context compaction | Natural follow-up to PERSIST-02 once we have real session lengths to compact against. v2.1 candidate. |
| Session listing / pruning UI | `ls ~/.bluecode/sessions/` is sufficient for v2.0. v2.1+ adds `--list-sessions` or `/sessions`. |
| Branching / forking sessions | Single-resume, no merge. v2.1+ if observation surfaces need. |
| Cross-host session sync | Mac-only ethos preserved. Permanent OOS. |
| MCP / LSP / Plugin / hook / GUI / Windows·Linux / AOT | v1.0 OOS unchanged across v1.x and v2.0. |

## Future Requirements (v2.1+ candidates)

Tracked for awareness; not pulled into v2.0 roadmap.

- **COMPACT-01** (v2.1) — Auto-compaction when session approaches 80% of `max_model_len`. Default: drop oldest tool results, keep thoughts.
- **SLASH-01** (v2.1) — `/sessions`, `/plan`, `/clear` slash commands inside REPL.
- **SUBAG-01** (v2.1+) — Sub-agent delegation via Agent tool. Requires memory + planning to be stable first.
- **STM-01** (v2.1+ deferred 7x) — SSE token streaming.
- **ROU-05** (deprioritized) — Auto 32B→72B escalation on MaxLoopsExceeded.
- **CLI-08** (minor) — Ctrl+C "Cancelling…" display.
- **OPS-01** (deprioritized) — launchd-based prompt cache kickstart.
- **OBS-06** (minor) — Per-port `MaxModelLen` visibility.

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| PERSIST-01  | Phase 14 (shape) + Phase 15 (wiring) | ✓ Complete (Phase 15, 2026-04-27) |
| PERSIST-02  | Phase 15 | ✓ Complete (Phase 15, 2026-04-27) |
| PERSIST-03  | Phase 15 | ✓ Complete (Phase 15, 2026-04-27) |
| PERSIST-04  | Phase 15 | ✓ Complete (Phase 15, 2026-04-27) |
| PLAN-01     | Phase 14 (DU) + Phase 16 (JSON parse) | ✓ Complete (Phase 16, 2026-04-27) |
| PLAN-02     | Phase 16 | ✓ Complete (Phase 16, 2026-04-27) |
| PLAN-03     | Phase 16 | ✓ Complete (Phase 16, 2026-04-27) |
| PLAN-04     | Phase 14 (validator) + Phase 16 (wiring) | ✓ Complete (Phase 16, 2026-04-27) |

**Coverage:**
- v2.0 requirements: 8 total
- Mapped to phases: 8/8 ✓
- Unmapped: 0

**Phase assignment (canonical — each req owns exactly one delivery phase):**

| Requirement | Delivery Phase |
|-------------|----------------|
| PERSIST-01  | Phase 15 |
| PERSIST-02  | Phase 15 |
| PERSIST-03  | Phase 15 |
| PERSIST-04  | Phase 15 |
| PLAN-01     | Phase 14 |
| PLAN-02     | Phase 16 |
| PLAN-03     | Phase 16 |
| PLAN-04     | Phase 14 |

*Note: PERSIST-01's `Session` shape and PLAN-04's validator logic land in Phase 14 (Domain types), but behavioral delivery (user-observable) completes in Phase 15 and Phase 16 respectively.*

---
*Requirements defined: 2026-04-26*
*Last updated: 2026-04-26 — traceability filled after roadmap creation*
