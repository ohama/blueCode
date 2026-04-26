# Phase 17: Qwen 3.5 Evaluation - Research

**Researched:** 2026-04-27
**Domain:** Qwen 3.5 MoE models, MLX inference, Apple Silicon memory budgeting, service migration
**Confidence:** HIGH (model existence + HF repo ids verified; memory figures triangulated from 3 sources; architectural impact from direct code inspection)

---

## Summary

Qwen 3.5 35B and 122B exist as released models (as of 2026-02-24), but they are **Mixture-of-Experts architectures**, not dense models like the current 32B/72B pair. Both have MLX-quantized variants on the `mlx-community` HuggingFace organisation and are supported by `mlx_lm >= 0.25.2`. The primary planning risk is not availability but **thinking mode**: all Qwen 3.5 models emit `<think>…</think>` blocks before their JSON payload by default. This will cause every blueCode inference call to fail at the `parseLlmResponse`/`extractLlmStep` stage unless thinking is disabled at the API layer before or during Phase 17.

The 35B MoE model activates only 3B parameters per token (hence "35B-A3B"), giving MLX throughput of 90–108 tok/s through the HTTP server — well above the current 32B at comparable latency. The 122B activates 10B per token and fits in 4-bit MLX at ~70 GB. Running both simultaneously on 128 GB is feasible (22 GB + 70 GB = 92 GB of model weights, leaving ~36 GB for OS + KV cache), but is tighter than the current 18 GB + 40 GB = 58 GB pair. A second known risk is multi-turn tool-call degradation in the mlx-community 4-bit checkpoints: structured JSON output degrades after ~5 conversational rounds. The phase plan must include verification steps for both risks.

**Primary recommendation:** Use `mlx_lm.server` launched with `--chat-template-kwargs '{"enable_thinking": false}'` so thinking tokens never appear in `choices[0].message.content`. Verify this flag works before loading both services under launchd; if the flag is absent from the installed mlx_lm version, upgrade to >= 0.25.2. Treat multi-turn tool degradation as a post-swap regression test item.

---

## Model Availability Status

**Status as of 2026-04-27: CONFIRMED RELEASED**

Qwen 3.5 was released in stages:
- 2026-02-16: Qwen3.5-397B-A17B (MoE)
- 2026-02-24: Qwen3.5-122B-A10B, Qwen3.5-35B-A3B, Qwen3.5-27B

### Architecture note (critical for planning)

Both 35B and 122B are **sparse MoE (Mixture of Experts)**:

| Model | Total params | Activated per token | Experts |
|-------|-------------|---------------------|---------|
| Qwen3.5-35B-A3B | 35B | 3B | 256 experts, 8 routed + 1 shared |
| Qwen3.5-122B-A10B | 122B | 10B | same hybrid architecture |

This means compute per token is much lower than a dense 35B. Generation speed is not proportional to total parameter count.

### Canonical HuggingFace repo IDs

| Role | HF repo ID | Notes |
|------|------------|-------|
| 35B base (fp16) | `Qwen/Qwen3.5-35B-A3B` | Too large for 128 GB alone without quant |
| 35B MLX 4-bit | `mlx-community/Qwen3.5-35B-A3B-4bit` | **Recommended for port 8000** |
| 35B MLX 6-bit | `mlx-community/Qwen3.5-35B-A3B-6bit` | Higher accuracy, ~30 GB RAM |
| 35B MLX 8-bit | `mlx-community/Qwen3.5-35B-A3B-8bit` | Highest accuracy, ~40 GB RAM |
| 122B base (fp16) | `Qwen/Qwen3.5-122B-A10B` | ~234 GB, not directly runnable |
| 122B MLX 4-bit | `mlx-community/Qwen3.5-122B-A10B-4bit` | **Recommended for port 8001** |
| 122B MLX 6.5-bit | `inferencerlabs/Qwen3.5-122B-A10B-MLX-6.5bit` | ~92.5 GB; likely exceeds simultaneous budget |
| 35B base reference | `Qwen/Qwen3.5-35B-A3B-Base` | FIM/continuation only — DO NOT USE |

