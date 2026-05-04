# Phase 36: Manual Test Fixes - Research

**Researched:** 2026-05-04
**Domain:** F# Cli-layer patches — FsToolExecutor, CliArgs, CompositionRoot, Rendering, AgentLoop (read-only for Track 4)
**Confidence:** HIGH — all findings from direct file reads and fsi script verification

---

## Summary

Phase 36 targets four concrete bugs surfaced by the 2026-05-04 manual test round. All fixes are
Cli-layer only; `src/BlueCode.Core/` is read-only throughout. Each track has a well-understood
root cause verified against the source files.

**Track 1 (glob_search):** `globToRegex "*.fsproj"` produces `^[^/]*\.fsproj$` which never
matches a relative path like `src/BlueCode.Core/BlueCode.Core.fsproj` (the `/` is not in
`[^/]*`). The fix is one line in the system prompt: document that bare patterns like `*.fsproj`
only match the repo root; or auto-expand to `**/*.fsproj`. An fsi test confirmed 0 matches with
`*.fsproj` and 3 matches with `**/*.fsproj` against the actual repo tree.

**Track 2 (PlanValidator UX):** `PlanValidator` lives in Core (`src/BlueCode.Core/PlanValidator.fs`)
and is read-only. All UX improvements (friendly error messages, placeholder detection) belong in
the Cli layer: the system prompt (how the LLM is told to stay under 10 steps), the `buildCorrection`
helper in `AgentLoop.runPlanTurn`, and `Rendering.renderError`. No Core changes needed.

**Track 3 (`--allow-paths`):** `FsToolExecutor.validatePath` accepts a single `projectRoot`; all
paths are validated against it. The fix adds an `allowedPaths: string list` parameter to
`FsToolExecutor.create`, wires a new `--allow-paths` Argu flag through `CliOptions` and
`CompositionRoot.bootstrap`, and extends `validatePath` to also accept prefix-matched canonical paths.

**Track 4 (hallucinated success):** `AgentLoop.runLoop` at lines 363-369 correctly maps
`PathEscapeBlocked _ -> StepFailed "path escape blocked"`. The step renderer at `Rendering.fs`
line 29 displays `StepFailed` as `"fail"`. The `[ok]` the tester saw in T-100 was from the
SECOND LLM turn (`bc --resume "$SID" ...`) — the second invocation found no priorStep evidence
of a failure for the `/tmp/bc-e2e/notes.md` path (the first turn's PathEscapeBlocked step was
serialized but the model was told "[PATH BLOCKED] Attempted: /tmp/bc-e2e/notes.md" as an
observation, which it can still claim success over). The hallucinated success is a model behaviour
issue, not a code bug. Fix: update T-100 prompt to use project-root paths; document finding.

**Primary recommendation:** Fix tracks 1 and 3 as code changes; fix track 2 as prompt/UX
improvements only (no Core changes); document track 4 as model behaviour with prompt update to
manual test guide.

---

## Standard Stack

No new packages. All changes use existing:

### Core
| Component | File | Purpose |
|-----------|------|---------|
| Argu | `src/BlueCode.Cli/CliArgs.fs` | CLI flag parsing |
| FsToolExecutor | `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` | Path validation + glob |
| CompositionRoot | `src/BlueCode.Cli/CompositionRoot.fs` | Wiring |
| Rendering | `src/BlueCode.Cli/Rendering.fs` | Error messages |
| AgentLoop | `src/BlueCode.Core/AgentLoop.fs` | `buildCorrection` for plan retry |
| PlanValidator | `src/BlueCode.Core/PlanValidator.fs` | READ-ONLY (Core purity) |

**Installation:** No new packages.

---

## Architecture Patterns

### Track 1: glob_search recursive default

**Root cause confirmed by fsi:** `globToRegex "*.fsproj"` → `^[^/]*\.fsproj$`.
Relative paths from `Path.GetRelativePath(root, f)` include `/` separators
(`src/BlueCode.Core/BlueCode.Core.fsproj`). The `[^/]*` portion cannot match
across directories, so 0 files match.

