# mlx-openai-router — Apple Silicon mlx_lm.server 위 OpenAI-compatible smart router (F# / .NET 10)

**작성일:** 2026-05-07
**Project codename:** `mlx-openai-router` (이 문서에서는 "Router" 로 표기)
**Goal:** mlx_lm.server (Apple Silicon, MoE 모델) 위에서 **OpenAI Python SDK / openai-python / litellm / langchain 등 표준 client 가 코드 변경 없이 작동하는** 독립 reverse-proxy. F# 10 / .NET 10 single self-contained binary 로 배포.

**Source — empirical evidence base:** mlx_lm.server 0.31.3 에 대한 25-probe / 8-surface OpenAI-API conformance evaluation. 측정 결과: HIGH=3 (response_format silent-ignore × 3), MEDIUM=2 (n>1 silent-1, logprobs unverified), LOW=5 (error envelope shape, /v1/responses 404, [DONE] sentinel timing 등), PASS=15 (tools/tool_choice 완전 conformant, parallel decode N=2 confirmed, mid-conv role:system rejection invariant 등). 상세 probe 명세 + verdict mapping 은 §2 (Compensation Surface) 와 §9.4 (Conformance probe replay) 에 inline. 이 문서를 새 repo 에 복사하면 self-contained — 외부 의존성 없음.

---

## 0. Project Goal

**한 줄:**
> Apple Silicon Mac 의 mlx_lm.server 위에 OpenAI Python SDK / openai-python / litellm / langchain 등 표준 client 가 코드 변경 없이 작동하도록 만드는 reverse-proxy. F# / .NET 10 으로 작성된 single self-contained binary.

**구체적으로:**

```python
# 사용자 입장에서는 (어떤 언어 client 든) 이게 그대로 동작해야 함:
from openai import OpenAI

client = OpenAI(
    api_key="dummy",  # 라우터는 인증 안 함 (loopback only)
    base_url="http://127.0.0.1:8001/v1"  # Router endpoint
)

response = client.chat.completions.create(
    model="qwen122b",
    messages=[{"role": "user", "content": "Output JSON: {name, age}"}],
    response_format={"type": "json_object"},  # ← 진짜 강제됨
    n=3,                                       # ← 진짜 3개 choices
)

# 그리고 mid-conv system, error envelope, tool calls 모두 OpenAI 와 동일 동작
```

**왜 "smart" router 인가** — 단순 passthrough proxy 가 아니다. 7개 gap 을 적극적으로 보완 (request/response 양방향 transformation), state-aware (capacity tracking, 429 emulation), observability built-in (per-route metrics, conformance test fixture).

**왜 standalone 인가** — 어떤 특정 client 와도 결합되지 않은 universal protocol shim. 누구든 binary 받아 launchd 에 등록하면 바로 쓸 수 있는 product. 자체 repo, 자체 CI, 자체 release cycle.

**왜 F# / .NET 10 인가** — §3 (Architectural Decision) 에서 자세히. 핵심: strong typing 으로 protocol shim 의 정확성 보장; `task {}` async 가 HTTP forwarding 에 자연스러움; .NET 10 의 inbox SSE parser + `JsonSerializerOptions.Strict` 가 router 작업의 절반을 free 로 처리; self-contained single binary 배포.

---

## 1. Scope & Non-Goals

### 1.1 In scope

| 영역 | 범위 |
|------|------|
| Stack | F# 10 / .NET 10 (LTS through 2028); ASP.NET Core 10 (Kestrel + minimal API) |
| Backend | mlx_lm.server (mainline mlx-lm 0.31.x+), Apple Silicon Mac only |
| Models | MLX 4-bit MoE 계열 (Qwen 3.5 122B-A10B / 35B-A3B; Llama-MoE 계열 호환 가능) |
| API | OpenAI v1 — `/v1/chat/completions`, `/v1/completions`, `/v1/models`, `/v1/embeddings` (옵션) |
| Compensation | 아래 §2 의 7 gaps 모두 (response_format × 2, error envelope, n>1, mid-conv role, 429, /v1/responses) |
| Streaming | SSE chat/completions 양방향 — chunk shape 보정 + `[DONE]` 처리 |
| Auth | None for v1 (loopback only); v2 에서 bearer token 추가 |
| Multi-model | 단일 backend 시작; v2 에서 multi-backend (122B + 35B + cloud fallback) |
| Deployment | macOS launchd plist + self-contained binary (`dotnet publish -r osx-arm64 --self-contained`) |

### 1.2 Out of scope (v1)

| 영역 | 이유 |
|------|------|
| 멀티-tenant / 사용자 격리 | personal tool. 단일 사용자 가정. 멀티-tenant = v3+ |
| TLS / HTTPS | loopback only. 외부 노출 안 함. v2+ 옵션. |
| Rate limit by API key | auth 없음. 단일 사용자 = 단일 quota. |
| Streaming function/tool calls 의 partial chunk 변환 | 현재 mlx_lm 이 이미 OpenAI envelope conformant. 변환 불필요. |
| Vector DB / RAG / 메모리 | 별 product 영역. router 는 protocol layer 만. |
| Audio (Whisper) / Vision API | mlx_lm.server 가 아직 미지원. 모델은 multimodal 이지만 server 가 expose 안 함. |
| Constrained decoding (true token-level enforcement) | engine-level 변경 필요 (router 에서 불가). 대신 prompt+validate+retry 로 emulate. |
| Production-grade observability (Prometheus exporter, OpenTelemetry) | v2+. v1 은 Serilog structured JSON log + counters 만. |
| Cross-platform Windows / Linux | macOS-first. ASP.NET Core 자체는 cross-platform 이지만 launchd 패키징은 macOS only. |

### 1.3 Non-goals (영원히 X)

- **Cloud-only competitor** — Router 의 가치는 local backend 지원에 있다. cloud-only 면 LiteLLM 등이 더 낫다.
- **Inference engine reimplementation** — mlx_lm 의 wrapper 일 뿐. mlx_lm 이 발전하면 router 도 같이.
- **Auth provider** — Router 는 protocol shim. 인증/인가는 별도 layer.

---

## 2. Compensation Surface — 무엇을 보완하는가

25-probe / 8-surface conformance evaluation 이 발견한 7 gap 을 **product feature** 로 재구성. 각 strategy 는 명시적 probe 결과 (probe ID, expected vs observed) 에 근거.

### 2.1 Feature: `response_format` enforcement

**Spec:** OpenAI client 가 `response_format: {type: "json_object"}` 또는 `{type: "json_schema", strict: true, schema: {...}}` 를 요청할 때 server 가 honor 한 것처럼 동작.

**Strategy A — `json_object` mode:**
1. 요청에서 `response_format` 추출
2. Last user message 끝에 instruction inject:
   ```
   Output ONLY valid JSON. No prose, no markdown fences, no explanations.
   ```
3. mlx_lm forward
4. 응답 content 가 parseable JSON 인지 검증 (`JsonDocument.Parse` try)
5. Parse 실패 시:
   - **Recovery 1**: 3-stage extraction (bare JSON parse → brace-counted brace-matched substring → markdown-fence stripping — well-established pattern for prose-wrapped LLM JSON)
   - **Recovery 2**: 같은 request 1회 retry with reinforced instruction
   - **Recovery 3**: OpenAI 표준 에러 반환 (`invalid_response_format`)
6. 성공 시 OpenAI envelope 으로 응답

**Strategy B — `json_schema` mode:**
1. Schema 추출
2. Last system or user message 끝에 schema-instructed prompt:
   ```
   Output ONLY a JSON object matching this exact schema:
   {SCHEMA_AS_PRETTY_JSON}
   No additional fields. No prose. No markdown.
   ```
3. mlx_lm forward
4. 응답 → `JsonSchema.Net.JsonSchema.Validate(responseDoc, schema)` (이미 검증된 .NET 라이브러리)
5. Validation 실패 시:
   - **Recovery 1**: Validation error message 를 prompt 에 추가하고 1회 retry
   - **Recovery 2**: OpenAI `invalid_response_format` 에러
6. `strict: true` 와 `false` 차이: `false` 면 retry 안 하고 first response 반환 (best-effort).

**Caveat:** OpenAI 의 `strict: true` 는 token-level constrained decoding (server 가 schema 외 token 자체를 sample 못 하게 mask). Router 단에서는 불가능 — post-validate + retry 만 가능. **이는 의도적 trade-off** — true constrained decoding 은 inference engine 변경 (Section 7) 가 필요. Router 는 best-effort emulation.

### 2.2 Feature: Mid-conversation `role: system` translation

**Spec:** OpenAI 는 transcript 어디든 `role: system` 메시지 허용. mlx_lm + Qwen 은 첫 메시지 only (template 강제). Router 가 자동 변환.

**Strategy E:**
1. Request `messages[]` walk
2. 두 번째 이상의 system 메시지 발견 시:
   - 변환 옵션 a: `role: user` 로 변환 + content 앞 `[SYSTEM HINT] ` prefix
   - 변환 옵션 b: 첫 system 메시지에 concat (긴 system prompt)
   - **default** = 옵션 a (semantic 보존이 더 자연스러움)
3. mlx_lm forward
4. 응답 unchanged

