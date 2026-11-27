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
    /// This slot's colour is not its run's: one slot of another colour inside a run (SpawnStage.MixChance).
    public bool Mixed;
    /// Enemies of this slot, 1..GameplayTuning.MaxGroupSize: same colour and speed, from different directions.
    /// Hidden / SecondColorIndex / Kind describe the first one only; the others are plain Basic enemies.
    public int Count;

    /// The order for the other enemies of the slot: same colour (a mixed slot's own) and speed, never gray, a chord pair or a special kind.
    public SpawnOrder Mate()
    {
        var mate = this;
        mate.Hidden = false;
        mate.SecondColorIndex = -1;
        mate.Kind = EnemyKind.Basic;
        mate.Count = 1;
        return mate;
    }
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
    double _speedScale = 1.0;

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

    /// Call every frame. Returns true when a slot is due now: one order per call, for order.Count enemies (1..3).
    public bool TryNext(in SpawnContext context, out SpawnOrder order)
    {
        order = default;
        int step = context.Step;
        if (step < 0) return false;
        _speedScale = LateSpeedScale(context.RoundSeconds);

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
            // Fever: one slot per beat, plain enemies of any colour (a fever pulse takes them all), in groups as big as
            // the row in force gives, so there is as much to burst as outside fever.
            order = MakeOrder(_random.Next(ColorCount), false);
            var feverStage = GameplayTuning.SpawnStages[StageIndex(context.RoundSeconds)];
            if (feverStage.PairChance + feverStage.TripleChance > 0)
            {
                double roll = _random.NextDouble();
                order.Count = roll < feverStage.TripleChance ? 3 : roll < feverStage.TripleChance + feverStage.PairChance ? 2 : 1;
            }
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
        bool runStart = _runRemaining <= 0;
        if (runStart)
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
        // Mixing: one slot of another colour inside a run. Never the run's first or last slot, so a run still opens
        // and closes in its own colour (a chord pair bridges it, the next run differs from it). A breath phrase has none.
        // The slot's enemies still share one colour; gray / splitter / group size are drawn as for any slot.
        int color = _runColor;
        bool mixed = !context.Breath && !runStart && _runRemaining > 1
            && stage.MixChance > 0 && _random.NextDouble() < stage.MixChance;
        if (mixed)
            color = (_runColor + 1 + _random.Next(ColorCount - 1)) % ColorCount;
        order = MakeOrder(color, hidden, secondColor, kind);
        order.Mixed = mixed;
        // Groups only in flow: a breath phrase is a rest. One draw decides between 1, 2 and 3.
        // A chord pair comes alone: plain mates are bigger, so they would reach a pulse first and block the pair's
        // other colour (the one the player still has selected) before it could strip the pair.
        if (!context.Breath && secondColor < 0 && stage.PairChance + stage.TripleChance > 0)
        {
            double roll = _random.NextDouble();
            order.Count = roll < stage.TripleChance ? 3 : roll < stage.TripleChance + stage.PairChance ? 2 : 1;
        }
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
            Speed = (float)(baseSpeed * (1.0 + jitter) * _speedScale),
            SecondColorIndex = secondColor,
            Kind = kind,
            Count = 1,
        };
    }

    /// Degrees a mate is turned from the enemy spawned just before it, for unit in [0, 1): 60..150. Two such steps put
    /// the third enemy of a slot 120..300 degrees from the first, so no two of a group share a direction.
    public static float MateAngleStep(double unit) => 60f + 90f * (float)unit;

    /// Row of GameplayTuning.SpawnStages in force: the last one whose StartSeconds has passed (0 before the first).
    public static int StageIndex(double roundSeconds)
    {
        var stages = GameplayTuning.SpawnStages;
        int index = 0;
        for (int i = 1; i < stages.Length && roundSeconds >= stages[i].StartSeconds; i++)
            index = i;
        return index;
    }

    /// Speed factor for every enemy: 1 until GameplayTuning.LateSpeedStart, then +LateSpeedStep per whole
    /// LateSpeedStepSeconds after it, without a cap.
    public static double LateSpeedScale(double roundSeconds)
    {
        double late = roundSeconds - GameplayTuning.LateSpeedStart;
        if (late <= 0) return 1.0;
        return 1.0 + GameplayTuning.LateSpeedStep * System.Math.Floor(late / GameplayTuning.LateSpeedStepSeconds);
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
