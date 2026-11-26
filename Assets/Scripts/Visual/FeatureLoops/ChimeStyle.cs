using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mallet look (marimba, xylophone, glockenspiel): a small star pops in the band beside the arena.
/// Height follows pitch and higher notes are smaller. Each bar uses one side and each beat sweeps outwards,
/// so a 16th-note arpeggio draws its own up-and-down wave.
/// </summary>
[CreateAssetMenu(menuName = "Chord Pulse/Feature Visuals/Chime Style")]
public sealed class ChimeStyle : FeatureVisualStyle
{
    [Tooltip("Size of the highest note relative to the lowest.")]
    public float highNoteScale = 0.6f;
    [Tooltip("Accented (stacked) notes are this much bigger and whiter.")]
    public float accentScale = 1.6f;

    public ChimeStyle()
    {
        color = new Color(1f, 0.8f, 0.5f, 0.85f);
        midiRange = new Vector2Int(79, 100);
        seconds = 0.45f;
        prewarm = 20;
        size = 0.22f;
    }

    public override IFeatureEmitter CreateEmitter(FeatureVisualContext context) => new ChimeEmitter(this, context);
}

public sealed class ChimeEmitter : IFeatureEmitter
{
    sealed class Chime
    {
        public SpriteRenderer Renderer;
        public Color Color;
        public float Size;
        public float Age;
        public bool Active;
    }

    readonly ChimeStyle _style;
    readonly FeatureVisualContext _context;
    readonly List<Chime> _chimes = new List<Chime>();

    public ChimeEmitter(ChimeStyle style, FeatureVisualContext context)
    {
        _style = style;
        _context = context;
    }

    public void Emit(FeatureLoopNotes.Note note, float sizeScale, int side)
    {
        int bar = Mathf.FloorToInt(note.Beat / SoundManager.BeatsPerBar);
        int s = side != 0 ? side : (bar % 2 == 0 ? -1 : 1);
        float inner = _context.ArenaRadius + 0.6f;
        float outer = Mathf.Max(inner, Mathf.Min(_context.HalfWidth - 0.4f, _context.ArenaRadius + 2.6f));
        float withinBeat = note.Beat - Mathf.Floor(note.Beat);
        float x = s * Mathf.Lerp(inner, outer, withinBeat);
        float pitch = Mathf.InverseLerp(_style.midiRange.x, _style.midiRange.y, note.Midi);
        bool accent = note.Voices > 1;

        var chime = Take();
        chime.Age = 0f;
        chime.Active = true;
        chime.Size = _style.size * sizeScale * Mathf.Lerp(1f, _style.highNoteScale, pitch) * (accent ? _style.accentScale : 1f);
        chime.Color = accent ? Color.Lerp(_style.color, Color.white, 0.6f) : _style.color;
        var t = chime.Renderer.transform;
        t.localPosition = new Vector3(x, _style.PitchToY(_context, note.Midi), 0f);
        t.gameObject.SetActive(true);
    }

    public void Tick(float deltaTime)
    {
        foreach (var chime in _chimes)
        {
            if (!chime.Active)
                continue;
            chime.Age += deltaTime;
            float t = chime.Age / _style.seconds;
            if (t >= 1f)
            {
                Hide(chime);
                continue;
            }
            // Struck: full size almost at once, then a slow shrink and fade like a ringing bar.
            float pop = Mathf.Min(1f, t * 8f);
            chime.Renderer.transform.localScale = Vector3.one * chime.Size * pop * (1.2f - 0.5f * t);
            var c = chime.Color;
            c.a *= (1f - t) * (1f - t);
            chime.Renderer.color = c;
        }
    }

    public void Prewarm(int count)
    {
        while (_chimes.Count < count)
            Take().Active = true;
        Clear();
    }

    public void Clear()
    {
        foreach (var chime in _chimes)
            Hide(chime);
    }

    Chime Take()
    {
        foreach (var chime in _chimes)
            if (!chime.Active)
                return chime;
        var go = new GameObject("Feature Chime");
        go.transform.SetParent(_context.Root, false);
        var created = new Chime { Renderer = go.AddComponent<SpriteRenderer>() };
        created.Renderer.sprite = _context.Dot;
        created.Renderer.sortingOrder = _context.SortingOrder;
        _chimes.Add(created);
        return created;
    }

    static void Hide(Chime chime)
    {
        chime.Active = false;
        chime.Renderer.gameObject.SetActive(false);
    }
}
