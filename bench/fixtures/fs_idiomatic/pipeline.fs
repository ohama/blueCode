module Pipeline

/// Returns the sum of squares of even numbers in the list, multiplied by 2.
/// Idiomatic implementation should use a |> pipeline.
let transform (xs: int list) : int =
    failwith "TODO: implement transform using a pipeline of List.filter, List.map, List.sum"

let result = try transform [1; 2; 3; 4; 5; 6] with _ -> 0
printfn "transform [1;2;3;4;5;6] = %d (expected 112)" result
