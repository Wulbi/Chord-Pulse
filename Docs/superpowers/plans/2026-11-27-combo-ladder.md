# 콤보 사다리 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 화면 콤보가 음악 레이어를 0~6단 사다리로 쌓고, 콤보가 끊기면 바닥 위의 음이 2박에 걸쳐 페이드되며, 게스트 루프가 단이 높을수록 자주 나오게 한다.

**Architecture:** 순수 규칙 `ComboLadder`(콤보→단, 시간→바닥, 단→편성 밀도·콤보 값)와 `ComboFade`(박 위치→페이드 값)를 만든다. `ArrangementTimeline`은 편성 밀도와 콤보 값을 마디 선에 걸린 "단" 하나에서 계산한다. `PatternScheduler`가 음마다 "필요한 단"을 실어 `OneShotScheduler`에 넘기고, 스케줄러가 바닥 위 음의 FMOD 인스턴스 음량을 프레임마다 내린다. FMOD 프로젝트·뱅크·생성 파일은 바꾸지 않는다.

**Tech Stack:** Unity 6 C#, FMOD for Unity 2.02 (런타임 API만), NUnit EditMode, `AudioCapture` 씬 + `FMOD/Tools/analyze_capture.py`.

**Spec:** `Docs/superpowers/specs/2026-11-27-combo-ladder-design.md`

## Global Constraints

- FMOD 프로젝트(`FMOD/ChordPulse/**`), 뱅크, `FMOD/Tools/*.py`(분석기 `analyze_capture.py` 제외), 생성된 `*.g.cs`는 바꾸지 않는다.
- 음의 시작·정지 예약 경로(`setDelay`, 정지 재적용, `OneShotArming`)는 바꾸지 않는다. 페이드는 `EventInstance.setVolume`만 쓴다.
- 기준값 (스펙 그대로): 콤보 기준 `2 / 3 / 5 / 6 / 8 / 10` → 단 1~6. 바닥: 0~30초 0, 30초~2분 1, 2분 이후 2. 페이드 2박, 곡선 `1 - t²`. 게스트 확률 단 0~1 `0.10`, 2 `0.20`, 3 `0.30`, 4 `0.45`, 5 `0.60`, 6 `0.75`; 단 5 이상에서 두 프레이즈 연속 허용.
- 피버 4마디의 음, 피버 전용 레이어, 피버 게스트, 처치 화음은 페이드되지 않는다.
- 구간(템포) 선택, 패턴 변형, 게스트 루프 길이 가중치는 계속 `SoundManager.Intensity`(경과 시간)를 쓴다.
- 테이크: `AssetDatabase.DisallowAutoRefresh()` / `AllowAutoRefresh()`로 감싼다. 녹음 중에는 에디터에 질의하지 않는다 (`execute_code` 금지); `.cpu.txt` 파일이 생길 때까지 파일로 기다린다. 강제하지 않은 100 ms 초과 정지가 있으면 무효, 새 이름으로 다시 녹음한다. 녹음 파일을 덮어쓰거나 지우지 않는다. FMOD Studio는 닫혀 있어야 하고, 직접 종료시키지 않는다.
- 커밋: `git add <파일 경로>`로 그 작업의 파일만. `-A` / `.` / `-u` 금지. 푸시 금지. 메시지 끝에 `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. `git stash / checkout / restore / reset / clean` 금지.
- 테스트 실행: UnityMCP `run_tests` (EditMode). EditMode 전체가 통과해야 한다 (시작 시점 284개).

## Review Focus

1. 페이드 중에 음악이 멈추거나 재시작된다 (게임 오버, 출력 장치 변경) → 페이드 상태가 남지 않고, 새 판의 첫 음이 온전한 음량으로 난다. (Task 4)
2. 콤보가 끊기는 프레임에 단이 바닥과 같다 → 페이드도 취소도 없고, 연주 중인 게스트 루프는 끝까지 간다. (Task 4)
3. 페이드 2박 사이에 마디 선·프레이즈 경계·빌드업 마디가 온다 → 음량이 튀지 않고(0이나 1로 순간 이동 없음) 페이드가 끝까지 간다. (Task 4)
4. 페이드가 끝난 직후, 이미 FMOD에 넘겨졌지만 아직 시작하지 않은 바닥 위의 음 → 들리지 않는다. (Task 1, Task 4)
5. 프레임이 길게 멈춘 뒤(300 ms 이상) 돌아온 첫 프레임이 페이드 구간을 통째로 건너뛴다 → 바닥 위의 음이 남아 울리지 않는다. (Task 4)

---

### Task 1: 원샷 음량 통로와 확인 실험 (관문)

**Files:**
- Modify: `Assets/Scripts/Audio/OneShotScheduler.cs` (`IOneShotBackend`, `OneShotNote`, `OneShotScheduler`, `FmodOneShotBackend`)
- Modify: `Assets/Scripts/Audio/Capture/CaptureScenario.cs`
- Modify: `Assets/Tests/Editor/OneShotSchedulerTests.cs`, `PatternSchedulerTests.cs`, `TransportSchedulingTests.cs` (가짜 백엔드에 새 메서드)
- Modify: `Docs/Captures/README.md`

**Interfaces:**
- Produces:
  - `IOneShotBackend.SetVolume(int handle, float gainDb, float scale)`: 인스턴스 음량을 `10^(gainDb/20) * scale`로. 알 수 없는 핸들은 무시.
  - `OneShotNote.Rung` (`int`, 기본 0): 이 음이 필요로 하는 사다리 단. 생성자에 마지막 선택 인자 `int rung = 0`.
  - `OneShotScheduler.SetFade(int aboveRung, float scale)`: `Rung > aboveRung`인 음 가운데 백엔드에 넘겨진 것 전부에 `SetVolume(handle, GainDb, scale)`을 부르고, 값을 기억해 이후 넘겨지는 음에도 `Start` 직후 같은 호출을 한다. `scale`은 0~1로 자른다.
  - `OneShotScheduler.ClearFade()`: 기억한 값을 버린다 (이후 넘겨지는 음은 `SetVolume`을 받지 않는다). 이미 줄인 음은 건드리지 않는다.
  - `OneShotScheduler.SilenceAbove(int rung)`: `Rung > rung`인 음을 모두 없앤다. 대기 중이면 버리고, 넘겨졌으면 지금 시각에 `Stop` + `Release`. 없앤 수를 돌려준다.
  - `CaptureScenario.fadeProbe` (`bool`, 기본 false): 켜면 매 프레임 `SetFade(-1, 1f)`을 부른다 (모든 음에 같은 음량을 다시 설정).

- [ ] **Step 1: 실패하는 테스트 작성** (`OneShotSchedulerTests.cs`; 그 파일의 `FakeBackend`에 `SetVolume` 호출 기록 `List<(int handle, float gainDb, float scale)> Volumes`를 추가)

```csharp
[Test] public void SetFade_ScalesOnlyHandedNotesAboveTheRung()
// rung 0, 2, 5 세 음을 넘긴 뒤 SetFade(2, 0.5f) → Volumes에 rung 5 음의 핸들만, scale 0.5, gainDb는 그 음의 GainDb

