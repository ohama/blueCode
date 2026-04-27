---
phase: 20
plan: 20-03
name: role-probe-and-restore
subsystem: llm-client-protocol
tags: [AgentLoop, mid-conversation-role, mlx_lm, probe, documentation]
requires: ["20-01", "20-02"]
provides: ["scripts/probe-system-role.sh", "20-03-PROBE-OUTPUT.md", "AgentLoop.fs:249,260,266 Phase 20-03 invariant comments", "howto doc sync", "qwen35-install.md cross-reference"]
affects: []
tech-stack:
  added: []
  patterns: ["re-runnable bash probe as evidence artifact", "in-code comment citing probe run + date + HTTP code"]
key-files:
  created:
    - scripts/probe-system-role.sh
    - .planning/phases/20-qwen-3-5-protocol-alignment/20-03-PROBE-OUTPUT.md
  modified:
    - src/BlueCode.Core/AgentLoop.fs
    - documentation/howto/enforce-llm-tool-terminality-via-post-user-injection.md
    - documentation/qwen35-install.md
decisions:
  - "REJECT verdict: 122B HTTP 404 on mid-conversation System message — Role = User is permanent invariant"
  - "AgentLoop.fs:249/260/266 Role = User unchanged, comments added per REJECT path"
  - "howto F# snippets updated to Role = User; History section added"
  - "qwen35-install.md Phase 20-03 section added with probe-system-role.sh reference"
metrics:
  duration: "~15 min (includes 2× probe HTTP round-trips + bench gate 6×)"
  completed: "2026-04-27"
---

# Phase 20 Plan 03: Role Probe and Restore Summary

**One-liner:** 122B REJECT probe (HTTP 404) confirmed Role = User at AgentLoop.fs injection sites is an mlx_lm.server invariant, not a 35B-only workaround; comments + doc sync applied.

## What Was Built

Phase 20-03 added `scripts/probe-system-role.sh` as re-runnable evidence and probed Qwen 3.5 122B (port 8001) with a 3-message system/user/system conversation. Verdict: **REJECT** (HTTP 404 — `"System message must be at the beginning."`), captured in `20-03-PROBE-OUTPUT.md`. The verdict is unambiguous: mlx_lm.server's chat template enforces a structural rule that no System message may appear after position 0. Based on this verdict (REJECT path), `AgentLoop.fs:249/260/266` was retained at `Role = User` — the code itself is unchanged from Phase 17-02, but each of the three POST-EDIT CONSTRAINT / POST-READ HINT injection sites now has an explicit three-line comment documenting Phase 17-02 + Phase 20-03 evidence and pointing to `20-03-PROBE-OUTPUT.md`. The `enforce-llm-tool-terminality-via-post-user-injection.md` howto doc's F# snippets (lines 110 and 188) were updated from `Role = System` (stale since v1.1 Phase 17-02) to `Role = User` with inline comments, the checklist was updated, and a History section was added. A Phase 20-03 cross-reference section was appended to `qwen35-install.md`. The final Phase 20 bench gate (`bench/run.sh --gate`) exited 0 (6/6 PASS), validating that all of 20-01 (sampling params + timeout), 20-02 (reasoning_content fallback), and 20-03 (role probe + doc sync) stack cleanly.

## Probe Verdict

**REJECT** — HTTP 404, body: `{"error": "System message must be at the beginning."}`

Both Qwen 3.5 35B (Phase 17-02 evidence) and Qwen 3.5 122B (Phase 20-03 probe) reject mid-conversation `role: system` entries with the same mlx_lm.server error. The Phase 17-02 workaround was not 35B-specific; it is a chat template structural constraint.

## Commits

| Hash | Message |
|------|---------|
| cb80a8a | chore(20-03): add probe-system-role.sh + capture probe verdict |
| 54265ac | docs(20-03): document Role = User invariant at AgentLoop.fs:249,260,266 (probe REJECT) |
| f4fd87a | docs(20-03): sync howto doc F# snippets to current AgentLoop.fs state |
| 8164160 | docs(20-03): cross-reference Phase 20-03 probe verdict in qwen35-install.md |

## Test Count Delta

266/1/0 → 266/1/0 (no change; 20-03 adds no tests — comments and doc sync are non-functional)

## Bench Gate Result

`bench/run.sh --gate` exit 0, 6/6 PASS — final Phase 20 gate validates the full Qwen 3.5-aligned client stack.

```
PASS T6_122b    steps=5/5 exit=0
PASS W1_122b    steps=3/3 exit=0
PASS W2_122b    steps=3/3 exit=0
PASS T1_122b    steps=1/3 exit=0
PASS T5_122b    steps=3/4 exit=0
PASS B2_122b    steps=2/3 exit=0
GATE PASS (6/6)
```

## Files Modified

| File | Change |
|------|--------|
| `scripts/probe-system-role.sh` | Created — re-runnable 122B probe (chmod +x) |
| `.planning/phases/20-qwen-3-5-protocol-alignment/20-03-PROBE-OUTPUT.md` | Created — HTTP code + body + verdict |
| `src/BlueCode.Core/AgentLoop.fs` | Added 3-line comments above lines 249/253, 264/267, 273/276 (REJECT path) |
| `documentation/howto/enforce-llm-tool-terminality-via-post-user-injection.md` | Updated F# snippets at lines 110+188 to Role = User; added checklist item; added History section |
| `documentation/qwen35-install.md` | Added §Phase 20-03 section with probe verdict + script reference |

## AgentLoop.fs Role State (Post-20-03)

| Site | Marker | Role | Phase 20-03 Comment |
|------|--------|------|---------------------|
| Line ~249 | POST-EDIT CONSTRAINT | `Role = User` | Yes — REJECT (HTTP 404) |
| Line ~264 | POST-READ HINT truncated | `Role = User` | Yes — REJECT (HTTP 404) |
| Line ~273 | POST-READ HINT out-of-range | `Role = User` | Yes — REJECT (HTTP 404) |
| Line 172 | PARSE ERROR correction | `Role = User` | Unchanged (out of scope) |

## Decisions Made

See frontmatter `decisions` field. Key: REJECT verdict converts the v1.1 open question "is this workaround still needed for 122B?" into a permanently documented invariant backed by a re-runnable probe.

## Phase 20 Cross-Cutting Summary

Phase 20 closed three Qwen 2.5 → Qwen 3.5 protocol gaps in the LLM client: sampling parameters now match the Qwen 3.5 model card (20-01: temp=0.7, top_p=0.8, top_k=20, presence_penalty=0.0; timeout 180→300s), `extractContent` falls back to `reasoning_content` (20-02: 4 new tests, 262→266 test count), and the mid-conversation `Role = System` workaround status is documented and probe-verified for 122B (20-03: REJECT — Role = User confirmed invariant for both 35B and 122B). The bench gate remained 6/6 PASS through all three plans; test count grew from 262/1/0 to 266/1/0 (4 net new tests in 20-02; 20-01 and 20-03 added 0). v2.1+ candidates (thinking-mode-on, native tool_calls, additionalProperties relaxation, max_tokens bump) remain explicitly out of scope.

## Deviations from Plan

None — plan executed exactly as written. The probe verdict (REJECT) and task numbering (Tasks 1-4 mapping to script/AgentLoop/howto/qwen35) matched the plan's branching specification.
