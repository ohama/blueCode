# File Protocol — 파일 시스템을 State Machine 으로

GSD 의 가장 중요한 implementation choice 중 하나는 **agent 간 통신을 파일로** 한다는 점. memory/conversation 이 아닌 file system 이 source of truth. 이 문서는 어떤 파일이 어떤 역할인지, 어떤 schema 를 갖는지, 누가 read/write 하는지 정리.

## 디렉토리 구조

```
.planning/
├── PROJECT.md              # what this is (정적, 거의 안 바뀜)
├── STATE.md                # 살아있는 메모리 (자주 update)
├── ROADMAP.md              # phase 목록 + goal
├── REQUIREMENTS.md         # requirement traceability table
├── MILESTONES.md           # 완료된 milestone history
├── config.json             # workflow toggle, model profile
├── codebase/               # codebase map (옵션)
│   ├── STACK.md
│   ├── ARCHITECTURE.md
│   ├── CONVENTIONS.md
│   └── ...
├── phases/
│   └── 05-prompt-readline-history/
│       ├── 05-CONTEXT.md       # discuss-phase 산출 (옵션)
│       ├── 05-RESEARCH.md      # phase-researcher 산출
│       ├── 05-01-PLAN.md       # planner 산출
│       ├── 05-02-PLAN.md
│       ├── 05-01-SUMMARY.md    # executor 산출
│       ├── 05-02-SUMMARY.md
│       ├── 05-VERIFICATION.md  # verifier 산출
│       └── 05-UAT.md           # verify-work 산출 (옵션)
├── quick/                  # quick mode tasks (별도 디렉토리)
│   └── 001-add-cli-flag/
│       ├── 001-PLAN.md
│       └── 001-SUMMARY.md
├── milestones/             # 완료된 milestone 의 phase 들 archive
│   └── v1.0-phases/
└── todos/
    └── pending/            # add-todo 로 capture 된 idea 들
```

핵심 invariant: **phase 디렉토리 명은 zero-padded number + slug** (`05-prompt-readline-history`). agent 들이 normalize 후 glob 으로 찾는다.

## 파일 별 역할

### PROJECT.md (정적)

What this is, core value, validated requirements, out of scope. milestone 마다 한두 번 update. blueCode 의 `.planning/PROJECT.md` 가 좋은 예.

### STATE.md (살아있는 메모리)

가장 중요한 cross-session 파일. 매 plan 끝날 때마다 업데이트.

스키마:
```markdown
## Current Position
Phase: 5 of 12 (prompt-readline-history)
Plan: 2 of 3
Status: In progress
Last activity: 2026-04-29 - Completed 05-02-PLAN.md
Progress: [████████░░░░] 66%

## Decisions Made
| Date | Decision | Phase | Why |
|------|----------|-------|-----|
| 2026-04-29 | Use PrettyPrompt 4.x | 05 | Cross-platform, .NET native |

## Blockers/Concerns
- ...

## Quick Tasks Completed
| # | Description | Date | Commit | Directory |
| 001 | Add --eval flag | 2026-04-28 | abc1234 | [001-add-eval](./quick/001-add-eval/) |

## Session Continuity
Last session: 2026-04-29 14:23
Stopped at: Completed 05-02-PLAN.md
Resume file: None
```

**누가 read/write:**
- planner: read (decisions 가 constraint, todos 가 candidate)
- executor: read at start, write at end (decisions, blockers 추가, position 업데이트)
- progress: read for 현재 상태 표시
- quick: append "Quick Tasks Completed" row

### ROADMAP.md

phase 목록과 각 phase 의 goal/requirements/plan-checkbox.

```markdown
## Phase 5: prompt-readline-history
**Goal:** Migrate from Console.ReadLine to PrettyPrompt for arrow-key history
**Requirements:** UI-04, HIST-01, HIST-02, HIST-03
**Plans:** 3 plans

Plans:
- [x] 05-01-PLAN.md — IPromptReader port
- [x] 05-02-PLAN.md — PrettyPrompt adapter
- [ ] 05-03-PLAN.md — Wire into REPL
```

**누가 read/write:**
- planner: read goal, write Plans 목록 (placeholder 채움)
- executor: write checkbox `[x]` 표시
- verifier: read goal (must_haves derive 시)

### REQUIREMENTS.md

요구사항 traceability table. ID 로 phase 와 매핑.

```markdown
| Req ID | Description | Phase | Status |
|--------|-------------|-------|--------|
| HIST-01 | Up arrow recalls last input | 05 | Pending |
| HIST-02 | History persists across sessions | 05 | Pending |
```

