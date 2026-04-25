module DivideZero

/// Computes the integer mean of a list. Raises DivideByZeroException on empty input.
/// Bug trigger: call with an empty list (e.g., average []) — List.length [] returns 0,
/// causing integer division by zero at runtime.
let average (xs: int list) : int =
    List.sum xs / List.length xs
