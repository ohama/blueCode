# Phase 18: Single-Model 122B Evaluation — Research

**Researched:** 2026-04-24
**Domain:** mlx_lm.server service management, blueCode bench harness, macOS launchd, memory profiling
**Confidence:** HIGH

---

## Summary

Phase 18 evaluates whether 122B alone can replace the 35B/122B dual-model pair. The phase is
entirely operations + data-gathering — no Core code changes, no new requirements. The primary
research questions are: (1) how to route all bench invocations to port 8001 without modifying
Core, (2) what to expect of 122B's memory profile after 35B unloads, (3) how to unload and
reload 35B cleanly, and (4) how to structure the three plans and their verification steps.

The current state: 35B on port 8000 (`com.ohama.qwen35b`), 122B on port 8001
(`com.ohama.qwen122b`). The `--model 32b` CLI alias routes to port 8000 via `Router.modelToEndpoint`
(`Qwen32B -> Port8000`). After 35B unloads, any blueCode call with `--model 32b` will hit a
connection-refused on port 8000. The bench script (`bench/run.sh`) uses `--model 32b` for about
half its invocations and `--model 72b` for the other half.

**Primary recommendation:** Create `scripts/bench-122b-only.sh` (Option A) as a self-contained
replacement script. It avoids editing `bench/run.sh` (protecting the regression gate) and avoids
Core changes (protecting purity). The script can reuse the identical `run()` helper but hardcode
`"72b"` as the model string everywhere — since `--model 72b` always routes to port 8001 (122B), a
single-model bench run is achieved with no code changes to either Core or the CLI.

---

## Standard Stack

### Core (already in place — no new dependencies)

| Tool | Purpose | Status |
|------|---------|--------|
| `launchctl unload` | Unregister 35B service from launchd | Already documented in `qwen35-install.md §5.1.1` |
| `launchctl load -w` | Re-register after bench | Same; rollback is the same as reload |
| `vm_stat` / `top -l 1` | Capture PhysMem / Compressor | Already used in Phase 17-02 measurement protocol |
| `ps -o pid,rss,command` + `pgrep` | Capture 122B RSS | Same pattern used in Phase 17 |
| `curl http://127.0.0.1:8001/v1/models` | Verify 122B still up after unload | Standard smoke |
| `lsof -iTCP:8000 -sTCP:LISTEN` | Verify port 8000 released after unload | Same |
| `bench/run.sh --gate` | Gate validation post-bench | Unchanged — reads `bench/baseline.json` |

### New script (Phase 18-02 output)

| File | Purpose | Notes |
|------|---------|-------|
| `scripts/bench-122b-only.sh` | Full bench routed to 122B only | Copies `run()` + subset modes from `bench/run.sh`; uses `"72b"` everywhere |

**No new NuGet packages. No new Python packages. No new launchd plists.**

---

## Architecture Patterns

### How CLI model routing works (confirmed from source)

```
--model 32b → parseForcedModel → Some Qwen32B → AgentConfig.ForcedModel = Some Qwen32B
--model 72b → parseForcedModel → Some Qwen72B → AgentConfig.ForcedModel = Some Qwen72B

Router.modelToEndpoint:
  Qwen32B → Port8000 → "http://127.0.0.1:8000/v1/chat/completions"
  Qwen72B → Port8001 → "http://127.0.0.1:8001/v1/chat/completions"
```

There is NO env-var override hook in the current Router. Adding one would require a Core change
(or an Adapter shim). Both are out of scope for this data-gathering phase.

### Bench script approach — Option A chosen

**Three options evaluated:**

**Option A: New `scripts/bench-122b-only.sh`**
- Copies the `run()` helper verbatim from `bench/run.sh`
- Defines the same test suites (regression, variance, diagnose, write) but replaces every
  `"32b"` model argument with `"72b"` (since `--model 72b` → port 8001 = 122B)
- New flags: `--all`, `--gate-equiv`, `--canary`, `--b2`
- Writes to `bench/runs/<ts>/` like the original
- Does NOT touch `bench/run.sh` (gate remains intact for dual-model re-verification)
- LOG_DIR naming uses `122b-only-` prefix for disambiguation

