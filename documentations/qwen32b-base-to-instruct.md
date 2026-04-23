# 32B 모델 교체 가이드 — Base Coder → Instruct

> **TL;DR** 현재 받은 `~/llm-system/models/qwen32b/`는 **Base Coder** (FIM 전용).
> Chat API로는 의미 없는 응답만 나온다. **Instruct 변종으로 교체해야** default
> intent 라우팅 (`--model 32b` 및 기본 Qwen32B 라우팅) 이 정상 작동한다.
> 교체는 서비스 중지 → 기존 모델 rename(백업) → 새 모델 다운로드 → 서비스 재시작
> → smoke test 순으로 **30~45분** (네트워크에 따라) 걸린다.

---

## 1. 이 증상이면 해당 가이드 대상이다

`blueCode --trace "<prompt>"` 실행 후 stderr 에 다음 중 하나라도 보이면 이 문서 적용 대상.

| 증상 | 확인 명령 | 의미 |
|------|-----------|------|
| 응답에 `<\|fim_prefix\|>`, `<\|fim_middle\|>`, `<\|fim_suffix\|>` 토큰이 raw text로 등장 | `grep 'fim_' /tmp/trace.log` | FIM 모델 (Base Coder) 이 chat input 을 code completion 용 context 로 해석 |
| 응답이 "Assistant:", "User:" 같은 가상 대화를 스스로 이어 씀 | stdout에 system prompt 전체가 echo 된 뒤 루프 | chat template 미적용 상태의 base model continuation |
| `GET /v1/models` 의 `data[0].id` 가 `Qwen/Qwen2.5-Coder-32B` | `curl -s localhost:8000/v1/models \| jq '.data[0].id'` | **Base** Coder (Instruct 는 `-Instruct` 접미사를 붙임) |
| blueCode 최종 에러가 `InvalidJsonOutput` 반복 | `[WRN] Session error: InvalidJsonOutput` | LLM 이 스키마 JSON 생성 못함 — base model 특성 |

### 결정적 1줄 체크

```bash
ls /Users/ohama/llm-system/models/qwen32b/special_tokens_map.json \
   /Users/ohama/llm-system/models/qwen32b/added_tokens.json 2>/dev/null
```

둘 다 `No such file or directory` 면 **확정**: 현재 모델에 `<|im_start|>`/`<|im_end|>` 가 학습되지 않았다. 즉 Base 모델.

---

## 2. Qwen2.5 32B 계열 모델 지도

Instruct 모델만이 chat API에 쓸 수 있다. 아래 표에서 **Base** 열의 모델은 전부 코드 자동완성/continuation 용이고, blueCode agent loop 에는 부적합.

| HF 레포 | 종류 | 용도 | MLX 변형 예시 |
|---------|------|------|---------------|
| `Qwen/Qwen2.5-32B` | Base (general) | 일반 continuation, fine-tune 기반 | `mlx-community/qwen2.5-32b-mlx` ← **지금 받은 것** |
| `Qwen/Qwen2.5-32B-Instruct` | Instruct | 일반 chat | `mlx-community/Qwen2.5-32B-Instruct-4bit-MLX` |
| `Qwen/Qwen2.5-Coder-32B` | **Base Coder** | FIM, code completion | — |
| `Qwen/Qwen2.5-Coder-32B-Instruct` | **Coder Instruct** ⭐ | **코딩 agent용 chat** | `mlx-community/Qwen2.5-Coder-32B-Instruct-4bit` |

> 서버가 현재 `id=Qwen/Qwen2.5-Coder-32B` 를 리포트하는 이유는 HF config 의
> 내부 식별자가 재-양자화/재-포맷 과정에서 Coder 기반으로 찍힌 것이다.
> 디렉토리 이름(`qwen2.5-32b-mlx`)과 서버의 리포트된 id 사이에 불일치가 있을 수 있으니
> **이름만 보고 판단하지 말고** §1의 특수 토큰 존재 여부로 판별해야 한다.

### 권장 교체 대상

**`mlx-community/Qwen2.5-Coder-32B-Instruct-4bit`**

근거:
- Coder 특화 → blueCode 의 파일 읽기/수정/쉘 실행 도메인에 최적
- Instruct → chat API + JSON 출력 제약을 학습으로 따를 수 있음
- 4-bit 양자화 → 현재 디스크 사용량(17GB)과 비슷한 규모 유지

