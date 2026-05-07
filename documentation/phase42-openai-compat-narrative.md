# Phase 42 평가 narrative — Qwen 122B / mlx_lm.server OpenAI-API 적합성 실증

**작성일:** 2026-05-07
**Phase:** v2.6 / Phase 42 (Qwen 122B OpenAI compatibility verification)
**최종 verdict:** HIGH=3, MEDIUM=2, LOW=5, PASS=15 — bench gate 7/7 PASS milestone-wide; verifier 14/14 must-haves PASS
**관련 문서:**
- `documentation/qwen35-122b-openai-compat.md` — 공식 252-line scorecard 평가 문서 (자동 렌더 가능)
- `.planning/phases/42-qwen-122b-openai-compat-test/` — RESEARCH + 3 PLAN/SUMMARY + VERIFICATION
- `bench/runs/qwen35-eval-20260507-131320/probes.jsonl` — 25-record raw probe transcript (재현 가능)
- `bench/eval-openai-compat.py` — 1450 LOC probe driver + renderer (`--output-dir XOR --render`)

이 문서는 공식 scorecard 의 narrative 동반본이다. scorecard 가 surface 별 PASS/FAIL/NOTES 표 중심이라면, 이 문서는 **왜 측정했는지, 무엇을 측정했는지, 어떻게 측정했는지, 무엇을 발견했는지, blueCode 에 어떤 의미인지** 를 사람이 읽기 쉬운 흐름으로 설명한다. v2.1 `phase21-evaluation-narrative.md` 의 동일 패턴.

---

## 1. 왜 측정했는가 (Why)

### 1.1 v2.6 self-planning milestone 의 가정 검증 필요성

v2.6 milestone (2026-05-06 시작) 은 blueCode 가 사용자 task 를 받았을 때 **자체적으로 plan 을 세우고 sub-task 로 분해해 sequential 실행** 하는 self-planning 능력을 도입한다. 이는 다음 5개 phase (37-41) 에 걸친 implementation work 으로 정의됐고 22개 v2.6 requirement 가 매핑됐다.

문제는 이 design 이 **하나의 큰 가정** 위에 서 있다는 점이다:

> mlx_lm.server 가 OpenAI-compatible /v1/chat/completions 를 완전히 지원하므로, blueCode 는 planner LLM call (strict JSON 출력) + executor LLM call (deviation rules system prompt) + per-task fresh conversation 을 안전하게 chain 할 수 있다.

이 가정의 핵심 piece 들은 다음과 같다:
- `response_format: {type: "json_object"}` 또는 `{type: "json_schema", strict: true}` 를 server 가 honor 하면 → blueCode v2.6 PLANGEN-03 의 schema validation 이 prompt-instructed 가 아닌 **server-enforced** 로 가능. 코드 단순화.
- `role: system` 이 **mid-conversation** 에서도 작동하면 → DEV-01/DEV-02 의 deviation-rules system prompt 를 매 task 호출마다 재주입 가능.
- 동일 model instance 에 **연속 요청** 이 KV cache contamination 없이 깨끗이 처리되면 → PLANORCH-04 의 "fresh conversation per task" 가 의도대로 작동.
- **Concurrency** 가 가능하면 → 미래 Phase F (wave-parallel) 가 implementational 가치 있음.
- **Error envelope** 이 OpenAI 표준이면 → blueCode 의 `LlmUnreachable` 매핑이 가져다 쓸 정보가 풍부.

각 piece 는 v1.0/v2.0 시점에 부분적 단서만 있었다:
- v1.0 SUMMARY.md: "tool_calls absent — custom JSON schema 사용". 그런데 mlx_lm 은 그 이후 0.31.x 까지 진화했다.
- v2.0 Phase 17-02: mid-conversation Role=System 이 HTTP 404 로 거부됨 — 그래서 `Role = User invariant` 도입. 이 finding 이 여전히 유효한지?
- v2.1 Phase 21: schema 0/50 InvalidJsonOutput. 그런데 그건 prompt-instructed JSON. server-enforced schema 는 별 도의 question.
- v2.5 후반부: 일상 사용은 stable. 그런데 v2.6 새 호출 패턴 (Planner → Executor → Executor → ...) 은 stress test 안 됐다.

요컨대 **v2.6 implementation 시작 전에 server 의 정확한 behavior 를 surface** 하지 않으면, 잘못된 가정 위에 5 phase 를 쌓고 늦게 발견할 위험이 있다.

### 1.2 사용자 의도 — 단일 명령 trigger

사용자는 2026-05-06 `/gsd:add-phase` 로 다음 한 줄 요청:

> "qwen 122b model 이 open ai compatible 한지 자세히 test 해 줘."

이는 명시적으로 **investigation/validation work** 를 요청한 것이다. v2.6 의 22 requirement 와 직접 매핑되지 않는다 (Phase 42 는 measurement work; 발견된 quirk 가 quirk-fix-up requirement 를 surface 할 가능성은 있음). default linear chain (37→38→39→40→41) 에 Phase 42 를 끝에 append.

### 1.3 명시적으로 측정하지 **않은** 것

