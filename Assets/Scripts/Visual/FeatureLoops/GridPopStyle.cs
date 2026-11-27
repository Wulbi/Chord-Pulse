using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hard-edged squares snapped to a grid in the bands beside the arena: chip tunes as 8-bit pixels (they appear
/// whole and blink out in steps, no smooth fade) or the toy box as little blocks that pop up and bounce.
/// Height follows pitch (snapped to the grid); each 16th moves one column outwards, so a run reads like a sprite
/// walking. Colours cycle through the palette.
/// </summary>
[CreateAssetMenu(menuName = "Chord Pulse/Feature Visuals/Grid Pop Style")]
public sealed class GridPopStyle : FeatureVisualStyle
{
    [Tooltip("Grid cell size in world units.")]
    public float cell = 0.28f;
    [Tooltip("Colours used in turn (the style colour is the first).")]
    public Color[] palette = { };
    [Tooltip("Blocks hop up, then fall (0 = pixels stay in their cell).")]
    public float gravity;
    public float hop = 2.2f;
    [Tooltip("Visible steps of the blink-out (pixel look); 0 = smooth fade.")]
    public int blinkSteps = 3;

    public GridPopStyle()
    {
        color = new Color(0.45f, 1f, 0.55f, 0.85f);
        midiRange = new Vector2Int(60, 96);
        seconds = 0.5f;
        size = 1f;
        prewarm = 24;
    }

    public override IFeatureEmitter CreateEmitter(FeatureVisualContext context) => new GridPopEmitter(this, context);
}

public sealed class GridPopEmitter : IFeatureEmitter
{
    sealed class Block
    {
        public SpriteRenderer Renderer;
        public Vector3 Start;
        public Color Color;
        public float Size;
        public float Age;
        public bool Active;
    }

    readonly GridPopStyle _style;
    readonly FeatureVisualContext _context;
    readonly List<Block> _blocks = new List<Block>();
    int _colorIndex;

    public GridPopEmitter(GridPopStyle style, FeatureVisualContext context)
    {
        _style = style;
        _context = context;
    }

    public void Emit(FeatureLoopNotes.Note note, float sizeScale, int side)
    {
        float cell = _style.cell * sizeScale;
        float inner = _context.ArenaRadius + 0.5f;
        int columns = Mathf.Max(1, Mathf.FloorToInt((_context.HalfWidth - 0.2f - inner) / cell));
        // Each bar flips sides; each 16th steps one column outwards.
        int bar = Mathf.FloorToInt(note.Beat / SoundManager.BeatsPerBar);
        int s = side != 0 ? side : (bar % 2 == 0 ? -1 : 1);
        int column = Mathf.FloorToInt(note.Beat * 4f) % columns;
        float x = s * (inner + (column + 0.5f) * cell);
        float y = Mathf.Round(_style.PitchToY(_context, note.Midi) / cell) * cell;

        int count = Mathf.Max(1, note.Voices);
        for (int v = 0; v < count; v++)
        {
            var b = Take();
            b.Start = new Vector3(x, y - v * cell, 0f);
            b.Size = cell * 0.9f;
            b.Color = NextColor();
            b.Age = 0f;
            b.Active = true;
            var tr = b.Renderer.transform;
            tr.localPosition = b.Start;
            tr.localScale = new Vector3(b.Size, b.Size, 1f);
            b.Renderer.color = b.Color;
            b.Renderer.gameObject.SetActive(true);
        }
    }

    Color NextColor()
    {
        if (_style.palette == null || _style.palette.Length == 0)
            return _style.color;
        var c = _style.palette[_colorIndex % _style.palette.Length];
        _colorIndex++;
        return c;
    }

    public void Tick(float deltaTime)
    {
        foreach (var b in _blocks)
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
            var tr = b.Renderer.transform;
            if (_style.gravity > 0f)
            {
                // Hop up, fall back down, with a little squash on the way up.
                float y = _style.hop * b.Age - 0.5f * _style.gravity * b.Age * b.Age;
                tr.localPosition = b.Start + new Vector3(0f, Mathf.Max(-1.5f, y) * 0.35f, 0f);
                float squash = 1f + 0.25f * Mathf.Sin(Mathf.Min(1f, t * 3f) * Mathf.PI);
                tr.localScale = new Vector3(b.Size / squash, b.Size * squash, 1f);
            }
            float alpha = 1f - t;
            if (_style.blinkSteps > 0)
                alpha = Mathf.Ceil(alpha * _style.blinkSteps) / _style.blinkSteps;
            var c = b.Color;
            c.a *= alpha;
            b.Renderer.color = c;
        }
    }

    public void Prewarm(int count)
    {
        while (_blocks.Count < count)
            Take().Active = true;
        Clear();
    }

    public void Clear()
    {
        foreach (var b in _blocks)
            Hide(b);
    }

    Block Take()
    {
        foreach (var b in _blocks)
            if (!b.Active)
                return b;
        var go = new GameObject("Feature Grid Block");
        go.transform.SetParent(_context.Root, false);
        var created = new Block { Renderer = go.AddComponent<SpriteRenderer>() };
        created.Renderer.sprite = _context.Square;
        created.Renderer.sortingOrder = _context.SortingOrder;
        _blocks.Add(created);
        return created;
    }

    static void Hide(Block b)
    {
        b.Active = false;
        b.Renderer.gameObject.SetActive(false);
    }
}
