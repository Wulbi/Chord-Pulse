"""
Adaptive bed, axis 4: arrangement styles for event:/Music/ChordPulseGame (Docs/AdaptiveAudioPlan.md 8-4).

Writes bed_style.js next to this file. Run it on the FMOD project (FMOD Studio closed) with
    fmodstudiocl.exe -script FMOD/Tools/bed_style.js FMOD/ChordPulse/ChordPulse.fspro
Run it LAST, after feature_loops / bed_tension / bed_variation / bed_combo: it puts a BedStyle condition on
notes those scripts make (re-running any of them means re-running this one). It is idempotent.

Game parameter BedStyle (one value per phrase, chosen by Unity one phrase ahead; fever is always 0):
    0  synthpop - the original kit, bass, pluck (+ their variations and combo hats)
    1  latin    - snare 2&4 -> conga slap 2&4 | closed 8th hats -> shaker | open hats (I2+) -> cowbell
                  clap (I3) -> bongo martillo | + son clave 3-2 (I1+), open conga tones on 3& and 4&
                  bass -> tumbao (next chord's root anticipated on 4) | pluck (I1+) -> marimba montuno
                  combo hats -> shaker 16ths
    2  funk     - snare 2&4 with ghost notes | closed 8th hats -> accented 16th hats | open hat on 2& 4& (I2+)
                  clap on 2&4 (I3) | bass -> AT303 rubber/pocket bass, syncopated 16ths with octave pops
                  pluck (I1+) -> clavi-synth 16th dyads | + clean guitar "chank" on 2&4 (I2+)
                  combo hats -> shaker 16ths
    3  dreamy   - only for breath phrases (Unity): soft claves on 2&4 and a whisper of shaker, sine sub
                  bass (one long note per bar), vowel-choir pad chord per bar, two formant bells per bar
Shared by all styles: kick (the visuals pulse on it), tension layers, combo pad/bells, feature loops,
kill chords, fever. The backbeat stays on 2 and 4 in every style, where the snare visuals expect it.
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

SYNTHPOP, LATIN, FUNK, DREAMY = 0, 1, 2, 3
STYLE_COUNT = 4

# Tracks that belong to the synthpop style and go quiet in other styles.
SYNTHPOP_TRACKS = ["Snare - beats 2 and 4", "Eighth-note hats", "Clap layer", "Bass - C Am F G",
                   "Offbeat pluck", "Snare variations", "Combo hats"]
# Tracks this script owns: name -> (output bus, high-pass Hz or 0)
OWN_TRACKS = {
    "Latin perc": ("bed", 90), "Latin shaker": ("bed", 0), "Latin marimba": ("harmony", 200),
    "Funk drums": ("bed", 0), "Funk keys": ("harmony", 250), "Funk guitar": ("harmony", 180),
    "Dreamy perc": ("bed", 0), "Dreamy sub": ("bed", 0), "Dreamy choir": ("harmony", 150),
    "Dreamy bells": ("harmony", 300),
}
BASS_TRACK = "Bass - C Am F G"   # latin/funk basses go here so the Tension bass/acid crossfade still applies

# Unpitched: name -> (file, peak short-term RMS dB)
PERC = {
    "shaker": ("Pallor_Shaker_Synthpop_v110.wav", -21.6),
    "cowbell": ("WorldPerc/Cowbell2_Normal_v3_rr1_Mid.wav", -21.8),
    "claves": ("WorldPerc/Claves1_Hit_v3_rr1_Mid.wav", -38.8),
    "bongo_hi": ("WorldPerc/BongoH_Hit1_v3_rr1_Mid.wav", -27.5),
    "bongo_lo": ("WorldPerc/BongoL_Hit1_v3_rr1_Mid.wav", -26.2),
    "conga": ("WorldPerc/Conga_HitN_v3_rr1_Sum.wav", -28.5),
    "conga_slap": ("WorldPerc/Quinto_HitN_v3_rr1_Sum.wav", -26.0),
    "snare": ("Pallor_Snare_Synthpop_v115.wav", -14.8),
    "hat": ("Pallor_ClosedHat_Synthpop_v110.wav", -28.5),
    "open_hat": ("Pallor_OpenHat_Synthpop_v110.wav", -19.0),
    "clap": ("Pallor_Clap_Synthpop_v110.wav", -15.9),
}
# Pitched: (file, measured root MIDI, peak short-term RMS dB)
SYNTH_BASS = [("AT101V_101Bass_C2_v100.wav", 36, None)]   # original bass: levels set as note volume directly
MARIMBA = [("Mallets/Marimba_hit_Outrigger_F3_loud_01.wav", 65, -30.7),
           ("Mallets/Marimba_hit_Outrigger_C4_loud_01.wav", 72, -33.2),
           ("Mallets/Marimba_hit_Outrigger_G4_loud_01.wav", 79, -32.2),
           ("Mallets/Marimba_hit_Outrigger_F5_loud_01.wav", 89, -37.7)]
RUBBER = [("SynthBass/AT303_Pocket_G1_v100.wav", 31, -24.8), ("SynthBass/AT303_RubberRoot_A1_v100.wav", 33, -24.0),
          ("SynthBass/AT303_Pocket_D2_v100.wav", 38, -13.8), ("SynthBass/AT303_RubberRoot_E2_v100.wav", 40, -15.8)]
# Clavi-synth: the fundamental is weak; the file named C3 sounds strongest from C5 up, so it is mapped to C5.
CLAV = [("FMKeys/Clavisynth_C3_vl3.wav", 72, -23.2)]
GUITAR = [("Guitar/Thrum_CleanNote_E3_v100.wav", 52, -11.0), ("Guitar/Thrum_CleanNote_G4_v100.wav", 55, -14.1),
          ("Guitar/Thrum_CleanNote_A3_v100.wav", 57, -14.2), ("Guitar/Thrum_CleanNote_D4_v100.wav", 62, -16.6)]
SINE_SUB = [("SynthBass/DeviceNTI_SineSub_F1_v100.wav", 29, -9.2), ("SynthBass/DeviceNTI_SineSub_C2_v100.wav", 36, -9.8)]
CHOIR = [("Synth/ATFS1_VowelChoir_C3_v90.wav", 48, -14.0), ("Synth/ATFS1_VowelChoir_G3_v90.wav", 55, -11.3),
         ("Synth/ATFS1_VowelChoir_C4_v90.wav", 60, -11.8)]
BELLS = [("Synth/ATFS1_FormantBells_C5_v100.wav", 72, -9.2), ("Synth/ATFS1_FormantBells_G5_v100.wav", 79, -9.0)]

# Per-hit loudness targets (dB), matched to the synthpop parts they replace (sample RMS + note + track volume:
# snare -35, closed hat -45.5, open hat -37, clap -36, pluck -32), then trimmed from offline renders so every
# style sits within about 0.2 dB of synthpop at each Intensity (dreamy deliberately ~3 dB softer).
TARGET = {
    "conga_slap": -35.0, "shaker": -44.0, "cowbell": -38.0, "bongo": -37.0, "conga": -38.0, "claves": -42.0,
    "marimba": -36.0, "shaker16": -47.0,
    "f_snare": -34.0, "f_ghost": -48.0, "f_hat": -46.0, "f_open": -38.0, "f_clap": -37.0, "f_bass": -25.5,
    "f_clav": -35.0, "f_guitar": -37.0,
    "d_claves": -45.0, "d_shaker": -51.0, "d_sub": -32.0, "d_choir": -39.0, "d_bells": -44.0,
}

# Per bar of the phrase (C, Am, F, G).
BARS = [
    dict(root=36, next=33, fifth=43, marimba=(67, 72, 76, 79), clav=(76, 79), guitar=(64, 67),
         choir=(60, 64, 67), bells=(76, 79)),                                     # C
    dict(root=33, next=29, fifth=40, marimba=(69, 72, 76, 81), clav=(72, 76), guitar=(64, 69),
         choir=(57, 60, 64), bells=(76, 81)),                                     # Am
    dict(root=29, next=31, fifth=36, marimba=(69, 72, 77, 81), clav=(77, 81), guitar=(65, 69),
         choir=(57, 60, 65), bells=(77, 81)),                                     # F
    dict(root=31, next=36, fifth=38, marimba=(67, 71, 74, 79), clav=(74, 79), guitar=(62, 67),
         choir=(55, 59, 62), bells=(74, 79)),                                     # G
]
CLAVE = [(0, 0.0), (0, 1.5), (0, 3.0), (1, 1.0), (1, 2.0)]   # son clave 3-2 over two bars: (bar % 2, beat)

I_ALL, I_1, I_2, I_3 = None, (0.5, 3.0), (1.5, 3.0), (2.5, 3.0)


def vol(target, rms, accent=0.0):
    return round(max(-60.0, min(target - rms + accent, 10.0)), 2)


def build():
    notes = []

    def add(style, track, file, pitch, start, length, volume, intensity=I_ALL, combo=None):
        notes.append(dict(style=style, track=track, file=file, pitch=pitch, start=round(start, 6),
                          length=round(length, 6), volume=volume, intensity=intensity, combo=combo))

    def perc(style, track, name, target, start, length, intensity=I_ALL, combo=None, accent=0.0):
        file, rms = PERC[name]
        add(style, track, file, 0, start, length, vol(target, rms, accent), intensity, combo)

    def tone(style, track, table, midi, target, start, length, intensity=I_ALL, accent=0.0):
        s = min(table, key=lambda x: (abs(midi - x[1]), -x[1]))
        assert -12 <= midi - s[1] <= 12, (track, midi)
        add(style, track, s[0], midi - s[1], start, length, vol(target, s[2], accent), intensity)

    for start, bpm in SECTIONS:
        beat = 60.0 / bpm
        for bar in range(BARS_PER_SECTION):
            t0 = start + bar * 4 * beat
            pb = bar % 4
            last_bar = pb == 3          # nothing on the phrase's final 16th (the style may switch there)
            at = lambda x: t0 + x * beat
            b = BARS[pb]

            # ===== latin =====
            for x in (1.0, 3.0):
                perc(LATIN, "Latin perc", "conga_slap", TARGET["conga_slap"], at(x), 0.5 * beat)
            perc(LATIN, "Latin perc", "conga", TARGET["conga"], at(2.5), 0.5 * beat, accent=-3.0)
            perc(LATIN, "Latin perc", "conga", TARGET["conga"], at(3.5), 0.5 * beat)
            for i in range(8):
                perc(LATIN, "Latin shaker", "shaker", TARGET["shaker"], at(i * 0.5), 0.2 * beat,
                     accent=0.0 if i % 2 else -2.0)
            for i in range(4):
                perc(LATIN, "Latin perc", "cowbell", TARGET["cowbell"], at(i), 0.3 * beat, I_2,
                     accent=0.0 if i == 0 else -3.0)
                perc(LATIN, "Latin perc", "bongo_hi" if i % 2 else "bongo_lo", TARGET["bongo"], at(i + 0.5),
                     0.3 * beat, I_3, accent=0.0 if i == 3 else -2.0)
            for clave_bar, x in CLAVE:
                if bar % 2 == clave_bar:
                    perc(LATIN, "Latin perc", "claves", TARGET["claves"], at(x), 0.3 * beat, I_1)
            for i in range(4):
                for sub in (0.25, 0.75):
                    for style in (LATIN, FUNK):
                        perc(style, "Latin shaker", "shaker", TARGET["shaker16"], at(i + sub), 0.15 * beat,
                             combo=1, accent=-2.0 if sub == 0.25 else 0.0)
            for x, midi, length, v in ((0.0, b["root"], 0.45, -11.0), (1.5, b["root"], 1.3, -9.0),
                                       (3.0, b["next"], 0.9, -9.0)):
                add(LATIN, BASS_TRACK, SYNTH_BASS[0][0], midi - 36, at(x), length * beat, v)
            low, mid, high, top = b["marimba"]
            for x, midi, acc in ((0.0, low, 0.0), (0.5, top, -2.0), (1.0, high, -1.0), (1.5, top, -2.0),
                                 (2.5, mid, -1.0), (3.0, high, -1.0), (3.5, top, -2.0)):
                tone(LATIN, "Latin marimba", MARIMBA, midi, TARGET["marimba"], at(x), 0.6 * beat, I_1, acc)

            # ===== funk =====
            for x in (1.0, 3.0):
                perc(FUNK, "Funk drums", "snare", TARGET["f_snare"], at(x), 0.6 * beat)
                perc(FUNK, "Funk drums", "clap", TARGET["f_clap"], at(x), 0.6 * beat, I_3)
            for x in (0.75, 1.75, 2.25) + (() if last_bar else (3.75,)):
                perc(FUNK, "Funk drums", "snare", TARGET["f_ghost"], at(x), 0.25 * beat)
            for i in range(16):
                x = i * 0.25
                if last_bar and i == 15:
                    continue
                accent = (0.0, -6.0, -3.0, -6.0)[i % 4]
                if x in (1.5, 3.5):
                    perc(FUNK, "Funk drums", "open_hat", TARGET["f_open"], at(x), 0.45 * beat, I_2)
                perc(FUNK, "Funk drums", "hat", TARGET["f_hat"], at(x), 0.15 * beat, accent=accent)
            # Bass: syncopated 16ths, octave pops, fifth on 3&, root again on 4&.
            for x, midi, length, acc in ((0.0, b["root"], 0.4, 0.0), (0.75, b["root"], 0.2, -3.0),
                                         (1.25, b["root"] + 12, 0.2, -2.0), (1.5, b["root"], 0.4, -1.0),
                                         (2.5, b["fifth"], 0.4, -1.0), (2.75, b["root"] + 12, 0.2, -2.0),
                                         (3.25, b["root"], 0.2, -3.0), (3.5, b["fifth"], 0.4, -1.0)):
                tone(FUNK, BASS_TRACK, RUBBER, midi, TARGET["f_bass"], at(x), length * beat, accent=acc)
            # Clav: 16th dyads around the backbeat (Intensity 1+, like the pluck).
            for x, acc in ((0.5, 0.0), (0.75, -3.0), (1.5, -1.0), (2.25, -2.0), (2.5, 0.0), (3.5, -1.0)):
                for m in b["clav"]:
                    tone(FUNK, "Funk keys", CLAV, m, TARGET["f_clav"], at(x), 0.15 * beat, I_1, acc - 3.0)
            # Guitar "chank" on 2 and 4 (Intensity 2+).
            for x in (1.0, 3.0):
                for m in b["guitar"]:
                    tone(FUNK, "Funk guitar", GUITAR, m, TARGET["f_guitar"], at(x) - 0.025, 0.2 * beat, I_2, -3.0)

            # ===== dreamy (breath phrases) =====
            for x in (1.0, 3.0):
                perc(DREAMY, "Dreamy perc", "claves", TARGET["d_claves"], at(x), 0.3 * beat)
            for i in range(8):
                perc(DREAMY, "Dreamy perc", "shaker", TARGET["d_shaker"], at(i * 0.5), 0.2 * beat,
                     accent=0.0 if i % 2 else -3.0)
            tone(DREAMY, "Dreamy sub", SINE_SUB, b["root"], TARGET["d_sub"], at(0.0), 3.8 * beat)
            for m in b["choir"]:
                tone(DREAMY, "Dreamy choir", CHOIR, m, TARGET["d_choir"], at(0.0) if pb == 0 else at(0.0) - 0.06,
                     4.0 * beat)
            for x, m in zip((0.0, 2.0), b["bells"]):
                tone(DREAMY, "Dreamy bells", BELLS, m, TARGET["d_bells"], at(x), 1.8 * beat, accent=0.0 if x == 0 else -3.0)
    for n in notes:
        n["start"] = max(0.0, n["start"])
    return notes


JS_TEMPLATE = r"""// Generated by FMOD/Tools/bed_style.py - edit the Python file, not this one.
var NOTES = __NOTES__;
var SYNTHPOP_TRACKS = __SYNTHPOP__;
var OWN_TRACKS = __OWN__;
var BASS_TRACK = __BASS__;
var STYLE_MAX = __MAX__;

