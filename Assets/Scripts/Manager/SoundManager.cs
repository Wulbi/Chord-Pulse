using System;
using System.Collections;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

[DefaultExecutionOrder(-100), DisallowMultipleComponent]
public sealed class SoundManager : SingletonBehaviour<SoundManager>, IKillBedState, IArrangement
{
    public const int BeatsPerBar = 4;
    public const int StepsPerBeat = 2;
    public const int StepsPerBar = BeatsPerBar * StepsPerBeat;

    const string BankName = "ChordPulse";
    // WebGL / Android fetch banks over HTTP; give a slow connection time before reporting failure.
    const float BankLoadTimeoutSeconds = 30f;
    // If the mixer clock has not started after this long (browser audio still locked), log a hint once.
    const float AudioStartHintSeconds = 3f;
    const int FeverSteps = 4 * StepsPerBar;
    const int FeverBuildSteps = StepsPerBar;

    public const int MaxIntensity = 3;

    // Beat 0 sounds this long after the samples are loaded (MusicTransport.Start), so the schedulers can hand beat 0's
    // notes to FMOD before it.
    const double StartLeadSeconds = 0.25;
    // QueueFever: the fever's build-up bar must still be at least this far ahead of the mixer's position, otherwise fever
    // waits one more phrase (the value of the old parameter lead it was measured with).
    const double FeverMinNoticeSeconds = 0.25;

    // Kill chords are one-shots of the instrument events (event:/Inst/*, FMOD/Tools/instruments.py) scheduled on the
    // music's beat clock; they play through bus:/Kill Chords, which ducks the Harmony and Feature groups
    // (FMOD/Tools/kill_oneshots.py). Which beat's chord a kill joins and which notes it adds: KillChordPlanner.
    const string KillBusPath = "bus:/Kill Chords";
    readonly KillChordPlanner _killPlanner = new KillChordPlanner();
    readonly List<OneShotNote> _killNotes = new List<OneShotNote>();
    // One scheduler and backend for every one-shot (kill chords and the bed's pattern layers; owners scope CancelFrom).
    // Stop fades come from InstrumentTable.ReleaseSeconds: kill chords their release, pattern layers the timeline's
    // ~5 ms region-end ramp (FMOD/Tools/instruments.py PATTERN_STOP_FADE_MS).
    readonly FmodOneShotBackend _shotBackend = new FmodOneShotBackend();
    OneShotScheduler _shots;
    // The pattern layers (PatternTable: the bed's pitched layers, the drums, the feature loops and the fever layers) as one-shots.
    // Two schedulers: _patterns takes every layer but the core kick / snare / hat. Lookahead: the half-beat dispatch window
    // plus the largest sample onset, with margin (it was sized for the old one-beat window after a phrase boundary). _corePatterns takes the core layers
    // (PatternTable.CoreLayerMask) a bar ahead and hands them to FMOD at once: they depend only on decisions made a phrase
    // ahead (style, fever), so an ordinary frame stall cannot cut them. Every Update / Invalidate / Reset goes to both
    // (UpdatePatterns, InvalidatePatterns, ResetPatterns).
    const double PatternLookaheadBeats = 1.5;
    static readonly double MaxPatternLookaheadBeats = Math.Max(PatternLookaheadBeats, PatternTable.CoreLookaheadBeats);
    PatternScheduler _patterns;
    PatternScheduler _corePatterns;
    ArrangementInputs _arrangement;
    bool _arrangementValid;
    FMOD.Studio.Bus _killBus;
    // MusicMix: bus:/Music Bed (with Harmony below it) and the Feature bus.
    const string MusicBedBusPath = "bus:/Music Bed";
    const string FeatureBusPath = "bus:/Feature";
    FMOD.Studio.Bus _musicBedBus;
    FMOD.Studio.Bus _featureBus;

    [Tooltip("Shifts visuals later to match audible output latency (measure per device).")]
    [SerializeField] float visualLatencyMs;
    [Tooltip("Accompaniment level (drums, bass, harmony, guest loops). Drives the volume of bus:/Music Bed and bus:/Feature (MusicMixGain curve).")]
    [SerializeField, Range(0f, 1f)] float musicMix = 1f;
    [Tooltip("Kill chord level, mixed independently of the accompaniment. Drives the volume of bus:/Kill Chords.")]
    [SerializeField, Range(0f, 1f)] float killChordMix = 1f;
    [Tooltip("Seed for the per-phrase section choice. 0 = different every round.")]
    [SerializeField] int sectionSeed;

