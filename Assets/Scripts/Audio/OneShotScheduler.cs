using System;
using System.Collections.Generic;

/// <summary>Starts instrument one-shots at DSP clock times. The FMOD implementation is <see cref="FmodOneShotBackend"/>.</summary>
public interface IOneShotBackend
{
    /// <summary>Starts a note so its sample begins at <paramref name="dspStart"/>; returns a handle, or -1 on failure.</summary>
    int Start(Instrument inst, OutputBus bus, int midi, float gainDb, ulong dspStart);
    void Stop(int handle, ulong dspStop);
    void Release(int handle);
    /// <summary>True once the note has ended on its own (or is unknown): its sample played to the end.</summary>
    bool IsFinished(int handle);
    /// <summary>Current DSP clock of the master channel group.</summary>
    ulong NowDsp { get; }
}

/// <summary>One note for <see cref="OneShotScheduler.Schedule"/>. <c>LengthBeats &lt;= 0</c> plays the sample to its end.</summary>
public readonly struct OneShotNote
{
    public readonly Instrument Instrument;
    public readonly OutputBus Bus;
    public readonly int Midi;
    public readonly double Beat;
    public readonly double LengthBeats;
    /// <summary>
    /// Minimum sounding time in seconds from the start (0 = none): the stop is the later of the beat length and this.
    /// Ignored for zero-length notes (they play to the end of the sample).
    /// </summary>
    public readonly double HoldSeconds;
    public readonly float GainDb;
    public readonly double OnsetSeconds;

    public OneShotNote(Instrument instrument, OutputBus bus, int midi, double beat, double lengthBeats, double holdSeconds,
        float gainDb, double onsetSeconds)
    {
        Instrument = instrument; Bus = bus; Midi = midi; Beat = beat; LengthBeats = lengthBeats;
        HoldSeconds = holdSeconds; GainDb = gainDb; OnsetSeconds = onsetSeconds;
    }
}

/// <summary>
/// Release fade (samples) of a note, looked up from its own (instrument, bus) every time: different combinations can
/// have different releases, so nothing is cached from the first note. Pure, so it is testable without FMOD.
/// </summary>
public sealed class OneShotRelease
{
    const int FallbackSampleRate = 48000;
    readonly Func<Instrument, OutputBus, double> _releaseSeconds;

    public OneShotRelease(Func<Instrument, OutputBus, double> releaseSeconds)
    {
        _releaseSeconds = releaseSeconds ?? throw new ArgumentNullException(nameof(releaseSeconds));
    }

    public ulong SamplesFor(Instrument inst, OutputBus bus, int sampleRate)
    {
        double seconds = _releaseSeconds(inst, bus);
        return seconds <= 0 ? 0UL : (ulong)Math.Round(seconds * (sampleRate > 0 ? sampleRate : FallbackSampleRate));
    }
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
        public OutputBus Bus;
        public object Owner;
        public double HoldSeconds;
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

    /// <summary>Diagnostics: a note was counted in <see cref="LateCount"/> (its owner and beat).</summary>
    public event Action<object, double> NoteLate;

    /// <summary>
    /// Notes waiting for the dispatch window plus notes handed to the backend that are still sounding or pending
    /// (stop time not passed; for zero-length notes, not yet reported finished by the backend).
    /// </summary>
    public int ActiveCount => _notes.Count;

