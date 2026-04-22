# Requirements: blueCode

**Defined:** 2026-04-22
**Core Value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다

## v1 Requirements

v1 Minimal scope. 본인 `localLLM/` 설계 노트 + 연구 SUMMARY.md의 Phase 1 must-haves 기반.

### Foundation

- [ ] **FND-01**: F# / .NET 10 솔루션이 `dotnet run`으로 빌드·실행된다 (BlueCode.Core + BlueCode.Cli 2-project)
- [ ] **FND-02**: 핵심 도메인 타입이 Discriminated Union으로 정의된다 (`AgentState`, `Intent`, `Model`, `Tool`, `LlmOutput`, `AgentError`, `Step`)
- [ ] **FND-03**: `task {}` CE 일관 사용 정책이 적용된다 (async {} 금지, HttpClient/Process/IO 호환)
- [ ] **FND-04**: `FsToolkit.ErrorHandling`의 `taskResult {}` CE로 에이전트 루프 에러 흐름이 표현된다

### LLM Client

- [ ] **LLM-01**: OpenAI-compatible 엔드포인트(`localhost:8000`/`localhost:8001`)로 chat completion POST 가능
- [ ] **LLM-02**: 응답 JSON을 추출 파이프라인(bare → brace-nest → fence-strip → ParseFailure)으로 파싱
- [ ] **LLM-03**: `JsonSchema.Net`으로 LLM 출력 `{thought, action, input}` 스키마를 런타임 검증
- [ ] **LLM-04**: `FSharp.SystemTextJson`의 `JsonFSharpConverter`가 옵션에 등록되고 DU 직렬화가 정상 동작
- [ ] **LLM-05**: 32B는 temperature=0.2, 72B는 temperature=0.4로 하드코딩 (사용자 노출 금지)
- [ ] **LLM-06**: HTTP 실패는 `AgentError.LlmUnreachable`로 매핑됨 (예외 누수 없음)

### Model Routing

- [ ] **ROU-01**: `classifyIntent: string -> Intent` 순함수로 사용자 입력에서 Intent 분류 (`Debug | Design | Analysis | Implementation | General`)
- [ ] **ROU-02**: `intentToModel: Intent -> Model` 순함수로 Intent를 모델 선택으로 변환 (Debug/Design/Analysis → 72B, 나머지 → 32B)
- [ ] **ROU-03**: `Model`을 엔드포인트로 변환해 LLM 클라이언트 호출 (32B → 8000, 72B → 8001)
- [ ] **ROU-04**: 사용자가 `--model 72b` / `--model 32b` 플래그로 라우팅을 강제 오버라이드 가능

### Tools

- [ ] **TOOL-01**: `read_file` — 경로 받아 내용 반환. 옵션으로 line range 지원
- [ ] **TOOL-02**: `write_file` — 경로 받아 전체 내용 덮어쓰기. 프로젝트 루트 밖 경로는 거부
- [ ] **TOOL-03**: `list_dir` — 디렉토리 내용 나열. 비재귀 기본, depth-limit 옵션
- [ ] **TOOL-04**: `run_shell` — 쉘 명령 실행. 30s 타임아웃, stdout 100KB cap, stderr 10KB cap
- [ ] **TOOL-05**: `run_shell` 보안 검증기 체인 — `claw-code-agent/src/bash_security.py` 포팅 (command substitution, IFS injection, fork bomb, destructive 패턴, redirect chain 차단)
- [ ] **TOOL-06**: 모든 툴 출력은 메시지 히스토리에 추가되기 전 2000자에서 절단 (context overflow 방지)
- [ ] **TOOL-07**: `ToolResult` DU로 결과 표현 (`Success | Failure | SecurityDenied | PathEscapeBlocked | Timeout`) — 누구나 컴파일 시점에 모든 케이스 처리 강제

### Agent Loop

