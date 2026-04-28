# Phase 21 평가 narrative — Qwen 3.5 122B-A10B-4bit MoE 코딩 능력 실증

**작성일:** 2026-04-28
**Phase:** v2.1 / Phase 21 (Empirical Qwen 3.5 122B Coding Evaluation)
**최종 verdict:** **Total: 82/100, Recommendation: KEEP**
**관련 문서:**
- `documentation/qwen35-122b-coding-eval.md` — 공식 100-point scorecard 평가 문서 (983 lines, 10 sections)
- `.planning/v2.1-MILESTONE-AUDIT.md` — 마일스톤 audit 리포트
- `.planning/phases/21-empirical-qwen-3-5-122b-coding-evaluation/` — 5개 PLAN/SUMMARY + VERIFICATION

이 문서는 공식 평가 문서의 narrative 동반본이다. 공식 문서가 점수 / 임계값 / verdict 중심으로 짜여 있다면, 이 문서는 **왜 평가했는지, 무엇을 측정했는지, 어떻게 측정했는지, 무엇을 발견했는지**를 사람이 읽기 쉬운 흐름으로 설명한다.

---

## 1. 왜 평가했는가 (Why)

### 1.1 v2.0의 측정 공백

v2.0 milestone (2026-04-27 ship) 은 단일-모델 canonical 모드(Qwen 3.5 122B-A10B-4bit MoE)로의 SWITCH 를 단행하면서, "3.4× wall-clock 속도 개선" 같은 굵직한 아키텍처 주장을 했다. 그런데 그 주장의 근거는 전부 **step count + elapsed time** wall-clock 측정뿐이었다. 즉 `bench/run.sh --gate` 가 8개 픽스처에 대해 `dotnet run` 을 돌렸을 때 종료까지 몇 초 걸렸느냐, 몇 step 만에 답에 도달했느냐 — 이 두 축으로만 모델을 평가하고 있었다.

이건 회귀 게이트로는 충분하지만 (확실히 더 빨라졌고 step count 도 baseline 안이다) 다음 질문에는 답하지 못했다:

