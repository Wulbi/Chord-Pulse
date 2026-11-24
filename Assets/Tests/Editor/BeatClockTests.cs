using NUnit.Framework;

public class BeatClockTests
{
    [Test]
    public void BeatAt_Double_MatchesUlong()
    {
        var clock = new BeatClock(48000);
        clock.Anchor(0, 0, 120);
        Assert.IsTrue(clock.BeatAt(24000.0, out double a));
        Assert.IsTrue(clock.BeatAt(24000UL, out double b));
        Assert.AreEqual(1.0, a, 1e-9);
        Assert.AreEqual(1.0, b, 1e-9);
        Assert.IsTrue(clock.BeatAt(24000.5, out double c));
        Assert.Greater(c, 1.0);
        Assert.Less(c, 1.0001);
    }

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
    public void SetTempoFrom_LaterBoundary_KeepsUnanchoredEarlierChange()
    {
        // The landing beat (16) never got an anchor, and the next phrase (from 32) is already announced.
        var clock = new BeatClock(48000);
        clock.Anchor(15, 720000, 108);
        clock.SetTempoFrom(16, 116);
        clock.SetTempoFrom(32, 124);
        double at16 = 720000 + 60.0 / 108 * 48000;
        Assert.IsTrue(clock.DspAt(18, out ulong dsp18));
        Assert.AreEqual((ulong)System.Math.Round(at16 + 2 * 60.0 / 116 * 48000), dsp18);
        Assert.IsTrue(clock.DspAt(33, out ulong dsp33));
        Assert.AreEqual((ulong)System.Math.Round(at16 + 16 * 60.0 / 116 * 48000 + 60.0 / 124 * 48000), dsp33, 1.0);
    }

    [Test]
    public void BeatAt_ReturnsFalse_BeforeFirstAnchor()
    {
        var clock = new BeatClock(48000);
        Assert.IsFalse(clock.BeatAt(1000, out _));
    }

    [Test]
    public void BeatAt_RoundTripsDspAt_WithinOneSample()
    {
        var clock = new BeatClock(48000);
        clock.Anchor(8, 480000, 108);
        foreach (double beat in new[] { 7.25, 8.0, 8.3, 10.75, 15.999 })
            AssertRoundTrip(clock, beat);
    }

    [Test]
    public void BeatAt_RoundTripsDspAt_AcrossPendingTempo()
    {
        var clock = new BeatClock(48000);
        clock.Anchor(14, 700000, 116);
        clock.SetTempoFrom(16, 132);
        foreach (double beat in new[] { 14.5, 15.9, 16.0, 16.3, 17.0, 19.6 })
            AssertRoundTrip(clock, beat);
        // Past the boundary the beat runs at 132 BPM: one beat = 21818.18 samples.
        Assert.IsTrue(clock.DspAt(16, out ulong at16));
        Assert.IsTrue(clock.BeatAt(at16 + 21818, out double b));
        Assert.AreEqual(17.0, b, 1.0 / 21818);
    }

    static void AssertRoundTrip(BeatClock clock, double beat)
    {
        Assert.IsTrue(clock.DspAt(beat, out ulong dsp));
        Assert.IsTrue(clock.BeatAt(dsp, out double back));
        Assert.IsTrue(clock.DspAt(back, out ulong again));
        Assert.LessOrEqual(System.Math.Abs((long)again - (long)dsp), 1, "beat " + beat);
        double samplesPerBeat = 60.0 / 132 * clock.SampleRate; // the shortest beat used in these tests
        Assert.AreEqual(beat, back, 1.0 / samplesPerBeat, "beat " + beat);
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
