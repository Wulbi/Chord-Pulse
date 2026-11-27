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
/// (capture frame of every beat as the music clock computes it) and CPU numbers land in Docs/Captures/.
/// </summary>
public sealed class CaptureScenario : MonoBehaviour
{
    public enum Scenario { baseline, heavy, spike, styles, duck, features, fever, combobreak }

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
    [Tooltip("SoundManager.MusicMix for the whole take (killsOnly / styles still force 0). Bus volumes of Music Bed and Feature.")]
    [SerializeField, Range(0f, 1f)] float musicMix = 1f;
    [Tooltip("SoundManager.Danger = true for the whole take (last-life low-pass on bus:/Music Bed).")]
    [SerializeField] bool forceDanger;
    [Tooltip("> 0: SoundManager.Hurt() on every beat that is a multiple of this (beats 0 excluded). 0 = off.")]
    [SerializeField] int hurtEveryBeats;
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
    [Tooltip("What spike schedules on its beats: Instrument:OutputBus:MIDI (InstrumentTable). Default the 3 kHz click on " +
             "Kill Chords; e.g. SpikeClick:MusicBed:72 or Drums:MusicBed:42 measures that routing's latency.")]
    [SerializeField] string spikeTarget = "SpikeClick:KillChords:72";
    [Tooltip("Trigger the 0.3-beat-lead clicks from the beat clock's DSP time instead of SoundManager.BeatPosition (the " +
             "heard beat, behind the mixer by the output latency).")]
    [SerializeField] bool shortLeadOnDsp;
    [Tooltip("Dispatch window (beats) of the spike clicks: how far ahead of its beat a click is handed to FMOD. 0.5 = the " +
             "scheduler default; 4.0 = a bar ahead, like the core drums.")]
    [SerializeField] double spikeDispatchBeats = OneShotScheduler.DispatchWindowBeats;
    [Tooltip("Write <take>.oneshots.csv: FmodOneShotBackend.DiagnosticLog of the spike and kill-chord backends (frame, " +
             "event, handle, DSP clock, ...). Off = the backends compute nothing for it.")]
    [SerializeField] bool logOneShots;
    [Tooltip("Combo ladder Task 1 probe: every frame OneShotScheduler.SetFade(-1, 1f) on the spike scheduler and on " +
             "SoundManager's shared one (_shots, by reflection), i.e. setVolume at the same level on every handed note, " +
             "armed or sounding. Checks that per-frame setVolume does not move start times.")]
    [SerializeField] bool fadeProbe;

    [Header("forced stalls (unity-music-clock Task 7)")]
    [Tooltip("> 0: System.Threading.Thread.Sleep(stallMs) once on the frame in which SoundManager.BeatPosition first " +
             "passes each value of stallAtBeats (a forced main-thread stall). The frame after it is counted as a forced " +
             "stall in .cpu.txt, apart from the unforced stalls, and marked forced=1 in .frames.csv. 0 = off.")]
    [SerializeField] int stallMs;
    [Tooltip("Visual beats at which stallMs is forced (ascending).")]
    [SerializeField] float[] stallAtBeats = new float[0];

    public enum Solo { none, musicBed, harmony, bedWithoutHarmony, feature }

    [Header("Task 11: bed pitched layers")]
    [Tooltip("Mute global buses to hear one family. musicBed: bus:/Kill Chords and bus:/Feature muted (the whole bed). " +
             "harmony: also bus:/Music Bed/Layers (drums, bass, tension, dreamy sub) muted. bedWithoutHarmony: Kill Chords, " +
             "Feature and bus:/Music Bed/Harmony muted (drums + bass/tension/dreamy sub). feature: bus:/Kill Chords, " +
             "bus:/Music Bed/Layers and bus:/Music Bed/Harmony muted (bus:/Feature alone). The kill-keyed duck is off in " +
             "every solo (muted key).")]
    [SerializeField] Solo soloLayers = Solo.none;
    [Tooltip("Steps SoundManager.Tension 0 -> 0.3 -> 0.5 -> 0.7 -> 0.9 over the take (fifths of the beats); overrides heavy's 0.8.")]
    [SerializeField] bool tensionSteps;
    [Tooltip("styles: keep the bed audible (musicMix) instead of forcing MusicMix 0.")]
    [SerializeField] bool stylesWithBed;
    [Tooltip(">= 0: arrangement intensity for the take, pinned with SoundManager.PinArrangement (the off-beat pluck plays " +
             "from 1, funk guitar from 2), and SoundManager.Intensity (section choice). -1 = intensity 0 pinned, " +
             "SoundManager.Intensity left at 0 (duck pins and sets 3).")]
    [SerializeField] int intensity = -1;
    [Tooltip("Comma-separated PatternTable.LayerNames: only these pattern layers are scheduled (one-shot path; sets the " +
             "layer masks of both PatternSchedulers, core and the rest). Empty = all.")]
    [SerializeField] string patternLayers = "";

    [Header("duck (kill-chord ducking of the Harmony group)")]
    [Tooltip("Off = the same take without kills, the reference the ducked take is compared with.")]
    [SerializeField] bool duckKills = true;
    [Tooltip("Any scenario but duck: meter the Feature-keyed compressor of bus:/Music Bed/Harmony (input / output RMS per " +
             "mixer block -> <take>.duck.csv), i.e. how much the feature loops duck the Harmony layers. Harmony must be audible.")]
    [SerializeField] bool meterFeatureDuck;