**옵션 expose:** Router config 의 `MidConvSystemStrategy = "user_prefix" | "concat_first"` 으로 사용자가 선택.

### 2.3 Feature: Error envelope normalization

**Spec:** mlx_lm 의 flat `{"error": "<string>"}` 을 OpenAI 표준 `{"error": {"message", "type", "code", "param"}}` 으로 wrap.

**Strategy C:**
1. Response status_code 가 non-200 이면
2. Body parse 시도. flat string 이면 wrap, 이미 structured 면 passthrough.
3. status → type 매핑:

```
400 → "invalid_request_error"
401 → "authentication_error"  (Router 가 auth 안 하므로 미발생; backend 가 401 보내면 forward)
403 → "permission_error"
404 → "not_found_error"
422 → "invalid_request_error"
429 → "rate_limit_error"  (Router 가 emulate; Section 2.6)
500/502 → "server_error"
503 → "service_unavailable_error"
504 → "timeout_error"
```

4. `code`, `param` 은 best-effort inference (request body 의 어느 field 가 invalid 인지). 정보 부족하면 `null`.

### 2.4 Feature: `n > 1` choice fan-out

**Spec:** OpenAI 는 `n: 3` 이면 3개 choice 반환. mlx_lm 은 silently 1 반환. Router 가 fan-out.

**Strategy D:**
1. Request 의 `n` 추출. `n == 1` 또는 누락이면 passthrough.
2. `n: K` (K > 1) 이면:
   - Request 에서 `n` 제거
   - K 개의 동일 request 를 `Task.WhenAll` + F# `task {}` CE 로 병렬 fire (mlx_lm BatchGenerator 가 native batch — N=2 empirical: wall=max(t1,t2)=0.39s vs sum=0.76s, 즉 진짜 parallel decode)
3. K 응답 aggregate:

```json
{
  "id": "<router_generated_id>",
  "object": "chat.completion",
  "choices": [
    {response_1.choices[0]} with index 0,
    {response_2.choices[0]} with index 1,
    ...
    {response_K.choices[0]} with index K-1
  ],
  "usage": {
    "prompt_tokens": <K번 등장하므로 K× 일 수 있음 — choose first or sum>,
    "completion_tokens": <sum of all K>,
    "total_tokens": <sum>
  }
}
```

**Caveat:** mlx_lm BatchGenerator 의 `--decode-concurrency 32` 가 capacity. `n > 32` 요청은 Router 가 즉시 422 invalid_request 반환 (`n must be ≤ 32`).

### 2.5 Feature: `/v1/responses` stateful API (Optional, v2)

**Spec:** OpenAI 2024 가 `/v1/responses` 추가 — `previous_response_id` 로 stateful conversation. mlx_lm 은 미지원.

**v1 strategy G-a:** 503 + structured error.
```json
{
  "error": {
    "message": "/v1/responses not supported by mlx_lm.server backend. Use /v1/chat/completions with full message history.",
    "type": "endpoint_not_supported",
    "code": "stateless_backend"
  }
}
```

**v2 strategy G-b (옵션):** Router 가 stateful state store (sqlite or in-memory dict) 로 emulate.
- Client 가 첫 호출 시 Router 가 response_id 생성, state 저장.
- 다음 호출에 `previous_response_id` 보내면 Router 가 history 합쳐서 chat/completions 로 forward.
- TTL eviction (예: 1시간), 최대 history depth 제한.

v1 에서는 503 으로 충분 — 사용자가 직접 chat/completions 로 마이그레이션.

### 2.6 Feature: Capacity tracking + `429` emulation

**Spec:** OpenAI 는 capacity 초과 시 429 + `Retry-After` 헤더 반환. mlx_lm 은 indefinite queue.

**Strategy F:**
1. Router 가 in-flight request count 를 `System.Threading.Interlocked` atomic counter 로 추적.
2. Configurable threshold `MaxConcurrent` (default = 16, mlx_lm BatchGenerator capacity 32 의 절반).
3. 신규 request 시:
   - count < threshold → forward, count++
   - count ≥ threshold → 즉시 429 + `Retry-After: 5` 헤더
4. Response 받으면 count--.

**Why threshold = 16, not 32:**
- Router 가 mlx_lm 직접 호출만이 아니라 fan-out (n>1) + retry (response_format) 로 internal multiplier 가 있음
- BatchGenerator 가 진짜 32 까지 batch 하면 latency 가 hundred-of-ms 단위로 길어짐 — 16 에서 자르면 throughput vs latency trade-off 균형
- Configurable — 사용자가 알아서 튜닝

### 2.7 Feature: SSE streaming chunk normalization

**Spec:** mlx_lm 의 SSE chunk 가 거의 OpenAI 와 동일하지만 minor diff:
- `role: "assistant"` 가 every chunk 에 반복 (OpenAI 는 first only)
- `[DONE]` sentinel 동작 — 현재 build 에서는 conformant

**Strategy H:**
1. Streaming request (`stream: true`) 도착 시 Router 가 `.NET 10 inbox System.Net.ServerSentEvents` parser 로 forward (3rd-party SSE 라이브러리 불필요).
2. Chunk 별 transformation:
   - First chunk 만 `delta.role = "assistant"` 보존, 나머지 chunk 에서는 strip
   - `[DONE]` 통과
   - chunk envelope 의 `id`, `object`, `created` 표준화
3. Stream end 검출 + 연결 close

### 2.8 Compensation matrix (요약)

| # | Feature | Strategy | Cost | Default ON? |
|---|---------|----------|------|-------------|
| 1 | response_format json_object | A: prompt+validate+retry | Low | ✓ |
| 2 | response_format json_schema | B: schema-prompt+validate+retry | Medium (schema 토큰) | ✓ |
| 3 | Error envelope shape | C: response wrap | Trivial | ✓ |
| 4 | n>1 choice fan-out | D: parallel K calls | High (K× tokens) | ✓ |
| 5 | Mid-conv role:system | E: request rewrite | Trivial | ✓ |
| 6 | Capacity 429 emulation | F: counter + threshold | Trivial | ✓ |
| 7 | SSE chunk normalization | H: streaming transform | Low | ✓ |
| 8 | /v1/responses stateful | G-a (503) v1 / G-b (emulate) v2 | Trivial / High | v1: ✓ G-a only |

8개 feature 모두 default ON. 사용자가 config 로 끌 수 있음 (debugging 용).

---

## 3. Architectural Decision — F# / .NET 10 ASP.NET Core

### 3.1 왜 F# 인가

**Strong typing 이 protocol shim 에 ideal:**
- OpenAI request/response 의 다양한 shape 을 F# discriminated unions 로 표현 — 잘못된 case 가 컴파일 시간에 잡힘
- `match` exhaustiveness 검사 — 새 OpenAI field 추가 시 모든 처리 위치 강제 update
- Record types + immutability — request/response 의 functional transformation (Strategy A-H 가 사실상 함수 chain) 자연

**`task {}` async 가 HTTP forwarding 에 native:**
- `HttpClient.SendAsync` 가 `Task<HttpResponseMessage>` — F# `task {}` CE 로 직접 chain
- `Task.WhenAll` 로 fan-out (Strategy D) 가 단순
- F# 10 의 `task {}` CE 는 tail-recursive — long streaming 에서 stack 안전
- 개념적으로 async {} 와 다른 것: blueCode CLAUDE.md 의 invariant "task only" 와 동일 철학

**`.NET 10` inbox features 가 router 작업의 절반을 free:**
- `JsonSerializerOptions.Strict` — unknown field rejection (LLM output validation 강화)
- `System.Net.ServerSentEvents.SseParser` — SSE streaming 처리에 third-party dep 불필요
- `System.Net.Http.HttpClient` 의 `PipeReader` 통합 — zero-copy forwarding
- `Microsoft.Extensions.Hosting` — IHostedService, IOptions, configuration 표준 패턴

**.NET ASP.NET Core minimal API 가 lightweight:**
- Kestrel 자체가 production-grade web server (IIS / nginx 불필요)
- Minimal API endpoint syntax 가 F# DSL 과 자연스러움 (`MapPost(...)`)
- Middleware pipeline 으로 cross-cutting concerns (trace ID, logging, error handling) 처리

**Self-contained single binary 배포:**
- `dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true` → 단일 ~30MB 바이너리
- `.NET runtime` 사용자 시스템에 미리 깔려있을 필요 없음
- launchd plist 가 단일 바이너리만 가리킴 → 배포 단순

### 3.2 왜 ASP.NET Core minimal API 가 아닌가? (그것이 답이지만 — Giraffe 등 대안 검토)

F# 에서 Web framework 옵션:
1. **ASP.NET Core minimal API** ★ — 표준, 잘 maintained, .NET 10 의 모든 feature 활용
2. **Giraffe** — F#-idiomatic functional routing, ASP.NET Core 위에 구축. middleware composition 이 더 functional. 약간의 cognitive overhead.
3. **Falco** — 더 가벼운 functional alternative. 작은 community.
4. **Suave** — legacy F# web framework. .NET 10 호환성 검증 필요.

