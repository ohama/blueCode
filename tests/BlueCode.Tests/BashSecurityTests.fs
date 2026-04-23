module BlueCode.Tests.BashSecurityTests

open Expecto
open BlueCode.Cli.Adapters.BashSecurity

// ── Test helpers ──────────────────────────────────────────────────────────────

let private shouldBlock (desc: string) (cmd: string) =
    testCase (sprintf "BLOCK: %s" desc)
    <| fun () ->
        match validateCommand cmd with
        | Error _ -> ()
        | Ok() -> failtestf "expected BLOCK for '%s' but validateCommand returned Ok" desc

let private shouldAllow (desc: string) (cmd: string) =
    testCase (sprintf "ALLOW: %s" desc)
    <| fun () ->
        match validateCommand cmd with
        | Ok() -> ()
        | Error r -> failtestf "expected ALLOW for '%s' but got Error: %s" desc r

// ── Command substitution (BLOCK) ──────────────────────────────────────────────

let commandSubstitutionTests =
    testList
        "command substitution (BLOCK)"
        [ shouldBlock "$() substitution" "echo $(whoami)"
          shouldBlock "backtick substitution" "echo `whoami`"
          shouldBlock "process substitution <()" "diff <(ls) <(ls /)"
          shouldBlock "process substitution >()" "tee >(cat) < /tmp/x"
          shouldBlock "${} parameter substitution" "echo ${PATH}"
          shouldBlock "$[] arithmetic expansion" "echo $[1+1]"
          shouldBlock "zsh =() substitution" "cat =(ls)"
          shouldBlock "cmd subst in dangerous cmd" "echo $(rm -rf /tmp)" ]

// ── IFS injection (BLOCK) ─────────────────────────────────────────────────────

let ifsInjectionTests =
    testList
        "IFS injection (BLOCK)"
        [ shouldBlock "$IFS variable" "IFS=/ read a b c <<< /x/y/z; echo $a$IFS$b"
          shouldBlock "${IFS} in braces" "echo a${IFS}b" ]

// ── Dangerous redirections (BLOCK / ALLOW) ────────────────────────────────────

let dangerousRedirectionTests =
    testList
        "dangerous redirections"
        [ shouldBlock "output redirect to arbitrary file" "ls > /tmp/escape"
          shouldBlock "input redirect from sensitive file" "cat < /etc/passwd"
          shouldAllow "safe 2>&1" "ls 2>&1"
          shouldAllow "safe >/dev/null" "ls > /dev/null"
          shouldAllow "safe 2>/dev/null" "grep x file 2> /dev/null"
          shouldAllow "safe </dev/null" "cat < /dev/null" ]

// ── Newline injection (BLOCK / ALLOW) ─────────────────────────────────────────

let newlineInjectionTests =
    testList
        "newline separator"
        [ shouldBlock "literal newline between commands" "ls\nrm -rf /tmp/x"
          shouldBlock "literal CR between commands" "ls\r-la"
          shouldAllow "backslash-newline continuation" "ls \\\n-la" ]

// ── Control characters (BLOCK) ────────────────────────────────────────────────

let controlCharTests =
    testList
        "control characters (BLOCK)"
        [ shouldBlock "NUL byte (0x00)" "ls\x00-la"
          shouldBlock "bell char (0x07)" "ls\x07-la"
          shouldBlock "ESC char (0x1B)" "ls\x1b-la"
          shouldBlock "form feed (0x0C)" "ls\x0c-la"
          shouldBlock "backspace (0x08)" "ls\x08-la" ]

// ── Unicode whitespace (BLOCK) ────────────────────────────────────────────────
// Invisible Unicode hazard: embedding raw U+00A0 and U+FEFF in string
// literals renders them invisible in most editors. If the file is
// re-encoded, reformatted, or a tool strips BOM-like characters, these
// tests silently become regressions. Use explicit single-char codes
// so the intent is visible in source code.
//
// U+00A0 NON-BREAKING SPACE
// U+FEFF ZERO-WIDTH NO-BREAK SPACE (BOM)

