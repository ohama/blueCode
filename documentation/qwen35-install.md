# Qwen 3.5 35B + 122B 설치 및 운영 가이드 (8000 / 8001 candidate)

blueCode가 현재 Qwen 2.5 32B/72B 페어를 사용한다. 이 문서는 그 페어를 Qwen 3.5
35B-A3B / 122B-A10B (둘 다 MoE, 둘 다 MLX 4-bit) 페어로 **평가하기 위한** 설치
절차다. v2.0 Phase 17의 산출물이며, **교체를 확정하기 전에** 두 페어 모두 디스크에
공존하는 시점이 존재한다 (롤백 가능).

> **핵심 차이 한 줄**: Qwen 3.5는 dense가 아니라 MoE (Mixture-of-Experts).
> 35B-A3B = 35B total / 3B activated; 122B-A10B = 122B total / 10B activated.
> 즉 추론 속도는 dense 35B보다 훨씬 빠르고, 메모리는 4-bit 기준 ~20GB / ~70GB.

> **새로운 함정**: Qwen 3.5는 기본적으로 `<think>...</think>` 토큰을 응답 앞에
> 붙인다 (thinking mode). blueCode의 strict JSON 스키마 검증과 호환되지 않는다.
> §5 검증을 반드시 통과시켜야 한다.

**관련 문서**:
- `documentation/local-llm-services.md` — 현 32B/72B 페어 운영 (이 페어가 살아있는 동안 유효)
- `documentation/qwen32b-base-to-instruct.md` — v1 Base/Coder/Instruct 함정 (구조적으로 유사하나 원인은 다름)
- `.planning/phases/17-qwen-3-5-evaluation/17-RESEARCH.md` — 본 가이드의 단일 진실 소스

---

## 1. 사전 점검 (mlx_lm 버전, 디스크 여유, 기존 서비스 상태)

이 절을 건너뛰지 않는다. 셋 중 하나라도 실패하면 이후 단계가 의미 없다.

| 확인 항목 | 확인 명령 | 정상 조건 |
|-----------|-----------|-----------|
| mlx_lm 버전 ≥ 0.25.2 | `python3 -c "import mlx_lm; print(mlx_lm.__version__)"` (venv 활성화 후) | 출력이 `0.25.2` 이상 |
| 디스크 여유 ≥ 150 GB | `df -h ~/llm-system` | Available 열이 150G 이상 |
| 기존 서비스 가동 중 | `launchctl list \| grep ohama` | `qwen32b`, `qwen72b` 두 줄 이상 |

### 1.1 mlx_lm 버전 검증 (가장 먼저 실행)

```bash
source ~/llm-system/env/qwen-env/bin/activate
python3 -c "import mlx_lm; print(mlx_lm.__version__)"
```

출력이 `0.25.2` 미만이면 즉시 업그레이드한 후 재확인한다:

```bash
pip install --upgrade mlx-lm
python3 -c "import mlx_lm; print(mlx_lm.__version__)"
# 반드시 0.25.2 이상이어야 Qwen3.5 MoE 아키텍처를 로드할 수 있다.
```

> **왜 0.25.2인가**: Qwen3/Qwen3-MoE 아키텍처 지원이 mlx-lm 0.25.2에서 합류됐다
> (2025-04-28 PR 병합). Qwen3.5는 동일 아키텍처를 사용하므로 이 버전 이상이 필수다.

### 1.2 디스크 여유 확인

35B (~20 GB) + 122B (~70 GB) 다운로드에 기존 모델 (~55 GB) 공존 + 헤드룸을 더하면
최소 150 GB 여유가 필요하다.

```bash
df -h ~/llm-system
# 또는:
df -h ~
```

150 GB 미만이면 다운로드를 시작하지 않는다. 기존 32B/72B는 §2 정책에 따라 삭제하지 않는다.

### 1.3 기존 서비스 상태 확인

다운로드 도중 32B/72B는 계속 동작해야 한다.

