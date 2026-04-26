---
created: 2026-04-26
description: prompt 를 줄이거나 변경할 때 직관 대신 bench gate 로 매 cycle 검증하고 FAIL 시 bisect 로 원인 좁히는 패턴
---

# Iterate LLM Prompts with Bench-Driven Validation

LLM system prompt 를 줄이거나 단어를 바꾸는 작업은 **인간 직관이 거짓말한다.**
"이 문장은 별로 중요하지 않을 것 같다" 가 막상 지우면 T6 가 regression 한다. 해법은
**모든 변경에 대해 즉시 자동화된 측정** — 측정 없이는 한 번에 한 가지만 바꿔도 누가
어떤 것을 깨뜨렸는지 알 수 없다.

## The Insight

prompt engineering 은 **bisection-friendly** 한 워크플로로 만들어야 한다. 매 변경마다:

1. 변경 (1 sentence 제거 또는 단어 압축)
2. 측정 (gate 실행, ~수 분)
3. PASS → 다음 변경
4. FAIL → 어느 변경이 깨뜨렸는지 좁히기

이 cycle 은 단순해 보이지만 **bench 가 자동화 + 빠르 + 결정적** 이어야 가능하다.
수동으로 LLM 응답 보고 판단하면 절대 못 한다 (variance + slow).

## Why This Matters

prompt 변경의 효과는 **비선형**이고 **모델별로 다르다.**

- system prompt 에서 한 문장 빼면 32B 가 회복하고 72B 가 깨질 수도
- 단어 하나 바꾸면 T6 가 좋아지고 W2 가 나빠질 수도
- 길이만 보면 줄었는데 실측 정확도는 떨어질 수도

수동 추측으로는 시작점만큼 빠르게 끝점에 도달 못 한다. 자동화된 게이트가 있으면
**"5분 후엔 답을 안다"** 는 안전장치가 생겨서 과감한 변경을 시도할 수 있다.

## Recognition Pattern

다음 상황에서 이 패턴 적용:

- system prompt 단축, action 추가, 단어 변경 등 **LLM behavior 에 영향을 주는 모든 변경**
- 변경 효과가 모델별로 / 케이스별로 다를 수 있는 경우
- "이 문장 지워도 될까?" 라는 의문이 1초 이상 걸리는 경우 → 직관 의존이라 위험
- 회귀를 빨리 발견해야 하는 경우 (큰 변경 batch 에서 어느 변경이 깨뜨렸는지 모르면 비용 폭증)

## The Approach

### Step 1: regression gate 를 먼저 만든다

prompt 를 바꾸기 *전* 에 다음 조건을 만족하는 gate 가 있어야 한다:

- **빠름**: 1 cycle ≤ 5 분 (10 분 넘으면 iteration 의지 꺾임)
- **자동**: 사람 판단 없이 PASS/FAIL 출력
- **결정적**: 같은 코드에서 같은 결과 (LLM variance 가 큰 케이스는 step count 같은 robust metric 으로)
- **subset 가능**: 전체 회귀 테스트가 너무 무거우면 "core 8 tests" 같은 subset

본 프로젝트의 `bench/run.sh --gate` 는 8 invocations 를 ~115s 에 돌리고 step count
+ exit code 를 baseline.json 과 diff 한다.

### Step 2: 한 번에 한 변경, 즉시 검증

```bash
# Cycle N
$ vi src/BlueCode.Cli/CompositionRoot.fs   # 한 sentence 만 지우기
$ wc -c <(extract_prompt)                   # 사이즈 확인
1450  # before: 1689, after: 1450 (-239)
$ bash bench/run.sh --gate
... ~115s ...
===== GATE PASS (8/8) =====
exit 0  # OK, 다음 cycle 로
```

여러 변경을 묶어서 하지 말 것 — FAIL 시 어느 게 원인인지 모른다.

### Step 3: FAIL 시 즉시 bisect