let unicodeWhitespaceTests =
    let nbsp = char 0x00A0 |> string // U+00A0 NON-BREAKING SPACE
    let zwnbsp = char 0xFEFF |> string // U+FEFF ZERO-WIDTH NO-BREAK SPACE (BOM)

    testList
        "unicode whitespace (BLOCK)"
        [ shouldBlock "NBSP between tokens" ("ls" + nbsp + "-la")
          shouldBlock "zero-width no-break space" ("ls" + zwnbsp + "-la")
          shouldBlock "NBSP before flag" ("rm" + nbsp + "-rf /tmp/test") ]

// ── Destructive patterns (BLOCK) ──────────────────────────────────────────────

let destructiveTests =
    testList
        "destructive patterns (BLOCK)"
        [
          // File deletion
          shouldBlock "rm -rf /" "rm -rf /"
          shouldBlock "rm -rf /tmp/dir" "rm -rf /tmp/safe_dir"
          shouldBlock "rm -fr" "rm -fr /tmp/x"
          shouldBlock "rm -r" "rm -r /tmp/x"
          shouldBlock "rm -f" "rm -f /tmp/x"
          // Git - data loss
          shouldBlock "git reset --hard" "git reset --hard HEAD~5"
          shouldBlock "git push --force" "git push --force origin main"
          shouldBlock "git push -f" "git push -f origin main"
          shouldBlock "git clean -f" "git clean -fd"
          shouldBlock "git branch -D" "git branch -D feature/x"
          shouldBlock "git stash drop" "git stash drop"
          shouldBlock "git stash clear" "git stash clear"
          shouldBlock "git checkout --." "git checkout -- ."
          shouldBlock "git restore --." "git restore -- ."
          // Git - safety bypass
          shouldBlock "git commit --no-verify" "git commit --no-verify -m x"
          shouldBlock "git commit --amend" "git commit --amend -m x"
          // Infrastructure
          shouldBlock "kubectl delete" "kubectl delete pod foo"
          shouldBlock "terraform destroy" "terraform destroy -auto-approve"
          // SQL
          shouldBlock "SQL DROP TABLE" "psql -c 'DROP TABLE users'"
          shouldBlock "SQL DROP TABLE bare" "DROP TABLE users"
          shouldBlock "SQL DELETE FROM" "DELETE FROM users" ]

// ── Zsh dangerous commands (BLOCK) ───────────────────────────────────────────

let zshDangerousTests =
    testList
        "zsh dangerous commands (BLOCK)"
        [ shouldBlock "zmodload" "zmodload foo"
          shouldBlock "zsocket" "zsocket bar"
          shouldBlock "zf_rm" "zf_rm -rf /tmp/x"
          shouldBlock "zpty" "zpty proc cat"
          shouldBlock "emulate" "emulate bash"
          shouldBlock "fc -e (editor)" "fc -e vim"
          // precommand modifier + zsh dangerous command
          shouldBlock "noglob + zmodload" "noglob zmodload zsh/net/socket" ]

// ── Obfuscated flags (BLOCK) ──────────────────────────────────────────────────

let obfuscatedFlagsTests =
    testList
        "obfuscated flags (BLOCK)"
        [ shouldBlock "ANSI-C quoting $'...'" "rm $'\\x2drf' /tmp/x"
          shouldBlock "empty quote before dash ''-" "rm '' -rf /tmp/x"
          shouldBlock "locale quoting $\"...\"" "rm $\"-rf\" /tmp/x" ]

// ── Backslash-escaped operators (BLOCK) ──────────────────────────────────────

let backslashEscapeTests =
    testList
        "backslash-escaped operators (BLOCK)"
        [ shouldBlock "backslash semicolon \\;" "ls\\;rm -rf /tmp"
          shouldBlock "backslash pipe \\|" "ls\\|cat"
          shouldBlock "backslash ampersand \\&" "ls\\&rm -rf" ]

// ── Backslash-escaped whitespace (BLOCK) ─────────────────────────────────────

let backslashWhitespaceTests =
    testList
        "backslash-escaped whitespace (BLOCK)"
        [ shouldBlock "backslash-space" "rm\\ -rf /tmp"
          shouldBlock "backslash-tab" "rm\\\t-rf /tmp" ]

