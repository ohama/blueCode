---
created: 2026-04-26
description: tool 응답에 fixed-format 헤더를 prepend 할 때, 본문 substring 테스트가 헤더 단어와 충돌하지 않도록 `\n` 으로 anchor 하는 패턴
---

# Anchor Tool Output Header Tests with Newlines

tool output 의 첫 줄에 metadata 헤더 (예: `[file: foo.fs, lines 1-10 of 50, truncated]`) 를
prepend 하면, 헤더의 짧은 단어 (`truncated`, `lines`, `file`) 가 본문의 평범한
substring (예: `"a"`, `"e"`, 단일 문자 라인) 과 **substring match 를 통해 충돌**한다.
본문이 들어있는지 검증하려고 `Expect.stringContains "a"` 같은 짧은 substring 을 쓰면
헤더의 `truncated` 가 매칭되어 false positive 가 발생한다.

## The Insight

**`Contains "<짧은 substring>"` 은 헤더의 임의의 위치를 매칭한다.** 본문에 단 한 글자
`"a"` 만 있어야 하는 fixture 라도, 헤더에 `truncated` 가 있으면 `Contains "a"` 는
true 다. 이는 헤더가 prepend 됐기 때문이지 본문에 `"a"` 가 들어가서가 아니다.

해결책은 본문 substring 을 **양쪽 newline 으로 둘러싸기**: `Contains "\na\n"` — 헤더에
`\n` 직후 `a` 가 직접 나올 일은 거의 없으므로 충돌 안전.

```
헤더:  [file: foo.fs, lines 1-3 of 5, truncated]\n
본문:  a\nb\nc\n
전체 응답: "[file: foo.fs, lines 1-3 of 5, truncated]\na\nb\nc\n"

❌ Contains "a"     — 매칭됨 (truncated 의 'a' 또는 본문의 'a')
✅ Contains "\na\n" — 본문의 'a\n' 만 매칭
```

## Why This Matters

이 collision 을 인지 못 하면:

- `out-of-range` 케이스에서 본문이 비어있어야 하는데 `Contains "a"` 가 true → 본문이
  비었음을 검증 못 함 → out-of-range 가 잘못 동작해도 test 가 PASS
- `truncated` 라는 단어가 헤더 status 에 들어가는 한 모든 짧은 char substring 검증이
  거짓을 잡지 못함

본 프로젝트에서 v1.2 Phase 9 plan 01 이 이 문제로 brittle test 를 만들었다가 fix:
> "Test substring assertions for line content must anchor with `\n` to avoid collision
> with header words (e.g. `truncated` contains `a`, `lines` contains `e`) — generalizable
> pattern for any future tool that prepends a fixed-format header"

이 패턴은 v1.3 Phase 11 의 `[POST-READ HINT]` 시스템에서도 재사용됨.

## Recognition Pattern

다음 상황에서 이 패턴 적용:

- tool 응답에 fixed-format 헤더가 prepend 되는 모든 케이스
- 헤더가 영어 단어를 포함하고, 본문이 짧은 텍스트일 때
- "본문에 X 가 없는지" 를 검증해야 하는 negative test (out-of-range, empty result 등)
- 헤더 단어 중 흔한 알파벳 문자 (a, e, i 등) 가 들어있는 케이스 — `truncated`, `lines`,
  `error`, `success`, `complete` 등 거의 모든 영어 status 단어

## The Approach

### Step 1: 본문 substring 검증은 항상 `\n` anchor

```fsharp
// ❌ Bad — 헤더와 충돌 위험
Expect.isFalse (content.Contains "a") "no body content (line 'a')"

// ✅ Good — newline 양쪽으로 anchor
Expect.isFalse (content.Contains "\na\n") "no body content (line 'a')"
```

본문이 line-oriented 이고 각 line 끝에 `\n` 이 보장되면 `\nLINE\n` 이 안전.

### Step 2: 헤더 substring 검증은 그대로 OK

헤더의 *존재* 를 검증하는 건 충돌 안 됨 (헤더 자체가 substring 이므로):

