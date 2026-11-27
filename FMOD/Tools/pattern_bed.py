"""
Pattern data: the background bed's pitched layers, one 4-bar phrase (16 beats) each, for the PatternScheduler.

Layers (the deleted timeline track each replaced in brackets; those tracks, their generators and the --compare-legacy
check are gone since stages 3-8 Task 15b, the music event since unity-music-clock Task 8):
    Bass             A original half notes / B drive / C walk (synthpop), Latin tumbao, Funk rubber bass  [Bass - C Am F G]
    Pluck            A original off-beat 8ths (two phrases), B arpeggio, C tresillo stabs               [Offbeat pluck]
    Combo pad, Combo bells                                                                              [Combo pad / bells]
    Tension strings, Tension sub, Tension acid  (always scheduled; the sub-bus Tension curves decide what is heard)
    Latin marimba, Funk keys, Funk guitar, Dreamy choir, Dreamy bells, Dreamy sub

Values moved from bed_variation.py, bed_combo.py, bed_tension.py and bed_style.py. Bass A and Pluck A were never
generated: they are the hand-made original notes of those two tracks, copied here from the event XML (compare-legacy
checked every copy in the event matched, before the tracks were deleted). Pluck A is an 8-bar pattern, so it is two clips (SectionPhrase 1 / 2).
Every part is built once per chord progression (progressions.py, patterns.expand): progression 0 is the original
C - Am - F - G and plays exactly the original notes.

Each note's volume is the dB the old timeline note had (target - sample RMS + accent, clamped to [-60, 10] like the old
generators, or the direct note volume); patterns.py splits it into the instrument event's NORMALIZE_DB - RMS plus GainDb.
Data only (plus the small helpers that turn the old generator formulas into notes); no numpy.
"""
import progressions as P
import samples
from patterns import STYLE_COUNT, Inst, Layer, N, expand

# --- buses (mixer.py SUB_BUSES): each old track with its own effect or automation has its own sub-bus ---------------
BASS_BUS = "Music Bed/Layers/Bass"                       # Tension crossfade (bass -> acid)
STRINGS_BUS = "Music Bed/Layers/Tension Strings"
SUB_BUS = "Music Bed/Layers/Tension Sub"
ACID_BUS = "Music Bed/Layers/Tension Acid"
LAYERS_BUS = "Music Bed/Layers"                          # Music Bed tracks without own effects (Dreamy sub)
HARMONY = "Music Bed/Harmony"                            # Harmony tracks without own effects (pluck, combo bells)
PAD_BUS = "Music Bed/Harmony/Combo Pad"
MARIMBA_BUS = "Music Bed/Harmony/Latin Marimba"
KEYS_BUS = "Music Bed/Harmony/Funk Keys"
GUITAR_BUS = "Music Bed/Harmony/Funk Guitar"
CHOIR_BUS = "Music Bed/Harmony/Dreamy Choir"
DREAMY_BELLS_BUS = "Music Bed/Harmony/Dreamy Bells"

# --- instruments: one (name, bus) event each; attack / release = the old track's ADSR (0 / 0 = the track had none) ---
BASS = Inst("Bass", samples.BASS, BASS_BUS, 0, 0)
BASS_ENV = Inst("BassEnv", samples.BASS, BASS_BUS, 2, 60)          # Latin tumbao: bed_style's bass ADSR
RUBBER = Inst("Rubber", samples.RUBBER, BASS_BUS, 2, 60)           # Funk bass
PLUCK = Inst("Pluck", samples.PLUCK, HARMONY, 0, 0)
PAD = Inst("GlassPad", samples.GLASS_PAD, PAD_BUS, 300, 500, max_pitch=24)
COMBO_BELLS = Inst("Bells", samples.BELLS, HARMONY, 2, 250)
STRINGS = Inst("Strings", samples.STRINGS, STRINGS_BUS, 250, 400)
TENSION_SUB = Inst("SineSub", samples.SINE_SUB, SUB_BUS, 3, 60)
ACID = Inst("Acid", samples.ACID, ACID_BUS, 2, 50)
MARIMBA = Inst("Marimba", samples.MARIMBA, MARIMBA_BUS, 0, 0)
CLAV = Inst("ClavHigh", samples.CLAV_HIGH, KEYS_BUS, 0, 0)
GUITAR = Inst("Guitar", samples.GUITAR, GUITAR_BUS, 0, 0)
CHOIR = Inst("Choir", samples.CHOIR, CHOIR_BUS, 200, 600)
DREAMY_BELLS = Inst("Bells", samples.BELLS, DREAMY_BELLS_BUS, 0, 0)
DREAMY_SUB = Inst("SineSub", samples.SINE_SUB, LAYERS_BUS, 200, 600)

