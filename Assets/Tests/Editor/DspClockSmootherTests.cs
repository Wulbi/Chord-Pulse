using NUnit.Framework;

public class DspClockSmootherTests
{
    const int Rate = 48000;
    const int Block = 1024;
    const double Dt = 1.0 / 144;

    static ulong Raw(double t) => (ulong)(System.Math.Floor(Rate * t / Block) * Block);

    [Test]
    public void Sample_IsMonotonic()
    {
        var s = new DspClockSmoother(Rate);
        double prev = double.NegativeInfinity;
        for (double t = 0; t < 2.0; t += Dt)
        {
            double v = s.Sample(Raw(t), t);
            Assert.GreaterOrEqual(v, prev, $"t={t}");
            prev = v;
        }
    }

    [Test]
    public void Sample_StaysWithinOneBlockOfTrueClock()
    {
        var s = new DspClockSmoother(Rate);
        for (double t = 0; t < 2.0; t += Dt)
        {
            double v = s.Sample(Raw(t), t);
            if (t > 0.1)
                Assert.LessOrEqual(System.Math.Abs(v - Rate * t), Block, $"t={t}");
        }
    }

    [Test]
    public void Sample_FrameIncrements_AreSmooth()
    {
        var s = new DspClockSmoother(Rate);
        double prev = 0;
        for (double t = 0; t < 2.0; t += Dt)
        {
            double v = s.Sample(Raw(t), t);
            if (t > 0.1)
            {
                double d = v - prev;
                Assert.GreaterOrEqual(d, 0, $"t={t}");
                Assert.LessOrEqual(d, 2 * Dt * Rate, $"t={t}");
            }
            prev = v;
        }
    }

    [Test]
    public void Sample_MixerStall_CapsExtrapolation()
    {
        var s = new DspClockSmoother(Rate);
        double t = 0;
        for (; t < 1.0; t += Dt)
            s.Sample(Raw(t), t);
        ulong frozen = Raw(t);
        for (double end = t + 0.2; t < end; t += Dt)
            Assert.LessOrEqual(s.Sample(frozen, t), frozen + 2048.0, $"t={t}");
    }

    [Test]
    public void Sample_RecoversAfterMixerStall()
    {
        var s = new DspClockSmoother(Rate);
        double t = 0;
        for (; t < 1.0; t += Dt)
            s.Sample(Raw(t), t);
        double stallStart = t;
        ulong frozen = Raw(t);
        for (double end = t + 0.2; t < end; t += Dt)
            s.Sample(frozen, t);
        double stallEnd = t;
        double prev = double.NegativeInfinity;
        int zeroRun = 0;
        for (double end = t + 2.0; t < end; t += Dt)
        {
            // the mixer resumes where it stopped: its clock now trails real time by the stall length
            double v = s.Sample(Raw(stallStart + (t - stallEnd)), t);
            Assert.GreaterOrEqual(v, prev, $"monotonic t={t}");
            if (t - stallEnd >= 0.5)
            {
                double d = v - prev;
                Assert.LessOrEqual(d, 2 * Dt * Rate, $"t={t}");
                zeroRun = d == 0 ? zeroRun + 1 : 0;
                Assert.LessOrEqual(zeroRun, 2, $"staircase t={t}");
                Assert.LessOrEqual(System.Math.Abs(v - Rate * (stallStart + (t - stallEnd))), Block, $"t={t}");
            }
            prev = v;
        }
    }

    [Test]
    public void Sample_SlowAudioClock_DoesNotPinAtCap()
    {
        const double dt = 1.0 / 60;
        const double total = 600;
        var s = new DspClockSmoother(Rate);
        double prev = double.NegativeInfinity;
        int zeroRun = 0;
        for (double t = 0; t < total; t += dt)
        {
            double audio = t * (1 - 1e-4);
            ulong raw = Raw(audio);
            double v = s.Sample(raw, t);
            Assert.GreaterOrEqual(v, prev, $"monotonic t={t}");
            if (t > total - 10)
            {
                Assert.AreNotEqual(raw + 2048.0, v, $"pinned at cap t={t}");
                double d = v - prev;
                Assert.LessOrEqual(d, 2 * dt * Rate, $"t={t}");
                zeroRun = d == 0 ? zeroRun + 1 : 0;
                Assert.LessOrEqual(zeroRun, 2, $"staircase t={t}");
                Assert.LessOrEqual(System.Math.Abs(v - Rate * audio), Block, $"t={t}");
            }
            prev = v;
        }
    }

    [Test]
    public void Sample_RawWentBackwards_Restarts()
    {
        var s = new DspClockSmoother(Rate);
        s.Sample(480000UL, 10.0);
        s.Sample(480000UL, 10.007);
        double v = 0;
        Assert.DoesNotThrow(() => v = s.Sample(1024UL, 10.014));
        Assert.Less(v, 4096);
    }

    [Test]
    public void FollowBeat_LateAnchorJump_IsSpreadOverFrames()
    {
        // The shown beat holds at 96.92 while the clock is behind, then the clock is 0.13 beat ahead in one frame.
        double shown = 96.92, clockLead = 0.13, step = 0.009;
        double prev = shown;
        int frames = 0;
        while (shown < 96.92 + clockLead && frames < 20)
        {
            shown = DspClockSmoother.FollowBeat(shown, 96.92 + clockLead, step);
            Assert.LessOrEqual(shown - prev, step + 0.04 + 1e-9);
            prev = shown;
            frames++;
        }
        Assert.GreaterOrEqual(frames, 3);
        Assert.AreEqual(96.92 + clockLead, shown, 1e-9);
    }

    [Test]
    public void FollowBeat_NeverGoesBack_AndStartsAtTarget()
    {
        Assert.AreEqual(10.0, DspClockSmoother.FollowBeat(10.0, 9.9, 0.01), 1e-12);
        Assert.AreEqual(3.5, DspClockSmoother.FollowBeat(-1, 3.5, 0.01), 1e-12);
        Assert.AreEqual(10.005, DspClockSmoother.FollowBeat(10.0, 10.005, 0.01), 1e-12);
    }
}
