# Phase 32: SLASH session commands - Research

**Researched:** 2026-04-29
**Domain:** F# Cli layer — FileSessionStore extension, slash command dispatch, REPL integration
**Confidence:** HIGH (all findings from direct code inspection)

## Summary

Phase 32 adds `/sessions` (list recent N sessions) and `/resume <id>` (in-place session switch) to the blueCode REPL. The slash command parser (SlashCommand.fs) already handles both variants — `Sessions` and `Resume of id: string` — producing `Slash Sessions` and `Slash (Resume "...")` respectively. The dispatcher in Repl.fs stubs both as "not yet implemented". Phase 32 replaces that stub.

The work is entirely in the Cli layer. Core is untouched — `FileSessionStore` lives in `src/BlueCode.Cli/Adapters/`, `ISessionStore` in `Core/Ports.fs` stays unchanged (only concrete type gains new methods), `AgentError.SessionCorrupt` and `AgentError.SessionNotFound` already exist in `Core/Domain.fs`.

The primary new work is: (1) add `listRecent` to `FileSessionStore` using `Directory.GetFiles` + `FileInfo.LastWriteTime` sort, reading only the header line (line 0) of each file; (2) add `renderSessions` and update `renderHelp` in `Rendering.fs`; (3) replace the Repl.fs dispatcher stub with real handlers; (4) add tests (~15-20 new tests).

**Primary recommendation:** Two-plan split — Plan 32-01: FileSessionStore.listRecent + Rendering.renderSessions (pure functions, fully testable in isolation); Plan 32-02: Repl.fs dispatcher wiring + integration tests.

---

## Q1: FileSessionStore Current API Surface

**File:** `src/BlueCode.Cli/Adapters/FileSessionStore.fs` (147 lines)

The interface `ISessionStore` (defined in `Core/Ports.fs`) has exactly two methods:
```fsharp
abstract member Save: session: Session -> ct: CancellationToken -> Task<Result<unit, AgentError>>
abstract member Load: id: SessionId -> ct: CancellationToken -> Task<Result<Session, AgentError>>
```

The concrete `FileSessionStore` type implements both. `Load` signature: `Load (id: SessionId) (ct: CancellationToken) : Task<Result<Session, AgentError>>`.

`listRecent` does NOT exist yet. It is a new concrete method (not on the interface — the interface stays frozen).

`Session` type (`Core/Domain.fs` line 208):
```fsharp
type Session =
    { Id: SessionId
      Steps: Step list
      CreatedAt: DateTimeOffset
      LastActivityAt: DateTimeOffset }
```

---

## Q2: JSONL Format — Session Id, started_at, Turns, First Prompt

**Confirmed from inspecting `~/.bluecode/sessions/*.jsonl` files (534 exist) and FileSessionStore.fs lines 13-23:**

Line 0 (header):
```json
{"version":2,"sessionId":"<32-char-hex>","createdAt":"<iso8601>"}
```

Line 1..N (TurnComplete envelopes):
```json
{"type":"TurnComplete","turnIndex":<int>,"writtenAt":"<iso8601>","steps":[...]}
```

`steps` in each envelope is the **full cumulative** list up to that turn (not a delta).

**For `listRecent` metadata extraction:**
- **id**: from `header.sessionId` (line 0) or from the filename (`<id>.jsonl`)
- **started_at**: from `header.createdAt` (line 0 only — no need to read further)
- **turn count**: `lines.Length - 1` (all non-header lines that are non-blank) = number of TurnComplete envelopes
- **first prompt**: NOT directly stored in the jsonl. The steps array contains `Step.Action` values. The first prompt is the first `FinalAnswer` or `ToolCall` step in the first envelope. However, there is no "user prompt" field stored — only LLM steps. The "first prompt" excerpt in SLASH-05 must come from `Session.Steps[0].Action` as a proxy (or be marked as "n/a" for zero-step sessions). See Q10 for efficiency consideration.

**Alternative approach for first prompt:** read only line 0 (header) for id + createdAt + counts. For "first prompt excerpt", read line 1 (first envelope) and extract the first Step's Action. This is still cheap — only 2 lines needed for all metadata.

---

## Q3: SessionMeta Type Design

