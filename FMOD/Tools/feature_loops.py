"""
Feature loops (trumpet, whistle, mallets, latin percussion, synth lead, guitar) for event:/Music/ChordPulseGame.

Writes feature_loops.js next to this file. Run it on the FMOD project with
    fmodstudiocl.exe -script FMOD/Tools/feature_loops.js FMOD/ChordPulse/ChordPulse.fspro
The script is idempotent: it removes the "Feature*" tracks it made before and rebuilds them.

Music rules (see Docs/AdaptiveAudioPlan.md section 7):
- Every phrase is 4 bars of C - Am - F - G, so a loop written once fits every tempo section.
- A loop is placed at both phrase starts of all four tempo sections (8 copies), gated by
  FeatureLoop == id. Unity picks the id one phrase ahead (Audio/FeatureSelector.cs).
- Beats below are relative to the phrase start (0..16). MIDI 60 = C4.

Samples: VSCO 2 CE / VCSL (CC0) and AT / Thrum packs (see SOURCES_AND_LICENSES.txt). VSCO trumpet,
flute, marimba, xylophone and glockenspiel file names are one octave below the measured pitch,
so the roots below are measured notes, not the ones in the file names.
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))

# Tempo sections on the event timeline: (start seconds, bpm). Each section is 8 bars = 2 phrases.
SECTIONS = []
_t = 0.0
for _bpm in (108, 116, 124, 132):
    SECTIONS.append((_t, _bpm))
    _t += 32 * 60.0 / _bpm

# file, measured root (MIDI), peak short-term RMS (dB), audible seconds, attack onset seconds
TRUMPET = [
    ("Brass/Sum_SHTrumpet_stac_C3_v3_rr1.wav", 60, -33.0, 0.50, 0.004),
    ("Brass/Sum_SHTrumpet_stac_F3_v3_rr1.wav", 65, -36.1, 0.40, 0.004),
    ("Brass/Sum_SHTrumpet_stac_A#3_v3_rr1.wav", 70, -26.8, 0.41, 0.005),
    ("Brass/Sum_SHTrumpet_stac_D4_v3_rr1.wav", 74, -21.5, 0.40, 0.005),
    ("Brass/Sum_SHTrumpet_stac_F4_v3_rr1.wav", 77, -20.6, 0.38, 0.003),
]
TRUMPET_SUSTAIN = ("Brass/Sum_SHTrumpet_sus_C3_v3_rr1.wav", 60, -21.9, 6.0, 0.010)
# Whistle = flute played high with the air filtered off (track EQ in the script).
WHISTLE = [
    ("Woodwinds/LDFlute_stac_C5_v3_rr1.wav", 84, -27.9, 0.24, 0.015),
    ("Woodwinds/LDFlute_stac_E5_v3_rr1.wav", 88, -26.9, 0.24, 0.020),
    ("Woodwinds/LDFlute_stac_A5_v3_rr1.wav", 93, -22.6, 0.26, 0.018),
]

MARIMBA = [
    ("Mallets/Marimba_hit_Outrigger_F3_loud_01.wav", 65, -30.7, 1.69, 0.006),
    ("Mallets/Marimba_hit_Outrigger_C4_loud_01.wav", 72, -33.2, 1.02, 0.005),
    ("Mallets/Marimba_hit_Outrigger_G4_loud_01.wav", 79, -32.2, 0.65, 0.005),
    ("Mallets/Marimba_hit_Outrigger_F5_loud_01.wav", 89, -37.7, 0.46, 0.004),
    ("Mallets/Marimba_hit_Outrigger_C6_loud_01.wav", 96, -36.3, 0.31, 0.003),
]
XYLO = [
    ("Mallets/Xylo_Medium_G4_ff_01_far.wav", 79, -18.0, 0.83, 0.003),
    ("Mallets/Xylo_Medium_C5_ff_01_far.wav", 84, -18.3, 0.59, 0.003),
    ("Mallets/Xylo_Medium_G5_ff_01_far.wav", 91, -20.7, 0.34, 0.003),
    ("Mallets/Xylo_Medium_C6_ff_01_far.wav", 96, -21.7, 0.30, 0.002),
]
GLOCK = [
    ("Mallets/glock_medium_G4.wav", 79, -40.9, 3.03, 0.007),
    ("Mallets/glock_medium_C5.wav", 84, -40.8, 3.60, 0.007),
    ("Mallets/glock_medium_G5.wav", 91, -40.9, 1.94, 0.007),
    ("Mallets/glock_medium_C6.wav", 96, -41.7, 1.81, 0.007),
]
SYNTH = [
    ("Synth/AT20V_ScreamLead_A3_v105.wav", 57, -8.4, 1.44, 0.001),
    ("Synth/AT20V_ScreamLead_E4_v105.wav", 64, -8.2, 1.44, 0.001),
    ("Synth/AT20V_ScreamLead_A4_v105.wav", 69, -8.1, 1.41, 0.001),
]
GUITAR = [("Guitar/Thrum_PowerChordE5_E2B2E3_v100.wav", 40, -13.1, 2.22, 0.001)]
# Unpitched: name -> sample (the "midi" field of a percussion note holds this name).
PERC = {
    "cowbell": ("WorldPerc/Cowbell2_Normal_v3_rr1_Mid.wav", 0, -21.8, 0.24, 0.010),
    "cowbell_mute": ("WorldPerc/Cowbell1_Muted_v3_rr1_Mid.wav", 0, -29.2, 0.25, 0.009),
    "agogo_hi": ("WorldPerc/Agogo_High_v3_rr1_Mid.wav", 0, -40.5, 0.25, 0.009),
    "agogo_lo": ("WorldPerc/Agogo_Low_v2_rr1_Mid.wav", 0, -44.6, 0.76, 0.009),
    "claves": ("WorldPerc/Claves1_Hit_v3_rr1_Mid.wav", 0, -38.8, 0.26, 0.009),
    "bongo_hi": ("WorldPerc/BongoH_Hit1_v3_rr1_Mid.wav", 0, -27.5, 0.32, 0.009),
    "bongo_lo": ("WorldPerc/BongoL_Hit1_v3_rr1_Mid.wav", 0, -26.2, 0.35, 0.011),
    "conga": ("WorldPerc/Conga_HitN_v3_rr1_Sum.wav", 0, -28.5, 0.48, 0.001),
    "conga_slap": ("WorldPerc/Quinto_HitN_v3_rr1_Sum.wav", 0, -26.0, 0.33, 0.001),
    "tumba": ("WorldPerc/Tumba_HitFM_v3_rr1_Sum.wav", 0, -32.6, 0.32, 0.001),
    "guiro": ("WorldPerc/Guiro_Fast_rr1_Mid.wav", 0, -50.6, 0.39, 0.009),
}

# instrument -> (FMOD track, sample table, target dB, gated). Gated instruments stop at the written
# length (sustaining synth / guitar); the others ring out like the real instrument.
# Targets come from offline renders: a loop sits about 2-3 dB under the whole accompaniment
# (bed RMS -36 dB at Intensity 1).
INSTRUMENTS = {
    "trumpet": ("trumpet", TRUMPET, -29.0, False),
    "whistle": ("whistle", WHISTLE, -28.0, False),
    "marimba": ("mallets", MARIMBA, -29.0, False),
    "xylo": ("mallets", XYLO, -31.0, False),
    "glock": ("mallets", GLOCK, -31.0, False),
    "perc": ("perc", PERC, -32.0, False),
    "synth": ("synth", SYNTH, -31.0, True),
    "guitar": ("guitar", GUITAR, -29.0, True),
}
# Instrument flags, shared with FeatureSelector.Instrument in Unity.
FLAGS = {"trumpet": 1, "whistle": 2, "marimba": 4, "xylo": 4, "glock": 4, "perc": 8, "synth": 16, "guitar": 32}

C4, D4, E4, F4, G4, A4, B4 = 60, 62, 64, 65, 67, 69, 71
C5, D5, E5, F5, G5, A5, B5 = 72, 74, 76, 77, 79, 81, 83
C6, D6, E6, F6, G6, A6, B6 = 84, 86, 88, 89, 91, 93, 95
C7, D7, E7 = 96, 98, 100
G2 = 43


def T(beat, *notes, length=0.5, vol=0.0, hold=False):
    """Trumpet note(s). Several notes = a brass-section stab."""
    return [("trumpet", beat, n, length, vol - (3.0 if len(notes) > 1 else 0.0), hold) for n in notes]


def W(beat, note, length=0.5, vol=0.0):
    return [("whistle", beat, note, length, vol, False)]


def M(beat, note, inst="marimba", length=0.5, vol=0.0):
    """Mallets: marimba, xylo or glock."""
    return [(inst, beat, note, length, vol, False)]


def P(beat, *names, vol=0.0):
    return [("perc", beat, n, 0.5, vol, False) for n in names]


def S(beat, note, length=0.45, vol=0.0):
    """Synth lead, gated: length is how long the key is held."""
    return [("synth", beat, note, length, vol, False)]


def G(beat, root, length=0.3, vol=0.0):
    """Guitar power chord on root (MIDI note of the lowest string)."""
    return [("guitar", beat, root, length, vol, False)]


def loop(id_, name, start_bar, bars, *events):
    notes = [n for e in events for n in e]
    for inst, beat, *_ in notes:
        assert start_bar * 4 <= beat < (start_bar + bars) * 4, (name, beat)
    return {"id": id_, "name": name, "startBar": start_bar, "bars": bars, "notes": notes}


LOOPS = [
    # 1 bar, bar 1 (C): pick-up triplet feel into a C-major brass stab.
    loop(1, "Trumpet Fanfare", 0, 1,
         T(0.0, G4), T(0.5, G4), T(1.0, C5), T(1.5, E5),
         T(2.0, C5, E5, G5, vol=1.5), T(3.0, E5), T(3.5, G5, vol=1.0)),

    # 1 bar, bar 4 (G): run up through G7 and two stabs that lean into the next phrase's C.
    loop(2, "Trumpet Rise", 3, 1,
         T(12.0, G4), T(12.5, B4), T(13.0, D5), T(13.5, F5),
         T(14.0, G5, vol=1.0), T(14.5, F5),
         T(15.0, B4, D5, G5), T(15.5, B4, D5, G5, vol=1.5)),

    # 2 bars, bars 1-2 (C, Am): bouncy whistle hook.
    loop(3, "Whistle Hook", 0, 2,
         W(0.0, E6), W(0.5, G6), W(1.5, E6), W(2.0, D6), W(2.5, C6), W(3.0, D6), W(3.5, E6),
         W(4.0, A6, vol=1.0), W(4.5, G6), W(5.0, E6), W(6.0, C6), W(6.5, D6), W(7.0, E6, vol=1.0)),

    # 2 bars, bars 3-4 (F, G): leaping whistle line ending on F6 so the next C resolves it.
    loop(4, "Whistle Skip", 2, 2,
         W(8.0, A6, vol=1.0), W(8.5, F6), W(9.0, C6), W(9.5, F6), W(10.0, A6), W(11.0, C7, vol=1.0), W(11.5, A6),
         W(12.0, B6, vol=1.0), W(12.5, G6), W(13.0, D6), W(13.5, G6), W(14.0, B6), W(14.5, A6), W(15.0, G6),
         W(15.5, F6)),

    # 4 bars: trumpet riff over the whole phrase, whistle drops a note into each gap.
    loop(5, "Brass Solo", 0, 4,
         T(0.0, G5), T(0.5, G5), T(1.0, E5), T(1.5, G5), W(2.0, G6), T(2.5, A5), T(3.0, G5), T(3.5, E5),
         T(4.0, E5), T(4.5, E5), T(5.0, C5), T(5.5, E5), W(6.0, E6), T(6.5, D5), T(7.0, C5), T(7.5, A4),
         T(8.0, A5), T(8.5, A5), T(9.0, F5), T(9.5, A5), W(10.0, C7), T(10.5, A5), T(11.0, F5), T(11.5, C5),
         T(12.0, B4, D5, G5), T(13.0, B4, D5, G5), T(13.5, D5), T(14.0, F5), T(14.5, G5),
         T(15.0, B4, D5, G5, vol=1.5), W(15.5, B6)),

    # 4 bars: trumpet calls (bars 1, 3), whistle answers (bar 2), both together an octave apart (bar 4).
    loop(6, "Call and Response", 0, 4,
         T(0.0, C5), T(0.5, E5), T(1.0, G5), T(2.0, C5, length=1.0, hold=True), T(2.5, E5), T(3.0, G5, vol=1.0),
         W(4.5, E6), W(5.0, A6), W(5.5, E6), W(6.0, C6), W(6.5, E6), W(7.0, A6), W(7.5, G6),
         T(8.0, F5), T(8.5, A5), T(9.0, C5), T(9.5, F5), T(10.0, A5, vol=1.0), T(11.0, G5), T(11.5, F5),
         T(12.0, G4), W(12.0, G5), T(12.5, A4), W(12.5, A5), T(13.0, B4), W(13.0, B5),
         T(13.5, D5), W(13.5, D6), T(14.0, G5, vol=1.0), W(14.0, G6), T(15.0, B4, D5, F5), W(15.0, F6),
         T(15.5, B4, D5, G5, vol=1.5), W(15.5, G6)),

    # 4 bars: salsa-style percussion - cowbell on the beat, 3-2 clave, agogo and conga answers,
    # a guiro scrape per bar, then a bongo/conga fill into the next phrase.
    loop(7, "Latin Groove", 0, 4,
         *[P(b + 4 * bar, "cowbell", vol=0.0 if b == 0 else -4.0) for bar in range(4) for b in range(4)
           if not (bar == 3 and b >= 2)],
         *[P(b, "claves", vol=-2.0) for b in (0.0, 1.5, 3.0, 5.0, 6.0, 8.0, 9.5, 11.0, 13.0)],
         *[P(b + 4 * bar, "agogo_hi") + P(b + 4 * bar + 1.0, "agogo_lo") for bar in range(3) for b in (0.5, 2.5)],
         *[P(b + 4 * bar, "conga") + P(b + 4 * bar + 0.5, "conga", vol=-3.0) for bar in range(3) for b in (1.5, 3.5)],
         *[P(4.0 * bar, "guiro", vol=-2.0) for bar in range(4)],
         P(12.5, "bongo_hi"), P(13.5, "bongo_hi"), P(13.75, "bongo_lo", vol=-3.0),
         P(14.0, "bongo_hi"), P(14.25, "bongo_hi", vol=-3.0), P(14.5, "bongo_lo"), P(14.75, "bongo_lo", vol=-3.0),
         P(15.0, "conga_slap"), P(15.25, "conga", vol=-2.0), P(15.5, "tumba"), P(15.75, "tumba", vol=-2.0)),

    # 2 bars, bars 3-4 (F, G): marimba arpeggios in 16ths up and back, xylophone doubling the accents.
    loop(8, "Marimba Run", 2, 2,
         M(8.0, F5), M(8.0, F6, "xylo"), M(8.25, A5), M(8.5, C6), M(8.75, F6), M(9.0, A6, vol=1.0),
         M(9.5, F6), M(10.0, C6), M(10.0, C7, "xylo"), M(10.5, A5), M(11.0, C6), M(11.5, F6),
         M(12.0, G5), M(12.0, G6, "xylo"), M(12.25, B5), M(12.5, D6), M(12.75, G6), M(13.0, B6, vol=1.0),
         M(13.5, G6), M(14.0, D6), M(14.0, D7, "xylo"), M(14.5, B5),
         M(15.0, D6), M(15.25, F6), M(15.5, G6), M(15.5, G6, "xylo"), M(15.75, B6)),

    # 1 bar, bar 2 (Am): glockenspiel climbing the A minor chord and sparkling at the top.
    loop(9, "Glock Sparkle", 1, 1,
         M(4.0, A5, "glock"), M(4.25, C6, "glock"), M(4.5, E6, "glock"), M(4.75, A6, "glock"),
         M(5.0, C7, "glock", vol=1.0), M(5.5, A6, "glock"), M(5.75, E6, "glock"),
         M(6.0, C7, "glock"), M(6.5, E7, "glock", vol=1.0), M(7.0, A6, "glock"), M(7.5, E7, "glock", vol=-2.0)),

    # 4 bars: chopped synth lead riff, a root-fifth bounce that climbs each chord.
    loop(10, "Synth Hook", 0, 4,
         S(0.0, C5), S(0.5, C5), S(1.0, G4), S(1.5, C5), S(2.0, E5, vol=1.0), S(2.5, D5), S(3.0, C5), S(3.5, G4),
         S(4.0, A4), S(4.5, A4), S(5.0, E4), S(5.5, A4), S(6.0, C5, vol=1.0), S(6.5, B4), S(7.0, A4), S(7.5, E5),
         S(8.0, F5), S(8.5, F5), S(9.0, C5), S(9.5, F5), S(10.0, A5, vol=1.0), S(10.5, G5), S(11.0, F5), S(11.5, C5),
         S(12.0, G5), S(12.5, G5), S(13.0, D5), S(13.5, G5), S(14.0, A5, vol=1.0), S(14.5, G5),
         S(15.0, F5), S(15.5, D5, length=0.5)),

    # 1 bar, bar 4 (G): syncopated G power-chord hits pushing into the next phrase.
    loop(11, "Power Chord Hits", 3, 1,
         G(12.0, G2, vol=1.0), G(13.5, G2), G(14.5, G2), G(15.0, G2, length=0.2, vol=-2.0),
         G(15.5, G2, length=0.5, vol=1.0)),

    # 2 bars, bars 1-2 (C, Am): bouncy xylophone tune over a muted cowbell pulse.
    loop(12, "Mallet Hook", 0, 2,
         M(0.0, E6, "xylo"), M(0.5, G6, "xylo"), M(1.0, C7, "xylo", vol=1.0), M(1.5, G6, "xylo"),
         M(2.0, E6, "xylo"), M(2.5, G6, "xylo"), M(3.0, A6, "xylo"), M(3.5, G6, "xylo"),
         M(4.0, E6, "xylo"), M(4.5, A6, "xylo"), M(5.0, C7, "xylo", vol=1.0), M(5.5, A6, "xylo"),
         M(6.0, E6, "xylo"), M(6.5, D6, "xylo"), M(7.0, C6, "xylo"), M(7.5, E6, "xylo"),
         *[P(float(b), "cowbell_mute", vol=0.0 if b % 2 == 0 else -4.0) for b in range(8)]),
]


def pick(inst, midi, hold):
    if inst == "perc":
        return PERC[midi], 0
    table = [TRUMPET_SUSTAIN] if inst == "trumpet" and hold else INSTRUMENTS[inst][1]
    sample = min(table, key=lambda s: (abs(midi - s[1]), -s[1]))
    pitch = midi - sample[1]
    assert -12 <= pitch <= 12, (inst, midi, pitch)
    return sample, pitch


def build():
    placements = []
    for loop_ in LOOPS:
        for section, (start, bpm) in enumerate(SECTIONS):
            beat_sec = 60.0 / bpm
            for phrase in range(2):
                phrase_start = start + phrase * 16 * beat_sec
                for inst, beat, midi, length, vol, hold in loop_["notes"]:
                    sample, pitch = pick(inst, midi, hold)
                    ratio = 2 ** (pitch / 12.0)
                    audible = sample[3] / ratio
                    track, _, target, gated = INSTRUMENTS[inst]
                    # Let struck / staccato notes ring out, but stop sustaining samples at the written length.
                    if hold or gated:
                        seconds = length * beat_sec
                    else:
                        seconds = max(length * beat_sec, min(audible, 1.2 if inst == "glock" else 0.6))
                    volume = target - sample[2] + vol
                    placements.append({
                        "loop": loop_["id"], "track": track, "file": sample[0],
                        # Start early by the sample's attack so the note speaks on the beat - except on the
                        # phrase downbeat: a section jump lands exactly there and would skip an earlier note.
                        "start": round(phrase_start + beat * beat_sec - (sample[4] / ratio if beat > 0 else 0.0), 6), "length": round(seconds, 6),
                        "pitch": pitch, "volume": round(max(-30.0, min(volume, 10.0)), 2),
                    })
    return placements


JS_TEMPLATE = r"""// Generated by FMOD/Tools/feature_loops.py - edit the Python file, not this one.
var NOTES = __NOTES__;
var LOOP_COUNT = __LOOP_COUNT__;

