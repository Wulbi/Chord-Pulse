using NUnit.Framework;

public class ArrangementTimelineTests
{
    static int[] Phrases(params int[] sections) => ArrangementTimeline.SectionPhrases(sections);

    [Test]
    public void SectionPhrase_Seed7_MatchesTheTimeline()
    {
        // Seed 7 (unity-music-clock spec 1): sections 0,0,1,0,0,0,1,1 per 16 beats. Boundary 16 plays on (bars 5-8),
        // 32 goes to section 1's start, 48 jumps back to section 0's start, 64 plays on, 80 loops back to bar 1,
        // 96 jumps, 112 plays on.
        CollectionAssert.AreEqual(new[] { 1, 2, 1, 1, 2, 1, 1, 2 }, Phrases(0, 0, 1, 0, 0, 0, 1, 1));
    }

    [Test]
    public void SectionPhrase_SameSectionAlternates_NewSectionStartsAtBar1()
    {
        CollectionAssert.AreEqual(new[] { 1, 2, 1, 2, 1 }, Phrases(2, 2, 2, 2, 2));
        CollectionAssert.AreEqual(new[] { 1, 1, 2, 1, 1 }, Phrases(0, 1, 1, 1, 3));
        CollectionAssert.AreEqual(new[] { 1, 1, 1, 1 }, Phrases(0, 1, 0, 1)); // seed 33 spike: a jump every phrase
    }

    [Test]
    public void NextSectionPhrase_Rule()
    {
        Assert.AreEqual(2, ArrangementTimeline.NextSectionPhrase(1, 1, 1));
        Assert.AreEqual(1, ArrangementTimeline.NextSectionPhrase(1, 2, 1));
        Assert.AreEqual(1, ArrangementTimeline.NextSectionPhrase(1, 1, 2));
        Assert.AreEqual(1, ArrangementTimeline.NextSectionPhrase(1, 2, 3));
    }

    static ArrangementInputs Inputs() => new ArrangementInputs
    {
        PhraseStartStep = 32, StyleCurrent = 1, StyleNext = 2, VariationCurrent = 3, VariationNext = 4,
        SectionPhraseCurrent = 1, SectionPhraseNext = 2, FeatureCurrent = 5, FeatureNext = 6,
        // Intensity pinned at 2 (independent of the combo part); combo 1 (rung 4) latched up to bar 5, combo 3 (rung 6) after.
        PinIntensity = 2, AppliedRung = 4, LiveRung = 6, RungBar = 5, FeverStart = -1,
    };

    [Test]
    public void At_PhraseDecisions_SwitchExactlyOnTheBoundaryStep()
    {
        var x = Inputs();
        var a = ArrangementTimeline.At(63, in x);
        var b = ArrangementTimeline.At(64, in x);
        Assert.AreEqual((1, 3, 1, 5), (a.Style, a.Variation, a.SectionPhrase, a.FeatureLoop));
        Assert.AreEqual((2, 4, 2, 6), (b.Style, b.Variation, b.SectionPhrase, b.FeatureLoop));
        Assert.AreEqual(FeverPhaseMask.None, a.Fever);
    }

    [Test]
    public void At_Combo_LatchedUpToComboBar_GameValueAfter()
    {
        var x = Inputs();
        Assert.AreEqual(1, ArrangementTimeline.At(5 * 8 + 7, in x).Combo); // bar 5 (latched)
        Assert.AreEqual(3, ArrangementTimeline.At(6 * 8, in x).Combo);     // bar 6: the game's level
    }

    [Test]
    public void At_Intensity_BreathPhraseIsZero_OtherwiseTheGameValue()
    {
        var x = Inputs();
        x.BreathNext = true;
        Assert.AreEqual(2f, ArrangementTimeline.At(63, in x).Intensity);
        Assert.AreEqual(0f, ArrangementTimeline.At(64, in x).Intensity);
    }

    [Test]
    public void At_FeverZone_PlaysStyle0Variation0Combo0_FeverIntensityMax()
    {
        var x = Inputs();
        x.FeverStart = 96; // build-up bar 88..95, fever 96..127
        var before = ArrangementTimeline.At(87, in x);
        var build = ArrangementTimeline.At(88, in x);
        var impact = ArrangementTimeline.At(96, in x);
        var fever = ArrangementTimeline.At(100, in x);
        var outro = ArrangementTimeline.At(120, in x);
        var after = ArrangementTimeline.At(128, in x);
        Assert.AreEqual((2, 4, 3, 2f, FeverPhaseMask.None), (before.Style, before.Variation, before.Combo, before.Intensity, before.Fever));
        Assert.AreEqual((0, 0, 0, 2f, FeverPhaseMask.Build), (build.Style, build.Variation, build.Combo, build.Intensity, build.Fever));
        Assert.AreEqual((0, 0, 0, 3f, FeverPhaseMask.Impact), (impact.Style, impact.Variation, impact.Combo, impact.Intensity, impact.Fever));
        Assert.AreEqual(FeverPhaseMask.Fever, fever.Fever);
        Assert.AreEqual(3f, fever.Intensity);
        Assert.AreEqual(FeverPhaseMask.Outro, outro.Fever);
        Assert.AreEqual(0, build.FeatureLoop);
        Assert.AreEqual(FeverPhaseMask.None, after.Fever);
        Assert.AreEqual(2f, after.Intensity);
    }

