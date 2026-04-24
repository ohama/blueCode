# 로컬 Qwen 서버 상시 운영 가이드 (8000 / 8001)

blueCode는 `localhost:8000` (Qwen 32B), `localhost:8001` (Qwen 72B)에 직접 붙는다.
둘 다 launchd 서비스로 상시 떠 있어야 하고, 장애 시 자동으로 되살아나야 한다.

이 문서는 "명령 한 줄"이 아니라 **설치되지 않은 상태 → 부팅 시 자동 기동 → 장애 복구**까지 전 구간의 운영 절차를 담는다.

---

## 1. 현재 상태 점검 (가장 먼저)

blueCode 실행 시 `Connection refused (127.0.0.1:8000)` 이 나오면 아래 세 가지 중 하나다.

| 원인 | 확인 명령 | 정상 출력 |
|------|-----------|-----------|
| launchd 서비스 미로드 | `launchctl list \| grep qwen` | `qwen32b`, `qwen72b` 두 줄 |
| 서비스는 로드됐지만 프로세스 없음 | `lsof -iTCP:8000 -sTCP:LISTEN` | `Python ... LISTEN` 한 줄 |
| 모델 로딩 중 (정상) | `tail -f ~/llm-system/services/logs/32b.log` | `Uvicorn running on http://0.0.0.0:8000` |

세 명령을 위에서부터 실행해서 어디서 막히는지 먼저 특정한다.

---

## 2. 디렉토리 & 환경 (0에서 시작할 때만)

이미 설치되어 있다면 §3으로 건너뛴다.

### 2.1 디렉토리 구조

```
~/llm-system/
├── models/
│   ├── qwen32b/        # mlx-community/qwen2.5-32b-mlx
│   └── qwen72b/        # mlx-community/Qwen2.5-72B-Instruct-4bit-AWQ
├── env/
│   └── qwen-env/       # Python venv
├── services/
│   └── logs/
│       ├── 32b.log
│       ├── 32b.err
│       ├── 72b.log
│       └── 72b.err
└── launchd/            # plist 원본 보관 (선택)
    ├── qwen32b.plist
    └── qwen72b.plist
```

### 2.2 Python 환경

```bash
mkdir -p ~/llm-system/env ~/llm-system/services/logs ~/llm-system/models
python3 -m venv ~/llm-system/env/qwen-env
source ~/llm-system/env/qwen-env/bin/activate

# mlx-lm 하나로 충분 (OpenAI-compat 서버 포함)
pip install --upgrade pip
pip install mlx-lm huggingface_hub
```

`mlx_lm.server` 가 설치 확인:

```bash
python3 -c "import mlx_lm.server; print('ok')"
```

### 2.3 모델 다운로드 (한 번만 — 이미 받았으면 스킵)

먼저 이미 있는지 확인:

```bash
ls ~/llm-system/models/qwen32b/config.json ~/llm-system/models/qwen72b/config.json
# 두 경로 다 존재하면 → 이 절 전체 스킵, §3으로 진행
```

두 모델 합계 약 40~60GB, 디스크 70GB 이상 여유가 있어야 한다.

```bash
source ~/llm-system/env/qwen-env/bin/activate

python3 - <<'PY'
from huggingface_hub import snapshot_download

# 32B (대략 18GB)
snapshot_download(
    repo_id="mlx-community/qwen2.5-32b-mlx",
    local_dir="/Users/ohama/llm-system/models/qwen32b",
    local_dir_use_symlinks=False,
)

# 72B 4-bit AWQ (대략 40GB)
snapshot_download(
    repo_id="mlx-community/Qwen2.5-72B-Instruct-4bit-AWQ",
    local_dir="/Users/ohama/llm-system/models/qwen72b",
    local_dir_use_symlinks=False,
)
PY
```

---

## 3. launchd plist — "항상 떠 있게" 만드는 핵심

`RunAtLoad=true` (부팅 시 기동) + `KeepAlive=true` (죽으면 자동 재기동) 이 두 키가 "상시 구동"의 전부다.

