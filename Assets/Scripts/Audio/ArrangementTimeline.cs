/// <summary>
/// The arrangement decisions SoundManager has made so far, as plain values (filled every frame, no allocation).
/// <see cref="ArrangementTimeline.At"/> turns them into the state of any step.
/// </summary>
public struct ArrangementInputs
{
    /// <summary>First step of the phrase being played; the next phrase starts StepsPerPhrase later.</summary>
    public int PhraseStartStep;
    public int StyleCurrent, StyleNext;
    public int VariationCurrent, VariationNext;
    public bool BreathCurrent, BreathNext;
    /// <summary>Feature loop id of the phrase (0 = none).</summary>
    public int FeatureCurrent, FeatureNext;
    public int SectionPhraseCurrent, SectionPhraseNext;
    /// <summary>The game's combo-ladder rung (0..<see cref="ComboLadder.MaxRung"/>); it applies from the bar after <see cref="RungBar"/>.</summary>
    public int LiveRung;
    /// <summary>Rung latched for bars up to and including <see cref="RungBar"/> (-1: none latched, plays as rung 0).</summary>
    public int AppliedRung, RungBar;

    // Stored as value + 1 so that a default (zeroed) struct reads -1: nothing pinned.
    int _pinIntensityPlus1, _pinComboPlus1;

    /// <summary>
    /// Intensity used in place of the ladder's (<see cref="ComboLadder.Intensity"/> of the rung) when 0 or above
    /// (default -1: not pinned). For measurement scenes and the transition to the ladder.
    /// </summary>
    public int PinIntensity { readonly get => _pinIntensityPlus1 - 1; set => _pinIntensityPlus1 = value + 1; }

    /// <summary>Combo level used in place of the ladder's (<see cref="ComboLadder.ComboLevel"/>) when 0 or above (default -1).</summary>
    public int PinCombo { readonly get => _pinComboPlus1 - 1; set => _pinComboPlus1 = value + 1; }
    /// <summary>First step of fever (-1: none queued); its build-up bar is the bar before.</summary>
    public int FeverStart;
    /// <summary>Fever bed variant of the queued fever (FeverSelector), and its fever guest loop id (0 = none).</summary>
    public int FeverVariant, FeverGuest;
    /// <summary>Chord progression of the phrase being played and of the next one (ProgressionSelector).</summary>
    public int ProgressionCurrent, ProgressionNext;
}

/// <summary>
/// SoundManager's arrangement rules as step functions, without the old parameter lead: a decision takes effect exactly
/// on its step (phrase decisions on the phrase boundary, combo on the bar line, intensity / breath / fever on their steps).
/// Pure; used for <see cref="IArrangement.At"/> and to find the first step a change of decisions affects.
/// </summary>
public static class ArrangementTimeline
{
    public const int StepsPerBar = 8;
    public const int StepsPerPhrase = 4 * StepsPerBar;
    public const int FeverSteps = 4 * StepsPerBar;
    public const int FeverBuildSteps = StepsPerBar;
    public const int ImpactSteps = 2;
    public const int MaxIntensity = 3;

    /// <summary>
    /// SectionPhrase of the next phrase: the timeline plays on into bars 5-8 when the same section follows its bars 1-4
    /// (2), and starts at bar 1 otherwise: another section, or the same section after its bars 5-8 (it loops back).
    /// </summary>
    public static int NextSectionPhrase(int currentSection, int currentSectionPhrase, int nextSection) =>
        nextSection == currentSection && currentSectionPhrase == 1 ? 2 : 1;

    /// <summary>SectionPhrase of each phrase of a run of sections (the first phrase starts at bar 1).</summary>
    public static int[] SectionPhrases(int[] sections)
    {
        var result = new int[sections.Length];
        for (int i = 0; i < sections.Length; i++)
            result[i] = i == 0 ? 1 : NextSectionPhrase(sections[i - 1], result[i - 1], sections[i]);
        return result;
    }

    static bool FeverZone(int step, in ArrangementInputs x) =>
        x.FeverStart >= 0 && step >= x.FeverStart - FeverBuildSteps && step < x.FeverStart + FeverSteps;

