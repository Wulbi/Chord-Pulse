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

    static List<(int Color, int Length)> RunsOf(List<(int step, SpawnOrder order)> spawns)
    {
        var runs = new List<(int Color, int Length)>();
        foreach (var x in spawns)
        {
            int c = x.order.ColorIndex;
            if (runs.Count > 0 && runs[runs.Count - 1].Color == c)
                runs[runs.Count - 1] = (c, runs[runs.Count - 1].Length + 1);
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
    public void Flow_Level3_SpawnsEveryBeatWithinARun()
    {
        var s = AfterOpening(Collect(0, 400, Ctx(section: 3, seconds: 60)));
        Assert.AreEqual(GameplayTuning.FlowIntervalSteps[3], MinGap(s));
        Assert.LessOrEqual(MaxGap(s), GameplayTuning.FlowIntervalSteps[3] + GameplayTuning.RunGapSteps);
    }

    [Test]
    public void Runs_AreOneColor_AndNeighbouringRunsDiffer()
    {
        var runs = RunsOf(AfterOpening(Collect(0, 800, Ctx(section: 3, seconds: 60))));
        Assert.Greater(runs.Count, 5);
        foreach (var r in runs.Skip(1).Take(runs.Count - 2))
        {
            Assert.GreaterOrEqual(r.Length, GameplayTuning.RunMin[3]);
            Assert.LessOrEqual(r.Length, GameplayTuning.RunMax[3]);
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
        var all = Collect(0, 400, Ctx(section: 3, seconds: 60));
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
    { var s = AfterOpening(Collect(0, 800, Ctx(section: 3, seconds: 60)));
      for (int i = 1; i < s.Count; i++)
      { var o = s[i].order; if (o.SecondColorIndex < 0) continue;
        Assert.AreNotEqual(o.ColorIndex, o.SecondColorIndex);
        Assert.AreEqual(s[i - 1].order.ColorIndex, o.SecondColorIndex);
        Assert.IsFalse(o.Hidden); } }

    [Test] public void Pairs_AreRoughlyHalfOfRunStarts()
    { var s = AfterOpening(Collect(0, 4000, Ctx(section: 3, seconds: 60)));
      int runs = RunsOf(s).Count; int pairs = s.Count(x => x.order.SecondColorIndex >= 0);
      Assert.Greater(runs, 100); Assert.Greater(pairs, runs * 0.3); Assert.Less(pairs, runs * 0.7); }

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

    [Test] public void NoSpecialKinds_BeforeTheirStartTimes()
    { Assert.IsTrue(Collect(0, 2000, Ctx(section: 3, seconds: 69)).All(x => x.order.Kind == EnemyKind.Basic));
      _dir.Reset(); Assert.IsFalse(Collect(0, 2000, Ctx(section: 3, seconds: 79)).Any(x => x.order.Kind == EnemyKind.Splitter)); }
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
      { Assert.GreaterOrEqual(r.Length, GameplayTuning.RunMin[3]); Assert.LessOrEqual(r.Length, GameplayTuning.RunMax[3]); }
      Assert.Greater(chargers.Count, runs.Count * 0.15); Assert.Less(chargers.Count, runs.Count * 0.45); }
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
      Assert.Greater(splitters.Count, s.Count * 0.05); Assert.Less(splitters.Count, s.Count * 0.2);
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
            foreach (double p in new[] { r.PairChance, r.TripleChance, r.HiddenChance, r.ChordPairChance, r.ChargerChance, r.SplitterChance })
            { Assert.GreaterOrEqual(p, 0.0, "row " + i); Assert.LessOrEqual(p, 1.0, "row " + i); }
        }
    }

    [Test]
    public void SpawnStages_LearningRowsHaveNoSpecialsAndNoGroups()
    {
        foreach (double t in new[] { 0.0, 20.0 })
        {
            var r = Stage(t);
            Assert.AreEqual(0.0, r.PairChance + r.TripleChance + r.HiddenChance + r.ChordPairChance + r.ChargerChance + r.SplitterChance);
        }
        Assert.Greater(Stage(0).IntervalSteps, Stage(20).IntervalSteps);
        Assert.Greater(Stage(20).IntervalSteps, Stage(40).IntervalSteps);
    }

    [Test]
    public void DensityLevel_CombinesDifficultyAndSection()
    {
        Assert.AreEqual(0, SpawnDirector.DensityLevel(0, 0));
        Assert.AreEqual(1, SpawnDirector.DensityLevel(2, 0));
        Assert.AreEqual(2, SpawnDirector.DensityLevel(0, 40));
        Assert.AreEqual(3, SpawnDirector.DensityLevel(3, 60));
    }
}
