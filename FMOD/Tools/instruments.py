"""
Shared instrument library: one small FMOD one-shot event per (instrument, bus), event:/Inst/<BusFolder>/<Name>
(BusFolder = the bus name without spaces, e.g. Kill Chords -> KillChords).

Writes instruments.js (next to this file) and Assets/Scripts/Audio/Generated/InstrumentTable.g.cs.
Run on the FMOD project (FMOD Studio closed), then build the banks as a SEPARATE command. Run order: instruments.js ->
mixer.js (python -I FMOD/Tools/mixer.py; it owns the buses these events output to) -> kill_oneshots.js -> the rest:
    python -I FMOD/Tools/instruments.py
    fmodstudiocl.exe -script FMOD/Tools/instruments.js FMOD/ChordPulse/ChordPulse.fspro
    fmodstudiocl.exe -script FMOD/Tools/mixer.js FMOD/ChordPulse/ChordPulse.fspro
    fmodstudiocl.exe -script FMOD/Tools/kill_oneshots.js FMOD/ChordPulse/ChordPulse.fspro
    fmodstudiocl.exe -build FMOD/ChordPulse/ChordPulse.fspro
Idempotent: the script updates the events in place (GUIDs stay, so Unity shows no "Events Renamed" dialog). It finds each
event by path (or moves the first-generation event:/Inst/<Name> into its bus folder), then writes audio file, pitch,
volume, Note condition and ADSR of each sound, plus Max Instances, stealing and output, only where they differ; sounds the
list no longer has are deleted, and so are event:/Inst/** events that are not listed.

Each event has an event-local parameter Note (0-127, continuous; Unity sets whole numbers) and, at timeline 0, one SingleSound per MIDI note
(sample + pitch from samples.pick(), volume = NORMALIZE_DB - sample RMS), enabled only for Note in [m-0.4, m+0.4].
Unity starts the instrument, sets Note, and schedules it on the beat. Output goes to bus:/<bus> (a path of
mixer.OUTPUT_BUSES; Kill Chords for now).

Layer modules (kill_oneshots.py, ...) expose register(lib) and are listed in LAYERS:
    lib.add("Marimba", samples.MARIMBA, max_instances=16, bus="Kill Chords")   # declare (instrument, bus) once
        # (max_pitch=15 semitones, attack_ms=1, release_ms=120 by default; adding the same pair differently asserts)
    lib.need("Marimba", "Kill Chords", 72)                                    # request a note
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)  # so `python -I` (isolated mode) still finds samples.py and the layer modules

import samples  # noqa: E402
import spike_click  # noqa: E402
import kill_oneshots  # noqa: E402
import mixer  # noqa: E402
import patterns  # noqa: E402
CS_PATH = os.path.join(HERE, "..", "..", "Assets", "Scripts", "Audio", "Generated", "InstrumentTable.g.cs")
JS_PATH = os.path.join(HERE, "instruments.js")

NORMALIZE_DB = -27.0      # per-note target loudness (what the kill chords have now); layers add gain in Unity
VOLUME_MIN, VOLUME_MAX = -60.0, 10.0   # same clamp as the old kill_voices generator
# Stop fade of pattern-layer one-shots (InstrumentTable.ReleaseSeconds for every bus but Kill Chords). The timeline cuts
# the old tracks' notes at the end of their region with a ramp of about 5 ms instead of playing their ADSR release
# (Task 11, offline render of the real banks: funk keys -44 -> -90 dBFS within the last ~5 ms; in game a 73 ms funk note
# sounded 80 ms on the timeline, and 95 ms as a one-shot with the event's 20 ms fade: +1.2 dB). The events keep their ADSR.
PATTERN_STOP_FADE_MS = 5
# Start latency (Task 12 follow-up, spike clicks against the timeline's beat clicks, FMOD 2.02.35, 1024-sample blocks):
# an instrument event with more than one sound (the Note condition picks one) starts its voice one mixer block after its
# channel group's delay start; with one sound it starts on it (3 kHz click, same file / bus / ADSR: 1 sound +0.22 ms,
# 8 sounds +21.71 ms). -> InstrumentTable.VoiceDelayBlocks; the backend starts the group that much earlier.
MULTI_SOUND_VOICE_DELAY_BLOCKS = 1
# Extra mixer blocks a bus family's one-shots sound after their beat: bus path prefix -> blocks.
# -> InstrumentTable.PathDelayBlocks. Empty since the music event was deleted (unity-music-clock final fix wave): the
# Harmony and Feature one-shots used to wait one block to match the legacy timeline layers, which reached the mixer one
# block late through the event's sends to return:/Harmony Duck / return:/Feature Duck. With the event gone every bus
# starts on the clock (Harmony / Combo Pad spike clicks measured +1030 samples with the delay, Kill Chords 0).
PATH_DELAY_BLOCKS = {}


def voice_delay_blocks(inst):
    return MULTI_SOUND_VOICE_DELAY_BLOCKS if len(inst["notes"]) > 1 else 0


def path_delay_blocks(bus):
    return max([b for p, b in PATH_DELAY_BLOCKS.items() if bus == p or bus.startswith(p + "/")] or [0])


def stop_fade_ms(inst):
    """Backend fade after a one-shot's stop: kill chords (and the spike click) keep their release, pattern layers end like
    the timeline notes they replace."""
    return inst["release"] if inst["bus"] == kill_oneshots.BUS else PATTERN_STOP_FADE_MS


class Library:
    """Instrument events keyed by (name, bus): the same instrument on two buses is two events with their own settings."""

    def __init__(self):
        self.tables = {}   # (name, bus) -> (sample table, max instances, max pitch, attack ms, release ms)
        self.notes = {}    # (name, bus) -> set of midi

    def add(self, name, table, max_instances, bus, max_pitch=15, attack_ms=1, release_ms=120):
        key = (name, bus)
        value = (table, max_instances, max_pitch, attack_ms, release_ms)
        if key in self.tables:
            assert self.tables[key] == value, f"instrument {name} on {bus} redefined differently"
        self.tables[key] = value
        self.notes.setdefault(key, set())

    def need(self, name, bus, midi):
        assert (name, bus) in self.tables, f"instrument {name} on {bus} not added"
        self.notes[(name, bus)].add(int(midi))


# Layer modules, each with register(lib).
LAYERS = [kill_oneshots, spike_click, patterns]


def bus_folder(bus):
    """Event folder / C# OutputBus member of a bus path (mixer.OUTPUT_BUSES: Kill Chords -> KillChords,
    Music Bed/Harmony -> Harmony)."""
    for member, path in mixer.OUTPUT_BUSES:
        if path == bus:
            return member
    raise AssertionError(f"bus {bus} is not in mixer.OUTPUT_BUSES")


def build():
    assert patterns.NORMALIZE_DB == NORMALIZE_DB == kill_oneshots.NORMALIZE_DB
    lib = Library()
    for layer in LAYERS:
        layer.register(lib)
    insts = []
    for name, bus in sorted(lib.tables):
        table, max_inst, max_pitch, attack_ms, release_ms = lib.tables[(name, bus)]
        notes = []
        for midi in sorted(lib.notes[(name, bus)]):
            s, pitch = samples.pick(table, midi, max_pitch)
            volume = round(max(VOLUME_MIN, min(NORMALIZE_DB - s[2], VOLUME_MAX)), 2)
            onset = s[3] / (2 ** (pitch / 12.0))
            notes.append(dict(midi=midi, file=s[0], pitch=pitch, volume=volume, onset=round(onset, 6)))
        insts.append(dict(name=name, bus=bus, busFolder=bus_folder(bus), max=max_inst,
                          attack=attack_ms, release=release_ms, notes=notes))
    # OnsetSeconds is keyed by (instrument, midi) only: every bus must give the same onset for the same note.
    onsets = {}
    for i in insts:
        for n in i["notes"]:
            first = onsets.setdefault((i["name"], n["midi"]), n["onset"])
            assert first == n["onset"], f"{i['name']} note {n['midi']} has different onsets on different buses"
    return insts


JS_TEMPLATE = r"""// Generated by FMOD/Tools/instruments.py - edit the Python file, not this one.
var INSTRUMENTS = __INSTRUMENTS__;
var FOLDER = "Inst";
var EPS = 0.001;