**Option B: Patch `bench/run.sh` with `--all-on-122b` mode**
- 10-line edit, easy to revert
- Problem: the `gate()` function inside `bench/run.sh` has hardcoded model strings tied to
  `bench/baseline.json` keys (`T6_32b`, `T6_122b`, etc.). Extending `run.sh` risks
  accidentally breaking the gate. The gate is load-bearing for Phase 16's bench fixtures.
- Risk: the gate function's label parsing (`labels="T6_32b T6_72b W1_32b ..."`) is coupled
  to model strings; a new mode would need careful scoping to not pollute global state.

**Option C: `BLUECODE_FORCE_PORT=8001` env var in Router**
- Requires touching `BlueCode.Core/Router.fs` (Core purity boundary)
- Core must NOT read env vars — that's an Adapter-layer concern (CLAUDE.md: "Core purity (absolute)")
- Rejected: violates architecture invariant

**Recommendation: Option A.** Cleanest separation of concerns. Leaves `bench/run.sh` untouched.
The new script is 100–120 lines, fully self-contained, easy to read and audit. Reversibility is
trivially: `git rm scripts/bench-122b-only.sh`.

### Memory snapshot pattern (from Phase 17 established protocol)

```bash
# Before unload
vm_stat | head -15
top -l 1 -s 0 -n 0 | grep -E "PhysMem|Compressor"
ps -o pid,rss,command -p $(pgrep -f qwen35b)
ps -o pid,rss,command -p $(pgrep -f qwen122b)

# Unload 35B
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen35b.plist

# Verify port 8000 released
lsof -iTCP:8000 -sTCP:LISTEN || echo "8000 free"
launchctl list | grep ohama   # should show only qwen122b

# After unload — wait ~30s for page reclaim
sleep 30
vm_stat | head -15
top -l 1 -s 0 -n 0 | grep -E "PhysMem|Compressor"
ps -o pid,rss,command -p $(pgrep -f qwen122b)
```

### Recommended project structure for Phase 18 outputs

```
scripts/
└── bench-122b-only.sh     # New; Phase 18-02

bench/runs/
└── 122b-only-<ts>/        # New run dir; Phase 18-02 output; gitignored

documentation/
└── single-model-eval.md   # New; Phase 18-03 output (≥150 lines)

.planning/phases/18-single-model-eval/
├── 18-RESEARCH.md         # This file
├── 18-01-PLAN.md
├── 18-02-PLAN.md
└── 18-03-PLAN.md
```

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Port override for bench routing | Env var hook in Core Router | `--model 72b` flag (routes to port 8001) | Core stays pure; the existing alias is the correct lever |
| Service kill for unload | `kill -9 $(pgrep qwen35b)` | `launchctl unload plist` | Kill bypasses KeepAlive graceful shutdown; KeepAlive immediately restarts the process; `unload` removes the launchd registration first |
| Memory stats | Custom `/proc`-style parsing | `vm_stat`, `top -l 1`, `ps -o rss` | macOS standard tools; same ones used in Phase 17-02 |
| Gate logic for 122B-only | Rewrite gate() | Run `bench/run.sh --gate` post-reload (dual-model gate) OR define a 122B-only gate-equiv in the new script | The existing gate is the source of truth for regression tracking |

---

## Common Pitfalls

### Pitfall 1: Killing 35B instead of unloading

**What goes wrong:** `kill -9 $(pgrep qwen35b)` or `launchctl kickstart -k` kills the process
but leaves it registered. KeepAlive restarts it within 30 seconds. Port 8000 remains in use.

**Why it happens:** KeepAlive = true in the plist. A process kill triggers an automatic restart.

**How to avoid:** Always use `launchctl unload ~/Library/LaunchAgents/com.ohama.qwen35b.plist`.
This removes the launchd registration before terminating the process, preventing auto-restart.

**Warning signs:** `launchctl list | grep ohama` still shows `qwen35b` entry after the kill.

---

### Pitfall 2: `Load failed: 5: Input/output error` on reload

**What goes wrong:** After bench completes, running `launchctl load -w com.ohama.qwen35b.plist`
fails with `Load failed: 5: Input/output error` because the service label is still registered
(KeepAlive may have re-registered it, or the unload was partial).

