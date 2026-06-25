# tmux-cli-session — 디렉터리별 tmux 세션으로 임의의 CLI 실행

아무 CLI(claude, codex, aider, gemini …)를 **디렉터리마다 전용 tmux 세션** 안에서 띄워,
연결이 끊겨도(SSH 끊김·터미널 종료·폰 화면 꺼짐) 작업이 **머신에서 계속 진행**되게 하는 도구.

---

## 핵심 아이디어

- CLI를 **tmux 세션 안에서** 실행 → 터미널이 끊겨도 세션은 살아남고 작업이 계속됨.
- 세션을 **(디렉터리 × 도구)** 마다 1개로 자동 매핑 → 프로젝트별·도구별 독립 에이전트 유지.
- 세션명 = `<디렉터리이름>_<PWD SHA 해시 6자리>_<도구>`
  - 경로가 다르면 충돌 없음, 도구가 맨 뒤라 같은 폴더에서도 claude/codex 분리.

```
ssh/터미널/폰 ──attach──▶ tmux 세션 "<dir>_<hash>_<cli>" ──▶ CLI (작업 계속)
            끊겨도 ↑ 세션 유지 → 다시 attach 하면 이어서 진행
```

---

## 구성 (`~/.local/bin/`)

```
tmux-cli-session      공통 코어 (세션 생성/검색/attach 로직, 한 곳)
my<tool> → tmux-cli-session   심볼릭 (현재 myclaude, mycodex)
tmux-cli-register     새 도구 등록 헬퍼 (심볼릭 + alias 자동 생성)
```

`~/.zshrc_aliases`:
```sh
alias claude='$HOME/.local/bin/myclaude'
alias codex='$HOME/.local/bin/mycodex'
```

### 작동 원리
- 코어는 **자기가 불린 이름(`$0`)** 으로 도구를 정한다: `my<tool>` 로 호출되면 `my` 를 떼어
  `<tool>` 을 CLI로 사용. (`tmux-cli-session <CLI>` 로 직접 호출하면 첫 인자가 CLI.)
- 진짜 바이너리는 **`type -P <tool>`** 로 찾는다(스크립트엔 alias 미적용) → alias가 자기 자신을
  부르는 무한루프 없음.
- 도구마다 별도 코드 불필요 — **심볼릭 1개 + alias 1줄**이면 끝.

---

## 사용법

### 디렉터리에서 그냥 실행
```sh
cd ~/projs/myapp
codex        # myapp_<해시>_codex 세션 attach(없으면 생성) → 그 안에서 codex 실행
claude       # 같은 폴더라도 myapp_<해시>_claude (독립 세션)
```
같은 디렉터리에서 다시 같은 명령 → **같은 세션 재접속** (직전 화면 그대로).

### 이름으로 세션 지정/검색
```sh
codex myapp        # 이 도구(codex) 세션만 대상, dirname 매치 → myapp_<해시>_codex
codex myapp_a1     # 해시 prefix 매치 → myapp_a1b2c3_codex
codex scratch      # 매치 없으면 'scratch' 세션 새로 생성
```
- 검색은 **호출한 도구의 세션만** 대상(`codex`는 codex, `claude`는 claude).
- 후보가 2개 이상이면 목록을 출력하고 멈춤 → 더 긴 prefix/전체 이름으로 재호출.

### 세션 명명 규칙
```
세션명 = $(basename "$PWD" | tr '.:' '__')_$(echo -n "$PWD" | shasum | cut -c1-6)_<CLI>
예) /Users/ohama/projs/codex 에서  codex  → codex_a39712_codex
                                  claude → codex_a39712_claude
```

---

## 다른 도구 추가

```sh
tmux-cli-register aider           # myaider 심볼릭 + alias aider 생성
tmux-cli-register gemini qwen     # 여러 개 한꺼번에
source ~/.zshrc                   # (또는 새 셸)

cd ~/projs/x && aider             # x_<해시>_aider 세션에서 aider 실행
```
- 바이너리가 아직 없어도 등록 가능(경고만) — 나중에 설치하면 그대로 동작.
- 수동 등록:
  ```sh
  ln -sf tmux-cli-session ~/.local/bin/my<tool>
  echo "alias <tool>='\$HOME/.local/bin/my<tool>'" >> ~/.zshrc_aliases
  ```

---

## tmux 기본 조작 ("끊겨도 진행"의 핵심)

| 동작 | 키/명령 |
|------|---------|
| 세션에서 빠져나오기(작업 유지) | `Ctrl+b` 뗀 뒤 `d` (detach) |
| 다시 들어가기 | `<tool>`(같은 디렉터리) 또는 `<tool> <이름>` |
| 세션 목록 | `tmux ls` |
| 세션 종료 | `tmux kill-session -t <이름>` |
| 스크롤 모드 | `Ctrl+b` 뗀 뒤 `[` (방향키, `q` 종료) |

CLI는 tmux 세션(=백그라운드 프로세스) 안에서 돈다. 터미널/SSH/폰이 끊겨도 tmux 서버가 세션을
유지하므로 작업이 멈추지 않고, 다시 attach 하면 그동안 진행분이 보인다. 원격(폰 등)에서 SSH로
접속해 `<tool>` 로 attach 하면 이동 중 끊겨도 그대로 이어진다.

---

## 주의

- **비대화형 `codex exec` 는 별개**다. 이 도구는 *대화형 CLI를 tmux로 유지*하는 것.
  스크립트/CI로 `codex exec` 를 돌릴 땐 stdin을 닫아야 한다: `codex exec "..." < /dev/null`.
- **머신이 sleep** 되면 tmux 세션도 멈춘다. 장시간 원격 작업 시 `caffeinate -s`(macOS) 또는 잠자기 방지.
