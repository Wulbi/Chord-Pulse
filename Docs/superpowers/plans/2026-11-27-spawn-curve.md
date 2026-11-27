# 적 등장 곡선 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 스폰 수치를 시간대별 단계 표 하나로 옮겨, 0~15초 학습 구간과 10분까지 오르는 난이도, 한 자리에 최대 3마리 스폰을 만든다.

**Architecture:** `GameplayTuning.SpawnStages`(시작 시간순 줄들)를 `SpawnDirector`가 라운드 시간으로 골라 간격·뭉치 길이·특수 적 확률·한 자리 마리 수를 읽는다. `SpawnOrder.Count`가 한 자리의 마리 수를 전달하고, `ChordPulsePrototype`이 그만큼 내보낸다. 피버, 쉬는 프레이즈, 색 소개 3마리의 규칙은 그대로다.

**Tech Stack:** Unity 6 C#, NUnit EditMode 테스트 (`Assets/Tests/Editor`).

**Spec:** `Docs/superpowers/specs/2026-11-27-spawn-curve-design.md`

## Global Constraints

- 간격은 어느 줄에서도 1박(2스텝)보다 짧지 않다 (펄스 쿨타임 1박).
- 한 자리의 적은 1~3마리, 같은 색, 같은 속도. 특수 적은 그중 첫 마리뿐이고 나머지는 기본 적.
- 돌격형, 색 소개 3마리, 피버, 쉬는 프레이즈의 자리는 항상 1마리.
- 음악 편성 밀도용 `SpawnDirector.Difficulty()`와 `GameplayTuning.DifficultyThresholds`는 바꾸지 않는다.
- 적 속도, 펄스 쿨타임, 체력, 특수 적 자체의 규칙은 바꾸지 않는다.
- 커밋: `git add <파일 경로>`로 그 작업의 파일만. `-A` / `.` / `-u` 금지. 푸시 금지. 메시지 끝에 `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`.
- `git stash / checkout / restore / reset / clean` 금지. 작업 트리에 다른 세션의 커밋되지 않은 변경이 있을 수 있다.
- 테스트 실행: UnityMCP `run_tests` (EditMode). 연결되지 않으면 `execute_code`로 `TestRunnerApi`를 실행한다. EditMode 전체가 통과해야 한다.

## Review Focus

1. 라운드 시간이 표의 마지막 줄을 한참 넘는다 (예: 3600초) → 마지막 줄을 계속 쓴다. (Task 1)
2. 라운드 시간이 음수이거나 뒤로 간다 (출력 장치 변경으로 음악이 재시작) → 첫 줄을 쓰고 예외가 없다. (Task 1)
3. 줄이 바뀌는 순간에 진행 중이던 뭉치 → 남은 길이는 그대로 두고, 간격과 확률만 새 줄을 따른다. 예외나 음수 길이가 없다. (Task 2)
4. 한 자리 3마리가 나올 때 적 풀이 바닥났다 → 풀이 주는 만큼만 나오고 예외가 없다. 화면에 없는 적이 상태 목록에 남지 않는다. (Task 3)
5. 화음·회색·분열 자리에 2~3마리가 걸린다 → 첫 마리만 특수, 나머지는 같은 색의 기본 적. (Task 3)

---

### Task 1: 단계 표와 줄 고르기

**Files:**
- Modify: `Assets/Scripts/Gameplay/GameplayTuning.cs`
- Modify: `Assets/Scripts/Gameplay/SpawnDirector.cs`
- Test: `Assets/Tests/Editor/SpawnDirectorTests.cs`

**Interfaces:**
- Produces:
  - `public readonly struct SpawnStage` (`GameplayTuning.cs` 안, 최상위 타입): `double StartSeconds; int IntervalSteps; int RunMin; int RunMax; double PairChance; double TripleChance; double HiddenChance; double ChordPairChance; double ChargerChance; double SplitterChance;` 생성자는 이 순서의 인자 10개.
  - `GameplayTuning.SpawnStages` (`static readonly SpawnStage[]`), `GameplayTuning.MaxGroupSize = 3`.
  - `public static int SpawnDirector.StageIndex(double roundSeconds)`: `StartSeconds <= roundSeconds`인 마지막 줄의 인덱스. 어떤 줄보다 이르면 0.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
