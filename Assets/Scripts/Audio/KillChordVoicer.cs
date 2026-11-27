using System.Collections.Generic;

/// <summary>One note of a kill chord: which instrument event, MIDI note, length in beats and gain trim (dB, relative to the -27 dB instrument level).</summary>
public readonly struct KillNote
{
    public readonly Instrument Instrument;
    public readonly int Midi;
    public readonly double LengthBeats;
    public readonly float GainDb;

    public KillNote(Instrument instrument, int midi, double lengthBeats, float gainDb)
    {
        Instrument = instrument;
        Midi = midi;
        LengthBeats = lengthBeats;
        GainDb = gainDb;
    }
}

/// <summary>Decides which notes a kill chord plays (tables in the generated KillChordTable). Pure: no FMOD, no allocation.</summary>
public static class KillChordVoicer
{
    public const int ComboSeventh = 2;
    public const int ComboBell = 3;
    public const float TensionPunch = 0.65f;

    /// <param name="style">0 synthpop, 1 latin, 2 funk, 3 dreamy.</param>
    /// <param name="color">0 Red, 1 Blue, 2 Yellow.</param>
    /// <param name="chordIndex">Chord id (PatternTable.ChordNames): 0 C, 1 Am, 2 F, 3 G, 4 Dm, 5 Em.</param>
    /// <param name="voicing">0 none, 1 triad, 2 + octave, 3 + top voice.</param>
    public static void Voice(int style, int color, int chordIndex, int voicing, int combo, float tension, List<KillNote> into)
    {
        Instrument inst = KillChordTable.ColorInstrument[style, color];
        int octave = KillChordTable.Octave[style, color];
        double length = KillChordTable.LengthBeats[style, color];
        float mainGain = KillChordTable.TargetDb[style] - KillChordTable.NormalizeDb;   // style 0: target -27 = original level, 0 dB

        for (int v = 0; v < KillChordTable.VoiceMinVoicing.Length; v++)
        {
            if (voicing >= KillChordTable.VoiceMinVoicing[v])
                into.Add(new KillNote(inst, KillChordTable.Voicing[chordIndex, v] + octave, length, mainGain));
        }
        if (voicing < 1) return;   // no kill, so no extras either

        if (combo >= ComboSeventh)
            into.Add(new KillNote(inst, KillChordTable.Seventh[chordIndex] + octave, length, KillChordTable.SeventhDb - KillChordTable.NormalizeDb));
        if (combo >= ComboBell)
            into.Add(new KillNote(Instrument.Bells, KillChordTable.Voicing[chordIndex, 2] + KillChordTable.BellStyleOffset,
                KillChordTable.BellLengthBeats, KillChordTable.BellDb - KillChordTable.NormalizeDb));
        if (tension >= TensionPunch)
            into.Add(new KillNote(KillChordTable.PunchInstrument(style), KillChordTable.RootLow[chordIndex],
                KillChordTable.PunchLengthBeats, KillChordTable.PunchDb - KillChordTable.NormalizeDb));
    }

    /// <summary>
    /// Seconds to start the one-shot early so its attack lands on the beat. Applied on every beat, phrase downbeats
    /// (possible section-jump landings) included: a DSP-scheduled start may sit before the jump (the old timeline notes
    /// could not, hence their no-shift rule).
    /// </summary>
    public static double OnsetShiftSeconds(KillNote n)
    {
        return InstrumentTable.OnsetSeconds(n.Instrument, n.Midi);
    }
}
