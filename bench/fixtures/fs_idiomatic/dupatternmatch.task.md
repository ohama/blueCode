# Task: dupatternmatch

File: `dupatternmatch.fs`

`Shape` is a discriminated union: `Circle of float`, `Rectangle of float * float`, `Triangle of float * float * float` (base, height, hypotenuse — only base and height are used).

Implement `area : Shape -> float`.

Idiomatic F# pattern required: **exhaustive `match` over the DU**, one branch per case, deconstructing all fields. Avoid `if`/`else if` chains.

Preserve the type signature. Replace `failwith "TODO"` with the match expression.
