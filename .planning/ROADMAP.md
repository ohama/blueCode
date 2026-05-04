# Roadmap: blueCode v2.5 REPL ergonomics

**Defined:** 2026-04-29 (Phase 36 added 2026-05-04 — manual test round 1 fixes)
**Phases:** 31 → 36 (6 phases)
**Total requirements:** 12 across 3 categories (Phase 36 is hardening, no new requirements)

## Current Milestone: v2.5 REPL ergonomics

REPL 인터랙티브 사용성 4 갈래 ergonomic gap 묶음. v1.2 Tool ergonomics shape; Cli-layer only; Core purity / bench gate / schema 0/50 / `bench/baseline.json` byte-equal invariant 모두 보존.

### Phase 31: SLASH command core ✓ (completed 2026-04-29)

**Goal:** Slash command parser + dispatcher + 4 in-process commands (`/help`, `/status`, `/clear`, `/exit`/`/quit`) — LLM 호출 없이 REPL 메타-제어 surface 확립.

**Depends on:** None (Cli-layer only; Core 무관)

**Requirements:** SLASH-01, SLASH-02, SLASH-03, SLASH-04

**Success criteria:**
1. `/help` 입력 시 9 commands list (현재 milestone 의 7 + future-stub 표시 가능) 표시; LLM 호출 없음
2. `/status` 가 session id, model name, current turn step count, accumulated chars, 32k context % 모두 표시
3. `/clear` 호출 후 priorSteps = []; new session id; FileSessionStore 에 새 session 시작; 기존 session 의 jsonl 은 untouched
4. `/exit` / `/quit` 입력 시 graceful exit (exit code 0); 현재 session 자동 저장됨
5. Bench gate 7/7 PASS preserved (REPL 은 bench 영역 밖이지만 변경 회귀 방지)
6. 신규 `Cli/SlashCommand.fs` 모듈 + `Cli/Repl.fs` dispatcher 통합 + `Cli/Rendering.fs` `renderHelp`/`renderStatus`

**Plans:** 2 plans

- [x] 31-01-parser-PLAN.md — Pure SlashCommand parser DU + 17 unit tests (Wave 1)
- [x] 31-02-rendering-and-dispatch-PLAN.md — renderHelp/renderStatus + Repl integration + 12 tests + bench gate (Wave 2)

### Phase 32: SLASH session commands ✓ (completed 2026-05-04)

**Goal:** Session 메타-management 명령 — `/sessions` (목록) + `/resume <id>` (in-place switch).

**Depends on:** Phase 31 (parser/dispatcher infrastructure)

**Requirements:** SLASH-05, SLASH-06

**Success criteria:**
1. `/sessions` 가 `~/.bluecode/sessions/*.jsonl` 의 최근 N개 (N = 10 default) 를 id, started_at, turns, first prompt 첫 80자 로 표시
2. `/resume <id>` 가 unknown id → 친화적 에러 표시 (현재 session 유지); known id → in-place switch (priorSteps reload, FileSessionStore active session 변경)
3. corrupt jsonl → SessionCorrupt 식 에러 표시 (REPL 종료 안 함)
4. `FileSessionStore` 에 `listRecent: int -> SessionMeta list` + `loadById: string -> Result<Session, AgentError>` 메서드 추가 (load 는 v2.0 이미 존재; list 만 신규)
5. Bench gate 7/7 PASS preserved

**Plans:** 2 plans

- [x] 32-01-data-and-rendering-PLAN.md — SessionMeta + listRecent in FileSessionStore + renderSessions in Rendering + 12 unit tests (Wave 1)
- [x] 32-02-repl-dispatch-PLAN.md — /sessions and /resume dispatcher arms in Repl + 5 integration tests + bench gate (Wave 2)

### Phase 33: SLASH plan toggle

**Goal:** `/plan` mid-REPL on/off — 다음 turn 부터 plan-mode 적용. `--plan` flag 와 동등한 path 를 REPL 안에서 toggle 가능하게.

**Depends on:** Phase 31 (parser/dispatcher), v2.0 PlanGate (already shipped)

**Requirements:** SLASH-07

