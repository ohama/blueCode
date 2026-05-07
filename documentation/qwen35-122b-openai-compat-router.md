# mlx-openai-router — Qwen 122B 를 위한 standalone OpenAI-compatible smart router 설계

**작성일:** 2026-05-07
**Project codename:** `mlx-openai-router` (이 문서에서는 "Router" 로 표기)
**Goal:** mlx_lm.server (Apple Silicon, MoE 모델) 위에서 **OpenAI Python SDK 가 그대로 작동하는** 독립 reverse-proxy 를 만든다.

**Source — empirical evidence base:** mlx_lm.server 0.31.3 에 대한 25-probe / 8-surface OpenAI-API conformance evaluation. 측정 결과: HIGH=3 (response_format silent-ignore × 3), MEDIUM=2 (n>1 silent-1, logprobs unverified), LOW=5 (error envelope shape, /v1/responses 404, [DONE] sentinel timing 등), PASS=15 (tools/tool_choice 완전 conformant, parallel decode N=2 confirmed, mid-conv role:system rejection invariant 등). 상세 probe 명세 + verdict mapping 은 §2 (Compensation Surface) 와 §9.4 (Conformance probe replay) 에 inline. 이 문서를 새 repo 에 복사하면 self-contained — 외부 의존성 없음.

---

## 0. Project Goal

**한 줄:**
> Apple Silicon Mac 의 mlx_lm.server 위에 OpenAI Python SDK / openai-python / litellm / langchain 등 표준 client 가 코드 변경 없이 작동하도록 만드는 reverse-proxy.

**구체적으로:**

