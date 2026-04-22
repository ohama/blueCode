# blueCode

## What This Is

F#으로 작성하는 로컬 Qwen 기반 coding agent. Claude Code의 아키텍처는 참고하되 Qwen의 특성에 맞춰 단순화한다 — 엄격한 JSON 출력, 최대 5루프, 최소 툴셋. 본인이 Mac에서 일상적으로 쓸 로컬 AI 코딩 도구로 `~/projs/claw-code-agent/` (Python 구현)를 대체한다.

## Core Value

Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 **안정적으로** 돌린다. 모든 것의 전제는 "로컬 Qwen이 루프 안에서 예측 가능하게 동작한다"는 것이다. 이게 무너지면 나머지는 의미 없다.

## Requirements

### Validated

<!-- Shipped and confirmed valuable. -->

(None yet — ship to validate)

### Active

<!-- v1 Minimal scope. 본인의 localLLM/ 설계 노트와 일치. -->

- [ ] F# 프로젝트 구조 + .NET 10 빌드/실행 (`dotnet run`)
- [ ] OpenAI-compatible chat completions 클라이언트 (localhost:8000 / localhost:8001)
- [ ] 32B/72B 직접 라우팅 — intent 분류를 Discriminated Union으로 표현, 모델 선택을 타입으로 강제
- [ ] Agent loop — 엄격한 JSON 출력 (`{thought, action, input}`), 최대 5루프, one tool per step
- [ ] 4개 기본 툴: `read_file`, `write_file`, `list_dir`, `run_shell`
- [ ] 단순 메모리 (최근 N 스텝, 기본 3)
- [ ] CLI 인터페이스 (single-turn 또는 multi-turn REPL 최소 수준)
- [ ] 관찰 가능한 step 구조(step/thought/action/input/output/status) + verbose 렌더링

### Out of Scope (v1)

<!-- v2 이후로 미룸. 지금 빌드하지 않음. -->

- **세션 영속화 / 히스토리 / 재개** — v2+. 일단은 process 수명 내에서만 상태 유지
- **서브에이전트 / 위임** — v2+. 단일 에이전트 루프만
- **Slash commands** (`/context`, `/compact`, `/agents` 등) — v2+
- **Context compaction / auto-snip** — v2+. 지금은 단순한 ring-buffer 메모리
- **MCP / LSP / Plugin / Hook / Remote / Worktree runtime** — Python 버전의 나머지 runtime들. 필요해지면 그때
- **GUI (웹 UI)** — 본인 CLI만 사용
- **Windows / Linux 지원** — Mac만
- **AOT 단일 바이너리 배포** — `dotnet run` 개발 모드만
- **Claude Code 프롬프트 이식** — "Do NOT reuse Claude prompts directly" (본인 설계 원칙)
- **Zero-dependency 철학 유지** — Python 버전과 달리 NuGet 자유롭게 사용 (.NET 관례대로)

## Context

**기존 환경 (이미 동작 확인):**
- Mac (호스트 `ohama`)에 Qwen 32B (port 8000), Qwen 72B (port 8001)이 launchd 서비스로 상시 구동
- Python FastAPI router가 port 9000에서 intent 기반 라우팅 중 (현재 VSCode CodeGPT에서 사용)
- blueCode는 router를 거치지 않고 8000/8001에 직접 붙고, 라우팅 자체를 F#으로 처리 (사용자 결정)

**참고 자료:**
- `~/projs/claw-code-agent/` — Python 전체 구현 (src/에 70+ 모듈). 아키텍처 레퍼런스로만 사용. 1:1 포팅 아님.
- `./localLLM/qwen_agent_rewrite.md` — "Reuse architecture, remove complexity" 원칙
- `./localLLM/qwen_claude_full_design.md` — 에이전트 루프 설계 (max 5 loops, strict JSON, minimal tools)
- `./localLLM/agent_32b_72b_codegpt.md` — intent 분류 및 32B/72B 라우팅 로직 (F#으로 재구현 예정)
- `./localLLM/llm_production_setup.md` — 로컬 LLM 서비스 설정

**F# 재작성의 설계 의도:**
- Python의 동적 타입 → F# Discriminated Union / Result / 불변성으로 agent 상태, 툴 시그니처, 메시지 구조 표현
- "strong type을 가능하면 최대한 사용" — 잘못된 상태 전이 / 툴 파라미터 조합이 **컴파일 에러**로 걸리는 구조 지향

## Constraints

- **Tech stack**: F# / .NET 10 — 선호 언어와 최신 런타임 사용
- **Platform**: macOS만 — 본인 장비에서만 돌리므로 크로스플랫폼 부담 제거
- **Deployment**: `dotnet run` 개발 모드 — 퍼블리시/AOT 안 함 (범위 확대 방지)
- **Model backend**: localhost Qwen 32B/72B OpenAI-compat만 — 외부 API 호출 없음, 오프라인 동작
- **LLM 출력**: 엄격한 JSON 포맷 강제 — Qwen의 불안정한 tool-call 관행 우회
- **Loop 상한**: 최대 5 iterations per turn — 무한 루프 및 컨텍스트 폭주 방지
- **Dependencies**: NuGet 자유 — `System.Text.Json`, HTTP 클라이언트, 필요시 `Spectre.Console`/`FsToolkit.ErrorHandling` 등 .NET 생태계 활용

## Key Decisions

<!-- 초기화 시점 결정사항. 프로젝트 진행하며 추가. -->

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| F# + .NET 10 | 사용자 선호 언어, 최신 런타임의 타입 시스템/성능 활용 | — Pending |
| Mac 전용, `dotnet run` 개발 모드 | 본인만 쓰므로 크로스플랫폼/AOT 배포 복잡도 제거 | — Pending |
| v1은 Minimal scope (4개 툴 + 엄격 JSON + 5루프) | 본인 `localLLM/` 설계 노트의 "simple → evolve" 전략과 일치. Qwen 안정성이 핵심이라 실제 돌려보며 확장 | — Pending |
| Python router(9000) 우회, F#이 8000/8001 직접 라우팅 | intent/모델 선택을 F# DU로 표현 → 타입 수준에서 잘못된 라우팅 방지. Python router 의존성 제거 | — Pending |
| Claude Code 프롬프트 재사용 금지, Qwen용 JSON 스키마 별도 설계 | 본인 설계 원칙(`qwen_agent_rewrite.md` §8). Qwen이 Claude 스타일 프롬프트에서 불안정 | — Pending |
| NuGet 자유 사용 (Python의 zero-deps 철학 폐기) | .NET 관례에 맞춤. `System.Text.Json` 등 표준 라이브러리 활용이 오히려 안정적 | — Pending |
| claw-code-agent는 아키텍처 레퍼런스, 1:1 포팅 아님 | 70+ 모듈 전부 옮기면 수개월. 본인이 실제 쓰는 것만 점진 포팅 | — Pending |

---
*Last updated: 2026-04-22 after initialization*
