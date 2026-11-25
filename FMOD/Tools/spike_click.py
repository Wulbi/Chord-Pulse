"""
Scheduling-accuracy test signals (Task 3 spike, Docs/Captures/README.md "Scheduling spike").

    python FMOD/Tools/spike_click.py
    python FMOD/Tools/instruments.py   (registers the SpikeClick instrument through LAYERS)
    fmodstudiocl.exe -script FMOD/Tools/instruments.js FMOD/ChordPulse/ChordPulse.fspro
    fmodstudiocl.exe -build FMOD/ChordPulse/ChordPulse.fspro      (separate command)

Writes:
- FMOD/ChordPulse/Assets/Spike/click_3k.wav: a 5 ms 3 kHz sine burst, 48 kHz mono 16-bit. The attack is at sample 0
  (onset 0 s).
- register(lib): the SpikeClick instrument (3 kHz click) for instruments.py -> event:/Inst/<bus>/SpikeClick.

The 1 kHz "Spike ref" timeline track in event:/Music/ChordPulseGame (and its SpikeRef parameter) is gone with that
event (unity-music-clock Task 8, remove_music_event.py): the capture's spike clicks are measured against the Unity music
clock (`analyze_capture.py spike`, clock mode), so no reference click is needed. click_1k.wav is no longer written.
"""
import math
import os
import struct
import wave

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "ChordPulse", "Assets")
REL_3K = "Spike/click_3k.wav"

RATE = 48000
LENGTH_S = 0.005
AMPLITUDE = 0.7
ATTACK_S = 0.0002     # raised-cosine fade-in, short enough to keep the onset sharp
RELEASE_S = 0.001     # raised-cosine fade-out at the end of the 5 ms burst


def click_samples(freq):
    n = int(round(LENGTH_S * RATE))
    a, r = int(ATTACK_S * RATE), int(RELEASE_S * RATE)
    out = []
    for i in range(n):
        env = 1.0
        if i < a:
            env = 0.5 - 0.5 * math.cos(math.pi * i / a)
        elif i >= n - r:
            env = 0.5 - 0.5 * math.cos(math.pi * (n - 1 - i) / r)
        out.append(AMPLITUDE * env * math.sin(2 * math.pi * freq * i / RATE))
    return out


def short_term_rms_db(samples, window_s=0.05):
    """Peak short-term RMS over a 50 ms window (the burst is shorter, so this is the whole burst over 50 ms)."""
    n = int(window_s * RATE)
    energy = sum(s * s for s in samples)
    return 10 * math.log10(energy / n)


def write_wav(rel, samples):
    path = os.path.join(ASSETS, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(round(max(-1, min(1, s)) * 32767))) for s in samples))
    return path


CLICK_3K_RMS_DB = round(short_term_rms_db(click_samples(3000)), 1)


# Buses the click event exists on: Kill Chords (Task 3 spike) and one per routing kind of the pattern layers (Task 12
# follow-up: the one-shot latency is measured per bus with CaptureScenario.spikeTarget).
BUSES = ["Kill Chords", "Music Bed/Layers", "Music Bed/Layers/Bass", "Music Bed/Harmony", "Music Bed/Harmony/Combo Pad"]


def register(lib):
    """instruments.py layer: one note (MIDI 72) of the 3 kHz click on each of BUSES; onset 0."""
    for bus in BUSES:
        lib.add("SpikeClick", [(REL_3K, 72, CLICK_3K_RMS_DB, 0.0)], 8, bus=bus)
        lib.need("SpikeClick", bus, 72)


def main():
    write_wav(REL_3K, click_samples(3000))
    print("click_3k short-term RMS", CLICK_3K_RMS_DB, "dB")


if __name__ == "__main__":
    main()
