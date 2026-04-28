module Main

open Calculator

[<EntryPoint>]
let main argv =
    let result = add 2 3
    let result3 = add3 1 2 3
    printfn "add 2 3 = %d" result
    printfn "add3 1 2 3 = %d" result3
    0
