"""
Pattern data pipeline: phrase patterns (16 beats) of notes per layer, with the conditions under which each clip plays,
generated to C# for the PatternScheduler (Assets/Scripts/Audio/Generated/PatternTable.g.cs), and every note registered
with the instrument library (instruments.py LAYERS calls register(lib)).

    python -I FMOD/Tools/instruments.py              # writes instruments.js, InstrumentTable.g.cs and PatternTable.g.cs
    python -I FMOD/Tools/patterns.py                 # PatternTable.g.cs only

(The --compare-legacy check against the music event's timeline tracks was removed in stages 3-8 Task 15b together with
those tracks; its results are in Docs/Captures/README.md, Tasks 9-14.)

Data modules (PATTERN_LAYERS) define Inst / Layer / Clip / N; see pattern_bed.py, pattern_drums.py, pattern_feature.py and
pattern_fever.py.

Rules (same sound as the old generators):
- Volume: old note volume (target - rms + accent, clamped to [-60, 10]) = instrument event volume (NORMALIZE_DB - rms,
  clamped) + GainDb. A note whose old target - rms + accent was clamped is reported (warning) and GainDb carries the clamp.
- Onset: the scheduler starts every note InstrumentTable.OnsetSeconds early (also on a phrase's first beat).
- Length 0 (the drums): the note plays to the end of its sample; the old track's fader is folded into the note's dB.
- HoldSeconds (the feature loops' struck / staccato notes): the note sounds at least that long from its start, like the
  old placement's region of max(written length, audible time).
- ADSR: an instrument with the old track's attack / release is its own (instrument, bus) event (0 / 0 = the track had
  no ADSR: the note stops hard at its end like the timeline note).
- The old style / variation generators left the last 16th of the phrase empty (Unity switched those parameters just
  before the boundary); the combo hats and the 16th shaker always had a note there, and keep it (the one-shot path
  decides per step, without lead).
- Max Instances per (instrument, bus) = the most instances alive at once at 132 BPM + 4, over every arrangement state
  and the previous phrase's tails. An instance lives from its dispatch (a started, paused instance counts: the dispatch
  window before the beat, CORE_LOOKAHEAD_BEATS for core layers, DISPATCH_WINDOW_BEATS otherwise) to its stop + release
  (the backend stops it; at least its HoldSeconds), or for length 0 until the event's timeline ends: the end of its longest sound region (every
  note's region is its sample, at least EVENT_REGION_MIN_S, and all of them start at 0), whichever note plays. Counting
  only the sounding time let the Drums event steal its oldest instances, the pending core kicks (Task 12: in funk every
  hat instance lived 1.3 s, the claves region, and the kick never sounded).
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import samples  # noqa: E402

CS_PATH = os.path.join(HERE, "..", "..", "Assets", "Scripts", "Audio", "Generated", "PatternTable.g.cs")
NORMALIZE_DB = -27.0                   # keep equal to instruments.NORMALIZE_DB (instruments imports this module)
VOLUME_MIN, VOLUME_MAX = -60.0, 10.0   # old generators' note volume clamp, and the instrument event volume clamp
PHRASE_BEATS = 16
MAX_BPM = 132
ASSETS = os.path.join(HERE, "..", "ChordPulse", "Assets")
SPARE_INSTANCES = 4
# instruments.js makes every sound region at least this long; a length-0 instance runs until its timeline passes it.
EVENT_REGION_MIN_S = 0.5
# Dispatch windows assumed by max_instances() (how long a started, paused instance lives before its beat). This is the
# OLD rule: 1 beat for the first POST_BOUNDARY_BEATS + 1 beats of a phrase, else DISPATCH_WINDOW_BEATS. Since
# unity-music-clock Task 8 KillChordPlanner.DispatchWindow (PatternScheduler's default) is
# OneShotScheduler.DispatchWindowBeats (0.5) on every beat, so the count below over-estimates the landing beats; it is
# kept as is because changing it changes Max Instances in instruments.js (an FMOD CLI run and a bank rebuild).
POST_BOUNDARY_BEATS, POST_BOUNDARY_DISPATCH_BEATS, DISPATCH_WINDOW_BEATS = 1, 1.0, 0.5
FEVER_PHASES = ["None", "Build", "Impact", "Fever", "Outro"]   # FeverPhaseMask bits 1, 2, 4, 8, 16 (None = outside fever)


class Inst:
    def __init__(self, name, table, bus, attack_ms, release_ms, max_pitch=15):
        self.name, self.table, self.bus = name, table, bus
        self.attack_ms, self.release_ms, self.max_pitch = attack_ms, release_ms, max_pitch

    @property
    def key(self):
        return (self.name, self.bus)

    def sample(self, midi):
        return samples.pick(self.table, midi, self.max_pitch)

    def event_volume(self, midi):
        """Volume of the note's sound in the instrument event (instruments.py: NORMALIZE_DB - rms, clamped, 2 decimals)."""
        s, _ = self.sample(midi)
        return round(max(VOLUME_MIN, min(NORMALIZE_DB - s[2], VOLUME_MAX)), 2)

    def sample_seconds(self, midi):
        """Length of the note's sample at its pitch (RIFF header; pure Python)."""
        s, pitch = self.sample(midi)
        return wav_seconds(s[0]) / (2 ** (pitch / 12.0))

    def onset(self, midi):
        """InstrumentTable.OnsetSeconds (instruments.py): sample onset / pitch ratio, 6 decimals."""
        s, pitch = self.sample(midi)
        return round(s[3] / (2 ** (pitch / 12.0)), 6)


