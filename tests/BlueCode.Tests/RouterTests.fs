module BlueCode.Tests.RouterTests

open Expecto

// Plan 01-02 populates the test cases.
let routerTests = testList "Router" []

[<EntryPoint>]
let main args = runTestsWithCLIArgs [] args routerTests
