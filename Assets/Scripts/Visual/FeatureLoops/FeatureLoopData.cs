using System;
using UnityEngine;

/// <summary>
/// Visual tuning of one feature loop. Audio facts (bars, instruments, notes) come from the generated
/// <see cref="FeatureLoopNotes"/>; this asset only says how the loop looks (Docs/FeatureLoopVisuals.md 3-2).
/// </summary>
[CreateAssetMenu(menuName = "Chord Pulse/Feature Visuals/Feature Loop Data")]
public sealed class FeatureLoopData : ScriptableObject
{
    [Tooltip("FeatureLoop id (FeatureLoopNotes.Loops).")]
    public int loopId;
    [Tooltip("Colour of the arrival cue. Alpha 0 = use the colour of the loop's lead instrument style.")]
    public Color accent = Color.clear;
    [Tooltip("Per-instrument style for this loop only. Instruments not listed use the library default.")]
    public VoiceOverride[] voices = Array.Empty<VoiceOverride>();
}

public enum FeatureSide { Auto = 0, Left = -1, Right = 1 }

[Serializable]
public struct VoiceOverride
{
    public FeatureSelector.Instrument instrument;
    [Tooltip("Empty = keep the library's style for this instrument.")]
    public FeatureVisualStyle style;
    [Tooltip("0 or 1 = normal size.")]
    public float sizeScale;
    [Tooltip("Auto = the style alternates sides itself.")]
    public FeatureSide side;

    public float SizeScale => sizeScale > 0f ? sizeScale : 1f;
}
