/// 스폰 단계 표의 한 줄. 라운드 시간이 StartSeconds를 지나면 이 줄의 값이 쓰인다.
public readonly struct SpawnStage
{
    public readonly double StartSeconds;
    public readonly int IntervalSteps;       // 한 자리에서 다음 자리까지 (스텝 = 반 박; 2보다 작아지면 안 된다: 펄스 쿨타임 1박)
    public readonly int RunMin, RunMax;      // 같은 색 뭉치의 길이 (자리 수)
    public readonly double PairChance;       // 한 자리에 2마리가 나올 확률
    public readonly double TripleChance;     // 한 자리에 3마리가 나올 확률
    public readonly double HiddenChance;     // 자리마다: 회색
    public readonly double ChordPairChance;  // 새 뭉치가 시작될 때: 첫 적이 화음 적
    public readonly double ChargerChance;    // 뭉치가 끝날 때: 돌격형 한 마리가 추가로 끼어든다
    public readonly double SplitterChance;   // 자리마다 (회색도 화음도 아닐 때): 분열형

    public SpawnStage(double startSeconds, int intervalSteps, int runMin, int runMax, double pairChance, double tripleChance,
        double hiddenChance, double chordPairChance, double chargerChance, double splitterChance)
    {
        StartSeconds = startSeconds;
        IntervalSteps = intervalSteps;
        RunMin = runMin;
        RunMax = runMax;
        PairChance = pairChance;
        TripleChance = tripleChance;
        HiddenChance = hiddenChance;
        ChordPairChance = chordPairChance;
        ChargerChance = chargerChance;
        SplitterChance = splitterChance;
    }
}

public static class GameplayTuning
{
    // 스폰 단계 표 (Docs/superpowers/specs/2026-11-27-spawn-curve-design.md 5.1). 숫자는 출발값이고, 조정 방법은 그 문서 6장.
    // 확률들은 서로 다른 순간에 따로 뽑으므로 한 줄의 합이 1과 무관하다 (단, 2마리 + 3마리 <= 1).
    // 마리 수는 압박감이고, 실제 난이도는 뭉치 길이(색이 바뀌는 빈도)와 특수 적 확률이 정한다.
    public const int MaxGroupSize = 3;
    public static readonly SpawnStage[] SpawnStages =
    {
        //            시작   간격  뭉치    2마리  3마리  회색   화음   돌격   분열
        new SpawnStage(  0,   4,   2, 3,   0.00,  0.00,  0.00,  0.00,  0.00,  0.00),   // 학습: 2박마다 한 마리
        new SpawnStage( 15,   3,   3, 4,   0.00,  0.00,  0.00,  0.00,  0.00,  0.00),
        new SpawnStage( 30,   2,   4, 5,   0.15,  0.00,  0.15,  0.00,  0.00,  0.00),
        new SpawnStage( 60,   2,   4, 6,   0.30,  0.00,  0.20,  0.30,  0.15,  0.05),
        new SpawnStage(120,   2,   4, 6,   0.40,  0.10,  0.25,  0.40,  0.22,  0.08),
        new SpawnStage(180,   2,   4, 5,   0.45,  0.20,  0.28,  0.45,  0.28,  0.12),
        new SpawnStage(300,   2,   3, 5,   0.45,  0.30,  0.30,  0.50,  0.33,  0.15),
        new SpawnStage(420,   2,   3, 4,   0.40,  0.40,  0.33,  0.55,  0.37,  0.18),
        new SpawnStage(600,   2,   3, 4,   0.35,  0.50,  0.35,  0.60,  0.40,  0.20),
    };

    // 쿨타임
    public const double CooldownBeats = 1.0;
    public const double CooldownBufferBeats = 0.25;
    // 적
    public const float BaseEnemySpeed = 0.4f;          // 기존 0.65
    public const float EnemySpeedJitter = 0.15f;       // 속도 = Base * (1 ± Jitter)
    public const float BaseEnemyHalfSize = 0.21f;
    public const int EnemyPoolSize = 48;
    // 스폰 (스텝 = 반 박)
    public const int BreathIntervalSteps = 8;
    public const int BreathRunMax = 2;
    public const int RunGapSteps = 1;                  // 색 뭉치 사이 추가 간격
    public static readonly double[] DifficultyThresholds = { 17.8, 34.3, 49.8 };   // 음악 편성 밀도 (SoundManager.Intensity); 스폰은 SpawnStages
    public const float ChordShapeScale = 0.7f;         // 화음 적의 도형 하나 크기 (기본 대비)
    public const float SplitterScale = 1.5f;            // 분열형 크기 (기본 대비)
    public const float SplitChildScale = 0.7f;          // 새끼 크기 (기본 대비)
    public const float ChargerScale = 0.75f;            // 돌격형 크기 (기본 대비)
    public const float ChargerSpeedMultiplier = 2.0f;   // 돌격형 기본 속도 (기본 적 대비)
    public const float ChargerBoostMultiplier = 1.5f;   // 틀린 색 한 번당 속도 배율
    public const int ChargerMaxBoosts = 2;
    // 긴장도
    public const float TensionOuterRadius = 0.55f;     // 경기장 반지름 대비
    public const float TensionCrowdBand = 0.35f;       // 접촉~외곽 구간 중 무리로 세는 안쪽 비율
    public const float TensionPerCrowd = 0.15f;
}
