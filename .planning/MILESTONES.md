# Project Milestones: blueCode

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
