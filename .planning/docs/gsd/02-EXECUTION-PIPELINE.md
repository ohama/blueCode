# Execution Pipeline 분석

`/gsd:execute-phase` 가 어떻게 동작하는지 분해. blueCode 가 task 를 실행할 때 어떤 메커니즘을 가져올지 결정하는 핵심 문서.

## High-Level 흐름

```
사용자: /gsd:execute-phase 5
              │
              ▼
[1] Validate Phase + Discover Plans      ← PLAN.md 들 찾고, 어느 게 미완료인지
              │
              ▼
[2] Group by Wave                         ← frontmatter.wave 로 grouping
              │
              ▼
[3] Execute Wave-by-Wave                  ← 각 wave 안에서 plans 를 PARALLEL spawn
              │                              wave 끝나면 다음 wave
              ▼
[4] Aggregate                             ← SUMMARY.md 들 수집
              │
              ▼
[5] Commit Orchestrator Corrections       ← orchestrator 가 만든 수정 사항 commit
              │
              ▼
[6] Verify Phase Goal                     ← gsd-verifier spawn (goal-backward)
              │
        ┌─────┼─────┐
       PASS  GAPS  HUMAN
        │     │     │
        │     ▼     │
        │  [7] Loop: plan-phase --gaps → execute-phase --gaps-only
        │           (verifier 가 다시 통과할 때까지)
        ▼
[8] Update ROADMAP/STATE/REQUIREMENTS, Commit phase completion
```

## Wave-Based Parallel Execution — 핵심

### 어떻게 spawn 하나

```python
# 한 wave 안의 plans 를 한 message 안에서 동시 spawn
Task(prompt=plan_01_prompt, subagent_type="gsd-executor", model="...")
Task(prompt=plan_02_prompt, subagent_type="gsd-executor", model="...")
Task(prompt=plan_03_prompt, subagent_type="gsd-executor", model="...")
```

세 호출이 모두 끝날 때까지 Task tool 이 block. 이것이 fan-out/fan-in 메커니즘.

**No polling. No background agents. No TaskOutput loops.** 단순함이 핵심.

### 왜 wave 가 미리 계산되나

planner 가 `wave: 2` 를 frontmatter 에 박아두기 때문에 executor orchestrator 는 wave number 를 보고 단순 grouping 만 하면 됨. **dependency graph 를 다시 풀지 않는다** — 그건 planning 시점에 이미 풀렸음.

### Plan 간 충돌 방지

같은 wave 의 plans 는 `files_modified` 가 disjoint 해야 함. 그래야 동시 실행 시 git/filesystem race 없음. planner 가 이걸 보장 (vertical slice 선호).

### inline content 의 중요성

```bash
PLAN_01_CONTENT=$(cat "{plan_01_path}")
STATE_CONTENT=$(cat .planning/STATE.md)

Task(prompt=f"Execute plan at {plan_01_path}\n\nPlan:\n{PLAN_01_CONTENT}\n\nProject state:\n{STATE_CONTENT}", ...)
```

`@` reference 는 Task() 경계를 못 넘으므로, 모든 의존 파일을 prompt 에 inline. 이것이 subagent 가 context 를 fresh 로 시작할 수 있는 이유.

→ blueCode: F# 에서 LLM call 의 system message 에 plan + state 를 직접 포함. 별도 file system access 없이도 LLM 이 모든 정보를 갖고 시작.

## Executor 의 내부 알고리즘 (gsd-executor.md 에서)

각 executor 가 spawn 된 후 하는 일:

### Step 1: Load Project State

```bash
cat .planning/STATE.md
```

현재 phase, 누적 결정, blocker 들을 internalize. **STATE.md 가 없으면** 옵션 제시 (reconstruct vs continue without).

### Step 2: Load Plan

prompt 안의 plan 을 parse:
- frontmatter (phase, plan, type, autonomous, wave, depends_on)
- objective, context references, tasks, verification, success criteria, output

### Step 3: Determine Execution Pattern

```bash
grep -n "type=\"checkpoint" [plan-path]
```