```bash
launchctl list | grep ohama   # qwen32b, qwen72b 두 줄 보이면 OK
curl -fsS http://127.0.0.1:8000/v1/models > /dev/null && echo "8000 OK"
curl -fsS http://127.0.0.1:8001/v1/models > /dev/null && echo "8001 OK"
```

서비스가 내려가 있다면 `documentation/local-llm-services.md §7` 복구 플로우를 먼저 실행한다.

### 1.4 메모리 여유 권고

콜드 스타트 시 메모리 압박이 크다 (§7 메모리 예산 참조). 다운로드 중이라도 Chrome,
Xcode, VM 등 GB 단위 점유 프로세스를 종료하는 것을 권장한다.

---

## 2. 디렉토리 컨벤션

### 2.1 전체 구조

```
~/llm-system/
├── models/
│   ├── qwen32b/        # 기존 — 17-02 swap 확정 전까지 유지 (롤백 자산)
│   ├── qwen72b/        # 기존 — 동일
│   ├── qwen35b/        # 신규 — Qwen3.5-35B-A3B-4bit (~20.4 GB on disk)
│   └── qwen122b/       # 신규 — Qwen3.5-122B-A10B-4bit (~69.6 GB on disk)
├── env/
│   └── qwen-env/       # Python venv (§1.1에서 이미 활성화)
└── services/
    └── logs/
        ├── 32b.log     # 기존
        ├── 32b.err     # 기존
        ├── 72b.log     # 기존
        ├── 72b.err     # 기존
        ├── 35b.log     # 신규 (§4 plist에서 생성)
        ├── 35b.err     # 신규
        ├── 122b.log    # 신규
        └── 122b.err    # 신규
```

### 2.2 기존 모델 보존 정책

기존 32B/72B 모델 파일을 **삭제하지 않는다**. 17-03이 SWITCH 결정을 내리고 1주
이상 안정 운영된 후에야 정리 후보가 된다 (그조차도 본 Phase의 OOS).

롤백이 필요하면 §9의 롤백 절차로 32B/72B 서비스를 즉시 복구할 수 있다 — 모델
파일이 남아 있어야 이것이 가능하다.

---

## 3. 모델 다운로드

### 3.1 HuggingFace 레포 ID

| 역할 | HF 레포 ID | 디스크 크기 |
|------|-----------|------------|
| 포트 8000 후보 | `mlx-community/Qwen3.5-35B-A3B-4bit` | ~20.4 GB |
| 포트 8001 후보 | `mlx-community/Qwen3.5-122B-A10B-4bit` | ~69.6 GB |

> **주의**: `mlx-community/Qwen3.5-35B-A3B-Instruct-4bit` 라는 레포는 존재하지 않는다.
> Qwen 3.5는 Coder/Instruct 계열이 분리되지 않았다 — 모든 비-Base 변형이 instruction-tuned이다.
> `-Instruct` 접미사를 붙이지 않는다.

### 3.2 다운로드 실행

```bash
source ~/llm-system/env/qwen-env/bin/activate

python3 - <<'PY'
from huggingface_hub import snapshot_download

# 35B-A3B 4-bit MLX (~20 GB disk)
snapshot_download(
    repo_id="mlx-community/Qwen3.5-35B-A3B-4bit",
    local_dir="/Users/ohama/llm-system/models/qwen35b",
    local_dir_use_symlinks=False,
)

# 122B-A10B 4-bit MLX (~70 GB disk)
snapshot_download(
    repo_id="mlx-community/Qwen3.5-122B-A10B-4bit",
    local_dir="/Users/ohama/llm-system/models/qwen122b",
    local_dir_use_symlinks=False,
)
print("done")
PY
```

총 다운로드 ~90 GB. 네트워크 속도에 따라 30분~수 시간. 중단되어도 동일 명령으로
재시작하면 이어받기가 된다 (`snapshot_download`는 resume 지원).

### 3.3 다운로드 검증

```bash
du -sh ~/llm-system/models/qwen35b    # 약 20G
du -sh ~/llm-system/models/qwen122b   # 약 70G
ls ~/llm-system/models/qwen35b/config.json ~/llm-system/models/qwen122b/config.json
```

