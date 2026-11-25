"""
Pattern data: the drums, one 4-bar phrase (16 beats) each, for the PatternScheduler (Task 12).

Layers (the deleted timeline track each replaced in brackets; the --compare-legacy check went with those tracks):
    Core kick, Core snare, Core hat   the hand-made core kit (values copied from the event XML)
                                      [Kick - beats 1 and 3 / Snare - beats 2 and 4 / Eighth-note hats, closed hats]
    Open hat                          off-beat open hats, Intensity 1.5+               [Eighth-note hats, open hats]
    Clap layer                        clap on 2 and 4, Intensity 2.5+ (hand-made)      [Clap layer]
    Snare variations                  ghosts (Variation 2-4) and the bar-4 fill (2)    [Snare variations]  bed_variation.py
    Combo hats                        16th hats on the e / a, Combo 1+                 [Combo hats]        bed_combo.py
    Latin perc, Latin shaker, Funk drums, Dreamy perc                                                      bed_style.py
        (Latin shaker also holds the latin / funk 16th shaker of Combo 1+)

The three core layers are PatternTable.CoreLayerMask: SoundManager schedules them with its second PatternScheduler, a
bar ahead (CoreLookaheadBeats), since they only depend on decisions made a phrase ahead (style, fever). Core kick plays
in every style and fever phase but the build-up bar (the old FeverBuild condition); Core snare / Core hat play in the
styles of patterns.STYLE_USES_CORE_BACKBEAT (synthpop; fever is synthpop).

Every drum note has length 0: it plays to the end of its sample (the timeline cut it at its region end). One instrument,
Drums, holds the kit; its Note is the percussion number (samples.PERC, pitch 0). Notes keep the dB the timeline note had,
plus their old track's fader (TRACK_DB), since the one-shot path has no track fader. Latin perc had a 90 Hz high-pass on
its track: its notes play through their own sub-bus (mixer.py SUB_BUSES). Data only.
"""
import samples
from patterns import STYLE_COUNT, STYLE_USES_CORE_BACKBEAT, Clip, Inst, Layer, N

LAYERS_BUS = "Music Bed/Layers"
LATIN_PERC_BUS = "Music Bed/Layers/Latin Perc"

# max_pitch 0: every sound of the kit plays at its own pitch.
DRUMS = Inst("Drums", samples.PERC, LAYERS_BUS, 0, 0, max_pitch=0)
LATIN_DRUMS = Inst("Drums", samples.PERC, LATIN_PERC_BUS, 0, 0, max_pitch=0)

SYNTHPOP, LATIN, FUNK, DREAMY = 0, 1, 2, 3
assert STYLE_COUNT == 4
CORE_BACKBEAT_STYLES = tuple(s for s in range(STYLE_COUNT) if STYLE_USES_CORE_BACKBEAT[s])
NOT_IN_BUILD = ("None", "Impact", "Fever", "Outro")

KICK_TRACK, SNARE_TRACK, HATS_TRACK = "Kick - beats 1 and 3", "Snare - beats 2 and 4", "Eighth-note hats"
# Fader of each old track (event XML); the other drum tracks are at 0 dB.
TRACK_DB = {KICK_TRACK: -12.0, SNARE_TRACK: -4.0, HATS_TRACK: -2.0, "Clap layer": -6.0, "Snare variations": -4.0}

LAYERS = [
    Layer("Core kick", KICK_TRACK, core=True), Layer("Core snare", SNARE_TRACK, core=True),
    Layer("Core hat", HATS_TRACK, core=True), Layer("Open hat", HATS_TRACK),
    Layer("Clap layer", "Clap layer"), Layer("Snare variations", "Snare variations"), Layer("Combo hats", "Combo hats"),
    Layer("Latin perc", "Latin perc"), Layer("Latin shaker", "Latin shaker"), Layer("Funk drums", "Funk drums"),
    Layer("Dreamy perc", "Dreamy perc"),
]
TRACK_OF = {l.name: l.legacy_track for l in LAYERS}

BARS = 4


def rms(number):
    return next(s[2] for s in samples.PERC if s[1] == number)


def hit(layer, beat, number, volume, inst=DRUMS):
    """A drum note: phrase beat, percussion number, old note dB (+ its track's fader), to the sample end."""
    return N(beat, number, 0, volume + TRACK_DB.get(TRACK_OF[layer], 0.0), inst)


