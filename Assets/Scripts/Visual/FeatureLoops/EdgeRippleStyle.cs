using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Synth look: an arc-shaped wave rolls in from a screen edge towards the arena at the note's pitch height,
/// switching sides note by note. Gated like the synth lead: it flashes on hard and is gone quickly.
/// It stops short of the arena so it never reads as a player pulse. Every other 2-bar group uses the
/// alternate colour, so a 4-bar riff does not look like the same 2 bars twice.
/// </summary>
[CreateAssetMenu(menuName = "Chord Pulse/Feature Visuals/Edge Ripple Style")]
public sealed class EdgeRippleStyle : FeatureVisualStyle
{
    public Color alternateColor = new Color(0.72f, 0.5f, 1f, 0.8f);
    [Tooltip("Half the arc's opening angle, in degrees.")]
    public float arcDegrees = 35f;
    [Tooltip("How far beyond the screen edge the arc's centre sits.")]
    public float centreOutside = 0.8f;
    [Tooltip("Closest the wave gets to the arena boundary.")]
    public float arenaGap = 0.3f;
    public float lineWidth = 0.06f;

    public EdgeRippleStyle()
    {
        color = new Color(1f, 0.45f, 0.85f, 0.8f);
        midiRange = new Vector2Int(62, 84);
        seconds = 0.4f;
        size = 1f;
    }

    public override IFeatureEmitter CreateEmitter(FeatureVisualContext context) => new EdgeRippleEmitter(this, context);
}

public sealed class EdgeRippleEmitter : IFeatureEmitter
{
    const int Segments = 16;

    sealed class Ripple
    {
        public LineRenderer Line;
        public Color Color;
        public float MaxRadius;
        public float Width;
        public float Age;
        public bool Active;
    }

    readonly EdgeRippleStyle _style;
    readonly FeatureVisualContext _context;
    readonly List<Ripple> _ripples = new List<Ripple>();
    int _side = 1;

    public EdgeRippleEmitter(EdgeRippleStyle style, FeatureVisualContext context)
    {
        _style = style;
        _context = context;
    }

    public void Emit(FeatureLoopNotes.Note note, float sizeScale, int side)
    {
        int s = FeatureVisualContext.NextSide(ref _side, side);
        float centreX = s * (_context.HalfWidth + _style.centreOutside);
        float maxRadius = Mathf.Abs(centreX) - (_context.ArenaRadius + _style.arenaGap);
        if (maxRadius <= 0.2f)
            return;   // no room beside the arena (portrait screen)

        var ripple = Take();
        ripple.Age = 0f;
        ripple.Active = true;
        ripple.MaxRadius = maxRadius * Mathf.Clamp01(_style.size * sizeScale);
        ripple.Width = _style.lineWidth * sizeScale;
        bool alternate = Mathf.FloorToInt(note.Beat / (2 * SoundManager.BeatsPerBar)) % 2 == 1;
        ripple.Color = alternate ? _style.alternateColor : _style.color;

        // Unit arc facing the arena; the transform scale sets the radius (as MusicReactiveVisuals' ripple does).
        float half = _style.arcDegrees * Mathf.Deg2Rad;
        float facing = s > 0 ? Mathf.PI : 0f;
        for (int i = 0; i < Segments; i++)
        {
            float a = facing + Mathf.Lerp(-half, half, i / (float)(Segments - 1));
            ripple.Line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        }
        var t = ripple.Line.transform;
        t.localPosition = new Vector3(centreX, _style.PitchToY(_context, note.Midi), 0f);
        t.gameObject.SetActive(true);
        Apply(ripple, 0f);
    }

    public void Tick(float deltaTime)
    {
        foreach (var ripple in _ripples)
        {
            if (!ripple.Active)
                continue;
            ripple.Age += deltaTime;
            float t = ripple.Age / _style.seconds;
            if (t >= 1f)
            {
                Hide(ripple);
                continue;
            }
            Apply(ripple, t);
        }
    }

    static void Apply(Ripple ripple, float t)
    {
        float eased = 1f - (1f - t) * (1f - t);
        float radius = Mathf.Lerp(0.4f, ripple.MaxRadius, eased);
        ripple.Line.transform.localScale = new Vector3(radius, radius, 1f);
        ripple.Line.startWidth = ripple.Line.endWidth = ripple.Width * (1f - 0.6f * t);
        var c = ripple.Color;
        // Gated: stays bright for the first half, then cuts away fast.
        c.a *= t < 0.5f ? 1f : Mathf.Clamp01((1f - t) * 2f);
        ripple.Line.startColor = ripple.Line.endColor = c;
    }

    public void Clear()
    {
        foreach (var ripple in _ripples)
            Hide(ripple);
    }

    Ripple Take()
    {
        foreach (var ripple in _ripples)
            if (!ripple.Active)
                return ripple;
        var go = new GameObject("Feature Edge Ripple");
        go.transform.SetParent(_context.Root, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = Segments;
        line.sharedMaterial = _context.Material;
        line.sortingOrder = _context.SortingOrder;
        line.numCapVertices = 2;
        var created = new Ripple { Line = line };
        _ripples.Add(created);
        return created;
    }

    static void Hide(Ripple ripple)
    {
        ripple.Active = false;
        ripple.Line.gameObject.SetActive(false);
    }
}