세 패턴:
- **Pattern A: Fully autonomous** — 모든 task 순차 실행, SUMMARY 작성, commit, return
- **Pattern B: Has checkpoints** — checkpoint 까지 실행 → STOP, structured message return → orchestrator 가 user 와 대화 → fresh continuation agent 로 재개 (resume 아님)
- **Pattern C: Continuation** — `<completed_tasks>` 가 prompt 에 있음 → 이미 한 task 는 verify 만 하고 skip, resume point 부터 실행

### Step 4: Execute Each Task

```
for each task in plan.tasks:
    if task.type == "auto":
        if task.tdd: TDD execution flow (RED-GREEN-REFACTOR)
        else: 일반 execution

        # 도중에 deviation 발견 시 deviation rules 적용
        # auth error 발견 시 authentication gate (checkpoint)

        run task.verify
        confirm task.done met
        commit task atomically
        track commit hash for SUMMARY

    elif task.type starts with "checkpoint:":
        STOP immediately
        return structured checkpoint message
        # 이 agent 는 다시 안 돌아옴 — fresh continuation 이 spawn 됨
```

### Deviation Rules — 가장 실용적인 부분

**execution 도중에 plan 에 없는 work 를 발견할 것이다.** 이는 정상이다. 4 가지 규칙:

| Rule | Trigger | Action |
|------|---------|--------|
| 1 | Bug found | Auto-fix, SUMMARY 에 기록 |
| 2 | Missing critical functionality | Auto-add, 기록 |
| 3 | Blocking issue | Auto-fix to unblock, 기록 |
| 4 | Architectural change needed | **STOP, return checkpoint** |

**우선순위:**
1. Rule 4 → STOP
2. Rules 1–3 → fix automatically
3. 모르겠으면 → Rule 4 (safer to ask)

**핵심 질문:** "이게 correctness, security, 또는 task 완료 능력에 영향 주나?"
- YES → Rules 1–3 (자동 fix)
- MAYBE → Rule 4 (return checkpoint)

→ blueCode: 이건 system prompt 에 박을 수 있는 명시적 정책. agent 가 "이거 추가 작업이 필요한데 어떡하지?" 라고 헛돌지 않도록.

### Authentication Gates — Special Case

CLI/API 호출 중 auth error 만나면 **failure 가 아니라 gate**.
인식: "Not authenticated", "401", "403", "Please run X login"

처리:
1. STOP current task
2. Return checkpoint with type `human-action`
3. Auth 명령 명시 (e.g., `vercel login`)
4. Verify 방법 명시 (e.g., `vercel whoami`)

### Atomic Commit Protocol

각 task 완료 시 **즉시** commit. 다음 task 로 넘어가기 전.

```bash
# 1. Identify modified files
git status --short

# 2. Stage 개별 파일 (NEVER git add . / -A)
git add src/api/auth.ts
git add src/types/user.ts

# 3. Commit
git commit -m "{type}({phase}-{plan}): {task description}

- key change 1
- key change 2"

# 4. Record hash for SUMMARY
TASK_COMMIT=$(git rev-parse --short HEAD)
```

Commit type: `feat | fix | test | refactor | perf | docs | style | chore`

**Atomic commit 의 가치:**
- 각 task 독립적으로 revertable
- `git bisect` 으로 깨뜨린 task 정확히 찾음
- `git blame` 이 line → task context 추적
- 미래 LLM session 에서 history 가 명확

**금지 사항** (CLAUDE.md 와 일치):
- `git add .` 절대 안 됨
- `git add -A` 절대 안 됨
- `git add src/` 같은 broad directory 안 됨
- 항상 individual staging

→ blueCode: F# IToolExecutor 의 Bash tool 이 이미 git 호출 가능. 자동 staging logic 을 deviation rule 처럼 system prompt 에 박을 수 있음.

### TDD Execution (tdd:true task 일 때)

```
RED phase:
  - 첫 TDD task 면 test framework 자동 install
  - test file 작성, expected behavior 기술
  - test 실행 → MUST fail
  - commit: test({phase}-{plan}): add failing test for [feature]

GREEN phase:
  - 최소 코드로 통과
  - 영리하게 X, 작동만
  - test 실행 → MUST pass
  - commit: feat({phase}-{plan}): implement [feature]

REFACTOR phase (if needed):
  - 분명한 개선만
  - test 실행 → MUST still pass
  - commit only if changes: refactor({phase}-{plan}): clean up [feature]
```

