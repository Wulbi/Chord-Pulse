"""
Pattern data pipeline: phrase patterns (16 beats) of notes per layer, with the conditions under which each clip plays,
generated to C# for the PatternScheduler (Assets/Scripts/Audio/Generated/PatternTable.g.cs), and every note registered
with the instrument library (instruments.py LAYERS calls register(lib)).

    python -I FMOD/Tools/instruments.py              # writes instruments.js, InstrumentTable.g.cs and PatternTable.g.cs
    python -I FMOD/Tools/patterns.py                 # PatternTable.g.cs only
    python -I FMOD/Tools/patterns.py --compare-legacy
        Compares the table with what the music event's timeline plays today on the migrated tracks (LEGACY_TRACKS): every
        timeline note of all four tempo sections (both phrases of each 8-bar section) from the event XML, and checks the
        old generators' build() notes are exactly those notes minus the hand-made Bass A / Pluck A. Differences are
        counted per rule of the Task 9 brief; anything else is printed as UNEXPLAINED (exit code 1).

Data modules (PATTERN_LAYERS) define Inst / Layer / Clip / N; see pattern_bed.py.

Rules (same sound as the old generators):
- Volume: old note volume (target - rms + accent, clamped to [-60, 10]) = instrument event volume (NORMALIZE_DB - rms,
  clamped) + GainDb. A note whose old target - rms + accent was clamped is reported (warning) and GainDb carries the clamp.
- Onset: the scheduler starts every note InstrumentTable.OnsetSeconds early (also on a phrase's first beat).
- ADSR: an instrument with the old track's attack / release is its own (instrument, bus) event (0 / 0 = the track had
  no ADSR: the note stops hard at its end like the timeline note).
- The last 16th of the phrase is empty (Unity switches style / variation just before the phrase boundary).
- Max Instances per (instrument, bus) = the most notes sounding at once (length + release) at 132 BPM + 4, over every
  arrangement state and the previous phrase's tails.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import samples  # noqa: E402

CS_PATH = os.path.join(HERE, "..", "..", "Assets", "Scripts", "Audio", "Generated", "PatternTable.g.cs")
NORMALIZE_DB = -27.0                   # keep equal to instruments.NORMALIZE_DB (instruments imports this module)
VOLUME_MIN, VOLUME_MAX = -60.0, 10.0   # old generators' note volume clamp, and the instrument event volume clamp
PHRASE_BEATS = 16
LAST_SIXTEENTH = PHRASE_BEATS - 0.25
MAX_BPM = 132
SPARE_INSTANCES = 4
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

    def onset(self, midi):
        """InstrumentTable.OnsetSeconds (instruments.py): sample onset / pitch ratio, 6 decimals."""
        s, pitch = self.sample(midi)
        return round(s[3] / (2 ** (pitch / 12.0)), 6)


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
    def __init__(self, name, legacy_track):
        self.name, self.legacy_track = name, legacy_track


class Clip:
    def __init__(self, name, layer, notes, styles=(0, 1, 2, 3), variation=(0, 4), combo=0, intensity=0.0,
                 fever=tuple(FEVER_PHASES), feature_loop=0, section_phrase=0, legacy_shift="none"):
        self.name, self.layer, self.notes = name, layer, sorted(notes, key=lambda n: (n.beat, n.midi))
        self.styles, self.variation, self.combo, self.intensity = tuple(styles), tuple(variation), combo, intensity
        self.fever, self.feature_loop, self.section_phrase = tuple(fever), feature_loop, section_phrase
        self.legacy_shift = legacy_shift

    def matches(self, style, variation, combo, intensity, section_phrase):
        return (style in self.styles and self.variation[0] <= variation <= self.variation[1] and combo >= self.combo
                and intensity >= self.intensity and self.section_phrase in (0, section_phrase))

    @property
    def conditions(self):
        """(styles, variation min, max, combo min, intensity min) - the part compare-legacy checks."""
        return (tuple(sorted(self.styles)), self.variation[0], self.variation[1], self.combo, self.intensity)


# Data modules (each defines LAYERS and CLIPS), imported lazily: they import the classes above from this module.
PATTERN_LAYERS = ["pattern_bed"]
STYLE_COUNT = 4   # 0 synthpop, 1 latin, 2 funk, 3 dreamy (SoundManager BedStyle)


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
        for n in c.notes:
            assert 0 <= n.beat < LAST_SIXTEENTH, f"clip {c.name}: note at beat {n.beat} (the last 16th must stay empty)"
            assert 0 <= n.midi <= 127 and n.length >= 0, (c.name, n.beat, n.midi)
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


def max_instances(clips, insts):
    """(name, bus) -> most notes sounding at once at MAX_BPM (+ SPARE_INSTANCES), over every arrangement state."""
    spb = 60.0 / MAX_BPM
    result = {}
    states = list(_states())
    for key, inst in insts.items():
        rel = inst.release_ms / 1000.0
        per_state = []
        for st in states:
            iv = []
            for c in clips:
                if c.matches(*st):
                    for n in c.notes:
                        if n.inst.key == key:
                            assert n.length > 0, "length 0 needs the sample length here"
                            a = n.beat * spb
                            iv.append((a, a + n.length * spb + rel))
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
    _, clips, insts = collect()
    for w in clamp_warnings(clips):
        print(w)
    mx = max_instances(clips, insts)
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
        "// Generated by FMOD/Tools/patterns.py - edit the Python file (data: pattern_bed.py), not this one.",
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
    """LEGACY_TRACKS: the old timeline tracks the pattern layers replace (one per layer; legacy_gate.py reads it)."""
    if name == "LEGACY_TRACKS":
        return [l.legacy_track for l in collect()[0]]
    raise AttributeError(name)


# ===== --compare-legacy ===============================================================================================
EVENT_XML = os.path.join(HERE, "..", "ChordPulse", "Metadata", "Event", "{3d468f15-e95b-49c7-89e5-8b0245d9bf6d}.xml")
METADATA = os.path.join(HERE, "..", "ChordPulse", "Metadata")
SECTION_BPMS = (108, 116, 124, 132)
SECTION_BEATS = 32
TIME_EPS = 0.0005     # 0.5 ms
VOLUME_EPS = 0.006


def _load_objects(path):
    import xml.etree.ElementTree as ET
    objs = {}
    for o in ET.parse(path).getroot().iter("object"):
        props = {p.get("name"): p.find("value").text for p in o.findall("property") if p.find("value") is not None}
        rels = {r.get("name"): [d.text for d in r.findall("destination")] for r in o.findall("relationship")}
        objs[o.get("id")] = (o.get("class"), props, rels)
    return objs


def legacy_notes(tracks):
    """Every SingleSound of the music event on the given tracks: start / length s, file, pitch, volume, conditions, ADSR.
    Studio leaves properties at their default out of the XML (start 0, length 1 s, volume 0, pitch 0)."""
    import glob
    objs = _load_objects(EVENT_XML)
    files, params = {}, {}
    for f in glob.glob(os.path.join(METADATA, "AudioFile", "*.xml")):
        for k, v in _load_objects(f).items():
            files[k] = v[1].get("assetPath")
    for f in glob.glob(os.path.join(METADATA, "ParameterPreset", "*.xml")):
        for k, v in _load_objects(f).items():
            if v[0] == "ParameterPreset":
                params[v[2]["parameter"][0]] = v[1]["name"]
    out = []
    for cls, props, rels in objs.values():
        if cls != "GroupTrack":
            continue
        name = objs[rels["mixerGroup"][0]][1].get("name")
        if name not in tracks:
            continue
        for sid in rels.get("modules", []):
            c, p, r = objs[sid]
            assert c == "SingleSound", (name, c)
            conds = {}
            for cid in r.get("triggerConditions", []):
                cc = objs[cid]
                pname = params.get(cc[2]["parameter"][0], cc[2]["parameter"][0])
                conds[pname] = (float(cc[1].get("minimum", 0)), float(cc[1].get("maximum", 0)))
            env = None
            for mid in r.get("modulators", []):
                m = objs[mid]
                if m[0] == "ADSRModulator" and m[1].get("nameOfPropertyBeingModulated") == "volume":
                    env = (float(m[1].get("attackTime", 0)), float(m[1].get("releaseTime", 0)))
            out.append(dict(track=name, start=float(p.get("start", 0)), length=float(p.get("length", 1)),
                            volume=float(p.get("volume", 0)), pitch=float(p.get("pitch", 0)),
                            file=files.get((r.get("audioFile") or [None])[0]), conds=conds, env=env))
    return out


def _sections():
    out, t = [], 0.0
    for bpm in SECTION_BPMS:
        out.append((t, bpm))
        t += SECTION_BEATS * 60.0 / bpm
    return out


def _legacy_conditions(conds):
    """XML trigger conditions -> (styles, variation min, max, combo min, intensity min), or a string if one does not map."""
    styles, var, combo, inten = (0, 1, 2, 3), (0, 4), 0, 0.0
    for name, (lo, hi) in conds.items():
        if name == "BedStyle":
            styles = tuple(s for s in range(STYLE_COUNT) if lo <= s <= hi)
        elif name == "Variation":
            var = (round(lo + 0.4), round(hi - 0.4))
        elif name == "Combo":
            assert abs(hi - 3.4) < 1e-6, conds
            combo = round(lo + 0.4)
        elif name == "Intensity":
            assert abs(hi - 3) < 1e-6, conds
            inten = round(lo, 3)
        else:
            return f"unmapped condition {name} [{lo}, {hi}]"
    return (styles, var[0], var[1], combo, inten)


def _old_shift(clip, note, bar):
    rule = clip.legacy_shift
    if rule == "none":
        return 0.0
    if rule in ("onset", "onset_not_phrase_start"):
        if rule == "onset_not_phrase_start" and bar % 4 == 0:
            return 0.0
        s, pitch = note.inst.sample(note.midi)
        return s[3] / (2 ** (pitch / 12.0))
    kind, seconds = rule
    if kind == "fixed_not_phrase_start" and bar % 4 == 0:
        return 0.0
    return seconds


def compare_legacy():
    layers, clips, insts = collect()
    track_of = {l.name: l.legacy_track for l in layers}
    tracks = set(track_of.values())
    legacy = legacy_notes(tracks)
    secs = _sections()
    rules = {"exact": 0, "volume split (NORMALIZE_DB - rms + GainDb)": 0, "onset rule": 0,
             "ADSR as separate (instrument, bus) event": 0, "no ADSR -> event attack 0 / release 0": 0}
    unexplained, onset_rows = [], {}
    per_section = []
    for si, (s0, bpm) in enumerate(secs):
        spb = 60.0 / bpm
        old = [n for n in legacy if s0 - 0.08 <= n["start"] < s0 + SECTION_BEATS * spb - 0.08]
        new = []
        for half in (0, 1):
            for c in clips:
                if c.section_phrase not in (0, half + 1):
                    continue
                for n in c.notes:
                    bar = half * 4 + int(n.beat // 4)
                    s, pitch = n.inst.sample(n.midi)
                    t = s0 + (half * PHRASE_BEATS + n.beat) * spb
                    new.append(dict(track=track_of[c.layer], time=t, file=s[0], pitch=pitch,
                                    volume=round(n.inst.event_volume(n.midi) + n.gain_db, 2),
                                    event_volume=n.inst.event_volume(n.midi), length=n.length * spb,
                                    conds=c.conditions, env=(n.inst.attack_ms, n.inst.release_ms),
                                    onset=n.inst.onset(n.midi), old_shift=_old_shift(c, n, bar), clip=c, note=n,
                                    beat=half * PHRASE_BEATS + n.beat))
        used = set()
        counts = dict.fromkeys(rules, 0)
        bad = []
        for o in sorted(old, key=lambda x: x["start"]):
            oc = _legacy_conditions(o["conds"])
            if isinstance(oc, str):
                bad.append(f"section {si} {o['track']} t={o['start']:.4f}: {oc}")
                continue
            best = None
            for i, x in enumerate(new):
                if i in used or x["track"] != o["track"] or x["file"] != o["file"] or abs(x["pitch"] - o["pitch"]) > 1e-6:
                    continue
                if x["conds"] != oc or abs(x["time"] - o["start"]) > 0.1:
                    continue
                d = abs(x["time"] - x["old_shift"] - o["start"])
                if best is None or d < best[0]:
                    best = (d, i)
            if best is None:
                bad.append(f"section {si} {o['track']} t={o['start']:.4f} {o['file']} pitch {o['pitch']} vol {o['volume']} "
                           f"conds {o['conds']}: no new note")
                continue
            used.add(best[1])
            x = new[best[1]]
            what = []
            if abs(x["volume"] - o["volume"]) > VOLUME_EPS:
                bad.append(f"section {si} {o['track']} beat {x['beat']}: volume {o['volume']} -> {x['volume']}")
            elif abs(x["event_volume"] - o["volume"]) > VOLUME_EPS:
                what.append("volume split (NORMALIZE_DB - rms + GainDb)")
            if abs(x["length"] - o["length"]) > TIME_EPS:
                bad.append(f"section {si} {o['track']} beat {x['beat']}: length {o['length']:.4f} s -> {x['length']:.4f} s")
            old_env = o["env"] or (0.0, 0.0)
            if (float(x["env"][0]), float(x["env"][1])) != old_env:
                bad.append(f"section {si} {o['track']} beat {x['beat']}: ADSR {o['env']} -> {x['env']}")
            elif o["env"] is None:
                what.append("no ADSR -> event attack 0 / release 0")
            else:
                what.append("ADSR as separate (instrument, bus) event")
            new_start = x["time"] - x["onset"]
            if abs(new_start - o["start"]) > TIME_EPS:
                if abs(x["time"] - x["old_shift"] - o["start"]) <= TIME_EPS:
                    what.append("onset rule")
                else:
                    bad.append(f"section {si} {o['track']} beat {x['beat']}: start {o['start']:.5f} -> {new_start:.5f} "
                               f"(old shift {x['old_shift']:.4f} does not explain it)")
            if si == 0:
                key = (x["clip"].name, x["note"].inst.name, x["note"].midi, round(x["old_shift"], 4))
                onset_rows[key] = x["onset"]
            if not what or what == ["no ADSR -> event attack 0 / release 0"] or what == ["ADSR as separate (instrument, bus) event"]:
                counts["exact"] += 1
            for w in what:
                counts[w] += 1
        for i, x in enumerate(new):
            if i not in used:
                bad.append(f"section {si} {x['track']} beat {x['beat']} {x['file']} pitch {x['pitch']}: no timeline note")
        per_section.append((si, bpm, len(old), len(new), counts, bad))
        unexplained += bad
        for k in rules:
            rules[k] += counts[k]
    return per_section, rules, unexplained, onset_rows, legacy


def generator_check(legacy):
    """The old generators' build() notes on the migrated tracks are exactly the timeline notes minus Bass A / Pluck A."""
    import bed_combo
    import bed_style
    import bed_tension
    import bed_variation
    tr = {"bass": "Bass - C Am F G", "pluck": "Offbeat pluck", "pad": "Combo pad", "bells": "Combo bells",
          "strings": "Tension strings", "sub": "Tension sub", "acid": "Tension acid bass"}
    gen = []
    for n in bed_variation.build():
        if n["track"] in tr:
            gen.append((tr[n["track"]], n["start"], n["file"], n["pitch"], n["volume"]))
    for n in bed_combo.build() + bed_tension.build():
        if n["track"] in tr:
            gen.append((tr[n["track"]], n["start"], n["file"], n["pitch"], n["volume"]))
    tracks = set(l.legacy_track for l in collect()[0])
    for n in bed_style.build():
        if n["track"] in tracks:
            gen.append((n["track"], n["start"], n["file"], n["pitch"], n["volume"]))

    def key(t, s, f, p, v):
        return (t, round(s, 4), f, round(p, 3), round(v, 2))

    from collections import Counter
    g = Counter(key(*x) for x in gen)
    x = Counter(key(n["track"], n["start"], n["file"], n["pitch"], n["volume"]) for n in legacy)
    rest = x - g
    missing = g - x
    hand = Counter()
    for k, c in rest.items():
        hand[k[0]] += c
    return sum(g.values()), sum(x.values()), sum(missing.values()), dict(hand)