[Test] public void SetFade_AppliesToNotesHandedLater()
// SetFade(0, 0.25f) 뒤에 rung 3 음이 dispatch 되면 Start 직후 SetVolume(handle, gainDb, 0.25f) 한 번; rung 0 음은 호출 없음

[Test] public void ClearFade_StopsScalingNewNotes()
// SetFade(0, 0.25f); ClearFade(); 그 뒤 dispatch 된 rung 3 음은 SetVolume 호출 없음

[Test] public void SilenceAbove_DropsWaitingAndStopsHandedNotes()
// rung 1(대기), rung 4(대기), rung 4(넘겨짐·아직 시작 전), rung 4(울리는 중), rung 0(울리는 중)
// SilenceAbove(1) == 3; rung 4의 두 핸들은 Stop(now)와 Release를 받는다; ActiveCount == 2; 이후 Update가 rung 4 대기 음을 dispatch 하지 않는다

[Test] public void SetFade_ClampsScale()
// SetFade(0, 1.7f) → 기록된 scale 1f; SetFade(0, -0.2f) → 0f
```

- [ ] **Step 2: 실행해 실패 확인** — 컴파일 오류 (`SetFade` 없음).

- [ ] **Step 3: 위 Interfaces를 구현한다**

`FmodOneShotBackend.SetVolume`은 `Start`가 쓰는 것과 같은 식(`Math.Pow(10.0, gainDb / 20.0)`)에 `scale`을 곱해 `instance.setVolume`을 부른다. 다른 테스트 파일의 가짜 백엔드에는 빈 `SetVolume`을 추가한다.

- [ ] **Step 4: 실행해 통과 확인** — EditMode 전체 통과.

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Audio/OneShotScheduler.cs Assets/Tests/Editor/OneShotSchedulerTests.cs Assets/Tests/Editor/PatternSchedulerTests.cs Assets/Tests/Editor/TransportSchedulingTests.cs
git commit -m "audio: one-shot notes carry a ladder rung; scheduler can scale and silence notes above a rung"
```

- [ ] **Step 6: `CaptureScenario.fadeProbe`를 추가하고 확인 실험을 녹음한다**

같은 설정으로 두 번: `spike`, seed 33, 68박, `spikeDispatchBeats` 0.5. 이름 `spike_seed33_cl1_probe_off`, `spike_seed33_cl1_probe_on`. 각각 `analyze_capture.py spike --xcorr`.

- [ ] **Step 7: 관문 판정**

`probe_on`이 통과해야 하는 값: 보정 오차 평균 ≤ 0.10 ms, 최대 ≤ 0.50 ms, missing / late / early 0, xcorr lag 0이 64 / 64, `.cpu.txt`의 late 0 / dropped 0 / RESYNCED 0. `probe_off`와 나란히 `Docs/Captures/README.md`에 "Combo ladder — Task 1 (setVolume probe)" 절로 적는다.

**통과하지 못하면 여기서 멈춘다.** 코드를 고쳐 맞추려 하지 말고, 측정값과 함께 BLOCKED로 보고한다 (스펙 6장: 페이드 방식을 다시 정한다).

- [ ] **Step 8: 커밋**

```bash
git add Assets/Scripts/Audio/Capture/CaptureScenario.cs Docs/Captures/README.md
git commit -m "capture: fadeProbe option; README combo ladder Task 1 (setVolume on armed notes vs start times)"
```

---

### Task 2: 사다리와 페이드의 규칙

**Files:**
- Create: `Assets/Scripts/Audio/ComboLadder.cs` (+ Unity가 만드는 `.meta`)
- Create: `Assets/Tests/Editor/ComboLadderTests.cs` (+ `.meta`)

**Interfaces:**
- Produces (`public static class ComboLadder`):
  - `const int MaxRung = 6; const int GuestRung = 7;` (게스트 루프의 음에 싣는 값: 어떤 바닥보다도 높다)
  - `static readonly int[] ComboThresholds = { 2, 3, 5, 6, 8, 10 };`
  - `static readonly double[] FloorSeconds = { 30.0, 120.0 };`
  - `int RungForCombo(int combo)`; `int FloorForSeconds(double seconds)`; `int Rung(int combo, double seconds)` = 둘 중 큰 값.
  - `int Intensity(int rung)` = `min(3, rung)` (음수는 0); `int ComboLevel(int rung)` = `max(0, rung - 3)`.
  - `int RequiredRung(float intensityMin, int comboMin)`: `comboMin > 0`이면 `3 + comboMin`, 아니면 `intensityMin`이 `> 2`면 3, `> 1`이면 2, `> 0`이면 1, 그 밖은 0.
  - `double GuestChance(int rung)`; `bool GuestsMayRepeat(int rung)` (`rung >= 5`).
