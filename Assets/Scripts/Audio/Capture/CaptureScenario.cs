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
    public enum Scenario { baseline, heavy, spike }

    [SerializeField] Scenario scenario = Scenario.baseline;
    [SerializeField] int seed = 7;
    [SerializeField] int beats = 128;
    [Tooltip("Appended to the file name, e.g. _run2 for a reproducibility recording.")]
    [SerializeField] string suffix = "";
    [SerializeField] bool exitPlayModeWhenDone = true;

    [Header("spike (scheduling accuracy)")]
    [Tooltip("OneShotScheduler.ScheduleOffsetSamples for this take (default: the measured constant).")]
    [SerializeField] long scheduleOffsetSamples = OneShotScheduler.DefaultScheduleOffsetSamples;
    [Tooltip("Beats with a SpikeClick scheduled (1 .. this). Even beats are scheduled 1 beat ahead, odd beats 0.3 beat ahead.")]
    [SerializeField] int spikeBeats = 64;

    const int Colors = 3;

    string _wavPath;
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

    IEnumerator Start()
    {
        var sound = SoundManager.Instance;
        typeof(SoundManager).GetField("sectionSeed", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(sound, seed);

        string folder = Path.Combine(Application.dataPath, "..", "Docs", "Captures");
        _wavPath = Path.GetFullPath(Path.Combine(folder, scenario + "_seed" + seed + suffix + ".wav"));
        // Never overwrite a recording (the references are the point): take the next free _runN instead.
        for (int run = 2; File.Exists(_wavPath); run++)
            _wavPath = Path.GetFullPath(Path.Combine(folder, scenario + "_seed" + seed + suffix + "_run" + run + ".wav"));
        _conductor = (MusicConductor)typeof(SoundManager).GetField("_conductor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sound);
        _anchorSeconds = typeof(MusicConductor).GetField("_anchorSeconds", BindingFlags.Instance | BindingFlags.NonPublic);
        _conductor.BeatChanged += OnBeat;
        if (scenario == Scenario.spike)
        {
            _backend = new FmodOneShotBackend();
            _scheduler = new OneShotScheduler(_conductor.Clock, _backend) { ScheduleOffsetSamples = scheduleOffsetSamples };
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
        Debug.Log("[CaptureScenario] " + scenario + " seed " + seed + ", " + beats + " beats -> " + _wavPath);
    }

    void Update()
    {
        if (_finished || !AudioCapture.IsCapturing)
            return;
        var sound = SoundManager.Instance;

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

        if (scenario == Scenario.spike)
            UpdateSpike(sound);

        int beat = sound.CurrentBeat;
        while (beat > _lastBeat && _lastBeat < beats)
        {
            _lastBeat++;
            if (_lastBeat >= beats)
            {
                Finish(sound);
                return;
            }
            QueueKills(sound, _lastBeat);
        }
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
        // R11: the next phrase's BPM is known a phrase ahead (sections are 108 + 8 * index BPM, Docs/AdaptiveAudioPlan.md),
        // so beats past the boundary are timed with it before the landing beat's anchor arrives.
        _conductor.Clock.SetTempoFrom(sound.NextPhraseStep / SoundManager.StepsPerBeat, 108 + 8 * sound.UpcomingPhrase.Section);
        _scheduler.Update();
        _backend.Update();
        // Short lead: odd beats, 0.3 beat ahead of the (visual) beat position.
        double position = sound.BeatPosition;
        while (_nextShort <= spikeBeats && position >= _nextShort - ShortLead)
        {
            if (_conductor.Clock.HasAnchor)
                ScheduleClick(_nextShort, ShortLead);
            else
                _spikeLog.Add(_nextShort + "," + ShortLead.ToString(CultureInfo.InvariantCulture) + ",0,0,-1,0");
            _nextShort += 2;
        }
    }

    void ScheduleClick(int beat, double lead)
    {
        var clock = _conductor.Clock;
        clock.DspAt(beat, out ulong target);
        ulong now = _backend.NowDsp;
        bool ok = _scheduler.Schedule(Instrument.SpikeClick, 72, beat, 0.25, 0f, 0.0);
        _spikeLog.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5}",
            beat, lead, ok ? 1 : 0, target, clock.AnchorBeat, (long)target - (long)now));
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
        }
    }

    void Finish(SoundManager sound)
    {
        _finished = true;
        string path = AudioCapture.End();
        sound.StopMusic();
        _conductor.BeatChanged -= OnBeat;
        var log = new StringBuilder("beat,frame,bpm\n");
        foreach (var (beat, seconds, tempo) in _beats)
            log.Append(beat).Append(',').Append(Math.Round(AudioCapture.FrameAt(seconds)).ToString("F0", CultureInfo.InvariantCulture))
                .Append(',').Append(tempo.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
        File.WriteAllText(Path.ChangeExtension(path, ".beats.csv"), log.ToString());
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

        double dspAvg = _cpuSamples > 0 ? _dspSum / _cpuSamples : 0;
        double totalAvg = _cpuSamples > 0 ? _totalSum / _cpuSamples : 0;
        double studioAvg = _cpuSamples > 0 ? _studioSum / _cpuSamples : 0;
        string summary = string.Format(CultureInfo.InvariantCulture,
            "[CaptureScenario] DONE {0}: STUDIO update avg {9:F2}% max {10:F2}%; CORE dsp avg {1:F2}% max {2:F2}%, core total (dsp+stream+update) avg {3:F2}% max {4:F2}%, samples {5}, dropped frames {6}, stalls >100 ms after the first beat {7} (take is only a valid reference if 0), longest frame {8:F0} ms",
            Path.GetFileName(path), dspAvg, _dspMax, totalAvg, _totalMax, _cpuSamples, AudioCapture.FramesDropped,
            _stalls, _longestFrame * 1000f, studioAvg, _studioMax) + spikeSummary;
        Debug.Log(summary);
        File.WriteAllText(Path.ChangeExtension(path, ".cpu.txt"), summary + "\n");
#if UNITY_EDITOR
        if (exitPlayModeWhenDone)
            UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    void OnDestroy()
    {
        if (AudioCapture.IsCapturing && !_finished)
        {
            Debug.LogWarning("[CaptureScenario] Stopped early at beat " + _lastBeat + " of " + beats + "; recording is incomplete.");
            AudioCapture.End();
        }
    }
}
