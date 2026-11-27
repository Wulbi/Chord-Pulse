"""
Kill chord one-shots: which instrument, notes, length and gain each style/colour plays (was kill_voices.py's
timeline notes; now Unity schedules them as one-shots of the shared instrument events, see instruments.py).

Registers every note a kill chord can play with the instrument library (LAYERS in instruments.py) and writes
Assets/Scripts/Audio/Generated/KillChordTable.g.cs and kill_oneshots.js (the FMOD project step that removes the old
timeline kill chords; the kill-chord mixing is in mixer.py; see the comment above JS_PATH). Run
`python FMOD/Tools/instruments.py` (it calls register() and also writes both files); running this file alone rewrites
only the table and the .js.

Values are the ones from the old kill_voices.py (style 3's target trimmed by 0.5 dB in Task 6). The instrument events normalise every note to instruments.NORMALIZE_DB
(-27 dB); GainDb = target - (-27). Style 0 uses the original synth samples at their original level (GainDb 0).
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import progressions  # noqa: E402
import samples  # noqa: E402

CS_PATH = os.path.join(HERE, "..", "..", "Assets", "Scripts", "Audio", "Generated", "KillChordTable.g.cs")
BUS = "Kill Chords"    # mixer bus the instrument events play through (bus:/Kill Chords)
NORMALIZE_DB = -27.0   # keep equal to instruments.NORMALIZE_DB (not imported: instruments imports this module)
MAX_INSTANCES = 24     # one beat can stack 3 colours x 5 voices + extras on the same instrument

COLORS = ["Red", "Blue", "Yellow"]
VOICE_MIN = [1, 1, 1, 2, 3]   # smallest voicing (1 triad, 2 + octave, 3 + top voice) that plays voice i
BELL_LENGTH = 1.5
PUNCH_LENGTH = 0.7

# instrument name -> sample table
TABLES = {
    "OrigRed": samples.ORIGINAL["Red"], "OrigBlue": samples.ORIGINAL["Blue"], "OrigYellow": samples.ORIGINAL["Yellow"],
    "Marimba": samples.MARIMBA, "Vibes": samples.VIBES, "Glock": samples.GLOCK,
    "Brass": samples.BRASS, "Guitar": samples.GUITAR, "Clav": samples.CLAV,
    "Glass": samples.GLASS, "Harp": samples.HARP, "Bells": samples.BELLS,
    "Bass": samples.BASS, "Sub": samples.SUB,
}
# style -> colour -> (instrument, octave shift in semitones, note length in beats)
STYLES = {
    0: [("OrigRed", 0, 0.78), ("OrigBlue", 0, 0.78), ("OrigYellow", 0, 0.78)],
    1: [("Marimba", 0, 1.0), ("Vibes", 0, 1.5), ("Glock", 24, 1.5)],
    2: [("Brass", 0, 0.6), ("Guitar", -12, 0.6), ("Clav", 12, 0.5)],
    3: [("Glass", 0, 1.5), ("Harp", 0, 2.0), ("Bells", 12, 2.0)],
}
TARGET = [-27.0, -27.0, -27.0, -28.5]   # style 0: original per-note level (~-27 dB); 3 trimmed by the Task 6 styles capture
EXTRA = {"seventh": -30.0, "bell": -33.0, "punch": -30.0}


def _max_pitch(name):
    # the original synth leads/plucks are one sample each and were pitched up to +19 semitones on the timeline
    return 24 if name.startswith("Orig") else 15


def _fits(entry):
    """A chord's kill notes stay inside every style's instrument range (progressions.derive ok)."""
    try:
        for style, cols in STYLES.items():
            for inst, octave, _ in cols:
                for m in list(entry["v"]) + [entry["s"]]:
                    samples.pick(TABLES[inst], m + octave, _max_pitch(inst))
            samples.pick(TABLES["Bells"], entry["v"][2] + 12, 15)
        return True
    except AssertionError:
        return False


# Per chord id (progressions.CHORD_NAMES: C Am F G are the old bar order, then Dm Em): the voicing (VOICE_MIN voices),
# the seventh (combo 2+) and the low punch root. Dm / Em are derived from the original four (progressions.derive);
# KillChordPlanner reads the chord of the beat's bar from the phrase's progression.
_CHORDS = progressions.derive({
    "C": dict(v=(60, 64, 67, 72, 79), s=71), "Am": dict(v=(57, 60, 64, 69, 76), s=67),
    "F": dict(v=(53, 57, 60, 65, 72), s=64), "G": dict(v=(55, 59, 62, 67, 74), s=65),
}, _fits)
VOICING = [_CHORDS[c]["v"] for c in progressions.CHORD_NAMES]
SEVENTH = [_CHORDS[c]["s"] for c in progressions.CHORD_NAMES]
ROOT_LOW = [progressions.BASS_ROOT[c] + 12 for c in progressions.CHORD_NAMES]
CHORD_COUNT = len(progressions.CHORD_NAMES)
assert VOICING[:4] == [(60, 64, 67, 72, 79), (57, 60, 64, 69, 76), (53, 57, 60, 65, 72), (55, 59, 62, 67, 74)]
assert SEVENTH[:4] == [71, 67, 64, 65] and ROOT_LOW[:4] == [48, 45, 41, 43]


def punch_instrument(style):
    return "Sub" if style == 3 else "Bass"


def all_notes():
    """Yield (instrument, midi) for every note any kill chord can play."""
    for style, cols in STYLES.items():
        for inst, octave, _ in cols:
            for pb in range(CHORD_COUNT):
                for m in VOICING[pb]:
                    yield inst, m + octave
                yield inst, SEVENTH[pb] + octave
        for pb in range(CHORD_COUNT):
            yield "Bells", VOICING[pb][2] + 12
            yield punch_instrument(style), ROOT_LOW[pb]


def register(lib):
    for name, table in TABLES.items():
        lib.add(name, table, MAX_INSTANCES, bus=BUS, max_pitch=_max_pitch(name))
    for inst, midi in all_notes():
        lib.need(inst, BUS, midi)


def _arr(values):
    return "{ " + ", ".join(str(v) for v in values) + " }"


def cs_text():
    inst = [[c[0] for c in STYLES[s]] for s in range(4)]
    L = [
        "// Generated by FMOD/Tools/kill_oneshots.py - edit the Python file, not this one.",
        "// Kill chord one-shot tables: per style/colour instrument, octave shift and length, plus the voicing and extras.",
        "public static class KillChordTable",
        "{",
        "    public const int Styles = 4;",
        "    public const int Colors = 3;",
        "",
        "    /// <summary>[style, colour] instrument (colour: 0 Red, 1 Blue, 2 Yellow).</summary>",
        "    public static readonly Instrument[,] ColorInstrument =",
        "    {",
    ]
    for s in range(4):
        L.append("        { " + ", ".join(f"Instrument.{n}" for n in inst[s]) + " },")
    L += ["    };", "", "    /// <summary>[style, colour] octave shift in semitones.</summary>",
          "    public static readonly int[,] Octave =", "    {"]
    for s in range(4):
        L.append("        " + _arr([c[1] for c in STYLES[s]]) + ",")
    L += ["    };", "", "    /// <summary>[style, colour] note length in beats.</summary>",
          "    public static readonly double[,] LengthBeats =", "    {"]
    for s in range(4):
        L.append("        " + _arr([repr(c[2]) for c in STYLES[s]]) + ",")
    L += ["    };", "", "    /// <summary>[style] main-note target level in dB; style 0 keeps the original per-note level.</summary>",
          "    public static readonly float[] TargetDb = " + _arr([f"{t}f" for t in TARGET]) + ";", "",
          "    /// <summary>Chord ids (PatternTable.ChordNames): 0 C, 1 Am, 2 F, 3 G, 4 Dm, 5 Em.</summary>",
          f"    public const int Chords = {CHORD_COUNT};", "",
          "    /// <summary>[chord, voice] MIDI note, lowest first.</summary>",
          "    public static readonly int[,] Voicing =", "    {"]
    for pb in range(CHORD_COUNT):
        L.append("        " + _arr(VOICING[pb]) + ",")
    L += ["    };", "",
          "    /// <summary>[voice] smallest voicing (1 triad, 2 + octave, 3 + top voice) that plays the voice.</summary>",
          "    public static readonly int[] VoiceMinVoicing = " + _arr(VOICE_MIN) + ";", "",
          "    public static readonly int[] Seventh = " + _arr(SEVENTH) + ";",
          "    public static readonly int[] RootLow = " + _arr(ROOT_LOW) + ";", "",
          f"    public const float SeventhDb = {EXTRA['seventh']}f;",
          f"    public const float BellDb = {EXTRA['bell']}f;",
          f"    public const float PunchDb = {EXTRA['punch']}f;",
          f"    public const double BellLengthBeats = {BELL_LENGTH!r};",
          f"    public const double PunchLengthBeats = {PUNCH_LENGTH!r};",
          f"    public const float NormalizeDb = {NORMALIZE_DB}f;",
          "    public const int BellStyleOffset = 12;   // bell plays VOICING[chord][2] + 12", "",
          "    public static Instrument PunchInstrument(int style)",
          "    {",
          f"        return style == 3 ? Instrument.Sub : Instrument.Bass;",
          "    }", "}", ""]
    return "\n".join(L)


def write_if_different(path, text):
    """Writes only when the content changed (a second run leaves the file and its mtime alone)."""
    old = None
    if os.path.exists(path):
        with open(path, "r", encoding="utf-8", newline="") as f:
            old = f.read()
    if old == text:
        return False
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    return True


def write_table():
    write_if_different(CS_PATH, cs_text())


# --- FMOD project step: kill_oneshots.js (run after mixer.js) ------------------------------------------------------
# Removes what is left of the old timeline kill chords in the music event: the Red/Blue/Yellow kill chord tracks,
# leftover Kill combo/punch/<style> tracks, the in-event "Kill Chords" group, and the RedKills/BlueKills/YellowKills,
# *KillsEven/Odd and KillChordMix parameters (only if present; KillChordMix is now Unity's bus setVolume).
# The kill-chord mixing (bus:/Kill Chords chain, ducking of Harmony and Feature) moved to mixer.py.
# Idempotent. Run with FMOD Studio closed, then build as a SEPARATE command (the CLI can crash after saving):
#     fmodstudiocl.exe -script FMOD/Tools/kill_oneshots.js FMOD/ChordPulse/ChordPulse.fspro
#     fmodstudiocl.exe -build FMOD/ChordPulse/ChordPulse.fspro
JS_PATH = os.path.join(HERE, "kill_oneshots.js")
OLD_TRACKS = ["Red kill chord", "Blue kill chord", "Yellow kill chord"]
OLD_TRACK_PATTERN = r"^Kill (combo|punch|latin|funk|dreamy)$"
OLD_PARAMETERS = [c + s for c in COLORS for s in ("Kills", "KillsEven", "KillsOdd")] + ["KillChordMix"]

JS_TEMPLATE = r"""// Generated by FMOD/Tools/kill_oneshots.py - edit the Python file, not this one.
// Kill chords as Unity one-shots: removes what is left of the timeline kill chords (mixing: mixer.js).
var OLD_TRACKS = __OLD_TRACKS__;
var OLD_TRACK_PATTERN = new RegExp(__OLD_PATTERN__);
var OLD_PARAMETERS = __OLD_PARAMETERS__;

