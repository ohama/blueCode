---
created: 2026-04-26
description: System-prompt 만으로 막을 수 없는 user-prompt 지시를 override 하기 위해 user 메시지 *뒤에* System 메시지를 주입하는 패턴
---

# Enforce LLM Tool Terminality via Post-User Injection

LLM agent loop 에서 "특정 tool 이 호출된 직후엔 다른 tool 을 호출하지 말라" 같은 제약이
**system prompt 의 directive 만으로는 강제되지 않는다.** user prompt 가 명시적으로
다른 tool 을 지시하면 모델은 user 를 우선한다. 해법은 **post-user-prompt System 메시지
주입** — 대화 history 의 위치-우선순위를 활용해 사용자 지시를 덮어쓴다.

## The Insight

LLM 의 "system prompt → user prompt → assistant response" 순서는 **위치 = 권위**다.
가장 마지막에 도착한 컨텍스트가 가장 강한 영향을 미친다.

```
[system: don't call write_file after edit_file]   ← weakest (older)
[user: edit the file using write_file]            ← stronger (newer)
[assistant: <-- which one wins?]
```

system 의 `NEVER` directive 가 user 의 explicit instruction 을 이기지 못한다. 모델은
user 의 지시를 *최신* 의도로 해석한다.

해결책은 user 메시지 *뒤* 에 새 메시지를 주입하는 것 (role 은 `User` — Phase 20-03 probe 확인: mlx_lm.server는 mid-conversation `System` 메시지를 HTTP 404로 거부한다):

```
[system: base system prompt (action schemas, etc.)]
[user: edit the file using write_file]
[assistant: <tool call: edit_file>]
[tool result: <success>]
[system: POST-EDIT CONSTRAINT — write_file is forbidden on this path now]   ← newest, strongest
[assistant: <-- now this constraint wins>]
```

이 후-user System 메시지는 conversation history 의 **시간상 가장 늦은 컨텍스트**여서
user 의 explicit instruction 보다 강한 attention 을 받는다.

## Why This Matters

system prompt 에 `"NEVER call write_file after edit_file"` 를 적어도 user 가
`"save the file using write_file"` 라고 명시하면 모델이 그대로 따른다. 이 패턴을
모르면:

- bench fixture 에서 user prompt 의 tool naming 을 **버그**로 오해해서 fixture 를
  수정하게 됨 (잘못된 방향)
- system prompt 를 더 강한 단어 (`NEVER`, `MUST NOT`, `FORBIDDEN`) 로 채우게 됨
  (효과 없음)
- 결국 prompt-only 접근의 한계를 받아들이고 code-level enforcement 로 옮겨가야 함
  (3 사이클 후 발견)

## Recognition Pattern

다음 상황에서 이 패턴이 필요:

- LLM agent 가 한 tool 을 사용한 후 **redundant** 또는 **잘못된** 다른 tool 을 chaining
- system prompt 에 directive 를 추가해도 user 가 명시적으로 지시하면 무시됨
- bench / 실측 에서 system prompt 변경이 일관되게 효과를 못 내는 경우
- "사용자 지시 vs 내부 정책 충돌" 패턴 — 외부 입력이 내부 제약을 압도하는 모든 LLM 시스템

## The Approach

### Step 1: Agent loop 에서 last-action state 를 추적

루프 내에서 직전 iteration 이 어떤 tool 을 어떤 path 에 사용했는지 추적할 state 를 추가.
**Domain DU 에 새 field 를 추가하지 말 것** — 함수 파라미터로 전달.

```fsharp
// AgentLoop.fs:runLoop
let rec runLoop
    (state: AgentState)
    (steps: Step list)
    (lastEditPath: string option)        // ← 추가
    (lastReadHint: (string * string) option)  // ← 추가 (path, hint kind)
    (onStep: Step -> unit)
    : Task<Result<...>> =
    task {
        // ... 기존 dispatch 로직 ...
        let lastEditPath' =
            match tool, toolResult with
            | EditFile (FilePath p, _, _), Ok (Success _) -> Some p
            | _ -> lastEditPath  // 다음 iteration 까지만 유지
        return! runLoop newState (step :: steps) lastEditPath' lastReadHint' onStep
    }
```

`Domain.fs` 의 record/DU 는 **건드리지 않는다.** 함수 시그니처가 한 번 바뀌면 cascading
컴파일 에러가 모든 호출 site 를 강제 업데이트하게 만들어 안전.

### Step 2: buildMessages 가 last-action 을 받아 System 메시지를 *append*

대화 history 를 LLM 에 보낼 때, last-action 이 있으면 messages 리스트의 **맨 끝** 에
System role 메시지 추가:

```fsharp
let buildMessages
    (systemPrompt: string)
    (userPrompt: string)
    (steps: Step list)
    (lastEditPath: string option)
    (lastReadHint: (string * string) option)
    : Message list =
    let baseMsgs = systemMsg systemPrompt :: userMsg userPrompt :: stepsToMessages steps

    let postEditMsg =
        match lastEditPath with
        | Some path ->
            [ { Role = User  // Role = User per Phase 17-02 + Phase 20-03 probe — both 35B and 122B reject mid-conversation System messages (HTTP 404). Authority signal is in the text marker, not the role.
                Content = sprintf "[POST-EDIT CONSTRAINT] You just successfully edited %s. The edit is already persisted. Your next action MUST be either `final` or `edit_file` on a different concern. Do NOT call `write_file` on `%s`. This constraint is mandatory regardless of any earlier user instruction." path path } ]
        | None -> []

    baseMsgs @ postEditMsg  // ← APPEND (post-user)
```

