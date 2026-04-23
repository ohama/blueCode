---
created: 2026-04-23
description: Expecto 병렬 실행이 Console.SetOut/SetError를 덮어써 발생하는 플레이키 테스트를 testSequenced로 직렬화하는 법
---

# Expecto 병렬 테스트에서 Console.SetOut 충돌 피하기

`Console.SetOut`으로 stdout을 캡처하는 테스트는 **반드시** `testSequenced`로 감싼다. 안 그러면 플레이키.

## The Insight

Expecto는 **`testList`의 항목을 기본적으로 병렬 실행**한다. 그런데 `Console.Out`/`Console.Error`는 **프로세스 전역 정적 상태**다. 테스트 A가 `Console.SetOut(writer_A)`를 부르고 동시에 테스트 B가 `Console.SetOut(writer_B)`를 부르면, **둘이 서로의 writer를 덮어써서 stdout이 섞인다**.

증상은 두 가지 중 하나:
- 테스트 A의 기대 출력이 테스트 B의 writer에 들어감 → A 실패
- `System.ObjectDisposedException: Cannot access a closed TextWriter` → B가 writer_B를 dispose한 뒤 A가 여전히 거기에 쓰려 함

개별 실행(`--filter`)은 통과하는데 풀 스위트는 플레이키하게 실패한다. 이게 전형적 신호.

## Why This Matters

CLI 도구를 만들면 stdout에 뭐가 출력되는지 검증하는 테스트를 쓴다. `Console.SetOut`으로 `StringWriter` 붙이고 함수 실행 뒤 writer 내용을 assert 한다. 이 패턴은 기본적이고 널리 쓰인다 — **Expecto 병렬 기본 설정이 이 패턴과 근본적으로 충돌한다는 사실**을 모르면 하루를 잃는다.

같은 프로젝트에서 이 함정을 **여러 명이 독립적으로 밟았다** — 한 번 배우면 반복 안 하니 early-docs 가치가 크다.

## Recognition Pattern

다음이 섞여 나오면 이 문제:

- `Console.SetOut`, `Console.SetError`, `new StringWriter()` 가 테스트 코드에 등장
- `dotnet test` 풀 스위트에서 특정 테스트 플레이키 (돌릴 때마다 통과/실패 바뀜)
- `dotnet test --filter "FullyQualifiedName~SpecificTest"` 로 단일 실행하면 항상 통과
- 에러 메시지: `ObjectDisposedException: Cannot access a closed TextWriter` 또는 기대 문자열이 `""`
- 테스트가 몇 개 추가될수록 플레이키 비율 상승

## The Approach

**Console 전역 상태를 건드리는 testList를 직렬화**한다. 두 레벨의 옵션:

1. **세밀**: 충돌하는 테스트만 `testSequenced`로 감싼다 — 다른 테스트는 여전히 병렬.
2. **거친**: 모듈 전체 `testList`를 `testSequenced`로 감싼다 — 한 모듈은 전부 직렬.

거친 쪽이 안전하고 파악하기 쉽다. CLI 렌더링/로그 테스트처럼 **모듈 전체가 Console을 건드리면** 거친 쪽을 쓴다.

### Step 1: Console 전역을 건드리는 테스트 모듈 식별

```bash
grep -rln "Console.SetOut\|Console.SetError" tests/
```

나온 파일들이 후보.

### Step 2: 해당 모듈의 `[<Tests>]` 선언을 `testSequenced`로 감싼다

Before:
```fsharp
[<Tests>]
let tests =
    testList "Repl" [
        testCase "compact prints step line" <| fun _ ->
            use sw = new StringWriter()
            Console.SetOut(sw)
            runSingleTurn ...
            Expect.stringContains (sw.ToString()) "ms]" "step line"

        testCase "verbose prints multi-line" <| fun _ ->
            use sw = new StringWriter()
            Console.SetOut(sw)
            ...
    ]
```

After:
```fsharp
[<Tests>]
let tests =
    testSequenced <| testList "Repl" [     // 이 한 줄 추가
        testCase "compact prints step line" <| fun _ ->
            ...
        testCase "verbose prints multi-line" <| fun _ ->
            ...
    ]
```

