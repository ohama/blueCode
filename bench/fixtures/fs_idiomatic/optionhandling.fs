module OptionHandling

/// Parses input; returns 2*value if successful, 0 if not.
/// Idiomatic implementation should use Option.map + Option.defaultValue.
let safeDouble (input: string) : int =
    failwith "TODO: implement safeDouble using Option.map and Option.defaultValue"

for s in [ "42"; "abc"; "0"; "-7"; "" ] do
    let r = try safeDouble s with _ -> -1
    printfn "safeDouble %A = %d" s r