### 3.1 32B — `~/Library/LaunchAgents/com.ohama.qwen32b.plist`

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.ohama.qwen32b</string>

    <key>ProgramArguments</key>
    <array>
        <string>/Users/ohama/llm-system/env/qwen-env/bin/python3</string>
        <string>-m</string>
        <string>mlx_lm.server</string>
        <string>--model</string>
        <string>/Users/ohama/llm-system/models/qwen32b</string>
        <string>--host</string>
        <string>127.0.0.1</string>
        <string>--port</string>
        <string>8000</string>
    </array>

    <key>RunAtLoad</key>
    <true/>

    <key>KeepAlive</key>
    <true/>

    <key>ThrottleInterval</key>
    <integer>30</integer>

    <key>StandardOutPath</key>
    <string>/Users/ohama/llm-system/services/logs/32b.log</string>

    <key>StandardErrorPath</key>
    <string>/Users/ohama/llm-system/services/logs/32b.err</string>

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

### 3.2 72B — `~/Library/LaunchAgents/com.ohama.qwen72b.plist`

32B plist를 그대로 복사하고 아래만 바꾼다.

- `Label` → `com.ohama.qwen72b`
- `--model` 경로 → `/Users/ohama/llm-system/models/qwen72b`
- `--port` → `8001`
- 로그 경로 → `72b.log`, `72b.err`

### 3.3 plist 검증

XML 파싱 오류가 있으면 launchd가 조용히 무시한다. 로드 전에 반드시 검증:

```bash
plutil -lint ~/Library/LaunchAgents/com.ohama.qwen32b.plist
plutil -lint ~/Library/LaunchAgents/com.ohama.qwen72b.plist
# 둘 다 "OK" 떠야 한다
```

---

## 4. 서비스 로드 & 기동

### 4.1 최초 로드

```bash
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen32b.plist
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen72b.plist
```

`-w` 플래그는 "Disabled 키 무시하고 활성화"를 뜻한다. 최초 로드에는 항상 `-w`를 붙인다.

### 4.2 즉시 실행 (launchd가 아직 시작을 보류 중일 때)

```bash
launchctl kickstart -k gui/$(id -u)/com.ohama.qwen32b
launchctl kickstart -k gui/$(id -u)/com.ohama.qwen72b
```

`-k` 는 "이미 실행 중이면 죽이고 새로 시작". 설정 변경 후 재기동에 유용하다.

### 4.3 기동 확인 (30~120초 소요)

모델 메모리 로딩 때문에 서버가 뜨는 데 시간이 걸린다. 로그를 봐야 정확하다:

```bash
tail -f ~/llm-system/services/logs/32b.log
# 기대 출력: "Uvicorn running on http://127.0.0.1:8000"

tail -f ~/llm-system/services/logs/72b.log
# 기대 출력: "Uvicorn running on http://127.0.0.1:8001"
```

헬스체크:

```bash
curl -s http://127.0.0.1:8000/v1/models | python3 -m json.tool
curl -s http://127.0.0.1:8001/v1/models | python3 -m json.tool
```

둘 다 `{"data":[...]}` JSON이 나오면 blueCode가 연결 가능하다.

---

## 5. 일상 운영

### 5.1 상태 조회

```bash
# launchd에 등록돼 있나
launchctl list | grep -i qwen
# 기대: 2줄, 각각 PID가 0이 아닌 숫자

# 포트가 실제로 LISTEN 중인가
lsof -iTCP:8000 -sTCP:LISTEN
lsof -iTCP:8001 -sTCP:LISTEN
# 기대: Python 프로세스 한 줄씩

# HTTP 응답
curl -fsS http://127.0.0.1:8000/v1/models > /dev/null && echo "8000 OK" || echo "8000 DOWN"
curl -fsS http://127.0.0.1:8001/v1/models > /dev/null && echo "8001 OK" || echo "8001 DOWN"
```