**선택: ASP.NET Core minimal API.** 이유:
- 표준 — 사용자가 .NET 문서 / Stack Overflow 에서 검색 가능
- .NET 10 신기능 (SseParser, JsonSerializerOptions.Strict) 즉시 활용
- minimal API 의 `MapPost` 시퀀스가 F# 에서도 readable
- 미래 contributor 가 ASP.NET 배경이면 진입 즉시
- Giraffe / Falco 도 결국 ASP.NET Core 위 — performance 차이 없음

### 3.3 왜 fork (mlx-openai-server) 가 아닌가

`mlx-openai-server` 는 mlx_lm 의 fork 로 **inference engine 자체** 를 변경. router 는 inference engine 에 무관해야 한다. 즉:
- Router 는 mlx_lm 0.31.3 이든 0.40.x 든 작동해야 함 (backend 교체에 강함)
- Fork 는 backend 자체를 swap — mlx_lm mainline 의 진보를 잃을 위험
- Fork 가 outlines (constrained decoding) 통합한다면 Router 가 그 fork 를 *backend 로* 사용하면 됨 — 둘은 layered

따라서 Router 는 **모든 OpenAI-compatible-or-not 한 mlx 계열 backend 위에서 작동** 하는 universal shim 으로 설계.

### 3.4 왜 Python / Go / Rust 가 아닌가 (Section 11 부록 참조)

선택지가 여러 개 있지만 F# 가 ROI 가장 좋음 — strong typing, .NET 10 inbox features, single binary, async I/O 자연. 비교 detail 은 §11.

---

## 4. API Surface

Router 가 expose 하는 endpoint.

### 4.1 OpenAI v1 endpoints (forwarded)

| Endpoint | Method | Behavior |
|---------|--------|----------|
| `/v1/chat/completions` | POST | 8개 feature 모두 적용 + forward to mlx_lm |
| `/v1/completions` | POST | mid-conv role:system 무관, response_format only, capacity tracking |
| `/v1/models` | GET | passthrough (mlx_lm 이 OpenAI shape) |
| `/v1/embeddings` | POST | 503 (mlx_lm 미지원) |
| `/v1/responses` | POST | v1: 503 / v2: emulate (G-b) |
| `/v1/audio/*` | * | 503 |
| `/v1/images/*` | * | 503 |
| `/v1/fine_tuning/*` | * | 503 |
| `/v1/files/*` | * | 503 |
| `/v1/batches/*` | * | 503 |

### 4.2 Router-specific endpoints (admin)

| Endpoint | Method | Behavior |
|---------|--------|----------|
| `/health` | GET | Router self-health (`{status: "ok"}`); upstream 상태 별개 |
| `/health/upstream` | GET | mlx_lm.server health probe |
| `/metrics` | GET | Prometheus-style counter + histogram |
| `/admin/config` | GET | 현재 config dump (auth 추가 시 protected) |
| `/admin/inflight` | GET | 현재 in-flight request count |

### 4.3 Request/response shape

OpenAI v1 spec 그대로. 차이점:
- Request 에 router-specific extension 가능 (config override): `extra_body: {"router_extras": {"prefer_recovery": "extract"}}`
- Response header 에 `X-Router-Trace-Id`, `X-Router-Compensation-Applied: A,C` 같은 introspection 정보 추가

---

## 5. Project Structure

F# .NET solution. Core/Web/Tests 3-project layout — Core 가 pure logic, Web 이 ASP.NET host, Tests 가 Expecto.

```
[project-root]/
├── README.md
├── LICENSE                                 # MIT or Apache-2.0
├── CHANGELOG.md
├── global.json                             # .NET SDK 10.x pin
├── Directory.Build.props                   # shared MSBuild props
├── nuget.config                            # optional package source
├── MlxOpenAIRouter.sln                     # solution file
├── src/
│   ├── MlxOpenAIRouter.Core/
│   │   ├── MlxOpenAIRouter.Core.fsproj
│   │   ├── Domain.fs                       # OpenAI request/response DUs (먼저 컴파일)
│   │   ├── Errors.fs                       # RouterError DU + OpenAI error envelope mapping
│   │   ├── Recovery.fs                     # 3-stage JSON extraction
│   │   ├── SchemaValidate.fs               # JsonSchema.Net wrapper
│   │   └── Strategies/
│   │       ├── ResponseFormat.fs           # Strategy A + B
│   │       ├── RoleTranslation.fs          # Strategy E
│   │       ├── ErrorEnvelope.fs            # Strategy C
│   │       ├── NFanout.fs                  # Strategy D
│   │       ├── StatefulResponses.fs        # Strategy G-a (v1: 503 stub)
│   │       ├── Capacity.fs                 # Strategy F (Interlocked counter)
│   │       └── Streaming.fs                # Strategy H (SseParser wrapper)
│   ├── MlxOpenAIRouter.Web/
│   │   ├── MlxOpenAIRouter.Web.fsproj
│   │   ├── Program.fs                      # Kestrel host + minimal API + DI wire-up
│   │   ├── Routes.fs                       # `MapPost`/`MapGet` endpoint definitions
│   │   ├── UpstreamClient.fs               # HttpClient wrapper to mlx_lm
│   │   ├── Configuration.fs                # AppSettings record + IOptions binding
│   │   ├── Observability.fs                # Serilog setup + metrics
│   │   └── Middleware/
│   │       ├── TraceId.fs                  # X-Router-Trace-Id middleware
│   │       └── ErrorHandling.fs            # global exception → OpenAI error
├── tests/
│   ├── MlxOpenAIRouter.Tests/
│   │   ├── MlxOpenAIRouter.Tests.fsproj
│   │   ├── Program.fs                      # Expecto entry [<EntryPoint>]
│   │   ├── RootTests.fs                    # explicit rootTests list (test discovery)
│   │   ├── Unit/
│   │   │   ├── ResponseFormatTests.fs
│   │   │   ├── RoleTranslationTests.fs
│   │   │   ├── ErrorEnvelopeTests.fs
│   │   │   ├── NFanoutTests.fs
│   │   │   ├── CapacityTests.fs
│   │   │   ├── StreamingTests.fs
│   │   │   └── RecoveryTests.fs            # 3-stage JSON extraction
│   │   ├── Integration/
│   │   │   ├── E2EChatTests.fs             # against real mlx_lm (gated by env var)
│   │   │   ├── E2EStreamingTests.fs
│   │   │   └── E2ENFanoutTests.fs
│   │   └── Conformance/
│   │       ├── OpenAISdkTests.fs           # openai-python via PythonNet or subprocess
│   │       └── ConformanceProbeTests.fs    # 25-probe replay
│   └── Fixtures/
│       └── conformance-probes.jsonl        # 25-record probe transcript (Stage 0 에 import)
├── bench/
│   ├── MlxOpenAIRouter.Bench.fsproj
│   ├── Latency.fs                          # router 추가 latency p50/p99 (vs mlx_lm 직접)
│   └── Throughput.fs                       # n>1 fan-out 효율
├── docs/
│   ├── architecture.md
│   ├── strategies.md                       # Strategy A-H 상세
│   ├── deployment.md
│   ├── development.md
│   └── compensation-matrix.md              # 7 gaps × 8 strategies × verdict mapping
├── packaging/
│   ├── launchd/
│   │   └── com.example.mlx-openai-router.plist
│   └── scripts/
│       ├── install.sh                      # publish + plist install
│       ├── uninstall.sh
│       └── status.sh                       # quick health check
├── .github/                                # optional GitHub Actions
│   └── workflows/
│       ├── build.yml                       # dotnet build + test
│       └── release.yml                     # publish on tag
└── scripts/
    └── check-no-async.sh                   # CI grep — Core 에 async {} literal 금지
```

### 5.1 Project files (.fsproj)

#### MlxOpenAIRouter.Core.fsproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>FS0044</NoWarn>  <!-- ObsoleteAttribute 가 일부 사용 -->
  </PropertyGroup>

  <ItemGroup>
    <!-- Compile order matters in F# — Domain.fs first -->
    <Compile Include="Domain.fs" />
    <Compile Include="Errors.fs" />
    <Compile Include="Recovery.fs" />
    <Compile Include="SchemaValidate.fs" />
    <Compile Include="Strategies/ResponseFormat.fs" />
    <Compile Include="Strategies/RoleTranslation.fs" />
    <Compile Include="Strategies/ErrorEnvelope.fs" />
    <Compile Include="Strategies/NFanout.fs" />
    <Compile Include="Strategies/StatefulResponses.fs" />
    <Compile Include="Strategies/Capacity.fs" />
    <Compile Include="Strategies/Streaming.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="FSharp.SystemTextJson" Version="1.4.36" />
    <PackageReference Include="JsonSchema.Net" Version="9.2.0" />
    <PackageReference Include="FsToolkit.ErrorHandling" Version="5.2.0" />
  </ItemGroup>

