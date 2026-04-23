module BlueCode.Tests.ContextWarningTests

open Expecto
open BlueCode.Cli.Repl

// Note: NO [<Tests>] attribute — this project uses explicit rootTests registration
// in RouterTests.fs. See STATE.md Accumulated Decisions (04-02).

let tests =
    testList
        "Repl.shouldWarnContextWindow"
        [

          testCase "below 80% threshold -> no warning"
          <| fun _ ->
              // maxModelLen = 8192, max chars ~= 32768, 80% ~= 26214
              Expect.isFalse (shouldWarnContextWindow 10000 8192 false) "10K chars of 32K budget should not warn"

          testCase "exactly at 80% boundary -> warn"
          <| fun _ ->
              // 80% of 8192*4 = 26214.4; integer threshold is totalChars*5 >= maxModelLen*16
              // 8192 * 16 = 131072; need totalChars*5 >= 131072, so totalChars >= 26215 (ceil)
              // 26214*5 = 131070 < 131072 -> no warn
              // 26215*5 = 131075 >= 131072 -> warn
              Expect.isFalse
                  (shouldWarnContextWindow 26214 8192 false)
                  "26214 chars (just below boundary) should NOT warn"

              Expect.isTrue (shouldWarnContextWindow 26215 8192 false) "26215 chars (at/above 80% boundary) should warn"

          testCase "above 80% threshold -> warn"
          <| fun _ ->
              Expect.isTrue
                  (shouldWarnContextWindow 30000 8192 false)
                  "30000 chars well above 80% of 8192 token budget should warn"

          testCase "already warned this turn -> suppressed"
          <| fun _ ->
              Expect.isFalse
                  (shouldWarnContextWindow 30000 8192 true)
                  "Subsequent triggers in same turn are suppressed (alreadyWarned=true)"

          testCase "larger model (32768 max_model_len) scales threshold"
          <| fun _ ->
              // 80% of 32768*4 = 104857.6 chars
              // threshold: totalChars*5 >= 32768*16 = 524288; need >= 104858 chars
              Expect.isFalse
                  (shouldWarnContextWindow 100000 32768 false)
                  "100K chars of 128K budget should not warn for 32768 token model"

              Expect.isTrue
                  (shouldWarnContextWindow 110000 32768 false)
                  "110K chars exceeds 80% of 32768 token model budget"

          testCase "zero chars never warns"
          <| fun _ ->
              Expect.isFalse
                  (shouldWarnContextWindow 0 8192 false)
                  "Zero accumulated chars should never trigger warning"

          testCase "small model (2048) fires warning earlier"
          <| fun _ ->
              // 80% of 2048*4 = 6553.6 chars
              // threshold: totalChars*5 >= 2048*16 = 32768; need >= 6554 chars
              Expect.isFalse
                  (shouldWarnContextWindow 6000 2048 false)
                  "6000 chars below 80% of 2048 token model (threshold ~6554)"

              Expect.isTrue (shouldWarnContextWindow 7000 2048 false) "7000 chars above 80% of 2048 token model" ]
