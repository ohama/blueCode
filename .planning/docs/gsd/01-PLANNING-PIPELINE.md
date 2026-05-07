# Planning Pipeline 분석

`/gsd:plan-phase` 가 어떻게 동작하는지 단계별로 분해. blueCode 가 도입할 가장 핵심적인 알고리즘.

## High-Level 흐름

```
사용자: /gsd:plan-phase 5
              │
              ▼
[1] Validate & Normalize   ← 환경 체크, phase 번호 정규화 (5 → 05)
              │
              ▼
[2] Research?              ← RESEARCH.md 없으면 phase-researcher spawn
              │              (--skip-research / --gaps 면 skip)
              ▼
[3] Spawn Planner          ← gsd-planner 가 PLAN.md 파일들 작성
              │
              ▼
[4] Spawn Plan Checker     ← gsd-plan-checker 가 6 dimension 검사
              │
        ┌─────┴─────┐
       PASS       ISSUES
        │           │
        │           ▼
        │     [5] Revision Loop ── (iter ≤ 3)
        │           │
        │           ▼
        │     Re-spawn Planner with checker feedback
        │           │
        │           └─── back to [4]
        ▼
[6] Done — present wave structure, route to execute
```

## 단계별 상세

### [1] Validate & Normalize

```bash
# Phase 번호 정규화: "5" → "05", "2.1" → "02.1"
if [[ "$PHASE" =~ ^[0-9]+$ ]]; then
  PHASE=$(printf "%02d" "$PHASE")
elif [[ "$PHASE" =~ ^([0-9]+)\.([0-9]+)$ ]]; then
  PHASE=$(printf "%02d.%s" "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}")
fi
```

**왜 정규화가 중요한가:** 디렉토리 매칭이 일관되어야 한다. 같은 phase 가 `5-foo/`, `05-foo/`, `5.0-foo/` 로 흩어지면 파일 시스템 state 가 깨진다.

→ blueCode: 사용자 task 에 ID 를 매기는 규칙 (예: `001`, `002`...) 정해서 directory 명을 일관되게.

### [2] Research (조건부)

**조건 평가 순서:**
1. `--gaps` flag → research skip (gap closure 는 VERIFICATION.md 사용)
2. `--skip-research` flag → skip
3. `config.workflow.research = false` AND `--research` 없음 → skip
4. `RESEARCH.md` 이미 존재 AND `--research` 없음 → skip (재사용)
5. 그 외 → spawn researcher

**researcher 에게 주는 context:**
```
- phase 설명 (ROADMAP.md grep)
- requirements (REQUIREMENTS.md ## Requirements section)
- prior decisions (STATE.md ### Decisions Made section)
- phase context (CONTEXT.md if exists)
```

**핵심 질문 (researcher 의 mental model):**
> "Which library should I use?" 가 아니라
> **"What do I not know that I don't know?"**

researcher 는 다음을 발견해야 함:
- Standard architecture pattern
- Standard library stack
- Common pitfalls
- SOTA vs LLM training data 가 알고 있는 것
- "Don't hand-roll" 목록

**researcher 산출물 (RESEARCH.md) 의 구조 — planner 가 직접 소비하는 section:**

| Section | Planner 가 사용하는 방식 |
|---|---|
| `## Standard Stack` | Plan 의 task 가 이 library 를 사용하도록 강제 |
| `## Architecture Patterns` | Task 구조가 이 pattern 을 따르도록 |
| `## Don't Hand-Roll` | Task 는 listed problem 들에 대해 절대 custom 으로 안 짓도록 |
| `## Common Pitfalls` | Verification step 이 이것들을 체크하도록 |
| `## Code Examples` | Task action 이 이 예시를 reference |

**중요:** researcher 는 **prescriptive** 해야 함. "Consider X or Y" 가 아니라 **"Use X"**. 한쪽으로 결정 내려야 다음 planner 가 망설이지 않는다.

→ blueCode: 작은 task 에는 research 없이 바로 plan 으로. 큰/불확실한 task 만 별도 LLM call 로 research → researchOutput.md 작성 후 planner 에 inline.

### [3] Spawn Planner — 핵심 단계

planner 에게 들어가는 prompt 의 구조 (직접 인용):

```markdown
<planning_context>
**Phase:** {phase_number}
**Mode:** {standard | gap_closure}
**Project State:** {STATE.md content INLINED}
**Roadmap:** {ROADMAP.md content INLINED}
**Requirements:** {REQUIREMENTS.md content if exists}
**Phase Context:** {CONTEXT.md content if exists}
**Research:** {RESEARCH.md content if exists}
</planning_context>

<downstream_consumer>
Output consumed by /gsd:execute-phase.
Plans must be executable prompts with:
- Frontmatter (wave, depends_on, files_modified, autonomous)
- Tasks in XML format
- Verification criteria
- must_haves for goal-backward verification
</downstream_consumer>

<quality_gate>
Before returning PLANNING COMPLETE:
- [ ] PLAN.md files created in phase directory
- [ ] Each plan has valid frontmatter
- [ ] Tasks are specific and actionable
- [ ] Dependencies correctly identified
- [ ] Waves assigned for parallel execution
- [ ] must_haves derived from phase goal
</quality_gate>
```

