using FMODUnity;

/// <summary>
/// The FMOD mixer clock: the DSP clock of the master channel group (the clock <see cref="BeatClock"/>, the one-shot
/// scheduler and ChannelGroup.setDelay use) and the mixer's sample rate. The master channel group handle is taken on the
/// first call and taken again when it has become invalid (FMOD re-initialised). Before FMOD is initialised both return 0.
/// </summary>
public static class FmodClock
{
    static FMOD.ChannelGroup s_master;
    static ulong s_lastDsp; // last good read; 0 until the first one and after FMOD was found not initialised

    /// <summary>
    /// Current DSP clock of the master channel group (0 while FMOD is not initialised). A failed read while FMOD is
    /// initialised returns the last good value instead of 0, so a caller never sees the clock jump back to the start.
    /// </summary>
    public static ulong MasterDsp()
    {
        if (s_master.hasHandle() && s_master.getDSPClock(out ulong dsp, out _) == FMOD.RESULT.OK)
            return s_lastDsp = dsp;
        if (!RuntimeManager.IsInitialized)
        {
            s_master.clearHandle();
            s_lastDsp = 0;
            return 0;
        }
        if (RuntimeManager.CoreSystem.getMasterChannelGroup(out s_master) != FMOD.RESULT.OK)
        {
            s_master.clearHandle();
            return s_lastDsp;
        }
        return s_master.getDSPClock(out dsp, out _) == FMOD.RESULT.OK ? s_lastDsp = dsp : s_lastDsp;
    }

    /// <summary>Sample rate of the FMOD software mixer (0 while FMOD is not initialised).</summary>
    public static int SampleRate()
    {
        if (!RuntimeManager.IsInitialized ||
            RuntimeManager.CoreSystem.getSoftwareFormat(out int rate, out _, out _) != FMOD.RESULT.OK)
            return 0;
        return rate;
    }
}
