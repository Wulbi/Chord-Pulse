using System;

/// <summary>
/// Picks which music section plays in each 4-bar phrase. Unity decides one phrase ahead (FMOD performs the jump
/// at the phrase boundary via the NextSection parameter), so gameplay always knows what is coming.
/// Sections are ordered by energy: 0 = slowest/calmest ... SectionCount-1 = fastest.
/// </summary>
public sealed class SectionSelector
{
    public const int SectionCount = 4;
    public const int BarsPerPhrase = 4;

    // Tempo of each section: the FMOD tempo markers of event:/Music/ChordPulseGame (Docs/AdaptiveAudioPlan.md).
    static readonly double[] SectionBpm = { 108, 116, 124, 132 };

    /// <summary>BPM of a section (clamped to the known sections).</summary>
    public static double Bpm(int section) => SectionBpm[Math.Max(0, Math.Min(section, SectionBpm.Length - 1))];

    public struct Phrase
    {
        public int Section;
        /// <summary>A deliberate low-energy phrase: slow section, thinner arrangement, sparser spawns.</summary>
        public bool Breath;
    }

    // Breaths only once the round has some tension, never back to back, and not too often.
    const double BreathChance = 0.22;
    const int MinPhrasesBetweenBreaths = 3;
    const int MaxRepeats = 2;

    readonly Random _random;
    int _phrasesSinceBreath;
    int _repeats;

    public Phrase Current { get; private set; }
    public Phrase Next { get; private set; }

    public SectionSelector(int seed)
    {
        _random = seed == 0 ? new Random() : new Random(seed);
        Current = new Phrase { Section = 0 };
        Next = Current;
        _phrasesSinceBreath = MinPhrasesBetweenBreaths;
    }

    /// <summary>
    /// Called when a new phrase starts: the queued phrase becomes current and the one after it is chosen.
    /// </summary>
    /// <param name="energy">Target energy 0..SectionCount-1 (from difficulty).</param>
    /// <param name="allowBreath">False while fever is building or playing.</param>
    /// <param name="hold">Keep the current section (same tempo) for the next phrase, e.g. while fever plays.</param>
    public Phrase Advance(int energy, bool allowBreath, bool hold = false)
    {
        Current = Next;
        Next = hold ? Hold() : Choose(energy, allowBreath);
        return Next;
    }

    /// <summary>Replaces an already-chosen next phrase with the current section (no tempo change at the boundary).</summary>
    public Phrase HoldNext()
    {
        Next = Hold();
        return Next;
    }

    Phrase Hold() => Remember(new Phrase { Section = Current.Section });

    Phrase Choose(int energy, bool allowBreath)
    {
        energy = Math.Max(0, Math.Min(energy, SectionCount - 1));
        _phrasesSinceBreath++;

        if (allowBreath && energy > 0 && !Current.Breath &&
            _phrasesSinceBreath > MinPhrasesBetweenBreaths && _random.NextDouble() < BreathChance)
        {
            _phrasesSinceBreath = 0;
            return Remember(new Phrase { Section = 0, Breath = true });
        }

        int section;
        do
        {
            // Mostly the target energy, sometimes a neighbour, so tempo wanders around the difficulty.
            double roll = _random.NextDouble();
            int offset = roll < 0.25 ? -1 : roll < 0.75 ? 0 : 1;
            section = Math.Max(0, Math.Min(energy + offset, SectionCount - 1));
        }
        while (section == Current.Section && _repeats >= MaxRepeats && SectionCount > 1);

        return Remember(new Phrase { Section = section });
    }

    Phrase Remember(Phrase phrase)
    {
        _repeats = phrase.Section == Current.Section ? _repeats + 1 : 0;
        return phrase;
    }
}
