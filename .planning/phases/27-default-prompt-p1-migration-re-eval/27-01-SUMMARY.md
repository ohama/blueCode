---
phase: 27-default-prompt-p1-migration-re-eval
plan: 01
status: complete
date: 2026-04-29
files_changed: 1
tests_added: 0
commits: 2
affects: ["src/BlueCode.Cli/CompositionRoot.fs"]
subsystem: "cli-prompt"
requires: []
tech-stack:
  added: []
  removed: []
  unchanged: ["F#", ".NET 8", "Argu", "Spectre.Console", "Serilog"]
decisions:
  - "P1 directive migrated from planSystemPromptSuffix to defaultSystemPrompt (architectural fix for Phase 26 BLOCKED gap)"
  - "P2 few-shot Example/Targets/Steps block stays in plan-mode only (per ROADMAP guardrail)"
  - "Char budget plan-mode-combined invariant: 1968 chars before = 1968 chars after"
patterns: []
---

# Plan 27-01 Summary: P1 Migration to defaultSystemPrompt

## Outcome

PASS — P1 enumeration directive migrated; bench gate 7/7 PASS preserved (0 phrasing iterations); one feat commit + one plan-meta docs commit.

## Migration Details

| Constant | Before (chars) | After (chars) | Delta |
|---|---|---|---|
| defaultSystemPrompt | 783 | 967 | +184 |
| planSystemPromptSuffix | 1183 | 999 | -184 |
| Plan-mode combined | 1968 | 1968 | 0 (invariant) |

Pre-edit measurement (`/tmp/27-01-charcount-pre.txt`):
```
defaultSystemPrompt_len=783
planSystemPromptSuffix_len=1183
combined_planmode=1968
```

Post-edit measurement (`/tmp/27-01-charcount-post.txt`):
```
defaultSystemPrompt_len=967
planSystemPromptSuffix_len=999
combined_planmode=1968
```

Verified by `dotnet fsi /tmp/27-01-charcount.fsx` (F# script using `System.IO.File.ReadAllText` + `IndexOf` to extract inner string content from both triple-quoted constants).

## Edit Operations Applied

1. **Operation 1** — Append P1 to `defaultSystemPrompt` after `Rules:` paragraph:
   - `old_string`: `"Rules: One tool per response. ... No prose, no markdown — JSON object only."""`
   - `new_string`: same + `\n\nWhen the task requires renaming or restructuring multiple symbols, list ALL targets explicitly in your thought before editing. Do not start editing until the full list is enumerated."""`
   - P1 text left-flush at column 0; closing `"""` immediately follows final `.`

2. **Operation 2** — Remove P1 + trailing blank separator from `planSystemPromptSuffix`:
   - Removed single line: `When the task requires renaming or restructuring multiple symbols, list ALL targets explicitly in your thought before editing. Do not start editing until the full list is enumerated.`
   - Plus one blank line (extra blank line left by Edit tool corrected in follow-up edit)
   - Result: `approve first.\n\nExample: rename add->sum...` (one blank line before Example, as designed)

## Bench Gate Evidence

`bash bench/run.sh --gate` post-migration:

```
===== GATE: compare to baseline =====
  PASS T6_122b    steps=4/5 exit=0
  PASS W1_122b    steps=3/3 exit=0
  PASS W2_122b    steps=3/3 exit=0
  PASS T1_122b    steps=1/3 exit=0
  PASS T5_122b    steps=3/4 exit=0
  PASS B2_122b    steps=2/3 exit=0
  PASS MT_122b    steps=2/4 exit=0
===== GATE PASS (7/7) =====
```

- All 7 fixtures PASS within v2.2 baseline_max
- Phrasing iteration count: **0** — initial migration phrasing held without any regression
- P1 conditional clause "When the task requires renaming or restructuring multiple symbols" dormant for all 7 fixtures as predicted (27-RESEARCH.md Q5)

Full log: `/tmp/27-01-gate.log`

## Out-of-Scope Guardrails Held

- `git diff src/BlueCode.Core/` empty (Core purity preserved — migration is Cli-only)
- `git diff bench/baseline.json` empty (out-of-scope guardrail)
- `git diff bench/run.sh` empty (out-of-scope guardrail)
- Phase 26 BLOCKED commit `7837ad5` intact in git history (`git log --oneline | grep "block Phase 26"` confirmed)
- P2 few-shot Example/Targets/Steps block UNCHANGED in `planSystemPromptSuffix`
- `git diff HEAD~1 HEAD --stat` shows exactly 1 file changed: `src/BlueCode.Cli/CompositionRoot.fs`

## Structural Impact

The P1 directive now reaches the agent-loop path (`runLoop` in AgentLoop.fs) via `defaultSystemPrompt` → `AppComponents.Config.SystemPrompt`. This closes the architectural gap exposed by Phase 26 BLOCKED: the CORR-EVAL-02 eval harness invokes `blueCode --verbose --model 122b` WITHOUT `--plan`, so previously P1/P2/P3 were all bypassed.

P2 (few-shot Example/Targets/Steps) remains in `planSystemPromptSuffix` only — its `Steps:` notation is plan-mode-specific and would create semantic confusion in agent-loop mode (per 27-RESEARCH.md Q9).

## Commits

1. `feat(27-01): migrate P1 enumeration directive to defaultSystemPrompt` — commit `fbb9c55` (1 file: `src/BlueCode.Cli/CompositionRoot.fs`)
2. `docs(27-01): complete P1 migration plan` — commit TBD (1 file: `27-01-SUMMARY.md`)

## Next

Plan 27-02: kickstart 122B service (mandatory KV cache clear) + CORR-EVAL-02 stochastic re-run (up to 3 attempts). P1 now reaches the eval harness path; extraction bias gap closed.
