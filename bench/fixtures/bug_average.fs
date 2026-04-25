module Average

let average (xs: int list) : int =
    (List.sum xs) / (List.length xs)
