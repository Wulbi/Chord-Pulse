using System;
using System.Collections.Generic;
using NUnit.Framework;

public class PatternSchedulerTests
{
    sealed class FakeBackend : IOneShotBackend
    {
        public readonly List<(int handle, Instrument inst, int midi, float gainDb, ulong dspStart)> Starts =
            new List<(int, Instrument, int, float, ulong)>();
        public readonly List<(int handle, ulong dspStop)> Stops = new List<(int, ulong)>();
        public readonly List<ulong> NowAtStart = new List<ulong>();
        public readonly List<int> Releases = new List<int>();
        public ulong Now;
        int _next;
        public ulong NowDsp => Now;

        public int Start(Instrument inst, OutputBus bus, int midi, float gainDb, ulong dspStart)
        {
            int handle = _next++;
            Starts.Add((handle, inst, midi, gainDb, dspStart));
            NowAtStart.Add(Now);
            return handle;
        }

        public void Stop(int handle, ulong dspStop) => Stops.Add((handle, dspStop));
        public void Release(int handle) => Releases.Add(handle);
        public bool IsFinished(int handle) => false;
    }

    sealed class FakeArrangement : IArrangement
    {
        public Func<int, ArrangementState> F;
        public int MinStep = int.MaxValue;
        public ArrangementState At(int step)
        {
            MinStep = Math.Min(MinStep, step);
            return F(step);
        }
    }

    const double SamplesPerBeat = 24000; // 120 BPM at 48 kHz

    // NUnit shares one fixture instance between tests: build fresh state per test.
    BeatClock _clock;
    FakeBackend _backend;
    OneShotScheduler _shots;
    ArrangementState _state;
    FakeArrangement _arr;
    List<(PatternNote note, double beat)> _events;

    [SetUp]
    public void SetUp()
    {
        _clock = new BeatClock(48000);
        _clock.Anchor(0, 0, 120);
        _backend = new FakeBackend();
        _shots = new OneShotScheduler(_clock, _backend) { ScheduleOffsetSamples = 0 };
        _state = new ArrangementState { Style = 0, Variation = 0, Combo = 0, Intensity = 1f, Fever = FeverPhaseMask.None, SectionPhrase = 1 };
        _arr = new FakeArrangement { F = step => _state };
        _events = new List<(PatternNote, double)>();
    }

    PatternScheduler Make(double lookahead = 1.0, ulong mask = ulong.MaxValue, double window = 0, IOneShotBackend unused = null)
    {
        var p = new PatternScheduler(_shots, _arr, lookahead, mask, window);
        p.NoteScheduled += (n, b) => _events.Add((n, b));
        return p;
    }

    void At(double beat) => _backend.Now = (ulong)Math.Round(beat * SamplesPerBeat);

    static int ClipIndex(string name) => Array.IndexOf(PatternTable.ClipNames, name);
    static int LayerBit(string name) => Array.IndexOf(PatternTable.LayerNames, name);

    static bool IsNoteOf(string clipName, in PatternNote n, double beat)
    {
        var c = PatternTable.Clips[ClipIndex(clipName)];
        for (int i = c.First; i < c.First + c.Count; i++)
        {
            var m = PatternTable.Notes[i];
            if (m.Beat == (float)(beat % 16.0) && m.Midi == n.Midi && m.GainDb == n.GainDb && m.Instrument == n.Instrument && m.LengthBeats == n.LengthBeats)
                return true;
        }
        return false;
    }

    int CountOf(string clipName, double beatFrom = double.NegativeInfinity)
    {
        int count = 0;
        foreach (var e in _events)
            if (e.beat >= beatFrom && IsNoteOf(clipName, e.note, e.beat))
                count++;
        return count;
    }

    // ---- PatternMatch ----

    static PatternClip Clip(int styleMask = 15, byte vMin = 0, byte vMax = 4, byte comboMin = 0, float intensityMin = 0f,
        FeverPhaseMask fever = (FeverPhaseMask)31, byte loop = 0, byte section = 0) =>
        new PatternClip(0, styleMask, vMin, vMax, comboMin, intensityMin, fever, loop, section, 0, 0);

    static ArrangementState S(int style = 0, int variation = 0, int combo = 0, float intensity = 1f,
        FeverPhaseMask fever = FeverPhaseMask.None, int loop = 0, int section = 1) =>
        new ArrangementState { Style = style, Variation = variation, Combo = combo, Intensity = intensity, Fever = fever, FeatureLoop = loop, SectionPhrase = section };