function log(m) { console.log("[feature] " + m); }
var project = studio.project;
var ev = project.lookup("event:/Music/ChordPulseGame");
if (!ev) throw "event:/Music/ChordPulseGame not found";
var assetsRoot = project.filePath.replace(/[^\/\\]+$/, "") + "Assets/";

// --- FeatureLoop parameter (0 = none, 1..N = loop id) ---
var preset = project.lookup("parameter:/FeatureLoop");
if (!preset) {
    var created = project.workspace.addGameParameter({ name: "FeatureLoop", type: project.parameterType.User, min: 0, max: LOOP_COUNT });
    log("created parameter FeatureLoop (" + created.entity + ")");
    preset = project.lookup("parameter:/FeatureLoop") || created;
}
log("FeatureLoop preset " + preset.entity + " parameter " + preset.parameter);
preset.parameter.minimum = 0;
preset.parameter.maximum = LOOP_COUNT;
preset.parameter.initialValue = 0;
var hasProxy = false;
ev.parameters.forEach(function (p) {
    if (p.preset && (p.preset.id === preset.parameter.id || p.preset.id === preset.id)) hasProxy = true;
});
if (!hasProxy) { ev.addGameParameter(preset); log("added FeatureLoop to event"); }

// --- remove tracks from an earlier run ---
var old = [];
ev.groupTracks.forEach(function (t) { if (/^Feature/.test(t.mixerGroup.name)) old.push(t); });
var harmony = null, killChords = null;
ev.groupTracks.forEach(function (t) {
    if (t.mixerGroup.name === "Harmony") harmony = t.mixerGroup;
    if (t.mixerGroup.name === "Kill Chords") killChords = t.mixerGroup;
});
var harmonyComp = null, killSidechain = null;
harmony.effectChain.effects.forEach(function (e) { if (e.entity === "CompressorEffect") harmonyComp = e; });
// The in-event Kill Chords group only exists before kill_oneshots.js (kill chords are one-shots on bus:/Kill Chords
// now). Without it the Feature group gets no in-event ducking; mixer.js, run after this script, moves the
// Feature effects to bus:/Feature (fed by return:/Feature Duck), keyed by the bus (Docs/AdaptiveAudioPlan.md 8-10).
if (killChords) killChords.effectChain.effects.forEach(function (e) { if (e.entity === "Sidechain") killSidechain = e; });
old.forEach(function (t) {
    t.mixerGroup.effectChain.effects.forEach(function (e) {
        if (e.entity === "Sidechain" && harmonyComp) harmonyComp.relationships.sidechains.remove(e);
    });
    project.deleteObject(t);
});
if (old.length) log("removed " + old.length + " old feature tracks");