def bars(layer, pattern, inst=DRUMS):
    """pattern(bar) -> [(beat in bar, number, old note dB)] for each bar of the phrase."""
    return [hit(layer, bar * 4 + x, m, v, inst) for bar in range(BARS) for x, m, v in pattern(bar)]


def target(t, number, accent=0.0):
    """bed_style.vol: per-hit loudness target - sample RMS + accent (patterns.py clamps and warns)."""
    return t - rms(number) + accent


# ===== core kit and the synthpop extras (hand-made, from the event XML) ================================================
def core_kick(bar):
    return [(x, samples.KICK, -3.0) for x in (0.0, 2.0)]


def core_snare(bar):
    return [(x, samples.SNARE, -16.0) for x in (1.0, 3.0)]


def core_hat(bar):
    return [(float(x), samples.CLOSED_HAT, -15.0) for x in range(4)]


def open_hat(bar):
    return [(x + 0.5, samples.OPEN_HAT, -16.0) for x in range(4)]


def clap(bar):
    return [(x, samples.CLAP, -14.0) for x in (1.0, 3.0)]


# ===== snare variations (bed_variation.py) =============================================================================
def ghosts(bar):
    return [(x, samples.SNARE, v) for x, v in [(0.75, -30.0), (2.5, -28.0)] + ([(3.75, -31.0)] if bar != 3 else [])]


def fill(bar):
    if bar != 3:
        return []
    steps = [2.0, 2.25, 2.5, 2.75, 3.0, 3.25, 3.5]
    return [(s, samples.SNARE, -26.0 + 10.0 * i / (len(steps) - 1)) for i, s in enumerate(steps)]


# ===== combo hats (bed_combo.py) =======================================================================================
def combo_hats(bar):
    return [(i + sub, samples.CLOSED_HAT, -16.0 + (-2.0 if sub == 0.75 else 0.0)) for i in range(4) for sub in (0.25, 0.75)]


# ===== styles (bed_style.py TARGET) ====================================================================================
T = {"conga_slap": -35.0, "shaker": -44.0, "cowbell": -38.0, "bongo": -37.0, "conga": -38.0, "claves": -42.0,
     "shaker16": -47.0, "f_snare": -34.0, "f_ghost": -48.0, "f_hat": -46.0, "f_open": -38.0, "f_clap": -37.0,
     "d_claves": -45.0, "d_shaker": -51.0}
CLAVE = [(0, 0.0), (0, 1.5), (0, 3.0), (1, 1.0), (1, 2.0)]   # son clave 3-2 over two bars: (bar % 2, beat)


def latin_conga(bar):
    return ([(x, samples.CONGA_SLAP, target(T["conga_slap"], samples.CONGA_SLAP)) for x in (1.0, 3.0)] +
            [(2.5, samples.CONGA, target(T["conga"], samples.CONGA, -3.0)), (3.5, samples.CONGA, target(T["conga"], samples.CONGA))])


def latin_cowbell(bar):
    return [(float(i), samples.COWBELL, target(T["cowbell"], samples.COWBELL, 0.0 if i == 0 else -3.0)) for i in range(4)]


def latin_bongo(bar):
    return [(i + 0.5, samples.BONGO_HI if i % 2 else samples.BONGO_LO,
             target(T["bongo"], samples.BONGO_HI if i % 2 else samples.BONGO_LO, 0.0 if i == 3 else -2.0)) for i in range(4)]


def latin_claves(bar):
    return [(x, samples.CLAVES, target(T["claves"], samples.CLAVES)) for b, x in CLAVE if bar % 2 == b]


def eighth_shaker(t, accent_on_beat):
    def pattern(bar):
        return [(i * 0.5, samples.SHAKER, target(t, samples.SHAKER, 0.0 if i % 2 else accent_on_beat)) for i in range(8)]
    return pattern


def shaker16(bar):
    return [(i + sub, samples.SHAKER, target(T["shaker16"], samples.SHAKER, -2.0 if sub == 0.25 else 0.0))
            for i in range(4) for sub in (0.25, 0.75)]


def funk_snare(bar):
    return [(x, samples.SNARE, target(T["f_snare"], samples.SNARE)) for x in (1.0, 3.0)]


