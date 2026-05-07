# Qwen 122B OpenAI-API gap 보완 — Router 도입 가능성 분석

**작성일:** 2026-05-07
**소스:** Phase 42 empirical findings (`documentation/qwen35-122b-openai-compat.md` + `phase42-openai-compat-narrative.md`)
**Phase:** v2.6 milestone, post-Phase-42 architectural consideration
**관련 문서:**
- `documentation/qwen35-122b-openai-compat.md` — 252-line scorecard (HIGH=3 / MEDIUM=2 / LOW=5 / PASS=15)
- `documentation/phase42-openai-compat-narrative.md` — 592-line narrative companion
- `bench/runs/qwen35-eval-20260507-131320/probes.jsonl` — 25-record raw transcript
- `bench/eval-openai-compat.py` — probe driver + renderer

---

## 0. 질문 정의

> "Qwen 35-122B 가 OpenAI 에 compatibility 를 하려면 router 가 이를 보완해 줄 수 있나?"

**Router** = mlx_lm.server (8001) 와 client (blueCode 또는 그 외) 사이에 reverse-proxy 로 끼어드는 process. 들어오는 OpenAI-style request 를 mlx_lm 이 이해할 수 있게 재작성하고, 나가는 mlx_lm response 를 OpenAI-style 로 정규화. 클라이언트 입장에서는 native OpenAI API 를 쓰는 것처럼 보임.

이 문서는 다음 4가지에 대해 답한다:
1. **무엇을 보완해야 하나** — Phase 42 가 발견한 gap 7개의 정리
2. **각 gap 이 router-compensable 인가** — 가능 / 불가능 / 부분 가능 분류
3. **architectural option 5가지** — sidecar Python proxy / F# native / 기존 fork 채택 / nginx+Lua / Ollama wrapping
4. **blueCode 에 도입할만한가** — ROI 분석 + 추천 verdict + implementation sketch (도입 시)

---

## 1. 무엇을 보완해야 하나 (Gap recap)

Phase 42 가 발견한 **7개의 OpenAI-spec divergence** 를 router 가 다룰 수 있는 후보로 정리.

### 1.1 HIGH severity — `response_format` silent-ignore (3건)

| Probe | 요청 | OpenAI 표준 동작 | mlx_lm 0.31.3 실측 |
|-------|------|----------------|------------------|
| 06 | `response_format: {type: "json_object"}` | server 가 출력을 valid JSON 으로 강제 (또는 `invalid_response_format` 에러) | **silently ignored** — server 가 prose 응답 emit, HTTP 200 |
| 07 | `response_format: {type: "json_schema", strict: true, schema: {...}}` | server 가 schema 강제 (constrained decoding 또는 post-validate) | **silently ignored** — schema 무관 prose 응답 |
| 08 | 06 재실행 (nondeterministic 가능성 배제) | 06 과 동일 | 06 과 동일 — silent-ignore 가 deterministic |

**source code 진단** (RESEARCH.md §B): `mlx_lm/server.py` 의 `parse_request_body()` 가 `response_format` key 를 인식하지 않음. fork `mlx-openai-server` 는 구현했지만 mainline mlx-lm 안 함.

### 1.2 MEDIUM severity (2건)

| Issue | OpenAI 표준 | mlx_lm 실측 |
|-------|-----------|------------|
| 멀티-`n` choice | `n: 3` 요청 시 3개 choice 반환 | silently `n=1` 반환 |
| `logprobs` shape | OpenAI 표준 응답 shape | server.py 에 implemented 됐으나 응답 shape 검증 안 됨 (Open Question 5) |

### 1.3 LOW severity / deviations (5건)

| Issue | OpenAI 표준 | mlx_lm 실측 |
|-------|-----------|------------|
| Error envelope shape | `{"error":{"message","type","code","param"}}` | flat `{"error":"<string>"}` |
| `[DONE]` SSE sentinel (probe 14) | spec varies; latest = always emit | always emit (RESEARCH preliminary 9 와 차이; 현재는 conformant) |
| `role:"assistant"` on every SSE chunk | first chunk 만 | every chunk 에 반복 |
| `/v1/responses` (OpenAI 2024 stateful API) | 200 + responses object | 404 (endpoint 없음) |
| Capacity overflow / no 429 | 429 + retry-after | indefinite queue, never 429 |

### 1.4 Template-level (server-level 아님)

| Issue | OpenAI 표준 | Qwen 실측 |
|-------|-----------|----------|
| Mid-conv `role: system` | 허용 (transcript 어디든 system 메시지 가능) | HTTP 404 — `chat_template.jinja:85` 의 Jinja `raise_exception('System message must be at the beginning.')` |

