using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Raindrops on water (tine electric piano): each note drops a small ring in the band beside the arena that widens
/// and fades, with a second, fainter ring behind it. Height follows pitch; stacked voices drop a ring each,
/// a little apart. Full circles, but small and outside the arena, so they never read as a player pulse.
/// </summary>
[CreateAssetMenu(menuName = "Chord Pulse/Feature Visuals/Droplet Style")]
public sealed class DropletStyle : FeatureVisualStyle
{
    [Tooltip("Radius the ring grows to.")]
    public float radius = 0.55f;
    public float lineWidth = 0.035f;
    [Tooltip("The echo ring starts this much later (fraction of the life).")]
    public float echoDelay = 0.25f;

    public DropletStyle()
    {
        color = new Color(0.7f, 0.95f, 1f, 0.8f);
        midiRange = new Vector2Int(60, 96);
        seconds = 0.8f;
        size = 1f;
        prewarm = 12;
    }

    public override IFeatureEmitter CreateEmitter(FeatureVisualContext context) => new DropletEmitter(this, context);
}

public sealed class DropletEmitter : IFeatureEmitter
{
    const int Segments = 28;

    sealed class Ring
    {
        public LineRenderer Line;
        public float Radius;
        public float Delay;
        public float Strength;
        public float Age;
        public bool Active;
    }

    readonly DropletStyle _style;
    readonly FeatureVisualContext _context;
    readonly List<Ring> _rings = new List<Ring>();
    int _side = 1;

    public DropletEmitter(DropletStyle style, FeatureVisualContext context)
    {
        _style = style;
        _context = context;
    }

    public void Emit(FeatureLoopNotes.Note note, float sizeScale, int side)
    {
        int s = FeatureVisualContext.NextSide(ref _side, side);
        float r = _style.radius * sizeScale;
        float inner = _context.ArenaRadius + 0.4f + r;
        float outer = Mathf.Max(inner + 0.05f, _context.HalfWidth - r - 0.1f);
        float y = _style.PitchToY(_context, note.Midi);
        int voices = Mathf.Max(1, note.Voices);
        for (int v = 0; v < voices; v++)
        {
            var centre = new Vector3(s * Random.Range(inner, outer), y + Random.Range(-0.25f, 0.25f) - v * 0.35f, 0f);
            Drop(centre, r, 0f, 1f);
            Drop(centre, r * 0.75f, _style.echoDelay * _style.seconds, 0.5f);
        }
    }

    void Drop(Vector3 centre, float radius, float delay, float strength)
    {
        var ring = Take();
        ring.Radius = radius;
        ring.Delay = delay;
        ring.Age = 0f;
        ring.Active = true;
        ring.Line.transform.localPosition = centre;
        ring.Line.transform.localScale = Vector3.zero;
        ring.Strength = strength;
        ring.Line.gameObject.SetActive(true);
    }

    public void Tick(float deltaTime)
    {
        foreach (var ring in _rings)
        {
            if (!ring.Active)
                continue;
            ring.Age += deltaTime;
            float t = (ring.Age - ring.Delay) / _style.seconds;
            if (t >= 1f)
            {
                Hide(ring);
                continue;
            }
            if (t < 0f)
                continue;
            float r = ring.Radius * (1f - (1f - t) * (1f - t));
            ring.Line.transform.localScale = new Vector3(r, r, 1f);
            ring.Line.startWidth = ring.Line.endWidth = _style.lineWidth * ring.Strength * (1f - 0.6f * t);
            var c = _style.color;
            c.a *= (1f - t) * ring.Strength;
            ring.Line.startColor = ring.Line.endColor = c;
        }
    }

    public void Prewarm(int count)
    {
        while (_rings.Count < count)
            Take().Active = true;
        Clear();
    }

    public void Clear()
    {
        foreach (var ring in _rings)
            Hide(ring);
    }

    Ring Take()
    {
        foreach (var ring in _rings)
            if (!ring.Active)
                return ring;
        var line = new GameObject("Feature Droplet").AddComponent<LineRenderer>();
        line.transform.SetParent(_context.Root, false);
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = Segments;
        for (int i = 0; i < Segments; i++)
        {
            float a = i * (2f * Mathf.PI / Segments);
            line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        }
        line.sharedMaterial = _context.Material;
        line.sortingOrder = _context.SortingOrder;
        var created = new Ring { Line = line };
        _rings.Add(created);
        return created;
    }

    static void Hide(Ring ring)
    {
        ring.Active = false;
        ring.Line.gameObject.SetActive(false);
    }
}
