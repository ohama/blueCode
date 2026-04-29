# Task: optionhandling

File: `optionhandling.fs`

Implement `safeDouble : string -> int` that:
1. Parses the string as an int (`System.Int32.TryParse`)
2. Returns 2× the parsed value on success, 0 on failure

Idiomatic F# pattern required: build an `int option` from the parse result, then chain **`Option.map`** (double) and **`Option.defaultValue 0`** (extract). Avoid `if`/`else` on the bool and `try`/`catch`.

Preserve the type signature. Replace `failwith "TODO"` with the Option chain.