**누가 read/write:**
- planner: read 매핑된 req
- executor: phase 끝나면 status `Pending` → `Complete`
- plan-checker: requirement_coverage 검사 시 read

### {phase}-CONTEXT.md (옵션)

`/gsd:discuss-phase` 산출. 사용자 vision/decisions.

스키마:
```markdown
## Decisions
- Auth provider: Clerk
- Session storage: cookie (httpOnly)

## Claude's Discretion
- Error message wording
- CSS class naming convention

## Deferred Ideas
- Social login (next phase)
```

**누가 read:**
- researcher: 어디를 deep dive 하고 어디는 무시할지 결정
- planner: locked decisions 는 honor

### {phase}-RESEARCH.md

`gsd-phase-researcher` 산출. 강제 section 명 (planner contract):
- `## Standard Stack`
- `## Architecture Patterns`
- `## Don't Hand-Roll`
- `## Common Pitfalls`
- `## Code Examples`
- `## State of the Art`
- `## Sources` (with confidence)

### {phase}-{plan}-PLAN.md

`gsd-planner` 산출. **다음 LLM 의 prompt 그 자체.** frontmatter 가 contract:

```yaml
---
phase: 05-prompt-readline-history
plan: 02
type: execute
wave: 2
depends_on: ["01"]
files_modified:
  - src/BlueCode.Cli/PromptReader.fs
autonomous: true
must_haves:
  truths:
    - "User can recall last input via Up arrow"
  artifacts:
    - path: "src/BlueCode.Cli/PromptReader.fs"
      provides: "PrettyPrompt-backed reader"
      min_lines: 40
  key_links:
    - from: "src/BlueCode.Cli/Program.fs"
      to: "PromptReader.read"
      via: "module call"
      pattern: "PromptReader\\.read"
---

<objective>...</objective>
<execution_context>@workflows/execute-plan.md</execution_context>
<context>@.planning/STATE.md ...</context>
<tasks>
  <task type="auto">
    <name>...</name>
    <files>...</files>
    <action>...</action>
    <verify>...</verify>
    <done>...</done>
  </task>
</tasks>
<verification>...</verification>
<success_criteria>...</success_criteria>
<output>...SUMMARY.md path...</output>
```

### {phase}-{plan}-SUMMARY.md

`gsd-executor` 산출. dependency graph 의 핵심 frontmatter:

```yaml
---
phase: 05
plan: 02
subsystem: prompt        # 미래 plan 이 같은 subsystem 의 SUMMARY 만 read
tags: [prettyprompt, readline]
requires: [03]           # 이전에 의존한 phases
provides:                # 우리가 만든 것 (다른 phase 가 require 할 수 있음)
  - "IPromptReader port"
  - "PrettyPrompt adapter"
affects: [06]            # 미래 phase 가 영향 받을 수 있음
tech-stack:
  added: ["PrettyPrompt 4.x"]
  patterns: ["readline w/ history", "port-adapter"]
key-files:
  created: ["src/BlueCode.Cli/PromptReader.fs"]
  modified: ["src/BlueCode.Cli/Program.fs"]
duration: "1h 23m"
completed: "2026-04-29"
---

# Phase 5 Plan 2: PrettyPrompt adapter Summary

[substantive one-liner]
JWT auth with refresh rotation using jose library, httpOnly cookie 15min/7day.

## Decisions Made
- ...

## Deviations from Plan
### Auto-fixed Issues
**1. [Rule 1 - Bug] Fixed case-sensitive email uniqueness**
- Found during: Task 4
- Issue: ...
- Fix: ...
- Files modified: ...
- Commit: abc1234

## Authentication Gates
- ...

## Files Created/Modified
- ...

## Next Phase Readiness
- ...
```

**왜 이렇게 풍부한가:** 미래 planner 가 `affects`/`requires`/`provides` 만으로 dependency graph 를 build → 모든 SUMMARY 본문 안 읽고 frontmatter scan 만 함 → context efficient.

### {phase}-VERIFICATION.md

`gsd-verifier` 산출. status 가 단순 boolean 이 아닌 **3-state**: `passed | gaps_found | human_needed`. gaps section 이 다음 planner 의 `--gaps` 모드 input.

스키마는 `03-AGENT-CONTRACTS.md` 의 verifier 섹션 참조.

### {phase}-UAT.md (옵션)

`/gsd:verify-work` 산출. 사용자가 plain text 로 답하는 UAT log.

