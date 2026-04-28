module BugBinSearch

/// Binary search returning Some index of `target` in sorted `arr`, or None.
///
/// BUG: When `hi <- mid` (instead of `hi <- mid - 1`), and `lo` happens to
/// equal `mid`, the loop never terminates because the search window does
/// not shrink. Specifically:
///   - With arr = [|1; 3; 5|] and target = 4, after first iteration:
///     lo=0, hi=2, mid=1, arr.[1]=3 < 4 → lo <- 2
///     lo=2, hi=2, mid=2, arr.[2]=5 > 4 → hi <- mid = 2  (NO PROGRESS)
///     loop continues forever.
let binsearch (arr: int []) (target: int) : int option =
    let mutable lo = 0
    let mutable hi = arr.Length - 1
    let mutable result : int option = None
    while lo <= hi && result.IsNone do
        let mid = lo + (hi - lo) / 2
        if arr.[mid] = target then
            result <- Some mid
        elif arr.[mid] < target then
            lo <- mid + 1
        else
            // BUG: should be `hi <- mid - 1` to make progress
            hi <- mid

    result

/// Example caller — DO NOT use as test (will hang on triggering inputs).
let demo () =
    binsearch [| 1; 3; 5 |] 4