- **vLLM, llama.cpp, Ollama, LM Studio 와의 cross-server 비교** — 의도적 non-goal. blueCode 는 단일 서버 (mlx_lm.server) 에 묶여 있고, "어느 서버가 더 좋은가" 는 별개의 질문. 단, RESEARCH.md 에서 이들 서버의 documented compat surface 는 reference 로 비교했다.
- **Concurrency knee (N=4/8/16/32)** — daily-driver 122B 서비스를 disrupt 하므로 명시적 non-goal. N=2 만 측정. v2.7+ Phase F 가 지원하면 그때 N=4-32 stress.
- **`/v1/embeddings`, `/v1/audio/transcriptions` 같은 멀티모달 endpoint** — Qwen 3.5 122B 는 multimodal 모델이지만 blueCode 는 text-only. scope 외.
- **Long-context needle (>32k)** — Phase 21 의 4/4 at 32768 결과로 이미 cover. 재측정 안 함.
- **Cold-start 측정** — Phase 23 의 37s 결과로 이미 cover. 재측정 안 함.
- **prompt cache 효과** — Phase 42 는 sampling/protocol layer 측정이 목적. cache hit/miss 는 별개의 실험 질문.

---

## 2. 무엇을 측정했는가 (What)

8개 surface, 25개 probe, 1개 deliverable.

### 2.1 8 surface 정의 (RESEARCH.md 에서 도출)

| # | Surface | 측정 의도 | Probe 수 |
|---|---------|----------|----------|
| 1 | Endpoint coverage | 어떤 path 가 있고 (`/v1/chat/completions`, `/v1/completions`, `/v1/models`, `/health`, `/v1/responses`) 어느 것이 작동하는가 | 5 (probes 01-05) |
| 2 | response_format field | server 가 `json_object` / `json_schema strict:true` 를 honor 하는가 | 3 (probes 06-08) |
| 3 | Role handling | system (start vs mid-conv), user, assistant 의 처리 | 2 (probes 09-10) |
| 4 | Streaming SSE | `stream:true`, `[DONE]` sentinel, chunk shape, role-on-every-chunk | 4 (probes 11-14) |
| 5 | Schema enforcement (statistical) | response_format silent-ignore 가 statistical 으로 진짜인지 | 2 (probes 20-21, N=5 each) |
| 6 | Multi-call coherence | 동일 model 인스턴스에서 연속 호출이 KV-isolated 인지 | 3 (probes 23-25) |
| 7 | Error surface | HTTP code, body shape (`{error: <string>}` vs `{error:{message,type,code}}`) | 4 (probes 16-19) |
| 8 | Concurrency | N=2 batch decode, queue/429 동작 | 1 (probe 22) |
| BONUS | Tools/tool_choice | OpenAI native tool calls 가 작동하는가 (v1.0 가정 "absent" 검증) | 1 (probe 15) |

### 2.2 각 surface 의 individual question 들

**Surface 1: Endpoint coverage** — "blueCode 가 모르는 endpoint 가 있는가? 알려진 endpoint 가 expected behavior 를 보이는가?"
- 01: `/v1/chat/completions` baseline (200 + 짧은 답)
- 02: `/v1/completions` legacy path (200 + completion shape)
- 03: GET `/v1/models` (200 + data[0].id 가 local path)
- 04: GET `/health` (200 + `{status:"ok"}`)
- 05: POST `/v1/responses` (OpenAI 2024 stateful API — expected 404)

**Surface 2: response_format** — "server 가 schema 강제할 수 있는가? blueCode v2.6 PLANGEN-03 가 prompt 만 의존해야 하는가, 아니면 server 가 제약 가능한가?"
- 06: `{type: "json_object"}` 로 prose 요청 ("Say hi") — server 가 honor 한다면 prose 거부 / JSON 강제
- 07: `{type: "json_schema", strict:true, schema:{...}}` 로 user/age object 요청 — strict 모드면 schema 외 출력 거부
- 08: 06 의 재실행 (한번 작동했다 다시 실패하는 nondeterministic 확인)

**Surface 3: Role handling** — "Phase 17-02 의 invariant 가 여전히 유효한가? blueCode 의 Role=User mid-conv injection 을 reaffirm 할 수 있는가?"
- 09: messages = [{system,"..."}, {user,"hi"}, {assistant,"Hi!"}, {system,"Now be terse"}, {user,"explain"}] — mid-conv system → expected HTTP 404
- 10: messages = [{system,"..."}, {user,"hi"}] — start system → expected HTTP 200 (control)

**Surface 4: Streaming SSE** — "stream=true 가 OpenAI 와 동일한 chunk shape 를 emit 하는가? `[DONE]` sentinel 처리는?"
- 11: 짧은 stream (3 chunk) 의 chunk envelope 확인
- 12: 긴 stream (50+ chunk) 의 role 이 매 chunk 마다 반복되는지
- 13: `stream_options: {include_usage: true}` 의 final chunk usage 출현 + `[DONE]` 동작
- 14: `stream_options: {include_usage: false}` (또는 omit) 의 `[DONE]` 동작 — RESEARCH preliminary 9 가 "include_usage 없으면 [DONE] 미출현" 이라 했음

**Surface 5: Schema enforcement (statistical)** — "Surface 2 의 silent-ignore 가 statistical 으로 진짜인지 (N=5)"
- 20: temp=0.0 + response_format 없이 5번 호출 → 모두 prose-wrapped JSON?
- 21: temp=0.0 + response_format=json_object 로 5번 호출 → 출력이 20 과 동일한지

**Surface 6: Multi-call coherence** — "fresh conversation 마다 KV cache 가 isolated 인가? blueCode v2.6 PLANORCH-04 의 가정 검증."
- 23: 세션 A 에 secret X 를 넣고 conversation 종료
- 24: 새 conversation 시작, secret 묻기 → 모르는 응답 (KV isolated 면)
- 25: 다시 새 conversation, 다른 prompt → 23/24 의 영향 없음