    SectionSelector _sections = new SectionSelector(0);
    FeatureSelector _features = new FeatureSelector(0);
    // Variant and guest loop of the queued / playing fever (chosen when fever is queued).
    FeverSelector _fever = new FeverSelector(0);
    float _tension;
    bool _danger;
    float _appliedTension = -1f;
    float _appliedDanger = -1f;
    // The global parameters Tension, Danger and Hurt exist in the loaded bank (they drive bus effects; FMOD/Tools/mixer.py).
    bool _globalsAvailable;
    // Pattern variation per phrase (0..4, see FMOD/Tools/pattern_bed.py), chosen one phrase ahead.
    const int VariationCount = 5;
    System.Random _variationRandom = new System.Random();
    int _variationCurrent;
    int _variationNext;
    int _appliedVariation = -1;
    // Combo layers (0..3, latched on bar lines) and the hurt dip (global "Hurt", snapped to 1, seeks back to 0).
    int _combo;
    int _appliedCombo = -1;
    int _comboBar = -1;
    bool _hurtPending;
    // Bed style per phrase (0 synthpop, 1 latin, 2 funk, 3 dreamy),
    // chosen one phrase ahead and held for a few phrases so the arrangement does not flip-flop.
    public const int StyleSynthpop = 0, StyleLatin = 1, StyleFunk = 2, StyleDreamy = 3;
    const int RotatingStyles = 3;   // synthpop, latin, funk; dreamy is only for breath phrases
    const int LatinGrooveLoopId = 7;
    System.Random _styleRandom = new System.Random();
    int _styleCurrent;
    int _styleNext;
    int _appliedStyle = -1;
    int _phrasesInStyle;
    int _styleDwell;
    int _styleBeforeBreath;
    int _phraseCount;
    int _lastPhrase = -1;
    int _phraseStartStep;
    // Which half of its section's 8 bars a phrase plays (1 = bars 1-4, 2 = bars 5-8; ArrangementTimeline.NextSectionPhrase).
    int _sectionPhraseCurrent = 1;
    int _sectionPhraseNext = 2;

    float _appliedMusicMix = -1f;
    float _appliedKillChordMix = -1f;

    int _intensity;
    int _appliedIntensity = -1;

    /// <summary>
    /// Target musical energy (set from difficulty). Drives arrangement density for normal sections
    /// (0 kick/snare/closed hat/bass, 1 +pluck, 2 +open hats, 3 +clap) and which tempo sections get picked.
    /// Fever always plays the full arrangement; breath phrases drop back to the sparse one.
    /// </summary>
    public int Intensity { get => _intensity; set => _intensity = Mathf.Clamp(value, 0, MaxIntensity); }

    public float MusicMix { get => musicMix; set => musicMix = Mathf.Clamp01(value); }
    public float KillChordMix { get => killChordMix; set => killChordMix = Mathf.Clamp01(value); }

    // The music clock: beat <-> FMOD master DSP sample (MusicTransport anchors it on every phrase boundary). One instance
    // each for the component's lifetime; the one-shot scheduler holds the clock.
    readonly BeatClock _clock;
    readonly MusicTransport _transport;
    Coroutine _startRoutine;
    bool _ownsBank;
    bool _musicPlaying;
    int _feverStart = -1;
    // Beat position as FMOD is mixing it (drives audio scheduling) and as the player hears it (drives visuals).
    double _audioBeat = -1;
    double _visualBeat = -1;
    readonly DspClockSmoother _dspSmoother = new DspClockSmoother(48000);
    double _roundSeconds;

    SoundManager()
    {
        _clock = new BeatClock(48000);
        _transport = new MusicTransport(_clock);
        _transport.PhraseStarted += OnPhraseStarted;
        _transport.Resynced += OnResynced;
    }

    // From beat 0 as heard on screen; -1 / false during the start lead.
    bool Running => _transport.Running && _visualBeat >= 0;

    /// <summary>Play time since the first beat (stops while paused); use for difficulty, not for rhythm.</summary>
    public double RoundSeconds => Running ? _roundSeconds : 0;
    public double BeatPosition => Running ? _visualBeat : -1;
    public int CurrentBeat => Running ? (int)Math.Floor(_visualBeat) : -1;
    public int CurrentStep => Running ? (int)Math.Floor(_visualBeat * StepsPerBeat) : -1;
    public int CurrentBpm => Running ? (int)Math.Round(_transport.Bpm) : 0;
    public const int StepsPerPhrase = SectionSelector.BarsPerPhrase * StepsPerBar;
    /// <summary>Section (0 = slowest ... 3 = fastest) of the phrase being heard.</summary>
    public int SectionIndex => _sections.Current.Section;
    /// <summary>The phrase being heard is a low-energy breather: thinner music, fewer enemies.</summary>
    public bool IsBreath => Running && _sections.Current.Breath && !FeverActive;
    /// <summary>Already-decided phrase that starts at <see cref="NextPhraseStep"/>.</summary>
    public SectionSelector.Phrase UpcomingPhrase => _sections.Next;
    /// <summary>
    /// Guest loop of the phrase being heard (null = none): the phrase's feature loop, or over fever the fever's guest loop
    /// (none in the build-up bar).
    /// It only sounds in <see cref="FeatureSelector.Loop.PlaysInBar"/> bars of the phrase; see <see cref="FeatureSounding"/>.
    /// </summary>
    public FeatureSelector.Loop CurrentFeature =>
        !Running || FeverBuilding ? null : FeverActive ? _fever.Guest : _features.Current;
    /// <summary>Name of the fever bed variant playing (null outside fever).</summary>
    public string CurrentFeverVariant => FeverActive ? FeverSelector.VariantName(_fever.Variant) : null;
    /// <summary>Loop already chosen for the phrase starting at <see cref="NextPhraseStep"/> (null = none or fever).</summary>
    public FeatureSelector.Loop UpcomingFeature =>
        Running && !FeverOverlaps(NextPhraseStep, NextPhraseStep + StepsPerPhrase) ? _features.Next : null;
    /// <summary>True while the current feature loop is actually playing notes in this bar.</summary>
    /// <summary>
    /// Threat 0..1 from the game (nearest enemy, low health). FMOD fades in a string pad (0.25+), an off-beat
    /// sub pulse (0.45+) and swaps the bass for an acid bass (0.65+). Forced to 0 during fever build-up and fever.
    /// </summary>
    public float Tension { get => _tension; set => _tension = Mathf.Clamp01(value); }
    /// <summary>Last life: the accompaniment is low-passed so it sounds smothered (kill chords stay clear).</summary>
    public bool Danger { get => _danger; set => _danger = value; }
    /// <summary>
    /// Arrangement pattern of the phrase being heard: 0 original, 1 driving 8th bass, 2 driving bass + snare fill,
    /// 3 walking bass + arpeggio + ghost snares, 4 walking bass + chord stabs + ghost snares.
    /// </summary>
    /// <summary>
    /// Kill-streak level 0..3 from the game: 1 adds 16th hats, 2 a glass pad, 3 a bell arpeggio.
    /// Applied on the next bar line; 0 during fever build-up and fever (fever has its own full arrangement).
    /// </summary>
    public int Combo { get => _combo; set => _combo = Mathf.Clamp(value, 0, 3); }
    /// <summary>Combo level FMOD is playing right now.</summary>
    public int CurrentCombo => Running ? Math.Max(0, _appliedCombo) : 0;
    /// <summary>The core was hit: the accompaniment loses its lows and dips for about 0.6 s.</summary>
    public void Hurt() => _hurtPending = true;
    /// <summary>Bed style FMOD is playing: <see cref="StyleSynthpop"/>, <see cref="StyleLatin"/>,
    /// <see cref="StyleFunk"/> or <see cref="StyleDreamy"/> (breath phrases).</summary>
    public int CurrentStyle => Running ? Math.Max(0, _appliedStyle) : StyleSynthpop;
    /// <summary>Bed style already chosen for the phrase starting at <see cref="NextPhraseStep"/>.</summary>
    public int UpcomingStyle => _styleNext;
    public int CurrentVariation => Running ? _appliedVariation : 0;