- Produces (`public sealed class ComboFade`):
  - `const double Beats = 2.0;`
  - `void Begin(double beat, int fromRung, int floor)`; `void Clear()`;
  - `bool Active`; `int FromRung`; `int Floor`;
  - `bool Finished(double beat)` (Active이고 `beat >= 시작 + Beats`);
  - `float Scale(double beat)`: 비활성 1; 시작 전 1; `t = (beat - 시작) / Beats`를 0~1로 잘라 `1 - t*t`.
  - `float Presence(int requiredRung, int heardRung, double beat)`: `requiredRung > heardRung`이면 0; 비활성이거나 `requiredRung <= Floor`면 1; 그 밖은 `Scale(beat)`.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[Test] public void RungForCombo_FollowsTheThresholds()
{
    var expected = new[] { 0, 0, 1, 2, 2, 3, 4, 4, 5, 5, 6 };   // combo 0..10
    for (int c = 0; c <= 10; c++) Assert.AreEqual(expected[c], ComboLadder.RungForCombo(c), "combo " + c);
    Assert.AreEqual(6, ComboLadder.RungForCombo(500));
    Assert.AreEqual(0, ComboLadder.RungForCombo(-3));
}

[Test] public void Floor_RisesWithTime()
{
    Assert.AreEqual(0, ComboLadder.FloorForSeconds(-1)); Assert.AreEqual(0, ComboLadder.FloorForSeconds(29.9));
    Assert.AreEqual(1, ComboLadder.FloorForSeconds(30)); Assert.AreEqual(1, ComboLadder.FloorForSeconds(119.9));
    Assert.AreEqual(2, ComboLadder.FloorForSeconds(120)); Assert.AreEqual(2, ComboLadder.FloorForSeconds(3600));
    Assert.AreEqual(2, ComboLadder.Rung(0, 200)); Assert.AreEqual(4, ComboLadder.Rung(6, 200)); Assert.AreEqual(1, ComboLadder.Rung(2, 0));
}

[Test] public void Rung_SplitsIntoIntensityAndComboLevel()
{
    var intensity = new[] { 0, 1, 2, 3, 3, 3, 3 }; var combo = new[] { 0, 0, 0, 0, 1, 2, 3 };
    for (int r = 0; r <= 6; r++) { Assert.AreEqual(intensity[r], ComboLadder.Intensity(r)); Assert.AreEqual(combo[r], ComboLadder.ComboLevel(r)); }
}

[Test] public void RequiredRung_IsTheHigherOfTheTwoConditions()
{
    Assert.AreEqual(0, ComboLadder.RequiredRung(0f, 0));
    Assert.AreEqual(1, ComboLadder.RequiredRung(0.5f, 0)); Assert.AreEqual(2, ComboLadder.RequiredRung(1.5f, 0)); Assert.AreEqual(3, ComboLadder.RequiredRung(2.5f, 0));
    Assert.AreEqual(4, ComboLadder.RequiredRung(0f, 1)); Assert.AreEqual(6, ComboLadder.RequiredRung(0.5f, 3));
}

[Test] public void EveryClipOfThePatternTable_HasARungTheLadderCanReach()
{
    // 패턴 표의 모든 클립: 필요한 단에서 Intensity(r) >= IntensityMin 이고 ComboLevel(r) >= ComboMin
    foreach (var c in PatternTable.Clips)
    {
        int r = ComboLadder.RequiredRung(c.IntensityMin, c.ComboMin);
        Assert.LessOrEqual(r, ComboLadder.MaxRung);
        Assert.GreaterOrEqual(ComboLadder.Intensity(r), c.IntensityMin); Assert.GreaterOrEqual(ComboLadder.ComboLevel(r), c.ComboMin);
        if (r > 0) Assert.IsTrue(ComboLadder.Intensity(r - 1) < c.IntensityMin || ComboLadder.ComboLevel(r - 1) < c.ComboMin);
    }
}

[Test] public void GuestChance_RisesWithTheRung()
{
    var expected = new[] { 0.10, 0.10, 0.20, 0.30, 0.45, 0.60, 0.75 };
    for (int r = 0; r <= 6; r++) Assert.AreEqual(expected[r], ComboLadder.GuestChance(r), 1e-9);
    Assert.IsFalse(ComboLadder.GuestsMayRepeat(4)); Assert.IsTrue(ComboLadder.GuestsMayRepeat(5));
}

[Test] public void Fade_ScaleFollowsOneMinusTSquared_OverTwoBeats()
{
    var f = new ComboFade();
    Assert.AreEqual(1f, f.Scale(10)); Assert.IsFalse(f.Active);
    f.Begin(10.0, 5, 1);
    Assert.AreEqual(1f, f.Scale(9.0)); Assert.AreEqual(1f, f.Scale(10.0));
    Assert.AreEqual(0.9375f, f.Scale(10.5), 1e-6f); Assert.AreEqual(0.75f, f.Scale(11.0), 1e-6f); Assert.AreEqual(0.4375f, f.Scale(11.5), 1e-6f);
    Assert.AreEqual(0f, f.Scale(12.0)); Assert.AreEqual(0f, f.Scale(40.0));
    Assert.IsFalse(f.Finished(11.99)); Assert.IsTrue(f.Finished(12.0));
    f.Clear(); Assert.IsFalse(f.Active); Assert.AreEqual(1f, f.Scale(11.0));
}