두 `config.json` 모두 존재해야 한다. 하나라도 없으면 다운로드가 불완전하게 끊긴 것이므로
동일 명령 재실행.

---

## 4. launchd plist (Path A: 서버 플래그로 thinking 끄기)

### 4.1 `~/Library/LaunchAgents/com.ohama.qwen35b.plist`

아래 XML을 그대로 복사해 저장한다. `--chat-template-args` + `{"enable_thinking": false}`
쌍이 핵심이다 — 이 두 줄이 없으면 thinking mode가 활성화된 채 서버가 뜬다.

> **플래그 이름 주의**: mlx_lm 0.31.x 기준 server CLI 플래그는 `--chat-template-args`이며
> JSON kwargs string을 받는다 (`--help` 예시: `'{"enable_thinking":false}'`).
> 이전 패치에서 `--chat-template-kwargs`로 작성됐다면 모두 `--chat-template-args`로 교체할 것.
> Path B (§6) 의 F# 패치는 HTTP 요청 body 필드 이름을 사용하는데 — mlx_lm 0.31.x 의 해당
> body 필드 이름은 `chat_template_args` 또는 `chat_template_kwargs` 중 하나로 추정되며,
> Path A 가용 시 검증 불필요. Path B 진입 시 mlx_lm.server source 의 request handler 를
> 참조하여 정확한 필드명을 확정한다.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.ohama.qwen35b</string>

    <key>ProgramArguments</key>
    <array>
        <string>/Users/ohama/llm-system/env/qwen-env/bin/python3</string>
        <string>-m</string>
        <string>mlx_lm.server</string>
        <string>--model</string>
        <string>/Users/ohama/llm-system/models/qwen35b</string>
        <string>--host</string>
        <string>127.0.0.1</string>
        <string>--port</string>
        <string>8000</string>
        <string>--chat-template-args</string>
        <string>{"enable_thinking": false}</string>
    </array>

    <key>RunAtLoad</key>
    <true/>

    <key>KeepAlive</key>
    <true/>

    <key>ThrottleInterval</key>
    <integer>30</integer>

    <key>StandardOutPath</key>
    <string>/Users/ohama/llm-system/services/logs/35b.log</string>

    <key>StandardErrorPath</key>
    <string>/Users/ohama/llm-system/services/logs/35b.err</string>

    <key>WorkingDirectory</key>
    <string>/Users/ohama/llm-system</string>

    <key>EnvironmentVariables</key>
    <dict>
        <key>PATH</key>
        <string>/Users/ohama/llm-system/env/qwen-env/bin:/usr/local/bin:/usr/bin:/bin</string>
    </dict>
</dict>
</plist>
```

### 4.2 `~/Library/LaunchAgents/com.ohama.qwen122b.plist`

35B plist를 복사한 뒤 아래 4줄만 바꾼다:

| 키 / 인자 | 35B 값 | 122B 값 |
|-----------|--------|---------|
| `Label` | `com.ohama.qwen35b` | `com.ohama.qwen122b` |
| `--model` 다음 `<string>` | `/Users/ohama/llm-system/models/qwen35b` | `/Users/ohama/llm-system/models/qwen122b` |
| `--port` 다음 `<string>` | `8000` | `8001` |
| `StandardOutPath` | `.../logs/35b.log` | `.../logs/122b.log` |
| `StandardErrorPath` | `.../logs/35b.err` | `.../logs/122b.err` |

완성된 122B plist는 다음과 같다:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.ohama.qwen122b</string>

    <key>ProgramArguments</key>
    <array>
        <string>/Users/ohama/llm-system/env/qwen-env/bin/python3</string>
        <string>-m</string>
        <string>mlx_lm.server</string>
        <string>--model</string>
        <string>/Users/ohama/llm-system/models/qwen122b</string>
        <string>--host</string>
        <string>127.0.0.1</string>
        <string>--port</string>
        <string>8001</string>
        <string>--chat-template-args</string>
        <string>{"enable_thinking": false}</string>
    </array>

    <key>RunAtLoad</key>
    <true/>

    <key>KeepAlive</key>
    <true/>

    <key>ThrottleInterval</key>
    <integer>30</integer>

    <key>StandardOutPath</key>
    <string>/Users/ohama/llm-system/services/logs/122b.log</string>

    <key>StandardErrorPath</key>
    <string>/Users/ohama/llm-system/services/logs/122b.err</string>

    <key>WorkingDirectory</key>
    <string>/Users/ohama/llm-system</string>

    <key>EnvironmentVariables</key>
    <dict>
        <key>PATH</key>
        <string>/Users/ohama/llm-system/env/qwen-env/bin:/usr/local/bin:/usr/bin:/bin</string>
    </dict>
</dict>
</plist>
```

