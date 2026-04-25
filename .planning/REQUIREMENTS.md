# Requirements: blueCode v1.3 Bench-Driven Quality Gates

**Defined:** 2026-04-26
**Core Value:** Mac 로컬 Qwen 32B/72B를 strong-typed F# agent loop로 안정적으로 돌린다
**Milestone goal:** Lock in v1.2's behavioral wins as a regressable suite, then use that suite to validate a system-prompt shrink that closes the B2 regression and creates headroom for future tools.

## v1.3 Requirements

8 requirements across 2 categories. Each anchored in measured pain or audit-discovered learning from v1.2.

### Bench

Move v1.2's bench harness from `/tmp/` into the repo with a regression-gate mode. v1.2's two audit-rebench cycles surfaced the cost of an ephemeral bench: every regression we caught (T6, W1, W2) and every fix we shipped (Phase 9.1) was validated against an `/tmp/`-anchored script with fixtures created mid-execution. The next regression — there will be one — needs a baseline to compare against.

- [ ] **BENCH-01**: `bench/run.sh` repo-tracked, consolidates v1.2's `/tmp/bench-v1.2/run.sh` selectors (`phase1`, `phaseA`, `phaseB`, `phaseC`, `v9_1`, `v9_1_rev`, `v9_1_rev2`) into semantically-named groupings (e.g., `regression`, `t6-suite`, `w1-w2-edit-terminality`, `canaries`)
  - Input: bash script in `bench/run.sh`
  - Behavior: Mode-flag dispatcher (`--gate`, `--regression`, `--canary`, `--all`) replacing v1.2's case-statement selectors
  - **근거**: v1.2's `/tmp/`-anchored `run.sh` was rewritten across three commits (v9_1, v9_1_rev, v9_1_rev2) and lives outside the git tree; loss of `/tmp/` = loss of the bench

- [ ] **BENCH-02**: `bench/fixtures/` holds versioned bug fixtures
  - Move from untracked `bench-fixtures/{bug_lastchar.fs, bug_average.fs}` to repo-tracked `bench/fixtures/`
  - Add at least one new fixture for each existing audit failure mode (B2 divide-by-zero is a candidate to formalize as a fixture)
  - **근거**: 09.1-03 executor created `bench-fixtures/` mid-execution because it was missing; explicit version control prevents fixture drift

- [ ] **BENCH-03**: `bench/baseline.json` records the post-9.1 baseline
  - Schema: per-test step counts (T6 32B/72B = 3, W1/W2 32B = 3, T1 32B = 1-3), pass/fail status, elapsed-time medians (T1/T5 timings), B2 32B/72B regression status (current state — flagged for PERF-03 to fix)
  - JSON format suitable for diff comparison and machine-parseable in `--gate` mode
  - **근거**: Without recorded baseline, "did anything regress?" has no answer; v1.2's audit-rebench cycle was 36 runs because no baseline existed

- [ ] **BENCH-04**: `bench/run.sh --gate` mode for regression detection
  - Behavior: Runs the regression subset (T6 × 32B/72B, W1/W2 × 32B, T1/T5 canaries — ~8 invocations, ~2 min wall-clock), compares to `baseline.json`, prints one-line PASS/FAIL summary + per-test diff on FAIL, exits non-zero on regression
  - Suitable for pre-commit hooks, manual pre-PR validation, or CI integration
  - **근거**: The `--gate` mode IS what makes the bench regressable rather than just runnable; without it, "lock in v1.2's wins" stays informal

- [ ] **BENCH-05**: `documentation/bench.md` explains fixture conventions and operational use
  - Topics: fixture file naming convention, prompt design (avoid explicit tool naming per 09.1-04 discovery), how to add a new test, how to update baseline after intentional regression-fix, hang-contingency for `mlx_lm.server` 32B
  - **근거**: v1.2's bench knowledge is currently scattered across `documentation/v1.2-bench-followup.md`, the audit, and the SUMMARY.md files; consolidate so v1.4+ contributors don't re-derive

### Performance

System-prompt shrink validated by the BENCH gates. v1.2 grew the prompt from ~700 chars (v1.0) to ~1500 chars (post-9.1: 8 actions + 3 strategy hints + edit_file directive wording). The audit hypothesized B2's both-model regression (32B + 72B both flagged "integer truncation" instead of "empty list → DivideByZeroException") was prompt-length attention shift. This category tests that hypothesis with a fix.

