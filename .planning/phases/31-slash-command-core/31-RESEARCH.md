# Phase 31: SLASH Command Core - Research

**Researched:** 2026-04-29
**Domain:** F# CLI REPL slash command parser + dispatcher (Cli-layer only, no new dependencies)
**Confidence:** HIGH

## Summary

Phase 31 adds a slash command subsystem to the existing multi-turn REPL in `src/BlueCode.Cli/`. All implementation is Cli-layer only — Core (`src/BlueCode.Core/`) is read-only for this phase. No new NuGet packages are needed; the phase uses only the existing F# standard library, Spectre.Console (already present), and Expecto (already in tests).

The existing REPL loop in `Repl.fs` already handles `"/exit"` as a literal string match (line 185 of `Repl.fs`). Phase 31 systematically replaces that ad-hoc match with a proper parser + dispatcher that handles all 4 in-scope commands and stubs the 5 future commands. The parser produces a discriminated union `ParsedInput` that the loop dispatches before calling `runSingleTurn`. All state the dispatcher needs is already threaded through the loop as mutable `let mutable` vars (or derivable from `AppComponents`).

The `/status` display requires care: `MaxModelLen` in `AppComponents` is a fixed floor of 8192 (not the real per-port value). The real value lives inside the `Lazy<Task<ModelInfo>>` cells inside the `QwenHttpClient` closure — not surfaced to `AppComponents` in v2.0. For Phase 31, `/status` should use `components.MaxModelLen` (the floor) with a note that it reflects the startup floor, not the live probed value. Awaiting the lazy probe in a slash command would introduce an HTTP call that may block for 300s; that is out of scope for a meta-control command.

**Primary recommendation:** Introduce `SlashCommand.fs` (parser + pure dispatcher data types) before `Rendering.fs` in the `.fsproj` compile order, then integrate the dispatcher into `runMultiTurnWithSession` in `Repl.fs`. Keep the dispatcher pure (returns a value, does not call `Console.ReadLine` or `Environment.Exit`); let the loop drive all I/O side effects.

## Standard Stack

No new NuGet packages. All tooling already in the project:

### Core
| Component | Source | Purpose | Already Present |
|-----------|--------|---------|-----------------|
| F# DU + pattern match | Language | Parser + dispatcher | Yes |
| Spectre.Console 0.55.2 | `BlueCode.Cli.fsproj` | `renderHelp`/`renderStatus` formatting via `printfn` (not `AnsiConsole`) | Yes |
| Expecto 10.2.1 | `BlueCode.Tests.fsproj` | Unit tests for parser + rendering | Yes |

### No Alternatives Needed

This is a pure code addition in F#. The parser is 10–15 lines of string matching; no parser combinator library is needed or appropriate at this size. Hand-rolling is correct here (see "Don't Hand-Roll" section for what NOT to hand-roll).

**Installation:** None required.

## Architecture Patterns

### Recommended Project Structure After Phase 31

```
src/BlueCode.Cli/
├── Adapters/            # (unchanged)
├── Rendering.fs         # + renderHelp, renderStatus (new functions)
├── SlashCommand.fs      # NEW: parser types + parse function (pure)
├── CompositionRoot.fs   # (unchanged)
├── PlanGate.fs          # (unchanged)
├── Repl.fs              # MODIFIED: dispatcher integrated into runMultiTurnWithSession
├── CliArgs.fs           # (unchanged)
└── Program.fs           # (unchanged)
```

**Compile order in `BlueCode.Cli.fsproj`:** `SlashCommand.fs` must come AFTER `Rendering.fs` (it references `RenderMode`) but BEFORE `Repl.fs`. Insert between `Rendering.fs` and `CompositionRoot.fs` or between `PlanGate.fs` and `Repl.fs` — either works; recommended: between `Rendering.fs` and `CompositionRoot.fs` so the type is available to anything above it.

Current fsproj compile order (from `BlueCode.Cli.fsproj`):
1. `Adapters/LlmWire.fs`
2. `Adapters/Json.fs`
3. `Adapters/QwenHttpClient.fs`
4. `Adapters/BashSecurity.fs`
5. `Adapters/FsToolExecutor.fs`
6. `Adapters/Logging.fs`
7. `Adapters/JsonlSink.fs`
8. `Adapters/FileSessionStore.fs`
9. `Rendering.fs`
10. `CompositionRoot.fs`
11. `PlanGate.fs`
12. `Repl.fs`
13. `CliArgs.fs`
14. `Program.fs`

