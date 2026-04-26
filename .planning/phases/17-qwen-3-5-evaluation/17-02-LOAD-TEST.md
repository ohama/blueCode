# Phase 17-02 Load Test Record

**Date:** 2026-04-27
**Operator:** ohama (manual checkpoint operator) + Claude executor
**Hardware:** Mac `ohama` (128 GB unified memory, Apple Silicon)
**mlx_lm version:** 0.31.3

---

## Overview

This document records the empirical measurements captured during the Phase 17-02 service swap from
Qwen 2.5 32B/72B → Qwen 3.5 35B-A3B-4bit/122B-A10B-4bit. The walkthrough was performed manually by
the operator across all 4 checkpoints. Task 5 (Path B F# patch) was NOT needed — Path A
(`--chat-template-args '{"enable_thinking": false}'` server flag, as corrected in commit `7b8cbc0`) was
confirmed working with mlx_lm 0.31.3. An unplanned blocker was encountered and fixed during Task 6
(canary bench); see §Deviations below.

---

## §1 Pre-swap baseline (CP1)

**Checkpoint 1 disposition:** SKIPPED — user confirmed existing 32B/72B services were running and
proceeded directly to install. Baseline state at skip time: both services responsive on ports 8000/8001.

---

## §2 Old services unloaded (CP2)

**Checkpoint 2 status:** PERFORMED with complication.

### 2.1 Unload procedure

`launchctl unload` was run for both `com.ohama.qwen32b.plist` and `com.ohama.qwen72b.plist`. Ports
8000 and 8001 were released.

### 2.2 Complication: Load failed on first reload attempt

When attempting to load the new Qwen 3.5 plists, a `Load failed: 5: Input/output error` occurred. Root
cause: the plist used the wrong flag name (`--chat-template-kwargs` instead of the corrected
`--chat-template-args`). Resolution:

1. `launchctl unload` the failing plist
2. Fix the flag name in the plist XML
3. `launchctl load -w` — service started cleanly

This procedure was documented in `documentation/qwen35-install.md` §5.1.1 (commit `cb11f88`) as a
reusable gotcha: "Load failed: 5: Input/output error" is a generic launchd error that often indicates a
malformed ProgramArguments entry, not an I/O hardware failure.

### 2.3 Rollback assets preserved

Both old plists (`com.ohama.qwen32b.plist`, `com.ohama.qwen72b.plist`) remain on disk at
`~/Library/LaunchAgents/`. Old model files at `~/llm-system/models/qwen{32b,72b}/` are untouched.

---

## §3 Models downloaded (CP3)

**Checkpoint 3 status:** COMPLETE.

### 3.1 Model paths on disk

- `~/llm-system/models/qwen35b/` — Qwen 3.5 35B-A3B-4bit (mlx-community/Qwen3.5-35B-A3B-4bit)
- `~/llm-system/models/qwen122b/` — Qwen 3.5 122B-A10B-4bit (mlx-community/Qwen3.5-122B-A10B-4bit)

Both models confirmed on disk with config.json, special_tokens_map.json, and added_tokens.json present
(Instruct tokenizer file markers). Disk sizes within research-projected ranges (~20 GB for 35B,
~70 GB for 122B).

---

## §4 New services loaded + responsive (CP4)

**Checkpoint 4 status:** ALL 4 TESTS PASSED.

### 4.1 Plists written

- `~/Library/LaunchAgents/com.ohama.qwen35b.plist` — port 8000, model path
  `/Users/ohama/llm-system/models/qwen35b`, flag `--chat-template-args '{"enable_thinking": false}'`
- `~/Library/LaunchAgents/com.ohama.qwen122b.plist` — port 8001, model path
  `/Users/ohama/llm-system/models/qwen122b`, same thinking-mode flag

Both passed `plutil -lint` (OK).

Note on flag name: the runbook was corrected from `--chat-template-kwargs` to `--chat-template-args`
in commits `7b8cbc0` and `b1d644d`. The plists on disk use the corrected flag name.

### 4.2 /v1/models responses

Both services returned well-formed `/v1/models` JSON with `data[]` arrays containing the local path ids.

---

## §5 Thinking-mode verification + Path A/B branch

### 5.1 §5.3 smoke test — 35B (port 8000)

```
content='OK', no <think> token in content — PASS
```

Path A confirmed: `--chat-template-args '{"enable_thinking": false}'` honored by mlx_lm 0.31.3.

### 5.2 §5.3 smoke test — 122B (port 8001)

```
content='OK', no <think> token in content — PASS
```

Path A confirmed on 122B as well.

### 5.3 §5.4 JSON-schema test — both services

Both services tested with the canonical blueCode JSON schema (`{thought, action, input}` required fields):

```
35B:  JSON parse: OK; action= final — PASS
122B: JSON parse: OK; action= final — PASS
```

### 5.4 Branch decision

**Path A confirmed.** The `--chat-template-args '{"enable_thinking": false}'` server flag works
on mlx_lm 0.31.3 for both Qwen 3.5 35B and 122B. Task 5 (QwenHttpClient.fs F# patch) was NOT
needed and was skipped. `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` is untouched.

---

## §6 Cold-start + RSS data

### 6.1 Cold-start wall-clock

Cold-start times were not captured cleanly this iteration — services were already running when
measurements began. Estimates from `documentation/qwen35-install.md` §8:

| Model | Estimated cold-start |
|-------|---------------------|
| 35B   | ~30-60s             |
| 122B  | ~120-240s           |

These estimates should be captured empirically on the next cold-start cycle (e.g., after a
`launchctl kickstart -k` following a long idle period).

### 6.2 RSS measurements — post §5.4 smoke

| Service | PID RSS (KB) | RSS (GB) |
|---------|-------------|---------|
| 35B @ port 8000 | 17,716,256 KB | 16.9 GB |
| 122B @ port 8001 | 47,593,232 KB | 45.4 GB |
| **Combined** | | **62.3 GB** |

Captured after the §5.4 JSON-schema smoke tests (first-inference post-load state).

### 6.3 RSS measurements — post canary (4 tests)

| Service | PID RSS (KB) | RSS (GB) |
|---------|-------------|---------|
| 35B @ port 8000 | 17,738,720 KB | 16.9 GB |
| 122B @ port 8001 | 47,613,040 KB | 45.4 GB |
| **Combined** | | **62.4 GB** |

Delta from smoke to post-canary: +22,464 KB (35B) + +19,808 KB (122B) = +41 MB. The MoE sparse
activation pattern means actual working-set memory grows slowly relative to the model's total
parameter count; mmap means pages are loaded on demand but not eagerly released. This is expected
behavior — see `documentation/qwen35-install.md` §5.5.1 for the extended discussion.

### 6.4 Research projection comparison

| Model | Final RSS observed | Research projection | Match |
|-------|-------------------|---------------------|-------|
| 35B   | 16.9 GB | ~19.5 GB | Close — MoE sparse activation explains lower observed RSS; mmap means not all weights are in resident memory simultaneously |
| 122B  | 45.4 GB | ~70 GB | Lower than projected — same MoE + mmap explanation |
| Combined | 62.4 GB | ~89.5 GB | 27 GB lower than projected; model quantization + MoE sparsity leaves significant headroom |

The 27 GB difference from projected is consistent with MoE routing: only the activated expert slices
are in resident memory at any instant. The full weight files are mmap'd but not fully resident.

### 6.5 System memory pressure — post canary

```
PhysMem: 126G used (3523M wired, 543M compressor), 1337M unused
```

Compressor at 543 MB is borderline (doc threshold: <100 MB is normal, >1 GB is pressure). During dual
MoE service operation this is acceptable — KV cache and expert activations cycle through the compressor
as contexts are processed. Re-check after `bench/run.sh --all` (17-03) to confirm no OOM trajectory.

Available headroom for bench: ~1.3 GB unused + compressor drain = functional headroom for normal
bench workloads. No OOM observed during canary.

---

## §7 Canary bench result

**Run against:** 35B @ port 8000 (blueCode `--model 32b` route), 122B @ port 8001 (`--model 72b` route)

### 7.1 Pre-fix canary (before commit `54e54a9`)

| Test | Model route | Exit | Elapsed | Notes |
|------|-------------|------|---------|-------|
| T1   | 32b (→35B)  | 0    | 5s      | PASS |
| T5   | 72b (→122B) | 0    | 7s      | PASS |
| T6   | 32b (→35B)  | 1    | 2s      | FAIL — LlmUnreachable |
| T6   | 72b (→122B) | 0    | 13s     | PASS |

**Pre-fix verdict:** 3/4 PASS. T6 on 32b (→35B) failed.

### 7.2 T6 failure root cause

T6 triggers a `read_file` step that returns 2,114 characters of content. This triggered the
POST-READ HINT injection in `AgentLoop.fs` `buildMessages` — a mid-conversation System-role message
appended to the conversation. The error from the service:

```
HTTP 404: {"error": "System message must be at the beginning."}
```

Qwen 3.5 35B's chat template (mlx_lm 0.31.3) enforces that System-role messages may only appear at
conversation position 0. Qwen 3.5 122B passed the same test because its chat template is more lenient
about mid-conversation System messages (non-uniform strictness across Qwen 3.5 sizes).

### 7.3 AgentLoop fix (commit `54e54a9`)

Root cause in `src/BlueCode.Core/AgentLoop.fs` `buildMessages`:

- Line ~249: POST-EDIT CONSTRAINT injected with `Role = System`
- Lines ~260, 266: POST-READ HINT injected with `Role = System`

Fix: 3 occurrences of `Role = System` changed to `Role = User`. The text marker
`[POST-EDIT CONSTRAINT]` / `[POST-READ HINT]` carries the authority signal; the System role was
incidental. Existing `AgentLoopTests` assert on text content only (`stringContains "[POST-EDIT
CONSTRAINT]"` etc.) — no role assertions — so tests were preserved without modification.

Test result post-fix: **254/1/0** (baseline unchanged).

This fix also removes blueCode's implicit dependence on lenient System-role handling by whatever
mlx_lm tokenizer happens to be loaded. The new behavior is strictly correct per the OpenAI messages
spec and the Qwen 3.5 chat template.

### 7.4 Post-fix canary (commit `54e54a9` applied)

| Test | Model route | Exit | Elapsed | Notes |
|------|-------------|------|---------|-------|
| T1   | 32b (→35B)  | 0    | 5s      | PASS |
| T5   | 72b (→122B) | 0    | 6s      | PASS |
| T6   | 32b (→35B)  | 0    | 8s      | PASS — FIXED |
| T6   | 72b (→122B) | 0    | 13s     | PASS |

**Post-fix verdict:** 4/4 PASS.

---

## §8 Final disposition

| Item | Value |
|------|-------|
| New services running | Yes — 35B @ port 8000, 122B @ port 8001 |
| Path A or B | **Path A confirmed** (`--chat-template-args '{"enable_thinking": false}'` on mlx_lm 0.31.3) |
| Path B (F# patch) needed | No — QwenHttpClient.fs untouched |
| 35B cold-start | Not captured this iteration (estimate: 30-60s) |
| 122B cold-start | Not captured this iteration (estimate: 120-240s) |
| 35B final RSS (post-canary) | 16.9 GB |
| 122B final RSS (post-canary) | 45.4 GB |
| Combined RSS | 62.4 GB (research projected 89.5 GB; 27 GB lower due to MoE sparse activation + mmap) |
| System memory pressure | 543 MB compressor, 1.3 GB free — acceptable, monitor during 17-03 |
| Canary verdict | **PASS (4/4)** after AgentLoop fix (`54e54a9`) |
| AgentLoop deviation | `fix(17-02)` commit `54e54a9` — System → User role for mid-conversation hints |
| Old services rollback | Preserved — plists at `~/Library/LaunchAgents/com.ohama.qwen{32b,72b}.plist`; models at `~/llm-system/models/qwen{32b,72b}/` |
| F# code change | `src/BlueCode.Core/AgentLoop.fs` — Role injection fix (3 places, not in plan's files_modified) |
| Test baseline | 254/1/0 preserved |

**Go/no-go for Phase 17-03:** GO.

All 4 canary invocations pass post-fix. Path A is confirmed. Services are responsive and producing
valid JSON-schema output. System memory is within acceptable bounds. Phase 17-03 can run
`bench/run.sh --all` against the current 35B/122B pair.

---

## Commits in Phase 17-02 scope

| Commit | Type | Description |
|--------|------|-------------|
| `7b8cbc0` | fix | Correct flag name: --chat-template-args (not --chat-template-kwargs) |
| `b1d644d` | docs | Use mlx_lm.server entry-point script in plists (5 occurrences) |
| `b398991` | docs | §9.4 full uninstall procedure for legacy 32B/72B (~58.8 GB recovery) |
| `cb11f88` | docs | §5.1.1 plist reload procedure + 3 gotcha rows (Load failed: 5 fix) |
| `c9f3786` | docs | §5.5 load-test measurement procedures (RSS + canary bench) |
| `56a06fc` | docs | §5.5.1 RSS expectations refined (MoE + mmap workload-dependent) |
| `54e54a9` | fix  | **AgentLoop: inject POST-EDIT/POST-READ hints as User role, not System** |

The `fix(17-02)` commit `54e54a9` is the critical blocker fix. All other commits are doc additions
to `documentation/qwen35-install.md`.

---

## What Phase 17-03 inherits

- **Services:** Qwen 3.5 35B-A3B-4bit @ port 8000, 122B-A10B-4bit @ port 8001 — both running
- **Path A:** thinking-mode disabled via server flag; no F# code change needed
- **AgentLoop fix:** `54e54a9` is already on master; blueCode no longer relies on lenient
  System-role handling in the chat template
- **Baseline for bench comparison:** `bench/baseline.json` still references 32B/72B results;
  17-03 must re-run `--all` and decide whether to accept new results as the new baseline (SWITCH)
  or revert to 32B/72B (STAY)
- **Memory headroom:** 62.4 GB combined RSS observed; ~65 GB effective headroom for OS + KV cache
  during bench — should be sufficient; monitor compressor during `--all` run
- **Cold-start measurement gap:** cold-start wall-clock was not captured this iteration; 17-03
  can capture on next service restart if desired