    [Test]
    public void Matches_Conditions()
    {
        Assert.IsTrue(PatternMatch.Matches(Clip(styleMask: 2), S(style: 1)));
        Assert.IsFalse(PatternMatch.Matches(Clip(styleMask: 2), S(style: 0)));
        Assert.IsTrue(PatternMatch.Matches(Clip(vMin: 1, vMax: 2), S(variation: 2)));
        Assert.IsFalse(PatternMatch.Matches(Clip(vMin: 1, vMax: 2), S(variation: 3)));
        Assert.IsFalse(PatternMatch.Matches(Clip(vMin: 1, vMax: 2), S(variation: 0)));
        Assert.IsTrue(PatternMatch.Matches(Clip(comboMin: 3), S(combo: 3)));
        Assert.IsFalse(PatternMatch.Matches(Clip(comboMin: 3), S(combo: 2)));
        Assert.IsTrue(PatternMatch.Matches(Clip(intensityMin: 0.5f), S(intensity: 0.5f)));
        Assert.IsFalse(PatternMatch.Matches(Clip(intensityMin: 0.5f), S(intensity: 0.4f)));
        Assert.IsTrue(PatternMatch.Matches(Clip(fever: FeverPhaseMask.Fever), S(fever: FeverPhaseMask.Fever)));
        Assert.IsFalse(PatternMatch.Matches(Clip(fever: FeverPhaseMask.Fever), S(fever: FeverPhaseMask.None)));
        Assert.IsTrue(PatternMatch.Matches(Clip(loop: 0), S(loop: 2)));
        Assert.IsTrue(PatternMatch.Matches(Clip(loop: 2), S(loop: 2)));
        Assert.IsFalse(PatternMatch.Matches(Clip(loop: 2), S(loop: 1)));
    }

    [Test]
    public void Matches_SectionPhrase_AnyMatchesBoth()
    {
        Assert.IsTrue(PatternMatch.Matches(Clip(section: 0), S(section: 1)));
        Assert.IsTrue(PatternMatch.Matches(Clip(section: 0), S(section: 2)));
        Assert.IsTrue(PatternMatch.Matches(Clip(section: 1), S(section: 1)));
        Assert.IsFalse(PatternMatch.Matches(Clip(section: 1), S(section: 2)));
        Assert.IsTrue(PatternMatch.Matches(Clip(section: 2), S(section: 2)));
        Assert.IsFalse(PatternMatch.Matches(Clip(section: 2), S(section: 1)));
    }

    // ---- PatternScheduler ----

    [Test]
    public void Update_SchedulesOnlyWithinLookahead()
    {
        var p = Make();
        At(0);
        p.Update(0);
        Assert.Greater(_events.Count, 0);
        foreach (var e in _events)
            Assert.That(e.beat, Is.GreaterThan(0).And.LessThanOrEqualTo(1.0));
    }

    [Test]
    public void Update_DoesNotDoubleSchedule()
    {
        var p = Make();
        At(0.5);
        p.Update(0.5);
        int first = _events.Count;
        Assert.Greater(first, 0);
        p.Update(0.5);
        Assert.AreEqual(first, _events.Count);
        Assert.AreEqual(first, p.ScheduledCount);
    }

    [Test]
    public void Variation1_PlaysBassDrive_NotBassA()
    {
        _state.Variation = 1;
        var p = Make();
        At(15.5);
        p.Update(15.5); // (15.5, 16.5]: includes the next phrase's first beat
        bool drive = false;
        foreach (var e in _events)
        {
            if (e.beat == 16.0 && e.note.Midi == 36 && IsNoteOf("BassDrive", e.note, e.beat))
                drive = true;
            Assert.IsFalse(IsNoteOf("BassA", e.note, e.beat), "BassA note at " + e.beat);
        }
        Assert.IsTrue(drive);
        Assert.AreEqual(0, CountOf("BassA"));
    }

    [Test]
    public void Variation0_PlaysBassA_NotBassDrive()
    {
        var p = Make();
        At(15.5);
        p.Update(15.5);
        Assert.AreEqual(1, CountOf("BassA"));
        Assert.AreEqual(0, CountOf("BassDrive"));
    }

    [Test]
    public void Breath_IntensityZero_DropsIntensityClips()
    {
        foreach (int style in new[] { 3, 0 })
        {
            _events.Clear();
            _state.Style = style;
            _state.Intensity = 0f;
            var p = Make(lookahead: 16.0);
            At(0.5);
            p.Update(0.5);
            Assert.Greater(_events.Count, 0, "style " + style);
            foreach (var e in _events)
                Assert.AreNotEqual(Instrument.Pluck, e.note.Instrument, "pluck in style " + style);
            // no note of any clip with IntensityMin > 0 (style 3: none of them plays at all; style 0: pluck, checked above)
            foreach (var e in _events)
                foreach (var clipName in new[] { "PluckA1", "PluckA2", "LatinMarimba", "FunkKeys", "FunkGuitar" })
                    Assert.IsFalse(IsNoteOf(clipName, e.note, e.beat), clipName + " in style " + style);
        }
    }

    [Test]
    public void Intensity1_Style0_PlaysPluck()
    {
        var p = Make(lookahead: 16.0);
        At(0.5);
        p.Update(0.5);
        Assert.Greater(CountOf("PluckA1"), 0);
    }

