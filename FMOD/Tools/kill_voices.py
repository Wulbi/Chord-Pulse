"""
Kill chords that follow the adaptive bed (Docs/AdaptiveAudioPlan.md section 9).

Writes kill_voices.js next to this file. Run it on the FMOD project (FMOD Studio closed), after bed_style.js
(it needs the BedStyle, Combo and Tension parameters):
    fmodstudiocl.exe -script FMOD/Tools/kill_voices.js FMOD/ChordPulse/ChordPulse.fspro
It is idempotent: it deletes its "Kill *" tracks and resets the BedStyle condition on the original kill chords.

The original Red / Blue / Yellow kill chords stay exactly as they are, but only in BedStyle 0 (synthpop).
Every beat of the timeline already holds the bar's chord for each colour (RedKills / BlueKills / YellowKills
1 = triad, 2 = + octave, 3 = + top voice); this script lays the same notes, on the same beats and with the
same voicing conditions, in each style's instruments - the style's sound REPLACES the original:

    BedStyle   Red                Blue                    Yellow
    1 latin    marimba            vibraphone              glockenspiel (+2 oct)
    2 funk     brass stab         clean guitar (-1 oct)   clavi-synth (+1 oct)
    3 dreamy   glass marimba      harp                    formant bells (+1 oct)

and, in every style (including synthpop, in that style's instrument):
    Combo 2+     the chord's 7th (Cmaj7, Am7, Fmaj7, G7)
    Combo 3      a formant-bell octave on the top note
    Tension .65+ a low-octave root punch (bass), so kills hit harder when the bed is tense
Fever forces BedStyle 0 / Combo 0 / Tension 0 in Unity, so fever kill chords are the original ones.
No new game parameter and no Unity change: the kill chords read the bed's own parameters.
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))

SECTIONS = []
_t = 0.0
for _bpm in (108, 116, 124, 132):
    SECTIONS.append((_t, _bpm))
    _t += 32 * 60.0 / _bpm
BARS_PER_SECTION = 8

COLORS = ["Red", "Blue", "Yellow"]
# Voicing per bar of the phrase (C, Am, F, G), lowest first, exactly as the original kill chords:
# voices 0-2 need XKills >= 1, voice 3 needs >= 2, voice 4 needs >= 3.
VOICING = [(60, 64, 67, 72, 79), (57, 60, 64, 69, 76), (53, 57, 60, 65, 72), (55, 59, 62, 67, 74)]
VOICE_MIN = [0.5, 0.5, 0.5, 1.5, 2.5]
SEVENTH = [71, 67, 64, 65]      # Cmaj7 B4, Am7 G4, Fmaj7 E4, G7 F4
ROOT_LOW = [48, 45, 41, 43]     # punch: C3 A2 F2 G2

# (file, measured root MIDI, peak short-term RMS dB, attack onset s)
ORIGINAL = {
    "Red": [("AT101V_ClassicLead_C4_v100.wav", 60, -11.8, 0.0)],
    "Blue": [("ATMD_LuckyLead_E4_v100.wav", 64, -12.0, 0.0)],
    "Yellow": [("AT101V_PolyPluck_C4_v100.wav", 60, -14.0, 0.0)],
}
MARIMBA = [("Mallets/Marimba_hit_Outrigger_F3_loud_01.wav", 65, -30.7, 0.006),
           ("Mallets/Marimba_hit_Outrigger_C4_loud_01.wav", 72, -33.2, 0.005),
           ("Mallets/Marimba_hit_Outrigger_G4_loud_01.wav", 79, -32.2, 0.005),
           ("Mallets/Marimba_hit_Outrigger_F5_loud_01.wav", 89, -37.7, 0.004)]
VIBES = [("Mallets/Vibes_hard_C3_v3_rr1_Main.wav", 60, -17.4, 0.007),
         ("Mallets/Vibes_hard_E3_v3_rr1_Main.wav", 64, -24.5, 0.007),
         ("Mallets/Vibes_hard_D4_v3_rr1_Main.wav", 74, -18.0, 0.007),
         ("Mallets/Vibes_hard_A4_v3_rr1_Main.wav", 81, -21.0, 0.007),
         ("Mallets/Vibes_hard_C5_v3_rr1_Main.wav", 84, -17.4, 0.006)]
GLOCK = [("Mallets/glock_medium_G4.wav", 79, -40.9, 0.007), ("Mallets/glock_medium_C5.wav", 84, -40.8, 0.007),
         ("Mallets/glock_medium_G5.wav", 91, -40.9, 0.007), ("Mallets/glock_medium_C6.wav", 96, -41.7, 0.007)]
BRASS = [("Synth/AT700_BrassStab_Bb3_v100.wav", 58, -9.1, 0.008), ("Synth/AT700_BrassStab_F4_v100.wav", 65, -10.5, 0.009)]
GUITAR = [("Guitar/Thrum_CleanNote_E3_v100.wav", 52, -11.0, 0.025), ("Guitar/Thrum_CleanNote_G4_v100.wav", 55, -14.1, 0.025),
          ("Guitar/Thrum_CleanNote_A3_v100.wav", 57, -14.2, 0.025), ("Guitar/Thrum_CleanNote_D4_v100.wav", 62, -16.6, 0.025)]
# Clavi-synth: weak fundamental, so files are mapped to their lowest strong partial (C2 file -> C4, and so on).
CLAV = [("FMKeys/Clavisynth_C2_vl3.wav", 60, -22.6, 0.007), ("FMKeys/Clavisynth_C3_vl3.wav", 72, -23.2, 0.003),
        ("FMKeys/Clavisynth_C4_vl3.wav", 84, -25.1, 0.002)]
GLASS = [("Synth/ATFS1_GlassMarimba_C4_v100.wav", 60, -13.7, 0.001), ("Synth/ATFS1_GlassMarimba_G4_v100.wav", 67, -13.2, 0.0),
         ("Synth/ATFS1_GlassMarimba_C5_v100.wav", 72, -13.3, 0.0)]
HARP = [("Mallets/KSHarp_D4_mf.wav", 62, -31.6, 0.026), ("Mallets/KSHarp_F4_mf.wav", 65, -28.9, 0.015),
        ("Mallets/KSHarp_A4_mf.wav", 69, -30.8, 0.021), ("Mallets/KSHarp_C5_mf.wav", 72, -32.0, 0.026),
        ("Mallets/KSHarp_E5_mf.wav", 76, -29.7, 0.025)]
BELLS = [("Synth/ATFS1_FormantBells_C5_v100.wav", 72, -9.2, 0.001), ("Synth/ATFS1_FormantBells_G5_v100.wav", 79, -9.0, 0.0)]
BASS = [("AT101V_101Bass_C2_v100.wav", 36, -15.6, 0.001)]
SUB = [("SynthBass/DeviceNTI_SineSub_C2_v100.wav", 36, -9.8, 0.0)]

# style -> colour -> (sample table, octave shift in semitones, note length in beats)
STYLES = {
    0: {c: (ORIGINAL[c], 0, 0.78) for c in COLORS},
    1: {"Red": (MARIMBA, 0, 1.0), "Blue": (VIBES, 0, 1.5), "Yellow": (GLOCK, 24, 1.5)},
    2: {"Red": (BRASS, 0, 0.6), "Blue": (GUITAR, -12, 0.6), "Yellow": (CLAV, 12, 0.5)},
    3: {"Red": (GLASS, 0, 1.5), "Blue": (HARP, 0, 2.0), "Yellow": (BELLS, 12, 2.0)},
}
STYLE_NAMES = {1: "latin", 2: "funk", 3: "dreamy"}
# Per-note loudness target (dB) - the originals hit about -27 dB per note (sample RMS + their -15 dB track);
# trimmed from offline renders so each style's kill chords match the synthpop ones.
TARGET = {1: -27.0, 2: -27.0, 3: -28.0}
EXTRA = {"seventh": -30.0, "bell": -33.0, "punch": -30.0}
# Our tracks run at 0 dB (the originals use -15 dB because their synth samples are hot); quiet acoustic
# samples would otherwise hit the +10 dB note-volume ceiling.
KILL_TRACK_VOLUME = 0.0


def pick(table, midi):
    s = min(table, key=lambda x: (abs(midi - x[1]), -x[1]))
    pitch = midi - s[1]
    assert -15 <= pitch <= 15, (s[0], midi, pitch)
    return s, pitch


def build():
    notes = []

    def add(track, color, style, table, midi, start, length, target, vmin, extra=None, shift_ok=True):
        s, pitch = pick(table, midi)
        ratio = 2 ** (pitch / 12.0)
        onset = s[3] / ratio if shift_ok else 0.0
        volume = round(max(-60.0, min(target - s[2] - KILL_TRACK_VOLUME, 10.0)), 2)
        notes.append(dict(track=track, color=color, style=style, file=s[0], pitch=pitch,
                          start=round(max(0.0, start - onset), 6), length=round(length, 6), volume=volume,
                          vmin=vmin, extra=extra))

    for start, bpm in SECTIONS:
        beat = 60.0 / bpm
        for bar in range(BARS_PER_SECTION):
            pb = bar % 4
            for b in range(4):
                t = start + (bar * 4 + b) * beat
                # A phrase downbeat can be a section-jump landing: never start a note before it.
                shift_ok = not (pb == 0 and b == 0)
                for style, by_color in STYLES.items():
                    for color in COLORS:
                        table, octave, length = by_color[color]
                        if style != 0:
                            for v, m in enumerate(VOICING[pb]):
                                add("Kill " + STYLE_NAMES[style], color, style, table, m + octave, t, length * beat,
                                    TARGET[style], VOICE_MIN[v], shift_ok=shift_ok)
                        add("Kill combo", color, style, table, SEVENTH[pb] + octave, t, length * beat,
                            EXTRA["seventh"], 0.5, ("Combo", 1.6, 3.4), shift_ok)
                        add("Kill combo", color, style, BELLS, VOICING[pb][2] + 12, t, 1.5 * beat,
                            EXTRA["bell"], 0.5, ("Combo", 2.6, 3.4), shift_ok)
                        add("Kill punch", color, style, SUB if style == 3 else BASS, ROOT_LOW[pb], t, 0.7 * beat,
                            EXTRA["punch"], 0.5, ("Tension", 0.65, 1.0), shift_ok)
    return notes


JS_TEMPLATE = r"""// Generated by FMOD/Tools/kill_voices.py - edit the Python file, not this one.
var NOTES = __NOTES__;
var TRACKS = __TRACKS__;
var TRACK_VOLUME = __VOLUME__;