static SpawnStage Stage(double seconds) => GameplayTuning.SpawnStages[SpawnDirector.StageIndex(seconds)];

[Test]
public void StageIndex_PicksTheLastStartedRow()
{
    Assert.AreEqual(0, SpawnDirector.StageIndex(-5));
    Assert.AreEqual(0, SpawnDirector.StageIndex(0));
    Assert.AreEqual(0, SpawnDirector.StageIndex(14.99));
    Assert.AreEqual(1, SpawnDirector.StageIndex(15));
    Assert.AreEqual(GameplayTuning.SpawnStages.Length - 1, SpawnDirector.StageIndex(3600));
}

[Test]
public void SpawnStages_KeepTheRulesOfTheTable()
{
    var rows = GameplayTuning.SpawnStages;
    Assert.AreEqual(0.0, rows[0].StartSeconds);
    for (int i = 0; i < rows.Length; i++)
    {
        var r = rows[i];
        if (i > 0) Assert.Greater(r.StartSeconds, rows[i - 1].StartSeconds, "row " + i);
        Assert.GreaterOrEqual(r.IntervalSteps, SpawnDirector.StepsPerBeat, "row " + i);   // pulse cooldown = 1 beat
        Assert.GreaterOrEqual(r.RunMin, 1, "row " + i);
        Assert.GreaterOrEqual(r.RunMax, r.RunMin, "row " + i);
        Assert.LessOrEqual(r.PairChance + r.TripleChance, 1.0, "row " + i);
        foreach (double p in new[] { r.PairChance, r.TripleChance, r.HiddenChance, r.ChordPairChance, r.ChargerChance, r.SplitterChance })
        { Assert.GreaterOrEqual(p, 0.0, "row " + i); Assert.LessOrEqual(p, 1.0, "row " + i); }
    }
}

[Test]
public void SpawnStages_LearningRowsHaveNoSpecialsAndNoGroups()
{
    foreach (double t in new[] { 0.0, 20.0 })
    {
        var r = Stage(t);
        Assert.AreEqual(0.0, r.PairChance + r.TripleChance + r.HiddenChance + r.ChordPairChance + r.ChargerChance + r.SplitterChance);
    }
    Assert.Greater(Stage(0).IntervalSteps, Stage(20).IntervalSteps);
    Assert.Greater(Stage(20).IntervalSteps, Stage(40).IntervalSteps);
}
```

- [ ] **Step 2: 실행해 실패 확인** — `SpawnDirectorTests`, 컴파일 오류(`SpawnStage` 없음)로 실패.

- [ ] **Step 3: `SpawnStage`, `SpawnStages`, `MaxGroupSize`, `StageIndex` 구현**

표의 값 (스펙 5.1 그대로, 확률은 0~1):

| StartSeconds | IntervalSteps | RunMin | RunMax | Pair | Triple | Hidden | ChordPair | Charger | Splitter |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 4 | 2 | 3 | 0 | 0 | 0 | 0 | 0 | 0 |
| 15 | 3 | 3 | 4 | 0 | 0 | 0 | 0 | 0 | 0 |
| 30 | 2 | 4 | 5 | 0.15 | 0 | 0.15 | 0 | 0 | 0 |
| 60 | 2 | 4 | 6 | 0.30 | 0 | 0.20 | 0.30 | 0.15 | 0.05 |
| 120 | 2 | 4 | 6 | 0.40 | 0.10 | 0.25 | 0.40 | 0.22 | 0.08 |
| 180 | 2 | 4 | 5 | 0.45 | 0.20 | 0.28 | 0.45 | 0.28 | 0.12 |
| 300 | 2 | 3 | 5 | 0.45 | 0.30 | 0.30 | 0.50 | 0.33 | 0.15 |
| 420 | 2 | 3 | 4 | 0.40 | 0.40 | 0.33 | 0.55 | 0.37 | 0.18 |
| 600 | 2 | 3 | 4 | 0.35 | 0.50 | 0.35 | 0.60 | 0.40 | 0.20 |

표 위에 한국어 주석으로 열의 뜻과 "숫자는 출발값, 조정은 스펙 6장"을 적는다. 기존 상수는 이 Task에서 지우지 않는다.

- [ ] **Step 4: 실행해 통과 확인** — EditMode 전체 통과.

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Gameplay/GameplayTuning.cs Assets/Scripts/Gameplay/SpawnDirector.cs Assets/Tests/Editor/SpawnDirectorTests.cs
git commit -m "gameplay: spawn stage table and StageIndex"
```