**핵심 포인트 (반복할 가치 있음):**
- `@` reference 는 Task() 경계를 못 넘는다 → 모든 파일 내용을 **inline** 해야 함
- prompt 가 "downstream consumer" 를 명시한다 → planner 가 누구를 위해 쓰는지 알게 됨
- "quality gate" 가 self-check checklist 로 박혀 있다

#### Planner 의 내부 알고리즘 (gsd-planner.md 에서 추출)

1. **STATE.md 흡수** — 현재 phase, 누적 결정, pending todos, blockers
2. **Codebase map 로딩** (있으면) — 키워드별로 다른 map 파일을 선택적으로 load
3. **Phase 파악** — ROADMAP 에서 goal 추출, 기존 PLAN/DISCOVERY 확인
4. **Discovery level 적용** — Level 0 (skip) ~ Level 3 (deep dive)
5. **Project history 흡수** — frontmatter dependency graph 로 관련 phase 의 SUMMARY 만 선택적으로 read (이게 중요. 모든 SUMMARY 를 reflexively chain 하지 않음)
6. **Phase context 로딩** — CONTEXT.md, RESEARCH.md, DISCOVERY.md
7. **Task breakdown** — dependency 기반, sequence 기반 X
   - 각 task 에 대해: `needs` (전제), `creates` (산출), `has_checkpoint` 기록
8. **Dependency graph build** — vertical slice 선호 (horizontal layer 회피)
9. **Wave assign** — `wave = max(deps[*].wave) + 1`
10. **Plans 로 group** — 같은 wave + 파일 충돌 없음 = 병렬
11. **Goal-backward must_haves derive** — 각 plan 에 대해
12. **Scope estimate** — 50% context budget 안에 맞나? 안 맞으면 split
13. **PLAN.md 작성**
14. **ROADMAP 업데이트** — phase placeholder 채우기
15. **Git commit** — `docs({phase}): create phase plan`

#### Plan 의 frontmatter 구조 (executor 가 직접 파싱하는 계약)

```yaml
---
phase: 05-name
plan: 02
type: execute              # or "tdd" if test-first plan
wave: 2                    # 미리 계산해서 넣음. executor 가 계산하지 않음
depends_on: ["01"]         # plan ID 들
files_modified:            # 동시 실행 시 파일 충돌 검사용
  - src/foo.fs
  - src/bar.fs
autonomous: true           # checkpoint 가 있으면 false
user_setup: []             # 외부 service 가 있을 때만

must_haves:
  truths:                  # Observable behaviors (user perspective)
    - "User can see existing messages"
  artifacts:               # 존재해야 할 파일 + 검증 가능한 속성
    - path: "src/components/Chat.tsx"
      provides: "Message list rendering"
      min_lines: 30        # stub 검출용
  key_links:               # 가장 깨지기 쉬운 연결
    - from: "Chat.tsx"
      to: "/api/chat"
      via: "fetch in useEffect"
      pattern: "fetch.*api/chat"   # grep 으로 검증
---
```

**왜 이렇게 풍부한 frontmatter 인가:** 다음 단계 (executor + verifier) 가 이 frontmatter 만으로 일을 완수할 수 있어야 한다. plain text 안에 정보를 묻어두면 다음 LLM 이 자유롭게 해석한다.

→ blueCode: F# record type 으로 동일 구조를 정의 → JSON/YAML 직렬화 → 디스크에 저장.

#### Plan body 의 task 구조 (XML 사용 이유)

```xml
<task type="auto">
  <name>Task 1: Add CLI flag --eval</name>
  <files>src/BlueCode.Cli/Program.fs</files>
  <action>Add --eval flag using Argu. When present, route to eval mode...</action>
  <verify>dotnet build &amp;&amp; ./blueCode --eval "1+1" returns "2"</verify>
  <done>CLI accepts --eval, returns LLM output, exits 0</done>
</task>
```

각 field 의 의미:
- `<files>`: 정확한 경로. "the auth files" 같이 모호하면 안 됨
- `<action>`: 무엇을 어떻게 + **무엇을 피할지 + 왜** (예: "use jose, NOT jsonwebtoken — Edge runtime CommonJS issue")
- `<verify>`: 실행 가능한 명령. "It works" 안 됨
- `<done>`: measurable acceptance criteria

**Test:** "다른 Claude instance 가 clarifying question 없이 실행할 수 있는가?" — 못 하면 specificity 추가.

#### Task type

| Type | 용도 | autonomy |
|---|---|---|
| `auto` | LLM 이 독립 실행 | Fully autonomous |
| `checkpoint:human-verify` | 시각/기능 확인 (90%) | User pauses |
| `checkpoint:decision` | 구현 선택 (9%) | User pauses |
| `checkpoint:human-action` | 진짜 unavoidable (1%, 예: 이메일 인증) | User pauses |

**Automation-first rule:** LLM 이 CLI/API 로 할 수 있으면 LLM 이 한다. checkpoint 는 자동화 끝난 **후** verification 만.

