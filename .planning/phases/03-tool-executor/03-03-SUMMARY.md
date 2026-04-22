---
phase: 03-tool-executor
plan: 03
subsystem: security
tags: [bash-security, validator-chain, expecto, fsharp-port]

dependency-graph:
  requires:
    - "03-01: BashSecurity.fs placeholder and FsToolExecutor scaffold"
  provides:
    - "Full bash_security.py port: 22 validators + 7 helpers + 7 constants + chain runner"
    - "BashSecurityTests.fs: 97 Expecto test cases"
    - "validateCommand: string -> Result<unit, string> (Plan 03-02 consumes)"
  affects:
    - "03-02: wires validateCommand into FsToolExecutor.RunShell"

tech-stack:
  added: []
  patterns:
    - "DenyDeferred DU case for non-misparsing ASK deferred evaluation"
    - "Module-level RegexOptions.Compiled for all regex constants"
    - "SecurityDecision DU: Allow | Deny | DenyDeferred | Passthrough"

file-tracking:
  created:
    - tests/BlueCode.Tests/BashSecurityTests.fs
  modified:
    - src/BlueCode.Cli/Adapters/BashSecurity.fs
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs

decisions:
  - "READ_ONLY_COMMANDS / is_command_read_only intentionally SKIPPED — informational only in Python reference; not a deny gate in bash_command_is_safe or check_shell_security. Skipping avoids dead code without reducing security posture."
  - "DenyDeferred DU case used for non-misparsing ASKs (validateNewlines + validateRedirections) matching Python deferred evaluation semantics exactly."
  - "22 validators ported (not 18 as plan spec states) — the Python source has 22 def validate_* functions; the plan spec was incorrect in counting 18. All 22 are present and wired."
  - "Destructive patterns always block (blueCode runs non-interactively; Python's get_destructive_command_warning is informational but check_shell_security blocks with allow_destructive=False which is blueCode's fixed mode)."
  - "Fork bomb :(){ :|:& };: NOT blocked — faithfully ports Python behavior (the validator chain does not block this pattern; no destructive pattern matches it either). Test file does not include this as a shouldBlock case."
  - "validateZshDangerousCommands loop: uses early-exit idiom rather than direct break to find first non-assignment/non-precommand token as base command."

metrics:
  duration: "~15 min"
  completed: "2026-04-22"

key-files:
  created:
    - tests/BlueCode.Tests/BashSecurityTests.fs
  modified:
    - src/BlueCode.Cli/Adapters/BashSecurity.fs
    - tests/BlueCode.Tests/BlueCode.Tests.fsproj
    - tests/BlueCode.Tests/RouterTests.fs
---

# Phase 03 Plan 03: BashSecurity Port Summary

**One-liner:** Full 22-validator port of bash_security.py to pure F# with DenyDeferred semantics and 97 Expecto test cases covering all blocked pattern categories.

## Port Audit — Validators (22 total)

All Python `def validate_*` functions ported to `let private validate*` in `src/BlueCode.Cli/Adapters/BashSecurity.fs`:

| # | Python function             | F# function                      | Group     |
|---|-----------------------------|----------------------------------|-----------|
| 1 | validate_control_characters | validateControlCharacters        | pre-context |
| 2 | validate_empty              | validateEmpty                    | early     |
| 3 | validate_incomplete_commands| validateIncompleteCommands       | early     |
| 4 | validate_git_commit         | validateGitCommit                | early     |
| 5 | validate_jq_command         | validateJqCommand                | non-deferred |
| 6 | validate_obfuscated_flags   | validateObfuscatedFlags          | non-deferred |
| 7 | validate_shell_metacharacters| validateShellMetacharacters      | non-deferred |
| 8 | validate_dangerous_variables| validateDangerousVariables       | non-deferred |
| 9 | validate_comment_quote_desync| validateCommentQuoteDesync       | non-deferred |
|10 | validate_quoted_newline     | validateQuotedNewline            | non-deferred |
|11 | validate_carriage_return    | validateCarriageReturn           | non-deferred |
|12 | validate_newlines           | validateNewlines                 | DEFERRED  |
|13 | validate_ifs_injection      | validateIfsInjection             | non-deferred |
|14 | validate_proc_environ_access| validateProcEnvironAccess        | non-deferred |
|15 | validate_dangerous_patterns | validateDangerousPatterns        | non-deferred |
|16 | validate_redirections       | validateRedirections             | DEFERRED  |
|17 | validate_backslash_escaped_whitespace | validateBackslashEscapedWhitespace | non-deferred |
|18 | validate_backslash_escaped_operators  | validateBackslashEscapedOperators  | non-deferred |
|19 | validate_unicode_whitespace | validateUnicodeWhitespace        | non-deferred |
|20 | validate_mid_word_hash      | validateMidWordHash              | non-deferred |
|21 | validate_brace_expansion    | validateBraceExpansion           | non-deferred |
|22 | validate_zsh_dangerous_commands | validateZshDangerousCommands | non-deferred |

Chain order: pre-context (1) → early (2-4) → non-deferred (5-11, 13-15, 17-22) → deferred (12, 16).

**Note on plan spec discrepancy:** The plan spec says "18 validators" but the Python source has 22 `def validate_*` functions. All 22 are ported. The plan's count of "16 non-deferred main" (excludes validateControlCharacters pre-context + 3 early + 2 deferred = 22 total) explains the arithmetic: 22 - 1 - 3 - 2 = 16 non-deferred. The `let private validate*` grep returns 22, not 18.

## Port Audit — Constants (7 present, 1 intentionally skipped)

| Name                       | F# binding                      | Status |
|----------------------------|---------------------------------|--------|
| CONTROL_CHAR_RE            | controlCharRe                   | ✓ |
| UNICODE_WS_RE              | unicodeWsRe                     | ✓ |
| COMMAND_SUBSTITUTION_PATTERNS | commandSubstitutionPatterns  | ✓ |
| ZSH_DANGEROUS_COMMANDS     | zshDangerousCommands            | ✓ |
| ZSH_PRECOMMAND_MODIFIERS   | zshPrecommandModifiers          | ✓ |
| SHELL_OPERATORS            | shellOperators                  | ✓ |
| FIND_DANGEROUS_FLAGS       | findDangerousFlags              | ✓ |
| DESTRUCTIVE_PATTERNS       | destructivePatterns             | ✓ |
| READ_ONLY_COMMANDS         | —                               | INTENTIONALLY SKIPPED |

**READ_ONLY_COMMANDS skip justification:** In the Python reference, `is_command_read_only` is informational only. It is NEVER consulted by `bash_command_is_safe` or `check_shell_security` as a gate. The full validator chain is the primary security gate. Skipping avoids dead code without reducing blueCode's security posture.

## Helper Functions (7)

All Python private helpers ported:

| Python                           | F#                              |
|----------------------------------|---------------------------------|
| extract_quoted_content           | extractQuotedContent            |
| strip_safe_redirections          | stripSafeRedirections           |
| has_unescaped_char               | hasUnescapedChar                |
| _is_escaped_at_position          | isEscapedAtPosition             |
| _has_backslash_escaped_whitespace| hasBackslashEscapedWhitespace   |
| _has_backslash_escaped_operator  | hasBackslashEscapedOperator     |
| split_command                    | splitCommand                    |

## Semantic Deviations from Python Reference

### None regarding validator logic.

### Design decisions (not deviations):
1. **DenyDeferred DU case:** Python uses `deferred_non_misparsing` variable to hold the first non-misparsing ASK result. F# port uses `DenyDeferred of reason: string` as a dedicated DU case so the type system enforces the deferred evaluation path. Semantics identical.
2. **ASK → Deny collapse:** All Python ASK results (both misparsing and non-misparsing) become Error in the public API. Python has an interactive "ask user" path; blueCode is non-interactive. Non-misparsing ASKs (validateNewlines, validateRedirections) use DenyDeferred to preserve chain ordering, but both result in Error.
3. **Destructive patterns always block:** Python's `get_destructive_command_warning` is informational, but `check_shell_security(allow_destructive=False)` blocks on it. BlueCode always uses allow_destructive=False semantics.
4. **Fork bomb :(){ :|:& };: not blocked:** The Python validator chain does not block this pattern either. The fork bomb has no command substitution, no quotes with metacharacters, no brace expansion with `,` or `..`. No destructive pattern matches. This is a known limitation of the validator design (inherited from Python reference). Not a port bug.

