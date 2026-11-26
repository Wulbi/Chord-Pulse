using UnityEngine;

/// <summary>
/// Draws one visual style for feature-loop notes. <see cref="FeatureLoopVisuals"/> decides when a note fires;
/// an emitter only draws it, so it never needs to know about FMOD or beats.
/// </summary>
public interface IFeatureEmitter
{
    /// <param name="note">The note to draw.</param>
    /// <param name="sizeScale">Per-loop size multiplier (1 = the style's own size).</param>
    /// <param name="side">-1 left, +1 right, 0 = the emitter's own choice (usually alternating).</param>
    void Emit(FeatureLoopNotes.Note note, float sizeScale, int side);
    void Tick(float deltaTime);
    void Clear();
    /// <summary>Creates pooled objects up front, so a guest's first notes do not build GameObjects mid-song.</summary>
    void Prewarm(int count);
}

/// <summary>Everything an emitter may use from the scene. Shared by all emitters of one director.</summary>
public sealed class FeatureVisualContext
{
    public Camera Camera;
    public float ArenaRadius;
    public Material Material;
    /// <summary>Soft round sprite for heads and sparkles.</summary>
    public Sprite Dot;
    public Transform Root;
    public int SortingOrder;

    public float HalfHeight => Camera.orthographicSize;
    public float HalfWidth => Camera.orthographicSize * Camera.aspect;

    /// <summary>Picks the fixed side when one is given, otherwise flips the running one.</summary>
    public static int NextSide(ref int running, int fixedSide)
    {
        if (fixedSide != 0)
            return fixedSide;
        running = -running;
        return running;
    }
}
