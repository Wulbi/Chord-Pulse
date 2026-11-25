using System;

/// <summary>
/// A self-running phrase clock on the DSP clock. Beat 0 sounds at the DSP sample given to <see cref="Start"/>; every
/// 16 beats a new phrase begins. Phrase boundaries are fixed once as integer DSP samples
/// (boundary[n+1] = boundary[n] + round(16 * 60 / bpm[n] * sampleRate)), so they never drift, and the shared
/// <see cref="BeatClock"/> is re-anchored on each boundary before <see cref="PhraseStarted"/> is raised.
/// A resync (DSP clock went backwards) that lands on a phrase boundary raises <see cref="PhraseStarted"/> about 0.1 s
/// before that boundary sounds, so <see cref="BeatAt"/> of "now" can be below <c>Phrase * 16</c> in that window.
/// Handlers may call <see cref="Stop"/>, <see cref="Start"/> and <see cref="SetNextTempo"/>; calling
/// <see cref="Update"/> from inside a handler is not supported.
/// </summary>
public sealed class MusicTransport
{
    public const int BeatsPerBar = 4;
    public const int BeatsPerPhrase = 16;

    const double ResyncLeadSeconds = 0.1;

    readonly BeatClock _clock;
    bool _running;
    int _run;               // bumped by Start and Stop, so a handler that restarts the transport ends the old catch-up
    int _phrase = -1;
    double _bpm;            // tempo of the sounding phrase (of phrase 0 before it starts)
    double _nextBpm;        // tempo of the phrase after the sounding one
    int _nextPhrase;        // phrase whose boundary is awaited
    ulong _nextBoundary;    // DSP sample of that boundary
    bool _hasLast;
    ulong _lastNow;

    public MusicTransport(BeatClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool Running => _running;
    public int Phrase => _phrase;
    public int NextPhraseBeat => (_phrase + 1) * BeatsPerPhrase;
    public double Bpm => _running ? _bpm : 0;

    public event Action<int> PhraseStarted;
    public event Action Resynced;

    /// <summary>Beat 0 sounds at <paramref name="dspStart"/>; <paramref name="nextBpm"/> is the tempo from beat 16.</summary>
    public void Start(ulong dspStart, double bpm, double nextBpm)
    {
        if (bpm <= 0 || nextBpm <= 0)
            throw new ArgumentOutOfRangeException(bpm <= 0 ? nameof(bpm) : nameof(nextBpm));
        _clock.Reset();
        _run++;
        _running = true;
        _phrase = -1;
        _bpm = bpm;
        _nextBpm = nextBpm;
        _nextPhrase = 0;
        _nextBoundary = dspStart;
        _hasLast = false;
        _lastNow = 0;
        _clock.Anchor(0, dspStart, bpm);
        _clock.SetTempoFrom(BeatsPerPhrase, nextBpm);
    }

    public void Stop()
    {
        _run++;
        _running = false;
        _phrase = -1;
        _bpm = 0;
        _nextBpm = 0;
        _nextPhrase = 0;
        _nextBoundary = 0;
        _hasLast = false;
        _lastNow = 0;
        _clock.Reset();
    }

    /// <summary>Tempo from <see cref="NextPhraseBeat"/> on; can be replaced until that boundary has passed.</summary>
    public void SetNextTempo(double bpm)
    {
        if (!_running || bpm <= 0)
            return;
        if (_phrase < 0)
        {
            // Before beat 0 the next phrase is phrase 0 itself.
            _bpm = bpm;
            _clock.Anchor(0, _nextBoundary, bpm);
            return;
        }
        _nextBpm = bpm;
        _clock.SetTempoFrom(NextPhraseBeat, bpm);
    }

    public bool BeatAt(double dsp, out double beat) => _clock.BeatAt(dsp, out beat);

    public void Update(ulong dspNow)
    {
        if (!_running)
            return;
        int run = _run;
        if (_hasLast && dspNow < _lastNow)
        {
            Resync(dspNow);
            if (run != _run)
                return; // a handler stopped or restarted the transport: this catch-up belongs to the old run
        }
        _hasLast = true;
        _lastNow = dspNow;

        while (dspNow >= _nextBoundary)
        {
            int n = AdvancePhrase(_nextBoundary);
            PhraseStarted?.Invoke(n);
            if (run != _run)
                return;
        }
    }

    /// <summary>Moves state and clock anchor to the next phrase, which starts at <paramref name="boundary"/>.</summary>
    int AdvancePhrase(ulong boundary)
    {
        int n = _nextPhrase;
        if (n > 0)
            _bpm = _nextBpm; // _nextBpm stays as is: without a SetNextTempo the next phrase keeps this tempo
        _nextPhrase = n + 1;
        _nextBoundary = boundary + PhraseSamples(_bpm);
        _phrase = n;
        _clock.Anchor(n * BeatsPerPhrase, boundary, _bpm);
        return n;
    }

    ulong PhraseSamples(double bpm)
    {
        return (ulong)Math.Round(BeatsPerPhrase * 60.0 / bpm * _clock.SampleRate, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// DSP clock went backwards: re-anchors at the next bar, 0.1 s after <paramref name="dspNow"/>. If that bar is the next
    /// phrase boundary, the boundary counts as passed at once: the phrase advances, the new tempo applies from the anchor,
    /// <see cref="PhraseStarted"/> is raised right after <see cref="Resynced"/>, and the tempo of that phrase can no longer
    /// be replaced by <see cref="SetNextTempo"/>.
    /// </summary>
    void Resync(ulong dspNow)
    {
        int run = _run;
        int startedPhrase = -1;
        ulong lead = (ulong)Math.Round(ResyncLeadSeconds * _clock.SampleRate, MidpointRounding.AwayFromZero);
        ulong newDsp = dspNow + lead;
        if (_phrase < 0)
        {
            // Not yet at beat 0: shift the start itself.
            _nextBoundary = newDsp;
            _clock.Anchor(0, newDsp, _bpm);
            _clock.SetTempoFrom(BeatsPerPhrase, _nextBpm);
        }
        else
        {
            _clock.BeatAt(_lastNow, out double current);
            int bar = (int)Math.Floor(current / BeatsPerBar) + 1;
            int beat = Math.Min(bar * BeatsPerBar, NextPhraseBeat);
            if (beat == NextPhraseBeat)
            {
                startedPhrase = AdvancePhrase(newDsp);
            }
            else
            {
                _clock.Anchor(beat, newDsp, _bpm);
                double toBoundary = (NextPhraseBeat - beat) * 60.0 / _bpm * _clock.SampleRate;
                _nextBoundary = newDsp + (ulong)Math.Round(toBoundary, MidpointRounding.AwayFromZero);
            }
        }
        Resynced?.Invoke();
        if (startedPhrase >= 0 && run == _run)
            PhraseStarted?.Invoke(startedPhrase);
    }
}
