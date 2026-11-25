using System.Collections.Generic;
using NUnit.Framework;

public class OneShotSchedulerTests
{
    sealed class FakeBackend : IOneShotBackend
    {
        public readonly List<(int handle, Instrument inst, OutputBus bus, int midi, float gainDb, ulong dspStart)> Starts =
            new List<(int, Instrument, OutputBus, int, float, ulong)>();
        public readonly HashSet<int> Finished = new HashSet<int>();
        public readonly List<(int handle, ulong dspStop)> Stops = new List<(int, ulong)>();
        public readonly List<int> Releases = new List<int>();
        public ulong Now;
        /// <summary>Added to Now after every NowDsp read (simulates the mixer clock running between reads).</summary>
        public ulong AdvancePerRead;
        int _next;

        public ulong NowDsp
        {
            get
            {
                ulong now = Now;
                Now += AdvancePerRead;
                return now;
            }
        }

        public int Start(Instrument inst, OutputBus bus, int midi, float gainDb, ulong dspStart)
        {
            int handle = _next++;
            Starts.Add((handle, inst, bus, midi, gainDb, dspStart));
            return handle;
        }

        public void Stop(int handle, ulong dspStop) => Stops.Add((handle, dspStop));
        public void Release(int handle) => Releases.Add(handle);
        public bool IsFinished(int handle) => Finished.Contains(handle);
    }

    const int Rate = 48000;
    static readonly Instrument Inst = (Instrument)0;

    static readonly object Owner = new object();

    static OneShotNote N(int midi, double beat, double length, float gain, double onset, double hold = 0, OutputBus bus = OutputBus.KillChords) =>
        new OneShotNote(Inst, bus, midi, beat, length, hold, gain, onset);

    static (BeatClock clock, FakeBackend backend, OneShotScheduler scheduler) Make()
    {
        var clock = new BeatClock(Rate);
        var backend = new FakeBackend();
        var scheduler = new OneShotScheduler(clock, backend) { ScheduleOffsetSamples = 0 };
        return (clock, backend, scheduler);
    }

