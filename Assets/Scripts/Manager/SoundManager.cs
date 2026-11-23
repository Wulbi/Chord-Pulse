using System;
using System.Collections;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

[DefaultExecutionOrder(-100), DisallowMultipleComponent]
public sealed class SoundManager : SingletonBehaviour<SoundManager>
{
    public const int BeatsPerBar = 4;
    public const int StepsPerBeat = 2;
    public const int StepsPerBar = BeatsPerBar * StepsPerBeat;

    const string BankName = "ChordPulse";
    const string MusicPath = "event:/Music/ChordPulseGame";
    const int MaxChordVoicing = 3;
    const int FeverSteps = 4 * StepsPerBar;
    const int FeverBuildSteps = StepsPerBar;

    public const int MaxIntensity = 3;

    enum FeverPhase { Fever, Build, Impact, Outro }
    static readonly string[] FeverParameters = { "Fever", "FeverBuild", "FeverImpact", "FeverOutro" };
    const double ChordLookahead = 0.09;
    const double RhythmLookahead = 0.15;

    static readonly string[] KillParameters = { "RedKills", "BlueKills", "YellowKills" };

    [Tooltip("Shifts visuals later to match audible output latency (measure per device).")]
    [SerializeField] float visualLatencyMs;
    [Tooltip("Accompaniment level (drums, bass, harmony). Drives the FMOD MusicMix parameter.")]
    [SerializeField, Range(0f, 1f)] float musicMix = 1f;
    [Tooltip("Kill chord level, mixed independently of the accompaniment. Drives KillChordMix.")]
    [SerializeField, Range(0f, 1f)] float killChordMix = 1f;

    float _appliedMusicMix = -1f;
    float _appliedKillChordMix = -1f;

    int _intensity;
    int _appliedIntensity = -1;

    /// <summary>
    /// Arrangement density for normal sections: 0 kick/snare/closed hat/bass, 1 +pluck, 2 +open hats, 3 +clap.
    /// Fever always plays the full arrangement.
    /// </summary>
    public int Intensity { get => _intensity; set => _intensity = Mathf.Clamp(value, 0, MaxIntensity); }

    public float MusicMix { get => musicMix; set => musicMix = Mathf.Clamp01(value); }
    public float KillChordMix { get => killChordMix; set => killChordMix = Mathf.Clamp01(value); }

    readonly SortedDictionary<int, int[]> _pendingKillChords = new SortedDictionary<int, int[]>();
    readonly MusicConductor _conductor = new MusicConductor();
    FMOD.Studio.EventInstance _music;
    Coroutine _startRoutine;
    bool _ownsBank;
    bool _musicPlaying;
    readonly bool[] _appliedFever = new bool[FeverParameters.Length];
    int _feverStart = -1;
    int _lastChordStep = -1;
    // Beat position as FMOD is mixing it (drives audio scheduling) and as the player hears it (drives visuals).
    double _audioBeat = -1;
    double _visualBeat = -1;
    double _roundSeconds;

    bool Running => _musicPlaying && _conductor.HasStarted;

    /// <summary>Play time since the first beat (stops while paused); use for difficulty, not for rhythm.</summary>
    public double RoundSeconds => Running ? _roundSeconds : 0;
    public double BeatPosition => Running ? _visualBeat : -1;
    public int CurrentBeat => Running ? (int)Math.Floor(_visualBeat) : -1;
    public int CurrentStep => Running ? (int)Math.Floor(_visualBeat * StepsPerBeat) : -1;
    public int CurrentBpm => Running ? (int)Math.Round(_conductor.Tempo) : 0;
    public string CurrentSection => _conductor.Section;
    public bool FeverActive => Running && IsFeverStep(CurrentStep);
    public bool FeverPending => Running && _feverStart >= 0 && CurrentStep < _feverStart;
    /// <summary>True during the build-up bar right before fever lands (for visual tension cues).</summary>
    public bool FeverBuilding => FeverPending && CurrentStep >= _feverStart - FeverBuildSteps;