```python
# 사용자 입장에서는 이게 그대로 동작해야 함:
from openai import OpenAI

client = OpenAI(
    api_key="dummy",  # 라우터는 인증 안 함 (loopback only)
    base_url="http://127.0.0.1:8001/v1"  # 라우터 endpoint
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

**왜 standalone 인가** — 어떤 특정 client 와도 결합되지 않은 universal protocol shim. 누구든 `pip install mlx-openai-router` 하고 바로 쓸 수 있는 product. 자체 repo, 자체 CI, 자체 release cycle.

---

## 1. Scope & Non-Goals

### 1.1 In scope

| 영역 | 범위 |
|------|------|
| Backend | mlx_lm.server (mainline mlx-lm 0.31.x+), Apple Silicon Mac only |
| Models | MLX 4-bit MoE 계열 (Qwen 3.5 122B-A10B / 35B-A3B; Llama-MoE 계열 호환 가능) |
| API | OpenAI v1 — `/v1/chat/completions`, `/v1/completions`, `/v1/models`, `/v1/embeddings` (옵션) |
| Compensation | 아래 §2 의 7 gaps 모두 (response_format × 2, error envelope, n>1, mid-conv role, 429, /v1/responses) |
| Streaming | SSE chat/completions 양방향 — chunk shape 보정 + `[DONE]` 처리 |
| Auth | None for v1 (loopback only); v2 에서 bearer token 추가 |
| Multi-model | 단일 backend 시작; v2 에서 multi-backend (122B + 35B + cloud fallback) |

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
| Production-grade observability (Prometheus exporter, OpenTelemetry) | v2+. v1 은 structured JSON log + counters 만. |

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
4. 응답 content 가 parseable JSON 인지 검증 (`json.loads()` try)
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
4. 응답 → `jsonschema.validate(response_json, schema)`
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

**옵션 expose:** Router config 의 `mid_conv_system_strategy: "user_prefix" | "concat_first"` 으로 사용자가 선택.

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
   - K 개의 동일 request 를 `asyncio.gather` 로 병렬 fire (mlx_lm BatchGenerator 가 native batch — N=2 empirical: wall=max(t1,t2)=0.39s vs sum=0.76s, 즉 진짜 parallel decode)
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
1. Router 가 in-flight request count 를 atomic counter 로 추적.
2. Configurable threshold `max_concurrent` (default = 16, mlx_lm BatchGenerator capacity 32 의 절반).
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
- `[DONE]` sentinel 동작 — 현재 build 에서는 conformant (probe 14)

**Strategy H:**
1. Streaming request (`stream: true`) 도착 시 Router 가 `iter_lines()` 또는 SSE parser 로 forward.
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

8개 feature 모두 default ON. 사용자가 `--disable-feature N` 으로 끌 수 있음 (debugging 용).

---

## 3. Architectural Decision

5개 옵션 중 **Python FastAPI sidecar** 채택. 다른 옵션들의 trade-off 는 부록 (Section 11) 참고.

### 3.1 왜 Python FastAPI

- **Async I/O native** — SSE streaming + parallel fan-out 자연스러움
- **Eco** — `httpx`, `pydantic`, `jsonschema`, `uvicorn` 모두 표준
- **OpenAI-python SDK 가 Python** — 같은 stack, debug 쉬움
- **소스 가독성** — 미래 contributor 가 진입 쉬움
- **Container-friendly** — `pip install` 또는 docker; macOS launchd 와도 호환

### 3.2 왜 fork (mlx-openai-server) 가 아닌가

`mlx-openai-server` 는 mlx_lm 의 fork 로 **inference engine 자체** 를 변경. router 는 inference engine 에 무관해야 한다. 즉:
- Router 는 mlx_lm 0.31.3 이든 0.40.x 든 작동해야 함 (backend 교체에 강함)
- Fork 는 backend 자체를 swap — mlx_lm mainline 의 진보를 잃을 위험
- Fork 가 outlines (constrained decoding) 통합한다면 Router 가 그 fork 를 *backend 로* 사용하면 됨 — 둘은 layered

따라서 Router 는 **모든 OpenAI-compatible-or-not 한 mlx 계열 backend 위에서 작동** 하는 universal shim 으로 설계.

### 3.3 왜 nginx + Lua 가 아닌가

JSON schema validation, async fan-out, structured retry 가 Lua 에서 cumbersome. nginx 는 단순 reverse-proxy 에 강하지만 "smart" 요건의 절반 이상이 high-level 로직. Python 이 right tool.

### 3.4 왜 Go / Rust 가 아닌가

Performance 측면에서는 매력적. 그러나:
- Single-user personal tool 의 throughput 요구가 ms 단위 — Python 이면 충분
- Mac dev 환경에서 Python venv 가 정착돼 있음 (.venv-eval 같은 패턴)
- v2+ 에서 hot path 만 Rust extension (PyO3) 으로 옮길 수 있음

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

```
mlx-openai-router/
├── pyproject.toml                       # build + deps
├── README.md
├── LICENSE                              # MIT or Apache-2.0
├── CHANGELOG.md
├── .python-version                      # 3.12+
├── src/
│   └── mlx_openai_router/
│       ├── __init__.py
│       ├── __main__.py                  # `python -m mlx_openai_router`
│       ├── main.py                      # FastAPI app, route dispatch
│       ├── config.py                    # pydantic-settings, env vars + YAML
│       ├── upstream.py                  # async httpx client with pooling
│       ├── strategies/
│       │   ├── __init__.py
│       │   ├── response_format.py       # Strategy A + B
│       │   ├── role_translation.py      # Strategy E
│       │   ├── error_envelope.py        # Strategy C
│       │   ├── n_fanout.py              # Strategy D
│       │   ├── stateful_responses.py    # Strategy G (v1: stub; v2: full)
│       │   ├── capacity.py              # Strategy F (counter + threshold)
│       │   └── streaming.py             # Strategy H (SSE chunk normalization)
│       ├── models.py                    # pydantic OpenAI spec models
│       ├── schema_validate.py           # jsonschema wrapper with retry
│       ├── observability.py             # structured logging, metrics
│       ├── recovery.py                  # 3-stage JSON extraction (bare/brace/fence)
│       └── exceptions.py                # Router-internal exceptions → OpenAI errors
├── tests/
│   ├── unit/
│   │   ├── test_response_format.py
│   │   ├── test_role_translation.py
│   │   ├── test_n_fanout.py
│   │   └── ... (one per strategy)
│   ├── integration/
│   │   ├── test_e2e_chat.py             # against real mlx_lm
│   │   ├── test_e2e_streaming.py
│   │   └── test_e2e_n_fanout.py
│   ├── conformance/
│   │   ├── test_openai_sdk.py           # openai-python 으로 호출 → 정상?
│   │   └── test_conformance_probes.py   # 25 conformance probes 가 모두 PASS or HIGH→PASS conversion
│   └── fixtures/
│       └── conformance-probes.jsonl     # 25-record probe transcript (Stage 0 에 import)
├── benchmarks/
│   ├── latency.py                       # router 추가 latency 측정 (vs mlx_lm 직접)
│   └── throughput.py                    # n>1 fan-out 효율
├── docs/
│   ├── architecture.md
│   ├── strategies.md
│   ├── deployment.md
│   ├── development.md
│   └── compensation-matrix.md           # 7 gaps × 8 strategies × verdict mapping (empirical)
├── packaging/
│   ├── launchd/
│   │   └── com.ohama.mlx-openai-router.plist
│   ├── docker/
│   │   └── Dockerfile
│   └── homebrew/
│       └── mlx-openai-router.rb         # brew tap formula (v2+)
└── scripts/
    ├── install.sh
    ├── uninstall.sh
    └── status.sh                        # quick health check