### 4.3 plist 검증

XML 파싱 오류가 있으면 launchd가 조용히 무시한다. 로드 전에 반드시 검증:

```bash
plutil -lint ~/Library/LaunchAgents/com.ohama.qwen35b.plist
plutil -lint ~/Library/LaunchAgents/com.ohama.qwen122b.plist
# 둘 다 "OK" 출력이어야 한다
```

### 4.4 `--chat-template-args` 플래그 가용성 사전 검증 (Path A 선택의 결정적 단계)

launchd로 로드하기 **전에** 이 확인을 반드시 실행한다. 플래그가 없는 버전을 사용하면
plist를 수정해야 하는 번거로움이 생긴다.

```bash
source ~/llm-system/env/qwen-env/bin/activate
python3 -m mlx_lm.server --help 2>&1 | grep chat-template-args
```

| 출력 | 의미 | 다음 단계 |
|------|------|-----------|
| `--chat-template-args ...` 한 줄 출력 (예: `A JSON formatted string of arguments for the tokenizer's apply_chat_template, e.g. '{"enable_thinking":false}'`) | Path A 사용 가능 | §5로 진행 — plist 그대로 사용 |
| 출력 없음 | Path A 불가 — 이 버전의 mlx_lm.server가 플래그를 미지원 | §6 (Path B 코드 패치) 적용 후 plist에서 `--chat-template-args` 관련 `<string>` 2줄 제거 |

출력 없음이 나왔다면 `pip install --upgrade mlx-lm` 후 재확인을 시도한다 (§1.1에서 이미
업그레이드했다면 Path B 적용이 불가피).

> **2026-04-27 검증 완료** (mlx-lm 0.31.3): `--chat-template-args` 플래그 존재 확인 — Path A 사용 가능.

---

## 5. 서비스 로드 + 검증 프로토콜

### 5.1 로드 + 준비 대기

```bash
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen35b.plist
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen122b.plist
```

모델 메모리 로딩 때문에 서버가 뜨는 데 시간이 걸린다. 강제 인터럽트 금지 — 절반만
로드된 상태에서 `Ctrl+C`를 누르면 KeepAlive가 반복 재시작 루프에 빠진다.

```bash
# 35B는 ~30-60s, 122B는 ~120-240s
until curl -fsS http://127.0.0.1:8000/v1/models > /dev/null 2>&1; do sleep 3; done && echo "35B ready"
until curl -fsS http://127.0.0.1:8001/v1/models > /dev/null 2>&1; do sleep 3; done && echo "122B ready"
```

로딩 진행 상황을 로그로 확인하려면:

```bash
tail -f /Users/ohama/llm-system/services/logs/35b.log   # "Uvicorn running" 대기
tail -f /Users/ohama/llm-system/services/logs/122b.log
```

### 5.2 Instruct tokenizer 검증 (Base 모델이 잘못 다운된 케이스 차단)

Qwen 3.5는 Qwen 2.5와 달리 별도 Coder 계열이 없어 "Base Coder 실수 다운로드" 함정은
동일한 방식으로는 발생하지 않는다. 그러나 `mlx-community/Qwen3.5-35B-A3B-4bit`가
**Instruct 변형**임을 명시적으로 확인한다.

