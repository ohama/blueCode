# Agent Contracts — Input/Output 계약

각 agent 의 정확한 책임, 받는 input, 반환하는 structured output 을 정리. blueCode 가 LLM call 들을 어떻게 chain 할지 결계 시 직접 사용할 reference.

## Agent 역할 분담 (한눈에)

| Agent | 입력 | 출력 | 언제 호출 |
|-------|------|------|----------|
| **gsd-phase-researcher** | phase 설명, 기존 결정 | RESEARCH.md | plan 전에 도메인 조사 |
| **gsd-planner** | STATE, ROADMAP, RESEARCH, CONTEXT | PLAN.md 들 | plan-phase 의 main step |
| **gsd-plan-checker** | PLAN.md 들, phase goal | issues YAML 또는 PASS | plan-phase 끝, execution 전 |
| **gsd-executor** | 1개 PLAN.md, STATE | SUMMARY.md + commits | execute-phase 의 wave 안에서 |
| **gsd-verifier** | phase 의 모든 PLAN/SUMMARY, codebase | VERIFICATION.md | execute-phase 끝, phase 완료 전 |

오직 5개의 핵심 agent. 나머지 (debugger, integration-checker, codebase-mapper, project-researcher, roadmapper, research-synthesizer) 는 milestone-level / 새 project setup 용이라 task-execution 에는 덜 중요.

---

## 1. gsd-phase-researcher

### 입력 (orchestrator 가 prompt 에 inline)
```yaml
phase_number: "05"
phase_name: "prompt-readline-history"
phase_description: "Migrate from Console.ReadLine to PrettyPrompt for arrow-key history"
requirements: "..."  # REQUIREMENTS.md grep 결과
prior_decisions: "..."  # STATE.md ### Decisions Made
phase_context: "..."  # CONTEXT.md if exists
output_file: ".planning/phases/05-prompt-readline-history/05-RESEARCH.md"
```

### 출력 — RESEARCH.md 의 강제 section

planner 가 직접 소비하는 section 명을 절대 바꾸면 안 됨:

| Section | Planner 사용 |
|---|---|
| `## Standard Stack` | task 가 이 library 사용 |
| `## Architecture Patterns` | task 구조가 이 pattern 따름 |
| `## Don't Hand-Roll` | task 가 listed problem 들에 custom 솔루션 안 만듦 |
| `## Common Pitfalls` | verification step 이 이것들 체크 |
| `## Code Examples` | task action 이 reference |

### Structured Return (orchestrator 에게)

```markdown
## RESEARCH COMPLETE
**Confidence:** HIGH
### Key Findings
- ...
### File Created
.planning/phases/05-.../05-RESEARCH.md
### Confidence Assessment
| Area | Level | Reason |
| Standard Stack | HIGH | Context7 verified |
### Open Questions
- ...
### Ready for Planning
```

또는:
```markdown
## RESEARCH BLOCKED
**Blocked by:** ...
### Awaiting
- ...
```

### 핵심 규칙
- **Prescriptive, not exploratory:** "Use X" not "Consider X or Y"
- 모든 finding 에 confidence (HIGH/MEDIUM/LOW)
- Negative claims 는 official docs 로 verify

---

## 2. gsd-planner

### 입력 (orchestrator 가 prompt 에 inline)

#### Standard mode
```yaml
phase: "05"
mode: "standard"
state_content: "..."           # STATE.md 전체
roadmap_content: "..."         # ROADMAP.md 전체
requirements_content: "..."    # REQUIREMENTS.md (있으면)
context_content: "..."         # CONTEXT.md (있으면)
research_content: "..."        # RESEARCH.md (있으면)
```

#### Gap closure mode
```yaml
phase: "05"
mode: "gap_closure"
verification_content: "..."    # VERIFICATION.md gaps section
uat_content: "..."             # UAT.md (manual testing 결과, 있으면)
```

#### Revision mode (checker feedback 후)
```yaml
phase: "05"
mode: "revision"
existing_plans: "..."          # 모든 PLAN.md inline
checker_issues: "..."          # plan-checker 의 structured YAML
```

### 출력 — PLAN.md 파일들

`.planning/phases/{phase}-{name}/{phase}-{NN}-PLAN.md`

frontmatter 가 contract:
```yaml
---
phase: 05-name
plan: 02
type: execute              # or "tdd"
wave: 2
depends_on: ["01"]
files_modified: ["src/foo.fs"]
autonomous: true
must_haves:
  truths: ["User can ..."]
  artifacts:
    - path: "..."
      provides: "..."
      min_lines: 30
  key_links:
    - from: "..."
      to: "..."
      via: "..."
      pattern: "..."         # grep regex
gap_closure: true            # gap closure mode 일 때만
---
```

본문은 XML task structure (`<task>...<files>..<action>..<verify>..<done></task>`).

### Structured Return