`testSequenced`는 Expecto 내장 함수 — `open Expecto` 되어있으면 바로 쓴다.

### Step 3: `testCaseAsync`를 쓰고 있으면 `testCase`로 내린다 (선택)

`testCaseAsync` + `Console.SetOut`은 async 경계 때문에 `AsyncLocal`이 아닌 정적 상태를 다룰 때 추가적으로 깨지기 쉽다. 다음처럼 동기 버전으로 내리는 게 안전:

Before:
```fsharp
testCaseAsync "async version" <| async {
    use sw = new StringWriter()
    Console.SetOut(sw)
    do! runSingleTurn ... |> Async.AwaitTask
    Expect.stringContains (sw.ToString()) "..." "..."
}
```

After:
```fsharp
testCase "sync version" <| fun _ ->
    use sw = new StringWriter()
    Console.SetOut(sw)
    (runSingleTurn ...).GetAwaiter().GetResult()
    Expect.stringContains (sw.ToString()) "..." "..."
```

`testSequenced`로 이미 병렬성을 제거했으므로 async의 이점(병렬 대기)도 사라졌다 — 동기로 내려도 성능 손실 없음.

### Step 4: 원상복구 확인

```fsharp
testCase "..." <| fun _ ->
    let original = Console.Out   // 저장
    use sw = new StringWriter()
    Console.SetOut(sw)
    try
        // 테스트 본문
        ...
    finally
        Console.SetOut(original)  // 복구
```

`testSequenced`로 직렬화됐어도 **다른 모듈의 테스트**가 Console을 건드리면 같은 문제. 가능하면 원복까지 추가.

## Example

F# Expecto 실제 패턴:

```fsharp
module BlueCode.Tests.ReplTests

open System
open System.IO
open Expecto

[<Tests>]
let tests =
    // testSequenced — Console.SetOut 전역 상태 때문에 이 모듈 전체 직렬화
    testSequenced <| testList "Repl" [

        testCase "onStep prints per-step line with ms marker" <| fun _ ->
            let original = Console.Out
            use sw = new StringWriter()
            Console.SetOut(sw)
            try
                // stub ILlmClient + IToolExecutor ... runSingleTurn 호출
                let output = sw.ToString()
                let msLines =
                    output.Split('\n')
                    |> Array.filter (fun l -> l.Contains("ms]"))
                Expect.isTrue (msLines.Length >= 2) "at least 2 step lines with ms] marker"
            finally
                Console.SetOut(original)

        testCase "verbose mode produces multi-line step output" <| fun _ ->
            ...
    ]
```

### Router/EntryPoint 모듈은 제외

`[<EntryPoint>]`가 있는 테스트 파일(`RouterTests.fs` 등)은 Expecto 메인 진입 지점이다. `testSequenced`를 모듈 최상위 `tests` 변수에 적용하는 것은 무관 — 어차피 이 자체가 rootTests 모음이다. 혼동하지 말 것.

## 체크리스트

Expecto 프로젝트에 Console 관련 테스트 추가할 때:

- [ ] `grep -rln "Console.SetOut\|Console.SetError" tests/` 실행
- [ ] 해당 모듈의 `[<Tests>] let tests = testList` → `testSequenced <| testList` 로 변경
- [ ] `testCaseAsync` 쓰고 있다면 동기 `testCase`로 고려
- [ ] 테스트 본문에 `original = Console.Out` 저장 + `finally Console.SetOut(original)` 복구 추가
- [ ] `dotnet test` 3회 연속 실행 — 결과 동일한지 (플레이키 사라졌는지) 확인

## 한 가지 더 — "Tests auto-discovery 안 됨" 문제와 별개

이 문제와 별도로, 프로젝트에 따라 Expecto가 `[<Tests>]` 속성 auto-discovery를 쓰지 않고 **명시적 `rootTests` 리스트**를 조립하는 경우가 있다. 그 경우 새 테스트 모듈을 rootTests에도 등록해야 실행된다. 두 문제가 함께 나타나기 쉬우니(둘 다 "테스트를 추가했는데 풀 스위트에서 이상함") 동시에 체크한다.

## 관련 문서

- (external) Expecto README — `testSequenced` 섹션
- (external) Microsoft Docs — `Console.SetOut` thread-safety 주의사항