    [Test]
    public void SectionPhrase2_PlaysPluckA2_NotPluckA1()
    {
        _state.SectionPhrase = 2;
        var p = Make(lookahead: 16.0);
        At(0.5);
        p.Update(0.5);
        Assert.Greater(CountOf("PluckA2"), 0);
        Assert.AreEqual(0, CountOf("PluckA1"));

        _events.Clear();
        _state.SectionPhrase = 1;
        p.Reset();
        p.Update(0.5);
        Assert.Greater(CountOf("PluckA1"), 0);
        Assert.AreEqual(0, CountOf("PluckA2"));
    }

    [Test]
    public void Invalidate_CancelsOwnNotesOnly()
    {
        var other = new object();
        var p = Make();
        At(0);
        Assert.IsTrue(_shots.Schedule(new OneShotNote((Instrument)0, OutputBus.KillChords, 60, 5.0, 0.5, 0, 0f, 0.0), other));
        p.Update(0);
        Assert.Greater(p.ScheduledCount, 0);
        Assert.AreEqual(1 + p.ScheduledCount, _shots.ActiveCount);
        p.Invalidate(0, 0, 2.0);
        Assert.AreEqual(1, _shots.ActiveCount);
    }

    [Test]
    public void ComboDrop_ReschedulesNextBar()
    {
        _state.Combo = 3;
        var p = Make(lookahead: 2.0);
        At(2.5);
        p.Update(2.5); // scheduled until 4.5
        Assert.Greater(CountOf("ComboBells", 4.0), 0, "bell at 4.5 scheduled with combo 3");

        _state.Combo = 0;
        p.Invalidate(4, 2.5, 2.2);
        At(3.5);
        p.Update(3.5);
        int bellsAfter = CountOf("ComboBells", 4.0);
        Assert.AreEqual(1, bellsAfter, "only the original bell at 4.5 was counted; none re-scheduled");
        At(4.4);
        _shots.Update();
        foreach (var s in _backend.Starts)
            Assert.IsFalse(s.inst == Instrument.Bells && s.dspStart >= (ulong)(4.0 * SamplesPerBeat), "cancelled bell started");
    }

    [Test]
    public void StyleChange_AtPhraseBoundary()
    {
        _arr.F = step => { var s = _state; s.Style = step >= 32 ? 1 : 0; return s; };
        var p = Make();
        At(15.0);
        p.Update(15.0); // (15, 16]
        int at155 = 0, at16 = 0;
        foreach (var e in _events)
        {
            if (e.beat == 15.5)
            {
                at155++;
                Assert.IsTrue(e.note.Instrument != Instrument.BassEnv && e.note.Instrument != Instrument.Marimba, "latin at 15.5: " + e.note.Instrument);
            }
            if (e.beat == 16.0)
            {
                if (e.note.Instrument == Instrument.BassEnv || e.note.Instrument == Instrument.Marimba)
                    at16++;
                Assert.IsTrue(e.note.Instrument != Instrument.Bass && e.note.Instrument != Instrument.Pluck, "synthpop at 16: " + e.note.Instrument);
            }
        }
        Assert.Greater(at155, 0);
        Assert.Greater(at16, 0);
    }

    [Test]
    public void LandingBeats_UseDefaultDispatchWindow()
    {
        // No landing special case any more: the boundary beat waits for half a beat like every other beat.
        var p = Make();
        At(15.0);
        p.Update(15.0);
        _shots.Update();
        ulong boundary = (ulong)(16 * SamplesPerBeat);
        // The beat 16 notes are already scheduled (lookahead 1 beat), so the check below is about the dispatch window,
        // not about notes that do not exist yet.
        int pending16 = 0;
        foreach (var e in _events)
            if (Math.Abs(e.beat - 16.0) < 1e-9)
                pending16++;
        Assert.Greater(pending16, 0, "no beat 16 note was scheduled at 15.0");
        foreach (var s in _backend.Starts)
            Assert.Less(s.dspStart + 8000, boundary, "a beat 16 note was handed over a beat early");

        // within half a beat, the boundary beat's notes are handed over.
        _backend.Starts.Clear();
        At(15.6);
        p.Update(15.6);
        _shots.Update();
        int atBoundary = 0;
        foreach (var s in _backend.Starts)
            if (s.dspStart <= boundary && s.dspStart + 8000 >= boundary)
                atBoundary++;
        Assert.Greater(atBoundary, 0);

        // beat 18 likewise.
        _backend.Starts.Clear();
        At(17.0);
        p.Update(17.0);
        ulong at18 = (ulong)(18 * SamplesPerBeat);
        foreach (var s in _backend.Starts)
            Assert.Less(s.dspStart + 8000, at18, "a beat 18 note was handed over a beat early");
    }

