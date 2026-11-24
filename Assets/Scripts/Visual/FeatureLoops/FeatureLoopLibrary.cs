using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which style draws which instrument, plus per-loop tuning. Loaded from Resources/FeatureLoopLibrary;
/// without that asset <see cref="CreateDefault"/> builds the same look in memory, so the game never depends on it.
/// </summary>
[CreateAssetMenu(menuName = "Chord Pulse/Feature Visuals/Feature Loop Library")]
public sealed class FeatureLoopLibrary : ScriptableObject
{
    public const string ResourcePath = "FeatureLoopLibrary";

    [Serializable]
    public struct VoiceDefault
    {
        public FeatureSelector.Instrument instrument;
        public FeatureVisualStyle style;
    }

    [Tooltip("Style per instrument. Instruments without an entry use the fallback style.")]
    public VoiceDefault[] voiceDefaults = Array.Empty<VoiceDefault>();
    [Tooltip("Style for any instrument that has no entry yet.")]
    public FeatureVisualStyle fallbackStyle;
    public FeatureLoopData[] loops = Array.Empty<FeatureLoopData>();
    [Tooltip("Drawn on the kicks of the fever phrase (no guest loop plays then). Empty = no fever visuals here.")]
    public FeatureVisualStyle feverStyle;

    public static FeatureLoopLibrary Load()
    {
        var library = Resources.Load<FeatureLoopLibrary>(ResourcePath);
        return library != null && library.fallbackStyle != null ? library : CreateDefault();
    }

    /// <summary>
    /// The built-in look (Docs/MusicVisualsPlan.md 3-1, 3-2): trumpet = comet, whistle = bird, mallets = chime,
    /// percussion = rim orbit, synth = edge ripple, guitar = shock, plus the loops that pin sides or sizes.
    /// The asset menu saves exactly this, so the in-memory default and the assets start out identical.
    /// </summary>
    public static FeatureLoopLibrary CreateDefault()
    {
        var comet = CreateTemp<CometStyle>("Comet Style");
        var bird = CreateTemp<BirdStyle>("Bird Style");
        var chime = CreateTemp<ChimeStyle>("Chime Style");
        var glock = CreateTemp<ChimeStyle>("Glock Chime Style");
        glock.color = new Color(0.72f, 0.88f, 1f, 0.9f);
        glock.size = 0.17f;
        var orbit = CreateTemp<RimOrbitStyle>("Rim Orbit Style");
        var ripple = CreateTemp<EdgeRippleStyle>("Edge Ripple Style");
        var shock = CreateTemp<ShockStyle>("Shock Style");
        var fever = CreateTemp<CometStyle>("Fever Comet Style");
        fever.rainbow = true;
        fever.color = new Color(1f, 1f, 1f, 0.85f);
        fever.seconds = 0.9f;
        fever.size = 0.28f;
        fever.chordSize = 0.34f;

        var library = CreateTemp<FeatureLoopLibrary>("Default Feature Loop Library");
        library.fallbackStyle = comet;
        library.feverStyle = fever;
        library.voiceDefaults = new[]
        {
            new VoiceDefault { instrument = FeatureSelector.Instrument.Trumpet, style = comet },
            new VoiceDefault { instrument = FeatureSelector.Instrument.Whistle, style = bird },
            new VoiceDefault { instrument = FeatureSelector.Instrument.Mallets, style = chime },
            new VoiceDefault { instrument = FeatureSelector.Instrument.Percussion, style = orbit },
            new VoiceDefault { instrument = FeatureSelector.Instrument.Synth, style = ripple },
            new VoiceDefault { instrument = FeatureSelector.Instrument.Guitar, style = shock },
        };

        var loops = new List<FeatureLoopData>();
        foreach (var loop in FeatureLoopNotes.Loops)
        {
            var data = CreateTemp<FeatureLoopData>($"{loop.Id:00} {loop.Name}");
            data.loopId = loop.Id;
            data.voices = DefaultVoices(loop.Id, glock);
            loops.Add(data);
        }
        library.loops = loops.ToArray();
        return library;
    }

    // Per-loop signatures that are only a side or size change (the rest comes from the styles themselves).
    static VoiceOverride[] DefaultVoices(int loopId, FeatureVisualStyle glock)
    {
        switch (loopId)
        {
            case 5: // Brass Solo: the trumpet leads, the whistle only drops a note into the gaps.
                return new[] { new VoiceOverride { instrument = FeatureSelector.Instrument.Whistle, sizeScale = 0.7f } };
            case 6: // Call and Response: the trumpet calls from the left, the whistle answers from the right.
                return new[]
                {
                    new VoiceOverride { instrument = FeatureSelector.Instrument.Trumpet, side = FeatureSide.Left },
                    new VoiceOverride { instrument = FeatureSelector.Instrument.Whistle, side = FeatureSide.Right },
                };
            case 9: // Glock Sparkle: smaller, icy chimes instead of the warm marimba ones.
                return new[] { new VoiceOverride { instrument = FeatureSelector.Instrument.Mallets, style = glock } };
            case 12: // Mallet Hook: the muted cowbell only keeps time under the xylophone tune.
                return new[] { new VoiceOverride { instrument = FeatureSelector.Instrument.Percussion, sizeScale = 0.5f } };
            default:
                return Array.Empty<VoiceOverride>();
        }
    }

    static T CreateTemp<T>(string name) where T : ScriptableObject
    {
        var obj = CreateInstance<T>();
        obj.name = name;
        obj.hideFlags = HideFlags.DontSave;
        return obj;
    }

    /// <summary>Frees the in-memory library from <see cref="CreateDefault"/>; a real asset is left alone.</summary>
    public void DestroyIfDefault()
    {
        if (hideFlags != HideFlags.DontSave)
            return;
        var styles = new HashSet<FeatureVisualStyle> { fallbackStyle, feverStyle };
        foreach (var voice in voiceDefaults)
            styles.Add(voice.style);
        foreach (var data in loops)
        {
            foreach (var voice in data.voices)
                styles.Add(voice.style);
            Destroy(data);
        }
        foreach (var style in styles)
            if (style != null)
                Destroy(style);
        Destroy(this);
    }

    public FeatureLoopData Find(int loopId)
    {
        foreach (var data in loops)
            if (data != null && data.loopId == loopId)
                return data;
        return null;
    }

    /// <summary>The loop's override for this instrument (default struct when there is none).</summary>
    public static VoiceOverride VoiceFor(FeatureLoopData loop, FeatureSelector.Instrument instrument)
    {
        if (loop != null)
            foreach (var voice in loop.voices)
                if (voice.instrument == instrument)
                    return voice;
        return default;
    }

    public FeatureVisualStyle StyleFor(FeatureLoopData loop, FeatureSelector.Instrument instrument)
    {
        var voice = VoiceFor(loop, instrument);
        if (voice.style != null)
            return voice.style;
        foreach (var entry in voiceDefaults)
            if (entry.instrument == instrument && entry.style != null)
                return entry.style;
        return fallbackStyle;
    }

    /// <summary>Arrival-cue colour: the loop's accent, or else the colour of its lead (lowest-flag) instrument.</summary>
    public Color AccentFor(FeatureSelector.Loop loop)
    {
        var data = Find(loop.Id);
        if (data != null && data.accent.a > 0f)
            return data.accent;
        int flags = (int)loop.Instruments;
        var lead = (FeatureSelector.Instrument)(flags & -flags);
        return StyleFor(data, lead).color;
    }
}
