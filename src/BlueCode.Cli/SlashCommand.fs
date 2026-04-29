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
