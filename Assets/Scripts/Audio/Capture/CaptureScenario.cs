using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using FMODUnity;
using UnityEngine;

/// <summary>
/// Plays the music with a fixed scripted set of kills and records Unity's output with <see cref="AudioCapture"/>.
/// Put it in a scene without gameplay (Assets/Scenes/AudioCapture.unity) and press Play; the WAV, a beat log
/// (frame index of every beat in the capture's own clock) and CPU numbers land in Docs/Captures/.
/// </summary>
public sealed class CaptureScenario : MonoBehaviour
{
    public enum Scenario { baseline, heavy, spike, styles, duck }

    [SerializeField] Scenario scenario = Scenario.baseline;
    [SerializeField] int seed = 7;
    [SerializeField] int beats = 128;
    [Tooltip("Appended to the file name, e.g. _run2 for a reproducibility recording.")]
    [SerializeField] string suffix = "";
    [SerializeField] bool exitPlayModeWhenDone = true;
    [Tooltip("MusicMix 0: only the kill chords are heard (styles always does this).")]
    [SerializeField] bool killsOnly;
    [Tooltip("Every frame: _variationNext = 0, _styleNext = 0 and FeatureSelector.CancelNext(), so nothing about the " +
             "arrangement changes at a phrase boundary (diagnosis of the beat-64 landing delay).")]
    [SerializeField] bool holdArrangement;
    [Tooltip("> 0: Application.targetFrameRate = this and vSync off (low-frame-rate takes). 0 = leave the project's setting.")]
    [SerializeField] int targetFps;

    [Tooltip("> 0: after the last beat call StopMusic() and keep recording this many seconds (return-bus tail check); " +
             "the capture frame of the StopMusic() call is written to .cpu.txt as 'STOP frame N'. 0 = end at the last beat.")]
    [SerializeField] float stopTailSeconds;
    [Tooltip("With stopTailSeconds > 0: after the tail call StartMusic() again and record 8 more beats (restart check). Only the baseline/heavy state (kills, Combo, Tension) is re-applied; not valid for killsOnly, styles, duck or holdArrangement takes. " +
             "Their beat log goes to <take>.beats2.csv (beats restart from 0).")]
    [SerializeField] bool restartAfterStop;

    [Header("spike (scheduling accuracy)")]
    [Tooltip("OneShotScheduler.ScheduleOffsetSamples for this take (default: the measured constant).")]
    [SerializeField] long scheduleOffsetSamples = OneShotScheduler.DefaultScheduleOffsetSamples;
    [Tooltip("Beats with a SpikeClick scheduled (1 .. this). Even beats are scheduled 1 beat ahead, odd beats 0.3 beat ahead.")]
    [SerializeField] int spikeBeats = 64;
    [Tooltip("Trigger the 0.3-beat-lead clicks from the beat clock's DSP time instead of SoundManager.BeatPosition, which " +
             "stalls at anchor + 0.999 beat while a jump landing's beat callback is late (Task 5 take spike_seed33_task5).")]
    [SerializeField] bool shortLeadOnDsp;
    [Tooltip("Write <take>.oneshots.csv: FmodOneShotBackend.DiagnosticLog of the spike and kill-chord backends (frame, " +
             "event, handle, DSP clock, ...). Off = the backends compute nothing for it.")]
    [SerializeField] bool logOneShots;

    [Header("duck (kill-chord ducking of the Harmony group)")]
    [Tooltip("Off = the same take without kills, the reference the ducked take is compared with.")]
    [SerializeField] bool duckKills = true;

    const int Colors = 3;

