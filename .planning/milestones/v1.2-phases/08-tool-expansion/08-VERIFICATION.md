---
phase: 08-tool-expansion
verified: 2026-04-25T12:18:00Z
status: passed
score: 5/5 must-haves verified
---

# Phase 8: Tool Expansion — Verification Report

**Phase Goal:** The agent can use `edit_file`, `glob_search`, and `grep_search` as first-class tools — all three recognized by the JSON schema, dispatched by the agent loop, implemented in the executor, and described in the system prompt.

**Verified:** 2026-04-25T12:18:00Z
**Status:** passed
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Three new DU cases exist in Core; Core stays pure | ✓ VERIFIED | `Domain.fs:60-62` shows EditFile / GlobSearch / GrepSearch; `grep -c` returns 3; `check-no-async.sh` exits 0; no Serilog/Spectre/HttpClient in Core source files |
| 2 | Schema accepts the new actions; agent loop dispatches them | ✓ VERIFIED | `Json.fs:204` enum lists exactly 8 values; `LlmPipelineTests.fs:125-132` round-trips all 8 actions through `parseLlmResponse` (extraction + schema); `AgentLoop.fs:93-112` has match cases for all 3 |
| 3 | Schema enum has exactly the 8 expected actions; rejects unknowns | ✓ VERIFIED | `Json.fs:204` literal enum: `["read_file", "write_file", "list_dir", "run_shell", "edit_file", "glob_search", "grep_search", "final"]`; `LlmPipelineTests.fs` already has SchemaViolation tests for malformed input; `additionalProperties: false` enforced |
| 4 | edit_file behaves correctly for 1, 0, and N≥2 match counts | ✓ VERIFIED | `FsToolExecutor.fs:401-436` implements count-then-replace with 0/1/N branches; `ToolExpansionTests.fs:34-119` covers all three cases plus PathEscape, file-not-found, CRLF preservation |
| 5 | Full v1.1 test suite still passes; no regression | ✓ VERIFIED | `dotnet run --project tests/BlueCode.Tests` reports **236 passed, 0 failed, 0 errored, 1 ignored** (live Qwen smoke, intentionally disabled). 236 ≥ 218 baseline; matches SUMMARY claim of 18 new tests (218→236) |

**Score:** 5/5 truths verified

---

## Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/BlueCode.Core/Domain.fs` | Tool DU has EditFile/GlobSearch/GrepSearch cases | ✓ VERIFIED | Lines 60-62; substantive (179+ lines total); imported by AgentLoop, FsToolExecutor; pattern-matched exhaustively in 2 places |
| `src/BlueCode.Cli/Adapters/Json.fs` | `llmStepSchema` enum lists 8 actions | ✓ VERIFIED | Line 193 declaration, line 204 enum; substantive; consumed by `parseLlmResponse` (line 253+); used by QwenHttpClient |
| `src/BlueCode.Core/AgentLoop.fs` | `dispatchTool` recognises 3 new action strings | ✓ VERIFIED | Lines 36 (private fn), 93-112 (3 new match arms); falls through to `UnknownTool` at line 113 for any unrecognised string |
| `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` | Implementations for editFile/globSearch/grepSearch + dispatch update | ✓ VERIFIED | editFileImpl (lines 401-436), globSearchImpl (449-484), grepSearchImpl (498-565), `globToRegex` helper (363-386); ExecuteAsync dispatch at lines 583-592 covers all 7 Tool cases |
| `src/BlueCode.Cli/CompositionRoot.fs` | System prompt documents the 3 new actions | ✓ VERIFIED | Lines 51-70; line 55 enumerates all 8 in the action union; lines 62-64 give per-action input schemas |
| `tests/BlueCode.Tests/ToolExpansionTests.fs` | Full test coverage for TLX-01/02/03 | ✓ VERIFIED | 17 test cases across 3 testLists (editFile×6, globSearch×5, grepSearch×6); registered in fsproj line 25 (before RouterTests.fs line 26) and rootTests at RouterTests.fs:98 |

---

## Key Link Verification