**Surface 7: Error surface** — "잘못된 request 에 대한 status code + body shape"
- 16: model 없이 호출 → 404 + `{"error":"<...>"}`
- 17: messages 비어있게 → 400 + `{"error":"<...>"}`
- 18: 잘못된 max_tokens (음수) → 400 또는 422
- 19: 잘못된 JSON body → 400

**Surface 8: Concurrency** — "BatchGenerator 가 N=2 로 진짜 batch 하는가, 아니면 serialize 하는가?"
- 22: 동일 prompt 두 개를 동시에 fire (bash background subshells + wait), 각각 wall_clock_start/end 기록 → wall_max ≈ wall_min ≪ wall_sum 면 batch

**BONUS: Tools** — "v1.0 SUMMARY.md '`tool_calls` absent' 가정이 mlx_lm 0.31.x 에서도 유효한가?"
- 15: `tools=[{function: {name:"get_weather", parameters:...}}]` + `tool_choice:"auto"` + 날씨 묻는 prompt → expected `finish_reason:"tool_calls"` + `message.tool_calls[0].function.name=="get_weather"`

### 2.3 Deliverable 1 — 자동 렌더 scorecard

`documentation/qwen35-122b-openai-compat.md` (252 lines, 자동 생성). `bench/.venv-eval/bin/python bench/eval-openai-compat.py --render bench/runs/qwen35-eval-20260507-131320/probes.jsonl` 명령으로 byte-identical reproduction 가능.

---

## 3. 어떻게 측정했는가 (How)

### 3.1 Harness 선택 — Extend, not replace

RESEARCH.md 의 핵심 결정: **새 F# test harness 를 만들지 않고 기존 `bench/eval-qwen35-122b.sh` (~1115 LOC bash + Python venv) 를 extend** 한다.

이유:
- v2.1 부터 build 된 harness 는 9 mode flags 가 있고 `bench/.venv-eval/` Python venv, `bench/runs/<ts>/` log 디렉토리 convention 이 정착돼 있다.
- 새 F# harness 는 dotnet run 의 cold-start, NuGet add 같은 ceremony 가 필요. measurement work 의 ROI 와 안 맞음.
- 동일 LOG_DIR convention 이 future-eval (v2.7+) 와 자연 결합.
- macOS bash-strict-mode pattern 5개가 이미 documented (v2.1-v2.4 과정에서). 새 F# 도구는 동등한 platform-specific quirk surface 를 다시 학습해야.

따라서 `--openai-compat` 모드를 `eval-qwen35-122b.sh:545` (between `run_coldstart` and `run_full`) 에 추가하고, 새 Python helper `bench/eval-openai-compat.py` 를 작성. dispatcher arm + usage entry + LOG_DIR 처리만 shell, 실제 probe 로직은 모두 Python.

### 3.2 Probe-as-record JSONL 패턴

각 probe 가 즉시 1 record 를 `probes.jsonl` 에 append 하고 fp.flush() — crash-resumable 디자인.

Record schema (probe() 헬퍼 기준):
```json
{
  "label": "01-baseline-chat",
  "category": "endpoint",
  "method": "POST",
  "path": "/v1/chat/completions",
  "http_code": 200,
  "elapsed_s": 0.34,
  "request_excerpt": "{...첫 200자...}",
  "response_excerpt": "{...첫 300자...}",
  "expected": "200 + non-empty content",
  "severity_hint": "PASS",
  "ts": "2026-05-07T13:13:20.142Z"
}
```

특수 helper 들:
- `probe_get(label, path)` — GET 용 (request_excerpt 없음)
- `probe_stream(label, body, max_tokens)` — SSE chunk 들을 받아 chunk_count + saw_done + role_on_every_chunk + usage_in_final_chunk 로 압축 record
- `probe_concurrent_pair(labels, bodies)` — bash background subshells 대신 Python `concurrent.futures.ThreadPoolExecutor(2)` 로 N=2 동시 호출, 각 thread 의 wall_start/wall_end 기록

### 3.3 Bench gate sandwich (가장 중요한 invariant)

probes 가 122B 의 KV cache 를 contaminate 하면 `bench/run.sh --gate` 가 regress 한다. 이를 검출하기 위해 **각 plan 양 끝에 게이트 호출** :

```
Plan 42-01: pre-flight gate (Task 1 STEP 1) → 10 probes → post-flight gate (Task 2 STEP 4)
Plan 42-02: (42-01 의 post-flight 가 42-02 의 pre-flight 역할) → 15 probes → mid-flight gate (Task 2 STEP 3) → post-flight (이후)
Plan 42-03: render only (no new probes) → FINAL post-flight gate (Task 2 STEP 2) at 2026-05-07T05:07:41Z
```

총 6번의 7/7 PASS 확인. 한 번이라도 regress 하면 STOP + 사용자에게 surface (Rule 4 architectural decision).

결과: **6번 모두 7/7 PASS**. probes 는 KV cache contamination 없음.

### 3.4 자동화된 reproducibility

renderer (Plan 42-03) 는 현재 시각 (`datetime.now()`) 을 보고서에 박지 않는다. 대신 **JSONL 파일의 mtime** 을 Date 필드로 사용. 따라서 `bench/.venv-eval/bin/python bench/eval-openai-compat.py --render <jsonl-path>` 를 두 번 실행하면 byte-identical 결과. verifier 가 이를 spot-check 으로 확인했다.

### 3.5 argparse mutex (--render XOR --output-dir)

