# blueCode

## What This Is

F#으로 작성한 로컬 Qwen 기반 coding agent. Claude Code의 아키텍처는 참고하되 Qwen 특성에 맞춰 단순화한 구조 — 엄격한 JSON 출력, 최대 5루프, 최소 툴셋, 타입-중심 에러 모델. **v1.0 출시 이후 본인의 Mac 일상 코딩 도구로 `~/projs/claw-code-agent/` (Python 구현)를 대체함**.

## Current State (post-v2.2)

v2.2 Multi-file Capability shipped 2026-04-28. First **data-driven** v2.2 candidate (vs deferred-list draining) — scoped from v2.1 audit's CORR-EVAL-02 FAIL constraint discovery. Two-phase milestone: Phase 22 architectural ceiling raise (PLAN-04 5→10 across 4 source files; tests 282 → 284) + Phase 23 cold-start empirical measurement (37s warm-OS-cache; refutes v2.0's "up to 240s" estimate for common case). Final verdict **82 → 87/100, KEEP** (Performance 20→25 via cold-start +5; Correctness 31/40 stayed because CORR-EVAL-02 FAIL x2 surfaced persistent extraction bias on shared-prefix function names as v2.3 first candidate). Bench gate 7/7 PASS preserved throughout; zero `bench/baseline.json` diff. Eight milestones shipped. Daily-driver use ongoing.

**v2.1 (prior, 2026-04-28):** Empirical Qwen 3.5 122B Coding Evaluation. Single-phase observation-only milestone closing v2.0's measurement gap. `documentation/qwen35-122b-coding-eval.md` (983 lines, 10 sections); verdict 82/100 KEEP. HumanEval+ chat 0.939/0.902; schema 0/50 perfect; multi-turn N=7 stable; needle 4/4 at 32k.

**v2.0 (prior, 2026-04-27):** Persistence + Planning. Multi-turn REPL memory + JSONL session persistence + `--resume <id>` + plan-then-execute + Qwen 3.5 122B canonical + Qwen 2.5 retirement (-85 GB disk).

Detailed history: `.planning/MILESTONES.md` + `.planning/milestones/v{1.0,1.1,1.2,1.3,1.4,2.0,2.1,2.2}-ROADMAP.md`.

## Current Milestone: v2.3 Comprehension Layer

**Goal:** Resolve the persistent extraction bias on shared-prefix function names that v2.2 audit surfaced as the new bottleneck (CORR-EVAL-02 FAIL x2 with textually identical step-5 thoughts across two completely different READMEs). Multi-prong intervention: system prompt enumeration guidance + few-shot multi-file examples + plan-mode pre-flight rename-target enumeration. Verify by re-running CORR-EVAL-02 to PASS (orphan_count=0); flips Correctness 31/40 → 36/40 → Total 87 → 92.

**Why now:** Data-driven from v2.2 audit (`milestones/v2.2-MILESTONE-AUDIT.md`). v2.2 disproved the v2.1 hypothesis that the 5-step ceiling alone was the constraint; the comprehension layer is now the load-bearing layer to fix. Two README variants (902-char prose vs 2128-char enumerated rewrite) produced textually identical step-5 thoughts — README clarity cannot fix the bias. Multi-prong attack required.

**Target features (1 candidate, 3 prongs):**

- **P1: System prompt enumeration guidance** — `defaultSystemPrompt` or `planSystemPromptSuffix` updated with hint instructing the agent to enumerate ALL rename targets from spec before editing
- **P2: Few-shot multi-file examples** — Plan-mode prompt includes 1-2 inline correct multi-file refactor plan examples
- **P3: Plan-mode pre-flight rename-target enumeration** — Domain.fs Plan validator extended; new validator pass checks all rename targets from user prompt are enumerated as plan steps; new `RenameTargetsNotEnumerated` PlanInvalid reason; 2-attempt retry path

**Scope (per v2.3 scope agreement 2026-04-28):**

- 3 phases (24, 25, 26): Prompt-level (P1+P2 bundled) → Architectural (P3) → Re-eval + verdict
- ~4-5 days estimated; v2.0-style mid-size milestone but architecturally cleaner (one bottleneck, three prongs)
- bench gate stability mandatory after each phase
- Test count grows (P3 adds new validator + tests); target 284 → ~290+
- No `bench/baseline.json` modifications

**Phase numbering:** continues at 24 (v1.0: 1-5, v1.1: 6-7, v1.2: 8/9/9.1, v1.3: 10-11, v1.4: 12-13, v2.0: 14-20, v2.1: 21, v2.2: 22-23).

**Excluded** with explicit rationale (deferred to later milestones):
- IDIOMATIC-FS-01 — Coding-quality 1/5 medium-priority; may be Python-transcript artifact; needs F# task fixtures; v2.4+ candidate
- COLDSTART-PRISTINE-01 — Post-reboot pristine case untested; needs scheduled disruption window; v2.4+ candidate (low urgency)
- SLASH-01, COMPACT-01, SUBAG-01, PLAN-MODE-BENCH-01 — Carried-forward from v2.0/v2.1; observation-driven (no daily-driver pain signal); v2.4+ candidates
- THINK-01 — v2.1 data says thinking-OFF gives perfect schema 0/50; ON regresses; defer indefinitely
- TOOLCALLS-01 — Custom JSON schema is 0/50 perfect; v3.0 territory
- STM-01 — Deferred 8x; TTFT 222ms warm is already instant; defer pattern is the signal

## Future Milestone Goals (post-v2.1)

After v2.1 produces empirical evidence, observation-driven v2.2 picks 2-3 candidates from the v2.0-deferred set based on measured pain:

- **Compaction (COMPACT-01)** — PERSIST-02 saves full session JSONL; long sessions hit 80% context warning faster
- **Slash commands (SLASH-01)** — `/sessions`, `/plan`, `/clear`. UX layer over CLI flags
- **Sub-agent delegation (SUBAG-01)** — Now meaningful since memory + planning landed
- **Plan-mode bench fixture** — Deferred from Phase 16; mocked-IKeyReader pattern
- **Thinking-mode-on (THINK-01)** — Consume `<think>` blocks; `max_tokens` 1024→2048-4096
- **Native OpenAI `tool_calls` (TOOLCALLS-01)** — Replaces custom JSON schema
- **Streaming output (STM-01)** — Deferred 7th cycle

