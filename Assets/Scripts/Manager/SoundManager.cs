using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SoundManager : SingletonBehaviour<SoundManager>
{
    public const int BeatsPerBar = 4;

    public const int StepsPerBeat = 2;
    public const int StepsPerBar = BeatsPerBar * StepsPerBeat;
    const int StepsPerTempo = 8 * StepsPerBar;
    const int MaxChordVoicing = 3;
    const double ChordLookahead = 0.09;
    const int FeverSteps = 4 * StepsPerBar;
    const double ScheduleLead = 0.06;
    const double RhythmLookahead = 0.15;
    const int SampleRate = 44100;
    const float NoteDuration = 0.55f;

    static readonly int[] Tempos = { 108, 116, 124, 132 };
    static readonly int[] FeverMelody = { 0, -1, 1, 2, -1, 1, 2, -1 };
    static readonly (string Name, int Root, int Third, int Fifth)[] Progression =
    {
        ("C", 60, 64, 67),
        ("Am", 57, 60, 64),
        ("F", 53, 57, 60),
        ("G", 55, 59, 62)
    };

    // Instrument order follows the pulse colors: red, blue, yellow.
    static readonly string[] InstrumentNames = { "Lead Synth", "Electric Guitar", "Pluck Synth" };
    readonly AudioClip[,] _chords = new AudioClip[InstrumentNames.Length, Progression.Length];
    readonly AudioClip[,,] _expandedVoicings =
        new AudioClip[InstrumentNames.Length, Progression.Length, MaxChordVoicing - 1];
    readonly SortedDictionary<int, int[]> _pendingKillChords = new SortedDictionary<int, int[]>();
    readonly AudioClip[,] _lead = new AudioClip[Progression.Length, 3];
    readonly AudioClip[] _bassFifths = new AudioClip[Progression.Length];
    readonly AudioClip[] _bassNotes = new AudioClip[Progression.Length];
    readonly List<Voice> _voices = new List<Voice>();
    AudioClip _kick;
    AudioClip _snare;
    AudioClip _hat;
    double _startTime;
    int _nextRhythmStep;
    bool _musicPlaying;
    int _feverStart = -1;

    public double RoundSeconds => AudioSettings.dspTime - _startTime;
    public double BeatPosition => _musicPlaying ? StepPositionAt(RoundSeconds) / StepsPerBeat : -1;
    public int CurrentBeat => (int)Math.Floor(BeatPosition);
    public int CurrentStep => (int)Math.Floor(BeatPosition * StepsPerBeat);
    public int TempoStage => StageAt(CurrentStep);
    public int CurrentBpm => Tempos[TempoStage];
    public bool FeverActive => _musicPlaying && IsFeverStep(CurrentStep);
    public bool FeverPending => _musicPlaying && _feverStart >= 0 && CurrentStep < _feverStart;

    public static bool IsKickBeat(int beat) => beat % 2 == 0;

    static int StageAt(int step) => Math.Max(0, Math.Min(step / StepsPerTempo, Tempos.Length - 1));
    static double StepLength(int stage) => 60.0 / (Tempos[stage] * StepsPerBeat);

    static double StepPositionAt(double seconds)
    {
        int stage = 0;
        while (stage < Tempos.Length - 1 && seconds >= StepsPerTempo * StepLength(stage))
        {
            seconds -= StepsPerTempo * StepLength(stage);
            stage++;
        }

        return stage * StepsPerTempo + seconds / StepLength(stage);
    }

    double TimeAtStep(int step)
    {
        int stage = StageAt(step);
        double elapsed = 0;
        for (int i = 0; i < stage; i++)
            elapsed += StepsPerTempo * StepLength(i);
        return _startTime + elapsed + (step - stage * StepsPerTempo) * StepLength(stage);
    }

    bool IsFeverStep(int step) =>
        _feverStart >= 0 && step >= _feverStart && step < _feverStart + FeverSteps;

    public void QueueFever()
    {
        int nextBar = (CurrentStep / StepsPerBar + 1) * StepsPerBar;
        while (nextBar < _nextRhythmStep)
            nextBar += StepsPerBar;
        _feverStart = nextBar;
    }

    sealed class Voice
    {
        public AudioSource Source;
        public double EndsAt;
    }

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this)
            return;

        for (int harmony = 0; harmony < Progression.Length; harmony++)
        {
            var chord = Progression[harmony];
            _bassNotes[harmony] = CreateBass(chord.Name, Frequency(chord.Root - 24));
            _bassFifths[harmony] = CreateBass(chord.Name + " Fifth", Frequency(chord.Fifth - 24));
            _lead[harmony, 0] = CreateLead(chord.Name, chord.Root + 12);
            _lead[harmony, 1] = CreateLead(chord.Name, chord.Third + 12);
            _lead[harmony, 2] = CreateLead(chord.Name, chord.Fifth + 12);

            for (int instrument = 0; instrument < InstrumentNames.Length; instrument++)
            {
                var samples = new float[Mathf.RoundToInt(SampleRate * NoteDuration)];
                MixNote(samples, Frequency(chord.Root), instrument);
                MixNote(samples, Frequency(chord.Third), instrument);
                MixNote(samples, Frequency(chord.Fifth), instrument);
                string clipName = InstrumentNames[instrument] + " " + chord.Name;
                _chords[instrument, harmony] = CreateClip(clipName, samples);
                MixNote(samples, Frequency(chord.Root + 12), instrument);
                _expandedVoicings[instrument, harmony, 0] = CreateClip(clipName + " High Root", samples);
                MixNote(samples, Frequency(chord.Fifth + 12), instrument);
                _expandedVoicings[instrument, harmony, 1] = CreateClip(clipName + " High Fifth", samples);
            }
        }

        _kick = CreateKick();
        _snare = CreateSnare();
        _hat = CreateHat();
    }

    public void StartMusic()
    {
        StopMusic();
        _startTime = AudioSettings.dspTime + RhythmLookahead;
        _nextRhythmStep = 0;
        _feverStart = -1;
        _musicPlaying = true;
        ScheduleRhythm(AudioSettings.dspTime);
    }

    public void StopMusic()
    {
        _musicPlaying = false;
        _pendingKillChords.Clear();
        foreach (var voice in _voices)
        {
            voice.Source.Stop();
            voice.EndsAt = 0;
        }
    }

    public void QueueKillChord(int colorIndex)
    {
        if (!_musicPlaying)
            return;

        double now = AudioSettings.dspTime;
        // Collect kills until shortly before the next beat so the whole chord can be scheduled together.
        int target = (CurrentStep / StepsPerBeat + 1) * StepsPerBeat;
        if (TimeAtStep(target) <= now + ChordLookahead)
            target += StepsPerBeat;

        if (!_pendingKillChords.TryGetValue(target, out var kills))
        {
            kills = new int[InstrumentNames.Length];
            _pendingKillChords.Add(target, kills);
        }

        kills[colorIndex] = Math.Min(kills[colorIndex] + 1, MaxChordVoicing);
    }

    void ScheduleKillChords(double now)
    {
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

            double playAt = TimeAtStep(step);
            if (playAt > now + ChordLookahead)
                return;

            _pendingKillChords.Remove(step);
            if (playAt <= now)
            {
                int nextBeat = (GetNextSchedulableStep(now) + StepsPerBeat - 1) / StepsPerBeat;
                int nextStep = nextBeat * StepsPerBeat;
                if (!_pendingKillChords.TryGetValue(nextStep, out var nextKills))
                    _pendingKillChords.Add(nextStep, kills);
                else
                    for (int color = 0; color < kills.Length; color++)
                        nextKills[color] = Math.Min(nextKills[color] + kills[color], MaxChordVoicing);
                continue;
            }

            int harmony = HarmonyAtStep(step);
            for (int color = 0; color < kills.Length; color++)
            {
                int count = kills[color];
                if (count == 0)
                    continue;

                var clip = count == 1 ? _chords[color, harmony] : _expandedVoicings[color, harmony, count - 2];
                ScheduleClip(clip, playAt, now, 0.36f);
            }
        }
    }

    static int HarmonyAtStep(int step) => step / StepsPerBar % Progression.Length;

    void Update()
    {
        if (!_musicPlaying)
            return;

        double now = AudioSettings.dspTime;
        ScheduleRhythm(now);
        ScheduleKillChords(now);
    }

    void ScheduleRhythm(double now)
    {
        _nextRhythmStep = Math.Max(_nextRhythmStep, GetNextSchedulableStep(now));
        double playAt = TimeAtStep(_nextRhythmStep);
        while (playAt <= now + RhythmLookahead)
        {
            int step = _nextRhythmStep;
            int harmony = HarmonyAtStep(step);
            bool fever = IsFeverStep(step);
            ScheduleClip(_hat, playAt, now);
            if (step % StepsPerBeat == 0)
            {
                bool kick = IsKickBeat(step / StepsPerBeat);
                ScheduleClip(kick ? _kick : _snare, playAt, now);
                if (kick || fever)
                    ScheduleClip(kick ? _bassNotes[harmony] : _bassFifths[harmony], playAt, now);
            }

            if (fever)
            {
                int tone = FeverMelody[step % StepsPerBar];
                if (tone >= 0)
                    ScheduleClip(_lead[harmony, tone], playAt, now);
                int feverStep = step - _feverStart;
                if (feverStep == 1 || feverStep == 3)
                    ScheduleClip(_snare, playAt, now, 0.75f);
            }

            _nextRhythmStep++;
            playAt = TimeAtStep(_nextRhythmStep);
        }
    }

    int GetNextSchedulableStep(double now)
    {
        return Math.Max(0, (int)Math.Ceiling(StepPositionAt(now + ScheduleLead - _startTime)));
    }

    void ScheduleClip(AudioClip clip, double playAt, double now, float volume = 1f)
    {
        var voice = _voices.Find(candidate => candidate.EndsAt <= now);
        if (voice == null)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            voice = new Voice { Source = source };
            _voices.Add(voice);
        }

        voice.Source.clip = clip;
        voice.Source.volume = volume;
        voice.Source.PlayScheduled(playAt);
        voice.EndsAt = playAt + (double)clip.samples / clip.frequency;
    }

    static AudioClip CreateKick()
    {
        var samples = new float[Mathf.RoundToInt(SampleRate * 0.22f)];
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float phase = 2f * Mathf.PI * (45f * t + 2.2f * (1f - Mathf.Exp(-45f * t)));
            samples[i] = 0.28f * Mathf.Sin(phase) * Mathf.Exp(-23f * t);
        }
        return CreateClip("Kick", samples);
    }

    static AudioClip CreateSnare()
    {
        var samples = new float[Mathf.RoundToInt(SampleRate * 0.17f)];
        var noise = new System.Random(240);
        float previous = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float time = i / (float)SampleRate;
            float sample = (float)(2.0 * noise.NextDouble() - 1.0);
            float snap = (sample - 0.65f * previous) * Mathf.Exp(-27f * time);
            float body = Mathf.Sin(2f * Mathf.PI * 175f * time) * Mathf.Exp(-47f * time);
            float attack = Mathf.Clamp01(time / 0.002f);
            samples[i] = attack * (0.13f * snap + 0.07f * body);
            previous = sample;
        }
        return CreateClip("Snare", samples);
    }

    static AudioClip CreateHat()
    {
        var samples = new float[Mathf.RoundToInt(SampleRate * 0.065f)];
        var noise = new System.Random(120);
        float previous = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float sample = (float)(2.0 * noise.NextDouble() - 1.0);
            samples[i] = 0.065f * (sample - previous) * Mathf.Exp(-75f * t);
            previous = sample;
        }
        return CreateClip("Eighth-note Hat", samples);
    }

    static float Frequency(int midiNote) => 440f * Mathf.Pow(2f, (midiNote - 69) / 12f);

    static AudioClip CreateBass(string chordName, float frequency)
    {
        const float duration = 0.76f;
        var samples = new float[Mathf.RoundToInt(SampleRate * duration)];
        for (int i = 0; i < samples.Length; i++)
        {
            float time = i / (float)SampleRate;
            float phase = 2f * Mathf.PI * frequency * time;
            float attack = Mathf.Clamp01(time / 0.018f);
            float release = Mathf.Clamp01((duration - time) / 0.14f);
            float tone = 0.7f * Mathf.Sin(phase) + 0.25f * Mathf.Sin(2f * phase)
                + 0.08f * Mathf.Sin(3f * phase);
            samples[i] = 0.20f * attack * release * Mathf.Exp(-2.2f * time) * tone;
        }
        return CreateClip("Bass " + chordName, samples);
    }

    static AudioClip CreateLead(string chordName, int note)
    {
        var samples = new float[Mathf.RoundToInt(SampleRate * 0.24f)];
        MixNote(samples, Frequency(note), 0);
        return CreateClip("Fever Lead " + chordName + " " + note, samples);
    }

    static void MixNote(float[] samples, float frequency, int instrument)
    {
        float attackTime = instrument == 0 ? 0.018f : 0.004f;
        for (int i = 0; i < samples.Length; i++)
        {
            float time = i / (float)SampleRate;
            float phase = 2f * Mathf.PI * frequency * time;
            float attack = Mathf.Clamp01(time / attackTime);
            float release = Mathf.Clamp01((samples.Length / (float)SampleRate - time) / 0.06f);
            float tone = instrument switch
            {
                0 => LeadSynth(phase, time),
                1 => ElectricGuitar(phase, time),
                _ => PluckSynth(phase, time)
            };
            samples[i] += 0.52f * attack * release * tone;
        }
    }

    static float LeadSynth(float phase, float time)
    {
        float body = 0.6f * Mathf.Sin(phase) + 0.4f * Mathf.Sin(phase * 1.004f);
        float buzz = 0.25f * Mathf.Sin(2f * phase) + 0.16f * Mathf.Sin(3f * phase)
            + 0.1f * Mathf.Sin(5f * phase);
        return 0.16f * (body + buzz * Mathf.Exp(-5f * time)) * Mathf.Exp(-2.8f * time);
    }

    static float ElectricGuitar(float phase, float time)
    {
        float stringTone = Mathf.Sin(phase) + 0.52f * Mathf.Sin(2f * phase)
            + 0.32f * Mathf.Sin(3f * phase);
        float pick = (0.4f * Mathf.Sin(5f * phase) + 0.22f * Mathf.Sin(7f * phase))
            * Mathf.Exp(-19f * time);
        float drive = (float)Math.Tanh(1.35f * (stringTone + pick));
        return 0.18f * drive * Mathf.Exp(-4.5f * time);
    }

    static float PluckSynth(float phase, float time)
    {
        float modulation = 1.7f * Mathf.Exp(-23f * time);
        float tone = Mathf.Sin(phase + modulation * Mathf.Sin(2f * phase));
        return 0.22f * tone * Mathf.Exp(-11f * time);
    }

    static AudioClip CreateClip(string clipName, float[] samples)
    {
        var clip = AudioClip.Create(clipName, samples.Length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    void OnDestroy()
    {
        StopMusic();
        Destroy(_kick);
        Destroy(_snare);
        Destroy(_hat);
        foreach (var chord in _chords)
            Destroy(chord);
        foreach (var voicing in _expandedVoicings)
            Destroy(voicing);
        foreach (var bass in _bassNotes)
            Destroy(bass);
        foreach (var bass in _bassFifths)
            Destroy(bass);
        foreach (var lead in _lead)
            Destroy(lead);
    }
}
