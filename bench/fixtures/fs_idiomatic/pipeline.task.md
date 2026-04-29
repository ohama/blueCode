# Task: pipeline

File: `pipeline.fs`

Implement `transform : int list -> int` to return the sum of the squares of the even numbers in the list, multiplied by 2.

Idiomatic F# pattern required: use a **pipeline (`|>`)** chaining `List.filter` (keep evens), `List.map` (square), `List.sum`, then `(*) 2`. Avoid imperative for-loops and mutable accumulators.

Preserve the type signature. Replace the `failwith "TODO"` body with the pipeline. Do not modify other functions.