---

### Task 2: SpawnDirector가 표를 쓴다

**Files:**
- Modify: `Assets/Scripts/Gameplay/SpawnDirector.cs`
- Modify: `Assets/Scripts/Gameplay/GameplayTuning.cs`
- Test: `Assets/Tests/Editor/SpawnDirectorTests.cs`

**Interfaces:**
- Consumes: Task 1의 `SpawnStage`, `GameplayTuning.SpawnStages`, `SpawnDirector.StageIndex`, 테스트 헬퍼 `Stage(seconds)`.
- Produces: `TryNext`의 시그니처는 그대로. `SpawnDirector.DensityLevel` 삭제. `GameplayTuning`에서 삭제: `FlowIntervalSteps`, `RunMin`, `RunMax`, `HiddenEnemyStart`, `HiddenEvery`, `DenseSection`, `SpecialEnemyStart`, `ChordPairChance`, `ChargerStart`, `SplitterStart`, `ChargerChance`, `SplitterChance`. `SpawnContext.Section`은 남기되 `SpawnDirector`가 읽지 않는다 (필드 주석에 그렇게 적는다).

동작 (스펙 5.2, 5.5):
- 매 `TryNext`에서 `stage = SpawnStages[StageIndex(context.RoundSeconds)]`.
- 흐름 간격 = `stage.IntervalSteps`; 새 뭉치 길이 = `[stage.RunMin, stage.RunMax]`; 돌격형 뒤 간격도 `stage.IntervalSteps`.
- 화음: 새 뭉치가 시작될 때 `stage.ChordPairChance`. 피버 직후 첫 뭉치 금지는 그대로.
- 회색: 화음이 아닌 자리마다 `stage.HiddenChance` (스폰 번호 주기 `_spawnNumber`는 없앤다).
- 분열: 화음도 회색도 아닌 자리마다 `stage.SplitterChance`.
- 돌격: 뭉치가 끝날 때 `stage.ChargerChance` (쉬는 프레이즈 제외는 그대로).
- 확률이 0이면 난수를 뽑지 않는다.
- 쉬는 프레이즈(간격 `BreathIntervalSteps`, 길이 `BreathRunMax`), 피버, 색 소개 3마리는 그대로.

- [ ] **Step 1: 테스트를 새 규칙으로 고쳐 쓴다**

삭제: `DensityLevel_CombinesDifficultyAndSection`, `NoSpecialKinds_BeforeTheirStartTimes`, `Pairs_AreRoughlyHalfOfRunStarts`, `Flow_Level3_SpawnsEveryBeatWithinARun`.

고치기: `GameplayTuning.RunMin[3]` / `RunMax[3]`을 쓰는 곳(`Runs_AreOneColor_AndNeighbouringRunsDiffer`, `Chargers_AreFastPlainAndOutsideRuns`)은 그 테스트의 `seconds`에 맞는 `Stage(seconds).RunMin` / `.RunMax`로. `Chargers_AreFastPlainAndOutsideRuns`의 비율 단언은 `Stage(120).ChargerChance ± 0.08`로. `Splitters_AreRunMembers_NeverGrayOrPairs`의 비율 단언은 아래 `Splitters_FollowTheTable`로 옮기고 그 테스트에는 "회색도 화음도 아니다"만 남긴다. `Runs_AreOneColor_…`는 `NoChargers(...)`로 감싼다 (60초 줄에 돌격형이 있다).