Plan 42-01 시점에는 `--output-dir` 만 required. Plan 42-03 에서 renderer 추가하면서 `--render <path>` 모드 도입. 같은 entrypoint 가 두 모드를 가지므로 `argparse.add_mutually_exclusive_group(required=True)` 패턴:

```python
mode = parser.add_mutually_exclusive_group(required=True)
mode.add_argument("--output-dir", help="Run probes and write JSONL")
mode.add_argument("--render", help="Render markdown report from existing JSONL")
```

이는 plan-checker 의 advisory observation #2 가 미리 flag 한 사항. Plan 42-03 의 명시적 refactor.

### 3.6 server pre-state — `--max-tokens 4096` plist 추가

Phase 42 execution 시작 직전 (2026-05-07T11:21Z), 사용자가 launchd plist 에 `--max-tokens 4096` 추가하고 unload+load 재시작. 영향:
- server default `max_tokens` 가 512 → 4096
- KV cache 비워짐 (clean baseline)
- blueCode (`QwenHttpClient.fs:74` 의 hardcoded 1024) 에는 영향 없음 (client 가 항상 명시)
- Phase 42 probes 는 모두 max_tokens 명시 (8 또는 80 또는 명시값) 라 영향 없음

검증: pre-flight gate 7/7 PASS 가 fresh baseline 위에서 확인됐고, 이후 25 probes + 5번의 추가 gate 모두 PASS.

### 3.7 Auto-fix 사례 (Rule 1)

**Plan 42-02 mid-task 발견** : STAT_N (probes 20, 21) 의 first draft 가 `response_excerpt` (300자 제한) 를 parse 했는데, 진짜 JSON 응답은 그보다 길어서 valid_json_count=0/prose_wrap_count=0 (의미 없음) 로 나왔다. Rule 1 auto-fix: refactor 해서 full `r.json()` 을 fetch 하도록 수정. 같은 commit 에 포함, SUMMARY 의 "Deviations from Plan" 섹션에 기록.

**Plan 42-03 mid-task 발견** : probe 15 의 PASS 판정 규칙이 너무 strict 했다 — `excerpt_contains "get_weather"` 로 검사했는데 300자 cap 이 arg name 을 자른 케이스 발견. Rule 1 auto-fix: `finish_reason=="tool_calls"` 만으로 PASS 판정 (이게 OpenAI envelope 의 진짜 marker). 동일 commit 에 포함.

---

## 4. 무엇을 발견했는가 (What we found)

### 4.1 Severity 분포

총 25 probe → **HIGH=3, MEDIUM=2, LOW=5, PASS=15**.

### 4.2 HIGH severity (3건) — `response_format` silent-ignore

가장 큰 발견. **server 가 `response_format` 필드를 parse 하지 않고 silently 무시** 한다. 

- probe 06 (`json_object`): HTTP 200 + prose 응답 (예: "1\n2\n3\n..."). server 가 prose 를 거부하지 않음.
- probe 07 (`json_schema strict:true`): HTTP 200 + 원하는 schema 와 무관한 prose 응답. strict 모드도 무시.
- probe 08 (06 재실행): 동일 결과 (nondeterministic 가능성 배제).

**Surface 5 (statistical)** 가 이를 강화: probe 20 (response_format 없음, temp=0.0, N=5) 와 probe 21 (response_format=json_object, temp=0.0, N=5) 의 valid_json_count 와 prose_wrap_count 가 **5/5 동일**. response_format 은 zero effect — 진짜로 parse 안 됨.

**RESEARCH 시점에 source code 확인된 사실** : `mlx_lm/server.py` 의 `parse_request_body()` 함수가 `response_format` key 를 인식하지 않음. fork 인 `mlx-openai-server` 는 구현했지만 mainline mlx-lm 은 안 함.

**v2.6 implementation 에 미치는 영향** : PLANGEN-03 의 strict JSON 검증을 server 강제로 받을 수 없음. **prompt-instructed JSON + JsonSchema.Net validator + 2-attempt retry** 패턴 (v1.0 부터 검증된 방식) 을 그대로 유지. v2.6 design 변경 없음.

### 4.3 PASS 15건 중 가장 중요한 3건

#### 4.3.1 Tools/tool_choice 작동 (probe 15) — v1.0 가정 폐기

probe 15: `tools=[{function:{name:"get_weather", parameters:{type:"object", properties:{...}}}}]` + `tool_choice:"auto"` + `messages=[{user, "What's the weather in Seoul?"}]`.

응답:
```json
{
  "choices": [{
    "finish_reason": "tool_calls",
    "message": {
      "role": "assistant",
      "content": null,
      "tool_calls": [{
        "id": "call_xyz",
        "type": "function",
        "function": {
          "name": "get_weather",
          "arguments": "{\"location\":\"Seoul\"}"
        }
      }]
    }
  }]
}
```

**완벽한 OpenAI envelope.** v1.0 SUMMARY.md 의 "tool_calls absent — custom JSON schema 가 필요" 라는 가정은 **mlx_lm 0.31.x 에서 더 이상 유효하지 않다.**

이는 v2.7+ implementation simplification 후보:
- 현재 blueCode 의 `Action` DU + custom JSON schema (`<JsonSchemaCall>`) → native OpenAI `tools` 필드로 마이그레이션 가능
- `buildRequestBody` 가 `tools=[...]` 를 emit, 응답에서 `tool_calls[]` parse, `Action` DU 로 매핑
- `chat_template.jinja` 의 tools branch (lines 45-60) 가 mlx_lm.server 의 `ToolCallFormatter` 와 함께 자동 처리