SYNTHPOP, LATIN, FUNK, DREAMY = 0, 1, 2, 3
assert STYLE_COUNT == 4

LAYERS = [
    Layer("Bass", "Bass - C Am F G"), Layer("Pluck", "Offbeat pluck"),
    Layer("Combo pad", "Combo pad"), Layer("Combo bells", "Combo bells"),
    Layer("Tension strings", "Tension strings"), Layer("Tension sub", "Tension sub"),
    Layer("Tension acid", "Tension acid bass"),
    Layer("Latin marimba", "Latin marimba"), Layer("Funk keys", "Funk keys"), Layer("Funk guitar", "Funk guitar"),
    Layer("Dreamy choir", "Dreamy choir"), Layer("Dreamy bells", "Dreamy bells"), Layer("Dreamy sub", "Dreamy sub"),
]

BARS = 4


def vol(target, table, midi, accent=0.0):
    """Old timeline note volume: target - RMS of the sample the note uses + accent (patterns.py clamps and warns)."""
    s, _ = samples.pick(table, midi, 24)
    return target - s[2] + accent


# Chord progressions (progressions.py): every pattern below gets the bar's chord name (and the next bar's, for walks
# and anticipations) instead of the bar number. Bass-like parts use the fixed bass register (progressions.BASS_ROOT);
# voicing tables are written for the original four chords and progressions.derive() adds Dm and Em. On progression 0
# (C Am F G) every note is the one the original bar played.
R = P.BASS_ROOT


def fits(**insts):
    """ok() for progressions.derive: every field's notes inside its instrument's range (fits(_=inst) for a plain tuple)."""
    def notes(v):
        return [v] if isinstance(v, int) else list(v)

    def ok(entry):
        try:
            for key, inst in insts.items():
                for m in notes(entry if key == "_" else entry[key]):
                    inst.sample(m)
            return True
        except AssertionError:
            return False
    return ok


# ===== bass / pluck (bed_variation.py) ==================================================================================
PLUCK_CHORD = P.derive({"C": (60, 64, 67), "Am": (57, 60, 64), "F": (57, 60, 65), "G": (55, 59, 62)}, fits(_=PLUCK))


def bass_a(ch, nxt):
    """Original (hand-made) bass: root half notes, 0.85 beat, -8 dB, no ADSR."""
    r = R[ch]
    return [(0.0, r, 0.85, -8.0), (2.0, r, 0.85, -8.0)]


def bass_b(ch, nxt):
    """8th-note drive: root with an octave pop on the 'and' of 2 and 4."""
    return [(i * 0.5, R[ch] + (12 if i in (3, 7) else 0), 0.4, -10.0 if i % 2 == 0 else -13.0) for i in range(8)]


def bass_c(ch, nxt):
    """Syncopated: long root, push on 1.5, fifth on 2.5, walk into the next chord on 3.5."""
    return [(0.0, R[ch], 1.3, -8.0), (1.5, R[ch], 0.45, -10.0), (2.5, P.fifth(ch), 0.45, -9.0),
            (3.5, P.approach(ch, nxt), 0.45, -10.0)]


# Original (hand-made) off-beat pluck, MIDI per off-beat of each bar; it runs over 8 bars, so a section's two phrases
# differ. A melody over the original bars: progressions move it bar by bar (progressions.remap_rows).
PLUCK_A = {
    1: [(64, 60, 67, 64), (57, 64, 60, 57), (60, 57, 53, 60), (59, 55, 62, 59)],   # bars 1-4 of a section
    2: [(60, 67, 64, 60), (64, 60, 57, 64), (57, 53, 60, 57), (55, 62, 59, 55)],   # bars 5-8
}


def pluck_a(phrase_no, p):
    rows = [(bar * 4 + i + 0.5, m, 0.44, -18.0) for bar in range(BARS) for i, m in enumerate(PLUCK_A[phrase_no][bar])]
    return [N(*r, PLUCK) for r in P.remap_rows(rows, p, lambda r: 1)]


def pluck_b(ch, nxt):
    c = PLUCK_CHORD[ch]
    seq = [c[0], c[1], c[2], c[0] + 12, c[2], c[1], c[0], c[1]]
    return [(i * 0.5, m, 0.4, -21.0 if i % 2 else -20.0) for i, m in enumerate(seq)]


def pluck_c(ch, nxt):
    return [(b, m, 0.45, -23.0) for b in (0.0, 1.5, 3.0) for m in PLUCK_CHORD[ch]]