There is no separate "Coder" variant (unlike Qwen 2.5). The base Instruct models already include coding capability ("Qwen3.5 achieves parity with Qwen3 across reasoning, coding, agents").

### What does NOT exist (as of 2026-04-27)

- `Qwen3.5-35B` (dense) — the dense line stops at 27B
- `mlx-community/Qwen3.5-35B-A3B-Instruct-4bit` — the "-Instruct" suffix is not used; all non-Base variants are instruction-tuned
- A "no-thinking" model variant — thinking is a runtime toggle, not a separate download

---

## Memory Budget

### Per-model RAM usage (4-bit quantization, unified memory)

| Model | Disk size | RAM (weights) | KV cache (~8K ctx) | Total in-use |
|-------|-----------|---------------|--------------------|--------------|
| Qwen 2.5 32B (current) | 17 GB | 18.4 GB | ~1 GB | ~19 GB |
| Qwen 2.5 72B (current) | 38 GB | 40.4 GB | ~2 GB | ~42 GB |
| Qwen 3.5 35B-A3B 4-bit | 20.4 GB | ~19.5 GB | ~1 GB | ~21 GB |
| Qwen 3.5 122B-A10B 4-bit | 69.6 GB | ~70 GB | ~3 GB | ~73 GB |

### Simultaneous load comparison (128 GB Mac)

| Pair | Model RAM | OS + headroom | Total | Feasible? |
|------|-----------|---------------|-------|-----------|
| **Current: 32B + 72B** | 18.4 + 40.4 = **58.8 GB** | ~60 GB remaining | ~120 GB | Yes (comfortable) |
| **Candidate: 35B + 122B (4-bit)** | 19.5 + 70 = **89.5 GB** | ~38 GB remaining | ~128 GB | **Marginal — feasible but tight** |
| 35B 8-bit + 122B 4-bit | ~40 + 70 = 110 GB | ~18 GB remaining | ~128 GB | Risky, may OOM during cold start |
| 35B 4-bit + 122B 6.5-bit | ~19.5 + 92.5 = 112 GB | ~16 GB remaining | ~128 GB | Not recommended |

**Verdict:** 35B 4-bit + 122B 4-bit is feasible on 128 GB, but is ~30 GB tighter than the current pair. The safety margin requires:
- Chrome and other memory-hungry apps closed during model load
- No other large processes (Xcode, VMs, etc.) running simultaneously
- macOS compressed memory to stay active as a last resort

If OOM is observed during cold start of 122B, options are:
1. Unload 35B service before loading 122B (single-loaded-at-a-time workflow — requires changing launchd to manual activation)
2. Drop to 35B 4-bit only and keep 72B (mixed generation)
3. Use 122B 3-bit variant (~60 GB) from community quantizations

### Cold-start time estimates

Based on the current pair's observed behavior and MoE architecture:
- **35B-A3B 4-bit:** Expect 30–60 seconds (MoE loads all experts into RAM at startup; similar disk size to 32B but slightly more complex graph)
- **122B-A10B 4-bit:** Expect 120–240 seconds (disk size 4x larger than 72B; current 72B takes ~120 s; 122B will be slower)

The existing `probeModelInfoAsync` uses a 180-second HttpClient timeout. This will likely time out during 122B cold start. Phase 17 plan should include a manual wait + health-check loop before running bench.

---

## Install Procedure

### Prerequisites

Existing `~/llm-system/env/qwen-env` venv with `mlx-lm` installed. Upgrade to >= 0.25.2:

```bash
source ~/llm-system/env/qwen-env/bin/activate
pip install --upgrade mlx-lm
python3 -c "import mlx_lm; print(mlx_lm.__version__)"
# Must be >= 0.25.2 for Qwen3.5 MoE support
```

### Directory conventions

