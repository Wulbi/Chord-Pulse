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

    [Test]
    public void Queue_FeverZone_PlaysStyle0Combo0Tension0()
    {
        _bed.Style = 2;
        _bed.Combo = 3;
        _bed.Tension = 0.9f;
        _bed.Fever.Add(20);
        _planner.Queue(19.0, 0, -1, _bed, _into);
        Assert.AreEqual(3, _into.Count);
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
        _planner.Restate(20, _bed, _into);
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
        _planner.Restate(17, _bed, _into);
        Assert.AreEqual(0, _into.Count);
    }

    [Test]
    public void DispatchWindow_PhraseLanding()
    {
        Assert.AreEqual(1.0, KillChordPlanner.DispatchWindow(16));
        Assert.AreEqual(1.0, KillChordPlanner.DispatchWindow(17));
        Assert.AreEqual(0.5, KillChordPlanner.DispatchWindow(18));
    }
}