[Test] public void Fade_PresencePerLayer()
{
    var f = new ComboFade();
    Assert.AreEqual(1f, f.Presence(3, 5, 0)); Assert.AreEqual(0f, f.Presence(6, 5, 0));   // 페이드 없음: 들리는 단 이하면 1, 위면 0
    f.Begin(10.0, 5, 1);
    Assert.AreEqual(1f, f.Presence(1, 5, 11.0)); Assert.AreEqual(1f, f.Presence(0, 5, 11.0));   // 바닥 이하
    Assert.AreEqual(0.75f, f.Presence(4, 5, 11.0), 1e-6f); Assert.AreEqual(0.75f, f.Presence(ComboLadder.GuestRung, ComboLadder.GuestRung, 11.0), 1e-6f);
    Assert.AreEqual(0f, f.Presence(6, 5, 11.0));
}
```

- [ ] **Step 2: 실행해 실패 확인** — 컴파일 오류.
- [ ] **Step 3: `ComboLadder`와 `ComboFade`를 구현한다.**
- [ ] **Step 4: 실행해 통과 확인** — EditMode 전체 통과.
- [ ] **Step 5: 커밋** (새 파일의 `.meta` 포함)

```bash
git add Assets/Scripts/Audio/ComboLadder.cs Assets/Scripts/Audio/ComboLadder.cs.meta Assets/Tests/Editor/ComboLadderTests.cs Assets/Tests/Editor/ComboLadderTests.cs.meta
git commit -m "audio: ComboLadder rules and ComboFade curve"
```

---

### Task 3: 편성이 단에서 나오고, 음이 필요한 단을 싣는다

**Files:**
- Modify: `Assets/Scripts/Audio/ArrangementTimeline.cs`
- Modify: `Assets/Scripts/Audio/PatternScheduler.cs`
- Modify: `Assets/Scripts/Manager/SoundManager.cs` (`CurrentArrangement`와 마디 래치만; 동작은 지금과 같게 유지)
- Test: `Assets/Tests/Editor/ArrangementTimelineTests.cs`, `PatternSchedulerTests.cs`

**Interfaces:**
- Consumes: Task 1 `OneShotNote.Rung`; Task 2 `ComboLadder.Intensity / ComboLevel / RequiredRung / GuestRung`.
- Produces:
  - `ArrangementInputs`: `Intensity`, `LiveCombo`, `AppliedCombo`, `ComboBar`를 지우고 `int LiveRung`(다음 마디부터의 단), `int AppliedRung`(`RungBar`까지의 단, -1 = 없음), `int RungBar`를 넣는다. 선택 필드 `int PinIntensity`, `int PinCombo` (기본 -1; 0 이상이면 사다리 대신 그 값을 쓴다 — 측정 씬용).
  - `ArrangementTimeline.RungAt(int step, in ArrangementInputs x)`: `bar > x.RungBar ? x.LiveRung : max(0, x.AppliedRung)`.
  - `ArrangementTimeline.At`: `Intensity = fever ? MaxIntensity : breath ? 0 : ComboLadder.Intensity(rung)`; `Combo = zone ? 0 : ComboLadder.ComboLevel(rung)`; Pin 값이 0 이상이면 `rung` 자리에 그 값. (빌드업 마디: 편성 밀도는 단에서, 콤보 값 0 — 지금과 같은 모양.)
  - `PatternScheduler`가 넘기는 `OneShotNote.Rung`:
    - 그 음의 스텝 상태가 피버 4마디(`Impact` / `Fever` / `Outro`)면 0.
    - 클립의 `FeatureLoop != 0`이면 `ComboLadder.GuestRung` (위 규칙이 먼저라 피버 게스트는 0).
    - 그 밖은 `ComboLadder.RequiredRung(clip.IntensityMin, clip.ComboMin)`.

이 Task가 끝난 시점의 `SoundManager`는 지금과 같은 소리를 내야 한다. 지금의 `_intensity`와 `_combo`는 서로 독립이라 단 하나로 옮길 수 없으므로, 이 Task에서는 `PinIntensity = _intensity`, `PinCombo = max(0, _appliedCombo)`로 채워 기존 값을 그대로 통과시킨다. (콤보가 바뀐 직후 마디 선 앞의 1.5박에서 예약 내용이 지금과 다를 수 있는 중간 상태다. 콤보의 마디 래치는 Task 4에서 단의 래치로 돌아온다.) 사다리로 바꾸는 것은 Task 4.

- [ ] **Step 1: 실패하는 테스트 작성**

`ArrangementTimelineTests.cs` (기존 테스트가 `Intensity` / `LiveCombo` / `AppliedCombo` / `ComboBar`를 쓰는 곳은 같은 뜻의 Pin 또는 Rung 입력으로 고친다):

```csharp
[Test] public void Rung_GivesIntensityAndCombo_LatchedOnTheBarLine()
// AppliedRung 2, RungBar 3, LiveRung 5: 스텝 24..31(마디 3) → Intensity 2, Combo 0; 스텝 32(마디 4) → Intensity 3, Combo 2

[Test] public void Breath_ZeroesIntensity_KeepsTheComboPart()
// BreathCurrent, 단 5 → Intensity 0, Combo 2

[Test] public void BuildBar_TakesIntensityFromTheRung_ComboZero()
// FeverStart 64, 단 5: 스텝 56..63 → Intensity 3, Combo 0, Fever == Build;  단 1이면 Intensity 1
// 스텝 64..95 → Intensity 3 (MaxIntensity), Combo 0 — 단과 무관 (단 0으로도 같다)

[Test] public void Pin_OverridesTheLadder()
// PinIntensity 1, PinCombo 3, 단 0 → Intensity 1, Combo 3 (피버 4마디와 breath 규칙은 그대로 적용)

[Test] public void FirstDifferentStep_SeesARungDrop()
// before: AppliedRung 5, RungBar 3, LiveRung 5;  after: AppliedRung 1, RungBar 3, LiveRung 1 → FirstDifferentStep(…, 24, 40) == 24
```

`PatternSchedulerTests.cs` (그 파일의 `FakeBackend`가 받은 음의 단을 볼 수 없으므로, 스케줄러의 `NoteScheduled` 대신 `OneShotScheduler`에 넘어간 `OneShotNote.Rung`을 기록하는 길을 테스트 쪽에 만든다: `PatternScheduler`에 `public event Action<PatternNote, double, int> NoteRung`을 추가하거나 `NoteScheduled`에 단 인자를 더한다 — 하나를 골라 일관되게):

```csharp
[Test] public void Notes_CarryTheRungTheirClipNeeds()
// 단 6 상태로 한 프레이즈를 예약: ComboMin 2 클립의 음은 5, IntensityMin 0.5 클립의 음은 1, 조건 없는 클립의 음은 0

[Test] public void FeverPhraseNotes_AreRungZero_EvenFromGatedClips()
// 피버 4마디 상태(Intensity 3)에서 IntensityMin 2.5 클립의 음 → 0

