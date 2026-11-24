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
    // A kill targets the next beat if it is at least this far away, otherwise the beat after (Docs/AdaptiveAudioPlan.md
    // section 10), so the chord's one-shots always get enough lead to be scheduled sample-accurately.
    const double ChordLeadBeats = 0.4;
    // FMOD Studio stalls about 0.5 s around a section-jump landing; one-shots whose instance is created during the stall
    // get their channel group too late and are dropped (never played late, ruling R12). Notes on the first beats after
    // a phrase boundary are therefore handed to FMOD up to this many beats early instead of half a beat (ruling R15).
    const double PostBoundaryDispatchBeats = 1.0;
    const int PostBoundaryBeats = 1;
    // Arrangement parameters (fever, intensity, style, variation, feature loop...) switch this far before the bar
    // they affect. At 0.15 s FMOD started the newly enabled notes late at every phrase boundary ("Loading delay
    // exceeded": ~30 per 40 beats); 0.25 s brought that to ~0-7 (10-5). Longer leads let more of the old bar's tail
    // follow the new state.
    const double RhythmLookahead = 0.25;

    // Kill chords are one-shots of the instrument events (event:/Inst/*, FMOD/Tools/instruments.py) scheduled on the
    // music's beat clock; they play through bus:/Kill Chords, which ducks the Harmony and Feature groups
    // (FMOD/Tools/kill_oneshots.py). Per beat: how many kills of each colour (voicing 1..3) and the bed state it plays in.
    const int KillColorCount = 3;
    const string KillBusPath = "bus:/Kill Chords";
    sealed class KillBeat
    {
        public readonly int[] Voicing = new int[KillColorCount];
        public int Style;
        public int Combo;
        public float Tension;
    }
    readonly Dictionary<int, KillBeat> _killBeats = new Dictionary<int, KillBeat>();
    readonly List<int> _expiredKillBeats = new List<int>();
    readonly List<KillNote> _voicedBefore = new List<KillNote>();
    readonly List<KillNote> _voicedAfter = new List<KillNote>();
    readonly FmodOneShotBackend _killBackend = new FmodOneShotBackend();
    OneShotScheduler _killScheduler;
    FMOD.Studio.Bus _killBus;

    [Tooltip("Shifts visuals later to match audible output latency (measure per device).")]
    [SerializeField] float visualLatencyMs;
    [Tooltip("Accompaniment level (drums, bass, harmony). Drives the FMOD MusicMix parameter.")]
    [SerializeField, Range(0f, 1f)] float musicMix = 1f;
    [Tooltip("Kill chord level, mixed independently of the accompaniment. Drives the volume of bus:/Kill Chords.")]
    [SerializeField, Range(0f, 1f)] float killChordMix = 1f;
    [Tooltip("Seed for the per-phrase section choice. 0 = different every round.")]
    [SerializeField] int sectionSeed;

    SectionSelector _sections = new SectionSelector(0);
    FeatureSelector _features = new FeatureSelector(0);
    float _tension;
    bool _danger;
    float _appliedTension = -1f;
    float _appliedDanger = -1f;
    bool _tensionAvailable;
    // Pattern variation per phrase (FMOD "Variation" 0..4, see FMOD/Tools/bed_variation.py), chosen one phrase ahead.
    const int VariationCount = 5;
    System.Random _variationRandom = new System.Random();
    int _variationCurrent;
    int _variationNext;
    int _appliedVariation = -1;
    bool _variationAvailable;
    // Combo layers (FMOD "Combo" 0..3, changed on bar lines) and the hurt dip ("Hurt", snapped to 1, seeks back to 0).
    int _combo;
    int _appliedCombo = -1;
    int _comboBar = -1;
    bool _hurtPending;
    bool _comboAvailable;
    // Bed style per phrase (FMOD "BedStyle": 0 synthpop, 1 latin, 2 funk, 3 dreamy; see FMOD/Tools/bed_style.py),
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
    bool _styleAvailable;
    int _appliedFeature = -1;
    bool _featuresAvailable;
    int _lastPhrase = -1;
    int _phraseStartStep;

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
    public const int StepsPerPhrase = SectionSelector.BarsPerPhrase * StepsPerBar;
    /// <summary>Section (0 = slowest ... 3 = fastest) of the phrase being heard.</summary>
    public int SectionIndex => _sections.Current.Section;
    /// <summary>The phrase being heard is a low-energy breather: thinner music, fewer enemies.</summary>
    public bool IsBreath => Running && _sections.Current.Breath && !FeverActive;
    /// <summary>Already-decided phrase that starts at <see cref="NextPhraseStep"/>.</summary>
    public SectionSelector.Phrase UpcomingPhrase => _sections.Next;
    /// <summary>
    /// Guest trumpet/whistle loop of the phrase being heard (null = none, or cut off by fever).
    /// It only sounds in <see cref="FeatureSelector.Loop.PlaysInBar"/> bars of the phrase; see <see cref="FeatureSounding"/>.
    /// </summary>
    public FeatureSelector.Loop CurrentFeature =>
        Running && _featuresAvailable && !FeverActive && !FeverBuilding ? _features.Current : null;
    /// <summary>Loop already chosen for the phrase starting at <see cref="NextPhraseStep"/> (null = none or fever).</summary>
    public FeatureSelector.Loop UpcomingFeature =>
        Running && _featuresAvailable && !FeverOverlaps(NextPhraseStep, NextPhraseStep + StepsPerPhrase) ? _features.Next : null;
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

    // --- Values for visuals. FMOD gets parameters slightly ahead (lookahead); these follow what is being heard. ---

    /// <summary>Bed style of the phrase being heard (changes on the heard phrase downbeat, not ahead of it).</summary>
    public int HeardStyle => !Running || !_styleAvailable || FeverBuilding || FeverActive ? StyleSynthpop : _styleCurrent;
    /// <summary>Pattern variation of the phrase being heard (0 in build-up and fever, like the music).</summary>
    public int HeardVariation => !Running || !_variationAvailable || FeverBuilding || FeverActive ? 0 : _variationCurrent;
    /// <summary>Pattern variation already chosen for the phrase starting at <see cref="NextPhraseStep"/>.</summary>
    public int UpcomingVariation => _variationAvailable ? _variationNext : 0;
    /// <summary>Tension as FMOD is actually playing it (after its seek-speed smoothing), 0 when unavailable.</summary>
    public float HeardTension => ReadFinal("Tension", _tensionAvailable);
    /// <summary>Danger (last-life low-pass) as FMOD is actually playing it.</summary>
    public float HeardDanger => ReadFinal("Danger", _tensionAvailable);
    /// <summary>Hurt dip as FMOD is playing it: 1 right after a hit, back to 0 over about 0.6 s.</summary>
    public float HeardHurt => ReadFinal("Hurt", _comboAvailable);

    float ReadFinal(string parameter, bool available)
    {
        if (!Running || !available)
            return 0f;
        return _music.getParameterByName(parameter, out _, out float final) == FMOD.RESULT.OK ? final : 0f;
    }
    public bool FeatureSounding =>
        CurrentFeature != null && CurrentFeature.PlaysInBar(CurrentBar % SectionSelector.BarsPerPhrase);
    public int NextPhraseStep => _phraseStartStep + StepsPerPhrase;
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
        _sections = new SectionSelector(sectionSeed);
        _features = new FeatureSelector(sectionSeed);
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
        _styleAvailable = false;
        _hurtPending = false;
        _comboAvailable = false;
        _appliedVariation = -1;
        _variationAvailable = false;
        _tension = 0f;
        _danger = false;
        _appliedTension = _appliedDanger = -1f;
        _tensionAvailable = false;
        _appliedFeature = -1;
        _featuresAvailable = false;
        _lastPhrase = -1;
        _phraseStartStep = 0;
        _lastChordStep = -1;
        _killBeats.Clear();
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
        try
        {
            _killBus = RuntimeManager.GetBus(KillBusPath);
        }
        catch (Exception exception)
        {
            ReportFailure(exception.Message);
            yield break;
        }
        _appliedMusicMix = _appliedKillChordMix = -1f;
        if (!ApplyMix())
            yield break;
        foreach (var parameter in FeverParameters)
            if (!Check(_music.setParameterByName(parameter, 0f, true)))
                yield break;
        if (!Check(_music.setParameterByName("NextSection", 0f, true)))
            yield break;
        // Optional: banks built before the feature loops existed simply play without them.
        _featuresAvailable = _music.setParameterByName("FeatureLoop", 0f, true) == FMOD.RESULT.OK;
        _tensionAvailable = _music.setParameterByName("Tension", 0f, true) == FMOD.RESULT.OK &&
                            _music.setParameterByName("Danger", 0f, true) == FMOD.RESULT.OK;
        _appliedTension = _appliedDanger = 0f;
        _variationAvailable = _music.setParameterByName("Variation", 0f, true) == FMOD.RESULT.OK;
        _comboAvailable = _music.setParameterByName("Combo", 0f, true) == FMOD.RESULT.OK &&
                          _music.setParameterByName("Hurt", 0f, true) == FMOD.RESULT.OK;
        _appliedCombo = 0;
        _styleAvailable = _music.setParameterByName("BedStyle", 0f, true) == FMOD.RESULT.OK;
        _appliedStyle = 0;
        _appliedVariation = 0;
        if (!_tensionAvailable)
            Debug.LogWarning("[SoundManager / FMOD] No Tension/Danger parameters in the bank; tension layers are off. Rebuild the FMOD banks.", this);
        _appliedFeature = 0;
        if (!_featuresAvailable)
            Debug.LogWarning("[SoundManager / FMOD] No FeatureLoop parameter in the bank; feature loops are off. Rebuild the FMOD banks.", this);
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
        KillScheduler.StopAll();
        _killBackend.StopAll(); // also the release tails the scheduler has already let go of
        _killBeats.Clear();
        _conductor.Detach();
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
        // Fever never cuts into a phrase: it fills exactly one whole phrase, and its build-up is the last bar of the
        // phrase before. That build-up bar must still be far enough ahead and free of the current guest loop
        // (loops that play in bar 4 would clash with the snare roll), otherwise wait one more phrase.
        double earliest = AudioStepPosition + LookaheadSteps(RhythmLookahead);
        int lastBar = SectionSelector.BarsPerPhrase - 1;
        int feverStart = NextPhraseStep;
        bool loopInBuildBar = _features.Current != null && _features.Current.PlaysInBar(lastBar);
        if (feverStart - FeverBuildSteps < earliest || loopInBuildBar)
            feverStart += StepsPerPhrase;
        _feverStart = feverStart;

        // The guest loop already picked for the next phrase would either be silenced by fever or still be playing
        // in the build-up bar, so drop it. FeatureSelector will not pick another one until fever is over.
        _features.CancelNext();

        // The next section may already have been sent to FMOD; keep the tempo steady through build-up and fever.
        if (FeverHoldsBoundary(NextPhraseStep))
        {
            Check(_music.setParameterByName("NextSection", _sections.HoldNext().Section));
            OnNextSectionChanged();
        }
    }

    /// <summary>Length of one beat at the tempo FMOD is playing (108 BPM before the music starts).</summary>
    public double BeatSeconds => Running && _conductor.Tempo > 0 ? 60.0 / _conductor.Tempo : 60.0 / 108.0;

    /// <summary>
    /// Adds a kill to a beat's chord and returns that beat's step, or -1 if nothing was queued.
    /// Pass the step a pulse already used so every kill of one pulse joins the same chord; if that chord has
    /// already sounded, the kill stays silent instead of starting a second chord on the next beat.
    /// The notes the kill adds to the chord are scheduled at once as one-shots.
    /// </summary>
    public int QueueKillChord(int colorIndex, int chordStep = -1)
    {
        if (!Running || colorIndex < 0 || colorIndex >= KillColorCount)
            return -1;
        // "Now" from the mixer clock: BeatPosition is capped while a jump landing's beat callback is late.
        if (!_conductor.Clock.BeatAt(MusicConductor.MasterDsp(), out double now))
            return -1;
        // A beat whose time has passed has sounded; its chord is closed.
        _lastChordStep = Math.Max(_lastChordStep, (int)Math.Floor(now) * StepsPerBeat);

        int target;
        if (chordStep >= 0)
        {
            if (chordStep <= _lastChordStep)
                return -1;
            target = chordStep;
        }
        else
        {
            int beat = (int)Math.Floor(now) + 1;
            if (beat <= now + ChordLeadBeats)
                beat++;
            target = beat * StepsPerBeat;
        }
        AddKill(target / StepsPerBeat, colorIndex);
        return target;
    }

    OneShotScheduler KillScheduler => _killScheduler ??= new OneShotScheduler(_conductor.Clock, _killBackend);

    void AddKill(int beat, int color)
    {
        if (!_killBeats.TryGetValue(beat, out var chord))
        {
            // The bed state the beat plays in, fixed by its first kill so later kills add notes of the same sound.
            // Build-up and fever play the original kill chords (style 0, combo 0, tension 0), like the bed.
            int step = beat * StepsPerBeat;
            bool feverZone = FeverOverlaps(step, step);
            chord = new KillBeat
            {
                Style = feverZone ? StyleSynthpop : StyleAt(step),
                Combo = feverZone ? 0 : ComboAt(step),
                Tension = feverZone ? 0f : HeardTension,
            };
            _killBeats.Add(beat, chord);
        }
        int before = chord.Voicing[color];
        int after = Math.Min(before + 1, MaxChordVoicing);
        if (after == before)
            return;
        chord.Voicing[color] = after;

        int chordIndex = beat / BeatsPerBar % SectionSelector.BarsPerPhrase;
        _voicedBefore.Clear();
        _voicedAfter.Clear();
        KillChordVoicer.Voice(chord.Style, color, chordIndex, before, chord.Combo, chord.Tension, _voicedBefore);
        KillChordVoicer.Voice(chord.Style, color, chordIndex, after, chord.Combo, chord.Tension, _voicedAfter);
        int inPhrase = beat % (SectionSelector.BarsPerPhrase * BeatsPerBar);
        double window = inPhrase <= PostBoundaryBeats ? PostBoundaryDispatchBeats : OneShotScheduler.DispatchWindowBeats;
        foreach (var note in _voicedAfter)
        {
            if (RemoveSame(_voicedBefore, note))
                continue; // already scheduled by an earlier kill of this beat
            KillScheduler.Schedule(note.Instrument, note.Midi, beat, note.LengthBeats, note.GainDb,
                KillChordVoicer.OnsetShiftSeconds(note), window);
        }
    }

    static bool RemoveSame(List<KillNote> notes, KillNote note)
    {
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            if (n.Instrument == note.Instrument && n.Midi == note.Midi && n.LengthBeats == note.LengthBeats && n.GainDb == note.GainDb)
            {
                notes.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    // Bed style FMOD plays at a step (UpdateStyle's rule without the lookahead; fever is handled by the caller).
    int StyleAt(int step)
    {
        if (!_styleAvailable)
            return StyleSynthpop;
        return step >= NextPhraseStep ? _styleNext : _styleCurrent;
    }

    // Combo level FMOD plays at a step: the applied level up to the bar it was last switched for, the game's after it.
    int ComboAt(int step)
    {
        if (!_comboAvailable)
            return 0;
        return step / StepsPerBar > _comboBar ? _combo : Math.Max(0, _appliedCombo);
    }

    // The next phrase's section was sent or replaced: time beats past that boundary with its BPM, and move one-shots
    // already handed to FMOD for beats after it.
    void OnNextSectionChanged()
    {
        int boundaryBeat = NextPhraseStep / StepsPerBeat;
        _conductor.Clock.SetTempoFrom(boundaryBeat, SectionSelector.Bpm(_sections.Next.Section));
        KillScheduler.RescheduleFrom(boundaryBeat);
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

        // Beats past the next phrase boundary run at the upcoming section's BPM (ruling R11); safe every frame.
        _conductor.Clock.SetTempoFrom(NextPhraseStep / StepsPerBeat, SectionSelector.Bpm(_sections.Next.Section));
        UpdatePhrase();
        UpdateFever();
        UpdateKillChords();
    }

    // Each 4-bar phrase: the queued phrase becomes current and the one after it is chosen and sent to FMOD,
    // which jumps (or plays on) at the next phrase boundary. That leaves a whole phrase of lead time.
    // A phrase boundary inside build-up or fever must not change section, so BPM stays fixed for the whole fever.
    // A boundary exactly at the fever's end is free: the tempo can move as fever finishes.
    bool FeverHoldsBoundary(int boundaryStep) =>
        _feverStart >= 0 && boundaryStep >= _feverStart - FeverBuildSteps && boundaryStep < _feverStart + FeverSteps;

    void UpdatePhrase()
    {
        int phrase = _conductor.Bar / SectionSelector.BarsPerPhrase;
        if (phrase <= _lastPhrase)
            return;
        _lastPhrase = phrase;
        _phraseStartStep = phrase * StepsPerPhrase;
        var next = _sections.Advance(_intensity,
            allowBreath: _feverStart < 0 || AudioStep >= _feverStart + FeverSteps,
            hold: FeverHoldsBoundary(_phraseStartStep + StepsPerPhrase));
        Check(_music.setParameterByName("NextSection", next.Section));
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

    void UpdateFeature(double step)
    {
        if (!_featuresAvailable)
            return;
        // Switch on just before a phrase that has a loop, but switch off only once the phrase has really ended,
        // so the lookahead does not cut the last note of a loop.
        var loop = step >= NextPhraseStep && _features.Next != null ? _features.Next : _features.Current;
        int id = loop == null || FeverOverlaps(step, step) ? 0 : loop.Id;
        if (id != _appliedFeature && Check(_music.setParameterByName("FeatureLoop", id, true)))
            _appliedFeature = id;
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

        // Look ahead into the next phrase too, so a breath's thinner arrangement starts on its first downbeat.
        bool breath = step >= NextPhraseStep ? _sections.Next.Breath : _sections.Current.Breath;
        int intensity = fever ? MaxIntensity : breath ? 0 : _intensity;
        if (intensity != _appliedIntensity && Check(_music.setParameterByName("Intensity", intensity)))
            _appliedIntensity = intensity;

        UpdateFeature(step);
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
        if (!_styleAvailable)
            return;
        int style = step >= NextPhraseStep ? _styleNext : _styleCurrent;
        if (FeverOverlaps(step, step))
            style = StyleSynthpop;
        if (style != _appliedStyle && Check(_music.setParameterByName("BedStyle", style, true)))
            _appliedStyle = style;
    }

    void UpdateCombo(double step, bool feverZone)
    {
        if (!_comboAvailable)
            return;
        if (_hurtPending)
        {
            // Snap to 1, then aim at 0: FMOD's descending seek speed draws the recovery.
            _hurtPending = false;
            if (Check(_music.setParameterByName("Hurt", 1f, true)))
                Check(_music.setParameterByName("Hurt", 0f));
        }
        // Layers are gated per note, so only change the level on a bar line (just ahead of it).
        int bar = (int)Math.Floor(step / StepsPerBar);
        if (bar == _comboBar)
            return;
        _comboBar = bar;
        int combo = feverZone ? 0 : _combo;
        if (combo != _appliedCombo && Check(_music.setParameterByName("Combo", combo, true)))
            _appliedCombo = combo;
    }

    // Like FeatureLoop: switched just before the phrase boundary. Build-up and fever keep the original pattern
    // (they bring their own rolls and fills).
    void UpdateVariation(double step)
    {
        if (!_variationAvailable)
            return;
        int v = step >= NextPhraseStep ? _variationNext : _variationCurrent;
        if (FeverOverlaps(step, step))
            v = 0;
        if (v != _appliedVariation && Check(_music.setParameterByName("Variation", v, true)))
            _appliedVariation = v;
    }

    // FMOD smooths both parameters with its own seek speed (fast up, slow down), so values are sent as they are.
    void UpdateTension(bool feverZone)
    {
        if (!_tensionAvailable)
            return;
        float tension = feverZone ? 0f : _tension;
        float danger = feverZone || !_danger ? 0f : 1f;
        if (Mathf.Abs(tension - _appliedTension) >= 0.02f || (tension == 0f && _appliedTension != 0f))
            if (Check(_music.setParameterByName("Tension", tension)))
                _appliedTension = tension;
        if (danger != _appliedDanger && Check(_music.setParameterByName("Danger", danger)))
            _appliedDanger = danger;
    }

    void SetFeverParameter(FeverPhase phase, bool on)
    {
        int index = (int)phase;
        if (_appliedFever[index] == on)
            return;
        if (Check(_music.setParameterByName(FeverParameters[index], on ? 1f : 0f, true)))
            _appliedFever[index] = on;
    }

    // OneShotScheduler caller contract: scheduler first, then the backend, every frame (a note dispatched by the
    // scheduler gets its start time the same frame; skipping either leaves FMOD instances unreleased).
    void UpdateKillChords()
    {
        KillScheduler.Update();
        _killBackend.Update();
        if (_killBeats.Count == 0 || !_conductor.Clock.BeatAt(MusicConductor.MasterDsp(), out double now))
            return;
        _expiredKillBeats.Clear();
        foreach (int beat in _killBeats.Keys)
            if (beat < now - 1)
                _expiredKillBeats.Add(beat);
        foreach (int beat in _expiredKillBeats)
            _killBeats.Remove(beat);
    }

    // Accompaniment (music event parameter) and kill chords (bus:/Kill Chords volume) are mixed separately; only push
    // values when they change.
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
            if (!Check(_killBus.setVolume(KillChordGain(killChordMix))))
                return false;
            _appliedKillChordMix = killChordMix;
        }
        return true;
    }

    // The fader curve the old KillChordMix parameter drove (dB at 0, .25, .5, .75, 1), as a linear bus volume.
    static readonly float[] KillChordMixDb = { -80f, -18f, -9f, -3.5f, 0f };

    static float KillChordGain(float mix)
    {
        if (mix <= 0f)
            return 0f;
        float x = Mathf.Clamp01(mix) * (KillChordMixDb.Length - 1);
        int i = Mathf.Min((int)x, KillChordMixDb.Length - 2);
        float db = Mathf.Lerp(KillChordMixDb[i], KillChordMixDb[i + 1], x - i);
        return Mathf.Pow(10f, db / 20f);
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
