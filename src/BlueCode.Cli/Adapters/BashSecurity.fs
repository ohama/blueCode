module BlueCode.Cli.Adapters.BashSecurity

open System
open System.Text.RegularExpressions

// ── Internal decision type ────────────────────────────────────────────────────
// Port of Python SecurityBehavior enum → F# SecurityDecision DU.
// In Python, ASK results are sub-categorised by is_misparsing:
//   misparsing=True  → short-circuit immediately (implemented as Deny here)
//   misparsing=False → deferred until all misparsing checks pass (DenyDeferred)
// DENY in Python → Deny here.
// ALLOW → Allow (only early validators can allow).
// PASSTHROUGH → Passthrough (check has no opinion).
type private SecurityDecision =
    | Allow
    | Deny         of reason: string
    | DenyDeferred of reason: string  // non-misparsing ASK: defer to end of chain
    | Passthrough

// ── Pre-computed context ──────────────────────────────────────────────────────
// Port of Python ValidationContext dataclass.
type private ValidationCtx = {
    OriginalCommand        : string
    BaseCommand            : string
    UnquotedContent        : string   // outside single quotes (double quotes preserved)
    FullyUnquotedContent   : string   // outside all quotes, safe redirections stripped
    FullyUnquotedPreStrip  : string   // outside all quotes, before stripping redirections
    UnquotedKeepQuoteChars : string   // outside all quotes, but ' and " chars preserved
}

// ── Pre-compiled constants ────────────────────────────────────────────────────
// All Regex values are module-level let private bindings with RegexOptions.Compiled
// so they are compiled once at module initialisation, not per call.

// CONTROL_CHAR_RE — port of Python CONTROL_CHAR_RE.
// Matches 0x00-0x08, 0x0B-0x0C, 0x0E-0x1F, 0x7F.
// Excludes tab (0x09), newline (0x0A), carriage return (0x0D).
let private controlCharRe =
    Regex(@"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]", RegexOptions.Compiled)

// UNICODE_WS_RE — port of Python UNICODE_WS_RE.
// Unicode whitespace that can cause parser differentials.
// Explicit code points to avoid invisible-character hazards in source:
// U+00A0 U+1680 U+2000-U+200A U+2028 U+2029 U+202F U+205F U+3000 U+FEFF
let private unicodeWsRe =
    Regex(@"[   -     　﻿]",
          RegexOptions.Compiled)

// COMMAND_SUBSTITUTION_PATTERNS — port of Python list of same name.
let private commandSubstitutionPatterns : (Regex * string) list = [
    ( Regex(@"<\(",                             RegexOptions.Compiled), "process substitution <()"                    )
    ( Regex(@">\(",                             RegexOptions.Compiled), "process substitution >()"                    )
    ( Regex(@"=\(",                             RegexOptions.Compiled), "Zsh process substitution =()"                )
    ( Regex(@"(?:^|[\s;&|])=[a-zA-Z_]",        RegexOptions.Compiled), "Zsh equals expansion (=cmd)"                )
    ( Regex(@"\$\(",                            RegexOptions.Compiled), "$() command substitution"                    )
    ( Regex(@"\$\{",                            RegexOptions.Compiled), "${} parameter substitution"                  )
    ( Regex(@"\$\[",                            RegexOptions.Compiled), "$[] legacy arithmetic expansion"             )
    ( Regex(@"~\[",                             RegexOptions.Compiled), "Zsh-style parameter expansion"              )
    ( Regex(@"\(e:",                            RegexOptions.Compiled), "Zsh-style glob qualifiers"                  )
    ( Regex(@"\(\+",                            RegexOptions.Compiled), "Zsh glob qualifier with command execution"  )
    ( Regex(@"\}\s*always\s*\{",               RegexOptions.Compiled), "Zsh always block (try/always construct)"    )
    ( Regex(@"<#",                              RegexOptions.Compiled), "PowerShell comment syntax"                  )
]

// ZSH_DANGEROUS_COMMANDS — port of Python frozenset of same name.
let private zshDangerousCommands : Set<string> =
    Set.ofList [
        "zmodload"; "emulate"
        "sysopen"; "sysread"; "syswrite"; "sysseek"
        "zpty"; "ztcp"; "zsocket"; "mapfile"
        "zf_rm"; "zf_mv"; "zf_ln"; "zf_chmod"; "zf_chown"
        "zf_mkdir"; "zf_rmdir"; "zf_chgrp"
    ]

// ZSH_PRECOMMAND_MODIFIERS — port of Python frozenset of same name.
let private zshPrecommandModifiers : Set<string> =
    Set.ofList [ "command"; "builtin"; "noglob"; "nocorrect" ]

// SHELL_OPERATORS — port of Python SHELL_OPERATORS frozenset.
let private shellOperators : Set<char> =
    Set.ofList [ ';'; '|'; '&'; '<'; '>' ]

// FIND_DANGEROUS_FLAGS — port of Python FIND_DANGEROUS_FLAGS frozenset.
// Kept as a named constant matching the Python source; currently used for
// documentation/future use. The main security gate is the validator chain.
let private findDangerousFlags : Set<string> =
    Set.ofList [ "-exec"; "-execdir"; "-ok"; "-okdir"; "-delete" ]

