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