    // --- Values for visuals: these follow what is being heard. ---

    /// <summary>Bed style of the phrase being heard (changes on the heard phrase downbeat, not ahead of it).</summary>
    public int HeardStyle => !Running || FeverBuilding || FeverActive ? StyleSynthpop : _styleCurrent;
    /// <summary>Pattern variation of the phrase being heard (0 in build-up and fever, like the music).</summary>
    public int HeardVariation => !Running || FeverBuilding || FeverActive ? 0 : _variationCurrent;
    /// <summary>Pattern variation already chosen for the phrase starting at <see cref="NextPhraseStep"/>.</summary>
    public int UpcomingVariation => _variationNext;
    /// <summary>Tension as FMOD is actually playing it (after its seek-speed smoothing), 0 when unavailable.</summary>
    public float HeardTension => ReadFinal("Tension");
    /// <summary>Danger (last-life low-pass) as FMOD is actually playing it.</summary>
    public float HeardDanger => ReadFinal("Danger");
    /// <summary>Hurt dip as FMOD is playing it: 1 right after a hit, back to 0 over about 0.6 s.</summary>
    public float HeardHurt => ReadFinal("Hurt");

    // Final (seek-smoothed) value of a global parameter.
    float ReadFinal(string parameter)
    {
        if (!Running || !_globalsAvailable)
            return 0f;
        return RuntimeManager.StudioSystem.getParameterByName(parameter, out _, out float final) == FMOD.RESULT.OK ? final : 0f;
    }
    public bool FeatureSounding =>
        CurrentFeature != null && CurrentFeature.PlaysInBar(CurrentBar % SectionSelector.BarsPerPhrase);
    /// <summary>First step of the phrase after the one being mixed (from the music clock; 32 before the music starts).</summary>
    public int NextPhraseStep => (Math.Max(0, _transport.Phrase) + 1) * StepsPerPhrase;
    /// <summary>Bar being heard, counted from the first beat (-1 before the music starts).</summary>
    public int CurrentBar => Running ? CurrentBeat / BeatsPerBar : -1;
    /// <summary>Chord of the bar being heard: 0 C, 1 Am, 2 F, 3 G (every section starts on C, phrases are 4 bars).</summary>
    public int ChordIndex => Running ? CurrentBar % SectionSelector.BarsPerPhrase : 0;
    /// <summary>Arrangement density FMOD is actually playing (fever and breath already applied).</summary>
    public int ArrangementIntensity => Math.Max(0, _appliedIntensity);
    /// <summary>Whether the kick drum plays on this beat (it drops out during the fever build-up bar).</summary>
    public bool KickOn(int beat) => beat >= 0 && IsKickBeat(beat) && !FeverBuilding;
    public bool FeverActive => Running && IsFeverStep(CurrentStep);
    public bool FeverPending => Running && _feverStart >= 0 && CurrentStep < _feverStart;
    /// <summary>True during the build-up bar right before fever lands (for visual tension cues).</summary>
    public bool FeverBuilding => FeverPending && CurrentStep >= _feverStart - FeverBuildSteps;

    public static bool IsKickBeat(int beat) => beat % 2 == 0;

    double AudioStepPosition => _audioBeat * StepsPerBeat;
    double LookaheadSteps(double seconds) => seconds * _transport.Bpm / 60.0 * StepsPerBeat;

    bool IsFeverStep(int step) =>
        _feverStart >= 0 && step >= _feverStart && step < _feverStart + FeverSteps;

    // Buses that keep ringing (reverb tails) after the music is stopped; muted by StopMusic, unmuted before the next
    // start. Since FMOD/Tools/mixer.py the feature reverb is on bus:/Feature. Harmony's effects (two compressors) have no
    // tail of their own, so listing it is belt and braces; the Task 8 stop-tail take shows the tail is silent with both
    // buses muted.
    static readonly string[] TailBuses = { "bus:/Feature", "bus:/Music Bed/Harmony" };