def funk_clap(bar):
    return [(x, samples.CLAP, target(T["f_clap"], samples.CLAP)) for x in (1.0, 3.0)]


def funk_ghosts(bar):
    return [(x, samples.SNARE, target(T["f_ghost"], samples.SNARE)) for x in (0.75, 1.75, 2.25) + (() if bar == 3 else (3.75,))]


def funk_hats(bar):
    return [(i * 0.25, samples.CLOSED_HAT, target(T["f_hat"], samples.CLOSED_HAT, (0.0, -6.0, -3.0, -6.0)[i % 4]))
            for i in range(16) if not (bar == 3 and i == 15)]


def funk_open_hat(bar):
    return [(x, samples.OPEN_HAT, target(T["f_open"], samples.OPEN_HAT)) for x in (1.5, 3.5)]


def dreamy_claves(bar):
    return [(x, samples.CLAVES, target(T["d_claves"], samples.CLAVES)) for x in (1.0, 3.0)]


I1, I2, I3 = 0.5, 1.5, 2.5   # old Intensity conditions [0.5, 3] / [1.5, 3] / [2.5, 3]
CLIPS = [
    Clip("CoreKick", "Core kick", bars("Core kick", core_kick), fever=NOT_IN_BUILD),
    Clip("CoreSnare", "Core snare", bars("Core snare", core_snare), styles=CORE_BACKBEAT_STYLES),
    Clip("CoreHat", "Core hat", bars("Core hat", core_hat), styles=CORE_BACKBEAT_STYLES),
    Clip("OpenHat", "Open hat", bars("Open hat", open_hat), styles=(SYNTHPOP,), intensity=I2),
    Clip("ClapLayer", "Clap layer", bars("Clap layer", clap), styles=(SYNTHPOP,), intensity=I3),
    Clip("SnareGhosts", "Snare variations", bars("Snare variations", ghosts), styles=(SYNTHPOP,), variation=(2, 4)),
    Clip("SnareFill", "Snare variations", bars("Snare variations", fill), styles=(SYNTHPOP,), variation=(2, 2)),
    Clip("ComboHats", "Combo hats", bars("Combo hats", combo_hats), styles=(SYNTHPOP,), combo=1),
    Clip("LatinConga", "Latin perc", bars("Latin perc", latin_conga, LATIN_DRUMS), styles=(LATIN,)),
    Clip("LatinCowbell", "Latin perc", bars("Latin perc", latin_cowbell, LATIN_DRUMS), styles=(LATIN,), intensity=I2),
    Clip("LatinBongo", "Latin perc", bars("Latin perc", latin_bongo, LATIN_DRUMS), styles=(LATIN,), intensity=I3),
    Clip("LatinClaves", "Latin perc", bars("Latin perc", latin_claves, LATIN_DRUMS), styles=(LATIN,), intensity=I1),
    Clip("LatinShaker", "Latin shaker", bars("Latin shaker", eighth_shaker(T["shaker"], -2.0)), styles=(LATIN,)),
    Clip("LatinShaker16", "Latin shaker", bars("Latin shaker", shaker16), styles=(LATIN,), combo=1),
    Clip("FunkShaker16", "Latin shaker", bars("Latin shaker", shaker16), styles=(FUNK,), combo=1),
    Clip("FunkSnare", "Funk drums", bars("Funk drums", funk_snare), styles=(FUNK,)),
    Clip("FunkClap", "Funk drums", bars("Funk drums", funk_clap), styles=(FUNK,), intensity=I3),
    Clip("FunkGhosts", "Funk drums", bars("Funk drums", funk_ghosts), styles=(FUNK,)),
    Clip("FunkHats", "Funk drums", bars("Funk drums", funk_hats), styles=(FUNK,)),
    Clip("FunkOpenHat", "Funk drums", bars("Funk drums", funk_open_hat), styles=(FUNK,), intensity=I2),
    Clip("DreamyClaves", "Dreamy perc", bars("Dreamy perc", dreamy_claves), styles=(DREAMY,)),
    Clip("DreamyShaker", "Dreamy perc", bars("Dreamy perc", eighth_shaker(T["d_shaker"], -3.0)), styles=(DREAMY,)),
]