**Current code** (`FsToolExecutor.fs` lines 481-527):
```fsharp
let rx = globToRegex pattern
let opts = EnumerationOptions()
opts.RecurseSubdirectories <- true
opts.AttributesToSkip <- FileAttributes.System
let matches =
    Directory.EnumerateFiles(root, "*", opts)
    |> Seq.map (fun f -> Path.GetRelativePath(projectRoot, f).Replace('\\', '/'))
    |> Seq.filter (fun rel -> rx.IsMatch(rel))
```

`RecurseSubdirectories` is already `true` — ALL files are enumerated. The problem is only
in the regex applied to the RELATIVE path.

**Recommended fix (Option A — preferred):**
Auto-expand bare patterns (no `/` and no leading `**`) by prepending `**/` before converting to regex.

```fsharp
// In globSearchImpl, before calling globToRegex:
let effectivePattern =
    if not (pattern.Contains('/')) && not (pattern.StartsWith("**/"))
    then "**/" + pattern
    else pattern
let rx = globToRegex effectivePattern
```

This makes `*.fsproj` behave as `**/*.fsproj` (3 matches confirmed by fsi), while
`src/**/*.fs` and `**/*.fs` are unchanged. It does NOT change the existing test
`"src/**/*.fs"` — that pattern already has a `/` so it is not expanded.

**System prompt addition (alongside Option A):**
In `CompositionRoot.defaultSystemPrompt`, the glob_search input hint reads:
```
- glob_search: {pattern, path?}
```
Extend to:
```
- glob_search: {pattern, path?}  -- pattern: use **/*.ext to match recursively (e.g., **/*.fsproj); bare *.ext auto-expands
```
This prevents the model from asking for `*.fsproj` and being surprised, while the code-level
auto-expansion acts as a safety net.

**Invariant check:** `defaultSystemPrompt` is currently 967 chars and the combined
`defaultSystemPrompt + "\n\n" + planSystemPromptSuffix` must remain 1968 chars per CLAUDE.md.
The prompt suffix (999 chars) is fixed; only `defaultSystemPrompt` can grow, and the combined
invariant must be updated in the plan. Alternatively, make the prompt addition small enough
that the invariant note in CLAUDE.md is updated — but check whether the 1968-char invariant is
checked anywhere in tests.

**Check for prompt length tests:**

```bash
grep -rn "1968\|967\|999\|defaultSystemPrompt\|planSystemPromptSuffix" \
    tests/BlueCode.Tests/ src/BlueCode.Cli/
```

**Regression risk:** Adding `**/` auto-expansion only fires for patterns without `/`. Patterns
with explicit path prefix (`src/**/*.fs`, `bench/**/*.sh`) are unchanged. The existing
`globSearchTests` test uses `"src/**/*.fs"` (has `/`) and `"**/*.nonexistent"` (has `**/`) —
neither is affected.

---

### Track 2: PlanValidator UX rebuild

**PlanValidator location:** `src/BlueCode.Core/PlanValidator.fs` — CORE. Read-only.

**What validatePlan returns:** `Result<Plan, AgentError>` where the error is
`PlanInvalid "plan has N steps, max is 10"`. The validator itself is correct.

**Where `runPlanTurn` is:** `src/BlueCode.Core/AgentLoop.fs` lines 467-534.

**The retry path (lines 489-505):** `buildCorrection` produces a User-role message with
`[PLAN INVALID]` prefix when `PlanInvalid d` is returned. This IS already happening.

**Why T-75 showed "LLM returned invalid JSON twice":**
The LLM first returned an 11-step plan → PlanValidator rejected → `buildCorrection` injected
`[PLAN INVALID] ... max 10 steps ...` → LLM retried → second attempt ALSO failed (either
another 11+ step plan that the validator rejected again, or an actual JSON parse failure).
After 2 attempts, `runPlanTurn` returns `Error (PlanInvalid ...)`.