**Insert `SlashCommand.fs` at position 10** (after `Rendering.fs`, before `CompositionRoot.fs`). It does not depend on `CompositionRoot` types; `Repl.fs` depends on it.

### Pattern 1: ParsedInput Discriminated Union

**What:** A single-case-per-command DU with a fallthrough `Prompt` case for regular LLM input.
**When to use:** After every `Console.ReadLine()` call, before any dispatch.

```fsharp
// src/BlueCode.Cli/SlashCommand.fs
module BlueCode.Cli.SlashCommand

/// All commands the parser recognizes. Future commands (Phase 32-34)
/// parse cleanly but dispatch returns a "not yet implemented" message.
type SlashCommand =
    | Help
    | Status
    | Clear
    | Exit          // /exit and /quit both map here
    | Sessions      // Phase 32 — parse only, no-op in Phase 31
    | Resume of id: string   // Phase 32 — parse only
    | Plan          // Phase 33 — parse only
    | Edit          // Phase 34 — parse only

/// Result of parsing one REPL input line.
type ParsedInput =
    | Slash of SlashCommand
    | Prompt of string      // non-empty, non-slash — route to LLM

/// Parse one raw REPL line into ParsedInput.
/// Blank lines return None (caller skips them).
/// Lines starting with '/' are parsed as slash commands; unknown slash
/// commands return a Slash with an Unknown stub (see below).
let parse (line: string) : ParsedInput option =
    let trimmed = line.Trim()
    if trimmed = "" then None
    elif trimmed.StartsWith("/") then
        let parts = trimmed.Split([|' '|], 2, System.StringSplitOptions.RemoveEmptyEntries)
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
            | _           -> Help  // unknown slash → show help (safe default)
        Some (Slash slashCmd)
    else
        Some (Prompt trimmed)
```

**Note:** The unknown-slash fallback to `Help` is a UX choice. The planner may instead want `| _ -> Some (Slash (Unknown cmd))` with a separate `Unknown of string` case for better error messaging. Either is fine; the pure-function boundary makes this easy to change.

### Pattern 2: Loop Action Return Type

**What:** Dispatcher returns a value the loop acts on; does not call `Environment.Exit` or `Console.ReadLine` itself.
**Why:** Pure function = trivially testable; no I/O mocking needed.

```fsharp
// src/BlueCode.Cli/SlashCommand.fs (continued)

/// What the REPL loop should do after dispatching a slash command.
type LoopAction =
    | Continue          // keep looping (most commands)
    | ExitNormal        // /exit or /quit — loop should break, return 0
    | ClearSession      // /clear — caller resets priorSteps + creates new session id
```

### Pattern 3: Dispatcher Integration in runMultiTurnWithSession

**What:** The existing loop body in `Repl.fs` `runMultiTurnWithSession` (lines 179-205) currently has:

```fsharp
match line with
| null -> running <- false
| "/exit" -> running <- false
| s when s.Trim() = "" -> ()
| prompt ->
    let! (code, newSteps) = runSingleTurn prompt ...
    ...
```

**After Phase 31**, this becomes:

```fsharp
match line with
| null -> running <- false
| _ ->
    match SlashCommand.parse line with
    | None -> ()   // blank line, skip
    | Some (Slash Exit) ->
        running <- false
    | Some (Slash Help) ->
        printfn "%s" Rendering.renderHelp
    | Some (Slash Status) ->
        printfn "%s" (Rendering.renderStatus currentSession components renderMode)
    | Some (Slash Clear) ->
        let newId = BlueCode.Cli.Adapters.FileSessionStore.newSessionId ()
        let now = DateTimeOffset.UtcNow
        currentSession <- { Id = newId; Steps = []; CreatedAt = now; LastActivityAt = now }
        printfn "Session cleared. New session: %s" (let (SessionId id) = newId in id)
    | Some (Slash (Sessions | Resume _ | Plan | Edit)) ->
        printfn "(not yet implemented — coming in a future v2.5 phase)"
    | Some (Prompt prompt) ->
        let! (code, newSteps) = runSingleTurn prompt currentSession.Steps components renderMode
        ...
```

**The loop does NOT call `sessionStore.Save` on `/clear`** — the new session has 0 steps; saving an empty session header is optional but not required by the success criterion ("FileSessionStore 에 새 session 시작" means the new session JSONL is created lazily on the first completed turn save, which is the existing behavior of `FileSessionStore.Save` — it creates the file if it does not exist).

### Anti-Patterns to Avoid

