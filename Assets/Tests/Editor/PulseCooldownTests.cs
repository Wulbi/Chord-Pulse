using NUnit.Framework;

public class PulseCooldownTests
{
    // NUnit shares one fixture instance between tests: build fresh state per test.
    PulseCooldown _cd;

    [SetUp]
    public void SetUp()
    {
        _cd = new PulseCooldown();
    }

    // Times are written relative to a press at beat 4 so the tests follow the tuned cooldown length.
    const double C = GameplayTuning.CooldownBeats;
    const double B = GameplayTuning.CooldownBufferBeats;
    const double End = 4.0 + C;

    [Test] public void FirstPress_Fires() { Assert.IsTrue(_cd.Press(4.0, false)); }

    [Test]
    public void Press_BeforeMusicRuns_DoesNothing()
    { Assert.IsFalse(_cd.Press(-1, false)); Assert.IsTrue(_cd.Press(0.1, false)); }

    [Test]
    public void Press_DuringCooldown_IsIgnored()
    { _cd.Press(4.0, false); Assert.IsFalse(_cd.Press(4.0 + C * 0.5, false)); Assert.IsFalse(_cd.Tick(End, false)); Assert.IsTrue(_cd.Press(End, false)); }

    [Test]
    public void Press_InsideTheBuffer_IsBufferedAndFiresOnce()
    {
        _cd.Press(4.0, false); Assert.IsFalse(_cd.Press(End - B * 0.8, false)); Assert.IsFalse(_cd.Tick(End - B * 0.4, false));
        Assert.IsTrue(_cd.Tick(End, false)); Assert.IsFalse(_cd.Tick(End + 0.01, false)); Assert.IsFalse(_cd.Press(End + C * 0.5, false));
    }

    [Test]
    public void Fever_HasNoCooldown_AndStartsNone()
    { Assert.IsTrue(_cd.Press(4.0, true)); Assert.IsTrue(_cd.Press(4.1, true)); Assert.IsTrue(_cd.Press(4.2, false)); }

    [Test]
    public void FeverStart_ReleasesCooldown_AndBufferedPressFiresOnce()
    { _cd.Press(4.0, false); _cd.Press(End - B * 0.8, false); Assert.IsTrue(_cd.Tick(End - B * 0.6, true)); Assert.IsFalse(_cd.Tick(End - B * 0.4, true)); }

    [Test]
    public void Reset_AllowsPressAtEarlierBeat()
    { _cd.Press(40.0, false); _cd.Reset(); Assert.IsTrue(_cd.Press(0.5, false)); }

    [Test]
    public void Progress_IsClamped_WhenBeatIsBehindTheLock()
    { _cd.Press(4.0, false); Assert.AreEqual(0f, _cd.Progress(3.0, false)); }

    [Test]
    public void Progress_FillsOverTheCooldown()
    {
        Assert.AreEqual(1f, _cd.Progress(3.0, false)); _cd.Press(4.0, false);
        Assert.AreEqual(0.5f, _cd.Progress(4.0 + C * 0.5, false), 1e-4f); Assert.AreEqual(1f, _cd.Progress(End + 1.0, false)); Assert.AreEqual(1f, _cd.Progress(4.0 + C * 0.5, true));
    }
}