    string _wavPath;
    // targetFps: the previous settings, put back in OnDestroy so a capped take does not leak into the next Editor play session.
    int _savedFrameRate, _savedVSync;
    bool _frameCapApplied;
    // Beat times come from the conductor's own stamps (taken on the FMOD thread), read through reflection so the
    // game code stays untouched; polling CurrentBeat in Update would smear them by every frame hitch.
    readonly List<(int beat, double seconds, double tempo)> _beats = new List<(int, double, double)>();
    MusicConductor _conductor;
    FieldInfo _anchorSeconds;
    int _lastBeat = -1;
    int _cpuSamples;
    double _dspSum, _totalSum, _studioSum;
    float _studioMax;
    float _dspMax, _totalMax;
    bool _finished;
    // stopTailSeconds / restartAfterStop
    bool _stopping, _restarted;
    long _stopFrame = -1, _restartFrame = -1;
    int _restartQueued = -1;
    float _stopAt, _restartBeatsStartTime;
    readonly List<(int beat, double seconds, double tempo)> _beats2 = new List<(int, double, double)>();
    const int RestartBeats = 8;
    // spike
    const double LongLead = 1.0, ShortLead = 0.3;
    FmodOneShotBackend _backend;
    OneShotScheduler _scheduler;
    bool _spikeRefSet;
    int _nextShort = 1;
    readonly Dictionary<int, int> _timelineMs = new Dictionary<int, int>();
    readonly List<string> _spikeLog = new List<string>();
    int _stalls;
    float _longestFrame;
    // kill-chord one-shots (SoundManager's scheduler / backend, read through reflection)
    FmodOneShotBackend _killBackend;
    FieldInfo _killSchedulerField;
    int _killDropped, _killLate;
    readonly List<string> _killLossLog = new List<string>();
    readonly List<string> _oneShotLog = new List<string>();
    FieldInfo _pollCountField;
    // styles: 4 phrases (64 beats) per bed style 0..3
    const int StyleBlockBeats = 64;
    FieldInfo _styleCurrent, _styleNext;
    // holdArrangement
    FieldInfo _variationNextField, _featuresField;
    MethodInfo _cancelNext;
    // per-frame visual beat (dt, beat, bpm) and every music parameter write (beat_now, name, value)
    readonly List<string> _frameLog = new List<string>();
    readonly List<string> _paramLog = new List<string>();
    // duck: input / output RMS of the Harmony Duck compressor per mixer block (gain reduction = out - in)
    FMOD.DSP _duckComp;
    int _duckLastSamples = -1;
    float _duckLastIn = -1f;
    readonly List<string> _duckLog = new List<string>();