// DESTRUCTIVE_PATTERNS — port of Python DESTRUCTIVE_PATTERNS list.
// In Python, get_destructive_command_warning is informational; check_shell_security
// with allow_destructive=False blocks on it. In blueCode we always block (non-interactive).
let private destructivePatterns : (Regex * string) list = [
    // Git — data loss / hard to reverse
    ( Regex(@"\bgit\s+reset\s+--hard\b",                                        RegexOptions.Compiled),
      "Note: may discard uncommitted changes" )
    ( Regex(@"\bgit\s+push\b[^;&|\n]*[ \t](--force|--force-with-lease|-f)\b",  RegexOptions.Compiled),
      "Note: may overwrite remote history" )
    ( Regex(@"\bgit\s+clean\b(?![^;&|\n]*(?:-[a-zA-Z]*n|--dry-run))[^;&|\n]*-[a-zA-Z]*f",
                                                                                 RegexOptions.Compiled),
      "Note: may permanently delete untracked files" )
    ( Regex(@"\bgit\s+checkout\s+(--\s+)?\.[ \t]*($|[;&|\n])",                 RegexOptions.Compiled),
      "Note: may discard all working tree changes" )
    ( Regex(@"\bgit\s+restore\s+(--\s+)?\.[ \t]*($|[;&|\n])",                  RegexOptions.Compiled),
      "Note: may discard all working tree changes" )
    ( Regex(@"\bgit\s+stash[ \t]+(drop|clear)\b",                              RegexOptions.Compiled),
      "Note: may permanently remove stashed changes" )
    ( Regex(@"\bgit\s+branch\s+(-D[ \t]|--delete\s+--force|--force\s+--delete)\b", RegexOptions.Compiled),
      "Note: may force-delete a branch" )
    // Git — safety bypass
    ( Regex(@"\bgit\s+(commit|push|merge)\b[^;&|\n]*--no-verify\b",            RegexOptions.Compiled),
      "Note: may skip safety hooks" )
    ( Regex(@"\bgit\s+commit\b[^;&|\n]*--amend\b",                             RegexOptions.Compiled),
      "Note: may rewrite the last commit" )
    // File deletion
    ( Regex(@"(^|[;&|\n]\s*)rm\s+-[a-zA-Z]*[rR][a-zA-Z]*f|(^|[;&|\n]\s*)rm\s+-[a-zA-Z]*f[a-zA-Z]*[rR]",
                                                                                 RegexOptions.Compiled),
      "Note: may recursively force-remove files" )
    ( Regex(@"(^|[;&|\n]\s*)rm\s+-[a-zA-Z]*[rR]",                             RegexOptions.Compiled),
      "Note: may recursively remove files" )
    ( Regex(@"(^|[;&|\n]\s*)rm\s+-[a-zA-Z]*f",                                RegexOptions.Compiled),
      "Note: may force-remove files" )
    // Database
    ( Regex(@"\b(DROP|TRUNCATE)\s+(TABLE|DATABASE|SCHEMA)\b",                  RegexOptions.Compiled ||| RegexOptions.IgnoreCase),
      "Note: may drop or truncate database objects" )
    ( Regex(@"\bDELETE\s+FROM\s+\w+[ \t]*(;|""|\\'|\n|$)",                    RegexOptions.Compiled ||| RegexOptions.IgnoreCase),
      "Note: may delete all rows from a database table" )
    // Infrastructure
    ( Regex(@"\bkubectl\s+delete\b",                                            RegexOptions.Compiled),
      "Note: may delete Kubernetes resources" )
    ( Regex(@"\bterraform\s+destroy\b",                                         RegexOptions.Compiled),
      "Note: may destroy Terraform infrastructure" )
]

// ── Helper functions ───────────────────────────────────────────────────────────
// Ports of Python helpers: extract_quoted_content, strip_safe_redirections,
// has_unescaped_char, _is_escaped_at_position, _has_backslash_escaped_whitespace,
// _has_backslash_escaped_operator, split_command.

/// Port of extract_quoted_content.
/// Walk the command character-by-character tracking quote state and escaped flag.
/// Returns (withDoubleQuotes, fullyUnquoted, unquotedKeepQuoteChars).
///   withDoubleQuotes      = unquoted_content in Python (outside single quotes)
///   fullyUnquoted         = fully_unquoted_pre_strip (outside all quotes)
///   unquotedKeepQuoteChars= unquoted_keep_quote_chars (outside all quotes, quote chars kept)
let private extractQuotedContent (command: string) : string * string * string =
    let sbWithDq = Text.StringBuilder()
    let sbFully  = Text.StringBuilder()
    let sbKeep   = Text.StringBuilder()
    let mutable i        = 0
    let mutable inSingle = false
    let mutable inDouble = false
    let mutable escaped  = false

    while i < command.Length do
        let c = command.[i]

        if escaped then
            // Python: consume the next char; append to with_double_quotes if not in single quote,
            // and to fully_unquoted / unquoted_keep_quote_chars if not in any quote.
            escaped <- false
            if not inSingle then
                sbWithDq.Append(c) |> ignore
            if not inSingle && not inDouble then
                sbFully.Append(c) |> ignore
                sbKeep.Append(c)  |> ignore
            i <- i + 1

        elif c = '\\' && not inSingle then
            // Python: set escaped flag, append backslash to outputs
            escaped <- true
            if not inSingle then
                sbWithDq.Append(c) |> ignore
            if not inSingle && not inDouble then
                sbFully.Append(c) |> ignore
                sbKeep.Append(c)  |> ignore
            i <- i + 1

        elif c = '\'' && not inDouble then
            // Toggle single-quote. Quote char goes to keepQuoteChars only.
            inSingle <- not inSingle
            sbKeep.Append(c) |> ignore
            i <- i + 1

        elif c = '"' && not inSingle then
            // Toggle double-quote. Quote char goes to withDq and keepQuoteChars.
            inDouble <- not inDouble
            sbWithDq.Append(c) |> ignore
            sbKeep.Append(c)   |> ignore
            i <- i + 1

        else
            // Ordinary character.
            if not inSingle then
                sbWithDq.Append(c) |> ignore
            if not inSingle && not inDouble then
                sbFully.Append(c) |> ignore
                sbKeep.Append(c)  |> ignore
            i <- i + 1

    sbWithDq.ToString(), sbFully.ToString(), sbKeep.ToString()

