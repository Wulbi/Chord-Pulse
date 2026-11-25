"""
Global mixer: buses, ducking and the global mix parameters, in one place (spec 4-2 / 4-3 of
Docs/superpowers/specs/2026-11-24-fmod-scheduled-layers-design.md).

Writes mixer.js next to this file. Run order (Docs/AdaptiveAudioPlan.md 8-10): instruments.js -> mixer.js ->
cleanup_music.js, FMOD Studio closed, then build as a SEPARATE command (the CLI can crash after saving):
    python -I FMOD/Tools/mixer.py
    fmodstudiocl.exe -script FMOD/Tools/mixer.js FMOD/ChordPulse/ChordPulse.fspro
    fmodstudiocl.exe -build FMOD/ChordPulse/ChordPulse.fspro

Why global: instrument one-shots (event:/Inst/<BusFolder>/<Name>) bypass the music event's internal groups, so the
processing that must reach them lives on global buses, automated by GLOBAL parameters (a bus can only be automated by
global parameters). Structure (signal order, every effect before the bus fader):

    bus:/Music Bed            [low-pass (Danger), 3-EQ low band (Hurt), gain (Hurt)]
      bus:/Music Bed/Layers   [limiter], fader -8                                     <- bed / drum / fever one-shots
      bus:/Music Bed/Harmony  [compressor keyed by bus:/Feature, compressor keyed by bus:/Kill Chords], fader -8
    bus:/Feature              [key, compressor keyed by bus:/Kill Chords, reverb, limiter], fader -8
    bus:/Kill Chords          [key, compressor, limiter], fader -8                    <- kill-chord one-shots

The -8 dB is the old music event master's level (the timeline groups used to go through it). Two Harmony compressors,
not one: the Feature duck keeps its own values (-32 dB 4:1, was in the event's Harmony group) and the kill duck its own
(-20 dB 2:1, was on return:/Harmony Duck).

Since stages 3-8 Task 15b every layer is an event:/Inst/* one-shot: the legacy timeline tracks, the event's Harmony /
Feature groups, return:/Harmony Duck, return:/Feature Duck and the global LegacyLayers were deleted (cleanup_music.py),
so this script no longer creates or touches any of them. While event:/Music/ChordPulseGame still exists (it only holds
the Spike ref track), its output stays routed to bus:/Music Bed.

Parameters: Tension, Danger, Hurt are global (same presets, same GUIDs). Unity sets them on the Studio system.

The buses named here are owned by this script: it rebuilds their effect chains (effects, automation, sidechain keys,
faders) to match the tables above, so a hand edit of those chains in FMOD Studio is discarded on the next run.

Idempotent: every write is guarded by "only when different"; a second run logs created 0, updated 0, added 0, removed 0
and leaves FMOD/ChordPulse/Metadata unchanged. Layer tasks add their own sub-buses to SUB_BUSES (rule: only tracks with
their own effects or automation get a sub-bus; the rest use the family bus).
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import bed_combo  # noqa: E402
import bed_tension  # noqa: E402

JS_PATH = os.path.join(HERE, "mixer.js")
EVENT_MASTER_DB = -8.0   # the old music event master volume; the timeline groups went through it

# Global parameters: name -> (type, min, max, initial, seek up, seek down). Seek speeds are the values the bed scripts set
# (bed_tension.py, bed_combo.py); 0 = no seek.
PARAMETERS = {
    "Tension": ("User", 0, 1, 0, 2.0, 0.5),
    "Danger": ("User", 0, 1, 0, 3.0, 1.0),
    "Hurt": ("User", 0, 1, 0, 20.0, 1.6),
}


def _points(pairs):
    return [[float(p), float(v)] for p, v in pairs]


KILL_KEY = {"bus": "Kill Chords", "index": 0}      # the Sidechain effect at position 0 of that bus
FEATURE_KEY = {"bus": "Feature", "index": 0}

# path -> (parent path or None = master bus, fader dB, effects in signal order)
BUSES = [
    ("Kill Chords", None, EVENT_MASTER_DB, [
        {"entity": "Sidechain"},
        {"entity": "CompressorEffect", "props": {"threshold": -20, "ratio": 3, "attackTime": 3, "releaseTime": 120, "gain": 2}},
        {"entity": "LimiterEffect", "props": {"ceiling": -3, "release": 40}},
    ]),
    ("Feature", None, EVENT_MASTER_DB, [
        {"entity": "Sidechain"},
        {"entity": "CompressorEffect", "props": {"threshold": -24, "ratio": 3, "attackTime": 3, "releaseTime": 140, "gain": 0},
         "keys": [KILL_KEY]},
        {"entity": "SFXReverbEffect", "props": {"decayTime": 900, "earlyDelay": 10, "lateDelay": 20, "highCut": 6000,
                                                 "wetLevel": -14, "dryLevel": 0}},
        {"entity": "LimiterEffect", "props": {"ceiling": -4, "release": 30}},
    ]),
    ("Music Bed", None, 0.0, [
        {"entity": "LowpassEffect", "props": {"cutoff": 20000, "resonance": 1},
         "auto": [{"property": "cutoff", "parameter": "Danger", "points": _points(bed_tension.DANGER_CUTOFF)}]},
        {"entity": "ThreeEQEffect", "props": {"lowGain": 0, "midGain": 0, "highGain": 0, "lowCrossover": 300,
                                              "highCrossover": 4000, "crossoverSlope": 1},
         "auto": [{"property": "lowGain", "parameter": "Hurt", "points": _points(bed_combo.HURT_LOW_CUT)}]},
        {"entity": "GainEffect", "props": {"gain": 0},
         "auto": [{"property": "gain", "parameter": "Hurt", "points": _points(bed_combo.HURT_GAIN)}]},
    ]),
    ("Music Bed/Harmony", "Music Bed", EVENT_MASTER_DB, [
        # Feature duck (the values of the old in-event Harmony compressor)
        {"entity": "CompressorEffect", "props": {"threshold": -32, "ratio": 4, "attackTime": 5, "releaseTime": 180, "gain": 0},
         "keys": [FEATURE_KEY]},
        # Kill duck: was -32 / 4:1 / 5 / 180 in the event; the one-shot chords ring their full length, so retuned on the
        # duck capture to ~3.5 dB (Docs/Captures/README.md, Task 6 fix round 1).
        {"entity": "CompressorEffect", "props": {"threshold": -20, "ratio": 2, "attackTime": 5, "releaseTime": 60, "gain": 0},
         "keys": [KILL_KEY]},
    ]),
]
# Layer sub-buses, added by the layer tasks: same shape as BUSES (path, parent, fader dB, effects), parents first.
# Task 9 (background bed, pattern_bed.py): a one-shot passes the same gain and effects, in the same order, as its timeline
# track: track effects / Tension volume curve (own sub-bus) -> [event master: limiter, -8 dB] (Music Bed/Layers) or
# [Harmony compressors, -8 dB] (Music Bed/Harmony) -> Music Bed (Danger / Hurt). Tracks with no effect and no automation
# use the family bus (Music Bed/Layers or Music Bed/Harmony). Tension curves are bed_tension.CURVES on a gain effect,
# automated by the GLOBAL Tension (the track volume automation they replace went from -80 to 0 dB the same way).
# While layers are split between the timeline and one-shots, the event master limiter and the Layers limiter act in
# parallel on their own parts instead of once on the sum.
EVENT_MASTER_LIMITER = {"ceiling": -1, "release": 30}   # the music event master's limiter (event XML), before its -8 dB


def _hp(hz):
    return {"entity": "HighpassEffect", "props": {"cutoff": hz, "resonance": 1}}


def _lp(hz):
    return {"entity": "LowpassEffect", "props": {"cutoff": hz, "resonance": 1}}


def _tension_gain(curve):
    return {"entity": "GainEffect", "props": {"gain": 0},
            "auto": [{"property": "gain", "parameter": "Tension", "points": _points(bed_tension.CURVES[curve])}]}


SUB_BUSES = [
    ("Music Bed/Layers", "Music Bed", EVENT_MASTER_DB, [{"entity": "LimiterEffect", "props": EVENT_MASTER_LIMITER}]),
    ("Music Bed/Layers/Bass", "Music Bed/Layers", 0.0, [_tension_gain("bass")]),
    ("Music Bed/Layers/Tension Strings", "Music Bed/Layers", 0.0, [_hp(140), _tension_gain("strings")]),
    ("Music Bed/Layers/Tension Sub", "Music Bed/Layers", 0.0, [_tension_gain("sub")]),
    ("Music Bed/Layers/Tension Acid", "Music Bed/Layers", 0.0, [_lp(3500), _tension_gain("acid")]),
    # Task 12 (drums, pattern_drums.py): the Latin perc track's 90 Hz high-pass; the other drum tracks have no effect.
    ("Music Bed/Layers/Latin Perc", "Music Bed/Layers", 0.0, [_hp(90)]),
    ("Music Bed/Harmony/Combo Pad", "Music Bed/Harmony", 0.0, [_hp(300)]),
    ("Music Bed/Harmony/Latin Marimba", "Music Bed/Harmony", 0.0, [_hp(200)]),
    ("Music Bed/Harmony/Funk Keys", "Music Bed/Harmony", 0.0, [_hp(250)]),
    ("Music Bed/Harmony/Funk Guitar", "Music Bed/Harmony", 0.0, [_hp(180)]),
    ("Music Bed/Harmony/Dreamy Choir", "Music Bed/Harmony", 0.0, [_hp(150)]),
    ("Music Bed/Harmony/Dreamy Bells", "Music Bed/Harmony", 0.0, [_hp(300)]),
    # Task 13 (feature loops, pattern_feature.py): the six old Feature tracks' filters, before bus:/Feature (its key,
    # kill-keyed compressor, reverb, limiter and -8 dB). The whistle track's -1 dB fader is folded into the notes' dB.
    ("Feature/Trumpet", "Feature", 0.0, [_hp(180)]),
    ("Feature/Whistle", "Feature", 0.0, [_hp(700), _lp(5500)]),
    ("Feature/Mallets", "Feature", 0.0, [_hp(150)]),
    ("Feature/Perc", "Feature", 0.0, [_hp(120)]),
    ("Feature/Synth", "Feature", 0.0, [_hp(200), _lp(6500)]),
    ("Feature/Guitar", "Feature", 0.0, [_hp(110)]),
]

# C# OutputBus member -> bus path (instruments.py generates the enum and InstrumentTable.BusPath from this).
# MusicBed is bus:/Music Bed/Layers (one-shots need the event master's limiter and -8 dB that timeline tracks get on their
# way to bus:/Music Bed); the other sub-buses are named after their last path part without spaces.
OUTPUT_BUSES = [("KillChords", "Kill Chords"), ("MusicBed", "Music Bed/Layers"), ("Harmony", "Music Bed/Harmony"),
                ("Feature", "Feature")]
OUTPUT_BUSES += [(p.split("/")[-1].replace(" ", ""), p) for p, _, _, _ in SUB_BUSES if p not in dict(map(reversed, OUTPUT_BUSES))]

JS_TEMPLATE = r"""// Generated by FMOD/Tools/mixer.py - edit the Python file, not this one.
// Global mixer: buses, ducking and global parameters (see mixer.py for the structure).
var PARAMETERS = __PARAMETERS__;
var BUSES = __BUSES__;
var EVENT_BUS = "Music Bed";
var EPS = 0.001;