    IEnumerator Start()
    {
        if (targetFps > 0)
        {
            _savedFrameRate = Application.targetFrameRate;
            _savedVSync = QualitySettings.vSyncCount;
            _frameCapApplied = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFps;
        }
        var sound = SoundManager.Instance;
        typeof(SoundManager).GetField("sectionSeed", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(sound, seed);

        string folder = Path.Combine(Application.dataPath, "..", "Docs", "Captures");
        _wavPath = Path.GetFullPath(Path.Combine(folder, scenario + "_seed" + seed + suffix + ".wav"));
        // Never overwrite a recording (the references are the point): take the next free _runN instead.
        for (int run = 2; File.Exists(_wavPath); run++)
            _wavPath = Path.GetFullPath(Path.Combine(folder, scenario + "_seed" + seed + suffix + "_run" + run + ".wav"));
        _conductor = (MusicConductor)typeof(SoundManager).GetField("_conductor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sound);
        _anchorSeconds = typeof(MusicConductor).GetField("_anchorSeconds", BindingFlags.Instance | BindingFlags.NonPublic);
        _pollCountField = typeof(MusicConductor).GetField("_pollCount", BindingFlags.Instance | BindingFlags.NonPublic);
        _conductor.BeatChanged += OnBeat;
        sound.MusicParameterSet += OnMusicParameter;
        _variationNextField = typeof(SoundManager).GetField("_variationNext", BindingFlags.Instance | BindingFlags.NonPublic);
        _featuresField = typeof(SoundManager).GetField("_features", BindingFlags.Instance | BindingFlags.NonPublic);
        _cancelNext = _featuresField.FieldType.GetMethod("CancelNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _killBackend = (FmodOneShotBackend)typeof(SoundManager).GetField("_killBackend", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sound);
        _killSchedulerField = typeof(SoundManager).GetField("_killScheduler", BindingFlags.Instance | BindingFlags.NonPublic);
        _styleCurrent = typeof(SoundManager).GetField("_styleCurrent", BindingFlags.Instance | BindingFlags.NonPublic);
        _styleNext = typeof(SoundManager).GetField("_styleNext", BindingFlags.Instance | BindingFlags.NonPublic);
        if (scenario == Scenario.spike)
        {
            _backend = new FmodOneShotBackend();
            _scheduler = new OneShotScheduler(_conductor.Clock, _backend) { ScheduleOffsetSamples = scheduleOffsetSamples };
        }
        if (logOneShots)
        {
            if (_backend != null)
                _backend.DiagnosticLog = line => _oneShotLog.Add(Time.frameCount + ",spike," + line);
            _killBackend.DiagnosticLog = line => _oneShotLog.Add(Time.frameCount + ",kill," + line);
        }

        yield return null; // let FMOD finish initialising
        if (!AudioCapture.Begin(_wavPath))
        {
            Debug.LogError("[CaptureScenario] Could not start the capture.");
            yield break;
        }

        sound.StartMusic();
        if (scenario == Scenario.heavy)
        {
            // Applied after StartMusic, which resets both to 0.
            sound.Combo = 3;
            sound.Tension = 0.8f;
        }
        if (scenario == Scenario.styles || killsOnly)
            sound.MusicMix = 0f; // kill chords alone: the bed would differ between styles
        if (scenario == Scenario.duck)
        {
            // Only the Harmony group is heard: kill chords at -60 dB (their bus key sits before the fader, so they
            // still duck; at volume 0 FMOD would virtualise their voices and the key would go silent too), Feature
            // return muted, the other Music Bed tracks muted once the event has built them.
            sound.Intensity = SoundManager.MaxIntensity; // the offbeat pluck plays from intensity 1
            StartCoroutine(QuietKillBus());
            RuntimeManager.GetBus("bus:/Feature Duck").setMute(true);
            StartCoroutine(MuteMusicBed(sound));
            StartCoroutine(MeterDuckCompressor());
        }
        Debug.Log("[CaptureScenario] " + scenario + " seed " + seed + ", " + beats + " beats -> " + _wavPath);
    }

    void Update()
    {
        if (_finished || !AudioCapture.IsCapturing)
            return;
        var sound = SoundManager.Instance;
        if (_stopping)
        {
            UpdateStopTail(sound);
            return;
        }

        // A long frame delays kills and the beat log; such a take is not a valid reference.
        float dt = Time.unscaledDeltaTime;
        if (dt > 0.1f)
        {
            if (_lastBeat >= 0)
                _stalls++; // stalls before the first beat (music loading) do not matter
            Debug.LogWarning("[CaptureScenario] Frame stall " + (dt * 1000f).ToString("F0") + " ms at beat " + _lastBeat);
        }
        _longestFrame = Mathf.Max(_longestFrame, dt);

        if (AudioCapture.FramesCaptured > 0 &&
            RuntimeManager.StudioSystem.getCPUUsage(out FMOD.Studio.CPU_USAGE studio, out FMOD.CPU_USAGE cpu) == FMOD.RESULT.OK)
        {
            _studioSum += studio.update;
            _studioMax = Mathf.Max(_studioMax, studio.update);
            float total = cpu.dsp + cpu.stream + cpu.update;
            _cpuSamples++;
            _dspSum += cpu.dsp;
            _totalSum += total;
            _dspMax = Mathf.Max(_dspMax, cpu.dsp);
            _totalMax = Mathf.Max(_totalMax, total);
        }

        if (holdArrangement)
            HoldArrangement(sound);
        _frameLog.Add(string.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F5},{2}", dt, sound.BeatPosition, sound.CurrentBpm));

        if (logOneShots && _conductor.Clock.HasAnchor)
        {
            // Beat clock anchor as the conductor's per-frame polls refine it (pollCount from MusicConductor, read only).
            int anchor = _conductor.Clock.AnchorBeat;
            _conductor.Clock.DspAt(anchor, out ulong anchorDsp);
            _oneShotLog.Add(Time.frameCount + ",clock,anchor," + anchor + "," + MusicConductor.MasterDsp() + "," + anchorDsp + "," +
                            _pollCountField.GetValue(_conductor));
        }
        if (scenario == Scenario.spike)
            UpdateSpike(sound);
        if (scenario == Scenario.styles)
            ForceStyle(sound);
        TrackKillLosses(sound);
        LogDuckMeter();

        int beat = sound.CurrentBeat;
        while (beat > _lastBeat && _lastBeat < beats)
        {
            _lastBeat++;
            if (_lastBeat >= beats)
            {
                if (stopTailSeconds > 0f)
                    BeginStopTail(sound);
                else
                    Finish(sound);
                return;
            }
            QueueKills(sound, _lastBeat);
        }
    }

    void BeginStopTail(SoundManager sound)
    {
        _stopping = true;
        _stopFrame = AudioCapture.FramesCaptured;
        sound.StopMusic();
        _stopAt = Time.realtimeSinceStartup;
        Debug.Log("[CaptureScenario] StopMusic at capture frame " + _stopFrame);
    }

    void UpdateStopTail(SoundManager sound)
    {
        if (!_restarted)
        {
            if (Time.realtimeSinceStartup - _stopAt < stopTailSeconds)
                return;
            if (!restartAfterStop)
            {
                Finish(sound);
                return;
            }
            // Beats restart from 0 and go to the second log.
            _restarted = true;
            _conductor.BeatChanged -= OnBeat;
            _conductor.BeatChanged += OnBeat2;
            _restartFrame = AudioCapture.FramesCaptured;
            sound.StartMusic();
            if (scenario == Scenario.heavy)
            {
                sound.Combo = 3;
                sound.Tension = 0.8f;
            }
            return;
        }
        // Same kills per beat as the first run, so the first 4 beats of both are comparable.
        int beat = sound.CurrentBeat;
        while (beat > _restartQueued && _restartQueued < RestartBeats)
        {
            _restartQueued++;
            if (_restartQueued < RestartBeats)
                QueueKills(sound, _restartQueued);
        }
        if (_restartQueued >= RestartBeats)
            Finish(sound);
    }

    void OnBeat2(int beat)
    {
        _beats2.Add((beat, (double)_anchorSeconds.GetValue(_conductor), _conductor.Tempo));
    }

    void HoldArrangement(SoundManager sound)
    {
        _variationNextField.SetValue(sound, 0);
        _styleNext.SetValue(sound, 0);
        var features = _featuresField.GetValue(sound); // FeatureSelector: class or struct, written back either way
        _cancelNext.Invoke(features, null);
        _featuresField.SetValue(sound, features);
    }

    void OnMusicParameter(string name, float value)
    {
        if (!AudioCapture.IsCapturing || !_conductor.Clock.BeatAt(MusicConductor.MasterDsp(), out double now))
            return;
        _paramLog.Add(string.Format(CultureInfo.InvariantCulture, "{0:F4},{1},{2:G9}", now, name, value));
    }

    void OnBeat(int beat)
    {
        _beats.Add((beat, (double)_anchorSeconds.GetValue(_conductor), _conductor.Tempo));
        if (scenario != Scenario.spike)
            return;
        _timelineMs[beat] = _conductor.BeatTimelineMs;
        // Long lead: schedule the next beat right as this beat's anchor arrives (even beats only).
        int target = beat + 1;
        if (target % 2 == 0 && target <= spikeBeats)
            ScheduleClick(target, LongLead);
    }

    void UpdateSpike(SoundManager sound)
    {
        if (!_spikeRefSet)
        {
            var music = (FMOD.Studio.EventInstance)typeof(SoundManager)
                .GetField("_music", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sound);
            if (music.isValid() && music.setParameterByName("SpikeRef", 1f, true) == FMOD.RESULT.OK)
                _spikeRefSet = true;
        }
        // R11 (SetTempoFrom with the next section's BPM) is fed by SoundManager.Update every frame.
        _scheduler.Update();
        _backend.Update();
        // Short lead: odd beats, 0.3 beat ahead of the (visual) beat position.
        double position = sound.BeatPosition;
        while (_nextShort <= spikeBeats && ShortLeadDue(position))
        {
            if (_conductor.Clock.HasAnchor)
                ScheduleClick(_nextShort, ShortLead);
            else
                _spikeLog.Add(_nextShort + "," + ShortLead.ToString(CultureInfo.InvariantCulture) + ",0,0,-1,0");
            _nextShort += 2;
        }
    }

    bool ShortLeadDue(double position)
    {
        if (!shortLeadOnDsp)
            return position >= _nextShort - ShortLead;
        bool okDue = _conductor.Clock.DspAt(_nextShort - ShortLead, out ulong due);
        return okDue && _backend.NowDsp >= due;
    }

    void ScheduleClick(int beat, double lead)
    {
        var clock = _conductor.Clock;
        clock.DspAt(beat, out ulong target);
        ulong now = _backend.NowDsp;
        bool ok = _scheduler.Schedule(new OneShotNote(Instrument.SpikeClick, OutputBus.KillChords, 72, beat, 0.25, 0.0, 0f, 0.0), this);
        _spikeLog.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5}",
            beat, lead, ok ? 1 : 0, target, clock.AnchorBeat, (long)target - (long)now));
    }

