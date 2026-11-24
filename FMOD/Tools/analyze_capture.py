"""Analyse WAV recordings made by Unity's AudioCapture / CaptureScenario (Docs/Captures/README.md).

  python FMOD/Tools/analyze_capture.py rms <wav> --window-beats 4 --bpm-map seed7 [--compare <wav2>]
      Per-window RMS (dBFS). Windows are N beats long, cut along the beat log the capture wrote next to the WAV
      (<wav minus .wav>.beats.csv: beat,frame,bpm). --bpm-map is that log: a path to a csv, or any label such as
      "seed7" meaning "use the sidecar log of the WAV". With --compare the second recording is cut the same way
      (by its own beat log) and the per-window difference is printed (max abs difference at the end).

  python FMOD/Tools/analyze_capture.py onsets <wav> --ref-band 900-1100 --test-band 2800-3200
      Finds attack times in two frequency bands and pairs them: ref (e.g. a metronome click) vs test (e.g. a kill
      chord click). Prints the mean / max absolute and mean signed difference in ms.

  python FMOD/Tools/analyze_capture.py spike <wav> [--offset-ms X]
      Task 3 scheduling spike. Pairs every scheduled SpikeClick (3 kHz, test band 2800-3200) with the Spike ref
      timeline click (1 kHz, ref band 900-1100) of the same beat, using the take's sidecar logs
      (.beats.csv, .spike.csv, .timeline.csv). Prints error stats before and after removing a constant offset
      (the mean signed error of the take, or --offset-ms), for all beats and for the 4 beats after each section
      change (timeline discontinuity or tempo change). The verdict covers the measured (paired) beats only; beats
      that were not scheduled or have no click are counted and listed separately, never folded into the stats:
      "GATE: PASS on N measured beats; M not scheduled, K no click" (or FAIL). A PASS with M > 0 is conditional.

Needs numpy and scipy. Note: numpy lives in the user site-packages here, so `python -I` (isolated mode) cannot import
it; run with plain `python` (the WAVs are our own captures, nothing untrusted is loaded).
"""
import argparse
import csv
import os
import sys

import numpy as np