Then `Program.fs` line 196 catches this as `Error e` and calls:
```fsharp
eprintfn "%s" (renderError e)
lastError <- Some e
finalDecision <- Some PlanGate.Quit
```

`renderError (PlanInvalid detail)` at `Rendering.fs` line 121 produces:
```
Plan invalid: plan has 11 steps, max is 10
```
— which is friendly. But the manual test reported "LLM returned invalid JSON twice" which
means the second attempt returned `InvalidJsonOutput`, not `PlanInvalid`.

**Root cause of "invalid JSON twice" message:**
After attempt 2 fails with `InvalidJsonOutput raw` (not `PlanInvalid`), `runPlanTurn`
returns `Error (InvalidJsonOutput raw)`, and `renderError` at line 111 produces:
```
LLM returned invalid JSON twice. Raw: {...}
```
This replaces the expected `Plan invalid: ...` message. The validator never gets to run
on attempt 2 if attempt 2 doesn't even parse as valid JSON.

**Fix for T-75:** The `[PLAN INVALID]` correction message in `buildCorrection` (AgentLoop.fs line 502)
currently produces:
```
[PLAN INVALID] Your previous plan failed validation: plan has 11 steps, max is 10. Constraints: max 10 steps; ...
```
This is correct. The UX problem is that the LLM is not respecting it and submitting bad JSON on
retry. This is a model behaviour issue — fixing it in code is limited to improving the correction
message text, which is Cli-layer (AgentLoop.fs `buildCorrection` helper, or more precisely
it is in Core but its text content is a string literal that can be improved without changing the
Core contract).

**Caution — Core purity:** `AgentLoop.fs` IS in Core (`src/BlueCode.Core/AgentLoop.fs`).
The phase requirement says "Core untouched." This means the `buildCorrection` string content in
`runPlanTurn` CANNOT be changed. The only Cli-layer improvements are:

1. `Rendering.renderError` for `PlanInvalid` (currently: `"Plan invalid: %s" detail`) —
   **this is Cli-layer, changeable.**
2. `defaultSystemPrompt` glob_search hint — already covered above.
3. `planSystemPromptSuffix` — this is Cli-layer (in `CompositionRoot.fs`), changeable.
   Adding an explicit "Constraints: MAX 10 steps" emphasis to `planSystemPromptSuffix` may
   reduce the frequency of 11-step attempts.

**For T-76 (placeholder detection):** The `checkRenameTargetsEnumerated` check in Core is
read-only. The failing pattern is that LLM uses paths like `<discovered_file_X>` or
`"placeholder"`. These contain non-standard characters that the rename-target heuristic
doesn't catch (it looks at `old_string` field, not at `path` fields). Since Core is off-limits,
the fix is in `planSystemPromptSuffix` (CompositionRoot.fs) — adding a constraint:
"Use exact file paths; do NOT use placeholder names like `<file>`, `placeholder`, or `<discovered_file_X>`."

**PlanRejected friendly message:** `renderError PlanInvalid` already produces
`"Plan invalid: ..."`. The T-75 bad message was `InvalidJsonOutput`, not `PlanInvalid`.
No change needed to `renderError` for `PlanInvalid`. The work is in `planSystemPromptSuffix`
to reduce retry failures.

---

### Track 3: `--allow-paths` CLI flag

**Current path validation** (`FsToolExecutor.fs` lines 73-100):
```fsharp
let private validatePath (projectRoot: string) (inputPath: string) : Result<string, ToolResult> =
```
Single projectRoot, no concept of extra allowed paths.

**Factory function** (`FsToolExecutor.fs` lines 627-641):
```fsharp
let create (projectRoot: string) : IToolExecutor =
    let rootNormalized = Path.GetFullPath(projectRoot)
    { new IToolExecutor with
        member _.ExecuteAsync (tool: Tool) (ct: CancellationToken) =
            ...
```

**Recommended implementation:**