def wav_seconds(file):
    import struct
    with open(os.path.join(ASSETS, file), "rb") as f:
        riff, _, wave = struct.unpack("<4sI4s", f.read(12))
        assert riff == b"RIFF" and wave == b"WAVE", file
        rate = align = None
        while True:
            head = f.read(8)
            assert len(head) == 8, f"{file}: no data chunk"
            cid, size = struct.unpack("<4sI", head)
            if cid == b"fmt ":
                fmt = f.read(size)
                rate, align = struct.unpack("<I", fmt[4:8])[0], struct.unpack("<H", fmt[12:14])[0]
            elif cid == b"data":
                return size / align / rate
            else:
                f.seek(size, 1)
            if size % 2:
                f.seek(1, 1)


class N:
    """A note: beat in the phrase, MIDI, length in beats (0 = to the sample end), old timeline volume dB, instrument."""

    def __init__(self, beat, midi, length, volume, inst, hold=0.0):
        self.beat, self.midi, self.length, self.inst, self.hold = beat, int(midi), length, inst, hold
        self.raw_volume = volume
        self.volume = round(max(VOLUME_MIN, min(volume, VOLUME_MAX)), 2)
        self.clamped = self.volume != round(volume, 2)

    @property
    def gain_db(self):
        return round(self.volume - self.inst.event_volume(self.midi), 2)


class Layer:
    """core: one of PatternTable.CoreLayerMask, scheduled a bar ahead by SoundManager's second PatternScheduler."""

    def __init__(self, name, legacy_track, core=False):
        self.name, self.legacy_track, self.core = name, legacy_track, core


class Clip:
    def __init__(self, name, layer, notes, styles=(0, 1, 2, 3), variation=(0, 4), combo=0, intensity=0.0,
                 fever=tuple(FEVER_PHASES), feature_loop=0, section_phrase=0, legacy_shift="none"):
        self.name, self.layer, self.notes = name, layer, sorted(notes, key=lambda n: (n.beat, n.midi))
        self.styles, self.variation, self.combo, self.intensity = tuple(styles), tuple(variation), combo, intensity
        self.fever, self.feature_loop, self.section_phrase = tuple(fever), feature_loop, section_phrase
        self.legacy_shift = legacy_shift

    def matches(self, style, variation, combo, intensity, section_phrase, feature_loop=0):
        return (style in self.styles and self.variation[0] <= variation <= self.variation[1] and combo >= self.combo
                and intensity >= self.intensity and self.section_phrase in (0, section_phrase)
                and self.feature_loop in (0, feature_loop))

    @property
    def conditions(self):
        """(styles, variation min, max, combo min, intensity min, fever phases, feature loop)."""
        return (tuple(sorted(self.styles)), self.variation[0], self.variation[1], self.combo, self.intensity,
                tuple(p for p in FEVER_PHASES if p in self.fever), self.feature_loop)


