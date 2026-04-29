---
phase: 31-slash-command-core
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/BlueCode.Cli/SlashCommand.fs
  - src/BlueCode.Cli/BlueCode.Cli.fsproj
  - tests/BlueCode.Tests/SlashCommandTests.fs
  - tests/BlueCode.Tests/BlueCode.Tests.fsproj
  - tests/BlueCode.Tests/RouterTests.fs
autonomous: true

must_haves:
  truths:
    - "SlashCommand.parse \"/help\" returns Some (Slash Help)"
    - "SlashCommand.parse \"/exit\" and parse \"/quit\" both return Some (Slash Exit)"
    - "SlashCommand.parse \"\" (blank) returns None (caller will skip)"
    - "SlashCommand.parse \"hello world\" returns Some (Prompt \"hello world\")"
    - "SlashCommand.parse handles all 9 future commands (/help /status /clear /exit /quit /sessions /resume /plan /edit) without crashing"
    - "SlashCommandTests are picked up by the runTestsWithCLIArgs runner (full suite test count increases)"
  artifacts:
    - path: "src/BlueCode.Cli/SlashCommand.fs"
      provides: "SlashCommand DU + ParsedInput DU + parse function"
      contains: "module BlueCode.Cli.SlashCommand"
      contains_2: "type SlashCommand ="
      contains_3: "type ParsedInput ="
      contains_4: "let parse"
    - path: "tests/BlueCode.Tests/SlashCommandTests.fs"
      provides: "Parser unit tests (Category A — pure, no I/O)"
      contains: "module BlueCode.Tests.SlashCommandTests"
      contains_2: "let tests ="
  key_links:
    - from: "src/BlueCode.Cli/BlueCode.Cli.fsproj"
      to: "src/BlueCode.Cli/SlashCommand.fs"
      via: "<Compile Include=\"SlashCommand.fs\" /> AFTER Rendering.fs, BEFORE CompositionRoot.fs"
      pattern: "<Compile Include=\"SlashCommand.fs\""
    - from: "tests/BlueCode.Tests/BlueCode.Tests.fsproj"
      to: "tests/BlueCode.Tests/SlashCommandTests.fs"
      via: "<Compile Include=\"SlashCommandTests.fs\" /> BEFORE RouterTests.fs"
      pattern: "<Compile Include=\"SlashCommandTests.fs\""
    - from: "tests/BlueCode.Tests/RouterTests.fs"
      to: "BlueCode.Tests.SlashCommandTests.tests"
      via: "appended to rootTests list"
      pattern: "BlueCode\\.Tests\\.SlashCommandTests\\.tests"
---

<objective>
Phase 31 — Plan 01: Slash command parser and types (pure, no I/O).

Create the `BlueCode.Cli.SlashCommand` module containing the `SlashCommand` and `ParsedInput`
discriminated unions plus the pure `parse : string -> ParsedInput option` function. This is
the foundation that Plan 31-02 wires into `Repl.runMultiTurnWithSession`. Phases 32-35
(`/sessions`, `/resume <id>`, `/plan`, `/edit`) extend this DU; the parser must already
recognize all 9 commands now (returning the variants) so future phases only add dispatch
arms — they do NOT modify this file's parser.

Purpose: Establish the slash command surface area as a pure, exhaustively-matched DU so
the F# compiler will flag any unhandled variant when downstream phases add dispatch arms.
Pure function = trivially testable; the loop integration in Plan 31-02 is the only piece
that touches stdin/stdout.

Output:
- `src/BlueCode.Cli/SlashCommand.fs` (~50 LOC production)
- `tests/BlueCode.Tests/SlashCommandTests.fs` (~80 LOC, 13+ unit tests, no `testSequenced`)
- Both registered in their respective `.fsproj` files
- New test module added to `RouterTests.fs` `rootTests` list (this project does NOT use `[<Tests>]` auto-discovery — full suite is driven entirely by `rootTests`)
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/STATE.md
@.planning/phases/31-slash-command-core/31-RESEARCH.md
@CLAUDE.md
@src/BlueCode.Cli/BlueCode.Cli.fsproj
@tests/BlueCode.Tests/BlueCode.Tests.fsproj
@tests/BlueCode.Tests/RouterTests.fs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create SlashCommand.fs module with parser DU and parse function</name>
  <files>src/BlueCode.Cli/SlashCommand.fs, src/BlueCode.Cli/BlueCode.Cli.fsproj</files>
  <action>