    [Test]
    public void LayerMask_SkipsOtherLayers()
    {
        var p = Make(lookahead: 16.0, mask: 1UL << LayerBit("Pluck"));
        At(0.5);
        p.Update(0.5);
        Assert.Greater(_events.Count, 0);
        foreach (var e in _events)
            Assert.AreEqual(Instrument.Pluck, e.note.Instrument);
    }

    [Test]
    public void DispatchWindow_Override_HandsOverEarly()
    {
        var p = Make(lookahead: 4.0, window: 4.0);
        At(0);
        p.Update(0);
        ulong at4 = (ulong)(4 * SamplesPerBeat);
        bool handed = false;
        foreach (var s in _backend.Starts)
            if (s.dspStart + 8000 >= at4 && s.dspStart <= at4)
                handed = true;
        Assert.IsTrue(handed, "a beat 4 note is with the backend at now 0");
    }

    [Test]
    public void DefaultWindow_DoesNotHandOverEarly()
    {
        var p = Make(lookahead: 4.0);
        At(0);
        p.Update(0);
        ulong at4 = (ulong)(4 * SamplesPerBeat);
        foreach (var s in _backend.Starts)
            Assert.Less(s.dspStart, at4 - 8000, "only notes of the first beats are handed over");
    }

    [Test]
    public void Reset_ClearsScheduledUntil()
    {
        var p = Make();
        At(0.5);
        p.Update(0.5);
        int first = _events.Count;
        Assert.Greater(first, 0);
        p.Reset();
        Assert.AreEqual(0, p.ScheduledCount);
        p.Update(0.5);
        Assert.AreEqual(2 * first, _events.Count);
    }

    [Test]
    public void EarlyOnset_PhraseFirstBeat_ScheduledBeforeBoundary()
    {
        _state.Style = 3;
        // the beat-0 Choir note with the largest onset
        var clip = PatternTable.Clips[ClipIndex("DreamyChoir")];
        int midi = -1;
        double onset = 0;
        for (int i = clip.First; i < clip.First + clip.Count; i++)
        {
            var n = PatternTable.Notes[i];
            if (n.Beat == 0f && InstrumentTable.OnsetSeconds(n.Instrument, n.Midi) >= onset)
            {
                onset = InstrumentTable.OnsetSeconds(n.Instrument, n.Midi);
                midi = n.Midi;
            }
        }
        Assert.Greater(onset, 0.0);
        var p = Make();
        Assert.Greater(p.MaxOnsetSeconds, 0.05);
        At(15.5); // half a beat before the boundary
        p.Update(15.5);
        ulong boundary = (ulong)(16 * SamplesPerBeat);
        bool found = false;
        foreach (var s in _backend.Starts)
            if (s.inst == Instrument.Choir && s.midi == midi)
            {
                found = true;
                Assert.Less(s.dspStart, boundary, "starts before the boundary by its onset");
            }
        Assert.IsTrue(found, "handed to the backend while the boundary is still half a beat away");
        for (int i = 0; i < _backend.Starts.Count; i++)
            if (_backend.Starts[i].inst == Instrument.Choir && _backend.Starts[i].midi == midi)
                Assert.LessOrEqual(_backend.NowAtStart[i], boundary - 12000, "handed over at least 0.5 beat before the boundary");
        Assert.AreEqual(0, _shots.LateCount);
    }

    // Choir chord on beat 16 at 120 BPM (onsets 71.0 / 56.4 / 47.4 ms for MIDI 60 / 64 / 67), so the starts are
    // 71.0, 56.4 and 47.4 ms before the boundary.
    [Test]
    public void Invalidate_DoesNotDuplicateStartedEarlyOnsetNote()
    {
        _state.Style = 3;
        var p = Make();
        At(15.0);
        p.Update(15.0); // choir chord on beat 16 handed over
        int choirBefore = CountOf("DreamyChoir", 16.0);
        Assert.AreEqual(3, choirBefore);
        int startsBefore = _backend.Starts.Count;
        int stopsBefore = _backend.Stops.Count; // (Stop is also called once per note with its end time)

        // 30 ms before the boundary (beat 15.94): all three have started, by 41 / 26 / 17 ms.
        At(15.94);
        p.Invalidate(16, 15.94, 2.0);
        Assert.AreEqual(stopsBefore, _backend.Stops.Count, "nothing started was cancelled");
        p.Update(15.94);
        Assert.AreEqual(choirBefore, CountOf("DreamyChoir", 16.0), "no choir note scheduled twice");
        foreach (var st in _backend.Starts.GetRange(startsBefore, _backend.Starts.Count - startsBefore))
            Assert.AreNotEqual(Instrument.Choir, st.inst);
        Assert.AreEqual(0, _shots.LateCount, "a started note was handed over again and dropped as late");
    }