    [Test]
    public void Fever_StepFunction_BuildBar_ImpactFirstBeat_OutroLastBar()
    {
        // The fever layers' phase (pattern_fever.py) per step, no lead: the timeline's FeverBuild / FeverImpact / Fever /
        // FeverOutro parameter rule (SoundManager.UpdateFever) without its 0.25 s lead. Impact is fever's first beat.
        var x = Inputs();
        x.FeverStart = 96;
        for (int step = 80; step < 140; step++)
        {
            var expected = step < 88 || step >= 128 ? FeverPhaseMask.None
                : step < 96 ? FeverPhaseMask.Build
                : step < 96 + SoundManager.StepsPerBeat ? FeverPhaseMask.Impact
                : step < 120 ? FeverPhaseMask.Fever
                : FeverPhaseMask.Outro;
            Assert.AreEqual(expected, ArrangementTimeline.At(step, in x).Fever, $"step {step}");
        }
        x.FeverStart = -1;
        for (int step = 80; step < 140; step++)
            Assert.AreEqual(FeverPhaseMask.None, ArrangementTimeline.At(step, in x).Fever, $"no fever, step {step}");
    }

    [Test]
    public void FeatureLoopAt_StepFunction_SwitchesOnTheBoundary_ZeroOverFever()
    {
        var x = Inputs(); // current loop 5, next loop 6, next phrase from step 64
        Assert.AreEqual(5, ArrangementTimeline.FeatureLoopAt(63, in x));
        Assert.AreEqual(6, ArrangementTimeline.FeatureLoopAt(64, in x));
        x.FeatureNext = 0; // no loop next phrase: off exactly on the boundary (no lead, nothing to protect)
        Assert.AreEqual(5, ArrangementTimeline.FeatureLoopAt(63, in x));
        Assert.AreEqual(0, ArrangementTimeline.FeatureLoopAt(64, in x));
        x.FeatureNext = 6;
        x.FeverStart = 96; // build-up bar 88..95, fever 96..127: no loop over any of it
        Assert.AreEqual(6, ArrangementTimeline.FeatureLoopAt(87, in x));
        Assert.AreEqual(0, ArrangementTimeline.FeatureLoopAt(88, in x));
        Assert.AreEqual(0, ArrangementTimeline.FeatureLoopAt(127, in x));
        Assert.AreEqual(6, ArrangementTimeline.FeatureLoopAt(128, in x));
        for (int step = 30; step < 130; step++)
            Assert.AreEqual(ArrangementTimeline.FeatureLoopAt(step, in x), ArrangementTimeline.At(step, in x).FeatureLoop, $"step {step}");
    }

    [Test]
    public void FeverGuestAndVariant_OnlyOverTheFeverPhrase_NotTheBuildUpBar()
    {
        var x = Inputs(); // next phrase from step 64 plays loop 6
        x.FeverStart = 96; // build-up bar 88..95, fever 96..127
        x.FeverGuest = 14;
        x.FeverVariant = 2;
        Assert.AreEqual(6, ArrangementTimeline.FeatureLoopAt(87, in x));
        Assert.AreEqual(0, ArrangementTimeline.FeatureLoopAt(88, in x));  // build-up bar: no guest
        Assert.AreEqual(14, ArrangementTimeline.FeatureLoopAt(96, in x)); // impact
        Assert.AreEqual(14, ArrangementTimeline.FeatureLoopAt(127, in x)); // outro
        Assert.AreEqual(6, ArrangementTimeline.FeatureLoopAt(128, in x));
        Assert.AreEqual(0, ArrangementTimeline.At(87, in x).FeverVariant);
        Assert.AreEqual(2, ArrangementTimeline.At(88, in x).FeverVariant);
        Assert.AreEqual(2, ArrangementTimeline.At(127, in x).FeverVariant);
        Assert.AreEqual(0, ArrangementTimeline.At(128, in x).FeverVariant);
    }

