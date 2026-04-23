# blueCode

## What This Is

F#으로 작성한 로컬 Qwen 기반 coding agent. Claude Code의 아키텍처는 참고하되 Qwen 특성에 맞춰 단순화한 구조 — 엄격한 JSON 출력, 최대 5루프, 최소 툴셋, 타입-중심 에러 모델. **v1.0 출시 이후 본인의 Mac 일상 코딩 도구로 `~/projs/claw-code-agent/` (Python 구현)를 대체함**.

## Core Value

Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 **안정적으로** 돌린다. v1.0 UAT 검증 완료: 로컬 Qwen이 agent 루프 안에서 예측 가능하게 동작하며, JSON 스키마 검증 + 2회 재시도 + 5-step 루프 가드가 unstable LLM 응답을 전부 타입화된 `AgentError`로 수렴시킨다.

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

### Active (v1.1)

<!-- 다음 milestone에서 닫을 항목. v1.0 UAT에서 발견된 스냅샷 기준. -->

- [ ] **OBS-03 동적 모델 id** — `QwenHttpClient.getMaxModelLenAsync` 옆에 `getModelIdAsync` 추가해 `Router.modelToName`이 서버가 리포트하는 id를 bootstrap 시 쿼리. 현재 절대 경로 하드코딩 제거 (`Router.fs:59-60`).
- [ ] **32B cold-start probe 분리** — `bootstrapAsync`에서 32B `/v1/models` 대기가 `--model 72b` 모드에서도 발생. 첫 실제 LLM 호출 전으로 lazy 화.
- [ ] **Real LLM thought 캡처 (선택)** — 현재 `Step.Thought = "[not captured in v1]"` 리터럴. `ILlmClient.CompleteAsync` 반환 타입을 `Thought * LlmOutput` 또는 `LlmStep`으로 확장 필요. `--verbose` 출력 품질을 위함.

### Out of Scope

<!-- v1 OOS 유지. v1.1도 동일 경계. -->

- **세션 영속화 / 히스토리 / 재개** — v2+. 일단은 process 수명 내에서만
- **서브에이전트 / 위임** — v2+. 단일 에이전트 루프만
- **Slash commands** (`/context`, `/compact`, `/agents`) — v2+
- **Context compaction / auto-snip** — v2+. 지금은 단순 ring-buffer + 80% 경고
- **MCP / LSP / Plugin / Hook / Remote / Worktree** — Python 버전 runtime 전체 이식 안 함
- **GUI (웹/TUI)** — CLI stdout만
- **Windows / Linux** — Mac only
- **AOT / 단일 바이너리 배포** — `dotnet run` 개발 모드만
- **Claude Code 프롬프트 직접 이식** — Qwen에서 format error 유발
- **Cross-turn memory** in multi-turn REPL — v1 스코프 밖, 각 turn은 독립 `runSession`

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
- 서비스 운영 문서: `documentations/local-llm-services.md`
- 32B 모델 교체 가이드: `documentations/qwen32b-base-to-instruct.md`

**사용자 피드백 (v1.0 UAT 기반):**
- 실제 "List files in src" 요청 → 2 step (list_dir → final), 6.8s, exit 0 — end-to-end chat 정상
- `--trace`의 POST body + response 로깅이 실전 디버깅 (chat template 문제 진단)에 결정적이었음 — 기능으로 유지
- claw-code-agent 은퇴 후 blueCode가 단독 에이전트

**참고 자료:**
- `~/projs/claw-code-agent-retired/` — Python 전체 구현 (70+ 모듈). 아키텍처 레퍼런스.
- `./localLLM/qwen_agent_rewrite.md` — "Reuse architecture, remove complexity" 원칙
- `./localLLM/qwen_claude_full_design.md` — 에이전트 루프 설계 원본
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
| v1 Minimal scope (4 툴 + 엄격 JSON + 5루프) | localLLM/ 설계 노트의 "simple → evolve" | ✓ Good — Qwen 안정성 확보 후 v1.1에서 확장 예정 |
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
| 32B Instruct 재다운 (v1.0 UAT 중 발견) | `qwen2.5-32b-mlx`가 Base Coder (FIM) 였음 | ✓ Good (post-milestone) — `documentations/qwen32b-base-to-instruct.md` 프로세스 수립 |
| Fantomas 7.0.5 로컬 도구로 repo-wide 포맷 | CI-free 운영 + 단일 사용자 통일 | ✓ Good — 35 파일 정리, isolated commit으로 feature diff와 분리 |

## v2 후보 (notional, scoping 전)

- Streaming (SSE token streaming) — 터미널 blank 경험 개선 필요 시
- 세션 영속화 / `--resume <id>` — 장기 태스크 재개 필요 시
- Tool 확장 (`edit_file`, `glob_search`, `grep_search`) — 코딩 workflow 커버리지 넓힐 시
- Slash commands (`/context`, `/compact`, `/agents`)
- LLM-aware context compaction (자동 token-aware snip)
- Sub-agents (`Agent` tool) — flat loop 50+ 세션 검증 후
- Project memory (`CLAUDE.md` discovery)

---
*Last updated: 2026-04-23 after v1.0 milestone complete*
