# GSD 기법 분석 문서

`.claude/commands/gsd/` (slash commands) + `.claude/agents/gsd-*.md` (agent 정의) 를 분석해, blueCode 에 도입할 기법들을 정리한 문서 모음.

## 읽는 순서

| # | 파일 | 한 줄 요약 |
|---|------|-----------|
| 0 | [00-OVERVIEW.md](./00-OVERVIEW.md) | GSD 의 5가지 invariant — Plans Are Prompts, Quality Curve, Goal-Backward, File-as-State, Atomic+Wave |
| 1 | [01-PLANNING-PIPELINE.md](./01-PLANNING-PIPELINE.md) | `/gsd:plan-phase` 의 단계별 분해 — research → plan → check → revise loop |
| 2 | [02-EXECUTION-PIPELINE.md](./02-EXECUTION-PIPELINE.md) | `/gsd:execute-phase` 의 wave 병렬, deviation rules, atomic commit, goal-backward verifier |
| 3 | [03-AGENT-CONTRACTS.md](./03-AGENT-CONTRACTS.md) | 5개 핵심 agent (researcher/planner/checker/executor/verifier) 의 input/output 계약 |
| 4 | [04-FILE-PROTOCOL.md](./04-FILE-PROTOCOL.md) | 파일 시스템을 state machine 으로 — PLAN/SUMMARY/VERIFICATION/STATE/ROADMAP schema |
| 5 | [05-ADOPTION-BLUEPRINT.md](./05-ADOPTION-BLUEPRINT.md) | blueCode (F#) 에 도입하기 위한 6 phase 점진적 설계 + 코드 sketch |

## 핵심 통찰 (다 안 읽을 거면 이것만)

1. **Plans Are Prompts** — 다음 LLM 의 prompt 그 자체를 만들어라. PLAN.md 의 frontmatter 가 contract.
2. **2–3 task per plan** — Quality Degradation Curve 에 의해 50% context 안에서 끝내야 일관된 품질.
3. **Goal-Backward must_haves** — task 완료 ≠ goal 달성. truths/artifacts/key_links 3-level 검증.
4. **Subagent ≈ fresh conversation** — blueCode 에선 새 system prompt 로 새 conversation 을 시작하면 동일 효과.
5. **File-as-State** — disk 가 source of truth. session 끊겨도 이어받음.
6. **Deviation Rules 1–4** — execution 도중 plan 에 없는 일 발견 시 자동 처리 정책 (1–3 fix, 4 stop).
7. **Atomic commit per task** — bisect/blame 가능, 미래 LLM 이 history 명확히 읽음.

## 도입 대상 우선순위

추천: A → D + E → B + C → (skip F)

| Phase | 작업 | 가치 |
|-------|------|------|
| A | MVP — planner+executor split, sequential | ⭐⭐⭐ 큰 task 도 구조적으로 처리 가능 |
| D | Deviation Rules 명문화 | ⭐⭐ 일관된 self-correction |
| E | Atomic commit per task | ⭐⭐ history 가치, revert 가능 |
| B | must_haves + grep verifier | ⭐⭐ stub 검출 |
| C | Plan checker (code 부분 먼저, LLM 는 옵션) | ⭐ infinite loop 방지 |
| F | Wave-based parallel | — single-LLM-server 한계로 ROI 낮음 |

## 분석 source 파일들

```
.claude/commands/gsd/
  plan-phase.md          ← 가장 중요 (12.7KB)
  execute-phase.md       ← 가장 중요 (9.3KB)
  quick.md               ← 단순 path
  research-phase.md
  verify-work.md
  discuss-phase.md
  progress.md
  list-phase-assumptions.md
  ... (나머지: milestone-level)

.claude/agents/
  gsd-planner.md         ← 가장 중요 (~50KB equivalent)
  gsd-executor.md        ← 가장 중요
  gsd-plan-checker.md
  gsd-verifier.md
  gsd-phase-researcher.md
  ... (나머지)
```