    /// <summary>
    /// Plays <paramref name="note"/> so its attack lands on its beat (the sample starts OnsetSeconds earlier) and stops it
    /// LengthBeats later (at least HoldSeconds after the start); LengthBeats &lt;= 0 plays the sample to its end.
    /// <paramref name="owner"/> scopes <see cref="CancelFrom"/>. A note already inside the dispatch window goes to the
    /// backend at once.
    /// False if the clock has no anchor yet, the start is already in the past (counted in <see cref="LateCount"/>),
    /// or the backend failed on an immediate start.
    /// <paramref name="dispatchWindowBeats"/> widens (or narrows) the dispatch window for this note, e.g. to create the
    /// instance before FMOD Studio's stall at a section-jump landing.
    /// </summary>
    public bool Schedule(in OneShotNote note, object owner, double dispatchWindowBeats = DispatchWindowBeats)
    {
        var n = new Note
        {
            Inst = note.Instrument, Bus = note.Bus, Owner = owner, HoldSeconds = Math.Max(0, note.HoldSeconds),
            Midi = note.Midi, Beat = note.Beat, LengthBeats = Math.Max(0, note.LengthBeats), GainDb = note.GainDb,
            OnsetSeconds = note.OnsetSeconds, WindowBeats = Math.Max(0, dispatchWindowBeats),
        };
        return ScheduleNote(n);
    }

