using Unity.Profiling;

/// <summary>
/// Profiler markers for the music visuals, so their cost shows up by name in the Profiler (and in
/// ProfilerRecorder) instead of being lumped into BehaviourUpdate. Near zero cost when not profiling.
/// </summary>
static class VisualProfiler
{
    public static readonly ProfilerMarker ReactiveLate = new ProfilerMarker("Visuals.MusicReactive.LateUpdate");
    public static readonly ProfilerMarker StyleUpdate = new ProfilerMarker("Visuals.BedStyle.Update");
    public static readonly ProfilerMarker StyleLate = new ProfilerMarker("Visuals.BedStyle.LateUpdate");
    public static readonly ProfilerMarker LayerUpdate = new ProfilerMarker("Visuals.BedLayer.Update");
    public static readonly ProfilerMarker LayerLate = new ProfilerMarker("Visuals.BedLayer.LateUpdate");
    public static readonly ProfilerMarker TensionUpdate = new ProfilerMarker("Visuals.Tension.Update");
    public static readonly ProfilerMarker TensionLate = new ProfilerMarker("Visuals.Tension.LateUpdate");
    public static readonly ProfilerMarker FeatureUpdate = new ProfilerMarker("Visuals.FeatureLoop.Update");
}
