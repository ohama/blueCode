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

## Post-retirement (TBD — captured after Task 4)

_To be filled in by continuation agent after user executes Task 3 retirement commands._

### 1. Disk usage — home volume (post)

_(pending)_

### 2. Model directories (post)

_(pending — must show exactly: qwen35b/, qwen122b/)_

### 3. LaunchAgents plists (post)

_(pending — must show exactly: com.ohama.qwen35b.plist, com.ohama.qwen122b.plist)_

### 4. Running launchd services (post)

_(pending — must show exactly: com.ohama.qwen122b)_

### 5. 122B service health check (post)

_(pending)_

---

## Reclaim arithmetic

_(To be computed by Task 4 continuation agent)_

- Pre-retirement Used: 277 GiB
- Post-retirement Used: _(TBD)_
- Delta (reclaimed): _(TBD)_
- Threshold check: >= 50 GB _(TBD: PASS / FAIL)_

---

## Remaining-file map (post-retirement)

_(To be filled in by Task 4 — canonical post-retirement state for 19-02 docs)_

---

## SC1 verification

_(To be filled in by Task 4)_