    static int StyleFor(int beat) => Math.Max(0, beat) / StyleBlockBeats % 4;

    // Holds the bed style (and so the kill-chord style) at StyleFor(beat) for the current and the next phrase.
    void ForceStyle(SoundManager sound)
    {
        int beat = Math.Max(0, sound.CurrentBeat);
        _styleCurrent.SetValue(sound, StyleFor(beat));
        _styleNext.SetValue(sound, StyleFor(sound.NextPhraseStep / SoundManager.StepsPerBeat));
    }

    // Studio names every event group "Group Bus" at runtime, so the tree is matched by shape: the event master's
    // child with the most subgroups is Music Bed, and Music Bed's only child with subgroups is Harmony. Every other
    // child of Music Bed (the bed's leaf tracks) is muted; Harmony itself is left alone (it reaches the output through
    // its send to return:/Harmony Duck).
    IEnumerator MuteMusicBed(SoundManager sound)
    {
        var field = typeof(SoundManager).GetField("_music", BindingFlags.Instance | BindingFlags.NonPublic);
        for (int frame = 0; frame < 20000; frame++)
        {
            var music = (FMOD.Studio.EventInstance)field.GetValue(sound);
            if (music.isValid() && music.getChannelGroup(out FMOD.ChannelGroup root) == FMOD.RESULT.OK && root.hasHandle() &&
                MuteBedLeaves(root, out int muted))
            {
                Debug.Log("[CaptureScenario] duck: muted " + muted + " Music Bed tracks at frame " + frame);
                yield break;
            }
            yield return null;
        }
        Debug.LogError("[CaptureScenario] duck: Music Bed channel group not found");
    }

