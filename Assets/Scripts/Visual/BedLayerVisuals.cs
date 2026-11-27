using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The bed's layers on the arena ring (Docs/MusicVisualsPlan.md 2-1 to 2-3), timed by <see cref="BedNotes"/>:
/// <list type="bullet">
/// <item>Energy: the ring gets thicker per Intensity step; from Intensity 1 four ticks flash on the off-beat pluck</item>
/// <item>Variation 1-2 (8th drive bass): the ring breathes on every bass 8th</item>
/// <item>Variation 2 (bar-4 snare fill): the ring swells with the 16th crescendo, announcing the next phrase</item>
/// <item>Variation 3-4 (walking bass): a dot walks round the ring, one step per bass note</item>
/// <item>Variation 4 (tresillo stabs): three points on the ring flash together on each stab</item>
/// <item>Combo 1 (16th hats): a dotted ring outside the arena turns one step every 16th</item>
/// <item>Combo 2 (glass pad): a soft halo that breathes once a bar</item>
/// <item>Combo 3 (bells): a sparkle on the halo for each bell note, its angle set by pitch</item>
/// </list>
/// A layer's decoration is scaled by how much of that layer is heard (<see cref="SoundManager.LayerPresence"/> of the
/// combo ladder rung it needs), so after a combo break it fades on the sound's own curve.
/// Ring-wide changes go through <see cref="ArenaStage"/> requests; the decorations are drawn after
/// <see cref="MusicReactiveVisuals"/> so they follow the ring's pulse.
/// </summary>
[DefaultExecutionOrder(100), DisallowMultipleComponent]
public sealed class BedLayerVisuals : MonoBehaviour
{
    const int Order = -1;
    const int ComboDots = 24;
    const int WalkSteps = 16;
    const int Ticks = 4;
    const int Stabs = 3;
    const int BellPool = 8;
    const float ComboGap = 0.18f;
    const float HaloGap = 0.34f;

    static readonly Color TickColor = new Color(0.75f, 0.85f, 1f);
    static readonly Color WalkColor = new Color(0.6f, 1f, 0.85f);
    static readonly Color StabColor = new Color(1f, 0.9f, 0.7f);
    static readonly Color GlassColor = new Color(0.7f, 0.95f, 1f);

    SoundManager _sound;
    ArenaStage _stage;
    Material _material;
    Sprite _dot;

    // Beat tracking (same scheme as FeatureLoopVisuals: fire notes whose beat was crossed this frame).
    int _phraseStart = int.MinValue;
    double _lastPhraseBeat;
    double _phraseBeat;
    int _lastOffbeat = -1;

    // Impulses (1 on the note, decaying).
    float _drive;
    float _fill;
    float _ghost;
    float _stab;
    float _tick;
    float _comboUp;
    int _walk;
    float _walkVisible;
    int _lastCombo;
    readonly float[] _combo = new float[4];   // smoothed presence of combo levels 1..3

    readonly List<SpriteRenderer> _ticks = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> _stabs = new List<SpriteRenderer>();
    SpriteRenderer _walker;
    readonly List<SpriteRenderer> _comboDots = new List<SpriteRenderer>();
    LineRenderer _halo;
    readonly List<SpriteRenderer> _bells = new List<SpriteRenderer>();
    readonly float[] _bellAge = new float[BellPool];
    readonly float[] _bellAngle = new float[BellPool];
    int _nextBell;

    public void Init(ArenaStage stage, Material material)
    {
        _sound = SoundManager.Instance;
        _stage = stage;
        _material = material;
        _dot = DotSprite.Create("Bed Dot");
        for (int i = 0; i < Ticks; i++)
            _ticks.Add(MakeSprite("Bed Tick"));
        for (int i = 0; i < Stabs; i++)
            _stabs.Add(MakeSprite("Bed Stab"));
        _walker = MakeSprite("Bed Walker");
        for (int i = 0; i < ComboDots; i++)
            _comboDots.Add(MakeSprite("Combo Dot"));
        for (int i = 0; i < BellPool; i++)
        {
            _bells.Add(MakeSprite("Combo Bell"));
            _bellAge[i] = 99f;
        }
        _halo = new GameObject("Combo Halo").AddComponent<LineRenderer>();
        _halo.transform.SetParent(transform, false);
        _halo.useWorldSpace = false;
        _halo.loop = true;
        _halo.positionCount = 64;
        _halo.sharedMaterial = _material;
        _halo.sortingOrder = Order - 1;
        for (int i = 0; i < 64; i++)
        {
            float a = i * (2f * Mathf.PI / 64);
            _halo.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        }
        _halo.gameObject.SetActive(false);
    }

    public void ResetVisuals()
    {
        _phraseStart = int.MinValue;
        _lastOffbeat = -1;
        _drive = _fill = _ghost = _stab = _tick = _comboUp = 0f;
        _walk = 0;
        _walkVisible = 0f;
        _lastCombo = 0;
        System.Array.Clear(_combo, 0, _combo.Length);
        for (int i = 0; i < BellPool; i++)
            _bellAge[i] = 99f;
    }

