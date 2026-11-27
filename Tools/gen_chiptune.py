"""Generate NES-style chiptune one-shots into SampleLibrary/Chiptune/.

Everything here is synthesized from scratch (no third-party audio), so the
output is the project's own material. The output is deterministic: running
the script again recreates byte-identical files.

    python Tools/gen_chiptune.py

Voices (modelled on the NES 2A03 channels):
  pulse  - square wave with 12.5 / 25 / 50 % duty, 4-bit stepped volume
  tri    - 32-step triangle (the NES triangle has no volume control)
  noise  - 15-bit LFSR, long mode (hiss) and short mode (metallic)
Format: 44.1 kHz, 16-bit, mono WAV.
"""
import math
import struct
import wave
from pathlib import Path

SR = 44100
OUT = Path(__file__).resolve().parent.parent / "SampleLibrary" / "Chiptune"
NOTE_INDEX = {"C": 0, "C#": 1, "D": 2, "D#": 3, "E": 4, "F": 5, "F#": 6,
              "G": 7, "G#": 8, "A": 9, "A#": 10, "B": 11}


def freq(note: str) -> float:
    name, octave = note[:-1], int(note[-1])
    midi = 12 * (octave + 1) + NOTE_INDEX[name]
    return 440.0 * 2 ** ((midi - 69) / 12)


def quant_vol(v: float) -> float:
    """NES envelopes have 16 volume levels."""
    return round(max(0.0, min(1.0, v)) * 15) / 15


def env_pluck(t: float, decay: float) -> float:
    return quant_vol(math.exp(-t / decay))


def env_hold(t: float, length: float, release: float = 0.08) -> float:
    if t < length - release:
        return quant_vol(0.85)
    return quant_vol(0.85 * max(0.0, (length - t) / release))


def pulse(phase: float, duty: float) -> float:
    return 1.0 if (phase % 1.0) < duty else -1.0


def tri(phase: float) -> float:
    p = phase % 1.0
    step = int(p * 32)  # 32-step staircase like the 2A03 triangle
    level = step if step < 16 else 31 - step
    return level / 7.5 - 1.0


class Noise:
    def __init__(self, short: bool):
        self.reg = 1
        self.tap = 6 if short else 1

    def next(self) -> float:
        bit = (self.reg & 1) ^ ((self.reg >> self.tap) & 1)
        self.reg = (self.reg >> 1) | (bit << 14)
        return 1.0 if self.reg & 1 else -1.0


def write(name: str, samples: list[float], gain: float = 0.6) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    frames = b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s * gain)) * 32767)) for s in samples)
    with wave.open(str(OUT / f"{name}.wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(frames)


def render(length: float, fn) -> list[float]:
    return [fn(i / SR) for i in range(int(length * SR))]


def tone(wave_fn, f: float, length: float, env) -> list[float]:
    return render(length, lambda t: wave_fn(f * t) * env(t))


def noise_hit(length: float, short: bool, decay: float, rate: int) -> list[float]:
    """rate = how often (in samples) the LFSR clocks; lower = brighter."""
    n = Noise(short)
    out, cur = [], 0.0
    for i in range(int(length * SR)):
        if i % rate == 0:
            cur = n.next()
        out.append(cur * env_pluck(i / SR, decay))
    return out


def main() -> None:
    # Pulse plucks at three duty cycles.
    for duty, label in ((0.125, "Pulse12"), (0.25, "Pulse25"), (0.5, "Pulse50")):
        for note in ("C3", "C4", "C5"):
            write(f"Chip_{label}_Pluck_{note}",
                  tone(lambda p, d=duty: pulse(p, d), freq(note), 0.6, lambda t: env_pluck(t, 0.18)))

    # Sustained lead with delayed vibrato.
    f0 = freq("C4")
    phase = 0.0
    lead = []
    for i in range(int(1.0 * SR)):
        t = i / SR
        vib = 1 + (0.012 * math.sin(2 * math.pi * 6 * t) if t > 0.25 else 0.0)
        phase += f0 * vib / SR
        lead.append(pulse(phase, 0.5) * env_hold(t, 1.0))
    write("Chip_Pulse50_Lead_C4", lead)

    # Triangle bass.
    for note in ("C2", "G2", "C3"):
        write(f"Chip_TriBass_{note}", tone(tri, freq(note), 0.5, lambda t: env_hold(t, 0.5, 0.05)), gain=0.8)

    # Fast chord arpeggios (one note per 1/60 s frame) for the game's chords.
    chords = {"C": ("C4", "E4", "G4"), "Am": ("A3", "C4", "E4"),
              "F": ("F3", "A3", "C4"), "G": ("G3", "B3", "D4")}
    for name, notes in chords.items():
        fs = [freq(n) for n in notes]
        phase, out = 0.0, []
        for i in range(int(0.6 * SR)):
            t = i / SR
            phase += fs[int(t * 60) % 3] / SR
            out.append(pulse(phase, 0.25) * env_pluck(t, 0.3))
        write(f"Chip_Arp_{name}", out)

    # Drums.
    kick, n, cur = [], Noise(False), 0.0
    phase = 0.0
    for i in range(int(0.25 * SR)):
        t = i / SR
        phase += (40 + 140 * math.exp(-t / 0.03)) / SR
        if i % 8 == 0:
            cur = n.next()
        kick.append(tri(phase) * env_pluck(t, 0.09) + cur * 0.3 * env_pluck(t, 0.008))
    write("Chip_Kick", kick, gain=0.6)  # tri + click peaks at 1.3 before gain
    write("Chip_Snare", noise_hit(0.22, short=False, decay=0.06, rate=2))
    write("Chip_HatClosed", noise_hit(0.06, short=True, decay=0.015, rate=1), gain=0.4)
    write("Chip_HatOpen", noise_hit(0.3, short=True, decay=0.09, rate=1), gain=0.4)

    # Game-y pitched FX.
    coin = render(0.35, lambda t: pulse((freq("B5") if t < 0.06 else freq("E6")) * t, 0.5) * env_pluck(t, 0.12))
    write("Chip_FX_Coin", coin)
    phase, up = 0.0, []
    for i in range(int(0.5 * SR)):
        t = i / SR
        phase += freq("C4") * 2 ** (t * 4) / SR
        up.append(pulse(phase, 0.125) * env_hold(t, 0.5))
    write("Chip_FX_PowerUp", up)
    print("wrote", len(list(OUT.glob("*.wav"))), "files to", OUT)


if __name__ == "__main__":
    main()
