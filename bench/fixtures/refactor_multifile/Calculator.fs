module Calculator

/// Adds two integers and returns the result.
let add (x: int) (y: int) : int =
    x + y

/// Adds three integers and returns the result.
let add3 (x: int) (y: int) (z: int) : int =
    add (add x y) z
