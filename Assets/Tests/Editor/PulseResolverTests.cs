using System.Collections.Generic;
using NUnit.Framework;

public class PulseResolverTests
{
    // NUnit shares one fixture instance between tests: build fresh state per test.
    PulseResolver _resolver;
    List<PulseEvent> _events;

    [SetUp]
    public void SetUp()
    {
        _resolver = new PulseResolver();
        _events = new List<PulseEvent>();
    }

    static EnemyState E(int color, float distance, bool hidden = false) =>
        new EnemyState { ColorIndex = color, Distance = distance, Speed = 0.4f, HalfSize = 0.21f, IsHidden = hidden };

    static EnemyState Pair(int c1, int c2, float distance)
    {
        var e = E(c1, distance);
        e.SecondColorIndex = c2;
        return e;
    }

    static EnemyState Splitter(int color, float distance)
    {
        var e = E(color, distance);
        e.Kind = EnemyKind.Splitter;
        e.HalfSize = 0.21f * GameplayTuning.SplitterScale;
        return e;
    }

    static EnemyState Charger(int color, float distance)
    {
        var e = E(color, distance);
        e.Kind = EnemyKind.Charger;
        e.Speed = 0.8f;
        return e;
    }

    static PulseState P(int color, bool fever = false) =>
        new PulseState { ColorIndex = color, Radius = 0.42f, IsFever = fever };

    static List<EnemyState> L(params EnemyState[] enemies) => new List<EnemyState>(enemies);

    PulseOutcome Run(PulseState pulse, List<EnemyState> enemies)
    {
        PulseOutcome outcome;
        do
        {
            outcome = _resolver.Advance(pulse, enemies, 8f, 5f, 1f / 60f, _events);
        } while (outcome == PulseOutcome.Travelling);
        return outcome;
    }

    List<int> KillPoints()
    {
        var points = new List<int>();
        foreach (var e in _events)
            if (e.Type == PulseEventType.Kill)
                points.Add(e.Points);
        return points;
    }

