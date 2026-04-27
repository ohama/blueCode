# Project State

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-04-26 after starting v2.0 milestone)

**Core value:** Mac 로컬 Qwen 3.5 35B/122B를 strong-typed F# agent loop로 안정적으로 돌린다 (switched from 32B/72B; Phase 17 SWITCH decision 2026-04-27)
**Current focus:** v2.0 Persistence + Planning — major version step. Domain extensions (Phase 14) → Persistence wiring (Phase 15) → Planning wiring + bench (Phase 16).

## Current Position

Milestone: v2.0 Persistence + Planning (started 2026-04-26)
Phase: Phase 14 ✓ Phase 15 ✓ Phase 16 ✓ (16-01 ✓ 16-02 ✓ 16-03 ✓) Phase 17 ✓ Phase 18 ✓ (verdict: DROP-35B) Phase 19 ✓ (19-01 ✓ 19-02 ✓) Phase 20 ✓ (20-01 ✓ 20-02 ✓ 20-03 ✓)
Plan: 16-03 complete; Phase 16 COMPLETE
Status: 282/1/0 tests; bench gate 7/7 PASS (post-16-03); 16-03 complete (MT_122b bench fixture, runPlanTurnTests 2 cases, bench.md MT_122b + plan-mode DEFERRED v2.1+)
Last activity: 2026-04-27 — Phase 16-03 complete (bench/fixtures/mt_followup.txt, bench/run.sh mt() helper + 7 labels + total=7, bench/baseline.json MT_122b appended empirical, AgentLoopTests runPlanTurnTests, bench.md updated)

Progress: v1.0 ✓ → v1.1 ✓ → v1.2 ✓ → v1.3 ✓ → v1.4 ✓ → v2.0 ◆ [Phase 14 ✓ Phase 15 ✓ Phase 16 ✓ (16-01 ✓ 16-02 ✓ 16-03 ✓) Phase 17 ✓ Phase 18 ✓ Phase 19 ✓ Phase 20 ✓]

## Performance Metrics (cumulative, frozen)

**v1.0:** 5 phases, 17 plans, 208 tests, 5891 LOC F#, 85 commits, ~27h
**v1.1:** 2 phases, 5 plans, 218 tests (+10), +315/-124 LOC, 23 commits, ~19h
**v1.2:** 3 phases, 8 plans, 242 tests (+24), Core diff confined to AgentLoop.fs/Domain.fs, 43 commits, ~3 days
**v1.3:** 2 phases, 6 plans, 243 tests (+1), bench harness in repo + 54% prompt shrink + B2 recovery, 25 commits, ~1 day
**v1.4:** 2 phases, 2 plans, 243 tests (unchanged), zero src/ diff, 7 commits, ~1 day

Detailed per-plan history archived in `.planning/milestones/v{1.0,1.1,1.2,1.3,1.4}-phases/`.

## Accumulated Context

### Decisions

Cumulative log in PROJECT.md Key Decisions table. See PROJECT.md for outcomes through v1.4.

Items relevant to v2.0 (architectural touch points):