var counts = { moved: 0, updated: 0, added: 0, removed: 0 };
function log(m) { console.log("[inst] " + m); }
function differs(a, b) { return Math.abs(a - b) > EPS; }
var project = studio.project;
var assetsRoot = project.filePath.replace(/[^\/\\]+$/, "") + "Assets/";

var bank = project.lookup("bank:/ChordPulse");
if (!bank) throw "bank ChordPulse not found";

function childFolder(parent, name) {
    var found = null;
    project.model.EventFolder.findInstances().forEach(function (f) {
        if (f.isValid && f.name === name && f.folder && f.folder.id === parent.id) found = f;
    });
    if (!found) {
        found = project.create("EventFolder");
        found.name = name;
        found.folder = parent;
        log("created folder " + name);
    }
    return found;
}

// --- folder event:/Inst (top level, next to event:/Music) ---
var folder = null;
project.model.EventFolder.findInstances().forEach(function (f) { if (f.isValid && f.name === FOLDER) folder = f; });
if (!folder) {
    folder = project.create("EventFolder");
    folder.name = FOLDER;
    folder.folder = project.workspace.masterEventFolder;
    log("created folder " + FOLDER);
}

// --- buses: found by path (mixer.js sets up their effects and levels); a missing one is created, parents first ---
var buses = {};
function busFor(path) {
    if (buses[path]) return buses[path];
    var b = project.lookup("bus:/" + path);
    if (!b) {
        var cut = path.lastIndexOf("/");
        b = project.create("MixerGroup");
        b.name = path.substr(cut + 1);
        b.output = cut < 0 ? project.workspace.mixer.masterBus : busFor(path.substr(0, cut));
        log("created bus " + path);
    }
    buses[path] = b;
    return b;
}
INSTRUMENTS.forEach(function (inst) { busFor(inst.bus); });