대안 고려:
- **8-bit 변형**: 정확도 up, 메모리 ~2배 필요 (~34GB). M-series 128GB 이상 권장.
- **`-Instruct-MLX`** (non-quantized 또는 bf16): 최대 정확도, 60GB+
- **일반 `Qwen2.5-32B-Instruct` (non-Coder)**: Coder 특화 손실, 다만 자연어 task 포괄적

HF 페이지에서 정확한 레포 존재 여부를 한 번 확인한 뒤 다운로드한다.

```bash
# 레포 존재 확인 (HF API 공개 — 인증 불필요)
curl -s "https://huggingface.co/api/models/mlx-community/Qwen2.5-Coder-32B-Instruct-4bit" \
    | python3 -c "import sys,json; r=json.load(sys.stdin); print('id:', r.get('id')); print('pipeline:', r.get('pipeline_tag'))"
```

`id` 가 해당 레포 이름으로 돌아오면 존재. 404 에러면 레포명을 HF 검색으로 찾은 뒤 §3 의 repo_id 를 교체.

---

## 3. 교체 절차

### 3.1 사전 점검

```bash
# 현재 모델 파일 상태 기록 (정리 안전 확보용)
ls -la ~/llm-system/models/qwen32b/ | head -20
du -sh ~/llm-system/models/qwen32b/

# 서비스가 지금 떠 있는지
launchctl list | grep qwen32b
lsof -iTCP:8000 -sTCP:LISTEN

# 디스크 여유 (새 모델 + 기존 백업 동시에 들고 있을 자신이 없으면 §3.3 A 루트)
df -h ~/
```

### 3.2 서비스 중지

로드는 유지하고 프로세스만 내리는 대신, 모델 교체 중에는 **완전히 언로드**하는 게 안전하다.

```bash
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen32b.plist

# 확인: 해당 프로세스가 죽었는지
lsof -iTCP:8000 -sTCP:LISTEN || echo "8000 released"
```

### 3.3 기존 모델 처리 — 두 가지 경로

#### A. 먼저 제거하고 다운로드 (디스크 작을 때)

디스크 여유가 30GB 미만이면 기존 삭제 후 받는다.

```bash
mv ~/llm-system/models/qwen32b ~/llm-system/models/qwen32b.TRASH
# 실제 삭제는 새 모델 정상 로드 후 §3.6 에서
```

#### B. 백업 유지하며 병렬 다운로드 (권장)

디스크 여유 60GB+ 면 이 경로가 안전. 새 모델 문제 시 즉시 롤백 가능.

```bash
mv ~/llm-system/models/qwen32b ~/llm-system/models/qwen32b.base-coder.bak
```

### 3.4 새 Instruct 모델 다운로드

```bash
source ~/llm-system/env/qwen-env/bin/activate

python3 - <<'PY'
from huggingface_hub import snapshot_download

snapshot_download(
    repo_id="mlx-community/Qwen2.5-Coder-32B-Instruct-4bit",
    local_dir="/Users/ohama/llm-system/models/qwen32b",
    local_dir_use_symlinks=False,
)
print("done")
PY
```

네트워크에 따라 30~60분 (17~20GB 다운로드). 진행 중 끊기면 같은 명령 재실행 — `snapshot_download` 는 부분 다운로드 resume.

### 3.5 다운로드 검증

Instruct 여부를 §1 체크리스트로 재확인 — 반드시 통과해야 한다.

```bash
echo "=== Instruct 지표 ==="
ls ~/llm-system/models/qwen32b/special_tokens_map.json \
   ~/llm-system/models/qwen32b/added_tokens.json
# 둘 다 경로 출력돼야 함 (No such file 이면 또 Base 받음)

echo ""
echo "=== chat_template in tokenizer_config ==="
python3 -c "
import json
c = json.load(open('/Users/ohama/llm-system/models/qwen32b/tokenizer_config.json'))
print('chat_template present:', 'chat_template' in c)
print('length:', len(c.get('chat_template', '')))
"
# 'present: True' 와 length 2000+ 확인

echo ""
echo "=== im_start/im_end 토큰 학습 여부 ==="
grep -o "<|im_start|>\|<|im_end|>" \
    ~/llm-system/models/qwen32b/added_tokens.json 2>/dev/null | sort -u
# <|im_end|> <|im_start|> 둘 다 나와야 함
```