    void Update()
    {
        using var profile = VisualProfiler.LayerUpdate.Auto();
        if (_sound == null || _stage == null)
            return;
        float dt = Time.deltaTime;
        _drive = Decay(_drive, 14f, dt);
        _fill = Decay(_fill, 6f, dt);
        _ghost = Decay(_ghost, 20f, dt);
        _stab = Decay(_stab, 9f, dt);
        _tick = Decay(_tick, 16f, dt);
        _comboUp = Decay(_comboUp, 4f, dt);
        for (int i = 0; i < BellPool; i++)
            _bellAge[i] += dt;

        FireNotes();

        // Combo level: smooth fade in over about a beat, flash the ring when it climbs. Going out it never stays above
        // the layer's presence (combo level n = ladder rung 3 + n): a combo break fades it on the sound's curve.
        int combo = _sound.CurrentCombo;
        if (combo > _lastCombo)
            _comboUp = 1f;
        _lastCombo = combo;
        float rate = dt / Mathf.Max(0.05f, (float)_sound.BeatSeconds);
        for (int level = 1; level <= 3; level++)
            _combo[level] = Mathf.Min(Mathf.MoveTowards(_combo[level], combo >= level ? 1f : 0f, rate), Presence(3 + level));

        int variation = _sound.HeardVariation;
        _walkVisible = Mathf.MoveTowards(_walkVisible, BedNotes.UsesBassWalk(variation) ? 1f : 0f, rate);

        // Ring-wide requests.
        _stage.RingWidthAdd += 0.004f * _sound.ArrangementIntensity + 0.03f * _fill + 0.006f * _ghost + 0.02f * _comboUp;
        _stage.RingRadiusOffset += 0.004f * _drive + 0.012f * _fill;
    }

    void FireNotes()
    {
        double beat = _sound.BeatPosition;
        if (beat < 0)
            return;
        int phraseStart = _sound.NextPhraseStep - SoundManager.StepsPerPhrase;
        _phraseBeat = beat - phraseStart / (double)SoundManager.StepsPerBeat;
        if (phraseStart != _phraseStart)
        {
            _phraseStart = phraseStart;
            _lastPhraseBeat = -0.001;
        }

        int v = _sound.HeardVariation;
        if (BedNotes.UsesBassDrive(v) && Crossed(BedNotes.BassDrive) > 0)
            _drive = 1f;
        if (BedNotes.UsesGhosts(v) && Crossed(BedNotes.Ghosts) > 0)
            _ghost = 1f;
        if (BedNotes.UsesFill(v))
        {
            // Crescendo: later fill hits swell more.
            foreach (var note in BedNotes.Fill)
                if (Crossing(note))
                    _fill = Mathf.Max(_fill, 0.4f + 0.6f * Mathf.InverseLerp(14f, 15.5f, note.Beat));
        }
        if (BedNotes.UsesBassWalk(v))
            _walk += Crossed(BedNotes.BassWalk);
        if (BedNotes.UsesPluckStabs(v) && Crossed(BedNotes.PluckStabs) > 0)
            _stab = 1f;
        if (_sound.CurrentCombo >= 3)
            foreach (var note in BedNotes.ComboBells)
                if (Crossing(note))
                    RingBell(note.Midi);

        // The original off-beat pluck from Intensity 1 (no data needed: every "and").
        int offbeat = (int)System.Math.Floor(beat - 0.5);
        if (offbeat != _lastOffbeat)
        {
            _lastOffbeat = offbeat;
            if (beat >= 0.5 && _sound.ArrangementIntensity >= 1 && BedNotes.UsesOriginalPluck(v) && !_sound.FeverActive)
                _tick = 1f;
        }
        _lastPhraseBeat = _phraseBeat;
    }

    bool Crossing(BedNotes.Note note) => note.Beat > _lastPhraseBeat && note.Beat <= _phraseBeat;

    // How much of a layer that needs ladder rung requiredRung is heard (1 = whole, 0 = gone). Fever plays its own full
    // arrangement whatever the ladder says, so nothing is scaled there.
    float Presence(int requiredRung) => _sound.FeverActive ? 1f : _sound.LayerPresence(requiredRung);

    // How much of the guest loop is heard (it fades with the layers after a combo break; not in fever).
    float GuestPresence => _sound.FeverActive ? 1f : _sound.GuestPresence;

    /// <summary>Distinct beats crossed this frame (a stab's three voices count once).</summary>
    int Crossed(BedNotes.Note[] notes)
    {
        int count = 0;
        float last = -1f;
        foreach (var note in notes)
            if (Crossing(note) && note.Beat != last)
            {
                count++;
                last = note.Beat;
            }
        return count;
    }

    void RingBell(int midi)
    {
        int i = _nextBell;
        _nextBell = (_nextBell + 1) % BellPool;
        _bellAge[i] = 0f;
        // Bells span about D5..C6: low on the left going over the top to high on the right.
        float pitch = Mathf.InverseLerp(74f, 84f, midi);
        _bellAngle[i] = Mathf.Lerp(160f, 20f, pitch) * Mathf.Deg2Rad;
    }