function log(m) { console.log("[kill] " + m); }
var project = studio.project;
var ev = project.lookup("event:/Music/ChordPulseGame");
var assetsRoot = project.filePath.replace(/[^\/\\]+$/, "") + "Assets/";
function preset(name) {
    var p = project.lookup("parameter:/" + name);
    if (!p) throw "parameter " + name + " missing - run the bed_*.js scripts first";
    return p;
}
var style = preset("BedStyle");
var params = { Combo: preset("Combo"), Tension: preset("Tension") };
var kills = { Red: preset("RedKills"), Blue: preset("BlueKills"), Yellow: preset("YellowKills") };

function groupByName(name) {
    var found = null;
    ev.groupTracks.forEach(function (t) { if (t.mixerGroup.name === name) found = t; });
    return found;
}
function styleCondition(sound) {
    var found = null;
    sound.triggerConditions.forEach(function (c) { if (c.parameter && c.parameter.id === style.parameter.id) found = c; });
    return found;
}

// --- undo an earlier run ---
var old = [];
ev.groupTracks.forEach(function (t) { if (TRACKS.indexOf(t.mixerGroup.name) >= 0) old.push(t); });
old.forEach(function (t) { project.deleteObject(t); });
if (old.length) log("removed " + old.length + " old kill tracks");