/// Port of strip_safe_redirections.
let private stripSafeRedirections (content: string) : string =
    let mutable s = content
    s <- Regex.Replace(s, @"\s+2\s*>&\s*1(?=\s|$)", "")
    s <- Regex.Replace(s, @"[012]?\s*>\s*/dev/null(?=\s|$)", "")
    s <- Regex.Replace(s, @"\s*<\s*/dev/null(?=\s|$)", "")
    s

/// Port of has_unescaped_char.
/// Uses the skip-by-2 approach: skip each backslash and its following char.
let private hasUnescapedChar (content: string) (ch: char) : bool =
    let mutable i     = 0
    let mutable found = false
    while i < content.Length && not found do
        if content.[i] = '\\' && i + 1 < content.Length then
            i <- i + 2  // skip backslash + escaped char
        elif content.[i] = ch then
            found <- true
            i <- i + 1
        else
            i <- i + 1
    found

/// Port of _is_escaped_at_position.
/// Counts consecutive backslashes before pos; odd count = escaped.
let private isEscapedAtPosition (content: string) (pos: int) : bool =
    let mutable count = 0
    let mutable i     = pos - 1
    while i >= 0 && content.[i] = '\\' do
        count <- count + 1
        i     <- i - 1
    count % 2 = 1

/// Port of _has_backslash_escaped_whitespace.
/// True if \<space> or \<tab> appears outside any quotes.
let private hasBackslashEscapedWhitespace (command: string) : bool =
    let mutable inSingle = false
    let mutable inDouble = false
    let mutable i   = 0
    let mutable hit = false
    while i < command.Length && not hit do
        let c = command.[i]
        if c = '\\' && not inSingle then
            if i + 1 < command.Length then
                let next = command.[i + 1]
                if not inDouble && (next = ' ' || next = '\t') then
                    hit <- true
            i <- i + 2
        elif c = '\'' && not inDouble then
            inSingle <- not inSingle
            i <- i + 1
        elif c = '"' && not inSingle then
            inDouble <- not inDouble
            i <- i + 1
        else
            i <- i + 1
    hit

/// Port of _has_backslash_escaped_operator.
/// True if \<shell-operator> appears outside any quotes.
let private hasBackslashEscapedOperator (command: string) : bool =
    let mutable inSingle = false
    let mutable inDouble = false
    let mutable i   = 0
    let mutable hit = false
    while i < command.Length && not hit do
        let c = command.[i]
        if c = '\\' && not inSingle then
            if not inDouble && i + 1 < command.Length then
                let next = command.[i + 1]
                if shellOperators.Contains(next) then
                    hit <- true
            i <- i + 2
        elif c = '\'' && not inDouble then
            inSingle <- not inSingle
            i <- i + 1
        elif c = '"' && not inSingle then
            inDouble <- not inDouble
            i <- i + 1
        else
            i <- i + 1
    hit

/// Port of split_command.
/// Splits on &&, ||, ;, | respecting quotes and backslash escapes.
/// Single & does NOT split (appended to current segment).
let private splitCommand (command: string) : string list =
    let segments = Collections.Generic.List<string>()
    let current  = Text.StringBuilder()
    let mutable inSingle = false
    let mutable inDouble = false
    let mutable escaped  = false
    let mutable i = 0

    let flush () =
        let s = current.ToString().Trim()
        if s <> "" then segments.Add(s)
        current.Clear() |> ignore

    while i < command.Length do
        let c = command.[i]

        if escaped then
            escaped <- false
            current.Append(c) |> ignore
            i <- i + 1

        elif c = '\\' && not inSingle then
            escaped <- true
            current.Append(c) |> ignore
            i <- i + 1

        elif c = '\'' && not inDouble then
            inSingle <- not inSingle
            current.Append(c) |> ignore
            i <- i + 1

        elif c = '"' && not inSingle then
            inDouble <- not inDouble
            current.Append(c) |> ignore
            i <- i + 1

        elif inSingle || inDouble then
            current.Append(c) |> ignore
            i <- i + 1

        else
            // Unquoted: check for operators
            if c = ';' then
                flush ()
                i <- i + 1
            elif c = '|' then
                if i + 1 < command.Length && command.[i + 1] = '|' then
                    flush ()
                    i <- i + 2
                else
                    flush ()
                    i <- i + 1
            elif c = '&' then
                if i + 1 < command.Length && command.[i + 1] = '&' then
                    flush ()
                    i <- i + 2
                else
                    // single & (background) — does NOT split
                    current.Append(c) |> ignore
                    i <- i + 1
            else
                current.Append(c) |> ignore
                i <- i + 1

    flush ()
    List.ofSeq segments

// ── Validators (18 total) ─────────────────────────────────────────────────────
//
// Group A — pre-context (runs before ValidationCtx is fully populated):
//   validateControlCharacters
//
// Group B — early (can Allow or Deny; runs before main chain):
//   validateEmpty, validateIncompleteCommands, validateGitCommit
//
// Group C — main chain:
//   Non-deferred (16): validateJqCommand, validateObfuscatedFlags,
//     validateShellMetacharacters, validateDangerousVariables,
//     validateCommentQuoteDesync, validateQuotedNewline, validateCarriageReturn,
//     validateIfsInjection, validateProcEnvironAccess, validateDangerousPatterns,
//     validateBackslashEscapedWhitespace, validateBackslashEscapedOperators,
//     validateUnicodeWhitespace, validateMidWordHash, validateBraceExpansion,
//     validateZshDangerousCommands
//   Deferred (2): validateNewlines, validateRedirections

