using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Light from the edges:
/// <list type="bullet">
/// <item>Spotlights (horn section): cones of light rise from the bottom corners, leaning in more for higher notes;
///       a stab fans out into one beam per stacked voice. Like stage lights on a brass section.</item>
/// <item>KeyBars (FM piano): a piano-roll bar lights across the band beside the arena at the note's height,
///       one bar per voice of the chord, stacked downwards.</item>
/// </list>
/// </summary>
[CreateAssetMenu(menuName = "Chord Pulse/Feature Visuals/Beam Style")]
public sealed class BeamStyle : FeatureVisualStyle
{
    public enum Mode { Spotlights, KeyBars }

    public Mode mode = Mode.Spotlights;
    [Tooltip("Spotlights: lean towards the middle for the lowest and highest note (degrees).")]
    public Vector2 lean = new Vector2(4f, 24f);
    [Tooltip("Spotlights: spread between the beams of one stab (degrees).")]
    public float fan = 7f;
    [Tooltip("KeyBars: bar thickness and gap between stacked bars.")]
    public float barHeight = 0.11f;
    public float barGap = 0.2f;

    public BeamStyle()
    {
        color = new Color(1f, 0.8f, 0.45f, 0.22f);
        midiRange = new Vector2Int(55, 84);
        seconds = 0.55f;
        size = 1f;
        prewarm = 12;
    }

    public override IFeatureEmitter CreateEmitter(FeatureVisualContext context) => new BeamEmitter(this, context);
}

public sealed class BeamEmitter : IFeatureEmitter
{
    sealed class Beam
    {
        public LineRenderer Line;
        public SpriteRenderer Bar;
        public float Width;
        public float Age;
        public bool Active;
        public GameObject Go => Line != null ? Line.gameObject : Bar.gameObject;
    }

    readonly BeamStyle _style;
    readonly FeatureVisualContext _context;
    readonly List<Beam> _beams = new List<Beam>();
    int _side = 1;

    public BeamEmitter(BeamStyle style, FeatureVisualContext context)
    {
        _style = style;
        _context = context;
    }

    public void Emit(FeatureLoopNotes.Note note, float sizeScale, int side)
    {
        int s = FeatureVisualContext.NextSide(ref _side, side);
        int voices = Mathf.Max(1, note.Voices);
        if (_style.mode == BeamStyle.Mode.KeyBars)
        {
            float inner = _context.ArenaRadius + 0.45f;
            float outer = Mathf.Max(inner + 0.3f, _context.HalfWidth - 0.2f);
            float y = _style.PitchToY(_context, note.Midi);
            for (int v = 0; v < voices; v++)
            {
                var b = Take();
                var tr = b.Bar.transform;
                tr.localPosition = new Vector3(s * (inner + outer) * 0.5f, y - v * _style.barGap * sizeScale, 0f);
                tr.localScale = new Vector3(outer - inner, _style.barHeight * sizeScale, 1f);
                Show(b, sizeScale);
            }
            return;
        }

        float pitch = Mathf.InverseLerp(_style.midiRange.x, _style.midiRange.y, note.Midi);
        float h = _context.HalfHeight;
        var origin = new Vector3(s * (_context.HalfWidth - 0.3f), -h - 0.2f, 0f);
        for (int v = 0; v < voices; v++)
        {
            float lean = Mathf.Lerp(_style.lean.x, _style.lean.y, pitch) + (v - (voices - 1) * 0.5f) * _style.fan;
            float a = lean * Mathf.Deg2Rad;
            var dir = new Vector3(-s * Mathf.Sin(a), Mathf.Cos(a), 0f);
            var b = Take();
            b.Line.SetPosition(0, origin);
            b.Line.SetPosition(1, origin + dir * (2f * h + 0.4f));
            Show(b, sizeScale);
        }
    }

    void Show(Beam b, float sizeScale)
    {
        b.Age = 0f;
        b.Active = true;
        b.Width = _style.size * sizeScale;
        b.Go.SetActive(true);
        Apply(b, 0f);
    }

    public void Tick(float deltaTime)
    {
        foreach (var b in _beams)
        {
            if (!b.Active)
                continue;
            b.Age += deltaTime;
            float t = b.Age / _style.seconds;
            if (t >= 1f)
            {
                Hide(b);
                continue;
            }
            Apply(b, t);
        }
    }

    void Apply(Beam b, float t)
    {
        // Snaps on bright (a touch of white), then fades.
        var c = Color.Lerp(Color.white, _style.color, Mathf.Clamp01(t * 5f));
        c.a = _style.color.a * (1f - t) * (1f - t) * (t < 0.1f ? 1.6f : 1f);
        if (b.Line != null)
        {
            b.Line.startWidth = 0.06f * b.Width;
            // A narrow cone: stage-light feel without covering the side band.
            b.Line.endWidth = 0.5f * b.Width * (1f - 0.3f * t);
            var tip = c;
            tip.a *= 0.15f;
            b.Line.startColor = c;
            b.Line.endColor = tip;
        }
        else
            b.Bar.color = c;
    }

    public void Prewarm(int count)
    {
        while (_beams.Count < count)
            Take().Active = true;
        Clear();
    }

    public void Clear()
    {
        foreach (var b in _beams)
            Hide(b);
    }

    Beam Take()
    {
        foreach (var b in _beams)
            if (!b.Active)
                return b;
        var created = new Beam();
        if (_style.mode == BeamStyle.Mode.KeyBars)
        {
            var go = new GameObject("Feature Key Bar");
            go.transform.SetParent(_context.Root, false);
            created.Bar = go.AddComponent<SpriteRenderer>();
            created.Bar.sprite = _context.Square;
            created.Bar.sortingOrder = _context.SortingOrder;
        }
        else
        {
            var line = new GameObject("Feature Beam").AddComponent<LineRenderer>();
            line.transform.SetParent(_context.Root, false);
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.sharedMaterial = _context.Material;
            line.sortingOrder = _context.SortingOrder;
            created.Line = line;
        }
        _beams.Add(created);
        return created;
    }

    static void Hide(Beam b)
    {
        b.Active = false;
        b.Go.SetActive(false);
    }
}
