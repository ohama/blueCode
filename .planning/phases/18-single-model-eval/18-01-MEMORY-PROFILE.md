# Phase 18-01: 35B unload + memory profile

**Date:** 2026-04-27T02:58:00Z
**Operator:** ohama (interactive checkpoint)
**Pre-unload state:** Dual-loaded (35B @ 8000 + 122B @ 8001), Phase 17 canonical configuration.
**Post-unload target state:** Single-loaded (122B @ 8001 only), 35B unregistered from launchd.

---

## §1 Pre-unload baseline

Snapshot timestamp: 2026-04-27T02:58:00Z
Snapshot raw output: archived in `/tmp/18-01-pre.txt` (transient; key fields tabulated below).

| Metric                   | Value (pre-unload)                    | Source                                 |
|--------------------------|---------------------------------------|----------------------------------------|
| PhysMem used             | 126 GB                                | `top -l 1` PhysMem line               |
| PhysMem unused           | 1.58 GB (1618 MB)                     | `top -l 1` PhysMem line               |
| Compressor               | 463 MB                                | `top -l 1` PhysMem line (Compressor field) |
| Wired Memory             | 3775 MB                               | `top -l 1` PhysMem line               |
| 35B RSS                  | 16.93 GB (PID 44878)                  | `ps -o pid,rss` for qwen35b (17750784 KB) |
| 122B RSS                 | 45.42 GB (PID 44880)                  | `ps -o pid,rss` for qwen122b (47616656 KB) |
| Combined RSS             | 62.35 GB                              | 35B + 122B                             |
| launchctl ohama services | qwen35b + qwen122b                    | `launchctl list \| grep ohama`         |
| Port 8000 listener       | mlx_lm.server (qwen35b PID 44878)     | `lsof -iTCP:8000`                      |
| Port 8001 listener       | mlx_lm.server (qwen122b PID 44880)    | `lsof -iTCP:8001`                      |

Pre-unload comparison vs Phase 17 dual-loaded steady-state (`17-02-LOAD-TEST.md` §6):
- Phase 17: 35B RSS ~17 GB, 122B RSS ~45.4 GB, combined ~62.4 GB, PhysMem unused ~1.6 GB, Compressor ~541 MB.
- 18-01 pre-unload: 35B RSS 16.93 GB, 122B RSS 45.42 GB, combined 62.35 GB, PhysMem unused 1.58 GB, Compressor 463 MB.
- Delta vs Phase 17: Nearly identical. Combined RSS differs by only 0.05 GB. Compressor is 78 MB lower (541 → 463 MB), possibly due to lighter background load at time of snapshot. System is in expected dual-loaded steady-state.

---

## §2 Unload action (checkpoint outcome)

<populated in Task 2 — CHECKPOINT PENDING>

---

## §3 Post-unload baseline

<populated in Task 3>

---

## §4 122B health verification

<populated in Task 4>

---

## §5 Observations and hypothesis verdict

<populated in Task 5>
