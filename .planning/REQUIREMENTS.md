# Requirements: blueCode v1.1 Refinement

**Defined:** 2026-04-23
**Core Value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Milestone goal:** v1.0 UAT 중 노출된 3개 기술 빚 청소 — 이식성, startup 품질, `--verbose` 품질

## v1.1 Requirements

v1.0 UAT(05-04) 및 후속 세션에서 노출된 기술 빚. 새 기능 없음.

### Refactor / Portability

- [ ] **REF-01**: `Router.modelToName` 하드코딩 제거 — `CompositionRoot.bootstrapAsync` 가 `/v1/models` 응답의 `data[0].id` 를 조회해 `Router` 에 (또는 `AppComponents` 에) 동적으로 주입. 모델 디렉토리 경로가 바뀌어도 Core 재컴파일 필요 없이 작동. v1.0 UAT 핫픽스 (`5ab5a95`) 로 들어간 절대경로 하드코딩 제거.
- [ ] **REF-02**: `/v1/models` probe 를 bootstrap 에서 lazy 화 — 현재 `bootstrapAsync` 가 8000 을 동기적으로 probe, 이 때문에 `--model 72b` 모드에서도 32B cold-start 시 타임아웃 WARN 발생. probe 를 각 model 의 첫 실제 LLM 호출 직전으로 이동하여 해당 포트만 probe. bootstrap 은 네트워크 터치 없이 반환.

### Observability

- [ ] **OBS-05**: 실제 LLM thought 를 `Step.Thought` 에 저장 — 현재 placeholder `"[not captured in v1]"` 리터럴. `ILlmClient.CompleteAsync` 반환 타입을 확장하여 LLM 의 `thought` 필드를 Step 까지 전달 (예: `Task<Result<LlmStep, AgentError>>` 또는 `Task<Result<(Thought * LlmOutput), AgentError>>`). `--verbose` 출력이 실제 reasoning 을 보여주도록. Schema 는 Phase 2 에서 이미 `thought` 필드를 검증하므로 parsing 은 기존 pipeline 재사용.

## Deferred / v1.2+ Candidates

v1.0 research/SUMMARY.md 에서 식별된 항목 중 이번 milestone 에 포함 안 함:

### New Tools

- **TLX-01** (v1.2+): `edit_file` — exact-string old/new 매칭으로 surgical edit. 트리거: full-file write 가 diff noise 유발할 때.
- **TLX-02** (v1.2+): `glob_search` — 패턴으로 파일 찾기.
- **TLX-03** (v1.2+): `grep_search` — 파일 내용에서 패턴 검색.

### Streaming & Persistence

- **STM-01** (v1.2+): SSE 토큰 스트리밍 — blank terminal UX 불편 트리거 시.
- **SES-01** (v2+): 세션 영속화 + `--resume <id>`.

### Slash Commands & Compaction (v2+)

- `/context`, `/compact`, `/agents`, auto token-aware compaction.

### Sub-agents (v2+)

- `Agent` tool — flat loop 가 50+ 실제 세션에서 검증된 후.

### Project Memory (v2+)

- `CLAUDE.md` discovery — 현재/상위 디렉토리에서 찾아 system prompt 주입.

## Out of Scope

v1.0 OOS 유지. v1.1 에서 추가 제외는 없음.

| Feature | Reason |
|---------|--------|
| MCP runtime / LSP / Plugin / hook / GUI / Windows, Linux / AOT / multi-tool chaining / >5 step chains / LLM-based compaction / vision / voice / background daemon / team runtime / cost tracking / notebook editing / telemetry | v1.0 에서 이미 명시; 그대로 유지 |
| Cross-turn memory in multi-turn REPL | 각 turn 은 독립 `runSession` — v2+ 스코프 |
| Claude Code 프롬프트 직접 이식 | Qwen format error 유발; 설계 원칙 |
| Zero-dependency 제약 | .NET 관례대로 NuGet 자유 |

## Traceability

Roadmap 생성 후 채워짐.

| Requirement | Phase | Status |
|-------------|-------|--------|
| REF-01 | Phase 6 | Pending |
| REF-02 | Phase 6 | Pending |
| OBS-05 | Phase 7 | Pending |

**Coverage:**
- v1.1 requirements: 3 total
- Mapped to phases: 3 (100%)
- Unmapped: 0 ✓

---
*Requirements defined: 2026-04-23*
*Last updated: 2026-04-23 after v1.1 scope confirmation*
