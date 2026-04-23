---
created: 2026-04-23
description: 로컬 OpenAI-compat LLM 서버가 이상한 응답을 낼 때 3단계 체계적 격리로 원인 층을 좁히는 법
---

# 로컬 LLM 서버 이상 응답 체계적 디버깅

응답이 이상할 때 "서버 버그인가?", "내 코드 버그인가?", "모델 문제인가?"를 한 단계씩 제거한다.

## The Insight

로컬 LLM 서버(mlx_lm.server, vllm, llama.cpp server, TGI)가 이상한 응답을 낼 때 **원인은 반드시 다음 세 층 중 하나**다:

1. **클라이언트 request body가 네가 생각하는 것과 다르다** — 직렬화 오류, 필드 누락, 잘못된 messages 구조
2. **서버가 네 model id를 인식 못 한다** — HF Hub로 fallback resolve하거나 404
3. **서버가 request를 받았지만 chat template을 적용 안 한다** — 모델이 base라서, 서버 버전 이슈로, 또는 config 누락으로

이 셋은 **각각 다른 도구와 다른 명령**으로만 확인된다. 추측으로는 절대 좁혀지지 않는다. 한 층씩 관측 가능한 상태로 만들어 제거해야 한다. 순서는 1 → 2 → 3 — 아래층일수록 교체 비용이 크기 때문.

## Why This Matters

모르는 상태로 디버깅하면 각 층을 반복 건드리면서 결국 아무 것도 해결 못한다. 전형적 실패 패턴:

- 클라이언트 코드에 재시도 로직 추가 (1층으로 의심) — 효과 없음
- model id를 다른 걸로 바꿔봄 (2층 의심) — 다른 이상 증상 등장
- 서버 버전 업그레이드 (3층 의심) — 차이 없음
- 모델 재다운로드 — 같은 증상
- 결국 로그도 없이 "서버가 이상해요"로 끝

체계적으로 3층을 관측하면 **30분 안에 원인 층이 확정**된다.

## Recognition Pattern

이 방법이 필요한 상황:

- agent loop가 `InvalidJsonOutput`, `SchemaViolation` 같은 에러로 반복 종료되는데 코드에는 버그 없어 보임
- LLM이 system prompt를 무시하고 엉뚱한 응답 (프롬프트 echo, 가상 대화, raw special token)
- `curl` 단일 요청은 되는데 프레임워크를 통한 요청은 안 됨 (또는 반대)
- 서버 로그에는 정상, 클라이언트에선 에러
- 모델 업그레이드 없이 갑자기 증상 발생

## The Approach

**관측 가능하게 만든 뒤 한 층씩 배제**한다. 핵심은 "내가 보낸 것 vs 서버가 받은 것 vs 서버가 돌려준 것"을 분리해 각각 **raw bytes 수준**에서 보는 것.

### Step 1 — Layer 1: 클라이언트 실제 요청 본문 확인

**목표**: 네 코드가 서버로 **실제로 보낸 JSON**을 직접 본다.

두 방법 중 하나:

**A. 클라이언트 쪽에 request-body 로깅 한 줄 추가** (권장 — 빠르고 재현성 높음)

HTTP 어댑터의 "직렬화 후, POST 직전" 자리에 debug 로그 한 줄을 넣는다. 예시:

```fsharp
// F# / .NET HttpClient 예시
let body = buildRequestBody messages model
Log.Debug("POST {Url} body: {Body}", url, body)  // 추가
let! resp = httpClient.SendAsync(req, ct)
```

```python
# Python requests 예시
body = json.dumps(payload)
log.debug("POST %s body: %s", url, body)  # 추가
resp = requests.post(url, data=body)
```

레벨 스위치(Serilog `LoggingLevelSwitch`, Python `logging.getLogger().setLevel`)로 CLI 플래그 (`--trace`, `--debug`) 에 연동하면 운영에 흔적 없이 껐다 켠다.

**B. tcpdump/Wireshark (서버도 직접 못 건드릴 때만)**

```bash
sudo tcpdump -i lo0 -A -s 0 'tcp port 8000' | grep -A 100 '^POST'
```

