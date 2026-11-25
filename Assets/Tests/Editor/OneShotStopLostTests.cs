using NUnit.Framework;

public class OneShotStopLostTests
{
    [Test]
    public void ClearedDelayEnd_BeforeTheStop_IsLost()
    {
        Assert.IsTrue(OneShotArming.StopLost(0, 10000, 240, 5000));
    }

    [Test]
    public void DelayEndInPlace_IsNotLost()
    {
        Assert.IsFalse(OneShotArming.StopLost(10240, 10000, 240, 5000));
    }

    [Test]
    public void StopAlreadyPassed_IsNotReapplied()
    {
        Assert.IsFalse(OneShotArming.StopLost(0, 10000, 240, 10000));
    }

    [Test]
    public void NoteWithoutStop_IsNeverReapplied()
    {
        Assert.IsFalse(OneShotArming.StopLost(0, 0, 240, 5000));
    }

    // Task 12 (ruling 4): the voice starts one mixer block after its group's delay start on every bus, and Studio clears
    // the delay end on every bus, so kill chords are stopped like the pattern layers: StopAt has no bus to fork on, and
    // the backend re-applies a lost stop (StopLost) for every note. (RED for this change: the earlier per-bus version
    // StopAt(stop, bus, block) returned 10000 for OutputBus.KillChords, and kill notes were never re-applied.)
    [Test]
    public void StopPoint_IsOneBlockLater()
    {
        Assert.AreEqual(11024UL, OneShotArming.StopAt(10000, 1024));
    }

    [Test]
    public void NoStop_StaysNoStop()
    {
        Assert.AreEqual(0UL, OneShotArming.StopAt(0, 1024));
    }
}