</Project>
```

**Core 는 ASP.NET Core 의존성 없음** — 순수 logic + ports. CI 에서 grep 으로 검증 가능 (`grep -rn "Microsoft.AspNetCore" src/MlxOpenAIRouter.Core/` 가 0).

#### MlxOpenAIRouter.Web.fsproj

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>osx-arm64</RuntimeIdentifier>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Configuration.fs" />
    <Compile Include="Observability.fs" />
    <Compile Include="UpstreamClient.fs" />
    <Compile Include="Middleware/TraceId.fs" />
    <Compile Include="Middleware/ErrorHandling.fs" />
    <Compile Include="Routes.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MlxOpenAIRouter.Core\MlxOpenAIRouter.Core.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Serilog.AspNetCore" Version="8.0.3" />
    <PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />
    <PackageReference Include="Argu" Version="6.2.5" />
    <PackageReference Include="prometheus-net.AspNetCore" Version="8.2.1" />
  </ItemGroup>

</Project>
```

#### MlxOpenAIRouter.Tests.fsproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <!-- Order matters — RootTests.fs has rootTests list, must come AFTER all test modules -->
    <Compile Include="Unit/RecoveryTests.fs" />
    <Compile Include="Unit/ResponseFormatTests.fs" />
    <Compile Include="Unit/RoleTranslationTests.fs" />
    <Compile Include="Unit/ErrorEnvelopeTests.fs" />
    <Compile Include="Unit/NFanoutTests.fs" />
    <Compile Include="Unit/CapacityTests.fs" />
    <Compile Include="Unit/StreamingTests.fs" />
    <Compile Include="Integration/E2EChatTests.fs" />
    <Compile Include="Integration/E2EStreamingTests.fs" />
    <Compile Include="Integration/E2ENFanoutTests.fs" />
    <Compile Include="Conformance/OpenAISdkTests.fs" />
    <Compile Include="Conformance/ConformanceProbeTests.fs" />
    <Compile Include="RootTests.fs" />
    <Compile Include="Program.fs" />  <!-- [<EntryPoint>] -->
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\MlxOpenAIRouter.Core\MlxOpenAIRouter.Core.fsproj" />
    <ProjectReference Include="..\..\src\MlxOpenAIRouter.Web\MlxOpenAIRouter.Web.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Expecto" Version="10.2.1" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
  </ItemGroup>

</Project>
```

### 5.2 Code skeleton

#### Domain.fs (Core)

```fsharp
module MlxOpenAIRouter.Core.Domain

open System.Text.Json.Serialization

/// OpenAI chat completion role
[<JsonConverter(typeof<JsonStringEnumConverter>)>]
type Role =
    | [<JsonName "system">] System
    | [<JsonName "user">] User
    | [<JsonName "assistant">] Assistant
    | [<JsonName "tool">] Tool

/// OpenAI chat message — 단순 구조; 멀티모달 content 는 v2+
type ChatMessage = {
    [<JsonPropertyName "role">] Role: Role
    [<JsonPropertyName "content">] Content: string
    [<JsonPropertyName "name">] Name: string option
}

/// response_format type
type ResponseFormatType =
    | JsonObject
    | JsonSchema of schema: System.Text.Json.JsonElement * strict: bool

/// OpenAI chat completion request (relevant fields only)
type ChatRequest = {
    Model: string
    Messages: ChatMessage list
    Stream: bool option
    N: int option
    Temperature: float option
    TopP: float option
    MaxTokens: int option
    ResponseFormat: ResponseFormatType option
    Tools: System.Text.Json.JsonElement option  // pass-through
    ToolChoice: System.Text.Json.JsonElement option
}

/// OpenAI chat completion response
type ChatChoice = {
    Index: int
    Message: ChatMessage
    FinishReason: string
}

type Usage = {
    PromptTokens: int
    CompletionTokens: int
    TotalTokens: int
}

type ChatResponse = {
    Id: string
    Object: string
    Created: int64
    Model: string
    Choices: ChatChoice list
    Usage: Usage option
}

/// Compensation result — strategy 가 tagged DU 로 의도를 명확히
type CompensationOutcome<'T> =
    | Passed of 'T
    | RetryNeeded of reason: string * retriedRequest: ChatRequest
    | UnrecoverableError of message: string * errorType: string
```

#### Strategies/ResponseFormat.fs (Core)

```fsharp
module MlxOpenAIRouter.Core.Strategies.ResponseFormat

open System.Text.Json
open MlxOpenAIRouter.Core.Domain
open MlxOpenAIRouter.Core.Recovery

/// Strategy A: inject json_object instruction
let injectJsonObjectInstruction (req: ChatRequest) : ChatRequest =
    let instruction = "\n\nOutput ONLY valid JSON. No prose, no markdown fences, no explanations."
    let updatedMessages =
        req.Messages
        |> List.mapi (fun i msg ->
            if i = req.Messages.Length - 1 && msg.Role = User then
                { msg with Content = msg.Content + instruction }
            else msg)
    { req with Messages = updatedMessages; ResponseFormat = None }

/// Strategy B: inject schema-instructed prompt
let injectJsonSchemaInstruction
    (req: ChatRequest)
    (schema: JsonElement)
    : ChatRequest =
    let schemaStr = JsonSerializer.Serialize(schema)
    let instruction =
        sprintf
            "\n\nOutput ONLY a JSON object matching this exact schema:\n%s\nNo additional fields. No prose. No markdown."
            schemaStr
    let updatedMessages =
        req.Messages
        |> List.mapi (fun i msg ->
            if i = req.Messages.Length - 1 && msg.Role = User then
                { msg with Content = msg.Content + instruction }
            else msg)
    { req with Messages = updatedMessages; ResponseFormat = None }

/// Post-validate response against expected format
let postValidate
    (response: ChatResponse)
    (rf: ResponseFormatType)
    : CompensationOutcome<ChatResponse> =
    match rf with
    | JsonObject ->
        let content = response.Choices.[0].Message.Content
        match Recovery.tryExtractJson content with
        | Some validJson -> Passed { response with Choices = [{ response.Choices.[0] with Message = { response.Choices.[0].Message with Content = validJson } }] }
        | None ->
            RetryNeeded(
                "Response content is not parseable JSON; retrying with reinforced instruction",
                // … updated request with stricter prompt …
                Unchecked.defaultof<_>)
    | JsonSchema (schema, strict) ->
        let content = response.Choices.[0].Message.Content
        match SchemaValidate.tryValidate content schema with
        | Ok validatedJson -> Passed response
        | Error violations ->
            if strict then
                RetryNeeded(
                    sprintf "Schema violations: %A" violations,
                    Unchecked.defaultof<_>)
            else
                Passed response  // best-effort; return as-is
```

#### Program.fs (Web)

```fsharp
module MlxOpenAIRouter.Web.Program

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open MlxOpenAIRouter.Web.Configuration
open MlxOpenAIRouter.Web.Routes
open MlxOpenAIRouter.Web.Observability

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)

    // Configuration: appsettings.json → env vars → command line
    builder.Configuration
        .AddJsonFile("appsettings.json", optional = true)
        .AddEnvironmentVariables(prefix = "MLX_ROUTER_")
        .AddCommandLine(args)
    |> ignore

    builder.Services
        .Configure<RouterSettings>(builder.Configuration)
        .AddHttpClient<UpstreamClient>(fun client settings ->
            client.BaseAddress <- System.Uri(settings.UpstreamUrl)
            client.Timeout <- System.TimeSpan.FromSeconds(float settings.UpstreamTimeoutSec))
        .AddSingleton<CapacityTracker>()
        .AddSerilog(configureSerilog)
        .AddOpenTelemetryMetrics()
    |> ignore

    let app = builder.Build()

    app.UseMiddleware<TraceIdMiddleware>() |> ignore
    app.UseMiddleware<ErrorHandlingMiddleware>() |> ignore

    Routes.mapAll app

    app.Run()
    0
```

#### Routes.fs (Web)

```fsharp
module MlxOpenAIRouter.Web.Routes

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open MlxOpenAIRouter.Core.Domain
open MlxOpenAIRouter.Core.Strategies

let mapChatCompletions (app: WebApplication) =
    app.MapPost("/v1/chat/completions", fun (request: ChatRequest)
                                            (capacity: CapacityTracker)
                                            (upstream: UpstreamClient)
                                            (ctx: HttpContext) ->
        task {
            // Capacity check
            use! _slot = capacity.AcquireOrThrow429Async()

            // Strategy E: mid-conv role:system rewrite
            let request = RoleTranslation.rewriteMidConvSystem request "user_prefix"

            // Strategy A/B: response_format extraction + injection
            let request, originalFormat =
                match request.ResponseFormat with
                | Some JsonObject ->
                    ResponseFormat.injectJsonObjectInstruction request, request.ResponseFormat
                | Some (JsonSchema (schema, _)) ->
                    ResponseFormat.injectJsonSchemaInstruction request schema, request.ResponseFormat
                | None -> request, None

            // Strategy D: n>1 fan-out
            match request.N with
            | Some n when n > 1 -> return! NFanout.executeAsync upstream request n
            | _ -> ()

            // Streaming dispatch
            if request.Stream = Some true then
                return! Streaming.processAsync upstream request ctx.Response

            // Standard forward
            let! upstreamResp = upstream.PostChatCompletionAsync request

            match upstreamResp with
            | Error err -> return! ErrorEnvelope.normalizeAsync err ctx.Response
            | Ok response ->
                // Post-validate response_format
                match originalFormat with
                | Some rf ->
                    match ResponseFormat.postValidate response rf with
                    | Passed final -> return Results.Ok(final)
                    | RetryNeeded (_, retriedReq) ->
                        let! retryResp = upstream.PostChatCompletionAsync retriedReq
                        return Results.Ok(retryResp |> Result.defaultWith (fun _ -> response))
                    | UnrecoverableError (msg, errType) ->
                        return Results.BadRequest({| error = {| message = msg; type_ = errType |} |})
                | None -> return Results.Ok(response)
        })
    |> ignore

