# 음악 시계를 Unity로 (타임라인 점프 제거) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 프레이즈 경계에서 소리와 박이 끊기지 않도록, 박 시계를 FMOD 타임라인 콜백에서 FMOD 믹서 DSP 클럭 위의 Unity 계산으로 옮기고 음악 이벤트(타임라인·구간 점프)를 없앤다.

**Architecture:** 세 부분이다. Part A는 타임라인을 그대로 둔 채 연출·게임이 읽는 박만 `BeatClock`(DSP 클럭 기반, 점프 착지를 넘어 외삽) + 프레임 보간으로 바꾼다. Part B는 진행 중인 3~8단계 계획을 고쳐 코어 드럼까지 예약 원샷으로 옮기게 한다. Part C는 타임라인에 들리는 노트가 없어진 뒤, 스스로 가는 `MusicTransport`로 `MusicConductor`를 대체하고 음악 이벤트를 삭제한다.

**Tech Stack:** Unity 6 + FMOD for Unity 2.02 (Studio API), FMOD Studio 2.02.35 (`fmodstudiocl.exe`), Python 3 + numpy (분석), Unity Test Framework (EditMode, `Assets/Tests/Editor/`, asmdef 없음).

**Spec:** `Docs/superpowers/specs/2026-11-25-unity-music-clock-design.md`. 함께 읽을 것: 3~8단계 계획 `Docs/superpowers/plans/2026-11-25-fmod-scheduled-layers-stages-3-8.md`(이하 "옛 계획"), 측정 방법 `Docs/Captures/README.md`.

## Global Constraints

- 박 대비 어택 오차: 평균 ≤ 2 ms, 최대 ≤ 5 ms (템포 전환 직후 4박 포함).
- 원샷은 절대 늦게 울리지 않는다: 시작 시각이 지난 노트는 버린다. 이 계획부터는 **일찍 울리는 노트도 0**이어야 한다.
- 프레이즈 경계: 첫 반 박 RMS가 이전 프레이즈 같은 자리 대비 ±1 dB(편성이 같은 경계끼리), 20 ms 넘는 무음 0곳.
- 화면 박(`SoundManager.BeatPosition`): 뒤로 간 적 0회, 프레임별 증가량이 `dt × bpm / 60`과 최대 0.05박 차이 (프레임 정지 0회 테이크). 또한 박 증가량이 기대값의 25% 미만인 프레임이 30 ms 넘게 이어지면 안 된다(`analyze_capture.py frames`의 hold 기준).
- CPU: Studio update 평균 ≤ 11%, Core dsp 평균 baseline ≤ 6% / heavy ≤ 12% (코어 드럼 원샷 포함). 넘으면 그 Task에서 멈추고 측정 표와 함께 보고.
- `Assets/Scripts/Visual/*`와 `ChordPulsePrototype.cs`는 고치지 않는다(다른 세션이 작업 중). `SoundManager` 공개 멤버의 이름과 뜻을 유지한다. 예외: 쓰는 곳이 없는 `CurrentSection`은 Task 6에서 삭제.
- 템포 구간 선택(`SectionSelector`), 피버 정렬, 편성 결정 규칙은 바꾸지 않는다.
- 모든 캡처는 Editor, seed 7(128박) · seed 33(68박), `stalls >100 ms` 0인 테이크만 유효(강제 정지 테이크 제외). 결과는 `Docs/Captures/README.md`에 표로 남긴다.
- FMOD CLI 규칙은 옛 계획 Global Constraints 그대로(스크립트와 `-build`는 별도 명령, 뱅크를 `Assets/StreamingAssets`로 복사, Studio는 닫은 상태).
- 작업 트리에 다른 세션의 커밋 안 된 변경이 있다. 커밋은 각 Task가 적은 파일만 `git add <파일>`로 올린다(`git add -A` 금지).
- EditMode 테스트 실행: Unity MCP `run_tests`(mode EditMode). 컴파일 오류는 `read_console`로 확인.

## Review Focus

1. **룩어헤드보다 긴 정지가 프레이즈 경계를 넘는 경우**(2초 정지 등) — 시계는 제 위치로 가 있어야 하고, 지나친 프레이즈마다 구간·스타일 선택이 정확히 한 번씩 진행돼야 하며, 밀린 노트가 몰아서 울리면 안 된다. → Task 5 `Update_LongGap_RaisesEachPhraseOnce`, Task 7 강제 정지 2초 테이크.
2. **피버가 뒤늦게 다음 구간을 붙잡음**(`HoldNext`) — 경계 너머 템포가 이미 알린 값에서 바뀌고, 경계 너머로 이미 넘긴 노트는 새 템포로 다시 예약돼야 한다. 경계를 지난 뒤의 변경은 무시돼야 한다. → Task 5 `SetNextTempo_ReplacesUntilBoundary`, `SetNextTempo_AfterBoundary_AppliesToFollowingPhrase`, Task 6 Step 4 피버 테이크.
3. **DSP 클럭이 뒤로 감 / 샘플레이트 변경**(출력 장치 전환) — 예외·정지 없이 다음 마디 첫 박부터 다시 맞아야 한다. → Task 5 `Update_DspWentBackwards_ResyncsAtNextBar`, Task 8 Step 2 수동 확인.
4. **게임 오버 직후 재시작**(`StopMusic` → `StartMusic`) — 박은 시작 전 -1, 프레이즈 0부터, 이전 판의 예약·꼬리 없음. → Task 5 `Stop_ThenStart_ResetsPhraseAndBeat`, Task 8 수동 Play.
5. **넘긴 직후 프레임이 멈춤** — 인스턴스를 시작한 뒤 시작 시각을 걸기 전에 프레임이 멈추면 노트가 일찍 울릴 수 있다(한 마디 앞서 넘기는 코어 드럼). → Task 7 강제 정지 테이크의 `early` 집계.