**Decision:** Define `SessionMeta` as a new record in `FileSessionStore.fs` (Cli layer, NOT Core). Core purity invariant forbids file I/O types in `src/BlueCode.Core/`.

Proposed type (Cli-layer only):
```fsharp
type SessionMeta =
    { Id: SessionId
      StartedAt: DateTimeOffset
      TurnCount: int
      FirstPromptExcerpt: string }  // up to 80 chars; "" if no turns/steps
```

This type belongs at the top of `FileSessionStore.fs` (module-level, before the class) so `Rendering.fs` can reference it (currently `Rendering.fs` compiles before `CompositionRoot.fs` in the fsproj — see the fsproj order: FileSessionStore.fs → Rendering.fs → SlashCommand.fs → CompositionRoot.fs → Repl.fs).

**CRITICAL compile-order dependency:** `Rendering.fs` references `BlueCode.Core.Domain` only. If `renderSessions` needs `SessionMeta`, it must either: (a) define `SessionMeta` in a module that compiles before `Rendering.fs`, i.e. in `FileSessionStore.fs` (which compiles before `Rendering.fs`), OR (b) define `SessionMeta` inline in `Rendering.fs` itself (no external reference needed). Option (a) is cleaner — define in FileSessionStore.fs, import in Rendering.fs with `open BlueCode.Cli.Adapters.FileSessionStore`.

---

## Q4: /resume Semantics — Repl.fs Mutable State

**Confirmed from Repl.fs lines 178-236:**

`runMultiTurnWithSession` holds:
```fsharp
let mutable currentSession : Session = initialSession
```

This is the only mutable session state. `priorSteps` is not a separate variable — `currentSession.Steps` serves as the prior-steps accumulation (passed to `runSingleTurn` each iteration as `currentSession.Steps`).

For `/resume <id>`:
1. Call `FileSessionStore.loadById id ct` (which is the existing `ISessionStore.Load` method)
2. On `Ok session` → reassign `currentSession <- session`
3. On `Error (SessionNotFound _)` → print friendly error, keep `currentSession` unchanged
4. On `Error (SessionCorrupt _)` → print friendly error, keep `currentSession` unchanged (requirement: REPL does NOT exit)

The `sessionStore` parameter is also in scope in `runMultiTurnWithSession` (line 169). The FileSessionStore singleton does not hold mutable currentSessionId — each call to Save/Load is stateless from the store's perspective. No atomicity issue.

---

## Q5: Existing loadById / Load Method

**STATE.md says "load 는 v2.0 이미 존재; list 만 신규".**

Confirmed from Ports.fs and FileSessionStore.fs: `ISessionStore.Load` already exists with signature:
```
Load: id: SessionId -> ct: CancellationToken -> Task<Result<Session, AgentError>>
```

The Repl `/resume` handler will call `sessionStore.Load (SessionId id) CancellationToken.None`. No new `loadById` method needed — `Load` IS `loadById`. The requirement doc uses "loadById" as a description of the existing method.

The contract already matches `Result<Session, AgentError>` exactly. No adjustment needed.

---

## Q6: Corrupt JSONL Handling — Existing AgentError.SessionCorrupt

**Confirmed from Domain.fs line 151:**
```fsharp
| SessionCorrupt of detail: string
```
Already exists. FileSessionStore.Load already returns `Error (SessionCorrupt ...)` for:
- empty file
- unsupported version
- header sessionId mismatch
- envelope JSON parse failure
- any other exception (defensive catch at line 144)

`renderError` in `Rendering.fs` line 119 already renders it:
```fsharp
| SessionCorrupt detail -> sprintf "Session file corrupt: %s" detail
```

For `/resume`, no new error variant needed. For `/sessions` listing, if a header line fails to parse during `listRecent`, the entry should be silently skipped (or returned with a "corrupt" marker — planner decision). The requirement says "corrupt jsonl → SessionCorrupt 식 에러 표시 (REPL 종료 안 함)". This applies to `/resume`, not to `/sessions` listing (listing should skip corrupt files gracefully).

---

## Q7: Test Pattern for Stateful Repl Tests

