"""
Pattern data: the eight fever tracks (lead, shaker, bass fifths, build roll, build riser, impact, outro fall, outro
snare), for the PatternScheduler (Task 14). They were made by hand on the music event's timeline, so no generator ever
held them: extract_fever.py (now FMOD/Tools/retired/) read them from the event XML once (every copy - 4 tempo sections x 8 bars - was equal) and
printed the data block below. This file is the source from now on.

Fever phases (ArrangementState.Fever, ArrangementTimeline: a step function, no parameter lead):
    Build   the build-up bar (the last bar of the phrase before fever)  roll, riser    [old condition FeverBuild]
    Impact  fever's first beat                                          impact         [FeverImpact]
    Fever   the fever phrase: Impact + Fever + Outro                    lead, shaker, bass fifths   [Fever]
    Outro   fever's last bar                                            fall, snare    [FeverOutro]
Each clip is a 16-beat phrase like the timeline copy: 1-bar tracks are repeated in every bar, 4-bar tracks follow the
chords (C Am F G); a phase only lets its own bars through (the riser and the fall sound only in bar 4, G).

Layers = the eight old tracks (deleted in stages 3-8 Task 15b like the other migrated tracks). The tracks had no effects and no
ADSR: the lead, riser and fall played into the event's Harmony group (-> bus:/Music Bed/Harmony, ducked by the kill
chords, one block late like every Harmony layer), the rest into Music Bed (-> bus:/Music Bed/Layers). Every note keeps
its timeline length (stop with the 5 ms region-end fade) and its dB plus its track's fader (TRACK_DB; the one-shot path
has no track fader). The snare rolls pitch the snare by fractions of a semitone per hit, so the fever percussion is one
instrument (FeverDrums) whose table maps each Note to (sample, pitch) directly (samples.pick with a dict). Data only.
"""
import samples
from patterns import Clip, Inst, Layer, N