- [ ] **LOOP-01**: 단일 turn에서 prompt → LLM → tool → observe 사이클 최대 5회 반복 후 종료
- [ ] **LOOP-02**: `MaxLoopsExceeded`는 `AgentError`의 한 케이스로 표현 (예외 아님), 호출자가 컴파일 시점에 처리 강제
- [ ] **LOOP-03**: 한 step당 정확히 하나의 툴만 실행 (chaining 금지) — JSON 스키마가 단일 `action` 필드만 허용
- [ ] **LOOP-04**: 동일 `(action, input_hash)` 튜플이 한 turn에 3회 이상 나오면 루프 가드가 차단
- [ ] **LOOP-05**: JSON 파싱 실패 시 최대 2회 재시도 후 `AgentError.InvalidJsonOutput`으로 종료
- [ ] **LOOP-06**: 메시지 히스토리는 불변 ring buffer로 최근 N개(기본 3) step 유지
- [ ] **LOOP-07**: Ctrl+C(취소) 시 stack trace 없이 step 요약으로 graceful 종료

### CLI

- [ ] **CLI-01**: 단일 호출 모드 — `blueCode "<prompt>"` 형태로 실행, 최종 응답 stdout 출력
- [ ] **CLI-02**: 다중 turn REPL 모드 — 인자 없이 실행 시 대화형 루프, `/exit` 또는 Ctrl+D로 종료
- [ ] **CLI-03**: `--verbose` 플래그로 각 step의 thought/action/input/output/status 전체 출력 (기본은 compact)
- [ ] **CLI-04**: Compact 모드는 step당 한 줄 요약 출력 (`> reading file...`, `> editing code...`)
- [ ] **CLI-05**: LLM 추론 대기 중 Spectre.Console spinner + 경과 시간 표시
- [ ] **CLI-06**: Argu 기반 인자 파싱, `--help` 자동 생성

### Observability

- [ ] **OBS-01**: 모든 step이 JSONL 형식으로 `~/.bluecode/session_<timestamp>.jsonl`에 기록 (crash post-mortem용; 세션 resume 아님)
- [ ] **OBS-02**: Serilog로 구조화 로그를 stderr에 출력 (Spectre UI는 stdout, 로그는 stderr 분리)
- [ ] **OBS-03**: 시작 시 `/v1/models`로 실제 `max_model_len` 조회, 누적 컨텍스트가 80% 도달 시 사용자에게 경고

## v2 Requirements

v1 안정화 후 단계적 추가. 트리거 발생 시 v1.x로 이동.

### Streaming & Persistence

- **STM-01**: SSE 토큰 스트리밍 — Qwen 응답을 토큰 단위로 stdout에 출력 (트리거: blank terminal UX 불편)
- **STM-02**: `IAsyncEnumerable<string>` 기반 스트림 처리 (`System.Net.ServerSentEvents.SseParser`)
- **SES-01**: 세션 영속화 — 메시지 히스토리/step 로그를 디스크에 저장
- **SES-02**: `--resume <id>` 플래그로 이전 세션 복원

### Tools (Extended)

- **TLX-01**: `edit_file` — exact-string old/new 매칭으로 surgical edit
- **TLX-02**: `glob_search` — 패턴으로 파일 찾기
- **TLX-03**: `grep_search` — 파일 내용에서 패턴 검색

### Slash Commands & Compaction

- **SLA-01**: `/context` — 현재 토큰 사용량 조회
- **SLA-02**: `/compact` — context 강제 압축
- **SLA-03**: `/agents` — 등록된 에이전트 프로파일 조회 (sub-agent 도입 후)
- **CMP-01**: 자동 토큰 기반 snipping — 오래된 tool result를 토큰 카운트로 잘라냄
- **CMP-02**: 자동 압축 임계값 설정 (기본: actual context의 80%)

### Sub-agents (v2+, 단계적)

- **SUB-01**: `Agent` 툴로 자식 에이전트 위임
- **SUB-02**: 의존성 기반 토폴로지컬 배치 (단, flat loop가 50+ session 검증된 후)

### Project Memory

- **MEM-01**: `CLAUDE.md` 디스커버리 — 현재/상위 디렉토리에서 찾아 system prompt에 주입

## Out of Scope

영구 제외. 본인이 명시했거나 anti-feature로 판단됨.

