"""
Measures a sample for samples.py: peak short-term RMS (dB) and attack onset (s).

    python FMOD/Tools/measure_sample.py <wav under FMOD/ChordPulse/Assets or absolute> [...]
    python FMOD/Tools/measure_sample.py --check      # re-measure existing samples.py entries first

Needs numpy/scipy (plain `python`, user site-packages). Measurement only: the generators never import this file; the
results are pasted into samples.py as data, so generation stays numpy-free (`python -I`).

- RMS: channels averaged to mono, RMS over a sliding 150 ms window (hop 5 ms), the loudest window, dB re full scale.
  150 ms is the window that reproduces samples.py's existing values (22 entries within 0.05 dB; 100 / 200 ms miss by
  up to 1.5 / 1.2 dB).
- Onset: the first sample whose |level| reaches ONSET_FRACTION of the file's peak (mono), in seconds from sample 0.
"""
import os
import sys
import warnings

import numpy as np
from scipy.io import wavfile

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "ChordPulse", "Assets")
WINDOW_S = 0.15
HOP_S = 0.005
ONSET_FRACTION = 0.1


def load(path):
    if not os.path.isabs(path) and not os.path.exists(path):
        path = os.path.join(ASSETS, path)
    with warnings.catch_warnings():
        warnings.simplefilter("ignore")   # vendor metadata chunks
        rate, data = wavfile.read(path)
    if data.dtype.kind == "i":
        data = data.astype(np.float64) / float(np.iinfo(data.dtype).max + 1)
    elif data.dtype.kind == "u":
        data = (data.astype(np.float64) - 128.0) / 128.0
    else:
        data = data.astype(np.float64)
    if data.ndim > 1:
        data = data.mean(axis=1)
    return rate, data


def peak_rms_db(rate, x, window_s=WINDOW_S):
    n = max(1, int(window_s * rate))
    hop = max(1, int(HOP_S * rate))
    c = np.concatenate([[0.0], np.cumsum(x * x)])
    if len(x) <= n:
        return 10 * np.log10(max(c[-1] / n, 1e-20))
    starts = np.arange(0, len(x) - n + 1, hop)
    e = (c[starts + n] - c[starts]) / n
    return 10 * np.log10(max(e.max(), 1e-20))


def onset_s(rate, x, fraction=ONSET_FRACTION):
    a = np.abs(x)
    i = int(np.argmax(a >= fraction * a.max()))
    return i / rate


def measure(path):
    rate, x = load(path)
    return round(float(peak_rms_db(rate, x)), 1), round(onset_s(rate, x), 3)


def check():
    """Re-measures samples.py entries that have a measured RMS and onset; prints the differences."""
    sys.path.insert(0, HERE)
    import samples
    rows = []
    for name in ("MARIMBA", "VIBES", "HARP", "CLAV", "BRASS", "GLASS", "STRINGS", "ACID"):
        for f, root, rms, onset in getattr(samples, name, []):
            r, o = measure(f)
            rows.append((f, rms, r, onset, o))
            print(f"{f:50s} rms {rms:6.1f} -> {r:6.1f} ({r - rms:+.1f})  onset {onset:.3f} -> {o:.3f} ({(o - onset) * 1000:+.0f} ms)")
    return rows


if __name__ == "__main__":
    if sys.argv[1:] == ["--check"]:
        check()
    else:
        for p in sys.argv[1:]:
            r, o = measure(p)
            print(f"{p}: rms {r} dB, onset {o} s")
