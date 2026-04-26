# Project Milestones: blueCode

## v1.4 Test Hygiene + Bench Polish (Shipped: 2026-04-26)

**Delivered:** Two pieces of cited tech debt cleared (3-milestone-old shared `makeMockResponse` helper + bench fixture working-tree drift surfaced in v1.3 Part 4), then opens a 2-week observation window for v1.5 scoping. Path B from the v1.3 close — discipline preserved, small wins shipped, observation-driven scoping (not deferred-list draining).

**Phases completed:** 12 - 13 (2 plans total — one per phase)

**Key accomplishments:**

- **TST-01 closed (Phase 12):** Consolidated `makeMockResponse` from 2 in-repo definitions (one each in `AgentLoopTests.fs` and `ReplTests.fs`) into shared `tests/BlueCode.Tests/MockHelpers.fs`. Both consumers updated with `open BlueCode.Tests.MockHelpers`; 15 call sites continue to work without source change. Test count 243/1/0 preserved (zero behavioral change). Single combined `refactor` commit for 4 mechanically-coupled file edits. Other duplicated helpers (`toolCall`, `mockLlm`/`stubLlm`, `mockToolsOk`/`stubToolsOk`, `discardStep`) deliberately NOT factored — TST-01 scope discipline closed 3 prior milestones' worth of scope-creep deferral. Discovered REQUIREMENTS.md spec count discrepancy: actual was 2 definitions, not 3 (the spec conflated definition sites with use sites).
- **BENCH-06 closed (Phase 13):** Added EXIT trap to `bench/run.sh` line 18 (between `set -u` and `cd`). Trap body: `git checkout -- bench/fixtures/bug_lastchar.fs bench/fixtures/bug_average.fs 2>/dev/null || true`. Single-quoted, no `exit N`, exit-code preserving, bash 3.2 compatible. Targets W1/W2 write fixtures only — `bug_divide_zero.fs` (B2 read-only diagnose fixture) deliberately excluded. Defense-in-depth preserved: existing in-line heredoc-restore blocks at lines 137-153 and 278-296 (post-shift) handle between-invocation reset; trap handles exit-time cleanup. SC1 verified empirically with full `--gate` run (8/8 PASS, fixtures clean post-trap). New `## Auto-Reset of Write Fixtures` subsection in `documentation/bench.md` (lines 60-79).
- **Patterns established for future use:** "Test helper modules: no testList, no `RouterTests.fs:rootTests` entry; only fsproj `<Compile Include>` registration needed." "EXIT trap pattern: `trap 'git checkout -- <files> 2>/dev/null || true' EXIT` — reliable, bash 3.2 compatible, exit-code preserving."
- **Audit confirmed clean (status: passed):** No critical gaps, no cross-phase wiring issues (Phase 12 = `tests/`, Phase 13 = `bench/` + `documentation/` — fully independent), no E2E flow changes, zero `src/` diff. 1 pre-existing low-severity tech debt item deferred unchanged (other duplicated test helpers).

**Stats:**

- 2 phases (12, 13), 2 plans, 2 requirements (100% closure)
- Source code delta: zero `src/` change. `tests/`: 4 files changed (1 new `MockHelpers.fs`, 3 modified). `bench/run.sh`: +4 lines. `documentation/bench.md`: +21 lines. Total: 6 files, +37/-16 LOC.
- 243/1/0 tests passing; 1 env-gated smoke ignored (unchanged from v1.3)
- Bench gate 8/8 PASS throughout
- 7 commits in milestone range (v1.4 setup + execution + closes)
- ~1 day wall-clock (2026-04-26 milestone start → 2026-04-26 Phase 13 verified)

**Git range:** `48d90c9` (v1.4 milestone start) → `bd73a47` (Phase 13 complete)

**What's next:**

**2-week observation window opens 2026-04-26 (load-bearing exit criterion).** Daily-drive blueCode as primary Mac coding agent; use `/gsd:add-todo` from real coding sessions to capture pain signals. v1.5 scope is built from those todos — NOT from the deferred-list backlog. The bench gate now catches structural regressions automatically, so observation has zero structural cost. STM-01 (streaming) deferred 6th cycle; defer-pattern is itself the load-bearing signal.

---

## v1.3 Bench-Driven Quality Gates (Shipped: 2026-04-26)

**Delivered:** v1.2's behavioral wins formalized as a repo-tracked regression suite (`bench/run.sh --gate`), then a 54% system-prompt shrink (1689 → 783 chars) validated by that suite — recovering correct B2 divide-by-zero diagnosis on both 32B and 72B and empirically confirming the v1.2 audit's prompt-length attention-shift hypothesis.

**Phases completed:** 10 - 11 (6 plans total — 3 in Phase 10 + 3 in Phase 11)

