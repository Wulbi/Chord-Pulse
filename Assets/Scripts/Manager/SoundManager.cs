using UnityEngine;

[DisallowMultipleComponent]
public sealed class SoundManager : SingletonBehaviour<SoundManager>
{
    const int SampleRate = 44100;
    const float NoteDuration = 0.55f;

    static readonly float[] Frequencies = { 261.626f, 329.628f, 391.995f };
    static readonly string[] NoteNames = { "C4", "E4", "G4" };

    readonly AudioClip[] _notes = new AudioClip[Frequencies.Length];
    AudioSource _source;

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this)
            return;

        _source = GetComponent<AudioSource>();
        if (_source == null)
            _source = gameObject.AddComponent<AudioSource>();

        _source.playOnAwake = false;
        _source.spatialBlend = 0f;
        for (int i = 0; i < _notes.Length; i++)
            _notes[i] = CreateNote(NoteNames[i], Frequencies[i]);
    }

    public void PlayChordTone(int toneIndex)
    {
        _source.PlayOneShot(_notes[toneIndex % _notes.Length]);
    }

    static AudioClip CreateNote(string noteName, float frequency)
    {
        var samples = new float[Mathf.RoundToInt(SampleRate * NoteDuration)];
        for (int i = 0; i < samples.Length; i++)
        {
            float time = i / (float)SampleRate;
            float phase = 2f * Mathf.PI * frequency * time;
            float attack = Mathf.Clamp01(time / 0.008f);
            float release = Mathf.Clamp01((NoteDuration - time) / 0.08f);
            float envelope = attack * release * Mathf.Exp(-6f * time);
            samples[i] = 0.23f * envelope * (Mathf.Sin(phase) + 0.25f * Mathf.Sin(2f * phase));
        }

        var clip = AudioClip.Create(noteName, samples.Length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    void OnDestroy()
    {
        foreach (var note in _notes)
            if (note != null)
                Destroy(note);
    }
}
