# Multi-file Refactor Task

The `Calculator` module exposes **two** functions that need to be renamed:

1. `add` → `sum` (the 2-argument addition function)
2. `add3` → `sum3` (the 3-argument addition function)

Both functions appear in three files: `Calculator.fs` (definitions), `Main.fs` (call sites), and `Tests.fs` (test functions and assertions).

## Task

Apply BOTH renames across all three F# files. Preserve all behavior, including tests.

### Rename 1: `add` → `sum`

- `Calculator.fs`: change `let add (x: int) (y: int) : int =` to `let sum (x: int) (y: int) : int =`
- `Calculator.fs`: inside `add3`, change `add (add x y) z` to `sum (sum x y) z`
- `Main.fs`: change `add 2 3` to `sum 2 3` and update the `printfn` label
- `Tests.fs`: change `add 2 3` to `sum 2 3`; rename `testAdd` to `testSum`; update `printfn` label to `testSum: PASS`

### Rename 2: `add3` → `sum3`

- `Calculator.fs`: change `let add3 (x: int) (y: int) (z: int) : int =` to `let sum3 (x: int) (y: int) (z: int) : int =`
- `Main.fs`: change `add3 1 2 3` to `sum3 1 2 3` and update the `printfn` label
- `Tests.fs`: change `add3 1 2 3` to `sum3 1 2 3`; rename `testAdd3` to `testSum3`; update `printfn` label to `testSum3: PASS`

## Completion checklist

After your refactor, ALL of the following must be true:

- [ ] `Calculator.fs` defines `sum` and `sum3`; no remaining `let add` or `let add3`
- [ ] `Main.fs` calls `Calculator.sum` and `Calculator.sum3`; no remaining `add` or `add3` references
- [ ] `Tests.fs` defines `testSum` and `testSum3`; calls `sum` / `sum3`; prints `testSum: PASS` / `testSum3: PASS`
- [ ] `grep -E "\\b(let |Calculator\\.)add\\b" Calculator.fs Main.fs Tests.fs` returns nothing
- [ ] `grep -E "\\b(let |Calculator\\.)add3\\b" Calculator.fs Main.fs Tests.fs` returns nothing

Both renames are required. Completing only one (e.g., only `add3` → `sum3`) leaves orphan references and is a FAIL.

## Files in this directory

- `Calculator.fs` — module to refactor (defines `add` and `add3`)
- `Main.fs` — entry point that calls `Calculator.add` and `Calculator.add3`
- `Tests.fs` — tests that verify `Calculator.add` and `Calculator.add3` behavior
- `README.md` — this task statement (do NOT modify)