```
~/llm-system/
├── models/
│   ├── qwen32b/        # existing — keep until swap confirmed
│   ├── qwen72b/        # existing — keep until swap confirmed
│   ├── qwen35b/        # new — Qwen3.5-35B-A3B-4bit
│   └── qwen122b/       # new — Qwen3.5-122B-A10B-4bit
```

### Download commands

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

Total download: ~90 GB. With existing models on disk (~55 GB), ensure at least 150 GB free before starting.

### launchd plist templates

**35B — `~/Library/LaunchAgents/com.ohama.qwen35b.plist`**

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
        <string>--chat-template-kwargs</string>
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

**122B — `~/Library/LaunchAgents/com.ohama.qwen122b.plist`**

Copy the 35B plist and change:
- `Label` → `com.ohama.qwen122b`
- `--model` path → `/Users/ohama/llm-system/models/qwen122b`
- `--port` → `8001`
- Log paths → `122b.log`, `122b.err`

**Verify both plists before loading:**

```bash
plutil -lint ~/Library/LaunchAgents/com.ohama.qwen35b.plist   # must print "OK"
plutil -lint ~/Library/LaunchAgents/com.ohama.qwen122b.plist  # must print "OK"
```

**IMPORTANT — `--chat-template-kwargs` flag availability:** The `--chat-template-kwargs` flag was added to mlx_lm.server in a version aligned with Qwen3 support. If `mlx_lm.server --help` does not show this flag, upgrade `mlx_lm` before writing the plist. Without this flag, the fallback is to send `"chat_template_kwargs": {"enable_thinking": false}` in every POST request body from the F# adapter (requires a code change — see Architectural Impact section).

### Service swap sequence

```bash
# 1. Unload old services (do NOT kill — KeepAlive will restart; use unload)
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen32b.plist
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen72b.plist

# 2. Confirm ports released
lsof -iTCP:8000 -sTCP:LISTEN || echo "8000 released"
lsof -iTCP:8001 -sTCP:LISTEN || echo "8001 released"

# 3. Load new services
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen35b.plist
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen122b.plist

# 4. Wait for readiness (35B ~60s, 122B ~180-240s)
until curl -fsS http://127.0.0.1:8000/v1/models > /dev/null 2>&1; do sleep 3; done && echo "35B ready"
until curl -fsS http://127.0.0.1:8001/v1/models > /dev/null 2>&1; do sleep 3; done && echo "122B ready"
```

---

## Base-vs-Instruct Verification Protocol

The Qwen 2.5 32B trap (Base Coder shipped instead of Instruct) cannot happen with Qwen 3.5 in the same way because:
1. Qwen 3.5 has no separate "Coder" lineage — all models are unified (coding + general)
2. The `mlx-community/Qwen3.5-35B-A3B-4bit` repo is the instruction-tuned variant (no "-Base" suffix)

However, a NEW trap specific to Qwen 3.5 exists: **thinking mode active by default**. The symptoms are nearly identical to the Base model trap (verbose non-JSON output, `InvalidJsonOutput` errors), but the cause is different.

### Verification checklist (run after service load)

**Step 1: Confirm Instruct tokenizer (not Base)**

```bash
# Both files MUST exist for Instruct:
ls ~/llm-system/models/qwen35b/special_tokens_map.json \
   ~/llm-system/models/qwen35b/added_tokens.json

# Chat template must be present and non-empty:
python3 -c "
import json
c = json.load(open('/Users/ohama/llm-system/models/qwen35b/tokenizer_config.json'))
print('chat_template present:', 'chat_template' in c)
print('length:', len(c.get('chat_template', '')))
print('thinking_token present:', '<think>' in c.get('chat_template', ''))
"
# Expect: chat_template present: True, length > 2000, thinking_token present: True
# (Presence of <think> in chat_template is EXPECTED — it is Instruct, not Base)
```

**Step 2: Verify thinking mode is DISABLED (the new trap)**

```bash
# Test with thinking disabled (the --chat-template-kwargs flag in the plist):
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
print('PASS' if content.strip() == 'OK' else 'FAIL')
print('THINKING TRAP' if '<think>' in content else 'no think tags')
"
```

