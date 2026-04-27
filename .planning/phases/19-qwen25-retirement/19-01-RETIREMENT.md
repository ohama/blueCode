# Phase 19-01: Qwen 2.5 Retirement — Pre/Post Snapshot

Captured as part of Plan 19-01 (`retire-qwen25-disk-reclamation`).
This document records the exact disk, service, and plist state before and after
physically retiring the Qwen 2.5 (32B / 72B) model files and launchd services.

---

## Pre-retirement (captured 2026-04-27 05:44 UTC)

All captures below are verbatim output from the user's machine.

### 1. Disk usage — home volume

```
$ df -h ~/
Filesystem      Size    Used   Avail Capacity iused ifree %iused  Mounted on
/dev/disk3s5   1.8Ti   277Gi   1.5Ti    16%    642k   16G    0%   /System/Volumes/Data
```

Pre-retirement Used: **277 GiB**

### 2. Model directories

```
$ ls -d ~/llm-system/models/*/
/Users/ohama/llm-system/models/qwen122b//
/Users/ohama/llm-system/models/qwen32b//
/Users/ohama/llm-system/models/qwen35b//
/Users/ohama/llm-system/models/qwen72b.3bit//
/Users/ohama/llm-system/models/qwen72b//
```

Five directories present: qwen122b, qwen32b, qwen35b, qwen72b.3bit (old 3-bit experiment), qwen72b.

### 3. Model directory sizes

```
$ du -sh ~/llm-system/models/*
 65G	/Users/ohama/llm-system/models/qwen122b
 17G	/Users/ohama/llm-system/models/qwen32b
 19G	/Users/ohama/llm-system/models/qwen35b
 38G	/Users/ohama/llm-system/models/qwen72b
 30G	/Users/ohama/llm-system/models/qwen72b.3bit
```

To-be-deleted: qwen32b (17G) + qwen72b (38G) + qwen72b.3bit (30G) = **85G**
To-be-preserved: qwen35b (19G) + qwen122b (65G) = **84G**

### 4. LaunchAgents plists

```
$ ls ~/Library/LaunchAgents/ | grep ohama
com.ohama.qwen122b.plist
com.ohama.qwen32b.plist
com.ohama.qwen35b.plist
com.ohama.qwen72b.plist
```

Four plists present. To-be-deleted: qwen32b, qwen72b.

### 5. Running launchd services

```
$ launchctl list | grep ohama
44880   0   com.ohama.qwen122b
```

Only `com.ohama.qwen122b` is currently loaded (PID 44880, last exit 0).
35B was already unloaded in Phase 18 (DROP-35B verdict). 32B/72B are registered but not loaded.

### 6. 122B service health check

```
$ curl -fsS http://127.0.0.1:8001/v1/models | python3 -c "import sys,json; d=json.load(sys.stdin); print('OK:', d['data'][0]['id'])"
OK: Qwen/Qwen2.5-Coder-32B
```

Note: The model id returned is `Qwen/Qwen2.5-Coder-32B` (the HF repo id reported by the
qwen122b mlx_lm.server — this is expected; see `tryParseModelId` path-preference heuristic
in QwenHttpClient.fs and CLAUDE.md §Key Seams). The service is alive and responding.

---

## Post-retirement (captured 2026-04-27)

All captures below are verbatim output from the user's machine after Task 3 execution.

### 1. Disk usage — home volume (post)

```
$ df -h ~/
Filesystem      Size    Used   Avail Capacity iused ifree %iused  Mounted on
/dev/disk3s5   1.8Ti   192Gi   1.6Ti    11%    642k   17G    0%   /System/Volumes/Data
```

Post-retirement Used: **192 GiB**

### 2. Model directories (post)

```
$ ls -d ~/llm-system/models/*/
/Users/ohama/llm-system/models/qwen122b//
/Users/ohama/llm-system/models/qwen35b//
```

Exactly two directories remain: qwen122b (122B production) and qwen35b (35B cold rollback asset).
qwen32b, qwen72b, qwen72b.3bit have been deleted.

### 3. LaunchAgents plists (post)

```
$ ls ~/Library/LaunchAgents/ | grep ohama
com.ohama.qwen122b.plist
com.ohama.qwen35b.plist
```

Exactly two plists remain: qwen122b and qwen35b.
qwen32b.plist and qwen72b.plist have been deleted.

### 4. Running launchd services (post)

```
$ launchctl list | grep ohama
44880   0   com.ohama.qwen122b
```

Only `com.ohama.qwen122b` (PID 44880, last exit 0) is running.
No qwen32b or qwen72b services appear.

