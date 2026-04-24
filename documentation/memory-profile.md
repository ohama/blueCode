# Qwen 32B + 72B 메모리 프로파일

128GB Mac 에서 Qwen 32B / 72B / 둘 다 구동 시 실측 메모리 사용량과 OOM 위험 평가. 측정 2026-04-24.

> **TL;DR** 128GB Mac 에서 32B + 72B 동시 상시 구동 **가능하지만 여유 1GB 미만** — 빠듯. Instruct 모드 단답 워크로드에서 3회 연속 blueCode 요청 OOM 없이 통과. 과거 OOM 사례 (v1.1 세션)는 **mlx_lm.server HF-fallback regression + 1024-token Base-mode continuation** 으로 인한 것이었고, v1.1 06-03 gap closure 이후 재현 안 됨.

---

## 1. 측정 환경

| 항목 | 값 |
|------|-----|
| 하드웨어 | Mac16,9 (Apple Silicon), 128 GB unified memory, 16 코어 |
| OS 기준 프로세스 | Mac 기본 + 사용자 앱들 (세션 시점 실사용 상태) |
| 32B 모델 | `~/llm-system/models/qwen32b` Instruct (MLX 4-bit, 13 shards, 17GB 디스크) |
| 72B 모델 | `~/llm-system/models/qwen72b` Instruct AWQ 4-bit (8 shards, 38GB 디스크) |
| 서빙 | `mlx_lm.server` 0.31.3, Python 3.14, launchd |
| 측정 도구 | `top -l 1`, `ps -o rss`, `vm_stat`, 서비스 err 로그 |
| blueCode 버전 | v1.1 (post-06-03 gap closure) |

---

## 2. 실측 데이터

각 시나리오 순서: 서비스 로드 대기 → idle 측정 → bare curl smoke → 3회 `blueCode --model <X> "Say OK in 3 words"` → post-inference 측정.

### 2.1 시나리오별 메모리 (PhysMem from `top`)

| 상태 | Used | Unused (free) | Wired | Compressor | 비고 |
|------|------|---------------|-------|-----------|------|
| **Phase 0 — Baseline** (no services) | 69 GB | 58 GB | 3.0 GB | 205 MB | OS + 사용자 앱 기준선 |
| **Phase 1 — 32B alone (idle)** | 88 GB | 40 GB | 3.1 GB | 205 MB | |
| **Phase 1 — 32B alone (post 3× inference)** | 88 GB | 39 GB | 22 GB | 205 MB | 추론 시 weights가 wired로 pin |
| **Phase 2 — 72B alone (idle)** | 108 GB | 20 GB | 4.3 GB | 205 MB | |
| **Phase 2 — 72B alone (post 3× inference)** | 108 GB | 19 GB | 42 GB | 205 MB | |
| **Phase 3 — Both (idle)** | 125 GB | 1.8 GB | 3.8 GB | 205 MB | **여유 극히 빠듯** |
| **Phase 3 — Both (post bare smoke 양쪽)** | 126 GB | 1.0 GB | 60 GB | 205 MB | |
| **Phase 3 — Both (post 3× 섞인 stress)** | 127 GB | **700 MB** | 21 GB | 205 MB | **한계 근처** |

**Compressor 값이 세션 내내 205 MB 고정** — macOS 가 메모리 압축을 활발히 시도하지 않음 = 실질적 swap 압박 없음. 숫자만 빠듯하지 실제 동작은 안정.

### 2.2 프로세스별 RSS

| 프로세스 | RSS (post-load, idle) | RSS (post-inference) | %MEM |
|----------|----------------------|---------------------|------|
| `mlx_lm.server qwen32b` (PID 59076) | 18.4 GB | 18.4 GB | 13.7% |
| `mlx_lm.server qwen72b` (PID 64275) | 40.4 GB | 40.4 GB | 30.1% |
| **두 프로세스 합계** | **58.8 GB** | **58.8 GB** | ~43.8% |

