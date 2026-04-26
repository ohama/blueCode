---
created: 2026-04-26
description: bash + jq 로 LLM/외부-시스템 회귀 게이트 만드는 패턴 — baseline.json 기록, 실측 vs baseline diff, 3-branch verdict, regression-whitelist
---

# Design a Bench Regression Gate with jq Diff

LLM agent 처럼 출력 형태가 비결정적인 시스템에서 회귀 검증은 **각 테스트의 step count
+ exit code 같은 robust metric 을 baseline JSON 으로 동결** 하고, 매 실행마다 실측치를
jq 로 diff 해서 차이가 임계치를 넘으면 exit non-zero 하는 패턴이 가장 깔끔하다. 본 howto 는
3-branch verdict logic + regression whitelist 까지 포함한 패턴 전체를 정리한다.

## The Insight

LLM 출력의 *내용* 은 시드/모델/프롬프트에 따라 흔들려서 unit-test 처럼 정확 비교
불가능. 하지만 *행동의 측정값* (몇 step 걸렸나, exit code 가 0 인가) 은 정상 시스템에서
deterministic 에 가깝다 (variance 가 작은 정수).

→ **JSON 으로 baseline 을 동결 → 실측치를 grep 으로 추출 → jq 로 diff → 임계 위반 시
exit 1.** 이 cycle 은 ≤2 분 내에 PASS/FAIL 답을 준다. unit-test 가 안 잡는 회귀를
잡고, integration-test 보다 빠르다.

핵심 구성요소:
1. **Baseline JSON** — 알려진 PASS 시점의 metric (step counts, exit codes) 을 entry 별로 기록
2. **실측 추출** — 실행 후 log 에서 grep + sed 또는 단순 파싱으로 metric 추출
3. **3-branch verdict** — `is_regression` / `step_count_max` / `baseline_pass + actual_exit` 순서로 평가
4. **Regression whitelist** — 알려진 깨진 케이스를 PASS 처리하는 first-branch (시스템적 회귀가 아닌, 의도적 baseline drift 추적)

## Why This Matters

이 패턴이 없으면:

- 변경 후 "회귀 됐나?" 를 사람이 log 보고 판단 → 느리고 부정확
- LLM 출력 비교를 string-equal 로 시도 → variance 로 매번 실패 → 사람들이 무시
- 회귀를 PR review 시점이나 production 에서 발견 → 비용 폭증

이 패턴이 있으면:
- ≤2 분 cycle 의 자동 게이트 → 변경 직후 즉시 답
- 알려진 regressed 항목은 whitelist 로 처리해서 noise 제거 → real regression 만 alert
- 게이트 결과가 atomic (PASS/FAIL + exit code) → CI / pre-commit hook 에 끼워넣기 쉬움

## Recognition Pattern

다음 시스템에서 이 패턴 적용:

- 출력이 비결정적인데 **행동의 형태** 는 deterministic (step count, exit code, latency 등)
- LLM agent / RPC / async pipeline / 외부 시스템 통합처럼 unit-test 가 못 잡는 영역
- 회귀가 일어나면 단계 수 / 응답 시간 / 성공률 같은 numeric metric 으로 표현됨
- "이 변경이 X 를 깨뜨렸나?" 를 5 분 안에 알아야 하는 모든 상황

## The Approach

### Step 1: baseline.json schema 설계

각 테스트 entry 가 metric 을 갖되, **threshold 형태** 로 (정확값 아닌 max 허용치):

```json
{
  "tests": {
    "T6_32b": {
      "step_count": 3,
      "step_count_max": 5,        // 5 까지 허용 (variance slack)
      "pass": true,
      "elapsed_median_s": 20,
      "note": "..."
    },
    "B2_32b": {
      "step_count": 2,
      "step_count_max": 3,
      "pass": false,
      "regression": true,         // 알려진 깨진 케이스 — 게이트는 PASS 처리
      "expected_diagnosis": "...",
      "note": "..."
    }
  }
}
```

핵심 필드:
- `step_count` (현재 baseline 값)
- `step_count_max` (허용 max — 초과 시 FAIL)
- `pass` (이 entry 가 *원래* PASS 상태인가)
- `regression` (true 면 first-branch verdict 가 무조건 PASS)