# ---- data block (extract_fever.py) ----
# Fever lead: 4-bar pattern; condition {'Fever': (0.5, 1.0)}; ADSR None; track fader 0.0 dB
LEAD = [   # (beat in the phrase, MIDI, length beats, dB)
    (0.0, 72, 0.44, -15.0),
    (1.0, 76, 0.44, -15.0),
    (1.5, 79, 0.44, -15.0),
    (2.5, 76, 0.44, -15.0),
    (3.0, 79, 0.44, -15.0),
    (4.0, 69, 0.44, -15.0),
    (5.0, 72, 0.44, -15.0),
    (5.5, 76, 0.44, -15.0),
    (6.5, 72, 0.44, -15.0),
    (7.0, 76, 0.44, -15.0),
    (8.0, 65, 0.44, -15.0),
    (9.0, 69, 0.44, -15.0),
    (9.5, 72, 0.44, -15.0),
    (10.5, 69, 0.44, -15.0),
    (11.0, 72, 0.44, -15.0),
    (12.0, 67, 0.44, -15.0),
    (13.0, 71, 0.44, -15.0),
    (13.5, 74, 0.44, -15.0),
    (14.5, 71, 0.44, -15.0),
    (15.0, 74, 0.44, -15.0),
]
# Fever shaker: 1-bar pattern; condition {'Fever': (0.5, 1.0)}; ADSR None; track fader 0.0 dB
SHAKER = [   # (beat in the bar, pitch semitones, length beats, dB)
    (0.25, 0.0, 0.2, -18.0),
    (0.75, 0.0, 0.2, -20.0),
    (1.25, 0.0, 0.2, -18.0),
    (1.75, 0.0, 0.2, -20.0),
    (2.25, 0.0, 0.2, -18.0),
    (2.75, 0.0, 0.2, -20.0),
    (3.25, 0.0, 0.2, -18.0),
    (3.75, 0.0, 0.2, -20.0),
]
# Fever bass fifths: 4-bar pattern; condition {'Fever': (0.5, 1.0)}; ADSR None; track fader 0.0 dB
BASS_FIFTHS = [   # (beat in the phrase, MIDI, length beats, dB)
    (1.0, 43, 0.85, -10.0),
    (3.0, 43, 0.85, -10.0),
    (5.0, 40, 0.85, -10.0),
    (7.0, 40, 0.85, -10.0),
    (9.0, 36, 0.85, -10.0),
    (11.0, 36, 0.85, -10.0),
    (13.0, 38, 0.85, -10.0),
    (15.0, 38, 0.85, -10.0),
]
# Fever build roll: 1-bar pattern; condition {'FeverBuild': (0.5, 1.0)}; ADSR None; track fader -2.0 dB
BUILD_ROLL = [   # (beat in the bar, pitch semitones, length beats, dB)
    (0.0, 0.0, 0.2375, -30.0),
    (0.5, 0.363636374, 0.2375, -28.545454),
    (1.0, 0.727272749, 0.2375, -27.09091),
    (1.5, 1.09090912, 0.2375, -25.636364),
    (2.0, 1.4545455, 0.2375, -24.181818),
    (2.25, 1.81818187, 0.2375, -22.727272),
    (2.5, 2.18181825, 0.2375, -21.272728),
    (2.75, 2.5454545, 0.2375, -19.818182),
    (3.0, 2.909091, 0.2375, -18.363636),
    (3.25, 3.27272725, 0.2375, -16.9090919),
    (3.5, 3.63636374, 0.2375, -15.454546),
    (3.75, 4.0, 0.2375, -14.0),
]
# Fever build riser: 4-bar pattern; condition {'FeverBuild': (0.5, 1.0)}; ADSR None; track fader -4.0 dB
BUILD_RISER = [   # (beat in the phrase, MIDI, length beats, dB)
    (2.0, 60, 0.375, -22.0),
    (2.25, 64, 0.375, -20.0),
    (2.5, 67, 0.375, -18.0),
    (2.75, 72, 0.375, -16.0),
    (3.0, 76, 0.375, -14.0),
    (3.25, 79, 0.375, -12.0),
    (3.5, 84, 0.375, -10.0),
    (3.75, 84, 0.375, -8.0),
    (6.0, 57, 0.375, -22.0),
    (6.25, 60, 0.375, -20.0),
    (6.5, 64, 0.375, -18.0),
    (6.75, 69, 0.375, -16.0),
    (7.0, 72, 0.375, -14.0),
    (7.25, 76, 0.375, -12.0),
    (7.5, 81, 0.375, -10.0),
    (7.75, 84, 0.375, -8.0),
    (10.0, 65, 0.375, -22.0),
    (10.25, 69, 0.375, -20.0),
    (10.5, 72, 0.375, -18.0),
    (10.75, 77, 0.375, -16.0),
    (11.0, 81, 0.375, -14.0),
    (11.25, 84, 0.375, -12.0),
    (11.5, 84, 0.375, -10.0),
    (11.75, 84, 0.375, -8.0),
    (14.0, 55, 0.375, -22.0),
    (14.25, 59, 0.375, -20.0),
    (14.5, 62, 0.375, -18.0),
    (14.75, 67, 0.375, -16.0),
    (15.0, 71, 0.375, -14.0),
    (15.25, 74, 0.375, -12.0),
    (15.5, 79, 0.375, -10.0),
    (15.75, 83, 0.375, -8.0),
]
# Fever impact: 1-bar pattern; condition {'FeverImpact': (0.5, 1.0)}; ADSR None; track fader -3.0 dB
IMPACT = [   # (beat in the bar, sample, length beats, dB)
    (0.0, 'Pallor_Clap_Synthpop_v110.wav', 1.0, -12.0),
    (0.0, 'Pallor_Kick_Synthpop_v115.wav', 1.0, 0.0),
    (0.0, 'Pallor_OpenHat_Synthpop_v110.wav', 2.0, -4.0),
]
# Fever outro fall: 4-bar pattern; condition {'FeverOutro': (0.5, 1.0)}; ADSR None; track fader -4.0 dB
OUTRO_FALL = [   # (beat in the phrase, MIDI, length beats, dB)
    (2.0, 84, 0.375, -8.0),
    (2.25, 84, 0.375, -10.0),
    (2.5, 79, 0.375, -12.0),
    (2.75, 76, 0.375, -14.0),
    (3.0, 72, 0.375, -16.0),
    (3.25, 67, 0.375, -18.0),
    (3.5, 64, 0.375, -20.0),
    (3.75, 60, 0.375, -22.0),
    (6.0, 84, 0.375, -8.0),
    (6.25, 81, 0.375, -10.0),
    (6.5, 76, 0.375, -12.0),
    (6.75, 72, 0.375, -14.0),
    (7.0, 69, 0.375, -16.0),
    (7.25, 64, 0.375, -18.0),
    (7.5, 60, 0.375, -20.0),
    (7.75, 57, 0.375, -22.0),
    (10.0, 84, 0.375, -8.0),
    (10.25, 84, 0.375, -10.0),
    (10.5, 84, 0.375, -12.0),
    (10.75, 81, 0.375, -14.0),
    (11.0, 77, 0.375, -16.0),
    (11.25, 72, 0.375, -18.0),
    (11.5, 69, 0.375, -20.0),
    (11.75, 65, 0.375, -22.0),
    (14.0, 83, 0.375, -8.0),
    (14.25, 79, 0.375, -10.0),
    (14.5, 74, 0.375, -12.0),
    (14.75, 71, 0.375, -14.0),
    (15.0, 67, 0.375, -16.0),
    (15.25, 62, 0.375, -18.0),
    (15.5, 59, 0.375, -20.0),
    (15.75, 55, 0.375, -22.0),
]
# Fever outro snare: 1-bar pattern; condition {'FeverOutro': (0.5, 1.0)}; ADSR None; track fader -4.0 dB
OUTRO_SNARE = [   # (beat in the bar, pitch semitones, length beats, dB)
    (2.0, 0.0, 0.2375, -16.0),
    (2.25, -0.571428597, 0.2375, -18.5714283),
    (2.5, -1.14285719, 0.2375, -21.1428566),
    (2.75, -1.71428573, 0.2375, -23.7142868),
    (3.0, -2.28571439, 0.2375, -26.2857151),
    (3.25, -2.85714293, 0.2375, -28.8571434),
    (3.5, -3.42857146, 0.2375, -31.4285717),
    (3.75, -4.0, 0.2375, -34.0),
]
# ---- end of the data block ----