let mapAll (app: WebApplication) =
    mapChatCompletions app
    // …other endpoints…
    app.MapGet("/health", fun () -> {| status = "ok" |}) |> ignore
```

(실제 구현은 더 많은 edge case + error handling)

### 5.3 Test discovery pattern (load-bearing)

F# Expecto 는 attribute-based 자동 discovery 를 신뢰할 수 없음. 대신 **explicit `rootTests` list**:

```fsharp
// RootTests.fs
module MlxOpenAIRouter.Tests.RootTests

open Expecto

let rootTests =
    testList "all" [
        Unit.RecoveryTests.tests
        Unit.ResponseFormatTests.tests
        Unit.RoleTranslationTests.tests
        // … 모든 test module …
    ]
```

```fsharp
// Program.fs
[<EntryPoint>]
let main args =
    Tests.runTestsWithCLIArgs [] args MlxOpenAIRouter.Tests.RootTests.rootTests
```

새 test module 추가 시 BOTH:
1. `MlxOpenAIRouter.Tests.fsproj` 의 `<Compile Include>` 순서 (RootTests.fs 보다 먼저)
2. `RootTests.fs` 의 `rootTests` list

이게 안 지켜지면 **silent test skip** — 프로젝트 init 시 documentation 에 명시.

---

## 6. Configuration

### 6.1 Layers (precedence: high → low)

1. CLI args via Argu (`--port`, `--upstream-url`, ...)
2. Environment variables (`MLX_ROUTER_Port`, `MLX_ROUTER_UpstreamUrl`, ...)
3. `appsettings.json` (config file)
4. Defaults (in F# record constructor)

### 6.2 Config schema (F# record + IOptions)

```fsharp
// Configuration.fs
module MlxOpenAIRouter.Web.Configuration

[<CLIMutable>]
type RouterSettings = {
    // Server
    Host: string
    Port: int
    LogLevel: string

    // Upstream
    UpstreamUrl: string
    UpstreamTimeoutSec: int

    // Capacity
    MaxConcurrent: int

    // Strategies
    MidConvSystemStrategy: string  // "user_prefix" | "concat_first"
    ResponseFormatMaxRetries: int
    NFanoutMax: int

    // Streaming
    StreamBufferChunks: int

    // /v1/responses
    StatefulEmulation: bool         // v1: false; v2: true
    StatefulTtlSec: int

    // Observability
    MetricsEnabled: bool
    MetricsPath: string
}

module RouterSettings =
    let defaults = {
        Host = "127.0.0.1"
        Port = 8001
        LogLevel = "Information"
        UpstreamUrl = "http://127.0.0.1:8002"
        UpstreamTimeoutSec = 300
        MaxConcurrent = 16
        MidConvSystemStrategy = "user_prefix"
        ResponseFormatMaxRetries = 1
        NFanoutMax = 32
        StreamBufferChunks = 4
        StatefulEmulation = false
        StatefulTtlSec = 3600
        MetricsEnabled = true
        MetricsPath = "/metrics"
    }
```

### 6.3 Sample appsettings.json

```json
{
  "Host": "127.0.0.1",
  "Port": 8001,
  "LogLevel": "Information",
  "UpstreamUrl": "http://127.0.0.1:8002",
  "UpstreamTimeoutSec": 300,
  "MaxConcurrent": 16,
  "MidConvSystemStrategy": "user_prefix",
  "ResponseFormatMaxRetries": 1,
  "NFanoutMax": 16,
  "MetricsEnabled": true
}
```

### 6.4 Argu CLI override (선택)

```fsharp
// Program.fs 의 CLI parsing 부분
type CliArgs =
    | Port of int
    | UpstreamUrl of string
    | Config of path: string
    | LogLevel of string
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Port _ -> "Override server port"
            | UpstreamUrl _ -> "Override mlx_lm.server URL"
            | Config _ -> "Path to appsettings.json"
            | LogLevel _ -> "Verbose / Debug / Information / Warning / Error"
```

---

## 7. Constrained decoding (limitation 의 honest 표시)

Router 의 핵심 한계: **token-level constrained decoding 은 불가능**. 이는 inference engine 안에서만 가능.

### 7.1 무엇을 보완할 수 없는가

OpenAI 의 `response_format: {type: "json_schema", strict: true, ...}` 는 모델이 schema 외 token 을 emit 못 하도록 sample 단계에서 mask. 100% schema-compliant 보장.

Router 의 Strategy B (prompt + validate + retry) 는:
- Prompt 로 instruction → 모델이 따를 수 있지만 nondeterministic
- Validate 로 검출 → schema violation 검출 가능
- Retry → fix 시도, but converge 보장 X

따라서 Strategy B 가 99% schema-compliant 면 그게 최선. 1% case 는 retry 끝에 `invalid_response_format` 에러로 surface.

### 7.2 진짜 강제가 필요할 때

다음 둘 중 하나:

**Option A: Backend 를 fork 로 swap**
- `mlx-openai-server` (outlines 통합) 또는 vLLM (guided generation) 같은 server 로 backend 교체
- Router 는 backend 에 무관 — config 의 `UpstreamUrl` 만 변경하면 됨
- Trade-off: backend stability / 성능 / 양자화 호환성 새로 검증 필요

**Option B: Router 가 inference 직접 (engine 통합)**
- F# 에서 outlines 같은 라이브러리 통합은 어려움 (Python 라이브러리). MLX C++ direct integration 도 가능하지만 큰 work.
- 더 자연스러운 path: ONNX Runtime 또는 TensorRT-LLM 와의 통합
- 이는 v3+ 영역 — Router 의 scope 을 넘어섬

Router v1/v2 는 prompt-instructed best-effort 가 충분하다는 가정 위에 작동. 50-invocation prompt-instructed schema test 에서 50/50 perfect compliance 가 empirical 으로 관찰됨 (Qwen 3.5 122B-A10B-4bit MoE; thinking-mode disabled). 즉 well-structured prompt 만으로도 실용 수준의 conformance 가능.

---

## 8. Deployment

### 8.1 macOS launchd (default)

self-contained binary 가 `/usr/local/opt/mlx-openai-router/bin/MlxOpenAIRouter.Web` 에 설치된 가정.

```xml
<!-- packaging/launchd/com.example.mlx-openai-router.plist -->
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.example.mlx-openai-router</string>

    <key>ProgramArguments</key>
    <array>
        <string>/usr/local/opt/mlx-openai-router/bin/MlxOpenAIRouter.Web</string>
        <string>--Port</string>
        <string>8001</string>
        <string>--UpstreamUrl</string>
        <string>http://127.0.0.1:8002</string>
    </array>

    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>ThrottleInterval</key>
    <integer>30</integer>

    <key>StandardOutPath</key>
    <string>/usr/local/var/log/mlx-openai-router.log</string>
    <key>StandardErrorPath</key>
    <string>/usr/local/var/log/mlx-openai-router.err</string>

    <key>EnvironmentVariables</key>
    <dict>
        <key>DOTNET_NOLOGO</key>
        <string>1</string>
        <key>DOTNET_CLI_TELEMETRY_OPTOUT</key>
        <string>1</string>
        <key>MLX_ROUTER_LogLevel</key>
        <string>Information</string>
    </dict>
</dict>
</plist>
```

### 8.2 Build & install scripts

```bash
# packaging/scripts/install.sh
#!/usr/bin/env bash
set -euo pipefail

PREFIX=${PREFIX:-/usr/local/opt/mlx-openai-router}
PLIST_DIR=~/Library/LaunchAgents
PLIST_NAME=com.example.mlx-openai-router.plist

# 1. Build self-contained binary
echo "Building MlxOpenAIRouter.Web for osx-arm64..."
dotnet publish src/MlxOpenAIRouter.Web/MlxOpenAIRouter.Web.fsproj \
    -c Release \
    -r osx-arm64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$PREFIX/bin"

# 2. Copy appsettings.json (생성 또는 user override)
mkdir -p "$PREFIX/etc"
if [ ! -f "$PREFIX/etc/appsettings.json" ]; then
    cp packaging/appsettings.default.json "$PREFIX/etc/appsettings.json"
fi

# 3. Install plist
cp packaging/launchd/$PLIST_NAME "$PLIST_DIR/"

# 4. Critical: change mlx_lm port from 8001 → 8002 if currently using 8001
echo "WARNING: Router will bind to port 8001."
echo "         If your mlx_lm.server is currently on 8001, swap it to 8002."
echo "         This script can do that automatically if your plist is at"
echo "         ~/Library/LaunchAgents/com.ohama.qwen122b.plist"
read -p "Auto-swap mlx_lm port to 8002? [y/N] " yn
case $yn in
    [Yy]*)
        MLX_PLIST=~/Library/LaunchAgents/com.ohama.qwen122b.plist
        if [ -f "$MLX_PLIST" ]; then
            sed -i.backup 's|<string>8001</string>|<string>8002</string>|' "$MLX_PLIST"
            launchctl unload "$MLX_PLIST"
            launchctl load -w "$MLX_PLIST"
        fi
        ;;
    *) ;;
