"""
Pattern data: the 12 feature loops (guest trumpet, whistle, mallets, latin percussion, synth lead, guitar) and the 6
fever guest loops (ids 13-18, fever=True, 2026-11-27), for the
PatternScheduler (Task 13), and the one place a loop is defined (feature_loops.py writes FeatureLoopNotes.g.cs from LOOPS).

Music rules (Docs/AdaptiveAudioPlan.md section 7):
- Loops are written over the original 4 bars of C - Am - F - G. Every phrase plays one chord progression
  (progressions.py); a loop's pitched notes move bar by bar onto the progression's chords (moved_notes: the same
  diatonic shift for every instrument of the bar, the smallest that keeps each note in its instrument's range), so a
  loop written once fits every progression and every tempo section. Percussion never moves.
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
import progressions as PR
import samples
from patterns import Inst, Layer, N, expand

TRUMPET_BUS, WHISTLE_BUS, MALLETS_BUS = "Feature/Trumpet", "Feature/Whistle", "Feature/Mallets"
PERC_BUS, SYNTH_BUS, GUITAR_BUS = "Feature/Perc", "Feature/Synth", "Feature/Guitar"
WINDS_BUS, KEYS_BUS, CHIP_BUS = "Feature/Winds", "Feature/Keys", "Feature/Chip"
VOICE_BUS, ACID_BUS = "Feature/Voice", "Feature/AcidLine"
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
    # guest loops 19+ (2026-11-27)
    "sax": ("Feature winds", _inst("Sax", samples.SAX, WINDS_BUS), -29.5, False, 0.6),
    "clarinet": ("Feature winds", _inst("Clarinet", samples.CLARINET, WINDS_BUS), -26.0, False, 0.6),
    "flute": ("Feature winds", _inst("Flute", samples.FLUTE, WINDS_BUS), -27.0, False, 0.6),
    "horn": ("Feature winds", _inst("Horn", samples.HORN, WINDS_BUS), -27.0, False, 0.6),
    "trombone": ("Feature winds", _inst("Trombone", samples.TROMBONE, WINDS_BUS), -27.0, False, 0.6),
    "fmpiano": ("Feature keys", _inst("FMPiano", samples.FM_PIANO, KEYS_BUS), -26.0, False, 1.2),
    "tine": ("Feature keys", _inst("TinePiano", samples.TINE_PIANO, KEYS_BUS), -28.5, False, 1.2),
    "mbira": ("Feature keys", _inst("Mbira", samples.MBIRA, KEYS_BUS), -28.5, False, 0.6),
    "lala": ("Feature voice", _inst("LaLa", samples.LALA, VOICE_BUS), -26.0, True, 0.0),
    "chiplead": ("Feature chip", _inst("ChipLead", samples.CHIP_LEAD, CHIP_BUS, max_pitch=24), -28.0, True, 0.0),
    "chippluck": ("Feature chip", _inst("ChipPluck", samples.CHIP_PLUCK, CHIP_BUS), -31.0, False, 0.45),
    "chipbass": ("Feature chip", _inst("ChipBass", samples.CHIP_BASS, CHIP_BUS), -26.0, True, 0.0),
    "acid": ("Feature acid", _inst("AcidLine", samples.ACID_LINE, ACID_BUS), -28.0, True, 0.0),
    "twang": ("Feature guitar", _inst("Twang", samples.TWANG, GUITAR_BUS), -30.0, False, 0.9),
}
# The held trumpet note (hold=True) plays the sustain sample, gated at its written length.
TRUMPET_SUSTAIN = _inst("TrumpetSustain", samples.TRUMPET_SUSTAIN, TRUMPET_BUS)
# Old track faders (the one-shot path has none: folded into the note's dB).
TRACK_DB = {"Feature whistle": -1.0}
# Instrument flags, shared with FeatureSelector.Instrument in Unity.
FLAGS = {"trumpet": 1, "whistle": 2, "marimba": 4, "xylo": 4, "glock": 4, "perc": 8, "synth": 16, "guitar": 32,
         # the new instruments share the six visual families: brass-like = trumpet, airy = whistle, keys = mallets,
         # chip / acid = synth
         "sax": 1, "horn": 1, "trombone": 1, "clarinet": 2, "flute": 2, "lala": 2, "fmpiano": 4, "tine": 4, "mbira": 4,
         "chiplead": 16, "chippluck": 16, "chipbass": 16, "acid": 16, "twang": 32}
# Percussion sound name -> its General MIDI number in samples.FEATURE_PERC.
PERC = {"cowbell": 56, "cowbell_mute": 55, "agogo_hi": 67, "agogo_lo": 68, "claves": 75, "bongo_hi": 60, "bongo_lo": 61,
        "conga": 63, "conga_slap": 62, "tumba": 64, "guiro": 73,
        "tamb": 54, "sleigh": 83, "triangle": 81, "triangle_mute": 80, "wood": 76, "log_hi": 77, "log_lo": 45,
        "slapstick": 84, "whistle_up": 71, "whistle_down": 72, "anvil": 86}

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


def P(beat, *names, vol=0.0, length=0.5):
    return [("perc", beat, n, length, vol, False) for n in names]


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
FEVER_GUEST_DB = {13: 1.0, 14: -2.5, 15: 4.0, 16: 3.5, 17: 3.5, 18: 1.0, 32: 1.0, 33: 0.0}
# Phrase loops built on the shared percussion instrument: per-loop trim (dB) so the part sits near the other loops
# (-36 dB RMS over its bars on its own, offline renders at 124 BPM, 2026-11-27).
LOOP_DB = {28: 3.0, 29: 1.5}

LAYER_ORDER = ["Feature trumpet", "Feature whistle", "Feature mallets", "Feature perc", "Feature synth", "Feature guitar",
               "Feature winds", "Feature keys", "Feature chip", "Feature voice", "Feature acid"]
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


def _inst_of(inst_key, hold):
    return TRUMPET_SUSTAIN if inst_key == "trumpet" and hold else INSTRUMENTS[inst_key][1]


def _in_range(inst_key, hold, midi):
    try:
        _inst_of(inst_key, hold).sample(midi)
        return True
    except AssertionError:
        return False


def moved_notes(notes, p):
    """A loop's notes on progression p: each bar's pitched notes shifted together from the original chord to the
    progression's (progressions.steps_between, smallest first, skipping shifts that leave an instrument's range)."""
    target = PR.chords(p)
    out = list(notes)
    for bar in range(4):
        src, dst = PR.ORIGINAL[bar], target[bar]
        idx = [i for i, n in enumerate(notes) if int(n[1] // 4) == bar and n[0] != "perc"]
        if not idx or src == dst:
            continue
        for k in PR.steps_between(src, dst):
            moved = {i: PR.shift(notes[i][2], k) for i in idx}
            if all(_in_range(notes[i][0], notes[i][5], m) for i, m in moved.items()):
                break
        else:
            raise ValueError(f"loop note out of range on progression {p}, bar {bar}")
        for i, m in moved.items():
            out[i] = notes[i][:2] + (m,) + notes[i][3:]
    return out


# ===== guest loops 19+ (2026-11-27): the instruments that had no part yet ==============================================
# Winds (tenor sax, clarinet, flute, french horn, trombone), keys (FM piano, tine piano, mbira), a "la la" voice, chiptune
# (pulse lead, pulse pluck, triangle bass), an acid bass line, a twangy guitar and sparkle / toy percussion. Same rules
# as the loops above: written over C - Am - F - G, moved onto each progression bar by bar (moved_notes).
def X(inst, beat, note_, length=0.45, vol=0.0):
    """A note of any instrument in INSTRUMENTS."""
    return [(inst, beat, note_, length, vol, False)]


def riff(inst, figure, bars=(0, 1, 2, 3), last=None):
    """A one-bar figure written over C, repeated in `bars`, each copy moved diatonically onto that bar's original
    chord (smallest shift that stays in the instrument's range). last = another figure for bar 4 (on G, as written)."""
    events = []
    for bar in bars:
        if bar == 3 and last is not None:
            events += [X(inst, 12 + b, m, length, vol) for b, m, length, vol in last]
            continue
        dst = PR.ORIGINAL[bar]
        for k in PR.steps_between("C", dst):
            moved = [PR.shift(m, k) for _, m, _, _ in figure]
            if all(_in_range(inst, False, m) for m in moved):
                break
        else:
            raise ValueError(f"riff out of range for {inst} on {dst}")
        events += [X(inst, bar * 4 + b, m, length, vol) for (b, _, length, vol), m in zip(figure, moved)]
    return events


def _house_piano():
    """FM piano off-beat chord stabs, a push into each bar."""
    voicing = [(64, 67, 72), (64, 69, 72), (65, 69, 72), (62, 67, 71)]
    events = []
    for bar, chord in enumerate(voicing):
        o = bar * 4
        for b, vol in ((0.5, 0.0), (1.5, -2.0), (2.25, -4.0), (2.75, -1.0), (3.5, -2.0)):
            if bar == 3 and b == 3.5:
                continue
            events += [X("fmpiano", o + b, m, 0.3, vol - 3.0) for m in chord]
    return events


def _horn_section():
    """Horns hold the chord's upper notes, trombone the root; short hits on the 'and' of 3 and on 4."""
    rows = [((64, 67), 48), ((64, 69), 45), ((65, 69), 41), ((62, 67), 43)]
    events = []
    for bar, (horns, root) in enumerate(rows):
        o = bar * 4
        events += [X("horn", o, m, 1.5, -1.0) for m in horns] + [X("trombone", o, root, 1.5, 0.0)]
        events += [X("horn", o + 2.5, m, 0.3, -3.0) for m in horns] + [X("trombone", o + 2.5, root, 0.3, -2.0)]
        if bar < 3:
            events += [X("horn", o + 3.0, m, 0.3, -2.0) for m in horns] + [X("trombone", o + 3.0, root + 12, 0.3, -2.0)]
        else:
            events += [X("horn", o + 3.0, m, 0.6, 0.0) for m in (62, 65, 71)] + [X("trombone", o + 3.0, 43, 0.6, 1.0)]
    return events


def _chip_bass(bars=range(4)):
    """Triangle bass: root / octave 8ths."""
    roots = [48, 45, 41, 43]
    return [X("chipbass", bar * 4 + i * 0.5, roots[bar] + (12 if i % 2 else 0), 0.3, 0.0 if i % 2 == 0 else -3.0)
            for bar in bars for i in range(8) if not (bar == 3 and i == 7)]


def _jingle():
    """Sleigh bells on the off-beats, tambourine on 2 and 4, a soft muted triangle in 16ths, open triangle per 2 bars."""
    events = []
    for bar in range(4):
        o = bar * 4
        for b in range(4):
            events.append(P(o + b + 0.5, "sleigh", vol=-1.0))
            if b in (1, 3) and not (bar == 3 and b == 3):
                events.append(P(o + b, "tamb"))
            events.append(P(o + b + 0.25, "triangle_mute", vol=-6.0))
            if not (bar == 3 and b == 3):
                events.append(P(o + b + 0.75, "triangle_mute", vol=-8.0))
        if bar % 2 == 0:
            events.append(P(o, "triangle", vol=-2.0, length=3.0))
    events.append(P(15.0, "tamb", vol=1.0))
    return events


def _toy_box():
    """Wood clicks and log drums trade a little tune, slapstick on 2 and 4, slide whistles up and down, an anvil to end."""
    events = []
    for bar in range(4):
        o = bar * 4
        events += [P(o + b, "wood", vol=-2.0 if b % 1 else 0.0) for b in (0.0, 0.75, 1.5, 2.0, 2.75)]
        events += [P(o + 1.0, "slapstick", vol=-4.0), P(o + 3.0, "slapstick", vol=-4.0)]
        if bar != 3:
            events += [P(o + 0.5, "log_hi"), P(o + 2.5, "log_lo"), P(o + 3.5, "log_hi", vol=-2.0)]
    events.append(P(6.5, "whistle_up", length=1.0))
    events.append(P(13.0, "whistle_down", length=3.0))
    events.append(P(12.0, "anvil", vol=-5.0))
    events.append(P(15.0, "anvil", vol=-7.0))
    return events


def _fever_chip():
    """16th chip pluck arpeggio up and down each chord, the triangle bass under it, a pulse-lead hook on top."""
    arps = [(60, 64, 67), (57, 60, 64), (57, 60, 65), (55, 59, 62)]
    events = []
    for bar, (a, b, c) in enumerate(arps):
        seq = [a, b, c, a + 12, b + 12, c + 12, a + 12, c]
        seq = seq + seq[::-1]
        for i, m in enumerate(seq):
            if bar == 3 and i == 15:
                continue
            events.append(X("chippluck", bar * 4 + i * 0.25, m, 0.2, 0.0 if i % 4 == 0 else -4.0))
    hook = [(0.0, 79, 0.9), (1.0, 76, 0.4), (1.5, 79, 0.4), (2.5, 84, 0.9), (3.5, 79, 0.4)]
    events += riff("chiplead", [(b, m, length, -2.0) for b, m, length in hook],
                   last=[(0.0, 79, 0.9, -2.0), (1.0, 83, 0.9, -1.0), (2.0, 74, 0.4, -2.0), (2.5, 79, 0.9, 0.0)])
    return events + _chip_bass()


NEW_LOOPS = [
    # 4 bars: tenor sax riff, a bluesy pickup into each chord.
    loop(19, "Sax Groove", 0, 4,
         *riff("sax", [(0.0, 67, 0.4, 1.0), (0.5, 72, 0.3, 0.0), (1.0, 71, 0.25, -2.0), (1.5, 72, 0.4, 0.0),
                       (2.5, 76, 0.4, 1.0), (3.0, 74, 0.3, -1.0), (3.5, 72, 0.3, -2.0)],
               last=[(0.0, 67, 0.4, 1.0), (0.5, 71, 0.3, 0.0), (1.0, 74, 0.3, 0.0), (1.5, 77, 0.4, 1.0),
                     (2.0, 79, 0.9, 2.0), (3.0, 74, 0.3, -1.0), (3.5, 71, 0.3, -1.0)])),

    # 2 bars, bars 1-2 (C, Am): swung clarinet tune.
    loop(20, "Clarinet Swing", 0, 2,
         X("clarinet", 0.0, 76), X("clarinet", 0.67, 79, vol=-2.0), X("clarinet", 1.0, 76), X("clarinet", 1.67, 72, vol=-2.0),
         X("clarinet", 2.0, 74), X("clarinet", 2.67, 76, vol=-2.0), X("clarinet", 3.0, 79, 0.9, 1.0),
         X("clarinet", 4.0, 81, vol=1.0), X("clarinet", 4.67, 79, vol=-2.0), X("clarinet", 5.0, 76),
         X("clarinet", 5.67, 72, vol=-2.0), X("clarinet", 6.0, 76, 1.5, 1.0), X("clarinet", 7.5, 72, vol=-2.0)),

    # 4 bars: a light flute line high above the bed, answered by itself an octave down in bar 4.
    loop(21, "Flute Breeze", 0, 4,
         *riff("flute", [(0.5, 84, 0.4, 0.0), (1.0, 88, 0.4, 0.0), (1.5, 91, 0.4, 1.0), (2.5, 88, 0.4, -1.0),
                         (3.0, 84, 0.4, -2.0)],
               last=[(0.5, 74, 0.4, 0.0), (1.0, 79, 0.4, 0.0), (1.5, 83, 0.4, 1.0), (2.0, 86, 0.9, 1.0),
                     (3.0, 83, 0.4, -1.0)])),

    # 4 bars: french horns and trombone, a warm brass section on every chord.
    loop(22, "Horn Section", 0, 4, *_horn_section()),

    # 4 bars: FM piano off-beat house stabs.
    loop(23, "FM Piano Stabs", 0, 4, *_house_piano()),

    # 4 bars: an 8-bit adventure tune: pulse lead, triangle bass.
    loop(24, "Chip Quest", 0, 4,
         X("chiplead", 0.0, 72), X("chiplead", 0.5, 76, 0.2), X("chiplead", 0.75, 79, 0.2), X("chiplead", 1.0, 84),
         X("chiplead", 2.0, 79), X("chiplead", 3.0, 76, 0.25), X("chiplead", 3.5, 79, 0.25),
         X("chiplead", 4.0, 81, 0.9), X("chiplead", 5.0, 76), X("chiplead", 6.0, 72), X("chiplead", 7.0, 76),
         X("chiplead", 8.0, 77), X("chiplead", 8.5, 81, 0.4), X("chiplead", 9.0, 84, 0.9), X("chiplead", 10.0, 81),
         X("chiplead", 11.0, 77, 0.25), X("chiplead", 11.5, 79, 0.25),
         X("chiplead", 12.0, 79), X("chiplead", 12.5, 83, 0.2), X("chiplead", 13.0, 79), X("chiplead", 14.0, 74),
         X("chiplead", 15.0, 71, 0.4),
         *_chip_bass()),

    # 4 bars: a "la la" voice singing a hook.
    loop(25, "La La Choir", 0, 4,
         X("lala", 0.0, 67, 0.9), X("lala", 1.0, 64, 0.45), X("lala", 1.5, 67, 0.45), X("lala", 2.0, 72, 1.4),
         X("lala", 3.5, 71, 0.4, -2.0),
         X("lala", 4.0, 69, 0.9), X("lala", 5.0, 64, 0.45), X("lala", 5.5, 69, 0.45), X("lala", 6.0, 72, 1.4),
         X("lala", 7.5, 71, 0.4, -2.0),
         X("lala", 8.0, 69, 0.9), X("lala", 9.0, 65, 0.45), X("lala", 9.5, 69, 0.45), X("lala", 10.0, 72, 1.4),
         X("lala", 11.5, 74, 0.4, -2.0),
         X("lala", 12.0, 71, 0.9), X("lala", 13.0, 67, 0.45), X("lala", 13.5, 71, 0.45), X("lala", 14.0, 74, 1.6)),

    # 2 bars, bars 3-4 (F, G): dreamy tine-piano arpeggios.
    loop(26, "Tine Keys", 2, 2,
         X("tine", 8.0, 53), X("tine", 8.5, 60), X("tine", 9.0, 65), X("tine", 9.5, 69), X("tine", 10.0, 72, 1.0, 1.0),
         X("tine", 11.0, 69, vol=-2.0),
         X("tine", 12.0, 55), X("tine", 12.5, 59), X("tine", 13.0, 62), X("tine", 13.5, 67), X("tine", 14.0, 71, 1.0, 1.0),
         X("tine", 15.0, 67, vol=-2.0)),

    # 4 bars: interlocking mbira figure.
    loop(27, "Mbira Ripple", 0, 4,
         *riff("mbira", [(0.0, 72, 0.3, 0.0), (0.5, 79, 0.3, -2.0), (0.75, 76, 0.3, -3.0), (1.25, 72, 0.3, -2.0),
                         (1.5, 79, 0.3, -1.0), (2.0, 84, 0.3, 0.0), (2.5, 79, 0.3, -2.0), (2.75, 76, 0.3, -3.0),
                         (3.25, 72, 0.3, -2.0), (3.5, 76, 0.3, -1.0)])),

    # 4 bars: sleigh bells, tambourine and triangles.
    loop(28, "Jingle Sparkle", 0, 4, *_jingle()),

    # 4 bars: toy percussion - wood clicks, log drums, slapstick, slide whistles, an anvil.
    loop(29, "Toy Box", 0, 4, *_toy_box()),

    # 4 bars: squelchy acid line in 8ths and 16ths.
    loop(30, "Acid Line", 0, 4,
         *riff("acid", [(0.0, 36, 0.4, 0.0), (0.5, 36, 0.2, -3.0), (0.75, 43, 0.2, -2.0), (1.0, 36, 0.4, -1.0),
                        (1.5, 40, 0.2, -2.0), (2.0, 36, 0.4, 0.0), (2.5, 43, 0.2, -2.0), (3.0, 36, 0.2, -1.0),
                        (3.25, 36, 0.2, -3.0), (3.5, 40, 0.3, -1.0)])),

    # 1 bar, bar 4 (G): a surf-twang guitar lick into the next phrase.
    loop(31, "Twang Lick", 3, 1,
         X("twang", 12.0, 59), X("twang", 12.25, 62, vol=-2.0), X("twang", 12.5, 67), X("twang", 13.0, 71, vol=1.0),
         X("twang", 13.5, 67), X("twang", 14.0, 62), X("twang", 14.5, 59), X("twang", 15.0, 55, vol=1.0),
         X("twang", 15.5, 62)),

    # Fever: honking tenor sax riff over the whole fever.
    loop(32, "Fever Sax Blast", 0, 4,
         *riff("sax", [(0.0, 72, 0.3, 1.0), (0.5, 72, 0.2, -2.0), (0.75, 76, 0.3, 0.0), (1.5, 79, 0.4, 1.0),
                       (2.5, 76, 0.3, -1.0), (3.0, 79, 0.3, 0.0), (3.5, 84, 0.3, 1.0)],
               last=[(0.0, 79, 0.3, 1.0), (0.5, 79, 0.2, -2.0), (0.75, 83, 0.3, 0.0), (1.5, 86, 0.4, 1.0),
                     (2.5, 83, 0.3, -1.0), (3.0, 79, 0.4, 1.0)]),
         fever=True),

    # Fever: 8-bit rush - 16th chip arpeggios, triangle bass and a pulse-lead hook.
    loop(33, "Fever Chip Rush", 0, 4, *_fever_chip(), fever=True),
]
LOOPS += NEW_LOOPS
assert [l["id"] for l in LOOPS] == list(range(1, len(LOOPS) + 1)), "loop ids must be 1..N"


def _clips():
    out = []
    for lp in LOOPS:
        layers = {INSTRUMENTS[n[0]][0] for n in lp["notes"]}
        fever = ("Impact", "Fever", "Outro") if lp["fever"] else ("None", "Build", "Impact", "Fever", "Outro")
        for layer in LAYER_ORDER:
            if layer not in layers:
                continue

            def make(p, lp=lp, layer=layer):
                notes = []
                for n in moved_notes([tuple(x) for x in lp["notes"]], p):
                    lay, pn = note(*n, trim=FEVER_GUEST_DB.get(lp["id"], LOOP_DB.get(lp["id"], 0.0)))
                    if lay == layer:
                        notes.append(pn)
                return notes

            name = f"Loop{lp['id']:02d}{layer.split()[1].capitalize()}"
            out += expand(name, layer, make, feature_loop=lp["id"], legacy_shift="onset_not_beat0", fever=fever)
    return out


CLIPS = _clips()
