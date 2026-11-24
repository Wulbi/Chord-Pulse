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

  python FMOD/Tools/analyze_capture.py frames <wav>
      Reads <wav minus .wav>.frames.csv (dt,beat,bpm per Unity frame: the visual beat SoundManager.BeatPosition).
      Prints the number of backward steps, max |delta beat - dt*bpm/60| (with its beat position) and the longest hold:
      consecutive frames whose delta beat is below 0.25 * dt*bpm/60, in ms of wall time, with its beat position. Exit 1
      if the beat ever goes back, a frame deviates by more than 0.05 beat, or the longest hold exceeds 30 ms.

  python FMOD/Tools/analyze_capture.py landings <wav>
      For every 16th beat of the beat log: delay of the logged boundary frame against the frame predicted from the
      previous beat and its BPM (ms), RMS of the first and the next half beat after the predicted boundary, RMS of the
      same place one phrase (8 beats) earlier, and silent runs (>= 20 ms, 5 ms windows with peak below -70 dBFS)
      within one beat after the predicted boundary. A run is a "gap" only if the same offset one phrase earlier
      (boundary b-16, when present) has no silent run covering at least half of it; runs that are silent there as well are listed as "rest".

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


def rms_db(mono, a, b):
    a, b = max(0, int(round(a))), max(0, int(round(b)))
    seg = mono[a:b].astype(np.float64)
    if len(seg) == 0:
        return float("-inf")
    rms = float(np.sqrt(np.mean(seg ** 2)))
    return 20 * np.log10(rms) if rms > 0 else float("-inf")


def fmt_db(v):
    return "silent" if not np.isfinite(v) or v < -150.0 else "%.1f" % v


def silent_runs(mono, start, length, rate, win_ms=5.0, min_ms=20.0, floor_db=-70.0):
    """Runs of >= min_ms made of win_ms windows whose peak is below floor_db; returns [(offset_ms, length_ms)]."""
    win = max(1, int(round(rate * win_ms / 1000.0)))
    thr = 10 ** (floor_db / 20.0)
    start = int(round(start))
    runs, run_start, n = [], None, int(length // win)
    for i in range(n):
        seg = mono[start + i * win:start + (i + 1) * win]
        quiet = len(seg) > 0 and float(np.max(np.abs(seg))) < thr
        if quiet and run_start is None:
            run_start = i
        if (not quiet or i == n - 1) and run_start is not None:
            end = i + 1 if quiet else i
            if (end - run_start) * win_ms >= min_ms:
                runs.append((run_start * win_ms, (end - run_start) * win_ms))
            run_start = None
    return runs


def cmd_landings(args):
    audio, rate = read_wav(args.wav)
    mono = audio.mean(axis=1)
    path = os.path.splitext(args.wav)[0] + ".beats.csv"
    if not os.path.isfile(path):
        raise SystemExit("beat log not found: " + path)
    beats = {int(r["beat"]): (int(r["frame"]), float(r["bpm"])) for r in read_csv(path)}
    print("%s" % os.path.basename(args.wav))
    print("beat   bpm  delay_ms  first_half_dB  next_half_dB  prev_phrase_dB  gaps (offset+length ms after the expected boundary); rest = also silent one phrase earlier")
    for b in sorted(beats):
        if b == 0 or b % 16 != 0 or b - 1 not in beats:
            continue
        frame, bpm = beats[b]
        pf, pbpm = beats[b - 1]
        expected = pf + rate * 60.0 / pbpm
        half = 0.5 * rate * 60.0 / bpm
        first = rms_db(mono, expected, expected + half)
        nxt = rms_db(mono, expected + half, expected + 2 * half)
        prev = "n/a"
        if b - 8 in beats:
            ef, ebpm = beats[b - 8]
            prev = fmt_db(rms_db(mono, ef, ef + 0.5 * rate * 60.0 / ebpm))
        runs = silent_runs(mono, expected, rate * 60.0 / bpm, rate)
        gaps, rests = [], []
        prev_runs = None
        if b - 16 in beats and b - 17 in beats:
            prev_expected = beats[b - 17][0] + rate * 60.0 / beats[b - 17][1]
            scale = beats[b - 16][1] / bpm  # previous phrase's beat length in ms / this one's: compare in beats
            prev_runs = [(po * scale, pl * scale) for po, pl in
                         silent_runs(mono, prev_expected, rate * 60.0 / beats[b - 16][1], rate)]
        for off, length in runs:
            # A rest: a silent run one phrase earlier covers at least half of this one.
            quiet_before = prev_runs is not None and any(
                min(off + length, po + pl) - max(off, po) >= 0.5 * length for po, pl in prev_runs)
            (rests if quiet_before else gaps).append("%.0f+%.0f" % (off, length))
        sil = ", ".join(gaps) if gaps else "-"
        if rests:
            sil += "  (rest: " + ", ".join(rests) + ")"
        print("%4d  %4.0f  %+8.0f  %13s  %12s  %14s  %s" % (
            b, bpm, (frame - expected) / rate * 1000.0, fmt_db(first), fmt_db(nxt), prev, sil))
    return 0


def cmd_frames(args):
    path = os.path.splitext(args.wav)[0] + ".frames.csv"
    if not os.path.isfile(path):
        raise SystemExit("frame log not found: " + path)
    rows = [(float(r["dt"]), float(r["beat"]), float(r["bpm"])) for r in read_csv(path)]
    # Frames before the music runs report a negative beat; start at the first frame with a beat >= 0.
    first = next((i for i, r in enumerate(rows) if r[1] >= 0), None)
    if first is None or len(rows) - first < 2:
        raise SystemExit("no frames with a running beat clock in " + path)
    backward, worst, worst_at, over, stalled = 0, 0.0, 0.0, 0, 0
    hold_ms, hold_best, hold_at = 0.0, 0.0, 0.0
    for i in range(first + 1, len(rows)):
        dt, beat, bpm = rows[i]
        delta = beat - rows[i - 1][1]
        if delta < 0:
            backward += 1
        if delta == 0 and dt > 0:
            stalled += 1
        dev = abs(delta - dt * bpm / 60.0)
        if dev > worst:
            worst, worst_at = dev, beat
        if dev > 0.05:
            over += 1
        if delta < 0.25 * dt * bpm / 60.0:
            if hold_ms == 0.0:
                hold_start = rows[i - 1][1]
            hold_ms += dt * 1000.0
            if hold_ms > hold_best:
                hold_best, hold_at = hold_ms, hold_start
        else:
            hold_ms = 0.0
    print("%s: %d frames, backward steps %d, frozen frames (delta 0) %d, max |delta beat - dt*bpm/60| %.4f beat at beat %.2f, frames over 0.05: %d, longest hold %.0f ms at beat %.2f (limit 30 ms)" % (
        os.path.basename(path), len(rows) - first, backward, stalled, worst, worst_at, over, hold_best, hold_at))
    return 1 if backward or over or hold_best > 30.0 else 0


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
    p = sub.add_parser("frames")
    p.add_argument("wav")
    p.set_defaults(func=cmd_frames)
    p = sub.add_parser("landings")
    p.add_argument("wav")
    p.set_defaults(func=cmd_landings)
    args = parser.parse_args()
    sys.exit(args.func(args))


if __name__ == "__main__":
    main()