## Core Value

Mac 로컬 Qwen 3.5 122B를 strong-typed F# agent loop로 **안정적으로** 돌린다 (single-model canonical post-v2.0; switched from Qwen 2.5 32B/72B in Phase 17 SWITCH; Qwen 3.5 35B retained as cold rollback via `--with-35b`). v1.0 UAT 검증 완료: 로컬 Qwen이 agent 루프 안에서 예측 가능하게 동작하며, JSON 스키마 검증 + 2회 재시도 + 5-step 루프 가드 + (v2.0 추가) Session 기반 cross-turn memory + plan-then-execute 모드가 unstable LLM 응답을 전부 타입화된 `AgentError`로 수렴시킨다.

## Requirements

### Validated (v1.0)

<!-- Shipped and confirmed valuable. Full archive at .planning/milestones/v1.0-REQUIREMENTS.md -->

- ✓ F# / .NET 10 2-project 솔루션 + `dotnet run` — v1.0 (FND-01)
- ✓ Discriminated Union 도메인 모델 (AgentState, Intent, Model, Tool, LlmOutput, AgentError, Step, ToolResult) — v1.0 (FND-02)
- ✓ `task {}` 일관 사용 + Core에서 `async {}` CI 차단 — v1.0 (FND-03)
- ✓ `FsToolkit.ErrorHandling` `taskResult {}` 에러 체인 — v1.0 (FND-04)
- ✓ Intent 분류 + DU 기반 32B/72B 직접 라우팅 + `--model` 강제 오버라이드 — v1.0 (ROU-01..04)
- ✓ OpenAI-compatible Qwen HTTP 클라이언트 + 3단계 JSON 추출 + JsonSchema.Net 검증 + 전체 에러 매핑 — v1.0 (LLM-01..06)
- ✓ 4개 툴 (read_file, write_file, list_dir, run_shell) + 22-validator bash 보안 체인 + 2000자 truncation — v1.0 (TOOL-01..07)
- ✓ Agent loop — 5-step 상한, `(action, input_hash)` 루프 가드, 2회 JSON retry, Ctrl+C graceful, JSONL per-step — v1.0 (LOOP-01..07, OBS-01, OBS-02, OBS-04)
- ✓ CLI polish — Argu + 단일/멀티-turn REPL + `--verbose`/`--trace` + Spectre spinner + `/v1/models` 80% 경고 — v1.0 (CLI-01..07, OBS-03)
- ✓ Dynamic `/v1/models` 모델 id 조회 + lazy per-port probe + local-path preference heuristic — v1.1 (REF-01, REF-02)
- ✓ Real LLM thought 캡처 — `LlmResponse` Core 레코드로 `ILlmClient.CompleteAsync` 확장, `--verbose` 에 실제 reasoning 표시 — v1.1 (OBS-05)
- ✓ Surgical `edit_file` — exact-string find-and-replace, 0/1/N match handling — v1.2 (TLX-01, behaviorally hardened in 9.1 via directive wording + code-level `[POST-EDIT CONSTRAINT]` injection)
- ✓ Native `glob_search` — project-rooted pattern finder (replaces `run_shell find`) — v1.2 (TLX-02)
- ✓ Native `grep_search` — regex content search with ReDoS guard, structured `(path, line, content)` output (replaces `run_shell grep`) — v1.2 (TLX-03)
- ✓ `read_file` metadata header `[file:..., lines X-Y of Z, not-truncated|truncated|out-of-range]` with dispatcher default-window for partial bounds — v1.2 (TOOL-08, behaviorally completed in 9.1)
- ✓ Code-level loop-injection primitive — `lastEditPath` threaded through `runLoop`; post-user-prompt System-role message enforces tool-terminality at conversation-history layer (overrides user-prompt explicit tool naming) — v1.2 (Phase 9.1, reusable for future post-tool constraints)
- ✓ Bench harness with regression gate — `bench/run.sh --gate` (8-test, ~115s, jq-based JSON diff vs `bench/baseline.json`) + `bench/fixtures/` versioned bug fixtures + `documentation/bench.md` — v1.3 (BENCH-01..05)
- ✓ System prompt shrunk 54% (1689 → 783 chars, Path C ≤800 achieved) without regressing any gate test — v1.3 (PERF-01)
- ✓ Loop-injection extended to post-`read_file`-truncated/out-of-range — `lastReadHint: (string * string) option` parameter mirrors 09.1-05's `lastEditPath` discipline; `[POST-READ HINT]` System message fires only when relevant — v1.3 (PERF-02)
- ✓ B2 divide-by-zero diagnosis recovered on both 32B and 72B post-shrink — v1.2 audit's prompt-length attention-shift hypothesis empirically confirmed — v1.3 (PERF-03)
- ✓ Shared `BlueCode.Tests.MockHelpers` module with single canonical `makeMockResponse` — consolidates 3-milestone-old duplication; 243/1/0 preserved; zero `src/` diff — v1.4 (TST-01)
- ✓ `bench/run.sh` EXIT trap auto-resets W1/W2 write-task fixtures (`bug_lastchar.fs`, `bug_average.fs`) — `bug_divide_zero.fs` excluded; defense-in-depth with existing heredoc-restore blocks; bash 3.2 compatible, exit-code preserving — v1.4 (BENCH-06)