**Success criteria:**
1. REPL state 에 `planModeActive: bool` 추가; `/plan` 으로 toggle; 현재 상태가 `/status` 출력에 표시
2. `planModeActive = true` 일 때 다음 prompt 는 `runPlanTurn` 경로 (PlanGate 표시) 사용
3. plan-mode 중 `/plan` 다시 입력 시 off (다음 turn 부터 일반 agent-loop)
4. 현재 turn 진행 중 `/plan` 입력 = invalid (turn 끝나야 toggle 가능); 친화적 안내
5. Bench gate 7/7 PASS preserved (bench 는 single-turn `--plan` 만 사용; toggle 영향 없음)
6. v2.0 의 mid-conversation Role=System 금지 invariant 준수 — `[PLAN MODE]` toggle 알림은 다음 turn 시작 시 user-facing console only (LLM 으로 보내지 않음)

**Plans:** 2 plans (created 2026-05-05 via /gsd:plan-phase 33)

- [ ] 33-01-toggle-and-wiring-PLAN.md — planModeActive cell + /plan toggle + plan-gate REPL Prompt arm + renderStatus 4-arg + renderHelp delta + adapt 5 existing tests + 1 new RenderingTests testCase (Wave 1)
- [ ] 33-02-behavior-tests-and-bench-PLAN.md — 6 new ReplTests integration tests (toggle on/off, /status display, plan-gate Accept/Quit/Error paths, auto-disable) + smoke + bench gate (Wave 2)

### Phase 34: `/edit` multi-line input

**Goal:** `$EDITOR` 호출하여 multi-line prompt 입력. Long refactor / 다단계 명령 / structured prompt 작성 ergonomic.

**Depends on:** Phase 31 (parser/dispatcher)

**Requirements:** EDIT-01

**Success criteria:**
1. `/edit` 호출 시 `Path.GetTempFileName()` 으로 빈 tmpfile 생성
2. `$EDITOR` env var 우선 사용; unset 시 `vi` fallback; both 실패 시 친화적 에러
3. Editor 종료 후 tmpfile content 가 비어있지 않으면 다음 prompt 로 사용; 비어있으면 cancel (REPL 으로 복귀, 다른 명령 입력 가능)
4. tmpfile read 후 즉시 삭제; REPL exit 시 leftover 정리 (atexit-style)
5. Editor 호출 중 Ctrl+C 처리 — child process 종료 후 REPL 으로 복귀 (current turn 영향 없음)
6. Bench gate 7/7 PASS preserved

### Phase 36: Manual test fixes ✓ (completed 2026-05-04)

**Goal:** Address concrete bugs surfaced by 2026-05-04 manual test round (`documentation/manual-test-guide.md`, 65/82 tests executed; 4 FAIL + 5 MIXED). Cli-layer + doc only. Core untouched. Bench gate 7/7 PASS preserved.

**Depends on:** None — independent of Phases 33/34/35 (Cli adapter + PlanValidator + doc; no REPL surface dependency). Can interleave or run after them.

**Requirements:** None new (post-shipment hardening; no requirement IDs)

**Scope (4 fix tracks):**

1. **glob_search recursive default** (T-14 finding) — `Directory.GetFiles(path, pattern)` 가 default 로 top-level 만 매치. `*.fsproj` 같은 자연 패턴이 0 결과. `SearchOption.AllDirectories` 기본값 또는 `**/*` prefix 자동 expansion.
2. **PlanValidator UX rebuild** (T-75/T-76 findings):
   - retry 시 reject detail 을 next User message 로 inject (model 이 정정 가능)
   - placeholder 패턴 (`<...>`, `placeholder`, `<file>`, 빈 string) 식별하는 enumeration 가드 강화
   - PlanRejected 친화적 출력 (현재는 "invalid JSON twice" 로 wrap 됨)