### 5.2 재시작 (plist 수정 후 / 모델 교체 후)

```bash
launchctl kickstart -k gui/$(id -u)/com.ohama.qwen32b
launchctl kickstart -k gui/$(id -u)/com.ohama.qwen72b
```

### 5.3 일시 중지

```bash
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen32b.plist
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen72b.plist
# 다시 띄우려면 §4.1 재실행
```

### 5.4 영구 제거 (드물게)

```bash
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen32b.plist
rm ~/Library/LaunchAgents/com.ohama.qwen32b.plist
# 72B도 동일
```

---

## 6. 로그

| 파일 | 내용 |
|------|------|
| `~/llm-system/services/logs/32b.log` | 32B Uvicorn stdout (요청/응답 요약) |
| `~/llm-system/services/logs/32b.err` | 32B stderr (mlx 로딩 로그, 에러 스택) |
| `~/llm-system/services/logs/72b.log` | 72B Uvicorn stdout |
| `~/llm-system/services/logs/72b.err` | 72B stderr |

실시간 모니터링:

```bash
tail -f ~/llm-system/services/logs/32b.log ~/llm-system/services/logs/72b.log
```

로그 용량 급증 시 수동 로테이션 (launchd가 기본 rotate를 안 해준다):

```bash
mv ~/llm-system/services/logs/32b.log ~/llm-system/services/logs/32b.log.$(date +%Y%m%d)
launchctl kickstart -k gui/$(id -u)/com.ohama.qwen32b
# 새 32b.log 파일이 자동 생성됨
```

---

## 7. `Connection refused` 복구 플로우

blueCode 실행 중 아래 에러가 나왔을 때:

```
System.Net.Http.HttpRequestException: Connection refused (127.0.0.1:8000)
```

**한 번에 해결하는 스크립트**:

```bash
# 1) 서비스가 로드돼 있는지
launchctl list | grep -i qwen || {
    echo "LOAD 안 됨 — 로드한다"
    launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen32b.plist
    launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen72b.plist
}

# 2) 프로세스가 살아 있는지 / 포트 바인딩됐는지
lsof -iTCP:8000 -sTCP:LISTEN || launchctl kickstart -k gui/$(id -u)/com.ohama.qwen32b
lsof -iTCP:8001 -sTCP:LISTEN || launchctl kickstart -k gui/$(id -u)/com.ohama.qwen72b

# 3) 모델 로딩 대기 (최대 180초 — 32B는 50초, 72B는 2분 이상 걸릴 수 있음)
for port in 8000 8001; do
    echo "waiting for $port ..."
    until curl -fsS http://127.0.0.1:$port/v1/models > /dev/null 2>&1; do
        sleep 3
    done
    echo "$port OK"
done

# 4) blueCode 재실행
cd ~/projs/blueCode
dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj
```

### 자주 걸리는 하위 원인

| 증상 | 로그 패턴 | 해법 |
|------|-----------|------|
| 프로세스가 몇 초 주기로 재시작 | `32b.err`에 `ModuleNotFoundError: No module named 'mlx_lm'` | venv 경로 오타 or mlx-lm 미설치. `§2.2` 재실행 |
| 포트 이미 사용 중 | `32b.err`에 `OSError: [Errno 48] Address already in use` | `lsof -iTCP:8000` 로 점유 프로세스 kill 후 `kickstart` |
| 모델이 메모리 한계 초과 | `killed`, exit status 137 | 72B는 64GB RAM 이상 필요. `Activity Monitor`로 여유 확인 |
| `KeepAlive=true`로 재기동 루프 | `32b.err` 마지막 줄에 계속 같은 에러 | `ThrottleInterval=30` 덕에 30초 텀으로 재시도 중. 에러 원인 해결이 먼저 |
| HF 모델 경로 오류 | `32b.err`에 `FileNotFoundError: ... qwen32b` | `~/llm-system/models/qwen32b/` 안에 `config.json`, `tokenizer.json` 등이 있는지 확인 |