// --- parameter Note: one project preset (each event keeps its own value), 0-127 ---
// Continuous, not discrete: on a discrete parameter FMOD widened each note's [m-0.4, m+0.4] condition to reach m+1, so
// Note 60 also played the 59 sound (B+C, E+F clusters in every C / Am / F kill chord; measured 2026-11-25). Unity sets
// whole numbers with ignoreseekspeed, so a continuous parameter selects exactly one sound.
var noteParam = project.lookup("parameter:/Note");
if (!noteParam) {
    noteParam = project.workspace.addGameParameter({ name: "Note", type: project.parameterType.User, min: 0, max: 127 });
    noteParam = project.lookup("parameter:/Note") || noteParam;
    log("created parameter Note");
}
if (noteParam.parameter.parameterType !== project.parameterType.User) noteParam.parameter.parameterType = project.parameterType.User;
if (noteParam.parameter.seekSpeed !== 0) noteParam.parameter.seekSpeed = 0;
if (noteParam.parameter.minimum !== 0) noteParam.parameter.minimum = 0;
if (noteParam.parameter.maximum !== 127) noteParam.parameter.maximum = 127;
if (noteParam.parameter.initialValue !== 0) noteParam.parameter.initialValue = 0;

var files = {};
function audioFile(rel) {
    if (files[rel]) return files[rel];
    var found = null;
    project.model.AudioFile.findInstances().forEach(function (a) {
        if (a.isValid && a.masterAssetFolder && a.length > 0 && a.assetPath === rel) found = a;
    });
    if (!found) { found = project.importAudioFile(assetsRoot + rel); log("imported " + rel); }
    if (!found) throw "could not import " + rel;
    // Studio streams assets longer than ~10 s by default; a streamed one-shot starts hundreds of ms late, so the
    // scheduler drops it or it lands on the next chord (vibes and harp, measured 2026-11-25). Load them into memory.
    if (found.isStreaming) { found.isStreaming = false; log("not streaming: " + rel); }
    files[rel] = found;
    return found;
}

