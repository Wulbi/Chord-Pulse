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
        public ulong Now;
        int _next;
        public ulong NowDsp => Now;

        public int Start(Instrument inst, OutputBus bus, int midi, float gainDb, ulong dspStart)
        {
            int handle = _next++;
            Starts.Add((handle, inst, midi, gainDb, dspStart));
            return handle;
        }

        public void Stop(int handle, ulong dspStop) => Stops.Add((handle, dspStop));
        public void Release(int handle) { }
        public bool IsFinished(int handle) => false;
    }

    sealed class FakeArrangement : IArrangement
    {
        public Func<int, ArrangementState> F;
        public ArrangementState At(int step) => F(step);
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
    public void LandingBeats_UseWideDispatchWindow()
    {
        var p = Make();
        At(15.0);
        p.Update(15.0); // beat 16 notes: one beat ahead, handed over at once (landing window)
        ulong boundary = (ulong)(16 * SamplesPerBeat);
        int atBoundary = 0;
        foreach (var s in _backend.Starts)
            if (s.dspStart <= boundary && s.dspStart + 8000 >= boundary)
                atBoundary++;
        Assert.Greater(atBoundary, 0);

        // beat 18 is outside the landing window: its notes wait until half a beat away.
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
    public void Legacy_SchedulesNothing()
    {
        _state.Legacy = true;
        var p = Make(lookahead: 16.0);
        At(0.5);
        p.Update(0.5);
        Assert.AreEqual(0, _events.Count);
        Assert.AreEqual(0, p.ScheduledCount);
    }

    [Test]
    public void Legacy_IsPerStep()
    {
        _arr.F = step => { var s = _state; s.Legacy = step < 4; return s; }; // beats < 2 legacy
        var p = Make(lookahead: 4.0);
        At(0);
        p.Update(0);
        Assert.Greater(_events.Count, 0);
        foreach (var e in _events)
            Assert.GreaterOrEqual(e.beat, 2.0);
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
        Assert.AreEqual(0, _shots.LateCount);
    }

    [Test]
    public void Invalidate_DoesNotDuplicateStartedEarlyOnsetNote()
    {
        _state.Style = 3;
        var p = Make();
        At(15.0);
        p.Update(15.0); // choir chord on beat 16 handed over, its attack starts about 0.15 beat early
        int choirBefore = CountOf("DreamyChoir", 16.0);
        Assert.Greater(choirBefore, 0);
        int startsBefore = _backend.Starts.Count;

        At(15.9); // the early-onset notes have started sounding
        p.Invalidate(16, 15.9, 2.0);
        p.Update(15.9);
        Assert.AreEqual(choirBefore, CountOf("DreamyChoir", 16.0), "no choir note scheduled twice");
        foreach (var s in _backend.Starts.GetRange(startsBefore, _backend.Starts.Count - startsBefore))
            Assert.AreNotEqual(Instrument.Choir, s.inst);
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
}