**Why it happens:** If the service was already partially registered (e.g., unloaded but not fully
removed from the launchd domain), `load -w` on the same label is rejected as duplicate.

**How to avoid:** Always verify with `launchctl list | grep ohama` (should show no qwen35b line)
before running `load -w`. If unsure, use the bootout/bootstrap pair:
```bash
launchctl bootout gui/$(id -u) ~/Library/LaunchAgents/com.ohama.qwen35b.plist
launchctl bootstrap gui/$(id -u) ~/Library/LaunchAgents/com.ohama.qwen35b.plist
```

**Warning signs:** The `load -w` command exits non-zero with "Input/output error".

---

### Pitfall 3: bench/run.sh `--gate` runs with port 8000 down (mid-bench accidental gate call)

**What goes wrong:** If `bench/run.sh --gate` is called while 35B is still unloaded, the gate
invokes `run "gate_T6_32b" "32b" ...` which posts to port 8000, gets connection refused, and
records exit=non-zero. The gate exits 1 (false FAIL).

**Why it happens:** The gate function hardcodes `"32b"` model for 4 of its 8 invocations.

**How to avoid:** Never call `bench/run.sh --gate` during Phase 18 while 35B is unloaded.
The `scripts/bench-122b-only.sh` script must define its own `gate_equiv()` function that uses
`"72b"` for all invocations and compares against a 122B-only equivalence of the baseline.

**Warning signs:** `gate_T6_32b.meta` shows `exit=1` and elapsed is suspiciously short (<2s).

---

### Pitfall 4: `--model 32b` connection refused — user confusion

**What goes wrong:** If the user runs blueCode manually with `--model 32b` after 35B is unloaded,
they get `LlmUnreachable: HttpRequestException` (connection refused on port 8000). The error
message does not explain that 35B is intentionally unloaded.

**How to avoid:** Document in `18-01-PLAN.md` and in the memory snapshot procedure that port 8000
is intentionally down during Phase 18. The bench script (`bench-122b-only.sh`) only uses `"72b"`,
so the bench itself is unaffected.

**Warning signs:** `LlmUnreachable` on port 8000 during Phase 18 is expected, not a bug.

---

### Pitfall 5: 122B RSS expansion after 35B unload

**What goes wrong (hypothesis):** After 35B frees ~17 GB of mmap-backed pages, the OS has more
clean pages available. If 122B's working set was previously constrained by memory pressure, its
RSS might expand to fill the freed headroom (previously evicted expert weights get paged back in).

**What actually happens (from Phase 17 data):** Phase 17 showed 122B RSS held flat at 45.4 GB
through the entire `bench/run.sh --all` run. The bench fixtures drove a stable expert subset that
never required more than 65% of the 69.6 GB disk size. This finding is unlikely to change post-35B
unload — the expert access pattern is determined by prompt content, not by peer-process memory
pressure. However, the freed 17 GB may allow macOS to stop compressing other pages, reducing
Compressor size.

**Empirical verification:** Capture RSS before and 30 seconds after unload, and again after the
full bench. If 122B RSS grows >50 GB post-bench (compared to 45.4 GB in Phase 17), it indicates
that 35B's memory pressure was suppressing 122B's expert activation.

**Expected outcome:** RSS stays near 45.4 GB. PhysMem unused grows from ~1.6 GB to ~18–19 GB
(the freed 35B pages, minus OS reclaim). Compressor may shrink from ~541 MB toward 0.

---

### Pitfall 6: `--all` bench takes ~25 min and 122B is the bottleneck for every invocation

**What goes wrong:** In Phase 17, 35B invocations were 2–9s and 122B were 3–15s. In a 122B-only
bench, all 30+ invocations go through 122B. Estimated total time: ~45–60 min (vs Phase 17's
~25 min at `--all`). The user must not start another workload during this window.

**How to avoid:** Communicate the longer expected run time in Plan 18-02. The bench script should
print estimated time at startup.

**Warning signs:** If any invocation exceeds 180s, it will hit the blueCode HTTP timeout and exit
non-zero, causing a false bench failure. 122B worst-case observed in Phase 17 was 15s (T7_72b).
Single-model should be similar since there's no memory competition. 180s timeout is safe.

