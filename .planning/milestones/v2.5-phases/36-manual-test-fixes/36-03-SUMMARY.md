---
phase: 36-manual-test-fixes
plan: 03
plan_name: prompt-suffix-and-doc
status: complete
completed_at: 2026-05-04T09:18:47Z
test_count_delta: 1
files_modified:
  - src/BlueCode.Cli/CompositionRoot.fs
  - documentation/manual-test-guide.md
  - CLAUDE.md
  - .planning/ROADMAP.md
  - tests/BlueCode.Tests/CompositionRootTests.fs
core_diff_lines: 0
commits:
  - "feat(36-03): tighten planSystemPromptSuffix with explicit max-10 + no-placeholder constraints (T-75/T-76)"
  - "docs(36-03): update manual-test-guide T-16/17/18/19/100/101 for --allow-paths + T-100 re-interp"
bench_gate: PASS (7/7)
phase_test_count_delta: 12
subsystem: cli-prompt+docs
affects: []
requires: [36-02]
---

# Phase 36 Plan 03: prompt-suffix-and-doc Summary

**One-liner:** planSystemPromptSuffix expanded with MAXIMUM 10 HARD LIMIT + no-placeholder Path rules (T-75/T-76 prompt tuning); manual-test-guide threaded with --allow-paths flag; T-100 hallucinated-success diagnosed as FinalAnswer structural behaviour + model hallucination (no code bug); bench gate 7/7 PASS.

## Outcome

- **planSystemPromptSuffix tuned (T-75/T-76 mitigation):** Added two new clauses to `CompositionRoot.fs`:
  1. `Constraints:` line now reads "MAXIMUM 10 steps (HARD LIMIT — plans with 11+ steps are auto-rejected and your turn is wasted)".
  2. New `Path rules:` line: every step's path must be a literal file path; no placeholder forms (`<file>`, `<discovered_file_X>`, etc.); unknown paths require a discovery step first.
  Existing few-shot Example/Targets/Steps block (Phase 24-02 P2) preserved verbatim.

- **manual-test-guide.md updated:** Top-of-file blockquote explains `--allow-paths` requirement for all `/tmp/*` tests. T-16, T-17, T-18, T-19 commands now include `--allow-paths /tmp/bc-test`. T-100, T-101 commands now include `--allow-paths /tmp/bc-e2e`. T-19 obsolete action-item callout replaced with Phase 36 historical note.

- **T-100 hallucinated-success diagnosed and documented:** Research confirmed no code bug. `FinalAnswer` step is always structurally `StepSuccess` (AgentLoop.fs line 323); the `[ok]` display for the final step is correct and independent of tool failures in prior steps. "Successfully appended" was model hallucination after path-block — an LLM-layer behaviour, not fixable at the blueCode layer. With `--allow-paths` the path-block disappears and the hallucination trigger is eliminated.

- **CLAUDE.md:** Added planSystemPromptSuffix invariant note: `defaultSystemPrompt(967)` + `"\n\n"` + `planSystemPromptSuffix(1577)` = 2546 chars. Previous values (999, 1968) documented as historical.

- **ROADMAP.md:** Stats Target "Zero changes to:" bullet annotated: "planSystemPromptSuffix (modified by Phase 36 for T-75/T-76 mitigation)".

- **Regression guard test added:** `CompositionRootTests.fs` new testCase asserts `planSystemPromptSuffix` contains literal strings "MAXIMUM 10" and "placeholder". Protects T-75/T-76 mitigation against silent removal in future prompt tuning.

- **Bench gate 7/7 PASS:** All six fixture labels (T6_122b, W1_122b, W2_122b, T1_122b, T5_122b, B2_122b) + MT_122b passed. Zero regression across all three Phase 36 plans.

- **Phase 36 invariant:** `git diff master -- src/BlueCode.Core/` = 0 lines (cumulative).

## Suffix Change Diff (key additions)

Old Constraints line:
```
Constraints: 1-10 steps. Use the minimum steps needed; reserve the full budget only for tasks requiring reads across multiple files before editing. No two adjacent steps may be identical. Do NOT execute — user will approve first.
```

New Constraints line:
```
Constraints: MAXIMUM 10 steps (HARD LIMIT — plans with 11+ steps are auto-rejected and your turn is wasted). Use the minimum steps needed; reserve the full budget only for tasks requiring reads across multiple files before editing. No two adjacent steps may be identical. Do NOT execute — user will approve first.
```

