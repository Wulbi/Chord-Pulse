using System.Collections.Generic;
using NUnit.Framework;

public class TensionMeterTests
{
    static EnemyState At(float gap) =>
        new EnemyState { HalfSize = 0.21f, Distance = 0.42f + 0.21f + gap };

    static List<EnemyState> L(params EnemyState[] e) => new List<EnemyState>(e);

    static float T(List<EnemyState> list, int health) =>
        TensionMeter.Compute(list, 0.42f, 4.55f, health, 3);

    [Test] public void NoEnemies_FullHealth_IsZero() { Assert.AreEqual(0f, T(L(), 3)); }

    [Test] public void LostLives_SetAFloor()
    {
        Assert.AreEqual(0.3f, T(L(), 2), 1e-4f);
        Assert.AreEqual(0.75f, T(L(), 1), 1e-4f);
    }

    [Test] public void FarCrowd_AddsNothing()
    {
        var far = new List<EnemyState>();
        for (int i = 0; i < 20; i++) far.Add(At(2.5f + i * 0.05f));
        Assert.AreEqual(0f, T(far, 3));
    }

    [Test] public void OneEnemyAtContact_Is085() { Assert.AreEqual(0.85f, T(L(At(0f)), 3), 1e-4f); }

    [Test] public void MidFieldCrowd_DoesNotSaturate()
    {
        var mid = new List<EnemyState>();
        for (int i = 0; i < 10; i++) mid.Add(At(1.0f + i * 0.08f));
        Assert.Less(T(mid, 3), 0.5f);
    }

    [Test]
    public void CrowdInsideTheBand_RaisesTensionWithoutSaturating()
    {
        float single = T(L(At(0.3f)), 3);
        float crowd = T(L(At(0.3f), At(0.4f)), 3);
        Assert.Greater(crowd, single);
        Assert.Less(crowd, 1f);
    }

    [Test] public void CrowdAtTheCore_Saturates()
    {
        Assert.AreEqual(1f, T(L(At(0f), At(0.1f), At(0.2f), At(0.3f)), 3), 1e-4f);
    }
}
