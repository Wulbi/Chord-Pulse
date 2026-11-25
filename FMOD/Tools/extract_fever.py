"""
One-time tool (Task 14): reads the eight hand-made fever tracks of the music event (event XML, read only) and prints the
data block of pattern_fever.py. After that extraction pattern_fever.py is the source; this script is kept as the record
of where its numbers came from.

    python -I FMOD/Tools/extract_fever.py

The timeline holds a copy of every fever track in each of the 4 tempo sections x 8 bars (FMOD's FeverBuild / FeverImpact /
Fever / FeverOutro parameters pick the copy that sounds). The script compares those copies in beats (start, length,
sample, pitch, volume, conditions, ADSR):
- a track whose 32 bars are all equal is a 1-bar pattern (printed: beats 0..4 of the first section's bar 1);
- otherwise its 8 phrases (4 sections x bars 1-4 / 5-8) must all be equal: a 4-bar pattern (printed: the first phrase).
If neither holds it prints the differences and stops (exit 1): a hand-edited bar must go to the user, not be picked here.
"""
import os
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import patterns  # noqa: E402

# Track -> the one sample it may use (root MIDI for the pitched ones; None = print the pitch in semitones).
TRACKS = [
    ("Fever lead", "AT101V_ClassicLead_C4_v100.wav", 60),
    ("Fever shaker", "Pallor_Shaker_Synthpop_v110.wav", None),
    ("Fever bass fifths", "AT101V_101Bass_C2_v100.wav", 36),
    ("Fever build roll", "Pallor_Snare_Synthpop_v115.wav", None),
    ("Fever build riser", "AT101V_PolyPluck_C4_v100.wav", 60),
    ("Fever impact", None, None),
    ("Fever outro fall", "AT101V_PolyPluck_C4_v100.wav", 60),
    ("Fever outro snare", "Pallor_Snare_Synthpop_v115.wav", None),
]
INVENTORY = {"Fever lead": 160, "Fever shaker": 256, "Fever bass fifths": 64, "Fever build roll": 384,
             "Fever build riser": 256, "Fever impact": 96, "Fever outro fall": 256, "Fever outro snare": 256}
SECTIONS, BARS = 4, 8
BEAT_DIGITS = 4


def bars_of(track_notes):
    """(section, bar 0..7) -> sorted note keys in beats relative to the bar start."""
    secs = patterns._sections()
    out = defaultdict(list)
    for n in track_notes:
        si = max(i for i, (s0, _) in enumerate(secs) if n["start"] >= s0 - 1e-6)
        s0, bpm = secs[si]
        spb = 60.0 / bpm
        beat = (n["start"] - s0) / spb
        bar = int(round(beat, BEAT_DIGITS) // 4)
        assert 0 <= bar < BARS, (n["track"], n["start"])
        key = (round(beat - 4 * bar, BEAT_DIGITS), n["file"], n["pitch"], n["volume"],
               round(n["length"] / spb, BEAT_DIGITS), tuple(sorted(n["conds"].items())), n["env"])
        out[(si, bar)].append(key)
    for k in out:
        out[k].sort()
    return out


def describe(a, b):
    sa, sb = set(a), set(b)
    return [f"      only in the first:  {x}" for x in sorted(sa - sb)] + [f"      only in the second: {x}" for x in sorted(sb - sa)]


def check(track, bars):
    """Returns (period in bars, copies) or prints the differences and returns None."""
    ref = bars[(0, 0)]
    if all(bars[(s, b)] == ref for s in range(SECTIONS) for b in range(BARS)):
        return 1, SECTIONS * BARS
    first = [bars[(0, b)] for b in range(4)]
    bad = []
    for s in range(SECTIONS):
        for half in (0, 1):
            for b in range(4):
                if bars[(s, half * 4 + b)] != first[b]:
                    bad.append(f"  {track}: section {s} bar {half * 4 + b + 1} differs from section 0 bar {b + 1}")
                    bad += describe(first[b], bars[(s, half * 4 + b)])
    if bad:
        print("\n".join(bad))
        return None
    return 4, SECTIONS * 2


def main():
    names = [t for t, _, _ in TRACKS]
    legacy = patterns.legacy_notes(set(names))
    failed = False
    blocks = []
    print("Copies (4 sections x 8 bars) and note counts:")
    for track, file, root in TRACKS:
        notes = [n for n in legacy if n["track"] == track]
        bars = bars_of(notes)
        result = check(track, bars)
        if result is None:
            failed = True
            continue
        period, copies = result
        data = [k for b in range(period) for k in ((x[0] + 4 * b,) + x[1:] for x in bars[(0, b)])]
        per = len(data)
        ok = per * copies == len(notes) == INVENTORY[track]
        print(f"  {track:18s} {len(notes):4d} notes = {per:3d} per {period}-bar pattern x {copies:2d} copies "
              f"(inventory {INVENTORY[track]}): {'OK' if ok else 'MISMATCH'}")
        failed |= not ok
        conds = {x[5] for x in data}
        envs = {x[6] for x in data}
        files = {x[1] for x in data}
        assert len(conds) == 1 and len(envs) == 1, (track, conds, envs)
        assert file is None or files == {file}, (track, files)
        track_db = next(n["track_db"] for n in notes)
        blocks.append((track, file, period, root, data, conds.pop(), envs.pop(), track_db))
    if failed:
        print("\nASSERTION FAILED: the copies differ (or the counts do not match). Nothing printed; report to the user.")
        return 1
    print("\nAll copies equal.\n")
    print("# ---- data block (extract_fever.py) ----")
    for track, file, period, root, data, cond, env, track_db in blocks:
        unit = "bar" if period == 1 else "phrase"
        print(f"# {track}: {period}-bar pattern; condition {dict(cond)}; ADSR {env}; track fader {track_db} dB")
        var = track.upper().replace("FEVER ", "").replace(" ", "_")
        if root is not None:
            print(f"{var} = [   # (beat in the {unit}, MIDI, length beats, dB)")
            for beat, f, pitch, vol, length, _, _ in data:
                assert abs(pitch - round(pitch)) < 1e-9, (track, pitch)
                print(f"    ({beat!r}, {root + int(round(pitch))}, {length!r}, {vol!r}),")
        elif file is not None:
            print(f"{var} = [   # (beat in the {unit}, pitch semitones, length beats, dB)")
            for beat, f, pitch, vol, length, _, _ in data:
                print(f"    ({beat!r}, {pitch!r}, {length!r}, {vol!r}),")
        else:
            print(f"{var} = [   # (beat in the {unit}, sample, length beats, dB)")
            for beat, f, pitch, vol, length, _, _ in data:
                assert pitch == 0, (track, pitch)
                print(f"    ({beat!r}, {f!r}, {length!r}, {vol!r}),")
        print("]")
    return 0


if __name__ == "__main__":
    sys.exit(main())
