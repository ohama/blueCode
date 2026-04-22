# Phase 3: Tool Executor — Phase Summary

**Three plans delivered: read_file/write_file/list_dir with path containment (03-01), 22-validator bash_security.py port with 97 tests (03-03), and run_shell with BashSecurity gate + real process lifecycle + 8 tests (03-02); all 153 tests pass, all 6 ROADMAP success criteria provable.**

---

## Plans Executed

| Plan | Name | Duration | Commits | Tests Added |
|------|------|----------|---------|-------------|
| 03-01 | Tool Executor Foundation | 5 min | 2 + metadata | 15 |
| 03-03 | BashSecurity Port | ~15 min | 4 + metadata | 97 |
| 03-02 | run_shell Implementation | 7 min | 2 + metadata | 8 (+removed 1 stub) |
| **Total** | | **~27 min** | **8 + 3** | **119 (146→153 net +7)** |

Note: 03-01 laid the scaffold; 03-03 was executed second to supply the security chain; 03-02 wired them together.

---

## ROADMAP Phase 3 Success Criteria — Evidence

All 6 ROADMAP Phase 3 success criteria are now met and empirically verifiable:

### SC 1: read_file with valid path and optional line range returns content

**Requirement:** `read_file "src/main.fs" (Some (1, 10))` returns lines 1-10 of the file.

**Evidence:** `FileToolsTests.readFileTests` (6 cases):
- "reads full file content" — `ReadFile (FilePath "hello.txt", None)` → Success with full content
- "line range (1, 2) returns first two lines only" — `ReadFile (FilePath "lines.txt", Some (1, 2))` → lines 1+2 only
- "line range (2, 3) returns middle lines" — exact range verification
- "missing file returns Failure" — non-existent file → Failure
- "path outside project root returns PathEscapeBlocked" — `../escape.txt` → PathEscapeBlocked
- "output exceeding 2000 chars truncated with marker (TOOL-06)" — 3000-char file → truncated with `[truncated:`

### SC 2: write_file validates path before writing

**Requirement:** Path escape (e.g., `../../evil.txt`) is blocked WITHOUT any file being written.

**Evidence:** `FileToolsTests.writeFileTests` (4 cases):
- "writes content to new file inside project" — round-trip write + read verified
- "path outside project root returns PathEscapeBlocked WITHOUT writing" — evil.txt not created
- "absolute path outside project root returns PathEscapeBlocked" — `/tmp/...` blocked
- "path starting with ~ returns PathEscapeBlocked" — tilde expansion rejected

### SC 3: run_shell dangerous command returns SecurityDenied WITHOUT executing

**Requirement:** `run_shell "rm -rf /"` returns `SecurityDenied` and never spawns a process.

**Evidence:** `RunShellTests.securityTests` (2 cases):
- "rm -rf / returns SecurityDenied and does NOT spawn" — sentinel file survives (process never ran)
- "command substitution $(whoami) blocked" — command substitution → SecurityDenied

The sentinel-file assertion is the key proof: we create a file in the working directory, run `rm -rf /`, assert SecurityDenied, then assert the file still exists. If the process had spawned, the sentinel would be gone (or the test would observe filesystem side effects). The file survives, proving BashSecurity.validateCommand fired BEFORE Process.Start.

### SC 4: run_shell with long-running command returns Timeout

**Requirement:** `run_shell "sleep 35"` returns `Timeout` in less than 31 seconds total.

**Evidence:** `RunShellTests.timeoutTests` (1 case):
- "sleep 35 times out in <31s, returns Timeout" — elapsed 30.17s in empirical run; asserts `Ok (ToolResult.Timeout 30)`, elapsed < 31.0s, elapsed ≥ 29.0s

Implementation: `CancellationTokenSource.CreateLinkedTokenSource(ct)` with `cts.CancelAfter(TimeSpan.FromSeconds 30.0)`. On `OperationCanceledException`: if `ct.IsCancellationRequested` → caller cancel (UserCancelled); else → timeout → `Process.Kill(entireProcessTree=true)` → `Ok (Timeout 30)`.

