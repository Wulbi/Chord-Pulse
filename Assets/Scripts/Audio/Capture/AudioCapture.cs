using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using System.Threading;
using FMODUnity;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Records what FMOD sends to the output (a DSP at the tail of the master channel group) into a 32-bit float stereo WAV.
/// The read callback runs on the mixer thread: it only copies into preallocated buffers (no allocations, no Unity API).
/// A hidden driver drains the ring buffer to disk from the main thread every frame.
/// Used for level/timing measurements (Docs/Captures/README.md); not part of gameplay.
/// </summary>
public static class AudioCapture
{
    const int RingSeconds = 8;
    const int Channels = 2;
    const int MaxBlockFloats = 16384;
    const uint PluginSdkVersion = 110; // FMOD_PLUGIN_SDK_VERSION of FMOD 2.02 (not exposed by the C# wrapper)

    static readonly FMOD.DSP_READ_CALLBACK s_readCallback = OnRead; // static field keeps the delegate alive

    static FMOD.DSP s_dsp;
    static FMOD.ChannelGroup s_master;
    static float[] s_ring;
    static float[] s_scratch;
    static int s_ringFrames;
    static long s_written;      // frames written by the mixer thread (monotonic)
    static long s_read;         // frames drained by the main thread
    static long s_dropped;      // frames lost because the ring was full
    // Mixer clock: (Stopwatch seconds, capture frame) at the start of every mixer block. Lets us convert a Stopwatch
    // stamp (the music conductor's beat times) to a capture frame without depending on Unity's frame rate.
    const int MaxBlocks = 262144;
    static readonly double s_tickSeconds = 1.0 / Stopwatch.Frequency;
    static double[] s_blockSeconds;
    static long[] s_blockFrame;
    static int s_blockCount;
    static double s_fitSlope, s_fitIntercept, s_fitT0;
    static volatile bool s_active;
    static FileStream s_file;
    static byte[] s_chunk;
    static string s_path;
    static Driver s_driver;

    public static int SampleRate { get; private set; }
    public static bool IsCapturing => s_active;
    /// <summary>Frames the mixer has delivered since <see cref="Begin"/> (the capture's own clock).</summary>
    public static long FramesCaptured => Interlocked.Read(ref s_written);
    public static long FramesDropped => Interlocked.Read(ref s_dropped);

