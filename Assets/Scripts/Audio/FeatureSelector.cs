using System;

/// <summary>
/// Picks the "feature loop" (a guest trumpet/whistle part laid over the accompaniment) for each 4-bar phrase,
/// one phrase ahead like <see cref="SectionSelector"/>. FMOD plays loop <c>Id</c> while its FeatureLoop
/// parameter equals that id; the notes live at every phrase start of every tempo section
/// (FMOD/Tools/feature_loops.py), which also generates the loop table (<see cref="FeatureLoopNotes.Loops"/>).
/// </summary>
public sealed class FeatureSelector
{
    /// <summary>Flags, kept in sync with FLAGS in FMOD/Tools/feature_loops.py.</summary>
    [Flags]
    public enum Instrument
    {
        Trumpet = 1,
        Whistle = 2,
        Both = Trumpet | Whistle,
        Mallets = 4,
        Percussion = 8,
        Synth = 16,
        Guitar = 32,
    }

    public sealed class Loop
    {
        public readonly int Id;
        public readonly string Name;
        /// <summary>First bar of the phrase (0..3) the loop plays in, and how many bars it lasts.</summary>
        public readonly int StartBar;
        public readonly int Bars;
        public readonly Instrument Instruments;
        /// <summary>A fever guest: plays only over fever (picked by <see cref="FeverSelector"/>), never for an ordinary phrase.</summary>
        public readonly bool Fever;

        public Loop(int id, string name, int startBar, int bars, Instrument instruments, bool fever = false)
        {
            Fever = fever;
            Id = id;
            Name = name;
            StartBar = startBar;
            Bars = bars;
            Instruments = instruments;
        }

        public bool PlaysInBar(int barOfPhrase) => barOfPhrase >= StartBar && barOfPhrase < StartBar + Bars;
    }

    /// <summary>Generated from FMOD/Tools/feature_loops.py together with the notes, so it cannot drift from FMOD.</summary>
    static Loop[] Loops => FeatureLoopNotes.Loops;

    // A guest should feel like an event: its chance follows the arrangement rung (ComboLadder.GuestChance),
    // two phrases in a row only from the rung where ComboLadder.GuestsMayRepeat, and never in the first two
    // phrases while the player is still finding the beat.
    const int FirstPhrase = 2;
    const int RecentMemory = 4;

    readonly Random _random;
    readonly int[] _recent = new int[RecentMemory];
    int _phrase;

    /// <summary>Loop of the phrase being heard (null = none).</summary>
    public Loop Current { get; private set; }
    /// <summary>Loop already chosen for the next phrase (null = none).</summary>
    public Loop Next { get; private set; }

    public FeatureSelector(int seed)
    {
        // Own stream so adding feature loops does not change the section order for a given seed.
        _random = seed == 0 ? new Random() : new Random(unchecked(seed * 7919 + 17));
    }

    /// <summary>Called at each phrase start, right after <see cref="SectionSelector.Advance"/>.</summary>
    /// <param name="next">The phrase that was just queued.</param>
    /// <param name="energy">Target energy 0..3 (difficulty): busier, longer loops get likelier.</param>
    /// <param name="allowed">False when fever is queued or playing during the next phrase.</param>
    /// <param name="excludeId">A loop that would clash with the next phrase's bed style (0 = none).</param>
    /// <param name="rung">The combo ladder's rung when the phrase starts: sets the chance and whether guests may follow each other.</param>
    public Loop Advance(SectionSelector.Phrase next, int energy, bool allowed, int rung, int excludeId = 0)
    {
        Current = Next;
        _phrase++;
        Next = allowed && !next.Breath && (Current == null || ComboLadder.GuestsMayRepeat(rung))
               && _phrase >= FirstPhrase && _random.NextDouble() < ComboLadder.GuestChance(rung)
            ? Choose(energy, excludeId)
            : null;
        if (Next != null)
        {
            Array.Copy(_recent, 0, _recent, 1, RecentMemory - 1);
            _recent[0] = Next.Id;
        }
        return Next;
    }

    /// <summary>
    /// Drops the loop chosen for the next phrase (fever was queued over it). It was never heard, so it also
    /// leaves the recent-loop memory instead of lowering its own odds next time.
    /// </summary>
    public void CancelNext()
    {
        if (Next == null)
            return;
        // Advance pushed Next to the front of the memory; pop it back off.
        Array.Copy(_recent, 1, _recent, 0, RecentMemory - 1);
        _recent[RecentMemory - 1] = 0;
        Next = null;
    }

    /// <summary>
    /// Drops the loop of the phrase being heard (a broken combo faded it out). It was heard, so the recent-loop memory
    /// keeps it.
    /// </summary>
    public void CancelCurrent() => Current = null;

    Loop Choose(int energy, int excludeId)
    {
        energy = Math.Max(0, Math.Min(energy, SectionSelector.SectionCount - 1));
        var weights = new double[Loops.Length];
        double total = 0;
        for (int i = 0; i < Loops.Length; i++)
        {
            var loop = Loops[i];
            double weight = loop.Bars == 4 ? 0.6 + 0.3 * energy : 1.0;
            if (loop.Fever || loop.Id == _recent[0] || loop.Id == excludeId)
                weight = 0;
            else if (Array.IndexOf(_recent, loop.Id) >= 0)
                weight *= 0.4;
            weights[i] = weight;
            total += weight;
        }

        double roll = _random.NextDouble() * total;
        for (int i = 0; i < Loops.Length; i++)
        {
            roll -= weights[i];
            if (roll < 0)
                return Loops[i];
        }
        return Loops[Loops.Length - 1];
    }
}
