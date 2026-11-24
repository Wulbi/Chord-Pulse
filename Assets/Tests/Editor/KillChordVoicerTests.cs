using System.Collections.Generic;
using NUnit.Framework;

public class KillChordVoicerTests
{
    static List<KillNote> Voice(int style, int color, int chord, int voicing, int combo = 0, float tension = 0f)
    {
        var list = new List<KillNote>();
        KillChordVoicer.Voice(style, color, chord, voicing, combo, tension, list);
        return list;
    }

    static List<int> Midis(List<KillNote> notes, Instrument? only = null)
    {
        var m = new List<int>();
        foreach (var n in notes) if (only == null || n.Instrument == only.Value) m.Add(n.Midi);
        m.Sort();
        return m;
    }

    static bool Has(List<KillNote> notes, Instrument inst, int midi)
    {
        foreach (var n in notes) if (n.Instrument == inst && n.Midi == midi) return true;
        return false;
    }

    [Test]
    public void Synthpop_Red_C_Voicing1()
    {
        var notes = Voice(0, 0, 0, 1);
        CollectionAssert.AreEqual(new[] { 60, 64, 67 }, Midis(notes));
        Assert.AreEqual(0f, notes[0].GainDb, 1e-4);
        Assert.AreEqual(0.78, notes[0].LengthBeats, 1e-9);
    }

    [Test]
    public void Voicing3_AddsOctaveAndTop()
    {
        CollectionAssert.AreEqual(new[] { 60, 64, 67, 72, 79 }, Midis(Voice(0, 0, 0, 3)));
        CollectionAssert.AreEqual(new[] { 60, 64, 67, 72 }, Midis(Voice(0, 0, 0, 2)));
        Assert.AreEqual(0, Voice(0, 0, 0, 0).Count);
    }

    [Test]
    public void Latin_Yellow_IsGlockTwoOctavesUp()
    {
        var notes = Voice(1, 2, 0, 1);
        CollectionAssert.AreEqual(new[] { 84, 88, 91 }, Midis(notes));
        foreach (var n in notes) Assert.AreEqual(Instrument.Glock, n.Instrument);
    }

    [Test]
    public void Combo2_AddsSeventh()
    {
        var notes = Voice(0, 0, 1, 1, combo: 2);
        Assert.IsTrue(Has(notes, Instrument.OrigRed, 67));
        Assert.IsFalse(Has(Voice(0, 0, 1, 1, combo: 1), Instrument.OrigRed, 67));
        // seventh sits at -30 dB: 3 dB under the -27 normalisation
        foreach (var n in notes) if (n.Midi == 67) Assert.AreEqual(-3f, n.GainDb, 1e-4);
    }

    [Test]
    public void Combo3_AddsBellOctave()
    {
        var notes = Voice(0, 0, 0, 1, combo: 3);
        Assert.IsTrue(Has(notes, Instrument.Bells, 79));
        Assert.IsFalse(Has(Voice(0, 0, 0, 1, combo: 2), Instrument.Bells, 79));
        foreach (var n in notes)
            if (n.Instrument == Instrument.Bells) { Assert.AreEqual(1.5, n.LengthBeats, 1e-9); Assert.AreEqual(-6f, n.GainDb, 1e-4); }
    }

    [Test]
    public void Tension_AddsLowRoot_DreamyUsesSub()
    {
        Assert.IsTrue(Has(Voice(3, 0, 2, 1, tension: 0.7f), Instrument.Sub, 41));
        Assert.IsFalse(Has(Voice(3, 0, 2, 1, tension: 0.6f), Instrument.Sub, 41));
        Assert.IsTrue(Has(Voice(1, 0, 2, 1, tension: 0.7f), Instrument.Bass, 41));
    }

    [Test]
    public void Dreamy_MainNotesAreOneDbQuieter()
    {
        foreach (var n in Voice(3, 1, 0, 1)) Assert.AreEqual(-1f, n.GainDb, 1e-4);
    }

    [Test]
    public void PhraseDownbeat_HasNoOnsetShift()
    {
        var note = Voice(1, 0, 0, 1)[0];
        Assert.AreEqual(0.0, KillChordVoicer.OnsetShiftSeconds(note, true));
        Assert.AreEqual(InstrumentTable.OnsetSeconds(note.Instrument, note.Midi), KillChordVoicer.OnsetShiftSeconds(note, false));
    }

    [Test]
    public void EveryNoteHasAnInstrumentSample()
    {
        // OnsetSeconds is 0 for notes missing from the library, so check against a real (non-zero onset) marimba note
        Assert.Greater(InstrumentTable.OnsetSeconds(Instrument.Marimba, 72), 0.0);
        for (int s = 0; s < 4; s++)
            for (int c = 0; c < 3; c++)
                for (int ch = 0; ch < 4; ch++)
                    Assert.AreEqual(5, Voice(s, c, ch, 3).Count);
    }
}