    // A bus the loaded banks do not have, or FMOD shutting down, is not an error: the lookup throws and is ignored.
    static void MuteTailBuses(bool mute)
    {
        foreach (var path in TailBuses)
        {
            try
            {
                RuntimeManager.GetBus(path).setMute(mute);
            }
            catch (Exception)
            {
            }
        }
    }

    public void StartMusic()
    {
        StopMusic();
        MuteTailBuses(false);
        _audioBeat = _visualBeat = -1;
        _roundSeconds = 0;
        _feverStart = -1;
        _intensity = 0;
        _appliedIntensity = -1;
        _sections = new SectionSelector(sectionSeed);
        _features = new FeatureSelector(sectionSeed);
        _fever = new FeverSelector(sectionSeed);
        _variationRandom = sectionSeed == 0 ? new System.Random() : new System.Random(unchecked(sectionSeed * 31 + 5));
        _variationCurrent = _variationNext = 0;
        _combo = 0;
        _appliedCombo = _comboBar = -1;
        _styleRandom = sectionSeed == 0 ? new System.Random() : new System.Random(unchecked(sectionSeed * 131 + 9));
        _styleCurrent = _styleNext = StyleSynthpop;
        _appliedStyle = -1;
        _phrasesInStyle = _phraseCount = 0;
        _styleDwell = 4;
        _styleBeforeBreath = StyleSynthpop;
        _hurtPending = false;
        _appliedVariation = -1;
        _tension = 0f;
        _danger = false;
        _appliedTension = _appliedDanger = -1f;
        _globalsAvailable = false;
        _lastPhrase = -1;
        _phraseStartStep = 0;
        _sectionPhraseCurrent = 1;
        _sectionPhraseNext = ArrangementTimeline.NextSectionPhrase(_sections.Current.Section, 1, _sections.Next.Section);
        _arrangementValid = false;
        _killPlanner.Clear();
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

        // On WebGL and Android, LoadBank downloads the bank from StreamingAssets in the background
        // (RuntimeManager.loadFromWeb); asking for it before it arrives fails and used to stop the music for good.
        // On desktop it is already loaded, so this loop exits at once.
        float loadDeadline = Time.realtimeSinceStartup + BankLoadTimeoutSeconds;
        while (!RuntimeManager.HaveAllBanksLoaded)
        {
            if (Time.realtimeSinceStartup > loadDeadline)
            {
                ReportFailure("Timed out loading bank '" + BankName + "' (check StreamingAssets holds this platform's banks).");
                yield break;
            }
            yield return null;
        }
        if (!RuntimeManager.HasBankLoaded(BankName))
        {
            ReportFailure("Bank '" + BankName + "' failed to load (wrong platform's banks in StreamingAssets?).");
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
            _killBus = RuntimeManager.GetBus(KillBusPath);
            _musicBedBus = RuntimeManager.GetBus(MusicBedBusPath);
            _featureBus = RuntimeManager.GetBus(FeatureBusPath);
        }
        catch (Exception exception)
        {
            ReportFailure(exception.Message);
            yield break;
        }
        _appliedMusicMix = _appliedKillChordMix = -1f;
        if (!ApplyMix())
            yield break;
        // Optional: banks built before mixer.py lack the global parameters; then tension, danger and hurt are off.
        _globalsAvailable = SetMusicParameter("Tension", 0f, true) == FMOD.RESULT.OK &&
                            SetMusicParameter("Danger", 0f, true) == FMOD.RESULT.OK &&
                            SetMusicParameter("Hurt", 0f, true) == FMOD.RESULT.OK;
        _appliedTension = _appliedDanger = 0f;
        _appliedCombo = 0;
        _appliedStyle = 0;
        _appliedVariation = 0;
        if (!_globalsAvailable)
            Debug.LogWarning("[SoundManager / FMOD] No global Tension/Danger/Hurt parameters in the bank; tension, danger and hurt are off. Rebuild the FMOD banks.", this);

        // Beat 0 a start lead from now; phrase 0 and the phrase after it at the first section's tempo (OnPhraseStarted
        // announces the next one).
        int rate = FmodClock.SampleRate();
        if (rate <= 0)
        {
            ReportFailure("FMOD mixer sample rate unavailable.");
            yield break;
        }
        // On WebGL the FMOD mixer only runs once the browser has unlocked audio (first click / key press), and its DSP
        // clock stays 0 until it has mixed its first block. 0 means "not started yet" there, not a failure - this used
        // to stop the music for good on itch.io. Wait for the clock; on desktop it is already running.
        ulong startDsp;
        float hintTime = Time.realtimeSinceStartup + AudioStartHintSeconds;
        while ((startDsp = FmodClock.MasterDsp()) == 0)
        {
            if (hintTime > 0f && Time.realtimeSinceStartup > hintTime)
            {
                Debug.Log("[SoundManager / FMOD] Waiting for the audio device to start (in a browser: click or press a key).", this);
                hintTime = -1f;
            }
            yield return null;
        }
        _clock.Reset(rate);
        _dspSmoother.Reset(rate);
        _transport.Start(startDsp + (ulong)Math.Round(StartLeadSeconds * rate),
            SectionSelector.Bpm(_sections.Current.Section), SectionSelector.Bpm(_sections.Next.Section));

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
        Shots.StopAll();
        _shotBackend.StopAll(); // also the release tails the scheduler has already let go of
        ResetPatterns();
        _arrangementValid = false;
        _killPlanner.Clear();
        _transport.Stop();
        _audioBeat = _visualBeat = -1;
        MuteTailBuses(true);
    }

