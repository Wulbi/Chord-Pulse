using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Guitar look: each power-chord hit sends a bright band up from the bottom edge of the screen.
/// Hits on the phrase's last beat (the push into the next phrase) are bigger. Pitch is not used.
/// </summary>
[CreateAssetMenu(menuName = "Chord Pulse/Feature Visuals/Shock Style")]
public sealed class ShockStyle : FeatureVisualStyle
{
    [Tooltip("Hits from this phrase beat on are the big ones (beat 15 = the phrase's last beat).")]
    public float bigFromBeat = 15f;
    public float bigScale = 1.8f;
    [Tooltip("How far the band climbs from the bottom edge.")]
    public float rise = 0.7f;
    public float bandWidth = 0.12f;

    public ShockStyle()
    {
        color = new Color(1f, 0.58f, 0.3f, 0.8f);
        seconds = 0.4f;
        prewarm = 6;
        size = 1f;
    }

    public override IFeatureEmitter CreateEmitter(FeatureVisualContext context) => new ShockEmitter(this, context);
}

public sealed class ShockEmitter : IFeatureEmitter
{
    sealed class Band
    {
        public LineRenderer Line;
        public float Scale;
        public float Age;
        public bool Active;
    }

    readonly ShockStyle _style;
    readonly FeatureVisualContext _context;
    readonly List<Band> _bands = new List<Band>();

    public ShockEmitter(ShockStyle style, FeatureVisualContext context)
    {
        _style = style;
        _context = context;
    }

    public void Emit(FeatureLoopNotes.Note note, float sizeScale, int side)
    {
        var band = Take();
        band.Age = 0f;
        band.Active = true;
        band.Scale = _style.size * sizeScale * (note.Beat >= _style.bigFromBeat ? _style.bigScale : 1f);
        float x = _context.HalfWidth + 0.5f;
        band.Line.SetPosition(0, new Vector3(-x, 0f, 0f));
        band.Line.SetPosition(1, new Vector3(x, 0f, 0f));
        band.Line.gameObject.SetActive(true);
        Apply(band, 0f);
    }

    public void Tick(float deltaTime)
    {
        foreach (var band in _bands)
        {
            if (!band.Active)
                continue;
            band.Age += deltaTime;
            float t = band.Age / _style.seconds;
            if (t >= 1f)
            {
                Hide(band);
                continue;
            }
            Apply(band, t);
        }
    }

    void Apply(Band band, float t)
    {
        float eased = 1f - (1f - t) * (1f - t);
        float y = -_context.HalfHeight + _style.rise * band.Scale * eased;
        band.Line.transform.localPosition = new Vector3(0f, y, 0f);
        band.Line.startWidth = band.Line.endWidth = _style.bandWidth * band.Scale * (1f - 0.7f * t);
        var c = _style.color;
        c.a *= 1f - t;
        band.Line.startColor = band.Line.endColor = c;
    }

    public void Prewarm(int count)
    {
        while (_bands.Count < count)
            Take().Active = true;
        Clear();
    }

    public void Clear()
    {
        foreach (var band in _bands)
            Hide(band);
    }

    Band Take()
    {
        foreach (var band in _bands)
            if (!band.Active)
                return band;
        var go = new GameObject("Feature Shock");
        go.transform.SetParent(_context.Root, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = 2;
        line.sharedMaterial = _context.Material;
        line.sortingOrder = _context.SortingOrder;
        var created = new Band { Line = line };
        _bands.Add(created);
        return created;
    }

    static void Hide(Band band)
    {
        band.Active = false;
        band.Line.gameObject.SetActive(false);
    }
}