function log(m) { console.log("[style] " + m); }
var project = studio.project;
var ev = project.lookup("event:/Music/ChordPulseGame");
var assetsRoot = project.filePath.replace(/[^\/\\]+$/, "") + "Assets/";

var style = project.lookup("parameter:/BedStyle");
if (!style) {
    project.workspace.addGameParameter({ name: "BedStyle", type: project.parameterType.User, min: 0, max: STYLE_MAX });
    style = project.lookup("parameter:/BedStyle");
    log("created parameter BedStyle");
}
style.parameter.minimum = 0; style.parameter.maximum = STYLE_MAX; style.parameter.initialValue = 0;
var has = false;
ev.parameters.forEach(function (x) { if (x.preset && (x.preset.id === style.parameter.id || x.preset.id === style.id)) has = true; });
if (!has) { ev.addGameParameter(style); log("added BedStyle to event"); }
var intensity = project.lookup("parameter:/Intensity");
var combo = project.lookup("parameter:/Combo");

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

// --- undo an earlier run: our tracks, and the non-synthpop notes we put on the bass track ---
var old = [];
ev.groupTracks.forEach(function (t) { if (OWN_TRACKS[t.mixerGroup.name]) old.push(t); });
old.forEach(function (t) { project.deleteObject(t); });
var bass = groupByName(BASS_TRACK);
var removedBass = 0;
bass.modules.slice().forEach(function (s) {
    var c = styleCondition(s);
    if (c && c.minimum > 0) { project.deleteObject(s); removedBass++; }
});
if (old.length || removedBass) log("removed " + old.length + " style tracks and " + removedBass + " style bass notes");

