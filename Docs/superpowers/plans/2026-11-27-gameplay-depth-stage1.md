# 게임플레이 확장 1단계 (기본 규칙) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 2박 쿨타임, 다중 처치 점수, 느리고 많은 적과 음악을 따르는 밀물·썰물, 코어의 색 도형 표시를 넣어 "모아서 한 번에 잡는" 플레이가 가능한 빌드를 만든다.

**Architecture:** 게임 규칙을 Unity에 의존하지 않는 순수 클래스(`Assets/Scripts/Gameplay`)로 떼어내 EditMode 테스트로 검증하고, `ChordPulsePrototype`은 입력·표시·음악 연결만 맡는다. 기존 음악 로직(`Assets/Scripts/Audio`)이 쓰는 방식과 같다. 2단계(화음 적)와 3단계(분열형, 돌격형)는 1단계 플레이 조정 뒤 별도 계획으로 쓴다.

**Tech Stack:** Unity (C#), NUnit EditMode 테스트, 새 Input System, FMOD (변경 없음). asmdef 없음: `Assets/Scripts`는 Assembly-CSharp, `Assets/Tests/Editor`는 Assembly-CSharp-Editor.

**Spec:** `Docs/superpowers/specs/2026-11-27-gameplay-depth-design.md` (구현자는 이 문서도 읽는다)

## Global Constraints

- FMOD 프로젝트와 뱅크는 건드리지 않는다. `Assets/Scripts/Audio`, `Assets/Scripts/Visual`, `SoundManager`의 동작도 바꾸지 않는다.
- 작업 트리에 사용자의 커밋되지 않은 수정이 많다. **커밋하지 않는다.** `git add`, `git commit`, `git stash`, `git checkout --`, `git restore` 금지. 자기 태스크의 파일만 고친다.
- 박자 판정, 코어 근접 보너스, 무적 시간은 넣지 않는다. 코어 체력 규칙(적 한 마리당 1, 체력 3)은 그대로다.
- 콤보 규칙은 그대로다: 처치가 있었던 펄스마다 +1(몇 마리든 1), 10에서 피버. 처치 없이 틀린 색에 막히거나 끝까지 빗나가면 0, 코어가 맞으면 0, 회색만 벗긴 펄스는 유지.
- 조작은 그대로다: A = 색 순환, Space = 발사.
- 색이나 상태를 색 변화만으로 전달하지 않는다.
- 조정용 수치는 전부 `GameplayTuning`에 둔다. 다른 파일에 숫자를 새로 박지 않는다.
- 테스트 클래스는 기존 스타일을 따른다: 네임스페이스 없음, `using NUnit.Framework;`, `[SetUp]`에서 상태를 새로 만든다.
- 테스트 실행: Unity 에디터가 열려 있으면 UnityMCP의 `refresh_unity` → `read_console`(컴파일 오류 0 확인) → `run_tests`(mode EditMode, 해당 테스트 클래스 필터). 에디터에 연결할 수 없으면 그 사실을 보고하고 멈춘다(배치 모드로 우회하지 않는다).
- 새 `.cs` 파일의 `.meta`는 Unity가 만든다. 손으로 쓰지 않는다.

## Review Focus

1. 음악이 아직 안 돌 때(`BeatPosition == -1`) Space를 누름: 발사되지 않아야 하고, 쿨타임이 영원히 잠기지도 않아야 한다. (Task 1)
2. 프레임이 크게 끊겨 한 프레임에 펄스가 경기장 끝까지 감: 적을 가까운 순서대로 처리하고, 틀린 색 뒤의 적은 살아 있어야 한다. (Task 2)
3. 라운드 재시작: 박 위치가 0으로 돌아가도 첫 Space가 바로 나가고, 스폰이 오프닝부터 다시 시작해야 한다. (Task 1, 3)
4. 쿨타임 도중 피버가 시작되거나, 예약된 입력이 있는 채로 피버가 시작됨: 바로 쏠 수 있어야 하고 예약 입력이 두 번 나가면 안 된다. (Task 1)
5. 프레임 끊김으로 스텝이 여러 개 건너뜀: 밀린 만큼 한꺼번에 쏟아내지 않고 한 마리만 내고 다음 간격을 잡아야 한다. (Task 3)

---

### Task 1: 수치 모음과 펄스 쿨타임

**Files:**
- Create: `Assets/Scripts/Gameplay/GameplayTuning.cs`
- Create: `Assets/Scripts/Gameplay/PulseCooldown.cs`
- Test: `Assets/Tests/Editor/PulseCooldownTests.cs`

**Interfaces:**
- Produces: `GameplayTuning` (아래 값 그대로), `PulseCooldown`

```csharp
public static class GameplayTuning
{
    // 쿨타임
    public const double CooldownBeats = 2.0;
    public const double CooldownBufferBeats = 0.25;
    // 적
    public const float BaseEnemySpeed = 0.4f;          // 기존 0.65
    public const float EnemySpeedJitter = 0.15f;       // 속도 = Base * (1 ± Jitter)
    public const float BaseEnemyHalfSize = 0.21f;
    public const int EnemyPoolSize = 48;
    // 스폰 (스텝 = 반 박)
    public const int BreathIntervalSteps = 8;
    public static readonly int[] FlowIntervalSteps = { 4, 3, 2, 2 };   // 밀도 단계 0..3
    public static readonly int[] RunMin = { 2, 3, 4, 4 };
    public static readonly int[] RunMax = { 3, 4, 6, 6 };
    public const int BreathRunMax = 2;
    public const int RunGapSteps = 2;                  // 색 뭉치 사이 추가 간격
    public const double HiddenEnemyStart = 30.0;
    public const int HiddenEvery = 4;                  // n번째 스폰마다 회색
    public static readonly double[] DifficultyThresholds = { 17.8, 34.3, 49.8 };
    public const int DenseSection = 2;                 // SectionIndex가 이 이상이면 밀도 +1
    // 긴장도
    public const float TensionOuterRadius = 0.55f;     // 경기장 반지름 대비
    public const float TensionCrowdBand = 0.35f;       // 접촉~외곽 구간 중 무리로 세는 안쪽 비율
    public const float TensionPerCrowd = 0.15f;
}
```

```csharp
public sealed class PulseCooldown
{
    public void Reset();
    /// Space가 눌렸다. true면 지금 발사. false면 무시되었거나 예약되었다.
    public bool Press(double beat, bool fever);
    /// 매 프레임 호출. true면 예약된 입력이 지금 발사된다(한 번만).
    public bool Tick(double beat, bool fever);
    /// 0..1, 1이면 쏠 수 있다. 피버 중이나 발사 전에는 1.
    public float Progress(double beat, bool fever);
}
```

규칙: `beat < 0`이면 `Press`는 false이고 아무 상태도 바꾸지 않는다. 피버 중 발사는 쿨타임을 시작하지 않는다. 일반 발사는 그 박 위치부터 `CooldownBeats` 동안 잠근다. 잠긴 동안의 입력은 남은 시간이 `CooldownBufferBeats` 이하일 때만 예약된다.

- [ ] **Step 1: 실패하는 테스트 작성** (`PulseCooldownTests`)

```csharp
[Test] public void FirstPress_Fires() { Assert.IsTrue(_cd.Press(4.0, false)); }
[Test] public void Press_BeforeMusicRuns_DoesNothing()
{ Assert.IsFalse(_cd.Press(-1, false)); Assert.IsTrue(_cd.Press(0.1, false)); }
[Test] public void Press_DuringCooldown_IsIgnored()
{ _cd.Press(4.0, false); Assert.IsFalse(_cd.Press(5.0, false)); Assert.IsFalse(_cd.Tick(6.0, false)); Assert.IsTrue(_cd.Press(6.0, false)); }
[Test] public void Press_InLastQuarterBeat_IsBufferedAndFiresOnce()
{ _cd.Press(4.0, false); Assert.IsFalse(_cd.Press(5.8, false)); Assert.IsFalse(_cd.Tick(5.9, false));
  Assert.IsTrue(_cd.Tick(6.0, false)); Assert.IsFalse(_cd.Tick(6.01, false)); Assert.IsFalse(_cd.Press(6.5, false)); }
[Test] public void Fever_HasNoCooldown_AndStartsNone()
{ Assert.IsTrue(_cd.Press(4.0, true)); Assert.IsTrue(_cd.Press(4.1, true)); Assert.IsTrue(_cd.Press(4.2, false)); }
[Test] public void FeverStart_ReleasesCooldown_AndBufferedPressFiresOnce()
{ _cd.Press(4.0, false); _cd.Press(5.8, false); Assert.IsTrue(_cd.Tick(5.85, true)); Assert.IsFalse(_cd.Tick(5.9, true)); }
[Test] public void Reset_AllowsPressAtEarlierBeat()
{ _cd.Press(40.0, false); _cd.Reset(); Assert.IsTrue(_cd.Press(0.5, false)); }
[Test] public void Progress_FillsOverTwoBeats()
{ Assert.AreEqual(1f, _cd.Progress(3.0, false)); _cd.Press(4.0, false);
  Assert.AreEqual(0.5f, _cd.Progress(5.0, false), 1e-4f); Assert.AreEqual(1f, _cd.Progress(7.0, false)); Assert.AreEqual(1f, _cd.Progress(5.0, true)); }
```

- [ ] **Step 2: 테스트 실행, 컴파일 실패 확인** (`PulseCooldown` 없음)
- [ ] **Step 3: `GameplayTuning`과 `PulseCooldown` 구현**
- [ ] **Step 4: `PulseCooldownTests` 실행, 전부 통과 확인**

---

### Task 2: 펄스와 적의 접촉 판정

**Files:**
- Create: `Assets/Scripts/Gameplay/EnemyState.cs`
- Create: `Assets/Scripts/Gameplay/PulseResolver.cs`
- Test: `Assets/Tests/Editor/PulseResolverTests.cs`

**Interfaces:**
- Consumes: 없음 (현재 동작의 원본은 `ChordPulsePrototype.AdvanceWaves`)
- Produces:

```csharp
public sealed class EnemyState
{
    public int ColorIndex;
    public float Distance;      // 코어 중심에서 적 중심까지
    public float Speed;
    public float HalfSize;
    public bool IsHidden;
}

public sealed class PulseState
{
    public int ColorIndex;
    public float Radius;
    public bool IsFever;
    public int Kills;           // 이 펄스의 누적 처치 수
    public bool HitWrongColor;
}

public enum PulseEventType { Kill, Reveal }

public readonly struct PulseEvent
{
    public readonly PulseEventType Type;
    public readonly EnemyState Enemy;
    public readonly int Points;   // Kill: 이 펄스의 n번째 처치면 n. Reveal: 0
}

public enum PulseOutcome { Travelling, Blocked, ReachedEdge }

public sealed class PulseResolver
{
    public const float WaveHitPadding = 0.0275f;
    public const float SimultaneousContactTolerance = 0.01f;
    /// 펄스를 한 프레임 전진시킨다. 죽은 적은 enemies에서 제거하고, 일어난 일을 가까운 순서대로 events에 더한다.
    public PulseOutcome Advance(PulseState pulse, List<EnemyState> enemies, float waveSpeed, float maxRadius,
        float deltaTime, List<PulseEvent> events);
}
```

`Advance`는 `AdvanceWaves`의 한 웨이브 처리와 **같은 결과**를 내야 한다. 달라지는 것은 세 가지뿐이다: 접촉 허용치는 `enemy.HalfSize + WaveHitPadding`, 접촉 시간 계산의 적 속도는 `enemy.Speed`, 처치마다 `Points = ++pulse.Kills`. 유지할 기존 동작: 이미 지나친 적(`Distance + tolerance < Radius`)은 무시, 접촉 반경 순으로 처리, 회색은 피버가 아니면 벗기기만 하고 펄스를 막음(`HitWrongColor`는 세우지 않음), 틀린 색은 막고 `HitWrongColor = true`, 막힌 뒤에도 `SimultaneousContactTolerance` 안의 동시 접촉은 마저 처리, 피버 펄스는 색과 회색을 무시하고 죽임. 반환: 막혔으면 `Blocked`, 아니고 `maxRadius`에 닿았으면 `ReachedEdge`, 그 외 `Travelling`(이때 `pulse.Radius`를 갱신). 내부 버퍼는 재사용해 호출당 할당이 없게 한다.

- [ ] **Step 1: 실패하는 테스트 작성** (`PulseResolverTests`). 헬퍼: `E(color, distance, hidden = false)`는 `Speed = 0.4f, HalfSize = 0.21f`인 적. `Run(pulse, enemies)`는 `waveSpeed = 8f, maxRadius = 5f, deltaTime = 1/60f`로 `Travelling`이 아닐 때까지 `Advance`를 반복하고(적 이동은 하지 않음) 마지막 결과를 돌려준다.

```csharp
[Test] public void SameColor_PiercesAll_PointsCountUp()
{ var e = L(E(0,1f), E(0,2f), E(0,3f)); var p = P(0);
  Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e));
  Assert.AreEqual(0, e.Count); CollectionAssert.AreEqual(new[]{1,2,3}, KillPoints()); Assert.AreEqual(3, p.Kills); }
[Test] public void WrongColor_Blocks_AndEnemiesBehindSurvive()
{ var e = L(E(0,1f), E(1,2f), E(0,3f)); var p = P(0);
  Assert.AreEqual(PulseOutcome.Blocked, Run(p, e));
  Assert.AreEqual(2, e.Count); Assert.IsTrue(p.HitWrongColor); Assert.AreEqual(1, p.Kills); }
[Test] public void Hidden_IsRevealedNotKilled_BlocksWithoutWrongColor()
{ var gray = E(1,1f,true); var e = L(gray, E(0,2f)); var p = P(0);
  Assert.AreEqual(PulseOutcome.Blocked, Run(p, e));
  Assert.IsFalse(gray.IsHidden); Assert.AreEqual(2, e.Count); Assert.IsFalse(p.HitWrongColor);
  Assert.AreEqual(PulseEventType.Reveal, _events[0].Type); Assert.AreEqual(0, _events[0].Points); }
[Test] public void Fever_KillsEveryColorAndHidden()
{ var e = L(E(0,1f), E(1,2f,true), E(2,3f)); var p = P(0, fever: true);
  Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e)); Assert.AreEqual(0, e.Count); Assert.AreEqual(3, p.Kills); }
[Test] public void SimultaneousContact_MatchingEnemyBesideWrongColorStillDies()
{ var e = L(E(1,2f), E(0,2.005f)); var p = P(0);
  Assert.AreEqual(PulseOutcome.Blocked, Run(p, e)); Assert.AreEqual(1, e.Count); Assert.AreEqual(1, e[0].ColorIndex); }
[Test] public void EnemyAlreadyBehindTheWave_IsIgnored()
{ var e = L(E(0,1f)); var p = P(0); p.Radius = 2f;
  Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e)); Assert.AreEqual(1, e.Count); }
[Test] public void LargerEnemy_IsHitEarlier()
{ var big = E(1,3f); big.HalfSize = 0.5f; var p = P(0);
  _resolver.Advance(p, L(big), 8f, 5f, 0.3f, _events);   // 반경 0.42 + 2.4 = 2.82 >= 3 - 0.5 - 패딩
  Assert.IsTrue(p.HitWrongColor); }
[Test] public void HugeFrame_StillResolvesInOrder()
{ var e = L(E(0,3f), E(1,2f), E(0,1f)); var p = P(0);
  Assert.AreEqual(PulseOutcome.Blocked, _resolver.Advance(p, e, 8f, 5f, 10f, _events));
  Assert.AreEqual(2, e.Count); Assert.AreEqual(1, p.Kills); }
[Test] public void NoEnemies_ZeroDelta_KeepsTravelling()
{ var p = P(0); Assert.AreEqual(PulseOutcome.Travelling, _resolver.Advance(p, L(), 8f, 5f, 0f, _events)); Assert.AreEqual(0, _events.Count); }
```

`P(color, fever = false)`는 `Radius = 0.42f`인 펄스, `L(...)`은 `List<EnemyState>`, `KillPoints()`는 Kill 이벤트의 `Points` 목록이다.

- [ ] **Step 2: 테스트 실행, 컴파일 실패 확인**
- [ ] **Step 3: `EnemyState.cs`(위 타입 전부)와 `PulseResolver` 구현**
- [ ] **Step 4: `PulseResolverTests` 실행, 전부 통과 확인**

---

### Task 3: 스폰 결정 (밀물과 썰물)

**Files:**
- Create: `Assets/Scripts/Gameplay/SpawnDirector.cs`
- Test: `Assets/Tests/Editor/SpawnDirectorTests.cs`

**Interfaces:**
- Consumes: `GameplayTuning` (Task 1)
- Produces:

```csharp
public struct SpawnContext
{
    public int Step;            // SoundManager.CurrentStep (반 박 단위, 한 마디 = 8)
    public bool Fever;
    public bool Breath;         // SoundManager.IsBreath
    public int Section;         // SoundManager.SectionIndex 0..3
    public double RoundSeconds;
}

public struct SpawnOrder
{
    public int ColorIndex;      // 0..2
    public bool Hidden;
    public float Speed;
}

public sealed class SpawnDirector
{
    public const int ColorCount = 3;
    public const int StepsPerBeat = 2;
    public const int StepsPerBar = 8;
    public SpawnDirector(System.Random random);
    public void Reset();
    /// 매 프레임 호출. 이번에 낼 적이 있으면 true. 한 번에 최대 한 마리.
    public bool TryNext(in SpawnContext context, out SpawnOrder order);
    public static int Difficulty(double roundSeconds);     // DifficultyThresholds를 넘긴 개수 0..3
    public static int DensityLevel(int section, double roundSeconds);  // min(3, Difficulty + (section >= DenseSection ? 1 : 0))
}
```

규칙:
- `Step < 0`이면 false.
- 오프닝: 처음 세 마리는 색 0, 1, 2 순서로 마디 첫 스텝(`Step % 8 == 0`)에 한 마리씩, 회색 아님.
- 그 뒤는 색 뭉치(run) 단위다. 뭉치는 한 색이고, 새 뭉치의 색은 직전 뭉치와 다르다. 뭉치 길이는 밀물이면 `[RunMin[L], RunMax[L]]`, 썰물(`Breath`)이면 `[1, BreathRunMax]`에서 고른다 (`L = DensityLevel`).
- 스폰 간격: 썰물 `BreathIntervalSteps`, 밀물 `FlowIntervalSteps[L]`. 뭉치가 끝나면 다음 스폰까지 `RunGapSteps`를 더한다.
- 다음 스폰 스텝은 **실제로 스폰한 스텝**을 기준으로 잡는다. 스텝이 여러 개 건너뛰어도 한 마리만 낸다.
- 회색: `RoundSeconds >= HiddenEnemyStart`이고 오프닝 이후 스폰 번호가 `HiddenEvery`의 배수일 때.
- 속도: `BaseEnemySpeed * (1 ± EnemySpeedJitter)` 범위의 균등 난수.
- 피버: 색 무작위, 박마다(`StepsPerBeat` 간격) 한 마리. 피버가 켜지거나 꺼지는 순간 진행 중인 뭉치를 버리고 그 스텝부터 다시 계획한다.

- [ ] **Step 1: 실패하는 테스트 작성** (`SpawnDirectorTests`). 헬퍼 `Collect(fromStep, toStep, ctx 템플릿)`은 스텝을 1씩 올리며 `TryNext`를 부르고 `(step, order)` 목록을 돌려준다. `new System.Random(1234)` 사용.

```csharp
[Test] public void BeforeMusic_SpawnsNothing() { Assert.IsFalse(_dir.TryNext(Ctx(step: -1), out _)); }
[Test] public void Opening_ThreeColorsOnDownbeats()
{ var s = Collect(0, 23, Ctx());
  CollectionAssert.AreEqual(new[]{0,8,16}, Steps(s).Take(3)); CollectionAssert.AreEqual(new[]{0,1,2}, Colors(s).Take(3)); }
[Test] public void Breath_SpawnsAtMostOncePerBar()
{ var s = AfterOpening(Collect(0, 200, Ctx(breath: true))); AssertGapsAtLeast(s, 8); }
[Test] public void Flow_Level3_SpawnsEveryBeatWithinARun()
{ var s = AfterOpening(Collect(0, 400, Ctx(section: 3, seconds: 60)));
  Assert.AreEqual(2, MinGap(s)); Assert.LessOrEqual(MaxGap(s), 4); }
[Test] public void Runs_AreOneColor_AndNeighbouringRunsDiffer()
{ var runs = RunsOf(AfterOpening(Collect(0, 800, Ctx(section: 3, seconds: 60))));
  foreach (var r in runs.Skip(1).Take(runs.Count - 2)) { Assert.GreaterOrEqual(r.Length, 4); Assert.LessOrEqual(r.Length, 6); }
  for (int i = 1; i < runs.Count; i++) Assert.AreNotEqual(runs[i-1].Color, runs[i].Color); }
[Test] public void NoHiddenBeforeThirtySeconds_SomeAfter()
{ Assert.IsFalse(Collect(0, 400, Ctx(seconds: 29)).Any(x => x.order.Hidden));
  _dir.Reset(); Assert.IsTrue(Collect(0, 400, Ctx(seconds: 31)).Any(x => x.order.Hidden)); }
[Test] public void Speed_StaysInsideJitterRange()
{ foreach (var x in Collect(0, 400, Ctx(section: 3, seconds: 60))) { Assert.GreaterOrEqual(x.order.Speed, 0.34f - 1e-4f); Assert.LessOrEqual(x.order.Speed, 0.46f + 1e-4f); } }
[Test] public void Fever_SpawnsOncePerBeat()
{ var s = Collect(100, 131, Ctx(fever: true)); Assert.AreEqual(16, s.Count); }
[Test] public void SkippedSteps_SpawnOnlyOne()
{ Collect(0, 40, Ctx(section: 3, seconds: 60));
  Assert.IsTrue(_dir.TryNext(Ctx(step: 200, section: 3, seconds: 60), out _));
  Assert.IsFalse(_dir.TryNext(Ctx(step: 200, section: 3, seconds: 60), out _)); }
[Test] public void Reset_StartsOpeningAgain()
{ Collect(0, 100, Ctx()); _dir.Reset(); var s = Collect(0, 23, Ctx()); CollectionAssert.AreEqual(new[]{0,1,2}, Colors(s).Take(3)); }
[Test] public void DensityLevel_CombinesDifficultyAndSection()
{ Assert.AreEqual(0, SpawnDirector.DensityLevel(0, 0)); Assert.AreEqual(1, SpawnDirector.DensityLevel(2, 0));
  Assert.AreEqual(2, SpawnDirector.DensityLevel(0, 40)); Assert.AreEqual(3, SpawnDirector.DensityLevel(3, 60)); }
```

- [ ] **Step 2: 테스트 실행, 컴파일 실패 확인**
- [ ] **Step 3: `SpawnDirector` 구현**
- [ ] **Step 4: `SpawnDirectorTests` 실행, 전부 통과 확인**

---

### Task 4: 음악 긴장도 계산

**Files:**
- Create: `Assets/Scripts/Gameplay/TensionMeter.cs`
- Test: `Assets/Tests/Editor/TensionMeterTests.cs`

**Interfaces:**
- Consumes: `GameplayTuning` (Task 1), `EnemyState` (Task 2)
- Produces:

```csharp
public static class TensionMeter
{
    /// 0..1. coreRadius: 코어 반지름, arenaRadius: 경기장 반지름.
    public static float Compute(IReadOnlyList<EnemyState> enemies, float coreRadius, float arenaRadius, int health, int maxHealth);
}
```

공식 (기존 `ChordPulsePrototype.MusicTension`에서 무리 가산만 바꾼다):
- 적의 간격 `gap = Distance - HalfSize - coreRadius` (0 미만은 0). `outerGap = max(0.01, arenaRadius * TensionOuterRadius - coreRadius - BaseEnemyHalfSize)`.
- `proximity = 1 - clamp01(nearestGap / outerGap)` (적이 없으면 0).
- `crowd = clamp01((gap <= outerGap * TensionCrowdBand인 적 수 - 1) * TensionPerCrowd)` (0 미만은 0).
- `lives = health <= 1 ? 0.75 : health < maxHealth ? 0.3 : 0`.
- 결과 `max(clamp01(proximity * 0.85 + crowd), lives)`.

- [ ] **Step 1: 실패하는 테스트 작성** (`TensionMeterTests`). `core = 0.42f, arena = 4.55f`, `At(gap)`은 `HalfSize = 0.21f, Distance = 0.42f + 0.21f + gap`인 적. 이 값에서 `outerGap ≈ 1.8725`.

```csharp
[Test] public void NoEnemies_FullHealth_IsZero() { Assert.AreEqual(0f, T(L(), 3)); }
[Test] public void LostLives_SetAFloor() { Assert.AreEqual(0.3f, T(L(), 2), 1e-4f); Assert.AreEqual(0.75f, T(L(), 1), 1e-4f); }
[Test] public void FarCrowd_AddsNothing()
{ var far = new List<EnemyState>(); for (int i = 0; i < 20; i++) far.Add(At(2.5f + i * 0.05f)); Assert.AreEqual(0f, T(far, 3)); }
[Test] public void OneEnemyAtContact_Is085() { Assert.AreEqual(0.85f, T(L(At(0f)), 3), 1e-4f); }
[Test] public void MidFieldCrowd_DoesNotSaturate()
{ var mid = new List<EnemyState>(); for (int i = 0; i < 10; i++) mid.Add(At(1.0f + i * 0.08f));
  Assert.Less(T(mid, 3), 0.5f); }
[Test] public void CrowdAtTheCore_Saturates() { Assert.AreEqual(1f, T(L(At(0f), At(0.1f), At(0.2f), At(0.3f)), 3), 1e-4f); }
```

- [ ] **Step 2: 테스트 실행, 컴파일 실패 확인**
- [ ] **Step 3: `TensionMeter` 구현**
- [ ] **Step 4: `TensionMeterTests` 실행, 전부 통과 확인**

---

### Task 5: `ChordPulsePrototype`에 연결

**Files:**
- Modify: `Assets/Scripts/ChordPulsePrototype.cs`
- Modify: `Assets/Scripts/UI/Panel/GamePanel.cs` (`SetStatus`의 표시 문구 "Hits" → "Score")

**Interfaces:**
- Consumes: Task 1~4의 전부 (각 태스크의 Produces 블록 그대로)
- Produces: 플레이 가능한 1단계 빌드

요구사항 (기존 파일을 먼저 끝까지 읽는다. 여기 없는 동작은 그대로 둔다):

- **적 모델**: 내부 `Enemy`는 `EnemyState State`와 `GameObject Instance`, 스폰 방향(단위 벡터)을 가진다. 규칙용 `List<EnemyState>`와 표시용 목록을 같이 관리한다. 위치는 매 프레임 `방향 * State.Distance`로 놓는다. 적 이동은 `State.Speed`, 코어 접촉 판정은 `State.Distance <= CoreRadius + State.HalfSize`. 스폰 시 `HalfSize = GameplayTuning.BaseEnemyHalfSize`. 풀 등록 크기는 `GameplayTuning.EnemyPoolSize`.
- **스폰**: `SpawnNextEnemy`, `_groupColor`, `_groupSize`, `_spawnCount`, `_openingEnemiesSpawned`, `_nextSpawnStep`, `SameColorChance`, `HiddenEnemyStart`, `DifficultyThresholds`, `DifficultyLevel`, `NextDownbeat`를 지우고 `SpawnDirector.TryNext`를 매 프레임 부른다. 컨텍스트는 `CurrentStep`, `FeverActive`, `IsBreath`, `SectionIndex`, `RoundSeconds`. 방향 선택(직전 스폰과 60도 이상 차이)은 지금 로직을 유지한다. `_soundManager.Intensity`에는 `SpawnDirector.Difficulty(RoundSeconds)`를 넣는다. `StartRound`에서 `Reset`.
- **펄스**: `AdvanceWaves`의 판정부를 `PulseResolver.Advance`로 바꾼다. `Kill` 이벤트마다: 파편 효과, 풀 반환, 표시 목록에서 제거, `QueueKillChord`(한 펄스의 처치는 첫 처치의 `ChordStep` 공유, 지금과 동일), `_score += Points`, `_killStreak++`, 그 펄스의 첫 처치이고 피버 펄스가 아니면 `AddPulseCombo()`. `Reveal` 이벤트는 모양 갱신. 펄스가 끝날 때(`Blocked`/`ReachedEdge`) 콤보 초기화 조건은 지금과 같다: 피버 펄스가 아니고 `Kills == 0`이며 "막혔지만 `HitWrongColor`가 아님"이 아닐 때 `ResetPulseCombo()`.
- **쿨타임**: Space 입력은 `PulseCooldown.Press(BeatPosition, fever)`가 true일 때만 발사한다. 매 프레임 `Tick`이 true면 같은 발사 함수를 부른다. "도움말을 닫은 Space는 발사가 아니다"(`_roundStartFrame`) 규칙은 유지한다. `StartRound`에서 `Reset`.
- **쿨타임 표시**: 코어 안쪽(반지름 `CoreRadius - 0.1f`)에 닫히지 않은 호(arc) `LineRenderer`를 하나 두고 `Progress`만큼 12시 방향에서 시계 방향으로 그린다. `Progress == 1`이면 숨긴다. 색은 현재 펄스 색을 따르되 진행은 호의 길이로만 전달한다.
- **코어의 색 도형**: 코어 중심에 `SpriteRenderer`를 두고 `_enemyShapes[_selectedColor]`를 현재 색으로, 지름 약 `CoreRadius * 0.8f` 크기로 표시한다. A로 색을 바꿀 때 갱신한다. 피버 중에는 숨긴다.
- **긴장도**: `MusicTension()`을 `TensionMeter.Compute(states, CoreRadius, _arenaRadius, _health, MaxHealth)`로 바꾸고 지운다. `UpdateTargetMarkers`의 위험도 계산은 그대로 두되 `EnemyHalfSize` 상수 대신 적별 `HalfSize`를 쓴다.
- **HUD**: `_hits`를 `_score`로 바꾸고 `GamePanel`의 문구를 "Score"로 바꾼다. `SetStatus`의 인자 구성은 바꾸지 않는다.
- 쓰이지 않게 된 상수와 필드(`EnemySpeed`, `EnemyHalfSize`, `WaveHitPadding`, `SimultaneousContactTolerance`, `_contacts` 등)는 지운다.

- [ ] **Step 1: 위 요구사항대로 `ChordPulsePrototype.cs`와 `GamePanel.cs` 수정**
- [ ] **Step 2: `refresh_unity` 후 `read_console`로 컴파일 오류와 경고 0 확인**
- [ ] **Step 3: EditMode 테스트 전체 실행, 기존 테스트 포함 전부 통과 확인**
- [ ] **Step 4: 플레이 모드로 `Assets/Scenes/ChordPulsePrototype.unity`를 30초 이상 돌리고 `read_console`에 예외가 없는지 확인. 가능하면 스크린샷으로 코어의 도형과 쿨타임 호, 다수의 적이 보이는지 확인. 플레이 모드를 쓸 수 없으면 그 사실을 보고한다.**

수동 확인 항목 (사용자가 플레이하며 확인, 구현자는 보고서에 옮겨 적는다):
- Space 연타 시 2박에 한 번만 나가고, 풀리기 직전 누른 입력은 풀리는 순간 나간다.
- 피버 중에는 연타가 된다.
- 한 펄스로 여러 마리를 잡으면 점수가 1, 2, 3...으로 붙는다.
- 쉬는 프레이즈에는 한산하고 그 외에는 화면에 10~20마리가 뜬다.
- 색을 바꾸면 코어 안의 도형이 바뀐다.