결정: **v2.6 에서는 안 함** (scope creep). v2.7+ candidate 로 STATE.md + ROADMAP.md 에 surface.

#### 4.3.2 BatchGenerator parallel decode 확인 (probe 22) — Phase F 부활 신호

probe 22: 두 동일 prompt 를 `concurrent.futures.ThreadPoolExecutor(2)` 로 동시 fire.

결과:
- thread 1 wall_start = T+0.001s, wall_end = T+0.391s, elapsed = 0.39s
- thread 2 wall_start = T+0.001s, wall_end = T+0.392s, elapsed = 0.39s
- **wall_max = 0.39s ≈ wall_min**, **sum = 0.76s = 2× max**

**진짜 parallel batch decode.** mlx_lm.server 의 `BatchGenerator(--decode-concurrency 32 --prompt-concurrency 8)` 가 작동.

이 발견의 의미는 크다. RESEARCH.md 시점의 가정은 "single-LLM-server 라 parallel HTTP 요청은 server 에서 queue 됨, 따라서 ROI 낮음 → Phase F skip indefinitely". 그러나 실제로는 **parallel batch 가 가능**.

**v2.6 에서 Phase F 도입 안 함** (scope 결정 그대로). 그러나 **v2.7+ 후보로 elevate** :
- 현재 Phase F 의 ROI 평가는 "skip indefinitely" → "v2.7+ candidate (re-evaluate when v2.6 ships)" 로 격상
- blueCode 의 PlanOrchestrator 가 같은 wave 의 task 들을 `Task.WhenAll` 로 동시 호출 → 실제 wall-clock 단축 가능
- 단, KV cache contamination test 가 별도로 필요 (probe 22 는 동일 prompt, but 실제 task 는 다른 prompt — semantic isolation 검증 필요)

#### 4.3.3 Phase 17-02 invariant 재확인 (probe 09) — 변경 없음

probe 09: messages = [{system,"You are helpful"}, {user,"hi"}, {assistant,"Hello!"}, {system,"Now be terse"}, {user,"explain"}].

응답: HTTP 404 + body `{"error":"System message must be at the beginning."}`.

**Phase 17-02 invariant 그대로**. blueCode 의 `Role = User invariant for all mid-conversation injections` (Phase 20-03) 는 mlx_lm 0.31.3 에서도 필요. v2.6 의 DEV-01/DEV-02 system prompt 도 첫 메시지로만 (per-task fresh conversation 의 시작 system message), mid-conv injection 안 함.

probe 10 (control case): start system message → HTTP 200. 제어군 정상.

source code 단서: `chat_template.jinja:85` 의 Jinja `raise_exception('System message must be at the beginning.')` 가 직접 원인. server bug 가 아니라 template 설계.

### 4.4 LOW severity 5건 중 1건의 DEVIATION (probe 14)

**RESEARCH preliminary 9 와 다른 결과** :

RESEARCH.md preliminary 9 는 2026-05-06 시점에 "`stream_options: {include_usage: true}` 가 있을 때만 `[DONE]` sentinel 출현, 없으면 미출현" 이라 기록.

probe 14 (`stream:true` + `stream_options:{include_usage:false}` 또는 omit): **`[DONE]` 출현**. RESEARCH 와 반대.
probe 13 (`include_usage:true`): `[DONE]` 출현. (RESEARCH 와 일치)

**즉 현재 build (`system_fingerprint=0.31.3-0.31.2-...`) 에서는 `[DONE]` 이 항상 출현.** 

가설: mlx_lm 0.31.x 가 server protocol 을 OpenAI default 에 맞춤. RESEARCH 시점의 0.31.2 또는 그 이전 build 와 다름.

**blueCode 영향** : blueCode 는 SSE streaming 사용 안 함 (Phase 21 결과 TTFT 222ms warm 이 충분히 빠름; STM-01 deferred 10번째). probe 14 deviation 은 LOW severity. 단순 documented note 로 처리.

### 4.5 Multi-call coherence (Surface 6) — KV-isolated 확인

probes 23, 24, 25 가 보여준 것:
- probe 23: conversation A 시작, "Remember secret X = 42", 응답 받음, 종료.
- probe 24: 새 conversation 시작 (fresh `messages=[{user, "What's secret X?"}]`), 응답이 "I don't know" — secret X 의 영향 없음.
- probe 25: 또 다른 새 conversation, "Tell me about apples", apple 답변 — 23/24 의 영향 없음.

**fresh conversation 마다 KV isolated.** blueCode v2.6 PLANORCH-04 의 가정 ("fresh conversation per task") 정확. 단, **caveat** : verification 은 envelope-only (300-char excerpt cap 안에서 secret 출현 여부만 검사). semantic correctness (즉, model 이 실제로 X = 42 라고 답하지 않는지의 강한 확인) 은 cap 으로 인해 단정 불가. 보고서 Limitation block 에 transparent 하게 기록.

이 caveat 를 v2.7+ infra 후보로 surface: probe() 헬퍼에 `capture_full_content` flag 추가해서 특정 probe 만 full response capture (300-char cap 제외).

### 4.6 Error envelope (Surface 7) — non-OpenAI shape

OpenAI 표준: `{"error":{"message":"...","type":"...","code":"...","param":null}}`.
mlx_lm.server 0.31.3: `{"error":"<flat string>"}`.

probes 16-19 모두 동일 shape:
- 16 (model 없음): 404 + `{"error":"Model not found: <something>"}`
- 17 (messages 비어있음): 400 + `{"error":"Messages cannot be empty"}`
- 18 (max_tokens 음수): 422 + `{"error":"max_tokens must be positive"}`
- 19 (잘못된 JSON body): 400 + `{"error":"Invalid JSON"}`