    [Header("features (Task 13)")]
    [Tooltip("features: phrase featureFirstPhrase + k - 1 plays feature loop k (1 .. 12; FeatureSelector Current / Next " +
             "forced every frame), the other phrases none; no kills. From phrase 1: 13 phrases = 208 beats.")]
    [SerializeField] int featureFirstPhrase = 1;

    [Header("fever (Task 14)")]
    [Tooltip("fever: SoundManager.QueueFever() at this beat and every feverEveryBeats after it (0 = once); one kill chord " +
             "per colour on every beat (fever kill chords: style 0). Seed 7: fever 32..48 (build-up bar 28..32), 96..112.")]
    [SerializeField] int feverAtBeat = 24;
    [SerializeField] int feverEveryBeats = 64;
    [Tooltip("fever: no kills (timing / level takes of the bed alone).")]
    [SerializeField] bool feverWithoutKills;

    [Header("combobreak (combo ladder Task 6)")]
    [Tooltip("combobreak: no kills; SoundManager.ComboCount is set from the mixer beat: +1 on every beat from beat 4 " +
             "(up to comboMax), 0 at each of these mixer beats (comma-separated, ascending), then +1 on every beat again. " +
             "Empty = never broken. Guest loops are kept out (holdArrangement is applied: no feature loop, style 0, " +
             "variation 0). The arrangement is the ladder's unless intensity >= 0 (then PinArrangement(intensity, 0)). " +
             "Writes <take>.ladder.csv every frame.")]
    [SerializeField] string comboBreakBeats = "40.5";
    [Tooltip("combobreak: the combo stops rising at this value (10 reaches rung 6; 2 stays at rung 1).")]
    [SerializeField] int comboMax = 10;

    const int Colors = 3;
    // combobreak: first beat that adds 1, the break beats, the next one, the last whole beat counted, the combo set
    const int ComboFirstBeat = 4;
    double[] _comboBreaks = new double[0];
    int _nextComboBreak;
    int _comboCountedBeat = ComboFirstBeat - 1;
    int _combo;
    FieldInfo _audioBeatField;
    readonly List<string> _ladderLog = new List<string>();

