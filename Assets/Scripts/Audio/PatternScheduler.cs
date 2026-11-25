using System;

/// <summary>
/// Schedules the notes of the generated <see cref="PatternTable"/> through the shared <see cref="OneShotScheduler"/>.
/// Every <see cref="Update"/> hands over the notes whose beat lies in (scheduledUntil, now + lookahead]; which clips
/// play is decided per note by <see cref="IArrangement"/> at the note's own step. Pure: no FMOD, no Unity API, and no
/// allocation on the Update path (apart from what OneShotScheduler.Schedule itself allocates per note).
/// The lookahead must be long enough that a note is handed over before its start: at the fastest tempo it (in seconds)
/// has to exceed <see cref="MaxOnsetSeconds"/> plus the dispatch lead (the dispatch window, 1 beat after a phrase
/// boundary), or early-onset notes on phrase starts would be late. Notes are never scheduled before beat 0.
/// </summary>
public sealed class PatternScheduler
{
    /// <summary>Steps per beat of <c>IArrangement.At</c>. Must equal SoundManager.StepsPerBeat (EditMode test); kept here so this file has no game-code dependency.</summary>
    public const int StepsPerBeat = 2;

    // The arrangement is asked once per 8th-note step (StepsPerBeat), at floor(beat * StepsPerBeat) of each
    // note, not per 16th: every state boundary the game has (phrase, bar, step) is a multiple of a step, so a note on a
    // 16th position (x.25, x.75) shares the state of the 8th step it lies in. Exact for those boundaries.
    const double PhraseBeats = PatternTable.PhraseBeats;

    /// <summary>
    /// Slack on top of <see cref="MaxOnsetSeconds"/> for Invalidate: the schedule offset (about 2.3 ms) and a mixer block or
    /// frame between the beat the caller read and the DSP clock that decides "already started".
    /// </summary>
    public const double OnsetMarginSeconds = 0.05;

    readonly OneShotScheduler _shots;
    readonly IArrangement _arrangement;
    readonly double _lookahead;
    readonly ulong _layerMask;
    readonly double _dispatchWindow;
    ArrangementState[] _states = new ArrangementState[64];
    double _scheduledUntil = double.NaN; // NaN: nothing scheduled yet, the first Update starts at its own beat
    bool _includeLower; // the next window includes its lower bound (set by Invalidate: notes at >= from were cancelled)

    /// <param name="layerMask">Bit = <see cref="PatternClip.Layer"/>. Layers of 64 and above cannot be masked (always on); the table has 13.</param>
    public PatternScheduler(OneShotScheduler shots, IArrangement arrangement, double lookaheadBeats = 1.0,
        ulong layerMask = ulong.MaxValue, double dispatchWindowBeats = 0)
    {
        if (!(lookaheadBeats > 0))
            throw new ArgumentOutOfRangeException(nameof(lookaheadBeats), "the lookahead must be positive");
        _shots = shots ?? throw new ArgumentNullException(nameof(shots));
        _arrangement = arrangement ?? throw new ArgumentNullException(nameof(arrangement));
        _lookahead = lookaheadBeats;
        _layerMask = layerMask;
        _dispatchWindow = dispatchWindowBeats;
        double max = 0;
        foreach (var n in PatternTable.Notes)
            max = Math.Max(max, InstrumentTable.OnsetSeconds(n.Instrument, n.Midi));
        MaxOnsetSeconds = max;
    }

    /// <summary>The largest sample onset of any pattern note: such a note starts this long before its beat.</summary>
    public double MaxOnsetSeconds { get; }

    /// <summary>Notes accepted by the one-shot scheduler since the last <see cref="Reset"/>.</summary>
    public int ScheduledCount { get; private set; }

    /// <summary>A note was handed to the one-shot scheduler; the second argument is its absolute beat.</summary>
    public event Action<PatternNote, double> NoteScheduled;

    /// <summary>
    /// Hands over the notes in (scheduledUntil, nowBeat + lookahead]; notes at or before nowBeat are never scheduled and
    /// nothing before beat 0 is. A first call with nowBeat &lt; 0 takes beat 0's notes, so to hear them the caller must
    /// start calling this before beat 0 (e.g. from the moment the clock has an anchor); a first call at or after beat 0
    /// starts with (nowBeat, ...].
    /// </summary>
    public void Update(double nowBeat)
    {
        double hi = nowBeat + _lookahead;
        double lo = double.IsNaN(_scheduledUntil) ? nowBeat : Math.Max(_scheduledUntil, nowBeat);
        // Invalidate's inclusive cut only applies while it is still ahead of now (a note at or before now is never taken).
        bool include = _includeLower && _scheduledUntil > nowBeat;
        _includeLower = false;
        double scheduledTo = Math.Max(hi, lo);
        if (lo < 0)
        {
            lo = 0; // beat 0's notes are taken, nothing before them
            include = true;
        }
        if (hi >= lo)
            ScheduleRange(lo, hi, include);
        _scheduledUntil = scheduledTo;
    }