RSS 는 inference 전후 거의 동일 (weights + tokenizer 가 대부분). KV cache 와 prompt cache 는 별도 영역.

### 2.3 Prompt Cache 축적 (inference 당 증가)

`mlx_lm.server` 는 각 완료된 chat 요청의 prompt + 생성 결과를 세션 캐시에 보관해 다음 요청의 prefix 매칭에 사용. 누적 시 메모리 압박의 주 원인.

| 측정 시점 | 32B Prompt Cache | 72B Prompt Cache |
|----------|-----------------|-----------------|
| Phase 1 초기 | 5 sequences, 0.83 GB (이전 세션 잔존) | — |
| Phase 1 post-3× | 5 sequences, 0.83 GB (변화 없음 — 이미 캐시됨) | — |
| Phase 2 post-72B load | — | 0 → 2 sequences, 0.10 GB |
| Phase 3 post-stress | 0 → 2 sequences, 0.08 GB (32B kickstart 후 재시작) | 2 sequences, 0.10 GB |

**축적 속도**: 짧은 "Say OK" 요청 1회 당 대략 40-80 MB 씩 증가. v1.1 세션 중 1.51 GB 까지 쌓여 32B OOM 임박한 사례 있음 (그때는 Base mode 1024-token continuation).

### 2.4 추론 시간 (v1.1 post-06-03)

| 시나리오 | blueCode 각 run 시간 |
|---------|---------------------|
| 32B alone | 2, 2, 3 초 |
| 72B alone | 8, 5, 5 초 |
| Both (mixed 32B/72B) | 4, 5, 3 초 |

첫 run 은 항상 조금 느림 (cold prompt processing). Subsequent calls benefit from prompt cache prefix matching.

### 2.5 서비스 안정성 (세션 통계)

| 지표 | 32B | 72B |
|------|-----|-----|
| `Starting httpd` 기록 | 10회 | 6회 |
| `[METAL] Insufficient Memory` | **1회** (v1.1 세션, pre-gap-closure) | 0회 |
| KeepAlive 재기동 | 여러 차례 (ThrottleInterval 30s) | — |

**32B OOM 1건 내역**: 2026-04-24 09:18:45, blueCode 가 아직 수정 전 HF-fallback → Base-mode 상태로 1024 max_tokens continuation 생성 중 발생. prompt cache 가 0.83GB 에서 1.51GB 로 급증하며 Metal GPU 메모리 한계 도달. v1.1 06-03 gap closure (`a794b42`) 이후 재현 안 됨.

---

## 3. 용량 분석

### 3.1 모델별 메모리 실수요

```
32B 모델 (MLX 4-bit):
  RSS:         18.4 GB   (weights + tokenizer + overhead)
  추론 wired:  +22 GB    (GPU에 pinned, process RSS와 일부 중복)
  KV cache:   ~50 MB/req (Instruct 짧은 JSON 응답)
  Total:       ~22 GB 실질 (idle), ~25 GB 피크 (active inference)

72B 모델 (MLX AWQ 4-bit):
  RSS:         40.4 GB   (weights + tokenizer + overhead)
  추론 wired:  +42 GB    (GPU에 pinned)
  KV cache:   ~100 MB/req (Instruct)
  Total:       ~44 GB 실질 (idle), ~48 GB 피크
```

### 3.2 시스템 capacity

```
총 RAM:        128 GB
OS baseline:  ~69 GB   (kernel + 이 사용자의 실제 앱 부하)
              ─────────
여유:          ~59 GB  모델 서빙에 사용 가능

32B only:     25 GB / 59 GB used  → 34 GB 헤드룸 (충분)
72B only:     48 GB / 59 GB used  → 11 GB 헤드룸 (여유)
Both:         ~66 GB / 59 GB      → -7 GB (OS 압축으로 흡수)
              ↑ 이것이 현재 125-127 GB used / 700 MB - 1.8 GB free 상태
```

