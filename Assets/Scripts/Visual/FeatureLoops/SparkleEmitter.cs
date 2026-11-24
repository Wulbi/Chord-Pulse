using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The "a guest is coming" cue: points along both screen edges twinkle once, where the guest's visuals will
/// come from. Not tied to a note, so it is driven by <see cref="FeatureLoopVisuals"/> directly.
/// </summary>
public sealed class SparkleEmitter
{
    const int PerSide = 3;
    const float Seconds = 0.5f;
    const float Size = 0.18f;
    const float EdgeInset = 0.35f;

    sealed class Sparkle
    {
        public SpriteRenderer Renderer;
        public float Age;
        public bool Active;
    }

    readonly FeatureVisualContext _context;
    readonly List<Sparkle> _sparkles = new List<Sparkle>();

    public SparkleEmitter(FeatureVisualContext context)
    {
        _context = context;
    }

    public void Twinkle(Color color)
    {
        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < PerSide; i++)
            {
                var sparkle = Take();
                sparkle.Age = 0f;
                sparkle.Active = true;
                var t = sparkle.Renderer.transform;
                t.localPosition = new Vector3(side * (_context.HalfWidth - EdgeInset),
                    (i - (PerSide - 1) * 0.5f) * _context.HalfHeight * 0.45f, 0f);
                t.gameObject.SetActive(true);
                sparkle.Renderer.color = color;
            }
    }

    public void Tick(float deltaTime)
    {
        foreach (var sparkle in _sparkles)
        {
            if (!sparkle.Active)
                continue;
            sparkle.Age += deltaTime;
            float t = sparkle.Age / Seconds;
            if (t >= 1f)
            {
                Hide(sparkle);
                continue;
            }
            float s = Mathf.Sin(t * Mathf.PI);
            sparkle.Renderer.transform.localScale = Vector3.one * Size * (0.5f + s);
            var c = sparkle.Renderer.color;
            c.a = s;
            sparkle.Renderer.color = c;
        }
    }

    public void Clear()
    {
        foreach (var sparkle in _sparkles)
            Hide(sparkle);
    }

    Sparkle Take()
    {
        foreach (var sparkle in _sparkles)
            if (!sparkle.Active)
                return sparkle;

        var go = new GameObject("Feature Sparkle");
        go.transform.SetParent(_context.Root, false);
        var created = new Sparkle { Renderer = go.AddComponent<SpriteRenderer>() };
        created.Renderer.sprite = _context.Dot;
        created.Renderer.sortingOrder = _context.SortingOrder;
        _sparkles.Add(created);
        return created;
    }

    static void Hide(Sparkle sparkle)
    {
        sparkle.Active = false;
        sparkle.Renderer.gameObject.SetActive(false);
    }
}
