using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

public class SpawnDirectorTests
{
    SpawnDirector _dir;

    [SetUp]
    public void SetUp()
    {
        _dir = new SpawnDirector(new System.Random(1234));
    }

    static SpawnContext Ctx(int step = 0, bool fever = false, bool breath = false, int section = 0, double seconds = 0)
    {
        return new SpawnContext { Step = step, Fever = fever, Breath = breath, Section = section, RoundSeconds = seconds };
    }

    List<(int step, SpawnOrder order)> Collect(int fromStep, int toStep, SpawnContext template)
    {
        var list = new List<(int step, SpawnOrder order)>();
        for (int s = fromStep; s <= toStep; s++)
        {
            template.Step = s;
            if (_dir.TryNext(template, out var order)) list.Add((s, order));
        }
        return list;
    }

    static List<(int step, SpawnOrder order)> AfterOpening(List<(int step, SpawnOrder order)> spawns)
    {
        return spawns.Skip(3).ToList();
    }

    static List<int> Steps(List<(int step, SpawnOrder order)> spawns) { return spawns.Select(x => x.step).ToList(); }
    static List<int> Colors(List<(int step, SpawnOrder order)> spawns) { return spawns.Select(x => x.order.ColorIndex).ToList(); }

    // A slot of another colour inside a run (order.Mixed) counts toward that run's length.
    static List<(int Color, int Length)> RunsOf(List<(int step, SpawnOrder order)> spawns)
    {
        var runs = new List<(int Color, int Length)>();
        foreach (var x in spawns)
        {
            int c = x.order.ColorIndex;
            if (runs.Count > 0 && (x.order.Mixed || runs[runs.Count - 1].Color == c))
                runs[runs.Count - 1] = (runs[runs.Count - 1].Color, runs[runs.Count - 1].Length + 1);
            else
                runs.Add((c, 1));
        }
        return runs;
    }

    static int MinGap(List<(int step, SpawnOrder order)> spawns)
    {
        int m = int.MaxValue;
        for (int i = 1; i < spawns.Count; i++) m = System.Math.Min(m, spawns[i].step - spawns[i - 1].step);
        return m;
    }

    static int MaxGap(List<(int step, SpawnOrder order)> spawns)
    {
        int m = 0;
        for (int i = 1; i < spawns.Count; i++) m = System.Math.Max(m, spawns[i].step - spawns[i - 1].step);
        return m;
    }

    static void AssertGapsAtLeast(List<(int step, SpawnOrder order)> spawns, int gap)
    {
        Assert.Greater(spawns.Count, 1);
        Assert.GreaterOrEqual(MinGap(spawns), gap);
    }

    [Test] public void BeforeMusic_SpawnsNothing() { Assert.IsFalse(_dir.TryNext(Ctx(step: -1), out _)); }

