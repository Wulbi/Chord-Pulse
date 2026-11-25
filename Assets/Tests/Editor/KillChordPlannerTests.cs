using System.Collections.Generic;
using NUnit.Framework;

public class KillChordPlannerTests
{
    sealed class FakeBed : IKillBedState
    {
        public int Style;
        public int Combo;
        public float Tension { get; set; }
        public HashSet<int> Fever = new HashSet<int>();
        public bool FeverZone(int beat) => Fever.Contains(beat);
        public int StyleAt(int beat) => Style;
        public int ComboAt(int beat) => Combo;
    }

    // NUnit shares one fixture instance between tests: build fresh state per test.
    FakeBed _bed;
    KillChordPlanner _planner;
    List<OneShotNote> _into;

    [SetUp]
    public void SetUp()
    {
        _bed = new FakeBed();
        _planner = new KillChordPlanner();
        _into = new List<OneShotNote>();
    }

    static List<int> Midis(List<OneShotNote> notes)
    {
        var m = new List<int>();
        foreach (var n in notes) m.Add(n.Midi);
        m.Sort();
        return m;
    }

    [Test]
    public void Queue_TargetsNextBeat_WhenAtLeastLeadAway()
    {
        Assert.AreEqual(8, _planner.Queue(3.5, 0, -1, _bed, _into));
        Assert.AreEqual(4.0, _into[0].Beat);
    }

    [Test]
    public void Queue_SkipsBeat_WhenLessThanLeadAway()
    {
        Assert.AreEqual(10, _planner.Queue(3.7, 0, -1, _bed, _into));
        Assert.AreEqual(5.0, _into[0].Beat);
    }

    [Test]
    public void Queue_JoinsGivenStep()
    {
        Assert.AreEqual(12, _planner.Queue(3.5, 0, 12, _bed, _into));
        Assert.AreEqual(6.0, _into[0].Beat);
    }

    [Test]
    public void Queue_ClosedStep_ReturnsMinusOne()
    {
        Assert.AreEqual(-1, _planner.Queue(5.2, 0, 10, _bed, _into));
        Assert.AreEqual(0, _into.Count);
    }

    [Test]
    public void Queue_SecondKill_AddsOnlyNewVoices()
    {
        // Beat 16 plays chord C.
        _planner.Queue(15.0, 0, -1, _bed, _into);
        Assert.AreEqual(3, _into.Count);
        _into.Clear();
        _planner.Queue(15.1, 0, 32, _bed, _into);
        Assert.AreEqual(new List<int> { 72 }, Midis(_into));
    }

    [Test]
    public void Queue_FourthKill_AddsNothing()
    {
        for (int i = 0; i < 3; i++)
            _planner.Queue(15.0, 0, 32, _bed, _into);
        _into.Clear();
        Assert.AreEqual(32, _planner.Queue(15.0, 0, 32, _bed, _into));
        Assert.AreEqual(0, _into.Count);
    }

    // The notes KillChordVoicer.Voice gives for colours at voicings (style 0, combo 0, tension 0), as OneShot (instrument, midi, gain) keys.
    static List<string> Expected(int chordIndex, params (int color, int voicing)[] chord)
    {
        var keys = new List<string>();
        var notes = new List<KillNote>();
        foreach (var (color, voicing) in chord)
        {
            notes.Clear();
            KillChordVoicer.Voice(0, color, chordIndex, voicing, 0, 0f, notes);
            foreach (var n in notes) keys.Add($"{n.Instrument}/{n.Midi}/{n.LengthBeats}/{n.GainDb}");
        }
        keys.Sort();
        return keys;
    }

    static List<string> Keys(List<OneShotNote> notes)
    {
        var keys = new List<string>();
        foreach (var n in notes) keys.Add($"{n.Instrument}/{n.Midi}/{n.LengthBeats}/{n.GainDb}");
        keys.Sort();
        return keys;
    }