    [Test] public void SameColor_PiercesAll_PointsCountUp()
    { var e = L(E(0,1f), E(0,2f), E(0,3f)); var p = P(0);
      Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e));
      Assert.AreEqual(0, e.Count); CollectionAssert.AreEqual(new[]{1,2,3}, KillPoints()); Assert.AreEqual(3, p.Kills); }
    [Test] public void WrongColor_Blocks_AndEnemiesBehindSurvive()
    { var e = L(E(0,1f), E(1,2f), E(0,3f)); var p = P(0);
      Assert.AreEqual(PulseOutcome.Blocked, Run(p, e));
      Assert.AreEqual(2, e.Count); Assert.IsTrue(p.HitWrongColor); Assert.AreEqual(1, p.Kills); }
    [Test] public void Hidden_IsRevealedNotKilled_BlocksWithoutWrongColor()
    { var gray = E(1,1f,true); var e = L(gray, E(0,2f)); var p = P(0);
      Assert.AreEqual(PulseOutcome.Blocked, Run(p, e));
      Assert.IsFalse(gray.IsHidden); Assert.AreEqual(2, e.Count); Assert.IsFalse(p.HitWrongColor);
      Assert.AreEqual(PulseEventType.Reveal, _events[0].Type); Assert.AreEqual(0, _events[0].Points); }
    [Test] public void Fever_KillsEveryColorAndHidden()
    { var e = L(E(0,1f), E(1,2f,true), E(2,3f)); var p = P(0, fever: true);
      Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e)); Assert.AreEqual(0, e.Count); Assert.AreEqual(3, p.Kills); }
    [Test] public void SimultaneousContact_MatchingEnemyBesideWrongColorStillDies()
    { var e = L(E(1,2f), E(0,2.005f)); var p = P(0);
      Assert.AreEqual(PulseOutcome.Blocked, Run(p, e)); Assert.AreEqual(1, e.Count); Assert.AreEqual(1, e[0].ColorIndex); }
    [Test] public void EnemyAlreadyBehindTheWave_IsIgnored()
    { var e = L(E(0,1f)); var p = P(0); p.Radius = 2f;
      Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e)); Assert.AreEqual(1, e.Count); }
    [Test] public void LargerEnemy_IsHitEarlier()
    { var big = E(1,3.4f); big.HalfSize = 0.5f; var p = P(0);
      _resolver.Advance(p, L(big), 8f, 5f, 0.3f, _events);   // 반경 0.42 + 2.4 = 2.82 >= 3.4 - 0.5 - 패딩
      Assert.IsTrue(p.HitWrongColor);
      var control = P(0);
      var outcome = _resolver.Advance(control, L(E(1,3.4f)), 8f, 5f, 0.3f, new List<PulseEvent>());
      Assert.IsFalse(control.HitWrongColor); Assert.AreEqual(PulseOutcome.Travelling, outcome); }
    [Test] public void Travelling_AdvancesTheRadius()
    { var p = P(0); _resolver.Advance(p, L(), 8f, 5f, 0.1f, _events);
      Assert.AreEqual(0.42f + 0.8f, p.Radius, 1e-4f); }
    [Test] public void HugeFrame_StillResolvesInOrder()
    { var e = L(E(0,3f), E(1,2f), E(0,1f)); var p = P(0);
      Assert.AreEqual(PulseOutcome.Blocked, _resolver.Advance(p, e, 8f, 5f, 10f, _events));
      Assert.AreEqual(2, e.Count); Assert.AreEqual(1, p.Kills); }
    [Test] public void Pair_HitByFirstColor_StripsAndBlocks()
    { var pair = Pair(0, 1, 1f); var e = L(pair); var p = P(0);
      Assert.AreEqual(PulseOutcome.Blocked, Run(p, e));
      Assert.AreEqual(1, e.Count); Assert.AreEqual(1, pair.ColorIndex); Assert.AreEqual(-1, pair.SecondColorIndex);
      Assert.AreEqual(0, pair.StrippedColorIndex); Assert.IsFalse(p.HitWrongColor); Assert.AreEqual(0, p.Kills);
      Assert.AreEqual(PulseEventType.Strip, _events[0].Type); Assert.AreEqual(0, _events[0].Points); }
    [Test] public void Pair_HitBySecondColor_StripsAndKeepsTheFirst()
    { var pair = Pair(0, 1, 1f); var p = P(1);
      Assert.AreEqual(PulseOutcome.Blocked, Run(p, L(pair)));
      Assert.AreEqual(0, pair.ColorIndex); Assert.AreEqual(1, pair.StrippedColorIndex); Assert.IsFalse(pair.IsChordPair); }
    [Test] public void Pair_HitByThirdColor_IsWrongColor()
    { var pair = Pair(0, 1, 1f); var p = P(2);
      Assert.AreEqual(PulseOutcome.Blocked, Run(p, L(pair))); Assert.IsTrue(p.HitWrongColor); Assert.IsTrue(pair.IsChordPair); }
    [Test] public void Pair_TwoPulsesInEitherOrder_Kill()
    { var pair = Pair(0, 1, 1f); var e = L(pair);
      Run(P(1), e); _events.Clear(); var p = P(0);
      Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e));
      Assert.AreEqual(0, e.Count); Assert.AreEqual(1, p.Kills); Assert.AreEqual(1, pair.StrippedColorIndex); }
    [Test] public void Pair_FirstColourThenSecond_Kills()
    { var pair = Pair(0, 1, 1f); var e = L(pair);
      Run(P(0), e); _events.Clear(); var p = P(1);
      Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e));
      Assert.AreEqual(0, e.Count); Assert.AreEqual(1, p.Kills); Assert.AreEqual(0, pair.StrippedColorIndex); }
    [Test] public void StrippedPair_HitByItsPoppedColor_IsWrongColor()
    { var pair = Pair(0, 1, 1f); var e = L(pair);
      Run(P(0), e); var p = P(0);
      Assert.AreEqual(PulseOutcome.Blocked, Run(p, e)); Assert.IsTrue(p.HitWrongColor); Assert.AreEqual(1, e.Count); }
    [Test] public void Pair_BlocksThePulse_EnemyBehindSurvives()
    { var e = L(Pair(0, 1, 1f), E(0, 2f)); var p = P(0);
      Assert.AreEqual(PulseOutcome.Blocked, Run(p, e)); Assert.AreEqual(2, e.Count); Assert.AreEqual(0, p.Kills); }
    [Test] public void Pair_AndMatchingEnemyInTheSameBand_StripAndKill()
    { var e = L(Pair(0, 1, 2f), E(0, 2.005f)); var p = P(0);
      Assert.AreEqual(PulseOutcome.Blocked, Run(p, e)); Assert.AreEqual(1, e.Count); Assert.AreEqual(1, p.Kills); }
    [Test] public void Fever_KillsAnIntactPairOutright()
    { var pair = Pair(0, 1, 1f); var e = L(pair); var p = P(0, fever: true);
      Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e));
      Assert.AreEqual(0, e.Count); Assert.AreEqual(1, p.Kills); Assert.IsTrue(pair.IsChordPair); }
    [Test] public void Splitter_Killed_LeavesTwoImmuneChildren()
    { var parent = Splitter(0, 1f); var e = L(parent); var p = P(0);
      Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e));
      Assert.AreEqual(2, e.Count); Assert.AreEqual(1, p.Kills);
      foreach (var c in e) { Assert.AreEqual(EnemyKind.Basic, c.Kind); Assert.AreEqual(0, c.ColorIndex);
        Assert.AreEqual(parent.Distance, c.Distance, 1e-4f); Assert.AreEqual(GameplayTuning.BaseEnemySpeed, c.Speed);
        Assert.AreEqual(0.21f * GameplayTuning.SplitChildScale, c.HalfSize, 1e-4f); Assert.AreSame(p, c.ImmuneTo); }
      Assert.AreEqual(PulseEventType.Split, _events[0].Type); Assert.AreSame(parent, _events[0].Parent);
      Assert.AreEqual(PulseEventType.Split, _events[1].Type); Assert.AreNotSame(_events[0].Enemy, _events[1].Enemy);
      Assert.AreEqual(PulseEventType.Kill, _events[2].Type); Assert.AreSame(parent, _events[2].Enemy); Assert.AreEqual(1, _events[2].Points); }
    [Test] public void SplitChildren_DieToTheNextPulse()
    { var e = L(Splitter(0, 1f)); Run(P(0), e); var p = P(0);
      Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e)); Assert.AreEqual(0, e.Count); Assert.AreEqual(2, p.Kills); }
    [Test] public void Splitter_InsideARun_PulseKeepsPiercing()
    { var e = L(E(0, 1f), Splitter(0, 2f), E(0, 3f)); var p = P(0);
      Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e)); Assert.AreEqual(3, p.Kills); Assert.AreEqual(2, e.Count); }
    [Test] public void Splitter_WrongColor_BlocksWithoutSplitting()
    { var e = L(Splitter(1, 1f)); var p = P(0);
      Assert.AreEqual(PulseOutcome.Blocked, Run(p, e)); Assert.AreEqual(1, e.Count); Assert.IsTrue(p.HitWrongColor); }
    [Test] public void Fever_SplitsToo_AndChildrenSurviveThatPulse()
    { var e = L(Splitter(1, 1f)); var p = P(0, fever: true);
      Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e)); Assert.AreEqual(2, e.Count); Assert.AreEqual(1, p.Kills); }
    [Test] public void Charger_WrongColor_SpeedsUpAndBlocks()
    { var c = Charger(1, 1f); var p = P(0);
      Assert.AreEqual(PulseOutcome.Blocked, Run(p, L(c))); Assert.IsTrue(p.HitWrongColor);
      Assert.AreEqual(0.8f * GameplayTuning.ChargerBoostMultiplier, c.Speed, 1e-4f); Assert.AreEqual(1, c.Boosts);
      Assert.AreEqual(PulseEventType.Boost, _events[0].Type); Assert.AreSame(c, _events[0].Enemy); }
    [Test] public void Charger_BoostIsCapped()
    { var c = Charger(1, 1f); var e = L(c);
      Run(P(0), e); Run(P(0), e); _events.Clear(); Run(P(0), e);
      Assert.AreEqual(GameplayTuning.ChargerMaxBoosts, c.Boosts);
      Assert.AreEqual(0.8f * GameplayTuning.ChargerBoostMultiplier * GameplayTuning.ChargerBoostMultiplier, c.Speed, 1e-4f);
      Assert.AreEqual(0, _events.Count); }
    [Test] public void Charger_RightColor_DiesWithoutBoost()
    { var c = Charger(0, 1f); var e = L(c); var p = P(0);
      Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e)); Assert.AreEqual(0, e.Count); Assert.AreEqual(0, c.Boosts); }
    [Test] public void Fever_NeverBoostsACharger()
    { var c = Charger(1, 1f); var e = L(c); var p = P(0, fever: true);
      Assert.AreEqual(PulseOutcome.ReachedEdge, Run(p, e)); Assert.AreEqual(0, e.Count); Assert.AreEqual(0, c.Boosts);
      foreach (var ev in _events) Assert.AreNotEqual(PulseEventType.Boost, ev.Type); }
    [Test] public void GrayCharger_Reveal_IsNotABoost()
    { var c = Charger(1, 1f); c.IsHidden = true; var e = L(c); var p = P(0);
      Assert.AreEqual(PulseOutcome.Blocked, Run(p, e));
      Assert.IsFalse(c.IsHidden); Assert.AreEqual(0, c.Boosts); Assert.AreEqual(0.8f, c.Speed, 1e-4f); Assert.IsFalse(p.HitWrongColor);
      Assert.AreEqual(1, e.Count);
      foreach (var ev in _events) Assert.AreNotEqual(PulseEventType.Boost, ev.Type); }
    [Test] public void BasicEnemy_WrongColor_NeverSpeedsUp()
    { var b = E(1, 1f); float speed = b.Speed; Run(P(0), L(b)); Assert.AreEqual(speed, b.Speed); Assert.AreEqual(0, b.Boosts); }
    [Test] public void NoEnemies_ZeroDelta_KeepsTravelling()
    { var p = P(0); Assert.AreEqual(PulseOutcome.Travelling, _resolver.Advance(p, L(), 8f, 5f, 0f, _events)); Assert.AreEqual(0, _events.Count); }
}
