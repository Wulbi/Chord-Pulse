using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// Single music clock driven by FMOD timeline callbacks.
/// FMOD reports every beat (and named marker) of the playing event; this class counts them into an
/// absolute beat index, so tempo changes, loops and section jumps authored in FMOD never desync gameplay.
/// Callbacks arrive on the FMOD thread and are queued; <see cref="Update"/> must run on the main thread.
/// Every counted beat also anchors <see cref="Clock"/> (beat -> FMOD DSP clock) so one-shots can be scheduled on it.
/// The anchor pairs getTimelinePosition with the master DSP clock on the main thread (Task 3 spike, Docs/Captures/README.md:
/// reading the DSP clock inside the beat callback is only mixer-block accurate, about 21 ms).
/// </summary>
public sealed class MusicConductor
{
    // Beat callbacks are stamped on the FMOD thread, so they jitter by roughly one Studio update.
    // Corrections smaller than this are blended in to keep the visual clock smooth.
    const double SmoothWindowSeconds = 0.06;
    const double SmoothFactor = 0.35;
    const double MaxExtrapolationBeats = 0.999;

    enum SignalType { Beat, Marker }

    struct Signal
    {
        public SignalType Type;
        public long Timestamp;
        public float Tempo;
        public int BeatsPerBar;
        public int BeatInBar;
        public string Name;
        public int PositionMs;
    }

    static readonly FMOD.Studio.EVENT_CALLBACK s_callback = OnEventCallback;
    static readonly double s_tickSeconds = 1.0 / Stopwatch.Frequency;
    static FMOD.ChannelGroup s_master;

    readonly ConcurrentQueue<Signal> _signals = new ConcurrentQueue<Signal>();
    GCHandle _handle;
    bool _attached;
    FMOD.Studio.EventInstance _instance;
    // Running mean of the per-frame estimates of the current beat's DSP clock.
    double _pollSum;
    int _pollCount;
    int _anchorPositionMs;

    // Anchor: the most recent beat, the time it was heard and the tempo leading out of it.
    int _anchorBeat = -1;
    double _anchorSeconds;
    double _tempo;
    double _lastReported = -1;

    public bool HasStarted => _anchorBeat >= 0;
    /// <summary>BPM reported by FMOD for the current beat.</summary>
    public double Tempo => _tempo;
    public int BeatsPerBar { get; private set; } = 4;
    /// <summary>Absolute index of the bar containing the current beat, counted from the first beat.</summary>
    public int Bar { get; private set; } = -1;
    /// <summary>Latest named marker passed in the FMOD timeline (section id).</summary>
    public string Section { get; private set; } = string.Empty;

    /// <summary>Beat -> DSP clock of the master channel group, anchored on every beat.</summary>
    public BeatClock Clock { get; private set; } = new BeatClock(48000);
    /// <summary>Timeline position (ms) FMOD reported for the current beat; a jump shows as a discontinuity.</summary>
    public int BeatTimelineMs => _anchorPositionMs;

    /// <summary>Current DSP clock of the master channel group (the clock <see cref="Clock"/> and ChannelGroup.setDelay use).</summary>
    public static ulong MasterDsp()
    {
        if (!s_master.hasHandle())
            return 0;
        s_master.getDSPClock(out ulong dsp, out _);
        return dsp;
    }

    public event Action<int> BeatChanged;
    public event Action<string> SectionChanged;

    public void Attach(FMOD.Studio.EventInstance instance)
    {
        Detach();
        while (_signals.TryDequeue(out _)) { }
        _anchorBeat = -1;
        _lastReported = -1;
        _tempo = 0;
        Bar = -1;
        Section = string.Empty;
        BeatsPerBar = 4;
        _instance = instance;
        FMODUnity.RuntimeManager.CoreSystem.getMasterChannelGroup(out s_master);
        FMODUnity.RuntimeManager.CoreSystem.getSoftwareFormat(out int sampleRate, out _, out _);
        if (Clock.SampleRate != sampleRate)
            Clock = new BeatClock(sampleRate);
        Clock.Reset();
        _pollSum = 0;
        _pollCount = 0;

        // Freed by the DESTROYED callback so a late FMOD callback never touches a dead handle.
        _handle = GCHandle.Alloc(this);
        instance.setUserData(GCHandle.ToIntPtr(_handle));
        instance.setCallback(s_callback,
            FMOD.Studio.EVENT_CALLBACK_TYPE.TIMELINE_BEAT |
            FMOD.Studio.EVENT_CALLBACK_TYPE.TIMELINE_MARKER |
            FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROYED);
        _attached = true;
    }

    public void Detach()
    {
        _attached = false;
        _anchorBeat = -1;
        Clock.Reset();
    }

    /// <summary>Continuous beat position as heard (0 = first beat). -1 before the first beat.</summary>
    public double BeatPosition()
    {
        if (!HasStarted)
            return -1;
        double elapsed = Math.Max(0, Now() - _anchorSeconds);
        double position = _anchorBeat + Math.Min(elapsed * _tempo / 60.0, MaxExtrapolationBeats);
        // Never run backwards: a beat that arrives early would otherwise re-trigger beat-driven logic.
        if (position < _lastReported)
            position = _lastReported;
        _lastReported = position;
        return position;
    }