// ── Group A ───────────────────────────────────────────────────────────────────

/// Port of validate_control_characters.
/// Misparsing ASK in Python → Deny in blueCode (non-interactive).
let private validateControlCharacters (ctx: ValidationCtx) : SecurityDecision =
    if controlCharRe.IsMatch(ctx.OriginalCommand) then
        Deny "Command contains non-printable control characters that could bypass security checks"
    else
        Passthrough

// ── Group B ───────────────────────────────────────────────────────────────────

/// Port of validate_empty.
let private validateEmpty (ctx: ValidationCtx) : SecurityDecision =
    if ctx.OriginalCommand.Trim() = "" then Allow
    else Passthrough

/// Port of validate_incomplete_commands.
/// All branches are misparsing ASKs in Python → Deny.
let private validateIncompleteCommands (ctx: ValidationCtx) : SecurityDecision =
    let original = ctx.OriginalCommand
    let trimmed  = original.TrimStart()

    if Regex.IsMatch(original, @"^\s*\t") then
        Deny "Command appears to be an incomplete fragment (starts with tab)"
    elif trimmed.StartsWith("-") then
        Deny "Command appears to be an incomplete fragment (starts with flags)"
    elif Regex.IsMatch(original, @"^\s*(&&|\|\||;|>>?|<)") then
        Deny "Command appears to be a continuation line (starts with operator)"
    else
        Passthrough

/// Port of validate_git_commit.
/// Early-allows simple `git commit -m '...'` patterns.
/// Blocks git commit messages with command substitution.
/// MUST remain in the early chain — do NOT comment out.
let private validateGitCommit (ctx: ValidationCtx) : SecurityDecision =
    // Only applies to git commit commands
    if ctx.BaseCommand <> "git" || not (Regex.IsMatch(ctx.OriginalCommand, @"^git\s+commit\s+")) then
        Passthrough
    // If backslash present, fall through to full validation
    elif ctx.OriginalCommand.Contains('\\') then
        Passthrough
    else

    let m =
        Regex.Match(
            ctx.OriginalCommand,
            "^git[ \\t]+commit[ \\t]+[^;&|`$<>()\\n\\r]*?-m[ \\t]+([\"'])([\\s\\S]*?)\\1(.*)$"
        )

    if not m.Success then
        Passthrough
    else

    let quote          = m.Groups.[1].Value
    let messageContent = m.Groups.[2].Value
    let remainder      = m.Groups.[3].Value

    // Double-quoted message with command substitution
    if quote = "\"" && messageContent <> "" && Regex.IsMatch(messageContent, @"\$\(|`|\$\{") then
        Deny "Git commit message contains command substitution patterns"
    // Remainder has shell metacharacters — fall through to full validation
    elif remainder <> "" && Regex.IsMatch(remainder, @"[;|&()`]|\$\(|\$\{") then
        Passthrough
    else
        // Check remainder for unquoted redirect operators
        let hasUnquotedRedirect =
            if remainder = "" then false
            else
                let mutable inSq  = false
                let mutable inDq  = false
                let sb = Text.StringBuilder()
                for c in remainder do
                    if   c = '\'' && not inDq then inSq <- not inSq
                    elif c = '"'  && not inSq then inDq <- not inDq
                    elif not inSq && not inDq then sb.Append(c) |> ignore
                Regex.IsMatch(sb.ToString(), "[<>]")

        if hasUnquotedRedirect then
            Passthrough
        elif messageContent <> "" && messageContent.StartsWith("-") then
            Deny "Command contains quoted characters in flag names"
        else
            Allow

// ── Group C — Main chain ──────────────────────────────────────────────────────

/// Port of validate_jq_command.
/// Blocks dangerous jq patterns: system(), file-reading flags.
let private validateJqCommand (ctx: ValidationCtx) : SecurityDecision =
    if ctx.BaseCommand <> "jq" then
        Passthrough
    elif Regex.IsMatch(ctx.OriginalCommand, @"\bsystem\s*\(") then
        Deny "jq command contains system() function which executes arbitrary commands"
    else
        let afterJq =
            if ctx.OriginalCommand.Length > 3 then ctx.OriginalCommand.[3..].TrimStart()
            else ""
        if Regex.IsMatch(afterJq, @"(?:^|\s)(?:-f\b|--from-file|--rawfile|--slurpfile|-L\b|--library-path)") then
            Deny "jq command contains dangerous flags that could read arbitrary files"
        else
            Passthrough