```markdown
## PLANNING COMPLETE
**Phase:** 05-prompt-readline-history
**Plans:** 3 plan(s) in 2 wave(s)

### Wave Structure
| Wave | Plans | Autonomous |
| 1 | 01, 02 | yes, yes |
| 2 | 03 | no (has checkpoint) |

### Plans Created
| Plan | Objective | Tasks | Files |
| 05-01 | reader port | 2 | 4 |

### Next Steps
Execute: /gsd:execute-phase 05
```

또는 `## CHECKPOINT REACHED`, `## PLANNING INCONCLUSIVE`, `## REVISION COMPLETE`, `## GAP CLOSURE PLANS CREATED`.

### 핵심 규칙

- 한 plan 당 task **2–3개 max** (50% context budget)
- vertical slice 선호 > horizontal layer
- frontmatter 의 `must_haves` 는 goal-backward 로 도출 — 다음 verifier 가 직접 사용
- `wave` 미리 계산해서 박음 (executor 가 다시 풀지 않게)
- `files_modified` 는 wave 안의 plans 가 disjoint 해야 (race 방지)
- `<action>` 은 specific + 무엇을 피할지 + 왜 (예: "use jose, not jsonwebtoken — Edge runtime CommonJS issue")

---

## 3. gsd-plan-checker

### 입력
```yaml
phase: "05"
phase_goal: "..."           # ROADMAP 에서
plans_content: "..."        # 모든 PLAN.md inline
requirements_content: "..." # REQUIREMENTS.md
```

### 출력 — Structured Issues YAML

dimensions:
1. requirement_coverage
2. task_completeness
3. dependency_correctness
4. key_links_planned
5. scope_sanity
6. verification_derivation

```yaml
issues:
  - plan: "05-01"
    dimension: "task_completeness"
    severity: "blocker"      # blocker | warning | info
    description: "Task 2 missing <verify> element"
    task: 2
    fix_hint: "Add command to confirm output"
  - plan: null               # phase-level
    dimension: "requirement_coverage"
    severity: "blocker"
    description: "AUTH-02 (logout) has no covering task"
    fix_hint: "Add logout task to existing plan or new plan"
```

### Structured Return

```markdown
## VERIFICATION PASSED
**Phase:** 05-...
### Coverage Summary
| Requirement | Plans | Status |
### Plan Summary
| Plan | Tasks | Files | Wave | Status |
### Ready for Execution
```

또는:
```markdown
## ISSUES FOUND
**Issues:** 2 blocker(s), 1 warning(s)
### Blockers (must fix)
1. [task_completeness] Task 2 missing <verify>
   - Plan: 05-01, Task: 2
   - Fix: ...
### Warnings (should fix)
...
### Structured Issues
```yaml
issues: [...]
```
```

### 핵심 규칙

- **Static analysis only.** code 실행 X, 앱 실행 X
- task name 만 보고 판단 X — `<action>`, `<verify>`, `<done>` 다 봄
- circular dep 검출, 5+ task/plan flag, missing verify flag
- 다른 검증 (`gsd-verifier`) 와 차이: 이건 **plan** 검증 (코드 실행 전), 그건 **code** 검증 (실행 후)

---

## 4. gsd-executor

### 입력
```yaml
plan_path: ".planning/phases/05-.../05-01-PLAN.md"
plan_content: "..."          # PLAN.md inline
state_content: "..."         # STATE.md inline
completed_tasks:             # continuation 일 때만
  - { task: 1, name: "...", commit: "abc1234", files: [...] }
checkpoint_response:         # checkpoint 후 재개 시
  type: "human-verify"
  response: "approved"
```

### 출력

#### 산출 파일들
- `.planning/phases/{phase}-{name}/{phase}-{plan}-SUMMARY.md`
- `.planning/STATE.md` (업데이트)
- 각 task 당 1+ git commit
- 1개 metadata commit (plan 끝나고)

#### Structured Return — 정상 완료
```markdown
## PLAN COMPLETE
**Plan:** 05-01
**Tasks:** 3/3
**SUMMARY:** .planning/phases/05-.../05-01-SUMMARY.md
**Commits:**
- abc1234: feat(05-01): add IPromptReader port
- def5678: feat(05-01): add ConsolePromptReader adapter
- 9012abc: docs(05-01): complete reader-port plan
**Duration:** 1h 23m
```

#### Structured Return — Checkpoint
```markdown
## CHECKPOINT REACHED
**Type:** human-verify | decision | human-action
**Plan:** 05-01
**Progress:** 1/3 tasks complete

### Completed Tasks
| Task | Name | Commit | Files |
| 1 | ... | abc1234 | ... |

### Current Task
**Task 2:** ...
**Status:** awaiting verification | blocked | awaiting decision
**Blocked by:** ...

### Checkpoint Details
[type 별 다른 content]

### Awaiting
[user 가 할 일]
```

### 핵심 규칙