LAYERS_BUS, HARMONY = "Music Bed/Layers", "Music Bed/Harmony"
TRACK_DB = {"Fever build roll": -2.0, "Fever build riser": -4.0, "Fever impact": -3.0, "Fever outro fall": -4.0,
            "Fever outro snare": -4.0}

# The fever percussion: Note -> (samples.PERC entry, pitch). Unpitched sounds keep their drum-kit number; each other
# snare pitch of the rolls gets a note from SNARE_NOTE_BASE up, in pitch order.
SNARE_NOTE_BASE = 100
_PERC = {s[0]: s for s in samples.PERC}
_BY_FILE = {"Pallor_Kick_Synthpop_v115.wav": samples.KICK, "Pallor_Clap_Synthpop_v110.wav": samples.CLAP,
            "Pallor_OpenHat_Synthpop_v110.wav": samples.OPEN_HAT}
_SNARE = _PERC["Pallor_Snare_Synthpop_v115.wav"]
_SNARE_PITCHES = sorted({p for _, p, _, _ in BUILD_ROLL + OUTRO_SNARE} - {0.0})
FEVER_DRUMS_TABLE = {samples.SHAKER: (_PERC["Pallor_Shaker_Synthpop_v110.wav"], 0), samples.SNARE: (_SNARE, 0)}
FEVER_DRUMS_TABLE.update({n: (_PERC[f], 0) for f, n in _BY_FILE.items()})
FEVER_DRUMS_TABLE.update({SNARE_NOTE_BASE + i: (_SNARE, p) for i, p in enumerate(_SNARE_PITCHES)})
assert SNARE_NOTE_BASE + len(_SNARE_PITCHES) <= 128


def snare_note(pitch):
    return samples.SNARE if pitch == 0 else SNARE_NOTE_BASE + _SNARE_PITCHES.index(pitch)


# One (instrument, bus) event each, no ADSR (attack 0 / release 0 like the tracks).
LEAD_INST = Inst("FeverLead", samples.ORIGINAL["Red"], HARMONY, 0, 0, max_pitch=24)
PLUCK_INST = Inst("FeverPluck", samples.PLUCK, HARMONY, 0, 0, max_pitch=24)
BASS_INST = Inst("Bass", samples.BASS, LAYERS_BUS, 0, 0)
DRUMS_INST = Inst("FeverDrums", FEVER_DRUMS_TABLE, LAYERS_BUS, 0, 0, max_pitch=4)

FEVER_ON = ("Impact", "Fever", "Outro")   # the old Fever parameter: on for the whole fever phrase

LAYERS = [Layer(t, t) for t in ("Fever lead", "Fever shaker", "Fever bass fifths", "Fever build roll",
                                "Fever build riser", "Fever impact", "Fever outro fall", "Fever outro snare")]


def phrase(layer, rows, inst):
    """A 4-bar track: (beat in the phrase, MIDI, length beats, dB)."""
    return [N(b, m, length, v + TRACK_DB.get(layer, 0.0), inst) for b, m, length, v in rows]


def every_bar(layer, rows, note_of):
    """A 1-bar track repeated in the 4 bars of the phrase: (beat in the bar, x, length beats, dB); note_of(x) -> Note."""
    return [N(bar * 4 + b, note_of(x), length, v + TRACK_DB.get(layer, 0.0), DRUMS_INST)
            for bar in range(4) for b, x, length, v in rows]


CLIPS = [
    Clip("FeverLead", "Fever lead", phrase("Fever lead", LEAD, LEAD_INST), fever=FEVER_ON),
    Clip("FeverShaker", "Fever shaker", every_bar("Fever shaker", SHAKER, lambda p: samples.SHAKER), fever=FEVER_ON),
    Clip("FeverBassFifths", "Fever bass fifths", phrase("Fever bass fifths", BASS_FIFTHS, BASS_INST), fever=FEVER_ON),
    Clip("FeverBuildRoll", "Fever build roll", every_bar("Fever build roll", BUILD_ROLL, snare_note), fever=("Build",)),
    Clip("FeverBuildRiser", "Fever build riser", phrase("Fever build riser", BUILD_RISER, PLUCK_INST), fever=("Build",)),
    Clip("FeverImpact", "Fever impact", every_bar("Fever impact", IMPACT, lambda f: _BY_FILE[f]), fever=("Impact",)),
    Clip("FeverOutroFall", "Fever outro fall", phrase("Fever outro fall", OUTRO_FALL, PLUCK_INST), fever=("Outro",)),
    Clip("FeverOutroSnare", "Fever outro snare", every_bar("Fever outro snare", OUTRO_SNARE, snare_note),
         fever=("Outro",)),
]