    [Test]
    public void Opening_ThreeColorsOnDownbeats()
    {
        var s = Collect(0, 23, Ctx());
        CollectionAssert.AreEqual(new[] { 0, 8, 16 }, Steps(s).Take(3));
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Colors(s).Take(3));
    }

    [Test]
    public void Opening_SurvivesASkippedDownbeat()
    {
        for (int s = 0; s <= 6; s++) _dir.TryNext(Ctx(step: s), out _);
        Assert.IsTrue(_dir.TryNext(Ctx(step: 9), out var order));
        Assert.AreEqual(1, order.ColorIndex);
    }

    [Test]
    public void FirstRunAfterOpening_StartsOnTheNextBar()
    {
        var s = Collect(0, 40, Ctx());
        Assert.AreEqual(24, s[3].step);
    }

    [Test]
    public void StepConstants_MatchSoundManager()
    {
        Assert.AreEqual(SoundManager.StepsPerBeat, SpawnDirector.StepsPerBeat);
        Assert.AreEqual(SoundManager.StepsPerBar, SpawnDirector.StepsPerBar);
    }

    [Test]
    public void Breath_AfterFlow_CutsTheRunShort()
    {
        int step = 0;
        var flow = Ctx(section: 3, seconds: 60);
        int lastColor = -1, spawns = 0, runColor = -1;
        // Flow until a new run's first spawn after the opening.
        for (; ; step++)
        {
            flow.Step = step;
            if (!_dir.TryNext(flow, out var o)) continue;
            spawns++;
            if (spawns > 3 && o.ColorIndex != lastColor) { runColor = o.ColorIndex; step++; break; }
            lastColor = o.ColorIndex;
        }
        var breath = Ctx(breath: true, section: 3, seconds: 60);
        int same = 0;
        for (int i = 0; i < 200; i++, step++)
        {
            breath.Step = step;
            if (!_dir.TryNext(breath, out var o)) continue;
            if (o.ColorIndex == runColor) same++;
            else break;
        }
        Assert.LessOrEqual(same, GameplayTuning.BreathRunMax);
    }

    [Test]
    public void Breath_SpawnsAtMostOncePerBar()
    {
        var s = AfterOpening(Collect(0, 200, Ctx(breath: true)));
        AssertGapsAtLeast(s, 8);
    }

    [Test]
    public void Runs_AreOneColor_AndNeighbouringRunsDiffer()
    {
        var runs = RunsOf(NoChargers(AfterOpening(Collect(0, 800, Ctx(section: 3, seconds: 60)))));
        Assert.Greater(runs.Count, 5);
        foreach (var r in runs.Skip(1).Take(runs.Count - 2))
        {
            Assert.GreaterOrEqual(r.Length, Stage(60).RunMin);
            Assert.LessOrEqual(r.Length, Stage(60).RunMax);
        }
        for (int i = 1; i < runs.Count; i++) Assert.AreNotEqual(runs[i - 1].Color, runs[i].Color);
    }

    [Test]
    public void NoHiddenBeforeThirtySeconds_SomeAfter()
    {
        Assert.IsFalse(Collect(0, 400, Ctx(seconds: 29)).Any(x => x.order.Hidden));
        _dir.Reset();
        Assert.IsTrue(Collect(0, 400, Ctx(seconds: 31)).Any(x => x.order.Hidden));
    }

    [Test]
    public void Speed_StaysInsideJitterRange()
    {
        var all = NoChargers(Collect(0, 400, Ctx(section: 3, seconds: 60)));
        Assert.IsNotEmpty(all);
        foreach (var x in all)
        {
            Assert.GreaterOrEqual(x.order.Speed, 0.34f - 1e-4f);
            Assert.LessOrEqual(x.order.Speed, 0.46f + 1e-4f);
        }
    }

    [Test]
    public void Fever_SpawnsOncePerBeat()
    {
        var s = Collect(100, 131, Ctx(fever: true));
        Assert.AreEqual(16, s.Count);
    }

    [Test]
    public void SkippedSteps_SpawnOnlyOne()
    {
        Collect(0, 40, Ctx(section: 3, seconds: 60));
        Assert.IsTrue(_dir.TryNext(Ctx(step: 200, section: 3, seconds: 60), out _));
        Assert.IsFalse(_dir.TryNext(Ctx(step: 200, section: 3, seconds: 60), out _));
    }

    [Test]
    public void Reset_StartsOpeningAgain()
    {
        Collect(0, 100, Ctx());
        _dir.Reset();
        var s = Collect(0, 23, Ctx());
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Colors(s).Take(3));
    }

    [Test] public void NonPairOrders_AlwaysHaveNoSecondColor()
    { foreach (var x in Collect(0, 400, Ctx(section: 3, seconds: 59))) Assert.AreEqual(-1, x.order.SecondColorIndex);
      _dir.Reset(); foreach (var x in Collect(0, 100, Ctx(fever: true, seconds: 90))) Assert.AreEqual(-1, x.order.SecondColorIndex); }

    [Test] public void Pairs_AppearOnlyAfterSixtySeconds()
    { Assert.IsTrue(Collect(0, 800, Ctx(section: 3, seconds: 60)).Any(x => x.order.SecondColorIndex >= 0)); }

    [Test] public void Pair_OpensARun_AndBridgesThePreviousRunsColor()
    { var s = NoChargers(AfterOpening(Collect(0, 800, Ctx(section: 3, seconds: 60))));
      for (int i = 1; i < s.Count; i++)
      { var o = s[i].order; if (o.SecondColorIndex < 0) continue;
        Assert.AreNotEqual(o.ColorIndex, o.SecondColorIndex);
        Assert.AreEqual(s[i - 1].order.ColorIndex, o.SecondColorIndex);
        Assert.IsFalse(o.Hidden); } }

    [Test]
    public void FirstRunAfterFever_IsNeverAPair()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var dir = new SpawnDirector(new System.Random(seed));
            int step = 0;
            for (int i = 0; i < 200; i++, step++) dir.TryNext(Ctx(step: step, section: 3, seconds: 90), out _);
            for (int i = 0; i < 32; i++, step++) dir.TryNext(Ctx(step: step, fever: true, section: 3, seconds: 90), out _);
            SpawnOrder first = default;
            bool found = false;
            for (int i = 0; i < 100 && !found; i++, step++)
                found = dir.TryNext(Ctx(step: step, section: 3, seconds: 90), out first);
            Assert.IsTrue(found, "seed " + seed);
            Assert.AreEqual(-1, first.SecondColorIndex, "seed " + seed);
        }
    }

    static List<(int step, SpawnOrder order)> NoChargers(List<(int step, SpawnOrder order)> spawns)
    {
        return spawns.Where(x => x.order.Kind != EnemyKind.Charger).ToList();
    }

    [Test] public void NoSpecialKinds_InFever()
    { Assert.IsTrue(Collect(0, 400, Ctx(fever: true, seconds: 120)).All(x => x.order.Kind == EnemyKind.Basic)); }
    [Test] public void Chargers_AreFastPlainAndOutsideRuns()
    { var s = AfterOpening(Collect(0, 4000, Ctx(section: 3, seconds: 120)));
      var chargers = s.Where(x => x.order.Kind == EnemyKind.Charger).ToList(); Assert.Greater(chargers.Count, 20);
      float fast = GameplayTuning.BaseEnemySpeed * GameplayTuning.ChargerSpeedMultiplier;
      foreach (var c in chargers) { Assert.IsFalse(c.order.Hidden); Assert.AreEqual(-1, c.order.SecondColorIndex);
        Assert.GreaterOrEqual(c.order.Speed, fast * (1f - GameplayTuning.EnemySpeedJitter) - 1e-4f);
        Assert.LessOrEqual(c.order.Speed, fast * (1f + GameplayTuning.EnemySpeedJitter) + 1e-4f); }
      var runs = RunsOf(NoChargers(s)); Assert.Greater(runs.Count, 100);
      for (int i = 1; i < runs.Count; i++) Assert.AreNotEqual(runs[i - 1].Color, runs[i].Color);
      foreach (var r in runs.Skip(1).Take(runs.Count - 2))
      { Assert.GreaterOrEqual(r.Length, Stage(120).RunMin); Assert.LessOrEqual(r.Length, Stage(120).RunMax); }
      Assert.AreEqual(Stage(120).ChargerChance, chargers.Count / (double)runs.Count, 0.08); }
    [Test] public void Chargers_UseAllThreeColours()
    { var colours = AfterOpening(Collect(0, 4000, Ctx(section: 3, seconds: 120)))
        .Where(x => x.order.Kind == EnemyKind.Charger).Select(x => x.order.ColorIndex).Distinct().Count();
      Assert.AreEqual(3, colours); }
    [Test] public void Pairs_StillBridgeRunColours_WithChargersBetween()
    { var s = NoChargers(AfterOpening(Collect(0, 4000, Ctx(section: 3, seconds: 120)))); int pairs = 0;
      for (int i = 1; i < s.Count; i++)
      { var o = s[i].order; if (o.SecondColorIndex < 0) continue; pairs++;
        Assert.AreEqual(s[i - 1].order.ColorIndex, o.SecondColorIndex); Assert.AreEqual(EnemyKind.Basic, o.Kind); }
      Assert.Greater(pairs, 20); }
    [Test] public void NoChargers_InBreath()
    { Assert.IsFalse(Collect(0, 4000, Ctx(breath: true, section: 3, seconds: 120)).Any(x => x.order.Kind == EnemyKind.Charger)); }
    [Test]
    public void ChargerReservedInFlow_IsDroppedWhenBreathStarts()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var dir = new SpawnDirector(new System.Random(seed));
            int step = 0;
            for (int i = 0; i < 300; i++, step++) dir.TryNext(Ctx(step: step, section: 3, seconds: 120), out _);
            for (int i = 0; i < 300; i++, step++)
                if (dir.TryNext(Ctx(step: step, breath: true, section: 3, seconds: 120), out var o))
                    Assert.AreNotEqual(EnemyKind.Charger, o.Kind, "seed " + seed + " step " + step);
        }
    }
    [Test]
    public void Fever_DropsAPendingCharger()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var dir = new SpawnDirector(new System.Random(seed));
            int step = 0;
            for (int i = 0; i < 300; i++, step++) dir.TryNext(Ctx(step: step, section: 3, seconds: 120), out _);
            for (int i = 0; i < 32; i++, step++) dir.TryNext(Ctx(step: step, fever: true, section: 3, seconds: 120), out _);
            SpawnOrder first = default;
            bool found = false;
            for (int i = 0; i < 100 && !found; i++, step++)
                found = dir.TryNext(Ctx(step: step, section: 3, seconds: 120), out first);
            Assert.IsTrue(found, "seed " + seed);
            Assert.AreNotEqual(EnemyKind.Charger, first.Kind, "seed " + seed);
        }
    }
    [Test] public void Splitters_AreRunMembers_NeverGrayOrPairs()
    { var s = NoChargers(AfterOpening(Collect(0, 4000, Ctx(section: 3, seconds: 120))));
      var splitters = s.Where(x => x.order.Kind == EnemyKind.Splitter).ToList();
      Assert.Greater(splitters.Count, 0);
      foreach (var x in splitters) { Assert.IsFalse(x.order.Hidden); Assert.AreEqual(-1, x.order.SecondColorIndex); } }

    static SpawnStage Stage(double seconds) => GameplayTuning.SpawnStages[SpawnDirector.StageIndex(seconds)];

    [Test]
    public void StageIndex_PicksTheLastStartedRow()
    {
        Assert.AreEqual(0, SpawnDirector.StageIndex(-5));
        Assert.AreEqual(0, SpawnDirector.StageIndex(0));
        Assert.AreEqual(0, SpawnDirector.StageIndex(14.99));
        Assert.AreEqual(1, SpawnDirector.StageIndex(15));
        Assert.AreEqual(GameplayTuning.SpawnStages.Length - 1, SpawnDirector.StageIndex(3600));
    }

    [Test]
    public void SpawnStages_KeepTheRulesOfTheTable()
    {
        var rows = GameplayTuning.SpawnStages;
        Assert.AreEqual(0.0, rows[0].StartSeconds);
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            if (i > 0) Assert.Greater(r.StartSeconds, rows[i - 1].StartSeconds, "row " + i);
            Assert.GreaterOrEqual(r.IntervalSteps, SpawnDirector.StepsPerBeat, "row " + i);   // pulse cooldown = 1 beat
            Assert.GreaterOrEqual(r.RunMin, 1, "row " + i);
            Assert.GreaterOrEqual(r.RunMax, r.RunMin, "row " + i);
            Assert.LessOrEqual(r.PairChance + r.TripleChance, 1.0, "row " + i);
            foreach (double p in new[] { r.PairChance, r.TripleChance, r.HiddenChance, r.ChordPairChance, r.ChargerChance, r.SplitterChance, r.MixChance })
            { Assert.GreaterOrEqual(p, 0.0, "row " + i); Assert.LessOrEqual(p, 1.0, "row " + i); }
        }
    }

    [Test]
    public void SpawnStages_LearningRowsHaveNoSpecialsAndNoGroups()
    {
        foreach (double t in new[] { 0.0, 20.0 })
        {
            var r = Stage(t);
            Assert.AreEqual(0.0, r.PairChance + r.TripleChance + r.HiddenChance + r.ChordPairChance + r.ChargerChance + r.SplitterChance + r.MixChance);
        }
        Assert.Greater(Stage(0).IntervalSteps, Stage(20).IntervalSteps);
        Assert.Greater(Stage(20).IntervalSteps, Stage(40).IntervalSteps);
    }

    [Test]
    public void Flow_UsesTheRowsInterval()
    {
        foreach (double t in new[] { 10.0, 20.0, 60.0 })
        {
            _dir.Reset();
            var s = AfterOpening(Collect(0, 800, Ctx(seconds: t)));
            Assert.AreEqual(Stage(t).IntervalSteps, MinGap(s), "seconds " + t);
            Assert.LessOrEqual(MaxGap(s), Stage(t).IntervalSteps + GameplayTuning.RunGapSteps, "seconds " + t);
        }
    }

    [Test]
    public void LearningRow_ShortRunsNoSpecials()
    {
        var s = AfterOpening(Collect(0, 800, Ctx(seconds: 10)));
        Assert.IsTrue(s.All(x => x.order.Kind == EnemyKind.Basic && !x.order.Hidden && x.order.SecondColorIndex < 0));
        var runs = RunsOf(s);
        foreach (var r in runs.Skip(1).Take(runs.Count - 2))
        { Assert.GreaterOrEqual(r.Length, 2); Assert.LessOrEqual(r.Length, 3); }
    }

    [Test]
    public void NoSpecials_BeforeTheirRows()
    {
        Assert.IsTrue(Collect(0, 2000, Ctx(seconds: 29)).All(x => !x.order.Hidden));
        _dir.Reset();
        Assert.IsTrue(Collect(0, 2000, Ctx(seconds: 59)).All(x => x.order.Kind == EnemyKind.Basic && x.order.SecondColorIndex < 0));
    }

    [Test]
    public void Hidden_FollowsTheTable()
    {
        var s = NoChargers(AfterOpening(Collect(0, 8000, Ctx(seconds: 120)))).Where(x => x.order.SecondColorIndex < 0).ToList();
        double share = s.Count(x => x.order.Hidden) / (double)s.Count;
        Assert.AreEqual(Stage(120).HiddenChance, share, 0.05);
    }

    [Test]
    public void Pairs_FollowTheTable()
    {
        var s = NoChargers(AfterOpening(Collect(0, 8000, Ctx(seconds: 120))));
        double share = s.Count(x => x.order.SecondColorIndex >= 0) / (double)RunsOf(s).Count;
        Assert.AreEqual(Stage(120).ChordPairChance, share, 0.08);
    }

    [Test]
    public void Splitters_FollowTheTable()
    {
        var eligible = NoChargers(AfterOpening(Collect(0, 8000, Ctx(seconds: 300))))
            .Where(x => !x.order.Hidden && x.order.SecondColorIndex < 0).ToList();
        double share = eligible.Count(x => x.order.Kind == EnemyKind.Splitter) / (double)eligible.Count;
        Assert.AreEqual(Stage(300).SplitterChance, share, 0.04);
    }

    [Test]
    public void LateRows_HaveMoreSpecialsThanEarlyRows()
    {
        int Specials(double t)
        {
            _dir.Reset();
            return Collect(0, 8000, Ctx(seconds: t)).Count(x => x.order.Hidden || x.order.SecondColorIndex >= 0 || x.order.Kind != EnemyKind.Basic);
        }
        int at60 = Specials(60), at180 = Specials(180), at600 = Specials(600);
        Assert.Greater(at180, at60);
        Assert.Greater(at600, at180);
    }

    [Test]
    public void Section_NoLongerChangesSpawns()
    {
        var a = Collect(0, 800, Ctx(section: 0, seconds: 20));
        _dir = new SpawnDirector(new System.Random(1234));
        var b = Collect(0, 800, Ctx(section: 3, seconds: 20));
        CollectionAssert.AreEqual(Steps(a), Steps(b));
        CollectionAssert.AreEqual(Colors(a), Colors(b));
    }

    [Test]
    public void RowChange_MidRun_KeepsSpawning()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var dir = new SpawnDirector(new System.Random(seed));
            int count = 0;
            for (int step = 0; step < 2000; step++)
                if (dir.TryNext(Ctx(step: step, seconds: step * 0.4), out var o)) { count++; Assert.GreaterOrEqual(o.ColorIndex, 0); }
            Assert.Greater(count, 300, "seed " + seed);
        }
    }

    [Test]
    public void Difficulty_StillFollowsItsThresholds()
    {
        Assert.AreEqual(0, SpawnDirector.Difficulty(0));
        Assert.AreEqual(2, SpawnDirector.Difficulty(40));
        Assert.AreEqual(3, SpawnDirector.Difficulty(60));
    }

    [Test]
    public void Count_IsOne_WhereGroupsAreNotAllowed()
    {
        Assert.IsTrue(Collect(0, 2000, Ctx(seconds: 29)).All(x => x.order.Count == 1));
        _dir.Reset();
        Assert.IsTrue(Collect(0, 400, Ctx(fever: true, seconds: 600)).All(x => x.order.Count == 1));
        _dir.Reset();
        Assert.IsTrue(Collect(0, 4000, Ctx(breath: true, seconds: 600)).All(x => x.order.Count == 1));
        _dir.Reset();
        var s = Collect(0, 8000, Ctx(seconds: 600));
        Assert.IsTrue(s.Take(3).All(x => x.order.Count == 1));
        Assert.IsTrue(s.Where(x => x.order.Kind == EnemyKind.Charger).All(x => x.order.Count == 1));
    }

    [Test]
    public void Count_FollowsTheTable()
    {
        var s = NoChargers(AfterOpening(Collect(0, 8000, Ctx(seconds: 600)))).Where(x => x.order.SecondColorIndex < 0).ToList();
        Assert.IsTrue(s.All(x => x.order.Count >= 1 && x.order.Count <= GameplayTuning.MaxGroupSize));
        Assert.AreEqual(Stage(600).PairChance, s.Count(x => x.order.Count == 2) / (double)s.Count, 0.05);
        Assert.AreEqual(Stage(600).TripleChance, s.Count(x => x.order.Count == 3) / (double)s.Count, 0.05);
    }

    [Test]
    public void Count_DoesNotChangeRunLengthOrInterval()
    {
        var s = NoChargers(AfterOpening(Collect(0, 8000, Ctx(seconds: 600))));
        Assert.AreEqual(Stage(600).IntervalSteps, MinGap(s));
        var runs = RunsOf(s);
        foreach (var r in runs.Skip(1).Take(runs.Count - 2))
        { Assert.GreaterOrEqual(r.Length, Stage(600).RunMin); Assert.LessOrEqual(r.Length, Stage(600).RunMax); }
    }

    [Test]
    public void ChordPairSlots_AreAlwaysAlone()   // mates would arrive ahead of the pair and block the bridging colour
    {
        var pairs = Collect(0, 8000, Ctx(seconds: 600)).Where(x => x.order.SecondColorIndex >= 0).ToList();
        Assert.Greater(pairs.Count, 100);
        Assert.IsTrue(pairs.All(x => x.order.Count == 1));
    }

    [Test]
    public void Mate_IsAPlainCopy_SameColourAndSpeed()
    {
        var specials = Collect(0, 8000, Ctx(seconds: 600)).Where(x => x.order.Count > 1 && (x.order.Hidden || x.order.Kind != EnemyKind.Basic)).ToList();
        Assert.Greater(specials.Count, 50);
        foreach (var x in specials)
        {
            var mate = x.order.Mate();
            Assert.IsFalse(mate.Hidden);
            Assert.AreEqual(-1, mate.SecondColorIndex);
            Assert.AreEqual(EnemyKind.Basic, mate.Kind);
            Assert.AreEqual(x.order.ColorIndex, mate.ColorIndex);
            Assert.AreEqual(x.order.Speed, mate.Speed);
        }
    }

    [Test]
    public void MixedSlots_AppearOnlyFromTwoMinutes()
    {
        Assert.IsFalse(Collect(0, 4000, Ctx(seconds: 119)).Any(x => x.order.Mixed));
        _dir.Reset();
        Assert.IsTrue(Collect(0, 4000, Ctx(seconds: 120)).Any(x => x.order.Mixed));
    }

    [Test]
    public void MixedSlot_IsInsideARun_InAnotherColour()
    {
        foreach (double t in new[] { 120.0, 600.0 })
        {
            _dir.Reset();
            var s = NoChargers(AfterOpening(Collect(0, 8000, Ctx(seconds: t))));
            int mixed = 0;
            for (int i = 1; i < s.Count - 1; i++)
            {
                if (!s[i].order.Mixed) continue;
                mixed++;
                int before = i - 1, after = i + 1;
                while (before > 0 && s[before].order.Mixed) before--;
                while (after < s.Count - 1 && s[after].order.Mixed) after++;
                if (s[before].order.Mixed || s[after].order.Mixed) continue;
                // Never the first or the last slot of its run: the run's colour is on both sides.
                Assert.AreEqual(s[before].order.ColorIndex, s[after].order.ColorIndex, "seconds " + t + " slot " + i);
                Assert.AreNotEqual(s[before].order.ColorIndex, s[i].order.ColorIndex, "seconds " + t + " slot " + i);
                Assert.AreEqual(-1, s[i].order.SecondColorIndex);
            }
            Assert.Greater(mixed, 50, "seconds " + t);
        }
    }

    [Test]
    public void MixedSlots_FollowTheTable()
    {
        var s = NoChargers(AfterOpening(Collect(0, 8000, Ctx(seconds: 300))));
        var runs = RunsOf(s);
        int inside = runs.Sum(r => System.Math.Max(0, r.Length - 2));
        Assert.AreEqual(Stage(300).MixChance, s.Count(x => x.order.Mixed) / (double)inside, 0.05);
    }

    [Test]
    public void NoMixedSlots_InBreathOrFever()
    {
        Assert.IsFalse(Collect(0, 8000, Ctx(breath: true, seconds: 600)).Any(x => x.order.Mixed));
        _dir.Reset();
        Assert.IsFalse(Collect(0, 800, Ctx(fever: true, seconds: 600)).Any(x => x.order.Mixed));
    }

    [Test]
    public void MixedSlot_KeepsOneColourPerSlot()
    {
        var groups = Collect(0, 8000, Ctx(seconds: 600)).Where(x => x.order.Mixed && x.order.Count > 1).ToList();
        Assert.Greater(groups.Count, 20);
        foreach (var x in groups) Assert.AreEqual(x.order.ColorIndex, x.order.Mate().ColorIndex);
    }

    [Test]
    public void MateAngles_KeepAGroupApart()
    {
        foreach (double a in new[] { 0.0, 0.5, 0.999 })
        {
            Assert.GreaterOrEqual(SpawnDirector.MateAngleStep(a), 60f);
            Assert.LessOrEqual(SpawnDirector.MateAngleStep(a), 150f);
            foreach (double b in new[] { 0.0, 0.5, 0.999 })
            {
                float thirdFromFirst = SpawnDirector.MateAngleStep(a) + SpawnDirector.MateAngleStep(b);
                Assert.GreaterOrEqual(thirdFromFirst, 120f);
                Assert.LessOrEqual(thirdFromFirst, 300f);
            }
        }
    }

    [Test]
    public void CoreHitRule_OneHealthPerGroup()
    {
        var rule = new CoreHitRule();
        Assert.IsTrue(rule.CostsHealth(0));
        Assert.IsTrue(rule.CostsHealth(0));     // enemies that came alone always cost one each
        Assert.IsTrue(rule.CostsHealth(7));
        Assert.IsTrue(rule.CostsHealth(8));
        Assert.IsFalse(rule.CostsHealth(7));    // the rest of group 7, even after another group hit in between
        Assert.IsFalse(rule.CostsHealth(8));
        rule.Reset();
        Assert.IsTrue(rule.CostsHealth(7));
    }
}