1. Create `src/BlueCode.Cli/SlashCommand.fs` with this exact content (verbatim from research § Code Examples; the parser shape is HIGH-confidence locked):

```fsharp
module BlueCode.Cli.SlashCommand

/// All commands the parser recognizes. Future commands (Phase 32-34)
/// parse cleanly; Phase 31 dispatcher prints "not yet implemented" for
/// Sessions/Resume/Plan/Edit. The compiler will flag any match arm in
/// downstream phases that does not handle every variant.
type SlashCommand =
    | Help
    | Status
    | Clear
    | Exit          // /exit and /quit both map here (semantically identical)
    | Sessions      // Phase 32 — parse-only in Phase 31
    | Resume of id: string   // Phase 32 — parse-only in Phase 31; arg = "" if /resume typed alone
    | Plan          // Phase 33 — parse-only in Phase 31
    | Edit          // Phase 34 — parse-only in Phase 31

/// Result of parsing one REPL input line.
type ParsedInput =
    | Slash of SlashCommand
    | Prompt of string      // non-empty, non-slash — caller routes to LLM

/// Parse one raw REPL line into ParsedInput.
/// - Blank lines (after Trim) return None (caller skips them).
/// - Lines starting with '/' parse as slash commands.
/// - Unknown slash commands fall back to Help (safe default — shows the help text).
/// - Everything else returns Prompt (trimmed) for LLM dispatch.
/// Pure: no I/O, no side effects. Trivially unit-testable.
let parse (line: string) : ParsedInput option =
    let trimmed = line.Trim()
    if trimmed = "" then None
    elif trimmed.StartsWith("/") then
        let parts = trimmed.Split([| ' ' |], 2, System.StringSplitOptions.RemoveEmptyEntries)
        let cmd = parts.[0].ToLowerInvariant()
        let arg = if parts.Length > 1 then parts.[1].Trim() else ""
        let slashCmd =
            match cmd with
            | "/help"     -> Help
            | "/status"   -> Status
            | "/clear"    -> Clear
            | "/exit"
            | "/quit"     -> Exit
            | "/sessions" -> Sessions
            | "/resume"   -> Resume arg
            | "/plan"     -> Plan
            | "/edit"     -> Edit
            | _           -> Help    // unknown slash — show help (safe default)
        Some (Slash slashCmd)
    else
        Some (Prompt trimmed)
```

2. Edit `src/BlueCode.Cli/BlueCode.Cli.fsproj`. Insert `<Compile Include="SlashCommand.fs" />` BETWEEN the existing `<Compile Include="Rendering.fs" />` (line 17) AND `<Compile Include="CompositionRoot.fs" />` (line 18). After edit, the relevant block must read:

```xml
    <Compile Include="Rendering.fs" />
    <Compile Include="SlashCommand.fs" />
    <Compile Include="CompositionRoot.fs" />
    <Compile Include="PlanGate.fs" />
    <Compile Include="Repl.fs" />
    <Compile Include="CliArgs.fs" />
    <Compile Include="Program.fs" />
```

WHY this position: SlashCommand.fs has zero dependencies on CompositionRoot/PlanGate/Repl,
but Repl.fs (which imports it via `open BlueCode.Cli.SlashCommand` in Plan 31-02) is later
in the compile order. Insert AFTER Rendering.fs so SlashCommand can later reference RenderMode
if a future revision wants to (not needed in Phase 31; safe placement either way). Research §
"Pattern 1: Compile order" confirms this slot.

3. Verify the project still builds:
   ```
   dotnet build /Users/ohama/projs/blueCode/src/BlueCode.Cli/BlueCode.Cli.fsproj
   ```

4. After verification passes, commit:
   ```
   git add src/BlueCode.Cli/SlashCommand.fs src/BlueCode.Cli/BlueCode.Cli.fsproj
   git commit -m "feat(31-01): add SlashCommand parser module"
   ```

DO NOT use `git add -A` or `git add .` (CLAUDE.md invariant; sweeps `.claude/` and `localLLM/`).

DO NOT add this file to `src/BlueCode.Core/` — Cli-layer only (CLAUDE.md Core purity invariant).
The parser intentionally lives in the Cli adapter layer because slash commands are a REPL UX
concern, not a domain concern.
  </action>
  <verify>