    public void QueueFever()
    {
        if (!Running || FeverActive || FeverPending)
            return;
        // Fever never cuts into a phrase: it fills exactly one whole phrase, and its build-up is the last bar of the
        // phrase before. That build-up bar must still be far enough ahead and free of the current guest loop
        // (loops that play in bar 4 would clash with the snare roll), otherwise wait one more phrase.
        // "Now" is read from the mixer clock here, not taken from this frame's Update: QueueFever may run after a stall,
        // and a boundary the mixer has already passed must not be held (its tempo is fixed).
        if (!_clock.BeatAt(FmodClock.MasterDsp(), out double nowBeat))
            return;
        double earliest = nowBeat * StepsPerBeat + LookaheadSteps(FeverMinNoticeSeconds);
        int lastBar = SectionSelector.BarsPerPhrase - 1;
        int feverStart = NextPhraseStep;
        bool loopInBuildBar = _features.Current != null && _features.Current.PlaysInBar(lastBar);
        if (feverStart - FeverBuildSteps < earliest || loopInBuildBar)
            feverStart += StepsPerPhrase;
        _feverStart = feverStart;
        _fever.Choose();

        // Kill beats already queued from the build-up bar on were voiced for the old bed; re-voice them as fever's sound.
        {
            int buildBeat = (feverStart - FeverBuildSteps) / StepsPerBeat;
            _killNotes.Clear();
            int firstBeat = _killPlanner.Restate(nowBeat, buildBeat, this, _killNotes);
            Shots.CancelFrom(firstBeat, _killPlanner); // exactly the beats Restate re-emitted
            ScheduleKillNotes();
        }

        // The guest loop already picked for the next phrase would either be silenced by fever or still be playing
        // in the build-up bar, so drop it. FeatureSelector will not pick another one until fever is over.
        _features.CancelNext();

        // The next section's tempo is already announced to the clock; keep the tempo steady through build-up and fever.
        if (FeverHoldsBoundary(NextPhraseStep))
        {
            _sections.HoldNext();
            _sectionPhraseNext = ArrangementTimeline.NextSectionPhrase(_sections.Current.Section, _sectionPhraseCurrent, _sections.Next.Section);
            OnNextSectionChanged();
        }
        // Pattern notes already scheduled from the build-up bar on change too (style / variation / combo 0 there, fever
        // layers, no core kick): RefreshArrangement invalidates both schedulers from the first changed step, the build-up
        // bar's first step (the core one may already have handed that bar's kicks to FMOD; they are cancelled unstarted).
        if (_clock.BeatAt(FmodClock.MasterDsp(), out double now))
            RefreshArrangement(now);
    }

    /// <summary>Length of one beat at the tempo being mixed (108 BPM before the music starts).</summary>
    public double BeatSeconds => _transport.Running ? 60.0 / _transport.Bpm : 60.0 / 108.0;

    /// <summary>
    /// Adds a kill to a beat's chord and returns that beat's step, or -1 if nothing was queued.
    /// Pass the step a pulse already used so every kill of one pulse joins the same chord; if that chord has
    /// already sounded, the kill stays silent instead of starting a second chord on the next beat.
    /// The notes the kill adds to the chord are scheduled at once as one-shots.
    /// </summary>
    public int QueueKillChord(int colorIndex, int chordStep = -1)
    {
        if (!Running || colorIndex < 0 || colorIndex >= KillChordPlanner.ColorCount)
            return -1;
        // "Now" from the mixer clock (BeatPosition is the heard beat, behind it by the output latency).
        if (!_clock.BeatAt(FmodClock.MasterDsp(), out double now))
            return -1;
        _killNotes.Clear();
        int target = _killPlanner.Queue(now, colorIndex, chordStep, this, _killNotes);
        ScheduleKillNotes();
        return target;
    }

    /// <summary>The one-shot scheduler shared by the kill chords and the pattern layers.</summary>
    OneShotScheduler Shots => _shots ??= new OneShotScheduler(_clock, _shotBackend);
    PatternScheduler Patterns => _patterns ??= new PatternScheduler(Shots, this, PatternLookaheadBeats, ~PatternTable.CoreLayerMask);
    PatternScheduler CorePatterns => _corePatterns ??= new PatternScheduler(Shots, this, PatternTable.CoreLookaheadBeats,
        PatternTable.CoreLayerMask, PatternTable.CoreLookaheadBeats);

    void UpdatePatterns(double now)
    {
        Patterns.Update(now);
        CorePatterns.Update(now);
    }

    void InvalidatePatterns(double fromBeat, double now, double beatsPerSecond)
    {
        Patterns.Invalidate(fromBeat, now, beatsPerSecond);
        CorePatterns.Invalidate(fromBeat, now, beatsPerSecond);
    }

    void ResetPatterns()
    {
        Patterns.Reset();
        CorePatterns.Reset();
    }

    void ScheduleKillNotes()
    {
        foreach (var note in _killNotes)
            Shots.Schedule(note, _killPlanner, KillChordPlanner.DispatchWindow((int)note.Beat));
        _killNotes.Clear();
    }

    // IArrangement: what the bed's pattern layers play at a step, from the decisions made so far (no lead; fever zone =
    // style 0, variation 0, combo 0; ArrangementTimeline). Cheap and allocation-free (PatternScheduler asks per note).
    ArrangementState IArrangement.At(int step) => ArrangementTimeline.At(step, in _arrangement);