    void ScheduleRange(double lo, double hi, bool includeLo)
    {
        int loStep = (int)Math.Floor(lo * StepsPerBeat);
        int hiStep = (int)Math.Floor(hi * StepsPerBeat);
        int steps = hiStep - loStep + 1;
        if (steps > _states.Length)
            _states = new ArrangementState[steps * 2]; // only when a longer window than ever before is asked for
        for (int i = 0; i < steps; i++)
            _states[i] = _arrangement.At(loStep + i);

        int phraseLo = (int)Math.Floor(lo / PhraseBeats);
        int phraseHi = (int)Math.Floor(hi / PhraseBeats);
        var clips = PatternTable.Clips;
        var notes = PatternTable.Notes;
        for (int phrase = phraseLo; phrase <= phraseHi; phrase++)
        {
            double phraseStart = phrase * PhraseBeats;
            for (int c = 0; c < clips.Length; c++)
            {
                if (clips[c].Layer < 0 || (clips[c].Layer < 64 && (_layerMask & (1UL << clips[c].Layer)) == 0))
                    continue;
                int end = clips[c].First + clips[c].Count;
                for (int i = clips[c].First; i < end; i++)
                {
                    double beat = phraseStart + notes[i].Beat;
                    if (includeLo ? beat < lo : beat <= lo)
                        continue;
                    if (beat > hi)
                        break; // the clip's notes are sorted by beat
                    int stateIndex = (int)Math.Floor(beat * StepsPerBeat) - loStep;
                    if (!PatternMatch.Matches(in clips[c], in _states[stateIndex]))
                        continue;
                    ScheduleNote(notes[i], beat);
                }
            }
        }
    }

    void ScheduleNote(in PatternNote n, double beat)
    {
        double window = _dispatchWindow > 0 ? _dispatchWindow : KillChordPlanner.DispatchWindow((int)Math.Floor(beat));
        var shot = new OneShotNote(n.Instrument, n.Bus, n.Midi, beat, n.LengthBeats, n.HoldSeconds, n.GainDb,
            InstrumentTable.OnsetSeconds(n.Instrument, n.Midi));
        if (!_shots.Schedule(in shot, this, window))
            return;
        ScheduledCount++;
        NoteScheduled?.Invoke(n, beat);
    }

    /// <summary>
    /// The arrangement changed for beats from <paramref name="fromBeat"/> on: cancels this scheduler's notes from there and
    /// schedules them again on the next <see cref="Update"/> with the new state. Takes effect from the later of
    /// <paramref name="fromBeat"/> and the first beat whose note cannot have started yet (a note starts up to
    /// <see cref="MaxOnsetSeconds"/> before its beat, and CancelFrom keeps started notes, so re-scheduling those would
    /// double them): nearer notes keep their old state's sound. <paramref name="nowBeat"/> and
    /// <paramref name="beatsPerSecond"/> (clock BPM / 60, must be positive) convert that onset to beats. Notes of other
    /// owners are untouched. Exactly the notes with beat >= the effective start are cancelled and re-stated.
    /// </summary>
    public void Invalidate(double fromBeat, double nowBeat, double beatsPerSecond)
    {
        if (!(beatsPerSecond > 0))
            throw new ArgumentOutOfRangeException(nameof(beatsPerSecond), "the tempo is needed for the onset safety");
        double safe = nowBeat + (MaxOnsetSeconds + OnsetMarginSeconds) * beatsPerSecond;
        double from = Math.Max(fromBeat, safe);
        _shots.CancelFrom(from, this);
        if (_scheduledUntil >= from) // (false for NaN)
        {
            _scheduledUntil = from;
            _includeLower = true; // the note at `from` itself was cancelled and is taken again
        }
    }

    /// <summary>Forgets what was scheduled (the instances are cleaned up by <see cref="OneShotScheduler.StopAll"/>).</summary>
    public void Reset()
    {
        _scheduledUntil = double.NaN;
        _includeLower = false;
        ScheduledCount = 0;
    }
}