---

### Pitfall 7: 35B reload after bench — port 8000 still in use

**What goes wrong:** If a previous `launchctl unload` was immediately followed by `load -w` for
some reason (e.g., accidental), or if port 8000 was grabbed by another process, the reload fails.

**How to avoid:** Before reload:
```bash
lsof -iTCP:8000 -sTCP:LISTEN || echo "8000 free"
launchctl list | grep ohama   # should show only qwen122b
```
If 8000 is in use by something unexpected, investigate before loading 35B plist.

---

## Code Examples

### bench-122b-only.sh skeleton (Option A implementation)

```bash
#!/bin/bash
# scripts/bench-122b-only.sh — 122B-only bench (Phase 18)
# Routes all invocations to --model 72b (port 8001 = 122B).
# 35B must be unloaded before running this script.
# Usage: scripts/bench-122b-only.sh [--all | --canary | --b2 | --gate-equiv]

set -u
trap 'git checkout -- bench/fixtures/bug_lastchar.fs bench/fixtures/bug_average.fs 2>/dev/null || true' EXIT
cd /Users/ohama/projs/blueCode

MODEL="72b"   # All invocations go to port 8001 (122B only)
LOG_DIR="bench/runs/122b-only-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$LOG_DIR"

run() {
  local label="$1"
  local model="$2"
  local prompt="$3"
  local out="$LOG_DIR/${label}.log"
  local meta="$LOG_DIR/${label}.meta"
  local start_ts=$(date +%s)
  echo "===== $label (model=$model) =====" | tee -a "$LOG_DIR/timeline.txt"
  echo "PROMPT: $prompt" >> "$out"
  echo "----" >> "$out"
  /usr/bin/time -p dotnet run --project src/BlueCode.Cli -- --verbose --model "$model" "$prompt" >> "$out" 2>&1
  local exit_code=$?
  local end_ts=$(date +%s)
  local elapsed=$((end_ts - start_ts))
  echo "label=$label model=$model exit=$exit_code elapsed=${elapsed}s" > "$meta"
  echo "  -> exit=$exit_code elapsed=${elapsed}s" | tee -a "$LOG_DIR/timeline.txt"
}

regression_122b() {
  # T1–T7 routed entirely to 122B (model=72b → port 8001)
  for test_label in T1 T2 T3 T4 T5 T6 T7; do
    # ... test prompts matching bench/run.sh regression() ...
    run "regression_${test_label}_122b" "$MODEL" "<prompt>"
  done
}
# ... variance_122b, diagnose_122b, write_122b, all_mode_122b ...
```

The key invariant: every `run()` call uses `"$MODEL"` (`"72b"`), never `"32b"`.

### Unload + verify snippet (18-01 canonical commands)

```bash
# Step 1: before-unload snapshot
top -l 1 -s 0 -n 0 | grep -E "PhysMem|Compressor" | tee "$SNAP_FILE"
ps -o pid,rss -p $(pgrep -f qwen35b) 2>/dev/null | tee -a "$SNAP_FILE"
ps -o pid,rss -p $(pgrep -f qwen122b) 2>/dev/null | tee -a "$SNAP_FILE"

# Step 2: unload 35B
launchctl unload ~/Library/LaunchAgents/com.ohama.qwen35b.plist

# Step 3: verify port 8000 released
lsof -iTCP:8000 -sTCP:LISTEN || echo "8000 free"
launchctl list | grep ohama   # must show only com.ohama.qwen122b

# Step 4: verify 122B still responsive
curl -fsS http://127.0.0.1:8001/v1/models > /dev/null && echo "122B OK"

# Step 5: after-unload snapshot (wait 30s for page reclaim)
sleep 30
top -l 1 -s 0 -n 0 | grep -E "PhysMem|Compressor" | tee -a "$SNAP_FILE"
ps -o pid,rss -p $(pgrep -f qwen122b) 2>/dev/null | tee -a "$SNAP_FILE"
```

### Reload 35B after bench (KEEP-DUAL / CONDITIONAL path)