    [Test]
    public void Queue_FeverZone_PlaysStyle0Combo0Tension0()
    {
        _bed.Style = 2;
        _bed.Combo = 3;
        _bed.Tension = 0.9f;
        _bed.Fever.Add(20);
        _planner.Queue(19.0, 0, -1, _bed, _into);
        Assert.AreEqual(3, _into.Count);
        Assert.AreEqual(Expected(1, (0, 1)), Keys(_into));
        foreach (var n in _into)
            Assert.AreEqual(Instrument.OrigRed, n.Instrument);
    }

    [Test]
    public void Restate_FeverQueuedAfterKill_RevoicesAsSynthpop()
    {
        _bed.Style = 2;
        _planner.Queue(19.0, 0, -1, _bed, _into);
        Assert.AreEqual(Instrument.Brass, _into[0].Instrument);
        _into.Clear();

        _bed.Fever.Add(20);
        _planner.Restate(19.0, 20, _bed, _into);
        Assert.AreEqual(3, _into.Count);
        foreach (var n in _into)
        {
            Assert.AreEqual(Instrument.OrigRed, n.Instrument);
            Assert.AreEqual(20.0, n.Beat);
        }
    }

    [Test]
    public void Restate_SkipsBeatsBeforeFromBeat()
    {
        _planner.Queue(15.0, 0, -1, _bed, _into);
        _into.Clear();
        _planner.Restate(15.0, 17, _bed, _into);
        Assert.AreEqual(0, _into.Count);
    }

    [Test]
    public void Restate_SkipsBeatsAlreadyClosedByNow()
    {
        _bed.Style = 2;
        _planner.Queue(19.0, 0, -1, _bed, _into);
        _into.Clear();
        _bed.Fever.Add(20);
        _planner.Restate(20.3, 20, _bed, _into);
        Assert.AreEqual(0, _into.Count);
    }

    [Test]
    public void Restate_LeavesBeatsNearerThanLead()
    {
        _bed.Style = 2;
        _planner.Queue(19.0, 0, -1, _bed, _into);
        _into.Clear();
        _bed.Fever.Add(20);
        int first = _planner.Restate(19.7, 20, _bed, _into);
        Assert.AreEqual(0, _into.Count);
        Assert.Greater(first, 20);
        // half a beat ahead is far enough
        Assert.AreEqual(20, _planner.Restate(19.5, 20, _bed, _into));
        Assert.AreEqual(3, _into.Count);
    }

    [Test]
    public void Restate_MultiVoicing_ReturnsFullChord()
    {
        _bed.Style = 2;
        _bed.Combo = 3;
        _bed.Tension = 0.9f;
        for (int i = 0; i < 2; i++) _planner.Queue(23.0, 0, 48, _bed, _into);
        for (int i = 0; i < 3; i++) _planner.Queue(23.0, 1, 48, _bed, _into);
        _into.Clear();
        _bed.Fever.Add(24);
        Assert.AreEqual(24, _planner.Restate(23.0, 24, _bed, _into));
        Assert.AreEqual(Expected(2, (0, 2), (1, 3)), Keys(_into));
        foreach (var n in _into) Assert.AreEqual(24.0, n.Beat);
    }

    [Test]
    public void QueueAfterRestate_AddsOnlyFeverVoices()
    {
        _bed.Style = 2;
        _planner.Queue(19.0, 0, -1, _bed, _into);
        _into.Clear();
        _bed.Fever.Add(20);
        _planner.Restate(19.0, 20, _bed, _into);
        _into.Clear();
        Assert.AreEqual(40, _planner.Queue(19.1, 0, 40, _bed, _into));
        Assert.AreEqual(1, _into.Count);
        Assert.AreEqual(Instrument.OrigRed, _into[0].Instrument);
        Assert.AreEqual(69, _into[0].Midi); // Am octave voice, chord index 1
    }

    [Test]
    public void DispatchWindow_PhraseLanding()
    {
        // No landing special case: every beat, boundary or not, uses half a beat.
        foreach (int beat in new[] { 0, 1, 2, 15, 16, 17, 18, 31, 32, 33, 64, 65, 127 })
            Assert.AreEqual(0.5, KillChordPlanner.DispatchWindow(beat), "beat " + beat);
    }
}