    [Test]
    public void StartsAtClockDspMinusOnset()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 40000; // beat 2 = 48000, inside the half-beat window (12000 samples)
        Assert.IsTrue(scheduler.Schedule(N(72, 2, 0.5, -3f, 0.01), Owner));
        Assert.AreEqual(1, backend.Starts.Count);
        Assert.AreEqual(48000UL - 480UL, backend.Starts[0].dspStart);
        Assert.AreEqual(72, backend.Starts[0].midi);
        Assert.AreEqual(-3f, backend.Starts[0].gainDb);
    }

    [Test]
    public void Schedule_OutsideHalfBeat_WaitsForUpdate()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 0;
        Assert.IsTrue(scheduler.Schedule(N(72, 2, 0.5, 0f, 0.0), Owner));
        Assert.AreEqual(0, backend.Starts.Count);
        backend.Now = 35999; // 12001 samples ahead: still outside
        scheduler.Update();
        Assert.AreEqual(0, backend.Starts.Count);
        backend.Now = 36000; // exactly half a beat ahead
        scheduler.Update();
        Assert.AreEqual(1, backend.Starts.Count);
        Assert.AreEqual(48000UL, backend.Starts[0].dspStart);
    }

    [Test]
    public void Schedule_WiderWindow_DispatchesUpToThatManyBeatsEarly()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 0;
        Assert.IsTrue(scheduler.Schedule(N(72, 2, 0.5, 0f, 0.0), Owner, 1.0));
        Assert.AreEqual(0, backend.Starts.Count);
        backend.Now = 23999; // 24001 samples ahead: outside a 1-beat window
        scheduler.Update();
        Assert.AreEqual(0, backend.Starts.Count);
        backend.Now = 24000; // exactly one beat ahead
        scheduler.Update();
        Assert.AreEqual(1, backend.Starts.Count);
        Assert.AreEqual(48000UL, backend.Starts[0].dspStart);
    }

    [Test]
    public void Reschedules_WhenTempoChangesAfterBoundary()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 374400; // beat 15.6: beat 16 (384000) is inside the window
        Assert.IsTrue(scheduler.Schedule(N(72, 16, 1, 0f, 0.0), Owner));
        Assert.AreEqual(1, backend.Starts.Count);
        Assert.AreEqual(384000UL, backend.Starts[0].dspStart);
        int oldHandle = backend.Starts[0].handle;

        clock.Anchor(16, 388000, 132);
        scheduler.RescheduleFrom(16);
        backend.Now = 380000;
        scheduler.Update();

        Assert.Contains((oldHandle, 374400UL), backend.Stops, "old instance cancelled at once");
        Assert.Contains(oldHandle, backend.Releases);
        Assert.AreEqual(2, backend.Starts.Count);
        Assert.AreEqual(388000UL, backend.Starts[1].dspStart);
        ulong expectedStop = 388000UL + (ulong)System.Math.Round(60.0 / 132 * Rate);
        Assert.Contains((backend.Starts[1].handle, expectedStop), backend.Stops);
    }

    [Test]
    public void RescheduleFrom_LeavesEarlierNotesAlone()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 374400;
        scheduler.Schedule(N(72, 15.8, 0.1, 0f, 0.0), Owner);
        clock.Anchor(16, 388000, 132);
        scheduler.RescheduleFrom(16);
        scheduler.Update();
        Assert.AreEqual(1, backend.Starts.Count);
        Assert.AreEqual(0, backend.Releases.Count);
    }

    [Test]
    public void StopAll_ReleasesPending()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 0;
        scheduler.Schedule(N(60, 4, 1, 0f, 0.0), Owner);
        scheduler.Schedule(N(64, 5, 1, 0f, 0.0), Owner);
        scheduler.Schedule(N(67, 6, 1, 0f, 0.0), Owner);
        scheduler.StopAll();
        backend.Now = 140000; // all three would be inside the window now
        scheduler.Update();
        Assert.AreEqual(0, backend.Starts.Count);
    }

    [Test]
    public void StopAll_StopsAndReleasesSentNotes()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 40000;
        scheduler.Schedule(N(72, 2, 4, 0f, 0.0), Owner);
        int handle = backend.Starts[0].handle;
        backend.Now = 50000; // sounding
        scheduler.StopAll();
        Assert.Contains((handle, 50000UL), backend.Stops);
        Assert.Contains(handle, backend.Releases);
    }

    [Test]
    public void StopsAfterLength()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 40000;
        scheduler.Schedule(N(72, 2, 0.78, 0f, 0.0), Owner);
        Assert.AreEqual(1, backend.Starts.Count);
        ulong start = backend.Starts[0].dspStart;
        ulong expected = start + (ulong)System.Math.Round(0.78 * 60.0 / 120 * Rate);
        Assert.AreEqual(1, backend.Stops.Count);
        Assert.AreEqual(expected, backend.Stops[0].dspStop);

        // The handle is released once the stop time has passed.
        backend.Now = expected - 1;
        scheduler.Update();
        Assert.AreEqual(0, backend.Releases.Count);
        backend.Now = expected;
        scheduler.Update();
        Assert.AreEqual(1, backend.Releases.Count);
    }

    [Test]
    public void Update_NeverStartsPastNote_CountsLate()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 0;
        scheduler.Schedule(N(72, 2, 0.5, 0f, 0.0), Owner);
        backend.Now = 48000; // a hitch: Update was not called inside the window
        scheduler.Update();
        Assert.AreEqual(0, backend.Starts.Count);
        Assert.AreEqual(1, scheduler.LateCount);
        backend.Now = 30000;
        scheduler.Update(); // dropped for good
        Assert.AreEqual(0, backend.Starts.Count);
    }

    [Test]
    public void Schedule_PastStart_ReturnsFalse_CountsLate()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 48000;
        Assert.IsFalse(scheduler.Schedule(N(72, 2, 0.5, 0f, 0.0), Owner));
        Assert.AreEqual(1, scheduler.LateCount);
        Assert.AreEqual(0, backend.Starts.Count);
    }

    [Test]
    public void Schedule_LateOnSecondNowRead_ReturnsFalse_CountsLateOnce()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 47999;        // first read: beat 2 (48000) is still ahead
        backend.AdvancePerRead = 1; // second read: 48000, the start has passed
        Assert.IsFalse(scheduler.Schedule(N(72, 2, 0.5, 0f, 0.0), Owner));
        Assert.AreEqual(1, scheduler.LateCount);
        Assert.AreEqual(0, scheduler.ActiveCount, "late note not kept");
        backend.AdvancePerRead = 0;
        scheduler.Update();
        Assert.AreEqual(1, scheduler.LateCount, "not counted again by Update");
        Assert.AreEqual(0, backend.Starts.Count);
    }

    [Test]
    public void PhraseLanding_OnsetShiftUsesPredictedAnchor()
    {
        // R19: a kill on a phrase downbeat (jump landing, beat 16) starts its sample early, before the boundary;
        // the landing's DSP time comes from the predicted anchor (old tempo up to beat 16, pending tempo after it).
        var (clock, backend, scheduler) = Make();
        clock.Anchor(15, 15 * 24000, 120);
        clock.SetTempoFrom(16, 132);
        backend.Now = 16 * 24000 - 6000;
        Assert.IsTrue(scheduler.Schedule(N(72, 16, 0.5, 0f, 0.045), Owner));
        Assert.AreEqual(1, backend.Starts.Count);
        Assert.AreEqual(16UL * 24000 - 2160, backend.Starts[0].dspStart, "landing minus 45 ms");
        ulong expectedStop = 16UL * 24000 + (ulong)System.Math.Round(0.5 * 60.0 / 132 * Rate);
        Assert.Contains((backend.Starts[0].handle, expectedStop), backend.Stops);
    }

    [Test]
    public void Schedule_WithoutAnchor_ReturnsFalse()
    {
        var (_, backend, scheduler) = Make();
        Assert.IsFalse(scheduler.Schedule(N(72, 2, 0.5, 0f, 0.0), Owner));
        scheduler.Update();
        Assert.AreEqual(0, backend.Starts.Count);
    }

    [Test]
    public void ShortLeadAfterBoundary_UsesPendingTempo()
    {
        // R11: the landing beat's anchor has not arrived; the next section's BPM is known.
        var (clock, backend, scheduler) = Make();
        clock.Anchor(15, 15 * 24000, 120);
        clock.SetTempoFrom(16, 132);
        double beat = 17;
        ulong expected = 16 * 24000 + (ulong)System.Math.Round(60.0 / 132 * Rate);
        backend.Now = expected - 6545; // 0.3 beat at 132 BPM ahead
        Assert.IsTrue(scheduler.Schedule(N(72, beat, 0.5, 0f, 0.0), Owner));
        Assert.AreEqual(1, backend.Starts.Count);
        Assert.AreEqual(expected, backend.Starts[0].dspStart);
    }

    [Test]
    public void CancelFrom_CancelsOwnNotesOnly()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        var a = new object();
        var b = new object();
        backend.Now = 0;
        scheduler.Schedule(N(60, 4, 1, 0f, 0), a);
        scheduler.Schedule(N(62, 8, 1, 0f, 0), a);
        scheduler.Schedule(N(64, 8, 1, 0f, 0), b);
        Assert.AreEqual(1, scheduler.CancelFrom(6, a));
        backend.Now = 180000; // beat 8 (192000) inside the window
        scheduler.Update();
        Assert.AreEqual(1, backend.Starts.Count);
        Assert.AreEqual(64, backend.Starts[0].midi);
    }

    [Test]
    public void CancelFrom_StopsDispatchedNotStarted()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 40000;
        scheduler.Schedule(N(72, 2, 1, 0f, 0), Owner);
        int handle = backend.Starts[0].handle;
        backend.Now = 41000;
        Assert.AreEqual(1, scheduler.CancelFrom(0, Owner));
        Assert.Contains((handle, 41000UL), backend.Stops);
        Assert.Contains(handle, backend.Releases);
        backend.Now = 47000;
        scheduler.Update();
        Assert.AreEqual(1, backend.Starts.Count, "not dispatched again");
        Assert.AreEqual(0, scheduler.ActiveCount);
    }

    [Test]
    public void CancelFrom_LeavesSoundingNotes()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 40000;
        scheduler.Schedule(N(72, 2, 4, 0f, 0), Owner);
        int stops = backend.Stops.Count;
        backend.Now = 50000; // start (48000) has passed
        Assert.AreEqual(0, scheduler.CancelFrom(0, Owner));
        Assert.AreEqual(stops, backend.Stops.Count);
        Assert.AreEqual(0, backend.Releases.Count);
    }

    [Test]
    public void ZeroLength_NeverStopsAtStart()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 40000;
        scheduler.Schedule(N(72, 2, 0, 0f, 0), Owner);
        Assert.AreEqual(1, backend.Starts.Count);
        Assert.AreEqual(0, backend.Stops.Count);
        backend.Now = 400000;
        scheduler.Update();
        Assert.AreEqual(0, backend.Releases.Count, "not released by a stop time");
        Assert.AreEqual(1, scheduler.ActiveCount);
        backend.Finished.Add(backend.Starts[0].handle);
        scheduler.Update();
        Assert.AreEqual(1, backend.Releases.Count);
        Assert.AreEqual(0, scheduler.ActiveCount);
    }

    [Test]
    public void HoldSeconds_ExtendsShortNote()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 40000;
        scheduler.Schedule(N(72, 2, 0.25, 0f, 0, hold: 0.6), Owner);
        Assert.AreEqual(1, backend.Stops.Count);
        Assert.AreEqual(48000UL + 28800UL, backend.Stops[0].dspStop);
    }

    [Test]
    public void StopAll_StopsZeroLengthNotes()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 40000;
        scheduler.Schedule(N(72, 2, 0, 0f, 0), Owner);
        int handle = backend.Starts[0].handle;
        backend.Now = 50000;
        scheduler.StopAll();
        Assert.Contains((handle, 50000UL), backend.Stops);
        Assert.Contains(handle, backend.Releases);
    }

    [Test]
    public void Start_PassesBus()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 40000;
        scheduler.Schedule(N(72, 2, 1, 0f, 0, bus: OutputBus.KillChords), Owner);
        Assert.AreEqual(OutputBus.KillChords, backend.Starts[0].bus);
    }

    [Test]
    public void Release_IsLookedUpPerNote_NotCachedFromFirst()
    {
        var a = (Instrument)1;
        var b = (Instrument)2;
        var release = new OneShotRelease((inst, bus) => inst == a ? 0.1 : 0.3);
        Assert.AreEqual(4800UL, release.SamplesFor(a, OutputBus.KillChords, 48000));
        Assert.AreEqual(14400UL, release.SamplesFor(b, OutputBus.KillChords, 48000));
        Assert.AreEqual(4800UL, release.SamplesFor(a, OutputBus.KillChords, 48000));
        Assert.AreEqual(0UL, new OneShotRelease((_, __) => 0.0).SamplesFor(a, OutputBus.KillChords, 48000));
        Assert.AreEqual(4800UL, release.SamplesFor(a, OutputBus.KillChords, 0), "unknown rate falls back to 48 kHz");
    }

    [Test]
    public void ZeroLength_Late_IsDroppedAndCounted()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 48000;
        Assert.IsFalse(scheduler.Schedule(N(72, 2, 0, 0f, 0), Owner));
        Assert.AreEqual(1, scheduler.LateCount);
        Assert.AreEqual(0, backend.Starts.Count);
        backend.Now = 0;
        scheduler.Schedule(N(72, 2, 0, 0f, 0), Owner);
        backend.Now = 50000; // hitch: missed the window
        scheduler.Update();
        Assert.AreEqual(2, scheduler.LateCount);
        Assert.AreEqual(0, backend.Starts.Count);
        Assert.AreEqual(0, scheduler.ActiveCount);
    }

    [Test]
    public void CancelFrom_AtExactBeat_Cancels()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 0;
        scheduler.Schedule(N(60, 6, 1, 0f, 0), Owner);
        Assert.AreEqual(1, scheduler.CancelFrom(6, Owner));
        Assert.AreEqual(0, scheduler.ActiveCount);
    }

    [Test]
    public void HoldSeconds_ShorterThanLength_DoesNotShorten()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 40000;
        scheduler.Schedule(N(72, 2, 2, 0f, 0, hold: 0.1), Owner); // 2 beats = 1 s
        Assert.AreEqual(48000UL + 48000UL, backend.Stops[0].dspStop);
    }

    // FmodOneShotBackend's wait for the channel group (OneShotArming): time-based, never frame-based.

    [Test]
    public void Arming_NoGroupStartAhead_WaitsHoweverManyFrames()
    {
        // 100000 frames of 1 sample each (an uncapped frame rate) while the start is still ahead: always Wait.
        for (ulong now = 0; now < 100000; now++)
            Assert.AreEqual(OneShotArm.Wait, OneShotArming.Decide(true, false, 200000, now));
    }

    [Test]
    public void Arming_GroupAndStartAhead_Applies()
    {
        Assert.AreEqual(OneShotArm.Apply, OneShotArming.Decide(true, true, 48000, 47999));
    }

    [Test]
    public void Arming_StartReachedWithoutGroup_Drops()
    {
        Assert.AreEqual(OneShotArm.Drop, OneShotArming.Decide(true, false, 48000, 48000));
        Assert.AreEqual(OneShotArm.Drop, OneShotArming.Decide(true, false, 48000, 60000));
    }

    [Test]
    public void Arming_GroupOnlyAfterStart_Drops()
    {
        // Applying a start that has passed would play the note late.
        Assert.AreEqual(OneShotArm.Drop, OneShotArming.Decide(true, true, 48000, 48000));
        Assert.AreEqual(OneShotArm.Drop, OneShotArming.Decide(true, true, 48000, 48001));
    }

    [Test]
    public void Arming_StartInsideTheBlockBeingMixed_Drops()
    {
        // 1024-sample mixer block: a start less than one block ahead may already be in the block the mixer is
        // processing, where setDelay can come too late (Task 5b, 15 fps take: applied 798 samples ahead, never sounded).
        Assert.AreEqual(OneShotArm.Drop, OneShotArming.Decide(true, true, 48000, 48000 - 1023, 1024));
        Assert.AreEqual(OneShotArm.Drop, OneShotArming.Decide(true, false, 48000, 48000 - 1023, 1024));
        Assert.AreEqual(OneShotArm.Apply, OneShotArming.Decide(true, true, 48000, 48000 - 1024, 1024));
        Assert.AreEqual(OneShotArm.Wait, OneShotArming.Decide(true, false, 48000, 48000 - 1024, 1024));
    }

    [Test]
    public void Arming_InvalidInstance_Drops()
    {
        Assert.AreEqual(OneShotArm.Drop, OneShotArming.Decide(false, false, 200000, 0));
        Assert.AreEqual(OneShotArm.Drop, OneShotArming.Decide(false, true, 200000, 0));
    }

    // Task 12 follow-up: a note handed over beats ahead (core drums: 4 beats) keeps the start of the clock it was dispatched
    // with; at a jump landing that clock was extrapolated from a drifting pre-landing anchor (+12..+16 ms measured). Update
    // re-times such notes when the clock moves their start by more than RetimeThresholdSamples.
    [Test]
    public void Update_ClockMovedDispatchedNote_RetimesIt()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 0;
        Assert.IsTrue(scheduler.Schedule(N(72, 4, 0, 0f, 0.0), Owner, 4.0)); // handed over at once
        Assert.AreEqual(1, backend.Starts.Count);
        Assert.AreEqual(96000UL, backend.Starts[0].dspStart);

        clock.Anchor(1, 24000 - 600, 120); // the clock learns beat 1 was 600 samples (12.5 ms) earlier
        backend.Now = 24000;
        scheduler.Update();
        Assert.AreEqual(2, backend.Starts.Count, "re-dispatched with the new time");
        Assert.AreEqual(96000UL - 600UL, backend.Starts[1].dspStart);
        Assert.IsTrue(backend.Stops.Exists(s => s.handle == backend.Starts[0].handle), "the old instance is stopped");
        Assert.AreEqual(1, scheduler.ActiveCount);
    }

    [Test]
    public void Update_SmallClockMove_KeepsTheDispatchedNote()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 0;
        Assert.IsTrue(scheduler.Schedule(N(72, 4, 0, 0f, 0.0), Owner, 4.0));
        clock.Anchor(1, 24000 - (ulong)(OneShotScheduler.RetimeThresholdSamples / 2), 120);
        backend.Now = 24000;
        scheduler.Update();
        Assert.AreEqual(1, backend.Starts.Count);
        Assert.AreEqual(0, backend.Stops.Count);
    }

    [Test]
    public void Update_NoteAboutToStart_IsNotRetimed()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 0;
        Assert.IsTrue(scheduler.Schedule(N(72, 1, 0, 0f, 0.0), Owner, 4.0));
        clock.Anchor(0, 600, 120);
        backend.Now = 24000 - (ulong)OneShotScheduler.RetimeMinLeadSamples + 1; // nearer than the re-time lead
        scheduler.Update();
        Assert.AreEqual(1, backend.Starts.Count);
    }
}
