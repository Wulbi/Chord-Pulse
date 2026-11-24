"""
Pattern data: the background bed's pitched layers, one 4-bar phrase (16 beats) each, for the PatternScheduler.

Layers (legacy timeline track in brackets; the old generators that put these notes on the timeline still run until the
tracks are gated / deleted, and `python -I FMOD/Tools/patterns.py --compare-legacy` checks the two agree):
    Bass             A original half notes / B drive / C walk (synthpop), Latin tumbao, Funk rubber bass  [Bass - C Am F G]
    Pluck            A original off-beat 8ths (two phrases), B arpeggio, C tresillo stabs               [Offbeat pluck]
    Combo pad, Combo bells                                                                              [Combo pad / bells]
    Tension strings, Tension sub, Tension acid  (always scheduled; the sub-bus Tension curves decide what is heard)
    Latin marimba, Funk keys, Funk guitar, Dreamy choir, Dreamy bells, Dreamy sub

Values moved from bed_variation.py, bed_combo.py, bed_tension.py and bed_style.py. Bass A and Pluck A were never
generated: they are the hand-made original notes of those two tracks, copied here from the event XML (compare-legacy
checks every copy in the event still matches). Pluck A is an 8-bar pattern, so it is two clips (SectionPhrase 1 / 2).

Each note's volume is the dB the old timeline note had (target - sample RMS + accent, clamped to [-60, 10] like the old
generators, or the direct note volume); patterns.py splits it into the instrument event's NORMALIZE_DB - RMS plus GainDb.
Data only (plus the small helpers that turn the old generator formulas into notes); no numpy.
"""
import samples
from patterns import STYLE_COUNT, Clip, Inst, Layer, N

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


# ===== bass / pluck (bed_variation.py) ==================================================================================
VAR_BARS = [   # bass root, fifth, approach into the next bar, pluck chord
    dict(root=36, fifth=43, approach=35, chord=(60, 64, 67)),
    dict(root=33, fifth=40, approach=31, chord=(57, 60, 64)),
    dict(root=29, fifth=36, approach=33, chord=(57, 60, 65)),
    dict(root=31, fifth=38, approach=35, chord=(55, 59, 62)),
]


def bass_a(bar):
    """Original (hand-made) bass: root half notes, 0.85 beat, -8 dB, no ADSR."""
    r = VAR_BARS[bar]["root"]
    return [(0.0, r, 0.85, -8.0), (2.0, r, 0.85, -8.0)]


def bass_b(bar):
    """8th-note drive: root with an octave pop on the 'and' of 2 and 4."""
    b = VAR_BARS[bar]
    return [(i * 0.5, b["root"] + (12 if i in (3, 7) else 0), 0.4, -10.0 if i % 2 == 0 else -13.0) for i in range(8)]


def bass_c(bar):
    """Syncopated: long root, push on 1.5, fifth on 2.5, walk into the next chord on 3.5."""
    b = VAR_BARS[bar]
    return [(0.0, b["root"], 1.3, -8.0), (1.5, b["root"], 0.45, -10.0), (2.5, b["fifth"], 0.45, -9.0),
            (3.5, b["approach"], 0.45, -10.0)]


# Original (hand-made) off-beat pluck, MIDI per off-beat of each bar; it runs over 8 bars, so a section's two phrases differ.
PLUCK_A = {
    1: [(64, 60, 67, 64), (57, 64, 60, 57), (60, 57, 53, 60), (59, 55, 62, 59)],   # bars 1-4 of a section
    2: [(60, 67, 64, 60), (64, 60, 57, 64), (57, 53, 60, 57), (55, 62, 59, 55)],   # bars 5-8
}


def pluck_a(phrase):
    def bar_notes(bar):
        return [(i + 0.5, m, 0.44, -18.0) for i, m in enumerate(PLUCK_A[phrase][bar])]
    return bar_notes


def pluck_b(bar):
    c = VAR_BARS[bar]["chord"]
    seq = [c[0], c[1], c[2], c[0] + 12, c[2], c[1], c[0], c[1]]
    return [(i * 0.5, m, 0.4, -21.0 if i % 2 else -20.0) for i, m in enumerate(seq)]


def pluck_c(bar):
    c = VAR_BARS[bar]["chord"]
    return [(b, m, 0.45, -23.0) for b in (0.0, 1.5, 3.0) for m in c]


# ===== combo (bed_combo.py) ============================================================================================
COMBO_BARS = [
    dict(pad=(67, 72, 76), bells=(76, 79, 84, 79)),
    dict(pad=(69, 72, 76), bells=(76, 81, 84, 81)),
    dict(pad=(69, 72, 77), bells=(77, 81, 84, 81)),
    dict(pad=(67, 71, 74), bells=(74, 79, 83, 79)),
]
COMBO_LEVEL = {"pad": -27.0, "bells": -24.0}