// ── /proc environ access (BLOCK) ─────────────────────────────────────────────

let procEnvironTests =
    testList
        "/proc environ access (BLOCK)"
        [ shouldBlock "cat /proc/self/environ" "cat /proc/self/environ"
          shouldBlock "head /proc/1/environ" "head /proc/1/environ"
          shouldBlock "/proc/12345/environ" "cat /proc/12345/environ" ]

// ── Brace expansion (BLOCK) ──────────────────────────────────────────────────

let braceExpansionTests =
    testList
        "brace expansion (BLOCK)"
        [ shouldBlock "comma brace {a,b,c}" "echo {a,b,c}"
          shouldBlock "range brace {1..10}" "echo {1..10}"
          shouldBlock "brace in path" "cat file{1,2,3}.txt" ]

// ── JQ dangerous flags (BLOCK) ───────────────────────────────────────────────

let jqTests =
    testList
        "jq dangerous patterns (BLOCK)"
        [ shouldBlock "jq system() function" "jq 'system(\"ls\")' file.json"
          shouldBlock "jq -f flag" "jq -f /tmp/script.jq file.json"
          shouldBlock "jq --from-file" "jq --from-file /tmp/script.jq"
          shouldBlock "jq --rawfile" "jq --rawfile data /tmp/data file.json" ]

// ── Zsh equals expansion (BLOCK) ─────────────────────────────────────────────

let zshEqualsTests =
    testList
        "zsh equals expansion (BLOCK)"
        [ shouldBlock "=cmd expansion" "=ls"
          shouldBlock "=cmd after space" "echo =ls" ]

// ── Shell metacharacters in quoted args (BLOCK) ───────────────────────────────

let shellMetacharTests =
    testList
        "shell metacharacters in quoted args (BLOCK)"
        [ shouldBlock "semicolon in double-quoted arg" "cmd \"arg;dangerous\""
          shouldBlock "ampersand in double-quoted arg" "cmd \"arg&dangerous\"" ]

// ── Safe commands (ALLOW) — false-positive regression checks ─────────────────

let allowedCommandTests =
    testList
        "safe commands (ALLOW)"
        [ shouldAllow "empty string" ""
          shouldAllow "whitespace only" "   "
          shouldAllow "ls -la" "ls -la"
          shouldAllow "pwd" "pwd"
          shouldAllow "whoami" "whoami"
          shouldAllow "date" "date"
          shouldAllow "echo hello" "echo hello"
          shouldAllow "cat README.md" "cat README.md"
          shouldAllow "grep pattern file.txt" "grep pattern file.txt"
          shouldAllow "git status" "git status"
          shouldAllow "git log --oneline" "git log --oneline"
          shouldAllow "git diff HEAD" "git diff HEAD"
          shouldAllow "wc -l file.txt" "wc -l file.txt"
          shouldAllow "head file.txt" "head file.txt"
          shouldAllow "tail -20 file.txt" "tail -20 file.txt"
          shouldAllow "find . -name '*.fs'" "find . -name '*.fs'"
          shouldAllow "dotnet build" "dotnet build"
          shouldAllow "dotnet test BlueCode.slnx" "dotnet test BlueCode.slnx"
          shouldAllow "echo with quotes" "echo 'hello world'"
          shouldAllow "git commit simple -m" "git commit -m 'fix bug'" ]

// ── Aggregate root ────────────────────────────────────────────────────────────

[<Tests>]
let bashSecurityTests =
    testList
        "BashSecurity (Phase 3 TOOL-05)"
        [ commandSubstitutionTests
          ifsInjectionTests
          dangerousRedirectionTests
          newlineInjectionTests
          controlCharTests
          unicodeWhitespaceTests
          destructiveTests
          zshDangerousTests
          obfuscatedFlagsTests
          backslashEscapeTests
          backslashWhitespaceTests
          procEnvironTests
          braceExpansionTests
          jqTests
          zshEqualsTests
          shellMetacharTests
          allowedCommandTests ]