3. **`--allow-paths` CLI flag for scratch dir opt-in** (T-16/17/18/19/100/101 finding) — 신규 CLI 플래그 `--allow-paths <p1>[,<p2>,...]` 추가하여 사용자가 명시적으로 file tool 의 path 잠금을 확장. 기본값은 project-root only (security invariant 보존). `bc --allow-paths /tmp/bc-test "..."` 와 같이 호출 시 FsToolExecutor 가 해당 디렉토리 prefix 도 허용. 매뉴얼 테스트 가이드의 prompt (`/tmp/bc-test/*`, `/tmp/bc-e2e/*`) 는 그대로 유지하되 commands 에 `--allow-paths` 추가. bench/CI 는 플래그 미사용 → 보안 invariant 유지.
4. **Hallucinated success investigation** (T-100 finding) — write_file 가 path 차단됐는데 step renderer 가 `[ok]` 표시 + model 이 "Successfully appended" 답변. FsToolExecutor → AgentLoop.Step.Status → Rendering chain 추적. 가설 (1) tool 결과 wrap 잘못, (2) status field 매핑 버그, (3) model hallucinate. 진단 후 수정 또는 문서화.

**Success criteria:**
1. T-14 재실행 → `*.fsproj` 패턴이 정확히 3개 fsproj 파일 enumerate (BlueCode.Cli/Core/Tests)
2. T-75 재실행 → 11+ step plan 시도 시 친화적 "plan exceeds 10 steps" 메시지 (invalid JSON twice 메시지 아님)
3. T-76 재실행 → placeholder path (`<discovered_file>`, `"placeholder"`) plan 거부 → 친화적 enumeration 가드 메시지
4. PlanValidator reject detail 이 retry 시 LLM 에 visible (trace 로 메시지 배열 확인)
5. T-16/17/18/19/100/101 재실행 시 모두 PASS — `--allow-paths /tmp/bc-test,/tmp/bc-e2e` 사용 시 file tool 들이 정상 동작; 플래그 없이는 기존대로 차단
6. T-100 step status flow 의 root cause 식별 — fix or document
7. Bench gate `bash bench/run.sh --gate` 7/7 PASS preserved
8. Test count delta +7~12 (FsToolExecutor glob recursive test, allow-paths boundary tests [allow-listed pass / non-allow-listed block / parent traversal block], CliArgs `--allow-paths` parse test, PlanValidator placeholder test, retry feedback test, etc.)
9. `git diff master -- src/BlueCode.Core/` 빈 줄 (Core 무수정)

**Out of scope (explicit defer):**
- Auto-allowlist 또는 기본 `/tmp/*` 허용 — 명시적 opt-in (`--allow-paths`) 만 — 자동 fallback 은 보안 invariant 약화 우려
- priorSteps message-ordering quirk (T-54/59/61) — LLM prompt territory; trace 후 prompt-tuning 또는 v2.6+
- Other model behavior issues not fixable at blueCode layer
- `--allow-paths` 의 글로브/와일드카드 패턴 (`/tmp/bc-*`) — Phase 36 은 정확한 prefix 매칭만; glob 은 v2.6+

**Plans:** 3 plans (created 2026-05-04 via /gsd:plan-phase 36)

- [x] 36-01-glob-recursive-PLAN.md — bare-pattern auto-expansion in globSearchImpl + 3 tests (Wave 1; T-14)
- [x] 36-02-allow-paths-PLAN.md — --allow-paths CLI flag + FsToolExecutor extra-roots threading + 8 tests (Wave 2; T-16..T-19/T-100/T-101)
- [x] 36-03-prompt-suffix-and-doc-PLAN.md — planSystemPromptSuffix tightening + manual-test-guide updates + bench gate (Wave 3; T-75/T-76 + T-100 re-interp)

### Phase 35: PrettyPrompt readline + history

**Goal:** `Console.ReadLine` 을 `PrettyPrompt` 라이브러리로 대체. up/down arrow recall + cross-session history persistence + Ctrl+R 검색 + line editing.

**Depends on:** Phase 31 (slash command 도 readline 입력 통과해야 동작; PrettyPrompt 가 slash 인식하도록 통합 검증)

**Requirements:** HIST-01, HIST-02, HIST-03, HIST-04