> **참고**: v1.x의 32B Base Coder 함정과 구조적으로 유사한 증상(verbose non-JSON 출력,
> `InvalidJsonOutput` 반복)이 Qwen 3.5에서도 나타날 수 있다. 그러나 v1.x 함정은
> Base 모델이 원인이었고, Qwen 3.5 함정은 thinking mode가 원인이다. 원인이 다르다.
> v1.x 함정의 전체 진단 절차는 `documentation/qwen32b-base-to-instruct.md`를 참고하라.
> Qwen 3.5에서 동일 증상이 나오면 먼저 §5.3 thinking-mode 검증을 실행한다.

```bash
# Instruct 지표 파일 4개 모두 존재해야 한다
ls /Users/ohama/llm-system/models/qwen35b/special_tokens_map.json \
   /Users/ohama/llm-system/models/qwen35b/added_tokens.json
ls /Users/ohama/llm-system/models/qwen122b/special_tokens_map.json \
   /Users/ohama/llm-system/models/qwen122b/added_tokens.json
```

파일이 없으면 Base 모델이 다운된 것 — §3 다운로드 명령을 재확인한다.

```bash
python3 -c "
import json
for p in ['/Users/ohama/llm-system/models/qwen35b/tokenizer_config.json',
         '/Users/ohama/llm-system/models/qwen122b/tokenizer_config.json']:
    c = json.load(open(p))
    print(p, 'chat_template len:', len(c.get('chat_template','')), 'has <think>:', '<think>' in c.get('chat_template',''))
# 두 모델 모두: chat_template len > 2000, has <think>: True (정상 — Instruct + thinking 토큰 정의됨)
"
```

`<think>`가 `chat_template`에 등장하는 것은 **정상**이다 — Qwen 3.5 Instruct는 thinking
토큰을 정의한다. 우리는 §5.3 단계에서 *런타임*에 thinking을 끈다.

| 출력 | 진단 |
|------|------|
| `chat_template len > 2000`, `has <think>: True` | Instruct 확인 — 계속 진행 |
| `chat_template len: 0` 또는 `has <think>: False` | Base 모델 또는 잘못된 다운로드 — §3 재다운로드 |

### 5.3 Thinking-mode 무력화 검증 (Qwen 3.5 핵심 검증)

이 단계가 실패하면 blueCode는 첫 호출부터 `InvalidJsonOutput`을 반복한다. §4의 plist에
`--chat-template-args` 플래그가 정상적으로 반영됐는지 확인하는 유일한 경험적 방법이다.

**35B (포트 8000) 검증:**

```bash
curl -s -X POST http://127.0.0.1:8000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "/Users/ohama/llm-system/models/qwen35b",
    "messages": [
      {"role": "system", "content": "You are a terse assistant. Respond with exactly one word."},
      {"role": "user", "content": "Say OK"}
    ],
    "max_tokens": 100,
    "temperature": 0.0
  }' | python3 -c "
import sys, json
r = json.load(sys.stdin)
content = r['choices'][0]['message']['content']
print('content:', repr(content))
print('PASS' if content.strip() == 'OK' and '<think>' not in content else 'FAIL')
"
```

**122B (포트 8001) 검증 — 동일 curl을 port와 model 경로만 바꿔 반복:**

```bash
curl -s -X POST http://127.0.0.1:8001/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "/Users/ohama/llm-system/models/qwen122b",
    "messages": [
      {"role": "system", "content": "You are a terse assistant. Respond with exactly one word."},
      {"role": "user", "content": "Say OK"}
    ],
    "max_tokens": 100,
    "temperature": 0.0
  }' | python3 -c "
import sys, json
r = json.load(sys.stdin)
content = r['choices'][0]['message']['content']
print('content:', repr(content))
print('PASS' if content.strip() == 'OK' and '<think>' not in content else 'FAIL')
"
```

**응답 분류 표:**

| `content` 값 | 진단 | 조치 |
|--------------|------|------|
| `'OK'` (대소문자/공백 무시), PASS 출력 | Path A 동작 — thinking 비활성 | §5.4로 진행 |
| `'<think>...</think>OK'` 또는 `'<think>...'` 형태 | thinking 활성 — server flag 미반영 | §6 (Path B fallback) |
| `<think>...` 만 (비완료, `max_tokens`에 잘림) | thinking이 max_tokens 소진 | §6 |
| 시스템 프롬프트 echo, FIM 토큰 (`<\|fim_prefix\|>` 등) | Base 모델 — §3 다운로드 자체가 잘못됨 | §3 재다운로드 |
| 빈 문자열 + `reasoning_content` 필드에 응답 | omlx-style 분리 응답 | §6 + extractContent 패치 필요 |

