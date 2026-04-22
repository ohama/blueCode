module BlueCode.Core.Router

open BlueCode.Core.Domain

/// Classifies free-text user input into an Intent by scanning for
/// characteristic keywords. Pure: no IO, no mutation, deterministic.
///
/// ROU-01: Debug/Design/Analysis -> 72B, Implementation/General -> 32B
/// (routing rule is applied by intentToModel, not here).
///
/// Keyword sets intentionally cover English + Korean where this repo's
/// user works bilingually. Extending the lists does not change the
/// function's signature or purity.
let classifyIntent (userInput: string) : Intent =
    let s = userInput.ToLowerInvariant()
    let anyMatch (needles: string list) = needles |> List.exists s.Contains
    if   anyMatch ["error"; "bug"; "fix"; "debug"; "traceback"; "exception"; "null"] then Debug
    elif anyMatch ["design"; "architecture"; "system"; "구조"; "설계"]               then Design
    elif anyMatch ["analyze"; "analyse"; "compare"; "tradeoff"; "difference"; "분석"] then Analysis
    elif anyMatch ["write"; "implement"; "code"; "example"]                           then Implementation
    else General

/// Maps an Intent to the Qwen model that should handle it (ROU-02).
/// Exhaustive match — adding a new Intent case without updating this
/// function is a compile error (FS0025). NEVER add `| _ ->` here.
let intentToModel : Intent -> Model = function
    | Debug | Design | Analysis -> Qwen72B
    | Implementation | General  -> Qwen32B

/// Maps a Model to its serving endpoint (ROU-03).
/// Port 8000 hosts 32B; Port 8001 hosts 72B (PROJECT.md Context).
let modelToEndpoint : Model -> Endpoint = function
    | Qwen32B -> Port8000
    | Qwen72B -> Port8001

/// Resolves an Endpoint to a concrete HTTP URL. Phase 2 consumes this.
let endpointToUrl : Endpoint -> string = function
    | Port8000 -> "http://127.0.0.1:8000/v1/chat/completions"
    | Port8001 -> "http://127.0.0.1:8001/v1/chat/completions"