이건 server bug 가 아니라 **모델 자체** 의 chat template 설계. mlx_lm 도 어쩔 수 없음. router 관점에서는 request 재작성으로 해결 가능 (mid-conv system → user role with `[SYSTEM]` prefix).

### 1.5 Conformant (이미 PASS — router 보완 불필요)

| Feature | mlx_lm 동작 |
|---------|------------|
| `tools` / `tool_choice` | OpenAI envelope 완전 conformant (`finish_reason: tool_calls`, `message.tool_calls[]`) |
| `BatchGenerator` parallel decode | N=2 에서 진짜 batch (wall=max ≪ sum) |
| Multi-call KV isolation | 새 conversation = fresh KV state |
| `/v1/chat/completions` baseline | OpenAI envelope 정확 |
| `/v1/models` GET | OpenAI shape |
| Streaming chunk 구조 | OpenAI shape (role-on-every-chunk 만 minor diff) |

---

## 2. 각 gap 의 router-compensable 분석

각 gap 에 대해 "router 가 이걸 어떻게 다룰 수 있는가?" 를 case-by-case 로.

### 2.1 `response_format: json_object` (HIGH × 1)

**Router 전략 A — Prompt rewrite + post-validate (faithful):**
```
1. Request 가 response_format.type == "json_object" 면
2. Last user message 또는 system message 끝에 instruction 추가:
   "Output ONLY valid JSON. No prose, no explanations."
3. mlx_lm 으로 forward
4. 응답 받으면 content 가 parseable JSON 인지 검증 (json.loads)
5. 실패 시:
   a. 같은 request 재시도 (with stronger instruction)
   b. 또는 brace-counting extraction (blueCode QwenHttpClient 의 3-stage extractor 와 동일)
   c. 또는 OpenAI 표준 에러 반환: `{"error":{"message":"Could not produce valid JSON","type":"invalid_response_format"}}`
```

**Compensable?** ✓ YES — 실용적으로 가능
**Cost:** Low — prompt 수정 + 1번 validate. retry 시 N×latency.
**Caveat:** *constrained decoding* (decoding 단계에서 token 단위 제약) 은 router 단에서 불가능. 이건 inference engine 안에서만 가능 (outlines, lm-format-enforcer, mlx_lm fork). prompt-instructed 는 model 이 따를지 nondeterministic.

**현재 blueCode 동작:** 이미 client-side 에서 동등한 처리 — `QwenHttpClient.fs` 의 3-stage JSON extractor (bare → brace-nested → fence-strip) + 2-attempt retry. router 가 도입하면 client 코드 단순화 가능.

### 2.2 `response_format: json_schema strict:true` (HIGH × 2)

**Router 전략 B — Schema-instructed prompt + jsonschema validate (faithful):**
```
1. Request 가 response_format.type == "json_schema" 면
2. Schema 를 prompt 로 inject:
   "Output JSON matching exactly this schema:
   {schema_as_json}
   No additional fields, no prose."
3. mlx_lm forward
4. 응답 받으면 jsonschema.validate(response_json, schema)
5. 실패 시:
   a. retry with violation message in prompt
   b. 또는 OpenAI 표준 에러
```

**Compensable?** ✓ YES — 실용적으로 가능
**Cost:** Medium — schema 가 prompt 토큰 차지 (큰 schema 면 100s of tokens). validation 자체는 fast.
**Caveat:** OpenAI 의 `strict: true` 는 server 가 token-level 강제 (constrained decoding). router 는 post-validate 만 가능 — 모델이 schema 어기면 retry 만 가능, 진짜 강제 X.

**현재 blueCode 동작:** v2.6 PLANGEN-03 가 `JsonSchema.Net` validator + 2-retry 패턴 — 이미 동등한 보완.

### 2.3 Error envelope shape (LOW × 1)

**Router 전략 C — Response rewrite (trivial):**
```
1. mlx_lm 응답이 non-200 이면
2. body 를 parse 시도 ({"error": "<string>"})
3. OpenAI shape 로 wrap:
   {
     "error": {
       "message": <original_string>,
       "type": <inferred_from_status_code>,
       "code": null,
       "param": null
     }
   }
4. status code → type 매핑:
   400/422 → "invalid_request_error"
   401     → "authentication_error"
   404     → "not_found_error"  
   429     → "rate_limit_error"
   500/502 → "server_error"
   503     → "service_unavailable_error"
```

**Compensable?** ✓ YES — trivial
**Cost:** Negligible
**Value:** OpenAI SDK 가 error 를 structured 로 dispatch. blueCode 처럼 flat string 만 처리하는 client 는 무관.