[Test] public void GuestLoopNotes_CarryGuestRung_ButNotInTheFeverPhrase()
// FeatureLoop n 상태의 그 루프 음 → ComboLadder.GuestRung;  피버 게스트(피버 4마디 상태) → 0
```

- [ ] **Step 2: 실행해 실패 확인.**
- [ ] **Step 3: 구현한다.**
- [ ] **Step 4: 실행해 통과 확인** — EditMode 전체 통과.
- [ ] **Step 5: 소리가 그대로인지 테이크로 확인**

`baseline` seed 7 128박을 `baseline_seed7_cl3`으로 녹음하고, 같은 설정의 직전 기준 테이크 `baseline_seed7_s9f`와 `analyze_capture.py rms --compare`: 모든 창이 ±0.1 dB 안. (`s9f`가 없으면 이 Task를 시작하기 전 커밋에서 기준 테이크를 먼저 녹음하지 말고, Task 1 직후의 커밋에서 `baseline_seed7_cl1`을 녹음한 것으로 대신한다 — 이 경우 그렇게 했다고 README에 적는다.)

- [ ] **Step 6: 커밋**

```bash
git add Assets/Scripts/Audio/ArrangementTimeline.cs Assets/Scripts/Audio/PatternScheduler.cs Assets/Scripts/Manager/SoundManager.cs Assets/Tests/Editor/ArrangementTimelineTests.cs Assets/Tests/Editor/PatternSchedulerTests.cs Docs/Captures/README.md
git commit -m "audio: arrangement intensity and combo come from one bar-latched rung; pattern notes carry their rung"
```

---

### Task 4: SoundManager가 사다리를 굴린다 (콤보, 바닥, 페이드)

**Files:**
- Modify: `Assets/Scripts/Manager/SoundManager.cs`
- Modify: `Assets/Scripts/Audio/FeatureSelector.cs` (`CancelCurrent`만)
- Modify: `Assets/Scripts/ChordPulsePrototype.cs` (콤보를 넣는 한 줄, `_killStreak` 정리)
- Modify: `Assets/Scripts/Audio/Capture/CaptureScenario.cs` (`sound.Combo = 3` 등을 새 API로)
- Create: `Assets/Scripts/Audio/LadderState.cs` (+ `.meta`), `Assets/Tests/Editor/LadderStateTests.cs` (+ `.meta`)

**Interfaces:**
- Consumes: Task 1 `SetFade / ClearFade / SilenceAbove`; Task 2 전부; Task 3 `ArrangementInputs.LiveRung / AppliedRung / RungBar / PinIntensity / PinCombo`.
- Produces (`public sealed class LadderState` — `SoundManager`가 쓰는 순수 상태 기계; Unity·FMOD 타입 없음):
  - `void Reset()`;
  - `Step Update(int combo, double seconds, double beat, int bar, bool feverPhrase)` — 프레임마다 한 번. 돌려주는 `Step`(구조체): `bool FadeStarted`, `bool FadeEnded`, `int Floor`(FadeEnded일 때 `SilenceAbove`에 줄 값), `float Scale`(지금 페이드 값; 페이드 없으면 1).
  - 읽기: `int LiveRung`, `int AppliedRung`, `int RungBar`, `int HeardRung`, `int Floor`, `ComboFade Fade`.
  - 규칙:
    - 마디가 바뀌면 `AppliedRung = LiveRung`, `RungBar = bar` (페이드 중에는 `AppliedRung`을 올리지 않는다).
    - 평소 `LiveRung = ComboLadder.Rung(combo, seconds)`.
    - **끊김**: 직전 프레임의 `combo > 0`이고 이번 `combo == 0`일 때, `feverPhrase`가 아니고 `HeardRung > Floor`면 `Fade.Begin(beat, HeardRung, Floor)`; 페이드 동안 `LiveRung`과 `AppliedRung`은 끊기기 전 `HeardRung`으로 묶인다.
    - **끝**: `Fade.Finished(beat)`인 프레임에 `AppliedRung = Floor`, `LiveRung = ComboLadder.Rung(combo, seconds)` (페이드 중에 새로 쌓은 단은 다음 마디 선에서 들어온다), `Fade.Clear()`, `FadeEnded = true`.
    - `feverPhrase`(피버 4마디)인 동안 끊겨도 페이드를 시작하지 않는다. 피버가 끝난 첫 프레임의 단은 그때의 `combo`와 바닥에서 바로 정한다 (`AppliedRung`도 그 값).
    - `HeardRung` = 페이드 중이면 `Fade.FromRung`, 아니면 `max(0, AppliedRung)`.
- Produces (`SoundManager` 공개):
  - `int ComboCount { get; set; }` — 게임이 화면 콤보를 넣는다 (음수는 0). 기존 `Combo` 세터는 지운다.
  - `int LadderRung`(= `HeardRung`), `int LadderFloor`, `float ComboFadeScale`,
  - `float LayerPresence(int requiredRung)` (= `ComboFade.Presence(requiredRung, LadderRung, 지금 박)`), `float GuestPresence` (페이드 중이면 `ComboFadeScale`, 아니면 1).
  - `void PinArrangement(int intensity, int combo)` — 측정 씬용; 둘 다 -1이면 해제. `StartMusic`이 해제한다.
  - `CurrentCombo` = `ComboLadder.ComboLevel(LadderRung)`(피버 구간 0), `ArrangementIntensity` = 지금처럼 피버 최대 / 쉬는 프레이즈 0 / 그 밖 `ComboLadder.Intensity(LadderRung)`. Pin이 걸려 있으면 Pin 값.
- Produces (`FeatureSelector.CancelCurrent()`): `Current = null` (최근 기억은 그대로).

`SoundManager`의 프레임 순서 (지금의 `UpdateFever` 자리): `LadderState.Update` → `FadeStarted`이거나 페이드 중이면 `Shots.SetFade(Floor, Scale)` → `FadeEnded`면 `Shots.SetFade(Floor, 0f)`, `Shots.SilenceAbove(Floor)`, `Shots.ClearFade()`, 게스트 루프가 연주 중이거나 예약돼 있으면 `_features.CancelCurrent()` + `_features.CancelNext()`, 그리고 `RefreshArrangement(now)` → `CurrentArrangement()`가 `LiveRung / AppliedRung / RungBar`를 싣는다. `StopMusic`과 `StartMusic`은 `LadderState.Reset()`과 `Shots.ClearFade()`를 부른다. `IKillBedState.ComboAt`은 `ComboLadder.ComboLevel(RungAt(step))`.

`ChordPulsePrototype`: `_soundManager.Combo = MusicLayerLevel(_killStreak);` → `_soundManager.ComboCount = _pulseCombo;`. `_killStreak`, `KillStreakLayerThresholds`, `MusicLayerLevel`은 다른 사용처가 없으면 지운다.

`CaptureScenario`: 기존 시나리오는 **기존 테이크와 같은 편성**을 내야 한다. 지금 `sound.Combo`와 `sound.Intensity`에 넣는 값(넣지 않으면 `StartMusic`이 둔 0)을 코드에서 읽어, 시나리오마다 그 (편성 밀도, 콤보 값) 조합을 `PinArrangement`로 고정한다. heavy는 지금 `sound.Combo = 3`만 넣는다. `intensity >= 0` 옵션도 `PinArrangement`로 옮긴다 (`sound.Intensity`는 선택 규칙용으로 그대로 넣는다).

- [ ] **Step 1: 실패하는 테스트 작성** (`LadderStateTests.cs`; 한 박 = `beat` 1, 한 마디 = 4박, `bar = floor(beat / 4)`로 프레임을 흉내 내는 헬퍼 `Run(state, combo, seconds, fromBeat, toBeat, stepBeats = 0.05)`)

```csharp
[Test] public void Rises_OnTheNextBarLine()
// combo 6을 beat 1.0부터 넣는다 → beat 3.9까지 HeardRung 0, beat 4.0부터 4