    // The decisions SoundManager has made, as ArrangementTimeline inputs (the same values UpdateStyle / UpdateVariation /
    // UpdateCombo / UpdateFever use).
    ArrangementInputs CurrentArrangement()
    {
        return new ArrangementInputs
        {
            PhraseStartStep = _phraseStartStep,
            StyleCurrent = _styleCurrent,
            StyleNext = _styleNext,
            VariationCurrent = _variationCurrent,
            VariationNext = _variationNext,
            BreathCurrent = _sections.Current.Breath,
            BreathNext = _sections.Next.Breath,
            FeatureCurrent = _features.Current != null ? _features.Current.Id : 0,
            FeatureNext = _features.Next != null ? _features.Next.Id : 0,
            SectionPhraseCurrent = _sectionPhraseCurrent,
            SectionPhraseNext = _sectionPhraseNext,
            Intensity = _intensity,
            LiveCombo = _combo,
            AppliedCombo = _appliedCombo,
            ComboBar = _comboBar,
            FeverStart = _feverStart,
            FeverVariant = _feverStart >= 0 ? _fever.Variant : 0,
            FeverGuest = _feverStart >= 0 && _fever.Guest != null ? _fever.Guest.Id : 0,
        };
    }

    // Takes the current decisions for the pattern layers; if they change a step that may already be scheduled (now ..
    // now + the longer lookahead), both schedulers are invalidated from the first such step (each keeps notes within its
    // onset safety; a change only one scheduler's layers care about re-states the other's notes unchanged).
    void RefreshArrangement(double now)
    {
        var next = CurrentArrangement();
        if (_arrangementValid)
        {
            int from = (int)Math.Floor(now * StepsPerBeat);
            int to = (int)Math.Ceiling((now + MaxPatternLookaheadBeats) * StepsPerBeat);
            int step = ArrangementTimeline.FirstDifferentStep(in _arrangement, in next, from, to);
            if (step >= 0)
                InvalidatePatterns((double)step / StepsPerBeat, now, SafetyBeatsPerSecond());
        }
        _arrangement = next;
        _arrangementValid = true;
    }

    // Tempo for Invalidate's onset safety (seconds -> beats ahead of now): the faster of the playing and the next
    // section's BPM, so the protected span is never too short across a boundary.
    double SafetyBeatsPerSecond()
    {
        double bpm = Math.Max(_transport.Bpm, SectionSelector.Bpm(_sections.Next.Section));
        return Math.Max(bpm, SectionSelector.Bpm(_sections.Current.Section)) / 60.0;
    }

    // IKillBedState: the bed state at a beat, from the step-based helpers.
    bool IKillBedState.FeverZone(int beat) => FeverOverlaps(beat * StepsPerBeat, beat * StepsPerBeat);
    int IKillBedState.StyleAt(int beat) => StyleAt(beat * StepsPerBeat);
    int IKillBedState.ComboAt(int beat) => ComboAt(beat * StepsPerBeat);
    float IKillBedState.Tension => HeardTension;

    // Bed style played at a step (UpdateStyle's rule; fever is handled by the caller).
    int StyleAt(int step) => step >= NextPhraseStep ? _styleNext : _styleCurrent;

    // Combo level played at a step: the applied level up to the bar it was last latched for, the game's after it.
    int ComboAt(int step) => step / StepsPerBar > _comboBar ? _combo : Math.Max(0, _appliedCombo);

    // The next phrase's section was chosen or replaced: the clock times beats past that boundary with its BPM, and
    // one-shots already handed to FMOD for beats after it are moved.
    void OnNextSectionChanged()
    {
        int boundaryBeat = NextPhraseStep / StepsPerBeat;
        _transport.SetNextTempo(SectionSelector.Bpm(_sections.Next.Section));
        Shots.RescheduleFrom(boundaryBeat); // every owner: kill chords and pattern notes
    }

    void Update()
    {
        if (!_musicPlaying)
            return;
        // Output device change with another mixer rate: start over from phrase 0 (rare; the position is not kept).
        int rate = FmodClock.SampleRate();
        if (rate > 0 && rate != _clock.SampleRate)
        {
            StopMusic();
            StartMusic();
            return;
        }
        if (!ApplyMix())
            return;
        // The clock first: phrase boundaries the mixer has passed are handled (OnPhraseStarted, which announces the next
        // tempo) before any game logic of this frame can call QueueFever.
        ulong dsp = FmodClock.MasterDsp();
        _transport.Update(dsp);
        if (!_musicPlaying || !_transport.Running)
            return; // a handler stopped the music
        _transport.BeatAt(dsp, out _audioBeat);
        double bpm = _transport.Bpm;
        double latencyBeats = visualLatencyMs / 1000.0 * bpm / 60.0;
        // The screen reads the mixer clock between mixer blocks (DspClockSmoother) and never goes back (FollowBeat), also
        // across a resync; before beat 0 it is negative and BeatPosition reports -1.
        _transport.BeatAt(
            _dspSmoother.Sample(dsp, System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency),
            out double clockBeat);
        _visualBeat = DspClockSmoother.FollowBeat(_visualBeat, clockBeat - latencyBeats, Time.unscaledDeltaTime * bpm / 60.0);
        if (_visualBeat >= 0)
            _roundSeconds += Time.deltaTime;

        UpdateFever();
        UpdateOneShots();
    }

