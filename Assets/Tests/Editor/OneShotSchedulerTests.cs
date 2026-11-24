using System.Collections.Generic;
using NUnit.Framework;

public class OneShotSchedulerTests
{
    sealed class FakeBackend : IOneShotBackend
    {
        public readonly List<(int handle, Instrument inst, int midi, float gainDb, ulong dspStart)> Starts =
            new List<(int, Instrument, int, float, ulong)>();
        public readonly List<(int handle, ulong dspStop)> Stops = new List<(int, ulong)>();
        public readonly List<int> Releases = new List<int>();
        public ulong Now;
        int _next;

        public ulong NowDsp => Now;

        public int Start(Instrument inst, int midi, float gainDb, ulong dspStart)
        {
            int handle = _next++;
            Starts.Add((handle, inst, midi, gainDb, dspStart));
            return handle;
        }

        public void Stop(int handle, ulong dspStop) => Stops.Add((handle, dspStop));
        public void Release(int handle) => Releases.Add(handle);
    }

    const int Rate = 48000;
    static readonly Instrument Inst = (Instrument)0;

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
        Assert.IsTrue(scheduler.Schedule(Inst, 72, 2, 0.5, -3f, 0.01));
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
        Assert.IsTrue(scheduler.Schedule(Inst, 72, 2, 0.5, 0f, 0.0));
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
    public void Reschedules_WhenTempoChangesAfterBoundary()
    {
        var (clock, backend, scheduler) = Make();
        clock.Anchor(0, 0, 120);
        backend.Now = 374400; // beat 15.6: beat 16 (384000) is inside the window
        Assert.IsTrue(scheduler.Schedule(Inst, 72, 16, 1, 0f, 0.0));
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
        scheduler.Schedule(Inst, 72, 15.8, 0.1, 0f, 0.0);
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
        scheduler.Schedule(Inst, 60, 4, 1, 0f, 0.0);
        scheduler.Schedule(Inst, 64, 5, 1, 0f, 0.0);
        scheduler.Schedule(Inst, 67, 6, 1, 0f, 0.0);
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
        scheduler.Schedule(Inst, 72, 2, 4, 0f, 0.0);
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
        scheduler.Schedule(Inst, 72, 2, 0.78, 0f, 0.0);
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
        scheduler.Schedule(Inst, 72, 2, 0.5, 0f, 0.0);
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
        Assert.IsFalse(scheduler.Schedule(Inst, 72, 2, 0.5, 0f, 0.0));
        Assert.AreEqual(1, scheduler.LateCount);
        Assert.AreEqual(0, backend.Starts.Count);
    }

    [Test]
    public void Schedule_WithoutAnchor_ReturnsFalse()
    {
        var (_, backend, scheduler) = Make();
        Assert.IsFalse(scheduler.Schedule(Inst, 72, 2, 0.5, 0f, 0.0));
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
        Assert.IsTrue(scheduler.Schedule(Inst, 72, beat, 0.5, 0f, 0.0));
        Assert.AreEqual(1, backend.Starts.Count);
        Assert.AreEqual(expected, backend.Starts[0].dspStart);
    }
}