    [Test]
    public void Invalidate_RestatesNotStartedEarlyOnsetNotes_Once()
    {
        _state.Style = 3;
        var p = Make();
        At(15.0);
        p.Update(15.0);
        Assert.AreEqual(3, CountOf("DreamyChoir", 16.0));

        // beat 15.5 (250 ms before the boundary): the beat 16 notes are handed over (half-beat window, no landing
        // special case) but none has started; the arrangement changes from beat 16.
        At(15.5);
        _shots.Update();
        _state.Combo = 3;
        int stopsBefore = _backend.Stops.Count;
        p.Invalidate(16, 15.5, 2.0);
        Assert.GreaterOrEqual(_backend.Stops.Count - stopsBefore, 3, "the handed-over beat 16 notes were cancelled");
        _events.Clear();
        p.Update(15.5);
        int choirAgain = 0;
        foreach (var e in _events)
            if (e.beat == 16.0 && e.note.Instrument == Instrument.Choir)
                choirAgain++;
        Assert.AreEqual(3, choirAgain);
        Assert.AreEqual(0, _shots.LateCount);
    }

    [Test]
    public void Invalidate_RestatesNoteExactlyAtFromBeat_Once()
    {
        var p = Make(lookahead: 4.0);
        At(0.5);
        p.Update(0.5); // until 4.5
        int before = 0;
        bool onFour = false;
        foreach (var e in _events)
            if (e.beat >= 4.0)
            {
                before++;
                onFour |= e.beat == 4.0;
            }
        Assert.Greater(before, 0);
        Assert.IsTrue(onFour, "a note exactly on beat 4");
        _events.Clear();
        p.Invalidate(4, 0.5, 2.0);
        p.Update(0.5);
        Assert.AreEqual(before, _events.Count);
        foreach (var e in _events)
            Assert.GreaterOrEqual(e.beat, 4.0);
        p.Update(0.5);
        Assert.AreEqual(before, _events.Count, "taken once");
    }

    [Test]
    public void Invalidate_NonPositiveTempo_Throws()
    {
        var p = Make();
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Invalidate(4, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Invalidate(4, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Invalidate(4, 0, double.NaN));
    }

    [Test]
    public void Constructor_NonPositiveLookahead_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PatternScheduler(_shots, _arr, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PatternScheduler(_shots, _arr, -1));
    }

    [Test]
    public void StepsPerBeat_EqualsSoundManager()
    {
        Assert.AreEqual(SoundManager.StepsPerBeat, PatternScheduler.StepsPerBeat);
    }

    [Test]
    public void Update_NegativeNow_SchedulesNothingBeforeBeatZero()
    {
        _state.Variation = 1;
        _clock.Anchor(0, 48000, 120); // beat 0 sounds at DSP 48000
        _backend.Now = 36000;          // beat -0.5
        var p = Make();
        p.Update(-0.5); // window -0.5 .. 0.5
        Assert.GreaterOrEqual(_arr.MinStep, 0, "At was asked for a negative step");
        Assert.Greater(_events.Count, 0);
        bool drive0 = false;
        foreach (var e in _events)
        {
            Assert.GreaterOrEqual(e.beat, 0.0);
            if (e.beat == 0.0 && IsNoteOf("BassDrive", e.note, 0))
                drive0 = true;
        }
        Assert.IsTrue(drive0, "beat 0 notes are taken");
    }

    [Test]
    public void Update_FromNegativeNow_TakesBeatZeroOnce()
    {
        _clock.Anchor(0, 48000, 120);
        _backend.Now = 36000;
        var p = Make();
        p.Update(-0.5);
        int zero = 0;
        foreach (var e in _events)
            if (e.beat == 0.0)
                zero++;
        Assert.Greater(zero, 0);
        _backend.Now = 48000 + 4800; // beat 0.2
        p.Update(0.2);
        int zeroAfter = 0;
        foreach (var e in _events)
            if (e.beat == 0.0)
                zeroAfter++;
        Assert.AreEqual(zero, zeroAfter);
    }

    [Test]
    public void Update_FarBeforeZero_SchedulesNothingThenBeatZero()
    {
        _clock.Anchor(0, 96000, 120);
        _backend.Now = 24000;
        var p = Make();
        p.Update(-3.0); // window ends at -2
        Assert.AreEqual(0, _events.Count);
        Assert.AreEqual(int.MaxValue, _arr.MinStep, "At is not asked for a window before beat 0");
        _backend.Now = 96000 - 12000;
        p.Update(-0.5);
        Assert.Greater(_events.Count, 0);
        foreach (var e in _events)
            Assert.GreaterOrEqual(e.beat, 0.0);
        Assert.GreaterOrEqual(_arr.MinStep, 0);
    }