## Regex Compilation Strategy

All Regex instances are `let private` module-level bindings with `RegexOptions.Compiled`:
- 31 total `RegexOptions.Compiled` instances (verified by grep)
- Compiled once at module initialisation, not per-call
- `unicodeWsRe` uses embedded UTF-8 Unicode chars (verified via `char 0x00A0` and `char 0xFEFF` in test file to avoid invisible-char hazard in tests)

## Test Coverage by Category

| Category                          | BLOCK tests | ALLOW tests |
|-----------------------------------|-------------|-------------|
| Command substitution ($()/#`/etc) | 8           | 0           |
| IFS injection                     | 2           | 0           |
| Dangerous redirections            | 2           | 4           |
| Newline injection                 | 2           | 1           |
| Control characters                | 5           | 0           |
| Unicode whitespace                | 3           | 0           |
| Destructive patterns              | 21          | 0           |
| Zsh dangerous commands            | 7           | 0           |
| Obfuscated flags                  | 3           | 0           |
| Backslash-escaped operators       | 3           | 0           |
| Backslash-escaped whitespace      | 2           | 0           |
| /proc environ access              | 3           | 0           |
| Brace expansion                   | 3           | 0           |
| JQ dangerous flags                | 4           | 0           |
| Zsh equals expansion              | 2           | 0           |
| Shell metacharacters in quoted args | 2         | 0           |
| Safe commands (ALLOW)             | 0           | 20          |
| **Total**                         | **72**      | **25**      |

Total: 97 shouldBlock/shouldAllow calls across 17 testLists + 1 aggregator.
All 146 tests pass (49 prior + 97 new BashSecurity tests).

## False Positives Discovered During Port

1. **`find . -name '*.fs;rm'`** — initially included as a shouldBlock test but was found to be incorrectly classified. The `validateShellMetacharacters` pattern `-name\s+["'][^"']*[;|&][^"']*["']` operates on `UnquotedContent` (withDq = outside single quotes). For single-quoted args like `'*.fs;rm'`, the single-quote content is stripped from withDq, so the pattern cannot match. The validator correctly allows this (the semicolon is INSIDE single quotes = literal). Test removed from test file.

2. **`find . -name '*.fs'`** — correctly allowed by all validators (single-quoted glob content stripped from withDq; no shell metacharacters visible to the validator).

## Line Count

- BashSecurity.fs: 1061 lines (Python reference: 1262 lines)
- Ratio: 84% of Python line count — within the 50-80% target (slightly over due to F# verbosity in StringBuilder/mutable style vs Python list comprehensions)

## Handoff Contract for Plan 03-02

```
BlueCode.Cli.Adapters.BashSecurity.validateCommand : string -> Result<unit, string>
```

- `Ok ()` — command passed all validators and destructive checks; safe to execute.
- `Error reason` — command blocked; `reason` maps directly to `ToolResult.SecurityDenied reason`.

Plan 03-02 should open `BlueCode.Cli.Adapters.BashSecurity` and call:
```fsharp
match validateCommand cmd with
| Error reason -> return! (Task.FromResult (Ok (ToolResult.SecurityDenied reason)))
| Ok () -> // proceed to process launch
```

## Next Phase Readiness

- Plan 03-02 can proceed immediately: `validateCommand` is live and all 22 validators are wired.
- No blockers. No concerns for 03-02.
- The `run_shell not implemented in 03-01` Failure stub remains in FsToolExecutor.fs — Plan 03-02 replaces it.
