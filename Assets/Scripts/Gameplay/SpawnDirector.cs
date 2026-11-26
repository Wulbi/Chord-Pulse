public struct SpawnContext
{
    public int Step;            // SoundManager.CurrentStep (half-beat units, one bar = 8)
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
    public int SecondColorIndex;   // chord pair: second colour; otherwise -1
    public EnemyKind Kind;         // default Basic
}

/// Decides when and what to spawn, following the music (flow and breath). Pure rules, no Unity types.
public sealed class SpawnDirector
{
    public const int ColorCount = 3;
    public const int StepsPerBeat = 2;
    public const int StepsPerBar = 8;
    const int OpeningCount = 3;

    readonly System.Random _random;
    int _openingDone;
    int _nextStep;
    int _runRemaining;
    int _runColor;
    int _spawnNumber;
    bool _prevFever;
    bool _noPairNextRun;
    bool _chargerPending;

    public SpawnDirector(System.Random random)
    {
        _random = random;
        Reset();
    }

    public void Reset()
    {
        _openingDone = 0;
        _nextStep = 0;
        _runRemaining = 0;
        _runColor = -1;
        _spawnNumber = 0;
        _prevFever = false;
        _noPairNextRun = false;
        _chargerPending = false;
    }

    /// Call every frame. Returns true when an enemy should spawn now; at most one per call.
    public bool TryNext(in SpawnContext context, out SpawnOrder order)
    {
        order = default;
        int step = context.Step;
        if (step < 0) return false;

        if (context.Fever != _prevFever)
        {
            _prevFever = context.Fever;
            _runRemaining = 0;
            _nextStep = step;
            _noPairNextRun = true;
            _chargerPending = false;
        }

        if (context.Fever)
        {
            if (step < _nextStep) return false;
            order = MakeOrder(_random.Next(ColorCount), false);
            _nextStep = step + StepsPerBeat;
            return true;
        }

        if (_openingDone < OpeningCount)
        {
            if (step < _nextStep) return false;
            order = MakeOrder(_openingDone, false);
            _runColor = _openingDone;
            _openingDone++;
            _nextStep = step - step % StepsPerBar + StepsPerBar;
            return true;
        }

        if (step < _nextStep) return false;

        int level = DensityLevel(context.Section, context.RoundSeconds);

        if (_chargerPending)
        {
            _chargerPending = false;
            // Breath phrases have no chargers: drop the reservation and go on with the normal run logic below.
            if (!context.Breath)
            {
                // A charger sits between two runs: it leaves the run state and spawn counter untouched.
                order = MakeOrder(_random.Next(ColorCount), false, -1, EnemyKind.Charger);
                _nextStep = step + GameplayTuning.FlowIntervalSteps[level];
                return true;
            }
        }

        int secondColor = -1;
        if (_runRemaining <= 0)
        {
            int previousColor = _runColor;
            _runColor = (_runColor + 1 + _random.Next(ColorCount - 1)) % ColorCount;
            _runRemaining = context.Breath
                ? _random.Next(1, GameplayTuning.BreathRunMax + 1)
                : _random.Next(GameplayTuning.RunMin[level], GameplayTuning.RunMax[level] + 1);
            // Drawn only when a pair is possible, so the random sequence before SpecialEnemyStart is unchanged.
            // Never right after a fever phrase: the previous colour is stale then.
            if (_noPairNextRun)
                _noPairNextRun = false;
            else if (context.RoundSeconds >= GameplayTuning.SpecialEnemyStart
                && _random.NextDouble() < GameplayTuning.ChordPairChance)
                secondColor = previousColor;
        }

        if (context.Breath && _runRemaining > GameplayTuning.BreathRunMax)
            _runRemaining = GameplayTuning.BreathRunMax;

        _spawnNumber++;
        bool hidden = secondColor < 0
                      && context.RoundSeconds >= GameplayTuning.HiddenEnemyStart
                      && _spawnNumber % GameplayTuning.HiddenEvery == 0;
        // Drawn only when the conditions hold, so earlier random sequences are unchanged.
        var kind = EnemyKind.Basic;
        if (!hidden && secondColor < 0
            && context.RoundSeconds >= GameplayTuning.SplitterStart
            && _random.NextDouble() < GameplayTuning.SplitterChance)
            kind = EnemyKind.Splitter;
        order = MakeOrder(_runColor, hidden, secondColor, kind);
        _runRemaining--;

        if (_runRemaining <= 0
            && !context.Breath
            && context.RoundSeconds >= GameplayTuning.ChargerStart
            && _random.NextDouble() < GameplayTuning.ChargerChance)
            _chargerPending = true;

        int interval = context.Breath ? GameplayTuning.BreathIntervalSteps : GameplayTuning.FlowIntervalSteps[level];
        if (_runRemaining <= 0) interval += GameplayTuning.RunGapSteps;
        _nextStep = step + interval;
        return true;
    }

    SpawnOrder MakeOrder(int color, bool hidden, int secondColor = -1, EnemyKind kind = EnemyKind.Basic)
    {
        double jitter = (_random.NextDouble() * 2.0 - 1.0) * GameplayTuning.EnemySpeedJitter;
        double baseSpeed = kind == EnemyKind.Charger
            ? GameplayTuning.BaseEnemySpeed * GameplayTuning.ChargerSpeedMultiplier
            : GameplayTuning.BaseEnemySpeed;
        return new SpawnOrder
        {
            ColorIndex = color,
            Hidden = hidden,
            Speed = (float)(baseSpeed * (1.0 + jitter)),
            SecondColorIndex = secondColor,
            Kind = kind,
        };
    }

    /// Number of DifficultyThresholds the round time has passed, 0..3.
    public static int Difficulty(double roundSeconds)
    {
        int d = 0;
        foreach (double t in GameplayTuning.DifficultyThresholds)
            if (roundSeconds >= t) d++;
        return d;
    }

    public static int DensityLevel(int section, double roundSeconds)
    {
        int level = Difficulty(roundSeconds) + (section >= GameplayTuning.DenseSection ? 1 : 0);
        return level > 3 ? 3 : level;
    }
}