/// Port of validate_obfuscated_flags.
/// Detects ANSI-C quoting, locale quoting, and other flag-obfuscation patterns.
let private validateObfuscatedFlags (ctx: ValidationCtx) : SecurityDecision =
    let original = ctx.OriginalCommand
    let baseCmd  = ctx.BaseCommand

    // echo without shell operators is safe for this check
    if baseCmd = "echo" && not (Regex.IsMatch(original, @"[|&;]")) then
        Passthrough
    // $'...' ANSI-C quoting
    elif Regex.IsMatch(original, @"\$'[^']*'") then
        Deny "Command contains ANSI-C quoting which can hide characters"
    // $"..." locale quoting
    elif Regex.IsMatch(original, "\\$\"[^\"]*\"") then
        Deny "Command contains locale quoting which can hide characters"
    // $'' or $"" before dash
    elif Regex.IsMatch(original, "\\$['\"]['\"]\\s*-") then
        Deny "Command contains empty special quotes before dash (potential bypass)"
    // '' or "" at word start before dash
    elif Regex.IsMatch(original, "(?:^|\\s)(?:''|\"\"){1,}\\s*-") then
        Deny "Command contains empty quotes before dash (potential bypass)"
    // Empty quote pairs adjacent to quoted dash
    elif Regex.IsMatch(original, "(?:\"\"|''){1,}['\"][-]") then
        Deny "Command contains empty quote pair adjacent to quoted dash"
    // 3+ consecutive quote chars at word start
    elif Regex.IsMatch(original, "(?:^|\\s)['\"]['\"]['\"]") then
        Deny "Command contains consecutive quote characters at word start"
    else

    // Walk character-by-character tracking quote state; detect obfuscated flags
    let mutable inSingle = false
    let mutable inDouble = false
    let mutable escaped  = false
    let mutable decision = Passthrough
    let mutable i = 0

    while i < original.Length - 1 && decision = Passthrough do
        let c    = original.[i]
        let next = original.[i + 1]

        if escaped then
            escaped <- false

        elif c = '\\' && not inSingle then
            escaped <- true

        elif c = '\'' && not inDouble then
            inSingle <- not inSingle

        elif c = '"' && not inSingle then
            inDouble <- not inDouble

        elif not inSingle && not inDouble then
            // Whitespace before a quote containing a dash (obfuscated flag)
            if (c = ' ' || c = '\t' || c = '\n') && (next = '\'' || next = '"' || next = '`') then
                let quoteChar = next
                let mutable j = i + 2
                let insideQuote = Text.StringBuilder()
                while j < original.Length && original.[j] <> quoteChar do
                    insideQuote.Append(original.[j]) |> ignore
                    j <- j + 1
                if j < original.Length && original.[j] = quoteChar then
                    let inside = insideQuote.ToString()
                    let hasFlagCharsInside = Regex.IsMatch(inside, @"^-+[a-zA-Z0-9$`]")
                    let charAfter =
                        if j + 1 < original.Length then string original.[j + 1]
                        else ""
                    let hasFlagContinuing =
                        Regex.IsMatch(inside, @"^-+$") &&
                        charAfter <> "" &&
                        Regex.IsMatch(charAfter, @"[a-zA-Z0-9\\${`\-]")
                    if hasFlagCharsInside || hasFlagContinuing then
                        decision <- Deny "Command contains quoted characters in flag names"

            // Whitespace before dash; check for quotes mixed into flag token
            elif (c = ' ' || c = '\t' || c = '\n') && next = '-' then
                let mutable j = i + 1
                let flagContent = Text.StringBuilder()
                let mutable stop = false
                while j < original.Length && not stop do
                    let fc = original.[j]
                    if fc = ' ' || fc = '\t' || fc = '\n' || fc = '\r' || fc = '=' then
                        stop <- true
                    elif (fc = '\'' || fc = '"') && baseCmd = "cut" && flagContent.ToString() = "-d" then
                        stop <- true
                    else
                        flagContent.Append(fc) |> ignore
                        j <- j + 1
                let fc = flagContent.ToString()
                if fc.Contains("\"") || fc.Contains("'") then
                    decision <- Deny "Command contains quoted characters in flag names"

        i <- i + 1

    decision

/// Port of validate_shell_metacharacters.
/// Detects shell metacharacters (;, |, &) inside quoted arguments.
let private validateShellMetacharacters (ctx: ValidationCtx) : SecurityDecision =
    let unquoted = ctx.UnquotedContent
    // Shell metacharacters in a bare quoted string at argument position
    if Regex.IsMatch(unquoted, "(?:^|\\s)[\"'][^\"']*[;&][^\"']*[\"'](?:\\s|$)") then
        Deny "Command contains shell metacharacters (;, |, or &) in arguments"
    // Shell metacharacters in -name/-path/-iname quoted args
    elif Regex.IsMatch(unquoted, "-name\\s+[\"'][^\"']*[;|&][^\"']*[\"']") then
        Deny "Command contains shell metacharacters (;, |, or &) in arguments"
    elif Regex.IsMatch(unquoted, "-path\\s+[\"'][^\"']*[;|&][^\"']*[\"']") then
        Deny "Command contains shell metacharacters (;, |, or &) in arguments"
    elif Regex.IsMatch(unquoted, "-iname\\s+[\"'][^\"']*[;|&][^\"']*[\"']") then
        Deny "Command contains shell metacharacters (;, |, or &) in arguments"
    else
        Passthrough

/// Port of validate_dangerous_variables.
/// Detects variables used in dangerous positions (redirections or pipes).
let private validateDangerousVariables (ctx: ValidationCtx) : SecurityDecision =
    let fu = ctx.FullyUnquotedContent
    if Regex.IsMatch(fu, @"[<>|]\s*\$[A-Za-z_]") ||
       Regex.IsMatch(fu, @"\$[A-Za-z_][A-Za-z0-9_]*\s*[|<>]") then
        Deny "Command contains variables in dangerous contexts (redirections or pipes)"
    else
        Passthrough