| Output | Diagnosis |
|--------|-----------|
| `content: 'OK'` / `PASS` / `no think tags` | Correct: Instruct + thinking disabled |
| `content: '<think>...'` / `THINKING TRAP` | Thinking mode still active — `--chat-template-kwargs` flag not taking effect. Check plist, upgrade mlx_lm, or patch F# adapter |
| `finish_reason: length`, content echoes system prompt | Base model loaded — wrong repo downloaded |
| `content` contains `<\|fim_prefix\|>` | Base Coder loaded — wrong repo |

**Step 3: Verify JSON schema output works**

```bash
curl -s -X POST http://127.0.0.1:8000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "/Users/ohama/llm-system/models/qwen35b",
    "messages": [
      {"role": "system", "content": "Respond ONLY with valid JSON matching this schema: {\"thought\": string, \"action\": \"final\", \"input\": {\"answer\": string}}"},
      {"role": "user", "content": "What is 2+2?"}
    ],
    "max_tokens": 200,
    "temperature": 0.0
  }' | python3 -c "
import sys, json
r = json.load(sys.stdin)
content = r['choices'][0]['message']['content']
print('raw content:', repr(content[:200]))
try:
    # Should parse cleanly without <think> prefix
    obj = json.loads(content)
    print('JSON parse: OK')
    print('action:', obj.get('action'))
except:
    print('JSON parse: FAILED — thinking tokens likely present')
"
```

---

## Known Gotchas

### Gotcha 1: Thinking mode is the default — it will break blueCode immediately

**What goes wrong:** All Qwen 3.5 models think before responding. Raw `choices[0].message.content` will start with `<think>\n...reasoning...\n</think>\n\n` before the JSON payload. The blueCode `parseLlmResponse` pipeline (`extractLlmStep` Stage 1, 2, and 3) will actually extract the JSON correctly — the brace-scan (Stage 2) will find the JSON object after `</think>` — but the `llmStepSchema` has `additionalProperties: false`, so if any thinking markers leak into the JSON object itself, it will fail as `SchemaViolation`. More critically, if the model emits `<think>...</think>` then a JSON object, the brace-scan should handle it. **However**, when `max_tokens` is tight and the thinking block is long, the final JSON may be truncated, yielding `InvalidJsonOutput`.

**Prevention:** Launch mlx_lm.server with `--chat-template-kwargs '{"enable_thinking": false}'`. This must be in the launchd plist.