// --- synthpop-only tracks: every note (including other scripts' variations) plays only in style 0 ---
SYNTHPOP_TRACKS.forEach(function (name) {
    var t = groupByName(name);
    if (!t) { log("no track " + name + " (skipped)"); return; }
    var n = 0;
    t.modules.forEach(function (s) {
        var c = styleCondition(s);
        if (c) project.deleteObject(c);
        s.addParameterCondition(style, -0.4, 0.4);
        n++;
    });
    log("synthpop only: " + name + " (" + n + ")");
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
var buses = { bed: groupByName("Music Bed").mixerGroup, harmony: groupByName("Harmony").mixerGroup };
var tracks = {};
tracks[BASS_TRACK] = bass;
Object.keys(OWN_TRACKS).forEach(function (name) {
    var spec = OWN_TRACKS[name];
    var t = project.create("GroupTrack");
    t.mixerGroup.name = name;
    t.mixerGroup.output = buses[spec[0]];
    ev.relationships.groupTracks.add(t);
    if (spec[1] > 0) {
        var hp = t.mixerGroup.effectChain.addEffect("HighpassEffect");
        hp.cutoff = spec[1]; hp.resonance = 1;
    }
    tracks[name] = t;
});

var counts = {};
NOTES.forEach(function (n) {
    var s = tracks[n.track].addSound(ev.timeline, "SingleSound", n.start, n.length);
    s.audioFile = audioFile(n.file);
    s.pitch = n.pitch;
    s.volume = n.volume;
    s.addParameterCondition(style, n.style - 0.4, n.style + 0.4);
    if (n.intensity) s.addParameterCondition(intensity, n.intensity[0], n.intensity[1]);
    if (n.combo) s.addParameterCondition(combo, n.combo - 0.4, 3.4);
    if (n.track === BASS_TRACK || n.track === "Dreamy choir" || n.track === "Dreamy sub") {
        var env = project.create("ADSRModulator");
        env.nameOfPropertyBeingModulated = "volume";
        var slow = n.track !== BASS_TRACK;
        env.attackTime = slow ? 200 : 2; env.releaseTime = slow ? 600 : 60;
        env.objectBeingModulated = s;
    }
    counts[n.style] = (counts[n.style] || 0) + 1;
});
log("placed notes per style: " + JSON.stringify(counts));
project.save();
log("saved");
"""


def main():
    notes = build()
    js = (JS_TEMPLATE.replace("__NOTES__", json.dumps(notes)).replace("__SYNTHPOP__", json.dumps(SYNTHPOP_TRACKS))
          .replace("__OWN__", json.dumps(OWN_TRACKS)).replace("__BASS__", json.dumps(BASS_TRACK))
          .replace("__MAX__", str(STYLE_COUNT - 1)))
    with open(os.path.join(HERE, "bed_style.js"), "w", encoding="utf-8") as f:
        f.write(js)
    counts = {}
    for n in notes:
        counts[n["track"]] = counts.get(n["track"], 0) + 1
    print(counts, "->", "bed_style.js")


if __name__ == "__main__":
    main()