    string _wavPath;
    // targetFps: the previous settings, put back in OnDestroy so a capped take does not leak into the next Editor play session.
    int _savedFrameRate, _savedVSync;
    bool _frameCapApplied;
    // Beat times are the music clock's own: the DSP sample of every beat of a phrase, taken when MusicTransport starts that
    // phrase (SoundManager's _clock / _transport, read through reflection so the game code stays untouched); a resync
    // re-takes the rest of its phrase. Converted to capture frames with AudioCapture.FrameAtDsp.
    readonly SortedDictionary<int, (ulong dsp, double bpm)> _beats = new SortedDictionary<int, (ulong, double)>();
    BeatClock _clock;
    MusicTransport _transport;
    int _lastBeat = -1;
    int _cpuSamples;
    double _dspSum, _totalSum, _studioSum;
    float _studioMax;
    float _dspMax, _totalMax;
    int _channelsPeak, _realChannelsPeak; // Core System getChannelsPlaying, per frame (playing incl. virtual / real)
    bool _finished;
    // stopTailSeconds / restartAfterStop
    bool _stopping, _restarted;
    long _stopFrame = -1, _restartFrame = -1;
    int _restartQueued = -1;
    float _stopAt, _restartBeatsStartTime;
    readonly SortedDictionary<int, (ulong dsp, double bpm)> _beats2 = new SortedDictionary<int, (ulong, double)>();
    const int RestartBeats = 8;
    // spike
    const double LongLead = 1.0, ShortLead = 0.3;
    FmodOneShotBackend _backend;
    OneShotScheduler _scheduler;
    int _nextShort = 1, _nextLong = 2;
    readonly List<string> _spikeLog = new List<string>();
    int _stalls;
    float _longestFrame;
    // forced stalls: next stallAtBeats index, whether the previous frame slept, frames counted as forced, resyncs seen
    int _nextStall, _forcedStalls, _resyncs;
    bool _forcedPending;
    readonly List<string> _stallLog = new List<string>();
    // one-shots (SoundManager's shared scheduler / backend, read through reflection: _shots, _shotBackend, _patterns)
    FmodOneShotBackend _killBackend;
    FieldInfo _killSchedulerField, _patternsField, _corePatternsField;
    readonly List<string> _killLossLog = new List<string>();
    readonly List<string> _lossLog = new List<string>();
    int _killLate, _patternLate, _patternScheduled;
    OneShotScheduler _subscribedShots;
    double _firstAnchorBeat = double.NaN;
    readonly List<string> _patternLog = new List<string>();
    readonly List<string> _studioLog = new List<string>();   // per frame: Studio update and Core dsp CPU (.studio.csv)
    static readonly float[] TensionStepValues = { 0f, 0.3f, 0.5f, 0.7f, 0.9f };
    readonly List<string> _oneShotLog = new List<string>();
    // styles: 4 phrases (64 beats) per bed style 0..3
    const int StyleBlockBeats = 64;
    FieldInfo _styleCurrent, _styleNext;
    // holdArrangement
    FieldInfo _variationNextField, _featuresField, _lastPhraseField;
    MethodInfo _cancelNext;
    // per-frame visual beat (dt, beat, bpm) and every music parameter write (beat_now, name, value)
    readonly List<string> _frameLog = new List<string>();
    readonly List<string> _paramLog = new List<string>();
    // duck: input / output RMS of the kill-keyed Harmony compressor (bus:/Music Bed/Harmony) per mixer block (gain reduction = out - in)
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
        _clock = (BeatClock)typeof(SoundManager).GetField("_clock", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sound);
        _transport = (MusicTransport)typeof(SoundManager).GetField("_transport", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sound);
        _transport.PhraseStarted += OnPhrase;
        _transport.Resynced += OnResync;
        sound.MusicParameterSet += OnMusicParameter;
        _variationNextField = typeof(SoundManager).GetField("_variationNext", BindingFlags.Instance | BindingFlags.NonPublic);
        _featuresField = typeof(SoundManager).GetField("_features", BindingFlags.Instance | BindingFlags.NonPublic);
        _lastPhraseField = typeof(SoundManager).GetField("_lastPhrase", BindingFlags.Instance | BindingFlags.NonPublic);
        _cancelNext = _featuresField.FieldType.GetMethod("CancelNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _killBackend = (FmodOneShotBackend)typeof(SoundManager).GetField("_shotBackend", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sound);
        _killSchedulerField = typeof(SoundManager).GetField("_shots", BindingFlags.Instance | BindingFlags.NonPublic);
        _patternsField = typeof(SoundManager).GetField("_patterns", BindingFlags.Instance | BindingFlags.NonPublic);
        _corePatternsField = typeof(SoundManager).GetField("_corePatterns", BindingFlags.Instance | BindingFlags.NonPublic);
        _killBackend.NoteDropped += OnNoteDropped;
        _styleCurrent = typeof(SoundManager).GetField("_styleCurrent", BindingFlags.Instance | BindingFlags.NonPublic);
        _styleNext = typeof(SoundManager).GetField("_styleNext", BindingFlags.Instance | BindingFlags.NonPublic);
        _audioBeatField = typeof(SoundManager).GetField("_audioBeat", BindingFlags.Instance | BindingFlags.NonPublic);
        if (scenario == Scenario.combobreak && !string.IsNullOrWhiteSpace(comboBreakBeats))
        {
            var breaks = new List<double>();
            foreach (var text in comboBreakBeats.Split(','))
                if (!string.IsNullOrWhiteSpace(text))
                    breaks.Add(double.Parse(text.Trim(), CultureInfo.InvariantCulture));
            breaks.Sort();
            _comboBreaks = breaks.ToArray();
        }
        if (scenario == Scenario.spike)
        {
            _backend = new FmodOneShotBackend();
            _scheduler = new OneShotScheduler(_clock, _backend) { ScheduleOffsetSamples = scheduleOffsetSamples };
        }
        if (logOneShots)
        {
            if (_backend != null)
                _backend.DiagnosticLog = line => _oneShotLog.Add(Time.frameCount + ",spike," + line);
            _killBackend.DiagnosticLog = line => _oneShotLog.Add(Time.frameCount + ",shot," + line);
        }

        yield return null; // let FMOD finish initialising
        if (!AudioCapture.Begin(_wavPath))
        {
            Debug.LogError("[CaptureScenario] Could not start the capture.");
            yield break;
        }

        sound.StartMusic();
        if (scenario == Scenario.heavy)
            sound.Tension = 0.8f; // applied after StartMusic, which resets it
        sound.MusicMix = musicMix;
        if ((scenario == Scenario.styles && !stylesWithBed) || killsOnly)
            sound.MusicMix = 0f; // kill chords alone: the bed would differ between styles
        if (intensity >= 0)
            sound.Intensity = intensity;
        if (!string.IsNullOrWhiteSpace(patternLayers) && _patternsField.GetValue(sound) is PatternScheduler mask &&
            _corePatternsField.GetValue(sound) is PatternScheduler coreMask)
        {
            ulong bits = 0;
            foreach (var name in patternLayers.Split(','))
            {
                int layer = Array.IndexOf(PatternTable.LayerNames, name.Trim());
                if (layer < 0)
                    Debug.LogError("[CaptureScenario] unknown pattern layer " + name);
                else
                    bits |= 1UL << layer;
            }
            // Each scheduler keeps only its own layers (SoundManager: core layers vs the rest).
            var maskField = typeof(PatternScheduler).GetField("_layerMask", BindingFlags.Instance | BindingFlags.NonPublic);
            maskField.SetValue(mask, bits & ~PatternTable.CoreLayerMask);
            maskField.SetValue(coreMask, bits & PatternTable.CoreLayerMask);
        }
        SubscribeShots(sound);
        ApplySolo(sound);
        sound.Danger = forceDanger; // applied after StartMusic, which resets it
        if (scenario == Scenario.duck)
        {
            // Only the Harmony group is heard: kill chords at -60 dB (their bus key sits before the fader, so they
            // still duck; at volume 0 FMOD would virtualise their voices and the key would go silent too), bus:/Feature
            // muted (it carries the feature loops and also the Feature sidechain key, so the Feature duck is off too),
            // the other Music Bed layers muted (bus:/Music Bed/Layers).
            sound.Intensity = SoundManager.MaxIntensity; // the offbeat pluck plays from intensity 1
            StartCoroutine(QuietKillBus());
            RuntimeManager.GetBus("bus:/Feature").setMute(true);
            MuteMusicBedLayers();
            StartCoroutine(MeterDuckCompressor(featureKeyed: false));
        }
        else if (meterFeatureDuck)
            StartCoroutine(MeterDuckCompressor(featureKeyed: true));
        PinArrangement(sound, restart: false);
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
        // The frame after a forced Thread.Sleep carries it in its dt: counted apart from the unforced stalls.
        bool forced = _forcedPending;
        _forcedPending = false;
        if (dt > 0.1f)
        {
            if (forced)
                _forcedStalls++;
            else if (_lastBeat >= 0)
                _stalls++; // stalls before the first beat (music loading) do not matter
            Debug.LogWarning("[CaptureScenario] Frame stall " + (dt * 1000f).ToString("F0") + " ms at beat " + _lastBeat +
                             (forced ? " (forced)" : ""));
        }
        _longestFrame = Mathf.Max(_longestFrame, dt);

        if (AudioCapture.FramesCaptured > 0 &&
            RuntimeManager.StudioSystem.getCPUUsage(out FMOD.Studio.CPU_USAGE studio, out FMOD.CPU_USAGE cpu) == FMOD.RESULT.OK)
        {
            _studioSum += studio.update;
            _studioMax = Mathf.Max(_studioMax, studio.update);
            bool okBeat = _clock.BeatAt(FmodClock.MasterDsp(), out double cpuBeat);
            _studioLog.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1:F3},{2:F4},{3:F2},{4:F2}",
                Time.frameCount, okBeat ? cpuBeat : -1, Time.unscaledDeltaTime, studio.update, cpu.dsp));
            float total = cpu.dsp + cpu.stream + cpu.update;
            _cpuSamples++;
            _dspSum += cpu.dsp;
            _totalSum += total;
            _dspMax = Mathf.Max(_dspMax, cpu.dsp);
            _totalMax = Mathf.Max(_totalMax, total);
            if (RuntimeManager.CoreSystem.getChannelsPlaying(out int channels, out int realChannels) == FMOD.RESULT.OK)
            {
                _channelsPeak = Math.Max(_channelsPeak, channels);
                _realChannelsPeak = Math.Max(_realChannelsPeak, realChannels);
            }
        }

        if (holdArrangement || scenario == Scenario.combobreak)
            HoldArrangement(sound);
        if (scenario == Scenario.combobreak)
            UpdateComboBreak(sound);
        _frameLog.Add(string.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F5},{2},{3}", dt, sound.BeatPosition, sound.CurrentBpm,
            forced ? 1 : 0));

        if (logOneShots && _clock.HasAnchor)
        {
            // Beat clock anchor (MusicTransport moves it on every phrase boundary, and on a resync).
            int anchor = _clock.AnchorBeat;
            _clock.DspAt(anchor, out ulong anchorDsp);
            _oneShotLog.Add(Time.frameCount + ",clock,anchor," + anchor + "," + FmodClock.MasterDsp() + "," + anchorDsp);
        }
        if (scenario == Scenario.spike)
            UpdateSpike(sound);
        if (fadeProbe)
        {
            _scheduler?.SetFade(-1, 1f);
            (_killSchedulerField.GetValue(sound) as OneShotScheduler)?.SetFade(-1, 1f);
        }
        if (scenario == Scenario.styles)
            ForceStyle(sound);
        if (scenario == Scenario.features)
            ForceFeatures(sound);
        SubscribeShots(sound);
        if (double.IsNaN(_firstAnchorBeat) && _clock.HasAnchor &&
            _clock.BeatAt(FmodClock.MasterDsp(), out double anchorNow))
            _firstAnchorBeat = anchorNow;
        if (tensionSteps && beats > 0)
            sound.Tension = TensionStepValues[Math.Min(TensionStepValues.Length - 1, Math.Max(0, sound.CurrentBeat) * TensionStepValues.Length / beats)];
        LogDuckMeter();
        ForceStall(sound);

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

    // The arrangement of every scenario is fixed as it was before the combo ladder (no ladder: the take never sets
    // SoundManager.ComboCount, and the floor would otherwise rise with the round time): heavy plays combo level 3, every
    // scenario intensity 0, except duck (3) and the intensity option. After a restart (restartAfterStop) only heavy's
    // combo was re-applied, so the intensity there is 0 again. Called after StartMusic, which clears the pin.
    void PinArrangement(SoundManager sound, bool restart)
    {
        // combobreak plays the ladder (its ComboCount), unless the intensity option pins a fixed rung (intensity, combo 0).
        if (scenario == Scenario.combobreak)
        {
            if (intensity >= 0 && !restart)
                sound.PinArrangement(intensity, 0);
            return;
        }
        int pinIntensity = restart ? 0 : scenario == Scenario.duck ? SoundManager.MaxIntensity : Math.Max(0, intensity);
        sound.PinArrangement(pinIntensity, scenario == Scenario.heavy ? 3 : 0);
    }

    // stallMs / stallAtBeats: one Thread.Sleep per value, on the first frame whose visual beat is at or past it (after
    // this frame's scheduling, so the next frame sees the whole gap).
    void ForceStall(SoundManager sound)
    {
        if (stallMs <= 0 || stallAtBeats == null)
            return;
        double position = sound.BeatPosition;
        while (_nextStall < stallAtBeats.Length && position >= stallAtBeats[_nextStall])
        {
            bool okNow = _clock.BeatAt(FmodClock.MasterDsp(), out double mixerBeat);
            _stallLog.Add(string.Format(CultureInfo.InvariantCulture, "STALL forced {0} ms at visual beat {1:F3} (target {2:F2}, mixer beat {3:F3})",
                stallMs, position, stallAtBeats[_nextStall], okNow ? mixerBeat : -1));
            Debug.Log("[CaptureScenario] " + _stallLog[_stallLog.Count - 1]);
            _nextStall++;
            System.Threading.Thread.Sleep(stallMs);
            _forcedPending = true;
        }
    }

    // combobreak: the combo follows the mixer beat (the beat the ladder and its fade are timed in, and the beat the WAV is
    // cut by), in time order: a whole beat k >= ComboFirstBeat adds 1 (up to comboMax), a break beat sets 0; a break on a
    // whole beat comes after that beat's +1. SoundManager (execution order -100) has already run this frame, so it sees the
    // new combo on the next frame. Then one .ladder.csv row with what the ladder played this frame.
    void UpdateComboBreak(SoundManager sound)
    {
        if (_clock.HasAnchor && _clock.BeatAt(FmodClock.MasterDsp(), out double now))
        {
            while (true)
            {
                int nextWhole = _comboCountedBeat + 1;
                double nextBreak = _nextComboBreak < _comboBreaks.Length ? _comboBreaks[_nextComboBreak] : double.PositiveInfinity;
                if (nextWhole <= nextBreak && nextWhole <= now)
                {
                    _comboCountedBeat = nextWhole;
                    _combo = Math.Min(comboMax, _combo + 1);
                }
                else if (nextBreak < nextWhole && nextBreak <= now)
                {
                    _nextComboBreak++;
                    _combo = 0;
                    Debug.Log(string.Format(CultureInfo.InvariantCulture, "[CaptureScenario] combo break at mixer beat {0:F4} (target {1})", now, nextBreak));
                }
                else
                    break;
            }
            sound.ComboCount = _combo;
        }
        if (!AudioCapture.IsCapturing)
            return;
        var feature = sound.CurrentFeature;
        _ladderLog.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1:F5},{2},{3},{4},{5:F5},{6:F5},{7},{8},{9}",
            Time.frameCount, (double)_audioBeatField.GetValue(sound), sound.ComboCount, sound.LadderRung, sound.LadderFloor,
            sound.ComboFadeScale, sound.BeatPosition, AudioCapture.FramesCaptured,
            sound.FeverActive || sound.FeverPending ? 1 : 0, feature != null ? feature.Id : 0));
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
            // Beats restart from 0 and go to the second log (OnPhrase).
            _restarted = true;
            _restartFrame = AudioCapture.FramesCaptured;
            sound.StartMusic();
            if (scenario == Scenario.heavy)
                sound.Tension = 0.8f;
            PinArrangement(sound, restart: true);
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

    // MusicTransport.PhraseStarted (after SoundManager's own handler): the clock is anchored on this phrase's boundary, so
    // every beat of the phrase is exact now (a later SetNextTempo only moves beats from the next boundary on).
    void OnPhrase(int phrase)
    {
        LogBeats(phrase * MusicTransport.BeatsPerPhrase);
    }

    // A resync re-anchors the clock at a bar: the rest of the phrase is taken again.
    void OnResync()
    {
        _resyncs++;
        string line = "RESYNC at capture frame " + AudioCapture.FramesCaptured + ", master dsp " + FmodClock.MasterDsp() +
                      ", anchor beat " + (_clock.HasAnchor ? _clock.AnchorBeat.ToString(CultureInfo.InvariantCulture) : "-");
        _stallLog.Add(line);
        Debug.LogWarning("[CaptureScenario] " + line);
        if (_clock.HasAnchor)
            LogBeats(_clock.AnchorBeat);
    }

    void LogBeats(int fromBeat)
    {
        var log = _restarted ? _beats2 : _beats;
        int to = _transport.NextPhraseBeat;
        for (int beat = fromBeat; beat < to; beat++)
            if (_clock.DspAt(beat, out ulong dsp))
                log[beat] = (dsp, _transport.Bpm);
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
        if (!AudioCapture.IsCapturing || !_clock.BeatAt(FmodClock.MasterDsp(), out double now))
            return;
        _paramLog.Add(string.Format(CultureInfo.InvariantCulture, "{0:F4},{1},{2:G9}", now, name, value));
    }

    void UpdateSpike(SoundManager sound)
    {
        // The next phrase's BPM reaches the clock through SoundManager (MusicTransport.SetNextTempo).
        _scheduler.Update();
        _backend.Update();
        // Long lead: even beats, scheduled once the mixer is a beat before them.
        while (_nextLong <= spikeBeats && _clock.HasAnchor && _clock.BeatAt(_backend.NowDsp, out double now) &&
               now >= _nextLong - LongLead)
        {
            ScheduleClick(_nextLong, LongLead);
            _nextLong += 2;
        }
        // Short lead: odd beats, 0.3 beat ahead of the (visual) beat position.
        double position = sound.BeatPosition;
        while (_nextShort <= spikeBeats && ShortLeadDue(position))
        {
            if (_clock.HasAnchor)
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
        bool okDue = _clock.DspAt(_nextShort - ShortLead, out ulong due);
        return okDue && _backend.NowDsp >= due;
    }

    void ScheduleClick(int beat, double lead)
    {
        var clock = _clock;
        clock.DspAt(beat, out ulong target);
        ulong now = _backend.NowDsp;
        var t = spikeTarget.Split(':');
        var inst = (Instrument)Enum.Parse(typeof(Instrument), t[0].Trim());
        int midi = int.Parse(t[2].Trim(), CultureInfo.InvariantCulture);
        bool ok = _scheduler.Schedule(new OneShotNote(inst, (OutputBus)Enum.Parse(typeof(OutputBus), t[1].Trim()), midi, beat,
            inst == Instrument.SpikeClick ? 0.25 : 0.0, 0.0, 0f, InstrumentTable.OnsetSeconds(inst, midi)), this, spikeDispatchBeats);
        _spikeLog.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5}",
            beat, lead, ok ? 1 : 0, target, clock.AnchorBeat, (long)target - (long)now));
    }

    static int StyleFor(int beat) => Math.Max(0, beat) / StyleBlockBeats % 4;

    FeatureSelector.Loop FeatureFor(int phrase)
    {
        int id = phrase - featureFirstPhrase + 1;
        return id >= 1 && id <= FeatureLoopNotes.Loops.Length ? FeatureLoopNotes.Loops[id - 1] : null;
    }

    // features: the current and the next phrase get their loop; SoundManager reads both every frame (ArrangementInputs). Its own Advance at a boundary picks nothing new (Current is set by then). Next is
    // only set once SoundManager has advanced to the phrase before it: the first Advance (phrase 0) would otherwise
    // promote the next phrase's loop into phrase 0 for a frame (seen in the first takes: loop 1's beat-0 note at beat 0).
    void ForceFeatures(SoundManager sound)
    {
        var features = (FeatureSelector)_featuresField.GetValue(sound);
        int next = sound.NextPhraseStep / SoundManager.StepsPerPhrase;
        bool advanced = (int)_lastPhraseField.GetValue(sound) == next - 1;
        typeof(FeatureSelector).GetField("<Current>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(features, FeatureFor(next - 1));
        typeof(FeatureSelector).GetField("<Next>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(features, advanced ? FeatureFor(next) : null);
    }

    // Holds the bed style (and so the kill-chord style) at StyleFor(beat) for the current and the next phrase.
    void ForceStyle(SoundManager sound)
    {
        int beat = Math.Max(0, sound.CurrentBeat);
        _styleCurrent.SetValue(sound, StyleFor(beat));
        _styleNext.SetValue(sound, StyleFor(sound.NextPhraseStep / SoundManager.StepsPerBeat));
    }

    // Every Music Bed layer but Harmony: the one-shots of the drums, bass, tension and dreamy sub play through
    // bus:/Music Bed/Layers and its sub-buses (InstrumentTable.BusPath).
    static void MuteMusicBedLayers()
    {
        RuntimeManager.GetBus("bus:/Music Bed/Layers").setMute(true);
        Debug.Log("[CaptureScenario] bus:/Music Bed/Layers muted");
    }

    // Metering on the kill-keyed compressor of bus:/Music Bed/Harmony (FMOD/Tools/mixer.py; until Task 7 it was on
    // return:/Harmony Duck). The bus holds [Feature-keyed, kill-keyed] compressors in signal order; DSP index 0 is the
    // fader (head), so the first compressor found from index 0 is the kill-keyed one, the last before the fader, and the
    // second is the Feature-keyed one.
    IEnumerator MeterDuckCompressor(bool featureKeyed)
    {
        var bus = RuntimeManager.GetBus("bus:/Music Bed/Harmony");
        bus.lockChannelGroup();
        for (int frame = 0; frame < 20000; frame++)
        {
            if (bus.getChannelGroup(out FMOD.ChannelGroup group) == FMOD.RESULT.OK && group.hasHandle())
            {
                group.getNumDSPs(out int count);
                int skip = featureKeyed ? 1 : 0;
                for (int i = 0; i < count; i++)
                {
                    group.getDSP(i, out FMOD.DSP dsp);
                    if (dsp.getType(out FMOD.DSP_TYPE type) == FMOD.RESULT.OK && type == FMOD.DSP_TYPE.COMPRESSOR && skip-- == 0)
                    {
                        dsp.setMeteringEnabled(true, true);
                        _duckComp = dsp;
                        Debug.Log("[CaptureScenario] duck: metering the " + (featureKeyed ? "Feature" : "kill") + "-keyed Harmony compressor");
                        yield break;
                    }
                }
            }
            yield return null;
        }
        Debug.LogError("[CaptureScenario] duck: " + (featureKeyed ? "Feature" : "kill") + "-keyed Harmony compressor not found");
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
        if (!_clock.BeatAt(FmodClock.MasterDsp(), out double now))
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

    // The shared scheduler exists once StartMusic ran (SoundManager creates it lazily); subscribe once.
    void SubscribeShots(SoundManager sound)
    {
        var shots = (OneShotScheduler)_killSchedulerField.GetValue(sound);
        if (shots != null && shots != _subscribedShots)
        {
            shots.NoteLate += OnNoteLate;
            _subscribedShots = shots;
        }
        if (_patternsField.GetValue(sound) is PatternScheduler patterns && patterns != _subscribedPatterns)
        {
            patterns.NoteScheduled += OnPatternNote;
            _subscribedPatterns = patterns;
        }
        if (_corePatternsField.GetValue(sound) is PatternScheduler core && core != _subscribedCorePatterns)
        {
            core.NoteScheduled += OnPatternNote;
            _subscribedCorePatterns = core;
        }
    }

    PatternScheduler _subscribedPatterns, _subscribedCorePatterns;

    static void UnmuteSoloBuses()
    {
        foreach (var path in new[] { "bus:/Kill Chords", "bus:/Feature", "bus:/Music Bed/Layers", "bus:/Music Bed/Harmony" })
        {
            try { RuntimeManager.GetBus(path).setMute(false); }
            catch (Exception) { }
        }
    }

    // Pattern notes as scheduled (logOneShots): beat, instrument, bus, MIDI, onset and the DSP start the scheduler
    // computes for it right now (clock as of this frame; RescheduleFrom may move it later at a tempo change).
    void OnPatternNote(PatternNote n, double beat)
    {
        _patternScheduled++;
        if (!logOneShots)
            return;
        _clock.DspAt(beat, out ulong onBeat);
        double onset = InstrumentTable.OnsetSeconds(n.Instrument, n.Midi);
        long offset = _subscribedShots != null ? _subscribedShots.ScheduleOffsetSamples : OneShotScheduler.DefaultScheduleOffsetSamples;
        long start = (long)onBeat + offset - (long)Math.Round(onset * _clock.SampleRate);
        _patternLog.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1:F4},{2},{3},{4},{5:F6},{6},{7}",
            Time.frameCount, beat, n.Instrument, n.Bus, n.Midi, onset, onBeat, start));
    }

    void ApplySolo(SoundManager sound)
    {
        if (soloLayers == Solo.none)
            return;
        RuntimeManager.GetBus("bus:/Kill Chords").setMute(true);
        RuntimeManager.GetBus("bus:/Feature").setMute(true);
        if (soloLayers == Solo.harmony)
            MuteMusicBedLayers();
        else if (soloLayers == Solo.bedWithoutHarmony)
            RuntimeManager.GetBus("bus:/Music Bed/Harmony").setMute(true);
        else if (soloLayers == Solo.feature)
        {
            RuntimeManager.GetBus("bus:/Feature").setMute(false);
            RuntimeManager.GetBus("bus:/Music Bed/Layers").setMute(true);
            RuntimeManager.GetBus("bus:/Music Bed/Harmony").setMute(true);
        }
    }

    void OnNoteLate(object owner, double beat)
    {
        bool kill = !(owner is PatternScheduler);
        if (kill) _killLate++; else _patternLate++;
        bool okNow = _clock.BeatAt(FmodClock.MasterDsp(), out double now);
        _lossLog.Add(string.Format(CultureInfo.InvariantCulture, "{0:F3},{1:F3},{2},late", okNow ? now : -1, beat, kill ? "kill" : "pattern"));
        if (kill)
            _killLossLog.Add(string.Format(CultureInfo.InvariantCulture, "{0:F2},1,0", okNow ? now : -1));
    }

    void OnNoteDropped(OutputBus bus, ulong start)
    {
        bool kill = bus == OutputBus.KillChords;
        bool okNow = _clock.BeatAt(FmodClock.MasterDsp(), out double now);
        double beat = _clock.BeatAt(start, out double b) ? b : -1;
        _lossLog.Add(string.Format(CultureInfo.InvariantCulture, "{0:F3},{1:F3},{2},drop,{3}", okNow ? now : -1, beat, kill ? "kill" : "pattern", bus));
        if (kill)
            _killLossLog.Add(string.Format(CultureInfo.InvariantCulture, "{0:F2},0,1", okNow ? now : -1));
    }

    void QueueKills(SoundManager sound, int beat)
    {
        if (hurtEveryBeats > 0 && beat > 0 && beat % hurtEveryBeats == 0)
            sound.Hurt();
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
                // No kills: SpikeClick one-shots are scheduled on the beat clock instead (UpdateSpike).
                break;
            case Scenario.styles:
                for (int color = 0; color < Colors; color++)
                    sound.QueueKillChord(color);
                break;
            case Scenario.features:
            case Scenario.combobreak:
                break; // no kills
            case Scenario.fever:
                if (beat >= feverAtBeat && (beat == feverAtBeat || (feverEveryBeats > 0 && (beat - feverAtBeat) % feverEveryBeats == 0)))
                    sound.QueueFever();
                if (!feverWithoutKills)
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
        _transport.PhraseStarted -= OnPhrase;
        _transport.Resynced -= OnResync;
        sound.MusicParameterSet -= OnMusicParameter;
        File.WriteAllText(Path.ChangeExtension(path, ".frames.csv"), "dt,beat,bpm,forced\n" + string.Join("\n", _frameLog) + "\n");
        File.WriteAllText(Path.ChangeExtension(path, ".studio.csv"), "frame,beat_now,dt,studio_update,core_dsp\n" + string.Join("\n", _studioLog) + "\n");
        File.WriteAllText(Path.ChangeExtension(path, ".params.csv"), "beat_now,name,value\n" + string.Join("\n", _paramLog) + "\n");
        File.WriteAllText(Path.ChangeExtension(path, ".beats.csv"), BeatLog(_beats));
        if (_beats2.Count > 0)
            File.WriteAllText(Path.ChangeExtension(path, ".beats2.csv"), BeatLog(_beats2));
        string spikeSummary = "";
        if (scenario == Scenario.spike)
        {
            var spike = new StringBuilder("beat,lead,scheduled,target_dsp,anchor_beat,lead_samples\n");
            foreach (var line in _spikeLog)
                spike.Append(line).Append('\n');
            File.WriteAllText(Path.ChangeExtension(path, ".spike.csv"), spike.ToString());
            spikeSummary = string.Format(CultureInfo.InvariantCulture,
                "; SPIKE offset {0} samples, dispatch window {6} beats, scheduled {1}, started {2}, late (scheduler, not started) {3}, dropped (backend, group too late) {4}, cancelled {5}",
                scheduleOffsetSamples, _spikeLog.Count, _backend.StartedCount, _scheduler.LateCount, _backend.DroppedCount, _backend.CancelledCount,
                spikeDispatchBeats);
        }

        var scheduler = (OneShotScheduler)_killSchedulerField.GetValue(sound);
        int killStarted = _killBackend.StartedOn(OutputBus.KillChords), killDropped = _killBackend.DroppedOn(OutputBus.KillChords);
        string killSummary = string.Format(CultureInfo.InvariantCulture,
            "; KILLS one-shots started {0}, late (scheduler) {1}, dropped (backend) {2}",
            killStarted, _killLate, killDropped) +
            string.Format(CultureInfo.InvariantCulture,
            "; PATTERNS scheduled {0}, started {1}, late (scheduler) {2}, dropped (backend) {3}; FIRSTANCHOR now beat {4:F5}; " +
            "CAPTURE dsp-to-frame mismatches {5}",
            _patternScheduled, _killBackend.StartedCount - killStarted, _patternLate, _killBackend.DroppedCount - killDropped,
            _firstAnchorBeat, AudioCapture.DspOffsetMismatches);
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
        // Every lost one-shot with its own beat and owner (kill = KillChordPlanner, pattern = PatternScheduler).
        File.WriteAllText(Path.ChangeExtension(path, ".losses.csv"), "beat_now,note_beat,owner,kind,bus\n" + string.Join("\n", _lossLog) + "\n");
        if (logOneShots)
            File.WriteAllText(Path.ChangeExtension(path, ".patterns.csv"),
                "frame,beat,instrument,bus,midi,onset_s,beat_dsp,start_dsp\n" + string.Join("\n", _patternLog) + "\n");
        if (logOneShots)
        {
            _killBackend.DiagnosticLog = null;
            File.WriteAllText(Path.ChangeExtension(path, ".oneshots.csv"),
                "frame,backend,event,handle,now_dsp,start_dsp,...\n" + string.Join("\n", _oneShotLog) + "\n");
        }
        if (scenario == Scenario.combobreak)
            File.WriteAllText(Path.ChangeExtension(path, ".ladder.csv"),
                "frame,beat,combo,rung,floor,scale,visual_beat,capture_frame,fever,feature\n" + string.Join("\n", _ladderLog) + "\n");
        if (scenario == Scenario.duck || meterFeatureDuck)
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
        if (stallMs > 0)
            summary += string.Format(CultureInfo.InvariantCulture, "; forced stalls {0} ({1} ms at {2}; not counted in the stalls above)",
                _forcedStalls, stallMs, stallAtBeats == null ? "-" : string.Join("/", Array.ConvertAll(stallAtBeats, v => v.ToString("0.###", CultureInfo.InvariantCulture))));
        summary += "; RESYNCED " + _resyncs;
        summary += "; CHANNELS playing peak " + _channelsPeak + ", real peak " + _realChannelsPeak;
        if (_stopFrame >= 0)
            summary += "; STOP frame " + _stopFrame + (_restartFrame >= 0 ? "; RESTART frame " + _restartFrame : "");
        foreach (var line in _stallLog)
            summary += "\n" + line;
        Debug.Log(summary);
        File.WriteAllText(Path.ChangeExtension(path, ".cpu.txt"), summary + "\n");
#if UNITY_EDITOR
        if (exitPlayModeWhenDone)
            UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // beat, capture frame of the beat's DSP sample, BPM of its phrase, and the DSP sample itself.
    static string BeatLog(SortedDictionary<int, (ulong dsp, double bpm)> beats)
    {
        var log = new StringBuilder("beat,frame,bpm,dsp\n");
        foreach (var pair in beats)
            log.Append(pair.Key).Append(',')
                .Append(Math.Round(AudioCapture.FrameAtDsp(pair.Value.dsp)).ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                .Append(pair.Value.bpm.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(pair.Value.dsp.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return log.ToString();
    }

    void OnDestroy()
    {
        if (_transport != null)
        {
            _transport.PhraseStarted -= OnPhrase;
            _transport.Resynced -= OnResync;
        }
        if (soloLayers != Solo.none)
            UnmuteSoloBuses();
        if (_killBackend != null)
            _killBackend.NoteDropped -= OnNoteDropped;
        if (_subscribedShots != null)
            _subscribedShots.NoteLate -= OnNoteLate;
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