### 2.4 `n > 1` choice (MEDIUM × 1)

**Router 전략 D — Fan-out parallel (expensive):**
```
1. Request 의 n: K 가 1 이상이면
2. Request 에서 n 제거 + 동일 body 로 K 개 병렬 호출 (BatchGenerator 가 N≤32 까지 batch)
3. 결과 K choices 를 aggregate:
   {
     "choices": [
       {response_1},
       {response_2},
       ...
       {response_K}
     ]
   }
4. usage 합산 (prompt_tokens 는 K 번 등장 → K×, completion_tokens 는 sum)
```

**Compensable?** ✓ YES — 가능
**Cost:** High — K× compute. 단, BatchGenerator 가 batch 하므로 wall-clock 은 K× 되지 않음 (probe 22 PASS 결과 적용 — N=2 에서 wall ≈ 1×, sum 만 K×).
**Caveat:** mlx_lm 의 capacity (decode_concurrency 32) 를 넘으면 queue. `n=33` 같은 요청은 시간 폭주.

### 2.5 Mid-conv `role: system` (template-level)

**Router 전략 E — Request rewrite (low-cost):**
```
1. Request messages[] 를 walk
2. 두 번째 이상의 system 메시지 발견 시:
   a. 그 자리의 system 메시지를 user role 로 변환
   b. content 앞에 "[SYSTEM HINT] " prefix 추가
   c. 또는 first system message 에 concat (긴 system prompt 됨)
3. mlx_lm 으로 forward (이제 system 은 첫 번째에만)
```

**Compensable?** ✓ YES — request 단순 재작성
**Cost:** Negligible
**Caveat:** blueCode 가 이미 동일 처리 — Phase 17-02 + 20-03 의 `Role = User invariant for all mid-conversation injections`. router 가 이걸 client 에서 lift 해 가져갈 수 있음.

### 2.6 Capacity overflow / 429 emulation

**Router 전략 F — In-memory capacity tracking:**
```
1. Router 가 active request count (in-flight) 를 track
2. New request 도착 시:
   a. count < threshold (예: 32) → forward
   b. count ≥ threshold → 즉시 429 + retry-after 헤더로 응답
3. Response 받으면 count 감소
```

**Compensable?** ✓ YES — pure proxy state
**Cost:** Low — 단순 counter
**Value:** OpenAI SDK 의 retry-on-429 backoff 가 작동. blueCode 처럼 timeout 만 처리하는 client 는 무관.

### 2.7 `/v1/responses` (OpenAI 2024 stateful API) — 404

**Router 전략 G — 503 rewrite or stub:**
```
1. /v1/responses 요청 도착
2. 옵션 a: 503 Service Unavailable 반환 + "stateless backend; use /v1/chat/completions"
3. 옵션 b: stateful conversation 을 router 가 직접 관리 (request_id → conversation history map). client 가 parameter `previous_response_id` 보내면 router 가 history 합쳐서 chat/completions 로 forward
```

**Compensable?** 부분 가능
- (a) status code rewrite — trivial
- (b) full stateful API emulation — 복잡 (state store 필요, eviction 정책, persistence 결정)

**Cost:** (a) negligible, (b) high (sidecar redis 또는 sqlite 필요)
**Value:** 거의 없음 — OpenAI 의 responses API 도 새 표준이라 client 호환성 미미.

### 2.8 종합 Compensation matrix

| Gap | Compensable? | Strategy | Cost | blueCode 가치 | 일반 가치 |
|-----|--------------|----------|------|--------------|----------|
| response_format json_object | ✓ Yes (prompt+validate) | A | Low | Low (이미 client) | **High** |
| response_format json_schema | ✓ Yes (schema-prompt+validate) | B | Medium | Low (v2.6 처리) | **High** |
| Error envelope | ✓ Yes (response rewrite) | C | Negligible | Negligible | Medium |
| `n > 1` | ✓ Yes (parallel fan-out) | D | High (K× tokens) | Negligible | Medium |
| Mid-conv `role: system` | ✓ Yes (request rewrite) | E | Negligible | Low (이미 client) | **High** |
| Capacity 429 emulation | ✓ Yes (state counter) | F | Low | Negligible | Medium |
| `/v1/responses` stateful | 부분 (rewrite) / Hard (emulate) | G | Negligible / High | Negligible | Low |
| Constrained decoding (true strict) | **✗ NO** | (unavailable) | N/A | N/A | N/A |
| `/v1/embeddings`, audio | ✗ NO (모델 자체가 image+text only) | (unavailable) | N/A | N/A | N/A |