- [ ] **PERF-01**: `defaultSystemPrompt` cut from ~1500 chars to ≤800 chars without regressing any `bench/run.sh --gate` test
  - Method: audit each sentence — sentences without an observed-failure citation become removal candidates; aggressive trimming on the 8-action documentation block (likely the densest section)
  - Validation: `bench/run.sh --gate` after every prompt edit (iterate until shrink-target met AND gates green)
  - **근거**: B2 regression evidence + v1.2 milestone audit's PERF-01 framing; ~50% reduction creates headroom for v1.4+ tools without further attention-shift risk

- [ ] **PERF-02**: 09.1-05 loop-injection primitive extended to post-tool-result hints
  - Required: post-`read_file`-truncated injection (move the `If truncated: pick a smaller window — end_line - start_line < 50` hint from base prompt to a System message that fires only when the read returned `truncated`)
  - Optional: post-`write_file` redundancy guard (similar mechanism if observed value)
  - Out of scope: a general "post-tool framework" — keep PERF-02 tightly scoped to the read_file case to ship without over-engineering
  - **근거**: PERF-01's shrink target is hard to hit while keeping the contextual hints; moving them to post-tool-result injections is the architectural lever 09.1-05 unlocked. Side benefit: hints fire only when relevant, less noise

- [ ] **PERF-03**: B2 fixture (divide-by-zero misdiagnosis) returns to v1.1 baseline behavior
  - Pre-fix state (v1.2): both 32B and 72B flag "integer truncation" instead of correctly identifying empty list as the failure cause
  - Post-fix expectation: at least one model (preferably both) correctly identifies empty-list cause; `bench/baseline.json` updated to reflect the recovery
  - **근거**: Direct test of the audit's prompt-length-attention-shift hypothesis. If B2 doesn't recover after PERF-01 shrink, the hypothesis was wrong and we learn something useful for v1.4

## Future Requirements (v1.4+)

v1.3 explicitly excludes these. Tracked but not in current roadmap.

### Streaming & Persistence

- **STM-01** (v1.4+): SSE token streaming — `ILlmClient.CompleteAsync` → `IAsyncEnumerable<string>`; Rendering.fs real-time token output. UX win, no measured pain
- **SES-01** (v2+): Session persistence + `--resume <id>` — XL, no measured pain

### Escalation & UX

- **ROU-05** (v1.4+): `MaxLoopsExceeded` auto 32B→72B escalation — TOOL-08 + 9.1 closure makes this less urgent
- **CLI-08** (v1.4+): Ctrl+C "Cancelling..." display + partial token count

### Hygiene

- **OPS-01** (v1.4+): launchd-based prompt cache kickstart schedule
- **OBS-06** (v1.4+): Per-port `MaxModelLen` visibility
- **TST-01** (v1.4+): Shared `makeMockResponse` test helper — possible bundle into Phase 10 cleanup

## Out of Scope

v1 boundaries unchanged. v1.3 adds no new exclusions; the deferred items above are "v1.4+", not "out of scope".

| Feature | Reason |
|---------|--------|
| MCP / LSP / Plugin / hook / GUI / Windows·Linux / AOT / multi-tool chaining / >5 step chains / LLM-based compaction / vision / voice / background daemon / team runtime / cost tracking / notebook editing / telemetry | v1.0 OOS unchanged across v1.1, v1.2, v1.3 |
| Cross-turn memory in multi-turn REPL | v2+ scope |
| Multi-platform `tryParseModelId` | Windows OOS — `StartsWith("/")` heuristic Mac/Linux only |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| BENCH-01    | Phase 10 | Pending |
| BENCH-02    | Phase 10 | Pending |
| BENCH-03    | Phase 10 | Pending |
| BENCH-04    | Phase 10 | Pending |
| BENCH-05    | Phase 10 | Pending |
| PERF-01     | Phase 11 | Pending |
| PERF-02     | Phase 11 | Pending |
| PERF-03     | Phase 11 | Pending |

**Coverage:**
- v1.3 requirements: 8 total
- Mapped to phases: 8 (roadmap created 2026-04-26)
- Unmapped: 0 ✓

---
*Requirements defined: 2026-04-26*
*Last updated: 2026-04-26 — traceability filled after roadmap creation*
