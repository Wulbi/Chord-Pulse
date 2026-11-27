using System;
using System.Collections.Generic;
using NUnit.Framework;

public class LadderStateTests
{
    // One frame of a run: the beat it was at, what Update returned and the state right after it.
    struct Frame
    {
        public double Beat;
        public LadderState.Step Step;
        public int HeardRung, AppliedRung, LiveRung;
    }

    // Frames from fromBeat (inclusive) to toBeat (exclusive), stepBeats apart; one beat = 1.0, a bar = 4 beats.
    static List<Frame> Run(LadderState state, int combo, double seconds, double fromBeat, double toBeat,
        double stepBeats = 0.05, bool fever = false)
    {
        var frames = new List<Frame>();
        int count = (int)Math.Round((toBeat - fromBeat) / stepBeats);
        for (int i = 0; i < count; i++)
        {
            double beat = Math.Round(fromBeat + i * stepBeats, 9);
            var step = state.Update(combo, seconds, beat, (int)Math.Floor(beat / 4), fever);
            frames.Add(new Frame
            {
                Beat = beat, Step = step,
                HeardRung = state.HeardRung, AppliedRung = state.AppliedRung, LiveRung = state.LiveRung,
            });
        }
        return frames;
    }

    static Frame At(List<Frame> frames, double beat) => frames.Find(f => Math.Abs(f.Beat - beat) < 1e-6);

    // Seconds 40 (floor 1); combo 8 (rung 5) from beat 1, heard from beat 4 on. Runs up to (not including) toBeat.
    static LadderState AtRungFive(double toBeat)
    {
        var state = new LadderState();
        Run(state, 0, 40, 0, 1);
        Run(state, 8, 40, 1, toBeat);
        Assert.AreEqual(5, state.HeardRung, "setup: rung 5 heard");
        return state;
    }

    [Test] public void Rises_OnTheNextBarLine()
    {
        var state = new LadderState();
        Run(state, 0, 0, 0, 1);
        foreach (var f in Run(state, 6, 0, 1, 8))
            Assert.AreEqual(f.Beat < 4 ? 0 : 4, f.HeardRung, "beat " + f.Beat);
    }

    [Test] public void TwoThresholdsInOneBar_RiseTogether()
    {
        var state = new LadderState();
        Run(state, 0, 0, 0, 1);
        Run(state, 2, 0, 1, 2);
        var before = Run(state, 3, 0, 2, 4);
        Assert.AreEqual(0, At(before, 3.95).HeardRung);
        var after = Run(state, 3, 0, 4, 4.1);
        Assert.AreEqual(2, At(after, 4.0).HeardRung);
    }

    [Test] public void Break_FadesForTwoBeats_ThenDropsToTheFloor()
    {
        var state = AtRungFive(10);
        var frames = Run(state, 0, 40, 10, 16);
        var curve = new ComboFade();
        curve.Begin(10, 5, 1);

        var start = At(frames, 10.0);
        Assert.IsTrue(start.Step.FadeStarted);
        Assert.AreEqual(1, start.Step.Floor);
        Assert.AreEqual(1, frames.FindAll(f => f.Step.FadeStarted).Count, "one fade");
        foreach (var f in frames.FindAll(f => f.Beat < 12.0 - 1e-6))
        {
            Assert.AreEqual(5, f.HeardRung, "beat " + f.Beat);
            Assert.AreEqual(curve.Scale(f.Beat), f.Step.Scale, 1e-6, "beat " + f.Beat);
            Assert.IsFalse(f.Step.FadeEnded, "beat " + f.Beat);
        }

        var end = At(frames, 12.0);
        Assert.IsTrue(end.Step.FadeEnded);
        Assert.AreEqual(1, end.Step.Floor);
        foreach (var f in frames.FindAll(f => f.Beat > 12.0 + 1e-6))
        {
            Assert.AreEqual(1, f.HeardRung, "beat " + f.Beat);
            Assert.AreEqual(1, f.AppliedRung, "beat " + f.Beat);
            Assert.AreEqual(1f, f.Step.Scale, "beat " + f.Beat);
            Assert.IsFalse(f.Step.FadeEnded || f.Step.FadeStarted, "beat " + f.Beat);
        }
    }

    [Test] public void Break_AtTheFloor_DoesNothing()
    {
        var state = new LadderState();
        Run(state, 0, 200, 0, 1);
        Run(state, 3, 200, 1, 6);      // combo rung 2 == floor 2
        Assert.AreEqual(2, state.HeardRung, "setup");
        foreach (var f in Run(state, 0, 200, 6, 12))
        {
            Assert.IsFalse(f.Step.FadeStarted, "beat " + f.Beat);
            Assert.AreEqual(2, f.HeardRung, "beat " + f.Beat);
        }
        Assert.IsFalse(state.Fade.Active);
    }