### 5.4 JSON 스키마 출력 검증 (blueCode가 사용할 실제 응답 형태)

§5.3이 PASS였다면 이 단계도 통과할 가능성이 높다. 하지만 blueCode가 실제 사용하는
JSON 스키마 형태로 smoke test를 한 번 더 한다.

```bash
curl -s -X POST http://127.0.0.1:8000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "/Users/ohama/llm-system/models/qwen35b",
    "messages": [
      {"role": "system", "content": "Respond ONLY with valid JSON: {\"thought\": string, \"action\": \"final\", \"input\": {\"answer\": string}}"},
      {"role": "user", "content": "What is 2+2?"}
    ],
    "max_tokens": 200,
    "temperature": 0.0
  }' | python3 -c "
import sys, json
r = json.load(sys.stdin)
content = r['choices'][0]['message']['content']
try:
    obj = json.loads(content)
    print('JSON parse: OK; action=', obj.get('action'))
except Exception as e:
    print('JSON parse FAILED:', e); print('raw:', content[:300])
"
```

`JSON parse: OK; action= final` 이면 통과. 122B (포트 8001, model `qwen122b`)도 동일하게 반복한다.

---

## 6. Path B fallback: F# QwenHttpClient.fs 패치 (Path A 불가 시에만)

> **중요**: §6은 17-02에서 Path A 실패가 *경험적으로 확인된 후에만* 실행한다.
> 17-01은 이 절차를 문서화만 하고 코드를 건드리지 않는다.

**명시적 트리거**: §4.4에서 `--chat-template-args` 미지원 확인, 또는 §5.3/§5.4 FAIL.

**패치 대상**: `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` 내 `buildRequestBody` 함수.

현재 코드 (확인됨, line ~65):

```fsharp
let req =
    {| model = modelId
       messages = msgArr
       temperature = modelToTemperature model
       max_tokens = 1024
       presence_penalty = 1.5
       stream = false |}
```

수정 후 (라인 1개 추가):

```fsharp
let req =
    {| model = modelId
       messages = msgArr
       temperature = modelToTemperature model
       max_tokens = 1024
       presence_penalty = 1.5
       stream = false
       chat_template_kwargs = {| enable_thinking = false |} |}
```

이 `chat_template_kwargs` 필드가 POST 요청 body에 포함되면 서버 플래그 없이도
thinking mode를 비활성화할 수 있다. Cli 어댑터 내부이므로 Core 순수성에 영향 없음.

**패치 후 검증**:

```bash
# 빌드 확인
dotnet build BlueCode.sln 2>&1 | tail -3

# 테스트 통과 확인
dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj --summary 2>&1 | grep "Passed:"

# §5.3 smoke test 재실행 — PASS 확인
```

**Path B 선택 시 plist 수정**: `--chat-template-args` 와 `{"enable_thinking": false}` 두 `<string>` 요소를 plist에서 제거하거나 그대로 둬도 무해하다 (서버가 플래그를 무시하고 요청 body를 따른다). 단 plist를 정리하려면 `plutil -lint` 재확인 필수.

> **주의 (Path B body 필드명)**: 위 코드의 `chat_template_kwargs` 필드명은 mlx_lm 0.31.x server 의 request handler 가 받는 정확한 필드명을 패치 적용 직전 source 또는 README 에서 재확인할 것 (`chat_template_args` 일 가능성도 있음 — CLI 플래그가 `--chat-template-args` 인 것과 일관성). Path A 가용 시 (2026-04-27 기준 mlx-lm 0.31.3 에서 가용 확인됨) 이 검증 불필요.

---

## 7. 통합 메모리 예산 (128 GB Mac)

### 7.1 페어 비교표

