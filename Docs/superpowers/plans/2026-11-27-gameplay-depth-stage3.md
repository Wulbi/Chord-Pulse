# 게임플레이 확장 3단계 (분열형, 돌격형) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 죽으면 같은 색 새끼 둘로 갈라지는 분열형과, 빠르게 들어오며 틀린 색에 맞으면 더 빨라지는 돌격형을 넣는다.

**Architecture:** 1~2단계의 순수 규칙 클래스(`Assets/Scripts/Gameplay`)를 확장한다. 적 종류는 `EnemyState.Kind`로 표현하고, 분열과 가속은 `PulseResolver`가, 등장은 `SpawnDirector`가 맡으며, `ChordPulsePrototype`은 모양과 새끼의 화면 배치를 맡는다.

**Tech Stack:** Unity (C#), NUnit EditMode 테스트, FMOD (변경 없음).

**Spec:** `Docs/superpowers/specs/2026-11-27-gameplay-depth-design.md` 5.4 "분열형", "돌격형", 5.5 (구현자는 이 문서도 읽는다)

## Global Constraints

- FMOD 프로젝트와 뱅크, `Assets/Scripts/Audio`, `Assets/Scripts/Visual`, `SoundManager`, `GamePanel`은 건드리지 않는다.
- 작업 트리에 사용자의 커밋되지 않은 수정이 많고, 사용자가 같은 파일을 동시에 고칠 수 있다. **커밋하지 않는다.** `git add`, `git commit`, `git stash`, `git checkout --`, `git restore`, `git clean` 금지. 자기 태스크의 파일만 고치고, 고치기 직전에 다시 읽는다.
- 분열형: 죽으면 **같은 색** 새끼 둘. 새끼는 다시 분열하지 않는다. 새끼는 **부모를 죽인 그 펄스에는 맞지 않고** 다음 펄스부터 맞는다 (피버 펄스도 같다). 부모와 새끼는 각각 1마리 처치다.
- 돌격형: 틀린 색 펄스에 맞으면 속도가 오른다. 이 가속은 **돌격형에만** 있다. 한 번에 `ChargerBoostMultiplier`배, 최대 `ChargerMaxBoosts`번.
- 분열형과 돌격형은 회색이 되지 않고 화음 적이 되지 않는다. 피버 중에는 나오지 않는다.
- 기존 규칙(관통, 막힘, 회색 벗기기, 화음 적 벗기기, 콤보, 코어 체력)과 기존 테스트는 그대로다.
- 종류와 상태(가속됨)는 색 변화가 아니라 윤곽, 크기, 잔상, 떨림으로 구분한다.
- 조정용 수치는 `GameplayTuning`에만 둔다. 화면 배치용 수치는 `ChordPulsePrototype`의 상수로 둘 수 있다.
- 테스트 스타일: 네임스페이스 없음, `using NUnit.Framework;`, `[SetUp]`에서 상태 생성. 기존 테스트 파일의 헬퍼를 재사용한다.
- 테스트 실행: UnityMCP `refresh_unity` → `read_console`(오류 0) → `run_tests`(EditMode). 에디터가 플레이 모드이거나 연결되지 않으면 보고하고 멈춘다.

## Review Focus

1. 분열형이 같은 색 뭉치 한가운데서 죽을 때: 펄스는 계속 관통해 뒤의 같은 색 적도 죽이고, 새끼 둘만 남아야 한다. (Task 1)
2. 부모가 죽은 프레임 이후에도 같은 펄스가 계속 날아가는 동안 새끼가 맞으면 안 된다. 그 펄스가 사라진 뒤에는 면역이 남아 있으면 안 된다. (Task 1)
3. 돌격형이 세 번째로 틀린 색에 맞을 때: 더 빨라지지 않고, 가속 이벤트도 나오지 않는다. (Task 1)
4. 돌격형이 색 뭉치 사이에 끼어도 뭉치 규칙(이웃 뭉치는 다른 색, 길이 범위)과 화음 적의 색 연결이 깨지지 않아야 한다. (Task 2)
5. 분열형, 돌격형으로 쓰였던 풀 오브젝트가 일반 적이나 화음 적으로 재사용될 때 크기, 늘어남, 잔상, 테두리, 속 도형이 남으면 안 된다. (Task 3)

---

### Task 1: 분열과 가속 규칙

**Files:**
- Modify: `Assets/Scripts/Gameplay/GameplayTuning.cs`
- Modify: `Assets/Scripts/Gameplay/EnemyState.cs`
- Modify: `Assets/Scripts/Gameplay/PulseResolver.cs`
- Test: `Assets/Tests/Editor/PulseResolverTests.cs`

**Interfaces:**
- Produces:

```csharp
// GameplayTuning에 추가
public const float SplitterScale = 1.5f;            // 분열형 크기 (기본 대비)
public const float SplitChildScale = 0.7f;          // 새끼 크기 (기본 대비)
public const float ChargerScale = 0.75f;            // 돌격형 크기 (기본 대비)
public const float ChargerSpeedMultiplier = 2.0f;   // 돌격형 기본 속도 (기본 적 대비)
public const float ChargerBoostMultiplier = 1.5f;   // 틀린 색 한 번당 속도 배율
public const int ChargerMaxBoosts = 2;

// EnemyState.cs
public enum EnemyKind { Basic, Splitter, Charger }
// EnemyState에 추가
public EnemyKind Kind;        // 기본값 Basic
public int Boosts;            // 돌격형이 가속된 횟수
public PulseState ImmuneTo;   // 이 펄스에는 맞지 않는다 (분열 새끼)

// PulseEventType에 추가
public enum PulseEventType { Kill, Reveal, Strip, Split, Boost }
// PulseEvent에 추가 (생성자 네 번째 인자, 기본값 null)
public readonly EnemyState Parent;   // Split: 갈라진 부모. 그 외 null
```

규칙:
- `ImmuneTo == pulse`인 적은 그 펄스의 접촉 후보에서 뺀다.
- **분열**: `Kind == Splitter`인 적이 죽으면(일반 펄스든 피버 펄스든) 새끼 둘을 `enemies`에 더한다. 새끼는 `Kind = Basic`, 부모와 같은 `ColorIndex`, `Distance`는 부모의 현재 값, `Speed = GameplayTuning.BaseEnemySpeed`, `HalfSize = BaseEnemyHalfSize * SplitChildScale`, `ImmuneTo = pulse`. 이벤트 순서는 `Split`(새끼 1, Parent = 부모), `Split`(새끼 2, Parent = 부모), 그다음 부모의 `Kill`이다 (화면 쪽이 부모가 사라지기 전에 새끼 위치를 잡을 수 있게). `Split`의 Points는 0이고 `pulse.Kills`는 부모 처치로만 1 오른다.
- 분열형이 틀린 색에 맞으면 일반 적과 같이 막기만 한다 (분열 없음).
- **가속**: 피버가 아닌 펄스의 틀린 색 판정(회색 벗기기와 화음 벗기기가 아닌 경우)을 받은 적이 `Kind == Charger`이고 `Boosts < ChargerMaxBoosts`이면 `Speed *= ChargerBoostMultiplier`, `Boosts++`, `Boost` 이벤트(Points 0)를 더한다. 막힘과 `HitWrongColor`는 기존과 같다.
- `Advance`는 여전히 호출당 할당이 없어야 한다. 단, 분열이 일어날 때 새끼 `EnemyState` 둘을 만드는 것은 허용한다.

- [ ] **Step 1: 실패하는 테스트 추가** (`PulseResolverTests`). 헬퍼: `Splitter(color, distance)`는 `E`에 `Kind = EnemyKind.Splitter`, `HalfSize = 0.21f * GameplayTuning.SplitterScale`. `Charger(color, distance)`는 `E`에 `Kind = EnemyKind.Charger`, `Speed = 0.8f`.

```csharp
[Test] public void Splitter_Killed_LeavesTwoImmuneChildren()
{ var parent = Splitter(0, 1f); var e = L(parent); var p = P(0);
  Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e));
  Assert.AreEqual(2, e.Count); Assert.AreEqual(1, p.Kills);
  foreach (var c in e) { Assert.AreEqual(EnemyKind.Basic, c.Kind); Assert.AreEqual(0, c.ColorIndex);
    Assert.AreEqual(parent.Distance, c.Distance, 1e-4f); Assert.AreEqual(GameplayTuning.BaseEnemySpeed, c.Speed);
    Assert.AreEqual(0.21f * GameplayTuning.SplitChildScale, c.HalfSize, 1e-4f); Assert.AreSame(p, c.ImmuneTo); }
  Assert.AreEqual(PulseEventType.Split, _events[0].Type); Assert.AreSame(parent, _events[0].Parent);
  Assert.AreEqual(PulseEventType.Split, _events[1].Type); Assert.AreNotSame(_events[0].Enemy, _events[1].Enemy);
  Assert.AreEqual(PulseEventType.Kill, _events[2].Type); Assert.AreSame(parent, _events[2].Enemy); Assert.AreEqual(1, _events[2].Points); }
[Test] public void SplitChildren_DieToTheNextPulse()
{ var e = L(Splitter(0, 1f)); Run(P(0), e); var p = P(0);
  Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e)); Assert.AreEqual(0, e.Count); Assert.AreEqual(2, p.Kills); }
[Test] public void Splitter_InsideARun_PulseKeepsPiercing()
{ var e = L(E(0, 1f), Splitter(0, 2f), E(0, 3f)); var p = P(0);
  Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e)); Assert.AreEqual(3, p.Kills); Assert.AreEqual(2, e.Count); }
[Test] public void Splitter_WrongColor_BlocksWithoutSplitting()
{ var e = L(Splitter(1, 1f)); var p = P(0);
  Assert.AreEqual(PulseOutcome.Blocked, Run(p, e)); Assert.AreEqual(1, e.Count); Assert.IsTrue(p.HitWrongColor); }
[Test] public void Fever_SplitsToo_AndChildrenSurviveThatPulse()
{ var e = L(Splitter(1, 1f)); var p = P(0, fever: true);
  Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e)); Assert.AreEqual(2, e.Count); Assert.AreEqual(1, p.Kills); }
[Test] public void Charger_WrongColor_SpeedsUpAndBlocks()
{ var c = Charger(1, 1f); var p = P(0);
  Assert.AreEqual(PulseOutcome.Blocked, Run(p, L(c))); Assert.IsTrue(p.HitWrongColor);
  Assert.AreEqual(0.8f * GameplayTuning.ChargerBoostMultiplier, c.Speed, 1e-4f); Assert.AreEqual(1, c.Boosts);
  Assert.AreEqual(PulseEventType.Boost, _events[0].Type); Assert.AreSame(c, _events[0].Enemy); }
[Test] public void Charger_BoostIsCapped()
{ var c = Charger(1, 1f); var e = L(c);
  Run(P(0), e); Run(P(0), e); _events.Clear(); Run(P(0), e);
  Assert.AreEqual(GameplayTuning.ChargerMaxBoosts, c.Boosts);
  Assert.AreEqual(0.8f * GameplayTuning.ChargerBoostMultiplier * GameplayTuning.ChargerBoostMultiplier, c.Speed, 1e-4f);
  Assert.AreEqual(0, _events.Count); }
[Test] public void Charger_RightColor_DiesWithoutBoost()
{ var c = Charger(0, 1f); var e = L(c); var p = P(0);
  Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e)); Assert.AreEqual(0, e.Count); Assert.AreEqual(0, c.Boosts); }
[Test] public void BasicEnemy_WrongColor_NeverSpeedsUp()
{ var b = E(1, 1f); float speed = b.Speed; Run(P(0), L(b)); Assert.AreEqual(speed, b.Speed); Assert.AreEqual(0, b.Boosts); }
```

- [ ] **Step 2: 테스트 실행, 컴파일 실패 확인**
- [ ] **Step 3: 상수, 타입, `PulseResolver.Advance` 규칙 구현**
- [ ] **Step 4: `PulseResolverTests` 전체 실행 후 EditMode 전체 실행, 기존 테스트 포함 통과 확인**

---

### Task 2: 분열형과 돌격형 스폰

**Files:**
- Modify: `Assets/Scripts/Gameplay/GameplayTuning.cs`
- Modify: `Assets/Scripts/Gameplay/SpawnDirector.cs`
- Test: `Assets/Tests/Editor/SpawnDirectorTests.cs`

**Interfaces:**
- Consumes: `EnemyKind`, `GameplayTuning.ChargerSpeedMultiplier` (Task 1)
- Produces:

```csharp
// GameplayTuning에 추가
public const double ChargerStart = 70.0;      // 돌격형이 나오기 시작하는 라운드 시간(초)
public const double SplitterStart = 80.0;     // 분열형이 나오기 시작하는 라운드 시간(초)
public const double ChargerChance = 0.3;      // 색 뭉치가 끝날 때 돌격형이 끼어들 확률
public const double SplitterChance = 0.12;    // 뭉치 안의 적이 분열형일 확률

// SpawnOrder에 추가
public EnemyKind Kind;   // 기본값 Basic
```

규칙 (모두 피버가 아니고 오프닝 이후일 때만. 난수는 조건이 맞을 때만 뽑아서, 각 시작 시간 전의 난수 순서는 지금과 같아야 한다):
- **돌격형**은 뭉치에 속하지 않는다. 뭉치의 마지막 적을 낸 직후, `RoundSeconds >= ChargerStart`이면 `ChargerChance` 확률로 "다음 스폰은 돌격형"으로 예약한다. 예약된 스폰은 색이 세 색 중 무작위, `Kind = Charger`, `Speed = BaseEnemySpeed * ChargerSpeedMultiplier * (1 ± EnemySpeedJitter)`, 회색 아님, `SecondColorIndex = -1`이다. 돌격형은 `_runColor`와 뭉치 길이, 회색 순번(`_spawnNumber`)을 바꾸지 않는다. 돌격형 다음 스폰까지의 간격은 현재 문맥의 일반 간격(썰물이면 `BreathIntervalSteps`, 아니면 `FlowIntervalSteps[L]`)이고, 그다음에 새 뭉치가 평소대로 시작한다. 피버가 켜지거나 꺼지면 예약을 버린다.
- **분열형**은 뭉치의 한 칸이다. `RoundSeconds >= SplitterStart`이고 그 스폰이 화음 적이 아니면 `SplitterChance` 확률로 `Kind = Splitter`가 된다. 색은 뭉치 색 그대로, 회색 아님 (회색 차례가 겹치면 회색이 아니다).
- 그 외 모든 주문은 `Kind = Basic`.

- [ ] **Step 1: 실패하는 테스트 추가** (`SpawnDirectorTests`, 기존 헬퍼 사용. `NoChargers(list)`는 돌격형을 뺀 목록.)

```csharp
[Test] public void NoSpecialKinds_BeforeTheirStartTimes()
{ Assert.IsTrue(Collect(0, 2000, Ctx(section: 3, seconds: 69)).All(x => x.order.Kind == EnemyKind.Basic));
  _dir.Reset(); Assert.IsFalse(Collect(0, 2000, Ctx(section: 3, seconds: 79)).Any(x => x.order.Kind == EnemyKind.Splitter)); }
[Test] public void NoSpecialKinds_InFever()
{ Assert.IsTrue(Collect(0, 400, Ctx(fever: true, seconds: 120)).All(x => x.order.Kind == EnemyKind.Basic)); }
[Test] public void Chargers_AreFastPlainAndOutsideRuns()
{ var s = AfterOpening(Collect(0, 4000, Ctx(section: 3, seconds: 120)));
  var chargers = s.Where(x => x.order.Kind == EnemyKind.Charger).ToList(); Assert.Greater(chargers.Count, 20);
  float fast = GameplayTuning.BaseEnemySpeed * GameplayTuning.ChargerSpeedMultiplier;
  foreach (var c in chargers) { Assert.IsFalse(c.order.Hidden); Assert.AreEqual(-1, c.order.SecondColorIndex);
    Assert.GreaterOrEqual(c.order.Speed, fast * (1f - GameplayTuning.EnemySpeedJitter) - 1e-4f);
    Assert.LessOrEqual(c.order.Speed, fast * (1f + GameplayTuning.EnemySpeedJitter) + 1e-4f); }
  var runs = RunsOf(NoChargers(s)); Assert.Greater(runs.Count, 100);
  for (int i = 1; i < runs.Count; i++) Assert.AreNotEqual(runs[i - 1].Color, runs[i].Color);
  foreach (var r in runs.Skip(1).Take(runs.Count - 2))
  { Assert.GreaterOrEqual(r.Length, GameplayTuning.RunMin[3]); Assert.LessOrEqual(r.Length, GameplayTuning.RunMax[3]); }
  Assert.Greater(chargers.Count, runs.Count * 0.15); Assert.Less(chargers.Count, runs.Count * 0.45); }
[Test] public void Chargers_UseAllThreeColours()
{ var colours = AfterOpening(Collect(0, 4000, Ctx(section: 3, seconds: 120)))
    .Where(x => x.order.Kind == EnemyKind.Charger).Select(x => x.order.ColorIndex).Distinct().Count();
  Assert.AreEqual(3, colours); }
[Test] public void Pairs_StillBridgeRunColours_WithChargersBetween()
{ var s = NoChargers(AfterOpening(Collect(0, 4000, Ctx(section: 3, seconds: 120)))); int pairs = 0;
  for (int i = 1; i < s.Count; i++)
  { var o = s[i].order; if (o.SecondColorIndex < 0) continue; pairs++;
    Assert.AreEqual(s[i - 1].order.ColorIndex, o.SecondColorIndex); Assert.AreEqual(EnemyKind.Basic, o.Kind); }
  Assert.Greater(pairs, 20); }
[Test] public void Splitters_AreRunMembers_NeverGrayOrPairs()
{ var s = NoChargers(AfterOpening(Collect(0, 4000, Ctx(section: 3, seconds: 120))));
  var splitters = s.Where(x => x.order.Kind == EnemyKind.Splitter).ToList();
  Assert.Greater(splitters.Count, s.Count * 0.05); Assert.Less(splitters.Count, s.Count * 0.2);
  foreach (var x in splitters) { Assert.IsFalse(x.order.Hidden); Assert.AreEqual(-1, x.order.SecondColorIndex); } }
```

- [ ] **Step 2: 테스트 실행, 컴파일 실패 확인**
- [ ] **Step 3: 상수, `SpawnOrder.Kind`, `SpawnDirector` 규칙 구현**
- [ ] **Step 4: `SpawnDirectorTests` 전체 실행 후 EditMode 전체 실행, 기존 테스트 포함 통과 확인**

---

### Task 3: 분열형과 돌격형의 모양 연결

**Files:**
- Modify: `Assets/Scripts/ChordPulsePrototype.cs`

**Interfaces:**
- Consumes: Task 1의 `EnemyState.Kind`, `Boosts`, `PulseEventType.Split`, `PulseEventType.Boost`, `PulseEvent.Parent`, 크기와 속도 상수; Task 2의 `SpawnOrder.Kind`.
- Produces: 특수 적 세 종류가 모두 나오는 플레이 가능한 빌드

요구사항 (파일을 먼저 끝까지 읽고, 여기 없는 동작은 그대로 둔다):

- **스폰**: `order.Kind`를 `EnemyState.Kind`에 넣는다. `HalfSize`는 분열형 `BaseEnemyHalfSize * SplitterScale`, 돌격형 `BaseEnemyHalfSize * ChargerScale`. 속도는 `order.Speed` 그대로다.
- **분열형 모양**: 자기 색 기본 도형의 **속 빈 테두리**를 기본의 `SplitterScale`배로 그리고, 그 안에 같은 색의 작은 기본 도형 둘을 나란히 넣는다. 속이 찬 일반 적과 윤곽만으로 구분되어야 한다.
- **돌격형 모양**: 자기 색 기본 도형을 진행 방향으로 가늘고 길게 늘리고(전체 크기는 `ChargerScale`), 뒤쪽(코어 반대편)에 같은 도형의 흐린 잔상 두 겹을 둔다.
- **가속 표시(`Boost` 이벤트와 `Boosts > 0` 상태)**: 색을 바꾸지 않는다. 가속된 돌격형은 좌우로 잘게 떨리고 잔상이 더 길어진다. `Boosts`가 2면 1보다 더 강하게.
- **분열(`Split` 이벤트)**: 새끼의 화면 오브젝트를 풀에서 꺼내 부모의 방향에서 좌우로 조금 벌린 방향(새끼 둘이 서로 반대쪽)에 놓는다. 벌림 각도는 화면 배치 상수다. 새끼는 일반 적 모양이며 크기는 상태의 `HalfSize`를 따른다. `Split` 이벤트는 부모의 `Kill`보다 먼저 오므로 부모의 방향을 그때 읽는다. 새끼가 생길 때 처치음, 점수, 콤보 변화는 없다.
- **크기**: 일반 적과 새끼의 그려지는 크기는 `HalfSize`를 따라야 한다 (새끼는 기본의 `SplitChildScale`배로 보인다).
- **조준 표시**: 기존 로직을 유지한다 (가장 가까운 적 기준, 적별 `HalfSize`).
- **풀 반환과 재사용**: 어떤 종류로 쓰였든 다음에 꺼낼 때 크기, 늘어남, 잔상, 테두리, 속 도형, 떨림 오프셋이 그 적의 상태대로 다시 그려져야 한다.
- 매 프레임 경로(`Update`)에 새 할당을 넣지 않는다.

- [ ] **Step 1: 위 요구사항대로 `ChordPulsePrototype.cs` 수정**
- [ ] **Step 2: `refresh_unity` 후 `read_console`로 컴파일 오류와 새 경고 0 확인**
- [ ] **Step 3: EditMode 테스트 전체 실행, 전부 통과 확인**
- [ ] **Step 4: 플레이 모드로 `Assets/Scenes/ChordPulsePrototype.unity`를 돌려 분열형이 나오는 80초 이후까지 진행하고(자동 조작, 임시 체력 변경, 특수 적 직접 스폰은 메모리 안에서만 하고 파일에 남기지 않는다) 콘솔에 예외가 없는지 확인. 분열형, 돌격형, 가속된 돌격형, 갓 나온 새끼가 보이는 스크린샷을 가능하면 찍는다. 끝나면 반드시 플레이 모드를 끈다. 확인한 것과 못 한 것을 구분해 보고한다.**

수동 확인 항목 (사용자가 플레이하며 확인, 구현자는 보고서에 옮겨 적는다):
- 70초쯤부터 가늘고 빠른 적이 뭉치 사이로 뚫고 들어온다. 틀린 색으로 맞히면 떨리면서 더 빨라진다.
- 80초쯤부터 속 빈 큰 테두리 적이 나오고, 잡으면 같은 색 작은 적 둘로 갈라진다. 새끼는 그 펄스에는 죽지 않고 다음 펄스에 죽는다.
- 분열형이 같은 색 뭉치 안에서 죽어도 펄스는 뒤의 적까지 계속 잡는다.
- 색을 보지 않고 윤곽만으로 기본, 화음, 분열형, 돌격형이 구분된다.
