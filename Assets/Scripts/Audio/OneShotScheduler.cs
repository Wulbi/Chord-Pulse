using System;
using System.Collections.Generic;

/// <summary>Starts instrument one-shots at DSP clock times. The FMOD implementation is <see cref="FmodOneShotBackend"/>.</summary>
public interface IOneShotBackend
{
    /// <summary>Starts a note so its sample begins at <paramref name="dspStart"/>; returns a handle, or -1 on failure.</summary>
    int Start(Instrument inst, int midi, float gainDb, ulong dspStart);
    void Stop(int handle, ulong dspStop);
    void Release(int handle);
    /// <summary>Current DSP clock of the master channel group.</summary>
    ulong NowDsp { get; }
}

/// <summary>
/// Schedules instrument one-shots on beats of a <see cref="BeatClock"/>. Minimal version (Task 3 spike):
/// only <see cref="Schedule"/>; Task 5 adds Update / RescheduleFrom / StopAll.
/// </summary>
public sealed class OneShotScheduler
{
    readonly BeatClock _clock;
    readonly IOneShotBackend _backend;

    public OneShotScheduler(BeatClock clock, IOneShotBackend backend)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <summary>
    /// Measured with the Task 3 spike capture (Docs/Captures/README.md, take spike_seed33_B2): uncorrected one-shots
    /// landed 2.29 ms (110 samples at 48 kHz) after the timeline beat on average.
    /// </summary>
    public const long DefaultScheduleOffsetSamples = -110;

    /// <summary>
    /// Constant correction added to every start (samples): the mean lag between the beat anchor and the audible
    /// timeline beat. Negative = start earlier.
    /// </summary>
    public long ScheduleOffsetSamples { get; set; } = DefaultScheduleOffsetSamples;

    /// <summary>Notes that were not started because their start time had already passed.</summary>
    public int LateCount { get; private set; }

    /// <summary>
    /// Plays <paramref name="midi"/> on <paramref name="inst"/> so its attack lands on <paramref name="beat"/>
    /// (the sample starts <paramref name="onsetSeconds"/> earlier) and stops it <paramref name="lengthBeats"/> later.
    /// False if the clock has no anchor yet, the start is already in the past, or the backend failed.
    /// </summary>
    public bool Schedule(Instrument inst, int midi, double beat, double lengthBeats, float gainDb, double onsetSeconds)
    {
        if (!_clock.DspAt(beat, out ulong onBeat) || !_clock.DspAt(beat + lengthBeats, out ulong offBeat))
            return false;
        long start = (long)onBeat + ScheduleOffsetSamples - (long)Math.Round(onsetSeconds * _clock.SampleRate);
        long stop = (long)offBeat + ScheduleOffsetSamples;
        if (start <= (long)_backend.NowDsp)
        {
            LateCount++;
            return false;
        }
        int handle = _backend.Start(inst, midi, gainDb, (ulong)start);
        if (handle < 0)
            return false;
        _backend.Stop(handle, (ulong)stop);
        _backend.Release(handle);
        return true;
    }
}

/// <summary>
/// FMOD Studio backend: one event instance of event:/Inst/&lt;name&gt; per note, Note parameter set per instance.
/// start() is called at once; the start time is applied through the event's channel group (ChannelGroup.setDelay,
/// sample accurate) on the first <see cref="Update"/> after start(), once Studio has created the group.
/// Call Update every frame. (Task 3 spike: EVENT_PROPERTY.SCHEDULE_DELAY was as precise but about 24 ms late on
/// average, an offset that depends on the Studio update timing; see Docs/Captures/README.md.)
/// </summary>
public sealed class FmodOneShotBackend : IOneShotBackend
{
    struct Pending
    {
        public FMOD.Studio.EventInstance Instance;
        public ulong Start;
        public ulong Stop;
        public bool Released;
        public int Frames;
    }

    readonly Dictionary<int, Pending> _pending = new Dictionary<int, Pending>();
    readonly List<int> _keys = new List<int>();
    readonly Dictionary<Instrument, FMOD.Studio.EventDescription> _descriptions = new Dictionary<Instrument, FMOD.Studio.EventDescription>();
    int _next;

    /// <summary>
    /// Notes stopped without sounding: their channel group appeared only after the start time had passed, or never
    /// (30 frames). They are never played late.
    /// </summary>
    public int DroppedCount { get; private set; }
    public int StartedCount { get; private set; }

    public ulong NowDsp => MusicConductor.MasterDsp();

    public int Start(Instrument inst, int midi, float gainDb, ulong dspStart)
    {
        if (!_descriptions.TryGetValue(inst, out var description))
        {
            if (FMODUnity.RuntimeManager.StudioSystem.getEvent(InstrumentTable.EventPath(inst), out description) != FMOD.RESULT.OK)
                return -1;
            _descriptions[inst] = description;
        }
        if (description.createInstance(out var instance) != FMOD.RESULT.OK)
            return -1;
        instance.setParameterByName("Note", midi, true);
        instance.setVolume((float)Math.Pow(10.0, gainDb / 20.0));
        if (instance.start() != FMOD.RESULT.OK)
        {
            instance.release();
            return -1;
        }
        StartedCount++;
        int handle = _next++;
        _pending[handle] = new Pending { Instance = instance, Start = dspStart };
        return handle;
    }

    public void Stop(int handle, ulong dspStop)
    {
        if (_pending.TryGetValue(handle, out var p))
        {
            p.Stop = dspStop;
            _pending[handle] = p;
        }
    }

    public void Release(int handle)
    {
        if (_pending.TryGetValue(handle, out var p))
        {
            p.Released = true;
            _pending[handle] = p;
        }
    }

    /// <summary>
    /// Applies start / stop times once each instance's channel group exists; releases finished handles. A note whose
    /// start is already past (or whose group never appears) is stopped immediately and dropped, not played late.
    /// </summary>
    public void Update()
    {
        if (_pending.Count == 0)
            return;
        ulong now = NowDsp;
        _keys.Clear();
        _keys.AddRange(_pending.Keys);
        foreach (int key in _keys)
        {
            var p = _pending[key];
            bool hasGroup = p.Instance.getChannelGroup(out FMOD.ChannelGroup group) == FMOD.RESULT.OK && group.hasHandle();
            if (!hasGroup && ++p.Frames <= 30)
            {
                _pending[key] = p;
                continue;
            }
            if (hasGroup && p.Start > now)
            {
                group.setDelay(p.Start, p.Stop, true);
                if (p.Released)
                    p.Instance.release();
            }
            else
            {
                // Too late (start already past) or no channel group after 30 frames: drop it, never play it late.
                DroppedCount++;
                p.Instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                p.Instance.release();
            }
            _pending.Remove(key);
        }
    }
}