/// Port of validate_comment_quote_desync.
/// Quote characters inside # comments can desync downstream quote trackers.
/// Misparsing ASK in Python → Deny.
let private validateCommentQuoteDesync (ctx: ValidationCtx) : SecurityDecision =
    let original = ctx.OriginalCommand
    let mutable inSingle = false
    let mutable inDouble = false
    let mutable escaped  = false
    let mutable decision = Passthrough
    let mutable i = 0

    while i < original.Length && decision = Passthrough do
        let c = original.[i]

        if escaped then
            escaped <- false

        elif inSingle then
            if c = '\'' then inSingle <- false

        elif c = '\\' then
            escaped <- true

        elif inDouble then
            if c = '"' then inDouble <- false

        elif c = '\'' then
            inSingle <- true

        elif c = '"' then
            inDouble <- true

        elif c = '#' then
            let lineEnd = original.IndexOf('\n', i)
            let commentText =
                if lineEnd = -1 then original.[i + 1 ..]
                else original.[i + 1 .. lineEnd - 1]
            if Regex.IsMatch(commentText, "['\"]") then
                decision <- Deny "Command contains quote characters inside a # comment which can desync quote tracking"
            if lineEnd = -1 then
                i <- original.Length  // break

        i <- i + 1

    decision

/// Port of validate_quoted_newline.
/// Newlines inside quoted strings where the next line starts with # can
/// hide arguments from line-based permission checks.
/// Misparsing ASK in Python → Deny.
let private validateQuotedNewline (ctx: ValidationCtx) : SecurityDecision =
    let original = ctx.OriginalCommand
    if not (original.Contains('\n')) || not (original.Contains('#')) then
        Passthrough
    else

    let mutable inSingle = false
    let mutable inDouble = false
    let mutable escaped  = false
    let mutable decision = Passthrough
    let mutable i = 0

    while i < original.Length && decision = Passthrough do
        let c = original.[i]

        if escaped then
            escaped <- false

        elif c = '\\' && not inSingle then
            escaped <- true

        elif c = '\'' && not inDouble then
            inSingle <- not inSingle

        elif c = '"' && not inSingle then
            inDouble <- not inDouble

        elif c = '\n' && (inSingle || inDouble) then
            let lineStart = i + 1
            let nextNewline = original.IndexOf('\n', lineStart)
            let lineEnd =
                if nextNewline = -1 then original.Length
                else nextNewline
            let nextLine = original.[lineStart .. lineEnd - 1]
            if nextLine.TrimStart().StartsWith("#") then
                decision <- Deny "Command contains a quoted newline followed by a #-prefixed line, which can hide arguments from permission checks"

        i <- i + 1

    decision

/// Port of validate_carriage_return.
/// CR outside double quotes causes parser differentials (shell-quote treats
/// it as a word separator, bash does not). Misparsing ASK → Deny.
let private validateCarriageReturn (ctx: ValidationCtx) : SecurityDecision =
    let original = ctx.OriginalCommand
    if not (original.Contains('\r')) then
        Passthrough
    else

    let mutable inSingle = false
    let mutable inDouble = false
    let mutable escaped  = false
    let mutable decision = Passthrough
    let mutable i = 0

    while i < original.Length && decision = Passthrough do
        let c = original.[i]

        if escaped then
            escaped <- false

        elif c = '\\' && not inSingle then
            escaped <- true

        elif c = '\'' && not inDouble then
            inSingle <- not inSingle

        elif c = '"' && not inSingle then
            inDouble <- not inDouble

        elif c = '\r' && not inDouble then
            decision <- Deny @"Command contains carriage return (\r) which may cause parser differentials"

        i <- i + 1

    decision

/// Port of validate_newlines.
/// Detects newlines that could separate multiple commands.
/// NON-MISPARSING ASK in Python → DenyDeferred (evaluated after all
/// misparsing validators have passed).
let private validateNewlines (ctx: ValidationCtx) : SecurityDecision =
    let fu = ctx.FullyUnquotedPreStrip
    if not (Regex.IsMatch(fu, @"[\n\r]")) then
        Passthrough
    // Allow backslash-newline continuations; block anything else
    elif Regex.IsMatch(fu, @"(?<![\s]\\)[\n\r]\s*\S") then
        DenyDeferred "Command contains newlines that could separate multiple commands"
    else
        Passthrough

/// Port of validate_ifs_injection.
/// $IFS bypasses word-splitting security checks.
let private validateIfsInjection (ctx: ValidationCtx) : SecurityDecision =
    if Regex.IsMatch(ctx.OriginalCommand, @"\$IFS|\$\{[^}]*IFS") then
        Deny "Command contains IFS variable usage which could bypass security validation"
    else
        Passthrough

/// Port of validate_proc_environ_access.
/// /proc/*/environ exposes sensitive environment variables.
let private validateProcEnvironAccess (ctx: ValidationCtx) : SecurityDecision =
    if Regex.IsMatch(ctx.OriginalCommand, @"/proc/.*/environ") then
        Deny "Command accesses /proc/*/environ which could expose sensitive environment variables"
    else
        Passthrough

/// Port of validate_dangerous_patterns.
/// Detects backticks and COMMAND_SUBSTITUTION_PATTERNS in the unquoted content.
let private validateDangerousPatterns (ctx: ValidationCtx) : SecurityDecision =
    let unquoted = ctx.UnquotedContent
    if hasUnescapedChar unquoted '`' then
        Deny "Command contains backticks (`) for command substitution"
    else
        commandSubstitutionPatterns
        |> List.tryPick (fun (re, desc) ->
            if re.IsMatch(unquoted) then
                Some (Deny (sprintf "Command contains %s" desc))
            else None)
        |> Option.defaultValue Passthrough

