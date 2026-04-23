---
created: 2026-04-23
description: HF 모델 레포가 Base인지 Instruct인지 이름이 아닌 4가지 구조적 지표로 판별하는 법
---

# LLM 모델이 Base인지 Instruct인지 확인하는 법

HF 레포 이름은 믿을 수 없다. 파일 시스템에 있는 4가지 구조적 지표로 판별한다.

## The Insight

**LLM 레포의 디렉토리명이나 파일명은 Base/Instruct 구분의 근거가 아니다.** `qwen2.5-32b-mlx`, `llama-3-8b`, `mistral-7b` 같은 이름은 계열만 알려줄 뿐 학습 상태를 말해주지 않는다. 실제 구분은 **토크나이저에 특수 토큰이 학습됐는가**로 결정되며, 이건 모델 디렉토리 안의 파일 4종으로 확인한다.

## Why This Matters

Base 모델을 chat 엔드포인트(`/v1/chat/completions`)에 먹이면 서버는 **조용히 실패하지 않는다. 조용히 이상하게 성공한다.** 200 OK를 반환하면서 system prompt를 응답에 그대로 echo하고, `<|fim_prefix|>` 같은 raw special token을 뱉고, 가상 "User:/Assistant:" 대화를 스스로 이어 쓴다. 네가 쓴 agent loop는 이걸 "형식이 잘못된 응답"으로 받아들여 JSON retry를 반복하다 결국 `InvalidJsonOutput`으로 종료한다. 진짜 원인은 **모델 자체가 chat을 못 한다**는 거지만 에러 메시지는 "JSON 파싱 실패"로 나온다.

이 오진단으로 평균 2-4시간을 잃는다. 구글링해도 "chat template 적용 버그", "mlx-lm 버전 이슈" 같은 잘못된 방향으로 빠지기 쉽다.

## Recognition Pattern

다음 중 하나라도 해당하면 이 체크를 돌린다:

- 로컬에 새 LLM을 내려받고 OpenAI-compat 서버(mlx_lm.server, vllm, llama.cpp server, TGI)로 올린 직후
- chat 응답에 `<|fim_prefix|>`, `<|fim_middle|>`, `<|fim_suffix|>` 같은 특수 토큰이 raw text로 섞여 나옴
- 응답이 system prompt를 재출력하거나 "User:", "Assistant:" 가상 대화를 혼자 이어 씀
- `finish_reason: "length"` 가 반복되고 `stop` 으로 끝나는 경우가 드뭄
- agent loop가 계속 "JSON 출력이 깨짐"으로 에러 종료
- HF에서 받은 레포 이름이 `-Instruct`, `-Chat`, `-sft`, `-dpo` 같은 명시적 접미사가 없음

## The Approach

구조적 지표 4개를 본다. **2개 이상이 Base 쪽으로 기울면 Base 모델**이다. 이름으로 판단하지 않는다.

### Step 1: special tokens 파일 존재 여부 (결정적 1줄 체크)

```bash
MODEL_DIR=~/path/to/your/model

ls "$MODEL_DIR/special_tokens_map.json" "$MODEL_DIR/added_tokens.json" 2>/dev/null
```

- 둘 다 파일 경로가 출력됨 → Instruct 후보 (다음 단계로)
- 한쪽이라도 `No such file or directory` → **Base 확정**. Instruct 모델은 `<|im_start|>`/`<|im_end|>` 같은 chat special token을 토크나이저에 추가하면서 반드시 이 두 파일을 생성한다.

### Step 2: tokenizer_config.json 안에 chat_template 필드가 있는가

```bash
python3 -c "
import json
c = json.load(open('$MODEL_DIR/tokenizer_config.json'))
print('chat_template present:', 'chat_template' in c)
print('length:', len(c.get('chat_template', '')))
"
```

- `present: True`, `length: 2000+` → Instruct 신호
- `present: False` → Base 강한 신호 (일부 최신 Instruct 레포는 외부 `chat_template.jinja`로 분리하므로 Step 3 보강 확인)

### Step 3: im_start/im_end 토큰이 실제로 학습됐는가

```bash
grep -o "<|im_start|>\|<|im_end|>" \
    "$MODEL_DIR/added_tokens.json" \
    "$MODEL_DIR/tokenizer.json" 2>/dev/null | sort -u
```