    [Test]
    public void Update_MultiBeatJump_TakesEachNoteOnceAndSkipsPast()
    {
        var p = Make();
        At(0.5);
        p.Update(0.5);
        _events.Clear();
        At(6.3);
        p.Update(6.3); // a hitch: (6.3, 7.3]
        var reference = new List<(PatternNote, double)>();
        var p2 = new PatternScheduler(_shots, _arr, 1.0);
        p2.NoteScheduled += (n, b) => reference.Add((n, b));
        p2.Update(6.3);
        Assert.AreEqual(reference.Count, _events.Count);
        Assert.Greater(_events.Count, 0);
        foreach (var e in _events)
            Assert.That(e.beat, Is.GreaterThan(6.3).And.LessThanOrEqualTo(7.3));
        Assert.AreEqual(0, _shots.LateCount);
    }

    [Test]
    public void Update_NowGoingBackwards_SchedulesNothing()
    {
        var p = Make();
        At(5.0);
        p.Update(5.0);
        _events.Clear();
        p.Update(3.0);
        Assert.AreEqual(0, _events.Count);
    }

    [Test]
    public void Matches_FeverZero_IsNone_AndEachPhase()
    {
        Assert.IsTrue(PatternMatch.Matches(Clip(fever: FeverPhaseMask.None), S(fever: 0)));
        Assert.IsFalse(PatternMatch.Matches(Clip(fever: FeverPhaseMask.Fever), S(fever: 0)));
        foreach (var phase in new[] { FeverPhaseMask.Build, FeverPhaseMask.Impact, FeverPhaseMask.Outro })
        {
            Assert.IsTrue(PatternMatch.Matches(Clip(fever: phase), S(fever: phase)), phase.ToString());
            Assert.IsFalse(PatternMatch.Matches(Clip(fever: FeverPhaseMask.None | FeverPhaseMask.Fever), S(fever: phase)), phase.ToString());
        }
    }

    [Test]
    public void Invalidate_NeverSchedulesBeatsAtOrBeforeNow()
    {
        var p = Make();
        At(3.0);
        p.Update(3.0);
        _events.Clear();
        p.Invalidate(0, 3.0, 2.0);
        p.Update(3.0);
        foreach (var e in _events)
            Assert.Greater(e.beat, 3.0);
        Assert.AreEqual(0, _shots.LateCount);
    }

    // ---- Task 12: drums (percussion numbers: FMOD/Tools/samples.py PERC) ----

    const int Kick = 36, Snare = 38, ClosedHat = 42, Shaker = 82;

    PatternScheduler MakeCore() =>
        Make(lookahead: PatternTable.CoreLookaheadBeats, mask: PatternTable.CoreLayerMask, window: PatternTable.CoreLookaheadBeats);

    int CountDrum(int number, Func<double, bool> beatFilter = null)
    {
        int count = 0;
        foreach (var e in _events)
            if (e.note.Instrument == Instrument.Drums && e.note.Midi == number && (beatFilter == null || beatFilter(e.beat)))
                count++;
        return count;
    }

    static bool OnSixteenthEorA(double beat)
    {
        double f = beat - Math.Floor(beat);
        return f == 0.25 || f == 0.75;
    }

    [Test]
    public void Funk_Combo1_Adds16thShaker()
    {
        _state.Style = 2;
        _state.Combo = 0;
        var p = Make(lookahead: 16.0, mask: ~PatternTable.CoreLayerMask);
        At(0.1);
        p.Update(0.1);
        Assert.Greater(CountDrum(ClosedHat), 0, "funk 16th hats play at combo 0");
        Assert.AreEqual(0, CountDrum(Shaker), "no shaker in funk at combo 0");

        _events.Clear();
        _state.Combo = 1;
        p.Reset();
        p.Update(0.1);
        Assert.AreEqual(32, CountDrum(Shaker, OnSixteenthEorA), "16th shaker on every e / a of the phrase (beat 0.25 .. 15.75)");
        Assert.AreEqual(CountDrum(Shaker), CountDrum(Shaker, OnSixteenthEorA), "funk has no 8th shaker");
    }

    // Feature-loop notes handed to the scheduler (their bus is a sub-bus of bus:/Feature).
    List<double> FeatureBeats()
    {
        var beats = new List<double>();
        foreach (var e in _events)
            if (InstrumentCoverageTests.FeatureFlag(e.note.Bus) != 0)
                beats.Add(e.beat);
        return beats;
    }

    [Test]
    public void FeatureLoop_PlaysOnlyItsBars()
    {
        // Loop 2 "Trumpet Rise": startBar 3, 1 bar -> beats 12 .. 16 of its phrase only, all 12 notes of it.
        var loop = FeatureLoopNotes.Loops[1];
        Assert.AreEqual((2, 3, 1), (loop.Id, loop.StartBar, loop.Bars));
        _state.FeatureLoop = 2;
        var p = Make(lookahead: 16.0);
        At(0);
        p.Update(-0.01); // from beat 0 on
        var beats = FeatureBeats();
        Assert.AreEqual(12, beats.Count, "loop 2 has 12 notes (two 3-note stabs)");
        foreach (double b in beats)
            Assert.IsTrue(b >= 12.0 && b < 16.0, $"loop 2 note at beat {b}, outside bar 4");

        // No loop: no feature note; another loop: none of loop 2's beats-12..16-only shape.
        foreach (int other in new[] { 0, 1 })
        {
            _events.Clear();
            _state.FeatureLoop = other;
            p.Reset();
            p.Update(-0.01);
            beats = FeatureBeats();
            if (other == 0)
                Assert.AreEqual(0, beats.Count, "feature notes without a loop");
            else
                foreach (double b in beats)
                    Assert.IsTrue(b >= 0.0 && b < 4.0, $"loop 1 (bar 1) note at beat {b}");
        }
    }