---

# Part A — 연출·게임 박을 DSP 클럭 기반으로 (지금, 옛 계획과 독립)

### Task 1: `DspClockSmoother` + 화면 박 원천 교체

**Files:**
- Create: `Assets/Scripts/Audio/DspClockSmoother.cs`
- Modify: `Assets/Scripts/Audio/BeatClock.cs` (`BeatAt`에 `double` 오버로드)
- Modify: `Assets/Scripts/Manager/SoundManager.cs` (`Update`의 `_visualBeat` 계산, `StartMusic`의 초기화)
- Test: `Assets/Tests/Editor/DspClockSmootherTests.cs`, `Assets/Tests/Editor/BeatClockTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class DspClockSmoother { public DspClockSmoother(int sampleRate, int maxAheadSamples = 2048); public double Sample(ulong rawDsp, double seconds); public void Reset(); public void Reset(int sampleRate); }` — 순수 클래스. `rawDsp`는 메인 스레드에서 읽은 마스터 DSP 클럭(믹서 블록 단위로만 바뀜), `seconds`는 단조 시계(`Stopwatch`). 반환 = 보간된 DSP 샘플(소수). 규칙: 반환값은 뒤로 가지 않는다. `rawDsp`가 멈춰도 마지막 raw 값 + `maxAheadSamples`를 넘지 않는다. `rawDsp`가 이전 raw보다 작아지면(클럭 리셋) 내부 상태를 버리고 그 값에서 다시 시작한다.
  - `public bool BeatClock.BeatAt(double dspClock, out double beat)` — 기존 `ulong` 오버로드와 같은 식. `ulong` 오버로드는 이것을 부른다.
- `SoundManager` 변경: `_visualBeat = max(직전 _visualBeat, Clock.BeatAt(smoother.Sample(MasterDsp(), now)) − visualLatency박)`. `BeatAt`이 false(첫 앵커 전)면 지금처럼 `_conductor.BeatPosition()` 기반 값을 쓴다. `_audioBeat`(내부 타이밍용), `UpdatePhrase`, 파라미터 쓰는 시점, `MusicConductor`는 **건드리지 않는다**. `StartMusic`에서 smoother `Reset`.
- 알려진 한계(Part C에서 해소): 점프 착지에서 화면 박은 경계를 제때 넘지만 `NextPhraseStep`·`HeardStyle`·`CurrentFeature` 등 프레이즈 상태는 착지 콜백이 올 때까지(최대 약 0.8초) 이전 프레이즈 값이다.

- [ ] **Step 1: 실패하는 테스트**
  - `DspClockSmootherTests` (48000 Hz, raw = `floor(48000·t / 1024) · 1024`, 144 fps로 2초 샘플링):
    - `Sample_IsMonotonic` → 모든 연속 반환값 `b >= a`.
    - `Sample_StaysWithinOneBlockOfTrueClock` → 처음 0.1초 이후 `|반환 − 48000·t| <= 1024`.
    - `Sample_FrameIncrements_AreSmooth` → 처음 0.1초 이후 프레임별 증가량이 `[0, 2 · dt · 48000]`.
    - `Sample_MixerStall_CapsExtrapolation` → raw를 200 ms 동안 고정 → 반환 `<= 고정 raw + 2048`.
    - `Sample_RawWentBackwards_Restarts` → raw 480000 뒤 raw 1024 → 반환 `< 4096`, 예외 없음.
  - `BeatClockTests.BeatAt_Double_MatchesUlong` → `Anchor(0, 0, 120)` 후 `BeatAt(24000.0)` = `BeatAt(24000UL)` = 1.0; `BeatAt(24000.5)`는 1.0보다 크고 1.0001보다 작다.
- [ ] **Step 2:** `run_tests` → FAIL (타입 없음).
- [ ] **Step 3:** `DspClockSmoother`, `BeatAt(double)` 구현 → 위 테스트 PASS.
- [ ] **Step 4:** `SoundManager` 연결. 컴파일 오류 0, 기존 EditMode 테스트 전부 PASS.
- [ ] **Step 5:** Commit `audio: visual beat from the DSP clock, smoothed between mixer blocks` (파일: 위 5개 + `.meta`).