```fsharp
Expect.stringContains content "[file: foo.fs, lines 1-3 of 5, truncated]" "header present"
// 본문이 무엇이든 헤더는 첫 줄이라 정확히 매칭됨
```

핵심은 *본문 검증* 만 anchor 가 필요하다는 것.

### Step 3: 첫 줄 / 끝 줄 본문에는 부분 anchor

본문의 *첫* line 은 앞에 `\n` 이 없으므로 `\n` 한쪽만 anchor:

```fsharp
// 본문이 "a\nb\nc\n" 이면 첫 줄 'a' 는 \n 앞에만 있음
Expect.stringContains content "\na" "starts with 'a' line"   // 헤더 직후
```

또는 더 robust 하게 정확한 prefix 매칭:

```fsharp
let bodyStart = content.IndexOf '\n' + 1   // 헤더 다음 첫 줄 시작
let body = content.Substring bodyStart
Expect.equal body "a\nb\nc\n" "body matches"
```

### Step 4: 가능하면 line 단위로 비교

substring 검증이 brittle 한 케이스에서는 split 후 line list 비교가 가장 견고:

```fsharp
let lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> Array.toList
Expect.equal lines.[0] "[file: foo.fs, lines 1-3 of 5, truncated]" "header"
Expect.equal lines.[1..] [ "a"; "b"; "c" ] "body lines"
```

이 방식은 헤더와 본문이 line boundary 로 명확히 분리됨을 가정하므로 헤더 format 이
single-line 일 때 가장 깔끔.

## Example

본 프로젝트 `tests/BlueCode.Tests/FileToolsTests.fs` 의 TOOL-08 out-of-range test
실제 코드:

```fsharp
testCase "TOOL-08-bench: dispatcher Some(s, s+99) output triggers out-of-range header (T6 production trace)"
<| fun () ->
    let root = newFixture ()
    try
        File.WriteAllText(Path.Combine(root, "small.fs"), "a\nb\nc\n")  // 3 lines
        let exe = create root
        let result = exec exe (ReadFile(FilePath "small.fs", Some(2001, 2100)))
        match result with
        | Ok(Success content) ->
            // 헤더 검증 — substring 그대로 OK
            Expect.stringContains
                content
                "[file: small.fs, lines 2001-2100 of 3, out-of-range]"
                "header preserves raw 2001-2100 range and shows totalLines=3"

            // 본문 부재 검증 — \n 으로 anchor
            Expect.isFalse (content.Contains "\na\n") "no body content (line 'a')"
            Expect.isFalse (content.Contains "\nb\n") "no body content (line 'b')"
            Expect.isFalse (content.Contains "\nc\n") "no body content (line 'c')"
        | other -> failtestf "expected Success, got %A" other
    finally
        cleanup root
```

`Contains "a"` 면 헤더의 `truncated` 가 매칭되거나 (지금은 `out-of-range` 이지만)
`lines` 의 'l-i-n-e-s' 중 'e' 가 매칭되어 false positive. `\na\n` 으로 anchor 하면
본문 line 'a' 만 매칭.

## 체크리스트

- [ ] 본문 substring 검증마다 `\n` anchor 가 적용됐는가
- [ ] 헤더의 영어 status 단어가 본문 charset 과 겹칠 수 있는지 검토됐는가
- [ ] 본문이 비어있어야 하는 negative test 가 헤더 단어 매칭으로 false positive 잡고 있지 않은가
- [ ] line 단위 비교가 더 robust 한 케이스인 경우 split-and-compare 로 변경했는가
- [ ] 헤더 format 변경 시 모든 본문 검증이 깨지지 않는지 확인 (heading word 가 새로 들어가면 충돌 신규 가능)

## 관련 문서

- `documentation/benchmark-32b-vs-72b.md` — TOOL-08 헤더 format
- v1.2 Phase 9 plan 01 의 SUMMARY (archived) — 이 패턴이 처음 발견된 fix 이력
