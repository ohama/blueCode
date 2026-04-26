---
created: 2026-04-26
description: LLM agent 의 bench fixture 에서 prompt 가 의도치 않게 system 정책을 무력화하지 않도록 작성하는 원칙 — 명시적 tool naming 회피, 작업-목적 표현, 의도적 예외 명시
---

# Design LLM Agent Bench Fixtures

LLM agent 의 bench 결과는 **fixture prompt 의 표현에 강하게 좌우된다.** "fix the bug
using write_file" 와 "fix the bug" 는 같은 작업을 시키는 것 같지만 측정값이 완전히
다르다. 이 howto 는 fixture prompt 가 의도치 않게 system 정책을 흔들지 않도록 작성하는
원칙을 정리한다.

## The Insight

fixture prompt 의 *목적* 은 **agent 가 자연스러운 의사결정을 통해 작업을 완수하는지**
측정하는 것. tool 을 명시적으로 naming 하면 의사결정 경로가 우회된다 — 모델이
"내 정책을 따를까, 아니면 user 가 시킨 도구를 쓸까" 를 고민하는 게 아니라 "user 가
write_file 을 명시했으니 그걸 쓰자" 로 직행한다.

이는 system prompt 의 directive (예: "edit_file 후 write_file 호출 금지") 가
**user prompt 의 explicit instruction 에 의해 override 됨** 을 노출시킨다. 측정하려는
것이 *system 정책의 효과* 라면 user prompt 가 그 정책을 회피할 표현을 안 써야 한다.

## Why This Matters

fixture prompt 에 "using write_file" 이 있으면:

- **W1 fixture** 측정값: agent 가 4 step (read → edit_file → write_file → final). system
  의 "edit_file 만으로 충분" directive 가 있어도 user 가 write_file 을 명시했으니 모델
  이 따른다.
- prompt 의 "using write_file" 을 빼면: 3 step (read → edit_file → final). system 정책이
  살아남.

같은 system prompt, 같은 코드인데 fixture 의 한 단어가 step count 를 33% 늘린다. 이걸
**"system 정책이 효과 없다"** 로 잘못 결론 내면 잘못된 fix 방향 (post-user injection
같은 architectural intervention) 으로 가게 됨. 실제로는 fixture 가 의도적으로 정책을
test 하고 있던 거다.

본 프로젝트의 W1 fixture 는 정확히 이 효과를 *test* 하기 위해 "using write_file" 을
**의도적으로** 유지한다. 09.1-04 가 이 사실을 발견하기 전엔 fixture 의 잘못된 prompt
로 오해받았다.

## Recognition Pattern

다음 상황에서 fixture prompt 검토 필요:

- bench 결과가 system prompt 변경에 비반응
- 같은 작업의 다른 표현 (tool naming vs goal-only) 이 다른 측정값을 만들 때
- "이 작업은 X tool 을 써야 한다" 는 가정이 fixture 에 들어있을 때
- bench fail 의 원인이 system 정책 vs fixture wording 인지 불분명할 때

## The Approach

### Step 1: 작업-목적 vs tool-지정 표현 구분

각 fixture prompt 의 표현을 두 카테고리로 분류:

| 카테고리 | 예시 | 측정 대상 |
|----------|------|-----------|
| **작업-목적 (goal-only)** | "Fix the bug and save the corrected version" | agent 의 자연스러운 tool 선택 |
| **tool-지정 (tool-named)** | "Save the corrected version **using write_file**" | system 정책이 user-override 를 막을 수 있는지 |

대부분 fixture 는 **goal-only** 여야 한다 — agent 의 의사결정 능력 측정이 주 목적.

### Step 2: tool-named 사용은 *의도적 예외* 로만

특정 fixture 가 system 정책 vs user-override 충돌을 *test 하려는 목적* 이면 tool-named
사용. **이 경우 documentation 에 명시.**

본 프로젝트 W1:
> "Read bench-fixtures/bug_lastchar.fs and fix the bug. Save the corrected version
> **using write_file**."