    [Test]
    public void PatternMatch_FeverVariantMask_SelectsTheVariantsClips()
    {
        var only2 = new PatternClip(0, 1, 0, 4, 0, 0f, FeverPhaseMask.Fever, 0, 0, 0, 0, 1 << 2);
        var any = new PatternClip(0, 1, 0, 4, 0, 0f, FeverPhaseMask.Fever, 0, 0, 0, 0);
        var s = new ArrangementState { Fever = FeverPhaseMask.Fever, FeverVariant = 2 };
        Assert.IsTrue(PatternMatch.Matches(in only2, in s));
        Assert.IsTrue(PatternMatch.Matches(in any, in s));
        s.FeverVariant = 1;
        Assert.IsFalse(PatternMatch.Matches(in only2, in s));
        Assert.IsTrue(PatternMatch.Matches(in any, in s));
    }

    [Test]
    public void FeatureSelector_NeverPicksAFeverGuest_ForAnOrdinaryPhrase()
    {
        var selector = new FeatureSelector(1234);
        var phrase = new SectionSelector.Phrase();
        for (int i = 0; i < 2000; i++)
        {
            var loop = selector.Advance(phrase, 3, allowed: true);
            Assert.IsTrue(loop == null || !loop.Fever, $"phrase {i}: {loop?.Name}");
        }
    }

    [Test]
    public void FeverSelector_PicksAGuestOfFeverLoops_NeverTheSameTwiceInARow()
    {
        var selector = new FeverSelector(99);
        int lastVariant = -1, lastGuest = 0, guests = 0;
        for (int i = 0; i < 400; i++)
        {
            selector.Choose();
            Assert.That(selector.Variant, Is.InRange(0, PatternTable.FeverVariantCount - 1));
            Assert.AreNotEqual(lastVariant, selector.Variant, $"fever {i}: variant repeated");
            if (selector.Guest != null)
            {
                guests++;
                Assert.IsTrue(selector.Guest.Fever, selector.Guest.Name);
                Assert.AreNotEqual(lastGuest, selector.Guest.Id, $"fever {i}: guest repeated");
            }
            lastVariant = selector.Variant;
            lastGuest = selector.Guest != null ? selector.Guest.Id : 0;
        }
        Assert.That(guests, Is.InRange(300, 380)); // about 85 %
    }

    [Test]
    public void FirstDifferentStep_NoChange_IsMinus1()
    {
        var a = Inputs();
        var b = Inputs();
        Assert.AreEqual(-1, ArrangementTimeline.FirstDifferentStep(in a, in b, 40, 70));
    }

    [Test]
    public void FirstDifferentStep_IntensityChange_FromTheFirstStepAsked()
    {
        var a = Inputs();
        var b = Inputs();
        b.PinIntensity = 3;
        Assert.AreEqual(40, ArrangementTimeline.FirstDifferentStep(in a, in b, 40, 70));
    }

    [Test]
    public void FirstDifferentStep_ComboChange_FromTheNextBarLine()
    {
        var a = Inputs();
        var b = Inputs();
        b.LiveRung = 3; // hurt / combo drop (combo 0; intensity is pinned): bar 5 is latched, bar 6 on changes
        Assert.AreEqual(48, ArrangementTimeline.FirstDifferentStep(in a, in b, 41, 70));
    }

    [Test]
    public void FirstDifferentStep_NextPhraseStyle_FromTheBoundary_OnlyIfInRange()
    {
        var a = Inputs();
        var b = Inputs();
        b.StyleNext = 0;
        Assert.AreEqual(64, ArrangementTimeline.FirstDifferentStep(in a, in b, 60, 66));
        Assert.AreEqual(-1, ArrangementTimeline.FirstDifferentStep(in a, in b, 50, 63));
    }

    [Test]
    public void FirstDifferentStep_PhraseAdvance_ChangesNothingThatSounds()
    {
        // UpdatePhrase: the next phrase becomes current, a new next is chosen; steps of the new phrase keep their state.
        var a = Inputs();
        var b = Inputs();
        b.PhraseStartStep = 64;
        b.StyleCurrent = a.StyleNext; b.VariationCurrent = a.VariationNext; b.SectionPhraseCurrent = a.SectionPhraseNext;
        b.FeatureCurrent = a.FeatureNext; b.BreathCurrent = a.BreathNext;
        b.StyleNext = 3; b.VariationNext = 1; b.SectionPhraseNext = 1; b.FeatureNext = 0; b.BreathNext = true;
        Assert.AreEqual(-1, ArrangementTimeline.FirstDifferentStep(in a, in b, 64, 90));
    }