    // Metering on the return's compressor: with an external key, out - in is exactly the gain reduction.
    IEnumerator MeterDuckCompressor()
    {
        var bus = RuntimeManager.GetBus("bus:/Harmony Duck");
        bus.lockChannelGroup();
        for (int frame = 0; frame < 20000; frame++)
        {
            if (bus.getChannelGroup(out FMOD.ChannelGroup group) == FMOD.RESULT.OK && group.hasHandle())
            {
                group.getNumDSPs(out int count);
                for (int i = 0; i < count; i++)
                {
                    group.getDSP(i, out FMOD.DSP dsp);
                    if (dsp.getType(out FMOD.DSP_TYPE type) == FMOD.RESULT.OK && type == FMOD.DSP_TYPE.COMPRESSOR)
                    {
                        dsp.setMeteringEnabled(true, true);
                        _duckComp = dsp;
                        Debug.Log("[CaptureScenario] duck: metering the Harmony Duck compressor");
                        yield break;
                    }
                }
            }
            yield return null;
        }
        Debug.LogError("[CaptureScenario] duck: Harmony Duck compressor not found");
    }

    void LogDuckMeter()
    {
        if (!_duckComp.hasHandle() ||
            _duckComp.getMeteringInfo(out FMOD.DSP_METERING_INFO input, out FMOD.DSP_METERING_INFO output) != FMOD.RESULT.OK)
            return;
        float rmsIn = Rms(input), rmsOut = Rms(output);
        if (rmsIn == _duckLastIn && input.numsamples == _duckLastSamples)
            return; // same mixer block as last frame
        _duckLastIn = rmsIn;
        _duckLastSamples = input.numsamples;
        if (!_conductor.Clock.BeatAt(MusicConductor.MasterDsp(), out double now))
            return;
        _duckLog.Add(string.Format(CultureInfo.InvariantCulture, "{0:F4},{1:E4},{2:E4}", now, rmsIn, rmsOut));
    }

