"""
DATA MODULE since stages 3-8 Task 15b (read by mixer.py (HURT_LOW_CUT, HURT_GAIN)). The FMOD script described below (bed_combo.js) and its
timeline tracks were deleted; running this file only prints that.

Adaptive bed, axis 2: combo layers and the hurt dip for event:/Music/ChordPulseGame (Docs/AdaptiveAudioPlan.md 8-2).

The bed_combo.js script and the timeline tracks it wrote are gone; the notes below describe what the tracks were.

Game parameters:
- Combo 0..3: kill streak level, changed by Unity only on bar lines (instruments are gated by conditions).
    1+  16th closed hats on the "e" and "a" of every beat          (track "Combo hats", in Music Bed)
    2+  airy glass pad, one high chord per bar                      (track "Combo pad", in Harmony)
    3   formant-bell arpeggio on the off-beats, above the pluck     (track "Combo bells", in Harmony)
  Pad and bells sit in Harmony, so kill chords duck them like the pluck.
- Hurt 0..1: Unity snaps it to 1 when the core is hit; FMOD's seek speed brings it back to 0 in ~0.6 s.
  Cutting the lows (3-band EQ, low band 0 -> -24 dB below 300 Hz) and a gain dip (-4 dB) on Music Bed make
  the hit "knock the wind out" of the accompaniment. It works on the lows, so it never fights the Danger
  low-pass from bed_tension.py.
"""
import os

HERE = os.path.dirname(os.path.abspath(__file__))

HAT = ("Pallor_ClosedHat_Synthpop_v110.wav", 0)
PAD = ("Synth/AT20V_GlassPad_A3_v90.wav", 57)
BELLS = [("Synth/ATFS1_FormantBells_C5_v100.wav", 72), ("Synth/ATFS1_FormantBells_G5_v100.wav", 79)]

# Per bar of the phrase (C, Am, F, G): pad voicing (above the tension strings) and bell arpeggio (off-beats).
BARS = [
    dict(pad=(67, 72, 76), bells=(76, 79, 84, 79)),   # C:  G4 C5 E5 / E5 G5 C6 G5
    dict(pad=(69, 72, 76), bells=(76, 81, 84, 81)),   # Am: A4 C5 E5 / E5 A5 C6 A5
    dict(pad=(69, 72, 77), bells=(77, 81, 84, 81)),   # F:  A4 C5 F5 / F5 A5 C6 A5
    dict(pad=(67, 71, 74), bells=(74, 79, 83, 79)),   # G:  G4 B4 D5 / D5 G5 B5 G5
]
LEVEL = {"hats": -16.0, "pad": -27.0, "bells": -24.0}     # instrument volume (dB); set from offline renders
HURT_LOW_CUT = [(0.0, 0.0), (0.3, -8.0), (1.0, -24.0)]   # 3-band EQ low band gain (dB), crossover 300 Hz
HURT_GAIN = [(0.0, 0.0), (1.0, -4.0)]


def pick(table, midi):
    sample = min(table, key=lambda s: (abs(midi - s[1]), -s[1]))
    return sample, midi - sample[1]


# Stages 3-8 Task 15b: the timeline tracks this file built were deleted from the music event (retired/cleanup_music.py),
# and bed_combo.js with them. Only the data above is still read, by mixer.py (HURT_LOW_CUT, HURT_GAIN); this file no longer writes an
# FMOD script, so running it cannot rebuild those tracks.
if __name__ == "__main__":
    raise SystemExit("bed_combo.py is a data module now (Task 15b): its timeline tracks and bed_combo.js were deleted")