추가:

```csharp
[Test]
public void Flow_UsesTheRowsInterval()
{
    foreach (double t in new[] { 10.0, 20.0, 60.0 })
    {
        _dir.Reset();
        var s = AfterOpening(Collect(0, 800, Ctx(seconds: t)));
        Assert.AreEqual(Stage(t).IntervalSteps, MinGap(s), "seconds " + t);
        Assert.LessOrEqual(MaxGap(s), Stage(t).IntervalSteps + GameplayTuning.RunGapSteps, "seconds " + t);
    }
}

[Test]
public void LearningRow_ShortRunsNoSpecials()
{
    var s = AfterOpening(Collect(0, 800, Ctx(seconds: 10)));
    Assert.IsTrue(s.All(x => x.order.Kind == EnemyKind.Basic && !x.order.Hidden && x.order.SecondColorIndex < 0));
    var runs = RunsOf(s);
    foreach (var r in runs.Skip(1).Take(runs.Count - 2))
    { Assert.GreaterOrEqual(r.Length, 2); Assert.LessOrEqual(r.Length, 3); }
}

[Test]
public void NoSpecials_BeforeTheirRows()
{
    Assert.IsTrue(Collect(0, 2000, Ctx(seconds: 29)).All(x => !x.order.Hidden));
    _dir.Reset();
    Assert.IsTrue(Collect(0, 2000, Ctx(seconds: 59)).All(x => x.order.Kind == EnemyKind.Basic && x.order.SecondColorIndex < 0));
}

[Test]
public void Hidden_FollowsTheTable()
{
    var s = NoChargers(AfterOpening(Collect(0, 8000, Ctx(seconds: 120)))).Where(x => x.order.SecondColorIndex < 0).ToList();
    double share = s.Count(x => x.order.Hidden) / (double)s.Count;
    Assert.AreEqual(Stage(120).HiddenChance, share, 0.05);
}

[Test]
public void Pairs_FollowTheTable()
{
    var s = NoChargers(AfterOpening(Collect(0, 8000, Ctx(seconds: 120))));
    double share = s.Count(x => x.order.SecondColorIndex >= 0) / (double)RunsOf(s).Count;
    Assert.AreEqual(Stage(120).ChordPairChance, share, 0.08);
}

[Test]
public void Splitters_FollowTheTable()
{
    var eligible = NoChargers(AfterOpening(Collect(0, 8000, Ctx(seconds: 300))))
        .Where(x => !x.order.Hidden && x.order.SecondColorIndex < 0).ToList();
    double share = eligible.Count(x => x.order.Kind == EnemyKind.Splitter) / (double)eligible.Count;
    Assert.AreEqual(Stage(300).SplitterChance, share, 0.04);
}

[Test]
public void LateRows_HaveMoreSpecialsThanEarlyRows()
{
    int Specials(double t)
    {
        _dir.Reset();
        return Collect(0, 8000, Ctx(seconds: t)).Count(x => x.order.Hidden || x.order.SecondColorIndex >= 0 || x.order.Kind != EnemyKind.Basic);
    }
    int at60 = Specials(60), at180 = Specials(180), at600 = Specials(600);
    Assert.Greater(at180, at60);
    Assert.Greater(at600, at180);
}

[Test]
public void Section_NoLongerChangesSpawns()
{
    var a = Collect(0, 800, Ctx(section: 0, seconds: 60));
    _dir = new SpawnDirector(new System.Random(1234));
    var b = Collect(0, 800, Ctx(section: 3, seconds: 60));
    CollectionAssert.AreEqual(Steps(a), Steps(b));
    CollectionAssert.AreEqual(Colors(a), Colors(b));
}

[Test]
public void RowChange_MidRun_KeepsSpawning()   // Review Focus 3
{
    for (int seed = 0; seed < 20; seed++)
    {
        var dir = new SpawnDirector(new System.Random(seed));
        int count = 0;
        for (int step = 0; step < 2000; step++)
            if (dir.TryNext(Ctx(step: step, seconds: step * 0.4), out var o)) { count++; Assert.GreaterOrEqual(o.ColorIndex, 0); }
        Assert.Greater(count, 300, "seed " + seed);
    }
}

[Test]
public void Difficulty_StillFollowsItsThresholds()
{
    Assert.AreEqual(0, SpawnDirector.Difficulty(0));
    Assert.AreEqual(2, SpawnDirector.Difficulty(40));
    Assert.AreEqual(3, SpawnDirector.Difficulty(60));
}
```