    [Test]
    public void DrumNotes_PlayToSampleEnd()
    {
        int drums = 0;
        foreach (var n in PatternTable.Notes)
            if (n.Instrument == Instrument.Drums)
            {
                drums++;
                Assert.AreEqual(0f, n.LengthBeats, $"drum note {n.Midi} at beat {n.Beat}");
            }
        Assert.Greater(drums, 0, "no drum notes in the pattern table");
    }

    [Test]
    public void CoreKick_SilentInBuildBar()
    {
        _state.Fever = FeverPhaseMask.Build;
        var p = MakeCore();
        At(0.1);
        p.Update(0.1);
        Assert.AreEqual(0, CountDrum(Kick), "kick in the build-up bar");
        Assert.Greater(CountDrum(Snare), 0, "the snare keeps playing in the build-up bar");

        foreach (var phase in new[] { FeverPhaseMask.None, FeverPhaseMask.Impact, FeverPhaseMask.Fever, FeverPhaseMask.Outro })
        {
            _events.Clear();
            _state.Fever = phase;
            p.Reset();
            p.Update(0.1);
            Assert.AreEqual(2, CountDrum(Kick), "kicks at 2 and 4 in " + phase);
        }
    }

    [Test]
    public void CoreBackbeat_OnlyInSynthpop()
    {
        _state.Style = 1;
        var p = MakeCore();
        At(0.1);
        p.Update(0.1);
        Assert.AreEqual(0, CountDrum(Snare) + CountDrum(ClosedHat), "core snare / hat in latin");
        Assert.Greater(CountDrum(Kick), 0, "the kick plays in every style");
        Assert.IsFalse(PatternTable.StyleUsesCoreBackbeat[1]);

        _events.Clear();
        _state.Style = 0;
        p.Reset();
        p.Update(0.1);
        Assert.Greater(CountDrum(Snare), 0);
        Assert.Greater(CountDrum(ClosedHat), 0);
        Assert.IsTrue(PatternTable.StyleUsesCoreBackbeat[0]);
    }

    [Test]
    public void Core_LookaheadIsOneBar()
    {
        Assert.AreEqual(4.0, PatternTable.CoreLookaheadBeats);
        var p = MakeCore();
        At(0);
        p.Update(0);
        Assert.AreEqual(1, CountDrum(Kick, b => b == 4.0), "the kick of beat 4 is scheduled at now 0");
        // ... and handed to FMOD at once (dispatch window = the lookahead), not half a beat before it.
        ulong at4 = (ulong)(4 * SamplesPerBeat);
        bool handed = false;
        foreach (var s in _backend.Starts)
            if (s.inst == Instrument.Drums && s.midi == Kick && s.dspStart + 8000 >= at4 && s.dspStart <= at4)
                handed = true;
        Assert.IsTrue(handed, "beat 4 kick not handed to the backend at now 0");
        // the other layers are not this scheduler's
        foreach (var e in _events)
            Assert.AreEqual(Instrument.Drums, e.note.Instrument);
    }

    // ---- Task 14: fever layers (pattern_fever.py); the arrangement is ArrangementTimeline's step function ----

    // A phrase from step 0 in one style, fever queued from feverStartStep (-1: none); everything else neutral.
    static ArrangementInputs FeverInputs(int feverStartStep, int style) => new ArrangementInputs
    {
        PhraseStartStep = 0, StyleCurrent = style, StyleNext = style, SectionPhraseCurrent = 1, SectionPhraseNext = 2,
        Intensity = 1, ComboBar = int.MaxValue, FeverStart = feverStartStep,
    };

    static readonly string[] LatinClips = { "LatinBass", "LatinMarimba", "LatinConga", "LatinCowbell", "LatinBongo", "LatinClaves", "LatinShaker" };