```bash
# Cycle N+3 — 4 changes 모은 후 처음 FAIL
$ bash bench/run.sh --gate
FAIL T6_72b steps=5/5  # max=4
exit 1
```

모은 4개 변경을 절반씩 revert 하며 좁히기:

```bash
$ git checkout HEAD -- src/BlueCode.Cli/CompositionRoot.fs   # 전체 revert
$ git stash pop  # 또는 변경 절반만 다시 적용
$ bash bench/run.sh --gate   # 절반 변경 후 재측정
```

또는 git history 가 atomic 이면 `git bisect` 자동화 가능.

### Step 4: lock-in 시점 만들기

목표 (예: ≤800 chars) 달성 + gate green 인 시점에 commit 하고 다음 단계로:

```bash
$ wc -c <(extract_prompt)
783
$ bash bench/run.sh --gate
===== GATE PASS (8/8) =====
$ git add src/BlueCode.Cli/CompositionRoot.fs
$ git commit -m "feat(11-02): shrink defaultSystemPrompt to 783 chars (PERF-01)"
```

이 commit hash 는 미래의 회귀 분석 시 **알려진 PASS 점**으로 작용한다. baseline.json
도 이 시점 기준으로 갱신.

### Step 5: escape hatch (Path D) 미리 정의

목표가 ≤800 인데 ≤1100 이하로는 안 내려가는 경우, **사전에 정의한 escape hatch** 가
있어야 한다. 본 프로젝트는:

- Path C: ≤800 (default goal)
- Path D: ≤1000 with documented rationale (escape hatch)

목표 미달성을 사후에 합리화하는 게 아니라 plan 단계에서 *언제 어떤 조건에서 escape* 할지
명시하면 무한 iteration 트랩에 빠지지 않는다.

## Example

**Bad — 직감으로 batch 변경 후 한 번에 측정:**

```bash
$ vi prompt.txt   # 5 sentences 지우고 3 단어 압축
$ run-tests       # FAIL — 어느 변경이 원인인지 모름
$ git checkout -- prompt.txt   # 전체 revert
# 어디서 다시 시작해야 하나? 다시 처음부터 시도, 비용 2x
```

**Good — atomic cycle, FAIL 시 즉시 bisect:**

```bash
# Cycle 1
$ vi prompt.txt   # 1 sentence 지움
$ bash bench/run.sh --gate
PASS 8/8  # OK

# Cycle 2
$ vi prompt.txt   # 다른 sentence 지움
$ bash bench/run.sh --gate
PASS 8/8  # OK

# Cycle 3
$ vi prompt.txt   # action schema 압축
$ bash bench/run.sh --gate
FAIL T6_72b   # 이 한 변경이 깨뜨림 — bisect 불필요, 직접 원인 보임
$ git checkout -- prompt.txt   # 이 cycle 만 revert
$ vi prompt.txt   # 다른 압축 시도
$ bash bench/run.sh --gate
PASS 8/8  # OK
```

cycle 마다 한 변경만 하면 FAIL 의 원인이 즉시 보인다. bisect 는 batch 가 클 때만 필요.

## 체크리스트

- [ ] gate cycle 이 5 분 이내인지 (느리면 iteration 의지 꺾임)
- [ ] gate 가 자동/결정적인지 (사람 판단 끼어들면 cycle 중단)
- [ ] 한 cycle 당 한 변경만 했는지
- [ ] FAIL 시 즉시 revert 하고 다른 시도 하는지 (FAIL 누적하면 bisect 비용 폭증)
- [ ] escape hatch 가 plan 단계에 정의됐는지 (사후 합리화 방지)
- [ ] lock-in commit 으로 PASS 점 기록하는지 (미래 회귀 분석용)

## 관련 문서

- `documentation/bench.md` — `bench/run.sh --gate` 사용법
- `documentation/benchmark-32b-vs-72b.md` Part 4 — v1.3 prompt shrink iteration 실측
- `design-bench-regression-gate-with-jq-diff.md` — gate 자체를 어떻게 만드는가