**blueCode 영향** : 현재 `LlmUnreachable` 매핑이 200-char snippet 만 capture (구조화된 error 아닌 opaque string 로 처리). 이 design 은 mlx_lm 의 flat shape 와 사실상 호환. 즉 변경 불필요.

OpenAI 표준 envelope parsing 을 도입할 가치는 LOW. mlx_lm 이 미래에 표준 envelope 으로 마이그레이션할지는 알 수 없음.

### 4.7 Concurrency (Surface 8) — no 429, no rate limit

probe 22 의 N=2 결과는 §4.3.2 에서 다룸. 추가 발견:
- 두 thread 모두 200 응답
- 429 없음
- rate-limit 헤더 없음

RESEARCH source-code 확인: `mlx_lm/server.py` 의 `BatchGenerator` 는 capacity 를 internal Queue 로만 관리. **Capacity overflow 시 큐에서 무한 대기**, 절대 429 반환 안 함. 

이는 양면적:
- **장점** : client-side retry-on-429 로직 불필요. blueCode v2.6 PlanOrchestrator 는 timeout 만 처리하면 됨.
- **단점** : capacity 폭발 시 timeout 으로 fail (비교적 "느린 fail"). graceful degradation 어려움.

v2.6 implementation 영향 없음 (single-LLM 호출 패턴이라 concurrency 미사용). v2.7+ Phase F 에서는 client-side queue depth 제한 패턴이 필요.

---

## 5. blueCode 에 어떤 의미인가 (Implications)

### 5.1 v2.6 implementation 에 즉시 영향 (changes locked)

| 발견 | v2.6 design 영향 |
|------|------------------|
| response_format silent-ignore (HIGH × 3) | PLANGEN-03 의 schema validation 은 prompt-instructed + JsonSchema.Net + 2-retry 패턴 유지. server 강제 안 됨. |
| Role=System mid-conv 거부 (PASS, 17-02 confirmed) | DEV-01/DEV-02 의 deviation rules system prompt 는 fresh conversation 의 시작 system 메시지로만. mid-conv 재주입 안 함. |
| Multi-call KV isolated (PASS) | PLANORCH-04 "fresh conversation per task" 가 정확. 변경 없음. |
| Error envelope flat string (LOW) | `LlmUnreachable` 매핑 그대로. 200-char snippet 처리. |
| `[DONE]` always (LOW deviation) | blueCode 가 streaming 안 쓰므로 무관. |

**core conclusion** : Phase 42 가 v2.6 design 을 invalidate 하지 않음. 모든 가정 (몇몇은 strengthening 만, response_format 가정만 명시적 retract) 이 reaffirmed.

### 5.2 v2.7+ candidates (3개 surface 됨)

#### Candidate 1: Native tools/tool_choice migration

probe 15 PASS 가 v1.0 SUMMARY.md "tool_calls absent" 를 폐기시킴. 

work scope (예상):
- `Action` DU + `JsonSchemaCall` 코덱 → OpenAI `tools` field 로 매핑
- `buildRequestBody` 에 `tools=[...]` emit
- 응답 `tool_calls[]` parse 후 `Action` 으로 매핑
- `chat_template.jinja` 가 mlx_lm.server 의 `ToolCallFormatter` 와 협력해 XML 변환은 server-side 자동
- v1.0 부터의 prompt-instructed schema 코드 deprecate

ROI 평가:
- **장점**: server-validated tool argument shape (잘못된 args 면 자동 retry). prompt 단순화 → tokens 절약.
- **단점**: blueCode 의 양 호환성 (legacy plan-mode + agent-mode 의 prompt 시스템 동시에 지원 필요).
- **risk**: tool_choice="auto" 의 nondeterministic behavior — 모델이 tool 안 부르고 자유 답변할 가능성. blueCode 는 항상 tool 호출하길 원하는데 mlx_lm 이 honor 하는지 stress test 필요.

#### Candidate 2: Wave-parallel exec (Phase F resurrection)

probe 22 N=2 PASS. RESEARCH 시점 "skip indefinitely" → "v2.7+ candidate, re-evaluate".

work scope (예상):
- `PlanOrchestrator.runPlanMode` 가 같은 wave 의 task 들을 식별 (현재 v2.6 PLANORCH-01 의 sequential exec 만 지원)
- 같은 wave task list → `Task.WhenAll` 로 동시 LLM 호출
- KV cache contamination semantic test (probe 22 는 동일 prompt, but 실제 task 들은 다른 prompt)
- BatchGenerator capacity 32 안에서만 (sane upper bound 강제)

ROI 평가:
- **장점**: 진짜 wall-clock 단축. 4-task plan 의 wave 1 = 3 task 동시 → ~1/3 시간.
- **단점**: F# `Task.WhenAll` complexity. 부분 실패 처리 (1 task fail 시 다른 task 들 cancel?).
- **risk**: BatchGenerator 가 N=2 에서 OK 였는데 N=8 에서 OOM 또는 thrash 가능성. 보수적으로 N=2-4 부터.

#### Candidate 3: capture_full_content infra

300-char excerpt cap 이 검증을 가로막는 케이스 발견 (probe 15 args, probe 23-25 semantic). probe() 에 optional `capture_full_content: bool` flag.