function log(m) { console.log("[kill-oneshots] " + m); }
var project = studio.project;
// The music event is gone since unity-music-clock Task 8 (remove_music_event.js); then only the parameters are checked.
var ev = project.lookup("event:/Music/ChordPulseGame");
if (!ev) ev = { groupTracks: [], parameters: [] };

function countNotes() { var n = 0; ev.groupTracks.forEach(function (t) { n += t.modules.length; }); return n; }
var notesBefore = countNotes();

// --- 1. old timeline kill chords and the in-event Kill Chords group (it only fed them) ---
var removed = [];
ev.groupTracks.slice().forEach(function (t) {
    var n = t.mixerGroup.name;
    if (OLD_TRACKS.indexOf(n) >= 0 || OLD_TRACK_PATTERN.test(n) || n === "Kill Chords") {
        removed.push(n + " (" + t.modules.length + ")");
        project.deleteObject(t);
    }
});
log("removed tracks: " + (removed.join(", ") || "none"));

// --- 2. parameters ---
var gone = [];
OLD_PARAMETERS.forEach(function (name) {
    var preset = null;
    project.model.ParameterPreset.findInstances().forEach(function (p) { if (p.isValid && p.name === name) preset = p; });
    if (!preset) return;
    ev.parameters.slice().forEach(function (proxy) {
        if (proxy.preset && (proxy.preset.id === preset.parameter.id || proxy.preset.id === preset.id)) project.deleteObject(proxy);
    });
    project.deleteObject(preset);
    gone.push(name);
});
log("removed parameters: " + (gone.join(", ") || "none"));
log("notes in event: " + notesBefore + " -> " + countNotes());
if (removed.length || gone.length) { project.save(); log("saved"); } else log("nothing to do");
"""


def js_text():
    return (JS_TEMPLATE.replace("__OLD_TRACKS__", json.dumps(OLD_TRACKS))
            .replace("__OLD_PATTERN__", json.dumps(OLD_TRACK_PATTERN))
            .replace("__OLD_PARAMETERS__", json.dumps(OLD_PARAMETERS)))


def write_js():
    write_if_different(JS_PATH, js_text())


if __name__ == "__main__":
    write_table()
    write_js()
    print("KillChordTable.g.cs and kill_oneshots.js written")