# Data modules (each defines LAYERS and CLIPS), imported lazily: they import the classes above from this module.
PATTERN_LAYERS = ["pattern_bed", "pattern_drums", "pattern_feature", "pattern_fever"]
FEATURE_LOOP_MAX = 255   # PatternClip.FeatureLoop is a byte
STYLE_COUNT = 4   # 0 synthpop, 1 latin, 2 funk, 3 dreamy (SoundManager BedStyle)
# Styles whose backbeat is the core snare and hat (the other styles bring their own); a new style adds one entry here,
# the music event does not change. Fever phrases are synthpop. -> PatternTable.StyleUsesCoreBackbeat
STYLE_USES_CORE_BACKBEAT = [True, False, False, False]
# The core layers (Layer.core) depend only on decisions made a phrase ahead (style, fever): SoundManager schedules and
# hands them to FMOD a bar ahead, so an ordinary frame stall cannot cut them. -> PatternTable.CoreLookaheadBeats
CORE_LOOKAHEAD_BEATS = 4.0
assert len(STYLE_USES_CORE_BACKBEAT) == STYLE_COUNT


def collect():
    import importlib
    layers, clips = [], []
    for name in PATTERN_LAYERS:
        mod = importlib.import_module(name)
        layers += mod.LAYERS
        clips += mod.CLIPS
    names = [l.name for l in layers]
    assert len(set(names)) == len(names), "duplicate layer names"
    assert len(layers) <= 64, "Layer is a bit of a ulong mask"
    clip_names = [c.name for c in clips]
    assert len(set(clip_names)) == len(clip_names), "duplicate clip names"
    insts = {}
    for c in clips:
        assert c.layer in names, f"clip {c.name}: unknown layer {c.layer}"
        assert all(0 <= s < STYLE_COUNT for s in c.styles) and c.styles, c.name
        assert 0 <= c.variation[0] <= c.variation[1] <= 255 and 0 <= c.combo <= 255, c.name
        assert set(c.fever) <= set(FEVER_PHASES) and c.fever, c.name
        assert c.section_phrase in (0, 1, 2), c.name
        assert 0 <= c.feature_loop <= FEATURE_LOOP_MAX, c.name
        for n in c.notes:
            assert 0 <= n.beat < PHRASE_BEATS, f"clip {c.name}: note at beat {n.beat}"
            assert 0 <= n.midi <= 127 and n.length >= 0 and n.hold >= 0, (c.name, n.beat, n.midi)
            n.inst.sample(n.midi)   # asserts the pitch range
            first = insts.setdefault(n.inst.key, n.inst)
            assert first is n.inst or vars(first) == vars(n.inst), f"instrument {n.inst.key} defined twice differently"
    return layers, clips, insts


def clamp_warnings(clips):
    out = []
    for c in clips:
        for n in c.notes:
            if n.clamped:
                out.append(f"warning: clip {c.name} beat {n.beat} MIDI {n.midi}: old volume {n.raw_volume:.2f} dB clamped to "
                           f"{n.volume} dB (GainDb {n.gain_db} carries the clamp)")
    return out


def _states():
    for style in range(STYLE_COUNT):
        for variation in range(5):
            for combo in range(4):
                for intensity in range(4):
                    for phrase in (1, 2):
                        yield (style, variation, combo, intensity, phrase)


def max_instances(clips, insts, layers):
    """(name, bus) -> most notes sounding at once at MAX_BPM (+ SPARE_INSTANCES), over every arrangement state (feature
    loops: each loop id the instrument's clips belong to)."""
    spb = 60.0 / MAX_BPM
    result = {}
    base = list(_states())
    core = {l.name for l in layers if l.core}
    for key, inst in insts.items():
        rel = inst.release_ms / 1000.0
        mine = [c for c in clips if any(n.inst.key == key for n in c.notes)]
        timeline = max(max(n.inst.sample_seconds(n.midi), EVENT_REGION_MIN_S)
                       for c in mine for n in c.notes if n.inst.key == key)
        loops = sorted({c.feature_loop for c in mine} - {0}) or [0]
        per_state = []
        for st in (b + (fl,) for b in base for fl in loops):
            iv = []
            for c in mine:
                if c.matches(*st):
                    for n in c.notes:
                        if n.inst.key == key:
                            a = n.beat * spb
                            end = a + (max(n.length * spb, n.hold) + rel if n.length > 0 else timeline)
                            if c.layer in core:
                                window = CORE_LOOKAHEAD_BEATS
                            elif int(n.beat) % PHRASE_BEATS <= POST_BOUNDARY_BEATS:
                                window = POST_BOUNDARY_DISPATCH_BEATS
                            else:
                                window = DISPATCH_WINDOW_BEATS
                            iv.append((a - window * spb, end))
            per_state.append(iv)
        times = sorted({a for iv in per_state for a, _ in iv})
        best = 0
        for t in times:
            tail = max(sum(1 for a, b in iv if a - PHRASE_BEATS * spb <= t < b - PHRASE_BEATS * spb) for iv in per_state)
            cur = max(sum(1 for a, b in iv if a <= t < b) for iv in per_state)
            best = max(best, cur + tail)
        result[key] = best + SPARE_INSTANCES
    return result