    // Each 4-bar phrase: the queued phrase becomes current and the one after it is chosen; its tempo goes to the clock,
    // which switches at the next phrase boundary. That leaves a whole phrase of lead time.
    // A phrase boundary inside build-up or fever must not change section, so BPM stays fixed for the whole fever.
    // A boundary exactly at the fever's end is free: the tempo can move as fever finishes.
    bool FeverHoldsBoundary(int boundaryStep) =>
        _feverStart >= 0 && boundaryStep >= _feverStart - FeverBuildSteps && boundaryStep < _feverStart + FeverSteps;

    // MusicTransport.PhraseStarted: once per phrase, in order, also for every phrase a long stall skipped (one Update can
    // raise several). It only decides; notes are scheduled by UpdateOneShots from "now", so nothing is scheduled for a
    // phrase that is already over. After a resync it can come about 0.1 s before the boundary sounds, so it reads the
    // phrase number, not the beat.
    void OnPhraseStarted(int phrase)
    {
        if (phrase <= _lastPhrase)
            return;
        _lastPhrase = phrase;
        _phraseStartStep = phrase * StepsPerPhrase;
        var next = _sections.Advance(_intensity,
            allowBreath: _feverStart < 0 || _phraseStartStep >= _feverStart + FeverSteps,
            hold: FeverHoldsBoundary(_phraseStartStep + StepsPerPhrase));
        _sectionPhraseCurrent = phrase == 0 ? 1 : _sectionPhraseNext;
        _sectionPhraseNext = ArrangementTimeline.NextSectionPhrase(_sections.Current.Section, _sectionPhraseCurrent, next.Section);
        OnNextSectionChanged();
        bool feverNext = FeverOverlaps(NextPhraseStep, NextPhraseStep + StepsPerPhrase);
        _styleCurrent = _styleNext;
        _styleNext = ChooseStyle(next.Breath, feverNext);
        _variationCurrent = _variationNext;
        _variationNext = ChooseVariation(next.Breath);
        // The latin bed already is a percussion groove: the Latin Groove guest would only double it.
        _features.Advance(next, _intensity, allowed: !feverNext,
            excludeId: _styleNext == StyleLatin ? LatinGrooveLoopId : 0);
    }

    // Fever (with its build-up bar) is the highlight: no guest loop plays over any part of it.
    bool FeverOverlaps(double fromStep, double toStep) =>
        _feverStart >= 0 && fromStep < _feverStart + FeverSteps && toStep >= _feverStart - FeverBuildSteps;

    // The applied values (what is being mixed now: ArrangementIntensity, CurrentStyle, CurrentVariation, CurrentCombo, the
    // combo latch) and the global parameters, at the mixer's step. The pattern layers themselves follow
    // ArrangementTimeline per step (CurrentArrangement).
    void UpdateFever()
    {
        double step = AudioStepPosition;
        bool fever = _feverStart >= 0 && step >= _feverStart && step < _feverStart + FeverSteps;

        bool breath = step >= NextPhraseStep ? _sections.Next.Breath : _sections.Current.Breath;
        _appliedIntensity = fever ? MaxIntensity : breath ? 0 : _intensity;

        UpdateTension(fever || FeverOverlaps(step, step));
        UpdateVariation(step);
        UpdateCombo(step, fever || FeverOverlaps(step, step));
        UpdateStyle(step);
    }

    // The original pattern a bit more often than each variant; never the same variant twice in a row.
    // Breath phrases keep the original (sparse) pattern; busier variants get likelier as the round heats up.
    int ChooseVariation(bool breath)
    {
        if (breath)
            return 0;
        var weights = new double[VariationCount];
        double total = 0;
        for (int v = 0; v < VariationCount; v++)
        {
            double w = v == 0 ? 1.6 : 1.0 + (v <= 2 ? 0.15 * _intensity : 0.0);
            if (v != 0 && v == _variationCurrent)
                w = 0;
            weights[v] = w;
            total += w;
        }
        double roll = _variationRandom.NextDouble() * total;
        for (int v = 0; v < VariationCount; v++)
        {
            roll -= weights[v];
            if (roll < 0)
                return v;
        }
        return 0;
    }

    // Synthpop for the first four phrases, then each style is held 2-4 phrases before it may move to another of
    // synthpop / latin / funk. Breath phrases are dreamy and the style resumes afterwards; fever phrases are
    // synthpop (fever's arrangement is built on it).
    int ChooseStyle(bool breath, bool fever)
    {
        _phraseCount++;
        if (fever)
            return SwitchTo(StyleSynthpop);
        if (breath)
        {
            if (_styleCurrent != StyleDreamy)
                _styleBeforeBreath = _styleCurrent;
            return StyleDreamy;
        }
        int style = _styleCurrent == StyleDreamy ? _styleBeforeBreath : _styleCurrent;
        _phrasesInStyle++;
        if (_phraseCount >= 4 && _phrasesInStyle >= _styleDwell && _styleRandom.NextDouble() < 0.6)
            style = (style + 1 + _styleRandom.Next(RotatingStyles - 1)) % RotatingStyles;
        return SwitchTo(style);
    }

    int SwitchTo(int style)
    {
        int from = _styleCurrent == StyleDreamy ? _styleBeforeBreath : _styleCurrent;
        if (style != from)
        {
            _phrasesInStyle = 0;
            _styleDwell = _styleRandom.Next(2, 5);
        }
        return style;
    }

