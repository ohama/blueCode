module BlueCode.Tests.RunShellTests

open System
open System.Diagnostics
open System.IO
open System.Threading
open Expecto
open BlueCode.Core.Domain
open BlueCode.Core.Ports
open BlueCode.Cli.Adapters.FsToolExecutor

// ── Fixture helpers ────────────────────────────────────────────────────────────

let private newFixture () : string =
    let dir =
        Path.Combine(Path.GetTempPath(), "bluecode-runshell-" + Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory(dir) |> ignore
    Path.GetFullPath(dir)

let private cleanup (dir: string) =
    try
        if Directory.Exists dir then
            Directory.Delete(dir, true)
    with _ ->
        ()

let private exec (executor: IToolExecutor) (tool: Tool) =
    (executor.ExecuteAsync tool CancellationToken.None).GetAwaiter().GetResult()

// Tool.RunShell carries Timeout in milliseconds — for Plan 03-02 the
// impl hardcodes 30s regardless of this value. Passing 30000 documents intent.
let private shellTool (cmd: string) =
    RunShell(Command cmd, BlueCode.Core.Domain.Timeout 30000)

// ── Happy path ────────────────────────────────────────────────────────────────

let happyPathTests =
    testList
        "run_shell happy path (TOOL-04)"
        [

          testCase "echo hello returns Success with 'hello'"
          <| fun () ->
              let root = newFixture ()

              try
                  let exe = create root
                  let result = exec exe (shellTool "echo hello")

                  match result with
                  | Ok(Success out) -> Expect.stringContains out "hello" "stdout should contain 'hello'"
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root

          testCase "pwd returns projectRoot (working-dir lock)"
          <| fun () ->
              let root = newFixture ()

              try
                  let exe = create root
                  let result = exec exe (shellTool "pwd")

                  match result with
                  | Ok(Success out) ->
                      // On macOS, /tmp may resolve to /private/tmp; accept either.
                      let normalized = out.Trim()

                      Expect.isTrue
                          (normalized = root || normalized = "/private" + root || root.EndsWith(normalized))
                          (sprintf "pwd should be %s, got %s" root normalized)
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root ]

// ── Security denial (TOOL-05 integration) ─────────────────────────────────────

let securityTests =
    testList
        "run_shell security denial (TOOL-05 + Success Criterion 3)"
        [

          testCase "rm -rf / returns SecurityDenied and does NOT spawn"
          <| fun () ->
              let root = newFixture ()

              try
                  // Marker file that `rm -rf /` would destroy if it somehow ran.
                  let sentinel = Path.Combine(root, "sentinel.txt")
                  File.WriteAllText(sentinel, "SHOULD-REMAIN")
                  let exe = create root
                  let result = exec exe (shellTool "rm -rf /")

                  match result with
                  | Ok(SecurityDenied _) -> ()
                  | other -> failtestf "expected SecurityDenied, got %A" other
                  // Sentinel survives — proves process was NEVER spawned.
                  Expect.isTrue (File.Exists sentinel) "sentinel must survive — process never ran"
              finally
                  cleanup root

          testCase "command substitution $(whoami) blocked"
          <| fun () ->
              let root = newFixture ()

              try
                  let exe = create root
                  let result = exec exe (shellTool "echo $(whoami)")

                  match result with
                  | Ok(SecurityDenied _) -> ()
                  | other -> failtestf "expected SecurityDenied, got %A" other
              finally
                  cleanup root ]

// ── Timeout (TOOL-04 + Success Criterion 4) ───────────────────────────────────

let timeoutTests =
    testList
        "run_shell timeout (TOOL-04 + Success Criterion 4)"
        [

          testCase "sleep 35 times out in <31s, returns Timeout"
          <| fun () ->
              let root = newFixture ()

              try
                  let exe = create root
                  let sw = Stopwatch.StartNew()
                  let result = exec exe (shellTool "sleep 35")
                  sw.Stop()

                  match result with
                  | Ok(ToolResult.Timeout 30) -> ()
                  | other -> failtestf "expected Timeout 30, got %A (elapsed=%dms)" other sw.ElapsedMilliseconds

                  Expect.isLessThan sw.Elapsed.TotalSeconds 31.0 "should time out within 31s"
                  Expect.isGreaterThanOrEqual sw.Elapsed.TotalSeconds 29.0 "should take at least ~30s"
              finally
                  cleanup root ]

// ── Output truncation (TOOL-06 + Success Criterion 5) ─────────────────────────

let truncationTests =
    testList
        "run_shell output truncation (TOOL-06)"
        [

          testCase "yes | head -3000 returns Success with truncation marker"
          <| fun () ->
              let root = newFixture ()

              try
                  let exe = create root
                  // Each line of `yes` output is "y\n" (2 chars). 3000 lines = 6000 chars.
                  // After 100KB raw cap (no-op here, 6000 < 102400) and 2000-char TOOL-06,
                  // the result should be truncated at 2000 chars with marker.
                  let result = exec exe (shellTool "yes | head -3000")

                  match result with
                  | Ok(Success out) ->
                      Expect.isLessThan out.Length 2200 "should be near 2000 chars after truncation"
                      Expect.stringContains out "[truncated:" "must contain the TOOL-06 truncation marker"
                  | other -> failtestf "expected Success, got %A" other
              finally
                  cleanup root ]

// ── Non-zero exit ─────────────────────────────────────────────────────────────

let nonZeroExitTests =
    testList
        "run_shell non-zero exit -> Failure"
        [

          testCase "false (exit 1) returns Failure with code 1"
          <| fun () ->
              let root = newFixture ()

              try
                  let exe = create root
                  let result = exec exe (shellTool "false")

                  match result with
                  | Ok(Failure(code, _)) -> Expect.equal code 1 "false should exit with code 1"
                  | other -> failtestf "expected Failure, got %A" other
              finally
                  cleanup root

          testCase "exit 7 returns Failure with code 7"
          <| fun () ->
              let root = newFixture ()

              try
                  let exe = create root
                  let result = exec exe (shellTool "exit 7")

                  match result with
                  | Ok(Failure(code, _)) -> Expect.equal code 7 "exit 7 should surface exit code 7"
                  | other -> failtestf "expected Failure, got %A" other
              finally
                  cleanup root ]

// ── Aggregate ──────────────────────────────────────────────────────────────────

[<Tests>]
let runShellTests =
    testList
        "RunShell (Phase 3 Plan 03-02)"
        [ happyPathTests
          securityTests
          timeoutTests
          truncationTests
          nonZeroExitTests ]