    static float Rms(FMOD.DSP_METERING_INFO info)
    {
        double sum = 0;
        int n = Math.Max(1, (int)info.numchannels);
        for (int c = 0; c < n && c < info.rmslevel.Length; c++)
            sum += info.rmslevel[c] * info.rmslevel[c];
        return (float)Math.Sqrt(sum / n);
    }

    // SoundManager sets the bus volume once when the music starts (and again only if KillChordMix changes).
    IEnumerator QuietKillBus()
    {
        while (SoundManager.Instance.BeatPosition < 0) // music not running yet
            yield return null;
        RuntimeManager.GetBus("bus:/Kill Chords").setVolume(0.001f);
        Debug.Log("[CaptureScenario] duck: bus:/Kill Chords at -60 dB");
    }

    static bool MuteBedLeaves(FMOD.ChannelGroup root, out int muted)
    {
        muted = 0;
        root.getNumGroups(out int count);
        FMOD.ChannelGroup bed = default;
        int most = 0;
        for (int i = 0; i < count; i++)
        {
            root.getGroup(i, out FMOD.ChannelGroup child);
            child.getNumGroups(out int n);
            if (n > most)
            {
                most = n;
                bed = child;
            }
        }
        if (most < 21) // Music Bed has 21 tracks (incl. Harmony) once the event has built them
            return false;
        int withChildren = 0;
        for (int i = 0; i < most; i++)
        {
            bed.getGroup(i, out FMOD.ChannelGroup track);
            track.getNumGroups(out int n);
            if (n > 0)
            {
                withChildren++;
                continue;
            }
            track.setMute(true);
            muted++;
        }
        return withChildren == 1;
    }

    // One-shots lost by the kill-chord path: late in the scheduler (start passed before dispatch) or dropped by the
    // backend (channel group appeared after the start, e.g. during FMOD's stall at a jump landing). Logged against the
    // beat heard when the loss was noticed, which is the beat the note was due on (or the one after).
    void TrackKillLosses(SoundManager sound)
    {
        var scheduler = (OneShotScheduler)_killSchedulerField.GetValue(sound);
        int late = scheduler != null ? scheduler.LateCount : 0;
        int dropped = _killBackend.DroppedCount;
        if (late == _killLate && dropped == _killDropped)
            return;
        bool okNow = _conductor.Clock.BeatAt(MusicConductor.MasterDsp(), out double now);
        _killLossLog.Add(string.Format(CultureInfo.InvariantCulture, "{0:F2},{1},{2}", okNow ? now : -1, late - _killLate, dropped - _killDropped));
        _killLate = late;
        _killDropped = dropped;
    }

