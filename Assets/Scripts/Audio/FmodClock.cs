using FMODUnity;

/// <summary>
/// The FMOD mixer clock: the DSP clock of the master channel group (the clock <see cref="BeatClock"/>, the one-shot
/// scheduler and ChannelGroup.setDelay use) and the mixer's sample rate. The master channel group handle is taken on the
/// first call and taken again when it has become invalid (FMOD re-initialised). Before FMOD is initialised both return 0.
/// </summary>
public static class FmodClock
{
    static FMOD.ChannelGroup s_master;

    /// <summary>Current DSP clock of the master channel group (0 while FMOD is not initialised).</summary>
    public static ulong MasterDsp()
    {
        if (s_master.hasHandle() && s_master.getDSPClock(out ulong dsp, out _) == FMOD.RESULT.OK)
            return dsp;
        if (!RuntimeManager.IsInitialized ||
            RuntimeManager.CoreSystem.getMasterChannelGroup(out s_master) != FMOD.RESULT.OK)
        {
            s_master.clearHandle();
            return 0;
        }
        return s_master.getDSPClock(out dsp, out _) == FMOD.RESULT.OK ? dsp : 0;
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
