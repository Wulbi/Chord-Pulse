# Unity output captures

Reference recordings of what Unity/FMOD actually outputs, made before kill chords move from FMOD timeline notes to
Unity-scheduled one-shots (Docs/AdaptiveAudioPlan.md sections 9-10). Later tasks record the same scenarios the same way
and compare against these numbers. The WAV files themselves are git-ignored (`Docs/Captures/*.wav`, plus the
`.beats.csv` / `.cpu.txt` side files); only this README is committed.

## How to record

1. Open `Assets/Scenes/AudioCapture.unity` (only a `CaptureScenario` object; `SoundManager` is created on demand).
2. Pick `scenario` (`baseline` | `heavy` | `spike`), `seed` (7), `beats` (128) on the object and press Play.
   Leave the machine alone while it runs (about 75 s) and do not touch the Unity Editor; it leaves play mode by itself.
3. Output in `Docs/Captures/`: `<scenario>_seed<seed>.wav` (32-bit float stereo, FMOD software-mixer rate = 48000 Hz,
   taken at the tail of the master channel group, i.e. what is sent to the device), `.beats.csv` (beat, capture frame,
   BPM) and `.cpu.txt`. An existing file is never overwritten; the next take is named `..._run2`, `_run3`, ...
4. Check the `.cpu.txt` line (Studio and Core CPU, see below): it says `stalls >100 ms after the first beat N`. A take with N > 0 is not a valid reference
   (a main-thread hitch delays the kill chords, which changes the audio). Re-record.

Scenarios (all start `StartMusic()` with `sectionSeed = seed`, Intensity 0):

- `baseline`: every 4th beat one kill of each of the 3 colours (voicing 1).
- `heavy`: every beat 3 kills of each colour (voicing 3), Combo 3 and Tension 0.8 forced after `StartMusic()`.
- `spike`: no kills. The scheduling-accuracy test of Task 3 (section "Scheduling spike" below): sets the music's
  `SpikeRef` parameter to 1 (a 1 kHz timeline click on every beat) and schedules an `event:/Inst/SpikeClick` one-shot
  (3 kHz click) on beats 1..64 through `OneShotScheduler`. Record it with seed 33 and `beats` 68.

Beat times come from the music conductor's own FMOD-thread stamps, converted to capture frames through a line fitted to
the mixer-block clock (`AudioCapture.FrameAt`), so they do not depend on Unity's frame rate. The beat clock starts about
1-3.5 s after capture start (sample loading), so analyse by beat windows, not by absolute time.

## How to analyse

numpy lives in the user site-packages on this machine, so `python -I` (isolated mode) cannot import it; use plain `python`.
The first import of scipy can take about a minute on a cold start (only `onsets` needs it).

```
python FMOD/Tools/analyze_capture.py rms Docs/Captures/baseline_seed7.wav --window-beats 4 --bpm-map seed7
python FMOD/Tools/analyze_capture.py rms <wav> --bpm-map seed7 --compare <other wav>   # per-window diff, exit 1 if > 0.3 dB
python FMOD/Tools/analyze_capture.py onsets <wav> --ref-band 900-1100 --test-band 2800-3200
```

`--bpm-map seed7` is only a label: the beat log is the WAV's own `.beats.csv` (or pass a csv path). A `?` after a row
means the window is more than 10% off the length its start BPM predicts (tempo change inside it, or a hitch).
`onsets` pairs attacks in the two bands and prints mean / max |difference| in ms; it is meant for Task 3's click test
signals (on the music itself the numbers are meaningless, e.g. 16 ms mean on baseline).

## Results (Unity Editor, Windows, WASAPI, 128 beats, seed 7)

Sections of seed 7: 108 BPM for beats 0-31, 116 for 32-47, 108 for 48-95, 116 from 96.

### Reproducibility (0.3 dB limit, per 4-beat window RMS)

Both scenarios were recorded twice (`baseline_seed7` vs `_run2`, `heavy_seed7` vs `_run2`), all four takes with 0 stalls.

| pair | windows not flagged `?` | windows flagged `?` (tempo change inside the window) |
|---|---:|---:|
| baseline | max 0.07 dB | max 0.39 dB (windows 23 and 24 at the 108 to 116 BPM change) |
| heavy | max 0.17 dB | max 0.19 dB |

So the 0.3 dB limit holds everywhere except the windows around one tempo change in baseline, where the window edges
(derived from the smoothed beat stamps) move by a few ms between takes. Compare windows away from tempo changes at 0.3 dB.
Takes with a frame stall mid-run differed by up to 1.2 dB near the stall, hence the stall check above.

### Window RMS (dBFS, 4-beat windows; `baseline_seed7`, `heavy_seed7`)

