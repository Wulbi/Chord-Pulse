"""
DATA MODULE since stages 3-8 Task 15b (read by bed_notes.py (RANGES, ghosts, fill)). The FMOD script described below (bed_variation.js) and its
timeline tracks were deleted; running this file only prints that.

Adaptive bed, axis 3: per-phrase pattern variations for event:/Music/ChordPulseGame (Docs/AdaptiveAudioPlan.md 8-3).

Writes bed_variation.js next to this file. Run it on the FMOD project (FMOD Studio closed) with
    fmodstudiocl.exe -script FMOD/Tools/bed_variation.js FMOD/ChordPulse/ChordPulse.fspro
It is idempotent: notes it added before are removed and the conditions it put on the original notes are reset.

Game parameter Variation (0..4, one value per 4-bar phrase, chosen by Unity one phrase ahead):

    Variation   bass                     pluck                       drums (added to the snare)
    0           A  original half notes   A  original off-beat 8ths   A  nothing (original)
    1           B  8th-note drive        A                           A
    2           B  8th-note drive        A                           C  ghost notes + bar-4 fill
    3           C  syncopated + walk     B  straight 8th arpeggio    B  ghost notes
    4           C  syncopated + walk     C  tresillo chord stabs     B  ghost notes

The table is ordered so every pattern is used by a contiguous range of values: each note needs only one
range condition (FMOD conditions are AND-ed), and the original bass/pluck notes just get a range too.
The bass variants live on the original bass track, so the Tension bass/acid crossfade still applies.
Unity switches Variation shortly before a phrase boundary (lookahead), so no pattern has a note on the
last 16th of a phrase; fills end on the "and" of beat 4.
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

BASS_FILE, BASS_ROOT = "AT101V_101Bass_C2_v100.wav", 36
PLUCK_FILE, PLUCK_ROOT = "AT101V_PolyPluck_C4_v100.wav", 60
SNARE_FILE = "Pallor_Snare_Synthpop_v115.wav"

# Variation ranges each pattern is used for (inclusive).
RANGES = {
    "bassA": (0, 0), "bassB": (1, 2), "bassC": (3, 4),
    "pluckA": (0, 2), "pluckB": (3, 3), "pluckC": (4, 4),
    "drumsGhost": (2, 4), "drumsFill": (2, 2),
}

# Per bar of the phrase (C, Am, F, G): bass root, fifth, approach note into the next bar, pluck chord.
BARS = [
    dict(root=36, fifth=43, approach=35, chord=(60, 64, 67)),   # C:  C2 G2, B1 -> A1;  C4 E4 G4
    dict(root=33, fifth=40, approach=31, chord=(57, 60, 64)),   # Am: A1 E2, G1 -> F1;  A3 C4 E4
    dict(root=29, fifth=36, approach=33, chord=(57, 60, 65)),   # F:  F1 C2, A1 -> G1;  A3 C4 F4
    dict(root=31, fifth=38, approach=35, chord=(55, 59, 62)),   # G:  G1 D2, B1 -> C2;  G3 B3 D4
]


def bass_b(bar):
    """8th-note drive: root with an octave pop on the 'and' of 2 and 4."""
    b = BARS[bar]
    notes = []
    for i in range(8):
        octave = i in (3, 7)
        notes.append((i * 0.5, b["root"] + (12 if octave else 0), 0.4, -10.0 if i % 2 == 0 else -13.0))
    return notes


def bass_c(bar):
    """Syncopated: long root, push on 1.5, fifth on 2.5, walk into the next chord on 3.5."""
    b = BARS[bar]
    return [(0.0, b["root"], 1.3, -8.0), (1.5, b["root"], 0.45, -10.0), (2.5, b["fifth"], 0.45, -9.0),
            (3.5, b["approach"], 0.45, -10.0)]


def pluck_b(bar):
    """Straight 8th arpeggio up and down the chord, quieter than the off-beat pattern."""
    c = BARS[bar]["chord"]
    seq = [c[0], c[1], c[2], c[0] + 12, c[2], c[1], c[0], c[1]]
    return [(i * 0.5, m, 0.4, -21.0 if i % 2 else -20.0) for i, m in enumerate(seq)]


def pluck_c(bar):
    """Tresillo stabs (beats 1, 2.5, 4): the whole triad at once."""
    c = BARS[bar]["chord"]
    return [(b, m, 0.45, -23.0) for b in (0.0, 1.5, 3.0) for m in c]


def ghosts(bar):
    """Quiet snare ghosts on the 'e' before beat 2 and the 'and' of 3."""
    return [(0.75, -30.0), (2.5, -28.0)] + ([(3.75, -31.0)] if bar != 3 else [])


def fill(bar):
    """Bar 4 only: 16th snare crescendo from beat 3, ending on the 'and' of 4."""
    if bar != 3:
        return []
    steps = [2.0, 2.25, 2.5, 2.75, 3.0, 3.25, 3.5]
    return [(s, -26.0 + 10.0 * i / (len(steps) - 1)) for i, s in enumerate(steps)]


def build():
    notes = []
    for start, bpm in SECTIONS:
        beat = 60.0 / bpm
        for bar in range(BARS_PER_SECTION):
            bar_start = start + bar * 4 * beat
            pb = bar % 4

            def add(track, kind, b, midi, root, length, vol, file):
                notes.append({"track": track, "range": RANGES[kind], "file": file,
                              "start": round(bar_start + b * beat, 6), "length": round(length * beat, 6),
                              "pitch": midi - root, "volume": vol, "intensity": track == "pluck"})

            for b, m, l, v in bass_b(pb):
                add("bass", "bassB", b, m, BASS_ROOT, l, v, BASS_FILE)
            for b, m, l, v in bass_c(pb):
                add("bass", "bassC", b, m, BASS_ROOT, l, v, BASS_FILE)
            for b, m, l, v in pluck_b(pb):
                add("pluck", "pluckB", b, m, PLUCK_ROOT, l, v, PLUCK_FILE)
            for b, m, l, v in pluck_c(pb):
                add("pluck", "pluckC", b, m, PLUCK_ROOT, l, v, PLUCK_FILE)
            for b, v in ghosts(pb):
                add("snare", "drumsGhost", b, 0, 0, 0.5, v, SNARE_FILE)
            for b, v in fill(pb):
                add("snare", "drumsFill", b, 0, 0, 0.4, v, SNARE_FILE)
    for n in notes:
        assert -12 <= n["pitch"] <= 12, n
    return notes


# Stages 3-8 Task 15b: the timeline tracks this file built were deleted from the music event (cleanup_music.py),
# and bed_variation.js with them. Only the data above is still read, by bed_notes.py (RANGES, ghosts, fill); this file no longer writes an
# FMOD script, so running it cannot rebuild those tracks.
if __name__ == "__main__":
    raise SystemExit("bed_variation.py is a data module now (Task 15b): its timeline tracks and bed_variation.js were deleted")