- **Calling `Environment.Exit` inside the dispatcher:** Makes testing impossible. Return `ExitNormal` from dispatcher; let the loop call `running <- false` and fall through to `return lastCode`.
- **Awaiting the `Lazy<Task<ModelInfo>>` probe in `/status`:** This can block for 300s if the model server is cold-starting. Use `components.MaxModelLen` (the 8192 floor) with a label like `"(floor; probed on first LLM call)"`.
- **Using `AnsiConsole.MarkupLine` in `renderHelp`/`renderStatus`:** These functions will be captured in tests via `Console.SetOut`. `AnsiConsole` bypasses `Console.SetOut` (confirmed in CLAUDE.md). Use `printfn` or return a `string` and let the caller `printfn` it.
- **Using `sprintf "[122B]"` in Spectre output labels:** Spectre parses `[122B]` as a color tag; escape with `[[122B]]`. This only matters if Spectre helpers are used — for `renderHelp`/`renderStatus` using `printfn`, no escaping is needed.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Session ID generation | Custom UUID generator | `Guid.NewGuid().ToString("N")` (already in `FileSessionStore.newSessionId`) | Already exists at `FileSessionStore.newSessionId ()`; call it directly |
| JSONL session path | Custom path builder | `FileSessionStore.buildSessionPath` | Already implemented; "untouched existing JSONL" is automatic because you just stop writing to the old path |
| Command-line argument parser | Custom tokenizer | `line.Split([|' '|], 2, ...)` for the slash arg | Slash commands have at most one argument (`/resume <id>`); a two-part split is sufficient and correct |
| Rich table formatting for `/help` | Spectre `Table` | Plain `printfn` with aligned columns | Simpler, testable via `Console.SetOut`, avoids Spectre markup escape complexity |

**Key insight:** All the infrastructure this phase needs already exists in the project. The work is wiring existing pieces, not building new systems.

## Q1 — Parser Shape

**Recommendation:** Use a `SlashCommand` DU (one variant per command) and a `ParsedInput` DU with `Slash of SlashCommand | Prompt of string`. See Pattern 1 above.

**Why not a string + args list:** A DU makes exhaustive matching mandatory at compile time. When Phase 32 adds `Sessions` and `Resume`, the compiler will flag any match arm that does not handle them. This is the F# idiom and matches the existing codebase's style (see `AgentError`, `LlmOutput`, `ToolResult` — all DUs).

**`/exit` and `/quit` collapse:** Map both to `Exit` in the parser (single `| "/exit" | "/quit" -> Exit` arm). No need for a `Quit` variant — they are semantically identical.

**`/resume <id>` composition:** `Resume of id: string` carries the argument. An empty string (`/resume` with no arg) is caught at dispatch time: if `id = ""`, print "Usage: /resume <session-id>" without changing session state.

**Parser signature:**
```fsharp
val parse : string -> ParsedInput option
// None = blank line (caller skips)
// Some (Slash _) = slash command
// Some (Prompt _) = regular LLM prompt
```

## Q2 — Dispatcher Integration in Repl.fs

**Where to hook:** In `runMultiTurnWithSession`, after `let line = Console.ReadLine()`, replace the current `match line with | ... | "/exit" -> ...` block with `SlashCommand.parse line` dispatch. See Pattern 3 above.

**State the dispatcher needs access to:**

| State | Source | Notes |
|-------|--------|-------|
| `currentSession` (Session) | `let mutable currentSession` in loop | For `/status` (session id, step count) and `/clear` (create new) |
| `components` (AppComponents) | Function parameter | For `/status` (model, MaxModelLen) |
| `renderMode` | Function parameter | Passed through to `renderStatus` for consistency |
| `sessionStore` | ISessionStore parameter | `/clear` does NOT need to call `Save` (see below) |

**State NOT needed in the dispatcher:** `priorSteps` is now `currentSession.Steps` in `runMultiTurnWithSession` (the loop already uses `currentSession.Steps` as the prior steps for `runSingleTurn`). After `/clear`, set `currentSession <- freshSession` and the next `runSingleTurn` call will automatically use `[]` as prior steps.

**Data flow recommendation:** Do NOT extract a separate `dispatch` function that takes all state as parameters and returns `(Session * LoopAction * string list)`. That pattern is useful when the dispatcher needs to be tested independently of the loop, but in this codebase the loop is already testable via the existing `runMultiTurnWithSession` test harness in `ReplTests.fs`. Keep the dispatch inline in the loop body (a `match SlashCommand.parse line with` arm). The `SlashCommand.parse` function is the unit-testable pure piece; the loop integration is tested via the existing `ReplTests` pattern.

## Q3 — `/status` Data Sources