Step 1에서 확인할 것:

```
POST http://127.0.0.1:8000/v1/chat/completions body: {
  "model": "...",
  "messages": [
    {"role": "system", "content": "..."},
    {"role": "user", "content": "..."}
  ],
  "max_tokens": 1024,
  ...
}
```

**판정**:
- `messages`가 배열 형태로 role/content 분리됐는가? (하나의 문자열로 합쳐있으면 client 버그)
- `model` 값이 서버가 아는 id인가? (Step 2에서 검증)
- 예상한 필드(`temperature`, `max_tokens`, `stream`)가 있는가?

1층이 정상이면 2층으로.

### Step 2 — Layer 2: 서버가 그 model id를 인식하는가

**목표**: 서버가 내 request의 `model` 필드를 어떻게 해석하는지 확인.

```bash
# 서버가 공식적으로 알고 있는 model id 조회
curl -s http://localhost:8000/v1/models | python3 -m json.tool
```

출력 예시:
```json
{
  "data": [
    {"id": "Qwen/Qwen2.5-Coder-32B", ...},
    {"id": "/Users/ohama/llm-system/models/qwen32b", ...}
  ]
}
```

**판정 규칙**: Step 1에서 본 `"model"` 값이 이 `data[*].id` 리스트 중 하나와 **완전 일치**해야 한다.

일치 안 하면 서버의 fallback 동작을 이해해야 한다:
- **mlx_lm.server / HF 계열**: 모르는 id는 HuggingFace Hub로 resolve 시도 → 404 (실 세션 예: `HTTP 404: Repository Not Found for url: https://huggingface.co/api/models/qwen2.5-coder-32b-instruct`)
- **vllm**: 보통 서버 시작 시 `--served-model-name`으로 고정된 id만 받음
- **llama.cpp server**: `model` 필드 무시 (어떤 값이든 받음)

**수정**: 클라이언트 측 `modelToName` 매핑을 서버가 리포트한 id 중 하나로 고정. 이식성 위해 서버 기동 시 `/v1/models`를 조회해 동적 결정하는 게 장기 해법이지만, 단기는 하드코딩도 OK.

2층이 정상인데도 응답이 이상하면 3층으로.

### Step 3 — Layer 3: chat template 적용 여부

**목표**: 서버가 request를 받았고 model도 찾았는데, 응답이 여전히 이상하면 chat template 적용 안 됐을 가능성.

증상:
- 응답이 system prompt 전체를 echo
- 응답에 `<|fim_*|>`, `<|im_start|>`, `<|endoftext|>` 같은 raw special token 출력
- 응답이 "User:", "Assistant:" 가상 대화를 스스로 이어씀
- `finish_reason: length` 가 반복

**1차 확인**: 모델이 Base인지 Instruct인지 먼저 확정. 자세한 판별은 `identify-base-vs-instruct-llm.md` 참고. Base면 chat template을 주입해도 소용없다 — **Base 모델 문제**로 분기.

**2차 확인 (Instruct 확정된 경우)**: 서버가 chat_template을 실제로 읽었는지.

```bash
# tokenizer_config.json에 chat_template 필드 존재?
python3 -c "
import json
c = json.load(open('$MODEL_DIR/tokenizer_config.json'))
print('chat_template present:', 'chat_template' in c)
print('length:', len(c.get('chat_template', '')))
"

# 외부 분리된 경우
ls "$MODEL_DIR/chat_template.jinja"
```

2026년 기준 일부 서버 버전은 외부 `chat_template.jinja` 파일을 지원하지 않는다. 이 경우 분리된 jinja 내용을 `tokenizer_config.json`의 `chat_template` 키로 병합:

```python
import json, shutil

base = "/path/to/model"
tc = f"{base}/tokenizer_config.json"
tmpl = f"{base}/chat_template.jinja"

shutil.copy(tc, tc + ".backup")
with open(tc) as f: cfg = json.load(f)
with open(tmpl) as f: cfg['chat_template'] = f.read()
with open(tc, 'w') as f: json.dump(cfg, f, ensure_ascii=False, indent=2)
```

서버 재시작 후 재검증. (Base 모델엔 이 수술도 무의미.)