function noteCondition(s) {
    var found = null;
    s.triggerConditions.forEach(function (c) { if (c.parameter && c.parameter.id === noteParam.parameter.id) found = c; });
    return found;
}

function adsr(s) {
    var found = [];
    s.modulators.forEach(function (m) { if (m.entity === "ADSRModulator" && m.nameOfPropertyBeingModulated === "volume") found.push(m); });
    return found;
}

// Brings one sound in line with the wanted note; returns true when anything was written.
function syncSound(s, n, inst, isNew) {
    var changed = false;
    var af = audioFile(n.file);
    if (isNew || !s.audioFile || s.audioFile.id !== af.id) {
        s.audioFile = af;
        var len = Math.max(__MIN_REGION__, af.length);
        if (differs(s.length, len)) s.length = len;
        changed = true;
    }
    if (differs(s.pitch, n.pitch)) { s.pitch = n.pitch; changed = true; }
    if (differs(s.volume, n.volume)) { s.volume = n.volume; changed = true; }
    var c = noteCondition(s);
    if (!c) { s.addParameterCondition(noteParam, n.midi - 0.4, n.midi + 0.4); changed = true; }
    else {
        if (differs(c.minimum, n.midi - 0.4)) { c.minimum = n.midi - 0.4; changed = true; }
        if (differs(c.maximum, n.midi + 0.4)) { c.maximum = n.midi + 0.4; changed = true; }
    }
    var mods = adsr(s);
    mods.slice(1).forEach(function (m) { project.deleteObject(m); changed = true; });
    var env = mods[0];
    if (!env) {
        env = project.create("ADSRModulator");
        env.nameOfPropertyBeingModulated = "volume";
        env.objectBeingModulated = s;
        changed = true;
    }
    if (differs(env.attackTime, inst.attack)) { env.attackTime = inst.attack; changed = true; }
    if (differs(env.releaseTime, inst.release)) { env.releaseTime = inst.release; changed = true; }
    return changed;
}

var wanted = {};   // event paths this run owns
var soundCount = 0;
INSTRUMENTS.forEach(function (inst) {
    var sub = childFolder(folder, inst.busFolder);
    var path = "event:/" + FOLDER + "/" + inst.busFolder + "/" + inst.name;
    wanted[path] = true;
    var ev = project.lookup(path);
    var isNewEvent = false, evChanged = false;
    if (!ev) {
        // the first-generation event event:/Inst/<Name>: move it into the bus folder (same GUID, so no "Events Renamed")
        var old = project.lookup("event:/" + FOLDER + "/" + inst.name);
        // only an event: a bus folder can have the same name as an instrument (event:/Inst/Bass is the Bass bus folder)
        if (old && old.entity === "Event") {
            old.folder = sub;
            ev = old;
            counts.moved++;
        } else {
            ev = project.workspace.addEvent(inst.name, false);
            ev.folder = sub;
            counts.added++;
            isNewEvent = true;
        }
    }
    var inBank = false;
    ev.banks.forEach(function (b) { if (b.id === bank.id) inBank = true; });
    if (!inBank) { ev.relationships.banks.add(bank); evChanged = true; }
    var out = buses[inst.bus];
    if (!ev.mixerInput.output || ev.mixerInput.output.id !== out.id) { ev.mixerInput.output = out; evChanged = true; }
    var ap = ev.automatableProperties;
    if (ap.maxVoices !== inst.max) { ap.maxVoices = inst.max; evChanged = true; }   // "Max Instances" in the Studio UI
    if (ap.voiceStealing !== 0) { ap.voiceStealing = 0; evChanged = true; }         // Oldest (enum: Oldest, Quietest, Virtualize, None, Furthest)
    // the event's parameter proxy points at the GameParameter (noteParam.parameter), not at the preset; keep exactly one
    var noteProxies = [];
    ev.parameters.forEach(function (p) { if (p.preset && p.preset.id === noteParam.parameter.id) noteProxies.push(p); });
    noteProxies.slice(1).forEach(function (p) { project.deleteObject(p); evChanged = true; });
    if (noteProxies.length === 0) { ev.addGameParameter(noteParam); evChanged = true; }

    // keep the first Notes track (its sounds are reconciled below); extra Notes tracks go, with their sounds
    var noteTracks = [];
    ev.groupTracks.forEach(function (t) { if (t.mixerGroup.name === "Notes") noteTracks.push(t); });
    var track = noteTracks[0];
    noteTracks.slice(1).forEach(function (t) {
        t.modules.slice().forEach(function (s) { project.deleteObject(s); counts.removed++; });
        project.deleteObject(t);
        counts.removed++;
        evChanged = true;
    });
    if (!track) { track = ev.addGroupTrack("Notes"); evChanged = true; }

    // match the track's sounds to the wanted notes by the MIDI number in their Note condition
    var byMidi = {}, drop = [];
    track.modules.forEach(function (s) {
        var c = noteCondition(s);
        var midi = c ? Math.round((c.minimum + c.maximum) / 2) : -1;
        if (midi < 0 || byMidi[midi]) drop.push(s); else byMidi[midi] = s;
    });
    inst.notes.forEach(function (n) {
        var s = byMidi[n.midi], isNew = false;
        if (s) delete byMidi[n.midi];
        else {
            s = track.addSound(ev.timeline, "SingleSound", 0, 0.5);
            isNew = true;
            counts.added++;
        }
        if (syncSound(s, n, inst, isNew)) evChanged = true;   // a new or changed sound counts as an update of its event
        soundCount++;
    });
    Object.keys(byMidi).forEach(function (k) { drop.push(byMidi[k]); });
    drop.forEach(function (s) { project.deleteObject(s); counts.removed++; evChanged = true; });
    if (evChanged && !isNewEvent) counts.updated++;
});