- [ ] **Step 2: 실행해 실패 확인** — 새 테스트 다수가 실패하거나(예: `Flow_UsesTheRowsInterval`의 10초), 삭제 전 상수 때문에 통과하는 것이 섞인다. `LearningRow_ShortRunsNoSpecials`와 `Section_NoLongerChangesSpawns`가 실패해야 한다.

- [ ] **Step 3: `SpawnDirector.TryNext`를 위 동작대로 고치고, `DensityLevel`과 위에 적은 `GameplayTuning` 상수를 지운다**

`ChordPulsePrototype.cs`는 이 상수들을 쓰지 않는다 (`EnemyPoolSize`만 쓴다). 지운 뒤 컴파일 오류가 나는 다른 파일이 있으면 멈추고 보고한다.

- [ ] **Step 4: 실행해 통과 확인** — EditMode 전체 통과.

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Gameplay/SpawnDirector.cs Assets/Scripts/Gameplay/GameplayTuning.cs Assets/Tests/Editor/SpawnDirectorTests.cs
git commit -m "gameplay: SpawnDirector reads interval, run length and special chances from the stage table"
```

---

### Task 3: 한 자리에 최대 3마리, 풀 96

**Files:**
- Modify: `Assets/Scripts/Gameplay/SpawnDirector.cs`
- Modify: `Assets/Scripts/Gameplay/GameplayTuning.cs` (`EnemyPoolSize` 48 → 96)
- Modify: `Assets/Scripts/ChordPulsePrototype.cs` (`TryNext` 호출부, 약 410행; `CreateEnemyView`)
- Test: `Assets/Tests/Editor/SpawnDirectorTests.cs`

**시작 조건:** `git status --short Assets/Scripts/ChordPulsePrototype.cs`가 비어 있어야 한다. 변경이 남아 있으면 그 파일을 건드리지 말고 BLOCKED로 보고한다 (다른 세션의 작업).

**Interfaces:**
- Consumes: Task 1의 `SpawnStage.PairChance` / `TripleChance`, `GameplayTuning.MaxGroupSize`.
- Produces: `SpawnOrder.Count` (`int`, 1..3): 이 자리에서 나올 적의 수. `Hidden` / `SecondColorIndex` / `Kind`는 첫 마리에만 해당하고, 나머지는 같은 `ColorIndex`와 같은 `Speed`의 기본 적이다.

동작:
- 흐름의 뭉치 자리: 난수 하나 `r`로 `r < TripleChance` → 3, `r < TripleChance + PairChance` → 2, 아니면 1. 두 확률이 모두 0이면 뽑지 않고 1.
- 돌격형, 색 소개 3마리, 피버, 쉬는 프레이즈의 자리: 항상 1.
- 뭉치 길이는 자리 수로 센다 (마리 수가 아니다).

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[Test]
public void Count_IsOne_WhereGroupsAreNotAllowed()
{
    Assert.IsTrue(Collect(0, 2000, Ctx(seconds: 29)).All(x => x.order.Count == 1));
    _dir.Reset();
    Assert.IsTrue(Collect(0, 400, Ctx(fever: true, seconds: 600)).All(x => x.order.Count == 1));
    _dir.Reset();
    Assert.IsTrue(Collect(0, 4000, Ctx(breath: true, seconds: 600)).All(x => x.order.Count == 1));
    _dir.Reset();
    var s = Collect(0, 8000, Ctx(seconds: 600));
    Assert.IsTrue(s.Take(3).All(x => x.order.Count == 1));
    Assert.IsTrue(s.Where(x => x.order.Kind == EnemyKind.Charger).All(x => x.order.Count == 1));
}

[Test]
public void Count_FollowsTheTable()
{
    var s = NoChargers(AfterOpening(Collect(0, 8000, Ctx(seconds: 600))));
    Assert.IsTrue(s.All(x => x.order.Count >= 1 && x.order.Count <= GameplayTuning.MaxGroupSize));
    Assert.AreEqual(Stage(600).PairChance, s.Count(x => x.order.Count == 2) / (double)s.Count, 0.05);
    Assert.AreEqual(Stage(600).TripleChance, s.Count(x => x.order.Count == 3) / (double)s.Count, 0.05);
}

[Test]
public void Count_DoesNotChangeRunLengthOrInterval()
{
    var s = NoChargers(AfterOpening(Collect(0, 8000, Ctx(seconds: 600))));
    Assert.AreEqual(Stage(600).IntervalSteps, MinGap(s));
    var runs = RunsOf(s);
    foreach (var r in runs.Skip(1).Take(runs.Count - 2))
    { Assert.GreaterOrEqual(r.Length, Stage(600).RunMin); Assert.LessOrEqual(r.Length, Stage(600).RunMax); }
}
```