Step 1 — Add `--allow-paths` to `CliArgs.fs`:
```fsharp
| [<AltCommandLine("--allow-paths")>] AllowPaths of paths: string
// usage: "--allow-paths <p1[,p2,...]>  Comma-separated additional paths the agent may access."
```

Single string with comma-separated paths (simplest Argu approach — Argu `[<Repeating>]` is
awkward with shell quoting for this use case). Parsed in Program.fs by splitting on `,`.

Step 2 — Add `AllowPaths: string list` to `CliOptions`:
```fsharp
type CliOptions =
    { ...
      AllowPaths: string list }  // NEW (36): extra allowed path prefixes beyond projectRoot
```

Step 3 — Thread through `CompositionRoot.bootstrap` to `FsToolExecutor.create`:
```fsharp
ToolExecutor = Adapters.FsToolExecutor.create projectRoot opts.AllowPaths
```

Step 4 — Extend `FsToolExecutor.create` signature:
```fsharp
let create (projectRoot: string) (extraAllowedPaths: string list) : IToolExecutor =
    let rootNormalized = Path.GetFullPath(projectRoot)
    let allowedRoots =
        extraAllowedPaths
        |> List.map Path.GetFullPath  // canonicalize at startup
```

Step 5 — Extend `validatePath` or add `validatePathWithExtras`:
```fsharp
let private validatePathWithExtras
    (projectRoot: string)
    (extraRoots: string list)
    (inputPath: string)
    : Result<string, ToolResult> =
    if String.IsNullOrWhiteSpace(inputPath) then Error(PathEscapeBlocked ...)
    elif inputPath.StartsWith("~") then Error(PathEscapeBlocked inputPath)
    else
        try
            // First try projectRoot resolution (handles relative paths)
            let combined = Path.Combine(projectRoot, inputPath)
            let resolved = Path.GetFullPath(combined)
            let rootWithSep r = if r.EndsWith(string Path.DirectorySeparatorChar) then r else r + string Path.DirectorySeparatorChar
            let inProjectRoot =
                resolved = projectRoot || resolved.StartsWith(rootWithSep projectRoot, StringComparison.Ordinal)
            let inExtraRoot =
                extraRoots |> List.exists (fun xr ->
                    resolved = xr || resolved.StartsWith(rootWithSep xr, StringComparison.Ordinal))
            if inProjectRoot || inExtraRoot then Ok resolved
            else Error(PathEscapeBlocked inputPath)
        with _ -> Error(PathEscapeBlocked inputPath)
```

**Key correctness properties:**
- `Path.GetFullPath` canonicalizes at startup AND at check time — handles `../` traversal.
- Trailing-separator fix (same pattern as existing `validatePath` at line 85-89) prevents
  prefix-attack: `/tmp/bc-test` does not permit `/tmp/bc-testing/evil`.
- Absolute paths are handled: `Path.Combine("/tmp/bc-test", "/tmp/bc-test/file.txt")` on .NET
  returns `/tmp/bc-test/file.txt` (absolute wins) — resolved path is then checked against roots.

**Bench/CI safety:** `FsToolExecutor.create projectRoot []` with empty `extraAllowedPaths` is
unchanged from current behaviour. `CompositionRoot.defaultCliOptions` adds `AllowPaths = []`.
Bench invokes `dotnet run --project src/BlueCode.Cli --` without `--allow-paths` → empty list →
existing security invariant preserved.

**Note on glob_search with `--allow-paths`:** `globSearchImpl` uses `validatePath` for the
optional `searchPath`. With `validatePathWithExtras`, a glob like
`glob_search(pattern="**/*.md", path="/tmp/bc-test")` will work. The recursive enumeration
is already `RecurseSubdirectories = true`.

**Manual test guide updates for T-16/17/18/19/100/101:**
Change from:
```bash
mkdir -p /tmp/bc-test
bc --verbose "Create a file at /tmp/bc-test/hello.txt ..."
```
To:
```bash
mkdir -p /tmp/bc-test
bc --allow-paths /tmp/bc-test --verbose "Create a file at /tmp/bc-test/hello.txt ..."
```

