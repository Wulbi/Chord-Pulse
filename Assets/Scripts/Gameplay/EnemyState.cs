public enum EnemyKind { Basic, Splitter, Charger }

public sealed class EnemyState
{
    public EnemyKind Kind;        // default Basic
    public int Boosts;            // times a charger has sped up
    public PulseState ImmuneTo;   // this pulse cannot hit it (split child)
    public int ColorIndex;
    public float Distance;      // core center to enemy center
    public float Speed;
    public float HalfSize;
    public bool IsHidden;
    public int SecondColorIndex = -1;    // >= 0: intact chord pair (ColorIndex + SecondColorIndex)
    public int StrippedColorIndex = -1;  // >= 0: colour already stripped (used for the mixed chord at finish)
    public bool IsChordPair => SecondColorIndex >= 0;
}

public sealed class PulseState
{
    public int ColorIndex;
    public float Radius;
    public bool IsFever;
    public int Kills;           // cumulative kills of this pulse
    public bool HitWrongColor;
}

public enum PulseEventType { Kill, Reveal, Strip, Split, Boost }

public readonly struct PulseEvent
{
    public readonly PulseEventType Type;
    public readonly EnemyState Enemy;
    public readonly int Points;   // Kill: n for this pulse's n-th kill. Reveal/Strip: 0

    public readonly EnemyState Parent;   // Split: the parent that broke apart. Otherwise null

    public PulseEvent(PulseEventType type, EnemyState enemy, int points, EnemyState parent = null)
    {
        Type = type;
        Enemy = enemy;
        Points = points;
        Parent = parent;
    }
}

public enum PulseOutcome { Travelling, Blocked, ReachedEdge }