def combo_pad(bar):
    return [(0.0, m, 4.0, COMBO_LEVEL["pad"]) for m in COMBO_BARS[bar]["pad"]]


def combo_bells(bar):
    return [(i + 0.5, m, 0.9, COMBO_LEVEL["bells"] - (0 if i == 0 else 2.0)) for i, m in enumerate(COMBO_BARS[bar]["bells"])]


# ===== tension (bed_tension.py) ========================================================================================
TENSION_CHORDS = [((52, 55, 60), 36), ((52, 57, 60), 33), ((53, 57, 60), 29), ((50, 55, 59), 31)]
TENSION_TARGET = {"strings": -31.0, "sub": -27.0, "acid": -33.0}
ACID_PATTERN = [0, 0, 12, 0, 0, 12, 0, 12]
SUB_BEATS = [0.5, 1.5, 2.5, 3.5]


def strings(bar):
    return [(0.0, m, 4.0, vol(TENSION_TARGET["strings"], samples.STRINGS, m)) for m in TENSION_CHORDS[bar][0]]


def tension_sub(bar):
    r = TENSION_CHORDS[bar][1]
    return [(b, r, 0.4, vol(TENSION_TARGET["sub"], samples.SINE_SUB, r)) for b in SUB_BEATS]


def acid(bar):
    r = TENSION_CHORDS[bar][1]
    return [(i * 0.5, r + o, 0.4, vol(TENSION_TARGET["acid"], samples.ACID, r + o, 0.0 if i % 2 == 0 else -3.0))
            for i, o in enumerate(ACID_PATTERN)]


# ===== styles (bed_style.py) ===========================================================================================
STYLE_BARS = [
    dict(root=36, next=33, fifth=43, marimba=(67, 72, 76, 79), clav=(76, 79), guitar=(64, 67), choir=(60, 64, 67), bells=(76, 79)),
    dict(root=33, next=29, fifth=40, marimba=(69, 72, 76, 81), clav=(72, 76), guitar=(64, 69), choir=(57, 60, 64), bells=(76, 81)),
    dict(root=29, next=31, fifth=36, marimba=(69, 72, 77, 81), clav=(77, 81), guitar=(65, 69), choir=(57, 60, 65), bells=(77, 81)),
    dict(root=31, next=36, fifth=38, marimba=(67, 71, 74, 79), clav=(74, 79), guitar=(62, 67), choir=(55, 59, 62), bells=(74, 79)),
]
STYLE_TARGET = {"marimba": -36.0, "f_bass": -25.5, "f_clav": -35.0, "f_guitar": -37.0,
                "d_sub": -32.0, "d_choir": -39.0, "d_bells": -44.0}


def latin_bass(bar):
    b = STYLE_BARS[bar]
    return [(0.0, b["root"], 0.45, -11.0), (1.5, b["root"], 1.3, -9.0), (3.0, b["next"], 0.9, -9.0)]


def latin_marimba(bar):
    low, mid, high, top = STYLE_BARS[bar]["marimba"]
    return [(x, m, 0.6, vol(STYLE_TARGET["marimba"], samples.MARIMBA, m, acc))
            for x, m, acc in ((0.0, low, 0.0), (0.5, top, -2.0), (1.0, high, -1.0), (1.5, top, -2.0),
                              (2.5, mid, -1.0), (3.0, high, -1.0), (3.5, top, -2.0))]


def funk_bass(bar):
    b = STYLE_BARS[bar]
    return [(x, m, length, vol(STYLE_TARGET["f_bass"], samples.RUBBER, m, acc))
            for x, m, length, acc in ((0.0, b["root"], 0.4, 0.0), (0.75, b["root"], 0.2, -3.0),
                                      (1.25, b["root"] + 12, 0.2, -2.0), (1.5, b["root"], 0.4, -1.0),
                                      (2.5, b["fifth"], 0.4, -1.0), (2.75, b["root"] + 12, 0.2, -2.0),
                                      (3.25, b["root"], 0.2, -3.0), (3.5, b["fifth"], 0.4, -1.0))]


def funk_keys(bar):
    return [(x, m, 0.15, vol(STYLE_TARGET["f_clav"], samples.CLAV_HIGH, m, acc - 3.0))
            for x, acc in ((0.5, 0.0), (0.75, -3.0), (1.5, -1.0), (2.25, -2.0), (2.5, 0.0), (3.5, -1.0))
            for m in STYLE_BARS[bar]["clav"]]


