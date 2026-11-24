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

    public void Init(Camera playCamera, ArenaStage stage, Material material)
    {
        _sound = SoundManager.Instance;
        _camera = playCamera;
        _stage = stage;
        _material = material;
        _dot = CreateDot();

        for (int i = 0; i < Beads; i++)
            _beads.Add(MakeSprite("Style Bead", RingOrder));
        for (int i = 0; i < Segments; i++)
            _segments.Add(Line("Style Segment", SegmentPoints, RingOrder));
        _dreamRing = Line("Style Dream Ring", 64, RingOrder);
        _dreamRing.loop = true;
        for (int i = 0; i < 64; i++)
        {
            float a = i * (2f * Mathf.PI / 64);
            _dreamRing.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        }
        for (int i = 0; i < GridLines * 2; i++)
            _grid.Add(Line("Motif Grid", 2, MotifOrder));
        for (int i = 0; i < Rays; i++)
            _rays.Add(Line("Motif Ray", 2, MotifOrder));
        for (int i = 0; i < Stripes * 2; i++)
            _stripes.Add(Line("Motif Stripe", 2, MotifOrder));
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
        if (_sound == null || _stage == null)
            return;
        double beat = Mathf.Max(0f, (float)_sound.BeatPosition);
        bool running = _sound.BeatPosition >= 0;
        // Guests take the foreground: the motif steps back while one plays (plan 3-4).
        float motif = MotifAlpha * (_sound.FeatureSounding ? 0.5f : 1f);

        DrawBeads(_weight[SoundManager.StyleLatin], beat, running);
        DrawSegments(_weight[SoundManager.StyleFunk], beat, running);
        DrawDreamRing(_weight[SoundManager.StyleDreamy], beat);
        DrawGrid(_weight[SoundManager.StyleSynthpop] * motif, running);
        DrawRays(_weight[SoundManager.StyleLatin] * motif, beat, running);
        DrawStripes(_weight[SoundManager.StyleFunk] * motif, beat);
        DrawMotes(_weight[SoundManager.StyleDreamy] * motif * 1.5f);
    }

    // ---- Ring shapes ----

    void DrawBeads(float weight, double beat, bool running)
    {
        bool on = weight > 0.01f;
        float claveAge = 99f;
        int claveIndex = -1;
        if (on && running)
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
            bead.gameObject.SetActive(on);
            if (!on)
                continue;
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
        bool on = weight > 0.01f;
        float inBar = (float)(beat % SoundManager.BeatsPerBar);
        int lit = Mathf.FloorToInt(inBar * 4f) % Segments;
        float litAge = inBar * 4f - Mathf.Floor(inBar * 4f);
        // Clean guitar chops on beats 2 and 4 from Intensity 2 (bed_style.py funk).
        float chop = 0f;
        if (running && _sound.ArrangementIntensity >= 2)
        {
            int beatInBar = Mathf.FloorToInt(inBar);
            if (beatInBar % 2 == 1)
                chop = Mathf.Exp(-10f * (inBar - beatInBar));
        }
        float radius = _stage.RingRadius;
        const float gap = 0.25f;   // fraction of each segment left open
        for (int k = 0; k < Segments; k++)
        {
            var seg = _segments[k];
            seg.gameObject.SetActive(on);
            if (!on)
                continue;
            float span = 2f * Mathf.PI / Segments;
            float start = Mathf.PI * 0.5f - k * span - span * gap * 0.5f;
            for (int p = 0; p < SegmentPoints; p++)
            {
                float a = start - span * (1f - gap) * p / (SegmentPoints - 1);
                seg.SetPosition(p, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
            float glow = k == lit ? 1f - litAge : 0f;
            glow = Mathf.Max(glow, chop);
            var c = _stage.Drain(Color.Lerp(FunkPurple, FunkOrange, glow));
            c.a = weight * (0.55f + 0.45f * glow);
            seg.startColor = seg.endColor = c;
            seg.startWidth = seg.endWidth = _stage.RingWidth + 0.02f + 0.02f * glow;
        }
    }

    void DrawDreamRing(float weight, double beat)
    {
        bool on = weight > 0.01f;
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
    float HalfWidth => _camera.orthographicSize * _camera.aspect;
    float HalfHeight => _camera.orthographicSize;

    void DrawGrid(float alpha, bool running)
    {
        bool on = alpha > 0.002f && HalfWidth > BandInner + 0.3f;
        // Each kick moves the lines one half-spacing down, eased, so the grid seems to come towards you.
        int beat = _sound.CurrentBeat;
        if (running && beat != _lastKickBeat && _sound.KickOn(beat))
        {
            _lastKickBeat = beat;
            _gridOffset += 0.5f;
        }
        float spacing = 2f * HalfHeight / GridLines;
        float shown = Mathf.Repeat(_gridOffset - 0.5f * _stage.BeatImpact, 1f);
        for (int i = 0; i < _grid.Count; i++)
        {
            var line = _grid[i];
            line.gameObject.SetActive(on);
            if (!on)
                continue;
            int side = i < GridLines ? -1 : 1;
            int row = i % GridLines;
            float y = HalfHeight - (row + shown) * spacing;
            float depth = 1f - Mathf.Abs(y) / HalfHeight;   // brighter towards the middle
            line.SetPosition(0, new Vector3(side * BandInner, y, 0f));
            line.SetPosition(1, new Vector3(side * (HalfWidth + 0.2f), y, 0f));
            line.startWidth = line.endWidth = 0.02f;
            var c = _stage.Drain(GridColor);
            c.a = alpha * (0.4f + 0.6f * depth);
            line.startColor = line.endColor = c;
        }
    }

    void DrawRays(float alpha, double beat, bool running)
    {
        bool on = alpha > 0.002f;
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
        float outer = Mathf.Sqrt(HalfWidth * HalfWidth + HalfHeight * HalfHeight);
        for (int i = 0; i < Rays; i++)
        {
            var ray = _rays[i];
            ray.gameObject.SetActive(on);
            if (!on)
                continue;
            float a = (_sunAngle + i * 360f / Rays) * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            ray.SetPosition(0, dir * BandInner);
            ray.SetPosition(1, dir * outer);
            ray.startWidth = 0.05f;
            ray.endWidth = 0.6f;
            var c = _stage.Drain(SunColor);
            c.a = alpha;
            ray.startColor = c;
            c.a = 0f;
            ray.endColor = c;
        }
    }

    void DrawStripes(float alpha, double beat)
    {
        bool on = alpha > 0.002f && HalfWidth > BandInner + 0.3f;
        float band = HalfWidth - BandInner;
        float spacing = band / Stripes * 2f;
        float slide = Mathf.Floor((float)(beat * 4.0)) * spacing * 0.25f;   // jumps every 16th
        for (int i = 0; i < _stripes.Count; i++)
        {
            var stripe = _stripes[i];
            stripe.gameObject.SetActive(on);
            if (!on)
                continue;
            int side = i < Stripes ? -1 : 1;
            int k = i % Stripes;
            float x = BandInner + Mathf.Repeat(k * spacing + slide, band + spacing) - spacing * 0.5f;
            float h = HalfHeight + 0.5f;
            stripe.SetPosition(0, new Vector3(side * Mathf.Max(BandInner, x - 0.8f), -h, 0f));
            stripe.SetPosition(1, new Vector3(side * Mathf.Max(BandInner, x + 0.8f), h, 0f));
            stripe.startWidth = stripe.endWidth = 0.08f;
            var c = _stage.Drain(k % 2 == 0 ? FunkPurple : FunkOrange);
            c.a = alpha;
            stripe.startColor = stripe.endColor = c;
        }
    }

    void DrawMotes(float alpha)
    {
        bool on = alpha > 0.002f && HalfWidth > BandInner + 0.3f;
        float band = HalfWidth - BandInner;
        float t = Time.time * 0.08f;
        for (int i = 0; i < Motes; i++)
        {
            var mote = _motes[i];
            mote.gameObject.SetActive(on);
            if (!on)
                continue;
            float seed = _moteSeed[i];
            int side = i % 2 == 0 ? -1 : 1;
            float rise = Mathf.Repeat(seed + t * (0.6f + seed * 0.5f), 1f);
            float x = side * (BandInner + band * Mathf.Repeat(seed * 7.3f + Mathf.Sin(t * 3f + seed * 10f) * 0.05f, 1f));
            mote.transform.localPosition = new Vector3(x, Mathf.Lerp(-HalfHeight, HalfHeight, rise), 0f);
            mote.transform.localScale = Vector3.one * (0.12f + 0.1f * seed);
            var c = _stage.Drain(DreamColor);
            c.a = alpha * Mathf.Sin(rise * Mathf.PI) * 3f;
            mote.color = c;
        }
    }

    // ---- Helpers ----

    LineRenderer Line(string objectName, int points, int order)
    {
        var line = new GameObject(objectName).AddComponent<LineRenderer>();
        line.transform.SetParent(transform, false);
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

    static Sprite CreateDot()
    {
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Style Dot", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        float r = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 2.5f));
            }
        texture.SetPixels(pixels);
        texture.Apply();
        return UnityEngine.Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    void OnDestroy()
    {
        if (_dot != null)
        {
            Destroy(_dot.texture);
            Destroy(_dot);
        }
    }
}