| 페어 | 모델 RAM | KV 캐시 (~8K ctx) | OS + 헤드룸 | 총 사용 | 여유도 |
|------|----------|-------------------|-------------|---------|--------|
| **현재: 32B + 72B** | 18.4 + 40.4 = **58.8 GB** | ~1 + ~2 = ~3 GB | ~15 GB | ~77 GB | 여유 있음 (~51 GB 잔여) |
| **후보: 35B + 122B (4-bit)** | 19.5 + 70 = **89.5 GB** | ~1 + ~3 = ~4 GB | ~15 GB | ~109 GB | **타이트** (~19 GB 잔여) |

후보 페어는 현재보다 **~30 GB 더 타이트**하다. 128 GB 통합 메모리에서 실행 가능하나 안전 마진이 크게 줄어든다.

### 7.2 운영 권고

- **콜드 스타트 전** Chrome, Xcode, VM, Parallels 등 GB 단위 프로세스 종료 필수
- macOS 메모리 압축(Compressed Memory)이 활성화되어 있어야 한다 (기본값 — 비활성화하지 않았다면 OK)
- 122B 콜드 스타트 중 Activity Monitor에서 Memory Pressure 게이지가 빨간색으로 치솟을 수 있다 — 정상 과도기이지만 다른 앱 사용을 자제한다

### 7.3 OOM 발생 시 옵션

OOM이 관찰되면 (`122b.err`에 `exit status 137` 또는 `[METAL] Insufficient Memory`):

1. **122B만 로드 (35B 언로드)** — 단일 모델 워크플로우; `--model 72b` 인자를 `--model 35b`로 blueCode를 실행
2. **35B 4-bit + 72B 유지** — 혼합 페어 (32B→35B 교체만, 72B 유지)
3. **122B 3-bit 변종** (~60 GB) — 커뮤니티 양자화; 본 Phase OOS, v2.1 후보
4. **35B 8-bit** (~40 GB) + **72B 유지** — 정확도 향상이 목적이지 않으면 과도한 비용

---

## 8. 콜드 스타트 + blueCode 180s timeout 회피

### 8.1 왜 문제인가

`QwenHttpClient.fs`의 `httpClient.Timeout = TimeSpan.FromSeconds(180.0)`는 72B
최악의 경우 (~60s) + 여유 마진으로 설정됐다. 122B는 콜드 스타트 시 180–240초가
소요될 수 있으므로, blueCode를 먼저 실행하면 `probeModelInfoAsync`가 타임아웃되어
`ModelId = ""`를 반환하고, 이후 POST에서 HTTP 400/422 → `LlmUnreachable`이 된다.

이것은 코드 버그가 아니라 운영 절차 문제다. 122B가 준비된 것을 확인한 뒤
blueCode를 실행하는 것이 가장 간단한 해결책이다 (코드 변경 없음).

### 8.2 수동 대기 절차

blueCode 실행 전에 다음을 먼저 실행한다:

```bash
# 122B 준비 확인 (blueCode 실행 전 필수)
until curl -fsS http://127.0.0.1:8001/v1/models > /dev/null 2>&1; do
  echo "waiting for 122B..."; sleep 5
done && echo "ready"

# 그 다음에야:
cd ~/projs/blueCode
dotnet run --project src/BlueCode.Cli -- --model 72b "smoke test"
```

`launchctl kickstart -k`로 강제 재기동 후에도 동일 절차 적용.

### 8.3 로그 모니터링으로 확인

`until curl` 대신 로그를 직접 볼 수도 있다:

```bash
tail -f /Users/ohama/llm-system/services/logs/122b.log
# "Uvicorn running on http://127.0.0.1:8001" 이 나오면 준비 완료
```

> **참고**: `Timeout = 300s`로 늘리는 코드 변경도 가능하나 본 Phase OOS.
> 이 결정은 Phase 17-03 bench 결과를 보고 판단한다.

---

## 9. 서비스 swap 절차 (17-02에서 사용)

> **이 절은 17-02 checkpoint의 "사전에 읽고 따라할 절차"다.**
> 본 Phase에서는 **실행하지 않는다** — 17-02가 user 동의를 받아 실행한다.