```

### 5.1 Dependencies (pyproject.toml)

```toml
[project]
name = "mlx-openai-router"
version = "0.1.0"
description = "OpenAI-compatible smart router for mlx_lm.server"
requires-python = ">=3.12"

dependencies = [
    "fastapi>=0.115",
    "uvicorn[standard]>=0.30",
    "httpx>=0.27",
    "pydantic>=2.7",
    "pydantic-settings>=2.4",
    "jsonschema>=4.21",
    "structlog>=24.1",          # structured logging
    "prometheus-client>=0.20",  # metrics
]

[project.optional-dependencies]
dev = [
    "pytest>=8.0",
    "pytest-asyncio>=0.23",
    "ruff>=0.6",
    "mypy>=1.10",
    "openai>=1.40",             # SDK conformance test
]

[project.scripts]
mlx-openai-router = "mlx_openai_router.__main__:main"
```

### 5.2 Code skeleton (main.py)

```python
from fastapi import FastAPI, Request, HTTPException
from fastapi.responses import JSONResponse, StreamingResponse
from .config import Settings
from .strategies import (
    response_format,
    role_translation,
    error_envelope,
    n_fanout,
    capacity,
    streaming,
    stateful_responses,
)
from .upstream import UpstreamClient
from .observability import setup_logging, request_counter

settings = Settings()
setup_logging(settings.log_level)
upstream = UpstreamClient(settings.upstream_url, settings.upstream_timeout)
capacity_tracker = capacity.CapacityTracker(settings.max_concurrent)

app = FastAPI(title="mlx-openai-router", version="0.1.0")

@app.middleware("http")
async def trace_id_middleware(request: Request, call_next):
    trace_id = request.headers.get("x-trace-id") or generate_trace_id()
    request.state.trace_id = trace_id
    response = await call_next(request)
    response.headers["x-router-trace-id"] = trace_id
    return response

@app.post("/v1/chat/completions")
async def chat_completions(request: Request):
    body = await request.json()
    
    # Capacity check
    async with capacity_tracker.acquire_or_429():
        # Strategy E: mid-conv role:system rewrite
        body = role_translation.rewrite(body, settings.mid_conv_strategy)
        
        # Strategy A/B: response_format extraction
        rf = body.pop("response_format", None)
        if rf:
            body = response_format.inject_instruction(body, rf)
        
        # Strategy D: n>1 fan-out
        n = body.pop("n", 1)
        if n > 1:
            return await n_fanout.execute(upstream, body, n)
        
        # Streaming dispatch
        if body.get("stream"):
            return StreamingResponse(
                streaming.process(upstream, body),
                media_type="text/event-stream",
            )
        
        # Standard forward
        upstream_resp = await upstream.post("/v1/chat/completions", body)
        
        if upstream_resp.status_code != 200:
            return error_envelope.normalize(upstream_resp)
        
        # Strategy A/B post-validate
        if rf:
            upstream_resp = await response_format.post_validate(
                upstream, body, upstream_resp, rf,
            )
        
        # Inject compensation header
        return JSONResponse(
            content=upstream_resp.json(),
            headers={"x-router-compensation-applied": ",".join(applied)},
        )

