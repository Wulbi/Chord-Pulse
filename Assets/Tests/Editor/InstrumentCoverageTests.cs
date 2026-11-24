using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

/// <summary>
/// Static check: every note the code can voice has a sample in the instrument library. A missing note is
/// silent at runtime, so it is pinned here. Later pattern tables add their notes through <see cref="AssertCovered"/>.
/// </summary>
public class InstrumentCoverageTests
{
    /// <summary>Fails listing every (instrument, bus, MIDI) that InstrumentTable has no sample for.</summary>
    public static void AssertCovered(IEnumerable<(Instrument, OutputBus, int)> notes)
    {
        var missing = new StringBuilder();
        foreach (var (instrument, bus, midi) in notes)
            if (!InstrumentTable.Has(instrument, bus, midi))
                missing.AppendLine($"no sample: instrument {instrument}, bus {bus}, MIDI {midi}");
        Assert.AreEqual(0, missing.Length, missing.ToString());
    }

    [Test]
    public void EveryVoicedKillNoteExists()
    {
        var failures = new StringBuilder();
        var list = new List<KillNote>();
        for (int style = 0; style < KillChordTable.Styles; style++)
            for (int color = 0; color < KillChordTable.Colors; color++)
                for (int chord = 0; chord < 4; chord++)
                    for (int voicing = 0; voicing <= 3; voicing++)
                        foreach (int combo in new[] { 0, 1, 2, 3 })
                            foreach (float tension in new[] { 0f, 0.7f })
                            {
                                list.Clear();
                                KillChordVoicer.Voice(style, color, chord, voicing, combo, tension, list);
                                foreach (var n in list)
                                    if (!InstrumentTable.Has(n.Instrument, OutputBus.KillChords, n.Midi))
                                        failures.AppendLine($"style {style} color {color} chord {chord} voicing {voicing} combo {combo} tension {tension}: instrument {n.Instrument} has no sample for MIDI {n.Midi}");
                            }
        Assert.AreEqual(0, failures.Length, failures.ToString());
    }
}
