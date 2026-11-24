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
/// Schedules instrument one-shots on beats of a <see cref="BeatClock"/>. A note is kept here until its start is at most
/// half a beat away, then handed to the backend with its start and stop DSP times (computed from the clock at that
/// moment, so anchors and pending tempo changes that arrive meanwhile are used). Call <see cref="Update"/> every frame.
/// </summary>
public sealed class OneShotScheduler
{
    /// <summary>Notes are handed to the backend once their start is at most this many beats ahead (default per note).</summary>
    public const double DispatchWindowBeats = 0.5;

    sealed class Note
    {
        public Instrument Inst;
        public int Midi;
        public double Beat;
        public double LengthBeats;
        public float GainDb;
        public double OnsetSeconds;
        public double WindowBeats;
        public int Handle = -1; // >= 0 once handed to the backend
        public ulong Start;
        public ulong Stop;
    }

    readonly BeatClock _clock;
    readonly IOneShotBackend _backend;
    readonly List<Note> _notes = new List<Note>();

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

    /// <summary>Notes that were never started because their start time had already passed (never played late).</summary>
    public int LateCount { get; private set; }

    /// <summary>Notes waiting for the dispatch window plus notes handed to the backend whose stop time has not passed.</summary>
    public int ActiveCount => _notes.Count;

