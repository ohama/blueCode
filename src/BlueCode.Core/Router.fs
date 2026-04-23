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

    if anyMatch [ "error"; "bug"; "fix"; "debug"; "traceback"; "exception"; "null" ] then
        Debug
    elif anyMatch [ "design"; "architecture"; "system"; "구조"; "설계" ] then
        Design
    elif anyMatch [ "analyze"; "analyse"; "compare"; "tradeoff"; "difference"; "분석" ] then
        Analysis
    elif anyMatch [ "write"; "implement"; "code"; "example" ] then
        Implementation
    else
        General

/// Maps an Intent to the Qwen model that should handle it (ROU-02).
/// Exhaustive match — adding a new Intent case without updating this
/// function is a compile error (FS0025). NEVER add `| _ ->` here.
let intentToModel: Intent -> Model =
    function
    | Debug
    | Design
    | Analysis -> Qwen72B
    | Implementation
    | General -> Qwen32B

/// Maps a Model to its serving endpoint (ROU-03).
/// Port 8000 hosts 32B; Port 8001 hosts 72B (PROJECT.md Context).
let modelToEndpoint: Model -> Endpoint =
    function
    | Qwen32B -> Port8000
    | Qwen72B -> Port8001

/// Resolves an Endpoint to a concrete HTTP URL. Phase 2 consumes this.
let endpointToUrl: Endpoint -> string =
    function
    | Port8000 -> "http://127.0.0.1:8000/v1/chat/completions"
    | Port8001 -> "http://127.0.0.1:8001/v1/chat/completions"

/// Maps a Model to the exact model-name string used in the vLLM
/// OpenAI `"model"` request field. These strings MUST match whatever
/// vLLM reports via GET /v1/models on the local host (Phase 5 OBS-03
/// will query this at runtime; Phase 2 hardcodes the served names).
let modelToName: Model -> string =
    function
    | Qwen32B -> "/Users/ohama/llm-system/models/qwen32b"
    | Qwen72B -> "/Users/ohama/llm-system/models/qwen72b"

/// Per-model sampling temperature (LLM-05). Hardcoded; MUST NOT be
/// exposed to users via CLI flags. 32B uses 0.2 (precise code edits);
/// 72B uses 0.4 (more exploratory reasoning for Debug/Design/Analysis).
let modelToTemperature: Model -> float =
    function
    | Qwen32B -> 0.2
    | Qwen72B -> 0.4