- ✓ Multi-turn REPL maintains conversation history within a session — `runSession priorSteps: Step list` parameter; REPL threads `Session` across turns — v2.0 (PERSIST-01)
- ✓ Session state persists to `~/.bluecode/sessions/<id>.jsonl` between turns — `version: 2` JSONL header + per-turn `TurnComplete` envelopes; coexists with v1 per-step crash log — v2.0 (PERSIST-02)
- ✓ `--resume <id>` loads prior session + continues; unknown id → exit 1 + `SessionNotFound`; corrupt JSONL → exit 1 + `SessionCorrupt` (no stack trace) — v2.0 (PERSIST-03)
- ✓ `--new-session` forces fresh id; `--resume X --new-session` rejected at startup with exit 2 + "conflicting flags" stderr — v2.0 (PERSIST-04)
- ✓ Plan DU as new `LlmOutput` variant (`Plan = { Steps: PlannedStep list; Rationale: string }`); JSON parse layer in Json.fs `llmStepSchema "plan"` + `toLlmOutput` Plan branch (4th PlanInvalid mode at parse time) — v2.0 (PLAN-01)
- ✓ `--plan` CLI flag enables plan-then-execute mode (single-turn only; `--plan --resume <id>` valid; `--plan --with-35b` exit 2; `--plan` no-prompt exit 2) — v2.0 (PLAN-02)
- ✓ User approval gate (Spectre numbered table + `IKeyReader` port + a/r/e/q dispatch); `[PLAN REJECTED]` Role=User re-prompt — v2.0 (PLAN-03)
- ✓ Plan validation: 3 structural rules in pure `validatePlan` (length≤5, unknown tool, duplicate adjacent) + schema-invalid at JSON parse layer; 2-attempt retry on either path — v2.0 (PLAN-04)
- ✓ Qwen 3.5 122B-A10B-4bit MoE single-model canonical; Qwen 2.5 32B/72B retired (-85 GB disk); 35B preserved as cold rollback via `--with-35b` opt-in — v2.0 (Phases 17-19)
- ✓ Qwen 3.5 protocol alignment: sampling params per model card (temp=0.7, top_p=0.8, top_k=20, presence_penalty=0.0); HttpClient timeout 180→300s; `extractContent` `reasoning_content` fallback; `Role = User` invariant for ALL mid-conversation injections (Phase 20-03 probe REJECT verdict) — v2.0 (Phase 20)

