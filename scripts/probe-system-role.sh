#!/usr/bin/env bash
# Phase 20-03: probe whether 122B (mlx_lm.server, port 8001) accepts mid-conversation
# Role = System messages. Re-runnable evidence for the AgentLoop.fs role decision.
#
# Exit code: 0 if HTTP 200 + non-empty content + no <|fim_ tokens → ACCEPT
#            1 if HTTP non-200 OR empty content OR <|fim_ tokens detected → REJECT
#
# Usage:   bash scripts/probe-system-role.sh
# Output:  HTTP code + first 600 chars of response body to stdout
#
# NOTE: The model field below is dynamically extracted from /v1/models data[1].id
# (the local filesystem path) per the Phase 19 gotcha — sending the HF repo id as
# data[0].id triggers mlx_lm.server's HuggingFace fallback tokenizer trap, which
# overwrites the loaded Instruct template with a Base/FIM tokenizer and produces
# `<|fim_*|>` tokens in responses. Using data[1].id matches blueCode production
# behavior (QwenHttpClient.tryParseModelId path-preference heuristic).
set -u
URL="http://127.0.0.1:8001/v1/chat/completions"
MODELS_URL="http://127.0.0.1:8001/v1/models"

# Step A: dynamically resolve the local model path (data[1].id)
MODEL_ID=$(curl -fsS "$MODELS_URL" | python3 -c "import sys, json; d=json.load(sys.stdin); ids=[m['id'] for m in d.get('data', [])]; locals_=[i for i in ids if i.startswith('/')]; print(locals_[0] if locals_ else (ids[1] if len(ids) > 1 else ids[0]))" 2>/dev/null || echo "")
if [[ -z "$MODEL_ID" ]]; then
    echo "ERROR: failed to extract local model path from $MODELS_URL"
    exit 2
fi
echo "Resolved model id (local path): $MODEL_ID"

BODY=$(python3 -c "
import json
print(json.dumps({
    'model': '$MODEL_ID',
    'messages': [
        {'role': 'system', 'content': 'You are a terse assistant. Respond in one short sentence.'},
        {'role': 'user', 'content': 'What is 2+2?'},
        {'role': 'system', 'content': '[CONSTRAINT] You must answer with the digit 4, no prose.'}
    ],
    'temperature': 0.7,
    'top_p': 0.8,
    'top_k': 20,
    'presence_penalty': 0.0,
    'max_tokens': 64,
    'stream': False
}))
")
echo "POST $URL"
echo "Messages: system / user / system (mid-conversation system role probe)"
RESPONSE=$(curl -sS -o /tmp/probe-system-role.body -w "%{http_code}" -H "Content-Type: application/json" -d "$BODY" "$URL")
HTTP=$RESPONSE
BODYTXT=$(cat /tmp/probe-system-role.body | head -c 600)
echo "HTTP code: $HTTP"
echo "Response body (first 600 chars):"
echo "----"
echo "$BODYTXT"
echo "----"

# Defensive sanity check: even with the correct local-path model id, verify the
# loaded tokenizer is Instruct (not Base/FIM-mode). <|fim_ tokens in the response
# indicate the HF fallback trap fired anyway — treat as REJECT.
if grep -q '<|fim_' /tmp/probe-system-role.body; then
    echo "Verdict: REJECT (response contains <|fim_ tokens — Base/FIM tokenizer loaded, not Instruct)"
    exit 1
fi

if [[ "$HTTP" == "200" ]]; then
    # Extract content + reasoning_content; if either is non-empty, ACCEPT
    CONTENT=$(cat /tmp/probe-system-role.body | python3 -c "import sys, json; d=json.load(sys.stdin); m=d.get('choices',[{}])[0].get('message',{}); print((m.get('content') or m.get('reasoning_content') or '').strip())" 2>/dev/null || echo "")
    if [[ -n "$CONTENT" ]]; then
        echo "Verdict: ACCEPT (HTTP 200, content non-empty: '$CONTENT')"
        exit 0
    else
        echo "Verdict: REJECT (HTTP 200 but content empty — likely silent chat-template skip)"
        exit 1
    fi
else
    echo "Verdict: REJECT (HTTP $HTTP)"
    exit 1
fi
