using NUnit.Framework;

public class OneShotReleaseRuleTests
{
    [Test]
    public void KillChords_KeepTheirInstrumentRelease()
    {
        Assert.AreEqual(InstrumentTable.ReleaseSeconds(Instrument.Bells, OutputBus.KillChords),
            SoundManager.OneShotReleaseSeconds(Instrument.Bells, OutputBus.KillChords));
    }

    [Test]
    public void PatternNotes_EndLikeTheTimeline_ShortStopFade()
    {
        // The timeline cut the old tracks' notes at their end (no audible ADSR release; Task 11 offline render).
        Assert.AreEqual(SoundManager.PatternStopFadeSeconds, SoundManager.OneShotReleaseSeconds(Instrument.Bells, OutputBus.Harmony));
        Assert.AreEqual(SoundManager.PatternStopFadeSeconds, SoundManager.OneShotReleaseSeconds(Instrument.GlassPad, OutputBus.ComboPad));
        Assert.AreEqual(SoundManager.PatternStopFadeSeconds, SoundManager.OneShotReleaseSeconds(Instrument.Bass, OutputBus.Bass));
        Assert.Less(SoundManager.PatternStopFadeSeconds, 0.03);
    }
}