var counts = { created: 0, updated: 0, added: 0, removed: 0 };
function log(m) { console.log("[mixer] " + m); }
function differs(a, b) { return Math.abs(a - b) > EPS; }
var project = studio.project;
var ev = project.lookup("event:/Music/ChordPulseGame");   // optional: only its output routing is set

// --- 1. global parameters ---
var params = {};
Object.keys(PARAMETERS).forEach(function (name) {
    var d = PARAMETERS[name];   // [type, min, max, initial, seek up, seek down]
    var type = project.parameterType[d[0]];
    var preset = project.lookup("parameter:/" + name);
    if (!preset) {
        project.workspace.addGameParameter({ name: name, type: type, min: d[1], max: d[2] });
        preset = project.lookup("parameter:/" + name);
        if (!preset) throw "could not create parameter " + name;
        counts.created++;
        log("created parameter " + name);
    }
    var p = preset.parameter, changed = false;
    if (p.parameterType !== type) { p.parameterType = type; changed = true; }
    if (differs(p.minimum, d[1])) { p.minimum = d[1]; changed = true; }
    if (differs(p.maximum, d[2])) { p.maximum = d[2]; changed = true; }
    if (p.initialValue === undefined || p.initialValue === null || differs(p.initialValue, d[3])) { p.initialValue = d[3]; changed = true; }
    var asym = d[4] !== d[5];
    if (p.seekSpeedAsymmetric !== asym) { p.seekSpeedAsymmetric = asym; changed = true; }
    if (differs(p.seekSpeed, d[4])) { p.seekSpeed = d[4]; changed = true; }
    if (asym && differs(p.seekSpeedDescending, d[5])) { p.seekSpeedDescending = d[5]; changed = true; }
    if (p.isGlobal !== true) { p.isGlobal = true; changed = true; log(name + " is now global"); }
    if (changed) counts.updated++;
    params[name] = p;
});

