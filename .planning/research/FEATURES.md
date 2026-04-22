# Feature Research

**Domain:** Local-LLM coding agent (F#, Qwen 32B/72B, Mac-local)
**Researched:** 2026-04-22
**Confidence:** HIGH

Sources: claw-code-agent README + PARITY_CHECKLIST (Python reference impl, April 2026),
qwen_claude_full_design.md, qwen_agent_rewrite.md, agent_32b_72b_codegpt.md (user design notes).

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features the user's own design notes call mandatory. Missing any of these = agent does not run.

| Feature | Why Expected | Complexity | v1 Minimal? | Notes |
|---------|--------------|------------|-------------|-------|
| Agent loop: prompt → LLM → tool → observe → loop | The entire point of the product | S | YES | Max 5 iterations; simple `while not done` |
| Structured JSON output enforcement (`{thought, action, input}`) | Qwen hallucinates without format constraints; malformed output = broken loop | S | YES | Core schema is 3 fields; retry on parse failure |
| read_file tool | Any coding task reads files first | S | YES | Needs line-range support to avoid context explosion |
| write_file tool | Code changes require writes | S | YES | Full overwrite; no patch needed in v1 |
| list_dir tool | Agent must navigate project structure | S | YES | Non-recursive by default; depth-limited |
| run_shell tool | Build, test, lint — essential coding loop | M | YES | Needs a hard timeout; no interactive stdin |
| Error/retry on malformed LLM output | Qwen 32B produces broken JSON under load; one bad step kills the session | S | YES | Max 3 parse retries before surfacing error to user |
| Max-iteration cap | Prevents runaway loops burning VRAM/time | S | YES | Default 5; configurable via CLI flag |
| CLI stdin/stdout interaction | Personal tool; no GUI needed in v1 | S | YES | Single command: `blueCode "<prompt>"` |
| Multi-turn conversation | Core REPL loop | S | YES | Maintain message history within session |
| Direct 32B/72B model routing (intent-based) | 32B is fast, 72B is smarter; routing avoids wasting time on trivial tasks | M | YES | Intent DU: Simple/Complex; model DU: Qwen32B/Qwen72B; keyword scoring from agent_32b_72b_codegpt.md |

### Differentiators (Competitive Advantage)

These separate blueCode from a raw curl loop or a Python throw-away script. Ordered by value-to-user.

| Feature | Value Proposition | Complexity | Priority | v1? | Notes |
|---------|-------------------|------------|----------|-----|-------|
| Transparent step log (thought/action/input/output/status) | Debugging agent failures is impossible without step visibility; verbose/compact toggle | S | must (v1) | YES | Two renderers, one data model. Start verbose; compact later. From qwen_claude_full_design.md §10 |
| Simple session memory (last N steps) | Agent can reference what it just did without re-reading the whole history | S | must (v1) | YES | Ring buffer of last 3 steps; user explicitly specified this |
| Streaming token output | Qwen 32B inference is slow; users stare at blank terminal without it | M | should (v1) | YES | Server-sent events from vLLM/Ollama; print tokens as received |
| Context window guard / max-token check | 32B context is 128k; long file reads blow it silently | M | should (v1) | YES | Preflight: estimate prompt size before LLM call; truncate or warn |
| Compact/verbose output toggle | Power users want detail; default should be clean | S | should (v1) | YES | `--verbose` flag; default compact |
| Session persistence / resume | Long coding tasks get interrupted; resume avoids starting over | M | v2 | NO | Save session state to disk (JSON); `--resume <id>` |
| Context compaction / auto-snip | As loops accumulate, context fills; old tool results can be summarised | M | v2 | NO | Snip tool-result messages beyond N tokens; keep system + recent tail |
| Slash commands (`/context`, `/compact`, `/agents`) | REPL power moves; not needed until multi-turn REPL is stable | M | v2 | NO | `/context` shows token usage; `/compact` forces compaction |
| edit_file tool (exact-string replace) | Surgical edits without full rewrites; important for large files | M | v2 | NO | Needs old_string/new_string matching; do NOT implement in v1, write_file is enough |
| glob_search / grep_search tools | Find files by pattern or content; very useful once base tools work | M | v2 | NO | Natural extension of list_dir + read_file |
| Sub-agent delegation | Parallelize subtasks; delegate to specialized agents | XL | v2+ | NO | Depends on session persistence; likely v3 |
| Cost/token budget tracking | Token-budget reporting per session; useful for long runs | M | v2+ | NO | Simple counter on response.usage fields |
| Structured output mode (response schemas) | Programmatic use; parse agent result in calling code | M | v2+ | NO | `--response-schema-file` flag; claw-code-agent has this fully |
| File edit journaling / replay | Snapshot file state before each write; replay on resume | L | v2+ | NO | Complex; claw-code-agent has it; overkill for personal tool |
| CLAUDE.md / project memory discovery | Context injection from project docs | M | v2+ | NO | Walk dirs for CLAUDE.md; inject into system prompt |

### Anti-Features (DO NOT Build)

Features that appear useful but will either hurt Qwen reliability, waste implementation time, or contradict the user's explicit scope.

| Anti-Feature | Why Avoid | Category | What to Do Instead |
|--------------|-----------|----------|--------------------|
| MCP runtime | User explicitly OUT-of-scope; adds 25+ file complexity; no personal-use benefit | Scope creep | Never. Use run_shell to call external CLIs if needed |
| LSP code intelligence | Heuristic LSP works poorly; real LSP needs server-backed daemon; wrong complexity for v1 | Over-engineering | Use grep_search for symbol lookup in v2+ |
| Plugin/hook system | Single-user agent gains nothing from plugin manifests; claw-code-agent has it, it's 654 lines of complexity | Over-engineering | Hard-code the 4 tools in v1; add tools as F# modules in v2 |
| Permissions/policy UI | For a personal single-user tool, tiered `--allow-write / --allow-shell` flags are enough; a permissions management UI is 0 value | Over-engineering | CLI flags only; no runtime policy manifest |
| Remote runtime / SSH / bridge | User explicitly OUT-of-scope | Scope creep | Never |
| GUI (web, Ink/TUI) | User explicitly OUT-of-scope; adds FastAPI/JS dep, breaks zero-dep goal | Scope creep | CLI stdout only |
| Windows/Linux support | User explicitly Mac-only | Scope creep | Darwin paths only; no platform abstraction layer |
| AOT / single binary | User explicitly removed from scope | Scope creep | dotnet run or published self-contained Mac app; not AOT |
| Reusing Claude Code prompts directly | Claude-tuned prompts produce hallucination/format errors on Qwen; the user's notes say this explicitly | Qwen mismatch | Write Qwen-specific prompts with strict JSON instruction |
| Heavy reasoning chains (chain-of-thought with >5 steps) | Qwen 32B loses coherence in long chains; context grows fast; hallucination rate climbs | Qwen limitation | Keep loop cap at 5; break complex tasks into sub-questions via prompt |
| OCR / image understanding | Qwen 32B/72B text models have no vision unless using VL variant | Qwen limitation | Never. Route image tasks to human or use a VL model separately |
| Nested recursion / sub-agent delegation in v1 | Qwen is unstable enough at the single-loop level; nested agents multiply failure modes | Qwen limitation | Flat single-agent loop in v1; add delegation only in v2+ when base loop is proven |
| Parallel tool execution (multiple tools per step) | User's design explicitly says "one tool per step, no chaining" | Design constraint | Sequential tool calls only; the JSON schema enforces this |
| Background / daemon sessions | Adds process management complexity with zero benefit for interactive personal use | Over-engineering | Foreground sessions only |
| Worktree runtime | User explicitly OUT-of-scope | Scope creep | git operations via run_shell |
| NuGet zero-dependency restriction | User removed this from scope explicitly | Scope creep | Use NuGet freely (System.Text.Json, etc.) |
| Full context memory extraction (LLM-based summarization) | Requires a second LLM call just for compaction; expensive and fragile on local Qwen | Qwen limitation | Simple ring-buffer memory (last 3 steps); snip old tool results by token count |
| Team / collaboration runtime | Single-user personal tool | Scope creep | Never |
| Analytics / telemetry | Single-user local tool | Scope creep | Never |
| Voice mode | Out of scope; requires STT/TTS deps | Scope creep | Never |
| Notebook (.ipynb) editing | Not a target use case | Scope creep | Never |

---

## Feature Dependencies

```
[Agent loop (v1)]
    requires --> [Structured JSON output enforcement]
    requires --> [LLM client (OpenAI-compat HTTP)]
    requires --> [Tool dispatcher]
    requires --> [read_file / write_file / list_dir / run_shell]
    requires --> [Error/retry on bad JSON]
    requires --> [Max-iteration cap]

[Streaming output (v1)]
    requires --> [LLM client with SSE support]
    requires --> [Agent loop]

[Step log / transparent trace (v1)]
    requires --> [Agent loop]
    enhances --> [Compact/verbose toggle]

[Context window guard (v1)]
    requires --> [LLM client (token counts from response.usage)]
    enhances --> [Agent loop stability]

[Intent routing 32B/72B (v1)]
    requires --> [Agent loop]
    requires --> [Two model endpoints (vLLM ports)]

[Session persistence (v2)]
    requires --> [Agent loop]
    requires --> [Step log data model]

[Context compaction (v2)]
    requires --> [Session persistence OR in-memory ring buffer]
    requires --> [Context window guard]

[Slash commands (v2)]
    requires --> [Multi-turn REPL]
    enhances --> [Context compaction] (/compact)
    enhances --> [Context window guard] (/context)

[edit_file tool (v2)]
    requires --> [write_file] (implementation reference)
    enhances --> [Agent loop quality]

[glob_search / grep_search (v2)]
    requires --> [list_dir] (concept)
    enhances --> [Agent loop tool surface]

[Sub-agent delegation (v2+)]
    requires --> [Session persistence]
    requires --> [Agent loop] (fully stable)
    requires --> [Context compaction]
```

### Dependency Notes

- **Session persistence requires a stable agent loop first:** Persisting a broken loop produces garbage resumes. Validate the v1 loop thoroughly before adding persistence.
- **Context compaction requires knowing token counts:** The context window guard is therefore a prerequisite, not a differentiator. It belongs in v1.
- **Slash commands require a multi-turn REPL:** The v1 single-shot CLI does not need slash commands. They belong in v2 when the REPL loop is the primary interface.
- **Intent routing has no hard dependency on any other feature:** It can be added as a thin wrapper around the LLM client at any point, but it delivers more value once the loop is stable (v1 end state or v2 start).

---

## MVP Definition

### Launch With (v1 Minimal — locked by user)

- [x] Agent loop: prompt → LLM → tool → observe → loop (max 5 iterations) — the core contract
- [x] Structured JSON output: `{thought, action, input}` with retry on parse failure — Qwen stability
- [x] read_file — coding agent without file reads is useless
- [x] write_file — coding agent without writes can only advise
- [x] list_dir — navigation; required before read or write
- [x] run_shell — build/test feedback; essential for coding loop
- [x] CLI entry: `blueCode "<prompt>"` with stdout response — minimum viable interface
- [x] Compact/verbose step log — debugging and transparency
- [x] Context window guard (token preflight) — prevents silent failures on large files
- [x] Direct 32B/72B routing (intent DU) — user's explicit design; 32B for simple, 72B for reasoning

### Add After Validation (v1.x — once loop is stable)

- [ ] Streaming token output — trigger: user finds blank terminal too slow
- [ ] Session persistence + resume — trigger: user loses context on long tasks
- [ ] edit_file tool (exact-string replace) — trigger: full-file writes cause diff noise
- [ ] glob_search / grep_search tools — trigger: user asks agent to "find where X is defined"

### Future Consideration (v2+)

- [ ] Context compaction / auto-snip — defer: only matters when sessions run >10 turns; build context guard first and measure
- [ ] Slash commands (`/context`, `/compact`, `/agents`) — defer: needs stable REPL first
- [ ] Sub-agent delegation — defer: proves useful only after base loop has run 50+ real sessions; multiplies failure modes otherwise
- [ ] CLAUDE.md / project memory — defer: nice polish, not essential
- [ ] File edit journaling / replay — defer: overkill for personal use; only valuable if the agent damages files, which run_shell's dry-run mode can prevent

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Agent loop (core) | HIGH | LOW | P1 |
| Strict JSON output + retry | HIGH | LOW | P1 |
| read_file / write_file / list_dir / run_shell | HIGH | LOW | P1 |
| Max-iteration cap | HIGH | LOW | P1 |
| Compact/verbose step log | HIGH | LOW | P1 |
| Context window guard | HIGH | LOW | P1 |
| CLI stdin/stdout | HIGH | LOW | P1 |
| 32B/72B intent routing | HIGH | MEDIUM | P1 |
| Streaming output | MEDIUM | MEDIUM | P2 |
| Session persistence / resume | HIGH | MEDIUM | P2 |
| edit_file (exact-string) | MEDIUM | MEDIUM | P2 |
| glob_search / grep_search | MEDIUM | LOW | P2 |
| Context compaction | MEDIUM | MEDIUM | P2 |
| Slash commands | LOW | MEDIUM | P3 |
| Sub-agent delegation | MEDIUM | HIGH | P3 |
| File edit journaling | LOW | HIGH | P3 |
| CLAUDE.md discovery | LOW | LOW | P3 |
| Structured output schema | LOW | MEDIUM | P3 |
| MCP runtime | NONE | XL | SKIP |
| Plugin/hook system | NONE | XL | SKIP |
| GUI (web/TUI) | NONE | XL | SKIP |
| LSP intelligence | LOW | XL | SKIP |
| Remote/worktree runtime | NONE | XL | SKIP |
| Permissions/policy UI | NONE | MEDIUM | SKIP |
| Team/collaboration | NONE | XL | SKIP |

**Priority key:**
- P1: Must have for v1 Minimal launch
- P2: Should have; add in v1.x once core is validated
- P3: Nice to have; v2+ only
- SKIP: Out of scope permanently or anti-feature

---

## Qwen-Specific Feature Warnings

These are behaviors that work in Claude Code or claw-code-agent but fail or degrade on Qwen 32B/72B:

| Claude/claw Feature | Qwen Reality | Mitigation in blueCode |
|---------------------|--------------|------------------------|
| Multi-tool chaining in one step | Qwen misroutes or picks wrong tool when given parallel choices | Enforce single-tool-per-step via JSON schema; `action` is singular |
| Long system prompts (>2k tokens) | Qwen 32B instruction-following degrades under dense system prompts | Keep system prompt tight: role + JSON schema + 4 tool defs |
| Claude's original prompts (reasoning, planning sections) | Claude-tuned prompts produce format errors and hallucination on Qwen | Write Qwen-specific prompts; test on both 32B and 72B |
| Tool reliability assumption (no retry) | Qwen produces malformed JSON in ~5-15% of turns under load | Always retry on JSON parse failure; max 3 retries per step |
| Nested delegation without proven base loop | Qwen 32B loses parent context when acting as both orchestrator and sub-agent | Build flat loop first; add delegation only in v2+ |
| `finish_reason=length` handling | Qwen truncates mid-JSON when output is long | Check finish_reason; if length, retry with shorter tool result summaries |
| Free-form reasoning before JSON | Some Qwen prompts allow thinking-then-JSON; this bloats context fast | Require JSON-only response; strip `<think>` tags if Qwen3 thinking mode is on |
| Context compaction via LLM summarization | A second LLM call for summarization doubles VRAM usage and latency | Use token-count-based snipping only; no LLM-based compaction |

---

## Competitor Feature Analysis

| Feature | claw-code-agent (Python) | blueCode (F# target) |
|---------|--------------------------|----------------------|
| Core agent loop | Full iterative loop, all modes | Same pattern, F# DU-typed steps |
| Tool surface | 65+ tools | 4 tools in v1; expand in v2+ |
| JSON enforcement | OpenAI tool-call schema | Custom `{thought, action, input}` strict schema |
| Model routing | Single model endpoint | 32B/72B intent-based routing (user's design) |
| Session persistence | Full journaling + replay | v2; ring-buffer memory in v1 |
| Context compaction | Auto-snip + LLM compact | Token-snip only in v2; no LLM compact |
| Streaming | Token-by-token SSE | v1 stretch goal; v1.x confirmed |
| Slash commands | 53 commands across 37 specs | v2; /context + /compact first |
| Sub-agents | Topological batching, delegation | v2+; flat loop only in v1 |
| Plugin system | Full manifest-based runtime | SKIP; hard-coded tool set |
| MCP | Local stdio transport | SKIP |
| GUI | FastAPI + vanilla JS | SKIP |
| LSP | Heuristic local LSP | SKIP |
| Permissions | 4-tier --allow flags | CLI flags only; no UI |
| Cost tracking | Token + USD budgets | v2+; response.usage counter |
| Zero-dependency constraint | Python stdlib only | NuGet allowed; System.Text.Json etc. |
| Platform | Linux/Mac/Ollama/vLLM | Mac + Ollama/vLLM only |

---

## Sources

- `/Users/ohama/projs/claw-code-agent/README.md` — full claw-code-agent feature surface (April 2026 update)
- `/Users/ohama/projs/claw-code-agent/PARITY_CHECKLIST.md` — 20-section implementation status vs npm Claude Code
- `/Users/ohama/projs/blueCode/localLLM/qwen_claude_full_design.md` — user's Qwen design strategy ("reuse architecture, remove complexity")
- `/Users/ohama/projs/blueCode/localLLM/qwen_agent_rewrite.md` — Qwen-oriented design principles
- `/Users/ohama/projs/blueCode/localLLM/agent_32b_72b_codegpt.md` — intent classification + model routing logic

---
*Feature research for: local-LLM coding agent (F#, Qwen 32B/72B)*
*Researched: 2026-04-22*
