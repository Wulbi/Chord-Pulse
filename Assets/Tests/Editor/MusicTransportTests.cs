using System;
using System.Collections.Generic;
using NUnit.Framework;

public class MusicTransportTests
{
    const int Rate = 48000;

    static ulong Round(double v) => (ulong)Math.Round(v, MidpointRounding.AwayFromZero);

    static ulong PhraseSamples(double bpm) => Round(16 * 60.0 / bpm * Rate);

    [Test]
    public void Start_BeatZeroAtDspStart()
    {
        var clock = new BeatClock(Rate);
        var t = new MusicTransport(clock);
        var started = new List<int>();
        t.PhraseStarted += started.Add;
        t.Start(48000, 120, 120);

        Assert.IsTrue(t.Running);
        Assert.IsTrue(t.BeatAt(48000, out double b0));
        Assert.AreEqual(0.0, b0, 1e-9);
        Assert.IsTrue(t.BeatAt(72000, out double b1));
        Assert.AreEqual(1.0, b1, 1e-9);
        Assert.IsTrue(t.BeatAt(24000, out double bm));
        Assert.AreEqual(-1.0, bm, 1e-9);

        t.Update(47999);
        Assert.AreEqual(-1, t.Phrase);
        Assert.AreEqual(0, t.NextPhraseBeat);
        Assert.AreEqual(0, started.Count);
        t.Update(48000);
        Assert.AreEqual(0, t.Phrase);
        Assert.AreEqual(16, t.NextPhraseBeat);
        CollectionAssert.AreEqual(new[] { 0 }, started);
    }

    [Test]
    public void PhraseBoundary_IsIntegerSample_NoDrift()
    {
        double[] cycle = { 108, 116, 124, 132 };
        var clock = new BeatClock(Rate);
        var t = new MusicTransport(clock);
        const ulong start = 48000;
        var seen = new List<ulong>();
        t.PhraseStarted += n =>
        {
            Assert.IsTrue(clock.DspAt(n * 16, out ulong dsp));
            seen.Add(dsp);
            t.SetNextTempo(cycle[(n + 1) % 4]);
        };
        t.Start(start, cycle[0], cycle[1]);

        ulong expected = start;
        ulong now = start;
        for (int n = 0; n < 80; n++)
        {
            now = expected;
            t.Update(now);
            Assert.AreEqual(n, t.Phrase);
            Assert.AreEqual(expected, seen[n], "phrase " + n);
            expected += PhraseSamples(cycle[n % 4]);
        }
        Assert.AreEqual(80, seen.Count);
    }

    [Test]
    public void SetNextTempo_ReplacesUntilBoundary()
    {
        var clock = new BeatClock(Rate);
        var t = new MusicTransport(clock);
        t.Start(0, 108, 116);
        t.Update(Round(10 * 60.0 / 108 * Rate));
        t.SetNextTempo(108);
        Assert.IsTrue(clock.DspAt(17, out ulong dsp));
        ulong expected = PhraseSamples(108) + Round(60.0 / 108 * Rate);
        Assert.That((double)dsp, Is.EqualTo((double)expected).Within(1));
    }

    [Test]
    public void SetNextTempo_AfterBoundary_AppliesToFollowingPhrase()
    {
        var clock = new BeatClock(Rate);
        var t = new MusicTransport(clock);
        t.Start(0, 120, 108);
        ulong boundary = PhraseSamples(120);
        t.Update(boundary + Round(0.5 * 60.0 / 108 * Rate));
        Assert.AreEqual(1, t.Phrase);
        t.SetNextTempo(132);

        Assert.IsTrue(clock.DspAt(17, out ulong d17));
        Assert.IsTrue(clock.DspAt(31, out ulong d31));
        Assert.That((double)(d31 - d17), Is.EqualTo(Round(14 * 60.0 / 108 * Rate)).Within(1));

        Assert.IsTrue(clock.DspAt(32, out ulong d32));
        Assert.IsTrue(clock.DspAt(33, out ulong d33));
        Assert.That((double)(d33 - d32), Is.EqualTo((double)Round(60.0 / 132 * Rate)).Within(1));
    }