@app.get("/health")
async def health():
    return {"status": "ok"}

@app.get("/health/upstream")
async def health_upstream():
    return await upstream.health_probe()
```

(실제 구현은 더 많은 edge case + error handling)

---

## 6. Configuration

### 6.1 Layers (precedence: high → low)

1. CLI flags (`--port`, `--upstream-url`, ...)
2. Environment variables (`MLX_ROUTER_PORT`, `MLX_ROUTER_UPSTREAM_URL`, ...)
3. YAML config file (`~/.config/mlx-openai-router/config.yaml`)
4. Defaults (in code)

### 6.2 Config schema (pydantic)

```python
class Settings(BaseSettings):
    # Server
    host: str = "127.0.0.1"
    port: int = 8001
    log_level: str = "INFO"
    
    # Upstream
    upstream_url: str = "http://127.0.0.1:8002"
    upstream_timeout: float = 300.0
    
    # Capacity
    max_concurrent: int = 16
    
    # Strategies
    mid_conv_strategy: Literal["user_prefix", "concat_first"] = "user_prefix"
    response_format_max_retries: int = 1
    n_fanout_max: int = 32
    
    # Streaming
    stream_buffer_chunks: int = 4
    
    # /v1/responses
    stateful_emulation: bool = False  # v1: False; v2: True
    stateful_ttl_seconds: int = 3600
    
    # Observability
    metrics_enabled: bool = True
    metrics_path: str = "/metrics"
    
    class Config:
        env_prefix = "MLX_ROUTER_"
        env_nested_delimiter = "__"
```

### 6.3 Sample YAML

```yaml
# ~/.config/mlx-openai-router/config.yaml
host: 127.0.0.1
port: 8001
log_level: INFO

upstream_url: http://127.0.0.1:8002
upstream_timeout: 300.0

max_concurrent: 16

mid_conv_strategy: user_prefix
response_format_max_retries: 1
n_fanout_max: 16

metrics_enabled: true
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
- Router 는 backend 에 무관 — config 의 `upstream_url` 만 변경하면 됨
- Trade-off: backend stability / 성능 / 양자화 호환성 새로 검증 필요

**Option B: Router 가 inference 직접 (engine 통합)**
- `outlines` 또는 `lm-format-enforcer` 를 Router 안에 통합
- Router 가 직접 model load + sampling — 더 이상 reverse-proxy 가 아닌 standalone server
- 이는 v3+ 영역 — Router 의 scope 을 넘어섬

Router v1/v2 는 prompt-instructed best-effort 가 충분하다는 가정 위에 작동. 50-invocation prompt-instructed schema test 에서 50/50 perfect compliance 가 empirical 으로 관찰됨 (Qwen 3.5 122B-A10B-4bit MoE; thinking-mode disabled). 즉 well-structured prompt 만으로도 실용 수준의 conformance 가능.

---

## 8. Deployment

### 8.1 macOS launchd (default)

```xml
<!-- packaging/launchd/com.ohama.mlx-openai-router.plist -->
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.ohama.mlx-openai-router</string>

    <key>ProgramArguments</key>
    <array>
        <string>/usr/local/opt/mlx-openai-router/.venv/bin/uvicorn</string>
        <string>mlx_openai_router.main:app</string>
        <string>--host</string>
        <string>127.0.0.1</string>
        <string>--port</string>
        <string>8001</string>
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
        <key>MLX_ROUTER_UPSTREAM_URL</key>
        <string>http://127.0.0.1:8002</string>
        <key>MLX_ROUTER_LOG_LEVEL</key>
        <string>INFO</string>
    </dict>
</dict>
</plist>
```

### 8.2 Install / Uninstall scripts

