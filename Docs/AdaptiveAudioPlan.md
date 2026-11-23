# Chord Pulse — FMOD 적응형 오디오 설계

> 목표: 플레이어가 디펜스를 하는 행위 자체가 곡을 "연주/편곡"하는 느낌이 들도록, 음악·적 등장·연출을 하나의 박자 기준 위에 올린다.

---

## 진행 현황

| 단계 | 상태 | 구현 위치 |
|---|---|---|
| 1. 박자 동기화 | ✅ 완료 (2026-11-24) | `Audio/MusicConductor.cs` — FMOD `TIMELINE_BEAT`/`TIMELINE_MARKER` 콜백으로 박을 직접 셈. `SoundManager`의 하드코딩 템포 표·타임라인 보정 제거. 난이도는 `ChordPulsePrototype.DifficultyLevel(생존 시간)`으로 분리 |
| 2. 반주·처치 화음 믹싱 | ✅ 완료 (2026-11-24) | FMOD `ChordPulseGame` 이벤트 내부 그룹 트랙 + `SoundManager.MusicMix / KillChordMix` |
| 3. 구간별 편곡·피버 | ✅ 완료 (2026-11-24) | 피버 빌드업·임팩트·아웃트로 트랙 + `Intensity` 파라미터로 일반 구간 밀도 조절 (`SoundManager.Intensity`, 현재는 난이도 단계와 연동) |
| 4. 랜덤 패턴·BPM | ✅ 1차 완료 (2026-11-24) | 4개 템포 구간을 4마디 프레이즈 단위로 점프 (FMOD 전환 마커 25개 + `NextSection`), `Audio/SectionSelector.cs`가 시드 기반으로 한 프레이즈 앞서 선택, 숨 돌리기 프레이즈 |
| 5. 음악 연동 시각 연출 | ✅ 1차 완료 (2026-11-24) | `Visual/MusicReactiveVisuals.cs` — 루프가 아닌 음악 의미(킥·스네어·코드·에너지·빌드업·피버·숨돌리기·구간 전환)에 반응 |
| 6. 랜덤 특색 루프 (기본 루프 위 레이어) | 📝 계획 | 트럼펫처럼 개성 있는 루프가 가끔 끼어들고, 루프마다 전용 화면 연출(무언가 날아다니는 등)이 함께 등장. 설계는 아래 [7장](#7-다음-단계-랜덤-특색-루프-feature-loop) |

### 2단계 믹싱 구조 (FMOD 이벤트 내부)

```
Event Master  [Limiter ceiling -1 dB]
├─ Music Bed            ← MusicMix 파라미터(0~1)가 볼륨 자동화
│   ├─ Kick / Snare / Hats / Fever shaker / Bass / Fever bass fifths
│   └─ Harmony          [Compressor: 사이드체인 덕킹, thr -32 / 4:1 / 5ms / 180ms]
│       └─ Offbeat pluck / Fever lead (이후 패드도 여기)
└─ Kill Chords          ← KillChordMix 파라미터(0~1)가 볼륨 자동화
    [Sidechain → Harmony 덕킹 키] [Compressor thr -20 / 3:1] [Limiter -3 dB]
    └─ Red / Blue / Yellow kill chord
```

- 처치 화음은 음악 타임라인 위에 그대로 있어 박 정확도가 유지된다 (별도 이벤트로 분리하지 않음).
- 측정(108~116 BPM 구간, 이벤트 출력): 반주만 RMS -25.4 dBFS / 3색 × 3음 처치 화음 매 박 RMS -21.7 dBFS, 피크 -6.4 dBFS.
- 덕킹 양·컴프레서 수치는 귀로 최종 조정할 것 (FMOD Studio에서 그룹 트랙 이펙트 수정 후 빌드).

### 3단계 피버 진입 구조

```
QueueFever (8킬 달성)
   │  다음 마디 경계(룩어헤드 0.15초 이상 남은 마디)
   ▼
[빌드업 1마디]  FeverBuild=1   킥 빠짐 · 스네어 롤(8분→16분, -20→-4 dB, 피치 +4) · 플럭 아르페지오 상승(현재 마디 코드음, 2옥타브)
   ▼  마디 경계
[피버 4마디]    Fever=1        피버 리드 · 셰이커 · 베이스 5도 (기존)
                FeverImpact=1  첫 박만: 킥 + 클랩 + 오픈햇 임팩트
```

- 킥 트랙 볼륨 -7 → -12 dB (메인 드럼이 과하게 튀던 문제).
- 빌드업 악기는 32마디 전체 타임라인의 매 마디에 배치되고 `FeverBuild` 조건으로만 울린다 → 어느 마디에서 피버가 와도 동작.
- `SoundManager.FeverBuilding`으로 빌드업 마디를 알 수 있다 (HUD에 "FEVER INCOMING..." 표시, 5단계 시각 연출에서 활용).
- 피버 마지막 마디는 `FeverOutro=1`: 현재 코드음으로 2옥타브 하강하는 플럭 + 작아지며 내려가는 스네어 16분.

### 일반 구간 밀도 (`Intensity` 0~3)

| Intensity | 연주 악기 | 현재 연결 |
|---|---|---|
| 0 | 킥 · 스네어 · 클로즈드 햇 · 베이스 | 생존 0~17.8초 |
| 1 | + 오프비트 플럭 | 17.8초~ |
| 2 | + 오픈 햇 | 34.3초~ |
| 3 | + 스네어에 클랩 레이어 | 49.8초~, **피버 중에는 항상 3** |

- 현재는 `ChordPulsePrototype`이 난이도 단계를 그대로 넣는다. 4단계(랜덤 셀 선택)에서 셀의 `energy` 값이나 "숨 돌리는 구간"이 이 값을 직접 정하도록 바꾸면 된다.

### 4단계 구간 선택 구조

```
[Unity] 프레이즈(4마디) 시작마다
   SectionSelector.Advance(목표 에너지=난이도, 피버 중엔 숨돌리기 금지)
     Current ← Next,  Next ← 새로 선택  ──setParameter("NextSection")──▶ [FMOD]
                                                                      프레이즈 경계(1ms 전) 전환 마커:
                                                                      NextSection == k 이면 Stage k 시작으로 점프
                                                                      (자연스러운 다음 구간이면 점프 없이 진행)
```

- 구간 = 기존 4개 템포 스테이지 (S0 108 · S1 116 · S2 124 · S3 132 BPM, 각 8마디 = 프레이즈 2개). 모든 구간이 같은 C–Am–F–G 진행이라 어디로 점프해도 화성이 이어진다.
- 선택 규칙: 목표 에너지 ±1 (25/50/25%), 같은 구간 3연속 금지.
- **숨 돌리기 프레이즈**: 난이도 1 이상에서 22% 확률(최소 3프레이즈 간격, 연속 불가) → S0(108 BPM) + `Intensity` 0 + 적이 한 마리씩 한 마디 더 띄워 등장. HUD에 `~breathe~`. 난이도(적 속도·숨은 적)는 생존 시간 기준 그대로.
- Unity가 다음 프레이즈를 한 프레이즈(4마디) 앞서 알기 때문에 `SoundManager.UpcomingPhrase`, `NextPhraseStep`으로 적 등장·연출을 미리 준비할 수 있다.
- `SoundManager.sectionSeed` (Inspector): 0이면 매 판 랜덤, 값을 넣으면 같은 구간 순서 재현.
- `MusicConductor`는 점프 시 박 콜백이 중복·누락돼도 FMOD의 마디 내 박 번호에 맞춰 보정한다. 테스트(75초, 점프 6회)에서 프레이즈 시작 박이 16의 배수에서 어긋나지 않음.
- 알려진 사항: FMOD 경고 "Loading delay exceeded"가 Play 시작 직후 1회 나온다 (구간 점프가 없는 테스트에서도 발생 → 점프와 무관, 에디터 Play 진입 시 프레임 지연으로 추정). 빌드에서도 나오면 음악 시작 전 한 프레임 대기 등을 검토.

### 5단계 시각 연출 (`MusicReactiveVisuals`)

`ChordPulsePrototype`이 런타임에 `AddComponent`로 붙인다 (씬 수정 없음). 기존 `AnimateArena`는 이 컴포넌트로 옮겼다.

| 음악 신호 (`SoundManager`) | 연출 |
|---|---|
| `KickOn(beat)` — 빌드업 마디에선 false | 경계 링 펄스 + 카메라 1.2% 줌 펀치 |
| 스네어 박 (2·4박) | 경계 링 플래시 |
| `ChordIndex` (C·Am·F·G) | 배경 색조: 파랑 · 보라 · 청록 · 호박 |
| `ArrangementIntensity` | 배경 밝기 (편성이 두꺼울수록 진하게) |
| `FeverBuilding` | 마디 동안 아레나 6% 수축 + 스네어 롤에 맞춘 깜빡임 (8분→16분), 금색 |
| `FeverActive` 진입 | 흰 플래시 + 바깥으로 퍼지는 링, 피버 동안 카메라 2.5% 줌아웃, 킥 펄스 흰색 |
| `IsBreath` | 배경 탈채도·어둡게, 킥 펄스 절반 |
| `SectionIndex` 변경 (템포 점프) | 새 구간 에너지 색의 리플 링 |

- Play 테스트 확인: 코드별 배경 변화, 빌드업 마디 스텝 16~23 동안 수축 0.985→0.947, 피버 첫 박 배경 플래시·리플·흰 킥 펄스.
- 새 음악 셀을 추가해도 위 신호만 제공되면 연출 코드는 바꿀 필요가 없다 (6-2 설계).

## 0. 현재 구조 요약 (2026-11 기준, 작업 전)

| 항목 | 현재 구현 | 한계 |
|---|---|---|
| 박자 계산 | `SoundManager`의 `Tempos = {108,116,124,132}` 하드코딩 + `getTimelinePosition()`으로 초 → 스텝 역산 | FMOD 쪽 BPM/구간을 바꾸면 Unity 계산과 즉시 어긋남 |
| 루프 처리 | 마지막 구간에서 타임라인이 되감기면 `_timelineOffset`을 더하는 방식 | 구간이 랜덤하게 점프하면 "되감김"을 판별할 수 없음 |
| 피버 | `Fever` 파라미터 0/1, 다음 마디에 적용 | 진입 연출(필인/라이저) 없음 |
| 처치 화음 | 음악 이벤트 안의 `RedKills/BlueKills/YellowKills` 파라미터 | 반주와 같은 이벤트라 별도 믹싱·덕킹·음수 제한이 어려움 |
| 난이도 | `TempoStage`가 그룹 크기·반박 등장 확률을 결정 | 음악 변화(BPM)와 난이도가 한 몸이라 "숨 돌리는 구간"을 만들 수 없음 |

---

## 1. 박자·구간 정보를 FMOD 기준으로 통일

**원칙: 시계는 하나. FMOD가 "지금 몇 마디 몇 박인지"를 알려주고, Unity는 그것만 믿는다.**

- FMOD 이벤트 콜백 사용 ([문서](https://www.fmod.com/docs/2.02/api/studio-api-common.html#fmod_studio_event_callback_type))
  - `TIMELINE_BEAT` → `bar`, `beat`, `tempo`, `timesignatureupper/lower`, `position`
  - `TIMELINE_MARKER` → 마커 `name`, `position` (구간 ID 전달용)
  - 중첩 이벤트(Nested Event)로 루프를 구성할 경우 `TIMELINE_NESTED_TIMELINE_BEAT`(2.02+)도 고려
- 콜백은 **FMOD 스레드**에서 호출 → 스레드 안전 큐에 넣고 Unity 메인 스레드 `Update()`에서 꺼내 처리.
- Unity 측에 `MusicConductor`(단일 시계) 도입
  - 누적 마디 번호(`AbsoluteBar`)는 콜백의 `beat == 1` 횟수로 직접 센다 → 타임라인 되감기/점프와 무관.
  - 프레임 사이 연속값(`BeatPosition`)은 "마지막 박 콜백 시각 + 경과시간 × tempo/60"으로 보간.
  - 하드코딩 `Tempos` 표와 `_timelineOffset` 로직 제거.
- 출력 지연 보정값(`visualLatencyMs`)을 설정으로 두고 실측해서 맞춘다 (박 콜백은 믹싱 시점 기준이라 실제 귀에 들리는 시점보다 약간 빠를 수 있음).

```csharp
// 골격 예시 — 콜백은 FMOD 스레드에서 실행됨
[AOT.MonoPInvokeCallback(typeof(FMOD.Studio.EVENT_CALLBACK))]
static FMOD.RESULT OnMusicEvent(FMOD.Studio.EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr paramPtr)
{
    var instance = new FMOD.Studio.EventInstance(instancePtr);
    instance.getUserData(out IntPtr data);
    var conductor = (MusicConductor)GCHandle.FromIntPtr(data).Target;

    if (type == FMOD.Studio.EVENT_CALLBACK_TYPE.TIMELINE_BEAT)
    {
        var beat = Marshal.PtrToStructure<FMOD.Studio.TIMELINE_BEAT_PROPERTIES>(paramPtr);
        conductor.Enqueue(MusicSignal.Beat(beat.bar, beat.beat, beat.tempo, beat.timesignatureupper));
    }
    else if (type == FMOD.Studio.EVENT_CALLBACK_TYPE.TIMELINE_MARKER)
    {
        var marker = Marshal.PtrToStructure<FMOD.Studio.TIMELINE_MARKER_PROPERTIES>(paramPtr);
        conductor.Enqueue(MusicSignal.Marker((string)marker.name, marker.position));
    }
    return FMOD.RESULT.OK;
}
// 등록: _handle = GCHandle.Alloc(this); _music.setUserData(GCHandle.ToIntPtr(_handle));
//       _music.setCallback(s_callback, TIMELINE_BEAT | TIMELINE_MARKER);  (delegate는 static 필드로 유지해 GC 방지)
```

**완료 기준**: FMOD Studio에서 BPM이나 구간 길이를 바꿔도 Unity 코드 수정 없이 적 등장·원 펄스가 박에 맞는다.

---

## 2. 랜덤 음악 = "구간(셀) 선택" 방식

- 처음부터 음을 생성하지 않고, **잘 맞는 조각(셀)을 미리 만들고 4~8마디마다 조합을 교체**한다.
- 레이어를 세로로 분리: `Drums × Bass × Melody (× Pad)` — 각 3~4종이면 12개 루프로 수십 가지 조합.
- **같은 코드 진행(예: C–Am–F–G) 안에서만 변주** → 어떤 조합이든 화성 충돌이 없고 곡의 일관성이 유지된다.
- 모든 셀은 4마디의 배수 길이, 같은 박자표.
- 전환은 FMOD Transition Region/Destination Marker를 **마디 단위로 퀀타이즈**.

## 3. 피버 = 곡의 하이라이트로 편곡

| | 일반 구간 | 피버 직전 (1마디) | 피버 |
|---|---|---|---|
| 드럼 | 기본 패턴 | 필인 / 스네어 롤 | 풀 킷 + 추가 타악기 |
| 베이스 | 단순 루트 | 정지 또는 슬라이드 | 옥타브/리듬 강화 |
| 멜로디 | 일부 비움 | 상승 라이저 | 리드 추가 |
| 패드 | 없음/약하게 | 필터 열림 | 풀 패드 |
| 처치 화음 | 유지 | 유지 | 유지 (한 옥타브 위/밝은 음색 가능) |

- `QueueFever()`가 "다음 마디"가 아니라 **"다음 다음 마디"**로 예약 → 그 사이 1마디를 필인/라이저 구간으로 사용.
- 피버 종료도 다운 필(필터 닫힘, 임팩트)로 명확히.

## 4. 처치 화음 믹싱 계획

- **반주와 처치 화음을 분리**: 처치 화음을 음악 이벤트의 파라미터가 아니라 별도 이벤트(또는 별도 트랙 → 별도 버스)로.
- 믹서 버스 구조
  ```
  Master
  ├─ Music
  │   ├─ Drums
  │   ├─ Bass
  │   └─ Harmony (Lead, Pad)   ← 사이드체인 덕킹 대상
  └─ KillChord                 ← 사이드체인 소스, 컴프레서 + 리미터
  ```
- 처치 화음이 울릴 때 `Harmony` 버스를 2~4dB 잠깐 낮춤 (FMOD 사이드체인 + 컴프레서, 빠른 어택/짧은 릴리즈).
- 많이 쌓일 때 대비
  - 이벤트 **Max Instances + Stealing**(가장 오래된/조용한 것 교체)
  - 같은 박에 겹친 처치는 지금처럼 박 단위로 모아 **보이싱 수만 늘림**(최대 3~4음), 볼륨은 √N 수준으로만 증가
  - KillChord 버스에 리미터
- 화음 음정은 **현재 코드(Chord)의 구성음**에서 고른다 → 섹션이 바뀌어도 항상 협화.

## 5. 음악 변화와 난이도 분리

```
spawnInterval = 음악 계수(BPM/에너지) × 난이도 계수(생존 시간)
```

- **음악 계수**: 셀의 BPM·에너지에 따라 등장 "박자 간격"을 결정 (느린 셀 = 숨 돌리는 구간).
- **난이도 계수**: 생존 시간에 따라 단조 증가 — 적 속도, 그룹 크기, 숨은 적 비율, 반박 등장 허용 여부.
- 현재 `TempoStage`가 그룹 크기·반박 확률을 정하는 부분을 `DifficultyLevel(생존시간)`로 교체.

## 추천 진행 순서

1. 박자 동기화 정리 (`MusicConductor`, 콜백, 하드코딩 템포 제거)
2. 반주·처치 화음 믹싱 (버스 분리, 덕킹, 음수 제한)
3. 구간별 편곡과 피버 (필인/라이저, 피버 전용 레이어)
4. 랜덤 패턴·BPM (셀 선택기)
5. 음악에 따른 시각 연출 (아래 6장)

---

## 6. 박자 기준으로 "수많은 루프 + 대응 연출"을 만든다면

루프가 수십~수백 개로 늘어날 때 **루프마다 연출을 손으로 짝지으면 반드시 무너진다.** 핵심은 _루프와 연출을 직접 연결하지 않고, 그 사이에 "음악 데이터" 층을 두는 것_이다.

```mermaid
flowchart LR
    A[DAW: 루프 오디오 + MIDI] --> B[FMOD Studio<br/>셀 = 마커/전환]
    A --> C[MIDI → JSON 변환<br/>악기별 온셋·코드·에너지]
    C --> D[MusicCell 데이터<br/>ScriptableObject]
    B -- 박/마커 콜백 --> E[MusicConductor<br/>단일 시계]
    D --> E
    E -- 의미 이벤트<br/>Kick/Snare/ChordChange/Energy --> F[연출 Reactor들]
    E -- 의미 이벤트 --> G[Spawn 패턴]
    H[DifficultyDirector<br/>생존 시간] --> G
```

### 6-1. 셀(Cell) 메타데이터를 표준화
각 루프에 같은 ID로 다음 데이터를 붙인다 (FMOD 마커 이름 = 셀 ID, Unity는 ID로 데이터 조회).

| 필드 | 예 | 용도 |
|---|---|---|
| `id` | `drum_break_02` | 마커 이름과 일치 |
| `role` | Drums / Bass / Melody / Pad / FX | 어느 연출 계층이 반응할지 |
| `bars` | 4, 8 | 전환 예약 |
| `bpm`, `timeSig` | 116, 4/4 | 검증용 (실제 값은 콜백 기준) |
| `energy` | 0~3 | 화면 강도, 등장 밀도 |
| `chords` | `[C, Am, F, G]` (마디별) | 처치 화음 음 선택, 색조 |
| `onsets` | 악기별 16분 스텝 목록 | 박 단위가 아닌 "실제로 친 음"에 연출 반응 |
| `tags` | `breath`, `buildup`, `drop` | 특수 연출/스폰 규칙 선택 |

- `onsets`와 `chords`는 **DAW에서 MIDI로 내보내 자동 변환**(에디터 스크립트)한다. 루프 100개여도 연출 코드는 0줄 추가.

### 6-2. 연출은 "루프"가 아니라 "역할·의미"에 반응
유한한 연출 어휘(Reactor)를 만들고 역할에 매핑한다.

| 음악 신호 | 연출 Reactor |
|---|---|
| Kick 온셋 | 코어 링 펄스, 카메라 미세 줌 (현재 `AnimateArena`) |
| Snare/Clap | 아레나 테두리 플래시 |
| Hi-hat | 배경 파티클 깜빡임 |
| 코드 변경 | 배경 색조 이동 (근음 → 색상) |
| Energy 변화 | 블룸/채도/파티클 양 |
| `buildup` 태그 | 화면 수축 + 라이저 연출, 피버 예고 |
| `drop`/피버 진입 | 화이트 플래시 + 링 확장 |

- 새 루프를 넣으면 메타데이터만으로 자동으로 연출이 따라온다.
- 꼭 필요한 시그니처 순간만 셀별 override 허용.

### 6-3. 랜덤 선택은 Unity가, 실행은 FMOD가
- FMOD 자체 랜덤(Multi Instrument)을 쓰면 **무엇이 나올지 Unity가 미리 모른다** → 적 등장 예고가 불가능.
- 권장: Unity의 `CellSelector`가 시드 기반으로 **다음 셀을 1~2마디 전에 결정** → FMOD 라벨 파라미터로 전달 → FMOD는 마디 경계에서 전환.
  - Unity가 미래를 알기 때문에 적을 "다음 구간 박에 맞춰" 미리 출발시킬 수 있다 (적이 도착하는 박 = 처치 화음이 울리는 박).
  - 시드 고정 → 버그 재현·리플레이·데일리 챌린지 가능.
- 게임플레이와 무관한 미세 변주(하이햇 변형, 필인 종류)만 FMOD 랜덤에 맡긴다.
- 선택 규칙 예: 같은 셀 연속 금지, 에너지는 ±1씩만 이동, 피버 전엔 `buildup` 강제, 일정 시간마다 `breath` 셀 보장.

### 6-4. BPM 변화 다루기
- BPM 단계는 2~3개로 제한, **단계마다 루프 세트를 따로 녹음** (타임스트레치보다 품질·박 정확도 우수).
- BPM 전환은 전용 전환 셀(필인)을 통해서만 → 듣는 사람도 템포 변화를 납득.
- Unity는 콜백의 `tempo`를 그대로 사용하므로 별도 처리 불필요.

### 6-5. 적 등장도 같은 데이터로
- 스폰 패턴 = "마디 단위 리듬 템플릿" 풀 (예: `X...X...X.X.....`)
- 셀의 `energy`/`tags` → 사용할 템플릿 풀, 난이도 → 템플릿 내 밀도·속도·숨은 적 비율.
- 선택적으로 **킥/스네어 온셋 위에 스폰**하면 "적을 잡으면 그 자리의 드럼이 연주되는" 느낌이 강해진다.

### 6-6. 제작 파이프라인과 검증
- 네이밍 규칙: `{role}_{name}_{bpm}_{bars}` (예: `bass_walk_116_8`)
- 에디터 검증 툴: FMOD 마커 이름 ↔ `MusicCell` 데이터 1:1 대응, 길이·BPM 불일치 경고. 런타임에 미등록 마커가 오면 경고 로그.
- 디버그 오버레이: 현재 마디/박, 현재 셀, 예약된 다음 셀, 박 콜백 대비 연출 오차(ms).
- 자동 플레이 봇으로 10분 이상 돌려 드리프트·박 누락 로그 확인.

### 6-7. 규모 가늠
- 레이어 4종 × 각 4셀 × BPM 2단계 = 루프 32개 → 조합 수백 가지.
- 이 정도면 1~2명의 사운드 작업량으로 충분하고, 연출 코드는 Reactor 7~10개로 고정된다.

---

## 7. 다음 단계: 랜덤 특색 루프 (Feature Loop)

> 기본 루프(드럼·베이스·하모니, 1~5단계에서 만든 반주)는 그대로 깔아 두고, 그 **위에** 개성 있는 루프가 랜덤하게 끼어든다. 예: 트럼펫 팡파레, 신스 브라스 훅, 휘파람 멜로디, 보컬 찹, 라틴 퍼커션 등.
> 특색 루프는 **소리와 화면 연출이 한 세트**다. 트럼펫이 끼어들면 화면에 무언가가 갑자기 날아다니는 식으로, "곡에 손님이 등장했다"는 느낌을 준다.

### 7-1. 기본 루프와의 관계

| | 기본 루프 | 특색 루프 |
|---|---|---|
| 역할 | 곡의 뼈대, 항상 재생 | 가끔 끼어드는 이벤트성 레이어 |
| 선택 | 구간(`SectionSelector`) | 별도 `FeatureSelector`가 기본 구간 위에 얹음 |
| 길이 | 4마디 프레이즈 | 1~4마디 (프레이즈 경계에서 시작) |
| 연출 | 의미 기반 공통 연출 (6-2) | **루프별 전용 연출** (6-2의 "시그니처 override"에 해당) |
| 화성 | C–Am–F–G | 같은 진행 위에서만 작곡 → 어느 구간에 얹어도 협화 |

### 7-2. 오디오 구조 (FMOD)

- `ChordPulseGame` 이벤트에 `Feature` 그룹 트랙 추가 → `Music Bed`와 별도로 둔다.
  - 특색 루프는 `FeatureLoop` 파라미터(라벨형: 0 = 없음, 1 = Trumpet, 2 = …)를 조건으로 프레이즈 시작 지점마다 배치 (피버 빌드업 트랙과 같은 방식).
  - 템포 구간이 4개이므로 루프마다 구간별 BPM 버전이 필요하다 (또는 짧은 원샷 프레이즈로 만들어 구간마다 배치).
- 믹싱
  - 특색 루프가 들어오면 `Harmony`(플럭·리드)를 사이드체인으로 살짝 덕킹 → 주인공 자리를 비켜준다 (2단계 Kill Chords 덕킹과 같은 구조).
  - 처치 화음은 여전히 최우선: `Kill Chords` 사이드체인이 `Feature`도 덕킹하도록 연결.
  - `Feature` 그룹에 리미터, 볼륨은 `FeatureMix` 파라미터로 Unity에서 조절 (2단계 `MusicMix`와 동일).

### 7-3. 선택 규칙 (Unity `FeatureSelector`)

- 4단계와 같이 **한 프레이즈 앞서** 결정 → `SoundManager.UpcomingPhrase`처럼 `UpcomingFeature`를 노출해 연출·스폰이 미리 준비.
- 시드 기반 (`sectionSeed`와 같은 시드 사용 → 리플레이 재현).
- 빈도: 기본은 낮게 (예: 프레이즈당 15~25%), 같은 루프 연속 금지, 최근 N회 등장한 루프는 가중치 감소.
- 상황별 규칙
  - 숨 돌리기 프레이즈: 잔잔한 특색 루프만 허용 (예: 휘파람) → 쉬는 구간이 지루하지 않게.
  - 피버 빌드업·피버 중: 끼어들지 않음 (피버가 하이라이트) 또는 피버 전용 특색 루프만.
  - 난이도가 높을수록 화려한 루프(브라스, 퍼커션) 가중치 증가.

### 7-4. 화면 연출 (루프 = 연출 세트)

- 루프마다 데이터 한 세트: `FeatureLoopData` (ScriptableObject)
  - `id`, FMOD 라벨 값, 길이(마디), 분위기 태그(`calm` / `hype` / `fever`)
  - **연출 프리팹** + 등장 타이밍 (프레이즈 시작, 특정 박, 멜로디 온셋)
  - 온셋 데이터 (6-1처럼 MIDI에서 추출) → 트럼펫 음 하나하나에 맞춰 연출이 반응
- 연출 예시

| 특색 루프 | 화면 연출 |
|---|---|
| 트럼펫 팡파레 | 아레나 밖에서 반짝이는 오브젝트(별·새·종이비행기)가 음표마다 휙 가로질러 날아감 |
| 신스 브라스 훅 | 배경에 큰 원형 파동이 음 높이에 맞춰 퍼짐 |
| 휘파람 (숨 돌리기용) | 느리게 떠다니는 빛 입자, 배경 살짝 밝아짐 |
| 라틴 퍼커션 | 아레나 테두리를 따라 작은 점들이 리듬에 맞춰 회전 |

- 원칙
  - **게임플레이 가독성 우선**: 날아다니는 오브젝트는 아레나 바깥·배경 레이어에서, 적과 헷갈리지 않는 형태·색으로.
  - 등장 1박 전에 예고(작은 반짝임)를 주면 "끼어든다"는 느낌이 살아난다 → 한 프레이즈 앞서 알고 있으므로 가능.
  - 공통 연출(`MusicReactiveVisuals`)과 겹칠 때는 특색 연출이 위, 공통 연출은 약하게.

### 7-5. 게임플레이 연결 (선택)

- 특색 루프 동안만 나오는 보너스 적/오브젝트 (예: 날아다니는 트럼펫 별을 맞히면 피버 게이지 +1).
- 특색 루프 음표 박에 처치하면 처치 화음이 그 루프 음색으로 바뀌는 등 "같이 연주한다"는 보상.
- 단, 난이도 곡선은 5장 원칙대로 생존 시간 기준 유지 — 특색 루프는 보상·분위기 담당.

### 7-6. 진행 순서 제안

1. 트럼펫 루프 1개로 수직 슬라이스: FMOD `Feature` 트랙 + `FeatureLoop` 파라미터 + Unity 선택(한 프레이즈 앞) + 날아다니는 연출 1종.
2. 믹싱(덕킹·리미터·`FeatureMix`) 조정 후 귀로 검증.
3. `FeatureLoopData` 구조로 일반화 → 루프 2~3개 추가 (calm 1개 포함).
4. (선택) 보너스 오브젝트 등 게임플레이 연결.
