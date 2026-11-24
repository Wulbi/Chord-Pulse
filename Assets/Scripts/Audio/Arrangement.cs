/// <summary>What the music should sound like at one step: the inputs of <see cref="PatternMatch"/>.</summary>
public struct ArrangementState
{
    public int Style;
    public int Variation;
    public int Combo;
    public float Intensity;
    /// <summary>The fever phase the step plays in (one phase; default 0 counts as <see cref="FeverPhaseMask.None"/>).</summary>
    public FeverPhaseMask Fever;
    public int FeatureLoop;
    /// <summary>1 = the first phrase of a section's 8 bars, 2 = the second (see <see cref="PatternClip.SectionPhrase"/>).</summary>
    public int SectionPhrase;
    /// <summary>True while the old timeline path plays the pitched layers: the pattern scheduler schedules nothing then.</summary>
    public bool Legacy;
}

/// <summary>The state that should sound at a step, without lookahead. Implemented by SoundManager.</summary>
public interface IArrangement
{
    ArrangementState At(int step);
}

public static class PatternMatch
{
    public static bool Matches(in PatternClip c, in ArrangementState s)
    {
        if (s.Style < 0 || s.Style > 30 || (c.StyleMask & (1 << s.Style)) == 0)
            return false;
        if (s.Variation < c.VariationMin || s.Variation > c.VariationMax)
            return false;
        if (s.Combo < c.ComboMin || s.Intensity < c.IntensityMin)
            return false;
        var fever = s.Fever == 0 ? FeverPhaseMask.None : s.Fever;
        if ((c.Fever & fever) == 0)
            return false;
        if (c.FeatureLoop != 0 && c.FeatureLoop != s.FeatureLoop)
            return false;
        return c.SectionPhrase == 0 || c.SectionPhrase == s.SectionPhrase;
    }
}