```bash
launchctl load -w ~/Library/LaunchAgents/com.ohama.qwen35b.plist
until curl -fsS http://127.0.0.1:8000/v1/models > /dev/null 2>&1; do sleep 3; done
echo "35B reloaded"
```

### B2 diagnosis verification command (per-invocation)

```bash
# After bench run, verify B2_122b log contains correct diagnosis
grep -i "dividebyzero\|divide by zero\|division by zero\|DivideByZero" \
  "$LOG_DIR/b2_122b.log" && echo "B2 PASS" || echo "B2 FAIL"
```

### Step count extraction (same as bench/run.sh gate logic)

```bash
actual_steps=$(grep -E "\[INF\] Session (ok|error)" "$logfile" 2>/dev/null \
  | grep -o "[0-9]* steps" | grep -o "[0-9]*" | head -1)
actual_steps=${actual_steps:-0}
```

---

## State of the Art

| Phase 17 finding | Phase 18 implication |
|-----------------|---------------------|
| 122B RSS held flat at 45.4 GB through bench-all | Expect similar flat profile; 35B unload frees ~17 GB RSS but 122B does not expand into it (expert access pattern is prompt-driven, not memory-availability-driven) |
| 122B T1=4s, T2=3s, T5=6s, T6=11s, T7=15s in dual-loaded state | Phase 18 baseline hypothesis: similar or slightly faster (no mmap competition); decision criterion is T1/T2 median ≤ 6s |
| W1/W2 = 3 steps on 122B in dual-loaded state | Expect same step count; loop-injection mechanism is model-agnostic |
| B2 diagnosis accurate on 122B in dual-loaded state | Expect same accuracy; the B2 fixture is a short 2-step prompt well within safe zone |
| Combined RSS = 62.4 GB; PhysMem unused = 1.6 GB | After 35B unload: RSS drops to ~45.4 GB; PhysMem unused rises to ~18–19 GB; Compressor likely shrinks |

**Deprecated approach:**
- Do not use `BLUECODE_FORCE_PORT` env var in Router — violates Core purity. The `--model 72b`
  alias is the correct lever.
- Do not `kill -9` the 35B process — use `launchctl unload`.

---

## Plan Decomposition

The ROADMAP's 3-plan structure is confirmed correct and is the right decomposition:

### Plan 18-01: Service unload + memory profile (CHECKPOINT — `autonomous: false`)

**Why a checkpoint:** Requires user to physically run launchctl commands and verify service state.
The memory snapshot must be captured immediately after unload (before pages are reclaimed by OS
or other processes). Claude cannot run `launchctl` autonomously on this system.

**Files touched:**
- `.planning/phases/18-single-model-eval/18-01-MEMORY-SNAPSHOT.md` (new — captures before/after numbers)
- `.planning/phases/18-single-model-eval/18-01-PLAN.md` (the plan)
- `.planning/phases/18-single-model-eval/18-01-SUMMARY.md` (post-execution)

**Verification commands:**
```bash
launchctl list | grep ohama           # only qwen122b visible
lsof -iTCP:8000 -sTCP:LISTEN || true  # 8000 free
curl -fsS http://127.0.0.1:8001/v1/models > /dev/null && echo "122B responsive"
top -l 1 -s 0 -n 0 | grep -E "PhysMem|Compressor"
```

---

### Plan 18-02: 122B-only bench

**Why separate from 18-01:** Bench takes ~45–60 min; separating it from the service unload
(a user-gated step) allows clean progress tracking and a clear rollback point.

**Files touched:**
- `scripts/bench-122b-only.sh` (new — the bench script)
- `bench/runs/122b-only-<ts>/` (new run dir; gitignored)

**Verification:** Every invocation in `bench/runs/122b-only-<ts>/timeline.txt` must show
`exit=0`. Any non-zero exit is a blocker for 18-03.

**Step count + latency capture per test label:**
```bash
for f in bench/runs/122b-only-*/; do
  for meta in "$f"*.meta; do
    cat "$meta"
  done
done
```

**B2 verification:**
```bash
grep -i "dividebyzero\|divide by zero\|division by zero" \
  bench/runs/122b-only-*/b2_122b.log && echo "B2 diagnosis PASS"
```