핵심: **8 gaps 중 7개가 router 단에서 보완 가능**. 1개 (true constrained decoding) 만 inference engine 변경 필요.

---

## 3. Architectural option (5가지)

이 router 를 실제로 구현하면 어떻게 만들까. 5가지 architectural choice.

### Option 1 — Sidecar Python FastAPI proxy

**구성:**
```
client (8001) ─→ Python FastAPI router (8001) ─→ mlx_lm.server (8002 로 이동)
                          │
                          └─ uvicorn worker, async I/O
                          └─ httpx async client to mlx_lm
                          └─ pydantic models for validation
                          └─ jsonschema for Strategy B
```

**규모:** ~500-800 LOC Python
**Stack:** FastAPI + httpx + jsonschema + pydantic
**Deployment:** 새 launchd plist `com.ohama.qwen122b-router.plist`. 8001 port 를 router 가 차지하고, mlx_lm 은 8002 로 이동 (또는 unix socket).

**장점:**
- async/await 가 native — streaming SSE 처리에 자연스러움
- FastAPI 의 OpenAPI schema generation 으로 router 자체가 self-documented
- pydantic validation 으로 request shape 강제
- `bench/.venv-eval` 에 이미 Python venv 있음 — 새 toolchain 불필요

**단점:**
- 새 process = 새 failure point (mlx_lm 죽으면 둘 다 영향)
- Python 의 GIL — high concurrency 시 단일 worker 부족 (단일 user 라 미문제)
- launchd plist 한 개 더, 의존성 한 개 더

### Option 2 — F# native proxy (Kestrel)

**구성:**
```
client (8001) ─→ F# Kestrel proxy ─→ mlx_lm.server (8002)
                          │
                          └─ ASP.NET Core minimal API
                          └─ HttpClient to mlx_lm
                          └─ JsonSchema.Net (이미 v1.0+)
```

**규모:** ~600-1000 LOC F#
**Stack:** Kestrel (ASP.NET Core 10) + HttpClient + JsonSchema.Net + FSharp.SystemTextJson

**장점:**
- blueCode 의 기존 stack 과 일치 — JsonSchema.Net + FSharp.SystemTextJson 재사용
- F# DU 로 router 의 internal state machine 강타입화 (request shape, route, validation result)
- single binary deploy (`dotnet publish`)
- `task {}` async 가 자연스러움

**단점:**
- **Core purity 위반 위험** — proxy 자체는 HTTP server (Kestrel) 가 필요. blueCode 의 invariant `src/BlueCode.Core/** must NOT reference Serilog, Spectre, Argu, or any HTTP client` 와 충돌. 새 project (`src/BlueCode.Router` 같은) 가 필요.
- F# 의 ASP.NET 통합은 C# 보다 verbose
- 배포 ceremony (.NET runtime 의존, but 사용자 시스템에 이미 있음)

### Option 3 — `mlx-openai-server` fork 채택

**구성:**
```
mlx_lm.server 를 mlx-openai-server 로 교체
client (8001) ─→ mlx-openai-server (8001)  # 자체적으로 OpenAI compat 보강
```

**규모:** 0 LOC (fork 사용)
**Stack:** Python (이미 있는 fork)

**장점:**
- 코드 zero — 기존 maintained fork 채택
- response_format 강제 (constrained decoding 까지 가능 — outlines integration)
- mlx_lm 의 모든 기능 + OpenAI gap 메움 + 더 많은 기능

**단점:**
- launchd plist 변경 (binary 가 다름: `mlx_openai_server` vs `mlx_lm.server`)
- maintenance 중단 위험 (mainline mlx_lm 가 빠르게 발전 → fork 가 뒤처질 가능성)
- 우리가 모르는 추가 dependency / quirk
- 기존 v1.0~v2.5 의 `bench/eval-qwen35-122b.sh` harness 가 mlx_lm 경로에 hardcode 됐을 가능성 — migration 필요
- 비교 검증 없이 swap 하면 회귀 위험 (다른 결과 dist 가능)

### Option 4 — nginx + Lua scripts

**구성:**
```
client ─→ nginx (8001) [Lua scripting] ─→ mlx_lm.server (8002)
```

**규모:** ~200-400 LOC Lua + nginx config
**Stack:** OpenResty (nginx + LuaJIT) + lua-resty-http

**장점:**
- 검증된 production-grade reverse proxy
- 빠름 (C 기반)
- TLS/TLS termination, rate limit 등 부가기능 무료
- Lua 가 가벼움

