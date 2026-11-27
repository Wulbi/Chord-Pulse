using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The bed style's face (Docs/MusicVisualsPlan.md 2-0, 2-5): background palette, the boundary ring's shape and a
/// faint motif in the bands beside the arena.
/// <list type="bullet">
/// <item>synthpop: the plain neon line, horizontal grid lines in the side bands step down on every kick</item>
/// <item>latin: warm palette, the ring becomes 32 beads that swell on the son clave, sun rays turn on the clave</item>
/// <item>funk: purple/orange, the ring splits into 16 segments lit by the 16th hats, guitar chops flash it,
///       diagonal stripes slide every 16th</item>
/// <item>dreamy (breath): pastel, a soft second ring breathes once a bar, light motes rise slowly</item>
/// </list>
/// Styles cross-fade over the last beat before the heard phrase downbeat, using <see cref="SoundManager.UpcomingStyle"/>.
/// Runs after <see cref="MusicReactiveVisuals"/> so the shapes follow the ring's beat pulse in the same frame.
/// </summary>
[DefaultExecutionOrder(100), DisallowMultipleComponent]
public sealed class BedStyleVisuals : MonoBehaviour
{
    const int StyleCount = 4;
    const int MotifOrder = -5;
    const int RingOrder = -1;
    const float MotifAlpha = 0.13f;
    const int Beads = 32;
    const int Segments = 16;
    const int SegmentPoints = 6;
    const int GridLines = 7;
    const int Rays = 12;
    const int Stripes = 9;
    const int Motes = 16;
    // Son clave 3-2 over two bars, in beats (FMOD/Tools/bed_style.py CLAVE).
    static readonly float[] Clave = { 0f, 1.5f, 3f, 5f, 6f };

    // Palette each style leans the background towards, and how far.
    static readonly Color[] Palettes =
    {
        new Color(0.05f, 0.12f, 0.30f),  // synthpop: neon navy (the original look)
        new Color(0.30f, 0.14f, 0.06f),  // latin: terracotta
        new Color(0.22f, 0.08f, 0.28f),  // funk: purple
        new Color(0.20f, 0.24f, 0.34f),  // dreamy: pastel sky
    };
    static readonly float[] PaletteAmount = { 0f, 0.35f, 0.35f, 0.3f };
    // How much of the plain boundary line stays visible under each style's own ring shape.
    static readonly float[] BaseLineAlpha = { 1f, 0.3f, 0.25f, 0.6f };

    static readonly Color GridColor = new Color(0.45f, 0.75f, 1f);
    static readonly Color BeadColor = new Color(1f, 0.76f, 0.5f);
    static readonly Color SunColor = new Color(1f, 0.7f, 0.4f);
    static readonly Color FunkPurple = new Color(0.68f, 0.45f, 1f);
    static readonly Color FunkOrange = new Color(1f, 0.6f, 0.3f);
    static readonly Color DreamColor = new Color(0.78f, 0.86f, 1f);

    SoundManager _sound;
    ArenaStage _stage;
    Camera _camera;
    Material _material;
    Sprite _dot;
    readonly float[] _weight = { 1f, 0f, 0f, 0f };

    readonly List<SpriteRenderer> _beads = new List<SpriteRenderer>();
    readonly List<LineRenderer> _segments = new List<LineRenderer>();
    LineRenderer _dreamRing;
    readonly List<LineRenderer> _grid = new List<LineRenderer>();
    readonly List<LineRenderer> _rays = new List<LineRenderer>();
    readonly List<LineRenderer> _stripes = new List<LineRenderer>();
    readonly List<SpriteRenderer> _motes = new List<SpriteRenderer>();
    readonly float[] _moteSeed = new float[Motes];
    float _gridOffset;
    float _sunAngle;
    int _lastKickBeat = -1;
    int _lastClave = -1;