esac

# 5. Reload services
launchctl load -w "$PLIST_DIR/$PLIST_NAME"

# 6. Wait for both ready
echo "Waiting for mlx_lm @ 8002..."
until curl -fsS http://127.0.0.1:8002/v1/models > /dev/null 2>&1; do sleep 5; done

echo "Waiting for router @ 8001..."
until curl -fsS http://127.0.0.1:8001/health > /dev/null 2>&1; do sleep 2; done

echo "Done. Test: curl http://127.0.0.1:8001/v1/models"
```

### 8.3 Cross-platform (v2)

ASP.NET Core 자체는 cross-platform 이지만 v1 은 macOS launchd 만 지원. v2:
- Linux systemd unit file
- Windows service (NSSM 또는 native)
- Docker image

```dockerfile
# packaging/docker/Dockerfile (v2+)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY publish/ /app/
EXPOSE 8001
ENTRYPOINT ["./MlxOpenAIRouter.Web"]
```

---

## 9. Testing Strategy

3-layer + 1 conformance.

### 9.1 Unit tests (Expecto)

각 strategy module 의 input/output 검증. Mock `UpstreamClient`.

```fsharp
// tests/MlxOpenAIRouter.Tests/Unit/ResponseFormatTests.fs
module MlxOpenAIRouter.Tests.Unit.ResponseFormatTests

open Expecto
open MlxOpenAIRouter.Core.Domain
open MlxOpenAIRouter.Core.Strategies.ResponseFormat

let tests =
    testList "Strategy A - JSON object injection" [

        testCase "appends instruction to last user message" <| fun _ ->
            let req = {
                Model = "qwen122b"
                Messages = [
                    { Role = User; Content = "Hi"; Name = None }
                ]
                Stream = None; N = None; Temperature = None; TopP = None
                MaxTokens = None
                ResponseFormat = Some JsonObject
                Tools = None; ToolChoice = None
            }

            let result = injectJsonObjectInstruction req
            Expect.stringContains
                result.Messages.[0].Content
                "Output ONLY valid JSON"
                "instruction must be appended"
            Expect.isNone result.ResponseFormat "response_format must be stripped before forward"

        testCase "handles empty messages list" <| fun _ ->
            let req =
                { Model = "qwen122b"; Messages = []; Stream = None; N = None
                  Temperature = None; TopP = None; MaxTokens = None
                  ResponseFormat = Some JsonObject; Tools = None; ToolChoice = None }
            let result = injectJsonObjectInstruction req
            Expect.equal result.Messages [] "empty messages stays empty"
    ]
```

목표: 90%+ coverage of strategy logic.

### 9.2 Integration tests (against real mlx_lm)

전체 router → mlx_lm forward path 검증. CI 에서는 `[<RealBackend>]` 같은 attribute 로 gate; local dev 에서는 실제 mlx_lm.

```fsharp
// tests/MlxOpenAIRouter.Tests/Integration/E2EChatTests.fs
module MlxOpenAIRouter.Tests.Integration.E2EChatTests

open Expecto
open System.Net.Http
open System.Text.Json

