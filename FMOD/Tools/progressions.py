"""
Chord progressions (2026-11-27): every phrase (4 bars, one chord per bar) plays one progression, picked per phrase by
Unity's ProgressionSelector (ArrangementState.Progression). Everything stays diatonic in C major (C Dm Em F G Am), so no
new samples are needed and every pitched part can be moved from the original C - Am - F - G onto any progression.

How the pattern modules use this file:
- A part whose notes depend only on the bar's chord (bass roots, pad voicings, arpeggios of the chord) reads its chord
  data by chord name: bass_root() / fifth() / approach(), and derive() fills the Dm / Em entries of a voicing table
  written for the original four chords (by diatonic shift of the closest original voicing).
- A part written as a melody over the original bars (guest loops, fever leads, risers) is moved bar by bar with
  remap_bar(): each bar is shifted diatonically from its original chord to the progression's chord by the smallest
  step, so the melody keeps its contour and every chord tone stays a chord tone (third -> third, fifth -> fifth).
- patterns.expand() builds a part once per progression and merges progressions that give the same notes into one clip
  (PatternClip.ProgressionMask).

Progression 0 is the original C - Am - F - G; its notes are exactly the notes before progressions existed.
Data and pure helpers only.
"""

SCALE = (0, 2, 4, 5, 7, 9, 11)          # C major
# Chord ids are KillChordTable / PatternTable.ChordNames indices: the original four keep their old bar index.
CHORD_NAMES = ["C", "Am", "F", "G", "Dm", "Em"]
DEGREE = {"C": 0, "Dm": 1, "Em": 2, "F": 3, "G": 4, "Am": 5}
ORIGINAL = ("C", "Am", "F", "G")

# Bass register per chord: the original roots (C2 A1 F1 G1) plus D2 and E1, so a bassist stays in one position.
BASS_ROOT = {"C": 36, "Dm": 38, "Em": 28, "F": 29, "G": 31, "Am": 33}

# (name, chords, mood). Mood steers the pick (ProgressionSelector): 0 bright, 1 dark (minor colour), 2 soft (dreamy).
PROGRESSIONS = [
    ("Classic", ("C", "Am", "F", "G"), 0),      # I vi IV V - the original
    ("Axis", ("C", "G", "Am", "F"), 0),         # I V vi IV
    ("Minor Axis", ("Am", "F", "C", "G"), 1),   # vi IV I V
    ("Royal Road", ("F", "G", "Em", "Am"), 2),  # IV V iii vi
    ("Turnaround", ("Dm", "G", "C", "Am"), 0),  # ii V I vi
    ("Plagal Lift", ("C", "F", "Am", "G"), 0),  # I IV vi V
    ("Melancholy", ("Am", "Em", "F", "G"), 1),  # vi iii IV V
    ("Doo-wop", ("C", "Am", "Dm", "G"), 0),     # I vi ii V
    ("Sunrise", ("F", "C", "G", "Am"), 2),      # IV I V vi
    ("Stepping Up", ("C", "Em", "F", "G"), 2),  # I iii IV V
    ("Shadow", ("Am", "Dm", "G", "C"), 1),      # vi ii V I
    ("Pop Lament", ("Am", "F", "G", "Em"), 1),  # vi IV V iii
]
PROGRESSION_COUNT = len(PROGRESSIONS)
ALL_PROGRESSIONS = tuple(range(PROGRESSION_COUNT))
assert PROGRESSION_COUNT <= 16, "PatternClip.ProgressionMask is a ushort"
assert PROGRESSIONS[0][1] == ORIGINAL
assert all(len(p[1]) == 4 and all(c in DEGREE for c in p[1]) for p in PROGRESSIONS)
assert len({p[1] for p in PROGRESSIONS}) == PROGRESSION_COUNT, "duplicate progression"


def chords(p):
    return PROGRESSIONS[p][1]


def shift(midi, steps):
    """Moves a note `steps` scale steps in C major (a note outside the scale moves with the step below it)."""
    octave, pc = divmod(midi, 12)
    sharp = 0
    if pc not in SCALE:
        pc, sharp = pc - 1, 1
    n = SCALE.index(pc) + steps
    o, i = divmod(n, 7)
    return (octave + o) * 12 + SCALE[i] + sharp


def _shift_any(x, steps):
    if isinstance(x, (tuple, list)):
        return type(x)(_shift_any(v, steps) for v in x)
    return shift(x, steps)