def funk_guitar(bar):
    return [(x, m, 0.2, vol(STYLE_TARGET["f_guitar"], samples.GUITAR, m, -3.0))
            for x in (1.0, 3.0) for m in STYLE_BARS[bar]["guitar"]]


def dreamy_sub(bar):
    r = STYLE_BARS[bar]["root"]
    return [(0.0, r, 3.8, vol(STYLE_TARGET["d_sub"], samples.SINE_SUB, r))]


def dreamy_choir(bar):
    return [(0.0, m, 4.0, vol(STYLE_TARGET["d_choir"], samples.CHOIR, m)) for m in STYLE_BARS[bar]["choir"]]


def dreamy_bells(bar):
    return [(x, m, 1.8, vol(STYLE_TARGET["d_bells"], samples.BELLS, m, 0.0 if x == 0 else -3.0))
            for x, m in zip((0.0, 2.0), STYLE_BARS[bar]["bells"])]


# ===== clips ===========================================================================================================
def phrase(pattern, inst):
    """Notes of every bar of the phrase: pattern(bar) -> [(beat in bar, midi, length beats, old volume dB)]."""
    return [N(bar * 4 + x, m, length, v, inst) for bar in range(BARS) for x, m, length, v in pattern(bar)]


I1, I2 = 0.5, 1.5   # old Intensity conditions [0.5, 3] / [1.5, 3]
# legacy_shift: how the old generator moved the note start (compare-legacy only): "none"; "onset" (sample onset / pitch
# ratio); "onset_not_phrase_start" (same, but not on a phrase's first bar); ("fixed", s); ("fixed_not_phrase_start", s).
CLIPS = [
    Clip("BassA", "Bass", phrase(bass_a, BASS), styles=(SYNTHPOP,), variation=(0, 0)),
    Clip("BassDrive", "Bass", phrase(bass_b, BASS), styles=(SYNTHPOP,), variation=(1, 2)),
    Clip("BassWalk", "Bass", phrase(bass_c, BASS), styles=(SYNTHPOP,), variation=(3, 4)),
    Clip("LatinBass", "Bass", phrase(latin_bass, BASS_ENV), styles=(LATIN,)),
    Clip("FunkBass", "Bass", phrase(funk_bass, RUBBER), styles=(FUNK,)),
    Clip("PluckA1", "Pluck", phrase(pluck_a(1), PLUCK), styles=(SYNTHPOP,), variation=(0, 2), intensity=I1, section_phrase=1),
    Clip("PluckA2", "Pluck", phrase(pluck_a(2), PLUCK), styles=(SYNTHPOP,), variation=(0, 2), intensity=I1, section_phrase=2),
    Clip("PluckArpeggio", "Pluck", phrase(pluck_b, PLUCK), styles=(SYNTHPOP,), variation=(3, 3), intensity=I1),
    Clip("PluckStabs", "Pluck", phrase(pluck_c, PLUCK), styles=(SYNTHPOP,), variation=(4, 4), intensity=I1),
    Clip("ComboPad", "Combo pad", phrase(combo_pad, PAD), combo=2),
    Clip("ComboBells", "Combo bells", phrase(combo_bells, COMBO_BELLS), combo=3),
    Clip("TensionStrings", "Tension strings", phrase(strings, STRINGS), legacy_shift="onset_not_phrase_start"),
    Clip("TensionSub", "Tension sub", phrase(tension_sub, TENSION_SUB), legacy_shift="onset"),
    Clip("TensionAcid", "Tension acid", phrase(acid, ACID), legacy_shift="onset"),
    Clip("LatinMarimba", "Latin marimba", phrase(latin_marimba, MARIMBA), styles=(LATIN,), intensity=I1),
    Clip("FunkKeys", "Funk keys", phrase(funk_keys, CLAV), styles=(FUNK,), intensity=I1),
    Clip("FunkGuitar", "Funk guitar", phrase(funk_guitar, GUITAR), styles=(FUNK,), intensity=I2,
         legacy_shift=("fixed", 0.025)),
    Clip("DreamyChoir", "Dreamy choir", phrase(dreamy_choir, CHOIR), styles=(DREAMY,),
         legacy_shift=("fixed_not_phrase_start", 0.06)),
    Clip("DreamyBells", "Dreamy bells", phrase(dreamy_bells, DREAMY_BELLS), styles=(DREAMY,)),
    Clip("DreamySub", "Dreamy sub", phrase(dreamy_sub, DREAMY_SUB), styles=(DREAMY,)),
]