    public static bool IsKickBeat(int beat) => beat % 2 == 0;

    int AudioStep => (int)Math.Floor(_audioBeat * StepsPerBeat);
    double AudioStepPosition => _audioBeat * StepsPerBeat;
    double LookaheadSteps(double seconds) => _conductor.SecondsToBeats(seconds) * StepsPerBeat;

    bool IsFeverStep(int step) =>
        _feverStart >= 0 && step >= _feverStart && step < _feverStart + FeverSteps;

    public void StartMusic()
    {
        StopMusic();
        _audioBeat = _visualBeat = -1;
        _roundSeconds = 0;
        _feverStart = -1;
        Array.Clear(_appliedFever, 0, _appliedFever.Length);
        _intensity = 0;
        _appliedIntensity = -1;
        _lastChordStep = -1;
        _startRoutine = StartCoroutine(LoadAndStartMusic());
    }

    IEnumerator LoadAndStartMusic()
    {
        try
        {
            if (!RuntimeManager.HasBankLoaded(BankName))
            {
                RuntimeManager.LoadBank(BankName, true);
                _ownsBank = true;
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception.Message);
            yield break;
        }

        FMOD.Studio.Bank bank;
        if (!Check(RuntimeManager.StudioSystem.getBank("bank:/" + BankName, out bank)))
            yield break;
        if (!Check(bank.loadSampleData()))
            yield break;

        while (true)
        {
            FMOD.Studio.LOADING_STATE state;
            if (!Check(bank.getSampleLoadingState(out state)))
                yield break;
            if (state == FMOD.Studio.LOADING_STATE.LOADED)
                break;
            if (state == FMOD.Studio.LOADING_STATE.ERROR)
            {
                ReportFailure("Music sample data failed to load.");
                yield break;
            }
            yield return null;
        }

        try
        {
            _music = RuntimeManager.CreateInstance(MusicPath);
        }
        catch (Exception exception)
        {
            ReportFailure(exception.Message);
            yield break;
        }

        _conductor.Attach(_music);
        _appliedMusicMix = _appliedKillChordMix = -1f;
        if (!ApplyMix())
            yield break;
        foreach (var parameter in FeverParameters)
            if (!Check(_music.setParameterByName(parameter, 0f, true)))
                yield break;
        foreach (var parameter in KillParameters)
            if (!Check(_music.setParameterByName(parameter, 0f, true)))
                yield break;
        if (!Check(_music.start()))
            yield break;

        _musicPlaying = true;
        _startRoutine = null;
    }

    public void StopMusic()
    {
        if (_startRoutine != null)
        {
            StopCoroutine(_startRoutine);
            _startRoutine = null;
        }
        _musicPlaying = false;
        _conductor.Detach();
        _pendingKillChords.Clear();
        if (_music.isValid())
        {
            _music.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            _music.release();
            _music.clearHandle();
        }
    }

    public void QueueFever()
    {
        if (!Running || FeverActive || FeverPending)
            return;
        double earliest = AudioStepPosition + LookaheadSteps(RhythmLookahead);
        int nextBar = (AudioStep / StepsPerBar + 1) * StepsPerBar;
        while (nextBar < earliest)
            nextBar += StepsPerBar;
        // The next whole bar is the build-up (snare roll + riser, kick drops out); fever lands on the bar after.
        _feverStart = nextBar + FeverBuildSteps;
    }

    public void QueueKillChord(int colorIndex)
    {
        if (!Running || colorIndex < 0 || colorIndex >= KillParameters.Length)
            return;

        int target = (AudioStep / StepsPerBeat + 1) * StepsPerBeat;
        if (target <= AudioStepPosition + LookaheadSteps(ChordLookahead))
            target += StepsPerBeat;
        int[] kills;
        if (!_pendingKillChords.TryGetValue(target, out kills))
        {
            kills = new int[KillParameters.Length];
            _pendingKillChords.Add(target, kills);
        }
        kills[colorIndex] = Math.Min(kills[colorIndex] + 1, MaxChordVoicing);
    }