| From | To | Via | Status | Details |
|------|----|----|--------|---------|
| LLM JSON response | LlmStep record | `llmStepSchema` enum | ✓ WIRED | `Json.fs:204` enum + `validateAndDeserialize` at line 224 |
| LlmStep.action string | Tool DU | `AgentLoop.dispatchTool` | ✓ WIRED | `AgentLoop.fs:36-113` matches all 8 actions including 3 new ones; produces `EditFile`/`GlobSearch`/`GrepSearch` |
| Tool DU | side effects | `FsToolExecutor.ExecuteAsync` | ✓ WIRED | `FsToolExecutor.fs:582-592` exhaustive match on all 7 cases |
| `defaultSystemPrompt` | bootstrap config | `CompositionRoot.bootstrap` | ✓ WIRED | `CompositionRoot.fs:85` assigns `SystemPrompt = defaultSystemPrompt` to Config; `CompositionRootTests.fs:33-44` asserts the prompt mentions all 8 actions |
| ToolExpansionTests module | test runner | rootTests list | ✓ WIRED | `RouterTests.fs:98` `BlueCode.Tests.ToolExpansionTests.tests`; runtime confirmed by 236 total passes including the 17 ToolExpansion cases |

---

## Requirements Coverage

| Requirement | Status | Evidence |
|-------------|--------|----------|
| TLX-01 (edit_file: unique-match replace) | ✓ SATISFIED | Implementation `FsToolExecutor.fs:401-436`; tests `ToolExpansionTests.fs:34-119` cover 1/0/N match contract + side-effect verification (file content on disk before/after) |
| TLX-02 (glob_search: project-rooted glob with cap) | ✓ SATISFIED | Implementation `FsToolExecutor.fs:449-484`; tests `ToolExpansionTests.fs:128-202` cover happy path, no-match, path-escape, 100-cap with marker, hidden files |
| TLX-03 (grep_search: regex with file_glob filter and ReDoS guard) | ✓ SATISFIED | Implementation `FsToolExecutor.fs:498-565`; tests `ToolExpansionTests.fs:211-324` cover match/no-match, invalid regex, file_glob path-separator rejection, file_glob filter, 200-char line truncation, 500ms ReDoS timeout |

---

## Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| (none) | — | — | — | No TODO/FIXME/placeholder/stub patterns in phase 8 source files |

A targeted scan of `Domain.fs`, `AgentLoop.fs`, `Json.fs`, `FsToolExecutor.fs`, `CompositionRoot.fs`, and `ToolExpansionTests.fs` found no `TODO`/`FIXME`/`placeholder`/`coming soon`/empty-return stubs. The Core-purity grep also produced only documentation comments saying *"No Serilog, Spectre, …"* (i.e., comments asserting the rule, not violations).

---

## Test Suite Result

```
Passed:  236
Failed:  0
Errored: 0
Ignored: 1   (live Qwen smoke — disabled unless BLUECODE_AGENT_SMOKE=1)
```

Command: `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj --configuration Debug -- --summary`

Baseline (v1.1): 218. Delta: +18 (matches SUMMARY claim).

---

## Human Verification Required

None. All success criteria verified programmatically:
- DU additions / Core purity → grep + script
- Schema enum membership → file inspection + round-trip test
- Dispatcher coverage → file inspection (all 3 match arms present + indirect compile-exhaustiveness on Tool DU)
- edit_file 1/0/N semantics → unit tests on real filesystem fixtures
- Test-suite regression → full Expecto run

A live agent run with a prompt that should provoke `glob_search` (e.g., "find all .fs files under src/") would corroborate end-to-end behaviour, but is **not required** by the success criteria — SC2 explicitly accepts "a parsing test that round-trips a mock JSON … through the schema" as the verification path, and `LlmPipelineTests.fs:125-132` does so for `glob_search`.

---

## Gaps Summary

No gaps. All 5 success criteria pass. The phase delivers what the goal promised: the agent has 3 new first-class tools wired through the schema, dispatcher, executor, and system prompt; the existing 218-test suite has grown to 236 with no regressions; Core purity invariants hold; and TLX-01/02/03 requirements are satisfied with substantive implementations and matching tests.

---

*Verified: 2026-04-25T12:18:00Z*
*Verifier: Claude (gsd-verifier)*
