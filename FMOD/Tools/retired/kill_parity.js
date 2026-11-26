// Kill chords on even / odd beats (Docs/AdaptiveAudioPlan.md section 10).
// Run AFTER kill_voices.js (and again whenever it is re-run):
//     fmodstudiocl.exe -script FMOD/Tools/kill_parity.js FMOD/ChordPulse/ChordPulse.fspro
//     fmodstudiocl.exe -build FMOD/ChordPulse/ChordPulse.fspro          (separately - see the note at the bottom)
// Idempotent.
//
// Why: every beat of the timeline holds a kill-chord note gated by RedKills / BlueKills / YellowKills. FMOD decides
// a beat ~0.15 s before it sounds, so a value set for one beat was still on when FMOD looked at the next beat and the
// chord played twice. With one parameter per beat parity, the next beat always reads the other parameter, and the
// same-parity parameter is not needed again for two beats - Unity can set it well ahead of time.
// Notes are kept no longer than MAX_REGION_BEATS so a parameter set ~0.2 s ahead is never inside the region of the
// note two beats earlier (which would trigger or cut that note).
var DRY_RUN = false;
var MAX_REGION_BEATS = 1.45;

var project = studio.project;
var ev = project.lookup("event:/Music/ChordPulseGame");
function log(m) { console.log("[parity] " + m); }

var SECTIONS = [], t = 0;
[108, 116, 124, 132].forEach(function (bpm) { SECTIONS.push({ start: t, beat: 60 / bpm }); t += 32 * 60 / bpm; });
function sectionAt(pos) { var s = SECTIONS[0]; SECTIONS.forEach(function (x) { if (pos >= x.start - 0.02) s = x; }); return s; }

var COLORS = ["Red", "Blue", "Yellow"];
var PARITY = ["Even", "Odd"];

function presetByName(name) {
    var found = null;
    project.model.ParameterPreset.findInstances().forEach(function (p) { if (p.name === name) found = p; });
    return found;
}
function ensureParameter(name) {
    var preset = presetByName(name);
    if (!preset) {
        if (DRY_RUN) return null;
        preset = project.create("ParameterPreset");
        preset.name = name;
        var gp = project.create("GameParameter");
        gp.minimum = 0; gp.maximum = 3; gp.initialValue = 0;
        preset.parameter = gp;
        log("created " + name);
    }
    var gp2 = preset.parameter;
    var has = ev.parameters.some(function (p) { return p.preset && p.preset.id === gp2.id; });
    if (!has && !DRY_RUN) {
        var proxy = project.create("ParameterProxy");
        proxy.preset = gp2;
        ev.relationships.parameters.add(proxy);
    }
    return gp2;
}

// source parameter (old single, or either parity) -> colour index
var source = {};
COLORS.forEach(function (c, i) {
    [c + "Kills", c + "KillsEven", c + "KillsOdd"].forEach(function (n) { var p = presetByName(n); if (p) source[p.parameter.id] = i; });
});
var target = COLORS.map(function (c) { return PARITY.map(function (p) { return ensureParameter(c + "Kills" + p); }); });

var converted = 0, already = 0, capped = 0, maxBeats = 0, offBeat = 0, perTrack = {};
ev.groupTracks.forEach(function (tr) {
    tr.modules.forEach(function (m) {
        if (!m.triggerConditions) return;
        m.triggerConditions.forEach(function (c) {
            if (!c.parameter || source[c.parameter.id] === undefined) return;
            var color = source[c.parameter.id];
            var s = sectionAt(m.start);
            var beatF = (m.start - s.start) / s.beat;
            var beat = Math.round(beatF);
            if (Math.abs(beatF - beat) > 0.26) offBeat++;
            var parity = ((beat % 2) + 2) % 2;
            var lenBeats = m.length / s.beat;
            if (lenBeats > maxBeats) maxBeats = lenBeats;
            perTrack[tr.mixerGroup.name] = Math.max(perTrack[tr.mixerGroup.name] || 0, Math.round(lenBeats * 100) / 100);
            if (lenBeats > MAX_REGION_BEATS) { capped++; if (!DRY_RUN) m.length = MAX_REGION_BEATS * s.beat; }
            var want = target[color][parity];
            if (want && c.parameter.id === want.id) { already++; return; }
            if (!DRY_RUN) c.parameter = want;
            converted++;
        });
    });
});
log("longest kill note per track (beats): " + JSON.stringify(perTrack));
log("converted " + converted + ", already on parity " + already + ", capped to " + MAX_REGION_BEATS + " beats: " + capped + ", notes > 1/4 beat off the beat grid: " + offBeat);
if (!DRY_RUN) {
    project.save();
    log("saved");
} else {
    log("dry run: nothing saved");
}
// Note: a crash of fmodstudiocl right after "saved" (in Qt5Script.dll) is a known FMOD scripting bug on big
// projects; the save has already happened. Always run -build as its own command afterwards.