각 TDD task = 2–3 commits.

### Step 5: Create SUMMARY.md

모든 task 완료 후 `{phase}-{plan}-SUMMARY.md` 작성.

frontmatter 가 다음 plan/phase 의 dependency graph 를 위해 중요:
```yaml
---
phase: 05
plan: 02
subsystem: prompt           # categorize for cross-phase queries
tags: [prettyprompt, fsharp, history]
requires: [03]              # 이전에 의존한 plans
provides:                   # 우리가 제공한 것
  - "PrettyPrompt history adapter"
  - "IPromptReader port"
affects: [06]               # 미래 phase 가 사용할 가능성
tech-stack:
  added: ["PrettyPrompt 4.x"]
  patterns: ["readline w/ history", "port-adapter"]
key-files:
  created: ["src/.../PromptReader.fs"]
  modified: ["src/Cli/Program.fs"]
duration: "2h 14m"
completed: "2026-04-29"
---
```

이 frontmatter 를 미래 planner 가 dependency graph 로 사용 → "어떤 prior phase 가 현재 phase 와 관계 있나" 를 SUMMARY 본문 다 안 읽고 frontmatter 만으로 결정.

본문 필수 section:
- One-liner (substantive: "JWT auth with refresh rotation using jose" — NOT "Authentication implemented")
- Decisions Made (다음 STATE.md 가 흡수)
- Deviations from Plan (Rule 1–3 의 결과)
- Authentication Gates (있었으면)

### Step 6: Update STATE.md

```
Phase: 5 of 12 (prompt-readline-history)
Plan: 2 of 3
Status: In progress
Last activity: 2026-04-29 - Completed 05-02-PLAN.md
Progress: [████████░░░░] 66%

### Decisions Made
- {SUMMARY 의 decision 들 추가}

### Blockers/Concerns
- {SUMMARY 의 next phase readiness 가 noticed 한 것}

### Session Continuity
Last session: 2026-04-29 14:23
Stopped at: Completed 05-02-PLAN.md
Resume file: None
```

### Step 7: Final Commit (per-plan metadata)

```bash
git add .planning/phases/05-name/05-02-SUMMARY.md
git add .planning/STATE.md
git commit -m "docs(05-02): complete [plan-name] plan

Tasks completed: 3/3
- Task 1 name
- Task 2 name
- Task 3 name

SUMMARY: .planning/phases/05-name/05-02-SUMMARY.md"
```

per-task commit 들과는 **별개**. 이건 metadata 만.

## Phase-Level: Verification Gate

executor 들이 모두 끝나면 orchestrator 가 spawn:

```
Task(subagent_type="gsd-verifier", prompt=...)
```

verifier 의 일은 **codebase 가 phase goal 을 달성했는지** 검증. SUMMARY 가 "구현했다" 고 주장해도 grep 결과는 placeholder 일 수 있음.

### Goal-Backward Verification (3-Level)

각 must_haves.artifacts 에 대해:

**Level 1: Existence** — 파일이 존재하나?
**Level 2: Substantive** — placeholder 가 아닌 실제 구현인가?
- min_lines 체크
- stub pattern grep: `TODO|FIXME|placeholder|not implemented|return null|return {}|return []`
- export 체크: `^export (default )?(function|const|class)`
**Level 3: Wired** — import 되고 사용되나?
- `grep -r "import.*$artifact" src/`
- usage count

| Exists | Substantive | Wired | Status |
|--------|-------------|-------|--------|
| ✓ | ✓ | ✓ | VERIFIED |
| ✓ | ✓ | ✗ | ORPHANED |
| ✓ | ✗ | – | STUB |
| ✗ | – | – | MISSING |

### Key Link Verification (가장 중요)

대부분의 stub 은 여기서 잡힘. 패턴 예:

```bash
# Component → API
fetch(['"].*$api_path) || axios\.(get|post).*$api_path
# 그리고 response 가 사용되나? await || .then || setData

# API → DB
prisma\.$model || db\.$model || $model\.(find|create|update|delete)
# result 가 return 되나?

# Form → Handler
onSubmit=\{ || handleSubmit
# handler 가 stub 인가? (preventDefault 만 || console.log 만)
```