// --- the original kill chords now only play in synthpop ---
["Red", "Blue", "Yellow"].forEach(function (color) {
    var t = groupByName(color + " kill chord");
    t.modules.forEach(function (s) {
        var c = styleCondition(s);
        if (c) project.deleteObject(c);
        s.addParameterCondition(style, -0.4, 0.4);
    });
    log("synthpop only: " + color + " kill chord (" + t.modules.length + ")");
});

var files = {};
function audioFile(rel) {
    if (files[rel]) return files[rel];
    var found = null;
    project.model.AudioFile.findInstances().forEach(function (a) {
        if (a.isValid && a.masterAssetFolder && a.length > 0 && a.assetPath === rel) found = a;
    });
    if (!found) { found = project.importAudioFile(assetsRoot + rel); log("imported " + rel); }
    if (!found) throw "could not import " + rel;
    files[rel] = found;
    return found;
}
var killBus = groupByName("Kill Chords").mixerGroup;
var tracks = {};
TRACKS.forEach(function (name) {
    var t = project.create("GroupTrack");
    t.mixerGroup.name = name;
    t.mixerGroup.output = killBus;
    t.mixerGroup.volume = TRACK_VOLUME;
    ev.relationships.groupTracks.add(t);
    tracks[name] = t;
});

NOTES.forEach(function (n) {
    var s = tracks[n.track].addSound(ev.timeline, "SingleSound", n.start, n.length);
    s.audioFile = audioFile(n.file);
    s.pitch = n.pitch;
    s.volume = n.volume;
    s.addParameterCondition(kills[n.color], n.vmin, 3);
    s.addParameterCondition(style, n.style - 0.4, n.style + 0.4);
    if (n.extra) s.addParameterCondition(params[n.extra[0]], n.extra[1], n.extra[2]);
    var env = project.create("ADSRModulator");
    env.nameOfPropertyBeingModulated = "volume";
    env.attackTime = 1; env.releaseTime = 120;
    env.objectBeingModulated = s;
});
log("placed " + NOTES.length + " notes");
project.save();
log("saved");
"""


def main():
    notes = build()
    tracks = sorted({n["track"] for n in notes})
    js = (JS_TEMPLATE.replace("__NOTES__", json.dumps(notes)).replace("__TRACKS__", json.dumps(tracks))
          .replace("__VOLUME__", str(KILL_TRACK_VOLUME)))
    with open(os.path.join(HERE, "kill_voices.js"), "w", encoding="utf-8") as f:
        f.write(js)
    counts = {}
    for n in notes:
        counts[n["track"]] = counts.get(n["track"], 0) + 1
    print(counts, "->", "kill_voices.js")


if __name__ == "__main__":
    main()