// --- 2. buses ---
var master = project.workspace.mixer.masterBus;
var buses = {};
function busFor(path) {
    if (buses[path]) return buses[path];
    var b = project.lookup("bus:/" + path);
    if (!b) {
        var cut = path.lastIndexOf("/");
        b = project.create("MixerGroup");
        b.name = path.substr(cut + 1);
        b.output = cut < 0 ? master : busFor(path.substr(0, cut));
        counts.created++;
        log("created bus " + path);
    }
    buses[path] = b;
    return b;
}
function effectsOf(bus) { return bus.effectChain.effects.filter(function (e) { return e.entity !== "MixerBusFader"; }); }
function fader(bus) { return bus.effectChain.effects.filter(function (e) { return e.entity === "MixerBusFader"; })[0]; }
// Adds an effect right before the bus fader (addEffect alone appends after it, i.e. post-fader).
function addPreFader(bus, entity) {
    var chain = bus.effectChain;
    var e = chain.addEffect(entity);
    chain.relationships.effects.remove(e);
    var at = -1, f = fader(bus);
    chain.effects.forEach(function (x, i) { if (x.id === f.id) at = i; });
    chain.relationships.effects.insert(at, e);
    return e;
}
function faderIsLast(bus) {
    var all = bus.effectChain.effects;
    return all.length > 0 && all[all.length - 1].entity === "MixerBusFader";
}
function samePoints(curve, points) {
    var have = curve.automationPoints;
    if (have.length !== points.length) return false;
    var sorted = have.slice().sort(function (a, b) { return a.position - b.position; });
    for (var i = 0; i < points.length; i++) {
        var p = sorted[i];
        if (differs(p.position, points[i][0]) || differs(p.value, points[i][1]) || p.curveShape !== 0 || p.isSCurve) return false;
    }
    return true;
}
// Automation of one effect: exactly the wanted (property, parameter, points); anything else is rebuilt or removed.
function syncAutomation(e, wanted) {
    var want = {};
    (wanted || []).forEach(function (w) { want[w.property] = w; });
    var seen = {};
    e.automators.slice().forEach(function (a) {
        var w = want[a.nameOfPropertyBeingAutomated];
        var curves = a.automationCurves;
        var ok = w && !seen[w.property] && curves.length === 1 && curves[0].parameter &&
            curves[0].parameter.id === params[w.parameter].id && samePoints(curves[0], w.points);
        if (ok) { seen[w.property] = true; return; }
        project.deleteObject(a);
        counts.removed++;
    });
    Object.keys(want).forEach(function (k) {
        if (seen[k]) return;
        var w = want[k];
        var curve = e.addAutomator(w.property).addAutomationCurve(params[w.parameter]);
        w.points.forEach(function (pt) { curve.addAutomationPoint(pt[0], pt[1]); });
        counts.added++;
    });
}
function syncProps(e, props) {
    var changed = false;
    Object.keys(props || {}).forEach(function (k) {
        if (typeof e[k] !== "number" || differs(e[k], props[k])) { e[k] = props[k]; changed = true; }
    });
    return changed;
}

