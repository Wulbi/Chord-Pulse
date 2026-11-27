using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Director for feature-loop ("guest") visuals (Docs/FeatureLoopVisuals.md): it follows the beat, fires each
/// note of the guest part on time and gives the arrival cue one beat ahead. Drawing is left to the emitter of the
/// style the <see cref="FeatureLoopLibrary"/> picks for the note's instrument (trumpet = comet, whistle = bird, ...).
/// Note timing comes from <see cref="FeatureLoopNotes"/>, generated from the same script as the FMOD notes,
/// so the visuals land on the beats FMOD plays.
/// Everything draws behind the arena and semi-transparent so it never reads as an enemy.
/// </summary>
[DisallowMultipleComponent]
public sealed class FeatureLoopVisuals : MonoBehaviour
{
    const int SortingOrder = -3;

    SoundManager _sound;
    FeatureLoopLibrary _library;
    FeatureVisualContext _context;
    SparkleEmitter _sparkles;
    // One emitter per style asset, created on first use.
    readonly Dictionary<FeatureVisualStyle, IFeatureEmitter> _emitters = new Dictionary<FeatureVisualStyle, IFeatureEmitter>();
    int _phraseStart = int.MinValue;
    double _lastPhraseBeat;
    bool _announced;
    int _lastFeverBeat = -1;

    public void Init(Camera playCamera, float arenaRadius, Material material)
    {
        _sound = SoundManager.Instance;
        _library = FeatureLoopLibrary.Load();
        _context = new FeatureVisualContext
        {
            Camera = playCamera,
            ArenaRadius = arenaRadius,
            Material = material,
            Dot = CreateDot(),
            Square = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4),
            Root = transform,
            SortingOrder = SortingOrder,
        };
        _sparkles = new SparkleEmitter(_context);
        Prewarm();
    }

    // Build every style's emitter and pool now, so the first notes of a guest loop (or the first fever) do not
    // create GameObjects and renderers mid-song, which showed up as frame spikes.
    void Prewarm()
    {
        var styles = new HashSet<FeatureVisualStyle> { _library.fallbackStyle, _library.feverStyle };
        foreach (var voice in _library.voiceDefaults)
            styles.Add(voice.style);
        foreach (var data in _library.loops)
            if (data != null)
                foreach (var voice in data.voices)
                    styles.Add(voice.style);
        foreach (var style in styles)
            if (style != null)
                EmitterFor(style).Prewarm(style.prewarm);
        _sparkles.Prewarm();
    }

    public void ResetVisuals()
    {
        _phraseStart = int.MinValue;
        _announced = false;
        _lastFeverBeat = -1;
        _sparkles?.Clear();
        foreach (var emitter in _emitters.Values)
            emitter.Clear();
    }

    void Update()
    {
        using var profile = VisualProfiler.FeatureUpdate.Auto();
        if (_sound == null || _context == null)
            return;
        SpawnFromMusic();
        float dt = Time.deltaTime;
        foreach (var emitter in _emitters.Values)
            emitter.Tick(dt);
        _sparkles.Tick(dt);
    }

    void SpawnFromMusic()
    {
        double beat = _sound.BeatPosition;
        if (beat < 0)
            return;

        int phraseStart = _sound.NextPhraseStep - SoundManager.StepsPerPhrase;
        double phraseBeat = beat - phraseStart / (double)SoundManager.StepsPerBeat;
        if (phraseStart != _phraseStart)
        {
            // New phrase: notes on its downbeat must still fire.
            _phraseStart = phraseStart;
            _lastPhraseBeat = -0.001;
            _announced = false;
        }

        // After a combo break the guest loop fades with the layers (SoundManager.GuestPresence) and is then dropped
        // (CurrentFeature null): its notes come out smaller on the same curve. Fever's guest does not fade.
        float presence = _sound.FeverActive ? 1f : _sound.GuestPresence;
        var loop = _sound.CurrentFeature;
        var notes = loop != null && loop.Id < FeatureLoopNotes.ById.Length ? FeatureLoopNotes.ById[loop.Id] : null;
        if (notes != null && presence > 0.01f)
        {
            var data = _library.Find(loop.Id);
            foreach (var note in notes)
            {
                if (note.Beat <= _lastPhraseBeat || note.Beat > phraseBeat)
                    continue;
                var voice = FeatureLoopLibrary.VoiceFor(data, note.Instrument);
                EmitterFor(_library.StyleFor(data, note.Instrument)).Emit(note, voice.SizeScale * presence, (int)voice.side);
            }
        }

        EmitFever(phraseBeat);

        // One beat before a guest loop arrives, the screen edges twinkle where its visuals will come from. Not while a
        // combo-break fade runs: the loop chosen for the next phrase is dropped when the fade ends.
        const float beatsPerPhrase = SoundManager.StepsPerPhrase / (float)SoundManager.StepsPerBeat;
        if (!_announced && phraseBeat >= beatsPerPhrase - 1f && _sound.UpcomingFeature != null && presence >= 1f)
        {
            _announced = true;
            _sparkles.Twinkle(_library.AccentFor(_sound.UpcomingFeature));
        }
        _lastPhraseBeat = phraseBeat;
    }

    // Fever fills a whole phrase and has no guest: rainbow comets cross from both sides on every kick
    // (Docs/MusicVisualsPlan.md 2-7). In the outro bar they thin out to one smaller comet per kick.
    void EmitFever(double phraseBeat)
    {
        int beat = _sound.CurrentBeat;
        if (_library.feverStyle == null || !_sound.FeverActive || beat == _lastFeverBeat || !_sound.KickOn(beat))
            return;
        _lastFeverBeat = beat;
        var emitter = EmitterFor(_library.feverStyle);
        bool outro = phraseBeat >= 3 * SoundManager.BeatsPerBar;
        // Heights alternate high/low so the crossings fill the screen; pitch picks the height.
        int high = (beat / 2) % 2 == 0 ? 92 : 74;
        var note = new FeatureLoopNotes.Note((float)phraseBeat, high, outro ? 1 : 2, FeatureSelector.Instrument.Trumpet);
        emitter.Emit(note, outro ? 0.75f : 1f, -1);
        if (!outro)
            emitter.Emit(new FeatureLoopNotes.Note((float)phraseBeat, 166 - high, 2, FeatureSelector.Instrument.Trumpet), 1f, 1);
    }

    IFeatureEmitter EmitterFor(FeatureVisualStyle style)
    {
        if (!_emitters.TryGetValue(style, out var emitter))
        {
            emitter = style.CreateEmitter(_context);
            _emitters.Add(style, emitter);
        }
        return emitter;
    }

    static Sprite CreateDot()
    {
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Feature Dot", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        float r = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
                float a = Mathf.Clamp01(1f - d);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
            }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    void OnDestroy()
    {
        _library?.DestroyIfDefault();
        var dot = _context?.Dot;
        if (dot != null)
        {
            Destroy(dot.texture);
            Destroy(dot);
        }
        // The square uses Unity's shared white texture: destroy only the sprite.
        if (_context?.Square != null)
            Destroy(_context.Square);
    }
}