- `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` exits 0 with no warnings about missing files.
- `grep -c "module BlueCode.Cli.SlashCommand" src/BlueCode.Cli/SlashCommand.fs` returns 1.
- `grep -c "<Compile Include=\"SlashCommand.fs\"" src/BlueCode.Cli/BlueCode.Cli.fsproj` returns 1.
- `grep -c "let parse " src/BlueCode.Cli/SlashCommand.fs` returns 1.
- `grep -A1 "Rendering.fs" src/BlueCode.Cli/BlueCode.Cli.fsproj | grep "SlashCommand.fs"` confirms ordering: SlashCommand immediately follows Rendering.
- Git log shows new commit: `git log -1 --oneline` contains `31-01` and `SlashCommand parser`.
  </verify>
  <done>
- `src/BlueCode.Cli/SlashCommand.fs` exists and exports `SlashCommand` (8 variants), `ParsedInput` (2 variants), and `parse : string -> ParsedInput option`.
- `BlueCode.Cli.fsproj` compile order is correct (SlashCommand.fs is at position 10, after Rendering.fs).
- `dotnet build` succeeds with no errors or warnings touching the new file.
- Atomic commit `feat(31-01): add SlashCommand parser module` exists.
- No file under `src/BlueCode.Core/` was modified (Core purity preserved).
  </done>
</task>

<task type="auto">
  <name>Task 2: Create SlashCommandTests.fs with 13 parser unit tests and register in BOTH .fsproj AND rootTests</name>
  <files>tests/BlueCode.Tests/SlashCommandTests.fs, tests/BlueCode.Tests/BlueCode.Tests.fsproj, tests/BlueCode.Tests/RouterTests.fs</files>
  <action>
**CRITICAL: Test discovery pitfall.** This project does NOT use `[<Tests>]` attribute
auto-discovery (CLAUDE.md, Research § Pitfall 2; four prior executor phases hit this).
The full test suite is driven by `runTestsWithCLIArgs [] args rootTests` in
`RouterTests.fs`. ANY new test module that is not added to BOTH:
  (a) `tests/BlueCode.Tests/BlueCode.Tests.fsproj` `<Compile Include>` (BEFORE `RouterTests.fs`)
  (b) `tests/BlueCode.Tests/RouterTests.fs` `rootTests` list
is silently skipped in the full suite. Symptom: tests compile but full-suite test count
unchanged.

This task addresses that pitfall explicitly with two registration steps below. Do NOT
skip either step.

1. Create `tests/BlueCode.Tests/SlashCommandTests.fs` with this content:

```fsharp
module BlueCode.Tests.SlashCommandTests

open Expecto
open BlueCode.Cli.SlashCommand

// Parser tests are pure (no I/O, no Console.SetOut), so testSequenced is NOT needed.
// Expecto's default parallelism is fine for these — they have no shared state.
//
// Note: NO [<Tests>] attribute. This project drives the full suite via the
// rootTests list in RouterTests.fs (see CLAUDE.md "Test discovery" invariant
// and Research § Pitfall 2). Registration in BOTH .fsproj AND rootTests is
// mandatory for these tests to run in the full suite.

let tests =
    testList "SlashCommand.parse" [

        testCase "/help -> Slash Help" <| fun () ->
            Expect.equal (parse "/help") (Some (Slash Help)) "/help maps to Help variant"

        testCase "/status -> Slash Status" <| fun () ->
            Expect.equal (parse "/status") (Some (Slash Status)) "/status maps to Status variant"

        testCase "/clear -> Slash Clear" <| fun () ->
            Expect.equal (parse "/clear") (Some (Slash Clear)) "/clear maps to Clear variant"

        testCase "/exit -> Slash Exit" <| fun () ->
            Expect.equal (parse "/exit") (Some (Slash Exit)) "/exit maps to Exit variant"

        testCase "/quit -> Slash Exit (alias of /exit)" <| fun () ->
            Expect.equal (parse "/quit") (Some (Slash Exit)) "/quit must collapse to same Exit variant as /exit"

        testCase "/sessions -> Slash Sessions (Phase 32 stub)" <| fun () ->
            Expect.equal (parse "/sessions") (Some (Slash Sessions)) "/sessions parses cleanly even though Phase 32 not shipped"

        testCase "/resume abc123 -> Slash (Resume \"abc123\")" <| fun () ->
            Expect.equal (parse "/resume abc123") (Some (Slash (Resume "abc123"))) "/resume captures id arg"

        testCase "/resume (no arg) -> Slash (Resume \"\")" <| fun () ->
            Expect.equal (parse "/resume") (Some (Slash (Resume ""))) "/resume with no arg captures empty string (dispatcher handles UX)"

        testCase "/plan -> Slash Plan (Phase 33 stub)" <| fun () ->
            Expect.equal (parse "/plan") (Some (Slash Plan)) "/plan parses cleanly"

        testCase "/edit -> Slash Edit (Phase 34 stub)" <| fun () ->
            Expect.equal (parse "/edit") (Some (Slash Edit)) "/edit parses cleanly"

        testCase "blank line -> None" <| fun () ->
            Expect.equal (parse "") None "empty string returns None — caller skips"

        testCase "whitespace-only line -> None" <| fun () ->
            Expect.equal (parse "   \t  ") None "whitespace-only returns None after Trim"

        testCase "regular prompt -> Prompt trimmed" <| fun () ->
            Expect.equal (parse "  hello world  ") (Some (Prompt "hello world")) "non-slash input returns Prompt with trimmed content"

        testCase "/HELP (uppercase) -> Slash Help (case-insensitive)" <| fun () ->
            Expect.equal (parse "/HELP") (Some (Slash Help)) "command lookup is ToLowerInvariant"

        testCase "/Help (mixed case) -> Slash Help" <| fun () ->
            Expect.equal (parse "/Help") (Some (Slash Help)) "mixed case also works"

        testCase "unknown /foo -> Slash Help (safe fallback)" <| fun () ->
            Expect.equal (parse "/foo") (Some (Slash Help)) "unknown slash falls back to Help (research § Pattern 1, safe default)"

        testCase "leading whitespace before slash is trimmed" <| fun () ->
            Expect.equal (parse "   /help") (Some (Slash Help)) "Trim happens before slash detection"
    ]
```

