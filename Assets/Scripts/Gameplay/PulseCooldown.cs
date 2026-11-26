/// <summary>
/// Pulse (Space) cooldown measured in beats. Pure logic, no engine dependency.
/// </summary>
public sealed class PulseCooldown
{
    double _lockedUntil = double.NegativeInfinity;
    bool _buffered;

    public void Reset()
    {
        _lockedUntil = double.NegativeInfinity;
        _buffered = false;
    }

    /// Space was pressed. True: fire now. False: ignored or buffered.
    public bool Press(double beat, bool fever)
    {
        if (beat < 0) return false;
        if (fever) return true;
        if (beat >= _lockedUntil)
        {
            Lock(beat);
            return true;
        }
        if (_lockedUntil - beat <= GameplayTuning.CooldownBufferBeats) _buffered = true;
        return false;
    }

    /// Call every frame. True: a buffered press fires now (once).
    public bool Tick(double beat, bool fever)
    {
        if (!_buffered) return false;
        if (fever)
        {
            _buffered = false;
            return true;
        }
        if (beat < _lockedUntil) return false;
        Lock(beat);
        return true;
    }

    /// 0..1, 1 means ready. Always 1 during fever or before the first shot.
    public float Progress(double beat, bool fever)
    {
        if (fever) return 1f;
        double remaining = _lockedUntil - beat;
        if (remaining <= 0) return 1f;
        double progress = 1.0 - remaining / GameplayTuning.CooldownBeats;
        return (float)(progress < 0.0 ? 0.0 : progress);
    }

    void Lock(double beat)
    {
        _lockedUntil = beat + GameplayTuning.CooldownBeats;
        _buffered = false;
    }
}