---

### Track 4: Hallucinated success investigation

**Status chain is correct in code.** Verified:

`AgentLoop.fs` lines 363-370:
```fsharp
let status =
    match tr with
    | Success _ -> StepSuccess
    | Failure _ -> StepFailed "tool failure"
    | SecurityDenied _ -> StepFailed "security denied"
    | PathEscapeBlocked _ -> StepFailed "path escape blocked"
    | ToolResult.Timeout _ -> StepFailed "timeout"
```

`Rendering.fs` lines 27-31:
```fsharp
let private statusSymbol: StepStatus -> string =
    function
    | StepSuccess -> "ok"
    | StepFailed _ -> "fail"
    | StepAborted -> "aborted"
```

So a `PathEscapeBlocked` result → `StepFailed "path escape blocked"` → displays as `[fail]`.

**What actually happened in T-100:**
T-100 involves two bc invocations. The FIRST invocation tried `write_file /tmp/bc-e2e/notes.md`
→ PathEscapeBlocked → step displayed `[fail]` → model produced `FinalAnswer` saying
"file was created" (hallucination). The SECOND invocation (`bc --resume $SID`) loaded prior
steps as `priorSteps`; the LLM sees `[PATH BLOCKED]` in the observation but then the model
said "Successfully appended" — this is pure model hallucination after seeing the failure.

The `[ok]` mentioned in the test output ("step 2 of model is `[ok]`") referred to the
FinalAnswer step (step 2), which is always `StepSuccess` because `FinalAnswer` branches at
`AgentLoop.fs` line 323: `Status = StepSuccess`. A FinalAnswer step always shows `[ok]`
regardless of what the model claims.

**Conclusion:** No code bug. The status chain is correct. The "hallucinated success" is:
1. Step 1 write_file → `[fail]` (PathEscapeBlocked) — correctly displayed.
2. Step 2 FinalAnswer → `[ok]` — correct; FinalAnswer always succeeds structurally.
3. Model's text content in FinalAnswer claimed success — LLM hallucination.

**Fix:** Documentation only — update T-100 to use `--allow-paths` flag and project-root paths.
Optionally, add a note to the system prompt that `[PATH BLOCKED]` means the file was NOT
written, to reduce hallucinated confirmation. This would be a small `defaultSystemPrompt` update
(Cli-layer).

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Path traversal prevention | Custom string comparison | `Path.GetFullPath` + trailing-sep fix (existing pattern) | Already in `validatePath` lines 80-96; extend, don't rewrite |
| Glob recursive matching | New enumeration logic | `EnumerationOptions { RecurseSubdirectories = true }` (already in place) + regex fix | Only change is the auto-expansion of bare `*` patterns |
| Argu repeating flags | Custom comma split | Single `string` arg with comma-split in Program.fs | Simpler than `[<Repeating>]` for this case; avoids Argu list parsing complexity |

---

## Common Pitfalls

### Pitfall 1: System prompt char invariant
**What goes wrong:** CLAUDE.md states `defaultSystemPrompt(967) + "\n\n" + planSystemPromptSuffix(999) = 1968 chars`. Adding to `defaultSystemPrompt` breaks this.
**Why it happens:** The invariant is documented but not enforced in tests (no test checks 1968 chars specifically — grep confirms no test references "1968").
**How to avoid:** Update the CLAUDE.md invariant comment when the prompt changes. Check actual char counts before commit.
**Warning signs:** The number in CLAUDE.md no longer matches `defaultSystemPrompt.Length`.

### Pitfall 2: Core purity — AgentLoop.fs is in Core
**What goes wrong:** Executor tries to improve `buildCorrection` text in `AgentLoop.fs` since it looks like UX, but it's in `src/BlueCode.Core/`.
**Why it happens:** Phase 36 requirement says "Core untouched". `AgentLoop.fs` is Core.
**How to avoid:** Only touch files under `src/BlueCode.Cli/`. Verify: `git diff master -- src/BlueCode.Core/ | wc -l` must be 0.