```bash
# scripts/install.sh
#!/usr/bin/env bash
set -euo pipefail

PREFIX=${PREFIX:-/usr/local/opt/mlx-openai-router}
PLIST_DIR=~/Library/LaunchAgents

# 1. Create venv
python3 -m venv "$PREFIX/.venv"
"$PREFIX/.venv/bin/pip" install --upgrade pip
"$PREFIX/.venv/bin/pip" install mlx-openai-router

# 2. Install plist
cp packaging/launchd/com.ohama.mlx-openai-router.plist "$PLIST_DIR/"

# 3. Critical: change mlx_lm port from 8001 → 8002
echo "WARNING: This script changes ~/Library/LaunchAgents/com.ohama.qwen122b.plist"
echo "         to use port 8002 instead of 8001 (router takes 8001)"
read -p "Continue? [y/N] " yn
case $yn in
    [Yy]*) ;;
    *) echo "Aborted"; exit 1 ;;
esac

# Find and replace port in mlx_lm plist
sed -i.backup 's|<string>8001</string>|<string>8002</string>|' \
    "$PLIST_DIR/com.ohama.qwen122b.plist"

# 4. Reload services
launchctl unload "$PLIST_DIR/com.ohama.qwen122b.plist"
launchctl load -w "$PLIST_DIR/com.ohama.qwen122b.plist"
launchctl load -w "$PLIST_DIR/com.ohama.mlx-openai-router.plist"

# 5. Wait for both ready
echo "Waiting for mlx_lm @ 8002..."
until curl -fsS http://127.0.0.1:8002/v1/models > /dev/null; do sleep 5; done

echo "Waiting for router @ 8001..."
until curl -fsS http://127.0.0.1:8001/health > /dev/null; do sleep 2; done

echo "Done. Test: curl http://127.0.0.1:8001/v1/models"
```

### 8.3 Docker (optional, v2)

```dockerfile
# packaging/docker/Dockerfile
FROM python:3.12-slim

WORKDIR /app
COPY pyproject.toml /app/
RUN pip install --no-cache-dir .

EXPOSE 8001
CMD ["uvicorn", "mlx_openai_router.main:app", "--host", "0.0.0.0", "--port", "8001"]
```

Mac 에서는 launchd 가 native — Docker 는 unnecessary. v2 에서 Linux 배포용으로만.

---

## 9. Testing Strategy

### 9.1 Unit tests

각 strategy module 의 input/output 검증. mock upstream client.

```python
# tests/unit/test_response_format.py
def test_json_object_injection():
    body = {"messages": [{"role": "user", "content": "Hi"}]}
    rf = {"type": "json_object"}
    
    result = response_format.inject_instruction(body, rf)
    
    assert "Output ONLY valid JSON" in result["messages"][-1]["content"]
```

목표: 90%+ coverage of strategy logic.

### 9.2 Integration tests (against real mlx_lm)

전체 router → mlx_lm forward path 검증. CI 에서는 mlx_lm 이 없으므로 mock backend; local dev 에서는 실제.

```python
# tests/integration/test_e2e_chat.py
@pytest.mark.real_backend
async def test_response_format_json_object_real():
    async with router_test_client() as client:
        response = await client.post("/v1/chat/completions", json={
            "model": "qwen122b",
            "messages": [{"role": "user", "content": "Output JSON: {greeting:string}"}],
            "response_format": {"type": "json_object"},
        })
        
        assert response.status_code == 200
        body = response.json()
        content = body["choices"][0]["message"]["content"]
        parsed = json.loads(content)  # Must parse — Strategy A retry guarantees
        assert "greeting" in parsed
```

### 9.3 Conformance tests

OpenAI Python SDK 가 실제로 작동하는지 검증.

```python
# tests/conformance/test_openai_sdk.py
def test_openai_sdk_chat_completions_basic():
    client = OpenAI(api_key="dummy", base_url="http://127.0.0.1:8001/v1")
    
    response = client.chat.completions.create(
        model="qwen122b",
        messages=[{"role": "user", "content": "Hi"}],
    )
    
    assert response.choices[0].message.content
    assert response.choices[0].finish_reason in ("stop", "length")
```

8개 feature 각각에 대해 conformance test 1개. 총 ~10-15 테스트.

### 9.4 Conformance probe regression replay