| Field (SLASH-02) | Source | Notes |
|-----------------|--------|-------|
| session id | `let (SessionId id) = currentSession.Id in id` | Available in loop state |
| model name | `components.Config.ForcedModel` | `Some Qwen122B -> "122B"`, `Some Qwen35B -> "35B"`, `None -> "122B (default)"` |
| current turn step count | `currentSession.Steps.Length` | All steps accumulated across turns in this session |
| accumulated char count | Sum of step repr lengths | See below |
| 32k context % | `accumulatedChars / (components.MaxModelLen * 4) * 100` | Use `components.MaxModelLen` (8192 floor); label as floor |

**"Accumulated chars" definition:** The existing `runSingleTurn` accumulates `totalChars` per-turn using `sprintf "%A" step.Action + sprintf "%A" step.ToolResult`. For `/status`, a simple proxy is:

```fsharp
let estimatedChars =
    currentSession.Steps
    |> List.sumBy (fun s ->
        (sprintf "%A" s.Action).Length + (sprintf "%A" s.ToolResult).Length)
```

This matches the in-turn accumulation logic already in `runSingleTurn` (lines 91-93 of `Repl.fs`). It is an estimate, not exact token count, which is consistent with the `shouldWarnContextWindow` heuristic already shipped.

**32k context %:** The requirement says "32k context" but `MaxModelLen` is 8192 tokens (floor), and 32k tokens = 131072 chars (at ~4 chars/token). Use `components.MaxModelLen` for the denominator:
```fsharp
let contextPct = (estimatedChars * 100) / (components.MaxModelLen * 4)
```

If `/status` is called before any LLM call, the probe has not fired, so `components.MaxModelLen` is 8192. Label the output: `"context: %d chars / ~%d chars (%d%%) [floor; actual probed on first LLM call]"`.

**Model name source:** `components.Config.ForcedModel` is `Model option` (from `AgentConfig`). In practice it is always `Some Qwen122B` in single-model canonical mode. Map it:
```fsharp
let modelName =
    match components.Config.ForcedModel with
    | Some Qwen122B -> "122b"
    | Some Qwen35B -> "35b"
    | None -> "122b (default)"
```

## Q4 — `/clear` Semantics

**"FileSessionStore 에 새 session 시작; 기존 session 의 jsonl 은 untouched":**

The existing `FileSessionStore.Save` (lines 61-77 of `FileSessionStore.fs`) creates a new JSONL file at `~/.bluecode/sessions/<id>.jsonl` only when the file does not exist (`let isNew = not (File.Exists path)`). It writes the header on first save, then appends envelopes.

After `/clear`, the loop state becomes:
```fsharp
let newId = FileSessionStore.newSessionId ()   // Guid.NewGuid().ToString("N")
let now = DateTimeOffset.UtcNow
currentSession <- { Id = newId; Steps = []; CreatedAt = now; LastActivityAt = now }
```

The OLD session's `.jsonl` is at `~/.bluecode/sessions/<old-id>.jsonl` — it is never touched again because the loop now uses `newId`. The new session's `.jsonl` is created lazily on the first `sessionStore.Save updated` call after the next completed turn. This fully satisfies "기존 session 의 jsonl 은 untouched" without any special logic.

**`/clear` should NOT call `sessionStore.Save`** for the new empty session — there is nothing to persist yet (no steps), and creating an empty-header JSONL with no envelopes is technically fine but pointless. The existing behavior is: file created on first Save after session genesis.

**`/clear` output:** Print a confirmation line:
```
Session cleared. New session: <new-id>
```
Use `printfn` (not `AnsiConsole`) so it is captured by `Console.SetOut` in tests.

**Session ID format:** `Guid.NewGuid().ToString("N")` = 32-char lowercase hex (e.g., `a3f7b2c1d0e4f5a6b7c8d9e0f1a2b3c4`). This is what `FileSessionStore.newSessionId` already produces (line 48 of `FileSessionStore.fs`).

## Q5 — `/exit` and `/quit` Graceful Exit

**Existing exit path:** In `runMultiTurnWithSession` (line 185 of `Repl.fs`):
```fsharp
| "/exit" -> running <- false
```
The loop then falls through to `return lastCode`. No explicit `Environment.Exit` call. The `use _jsonlSink = components.JsonlSink` in `Program.fs` disposes the sink when the function returns.

**"Auto-saved":** `FileSessionStore` saves after EVERY completed turn (line 197 of `Repl.fs`: `let! saveRes = sessionStore.Save updated ...`). So when `/exit` breaks the loop, the session is already saved from the last completed turn. There is no "flush on exit" needed.