    public double SecondsToBeats(double seconds) => seconds * _tempo / 60.0;

    public void Update()
    {
        if (!_attached)
            return;
        bool polled = false;
        while (_signals.TryDequeue(out var signal))
        {
            if (signal.Type == SignalType.Marker)
            {
                Section = signal.Name ?? string.Empty;
                SectionChanged?.Invoke(Section);
                continue;
            }

            double stamp = signal.Timestamp * s_tickSeconds;
            int beatsPerBar = Math.Max(1, signal.BeatsPerBar);
            int beatInBar = Math.Max(0, signal.BeatInBar - 1);
            if (_anchorBeat < 0)
            {
                _anchorBeat = beatInBar;
                _anchorSeconds = stamp;
            }
            else
            {
                double beatSeconds = 60.0 / _tempo;
                // A timeline jump can report the same beat twice; ignore the echo.
                if (stamp - _anchorSeconds < beatSeconds * 0.25 && beatInBar == _anchorBeat % beatsPerBar)
                    continue;
                // ...or skip one; FMOD's beat-in-bar is authoritative, so step forward to match it.
                int next = _anchorBeat + 1;
                int skipped = ((beatInBar - next % beatsPerBar) % beatsPerBar + beatsPerBar) % beatsPerBar;
                double predicted = _anchorSeconds + (1 + skipped) * beatSeconds;
                double error = stamp - predicted;
                _anchorBeat = next + skipped;
                _anchorSeconds = Math.Abs(error) < SmoothWindowSeconds ? predicted + error * SmoothFactor : stamp;
            }

            _tempo = signal.Tempo;
            BeatsPerBar = beatsPerBar;
            Bar = _anchorBeat / beatsPerBar;
            _anchorPositionMs = signal.PositionMs;
            _pollSum = 0;
            _pollCount = 0;
            // Anchor before BeatChanged, so handlers can schedule on this beat right away.
            PollAnchor();
            polled = true;
            BeatChanged?.Invoke(_anchorBeat);
        }
        if (HasStarted && !polled)
            PollAnchor();
    }

    // dsp(beat) = dspNow - (timelineNow - beatPosition). Both values are read back to back on the main thread; each
    // frame adds one estimate and the anchor is their running mean for the current beat. Measured: about 1 ms jitter.
    void PollAnchor()
    {
        if (!_instance.isValid() || _instance.getTimelinePosition(out int timelineMs) != FMOD.RESULT.OK)
            return;
        ulong now = MasterDsp();
        double sinceBeat = (timelineMs - _anchorPositionMs) / 1000.0;
        double beatSeconds = 60.0 / _tempo;
        // Before the timeline reached this beat, or after a jump that has not been reported yet: no estimate.
        if (sinceBeat < -beatSeconds || sinceBeat > 2 * beatSeconds)
            return;
        _pollSum += now - sinceBeat * Clock.SampleRate;
        _pollCount++;
        Clock.Anchor(_anchorBeat, (ulong)Math.Round(_pollSum / _pollCount), _tempo);
    }

    static double Now() => Stopwatch.GetTimestamp() * s_tickSeconds;

    [AOT.MonoPInvokeCallback(typeof(FMOD.Studio.EVENT_CALLBACK))]
    static FMOD.RESULT OnEventCallback(FMOD.Studio.EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
    {
        var instance = new FMOD.Studio.EventInstance(instancePtr);
        if (instance.getUserData(out IntPtr data) != FMOD.RESULT.OK || data == IntPtr.Zero)
            return FMOD.RESULT.OK;
        var handle = GCHandle.FromIntPtr(data);

        if (type == FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROYED)
        {
            handle.Free();
            return FMOD.RESULT.OK;
        }

        var conductor = handle.Target as MusicConductor;
        if (conductor == null)
            return FMOD.RESULT.OK;

        long now = Stopwatch.GetTimestamp();
        if (type == FMOD.Studio.EVENT_CALLBACK_TYPE.TIMELINE_BEAT)
        {
            var beat = (FMOD.Studio.TIMELINE_BEAT_PROPERTIES)Marshal.PtrToStructure(
                parameterPtr, typeof(FMOD.Studio.TIMELINE_BEAT_PROPERTIES));
            conductor._signals.Enqueue(new Signal
            {
                Type = SignalType.Beat,
                Timestamp = now,
                Tempo = beat.tempo,
                BeatsPerBar = beat.timesignatureupper,
                BeatInBar = beat.beat,
                PositionMs = beat.position
            });
        }
        else if (type == FMOD.Studio.EVENT_CALLBACK_TYPE.TIMELINE_MARKER)
        {
            var marker = (FMOD.Studio.TIMELINE_MARKER_PROPERTIES)Marshal.PtrToStructure(
                parameterPtr, typeof(FMOD.Studio.TIMELINE_MARKER_PROPERTIES));
            conductor._signals.Enqueue(new Signal
            {
                Type = SignalType.Marker,
                Timestamp = now,
                Name = (string)marker.name
            });
        }
        return FMOD.RESULT.OK;
    }
}
