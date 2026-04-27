module BlueCode.Core.Router

open BlueCode.Core.Domain

// NOTE (v1.1 / REF-01): model-id resolution moved to QwenHttpClient adapter. The
// vLLM model-id string is resolved at runtime via GET /v1/models per port.
// Core does not own the wire value; the adapter layer does.

/// Classifies free-text user input into an Intent by scanning for
/// characteristic keywords. Pure: no IO, no mutation, deterministic.
///
/// ROU-01: Debug/Design/Analysis -> 35B (smaller/faster), Implementation/General -> 122B (larger).
/// (routing rule is applied by intentToModel, not here). Note: dormant in single-model
/// default mode since ForcedModel = Some Qwen122B bypasses this classification.
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

/// Intent routing: dormant in single-model default mode (ForcedModel = Some Qwen122B).
/// Active when both --with-35b and explicit dual-mode invocation are set.
/// Phase 19: retained for future SHIP-BOTH evolution.
///
/// Maps an Intent to the Qwen model that should handle it (ROU-02).
/// Exhaustive match — adding a new Intent case without updating this
/// function is a compile error (FS0025). NEVER add `| _ ->` here.
let intentToModel: Intent -> Model =
    function
    | Debug
    | Design
    | Analysis -> Qwen35B
    | Implementation
    | General -> Qwen122B

/// Maps a Model to its serving endpoint (ROU-03).
/// Port 8000 hosts 35B (smaller); Port 8001 hosts 122B (larger). Phase 19: renamed from 32B/72B.
let modelToEndpoint: Model -> Endpoint =
    function
    | Qwen35B -> Port8000
    | Qwen122B -> Port8001

/// Resolves an Endpoint to a concrete HTTP URL. Phase 2 consumes this.
let endpointToUrl: Endpoint -> string =
    function
    | Port8000 -> "http://127.0.0.1:8000/v1/chat/completions"
    | Port8001 -> "http://127.0.0.1:8001/v1/chat/completions"

/// Per-model sampling parameters per the Qwen 3.5 model card (non-thinking coding mode).
/// Both 35B and 122B use temperature=0.7, top_p=0.8, top_k=20, presence_penalty=0.0.
/// Identical values today; explicit pattern match preserves compile-time exhaustiveness
/// so future per-model tuning is a one-line change. Replaces the v1.0-era per-model
/// temperature function (0.2/0.4) which targeted the retired Qwen 2.5 pair.
let modelToSamplingParams: Model -> SamplingParams =
    function
    | Qwen35B  -> { Temperature = 0.7; TopP = 0.8; TopK = 20; PresencePenalty = 0.0 }
    | Qwen122B -> { Temperature = 0.7; TopP = 0.8; TopK = 20; PresencePenalty = 0.0 }