**Recommendation for Phase 31:** Keep the same pattern. When `SlashCommand.parse line = Some (Slash Exit)`, set `running <- false`. Do NOT call `Environment.Exit 0` — the loop returns `lastCode` naturally, `Program.fs` calls `Log.CloseAndFlush()` and returns the exit code. This is cleaner than `Environment.Exit` which bypasses `finally` blocks and Serilog flush.

**`/quit` behavior:** Identical to `/exit` — both map to `Exit` in the parser; the loop sets `running <- false`.

**Loop action signal:** No need for a `LoopAction` type if the dispatcher is inlined in the loop body (recommended). The `| Exit -> running <- false` arm is three tokens. If the planner wants a pure extracted dispatcher for testability, use `LoopAction = Continue | ExitNormal | ClearSession` and map in the loop.

## Q6 — `/help` Content: Exact 9-Command List

The success criterion says "9 commands list — 현재 milestone 의 7 + future-stub". Count:

1. `/help` — show this help
2. `/status` — session id, model, steps, context usage
3. `/clear` — reset session, start fresh
4. `/exit` — save and quit
5. `/quit` — alias for /exit
6. `/sessions` — list recent sessions (coming in v2.5)
7. `/resume <id>` — switch to a saved session (coming in v2.5)
8. `/plan` — toggle plan-mode for next turn (coming in v2.5)
9. `/edit` — open $EDITOR for multi-line input (coming in v2.5)

Total: 9 distinct command tokens (counting `/exit` and `/quit` separately). This matches the success criterion.

**Recommended `/help` output format:**
```
slash commands:
  /help              show this help
  /status            session info: id, model, steps, context %
  /clear             reset session in-place (new session id, keep REPL running)
  /exit              save session and quit
  /quit              alias for /exit
  /sessions          list recent sessions [coming in v2.5]
  /resume <id>       switch to a saved session [coming in v2.5]
  /plan              toggle plan-mode for next turn [coming in v2.5]
  /edit              open $EDITOR for multi-line input [coming in v2.5]
```

Use `printfn` to render — the string can be assembled in `Rendering.renderHelp : string` (no parameters needed) and `printfn "%s" (Rendering.renderHelp)` in the loop.

## Q7 — Test Strategy

### Where Tests Live

New file: `tests/BlueCode.Tests/SlashCommandTests.fs`

**MANDATORY two-step registration (four prior executors hit this):**
1. Add `<Compile Include="SlashCommandTests.fs" />` to `tests/BlueCode.Tests/BlueCode.Tests.fsproj`, positioned BEFORE `RouterTests.fs` (which has `[<EntryPoint>]`). Insert after `SessionStoreTests.fs` and before `ToolExpansionTests.fs`.
2. Add `BlueCode.Tests.SlashCommandTests.tests` to the `rootTests` list in `RouterTests.fs`.

### Test Categories

**Category A — Pure parser tests (no I/O, no `testSequenced` needed):**
```fsharp
// SlashCommandTests.fs
let tests =
    testList "SlashCommand" [
        testCase "parse /help -> Slash Help" ...
        testCase "parse /status -> Slash Status" ...
        testCase "parse /clear -> Slash Clear" ...
        testCase "parse /exit -> Slash Exit" ...
        testCase "parse /quit -> Slash Exit (same variant)" ...
        testCase "parse /sessions -> Slash Sessions" ...
        testCase "parse /resume abc123 -> Slash (Resume \"abc123\")" ...
        testCase "parse /plan -> Slash Plan" ...
        testCase "parse /edit -> Slash Edit" ...
        testCase "parse blank line -> None" ...
        testCase "parse regular prompt -> Prompt _" ...
        testCase "parse /HELP (uppercase) -> Slash Help" ...  // case-insensitive
        testCase "parse /resume (no arg) -> Slash (Resume \"\")" ...
    ]
```

These tests have no I/O; they do NOT need `testSequenced`.

**Category B — `renderHelp`/`renderStatus` output tests (use `Console.SetOut`):**

These belong in `RenderingTests.fs` (existing file) OR in `SlashCommandTests.fs` with `testSequenced`. The existing `ReplTests.fs` is already `testSequenced`. If added to `SlashCommandTests.fs`, wrap the whole `testList` with `testSequenced`:

```fsharp
let tests =
    testSequenced <| testList "SlashCommand" [
        // parser tests (no I/O) — safe to include in testSequenced
        // rendering tests (Console.SetOut) — require testSequenced
        testCase "renderHelp contains /help" <| fun () ->
            let help = Rendering.renderHelp
            Expect.stringContains help "/help" "help text must mention /help"
            Expect.stringContains help "/exit" "help text must mention /exit"
        testCase "renderStatus contains session id" <| fun () ->
            let original = Console.Out
            use sw = new StringWriter()
            Console.SetOut(sw)
            try
                // call Rendering.renderStatus with stub session
                ...
            finally
                Console.SetOut(original)
    ]
```

**Recommendation:** Put parser tests (Category A) in `SlashCommandTests.fs` without `testSequenced`. Put `renderHelp`/`renderStatus` string tests in `RenderingTests.fs` (existing, already `testSequenced` — check if it already wraps with `testSequenced`; if not, add it). This avoids unnecessary serialization of fast pure parser tests.

**Category C — Repl integration tests:**

The existing `ReplTests.fs` tests `/exit` indirectly (the `runMultiTurn: stdin '/exit' exits cleanly` test, line 119). Phase 31 should add:
- `runMultiTurn: '/help' prints help without LLM call` — stdin `/help\n/exit\n`, assert stdout contains "/help" help text, LLM stub gets 0 calls
- `runMultiTurn: '/clear' resets session id` — stdin `/clear\n/exit\n`, capture the printed new session id, assert it differs from initial
- `runMultiTurn: '/status' prints session info` — stdin `/status\n/exit\n`, assert stdout contains session id substring

These go in `ReplTests.fs` (already `testSequenced`).

## Q8 — Pitfalls and Risks

### Pitfall 1: Spectre.Console Markup Parsing in Model Name Display
**What goes wrong:** If `renderStatus` uses `AnsiConsole.MarkupLine` (or any Spectre call) with a string containing `[122B]`, Spectre parses `[122B]` as a color/style tag and throws or silently drops the text.
**Why it happens:** Spectre markup syntax uses `[color]` for styling; `[` is reserved.
**How to avoid:** Use `printfn` for all slash command output (not `AnsiConsole`). If Spectre is ever used, escape with `[[122B]]`. The existing `QwenHttpClient.fs` already has this escape at line 492 (`"Thinking... [[%s]]" modelLabel`).
**Warning signs:** `Markup`-related exceptions or missing text in model name fields.

### Pitfall 2: Test Discovery Failure (HIGH PROBABILITY without explicit registration)
**What goes wrong:** `SlashCommandTests.fs` compiles and runs in isolation (`--filter`) but is silently skipped in the full suite.
**Why it happens:** This project does NOT use `[<Tests>]` auto-discovery. New test modules MUST be registered in BOTH the `.fsproj` compile list AND the `rootTests` list in `RouterTests.fs`. Four previous executor phases hit this exact pitfall (documented in CLAUDE.md and `RouterTests.fs` comment at line 38-39 of `ReplTests.fs`).
**How to avoid:** After creating `SlashCommandTests.fs`, immediately add it to both locations. Verify with `dotnet test` full suite (not `--filter`).
**Warning signs:** "Tests compile but don't run" — full suite shows 0 tests from the new module.

### Pitfall 3: `Console.SetOut` Race in Tests
**What goes wrong:** Slash command rendering tests pass individually but flake in the full suite.
**Why it happens:** Expecto runs `testList` items in parallel by default. `Console.Out` is process-global state.
**How to avoid:** Wrap any `testList` containing `Console.SetOut` calls with `testSequenced`. The `ReplTests.fs` already does this (line 43: `testSequenced <| testList`). Any new test module doing the same must also use `testSequenced`.
**Warning signs:** Flaky failures with `ObjectDisposedException` or empty captured strings; pass rate < 100% on repeated runs.

### Pitfall 4: `priorSteps` Reset on `/clear` — Missing State Reset
**What goes wrong:** `/clear` sets `currentSession.Steps = []` but some other state in the loop retains the old session's data.
**Why it happens:** If future phases add loop-level mutable state (e.g., a char counter for `/status`), resetting only `currentSession` leaves stale data.
**How to avoid:** In Phase 31, `currentSession` is the only session state. The per-turn `totalChars` and `warnedThisTurn` accumulators in `runSingleTurn` are local to that function and reset naturally on each call. There is no cross-turn mutable state in the loop other than `currentSession`.
**Warning signs:** `/status` after `/clear` shows old step count or char count.

