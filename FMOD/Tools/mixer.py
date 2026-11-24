"""
Global mixer: buses, returns, ducking and the global mix parameters, in one place (spec 4-2 / 4-3 of
Docs/superpowers/specs/2026-11-24-fmod-scheduled-layers-design.md).

Writes mixer.js next to this file. Run order (Docs/AdaptiveAudioPlan.md 8-10): ... bed_* -> instruments.js -> mixer.js ->
kill_oneshots.js, FMOD Studio closed, then build as a SEPARATE command (the CLI can crash after saving):
    python -I FMOD/Tools/mixer.py
    fmodstudiocl.exe -script FMOD/Tools/mixer.js FMOD/ChordPulse/ChordPulse.fspro
    fmodstudiocl.exe -build FMOD/ChordPulse/ChordPulse.fspro

Why global: instrument one-shots (event:/Inst/<BusFolder>/<Name>) bypass the music event's internal groups, so the
processing that must reach them lives on global buses, automated by GLOBAL parameters (a bus can only be automated by
global parameters). Structure (signal order, every effect before the bus fader):

    bus:/Music Bed            [low-pass (Danger), 3-EQ low band (Hurt), gain (Hurt)]   <- the music event's output
      bus:/Music Bed/Harmony  [compressor keyed by bus:/Feature, compressor keyed by bus:/Kill Chords], fader -8
        <- return:/Harmony Duck (no effects, 0 dB) <- pre-fader send of the event's Harmony group
    bus:/Feature              [key, compressor keyed by bus:/Kill Chords, reverb, limiter], fader -8
        <- return:/Feature Duck (no effects, 0 dB) <- pre-fader send of the event's Feature group
    bus:/Kill Chords          [key, compressor, limiter], fader -8                    <- kill-chord one-shots

The -8 dB is the music event master's level (the groups used to go through it). The returns stay (the timeline Harmony /
Feature tracks still reach the mixer through them) but only route; the compressors sit after them on the buses with the
fader moved there too, so every compressor sees the same input level as before. Two Harmony compressors, not one: the
Feature duck keeps its own values (-32 dB 4:1, was in the event's Harmony group) and the kill duck its own
(-20 dB 2:1, was on return:/Harmony Duck).

In the event: the output goes to bus:/Music Bed; the Music Bed group loses its Danger / Hurt effects (now on the bus);
the Harmony group keeps only its MusicMix gain and the send (its copy of the Music Bed chain and its Feature-keyed
compressor move to the buses); the Feature group keeps only its send. MusicMix stays an event parameter.

Parameters: Tension, Danger, Hurt become global (same presets, same GUIDs, the event's automation keeps working);
LegacyLayers (0/1, discrete, global) is new. Unity must set global parameters on the Studio system, not on the event.

The buses and returns named here are owned by this script: it rebuilds their effect chains (effects, automation, sidechain
keys, faders) to match the tables above, so a hand edit of those chains in FMOD Studio is discarded on the next run.

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
EVENT_MASTER_DB = -8.0   # music event master volume; the old in-event groups and returns went through it
MUTED_DB = -80.0         # in-event group faders: they leave the event through their pre-fader send only

# Global parameters: name -> (type, min, max, initial, seek up, seek down). Seek speeds are the values the bed scripts set
# (bed_tension.py, bed_combo.py); 0 = no seek.
PARAMETERS = {
    "Tension": ("User", 0, 1, 0, 2.0, 0.5),
    "Danger": ("User", 0, 1, 0, 3.0, 1.0),
    "Hurt": ("User", 0, 1, 0, 20.0, 1.6),
    # 0/1: old timeline layers vs. scheduled one-shots (later tasks). Studio's maximum of a discrete parameter is one past
    # the last value: with maximum 1 the built bank reports maximum 0 and clamps a write of 1 to 0 (measured on a copy
    # of the project, FMOD 2.02.35); maximum 2 gives the runtime values 0 and 1.
    "LegacyLayers": ("UserDiscrete", 0, 2, 0, 0, 0),
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
# Layer sub-buses, added by the layer tasks: same shape as BUSES (path, parent, fader dB, effects).
SUB_BUSES = []

# return name -> bus path it feeds
RETURNS = {"Harmony Duck": "Music Bed/Harmony", "Feature Duck": "Feature"}

# C# OutputBus member -> bus path (instruments.py generates the enum and InstrumentTable.BusPath from this).
OUTPUT_BUSES = [("KillChords", "Kill Chords"), ("MusicBed", "Music Bed"), ("Harmony", "Music Bed/Harmony"),
                ("Feature", "Feature")] + [(p.split("/")[-1].replace(" ", ""), p) for p, _, _, _ in SUB_BUSES]

JS_TEMPLATE = r"""// Generated by FMOD/Tools/mixer.py - edit the Python file, not this one.
// Global mixer: buses, returns, ducking and global parameters (see mixer.py for the structure).
var PARAMETERS = __PARAMETERS__;
var BUSES = __BUSES__;
var RETURNS = __RETURNS__;
var EVENT_BUS = "Music Bed";
var MUTED_DB = __MUTED__;
var EPS = 0.001;

var counts = { created: 0, updated: 0, added: 0, removed: 0 };
function log(m) { console.log("[mixer] " + m); }
function differs(a, b) { return Math.abs(a - b) > EPS; }
var project = studio.project;
var ev = project.lookup("event:/Music/ChordPulseGame");
if (!ev) throw "event:/Music/ChordPulseGame not found";

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

