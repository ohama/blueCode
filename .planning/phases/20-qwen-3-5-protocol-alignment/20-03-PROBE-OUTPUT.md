# Phase 20-03 Probe Output

**Probed:** 2026-04-27 17:13
**Target:** http://127.0.0.1:8001/v1/chat/completions (Qwen 3.5 122B, mlx_lm.server)
**Script:** scripts/probe-system-role.sh

## Test Conditions

3-message conversation:
1. system: "You are a terse assistant. Respond in one short sentence."
2. user: "What is 2+2?"
3. **system (mid-conversation):** "[CONSTRAINT] You must answer with the digit 4, no prose."

## Result

**HTTP Code:** 404
**Response Body (first 600 chars):**

```
{"error": "System message must be at the beginning."}
```

**Probe Exit Code:** 1

## Verdict

**REJECT** — 122B (Qwen 3.5 122B-A10B-4bit, mlx_lm.server) rejected mid-conversation System message with HTTP 404. The chat template enforces "System message must be at the beginning." — a mid-conversation `role: system` entry is structurally disallowed.

## Interpretation

- HTTP 404 with body `{"error": "System message must be at the beginning."}` is an unambiguous hard rejection by the chat template — not a silent skip, not a content issue, but a template-level structural violation.
- This confirms Phase 17-02's `Role = User` change at AgentLoop.fs lines 249/260/266 is NOT a 35B-only workaround: 122B enforces the same constraint.
- The `Role = User` assignment at the three POST-EDIT CONSTRAINT / POST-READ HINT injection sites in AgentLoop.fs is now documented as a permanent invariant for the current mlx_lm.server chat template implementation, applicable to both Qwen 3.5 35B and 122B.
- The authority signal for these constraint messages is carried by the `[POST-EDIT CONSTRAINT]` / `[POST-READ HINT]` text marker, not by the role. The User role is correct.

## Evidence Lineage

- Phase 17-02 (commit 54e54a9): mid-conversation `Role = System` changed to `Role = User` after 35B chat template returned HTTP 404 on mid-conversation system messages. 122B was not separately probed; the change applied uniformly because 35B + 122B were both in production at the time.
- Phase 19: Qwen 2.5 retired; 122B is sole canonical model; 35B is cold rollback only.
- Phase 20-03 (this probe, 2026-04-27): 122B tested in isolation. Result: HTTP 404 — same rejection behaviour as 35B. `Role = User` at lines 249/260/266 is confirmed correct for both models.