### Pitfall 5: Phase 35 Compatibility (Future-Proofing)
**What goes wrong:** Phase 35 replaces `Console.ReadLine()` with PrettyPrompt. If slash command dispatch is tied to the string-level input (which it is), it must work regardless of where the string comes from.
**How to avoid:** The `SlashCommand.parse` function takes a `string` — it does not care how the string was obtained. The loop body calls `parse line` where `line` is whatever `Console.ReadLine()` (or PrettyPrompt) returned. No change needed in Phase 35 for the parser. Confirmed by ROADMAP.md line 91: "parser 영향 없음".
**Warning signs:** None for Phase 31 — this is a future-phase concern, but the recommended architecture already handles it.

### Pitfall 6: `async {}` in SlashCommand.fs
**What goes wrong:** CI fails if any `async {}` CE appears in `src/BlueCode.Core/`.
**Scope:** This is Cli-layer only (`src/BlueCode.Cli/`); the ban applies to Core only. `SlashCommand.fs` is in Cli. However, keep consistency: use `task {}` if async work is ever needed here (not expected for Phase 31 — all dispatcher logic is synchronous).
**Warning signs:** `scripts/check-no-async.sh` failure.

## Code Examples

### Parser (source: derived from existing codebase patterns)

```fsharp
// src/BlueCode.Cli/SlashCommand.fs
module BlueCode.Cli.SlashCommand

type SlashCommand =
    | Help
    | Status
    | Clear
    | Exit
    | Sessions
    | Resume of id: string
    | Plan
    | Edit

type ParsedInput =
    | Slash of SlashCommand
    | Prompt of string

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
            | _           -> Help
        Some (Slash slashCmd)
    else
        Some (Prompt trimmed)
```

### `/status` render (pure string, no I/O)

```fsharp
// src/BlueCode.Cli/Rendering.fs — new function
let renderStatus (session: Session) (components: AppComponents) : string =
    let (SessionId idStr) = session.Id
    let modelName =
        match components.Config.ForcedModel with
        | Some Qwen122B -> "122b"
        | Some Qwen35B  -> "35b"
        | None          -> "122b (default)"
    let steps = session.Steps.Length
    let accChars =
        session.Steps
        |> List.sumBy (fun s ->
            (sprintf "%A" s.Action).Length + (sprintf "%A" s.ToolResult).Length)
    let maxChars = components.MaxModelLen * 4   // tokens * ~4 chars/token
    let pct = if maxChars > 0 then accChars * 100 / maxChars else 0
    sprintf
        "session:  %s\nmodel:    %s\nsteps:    %d\nchars:    %d / ~%d (%d%%) [floor; probed on first LLM call]"
        idStr modelName steps accChars maxChars pct
```

### `/clear` in loop

```fsharp
// src/BlueCode.Cli/Repl.fs — inside runMultiTurnWithSession match arm
| Some (Slash Clear) ->
    let newId = BlueCode.Cli.Adapters.FileSessionStore.newSessionId ()
    let now = DateTimeOffset.UtcNow
    currentSession <- { Id = newId; Steps = []; CreatedAt = now; LastActivityAt = now }
    let (SessionId idStr) = newId
    printfn "Session cleared. New session: %s" idStr
```

### Test registration (BlueCode.Tests.fsproj, after SessionStoreTests.fs)

```xml
<Compile Include="SessionStoreTests.fs" />
<Compile Include="SlashCommandTests.fs" />   <!-- ADD HERE -->
<Compile Include="ToolExpansionTests.fs" />
<Compile Include="RouterTests.fs" />
```

### Test registration (RouterTests.fs rootTests, after SessionStoreTests.tests)

```fsharp
BlueCode.Tests.ContextWarningTests.tests
BlueCode.Tests.SessionStoreTests.tests
BlueCode.Tests.SlashCommandTests.tests ]   // ADD HERE (last before closing bracket)
```

## Plan/Wave Breakdown Recommendation

### Plan 31-01: `SlashCommand.fs` — Parser and Types (Wave 1)

**Scope:**
- Create `src/BlueCode.Cli/SlashCommand.fs` with `SlashCommand` DU, `ParsedInput` DU, `parse` function
- Add to `BlueCode.Cli.fsproj` compile order (after `Rendering.fs`)
- Create `tests/BlueCode.Tests/SlashCommandTests.fs` with parser unit tests (Category A)
- Register in `.fsproj` and `rootTests`

**Wave:** 1 (autonomous — no dependencies within phase)
**Estimated LOC:** ~50 production + ~80 test
**Tests:** 13 parser unit tests, no `testSequenced` needed

### Plan 31-02: Rendering functions and Repl integration (Wave 2, depends on 31-01)