3개 블록 전부 기대 출력이 나오면 Instruct 모델. 하나라도 실패하면 **다른 레포를 받은 것** — §2 표로 돌아가 재선택.

### 3.6 서비스 재시작

```bash
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen32b.plist

# 모델 로딩 대기 (4-bit 32B 는 ~50-80초)
tail -f ~/llm-system/services/logs/32b.log
# "Starting httpd at 127.0.0.1 on port 8000" 뜨면 Ctrl+C 빠져나와라
```

### 3.7 Chat smoke test

```bash
curl -s -X POST http://127.0.0.1:8000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "/Users/ohama/llm-system/models/qwen32b",
    "messages": [
      {"role": "system", "content": "You are a terse assistant. Respond with exactly one word."},
      {"role": "user", "content": "Say OK"}
    ],
    "max_tokens": 20,
    "temperature": 0.0
  }' | python3 -c "
import sys, json
r = json.load(sys.stdin)
print('finish_reason:', r['choices'][0]['finish_reason'])
print('content:', repr(r['choices'][0]['message']['content']))
"
```

**성공 기대 출력:**

```
finish_reason: stop
content: 'OK'
```

다음이 나오면 여전히 실패 — §3.5 를 다시 확인하거나 다른 Instruct 변형(`-8bit`, `-MLX`)으로 재시도.

- `finish_reason: length` (max_tokens 소진 — base 모델 continuation)
- `content` 에 `<|fim_*|>` 토큰 등장
- `content` 에 system prompt echo 또는 "User:" 가상 대화

### 3.8 blueCode end-to-end 확인

```bash
cd ~/projs/blueCode

# 32B 경로 (default intent routing 에서 General, Implementation 이 32B 로 감)
dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj -- \
    --model 32b \
    "List the files in the src directory"
```

기대 출력:

```
> listing directory... [ok, XXXms]
> final answer... [ok, XXms]

The files in the src directory are: BlueCode.Cli, BlueCode.Core

[INF] Session ok: 2 steps, model=Qwen32B, log=/Users/ohama/.bluecode/session_<ts>.jsonl
```

통과하면 §3.9 로. 만약 여기서 또 `InvalidJsonOutput` 나오면 **모델은 Instruct 인데 Qwen 이 Qwen stream 에서 JSON 출력을 불안정하게 내는 경우** — STATE.md 에 기록된 temperature 조정 (현재 Qwen32B=0.2) 을 0.0 으로 낮춰 시도해볼 수 있다 (`src/BlueCode.Core/Router.fs:67`).

### 3.9 기존 Base 모델 정리

새 Instruct 가 `--model 32b` 로 확실히 작동한 **뒤에** 기존 백업 제거:

```bash
# A 경로였다면 (즉시 삭제했다면 이미 없음)
rm -rf ~/llm-system/models/qwen32b.TRASH

# B 경로였다면 (백업 유지)
rm -rf ~/llm-system/models/qwen32b.base-coder.bak
# 17GB 디스크 회수
```

---

## 4. Router.modelToName 영향

blueCode 의 `src/BlueCode.Core/Router.fs:59` 는 현재 `Qwen32B` 에 대해
`"/Users/ohama/llm-system/models/qwen32b"` 를 반환 (commit `5ab5a95`).

경로 기반이라 **디렉토리 내용(모델 파일)이 바뀌어도 code 수정 불필요**. 서비스 재시작 후 blueCode 는 자동으로 새 Instruct 모델에 POST 한다.

단, v1.1 에서 OBS-03 동적 모델 id 쿼리를 구현하면 (`getModelIdAsync` 추가 예정), 서버가 리포트하는 id (`Qwen/Qwen2.5-Coder-32B-Instruct` 등) 에 맞춰 자동 조정된다. 그때까지는 경로 방식으로 충분.

---

## 5. 대안 — 32B 없이 72B 만 쓰기

교체를 미루고 싶다면 72B 로만 운영 가능. 이미 확인된 경로.

### 5.1 일시적 — 호출마다 `--model 72b`

```bash
dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj -- --model 72b "<prompt>"
```

단점: 매번 명시해야 함. 72B 는 응답이 항상 느림(~15-30s 첫 토큰).