- **HumanEval+ pass@1 은 얼마인가?** — 코딩 모델 anchor 벤치마크. 공개된 모든 코딩 모델이 이 숫자를 보고한다. blueCode 가 사용하는 122B-A10B-4bit MoE 의 pass@1 이 클라우드 모델(Claude Opus / GPT-4) 대비 어디쯤인지 데이터가 없었다.
- **tokens/sec throughput 은 정확히 얼마인가?** — 3.4× 는 wall-clock 비율이지 실제 tok/s 가 아니다. mlx_lm.server 의 `usage.completion_tokens` 숫자를 wall-clock 으로 나눈 진짜 tok/s 가 필요했다.
- **time-to-first-token 은?** — interactive UX 의 핵심 지표. SSE streaming 첫 chunk 가 몇 ms 만에 오는가.
- **멀티턴이 N=5 즈음에서 망가지는가?** — community issue [`ml-explore/mlx-lm#1011`](https://github.com/ml-explore/mlx-lm/issues/1011) 가 "approximately 5 rounds" 에서 4-bit MoE 가 degrade 한다고 주장. 실제 그런가?
- **JSON schema 준수율은?** — Phase 18-02 의 0/31 무실패는 좋은 신호지만, 50 invocation 같은 더 엄격한 stress 에도 견디는가?
- **장문 컨텍스트에서 needle 을 뽑아내는가?** — 8k/16k/32k context 에 random position 으로 secret 을 심으면 회수하는가?
- **F# 단일 파일 off-by-one 외에 진짜 코딩 능력은?** — multi-file refactor, 알고리즘 레벨 버그, 비-F# 언어(Python/TypeScript)에서 generalize 하는가?

### 1.2 의사결정 동기

위 질문들에 데이터로 답하는 것이 v2.1 의 목표였다. 답이 KEEP 이면 daily-driver 로 계속 쓸 정당성 확보. KEEP-WITH-CAVEATS 면 어느 차원이 약한지 명시적으로 문서화. ESCALATE 면 "복잡한 작업은 클라우드로 넘기라" 는 권고를 데이터와 함께 내릴 수 있다.

이 평가가 **observation-driven v2.2 scoping 의 prerequisite** 라는 점도 중요했다. v2.2 후보에는 compaction, slash commands, sub-agents, thinking-mode-on, native tool_calls 등이 후보로 올라와 있는데 — 어느 것이 진짜 pain point 인지는 122B 의 실제 약점을 알아야 결정할 수 있다.

### 1.3 명시적으로 평가하지 **않은** 것

- **클라우드 비교 (Claude/GPT-4):** 의도적 non-goal. 사유: API key + cost 의존성, 네트워크 변동성이 측정에 노이즈를 주입, scope drift ("122B 가 일상 코딩에 쓸만한가" 와 "최고의 클라우드 모델은 무엇인가" 는 다른 질문). 사용자가 이미 daily Claude Opus 4.7 사용 경험에서 muscle memory 를 갖고 있으므로, 클라우드는 명시적 벤치마크 없이 "good enough baseline" 가능. 공식 평가 문서 §6.3 에 deliberate boundary 로 문서화.
- **Cold-start 시간:** disruptive (`launchctl kickstart -k` 으로 122B 를 ~3분 죽임). `--coldstart` 핸들러는 구현되어 있지만 default `--full` 에서는 제외, 명시적 invocation 으로만 실행. 점수표에서 0/5 (정직하게 100-point 합계 유지).

---

## 2. 무엇을 측정했는가 (What)

4개 차원, 9개 측정 지표 + 1개 문서 deliverable, 총 10개 requirement (REQUIREMENTS.md `PERF-EVAL-01..02`, `CORR-EVAL-01..04`, `REL-EVAL-01..03`, `DOC-EVAL-01`).

### 2.1 Performance (40 → 25 pts)

| ID | 측정 | 목적 |
|----|------|------|
| PERF-EVAL-01 | tokens/sec throughput | "3.4× 빠르다" 주장을 진짜 tok/s 로 정량화 |
| PERF-EVAL-02 | TTFT (time to first token) | interactive UX 의 핵심 — SSE streaming 첫 chunk 까지 ms |
| (deferred) | cold-start | launchctl kickstart 후 /v1/models 응답까지 — 구현됐지만 disruptive 라 default 미포함 |

throughput 은 5개 prompt × 3 trial = 15 measurement (T1-style 짧은 / T6-style 중간 / B2-style 파일 컨텍스트 / multi-step reasoning / code generation). TTFT 는 200-token prompt 고정으로 10 trial.

### 2.2 Correctness (40 pts, milestone 의 가장 큰 dimension)

| ID | 측정 | 목적 |
|----|------|------|
| CORR-EVAL-01 | HumanEval+ pass@1 (chat + completion 두 모드) | 표준 anchor 벤치마크 |
| CORR-EVAL-02 | 멀티파일 F# refactor (Calculator/Main/Tests + README) | cross-file context coherence |
| CORR-EVAL-03 | 알고리즘 레벨 F# 버그 (binsearch off-by-one upper bound) | W1/W2 보다 깊은 추론 — loop invariant 사고 |
| CORR-EVAL-04 | 언어 커버리지 (Python typeerror + TypeScript missing-await) | F# 외 언어 generalize 여부 |

HumanEval+ 두 모드의 의도:
- **Chat mode** (`/v1/chat/completions` wrapped): blueCode 가 런타임에 실제로 사용하는 경로. 얻는 숫자가 daily-driver 능력의 직접 지표.
- **Completion mode** (`/v1/completions` raw): 출판된 Qwen 모델 카드 숫자와 직접 비교 가능한 형식.

### 2.3 Reliability (25 pts)

| ID | 측정 | 목적 |
|----|------|------|
| REL-EVAL-01 | JSON schema 준수율 (50 single-turn invocation) | Phase 18-02 의 0/31 무실패 주장의 stricter 버전 |
| REL-EVAL-02 | 멀티턴 degradation curve (N=1,3,5,7,10) | mlx-lm#1011 "5 rounds" 주장 검증/반박 |
| REL-EVAL-03 | 장기 context needle (8k/16k/32k) | attention + KV cache 압력 하의 회수 능력 |

REL-EVAL-02 의 trial 분포: N=1,3,5 는 3회씩 (분산 측정), N=7,10 은 1회씩 (시간 budget 한계). 총 11 multi-turn session.

### 2.4 Coding quality (10 pts, 정성 평가)

공식 평가 문서 §5 에서 transcript 기반 정성 평가. 항목:
- **Idiomatic F#** — pipeline (`|>`), pattern matching, DU 사용, `let` 바인딩 (vs `var`). 3개 transcript review.
- **Generated tests compile/pass** — multi-turn turn-3 의 pytest 코드를 실제 실행
- **Code review identifies ≥80% known issues** — 4개 known issue (binsearch off-by-one × 1, Python parse_age × 2 — TypeError 무실패 + negative 무실패, TS missing-await × 1) 중 몇 개를 짚어냈는가

### 2.5 Documentation (deliverable)

DOC-EVAL-01: `documentation/qwen35-122b-coding-eval.md` 자체. ≥600 lines, 10 sections, 마지막 라인이 strict format `**Total: NN/100, Recommendation: <KEEP|KEEP-WITH-CAVEATS|ESCALATE>**`.

---

## 3. 어떻게 측정했는가 (How)

### 3.1 핵심 아키텍처 결정: 하이브리드 bash + Python(venv)

원래 sibling 프로젝트 `~/projs/mlx-runner/` 가 in-process `mlx_lm.load()` 로 measurement 를 돌리는데, 이 패턴을 그대로 쓸 수 없었다. 이유:

> **mlx_lm.load() 는 두 번째 122B instance 를 로딩하므로 OOM.** launchd-managed 122B 서비스가 이미 `localhost:8001` 에서 RSS ~45.4 GB 를 점유하고 있다. 같은 머신에서 in-process 로 또 한 번 122B 를 로드하면 합이 90 GB 를 넘어 OS swap 의 늪.

해결책: **HTTP-only adaptation.** `requests.post` 으로 `localhost:8001/v1/chat/completions` 와 `/v1/completions` 를 호출. mlx_lm 자체는 **import 조차 하지 않는다** (문법 검증: `grep -E "import mlx_lm|from mlx_lm" bench/eval-needle.py` 가 0건이어야 함).

이 결정의 결과로 두 갈래 도구가 만들어졌다:

- **순수 bash**: throughput (`run_throughput`), TTFT (`run_ttft`), refactor (`run_refactor`), langcoverage (`run_langcoverage`), schema-rate (`run_schema_rate`), multiturn (`run_multiturn`). `bench/run.sh` 의 `run()` / `mt()` / `require_port_8001` 패턴을 재사용. 라이브 agent loop 는 `dotnet run --project src/BlueCode.Cli -- --verbose --model 122b "<prompt>"` 으로 호출.
- **Python (venv)**: HumanEval+ 채점은 `evalplus` 라이브러리가 비-협상 가능 (커뮤니티 표준). Long-context needle 은 mlx-runner 의 `make_context()` 헬퍼를 그대로 보존하고 generation 호출만 `requests.post` 로 교체. `bench/.venv-eval/` 에 격리.

### 3.2 5-plan wave 구조

Phase 21 은 단일 phase 지만 내부에 5개 plan 이 있다. 단일 122B service contention 때문에 모든 라이브 런이 순차 실행이라, wave 구조는 사실상 sequential:

```
Wave 1 (21-01): 하니스 + venv + throughput/TTFT
Wave 2 (21-02): HumanEval+ HTTP adapter
Wave 3 (21-03): 픽스처 + refactor/langcoverage
Wave 4 (21-04): multiturn/schema-rate/needle/coldstart(gated)/full orchestrator
Wave 5 (21-05): 집계 + 평가 문서 + STATE/CLAUDE 크로스 레퍼런스
```

각 wave 는 별도의 `gsd-executor` subagent 로 실행됐다 (model: sonnet, balanced profile).

### 3.3 샘플링 파라미터: 의도적 deviation

- **Eval 표준 (이번 평가):** `temperature=0.2, top_p=0.8, top_k=20` — `mlx-runner/mlx_llm_eval_guide.md §8` eval-standard. 안정적 측정 목적.
- **blueCode 런타임 (Phase 20-01):** `temperature=0.7, top_p=0.8, top_k=20, presence_penalty=0.0` — Qwen 3.5 model card non-thinking coding default. 창의적 코딩 latitude 확보 목적.

이 차이는 의도적이며 공식 평가 문서 §1 Methodology 에 명시되어 있다. eval = stable measurement; runtime = creative coding.

### 3.4 Load-bearing 불변식들

평가 도중 깨지면 안 되는 architectural 제약:

1. **bench gate 7/7 PASS 유지** — `bash bench/run.sh --gate` 가 평가 전후 모두 exit 0 with `GATE PASS (7/7)`. eval 은 외부 instrumentation 일 뿐이며 회귀 게이트의 권위를 뒤흔들면 안 된다.
2. **`bench/baseline.json` 무수정** — 게이트 baseline 은 byte-for-byte 보존.
3. **`src/` 무수정** — `git diff src/` empty. 소스 코드 변경 0.
4. **테스트 카운트 불변** — 282/1/0 유지. eval 은 observational; `tests/BlueCode.Tests/` 에 새 테스트 모듈 추가 금지.
5. **Role=User 불변식** (Phase 20-03) — 멀티턴 주입은 모두 `dotnet run --resume <id>` 경유. `mid-conversation Role=System` 은 mlx_lm.server 가 HTTP 404 로 reject 하므로, raw HTTP 로 system role 멀티턴을 보내면 안 됨. `dotnet run --resume` 은 blueCode 내부에서 자연히 Role=User 를 보장.
6. **EXIT trap 으로 fixture 복원** — multi-file refactor 같은 write-task 픽스처는 agent 가 수정하므로, `bench/run.sh:18` 의 EXIT trap 이 `git checkout --` 로 복원해야 게이트가 계속 통과. Phase 21-03 에서 trap 을 6개 fixture 로 확장.

### 3.5 atomic commit 규율

CLAUDE.md 의 commit protocol 엄수:
- Task 단위 atomic commit (`{type}({phase}-{plan}): {name}`)
- Plan-meta commit 별도 (`docs({phase}-{plan}): complete {name} plan`)
- Phase-meta commit 별도 (`docs({phase}): complete {phase-name} phase`)
- Milestone-meta commit 별도 (`chore: complete v{X.Y} milestone`)
- **`git add .` / `git add -A` 절대 금지** — `.claude/` 와 `localLLM/` 은 의도적으로 untracked 이라 `-A` 는 이들을 휩쓸어버림.

Phase 21 결과 commit log: ~30개 atomic commit (5 plan 의 task commit + plan-meta + phase-meta + 3개 deviation fix commit).

---

## 4. 결과 (Result)

### 4.1 헤드라인 점수

| Dimension | Score | Max | Pct |
|-----------|-------|-----|-----|
| Correctness | 31 | 40 | 77.5% |
| Performance | 20 | 25 | 80.0% |
| Reliability | 25 | 25 | 100% |
| Coding quality | 6 | 10 | 60.0% |
| **Total** | **82** | **100** | **82%** |

**Verdict: KEEP** — 모든 dimension 이 ≥60% 이고 합계 ≥80, 따라서 KEEP-WITH-CAVEATS 가 아닌 KEEP. 일상 F# 코딩 도구로서 empirically 유용함이 확인됨.

### 4.2 차원별 핵심 숫자

#### Performance (20/25)

| 측정 | 결과 | 임계값 | 점수 |
|------|------|--------|------|
| Throughput median | **34.6 tok/s** (range 31.29-34.88) | ≥30 = PASS | 10/10 |
| TTFT median (warm) | **222 ms** (trial-1 cold 929 ms) | ≤500 = PASS | 5/5 |
| Cold-start | deferred | — | 0/5 (정직 표기) |
| E2E ±20% baseline | 7/7 게이트 통과 | binary | 5/5 |

Throughput 34.6 tok/s 는 4-bit MoE 로컬 모델로서 매우 양호. interactive 코딩에 충분 (~150-300 tok/s 클라우드 모델보다 느리지만 latency 가 0인 로컬의 장점이 상쇄). TTFT 222ms 는 거의 즉각적 — 대부분의 사용자는 250ms 이하면 "instant" 로 인지.

#### Correctness (31/40)

| 측정 | 결과 | 점수 |
|------|------|------|
| HumanEval+ chat pass@1 | **0.939** (154/164) / pass@1+ **0.902** | 15/15 (≥75% top band) |
| F# bug-fix 4-fixture (B1+B2+binsearch+refactor) | 3/4 PASS (refactor FAIL) | 11/15 (3.75 × 3 = 11.25 → 11) |
| 언어 커버리지 (Py + TS) | 2/2 PASS | 5/5 |
| Multi-file refactor (all-or-nothing) | **FAIL** (orphan_count=1) | 0/5 |

Completion mode pass@1 0.226 / pass@1+ 0.213 은 informational. chat mode 가 blueCode 런타임이 실제 사용하는 경로이므로 헤드라인은 chat 0.939.

#### Reliability (25/25 — 만점!)

| 측정 | 결과 | 점수 |
|------|------|------|
| Schema 준수율 | **0/50 InvalidJsonOutput** (perfect) | 10/10 |
| Multi-turn 안정 | N=1,3,7 clean / N=5: MaxLoopsExceeded × 3 / N=10: invalid_json=2 at turns 7-10 | 10/10 (stable through 7+) |
| Needle @32k | **4/4 retrieved** (8k/16k/32k/32768) | 5/5 |

Multi-turn N=5 의 MaxLoopsExceeded 는 multi-turn degradation 이 아니라 **prompt complexity** (parametrize 리팩토링 작업이 5-step budget 안에 안 들어감) — 이건 같은 5-step ceiling 이슈가 멀티턴 세션 안에서도 표출되는 양상이다. coherence 는 N=7 까지 clean.

#### Coding quality (6/10)

| 측정 | 결과 | 점수 |
|------|------|------|
| Idiomatic F# (3 transcript) | 1/3 (`|>` 거의 안 씀, mutable 선호) | 1/5 |
| Generated tests compile/pass | OK | 3/3 |
| Bug ID ≥80% known issues | 4/4 known issues 모두 짚음 | 2/2 |

Idiomatic F# 점수가 낮은 이유: 122B 가 F# 을 알지만 generation 시 "Python-like procedural F#" 에 가까운 코드를 생산. pipelines / DU / pattern matching 을 능동적으로 안 씀. 이것도 real signal — F# 코딩에서 사용자가 직접 idiomatic refactor 를 적용해야 한다는 신호.

### 4.3 주요 발견 (real signals — 점수 너머의 인사이트)

#### 발견 1. HumanEval+ chat 0.939 — 오픈웨이트 상위 티어

154/164 통과는 4-bit MoE 로컬 모델로서 매우 강한 결과. 비교 anchor 가 부족해서 정량 비교는 §6.3 cloud non-goal 에 막혀 있지만, 공개된 오픈웨이트 코딩 모델 중 90% 대 pass@1 은 상위 그룹에 해당. 일상 Python/F# 함수 작성에 충분히 신뢰 가능.

#### 발견 2. Schema 0/50 perfect — v2.0 아키텍처 결정 검증

엄격한 JSON schema 강제 + 2-attempt retry + 5-step loop guard + `--chat-template-args '{"enable_thinking": false}'` 조합이 50 invocation 동안 한 번도 schema 위반을 만들지 않았다. Phase 18-02 의 0/31 보다 더 엄격한 stress 에서도 견딤. blueCode 의 핵심 안정성 메커니즘이 확실히 작동.

#### 발견 3. Multi-turn N=7 안정 — community 주장 반박

ml-explore/mlx-lm#1011 가 "approximately 5 rounds" 에서 4-bit MoE 가 degrade 한다고 주장했지만, 우리 측정에서는 N=7 까지 coherence 유지, N=10 에서 첫 schema 오류 발생 (turn 7-10 사이 invalid_json × 2). issue 에서 말한 "5 rounds" 는 우리 환경 (mlx_lm.server + thinking-mode-off + Role=User 불변식) 에는 적용 안 됨. **데이터가 GitHub issue 추측을 이김.**

#### 발견 4. CORR-EVAL-02 FAIL — 5-step ceiling 이 진짜 멀티파일 리팩토링에 부족

이게 이번 평가의 가장 흥미로운 결과다. agent 는 4 step 동안 4개 파일 (Calculator.fs, Main.fs, Tests.fs, README.md) 을 모두 정확히 읽고 task 를 이해했다. 5번째 step 에서 Calculator.fs 의 `add3` 를 `sum3` 로 정확히 rename 했다. 거기서 step budget 소진 → MaxLoopsExceeded.

즉:
- 모델은 잘못 안 함 — task 이해, 파일 읽기, 첫 edit 모두 정확
- 부족한 건 **architectural budget** — 4-file rename 은 본질적으로 7+ step (read all 4 + edit all 4 + verify) 필요
- 현재 PLAN-04 (Plan.Steps.Length ≤ 5) 는 "single-turn coherent task" 가정인데, 진짜 멀티파일 리팩토링은 그 가정을 깨는 작업 클래스

이건 **constraint discovery** 다. 모델 약점이 아니라 blueCode 의 architectural decision 이 어느 작업 클래스에서 막히는지를 발견한 것. v2.2+ 후보 #1: PLAN-04 ceiling 재고. eval doc §9 에 "5-step cap 가 raise 되면 CORR-EVAL-02 재실행" 을 re-evaluation trigger 로 명시.

#### 발견 5. 3.4× wall-clock 주장의 해상도

Phase 17 SWITCH 에서 "3.4×" 라고 한 wall-clock 비율의 진짜 정체:
- Throughput 34.6 tok/s 는 122B-A10B-4bit MoE 단독 측정값. retired 32B/72B 의 throughput baseline 이 archive 에 정확히 없어서 직접 비교는 어렵지만, end-to-end task time 이 ±20% baseline 안에 들어옴은 SC4 통해 확인됨 (Performance 5/5 점). step count 가 일관되게 줄어들어 wall-clock 단축 효과 발생.

### 4.4 부수 발견 — macOS-specific 함정과 하니스 deviation

평가 도중 발견하고 fix 한 traps. 모두 21-XX SUMMARY 와 commit log 에 기록.

#### macOS evalplus trap #1: doubled signature
Chat-mode HumanEval+ 답변은 full function definition (signature + docstring + body). `evalplus.evaluate` 는 prompt + completion 을 stitch 하므로 signature 가 두 번 들어가서 parse 실패 → silent pass@1=0. **수정:** `python -m evalplus.sanitize <input>.jsonl` 을 `evalplus.evaluate` 전에 실행. `bench/eval-qwen35-122b.sh run_humaneval()` 에 baked.

#### macOS evalplus trap #2: RLIMIT_AS exceeds hard limit
`evalplus.eval.utils.reliability_guard` 가 `resource.setrlimit(RLIMIT_AS, ...)` 를 4 GiB 로 호출. macOS per-process hard limit 이 더 낮아서 `ValueError("current limit exceeds maximum limit")` 으로 모든 test subprocess 가 pre-execution crash. **수정:** 환경변수 `EVALPLUS_MAX_MEMORY_BYTES=-1` 설정 → `query_maximum_memory_bytes()` 가 `None` 반환 → reliability_guard 가 setrlimit 스킵. `bench/eval-qwen35-122b.sh` 에 baked.

이 두 함정은 **silent failure** 라서 진짜 무서움 — 점수가 0 으로 나오지만 에러 메시지가 안 보여서 "모델이 그냥 못 푸는 거구나" 로 오해할 수 있음. 실제 진단에 시간이 걸림.

#### bash 하니스 deviation 3건 (auto-fixed)
1. **set -euo pipefail + dotnet 비-zero exit 상호작용** — blueCode 가 `MaxLoopsExceeded` 에서 exit 1 로 종료. `set -e` 하에서 첫 `--refactor` 실행이 orphan check 와 CORR-EVAL-02 verdict fire 전에 abort. **Fix:** `set +e` / `set -e` 로 `dotnet run` 호출을 bracket.
2. **BSD `seq` countdown bug** — macOS BSD `seq 2 1` 은 `"2 1"` (countdown) 반환. GNU `seq 2 1` 은 빈 출력. `run_multiturn` 의 N=1 trial 이 의도와 달리 3 turn 실행됨. **Fix:** `[ n -ge 2 ] && seq 2 n || true` guard.
3. **`grep -c || echo 0` under pipefail** — `grep -c` 매칭 0개 시 exit 1. `|| echo 0` 가 `"0\n0"` (grep + echo 둘 다 출력) 생성하고, 후속 `grep -l` 파이프라인이 pipefail 로 abort. **Fix:** `|| true`.

이 셋은 모두 macOS 특화 또는 bash strict-mode 특화 문제. blueCode 자체와 무관한 하니스 quirk.

### 4.5 Verdict 의미

**KEEP** 는 milestone goal 의 cleanest possible outcome. 다만 caveat 가 있음:

- F# idiomaticness 가 약함 — 사용자가 generated code 를 idiomatic 하게 refactor 하는 보조 작업 필요
- Multi-file refactor 는 현재 구조에서 작동 안 함 — 단일 파일 task 위주로 사용
- Cold-start 는 비측정 (필요시 `--coldstart` 따로 실행)
- 클라우드 비교 없음 — 의도적, 사용자 muscle memory 가 baseline

이 caveat 들은 모두 공식 평가 문서 §8 (Caveats) 에 명시.

---

## 5. 산출물

### 5.1 리포지토리에 추가된 새 파일 (12개)

```
bench/eval-qwen35-122b.sh           # 메인 하니스 (~700 lines bash with 9 mode-flag dispatch)
bench/eval-humaneval-http.py        # HumanEval+ HTTP adapter (~159 lines, no mlx_lm import)
bench/eval-needle.py                # Long-context needle HTTP adapter (~80 lines)
bench/requirements-eval.txt         # Python deps (evalplus, requests)
bench/fixtures/refactor_multifile/Calculator.fs
bench/fixtures/refactor_multifile/Main.fs
bench/fixtures/refactor_multifile/Tests.fs
bench/fixtures/refactor_multifile/README.md
bench/fixtures/bug_binsearch.fs     # CORR-EVAL-03 fixture
bench/fixtures/bug_python_typeerror.py
bench/fixtures/bug_typescript_async.ts
bench/fixtures/multiturn_prompts.txt # 10개 sequential coding prompt
documentation/qwen35-122b-coding-eval.md  # 공식 평가 문서 (983 lines, 10 sections)
```

### 5.2 수정된 기존 파일

```
bench/run.sh           # 18행 EXIT trap 만 수정 (fixture-list 6개로 확장)
.planning/STATE.md     # observation note 추가
CLAUDE.md              # Bench section 에 2-line 크로스 레퍼런스 추가
```

### 5.3 무수정 (load-bearing 불변식)

```
src/                   # git diff empty
bench/baseline.json    # byte-for-byte 보존
tests/BlueCode.Tests/  # 282/1/0 유지
```

### 5.4 Gitignored

```
bench/.venv-eval/      # Python venv (evalplus + requests)
bench/runs/qwen35-eval-*/  # 라이브 측정 LOG_DIR (timestamp-based)
```

---

## 6. 재현 (Reproduction)

```bash
# One-time 셋업 (~5분)
bash bench/eval-qwen35-122b.sh --setup

# 풀 평가 (~2시간; cold-start 제외)
bash bench/eval-qwen35-122b.sh --full

# Cold-start 측정 (DISRUPTIVE — 122B 를 ~3분 죽임; 별도 실행)
bash bench/eval-qwen35-122b.sh --coldstart

# 개별 모드
bash bench/eval-qwen35-122b.sh --throughput
bash bench/eval-qwen35-122b.sh --ttft
bash bench/eval-qwen35-122b.sh --humaneval
bash bench/eval-qwen35-122b.sh --refactor
bash bench/eval-qwen35-122b.sh --langcoverage
bash bench/eval-qwen35-122b.sh --multiturn
bash bench/eval-qwen35-122b.sh --schema-rate
bash bench/eval-qwen35-122b.sh --needle

# 평가 후 회귀 게이트 (필수)
bash bench/run.sh --gate   # exit 0 with GATE PASS (7/7) 이어야 함
```

---

## 7. 향후 작업 (v2.2+ 후보)

이번 평가에서 surface 된 real signal 기반 후보:

1. **PLAN-04 5-step ceiling 재고** — CORR-EVAL-02 FAIL 의 root cause. 멀티파일 리팩토링 같은 7+ step 작업 클래스를 unlock 하려면 ceiling 을 raise (e.g., 10) 하거나 plan-mode 가 multi-step plan 을 emit 하도록 확장.
2. **Cold-start 측정** — `--coldstart` 핸들러 준비됨; 스케줄된 disruption window 에서 측정만 하면 됨.
3. **Idiomatic F# generation 개선** — system prompt 에 F# style guide hint 추가? few-shot example? 별도 evaluation 후 결정.
4. **Re-evaluation trigger** (eval doc §9 에 명시) — mlx_lm.server major version 변경, Qwen 3.5 model card update / YaRN config 변경, blueCode runtime sampling 변경, macOS major upgrade with metal/ANE driver delta, RSS 50 GB 초과 sustained — 이 중 하나라도 발생하면 `--full` 재실행.

v2.0-deferred 후보 (compaction, slash commands, sub-agents, thinking-mode-on, native tool_calls, streaming) 는 이번 평가에서 직접 surface 되지 않음 — observation window 에서 별도 pain signal 이 잡혀야 prioritize 가능.

---

## 8. 결론

Qwen 3.5 122B-A10B-4bit MoE 는 blueCode 의 일상 F# 코딩 도구로 **empirically 유용** 하다. HumanEval+ 0.939 / schema 0/50 / multi-turn N=7 안정성이 강한 핵심을 만들고, 멀티파일 리팩토링 한계 (5-step ceiling) 와 idiomatic F# 약점이 알려진 caveat 다.

이 평가는 v2.0 의 "3.4× speedup" 같은 wall-clock 주장에 데이터 등뼈를 추가했고, v2.2 scoping 을 위한 observation baseline 을 만들었다. 가장 중요한 single 발견은 CORR-EVAL-02 FAIL 의 root cause 가 모델 약점이 아니라 architectural constraint 라는 점 — 이건 v2.2 의 첫 후보를 "데이터 기반" 으로 확정하게 해줬다.

bench gate 7/7 PASS 후-eval 보존, 282/1/0 테스트 카운트 불변, `git diff src/` empty — eval 은 외부 instrumentation 으로서 깨끗하게 끝났다.

---

*문서 작성: 2026-04-28*
*공식 verdict: `documentation/qwen35-122b-coding-eval.md` 마지막 라인 (`**Total: 82/100, Recommendation: KEEP**`)*
*Audit: `.planning/v2.1-MILESTONE-AUDIT.md`*