def section_check(legacy):
    """Per migrated track: are the four sections the same apart from tempo, and do bars 1-4 and 5-8 differ?"""
    from collections import Counter, defaultdict
    secs = _sections()
    per = defaultdict(Counter)
    for n in legacy:
        si = max(i for i, (s0, _) in enumerate(secs) if n["start"] >= s0 - 0.08)
        s0, bpm = secs[si]
        spb = 60.0 / bpm
        b = (n["start"] - s0) / spb
        g = round(b * 4) / 4
        # notes moved early by a fixed time land between grid steps: snap with the residual in seconds
        for cand in (g, g + 0.25):
            r = n["start"] - (s0 + cand * spb)
            if -0.08 <= r <= 0.0005:
                g = cand
                break
        r = round((n["start"] - (s0 + g * spb)) * 1000, 1)
        half = int(g // 16)
        k = (g % 16, r, n["file"], n["pitch"], round(n["volume"], 2), round(n["length"] / spb, 3),
             json.dumps(sorted(n["conds"].items())), str(n["env"]))
        per[(n["track"], si, half)][k] += 1
    out = {}
    for track in sorted({k[0] for k in per}):
        ref = per[(track, 0, 0)]
        sections_same = all(per[(track, si, h)] == per[(track, 0, h)] for si in range(4) for h in (0, 1))
        halves_same = all(per[(track, si, 0)] == per[(track, si, 1)] for si in range(4))
        out[track] = (sum(ref.values()) * 2, sections_same, halves_same)
    return out


def main_compare():
    per_section, rules, unexplained, onset_rows, legacy = compare_legacy()
    print("Migrated tracks (LEGACY_TRACKS):", ", ".join(l.legacy_track for l in collect()[0]))
    print("\nTracks per section (timeline notes per 8 bars; sections equal apart from tempo; bars 1-4 == bars 5-8):")
    for track, (count, same, halves) in section_check(legacy).items():
        print(f"  {track:20s} {count:4d}   sections same: {same}   phrases same: {halves}")
    g, x, missing, hand = generator_check(legacy)
    print(f"\nOld generators' build(): {g} notes, all on the timeline: {missing == 0} (missing {missing}); timeline notes {x}; "
          f"not generated (hand-made): {hand}")
    for si, bpm, n_old, n_new, counts, bad in per_section:
        label = "both phrases, bars 1-8" if si == 0 else "bars 1-8"
        print(f"\nSection {si} ({bpm} BPM, {label}): timeline {n_old}, table {n_new}, unexplained {len(bad)}")
        for k, v in counts.items():
            print(f"    {k:45s} {v}")
    print("\nTotal over the four sections:")
    for k, v in rules.items():
        print(f"    {k:45s} {v}")
    _, clips, insts = collect()
    mx = max_instances(clips, insts)
    print("\nMax Instances (rule: most simultaneous notes at 132 BPM incl. release + 4):")
    for key in sorted(mx):
        print(f"    {key[0]:10s} {key[1]:36s} {mx[key]}")
    warns = clamp_warnings(clips)
    print(f"\nClamped notes: {len(warns)}")
    for w in warns:
        print("    " + w)
    print("\nOnset (108 BPM section): clip, instrument, MIDI, old shift s, new onset s, difference ms")
    for (clip, inst, midi, old_shift), onset in sorted(onset_rows.items()):
        d = (onset - old_shift) * 1000
        flag = "  > 5 ms" if abs(d) > 5 else ""
        if abs(d) > 0.05:
            print(f"    {clip:15s} {inst:9s} {midi:3d}  {old_shift:.4f}  {onset:.4f}  {d:+6.1f}{flag}")
    print(f"\nUNEXPLAINED: {len(unexplained)}")
    for u in unexplained[:200]:
        print("    " + u)
    return 1 if unexplained else 0


def main():
    if sys.argv[1:] == ["--compare-legacy"]:
        sys.exit(main_compare())
    import instruments
    changed = write_table(instruments.bus_folder)
    layers, clips, _ = collect()
    for w in clamp_warnings(clips):
        print(w)
    print(len(layers), "layers,", len(clips), "clips,", sum(len(c.notes) for c in clips), "notes -> PatternTable.g.cs",
          "(written)" if changed else "(unchanged)")


if __name__ == "__main__":
    main()
