public struct SpawnContext
{
    public int Step;            // SoundManager.CurrentStep (half-beat units, one bar = 8)
    public bool Fever;
    public bool Breath;         // SoundManager.IsBreath
    public int Section;         // SoundManager.SectionIndex 0..3 (not read by SpawnDirector: intervals are in beats, so tempo already scales the rate)
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

        // The row in force now. A run drawn under an earlier row keeps its remaining length; interval and chances follow this row.
        var stage = GameplayTuning.SpawnStages[StageIndex(context.RoundSeconds)];

        if (_chargerPending)
        {
            _chargerPending = false;
            // Breath phrases have no chargers: drop the reservation and go on with the normal run logic below.
            if (!context.Breath)
            {
                // A charger sits between two runs: it leaves the run state untouched.
                order = MakeOrder(_random.Next(ColorCount), false, -1, EnemyKind.Charger);
                _nextStep = step + stage.IntervalSteps;
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
                : _random.Next(stage.RunMin, stage.RunMax + 1);
            // Every chance is drawn only when it is above 0, so rows without a special leave the random sequence alone.
            // Never right after a fever phrase: the previous colour is stale then.
            if (_noPairNextRun)
                _noPairNextRun = false;
            else if (stage.ChordPairChance > 0 && _random.NextDouble() < stage.ChordPairChance)
                secondColor = previousColor;
        }

        if (context.Breath && _runRemaining > GameplayTuning.BreathRunMax)
            _runRemaining = GameplayTuning.BreathRunMax;

        // One special at most per slot: chord pair, else gray, else splitter.
        bool hidden = secondColor < 0 && stage.HiddenChance > 0 && _random.NextDouble() < stage.HiddenChance;
        var kind = EnemyKind.Basic;
        if (!hidden && secondColor < 0
            && stage.SplitterChance > 0 && _random.NextDouble() < stage.SplitterChance)
            kind = EnemyKind.Splitter;
        order = MakeOrder(_runColor, hidden, secondColor, kind);
        _runRemaining--;

        if (_runRemaining <= 0
            && !context.Breath
            && stage.ChargerChance > 0 && _random.NextDouble() < stage.ChargerChance)
            _chargerPending = true;

        int interval = context.Breath ? GameplayTuning.BreathIntervalSteps : stage.IntervalSteps;
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

    /// Row of GameplayTuning.SpawnStages in force: the last one whose StartSeconds has passed (0 before the first).
    public static int StageIndex(double roundSeconds)
    {
        var stages = GameplayTuning.SpawnStages;
        int index = 0;
        for (int i = 1; i < stages.Length && roundSeconds >= stages[i].StartSeconds; i++)
            index = i;
        return index;
    }

    /// Number of DifficultyThresholds the round time has passed, 0..3. Drives the music's arrangement density, not the spawns.
    public static int Difficulty(double roundSeconds)
    {
        int d = 0;
        foreach (double t in GameplayTuning.DifficultyThresholds)
            if (roundSeconds >= t) d++;
        return d;
    }
}
