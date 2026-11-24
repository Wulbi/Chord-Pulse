using NUnit.Framework;
using UnityEngine;

// MusicMix -> bus volume. The dB table is the MusicMix automation of the music event in FMOD/ChordPulse (Music Bed group
// volume and Harmony gain): -80 / -18 / -9 / -3.5 / 0 dB at 0, .25, .5, .75, 1.
public class MixGainTests
{
    static float Db(float gain) => 20f * Mathf.Log10(gain);

    [Test]
    public void MusicMixGain_EndPoints()
    {
        Assert.AreEqual(0f, SoundManager.MusicMixGain(0f));
        Assert.AreEqual(0f, SoundManager.MusicMixGain(-1f));
        Assert.AreEqual(1f, SoundManager.MusicMixGain(1f), 1e-6f);
        Assert.AreEqual(1f, SoundManager.MusicMixGain(2f), 1e-6f);
    }

    [TestCase(0.25f, -18f)]
    [TestCase(0.5f, -9f)]
    [TestCase(0.75f, -3.5f)]
    public void MusicMixGain_TablePoints(float mix, float db)
    {
        Assert.AreEqual(db, Db(SoundManager.MusicMixGain(mix)), 0.01f);
    }

    [Test]
    public void MusicMixGain_InterpolatesLinearlyInDb()
    {
        Assert.AreEqual(-13.5f, Db(SoundManager.MusicMixGain(0.375f)), 0.01f);
        Assert.AreEqual(-49f, Db(SoundManager.MusicMixGain(0.125f)), 0.01f);
    }

    [Test]
    public void MusicMixGain_IsMonotonic()
    {
        float last = 0f;
        for (int i = 1; i <= 100; i++)
        {
            float gain = SoundManager.MusicMixGain(i / 100f);
            Assert.Greater(gain, last, "mix " + i / 100f);
            last = gain;
        }
    }

    [Test]
    public void KillChordGain_KeepsItsTable()
    {
        Assert.AreEqual(0f, SoundManager.KillChordGain(0f));
        Assert.AreEqual(-9f, Db(SoundManager.KillChordGain(0.5f)), 0.01f);
        Assert.AreEqual(1f, SoundManager.KillChordGain(1f), 1e-6f);
    }
}