    void Update()
    {
        if (!_musicPlaying)
            return;
        if (!ApplyMix())
            return;
        _conductor.Update();
        if (!_conductor.HasStarted)
            return;
        _audioBeat = _conductor.BeatPosition();
        _visualBeat = Math.Max(0, _audioBeat - _conductor.SecondsToBeats(visualLatencyMs / 1000.0));
        _roundSeconds += Time.deltaTime;

        UpdateFever();
        UpdateKillChords();
    }

    void UpdateFever()
    {
        // Parameters are set slightly ahead so FMOD sees them before the instruments they gate start.
        double step = AudioStepPosition + LookaheadSteps(RhythmLookahead);
        bool scheduled = _feverStart >= 0;
        SetFeverParameter(FeverPhase.Build, scheduled && step >= _feverStart - FeverBuildSteps && step < _feverStart);
        SetFeverParameter(FeverPhase.Impact, scheduled && step >= _feverStart && step < _feverStart + StepsPerBeat);
        bool fever = scheduled && step >= _feverStart && step < _feverStart + FeverSteps;
        SetFeverParameter(FeverPhase.Fever, fever);
        SetFeverParameter(FeverPhase.Outro, fever && step >= _feverStart + FeverSteps - StepsPerBar);

        int intensity = fever ? MaxIntensity : _intensity;
        if (intensity != _appliedIntensity && Check(_music.setParameterByName("Intensity", intensity)))
            _appliedIntensity = intensity;
    }

    void SetFeverParameter(FeverPhase phase, bool on)
    {
        int index = (int)phase;
        if (_appliedFever[index] == on)
            return;
        if (Check(_music.setParameterByName(FeverParameters[index], on ? 1f : 0f, true)))
            _appliedFever[index] = on;
    }

    void UpdateKillChords()
    {
        int target = (AudioStep / StepsPerBeat + 1) * StepsPerBeat;
        if (target == _lastChordStep || target > AudioStepPosition + LookaheadSteps(ChordLookahead))
            return;

        var totals = new int[KillParameters.Length];
        // Carry delayed frames forward to the next playable beat instead of dropping kills.
        while (_pendingKillChords.Count > 0)
        {
            int step;
            int[] kills;
            using (var pending = _pendingKillChords.GetEnumerator())
            {
                pending.MoveNext();
                step = pending.Current.Key;
                kills = pending.Current.Value;
            }
            if (step > target)
                break;
            _pendingKillChords.Remove(step);
            for (int color = 0; color < totals.Length; color++)
                totals[color] = Math.Min(totals[color] + kills[color], MaxChordVoicing);
        }

        for (int color = 0; color < totals.Length; color++)
            if (!Check(_music.setParameterByName(KillParameters[color], totals[color], true)))
                return;
        _lastChordStep = target;
    }

    // Accompaniment and kill chords sit on separate FMOD group tracks; only push values when they change.
    bool ApplyMix()
    {
        if (!Mathf.Approximately(musicMix, _appliedMusicMix))
        {
            if (!Check(_music.setParameterByName("MusicMix", musicMix)))
                return false;
            _appliedMusicMix = musicMix;
        }
        if (!Mathf.Approximately(killChordMix, _appliedKillChordMix))
        {
            if (!Check(_music.setParameterByName("KillChordMix", killChordMix)))
                return false;
            _appliedKillChordMix = killChordMix;
        }
        return true;
    }

    bool Check(FMOD.RESULT result)
    {
        if (result == FMOD.RESULT.OK)
            return true;
        ReportFailure(result.ToString());
        return false;
    }

    void ReportFailure(string message)
    {
        StopMusic();
        Debug.LogError("[SoundManager / FMOD] " + message, this);
    }

    void OnDestroy()
    {
        StopMusic();
        if (_ownsBank)
            RuntimeManager.UnloadBank(BankName);
    }
}
