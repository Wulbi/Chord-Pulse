using UnityEngine;

/// <summary>
/// The player's volume settings (SoundManager.MusicMix / KillChordMix, 0..1), kept in PlayerPrefs between runs.
/// Nothing is stored until the player changes a slider, so until then SoundManager's inspector values apply.
/// </summary>
public static class VolumeSettings
{
    const string MusicKey = "ChordPulse.Volume.Music";
    const string KillChordKey = "ChordPulse.Volume.KillChord";

    /// <summary>Applies the saved values, if any, to the sound manager.</summary>
    public static void Apply(SoundManager sound)
    {
        if (sound == null)
            return;
        if (PlayerPrefs.HasKey(MusicKey))
            sound.MusicMix = PlayerPrefs.GetFloat(MusicKey);
        if (PlayerPrefs.HasKey(KillChordKey))
            sound.KillChordMix = PlayerPrefs.GetFloat(KillChordKey);
    }

    public static void Save(float musicMix, float killChordMix)
    {
        PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(musicMix));
        PlayerPrefs.SetFloat(KillChordKey, Mathf.Clamp01(killChordMix));
        PlayerPrefs.Save();
    }
}