// --- 3. returns: route only, 0 dB (the processing is on the buses they feed) ---
var returns = {};
Object.keys(RETURNS).forEach(function (name) {
    var r = null;
    project.model.MixerReturn.findInstances().forEach(function (x) { if (x.isValid && x.name === name) r = x; });
    if (!r) {
        r = project.create("MixerReturn");
        r.name = name;
        counts.created++;
        log("created return " + name);
    }
    var out = buses[RETURNS[name]];
    if (!r.output || r.output.id !== out.id) { r.output = out; counts.updated++; log("return:/" + name + " -> bus:/" + RETURNS[name]); }
    if (differs(r.volume, 0)) { r.volume = 0; counts.updated++; }
    effectsOf(r).forEach(function (e) { project.deleteObject(e); counts.removed++; });
    returns[name] = r;
});

// --- 4. the music event ---
if (!ev.mixerInput.output || ev.mixerInput.output.id !== buses[EVENT_BUS].id) {
    ev.mixerInput.output = buses[EVENT_BUS];
    counts.updated++;
    log("event output -> bus:/" + EVENT_BUS);
}
function group(name) {
    var found = null;
    ev.groupTracks.forEach(function (t) { if (t.mixerGroup.name === name) found = t.mixerGroup; });
    if (!found) throw "event group not found: " + name;
    return found;
}
function automatedBy(e, names) {
    var hit = false;
    e.automators.forEach(function (a) {
        a.automationCurves.forEach(function (c) {
            if (c.parameter && c.parameter.presetOwner && names.indexOf(c.parameter.presetOwner.name) >= 0) hit = true;
        });
    });
    return hit;
}
function moveBeforeFader(bus, e) {
    var chain = bus.effectChain;
    chain.relationships.effects.remove(e);
    var at = -1, f = fader(bus);
    chain.effects.forEach(function (x, i) { if (x.id === f.id) at = i; });
    chain.relationships.effects.insert(at, e);
}
// Brings an in-event group to [keep..., send to `ret` at 0 dB, fader at MUTED_DB]; everything else is deleted.
function sendOnly(g, keep, ret) {
    var send = null;
    effectsOf(g).forEach(function (e) {
        if (e.entity === "MixerSend" && !send && e.mixerReturn && e.mixerReturn.id === ret.id) { send = e; return; }
        if (keep(e)) return;
        project.deleteObject(e);
        counts.removed++;
    });
    if (!send) { send = addPreFader(g, "MixerSend"); send.mixerReturn = ret; counts.added++; }
    if (differs(send.level, 0)) { send.level = 0; counts.updated++; }
    var fx = effectsOf(g);
    if (fx[fx.length - 1].id !== send.id || !faderIsLast(g)) { moveBeforeFader(g, send); counts.updated++; }
    if (differs(g.volume, MUTED_DB)) { g.volume = MUTED_DB; counts.updated++; }
    return send;
}
var musicBed = group("Music Bed");
// Harmony's compressor goes first: it may be keyed by the Feature group's in-event Sidechain.
var harmony = group("Harmony");
effectsOf(harmony).forEach(function (e) {
    if (e.entity === "CompressorEffect") { project.deleteObject(e); counts.removed++; }
});
effectsOf(musicBed).forEach(function (e) {
    if (automatedBy(e, ["Danger", "Hurt"])) { project.deleteObject(e); counts.removed++; }
});
// Harmony: MusicMix gain (Harmony leaves the event before Music Bed's MusicMix fader) + send
function isMusicMixGain(e) { return e.entity === "GainEffect" && automatedBy(e, ["MusicMix"]) && !automatedBy(e, ["Danger", "Hurt"]); }
var mixGain = effectsOf(harmony).filter(isMusicMixGain)[0];
sendOnly(harmony, function (e) { return mixGain && e.id === mixGain.id; }, returns["Harmony Duck"]);
if (!mixGain) {
    var src = null;
    musicBed.automators.forEach(function (a) { if (a.nameOfPropertyBeingAutomated === "volume") src = a.automationCurves[0]; });
    if (!src) throw "Music Bed has no MusicMix volume automation";
    mixGain = harmony.effectChain.addEffect("GainEffect");
    harmony.effectChain.relationships.effects.remove(mixGain);
    harmony.effectChain.relationships.effects.insert(0, mixGain);
    var c = mixGain.addAutomator("gain").addAutomationCurve(src.parameter);
    src.automationPoints.forEach(function (p) { c.addAutomationPoint(p.position, p.value); });
    counts.added++;
    log("Harmony: MusicMix gain added");
}
sendOnly(group("Feature"), function () { return false; }, returns["Feature Duck"]);

function describe(b) { return b.effectChain.effects.map(function (e) { return e.entity; }).join(", ") + " @ " + b.volume + " dB"; }
Object.keys(buses).forEach(function (p) { log("bus:/" + p + ": " + describe(buses[p])); });
log("event groups: Music Bed " + describe(musicBed) + "; Harmony " + describe(harmony) + "; Feature " + describe(group("Feature")));
log("created " + counts.created + ", updated " + counts.updated + ", added " + counts.added + ", removed " + counts.removed);
project.save();
log("saved");
"""


def js_text():
    params = {k: list(v) for k, v in PARAMETERS.items()}
    buses = [[p, parent, vol, fx] for p, parent, vol, fx in BUSES + SUB_BUSES]
    return (JS_TEMPLATE.replace("__PARAMETERS__", json.dumps(params))
            .replace("__BUSES__", json.dumps(buses, indent=1))
            .replace("__RETURNS__", json.dumps(RETURNS))
            .replace("__MUTED__", json.dumps(MUTED_DB)))


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
