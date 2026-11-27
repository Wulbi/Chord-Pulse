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
        var looks = CreateLoopLooks();
        looks["glock"] = glock;

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
            data.voices = DefaultVoices(loop.Id, looks);
            loops.Add(data);
        }
        library.loops = loops.ToArray();
        return library;
    }

    // Looks for the guest loops whose instruments only share a family with the first ones (sax and horns are
    // "trumpet", flute / clarinet / choir "whistle", keys "mallets", chip / acid "synth", twang "guitar",
    // jingle / toy box "percussion"): each gets its own look (Docs/MusicVisualsPlan.md 3-5).
    static Dictionary<string, FeatureVisualStyle> CreateLoopLooks()
    {
        var smoke = CreateTemp<FloaterStyle>("Sax Smoke Style");
        smoke.color = new Color(0.72f, 0.55f, 1f, 0.42f);
        smoke.speed = 1.1f; smoke.swayAmount = 0.25f; smoke.swaySpeed = 2.6f; smoke.endScale = 2.4f;
        smoke.size = 0.38f; smoke.seconds = 1.6f;

        var swing = CreateTemp<FloaterStyle>("Clarinet Swing Style");
        swing.shape = FloaterStyle.Shape.Diamond;
        swing.color = new Color(1f, 0.78f, 0.55f, 0.8f);
        swing.speed = 0.9f; swing.swayAmount = 0.45f; swing.swaySpeed = 7f; swing.endScale = 0.6f;
        swing.size = 0.2f; swing.seconds = 1f;

        var breeze = CreateTemp<FloaterStyle>("Flute Breeze Style");
        breeze.motion = FloaterStyle.Motion.DriftOut;
        breeze.color = new Color(0.75f, 1f, 0.82f, 0.6f);
        breeze.speed = 3.2f; breeze.swayAmount = 0.08f; breeze.swaySpeed = 4f; breeze.endScale = 1.2f;
        breeze.stretch = new Vector2(4f, 0.35f); breeze.size = 0.3f; breeze.seconds = 0.9f;

        var orbs = CreateTemp<FloaterStyle>("Choir Orb Style");
        orbs.color = new Color(1f, 0.82f, 0.95f, 0.38f);
        orbs.speed = 0.7f; orbs.swayAmount = 0.15f; orbs.swaySpeed = 1.4f; orbs.endScale = 1.4f;
        orbs.size = 0.55f; orbs.seconds = 1.8f;

        var snow = CreateTemp<FloaterStyle>("Jingle Snow Style");
        snow.motion = FloaterStyle.Motion.Fall;
        snow.shape = FloaterStyle.Shape.Diamond;
        snow.color = new Color(0.9f, 0.97f, 1f, 0.9f);
        snow.speed = 1.8f; snow.swayAmount = 0.25f; snow.swaySpeed = 3f; snow.endScale = 0.7f;
        snow.size = 0.13f; snow.seconds = 1.3f; snow.perNote = 2; snow.spin = 180f; snow.prewarm = 24;

        var pixel = CreateTemp<GridPopStyle>("Chip Pixel Style");
        pixel.palette = new[]
        {
            new Color(0.45f, 1f, 0.55f, 0.85f), new Color(0.45f, 0.9f, 1f, 0.85f), new Color(0.92f, 1f, 0.85f, 0.85f),
        };

        var blocks = CreateTemp<GridPopStyle>("Toy Block Style");
        blocks.gravity = 9f; blocks.hop = 3.5f; blocks.blinkSteps = 0; blocks.seconds = 0.7f; blocks.cell = 0.3f;
        blocks.palette = new[]
        {
            new Color(1f, 0.7f, 0.4f, 0.9f), new Color(0.5f, 0.9f, 0.85f, 0.9f),
            new Color(1f, 0.6f, 0.8f, 0.9f), new Color(0.8f, 0.75f, 1f, 0.9f),
        };
        blocks.color = blocks.palette[0];

        var mbira = CreateTemp<StringStyle>("Mbira String Style");
        mbira.decay = 3f; mbira.vibrate = 45f; mbira.size = 0.18f;

        var twang = CreateTemp<StringStyle>("Twang String Style");
        twang.layout = StringStyle.Layout.Bottom;
        twang.color = new Color(1f, 0.6f, 0.3f, 0.9f);
        twang.midiRange = new Vector2Int(40, 76);
        twang.decay = 2.5f; twang.vibrate = 55f; twang.modes = new Vector2Int(2, 6); twang.size = 0.3f; twang.lineWidth = 0.05f;

        var scope = CreateTemp<StringStyle>("Acid Scope Style");
        scope.layout = StringStyle.Layout.TopScope;
        scope.color = new Color(0.6f, 1f, 0.35f, 0.85f);
        scope.midiRange = new Vector2Int(36, 72);
        scope.decay = 1.6f; scope.vibrate = 60f; scope.modes = new Vector2Int(3, 10); scope.size = 0.22f; scope.lineWidth = 0.04f;

        var beams = CreateTemp<BeamStyle>("Horn Spotlight Style");

        var keys = CreateTemp<BeamStyle>("FM Key Bar Style");
        keys.mode = BeamStyle.Mode.KeyBars;
        keys.color = new Color(0.85f, 0.9f, 1f, 0.75f);
        keys.seconds = 0.45f;

        var droplets = CreateTemp<DropletStyle>("Tine Droplet Style");

        return new Dictionary<string, FeatureVisualStyle>
        {
            ["smoke"] = smoke, ["swing"] = swing, ["breeze"] = breeze, ["orbs"] = orbs, ["snow"] = snow,
            ["pixel"] = pixel, ["blocks"] = blocks, ["mbira"] = mbira, ["twang"] = twang, ["scope"] = scope,
            ["beams"] = beams, ["keys"] = keys, ["droplets"] = droplets,
        };
    }

    static VoiceOverride Look(FeatureSelector.Instrument instrument, FeatureVisualStyle style, float sizeScale = 0f) =>
        new VoiceOverride { instrument = instrument, style = style, sizeScale = sizeScale };

    // Per-loop signatures: a side or size change, or a look of its own (see CreateLoopLooks).
    static VoiceOverride[] DefaultVoices(int loopId, Dictionary<string, FeatureVisualStyle> looks)
    {
        const FeatureSelector.Instrument Brass = FeatureSelector.Instrument.Trumpet;
        const FeatureSelector.Instrument Air = FeatureSelector.Instrument.Whistle;
        const FeatureSelector.Instrument Keys = FeatureSelector.Instrument.Mallets;
        const FeatureSelector.Instrument Perc = FeatureSelector.Instrument.Percussion;
        const FeatureSelector.Instrument Synth = FeatureSelector.Instrument.Synth;
        const FeatureSelector.Instrument Guitar = FeatureSelector.Instrument.Guitar;
        // Fever guests: the same family looks, bigger and bolder over the fever phrase.
        const float FeverScale = 1.35f;
        switch (loopId)
        {
            case 5: // Brass Solo: the trumpet leads, the whistle only drops a note into the gaps.
                return new[] { new VoiceOverride { instrument = Air, sizeScale = 0.7f } };
            case 6: // Call and Response: the trumpet calls from the left, the whistle answers from the right.
                return new[]
                {
                    new VoiceOverride { instrument = Brass, side = FeatureSide.Left },
                    new VoiceOverride { instrument = Air, side = FeatureSide.Right },
                };
            case 9: // Glock Sparkle: smaller, icy chimes instead of the warm marimba ones.
                return new[] { Look(Keys, looks["glock"]) };
            case 12: // Mallet Hook: the muted cowbell only keeps time under the xylophone tune.
                return new[] { new VoiceOverride { instrument = Perc, sizeScale = 0.5f } };
            case 13: return new[] { Look(Brass, null, FeverScale) };          // Fever Fanfare
            case 14: return new[] { Look(Air, null, FeverScale) };            // Fever Whistle
            case 15: return new[] { Look(Keys, looks["glock"], FeverScale) }; // Fever Glock Shower
            case 16: return new[] { Look(Synth, null, FeverScale) };          // Fever Scream
            case 17: return new[] { Look(Guitar, null, FeverScale) };         // Fever Power Drive
            case 18: return new[] { Look(Perc, null, FeverScale) };           // Fever Carnival
            case 19: return new[] { Look(Brass, looks["smoke"]) };            // Sax Groove: smoke curling up
            case 20: return new[] { Look(Air, looks["swing"]) };              // Clarinet Swing: diamonds wiggling in swing
            case 21: return new[] { Look(Air, looks["breeze"]) };             // Flute Breeze: streaks blown outwards
            case 22: return new[] { Look(Brass, looks["beams"]) };            // Horn Section: stage spotlights
            case 23: return new[] { Look(Keys, looks["keys"]) };              // FM Piano Stabs: piano-roll bars
            case 24: return new[] { Look(Synth, looks["pixel"]) };            // Chip Quest: 8-bit pixels
            case 25: return new[] { Look(Air, looks["orbs"]) };               // La La Choir: soft light orbs
            case 26: return new[] { Look(Keys, looks["droplets"]) };          // Tine Keys: raindrops on water
            case 27: return new[] { Look(Keys, looks["mbira"]) };             // Mbira Ripple: plucked tines
            case 28: return new[] { Look(Perc, looks["snow"]) };              // Jingle Sparkle: snowflakes
            case 29: return new[] { Look(Perc, looks["blocks"]) };            // Toy Box: bouncing blocks
            case 30: return new[] { Look(Synth, looks["scope"]) };            // Acid Line: oscilloscope trace
            case 31: return new[] { Look(Guitar, looks["twang"]) };           // Twang Lick: snapped string
            case 32: return new[] { Look(Brass, looks["smoke"], 1.5f) };      // Fever Sax Blast
            case 33: return new[] { Look(Synth, looks["pixel"], FeverScale) };// Fever Chip Rush
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