- 두 토큰이 모두 출력 → Instruct 확정
- 하나도 안 나옴 → Base 확정 (외부 chat_template.jinja를 만들어 끼워 넣어도 모델 자체가 이 토큰을 모르므로 raw text로 출력함)

### Step 4: 서버가 리포트하는 `/v1/models` id 확인 (서버가 이미 떠 있을 때만)

```bash
curl -s http://localhost:8000/v1/models | python3 -c "
import sys, json
r = json.load(sys.stdin)
for m in r['data']:
    print(m['id'])
"
```

id 중 어느 것에 `-Instruct`, `-Chat`, `-sft` 같은 접미사가 있는지 본다. Qwen의 경우:
- `Qwen/Qwen2.5-Coder-32B` → **Base Coder** (FIM 전용)
- `Qwen/Qwen2.5-Coder-32B-Instruct` → Coder Instruct

서버가 HF 레포 id를 파생적으로 리포트하는 경우가 있어 (config.json에서 내부 힌트로 결정), 디렉토리명과 다를 수 있다. 이게 네가 받은 실체에 가깝다.

## Example

Qwen2.5 32B 실제 사례 (2026-04-23 세션에서 나온 결과):

```bash
MODEL_DIR=~/llm-system/models/qwen32b

# Step 1
$ ls "$MODEL_DIR/special_tokens_map.json" "$MODEL_DIR/added_tokens.json"
ls: /Users/ohama/llm-system/models/qwen32b/special_tokens_map.json: No such file or directory
ls: /Users/ohama/llm-system/models/qwen32b/added_tokens.json: No such file or directory
# → Base 확정. 나머지 안 봐도 됨.

# Step 4로 확인 (참고용)
$ curl -s http://localhost:8000/v1/models | jq '.data[0].id'
"Qwen/Qwen2.5-Coder-32B"
# → -Instruct 접미사 없음. 역시 Base.
```

디렉토리 이름은 `qwen32b` 였고 다운로드 시 지정한 HF 레포는 `mlx-community/qwen2.5-32b-mlx` — 이름만으로는 Base/Instruct를 알 수 없었다. 하지만 구조적 지표는 즉시 Base를 가리켰다.

### 비교: 같은 세션의 72B (Instruct)

```bash
$ ls ~/llm-system/models/qwen72b/special_tokens_map.json
/Users/ohama/llm-system/models/qwen72b/special_tokens_map.json  # 존재

$ grep -o "<|im_start|>\|<|im_end|>" ~/llm-system/models/qwen72b/added_tokens.json | sort -u
<|im_end|>
<|im_start|>

$ curl -sX POST localhost:8001/v1/chat/completions \
    -H "Content-Type: application/json" \
    -d '{"model":"/Users/ohama/llm-system/models/qwen72b","messages":[{"role":"user","content":"Say OK"}],"max_tokens":10}' \
    | jq '.choices[0].message.content, .choices[0].finish_reason'
"OK"
"stop"
```

Instruct 확정. chat 정상 작동.

## 체크리스트

로컬에 새 LLM을 올린 직후 순서대로:

- [ ] `special_tokens_map.json` + `added_tokens.json` 둘 다 존재?
- [ ] `tokenizer_config.json`의 `chat_template` 필드 존재? (길이 1000+ 기대)
- [ ] `added_tokens.json` 또는 `tokenizer.json`에 `<|im_start|>`, `<|im_end|>` (또는 해당 모델 계열의 chat special token)?
- [ ] 서버 `/v1/models`의 id에 `-Instruct`, `-Chat`, `-sft`, `-dpo` 같은 접미사?
- [ ] 간단한 curl chat 요청에 `finish_reason: stop` + 정상 답변?

4개 이상 체크되면 Instruct. 체크 못 하면 **이름과 상관없이 Base**로 취급하고 교체.

## 잘못 판단했을 때의 비용

- **2-4시간 디버깅 손실** — "JSON 출력 버그" 방향으로 잘못 가면서 Router, Prompt, JSON schema, retry 로직 전부 의심
- **서버 버전 의심** — mlx-lm, vllm 업그레이드 시도 후 아무 효과 없음
- **Chat template 억지 주입 시도** — 외부 `chat_template.jinja`를 `tokenizer_config.json`에 merge해도 모델 weight가 special token을 모르므로 여전히 raw continuation (실제로 이 세션에서 시도됨 — 실패)

Step 1 한 줄이면 초장에 판정난다.