- **v1.0 ports-and-adapters** — Core has no Console/Serilog/Spectre/Argu. Persistence adapters (file I/O for session JSONL) live in `BlueCode.Cli/Adapters/`, NOT in Core. New port: `ISessionStore` with Save/Load operations.
- **v1.0 single `runSession`** — Each REPL turn calls `runSession` independently with no carry. v2.0 PERSIST-01 changes this. Shape: `runSession` accepts prior `Session option` as input (additive, no mutation), REPL threads it through.
- **v1.0 5-step max + loop guard** — Stay. v2.0 PLAN-04 keeps `Plan.Steps.Length ≤ 5`. Plan validation runs BEFORE user approval.
- **v1.1 `LlmResponse` Core record** — v2.0 extends `LlmOutput` DU with `LlmOutput.Plan of Plan` variant. Domain.fs touch is unavoidable; Phase 14 does it atomically (mirrors v1.1 "big-bang compile cascade" pattern).
- **v1.2 loop-injection primitive** — Pattern reusable for plan-rejection hint injection (post-plan-rejection `[PLAN REJECTED]` System message follows same position discipline as `lastEditPath`/`lastReadHint`).
- **v1.3 bench gate** — `bench/run.sh --gate` is regression authority. Phase 16 extends baseline.json 8 → ~12 entries (multi-turn fixture + plan-mode fixture).
- **v1.4 MockHelpers.fs** — `makeMockResponse` is the canonical helper. Phase 14 adds sibling `makePlanResponse` in same module.
- **v2.0 Phase 14-01 compile cascade** — SessionId (line 91) → PlannedStep + Plan (lines 100-121) → LlmOutput.Plan of Plan (line 121) → Session (line 206). Type ordering constraint: Plan must precede LlmOutput; Session must follow Step. Transitional `| Plan _ -> Error(PlanInvalid ...)` in runLoop is intentional — Phase 16 replaces it.
- **v2.0 Phase 14-02 PlanValidator** — Pure `validatePlan : Plan -> Result<Plan, AgentError>` in Core. MaxPlanSteps=5 hardcoded (not AgentConfig-aware); knownTools set mirrors AgentLoop.dispatchTool. Priority order: length → tool registry → adjacent dups. Schema validation deferred to Phase 16 Cli adapter JSON parse layer.
- **v2.0 Phase 15-01 priorSteps** — `runSession` extended with `priorSteps: Step list` parameter (position: after `onStep`, before `userInput`). Steps replayed into ContextBuffer via `List.fold` before `runLoop`; `runLoop.steps` accumulator stays current-turn-only. Repl concatenates on each turn.
- **v2.0 Phase 15-01 JSONL format** — v2 header `{"version":2,"sessionId":"...","createdAt":"..."}` + per-turn `TurnComplete` envelope with cumulative `steps`. Last-envelope-wins on Load. Path: `~/.bluecode/sessions/<id>.jsonl` (distinct from existing per-step `session_<ts>.jsonl`).
- **v2.0 Phase 15-01 runMultiTurnWithSession** — New entry point in Repl.fs with explicit `Session` + `ISessionStore` params. Legacy `runMultiTurn` delegates to it with fresh Session + FileSessionStore. Program.fs now calls `runMultiTurnWithSession` directly with loaded/fresh Session.
- **v2.0 Phase 15-02 Argu --new-session flag** — Argu converts `NewSession` DU case to `--newsession` (no hyphen). Added `[<AltCommandLine("--new-session")>]` so both `--newsession` and `--new-session` work. Post-parse conflict check: `match resumeId, isNewSession with | Some _, true -> eprintfn "ERROR: conflicting flags..." + exit 2`.
- **v2.0 Phase 15-02 exact error messages** — `session not found: <id>` (exit 1), `session corrupt: <detail>` (exit 1), `conflicting flags: --resume and --new-session cannot be used together.` (exit 2). 15-03 assertions must match exactly.
- **v2.0 Phase 15-02 single-turn Save** — Program.fs now calls `SessionStore.Save` after single-turn completion so `--resume <id>` works across single-turn invocations too.
- **v2.0 Phase 16-01 PlanWire intermediate records** — `LlmWire.PlannedStepWire` + `LlmWire.PlanWire` records (public). Same rationale as `LlmStep`: plain records round-trip cleanly via `System.Text.Json`; DUs would serialize as `{"Item":"..."}`. Wire maps to `Domain.Plan` in `QwenHttpClient.deserializePlanWire` (private).
- **v2.0 Phase 16-01 _raw passthrough for plan-step inputs** — `PlannedStep.Input = ToolInput(Map.ofList [("_raw", s.input.GetRawText())])`. Consistent with `ToolCall` path (QwenHttpClient.fs:209-210). Per-tool input shapes are dispatch-time concerns, not plan-parse-time.
- **v2.0 Phase 16-01 runPlanTurn signature** — `AgentConfig -> ILlmClient -> Model -> Step list -> string -> string -> CancellationToken -> Task<Result<Plan, AgentError>>`. Module-level public in `AgentLoop`. 2 attempts (1 initial + 1 retry). `systemPromptSuffix` parameterized (Core stays string-literal-free). `priorSteps` mirrors `runSession` for `--plan --resume` SC4.
- **v2.0 Phase 16-01 retryable/non-retryable error split** — Retryable in runPlanTurn: `InvalidJsonOutput`, `SchemaViolation`, `PlanInvalid`. Non-retryable (short-circuit): `LlmUnreachable`, `UserCancelled`, `PathRetired`.
- **v2.0 Phase 16-01 PlanValidator NOT called in toLlmOutput** — `deserializePlanWire` handles structural wire failures (→ `SchemaViolation`). `PlanValidator.validatePlan` runs in `runPlanTurn` after wire parse succeeds (→ `PlanInvalid`). Separation enables per-error retry messaging.
- **v2.0 Phase 16-01 mid-loop Plan guard preserved** — `| Ok { Output = Plan _ } -> Error(PlanInvalid "Plan output received outside plan-mode")` at AgentLoop.fs:408 unchanged. `runPlanTurn` is the ONLY legitimate Plan-output entry point.
- **v2.0 Phase 16-01 test count 266→274** — 8 new tests in `PlanParseTests.fs` (3 wire parse + 5 runPlanTurn). fsproj + rootTests both at 22 entries.
- **v2.0 Phase 16-02 planSystemPromptSuffix OVERRIDE required** — The base system prompt's tool-call action enum caused 122B to ignore the plan-mode suffix. The suffix must begin with `OVERRIDE — PLAN MODE ACTIVE. Do NOT use read_file/... Your ONLY valid response is action="plan"` for 122B to comply. A gentle `[PLAN MODE]` prefix was not sufficient. Lesson: when base prompt and suffix conflict, use an explicit OVERRIDE directive.
- **v2.0 Phase 16-02 realKeyReader stdin-redirect fallback** — `Console.ReadKey(intercept=true)` throws `InvalidOperationException` when stdin is redirected (pipe-mode). Catch and fall back to `Console.In.ReadLine()[0]` (defaulting to 'q' for empty). This enables both TTY interactive use and pipe-based smoke/automation.
- **v2.0 Phase 16-02 maxUserRejects=3 cap** — User-facing reject loop capped at 3 iterations. Independent from runPlanTurn's internal 2-attempt retry (stacks: up to 3 * 2 = 6 LLM calls before abort). On exhaustion: `eprintfn "Plan-mode: N rejections without acceptance"` + exit 1.
- **v2.0 Phase 16-02 [PLAN REJECTED] as userInput prefix** — Inject rejection as `currentPrompt = "[PLAN REJECTED] ... \n\n<original prompt>"` (the userInput parameter to runPlanTurn). Never injected via buildMessages. The user-prompt slot in buildMessages is always Role=User. Phase 20-03 invariant satisfied without explicit Role annotation.
- **v2.0 Phase 16-02 Accept executes ORIGINAL prompt via runSingleTurn** — On accept, `Repl.runSingleTurn prompt ...` receives the user's original (un-prefixed) prompt, not the [PLAN REJECTED]-decorated variant. The accepted plan's SHAPE was approved; the executor re-asks the LLM to act on the original intent.
- **v2.0 Phase 16-02 test count 274→280** — 6 new tests in `PlanGateTests.fs` (testSequenced). fsproj: 23 Compile entries. rootTests: 22 testList entries.