### Task 2: 화면 박 측정 + 64박 지연 진단

**Files:**
- Modify: `Assets/Scripts/Audio/Capture/CaptureScenario.cs` (프레임 로그, 파라미터 로그, 고정 옵션)
- Modify: `Assets/Scripts/Manager/SoundManager.cs` (음악 이벤트 파라미터 쓰기를 한 헬퍼로 모음)
- Modify: `FMOD/Tools/analyze_capture.py` (`frames`, `landings` 하위 명령)
- Modify: `Docs/Captures/README.md`

**Interfaces:**
- Produces (C#):
  - `SoundManager`: 모든 `_music.setParameterByName(...)` 호출을 `FMOD.RESULT SetMusicParameter(string name, float value, bool ignoreSeek = false)`로 모으고, 성공 시 `public event Action<string, float> MusicParameterSet`을 부른다. 동작 변화 없음.
  - `CaptureScenario`: 매 프레임 `(Time.unscaledDeltaTime, sound.BeatPosition, sound.CurrentBpm)`을 모아 `<take>.frames.csv`(`dt,beat,bpm`)로, `MusicParameterSet`을 `<take>.params.csv`(`beat_now,name,value`; `beat_now`는 `Clock.BeatAt(MasterDsp())`)로 저장. 인스펙터 `holdArrangement`(bool): 켜면 매 프레임 리플렉션으로 `_variationNext = 0`, `_styleNext = 0`을 쓰고 `_features.CancelNext()`를 불러 프레이즈 경계에서 바뀌는 편성 파라미터를 없앤다.
- Produces (Python):
  - `python FMOD/Tools/analyze_capture.py frames <wav>` → `.frames.csv`에서 뒤로 간 횟수, `|Δbeat − dt·bpm/60|` 최대값(박)과 그 박 위치, 0.05 초과 프레임 수. 초과가 있으면 exit 1.
  - `python FMOD/Tools/analyze_capture.py landings <wav>` → 16박마다: 직전 박에서 예상한 경계 프레임 대비 박 로그 지연(ms), 첫 반 박 RMS, 다음 반 박 RMS, 이전 프레이즈 같은 자리 RMS, 경계 뒤 1박 안의 20 ms 이상 무음 구간(임계 -70 dBFS). 스펙 1장 표와 같은 열.

- [ ] **Step 1:** `landings` 구현 → `Docs/Captures/baseline_seed7_jumpcheck.wav`에서 스펙 1장 표의 값(48: +350 ms / -46.1 dB, 80: +321 ms / 무음, 96: +823 ms, 64: +95 ms)이 ±1 ms · ±0.1 dB로 나오는지 확인.
- [ ] **Step 2:** `frames` 구현, `CaptureScenario` 로그 추가. baseline seed 7 녹음(`suffix` `_a2`) → `frames`: 뒤로 간 횟수 0, 초과 프레임 0(경계 48·80·96 포함). 실패하면 Task 1의 보간을 고치고 다시 녹음.
- [ ] **Step 3:** 이전 상태와의 비교는 새로 녹음하지 않고 기존 테이크로 한다(다른 세션의 변경이 작업 트리에 있으므로 `git stash`·체크아웃으로 옛 코드를 되살리지 않는다). `landings`로 Step 2 테이크의 박 로그 지연(타임라인 콜백 기준이라 48·80·96에서 여전히 수백 ms)과 `frames` 결과(화면 박은 같은 경계에서 초과 0)를 README에 나란히 적는다.
- [ ] **Step 4: 64박 진단.** Step 2 테이크의 `.params.csv`에서 박 63.0~64.5 사이에 쓰인 파라미터와 박 15~16.5, 111~112.5(지연 없는 경계) 사이에 쓰인 파라미터를 비교해 64에만 있는 것을 적는다. 이어서 `holdArrangement` 켠 테이크(`suffix` `_a2_hold`) 녹음 → `landings`의 64박 지연.
  - 지연이 사라짐(≤ 10 ms): 원인 = 타임라인 노트를 게이트하는 편성 파라미터 전환. 옛 계획이 그 파라미터를 없애므로 조치 없음, README에 결론.
  - 지연이 남음: 원인 미상. **여기서 멈추고** 두 테이크의 `landings` 표와 `.params.csv` 차이를 사용자에게 보고한다(Part C 뒤에도 남을 수 있음).
- [ ] **Step 5:** 수동 Play(`ChordPulsePrototype` 씬): 구간 전환 순간 펄스·연출이 굳지 않는지 확인. 소리 공백은 아직 남는 것이 정상.
- [ ] **Step 6:** README에 "Part A" 절(표 + 진단 결론) → Commit `capture: per-frame beat log, landing metric and beat-64 diagnosis`.

---

# Part B — 옛 계획 수정: 코어 드럼까지 원샷으로

### Task 3: 3~8단계 계획 문서 고치기 (옛 계획 실행 전에)

**Files:**
- Modify: `Docs/superpowers/plans/2026-11-25-fmod-scheduled-layers-stages-3-8.md`

문서만 고친다. 고칠 곳과 내용:

- [ ] **Step 1: 머리말.** `**Spec:**` 줄 아래에 한 줄 추가: "시계와 코어 드럼은 `Docs/superpowers/specs/2026-11-25-unity-music-clock-design.md`가 이 계획의 결정을 대체한다 — Task 10·12·15와 '타임라인 점프 정지' 절 참고." Goal의 "음악 이벤트를 '시계 + 코어 드럼'만 남긴 1,000노트 이하로 줄인다"를 "음악 이벤트의 들리는 노트를 0으로 줄인다(시계 전환과 이벤트 삭제는 `2026-11-25-unity-music-clock.md` Part C)"로.
- [ ] **Step 2: "타임라인 점프 정지" 절.** 본문을 "원인은 타임라인 점프로 확인됐고(위 스펙 1장) 해법은 시계 전환이다. 이 계획의 캡처는 `landings`로 착지 지연을 기록만 한다."로 교체. Task 11 Step 4를 "`landings`로 착지 지연·공백을 README에 기록(판단·보고 없음)"으로.
- [ ] **Step 3: Task 10 Interfaces.** 생성자를 `PatternScheduler(OneShotScheduler shots, IArrangement arrangement, double lookaheadBeats = 1.0, ulong layerMask = ulong.MaxValue, double dispatchWindowBeats = 0)`로. 설명 추가: `layerMask`의 비트 = `PatternClip.Layer`, 꺼진 레이어의 클립은 건너뛴다. `dispatchWindowBeats > 0`이면 `KillChordPlanner.DispatchWindow(beat)` 대신 그 값을 넘김 창으로 쓴다. 테스트 추가: `LayerMask_SkipsOtherLayers`(마스크에 없는 레이어 노트 0), `DispatchWindow_Override_HandsOverEarly`(창 4.0 → 박 4 노트가 now 0에서 백엔드로 넘어감).
- [ ] **Step 4: Task 12.** 제목을 "스타일 드럼 + 코어 드럼"으로, 아래를 교체:
  - Files에서 `core_drums.py`/`core_drums.js` 줄 삭제. `pattern_drums.py`에 코어 레이어 추가: `Core kick`(박 0·2마다, 옛 `Kick - beats 1 and 3` 값), `Core snare`(옛 `Snare - beats 2 and 4`), `Core hat`(옛 `Eighth-note hats`의 닫힌 햇). `patterns.py`의 `LEGACY_TRACKS`에 이 세 타임라인 트랙 추가(다른 트랙처럼 `LegacyLayers`로 게이트).
  - "결정 (확인 필요): 코어 드럼" 문단을 교체: "코어 드럼도 패턴 클립이다. 조건 — `Core kick`: `Fever` 마스크 = `None | Impact | Fever | Outro`(빌드업 마디에서만 꺼짐). `Core snare`·`Core hat`: `StyleMask` = `STYLE_USES_CORE_BACKBEAT`가 참인 스타일 비트(신스팝만; 피버 구간은 `ArrangementState.Style`이 이미 신스팝). 이벤트 파라미터 `CoreKick`·`CoreBackbeat`는 만들지 않는다."
  - 생성물 추가: `PatternTable.CoreLayerMask`(`ulong`, 코어 세 레이어의 비트), `PatternTable.CoreLookaheadBeats = 4.0`.
  - `SoundManager`: `PatternScheduler` 두 개 — `_patterns`(`layerMask = ~PatternTable.CoreLayerMask`, 룩어헤드 1.0)와 `_corePatterns`(`layerMask = CoreLayerMask`, 룩어헤드 4.0, `dispatchWindowBeats` 4.0). `Invalidate`·`Reset`·`Update`는 둘 다에 호출.
  - 테스트 추가: `PatternSchedulerTests.CoreKick_SilentInBuildBar`(Fever = Build인 마디에 킥 노트 0), `CoreBackbeat_OnlyInSynthpop`(스타일 1에서 Core snare·hat 노트 0), `Core_LookaheadIsOneBar`(now 0에서 박 4의 킥이 예약됨).
  - 캡처 기준 추가: `legacyLayers` 켠/끈 테이크의 Music Bed 솔로 RMS ±1 dB에 코어 드럼 포함, 킥 박(짝수 박) 어택 오차 기준 통과.
- [ ] **Step 5: Task 15.** 본문 맨 위에 추가: "이 Task는 `LEGACY_TRACKS` 삭제·옛 파라미터 삭제·생성기 정리(Step 1의 `cleanup_music`, Files의 Delete 목록, `SoundManager`의 옛 파라미터 쓰기 제거)까지만 한다. 음악 이벤트 자체·`NextSection`·`Spike ref`·`MusicConductor`는 남긴다 — `2026-11-25-unity-music-clock.md` Part C가 지운다. Step 1의 노트 수 기준은 `≤ 200`(Spike ref 128 + 0), Step 3 최종 캡처와 Step 4 확장성 확인은 그대로." 그리고 Step 4의 "`StyleUsesCoreBackbeat` 한 칸"을 "`STYLE_USES_CORE_BACKBEAT` 한 칸"으로.
- [ ] **Step 6:** "이 계획 밖 (후속)"의 점프 정지 항목을 "→ `2026-11-25-unity-music-clock.md` Part C"로.
- [ ] **Step 7:** Commit `docs: stages 3-8 plan follows the Unity music clock spec (core drums as patterns)`.

### Task 4: 옛 계획 실행

- [ ] 옛 계획 Task 1~15를 그 문서대로 실행한다(Task 3에서 고친 내용 포함). 이 계획의 Task가 아니라 관문이다: **Task 15까지 끝나 음악 이벤트에 들리는 노트가 없고 `legacyLayers`가 제거된 뒤에만 Part C를 시작한다.**
- [ ] 옛 계획 Task 12 끝의 CPU 예산 확인은 코어 드럼을 포함한 값으로 한다. 넘으면 거기서 멈추고 보고.

---

# Part C — 시계 전환

### Task 5: `MusicTransport` (순수 로직)

**Files:**
- Create: `Assets/Scripts/Audio/MusicTransport.cs`
- Test: `Assets/Tests/Editor/MusicTransportTests.cs`

**Interfaces:**
- Consumes: `BeatClock`(`Anchor`, `SetTempoFrom`, `DspAt`, `BeatAt`, `Reset`).
- Produces:
  ```csharp
  public sealed class MusicTransport
  {
      public const int BeatsPerBar = 4;
      public const int BeatsPerPhrase = 16;
      public MusicTransport(BeatClock clock);
      public bool Running { get; }
      /// 박 0이 dspStart에 울린다. nextBpm = 박 16부터의 템포.
      public void Start(ulong dspStart, double bpm, double nextBpm);
      public void Stop();                       // Running = false, clock.Reset(), Phrase = -1
      /// NextPhraseBeat부터의 템포. 그 경계를 지나기 전까지는 다시 불러 바꿀 수 있다.
      public void SetNextTempo(double bpm);
      /// 매 프레임. 지나친 프레이즈 경계마다 PhraseStarted를 한 번씩, 순서대로 부른다.
      public void Update(ulong dspNow);
      public int Phrase { get; }                // dspNow가 속한 프레이즈, 박 0 전에는 -1
      public int NextPhraseBeat { get; }        // (Phrase + 1) * 16, 박 0 전에는 0
      public double Bpm { get; }                // 지금 울리는 프레이즈의 템포
      public bool BeatAt(double dsp, out double beat);   // 박 0 전에는 음수 박
      public event System.Action<int> PhraseStarted;     // 인자 = 프레이즈 번호
      public event System.Action Resynced;               // DSP 클럭 이상으로 다시 맞춤
  }
  ```
- 규칙:
  - 프레이즈 경계의 DSP 샘플은 정수로 한 번 확정한다: `boundary[n+1] = boundary[n] + round(16 · 60 / bpm[n] · sampleRate)`. 구현은 `PhraseStarted(n)`을 부르기 전에 `clock.Anchor(n · 16, boundary[n], bpm[n])`으로 앵커를 경계에 옮기고, `SetNextTempo`는 `clock.SetTempoFrom(NextPhraseBeat, bpm)`을 부른다.
  - `PhraseStarted` 핸들러가 `SetNextTempo`를 부르지 않으면 다음 프레이즈도 같은 템포다.
  - `Update`에서 `dspNow`가 직전 `Update`의 값보다 작으면: 현재 박을 직전 값 기준으로 구해 다음 마디 첫 박 `B`(4의 배수)를 고르고, `clock.Anchor(B, dspNow + round(0.1 · sampleRate), Bpm)`으로 다시 맞춘 뒤 `Resynced`를 부른다. `B`가 프레이즈 경계를 넘으면 그 경계의 `PhraseStarted`도 평소처럼 부른다.
  - 샘플레이트 변경은 호출자가 `Stop()` 후 `clock.Reset(rate)` → `Start(...)`로 처리한다(이 클래스는 감지하지 않는다).

- [ ] **Step 1: 실패하는 테스트** (`BeatClock(48000)`)
  - `Start_BeatZeroAtDspStart` → `Start(48000, 120, 120)`; `BeatAt(48000)` = 0, `BeatAt(72000)` = 1, `BeatAt(24000)` = -1; `Update(47999)` 후 `Phrase` = -1, `Update(48000)` 후 `Phrase` = 0이고 `PhraseStarted(0)` 1회.
  - `PhraseBoundary_IsIntegerSample_NoDrift` → 프레이즈마다 템포를 108, 116, 124, 132 순환(`PhraseStarted`에서 `SetNextTempo`)으로 80프레이즈(약 10분) 진행 → 각 경계 `clock.DspAt(n·16)`이 `start + Σ round(16·60/bpm[k]·48000)`과 **정확히** 같다.
  - `SetNextTempo_ReplacesUntilBoundary` → `Start(0, 108, 116)`, 박 10에서 `SetNextTempo(108)` → `clock.DspAt(17)` = `round(16·60/108·48000) + round(60/108·48000)` (±1).
  - `SetNextTempo_AfterBoundary_AppliesToFollowingPhrase` → 박 16.5에서(프레이즈 1 안) `SetNextTempo(132)` → 박 17~31은 프레이즈 1의 템포, `DspAt(33) − DspAt(32)` = `round(60/132·48000)` (±1).
  - `Update_LongGap_RaisesEachPhraseOnce` → `Update`를 박 2에서 한 번, 박 40 위치에서 한 번 → `PhraseStarted` 인자 순서 `[0, 1, 2]`(0은 첫 호출), 각 1회; `Phrase` = 2.
  - `NoHandler_KeepsTempo` → 핸들러 없이 박 20까지 → `Bpm` = 시작 템포의 `nextBpm`(프레이즈 1), 프레이즈 2 경계는 같은 템포로 계산.
  - `Update_DspWentBackwards_ResyncsAtNextBar` → 120 BPM, 박 5.3 위치까지 `Update` 후 `Update(1000)` → `Resynced` 1회, `clock.DspAt(8)` = `1000 + 4800`, 예외 없음, 이후 `BeatAt`이 단조.
  - `Stop_ThenStart_ResetsPhraseAndBeat` → 박 20까지 진행, `Stop()` → `Running` false, `Phrase` -1, `BeatAt` false; `Start(…)` → `Phrase` -1에서 다시, `PhraseStarted(0)` 다시 1회.
- [ ] **Step 2:** `run_tests` → FAIL. **Step 3:** 구현. **Step 4:** `run_tests` PASS(기존 포함).
- [ ] **Step 5:** Commit `audio: MusicTransport, a self-running phrase clock on the DSP clock`.

### Task 6: `SoundManager`를 `MusicTransport`로 전환

**Files:**
- Modify: `Assets/Scripts/Manager/SoundManager.cs`
- Create: `Assets/Scripts/Audio/FmodClock.cs` (`public static class FmodClock { public static ulong MasterDsp(); public static int SampleRate(); }` — `MusicConductor.MasterDsp`의 새 집. 마스터 채널 그룹 핸들은 처음 부를 때 얻고, 핸들이 무효가 되면 다시 얻는다)
- Modify: `Assets/Scripts/Audio/OneShotScheduler.cs` (`FmodOneShotBackend.NowDsp` → `FmodClock.MasterDsp()`, `DefaultScheduleOffsetSamples = 0`)
- Modify: `Assets/Scripts/Audio/Capture/CaptureScenario.cs`, `Assets/Scripts/Audio/Capture/AudioCapture.cs`
- Delete: `Assets/Scripts/Audio/MusicConductor.cs`(+`.meta`), `Assets/Tests/Editor/SectionTempoTests.cs`(+`.meta`)

**Interfaces:**
- Consumes: `MusicTransport`(Task 5), `DspClockSmoother`(Task 1), 옛 계획이 만든 `PatternScheduler`·`KillChordPlanner`·`IArrangement`.
- `SoundManager` 변경:
  - `StartMusic`: 뱅크·샘플 로딩은 그대로. 음악 이벤트 인스턴스를 만들지 않는다. 로딩이 끝나면 `_clock.Reset(FmodClock.SampleRate())`, `_transport.Start(FmodClock.MasterDsp() + round(0.25 · rate), Bpm(Current), Bpm(Next))`. 시작 여유 0.25초.
  - `Update`: `_transport.Update(FmodClock.MasterDsp())` → 화면 박(Task 1 식, 원천 `_transport.BeatAt`) → 패턴·처치 스케줄러. 매 프레임 `FmodClock.SampleRate()`가 시계의 값과 다르면 `StopMusic()` 후 `StartMusic()`(프레이즈 0부터 다시; 드문 경우라 위치 유지는 하지 않는다).
  - `PhraseStarted` 핸들러 = 지금의 `UpdatePhrase` 본문(구간·스타일·변형·특색 선택) + `_transport.SetNextTempo(Bpm(_sections.Next.Section))`. `NextSection` 파라미터 쓰기 삭제.
  - `QueueFever`의 `HoldNext()` 뒤: `_transport.SetNextTempo(...)` + `Shots.RescheduleFrom(NextPhraseStep / StepsPerBeat)`.
  - `Resynced` 핸들러: `Shots.StopAll()`, 백엔드 `StopAll()`, 두 `PatternScheduler` `Reset()`, 처치 플래너 `Clear()`.
  - `RhythmLookahead` 삭제. `QueueFever`의 최소 여유는 `const double FeverMinNoticeSeconds = 0.25`로 남긴다(같은 값, 같은 식).
  - `PostBoundaryDispatchBeats`·`PostBoundaryBeats`와 `KillChordPlanner.DispatchWindow`의 착지 특례는 **Task 8 측정 뒤** 지운다(여기서는 유지).
  - 공개 멤버: `CurrentBpm` = `round(_transport.Bpm)`, `BeatSeconds` = `60 / _transport.Bpm`(실행 전 60/108), `CurrentBar`·`ChordIndex`·`NextPhraseStep` = 시계에서 계산. `Running` = `_transport.Running && 화면 박 >= 0`. `CurrentSection` 삭제.
  - 박 0 전(시작 여유 동안): `BeatPosition` = -1, `CurrentBeat` = -1.
- `CaptureScenario`/`AudioCapture`:
  - `public static double AudioCapture.FrameAtDsp(ulong dsp)` — 캡처 시작 시 마스터 DSP 클럭과 캡처 프레임을 짝지어 두고 변환.
  - `.beats.csv`는 `PhraseStarted`와 화면 박이 아니라 **시계 계산값**으로 쓴다: 박마다 `(beat, FrameAtDsp(clock.DspAt(beat)), bpm)`. `_conductor`·`_anchorSeconds` 리플렉션 삭제.
  - `spike` 시나리오: `SpikeRef` 파라미터·`.timeline.csv` 삭제. 클릭은 매 박(짝수 박 1박 앞, 홀수 박 0.3박 앞) 그대로. 인스펙터 `spikeDispatchBeats`(기본 0.5; 4.0이면 코어 드럼처럼 한 마디 앞서 넘김).

- [ ] **Step 1:** `FmodClock` 추가, `MusicConductor.MasterDsp` 호출부를 모두 교체(동작 변화 없음) → 컴파일 오류 0, `run_tests` PASS → Commit `audio: FmodClock holds the master DSP clock`.
- [ ] **Step 2:** `SoundManager` 전환, `MusicConductor`·`SectionTempoTests` 삭제, 캡처 코드 갱신 → 컴파일 오류 0, `run_tests` PASS.
- [ ] **Step 3:** 캡처 baseline seed 7 → `analyze_capture.py landings`: 모든 경계 지연 ≤ 5 ms, 20 ms 넘는 무음 0, 첫 반 박 RMS ±1 dB(편성이 같은 경계). `frames`: 뒤로 0회, 초과 0. ONESHOTS late·dropped 0.
- [ ] **Step 4:** 캡처 `fever` 시나리오(옛 계획 Task 14: 박 24에 `QueueFever`) seed 33 → 피버가 붙잡은 경계에서 템포가 유지되고(`.beats.csv`의 bpm), 경계 뒤 4박의 late·dropped 0.
- [ ] **Step 5:** Commit `audio: music runs on MusicTransport; the timeline event is no longer started`.

### Task 7: 정확도·프레임 정지 측정

**Files:**
- Modify: `Assets/Scripts/Audio/Capture/CaptureScenario.cs` (강제 정지 옵션)
- Modify: `FMOD/Tools/analyze_capture.py` (`spike`를 시계 기준으로, 이른 노트 집계)
- Modify: `Assets/Scripts/Audio/OneShotScheduler.cs` (이른 노트가 나오면)
- Modify: `Docs/Captures/README.md`

**Interfaces:**
- `CaptureScenario`: `stallMs`(int, 0 = 없음), `stallAtBeats`(float[]): 화면 박이 각 값을 지나는 프레임에 `System.Threading.Thread.Sleep(stallMs)` 한 번. `.cpu.txt`의 `stalls` 집계에서 강제 정지는 따로 센다(`forced stalls N`).
- `analyze_capture.py spike <wav>`: 기준은 `.beats.csv`의 프레임(시계 계산값). 클릭마다 `어택 프레임 − 박 프레임`(ms). 출력: 평균 |오차| / 최대, 템포 전환 뒤 4박 따로, `missing`(클릭 없음), `late`(> +5 ms), `early`(< −5 ms). 기준 위반 시 exit 1(`--allow-missing N`으로 누락 허용 수 지정).

- [ ] **Step 1:** `spike` seed 33, 68박, `spikeDispatchBeats` 0.5 → 평균 ≤ 2 ms, 최대 ≤ 5 ms, missing·late·early 0. 평균 부호 오차가 ±0.5 ms(24샘플)를 넘으면 `DefaultScheduleOffsetSamples`를 그만큼 보정하고 다시 녹음(값과 근거를 README에).
- [ ] **Step 2:** `spike` seed 33, `spikeDispatchBeats` 4.0, `stallMs` 300, `stallAtBeats` `{ 9.8, 15.7, 40.2 }`(프레이즈 중간, 경계 직전, 넘긴 직후) → missing 0, late 0, early 0, 정지 뒤 4박 정확도 통과.
- [ ] **Step 3:** early > 0이면: `FmodOneShotBackend.Start`에서 `instance.setPaused(true)` 후 `start()`, 시작 시각을 건 뒤(`ApplyTimes` 직후) `setPaused(false)`로 바꾸고 Step 1·2를 다시 녹음. 일시정지 상태에서 채널 그룹이 생기지 않아 dropped가 늘면 이 방법을 되돌리고, 두 방식의 측정 표와 함께 **멈추고 보고**한다.
- [ ] **Step 4:** `heavy` seed 7(코어 드럼·전 레이어), `stallMs` 300, `stallAtBeats` `{ 30.5, 47.7 }` → `landings`에서 정지 지점 뒤 1박 안 20 ms 넘는 무음 0(코어 드럼이 이어짐), late·early 0.
- [ ] **Step 5:** `heavy` seed 7, `stallMs` 2000, `stallAtBeats` `{ 30.5 }` → late·early 0(누락은 허용), 정지 뒤 첫 박부터 `.beats.csv` 프레임과 킥 어택 차이 ≤ 5 ms, `frames`에서 정지 프레임 외 초과 0.
- [ ] **Step 6:** README 표 → Commit `capture: clock-referenced accuracy, forced-stall takes`.

### Task 8: 정리 — 음악 이벤트 삭제, 우회로 제거, 최종 측정

**Files:**
- Create: `FMOD/Tools/remove_music_event.py` → `remove_music_event.js` (`event:/Music/ChordPulseGame` 삭제. 실행 전 이벤트의 `SingleSound` 수를 세어 `Spike ref` 외의 노트가 있으면 지우지 않고 목록을 출력하고 멈춘다. 이벤트가 없으면 아무것도 하지 않는다(다시 실행 가능). `event:/Music/ChordPulsePreview`는 건드리지 않는다)
- Delete: `FMOD/Tools/spike_click.py`의 `Spike ref` 트랙 생성 부분(`event:/Inst/SpikeClick`은 남김)
- Modify: `Assets/Scripts/Manager/SoundManager.cs`, `Assets/Scripts/Audio/KillChordPlanner.cs`, `Assets/Tests/Editor/KillChordPlannerTests.cs`, `Assets/Tests/Editor/PatternSchedulerTests.cs` (착지 특례 제거)
- Modify: `Docs/AdaptiveAudioPlan.md`, `Docs/Captures/README.md`

- [ ] **Step 1:** 착지 특례 제거: `PostBoundaryDispatchBeats`·`PostBoundaryBeats` 삭제, `KillChordPlanner.DispatchWindow(int beat)`는 항상 `OneShotScheduler.DispatchWindowBeats`를 돌려준다. 테스트 `DispatchWindow_PhraseLanding`·`LandingBeats_UseWideDispatchWindow`를 "모든 박 0.5"로 고친다. `run_tests` PASS → `heavy` seed 33(처치 매 박) 녹음: 경계 뒤 4박 late·dropped 0. 0이 아니면 특례를 되돌리고 README에 이유를 적는다.
- [ ] **Step 2: 깨지면 안 되는 상황 수동 확인**(스펙 4-5), 결과를 README에:
  - 에디터 일시정지 3초 → 재개: 밀린 노트가 몰아서 울리지 않음, 화면 박과 소리가 맞음.
  - 에디터 포커스 이탈 3초(Run In Background 꺼짐/켜짐 각각) → 같은 확인.
  - 재생 중 Windows 출력 장치 전환 → 예외 없이 한 마디 안에 음악이 이어짐(콘솔에 `Resynced` 또는 재시작 로그).
  - 게임 오버 → 즉시 재시작 3회: 잔류음 없음, 첫 박부터 정상.
  - 어느 하나라도 실패하면 그 항목의 재현 절차와 함께 **멈추고 보고**.
- [ ] **Step 3:** `remove_music_event.py` → JS 실행 → `-build` 별도 → 뱅크 복사. `SoundManager`에서 `MusicPath`와 남은 이벤트 참조 삭제. Unity "Events Renamed" 대화상자는 **No**. 컴파일 오류 0, `run_tests` PASS.
- [ ] **Step 4: 최종 측정**(스펙 5장 전부): baseline·heavy seed 7, heavy seed 33, `styles`, `features`, `fever`, `spike` seed 33 + Windows Development 빌드의 baseline·heavy. 표: `landings`(지연·공백·RMS), `spike`(평균/최대/전환 뒤 4박), `frames`, Studio·Core CPU, ONESHOTS started/late/dropped. Global Constraints 전부 통과 확인. Part A의 64박 자리(지연·RMS)를 표에 따로 적는다.
- [ ] **Step 5:** 수동 Play(`ChordPulsePrototype` 씬): 템포 전환, 피버 진입·종료, 숨 고르기, 게임 오버 → 재시작에서 끊김·잔류음 없음.
- [ ] **Step 6:** 문서: `AdaptiveAudioPlan.md`에 "시계는 Unity(`MusicTransport`), 음악 이벤트 없음" 절 추가, 타임라인·`NextSection`·R14·R15를 설명하는 절에 "대체됨" 표시, 8-10 실행 순서에서 음악 이벤트 관련 스크립트 제거. README에 Part C 결과 표.
- [ ] **Step 7:** Commit `fmod: remove the music timeline event; Unity owns the music clock`.
