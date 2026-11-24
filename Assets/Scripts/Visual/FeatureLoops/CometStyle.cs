using System.Collections.Generic;
using UnityEngine;

/// <summary>Trumpet look: a comet streaks across the whole screen behind the arena; a stab fans out into several.</summary>
[CreateAssetMenu(menuName = "Chord Pulse/Feature Visuals/Comet Style")]
public sealed class CometStyle : FeatureVisualStyle
{
    [Tooltip("Comet size when several notes are stacked on the beat (a brass stab).")]
    public float chordSize = 0.32f;
    [Tooltip("How far the path bows upwards in the middle.")]
    public float arc = 0.5f;
    [Tooltip("Rise from the start side to the end side.")]
    public float slant = 0.6f;
    [Tooltip("Vertical gap between the comets of one stab.")]
    public float voiceSpread = 0.45f;
    public float trailTime = 0.35f;
    [Tooltip("Ignore the colour's hue and step round the rainbow comet by comet (fever).")]
    public bool rainbow;

    // Plain field defaults (no Unity API), so new assets and CreateInstance both start from the original look.
    public CometStyle()
    {
        color = new Color(1f, 0.9f, 0.62f, 0.75f);
        seconds = 1.1f;
        size = 0.24f;
    }

    public override IFeatureEmitter CreateEmitter(FeatureVisualContext context) => new CometEmitter(this, context);
}

public sealed class CometEmitter : IFeatureEmitter
{
    sealed class Comet
    {
        public Transform Root;
        public SpriteRenderer Head;
        public TrailRenderer Trail;
        public Vector3 From;
        public Vector3 To;
        public float Age;
        public bool Active;
    }

    readonly CometStyle _style;
    readonly FeatureVisualContext _context;
    readonly List<Comet> _comets = new List<Comet>();
    int _side = 1;
    float _hue;

    public CometEmitter(CometStyle style, FeatureVisualContext context)
    {
        _style = style;
        _context = context;
    }

    public void Emit(FeatureLoopNotes.Note note, float sizeScale, int side)
    {
        float edge = _context.HalfWidth + 1f;
        float y = _style.PitchToY(_context, note.Midi);
        float size = (note.Voices > 1 ? _style.chordSize : _style.size) * sizeScale;
        // Alternate the start side note by note so a run criss-crosses the screen (unless the loop pins it).
        int from = FeatureVisualContext.NextSide(ref _side, side);
        for (int v = 0; v < note.Voices; v++)
        {
            float offset = (v - (note.Voices - 1) * 0.5f) * _style.voiceSpread;
            var comet = Take();
            comet.From = new Vector3(from * edge, y + offset - _style.slant, 0f);
            comet.To = new Vector3(-from * edge, y + offset + _style.slant, 0f);
            Show(comet, size, NextColor());
        }
    }

    Color NextColor()
    {
        if (!_style.rainbow)
            return _style.color;
        _hue = Mathf.Repeat(_hue + 0.13f, 1f);
        var c = Color.HSVToRGB(_hue, 0.45f, 1f);
        c.a = _style.color.a;
        return c;
    }

    public void Tick(float deltaTime)
    {
        foreach (var comet in _comets)
        {
            if (!comet.Active)
                continue;
            comet.Age += deltaTime;
            float t = comet.Age / _style.seconds;
            if (t >= 1f)
            {
                Hide(comet);
                continue;
            }
            Vector3 pos = Vector3.Lerp(comet.From, comet.To, t);
            pos.y += Mathf.Sin(t * Mathf.PI) * _style.arc;
            comet.Root.localPosition = pos;
            var c = comet.Head.color;
            c.a = _style.color.a * Mathf.Clamp01((1f - t) * 4f);
            comet.Head.color = c;
        }
    }

    public void Clear()
    {
        foreach (var comet in _comets)
            Hide(comet);
    }

    Comet Take()
    {
        foreach (var comet in _comets)
            if (!comet.Active)
                return comet;

        var root = new GameObject("Feature Comet").transform;
        root.SetParent(_context.Root, false);
        var created = new Comet { Root = root };
        created.Head = root.gameObject.AddComponent<SpriteRenderer>();
        created.Head.sprite = _context.Dot;
        created.Head.sortingOrder = _context.SortingOrder;
        created.Trail = root.gameObject.AddComponent<TrailRenderer>();
        created.Trail.sharedMaterial = _context.Material;
        created.Trail.sortingOrder = _context.SortingOrder;
        created.Trail.minVertexDistance = 0.05f;
        created.Trail.numCapVertices = 2;
        _comets.Add(created);
        return created;
    }

    void Show(Comet comet, float size, Color color)
    {
        comet.Age = 0f;
        comet.Active = true;
        comet.Root.localPosition = comet.From;
        comet.Root.localScale = Vector3.one * size;
        comet.Root.gameObject.SetActive(true);
        comet.Head.color = color;
        comet.Trail.Clear();
        comet.Trail.time = _style.trailTime;
        comet.Trail.widthMultiplier = size * 0.6f;
        var fade = color;
        fade.a = 0f;
        comet.Trail.startColor = color;
        comet.Trail.endColor = fade;
    }

    static void Hide(Comet comet)
    {
        comet.Active = false;
        comet.Trail.Clear();
        comet.Root.gameObject.SetActive(false);
    }
}