The existing pattern for multi-turn tests (ReplTests.fs):
1. Redirect `Console.In` (StringReader with scripted commands) and `Console.Out` (StringWriter)
2. Call `Repl.runMultiTurn components Compact` synchronously via `.GetAwaiter().GetResult()`
3. Assert on captured stdout
4. The whole testList is wrapped in `testSequenced` (line 43)

For `/resume` tests, the same pattern applies. Additional wrinkle: we need a real `.jsonl` session file on disk. The `SessionStoreTests.fs` pattern uses `buildSessionPath` + `withTempSession` to write/cleanup a temp file. The combination is straightforward:

```fsharp
// 1. Write a real session to disk via FileSessionStore.Save
// 2. Feed "/resume <id>\n/exit\n" as stdin
// 3. Assert stdout contains session id from loaded session
// 4. Cleanup temp file
```

For asserting priorSteps reload: capture LLM calls (using the `capturingLlm` pattern from ReplTests.fs line 300-316). After `/resume id` then a prompt, the LLM should see the resumed session's steps in its messages.

---

## Q8: Plan Breakdown

**2-plan split is optimal:**

**Plan 32-01: FileSessionStore.listRecent + Rendering.renderSessions**
- Add `SessionMeta` type to `FileSessionStore.fs`
- Add `listRecent (n: int) : SessionMeta list` concrete method to `FileSessionStore`
- Add `renderSessions (metas: SessionMeta list) : string` to `Rendering.fs`
- Add SessionStoreTests for listRecent (corrupt header skip, empty dir, N-limit, mtime sort)
- Add RenderingTests for renderSessions (empty list, N sessions, truncation at 80 chars)
- No Repl.fs changes in this plan

**Plan 32-02: Repl dispatcher wiring + integration tests**
- Replace `Some (Slash (Sessions | Resume _ | Plan | Edit))` stub in Repl.fs
- Add `| Some (Slash Sessions) ->` handler that calls `FileSessionStore().listRecent 10`
- Add `| Some (Slash (Resume id)) ->` handler with empty-id guard + load + currentSession swap
- Update `renderHelp` to remove "[coming in v2.5]" from `/sessions` and `/resume` (or update the label)
- Add ReplTests integration tests (testSequenced, Console.SetIn/SetOut pattern)
- Update "future-stub" test that currently asserts `/sessions` and `/resume` print "not yet implemented"

