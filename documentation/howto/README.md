# Howto Documents

| # | 문서 | 설명 | 작성일 |
|---|------|------|--------|
| 1 | [enforce-llm-tool-terminality-via-post-user-injection](enforce-llm-tool-terminality-via-post-user-injection.md) | System-prompt 만으로 막을 수 없는 user-prompt 지시를 override 하기 위해 user 메시지 *뒤에* System 메시지를 주입하는 패턴 | 2026-04-26 |
| 2 | [iterate-llm-prompts-with-bench-driven-validation](iterate-llm-prompts-with-bench-driven-validation.md) | prompt 를 줄이거나 변경할 때 직관 대신 bench gate 로 매 cycle 검증하고 FAIL 시 bisect 로 원인 좁히는 패턴 | 2026-04-26 |
| 3 | [design-llm-agent-bench-fixtures](design-llm-agent-bench-fixtures.md) | LLM agent 의 bench fixture 에서 prompt 가 의도치 않게 system 정책을 무력화하지 않도록 작성하는 원칙 — 명시적 tool naming 회피, 작업-목적 표현, 의도적 예외 명시 | 2026-04-26 |
| 4 | [anchor-tool-output-header-tests-with-newlines](anchor-tool-output-header-tests-with-newlines.md) | tool 응답에 fixed-format 헤더를 prepend 할 때, 본문 substring 테스트가 헤더 단어와 충돌하지 않도록 `\n` 으로 anchor 하는 패턴 | 2026-04-26 |
| 5 | [design-bench-regression-gate-with-jq-diff](design-bench-regression-gate-with-jq-diff.md) | bash + jq 로 LLM/외부-시스템 회귀 게이트 만드는 패턴 — baseline.json 기록, 실측 vs baseline diff, 3-branch verdict, regression-whitelist | 2026-04-26 |
| 6 | [identify-base-vs-instruct-llm](identify-base-vs-instruct-llm.md) | HF 모델 레포가 Base인지 Instruct인지 이름이 아닌 4가지 구조적 지표로 판별하는 법 | 2026-04-23 |
| 7 | [debug-local-llm-server-responses](debug-local-llm-server-responses.md) | 로컬 OpenAI-compat LLM 서버가 이상한 응답을 낼 때 3단계 체계적 격리로 원인 층을 좁히는 법 | 2026-04-23 |
| 8 | [handle-expecto-console-redirection](handle-expecto-console-redirection.md) | Expecto 병렬 실행이 Console.SetOut/SetError를 덮어써 발생하는 플레이키 테스트를 testSequenced로 직렬화하는 법 | 2026-04-23 |

---
총 8개 | 업데이트: 2026-04-26 (v1.2 + v1.3 lessons added) | 최신순 정렬
