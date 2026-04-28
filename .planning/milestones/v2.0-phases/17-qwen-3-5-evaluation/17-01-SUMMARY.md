---
phase: 17-qwen-3-5-evaluation
plan: 01
subsystem: infra
tags: [qwen3.5, mlx_lm, launchd, thinking-mode, moe, local-llm]

# Dependency graph
requires:
  - phase: 17-qwen-3-5-evaluation
    provides: 17-RESEARCH.md with model availability, memory budget, thinking-mode risk analysis
provides:
  - "documentation/qwen35-install.md — self-contained runbook for installing 35B/122B MoE services"
  - "launchd plist templates for com.ohama.qwen35b + com.ohama.qwen122b with thinking-mode mitigation"
  - "Path A vs Path B decision framework for thinking-mode disable"
  - "Unified-memory budget table (35B+122B vs 32B+72B on 128 GB Mac)"
affects:
  - "17-02 — Phase 17 service swap follows this doc step-by-step"
  - "17-03 — bench comparison references memory budget and Path A/B choice"
  - "Phase 16 — bench baseline must be re-keyed if SWITCH decision is made"

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Path A (server flag) preferred over Path B (F# patch) for thinking-mode disable"
    - "Co-existence policy: 32B/72B models kept on disk until SWITCH confirmed"
    - "Korean+English bilingual prose with ## section numbering, mirroring local-llm-services.md"

key-files:
  created:
    - "documentation/qwen35-install.md (685 lines, 9+ sections)"
  modified: []

key-decisions:
  - "Path A (--chat-template-kwargs server flag) is preferred; Path B (F# patch to buildRequestBody) is documented as empirical fallback if Path A is unavailable"
  - "Old models (32B/72B) NOT deleted; co-existence policy until Phase 17-03 SWITCH decision"
  - "mlx_lm >= 0.25.2 version gate is the FIRST install step — doc fails closed if older"
  - "122B cold-start may exceed 180s HttpClient.Timeout; documented as manual wait procedure (not code change) in §8"
  - "mlx-community/Qwen3.5-35B-A3B-4bit is Instruct (no -Instruct suffix used in Qwen3.5)"

patterns-established:
  - "Sibling doc pattern: new runbook mirrors local-llm-services.md shape (section numbers, table style, Korean prose + English code) without modifying the original"
  - "Thinking-mode is a runtime toggle, not a separate model download — documented explicitly to prevent misdiagnosis"

# Metrics
duration: 15min
completed: 2026-04-26
---

# Phase 17 Plan 01: Qwen 3.5 Install Runbook Summary

**685-line operational runbook for Qwen 3.5 35B-A3B + 122B-A10B MoE services with launchd plists, thinking-mode Path A/B mitigation, 128 GB memory budget table, and 17-02 swap procedure**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-04-26T22:50:22Z
- **Completed:** 2026-04-26T22:55:07Z
- **Tasks:** 1/1
- **Files modified:** 1 created, 0 modified

## Accomplishments

- Created `documentation/qwen35-install.md` (685 lines, 9 numbered sections + 2 appendices) as a fully self-contained runbook
- Embedded full launchd plist XML for both `com.ohama.qwen35b.plist` (port 8000) and `com.ohama.qwen122b.plist` (port 8001) with `--chat-template-kwargs {"enable_thinking": false}` in ProgramArguments
- Documented the thinking-mode dual-path mitigation (Path A = server flag preferred; Path B = F# `buildRequestBody` patch as fallback) with explicit decision table for §4.4 flag availability check
- Unified memory budget table comparing 32B+72B (~58.8 GB) vs 35B+122B (~89.5 GB) with ~30 GB tighter headroom noted and OOM mitigation options enumerated
- Cross-referenced `documentation/qwen32b-base-to-instruct.md` with explicit note that v1.x trap cause (Base model) differs from Qwen 3.5 trap cause (thinking mode)

## Task Commits

1. **Task 1: Author documentation/qwen35-install.md (sections §1-§9)** — (docs)

**Plan metadata:** (forthcoming)

## Files Created/Modified

- `documentation/qwen35-install.md` — Complete install + verification runbook for Qwen 3.5 35B/122B MoE services

## Decisions Made

1. **Path A preferred, Path B documented as fallback**: The `--chat-template-kwargs` server flag approach avoids any code change. Path B (adding `chat_template_kwargs` to F# `buildRequestBody` anonymous record) is documented verbatim in §6 but explicitly marked as 17-02-only if Path A empirically fails.

2. **Co-existence policy**: 32B/72B models are NOT deleted; `qwen32b/` and `qwen72b/` directories stay on disk as rollback assets throughout Phase 17 evaluation. Confirmed in §2.2.

3. **mlx_lm >= 0.25.2 as hard gate**: Made this the FIRST step in §1.1, with upgrade command and explanation of why this version is required (Qwen3/Qwen3-MoE architecture support PR).

4. **No code change in 17-01**: Path B patch is documented only — `QwenHttpClient.fs` remains untouched. The 180s timeout discussion in §8 is resolved operationally (manual wait) not programmatically.

5. **Full plist XML for both 35B and 122B**: Rather than just showing diffs, both plists are written out in full so user can copy-paste either one verbatim. Mirrors local-llm-services.md §3.1 style.

6. **mlx-community/Qwen3.5-35B-A3B-4bit has no -Instruct suffix**: Explicitly documented in §3.1 note to prevent the "-Instruct" hunt that caught the 32B trap in v1.x.

## Deviations from Plan

None — plan executed exactly as written. The single task produced exactly the content specified in the plan action (§1–§9 + header + appendices), all 15 mechanical verification checks pass, and no out-of-scope files were touched.

## Issues Encountered

Minor observation: the `chat_template_kwargs` count check (must be ≥ 2) was satisfied with exactly 2 occurrences (Path A plist section reference and Path B F# patch section). The plan's minimum was 2; the final doc has 2. All other checks had comfortable margins.

Also noted: the research doc mentions a `mlx-vlm` conversion gotcha (Qwen3.5-35B-A3B-4bit was converted using mlx-vlm, not mlx-lm) — this was documented in Appendix A for completeness, though the plan action did not explicitly mention it. Included as a minor safety note, not a deviation.

## User Setup Required

None for 17-01 itself — no services were loaded, no models downloaded, no code changed.

The doc itself describes what the user must do manually in 17-02:
- Model download (§3.2) — ~90 GB, requires user's network
- plist copy-paste and `plutil -lint` (§4)
- `launchctl load` (§5.1) — requires user's terminal session
- Path A/B decision at §4.4 smoke test (determines which path 17-02 implements)

## Next Phase Readiness

- **17-02 is ready**: The swap runbook (§9) and Path A/B decision framework (§4.4 + §6) give 17-02's checkpoints a concrete procedure to reference.
- **17-03 is ready**: Memory budget table and throughput context (§7) give 17-03's bench comparison its baseline framing.
- **No blockers**: All 15 verification checks pass; build is green; no source files touched.

---
*Phase: 17-qwen-3-5-evaluation*
*Completed: 2026-04-26*
