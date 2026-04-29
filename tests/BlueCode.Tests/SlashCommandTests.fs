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