| Feature | Reason |
|---------|--------|
| MCP runtime | 본인 명시 OUT, 25+ 모듈 복잡도, 개인용 가치 없음. `run_shell`로 외부 CLI 호출로 대체 |
| LSP intelligence | 휴리스틱 LSP는 품질 낮고, real LSP는 daemon 필요. v2의 grep_search로 대체 |
| Plugin/hook system | 단일 사용자 도구에 manifest plugin 가치 없음. 새 툴은 F# 모듈로 추가 |
| GUI (web/TUI) | 본인 명시 OUT. CLI stdout만 |
| Permissions/policy UI | 단일 사용자에게 4-tier policy UI 가치 없음. CLI 플래그로 충분 |
| Remote/worktree/team runtime | 본인 명시 OUT. Git은 `run_shell`로 |
| Windows/Linux 지원 | 본인 명시 Mac-only. Darwin path만 |
| AOT / 단일 바이너리 | 본인 명시 `dotnet run`만 |
| Claude 프롬프트 직접 재사용 | 본인 설계 원칙. Qwen에서 hallucination/format error 유발 |
| Zero-dependency 제약 | 본인 명시 NuGet 자유. .NET 관례 따름 |
| Multi-tool chaining (한 step에 여러 툴) | 본인 설계: "one tool per step, no chaining" |
| Heavy reasoning chains (>5 steps) | Qwen 32B는 long chain에서 coherence 잃음, hallucination 증가 |
| LLM 기반 context 압축 | 압축에 LLM 호출 = VRAM/latency 2배. 토큰 카운트 snip으로 충분 |
| OCR / image / vision | Qwen 32B/72B 텍스트 모델, vision 없음 |
| Background daemon sessions | 인터랙티브 개인 사용에 가치 없음. Foreground only |
| Cost/USD 추적 | 로컬 모델, USD 비용 없음. Token count는 OBS-03이 커버 |
| Notebook (.ipynb) editing | Target use case 아님 |
| Voice mode (STT/TTS) | Out of scope, 외부 의존성 |
| Telemetry/analytics | 단일 사용자 로컬 도구 |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| FND-01 | Phase 1 | Pending |
| FND-02 | Phase 1 | Pending |
| FND-03 | Phase 1 | Pending |
| FND-04 | Phase 1 | Pending |
| ROU-01 | Phase 1 | Pending |
| ROU-02 | Phase 1 | Pending |
| ROU-03 | Phase 1 | Pending |
| LLM-01 | Phase 2 | Pending |
| LLM-02 | Phase 2 | Pending |
| LLM-03 | Phase 2 | Pending |
| LLM-04 | Phase 2 | Pending |
| LLM-05 | Phase 2 | Pending |
| LLM-06 | Phase 2 | Pending |
| TOOL-01 | Phase 3 | Pending |
| TOOL-02 | Phase 3 | Pending |
| TOOL-03 | Phase 3 | Pending |
| TOOL-04 | Phase 3 | Pending |
| TOOL-05 | Phase 3 | Pending |
| TOOL-06 | Phase 3 | Pending |
| TOOL-07 | Phase 3 | Pending |
| LOOP-01 | Phase 4 | Pending |
| LOOP-02 | Phase 4 | Pending |
| LOOP-03 | Phase 4 | Pending |
| LOOP-04 | Phase 4 | Pending |
| LOOP-05 | Phase 4 | Pending |
| LOOP-06 | Phase 4 | Pending |
| LOOP-07 | Phase 4 | Pending |
| OBS-01 | Phase 4 | Pending |
| OBS-02 | Phase 4 | Pending |
| CLI-01 | Phase 5 | Pending |
| CLI-02 | Phase 5 | Pending |
| CLI-03 | Phase 5 | Pending |
| CLI-04 | Phase 5 | Pending |
| CLI-05 | Phase 5 | Pending |
| CLI-06 | Phase 5 | Pending |
| OBS-03 | Phase 5 | Pending |
| ROU-04 | Phase 5 | Pending |

**Coverage:**
- v1 requirements: 37 total
- Mapped to phases: 37
- Unmapped: 0 ✓

---
*Requirements defined: 2026-04-22*
*Last updated: 2026-04-22 — traceability finalized after roadmap creation*