def read_wav(path):
    """Minimal RIFF reader for 32-bit float / 16-bit PCM; returns (float32 array [n, ch], rate)."""
    with open(path, "rb") as f:
        data = f.read()
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise SystemExit("not a WAV file: " + path)
    pos, fmt, samples = 12, None, None
    while pos + 8 <= len(data):
        chunk_id = data[pos:pos + 4]
        size = int.from_bytes(data[pos + 4:pos + 8], "little")
        body = data[pos + 8:pos + 8 + size]
        if chunk_id == b"fmt ":
            fmt = (int.from_bytes(body[0:2], "little"), int.from_bytes(body[2:4], "little"),
                   int.from_bytes(body[4:8], "little"), int.from_bytes(body[14:16], "little"))
        elif chunk_id == b"data":
            samples = body
        pos += 8 + size + (size & 1)
    if fmt is None or samples is None:
        raise SystemExit("missing fmt/data chunk: " + path)
    tag, channels, rate, bits = fmt
    if tag == 3 and bits == 32:
        audio = np.frombuffer(samples[:len(samples) // 4 * 4], dtype="<f4")
    elif tag == 1 and bits == 16:
        audio = np.frombuffer(samples[:len(samples) // 2 * 2], dtype="<i2").astype(np.float32) / 32768.0
    else:
        raise SystemExit("unsupported WAV format tag=%d bits=%d" % (tag, bits))
    audio = audio[:len(audio) // channels * channels].reshape(-1, channels)
    return audio.astype(np.float32), rate


def read_beats(wav_path, bpm_map):
    path = bpm_map if bpm_map and os.path.isfile(bpm_map) else os.path.splitext(wav_path)[0] + ".beats.csv"
    if not os.path.isfile(path):
        raise SystemExit("beat log not found: " + path)
    with open(path, newline="") as f:
        return [(int(r["beat"]), int(r["frame"]), int(round(float(r["bpm"])))) for r in csv.DictReader(f)]


def window_rms_db(wav_path, window_beats, bpm_map):
    audio, rate = read_wav(wav_path)
    beats = read_beats(wav_path, bpm_map)
    rows = []
    for first in range(0, len(beats) - window_beats, window_beats):
        a, b = beats[first], beats[first + window_beats]
        seg = audio[a[1]:b[1]]
        rms = float(np.sqrt(np.mean(seg.astype(np.float64) ** 2))) if len(seg) else 0.0
        db = 20 * np.log10(rms) if rms > 0 else float("-inf")
        # The beat log is stamped on the Unity main thread: a frame hitch lumps beats together. Flag such windows.
        expected = window_beats * 60.0 / a[2]
        actual = (b[1] - a[1]) / rate
        rows.append((a[0], a[2], a[1] / rate, db, abs(actual - expected) > 0.1 * expected))
    return rows, audio, rate


def cmd_rms(args):
    rows, audio, rate = window_rms_db(args.wav, args.window_beats, args.bpm_map)
    other = None
    if args.compare:
        other, _, _ = window_rms_db(args.compare, args.window_beats, None)
    print("%s  (%.2f s, %d Hz)" % (args.wav, len(audio) / rate, rate))
    header = "window  beat   bpm  start_s   RMS dBFS" + ("   other dBFS    diff dB" if other else "")
    print(header)
    worst = 0.0
    bad = 0
    for i, (beat, bpm, start, db, hitch) in enumerate(rows):
        line = "%5d  %4d  %4d  %7.2f  %9.2f%s" % (i, beat, bpm, start, db, "  ?" if hitch else "")
        bad += hitch
        if other and i < len(other):
            diff = other[i][3] - db
            worst = max(worst, abs(diff))
            line += "  %11.2f  %9.2f" % (other[i][3], diff)
        print(line)
    if bad:
        print("? = window length differs >10%% from the start BPM (tempo change inside the window, or a frame hitch: %d windows)" % bad)
    valid = [r[3] for r in rows if np.isfinite(r[3])]
    if valid:
        print("overall: mean %.2f dBFS, max %.2f dBFS, windows %d" % (np.mean(valid), np.max(valid), len(valid)))
    if other:
        print("max |diff| = %.3f dB over %d windows (limit 0.3 dB)" % (worst, min(len(rows), len(other))))
        return 0 if worst <= 0.3 else 1
    return 0


def parse_band(text):
    lo, hi = text.split("-")
    return float(lo), float(hi)


def band_onsets(mono, rate, band):
    from scipy import signal  # imported lazily: slow on a cold start, and rms does not need it
    sos = signal.butter(4, band, btype="bandpass", fs=rate, output="sos")
    env = np.abs(signal.sosfiltfilt(sos, mono))
    smooth = signal.butter(2, 300.0, btype="lowpass", fs=rate, output="sos")
    env = np.maximum(signal.sosfiltfilt(smooth, env), 0)
    peak = env.max()
    if peak <= 0:
        return np.array([])
    # Peaks at least 25% of the band maximum and 80 ms apart; the onset is where the envelope first rose
    # above 15% of that peak (so loud and quiet clicks are timed the same way).
    peaks, _ = signal.find_peaks(env, height=0.25 * peak, distance=int(0.08 * rate))
    onsets = []
    for p in peaks:
        lo = max(0, p - int(0.05 * rate))
        above = np.nonzero(env[lo:p + 1] >= 0.15 * env[p])[0]
        onsets.append((lo + (above[0] if len(above) else p - lo)) / rate)
    return np.array(onsets)


def cmd_onsets(args):
    audio, rate = read_wav(args.wav)
    mono = audio.mean(axis=1)
    ref = band_onsets(mono, rate, parse_band(args.ref_band))
    test = band_onsets(mono, rate, parse_band(args.test_band))
    if len(ref) == 0 or len(test) == 0:
        raise SystemExit("no onsets found (ref %d, test %d)" % (len(ref), len(test)))
    diffs = []
    for t in test:
        nearest = ref[np.argmin(np.abs(ref - t))]
        if abs(nearest - t) <= args.max_pair_ms / 1000.0:
            diffs.append((t - nearest) * 1000.0)
    print("ref onsets %d, test onsets %d, paired %d (within %.0f ms)" % (len(ref), len(test), len(diffs), args.max_pair_ms))
    if not diffs:
        return 1
    d = np.array(diffs)
    print("test - ref attack: mean |diff| %.2f ms, max |diff| %.2f ms, mean signed %.2f ms" %
          (np.mean(np.abs(d)), np.max(np.abs(d)), np.mean(d)))
    return 0


def read_csv(path):
    with open(path, newline="") as f:
        return list(csv.DictReader(f))


def stats(values):
    a = np.abs(np.array(values))
    return (float(a.mean()), float(a.max())) if len(a) else (float("nan"), float("nan"))


def cmd_spike(args):
    audio, rate = read_wav(args.wav)
    base = os.path.splitext(args.wav)[0]
    beats = {int(r["beat"]): (int(r["frame"]), float(r["bpm"])) for r in read_csv(base + ".beats.csv")}
    timeline = {int(r["beat"]): int(r["timeline_ms"]) for r in read_csv(base + ".timeline.csv")}
    spikes = read_csv(base + ".spike.csv")
    mono = audio.mean(axis=1)
    ref = band_onsets(mono, rate, (900.0, 1100.0))
    test = band_onsets(mono, rate, (2800.0, 3200.0))

    # Section changes: the timeline position does not continue from the previous beat, or the tempo changes.
    changes = []
    for b in sorted(timeline):
        if b - 1 in timeline and b - 1 in beats and b in beats:
            expected = timeline[b - 1] + 60000.0 / beats[b - 1][1]
            if abs(timeline[b] - expected) > 5 or beats[b][1] != beats[b - 1][1]:
                changes.append((b, timeline[b - 1], timeline[b], beats[b - 1][1], beats[b][1]))
    post = set()
    for b, *_ in changes:
        post.update(range(b, b + 4))

    # The beat log is the conductor's callback stamps; a beat callback can arrive late (e.g. at a jump landing, where
    # two beats arrive together). Only used to find the right click pair, so replace stamps that do not fit the
    # previous beat's length by the previous beat + one beat.
    approx_frame = {}
    for b in sorted(beats):
        frame, bpm = beats[b]
        if b - 1 in approx_frame:
            expected = approx_frame[b - 1] + 60.0 / beats[b - 1][1] * rate
            if abs(frame - expected) > 0.25 * 60.0 / beats[b - 1][1] * rate:
                frame = expected
        approx_frame[b] = frame

    # Reference click of every beat (ref onset nearest to the beat's approximate frame).
    ref_of = {}
    for b in sorted(approx_frame):
        approx = approx_frame[b] / rate
        near_ref = ref[np.abs(ref - approx) <= 0.06] if len(ref) else ref
        if len(near_ref):
            ref_of[b] = near_ref[np.argmin(np.abs(near_ref - approx))]

    rows, missing, gridded = [], [], []
    for r in spikes:
        b = int(r["beat"])
        if r["scheduled"] != "1" or b not in beats:
            missing.append((b, "not scheduled"))
            continue
        rt = ref_of.get(b)
        if rt is None:
            # The music is silent at a jump landing (no ref click). Predict the beat from the previous beat's ref
            # click and tempo; these beats are listed apart, not mixed into the stats.
            if b - 1 in ref_of:
                grid = ref_of[b - 1] + 60.0 / beats[b - 1][1]
                near_test = test[np.abs(test - grid) <= args.max_pair_ms / 1000.0] if len(test) else test
                if len(near_test):
                    tt = near_test[np.argmin(np.abs(near_test - grid))]
                    gridded.append((b, float(r["lead"]), (tt - grid) * 1000.0))
                    continue
            missing.append((b, "no ref click"))
            continue
        near_test = test[np.abs(test - rt) <= args.max_pair_ms / 1000.0] if len(test) else test
        if len(near_test) == 0:
            missing.append((b, "no test click"))
            continue
        tt = near_test[np.argmin(np.abs(near_test - rt))]
        rows.append((b, float(r["lead"]), (tt - rt) * 1000.0))

    print("%s: scheduled %d, paired %d, missing %d" % (os.path.basename(args.wav), len(spikes), len(rows), len(missing)))
    for b, why in missing:
        print("  missing beat %d: %s" % (b, why))
    if changes:
        print("section changes: " + ", ".join("beat %d (%d->%d ms, %g->%g BPM)" % c for c in changes))
    if not rows:
        return 1
    for b, lead, e in gridded:
        print("  beat %d: no ref click (music silent at the jump landing); test click vs grid from beat %d: %+.2f ms"
              " (not in the stats)" % (b, b - 1, e))
    d = np.array([x[2] for x in rows])
    offset = args.offset_ms if args.offset_ms is not None else float(d.mean())
    print("offset used for correction: %.2f ms (%s) = %.0f samples at %d Hz" %
          (offset, "given" if args.offset_ms is not None else "mean signed error of this take", offset * rate / 1000.0, rate))

    def line(name, sel):
        raw = [x[2] for x in rows if sel(x)]
        cor = [v - offset for v in raw]
        m, mx = stats(raw)
        cm, cmx = stats(cor)
        sd = float(np.std(raw)) if raw else float("nan")
        print("  %-22s n=%3d  before: mean|e| %6.2f max|e| %6.2f signed %6.2f sd %5.2f   after: mean|e| %5.2f max|e| %5.2f ms" %
              (name, len(raw), m, mx, float(np.mean(raw)) if raw else float("nan"), sd, cm, cmx))
        return cm, cmx

    overall = line("all", lambda x: True)
    line("lead 1 beat", lambda x: x[1] >= 0.99)
    line("lead 0.3 beat", lambda x: x[1] < 0.99)
    jump = line("4 beats after change", lambda x: x[0] in post)
    if args.verbose:
        for b, lead, e in rows:
            print("    beat %3d lead %.1f err %7.2f ms%s" % (b, lead, e, "  *" if b in post else ""))
    ok = overall[0] <= 2 and overall[1] <= 5 and (np.isnan(jump[1]) or jump[1] <= 5)
    unscheduled = sum(1 for _, why in missing if why == "not scheduled")
    no_click = len(missing) - unscheduled
    print("GATE (after correction, mean <= 2 ms, max <= 5 ms, all and after changes): %s on %d measured beats; "
          "%d not scheduled, %d no click (+%d jump landings timed against the grid only)%s"
          % ("PASS" if ok else "FAIL", len(rows), unscheduled, no_click, len(gridded),
             "; conditional: not every beat was measured" if ok and missing else ""))
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("rms")
    p.add_argument("wav")
    p.add_argument("--window-beats", type=int, default=4)
    p.add_argument("--bpm-map", default=None, help="beat log csv, or a label (e.g. seed7) to use the WAV's sidecar log")
    p.add_argument("--compare", default=None, help="second recording to diff against (reproducibility check)")
    p.set_defaults(func=cmd_rms)
    p = sub.add_parser("onsets")
    p.add_argument("wav")
    p.add_argument("--ref-band", required=True, help="Hz range, e.g. 900-1100")
    p.add_argument("--test-band", required=True, help="Hz range, e.g. 2800-3200")
    p.add_argument("--max-pair-ms", type=float, default=50.0)
    p.set_defaults(func=cmd_onsets)
    p = sub.add_parser("spike")
    p.add_argument("wav")
    p.add_argument("--offset-ms", type=float, default=None, help="constant correction; default: this take's mean signed error")
    p.add_argument("--max-pair-ms", type=float, default=40.0)
    p.add_argument("--verbose", action="store_true")
    p.set_defaults(func=cmd_spike)
    args = parser.parse_args()
    sys.exit(args.func(args))


if __name__ == "__main__":
    main()
