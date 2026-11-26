"""
Pattern data: the 12 feature loops (guest trumpet, whistle, mallets, latin percussion, synth lead, guitar) and the 6
fever guest loops (ids 13-18, fever=True, 2026-11-27), for the
PatternScheduler (Task 13), and the one place a loop is defined (feature_loops.py writes FeatureLoopNotes.g.cs from LOOPS).

Music rules (Docs/AdaptiveAudioPlan.md section 7):
- Every phrase is 4 bars of C - Am - F - G, so a loop written once fits every tempo section.
- A loop plays in the phrase whose FeatureLoop is its id (clip condition FeatureLoop = id; ArrangementState.FeatureLoop,
  0 over fever and its build-up bar). Unity picks the id one phrase ahead (Audio/FeatureSelector.cs).
- Beats are relative to the phrase start (0..16). MIDI 60 = C4. Percussion notes name their sound (PERC).

Layers = the six old timeline tracks (`Feature trumpet` ... `Feature guitar`, deleted in stages 3-8 Task 15b with the
other migrated tracks); each plays through its own sub-bus of bus:/Feature carrying the track's filters (mixer.py
SUB_BUSES). One clip per (loop, layer). Same sound as the old feature_loops.py placements (checked by the removed
--compare-legacy, Docs/Captures/README.md Task 13):
- Volume: instrument target - sample RMS + accent (a stab's notes -3 dB), clamped to [-30, 10] like the old generator,
  plus the track fader (whistle -1 dB); patterns.py splits it into the event volume and GainDb.
- Length: gated instruments (synth, guitar) and the held trumpet note stop at the written length; the others ring at
  least their sample's audible time at that pitch, at most 0.6 s (glockenspiel 1.2 s): HoldSeconds.
- Onset: the old placement started every note but a phrase's first beat early by the sample onset; the scheduler starts
  every note early (legacy_shift "onset_not_beat0").
- ADSR attack 2 ms / release 70 ms on every note (the old tracks' envelope).

Fever guest loops (fever=True): play only over the fever phrase (Impact + Fever + Outro, never its build-up bar), picked
per fever by Unity's FeverSelector (FeatureSelector never picks them for an ordinary phrase). They run all 4 bars and use
only chord tones (or percussion), so they fit any fever bed variant (pattern_fever.py VARIANTS). Nothing on the
phrase's last 16th.
Data only.
"""
import samples
from patterns import Clip, Inst, Layer, N

TRUMPET_BUS, WHISTLE_BUS, MALLETS_BUS = "Feature/Trumpet", "Feature/Whistle", "Feature/Mallets"
PERC_BUS, SYNTH_BUS, GUITAR_BUS = "Feature/Perc", "Feature/Synth", "Feature/Guitar"
ATTACK_MS, RELEASE_MS = 2, 70
VOLUME_CLAMP = (-30.0, 10.0)   # the old feature generator's note volume clamp


def _inst(name, table, bus, max_pitch=12):
    return Inst(name, table, bus, ATTACK_MS, RELEASE_MS, max_pitch=max_pitch)


# old instrument -> (layer, Inst, target dB, gated, hold cap s). Gated instruments stop at the written length.
INSTRUMENTS = {
    "trumpet": ("Feature trumpet", _inst("Trumpet", samples.TRUMPET, TRUMPET_BUS), -29.0, False, 0.6),
    "whistle": ("Feature whistle", _inst("Whistle", samples.WHISTLE, WHISTLE_BUS), -28.0, False, 0.6),
    "marimba": ("Feature mallets", _inst("FeatureMarimba", samples.FEATURE_MARIMBA, MALLETS_BUS), -29.0, False, 0.6),
    "xylo": ("Feature mallets", _inst("Xylo", samples.XYLO, MALLETS_BUS), -31.0, False, 0.6),
    "glock": ("Feature mallets", _inst("Glock", samples.FEATURE_GLOCK, MALLETS_BUS), -31.0, False, 1.2),
    "perc": ("Feature perc", _inst("FeaturePerc", samples.FEATURE_PERC, PERC_BUS, max_pitch=0), -32.0, False, 0.6),
    "synth": ("Feature synth", _inst("ScreamLead", samples.SCREAM_LEAD, SYNTH_BUS), -31.0, True, 0.0),
    "guitar": ("Feature guitar", _inst("PowerChord", samples.POWER_CHORD, GUITAR_BUS), -29.0, True, 0.0),
}
# The held trumpet note (hold=True) plays the sustain sample, gated at its written length.
TRUMPET_SUSTAIN = _inst("TrumpetSustain", samples.TRUMPET_SUSTAIN, TRUMPET_BUS)
# Old track faders (the one-shot path has none: folded into the note's dB).
TRACK_DB = {"Feature whistle": -1.0}
# Instrument flags, shared with FeatureSelector.Instrument in Unity.
FLAGS = {"trumpet": 1, "whistle": 2, "marimba": 4, "xylo": 4, "glock": 4, "perc": 8, "synth": 16, "guitar": 32}
# Percussion sound name -> its General MIDI number in samples.FEATURE_PERC.
PERC = {"cowbell": 56, "cowbell_mute": 55, "agogo_hi": 67, "agogo_lo": 68, "claves": 75, "bongo_hi": 60, "bongo_lo": 61,
        "conga": 63, "conga_slap": 62, "tumba": 64, "guiro": 73}