def register(lib):
    """instruments.py layer: every (instrument, bus) of the patterns with its ADSR and Max Instances, and every note."""
    layers, clips, insts = collect()
    for w in clamp_warnings(clips):
        print(w)
    mx = max_instances(clips, insts, layers)
    for key, inst in sorted(insts.items()):
        lib.add(inst.name, inst.table, mx[key], bus=inst.bus, max_pitch=inst.max_pitch,
                attack_ms=inst.attack_ms, release_ms=inst.release_ms)
    for c in clips:
        for n in c.notes:
            lib.need(n.inst.name, n.inst.bus, n.midi)


def _f(x):
    return f"{float(x)!r}f"


def cs_text(bus_member):
    """bus_member(bus path) -> C# OutputBus member (instruments.bus_folder)."""
    layers, clips, _ = collect()
    layer_index = {l.name: i for i, l in enumerate(layers)}
    L = [
        "// Generated by FMOD/Tools/patterns.py - edit the Python file (data: pattern_bed.py, pattern_drums.py, pattern_feature.py, pattern_fever.py), not this one.",
        "// Phrase patterns (16 beats) per layer and the arrangement conditions of each clip, for the PatternScheduler.",
        "",
        "/// <summary>Fever phase a clip plays in (a bit per phase); None = outside fever.</summary>",
        "[System.Flags]",
        "public enum FeverPhaseMask { None = 1, Build = 2, Impact = 4, Fever = 8, Outro = 16 }",
        "",
        "public readonly struct PatternNote",
        "{",
        "    /// <summary>Beat within the 4-bar phrase (0..16); the scheduler starts it InstrumentTable.OnsetSeconds early.</summary>",
        "    public readonly float Beat;",
        "    public readonly byte Midi;",
        "    /// <summary>Length in beats; 0 = play to the end of the sample.</summary>",
        "    public readonly float LengthBeats;",
        "    /// <summary>Minimum sounding time in seconds (0 = none).</summary>",
        "    public readonly float HoldSeconds;",
        "    /// <summary>Gain on top of the instrument event's level: old note level - (NormalizeDb - sample RMS), accents included.</summary>",
        "    public readonly float GainDb;",
        "    public readonly Instrument Instrument;",
        "    /// <summary>The bus the note's instrument event outputs to (its sub-bus carries the old track's effects).</summary>",
        "    public readonly OutputBus Bus;",
        "",
        "    public PatternNote(float beat, byte midi, float lengthBeats, float holdSeconds, float gainDb, Instrument instrument, OutputBus bus)",
        "    {",
        "        Beat = beat; Midi = midi; LengthBeats = lengthBeats; HoldSeconds = holdSeconds; GainDb = gainDb;",
        "        Instrument = instrument; Bus = bus;",
        "    }",
        "}",
        "",
        "public readonly struct PatternClip",
        "{",
        "    /// <summary>Index into PatternTable.LayerNames (a bit of the scheduler's layer mask).</summary>",
        "    public readonly int Layer;",
        "    /// <summary>Bit per style (1 &lt;&lt; style) the clip plays in.</summary>",
        "    public readonly int StyleMask;",
        "    /// <summary>Variation range, inclusive.</summary>",
        "    public readonly byte VariationMin, VariationMax;",
        "    /// <summary>Plays when Combo &gt;= ComboMin.</summary>",
        "    public readonly byte ComboMin;",
        "    /// <summary>Plays when Intensity &gt;= IntensityMin.</summary>",
        "    public readonly float IntensityMin;",
        "    public readonly FeverPhaseMask Fever;",
        "    /// <summary>Feature loop id the clip belongs to; 0 = any.</summary>",
        "    public readonly byte FeatureLoop;",
        "    /// <summary>",
        "    /// 0 = any phrase. 1 when the phrase starts a run in its section (the previous phrase was another section, or was this",
        "    /// section's second phrase); 2 when the same section continues after its first phrase - the timeline plays on into",
        "    /// bars 5-8 and loops back to bar 1 after bar 8. (The original off-beat pluck is an 8-bar pattern.)",
        "    /// </summary>",
        "    public readonly byte SectionPhrase;",
        "    /// <summary>The clip's notes: PatternTable.Notes[First .. First + Count - 1], sorted by beat.</summary>",
        "    public readonly int First, Count;",
        "",
        "    public PatternClip(int layer, int styleMask, byte variationMin, byte variationMax, byte comboMin, float intensityMin,",
        "        FeverPhaseMask fever, byte featureLoop, byte sectionPhrase, int first, int count)",
        "    {",
        "        Layer = layer; StyleMask = styleMask; VariationMin = variationMin; VariationMax = variationMax; ComboMin = comboMin;",
        "        IntensityMin = intensityMin; Fever = fever; FeatureLoop = featureLoop; SectionPhrase = sectionPhrase;",
        "        First = first; Count = count;",
        "    }",
        "}",
        "",
        "public static class PatternTable",
        "{",
        f"    public const int StyleCount = {STYLE_COUNT};",
        f"    public const float PhraseBeats = {PHRASE_BEATS}f;",
        "",
        "    /// <summary>Layers (bits) of the core kick / snare / hat, scheduled a bar ahead by their own PatternScheduler.</summary>",
        f"    public const ulong CoreLayerMask = 0x{sum(1 << i for i, l in enumerate(layers) if l.core):X}UL;",
        "    /// <summary>Lookahead and dispatch window (beats) of the core layers' scheduler.</summary>",
        f"    public const double CoreLookaheadBeats = {CORE_LOOKAHEAD_BEATS!r};",
        "",
        "    /// <summary>Per style: the core snare and hat are its backbeat (the other styles bring their own).</summary>",
        "    public static readonly bool[] StyleUsesCoreBackbeat = { " +
        ", ".join("true" if b else "false" for b in STYLE_USES_CORE_BACKBEAT) + " };",
        "",
        "    public static readonly string[] LayerNames =",
        "    {",
    ]
    L += [f'        "{l.name}",' for l in layers]
    L += ["    };", "", "    /// <summary>Name of each clip (same index as Clips).</summary>", "    public static readonly string[] ClipNames =", "    {"]
    L += [f'        "{c.name}",' for c in clips]
    L += ["    };", "", "    public static readonly PatternClip[] Clips =", "    {"]
    first = 0
    for c in clips:
        style_mask = sum(1 << s for s in c.styles)
        fever = " | ".join(f"FeverPhaseMask.{p}" for p in FEVER_PHASES if p in c.fever)
        L.append(f"        new PatternClip({layer_index[c.layer]}, {style_mask}, {c.variation[0]}, {c.variation[1]}, {c.combo}, "
                 f"{_f(c.intensity)}, {fever}, {c.feature_loop}, {c.section_phrase}, {first}, {len(c.notes)}),   // {c.name}")
        first += len(c.notes)
    L += ["    };", "", "    public static readonly PatternNote[] Notes =", "    {"]
    for c in clips:
        L.append(f"        // {c.name}")
        for n in c.notes:
            L.append(f"        new PatternNote({_f(n.beat)}, {n.midi}, {_f(n.length)}, {_f(n.hold)}, {_f(n.gain_db)}, "
                     f"Instrument.{n.inst.name}, OutputBus.{bus_member(n.inst.bus)}),")
    L += ["    };", "}", ""]
    return "\n".join(L)


def write_table(bus_member):
    import kill_oneshots
    return kill_oneshots.write_if_different(CS_PATH, cs_text(bus_member))


def __getattr__(name):
    """LEGACY_TRACKS: the old timeline tracks the pattern layers replaced, each once, in layer order (read by the
    one-time retired/cleanup_music.py that deleted them; Core hat and Open hat share Eighth-note hats)."""
    if name == "LEGACY_TRACKS":
        out = []
        for l in collect()[0]:
            if l.legacy_track not in out:
                out.append(l.legacy_track)
        return out
    raise AttributeError(name)


def main():
    import instruments
    changed = write_table(instruments.bus_folder)
    layers, clips, _ = collect()
    for w in clamp_warnings(clips):
        print(w)
    print(len(layers), "layers,", len(clips), "clips,", sum(len(c.notes) for c in clips), "notes -> PatternTable.g.cs",
          "(written)" if changed else "(unchanged)")


if __name__ == "__main__":
    main()
