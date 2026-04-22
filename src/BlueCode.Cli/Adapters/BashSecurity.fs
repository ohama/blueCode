module BlueCode.Cli.Adapters.BashSecurity

// ── Placeholder for Plan 03-03 ────────────────────────────────────────────────
//
// This file is scaffolded by Plan 03-01 so that FsToolExecutor.fs can
// `open BlueCode.Cli.Adapters.BashSecurity` and call validateCommand
// without waiting for the full validator port. Plan 03-03 replaces the
// body below with the full 21-function port of claw-code-agent/src/bash_security.py.
//
// While this placeholder is in place, ALL commands are "safe" — run_shell
// MUST remain a Failure stub (see FsToolExecutor.fs RunShell case) until
// Plan 03-03 fills the validator chain AND Plan 03-02 wires real process
// launch. A live run_shell call with a permissive validator would be a
// security hole, so the stub in FsToolExecutor is the compensating control.

/// Validate a shell command. Returns Ok () if safe, Error reason if blocked.
/// PLAN 03-01 STUB: always returns Ok. Replaced by Plan 03-03.
let validateCommand (command: string) : Result<unit, string> =
    // Suppress unused-parameter warning without changing the signature.
    let _ = command
    // TODO Plan 03-03: port the 21 validators from bash_security.py.
    Ok ()
