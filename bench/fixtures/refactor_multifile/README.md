# Multi-file Refactor Task

The `Calculator` module exposes an `add` function and an `add3` function.
Other modules in this directory (`Main.fs`, `Tests.fs`) call them.

## Task

Rename `add` to `sum` everywhere it appears in this directory. Preserve all
behavior, including tests. After your refactor:

- `Calculator.fs` defines `sum` (and `add3` should now be `sum3`, calling `sum`).
- `Main.fs` calls `Calculator.sum` and `Calculator.sum3`.
- `Tests.fs` calls `sum` / `sum3` and prints `testSum: PASS` / `testSum3: PASS`.

No orphan references to `add` should remain in any of the three files.

## Files in this directory

- `Calculator.fs` — module to refactor
- `Main.fs` — entry point that calls Calculator
- `Tests.fs` — tests that verify Calculator behavior
- `README.md` — this task statement (do NOT modify)