**Key accomplishments:**

- **Bench harness in repo with gate mode**: moved `/tmp/bench-v1.2/run.sh` to repo-tracked `bench/run.sh` with mode-flag dispatcher (`--gate`, `--regression`, `--canary`, `--all`, `--b2`, `--help`); `bench/baseline.json` records 8-test baseline; `bench/fixtures/` versions the bug fixtures (added new `bug_divide_zero.fs` for B2). `--gate` mode runs 8 invocations in ~115s, jq-based JSON diff vs baseline, exits non-zero on regression. SC1 verified positive (current binary → exit 0) AND negative (tightened baseline → exit 1, restored → exit 0 round-trip clean).
- **System prompt shrink to 783 chars** (54% reduction from 1689) via 13-cycle iterative protocol: edit → `bench/run.sh --gate` → bisect on FAIL → repeat. Path C target (≤800) achieved without invoking Path D escape hatch. Source code delta confined to `src/BlueCode.Cli/CompositionRoot.fs` for the prompt itself plus 2 Rule 3 auto-fixes in `Adapters/FsToolExecutor.fs` (`edit_file` empty-`old_string` infinite-loop guard + `grep_search` file-path support).
- **Post-`read_file` injection extending 09.1-05's loop-injection primitive**: `lastReadHint: (string * string) option` parameter threaded through `runLoop`; on successful `read_file`, `buildMessages` appends a System-role `[POST-READ HINT]` message at END of message list (post-user position) when the header status is `truncated` or `out-of-range`. Two-arm hint logic with kind-specific corrective actions. New `testCaseAsync` in existing `AgentLoopTests.fs` (242 → 243 tests). Domain.fs untouched (parameter discipline mirrors `lastEditPath`).
- **B2 recovery on both 32B and 72B**: `bench/baseline.json` B2 entries flipped from `pass: false, regression: true` to `pass: true`. Verbatim model thoughts (32B): *"The bug is identified. It occurs when the function `average` is called with an empty list, leading to a division by zero error."* (72B): *"The bug is triggered when the function `average` is called with an empty list. This causes a division by zero because `List.length xs` returns 0 for an empty list."* The audit's prompt-length attention-shift hypothesis is confirmed: 54% prompt reduction was sufficient on its own; no surgical B2 hint needed.
- **5 new howtos capturing v1.2 + v1.3 reusable lessons** in `documentation/howto/` — post-user-injection pattern, bench-driven prompt iteration, agent fixture design, header-substring collision pitfall, jq-based regression gate. Each howto passed the 4-criteria quality gate (Non-Googleable + Hard-Won + Actionable + Reusable).
- **T6 step count deterministic at 3** for both 32B and 72B (was 4-5 with occasional MaxLoops at v1.2 close); the new `grep_search → read_file → final` pattern is reliable post-shrink.

**Stats:**

- 2 phases (10, 11), 6 plans, 243 tests passing (242 v1.2 baseline + 1 from 11-01); 1 env-gated smoke ignored
- Source code delta: `src/BlueCode.Core/AgentLoop.fs` (+lastReadHint threading + injection), `src/BlueCode.Cli/{CompositionRoot.fs (prompt shrink), Adapters/FsToolExecutor.fs (2 Rule 3 fixes)}`, `tests/BlueCode.Tests/AgentLoopTests.fs` (1 new testCase)
- New infrastructure: `bench/` directory tree (run.sh + 3 fixtures + baseline.json), `documentation/bench.md` (190 lines), 5 new howtos
- 25 commits since v1.2 close
- ~1 day wall-clock (2026-04-26 milestone start → 2026-04-26 Phase 11 verified)

**Git range:** `6133abc` (v1.2 close) → `13a875d` (howto additions)

**What's next:**

v1.4 candidate: "Test Hygiene + Bench Polish" — small tactical milestone (~3 days, 2 phases) for TST-01 (shared `makeMockResponse` helper, 3-milestone-old debt) + bench fixture cleanup automation (gitignore drift after `--gate` runs). Explicit exit criterion: 2-week observation window using `/gsd:add-todo` to capture real-use pain signals from daily-driver use, scoping v1.5 from that signal pool rather than the existing deferred-list backlog. Defer STM-01 (streaming) one more cycle — five deferrals is a strong signal that observation, not feature work, is the right next move.