### Step 2: gate() 함수 — 측정 + 추출 + diff

```bash
gate() {
    local LOG_DIR="bench/runs/gate-$(date +%Y%m%d-%H%M%S)"
    mkdir -p "$LOG_DIR"
    local BASELINE="bench/baseline.json"

    # 1) 모든 invocations 실행 (run() 헬퍼는 log + meta 파일 작성)
    run "gate_T6_32b" "32b" "<prompt>"
    run "gate_T6_72b" "72b" "<prompt>"
    # ... 8 invocations ...

    # 2) 각 invocation 결과 추출 + baseline 과 비교
    local fail_count=0 pass_count=0
    for key in T6_32b T6_72b W1_32b W2_32b T1_32b T5_72b B2_32b B2_72b; do
        local logfile="$LOG_DIR/gate_${key}.log"
        local metafile="$LOG_DIR/gate_${key}.meta"

        # 추출 — empty 시 0 default 로 bash arithmetic 안전
        local actual_steps
        actual_steps=$(grep -E "Session (ok|error)" "$logfile" | grep -o "[0-9]* steps" | grep -o "[0-9]*" | head -1)
        actual_steps=${actual_steps:-0}

        local actual_exit
        actual_exit=$(grep -o "exit=[0-9]*" "$metafile" | grep -o "[0-9]*" | head -1)
        actual_exit=${actual_exit:-99}

        # baseline 추출
        local baseline_max baseline_pass is_regression
        baseline_max=$(jq -r ".tests.${key}.step_count_max" "$BASELINE")
        baseline_pass=$(jq -r ".tests.${key}.pass" "$BASELINE")
        is_regression=$(jq -r ".tests.${key}.regression // false" "$BASELINE")

        # 3-branch verdict (순서 중요)
        local verdict reason
        if [ "$is_regression" = "true" ]; then
            verdict="PASS"
            reason="known regression"
        elif [ "$actual_steps" -gt "$baseline_max" ]; then
            verdict="FAIL"
            reason="steps=$actual_steps > max=$baseline_max"
        elif [ "$baseline_pass" = "true" ] && [ "$actual_exit" -ne 0 ]; then
            verdict="FAIL"
            reason="exit=$actual_exit but baseline expects pass"
        else
            verdict="PASS"
        fi

        if [ "$verdict" = "PASS" ]; then
            pass_count=$((pass_count + 1))
            printf "  PASS %-10s steps=%s/%s exit=%s\n" "$key" "$actual_steps" "$baseline_max" "$actual_exit"
        else
            fail_count=$((fail_count + 1))
            printf "  FAIL %-10s steps=%s/%s exit=%s — %s\n" "$key" "$actual_steps" "$baseline_max" "$actual_exit" "$reason"
        fi
    done

    if [ "$fail_count" -eq 0 ]; then
        echo "===== GATE PASS ($pass_count/8) ====="
        exit 0
    else
        echo "===== GATE FAIL ($fail_count/8 regressed) ====="
        exit 1
    fi
}
```

### Step 3: 3-branch verdict 의 순서가 핵심

**브랜치 순서** :
1. `is_regression == true` → PASS (알려진 깨진 케이스, whitelist)
2. `actual_steps > baseline_max` → FAIL (step count regression)
3. `baseline_pass == true && actual_exit != 0` → FAIL (unexpected error)
4. default → PASS

**왜 이 순서?**

- regression whitelist 가 *맨 앞* 이어야 한다. 두 번째 이후 검사가 깨진 entry 의
  현재 상태를 잘못 FAIL 시킬 수 있다. 예: B2 가 `regression: true, pass: false, steps=2`
  인데 `step_count_max=3` 이면 step check 는 통과하지만 `baseline_pass=false` 분기에서
  실측 exit_code 와 baseline.pass 가 mismatch 일 때 false positive.
- step check 가 두 번째 — 가장 자주 깨지는 metric 이라 빨리 잡아야 함.
- exit-code check 가 세 번째 — 이미 `pass=true` baseline 만 적용되도록 guard.

### Step 4: regression whitelist 갱신 워크플로

깨진 entry 를 의도적으로 fix 한 뒤 baseline 갱신:

```bash
# 1. 코드 변경 (예: prompt shrink 가 B2 회복 시킴)
# 2. 게이트 실행 — B2 는 regression=true 라 자동 PASS
$ bash bench/run.sh --gate
PASS B2_32b steps=2/3 exit=0   # 알려진 regression 으로 PASS 처리
===== GATE PASS (8/8) =====

# 3. 회복 여부 *수동* 확인 — log 의 답 내용 보기
$ grep -i "empty\|truncation" bench/runs/gate-XXX/gate_B2_32b.log
"thought: ... empty list ..."   # 회복됨!

# 4. baseline.json 갱신 — regression 제거, pass=true
$ jq '.tests.B2_32b |= (del(.regression) | .pass = true)' bench/baseline.json > /tmp/new.json
$ mv /tmp/new.json bench/baseline.json

# 5. 게이트 재실행 — 이제 B2 는 정상 검증 대상
$ bash bench/run.sh --gate
PASS B2_32b steps=2/3 exit=0   # 정상 검증으로 PASS
===== GATE PASS (8/8) =====
```

게이트 자체는 *답의 품질* 을 못 잡으므로 (numeric metric 만 본다), regression 회복은
사람이 log 읽어서 결정하고 baseline 을 명시적으로 flip 한다. 이 manual gate 가 자동화
가능한가? 가능하다 — content-based check 추가 — 하지만 LLM variance 로 false negative
위험이 커지므로 *명시적 사람 결정* 을 유지하는 게 안전하다.

### Step 5: false-positive 방지

```bash
# bash arithmetic 가 empty 입력 받으면 "syntax error" 에러
# → 항상 default 값으로 보호
actual_steps=${actual_steps:-0}
actual_exit=${actual_exit:-99}

# jq 가 missing 키에 'null' 문자열 반환 — 명시적 // false 로 감싸기
is_regression=$(jq -r ".tests.${key}.regression // false" "$BASELINE")
```

이런 사소한 default 가 없으면 한 entry 의 log 가 missing 일 때 게이트 자체가 crash 해서
*전체* 회귀를 못 잡는 사고 발생.

## Example

본 프로젝트 `bench/run.sh` 의 `gate()` 함수가 위 패턴 그대로. 8 invocation × ~14s = 약
115s wall-clock. 출력:

```
===== GATE: regression subset (8 invocations) =====
... per-test run logs ...
===== GATE: compare to baseline =====
  PASS T6_32b     steps=3/5 exit=0
  PASS T6_72b     steps=3/5 exit=0
  PASS W1_32b     steps=3/3 exit=0
  PASS W2_32b     steps=3/3 exit=0
  PASS T1_32b     steps=3/3 exit=0
  PASS T5_72b     steps=3/4 exit=0
  PASS B2_32b     steps=2/3 exit=0
  PASS B2_72b     steps=2/3 exit=0
===== GATE PASS (8/8) =====
```

FAIL 시:

```
  FAIL T6_72b    steps=6/5 exit=0 — steps=6 > max=5
===== GATE FAIL (1/8 regressed) =====
$ echo $?
1
```

이 exit code 는 pre-commit hook / CI 로 직접 사용 가능.

## 체크리스트

- [ ] baseline.json 이 `step_count_max` (threshold) + `pass` + `regression` 을 entry 별로 기록
- [ ] gate() 의 verdict 분기 순서가 `is_regression → step → exit` 인지
- [ ] bash arithmetic 모든 변수가 `${VAR:-DEFAULT}` 로 보호되는지
- [ ] jq 가 missing 키에 대해 `// fallback` 으로 명시적 default 를 가지는지
- [ ] gate cycle 이 ≤ 5 min 인지 (느리면 변경자가 안 돌림)
- [ ] gate 가 자동/결정적인지 (사람 판단 끼어들면 불용)
- [ ] regression whitelist 가 명시적 회복 절차를 갖는지 (auto-recover 안 함)

## 관련 문서

- `documentation/bench.md` — `bench/run.sh --gate` 사용 + 회복 절차
- `iterate-llm-prompts-with-bench-driven-validation.md` — 게이트를 변경 검증에 활용하는 패턴
