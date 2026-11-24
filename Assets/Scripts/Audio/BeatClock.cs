using System;

/// <summary>
/// Maps beat positions to FMOD DSP clock samples (the software mixer clock of the master channel group).
/// Each anchor says "beat N was heard at DSP sample D, and the tempo leading out of it is B";
/// any beat is extrapolated from the latest anchor: dsp = anchorDsp + (beat - anchorBeat) * 60 / bpm * sampleRate,
/// rounded to the nearest sample.
/// </summary>
public sealed class BeatClock
{
    readonly int _sampleRate;
    bool _anchored;
    int _anchorBeat;
    ulong _anchorDsp;
    double _bpm;

    public BeatClock(int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        _sampleRate = sampleRate;
    }

    public int SampleRate => _sampleRate;
    public bool HasAnchor => _anchored;
    public int AnchorBeat => _anchorBeat;
    public ulong AnchorDsp => _anchorDsp;
    public double Bpm => _bpm;

    public void Anchor(int beat, ulong dspClock, double bpm)
    {
        if (bpm <= 0)
            return;
        _anchorBeat = beat;
        _anchorDsp = dspClock;
        _bpm = bpm;
        _anchored = true;
    }

    /// <summary>DSP clock of a beat (fractional beats allowed); false before the first anchor or before DSP 0.</summary>
    public bool DspAt(double beat, out ulong dspClock)
    {
        dspClock = 0;
        if (!_anchored)
            return false;
        double samples = _anchorDsp + (beat - _anchorBeat) * 60.0 / _bpm * _sampleRate;
        if (samples < 0)
            return false;
        dspClock = (ulong)Math.Round(samples, MidpointRounding.AwayFromZero);
        return true;
    }

    public void Reset()
    {
        _anchored = false;
        _anchorBeat = 0;
        _anchorDsp = 0;
        _bpm = 0;
    }
}
