# 게임플레이 확장 2단계 (화음 적) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 두 색의 도형이 막대로 이어진 화음 적을 넣는다. 순서 무관하게 한쪽씩 두 번 맞혀야 죽고, 죽을 때 두 색의 처치음이 같은 박의 화음으로 울린다.

**Architecture:** 1단계에서 분리한 순수 규칙 클래스(`Assets/Scripts/Gameplay`)를 확장한다. 화음 적은 새 클래스가 아니라 `EnemyState`의 두 번째 색 필드로 표현하고, 접촉 판정(`PulseResolver`)과 스폰(`SpawnDirector`)에 규칙을 더한 뒤 `ChordPulsePrototype`에서 모양과 소리를 연결한다.

**Tech Stack:** Unity (C#), NUnit EditMode 테스트, FMOD (변경 없음).

**Spec:** `Docs/superpowers/specs/2026-11-27-gameplay-depth-design.md` 5.4 "화음 적", 5.5 (구현자는 이 문서도 읽는다)

## Global Constraints

- FMOD 프로젝트와 뱅크, `Assets/Scripts/Audio`, `Assets/Scripts/Visual`, `SoundManager`는 건드리지 않는다. 혼합 화음은 기존 `QueueKillChord(색, chordStep)`를 두 번 부르는 것으로 만든다.
- 작업 트리에 사용자의 커밋되지 않은 수정이 많고, 사용자가 같은 파일을 동시에 고칠 수 있다. **커밋하지 않는다.** `git add`, `git commit`, `git stash`, `git checkout --`, `git restore`, `git clean` 금지. 자기 태스크의 파일만 고치고, 고치기 직전에 다시 읽는다.
- 화음 적은 라운드 시작 `GameplayTuning.SpecialEnemyStart`(60초) 뒤부터만 나온다. 회색 변형은 없다.
- 색 조합은 서로 다른 두 색이다 (빨강+파랑, 파랑+노랑, 노랑+빨강).
- 반쪽 벗기기는 처치가 아니다: 펄스는 거기서 멈추고, 점수와 콤보는 오르지도 깎이지도 않는다. 벗길 때 처치음은 없다.
- 두 색 어느 쪽도 아닌 펄스는 틀린 색이다 (막힘, 콤보 규칙상 틀린 색 처리).
- 마무리 처치는 1마리로 센다. 피버 펄스는 온전한 화음 적을 한 번에 죽인다.
- 색과 종류는 색 없이 윤곽만으로 구분되어야 한다: 두 도형은 각 색의 기본 도형(삼각형, 원, 마름모) 그대로다.
- 조정용 수치는 `GameplayTuning`에만 둔다. 화면 배치용 수치는 `ChordPulsePrototype`의 상수로 둘 수 있다.
- 테스트 스타일: 네임스페이스 없음, `using NUnit.Framework;`, `[SetUp]`에서 상태 생성. 기존 테스트 파일의 헬퍼를 재사용한다.
- 테스트 실행: UnityMCP `refresh_unity` → `read_console`(오류 0) → `run_tests`(EditMode). 에디터가 플레이 모드이거나 연결되지 않으면 보고하고 멈춘다.

## Review Focus

1. `SpawnOrder`는 struct라서 새 필드의 기본값이 0이다: 화음 적이 아닌 모든 주문(오프닝, 피버, 일반)에서 두 번째 색이 -1이어야 한다. 아니면 모든 적이 "색 0과의 화음 적"이 된다. (Task 2)
2. 화음 적 바로 뒤에 같은 색 일반 적이 있을 때: 펄스는 반쪽을 벗기고 멈추며 뒤의 적은 살아 있어야 한다. (Task 1)
3. 화음 적과 같은 색 일반 적이 동시 접촉 범위 안에 있을 때: 벗기기와 처치가 둘 다 일어나야 한다. (Task 1)
4. 반쪽이 벗겨진 뒤 이미 터진 색의 펄스를 다시 쏨: 틀린 색으로 막혀야 한다. (Task 1)
5. 화음 적이 코어에 닿거나 라운드가 재시작될 때: 풀로 돌아간 오브젝트에 두 번째 도형이나 막대가 남아 다음 일반 적에 보이면 안 된다. (Task 3)

---

### Task 1: 화음 적의 접촉 규칙

**Files:**
- Modify: `Assets/Scripts/Gameplay/EnemyState.cs`
- Modify: `Assets/Scripts/Gameplay/PulseResolver.cs`
- Test: `Assets/Tests/Editor/PulseResolverTests.cs`

**Interfaces:**
- Produces:

```csharp
// EnemyState에 추가
public int SecondColorIndex = -1;    // 0 이상이면 온전한 화음 적 (ColorIndex + SecondColorIndex)
public int StrippedColorIndex = -1;  // 0 이상이면 이미 벗겨진 색 (마무리 때 혼합 화음에 쓴다)
public bool IsChordPair => SecondColorIndex >= 0;

// PulseEventType에 추가
public enum PulseEventType { Kill, Reveal, Strip }
```

규칙 (피버가 아닌 펄스가 온전한 화음 적에 닿았을 때, 기존 "틀린 색" 판정보다 먼저 본다):
- 펄스 색이 `ColorIndex` 또는 `SecondColorIndex`와 같으면 벗기기다. `StrippedColorIndex`에 맞은 색을 넣고, `ColorIndex`는 남은 색이 되고, `SecondColorIndex = -1`. `Strip` 이벤트(Points 0)를 더한다. 회색 벗기기와 똑같이 펄스를 막되 `HitWrongColor`는 세우지 않는다.
- 어느 쪽도 아니면 기존 틀린 색 처리 그대로다.
- 피버 펄스는 온전한 화음 적을 바로 죽인다 (`Kill`, 필드는 건드리지 않는다).
- 벗겨진 뒤의 적은 `ColorIndex` 하나만 가진 일반 적으로 판정된다.

- [ ] **Step 1: 실패하는 테스트 추가** (`PulseResolverTests`). `Pair(c1, c2, distance)`는 기존 `E(c1, distance)`에 `SecondColorIndex = c2`를 준 헬퍼.

```csharp
[Test] public void Pair_HitByFirstColor_StripsAndBlocks()
{ var pair = Pair(0, 1, 1f); var e = L(pair); var p = P(0);
  Assert.AreEqual(PulseOutcome.Blocked, Run(p, e));
  Assert.AreEqual(1, e.Count); Assert.AreEqual(1, pair.ColorIndex); Assert.AreEqual(-1, pair.SecondColorIndex);
  Assert.AreEqual(0, pair.StrippedColorIndex); Assert.IsFalse(p.HitWrongColor); Assert.AreEqual(0, p.Kills);
  Assert.AreEqual(PulseEventType.Strip, _events[0].Type); Assert.AreEqual(0, _events[0].Points); }
[Test] public void Pair_HitBySecondColor_StripsAndKeepsTheFirst()
{ var pair = Pair(0, 1, 1f); var p = P(1);
  Assert.AreEqual(PulseOutcome.Blocked, Run(p, L(pair)));
  Assert.AreEqual(0, pair.ColorIndex); Assert.AreEqual(1, pair.StrippedColorIndex); Assert.IsFalse(pair.IsChordPair); }
[Test] public void Pair_HitByThirdColor_IsWrongColor()
{ var pair = Pair(0, 1, 1f); var p = P(2);
  Assert.AreEqual(PulseOutcome.Blocked, Run(p, L(pair))); Assert.IsTrue(p.HitWrongColor); Assert.IsTrue(pair.IsChordPair); }
[Test] public void Pair_TwoPulsesInEitherOrder_Kill()
{ var pair = Pair(0, 1, 1f); var e = L(pair);
  Run(P(1), e); _events.Clear(); var p = P(0);
  Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e));
  Assert.AreEqual(0, e.Count); Assert.AreEqual(1, p.Kills); Assert.AreEqual(1, pair.StrippedColorIndex); }
[Test] public void StrippedPair_HitByItsPoppedColor_IsWrongColor()
{ var pair = Pair(0, 1, 1f); var e = L(pair);
  Run(P(0), e); var p = P(0);
  Assert.AreEqual(PulseOutcome.Blocked, Run(p, e)); Assert.IsTrue(p.HitWrongColor); Assert.AreEqual(1, e.Count); }
[Test] public void Pair_BlocksThePulse_EnemyBehindSurvives()
{ var e = L(Pair(0, 1, 1f), E(0, 2f)); var p = P(0);
  Assert.AreEqual(PulseOutcome.Blocked, Run(p, e)); Assert.AreEqual(2, e.Count); Assert.AreEqual(0, p.Kills); }
[Test] public void Pair_AndMatchingEnemyInTheSameBand_StripAndKill()
{ var e = L(Pair(0, 1, 2f), E(0, 2.005f)); var p = P(0);
  Assert.AreEqual(PulseOutcome.Blocked, Run(p, e)); Assert.AreEqual(1, e.Count); Assert.AreEqual(1, p.Kills); }
[Test] public void Fever_KillsAnIntactPairOutright()
{ var pair = Pair(0, 1, 1f); var e = L(pair); var p = P(2, fever: true);
  Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e));
  Assert.AreEqual(0, e.Count); Assert.AreEqual(1, p.Kills); Assert.IsTrue(pair.IsChordPair); }
```

- [ ] **Step 2: 테스트 실행, 컴파일 실패 확인**
- [ ] **Step 3: `EnemyState`와 `PulseEventType`, `PulseResolver.Advance`에 위 규칙 구현**
- [ ] **Step 4: `PulseResolverTests` 전체 실행, 기존 테스트 포함 통과 확인**

---

### Task 2: 화음 적 스폰

**Files:**
- Modify: `Assets/Scripts/Gameplay/GameplayTuning.cs`
- Modify: `Assets/Scripts/Gameplay/SpawnDirector.cs`
- Test: `Assets/Tests/Editor/SpawnDirectorTests.cs`

**Interfaces:**
- Produces:

```csharp
// GameplayTuning에 추가
public const double SpecialEnemyStart = 60.0;   // 특수 적이 나오기 시작하는 라운드 시간(초)
public const double ChordPairChance = 0.5;      // 새 색 뭉치의 첫 적이 화음 적일 확률
public const float ChordShapeScale = 0.7f;      // 화음 적의 도형 하나 크기 (기본 대비)

// SpawnOrder에 추가
public int SecondColorIndex;   // 화음 적이면 두 번째 색, 아니면 -1
```

규칙:
- 화음 적은 색 뭉치의 경계에 선다. 오프닝 이후, 피버가 아니고, `RoundSeconds >= SpecialEnemyStart`일 때, **새 뭉치를 시작하는 스폰**에서 `ChordPairChance` 확률로 그 첫 적이 화음 적이 된다. `ColorIndex`는 새 뭉치의 색, `SecondColorIndex`는 직전 뭉치의 색이다. (직전 뭉치를 정리하는 펄스가 반쪽을 벗기고, 다음 뭉치를 정리하는 펄스가 마무리한다.)
- 화음 적은 뭉치 길이 한 칸을 차지하고, 회색이 되지 않는다 (회색 차례가 겹치면 그 적은 회색이 아니다).
- 확률 난수는 조건이 맞을 때만 뽑는다 (60초 전의 난수 순서는 지금과 같아야 한다).
- 그 외 모든 주문(오프닝, 피버, 일반)은 `SecondColorIndex = -1`.

- [ ] **Step 1: 실패하는 테스트 추가** (`SpawnDirectorTests`, 기존 헬퍼 사용)

```csharp
[Test] public void NonPairOrders_AlwaysHaveNoSecondColor()
{ foreach (var x in Collect(0, 400, Ctx(section: 3, seconds: 59))) Assert.AreEqual(-1, x.order.SecondColorIndex);
  _dir.Reset(); foreach (var x in Collect(0, 100, Ctx(fever: true, seconds: 90))) Assert.AreEqual(-1, x.order.SecondColorIndex); }
[Test] public void Pairs_AppearOnlyAfterSixtySeconds()
{ Assert.IsTrue(Collect(0, 800, Ctx(section: 3, seconds: 60)).Any(x => x.order.SecondColorIndex >= 0)); }
[Test] public void Pair_OpensARun_AndBridgesThePreviousRunsColor()
{ var s = AfterOpening(Collect(0, 800, Ctx(section: 3, seconds: 60)));
  for (int i = 1; i < s.Count; i++)
  { var o = s[i].order; if (o.SecondColorIndex < 0) continue;
    Assert.AreNotEqual(o.ColorIndex, o.SecondColorIndex);
    Assert.AreEqual(s[i - 1].order.ColorIndex, o.SecondColorIndex);
    Assert.IsFalse(o.Hidden); } }
[Test] public void Pairs_AreRoughlyHalfOfRunStarts()
{ var s = AfterOpening(Collect(0, 4000, Ctx(section: 3, seconds: 60)));
  int runs = RunsOf(s).Count; int pairs = s.Count(x => x.order.SecondColorIndex >= 0);
  Assert.Greater(runs, 100); Assert.Greater(pairs, runs * 0.3); Assert.Less(pairs, runs * 0.7); }
```

- [ ] **Step 2: 테스트 실행, 컴파일 실패 확인**
- [ ] **Step 3: `GameplayTuning`에 상수 추가, `SpawnOrder`와 `SpawnDirector` 구현**
- [ ] **Step 4: `SpawnDirectorTests` 전체 실행, 기존 테스트 포함 통과 확인**

---

### Task 3: 화음 적의 모양과 소리 연결

**Files:**
- Modify: `Assets/Scripts/ChordPulsePrototype.cs`

**Interfaces:**
- Consumes: Task 1의 `EnemyState.SecondColorIndex`, `StrippedColorIndex`, `IsChordPair`, `PulseEventType.Strip`; Task 2의 `SpawnOrder.SecondColorIndex`, `GameplayTuning.ChordShapeScale`.
- Produces: 화음 적이 나오는 플레이 가능한 빌드

요구사항 (파일을 먼저 끝까지 읽고, 여기 없는 동작은 그대로 둔다):

- **스폰**: `order.SecondColorIndex >= 0`이면 `EnemyState.SecondColorIndex`에 넣고 `HalfSize = BaseEnemyHalfSize * ChordShapeScale`로 만든다.
- **모양**: 화음 적은 기본 도형 둘이 진행 방향에 수직으로 나란히 있고 가는 막대로 이어진다. 각 도형은 자기 색의 기본 도형과 색이며 크기는 기본의 `ChordShapeScale`배다. 두 도형 중심 사이 간격은 도형 하나의 지름보다 조금 넓게(겹치지 않게) 잡는다. 막대는 색이 아닌 밝은 중립색이다. 풀에서 꺼낸 오브젝트를 재사용하므로, 일반 적으로 쓰일 때는 두 번째 도형과 막대가 보이지 않아야 한다 (템플릿에 자식 오브젝트를 두고 켜고 끄는 방식을 권장).
- **벗기기(`Strip` 이벤트)**: 터진 색(`StrippedColorIndex`)의 도형 위치에서 그 색으로 파편 효과를 내고, 그 도형과 막대를 숨긴다. 남은 도형은 제자리에 그대로 둔다. 처치음은 내지 않는다. 점수, 콤보, 연속 처치 수는 바뀌지 않는다.
- **처치(`Kill` 이벤트)**: 죽은 적이 화음 적이었으면(`StrippedColorIndex >= 0` 또는 `IsChordPair`) 두 색 모두에 대해 `QueueKillChord`를 같은 `chordStep`으로 부른다(첫 호출이 돌려준 스텝을 둘째 호출과 그 펄스의 이후 처치가 공유). 파편 효과는 남아 있던 도형마다 그 색으로 낸다. 점수와 콤보는 1마리 처치와 같다.
- **조준 표시**: `UpdateTargetMarkers`의 `Ready`는 화음 적이면 선택한 색이 두 색 중 하나일 때 참이다.
- **풀 반환**: 코어 접촉, 처치, 라운드 재시작 어느 경로로 돌아가든 다음에 꺼낼 때 모양이 그 적의 상태대로 다시 그려져야 한다.

- [ ] **Step 1: 위 요구사항대로 `ChordPulsePrototype.cs` 수정**
- [ ] **Step 2: `refresh_unity` 후 `read_console`로 컴파일 오류와 새 경고 0 확인**
- [ ] **Step 3: EditMode 테스트 전체 실행, 전부 통과 확인**
- [ ] **Step 4: 플레이 모드로 `Assets/Scenes/ChordPulsePrototype.unity`를 돌려 화음 적이 나오는 60초 이후까지 진행하고(자동 조작이나 임시 체력 변경은 메모리 안에서만, 파일에 남기지 않는다) 콘솔에 예외가 없는지 확인. 가능하면 화음 적이 보이는 스크린샷을 찍는다. 끝나면 반드시 플레이 모드를 끈다. 확인한 것과 못 한 것을 구분해 보고한다.**

수동 확인 항목 (사용자가 플레이하며 확인, 구현자는 보고서에 옮겨 적는다):
- 1분 뒤부터 두 도형이 이어진 적이 색 뭉치 사이에 나온다.
- 한쪽 색으로 쏘면 그 도형만 터지고 펄스가 거기서 멈춘다. 남은 도형은 다음 펄스에 죽는다.
- 마무리할 때 두 악기가 섞인 화음이 들린다.
- 두 색이 아닌 색으로 쏘면 막히고 콤보가 끊긴다.
- 색을 보지 않고 도형만으로 어느 두 색인지 읽을 수 있다.