### 5. 122B service health check (post)

```
$ curl -fsS http://127.0.0.1:8001/v1/models | python3 -c "import sys,json; d=json.load(sys.stdin); print('OK:', d['data'][1]['id'])"
OK: /Users/ohama/llm-system/models/qwen122b
```

122B service alive and serving correctly. data[1].id confirms the local path model.
(See "Verification-script gotcha" addendum below for why data[1] is used, not data[0].)

---

## Reclaim arithmetic

**Reclaim total: 85 GiB** (threshold >= 50 GB — PASS)

- Pre-retirement Used: **277 GiB**
- Post-retirement Used: **192 GiB**
- Delta (reclaimed): **85 GiB**
- Expected: ~85 GB (qwen32b 17G + qwen72b 38G + qwen72b.3bit 30G = 85G)
- Threshold check: >= 50 GB — **PASS** (85 GiB >> 50 GB)

---

## Remaining-file map (post-retirement)

This is the canonical post-retirement state that 19-02 docs and code will describe.

**Preserved model directories:**

| Path | Size | Purpose |
|------|------|---------|
| `~/llm-system/models/qwen122b/` | ~65G | Production model (single-model canonical) |
| `~/llm-system/models/qwen35b/` | ~19G | Cold rollback asset (ROADMAP Decision A) |

**Preserved launchd plists:**

| Path | Status |
|------|--------|
| `~/Library/LaunchAgents/com.ohama.qwen122b.plist` | Active (port 8001, PID 44880) |
| `~/Library/LaunchAgents/com.ohama.qwen35b.plist` | Installed but NOT loaded (cold standby) |

---

## SC1 verification

All three SC1 criteria from the plan's `<verification>` section are confirmed:

- [x] **launchctl = 122B only** — `launchctl list | grep ohama` returns exactly one line: `com.ohama.qwen122b` (PID 44880). See Post-retirement §4.
- [x] **models/ = qwen35b + qwen122b only** — `ls -d ~/llm-system/models/*/` returns exactly two entries. qwen32b, qwen72b, qwen72b.3bit deleted. See Post-retirement §2.
- [x] **LaunchAgents/ = qwen35b + qwen122b plists only** — `ls ~/Library/LaunchAgents/ | grep ohama` returns exactly two filenames. See Post-retirement §3.

Additional confirmation: `curl -fsS http://127.0.0.1:8001/v1/models -o /dev/null` returns exit 0 (122B service unaffected throughout retirement).

---

## Verification-script gotcha

**Issue:** The plan's Task 1 health check used `d['data'][0]['id']` which returns
`Qwen/Qwen2.5-Coder-32B` — a hardcoded HF repo fallback id that `mlx_lm.server` announces
regardless of which model is actually loaded. This is the misleading `data[0]` behaviour.

**Root cause:** `mlx_lm.server` lists the HF repo id first in `data[]`, then appends the
actual local path as `data[1]`. The local path is the truth; the HF id is a static fallback.

**Resolution:** Post-retirement Task 4 health check uses `d['data'][1]['id']`, which returns
`/Users/ohama/llm-system/models/qwen122b` — confirming the correct local model is loaded.

**Reference:** CLAUDE.md `## Key Seams → Model id flow` documents the `tryParseModelId`
path-preference heuristic that handles this same discrepancy inside `QwenHttpClient.fs`.
The verification script should mirror that logic: prefer `data[*].id` starting with `"/"`.

This is worth a `/howto` entry (e.g., `documentation/howto/verify-mlx-lm-server-model-id.md`)
to prevent future verification scripts from being fooled by `data[0]`.

---

## Pre-flight (Task 2 — captured 2026-04-27 05:44 UTC)

**Pre-flight: PASS — only com.ohama.qwen122b loaded**

```
$ launchctl list | grep ohama
44880   0   com.ohama.qwen122b
```

Only `com.ohama.qwen122b` (PID 44880) is running. 35B was unloaded in Phase 18.
32B/72B plists exist on disk but their services are not loaded — Task 3 will unload
and delete them.

**Preservation check:**

```
$ [ -d ~/llm-system/models/qwen122b ] && echo "qwen122b: PRESERVE OK" || echo "MISSING qwen122b"
qwen122b: PRESERVE OK

$ [ -d ~/llm-system/models/qwen35b ] && echo "qwen35b: PRESERVE OK" || echo "MISSING qwen35b"
qwen35b: PRESERVE OK
```

Both directories confirmed present before retirement begins. Task 3 will NOT touch these.

**Pre-flight verdict: SAFE TO PROCEED to Task 3 (user retirement commands).**