- **Pattern A (autonomous), B (checkpoint stop), C (continuation)** 자동 분기
- Deviation Rules 1–3 자동 fix, Rule 4 stop
- Auth error → `human-action` checkpoint (failure 아님)
- per-task atomic commit, 절대 batch 안 함
- 절대 `git add .` / `-A` 안 함
- SUMMARY 에 모든 deviation 기록 (rule 1–3 fix 포함)
- continuation 으로 spawn 됐으면 이전 task redo 안 함, commit hash 로 verify 만 함

---

## 5. gsd-verifier

### 입력
```yaml
phase_dir: ".planning/phases/05-..."
phase_num: "05"
plans_paths: [...]
summaries_paths: [...]
requirements: "..."          # REQUIREMENTS.md grep
phase_goal: "..."            # ROADMAP.md
previous_verification: "..." # 이전 VERIFICATION.md (re-verify mode)
```

### 출력 — VERIFICATION.md

```yaml
---
phase: 05-...
verified: 2026-04-29T14:23:00Z
status: passed | gaps_found | human_needed
score: 5/5 must-haves verified
re_verification:             # 이전 verification 있을 때만
  previous_status: gaps_found
  previous_score: 2/5
  gaps_closed: ["..."]
  gaps_remaining: []
  regressions: []
gaps:                        # gaps_found 일 때
  - truth: "..."
    status: failed
    reason: "..."
    artifacts:
      - path: "..."
        issue: "..."
    missing: ["...", "..."]
human_verification:          # human_needed 일 때
  - test: "..."
    expected: "..."
    why_human: "..."
---

# Phase 05: ... Verification Report
[markdown body with tables]
```

### Structured Return

```markdown
## Verification Complete
**Status:** passed | gaps_found | human_needed
**Score:** 5/5 must-haves verified
**Report:** .planning/phases/05-.../05-VERIFICATION.md

[status 별 다른 content]
```

### 핵심 규칙

- **DO NOT trust SUMMARY.** SUMMARY 가 "implemented X" 라고 해도, 실제 grep 로 stub 인지 확인
- 3 level (exists, substantive, wired) 모두 체크
- key_links 체크가 가장 중요 — stub 의 80% 가 여기서 잡힘
- 자동 검증 불가능한 것은 솔직히 `human_needed` 로
- **DO NOT commit** — orchestrator 가 phase 완료 commit 에 bundle

---

## Agent 간 데이터 흐름 (전체)

```
ROADMAP.md (phase goals)        ─┐
STATE.md (decisions, position)   ├─→  orchestrator inline 들
REQUIREMENTS.md                  │
CONTEXT.md (user vision)        ─┘
                                       │
                                       ▼
                              ┌────────────────┐
                              │ phase-researcher│
                              └────────┬───────┘
                                       │ writes
                                       ▼
                                 RESEARCH.md
                                       │ inline
                                       ▼
                              ┌────────────────┐
                              │    planner     │
                              └────────┬───────┘
                                       │ writes
                                       ▼
                                  PLAN.md (×N)
                                       │ inline
                                       ▼
                              ┌────────────────┐
                              │  plan-checker  │
                              └────────┬───────┘
                                       │ issues YAML
                                       ▼
                                 (revision loop ×3 max)
                                       │ → planner again
                                       ▼
                                 PLAN.md OK
                                       │ inline
                                       ▼
                              ┌────────────────┐  parallel within wave
                              │   executor     │  (multiple instances)
                              └────────┬───────┘
                                       │ writes
                                       ▼
                                 SUMMARY.md (×N) + commits
                                       │ + codebase
                                       ▼
                              ┌────────────────┐
                              │   verifier     │
                              └────────┬───────┘
                                       │ writes
                                       ▼
                                 VERIFICATION.md
                                       │
                              ┌────────┼────────┐
                            passed   gaps    human
                              │        │       │
                              ▼        ▼       ▼
                          phase done  loop:   user UAT
                                      planner
                                      --gaps
                                      → executor
                                      --gaps-only
                                      → verifier
```

## blueCode 도입 시 매핑 제안

각 agent 를 LLM call 의 "역할" 로 매핑. 같은 LLM 이지만 system prompt 가 다름.

| GSD agent | blueCode 매핑 | 비고 |
|---|---|---|
| phase-researcher | `Researcher` system prompt | 작은 task 는 skip |
| planner | `Planner` system prompt | 출력 = plan JSON (XML 대신 F# 직렬화에 유리) |
| plan-checker | F# 코드 + LLM 혼합 | dependency cycle, scope count 는 코드, 나머지는 LLM |
| executor | `Executor` system prompt | 한 plan 당 1 LLM session |
| verifier | F# 코드 + LLM 혼합 | grep/exists 는 코드, key_link wiring 추론은 LLM |

상세 설계는 `05-ADOPTION-BLUEPRINT.md` 참조.
