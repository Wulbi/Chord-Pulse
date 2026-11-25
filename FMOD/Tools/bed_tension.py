"""
DATA MODULE since stages 3-8 Task 15b (read by mixer.py (CURVES, DANGER_CUTOFF)). The FMOD script described below (bed_tension.js) and its
timeline tracks were deleted; running this file only prints that.

Adaptive bed, axis 1: tension layers for event:/Music/ChordPulseGame (Docs/AdaptiveAudioPlan.md 8-1).

The bed_tension.js script and the timeline tracks it wrote are gone; the notes below describe what the tracks were.

Two game parameters, both 0..1 and smoothed by FMOD seek speed:
- Tension: how close the nearest enemy is / how low health is (Unity computes it).
    0.25-0.45  string pad fades in (one chord per bar)
    0.45-0.60  sub pulse on the off-beats fades in (between the kicks)
    0.65-0.85  the bass crossfades into a squelchy acid bass
- Danger: last life. A low-pass on "Music Bed" closes from 20 kHz to 900 Hz, so the accompaniment
  sounds smothered while kill chords and feature loops (outside Music Bed) stay clear.
Levels and curves were set from offline renders (bed RMS -36 dB): the layers add up to about
+1.5 dB at Tension 1, and the bass/acid crossfade does not dip.
All layers sit inside "Music Bed", so MusicMix still controls them.
Notes cover every bar of all four tempo sections; the chords are the usual C - Am - F - G per phrase.
"""
import os

HERE = os.path.dirname(os.path.abspath(__file__))

# file, root (MIDI), peak short-term RMS (dB), attack onset seconds. AT / DeviceNTI roots match file names.
STRINGS = [("Synth/AT700_EnsembleStrings_A2_v90.wav", 45, -9.2, 0.072)]
SUB = [("SynthBass/DeviceNTI_SineSub_F1_v100.wav", 29, -9.2, 0.0),
       ("SynthBass/DeviceNTI_SineSub_C2_v100.wav", 36, -9.8, 0.0)]
ACID = [("SynthBass/AT303_Squelch_A1_v127.wav", 33, -16.8, 0.001),
        ("SynthBass/AT303_Squelch_C2_v127.wav", 36, -15.9, 0.001),
        ("SynthBass/AT303_Squelch_E2_v127.wav", 40, -15.7, 0.001),
        ("SynthBass/AT303_Squelch_A2_v127.wav", 45, -15.1, 0.001)]

# Per bar of the phrase: (pad voicing, bass root). Voicings stay in the low-mid register under the plucks.
CHORDS = [
    ((52, 55, 60), 36),   # C:  E3 G3 C4 / C2
    ((52, 57, 60), 33),   # Am: E3 A3 C4 / A1
    ((53, 57, 60), 29),   # F:  F3 A3 C4 / F1
    ((50, 55, 59), 31),   # G:  D3 G3 B3 / G1
]

# Note loudness at full layer volume (before the Tension automation). Set from offline renders
# against the accompaniment (bed RMS -36 dB at Intensity 1).
TARGET_DB = {"strings": -31.0, "sub": -27.0, "acid": -33.0}

# Track volume automation on Tension: (position, dB).
CURVES = {
    "strings": [(0.0, -80.0), (0.25, -80.0), (0.3, -20.0), (0.45, -6.0), (0.75, -2.0), (1.0, 0.0)],
    "sub": [(0.0, -80.0), (0.45, -80.0), (0.5, -15.0), (0.6, -4.0), (1.0, 0.0)],
    # Crossfade with the bass, kept roughly equal-power so the low end never dips in the middle.
    "acid": [(0.0, -80.0), (0.65, -80.0), (0.7, -12.0), (0.8, -3.0), (1.0, 0.0)],
    "bass": [(0.0, 0.0), (0.65, 0.0), (0.75, -3.0), (0.85, -15.0), (1.0, -80.0)],
}
DANGER_CUTOFF = [(0.0, 20000.0), (0.4, 5000.0), (1.0, 900.0)]

ACID_PATTERN = [0, 0, 12, 0, 0, 12, 0, 12]   # 8ths: root / octave bounce
SUB_BEATS = [0.5, 1.5, 2.5, 3.5]             # off-beats, between kicks and snares


def pick(table, midi):
    sample = min(table, key=lambda s: (abs(midi - s[1]), -s[1]))
    pitch = midi - sample[1]
    assert -24 <= pitch <= 24, (sample, midi)
    return sample, pitch


def note(track, table, midi, start, length, vol=0.0, shift=True):
    sample, pitch = pick(table, midi)
    ratio = 2 ** (pitch / 12.0)
    return {"track": track, "file": sample[0], "pitch": pitch,
            "start": round(start - (sample[3] / ratio if shift else 0.0), 6), "length": round(length, 6),
            "volume": round(max(-60.0, min(TARGET_DB[track] - sample[2] + vol, 10.0)), 2)}


# Stages 3-8 Task 15b: the timeline tracks this file built were deleted from the music event (retired/cleanup_music.py),
# and bed_tension.js with them. Only the data above is still read, by mixer.py (CURVES, DANGER_CUTOFF); this file no longer writes an
# FMOD script, so running it cannot rebuild those tracks.
if __name__ == "__main__":
    raise SystemExit("bed_tension.py is a data module now (Task 15b): its timeline tracks and bed_tension.js were deleted")