**단점:**
- **major dependency** — OpenResty/nginx 설치, brew, plist
- Lua 가 macOS daily-driver 에서 idiomatic 하지 않음
- JSON schema validation 라이브러리가 Lua 에 흔하지 않음 — Strategy B 구현 어려움
- v3.0+ 가서나 의미 있을 over-engineered 답

### Option 5 — Ollama wrapping

**구성:**
```
client ─→ Ollama (11434) [native OpenAI compat] ─→ mlx_lm 모델로 inference
```

**규모:** 0 LOC (Ollama 사용)
**Stack:** Ollama (Go binary)

**장점:**
- Ollama 가 OpenAI compat 을 native 지원
- 코드 zero
- macOS 에서 잘 작동

**단점:**
- **MLX 가 아니라 GGUF 양자화** — Qwen 122B-A10B 가 Ollama 에서 GGUF 로 사용 가능한지? (확인 안 됨; Phase 42 시점에는 mlx_lm 만 사용)
- 122B-A10B-4bit MoE 의 정확한 양자화 호환성 검증 필요
- 성능 비교 불명 (mlx vs llama.cpp/Ollama)
- v2.0 의 -85GB disk reclaim (Phase 19) 이후 Qwen 2.5 retire 했는데, Ollama 도입 = disk 다시 차지

### Option 평가 매트릭스

| Option | 코드 | 통합 | blueCode fit | 운영 부담 | 우선순위 |
|--------|------|------|--------------|----------|---------|
| 1. Python FastAPI sidecar | 500-800 LOC Python | bench/.venv-eval 재사용 | Medium (다른 stack) | New process | **★ 가장 합리적** (만약 도입하면) |
| 2. F# Kestrel native | 600-1000 LOC F# | blueCode stack 일치 | Good | New process + Core 외 project | 좋지만 over-engineered |
| 3. mlx-openai-server fork | 0 LOC (swap) | 검증 필요 | Low risk swap | Plist 변경만 | **★★ 빠른 win 가능** (만약 fork 가 maintained 면) |
| 4. nginx + Lua | 200-400 LOC Lua | 새 toolchain | Bad fit | Major | 회피 |
| 5. Ollama swap | 0 LOC (swap) | 모델 호환성 검증 필요 | High risk swap | Disk + cold-start 변동 | 회피 |

---

## 4. blueCode 입장에서의 ROI 평가

이 router 를 **blueCode 가 도입할 가치 있나?** 단일 user, 단일 client 라는 특수 상황에서.

### 4.1 현재 client-side 보완 상태

blueCode 가 이미 거의 모든 gap 을 client-side 에서 처리:

| Gap | client-side 보완 위치 |
|-----|----------------------|
| response_format json_object | `QwenHttpClient.fs` 의 3-stage JSON extractor + 2-retry. v2.6 PLANGEN-03 가 동일 패턴 확장. |
| response_format json_schema | v2.6 PLANGEN-03 가 `JsonSchema.Net` validator + 2-retry. server enforce 안 하고 prompt+validate. |
| Error envelope flat | `LlmUnreachable` 매핑이 200-char snippet 으로 opaque 처리. flat string 받아도 동작. |
| Mid-conv `role: system` | Phase 17-02 + 20-03 의 `Role = User invariant`. 모든 mid-conv injection 이 user role. 이미 lifted. |
| `n > 1` choice | blueCode 가 항상 `n=1` (default) 보냄. 안 씀. |
| Capacity 429 emulation | blueCode 가 timeout (300s) 만 처리. queue 에서 기다려도 응답 받으면 OK. |
| `/v1/responses` | blueCode 가 안 씀. |

요컨대 **blueCode 는 모든 gap 에 대해 이미 그 길로 안 가거나 client 로 보완 중**. router 가 가져다 줄 추가 가치 = 0.

### 4.2 Router 도입 시 추가 운영 비용

