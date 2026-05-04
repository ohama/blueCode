# Requirements: blueCode v2.5 REPL ergonomics

**Defined:** 2026-04-29
**Core Value:** Mac 로컬 Qwen 3.5 122B를 strong-typed F# agent loop로 **empirically** 안정적으로 돌린다 (post-v2.4 verdict 96/100 KEEP)
**Milestone goal:** REPL 인터랙티브 사용성을 daily-driver 가치 수준으로 끌어올림. 4 가지 ergonomic gap (slash commands / multi-line input / readline / history) 을 한 milestone 에 묶음. v1.2 Tool ergonomics 와 같은 shape; Cli-layer only; Core purity / bench gate / schema 0/50 / `bench/baseline.json` byte-equal invariant 모두 보존.

## v2.5 Requirements (3 categories, 12 requirements)

### Slash commands (7 reqs)

- [x] **SLASH-01**: User can type `/help` in REPL to see a list of available slash commands with one-line descriptions
- [x] **SLASH-02**: User can type `/status` in REPL to see current session id, current model, step count for current turn, accumulated char count, and 32k context utilization percentage (absorbs OBS-06 token visibility)
- [x] **SLASH-03**: User can type `/clear` to reset priorSteps and start a fresh session id in-place without exiting the REPL
- [x] **SLASH-04**: User can type `/exit` or `/quit` to gracefully exit the REPL with final session state saved
- [x] **SLASH-05**: User can type `/sessions` to see a list of recent N persisted sessions (id, started_at, turn count, first prompt excerpt)
- [x] **SLASH-06**: User can type `/resume <id>` to switch to another persisted session in-place, threading its priorSteps into the current REPL
- [x] **SLASH-07**: User can type `/plan` to toggle plan-mode for subsequent turns (`/plan` on; `/plan` again off); takes effect from the next prompt

### Multi-line input (1 req)

- [ ] **EDIT-01**: User can type `/edit` to open `$EDITOR` (or `vi` fallback) on a tmpfile; saved content becomes the next prompt; tmpfile cleaned up after read or on REPL exit

### Readline + history (4 reqs)

- [ ] **HIST-01**: REPL uses PrettyPrompt NuGet for input; replaces `Console.ReadLine` in `Repl.fs` multi-turn loop
- [ ] **HIST-02**: User can press Up/Down arrow keys to navigate prior prompts within the current REPL session
- [ ] **HIST-03**: Prompt history persists across REPL invocations to `~/.bluecode/history` (line-per-prompt; loaded on REPL start; written on each prompt submit)
- [ ] **HIST-04**: User can press Ctrl+R to reverse-search through history (PrettyPrompt built-in)

## Out of Scope

| Feature | Reason |
|---------|--------|
| `/model 35b` mid-session switch | `--with-35b` opt-in semantics + 35B service load detection 검증 비용 큼; v2.6+ candidate |
| `/save <name>` named sessions | bare session id 로 충분 (사용자가 `/sessions` 에서 식별 가능); 명명은 future ergonomic, 현재 pain signal 없음 |
| Auto-completion of slash commands | PrettyPrompt 가 cheap 하면 future polish; 현재 9 commands 만으로 외워서 사용 가능 |
| 자체 readline 구현 | PrettyPrompt 로 갈음; ~300-400 LOC fragile ANSI-escape 코드 회피 (Key Decision entry 참조) |
| Cross-session history search UI | Ctrl+R in-session 만으로 시작; 과거 session 내용은 `/sessions` + `/resume` 로 접근 |
| COMPACT-01 자동 압축 | token visibility 만 (`/status`); 자동 압축은 daily-driver 에서 32k hit 가 measured 된 후 v2.6+ |
| STM-01 SSE streaming | 10번째 deferred (v2.4 close 시점 기준); defer pattern 자체가 load-bearing signal |
| AGENT-LOOP-FEW-SHOT-01 (P2 migration) | v2.4 close 시점 surface 안 됨; carried-forward to v2.6+ |
| COLDSTART-PRISTINE-01 | low urgency; 별도 reboot window 필요; carried-forward |
| SUBAG-01 sub-agent delegation | nested schema 가 0/50 perfect 깰 수 있음; v3.0 territory |
| THINK-01 thinking-mode-on | thinking-OFF 가 schema 0/50 보장; defer indefinitely |
| TOOLCALLS-01 native OpenAI tool_calls | v3.0 territory; custom JSON schema 0/50 perfect 유지 |
| Bench fixture 변경 / `bench/baseline.json` 변경 | v2.5 는 REPL UX 만; bench 영역 무관 |

## Future Requirements (v2.6+ candidates)

Tracked for awareness; not pulled into v2.5. Observation-driven scoping after v2.5 ships.

- **MODEL-SWITCH-01** — `/model 35b` / `/model 122b` mid-session switch (requires `--with-35b` opt-in + 35B service load probe)
- **SLASH-COMP-01** — Slash command auto-completion (PrettyPrompt completion API 사용 가능 시 cheap)
- **HIST-SEARCH-01** — Cross-session history search beyond Ctrl+R (예: `/find <pattern>`)
- **COMPACT-01** — Auto-compaction trigger when `/status` 가 80%+ 표시 시 (v2.5 가 visibility 까지만; trigger 는 측정 후)
- v2.4 close 의 deferred candidates: AGENT-LOOP-FEW-SHOT-01, COLDSTART-PRISTINE-01, SUBAG-01, PLAN-MODE-BENCH-01, STM-01, THINK-01, TOOLCALLS-01

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| SLASH-01 | Phase 31 | Complete |
| SLASH-02 | Phase 31 | Complete |
| SLASH-03 | Phase 31 | Complete |
| SLASH-04 | Phase 31 | Complete |
| SLASH-05 | Phase 32 | Complete |
| SLASH-06 | Phase 32 | Complete |
| SLASH-07 | Phase 33 | Complete |
| EDIT-01 | Phase 34 | Pending |
| HIST-01 | Phase 35 | Pending |
| HIST-02 | Phase 35 | Pending |
| HIST-03 | Phase 35 | Pending |
| HIST-04 | Phase 35 | Pending |

**Coverage:** 12 requirements total; mapped to 5 phases (31-35); 0 unmapped ✓

---
*Requirements defined: 2026-04-29*
*Last updated: 2026-05-05 after Phase 33 completion (SLASH-07 → Complete; 7/12 requirements done; Phases 34-35 remain)*
