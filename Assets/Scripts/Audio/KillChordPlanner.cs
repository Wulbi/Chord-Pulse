using System;
using System.Collections.Generic;

/// <summary>The bed state a kill beat plays in, by beat. Implemented by SoundManager.</summary>
public interface IKillBedState
{
    /// <summary>True if the beat lies in fever or its build-up bar (kills there play the original chords).</summary>
    bool FeverZone(int beat);
    int StyleAt(int beat);
    int ComboAt(int beat);
    float Tension { get; }
}

/// <summary>
/// Decides which beat a kill's chord joins and which one-shot notes that adds (Docs/AdaptiveAudioPlan.md section 10).
/// Pure: no FMOD, and no allocation per call once the per-beat records have been pooled.
/// </summary>
public sealed class KillChordPlanner
{
    // A kill targets the next beat if it is at least this far away, otherwise the beat after, so the chord's one-shots
    // always get enough lead to be scheduled sample-accurately.
    public const double ChordLeadBeats = 0.4;
    public const int MaxVoicing = 3;
    public const int ColorCount = 3;

    const int StepsPerBeat = SoundManager.StepsPerBeat;

    sealed class Record
    {
        public readonly int[] Voicing = new int[ColorCount];
        public int Style;
        public int Combo;
        public float Tension;
    }

    readonly Dictionary<int, Record> _beats = new Dictionary<int, Record>();
    readonly Stack<Record> _pool = new Stack<Record>();
    readonly List<int> _expired = new List<int>();
    readonly List<KillNote> _before = new List<KillNote>();
    readonly List<KillNote> _after = new List<KillNote>();
    int _lastChordStep = -1;

    /// <summary>
    /// How early a beat's notes are handed to FMOD: <see cref="OneShotScheduler.DispatchWindowBeats"/> on every beat.
    /// The music has no timeline jump any more (the clock is <c>MusicTransport</c>), so phrase landings need no wider window.
    /// </summary>
    public static double DispatchWindow(int beat) => OneShotScheduler.DispatchWindowBeats;

    /// <summary>
    /// Adds a kill to a beat's chord and returns that beat's step (-1: none). <paramref name="chordStep"/> >= 0 joins that
    /// step's chord, or returns -1 if it has already sounded. Only the notes this kill adds go into <paramref name="into"/>.
    /// </summary>
    public int Queue(double nowBeat, int color, int chordStep, IKillBedState bed, List<OneShotNote> into)
    {
        if (color < 0 || color >= ColorCount)
            return -1;
        // A beat whose time has passed has sounded; its chord is closed.
        _lastChordStep = Math.Max(_lastChordStep, (int)Math.Floor(nowBeat) * StepsPerBeat);

        int target;
        if (chordStep >= 0)
        {
            if (chordStep <= _lastChordStep)
                return -1;
            target = chordStep;
        }
        else
        {
            int beat = (int)Math.Floor(nowBeat) + 1;
            if (beat <= nowBeat + ChordLeadBeats)
                beat++;
            target = beat * StepsPerBeat;
        }
        AddKill(target / StepsPerBeat, color, bed, into);
        return target;
    }

    void AddKill(int beat, int color, IKillBedState bed, List<OneShotNote> into)
    {
        if (!_beats.TryGetValue(beat, out var chord))
        {
            chord = _pool.Count > 0 ? _pool.Pop() : new Record();
            Array.Clear(chord.Voicing, 0, ColorCount);
            ReadState(chord, beat, bed);
            _beats.Add(beat, chord);
        }
        int before = chord.Voicing[color];
        int after = Math.Min(before + 1, MaxVoicing);
        if (after == before)
            return;
        chord.Voicing[color] = after;

        int chordIndex = beat / SoundManager.BeatsPerBar % SectionSelector.BarsPerPhrase;
        _before.Clear();
        _after.Clear();
        KillChordVoicer.Voice(chord.Style, color, chordIndex, before, chord.Combo, chord.Tension, _before);
        KillChordVoicer.Voice(chord.Style, color, chordIndex, after, chord.Combo, chord.Tension, _after);
        foreach (var note in _after)
        {
            if (RemoveSame(_before, note))
                continue; // already scheduled by an earlier kill of this beat
            into.Add(ToOneShot(note, beat));
        }
    }

