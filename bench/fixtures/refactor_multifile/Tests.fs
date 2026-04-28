module Tests

open Calculator

let testAdd () =
    let actual = add 2 3
    let expected = 5
    if actual <> expected then
        failwithf "testAdd: expected %d, got %d" expected actual
    printfn "testAdd: PASS"

let testAdd3 () =
    let actual = add3 1 2 3
    let expected = 6
    if actual <> expected then
        failwithf "testAdd3: expected %d, got %d" expected actual
    printfn "testAdd3: PASS"
