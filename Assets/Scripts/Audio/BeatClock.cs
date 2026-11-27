using System;

/// <summary>
/// Maps beat positions to FMOD DSP clock samples (the software mixer clock of the master channel group).
/// Each anchor says "beat N was heard at DSP sample D, and the tempo leading out of it is B";
/// any beat is extrapolated from the latest anchor: dsp = anchorDsp + (beat - anchorBeat) * 60 / bpm * sampleRate,
/// rounded to the nearest sample.
/// A pending tempo change (<see cref="SetTempoFrom"/>) lets beats past an upcoming section boundary be computed with the
/// next section's BPM before that section's first anchor arrives.
/// </summary>
public sealed class BeatClock
{
    int _sampleRate;
    bool _anchored;
    int _anchorBeat;
    ulong _anchorDsp;
    double _bpm;
    bool _hasPending;
    int _pendingBeat;
    double _pendingBpm;

    public BeatClock(int sampleRate)
    {
        Reset(sampleRate);
    }

    public int SampleRate => _sampleRate;
    public bool HasAnchor => _anchored;
    public int AnchorBeat => _anchorBeat;
    public double Bpm => _bpm;

    public void Anchor(int beat, ulong dspClock, double bpm)
    {
        if (bpm <= 0)
            return;
        _anchorBeat = beat;
        _anchorDsp = dspClock;
        _bpm = bpm;
        _anchored = true;
        if (_hasPending && beat >= _pendingBeat)
            _hasPending = false; // the real anchor of the new section replaces the prediction
    }

    /// <summary>
    /// Announces that the tempo becomes <paramref name="bpm"/> from <paramref name="beat"/> on (e.g. the next section's
    /// BPM, known a phrase ahead). Until an anchor at or after that beat arrives, <see cref="DspAt"/> runs the anchor's
    /// tempo up to <paramref name="beat"/> and this tempo after it. A pending change at the same beat is replaced; one at
    /// an earlier beat that is still unanchored is first turned into a predicted anchor. Ignored if
    /// <paramref name="beat"/> is not after the current anchor. Cleared by the next anchor at/after it and by Reset.
    /// </summary>
    public void SetTempoFrom(int beat, double bpm)
    {
        if (bpm <= 0 || (_anchored && beat <= _anchorBeat))
            return;
        // An earlier change that never got a real anchor (a jump landing whose beat callback came too late) becomes a
        // predicted anchor, so the new change continues from it instead of from the old section's tempo.
        if (_anchored && _hasPending && _pendingBeat > _anchorBeat && beat > _pendingBeat)
        {
            double at = _anchorDsp + (_pendingBeat - _anchorBeat) * 60.0 / _bpm * _sampleRate;
            _anchorDsp = (ulong)Math.Round(at, MidpointRounding.AwayFromZero);
            _anchorBeat = _pendingBeat;
            _bpm = _pendingBpm;
        }
        _hasPending = true;
        _pendingBeat = beat;
        _pendingBpm = bpm;
    }

    /// <summary>DSP clock of a beat (fractional beats allowed); false before the first anchor or before DSP 0.</summary>
    public bool DspAt(double beat, out ulong dspClock)
    {
        dspClock = 0;
        if (!_anchored)
            return false;
        double samples;
        if (_hasPending && beat > _pendingBeat && _pendingBeat > _anchorBeat)
            samples = _anchorDsp + ((_pendingBeat - _anchorBeat) * 60.0 / _bpm + (beat - _pendingBeat) * 60.0 / _pendingBpm) * _sampleRate;
        else
            samples = _anchorDsp + (beat - _anchorBeat) * 60.0 / _bpm * _sampleRate;
        if (samples < 0)
            return false;
        dspClock = (ulong)Math.Round(samples, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>
    /// Beat position heard at a DSP clock (inverse of <see cref="DspAt"/>, same pending tempo change); false before the
    /// first anchor. Use it for "now in beats" from the mixer clock, which keeps running while beat callbacks are late.
    /// </summary>
    public bool BeatAt(ulong dspClock, out double beat) => BeatAt((double)dspClock, out beat);

    /// <summary>Same as the <c>ulong</c> overload for a fractional (e.g. frame-interpolated) DSP clock.</summary>
    public bool BeatAt(double dspClock, out double beat)
    {
        beat = 0;
        if (!_anchored)
            return false;
        double seconds = (dspClock - _anchorDsp) / _sampleRate;
        if (_hasPending && _pendingBeat > _anchorBeat)
        {
            double toPending = (_pendingBeat - _anchorBeat) * 60.0 / _bpm;
            if (seconds > toPending)
            {
                beat = _pendingBeat + (seconds - toPending) * _pendingBpm / 60.0;
                return true;
            }
        }
        beat = _anchorBeat + seconds * _bpm / 60.0;
        return true;
    }

    /// <summary>Drops the anchor and switches to another mixer rate; the same instance stays valid for its holders.</summary>
    public void Reset(int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        _sampleRate = sampleRate;
        Reset();
    }

    public void Reset()
    {
        _anchored = false;
        _anchorBeat = 0;
        _anchorDsp = 0;
        _bpm = 0;
        _hasPending = false;
    }
}
