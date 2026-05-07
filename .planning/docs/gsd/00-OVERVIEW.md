# GSD 기법 분석 — Overview

`.claude/commands/gsd/` + `.claude/agents/gsd-*.md` 를 분석해, blueCode (F# CLI agent) 에 도입할 기법들을 정리한 문서 모음. 목표는 **사용자가 task 를 주면, blueCode 가 자체적으로 plan 을 세우고 sub-work 로 쪼개서 실행하는 능력**을 갖추도록 하는 것.

## GSD 의 핵심 철학 (왜 이렇게 설계됐나)

GSD = "get-shit-done". Claude (LLM) 한 명이 ONE 사용자를 위해 일하는 단순한 모델. 복잡한 enterprise 절차 (RACI, sprint, stakeholder) 가 없음. 다음 5가지 invariant 위에서 작동:

### 1. Plans Are Prompts
PLAN.md 는 "나중에 prompt 로 변환되는 문서"가 아니라 **그 자체가 다음 agent 에게 들어갈 prompt**. 이것이 모든 설계를 좌우하는 가장 중요한 결정.

→ blueCode 에 적용: planner LLM 호출이 만들어내는 "plan" 은 그 자체로 executor LLM 호출의 system/user message 가 되도록 설계해야 함.

### 2. Quality Degradation Curve (가장 실용적 통찰)
LLM 은 context 사용량이 증가함에 따라 자기 자신을 "곧 끝내야 하는 모드"로 인식하고 품질이 떨어진다는 관찰. 데이터:

| Context | Quality | LLM State |
|---|---|---|
| 0–30% | PEAK | Thorough, comprehensive |
| 30–50% | GOOD | Confident, solid work |
| 50–70% | DEGRADING | Efficiency mode |
| 70%+ | POOR | Rushed, minimal |

**규칙:** 한 plan 은 ~50% context 안에서 끝나도록 작게 쪼갠다. **Plan 당 task 2–3개 max**. 더 많은 small plan + parallel/sequential = 더 일관된 품질.

→ blueCode 에 적용: 한 LLM call 당 처리할 sub-work 의 양을 명시적 budget 으로 제한. context 가 차면 "다음 sub-work 는 새 conversation 으로 분리".

### 3. Goal-Backward, Not Forward
- Forward: "What should we build?" → tasks 가 나옴
- Backward: "**What must be TRUE for the goal to be achieved?**" → requirements 가 나옴

핵심 차이: backward 는 task 완료 ≠ goal 달성을 인식한다. "chat component 만들기" task 가 완료돼도 (= 파일이 생겼어도), component 가 placeholder 면 "working chat" goal 은 달성 안 된 것.

세 단계 질문:
1. 무엇이 **TRUE** 여야 goal 이 달성되나? → observable truths (사용자 perspective)
2. 그 truth 들이 성립하려면 무엇이 **EXIST** 해야 하나? → required artifacts (파일/symbol)
3. 그 artifact 들이 작동하려면 무엇이 **WIRED** 되어야 하나? → key links (연결)

→ blueCode 에 적용: 모든 plan 의 frontmatter 에 `must_haves: { truths, artifacts, key_links }` 를 강제하고, 검증 시 SUMMARY 가 아니라 **실제 코드베이스**를 grep 으로 확인.

### 4. File System as State Machine
대화/메모리 대신 **파일이 state**. agent 끼리 통신할 때 "@-reference" 가 아니라 파일 내용을 inline 해서 전달 (`@` 는 Task() boundary 를 못 넘기 때문).

핵심 파일들:
- `.planning/STATE.md` — 살아있는 메모리 (현재 위치, 결정, 블로커)
- `.planning/ROADMAP.md` — 어떤 phase 들이 있는가
- `.planning/phases/{N}-{name}/{phase}-{plan}-PLAN.md` — 실행 가능한 prompt
- `.planning/phases/{N}-{name}/{phase}-{plan}-SUMMARY.md` — 완료 후 결과
- `.planning/phases/{N}-{name}/{phase}-VERIFICATION.md` — goal 달성 여부

→ blueCode 에 적용: 비슷한 file-as-state 디렉토리 구조를 만들면 session 이 끊겨도 다음 session 에서 이어받기 가능.

### 5. Atomic Commits + Wave-Based Parallel
- **Atomic commit**: task 하나 완료할 때마다 즉시 commit. `git bisect` 으로 어떤 task 가 깨뜨렸는지 정확히 찾을 수 있게.
- **Wave-based parallel**: dependency graph 를 분석해서 같은 wave (depends_on 이 모두 만족된) plan 들을 동시 spawn. 이론적으로 N개 LLM 호출이 병렬.

→ blueCode 에 적용: `Task` 가 끝날 때마다 commit. 독립적인 sub-work 는 동일 turn 에 여러 LLM call 로 fan-out 가능 (지금 architecture 에서는 어려움 — 5번 문서에서 논의).

## 두 가지 모드: Standard vs Quick

GSD 에는 두 가지 entry point 가 있다:

| Mode | 사용 시점 | Pipeline |
|------|----------|----------|
| **Full (`/gsd:plan-phase` + `/gsd:execute-phase`)** | 큰 작업, 불확실성 있음 | research → plan → check (≤3 iteration) → execute (wave) → verify → (gap close 루프) |
| **Quick (`/gsd:quick`)** | 작고 명확한 작업 | plan (1 plan, 1–3 task) → execute → STATE 업데이트 |

Quick 은 같은 시스템의 짧은 경로다. research/check/verify subagent 를 skip 하고 planner+executor 만 spawn. **blueCode 도 두 mode 를 갖추는 게 자연스럽다** — 사용자 task 의 크기에 따라 분기.

## Subagent 패턴의 의미

GSD 가 subagent 를 적극 사용하는 진짜 이유:
- **Context isolation**: research, planning, execution 은 각각 context 를 빠르게 소진하므로 fresh 200k 가 필요
- **Orchestrator 는 lean**: ~15% 만 쓰고, 나머지는 subagent 가 100% fresh 로 작업
- **User 가 흐름을 봄**: agent 사이 transition 이 main context 에 보임 (UX)

→ blueCode 의 LLM 한 개로는 진짜 isolation 이 안 됨. 대안:
- 매 단계마다 **새 conversation 시작** (이전 conversation 의 SUMMARY 만 inline)
- 또는 동일 LLM 에 **다른 system prompt** 를 주어 "역할" 을 바꿈

상세는 `05-ADOPTION-BLUEPRINT.md` 참조.

## 이 문서 모음 안내

| 파일 | 내용 |
|------|------|
| `00-OVERVIEW.md` (이 문서) | 철학과 5가지 invariant |
| `01-PLANNING-PIPELINE.md` | plan-phase 의 단계별 분석 (research → plan → check → revise) |
| `02-EXECUTION-PIPELINE.md` | execute-phase 의 wave/deviation/checkpoint/commit |
| `03-AGENT-CONTRACTS.md` | 각 agent 의 input/output 계약, structured return 포맷 |
| `04-FILE-PROTOCOL.md` | 파일 시스템을 state machine 으로 사용하는 방식 |
| `05-ADOPTION-BLUEPRINT.md` | blueCode (F#) 에 도입하기 위한 구체적 설계 제안 |