    static FeverPhaseMask FeverPhase(int step, in ArrangementInputs x)
    {
        if (x.FeverStart < 0 || step < x.FeverStart - FeverBuildSteps || step >= x.FeverStart + FeverSteps)
            return FeverPhaseMask.None;
        if (step < x.FeverStart)
            return FeverPhaseMask.Build;
        if (step < x.FeverStart + ImpactSteps)
            return FeverPhaseMask.Impact;
        return step >= x.FeverStart + FeverSteps - StepsPerBar ? FeverPhaseMask.Outro : FeverPhaseMask.Fever;
    }

    /// <summary>
    /// Feature loop that plays at a step (step function, no lead): the phrase's loop, switched exactly on the phrase
    /// boundary. No ordinary guest loop plays over fever or its build-up bar; the fever phrase itself (Impact, Fever,
    /// Outro) plays the fever's own guest loop (<see cref="ArrangementInputs.FeverGuest"/>, 0 = none).
    /// </summary>
    public static int FeatureLoopAt(int step, in ArrangementInputs x)
    {
        if (FeverZone(step, in x))
            return FeverPhase(step, in x) == FeverPhaseMask.Build ? 0 : x.FeverGuest;
        return step >= x.PhraseStartStep + StepsPerPhrase ? x.FeatureNext : x.FeatureCurrent;
    }

    static int BarOf(int step) => step >= 0 ? step / StepsPerBar : (step - StepsPerBar + 1) / StepsPerBar;

    /// <summary>Combo-ladder rung at a step: the latched rung up to <see cref="ArrangementInputs.RungBar"/>, the live one after.</summary>
    public static int RungAt(int step, in ArrangementInputs x) =>
        BarOf(step) > x.RungBar ? x.LiveRung : System.Math.Max(0, x.AppliedRung);

    public static ArrangementState At(int step, in ArrangementInputs x)
    {
        bool next = step >= x.PhraseStartStep + StepsPerPhrase;
        bool zone = FeverZone(step, in x);
        var phase = FeverPhase(step, in x);
        bool fever = phase != FeverPhaseMask.None && phase != FeverPhaseMask.Build;
        bool breath = next ? x.BreathNext : x.BreathCurrent;
        int rung = RungAt(step, in x);
        int intensity = x.PinIntensity >= 0 ? x.PinIntensity : ComboLadder.Intensity(rung);
        int combo = x.PinCombo >= 0 ? x.PinCombo : ComboLadder.ComboLevel(rung);
        return new ArrangementState
        {
            Style = zone ? 0 : next ? x.StyleNext : x.StyleCurrent,
            Variation = zone ? 0 : next ? x.VariationNext : x.VariationCurrent,
            Combo = zone ? 0 : combo,
            Intensity = fever ? MaxIntensity : breath ? 0 : intensity,
            Fever = phase,
            FeatureLoop = FeatureLoopAt(step, in x),
            SectionPhrase = next ? x.SectionPhraseNext : x.SectionPhraseCurrent,
            FeverVariant = zone ? x.FeverVariant : 0,
            Progression = next ? x.ProgressionNext : x.ProgressionCurrent,
        };
    }

    static bool Same(in ArrangementState a, in ArrangementState b) =>
        a.Style == b.Style && a.Variation == b.Variation && a.Combo == b.Combo && a.Intensity == b.Intensity &&
        a.Fever == b.Fever && a.FeatureLoop == b.FeatureLoop && a.SectionPhrase == b.SectionPhrase &&
        a.FeverVariant == b.FeverVariant && a.Progression == b.Progression;

    /// <summary>First step in [fromStep, toStep] whose state differs between the two decision sets, or -1.</summary>
    public static int FirstDifferentStep(in ArrangementInputs before, in ArrangementInputs after, int fromStep, int toStep)
    {
        for (int step = fromStep; step <= toStep; step++)
        {
            var a = At(step, in before);
            var b = At(step, in after);
            if (!Same(in a, in b))
                return step;
        }
        return -1;
    }
}