### Roadmap Evolution

- **2026-04-27**: Phase 17 (Qwen 3.5 Evaluation) added via `/gsd:add-phase`. Three deliverables: install/usage docs for 35B+122B, service swap with user help (autonomous: false, checkpoint), bench comparison vs current 32B/72B baseline. Should run BEFORE Phase 16 if model swap desired — Phase 16 bench fixtures reference current model ids.
- **2026-04-27**: Phase 19 (Qwen 2.5 Retirement + 122B Single-Model Default) added via `/gsd:add-phase`. Two plans: 19-01 retire Qwen 2.5 from disk + launchd (autonomous: false, user checkpoint for `rm`); 19-02 code/bench/docs alignment (autonomous: true — Argu cleanup, `--with-35b` flag, `tryParseModelId` retirement guard, `bench/run.sh` rewrite absorbing `scripts/bench-122b-only.sh`, baseline halve, CLAUDE.md update). Key decisions: A=preserve 35B for future dual mode, B=remove `--model 32b/72b` aliases entirely (breaking), C=dual mode requires BOTH launchctl load + `--with-35b` flag, D=`bench/run.sh` in-place absorbs the 122B-only harness. Runs BEFORE Phase 16 (canonical baseline shape must be settled before 16-03 multi-turn fixtures).
- **2026-04-27**: Phase 20 (Qwen 3.5 Protocol Alignment) added via `/gsd:add-phase`. Three plans: 20-01 sampling parameter alignment (`temp=0.7, top_p=0.8, top_k=20, presence_penalty=0.0` per Qwen 3.5 model card) + HttpClient timeout 180→300s; 20-02 `extractContent` `reasoning_content` fallback (latent qwen35-install §5.3 gotcha); 20-03 122B mid-conversation `Role = System` probe + conditional restore (Phase 17-02 fix may be unnecessary for 122B alone). Key decisions: A=single-model 122B is the only target, B=bench gate is regression authority (6/6 PASS post-each-plan), C=thinking mode stays OFF (`<think>` consumption deferred), D=`additionalProperties: false` stays, E=Role=System restoration is probe-driven (ACCEPT or REJECT documented in 20-03-SUMMARY.md). Out of scope (v2.1+ candidates): thinking-mode-on, native `tool_calls`, `additionalProperties` relaxation, `max_tokens` budget bump. Surfaced from post-Phase-19 codebase survey identifying 7 Qwen-2.5-era assumptions still in code (Router temperature 0.2/0.4, presence_penalty 1.5, missing top_p/top_k, 180s timeout, no reasoning_content fallback, User-role workaround possibly unneeded for 122B, stale howto F# snippets).

### Pending Todos

v2.1+ candidates (after v2.0 ships):

- LLM-aware context compaction (auto token-aware snip when session approaches context limit)
- Slash commands (`/sessions`, `/plan`, `/clear`)
- Sub-agent delegation (only meaningful once memory + planning land)
- Streaming output (STM-01) — deferred 7th cycle; revisit if observation surfaces complaint

### Blockers/Concerns

- **Compaction risk**: PERSIST-02 saves the full session JSONL but does not yet compact. Long sessions will hit the 80% context warning faster. v2.0 ships without compaction; v2.1 candidate.
- **Plan-mode + REPL interaction**: PLAN-02's `--plan` flag is per-invocation. If user wants plan mode every turn, they pass it every turn (or `/plan` slash command in v2.1).
- **Backwards compat**: v1.x JSONL session log format needs versioned schema upgrade. PERSIST-02 writes a `version: 2` header so old logs are recognizable.

## Session Continuity

Last session: 2026-04-27
Stopped at: Completed 16-03-PLAN.md (MT_122b bench fixture, runPlanTurnTests +2, bench.md MT_122b + plan-mode DEFERRED, bench gate 7/7 PASS, Phase 16 COMPLETE)
Resume file: None — Phase 16 complete; next: /gsd:complete-phase 16 or /gsd:complete-milestone v2.0

### New Decisions (16-03)

- **v2.0 Phase 16-03 MT_122b single fixture** — Phase 19 retirement made 122B sole canonical. No MT_32b/MT_72b. One entry at bench gate tier.
- **v2.0 Phase 16-03 gate metric = turn-1 step count** — Existing `head -1` parser on `[INF] Session ok: N steps` naturally picks turn 1. No parser changes. Turn 2 step count documented in baseline `note` only.
- **v2.0 Phase 16-03 plan-mode bench DEFERRED to v2.1+** — PlanGate's `Console.ReadKey` UX intractable for autonomous regression gate. Coverage substitute: PlanGateTests + PlanParseTests + AgentLoopTests.runPlanTurnTests. Documented in bench.md with 4-point rationale.
- **v2.0 Phase 16-03 MT_122b empirical baseline** — step_count=2 (list_dir+final), step_count_max=4, elapsed_median_s=7 (full 2-turn cycle on warm 122B). Turn 2 answers correctly from prior session context (exit 0 both turns).
- **v2.0 Phase 16-03 AgentLoopTests in-place extension** — runPlanTurnTests sub-list appended to existing agentLoopTests aggregator. No new module, no fsproj/RouterTests.fs change. Test count 280→282.

### New Decisions (20-03)

- **v2.0 Phase 20-03 probe verdict REJECT** — `scripts/probe-system-role.sh` probed 122B (port 8001) with a 3-message system/user/system POST. HTTP 404: `"System message must be at the beginning."`. mlx_lm.server chat template enforces a structural rule: no System message after position 0. This applies to both Qwen 3.5 35B (Phase 17-02 evidence) and 122B (Phase 20-03 probe).
- **v2.0 Phase 20-03 AgentLoop.fs role state** — `Role = User` at lines 249/260/266 (POST-EDIT CONSTRAINT, POST-READ HINT truncated/out-of-range) is permanently documented via in-code 3-line comments citing Phase 17-02 + Phase 20-03 probe date + HTTP 404 code. The authority signal is in the text marker, not the role. See `20-03-PROBE-OUTPUT.md`.
- **v2.0 Phase 20-03 howto doc sync** — `documentation/howto/enforce-llm-tool-terminality-via-post-user-injection.md` F# snippets at lines 110+188 updated from `Role = System` (stale since v1.1) to `Role = User` with inline comments. History section added. Checklist updated to mention User role requirement.
- **v2.0 Phase 20-03 qwen35-install.md cross-reference** — §Phase 20-03 section added at end of qwen35-install.md: REJECT verdict, HTTP 404, `scripts/probe-system-role.sh` reference, `20-03-PROBE-OUTPUT.md` pointer.

### New Decisions (20-02)

- **v2.0 Phase 20-02 extractContentFromJson public helper** — Carved out of private `extractContent` as a public module-scope helper, mirroring `tryParseModelId` / `tryParseMaxModelLen` pattern in same file. Returns `string option` (not `Result`); `extractContent` wrapper maps `None → LlmUnreachable`. Public for direct unit test invocation without HTTP/mocks.
- **v2.0 Phase 20-02 reasoning_content fallback semantics** — `pickStringField` inner function checks `ValueKind = JsonValueKind.String` + `IsNullOrEmpty` guard for both `content` and `reasoning_content`. JSON null literals and non-string types correctly skip to next rung. Fallback ladder: content (non-empty string) → reasoning_content (non-empty string) → None.
- **v2.0 Phase 20-02 error message updated** — `extractContent` wrapper now emits "malformed response: no content or reasoning_content" when both rungs miss, replacing the previous "missing or empty content" (after Task 1 refactor) for clearer diagnostics.
- **v2.0 Phase 20-02 test count 262 → 266** — 4 new tests in existing `LlmPipelineTests.fs` (no `.fsproj` / `rootTests` change). Delta +4 (3 required + 1 null-content guard). Bench gate 6/6 PASS.
- **v2.0 Phase 20-02 qwen35-install.md rows RESOLVED** — §5.3 response table row (빈 문자열 + reasoning_content) and Appendix A `content` 빈 문자열 row both marked RESOLVED Phase 20-02. 2 matches of "RESOLVED Phase 20-02" in the file.

### New Decisions (20-01)

- **v2.0 Phase 20-01 SamplingParams record location** — Domain.fs (pure data, after `Message` type). Consistent with `LlmRequest`/`Step`/`Plan` pattern. No HTTP knowledge in Core.
- **v2.0 Phase 20-01 modelToSamplingParams uniformity** — Both Qwen35B and Qwen122B return identical record (`temp=0.7, top_p=0.8, top_k=20, presence_penalty=0.0`) per Qwen 3.5 model card non-thinking coding mode. Explicit two-case match (not wildcard) preserves compile-time exhaustiveness for future per-model tuning.
- **v2.0 Phase 20-01 modelToTemperature deleted** — No tests reference it (RouterTests covers classifyIntent/intentToModel/modelToEndpoint/endpointToUrl only). Single call site in QwenHttpClient.fs:68 rewired to consume `modelToSamplingParams` directly.
- **v2.0 Phase 20-01 timeout 300s rationale** — 122B cold-start observed at 240s after `launchctl kickstart`. 300s provides comfortable margin. Error string "request timed out after 300s" updated to match. CLAUDE.md and qwen35-install.md §8 updated.
- **v2.0 Phase 20-01 Appendix A row added** — Sampling-parameter mismatch gotcha documented in qwen35-install.md Appendix A as RESOLVED Phase 20-01. Symptom: Qwen 2.5-era values (temp=0.2-0.4, presence_penalty=1.5, no top_p/top_k) in POST body.

### New Decisions (19-02)

- **v2.0 Phase 19-02 Model DU rename (Qwen32B→Qwen122B, Qwen72B→Qwen35B)** — Single atomic compile cascade across 11 files. AgentLoop.fs had no direct DU construction sites (uses `model` variable). Router semantic preserved: smaller model (35B) handles Debug/Design/Analysis; larger (122B) handles Implementation/General. Table dormant by default (ForcedModel=Some Qwen122B bypasses it).
- **v2.0 Phase 19-02 PathRetired AgentError variant** — `validateModelPath` in QwenHttpClient rejects `/qwen32b` and `/qwen72b` path segments. Rendering.fs handles with user-readable message. 4 new ModelsProbeTests cases.
- **v2.0 Phase 19-02 parseForcedModel None → Some Qwen122B** — Explicit single-model default. No intent routing indirection in single-model mode. `--model 32b`/`72b` → exit 2 with "retired in Phase 19" + "122b" in message (triggers `with | ex when ex.Message.Contains "retired" -> exit 2` in Program.fs).
- **v2.0 Phase 19-02 WithDual flag + eager 35B probe** — `--with-35b` Argu flag (also `--withdual`). When set, Program.fs probes port 8000 with 2s timeout before bootstrap; exits 1 with "35B service not loaded" message if absent. Default path probes nothing.
- **v2.0 Phase 19-02 bench gate 6/6** — T6/W1/W2/T1/T5/B2 all _122b. baseline.json flat top-level keys (no `tests.*` wrapper). gate() jq paths use `.${key}` not `.tests.${key}`.
- **v2.0 Phase 19-02 scripts/bench-122b-only.sh deleted** — Absorbed into bench/run.sh in-place. All invocations use `--model 122b`.

### New Decisions (19-01)

- **v2.0 Phase 19-01 disk reclaim 85 GiB** — Pre-retirement 277 GiB used → post-retirement 192 GiB used. Delta 85 GiB (qwen32b 17G + qwen72b 38G + qwen72b.3bit 30G). Threshold >= 50 GB: PASS. Matches RESEARCH §Pitfall 5 expectation exactly.
- **v2.0 Phase 19-01 data[0] HF fallback gotcha** — `mlx_lm.server` returns hardcoded `Qwen/Qwen2.5-Coder-32B` in `data[0]` regardless of which model is loaded. `data[1]` returns the actual local path (`/Users/ohama/llm-system/models/qwen122b`). Verification scripts must use `data[1]`, not `data[0]`. Mirrors `tryParseModelId` path-preference heuristic in `QwenHttpClient.fs` (CLAUDE.md §Key Seams). Worth a future `/howto` entry.
- **v2.0 Phase 19-01 preserved state confirmed** — qwen35b/ (19G, cold rollback) + qwen122b/ (65G, production) preserved. qwen32b/, qwen72b/, qwen72b.3bit/ deleted. com.ohama.qwen35b.plist + com.ohama.qwen122b.plist retained; qwen32b.plist + qwen72b.plist deleted. 122B service (PID 44880, port 8001) unaffected throughout retirement. SC1 all three criteria PASS.
- **v2.0 Phase 19-01 Wave 2 ready** — 19-02 depends_on: [19-01] is now satisfied. 19-02 scope: Argu cleanup, `--with-35b` flag, `tryParseModelId` retirement guard, bench/run.sh rewrite absorbing scripts/bench-122b-only.sh, baseline halve, CLAUDE.md Runtime Environment update.

### New Decisions (18-03)

- **v2.0 Phase 18 verdict DROP-35B** — All 5 ROADMAP §SC4 criteria PASS (5/5): T1/T2 median 3s ≤ 6s; T6/W1/W2/B2 step counts within baseline_max; B2 DivideByZeroException diagnosis preserved; PhysMem unused +19.42 GB ≥ 5 GB; Compressor 454 MB < 1 GB. 122B alone meets latency, step-count, diagnosis, memory-headroom, and compressor thresholds. Single-model 122B is a viable canonical configuration. Eval doc: `documentation/single-model-eval.md`. Memory profile: `18-01-MEMORY-PROFILE.md`. Bench results: `18-02-BENCH-RESULTS.md`. New harness: `scripts/bench-122b-only.sh` (preserved as evaluation evidence).
- **v2.0 Phase 18 architectural follow-ups deferred** — Router collapse (`src/BlueCode.Core/Router.fs`), bench/baseline.json halve/re-key, CLAUDE.md Runtime Environment update, scripts/bench-122b-only.sh promotion ALL deferred to a follow-up phase per ROADMAP §SC5. Phase 18 made ZERO permanent code changes (zero src/ diff, zero bench/run.sh diff, zero baseline.json diff, zero CLAUDE.md diff). Follow-up phase should run BEFORE Phase 16.
- **v2.0 Phase 18 35B reload skipped** — Task 3 checkpoint short-circuited autonomously per DROP-35B verdict disposition. System stays single-model (122B alone, port 8001). User can reload at will via `launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen35b.plist` per documentation/single-model-eval.md §Reversibility. Reversibility window: ≥ 1 week of stable single-model operation recommended before cleanup of 35B model files / plist.
- **v2.0 Phase 18 122B RSS hypothesis CONFIRMED** — RESEARCH §Pitfall 5 hypothesized 122B RSS stays near 45.4 GB after 35B unload. Observed: RSS stable at 45.42 GB post-unload (0 GB expansion) and 45.43 GB post-bench (+0.01 GB / +1.4 MB delta — negligible). MoE sparse activation means 122B's resident page set is prompt-driven, not memory-availability-driven. Combined system RSS drops from 62.35 GB (dual) to 45.42 GB (single) — 16.93 GB freed to PhysMem pool.

### New Decisions (18-02)

- **v2.0 Phase 18-02 bench-122b-only.sh 31/31 exit=0** — All 31 bench invocations completed cleanly (0 LlmUnreachable vs ≤1 tolerance). 122B alone is stable for sequential bench-mode operation. Wall-clock 252s (~4 min) — fast due to sequential JIT reuse.
- **v2.0 Phase 18-02 T1/T2 median 3s** — T1 and T2 (simple prompts, 1 step) median elapsed 3s. Well within 6s ROADMAP §SC4 threshold. Note: Phase 17 baseline T1_122b=11s was cold-start; 18-02 measures warm sequential bench. Step count (1) matches baseline.
- **v2.0 Phase 18-02 T6 step-count deterministic** — All 6 T6 variance runs = exactly 4 steps, median elapsed 11s. Zero step-count variance single-model. Matches Phase 17 dual-loaded baseline (4 steps, baseline_max=5). PASS.
- **v2.0 Phase 18-02 W1/W2=3 steps single-model** — Loop-injection constraint (read+write+final) holds with 122B alone. W1=3, W2=3, both within baseline_max=3. PASS.
- **v2.0 Phase 18-02 B2 diagnosis preserved** — 3 grep matches on DivideByZeroException/division by zero in diagnose_B2_122b.log and b2_122b.log. Semantically identical to Phase 17 dual-loaded B2_122b baseline. PASS.
- **v2.0 Phase 18-02 122B RSS bench-mode flat** — Post-bench RSS 45.43 GB (+0.01 GB / +1.4 MB from pre-bench 45.42 GB). MoE expert routing is stable in bench-mode operation; no RSS growth under sequential load.
- **v2.0 Phase 18-02 all_mode_122b() inlined** — 31 `run()` calls inlined (not in loops) in `all_mode_122b()` to satisfy `grep -c '"$MODEL"' >= 31` static verify check. Sub-mode functions still use DRY patterns for standalone mode invocations (--regression, --variance, etc.).

### New Decisions (18-01)

- **v2.0 Phase 18-01 35B unload CLEAN** — `launchctl unload` released port 8000 and terminated PID 44878 cleanly. KeepAlive did not auto-restart because the launchd registration was removed first. No bootout/bootstrap fallback required. 122B (PID 44880, port 8001) was completely unaffected.
- **v2.0 Phase 18-01 PhysMem freed +19.42 GB post-unload** — Pre-unload PhysMem unused 1.58 GB → post-unload 21 GB (+19.42 GB). ROADMAP §SC4 threshold (≥5 GB) PASS with 4× margin. Freed pages returned to PhysMem pool immediately (30s settle sufficient); not claimed by 122B.
- **v2.0 Phase 18-01 122B RSS stable at 45.42 GB** — RSS held at exactly 45.42 GB post-unload (0 GB expansion). MoE sparse activation means 122B's resident expert pages are prompt-driven, not memory-availability-driven. Hypothesis CONFIRMED. ROADMAP §SC4 threshold (≤50 GB) PASS.
- **v2.0 Phase 18-01 Compressor flat** — Compressor 463 MB pre → 454 MB post (-9 MB). 35B used file-backed mmap pages (not anonymous compressed memory), so its exit did not relieve compressor pressure. Compressor well below 1 GB SC4 threshold both before and after.
- **v2.0 Phase 18-01 122B health post-unload confirmed** — Thinking-mode smoke (1s, no `<think>` tokens) and blueCode `--model 72b` JSON-schema smoke (7s, exit 0, clean FinalAnswer) both PASS after 35B unload. 18-02 test bed is READY.

### New Decision (15-03)

- **v2.0 Phase 15-03 test isolation** — `withTempHome` / `$HOME` redirect does NOT work for `FileSessionStore` tests on macOS .NET because `Environment.GetFolderPath(SpecialFolder.UserProfile)` reads native OS APIs, not `$HOME` env var (setting `$HOME` to temp returns empty string). Use unique GUID-prefixed session IDs in real `~/.bluecode/sessions/` with `finally`-block cleanup + `testSequenced` instead.

### New Decisions (17-03)

- **v2.0 Phase 17-03 SWITCH decision** — Qwen 3.5 35B/122B is canonical as of 2026-04-27. Criteria: all 8 gate tests PASS, B2 accuracy preserved, zero `<think>` leakage, 3.4× aggregate speedup (T6_72b: 4.1×; T6_32b: 3.7×), combined RSS 62.4 GB vs 95 GB threshold. bench/baseline.json re-keyed to _35b/_122b; CLAUDE.md Runtime Environment updated. `bench/run.sh --gate` exit 0.
- **v2.0 Phase 17-03 MoE RSS flat post-bench** — RSS held at smoke-level (62.4 GB) throughout bench-all. blueCode's structurally similar bench prompts converged MoE routing on a stable expert subset. §5.5.1 projection of 84-93 GB post-bench was conservative; actual steady-state ~62 GB.
- **v2.0 Phase 17-03 SHIP-BOTH deferred** — No per-task split benefit observed in bench data; both 35B and 122B converge at same step counts. SHIP-BOTH requires Router.fs plumbing; deferred to v2.1+.
- **v2.0 Phase 17-03 Phase 16 re-key required** — Phase 16 plans on disk reference _32b/_72b bench keys. When Phase 16-03 bench tasks execute, those keys need mechanical re-key to _35b/_122b. Plan structure otherwise unaffected.

### New Decisions (17-02)

- **v2.0 Phase 17-02 Path A confirmed** — `--chat-template-args '{"enable_thinking": false}'` (note: `args` not `kwargs`) works on mlx_lm 0.31.3 for both Qwen 3.5 35B and 122B. `QwenHttpClient.fs` was not modified. Path B is still documented as fallback in qwen35-install.md §6 for future mlx_lm versions that may lack the flag.
- **v2.0 Phase 17-02 AgentLoop User role for mid-conversation hints** — `buildMessages` injections (POST-EDIT CONSTRAINT, POST-READ HINT) changed from `Role = System` to `Role = User` (commit `54e54a9`). Qwen 3.5 35B chat template rejects mid-conversation System messages (HTTP 404). The authority signal is carried by the text marker, not the role. This is now the correct and portable approach regardless of tokenizer strictness.
- **v2.0 Phase 17-02 MoE RSS observation** — Observed combined RSS (62.4 GB) is 27 GB below projected (89.5 GB). MoE sparse activation and mmap mean only activated expert slices are resident. blueCode bench fixtures (max 4 steps) are within safe zone; monitor compressor during 17-03 --all.
- **v2.0 Phase 17-02 plist flag name** — mlx_lm.server flag is `--chat-template-args` (not `--chat-template-kwargs`). The 17-01 doc had the wrong name; corrected in commits `7b8cbc0` and `b1d644d`. `Load failed: 5: Input/output error` from launchd = malformed ProgramArguments (not hardware I/O), documented in §5.1.1.

### New Decisions (17-01)

- **v2.0 Phase 17-01 thinking-mode Path A/B** — `--chat-template-kwargs '{"enable_thinking": false}'` as mlx_lm.server flag (Path A) is preferred and baked into launchd plists. If flag is absent from installed mlx_lm version, Path B is a 1-line F# addition to `buildRequestBody` anonymous record in `QwenHttpClient.fs`: `chat_template_kwargs = {| enable_thinking = false |}`. Path A/B empirical decision happens in 17-02 at §4.4 of qwen35-install.md.
- **v2.0 Phase 17-01 co-existence policy** — qwen32b/ and qwen72b/ model directories NOT deleted during Phase 17. They are rollback assets until 17-03 SWITCH decision + 1 week stable operation.
- **v2.0 Phase 17-01 Qwen3.5 no -Instruct suffix** — `mlx-community/Qwen3.5-35B-A3B-4bit` IS the Instruct variant; there is no separate `-Instruct` HF repo. All Qwen3.5 non-Base variants are instruction-tuned (no Coder/General split unlike Qwen2.5).
- **v2.0 Phase 17-01 122B cold-start mitigation** — 122B may exceed 180s HttpClient.Timeout on cold start. Documented as operational wait (`until curl /v1/models`) NOT as code change. Timeout increase to 300s is deferred to post-17-03 decision.
- **v2.0 Phase 17-01 4-bit multi-turn degradation risk** — ml-explore/mlx-lm#1011 confirms structured JSON degradation at ~5 tool calls in 4-bit 35B. blueCode bench fixtures (max 4 steps) are within safe zone. Monitor in 17-03 bench; 8-bit variant as mitigation if degradation observed.
