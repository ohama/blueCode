---
phase: 19
plan: 19-02
name: code-bench-docs-alignment
subsystem: cli-routing-bench-docs
tags: [model-rename, retirement-guard, bench-harness, single-model, documentation]
requires:
  - 19-01 (Qwen 2.5 physical retirement — filesystem + launchd state)
provides:
  - Model DU renamed (Qwen32B→Qwen122B, Qwen72B→Qwen35B) with PathRetired error variant
  - CLI retirement guard (--model 32b/72b → exit 2 with Phase 19 reference)
  - --with-35b dual-mode flag (WithDual Argu case)
  - bench/run.sh single-model rewrite (absorbed scripts/bench-122b-only.sh)
  - bench/baseline.json 6-entry single-model baseline
  - CLAUDE.md + qwen35-install.md + single-model-eval.md + bench.md updated
affects:
  - 16 (bench fixtures should reflect canonical model before Phase 16 baselines them)
tech-stack:
  added: []
  patterns:
    - single-model default via parseForcedModel None → Some Qwen122B
    - PathRetired AgentError variant + validateModelPath probe-layer guard
    - eager 35B-absent probe in Program.fs (gated on withDual, exit 1)
key-files:
  created: []
  modified:
    - src/BlueCode.Core/Domain.fs
    - src/BlueCode.Core/Router.fs
    - src/BlueCode.Cli/Adapters/QwenHttpClient.fs
    - src/BlueCode.Cli/Rendering.fs
    - src/BlueCode.Cli/CliArgs.fs
    - src/BlueCode.Cli/CompositionRoot.fs
    - src/BlueCode.Cli/Program.fs
    - tests/BlueCode.Tests/RouterTests.fs
    - tests/BlueCode.Tests/RenderingTests.fs
    - tests/BlueCode.Tests/ReplTests.fs
    - tests/BlueCode.Tests/SessionStoreTests.fs
    - tests/BlueCode.Tests/JsonlSinkTests.fs
    - tests/BlueCode.Tests/SmokeTests.fs
    - tests/BlueCode.Tests/AgentLoopTests.fs
    - tests/BlueCode.Tests/CliArgsTests.fs
    - tests/BlueCode.Tests/ModelsProbeTests.fs
    - bench/run.sh
    - bench/baseline.json
    - CLAUDE.md
    - .planning/ROADMAP.md
    - documentation/qwen35-install.md
    - documentation/single-model-eval.md
    - documentation/bench.md
  deleted:
    - scripts/bench-122b-only.sh
decisions:
  - "PathRetired in AgentError DU (not a passive string); validateModelPath probe-layer guard"
  - "parseForcedModel None → Some Qwen122B (explicit default; no intent routing indirection)"
  - "Eager 35B-absent probe in Program.fs only when --with-35b set (2s timeout, exit 1)"
  - "Gate set 6/6 (T6/W1/W2/T1/T5/B2 all _122b); no padding to 8"
  - "Router.intentToModel kept structurally, dormant by default (ForcedModel=Some Qwen122B bypasses it)"
  - "Router.modelToName Don't-Do rule reviewed and preserved (unrelated to Phase 19)"
  - "baseline.json flat top-level keys (no tests.* wrapper) to match jq verify contract"
metrics:
  duration: ~45 min
  completed: 2026-04-27
---

# Phase 19 Plan 02: Code-Bench-Docs Alignment Summary

Phase 19-02 retired the digital traces of Qwen 2.5 from the source code, bench harness,
and operator docs. `Domain.fs`'s `Model` DU was renamed (Qwen32B→Qwen122B, Qwen72B→Qwen35B)
in a single atomic compile cascade across 11 files; `AgentError.PathRetired` was added as a
first-class error variant and wired into `validateModelPath` in `QwenHttpClient.fs` for
probe-layer retirement detection. `parseForcedModel` gained a `withDual: bool` parameter
and now routes `--model 32b`/`--model 72b` to exit-2 retirement errors referencing Phase 19,
defaults `None` explicitly to `Some Qwen122B`, and gates `--model 35b` on the new `--with-35b`
Argu flag. `bench/run.sh` was rewritten in-place absorbing `scripts/bench-122b-only.sh`
(deleted via `git rm`); all invocations use `--model 122b` and the gate set shrank from 8
to 6 labels (T6/W1/W2/T1/T5/B2 all _122b). `bench/baseline.json` was halved to 6 flat
top-level entries using Phase 18 single-model-eval data for the three new keys (T1/W1/W2).
CLAUDE.md, qwen35-install.md, single-model-eval.md, and bench.md were updated to reflect
122B-only canonical runtime, dual-mode reactivation procedure, and Phase 19 cross-references.

