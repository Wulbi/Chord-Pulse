using System.Collections.Generic;
using NUnit.Framework;

/// <summary>Chord progressions (FMOD/Tools/progressions.py, ProgressionSelector, ArrangementState.Progression).</summary>
public class ProgressionTests
{
    [Test]
    public void Table_ProgressionZeroIsTheOriginal_AndEveryRowIsFourKnownChords()
    {
        Assert.Greater(PatternTable.ProgressionCount, 1);
        Assert.LessOrEqual(PatternTable.ProgressionCount, 16, "ProgressionMask is a ushort");
        Assert.AreEqual(PatternTable.ProgressionCount, PatternTable.ProgressionNames.Length);
        Assert.AreEqual(PatternTable.ProgressionCount, PatternTable.ProgressionMood.Length);
        // chord ids 0-3 are the original bars (C Am F G), the KillChordTable rows of the old bar index
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, new[] { ProgressionSelector.ChordAt(0, 0), ProgressionSelector.ChordAt(0, 1),
            ProgressionSelector.ChordAt(0, 2), ProgressionSelector.ChordAt(0, 3) });
        Assert.AreEqual(KillChordTable.Chords, PatternTable.ChordNames.Length);
        for (int p = 0; p < PatternTable.ProgressionCount; p++)
            for (int bar = 0; bar < 4; bar++)
                Assert.Less(ProgressionSelector.ChordAt(p, bar), KillChordTable.Chords, $"progression {p} bar {bar}");
    }

    [Test]
    public void PatternMatch_RespectsTheProgressionMask()
    {
        var clip = new PatternClip(0, 1, 0, 4, 0, 0f, FeverPhaseMask.None, 0, 0, 0, 0, 0xFF, 1 << 3);
        var state = new ArrangementState { Style = 0, Progression = 3 };
        Assert.IsTrue(PatternMatch.Matches(in clip, in state));
        state.Progression = 0;
        Assert.IsFalse(PatternMatch.Matches(in clip, in state));
        state.Progression = 16;
        Assert.IsFalse(PatternMatch.Matches(in clip, in state), "out of range");
    }

    [Test]
    public void EveryPhrasePlaysExactlyOneVersionOfEachPart()
    {
        // expand() splits a part into clips with disjoint progression masks that together cover every progression.
        var byName = new Dictionary<string, int>();
        for (int c = 0; c < PatternTable.Clips.Length; c++)
        {
            string name = PatternTable.ClipNames[c];
            int cut = name.LastIndexOf('P');
            string part = cut > 0 && cut < name.Length - 1 && char.IsDigit(name[cut + 1]) ? name.Substring(0, cut) : name;
            int mask = PatternTable.Clips[c].ProgressionMask;
            Assert.AreNotEqual(0, mask, name);
            byName.TryGetValue(part, out int have);
            Assert.AreEqual(0, have & mask, $"{name}: two versions of {part} for the same progression");
            byName[part] = have | mask;
        }
        int all = (1 << PatternTable.ProgressionCount) - 1;
        foreach (var pair in byName)
            Assert.AreEqual(all, pair.Value & all, $"{pair.Key} misses a progression");
    }

    [Test]
    public void Timeline_ProgressionSwitchesExactlyOnThePhraseBoundary()
    {
        var x = new ArrangementInputs { PhraseStartStep = 32, ProgressionCurrent = 2, ProgressionNext = 5, FeverStart = -1, RungBar = -1 };
        Assert.AreEqual(2, ArrangementTimeline.At(63, in x).Progression);
        Assert.AreEqual(5, ArrangementTimeline.At(64, in x).Progression);
        var y = x;
        y.ProgressionNext = 6;
        Assert.AreEqual(64, ArrangementTimeline.FirstDifferentStep(in x, in y, 40, 80));
    }

    [Test]
    public void Selector_HoldsOneOrTwoPhrases_NeverReturnsToTheOneBefore_UsesManyProgressions()
    {
        var s = new ProgressionSelector(42);
        var used = new HashSet<int> { s.Current };
        int run = 1, previous = -1;
        for (int i = 0; i < 400; i++)
        {
            int before = s.Next;
            int next = s.Advance(breath: i % 7 == 0, fever: i % 11 == 0, tension: (i % 5) / 4f, dreamy: false);
            Assert.AreEqual(before, s.Current);
            used.Add(next);
            if (next == s.Current)
            {
                run++;
                Assert.LessOrEqual(run, 2, "a progression holds at most two phrases");
            }
            else
            {
                Assert.AreNotEqual(previous, next, "no straight return to the progression before");
                previous = s.Current;
                run = 1;
            }
        }
        Assert.Greater(used.Count, PatternTable.ProgressionCount / 2, "the round wanders over most progressions");
    }

    [Test]
    public void Selector_SameSeed_SameProgressions()
    {
        var a = new ProgressionSelector(7);
        var b = new ProgressionSelector(7);
        for (int i = 0; i < 50; i++)
            Assert.AreEqual(a.Advance(false, false, 0.3f, false), b.Advance(false, false, 0.3f, false));
    }
}