Stage 0 에 import 된 25 conformance probe (`tests/fixtures/conformance-probes.jsonl` + 그것을 생성하는 driver) 를 Router 위에서 재실행. 각 probe 의 verdict 가 mlx_lm 직접 호출 vs Router 호출에서 어떻게 변하는지 측정.

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

이게 Router 의 진짜 acceptance test — 단순 "test 통과" 가 아니라 **측정 가능한 conformance 향상**. probe driver 와 fixture JSONL 은 Stage 0 의 첫 작업.

### 9.5 Performance benchmarks

```python
# benchmarks/latency.py
# Router 추가 latency 측정 — 목표: ≤20ms p50, ≤50ms p99 (vs mlx_lm 직접)
```

mlx_lm 의 자체 latency (TTFT 222ms warm) 대비 router overhead 는 무시할 만해야.

---

## 10. Phasing (Project Roadmap)

Standalone product 의 development phase. 각 phase = 1-2주 스프린트.

### Stage 0: Project init
- pyproject.toml + repo skeleton
- README.md draft
- Compensation matrix (7 gaps + strategies + verdict mapping) → docs/compensation-matrix.md
- conformance probe driver + 25-record fixture JSONL → tests/fixtures/
- License 결정 (MIT or Apache-2.0)
- CI setup (GitHub Actions or local)

### Stage 1: MVP forward proxy
- FastAPI app + uvicorn
- `/v1/chat/completions`, `/v1/models`, `/v1/completions` passthrough
- Strategy C (error envelope normalization) only
- Strategy F (capacity 429 emulation)
- Health endpoints
- Unit + 1 integration test

**Acceptance:** OpenAI Python SDK 의 basic chat completion call 작동. Router 추가 latency p50 ≤ 20ms.

### Stage 2: Compensation Strategies A + B + E
- Strategy A (json_object enforce)
- Strategy B (json_schema enforce)
- Strategy E (mid-conv role:system rewrite)
- Recovery (3-stage JSON extraction)

**Acceptance:** 3 HIGH-severity probes (response_format json_object / json_schema strict / json_object rerun) 가 Router 위에서 PASS. Mid-conv role:system probe 가 PASS (mid → user 변환). 즉 conformance 가 mlx_lm 직접 vs Router 비교에서 +4 PASS 변환.

### Stage 3: Streaming + n>1
- Strategy H (streaming chunk normalization)
- Strategy D (n>1 fan-out)

**Acceptance:** OpenAI SDK 의 `stream=True` 와 `n=3` 정상 작동. 4 streaming probes + concurrency probe (N=2) 모두 PASS.

### Stage 4: Observability + admin
- Structured logging (structlog)
- Prometheus metrics (`/metrics`)
- `/admin/*` endpoints

**Acceptance:** prometheus 스크래핑으로 in-flight count, p50/p99 latency, error rate 측정 가능.

### Stage 5: Packaging + deployment
- launchd plist + install/uninstall scripts (with backend port migration helper)
- README 마무리 + deployment.md
- v0.1.0 PyPI release

**Acceptance:** `pip install mlx-openai-router && mlx-openai-router-install` 한 줄로 배포. Conformance probe replay 결과 HIGH=0, MEDIUM=0, LOW≤2, PASS≥23 — v0.1.0 acceptance criteria 만족.

### Stage 6 (v0.2): Stateful /v1/responses (G-b)
- in-memory state store
- TTL eviction
- Optional 활성화

### Stage 7 (v0.3): Multi-backend
- Config 의 `upstream_urls: [...]` 지원
- Round-robin 또는 model-based routing
- Fallback to cloud (OpenAI / Anthropic / etc.) 옵션

### Stage 8 (v0.4): Auth + multi-tenant
- Bearer token + per-key rate limit
- TLS

총 8 stages. v0.1.0 = Stage 0-5 (5-10주), v0.2-v0.4 = 추가 6-12주.

---

## 11. Architectural Options 비교 (부록)

본문 §3 에서 Python FastAPI sidecar 채택. 다른 옵션들의 detail.