New Path rules line (added between Constraints and Example):
```
Path rules: Every step's input.path (or pattern, or command argument) MUST be a literal file path or filename you have determined from the prompt or from a prior grep_search/glob_search/list_dir step. Do NOT emit placeholder forms such as "<file>", "<discovered_file_X>", "<placeholder>", "filename", "path/to/file". If you do not yet know the exact path, your FIRST step must be the discovery tool (grep_search / glob_search / list_dir) — never a write_file or edit_file with a guessed path.
```

Suffix length: 999 → 1577 (+578 chars). Combined prompt (plan mode): 1968 → 2546.

## Doc Update Summary

- 6 test commands updated with `--allow-paths`: T-16, T-17, T-18, T-19, T-100 (2 commands), T-101.
- 1 top-of-file blockquote added explaining `--allow-paths` requirement.
- T-100 result section replaced with 3-part Phase 36 re-interpretation (round 1 FAIL, research finding, fix approach).
- T-19 obsolete "action item" callout replaced with Phase 36 historical note.
- Phase 36 references in guide: 12 occurrences.
- `--allow-paths` occurrences in guide: 16.

## Bench Gate Transcript (last 10 lines)

```
===== GATE: compare to baseline =====
  PASS T6_122b    steps=5/5 exit=0
  PASS W1_122b    steps=3/3 exit=0
  PASS W2_122b    steps=3/3 exit=0
  PASS T1_122b    steps=1/3 exit=0
  PASS T5_122b    steps=3/4 exit=0
  PASS B2_122b    steps=2/3 exit=0
  PASS MT_122b    steps=2/4 exit=0
===== GATE PASS (7/7) =====
```

## Phase-Cumulative Status

| Metric | Value |
|--------|-------|
| Plans | 3 (36-01, 36-02, 36-03) |
| Commits (code) | 5 (fix/test/feat/feat/docs) |
| Commits (meta) | 3 (docs) |
| Tests added | +12 (333 → 345) |
| Core diff | 0 lines |
| Bench gate | 7/7 PASS |
| Bench baseline.json | byte-identical to master |

Phase 36 success criteria from ROADMAP.md:
1. T-14 invariant: glob auto-expand (Plan 36-01) — DONE
2. T-75 mitigation: MAXIMUM 10 steps HARD LIMIT in suffix — DONE (this plan)
3. T-76 mitigation: no-placeholder Path rules in suffix — DONE (this plan)
4. PlanValidator reject detail on retry — SKIPPED (Core read-only, per phase requirement)
5. T-16/17/18/19/100/101 unblock with --allow-paths — DONE (Plans 36-02 + 36-03 docs)
6. T-100 root cause identified — DONE (no code bug; documented)
7. Bench gate 7/7 PASS — DONE
8. Test count delta +12 (in [+7, +12] range) — DONE (+12 exactly, ceiling)
9. `git diff master -- src/BlueCode.Core/` empty — DONE

## Deviations from Plan

None — plan executed exactly as written.

## Open Follow-Ups (deferred per phase out-of-scope)

- **priorSteps message-ordering quirk (T-54/59/61):** Model behaviour; no structural fix available at blueCode layer without LLM-side changes. Candidate for v2.6+ prompt engineering or message labelling.
- **Auto/default `/tmp/*` allowlist:** Only explicit `--allow-paths` opt-in by design (security invariant preserved). An `--allow-tmp` convenience flag is a future ergonomics candidate.
- **Glob/wildcard patterns in --allow-paths:** Current implementation is exact path prefix only (`startsWith`). Wildcard expansion would require shell-glob or regex support — v2.6+ scope.
- **System prompt path-block warning:** A clause in `defaultSystemPrompt` warning the model when tools return PathEscapeBlocked could reduce hallucinated-confirmation events. Out of scope for Phase 36; v2.6+ candidate.
- **T-100 round 2 rerun:** With `--allow-paths /tmp/bc-e2e` in place, next manual round should PASS. Deferred to next manual test session.

## Next Workflow

`/gsd:verify-work 36` UAT gate, then optionally re-run round 2 of manual test suite to confirm T-14, T-16..T-19, T-100, T-101 all PASS with `--allow-paths` flag.