    [Test]
    public void FirstDifferentStep_Fever_FromTheBuildUpBar()
    {
        var a = Inputs();
        var b = Inputs();
        b.FeverStart = 96;
        Assert.AreEqual(88, ArrangementTimeline.FirstDifferentStep(in a, in b, 70, 100));
    }

    // ---- Combo ladder Task 3: intensity and combo come from one bar-latched rung ----

    // One phrase from step 0, nothing pinned, no fever; the rung is latched as given.
    static ArrangementInputs RungInputs(int appliedRung, int rungBar, int liveRung) => new ArrangementInputs
    {
        PhraseStartStep = 0, SectionPhraseCurrent = 1, SectionPhraseNext = 2,
        AppliedRung = appliedRung, RungBar = rungBar, LiveRung = liveRung, FeverStart = -1,
    };

    [Test]
    public void Rung_GivesIntensityAndCombo_LatchedOnTheBarLine()
    {
        var x = RungInputs(2, 3, 5);
        Assert.AreEqual(-1, x.PinIntensity, "nothing pinned by default");
        Assert.AreEqual(-1, x.PinCombo, "nothing pinned by default");
        for (int step = 24; step < 32; step++) // bar 3: latched rung 2
        {
            Assert.AreEqual(2, ArrangementTimeline.RungAt(step, in x), $"step {step}");
            var s = ArrangementTimeline.At(step, in x);
            Assert.AreEqual((2f, 0), (s.Intensity, s.Combo), $"step {step}");
        }
        Assert.AreEqual(5, ArrangementTimeline.RungAt(32, in x));
        var after = ArrangementTimeline.At(32, in x); // bar 4: the live rung 5
        Assert.AreEqual((3f, 2), (after.Intensity, after.Combo));
    }

    [Test]
    public void Rung_NoneLatched_IsZeroUpToTheBar()
    {
        var x = RungInputs(-1, 3, 5);
        Assert.AreEqual(0, ArrangementTimeline.RungAt(31, in x));
        Assert.AreEqual((0f, 0), (ArrangementTimeline.At(31, in x).Intensity, ArrangementTimeline.At(31, in x).Combo));
    }

    [Test]
    public void Breath_ZeroesIntensity_KeepsTheComboPart()
    {
        var x = RungInputs(5, 3, 5);
        x.BreathCurrent = true;
        var s = ArrangementTimeline.At(10, in x);
        Assert.AreEqual((0f, 2), (s.Intensity, s.Combo));
    }

    [Test]
    public void BuildBar_TakesIntensityFromTheRung_ComboZero()
    {
        var x = RungInputs(5, -1, 5);
        x.FeverStart = 64; // build-up bar 56..63, fever 64..95
        for (int step = 56; step < 64; step++)
        {
            var s = ArrangementTimeline.At(step, in x);
            Assert.AreEqual((3f, 0, FeverPhaseMask.Build), (s.Intensity, s.Combo, s.Fever), $"step {step}");
        }
        x.AppliedRung = x.LiveRung = 1;
        for (int step = 56; step < 64; step++)
            Assert.AreEqual(1f, ArrangementTimeline.At(step, in x).Intensity, $"rung 1, step {step}");
        foreach (int rung in new[] { 5, 0 })
        {
            x.AppliedRung = x.LiveRung = rung;
            for (int step = 64; step < 96; step++)
            {
                var s = ArrangementTimeline.At(step, in x);
                Assert.AreEqual((3f, 0), (s.Intensity, s.Combo), $"rung {rung}, step {step}");
            }
        }
    }

    [Test]
    public void Pin_OverridesTheLadder()
    {
        var x = RungInputs(0, -1, 0);
        x.PinIntensity = 1;
        x.PinCombo = 3;
        var s = ArrangementTimeline.At(10, in x);
        Assert.AreEqual((1f, 3), (s.Intensity, s.Combo));

        x.BreathCurrent = true; // breath still zeroes the intensity
        s = ArrangementTimeline.At(10, in x);
        Assert.AreEqual((0f, 3), (s.Intensity, s.Combo));

        x.BreathCurrent = false;
        x.FeverStart = 64; // build-up bar: pinned intensity, combo 0; fever phrase: max intensity, combo 0
        s = ArrangementTimeline.At(60, in x);
        Assert.AreEqual((1f, 0), (s.Intensity, s.Combo));
        s = ArrangementTimeline.At(70, in x);
        Assert.AreEqual((3f, 0), (s.Intensity, s.Combo));
    }

    [Test]
    public void FirstDifferentStep_SeesARungDrop()
    {
        var before = RungInputs(5, 3, 5);
        var after = RungInputs(1, 3, 1);
        Assert.AreEqual(24, ArrangementTimeline.FirstDifferentStep(in before, in after, 24, 40));
    }
}