| window | beat | BPM | baseline | heavy |
|---:|---:|---:|---:|---:|
| 0 | 0 | 108 | -35.65 | -32.44 |
| 1 | 4 | 108 | -35.57 | -32.44 |
| 2 | 8 | 108 | -35.05 | -32.46 |
| 3 | 12 | 108 | -35.10 | -32.32 |
| 4 | 16 | 108 | -34.93 | -32.44 |
| 5 | 20 | 108 | -34.81 | -32.38 |
| 6 | 24 | 108 | -34.42 | -32.48 |
| 7 | 28 | 108 | -34.46 | -32.11 |
| 8 | 32 | 116 | -34.77 | -32.26 |
| 9 | 36 | 116 | -34.56 | -32.09 |
| 10 | 40 | 116 | -34.22 | -31.88 |
| 11 | 44 | 116 | -34.32 | -32.52 |
| 12 | 48 | 108 | -34.69 | -32.71 |
| 13 | 52 | 108 | -34.79 | -32.36 |
| 14 | 56 | 108 | -34.35 | -32.49 |
| 15 | 60 | 108 | -34.72 | -32.37 |
| 16 | 64 | 108 | -35.17 | -32.57 |
| 17 | 68 | 108 | -35.16 | -32.32 |
| 18 | 72 | 108 | -34.91 | -32.41 |
| 19 | 76 | 108 | -35.95 | -32.95 |
| 20 | 80 | 108 | -35.12 | -33.14 |
| 21 | 84 | 108 | -35.16 | -32.36 |
| 22 | 88 | 108 | -34.98 | -32.51 |
| 23 | 92 | 108 | -35.71 | -33.20 |
| 24 | 96 | 116 | -34.73 | -32.94 |
| 25 | 100 | 116 | -34.60 | -32.11 |
| 26 | 104 | 116 | -34.30 | -31.88 |
| 27 | 108 | 116 | -32.32 | -31.19 |
| 28 | 112 | 116 | -34.76 | -32.24 |
| 29 | 116 | 116 | -34.60 | -32.10 |
| 30 | 120 | 116 | -34.32 | -31.86 |
| 31 | 124 | 116 | -35.13 | -32.62 |

Windows 11, 12, 19, 20, 23, 24 and 31 are flagged `?` by the tool (tempo change inside the window or a hitch); their values are still usable.

| | baseline | heavy |
|---|---:|---:|
| mean RMS (dBFS) | -34.79 | -32.38 |
| max window RMS (dBFS) | -32.32 | -31.19 |

### FMOD CPU (percent, sampled every frame during the 128 beats)

Two different counters. **Studio** is `Studio::System::getCPUUsage` `update` (the Studio update thread: event/parameter
scheduling; the figure the "about 11%" budget refers to). **Core** is `System::getCPUUsage` (mixer `dsp`, and
`total` = dsp + stream + update). The Studio figure is spiky (per-frame samples, max can reach about 100%), so use the
average. Measured in the Editor; compare takes made the same way.

| take | Studio update avg | Studio update max | Core dsp avg | Core dsp max | Core total avg | Core total max |
|---|---:|---:|---:|---:|---:|---:|
| baseline_seed7 | 10.02 | 91.17 | 1.54 | 2.36 | 1.67 | 2.52 |
| baseline_seed7_run2 | 10.37 | 93.90 | 1.49 | 2.70 | 1.62 | 2.82 |
| heavy_seed7 | 20.54 | 106.66 | 3.39 | 5.00 | 3.52 | 5.22 |
| heavy_seed7_run2 | 20.22 | 97.95 | 3.45 | 4.73 | 3.58 | 4.89 |

No dropped capture frames in any take.

## Scheduling spike (Task 3: can Unity start a one-shot exactly on a timeline beat?)

Gate: after removing one constant offset, attack error mean <= 2 ms and max <= 5 ms, including right after section
changes and with less than 0.4 beat of lead. **Result: PASS** with anchor B + start method 2 (now the only code path).

### Setup

- Test signals (`FMOD/Tools/spike_click.py`): 5 ms sine bursts, same envelope, attack at sample 0.
  - Reference: track `Spike ref` in the music event, a 1 kHz click on every beat of all 32 bars, gated by the
    event-local parameter `SpikeRef` (silent unless 1; Unity sets it only in this scenario).
  - Test: `event:/Inst/SpikeClick` (3 kHz click, MIDI 72), on `bus:/Kill Chords`, scheduled with
    `OneShotScheduler.Schedule(SpikeClick, 72, beat, 0.25, 0, 0)`.
- Even beats are scheduled 1 beat ahead (when the previous beat's anchor arrives), odd beats 0.3 beat ahead (when the
  beat position passes beat - 0.3). Logged lead: 1 beat = 420-556 ms, 0.3 beat = 116-193 ms.
- Seed 33 (not 7): in the first 64 beats seed 7 has only one timeline jump (beat 48) plus one in-place tempo change;
  seed 33 jumps at beats 16 (8333 -> 17777 ms, 108 -> 116 BPM), 32 (25536 -> 0 ms), 48 and 64.
- `python FMOD/Tools/analyze_capture.py spike <wav> [--offset-ms X] [--verbose]` pairs each beat's test click with its
  reference click (bands 900-1100 / 2800-3200 Hz) and reports error before / after removing the offset (the take's
  mean signed error, or `--offset-ms`). "4 beats after change" = the 4 beats from each jump / tempo change.

Candidates:

- Anchor A: DSP clock of the master channel group read inside the `TIMELINE_BEAT` callback.
- Anchor B: main thread, every frame: `getTimelinePosition` + master `getDSPClock`, worked back to the beat
  (`dsp - (timeline - beat position)`), running mean over the frames of the current beat.
- Start 1: `EVENT_PROPERTY.SCHEDULE_DELAY = dspStart - now`, then `start()`.
- Start 2: `start()`, then next frame `getChannelGroup()` -> `ChannelGroup.setDelay(dspStart, dspStop, true)`.

### Results (`spike_seed33_<combo>.wav`, Unity Editor, 1024-sample mixer blocks, 0 stalls in every take)

Errors in ms, test attack minus reference attack. "before" = raw, "after" = minus the take's own mean signed error.

| combo | paired | offset (mean signed) | before: mean / max | after: mean / max | after, 4 beats after a change: mean / max (n) | sd |
|---|---:|---:|---:|---:|---:|---:|
| A + 1 | 44 of 64 | 31.27 | 31.27 / 39.79 | 4.08 / 9.93 | 2.50 / 5.52 (5) | 4.85 |
| A + 2 | 57 of 64 | 12.85 | 12.85 / 22.79 | 5.46 / 11.23 | 4.24 / 8.44 (6) | 6.24 |
| B + 1 | 57 of 64 | 23.63 | 23.63 / 25.10 | **0.77 / 1.61** | 0.68 / 1.39 (6) | 0.90 |
| B + 2 | 57 of 64 | 2.29 | 2.29 / 4.58 | **0.79 / 2.30** | 0.81 / 1.61 (6) | 0.92 |
| B + 2, offset -110 samples applied (`_B2_offset`) | 57 of 64 | (-0.01) | **0.79 / 2.00** | - | 0.60 / 0.98 (6) | 0.94 |

By lead (after correction): B + 2 1 beat 0.80 / 1.62, 0.3 beat 0.77 / 2.30; confirmation take 1 beat 0.79 / 1.62
(signed -0.78), 0.3 beat 0.78 / 2.00 (signed +0.74). Studio update CPU avg 9.0-10.5% in all takes.

Why A fails: the DSP clock read in the beat callback only moves in mixer blocks (1024 samples = 21.3 ms), so the
anchor is a sawtooth of 0-21 ms against the real beat (the logged error ramps down by ~0.9 ms per beat and wraps).
With start 1 the extra Studio-update latency pushes 13 clicks past the 40 ms pairing window (counted as unpaired).
Start 1 is as steady as start 2 with anchor B, but sits 24 ms late, an offset that depends on when the Studio update
thread picks up the command; start 2 places the channel group on an absolute DSP time, so its offset is only 2.3 ms.

**Chosen: anchor B + start 2, `OneShotScheduler.DefaultScheduleOffsetSamples = -110`** (2.29 ms at 48 kHz). The
A path (beat-callback DSP) and the `SCHEDULE_DELAY` path were removed from the code.

### What is not covered (same in all four combos)

- Beats 16, 32, 48, 64 (jump landings): the music output is digital silence for 450-570 ms right after each of
  these jumps (seed 33 takes; baseline_seed7 shows one 490 ms gap too), so there is no reference click. The test
  click still plays on the pre-jump beat grid (B + 2: +1.4 to +2.1 ms from the grid of the beat before, before the
  2.29 ms correction), and the grid continues unchanged after the gap (beats 18, 19 are on time). The gap is in the
  music event itself, not in the scheduling.
- Beats 17, 33, 49 (0.3 beat lead, the beat after a landing) were not scheduled: FMOD delivers the landing beat's
  `TIMELINE_BEAT` callback late, together with the next one (in the silent gap), so when the 0.3-beat point passes
  the clock has no anchor for the landing beat yet and the next beat is already past. Scheduling across a section
  boundary has to extrapolate with the next section's tempo, which `SectionSelector` knows a phrase ahead (Task 5).

### Note parameter is per instance

Runtime check (Editor, play mode): two `event:/Inst/Marimba` instances started together with `Note` 72 and 79 each
read back their own value (`getParameterByName` 72 / 79), the parameter description is not GLOBAL, and each plays its
own sample (`Marimba_hit_Outrigger_C4` / `_G4`); a third instance with Note 60 (no sample) has no channel.

### Sidechain from bus:/Kill Chords into the Harmony compressor (for Task 6): not possible this way

`FMOD/Tools/kill_bus_sidechain.js` adds a Sidechain effect to `bus:/Kill Chords` and adds it to the Harmony
compressor's `relationships.sidechains`. Studio accepts and builds it, but at runtime (FMOD 2.02.35) the link is not
made: the compressor DSP keeps only its two in-event `SEND_SIDECHAIN` inputs, the bus has no sidechain send, and
three bursts of loud Marimba one-shots on the bus (bus level +1 to +2 dBFS, Harmony playing) gave 0.00 dB gain
reduction. The link was removed again (`REMOVE = true`). Ducking from one-shots needs Harmony on a global bus too
(bus -> bus sidechain), which the spec's bus layout already plans.