- [ ] **Step 2: 실행해 실패 확인** — 컴파일 오류(`SpawnOrder.Count` 없음).

- [ ] **Step 3: `SpawnOrder.Count`와 그 추첨을 `SpawnDirector`에 구현한다** (`MakeOrder`가 만드는 모든 주문은 `Count`가 1 이상).

- [ ] **Step 4: 실행해 통과 확인** — EditMode 전체 통과.

- [ ] **Step 5: `ChordPulsePrototype`이 `Count`만큼 내보낸다**

`TryNext`가 참일 때: 주문 그대로 한 마리를 내고, 이어서 `Count - 1`마리를 `Hidden = false`, `SecondColorIndex = -1`, `Kind = EnemyKind.Basic`으로 바꾼 같은 주문으로 낸다. 모두 `SpawnFromNewDirection`을 거치므로 방향은 지금 규칙(직전 스폰과 60도 이상 차이)을 따른다.

풀 고갈 (Review Focus 4): `CreateEnemyView`가 쓰는 풀 호출(`_poolManager`)이 풀이 비었을 때 무엇을 돌려주는지 읽는다. 오브젝트를 받지 못할 수 있으면, 그 경우 `SpawnFromNewDirection`이 그 적의 상태를 `_enemyStates`에 넣지 않고 조용히 건너뛰게 한다. 풀이 스스로 늘어나는 구조면 코드를 바꾸지 않고 보고서에 그렇게 적는다.

`GameplayTuning.EnemyPoolSize`를 96으로 바꾼다.

- [ ] **Step 6: 컴파일과 테스트 확인** — Unity 콘솔에 컴파일 오류 없음, EditMode 전체 통과.

- [ ] **Step 7: 커밋**

```bash
git add Assets/Scripts/Gameplay/SpawnDirector.cs Assets/Scripts/Gameplay/GameplayTuning.cs Assets/Scripts/ChordPulsePrototype.cs Assets/Tests/Editor/SpawnDirectorTests.cs
git commit -m "gameplay: up to three same-colour enemies per spawn slot; enemy pool 96"
```

- [ ] **Step 8: 사용자 확인 항목을 보고서에 적는다 (직접 하지 않는다)**

`ChordPulsePrototype` 씬을 Play 해서 볼 것: 0~15초에 적이 2박마다 한 마리씩 나온다; 30초 뒤 같은 색 2마리가 같은 박에 다른 방향에서 나와 펄스 하나에 같이 잡힌다; 2분 뒤 3마리 자리가 보인다; 콘솔에 예외가 없다.