# ===== combo (bed_combo.py) ============================================================================================
COMBO_CHORD = P.derive({
    "C": dict(pad=(67, 72, 76), bells=(76, 79, 84, 79)),
    "Am": dict(pad=(69, 72, 76), bells=(76, 81, 84, 81)),
    "F": dict(pad=(69, 72, 77), bells=(77, 81, 84, 81)),
    "G": dict(pad=(67, 71, 74), bells=(74, 79, 83, 79)),
}, fits(pad=PAD, bells=COMBO_BELLS))
COMBO_LEVEL = {"pad": -27.0, "bells": -24.0}


def combo_pad(ch, nxt):
    return [(0.0, m, 4.0, COMBO_LEVEL["pad"]) for m in COMBO_CHORD[ch]["pad"]]


def combo_bells(ch, nxt):
    return [(i + 0.5, m, 0.9, COMBO_LEVEL["bells"] - (0 if i == 0 else 2.0)) for i, m in enumerate(COMBO_CHORD[ch]["bells"])]


# ===== tension (bed_tension.py) ========================================================================================
TENSION_VOICING = P.derive({"C": (52, 55, 60), "Am": (52, 57, 60), "F": (53, 57, 60), "G": (50, 55, 59)},
                           fits(_=STRINGS))
TENSION_TARGET = {"strings": -31.0, "sub": -27.0, "acid": -33.0}
ACID_PATTERN = [0, 0, 12, 0, 0, 12, 0, 12]
SUB_BEATS = [0.5, 1.5, 2.5, 3.5]


def strings(ch, nxt):
    return [(0.0, m, 4.0, vol(TENSION_TARGET["strings"], samples.STRINGS, m)) for m in TENSION_VOICING[ch]]


def tension_sub(ch, nxt):
    r = R[ch]
    return [(b, r, 0.4, vol(TENSION_TARGET["sub"], samples.SINE_SUB, r)) for b in SUB_BEATS]


def acid(ch, nxt):
    r = R[ch]
    return [(i * 0.5, r + o, 0.4, vol(TENSION_TARGET["acid"], samples.ACID, r + o, 0.0 if i % 2 == 0 else -3.0))
            for i, o in enumerate(ACID_PATTERN)]


# ===== styles (bed_style.py) ===========================================================================================
STYLE_CHORD = P.derive({
    "C": dict(marimba=(67, 72, 76, 79), clav=(76, 79), guitar=(64, 67), choir=(60, 64, 67), bells=(76, 79)),
    "Am": dict(marimba=(69, 72, 76, 81), clav=(72, 76), guitar=(64, 69), choir=(57, 60, 64), bells=(76, 81)),
    "F": dict(marimba=(69, 72, 77, 81), clav=(77, 81), guitar=(65, 69), choir=(57, 60, 65), bells=(77, 81)),
    "G": dict(marimba=(67, 71, 74, 79), clav=(74, 79), guitar=(62, 67), choir=(55, 59, 62), bells=(74, 79)),
}, fits(marimba=MARIMBA, clav=CLAV, guitar=GUITAR, choir=CHOIR, bells=DREAMY_BELLS))
STYLE_TARGET = {"marimba": -36.0, "f_bass": -25.5, "f_clav": -35.0, "f_guitar": -37.0,
                "d_sub": -32.0, "d_choir": -39.0, "d_bells": -44.0}


def latin_bass(ch, nxt):
    return [(0.0, R[ch], 0.45, -11.0), (1.5, R[ch], 1.3, -9.0), (3.0, R[nxt], 0.9, -9.0)]


def latin_marimba(ch, nxt):
    low, mid, high, top = STYLE_CHORD[ch]["marimba"]
    return [(x, m, 0.6, vol(STYLE_TARGET["marimba"], samples.MARIMBA, m, acc))
            for x, m, acc in ((0.0, low, 0.0), (0.5, top, -2.0), (1.0, high, -1.0), (1.5, top, -2.0),
                              (2.5, mid, -1.0), (3.0, high, -1.0), (3.5, top, -2.0))]


def funk_bass(ch, nxt):
    r, f = R[ch], P.fifth(ch)
    return [(x, m, length, vol(STYLE_TARGET["f_bass"], samples.RUBBER, m, acc))
            for x, m, length, acc in ((0.0, r, 0.4, 0.0), (0.75, r, 0.2, -3.0),
                                      (1.25, r + 12, 0.2, -2.0), (1.5, r, 0.4, -1.0),
                                      (2.5, f, 0.4, -1.0), (2.75, r + 12, 0.2, -2.0),
                                      (3.25, r, 0.2, -3.0), (3.5, f, 0.4, -1.0))]


def funk_keys(ch, nxt):
    return [(x, m, 0.15, vol(STYLE_TARGET["f_clav"], samples.CLAV_HIGH, m, acc - 3.0))
            for x, acc in ((0.5, 0.0), (0.75, -3.0), (1.5, -1.0), (2.25, -2.0), (2.5, 0.0), (3.5, -1.0))
            for m in STYLE_CHORD[ch]["clav"]]