#### TDD detection

heuristic: `expect(fn(input)).toBe(output)` 를 `fn` 작성 전에 쓸 수 있나?
- Yes → 별도 TDD plan (RED-GREEN-REFACTOR cycle)
- No → 일반 task

TDD candidates: business logic, API contract, data transform, validation rules, algorithms.
TDD skip: UI styling, config, glue code, simple CRUD.

→ blueCode: F# 의 strongly-typed 환경에서는 TDD 가 더 자연스럽다 (signature 부터 정해짐). 도입 가치 높음.

### [4] Spawn Plan Checker — 6 Dimension 검사

planner 가 PLAN.md 를 작성하면, **execution 시작 전에** plan 의 품질을 검사. 이는 "execution context 를 낭비하기 전에 plan 을 고치자" 라는 비용 절감 패턴.

**6 가지 검사 dimension** (gsd-plan-checker.md 에서):

| # | Dimension | 질문 |
|---|---|---|
| 1 | **Requirement Coverage** | Phase 의 모든 requirement 에 대응하는 task 가 있나? |
| 2 | **Task Completeness** | 모든 task 가 Files + Action + Verify + Done 을 갖췄나? |
| 3 | **Dependency Correctness** | depends_on 이 valid 한가? Cycle 없나? Wave 가 dep 와 일치하나? |
| 4 | **Key Links Planned** | 각 must_haves.key_links 를 실제로 구현하는 task 가 있나? (artifact 만 만들고 wiring 빼먹는 경우 잡기) |
| 5 | **Scope Sanity** | Plan 당 task ≤ 3? files ≤ 8? 50% context budget 안인가? |
| 6 | **Verification Derivation** | must_haves.truths 가 user-observable 한가? "JWT installed" (impl-focused) 아니라 "User can log in" (user-focused) |

**Severity:**
- `blocker` — execution 전에 반드시 fix
- `warning` — 실행 가능하지만 권장
- `info` — 개선 제안

**산출물 형식 (issue 1개 예):**
```yaml
issue:
  plan: "16-01"
  dimension: "task_completeness"
  severity: "blocker"
  description: "Task 2 missing <verify> element"
  task: 2
  fix_hint: "Add curl command or test command to confirm endpoint works"
```

→ blueCode: 이 6 dimension 은 거의 그대로 가져와도 된다. F# 으로 plan 데이터 파싱 후 validation function 으로 구현 가능 — LLM 호출 안 해도 되는 부분이 많음 (dependency cycle, scope count).

### [5] Revision Loop (≤ 3 iteration)

checker 가 issue 를 찾으면, **planner 를 다시 spawn** — 단 fresh planning 이 아닌 **revision mode**.

revision prompt:
```markdown
<revision_context>
**Phase:** {phase}
**Mode:** revision

**Existing plans:** {모든 PLAN.md inline}
**Checker issues:** {structured YAML 그대로}
</revision_context>

<instructions>
Make targeted updates to address checker issues.
Do NOT replan from scratch unless issues are fundamental.
Return what changed.
</instructions>
```

**철학:** "Surgeon, not architect." 최소 변경으로 specific issue 만 고침.

`iteration_count >= 3` 이면 user 에게:
1. Force proceed (issue 무시)
2. Provide guidance (사용자가 방향 줌, retry)
3. Abandon (planning 종료)

→ blueCode: 이 loop 는 비싸지만 강력하다. 작은 task 는 quick mode 에서 skip, 큰 task 만 적용.

### [6] 완료 — 결과 표시

```
GSD ► PHASE 5 PLANNED ✓

Phase 5: prompt-readline-history — 3 plan(s) in 2 wave(s)

| Wave | Plans | What it builds |
|------|-------|----------------|
| 1    | 01, 02 | reader port, history adapter |
| 2    | 03     | wire into REPL |

Research: Used existing
Verification: Passed

▶ Next Up
Execute Phase 5 — run all 3 plans
/gsd:execute-phase 5
```

## 도입 시 핵심 메커니즘 (요약)

| 메커니즘 | 효과 | blueCode 도입 난이도 |
|---|---|---|
| Phase 번호 정규화 | 디렉토리 매칭 일관성 | 쉬움 |
| Research → Plan 분리 | 두 단계 모두 prescriptive 해짐 | 중간 (LLM 2번 호출) |
| Frontmatter 가 contract | 다음 단계가 자유롭게 해석 안 함 | 쉬움 (F# record) |
| Plan-checker 6 dimension | 비싼 execution 전 plan 품질 보장 | 중간 (LLM + static check 혼합) |
| Revision loop ≤ 3 | infinite loop 방지 | 쉬움 |
| Wave 미리 계산 | executor 가 단순해짐 | 쉬움 |
| Goal-backward must_haves | task 완료 ≠ goal 달성 구분 | 쉬움 (스키마) + 어려움 (실제 검증) |
| 50% context budget | 품질 일관성 | 쉬움 (token count 측정) |

다음 문서 `02-EXECUTION-PIPELINE.md` 에서 execute-phase 의 wave/deviation/commit 메커니즘을 분석.