### 5.2 영구적 — alias 설정

```bash
# ~/.zshrc 에 추가
alias bluecode="dotnet run --project ~/projs/blueCode/src/BlueCode.Cli/BlueCode.Cli.fsproj -- --model 72b"
# 이후: bluecode "<prompt>"
```

### 5.3 코드 레벨 — Router 에서 32B 제거 (비권장)

`src/BlueCode.Core/Router.fs:32-38` 의 `intentToModel` 을 전부 `Qwen72B` 반환으로 바꾸면 forced model 없이도 항상 72B 로 라우팅. 단, Phase 1 SC-2 ("classifyIntent \"fix the null check\" → Intent.Debug → Qwen72B") 테스트는 통과하지만 SC-3 (일부 intent → Qwen32B) 테스트는 깨진다. 32B 재도입 시 원복 부담 있음.

---

## 6. v1.1 로드맵 연계

Phase 5 완료 시점에 다음 3개가 `.planning/STATE.md` Pending Todos 로 등록됐다. 이 작업이 끝나면 (1) 을 close.

1. **이 가이드의 작업**: 32B Instruct 재다운
2. OBS-03 동적 모델 id — `QwenHttpClient.getMaxModelLenAsync` 옆에 `getModelIdAsync` 추가, `Router.modelToName` 이 bootstrap 시 한 번 조회한 값을 참조
3. 32B cold-start probe 를 bootstrap 에서 분리 — `--model 72b` 모드에서도 32B `/v1/models` 타임아웃 WARN 이 출력되는 문제 해결

---

## 7. 트러블슈팅

### 다운로드가 멈춘다

`snapshot_download` 는 resume 지원. 같은 명령 재실행. HF 가 rate limit 걸면 10~15분 후 재시도.

### 서비스가 `KeepAlive=true` 때문에 재기동 루프

로그 `tail -f ~/llm-system/services/logs/32b.err` 으로 원인 먼저 확인. 자주 보는 원인:
- 새 모델 파일 권한 문제 — `chmod -R u+r ~/llm-system/models/qwen32b`
- `config.json` 또는 `model.safetensors.index.json` 결함 — 재다운로드 필요
- `mlx-lm` 과 모델 포맷 호환성 — `pip install -U mlx-lm` 시도

### smoke test 에서 `finish_reason: length` 여전

Instruct 모델을 받았는데도 나오면 두 가지:
1. `max_tokens: 20` 이 너무 작음 — `max_tokens: 100` 으로 재시도
2. System prompt 가 비어있거나 엉뚱 — curl 페이로드의 system message 를 다시 확인

### `curl http://localhost:8000/v1/models` 가 timeout

서비스는 살아있으나 모델 로딩이 아직 진행 중. `tail -f ~/llm-system/services/logs/32b.err` 에서 "Starting httpd" 메시지를 기다린다. 4-bit 32B 는 ~50-80초.

---

## 8. 관련 문서

- `documentations/local-llm-services.md` — launchd 서비스 운영 가이드 (항상 떠있게 하기)
- `.planning/PROJECT.md` — "Mac 로컬 Qwen 32B/72B" 전제
- `.planning/phases/05-cli-polish/05-04-SUMMARY.md` — 이 문제가 발견된 UAT 과정
- `src/BlueCode.Core/Router.fs:57-60` — `modelToName` (v1 하드코딩, v1.1 에서 동적화)

---

## 부록 A: 다른 Instruct 변형으로 갈 때 체크리스트

표의 어떤 variant 를 선택하든 동일하게 작동해야 한다. §3 절차 중 §3.4 의 `repo_id` 만 교체.

```bash
# 예: 8-bit 변형 (RAM 64GB+ 권장)
python3 -c "
from huggingface_hub import snapshot_download
snapshot_download(
    repo_id='mlx-community/Qwen2.5-Coder-32B-Instruct-8bit',
    local_dir='/Users/ohama/llm-system/models/qwen32b',
    local_dir_use_symlinks=False,
)
"

# 예: 일반 Instruct (Coder 아님, 자연어 태스크 범용)
# repo_id='mlx-community/Qwen2.5-32B-Instruct-4bit-MLX'
```

받은 뒤 §3.5 의 3개 검증 블록을 전부 통과해야 한다.