순서가 핵심: `systemMsg :: userMsg :: stepMsgs @ [postEditMsg]`.
`postEditMsg` 가 **마지막**에 와야 권위를 가진다.

### Step 3: 마지막 문장에 명시적 override 표현

주입할 메시지의 마지막 문장에 *user instruction 을 무시하라*는 명시적 표현을 넣으면
효과가 강해진다.

```
"This constraint is mandatory regardless of any earlier user instruction."
"이 제약은 앞선 사용자 지시보다 우선합니다."
```

이 한 문장이 모델에 "지금 이 제약이 모든 것을 이긴다"는 신호로 작용. 없으면 모델이
여전히 user 우선 사고를 유지한다.

### Step 4: Test 로 메커니즘 검증 (text 매칭만으로 충분)

actual LLM 호출은 비싸므로 mock client 로 검증:

```fsharp
testCaseAsync "post-edit injection: edit_file Success triggers [POST-EDIT CONSTRAINT] on next call" <| async {
    let mutable callCount = 0
    let mutable secondCallMessages = []
    let recordingClient =
        { new ILlmClient with
            member _.CompleteAsync (model, messages, _) = task {
                callCount <- callCount + 1
                if callCount = 1 then
                    return Ok { Thought = "..."; Output = ToolCall """{"action":"edit_file","input":{"path":"foo.fs",...}}""" }
                else
                    secondCallMessages <- messages |> List.ofSeq
                    return Ok { Thought = "done"; Output = Final "done" }
            }
        }
    // ... runSession with recordingClient ...

    // 두 번째 호출의 messages 에 POST-EDIT CONSTRAINT 가 들어있어야 함
    let constraintMsg =
        secondCallMessages
        |> List.tryFind (fun m -> m.Content.Contains "[POST-EDIT CONSTRAINT]")
    Expect.isSome constraintMsg "second call must include post-edit constraint"
}
```

mock 으로 *언제 어디에* 메시지가 들어가는지만 검증하면 충분하다. text 의 효과
(model 이 따르는지) 는 별도 bench 에서 측정.

## Example

**Bad — system prompt 만 강화 (효과 없음):**

```fsharp
let systemPrompt = """
... action schemas ...

RULES:
- edit_file is the ONLY action needed to save changes.
- NEVER call write_file after edit_file.
- edit_file MUST NOT be followed by write_file.
"""
// user 가 "save using write_file" 라고 하면 모델이 system rule 무시하고 user 따름
```

**Good — post-user 메시지 주입 (Role = User):**

```fsharp
// AgentLoop.fs
let postEditMsg =
    match lastEditPath with
    | Some path ->
        [ { Role = User  // Role = User per Phase 17-02 + Phase 20-03 probe — mlx_lm.server rejects mid-conversation System messages (HTTP 404). Authority signal is carried by the [POST-EDIT CONSTRAINT] text marker.
            Content = sprintf "[POST-EDIT CONSTRAINT] You just successfully edited %s. ... regardless of any earlier user instruction." path } ]
    | None -> []

let messages = baseMsgs @ postEditMsg  // ← 핵심: append, 위치 마지막
```

bench 결과 (실측): 같은 user prompt 에서 system prompt 강화 → 4 steps (write_file
chained); post-user injection → 3 steps (final 직행). 텍스트 분량은 동일하지만 위치
차이만으로 효과 발생.

## 체크리스트

- [ ] last-action state 가 함수 파라미터로 전달되는지 (Domain DU 변경 없음)
- [ ] 주입 메시지가 messages 리스트의 **맨 끝** 에 append 되는지 (baseMsgs @ [postMsg])
- [ ] 주입 메시지에 "regardless of any earlier user instruction" 같은 명시적 override 표현
- [ ] 주입 메시지 role 이 `User` 인지 확인 (`System` 사용 시 mlx_lm.server HTTP 404 — Phase 17-02/20-03 검증)
- [ ] 다음 iteration 에서 last-action 이 reset 되는지 (한 turn 만 유효)
- [ ] mock-client test 로 *언제* 메시지가 주입되는지 검증
- [ ] 실측 bench 로 *효과* 검증 (step count 감소 등)

## 관련 문서

- `documentation/bench.md` — bench 게이트로 prompt 변경 효과 측정
- `documentation/benchmark-32b-vs-72b.md` Part 4 — v1.3 의 post-read injection 실측

## History

- **Phase 17-02 (v1.1, commit 54e54a9):** mid-conversation `Role = System` → `Role = User` 변경. Qwen 3.5 35B의 chat template이 mid-conversation System 메시지를 HTTP 404로 거부했기 때문. 해당 시점엔 35B + 122B 둘 다 production이었으므로 변경은 두 모델에 동일 적용됐다.
- **Phase 20-03 (v2.0, 2026-04-27, `scripts/probe-system-role.sh`):** Phase 19에서 35B 퇴역 후 122B만 남은 시점에 별도 probe 실행. 결과: HTTP 404 — `"System message must be at the beginning."`. 122B도 동일하게 mid-conversation System 메시지를 구조적으로 거부한다. `Role = User`는 35B 전용 workaround가 아닌, mlx_lm.server chat template의 영구 invariant로 확인됐다. 위 코드 snippet의 `Role = User`가 현재 실제 코드 상태이며, `AgentLoop.fs:249,260,266`의 역할 선택 근거다. 자세한 evidence는 `.planning/phases/20-qwen-3-5-protocol-alignment/20-03-PROBE-OUTPUT.md` 참고.