### Pitfall 3: FsToolExecutor.create call-site update
**What goes wrong:** `FsToolExecutor.create` signature changes from `(string)` to `(string) (string list)`. The existing call site in `CompositionRoot.bootstrap` (line 114) must be updated.
**Why it happens:** F# curried functions — forgetting to pass `opts.AllowPaths` is a compile error, not a runtime error. Good: the compiler catches it.
**How to avoid:** After changing signature, build immediately — compile error surfaces all call sites.

### Pitfall 4: `[<Tests>]` attribute not needed
**What goes wrong:** New test file added with `[<Tests>]` attribute, but `rootTests` in `RouterTests.fs` is not updated.
**Why it happens:** CLAUDE.md "Test discovery pattern" — this project does NOT use auto-discovery. Both `BlueCode.Tests.fsproj` compile order AND `rootTests` list must be updated.
**How to avoid:** Checklist: (1) add `<Compile Include="NewTests.fs" />` before `RouterTests.fs` in `.fsproj`, (2) add `BlueCode.Tests.NewTests.tests` to `rootTests` in `RouterTests.fs`.

### Pitfall 5: `path?` in glob_search dispatch
**What goes wrong:** `globToRegex` auto-expansion (prepending `**/`) only applies in `globSearchImpl`. The `dispatchTool` in `AgentLoop.fs` (Core, read-only) just passes the raw pattern string through. The fix in `globSearchImpl` (Cli) is sufficient since Core doesn't manipulate the pattern.
**Why it happens:** Pattern flows: LLM → JSON → `dispatchTool` (Core) → `GlobSearch(pattern, searchPath)` → `globSearchImpl` (Cli).
**How to avoid:** Put the fix in `globSearchImpl` at line 508, not in Core dispatch.

### Pitfall 6: Trailing-separator for extra paths
**What goes wrong:** Allow-listing `/tmp/bc-test` permits `/tmp/bc-testing/` via naive `StartsWith`.
**Why it happens:** Without trailing separator: `"/tmp/bc-testing".StartsWith("/tmp/bc-test")` = true.
**How to avoid:** Same trailing-sep fix as existing `validatePath` lines 85-89: compare with `xr + Path.DirectorySeparatorChar`.

### Pitfall 7: Console.SetOut tests must use testSequenced
**What goes wrong:** New tests that redirect Console.SetOut race in parallel execution.
**Why it happens:** Expecto runs `testList` items concurrently by default.
**How to avoid:** Wrap any testList that uses `Console.SetOut` with `testSequenced`. See existing `ReplTests.fs`.

---

## Code Examples

### Glob auto-expansion (Track 1 fix)

```fsharp
// In globSearchImpl, before calling globToRegex (~line 508):
let effectivePattern =
    if not (pattern.Contains('/')) && not (pattern.StartsWith("**"))
    then "**/" + pattern
    else pattern
let rx = globToRegex effectivePattern
```

Verified by fsi: `globToRegex "**/*.fsproj"` → `^.*[^/]*\.fsproj$` → matches
`src/BlueCode.Core/BlueCode.Core.fsproj` (3 matches in actual repo).

### Extended validatePath (Track 3 fix)