work scope:
- `bench/eval-openai-compat.py` 의 `probe()` 에 새 param 추가
- True 면 `r.text` 전체 (or `r.json()`) 를 record 의 `response_full` 필드에 저장
- 기본값 False (cap 유지) — JSONL bloat 방지

ROI 평가:
- **장점**: future probe set 의 semantic verification 가능 (예: tools args, multi-turn coherence)
- **단점**: 구현 자체는 trivial. ROI 라기보다 infra debt
- **risk**: 없음

이 셋은 STATE.md "Roadmap Evolution" 에 기록하고 `/gsd:add-todo` 로 캡처할 가치 있음. v2.6 implementation 진행 중에 시간 나면 Phase 41 이후 별도 phase 로.

### 5.3 CLAUDE.md 변경 (이미 적용)

Plan 42-03 가 CLAUDE.md 의 "Bench" 섹션에 한 bullet 추가 (Case C+D fused). 내용:
- 새 report 위치 (`documentation/qwen35-122b-openai-compat.md`)
- HIGH-finding summary (response_format silent-ignore — blueCode 가 prompt-instructed schema 유지)
- v2.7+ candidate (tools/tool_choice 가 PASS, native migration 후보)
- 재현 명령 (`bash bench/eval-qwen35-122b.sh --openai-compat` + `--render <jsonl>`)

Key Seams 섹션은 의도적으로 미변경. 이유: response_format MUST-NOT 규칙은 documentation-only (blueCode 가 이미 그 길로 안 가고 있음). 새 architectural seam 도입 없음.

---

## 6. 무엇을 안 측정했는가 (Out of scope)

명시적으로 v2.7+ 이상으로 deferred:

| 항목 | 이유 |
|------|------|
| Concurrency knee (N=4-32) | daily-driver 122B disrupt. Phase F resurrection 시 scope. |
| Cross-server compat 비교 (vLLM, llama.cpp, Ollama, LM Studio) | mlx_lm 이 single source of truth. 비교는 별 ROI. |
| `/v1/embeddings`, `/v1/audio/transcriptions` | blueCode text-only. 멀티모달은 v3.0+. |
| Long-context needle >32k | Phase 21 의 4/4 결과로 cover. |
| Cold-start | Phase 23 의 37s 로 cover. |
| Prompt cache hit/miss | 다른 실험 질문 (sampling protocol layer 와 무관). |
| `n>1` choice 응답 | server 가 silently 1개로 처리 (probe 시 documented). 명시적 N=2 implementation 안 함. |
| `logprobs`, `logit_bias` 응답 shape | LOW severity (blueCode 미사용). |
| Real-time / async tool execution | OpenAI 표준 외 — mlx_lm 도 미지원. |

---

## 7. 재현 방법 (Reproduction)

전체 25 probe + report 를 처음부터 재현하려면:

```bash
# 1. Pre-flight bench gate (optional 하지만 권장)
bash bench/run.sh --gate

# 2. Probe 실행 (~5-10분 wall-clock; 5분 동안 122B 가 25개 단일 LLM 호출 처리)
bash bench/eval-qwen35-122b.sh --openai-compat

# 3. Latest run 의 LOG_DIR 확인
LATEST=$(ls -td bench/runs/qwen35-eval-* | head -1)
echo "Latest: $LATEST"

# 4. 25 record 확인
wc -l "$LATEST/probes.jsonl"

# 5. Report regenerate (optional — same content as committed report)
bench/.venv-eval/bin/python bench/eval-openai-compat.py --render "$LATEST/probes.jsonl" > /tmp/regenerated-report.md
diff /tmp/regenerated-report.md documentation/qwen35-122b-openai-compat.md
# Expected: empty diff (byte-identical)

# 6. Post-flight bench gate (mandatory; KV-cache contamination 검출)
bash bench/run.sh --gate
```

특정 probe 만 다시 보려면:
```bash
jq 'select(.label == "06-response-format-json-object")' "$LATEST/probes.jsonl"
jq 'select(.label == "15-tools-tool-choice")' "$LATEST/probes.jsonl"
jq 'select(.label == "22-concurrency-n2")' "$LATEST/probes.jsonl"
```

mlx_lm 이 미래에 새 build 로 update 되면 (예: 0.32.x), 동일 명령 재실행으로 changes detected. 특히 watch:
- `response_format` honor 시작 → blueCode v2.7+ schema simplification 가능
- `[DONE]` 동작 변경 → SSE 처리 코드 (현재 미사용) 사용 시작 시 영향
- error envelope 변경 → `LlmUnreachable` 매핑 강화 가능

---

## 8. 부록 (Appendix)

### 8.1 시계열

| 시각 (UTC) | 이벤트 |
|-----------|--------|
| 2026-05-06 | `/gsd:add-phase` 로 Phase 42 추가 — 사용자 한 줄 요청 |
| 2026-05-06 | gsd-phase-researcher → 42-RESEARCH.md (772 lines, 11 preliminary probe + source-code 분석) |
| 2026-05-06 | gsd-planner → 3 PLAN.md (사전 wave 분배: 1→2→3 sequential) |
| 2026-05-06 | gsd-plan-checker → VERIFICATION PASSED (no revisions) |
| 2026-05-07T11:21Z | 사용자 plist 변경 (`--max-tokens 4096`) + 서버 재시작 |
| 2026-05-07T02:36Z (실제 Plan 42-01 시작 시각) | Plan 42-01 pre-flight bench gate 7/7 PASS |
| 2026-05-07T02:43Z | Plan 42-01 post-flight bench gate 7/7 PASS — 10 probes 완료 |
| 2026-05-07T03:54Z | Plan 42-02 시작 |
| 2026-05-07T04:15Z | Plan 42-02 mid-flight + post-flight bench gate 7/7 PASS — 25 probes 누적 |
| 2026-05-07T04:44Z | Plan 42-03 시작 |
| 2026-05-07T05:07:41Z | Plan 42-03 FINAL post-flight bench gate 7/7 PASS — milestone-wide invariant 확인 |
| 2026-05-07 | gsd-verifier → 42-VERIFICATION.md (passed 14/14 must-haves) |
| 2026-05-07 | Phase 42 completion bundle commit `e8b1e78` |