**Scope:**
- Add `renderHelp : string` and `renderStatus : Session -> AppComponents -> string` to `Rendering.fs`
- Integrate `SlashCommand.parse` into `runMultiTurnWithSession` in `Repl.fs` (replace existing `/exit` literal match)
- Add Repl integration tests to `ReplTests.fs`: `/help`, `/clear`, `/status`, `/quit` cases
- Run `bench/run.sh --gate` and assert 7/7 PASS

**Wave:** 2 (depends on 31-01 for `SlashCommand.parse` and type definitions)
**Estimated LOC:** ~60 production (Rendering + Repl changes) + ~80 test
**Tests:** 4+ integration tests in `ReplTests.fs` (already `testSequenced`); rendering string tests in `RenderingTests.fs` or `SlashCommandTests.fs`

### Plan 31-03: NOT needed as a separate plan

Bench gate verification and integration test coverage belong in 31-02's verification step. Creating a third plan for verification alone would be overhead without value. The planner should merge the bench gate check into 31-02's success criteria.

**Summary: 2 plans, 2 waves.**

## Open Questions

1. **`renderStatus` signature — thread `renderMode` or not?**
   - What we know: `renderMode` (Compact/Verbose) affects step display in `runSingleTurn` but there is no obvious compact vs. verbose for `/status`
   - What's unclear: Should `/status` be more terse in Compact mode?
   - Recommendation: Ignore `renderMode` for `/status` — always show all fields. Simplest correct behavior; can add compact mode later.

2. **Unknown slash command behavior**
   - What we know: The parser currently falls back to `Help` for unknown commands (`| _ -> Help`)
   - What's unclear: Should unknown `/foo` print "Unknown command: /foo" then help, or silently show help?
   - Recommendation: Add an `Unknown of string` variant to `SlashCommand` and print `"Unknown command: /foo. Type /help for available commands."` This is more user-friendly than silently showing help.

3. **`/resume ""` (no arg) behavior**
   - What we know: `Resume of id: string` carries empty string when `/resume` is typed with no arg
   - What's unclear: Handled at dispatch time or parse time?
   - Recommendation: Dispatch time — the Phase 32 handler will print "Usage: /resume <session-id>". For Phase 31, the `| Some (Slash (Resume _)) -> printfn "(not yet implemented...)"` arm handles it uniformly.

## Sources

### Primary (HIGH confidence)
- Direct code reading: `src/BlueCode.Cli/Repl.fs` (all 228 lines) — loop structure, exit path, state threading
- Direct code reading: `src/BlueCode.Cli/CompositionRoot.fs` — `AppComponents` shape, `MaxModelLen = 8192` floor comment
- Direct code reading: `src/BlueCode.Cli/Adapters/FileSessionStore.fs` — `newSessionId`, `buildSessionPath`, Save/Load behavior
- Direct code reading: `src/BlueCode.Core/Domain.fs` — `Session`, `Step`, `SessionId`, `Model` types
- Direct code reading: `tests/BlueCode.Tests/RouterTests.fs` — `rootTests` list (current contents)
- Direct code reading: `tests/BlueCode.Tests/BlueCode.Tests.fsproj` — compile order
- Direct code reading: `tests/BlueCode.Tests/ReplTests.fs` — existing test patterns and `testSequenced` usage
- Direct code reading: `src/BlueCode.Cli/BlueCode.Cli.fsproj` — current compile order

### Secondary (MEDIUM confidence)
- `documentation/howto/handle-expecto-console-redirection.md` — testSequenced requirement confirmed
- `CLAUDE.md` project conventions — Spectre `[[]]` escape, `async {}` ban, commit protocol, test discovery pitfall

### Tertiary (LOW confidence)
- None — all findings grounded in direct code inspection of this repo

## Metadata

**Confidence breakdown:**
- Parser design: HIGH — DU pattern is idiomatic F# and matches existing codebase style throughout
- Dispatcher integration: HIGH — loop structure is clear from `Repl.fs` code; the hook point is unambiguous
- `/status` data sources: HIGH — all fields traced to specific variables/fields in existing code; one medium-confidence item (MaxModelLen floor vs. real value) is explicitly labeled
- `/clear` semantics: HIGH — FileSessionStore.Save creates file lazily; "untouched" invariant verified from code
- Test strategy: HIGH — matches existing ReplTests.fs patterns exactly; test registration pitfall documented and verified
- Pitfalls: HIGH — all grounded in CLAUDE.md, existing test files, and commit messages

**Research date:** 2026-04-29
**Valid until:** 2026-05-29 (stable codebase; no fast-moving dependencies)