var chains = {};
BUSES.forEach(function (spec) {
    var path = spec[0], parent = spec[1], volume = spec[2], wanted = spec[3];
    var bus = busFor(path);
    var out = parent === null ? master : busFor(parent);
    if (!bus.output || bus.output.id !== out.id) { bus.output = out; counts.updated++; log(path + " -> " + (parent || "master")); }
    if (differs(bus.volume, volume)) { bus.volume = volume; counts.updated++; }
    var have = effectsOf(bus);
    var match = have.length === wanted.length && faderIsLast(bus);
    for (var i = 0; match && i < wanted.length; i++) if (have[i].entity !== wanted[i].entity) match = false;
    if (!match) {
        have.forEach(function (e) { project.deleteObject(e); counts.removed++; });
        if (!faderIsLast(bus)) {
            var f = fader(bus);
            bus.effectChain.relationships.effects.remove(f);
            bus.effectChain.relationships.effects.add(f);
        }
        have = wanted.map(function (w) { counts.added++; return addPreFader(bus, w.entity); });
        log(path + ": effects rebuilt");
    }
    wanted.forEach(function (w, i) {
        if (syncProps(have[i], w.props)) counts.updated++;
        syncAutomation(have[i], w.auto);
    });
    chains[path] = have;
});
// sidechain keys, once every chain exists
BUSES.forEach(function (spec) {
    spec[3].forEach(function (w, i) {
        if (w.entity !== "CompressorEffect") return;
        var e = chains[spec[0]][i];
        var want = (w.keys || []).map(function (k) { return chains[k.bus][k.index]; });
        var have = e.sidechains;
        var same = have.length === want.length;
        want.forEach(function (k) { if (!have.some(function (h) { return h.id === k.id; })) same = false; });
        if (same) return;
        have.slice().forEach(function (h) { e.relationships.sidechains.remove(h); });
        want.forEach(function (k) { e.relationships.sidechains.add(k); });
        counts.updated++;
        log(spec[0] + ": compressor " + i + " keys relinked");
    });
});