    bool ScheduleNote(Note note)
    {
        if (!Times(note, out long start, out _, out _))
            return false;
        if (start <= (long)_backend.NowDsp)
        {
            LateCount++;
            NoteLate?.Invoke(note.Owner, note.Beat);
            return false;
        }
        _notes.Add(note);
        // The clock may pass the start between the two NowDsp reads: Late (already counted by Dispatch) is dropped
        // here like Failed, so Update does not count it again.
        var result = Dispatch(note, _backend.NowDsp);
        if (result == DispatchResult.Late || result == DispatchResult.Failed)
        {
            _notes.Remove(note);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Hands waiting notes whose start is at most <see cref="DispatchWindowBeats"/> away to the backend, drops waiting
    /// notes whose start has already passed (<see cref="LateCount"/>), and releases handles whose stop time has passed
    /// (zero-length notes: whose backend reports them finished). A handed-over note keeps the start it was handed over with
    /// when later anchors move the clock (Task 12 fix round 2: re-timing armed notes lost notes); tempo changes go through
    /// <see cref="RescheduleFrom"/>.
    /// </summary>
    public void Update()
    {
        ulong now = _backend.NowDsp;
        for (int i = _notes.Count - 1; i >= 0; i--)
        {
            var note = _notes[i];
            if (note.Handle >= 0)
            {
                bool done = note.LengthBeats > 0 ? now >= note.Stop : _backend.IsFinished(note.Handle);
                if (done)
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

    /// <summary>
    /// Cancels <paramref name="owner"/>'s notes on or after <paramref name="beat"/> that have not started sounding:
    /// waiting notes are dropped, notes handed to the backend but not yet started are stopped and released.
    /// Notes already sounding are left alone. Returns the number cancelled.
    /// </summary>
    public int CancelFrom(double beat, object owner)
    {
        ulong now = _backend.NowDsp;
        int cancelled = 0;
        for (int i = _notes.Count - 1; i >= 0; i--)
        {
            var note = _notes[i];
            if (!ReferenceEquals(note.Owner, owner) || note.Beat < beat)
                continue;
            if (note.Handle >= 0)
            {
                if (note.Start <= now)
                    continue;
                Cancel(note, now);
            }
            _notes.RemoveAt(i);
            cancelled++;
        }
        return cancelled;
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
            NoteLate?.Invoke(note.Owner, note.Beat);
            return DispatchResult.Late;
        }
        if (start - (long)now > window)
            return DispatchResult.Waiting;
        int handle = _backend.Start(note.Inst, note.Bus, note.Midi, note.GainDb, (ulong)start);
        if (handle < 0)
            return DispatchResult.Failed;
        note.Handle = handle;
        note.Start = (ulong)start;
        note.Stop = (ulong)Math.Max(stop, start);
        if (note.LengthBeats > 0)
            _backend.Stop(handle, note.Stop); // zero length: no stop time, the sample plays to its end
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
        if (note.HoldSeconds > 0)
            stop = Math.Max(stop, start + (long)Math.Round(note.HoldSeconds * _clock.SampleRate));
        // Window measured backwards from the beat at the tempo that leads into it; near DSP 0 just send.
        window = _clock.DspAt(note.Beat - note.WindowBeats, out ulong before) ? (long)onBeat - (long)before : long.MaxValue;
        return true;
    }
}

/// <summary>What <see cref="FmodOneShotBackend"/> does with a started note whose start time is not applied yet.</summary>
public enum OneShotArm { Wait, Apply, Drop }

/// <summary>
/// The backend's wait for an instance's channel group, in DSP time (not frames: at an uncapped frame rate 30 frames
/// were shorter than one Studio update and dropped almost every note). Pure, so it is testable without FMOD.
/// </summary>
public static class OneShotArming
{
    /// <summary>
    /// Drop: the instance is invalid, or the start is less than <paramref name="minLeadSamples"/> (at least 1) ahead
    /// without the start time applied (applying it now could come after the mixer passed the start: late or lost).
    /// Apply: the channel group exists and the start is far enough ahead. Otherwise Wait.
    /// </summary>
    public static OneShotArm Decide(bool instanceValid, bool hasGroup, ulong start, ulong now, ulong minLeadSamples = 0)
    {
        if (!instanceValid || start < now + Math.Max(1UL, minLeadSamples))
            return OneShotArm.Drop;
        return hasGroup ? OneShotArm.Apply : OneShotArm.Wait;
    }

    /// <summary>
    /// True if an applied note's stop is missing from its channel group (delay end is not stop + release) and the stop is
    /// still ahead, so it can be put back in time. A note without a stop (0) plays to its end and is never re-applied.
    /// </summary>
    public static bool StopLost(ulong delayEnd, ulong stop, ulong releaseSamples, ulong now) =>
        stop > 0 && now < stop && delayEnd != stop + releaseSamples;

    /// <summary>
    /// Delay start of a note's channel group for an audible start at <paramref name="start"/>: a multi-sound instrument
    /// event starts its voice <paramref name="voiceBlocks"/> mixer blocks after the group's delay start, so the group starts
    /// that much earlier; <paramref name="pathBlocks"/> keeps the whole note as late as the legacy layers of its bus family
    /// (InstrumentTable.VoiceDelayBlocks / PathDelayBlocks; Task 12 follow-up).
    /// </summary>
    public static ulong DelayStart(ulong start, int voiceBlocks, int pathBlocks, ulong blockSamples) =>
        (ulong)Math.Max(0L, (long)start + ((long)pathBlocks - voiceBlocks) * (long)blockSamples);

    /// <summary>
    /// Stop point in the group's delay clock (the delay end is exact, it does not wait for the voice): the stop plus the
    /// bus family's path delay, so the note keeps its length. 0 = no stop. (Task 11 added one block to every stop because
    /// the voice started a block late; that block now comes off the start instead.)
    /// </summary>
    public static ulong StopAt(ulong stop, int pathBlocks, ulong blockSamples) =>
        stop == 0 ? 0 : stop + (ulong)Math.Max(0, pathBlocks) * blockSamples;
}

/// <summary>
/// FMOD Studio backend: one event instance of event:/Inst/&lt;name&gt; per note, Note parameter set per instance.
/// The instance is paused, then start()ed at once: Studio creates its channel group (and the voice) on its own
/// update thread, paused. The start time is applied through that group (ChannelGroup.setDelay, sample accurate) on
/// the first <see cref="Update"/> that finds the group, and only then are the instance and then
/// the group unpaused (the group directly, so the start does not wait for a Studio update; the instance first, so a Studio
/// update between the two calls cannot re-assert its pause on the group). A note can therefore
/// never sound before its delay is in effect (Task 5b: without the pause, a main-thread frame longer than one Studio
/// update let the voice sound at dispatch, up to the lead early). Call Update every frame. (Task 3 spike:
/// EVENT_PROPERTY.SCHEDULE_DELAY was as precise but about 24 ms late on average, an offset that depends on the Studio
/// update timing; see Docs/Captures/README.md.)
/// <see cref="Stop"/> with a time at or before now, or at or before the note's start, cancels the note at once.
/// A note stopped at its end time fades out over <see cref="InstrumentTable.ReleaseSeconds"/> from that time (like the 120 ms ADSR
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
        public int Frames; // DiagnosticLog only: Update calls before the start was applied
        public int Checks; // DiagnosticLog only
        public ulong ReleaseSamples; // fade after Stop, from this note's own (instrument, bus)
        public int VoiceBlocks, PathBlocks; // InstrumentTable.VoiceDelayBlocks / PathDelayBlocks of its (instrument, bus)
        public OutputBus Bus; // diagnostics: StartedOn / DroppedOn / NoteDropped
    }

    readonly int[] _startedByBus = new int[64];
    readonly int[] _droppedByBus = new int[64];

    /// <summary>Notes started on one output bus (diagnostics; e.g. kill chords vs the bed's pattern layers).</summary>
    public int StartedOn(OutputBus bus) => (uint)bus < 64 ? _startedByBus[(int)bus] : 0;
    /// <summary>Notes counted in <see cref="DroppedCount"/> on one output bus.</summary>
    public int DroppedOn(OutputBus bus) => (uint)bus < 64 ? _droppedByBus[(int)bus] : 0;
    /// <summary>Diagnostics: a note was dropped (its bus and scheduled DSP start).</summary>
    public event Action<OutputBus, ulong> NoteDropped;

    void CountDrop(in Note n)
    {
        DroppedCount++;
        if ((uint)n.Bus < 64)
            _droppedByBus[(int)n.Bus]++;
        NoteDropped?.Invoke(n.Bus, n.Start);
    }

    readonly Dictionary<int, Note> _notes = new Dictionary<int, Note>();
    readonly List<int> _keys = new List<int>();
    readonly OneShotRelease _release = new OneShotRelease(InstrumentTable.ReleaseSeconds);
    readonly Dictionary<(Instrument, OutputBus), FMOD.Studio.EventDescription> _descriptions = new Dictionary<(Instrument, OutputBus), FMOD.Studio.EventDescription>();
    int _next;
    int _sampleRate;
    // Mixer block (DSP buffer) length: a start less than one block ahead may lie in the block being mixed already
    // (Task 5b: setDelay 798 samples ahead at 15 fps, the note never sounded). Such notes are dropped.
    ulong _blockSamples = 1024;

    /// <summary>
    /// Notes stopped without sounding: their start time came within one mixer block before their channel group could
    /// be given it, the instance became invalid, or pausing it / setting its delay failed. They are never played late.
    /// </summary>
    public int DroppedCount { get; private set; }
    public int StartedCount { get; private set; }
    /// <summary>Notes cancelled (Stop at or before their start) before they sounded.</summary>
    public int CancelledCount { get; private set; }
    /// <summary>Times a note's stop (delay end and fade) was put back after FMOD Studio cleared it.</summary>
    public int StopReappliedCount { get; private set; }

    static ulong DelayEnd(FMOD.ChannelGroup g) => g.getDelay(out _, out ulong end, out _) == FMOD.RESULT.OK ? end : 0;

    public ulong NowDsp => MusicConductor.MasterDsp();

    /// <summary>
    /// Capture diagnosis only (set by CaptureScenario's logOneShots): CSV lines start / apply / check / drop per note
    /// with the DSP clock and the channel group's delay, pause flag and first channel position. Null (the default) =
    /// off: nothing is computed.
    /// </summary>
    public Action<string> DiagnosticLog;

    static long FirstChannelPosition(FMOD.ChannelGroup g, int depth)
    {
        if (g.getNumChannels(out int nc) == FMOD.RESULT.OK && nc > 0 && g.getChannel(0, out FMOD.Channel ch) == FMOD.RESULT.OK)
            return ch.getPosition(out uint p, FMOD.TIMEUNIT.PCM) == FMOD.RESULT.OK ? p : -2;
        if (depth < 4 && g.getNumGroups(out int ng) == FMOD.RESULT.OK)
            for (int i = 0; i < ng; i++)
                if (g.getGroup(i, out FMOD.ChannelGroup child) == FMOD.RESULT.OK)
                {
                    long p = FirstChannelPosition(child, depth + 1);
                    if (p != -1)
                        return p;
                }
        return -1;
    }

    string Probe(FMOD.ChannelGroup g)
    {
        g.getDelay(out ulong ds, out ulong de, out _);
        g.getPaused(out bool paused);
        g.getDSPClock(out ulong own, out ulong parent);
        return ds + "," + de + "," + (paused ? 1 : 0) + "," + parent + "," + FirstChannelPosition(g, 0);
    }

    public int Start(Instrument inst, OutputBus bus, int midi, float gainDb, ulong dspStart)
    {
        var key = (inst, bus);
        if (!_descriptions.TryGetValue(key, out var description))
        {
            string path = InstrumentTable.EventPath(inst, bus);
            if (path == null || FMODUnity.RuntimeManager.StudioSystem.getEvent(path, out description) != FMOD.RESULT.OK)
                return -1;
            _descriptions[key] = description;
        }
        if (description.createInstance(out var instance) != FMOD.RESULT.OK)
            return -1;
        instance.setParameterByName("Note", midi, true);
        instance.setVolume((float)Math.Pow(10.0, gainDb / 20.0));
        if (_sampleRate == 0)
        {
            FMODUnity.RuntimeManager.CoreSystem.getSoftwareFormat(out int rate, out _, out _);
            _sampleRate = rate; // OneShotRelease falls back when this is 0
            if (FMODUnity.RuntimeManager.CoreSystem.getDSPBufferSize(out uint block, out _) == FMOD.RESULT.OK)
                _blockSamples = block;
        }
        // Silent until Update has applied the start time to its channel group. A note that cannot be paused would
        // sound at dispatch: never start it.
        if (instance.setPaused(true) != FMOD.RESULT.OK || instance.start() != FMOD.RESULT.OK)
        {
            instance.release();
            return -1;
        }
        StartedCount++;
        if ((uint)bus < 64)
            _startedByBus[(int)bus]++;
        int handle = _next++;
        _notes[handle] = new Note
        {
            Instance = instance, Start = dspStart, ReleaseSamples = _release.SamplesFor(inst, bus, _sampleRate), Bus = bus,
            VoiceBlocks = InstrumentTable.VoiceDelayBlocks(inst, bus), PathBlocks = InstrumentTable.PathDelayBlocks(bus),
        };
        DiagnosticLog?.Invoke("start," + handle + "," + NowDsp + "," + dspStart);
        return handle;
    }

    /// <summary>
    /// True for an unknown handle, an event reporting STOPPED (its sample played out), and any failed query (invalid
    /// instance, Studio shut down or reloaded): such a note is gone and must not be kept forever. An event that was
    /// start()ed is STARTING / PLAYING until it ends, also while its channel group waits for the delayed start.
    /// </summary>
    public bool IsFinished(int handle)
    {
        if (!_notes.TryGetValue(handle, out var n))
            return true;
        if (!n.Instance.isValid())
            return true;
        return n.Instance.getPlaybackState(out var state) != FMOD.RESULT.OK || state == FMOD.Studio.PLAYBACK_STATE.STOPPED;
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
        if (n.Applied && !ApplyTimes(n.Group, n))
        {
            // The stop time could not be set: never let the note run on unbounded.
            CountDrop(in n);
            n.Instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            n.Instance.release();
            _notes.Remove(handle);
            return;
        }
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
    bool ApplyTimes(FMOD.ChannelGroup group, Note n)
    {
        ulong stop = StopAt(n);
        ulong end = n.Stop > 0 ? stop + n.ReleaseSamples : 0;
        if (group.setDelay(DelayStart(n), end, true) != FMOD.RESULT.OK)
            return false;
        group.removeFadePoints(0, ulong.MaxValue);
        if (n.Stop > 0)
        {
            group.addFadePoint(stop, 1f);
            group.addFadePoint(end, 0f);
        }
        return true;
    }

    ulong StopAt(in Note n) => OneShotArming.StopAt(n.Stop, n.PathBlocks, _blockSamples);
    ulong DelayStart(in Note n) => OneShotArming.DelayStart(n.Start, n.VoiceBlocks, n.PathBlocks, _blockSamples);

    /// <summary>
    /// Applies start / stop times once each instance's channel group exists, then unpauses it. A note keeps waiting
    /// while its start is ahead (however many frames that takes); one whose start is reached first (or whose instance
    /// is invalid) is stopped immediately and dropped, not played late (<see cref="OneShotArming"/>).
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
                if (DiagnosticLog != null && (n.Checks < 6 || (n.Checks < 400 && n.Stop > 0 && now < n.Stop + n.ReleaseSamples + 4096)))
                {
                    n.Checks++;
                    _notes[key] = n;
                    DiagnosticLog("check," + key + "," + now + "," + n.Start + "," + n.Checks + "," + Probe(n.Group));
                }
                // FMOD Studio clears the group's delay end (and its fade points) on one of its updates after the unpause
                // (Task 11: in most notes' 3rd-6th check the delay end read 0), so the note ran until Update stopped the
                // instance a frame after its stop time. Put the stop back while it is still ahead (every bus).
                if (OneShotArming.StopLost(DelayEnd(n.Group), StopAt(n), n.ReleaseSamples, now))
                {
                    if (ApplyTimes(n.Group, n))
                        StopReappliedCount++;
                    DiagnosticLog?.Invoke("reapply," + key + "," + now + "," + n.Start + "," + n.Stop);
                }
                bool playedOut = n.Stop == 0 && n.Released && IsFinished(key);
                if (playedOut || (n.Released && n.Stop > 0 && now >= StopAt(n) + n.ReleaseSamples))
                {
                    n.Instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                    n.Instance.release();
                    _notes.Remove(key);
                }
                continue;
            }
            bool valid = n.Instance.isValid();
            FMOD.ChannelGroup group = default;
            bool hasGroup = valid && n.Instance.getChannelGroup(out group) == FMOD.RESULT.OK && group.hasHandle();
            n.Frames++;
            // The group's delay must be in place a block before the group itself starts (DelayStart, up to a block before
            // the audible start).
            var arm = OneShotArming.Decide(valid, hasGroup, DelayStart(n), now, _blockSamples);
            if (arm == OneShotArm.Wait)
            {
                _notes[key] = n;
                continue;
            }
            if (arm == OneShotArm.Apply)
            {
                if (DiagnosticLog != null)
                    DiagnosticLog("apply," + key + "," + now + "," + n.Start + "," + n.Frames + "," + Probe(group));
                // "The group exists" does not mean the voice does: Studio may create the channel later, and the parent
                // group's delay (set here) still gates it.
                if (!ApplyTimes(group, n))
                {
                    // Never let a note start un-delayed.
                    DiagnosticLog?.Invoke("drop," + key + "," + now + "," + n.Start + "," + n.Frames + ",delayfail");
                    CountDrop(in n);
                    n.Instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                    n.Instance.release();
                    _notes.Remove(key);
                    continue;
                }
                // The delay is in effect. Unpause the instance first, then the group (core call, immediate, so the
                // start does not wait for a Studio update): the other order leaves a window in which a Studio update
                // between the two calls re-asserts the instance's pause on the group.
                // (Results not checked: the delay is already in effect, so a failed unpause can only keep the note silent,
                // never early or late.)
                n.Instance.setPaused(false);
                group.setPaused(false);
                n.Group = group;
                n.Applied = true;
                _notes[key] = n;
                continue;
            }
            // Start reached before it could be applied, or the instance is gone: drop it, never play it late.
            DiagnosticLog?.Invoke("drop," + key + "," + now + "," + n.Start + "," + n.Frames + "," + (hasGroup ? 1 : 0));
            CountDrop(in n);
            n.Instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            n.Instance.release();
            _notes.Remove(key);
        }
    }
}