[Test] public void TwoThresholdsInOneBar_RiseTogether()
// beat 1: combo 2, beat 2: combo 3 → beat 4.0에 HeardRung 2

[Test] public void Break_FadesForTwoBeats_ThenDropsToTheFloor()
// seconds 40 (바닥 1), HeardRung 5에서 beat 10.0에 combo 0:
//  그 프레임 FadeStarted, Step.Floor 1; beat 10.0..11.95 HeardRung 5, Scale == ComboFade 곡선
//  beat 12.0 프레임 FadeEnded, Step.Floor 1; 이후 HeardRung 1, AppliedRung 1, Scale 1

[Test] public void Break_AtTheFloor_DoesNothing()            // Review Focus 2
// seconds 200 (바닥 2), combo 3 (콤보 단 2 == 바닥)에서 combo 0 → FadeStarted 한 번도 없음, HeardRung 2 유지

[Test] public void Fade_RunsAcrossABarLine_WithoutChangingTheHeardRung()   // Review Focus 3
// beat 11.0에서 끊김 (마디 선 12.0이 페이드 안): beat 12.0..12.95 HeardRung은 여전히 끊기기 전 값, Scale 단조 감소

[Test] public void ComboRebuiltDuringTheFade_EntersOnTheBarAfterItEnds()
// beat 10.0 끊김, beat 11.0에 combo 2, 11.5에 combo 3 → beat 12.0 FadeEnded 후 HeardRung == 바닥, beat 16.0부터 max(바닥, 2)

[Test] public void LongStall_SkippingTheWholeFade_StillEndsIt()   // Review Focus 5
// beat 10.0 끊김 프레임 다음 프레임이 beat 14.0 → 그 프레임에 FadeEnded, HeardRung == 바닥

[Test] public void BreakInsideTheFeverPhrase_StartsNoFade_AndFeverEndSetsTheRung()
// feverPhrase true 동안 combo 12 → 0: FadeStarted 없음; feverPhrase false가 된 첫 프레임 HeardRung == Rung(그때 combo, seconds)

[Test] public void FloorRise_EntersOnTheNextBarLine()
// combo 0, seconds가 29 → 31로 넘어가는 프레임이 beat 5.0 → beat 8.0부터 HeardRung 1