**Step count regression check (vs baseline.json 122B entries):**
```bash
# T6_122b baseline: step_count_max=5; actual must be ≤5
# W1/W2 baseline (35B): step_count_max=3; 122B expected same
# B2_122b baseline: step_count_max=3
```

---

### Plan 18-03: Documentation + decision

**Why separate from 18-02:** The decision document (`single-model-eval.md`) requires interpreting
the bench numbers against the decision criteria. Separating this from the raw bench run allows
the user to review raw data before the decision is committed to documentation.

**Files touched:**
- `documentation/single-model-eval.md` (new — ≥150 lines)
- CLAUDE.md `## Runtime Environment` update (if DROP-35B or CONDITIONAL)
- `bench/baseline.json` — NOT modified in this phase (baseline changes only in follow-up)

**The doc must contain:**
1. Decision criteria table with pass/fail per criterion
2. Per-test comparison table (122B-only vs Phase 17 dual-loaded)
3. Memory before/after table (from 18-01 snapshot)
4. Named verdict: DROP-35B / KEEP-DUAL / CONDITIONAL
5. If DROP-35B: enumeration of follow-up architectural changes (not executed here)
6. If KEEP-DUAL: 35B reload confirmation
7. If CONDITIONAL: opt-in mechanism sketch

---

## Decision Criteria (pre-specced, for 18-03 consumption)

| Criterion | Threshold | Source | How to measure |
|-----------|-----------|--------|----------------|
| T1/T2 simple task latency | Median ≤ 6s | ROADMAP §SC4 | `elapsed_s` from `.meta` files; take median of 3 variance runs |
| T6 step count | ≤ 5 (no regression from 4) | baseline.json T6_122b.step_count_max | `grep -o "[0-9]* steps"` from log |
| W1/W2 step count | = 3 (loop-injection holds) | baseline.json W1_35b.step_count_max | Same |
| B2 diagnosis | "empty list causes DivideByZeroException" (or semantically equivalent) | baseline.json B2_122b.actual_diagnosis | Manual review of b2_122b log final answer |
| PhysMem unused | ≥ 5 GB after unload | ROADMAP §SC4 | `top -l 1` PhysMem unused field |
| Compressor | < 1 GB | ROADMAP §SC4 | `top -l 1` Compressor field |
| All bench exits | = 0 | Implicit correctness | `grep exit= bench/runs/122b-only-*/timeline.txt` |

---

## Reversibility Checklist

Regardless of verdict, the system must be left in a clean state at end of phase:

**If KEEP-DUAL verdict:**
- [ ] 35B reloaded: `launchctl load -w com.ohama.qwen35b.plist`
- [ ] Port 8000 responsive: `curl localhost:8000/v1/models`
- [ ] `launchctl list | grep ohama` shows both entries
- [ ] `bench/run.sh --gate` exits 0 (dual-model gate)
- [ ] No code changes to any .fs file
- [ ] No changes to `bench/baseline.json`

**If DROP-35B verdict (end of phase — code changes deferred):**
- [ ] `documentation/single-model-eval.md` written with verdict
- [ ] 35B stays unloaded (intentional)
- [ ] CLAUDE.md `## Runtime Environment` updated to reflect 35B unloaded
- [ ] No Core/Router changes in this phase (those are follow-up)
- [ ] `bench/baseline.json` NOT modified (follow-up phase)

**If CONDITIONAL verdict:**
- [ ] 35B reloaded (until opt-in mechanism exists, default is dual)
- [ ] `documentation/single-model-eval.md` written with CONDITIONAL verdict and sketched opt-in

---

## What Happens If a Plan Fails Mid-Way

### 18-01 fails (unload doesn't release port 8000)

Use bootout/bootstrap pair (documented in `qwen35-install.md §5.1.1`):
```bash
launchctl bootout gui/$(id -u) ~/Library/LaunchAgents/com.ohama.qwen35b.plist
```
If port 8000 still in use after bootout, run `lsof -iTCP:8000` to identify the process.

### 18-02 fails (bench exits non-zero for some invocations)