    void QueueKills(SoundManager sound, int beat)
    {
        switch (scenario)
        {
            case Scenario.baseline:
                if (beat % 4 == 0)
                    for (int color = 0; color < Colors; color++)
                        sound.QueueKillChord(color);
                break;
            case Scenario.heavy:
                for (int color = 0; color < Colors; color++)
                    for (int kill = 0; kill < 3; kill++)
                        sound.QueueKillChord(color);
                break;
            case Scenario.spike:
                // No kills: SpikeClick one-shots are scheduled on the beat clock instead (UpdateSpike / OnBeat).
                break;
            case Scenario.styles:
                for (int color = 0; color < Colors; color++)
                    sound.QueueKillChord(color);
                break;
            case Scenario.duck:
                if (duckKills && beat % 4 == 0)
                    for (int color = 0; color < Colors; color++)
                        sound.QueueKillChord(color);
                break;
        }
    }

    void Finish(SoundManager sound)
    {
        _finished = true;
        string path = AudioCapture.End();
        sound.StopMusic();
        _conductor.BeatChanged -= OnBeat;
        _conductor.BeatChanged -= OnBeat2;
        sound.MusicParameterSet -= OnMusicParameter;
        File.WriteAllText(Path.ChangeExtension(path, ".frames.csv"), "dt,beat,bpm\n" + string.Join("\n", _frameLog) + "\n");
        File.WriteAllText(Path.ChangeExtension(path, ".params.csv"), "beat_now,name,value\n" + string.Join("\n", _paramLog) + "\n");
        var log = new StringBuilder("beat,frame,bpm\n");
        foreach (var (beat, seconds, tempo) in _beats)
            log.Append(beat).Append(',').Append(Math.Round(AudioCapture.FrameAt(seconds)).ToString("F0", CultureInfo.InvariantCulture))
                .Append(',').Append(tempo.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
        File.WriteAllText(Path.ChangeExtension(path, ".beats.csv"), log.ToString());
        if (_beats2.Count > 0)
        {
            var log2 = new StringBuilder("beat,frame,bpm\n");
            foreach (var (beat, seconds, tempo) in _beats2)
                log2.Append(beat).Append(',').Append(Math.Round(AudioCapture.FrameAt(seconds)).ToString("F0", CultureInfo.InvariantCulture))
                    .Append(',').Append(tempo.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
            File.WriteAllText(Path.ChangeExtension(path, ".beats2.csv"), log2.ToString());
        }
        string spikeSummary = "";
        if (scenario == Scenario.spike)
        {
            var spike = new StringBuilder("beat,lead,scheduled,target_dsp,anchor_beat,lead_samples\n");
            foreach (var line in _spikeLog)
                spike.Append(line).Append('\n');
            File.WriteAllText(Path.ChangeExtension(path, ".spike.csv"), spike.ToString());
            var timeline = new StringBuilder("beat,timeline_ms\n");
            foreach (var pair in _timelineMs)
                timeline.Append(pair.Key).Append(',').Append(pair.Value).Append('\n');
            File.WriteAllText(Path.ChangeExtension(path, ".timeline.csv"), timeline.ToString());
            spikeSummary = string.Format(CultureInfo.InvariantCulture,
                "; SPIKE offset {0} samples, scheduled {1}, started {2}, late (scheduler, not started) {3}, dropped (backend, group too late) {4}, cancelled {5}",
                scheduleOffsetSamples, _spikeLog.Count, _backend.StartedCount, _scheduler.LateCount, _backend.DroppedCount, _backend.CancelledCount);
        }

        var scheduler = (OneShotScheduler)_killSchedulerField.GetValue(sound);
        string killSummary = string.Format(CultureInfo.InvariantCulture,
            "; KILLS one-shots started {0}, late (scheduler) {1}, dropped (backend) {2}, cancelled {3}",
            _killBackend.StartedCount, scheduler != null ? scheduler.LateCount : 0, _killBackend.DroppedCount, _killBackend.CancelledCount);
        // Total across every one-shot user of this take: the kill chords plus, in spike takes, the SpikeClick scheduler.
        string oneShotSummary = string.Format(CultureInfo.InvariantCulture,
            "; ONESHOTS started {0}, late {1}, dropped {2}, cancelled {3}",
            _killBackend.StartedCount + (_backend != null ? _backend.StartedCount : 0),
            (scheduler != null ? scheduler.LateCount : 0) + (_scheduler != null ? _scheduler.LateCount : 0),
            _killBackend.DroppedCount + (_backend != null ? _backend.DroppedCount : 0),
            _killBackend.CancelledCount + (_backend != null ? _backend.CancelledCount : 0));
        var losses = new StringBuilder("beat_now,late,dropped\n");
        foreach (var line in _killLossLog)
            losses.Append(line).Append('\n');
        File.WriteAllText(Path.ChangeExtension(path, ".kills.csv"), losses.ToString());
        if (logOneShots)
        {
            _killBackend.DiagnosticLog = null;
            File.WriteAllText(Path.ChangeExtension(path, ".oneshots.csv"),
                "frame,backend,event,handle,now_dsp,start_dsp,...\n" + string.Join("\n", _oneShotLog) + "\n");
        }
        if (scenario == Scenario.duck)
        {
            var duck = new StringBuilder("beat,rms_in,rms_out\n");
            foreach (var line in _duckLog)
                duck.Append(line).Append('\n');
            File.WriteAllText(Path.ChangeExtension(path, ".duck.csv"), duck.ToString());
        }

        double dspAvg = _cpuSamples > 0 ? _dspSum / _cpuSamples : 0;
        double totalAvg = _cpuSamples > 0 ? _totalSum / _cpuSamples : 0;
        double studioAvg = _cpuSamples > 0 ? _studioSum / _cpuSamples : 0;
        string summary = string.Format(CultureInfo.InvariantCulture,
            "[CaptureScenario] DONE {0}: STUDIO update avg {9:F2}% max {10:F2}%; CORE dsp avg {1:F2}% max {2:F2}%, core total (dsp+stream+update) avg {3:F2}% max {4:F2}%, samples {5}, dropped frames {6}, stalls >100 ms after the first beat {7} (take is only a valid reference if 0), longest frame {8:F0} ms",
            Path.GetFileName(path), dspAvg, _dspMax, totalAvg, _totalMax, _cpuSamples, AudioCapture.FramesDropped,
            _stalls, _longestFrame * 1000f, studioAvg, _studioMax) + spikeSummary + killSummary + oneShotSummary;
        if (_stopFrame >= 0)
            summary += "; STOP frame " + _stopFrame + (_restartFrame >= 0 ? "; RESTART frame " + _restartFrame : "");
        Debug.Log(summary);
        File.WriteAllText(Path.ChangeExtension(path, ".cpu.txt"), summary + "\n");
#if UNITY_EDITOR
        if (exitPlayModeWhenDone)
            UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    void OnDestroy()
    {
        if (_frameCapApplied)
        {
            Application.targetFrameRate = _savedFrameRate;
            QualitySettings.vSyncCount = _savedVSync;
        }
        if (AudioCapture.IsCapturing && !_finished)
        {
            Debug.LogWarning("[CaptureScenario] Stopped early at beat " + _lastBeat + " of " + beats + "; recording is incomplete.");
            AudioCapture.End();
        }
    }
}
