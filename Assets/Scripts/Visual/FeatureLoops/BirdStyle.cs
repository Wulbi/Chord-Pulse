using System.Collections.Generic;
using UnityEngine;

/// <summary>Whistle look: a small bird flutters up and outwards in the free band beside the arena.</summary>
[CreateAssetMenu(menuName = "Chord Pulse/Feature Visuals/Bird Style")]
public sealed class BirdStyle : FeatureVisualStyle
{
    public float arc = 0.4f;
    [Tooltip("The bird takes off this far below the note's pitch height...")]
    public float startBelow = 1.2f;
    [Tooltip("...and flies to this far above it.")]
    public float endAbove = 1.4f;
    [Tooltip("Sideways drift away from the arena.")]
    public float drift = 1.4f;
    [Tooltip("Wing strokes speed (radians per second).")]
    public float flapSpeed = 18f;
    public float wingWidth = 0.05f;

    // Plain field defaults (no Unity API), so new assets and CreateInstance both start from the original look.
    public BirdStyle()
    {
        color = new Color(0.72f, 1f, 0.95f, 0.8f);
        seconds = 1.4f;
        prewarm = 8;
        size = 0.28f;
    }

    public override IFeatureEmitter CreateEmitter(FeatureVisualContext context) => new BirdEmitter(this, context);
}

public sealed class BirdEmitter : IFeatureEmitter
{
    sealed class Bird
    {
        public Transform Root;
        public LineRenderer Wings;
        public Vector3 From;
        public Vector3 To;
        public float Span;
        public float Age;
        public bool Active;
    }

    readonly BirdStyle _style;
    readonly FeatureVisualContext _context;
    readonly List<Bird> _birds = new List<Bird>();
    int _side = 1;

    public BirdEmitter(BirdStyle style, FeatureVisualContext context)
    {
        _style = style;
        _context = context;
    }

    public void Emit(FeatureLoopNotes.Note note, float sizeScale, int side)
    {
        float y = _style.PitchToY(_context, note.Midi);
        float inner = _context.ArenaRadius + 0.8f;
        float outer = Mathf.Max(inner, Mathf.Min(_context.HalfWidth - 0.6f, _context.ArenaRadius + 2.2f));
        int s = FeatureVisualContext.NextSide(ref _side, side);
        float x = s * Random.Range(inner, outer);
        var bird = Take();
        bird.From = new Vector3(x, y - _style.startBelow, 0f);
        bird.To = new Vector3(x + s * _style.drift, y + _style.endAbove, 0f);
        bird.Span = _style.size * sizeScale;
        bird.Age = 0f;
        bird.Active = true;
        bird.Root.localPosition = bird.From;
        bird.Root.gameObject.SetActive(true);
        bird.Wings.startColor = bird.Wings.endColor = _style.color;
        bird.Wings.startWidth = bird.Wings.endWidth = _style.wingWidth;
    }

    public void Tick(float deltaTime)
    {
        foreach (var bird in _birds)
        {
            if (!bird.Active)
                continue;
            bird.Age += deltaTime;
            float t = bird.Age / _style.seconds;
            if (t >= 1f)
            {
                Hide(bird);
                continue;
            }
            Vector3 pos = Vector3.Lerp(bird.From, bird.To, t);
            pos.y += Mathf.Sin(t * Mathf.PI) * _style.arc;
            bird.Root.localPosition = pos;

            float flap = Mathf.Sin(bird.Age * _style.flapSpeed) * 0.5f + 0.5f;
            float span = bird.Span;
            bird.Wings.SetPosition(0, new Vector3(-span, span * (0.2f + 0.6f * flap), 0f));
            bird.Wings.SetPosition(1, Vector3.zero);
            bird.Wings.SetPosition(2, new Vector3(span, span * (0.2f + 0.6f * flap), 0f));
            var c = _style.color;
            c.a *= Mathf.Clamp01((1f - t) * 4f);
            bird.Wings.startColor = bird.Wings.endColor = c;
        }
    }

    public void Prewarm(int count)
    {
        while (_birds.Count < count)
            Take().Active = true;
        Clear();
    }

    public void Clear()
    {
        foreach (var bird in _birds)
            Hide(bird);
    }

    Bird Take()
    {
        foreach (var bird in _birds)
            if (!bird.Active)
                return bird;

        var root = new GameObject("Feature Bird").transform;
        root.SetParent(_context.Root, false);
        var created = new Bird { Root = root };
        created.Wings = root.gameObject.AddComponent<LineRenderer>();
        created.Wings.useWorldSpace = false;
        created.Wings.positionCount = 3;
        created.Wings.sharedMaterial = _context.Material;
        created.Wings.sortingOrder = _context.SortingOrder;
        created.Wings.numCapVertices = 2;
        _birds.Add(created);
        return created;
    }

    static void Hide(Bird bird)
    {
        bird.Active = false;
        bird.Root.gameObject.SetActive(false);
    }
}