| 항목 | 비용 |
|------|------|
| New process | launchd plist 1개 추가, 의존성 1개 (Python venv 또는 F# project) |
| Failure mode 증가 | router 죽으면 blueCode 도 죽음 (단 reverse proxy 라 mlx_lm 만 살아도 안 됨). 두 process 의 health 모니터링 |
| 배포 복잡성 | `bench/run.sh --gate` 는 8001 직접 호출 → router 거치면 추가 latency 측정 표면 변경 |
| Bench gate 영향 | router 가 prompt 수정 (Strategy A/B) 하면 bench gate 의 step count 가 변할 수 있음 → 7/7 PASS invariant 위험 |
| 디버깅 복잡성 | 문제 발생 시 client / router / mlx_lm 3 layer 디버그 |

### 4.3 Router 도입 시 잠재적 가치 (가설)

router 가 있으면 가능해지는 것:
- **Other clients 호환성** — openai-python SDK, curl, 다른 toolchain. 단 blueCode 는 사실상 유일한 client.
- **Multi-backend swap** — 미래에 vLLM 또는 cloud API 로 swap 시 router 만 교체하면 client 변동 없음. 단 그런 swap 계획 없음 (현재 single-model 122B canonical).
- **Centralized observability** — 모든 LLM 호출의 통계 / latency / error rate 가 router 에 집중. 단 blueCode JSONL step log 이 이미 동일.
- **Constrained decoding 도입 가능 (Option 3 만)** — `mlx-openai-server` fork 가 outlines 통합. true strict schema enforcement.

### 4.4 결론 — blueCode 에는 NOT YET

**Router 는 v3.0+ architectural option.** v2.6/v2.7 에서는 도입 안 함. 이유:
- 기존 client-side 보완으로 모든 known gap 처리됨
- 새 process = 새 failure mode + bench gate 영향 위험
- blueCode 가 single client 라 router 의 multi-client 가치 zero
- mlx_lm 가 빠르게 진화 중 (probe 14 의 `[DONE]` 동작이 이미 RESEARCH 시점과 다름) — fork 채택 시 maintenance burden 위험

단 **두 가지 trigger 가 발생하면 재고려:**
1. v2.6/v2.7 implementation 후 다른 client (예: VS Code extension, 별도 Python notebook) 가 등장
2. Constrained decoding 이 critical 해짐 (예: agent 가 매번 multi-step 으로 prompt-instructed schema 깨면)

---

## 5. 일반 사용자 입장 ROI (blueCode 외 시나리오)

만약 다른 사용자/팀이 mlx_lm.server 위에 OpenAI-compatible 환경을 원한다면:

### 5.1 단순 swap — `mlx-openai-server` fork

**가장 합리적 path** : Option 3.
- 0 코드, plist 변경만
- response_format constrained decoding 까지 가능 (outlines)
- `n>1`, 일부 gap 자동 처리 가능
- **risk** : maintenance status 검증 필요. 마지막 commit 날짜, mainline mlx-lm 과의 sync 정도, 알려진 버그 확인.

### 5.2 Custom router — Option 1 (Python FastAPI sidecar)

**중간 복잡도, 최대 control** : 500-800 LOC.
- 모든 7개 gap 을 명시적으로 보완 (Strategies A-G)
- Test 가능 (router level pytest)
- 미래 backend swap 에 강함

### 5.3 Skip — 그냥 mlx_lm 그대로 + client-side 보완

**가장 빠른 path** : 0 코드, mlx_lm 그대로.
- blueCode 처럼 client 가 모든 gap 처리
- 새 client 마다 동일 코드 중복

이 셋 중 ROI 가장 좋은 건 5.1 (fork 채택). 5.2 는 customization 욕구 강할 때. 5.3 은 client 가 한 개일 때.

---

## 6. 만약 도입한다면 — Implementation sketch

만약 v3.0+ 또는 unanticipated trigger 발생해 router 를 도입한다고 가정. **Option 1 (Python FastAPI sidecar) sketch.**

### 6.1 Project 구조

```
qwen-openai-router/
├── pyproject.toml
├── README.md
├── src/qwen_openai_router/
│   ├── __init__.py
│   ├── main.py           # FastAPI app, route dispatch
│   ├── strategies.py     # A-G 구현 (compensation logic)
│   ├── upstream.py       # httpx client to mlx_lm
│   ├── models.py         # pydantic request/response shapes
│   ├── schema.py         # jsonschema validation helper
│   └── observability.py  # structured logging + metrics
├── tests/
│   ├── test_strategy_a.py
│   ├── test_strategy_b.py
│   ├── ...
│   └── test_e2e.py       # against real mlx_lm
└── ~/Library/LaunchAgents/com.ohama.qwen122b-router.plist
```

### 6.2 Routing logic skeleton

```python
# main.py
from fastapi import FastAPI, Request, HTTPException
from .strategies import (
    inject_json_object_instruction,
    inject_json_schema_instruction,
    rewrite_mid_conv_system,
    fan_out_n_choices,
    rewrite_error_envelope,
)

app = FastAPI()
UPSTREAM = "http://127.0.0.1:8002"

@app.post("/v1/chat/completions")
async def chat_completions(req: Request):
    body = await req.json()

    # Strategy E — mid-conv role:system rewrite
    body = rewrite_mid_conv_system(body)

    # Strategy A/B — response_format injection
    response_format = body.pop("response_format", None)
    if response_format:
        if response_format.get("type") == "json_object":
            body = inject_json_object_instruction(body)
        elif response_format.get("type") == "json_schema":
            body = inject_json_schema_instruction(
                body,
                response_format["json_schema"]["schema"]
            )

    # Strategy D — n>1 fan-out
    n = body.pop("n", 1)
    if n > 1:
        return await fan_out_n_choices(body, n)

    # Forward to mlx_lm
    upstream_response = await upstream_post("/v1/chat/completions", body)

    # Strategy A/B — post-validate response if response_format was set
    if response_format:
        validated = validate_response(upstream_response, response_format)
        if not validated.ok:
            return validated.error_response  # OpenAI-shaped error

    # Strategy C — error envelope rewrite
    if upstream_response.status_code != 200:
        return rewrite_error_envelope(upstream_response)

    return upstream_response.json()
```

### 6.3 Plist 변경

```xml
<!-- com.ohama.qwen122b-router.plist (NEW) -->
<key>ProgramArguments</key>
<array>
    <string>/Users/ohama/llm-system/env/qwen-router-env/bin/uvicorn</string>
    <string>qwen_openai_router.main:app</string>
    <string>--host</string>
    <string>127.0.0.1</string>
    <string>--port</string>
    <string>8001</string>
</array>

<!-- com.ohama.qwen122b.plist (MODIFIED — port 8001 → 8002) -->
<string>--port</string>
<string>8002</string>  <!-- was 8001 -->
```

### 6.4 Bench gate 영향 평가

`bench/run.sh --gate` 는 7/7 PASS 가 milestone-wide invariant. router 도입 시:

1. **Pre-rollout** : router 없이 7/7 PASS 확인
2. **Rollout** : router enable
3. **Post-rollout** : 7/7 PASS 재확인. 만약 변경되면:
   - step count 변동 (prompt rewrite 의 영향) → analyze 후 baseline.json update 또는 router 수정
   - Latency 변동 → acceptable (router 추가 latency = ~5-20ms)
   - 응답 shape 변동 → router 가 양자화 변경 안 했음에도 변동 시 (model 동일하므로 안 변해야) bug

이 단계가 없으면 silent regression 위험. 새 phase 의 verify-work 와 동등.

### 6.5 Test strategy

3 layer:

1. **Unit** — 각 strategy function 의 input/output 검증 (pytest)
2. **Integration** — router 띄우고 `bench/eval-openai-compat.py` 의 25 probes 다시 실행. 동일 결과 OR 더 conformant 결과.
3. **Bench gate** — `bench/run.sh --gate` 7/7 PASS

### 6.6 Phasing (만약 v3.0 phase 로 도입한다면)

- **Phase X.0**: Sketch + RFC document. blueCode core team 의견 수집.
- **Phase X.1**: Skeleton FastAPI project + Strategies C, E (가장 가벼움) 구현 + bench gate 7/7 PASS 확인.
- **Phase X.2**: Strategies A, B (response_format) 구현 + Phase 42 probe set 재실행으로 검증.
- **Phase X.3**: Strategies D, F (`n>1`, 429) 구현 — 옵트인 (default off, client 가 명시적 enable).
- **Phase X.4**: Production rollout — port swap, plist deploy, 1주일 daily-driver use, regression 모니터링.

총 4-6 phases. v2.6 robust MVP 와 비슷한 규모.

---

## 7. 대안 — Constrained decoding (engine-level fix)

Router 는 prompt rewrite + post-validate 만 가능. **진짜 schema 강제** 는 inference engine 안에서만 가능. 이는 router 의 한계.

### 7.1 가능한 stack

- **outlines** (https://github.com/outlines-dev/outlines) — token-level grammar/schema enforcement
- **lm-format-enforcer** — 비슷한 라이브러리
- **mlx-openai-server fork** — 이미 outlines 통합

### 7.2 도입 시점

만약 v2.6 PLANGEN-03 의 prompt-instructed schema 가 실패율 높으면 (예: model 이 schema 깨는 경우 5%+), router 단 보완 (Strategy B retry) 만으로는 부족 — 진짜 constrained decoding 이 필요. 이때:

- **Option 3 (mlx-openai-server fork)** 채택
- 또는 Custom inference engine 작성 (extreme; 사실상 v3.0 territory)

Phase 42 결과는 schema 0/50 InvalidJsonOutput (Phase 21) 가 prompt-instructed 만으로 충분 시사. 따라서 constrained decoding 도입 시급성 낮음.

---

## 8. 부록 — 비교 sheet

### 8.1 mlx_lm 0.31.3 vs mlx-openai-server fork

| 기능 | mlx_lm | mlx-openai-server |
|------|--------|------------------|
| `/v1/chat/completions` | ✓ | ✓ |
| `/v1/completions` | ✓ | ✓ |
| `/v1/models` | ✓ | ✓ |
| `/health` | ✓ | unknown |
| `/v1/embeddings` | ✗ | unknown |
| `response_format json_object` | ✗ | ✓ (outlines) |
| `response_format json_schema strict` | ✗ | ✓ (outlines) |
| Mid-conv role:system | ✗ (template) | ✗ (모델 자체 한계) |
| `tools` / `tool_choice` | ✓ | ✓ |
| Streaming SSE | ✓ | ✓ |
| `n > 1` | ✗ | unknown |
| 429 emulation | ✗ | unknown |
| `/v1/responses` | ✗ | unknown |
| OpenAI error envelope | ✗ | unknown |

→ fork 가 모든 gap 메우는지 검증 필요. 만약 메운다면 **Option 3 가 가장 간결한 답**.

### 8.2 vLLM, llama.cpp, Ollama 비교 (간단)

| Server | OpenAI compat | Quantization | Mac (Apple Silicon) | 122B-A10B 지원 |
|--------|--------------|--------------|---------------------|----------------|
| mlx_lm.server | 부분 (Phase 42 7 gaps) | MLX 4-bit affine | Native, fast | ✓ (현재) |
| mlx-openai-server | 더 풍부 (response_format 포함) | MLX | Native | ✓ |
| vLLM | 풍부 | AWQ, GPTQ | x86 only | ✗ (CUDA 필요) |
| llama.cpp | 부분 (있긴 함) | GGUF | Native, slower | unknown (GGUF 변환 필요) |
| Ollama | 풍부 (자체 보강) | GGUF | Native | unknown |

→ Apple Silicon + MoE 인 122B-A10B 는 **mlx 계열만 native**. 다른 stack 으로 넘어가면 양자화 변경 (GGUF 등) 으로 정확도/성능 표면이 변함 — Phase 21 의 96/100 verdict 가 무의미해짐. **단순 swap 안 됨**.

---

## 9. 추천 verdict (다시 명시)

| 시나리오 | 추천 |
|---------|------|
| **blueCode v2.6/v2.7 milestone** | **Router 도입 안 함**. client-side 보완으로 충분. |
| blueCode v3.0+ (multi-client 또는 backend swap 계획 시) | Option 1 (Python FastAPI sidecar) — 단계적 도입 (Phase X.0 ~ X.4) |
| 일반 사용자 / 팀 (Mac MLX 환경) | Option 3 (`mlx-openai-server` fork) 채택 검토 — 0 코드 win 가능성 |
| Production-grade multi-tenant | Option 4 (nginx + Lua) 또는 cloud-native gateway (Kong/Envoy) — over-engineered for personal use |
| true constrained decoding 필요 | Option 3 (fork with outlines) 또는 vLLM (x86 only) |

---

## 10. v2.6 implementation 으로 흘러들어가는 결론

Phase 42 가 router 가능성 분석을 가능하게 했다. **그러나 v2.6 design 이 router 의존하지 않는 길로 잘 설계됐음** 이 다시 한번 확인됐다:

- PLANGEN-03 가 prompt-instructed schema + JsonSchema.Net 으로 처리 — server 의 response_format 의존 안 함
- DEV-01/02 가 first-system-message 패턴으로 처리 — mid-conv role:system 의존 안 함
- Per-task fresh conversation 으로 처리 — capacity 429 의존 안 함 (timeout 만)
- LlmUnreachable 매핑이 flat string 처리 — error envelope 형 의존 안 함

따라서 **v2.6 implementation 은 router 없이 진행** 하면 된다. router 는 v2.6 ship 후 daily-driver 사용 patterns 가 surface 한 trigger (multi-client, backend swap, constrained decoding 필요) 에 따라 v3.0+ 에서 재고려.

특히 두 v2.7+ candidate 가 router 와 관련:
- **Native tools/tool_choice migration** — router 와 무관 (mlx_lm 이 이미 conformant)
- **Wave-parallel exec (Phase F resurrection)** — router 가 capacity 429 처리하면 client 가 단순화. 단 blueCode 가 timeout 만 처리해도 OK.

---

*문서 작성: 2026-05-07*
*작성자: Claude (Opus 4.7) via 사용자의 router 가능성 분석 요청*
*Source: Phase 42 empirical findings + RESEARCH.md source-code analysis*
*다음 step: 이 분석은 informational only. 실제 도입은 v3.0+ trigger 발생 시.*