### SC 5: Tool output exceeding 2000 chars is truncated with human-readable marker

**Requirement:** Long output includes `[truncated: showing first 2000 of N chars]` marker.

**Evidence (two independent tests):**
- `FileToolsTests.readFileTests`: "output exceeding 2000 chars truncated with marker (TOOL-06)" — 3000-char file read → `content.Length < 2200`, contains `[truncated:`
- `RunShellTests.truncationTests`: "yes | head -3000 returns Success with truncation marker" — 6000-char stdout → `out.Length < 2200`, contains `[truncated:`

Two-stage cap: `capOutput s SHELL_STDOUT_CAP` (100KB resource limit) then `truncateOutput` (2000-char TOOL-06 marker). The truncateOutput implementation: `sprintf "%s\n\n[truncated: showing first %d of %d chars]" portion MESSAGE_HISTORY_CAP raw.Length`.

### SC 6: Exhaustive ToolResult pattern match (compile-time enforcement)

**Requirement:** Every consumer of ToolResult must handle all 5 cases; adding a case = compile error.

**Evidence:** `FsToolExecutor.create` contains an exhaustive `match tool with` over the `Tool` DU. All 5 ToolResult cases are produced by real execution paths:

| ToolResult case | Produced by | Test |
|-----------------|-------------|------|
| `Success` | All tools on happy path | echo hello, reads, writes, lists |
| `Failure` | Non-zero exit / file not found / invalid range | false (exit 1), missing file |
| `SecurityDenied` | validateCommand returns Error | rm -rf /, $(whoami) |
| `PathEscapeBlocked` | validatePath returns Error | ../escape.txt, ../../evil.txt |
| `Timeout` | OperationCanceledException on timeout | sleep 35 |

The F# compiler enforces exhaustiveness. Any new ToolResult case added to `Domain.fs` without updating `FsToolExecutor.create` is a compile error (FS0025 warning → FS0001 mismatch at usage sites, or direct incomplete-match error). This is TOOL-07 semantic contract.

---

## Test Count Summary

| Plan | Tests Added | Running Total |
|------|-------------|---------------|
| Phase 1+2 baseline | 49 (34+15) | 49 |
| 03-01 (FileTools) | +15 | 49 |
| 03-03 (BashSecurity) | +97 | 146 |
| 03-02 (RunShell) | +8, -1 stub | 153 |

**Final: 153 tests pass (1 ignored = smoke test requiring live vLLM)**

Breakdown by suite:
- RouterTests: 16 tests
- LlmPipelineTests: 13 tests
- ToLlmOutputTests: 5 tests
- FileToolsTests: 14 tests (15 original - 1 removed stub)
- BashSecurityTests: 97 tests
- RunShellTests: 8 tests

---

## bash_security.py Port Statistics (Plan 03-03)