- ✓ Empirical Qwen 3.5 122B-A10B-4bit MoE coding evaluation — `documentation/qwen35-122b-coding-eval.md` (983 lines, 100-point scorecard); verdict **Total: 82/100, Recommendation: KEEP**. HumanEval+ chat pass@1 = 0.939 / pass@1+ = 0.902 (upper-tier OSS coding model); throughput 34.6 tok/s; TTFT 222 ms warm; schema 0/50 perfect; multi-turn coherence through N=7 (refutes mlx-lm#1011 "5 rounds" claim); long-context needle 4/4 retrieved at 32k. Bench gate 7/7 PASS post-eval; zero `src/` diff; tests 282/1/0 unchanged — v2.1 (PERF-EVAL-01..02, CORR-EVAL-01..04, REL-EVAL-01..03, DOC-EVAL-01)
- ✓ Hybrid bash + Python(venv) eval harness — `bench/eval-qwen35-122b.sh` + `bench/eval-humaneval-http.py` + `bench/eval-needle.py` (~1,115 LOC clean); HTTP-only adaptation (no in-process `mlx_lm.load()` — would OOM the launchd-managed 122B service); 9 mode flags (`--setup`/`--throughput`/`--ttft`/`--humaneval`/`--refactor`/`--langcoverage`/`--multiturn`/`--schema-rate`/`--needle`/`--coldstart` (gated)/`--full`); reproducible per §10 of eval doc — v2.1
- ✓ macOS evalplus scoring traps diagnosed and fixed (silent-failure surface) — `python -m evalplus.sanitize` pre-pass for chat-mode doubled signatures + `EVALPLUS_MAX_MEMORY_BYTES=-1` env var to skip RLIMIT_AS hard-limit crash; both baked into `run_humaneval()`. Without these the test suite returns silent pass@1=0 — v2.1 (21-02)
- ✓ Phase 21 evaluation narrative — `documentation/phase21-evaluation-narrative.md` (372 lines) — Korean-language why/what/how/result companion to the formal scorecard doc — v2.1

- ✓ PLAN-04 step ceiling raised 5→10 via config-driven seam (independent constants per Option 1) — `PlanValidator.MaxPlanSteps = 10` (Core) + `AgentConfig.MaxLoops = 10` (Cli bootstrap default); plus 4 user-visible string updates (system prompt "1-10 steps" + usage guidance, [PLAN INVALID] retry, MaxLoopsExceeded user msg, RenderingTests assertion); tests 282 → 284 (+2 boundary cases at 10/11); bench gate 7/7 PASS held; T6 baseline held first try without prompt iteration — v2.2 (PLAN-CAP-01..03)
- ✓ Cold-start empirical measurement — 37s to model-ready (PID change confirmed; warm OS file cache case; 5/5 top band ≤180s); Performance dimension 20/25 → 25/25; eval doc §3.3 flipped from "deferred per scope" to actual measurement; final scorecard `**Total: 87/100, Recommendation: KEEP**` — v2.2 (COLD-EVAL-01; Phase 23)
- ✓ Constraint discovery #2: persistent extraction bias on shared-prefix function names — CORR-EVAL-02 re-run produced FAIL twice with textually identical step-5 thoughts across two completely different READMEs (902-char prose + 2128-char enumerated rewrite). Disproves v2.1 hypothesis that ceiling alone was the constraint; surfaces comprehension layer as new bottleneck. Documented in eval doc §2.4 + §8 Caveat #6 + §9 item 8 — v2.2 (PLAN-CAP-04..05 partial-by-design per Option C)
- ✓ Third macOS bash-strict-mode pattern documented — `4bcd8a4 fix(23-01): move mkdir before tee in run_coldstart`. Pattern: under `set -euo pipefail`, any I/O command must verify its target dir exists first — v2.2 (23-01)

### Active (v2.3 Comprehension Layer)

<!-- v2.3 scope agreed 2026-04-28 from v2.2 audit's COMP-BIAS-01 first candidate. Multi-prong (P1+P2+P3) full intervention. -->

#### Comprehension intervention
- [ ] **COMP-01**: System prompt instructs agent to enumerate ALL rename targets from spec before editing (P1 prong)
- [ ] **COMP-02**: Plan-mode prompt includes 1-2 inline few-shot examples of correct multi-file refactor plans (P2 prong)
- [ ] **COMP-03**: Plan validator new pre-flight pass checks user-prompt rename targets are enumerated as plan steps; new `RenameTargetsNotEnumerated` PlanInvalid reason; 2-attempt retry (P3 prong)
- [ ] **COMP-04**: Tests + bench gate regression hold (PlanValidator new tests; AgentLoop new tests; 7/7 PASS held; per-fixture step counts unchanged for W1/W2/B2/T1/T5/T6/MT)

#### Re-evaluation
- [ ] **COMP-05**: CORR-EVAL-02 re-run produces orphan_count=0 PASS (`bench/eval-qwen35-122b.sh --refactor`)
- [ ] **COMP-06**: Eval doc updated; scorecard re-aggregated (Correctness 31/40 → 36/40; Total 87 → 92; final line `**Total: 92/100, Recommendation: KEEP**`)

### Deferred (v1.5+ candidates — scope from observation window, not backlog)

- Streaming output (STM-01) — deferred 5x; revisit only if 2-week observation surfaces complaint
- Session persistence + `--resume` (SES-01) — v2+ per scope
- Auto-escalation on MaxLoopsExceeded (ROU-05) — less urgent post-9.1
- Ctrl+C UX polish (CLI-08) — minor
- Per-port `MaxModelLen` visibility (OBS-06) — minor
- Prompt cache hygiene / launchd kickstart (OPS-01) — zero kickstarts in v1.3
- Multi-platform `tryParseModelId` — Windows OOS so likely permanent

### Out of Scope

<!-- v1 OOS 유지. v1.1도 동일 경계. -->

- **세션 영속화 / 히스토리 / 재개** — ✓ Resolved by v2.0 (PERSIST-01..04). FileSessionStore + `--resume <id>` + `--new-session` shipped.
- **Cross-turn memory** in multi-turn REPL — ✓ Resolved by v2.0 (PERSIST-01). `runSession priorSteps: Step list` threads Session across turns.
- **서브에이전트 / 위임** — Now meaningful for v2.1+ — memory + planning landed in v2.0; sub-agents are the natural next architectural layer.
- **Slash commands** (`/sessions`, `/plan`, `/clear`) — v2.1+ candidate. UX layer over the CLI flags shipped in v2.0.
- **Context compaction / auto-snip** — v2.1+ candidate. Natural follow-up to PERSIST-02; long sessions hit 80% context warning faster without compaction.
- **MCP / LSP / Plugin / Hook / Remote / Worktree** — Permanent OOS. Local-only ethos preserved across v1.x and v2.0.
- **GUI (웹/TUI)** — CLI stdout만
- **Windows / Linux** — Mac only
- **AOT / 단일 바이너리 배포** — `dotnet run` 개발 모드만
- **Claude Code 프롬프트 직접 이식** — Qwen에서 format error 유발

## Context

**Current codebase (v1.0 shipped):**
- 5,891 LOC F# (src + tests)
- 2-project: `BlueCode.Core` (pure domain + routing + agent loop) + `BlueCode.Cli` (all adapters, Argu, Serilog, Spectre, JSONL sink)
- 208 tests passing, 1 env-gated smoke ignored
- Fantomas 7.0.5 formatted (local tool, `.config/dotnet-tools.json`)
- Git: master 기준 85 commits, tag `milestone-v1.0` (v1.0 완료 시)

**Runtime environment (Mac ohama, 검증됨):**
- Qwen 32B Instruct (Coder) @ `localhost:8000` via `mlx_lm.server` + launchd (`com.ohama.qwen32b.plist`)
- Qwen 72B Instruct (AWQ 4-bit) @ `localhost:8001` 동일 패턴
- 모델 경로: `~/llm-system/models/qwen{32b,72b}/`
- 서비스 운영 문서: `documentation/local-llm-services.md`
- 32B 모델 교체 가이드: `documentation/qwen32b-base-to-instruct.md`

**사용자 피드백 (v1.0 UAT 기반):**
- 실제 "List files in src" 요청 → 2 step (list_dir → final), 6.8s, exit 0 — end-to-end chat 정상
- `--trace`의 POST body + response 로깅이 실전 디버깅 (chat template 문제 진단)에 결정적이었음 — 기능으로 유지
- claw-code-agent 은퇴 후 blueCode가 단독 에이전트

**참고 자료:**
- `~/projs/claw-code-agent-retired/` — Python 전체 구현 (70+ 모듈). 아키텍처 레퍼런스.
- ~~`./localLLM/qwen_agent_rewrite.md`~~ — "Reuse architecture, remove complexity" 원칙 _(directory removed 2026-04-28 post-v2.2; historical design notes from v1.0 milestone era)_
- ~~`./localLLM/qwen_claude_full_design.md`~~ — 에이전트 루프 설계 원본 _(removed 2026-04-28; see above)_
- `documentation/howto/` — 이번 milestone 세션 learnings (Base vs Instruct 판별, 로컬 LLM 서버 디버깅, Expecto Console 충돌)

**Known issues / technical debt (v1.1 target):**
- `Router.modelToName` 절대경로 하드코딩 (서버 가동 경로 변경 시 깨짐)
- 32B cold-start시 `--model 72b` 모드에서도 `/v1/models` timeout WARN 발생
- `Step.Thought` placeholder `"[not captured in v1]"` — verbose 모드 출력 품질 저하

## Constraints

- **Tech stack**: F# / .NET 10
- **Platform**: macOS only (Mac ohama 전용)
- **Deployment**: `dotnet run` 개발 모드 (AOT 안 함)
- **Model backend**: localhost Qwen 32B/72B OpenAI-compat (`mlx_lm.server` 기준)
- **LLM 출력**: 엄격한 JSON 포맷 강제 (Qwen tool-call instability 우회)
- **Loop 상한**: 최대 5 iterations per turn
- **Dependencies**: NuGet 자유 — Argu 6.2.5, FSharp.SystemTextJson 1.4.36, FsToolkit.ErrorHandling 5.2.0, JsonSchema.Net 9.2.0, Spectre.Console 0.55.2, Serilog 4.3.1 (+ Sinks.Console 6.1.1)
- **Core purity**: `BlueCode.Core`는 Serilog/Spectre/Argu 참조 금지 (ports-and-adapters 불변)
- **Stream separation**: Serilog → stderr, printfn/Spectre → stdout

## Key Decisions

<!-- v1.0 milestone 끝나면서 outcome 채움. v1.1 이후 새 결정은 추가. -->

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| F# + .NET 10 | 사용자 선호 언어, 최신 타입 시스템 | ✓ Good — DU/Result/task가 agent 상태를 타입 수준에서 완전히 표현. 1일 내 v1.0 출시 |
| Mac 전용, `dotnet run` | 크로스플랫폼/AOT 배포 복잡도 제거 | ✓ Good — 범위 관리 효과적, UAT 시점에 platform 문제 없음 |
| v1 Minimal scope (4 툴 + 엄격 JSON + 5루프) | localLLM/ 설계 노트의 "simple → evolve" _(localLLM/ removed 2026-04-28 post-v2.2)_ | ✓ Good — Qwen 안정성 확보 후 v1.1에서 확장 예정 |
| Python router(9000) 우회 직접 라우팅 | Intent/모델 선택을 F# DU로 표현 | ✓ Good — 타입 수준 정확성 + Python 의존 제거 |
| Claude 프롬프트 재사용 금지 | Qwen format error 회피 | ✓ Good — JSON 스키마 설계가 안정적 결과 생성 |
| NuGet 자유 사용 | .NET 관례 | ✓ Good — 표준 라이브러리 활용이 안정적 |
| claw-code-agent 아키텍처 레퍼런스, 1:1 포팅 아님 | 70+ 모듈 스코프 관리 | ✓ Good — 22 validator만 선택 포팅, 나머지는 필요 시 |
| Ports-and-adapters (Core는 Serilog/Spectre 미의존) | 테스트/재사용성 | ✓ Good — Phase 5에서 Core 변경 1필드(ForcedModel)만으로 전 기능 확장 |
| `task {}` only in Core (async {} 금지) | HttpClient/Process 호환 + CE 단순화 | ✓ Good — CI 스크립트로 자동 검증, 예외 없이 통과 |
| FSharp.SystemTextJson `WithUnionUnwrapFieldlessTags(true)` | Qwen이 `"System"` 같은 bare-string 요구 | ✓ Good — tool/intent 이름이 JSON에 자연스럽게 |
| Phase 1에서 ToolResult DU shape 선정의 (TOOL-07 분할) | 1-2-3 phase exhaustive-match 증명 가능 | ✓ Good — Phase 1 SC-2 compile-error 증명 확보 |
| Expecto 명시적 `rootTests` 리스트 (auto-discovery 미사용) | 프로젝트 관례 | ⚠ Revisit — 4명의 executor가 동일 함정을 밟음. v1.1에서 `[<Tests>]` auto-discovery 전환 검토 |
| Spinner `withSpinner`가 HTTP call만 감싸고 onStep은 감싸지 않음 | stream 분리 + stdout 경합 회피 | ✓ Good — `--verbose` 다줄 출력과 공존 |
| `Router.modelToName` 로컬 절대경로 하드코딩 (v1.0 UAT hotfix) | `mlx_lm.server`가 HF id로 해석 404 반환 | ⚠ Revisit — v1.1 OBS-03 동적 쿼리가 적절 |
| `Step.Thought = "[not captured in v1]"` placeholder | `ILlmClient.CompleteAsync` 시그니처 확장 Phase 4 scope 넘음 | ⚠ Revisit — v1.1에서 `--verbose` 품질 관점 재평가 |
| 32B Instruct 재다운 (v1.0 UAT 중 발견) | `qwen2.5-32b-mlx`가 Base Coder (FIM) 였음 | ✓ Good (post-milestone) — `documentation/qwen32b-base-to-instruct.md` 프로세스 수립 |
| Fantomas 7.0.5 로컬 도구로 repo-wide 포맷 | CI-free 운영 + 단일 사용자 통일 | ✓ Good — 35 파일 정리, isolated commit으로 feature diff와 분리 |
| v1.1: Option B (Core에서 modelToName 삭제, adapter가 wire id 소유) | 기존 `AgentConfig.ForcedModel` precedent 동일 패턴 | ✓ Good — Core purity 유지, 06-03 gap closure 로 `StartsWith('/')` heuristic 추가 |
| v1.1: Option C (new Core record `LlmResponse`) | 대안 A/B (LlmStep/tuple)보다 named 필드 + Core 포함 안전 | ✓ Good — F# big-bang 컴파일 캐스케이드로 단일 atomic commit 가능 |
| v1.1: `tryParseModelId` local-path preference heuristic | mlx_lm.server의 HF Hub fallback 이 Instruct tokenizer 를 Base 로 덮어쓰는 regression 우회 | ✓ Good — live 검증 통과. 단점: Windows 지원 시 path 감지 로직 재설계 필요 (v1 Mac-only라 무관) |
| v1.1: `makeMockResponse` 테스트 헬퍼 중복 (shared 모듈 아님) | scope 관리; shared 모듈 추출은 별도 test infra 작업 | ✓ Resolved by v1.4 (TST-01) — `tests/BlueCode.Tests/MockHelpers.fs` is single canonical home |
| v1.2: 8-action schema enum + executor stubs in same plan (08-01 shared seam) | DU/schema/dispatcher coupling; shared seam pattern | ✓ Good — single atomic commit landed Domain + Cli + system-prompt changes coherently |
| v1.2: TOOL-08 metadata header preserves RAW endLine (no clamp) | unambiguous bounds-violation signal to LLM | ✓ Good — 32B self-corrects on out-of-range header |
| v1.2: read_file header words anchor with `\n` in test substring assertions | `truncated` contains `a`, `lines` contains `e` — collision with body text | ✓ Good — pattern generalizable for any tool prepending fixed-format headers |
| v1.2: `dotnet test` documented as NOT running Expecto in this project | 4-executor pitfall; explicit `rootTests` + `[<EntryPoint>]` pattern | ✓ Good — STATE decisions log; canonical runner is `dotnet run --project tests/...` |
| v1.2 (9.1-04 discovery): User-prompt explicit tool naming overrides system-prompt directive wording | bench fixture "using write_file" exposed wording-only intervention class limit | ✓ Good — drove 9.1-05 to code-level enforcement, more robust |
| v1.2 (9.1-05): Loop-injection primitive — `lastEditPath` threaded through `runLoop`; post-user `[POST-EDIT CONSTRAINT]` System-role message overrides user-prompt priority via conversation-history position | Reusable mechanism for tool-terminality enforcement at a layer below LLM's view of "user said X, system said Y" | ✓ Good — closes W1 (`4→3 steps`); reusable primitive for future post-tool constraints; v1.3 PERF-02 extended pattern to `lastReadHint` |
| v1.2: Mid-milestone audit (`/gsd:audit-milestone`) caught structural-vs-behavioral gap | Audit checked spec contract (intact), missed behavioral effectiveness; live re-bench was the truth source | ✓ Good (resolved by v1.3 BENCH-04) — `bench/run.sh --gate` is now the structural answer; phase verifiers can rely on gate exit codes for behavioral verification |
| v1.3 (BENCH-04): jq-based 3-branch verdict with `is_regression` whitelist as first branch | Plan-checker iteration 1/3 caught a fourth-branch "regression recovery" detector that would have fired on every B2 run, breaking SC1 | ✓ Good — clean, testable; whitelist enables shipping with known regressions tracked rather than hidden |
| v1.3 (PERF-01): Path C target with Path D escape hatch | Aggressive shrink (≤800) needed pre-defined fallback (≤1000 with rationale) to avoid infinite iteration trap | ✓ Good — Path C achieved at 783; escape hatch unused but discipline prevented over-iteration |
| v1.3 (PERF-02): `lastReadHint: (string * string) option` mirrors `lastEditPath` discipline | Function parameter rather than Domain.fs record field; same single-iteration lifecycle | ✓ Good — Domain.fs untouched across both extensions; pattern reusable for any future post-tool injection (post-`write_file` redundancy guard, etc.) |
| v1.3 (PERF-03): Audit hypothesis empirically confirmed | 54% prompt reduction recovered correct B2 diagnosis on both models without surgical hint | ✓ Good — validates "ship-from-pain" discipline; future debugging starts with prompt-length sanity check |
| v1.3: 2 Rule 3 auto-fixes during PERF-01 iteration (`edit_file` empty `old_string` infinite loop, `grep_search` file-path support) | Bench-blockers surfaced during prompt-shrink iteration; fixed under workflow's auto-fix-blockers discipline | ✓ Good — no silent bug accumulation; Rule 3 worked as intended |
| v1.3: Howto pattern — capture v1.2/v1.3 reusable lessons (5 howtos) | Knowledge that previously lived in milestone-archive SUMMARYs (effectively buried) now in discoverable docs | — Pending — value depends on whether future sessions actually consult them; revisit after v1.5 |
| v1.4 (TST-01): Shared MockHelpers.fs single combined commit | 4 mechanically-coupled file edits with no valid intermediate build state; CLAUDE.md permits atomic-per-task (not per-file) | ✓ Good — refactor commit cleaner than 4 broken-build states; 15 call sites resolved through `open` without source change |
| v1.4 (TST-01): Scope discipline — only `makeMockResponse` factored | 3 prior milestones deferred TST-01 partly due to scope creep ("while I'm here, factor X too"); v1.4's discipline is the load-bearing closure mechanism | ✓ Good — `toolCall`, `mockLlm`/`stubLlm`, `mockToolsOk`/`stubToolsOk`, `discardStep` left duplicated by design; future test infra pass may revisit |
| v1.4 (TST-01): REQUIREMENTS.md count discrepancy discovered | Spec said "3 instances" but actual was 2 definitions (one per file); spec conflated definition sites with use sites | — Note — no scope impact; correction documented in v1.4 archive. Lesson: count code locations, not natural-language references, when defining cleanup scope |
| v1.4 (BENCH-06): EXIT trap with defense-in-depth | Existing heredoc-restore blocks preserved unchanged; trap is exit-time safety net, heredoc is between-invocation reset; either alone sufficient; together they cover every failure mode | ✓ Good — bash 3.2 compatible, exit-code preserving (no `exit N` in trap body); `bug_divide_zero.fs` deliberately excluded as B2 read-only diagnose fixture |
| v1.4: Path B chosen over A (streaming) and C (observation-only) | Middle path threads "discipline preserved" with "small wins shipped"; STM-01 deferred 6th cycle; observation window is load-bearing | ✓ Good — both REQs closed in ~1 day with zero `src/` diff; v1.5 scoping will come from observation-window `/gsd:add-todo` entries, not deferred-list draining |
| v2.0: Bundle persistence + planning together | Memory without planning gives long-context drift; planning without memory gives brilliant single-turn agents that forget yesterday | ✓ Good — Phase 16 SC4 (`--plan --resume <id>`) verifies the bundle works end-to-end. Both architectural investments delivered atomically across 7 phases |
| v2.0 Phase 14: Atomic Domain shift | Single Phase 14 commit captured all v2.0 type-level work (Session, Plan, ISessionStore, AgentError variants); F# big-bang compile cascade pattern (mirrors v1.1 LlmResponse) | ✓ Good — Phase 14 verified passed 5/5; Phases 15-16 worked purely at Cli layer without revisiting Core |
| v2.0 Phase 14: Validator scope (3 structural rules in Core; schema-invalid in Cli parse layer) | JsonSchema.Net lives in BlueCode.Cli/Adapters/Json.fs (Cli-side); Core purity preserved by deferring schema validation to parse time | ✓ Good — ROADMAP SC3/SC4 corrected mid-verification to match implementation; PlanValidator handles 3 rules; Phase 16 wires the 4th at JSON parse |
| v2.0 Phase 17 SWITCH: Qwen 3.5 35B/122B replaces 32B/72B | 3.4× speedup (T6_72b: 4.1×; T6_32b: 3.7×); zero `<think>` leakage; B2 accuracy preserved; combined RSS 62.4 GB vs 95 GB threshold; all 8 gate tests PASS | ✓ Good — daily-driver perf jump material; bench gate empirically confirmed canonical replacement viable |
| v2.0 Phase 17-02 critical discovery: Qwen 3.5 chat template REJECTS mid-conversation Role=System (HTTP 404) | mlx_lm.server enforces "System message must be at the beginning"; Phase 17-02 flipped POST-EDIT CONSTRAINT + POST-READ HINT to Role=User; Phase 20-03 re-probed 122B alone and confirmed REJECT | ✓ Good (load-bearing) — invariant documented in `scripts/probe-system-role.sh` + 3-line comments at AgentLoop.fs:249/260/266; applies to Phase 16's [PLAN REJECTED] re-prompt too |
| v2.0 Phase 18 DROP-35B: 122B alone is canonical | All 5 SC4 criteria PASS (T1/T2 median 3s; T6/W1/W2/B2 step counts within baseline_max; PhysMem +19.42 GB freed; Compressor 454 MB; B2 DivByZero preserved); MoE expert routing converges on stable subset under bench load (RSS flat +1.4 MB) | ✓ Good — single-model viability empirically proven; -17 GB RSS + simpler operational surface |
| v2.0 Phase 19: Breaking CLI changes (32b/72b → exit 2; --with-35b opt-in flag) | Single-model default predictable; 35B reactivation requires explicit opt-in (launchctl load + --with-35b) so service-load alone doesn't change blueCode behavior | ✓ Good — `bench/run.sh` absorbed `scripts/bench-122b-only.sh`; baseline halved 8→6; -85 GB disk reclaimed; 4 new ModelsProbeTests for `validateModelPath` PathRetired |
| v2.0 Phase 19: data[0] HF Hub fallback gotcha | mlx_lm.server returns hardcoded `Qwen/Qwen2.5-Coder-32B` in data[0] regardless of loaded model; data[1] returns actual local path | ✓ Good — verification scripts must use `data[1]`; mirrors `tryParseModelId` path-preference heuristic (CLAUDE.md §Key Seams); worth a future `/howto` entry |
| v2.0 Phase 20-03 probe-driven Role=User decision | Live curl to 122B (port 8001) with 3-message system/user/system POST returned HTTP 404; AgentLoop.fs Role stays User | ✓ Good — empirical evidence preserved at `20-03-PROBE-OUTPUT.md`; in-code 3-line comments cite probe date + HTTP code; howto F# snippets synced |
| v2.0 Phase 16 replan-from-scratch | Original Phase 16 plans (pre-Phase-17) assumed 32B/72B dual-model + 8-entry baseline + MT_32b/MT_72b fixtures; after Phases 17-20 shifted premises, surgical re-key would leave subtle drift; replan was cleaner | ✓ Good — stale `.stale` siblings preserved as forensic reference; new plans single-model + Role=User aware from start |
| v2.0 Phase 16: Plan-mode bench fixture DEFERRED to v2.1+ | Console.ReadKey-based UX intractable for autonomous regression gate; PlanParseTests + PlanGateTests + AgentLoopTests substitute via mocked IKeyReader | ✓ Good — single MT_122b fixture covers PERSIST-01 end-to-end; bench gate 7/7 PASS final |
| v2.0 Phase 16: IKeyReader port abstraction | `Console.ReadKey(intercept=true)` real impl + scripted reader for tests; gained stdin-redirect fallback in 16-02 for piped smoke tests | ✓ Good — testable keystroke dispatch without interactive harness; deviations auto-fixed during execution |
| v2.0 Phase 16: planSystemPromptSuffix `OVERRIDE — PLAN MODE ACTIVE` preamble | Base system prompt's tool-call enum caused 122B to ignore gentle `[PLAN MODE]` prefix entirely; OVERRIDE preamble forces compliance | ✓ Good — auto-fixed during 16-02 execution per Rule 3 deviation handling |
| v2.0 mid-milestone scope expansion (3 phases → 7 via /gsd:add-phase) | Phase 17 added evaluation; Phase 18 single-model viability; Phase 19 retirement; Phase 20 protocol alignment — all sequenced BEFORE Phase 16 because bench fixtures needed canonical model pair settled | ✓ Good — milestone ships clean despite scope churn; 8/8 reqs closed; 6/6 E2E flows verified in audit; documentation drift acceptable |
| v2.0 Phase 20 missing formal 20-VERIFICATION.md | Process gap, not code gap; per-plan SUMMARYs + bench gate 6/6 PASS post each + Phase 16 dependency on Phase 20-03 Role=User invariant serve as substitute verification | ⚠ Process drift — flagged in audit, non-blocking. Future phases should not skip the verifier step regardless of yolo mode |
| v2.1: Formalize Qwen 3.5 122B coding eval as milestone (not ad-hoc tooling) | 5-task scope, ~400 lines of harness code + ~600-line verdict doc, multi-dimensional measurement → warrants proper GSD framing (REQUIREMENTS, phase verifier, atomic commits per task) | ✓ Good — milestone shipped clean with **82/100 KEEP** verdict; verification 15/15 truths verified; audit passed 10/10 requirements + 5/5 plan-integration edges; ~25 atomic commits; bench gate 7/7 PASS preserved |
| v2.1: Hybrid bash + Python(venv) approach | Pure-bash for performance/reliability/refactoring (reuses `bench/run.sh` patterns); Python(venv) only for HumanEval+ scoring (`evalplus` library is non-negotiable) and long-context needle (mlx-runner template adapted to HTTP). mlx-runner's in-process `mlx_lm.load()` would OOM the launchd-managed 122B service | ✓ Good — HTTP-only constraint preserved; `grep -E "import mlx_lm" bench/eval-*.py` empty; no second 122B instance loaded at any point during eval; harness ~1,115 LOC clean (excl. venv) |
| v2.1: HumanEval+ both modes (chat + completion) | Mode A (chat) is "useful for blueCode" headline; Mode B (completion) is direct compare-to-published. Reporting both surfaces chat-wrapping artifacts (explanation+code instead of raw code) | ✓ Good — chat 0.939/0.902 is the headline (matches blueCode runtime); completion 0.226/0.213 informational; chat-mode gap was the trigger that surfaced the macOS evalplus.sanitize requirement (otherwise silent pass@1=0) |
| v2.1: 100-point scorecard verdict (Correctness 40 / Performance 25 / Reliability 25 / Coding-quality 10) | Coarse PASS/FAIL hides which dimension is weak; numeric scorecard with explicit thresholds gives actionable specificity. KEEP ≥80; KEEP-WITH-CAVEATS 60-79 OR any dimension <60%; ESCALATE <60 OR multi-turn degrades before turn 5 OR HumanEval+ <30% | ✓ Good — 82/100 lands cleanly in KEEP band; per-dimension percentages (77.5/80/100/60) surface the Coding-quality dimension at exact threshold for caveat documentation; arithmetic verified by audit (31+20+25+6=82) |
| v2.1: Cloud comparison (Claude/GPT-4) explicit non-goal | Requires API key + cost; user has muscle memory from daily use; preserves reproducibility without external dependencies; documented in eval doc §6.3 as deliberate boundary | ✓ Good — boundary held; eval doc §6.3 documents 4 explicit reasons (API key/cost, network variance noise, scope drift, user muscle memory baseline); no API calls made during eval |
| v2.1 (21-02): macOS evalplus silent-failure traps require sanitize + RLIMIT_AS env override | Chat-mode answers are full function defs → `evalplus.evaluate` stitches prompt+completion → doubled signature → silent pass@1=0. `RLIMIT_AS` 4 GiB exceeds macOS per-process hard limit → every test subprocess crashes pre-execution with `ValueError`. | ✓ Good — `python -m evalplus.sanitize` pre-pass + `EVALPLUS_MAX_MEMORY_BYTES=-1` env var both baked into `run_humaneval()`; without these the entire HumanEval+ verdict would have been wrong. Documented in 21-02 SUMMARY for future macOS evalplus users |
| v2.1 (21-03): 5-step PLAN-04 ceiling = constraint discovery via CORR-EVAL-02 FAIL | Multi-file refactor measured FAIL (orphan_count=1). Agent read all 4 fixture files coherently then exhausted 5-step budget on first edit. Genuine multi-file refactor needs 7+ steps. | ⚠ Revisit — first v2.2 candidate, **data-driven** from this eval (not from deferred-list backlog). Eval doc §9 lists "if 5-step cap is raised, re-run CORR-EVAL-02" as re-evaluation trigger. Needs Core change (Domain.fs Plan validator), not eval-harness fix |
| v2.1 (21-04): macOS BSD `seq 2 1` countdown + `set -euo pipefail`/`grep -c` interaction patterns | Three bash-strict-mode + macOS deviations auto-fixed during execution: set-e/dotnet exit, grep-c pipefail double-output, BSD seq countdown. Each silently corrupted measurement until traced to root cause. | ✓ Good — patterns documented in plan SUMMARYs (commits); future bash handler authors should bracket `dotnet run` with `set +e`/`set -e`, use `\|\| true` (not `\|\| echo 0`) under pipefail, and guard BSD `seq M N` with `[ M -le N ] && seq M N \|\| true` |
| v2.2: First data-driven candidate scoping (vs deferred-list draining) | CORR-EVAL-02 FAIL was empirically measured in v2.1; v2.2 scoped from that signal rather than picking from speculative deferred candidates (compaction, slash, sub-agents). Hypothesized 5-step ceiling = sole constraint. | ⚠ Revisit — hypothesis was partially correct (ceiling was necessary) but insufficient (CORR-EVAL-02 still FAIL post-ceiling-raise). Constraint discovery #2 (persistent extraction bias) surfaced as v2.3 first candidate. Discipline preserved: data → next data, no speculation |
| v2.2 (Option 1 design): independent constants (PlanValidator.MaxPlanSteps + AgentConfig.MaxLoops) | PlanValidator runs at JSON parse time without AgentConfig in scope (Phase 16 design invariant); merging into shared module would require new file + import discipline | ✓ Good — surgical change; no new module; PlanValidator.fs:36-40 docstring updated to "default 10"; both sites stay in sync via grep verification |
| v2.2 (Option C): accept CORR-EVAL-02 partial closure + document persistent extraction bias as v2.3 candidate | After README rewrite (Option A) attempt failed identically, two consecutive FAILs with completely different prose textually identical step-5 thoughts is decisive empirical signal. Symptom (README ambiguity) is not the disease (model bias). | ✓ Good — intellectually honest; v2.2 verdict went UP via Phase 23 cold-start path (different mechanism, same numeric end state 87/100); v2.3 has cleanly-scoped first candidate with multi-prong intervention space (system prompt + few-shot + plan-mode pre-flight) |
| v2.2 (23-01): cold-start handler had `tee` before `mkdir` under `set -euo pipefail` | First `--coldstart` invocation aborted silently before kickstart fired (tee failed → pipefail abort). Service was NOT killed; no actual disruption. | ✓ Good — fixed in commit; third macOS bash-strict-mode pattern documented in 23-01 SUMMARY for future bash handler authors. Lesson: under `set -euo pipefail`, any I/O command must verify its target dir exists first |
| v2.2: 37s warm-OS-cache cold-start refutes v2.0's "up to 240s" estimate for common case | Empirical measurement is ~6× faster than v2.0 SUMMARY's ceiling estimate. Likely cause: kernel preserves model weight pages even when launchctl kickstart kills the process. | ✓ Good — measurement preserved as authoritative for warm-cache case; v2.0 estimate stays valid for first-boot pristine case (untested in v2.2; v2.3 candidate COLDSTART-PRISTINE-01) |

## v2 후보 (notional, scoping 전)

- Streaming (SSE token streaming) — 터미널 blank 경험 개선 필요 시
- 세션 영속화 / `--resume <id>` — 장기 태스크 재개 필요 시
- Tool 확장 (`edit_file`, `glob_search`, `grep_search`) — 코딩 workflow 커버리지 넓힐 시
- Slash commands (`/context`, `/compact`, `/agents`)
- LLM-aware context compaction (자동 token-aware snip)
- Sub-agents (`Agent` tool) — flat loop 50+ 세션 검증 후
- Project memory (`CLAUDE.md` discovery)

---
*Last updated: 2026-04-28 after starting v2.3 Comprehension Layer milestone — multi-prong (P1+P2+P3) intervention on persistent extraction bias surfaced by v2.2 audit. Target: CORR-EVAL-02 PASS → Total 87 → 92/100. 8 milestones shipped (v2.3 in progress).*
