using System;

/// <summary>
/// Combo ladder rules. A rung 0..6: rungs 0..3 are intensity, rungs 4..6 are combo levels 1..3.
/// GuestRung is carried by guest-loop notes and is above any floor.
/// </summary>
public static class ComboLadder
{
    public const int MaxRung = 6;
    public const int GuestRung = 7;

    public static readonly int[] ComboThresholds = { 2, 3, 5, 6, 8, 10 };
    public static readonly double[] FloorSeconds = { 30.0, 120.0 };

    static readonly double[] GuestChances = { 0.10, 0.10, 0.20, 0.30, 0.45, 0.60, 0.75 };

    public static int RungForCombo(int combo)
    {
        int rung = 0;
        for (int i = 0; i < ComboThresholds.Length; i++)
            if (combo >= ComboThresholds[i]) rung = i + 1;
        return rung;
    }

    public static int FloorForSeconds(double seconds)
    {
        int floor = 0;
        for (int i = 0; i < FloorSeconds.Length; i++)
            if (seconds >= FloorSeconds[i]) floor = i + 1;
        return floor;
    }

    public static int Rung(int combo, double seconds)
    {
        return Math.Max(RungForCombo(combo), FloorForSeconds(seconds));
    }

    public static int Intensity(int rung) { return Math.Max(0, Math.Min(3, rung)); }

    public static int ComboLevel(int rung) { return Math.Max(0, rung - 3); }

    public static int RequiredRung(float intensityMin, int comboMin)
    {
        if (comboMin > 0) return 3 + comboMin;
        if (intensityMin > 2f) return 3;
        if (intensityMin > 1f) return 2;
        if (intensityMin > 0f) return 1;
        return 0;
    }

    public static double GuestChance(int rung)
    {
        return GuestChances[Math.Max(0, Math.Min(MaxRung, rung))];
    }

    public static bool GuestsMayRepeat(int rung) { return rung >= 5; }
}

/// <summary>Fade of the layers above the floor after a combo break: 1 - t^2 over Beats.</summary>
public sealed class ComboFade
{
    public const double Beats = 2.0;

    /// <summary>
    /// How many mixer blocks ahead of its curve the fade's volume is sent (SoundManager: Shots.SetFade gets
    /// Scale(beat + LeadBeats(LeadBlocks, ...))). Measured: a SetFade value reaches the mix about 1.5-2 mixer blocks
    /// after it was computed (Docs/Captures/README.md "Combo ladder — final fix wave": the last sent scale 0.0202 heard as a
    /// one-block plateau after the rung drop). Only the sent volume leads; the fade's start, end and Scale are unchanged.
    /// </summary>
    public const int LeadBlocks = 2;

    /// <summary><paramref name="blocks"/> mixer blocks of <paramref name="blockSamples"/> samples at
    /// <paramref name="sampleRate"/> as beats at <paramref name="bpm"/>; 0 when any input is 0 or negative.</summary>
    public static double LeadBeats(int blocks, uint blockSamples, int sampleRate, double bpm)
    {
        if (sampleRate <= 0 || blockSamples == 0 || blocks <= 0 || bpm <= 0)
            return 0.0;
        return blocks * blockSamples / (double)sampleRate * bpm / 60.0;
    }

    double _start;

    public bool Active { get; private set; }
    public int FromRung { get; private set; }
    public int Floor { get; private set; }

    public void Begin(double beat, int fromRung, int floor)
    {
        Active = true; _start = beat; FromRung = fromRung; Floor = floor;
    }

    public void Clear() { Active = false; }

    public bool Finished(double beat) { return Active && beat >= _start + Beats; }

    public float Scale(double beat)
    {
        if (!Active || beat <= _start) return 1f;
        double t = (beat - _start) / Beats;
        if (t > 1.0) t = 1.0;
        return (float)(1.0 - t * t);
    }

    public float Presence(int requiredRung, int heardRung, double beat)
    {
        if (requiredRung > heardRung) return 0f;
        if (!Active || requiredRung <= Floor) return 1f;
        return Scale(beat);
    }
}