// --- 3. the music event (Spike ref only, while it exists): output to bus:/Music Bed ---
if (ev && (!ev.mixerInput.output || ev.mixerInput.output.id !== buses[EVENT_BUS].id)) {
    ev.mixerInput.output = buses[EVENT_BUS];
    counts.updated++;
    log("event output -> bus:/" + EVENT_BUS);
}

function describe(b) { return b.effectChain.effects.map(function (e) { return e.entity; }).join(", ") + " @ " + b.volume + " dB"; }
Object.keys(buses).forEach(function (p) { log("bus:/" + p + ": " + describe(buses[p])); });
log("created " + counts.created + ", updated " + counts.updated + ", added " + counts.added + ", removed " + counts.removed);
if (counts.created || counts.updated || counts.added || counts.removed) { project.save(); log("saved"); } else log("nothing to do");
"""


def js_text():
    params = {k: list(v) for k, v in PARAMETERS.items()}
    buses = [[p, parent, vol, fx] for p, parent, vol, fx in BUSES + SUB_BUSES]
    return (JS_TEMPLATE.replace("__PARAMETERS__", json.dumps(params))
            .replace("__BUSES__", json.dumps(buses, indent=1)))


def write_if_different(path, text):
    old = None
    if os.path.exists(path):
        with open(path, "r", encoding="utf-8", newline="") as f:
            old = f.read()
    if old == text:
        return False
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    return True


if __name__ == "__main__":
    changed = write_if_different(JS_PATH, js_text())
    print("mixer.js " + ("written" if changed else "unchanged"))
