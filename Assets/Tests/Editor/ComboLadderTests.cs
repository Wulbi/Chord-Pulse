using NUnit.Framework;

public class ComboLadderTests
{
    [Test] public void RungForCombo_FollowsTheThresholds()
    {
        var expected = new[] { 0, 0, 1, 2, 2, 3, 4, 4, 5, 5, 6 };   // combo 0..10
        for (int c = 0; c <= 10; c++) Assert.AreEqual(expected[c], ComboLadder.RungForCombo(c), "combo " + c);
        Assert.AreEqual(6, ComboLadder.RungForCombo(500));
        Assert.AreEqual(0, ComboLadder.RungForCombo(-3));
    }

    [Test] public void Floor_RisesWithTime()
    {
        Assert.AreEqual(0, ComboLadder.FloorForSeconds(-1)); Assert.AreEqual(0, ComboLadder.FloorForSeconds(29.9));
        Assert.AreEqual(1, ComboLadder.FloorForSeconds(30)); Assert.AreEqual(1, ComboLadder.FloorForSeconds(119.9));
        Assert.AreEqual(2, ComboLadder.FloorForSeconds(120)); Assert.AreEqual(2, ComboLadder.FloorForSeconds(3600));
        Assert.AreEqual(2, ComboLadder.Rung(0, 200)); Assert.AreEqual(4, ComboLadder.Rung(6, 200)); Assert.AreEqual(1, ComboLadder.Rung(2, 0));
    }

    [Test] public void Rung_SplitsIntoIntensityAndComboLevel()
    {
        var intensity = new[] { 0, 1, 2, 3, 3, 3, 3 }; var combo = new[] { 0, 0, 0, 0, 1, 2, 3 };
        for (int r = 0; r <= 6; r++) { Assert.AreEqual(intensity[r], ComboLadder.Intensity(r)); Assert.AreEqual(combo[r], ComboLadder.ComboLevel(r)); }
    }

    [Test] public void RequiredRung_IsTheHigherOfTheTwoConditions()
    {
        Assert.AreEqual(0, ComboLadder.RequiredRung(0f, 0));
        Assert.AreEqual(1, ComboLadder.RequiredRung(0.5f, 0)); Assert.AreEqual(2, ComboLadder.RequiredRung(1.5f, 0)); Assert.AreEqual(3, ComboLadder.RequiredRung(2.5f, 0));
        Assert.AreEqual(4, ComboLadder.RequiredRung(0f, 1)); Assert.AreEqual(6, ComboLadder.RequiredRung(0.5f, 3));
    }

    [Test] public void EveryClipOfThePatternTable_HasARungTheLadderCanReach()
    {
        // every clip of the pattern table: at the required rung, Intensity(r) >= IntensityMin and ComboLevel(r) >= ComboMin
        foreach (var c in PatternTable.Clips)
        {
            int r = ComboLadder.RequiredRung(c.IntensityMin, c.ComboMin);
            Assert.LessOrEqual(r, ComboLadder.MaxRung);
            Assert.GreaterOrEqual(ComboLadder.Intensity(r), c.IntensityMin); Assert.GreaterOrEqual(ComboLadder.ComboLevel(r), c.ComboMin);
            if (r > 0) Assert.IsTrue(ComboLadder.Intensity(r - 1) < c.IntensityMin || ComboLadder.ComboLevel(r - 1) < c.ComboMin);
        }
    }

    [Test] public void GuestChance_RisesWithTheRung()
    {
        var expected = new[] { 0.10, 0.10, 0.20, 0.30, 0.45, 0.60, 0.75 };
        for (int r = 0; r <= 6; r++) Assert.AreEqual(expected[r], ComboLadder.GuestChance(r), 1e-9);
        Assert.IsFalse(ComboLadder.GuestsMayRepeat(4)); Assert.IsTrue(ComboLadder.GuestsMayRepeat(5));
        Assert.AreEqual(0.75, ComboLadder.GuestChance(ComboLadder.GuestRung), 1e-9);
    }

    [Test] public void Fade_ScaleFollowsOneMinusTSquared_OverTwoBeats()
    {
        var f = new ComboFade();
        Assert.AreEqual(1f, f.Scale(10)); Assert.IsFalse(f.Active);
        f.Begin(10.0, 5, 1);
        Assert.AreEqual(1f, f.Scale(9.0)); Assert.AreEqual(1f, f.Scale(10.0));
        Assert.AreEqual(0.9375f, f.Scale(10.5), 1e-6f); Assert.AreEqual(0.75f, f.Scale(11.0), 1e-6f); Assert.AreEqual(0.4375f, f.Scale(11.5), 1e-6f);
        Assert.AreEqual(0f, f.Scale(12.0)); Assert.AreEqual(0f, f.Scale(40.0));
        Assert.IsFalse(f.Finished(11.99)); Assert.IsTrue(f.Finished(12.0));
        f.Clear(); Assert.IsFalse(f.Active); Assert.AreEqual(1f, f.Scale(11.0));
    }

    [Test] public void Fade_PresencePerLayer()
    {
        var f = new ComboFade();
        Assert.AreEqual(1f, f.Presence(3, 5, 0)); Assert.AreEqual(0f, f.Presence(6, 5, 0));   // no fade: at or below the heard rung 1, above it 0
        f.Begin(10.0, 5, 1);
        Assert.AreEqual(1f, f.Presence(1, 5, 11.0)); Assert.AreEqual(1f, f.Presence(0, 5, 11.0));   // at or below the floor
        Assert.AreEqual(0.75f, f.Presence(4, 5, 11.0), 1e-6f); Assert.AreEqual(0.75f, f.Presence(ComboLadder.GuestRung, ComboLadder.GuestRung, 11.0), 1e-6f);
        Assert.AreEqual(0f, f.Presence(6, 5, 11.0));
    }

    [Test] public void LeadBeats_ConvertsMixerBlocksToBeats()
    {
        double atRate120 = 2 * 1024 / 48000.0 * 2;
        Assert.AreEqual(atRate120, ComboFade.LeadBeats(2, 1024, 48000, 120), 1e-9);
        Assert.AreEqual(0.085333333333, atRate120, 1e-9);
        Assert.AreEqual(atRate120 / 2, ComboFade.LeadBeats(2, 1024, 48000, 60), 1e-9);
        Assert.AreEqual(0.0, ComboFade.LeadBeats(0, 1024, 48000, 120));
        Assert.AreEqual(0.0, ComboFade.LeadBeats(-1, 1024, 48000, 120));
        Assert.AreEqual(0.0, ComboFade.LeadBeats(2, 0, 48000, 120));
        Assert.AreEqual(0.0, ComboFade.LeadBeats(2, 1024, 0, 120));
        Assert.AreEqual(0.0, ComboFade.LeadBeats(2, 1024, -48000, 120));
        Assert.AreEqual(0.0, ComboFade.LeadBeats(2, 1024, 48000, 0));
        Assert.AreEqual(0.0, ComboFade.LeadBeats(2, 1024, 48000, -120));
    }

    [Test] public void LedScale_ReachesZeroOneLeadBeforeTheEnd()
    {
        var f = new ComboFade();
        f.Begin(10.0, 5, 1);
        double lead = ComboFade.LeadBeats(2, 1024, 48000, 116);
        Assert.AreEqual(0f, f.Scale(12.0 - lead + lead));
        Assert.Greater(f.Scale(12.0 - lead - 0.001 + lead), 0f);
        Assert.Greater(f.Scale(10.0 + lead), 0.99f);
    }
}