/// Port of validate_redirections.
/// Detects input/output redirection in fully-unquoted content.
/// NON-MISPARSING ASK in Python → DenyDeferred.
let private validateRedirections (ctx: ValidationCtx) : SecurityDecision =
    let fu = ctx.FullyUnquotedContent
    if fu.Contains('<') then
        DenyDeferred "Command contains input redirection (<) which could read sensitive files"
    elif fu.Contains('>') then
        DenyDeferred "Command contains output redirection (>) which could write to arbitrary files"
    else
        Passthrough

/// Port of validate_backslash_escaped_whitespace.
/// Misparsing ASK in Python → Deny.
let private validateBackslashEscapedWhitespace (ctx: ValidationCtx) : SecurityDecision =
    if hasBackslashEscapedWhitespace ctx.OriginalCommand then
        Deny "Command contains backslash-escaped whitespace that could alter command parsing"
    else
        Passthrough

/// Port of validate_backslash_escaped_operators.
/// Backslash before a shell operator can hide command structure.
/// Misparsing ASK in Python → Deny.
let private validateBackslashEscapedOperators (ctx: ValidationCtx) : SecurityDecision =
    if hasBackslashEscapedOperator ctx.OriginalCommand then
        Deny "Command contains a backslash before a shell operator (;, |, &, <, >) which can hide command structure"
    else
        Passthrough

/// Port of validate_unicode_whitespace.
/// Unicode whitespace causes parser differentials between tools.
let private validateUnicodeWhitespace (ctx: ValidationCtx) : SecurityDecision =
    if unicodeWsRe.IsMatch(ctx.OriginalCommand) then
        Deny "Command contains Unicode whitespace characters that could cause parsing inconsistencies"
    else
        Passthrough

/// Port of validate_mid_word_hash.
/// Mid-word # is parsed differently by different shell parsers.
let private validateMidWordHash (ctx: ValidationCtx) : SecurityDecision =
    let text = ctx.UnquotedKeepQuoteChars
    // Match # preceded by non-whitespace, but NOT ${#
    if Regex.IsMatch(text, @"\S(?<!\$\{)#") then
        Deny "Command contains mid-word # which may be parsed differently by different tools"
    else
        Passthrough

/// Port of validate_brace_expansion.
/// Unquoted brace expansion ({a,b} or {1..5}) can inject arguments.
let private validateBraceExpansion (ctx: ValidationCtx) : SecurityDecision =
    let content = ctx.FullyUnquotedPreStrip

    // Count unescaped open and close braces
    let mutable openBraces  = 0
    let mutable closeBraces = 0
    for idx in 0 .. content.Length - 1 do
        if   content.[idx] = '{' && not (isEscapedAtPosition content idx) then
            openBraces  <- openBraces  + 1
        elif content.[idx] = '}' && not (isEscapedAtPosition content idx) then
            closeBraces <- closeBraces + 1

    // Excess closing braces indicate obfuscation
    if openBraces > 0 && closeBraces > openBraces then
        Deny "Command has excess closing braces (possible brace expansion obfuscation)"
    // Quoted brace inside brace context
    elif openBraces > 0 && Regex.IsMatch(ctx.OriginalCommand, "['\"][{}]['\"]") then
        Deny "Command contains quoted brace character inside brace context"
    else

    // Scan for actual brace expansion patterns ({a,b} or {a..b})
    let mutable i        = 0
    let mutable decision = Passthrough

    while i < content.Length && decision = Passthrough do
        if content.[i] <> '{' || isEscapedAtPosition content i then
            i <- i + 1
        else
            // Find matching closing brace (respecting nesting)
            let mutable depth        = 1
            let mutable matchingClose = -1
            let mutable j = i + 1
            while j < content.Length && matchingClose = -1 do
                if   content.[j] = '{' && not (isEscapedAtPosition content j) then
                    depth <- depth + 1
                elif content.[j] = '}' && not (isEscapedAtPosition content j) then
                    depth <- depth - 1
                    if depth = 0 then matchingClose <- j
                j <- j + 1

            if matchingClose = -1 then
                i <- i + 1
            else
                // Check inside the braces at outermost level for , or ..
                let mutable innerDepth = 0
                let mutable k = i + 1
                while k < matchingClose && decision = Passthrough do
                    let ch = content.[k]
                    if   ch = '{' && not (isEscapedAtPosition content k) then
                        innerDepth <- innerDepth + 1
                    elif ch = '}' && not (isEscapedAtPosition content k) then
                        innerDepth <- innerDepth - 1
                    elif innerDepth = 0 then
                        if ch = ',' then
                            decision <- Deny "Command contains brace expansion that could alter command parsing"
                        elif ch = '.' && k + 1 < matchingClose && content.[k + 1] = '.' then
                            decision <- Deny "Command contains brace expansion that could alter command parsing"
                    k <- k + 1
                i <- i + 1

    decision

/// Port of validate_zsh_dangerous_commands.
/// Detects Zsh-specific dangerous commands and fc -e (arbitrary editor execution).
let private validateZshDangerousCommands (ctx: ValidationCtx) : SecurityDecision =
    let original = ctx.OriginalCommand
    let trimmed  = original.Trim()
    let tokens   =
        trimmed.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)

    // Walk tokens: skip env-var assignments and precommand modifiers to find base command.
    let mutable baseCmd = ""
    let mutable idx     = 0
    while idx < tokens.Length && baseCmd = "" do
        let token = tokens.[idx]
        if Regex.IsMatch(token, @"^[A-Za-z_]\w*=") then
            idx <- idx + 1  // skip env-var assignment
        elif zshPrecommandModifiers.Contains(token) then
            idx <- idx + 1  // skip precommand modifier
        else
            baseCmd <- token
            idx <- idx + 1

    if zshDangerousCommands.Contains(baseCmd) then
        Deny (sprintf "Command uses Zsh-specific '%s' which can bypass security checks" baseCmd)
    elif baseCmd = "fc" && Regex.IsMatch(trimmed, @"\s-\S*e") then
        Deny "Command uses 'fc -e' which can execute arbitrary commands via editor"
    else
        Passthrough

