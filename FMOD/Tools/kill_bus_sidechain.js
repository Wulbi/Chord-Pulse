// Task 3 step 8 (sidechain feasibility, for Task 6): lets bus:/Kill Chords (where the instrument one-shots go) duck
// the Harmony group of event:/Music/ChordPulseGame, the way the in-event "Kill Chords" group track already does.
// Adds a Sidechain effect at the end of bus:/Kill Chords and adds it to the sidechain inputs of the Harmony compressor
// (keeping its existing inputs). Idempotent. Run with FMOD Studio closed, then build as a separate command:
//     fmodstudiocl.exe -script FMOD/Tools/kill_bus_sidechain.js FMOD/ChordPulse/ChordPulse.fspro
//
// RESULT (Task 3, FMOD 2.02.35, Docs/Captures/README.md): the link can be authored and builds, but at runtime Studio
// does not connect it: the Harmony compressor keeps only its two in-event sidechain inputs, the bus has no sidechain
// send, and loud one-shots on bus:/Kill Chords give 0.00 dB gain reduction. So the project is left WITHOUT the link
// (REMOVE = true undoes it). Task 6 needs Harmony on a global bus instead (bus -> bus sidechain).
var REMOVE = true;
// SUPERSEDED (Task 6): kill_oneshots.js now puts a Sidechain on bus:/Kill Chords that keys the Harmony Duck / Feature
// Duck returns; running this script (REMOVE = true) would delete it. Kept only as the record of the failed approach.
throw "kill_bus_sidechain.js is superseded by kill_oneshots.js - do not run";

function log(m) { console.log("[kill-bus-sidechain] " + m); }
var project = studio.project;
var ev = project.lookup("event:/Music/ChordPulseGame");
if (!ev) throw "event:/Music/ChordPulseGame not found";
var bus = project.lookup("bus:/Kill Chords");
if (!bus) throw "bus:/Kill Chords not found (run instruments.js first)";

var harmony = null;
ev.groupTracks.forEach(function (t) { if (t.mixerGroup.name === "Harmony") harmony = t.mixerGroup; });
if (!harmony) throw "Harmony group not found";
var comp = null;
harmony.effectChain.effects.forEach(function (e) { if (e.entity === "CompressorEffect") comp = e; });
if (!comp) throw "Harmony compressor not found";

var key = null;
bus.effectChain.effects.forEach(function (e) { if (e.entity === "Sidechain") key = e; });
if (REMOVE) {
    if (key) {
        comp.relationships.sidechains.remove(key);
        project.deleteObject(key);
        log("removed the bus:/Kill Chords sidechain and its link");
    }
} else {
    if (!key) { key = bus.effectChain.addEffect("Sidechain"); log("added Sidechain effect to bus:/Kill Chords"); }
    var linked = false;
    comp.relationships.sidechains.destinations.forEach(function (d) { if (d.id === key.id) linked = true; });
    if (!linked) { comp.relationships.sidechains.add(key); log("linked bus:/Kill Chords sidechain to the Harmony compressor"); }
}
log("Harmony compressor sidechain inputs: " + comp.relationships.sidechains.destinations.length);
project.save();
log("saved");