**Audit note:** v1.3 did NOT run `/gsd:audit-milestone` (Phase 10 + Phase 11 verifiers both passed independently with 5/5 and 4/4 must-haves; the bench gate's deterministic verdict logic provides cross-phase E2E validation by construction). This is the same shipping discipline as v1.0 and v1.1 (both shipped without formal audits, justified by other validation gates). The bench gate is now the project's primary regression-detection mechanism.

---

## v1.2 Tool Expansion (Shipped: 2026-04-26)

**Delivered:** Three new agent tools (`edit_file`, `glob_search`, `grep_search`) plus a `read_file` metadata header (TOOL-08), with a post-audit Phase 9.1 that hardened TOOL-08's actual behavioral effectiveness and shipped a reusable code-level loop-injection mechanism for tool-terminality enforcement (`[POST-EDIT CONSTRAINT]` System-role message, post-user-prompt position).

**Phases completed:** 8 - 9 + 9.1 (decimal insertion) — 8 plans total (3 original + 5 in inserted Phase 9.1 across two gap-closure rounds)

**Key accomplishments:**

- Three new tools across the full stack (Tool DU + 8-value schema enum + executor + system prompt + 18 unit tests in `ToolExpansionTests.fs`) — `edit_file` exact-string surgical edit with 0/1/N match handling, `glob_search` project-rooted pattern finder, `grep_search` regex content search with ReDoS guard
- `read_file` structured metadata header `[file: <path>, lines X-Y of Z, not-truncated|truncated|out-of-range]` prepended to every Success payload, with raw-`endLine` preservation on out-of-range to give the LLM an unambiguous bounds-violation signal
- Phase 9.1 closed three audit-discovered effectiveness gaps via a multi-layered fix: dispatcher default-window for partial line bounds (`Some s, None → Some(s, s+99)`), system-prompt `truncated` strategy hint (restored 72B's narrow-window heuristic), directive `edit_file` terminal wording, and — when wording-only intervention proved insufficient against user-prompt explicit tool naming (W1's "using write_file") — a code-level `[POST-EDIT CONSTRAINT]` System-role message threaded through `runLoop` via `lastEditPath: string option` and appended at the END of `buildMessages`, leveraging conversation-history position-as-authority to override prior user instructions
- Bench evidence: T6 × 32B 0/3 FAIL → 3/3 PASS; T6 × 72B 0/3 FAIL → 3/3 PASS (exceeded ≥2/3 prediction); W1 × 32B 4 → 3 steps PASS; W2 × 32B 4 → 3 steps PASS; T1/T5 canaries unchanged. Smoking-gun thought diff on W1 (post-09.1-05): `"Now, I will save the corrected version"` → `"The bug has been fixed and the change is confirmed. No further action is needed on this file."`
- New architectural primitive — the loop-injection mechanism is reusable for any future tool-terminality concern (post-`read_file`-truncated hint, post-`write_file` redundancy guard, etc.); could let v1.3's PERF-01 prompt-shrink work move contextual hints out of the base system prompt and into post-tool-result injections

**Stats:**

- 3 phases (8, 9, 9.1), 8 plans, 242 tests passing (1 env-gated smoke ignored)
- Source code delta confined to `src/BlueCode.Core/{AgentLoop.fs, Domain.fs}` and `src/BlueCode.Cli/{CompositionRoot.fs, Adapters/Json.fs, Adapters/FsToolExecutor.fs}`, plus `ToolExpansionTests.fs` (new) and `FileToolsTests.fs` / `AgentLoopTests.fs` additions
- 43 commits since v1.1 close
- ~3 days wall-clock (2026-04-24 milestone start → 2026-04-26 Phase 9.1 verifier passed)

**Git range:** `c4f087e` (v1.1 close) → `776e1be` (Phase 9.1 phase-completion)

**What's next:**

v1.3 candidate: "Bench-Driven Quality Gates" — formalize the bench harness (move from `/tmp/bench-v1.2/` to repo-tracked `bench/` with regression-gate mode) and execute PERF-01 system-prompt shrink (~1500 → ~700 chars) with the formalized bench as the validation gate. The 09.1-05 loop-injection mechanism is a primitive that could let some prompt content move to post-tool-result injections instead of always living in the base prompt. Alternatives: tactical "just lock the bench" 1-phase milestone, or "+ STM-01 streaming" 3-phase milestone.

**Audit note:** v1.2 ran `/gsd:audit-milestone` mid-milestone (post-Phase 9 completion). Initial verdict was `passed`; revised to `tech_debt` after a same-day 36-run re-bench surfaced TOOL-08 dispatcher gap, 72B `truncated` regression, and 32B edit_file+write_file redundancy. The audit's `tech_debt[]` items mapped 1:1 to Phase 9.1's deliverables, which closed all three. Final phase verifier returned `passed` 5/5 on 2026-04-26; v1.2 ships clean. **Bench discipline lesson:** structural verification (line numbers, schema enum, test pass) is necessary but insufficient — requirements citing specific failure traces need probe-style behavioral tests at phase verification time.

---

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