C4, D4, E4, F4, G4, A4, B4 = 60, 62, 64, 65, 67, 69, 71
C5, D5, E5, F5, G5, A5, B5 = 72, 74, 76, 77, 79, 81, 83
C6, D6, E6, F6, G6, A6, B6 = 84, 86, 88, 89, 91, 93, 95
C7, D7, E7 = 96, 98, 100
G2 = 43


# A note of a loop: (instrument, beat, MIDI or percussion name, length in beats, accent dB, held).
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


def loop(id_, name, start_bar, bars, *events, fever=False):
    notes = [n for e in events for n in e]
    for inst, beat, *_ in notes:
        assert start_bar * 4 <= beat < (start_bar + bars) * 4, (name, beat)
        assert not fever or beat < 15.75, (name, beat)   # nothing on the fever phrase's last 16th
    return {"id": id_, "name": name, "startBar": start_bar, "bars": bars, "notes": notes, "fever": fever}


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


def _fever_glock():
    """16th glockenspiel shower down each chord from the top, then two sparkles."""
    events = []
    tops = [(C7, G6, E6, C6), (C7, A6, E6, C6), (C7, A6, F6, C6), (B6, G6, D6, B5)]
    for bar, t in enumerate(tops):
        down = list(t) + [x - 12 for x in t]
        for i, m in enumerate(down):
            events.append(M(bar * 4 + i * 0.25, m, "glock", vol=0.0 if i == 0 else -3.0))
        events.append(M(bar * 4 + 2.5, t[0], "glock", vol=-1.0))
        events.append(M(bar * 4 + 3.0, t[1], "glock", vol=-3.0))
    return events


def _fever_power():
    """8th power chords, accents on the beats, chopped off-beats; a last-bar push."""
    roots = [48, 45, 41, 43]   # C3, A2, F2, G2
    events = []
    for bar, r in enumerate(roots):
        for i in range(8):
            if bar == 3 and i >= 6:
                continue
            events.append(G(bar * 4 + i * 0.5, r, length=0.35 if i % 2 == 0 else 0.18, vol=0.0 if i % 2 == 0 else -4.0))
    events.append(G(15.0, 43, length=0.25, vol=1.0))
    events.append(G(15.5, 43, length=0.2, vol=1.5))
    return events


def _fever_carnival():
    """Samba-ish percussion: cowbell 8ths, tumba on 2 and 4, agogo bell pattern, bongo / conga fill."""
    events = []
    for bar in range(4):
        o = bar * 4
        for i in range(8):
            if bar == 3 and i >= 6:
                continue
            events.append(P(o + i * 0.5, "cowbell" if i % 2 == 0 else "cowbell_mute", vol=-1.0 if i % 2 == 0 else -3.0))
        events.append(P(o + 1.0, "tumba", vol=1.0))
        events.append(P(o + 3.0, "tumba", vol=1.0))
        if bar < 3:
            events.append(P(o + 0.75, "agogo_hi") + P(o + 1.5, "agogo_lo") + P(o + 2.75, "agogo_hi")
                          + P(o + 3.5, "agogo_lo"))
            events.append(P(o + 2.25, "conga", vol=-3.0) + P(o + 2.5, "conga_slap", vol=-1.0))
    events.append(P(14.0, "bongo_hi") + P(14.25, "bongo_hi", vol=-3.0) + P(14.5, "bongo_lo")
                  + P(14.75, "bongo_lo", vol=-3.0) + P(15.0, "conga_slap", vol=1.0) + P(15.25, "conga")
                  + P(15.5, "tumba", vol=1.0))
    return events


