# Project Milestones: blueCode

## v1.1 Refinement (Shipped: 2026-04-24)

**Delivered:** Three pieces of v1.0 technical debt cleaned up — portable model id resolution, lazy bootstrap probe, and real LLM thought capture in `--verbose` output. Mid-milestone gap closure (06-03) fixed a live regression where mlx_lm.server's HF Hub fallback overwrote the Instruct tokenizer with Base Coder weights.

**Phases completed:** 6-7 (5 plans total — 3 in Phase 6 including 06-03 gap closure, 2 in Phase 7)

**Key accomplishments:**

- `Router.modelToName` absolute-path hardcode removed; `QwenHttpClient` adapter now resolves model id at runtime via `ModelInfo` record + `probeModelInfoAsync` + per-port `Lazy<Task<ModelInfo>>` cache, preferring local paths over HF ids to prevent tokenizer swap (REF-01 + 06-03 gap closure)
- `bootstrapAsync` deleted; `Program.fs` calls sync `bootstrap` only; zero HTTP activity at startup, probe fires lazily on first LLM call per port (REF-02)
- New Core record `LlmResponse = { Thought; Output }` carries LLM reasoning through `ILlmClient.CompleteAsync` into `Step.Thought`; `--verbose` now shows real LLM text instead of `"[not captured in v1]"` placeholder (OBS-05)
- Live end-to-end verified: `blueCode --verbose --model 32b "Say OK in 3 words"` → 8s, `thought: "The user wants a simple phrase."`, `final: OK`, exit 0, no HF fetch in server log

**Stats:**

- 2 phases, 5 plans, 218 tests passing (208 v1.0 baseline + 10 v1.1 additions; 1 env-gated smoke still ignored)
- 12 F# files changed, +315 / -124 LOC
- 23 git commits
- ~19 hours from v1.1 start to v1.1 tag (2026-04-23 17:32 → 2026-04-24 12:21)

**Git range:** `a03ff8e` (milestone start) → `4b61819` (Phase 6+7 complete, pre-archive)

**Dual-service stress test:** 3 back-to-back `--model 32b` runs with both 32B + 72B services loaded @ 126GB memory utilization — each run 2s, no OOM, prompt cache grew 0.70 → 0.83 GB (far below the 1.51 GB crash threshold observed pre-06-03 when Base-mode generation was running 1024-token continuations).

**What's next:**

v1.2 candidates (not scoped yet): per-port `MaxModelLen` visibility, streaming output, `edit_file`/`glob_search`/`grep_search` tools, session persistence, `makeMockResponse` shared test helper, multi-platform `tryParseModelId` path detection.

**Audit note:** v1.1 shipped without a formal `/gsd:audit-milestone` pass (same pattern as v1.0). Live verification on 2026-04-24 through real-Qwen `--verbose` run + dual-service stress test served as the practical validation gate.

---

## v1.0 MVP (Shipped: 2026-04-23)

**Delivered:** A strong-typed F# agent loop that drives locally-served Qwen 32B/72B on Mac, replacing the Python `claw-code-agent` as the daily driver.

**Phases completed:** 1-5 (17 plans total — 16 autonomous + 1 human-gated UAT)

**Key accomplishments:**

- 5-loop, one-tool-per-step agent with strict `{thought, action, input}` JSON contract and typed `AgentError`/`ToolResult` DUs that force compile-time exhaustive matching
- Qwen chat completions adapter with 3-stage JSON extraction (bare → brace-nest → fence-strip), runtime schema validation, full error mapping to `AgentError` (no exception leakage), and Spectre spinner
- 4-tool executor (`read_file`, `write_file`, `list_dir`, `run_shell`) with 22-validator bash security chain ported from the Python reference implementation — SecurityDenied gate verified to run before `Process.Start`
- Argu-based CLI with `--model 32b|72b` forced routing, `--verbose` multi-line / `--trace` Serilog Debug JSON mode, multi-turn REPL with `/exit` + Ctrl+D, Ctrl+C cancels-current-turn semantics, and JSONL session log at `~/.bluecode/session_<ts>.jsonl`
- `/v1/models` startup probe with 80%-of-max_model_len intra-turn warning and `bootstrapAsync` pattern alongside sync `bootstrap` for network-free testing
- Daily-driver cutover complete: `claw-code-agent/` retired → UAT real coding task passed end-to-end against live Qwen (72B Instruct, 2 steps, exit 0)

**Stats:**

- 5 phases, 17 plans (16 autonomous + 1 human-gated UAT), 208 tests passing (1 env-gated smoke ignored)
- 5,891 LOC F# across `src/` and `tests/`
- 85 git commits
- ~27 hours from first commit to v1.0 tag (2026-04-22 14:37 → 2026-04-23 17:18)

**Git range:** `c692774` (initial research) → `06f3bd5` (v1.1 todo #1 closed post-milestone)

**What's next:**

v1.1 — OBS-03 dynamic `/v1/models` model id query (replace `Router.modelToName` absolute-path hardcode), decouple 32B cold-start probe from bootstrap, and evaluate `--verbose` thought capture via adjusted `ILlmClient.CompleteAsync` signature.

**Audit note:** v1.0 shipped without a formal `/gsd:audit-milestone` pass. UAT through 05-04 (pre-flight 208-test suite, Fantomas clean, real-task run against live Qwen) served as the practical validation gate. Formal audit practice should start with v1.1.

---
