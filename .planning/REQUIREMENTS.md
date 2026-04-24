# Requirements: blueCode v1.2 Tool Expansion

**Defined:** 2026-04-24
**Core Value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Milestone goal:** 일상 코딩 워크플로 병목 제거 — surgical edit, native search, read_file metadata 로 agent 효율 + 소형 모델 안정성 개선

## v1.2 Requirements

4개 요구사항. 오늘 (2026-04-24) 벤치마크에서 실측된 pain signal 에 각각 대응.

### Tools (Extended)

- [ ] **TLX-01**: `edit_file` — exact-string find-and-replace surgical edit
  - Input: `{ path: string; oldString: string; newString: string }`
  - Behavior: 파일 읽고, `oldString` 이 정확히 1회 등장하면 `newString` 으로 치환 후 저장. 0회 또는 2회+ 등장 시 fail (`Failure "oldString not found"` 또는 `Failure "oldString matches N times; refine to make unique"`).
  - Path validation: project root scope (TOOL-02 `write_file` 와 동일)
  - Truncation: `oldString` / `newString` 길이 제한 없음 (TOOL-06 의 2000-char truncation 은 응답에만 적용, 입력 아님).
  - **근거**: 2026-04-24 벤치마크 W2 에서 `write_file` 로 4-line 파일 재작성 시 72B 14s, 32B 6.2s. 1000-line 파일에 1-line 수정하면 수초-수십초 낭비 + JSON content 필드 거대화. `edit_file` 은 1-2s.

- [ ] **TLX-02**: `glob_search` — 파일 패턴 finder
  - Input: `{ pattern: string; path: string option }` — pattern 예: `"src/**/*.fs"`, `"**/*.md"`
  - Behavior: project root 기준 (`path` 없으면) 또는 지정된 `path` 기준 glob 매칭 파일 경로 목록 반환. `.gitignore` 존중하지 않음 (v1.3+ 고려).
  - Output: `string list` of relative paths, 최대 100개 (초과 시 truncate marker)
  - Path validation: project root scope
  - **근거**: 현재 파일 찾기는 `run_shell "find . -name '*.fs'"` 패턴. bash_security.py 게이트 통과 부담 (T5 에서 72B 의 `find -exec \;` denied 사례). native tool 이면 deterministic.

- [ ] **TLX-03**: `grep_search` — 콘텐츠 패턴 finder
  - Input: `{ pattern: string; path: string option; fileGlob: string option }` — pattern 은 regex 또는 fixed string
  - Behavior: `pattern` 을 파일 내용에서 검색, 매칭된 line 을 `(relativePath, lineNumber, lineContent)` 구조로 반환. `fileGlob` 으로 검색 범위 제한 (예: `"*.fs"`).
  - Output: match list, 최대 100개 (초과 시 truncate marker). 각 lineContent 200-char truncate.
  - Path validation: project root scope
  - **근거**: `run_shell "grep -r TODO src/"` 패턴 빈번. native 는 security gate 우회 + 구조화된 output (agent 가 line number 직접 사용 가능 — 현재는 bash stdout 파싱 필요).

### Tools (Enhancement)

- [ ] **TOOL-08**: `read_file` 응답에 파일 bounds 메타데이터 포함
  - 변경: `ToolResult.Success` 의 `read_file` 분기 output 에 아래 정보 부가 (기존 text 내용 위 또는 아래에 구조화된 prefix/suffix 로 삽입):
    - `total_lines`: 파일 전체 line 수
    - `returned_range`: 반환된 line 범위 (`start_line` 없으면 `1-N`, `start_line=50, end_line=100` 이면 `50-100`)
    - `truncated`: 2000-char 제한에 걸렸는지 여부
  - 형식 제안 (JSON-serializable within ToolResult.Success string payload):
    ```
    [file: src/X.fs, lines 50-100 of 147, not-truncated]
    <actual content>
    ```
  - Backward compat: 기존 text content 는 그대로, 메타데이터는 header line 1줄 추가. LLM prompt 에 포함될 때 agent 가 파일 bounds 인지 가능.
  - **근거**: 2026-04-24 벤치마크 T6 에서 **32B 가 결정론적으로 실패** — `start_line=2001, 4001, 6001` 을 150-line 파일에 요청. 매번 "Success" 로 동일 응답 받음. 파일 크기 metadata 가 있으면 32B 도 `start_line <= total_lines` 가 필요함을 인지, 전략 교정 가능. 근본적으로 T6 failure mode 제거.

## Future Requirements (v1.3+)

v1.2 에서 제외한 시드 후보들. `.planning/STATE.md` Pending Todos 에 상세 기록.

### Streaming & Persistence

- **STM-01** (v1.3+): SSE 토큰 스트리밍 — `ILlmClient.CompleteAsync` 를 `IAsyncEnumerable<string>` 반환으로 확장, Rendering.fs 실시간 토큰 출력
- **SES-01** (v2+): 세션 영속화 + `--resume <id>`

### Escalation & UX

- **ROU-05** (v1.3+): `MaxLoopsExceeded` 자동 32B→72B escalation — TOOL-08 이 root cause 제거하면 불필요할 가능성 있어 후순위
- **CLI-08** (v1.3+): Ctrl+C 진행 중 "Cancelling..." 표시 + partial token count
- **PERF-01** (v1.3+ with research): System prompt 1200→600자 단축 (JSON schema 준수 검증 필요)

### Hygiene

- **OPS-01** (v1.3+): `launchd` 기반 Prompt cache 자동 kickstart 스케줄 (일일 등)
- **OBS-06** (v1.3+): Per-port `MaxModelLen` visibility 향상 (현재 8192 floor)
- **TST-01** (v1.3+): `makeMockResponse` shared test helper 통합

## Out of Scope

v1 정의 유지. v1.2 에서 추가 제외 없음.

| Feature | Reason |
|---------|--------|
| MCP / LSP / Plugin / hook / GUI / Windows·Linux / AOT / multi-tool chaining / >5 step chains / LLM-based compaction / vision / voice / background daemon / team runtime / cost tracking / notebook editing / telemetry | v1.0 에서 이미 명시; 그대로 유지 |
| Cross-turn memory in multi-turn REPL | 각 turn = 독립 `runSession` (v2+ 스코프) |
| Multi-platform `tryParseModelId` | Windows OOS — `StartsWith("/")` 휴리스틱 Mac/Linux 전용 |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| TLX-01 | Phase 8 | Pending |
| TLX-02 | Phase 8 | Pending |
| TLX-03 | Phase 8 | Pending |
| TOOL-08 | Phase 9 | Pending |

**Coverage:**
- v1.2 requirements: 4 total
- Mapped to phases: 4/4 (100%)

---
*Requirements defined: 2026-04-24*
*Last updated: 2026-04-23 — Traceability filled after ROADMAP.md creation (Phase 8: TLX-01/02/03; Phase 9: TOOL-08)*