### Status 결정

- `passed` — 모든 truth verified
- `gaps_found` — 1개 이상 truth failed → VERIFICATION.md frontmatter 에 structured gaps 작성 → planner 가 `--gaps` 모드로 새 plan 작성
- `human_needed` — 자동 검증 불가능한 것 (visual, real-time, external service)

### Gap Structure (다음 planner 가 직접 소비)

```yaml
gaps:
  - truth: "User can see existing messages"
    status: failed
    reason: "Chat.tsx exists but doesn't fetch from API"
    artifacts:
      - path: "src/components/Chat.tsx"
        issue: "No useEffect with fetch call"
    missing:
      - "API call in useEffect to /api/chat"
      - "State for storing fetched messages"
      - "Render messages array in JSX"
```

## Gap Closure Loop

verifier 가 gaps_found 반환 시:
1. 사용자에게 `/gsd:plan-phase 5 --gaps` 제안
2. planner 가 VERIFICATION.md 의 gaps 만 보고 새 PLAN (04, 05...) 작성
3. `/gsd:execute-phase 5` 다시 실행 — 미완료 plan 만 (04, 05) 실행
4. verifier 다시 → re-verification mode
   - 이전에 fail 한 것은 full 검증
   - 이전에 pass 한 것은 quick regression check
5. passed 까지 loop

## Phase Completion (verification passed 후)

```bash
# 1. Phase requirements 를 REQUIREMENTS.md 에서 Complete 로 마킹
# 2. ROADMAP.md 의 phase 항목 update
# 3. STATE.md 업데이트
# 4. Commit:
git add .planning/ROADMAP.md .planning/STATE.md .planning/REQUIREMENTS.md
git commit -m "docs(05): complete prompt-readline-history phase"
```

## Quick Mode 비교 (`/gsd:quick`)

같은 인프라, 짧은 path:

| 단계 | Full mode | Quick mode |
|------|-----------|------------|
| Research | gsd-phase-researcher spawn | Skip |
| Plan | gsd-planner standard mode | gsd-planner quick mode (1 plan, 1–3 task) |
| Plan check | gsd-plan-checker spawn (≤ 3 iter) | Skip |
| Execute | wave-based, 여러 executor | 단일 executor |
| Verify | gsd-verifier spawn | Skip |
| Roadmap update | Yes | No (`.planning/quick/NNN-slug/` 별도 디렉토리) |

Quick task 는 STATE.md 의 "Quick Tasks Completed" table 에 row 추가만. ROADMAP 은 안 건드림.

→ blueCode: 사용자 task 를 받았을 때 자동 분기 — 작고 명확하면 Quick path, 크고 불확실하면 Full path. 분기 기준은 LLM 자체가 첫 turn 에서 판단 (예: "1–3 task 로 끝낼 수 있나?").

## 도입 시 핵심 메커니즘 (요약)

| 메커니즘 | 효과 | blueCode 도입 난이도 |
|---|---|---|
| Wave-based parallel | 독립 작업 동시 실행 | **어려움** (현재 single-loop architecture) |
| Inline content (no @) | subagent 가 fresh start 가능 | 쉬움 (이미 그렇게 함) |
| Pattern A/B/C (autonomous, checkpoint, continuation) | resume 메커니즘 | 중간 (state 직렬화 필요) |
| Deviation Rules 1–4 | "이거 plan 에 없는데" 자동 처리 | 쉬움 (system prompt) |
| Authentication gates | auth error 자동 인식 | 쉬움 (system prompt) |
| Atomic commit per task | bisect 가능, 명확한 history | 쉬움 (이미 commit skill 있음) |
| TDD RED-GREEN-REFACTOR | F# strongly-typed 와 잘 맞음 | 중간 |
| Goal-backward verifier | task done ≠ goal achieved 검출 | **어려움** (grep + reasoning 필요) |
| Gap closure loop | 한 번에 못 끝내도 점진적 완성 | 중간 |

다음 문서 `03-AGENT-CONTRACTS.md` 에서 각 agent 의 정확한 input/output contract 를 정리.