// --- events under event:/Inst that this generator no longer lists ---
var stale = [];
project.model.Event.findInstances().forEach(function (ev) {
    if (!ev.isValid) return;
    var p = ev.getPath();
    if (p.indexOf("event:/" + FOLDER + "/") === 0 && !wanted[p]) stale.push(ev);
});
stale.forEach(function (ev) { log("removing " + ev.getPath()); project.deleteObject(ev); counts.removed++; });

// moved: events moved from event:/Inst/<Name> into their bus folder; updated: existing events (moved ones included) where
// any property, sound value or structure was written; added: new events plus new sounds; removed: deleted sounds,
// extra Notes tracks and stale events.
log("moved " + counts.moved + ", updated " + counts.updated + ", added " + counts.added + ", removed " + counts.removed);
log("events " + INSTRUMENTS.length + ", sounds " + soundCount);
project.save();
log("saved");
"""


def cs_text(insts):
    names = sorted({i["name"] for i in insts})
    lines = [
        "// Generated by FMOD/Tools/instruments.py - edit the Python file, not this one.",
        "// Instrument one-shot events (event:/Inst/<bus>/<name>) and the attack onset of each note's sample.",
        "public enum Instrument",
        "{",
    ]
    lines += [f"    {n}," for n in names]
    lines += ["}", "", "/// <summary>Global mixer bus an instrument event plays through (path: InstrumentTable.BusPath).</summary>",
              "public enum OutputBus", "{"]
    lines += [f"    {member}," for member, _ in mixer.OUTPUT_BUSES]
    lines += ["}", "", "public static class InstrumentTable", "{",
              "    /// <summary>FMOD path of the bus (bus:/Music Bed/Harmony, ...); null for an unknown value.</summary>",
              "    public static string BusPath(OutputBus bus)", "    {", "        switch (bus)", "        {"]
    for member, path in mixer.OUTPUT_BUSES:
        lines.append(f'            case OutputBus.{member}: return "bus:/{path}";')
    lines += ["        }", "        return null;", "    }", "",
              "    /// <summary>FMOD event path of the instrument on that bus; null if the combination does not exist.</summary>",
              "    public static string EventPath(Instrument instrument, OutputBus bus)", "    {"]
    for i in insts:
        lines.append(f'        if (instrument == Instrument.{i["name"]} && bus == OutputBus.{i["busFolder"]}) return "event:/Inst/{i["busFolder"]}/{i["name"]}";')
    lines += ["        return null;", "    }", "",
              "    /// <summary>True if the instrument event on that bus has a sound for the MIDI note.</summary>",
              "    public static bool Has(Instrument instrument, OutputBus bus, int midi)", "    {"]
    for i in insts:
        lines.append(f"        if (instrument == Instrument.{i['name']} && bus == OutputBus.{i['busFolder']})")
        lines.append("        {")
        lines.append("            switch (midi)")
        lines.append("            {")
        for n in i["notes"]:
            lines.append(f"                case {n['midi']}: return true;")
        lines.append("            }")
        lines.append("            return false;")
        lines.append("        }")
    lines += ["        return false;", "    }", "",
              "    /// <summary>",
              "    /// Seconds the one-shot backend fades a note out after its stop time; 0 if the combination does not exist. Kill chords:",
              "    /// their event's ADSR release. Pattern layers: the timeline's region-end ramp (instruments.PATTERN_STOP_FADE_MS).",
              "    /// </summary>",
              "    public static double ReleaseSeconds(Instrument instrument, OutputBus bus)", "    {"]
    for i in insts:
        lines.append(f"        if (instrument == Instrument.{i['name']} && bus == OutputBus.{i['busFolder']}) return {stop_fade_ms(i) / 1000.0!r};")
    lines += ["        return 0.0;", "    }", "",
              "    /// <summary>",
              "    /// Mixer blocks the event's voice starts after its channel group's delay start (instruments.MULTI_SOUND_VOICE_DELAY_BLOCKS:",
              "    /// events with more than one sound); 0 if the combination does not exist.",
              "    /// </summary>",
              "    public static int VoiceDelayBlocks(Instrument instrument, OutputBus bus)", "    {"]
    for i in insts:
        if voice_delay_blocks(i):
            lines.append(f"        if (instrument == Instrument.{i['name']} && bus == OutputBus.{i['busFolder']}) return {voice_delay_blocks(i)};")
    lines += ["        return 0;", "    }", "",
              "    /// <summary>Extra mixer blocks a note on this bus sounds after its beat (instruments.PATH_DELAY_BLOCKS;",
              "    /// empty since the music event was deleted, so 0 on every bus).</summary>",
              "    public static int PathDelayBlocks(OutputBus bus)", "    {", "        switch (bus)", "        {"]
    for member, path in mixer.OUTPUT_BUSES:
        if path_delay_blocks(path):
            lines.append(f"            case OutputBus.{member}: return {path_delay_blocks(path)};")
    lines += ["        }", "        return 0;", "    }", "",
              "    /// <summary>Seconds from the sample start to its attack, after pitch shifting; 0 if the note is not in the library.</summary>",
              "    public static double OnsetSeconds(Instrument instrument, int midi)", "    {",
              "        switch (instrument)", "        {"]
    onsets = {}   # name -> {midi: onset}, merged over the buses (build() asserts they agree)
    for i in insts:
        for n in i["notes"]:
            onsets.setdefault(i["name"], {})[n["midi"]] = n["onset"]
    for name in sorted(onsets):
        lines.append(f"            case Instrument.{name}:")
        lines.append("                switch (midi)")
        lines.append("                {")
        for midi in sorted(onsets[name]):
            lines.append(f"                    case {midi}: return {onsets[name][midi]!r};")
        lines.append("                }")
        lines.append("                break;")
    lines += ["        }", "        return 0.0;", "    }", "}", ""]
    return "\n".join(lines)


def write(path, text):
    kill_oneshots.write_if_different(path, text)


def main():
    insts = build()
    write(JS_PATH, JS_TEMPLATE.replace("__INSTRUMENTS__", json.dumps(insts, indent=1))
          .replace("__MIN_REGION__", repr(patterns.EVENT_REGION_MIN_S)))
    write(CS_PATH, cs_text(insts))
    kill_oneshots.write_table()
    kill_oneshots.write_js()
    patterns.write_table(bus_folder)
    print(len(insts), "instruments,", sum(len(i["notes"]) for i in insts), "notes -> instruments.js, InstrumentTable.g.cs")


if __name__ == "__main__":
    main()