**The two plans are sequential** (Plan 32-02 depends on Plan 32-01's `SessionMeta` type and `renderSessions`). No parallelizable waves here — unlike Phase 31 where parser was pure, here Plan 32-02 needs the concrete objects from Plan 32-01.

---

## Q9: File Listing Performance — N=10 from 534 Sessions

`Directory.GetFiles(dir, "*.jsonl")` returns all 534 paths in one call. Then `Array.sortByDescending (fun p -> File.GetLastWriteTime p)` on FileInfo. Then `Array.take 10`. This is O(534) stat calls — negligible on local filesystem.

**.NET API:**
```fsharp
let files = Directory.GetFiles(sessionsDir, "*.jsonl")
let sorted = files |> Array.sortByDescending (fun p -> File.GetLastWriteTimeUtc p)
let recent = sorted |> Array.truncate n
```

`File.GetLastWriteTimeUtc` avoids timezone ambiguity. Each call is a single syscall. Total: ~534 stat calls for current corpus — fast enough (sub-millisecond on NVMe).

---

## Q10: SessionMeta Listing Efficiency — Header-Only Reads

**Only 2 lines needed per session file for full metadata:**
- Line 0 (header): `sessionId`, `createdAt` → id + started_at
- Turn count: `File.ReadAllLines(path).Length - 1` (total lines minus header = envelope count)
- First prompt excerpt: from line 1 (first TurnComplete envelope), extract `steps[0].Action`

**More efficient approach:** Read only line 0 with `File.ReadLines(path) |> Seq.tryHead` for id + createdAt. For turn count, use `File.ReadLines(path) |> Seq.length` to stream without loading all content. For first prompt, read line 1 with `File.ReadLines(path) |> Seq.skip 1 |> Seq.tryHead`.

**Simplest correct approach:** `File.ReadAllLines(path)` — with 534 sessions averaging ~3KB each, streaming the 10 most-recent files is trivially fast. No need to optimize beyond reading only the 10 selected files (after mtime sort).

**First prompt extraction from steps:** The first `Step.Action` in `envelope.steps[0]` will be either a `ToolCall` or `FinalAnswer`. A `FinalAnswer` as step 0 means the LLM gave a direct answer; a `ToolCall` shows the first tool used. Neither directly represents the user's input prompt — the prompt is NOT stored in the jsonl. The `renderSessions` function should label the first-step action as `"first action"` not `"first prompt"`, or simply show the thought string (`Step.Thought`) truncated to 80 chars, which IS the LLM's first reasoning step. For the `/sessions` display the requirement says "first prompt 첫 80자" — since the user prompt is not stored, the best proxy is `(Thought step.Thought)` from `envelope.steps[0]`, truncated to 80 chars.

---

## Q11: Slash Command Argument Parsing — /resume

**Already fully implemented in SlashCommand.fs lines 43-44:**
```fsharp
| "/resume" -> Resume arg   // arg = "" if /resume typed alone
```

`Resume` is already a DU case `Resume of id: string`. Empty string means no arg — the dispatcher should detect `id = ""` and show "usage: /resume <session-id>" without crashing.

No changes to `SlashCommand.fs` needed for Phase 32.

---

## Q12: Existing Cli/Rendering.fs Surface

**Rendering.fs** (172 lines): all plain `printfn`-friendly strings, no Spectre markup. Pattern established in Phase 31:
- `renderHelp : string` — static constant
- `renderStatus (session: Session) (forcedModel: Model option) (maxModelLen: int) : string` — pure function

`renderSessions` should follow the same pattern: pure function, `printfn`-compatible, NO Spectre markup. Signature:

```fsharp
let renderSessions (metas: SessionMeta list) : string
```

Returns multi-line plain text. Columns: id (32 chars), started_at (ISO short), turns (int), excerpt (80 chars). Empty list case: `"no sessions found"`.

The `SessionMeta` type is from `BlueCode.Cli.Adapters.FileSessionStore` — `Rendering.fs` will need `open BlueCode.Cli.Adapters.FileSessionStore` added at the top.

---

## Q13: Test Coverage Estimate

Phase 31 added 17 (parser) + 12 (integration) = 29 tests.
Phase 32 estimate: ~15-20 new tests.

**Plan 32-01 (unit tests — no testSequenced needed for pure functions):**
- `listRecent` in SessionStoreTests: ~6 tests
  - empty dir → empty list
  - N=10 with 3 sessions → returns all 3
  - N=2 with 5 sessions → returns 2 most recent by mtime
  - corrupt header line → skipped (no exception)
  - TurnCount is correct (envelope count)
  - FirstPromptExcerpt truncated at 80 chars
- `renderSessions` in RenderingTests: ~4 tests
  - empty list → "no sessions found"
  - single meta → shows id, started_at, turns, excerpt
  - long excerpt truncated at 80 chars
  - multiple metas → all listed

**Plan 32-02 (integration tests — must be testSequenced):**
- `/sessions` empty → correct message (~1)
- `/sessions` with pre-created sessions → shows ids (~1)
- `/resume ""` (no arg) → friendly usage error, REPL continues (~1)
- `/resume unknown-id` → SessionNotFound friendly error, REPL continues (~1)
- `/resume known-id` → session switched, printfn confirms, priorSteps visible (~2)
- `/resume` corrupt → SessionCorrupt friendly error, REPL continues (~1)
- Future-stub test update: remove /sessions and /resume from "not yet implemented" assertion (~1)

Total: ~18 new tests.

---

## Q14: Bench Gate Regression Risk

**Bench runner analysis (`bench/run.sh`):** All invocations use `dotnet run --project src/BlueCode.Cli -- --verbose --model "$model" "$prompt"` — single-turn mode (prompt as CLI arg). This path goes through `Program.fs → Repl.runSingleTurn`, NOT through `runMultiTurnWithSession`. The slash dispatcher (`runMultiTurnWithSession`'s `while running do` loop) is never entered.

**Zero regression risk from Phase 32 changes** to `runMultiTurnWithSession`'s dispatch arms — those code paths are unreachable in bench mode. Changes to `FileSessionStore` (adding `listRecent`) are additive only. Changes to `Rendering.fs` (adding `renderSessions`, updating `renderHelp`) do not affect `renderStep`, `renderResult`, or `renderError`.

The `defaultSystemPrompt` and `planSystemPromptSuffix` char counts are NOT touched by Phase 32.

---

## Q15: Async vs Sync for listRecent

**FileSessionStore uses `task {}` for all I/O** (lines 56-82, 84-146). `listRecent` will do:
- `Directory.GetFiles` — sync (no CancellationToken overload needed)
- `File.ReadLines` — sync streaming (or `File.ReadAllLines` — sync)
- JSON deserialization — sync

`listRecent` can be **synchronous** (returns `SessionMeta list` directly, not `Task<>`). The call from Repl.fs dispatcher is already in `task {}` CE context and can call sync helpers without issue. This matches the existing `buildSessionPath` and `newSessionId ()` patterns — those are also synchronous module-level functions in `FileSessionStore.fs`.

Signature recommendation:
```fsharp
let listRecent (n: int) : SessionMeta list
```
(module-level function, not a method on `FileSessionStore` class — same style as `buildSessionPath` and `newSessionId`)

---

## Architecture Patterns

### Recommended Changes Per File

**`src/BlueCode.Cli/Adapters/FileSessionStore.fs`** (Plan 32-01):
- Add `SessionMeta` record type (before `FileSessionStore` class, after `TurnEnvelope` private type)
- Add `let listRecent (n: int) : SessionMeta list` module-level function
- No changes to `FileSessionStore` class or `ISessionStore` interface

**`src/BlueCode.Cli/Rendering.fs`** (Plan 32-01):
- Add `open BlueCode.Cli.Adapters.FileSessionStore` at top
- Add `let renderSessions (metas: SessionMeta list) : string`
- Update `renderHelp` string to remove "[coming in v2.5]" from `/sessions` and `/resume` lines (Plan 32-02, since changing renderHelp breaks the existing "future-stub" integration test)

**`src/BlueCode.Cli/Repl.fs`** (Plan 32-02):
- Replace `| Some (Slash (Sessions | Resume _ | Plan | Edit)) ->` stub with:
  ```fsharp
  | Some (Slash Sessions) ->
      let metas = BlueCode.Cli.Adapters.FileSessionStore.listRecent 10
      printfn "%s" (Rendering.renderSessions metas)
  | Some (Slash (Resume id)) when id = "" ->
      printfn "usage: /resume <session-id>"
  | Some (Slash (Resume id)) ->
      // call sessionStore.Load (SessionId id) CancellationToken.None
      // on Ok → currentSession <- loaded; print confirmation
      // on Error SessionNotFound → print friendly error, keep currentSession
      // on Error SessionCorrupt → print friendly error, keep currentSession
  | Some (Slash (Plan | Edit)) ->
      printfn "(not yet implemented — coming in a future v2.5 phase)"
  ```

**`tests/BlueCode.Tests/SessionStoreTests.fs`** (Plan 32-01): add listRecent tests
**`tests/BlueCode.Tests/RenderingTests.fs`** (Plan 32-01): add renderSessions tests
**`tests/BlueCode.Tests/ReplTests.fs`** (Plan 32-02): add /sessions and /resume integration tests; update "future-stub" test

### fsproj Compile Order (no changes needed)
The current order already places `FileSessionStore.fs` before `Rendering.fs` before `Repl.fs` — correct for the new `SessionMeta` type dependency.

No new files need to be added to `.fsproj` or `rootTests` — all changes are within existing modules.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Session listing sorted by recency | Custom sort algorithm | `Array.sortByDescending (fun p -> File.GetLastWriteTimeUtc p)` | Already in .NET BCL |
| Jsonl parsing for metadata | New parser | Reuse `JsonSerializer.Deserialize<SessionHeader>` from FileSessionStore.fs | Already handles version check |
| String truncation to N chars | Manual substring | `if s.Length > 80 then s.[..79] else s` | Trivial but already used in renderError (line 106) |

---

## Common Pitfalls

### Pitfall 1: renderHelp [coming in v2.5] test
**What goes wrong:** Existing test `"runMultiTurn: future-stub commands (/sessions /resume /plan /edit) print 'not yet implemented'"` (ReplTests.fs line 617) asserts 4 lines with "not yet implemented". After Phase 32 ships `/sessions` and `/resume`, that count drops to 2. Also the "future-stub" slash command test assertion `Expect.isGreaterThanOrEqual stubLines.Length 4` will fail.
**How to avoid:** In Plan 32-02, update the test to expect 2 "not yet implemented" lines (Plan and Edit remain stubs). Split the test if needed.

### Pitfall 2: Console.SetOut + testSequenced
**What goes wrong:** Adding new tests to `ReplTests.fs` that use `Console.SetIn/SetOut` without the outer `testSequenced` wrapper causes parallel races.
**How to avoid:** All new Repl integration tests go inside the existing `testSequenced <| testList "Repl" [...]` block. This is already established (line 43-44).

### Pitfall 3: listRecent silently swallowing exceptions
**What goes wrong:** If `listRecent` throws on any file (e.g., permission denied), the whole `/sessions` command fails.
**How to avoid:** Wrap per-file reads in `try/with` and skip on parse error. Keep the outer directory listing in a `try/with` that returns `[]` on error (e.g., sessions dir doesn't exist yet — which can happen if no sessions have been saved yet).

### Pitfall 4: /resume with empty-arg guard before DU pattern match
**What goes wrong:** `Resume ""` is a valid DU case. The Repl dispatcher must guard `id = ""` before calling `sessionStore.Load (SessionId "")` which would produce `SessionNotFound (SessionId "")` — correct but confusing UX.
**How to avoid:** Pattern match on `Resume id when id = ""` first (or `Resume ""` literal).

### Pitfall 5: First prompt "n/a" for zero-step sessions
**What goes wrong:** A session that was created but had no completed turns (crashed after header-only write) has no steps in the jsonl. `listRecent` should return `FirstPromptExcerpt = ""` for these (not throw).
**How to avoid:** `envelope.steps` may be empty or the file may have only the header line. Handle both cases in `listRecent`.

---

## Code Examples

### listRecent skeleton
```fsharp
// Source: inspection of FileSessionStore.fs + Domain.fs
let listRecent (n: int) : SessionMeta list =
    try
        let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        let dir = Path.Combine(home, ".bluecode", "sessions")
        if not (Directory.Exists dir) then []
        else
            Directory.GetFiles(dir, "*.jsonl")
            |> Array.sortByDescending (fun p -> File.GetLastWriteTimeUtc p)
            |> Array.truncate n
            |> Array.toList
            |> List.choose (fun path ->
                try
                    let lines = File.ReadLines(path) |> Seq.truncate 2 |> Seq.toArray
                    if lines.Length = 0 then None
                    else
                        let header = JsonSerializer.Deserialize<SessionHeader>(lines.[0], jsonOptions)
                        let turnCount = File.ReadAllLines(path).Length - 1
                        let excerpt =
                            if lines.Length > 1 then
                                let env = JsonSerializer.Deserialize<TurnEnvelope>(lines.[1], jsonOptions)
                                match env.steps with
                                | step :: _ ->
                                    let (Thought t) = step.Thought
                                    if t.Length > 80 then t.[..79] else t
                                | [] -> ""
                            else ""
                        Some { Id = SessionId header.sessionId
                               StartedAt = header.createdAt
                               TurnCount = turnCount
                               FirstPromptExcerpt = excerpt }
                with _ -> None)
    with _ -> []
```

Note: `SessionHeader` and `TurnEnvelope` are `private` types in `FileSessionStore.fs`. `listRecent` must be defined in the same module to access them.

### renderSessions skeleton
```fsharp
// Source: pattern from renderHelp/renderStatus in Rendering.fs
let renderSessions (metas: SessionMeta list) : string =
    if metas.IsEmpty then
        "no sessions found"
    else
        let header = sprintf "%-34s %-25s %-6s %s" "session id" "started" "turns" "first thought"
        let rows =
            metas
            |> List.map (fun m ->
                let (SessionId idStr) = m.Id
                let dateStr = m.StartedAt.ToString("yyyy-MM-dd HH:mm:ss")
                let excerpt = if m.FirstPromptExcerpt.Length > 40 then m.FirstPromptExcerpt.[..39] + "..." else m.FirstPromptExcerpt
                sprintf "%-34s %-25s %-6d %s" idStr dateStr m.TurnCount excerpt)
        header :: rows |> String.concat "\n"
```

### Repl /resume dispatch skeleton
```fsharp
// Source: Repl.fs pattern from /clear handler (lines 205-210)
| Some (Slash (Resume "")) ->
    printfn "usage: /resume <session-id>"
| Some (Slash (Resume id)) ->
    let! loadResult = sessionStore.Load (SessionId id) CancellationToken.None
    match loadResult with
    | Ok loaded ->
        currentSession <- loaded
        let (SessionId newIdStr) = loaded.Id
        printfn "Resumed session: %s (%d steps)" newIdStr loaded.Steps.Length
    | Error (SessionNotFound _) ->
        printfn "Session not found: %s" id
    | Error (SessionCorrupt detail) ->
        printfn "Session file corrupt: %s" detail
    | Error other ->
        printfn "Resume failed: %A" other
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `/sessions`/`/resume` stub ("not yet implemented") | Real handlers | Phase 32 | Replaces 4-arm stub with 5-arm match |
| No session listing | `listRecent 10` | Phase 32 | New module-level function in FileSessionStore.fs |

---

## Open Questions

1. **"first prompt excerpt" source**
   - What we know: user prompt is NOT stored in jsonl; only LLM steps (Thought + Action) are
   - What's unclear: SLASH-05 says "first prompt 첫 80자" — should this be the thought text of step 0, or should we note "n/a — prompt not stored"?
   - Recommendation: Use `step.Thought` (first step's LLM reasoning) as the best proxy. Label the column "first thought" in the UI, not "first prompt", to avoid confusion. Or store 0 chars from a non-existent field and label it "(prompt not stored in v2.0)".

2. **renderHelp update timing**
   - What we know: `renderHelp` currently marks `/sessions` and `/resume` as "[coming in v2.5]"; existing test asserts this
   - What's unclear: Should renderHelp be updated in Plan 32-01 or Plan 32-02?
   - Recommendation: Plan 32-02 (same plan that changes Repl dispatcher; updating help text without updating dispatcher would be misleading).

---

## Sources

### Primary (HIGH confidence)
- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/Adapters/FileSessionStore.fs` — full file read; JSONL format confirmed
- `/Users/ohama/projs/blueCode/src/BlueCode.Core/Domain.fs` — AgentError DU, Session type, Step type
- `/Users/ohama/projs/blueCode/src/BlueCode.Core/Ports.fs` — ISessionStore interface (Save + Load only)
- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/SlashCommand.fs` — Sessions + Resume already parsed
- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/Repl.fs` — mutable currentSession, stub dispatcher location
- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/Rendering.fs` — renderHelp, renderStatus patterns
- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/CompositionRoot.fs` — AppComponents shape
- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/BlueCode.Cli.fsproj` — compile order
- `/Users/ohama/projs/blueCode/tests/BlueCode.Tests/ReplTests.fs` — stubLlm, testSequenced, Console.SetIn/SetOut pattern
- `/Users/ohama/projs/blueCode/tests/BlueCode.Tests/SessionStoreTests.fs` — withTempSession pattern
- `/Users/ohama/projs/blueCode/tests/BlueCode.Tests/RouterTests.fs` — rootTests list (explicit registration required)
- `/Users/ohama/projs/blueCode/tests/BlueCode.Tests/BlueCode.Tests.fsproj` — compile Include order
- `~/.bluecode/sessions/` — 534 real session files inspected (format verified)
- `bench/run.sh` — confirmed all runs are single-turn (no REPL path)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all code read directly from source
- Architecture: HIGH — FileSessionStore internals fully inspected; ISessionStore interface confirmed unchanged
- Pitfalls: HIGH — derived from reading existing tests and stubs

**Research date:** 2026-04-29
**Valid until:** 2026-05-29 (stable domain; no external dependencies change)