| Option | LOC | 강점 | 약점 |
|--------|-----|------|------|
| **Python FastAPI** ★ | 1500-2500 | async native, eco, debuggable | GIL (single-user 무관) |
| F# Kestrel + ASP.NET | 2000-3000 | strong typing, .NET native | F# ASP.NET 생태계 작음 |
| Go (chi or gin) | 1500-2500 | fast, single binary deploy | learning curve, JSON schema 라이브러리 약 |
| Rust (axum) | 2000-3500 | fastest, memory-safe | dev velocity 낮음 |
| nginx + Lua | 500-1000 | production-grade | logic limit, JSON schema 어려움 |
| `mlx-openai-server` fork swap | 0 (swap) | zero code, immediate | inference engine 변경, maintenance risk |

Python 이 ROI 가장 좋음 — single user, dev velocity 우선, async I/O 자연.

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

**언제 Router 가 나은가:**
- Backend 를 swap 가능하게 유지하고 싶음 (mlx_lm → vLLM → cloud)
- mainline mlx-lm 의 발전을 즉시 누리고 싶음
- compensation 외 features (multi-backend routing, observability) 추가 가능성

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
- Config: `backends: [{name: "qwen122b", url: "http://...:8002"}, {name: "qwen35b", url: "http://...:8000"}, {name: "claude", url: "https://api.anthropic.com/...", api_key: "..."}]`
- Request 의 `model` field 로 backend 선택
- Cloud fallback (mlx 가 down 이면 Claude 로 자동)

### 13.2 Caching
- Idempotent request (`temperature: 0`) 의 응답 캐싱
- Cache key = hash(messages + sampling_params)
- TTL + LRU eviction

### 13.3 Auth
- Bearer token validation
- Per-key rate limit
- Audit log

### 13.4 Smart routing by task type
- Code task → 122B
- Quick chat → 35B
- Vision → cloud (mlx 안 됨)
- Heuristic 또는 small classifier model

### 13.5 RAG sidecar (optional)
- Vector DB integration (Chroma, Qdrant)
- Auto-augment user message with retrieved chunks before forward

---

## 14. Open Questions (v1 ship 전 결정 필요)

1. **License?** MIT (maximum permissive) vs Apache-2.0 (patent grant) vs proprietary. → 권장: **MIT** (PyPI eco 정착).
2. **Repo location?** GitHub public repo from day 1 vs. private until v0.1.0.
3. **Versioning?** SemVer 적용. v0.x 는 breaking change 허용.
4. **Python 최소 버전?** 3.12 (modern type hints) vs 3.11 (broader compat). → 권장: **3.12+**.
5. **Async runtime?** asyncio (default) vs trio. → 권장: **asyncio** (eco compat).
6. **Default port?** 8001 (mlx_lm 와 충돌하므로 swap 강제) vs 8080 (browser default) vs 11434 (Ollama-compat). → 권장: **8001 + install script 가 mlx_lm 을 8002 로 이동**.
7. **명명?** `mlx-openai-router` (이 문서) vs `qwen-router` (제한적) vs `openai-shim-mlx` (clear) vs `mlx-bridge`. → 권장: **`mlx-openai-router`** — 검색 가능, 의도 명확.
8. **Strict schema 의 강제 정도?** Strategy B 의 max retry. 1회 (default) vs 2회 (more aggressive) vs 0 (post-validate only no retry).
9. **`n>1` 의 max?** 16 (보수) vs 32 (BatchGenerator capacity 그대로). → 권장: **16** (latency 보호).
10. **`/metrics` 의 cardinality 폭발 위험?** label 에 model name 포함 = 모델 추가될 때마다 차원 증가. → 권장: model 별 metric 분리 (label 안 사용).

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

### 15.3 Personal tool 의 productization 경로
- 사용자 본인의 daily-driver 외에 community user 에게도 가치
- Open-source 시 Apple Silicon 지역 LLM 생태계의 표준 shim 후보 (Ollama 와 다른 niche — Ollama 는 자체 양자화 형식; Router 는 mlx 양자화)

### 15.4 Research / 측정 가능성
- conformance test 가 versioned — mlx_lm 새 release 될 때마다 자동 회귀 검출
- Empirical evaluation 이 future-proof — probe set 이 product 의 internal test fixture 가 되어 mlx_lm 새 release 마다 자동 회귀 검출

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
python3 -m venv .venv
.venv/bin/pip install --upgrade pip