    // The bed state the beat plays in, fixed by its first kill so later kills add notes of the same sound.
    // Build-up and fever play the original kill chords (style 0, combo 0, tension 0), like the bed.
    static void ReadState(Record chord, int beat, IKillBedState bed)
    {
        bool fever = bed.FeverZone(beat);
        chord.Style = fever ? SoundManager.StyleSynthpop : bed.StyleAt(beat);
        chord.Combo = fever ? 0 : bed.ComboAt(beat);
        chord.Tension = fever ? 0f : bed.Tension;
    }

    static OneShotNote ToOneShot(KillNote note, int beat) =>
        new OneShotNote(note.Instrument, OutputBus.KillChords, note.Midi, beat, note.LengthBeats, 0.0,
            note.GainDb, KillChordVoicer.OnsetShiftSeconds(note));

    /// <summary>
    /// The bed state changed for beats from <paramref name="fromBeat"/> on (fever was queued): re-reads it for every
    /// open beat there and outputs that beat's complete note list. Returns the first beat that can be restated; the
    /// caller then cancels the old notes with <c>CancelFrom(returned, planner)</c> and schedules everything in
    /// <paramref name="into"/>.
    /// Only beats at least <see cref="ChordLeadBeats"/> ahead of <paramref name="nowBeat"/> are touched (the lead a fresh
    /// kill needs): a nearer beat may already have notes that started (their onset is shifted before the beat), and
    /// CancelFrom keeps those, so re-emitting the full chord could double them. Nearer beats keep their old voicing
    /// and their already-scheduled notes.
    /// </summary>
    public int Restate(double nowBeat, int fromBeat, IKillBedState bed, List<OneShotNote> into)
    {
        _lastChordStep = Math.Max(_lastChordStep, (int)Math.Floor(nowBeat) * StepsPerBeat);
        int first = fromBeat;
        if (first - nowBeat < ChordLeadBeats)
            first = (int)Math.Ceiling(nowBeat + ChordLeadBeats);
        while (first - nowBeat < ChordLeadBeats)
            first++;
        foreach (var pair in _beats)
        {
            int beat = pair.Key;
            if (beat < first || beat * StepsPerBeat <= _lastChordStep)
                continue;
            var chord = pair.Value;
            ReadState(chord, beat, bed);
            int chordIndex = beat / SoundManager.BeatsPerBar % SectionSelector.BarsPerPhrase;
            for (int color = 0; color < ColorCount; color++)
            {
                if (chord.Voicing[color] <= 0)
                    continue;
                _after.Clear();
                KillChordVoicer.Voice(chord.Style, color, chordIndex, chord.Voicing[color], chord.Combo, chord.Tension, _after);
                foreach (var note in _after)
                    into.Add(ToOneShot(note, beat));
            }
        }
        return first;
    }

    /// <summary>Forgets beats that ended more than a beat ago.</summary>
    public void Expire(double nowBeat)
    {
        if (_beats.Count == 0)
            return;
        _expired.Clear();
        foreach (int beat in _beats.Keys)
            if (beat < nowBeat - 1)
                _expired.Add(beat);
        foreach (int beat in _expired)
        {
            _pool.Push(_beats[beat]);
            _beats.Remove(beat);
        }
    }

    public void Clear()
    {
        foreach (var chord in _beats.Values)
            _pool.Push(chord);
        _beats.Clear();
        _lastChordStep = -1;
    }

    static bool RemoveSame(List<KillNote> notes, KillNote note)
    {
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            if (n.Instrument == note.Instrument && n.Midi == note.Midi && n.LengthBeats == note.LengthBeats && n.GainDb == note.GainDb)
            {
                notes.RemoveAt(i);
                return true;
            }
        }
        return false;
    }
}