def funk_guitar(ch, nxt):
    return [(x, m, 0.2, vol(STYLE_TARGET["f_guitar"], samples.GUITAR, m, -3.0))
            for x in (1.0, 3.0) for m in STYLE_CHORD[ch]["guitar"]]


def dreamy_sub(ch, nxt):
    r = R[ch]
    return [(0.0, r, 3.8, vol(STYLE_TARGET["d_sub"], samples.SINE_SUB, r))]


def dreamy_choir(ch, nxt):
    return [(0.0, m, 4.0, vol(STYLE_TARGET["d_choir"], samples.CHOIR, m)) for m in STYLE_CHORD[ch]["choir"]]


def dreamy_bells(ch, nxt):
    return [(x, m, 1.8, vol(STYLE_TARGET["d_bells"], samples.BELLS, m, 0.0 if x == 0 else -3.0))
            for x, m in zip((0.0, 2.0), STYLE_CHORD[ch]["bells"])]


# ===== clips ===========================================================================================================
def phrase(pattern, inst):
    """make(progression) for patterns.expand: pattern(chord, next chord) -> [(beat in bar, midi, length beats, old
    volume dB)] for every bar of the phrase. The last bar's next chord is the progression's first (the phrase loops)."""
    def make(p):
        ch = P.chords(p)
        return [N(bar * 4 + x, m, length, v, inst)
                for bar in range(BARS) for x, m, length, v in pattern(ch[bar], ch[(bar + 1) % BARS])]
    return make


I1, I2 = 0.5, 1.5   # old Intensity conditions [0.5, 3] / [1.5, 3]
# legacy_shift: how the old generator moved the note start (read only by the removed compare-legacy check; kept as a
# record): "none"; "onset" (sample onset / pitch
# ratio); "onset_not_phrase_start" (same, but not on a phrase's first bar); ("fixed", s); ("fixed_not_phrase_start", s).
CLIPS = [c for group in (
    expand("BassA", "Bass", phrase(bass_a, BASS), styles=(SYNTHPOP,), variation=(0, 0)),
    expand("BassDrive", "Bass", phrase(bass_b, BASS), styles=(SYNTHPOP,), variation=(1, 2)),
    expand("BassWalk", "Bass", phrase(bass_c, BASS), styles=(SYNTHPOP,), variation=(3, 4)),
    expand("LatinBass", "Bass", phrase(latin_bass, BASS_ENV), styles=(LATIN,)),
    expand("FunkBass", "Bass", phrase(funk_bass, RUBBER), styles=(FUNK,)),
    expand("PluckA1", "Pluck", lambda p: pluck_a(1, p), styles=(SYNTHPOP,), variation=(0, 2), intensity=I1,
           section_phrase=1),
    expand("PluckA2", "Pluck", lambda p: pluck_a(2, p), styles=(SYNTHPOP,), variation=(0, 2), intensity=I1,
           section_phrase=2),
    expand("PluckArpeggio", "Pluck", phrase(pluck_b, PLUCK), styles=(SYNTHPOP,), variation=(3, 3), intensity=I1),
    expand("PluckStabs", "Pluck", phrase(pluck_c, PLUCK), styles=(SYNTHPOP,), variation=(4, 4), intensity=I1),
    expand("ComboPad", "Combo pad", phrase(combo_pad, PAD), combo=2),
    expand("ComboBells", "Combo bells", phrase(combo_bells, COMBO_BELLS), combo=3),
    expand("TensionStrings", "Tension strings", phrase(strings, STRINGS), legacy_shift="onset_not_phrase_start"),
    expand("TensionSub", "Tension sub", phrase(tension_sub, TENSION_SUB), legacy_shift="onset"),
    expand("TensionAcid", "Tension acid", phrase(acid, ACID), legacy_shift="onset"),
    expand("LatinMarimba", "Latin marimba", phrase(latin_marimba, MARIMBA), styles=(LATIN,), intensity=I1),
    expand("FunkKeys", "Funk keys", phrase(funk_keys, CLAV), styles=(FUNK,), intensity=I1),
    expand("FunkGuitar", "Funk guitar", phrase(funk_guitar, GUITAR), styles=(FUNK,), intensity=I2,
           legacy_shift=("fixed", 0.025)),
    expand("DreamyChoir", "Dreamy choir", phrase(dreamy_choir, CHOIR), styles=(DREAMY,),
           legacy_shift=("fixed_not_phrase_start", 0.06)),
    expand("DreamyBells", "Dreamy bells", phrase(dreamy_bells, DREAMY_BELLS), styles=(DREAMY,)),
    expand("DreamySub", "Dreamy sub", phrase(dreamy_sub, DREAMY_SUB), styles=(DREAMY,)),
) for c in group]