// --- audio files (reuse if already imported) ---
var files = {};
function audioFile(rel) {
    if (files[rel]) return files[rel];
    var found = null;
    // findInstances also returns stale, invalid entries left by the asset scan; only reuse real ones.
    project.model.AudioFile.findInstances().forEach(function (a) {
        if (a.isValid && a.masterAssetFolder && a.length > 0 && a.assetPath === rel) found = a;
    });
    if (!found) { found = project.importAudioFile(assetsRoot + rel); log("imported " + rel + " -> " + (found && found.assetPath)); }
    if (!found) throw "could not import " + rel;
    files[rel] = found;
    return found;
}

function addTrack(name, output) {
    var t = project.create("GroupTrack");
    t.mixerGroup.name = name;
    t.mixerGroup.output = output;
    ev.relationships.groupTracks.add(t);
    return t;
}

// --- Feature bus: ducked by kill chords, ducks the harmony, a bit of room ---
var bus = addTrack("Feature", ev.mixer.masterBus);
if (killSidechain) {
    // a compressor without its key would compress the loops on their own level
    var duck = bus.mixerGroup.effectChain.addEffect("CompressorEffect");
    duck.threshold = -24; duck.ratio = 3; duck.attackTime = 3; duck.releaseTime = 140;
    duck.relationships.sidechains.add(killSidechain);
} else {
    log("no in-event Kill Chords group: Feature ducking is set up by mixer.js");
}
var room = bus.mixerGroup.effectChain.addEffect("SFXReverbEffect");
room.decayTime = 900; room.earlyDelay = 10; room.lateDelay = 20; room.highCut = 6000; room.wetLevel = -14; room.dryLevel = 0;
var key = bus.mixerGroup.effectChain.addEffect("Sidechain");
// Since mixer.py the Feature duck of Harmony is on bus:/Music Bed/Harmony (keyed by bus:/Feature), and mixer.js, run
// after this script, strips this group back to its send; the in-event Harmony compressor no longer exists.
if (harmonyComp) harmonyComp.relationships.sidechains.add(key);
var limiter = bus.mixerGroup.effectChain.addEffect("LimiterEffect");
limiter.ceiling = -4; limiter.release = 30;

