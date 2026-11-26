using UnityEngine;

/// <summary>
/// Tunable look of one kind of feature-loop visual (comet, bird, ...). The asset holds the values;
/// <see cref="CreateEmitter"/> builds the runtime object that draws with them.
/// </summary>
public abstract class FeatureVisualStyle : ScriptableObject
{
    public Color color = Color.white;
    [Tooltip("MIDI range mapped from the bottom to the top of the screen.")]
    public Vector2Int midiRange = new Vector2Int(62, 98);
    [Min(0.05f)] public float seconds = 1f;
    [Min(0.01f)] public float size = 0.25f;
    [Tooltip("Pooled objects created when the game starts (about the most that are on screen at once).")]
    [Min(0)] public int prewarm = 8;

    public abstract IFeatureEmitter CreateEmitter(FeatureVisualContext context);

    /// <summary>Screen height for a pitch: low notes near the bottom, high notes near the top.</summary>
    public float PitchToY(FeatureVisualContext context, int midi)
    {
        float pitch = Mathf.InverseLerp(midiRange.x, midiRange.y, midi);
        return Mathf.Lerp(-context.HalfHeight * 0.75f, context.HalfHeight * 0.85f, pitch);
    }
}