    /// <summary>
    /// Plays <paramref name="midi"/> on <paramref name="inst"/> so its attack lands on <paramref name="beat"/>
    /// (the sample starts <paramref name="onsetSeconds"/> earlier) and stops it <paramref name="lengthBeats"/> later.
    /// A note already inside the dispatch window goes to the backend at once.
    /// False if the clock has no anchor yet, the start is already in the past (counted in <see cref="LateCount"/>),
    /// or the backend failed on an immediate start.
    /// <paramref name="dispatchWindowBeats"/> widens (or narrows) the dispatch window for this note, e.g. to create the
    /// instance before FMOD Studio's stall at a section-jump landing.
    /// </summary>
    public bool Schedule(Instrument inst, int midi, double beat, double lengthBeats, float gainDb, double onsetSeconds,
        double dispatchWindowBeats = DispatchWindowBeats)
    {
        var note = new Note
        {
            Inst = inst, Midi = midi, Beat = beat, LengthBeats = Math.Max(0, lengthBeats), GainDb = gainDb,
            OnsetSeconds = onsetSeconds, WindowBeats = Math.Max(0, dispatchWindowBeats),
        };
        if (!Times(note, out long start, out _, out _))
            return false;
        if (start <= (long)_backend.NowDsp)
        {
            LateCount++;
            return false;
        }
        _notes.Add(note);
        if (Dispatch(note, _backend.NowDsp) == DispatchResult.Failed)
        {
            _notes.Remove(note);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Hands waiting notes whose start is at most <see cref="DispatchWindowBeats"/> away to the backend, drops waiting
    /// notes whose start has already passed (<see cref="LateCount"/>), and releases handles whose stop time has passed.
    /// </summary>
    public void Update()
    {
        ulong now = _backend.NowDsp;
        for (int i = _notes.Count - 1; i >= 0; i--)
        {
            var note = _notes[i];
            if (note.Handle >= 0)
            {
                if (now >= note.Stop)
                {
                    _backend.Release(note.Handle);
                    _notes.RemoveAt(i);
                }
                continue;
            }
            var result = Dispatch(note, now);
            if (result == DispatchResult.Late || result == DispatchResult.Failed)
                _notes.RemoveAt(i);
        }
    }

    /// <summary>
    /// Recomputes every note on or after <paramref name="beat"/> with the current clock (call after the clock learned
    /// a tempo that differs from what was assumed, e.g. a section that did not change). Notes already handed to the
    /// backend but not yet sounding are cancelled and handed over again with the new times; notes already sounding
    /// are left alone.
    /// </summary>
    public void RescheduleFrom(double beat)
    {
        ulong now = _backend.NowDsp;
        for (int i = _notes.Count - 1; i >= 0; i--)
        {
            var note = _notes[i];
            if (note.Beat < beat || note.Handle < 0 || note.Start <= now)
                continue;
            Cancel(note, now);
        }
        Update();
    }

    /// <summary>Drops every waiting note and stops every note handed to the backend now.</summary>
    public void StopAll()
    {
        ulong now = _backend.NowDsp;
        foreach (var note in _notes)
            if (note.Handle >= 0)
                Cancel(note, now);
        _notes.Clear();
    }

    enum DispatchResult { Waiting, Sent, Late, Failed }

    DispatchResult Dispatch(Note note, ulong now)
    {
        if (!Times(note, out long start, out long stop, out long window))
            return DispatchResult.Failed;
        if (start <= (long)now)
        {
            LateCount++; // never hand the backend a start that is already past
            return DispatchResult.Late;
        }
        if (start - (long)now > window)
            return DispatchResult.Waiting;
        int handle = _backend.Start(note.Inst, note.Midi, note.GainDb, (ulong)start);
        if (handle < 0)
            return DispatchResult.Failed;
        note.Handle = handle;
        note.Start = (ulong)start;
        note.Stop = (ulong)Math.Max(stop, start);
        _backend.Stop(handle, note.Stop);
        return DispatchResult.Sent;
    }

    void Cancel(Note note, ulong now)
    {
        _backend.Stop(note.Handle, now);
        _backend.Release(note.Handle);
        note.Handle = -1;
    }

    /// <summary>Start / stop DSP times of a note and its dispatch window in samples, from the clock as it is now.</summary>
    bool Times(Note note, out long start, out long stop, out long window)
    {
        start = stop = window = 0;
        if (!_clock.DspAt(note.Beat, out ulong onBeat) || !_clock.DspAt(note.Beat + note.LengthBeats, out ulong offBeat))
            return false;
        start = (long)onBeat + ScheduleOffsetSamples - (long)Math.Round(note.OnsetSeconds * _clock.SampleRate);
        stop = (long)offBeat + ScheduleOffsetSamples;
        // Window measured backwards from the beat at the tempo that leads into it; near DSP 0 just send.
        window = _clock.DspAt(note.Beat - note.WindowBeats, out ulong before) ? (long)onBeat - (long)before : long.MaxValue;
        return true;
    }
}

/// <summary>
/// FMOD Studio backend: one event instance of event:/Inst/&lt;name&gt; per note, Note parameter set per instance.
/// start() is called at once; the start time is applied through the event's channel group (ChannelGroup.setDelay,
/// sample accurate) on the first <see cref="Update"/> after start(), once Studio has created the group.
/// Call Update every frame. (Task 3 spike: EVENT_PROPERTY.SCHEDULE_DELAY was as precise but about 24 ms late on
/// average, an offset that depends on the Studio update timing; see Docs/Captures/README.md.)
/// <see cref="Stop"/> with a time at or before now, or at or before the note's start, cancels the note at once.
/// A note stopped at its end time fades out over <see cref="ReleaseSeconds"/> from that time (like the 120 ms ADSR
/// release of the timeline notes it replaces) instead of being cut.
/// </summary>
public sealed class FmodOneShotBackend : IOneShotBackend
{
    struct Note
    {
        public FMOD.Studio.EventInstance Instance;
        public FMOD.ChannelGroup Group;
        public bool Applied; // setDelay done
        public ulong Start;
        public ulong Stop;
        public bool Released;
        public int Frames;
    }

    readonly Dictionary<int, Note> _notes = new Dictionary<int, Note>();
    readonly List<int> _keys = new List<int>();
    readonly Dictionary<Instrument, FMOD.Studio.EventDescription> _descriptions = new Dictionary<Instrument, FMOD.Studio.EventDescription>();
    int _next;
    ulong _releaseSamples;

    /// <summary>Fade-out after a note's stop time (the instrument events' ADSR release).</summary>
    public const double ReleaseSeconds = 0.12;

    /// <summary>
    /// Notes stopped without sounding: their channel group appeared only after the start time had passed, or never
    /// (30 frames). They are never played late.
    /// </summary>
    public int DroppedCount { get; private set; }
    public int StartedCount { get; private set; }
    /// <summary>Notes cancelled (Stop at or before their start) before they sounded.</summary>
    public int CancelledCount { get; private set; }

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
        if (_releaseSamples == 0)
        {
            FMODUnity.RuntimeManager.CoreSystem.getSoftwareFormat(out int rate, out _, out _);
            _releaseSamples = (ulong)Math.Round(ReleaseSeconds * (rate > 0 ? rate : 48000));
        }
        if (instance.start() != FMOD.RESULT.OK)
        {
            instance.release();
            return -1;
        }
        StartedCount++;
        int handle = _next++;
        _notes[handle] = new Note { Instance = instance, Start = dspStart };
        return handle;
    }

    public void Stop(int handle, ulong dspStop)
    {
        if (!_notes.TryGetValue(handle, out var n))
            return;
        ulong now = NowDsp;
        if (dspStop <= now || dspStop <= n.Start)
        {
            if (n.Start > now)
                CancelledCount++;
            n.Instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            n.Instance.release();
            _notes.Remove(handle);
            return;
        }
        n.Stop = dspStop;
        if (n.Applied)
            ApplyTimes(n.Group, n);
        _notes[handle] = n;
    }

    /// <summary>
    /// The caller is done with the handle. The instance is kept until its stop time plus the release has passed, then
    /// stopped and released by <see cref="Update"/>: its channels end with setDelay, but the Studio event itself would
    /// otherwise keep running (and keep its channel group and DSP chain alive) until its timeline reaches the end of
    /// the sample.
    /// </summary>
    public void Release(int handle)
    {
        if (!_notes.TryGetValue(handle, out var n))
            return;
        n.Released = true;
        _notes[handle] = n;
    }

    /// <summary>Stops and releases every instance at once (e.g. when the music stops).</summary>
    public void StopAll()
    {
        foreach (var n in _notes.Values)
        {
            n.Instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            n.Instance.release();
        }
        _notes.Clear();
    }

    // Start, then full level until the stop time, then a linear fade to silence over the release (Stop 0 = no stop yet).
    void ApplyTimes(FMOD.ChannelGroup group, Note n)
    {
        ulong end = n.Stop > 0 ? n.Stop + _releaseSamples : 0;
        group.setDelay(n.Start, end, true);
        group.removeFadePoints(0, ulong.MaxValue);
        if (n.Stop > 0)
        {
            group.addFadePoint(n.Stop, 1f);
            group.addFadePoint(end, 0f);
        }
    }

    /// <summary>
    /// Applies start / stop times once each instance's channel group exists. A note whose start is already past
    /// (or whose group never appears) is stopped immediately and dropped, not played late.
    /// </summary>
    public void Update()
    {
        if (_notes.Count == 0)
            return;
        ulong now = NowDsp;
        _keys.Clear();
        _keys.AddRange(_notes.Keys);
        foreach (int key in _keys)
        {
            var n = _notes[key];
            if (n.Applied)
            {
                if (n.Released && n.Stop > 0 && now >= n.Stop + _releaseSamples)
                {
                    n.Instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                    n.Instance.release();
                    _notes.Remove(key);
                }
                continue;
            }
            bool hasGroup = n.Instance.getChannelGroup(out FMOD.ChannelGroup group) == FMOD.RESULT.OK && group.hasHandle();
            if (!hasGroup && ++n.Frames <= 30)
            {
                _notes[key] = n;
                continue;
            }
            if (hasGroup && n.Start > now)
            {
                ApplyTimes(group, n);
                n.Group = group;
                n.Applied = true;
                _notes[key] = n;
                continue;
            }
            // Too late (start already past) or no channel group after 30 frames: drop it, never play it late.
            DroppedCount++;
            n.Instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            n.Instance.release();
            _notes.Remove(key);
        }
    }
}
