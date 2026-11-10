using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SoundManager : SingletonBehaviour<SoundManager>
{
    public const int BeatsPerMinute = 120;
    public const int BeatsPerBar = 4;
    public const double SecondsPerBeat = 60.0 / BeatsPerMinute;

    const int StepsPerBeat = 2;
    const double StepDuration = SecondsPerBeat / StepsPerBeat;
    const double ScheduleLead = 0.06;
    const double RhythmLookahead = 0.15;
    const int SampleRate = 44100;
    const float NoteDuration = 0.55f;

    static readonly float[] Frequencies = { 261.626f, 329.628f, 391.995f };
    static readonly string[] NoteNames = { "C4", "E4", "G4" };

    // Instrument order follows the pulse colors: red, blue, yellow.
    static readonly string[] InstrumentNames = { "Lead Synth", "Electric Guitar", "Pluck Synth" };
    readonly AudioClip[,] _notes = new AudioClip[InstrumentNames.Length, Frequencies.Length];
    readonly List<Voice> _voices = new List<Voice>();
    AudioClip _kick;
    AudioClip _snare;
    AudioClip _hat;
    double _startTime;
    int _nextRhythmStep;
    bool _musicPlaying;

    public double BeatPosition => _musicPlaying
        ? (AudioSettings.dspTime - _startTime) / SecondsPerBeat
        : -1;

    public int CurrentBeat => (int)Math.Floor(BeatPosition);

    public static bool IsKickBeat(int beat) => beat % 2 == 0;

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

        for (int instrument = 0; instrument < InstrumentNames.Length; instrument++)
        {
            for (int note = 0; note < Frequencies.Length; note++)
            {
                _notes[instrument, note] = CreateNote(
                    InstrumentNames[instrument] + " " + NoteNames[note], Frequencies[note], instrument);
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
        _musicPlaying = true;
        ScheduleRhythm(AudioSettings.dspTime);
    }

    public void StopMusic()
    {
        _musicPlaying = false;
        foreach (var voice in _voices)
        {
            voice.Source.Stop();
            voice.EndsAt = 0;
        }
    }

    // Each pulse owns its phrase. Separate pulses may share an eighth-note slot.
    public int ScheduleChordTone(int colorIndex, int toneIndex, int earliestStep)
    {
        if (!_musicPlaying)
            return earliestStep;

        double now = AudioSettings.dspTime;
        int step = Math.Max(earliestStep, GetNextSchedulableStep(now));
        ScheduleClip(_notes[colorIndex, toneIndex % Frequencies.Length], _startTime + step * StepDuration, now);
        return step + 1;
    }

    void Update()
    {
        if (_musicPlaying)
            ScheduleRhythm(AudioSettings.dspTime);
    }

    void ScheduleRhythm(double now)
    {
        _nextRhythmStep = Math.Max(_nextRhythmStep, GetNextSchedulableStep(now));
        double playAt = _startTime + _nextRhythmStep * StepDuration;
        while (playAt <= now + RhythmLookahead)
        {
            ScheduleClip(_hat, playAt, now);
            if (_nextRhythmStep % StepsPerBeat == 0)
            {
                int beat = _nextRhythmStep / StepsPerBeat;
                ScheduleClip(IsKickBeat(beat) ? _kick : _snare, playAt, now);
            }

            _nextRhythmStep++;
            playAt = _startTime + _nextRhythmStep * StepDuration;
        }
    }

    int GetNextSchedulableStep(double now)
    {
        return Math.Max(0, (int)Math.Ceiling((now + ScheduleLead - _startTime) / StepDuration));
    }

    void ScheduleClip(AudioClip clip, double playAt, double now)
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

    static AudioClip CreateNote(string noteName, float frequency, int instrument)
    {
        var samples = new float[Mathf.RoundToInt(SampleRate * NoteDuration)];
        float attackTime = instrument == 0 ? 0.018f : 0.004f;
        for (int i = 0; i < samples.Length; i++)
        {
            float time = i / (float)SampleRate;
            float phase = 2f * Mathf.PI * frequency * time;
            float attack = Mathf.Clamp01(time / attackTime);
            float release = Mathf.Clamp01((NoteDuration - time) / 0.06f);
            float tone = instrument switch
            {
                0 => LeadSynth(phase, time),
                1 => ElectricGuitar(phase, time),
                _ => PluckSynth(phase, time)
            };
            samples[i] = attack * release * tone;
        }

        return CreateClip(noteName, samples);
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
        foreach (var note in _notes)
            if (note != null)
                Destroy(note);
    }
}
