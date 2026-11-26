public static class GameplayTuning
{
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
    public static readonly int[] FlowIntervalSteps = { 3, 2, 2, 2 };   // 밀도 단계 0..3
    public static readonly int[] RunMin = { 3, 4, 5, 5 };
    public static readonly int[] RunMax = { 4, 5, 7, 7 };
    public const int BreathRunMax = 2;
    public const int RunGapSteps = 1;                  // 색 뭉치 사이 추가 간격
    public const double HiddenEnemyStart = 30.0;
    public const int HiddenEvery = 4;                  // n번째 스폰마다 회색
    public static readonly double[] DifficultyThresholds = { 17.8, 34.3, 49.8 };
    public const int DenseSection = 2;                 // SectionIndex가 이 이상이면 밀도 +1
    public const double SpecialEnemyStart = 60.0;      // 특수 적이 나오기 시작하는 라운드 시간(초)
    public const double ChordPairChance = 0.5;         // 새 색 뭉치의 첫 적이 화음 적일 확률
    public const float ChordShapeScale = 0.7f;         // 화음 적의 도형 하나 크기 (기본 대비)
    public const float SplitterScale = 1.5f;            // 분열형 크기 (기본 대비)
    public const float SplitChildScale = 0.7f;          // 새끼 크기 (기본 대비)
    public const float ChargerScale = 0.75f;            // 돌격형 크기 (기본 대비)
    public const float ChargerSpeedMultiplier = 2.0f;   // 돌격형 기본 속도 (기본 적 대비)
    public const float ChargerBoostMultiplier = 1.5f;   // 틀린 색 한 번당 속도 배율
    public const int ChargerMaxBoosts = 2;
    public const double ChargerStart = 70.0;            // 돌격형이 나오기 시작하는 라운드 시간(초)
    public const double SplitterStart = 80.0;           // 분열형이 나오기 시작하는 라운드 시간(초)
    public const double ChargerChance = 0.3;            // 색 뭉치가 끝날 때 돌격형이 끼어들 확률
    public const double SplitterChance = 0.12;          // 뭉치 안의 적이 분열형일 확률
    // 긴장도
    public const float TensionOuterRadius = 0.55f;     // 경기장 반지름 대비
    public const float TensionCrowdBand = 0.35f;       // 접촉~외곽 구간 중 무리로 세는 안쪽 비율
    public const float TensionPerCrowd = 0.15f;
}
