using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// SoundManager's music path without FMOD: MusicTransport anchors the shared BeatClock, a PatternScheduler that is
/// updated from the start lead on (before beat 0) hands its notes to a OneShotScheduler with the default schedule offset.
/// Every start must be the clock beat's exact sample (integer phrase boundaries, independent arithmetic) minus the note's
/// onset; beat 0 is taken.
/// </summary>
public class TransportSchedulingTests
{
    sealed class FakeBackend : IOneShotBackend
    {
        public readonly List<(Instrument inst, int midi, ulong dspStart)> Starts = new List<(Instrument, int, ulong)>();
        public ulong Now;
        int _next;
        public ulong NowDsp => Now;

        public int Start(Instrument inst, OutputBus bus, int midi, float gainDb, ulong dspStart)
        {
            Starts.Add((inst, midi, dspStart));
            return _next++;
        }

        public void Stop(int handle, ulong dspStop) { }
        public void Release(int handle) { }
        public bool IsFinished(int handle) => false;
    }

    sealed class FixedArrangement : IArrangement
    {
        public ArrangementState State;
        public ArrangementState At(int step) => State;
    }

    const int Rate = 48000;
    const ulong StartDsp = 480000;
    const int StartLeadSamples = 12000; // 0.25 s, SoundManager's start lead

    // Independent arithmetic: boundary[n+1] = boundary[n] + round(16 * 60 / bpm[n] * rate); a beat inside phrase n is
    // round(boundary[n] + (beat - 16 n) * 60 / bpm[n] * rate).
    static ulong Expected(double beat, double[] bpm)
    {
        ulong boundary = StartDsp;
        int phrase = 0;
        while (beat >= (phrase + 1) * 16)
        {
            boundary += (ulong)Math.Round(16 * 60.0 / bpm[phrase] * Rate, MidpointRounding.AwayFromZero);
            phrase++;
        }
        return (ulong)Math.Round(boundary + (beat - phrase * 16) * 60.0 / bpm[phrase] * Rate, MidpointRounding.AwayFromZero);
    }

    [Test]
    public void DefaultScheduleOffset_IsZero()
    {
        Assert.AreEqual(0, OneShotScheduler.DefaultScheduleOffsetSamples);
    }

    [Test]
    public void FromStartLead_EveryStartIsTheClockBeatMinusOnset_BeatZeroIncluded()
    {
        double[] bpm = { 108, 132, 116 };
        var clock = new BeatClock(Rate);
        var transport = new MusicTransport(clock);
        var backend = new FakeBackend();
        var shots = new OneShotScheduler(clock, backend); // default offset
        var arrangement = new FixedArrangement
        {
            State = new ArrangementState { Style = 0, Variation = 1, Combo = 0, Intensity = 3f, Fever = FeverPhaseMask.None, SectionPhrase = 1 }
        };
        var patterns = new PatternScheduler(shots, arrangement, 4.0, ulong.MaxValue, 4.0);
        var scheduled = new List<(PatternNote note, double beat)>();
        patterns.NoteScheduled += (n, b) => scheduled.Add((n, b));
        transport.PhraseStarted += n => transport.SetNextTempo(bpm[Math.Min(n + 1, bpm.Length - 1)]);

        transport.Start(StartDsp, bpm[0], bpm[0]);
        // Frames of 1000 samples from the start lead to beat 40 (into phrase 2).
        for (ulong now = StartDsp - StartLeadSamples; now < Expected(40, bpm); now += 1000)
        {
            backend.Now = now;
            transport.Update(now);
            Assert.IsTrue(transport.BeatAt(now, out double beat));
            patterns.Update(beat);
            shots.Update();
        }

        Assert.AreEqual(0, shots.LateCount, "no note late");
        Assert.AreEqual(scheduled.Count, backend.Starts.Count, "every scheduled note handed over");
        Assert.Greater(scheduled.Count, 100);
        var starts = new List<(Instrument inst, int midi, ulong dspStart)>(backend.Starts);
        bool beatZero = false;
        foreach (var (note, beat) in scheduled)
        {
            beatZero |= beat == 0.0;
            long expected = (long)Expected(beat, bpm) -
                            (long)Math.Round(InstrumentTable.OnsetSeconds(note.Instrument, note.Midi) * Rate);
            // A note of the next phrase handed over before its boundary is timed by BeatClock's pending tempo, which runs
            // from the previous anchor without rounding the boundary to a sample: at most 1 sample off. Phrase 0's notes
            // (anchored at beat 0) are exact.
            long tolerance = beat < 16 ? 0 : 1;
            int found = starts.FindIndex(s => s.inst == note.Instrument && s.midi == note.Midi &&
                                              Math.Abs((long)s.dspStart - expected) <= tolerance);
            Assert.GreaterOrEqual(found, 0, "no start within " + tolerance + " sample(s) for " + note.Instrument + " " + note.Midi + " beat " + beat);
            starts.RemoveAt(found);
        }
        Assert.IsTrue(beatZero, "beat 0's notes are scheduled");
    }
}