    // Parents that move whole groups, so their line geometry is written only once.
    Transform _segmentRoot;
    Transform _gridRoot;
    Transform _rayRoot;
    float _gridSpacing;
    float _baseHalfWidth;
    float _baseHalfHeight;
    // Which groups are currently shown, and the last colour state written, to skip redundant writes.
    bool _beadsShown, _segmentsShown, _gridShown, _raysShown, _stripesShown, _motesShown;
    float _gridAlpha = -1f, _rayAlpha = -1f, _stripeAlpha = -1f;
    int _lastStripeStep = int.MinValue;
    float _lastDesaturate = -1f, _lastDim = -1f;
    bool _drainChanged;

    public void Init(Camera playCamera, ArenaStage stage, Material material)
    {
        _sound = SoundManager.Instance;
        _camera = playCamera;
        _stage = stage;
        _material = material;
        _dot = DotSprite.Create("Style Dot");
        // Measured before any fever zoom, so the motif layout stays fixed.
        _baseHalfHeight = playCamera.orthographicSize;
        _baseHalfWidth = _baseHalfHeight * playCamera.aspect;
        _segmentRoot = Root("Style Segments");
        _gridRoot = Root("Motif Grid");
        _rayRoot = Root("Motif Rays");

        for (int i = 0; i < Beads; i++)
            _beads.Add(MakeSprite("Style Bead", RingOrder));
        // Funk segments on a unit circle; the root is scaled to the ring radius every frame.
        const float gap = 0.25f;   // fraction of each segment left open
        float span = 2f * Mathf.PI / Segments;
        for (int k = 0; k < Segments; k++)
        {
            var seg = Line("Style Segment", SegmentPoints, RingOrder, _segmentRoot);
            float start = Mathf.PI * 0.5f - k * span - span * gap * 0.5f;
            for (int p = 0; p < SegmentPoints; p++)
            {
                float a = start - span * (1f - gap) * p / (SegmentPoints - 1);
                seg.SetPosition(p, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
            }
            seg.startWidth = seg.endWidth = 0.045f;
            _segments.Add(seg);
        }
        _dreamRing = Line("Style Dream Ring", 64, RingOrder);
        _dreamRing.loop = true;
        for (int i = 0; i < 64; i++)
        {
            float a = i * (2f * Mathf.PI / 64);
            _dreamRing.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        }
        // Grid rows in both side bands, laid out once with a spare row above and below; scrolling moves the root.
        float inner = stage.BaseRadius + 0.4f;
        _gridSpacing = 2f * _baseHalfHeight / GridLines;
        for (int side = -1; side <= 1; side += 2)
            for (int row = -1; row <= GridLines; row++)
            {
                var line = Line("Motif Grid", 2, MotifOrder, _gridRoot);
                float y = _baseHalfHeight - row * _gridSpacing;
                line.SetPosition(0, new Vector3(side * inner, y, 0f));
                line.SetPosition(1, new Vector3(side * (_baseHalfWidth * 1.1f + 0.3f), y, 0f));
                line.startWidth = line.endWidth = 0.02f;
                _grid.Add(line);
            }
        // Sun rays from just outside the arena to beyond the corners; the root turns on the clave.
        float outer = Mathf.Sqrt(_baseHalfWidth * _baseHalfWidth + _baseHalfHeight * _baseHalfHeight) * 1.1f;
        for (int i = 0; i < Rays; i++)
        {
            var ray = Line("Motif Ray", 2, MotifOrder, _rayRoot);
            float a = i * (2f * Mathf.PI / Rays);
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            ray.SetPosition(0, dir * inner);
            ray.SetPosition(1, dir * outer);
            ray.startWidth = 0.05f;
            ray.endWidth = 0.6f;
            _rays.Add(ray);
        }
        for (int i = 0; i < Stripes * 2; i++)
        {
            var stripe = Line("Motif Stripe", 2, MotifOrder);
            stripe.startWidth = stripe.endWidth = 0.08f;
            _stripes.Add(stripe);
        }
        for (int i = 0; i < Motes; i++)
        {
            _motes.Add(MakeSprite("Motif Mote", MotifOrder));
            _moteSeed[i] = Random.value;
        }
        ResetVisuals();
    }

    public void ResetVisuals()
    {
        for (int i = 0; i < StyleCount; i++)
            _weight[i] = i == SoundManager.StyleSynthpop ? 1f : 0f;
        _lastKickBeat = -1;
        _lastClave = -1;
    }

    void Update()
    {
        using var profile = VisualProfiler.StyleUpdate.Auto();
        if (_sound == null || _stage == null)
            return;
        UpdateWeights();

        // Background palette: weighted blend of the styles' palettes.
        Color tint = Color.black;
        float amount = 0f;
        float lineAlpha = 0f;
        for (int i = 0; i < StyleCount; i++)
        {
            tint += Palettes[i] * (_weight[i] * PaletteAmount[i]);
            amount += _weight[i] * PaletteAmount[i];
            lineAlpha += _weight[i] * BaseLineAlpha[i];
        }
        if (amount > 0.0001f)
        {
            _stage.BackgroundTint = tint / amount;
            _stage.BackgroundTintAmount = amount;
        }
        _stage.RingAlpha *= lineAlpha;
    }

    void UpdateWeights()
    {
        int target = _sound.HeardStyle;
        double beat = _sound.BeatPosition;
        if (beat >= 0)
        {
            // Start blending into the next phrase's style one beat before its downbeat.
            double phraseStartBeat = (_sound.NextPhraseStep - SoundManager.StepsPerPhrase) / (double)SoundManager.StepsPerBeat;
            float beatsPerPhrase = SoundManager.StepsPerPhrase / (float)SoundManager.StepsPerBeat;
            int upcoming = _sound.FeverPending ? SoundManager.StyleSynthpop : _sound.UpcomingStyle;
            if (beat - phraseStartBeat >= beatsPerPhrase - 1f)
                target = upcoming;
        }
        float step = Time.deltaTime / Mathf.Max(0.05f, (float)_sound.BeatSeconds);
        for (int i = 0; i < StyleCount; i++)
            _weight[i] = Mathf.MoveTowards(_weight[i], i == target ? 1f : 0f, step);
    }

    void LateUpdate()
    {
        using var profile = VisualProfiler.StyleLate.Auto();
        if (_sound == null || _stage == null)
            return;
        double beat = Mathf.Max(0f, (float)_sound.BeatPosition);
        bool running = _sound.BeatPosition >= 0;
        // Guests take the foreground: the motif steps back while one plays (plan 3-4), less as the guest fades out after
        // a combo break (not in fever: fever's guest does not fade).
        float guest = _sound.FeverActive ? 1f : _sound.GuestPresence;
        float motif = MotifAlpha * (_sound.FeatureSounding ? 1f - 0.5f * guest : 1f);
        // Danger drains colour: cached line colours must be rewritten only when the drain moves.
        _drainChanged = Mathf.Abs(_stage.AppliedDesaturate - _lastDesaturate) > 0.01f || Mathf.Abs(_stage.AppliedDim - _lastDim) > 0.01f;
        if (_drainChanged)
        {
            _lastDesaturate = _stage.AppliedDesaturate;
            _lastDim = _stage.AppliedDim;
        }

        DrawBeads(_weight[SoundManager.StyleLatin], beat, running);
        DrawSegments(_weight[SoundManager.StyleFunk], beat, running);
        DrawDreamRing(_weight[SoundManager.StyleDreamy], beat);
        DrawGrid(_weight[SoundManager.StyleSynthpop] * motif, running);
        DrawRays(_weight[SoundManager.StyleLatin] * motif, beat, running);
        DrawStripes(_weight[SoundManager.StyleFunk] * motif, beat);
        DrawMotes(_weight[SoundManager.StyleDreamy] * motif * 1.5f);
    }

    // ---- Ring shapes ----
    // Performance: line geometry is written once in Init and moved with a parent transform (scale / rotation /
    // offset), colours are written only when they change, and a hidden group is skipped after hiding it once.
    // Every LineRenderer property write rebuilds that line's mesh, which showed up as frame spikes.

    void DrawBeads(float weight, double beat, bool running)
    {
        if (!SetGroup(_beads, ref _beadsShown, weight > 0.01f))
            return;
        float claveAge = 99f;
        int claveIndex = -1;
        if (running)
        {
            float inTwoBars = (float)(beat % 8.0);
            for (int i = Clave.Length - 1; i >= 0; i--)
                if (inTwoBars >= Clave[i])
                {
                    claveIndex = i;
                    claveAge = inTwoBars - Clave[i];
                    break;
                }
        }
        float radius = _stage.RingRadius;
        for (int i = 0; i < Beads; i++)
        {
            var bead = _beads[i];
            float a = Mathf.PI * 0.5f - i * (2f * Mathf.PI / Beads);
            // Each clave hit swells the beads around its own fifth of the circle.
            float swell = 0f;
            if (claveIndex >= 0)
            {
                float centre = claveIndex * (Beads / (float)Clave.Length);
                float d = Mathf.Abs(Mathf.DeltaAngle(i * 360f / Beads, centre * 360f / Beads)) / (360f / Beads);
                swell = Mathf.Exp(-6f * claveAge) * Mathf.Clamp01(1f - d / 3f);
            }
            bead.transform.localPosition = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
            bead.transform.localScale = Vector3.one * (0.11f + 0.05f * _stage.BeatImpact + 0.12f * swell);
            var c = _stage.Drain(Color.Lerp(BeadColor, Color.white, swell * 0.5f));
            c.a = weight * (0.75f + 0.25f * swell);
            bead.color = c;
        }
    }

    void DrawSegments(float weight, double beat, bool running)
    {
        if (!SetGroup(_segments, ref _segmentsShown, weight > 0.01f))
            return;
        float inBar = (float)(beat % SoundManager.BeatsPerBar);
        int lit = Mathf.FloorToInt(inBar * 4f) % Segments;
        float litAge = inBar * 4f - Mathf.Floor(inBar * 4f);
        // Clean guitar chops on beats 2 and 4 from Intensity 2 (bed_style.py funk), as much as that layer (ladder rung 2)
        // is heard: a combo break fades them on the sound's curve (fever has its own arrangement: not scaled).
        float chop = 0f;
        if (running && _sound.ArrangementIntensity >= 2)
        {
            int beatInBar = Mathf.FloorToInt(inBar);
            if (beatInBar % 2 == 1)
                chop = Mathf.Exp(-10f * (inBar - beatInBar)) * (_sound.FeverActive ? 1f : _sound.LayerPresence(2));
        }
        // The segments sit on a unit circle; the root follows the ring's pulse.
        float radius = _stage.RingRadius;
        _segmentRoot.localScale = new Vector3(radius, radius, 1f);
        for (int k = 0; k < Segments; k++)
        {
            // Quantised so a segment is only rebuilt when its look really changes.
            float glow = Mathf.Round(Mathf.Max(k == lit ? 1f - litAge : 0f, chop) * 12f) / 12f;
            var c = _stage.Drain(Color.Lerp(FunkPurple, FunkOrange, glow));
            c.a = Mathf.Round(weight * (0.55f + 0.45f * glow) * 40f) / 40f;
            var seg = _segments[k];
            if (seg.startColor != c)
            {
                seg.startColor = seg.endColor = c;
                seg.startWidth = seg.endWidth = 0.045f + 0.02f * glow;
            }
        }
    }

    void DrawDreamRing(float weight, double beat)
    {
        bool on = weight > 0.01f;
        if (_dreamRing.gameObject.activeSelf != on)
            _dreamRing.gameObject.SetActive(on);
        if (!on)
            return;
        // One slow breath per bar, like the choir chords.
        float breath = 0.5f - 0.5f * Mathf.Cos((float)(beat % SoundManager.BeatsPerBar) / SoundManager.BeatsPerBar * 2f * Mathf.PI);
        float r = _stage.RingRadius * (1.06f + 0.02f * breath);
        _dreamRing.transform.localScale = new Vector3(r, r, 1f);
        _dreamRing.startWidth = _dreamRing.endWidth = 0.07f + 0.04f * breath;
        var c = _stage.Drain(DreamColor);
        c.a = weight * (0.15f + 0.15f * breath);
        _dreamRing.startColor = _dreamRing.endColor = c;
    }

    // ---- Motifs in the bands beside the arena ----

    float BandInner => _stage.BaseRadius + 0.4f;
    bool HasBands => _baseHalfWidth > BandInner + 0.3f;

    void DrawGrid(float alpha, bool running)
    {
        // Each kick moves the lines one half-spacing down, eased, so the grid seems to come towards you.
        int beat = _sound.CurrentBeat;
        if (running && beat != _lastKickBeat && _sound.KickOn(beat))
        {
            _lastKickBeat = beat;
            _gridOffset += 0.5f;
        }
        if (!SetGroup(_grid, ref _gridShown, alpha > 0.002f && HasBands))
            return;
        // Rows are laid out once (one spare row); scrolling just moves the root by less than one spacing.
        float shown = Mathf.Repeat(_gridOffset - 0.5f * _stage.BeatImpact, 1f);
        _gridRoot.localPosition = new Vector3(0f, -shown * _gridSpacing, 0f);
        SetColors(_grid, GridColor, alpha * 0.7f, ref _gridAlpha);
    }

    void DrawRays(float alpha, double beat, bool running)
    {
        if (running)
        {
            int clave = -1;
            float inTwoBars = (float)(beat % 8.0);
            for (int i = Clave.Length - 1; i >= 0; i--)
                if (inTwoBars >= Clave[i]) { clave = i; break; }
            if (clave != _lastClave)
            {
                _lastClave = clave;
                _sunAngle += 360f / Rays / 2f;   // half a ray per clave hit
            }
        }
        if (!SetGroup(_rays, ref _raysShown, alpha > 0.002f))
            return;
        _rayRoot.localRotation = Quaternion.Euler(0f, 0f, _sunAngle);
        // Rays fade towards their tips, so only the start colour carries the alpha.
        float a = Mathf.Round(alpha * 400f) / 400f;
        if (Mathf.Abs(a - _rayAlpha) < 0.0001f && !_drainChanged)
            return;
        _rayAlpha = a;
        var c = _stage.Drain(SunColor);
        c.a = a;
        var tip = c;
        tip.a = 0f;
        foreach (var ray in _rays)
        {
            ray.startColor = c;
            ray.endColor = tip;
        }
    }

    void DrawStripes(float alpha, double beat)
    {
        if (!SetGroup(_stripes, ref _stripesShown, alpha > 0.002f && HasBands))
            return;
        float band = _baseHalfWidth - BandInner;
        float spacing = band / Stripes * 2f;
        int sixteenth = Mathf.FloorToInt((float)(beat * 4.0));
        // Positions change only on a 16th, so rebuild the lines then and not every frame.
        if (sixteenth != _lastStripeStep)
        {
            _lastStripeStep = sixteenth;
            float slide = sixteenth * spacing * 0.25f;
            float h = _baseHalfHeight * 1.1f + 0.5f;
            for (int i = 0; i < _stripes.Count; i++)
            {
                int side = i < Stripes ? -1 : 1;
                int k = i % Stripes;
                float x = BandInner + Mathf.Repeat(k * spacing + slide, band + spacing) - spacing * 0.5f;
                _stripes[i].SetPosition(0, new Vector3(side * Mathf.Max(BandInner, x - 0.8f), -h, 0f));
                _stripes[i].SetPosition(1, new Vector3(side * Mathf.Max(BandInner, x + 0.8f), h, 0f));
            }
        }
        float a = Mathf.Round(alpha * 400f) / 400f;
        if (Mathf.Abs(a - _stripeAlpha) < 0.0001f && !_drainChanged)
            return;
        _stripeAlpha = a;
        for (int i = 0; i < _stripes.Count; i++)
        {
            var c = _stage.Drain((i % Stripes) % 2 == 0 ? FunkPurple : FunkOrange);
            c.a = a;
            _stripes[i].startColor = _stripes[i].endColor = c;
        }
    }

    void DrawMotes(float alpha)
    {
        if (!SetGroup(_motes, ref _motesShown, alpha > 0.002f && HasBands))
            return;
        float band = _baseHalfWidth - BandInner;
        float t = Time.time * 0.08f;
        for (int i = 0; i < Motes; i++)
        {
            var mote = _motes[i];
            float seed = _moteSeed[i];
            int side = i % 2 == 0 ? -1 : 1;
            float rise = Mathf.Repeat(seed + t * (0.6f + seed * 0.5f), 1f);
            float x = side * (BandInner + band * Mathf.Repeat(seed * 7.3f + Mathf.Sin(t * 3f + seed * 10f) * 0.05f, 1f));
            mote.transform.localPosition = new Vector3(x, Mathf.Lerp(-_baseHalfHeight, _baseHalfHeight, rise), 0f);
            mote.transform.localScale = Vector3.one * (0.12f + 0.1f * seed);
            var c = _stage.Drain(DreamColor);
            c.a = alpha * Mathf.Sin(rise * Mathf.PI) * 3f;
            mote.color = c;
        }
    }

    /// <summary>Shows or hides a whole group, touching GameObjects only when the state flips.</summary>
    static bool SetGroup<T>(List<T> items, ref bool shown, bool on) where T : Component
    {
        if (on != shown)
        {
            shown = on;
            foreach (var item in items)
                item.gameObject.SetActive(on);
        }
        return on;
    }

    /// <summary>Writes one flat colour to a group of lines, only when its (quantised) alpha or the drain changed.</summary>
    void SetColors(List<LineRenderer> lines, Color color, float alpha, ref float lastAlpha)
    {
        float a = Mathf.Round(alpha * 400f) / 400f;
        if (Mathf.Abs(a - lastAlpha) < 0.0001f && !_drainChanged)
            return;
        lastAlpha = a;
        var c = _stage.Drain(color);
        c.a = a;
        foreach (var line in lines)
            line.startColor = line.endColor = c;
    }

    // ---- Helpers ----

    Transform Root(string objectName)
    {
        var root = new GameObject(objectName).transform;
        root.SetParent(transform, false);
        return root;
    }

    LineRenderer Line(string objectName, int points, int order, Transform parent = null)
    {
        var line = new GameObject(objectName).AddComponent<LineRenderer>();
        line.transform.SetParent(parent != null ? parent : transform, false);
        line.useWorldSpace = false;
        line.positionCount = points;
        line.sharedMaterial = _material;
        line.sortingOrder = order;
        line.numCapVertices = 2;
        line.gameObject.SetActive(false);
        return line;
    }

    SpriteRenderer MakeSprite(string objectName, int order)
    {
        var sprite = new GameObject(objectName).AddComponent<SpriteRenderer>();
        sprite.transform.SetParent(transform, false);
        sprite.sprite = _dot;
        sprite.sortingOrder = order;
        sprite.gameObject.SetActive(false);
        return sprite;
    }

    void OnDestroy() => DotSprite.Destroy(_dot);
}