    [Test]
    public void Update_LongGap_RaisesEachPhraseOnce()
    {
        var clock = new BeatClock(Rate);
        var t = new MusicTransport(clock);
        var started = new List<int>();
        t.PhraseStarted += started.Add;
        t.Start(48000, 120, 120);
        t.Update(48000 + 2 * 24000);
        t.Update(48000 + 40 * 24000);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, started);
        Assert.AreEqual(2, t.Phrase);
    }

    [Test]
    public void NoHandler_KeepsTempo()
    {
        var clock = new BeatClock(Rate);
        var t = new MusicTransport(clock);
        t.Start(0, 120, 108);
        t.Update(Round(20 * 60.0 / 120 * Rate)); // beat 20 is in phrase 1 (phrase 0 ends at beat 16)
        Assert.AreEqual(1, t.Phrase);
        Assert.AreEqual(108.0, t.Bpm, 1e-9);

        ulong b1 = PhraseSamples(120);
        ulong b2 = b1 + PhraseSamples(108);
        t.Update(b2);
        Assert.AreEqual(2, t.Phrase);
        Assert.AreEqual(108.0, t.Bpm, 1e-9);
        Assert.IsTrue(clock.DspAt(32, out ulong d32));
        Assert.AreEqual(b2, d32);
        Assert.IsTrue(clock.DspAt(48, out ulong d48));
        Assert.AreEqual(b2 + PhraseSamples(108), d48);
    }

    [Test]
    public void Update_DspWentBackwards_ResyncsAtNextBar()
    {
        var clock = new BeatClock(Rate);
        var t = new MusicTransport(clock);
        int resynced = 0;
        t.Resynced += () => resynced++;
        t.Start(0, 120, 120);
        t.Update(Round(5.3 * 24000));
        Assert.AreEqual(0, resynced);

        Assert.DoesNotThrow(() => t.Update(1000));
        Assert.AreEqual(1, resynced);
        Assert.IsTrue(clock.DspAt(8, out ulong d8));
        Assert.AreEqual(1000UL + 4800UL, d8);

        double prev = double.NegativeInfinity;
        for (ulong d = 1000; d <= 60000; d += 1000)
        {
            t.Update(d);
            Assert.IsTrue(t.BeatAt(d, out double beat));
            Assert.GreaterOrEqual(beat, prev);
            prev = beat;
        }
        Assert.AreEqual(1, resynced);
    }

    [Test]
    public void Stop_InsidePhraseStarted_RaisesNothingMore()
    {
        var clock = new BeatClock(Rate);
        var t = new MusicTransport(clock);
        var started = new List<int>();
        t.PhraseStarted += n =>
        {
            started.Add(n);
            if (n == 1)
                t.Stop();
        };
        t.Start(0, 120, 120);
        t.Update(40 * 24000);
        CollectionAssert.AreEqual(new[] { 0, 1 }, started);
        Assert.AreEqual(-1, t.Phrase);
        Assert.IsFalse(t.Running);
    }

    [Test]
    public void StopThenStart_InsidePhraseStarted_OldCatchUpRaisesNothingMore()
    {
        var clock = new BeatClock(Rate);
        var t = new MusicTransport(clock);
        var started = new List<int>();
        // The new start (480000) lies before dspNow (960000), so without the run counter the old catch-up loop would go on
        // and raise the new run's phrases inside the first Update.
        const ulong newStart = 480000;
        t.PhraseStarted += n =>
        {
            started.Add(n);
            if (n == 1 && started.Count == 2)
            {
                t.Stop();
                t.Start(newStart, 120, 120);
            }
        };
        t.Start(0, 120, 120);
        t.Update(960000);
        CollectionAssert.AreEqual(new[] { 0, 1 }, started);
        Assert.IsTrue(t.Running);
        Assert.AreEqual(-1, t.Phrase);

        // New run at 120 BPM: a phrase is 384000 samples, so phrase 0 starts at 480000, phrase 1 at 864000 and phrase 2
        // at 1248000 (not yet at 960000): [0, 1, 0, 1].
        t.Update(960000);
        Assert.AreEqual(1, t.Phrase);
        CollectionAssert.AreEqual(new[] { 0, 1, 0, 1 }, started);
    }

    // Semantics: a resync that lands on a phrase boundary counts that boundary as passed at once (it is no longer
    // replaceable by SetNextTempo; the new phrase's tempo applies from the resync anchor on).
    [Test]
    public void Update_DspWentBackwards_AtPhraseEnd_UsesNextTempo()
    {
        var clock = new BeatClock(Rate);
        var t = new MusicTransport(clock);
        var started = new List<int>();
        t.PhraseStarted += started.Add;
        t.Start(0, 120, 108);
        t.Update(324000); // beat 13.5
        CollectionAssert.AreEqual(new[] { 0 }, started);

        t.Update(1000);
        Assert.IsTrue(clock.DspAt(16, out ulong d16));
        Assert.IsTrue(clock.DspAt(17, out ulong d17));
        Assert.AreEqual(5800UL, d16);
        Assert.That((double)(d17 - d16), Is.EqualTo(26667.0).Within(1));

        CollectionAssert.AreEqual(new[] { 0, 1 }, started);
        Assert.AreEqual(1, t.Phrase);
        Assert.AreEqual(32, t.NextPhraseBeat);
        Assert.AreEqual(108.0, t.Bpm, 1e-9);

        t.Update(5800);
        t.Update(5800 + 100000);
        CollectionAssert.AreEqual(new[] { 0, 1 }, started);
        Assert.AreEqual(1, t.Phrase);

        // SetNextTempo now concerns phrase 2 only; phrase 1 (tempo 108) is already fixed.
        // Phrase 2 starts at 5800 + round(16*60/108*48000) = 5800 + 426667 = 432467.
        t.SetNextTempo(90);
        Assert.IsTrue(clock.DspAt(16, out d16));
        Assert.IsTrue(clock.DspAt(17, out d17));
        Assert.That((double)(d17 - d16), Is.EqualTo(26667.0).Within(1));
        Assert.IsTrue(clock.DspAt(32, out ulong d32));
        Assert.IsTrue(clock.DspAt(33, out ulong d33));
        Assert.That((double)(d33 - d32), Is.EqualTo(32000.0).Within(1));
        t.Update(432467);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, started);
        Assert.AreEqual(90.0, t.Bpm, 1e-9);
    }

    [Test]
    public void Resynced_HandlerStops_AtPhraseEnd_NoPhraseStartedFollows()
    {
        var clock = new BeatClock(Rate);
        var t = new MusicTransport(clock);
        var started = new List<int>();
        t.PhraseStarted += started.Add;
        t.Start(0, 120, 108);
        t.Update(324000); // beat 13.5
        t.Resynced += t.Stop;
        t.Update(1000);
        CollectionAssert.AreEqual(new[] { 0 }, started);
        Assert.IsFalse(t.Running);
        Assert.AreEqual(-1, t.Phrase);
    }

    [Test]
    public void Stop_ThenStart_ResetsPhraseAndBeat()
    {
        var clock = new BeatClock(Rate);
        var t = new MusicTransport(clock);
        var started = new List<int>();
        t.PhraseStarted += started.Add;
        t.Start(0, 120, 120);
        t.Update(Round(20 * 24000.0));
        Assert.AreEqual(1, t.Phrase);

        t.Stop();
        Assert.IsFalse(t.Running);
        Assert.AreEqual(-1, t.Phrase);
        Assert.IsFalse(t.BeatAt(1000, out _));

        started.Clear();
        t.Start(100000, 120, 120);
        Assert.IsTrue(t.Running);
        Assert.AreEqual(-1, t.Phrase);
        t.Update(100000);
        Assert.AreEqual(0, t.Phrase);
        CollectionAssert.AreEqual(new[] { 0 }, started);
    }
}