이 prompt 는 09.1-04 에서 발견된 user-prompt vs system-prompt priority issue 를
*test* 하기 위해 의도적으로 tool 을 명시. 09.1-05 의 post-user injection 메커니즘은
이 prompt 에서도 step count 를 3 으로 유지해야 한다.

`documentation/bench.md` 가 이 예외를 명시:
> **Exception:** The W1 prompt deliberately retains "using write_file" — this validates
> that the 09.1-05 loop injection holds even when the user explicitly names the tool
> the directive forbids. **Do not "fix" W1's prompt by removing the tool name.**

### Step 3: 새 fixture 작성 시 default 는 goal-only

```
✗ Bad: "Read foo.fs and identify the bug. Use grep_search if needed."
        → grep_search 가 의무로 보일 수 있음

✓ Good: "Read foo.fs and identify the bug. Be specific about what input triggers it."
        → tool 선택은 agent 자율, 답의 정확도만 평가
```

특히 `B2` (diagnose-only) fixture 는 tool 을 명시하면 안 된다. 진단의 정확도를
측정하는 것이지 어느 tool 을 쓰는지가 핵심이 아니므로.

### Step 4: bench 결과 해석 시 fixture 표현 재확인

bench 가 예상과 다르게 나오면 다음 순서로 의심:

1. fixture prompt 에 tool-named 표현이 있는가? → goal-only 로 바꾸고 재측정
2. fixture content (입력 파일) 가 의도된 버그를 갖고 있는가? → 빈 시작 / 잘못된 시작 상태 가능
3. 그 다음에 system prompt 또는 코드 측 가설 검토

이 순서를 무시하면 fixture wording 문제를 코드 문제로 오해하기 쉽다.

## Example

**Bad — bench fixture 가 tool 을 명시 (의도 불명):**

```bash
# bench/run.sh
run "T_lastchar_32b" "32b" \
  "Open bug_lastchar.fs and use edit_file to replace s.[s.Length] with s.[s.Length - 1]"
```

이 fixture 는:
- agent 의 tool 선택 능력 측정 안 함 (이미 edit_file 을 지정)
- agent 의 진단 능력 측정 안 함 (이미 정답을 알려줌)
- bug-fix 가 일어났는지만 binary 측정 — 너무 narrow

**Good — goal-only:**

```bash
# bench/run.sh
run "T_lastchar_32b" "32b" \
  "Read bench/fixtures/bug_lastchar.fs and fix the bug. Save the corrected version."
```

이 fixture 는:
- agent 가 read → diagnose → edit_file (또는 write_file) → final 의 자연스러운 의사결정
- step count 가 system 정책 효과 + agent 능력의 합성 척도
- tool 선택의 자유가 있어서 system 정책 변경의 영향을 측정 가능

**Intentional exception (W1) — documented:**

```bash
# bench/run.sh — 의도적 tool-named, documentation/bench.md 에 설명
run "W1_32b" "32b" \
  "Read bench/fixtures/bug_lastchar.fs and fix the bug. Save the corrected version using write_file."
```

이 fixture 는 *예외* 임이 명시됨. system prompt 의 "edit_file 만 사용" directive 가
user 의 명시적 write_file 지시 앞에서도 살아남는지 (post-user injection 이 강제하는지)
test.

## 체크리스트

- [ ] 새 fixture prompt 가 tool 을 명시하지 않는가
- [ ] tool 명시가 의도적이면 documentation 에 *왜* 인지 명시했는가
- [ ] fixture content (입력 파일) 가 정의된 버그를 정확히 갖고 있는가
- [ ] bench 결과가 의외일 때 fixture wording 부터 의심하는 절차가 있는가
- [ ] agent 의 자율 tool 선택 능력 vs system 정책 효과를 어느 fixture 가 측정하는지 정리됐는가

## 관련 문서

- `documentation/bench.md` — fixture naming convention + W1 예외 설명
- `enforce-llm-tool-terminality-via-post-user-injection.md` — W1 의 의도된 test 가
  검증하는 메커니즘