[Test] public void Reset_ClearsAFadeInProgress()             // Review Focus 1
// 페이드 중 Reset() → Fade.Active false, HeardRung 0, 다음 Update의 Scale 1
```

- [ ] **Step 2: 실행해 실패 확인.**
- [ ] **Step 3: `LadderState`를 구현하고 `SoundManager` / `FeatureSelector.CancelCurrent` / `ChordPulsePrototype` / `CaptureScenario`를 위와 같이 잇는다.**

`RefreshArrangement`가 이미 "바뀐 첫 스텝부터 두 스케줄러를 무효화"하므로, 페이드가 끝난 프레임에 `AppliedRung`이 내려간 입력으로 한 번 부르면 바닥 위의 예약 음이 다시 서술되지 않는다. 무효화의 안전 구간 안에 남은 바닥 위의 음은 `SilenceAbove`가 이미 없앴다 (Review Focus 4).

- [ ] **Step 4: 실행해 통과 확인** — EditMode 전체 통과, 콘솔 컴파일 오류 0.

- [ ] **Step 5: 고정 편성 테이크가 그대로인지 확인**

`heavy` seed 7 128박을 `heavy_seed7_cl4`로 녹음, `heavy_seed7_s9f`와 `rms --compare`: 모든 창 ±0.1 dB. 다르면 `PinArrangement`의 값이 틀린 것이다 (고친다).

- [ ] **Step 6: 커밋**

```bash
git add Assets/Scripts/Audio/LadderState.cs Assets/Scripts/Audio/LadderState.cs.meta Assets/Tests/Editor/LadderStateTests.cs Assets/Tests/Editor/LadderStateTests.cs.meta Assets/Scripts/Manager/SoundManager.cs Assets/Scripts/Audio/FeatureSelector.cs Assets/Scripts/ChordPulsePrototype.cs Assets/Scripts/Audio/Capture/CaptureScenario.cs Docs/Captures/README.md
git commit -m "audio: the screen combo drives the arrangement ladder; a broken combo fades the layers above the floor"
```

---

### Task 5: 게스트 루프 빈도가 단을 따른다

**Files:**
- Modify: `Assets/Scripts/Audio/FeatureSelector.cs`
- Modify: `Assets/Scripts/Manager/SoundManager.cs` (`_features.Advance` 호출부)
- Create: `Assets/Tests/Editor/FeatureSelectorTests.cs` (+ `.meta`); 기존 `ArrangementTimelineTests.cs`의 `FeatureSelector` 사용처를 새 시그니처로

**Interfaces:**
- Consumes: Task 2 `ComboLadder.GuestChance / GuestsMayRepeat`; Task 4 `SoundManager.LadderRung`, `FeatureSelector.CancelCurrent`.
- Produces: `FeatureSelector.Advance(SectionSelector.Phrase next, int energy, bool allowed, int rung, int excludeId = 0)`.
  - 확률은 `ComboLadder.GuestChance(rung)` (상수 `Chance` 삭제).
  - `Current != null`일 때: `ComboLadder.GuestsMayRepeat(rung)`이면 뽑을 수 있고(같은 루프는 기존 규칙대로 연달아 나오지 않는다), 아니면 지금처럼 뽑지 않는다.
  - `FirstPhrase`, 쉬는 프레이즈, `allowed`, 피버 전용 루프 제외는 그대로.
- `SoundManager`는 프레이즈가 시작될 때(지금 `Advance`를 부르는 자리)의 `LadderRung`을 넘긴다. Pin이 걸려 있으면 `ComboLadder.MaxRung`이 아니라 **3** (지금의 32%에 가장 가까운 30%; 기존 측정 시나리오의 게스트 빈도를 크게 바꾸지 않는다)을 넘긴다.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
static double Share(int rung, int phrases = 4000)   // 게스트가 뽑힌 프레이즈의 비율 (처음 2프레이즈 제외, 쉬는 프레이즈 없음, energy 1)

[Test] public void Chance_FollowsTheRung()
// rung 0: 0.10 ± 0.03;  rung 3: 연속 금지 때문에 p/(1+p) = 0.231 ± 0.03;  rung 4: 0.45/1.45 = 0.310 ± 0.03

[Test] public void HighRungs_AllowGuestsInARow_NeverTheSameLoopTwice()
// rung 6: 비율 0.75 ± 0.03; 연속 두 프레이즈에 같은 Id가 한 번도 없다

[Test] public void LowRungs_NeverTwoInARow()
// rung 4, 4000프레이즈: Current != null인 프레이즈 다음에 Next != null이 한 번도 없다

[Test] public void CancelCurrent_ClearsOnlyTheCurrentLoop()
// Current != null, Next == X 상태에서 CancelCurrent() → Current == null, Next == X

[Test] public void FirstTwoPhrases_Breath_AndNotAllowed_StillGiveNoGuest()
// rung 6에서도 처음 2프레이즈, next.Breath, allowed false는 Next == null
```

(연속 금지일 때의 기대 비율: 게스트 뒤 프레이즈는 반드시 비므로 정상 상태 비율은 `p / (1 + p)`. rung 0~1은 `0.10 / 1.10 = 0.091`이므로 `0.10 ± 0.03` 안이다.)