var trumpet = addTrack("Feature trumpet", bus.mixerGroup);
var tHp = trumpet.mixerGroup.effectChain.addEffect("HighpassEffect");
tHp.cutoff = 180; tHp.resonance = 1;
var whistle = addTrack("Feature whistle", bus.mixerGroup);
var wHp = whistle.mixerGroup.effectChain.addEffect("HighpassEffect");
wHp.cutoff = 700; wHp.resonance = 1;         // only the pure top of the flute
var wLp = whistle.mixerGroup.effectChain.addEffect("LowpassEffect");
wLp.cutoff = 5500; wLp.resonance = 1;
whistle.mixerGroup.volume = -1;

var mallets = addTrack("Feature mallets", bus.mixerGroup);
var mHp = mallets.mixerGroup.effectChain.addEffect("HighpassEffect");
mHp.cutoff = 150; mHp.resonance = 1;
var perc = addTrack("Feature perc", bus.mixerGroup);
var pHp = perc.mixerGroup.effectChain.addEffect("HighpassEffect");
pHp.cutoff = 120; pHp.resonance = 1;
var synth = addTrack("Feature synth", bus.mixerGroup);
var sHp = synth.mixerGroup.effectChain.addEffect("HighpassEffect");
sHp.cutoff = 200; sHp.resonance = 1;
var sLp = synth.mixerGroup.effectChain.addEffect("LowpassEffect");
sLp.cutoff = 6500; sLp.resonance = 1;          // tame the fizz of the scream lead
var guitar = addTrack("Feature guitar", bus.mixerGroup);
var gHp = guitar.mixerGroup.effectChain.addEffect("HighpassEffect");
gHp.cutoff = 110; gHp.resonance = 1;           // keep it off the bass