**Fallback if server flag unavailable:** Modify `buildRequestBody` in `QwenHttpClient.fs` to include `chat_template_kwargs` in the POST body (requires a code change; F# anonymous record must be extended). See Architectural Impact section.

**Warning signs:** `[WRN] Session error: InvalidJsonOutput` repeatedly; `--trace` shows response starting with `<think>`.

### Gotcha 2: Multi-turn tool-call degradation in 4-bit MLX checkpoints

**What goes wrong:** After approximately 5 tool calls in a multi-turn session (a single blueCode session with several steps), the 4-bit mlx-community checkpoint of Qwen3.5-35B-A3B starts emitting structured JSON as plain-text approximations (`[Tool call: read_file({"path":"..."})]`) instead of the schema-compliant format. This was confirmed in a GitHub issue (ml-explore/mlx-lm#1011, April 2026). The 8-bit variant degrades later (~13 rounds).

**Scope:** blueCode's bench fixtures (T6 = 4 steps, W1/W2 = 3 steps) are within the safe zone. Sessions with many steps may degrade. Monitoring in Phase 17 bench should include multi-step tests.

**Prevention/mitigation:**
- Use 8-bit variant if memory allows (40 GB for 35B)
- GGUF Q4_K_XL variant appears immune but requires llama.cpp server instead of mlx_lm.server — not compatible with existing infra

### Gotcha 3: mlx_lm version must be >= 0.25.2

Qwen3 and Qwen3.5 MoE model architecture support was added in mlx_lm 0.25.2 (Qwen3/Qwen3-MoE support PR merged April 28, 2025; Qwen3.5 released February 2026 uses same architecture). Older versions will fail at model load.

```bash
source ~/llm-system/env/qwen-env/bin/activate
python3 -c "import mlx_lm; print(mlx_lm.__version__)"
# Must be >= 0.25.2
```

### Gotcha 4: mlx-community 35B model was converted with mlx-vlm, not mlx-lm

The `mlx-community/Qwen3.5-35B-A3B-4bit` model card states it was converted using `mlx-vlm version 0.3.12` (vision-language multimodal package). Despite this, it should be loadable by `mlx_lm.server` since the text-generation architecture is the same. If `mlx_lm.server` fails to load it, try `mlx_vlm.server` instead (same OpenAI-compat interface, different package).

### Gotcha 5: `content` field may be empty; `reasoning_content` is populated separately

Some OpenAI-compat server implementations (omlx, some LM Studio builds) with Qwen3.5/3.6 return an empty `content` field and put the response in a `reasoning_content` field instead. The `extractContent` function in `QwenHttpClient.fs` reads only `choices[0].message.content`. If thinking mode is truly disabled via `--chat-template-kwargs`, this issue should not arise. If it does arise, the `extractContent` function would need to fall back to `reasoning_content`.

### Gotcha 6: Recommended sampling parameters differ from Qwen 2.5

The current `Router.modelToTemperature` uses `0.2` for 32B and `0.4` for 72B. Qwen 3.5's model card recommends:

| Mode | Temperature | top_p | top_k | presence_penalty |
|------|-------------|-------|-------|------------------|
| Non-thinking, general | 0.7 | 0.8 | 20 | 1.5 |
| Non-thinking, coding | 0.7 | 0.8 | 20 | 0.0 |
| Thinking, coding | 0.6 | 0.95 | 20 | 0.0 |

The current temperature of 0.2 for coding tasks is lower than recommended. Recommend starting bench with `temperature=0.7, top_k=20` for the 35B and `temperature=0.7, top_k=20` for the 122B, adjusting if JSON output reliability is lower than with 2.5.

### Gotcha 7: `tryParseModelId` heuristic still works unchanged

The existing heuristic (`StartsWith("/")` preference) will correctly prefer the local path `/Users/ohama/llm-system/models/qwen35b` over any HF repo ID the server might advertise. No code change needed for model-id resolution.

---

## Architectural Impact on F# Code

### Component analysis

| Component | Impact | Action required |
|-----------|--------|-----------------|
| `Domain.fs` (Model DU: Qwen32B \| Qwen72B) | Names are semantic (small/large), not literal. No change needed even if 35B replaces 32B. | None — leave DU case names as-is |
| `Router.fs` (modelToTemperature) | Current 0.2/0.4 may be suboptimal for Qwen 3.5 | Adjust after bench reveals optimal values |
| `Router.fs` (modelToEndpoint, endpointToUrl) | Port mapping unchanged (35B → 8000, 122B → 8001) | None |
| `QwenHttpClient.fs` (tryParseModelId) | Local path heuristic unchanged; new paths `/Users/ohama/llm-system/models/qwen{35b,122b}` still start with `/` | None |
| `QwenHttpClient.fs` (buildRequestBody) | Missing `chat_template_kwargs` field in POST body | **Required if server-side flag unavailable** |
| `QwenHttpClient.fs` (probe8000/probe8001) | 180 s HttpClient.Timeout may fail during 122B cold start (~180–240 s) | Increase to 300 s, OR document "wait for service manually before running blueCode" |
| `Json.fs` (parseLlmResponse) | If thinking mode is disabled at server, no change. If enabled, `<think>` prefix requires stripping before extractLlmStep | None if server flag works; otherwise add think-strip stage |
| `bench/baseline.json` | Keys use `_32b`/`_72b` suffixes (T6_32b, W1_32b, etc.) | If SWITCH decision: re-key all entries as `_35b`/`_122b` |
| `CLAUDE.md` §Runtime Environment | References `qwen32b`/`qwen72b` paths | Update if SWITCH decision |
| `documentation/local-llm-services.md` | Documents 32B/72B services | Add sibling `documentation/qwen35-install.md`; do not modify existing doc |

### The `buildRequestBody` change (if needed as fallback)

If `mlx_lm.server --chat-template-kwargs` is not available:

```fsharp
// In QwenHttpClient.fs buildRequestBody, change the request anonymous record:
let req =
    {| model = modelId
       messages = msgArr
       temperature = modelToTemperature model
       max_tokens = 1024
       presence_penalty = 1.5
       stream = false
       chat_template_kwargs = {| enable_thinking = false |} |}  // ADD THIS
```

This change is **in the Cli adapter layer** (not Core) and stays within the ports-and-adapters discipline. It does not affect any Core types.

### HttpClient timeout for 122B cold start

The existing `c.Timeout <- TimeSpan.FromSeconds(180.0)` in `QwenHttpClient.fs` was set for "72B worst case (~60s) + generous margin". The 122B model may take 180–240 seconds to cold start. If `blueCode` is run before the server is ready, `probeModelInfoAsync` will silently return the fallback `ModelId = ""` (which causes a 4xx on the next POST). The recommended fix is:

1. Increase `HttpClient.Timeout` to 300 s (conservative)
2. OR document in `qwen35-install.md` that users must wait for `curl localhost:8001/v1/models` to respond before running blueCode after a cold start

Option 2 is simpler and avoids a code change. The `probeModelInfoAsync` warning log will tell the user "GET /v1/models failed" clearly.

### bench/baseline.json re-keying (SWITCH decision only)

If the SWITCH decision is made, all test entries must be re-keyed:

```json
// Before:
"T6_32b": { ... }, "T6_72b": { ... }

// After:
"T6_35b": { ... }, "T6_122b": { ... }
```

Additionally, `elapsed_median_s` values will change significantly (35B MoE is faster per token but the step counts may differ). New baselines must be measured from actual bench runs, not extrapolated.

---

## BLOCKERS

No hard blockers. Both models exist, have MLX variants, and are supported by mlx_lm >= 0.25.2.

### Soft blockers requiring early verification in Phase 17

**SB-1: `--chat-template-kwargs` flag in mlx_lm.server** (HIGH PRIORITY)

The `--chat-template-kwargs` flag in `mlx_lm.server` must be confirmed available BEFORE the launchd plist is written. If it is absent, the fallback (patching `buildRequestBody`) must be implemented before Phase 17's bench run. This is a code change and should be done in Plan 17-01 (install docs) or as a pre-requisite task.

Verification command (run before anything else in Phase 17):

```bash
source ~/llm-system/env/qwen-env/bin/activate
python3 -m mlx_lm.server --help | grep chat-template-kwargs
# Expected: "  --chat-template-kwargs ..." if available
```

**SB-2: 122B cold start within probe timeout**

The existing blueCode 180 s HttpClient timeout may be too short for 122B cold start. If blueCode is run before 122B is ready, probeModelInfoAsync returns `ModelId = ""` and the subsequent POST returns HTTP 400/422 → `LlmUnreachable`. This is not a blocker for Phase 17's manual service setup, but it is a usability issue that should be documented in `qwen35-install.md` with an explicit "wait for ready" check.

**SB-3: 4-bit multi-turn degradation**

The ml-explore/mlx-lm#1011 issue (tool-call degradation at ~5 rounds in 4-bit MLX checkpoints) is a confirmed regression risk. Phase 17 bench should include at least one multi-step test (e.g., T6 with 4 steps) as a canary. If degradation is observed, the KEEP/SWITCH decision should weigh this against throughput gains.

---

## Code Examples

### Verify mlx_lm version and Qwen3.5 MoE support

```bash
source ~/llm-system/env/qwen-env/bin/activate
pip install --upgrade mlx-lm
python3 -c "
import mlx_lm
print('version:', mlx_lm.__version__)
# Verify Qwen3MoE architecture is registered
from mlx_lm.models import MODEL_REMAPPING, MODELS  
print('Qwen3Moe in models:', 'qwen3_moe' in [k.lower() for k in MODELS.keys()])
"
```

### Manual server test with thinking explicitly disabled

```bash
# Start server manually to test flag:
source ~/llm-system/env/qwen-env/bin/activate
python3 -m mlx_lm.server \
  --model /Users/ohama/llm-system/models/qwen35b \
  --port 8000 \
  --chat-template-kwargs '{"enable_thinking": false}' \
  &

# Wait for ready:
until curl -fsS http://127.0.0.1:8000/v1/models > /dev/null 2>&1; do sleep 3; done

# Test that no <think> tags appear:
curl -s -X POST http://127.0.0.1:8000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model": "/Users/ohama/llm-system/models/qwen35b", "messages": [{"role": "user", "content": "Say OK"}], "max_tokens": 10, "temperature": 0.0}' \
  | python3 -c "import sys,json; r=json.load(sys.stdin); c=r['choices'][0]['message']['content']; print('OK' if '<think>' not in c else 'THINKING ACTIVE: '+c[:100])"

kill %1
```

### blueCode end-to-end smoke test

```bash
cd ~/projs/blueCode
dotnet run --project src/BlueCode.Cli/BlueCode.Cli.fsproj -- \
    --model 32b \
    "List the files in the src directory"
# Expect: tool steps visible, final answer printed, no InvalidJsonOutput
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Dense model sizing (32B = ~17 GB) | MoE: 35B total but only 3B activated (~20 GB, faster inference) | Qwen3.5 release Feb 2026 | Throughput ~2x vs comparable dense; less KV cache pressure |
| Separate "Coder" model for coding tasks | Unified model — all Qwen3.5 Instruct variants are coding-capable | Qwen3.5 | Eliminates Base/Coder/Instruct confusion; simpler model selection |
| Pure instruction following | Hybrid reasoning (thinking mode built in) | Qwen3 (Apr 2025) then 3.5 | Thinking ON by default; must be explicitly disabled for structured JSON agents |

**Deprecated/outdated:**

- "Coder" variant line: Qwen3.5 has no separate Coder model. Use the unified Instruct weights.
- `/think` and `/nothink` soft switches: Qwen3.5 does not support these (they were a Qwen3-specific feature). Use `chat_template_kwargs: {enable_thinking: false}` instead.
- Per-model temperature 0.2/0.4: Qwen 3.5 model card recommends 0.7 non-thinking, 0.6 thinking. The current values should be updated after bench.

---

## Open Questions

1. **Does mlx_lm.server actually pass `--chat-template-kwargs` through to the tokenizer apply_chat_template call?**
   - What we know: vLLM and llama.cpp support `--chat-template-kwargs`; mlx_lm.server documentation is marked "To be updated for Qwen3" as of April 2026.
   - What's unclear: Whether the flag is implemented in the `mlx_lm.server` CLI (as opposed to requiring a per-request body parameter).
   - Recommendation: Test at the very start of Phase 17 execution. If absent, fallback to per-request body `chat_template_kwargs` field in `buildRequestBody`.

2. **Will 122B cold start exceed the 180 s HttpClient.Timeout?**
   - What we know: Current 72B takes ~120 s; 122B disk size is ~1.8x larger; 122B activates ~10B params vs 72B's full 72B — meaning per-token inference is faster but cold loading is weight-loading-dominated.
   - What's unclear: Whether MoE architecture allows partial loading (some servers do, mlx may not).
   - Recommendation: Manual wait in Phase 17-02 procedure; document in qwen35-install.md.

3. **Does mlx_lm.server populate `choices[0].message.content` correctly when thinking is disabled, or does it use `reasoning_content`?**
   - What we know: Standard vLLM and transformer implementations put the final answer in `content` when thinking is disabled. The omlx server had a bug where `content` was empty. mlx_lm.server behavior is unconfirmed.
   - What's unclear: Whether mlx_lm.server version >= 0.25.2 correctly handles the disable-thinking path.
   - Recommendation: The Step 2 smoke test in the verification protocol will catch this immediately.

---

## Sources

### Primary (HIGH confidence)

- [Qwen3.5 HuggingFace Collection](https://huggingface.co/collections/Qwen/qwen35) — model list, repo IDs, architecture type (MoE vs dense)
- [Qwen/Qwen3.5-35B-A3B model card](https://huggingface.co/Qwen/Qwen3.5-35B-A3B) — chat template, thinking mode, recommended parameters, max context
- [Qwen/Qwen3.5-122B-A10B model card](https://huggingface.co/Qwen/Qwen3.5-122B-A10B) — quantization options, API parameters
- [mlx-community/Qwen3.5-35B-A3B-4bit](https://huggingface.co/mlx-community/Qwen3.5-35B-A3B-4bit) — disk size (20.4 GB), mlx-vlm 0.3.12 conversion
- [mlx-community/Qwen3.5-122B-A10B-4bit](https://huggingface.co/mlx-community/Qwen3.5-122B-A10B-4bit) — disk size (69.6 GB)
- Direct code inspection: `src/BlueCode.Cli/Adapters/QwenHttpClient.fs`, `src/BlueCode.Cli/Adapters/Json.fs`, `src/BlueCode.Core/Router.fs` — confirmed impact surface
- `documentation/local-llm-services.md` — existing plist patterns, service operation procedures

### Secondary (MEDIUM confidence)

- [Qwen3.5 on Apple Silicon MLX guide](https://willitrunai.com/blog/qwen-3-5-mlx-apple-silicon-guide) — memory figures (19.5 GB RAM for 35B 4-bit, 70 GB for 122B 4-bit); throughput (90–108 tok/s for 35B via HTTP server)
- [Unsloth Qwen3.5 docs](https://unsloth.ai/docs/models/qwen3.5) — 4-bit RAM: 22 GB (35B), 70 GB (122B)
- [mlx-lm PR #41 adding Qwen3/Qwen3-MoE](https://github.com/ml-explore/mlx-lm/pull/41) — merged April 28 2025; confirmed mlx_lm >= 0.25.2 required
- [vLLM Qwen3.5 usage guide](https://docs.vllm.ai/projects/recipes/en/latest/Qwen/Qwen3.5.html) — `enable_thinking: false` via `chat_template_kwargs`

### Tertiary (LOW confidence — mark for validation)

- [ml-explore/mlx-lm issue #1011](https://github.com/ml-explore/mlx-lm/issues/1011) — 4-bit multi-turn degradation; confirmed symptom, no fix yet (April 2026)
- [omlx issue #903](https://github.com/jundot/omlx/issues/903) — empty `content` field with thinking-only responses (Qwen3.6, different server)
- Memory figures are cross-referenced from willitrunai + unsloth + HF model cards; actual RSS on this specific Mac must be measured in Phase 17-02

---

## Metadata

**Confidence breakdown:**
- Model availability + HF repo IDs: HIGH — verified from official Qwen HuggingFace organisation and collection page
- Memory budget: MEDIUM-HIGH — three sources agree on 4-bit figures; simultaneous load feasibility is theoretical (must measure in Phase 17-02)
- Thinking mode gotcha: HIGH — confirmed from official model card, multiple server issue reports
- mlx_lm version requirement: MEDIUM — confirmed >= 0.25.2; exact behavior of `--chat-template-kwargs` in mlx_lm.server unconfirmed
- Multi-turn degradation: MEDIUM — confirmed in mlx-lm issue tracker for 4-bit; severity for blueCode's typical step counts (3–5) is not catastrophic
- Architectural impact (F# code): HIGH — from direct code inspection

**Research date:** 2026-04-27
**Valid until:** 2026-05-27 (30 days — mlx_lm releases frequently; model card parameters are stable)