---

## 8. 부팅 시 자동 기동 확인

`RunAtLoad=true` + `launchctl load -w`가 함께 들어가면 Mac 로그인 시 자동 시작된다. 한 번 검증:

```bash
# 재부팅 없이도 "Disabled" 상태가 아닌지 확인
launchctl print gui/$(id -u)/com.ohama.qwen32b | grep -E "state|RunAtLoad|KeepAlive"
# 기대:
#   state = running
#   RunAtLoad = 1
#   KeepAlive = 1
```

`state = not running` + `RunAtLoad = 0` 이면 `launchctl enable gui/$(id -u)/com.ohama.qwen32b` 로 활성화.

**검증 1회**: 실제로 Mac을 재부팅한 뒤 로그인 → 2~3분 후 `curl http://127.0.0.1:8000/v1/models` 이 자동으로 200을 반환하면 완성.

---

## 9. blueCode 연동 체크리스트

blueCode UAT 전 이 4줄이 모두 OK 여야 한다:

```bash
# 1. 두 서비스 로드
launchctl list | grep -c qwen          # → 2

# 2. 두 포트 LISTEN
lsof -iTCP:8000 -sTCP:LISTEN > /dev/null && echo ok  # → ok
lsof -iTCP:8001 -sTCP:LISTEN > /dev/null && echo ok  # → ok

# 3. 두 엔드포인트 정상 응답
curl -fsS http://127.0.0.1:8000/v1/models > /dev/null && echo ok  # → ok
curl -fsS http://127.0.0.1:8001/v1/models > /dev/null && echo ok  # → ok

# 4. blueCode 스모크 (offline에서도 startup 로그 확인 목적)
cd ~/projs/blueCode
dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj -- "hi"
# LlmUnreachable 아니라 실제 LLM 응답이 돌아오면 완성
```

---

## 10. 관련 문서

- **메모리 프로파일**: `documentation/memory-profile.md` — 32B / 72B / 둘 다 구동 시 실측 메모리 사용량 + OOM 대응

- **32B 모델 교체**: `documentation/qwen32b-base-to-instruct.md` — 현재 32B가 Base Coder(FIM 전용)로 잘못 받아져 있음. Instruct 로 교체하는 절차.
- 원본 설치 노트: `localLLM/qwen32b_install.md`, `localLLM/qwen72b_install.md`
- 본래 production 설정 원본: `localLLM/llm_production_setup.md` (이 문서의 기반)
- 프로젝트 전반: `.planning/PROJECT.md` ("Core value" → Qwen 상시 구동 전제)

---

## 부록 A: 전체 재설치 한 방 스크립트

0에서 시작해 상시 기동까지 한 번에 가는 Bash (1회용, 시간 오래 걸림):

```bash
#!/usr/bin/env bash
set -euo pipefail

ROOT=~/llm-system
mkdir -p "$ROOT"/{models,env,services/logs}

# venv
python3 -m venv "$ROOT/env/qwen-env"
source "$ROOT/env/qwen-env/bin/activate"
pip install --upgrade pip
pip install mlx-lm huggingface_hub

# 모델 다운로드
python3 - <<PY
from huggingface_hub import snapshot_download
snapshot_download("mlx-community/qwen2.5-32b-mlx",
                  local_dir="$ROOT/models/qwen32b",
                  local_dir_use_symlinks=False)
snapshot_download("mlx-community/Qwen2.5-72B-Instruct-4bit-AWQ",
                  local_dir="$ROOT/models/qwen72b",
                  local_dir_use_symlinks=False)
PY

# plist 생성 (§3.1, §3.2 내용을 이 위치에 복붙)
# ... (본문 참고)

# 로드
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen32b.plist
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen72b.plist

# 기동 대기
for port in 8000 8001; do
    until curl -fsS http://127.0.0.1:$port/v1/models > /dev/null 2>&1; do
        sleep 3
    done
done

echo "Done — 8000 & 8001 up, KeepAlive on."
```