    void LateUpdate()
    {
        using var profile = VisualProfiler.LayerLate.Auto();
        if (_sound == null || _stage == null)
            return;
        float radius = _stage.RingRadius;
        // Guests take the foreground: decorations dim while one plays (plan 3-4), less as the guest fades out.
        float guest = _sound.FeatureSounding ? 1f - 0.3f * GuestPresence : 1f;
        // The off-beat pluck and the tresillo stabs are the pluck layer (intensity 1 = ladder rung 1).
        float pluck = Presence(1);
        var feature = _sound.CurrentFeature;
        bool percussionGuest = _sound.FeatureSounding && feature != null &&
                               (feature.Instruments & FeatureSelector.Instrument.Percussion) != 0;
        bool malletGuest = _sound.FeatureSounding && feature != null &&
                           (feature.Instruments & FeatureSelector.Instrument.Mallets) != 0;

        // Energy ticks at 0/90/180/270 degrees, just outside the ring.
        for (int i = 0; i < Ticks; i++)
        {
            float a = i * Mathf.PI * 0.5f;
            Place(_ticks[i], a, radius + 0.08f, 0.07f + 0.05f * _tick, TickColor, _tick * 0.8f * guest * pluck);
        }

        // Walking bass dot (hidden under a percussion guest: both would be dots going round the rim; back as the guest fades).
        float walkAngle = Mathf.PI * 0.5f - _walk * (2f * Mathf.PI / WalkSteps);
        Place(_walker, walkAngle, radius, 0.13f, WalkColor, _walkVisible * 0.85f * guest * (percussionGuest ? 1f - GuestPresence : 1f));

        // Tresillo stabs: three points a third of a turn apart, turning a little with each bar.
        float bar = Mathf.Floor((float)_phraseBeat / SoundManager.BeatsPerBar);
        for (int i = 0; i < Stabs; i++)
        {
            float a = Mathf.PI * 0.5f + bar * 0.3f + i * (2f * Mathf.PI / Stabs);
            Place(_stabs[i], a, radius, 0.1f + 0.1f * _stab, StabColor, _stab * guest * pluck);
        }

        // Combo 1: dotted ring turning one step per 16th.
        double beat = System.Math.Max(0.0, _sound.BeatPosition);
        float turn = Mathf.Floor((float)(beat * 4.0)) * (2f * Mathf.PI / ComboDots);
        for (int i = 0; i < ComboDots; i++)
        {
            float a = Mathf.PI * 0.5f - turn - i * (2f * Mathf.PI / ComboDots);
            Place(_comboDots[i], a, radius + ComboGap, 0.06f, GlassColor, _combo[1] * 0.55f * guest);
        }

        // Combo 2: glass halo breathing once a bar.
        bool halo = _combo[2] > 0.01f;
        if (_halo.gameObject.activeSelf != halo)
            _halo.gameObject.SetActive(halo);
        if (halo)
        {
            float breath = 0.5f - 0.5f * Mathf.Cos((float)(beat % SoundManager.BeatsPerBar) / SoundManager.BeatsPerBar * 2f * Mathf.PI);
            float r = radius + HaloGap;
            _halo.transform.localScale = new Vector3(r, r, 1f);
            _halo.startWidth = _halo.endWidth = 0.2f + 0.05f * breath;
            var c = _stage.Drain(GlassColor);
            c.a = _combo[2] * (0.08f + 0.06f * breath) * guest;
            _halo.startColor = _halo.endColor = c;
        }

        // Combo 3: bell sparkles on the halo (off under a mallet guest: both would be bell-like sparkles).
        for (int i = 0; i < BellPool; i++)
        {
            float t = _bellAge[i] / 0.5f;
            float alpha = t < 1f ? (1f - t) * _combo[3] * guest * (malletGuest ? 1f - GuestPresence : 1f) : 0f;
            Place(_bells[i], _bellAngle[i], radius + HaloGap, 0.14f * (1.2f - 0.5f * Mathf.Min(1f, t)),
                Color.Lerp(GlassColor, Color.white, 0.5f), alpha);
        }
    }

    void Place(SpriteRenderer sprite, float angle, float radius, float size, Color color, float alpha)
    {
        bool on = alpha > 0.005f;
        // Only touch the GameObject when visibility flips (SetActive every frame for ~45 sprites adds up).
        if (sprite.gameObject.activeSelf != on)
            sprite.gameObject.SetActive(on);
        if (!on)
            return;
        sprite.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        sprite.transform.localScale = Vector3.one * size;
        var c = _stage.Drain(color);
        c.a = Mathf.Clamp01(alpha);
        sprite.color = c;
    }

    static float Decay(float value, float rate, float dt) => value * Mathf.Exp(-rate * dt);

    SpriteRenderer MakeSprite(string objectName)
    {
        var sprite = new GameObject(objectName).AddComponent<SpriteRenderer>();
        sprite.transform.SetParent(transform, false);
        sprite.sprite = _dot;
        sprite.sortingOrder = Order;
        sprite.gameObject.SetActive(false);
        return sprite;
    }

    void OnDestroy() => DotSprite.Destroy(_dot);
}