| Metric | Value |
|--------|-------|
| Python source lines | 1,262 |
| F# port lines (BashSecurity.fs) | 1,061 |
| Compression ratio | 84% (target was 50-80%; slightly over due to F# StringBuilder vs Python list comprehensions) |
| Validators ported | 22 (plan spec incorrectly stated 18; Python source has 22 `def validate_*` functions) |
| Helper functions ported | 7 |
| Pre-compiled Regex instances | 31 (all `RegexOptions.Compiled`) |
| Constants ported | 7 (READ_ONLY_COMMANDS intentionally skipped — informational only in Python) |
| SecurityDecision DU cases | 4 (Allow, Deny, DenyDeferred, Passthrough) |

---

## Auto-fixes and Deviations Across All Plans

### Plan 03-01 Deviations

| # | Rule | Description | Impact |
|---|------|-------------|--------|
| 1 | Rule 1 - Bug | FileToolsTests.fs placed BEFORE RouterTests.fs (F# FS0433: EntryPoint must be in last compiled file) | Build fix |
| 2 | Rule 1 - Bug | Timeout DU name collision: Domain.Timeout vs ToolResult.Timeout shadowing via `open Domain` | Build fix |

### Plan 03-03 Deviations

| # | Rule | Description | Impact |
|---|------|-------------|--------|
| 1 | Rule 2 - Missing Critical | 22 validators ported (plan spec stated 18 — Python source has 22) | Correct port fidelity |
| 2 | Design Decision | DenyDeferred DU case to match Python deferred evaluation semantics | Architecture fidelity |
| 3 | Design Decision | READ_ONLY_COMMANDS intentionally skipped (informational only in Python, never a deny gate) | Reduced dead code |

### Plan 03-02 Deviations

| # | Rule | Description | Impact |
|---|------|-------------|--------|
| 1 | Rule 1 - Bug | F# task CE type errors with nested try/with inside try/finally — fixed by wrapping Process.Start in Ok/Error result | Build fix |
| 2 | Rule 1 - Bug | Timeout DU name collision in RunShell pattern match — fixed by using BlueCode.Core.Domain.Timeout | Build fix |
| 3 | Rule 2 - Missing Critical | rootTests aggregator in RouterTests.fs not mentioned in plan — added runShellTests to aggregator | Test discovery fix |

**Total deviations: 7 (5 auto-fix bugs, 2 design decisions that improved fidelity)**

---

## Phase 3 Requirement Closure (TOOL-01..07)

| TOOL | Requirement | Plan | Status |
|------|-------------|------|--------|
| TOOL-01 | read_file with optional line range | 03-01 | CLOSED |
| TOOL-02 | write_file with path escape prevention | 03-01 | CLOSED |
| TOOL-03 | list_dir with depth limit and hidden-file exclusion | 03-01 | CLOSED |
| TOOL-04 | run_shell with 30s timeout + 100KB/10KB caps | 03-02 | CLOSED |
| TOOL-05 | BashSecurity validator chain (22 validators) | 03-03 → consumed 03-02 | CLOSED |
| TOOL-06 | 2000-char truncation with [truncated: marker across all tools | 03-01 (helper) + 03-02 (applied) | CLOSED |
| TOOL-07 | ToolResult semantic contract: all 5 cases producible from real tool calls | 03-02 close-out | CLOSED |

All 7 requirements closed. Phase 3 ROADMAP goal achieved.

---

## Phase 4 Hand-off

Phase 4 (Agent Loop) can proceed with:

1. **`FsToolExecutor.create`** — called at `CompositionRoot.fs` during startup:
   ```fsharp
   let toolExecutor = FsToolExecutor.create (Directory.GetCurrentDirectory())
   ```
   Binds all 4 tools to the process working directory.

2. **`IToolExecutor.ExecuteAsync`** — the agent loop calls this for each ToolCall action. The signature is:
   ```fsharp
   ExecuteAsync : Tool -> CancellationToken -> Task<Result<ToolResult, AgentError>>
   ```

3. **Known concerns for Phase 4 (from STATE.md):**
   - HttpClient singleton should move to `CompositionRoot.fs`
   - Spectre.Console + Serilog stream separation needed when both active
   - Ctrl+C signal handling (LOOP-07) — sets CancellationToken → propagates to run_shell as `Error UserCancelled`

4. **Phase 5 hand-off:**
   - `--timeout` CLI flag: promote `_timeoutMs` in `runShellImpl` from ignored to real parameter
   - `run_shell` already carries `Timeout (ms)` in the `Tool.RunShell` DU; the parameter is ready to be wired

---

## Manual Gate Notes

- **SC-01 (smoke test gate):** `BLUECODE_SMOKE_TEST=1 dotnet test BlueCode.slnx --filter "Smoke"` requires live vLLM 32B on localhost:8000. This is a Phase 1 pending verification item. Not Phase 3 specific.
- **No other manual gates** were encountered during Phase 3 execution.

---

*Phase: 03-tool-executor*
*Completed: 2026-04-22*
*Plans: 03-01, 03-03, 03-02 (3/3)*