### 9.1 swap 실행

```bash
# 1) 기존 서비스 unload (KILL 금지 — KeepAlive가 재기동시킴; unload 사용)
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen32b.plist
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen72b.plist

# 2) 포트 해제 확인
lsof -iTCP:8000 -sTCP:LISTEN || echo "8000 released"
lsof -iTCP:8001 -sTCP:LISTEN || echo "8001 released"

# 3) 신규 서비스 load
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen35b.plist
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen122b.plist

# 4) Ready 대기 (§8.2 절차 적용)
until curl -fsS http://127.0.0.1:8000/v1/models > /dev/null 2>&1; do sleep 3; done && echo "35B ready"
until curl -fsS http://127.0.0.1:8001/v1/models > /dev/null 2>&1; do sleep 3; done && echo "122B ready"

# 5) §5.2–§5.4 검증 전체 재실행
```

### 9.2 롤백 (35B/122B에 문제가 있을 때)

35B/122B 서비스를 내리고 32B/72B를 다시 올린다. 모델 파일이 §2 정책에 따라 보존돼
있으므로 롤백은 즉시 가능하다.

```bash
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen35b.plist
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen122b.plist

launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen32b.plist
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen72b.plist

# 기동 대기
until curl -fsS http://127.0.0.1:8000/v1/models > /dev/null 2>&1; do sleep 3; done && echo "32B ready"
until curl -fsS http://127.0.0.1:8001/v1/models > /dev/null 2>&1; do sleep 3; done && echo "72B ready"
```

### 9.3 blueCode 연동 체크리스트 (swap 후)

```bash
# 1. 두 서비스 확인
launchctl list | grep -c ohama   # → 2 이상

# 2. 포트 LISTEN 확인
lsof -iTCP:8000 -sTCP:LISTEN > /dev/null && echo "8000 ok"
lsof -iTCP:8001 -sTCP:LISTEN > /dev/null && echo "8001 ok"

# 3. blueCode smoke
cd ~/projs/blueCode
dotnet run --project src/BlueCode.Cli -- --model 32b "List the files in the src directory"
# LlmUnreachable 아니라 tool steps + final answer가 돌아오면 완성
```

---

## 부록 A: Gotcha 빠른 참조

| Gotcha | 증상 | 조치 |
|--------|------|------|
| Thinking mode 기본 활성 | `<think>` 토큰이 응답 앞에 붙음; `InvalidJsonOutput` 반복 | §4.4 확인 → §5.3 검증 → 실패 시 §6 |
| mlx_lm 버전 미달 | 모델 로드 실패, 아키텍처 미지원 오류 | §1.1 업그레이드 |
| 122B 콜드 스타트 타임아웃 | `LlmUnreachable: request timed out after 180s` | §8.2 수동 대기 후 재실행 |
| 4-bit 멀티턴 JSON 열화 | ~5 tool call 이후 JSON이 plain-text 근사치로 변질 | Phase 17-03 bench에서 관찰; 8-bit 변형 고려 |
| mlx-vlm 변환 이슈 | mlx_lm.server 로드 실패 (35B는 mlx-vlm 0.3.12로 변환됨) | `mlx_vlm.server` 시도 (동일 인터페이스) |
| `content` 빈 문자열 | `reasoning_content`에 응답이 있고 `content`가 비어있음 | §6 + `extractContent` 패치 필요 |
| OOM on 122B cold start | `exit status 137` 또는 `[METAL] Insufficient Memory` | §7.3 옵션 참조 |

---

## 부록 B: 관련 문서 링크

- `documentation/local-llm-services.md` — 32B/72B 서비스 운영 전체 가이드 (swap 전까지 유효)
- `documentation/qwen32b-base-to-instruct.md` — v1.x Base/Coder/Instruct 함정 (이 문서의 §5.2 크로스레퍼런스)
- `.planning/phases/17-qwen-3-5-evaluation/17-RESEARCH.md` — 본 가이드의 수치 및 명령 소스
- `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` — Path B 패치 대상 (`buildRequestBody` 함수)
