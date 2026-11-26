using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Percussion look: every hit lights a dot one step further round a circle just outside the arena boundary,
/// so a groove becomes a dot walking round the rim and a fill becomes a fast spin. Louder hits (more voices
/// stacked on the beat) are bigger and brighter. Pitch is not used.
/// </summary>
[CreateAssetMenu(menuName = "Chord Pulse/Feature Visuals/Rim Orbit Style")]
public sealed class RimOrbitStyle : FeatureVisualStyle
{
    [Tooltip("Steps per full turn.")]
    public int steps = 16;
    [Tooltip("Gap between the arena boundary and the orbit.")]
    public float rimGap = 0.35f;

    public RimOrbitStyle()
    {
        color = new Color(1f, 0.7f, 0.5f, 0.85f);
        seconds = 0.5f;
        prewarm = 20;
        size = 0.16f;
    }

    public override IFeatureEmitter CreateEmitter(FeatureVisualContext context) => new RimOrbitEmitter(this, context);
}

public sealed class RimOrbitEmitter : IFeatureEmitter
{
    sealed class Dot
    {
        public SpriteRenderer Renderer;
        public float Size;
        public float Strength;
        public float Age;
        public bool Active;
    }

    readonly RimOrbitStyle _style;
    readonly FeatureVisualContext _context;
    readonly List<Dot> _dots = new List<Dot>();
    int _step;

    public RimOrbitEmitter(RimOrbitStyle style, FeatureVisualContext context)
    {
        _style = style;
        _context = context;
    }

    public void Emit(FeatureLoopNotes.Note note, float sizeScale, int side)
    {
        // Clockwise from the top; a pinned side mirrors the direction.
        int direction = side < 0 ? 1 : -1;
        _step++;
        float angle = Mathf.PI * 0.5f + direction * _step * (2f * Mathf.PI / Mathf.Max(4, _style.steps));
        float radius = _context.ArenaRadius + _style.rimGap;
        float strength = Mathf.Clamp01(note.Voices / 3f);

        var dot = Take();
        dot.Age = 0f;
        dot.Active = true;
        dot.Size = _style.size * sizeScale * (0.7f + 0.5f * strength);
        dot.Strength = 0.45f + 0.55f * strength;
        var t = dot.Renderer.transform;
        t.localPosition = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        t.gameObject.SetActive(true);
    }

    public void Tick(float deltaTime)
    {
        foreach (var dot in _dots)
        {
            if (!dot.Active)
                continue;
            dot.Age += deltaTime;
            float t = dot.Age / _style.seconds;
            if (t >= 1f)
            {
                Hide(dot);
                continue;
            }
            dot.Renderer.transform.localScale = Vector3.one * dot.Size * (1.3f - 0.5f * t);
            var c = _style.color;
            c.a *= dot.Strength * (1f - t);
            dot.Renderer.color = c;
        }
    }

    public void Prewarm(int count)
    {
        while (_dots.Count < count)
            Take().Active = true;
        Clear();
    }

    public void Clear()
    {
        _step = 0;
        foreach (var dot in _dots)
            Hide(dot);
    }

    Dot Take()
    {
        foreach (var dot in _dots)
            if (!dot.Active)
                return dot;
        var go = new GameObject("Feature Rim Dot");
        go.transform.SetParent(_context.Root, false);
        var created = new Dot { Renderer = go.AddComponent<SpriteRenderer>() };
        created.Renderer.sprite = _context.Dot;
        created.Renderer.sortingOrder = _context.SortingOrder;
        _dots.Add(created);
        return created;
    }

    static void Hide(Dot dot)
    {
        dot.Active = false;
        dot.Renderer.gameObject.SetActive(false);
    }
}
