module DuPatternMatch

type Shape =
    | Circle of radius: float
    | Rectangle of width: float * height: float
    | Triangle of base': float * height: float * hypotenuse: float

/// Returns the area of the given shape.
/// Idiomatic implementation should use exhaustive match-over-DU.
let area (s: Shape) : float =
    failwith "TODO: implement area using match over Shape"

let shapes = [ Circle 1.0; Rectangle (2.0, 3.0); Triangle (4.0, 5.0, 6.4) ]
for s in shapes do
    let r = try area s with _ -> 0.0
    printfn "area = %f" r
