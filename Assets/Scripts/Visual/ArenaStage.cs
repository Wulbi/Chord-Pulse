using UnityEngine;

/// <summary>
/// Shared state of the arena boundary ring and the background (Docs/MusicVisualsPlan.md 4-3).
/// Several visual components want to change them at once; writing the renderers directly would make them
/// overwrite each other. Instead:
/// 1. contributors write <b>requests</b> in <c>Update</c> (each axis owns different fields, see 2-0 of the plan),
/// 2. <see cref="MusicReactiveVisuals"/> merges them with its own beat pulse in <c>LateUpdate</c>, applies the
///    result once and clears the requests,
/// 3. overlays that draw on top of the ring (style shapes, combo layers) read the <b>result</b> in a later
///    <c>LateUpdate</c> (<c>DefaultExecutionOrder</c> above 0), so they move with the pulse in the same frame.
/// </summary>
public sealed class ArenaStage
{
    // ---- Requests (cleared every frame after they are applied) ----

    /// <summary>Added to the ring scale (0.02 = 2% bigger). Hurt shrinks it, pattern variation breathes it.</summary>
    public float RingRadiusOffset;
    /// <summary>Added to the ring line width (energy makes it thicker).</summary>
    public float RingWidthAdd;
    /// <summary>Multiplies the base line's opacity; style overlays fade it when they draw their own ring shape.</summary>
    public float RingAlpha = 1f;
    /// <summary>Radius noise amplitude as a fraction of the radius (acid bass under high tension).</summary>
    public float RingWobble;
    /// <summary>Background palette the bed style leans towards, and how far (0..1).</summary>
    public Color BackgroundTint;
    public float BackgroundTintAmount;
    /// <summary>0..1: drains colour from the bed visuals (last life: the bed is low-passed).</summary>
    public float Desaturate;
    /// <summary>0..1: darkens the bed visuals.</summary>
    public float Dim;

    // ---- Result of this frame (set by MusicReactiveVisuals) ----

    /// <summary>Arena radius before scaling.</summary>
    public float BaseRadius;
    /// <summary>Final ring scale (beat pulse, build-up squeeze and requests applied).</summary>
    public float RingScale = 1f;
    public float RingWidth;
    public Color RingColor;
    /// <summary>0..1 strength of the beat pulse right now (kick or snare), for overlays that pulse along.</summary>
    public float BeatImpact;
    /// <summary>How much the bed visuals are drained this frame (Desaturate / Dim as applied).</summary>
    public float AppliedDesaturate;
    public float AppliedDim;

    public float RingRadius => BaseRadius * RingScale;

    public void ClearRequests()
    {
        RingRadiusOffset = 0f;
        RingWidthAdd = 0f;
        RingAlpha = 1f;
        RingWobble = 0f;
        BackgroundTintAmount = 0f;
        Desaturate = 0f;
        Dim = 0f;
    }

    /// <summary>Applies this frame's drain (danger) to a bed colour, keeping its alpha.</summary>
    public Color Drain(Color color)
    {
        float gray = color.r * 0.3f + color.g * 0.59f + color.b * 0.11f;
        var drained = Color.Lerp(color, new Color(gray, gray, gray, color.a), AppliedDesaturate);
        float keep = 1f - AppliedDim;
        return new Color(drained.r * keep, drained.g * keep, drained.b * keep, color.a);
    }
}