    [Test] public void Fade_RunsAcrossABarLine_WithoutChangingTheHeardRung()
    {
        var state = AtRungFive(11);
        var frames = Run(state, 0, 40, 11, 14);
        Assert.IsTrue(At(frames, 11.0).Step.FadeStarted);
        // The bar line at 12.0 is inside the fade: the arrangement keeps the rung from before the break.
        foreach (var f in frames.FindAll(f => f.Beat > 11.95 && f.Beat < 12.96))
        {
            Assert.AreEqual(5, f.HeardRung, "heard, beat " + f.Beat);
            Assert.AreEqual(5, f.AppliedRung, "applied, beat " + f.Beat);
            Assert.AreEqual(5, f.LiveRung, "live, beat " + f.Beat);
        }
        float previous = 1f;
        foreach (var f in frames.FindAll(f => f.Beat > 11.0 + 1e-6 && f.Beat < 12.96))
        {
            Assert.Less(f.Step.Scale, previous, "scale falls, beat " + f.Beat);
            previous = f.Step.Scale;
        }
    }

    [Test] public void ComboRebuiltDuringTheFade_EntersOnTheBarAfterItEnds()
    {
        var state = AtRungFive(10);
        var frames = Run(state, 0, 40, 10, 11);
        frames.AddRange(Run(state, 2, 40, 11, 11.5));
        frames.AddRange(Run(state, 3, 40, 11.5, 18));
        Assert.AreEqual(1, frames.FindAll(f => f.Step.FadeStarted).Count, "one fade");
        Assert.IsTrue(At(frames, 12.0).Step.FadeEnded);
        foreach (var f in frames.FindAll(f => f.Beat > 11.99 && f.Beat < 16 - 1e-6))
            Assert.AreEqual(1, f.HeardRung, "floor until the next bar line, beat " + f.Beat);
        foreach (var f in frames.FindAll(f => f.Beat > 15.99))
            Assert.AreEqual(Math.Max(1, 2), f.HeardRung, "rebuilt rung, beat " + f.Beat);
    }

    [Test] public void LongStall_SkippingTheWholeFade_StillEndsIt()
    {
        var state = AtRungFive(10);
        var start = state.Update(0, 40, 10.0, 2, false);
        Assert.IsTrue(start.FadeStarted);
        var next = state.Update(0, 40, 14.0, 3, false);
        Assert.IsTrue(next.FadeEnded);
        Assert.AreEqual(1, next.Floor);
        Assert.AreEqual(1, state.HeardRung);
        Assert.IsFalse(state.Fade.Active);
    }

    [Test] public void BreakInsideTheFeverPhrase_StartsNoFade_AndFeverEndSetsTheRung()
    {
        var state = new LadderState();
        Run(state, 0, 40, 0, 1);
        Run(state, 12, 40, 1, 4);
        var frames = Run(state, 12, 40, 4, 10, fever: true);
        frames.AddRange(Run(state, 0, 40, 10, 17, fever: true));
        Assert.IsFalse(frames.Exists(f => f.Step.FadeStarted), "no fade inside the fever phrase");
        Assert.IsFalse(state.Fade.Active);
        // First frame after the fever phrase, mid-bar, with combo 5: rung from that combo and the floor at once.
        var first = Run(state, 5, 40, 17, 17.05);
        Assert.AreEqual(ComboLadder.Rung(5, 40), first[0].HeardRung);
        Assert.AreEqual(ComboLadder.Rung(5, 40), first[0].AppliedRung);
        Assert.IsFalse(first[0].Step.FadeStarted);
    }

    [Test] public void FloorRise_EntersOnTheNextBarLine()
    {
        var state = new LadderState();
        Run(state, 0, 29, 0, 5);
        foreach (var f in Run(state, 0, 31, 5, 10))
            Assert.AreEqual(f.Beat < 8 - 1e-6 ? 0 : 1, f.HeardRung, "beat " + f.Beat);
    }

    [Test] public void Reset_ClearsAFadeInProgress()
    {
        var state = AtRungFive(10);
        Run(state, 0, 40, 10, 11);
        Assert.IsTrue(state.Fade.Active, "setup: fading");
        state.Reset();
        Assert.IsFalse(state.Fade.Active);
        Assert.AreEqual(0, state.HeardRung);
        Assert.AreEqual(1f, state.Update(0, 40, 11.05, 2, false).Scale);
    }
}