```yaml
---
phase: 05
status: passed | diagnosed | in_progress
tests:
  - id: T1
    desc: "Up arrow recalls last input"
    expected: "..."
    result: pass | fail
    user_response: "yes"
---

## Test 1: Up arrow recalls last input
**Expected:** ...
**Result:** pass
**User said:** "yes"
```

## config.json — Workflow Toggle

```json
{
  "model_profile": "balanced",
  "workflow": {
    "research": true,
    "plan_check": true,
    "verifier": true
  },
  "commit_docs": true
}
```

각 단계를 켜고 끌 수 있음:
- `research: false` → planner 가 RESEARCH.md 없이 시작
- `plan_check: false` → planner 가 만든 PLAN 그대로 execute
- `verifier: false` → executor 끝나면 phase 완료로 간주

→ blueCode: 비슷한 toggle 을 `.planning/config.json` 에 두면 작은 task 에는 verifier 끄기 등 유연하게.

## Session Resume Pattern

session 중단 후 재개:

```
1. progress 또는 resume-work 호출
2. STATE.md read → "Last session, Stopped at" 위치 파악
3. 마지막 PLAN.md 의 SUMMARY 가 없으면 → executor 가 미완 plan 부터 재개
4. SUMMARY 가 있으면 → 다음 wave 또는 다음 plan 으로
```

→ blueCode: 비슷한 resume 메커니즘이 가능하다. `BlueCodeSession` 타입에 `currentPhase`, `currentPlan`, `currentTaskInPlan` 보관 → 디스크 직렬화 → 다음 invocation 에서 read.

## File 기반 통신의 trade-off

### 장점
- **Cross-session continuity** — process 죽어도 state 살아있음
- **Auditability** — git log 가 모든 결정의 history
- **No memory leak** — context 가 안 쌓임 (read 할 때만 inline)
- **Concurrent safety** — wave 안의 plans 가 disjoint 한 파일만 만지면 race 없음
- **Inspectable** — 사람이 직접 cat 해서 확인 가능

### 단점
- **Disk I/O 빈번** — 매 step 마다 read/write
- **Schema drift** — 누군가 frontmatter format 어기면 다음 단계 깨짐 (그래서 plan-checker 가 있음)
- **Partial state on crash** — task 중간에 죽으면 commit 됐는데 STATE 안 업데이트된 상태 가능 (그래서 git status 부터 체크)

→ blueCode: F# 의 strongly-typed record + JSON serialization 으로 schema drift 위험을 컴파일 타임에 잡을 수 있음. 이는 GSD markdown 파일보다 robust.

## Glob Pattern 들

agent 들이 자주 쓰는 패턴:

```bash
# 특정 phase 의 파일들
.planning/phases/${PHASE}-*/*-PLAN.md
.planning/phases/${PHASE}-*/*-SUMMARY.md

# 모든 phase 의 SUMMARY frontmatter (dependency graph build)
for f in .planning/phases/*/*-SUMMARY.md; do
  sed -n '1,/^---$/p; /^---$/q' "$f"
done

# 미완료 plan 찾기
ls .planning/phases/${PHASE}-*/*-PLAN.md 와 *-SUMMARY.md 비교
```

→ blueCode: F# 의 `System.IO.Directory.GetFiles` + glob library 로 동일하게 가능. 또는 `Glob` NuGet package.

## blueCode 에 그대로 가져갈 수 있는 부분

| 파일 | 그대로? | 변경 |
|------|---------|------|
| PROJECT.md | Yes | 이미 있음 |
| STATE.md | Yes | 이미 있음 |
| ROADMAP.md | Yes | 이미 있음 |
| phases/ 구조 | Yes | 이미 있음 |
| PLAN.md frontmatter | Yes (XML body 는 JSON 으로 바꿔도 OK) | F# 친화적 schema |
| SUMMARY.md frontmatter | Yes | dependency graph 활용 |
| VERIFICATION.md schema | Yes | gaps 구조 |
| RESEARCH.md sections | Yes | planner 가 직접 소비 |
| config.json | Yes | toggle |

**즉, 이미 있는 `.planning/` 구조 위에 GSD 의 PLAN/SUMMARY/VERIFICATION schema 를 얹기만 하면 된다.** blueCode 가 GSD 를 사용해 본인의 `.planning/` 을 만들어왔으므로 schema 호환성이 자연스럽다.

다음 문서 `05-ADOPTION-BLUEPRINT.md` 에서 blueCode 의 F# 코드베이스에 어떻게 도입할지 구체적 설계를 정리.