```fsharp
// Replace private validatePath with validatePathWithExtras.
// Existing callers pass [] for extraRoots.
let private validatePathWithExtras
    (projectRoot: string)
    (extraRoots: string list)
    (inputPath: string)
    : Result<string, ToolResult> =
    if String.IsNullOrWhiteSpace(inputPath) then
        Error(PathEscapeBlocked(inputPath |> Option.ofObj |> Option.defaultValue ""))
    elif inputPath.StartsWith("~") then
        Error(PathEscapeBlocked inputPath)
    else
        try
            let combined = Path.Combine(projectRoot, inputPath)
            let resolved = Path.GetFullPath(combined)
            let withSep (r: string) =
                if r.EndsWith(string Path.DirectorySeparatorChar) then r
                else r + string Path.DirectorySeparatorChar
            let inRoot (r: string) =
                resolved = r || resolved.StartsWith(withSep r, StringComparison.Ordinal)
            if inRoot projectRoot || List.exists inRoot extraRoots then
                Ok resolved
            else
                Error(PathEscapeBlocked inputPath)
        with _ ->
            Error(PathEscapeBlocked inputPath)

let create (projectRoot: string) (extraAllowedPaths: string list) : IToolExecutor =
    let rootNormalized = Path.GetFullPath(projectRoot)
    let allowedNormalized = extraAllowedPaths |> List.map Path.GetFullPath
    { new IToolExecutor with
        member _.ExecuteAsync (tool: Tool) (ct: CancellationToken) =
            match tool with
            | ReadFile(FilePath path, lineRange) ->
                readFileImpl rootNormalized allowedNormalized path lineRange ct
            ... }
```

Each `*Impl` function receives both `projectRoot` and `allowedNormalized` and calls
`validatePathWithExtras projectRoot allowedNormalized path`.

### CliArgs addition (Track 3)

```fsharp
// In CliArgs.fs, add to CliArgs DU:
| AllowPaths of paths: string   // --allow-paths /tmp/bc-test,/tmp/bc-e2e

// In IArgParserTemplate:
| AllowPaths _ -> "--allow-paths <p1[,p2,...]>  Extra paths the agent may read/write (comma-separated)."
```

### CliOptions addition (Track 3)

```fsharp
// In CompositionRoot.fs, add to CliOptions:
type CliOptions =
    { ...
      AllowPaths: string list }   // NEW (36-03): extra allowed path prefixes

let defaultCliOptions: CliOptions =
    { ...
      AllowPaths: [] }
```

### Program.fs parsing (Track 3)