- [ ] **Step 2: 실행해 실패 확인.**
- [ ] **Step 3: 구현한다.**
- [ ] **Step 4: 실행해 통과 확인** — EditMode 전체 통과.
- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Audio/FeatureSelector.cs Assets/Scripts/Manager/SoundManager.cs Assets/Tests/Editor/FeatureSelectorTests.cs Assets/Tests/Editor/FeatureSelectorTests.cs.meta Assets/Tests/Editor/ArrangementTimelineTests.cs
git commit -m "audio: guest loops come more often on higher rungs; in a row from rung 5"
```

---

### Task 6: 측정 — 콤보를 쌓고 끊는 테이크

**Files:**
- Modify: `Assets/Scripts/Audio/Capture/CaptureScenario.cs`
- Modify: `FMOD/Tools/analyze_capture.py`
- Modify: `Docs/Captures/README.md`

**Interfaces:**
- Consumes: Task 4 `SoundManager.ComboCount`, `LadderRung`, `LadderFloor`, `ComboFadeScale`.
- Produces:
  - `CaptureScenario.Scenario.combobreak`: 처치를 넣지 않는다. `sound.ComboCount`를 박에 맞춰 직접 넣는다: 박 4부터 박마다 +1 (10에서 멈춤), `comboBreakBeats`(문자열, 기본 `"40.5"`)의 박에 0으로, 그 뒤 다시 박마다 +1. `MusicMix` 그대로, 게스트 루프는 나오지 않게(`FeatureSelector`가 뽑지 못하도록 이 시나리오에서 기존 옵션으로 막거나, 막는 옵션이 없으면 `featuresOff` 불 옵션을 추가). 프레임마다 `.ladder.csv`에 `frame, beat, combo, rung, floor, scale`을 적는다.
  - `analyze_capture.py ladderfade <take> --break-beat B`: `.beats.csv`로 박 위치를 잡아 끊긴 박 전 2박, 페이드 2박(반 박 창 4개), 그 뒤 2박의 RMS를 버스 구분 없이 낸다. 그리고 같은 설정의 "끊지 않은" 테이크와 `--reference`로 비교해 창마다 dB 차이를 낸다. 페이드 창에서 순간 피크(인접 1 ms 창 대비 +12 dB 초과)를 세어 `clicks`로 낸다.

- [ ] **Step 1: 시나리오와 분석 명령을 구현한다.** (분석기 단위 확인: 합성 신호 — 1 kHz 사인에 `1 - t²` 포락선 — 에 대해 `ladderfade`가 네 창의 기대 dB(창 평균)를 ±0.5 dB 안으로 내는지 `--selftest` 플래그로 확인하고 그 출력을 README에 적는다.)

- [ ] **Step 2: 테이크 녹음** (seed 7, 64박, `roundSecondsOffset`이 없으므로 바닥은 0)

| 이름 | 설정 |
|---|---|
| `combobreak_seed7_cl6_ref` | `comboBreakBeats` 비움 (끊지 않음) |
| `combobreak_seed7_cl6_b405` | `"40.5"` (마디 안) |
| `combobreak_seed7_cl6_b47` | `"47.0"` (페이드가 마디 선 48을 넘는다) |
| `combobreak_seed7_cl6_floor` | `"40.5"`, 그리고 콤보를 2까지만 올린다 (단 1) → 페이드는 플럭만 |
| `combobreak_seed7_cl6_rung0` | `PinArrangement(0, 0)` (끝까지 단 0: "다 빠진 뒤"의 기준) |

- [ ] **Step 3: 기준과 대조** (스펙 7장; README에 표로)

| 기준 | 통과 조건 |
|---|---|
| 페이드 2박 동안 단조 감소 | `ref` 대비 차이가 네 반 박 창에서 순서대로 더 내려간다 (각 창 ≤ 앞 창) |
| 그 뒤 0 | 페이드 뒤 2박의 RMS가, "단 0만 연주하는" 구간(`ref`의 박 0~3: 콤보 0)과 같은 악기 구성의 수준 — `PinArrangement(0, 0)`으로 녹음한 `combobreak_seed7_cl6_rung0` 테이크의 같은 박과 ±1 dB |
| 바닥 이하는 그대로 | `floor` 테이크에서 끊기 전후로 킥 박(정박)의 첫 30 ms RMS가 `ref`와 ±1 dB |
| 클릭 없음 | `clicks` 0 (세 테이크 모두) |
| 타이밍 | `.cpu.txt` late 0 / dropped 0 / RESYNCED 0; `.ladder.csv`의 `rung`이 끊긴 박 + 2.0박(프레임 오차 1프레임)에 바닥으로 |
| 마디 선을 넘는 페이드 | `b47`에서 박 48 앞뒤 20 ms의 RMS 차이가 `ref`의 같은 자리 차이 + 페이드 기울기 이내 (튐 없음): 수치와 함께 판정 |

**통과하지 못한 기준은 "NOT met"으로 수치와 함께 적는다.** 맞추려고 기준이나 분석기를 바꾸지 않는다. 원인을 찾을 수 있으면 systematic-debugging으로 고치고 다시 녹음한다 (새 이름).

- [ ] **Step 4: 커밋**

```bash
git add Assets/Scripts/Audio/Capture/CaptureScenario.cs FMOD/Tools/analyze_capture.py Docs/Captures/README.md
git commit -m "capture: combobreak scenario and ladderfade analysis; README combo ladder measurement"
```

---

### Task 7: 연출이 같은 곡선으로 빠진다

**Files:**
- Modify: `Assets/Scripts/Visual/BedLayerVisuals.cs`, `Assets/Scripts/Visual/FeatureLoopVisuals.cs`
- Modify (읽어 보고 해당하면): `Assets/Scripts/Visual/BedStyleVisuals.cs`, `Assets/Scripts/Visual/MusicReactiveVisuals.cs`
- Modify: `Docs/AdaptiveAudioPlan.md` (사다리 절 추가), `Docs/MusicVisualsPlan.md` (연출 표에 한 줄)

**Interfaces:**
- Consumes: Task 4 `SoundManager.LayerPresence(int requiredRung)`, `GuestPresence`, `LadderRung`.

규칙:
- 레이어 하나를 그리는 요소(콤보 하이햇 틱, 패드, 벨, 플럭, 오픈 하이햇, 클랩, 스타일 악기)는 그 레이어의 필요한 단으로 `LayerPresence`를 곱한다. 단은 `ComboLadder.RequiredRung(그 레이어 클립의 IntensityMin, ComboMin)`과 같아야 한다: 숫자를 새로 적지 말고, 그 연출이 이미 보고 있는 조건(`CurrentCombo >= n`, `ArrangementIntensity >= n`)을 "필요한 단 `3 + n` / `n`의 presence > 0"으로 바꾸고 밝기·크기에 presence를 곱한다.
- `BedLayerVisuals`가 콤보 단계에 쓰는 자체 1박 페이드(`_combo[level]`의 `MoveTowards`)는 **들어올 때만** 쓰고, 나갈 때는 presence를 그대로 따른다 (소리보다 먼저 사라지지 않게).
- `FeatureLoopVisuals`의 게스트 연출은 `GuestPresence`를 곱한다. 페이드가 끝나 `CurrentFeature`가 null이 되면 지금처럼 끝낸다.
- 킥 펄스, 처치 연출, 피버 연출, 긴장도·피격 비네트는 건드리지 않는다.

- [ ] **Step 1: 네 파일을 읽고, 레이어별로 "무엇을 보고 그리는지 → 어떤 단"의 표를 보고서에 만든다.**
- [ ] **Step 2: presence를 곱한다.** (MonoBehaviour라 EditMode 테스트는 없다. 순수 계산은 Task 2의 `ComboFade.Presence` 테스트가 덮는다.)
- [ ] **Step 3: 컴파일과 테스트 확인** — 콘솔 오류 0, EditMode 전체 통과.
- [ ] **Step 4: 문서 두 곳을 고친다.** `AdaptiveAudioPlan.md`에는 사다리 표, 기준 콤보, 바닥, 페이드, 게스트 확률과 "FMOD 변경 없음: 인스턴스 음량"을 적는다.
- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Visual/BedLayerVisuals.cs Assets/Scripts/Visual/FeatureLoopVisuals.cs Docs/AdaptiveAudioPlan.md Docs/MusicVisualsPlan.md
# BedStyleVisuals.cs / MusicReactiveVisuals.cs 는 바꿨을 때만 추가
git commit -m "visual: layers and guest effects fade with the combo ladder"
```

- [ ] **Step 6: 사용자 확인 항목을 보고서에 적는다 (직접 하지 않는다)**

`ChordPulsePrototype` 씬 Play: 콤보 2 / 3 / 5 / 6 / 8 / 10에서 마디 선마다 소리가 한 겹씩 붙는가; 틀린 색으로 끊으면 약 2박에 걸쳐 소리와 연출이 같이 사그라지는가; 킥·베이스는 그대로인가; 30초 뒤에는 끊겨도 플럭이 남는가; 피버 중 실수해도 피버 음악이 그대로인가; 콤보가 높을 때 게스트 루프가 자주 나오는가; 게임 오버 → 재시작 뒤 첫 마디가 정상인가.
