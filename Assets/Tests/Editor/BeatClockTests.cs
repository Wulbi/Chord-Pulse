using NUnit.Framework;

public class BeatClockTests
{
    [Test]
    public void DspAt_ReturnsFalse_BeforeFirstAnchor()
    {
        var clock = new BeatClock(48000);
        Assert.IsFalse(clock.DspAt(0, out _));
    }

    [Test]
    public void DspAt_ExtrapolatesFromAnchor()
    {
        var clock = new BeatClock(48000);
        clock.Anchor(8, 480000, 120);
        Assert.IsTrue(clock.DspAt(10, out ulong dsp));
        Assert.AreEqual(480000UL + 48000UL, dsp);
    }

    [Test]
    public void DspAt_UsesLatestAnchorTempo()
    {
        var clock = new BeatClock(48000);
        clock.Anchor(0, 0, 108);
        clock.Anchor(4, 106667, 132);
        Assert.IsTrue(clock.DspAt(5, out ulong dsp));
        Assert.AreEqual(106667UL + 21818UL, dsp);
    }

    [Test]
    public void SetTempoFrom_UsesPendingTempoFromThatBeat()
    {
        var clock = new BeatClock(48000);
        clock.Anchor(0, 0, 116);
        clock.SetTempoFrom(16, 132);
        Assert.IsTrue(clock.DspAt(16.3, out ulong dsp));
        double expected = 16 * 60.0 / 116 * 48000 + 0.3 * 60.0 / 132 * 48000; // 397241.38 + 6545.45
        Assert.AreEqual((ulong)System.Math.Round(expected), dsp);
        Assert.IsTrue(clock.DspAt(15.5, out ulong before));
        Assert.AreEqual((ulong)System.Math.Round(15.5 * 60.0 / 116 * 48000), before);
    }

    [Test]
    public void SetTempoFrom_ClearedByAnchorAtOrAfterItsBeat()
    {
        var clock = new BeatClock(48000);
        clock.Anchor(0, 0, 116);
        clock.SetTempoFrom(16, 132);
        clock.Anchor(16, 400000, 140); // reality differs (e.g. another section)
        Assert.IsTrue(clock.DspAt(17, out ulong dsp));
        Assert.AreEqual(400000UL + (ulong)System.Math.Round(60.0 / 140 * 48000), dsp);
    }

    [Test]
    public void SetTempoFrom_IgnoredAtOrBeforeAnchor_ClearedByReset()
    {
        var clock = new BeatClock(48000);
        clock.Anchor(16, 400000, 116);
        clock.SetTempoFrom(16, 132);
        Assert.IsTrue(clock.DspAt(17, out ulong dsp));
        Assert.AreEqual(400000UL + (ulong)System.Math.Round(60.0 / 116 * 48000), dsp);

        clock.SetTempoFrom(32, 132);
        clock.Reset();
        clock.Anchor(0, 0, 116);
        Assert.IsTrue(clock.DspAt(33, out ulong after));
        Assert.AreEqual((ulong)System.Math.Round(33 * 60.0 / 116 * 48000), after);
    }

    [Test]
    public void ResetSampleRate_KeepsInstance_DropsAnchor_UsesNewRate()
    {
        var clock = new BeatClock(48000);
        var held = clock; // e.g. a scheduler holding the conductor's clock
        clock.Anchor(0, 0, 120);
        clock.Reset(44100);
        Assert.AreSame(held, clock);
        Assert.AreEqual(44100, held.SampleRate);
        Assert.IsFalse(held.DspAt(1, out _));
        held.Anchor(8, 441000, 120);
        Assert.IsTrue(held.DspAt(10, out ulong dsp));
        Assert.AreEqual(441000UL + 44100UL, dsp);
    }
}