### 최종 격리 테스트 — curl 원샷

세 층을 모두 한 번에 우회해 확인:

```bash
# 가장 작은 chat 요청, curl 직접
curl -s -X POST http://localhost:8000/v1/chat/completions \
    -H "Content-Type: application/json" \
    -d '{
      "model": "<서버가-리포트한-id>",
      "messages": [
        {"role": "system", "content": "You are a terse bot. Reply with exactly one word."},
        {"role": "user", "content": "Say OK"}
      ],
      "max_tokens": 20,
      "temperature": 0.0
    }' | python3 -m json.tool
```

기대:
```json
{
  "choices": [{
    "finish_reason": "stop",
    "message": {"role": "assistant", "content": "OK"}
  }]
}
```

- `finish_reason: stop` + 짧은 정답 → **서버/모델 OK**. 문제는 Layer 1 (네 클라이언트)에 있다.
- `finish_reason: length` 또는 raw special token → **Layer 3 문제**. 모델 Base 의심.
- 404/500 → **Layer 2 문제**. model id 불일치.

## Example — 실제 세션 디버깅 궤적 (2026-04-23)

**증상**: blueCode agent가 `List the files in src` 요청에 `InvalidJsonOutput`으로 두 번 종료. 똑같은 짓 무한 반복.

**Layer 1 확인**: `QwenHttpClient.CompleteAsync`에 `Log.Debug("POST body: {Body}", body)` 추가. `--trace`로 실행. stderr에:

```json
{"messages":[{"role":"system","content":"You are blueCode..."},{"role":"user","content":"List the files in src"}],"model":"/Users/ohama/llm-system/models/qwen32b",...}
```

messages 배열 정상, role 분리 정상. **Layer 1 OK.**

**Layer 2 확인**: `curl -s localhost:8000/v1/models`:

```json
{"data": [
  {"id": "Qwen/Qwen2.5-Coder-32B"},
  {"id": "/Users/ohama/llm-system/models/qwen32b"}
]}
```

request의 `"model"`이 두 번째 id와 일치. **Layer 2 OK.**

**Layer 3 확인**: 서버 응답 내용 (같은 `--trace` 로그):

```
"content": "You are blueCode, a coding agent...\n...Assistant: {...}\nUser: {...}\nAssistant: ...\n<|file_sep|><|fim_prefix|>system\n"
```

`<|fim_prefix|>` 등장. system prompt echo. 가상 대화 continuation. **Layer 3 이상.**

Base/Instruct 확인:

```bash
$ ls ~/llm-system/models/qwen32b/special_tokens_map.json
# No such file or directory
```

**Base 모델로 확정**. Chat template 병합해도 모델 토크나이저에 `<|im_start|>` 학습 안 됐으므로 무의미.

**해결**: Instruct 모델로 교체 또는 72B(Instruct 확인됨)로 우회. 실제 세션은 후자로 진행, UAT 통과.

**총 디버깅 시간**: 체계적 3-layer 격리로 약 40분. 같은 문제를 "뭔가 이상해요" 방식으로 접근하면 하루 단위로 깨짐.

## 체크리스트

로컬 LLM 서버 이상 응답 직면 시 순서대로:

- [ ] 클라이언트에 request-body 로깅 한 줄 (level-switch gated) 추가
- [ ] `--trace`/`--debug`로 실행해서 실제 POST body 확인
- [ ] body의 `messages` 배열 구조 정상?
- [ ] `curl -s /v1/models`로 서버가 아는 model id 조회
- [ ] request의 `"model"` 값이 그 id 리스트와 완전 일치?
- [ ] 불일치면 클라이언트 측 model mapping 수정
- [ ] 일치하는데도 이상하면 Base/Instruct 판별 (`identify-base-vs-instruct-llm.md`)
- [ ] Instruct면 chat_template 파일 위치/병합 확인
- [ ] curl 원샷으로 최소 chat 요청 → 최종 격리 테스트
- [ ] 원인 층 확정 후 해당 층만 수정

## 관련 문서

- `identify-base-vs-instruct-llm.md` — Layer 3에서 필요한 모델 판별