```fsharp
// After existing flag parsing in Program.fs:
let allowPaths =
    results.TryGetResult CliArgs.AllowPaths
    |> Option.map (fun s -> s.Split(',') |> Array.map (fun p -> p.Trim()) |> Array.toList)
    |> Option.defaultValue []

let opts = { ...; AllowPaths = allowPaths }
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `validatePath projectRoot` only | Same, extended with `extraAllowedPaths` | Phase 36 | Enables `/tmp/*` scratch use with explicit flag |
| `*.fsproj` returns 0 matches | `*.fsproj` auto-expands to `**/*.fsproj` | Phase 36 | T-14 becomes PASS |
| `planSystemPromptSuffix` has no placeholder warning | Added explicit "no placeholders" constraint | Phase 36 | Reduces T-76 MIXED |

---

## Open Questions

1. **System prompt char invariant (Track 1/2):**
   - What we know: `defaultSystemPrompt` is 967 chars, combined is 1968. No test enforces this number.
   - What's unclear: Is the 1968 invariant load-bearing anywhere (bench fixtures, integration tests)?
   - Recommendation: Grep for "1968" before committing prompt changes. Update CLAUDE.md comment
     to reflect new lengths.

2. **planSystemPromptSuffix is public (Track 2):**
   - What we know: `planSystemPromptSuffix` is declared `let` (not `let private`) in CompositionRoot.fs line 95.
   - What's unclear: Whether `CompositionRootTests.fs` or `PlanGateTests.fs` assert its exact content or length.
   - Recommendation: Grep for `planSystemPromptSuffix` in tests before modifying. Changes to its content
     that add "no placeholders" guidance are safe if the suffix remains a non-empty string; the 1968
     invariant may need updating.

3. **T-100 hallucinated success — FinalAnswer step always `[ok]`:**
   - What we know: `FinalAnswer` → `Status = StepSuccess` → `[ok]` is correct behaviour. The test tester
     interpreted this `[ok]` as the write step succeeding.
   - What's unclear: Whether we want to distinguish "model claimed success in final answer" from
     "no tool failure" in the renderer. Out of scope for Phase 36 per requirements.
   - Recommendation: Document in test guide; no code change needed.

---

## Per-Track Summary for Planner

### Track 1: glob_search recursive default
- **Files to change:** `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` (line ~508), `src/BlueCode.Cli/CompositionRoot.fs` (system prompt)
- **Change size:** ~5 lines of code + ~20 chars to system prompt
- **New tests:** 2 tests in `ToolExpansionTests.fs` — (a) bare `*.fsproj` matches nested files, (b) `**/*.fsproj` still works
- **Regression risk:** LOW — auto-expansion only fires for patterns without `/`

### Track 2: PlanValidator UX rebuild
- **Core is read-only.** Only Cli changes.
- **Files to change:** `src/BlueCode.Cli/CompositionRoot.fs` (`planSystemPromptSuffix` — add explicit max-steps and no-placeholder constraints)
- **Change size:** ~2-3 sentences added to `planSystemPromptSuffix`
- **New tests:** 0-1 unit tests verifying suffix contains "10 steps" constraint; or 0 (behaviour tested via integration)
- **Regression risk:** LOW — suffix is additive text

### Track 3: `--allow-paths` CLI flag
- **Files to change:**
  1. `src/BlueCode.Cli/CliArgs.fs` — add `AllowPaths` case to DU
  2. `src/BlueCode.Cli/CompositionRoot.fs` — add `AllowPaths` to `CliOptions`, update `bootstrap` call
  3. `src/BlueCode.Cli/Adapters/FsToolExecutor.fs` — extend `validatePath`, update `create` signature
  4. `src/BlueCode.Cli/Program.fs` — parse `--allow-paths` flag and pass to opts
  5. `documentation/manual-test-guide.md` — update T-16/17/18/19/100/101 commands
- **Change size:** ~40-60 lines across files
- **New tests:** 4-6 tests in `FileToolsTests.fs` or new `AllowPathsTests.fs` — (a) extra path allowed, (b) prefix boundary (bc-test vs bc-testing), (c) traversal blocked even within allowed root, (d) empty extraAllowedPaths preserves current behaviour; 2-3 tests in `CliArgsTests.fs` — (a) `--allow-paths` parses correctly, (b) comma-splitting works
- **Regression risk:** MEDIUM — `FsToolExecutor.create` signature change requires all callers updated (compile-checked)

### Track 4: Hallucinated success investigation
- **Conclusion:** No code bug. Status chain is correct.
- **Files to change:** `documentation/manual-test-guide.md` only (update T-100 prompt to use project-root paths with `--allow-paths`)
- **New tests:** 0
- **Regression risk:** NONE (documentation only)

---

## Sources

### Primary (HIGH confidence)
- Direct file reads: `FsToolExecutor.fs`, `AgentLoop.fs`, `PlanValidator.fs`, `CliArgs.fs`, `CompositionRoot.fs`, `Rendering.fs`, `Program.fs`, `Domain.fs`
- fsi script executed at `/tmp/test-glob.fsx` — confirmed `*.fsproj` → 0 matches, `**/*.fsproj` → 3 matches against actual repo
- `documentation/manual-test-guide.md` — test execution results 2026-05-04
- `tests/BlueCode.Tests/PlanValidatorTests.fs`, `ToolExpansionTests.fs`, `CliArgsTests.fs`, `RouterTests.fs`

### Secondary (MEDIUM confidence)
- Phase description research questions — all answered by source file inspection
- CLAUDE.md — conventions verified against actual source

## Metadata

**Confidence breakdown:**
- Track 1 (glob): HIGH — fsi-verified root cause and fix
- Track 2 (PlanValidator UX): HIGH — code traced end-to-end; Core read-only constraint firm
- Track 3 (allow-paths): HIGH — existing pattern in validatePath is directly extensible
- Track 4 (hallucinated success): HIGH — no bug found in status chain; documented as model behaviour

**Research date:** 2026-05-04
**Valid until:** 2026-06-04 (stable domain — no fast-moving ecosystem)