**Both 시나리오에서 이론 수요가 59 GB 여유를 약간 초과**. macOS 가 OS 페이지를 압축/swap-eligible 로 옮겨 수용. Compressor 205 MB 고정 관찰은 이 메커니즘이 아직 본격 발동 안 했다는 뜻 — 얕은 스왑 예비.

---

## 4. 문제 상황 가능성

### 4.1 OOM 발동 조건 (재현된 것)

v1.1 세션 재현 데이터 (06-03 fix 이전):
1. Both services loaded (기본 상태)
2. blueCode 가 HF repo id 를 POST body 에 보냄
3. mlx_lm.server 가 HF Hub 에서 Base Coder tokenizer fetch → Instruct template 덮어씀
4. 응답이 JSON 대신 1024-token continuation → KV cache 폭증
5. prompt cache 0.83 GB → 1.51 GB
6. Metal command buffer allocation 실패 → `kIOGPUCommandBufferCallbackErrorOutOfMemory`
7. 32B 프로세스 abort → KeepAlive 재기동

### 4.2 OOM 발동 안 한 조건 (현재 상태)

v1.1 06-03 post-fix:
1. Both services loaded
2. blueCode 가 로컬 경로 id 를 POST → mlx_lm.server HF fetch 안 함 → Instruct 유지
3. 응답 ~50 토큰 JSON → KV cache 작음 (~50 MB)
4. 3회 연속 blueCode 요청 완료 — OOM 없음, 서비스 재기동 없음, 각 2-5초

**구조적 결론**: Mac 128GB 에서 32B + 72B 동시 운영은 **정상 Instruct 워크로드에서는 안전**. OOM 위험은 "특정 요청이 비정상적으로 큰 생성을 유발할 때" 발생.

### 4.3 가능한 OOM 시나리오 (미재현, 이론적 위험)

| 시나리오 | 발동 메커니즘 | 대응 |
|---------|--------------|------|
| 연속 수백 회 요청 | prompt cache 1 GB 이상 누적 | §5.2 주기적 kickstart |
| 긴 출력 요청 (`max_tokens: 4096+` 코드 생성) | KV cache 대형 할당 | `max_tokens` 제한 (현재 blueCode 1024 고정) |
| Very long context (multi-turn 으로 8000+ 토큰) | history 누적 | v1.0 context 80% warning 발동; blueCode 가 경고 후 계속 |
| OS 측 큰 앱 로드 (VM, Docker, Xcode build) | baseline 이 69 → 80GB+ 로 상승 | §5.1 한 모델만 서빙 |
| 32B Base model 재도입 | HF fetch → Base → 1024 continuation 재발 | 방지됨 (06-03 gap closure). Instruct 유지 검증 문서: `qwen32b-base-to-instruct.md` |

---

## 5. 권장 운영 전략

### 5.1 Mac 용량별 운영 프로파일

| Mac RAM | OS baseline 여유 | 권장 구성 |
|---------|-----------------|----------|
| 64 GB | OS baseline ~35 GB 가정 → 모델 여유 29 GB | **32B only** (22 GB) |
| 96 GB | 여유 ~60 GB | **72B only** (48 GB) 또는 32B only |
| 128 GB | 여유 ~59 GB (현재 기기) | **Both** (66 GB, OS 압축으로 흡수) — but 타이트 |
| 128 GB (OS lean) | OS baseline ~35-40 GB | Both (편안) |
| 192 GB+ | 여유 120 GB+ | Both (여유) |

본 기기 (128 GB, OS baseline 69 GB) 는 **dual-service 가능 영역의 하한**. 타 앱 사용량이 늘면 한 모델만 유지 권장.

### 5.2 Prompt cache 누적 대응

긴 세션 후 주기적 kickstart 로 메모리 리셋:

```bash
# 개별 서비스 리셋 (다른 모델은 영향 없음)
launchctl kickstart -k gui/$(id -u)/com.ohama.qwen32b
# 또는
launchctl kickstart -k gui/$(id -u)/com.ohama.qwen72b
```

**타이밍 휴리스틱**:
- 100+ requests 후
- 서비스 err 로그에서 `Prompt Cache: N sequences, 1.0+ GB` 관찰될 때
- 응답이 평소보다 현저히 느려질 때 (prompt cache 가 커져 prefix matching 비용 증가)

현재 페이스 (blueCode 한 요청 당 40-80 MB 증가) 로는 100-300 requests 까지 안전. 실질적으로 하루 수백 회 사용 시에만 영향.

### 5.3 긴급 OOM 대응

서비스가 `METAL Insufficient Memory` 로 크래시 감지 시:

```bash
# 1. 크래시한 서비스는 KeepAlive=true 덕에 자동 재기동 중 (30초 throttle)
#    확인:
tail -5 ~/llm-system/services/logs/{32b,72b}.err

# 2. 만약 재기동 실패 (Address already in use 등):
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen{32b,72b}.plist
sleep 5
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen{32b,72b}.plist

# 3. 메모리 여유 확인 후 복구:
top -l 1 -n 0 | grep PhysMem
```

근본 원인 진단 가이드: `documentation/howto/debug-local-llm-server-responses.md`.

### 5.4 One-model-at-a-time 운영

메모리 극단 절약 필요 시 on-demand load 패턴:

```bash
# 필요한 모델만 load
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen72b.plist

# 작업 후 unload
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen72b.plist
```

blueCode 는 `--model 72b` 나 intent routing 이 해당 포트 접속 가능 여부로 서비스 활성을 감지. unload 된 모델 요청 시 `AgentError.LlmUnreachable` 로 깔끔히 종료.

Plist 의 `RunAtLoad=true` 때문에 재부팅 시 자동 기동됨 — on-demand 로 완전히 막으려면 `RunAtLoad=false` + `KeepAlive=false` 로 편집하고 `launchctl load -w` 대신 수동 시작으로 변경.

---

## 6. v1.1 아키텍처가 낮춘 위험 요소

2026-04-24 Phase 6 06-03 gap closure (`a794b42`) 와 v1.1 전반이 메모리 안정성에 미친 영향:

| 개선 | 효과 |
|------|------|
| `tryParseModelId` local-path preference | mlx_lm.server HF fetch 차단 → Instruct tokenizer 유지 → 응답 ~50토큰 → KV cache 축소 |
| Lazy per-port probe (v1.1 REF-02) | 미사용 모델 port 에는 network 터치 안 함 (blueCode 쪽) — 단, launchd 가 서비스를 이미 로드했으면 메모리엔 모델 적재됨 |
| 짧은 Instruct JSON 응답 | KV cache 증가량 50 MB / 응답 수준 (Base mode 1024-token continuation 대비 20-30x 절감) |

**핵심 통찰**: 오늘 측정 전까지 128GB Mac OOM 이력은 전부 v1.0 hotfix 시대의 Base-mode regression 이었다. v1.1 post-06-03 환경에서는 128GB 에서 dual-service 안정.

---

## 7. 관련 문서

- `documentation/local-llm-services.md` — launchd 서비스 운영 / 복구
- `documentation/qwen32b-base-to-instruct.md` — 32B 모델 교체 (Base → Instruct)
- `documentation/howto/debug-local-llm-server-responses.md` — 이상 응답 체계적 디버깅
- `documentation/howto/identify-base-vs-instruct-llm.md` — 모델 타입 판별
- `.planning/milestones/v1.1-ROADMAP.md` — Phase 6 + 06-03 gap closure 전모

---

*측정 완료: 2026-04-24*
*측정 환경: Mac16,9 128GB, macOS, mlx_lm.server 0.31.3, blueCode v1.1 post-06-03*