// ── Chain runner ──────────────────────────────────────────────────────────────
// Port of Python bash_command_is_safe (minus destructive-pattern check, which
// is handled separately by checkDestructive below).
//
// Deferred semantics:
//   - All validators listed in _NON_MISPARSING_VALIDATORS in Python
//     (validate_newlines, validate_redirections) return DenyDeferred.
//   - All other ASK/DENY return Deny (short-circuit).
//   - The deferred list runs ONLY after all non-deferred validators pass.
//
// Early-Allow semantics:
//   - Python bash_command_is_safe converts ALLOW from early validators to
//     PASSTHROUGH before returning. In blueCode we surface Allow and handle
//     it in validateCommand (run destructive check, then return Ok).

let private runChain (ctx: ValidationCtx) : SecurityDecision =
    // Step 3 — early validators (order matches Python)
    // validateGitCommit MUST be here — do NOT comment out.
    let early = [
        validateEmpty
        validateIncompleteCommands
        validateGitCommit
    ]

    let earlyResult =
        early
        |> List.tryPick (fun v ->
            match v ctx with
            | Passthrough -> None
            | other       -> Some other)

    match earlyResult with
    | Some result -> result

    | None ->
        // Step 4 — non-deferred main validators (16, order matches Python)
        let nonDeferred : (ValidationCtx -> SecurityDecision) list = [
            validateJqCommand
            validateObfuscatedFlags
            validateShellMetacharacters
            validateDangerousVariables
            validateCommentQuoteDesync
            validateQuotedNewline
            validateCarriageReturn
            validateIfsInjection
            validateProcEnvironAccess
            validateDangerousPatterns
            validateBackslashEscapedWhitespace
            validateBackslashEscapedOperators
            validateUnicodeWhitespace
            validateMidWordHash
            validateBraceExpansion
            validateZshDangerousCommands
        ]

        // Step 5 — deferred validators (2 non-misparsing, order matches Python)
        let deferred : (ValidationCtx -> SecurityDecision) list = [
            validateNewlines
            validateRedirections
        ]

        let mainResult =
            nonDeferred
            |> List.tryPick (fun v ->
                match v ctx with
                | Passthrough -> None
                | other       -> Some other)

        match mainResult with
        | Some result -> result
        | None ->
            let deferredResult =
                deferred
                |> List.tryPick (fun v ->
                    match v ctx with
                    | Passthrough -> None
                    | other       -> Some other)
            match deferredResult with
            | Some result -> result
            | None        -> Passthrough

// ── Destructive-pattern check ─────────────────────────────────────────────────
// Port of Python get_destructive_command_warning + check_shell_security logic
// (with allow_destructive=false, which is blueCode's fixed mode).

let private checkDestructive (command: string) : SecurityDecision =
    destructivePatterns
    |> List.tryPick (fun (re, desc) ->
        if re.IsMatch(command) then
            Some (Deny (sprintf "Potentially destructive command blocked: %s" desc))
        else None)
    |> Option.defaultValue Passthrough

// ── Public API ────────────────────────────────────────────────────────────────
// READ_ONLY_COMMANDS / is_command_read_only intentionally SKIPPED.
// In the Python reference it is informational — it is NEVER consulted by
// check_shell_security or bash_command_is_safe as a deny gate. The full
// validator chain above is blueCode's primary security gate.

/// Validate a shell command. Returns Ok () if safe, Error reason if blocked.
/// All ASK results from the Python reference are treated as Deny (blueCode
/// is non-interactive — there is no "ask the user" path).
///
/// Consumed by FsToolExecutor.RunShell (Plan 03-02). Error reason maps to
/// ToolResult.SecurityDenied reason.
let validateCommand (command: string) : Result<unit, string> =
    // Step 1 — pre-context control-character check (no ctx needed)
    let preCtx = {
        OriginalCommand        = command
        BaseCommand            = ""
        UnquotedContent        = ""
        FullyUnquotedContent   = ""
        FullyUnquotedPreStrip  = ""
        UnquotedKeepQuoteChars = ""
    }
    match validateControlCharacters preCtx with
    | Deny r -> Error r
    | _ ->

    // Step 2 — build full ValidationCtx
    let trimmed = command.TrimStart()
    let baseCmd =
        if trimmed = "" then ""
        else trimmed.Split([| ' '; '\t' |], 2, StringSplitOptions.None).[0]

    let withDq, fullyUnquoted, keepQuoteChars = extractQuotedContent command

    let ctx = {
        OriginalCommand        = command
        BaseCommand            = baseCmd
        UnquotedContent        = withDq
        FullyUnquotedContent   = stripSafeRedirections fullyUnquoted
        FullyUnquotedPreStrip  = fullyUnquoted
        UnquotedKeepQuoteChars = keepQuoteChars
    }

    // Steps 3-5 — validator chain
    match runChain ctx with
    | Deny r | DenyDeferred r ->
        Error r
    | Allow ->
        // Early-allowed (empty or simple git commit).
        // Still apply destructive pattern check (agent runs unattended).
        match checkDestructive command with
        | Deny r | DenyDeferred r -> Error r
        | _                       -> Ok ()
    | Passthrough ->
        // All validators passed.
        match checkDestructive command with
        | Deny r | DenyDeferred r -> Error r
        | _                       -> Ok ()