- Check `bench/runs/122b-only-<ts>/timeline.txt` for which labels failed
- If `LlmUnreachable` on port 8001: 122B may have crashed; check logs at
  `~/llm-system/services/logs/122b.err`; restart with `launchctl kickstart -k gui/$(id -u)/com.ohama.qwen122b`
- If `InvalidJsonOutput`: run §5.3 thinking-mode smoke test; Path A must be active
- Re-run failed test labels individually by calling the script with a targeted mode
- Do NOT run `bench/run.sh --gate` until 35B is reloaded (would false-fail)

### 18-03 fails (insufficient data for a named verdict)

If bench data is ambiguous (e.g., T1/T2 latencies are borderline 5–7s), document as CONDITIONAL
with the observation noted. The decision doc MUST name a verdict; "unclear" is not a valid outcome.
Use the pre-specced thresholds to force a mechanical decision.

---

## Open Questions

1. **Exact 122B RSS after 35B unload:** Unknown until empirically measured. Hypothesis: stays near
   45.4 GB (Phase 17 post-bench value). Verification: `ps -o rss` after unload + 30s wait.

2. **122B latency under single-model:** Phase 17 measured T1=4s, T2=3s, T5=6s, T6=11–12s on
   dual-loaded system. Single-model may be marginally faster (no mmap competition) or identical.
   Critical threshold is T1/T2 median ≤ 6s (per ROADMAP). Phase 17 data already shows T1=4s
   and T2=3s on 122B — both well under threshold. Low risk of failing latency criterion.

3. **bench-122b-only.sh `gate_equiv()` design:** The ROADMAP SC3 requires "30+ invocations".
   The `--all` mode equivalent (regression × 7 + variance × 4 + diagnose × 2 + write × 2 = 15
   labeled labels × 1 model = 15 invocations for basic suite; variance adds 6 more = ~21
   invocations). Adding a second variance pass brings it to 27. Suggest: 3× variance on T1 +
   3× variance on T6 = 6 additional, for a total of 27 labeled invocations. Acceptable.
   ROADMAP says "30+ invocations" — consider adding T7_122b × 3 to push over 30.

4. **B2 step-count on 122B-only:** Phase 17 showed 2 steps (read_file + final). Expected same
   in single-model. If it goes to 3 steps (extra read), that's within step_count_max=3 and not
   a regression.

---

## Sources

### Primary (HIGH confidence)

- `bench/run.sh` — complete harness structure, model alias usage, gate logic
- `src/BlueCode.Core/Router.fs` — `modelToEndpoint` routing
- `src/BlueCode.Cli/CompositionRoot.fs` — `parseForcedModel` ("32b" → Qwen32B → Port8000)
- `src/BlueCode.Cli/Adapters/QwenHttpClient.fs` — port-to-URL composition, Lazy probe
- `documentation/qwen35-install.md §5.1.1` — launchctl unload/reload procedure
- `documentation/benchmark-qwen35-eval.md` — Phase 17 per-test numbers (authoritative baseline)
- `bench/baseline.json` — current gate thresholds (T6_35b, T6_122b, W1_35b, W2_35b, T1_35b, T5_122b, B2_35b, B2_122b)
- `.planning/ROADMAP.md §Phase 18` — success criteria and plan outline

### Secondary (MEDIUM confidence)

- `documentation/qwen35-install.md §5.5.1` — RSS expansion hypothesis (MoE + mmap behavior),
  cross-referenced with Phase 17-03 empirical finding that RSS stayed flat through bench-all

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all tools already used in Phase 17; no new dependencies
- Architecture (bench script approach): HIGH — sourced directly from Router.fs and bench/run.sh
- launchctl unload/reload: HIGH — documented in qwen35-install.md §5.1.1 with known gotchas
- Memory expansion hypothesis: MEDIUM — extrapolated from Phase 17 flat-RSS empirical finding;
  must be verified empirically in 18-01
- Latency projection: HIGH — Phase 17 measured T1=4s, T2=3s on 122B; both under 6s threshold
- Decision criteria: HIGH — pre-specced in ROADMAP §SC4; not subject to interpretation

**Research date:** 2026-04-24
**Valid until:** 2026-05-24 (mlx_lm, macOS, and blueCode bench structure are stable at this scale)
