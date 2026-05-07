# blueCode 도입 Blueprint

GSD 의 기법들을 blueCode (F# CLI agent) 에 도입하기 위한 구체적 설계 제안. blueCode 가 사용자에게서 task 를 받았을 때 자체적으로 plan 을 세우고 sub-work 로 쪼개서 실행하도록 하는 게 목표.

## 현실 확인 — blueCode vs Claude Code

| 측면 | Claude Code | blueCode |
|------|-------------|----------|
| LLM | Claude Sonnet/Opus (cloud) | Qwen 3.5 122B (local @ 8001) |
| Subagent | `Task()` tool 로 fresh 200k context spawn | **없음** (single AgentLoop) |
| Parallelism | 진짜 병렬 (네트워크 cloud) | local 서버 1개라 사실상 sequential |
| State | conversation + memory + files | files (`.planning/`) — already aligned |
| Tool | 풍부 (Read/Write/Bash/Glob/Grep/...) | `IToolExecutor` (Bash/Read/Write/...) |
| Output | freeform | **strict JSON schema** (이미 강제됨) |

가장 큰 차이: **subagent 가 없다**. GSD 의 핵심 메커니즘 중 하나는 "fresh context 로 isolate" 인데, blueCode 는 같은 LLM 서버에 다시 HTTP 호출할 뿐임. 다행히 **conversation 을 새로 시작**하면 사실상 fresh context 가 된다 (KV cache 만 잔류). 같은 효과를 얻을 수 있다.

가장 큰 장점: **JSON schema 가 이미 강제됨**. PLAN.md 의 XML 대신 JSON record 를 직접 쓸 수 있다. F# DU/record 와 native fit.

## 도입 전략 — 점진적 6 phase

### Phase A: 최소 가치 (MVP)
사용자가 "X 해줘" 하면 LLM 이 plan JSON 으로 분해하고, 그 plan 을 task 단위로 sequential 실행.

### Phase B: Goal-backward must_haves
plan 에 must_haves 추가, 각 plan 끝에 자동 verification.

### Phase C: Plan checker
plan 작성 후 execution 전, 6 dimension 검사.

### Phase D: Deviation rules
"plan 에 없는 일을 발견했을 때" 규칙 4개를 system prompt 에 박음.

### Phase E: Atomic commits
각 task 끝날 때 IToolExecutor 가 자동 git commit.

### Phase F: Wave-based parallel (optional)
여러 task 를 같은 turn 에서 병렬 LLM 호출 (현재 architecture 변경 필요).

각 phase 가 독립적으로 가치 있음. A 만 도입해도 큰 개선.

---

## Phase A — MVP 설계

### 1. 새 system prompt — `Planner` mode

`CompositionRoot.fs` 의 `defaultSystemPrompt` 와 `planSystemPromptSuffix` 옆에 새 `plannerSystemPrompt` 추가:

```fsharp
let plannerSystemPrompt = """
You are a task planner. The user gives you a single task.
Decompose it into 1–3 sub-tasks (more = degraded quality).

For each sub-task, produce:
- name (action-oriented)
- files (exact paths to modify/create)
- action (specific implementation steps; what to AVOID and WHY)
- verify (executable command to confirm completion)
- done (measurable acceptance criteria)

Return strict JSON matching this schema (NO prose):

{
  "objective": "single-sentence outcome",
  "tasks": [
    { "name": "...", "files": ["..."], "action": "...", "verify": "...", "done": "..." }
  ]
}

Rules:
- Tasks 2–3 max. If you need 5+, the task is too big — pick a smaller scope.
- Each task should take 15–60 minutes to execute.
- Vertical slices > horizontal layers (don't make "all models", "all APIs", "all UIs" — make "feature 1 end-to-end", "feature 2 end-to-end").
- If task is unclear, return {"objective": "...", "tasks": [], "needs_clarification": "..."} instead.
"""
```

### 2. Plan / Task 의 F# schema

`src/BlueCode.Core/Plan.fs` (신규 파일):

```fsharp
module BlueCode.Core.Plan

open System.Text.Json.Serialization

type PlanTask = {
    [<JsonPropertyName("name")>]    Name: string
    [<JsonPropertyName("files")>]   Files: string array
    [<JsonPropertyName("action")>]  Action: string
    [<JsonPropertyName("verify")>]  Verify: string
    [<JsonPropertyName("done")>]    Done: string
}

type PlanRequest = {
    [<JsonPropertyName("objective")>]            Objective: string
    [<JsonPropertyName("tasks")>]                Tasks: PlanTask array
    [<JsonPropertyName("needs_clarification")>]  NeedsClarification: string option
}

type PlanState =
    | NotStarted
    | InProgress of completedTasks: int
    | Completed of summaryPath: string
    | Failed of reason: string

type ActiveSession = {
    SessionId: string                 // e.g., quick-001-add-eval-flag
    SessionDir: string                // .planning/quick/001-add-eval-flag/
    Plan: PlanRequest
    State: PlanState
    Commits: string list              // accumulated commit hashes
}
```

### 3. Orchestrator — sub-mode in AgentLoop

`AgentLoop.fs` 안에 새 mode 추가하지 말고, **별도 module** 로:

`src/BlueCode.Core/PlanOrchestrator.fs`:

```fsharp
module BlueCode.Core.PlanOrchestrator

open BlueCode.Core.Plan

let private generatePlan (llm: ILlmClient) (userTask: string) (ct: CancellationToken) =
    task {
        let messages = [
            { Role = "system"; Content = plannerSystemPrompt }
            { Role = "user"; Content = userTask }
        ]
        // model: 122b (single canonical mode)
        let! response = llm.CompleteAsync messages "122b" ct
        match response with
        | Ok r ->
            // Output 은 strict JSON. 직렬화 시도
            try
                let plan = JsonSerializer.Deserialize<PlanRequest>(r.Output)
                return Ok plan
            with ex ->
                return Error (PlanParseError ex.Message)
        | Error e -> return Error e
    }

let private executeOneTask (llm: ILlmClient) (tools: IToolExecutor) (task: PlanTask) (ct: CancellationToken) =
    task {
        // executor 는 일반 AgentLoop 가 아닌, task-focused system prompt 로
        let executorPrompt = sprintf """
You are executing this single task. Use the available tools to accomplish it.

Task:
  Name: %s
  Files to modify: %s
  Action: %s
  Verify with: %s
  Done when: %s

After completing the task, run the verify command and report the result.
""" task.Name (String.concat ", " task.Files) task.Action task.Verify task.Done

        let! result = AgentLoop.runLoop llm tools executorPrompt ct
        return result
    }

let runPlanMode (llm: ILlmClient) (tools: IToolExecutor) (userTask: string) (ct: CancellationToken) =
    task {
        // 1. Generate plan
        let! planResult = generatePlan llm userTask ct
        match planResult with
        | Error e -> return Error e
        | Ok plan when plan.NeedsClarification.IsSome ->
            return Error (Clarification plan.NeedsClarification.Value)
        | Ok plan when plan.Tasks.Length = 0 ->
            return Error (EmptyPlan plan.Objective)
        | Ok plan ->
            // 2. Display plan to user, get confirmation
            displayPlan plan
            let confirmed = promptUserToConfirm ()
            if not confirmed then return Error UserAborted
            else
                // 3. Create session directory
                let sessionId = nextSessionId ()
                let sessionDir = sprintf ".planning/quick/%s-%s" sessionId (slugify plan.Objective)
                Directory.CreateDirectory sessionDir |> ignore
                writePlanFile sessionDir plan

                // 4. Execute tasks sequentially
                let mutable commits = []
                let mutable failed = false
                for i, task in plan.Tasks |> Array.indexed do
                    if not failed then
                        printfn "▶ Task %d/%d: %s" (i+1) plan.Tasks.Length task.Name
                        let! result = executeOneTask llm tools task ct
                        match result with
                        | Ok summary ->
                            // Atomic commit (Phase E 이후 자동)
                            let commitHash = commitTaskFiles task ct
                            commits <- commitHash :: commits
                        | Error e ->
                            failed <- true
                            printfn "✗ Task %d failed: %A" (i+1) e

                // 5. Write SUMMARY.md
                writeSummaryFile sessionDir plan commits
                return Ok ()
    }
```

### 4. CLI flag — 진입점

`Program.fs` 의 Argu 정의에 추가:

```fsharp
type CliArgs =
    | [<Mandatory; MainCommand; ExactlyOnce>] Prompt of string
    | Plan
    | Eval
    // ...

// dispatch
match args with
| _ when args.Contains <@ Plan @> ->
    PlanOrchestrator.runPlanMode llm tools userTask ct
| _ ->
    AgentLoop.runLoop llm tools userTask ct
```

또는 **자동 감지**: 첫 turn 에서 LLM 에게 "이 task 가 1–3개 sub-task 로 쪼개야 할 만큼 큰가?" 를 묻는 분류 step 을 두고, big task 면 plan mode 진입.

### 5. UX

```
$ blueCode --plan "add a calculator REPL command that handles +, -, *, /"

Generating plan...

PLAN
├─ Objective: Add interactive calculator command to REPL
├─ Task 1: Add Calculator module with parse and evaluate
│   files: src/BlueCode.Core/Calculator.fs
│   verify: dotnet test --filter Calculator
├─ Task 2: Wire :calc command into REPL dispatcher
│   files: src/BlueCode.Cli/Program.fs
│   verify: echo ":calc 1+2" | blueCode → "3"

Proceed? [Y/n] y

▶ Task 1/2: Add Calculator module
  ... LLM working ...
  ✓ verified, committed abc1234

▶ Task 2/2: Wire :calc command
  ... LLM working ...
  ✓ verified, committed def5678

Session: .planning/quick/001-add-calculator-repl-command/
Commits: abc1234, def5678, 9012abc (metadata)
```

### Phase A 의 핵심 trade-off

- **장점:** 사용자가 "큰 일" 을 한 번 시켜도 LLM 이 헛돌지 않고 구조적으로 실행. 각 task 는 작아서 quality degradation 없음.
- **위험:** plan 이 잘못 분해되면 더 나쁜 결과 (잘못된 분해 + 충실한 실행). → Phase C (plan-checker) 가 이걸 잡음.
- **비용:** LLM 호출이 1 → N+1 회로 증가 (plan 1번 + task N번). 122B 가 task 당 30–120s 걸리므로 사용자가 인내심 필요.

---

## Phase B — Goal-Backward must_haves

`PlanRequest` 에 must_haves 추가:

```fsharp
type Artifact = {
    Path: string
    Provides: string
    MinLines: int option
}

type KeyLink = {
    From: string
    To: string
    Via: string
    Pattern: string  // grep regex
}

type MustHaves = {
    Truths: string array        // user-observable
    Artifacts: Artifact array
    KeyLinks: KeyLink array
}

type PlanRequest = {
    // ... 이전 field
    MustHaves: MustHaves
}
```

planner system prompt 에 추가:

```
After listing tasks, derive must_haves using goal-backward analysis:
1. What must be TRUE for this objective to be achieved? (3–5 user-observable behaviors)
2. What must EXIST? (specific file paths)
3. What must be WIRED? (connections between artifacts; provide a grep regex to verify)

Example for "add JWT auth":
  truths: ["User can log in with email/password", "Invalid credentials return 401"]
  artifacts: [{path: "src/api/auth.ts", provides: "login endpoint", min_lines: 30}]
  key_links: [{from: "LoginForm.tsx", to: "/api/auth", via: "fetch in onSubmit", pattern: "fetch.*api/auth"}]
```

execution 후 자동 verifier (Phase A 의 task verify 와 별개):

```fsharp
let verifyMustHaves (plan: PlanRequest) (workingDir: string) : VerificationResult =
    let mutable failures = []
    for art in plan.MustHaves.Artifacts do
        let path = Path.Combine(workingDir, art.Path)
        if not (File.Exists path) then
            failures <- (art.Path, "MISSING") :: failures
        else
            let lines = File.ReadAllLines path
            match art.MinLines with
            | Some min when lines.Length < min ->
                failures <- (art.Path, sprintf "TOO_SHORT (%d < %d)" lines.Length min) :: failures
            | _ -> ()
            // stub pattern check
            let content = String.concat "\n" lines
            let stubPatterns = [
                @"TODO|FIXME|placeholder|not implemented"
                @"return null|return \{\}|return \[\]"
            ]
            // ...
    for link in plan.MustHaves.KeyLinks do
        let fromPath = Path.Combine(workingDir, link.From)
        if File.Exists fromPath then
            let content = File.ReadAllText fromPath
            if not (Regex.IsMatch(content, link.Pattern)) then
                failures <- (link.From, sprintf "NOT_WIRED (%s)" link.Pattern) :: failures
    // ...
```

이 verifier 의 **80%는 F# 코드** (grep + 파일 존재 + 줄 수). LLM 호출 없이 빠르게 실행. nuance 가 필요한 부분 (substantive vs stub 판단) 만 LLM 에 물어볼 수 있음.

---

## Phase C — Plan Checker

`PlanRequest` 가 만들어진 후, execution 전에 검사. 6 dimension 중 **3개는 코드, 3개는 LLM**:

```fsharp
type CheckIssue = {
    Plan: string option
    Dimension: CheckDimension
    Severity: Severity
    Description: string
    FixHint: string
}

and CheckDimension =
    | RequirementCoverage     // LLM
    | TaskCompleteness        // 코드: 빈 field 있는가
    | DependencyCorrectness   // 코드: cycle, scope
    | KeyLinksPlanned         // LLM: action 이 link 를 implement 하는가
    | ScopeSanity             // 코드: task 5+ 면 blocker
    | VerificationDerivation  // LLM: truths 가 user-observable 한가

and Severity = Blocker | Warning | Info

let checkPlan (plan: PlanRequest) (llm: ILlmClient) : Task<CheckIssue list> = task {
    let mutable issues = []

    // 코드 기반 검사
    if plan.Tasks.Length > 4 then
        issues <- {
            Plan = None
            Dimension = ScopeSanity
            Severity = if plan.Tasks.Length >= 5 then Blocker else Warning
            Description = sprintf "Plan has %d tasks (target: 2–3)" plan.Tasks.Length
            FixHint = "Split into multiple plans"
        } :: issues

    for i, t in plan.Tasks |> Array.indexed do
        if String.IsNullOrWhiteSpace t.Verify then
            issues <- {
                Plan = None
                Dimension = TaskCompleteness
                Severity = Blocker
                Description = sprintf "Task %d (%s) missing verify" (i+1) t.Name
                FixHint = "Add executable command to confirm completion"
            } :: issues
        // 비슷하게 files, action, done 체크

    // LLM 기반 검사 (아직 task 풍부함)
    let! llmIssues = askLlmAboutPlan plan llm
    issues <- llmIssues @ issues

    return issues
}
```

issues 가 blocker 1개 이상이면 → planner 다시 호출 (revision mode), 최대 3 iter:

```fsharp
let rec planWithCheckLoop userTask iter llm =
    task {
        let! plan = generatePlan llm userTask ct
        let! issues = checkPlan plan llm
        let blockers = issues |> List.filter (fun i -> i.Severity = Blocker)
        if blockers.IsEmpty then return Ok plan
        elif iter >= 3 then
            promptUserAboutBlockers blockers
            // user 가 force | abandon | guidance
        else
            let! revisedPlan = revisePlan plan blockers llm
            // 다시 check
            ...
    }
```

---

## Phase D — Deviation Rules

executor system prompt 에 명시적으로 박음:

```fsharp
let executorSystemPromptBase = """
You are executing a sub-task in a larger plan. Use the available tools.

DEVIATION RULES — Apply automatically while executing:
1. Found a bug while implementing? → Fix it. Note in your final report.
2. Discovered missing critical functionality (security, correctness)? → Add it. Note it.
3. Hit a blocker that prevents this task? → Fix the blocker first. Note it.
4. Architectural change required (major structural shift)? → STOP. Return an explanation. Do NOT modify files.

Rules 1–3 are automatic. Rule 4 requires explicit pause.

AUTHENTICATION GATES:
If a CLI/API call returns "Not authenticated", "401", "403", "Please run X login":
  → STOP this task. Return: AUTH_REQUIRED with the exact command (e.g., "vercel login") and verification step.
  → This is NOT a failure. The user will authenticate and you'll be re-spawned.

OUTPUT FORMAT (after completing or stopping):
{
  "status": "completed" | "auth_required" | "architectural_decision_needed",
  "tasks_completed": [...],
  "deviations": [{ "rule": 1, "description": "..." }, ...],
  "verify_output": "...",
  "stop_reason": "..." // only when status != completed
}
"""
```

이 정책을 system prompt 에 박는 것 = LLM 이 self-correct 할 때 일관된 방향성을 갖게 함.

---

## Phase E — Atomic Commits

`IToolExecutor` 가 git commit 가능. 새 helper:

```fsharp
module GitCommit

let commitTask (taskName: string) (taskIndex: int) (sessionId: string) (modifiedFiles: string list) (tools: IToolExecutor) (ct: CancellationToken) : Task<string> =
    task {
        // 1. git status
        let! statusOut = tools.RunCommand "git" ["status"; "--short"] ct

        // 2. Stage individual files (NEVER git add . / -A)
        for file in modifiedFiles do
            let! _ = tools.RunCommand "git" ["add"; file] ct
            ()

        // 3. Determine commit type from heuristic
        let commitType = inferCommitType taskName modifiedFiles
        let msg = sprintf "%s(%s-%02d): %s" commitType sessionId taskIndex taskName

        // 4. Commit
        let! _ = tools.RunCommand "git" ["commit"; "-m"; msg] ct

        // 5. Get hash
        let! hashResult = tools.RunCommand "git" ["rev-parse"; "--short"; "HEAD"] ct
        return hashResult.Stdout.Trim()
    }
```

executor 가 task 끝낼 때마다 commitTask 호출. 실패 시 rollback (`git reset HEAD~1` 안 됨, 더 안전한 건 stash 후 사용자 알림).

**중요:** blueCode 의 CLAUDE.md 에 이미 박힌 규칙 — `git add .` / `-A` 절대 안 됨. system prompt 에 이 제약을 다시 명시.

---

## Phase F — Wave-based Parallel (선택적, 나중)

현재 single-LLM-server 구조에서는 진짜 병렬은 어려움. mlx_lm.server 가 단일 스레드로 model 을 host 하므로 동시 HTTP request 들이 queue 됨.

만약 도입한다면:
1. plan 에 `wave: int` 와 `depends_on: string[]` 추가 (이미 GSD schema 와 동일)
2. 같은 wave 의 task 들을 `Task.WhenAll` 로 동시 LLM 호출
3. 단, **performance gain 없음** (server 가 sequential 처리). 의미는 "사용자에게 병렬로 보여주기" UX 일 뿐.

진정한 병렬을 원하면 두 번째 LLM port 를 띄워야 함 (8000 의 35B 같은 standby model 활용 가능). 이건 성능 트레이드오프 검토 후.

→ **권장:** Phase F 는 도입 안 해도 무방. 사용자 체감은 sequential 로 충분.

---

## 구현 우선순위 — 추천 도입 순서

**MVP (1주):** Phase A
- 새 system prompt
- F# Plan/Task schema
- PlanOrchestrator module
- `--plan` CLI flag
- 사용자 confirm 후 sequential 실행

**Robustness (1주):** Phase D + E
- Deviation rules 를 system prompt 에 박음
- Atomic commit per task
- 이 둘만 추가해도 quality 가 크게 향상

**Quality gate (1주):** Phase B + C
- must_haves 추가, F# 기반 verifier (grep)
- Plan checker 의 코드 부분 (scope, completeness, dependency cycle)
- LLM 기반 검사는 optional toggle

**Skip:** Phase F (parallel) — 현재 architecture 에서 ROI 낮음

---

## 통합 시 blueCode 의 새 mental model

기존:
```
사용자: "X 해줘"
  → AgentLoop (LLM ↔ tool ↔ LLM ↔ ...) 한 conversation
  → 답변 또는 변경된 파일들
```

도입 후:
```
사용자: "X 해줘"
  → [classifier turn] LLM 이 "이거 plan 필요한가?" 판단
       ├─ NO → 기존 AgentLoop (단일 turn, 작은 task)
       └─ YES → PlanOrchestrator 진입
                  ├─ Planner 호출 → Plan JSON
                  ├─ (옵션) Plan checker → revise loop
                  ├─ 사용자 confirm
                  ├─ for each task:
                  │     Executor 호출 (fresh conversation)
                  │     → tool 사용해 task 완료
                  │     → atomic commit
                  ├─ (옵션) Verifier → goal-backward check
                  └─ SUMMARY 작성, STATE 업데이트
```

**LLM 호출 횟수:** 작은 task 1 → plan task 4–10. 그러나 각 호출이 더 작고 명확해서 retry 가 줄어듦.

**디스크 사용:** session 마다 directory 생성 (`.planning/quick/NNN-slug/`). PLAN.md, SUMMARY.md 만. 100bytes ~ 5KB 수준.

**Git history:** task 당 1 commit + plan-metadata commit. 사용자가 한 번 invoke 하면 N+1 commit 생김. bisect/blame 가치 큼.

---

## 핵심 위험과 완화

| 위험 | 완화 |
|------|------|
| LLM 이 task 를 잘못 분해 (너무 크게 / 너무 잘게) | Plan checker 의 scope dimension. 사용자 confirm step. |
| Task 가 plan 외 파일 수정 (deviation rule 1–3 으로 자동 fix 했으나 bug) | Atomic commit 으로 revertable. SUMMARY 에 deviation 명시. |
| LLM 이 verify 명령을 안 돌림 / 거짓 보고 | F# verifier 가 grep 으로 별도 확인 (Phase B). |
| Plan 이 아예 잘못됐는데 사용자가 confirm | iter 1 후 첫 task 가 실패하면 plan 자체를 의심. 자동 abort + 사용자 통지. |
| Local LLM 의 strict JSON 파싱 실패 (앞에서 본 thinking-mode trap) | 이미 `enable_thinking=false` 로 mitigated. JSON schema validation 추가. |

---

## CLAUDE.md 와의 정렬

이 도입은 blueCode 의 기존 규칙들과 충돌 없음:

- ✅ Core purity: PlanOrchestrator 는 Core 에, IO 는 ILlmClient/IToolExecutor 통해
- ✅ task {} 사용: 이미 위 코드 sketch 가 task CE
- ✅ Atomic commit: 강화됨
- ✅ Stream separation: 사용자 표시는 printfn (stdout), Serilog (stderr)
- ✅ `git add -A` 금지: PlanOrchestrator 가 individual file staging 강제
- ✅ Single-model 122B: Planner/Executor/Verifier 모두 8001 로 호출

새로 필요한 건:
- 새 모듈 `BlueCode.Core.Plan` (schema)
- 새 모듈 `BlueCode.Core.PlanOrchestrator` (orchestration logic)
- 새 모듈 `BlueCode.Core.PlanVerifier` (Phase B 부터, grep-based)
- 새 모듈 `BlueCode.Core.PlanChecker` (Phase C 부터)
- 새 system prompts in `CompositionRoot.fs`
- `--plan` flag in `Program.fs`

---

## 다음 step (사용자 결정 사항)

1. 어느 Phase 부터? 추천: A → D + E → B + C
2. classifier turn 자동 vs `--plan` 명시 옵트인? 추천: 처음엔 명시 옵트인, 익숙해지면 자동
3. confirmation step 항상 vs `--yes` 로 skip? 추천: 항상 confirm (atomic commit 이 N+1 개 생기는 동작이라 user awareness 필요)
4. Plan checker 의 LLM 호출 (Phase C) 처음부터? 추천: code-only check 만 먼저 도입 (cycle/scope/completeness), LLM check 는 나중

이 문서들이 새 GSD-style milestone (예: v2.6 — "self-planning agent") 을 set up 하는 input 이 될 수 있어. 만약 그렇게 진행한다면 `/gsd:new-milestone` 으로 시작하면 자연스러움.
