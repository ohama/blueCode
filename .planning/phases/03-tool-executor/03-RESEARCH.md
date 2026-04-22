# Phase 3: Tool Executor - Research

**Researched:** 2026-04-22
**Domain:** F# filesystem tools, shell process management, security validation, .NET 10 IO
**Confidence:** HIGH

---

## Summary

Phase 3 implements `IToolExecutor` in `FsToolExecutor.fs` and the security validation chain in `BashSecurity.fs`. The interface already exists in `Ports.fs` — the signature is `ExecuteAsync: Tool -> CancellationToken -> Task<Result<ToolResult, AgentError>>`. The `ToolResult` DU with all five cases (`Success | Failure | SecurityDenied | PathEscapeBlocked | Timeout`) exists in `Domain.fs`. This phase's job is to fill in the implementation, not redesign the types.

The highest-risk component is `BashSecurity.fs` — a port of the 1261-line `bash_security.py`. The Python file has been read in full. It contains 18 validator functions organized in a chain with short-circuit semantics, 4 pre-compiled regex constant groups, 3 helper functions, and 2 support functions for command splitting and quote-aware content extraction. This document provides a complete inventory so the planner can write task prompts without re-reading the Python source.

The second-highest-risk component is `run_shell` process management — stdout/stderr concurrent reading to avoid deadlock (dotnet/runtime #98347), process tree kill on timeout, and working-directory lock. The patterns are well-understood and verified against .NET 10 APIs.

**Primary recommendation:** Implement `BashSecurity.fs` first as a pure module with no IO, then `FsToolExecutor.fs` which imports it. All security checks run synchronously before process launch — no async in the security validator chain.

---

## Standard Stack

No new NuGet packages required for Phase 3. All needed APIs are inbox in .NET 10.

### Core (.NET 10 inbox)

| API | Namespace | Purpose | Notes |
|-----|-----------|---------|-------|
| `System.IO.File` | `System.IO` | `ReadAllTextAsync`, `WriteAllTextAsync`, `ReadLinesAsync` | Use `ReadLinesAsync` for large files (lazy enumerable) |
| `System.IO.Directory` | `System.IO` | `GetFileSystemEntries`, `EnumerateFileSystemEntries` | For list_dir; `EnumerateFileSystemEntries` is lazy |
| `System.IO.Path` | `System.IO` | `GetFullPath`, `Combine`, `IsPathRooted` | Path normalization and escape detection |
| `System.Diagnostics.Process` | `System.Diagnostics` | Process launch, stdout/stderr redirect, kill | `Kill(entireProcessTree: true)` since .NET 5; confirmed .NET 10 |
| `System.Text.RegularExpressions.Regex` | `System.Text.RegularExpressions` | Compile security validator patterns once at module init | Use `Regex.IsMatch` for one-shot checks; `Regex(pattern, RegexOptions.Compiled)` for hot paths |
| `System.Threading.CancellationTokenSource` | `System.Threading` | Linked token source for 30s shell timeout | `CancellationTokenSource.CreateLinkedTokenSource(outerCt)` then `cts.CancelAfter(30000)` |

### Existing (from prior phases)

| Library | Version | Already Present | Use In Phase 3 |
|---------|---------|-----------------|----------------|
| `FsToolkit.ErrorHandling` | 5.2.0 | Yes (transitive via Core) | `taskResult {}` CE in `FsToolExecutor.fs` |
| `BlueCode.Core.Domain` | n/a | Yes | `Tool`, `ToolResult`, `FilePath`, `Command`, `Timeout` DUs |
| `BlueCode.Core.Ports` | n/a | Yes | `IToolExecutor` interface to implement |

### Installation

No new packages needed. No `dotnet add package` commands required for Phase 3.

---

## bash_security.py — Complete Inventory

This is the critical deliverable for Phase 3 planning. All 18 validators, their chain order, and F# port strategy are documented here.

### Type System (Python → F# mapping)

**Python `SecurityBehavior` enum:**
- `ALLOW` — command is safe, auto-approve (early exit)
- `ASK` — needs user confirmation (in blueCode: map to `SecurityDenied`)
- `DENY` — outright blocked (in blueCode: map to `SecurityDenied`)
- `PASSTHROUGH` — no opinion, continue chain

**Python `SecurityResult` dataclass:**
```python
@dataclass(frozen=True)
class SecurityResult:
    behavior: SecurityBehavior
    message: str
    is_misparsing: bool = False
```

**F# equivalent DU:**
```fsharp
type SecurityDecision =
    | Allow                         // safe, proceed
    | Deny of reason: string        // blocked, return SecurityDenied
    | Passthrough                   // no opinion, run next validator
```

`is_misparsing` from Python is used only to classify ASK results. In blueCode where ASK → Deny, `is_misparsing` is informational only and can be included in the `reason` string (e.g., prefix with `"[misparsing] "`). The Python code promotes `misparsing` ASK results ahead of `non-misparsing` ASK results in the deferred evaluation logic — this matters for chain ordering (see Main Entry Point section below).

### Pre-Compiled Constants

All constants defined at module/file top level. Port as F# `let` bindings at module scope so Regex objects are compiled once at startup.

#### `COMMAND_SUBSTITUTION_PATTERNS` (12 entries)

```python
COMMAND_SUBSTITUTION_PATTERNS: list[tuple[re.Pattern[str], str]] = [
    (re.compile(r'<\('), 'process substitution <()'),
    (re.compile(r'>\('), 'process substitution >()'),
    (re.compile(r'=\('), 'Zsh process substitution =()'),
    (re.compile(r'(?:^|[\s;&|])=[a-zA-Z_]'), 'Zsh equals expansion (=cmd)'),
    (re.compile(r'\$\('), '$() command substitution'),
    (re.compile(r'\$\{'), '${} parameter substitution'),
    (re.compile(r'\$\['), '$[] legacy arithmetic expansion'),
    (re.compile(r'~\['), 'Zsh-style parameter expansion'),
    (re.compile(r'\(e:'), 'Zsh-style glob qualifiers'),
    (re.compile(r'\(\+'), 'Zsh glob qualifier with command execution'),
    (re.compile(r'\}\s*always\s*\{'), 'Zsh always block (try/always construct)'),
    (re.compile(r'<#'), 'PowerShell comment syntax'),
]
```

F# port: `let private commandSubstitutionPatterns: (Regex * string) list = [...]`

#### `ZSH_DANGEROUS_COMMANDS` (frozenset, 13 entries)

```
zmodload, emulate, sysopen, sysread, syswrite, sysseek,
zpty, ztcp, zsocket, mapfile, zf_rm, zf_mv, zf_ln,
zf_chmod, zf_chown, zf_mkdir, zf_rmdir, zf_chgrp
```

F# port: `let private zshDangerousCommands = Set.ofList [...]`

#### `ZSH_PRECOMMAND_MODIFIERS` (frozenset, 4 entries)

```
command, builtin, noglob, nocorrect
```

Used in `validate_zsh_dangerous_commands` to skip leading env-var assignments and precommand modifiers when extracting the base command.

#### `CONTROL_CHAR_RE` and `UNICODE_WS_RE`

```python
CONTROL_CHAR_RE = re.compile(r'[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]')
UNICODE_WS_RE = re.compile(
    r'[   -     　﻿]'
)
```

F# port: module-level `let private` Regex values.

#### `DESTRUCTIVE_PATTERNS` (16 entries)

Used only by `get_destructive_command_warning`, which is informational (returns a string warning, not a block). In blueCode's `check_shell_security`, destructive commands are blocked only when `allow_destructive=False`.

Key destructive patterns:
- `git reset --hard`, `git push --force/-f`, `git clean -f`, `git checkout -- .`
- `git restore -- .`, `git stash drop/clear`, `git branch -D`
- `git commit/push/merge --no-verify`, `git commit --amend`
- `rm -rf` (recursive+force), `rm -r` (recursive), `rm -f` (force)
- `DROP/TRUNCATE TABLE/DATABASE/SCHEMA`, `DELETE FROM table` (no WHERE)
- `kubectl delete`, `terraform destroy`

In blueCode: these patterns should also cause `SecurityDenied` since the agent runs unattended.

#### `READ_ONLY_COMMANDS` (large frozenset)

Commands safe to run without validation. Key entries:
```
cat, head, tail, less, more, bat, batcat
grep, egrep, fgrep, rg, ag, ack
ls, ll, la, dir, tree, exa, eza
stat, file, wc, du, df, readlink, realpath
md5sum, sha256sum, sha1sum
uname, hostname, whoami, id, groups, date, uptime, free
ls, ps, top, htop, pgrep
echo, printf, true, false
which, whereis, type, command, hash, dirname, basename, pwd
sort, uniq, cut, tr, fold, fmt, nl, rev, column, paste, join, comm, tee
awk, gawk, mawk
sed  (read-only only without -i)
diff, cmp, colordiff
base64, xxd, od, hexdump
find, fd, fdfind, locate, mlocate  (read-only without -exec/-delete)
man, info, help
env, printenv, set
ping, dig, nslookup, host, ifconfig, ip
git  (read-only subcommands only)
python, python3, node  (only with -c/--version/--help/-V/-h)
```

#### `GIT_READ_ONLY_SUBCOMMANDS` (frozenset)

```
status, log, diff, show, branch, remote, tag, describe, rev-parse,
rev-list, ls-files, ls-tree, ls-remote, cat-file, name-rev,
shortlog, blame, grep, reflog, stash list, config, version
```

#### `FIND_DANGEROUS_FLAGS` (frozenset)

```
-exec, -execdir, -ok, -okdir, -delete
```

#### `SHELL_OPERATORS` (frozenset)

```
; | & < >
```

Used in `validate_backslash_escaped_operators`.

### Helper Functions

#### `extract_quoted_content(command: str) -> tuple[str, str, str]`

Walks the command character-by-character tracking quote state. Returns:
1. `with_double_quotes`: content outside single quotes (double-quoted content preserved)
2. `fully_unquoted`: content outside both single AND double quotes (with safe redirections not yet stripped)
3. `unquoted_keep_quote_chars`: like `fully_unquoted` but preserves `'` and `"` characters

This is the core building block. It handles:
- `\\` escapes (backslash)
- `'...'` single quotes (suppress all special chars including backslash)
- `"..."` double quotes (suppress most but not `$`, `` ` ``, `\`)

F# port: `let private extractQuotedContent (command: string) : string * string * string`

Pure string traversal with 3 mutable accumulators (or use a `fold` with state record). No regex.

#### `strip_safe_redirections(content: str) -> str`

Strips `2>&1`, `>/dev/null`, `</dev/null` from content before dangerous-variable checks.

```python
result = re.sub(r'\s+2\s*>&\s*1(?=\s|$)', '', result)
result = re.sub(r'[012]?\s*>\s*/dev/null(?=\s|$)', '', result)
result = re.sub(r'\s*<\s*/dev/null(?=\s|$)', '', result)
```

F# port: `let private stripSafeRedirections (content: string) : string` — 3 `Regex.Replace` calls.

#### `has_unescaped_char(content: str, char: str) -> bool`

Walks content checking for unescaped occurrences of a specific single character. Correctly counts consecutive backslashes (even number = not escaped).

F# port: `let private hasUnescapedChar (content: string) (ch: char) : bool`

#### `split_command(command: str) -> list[str]`

Splits compound commands on `;`, `||`, `|`, `&&` while respecting quotes. Handles:
- `&&` and `||` as two-character operators
- Single `&` (background job) does NOT split — it's added to current segment
- Single `|` splits
- `;` splits
- Quote tracking (single and double)

F# port: `let private splitCommand (command: string) : string list`

#### `is_command_read_only(command: str) -> bool`

Not used directly in `bash_command_is_safe`. Used for separate auto-approval logic. In blueCode, this function is informational; the full validator chain is the primary gate.

#### `_is_escaped_at_position(content: str, pos: int) -> bool`

Counts consecutive backslashes before `pos` — odd count means escaped.
Used by `validate_brace_expansion`.

#### `_has_backslash_escaped_whitespace(command: str) -> bool`

Walks command tracking quotes. Returns True if a `\<space>` or `\<tab>` appears outside quotes.
Used by `validate_backslash_escaped_whitespace`.

#### `_has_backslash_escaped_operator(command: str) -> bool`

Walks command tracking quotes. Returns True if a `\;`, `\|`, `\&`, `\<`, `\>` appears outside quotes.
Used by `validate_backslash_escaped_operators`.

### `ValidationContext` — What Gets Pre-Computed

```python
@dataclass
class ValidationContext:
    original_command: str
    base_command: str              # first word of the command
    unquoted_content: str          # = with_double_quotes from extract_quoted_content
    fully_unquoted_content: str    # = strip_safe_redirections(fully_unquoted)
    fully_unquoted_pre_strip: str  # = fully_unquoted (before stripping safe redirections)
    unquoted_keep_quote_chars: str # = third return value of extract_quoted_content
```

F# port: Use an F# record `type ValidationCtx = { ... }` with the same 6 fields.

### The 18 Validators — Complete Inventory

Listed in chain order (see Main Entry Point section for exact grouping).

---

#### Group A: Pre-Context Validator (runs before ValidationContext is built)

**1. `validate_control_characters(ctx)` → SecurityResult**

Checks `original_command` against `CONTROL_CHAR_RE` (chars 0x00-0x08, 0x0B-0x0C, 0x0E-0x1F, 0x7F — excludes tab/newline/CR).
- Match → `ASK` (misparsing=True): `"Command contains non-printable control characters that could bypass security checks"`
- No match → `PASSTHROUGH`

**Runs on a minimal context** (only `original_command` field is populated) before the full `ValidationContext` is built. This is the only validator that runs before `extract_quoted_content` is called.

---

#### Group B: Early Validators (can ALLOW or block; run before main chain)

**2. `validate_empty(ctx)` → SecurityResult**

Checks `original_command.strip()` is empty.
- Empty → `ALLOW`: `"Empty command is safe"`
- Not empty → `PASSTHROUGH`

**3. `validate_incomplete_commands(ctx)` → SecurityResult**

Checks `original_command` for fragment patterns:
- Starts with `\t` (tab) → `ASK` (misparsing): `"Command appears to be an incomplete fragment (starts with tab)"`
- Trimmed starts with `-` → `ASK` (misparsing): `"Command appears to be an incomplete fragment (starts with flags)"`
- Starts with `&&`, `||`, `;`, `>>?`, `<` → `ASK` (misparsing): `"Command appears to be a continuation line (starts with operator)"`
- Otherwise → `PASSTHROUGH`

**4. `validate_git_commit(ctx)` → SecurityResult**

Only applies when `base_command == 'git'` and `original_command` matches `^git\s+commit\s+`.

Fast-allow path: matches the pattern `^git commit [no-bad-chars] -m ['|"] message ['|"] remainder`. If:
- Message is in double quotes and contains `$(`, `` ` ``, or `${` → `ASK`
- Remainder has shell metacharacters → `PASSTHROUGH` (falls through to full validation)
- Message starts with `-` → `ASK` (misparsing)
- Otherwise → `ALLOW`: `"Git commit with simple quoted message is allowed"`

If no pattern match → `PASSTHROUGH`: `"Git commit needs validation"`

Has a backslash fast-exit: if `\\` in `original_command` → `PASSTHROUGH`.

Note: When early validators return `ALLOW`, the chain returns `PASSTHROUGH('...')` to the outer caller — the ALLOW from an early validator translates to "passed security checks" not to literal ALLOW of the SecurityBehavior enum. This is an implementation detail of `bash_command_is_safe`.

---

#### Group C: Main Validators (18 minus 4 above = 14 here; run in order with deferred semantics)

The main chain runs the following validators IN ORDER. Two validators (`validate_newlines`, `validate_redirections`) are "non-misparsing" and their ASK results are deferred until after all misparsing validators have had a chance to return. First misparsing ASK wins; if none, deferred non-misparsing ASK wins.

**5. `validate_jq_command(ctx)` → SecurityResult**

Only applies when `base_command == 'jq'`.
- `system(` in `original_command` → `ASK`: `"jq command contains system() function..."`
- `-f`, `--from-file`, `--rawfile`, `--slurpfile`, `-L`, `--library-path` in args after `jq` → `ASK`: `"jq command contains dangerous flags..."`
- Otherwise → `PASSTHROUGH`

**6. `validate_obfuscated_flags(ctx)` → SecurityResult**

Checks for quote-based flag obfuscation patterns. Does not apply to bare `echo` without shell operators.

Checks in order (first match wins):
- `$'...'` (ANSI-C quoting) anywhere → `ASK`: `"Command contains ANSI-C quoting which can hide characters"`
- `$"..."` (locale quoting) anywhere → `ASK`: `"Command contains locale quoting which can hide characters"`
- `$['"]'` then `-` (empty special quotes before dash) → `ASK`: `"Command contains empty special quotes before dash (potential bypass)"`
- `(''|""){1,}\s*-` at word boundary → `ASK`: `"Command contains empty quotes before dash (potential bypass)"`
- `(""|''){1,}['"][-]` (empty quote pair adjacent to quoted dash) → `ASK`: `"Command contains empty quote pair adjacent to quoted dash"`
- 3+ consecutive quotes at word start → `ASK`: `"Command contains consecutive quote characters at word start"`
- Per-character scan: whitespace followed by quote containing a flag-like pattern (`-[a-zA-Z0-9$]`) → `ASK`: `"Command contains quoted characters in flag names"`
- Per-character scan: whitespace followed by dash with quotes mixed in flag content → `ASK`: `"Command contains quoted characters in flag names"`

Exception: `cut -d'...'` is specifically excluded from the last check (delimiter flag allows quoted char).

**7. `validate_shell_metacharacters(ctx)` → SecurityResult**

Uses `unquoted_content` (outside single quotes only).
- Quoted string containing `;` or `&` → `ASK`: `"Command contains shell metacharacters (;, |, or &) in arguments"`
- `-name`, `-path`, `-iname` with quoted arg containing `;`, `|`, `&` → `ASK`

**8. `validate_dangerous_variables(ctx)` → SecurityResult**

Uses `fully_unquoted_content` (outside all quotes, safe redirections stripped).
- Variable in redirect context: `[<>|]\s*\$[A-Za-z_]` or `\$[A-Za-z_]\w*\s*[|<>]` → `ASK`: `"Command contains variables in dangerous contexts (redirections or pipes)"`

**9. `validate_comment_quote_desync(ctx)` → SecurityResult**

Walks `original_command` tracking quote state. When unquoted `#` found:
- Extracts comment text to end of line
- If comment contains `'` or `"` → `ASK` (misparsing): `"Command contains quote characters inside a # comment which can desync quote tracking"`

**10. `validate_quoted_newline(ctx)` → SecurityResult**

Only runs if `\n` and `#` both appear in `original_command`.
Walks tracking quotes. When `\n` found inside a quote:
- Looks at next line start; if it begins with `#` → `ASK` (misparsing): `"Command contains a quoted newline followed by a #-prefixed line, which can hide arguments from permission checks"`

**11. `validate_carriage_return(ctx)` → SecurityResult**

Only runs if `\r` in `original_command`.
Walks tracking quotes (excluding double quotes). CR outside double quotes → `ASK` (misparsing): `"Command contains carriage return (\\r) which may cause parser differentials"`

**12. `validate_newlines(ctx)` → SecurityResult** ⚠️ DEFERRED (non-misparsing)

Uses `fully_unquoted_pre_strip` (before safe-redirection stripping).
- No `\n` or `\r` → `PASSTHROUGH`
- Newline followed by non-whitespace (not backslash-newline continuation) → `ASK`: `"Command contains newlines that could separate multiple commands"`

Regex: `(?<![\s]\\)[\n\r]\s*\S` — the lookbehind checks for space-backslash before the newline.

**13. `validate_ifs_injection(ctx)` → SecurityResult**

Checks `original_command` for `$IFS` or `${...IFS...}` → `ASK`: `"Command contains IFS variable usage which could bypass security validation"`

Pattern: `\$IFS|\$\{[^}]*IFS`

**14. `validate_proc_environ_access(ctx)` → SecurityResult**

Checks `original_command` for `/proc/.*/environ` → `ASK`: `"Command accesses /proc/*/environ which could expose sensitive environment variables"`

**15. `validate_dangerous_patterns(ctx)` → SecurityResult**

Uses `unquoted_content` (outside single quotes).
- `hasUnescapedChar(unquoted_content, '`')` → `ASK`: `"Command contains backticks (`) for command substitution"`
- Any pattern from `COMMAND_SUBSTITUTION_PATTERNS` matches → `ASK`: `"Command contains {description}"`

**16. `validate_redirections(ctx)` → SecurityResult** ⚠️ DEFERRED (non-misparsing)

Uses `fully_unquoted_content` (after safe-redirection stripping).
- `<` in content → `ASK`: `"Command contains input redirection (<) which could read sensitive files"`
- `>` in content → `ASK`: `"Command contains output redirection (>) which could write to arbitrary files"`

Note: Safe redirections (`>/dev/null`, `2>&1`, `</dev/null`) are already stripped by `strip_safe_redirections`, so `>` and `<` here are genuinely dangerous ones.

**17. `validate_backslash_escaped_whitespace(ctx)` → SecurityResult**

Delegates to `_has_backslash_escaped_whitespace(original_command)`.
- True → `ASK` (misparsing): `"Command contains backslash-escaped whitespace that could alter command parsing"`

**18. `validate_backslash_escaped_operators(ctx)` → SecurityResult**

Delegates to `_has_backslash_escaped_operator(original_command)`.
- True → `ASK` (misparsing): `"Command contains a backslash before a shell operator (;, |, &, <, >) which can hide command structure"`

**19. `validate_unicode_whitespace(ctx)` → SecurityResult**

Checks `original_command` against `UNICODE_WS_RE`.
- Match → `ASK`: `"Command contains Unicode whitespace characters that could cause parsing inconsistencies"`

**20. `validate_mid_word_hash(ctx)` → SecurityResult**

Uses `unquoted_keep_quote_chars`.
Pattern `\S(?<!\$\{)#` — `#` preceded by non-whitespace, excluding `${#` (array length).
- Match → `ASK`: `"Command contains mid-word # which may be parsed differently by different tools"`

**21. `validate_brace_expansion(ctx)` → SecurityResult**

Uses `fully_unquoted_pre_strip`.

Three sub-checks:
1. More closing braces than opening (after counting unescaped) → `ASK`: `"Command has excess closing braces (possible brace expansion obfuscation)"`
2. Open braces exist AND quoted brace `['"]{}'"]` in `original_command` → `ASK`: `"Command contains quoted brace character inside brace context"`
3. Per-character scan for `{...}` pattern containing `,` or `..` at outer depth → `ASK`: `"Command contains brace expansion that could alter command parsing"`

**22. `validate_zsh_dangerous_commands(ctx)` → SecurityResult**

Tokenizes `original_command`, skipping leading env-var assignments (`KEY=value`) and `ZSH_PRECOMMAND_MODIFIERS`, to find the base command:
- If base command is in `ZSH_DANGEROUS_COMMANDS` → `ASK`: `"Command uses Zsh-specific '{cmd}' which can bypass security checks"`
- If base is `fc` and command contains `-\S*e` → `ASK`: `"Command uses 'fc -e' which can execute arbitrary commands via editor"`

---

### Main Entry Point — Chain Order and Short-Circuit Logic

```python
def bash_command_is_safe(command: str) -> SecurityResult:
```

**Step 1:** Run `validate_control_characters` with a partial context (only `original_command`). If not PASSTHROUGH, return immediately.

**Step 2:** Build `ValidationContext`:
```python
base_command = command.split()[0] if command.strip() else ''
with_double_quotes, fully_unquoted, unquoted_keep_quote_chars = extract_quoted_content(command)
ctx = ValidationContext(
    original_command=command,
    base_command=base_command,
    unquoted_content=with_double_quotes,
    fully_unquoted_content=strip_safe_redirections(fully_unquoted),
    fully_unquoted_pre_strip=fully_unquoted,
    unquoted_keep_quote_chars=unquoted_keep_quote_chars,
)
```

**Step 3:** Run early validators `[validate_empty, validate_incomplete_commands, validate_git_commit]`:
- If `ALLOW` → return `PASSTHROUGH` (command passed, proceed to execution)
- If non-PASSTHROUGH (ASK/DENY) → return immediately
- If `PASSTHROUGH` → continue to next

**Step 4:** Run main validators in this exact order:
```
validate_jq_command
validate_obfuscated_flags
validate_shell_metacharacters
validate_dangerous_variables
validate_comment_quote_desync
validate_quoted_newline
validate_carriage_return
validate_newlines          ← NON-MISPARSING: defer result
validate_ifs_injection
validate_proc_environ_access
validate_dangerous_patterns
validate_redirections       ← NON-MISPARSING: defer result
validate_backslash_escaped_whitespace
validate_backslash_escaped_operators
validate_unicode_whitespace
validate_mid_word_hash
validate_brace_expansion
validate_zsh_dangerous_commands
```

**Deferred logic:** `validate_newlines` and `validate_redirections` save their ASK result as `deferred_non_misparsing` instead of returning immediately. All other ASK results (misparsing) return immediately and set `is_misparsing=True` on the returned result.

**Step 5:** After the main chain:
- If any deferred non-misparsing result exists, return it
- Otherwise return `PASSTHROUGH("Command passed all security checks")`

### Integration Function — `check_shell_security`

```python
def check_shell_security(command, *, allow_shell=True, allow_destructive=False) -> tuple[bool, str]:
```

This is the top-level function called by the agent. In blueCode it maps to:

```fsharp
let validateCommand (command: string) : Result<unit, string> =
```

Logic:
1. Run `bash_command_is_safe(command)` (the chain)
2. If `DENY` → blocked with reason
3. If `ASK` with `is_misparsing=True` → blocked with `"Security check: {message}"`
4. If `ASK` without misparsing (i.e., deferred non-misparsing) → in Python, this is NOT automatically blocked; the caller decides. In blueCode (no interactive ASK), map this to `Deny` as well.
5. Check `get_destructive_command_warning(command)` → if non-None, block unless `allow_destructive=True`
6. Otherwise → `Ok ()`

**blueCode simplification:** Since blueCode is non-interactive, ALL ASK results become `Deny`. There is no "ask the user" path.

---

## F# Port Strategy for BashSecurity.fs

### Module Structure

```fsharp
module BlueCode.Cli.Adapters.BashSecurity

open System.Text.RegularExpressions

// ── Constants (compiled at module init) ──────────────────────────────────────

let private controlCharRe = Regex(@"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]", RegexOptions.Compiled)
let private unicodeWsRe   = Regex(@"[   -     　﻿]", RegexOptions.Compiled)
let private commandSubstitutionPatterns: (Regex * string) list = [ ... ]
let private destructivePatterns: (Regex * string) list = [ ... ]
let private zshDangerousCommands = Set.ofList [ "zmodload"; "emulate"; ... ]
let private zshPrecommandModifiers = Set.ofList [ "command"; "builtin"; "noglob"; "nocorrect" ]
let private shellOperators = Set.ofList [ ';'; '|'; '&'; '<'; '>' ]
let private findDangerousFlags = Set.ofList [ "-exec"; "-execdir"; "-ok"; "-okdir"; "-delete" ]

// ── Security decision type ─────────────────────────────────────────────────

type SecurityDecision =
    | Allow
    | Deny of reason: string
    | Passthrough

// ── ValidationCtx record ──────────────────────────────────────────────────

type private ValidationCtx = {
    OriginalCommand       : string
    BaseCommand           : string
    UnquotedContent       : string   // outside single quotes
    FullyUnquotedContent  : string   // outside all quotes, safe redirections stripped
    FullyUnquotedPreStrip : string   // outside all quotes, before stripping
    UnquotedKeepQuoteChars: string   // outside all quotes, quote chars preserved
}

// ── Helper functions ──────────────────────────────────────────────────────

let private extractQuotedContent (command: string) : string * string * string = ...
let private stripSafeRedirections (content: string) : string = ...
let private hasUnescapedChar (content: string) (ch: char) : bool = ...
let private isEscapedAtPosition (content: string) (pos: int) : bool = ...
let private hasBackslashEscapedWhitespace (command: string) : bool = ...
let private hasBackslashEscapedOperator (command: string) : bool = ...

// ── Validators (21 functions) ─────────────────────────────────────────────

let private validateControlCharacters (ctx: ValidationCtx) : SecurityDecision = ...
// ... (one function per validator, see chain order above)

// ── Public API ────────────────────────────────────────────────────────────

/// Validate a shell command. Returns Ok () if safe, Error reason if blocked.
/// All ASK results from Python are treated as Deny in blueCode (non-interactive).
let validateCommand (command: string) : Result<unit, string> = ...
```

### Regex Compilation Strategy

- Compile ALL Regex objects at module initialization using `RegexOptions.Compiled`
- Do NOT use `Regex.IsMatch(input, pattern)` static method in hot paths (creates new Regex each call)
- Exception: single-use patterns inside functions can use `Regex(pattern)` without `Compiled` if called rarely

**Performance:** The Python source has ~25 regex compilations across all constants. With `RegexOptions.Compiled`, .NET compiles them to IL at startup (~few ms). No N^2 patterns found — all regex checks are O(N) in command length.

### SecurityDecision Shape

The F# port does not need `is_misparsing: bool`. All ASK (misparsing or not) becomes `Deny`. The message string can optionally include `[misparsing]` prefix for debugging but it does not affect behavior.

### Chain Implementation Pattern

```fsharp
let private runValidatorChain (validators: (ValidationCtx -> SecurityDecision) list) (ctx: ValidationCtx) : SecurityDecision =
    let mutable deferred: SecurityDecision option = None
    let mutable result: SecurityDecision option = None
    let mutable i = 0
    let arr = List.toArray validators
    while result.IsNone && i < arr.Length do
        match arr.[i] ctx with
        | Deny reason ->
            // Check if this is a non-misparsing validator (newlines, redirections)
            // by position index or name; if so, defer. Otherwise return immediately.
            result <- Some (Deny reason)
        | Allow -> result <- Some Allow
        | Passthrough -> ()
        i <- i + 1
    match result with
    | Some r -> r
    | None ->
        match deferred with
        | Some d -> d
        | None -> Passthrough
```

Simpler approach: implement with explicit deferred slot, not generic position checking. Pass a flag `isDeferred` per validator entry:

```fsharp
type private ValidatorEntry = {
    Fn        : ValidationCtx -> SecurityDecision
    IsDeferred: bool
}
```

---

## Architecture Patterns

### Recommended File Structure (Phase 3 additions)

```
src/BlueCode.Cli/
├── Adapters/
│   ├── LlmWire.fs           # existing
│   ├── Json.fs              # existing
│   ├── QwenHttpClient.fs    # existing
│   ├── BashSecurity.fs      # NEW (pure, no IO)
│   └── FsToolExecutor.fs    # NEW (IO heavy, uses BashSecurity)
└── Program.fs               # existing
```

### Compile Order in BlueCode.Cli.fsproj

Final `.fsproj` after Phase 3:

```xml
<ItemGroup>
  <Compile Include="Adapters/LlmWire.fs" />
  <Compile Include="Adapters/Json.fs" />
  <Compile Include="Adapters/QwenHttpClient.fs" />
  <Compile Include="Adapters/BashSecurity.fs" />
  <Compile Include="Adapters/FsToolExecutor.fs" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

`BashSecurity.fs` before `FsToolExecutor.fs` because `FsToolExecutor` imports `BashSecurity.validateCommand` for the `RunShell` case. Both before `Program.fs`.

### Pattern 1: IToolExecutor Implementation (Object Expression)

```fsharp
// FsToolExecutor.fs
module BlueCode.Cli.Adapters.FsToolExecutor

open System.IO
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Cli.Adapters.BashSecurity

let create (projectRoot: string) : IToolExecutor =
    { new IToolExecutor with
        member _.ExecuteAsync (tool: Tool) (ct: CancellationToken) =
            task {
                match tool with
                | ReadFile  (FilePath path)               -> return! readFileImpl  projectRoot path ct
                | WriteFile (FilePath path, content)      -> return! writeFileImpl projectRoot path content ct
                | ListDir   (FilePath path)               -> return! listDirImpl   projectRoot path ct
                | RunShell  (Command cmd, Timeout secs)   -> return! runShellImpl  projectRoot cmd secs ct
            }
    }
```

### Pattern 2: Path Validation (Write and Read)

```fsharp
let private validatePath (projectRoot: string) (inputPath: string) : Result<string, ToolResult> =
    let combined = Path.Combine(projectRoot, inputPath)
    let resolved = Path.GetFullPath(combined)
    if resolved.StartsWith(projectRoot, StringComparison.Ordinal) then
        Ok resolved
    else
        Error (PathEscapeBlocked inputPath)
```

**Edge cases:**
- `~` in paths: DO NOT expand. Reject paths starting with `~` (they resolve differently per OS). `Path.GetFullPath("~/foo")` on .NET treats `~` as literal char, not home directory — this is correct behavior (string will NOT escape the root). But for clarity: if input starts with `~`, return `PathEscapeBlocked` with message `"Paths starting with ~ are not supported"`.
- `..` traversal: `Path.GetFullPath` resolves these correctly — `../etc/passwd` from a project root will produce a path that does NOT start with `projectRoot`, triggering `PathEscapeBlocked`.
- Absolute paths: If `inputPath` is absolute (starts with `/`), `Path.Combine(root, inputPath)` ignores `root` on .NET (unlike Python's `os.path.join`). Then `Path.GetFullPath` returns the absolute path as-is, which will fail the `StartsWith(projectRoot)` check. This is the correct behavior.
- macOS symlinks: `/private/var` vs `/var`. `Path.GetFullPath` does NOT resolve symlinks on .NET — it only normalizes separators and `..` sequences. So `/var/folders/...` and `/private/var/folders/...` are distinct strings. **Resolution:** capture `projectRoot` using `Path.GetFullPath(Directory.GetCurrentDirectory())` at startup AND resolve symlinks via `Path.GetFullPath` + check. For macOS safety, also check resolved path against `Path.GetFullPath(Path.GetRealPath(projectRoot))` if needed. **Simpler approach:** Use `Path.GetFullPath` throughout and accept that paths expressed differently due to macOS symlinks may fail. For a personal tool this is acceptable.

**Apply to BOTH read_file and write_file.** Reading `/etc/passwd` is lower risk but consistent protection is cleaner.

### Pattern 3: run_shell — Async Stdout/Stderr Read

```fsharp
let private runShellImpl (projectRoot: string) (cmd: string) (timeoutSecs: int) (ct: CancellationToken) : Task<Result<ToolResult, AgentError>> =
    task {
        // 1. Security validation (synchronous, before any process launch)
        match validateCommand cmd with
        | Error reason -> return Ok (SecurityDenied reason)
        | Ok () ->
            // 2. Launch process
            let psi = ProcessStartInfo("/bin/bash", $"-c {cmd}")
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError  <- true
            psi.UseShellExecute        <- false
            psi.WorkingDirectory       <- projectRoot
            use proc = Process.Start(psi)

            // 3. Linked CTS for combined timeout + caller cancellation
            use cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
            cts.CancelAfter(TimeSpan.FromSeconds(float timeoutSecs))

            try
                // 4. Concurrent stdout + stderr read (prevents deadlock)
                // Read both to end BEFORE WaitForExitAsync (dotnet/runtime #98347)
                let! stdout = proc.StandardOutput.ReadToEndAsync(cts.Token)
                let! stderr = proc.StandardError.ReadToEndAsync(cts.Token)
                do! proc.WaitForExitAsync(cts.Token)

                // 5. Cap stdout at 100KB, stderr at 10KB
                let stdoutCapped = capOutput stdout 102_400 "stdout"
                let stderrCapped = capOutput stderr 10_240 "stderr"

                if proc.ExitCode = 0 then
                    return Ok (Success stdoutCapped)
                else
                    return Ok (Failure (proc.ExitCode, stderrCapped))
            with
            | :? OperationCanceledException when not ct.IsCancellationRequested ->
                // Timeout fired (inner CTS), not user cancel
                proc.Kill(entireProcessTree = true)
                return Ok (Timeout timeoutSecs)
            | :? OperationCanceledException ->
                // User cancelled (outer ct)
                proc.Kill(entireProcessTree = true)
                return Error UserCancelled
            | ex ->
                return Error (ToolFailure (RunShell (Command cmd, Timeout timeoutSecs), ex))
    }
```

**Shell choice:** Use `/bin/bash -c` (not `/bin/sh`). The security validators target bash semantics — brace expansion, `$(...)`, process substitution are bash features. Bash is always available on macOS at `/bin/bash`. If bash is absent, fall through to `sh` with a warning log.

**Deadlock prevention:** `ReadToEndAsync` for both stdout AND stderr must run concurrently, then `WaitForExitAsync`. The pattern above is sequential (stdout then stderr) which risks deadlock if the process fills the stderr pipe buffer before stdout finishes. Use `and!` (F# 10) or `Task.WhenAll` for true concurrency:

```fsharp
// Preferred: concurrent read using F# 10 and!
let! stdout, stderr =
    Task.WhenAll(
        proc.StandardOutput.ReadToEndAsync(cts.Token),
        proc.StandardError.ReadToEndAsync(cts.Token)
    ) |> fun t -> task {
        let! results = t
        return results.[0], results.[1]
    }
```

Or with F# 10 `and!`:
```fsharp
let! stdout = proc.StandardOutput.ReadToEndAsync(cts.Token)
and! stderr = proc.StandardError.ReadToEndAsync(cts.Token)
```

### Pattern 4: Output Truncation (TOOL-06)

The 2000-char truncation applies to ALL four tools before the result enters message history. Two design options:

**Option A (Recommended): Truncate INSIDE FsToolExecutor**
Each tool helper truncates its output string to 2000 chars before wrapping in `Success`. The `ToolResult.Success` case carries a plain `output: string` (already defined in Domain.fs). The truncation is invisible to callers — they see a 2000-char (max) string.

To preserve context: append a truncation marker to the string itself: `"\n\n[truncated: showing first 2000 of N chars]"`.

```fsharp
let private truncateOutput (maxLen: int) (raw: string) : string =
    if raw.Length <= maxLen then raw
    else
        let truncated = raw.Substring(0, maxLen)
        sprintf "%s\n\n[truncated: showing first %d of %d chars]" truncated maxLen raw.Length
```

Applied at 2000 chars for the Phase 4 message-history concern (TOOL-06), and separately for stdout at 100KB raw before any truncation marker is added (TOOL-04 resource limit).

**Option B: Truncate in Phase 4 agent loop**
Not recommended — Phase 3 must be self-contained per requirements. TOOL-06 says truncation happens "before being appended to message history" — FsToolExecutor can enforce this by truncating before returning.

**ToolResult.Success field check:** Current `Domain.fs` definition:
```fsharp
type ToolResult =
    | Success           of output: string
    | Failure           of exitCode: int * stderr: string
    | SecurityDenied    of reason: string
    | PathEscapeBlocked of attempted: string
    | Timeout           of seconds: int
```

The `output: string` in `Success` is a single string. `Failure` carries `stderr: string`. No `truncated: bool` or `originalLength: int` fields exist — truncation marker embedded in the string is the correct approach given the existing shape. **Do NOT modify Domain.fs** — embed the marker in the string content.

### Pattern 5: read_file Line Range

Current `Domain.fs` `Tool` definition:
```fsharp
type Tool =
    | ReadFile  of FilePath
    | WriteFile of FilePath * content: string
    | ListDir   of FilePath
    | RunShell  of Command * Timeout
```

`ReadFile` takes only `FilePath` — **no line range parameter in the current Domain.fs**. This is a gap vs. TOOL-01 requirement. The requirement says "with optional line range returns only those lines." Two options:

1. Amend `Domain.fs` to `ReadFile of FilePath * lineRange: (int * int) option`
2. Encode line range in the file path string (hack — don't do this)

**Recommendation:** Amend `Domain.fs` as an additive change. This is analogous to how Plan 02-01 amended `Ports.fs`. Planner should make this a distinct task (03-00 or part of 03-01) with the exact change.

**Line range semantics:** 1-indexed, inclusive both ends. `(1, 10)` = lines 1 through 10.

**Implementation:** Use `File.ReadLinesAsync` (lazy, returns `IAsyncEnumerable<string>`). For large files, enumerate only up to the end line:
```fsharp
// For line range: read lazily, skip/take
let lines = File.ReadLines(resolvedPath) // IEnumerable<string>
let selected =
    match lineRange with
    | None -> lines |> Seq.toArray
    | Some (startLine, endLine) ->
        lines
        |> Seq.skip (startLine - 1)
        |> Seq.truncate (endLine - startLine + 1)
        |> Seq.toArray
```

Note: `File.ReadLines` (not async) is fine for this use case — the file is read synchronously under a `Task.Run` wrapper or directly in a `task {}` block. `File.ReadLinesAsync` in .NET 10 returns `IAsyncEnumerable<string>` which requires `await foreach` — more complex. Use synchronous `File.ReadLines` inside the `task {}` block without additional wrapping.

**Encoding:** Assume UTF-8. Do not detect BOM explicitly — `File.ReadLines` uses `Encoding.Default` (UTF-8 on modern macOS/Linux). Binary files will produce garbled output; this is acceptable (not a v1 concern).

### Pattern 6: list_dir Depth

Current `Domain.fs` `ListDir of FilePath` — no depth parameter. Similar gap vs. TOOL-03.

**Recommendation:** Amend `Domain.fs` to `ListDir of FilePath * depth: int option`.

**Default depth:** 1 (current directory entries only, no recursion). **Cap:** 5 levels maximum (prevent explosion on large projects).

**Hidden files:** Exclude by default. Filter out entries whose filename starts with `.`. On macOS, also filter `.DS_Store` (already caught by dot-prefix rule).

**Symlinks:** List as regular entries (show their name, not their target). Do not follow.

**Output format:** Flat list with relative paths from the requested directory. One entry per line, directories suffixed with `/`:
```
src/
src/Main.fs
src/Types.fs
tests/
tests/Tests.fs
```

**Implementation:** Use `Directory.EnumerateFileSystemEntries` for the current level, recurse manually up to `depth` levels:
```fsharp
let rec private enumDir (path: string) (depth: int) (maxDepth: int) : string seq = seq {
    if depth > maxDepth then ()
    else
        let entries = Directory.EnumerateFileSystemEntries(path)
        for entry in entries do
            let name = Path.GetFileName(entry)
            if not (name.StartsWith(".")) then
                if Directory.Exists(entry) then
                    yield name + "/"
                    if depth < maxDepth then
                        yield! enumDir entry (depth + 1) maxDepth |> Seq.map (fun e -> name + "/" + e)
                else
                    yield name
}
```

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Shell security validator | Custom regex-based allowlist | Port `bash_security.py` validator chain | 18 validators address real bypass attacks; the fence exists for real reasons |
| Path normalization | String `Contains("..")` check | `Path.GetFullPath` + `StartsWith(projectRoot)` | String check misses `%2e%2e`, unicode dots, `//` etc. GetFullPath resolves all of them |
| Process timeout | `Thread.Sleep` + `Process.Kill` | `CancellationTokenSource.CancelAfter` + `WaitForExitAsync(ct)` | Native cancellation integrates with `task {}` CT chain; Sleep-based approach doesn't cancel reads |
| Stdout deadlock prevention | Sequential read then wait | Concurrent `Task.WhenAll` on stdout + stderr read | Sequential read deadlocks when process fills the OTHER pipe buffer |
| Recursive directory listing | Manual DFS loop | `Directory.EnumerateFileSystemEntries` + recursive seq | Built-in handles permissions errors gracefully; custom loop doesn't |

**Key insight:** The security validator is the most complex "don't hand-roll" item. The 18 validators in bash_security.py are not paranoid boilerplate — each one addresses a documented real-world bypass. Port the logic, not just the surface API.

---

## Common Pitfalls

### Pitfall 1: Stdout/Stderr Deadlock
**What goes wrong:** Process writes to stderr buffer (10KB cap), fills it while agent reads stdout to end. Process blocks writing stderr; agent blocks reading stdout. Deadlock.
**Why it happens:** Pipes have finite kernel buffers. Sequential read of one pipe before the other allows buffer fill.
**How to avoid:** Use `Task.WhenAll` or F# 10 `and!` to read stdout and stderr concurrently, then call `WaitForExitAsync`.
**Warning signs:** `run_shell` hanging on commands that produce stderr output.

### Pitfall 2: Symlink Escape on macOS
**What goes wrong:** Project root is `/Users/ohama/projs/blueCode`. Path resolves to `/private/Users/ohama/projs/blueCode` via macOS symlink. `StartsWith` check fails. All writes are blocked as path escapes.
**Why it happens:** `/Users` is a symlink to `/private/Users` on macOS. `Path.GetFullPath` does NOT resolve symlinks.
**How to avoid:** Capture `projectRoot` at startup with `Path.GetFullPath(Directory.GetCurrentDirectory())`. Apply the same `Path.GetFullPath` normalization to input paths before the `StartsWith` check. Both sides of the comparison use `Path.GetFullPath` only (no symlink resolution), so they are consistently un-resolved. As long as the user runs the CLI from within the project (not via a symlink path), the check works.
**Warning signs:** `write_file` returning `PathEscapeBlocked` for valid project paths on macOS.

### Pitfall 3: Process Kill Scope
**What goes wrong:** `proc.Kill()` kills only the shell process, not child processes launched by the shell command. A `sleep 35 &` or a subprocess keeps running after timeout.
**How to avoid:** Use `proc.Kill(entireProcessTree: true)` — available since .NET 5, confirmed in .NET 10. This terminates the entire process group.
**Warning signs:** Zombie processes after timeout test (`sleep 35`); `run_shell` returns `Timeout` but the sleep process still appears in `ps`.

### Pitfall 4: Bash Security Port Regression
**What goes wrong:** A validator is partially ported, missing edge cases. For example, `validate_obfuscated_flags` has 8 separate sub-checks — missing one sub-check silently allows an obfuscated flag bypass.
**Why it happens:** The Python function is complex with many early returns. Each sub-check is a separate regex or character-scan.
**How to avoid:** Write one test case per sub-check for each validator. The test suite for `BashSecurity.fs` should have ~40-50 cases minimum — one "blocked" and one "allowed" per sub-check.
**Warning signs:** Security test suite with fewer than 30 cases; any validator function in F# that is shorter than its Python equivalent by more than 50%.

### Pitfall 5: Line Range 0-Indexed vs 1-Indexed
**What goes wrong:** LLM sends `{"startLine": 1, "endLine": 5}` meaning lines 1-5. If the F# implementation treats these as 0-indexed, line 1 skips line 1 and reads lines 2-6.
**Why it happens:** `Seq.skip` uses 0-based skip count; line numbers are typically 1-indexed in user communication.
**How to avoid:** Document clearly in the tool description (in the system prompt) and in the F# implementation comments: line ranges are 1-indexed inclusive. Apply `skip (startLine - 1)` to convert 1-indexed to 0-indexed skip.

### Pitfall 6: `Path.Combine` Ignores Root on Absolute Input
**What goes wrong:** Input path is `/etc/passwd`. `Path.Combine(projectRoot, "/etc/passwd")` returns `/etc/passwd` (ignoring projectRoot). The `StartsWith(projectRoot)` check then correctly catches this — but only if the developer remembers that `Combine` does NOT prepend for absolute paths. If they use string concatenation instead: `projectRoot + "/" + inputPath` they get `"/path/to/project//etc/passwd"` which `GetFullPath` resolves to `/etc/passwd` — still caught. Both approaches work but the developer must test this.
**How to avoid:** Test `write_file` with an absolute path outside project root in the test suite. Both approaches (Combine + GetFullPath) handle this correctly.

### Pitfall 7: Domain.fs Amendment Needed Before FsToolExecutor
**What goes wrong:** `FsToolExecutor.fs` tries to pattern match `ReadFile (FilePath path, lineRange)` but `Domain.fs` only defines `ReadFile of FilePath`. Compile error. Planner must sequence the Domain.fs amendment (additive change) before the FsToolExecutor implementation task.
**How to avoid:** Task 03-01 must start with "amend Domain.fs to add `lineRange` to ReadFile and `depth` to ListDir" before implementing FsToolExecutor.

---

## Code Examples

### SecurityDecision Chain Skeleton

```fsharp
// BashSecurity.fs — chain runner with deferred non-misparsing results
let private nonDeferredValidators = [
    validateControlCharacters   // runs pre-context; handled separately
    validateJqCommand
    validateObfuscatedFlags
    validateShellMetacharacters
    validateDangerousVariables
    validateCommentQuoteDesync
    validateQuotedNewline
    validateCarriageReturn
    // validate_newlines — DEFERRED
    validateIfsInjection
    validateProcEnvironAccess
    validateDangerousPatterns
    // validate_redirections — DEFERRED
    validateBackslashEscapedWhitespace
    validateBackslashEscapedOperators
    validateUnicodeWhitespace
    validateMidWordHash
    validateBraceExpansion
    validateZshDangerousCommands
]

let private deferredValidators = [
    validateNewlines
    validateRedirections
]

let validateCommand (command: string) : Result<unit, string> =
    // Pre-context control char check
    let preCtx = { OriginalCommand = command; BaseCommand = ""; UnquotedContent = ""; FullyUnquotedContent = ""; FullyUnquotedPreStrip = ""; UnquotedKeepQuoteChars = "" }
    match validateControlCharacters preCtx with
    | Deny reason -> Error reason
    | _ ->
        // Build context
        let base_cmd = if command.Trim() = "" then "" else command.TrimStart().Split(' ').[0]
        let withDq, fullyUnquoted, keepQuoteChars = extractQuotedContent command
        let ctx = {
            OriginalCommand        = command
            BaseCommand            = base_cmd
            UnquotedContent        = withDq
            FullyUnquotedContent   = stripSafeRedirections fullyUnquoted
            FullyUnquotedPreStrip  = fullyUnquoted
            UnquotedKeepQuoteChars = keepQuoteChars
        }
        // Early validators
        let earlyValidators = [ validateEmpty; validateIncompleteCommands; validateGitCommit ]
        let earlyResult =
            earlyValidators |> List.tryPick (fun v ->
                match v ctx with
                | Allow     -> Some (Ok ())     // allowed early
                | Deny r    -> Some (Error r)   // blocked early
                | Passthrough -> None           // continue
            )
        match earlyResult with
        | Some r -> r
        | None ->
            // Main chain: non-deferred first
            let mainResult =
                nonDeferredValidators |> List.tryPick (fun v ->
                    match v ctx with
                    | Deny r  -> Some (Error r)
                    | Allow   -> Some (Ok ())
                    | Passthrough -> None
                )
            match mainResult with
            | Some r -> r
            | None ->
                // Deferred validators
                let deferredResult =
                    deferredValidators |> List.tryPick (fun v ->
                        match v ctx with
                        | Deny r  -> Some (Error r)
                        | _       -> None
                    )
                match deferredResult with
                | Some r -> r
                | None   ->
                    // Also check destructive patterns
                    checkDestructivePatterns command
```

### Concurrent Stdout/Stderr Read

```fsharp
// dotnet/runtime #98347: read both streams concurrently before WaitForExit
let stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token)
let stderrTask = proc.StandardError.ReadToEndAsync(cts.Token)
let! _ = Task.WhenAll(stdoutTask, stderrTask)
do! proc.WaitForExitAsync(cts.Token)
let stdout = stdoutTask.Result
let stderr  = stderrTask.Result
```

Or using F# 10 `and!` (cleaner):
```fsharp
let! stdout = proc.StandardOutput.ReadToEndAsync(cts.Token)
and! stderr = proc.StandardError.ReadToEndAsync(cts.Token)
do! proc.WaitForExitAsync(cts.Token)
```

### Path Validation

```fsharp
// Source: PITFALLS.md D-3 + Verified against .NET 10 Path API
let private validatePath (projectRoot: string) (inputPath: string) : Result<string, ToolResult> =
    if inputPath.StartsWith("~") then
        Error (PathEscapeBlocked inputPath)
    else
        let combined  = Path.Combine(projectRoot, inputPath)
        let resolved  = Path.GetFullPath(combined)
        // Ensure trailing separator on root to avoid prefix-match false positives
        let root = if projectRoot.EndsWith(Path.DirectorySeparatorChar) then projectRoot
                   else projectRoot + string Path.DirectorySeparatorChar
        if resolved.StartsWith(root, StringComparison.Ordinal) || resolved = projectRoot then
            Ok resolved
        else
            Error (PathEscapeBlocked inputPath)
```

**Important:** Add trailing separator to `projectRoot` before `StartsWith` check. Otherwise `/path/to/project-evil` would pass the check against `/path/to/project`.

### Truncation with Marker

```fsharp
let private truncateOutput (raw: string) : string =
    let maxLen = 2000
    if raw.Length <= maxLen then raw
    else
        let portion = raw.Substring(0, maxLen)
        sprintf "%s\n\n[truncated: showing first %d of %d chars]" portion maxLen raw.Length
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `Process.WaitForExit(timeoutMs)` (blocking) | `WaitForExitAsync(ct)` with `CancellationTokenSource.CancelAfter` | .NET 5+ | Non-blocking, integrates with task {} CT chain |
| `Process.Kill()` (root only) | `Process.Kill(entireProcessTree: true)` | .NET 5+ | Kills all child processes, not just shell |
| `File.ReadAllText` (whole file) | `File.ReadLines` (lazy enumerable) | .NET 2.0+ | `ReadLines` is lazy — doesn't allocate full file in memory |
| Manual SSE parser | `System.Net.ServerSentEvents.SseParser` | .NET 9 | Inbox since .NET 9; not needed for Phase 3 (tool executor) |
| `ReadToEndAsync` sequential | Concurrent `Task.WhenAll` or `and!` | F# 10 / .NET 10 | Prevents deadlock when both stdout and stderr are large |

**Deprecated/outdated:**
- `Process.WaitForExit(int timeout)` synchronous overload: avoid in async code; use `WaitForExitAsync(CancellationToken)` instead
- `StandardOutput.ReadToEnd()` synchronous: avoid inside `task {}`; use `ReadToEndAsync`

---

## Open Questions

1. **Domain.fs amendment scope for line range and depth**
   - What we know: Current `ReadFile of FilePath` and `ListDir of FilePath` have no parameters for line range or depth. TOOL-01 and TOOL-03 require them.
   - What's unclear: Whether line range and depth should be optional parameters in the DU case, or passed through a separate configuration type.
   - Recommendation: Amend Domain.fs with `ReadFile of FilePath * lineRange: (int * int) option` and `ListDir of FilePath * depth: int option`. These are additive changes (existing single-parameter callers break only at pattern-match sites, not conceptually).

2. **Shell choice: `/bin/bash` vs `/bin/sh`**
   - What we know: bash_security.py targets bash semantics. `/bin/bash` is present on macOS. The security validators check bash-specific features (brace expansion, process substitution, etc.).
   - What's unclear: If bash is absent (minimal Linux container), do we fall back or fail?
   - Recommendation: Use `/bin/bash -c`. If bash is not found, `Process.Start` throws — catch as `ToolFailure`. Document this in FsToolExecutor comments.

3. **Truncation for Failure case stderr**
   - What we know: TOOL-06 says "any tool whose output exceeds 2000 characters" — this implies `Failure` stderr should also be truncated.
   - What's unclear: `ToolResult.Failure of exitCode: int * stderr: string` — stderr at 10KB raw cap for run_shell, then 2000 chars for message history.
   - Recommendation: Apply both caps: 10KB raw cap on capture, then 2000-char truncation before wrapping in `ToolResult`. For non-shell tools that fail (read_file on missing file), stderr is the error message from IOException — typically short, under 2000 chars.

4. **`File.ReadLinesAsync` vs `File.ReadLines` in .NET 10**
   - What we know: `File.ReadLines` returns `IEnumerable<string>` (synchronous). `File.ReadLinesAsync` in .NET 10 returns `IAsyncEnumerable<string>`.
   - What's unclear: Whether `ReadLinesAsync` is worth the complexity for line-range reads.
   - Recommendation: Use synchronous `File.ReadLines` inside `task {}` for simplicity. For multi-GB files, this blocks the thread pool thread; acceptable for a personal tool. If needed later, migrate to `ReadLinesAsync` with `await foreach`.

---

## Test Strategy for Phase 3

### BashSecurity.fs Tests (~45 cases)

**Control characters:** blocked chars in hex range; tab/newline/CR allowed
**Command substitution:** `echo $(whoami)` → blocked; `echo hello` → passed
**Process substitution:** `diff <(ls) <(ls)` → blocked
**IFS injection:** `IFS=/ cmd` with `$IFS` → blocked
**Backtick:** `` cmd `echo foo` `` → blocked
**Obfuscated flags:** `rm $'\x2d'rf` → blocked; `rm -rf /tmp/safe` → may pass (depends on destructive check)
**Git commit:** `git commit -m 'safe message'` → passed; `git commit -m "msg $(id)"` → blocked
**Redirections:** `ls > /tmp/out` → blocked (deferred); `ls 2>/dev/null` → passed (stripped)
**Newlines:** `ls\nrm -rf /` → blocked; continuation `ls \\\nfoo` → passed
**Brace expansion:** `echo {a,b}` → blocked; `echo {}` → passed
**Destructive:** `rm -rf /` → blocked; `rm -rf /tmp/safe_dir` → blocked (rm -rf pattern)
**Unicode whitespace:** command with NBSP → blocked
**Zsh dangerous:** `zmodload foo` → blocked
**Safe commands:** `ls -la`, `grep pattern file`, `cat README.md`, `git status` → all passed
**Early allow:** empty string → passed; `git commit -m 'short message'` → passed

### Path Validation Tests (~10 cases)

- Relative path inside root → `Ok resolvedPath`
- Absolute path inside root → `Ok resolvedPath`
- `../` traversal → `Error (PathEscapeBlocked ...)`
- Absolute path outside root → `Error (PathEscapeBlocked ...)`
- Path starting with `~` → `Error (PathEscapeBlocked ...)`
- Path equal to root → `Ok rootPath` (edge case)
- Path that is a sibling of root (prefix attack) → `Error (PathEscapeBlocked ...)`
- Deep nested valid path → `Ok resolvedPath`

### FsToolExecutor Integration Tests

- `read_file` valid file → `Ok (Success content)`
- `read_file` missing file → `Ok (Failure (_, stderr))`
- `read_file` line range (1, 3) on 5-line file → 3 lines returned
- `write_file` valid path → file created, `Ok (Success "")`
- `write_file` path escape → `Ok (PathEscapeBlocked attempted)`
- `list_dir` root → flat list with no hidden files
- `list_dir` depth 2 → recursive up to 2 levels
- `run_shell "echo hello"` → `Ok (Success "hello\n")`
- `run_shell "rm -rf /"` → `Ok (SecurityDenied reason)`
- `run_shell "sleep 35"` → `Ok (Timeout 30)` (returned in under 31s)
- `run_shell "yes | head -50000"` → `Ok (Success truncated)` (output capped)
- `run_shell "echo $(whoami)"` → `Ok (SecurityDenied reason)` (command substitution)

---

## Sources

### Primary (HIGH confidence)

- `/Users/ohama/projs/claw-code-agent/src/bash_security.py` — read in full; all 1261 lines; inventoried all validators, constants, helpers
- `/Users/ohama/projs/blueCode/src/BlueCode.Core/Domain.fs` — read in full; exact ToolResult and Tool DU shapes confirmed
- `/Users/ohama/projs/blueCode/src/BlueCode.Core/Ports.fs` — read in full; IToolExecutor signature confirmed
- `.planning/research/PITFALLS.md` — D-2, D-3, D-4, C-2 pitfalls confirmed and cited

### Secondary (HIGH confidence, prior research)

- `.planning/research/STACK.md` — confirms `Process.Kill(entireProcessTree: true)`, `WaitForExitAsync`, `and!` in `task {}`; dotnet/runtime #98347 deadlock warning
- `.planning/research/ARCHITECTURE.md` — confirms `FsToolExecutor.fs` in `Adapters/`, object expression pattern, compile order
- `.planning/phases/02-llm-client/02-RESEARCH.md` — adapter pattern, `task {}` CE confirmed
- `/Users/ohama/projs/blueCode/src/BlueCode.Cli/BlueCode.Cli.fsproj` — actual current compile order verified

### Tertiary (MEDIUM confidence)

- General .NET 10 API documentation (Path, File, Directory, Process) — standard APIs, no surprises expected

---

## Metadata

**Confidence breakdown:**
- bash_security.py inventory: HIGH — read source in full, all validators documented
- Standard stack (no new packages): HIGH — confirmed against existing fsproj
- Architecture (file placement, compile order): HIGH — confirmed against existing Adapters/
- IToolExecutor signature: HIGH — read Ports.fs directly
- ToolResult shape: HIGH — read Domain.fs directly
- run_shell process management: HIGH — patterns confirmed in STACK.md + dotnet/runtime #98347
- Path validation: HIGH — standard .NET Path API, macOS symlink caveat documented
- Truncation strategy: HIGH — Domain.fs `Success of output: string` shape confirms embed-in-string approach
- Domain.fs amendment need (line range, depth): HIGH — gap confirmed by reading Domain.fs
- Test strategy: MEDIUM — extrapolated from bash_security.py sub-checks

**Research date:** 2026-04-22
**Valid until:** 2026-05-22 (30 days — stable APIs, no fast-moving dependencies)
