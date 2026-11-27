using System;

/// <summary>
/// Picks the chord progression of each phrase (FMOD/Tools/progressions.py, PatternTable.ProgressionNames), one phrase
/// ahead like the section and the guest loop. A progression usually holds for one or two phrases, then moves to another
/// one, never straight back to the one before. The mood of the moment tilts the pick: high tension favours the dark
/// (minor-coloured) progressions, breath phrases and the dreamy style the soft ones, fever the bright ones; the original
/// C - Am - F - G stays a little likelier than the rest, as the home theme.
/// </summary>
public sealed class ProgressionSelector
{
    public const int Home = 0;
    public const byte MoodBright = 0, MoodDark = 1, MoodSoft = 2;

    // How strongly a matching mood tilts the pick, and the home theme's extra weight.
    const double MoodWeight = 3.0;
    const double HomeWeight = 1.5;
    const float DarkTension = 0.5f;
    // Chance that a progression plays a second phrase before it may change.
    const double SecondPhraseChance = 0.6;

    readonly Random _random;
    int _phrasesHeld;
    int _holdFor;
    int _previous = -1;

    /// <summary>Progression of the phrase being played.</summary>
    public int Current { get; private set; }
    /// <summary>Progression of the next phrase (already chosen).</summary>
    public int Next { get; private set; }

    public ProgressionSelector(int seed)
    {
        // Own stream so progressions do not change the section, variation or guest order for a given seed.
        _random = seed == 0 ? new Random() : new Random(unchecked(seed * 7919 + 11));
        // The round opens on a bright progression (the home theme half the time).
        Current = Next = _random.NextDouble() < 0.5 ? Home : Pick(-1, MoodBright, fever: false);
        _phrasesHeld = 0;
        _holdFor = HoldLength();
    }

    public static string Name(int progression) =>
        progression >= 0 && progression < PatternTable.ProgressionCount ? PatternTable.ProgressionNames[progression] : "";

    /// <summary>Chord id (PatternTable.ChordNames) of a bar (0-3) of a progression.</summary>
    public static int ChordAt(int progression, int bar)
    {
        progression = Math.Max(0, Math.Min(progression, PatternTable.ProgressionCount - 1));
        return PatternTable.ProgressionChords[progression, ((bar % 4) + 4) % 4];
    }

    /// <summary>
    /// Called when a new phrase starts: the queued progression becomes current and the next phrase's is chosen.
    /// </summary>
    /// <param name="breath">The next phrase is a breath phrase.</param>
    /// <param name="fever">The next phrase is fever (or its build-up).</param>
    /// <param name="tension">The game's tension now (0..1).</param>
    /// <param name="dreamy">The next phrase plays the dreamy style.</param>
    public int Advance(bool breath, bool fever, float tension, bool dreamy)
    {
        Current = Next;
        _phrasesHeld++;
        if (_phrasesHeld < _holdFor)
            return Next;   // the progression plays another phrase

        int mood = fever ? MoodBright : breath || dreamy ? MoodSoft : tension >= DarkTension ? MoodDark : -1;
        int next = Pick(Current, mood, fever);
        _previous = Current;
        Next = next;
        _phrasesHeld = 0;
        _holdFor = HoldLength();
        return Next;
    }

    int HoldLength() => _random.NextDouble() < SecondPhraseChance ? 2 : 1;

    int Pick(int current, int mood, bool fever)
    {
        int count = PatternTable.ProgressionCount;
        if (count <= 1)
            return 0;
        double total = 0;
        var weights = new double[count];
        for (int p = 0; p < count; p++)
        {
            if (p == current || (p == _previous && count > 2))
                continue;
            double w = 1.0;
            if (mood >= 0 && PatternTable.ProgressionMood[p] == mood)
                w *= MoodWeight;
            if (p == Home)
                w *= HomeWeight;
            weights[p] = w;
            total += w;
        }
        if (total <= 0)
            return (current + 1) % count;
        double roll = _random.NextDouble() * total;
        for (int p = 0; p < count; p++)
        {
            roll -= weights[p];
            if (weights[p] > 0 && roll < 0)
                return p;
        }
        for (int p = count - 1; p >= 0; p--)
            if (weights[p] > 0)
                return p;
        return 0;
    }
}