# 2. pyproject.toml + skeleton
cat > pyproject.toml <<'EOF'
[project]
name = "mlx-openai-router"
version = "0.1.0a1"
description = "OpenAI-compatible smart router for mlx_lm.server"
requires-python = ">=3.12"
dependencies = ["fastapi", "uvicorn[standard]", "httpx", "pydantic", "pydantic-settings", "jsonschema", "structlog"]

[build-system]
requires = ["hatchling"]
build-backend = "hatchling.build"
EOF

mkdir -p src/mlx_openai_router tests
touch src/mlx_openai_router/__init__.py

# 3. Minimal main.py (just /health and forwarding)
cat > src/mlx_openai_router/main.py <<'EOF'
from fastapi import FastAPI
import httpx

app = FastAPI(title="mlx-openai-router", version="0.1.0a1")
upstream = httpx.AsyncClient(base_url="http://127.0.0.1:8002")

@app.get("/health")
async def health():
    return {"status": "ok"}

@app.api_route("/v1/{path:path}", methods=["GET", "POST"])
async def proxy(request, path):
    upstream_resp = await upstream.request(
        method=request.method,
        url=f"/v1/{path}",
        content=await request.body(),
    )
    return upstream_resp.json()
EOF

# 4. Install + run
.venv/bin/pip install -e .
.venv/bin/uvicorn mlx_openai_router.main:app --port 8001 &

# 5. Test
curl http://127.0.0.1:8001/health
# {"status":"ok"}

# 6. Iterate from here — Stage 1 starts.
```

---

## 17. 결론

이 문서는 **standalone smart router** 의 공학 설계서. 핵심 결정 정리:

| 결정 항목 | 답 |
|----------|-----|
| Stack | Python 3.12 + FastAPI + httpx + pydantic + jsonschema |
| Architecture | Sidecar reverse-proxy (separate process) |
| Backend coupling | Loose — config 의 upstream_url 한 줄 |
| Scope v1 | 7 compensation strategies + capacity tracking + observability |
| Constrained decoding | Best-effort (Strategy B); engine-level true enforcement 는 v3+ |
| Deployment | macOS launchd plist; Docker (v2) |
| Phasing | 6 stages to v0.1.0; 3 more to v0.4 |
| License | MIT 권장 |
| Naming | `mlx-openai-router` |

**Source materials — empirical foundation 으로 새 repo 에 import 필요:**
- 7-gap compensation matrix (this 문서 §2 — 각 gap 의 expected vs observed 동작 + Strategy 매핑)
- 25 conformance probe 명세 (this 문서 §9.4 — 8 surface 분포 + probe ID 별 측정 의도)
- probe driver 의 architecture (probe-as-record JSONL append-flush per call; bash dispatcher + Python helper 패턴; bench gate sandwich invariant)
- mlx_lm.server 0.31.3 source-code 의 결정적 fact: `parse_request_body()` 가 `response_format` 미인식; `chat_template.jinja:85` 의 `raise_exception('System message must be at the beginning.')` 가 mid-conv role:system 거부의 직접 원인; `BatchGenerator` capacity `--decode-concurrency 32 --prompt-concurrency 8`

이 셋이 있어서 router 의 acceptance test 가 day 1 에 정의됨 — 즉 "25 conformance probe 위에서 23+ 가 PASS" 가 v0.1.0 의 acceptance criteria.

**Next step** (만약 시작한다면):
1. 새 repo 생성 (`mlx-openai-router`)
2. Stage 0 (skeleton + compensation matrix import + probe fixture import) — 1-2일
3. Stage 1 (MVP forward proxy + 2 strategies) — 1주
4. Stage 2-5 — 4-5주
5. v0.1.0 alpha release — 누적 5-6주

이 문서 자체는 design RFC. 실제 implementation 결정 시 update.

---

*문서 작성: 2026-05-07*
*Source: mlx_lm.server 0.31.3 OpenAI-API conformance evaluation (25 probes / 8 surfaces; HIGH=3 / MEDIUM=2 / LOW=5 / PASS=15)*
*다음 step: design 검토 후 repo 생성*