    [Test]
    public void FeverQueued_ReplacesBuildBar()
    {
        var inputs = FeverInputs(-1, 1); // latin
        _arr.F = step => ArrangementTimeline.At(step, in inputs);
        var p = Make(lookahead: 4.0, mask: ~PatternTable.CoreLayerMask);
        At(11.0);
        p.Update(11.0); // (11, 15]: bar 4 of the phrase in latin
        Assert.Greater(CountOf("LatinBass", 12.0), 0, "the latin bass of bar 4 is scheduled");
        Assert.AreEqual(0, CountOf("FeverBuildRoll") + CountOf("FeverBuildRiser"));

        // QueueFever: fever from beat 16, so bar 4 (beats 12..16) becomes the build-up bar (style 0 + roll and riser).
        inputs.FeverStart = 32;
        _events.Clear();
        p.Invalidate(12, 11.0, 2.0);
        p.Update(11.0);
        Assert.AreEqual(9, CountOf("FeverBuildRoll"), "roll at 12, 12.5, 13, 13.5, 14, 14.25, 14.5, 14.75, 15");
        Assert.AreEqual(5, CountOf("FeverBuildRiser"), "riser at 14 .. 15 (bar 4's notes only)");
        foreach (var e in _events)
            Assert.GreaterOrEqual(e.beat, 12.0, "only the build-up bar is re-stated");
        foreach (var name in LatinClips)
            Assert.AreEqual(0, CountOf(name), name + " re-scheduled in the build-up bar");
        Assert.Greater(CountOf("BassA"), 0, "the build-up bar plays style 0");

        At(15.0);
        _shots.Update();
        ulong build = (ulong)(12 * SamplesPerBeat) - 4096;
        foreach (var s in _backend.Starts)
            Assert.IsFalse(s.dspStart >= build && (s.inst == Instrument.BassEnv || s.inst == Instrument.Marimba),
                $"cancelled latin note started: {s.inst} {s.midi}");
    }

    [Test]
    public void Impact_OnlyFirstBeat()
    {
        var inputs = FeverInputs(32, 0); // fever beats 16..32
        _arr.F = step => ArrangementTimeline.At(step, in inputs);
        var p = Make(lookahead: 16.0, mask: ~PatternTable.CoreLayerMask);
        At(15.5);
        p.Update(15.5); // (15.5, 31.5]: the whole fever phrase
        Assert.AreEqual(3, CountOf("FeverImpact"), "kick, clap and open hat once");
        foreach (var e in _events)
            if (IsNoteOf("FeverImpact", e.note, e.beat))
                Assert.AreEqual(16.0, e.beat, "impact note after fever's first beat");
        Assert.AreEqual(20, CountOf("FeverLead"), "the lead plays the whole fever phrase");
        Assert.Greater(CountOf("FeverOutroFall"), 0);
        foreach (var e in _events)
            if (IsNoteOf("FeverOutroFall", e.note, e.beat) || IsNoteOf("FeverOutroSnare", e.note, e.beat))
                Assert.GreaterOrEqual(e.beat, 28.0, "outro note before fever's last bar");
        Assert.AreEqual(0, CountOf("FeverBuildRoll", 16.0) + CountOf("FeverBuildRiser", 16.0), "build-up notes in fever");
        Assert.AreEqual(2, CountOf("FeverBuildRoll") + CountOf("FeverBuildRiser"), "the build-up bar's last roll and riser notes (15.75)");
    }

    [Test]
    public void FeverQueued_CancelsCoreKickInBuildBar()
    {
        var inputs = FeverInputs(-1, 0);
        _arr.F = step => ArrangementTimeline.At(step, in inputs);
        var p = MakeCore();
        At(10.5);
        p.Update(10.5); // core: (10.5, 14.5], handed to the backend at once
        ulong buildStart = (ulong)(12 * SamplesPerBeat), feverStart = (ulong)(16 * SamplesPerBeat);
        var kicks = new List<(int handle, ulong start)>();
        foreach (var s in _backend.Starts)
            if (s.inst == Instrument.Drums && s.midi == Kick && s.dspStart + 4096 >= buildStart && s.dspStart < feverStart)
                kicks.Add((s.handle, s.dspStart));
        Assert.AreEqual(2, kicks.Count, "the kicks of beats 12 and 14 are with the backend");

        // QueueFever: the bar 12..16 becomes the build-up bar, where the core kick is silent.
        inputs.FeverStart = 32;
        int startsBefore = _backend.Starts.Count;
        _events.Clear();
        p.Invalidate(12, 10.5, 2.0);
        p.Update(10.5);
        foreach (var (handle, start) in kicks)
        {
            Assert.IsTrue(_backend.Stops.Exists(x => x.handle == handle && x.dspStop < start), $"kick {handle} not stopped before its start");
            Assert.Contains(handle, _backend.Releases, $"kick {handle} not released");
        }
        Assert.AreEqual(0, CountDrum(Kick, b => b >= 12 && b < 16), "a kick re-scheduled in the build-up bar");
        for (int i = startsBefore; i < _backend.Starts.Count; i++)
            Assert.IsFalse(_backend.Starts[i].inst == Instrument.Drums && _backend.Starts[i].midi == Kick, "a kick handed over again");
        Assert.Greater(CountDrum(Snare, b => b >= 12 && b < 16), 0, "the core snare of the build-up bar is re-stated");
    }
}