def steps_between(src, dst):
    """Scale-step shifts that move chord src onto chord dst, smallest root movement (semitones) first."""
    d = DEGREE[dst] - DEGREE[src]
    cands = sorted({d, d - 7, d + 7}, key=lambda k: (abs(shift(SCALE[DEGREE[src]] + 48, k) - (SCALE[DEGREE[src]] + 48)), k < 0))
    return [k for k in cands if abs(k) <= 6]


def remap_bar(midis, src, dst, ok=None):
    """Moves one bar's pitched notes from chord src to chord dst (same shift for the whole bar, so the line keeps its
    shape). ok(list of midis) -> bool rejects a shift (e.g. out of the instrument's range); the next smallest is tried."""
    if src == dst:
        return list(midis)
    for k in steps_between(src, dst):
        out = [shift(m, k) for m in midis]
        if ok is None or ok(out):
            return out
    raise ValueError(f"no shift moves {midis} from {src} to {dst} within range")


def remap_rows(rows, prog, pitched, ok=None, bar_of=lambda beat: int(beat // 4)):
    """rows = tuples whose first item is the beat in the phrase; pitched(row) -> index of the MIDI item (None = leave
    the row as it is). Every bar is moved from the original chord to progression prog's chord."""
    target = chords(prog)
    out = list(rows)
    for bar in range(4):
        idx = [i for i, r in enumerate(rows) if bar_of(r[0]) == bar and pitched(r) is not None]
        if not idx:
            continue
        midis = [rows[i][pitched(rows[i])] for i in idx]
        moved = remap_bar(midis, ORIGINAL[bar], target[bar], ok)
        for i, m in zip(idx, moved):
            r = list(out[i])
            r[pitched(rows[i])] = m
            out[i] = tuple(r)
    return out


def derive(table, ok=None):
    """table: {C, Am, F, G: voicing (int / tuple / dict of those)} -> the same with Dm and Em added. Each new entry is
    the original voicing whose diatonic shift lands closest to the table's average register; ok(entry) -> bool rejects
    a candidate (e.g. a note outside the instrument's range)."""
    def notes(v):
        if isinstance(v, dict):
            return [n for x in v.values() for n in notes(x)]
        if isinstance(v, (tuple, list)):
            return [n for x in v for n in notes(x)]
        return [v]

    def move(v, k):
        if isinstance(v, dict):
            return {key: move(x, k) for key, x in v.items()}
        return _shift_any(v, k)

    every = notes([table[c] for c in ORIGINAL])
    mean = sum(every) / len(every)
    out = dict(table)
    for dst in CHORD_NAMES:
        if dst in out:
            continue
        best = None
        for src in ORIGINAL:
            d = DEGREE[dst] - DEGREE[src]
            for k in (d, d - 7, d + 7):
                if abs(k) > 4:
                    continue
                v = move(table[src], k)
                if ok is not None and not ok(v):
                    continue
                ns = notes(v)
                score = (abs(sum(ns) / len(ns) - mean), abs(k))
                if best is None or score < best[0]:
                    best = (score, v)
        assert best is not None, f"no {dst} voicing fits"
        out[dst] = best[1]
    return out


def fifth(chord):
    return BASS_ROOT[chord] + 7


def approach(chord, next_chord):
    """Walking note into the next bar: the scale neighbour of the next root nearest this root (never this root)."""
    r, n = BASS_ROOT[chord], BASS_ROOT[next_chord]
    cands = [shift(n, 1), shift(n, -1)]
    cands = [c for c in cands if c != r] or cands
    return min(cands, key=lambda c: (abs(c - r), -c))


def chord_tones(chord):
    """Pitch classes of the chord's triad."""
    root = SCALE[DEGREE[chord]]
    return {root % 12, shift(root + 48, 2) % 12, shift(root + 48, 4) % 12}


# --- self-check: the original bars reproduce the old hand-written data ---
assert [approach(a, b) for a, b in zip(ORIGINAL, ORIGINAL[1:] + ORIGINAL[:1])] == [35, 31, 33, 35]
assert [fifth(c) for c in ORIGINAL] == [43, 40, 36, 38]
assert remap_bar([60, 64, 67], "C", "Dm") == [62, 65, 69]
assert remap_bar([57, 60, 65], "F", "C") == [52, 55, 60]
