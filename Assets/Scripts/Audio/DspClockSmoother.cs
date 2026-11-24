using System;

/// <summary>
/// Interpolates the FMOD master DSP clock between mixer blocks. The raw clock only changes once per mixer block
/// (about 21 ms at 1024 samples), so a per-frame read is a staircase; this turns it into a smooth, monotonic value.
/// The raw clock never runs ahead of real time, so (raw - seconds * sampleRate) is at most the true offset between the
/// monotonic clock and the DSP clock; its running maximum converges to it within one frame of a block change. The result
/// is seconds * sampleRate + that offset (the offset slowly decays so clock drift is followed, and is re-based when a
/// block change shows it is stale after a stall), never below the raw clock and never more than maxAheadSamples above it
/// (so a stalled mixer cannot be extrapolated far), and never backwards. A raw clock that goes backwards means the
/// clock was reset: the state is dropped and restarts from the new value.
/// </summary>
public sealed class DspClockSmoother
{
    readonly int _maxAheadSamples;
    int _sampleRate;
    bool _started;
    ulong _lastRaw;
    double _offset;
    double _last;
    double _lastSeconds;

    // The stored offset sinks by this many samples per second (tracks an audio clock running slow against the
    // monotonic one, up to ~600 ppm); every raw block change lifts it back up to the fresh observation.
    const double OffsetDecayPerSecond = 30;

    public DspClockSmoother(int sampleRate, int maxAheadSamples = 2048)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        _maxAheadSamples = maxAheadSamples;
        _sampleRate = sampleRate;
    }

    /// <param name="rawDsp">Master DSP clock read on the main thread.</param>
    /// <param name="seconds">Monotonic time in seconds (e.g. Stopwatch).</param>
    /// <returns>Interpolated DSP sample position (fractional).</returns>
    public double Sample(ulong rawDsp, double seconds)
    {
        double raw = rawDsp;
        double sampleOffset = raw - seconds * _sampleRate;
        if (!_started || rawDsp < _lastRaw)
        {
            _started = true;
            _offset = sampleOffset;
            _last = raw;
        }
        else
        {
            double dt = Math.Min(Math.Max(seconds - _lastSeconds, 0), 0.25);
            _offset -= OffsetDecayPerSecond * dt;
            if (sampleOffset > _offset)
            {
                _offset = sampleOffset;
            }
            else if (rawDsp != _lastRaw && seconds * _sampleRate + _offset > raw + _maxAheadSamples)
            {
                // A fresh block arrived but the estimate is further ahead than a block can explain: the offset is
                // stale (mixer stall, device change). Re-base it on this observation instead of staying pinned at the cap.
                _offset = sampleOffset;
            }
        }
        _lastRaw = rawDsp;
        _lastSeconds = seconds;

        double value = seconds * _sampleRate + _offset;
        if (value > raw + _maxAheadSamples)
            value = raw + _maxAheadSamples;
        if (value < raw)
            value = raw;
        if (value < _last)
            value = _last;
        _last = value;
        return value;
    }

    public void Reset()
    {
        _started = false;
        _lastRaw = 0;
        _offset = 0;
        _last = 0;
        _lastSeconds = 0;
    }

    public void Reset(int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        _sampleRate = sampleRate;
        Reset();
    }
}