2. Edit `tests/BlueCode.Tests/BlueCode.Tests.fsproj`. Insert `<Compile Include="SlashCommandTests.fs" />` AFTER the existing `<Compile Include="SessionStoreTests.fs" />` line and BEFORE `<Compile Include="ToolExpansionTests.fs" />`. RouterTests.fs has `[<EntryPoint>]` and MUST remain last.

After edit, the test compile block must contain (in this order):
```xml
    <Compile Include="SessionStoreTests.fs" />
    <Compile Include="SlashCommandTests.fs" />
    <Compile Include="ToolExpansionTests.fs" />
    <Compile Include="RouterTests.fs" />
```

3. Edit `tests/BlueCode.Tests/RouterTests.fs`. Append `BlueCode.Tests.SlashCommandTests.tests`
to the `rootTests` list. The current last line of `rootTests` is:

```fsharp
          BlueCode.Tests.SessionStoreTests.tests ]         // NEW (15-03)
```

Change it to:

```fsharp
          BlueCode.Tests.SessionStoreTests.tests           // NEW (15-03)
          BlueCode.Tests.SlashCommandTests.tests ]         // NEW (Phase 31-01)
```

(Remove the closing `]` from the SessionStoreTests line and add it after SlashCommandTests.tests.)

4. Build and run the FULL test suite (not `--filter`):
   ```
   dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj
   ```
   This is the canonical test runner for this project (CLAUDE.md / STATE.md invariant; do NOT
   use `dotnet test`). Confirm:
   - Build succeeds (no F# compile errors).
   - All 17 SlashCommand.parse tests pass.
   - The full suite test count INCREASED by ≥17 vs. baseline — proves the new module is
     actually being executed (not silently skipped due to mis-registration).
   - No prior test regressed (full suite green).

5. Commit atomically:
   ```
   git add tests/BlueCode.Tests/SlashCommandTests.fs \
           tests/BlueCode.Tests/BlueCode.Tests.fsproj \
           tests/BlueCode.Tests/RouterTests.fs
   git commit -m "test(31-01): add SlashCommand parser unit tests"
   ```

DO NOT use `git add -A` or `git add .` (CLAUDE.md invariant).

DO NOT use `[<Tests>]` attribute on the testList — research § Pitfall 2 confirms this
project drives via rootTests; the attribute would be inert and is misleading to future
readers (the existing `RenderingTests.fs` has it as legacy noise, but new code should not
add it).

DO NOT wrap with `testSequenced` — these tests have NO `Console.SetOut` calls and no
shared state. `testSequenced` would slow them down for no benefit (Research § Q7
Category A).
  </action>
  <verify>
- `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0 (full suite green).
- Output contains "SlashCommand.parse" testList header and ≥17 passing test cases under it.
- `grep -c "BlueCode.Tests.SlashCommandTests.tests" tests/BlueCode.Tests/RouterTests.fs` returns 1 (registered in rootTests).
- `grep -c "<Compile Include=\"SlashCommandTests.fs\"" tests/BlueCode.Tests/BlueCode.Tests.fsproj` returns 1.
- The test count for the full suite is at least 17 higher than before this plan started (baseline ~287; expected ≥304 post-plan). Run `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj 2>&1 | grep -E "passed|failed"` and confirm.
- Git log shows two commits: `feat(31-01): add SlashCommand parser module` and `test(31-01): add SlashCommand parser unit tests`.
  </verify>
  <done>
- `SlashCommandTests.fs` contains 17 testCases covering: 9 commands × variants, blank/whitespace, regular prompt, case-insensitivity, unknown slash, leading whitespace.
- Module registered in `BlueCode.Tests.fsproj` `<Compile Include>` BEFORE `RouterTests.fs`.
- Module registered in `RouterTests.fs` `rootTests` list (last entry before closing `]`).
- Full test suite runs all 17 new tests + every prior test (no regressions, no silent skip).
- Two atomic commits exist with `(31-01)` scope.
  </done>
</task>

</tasks>

<verification>
After both tasks complete, run these gates:

1. **Build gate:** `dotnet build src/BlueCode.Cli/BlueCode.Cli.fsproj` and `dotnet build tests/BlueCode.Tests/BlueCode.Tests.fsproj` both exit 0.

2. **Test gate:** `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` exits 0; output shows ≥17 new SlashCommand.parse tests passing; full-suite count increased by ≥17.

3. **Core purity gate:** `git diff master -- src/BlueCode.Core/` is empty (no Core file modified).

4. **No-async gate:** `bash scripts/check-no-async.sh` exits 0 (SlashCommand.fs uses pure non-async functions only — no `task {}` or `async {}` either).

5. **Test discovery gate:** Manually verify the new module is in BOTH places:
   - `grep "SlashCommandTests.fs" tests/BlueCode.Tests/BlueCode.Tests.fsproj` returns the Compile line
   - `grep "SlashCommandTests.tests" tests/BlueCode.Tests/RouterTests.fs` returns the rootTests entry

6. **Atomic commits gate:** `git log --oneline -3` shows exactly two `31-01`-scoped commits (feat + test) — neither is amended, neither used `git add -A`.
</verification>

<success_criteria>
This plan succeeds when:

- [ ] `src/BlueCode.Cli/SlashCommand.fs` exists with `module BlueCode.Cli.SlashCommand`, exports `SlashCommand` DU (8 variants), `ParsedInput` DU (2 variants), `parse : string -> ParsedInput option`.
- [ ] `BlueCode.Cli.fsproj` `<Compile Include="SlashCommand.fs" />` is at compile position 10 (after Rendering.fs, before CompositionRoot.fs).
- [ ] `tests/BlueCode.Tests/SlashCommandTests.fs` exists with ≥17 testCases covering all 9 slash commands, edge cases, and case-insensitivity.
- [ ] `BlueCode.Tests.fsproj` `<Compile Include="SlashCommandTests.fs" />` is positioned BEFORE `RouterTests.fs` (which has `[<EntryPoint>]`).
- [ ] `RouterTests.fs` `rootTests` list contains `BlueCode.Tests.SlashCommandTests.tests` as last entry before closing `]`.
- [ ] `dotnet build` and `dotnet run --project tests/BlueCode.Tests/BlueCode.Tests.fsproj` both exit 0; full suite test count increased by ≥17.
- [ ] No file under `src/BlueCode.Core/**` modified (Core purity invariant from CLAUDE.md preserved).
- [ ] Two atomic commits exist with `(31-01)` scope, staged file-by-file (NEVER `git add -A`).
- [ ] No `[<Tests>]` attribute used on the new testList (research § Pitfall 2; project uses explicit rootTests).
- [ ] No `testSequenced` wrapper used (these tests have no Console.SetOut; testSequenced would be unnecessary serialization).

This plan UNBLOCKS Plan 31-02 (which depends on `BlueCode.Cli.SlashCommand` types and `parse`).
</success_criteria>

<output>
After completion, create `.planning/phases/31-slash-command-core/31-01-SUMMARY.md` documenting:

- Production LOC added (~50 in SlashCommand.fs)
- Test LOC added (~80 in SlashCommandTests.fs)
- Test count delta (e.g., 287 -> 304+)
- Two registrations confirmed (.fsproj + rootTests)
- Frontmatter to include: `requires: [31-01]` is empty (this is a root plan), `affects: [31-02]` (downstream plan needs SlashCommand types).
- Any deviations from the plan (should be none — research is HIGH confidence)
</output>