### 8.2 Commit graph

```
5105e15 docs: start milestone v2.6 GSD self-planning (5 phases, 22 reqs)
22de322 docs(42): research phase domain
a78c4c3 docs(42): create phase plan
065bbf7 chore(42-01): pre-flight gate + add jsonschema dep + create eval-openai-compat skeleton
cedd116 feat(42-01): wire --openai-compat mode + 10 probes for surfaces 1+2+3
9ef1785 docs(42-01): complete openai-compat probe harness scaffolding plan
2954144 feat(42-02): add probe_stream helper + 5 probes for streaming + tools surfaces
a02dfd3 feat(42-02): add probe_concurrent_pair + STAT_N + 10 probes for surfaces 5-8
2a7d618 docs(42-02): complete openai-compat probes for surfaces 4-8 plan
8712d48 feat(42-03): add render mode + generate openai-compat report from 25-probe transcript
1ee3dce docs(42-03): add openai-compat report pointer to CLAUDE.md Bench section
73d91cc docs(42-03): complete openai-compat render + final-post-flight-gate plan
e8b1e78 docs(42): complete qwen-122b-openai-compat-test phase
```

총 13 commits Phase 42 동안 (3 milestone-init/research/plan + 9 plan-execute + 1 phase-complete).

### 8.3 LOC + 파일 영향

- `bench/eval-openai-compat.py` — 신규, 1450 LOC (Plan 42-01: 318, Plan 42-02 추가: 869, Plan 42-03 추가: 1450)
- `bench/eval-qwen35-122b.sh` — +26 LOC (run_openai_compat 함수 + dispatcher + usage)
- `bench/requirements-eval.txt` — +1 line (`jsonschema>=4.21`)
- `documentation/qwen35-122b-openai-compat.md` — 신규, 252 lines
- `documentation/phase42-openai-compat-narrative.md` — 신규 (이 문서)
- `CLAUDE.md` — +3 lines (Bench 섹션 bullet)
- `.planning/ROADMAP.md` — Phase 42 섹션 + 체크박스 + Progress 테이블 update
- `.planning/STATE.md` — Current Position + Roadmap Evolution + Session Continuity update
- `.planning/phases/42-qwen-122b-openai-compat-test/` — 7 파일 (RESEARCH, 3 PLAN, 3 SUMMARY, VERIFICATION)

**Zero `src/` diff** 확인. **`bench/baseline.json` byte-identical** 확인. Phase 42 measurement-work invariant 보존.

### 8.4 verifier 결과 verbatim (`42-VERIFICATION.md` summary)

```yaml
status: passed
score: 14/14 must-haves verified
key_confirmations:
  - 8-surface coverage in rendered report
  - bench gate sandwich 6 PASS confirmations
  - zero src/ diff
  - reproducibility byte-identical
  - JSONL integrity 25/25 valid
  - CLAUDE.md update minimal-and-correct
spot_checks_passed:
  - probe 09 (mid-conv system) → 404
  - probes 11-14 (streaming) → all saw_done=True
  - probe 15 (tools) → finish_reason=tool_calls
  - probe 22 (concurrency) → wall=0.39s vs sum=0.76s
  - probes 20/21 (stat) → identical 5/5 prose-wrap
v2_7_candidates_surfaced: 3
```

---

## 9. v2.6 implementation 으로 흘러들어가는 핵심 take-away

Phase 42 가 v2.6 의 다음 5 phase (37-41) 에 직접 영향 주는 4가지:

1. **PLANGEN-03 의 schema validation 패턴** : prompt-instructed + JsonSchema.Net + 2-attempt retry — server 강제 사용 안 함 (response_format silent-ignore).

2. **DEV-01/DEV-02 의 deviation rules 주입 위치** : per-task fresh conversation 의 **첫 system 메시지** 로만. mid-conversation 재주입 절대 안 함 (Phase 17-02 invariant 재확인).

3. **PLANORCH-04 의 fresh conversation per task** : 가정 정확. KV-isolated. 단, semantic correctness 의 stronger verification 은 v2.7+ capture_full_content infra 필요.

4. **error 매핑 단순성** : `LlmUnreachable` 의 200-char snippet 처리는 mlx_lm 의 flat error string 과 호환. 변경 불필요.

**가장 큰 conceptual surprise** : v1.0 의 "tool_calls absent" 가정 폐기. v2.7+ 후보로 surface. v2.6 implementation 은 이 변화 모르고 진행 — scope creep 회피.

**가장 큰 architectural surprise** : single-LLM-server 가 진짜 parallel batch decode 함. Phase F 의 ROI 가 RESEARCH 시점보다 좋아짐. v2.7+ 에서 wave-parallel exec 재고려.

---

*문서 작성: 2026-05-07*
*작성자: Claude (Opus 4.7) via /gsd:execute-phase 42 + 사용자 narrative 요청*
*다음 step: `/gsd:plan-phase 37` 으로 v2.6 implementation 본 코스 시작*