**Success criteria:**
1. `BlueCode.Cli.fsproj` 에 `PrettyPrompt` PackageReference 추가; 버전은 Phase 35 plan-phase research 단계에서 NuGet 최신 verified 버전으로 결정 + Key Decision outcome 갱신
2. `Repl.fs` 의 `Console.ReadLine` 경로가 PrettyPrompt-based reader 로 교체; slash command 입력 정상 동작 (parser 영향 없음)
3. Up/Down arrow 가 prior prompts 를 recall (current REPL session 내)
4. `~/.bluecode/history` 에 매 prompt submit 시 append (line-per-prompt; multi-line `/edit` 결과는 first-line 만 또는 escape; 명세는 plan-phase 에서 결정)
5. REPL 시작 시 `~/.bluecode/history` 의 마지막 N개 (N = 1000 default) 를 PrettyPrompt history 에 load
6. Ctrl+R 가 reverse-search through history (PrettyPrompt 내장)
7. Bench gate 7/7 PASS preserved (bench 는 non-interactive; REPL 인터랙티브 path 외)
8. macOS Terminal.app + iTerm2 양쪽에서 up/down/Ctrl+R 정상 동작 manual verification
9. Tests: SlashCommand parser tests (Phase 31 에서 추가) 가 PrettyPrompt 통합 후에도 PASS — input 추출 layer 가 readline 아래에 있으므로 영향 없을 것

## Phase Dependencies

```
Phase 31 (slash core)
  ├─→ Phase 32 (slash sessions)
  ├─→ Phase 33 (slash plan toggle)
  ├─→ Phase 34 (/edit)
  └─→ Phase 35 (PrettyPrompt readline)

Phase 36 (manual test fixes) — independent (Cli adapter + PlanValidator + doc)
                              — interleavable with 33/34/35
```

Phase 31 = root. Phase 32-35 모두 31 위에 독립적으로 쌓임 (병렬 가능 / 순차 가능). 권장 순서는 위 (slash 우선 → multi-line → readline 마지막) — readline 이 가장 큰 변경이라 안정된 slash 기반 위에서 진행.

Phase 36 은 REPL 표면과 무관 (FsToolExecutor.glob, PlanValidator, doc) — 33/34/35 와 동시 또는 milestone 끝부분 cleanup 으로 진행 가능.

## Out-of-scope (preserved from v2.4 close + new exclusions)

- `/model` mid-session switch — v2.6+ candidate
- `/save <name>` named sessions — defer
- Auto-completion of slash commands — defer
- 자체 readline 구현 — PrettyPrompt 채택으로 회피
- Cross-session history search UI — Ctrl+R in-session 만으로 시작
- COMPACT-01 자동 압축 — token visibility 만 (`/status`)
- STM-01 streaming — 10번째 deferred
- AGENT-LOOP-FEW-SHOT-01 / COLDSTART-PRISTINE-01 / SUBAG-01 / PLAN-MODE-BENCH-01 / THINK-01 / TOOLCALLS-01 — 직전 milestone 검토에서 동일 사유로 deferred

## Phase Numbering

Continues at 31. Project phase history:
- v1.0: 1-5
- v1.1: 6-7
- v1.2: 8, 9, 9.1
- v1.3: 10-11
- v1.4: 12-13
- v2.0: 14-20 (Phase 16 replan; Phases 17-20 added mid-milestone)
- v2.1: 21
- v2.2: 22-23
- v2.3: 24-27 (Phase 26 BLOCKED; Phase 27 added mid-milestone)
- v2.4: 28, 30 (Phase 29 SKIPPED-by-design)
- **v2.5: 31-36** (Phase 36 added mid-milestone 2026-05-04 — manual test round 1 fixes)

## Stats Target

- 6 phases, ~14-15 plans (2-3 per phase), ~35-50 tests added (parser + dispatcher + integration + 36 fixes)
- LOC estimate: ~310-400 (Cli only)
- Bench gate 7/7 PASS preserved throughout
- Zero changes to: Core/, bench/baseline.json, schema validation, defaultSystemPrompt, planSystemPromptSuffix (modified by Phase 36 for T-75/T-76 mitigation)
- New NuGet: 1 (PrettyPrompt — Key Decision recorded)

---
*Roadmap created: 2026-04-29*
*Phase 36 added: 2026-05-04 (manual test round 1 fixes)*
*Phase 33 plans created: 2026-05-05 (/gsd:plan-phase 33)*