var tracks = { trumpet: trumpet, whistle: whistle, mallets: mallets, perc: perc, synth: synth, guitar: guitar };
var count = 0;
NOTES.forEach(function (n) {
    var s = tracks[n.track].addSound(ev.timeline, "SingleSound", n.start, n.length);
    s.audioFile = audioFile(n.file);
    s.pitch = n.pitch;
    s.volume = n.volume;
    s.addParameterCondition(preset, n.loop - 0.4, n.loop + 0.4);
    var env = project.create("ADSRModulator");
    env.nameOfPropertyBeingModulated = "volume";
    env.attackTime = 2; env.releaseTime = 70;
    env.objectBeingModulated = s;
    count++;
});
log("placed " + count + " notes");
project.save();
log("saved");
"""


CS_PATH = os.path.join(HERE, "..", "..", "Assets", "Scripts", "Audio", "FeatureLoopNotes.g.cs")


def write_cs():
    """Loop table and note onsets per loop for Unity (one onset per instrument per beat; chords count voices).
    FeatureSelector reads the loop table from here, so Python stays the only place a loop is defined."""
    assert [l["id"] for l in LOOPS] == list(range(1, len(LOOPS) + 1)), "loop ids must be 1..N (ById is indexed by id)"
    loop_lines = []
    note_lines = []
    for loop_ in LOOPS:
        flags = 0
        onsets = {}
        for inst, beat, midi, *_ in loop_["notes"]:
            flags |= FLAGS[inst]
            key = (beat, FLAGS[inst])
            midi = 72 if inst == "perc" else midi      # percussion has no pitch: middle of the range
            top, voices = onsets.get(key, (midi, 0))
            onsets[key] = (max(top, midi), voices + 1)
        loop_lines.append(
            f"        new FeatureSelector.Loop({loop_['id']}, \"{loop_['name']}\", {loop_['startBar']}, {loop_['bars']}, "
            f"(FeatureSelector.Instrument){flags}),")
        entries = ", ".join(
            f"new Note({beat}f, {midi}, {voices}, (FeatureSelector.Instrument){flag})"
            for (beat, flag), (midi, voices) in sorted(onsets.items()))
        note_lines.append(f"        /* {loop_['id']} {loop_['name']} */ new[] {{ {entries} }},")
    cs = (
        "// Generated by FMOD/Tools/feature_loops.py - edit the Python file, not this one.\n"
        "// The feature loop table and each loop's note onsets, so selection and visuals match what FMOD plays.\n"
        "public static class FeatureLoopNotes\n"
        "{\n"
        "    public readonly struct Note\n"
        "    {\n"
        "        /// <summary>Beat within the 4-bar phrase (0..16).</summary>\n"
        "        public readonly float Beat;\n"
        "        /// <summary>Highest MIDI note sounding on this beat for this instrument (percussion uses 72).</summary>\n"
        "        public readonly int Midi;\n"
        "        /// <summary>Notes stacked on this beat (a brass stab has 3).</summary>\n"
        "        public readonly int Voices;\n"
        "        /// <summary>Which feature instrument plays it.</summary>\n"
        "        public readonly FeatureSelector.Instrument Instrument;\n\n"
        "        public Note(float beat, int midi, int voices, FeatureSelector.Instrument instrument)\n"
        "        {\n"
        "            Beat = beat; Midi = midi; Voices = voices; Instrument = instrument;\n"
        "        }\n"
        "    }\n\n"
        "    /// <summary>Every feature loop, in id order (FMOD FeatureLoop parameter value = Id).</summary>\n"
        "    public static readonly FeatureSelector.Loop[] Loops =\n"
        "    {\n" + "\n".join(loop_lines) + "\n"
        "    };\n\n"
        "    /// <summary>Indexed by FeatureLoop id (0 = none).</summary>\n"
        "    public static readonly Note[][] ById =\n"
        "    {\n"
        "        null,\n" + "\n".join(note_lines) + "\n"
        "    };\n"
        "}\n")
    with open(CS_PATH, "w", encoding="utf-8", newline="\r\n") as f:
        f.write(cs)


def main():
    notes = build()
    js = JS_TEMPLATE.replace("__NOTES__", json.dumps(notes)).replace("__LOOP_COUNT__", str(len(LOOPS)))
    with open(os.path.join(HERE, "feature_loops.js"), "w", encoding="utf-8") as f:
        f.write(js)
    write_cs()
    for l in LOOPS:
        inst = sorted({n[0] for n in l["notes"]})
        print(f'{l["id"]:2d}: {l["name"]:18s} bars {l["startBar"] + 1}-{l["startBar"] + l["bars"]}  {"+".join(inst)}  {len(l["notes"])} notes')
    print(f"{len(notes)} instruments -> feature_loops.js")


if __name__ == "__main__":
    main()