    void UpdateStyle(double step)
    {
        int style = step >= NextPhraseStep ? _styleNext : _styleCurrent;
        if (FeverOverlaps(step, step))
            style = StyleSynthpop;
        _appliedStyle = style;
    }

    void UpdateCombo(double step, bool feverZone)
    {
        if (_hurtPending && _globalsAvailable)
        {
            // Snap to 1, then aim at 0: FMOD's descending seek speed draws the recovery.
            _hurtPending = false;
            if (Check(SetMusicParameter("Hurt", 1f, true)))
                Check(SetMusicParameter("Hurt", 0f));
        }
        // The level is latched per bar (ArrangementInputs.AppliedCombo / ComboBar).
        int bar = (int)Math.Floor(step / StepsPerBar);
        if (bar == _comboBar)
            return;
        _comboBar = bar;
        _appliedCombo = feverZone ? 0 : _combo;
    }

    // Switched on the phrase boundary. Build-up and fever keep the original pattern (they bring their own rolls and fills).
    void UpdateVariation(double step)
    {
        int v = step >= NextPhraseStep ? _variationNext : _variationCurrent;
        if (FeverOverlaps(step, step))
            v = 0;
        _appliedVariation = v;
    }

    // FMOD smooths both global parameters with its own seek speed (fast up, slow down), so values are sent as they are.
    void UpdateTension(bool feverZone)
    {
        if (!_globalsAvailable)
            return;
        float tension = feverZone ? 0f : _tension;
        float danger = feverZone || !_danger ? 0f : 1f;
        if (Mathf.Abs(tension - _appliedTension) >= 0.02f || (tension == 0f && _appliedTension != 0f))
            if (Check(SetMusicParameter("Tension", tension)))
                _appliedTension = tension;
        if (danger != _appliedDanger && Check(SetMusicParameter("Danger", danger)))
            _appliedDanger = danger;
    }

    // MusicTransport.Resynced (the DSP clock went backwards): the clock now restarts at the next bar, so nothing handed
    // over or planned on the old timing may sound; the schedulers start again from the new "now".
    void OnResynced()
    {
        Shots.StopAll();
        _shotBackend.StopAll();
        ResetPatterns();
        _killPlanner.Clear();
    }

    // After this frame's decisions (OnPhraseStarted, UpdateFever): pattern notes up to now + lookahead, then the
    // OneShotScheduler caller contract: scheduler first, then the backend, every frame (a note dispatched by the
    // scheduler gets its start time the same frame; skipping either leaves FMOD instances unreleased).
    // "Now" is the mixer clock, as for the kill chords. The clock is anchored from MusicTransport.Start on, so the
    // first call is during the start lead (now < 0) and beat 0's notes are taken.
    void UpdateOneShots()
    {
        bool hasNow = _clock.BeatAt(FmodClock.MasterDsp(), out double now);
        if (hasNow)
        {
            RefreshArrangement(now);
            UpdatePatterns(now);
        }
        Shots.Update();
        _shotBackend.Update();
        if (hasNow)
            _killPlanner.Expire(now);
    }

    // Accompaniment (bus:/Music Bed and bus:/Feature volume) and kill chords (bus:/Kill Chords volume) are mixed
    // separately; only push values when they change.
    bool ApplyMix()
    {
        if (!Mathf.Approximately(musicMix, _appliedMusicMix))
        {
            float gain = MusicMixGain(musicMix);
            if (!Check(_musicBedBus.setVolume(gain)) || !Check(_featureBus.setVolume(gain)))
                return false;
            _appliedMusicMix = musicMix;
        }
        if (!Mathf.Approximately(killChordMix, _appliedKillChordMix))
        {
            if (!Check(_killBus.setVolume(KillChordGain(killChordMix))))
                return false;
            _appliedKillChordMix = killChordMix;
        }
        return true;
    }

    // The fader curves the old MusicMix / KillChordMix parameters drove (dB at 0, .25, .5, .75, 1), as a linear bus volume.
    // MusicMix: the curve of the old MusicMix automation (Music Bed group volume, Harmony gain) of the music event.
    static readonly float[] MusicMixDb = { -80f, -18f, -9f, -3.5f, 0f };
    static readonly float[] KillChordMixDb = { -80f, -18f, -9f, -3.5f, 0f };

    public static float MusicMixGain(float mix) => DbCurveGain(MusicMixDb, mix);
    public static float KillChordGain(float mix) => DbCurveGain(KillChordMixDb, mix);

    // Linear interpolation in dB between evenly spaced points; 0 is silent (not -80 dB).
    static float DbCurveGain(float[] db, float mix)
    {
        if (mix <= 0f)
            return 0f;
        float x = Mathf.Clamp01(mix) * (db.Length - 1);
        int i = Mathf.Min((int)x, db.Length - 2);
        return Mathf.Pow(10f, Mathf.Lerp(db[i], db[i + 1], x - i) / 20f);
    }

    /// <summary>Raised after every music parameter write that FMOD accepted (capture / diagnostics).</summary>
    public event Action<string, float> MusicParameterSet;

    /// <summary>
    /// The one place the music's parameters are written: the global Tension / Danger / Hurt (FMOD/Tools/mixer.py; they
    /// drive bus effects, and so reach the instrument one-shots).
    /// </summary>
    FMOD.RESULT SetMusicParameter(string name, float value, bool ignoreSeek = false)
    {
        var result = RuntimeManager.StudioSystem.setParameterByName(name, value, ignoreSeek);
        if (result == FMOD.RESULT.OK)
            MusicParameterSet?.Invoke(name, value);
        return result;
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