[<Tests>]
let tests =
    testList "E2E chat completions (real backend)" [

        // 환경 변수 MLX_ROUTER_INTEGRATION=1 일 때만 활성화
        ptestCase "response_format json_object is enforced" <| fun _ ->
            task {
                use client = new HttpClient(BaseAddress = System.Uri("http://127.0.0.1:8001"))
                let body = """
                {
                    "model": "qwen122b",
                    "messages": [{"role": "user", "content": "Output JSON: {greeting: string}"}],
                    "response_format": {"type": "json_object"}
                }
                """
                let! resp = client.PostAsync("/v1/chat/completions",
                                             new StringContent(body, System.Text.Encoding.UTF8, "application/json"))
                let! respBody = resp.Content.ReadAsStringAsync()
                let json = JsonDocument.Parse(respBody)
                let content =
                    json.RootElement
                        .GetProperty("choices").[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString()

                // Must parse — Strategy A retry guarantees
                let parsed = JsonDocument.Parse(content)
                Expect.isTrue
                    (parsed.RootElement.TryGetProperty("greeting") |> fst)
                    "greeting key must exist"
            } |> Async.AwaitTask |> Async.RunSynchronously
    ]
```

`ptestCase` (pending test) 가 default — `MLX_ROUTER_INTEGRATION=1 dotnet run --project tests/...` 시 활성.

### 9.3 Conformance tests (OpenAI SDK)

OpenAI Python SDK 가 실제로 작동하는지 검증. PythonNet 으로 inline 또는 subprocess 호출.

```fsharp
// Conformance/OpenAISdkTests.fs (subprocess 방식)
let tests =
    testList "OpenAI SDK conformance" [
        testCase "openai-python chat.completions.create works" <| fun _ ->
            let pythonScript = """
import os
os.environ['OPENAI_API_KEY'] = 'dummy'
from openai import OpenAI
c = OpenAI(base_url='http://127.0.0.1:8001/v1')
r = c.chat.completions.create(model='qwen122b', messages=[{'role':'user','content':'Hi'}])
print(r.choices[0].message.content)
"""
            let psi = System.Diagnostics.ProcessStartInfo("python3", "-c \"" + pythonScript.Replace("\"", "\\\"") + "\"")
            psi.RedirectStandardOutput <- true
            use proc = System.Diagnostics.Process.Start(psi)
            proc.WaitForExit()
            Expect.equal proc.ExitCode 0 "openai-python must succeed"
            Expect.isNotEmpty (proc.StandardOutput.ReadToEnd()) "must return content"
    ]
```

8개 feature 각각에 대해 conformance test 1개. 총 ~10-15 테스트.

### 9.4 Conformance probe regression replay

Stage 0 에 import 된 25 conformance probe (`tests/Fixtures/conformance-probes.jsonl` + 그것을 생성하는 driver) 를 Router 위에서 재실행. 각 probe 의 verdict 가 mlx_lm 직접 호출 vs Router 호출에서 어떻게 변하는지 측정.

#### 25 probe 의 8 surface 분포

| Surface | Probe ID | 측정 |
|---------|----------|------|
| 1. Endpoint coverage | 01-05 | 5 probes — `/v1/chat/completions` baseline / `/v1/completions` legacy / `/v1/models` GET / `/health` GET / `/v1/responses` (404 expected) |
| 2. response_format | 06-08 | 3 probes — json_object / json_schema strict:true / json_object rerun (nondeterministic check) |
| 3. Role handling | 09-10 | 2 probes — mid-conv system rejected (404) / start-only system (200 control) |
| 4. Streaming SSE | 11-14 | 4 probes — chunk shape / role-on-every-chunk / `[DONE]` with include_usage / without include_usage |
| 5. Schema enforcement (statistical) | 20-21 | 2 probes — temp=0.0 N=5 each (without/with response_format) for statistical compliance |
| 6. Multi-call coherence | 23-25 | 3 probes — fresh conversation 3회로 KV isolation 검증 |
| 7. Error surface | 16-19 | 4 probes — model 누락 / messages 빈 / max_tokens 음수 / invalid JSON body |
| 8. Concurrency | 22 | 1 probe — N=2 simultaneous requests, wall_max vs sum 측정 |
| Tools (BONUS) | 15 | 1 probe — `tools` + `tool_choice:auto` envelope 검증 |

#### Verdict 변환 expected

| 직접 mlx_lm verdict | Router 위 expected |
|--------------------|-------------------|
| HIGH × 3 (response_format silent-ignore — probes 06/07/08) | **PASS** (Strategy A/B 가 prompt-instructed + post-validate + retry 로 enforce) |
| MEDIUM × 2 (n>1 silent-1 / logprobs unverified) | **PASS** (Strategy D fan-out / logprobs passthrough) |
| LOW × 5 (error envelope, /v1/responses, [DONE] timing 등) | mostly **PASS**; 1-2 는 그대로 LOW (mlx_lm 의 OpenAI 외 동작이 의도된 경우) |
| PASS × 15 (tools, parallel, role:system rejection 등) | **PASS** (passthrough — regression 없어야) |

#### Acceptance criteria for v0.1.0

**HIGH=0, MEDIUM=0, LOW≤2, PASS≥23.** 즉 Router 가 25 probe 중 23개 이상을 PASS 로 끌어올림.

probe driver 와 fixture JSONL 은 Stage 0 의 첫 작업.

### 9.5 Performance benchmarks

```fsharp
// bench/Latency.fs
// Router 추가 latency 측정 — 목표: ≤20ms p50, ≤50ms p99 (vs mlx_lm 직접)
```

mlx_lm 의 자체 latency (TTFT 222ms warm 측정) 대비 router overhead 는 무시할 만해야.

---

## 10. Phasing (Project Roadmap)

Standalone product 의 development stage. 각 stage = 1-2주 스프린트.

### Stage 0: Project init
- F# solution + 3 fsproj skeleton
- README.md draft + global.json (.NET SDK 10.x pin)
- Compensation matrix (7 gaps + strategies + verdict mapping) → docs/compensation-matrix.md
- conformance probe driver (F# port) + 25-record fixture JSONL → tests/Fixtures/
- License 결정 (MIT or Apache-2.0)
- CI setup (GitHub Actions: dotnet build + test)
- `scripts/check-no-async.sh` — Core 에 `async {}` literal 금지 grep

### Stage 1: MVP forward proxy
- Kestrel + minimal API
- `/v1/chat/completions`, `/v1/models`, `/v1/completions` passthrough via UpstreamClient
- Strategy C (error envelope normalization) only
- Strategy F (capacity 429 emulation via Interlocked counter)
- `/health`, `/health/upstream` endpoints
- Unit + 1 integration test

**Acceptance:** OpenAI Python SDK 의 basic chat completion call 작동. Router 추가 latency p50 ≤ 20ms.

### Stage 2: Compensation Strategies A + B + E
- Strategy A (json_object enforce)
- Strategy B (json_schema enforce — JsonSchema.Net wrapper)
- Strategy E (mid-conv role:system rewrite)
- Recovery (3-stage JSON extraction)

**Acceptance:** 3 HIGH-severity probes (response_format json_object / json_schema strict / json_object rerun) 가 Router 위에서 PASS. Mid-conv role:system probe 가 PASS (mid → user 변환). 즉 conformance 가 mlx_lm 직접 vs Router 비교에서 +4 PASS 변환.

### Stage 3: Streaming + n>1
- Strategy H (streaming chunk normalization via .NET 10 inbox SseParser)
- Strategy D (n>1 fan-out via `Task.WhenAll`)

**Acceptance:** OpenAI SDK 의 `stream=True` 와 `n=3` 정상 작동. 4 streaming probes + concurrency probe (N=2) 모두 PASS.

### Stage 4: Observability + admin
- Serilog structured logging (Serilog.AspNetCore)
- prometheus-net.AspNetCore metrics (`/metrics`)
- `/admin/*` endpoints

**Acceptance:** prometheus 스크래핑으로 in-flight count, p50/p99 latency, error rate 측정 가능.

### Stage 5: Packaging + deployment
- launchd plist + install.sh / uninstall.sh / status.sh
- README 마무리 + deployment.md
- v0.1.0 GitHub Release (self-contained binary attached)

**Acceptance:** `git clone && bash packaging/scripts/install.sh` 한 줄로 배포. Conformance probe replay 결과 HIGH=0, MEDIUM=0, LOW≤2, PASS≥23 — v0.1.0 acceptance criteria 만족.

### Stage 6 (v0.2): Stateful /v1/responses (G-b)
- in-memory state store (또는 SQLite via Microsoft.Data.Sqlite)
- TTL eviction (`Microsoft.Extensions.Caching.Memory`)
- Optional 활성화

### Stage 7 (v0.3): Multi-backend
- Config 의 `Backends: [{name, url}]` 지원
- Round-robin 또는 model-based routing
- Fallback to cloud (OpenAI / Anthropic / etc.) 옵션

### Stage 8 (v0.4): Auth + multi-tenant
- Bearer token + per-key rate limit
- TLS via `Microsoft.AspNetCore.Server.Kestrel.Https`

총 8 stages. v0.1.0 = Stage 0-5 (5-10주), v0.2-v0.4 = 추가 6-12주.

---

## 11. Architectural Alternatives (부록)

본문 §3 에서 F# / .NET 10 ASP.NET Core minimal API 채택. 다른 옵션들의 detail.

| Option | LOC | 강점 | 약점 |
|--------|-----|------|------|
| **F# / .NET 10 ASP.NET Core minimal API** ★ | 2000-3000 | strong typing, .NET 10 inbox SSE, single binary, JsonSchema.Net 검증된 dep | F# ASP.NET 생태계 작음 (community) |
| F# / Giraffe | 2200-3200 | functional routing, F#-idiomatic | learning curve, Microsoft 표준 외 |
| C# / .NET 10 ASP.NET Core | 1800-2800 | .NET 의 mainstream, library 풍부 | DUs 없음 — strategy 표현이 verbose |
| Python FastAPI | 1500-2500 | dev velocity, Python eco | runtime type weak, GIL, deploy ceremony |
| Go (chi or gin) | 1500-2500 | fast, single binary | learning curve, JSON schema 라이브러리 약 |
| Rust (axum) | 2000-3500 | fastest, memory-safe | dev velocity 낮음 |
| nginx + Lua | 500-1000 | production-grade | logic limit, JSON schema 어려움 |
| `mlx-openai-server` fork swap | 0 (swap) | zero code, immediate | inference engine 변경, maintenance risk |

**F# 의 ROI 가 가장 좋은 이유:**
- Protocol shim 의 정확성 = strong typing 으로 컴파일 타임 보장
- `task {}` async 가 HTTP forwarding 자연 (Python asyncio 보다 type-safe)
- .NET 10 의 inbox features (SseParser, JsonSerializerOptions.Strict) 가 router 작업 절반을 free
- Self-contained single binary — 배포 단순 (Python venv 또는 Docker 불필요)
- JsonSchema.Net + FSharp.SystemTextJson 가 이미 maintained

---

## 12. Comparison with `mlx-openai-server` fork

가장 가까운 alternative.

| 항목 | This project (Router) | mlx-openai-server fork |
|------|----------------------|------------------------|
| Layer | Reverse-proxy (separate process) | Inference engine fork |
| Backend | mlx_lm.server (mainline) | (자체 inference) |
| Compensation strategy | Prompt + validate + retry | Token-level constrained decoding (outlines) |
| Strict schema enforcement | Best-effort (~99%) | True 100% |
| Backend swap | ✓ (config change) | ✗ (engine 자체 lock-in) |
| `n>1`, 429, mid-conv role | ✓ Compensated | unknown (검증 필요) |
| Maintenance burden | Independent project | mlx-lm mainline drift risk |
| Performance overhead | ~10-30ms | minimal (inference 자체) |
| Stack | F# / .NET 10 | Python |

**언제 Router 가 나은가:**
- Backend 를 swap 가능하게 유지하고 싶음 (mlx_lm → vLLM → cloud)
- mainline mlx-lm 의 발전을 즉시 누리고 싶음
- compensation 외 features (multi-backend routing, observability) 추가 가능성
- Strong typing + single binary 배포 선호

**언제 fork 가 나은가:**
- 100% strict schema 가 필수
- inference 자체의 tuning (sampling, KV cache config) 도 customize 하고 싶음
- 추가 process 안 띄우고 싶음

**언제 둘 다:**
- Router (this project) 의 backend 가 fork → fork 의 strict decoding 을 누리면서 Router 의 보호 logic 도 적용

---

## 13. Future Extensibility

v1.0 ship 후 가능한 확장:

### 13.1 Multi-backend routing
- Config: `Backends: [{name: "qwen122b", url: "http://...:8002"}, {name: "qwen35b", url: "http://...:8000"}, {name: "claude", url: "https://api.anthropic.com/...", apiKey: "..."}]`
- Request 의 `model` field 로 backend 선택
- Cloud fallback (mlx 가 down 이면 Claude 로 자동)

### 13.2 Caching
- Idempotent request (`temperature: 0`) 의 응답 캐싱
- Cache key = SHA256(messages + sampling_params)
- TTL + LRU eviction (`Microsoft.Extensions.Caching.Memory`)

### 13.3 Auth
- Bearer token validation (ASP.NET Core 의 `AddAuthentication().AddJwtBearer(...)`)
- Per-key rate limit (별도 RateLimiter middleware)
- Audit log (Serilog enricher)

### 13.4 Smart routing by task type
- Code task → 122B
- Quick chat → 35B
- Vision → cloud (mlx 안 됨)
- Heuristic 또는 small classifier model

### 13.5 RAG sidecar (optional)
- Vector DB integration (Chroma, Qdrant via HTTP — F# client 작성)
- Auto-augment user message with retrieved chunks before forward

---

## 14. Open Questions (v1 ship 전 결정 필요)

1. **License?** MIT (maximum permissive) vs Apache-2.0 (patent grant) vs proprietary. → 권장: **MIT** (.NET eco 정착).
2. **Repo location?** GitHub public repo from day 1 vs. private until v0.1.0.
3. **Versioning?** SemVer 적용. v0.x 는 breaking change 허용.
4. **.NET 최소 버전?** .NET 10 (LTS, modern features) vs .NET 9 (broader compat). → 권장: **.NET 10** (LTS through 2028 + inbox SSE/Strict mode).
5. **F# 최소 버전?** F# 10 (modern). → 권장: **F# 10**.
6. **Async style?** `task {}` (recommended for HTTP/.NET interop) vs `async {}` (legacy F# convention). → 권장: **`task {}` exclusively** — Core 에 `async {}` literal 금지 (CI grep).
7. **Default port?** 8001 (mlx_lm 와 충돌하므로 swap 강제) vs 8080 (browser default) vs 11434 (Ollama-compat). → 권장: **8001 + install script 가 mlx_lm 을 8002 로 이동**.
8. **명명?** `mlx-openai-router` (이 문서) vs `MlxOpenAIRouter` (PascalCase, F# convention) vs `OpenAIShim`. → 권장: **product name `mlx-openai-router`**, **F# namespace `MlxOpenAIRouter`** (둘 다 사용).
9. **Strict schema 의 강제 정도?** Strategy B 의 max retry. 1회 (default) vs 2회 (more aggressive) vs 0 (post-validate only no retry).
10. **`n>1` 의 max?** 16 (보수) vs 32 (BatchGenerator capacity 그대로). → 권장: **16** (latency 보호).
11. **Distribution?** GitHub Release binary attachment vs Homebrew tap vs both. → 권장: **GitHub Release** v0.1.0; **Homebrew** v0.2+.
12. **`/metrics` 의 cardinality 폭발 위험?** label 에 model name 포함 = 모델 추가될 때마다 차원 증가. → 권장: model 별 metric 분리 (label 안 사용).

---

## 15. Project 의 가치 (Why bother)

이 router 가 존재함으로써 가능해지는 것:

### 15.1 OpenAI SDK ecosystem 즉시 활용
- LangChain / LlamaIndex / litellm / semantic-kernel 모두 OpenAI SDK 를 spec 으로 작성됨
- Router 가 있으면 이들이 mlx_lm backend 위에서 코드 수정 없이 작동
- 현재는 각 framework 마다 mlx_lm 어댑터 따로 필요 (있으면 stale, 없으면 작동 안 함)

### 15.2 Backend 교체에 강함
- mlx_lm 이 발전 멈추거나 quirky 해지면 vLLM / llama.cpp / cloud 로 swap. Client 코드 변경 없음.
- 미래 새 inference engine (예: Apple 의 future MLX-X) 등장 시도 swap 가능

### 15.3 Apple Silicon native 영역의 표준 shim 후보
- Mac / MoE 환경의 표준 — Ollama 와 다른 niche (Ollama 는 자체 양자화 형식; Router 는 mlx 양자화)
- F# / .NET native binary — Python venv 또는 Docker 의존 없음
- Open-source 시 community 가치

### 15.4 Research / 측정 가능성
- conformance test 가 versioned — mlx_lm 새 release 될 때마다 자동 회귀 검출
- empirical evaluation 이 future-proof — probe set 이 product 의 internal test fixture 가 되어 mlx_lm 새 release 마다 자동 회귀 검출

### 15.5 Observability 표준
- 모든 LLM 호출이 한 process 에서 측정됨
- Latency distribution, error rate, model-by-model split 등이 production-grade 도구로 분석 가능

---

## 16. 시작 명령

만약 이 project 를 진짜 시작한다면 첫 명령:

```bash
# 1. Repo init
mkdir mlx-openai-router && cd mlx-openai-router
git init

# 2. global.json — .NET SDK 10.x pin
cat > global.json <<'EOF'
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestMinor"
  }
}
EOF

# 3. Solution + 3 projects
dotnet new sln --name MlxOpenAIRouter
dotnet new classlib --language F# --output src/MlxOpenAIRouter.Core --name MlxOpenAIRouter.Core
dotnet new web --language F# --output src/MlxOpenAIRouter.Web --name MlxOpenAIRouter.Web
dotnet new console --language F# --output tests/MlxOpenAIRouter.Tests --name MlxOpenAIRouter.Tests

dotnet sln add \
    src/MlxOpenAIRouter.Core/MlxOpenAIRouter.Core.fsproj \
    src/MlxOpenAIRouter.Web/MlxOpenAIRouter.Web.fsproj \
    tests/MlxOpenAIRouter.Tests/MlxOpenAIRouter.Tests.fsproj

# 4. Project references
dotnet add src/MlxOpenAIRouter.Web/MlxOpenAIRouter.Web.fsproj reference \
    src/MlxOpenAIRouter.Core/MlxOpenAIRouter.Core.fsproj
dotnet add tests/MlxOpenAIRouter.Tests/MlxOpenAIRouter.Tests.fsproj reference \
    src/MlxOpenAIRouter.Core/MlxOpenAIRouter.Core.fsproj
dotnet add tests/MlxOpenAIRouter.Tests/MlxOpenAIRouter.Tests.fsproj reference \
    src/MlxOpenAIRouter.Web/MlxOpenAIRouter.Web.fsproj

# 5. NuGet packages
dotnet add src/MlxOpenAIRouter.Core/MlxOpenAIRouter.Core.fsproj package FSharp.SystemTextJson --version 1.4.36
dotnet add src/MlxOpenAIRouter.Core/MlxOpenAIRouter.Core.fsproj package JsonSchema.Net --version 9.2.0
dotnet add src/MlxOpenAIRouter.Core/MlxOpenAIRouter.Core.fsproj package FsToolkit.ErrorHandling --version 5.2.0

dotnet add src/MlxOpenAIRouter.Web/MlxOpenAIRouter.Web.fsproj package Serilog.AspNetCore
dotnet add src/MlxOpenAIRouter.Web/MlxOpenAIRouter.Web.fsproj package Serilog.Sinks.Console
dotnet add src/MlxOpenAIRouter.Web/MlxOpenAIRouter.Web.fsproj package Argu --version 6.2.5
dotnet add src/MlxOpenAIRouter.Web/MlxOpenAIRouter.Web.fsproj package prometheus-net.AspNetCore

dotnet add tests/MlxOpenAIRouter.Tests/MlxOpenAIRouter.Tests.fsproj package Expecto --version 10.2.1
dotnet add tests/MlxOpenAIRouter.Tests/MlxOpenAIRouter.Tests.fsproj package Microsoft.AspNetCore.Mvc.Testing

# 6. Minimal Program.fs (just /health)
cat > src/MlxOpenAIRouter.Web/Program.fs <<'EOF'
module MlxOpenAIRouter.Web.Program

open Microsoft.AspNetCore.Builder

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    let app = builder.Build()
    app.MapGet("/health", fun () -> {| status = "ok" |}) |> ignore
    app.Run("http://127.0.0.1:8001")
    0
EOF

# 7. Build + run
dotnet build
dotnet run --project src/MlxOpenAIRouter.Web/MlxOpenAIRouter.Web.fsproj &

# 8. Test
curl http://127.0.0.1:8001/health
# {"status":"ok"}

# 9. Iterate from here — Stage 1 starts.
```

---

## 17. 결론

이 문서는 **standalone smart router** 의 공학 설계서. 핵심 결정 정리:

| 결정 항목 | 답 |
|----------|-----|
| Stack | F# 10 / .NET 10 + ASP.NET Core minimal API + Kestrel |
| Architecture | Sidecar reverse-proxy (separate process), 3-project layout (Core / Web / Tests) |
| Backend coupling | Loose — config 의 UpstreamUrl 한 줄 |
| Scope v1 | 7 compensation strategies + capacity tracking + observability |
| Constrained decoding | Best-effort (Strategy B); engine-level true enforcement 는 v3+ |
| Deployment | macOS launchd plist + self-contained single binary (`dotnet publish -r osx-arm64 --self-contained -p:PublishSingleFile=true`) |
| Phasing | 6 stages to v0.1.0; 3 more to v0.4 |
| Async style | `task {}` exclusively (CI 가 Core 에 `async {}` 금지) |
| License | MIT 권장 |
| Naming | product `mlx-openai-router`, F# namespace `MlxOpenAIRouter` |
| Top-level dir | `[project-root]/` (이 문서의 placeholder; actual repo 명은 자유) |

**Source materials — empirical foundation 으로 새 repo 에 import 필요:**
- 7-gap compensation matrix (this 문서 §2 — 각 gap 의 expected vs observed 동작 + Strategy 매핑)
- 25 conformance probe 명세 (this 문서 §9.4 — 8 surface 분포 + probe ID 별 측정 의도)
- probe driver 의 architecture (probe-as-record JSONL append-flush per call; F# port 가능)
- mlx_lm.server 0.31.3 source-code 의 결정적 fact: `parse_request_body()` 가 `response_format` 미인식; `chat_template.jinja:85` 의 `raise_exception('System message must be at the beginning.')` 가 mid-conv role:system 거부의 직접 원인; `BatchGenerator` capacity `--decode-concurrency 32 --prompt-concurrency 8`

이 셋이 있어서 router 의 acceptance test 가 day 1 에 정의됨 — 즉 "25 conformance probe 위에서 23+ 가 PASS" 가 v0.1.0 의 acceptance criteria.

**Next step** (만약 시작한다면):
1. 새 repo 생성 (`mlx-openai-router` 또는 본인 선호 명)
2. Stage 0 (skeleton + compensation matrix import + probe fixture import) — 1-2일
3. Stage 1 (MVP forward proxy + 2 strategies) — 1주
4. Stage 2-5 — 4-5주
5. v0.1.0 alpha release — 누적 5-6주

이 문서 자체는 design RFC. 실제 implementation 결정 시 update.

---

*문서 작성: 2026-05-07*
*Source: mlx_lm.server 0.31.3 OpenAI-API conformance evaluation (25 probes / 8 surfaces; HIGH=3 / MEDIUM=2 / LOW=5 / PASS=15)*
*Stack: F# 10 / .NET 10 ASP.NET Core minimal API*
*다음 step: design 검토 후 repo 생성*
