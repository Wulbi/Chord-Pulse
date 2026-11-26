// Undo kill_voices.js (Docs/AdaptiveAudioPlan.md 10-5): its per-style kill tracks (10,368 notes) overloaded the FMOD
// Studio update thread (89% -> 11% CPU without them) and caused "Loading delay exceeded" / audible stutter.
//     fmodstudiocl.exe -script FMOD/Tools/kill_voices_remove.js FMOD/ChordPulse/ChordPulse.fspro
//     fmodstudiocl.exe -build FMOD/ChordPulse/ChordPulse.fspro          (separately)
// Removes the "Kill combo / punch / latin / funk / dreamy" tracks and the BedStyle condition kill_voices.js put on the
// original Red / Blue / Yellow kill chords, so those play in every style again. Idempotent.
// Per-style kill timbres are planned again as one-shot events (plan B, section 10-4).
var project = studio.project;
var ev = project.lookup("event:/Music/ChordPulseGame");
function log(m) { console.log("[kv-remove] " + m); }

var STYLE_TRACKS = /^Kill (combo|punch|latin|funk|dreamy)$/;
var removedTracks = [], removedNotes = 0;
ev.groupTracks.slice().forEach(function (t) {
    if (!STYLE_TRACKS.test(t.mixerGroup.name)) return;
    removedNotes += t.modules.length;
    removedTracks.push(t.mixerGroup.name);
    project.deleteObject(t);
});
log("removed tracks: " + (removedTracks.join(", ") || "none") + " (" + removedNotes + " notes)");

var ORIGINALS = ["Red kill chord", "Blue kill chord", "Yellow kill chord"];
var cleared = 0;
ev.groupTracks.forEach(function (t) {
    if (ORIGINALS.indexOf(t.mixerGroup.name) < 0) return;
    t.modules.forEach(function (m) {
        (m.triggerConditions || []).slice().forEach(function (c) {
            var p = c.parameter;
            if (p && p.presetOwner && p.presetOwner.name === "BedStyle") { project.deleteObject(c); cleared++; }
        });
    });
});
log("BedStyle conditions removed from original kill chords: " + cleared);

var left = 0;
ev.groupTracks.forEach(function (t) { left += t.modules.length; });
log("notes left in event: " + left);
project.save();
log("saved");