FEVER_LOOPS = [
    # Trumpet section stabs on every chord with a G7 run into the last bar's double hit.
    loop(13, "Fever Fanfare", 0, 4,
         T(0.0, C5, E5, G5, vol=1.5), T(1.5, C5, E5, G5), T(2.5, G5), T(3.0, E5), T(3.5, G5),
         T(4.0, C5, E5, A5, vol=1.5), T(5.5, C5, E5, A5), T(6.5, A5), T(7.0, E5), T(7.5, A5),
         T(8.0, C5, F5, A5, vol=1.5), T(9.5, C5, F5, A5), T(10.5, A5), T(11.0, F5), T(11.5, C6),
         T(12.0, G4), T(12.5, B4), T(13.0, D5), T(13.5, G5), T(14.0, B4, D5, G5, vol=1.0),
         T(15.0, B4, D5, G5, vol=1.5), T(15.5, B4, D5, G5), fever=True),

    # High whistle counter-line on the off-beats, leaving the downbeats to the fever lead.
    loop(14, "Fever Whistle", 0, 4,
         W(0.5, G6), W(1.0, C7, vol=1.0), W(1.5, G6), W(2.5, E6), W(3.0, G6), W(3.5, C7),
         W(4.5, A6), W(5.0, C7, vol=1.0), W(5.5, A6), W(6.5, E6), W(7.0, A6), W(7.5, C7),
         W(8.5, A6), W(9.0, C7, vol=1.0), W(9.5, A6), W(10.5, F6), W(11.0, A6), W(11.5, C7),
         W(12.5, B6), W(13.0, D7, vol=1.0), W(13.5, B6), W(14.0, G6), W(14.5, B6), W(15.0, D7, vol=1.0),
         W(15.5, B6), fever=True),

    # Glockenspiel showers falling down each chord.
    loop(15, "Fever Glock Shower", 0, 4, *_fever_glock(), fever=True),

    # Synth scream lead: a soaring line in long notes on the chord tones.
    loop(16, "Fever Scream", 0, 4,
         S(0.0, G5, length=1.4), S(1.5, E5, length=0.45), S(2.0, G5, length=0.9), S(3.0, C5, length=0.45),
         S(3.5, E5, length=0.45),
         S(4.0, A5, length=1.4), S(5.5, E5, length=0.45), S(6.0, A5, length=0.9), S(7.0, C5, length=0.45),
         S(7.5, E5, length=0.45),
         S(8.0, A5, length=1.4), S(9.5, F5, length=0.45), S(10.0, A5, length=0.9), S(11.0, C5, length=0.45),
         S(11.5, F5, length=0.45),
         S(12.0, G5, length=1.4), S(13.5, D5, length=0.45), S(14.0, G5, length=0.9), S(15.0, B4, length=0.45),
         S(15.5, D5, length=0.2), fever=True),

    # Rock power chords: driving 8ths under the whole fever.
    loop(17, "Fever Power Drive", 0, 4, *_fever_power(), fever=True),

    # Carnival percussion: cowbells, tumba, agogo and a bongo / conga fill.
    loop(18, "Fever Carnival", 0, 4, *_fever_carnival(), fever=True),
]
LOOPS += FEVER_LOOPS
# Fever guests play over the densest bed (intensity 3 + a fever variant): per-loop trim (dB) so each part sits near
# -36 dB RMS on its own (offline renders at 124 BPM, 2026-11-27: whistle -33, fanfare / carnival -37, glock / scream /
# power chords -40 before the trim).
FEVER_GUEST_DB = {13: 1.0, 14: -2.5, 15: 4.0, 16: 3.5, 17: 3.5, 18: 1.0}
assert [l["id"] for l in LOOPS] == list(range(1, len(LOOPS) + 1)), "loop ids must be 1..N"

LAYER_ORDER = ["Feature trumpet", "Feature whistle", "Feature mallets", "Feature perc", "Feature synth", "Feature guitar"]
LAYERS = [Layer(name, name) for name in LAYER_ORDER]
LOW_CLAMPED = []   # notes the old [-30, 10] clamp raised (patterns.py warns about the upper clamp itself)


def note(inst_key, beat, midi, length, vol, hold, trim=0.0):
    """One old loop note as a pattern note (and its layer); trim = a fever guest's level offset (dB)."""
    layer, inst, target, gated, cap = INSTRUMENTS[inst_key]
    if inst_key == "trumpet" and hold:
        inst = TRUMPET_SUSTAIN
    if inst_key == "perc":
        midi = PERC[midi]
    s, pitch = inst.sample(midi)
    ratio = 2 ** (pitch / 12.0)
    raw = target - s[2] + vol + trim
    if raw < VOLUME_CLAMP[0]:
        LOW_CLAMPED.append((inst_key, beat, midi, raw))
    volume = max(VOLUME_CLAMP[0], raw)
    assert TRACK_DB.get(layer, 0.0) == 0.0 or volume <= VOLUME_CLAMP[1], "a track fader must not move a clamped note"
    hold_s = 0.0 if gated or hold else round(min(s[4] / ratio, cap), 6)
    return layer, N(beat, midi, length, volume + TRACK_DB.get(layer, 0.0), inst, hold_s)


def _clips():
    out = []
    for lp in LOOPS:
        per_layer = {}
        for n in lp["notes"]:
            layer, pn = note(*n, trim=FEVER_GUEST_DB.get(lp["id"], 0.0))
            per_layer.setdefault(layer, []).append(pn)
        for layer in LAYER_ORDER:
            if layer in per_layer:
                name = f"Loop{lp['id']:02d}{layer.split()[1].capitalize()}"
                fever = ("Impact", "Fever", "Outro") if lp["fever"] else ("None", "Build", "Impact", "Fever", "Outro")
                out.append(Clip(name, layer, per_layer[layer], feature_loop=lp["id"], legacy_shift="onset_not_beat0",
                                fever=fever))
    return out


CLIPS = _clips()