## Bench Gate Result

`bench/run.sh --gate`: **GATE PASS (6/6)** — all 6 single-model 122B entries pass.

```
  PASS T6_122b    steps=4/5 exit=0
  PASS W1_122b    steps=3/3 exit=0
  PASS W2_122b    steps=3/3 exit=0
  PASS T1_122b    steps=1/3 exit=0
  PASS T5_122b    steps=3/4 exit=0
  PASS B2_122b    steps=2/3 exit=0
===== GATE PASS (6/6) =====
```

## Test Count Delta

254/1/0 (pre-19-02) → **262/1/0** (post-19-02)

- +4 tests: `ModelsProbeTests.validateModelPathTests` (Task 2)
- +5 net new tests: CliArgsTests retirement errors + --with-35b parsing (Task 7; replaced 2 old tests, added 7)
- Ignored: 1 (existing SmokeTests network test — unchanged)
- Failed: 0 (non-network tests clean)

## Task Commits

| Task | Name | Commit |
|------|------|--------|
| 1 | Model DU rename + PathRetired (compile cascade) | `dba1fa1` |
| 2 | PathRetired guard in tryParseModelId / validateModelPath | `77caae6` |
| 3 | WithDual flag + parseForcedModel retirement guard + eager 35B probe | `200ebdc` |
| 4 | Router intent table docstring (dormant in single-model default) | `5253155` |
| 5 | bench/run.sh rewrite + scripts/bench-122b-only.sh deleted | `a610f23` |
| 6 | bench/baseline.json halve to 6-entry single-model | `094f1cf` |
| 7 | CliArgsTests: retirement errors + --with-35b parsing | `a0740e1` |
| 8a | CLAUDE.md + qwen35-install.md + single-model-eval.md + bench.md docs | `f6e4f12` |
| 8b | ROADMAP.md SC6 baseline count 254→258-264 | `a4e3d81` |
| 9 | Final verification — no fixup needed; no commit |  |

## Deviations from Plan

1. **Auto-fix (Rule 1): baseline.json structure inconsistency** — Plan Task 5 (gate jq paths used `.tests.${key}`) and Task 6 verify (`jq 'keys | length'` returns 6) were inconsistent. The old baseline.json had a `{"_meta": ..., "tests": {...}}` wrapper structure; the verify expected 6 flat top-level keys. Resolved by: restructuring baseline.json to 6 flat top-level entries AND updating gate() jq paths from `.tests.${key}` to `.${key}`. Committed together in Task 6 commit. This is a plan-level inconsistency (not a code bug); the resolution is the most consistent interpretation of the plan's intent.

2. **Auto-fix (Rule 1): CliArgsTests.fs intermediate state in Task 1** — Task 1 needed to update CliArgsTests.fs to use the new `parseForcedModel` signature (which gained a `withDual` parameter in Task 3). To keep the build green at Task 1's commit boundary, temporary placeholder tests were written; Task 7 replaced them with the final retirement-error assertions as planned.

3. **Auto-add (Rule 2): Rendering.fs PathRetired arm added in Task 2** — The plan mentioned adding PathRetired to Rendering.fs "if any natural error-formatting path encountered." Rendering.fs had an exhaustive match on AgentError that produced an FS0025 warning after Task 1 added PathRetired. Added `| PathRetired path -> sprintf "Path retired in Phase 19: %s. ..."` in Task 2 to eliminate the warning and provide user-readable output.

4. **No change: AgentLoop.fs had no Qwen32B/Qwen72B direct DU references** — RESEARCH §Pitfall 6 warned about AgentResult.Model construction sites. Inspection showed AgentLoop.fs uses the `model` variable directly (not the DU cases) in all AgentResult construction sites. No edit needed in AgentLoop.fs. Still staged per plan for completeness (compiler confirmed no changes required).
