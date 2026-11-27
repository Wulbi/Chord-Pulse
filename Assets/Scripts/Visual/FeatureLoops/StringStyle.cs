using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plucked strings that vibrate and ring out:
/// <list type="bullet">
/// <item>SideStrings (mbira): a few vertical strings in each band beside the arena; pitch picks the string,
///       higher notes ring in a higher mode.</item>
/// <item>Bottom (twang): one string along the bottom edge, snapped hard on each note.</item>
/// <item>TopScope (acid line): an oscilloscope trace along the top edge; each note sets how many waves run along it
///       (pitch) and how tall they are, and the waves travel like a resonant filter sweep.</item>
/// </list>
/// Strings only draw while they still vibrate, so an idle loop leaves nothing on screen.
/// </summary>
[CreateAssetMenu(menuName = "Chord Pulse/Feature Visuals/String Style")]
public sealed class StringStyle : FeatureVisualStyle
{
    public enum Layout { SideStrings, Bottom, TopScope }

    public Layout layout = Layout.SideStrings;
    [Tooltip("SideStrings: strings per side.")]
    public int strings = 5;
    [Tooltip("How fast a plucked string dies down (per second).")]
    public float decay = 4f;
    [Tooltip("Vibration speed (radians per second); TopScope: wave travel speed.")]
    public float vibrate = 40f;
    [Tooltip("Lowest and highest vibration mode (half-waves along the string), chosen by pitch.")]
    public Vector2Int modes = new Vector2Int(1, 3);
    public float lineWidth = 0.035f;

    public StringStyle()
    {
        color = new Color(1f, 0.85f, 0.6f, 0.85f);
        midiRange = new Vector2Int(60, 96);
        seconds = 1f;
        size = 0.25f;   // pluck amplitude
        prewarm = 0;    // strings are built when the emitter is
    }

    public override IFeatureEmitter CreateEmitter(FeatureVisualContext context) => new StringEmitter(this, context);
}

public sealed class StringEmitter : IFeatureEmitter
{
    const int Points = 28;

    sealed class Strand
    {
        public LineRenderer Line;
        public Vector3 A;
        public Vector3 B;
        public Vector3 Normal;
        public float Amplitude;
        public int Mode;
        public float Phase;
        public bool Shown;
    }

    readonly StringStyle _style;
    readonly FeatureVisualContext _context;
    readonly List<Strand> _strings = new List<Strand>();
    readonly Vector3[] _buffer = new Vector3[Points];
    float _time;
    int _side = 1;

    public StringEmitter(StringStyle style, FeatureVisualContext context)
    {
        _style = style;
        _context = context;
        Build();
    }

    void Build()
    {
        float h = _context.HalfHeight;
        float w = _context.HalfWidth;
        switch (_style.layout)
        {
            case StringStyle.Layout.Bottom:
                Add(new Vector3(-w - 0.2f, -h + 0.3f, 0f), new Vector3(w + 0.2f, -h + 0.3f, 0f));
                break;
            case StringStyle.Layout.TopScope:
                Add(new Vector3(-w - 0.2f, h - 0.35f, 0f), new Vector3(w + 0.2f, h - 0.35f, 0f));
                break;
            default:
                float inner = _context.ArenaRadius + 0.5f;
                float outer = Mathf.Max(inner + 0.2f, w - 0.25f);
                int n = Mathf.Max(1, _style.strings);
                for (int side = -1; side <= 1; side += 2)
                    for (int i = 0; i < n; i++)
                    {
                        float x = side * Mathf.Lerp(inner, outer, n == 1 ? 0.5f : i / (float)(n - 1));
                        Add(new Vector3(x, -h * 0.8f, 0f), new Vector3(x, h * 0.8f, 0f));
                    }
                break;
        }
    }

    void Add(Vector3 a, Vector3 b)
    {
        var line = new GameObject("Feature String").AddComponent<LineRenderer>();
        line.transform.SetParent(_context.Root, false);
        line.useWorldSpace = false;
        line.positionCount = Points;
        line.sharedMaterial = _context.Material;
        line.sortingOrder = _context.SortingOrder;
        line.numCapVertices = 2;
        line.gameObject.SetActive(false);
        var dir = (b - a).normalized;
        _strings.Add(new Strand { Line = line, A = a, B = b, Normal = new Vector3(-dir.y, dir.x, 0f) });
    }

    public void Emit(FeatureLoopNotes.Note note, float sizeScale, int side)
    {
        if (_strings.Count == 0)
            return;
        float pitch = Mathf.InverseLerp(_style.midiRange.x, _style.midiRange.y, note.Midi);
        Strand s;
        if (_style.layout == StringStyle.Layout.SideStrings)
        {
            int perSide = _strings.Count / 2;
            int sd = FeatureVisualContext.NextSide(ref _side, side);
            // Low notes on the outer strings, high notes nearer the arena (like a mbira's tines).
            int index = Mathf.Clamp(Mathf.RoundToInt((1f - pitch) * (perSide - 1)), 0, perSide - 1);
            s = _strings[(sd < 0 ? 0 : perSide) + index];
        }
        else
            s = _strings[0];
        s.Amplitude = Mathf.Max(s.Amplitude, _style.size * sizeScale * (1f + 0.3f * (note.Voices - 1)));
        s.Mode = Mathf.RoundToInt(Mathf.Lerp(_style.modes.x, _style.modes.y, pitch));
        s.Phase = Random.value * 6.3f;
        if (!s.Shown)
        {
            s.Shown = true;
            s.Line.gameObject.SetActive(true);
            s.Line.startWidth = s.Line.endWidth = _style.lineWidth;
        }
    }

    public void Tick(float deltaTime)
    {
        _time += deltaTime;
        bool scope = _style.layout == StringStyle.Layout.TopScope;
        foreach (var s in _strings)
        {
            if (!s.Shown)
                continue;
            s.Amplitude *= Mathf.Exp(-_style.decay * deltaTime);
            if (s.Amplitude < 0.004f)
            {
                s.Amplitude = 0f;
                s.Shown = false;
                s.Line.gameObject.SetActive(false);
                continue;
            }
            for (int i = 0; i < Points; i++)
            {
                float u = i / (float)(Points - 1);
                float offset = scope
                    // Travelling waves under a hump, so the ends stay pinned.
                    ? Mathf.Sin(2f * Mathf.PI * (u * s.Mode - _time * _style.vibrate * 0.05f)) * Mathf.Sin(Mathf.PI * u)
                    // A standing wave in the string's mode, swinging back and forth.
                    : Mathf.Sin(Mathf.PI * u * s.Mode) * Mathf.Cos(_time * _style.vibrate + s.Phase);
                _buffer[i] = Vector3.Lerp(s.A, s.B, u) + s.Normal * (offset * s.Amplitude);
            }
            s.Line.SetPositions(_buffer);
            var c = _style.color;
            c.a *= Mathf.Clamp01(0.25f + s.Amplitude / Mathf.Max(0.01f, _style.size));
            s.Line.startColor = s.Line.endColor = c;
        }
    }

    // Strings are built with the emitter; nothing to add.
    public void Prewarm(int count) { }

    public void Clear()
    {
        foreach (var s in _strings)
        {
            s.Amplitude = 0f;
            s.Shown = false;
            s.Line.gameObject.SetActive(false);
        }
    }
}
