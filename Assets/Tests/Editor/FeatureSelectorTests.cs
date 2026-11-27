using NUnit.Framework;

public class FeatureSelectorTests
{
    const int Seed = 4711;

    // Share of phrases with a guest (first 2 phrases skipped, no breath phrases, energy 1).
    static double Share(int rung, int phrases = 4000)
    {
        var selector = new FeatureSelector(Seed);
        var phrase = new SectionSelector.Phrase();
        int guests = 0, counted = 0;
        for (int i = 0; i < phrases; i++)
        {
            var next = selector.Advance(phrase, 1, allowed: true, rung: rung);
            if (i < 2)
                continue;
            counted++;
            if (next != null)
                guests++;
        }
        return (double)guests / counted;
    }

    [Test]
    public void Chance_FollowsTheRung()
    {
        double r0 = Share(0), r3 = Share(3), r4 = Share(4);
        UnityEngine.Debug.Log($"FeatureSelector shares: rung0={r0:F4} rung3={r3:F4} rung4={r4:F4}");
        Assert.AreEqual(0.10, r0, 0.03, "rung 0");
        Assert.AreEqual(0.30 / 1.30, r3, 0.03, "rung 3");
        Assert.AreEqual(0.45 / 1.45, r4, 0.03, "rung 4");
    }

    [Test]
    public void HighRungs_AllowGuestsInARow_NeverTheSameLoopTwice()
    {
        var selector = new FeatureSelector(Seed);
        var phrase = new SectionSelector.Phrase();
        int lastId = 0, guests = 0, counted = 0, inARow = 0;
        for (int i = 0; i < 4000; i++)
        {
            var next = selector.Advance(phrase, 1, allowed: true, rung: 6);
            if (selector.Current != null && next != null)
                inARow++;
            if (next != null)
            {
                Assert.AreNotEqual(lastId, next.Id, $"phrase {i}: same loop twice in a row");
                lastId = next.Id;
            }
            else
                lastId = 0;
            if (i < 2)
                continue;
            counted++;
            if (next != null)
                guests++;
        }
        double share = (double)guests / counted;
        UnityEngine.Debug.Log($"FeatureSelector rung 6 share={share:F4}, guests in a row={inARow}");
        Assert.AreEqual(0.75, share, 0.03);
        Assert.Greater(inARow, 0);
    }

    [Test]
    public void RungFive_AllowsGuestsInARow()
    {
        var selector = new FeatureSelector(Seed);
        var phrase = new SectionSelector.Phrase();
        int guests = 0, counted = 0, inARow = 0;
        for (int i = 0; i < 4000; i++)
        {
            var next = selector.Advance(phrase, 1, allowed: true, rung: 5);
            if (selector.Current != null && next != null)
                inARow++;
            if (i < 2)
                continue;
            counted++;
            if (next != null)
                guests++;
        }
        double share = (double)guests / counted;
        UnityEngine.Debug.Log($"FeatureSelector rung 5 share={share:F4}, guests in a row={inARow}");
        Assert.AreEqual(0.60, share, 0.03);
        Assert.Greater(inARow, 0);
    }

    [Test]
    public void LowRungs_NeverTwoInARow()
    {
        var selector = new FeatureSelector(Seed);
        var phrase = new SectionSelector.Phrase();
        for (int i = 0; i < 4000; i++)
        {
            var next = selector.Advance(phrase, 1, allowed: true, rung: 4);
            if (selector.Current != null)
                Assert.IsNull(next, $"phrase {i}: guest after a guest");
        }
    }

    [Test]
    public void CancelCurrent_ClearsOnlyTheCurrentLoop()
    {
        var selector = new FeatureSelector(Seed);
        var phrase = new SectionSelector.Phrase();
        // Rung 6 (75%): find a phrase where a guest is heard and the next one is queued too.
        for (int i = 0; i < 2000; i++)
        {
            var next = selector.Advance(phrase, 1, allowed: true, rung: 6);
            if (selector.Current != null && next != null)
            {
                selector.CancelCurrent();
                Assert.IsNull(selector.Current);
                Assert.AreSame(next, selector.Next);
                return;
            }
        }
        Assert.Fail("never saw Current and Next both set");
    }

    [Test]
    public void FirstTwoPhrases_Breath_AndNotAllowed_StillGiveNoGuest()
    {
        var phrase = new SectionSelector.Phrase();
        var breath = new SectionSelector.Phrase { Breath = true };
        for (int seed = 1; seed <= 50; seed++)
        {
            var s = new FeatureSelector(seed);
            // Advance runs one phrase ahead: the first call queues nothing and the first two phrases heard
            // (Current after calls 1 and 2) carry no guest.
            Assert.IsNull(s.Advance(phrase, 1, true, 6), $"seed {seed}: first call");
            Assert.IsNull(s.Current, $"seed {seed}: phrase 0");
            s.Advance(phrase, 1, true, 6);
            Assert.IsNull(s.Current, $"seed {seed}: phrase 1");
        }
        var b = new FeatureSelector(Seed);
        var n = new FeatureSelector(Seed);
        for (int i = 0; i < 500; i++)
        {
            Assert.IsNull(b.Advance(breath, 1, true, 6), $"breath {i}");
            Assert.IsNull(n.Advance(phrase, 1, false, 6), $"not allowed {i}");
        }
    }
}