    public static bool Begin(string wavPath)
    {
        if (s_active)
            End();

        var core = RuntimeManager.CoreSystem;
        if (core.getSoftwareFormat(out int rate, out _, out _) != FMOD.RESULT.OK)
            return false;
        SampleRate = rate;

        s_ringFrames = rate * RingSeconds;
        s_ring = new float[s_ringFrames * Channels];
        s_scratch = new float[MaxBlockFloats];
        s_blockSeconds = new double[MaxBlocks];
        s_blockFrame = new long[MaxBlocks];
        s_blockCount = 0;
        s_chunk = new byte[16384 * Channels * sizeof(float)];
        s_written = s_read = s_dropped = 0;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(wavPath)));
        s_path = wavPath;
        s_file = new FileStream(wavPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        WriteHeader(s_file, 0, rate);

        var description = new FMOD.DSP_DESCRIPTION
        {
            pluginsdkversion = PluginSdkVersion,
            name = new byte[32],
            version = 0x00010000,
            numinputbuffers = 1,
            numoutputbuffers = 1,
            read = s_readCallback,
        };
        Encoding.ASCII.GetBytes("CPCapture").CopyTo(description.name, 0);

        if (core.createDSP(ref description, out s_dsp) != FMOD.RESULT.OK ||
            core.getMasterChannelGroup(out s_master) != FMOD.RESULT.OK ||
            s_master.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.TAIL, s_dsp) != FMOD.RESULT.OK)
        {
            s_file.Dispose();
            s_file = null;
            return false;
        }

        s_active = true;
        var host = new GameObject("AudioCaptureDriver") { hideFlags = HideFlags.HideAndDontSave };
        s_driver = host.AddComponent<Driver>();
        return true;
    }

    /// <summary>Stops the capture, finishes the file and returns its path.</summary>
    public static string End()
    {
        if (!s_active)
            return s_path;
        s_active = false;
        s_master.removeDSP(s_dsp);
        s_dsp.release();
        Drain();

        FitMixerClock();
        s_file.Seek(0, SeekOrigin.Begin);
        WriteHeader(s_file, s_read, SampleRate);
        s_file.Dispose();
        s_file = null;

        if (s_driver != null)
            UnityEngine.Object.Destroy(s_driver.gameObject);
        s_driver = null;
        if (s_dropped > 0)
            Debug.LogWarning("[AudioCapture] Ring buffer overflowed, " + s_dropped + " frames lost: " + s_path);
        return s_path;
    }

    // Least-squares line frame = intercept + slope * seconds over all mixer blocks (blocks arrive in bursts, so single
    // block stamps jitter; the fit does not).
    static void FitMixerClock()
    {
        int n = s_blockCount;
        if (n < 2)
            return;
        double t0 = s_blockSeconds[0], sumT = 0, sumF = 0;
        for (int i = 0; i < n; i++)
        {
            sumT += s_blockSeconds[i] - t0;
            sumF += s_blockFrame[i];
        }
        double meanT = sumT / n, meanF = sumF / n, num = 0, den = 0;
        for (int i = 0; i < n; i++)
        {
            double dt = s_blockSeconds[i] - t0 - meanT;
            num += dt * (s_blockFrame[i] - meanF);
            den += dt * dt;
        }
        s_fitSlope = den > 0 ? num / den : SampleRate;
        s_fitIntercept = meanF - s_fitSlope * meanT;
        s_fitT0 = t0;
    }

    /// <summary>Capture frame (fractional) of a Stopwatch time in seconds. Valid after <see cref="End"/>.</summary>
    public static double FrameAt(double stopwatchSeconds) => s_fitIntercept + s_fitSlope * (stopwatchSeconds - s_fitT0);

    sealed class Driver : MonoBehaviour
    {
        void Update() => Drain();
    }

    static void Drain()
    {
        if (s_file == null)
            return;
        const int frameBytes = Channels * sizeof(float);
        long available = Interlocked.Read(ref s_written) - s_read;
        while (available > 0)
        {
            int frames = (int)Math.Min(available, s_chunk.Length / frameBytes);
            int start = (int)(s_read % s_ringFrames);
            int first = Math.Min(frames, s_ringFrames - start);
            Buffer.BlockCopy(s_ring, start * frameBytes, s_chunk, 0, first * frameBytes);
            if (first < frames)
                Buffer.BlockCopy(s_ring, 0, s_chunk, first * frameBytes, (frames - first) * frameBytes);
            s_file.Write(s_chunk, 0, frames * frameBytes);
            Volatile.Write(ref s_read, s_read + frames);
            available -= frames;
        }
    }

    // Mixer thread. Passes audio through untouched and copies the first two channels into the ring.
    [AOT.MonoPInvokeCallback(typeof(FMOD.DSP_READ_CALLBACK))]
    static FMOD.RESULT OnRead(ref FMOD.DSP_STATE state, IntPtr inBuffer, IntPtr outBuffer, uint length, int inChannels, ref int outChannels)
    {
        outChannels = inChannels;
        int frames = (int)length;
        int floats = frames * inChannels;
        if (floats > MaxBlockFloats || s_ring == null)
            return FMOD.RESULT.OK; // block larger than the scratch buffer (not expected): skip, never allocate here

        // Read the block once into scratch, pass it through, and keep it for the ring.
        Marshal.Copy(inBuffer, s_scratch, 0, floats);
        if (inBuffer != outBuffer)
            Marshal.Copy(s_scratch, 0, outBuffer, floats);

        if (!s_active)
            return FMOD.RESULT.OK;

        long written = s_written;
        long free = s_ringFrames - (written - Volatile.Read(ref s_read));
        if (free < frames)
        {
            Interlocked.Add(ref s_dropped, frames);
            return FMOD.RESULT.OK;
        }

        if (s_blockCount < MaxBlocks)
        {
            s_blockSeconds[s_blockCount] = Stopwatch.GetTimestamp() * s_tickSeconds;
            s_blockFrame[s_blockCount] = written;
            s_blockCount++;
        }
        int start = (int)(written % s_ringFrames);
        for (int i = 0; i < frames; i++)
        {
            int dst = ((start + i) % s_ringFrames) * Channels;
            s_ring[dst] = s_scratch[i * inChannels];
            s_ring[dst + 1] = inChannels > 1 ? s_scratch[i * inChannels + 1] : s_scratch[i * inChannels];
        }
        Interlocked.Add(ref s_written, frames);
        return FMOD.RESULT.OK;
    }

    static void WriteHeader(Stream stream, long frames, int rate)
    {
        long dataBytes = frames * Channels * sizeof(float);
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write((uint)(36 + dataBytes));
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16u);
            writer.Write((ushort)3);                       // IEEE float
            writer.Write((ushort)Channels);
            writer.Write((uint)rate);
            writer.Write((uint)(rate * Channels * sizeof(float)));
            writer.Write((ushort)(Channels * sizeof(float)));
            writer.Write((ushort)32);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write((uint)dataBytes);
        }
    }
}
