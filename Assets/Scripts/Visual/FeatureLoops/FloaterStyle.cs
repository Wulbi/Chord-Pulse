using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drifting particles in the bands beside the arena. One emitter, several looks by its settings:
/// sax smoke (soft puffs rising, swelling and swaying), choir light orbs (slow, wide), clarinet swing (diamonds that
/// wiggle in swing time), flute breeze (thin streaks blown outwards), jingle snow (small diamonds falling).
/// Pitch sets the height a rising or drifting particle starts at; falling ones start at the top.
/// </summary>
[CreateAssetMenu(menuName = "Chord Pulse/Feature Visuals/Floater Style")]
public sealed class FloaterStyle : FeatureVisualStyle
{
    public enum Motion { Rise, Fall, DriftOut }
    public enum Shape { Soft, Diamond }

    public Motion motion = Motion.Rise;
    public Shape shape = Shape.Soft;
    [Tooltip("Units per second along the motion.")]
    public float speed = 1.2f;
    [Tooltip("Sideways sway (units) and its rate (radians per second).")]
    public float swayAmount = 0.2f;
    public float swaySpeed = 3f;
    [Tooltip("Size at the end of its life relative to the start (smoke swells, sparks shrink).")]
    public float endScale = 1f;
    [Tooltip("Stretch of the sprite: x across, y along (a breeze streak is long and thin).")]
    public Vector2 stretch = Vector2.one;
    [Tooltip("Particles per note, plus one per extra stacked voice.")]
    public int perNote = 1;
    public float spin;

    public FloaterStyle()
    {
        color = new Color(0.75f, 0.55f, 1f, 0.45f);
        seconds = 1.4f;
        size = 0.35f;
        prewarm = 16;
    }

    public override IFeatureEmitter CreateEmitter(FeatureVisualContext context) => new FloaterEmitter(this, context);
}

public sealed class FloaterEmitter : IFeatureEmitter
{
    sealed class Floater
    {
        public SpriteRenderer Renderer;
        public Vector3 Start;
        public Vector3 Direction;
        public float Size;
        public float SwayPhase;
        public float Age;
        public bool Active;
    }

    readonly FloaterStyle _style;
    readonly FeatureVisualContext _context;
    readonly List<Floater> _floaters = new List<Floater>();
    int _side = 1;

    public FloaterEmitter(FloaterStyle style, FeatureVisualContext context)
    {
        _style = style;
        _context = context;
    }

    public void Emit(FeatureLoopNotes.Note note, float sizeScale, int side)
    {
        int count = Mathf.Max(1, _style.perNote) + Mathf.Max(0, note.Voices - 1);
        float inner = _context.ArenaRadius + 0.5f;
        float outer = Mathf.Max(inner + 0.1f, _context.HalfWidth - 0.3f);
        for (int i = 0; i < count; i++)
        {
            int s = FeatureVisualContext.NextSide(ref _side, side);
            var f = Take();
            float x = s * Random.Range(inner, outer);
            float y = _style.PitchToY(_context, note.Midi);
            switch (_style.motion)
            {
                case FloaterStyle.Motion.Fall:
                    f.Start = new Vector3(x, _context.HalfHeight + 0.3f, 0f);
                    f.Direction = Vector3.down;
                    break;
                case FloaterStyle.Motion.DriftOut:
                    f.Start = new Vector3(s * inner, y + Random.Range(-0.3f, 0.3f), 0f);
                    f.Direction = new Vector3(s, 0.15f, 0f).normalized;
                    break;
                default:
                    f.Start = new Vector3(x, y - 0.8f, 0f);
                    f.Direction = Vector3.up;
                    break;
            }
            f.Size = _style.size * sizeScale * Random.Range(0.85f, 1.15f);
            f.SwayPhase = Random.value * 6.3f;
            f.Age = -i * 0.06f;   // a little stagger when several leave together
            f.Active = true;
            f.Renderer.sprite = _style.shape == FloaterStyle.Shape.Diamond ? _context.Square : _context.Dot;
            f.Renderer.transform.localPosition = f.Start;
            f.Renderer.transform.localRotation = Quaternion.Euler(0f, 0f, _style.shape == FloaterStyle.Shape.Diamond ? 45f : 0f);
            f.Renderer.color = Color.clear;
            f.Renderer.gameObject.SetActive(true);
        }
    }

    public void Tick(float deltaTime)
    {
        foreach (var f in _floaters)
        {
            if (!f.Active)
                continue;
            f.Age += deltaTime;
            if (f.Age < 0f)
                continue;
            float t = f.Age / _style.seconds;
            if (t >= 1f)
            {
                Hide(f);
                continue;
            }
            var side = new Vector3(-f.Direction.y, f.Direction.x, 0f);
            var pos = f.Start + f.Direction * (_style.speed * f.Age)
                      + side * (Mathf.Sin(f.Age * _style.swaySpeed + f.SwayPhase) * _style.swayAmount);
            var tr = f.Renderer.transform;
            tr.localPosition = pos;
            float scale = f.Size * Mathf.Lerp(1f, _style.endScale, t);
            // Diamonds are squares turned 45°; a streak lies along its motion.
            if (_style.motion == FloaterStyle.Motion.DriftOut)
                tr.localRotation = Quaternion.FromToRotation(Vector3.right, f.Direction);
            else if (_style.spin != 0f)
                tr.localRotation = Quaternion.Euler(0f, 0f, 45f + _style.spin * f.Age);
            tr.localScale = new Vector3(scale * _style.stretch.x, scale * _style.stretch.y, 1f);
            var c = _style.color;
            // Quick fade in, long fade out.
            c.a *= Mathf.Min(1f, t * 8f) * (1f - t);
            f.Renderer.color = c;
        }
    }

    public void Prewarm(int count)
    {
        while (_floaters.Count < count)
            Take().Active = true;
        Clear();
    }

    public void Clear()
    {
        foreach (var f in _floaters)
            Hide(f);
    }

    Floater Take()
    {
        foreach (var f in _floaters)
            if (!f.Active)
                return f;
        var go = new GameObject("Feature Floater");
        go.transform.SetParent(_context.Root, false);
        var created = new Floater { Renderer = go.AddComponent<SpriteRenderer>() };
        created.Renderer.sprite = _context.Dot;
        created.Renderer.sortingOrder = _context.SortingOrder;
        _floaters.Add(created);
        return created;
    }

    static void Hide(Floater f)
    {
        f.Active = false;
        f.Renderer.gameObject.SetActive(false);
    }
}
