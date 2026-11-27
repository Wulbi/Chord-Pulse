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
  (Since unity-music-clock Task 8 the music event, its `Spike ref` track and `SpikeRef` no longer exist; the clicks are
  measured against the clock, see "Clock-referenced accuracy" and "Task 8 (unity-music-clock)" below.)
- `combobreak`: no kills; the screen combo is set from the mixer beat (+1 per beat from beat 4, 0 at `comboBreakBeats`)
  and writes `.ladder.csv`. See "Combo ladder — Task 6" at the end.

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
changes and with less than 0.4 beat of lead. **Result: conditional PASS** with anchor B + start method 2 (now the
only code path): every beat that was scheduled lands within the limits (57 of 64 measured; jump landings timed
against the grid), but 3 of 64 beats, the 0.3-beat-lead beat right after each jump landing, could not be scheduled
at all (see "What is not covered").

Ruling R11 (controller): the gate passes on start accuracy. The post-landing short-lead case is a gap in what the
clock knows (the landing beat's anchor arrives too late), not in how precisely a start is placed; Task 5 closes it by
extrapolating across the phrase boundary with the next section's BPM, and it is re-measured then.

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
A path (beat-callback DSP) and the `SCHEDULE_DELAY` path were removed from the code. Ruling R12: a one-shot whose
channel group appears only after its start time (or never, 30 frames) is stopped immediately and dropped, never played
late; the capture log shows it as "dropped (backend)" (0 in every take above).

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

### Task 5 re-measure: short lead across a jump landing (R11)

`OneShotScheduler` now holds notes until their start is at most 0.5 beat away, and `BeatClock.SetTempoFrom(beat, bpm)`
times beats past the next phrase boundary with the next section's BPM (the spike scenario feeds it every frame from
`SoundManager.NextPhraseStep` / `UpcomingPhrase`, BPM = 108 + 8 * section). Seed 33, 68 beats, offset -110, 0 stalls:

| take | short-lead trigger | paired | after correction: mean / max | 4 beats after change | not scheduled |
|---|---|---:|---:|---:|---|
| `spike_seed33_task5` | `SoundManager.BeatPosition` (as in Task 3) | 57 of 64 | 0.49 / 1.35 ms | 0.37 / 1.04 ms (6) | 17, 33, 49 |
| `spike_seed33_task5_dsp` | beat clock DSP (`shortLeadOnDsp`) | 58 of 64 | 0.53 / 2.06 ms | 0.56 / 1.02 ms (7) | 17, 49 |

Every scheduled click lands within 2 / 5 ms; beat 33 (post-landing, 0.3-beat lead) is now scheduled and measured.
17 and 49 are still missed, and the cause is upstream of the scheduler (debug take, per-frame log of the clock):

- `SoundManager.BeatPosition` is capped at anchor + 0.999 beat (`MusicConductor.MaxExtrapolationBeats`), so while the
  landing beat's callback is late it sits at 15.999 and a beat-position trigger fires only after beat 17 has passed.
  Game code should take "now in beats" from `BeatClock.BeatAt(MasterDsp())` instead (ruling R14; the clamp stays for
  visuals).
- Anchor B's running mean for beat 15 drifted later every frame from about beat 15.9 (583296 -> 585600+ samples, still
  growing at the boundary): the timeline position stalls ahead of the jump while the DSP clock runs on. That moves every
  `DspAt` past the boundary late, i.e. notes triggered earlier would have played tens of ms late without R12 noticing.

Fix (R14): `MusicConductor.PollAnchor` averages only polls up to half a beat after the beat; a beat whose callback
arrives later than that (jump landings) gets no anchor, and the clock keeps extrapolating from the previous anchor plus
`SetTempoFrom` (an unanchored pending change is folded into a predicted anchor when the next one is announced).
An intermediate version that still took one poll from such a late beat placed beat 50 (`_r14`) and beats 18 / 34
(`_r14_dsp`) 8-21 ms late; those takes are kept as evidence.

| take (after R14) | trigger | paired | raw mean / max | after correction mean / max | 4 beats after change | late > 5 ms |
|---|---|---:|---:|---:|---:|---:|
| `spike_seed33_B2` (Task 3 reference) | BeatPosition | 57 | 2.29 / 4.58 | 0.79 / 2.30 | 0.81 / 1.61 | - |
| `spike_seed33_r14b` | BeatPosition | 57 | 0.76 / 1.67 | 0.48 / 1.50 | 0.39 / 0.75 | 0 |
| `spike_seed33_r14b_dsp` | beat clock DSP | 57 | 0.76 / 2.67 | 0.53 / 1.96 | 0.59 / 1.39 | 0 |

In `_r14b_dsp` the post-landing 0.3-beat clicks 17, 33, 49 were all scheduled on time (lead 6640-7821 samples from
anchor 15/31/47 + pending tempo). 33 played (+1.27 ms). 17 and 49 were dropped by the backend because their channel
group appeared only after the start (R12, never late): Studio is stalled around a jump landing, the same stall that
silences the music for about 0.5 s. Beat 18 (scheduled from the late beat-17 callback) was already past and was
counted late. So one-shots due within about 0.5 s after a jump landing can be lost, but none plays late.

### Note parameter is per instance

Runtime check (Editor, play mode): two `event:/Inst/Marimba` instances started together with `Note` 72 and 79 each
read back their own value (`getParameterByName` 72 / 79), the parameter description is not GLOBAL, and each plays its
own sample (`Marimba_hit_Outrigger_C4` / `_G4`); a third instance with Note 60 (no sample) has no channel.

### Sidechain from bus:/Kill Chords into the Harmony compressor (for Task 6): not possible this way

`FMOD/Tools/retired/kill_bus_sidechain.js` (retired: superseded by `kill_oneshots.js`, now only throws) adds a Sidechain effect to `bus:/Kill Chords` and adds it to the Harmony
compressor's `relationships.sidechains`. Studio accepts and builds it, but at runtime (FMOD 2.02.35) the link is not
made: the compressor DSP keeps only its two in-event `SEND_SIDECHAIN` inputs, the bus has no sidechain send, and
three bursts of loud Marimba one-shots on the bus (bus level +1 to +2 dBFS, Harmony playing) gave 0.00 dB gain
reduction. The link was removed again (`REMOVE = true`). Ducking from one-shots needs Harmony on a global bus too
(bus -> bus sidechain), which the spec's bus layout already plans.

## Task 6: kill chords as scheduled one-shots

New scenarios in `CaptureScenario`: `styles` (MusicMix 0, bed style forced to `beat / 64 % 4`, 3 colours x voicing 1
on every beat; 256 beats), `duck` (only the Harmony group audible: other Music Bed tracks muted, Feature Duck return
muted, bus:/Kill Chords at -60 dB so its pre-fader key still ducks; kills every 4th beat, `duckKills` off = reference
take). Every take also writes `.kills.csv` (one-shots lost: late in the scheduler / dropped by the backend, by beat)
and a `KILLS` line in `.cpu.txt`; `duck` also writes `.duck.csv` (Harmony Duck compressor input / output RMS per
mixer block). `killsOnly` = MusicMix 0 for any scenario.

All takes: Unity Editor, 0 stalls, 0 late, 0 dropped.

| take | Studio update avg / max | Core dsp avg / max | one-shots started |
|---|---:|---:|---:|
| baseline_seed7 (Task 1) | 10.02 / 91.17 | 1.54 / 2.36 | - |
| baseline_seed7_task6 | 8.93 / 87.53 | 3.17 / 5.63 | 288 |
| heavy_seed7 (Task 1) | 20.54 / 106.66 | 3.39 / 5.00 | - |
| heavy_seed7_task6 | 9.63 / 90.26 | 7.12 / 10.19 | 3021 |
| styles_seed7_task6 | 6.54 / 69.02 | 2.55 / 3.69 | 2304 |
| heavy_seed33_task6_r15 (4 jumps, 68 beats) | 7.16 / 57.73 | 5.82 / 7.57 | 1629 |

The music event went from 12,376 to 10,456 notes (1,920 kill notes removed; 128 of the rest are the Spike ref track).
A first heavy take before the backend stopped finished instances explicitly had Studio update 20.29% and Core dsp
20.88% avg: instances whose channels had ended kept running until their timeline reached the end of the sample.

Levels (dBFS, `analyze_capture.py rms --compare` and per-beat windows):

| | Task 1 | Task 6 | diff |
|---|---:|---:|---:|
| baseline, 4-beat windows, mean of 32 | | | +0.84 (3 windows > 1 dB, max +1.41) |
| baseline, first 150 ms of the chord beat | -30.39 | -30.49 | -0.10 |
| baseline, whole chord beat | -33.49 | -31.53 | +1.96 |
| baseline, beats 2-4 of each window (no chord) | -35.79 | -35.60 | +0.19 |
| heavy, 4-beat windows | | | +1.70 .. +3.54 (mean +2.84) |
| heavy, first 150 ms of every beat | -29.23 | -28.29 | +0.94 |

The attack level of a style-0 kill chord is unchanged; the chord now rings for its table length (0.78 beat, then a
120 ms fade). The old timeline chords were effectively cut after about 0.36 beat: the parity scheme switched the
parameter off 0.25 beat after the chord started and FMOD untriggered the notes (120 ms ADSR release), contrary to the
comment in the old code. heavy also gains the combo/tension extensions (7th, bell, punch), which the Task 1 build did
not play (removed with kill_voices). Attack time vs the beat log, chord beats of baseline: new - old median 0.0 ms,
mean +0.8 ms (Task 1 run-to-run: +0.5 / -1.3 ms).

Styles (kill chords only, steady state over 59 beats each, after trimming dreamy's target to -28.5 dB):

| style | beat RMS vs style 0 | first 150 ms vs style 0 |
|---|---:|---:|
| 1 latin | +0.88 | +0.27 |
| 2 funk | -0.50 | -0.19 |
| 3 dreamy | +0.93 (was +1.14 at -28 dB) | -0.29 |

Ducking (`duck` vs `duck ... nokills`, Harmony only, 31 chord beats). Measured as the gain that maps the no-kill take
onto the duck take (least-squares per 20 ms bin, each segment aligned by cross-correlation). A plain energy ratio is
biased while the chord sounds: the duck take also holds the kill chord itself (bus at -60 dB), which is not negligible
next to a Harmony ducked by 10+ dB (the residual then sits at -10..-16 dB; alignment changes nothing, so beat-log jitter
is not the cause). The Harmony Duck compressor's own metering agrees once the key is gone (its input meter includes the
sidechain key, so out - in overstates the reduction while the chord sounds).

| Harmony Duck compressor | mean while the chord sounds | max | back within 1 dB | back within 0.5 dB |
|---|---:|---:|---:|---:|
| -32 dB, 4:1, 5 / 180 ms (old in-event values) | -10.4 dB | -14.6 dB | 686 ms after the fade end | never (1.1 s window) |
| **-20 dB, 2:1, 5 / 60 ms** (now) | **-3.5 dB** | **-4.7 dB** | at the fade end (+5 ms) | +225 ms (audio), ~+125 ms (meter) |

(Chord stop ~375 ms after the beat, fade end ~495 ms; these sections run at 124-132 BPM.) Two no-kill takes against
each other differ by up to 0.8 dB per bin, so the 0.5 dB crossing is at the noise floor. The -14 dB maximum of the old
values was real (the reduction peaks around the chord's stop and is held by the 180 ms release), not an artefact; the
old ratio-based mean (-7.9 dB) understated it.

Section-jump landings (heavy seed 33, kills every beat, jumps at 16/32/48/64): 0 late, 0 dropped, chords present on
L..L+3 for every landing, with the 1-beat post-boundary dispatch window (R15) and without it (0.5 beat): no difference
in this scenario. Spike re-measure with Task 6 code (`spike_seed33_task6`, DSP trigger): 64 / 64 scheduled and started,
0 dropped (Task 5 lost 17 and 49), mean 0.48 / max 1.54 ms before correction, 4 beats after a change 0.55 / 1.54 ms.

## Part A: visual beat and phrase-boundary landings (unity-music-clock, Task 2)

`CaptureScenario` also writes `<take>.frames.csv` (`dt,beat,bpm` of `SoundManager.BeatPosition` every Unity frame) and
`<take>.params.csv` (`beat_now,name,value` of every music-event parameter write, `beat_now` = `Clock.BeatAt(MasterDsp())`;
all writes go through `SoundManager.SetMusicParameter`). Inspector `holdArrangement` zeroes `_variationNext` / `_styleNext`
and calls `FeatureSelector.CancelNext()` every frame, so no arrangement parameter changes at a phrase boundary.

```
python FMOD/Tools/analyze_capture.py frames <wav>      # backward steps, max |delta beat - dt*bpm/60|; exit 1 if any backward step or > 0.05 beat
python FMOD/Tools/analyze_capture.py landings <wav>    # every 16th beat: log delay, RMS around the boundary, silent runs
```

`landings` columns: `delay_ms` = beat-log frame of the boundary minus the frame predicted from the previous beat and its BPM
(this is the timeline-callback clock, i.e. what the old visuals used); `first_half_dB` / `next_half_dB` = mono RMS of the half
beat after the predicted boundary and the half beat after that; `prev_phrase_dB` = RMS of the same place one phrase (8 beats)
earlier (beat b-8: the `prev_phrase_dB` column); `gaps` = runs of >= 20 ms of 5 ms windows with peak below -70 dBFS within one beat
after the predicted boundary (offset+length in ms). A run is printed separately as a `rest` only if the window at the same offset
in beats (ms scaled by bpm[b] / bpm[b-4]) after the previous bar's downbeat (beat b-4, its logged frame) is entirely silent by the same rule; a run that starts less
than 20 ms after the boundary, or any run when beat b-4 is not in the beat log, is always a gap. `silent` = exact digital silence.
Offsets are compared in beats, so a tempo change does not shift the comparison; the window must be silent in full, so a few ms of edge mismatch still turns a rest into a gap.

`frames` also reports the longest hold: consecutive frames that advance the beat by less than a quarter of `dt * bpm / 60`, in
ms of wall time (exit 1 above 30 ms). At about 220 fps a frozen frame deviates only about 0.01 beat, so the 0.05 per-frame
limit alone cannot see a multi-frame freeze; the hold run can.

### Visual beat vs beat log (baseline, seed 7)

Reference `baseline_seed7_jumpcheck` (before the DSP-clock visual beat) reproduces the spec table (48: +350 ms / -46.1 dB,
64: +95, 80: +321 / silent, 96: +823). Beat-log delays are large at 48, 80 and 96 (the timeline jumps, callbacks arrive late);
the screen beat is read from the DSP clock instead.

| take | beat-log delay 48 / 64 / 80 / 96 (ms) | `frames`: backward / max per-frame deviation (beat) / frames > 0.05 / longest hold |
|---|---|---|
| `baseline_seed7_jumpcheck` (old visuals) | +350 / +95 / +321 / +823 | no frame log |
| `_a2` (DSP visual beat, `max()` only) | +348 / +86 / +286 / +458 | 0 / 0.1214 at 97.05 / 1 / 59 ms at 96.92 |
| `_a2fix2` (`FollowBeat` v1: hold when behind, capped catch-up) | +291 / +9 / +326 / +462 | 0 / 0.0400 at 96.98 / 0 / 55 ms at 96.94 |
| `_a2fix3b` (`FollowBeat` now: half-step advance when behind, capped catch-up) | +302 / +3 / +281 / +443 | 0 / 0.0400 at 97.02 / 0 / 8 ms |
| `_a2_hold` (`holdArrangement`) | +365 / +1 / +320 / +368 | 0 / 0.0231 / 0 / 7 ms |
| `_a2_hold2` | +292 / -4 / +326 / +316 | 0 / 0.0097 / 0 / 6 ms |
| `_a2_hold3` | +310 / -4 / +257 / +309 | 0 / 0.0128 / 0 / 8 ms |

`_a2` held the shown beat for 14 frames at 96.92 after the late beat-96 anchor and then leapt 0.128 beat. `FollowBeat` v1 capped
the leap (0.04 beat per frame beyond the time step) but still held by construction (55 ms), which the per-frame metric cannot see.
`FollowBeat` now keeps advancing at half the time step while the clock is behind (never backwards, never zero) and catches up
by at most the time step plus 0.04 beat per frame; the 0.04 cap is why the max deviation reads 0.0400 where a catch-up happens.
The beat-log delay at 48 / 80 / 96 stays at several hundred ms by design (timeline callbacks arrive late); the screen follows the
DSP clock. All takes: `stalls >100 ms after the first beat 0`. (Two earlier recordings, `_a2fix` and `_a2fix3`, were cut short
when the editor stalled or reloaded and are incomplete; they have no side files.)

### Beat-64 delay: cause unconfirmed

Delay at boundary 64 per take (`landings`), with the writes of the arrangement parameters and NextSection near it
(`.params.csv`, beat position at which FMOD accepted the write; "-" = no params log):

| take | hold | delay at 64 | BedStyle / Variation before 64 | NextSection written at (phrase 48 / phrase 64) |
|---|---|---:|---|---|
| `baseline_seed7` | off | +90 ms | - | - |
| `baseline_seed7_task6` | off | +92 ms | - | - |
| `baseline_seed7_jumpcheck` | off | +95 ms | - | - |
| `_a2` | off | +86 ms | Variation 4 and BedStyle 2 @63.54 | 48.64 / **64.16** |
| `_a2fix2` | off | +9 ms | Variation 4 and BedStyle 2 @63.54 | 48.53 / 64.04 |
| `_a2fix3b` | off | +3 ms | Variation 4 and BedStyle 2 @63.54 | 48.56 / 64.01 |
| `_a2_hold` | on | +1 ms | none | 48.68 / 64.01 |
| `_a2_hold2` | on | -4 ms | none | 48.56 / 64.01 |
| `_a2_hold3` | on | -4 ms | none | 48.56 / 64.01 |

Other boundaries without a delay (15 - 16.5, 111 - 112.5) have Variation 3 @15.55 / Variation 1 @111.50 and NextSection at
16.01 / 112.04; only phrase 64 has a BedStyle write (0 to 2, `ignoreSeek` true).

What the table does and does not show:

- Held takes are all <= 10 ms, but so are the two latest un-held takes (+9, +3 ms), which still contain the BedStyle / Variation
  writes. The arrangement switch is therefore not sufficient to produce the delay, and the hold takes do not discriminate.
- The three old takes and `_a2` (+86 to +95 ms) were made with visuals that froze at the preceding jump landing. In `_a2`, the one
  large-delay take with a params log, NextSection for phrase 64 was written at 64.16 (the visual beat lagged); in the three takes
  with a small delay it was written at 64.01 - 64.04. 0.16 beat at 108 BPM is about 89 ms, close to the +86 ms. The writes are
  driven by the shown beat (`UpdatePhrase`), and `FollowBeat` changed the freeze. A late NextSection write is a candidate cause
  of the late boundary, but one take with a params log is not proof, and the old takes have no log.
- Cause unconfirmed. Re-measured after the clock switch (plan Task 8).

Silences in the hold takes at 16 / 32 / 64 / 112 (about 445 - 490 ms after the boundary, 65 - 70 ms long) are also silent at the same offset in beats
after the previous bar's downbeat (b-4), so `landings` prints them as `rest` at 16, 64 and 112. At 32 (tempo change 108 to 116) it still prints
445+70 as a gap: the b-4 window (beat 28) scaled to 478 - 553 ms starts about 12 ms before that bar's own silence (490 - 555 ms), so it is not
silent in full; by eye it is the same rest. These silences occur at every phrase boundary of the held arrangement. The landing silences at
48 / 80 / 96 (0 - 360 ms, first half beat silent) are gaps in every take.

## 2차 출발점 (stages 3-8, Task 5): low frame rate, standalone, CPU budget

Recorded with the code of commit 08db63b plus the Task 5 `CaptureScenario` change: new field `targetFps` (> 0 sets
`Application.targetFrameRate` and `vSyncCount = 0`) and an `ONESHOTS started/late/dropped/cancelled` item in `.cpu.txt`
(total over every one-shot user of the take: kill chords + spike clicks; the `KILLS` item stays). `heavy` / `spike` are
seed 33, 68 beats (jumps at 16 / 32 / 48 / 64; `spike` with `shortLeadOnDsp`), `baseline` is seed 7, 128 beats. All takes below
have `stalls >100 ms after the first beat 0`. Editor = Unity Editor play mode; standalone = Windows Development player built from a
copy of `AudioCapture.unity` (see "Standalone" below), `targetFps` 60.

Budget (plan Global Constraints): Core dsp average baseline <= 6 %, heavy <= 12 %; Studio update average <= 11 %.
All takes below are inside it (Editor heavy Core 5.9 - 6.1 %, Studio 8.9 - 9.9 %).

| take | fps | Studio update avg / max | Core dsp avg / max | ONESHOTS started / late / dropped (landing drops) / cancelled | `landings` delay at 16 / 32 / 48 / 64 (ms), gap | `frames` longest hold |
|---|---:|---|---|---|---|---:|
| baseline_seed7_s5_fps60 (Editor) | 60 | 7.03 / 78.57 | 1.86 / 2.90 | 288 / 0 / 0 (0) / 0 | not measured | - |
| heavy_seed33_s5_fps60c (Editor) | 60 | 8.90 / 69.65 | 6.14 / 7.82 | 1629 / 0 / 0 (0) / 24 | +288 / +256 / +344 / +401, no gap | 0 ms |
| heavy_seed33_s5_fps30 (Editor; kill chords may be early, see below) | 30 | 8.94 / 71.47 | 5.87 / 7.37 | 1629 / 0 / 0 (0) / 0 | +280 / +330 / +346 / +275, no gap | 0 ms |
| heavy_seed33_s5_fps15 (Editor; kill chords may be early, see below) | 15 | 9.85 / 78.20 | 5.96 / 7.77 | 1629 / 0 / 0 (0) / 0 | +419 / +394 / +569 / +426, no gap | 0 ms |
| spike_seed33_s5_fps60 (Editor) | 60 | 6.84 / 62.25 | 1.72 / 2.42 | 64 / 0 / 0 (0) / 0 | - | - |
| spike_seed33_s5_fps30 (Editor) | 30 | 7.50 / 65.65 | 1.57 / 2.41 | 64 / 0 / 0 (0) / 0 | - | - |
| spike_seed33_s5_fps15 (Editor) | 15 | 7.61 / 66.71 | 1.72 / 2.56 | 64 / 0 / 0 (0) / 0 | - | - |
| baseline_seed7_s5_standalone60 | 60 | 4.06 / 65.84 | 1.59 / 2.57 | 288 / 0 / 0 (0) / 0 | not measured | - |
| heavy_seed33_s5_standalone60 | 60 | 5.32 / 51.20 | 3.33 / 4.86 | 1629 / 0 / 0 (0) / 24 | +166 / +155 / +175 / +257, gap 285+55 ms at 64 | 0 ms |
| spike_seed33_s5_standalone60 (+ run2, run3) | 60 | 4.76 / 50.72 | 1.34 / 2.08 | 64 / 0 / 0 (0) / 0 | - | - |
| spike_seed33_s5_standalone (frame rate not capped, about 2050 fps) | ~2050 | 5.24 / 54.38 | 0.94 / 2.14 | 64 / 0 / **60** / 0 | - | - |

`landings` delay is the beat-log callback delay (not audible); the 16 / 32 / 48 / 64 landings had no digital silence in any Editor take
and the audio first / second half beat after the boundary is within about 2.5 dB of one phrase earlier (largest: 15 fps beat 48, -31.3 / -31.2 dB vs -28.8 dB; 60 fps beat 64, -30.6 vs -28.6 dB).
The take suffix `c` in `heavy_seed33_s5_fps60c` is the third attempt at that take: `_s5_fps60` is invalid (see below) and `_s5_fps60b` was cut short at beat ~12 (919 ms frame stall, no `.cpu.txt`, invalid). In the standalone `heavy` take the half beat after each landing is 10 - 13 dB quieter than one phrase earlier (-39 .. -44 dB vs -29.5 dB),
with a silent run at 64; not seen in the Editor, cause not investigated. The standalone `baseline` take ran while the Editor was building the heavy player (stalls 0, CPU may be slightly perturbed).
A first Editor `heavy` 60 fps take (`_s5_fps60`, listed as invalid above) had 7 stalls and
24 late / 48 landing drops and is not a valid reference (landing drops at beat 33.4 and 66.0, 24 late at 50.1).
Landing drops (kills created inside FMOD Studio's stall after a jump) did not occur in any valid take here.

Attack-criterion verdict (mean <= 2 ms, max <= 5 ms, `spike`): **60 fps pass, 30 fps FAIL, 15 fps FAIL**. At 30 / 15 fps the heavy rows
show ONESHOTS late 0 / dropped 0, but the kill chords use the same backend as the spike clicks and may also sound early (not measured).

ONESHOTS `cancelled` is 24 in the 60 fps `heavy` takes (Editor and standalone) and 0 at 30 / 15 fps; not verified. In the code, only
`CancelFrom` (fever re-voicing, not used in these scenarios), `StopAll` and `RescheduleFrom` can cancel; `RescheduleFrom` runs from
`OnNextSectionChanged` and cancels notes already handed to FMOD whose start is still ahead, so 24 is most likely the notes pending at the
moment one NextSection write arrived (`.params.csv` shows one at about beats 16.6 / 32.5 / 48.7 / 64.8); which frame that lands on would
differ with the frame rate.

### Attack error against the beat (`analyze_capture.py spike`, SpikeClick one-shots, offset -110 samples)

| take | paired beats | mean signed error before correction | mean / max abs after correction | clicks audible > 5 ms BEFORE their beat (see counting note) |
|---|---:|---:|---:|---:|
| spike_seed33_s5_fps60 (Editor) | 60 | +0.17 ms (+8 samples) | 0.91 / 2.77 ms | 0 of 54 |
| spike_seed33_s5_fps60_run2 (Editor) | 60 | +0.29 ms (+14 samples) | 0.92 / 2.58 ms | 0 |
| spike_seed33_s5_fps30 (Editor) | 33 of 64 | +0.12 ms (+6) on the 33 that paired | 1.19 / 3.92 ms on those 33 | 25 of the 54 non-landing clicks, 112 - 253 ms early (29 within 5 ms) |
| spike_seed33_s5_fps15 (Editor) | 0 of 64 | - | - | 54 of the 54 non-landing clicks, 59 - 256 ms early (0 within 5 ms) |
| spike_seed33_s5_standalone60 | 60 | -0.50 ms (-24 samples) | 0.56 / 2.52 ms | 0 |
| spike_seed33_s5_standalone60_run2 | 60 | +0.03 ms (+2) | 1.04 / 3.30 ms | 0 |
| spike_seed33_s5_standalone60_run3 | 60 | -0.14 ms (-6) | 0.96 / 3.34 ms | 0 |

- 60 fps (Editor and standalone) passes the attack criterion (mean <= 2 ms, max <= 5 ms) with a wide margin.
- 30 and 15 fps do not: the scheduler reports `started 64, late 0, dropped 0`, but the clicks that sound early were never reported.
  A click sounds at (about) the moment its instance is dispatched instead of at its DSP time: the analyser sees them 59 - 256 ms
  before the beat, by at most the lead the note was dispatched with (long lead: window of 0.5 beat; short lead: 0.3 beat, 167 ms at 108 BPM;
  at 15 fps the 0.3-beat clicks are 59 - 140 ms early because dispatch is up to a frame later than due).
  Counting: every 2.8 - 3.2 kHz onset of the WAV is matched to the nearest 1 kHz reference onset; the 10 onsets within 0.8 s of a jump-landing
  beat (16 / 32 / 48 / 64, where the music, and so the reference, is silent) are left out, which leaves 54 of 64.
  Cause (hypothesis, not tested): `FmodOneShotBackend` applies `ChannelGroup.setDelay` on the first main-thread `Update` after
  Studio created the group, and Studio un-pauses the voice on its own update (about 20 ms); with a frame longer than that the voice has
  already sounded. Widening the dispatch window does not help (it only dispatches earlier). R12 (never late) holds at 15 / 30 fps,
  "never early" does not. The kill chords use the same backend, so they are presumably affected the same way (not measured).
- Standalone offset: mean signed error -24 / +2 / -6 samples (mean -9) against the Editor's +8 / +14 (mean +11): a 20-sample difference,
  inside the 24-sample limit and inside the run-to-run spread (sd of one take is about 1.1 ms), so `DefaultScheduleOffsetSamples` stays -110
  for both.
- Standalone with the frame rate left unlimited (76,901 frames in 37.6 s = about 2050 fps, tiny window): 60 of 64 clicks dropped, probably because the backend gives
  a channel group 30 frames to appear and 30 frames last only about 14 - 15 ms there (not tested). A capped or vsynced build is fine; an uncapped one is not.

### Standalone build

`Builds/Capture/<scenario>/ChordPulseCapture.exe` (git-ignored), built from a temporary copy of `AudioCapture.unity` per scenario
(fields set in memory, `SaveScene(..., saveAsCopy: true)`; the copies and their .meta were deleted afterwards) with
`BuildPipeline.BuildPlayer`, scene list of that one scene, `BuildOptions.Development`, `EditorBuildSettings` and ProjectSettings untouched.
The player writes to `Docs/Captures` next to the exe (`Application.dataPath/..`), i.e. `Builds/Capture/<scenario>/Docs/Captures/`, and does
not quit by itself (the exe has to be closed after the `.cpu.txt` appears). Analyse the files from there.

## Task 5b: one-shots never early; channel-group wait in DSP time

`analyze_capture.py spike` now also classifies every test-band click against the beat it belongs to (a click up to 0.7
beat early still counts for its own beat): `early` (> 5 ms before the beat), `late` (> 5 ms after), `missing` (no click;
those within one beat after a jump landing are listed apart), and exits 1 if early or late > 0. The paired mean / max
statistics are unchanged. `CaptureScenario.logOneShots` (off by default) writes `<take>.oneshots.csv`: the backends'
`FmodOneShotBackend.DiagnosticLog` (per note: `start` with the DSP clock at `start()` and the scheduled start; `apply`
with the frames waited and the channel group's delay, pause flag and first channel's position at that moment; six
`check` lines after; `drop`) plus one `clock` line per frame (beat-clock anchor beat, its DSP time, the conductor's poll
count for it). With logging off the backend only tests one null delegate.

### Cause (confirmed)

Diagnosis take `spike_seed33_s5b_diag_fps15` (old backend + logging, 15 fps): every `apply` (setDelay) came one or two
frames (64-128 ms) after `start()`, and at that moment 59 of 64 instances had no channel left (the 5 ms click had already
played and ended), 3 were mid-play (position > 0), 2 at position 0. Each click was heard `lead at start() - about 21 ms`
early (beat 1: lead 106 ms, heard 85 ms early; beat 2: 255 / 235; beat 3: 150 / 129): the voice sounds at the first
Studio update after `start()`, and `setDelay` arrives after it. At 60 fps the frame (17 ms) is shorter than a Studio
update, so the delay usually won the race.

### Fix (commits 84d4d10, 0063da9)

- The instance is paused before `start()`. Studio then creates the channel group *and the voice* paused (every `apply`
  line of the fixed takes, 64 spike clicks per take and the 1629 kill-chord notes of `heavy_seed33_s5b_killsonly_fps15`:
  group paused, channel at position 0). `Update` applies `setDelay` / fade points, then unpauses the group directly
  (core call, immediate) and the instance (Studio bookkeeping). In every `check` line before the start time the channel
  position is 0 (3771 such checks for the 1629 kill notes at 15 fps): nothing sounds before the delay.
- Wait is in DSP time (`OneShotArming.Decide`, EditMode tests): wait while the start is at least one mixer block (DSP
  buffer, 1024 samples) ahead, apply when the group exists, otherwise drop (`DroppedCount`). The one-block margin comes
  from `spike_seed33_s5b_fps15` (on 84d4d10, margin 0): beat 51 was applied 798 samples before its start and never
  sounded, invisible to every counter.
- Placement accuracy of the backend itself: frame in the WAV of each test click minus its `setDelay` start (one constant
  capture-to-DSP offset per take) is within +-0.6 ms for every click of every take below (60, 30, 15 fps, standalone).

Rejected / not needed:
- Studio-only unpause (`spike_seed33_s5b_a_fps15`, `instance.setPaused(false)` after `setDelay`): early 0 and placement
  equally exact, but the unpause waits for Studio's next update (or a 0.2-0.8 s landing stall), which can come after the
  start: a note could then play late. Unpausing the group directly (`_a2_fps15`, then the commits) has no such window.
- `EVENT_PROPERTY.SCHEDULE_DELAY` as a guard (approach b) and other variants: not tried. The paused start already places
  every click within 0.6 ms of its `setDelay` time; what remains at 15 / 30 fps is in the start time itself (below), which
  no backend variant can correct.

### Results (Editor unless stated; seed 33, 68 beats, `shortLeadOnDsp`; every take `stalls 0`; attack = `spike` "after correction")

| take | code | fps | early | late | missing / dropped | attack mean / max (ms) | Studio / Core dsp avg % |
|---|---|---:|---:|---:|---|---:|---|
| spike_seed33_s5_fps60 (Task 5) | old | 60 | 0 | 0 | 0 / 0 | 0.91 / 2.77 | 6.84 / 1.72 |
| spike_seed33_s5_fps30 (Task 5) | old | 30 | 30 | 0 | 0 / 0 | (33 paired) | 7.50 / 1.57 |
| spike_seed33_s5_fps15 (Task 5) | old | 15 | 64 | 0 | 0 / 0 | (0 paired) | 7.61 / 1.72 |
| spike_seed33_s5_standalone (Task 5) | old | ~2050 | 1 | 0 | 59 / 60 dropped | (4 paired) | 5.24 / 0.94 |
| spike_seed33_s5b_diag_fps15 | old + log | 15 | 62 | 0 | 0 / 0 | (1 paired) | 7.93 / 1.54 |
| spike_seed33_s5b_a_fps15 | Studio unpause | 15 | 0 | 10 (+6..+7) | 0 / 0 | 1.99 / 6.14 | 6.87 / 1.54 |
| spike_seed33_s5b_a2_fps15 | group unpause | 15 | 0 | 12 (+6..+10) | 0 / 0 | 2.40 / 9.47 | 6.33 / 1.54 |
| spike_seed33_s5b_fps15 | 84d4d10 | 15 | 0 | 9 | 1 (beat 51, silent) / 0 | 2.12 / 6.49 | 6.39 / 1.48 |
| spike_seed33_s5b_c_fps60 | 0063da9 | 60 | 0 | 0 | 0 / 0 | 0.90 / 3.17 | 6.52 / 1.45 |
| spike_seed33_s5b_c_fps30 | 0063da9 | 30 | 0 | 2 (+5.0, +5.1) | 0 / 0 | 1.29 / 4.96 | 6.79 / 1.50 |
| spike_seed33_s5b_c_fps15 | 0063da9 | 15 | 0 | 8 (+5..+7) | 0 / 0 | 1.55 / 7.22 | 6.92 / 1.53 |
| spike_seed33_s5b_standalone_uncapped (Development player) | 0063da9 | ~2150 | 0 | 0 | 0 / 0 | 0.33 / 1.42 | 5.51 / 1.40 |

(`spike_seed33_s5b_fps60` / `_fps30` on 84d4d10: 0 / 0 / 0, 0.83 / 2.30 ms and 0 / 1 / 0, 1.40 / 5.27 ms.) Uncapped
standalone: 81,662 frames in about 38 s; 59 of the 64 clicks waited more than 30 frames for their channel group (up to
75 frames, at most one mixer block of DSP time), so the old 30-frame limit would have dropped them.

Heavy (kill chords on the same backend): `heavy_seed33_s5b_fps60` Studio 8.20 / Core 5.42 %, one-shots 1629 started,
late 0, dropped 0, cancelled 24 (Task 5 `_s5_fps60c`: 8.90 / 6.14 %, same counts); `heavy_seed33_s5b_fps15` 7.55 / 5.56 %,
1629 / 0 / 0 / 0; `heavy_seed7_s5b_fps60` (128 beats) 7.31 / 5.47 %, 3069 / 0 / 0 / 0. No landing drops in any take.

### Still failing at 15 and 30 fps: the beat clock, not the backend

The late clicks at 15 / 30 fps are placed exactly on their `setDelay` start (+-0.6 ms); the start itself is 6-10 ms late.
Compared with the reference click of the same beat, the beat clock's anchor (`MusicConductor.PollAnchor`, running mean of
the main-thread polls within half a beat) is about +1 to +2 ms for most beats but +7 to +9 ms for about 10 of 64 beats
at 15 fps, where only 3-4 polls go into each anchor (14-17 at 60 fps, 6-8 at 30 fps); a click scheduled from such an
anchor is late by that much. This needs a change in `MusicConductor` (out of scope here), so the 15 fps line of the
attack criterion (max <= 5 ms, late 0) is not met, and the 30 fps line only marginally (2 clicks at +5.0 / +5.1 ms raw).

## Task 6: return-bus tail after `StopMusic()`

`SoundManager.StopMusic()` now mutes `TailBuses` (`bus:/Feature Duck`; a bus the banks lack is ignored) and `StartMusic()`
unmutes them after its internal `StopMusic()`, before the music starts. `CaptureScenario` got `stopTailSeconds` (after the
last beat: `StopMusic()`, keep recording that long; the capture frame of the call is written to `.cpu.txt` as
`STOP frame N`) and `restartAfterStop` (after the tail: `StartMusic()` again, 8 more beats with the same kills, beat log in
`<take>.beats2.csv`; `.cpu.txt` gets `RESTART frame M`). Measure with
`python FMOD/Tools/analyze_capture.py tail <wav>`: RMS of [stop, +100 ms] and [+100 ms, end of the tail], exit 1 above
-80 dBFS; with a `.beats2.csv` it also compares the first 4 beats of start and restart (limit 1 dB; run-to-run spread of
same-code 4-beat windows is about 0.5 dB, so the plan's 0.3 dB is relaxed).

Stop point: the feature loops of seed 7 (11 "Power Chord Hits" at beats 32-47, 2 "Trumpet Rise" at 96-111) sound only in
the last bar of their phrase, so `beats` 46 / 110 / 111 stop inside a loop (beats 40 and 104, tried first, stop before it).
All takes 2 s tail, 0 stalls.

| take (before the fix) | [stop, +100 ms] | [+100 ms, end] |
|---|---:|---:|
| baseline beats 40 (`_s6_before`) | -38.5 | silent |
| baseline beats 104 (`_s6_before_b104`) | -63.7 | silent |
| baseline beats 46 (`_s6_before_b46`) | -38.8 | -90.5 |
| heavy beats 46 (`_s6_before_b46`) | -35.9 | -93.4 |
| heavy beats 110 (`_s6_before_b110`) | -36.2 | -83.3 |
| heavy beats 111 (`_s6_before_b111`) | -39.0 | -83.1 |

**The problem does not reproduce at the -80 dBFS line.** Before the fix there is a decaying tail, but it is quiet enough
that the mean over the rest of the tail stays below -80 dBFS in every take, so the "before" take does not fail the
criterion. First 100 ms after `STOP frame`, heavy beats 111 (dBFS):

| window | before (`_s6_before_b111`) | after (`_s6_after_b111_r2`) |
|---|---:|---:|
| 0-10 ms | -31.6 | -31.3 |
| 10-20 ms | -33.1 | -33.2 |
| 20-30 ms | -41.7 | -42.7 |
| 30-40 ms | -49.0 | -51.1 |
| 40-50 ms | -52.3 | -59.4 |
| 50-60 ms | -61.4 | silent |
| 60-70 ms | -64.0 | silent |
| 70-80 ms | -63.2 | silent |
| 80-90 ms | -63.4 | silent |
| 90-100 ms | -66.3 | silent |
| **0-50 ms** | -36.0 | -35.9 |
| **50-100 ms** | -63.4 | silent |

In both takes the output keeps decaying for roughly the first 50 ms after `StopMusic()`: the stop and the bus mute do not
act instantly (probably they take effect on FMOD's next update, with a short ramp; not measured), so this window is not a
measure of the music. After the fix the output is silent from about 50 ms on. Before the fix the return's tail continued
(-63 to -75 dBFS in 100 ms steps, -86.3 at +300 ms) and was silent by +1 s. The fix removes a real but short, faint
tail; the numeric -80 dBFS criterion cannot tell before from after.

`restartAfterStop` re-applies only the baseline/heavy scenario state (kills, Combo, Tension); it is not valid for
`killsOnly`, `styles`, `duck` or `holdArrangement` takes.

Restart check (`heavy_seed7_s6_after_b111_r2`): first 4 beats at start -30.09 dBFS, first 4 beats after restart
-30.12 dBFS, difference -0.03 dB (limit 1 dB), so the unmute works and the return is audible again. (A first take,
`_s6_after_b111`, restarted without the kills and Combo/Tension of the heavy scenario and read -6.6 dB; the scenario was
fixed, not the SoundManager, and that take only shows the bug of the capture code.)

## Task 7: global mixer buses and global Tension / Danger / Hurt (`FMOD/Tools/mixer.py`)

`mixer.py` writes `mixer.js` (run after `instruments.js`, before `kill_oneshots.js`; Docs/AdaptiveAudioPlan.md 8-10). It
owns the global buses, the returns, the ducking and the global mix parameters; `kill_oneshots.js` now only removes the old
timeline kill chords. Built structure (signal order, every effect before the fader):

```
bus:/Music Bed            [low-pass (Danger), 3-EQ low band (Hurt), gain (Hurt)] 0 dB   <- music event output
  bus:/Music Bed/Harmony  [compressor keyed by bus:/Feature -32 dB 4:1 5/180 ms,
                           compressor keyed by bus:/Kill Chords -20 dB 2:1 5/60 ms] -8 dB
    return:/Harmony Duck  (no effects, 0 dB) <- pre-fader send of the event's Harmony group (MusicMix gain, send, fader -80)
bus:/Feature              [key, compressor keyed by bus:/Kill Chords, reverb, limiter] -8 dB
  return:/Feature Duck    (no effects, 0 dB) <- pre-fader send of the event's Feature group (send, fader -80)
bus:/Kill Chords          [key, compressor, limiter] -8 dB (unchanged, same GUIDs)
```

Parameters Tension, Danger, Hurt are now global (same presets and GUIDs, same seek speeds); LegacyLayers (discrete,
global, 0/1) is new. The -8 dB of the event master moved from the returns to the buses after them, so every compressor sees
the same input level as before. Differences from the plan's brief: two Harmony compressors instead of one compressor with
both keys (one compressor would have changed the Feature duck from -32 dB 4:1 180 ms to the kill duck's -20 dB 2:1 60 ms);
the event's Harmony group keeps its MusicMix gain (MusicMix stays an event parameter until it moves to a bus volume).

### Feasibility (on a copy of the project, FMOD Studio 2.02.35 CLI, then its bank loaded in a separate NRT Studio system in the Editor)

- (a) A return's `output` set by script to a group bus: accepted (`return.output = bus`), path of the bus
  `bus:/Music Bed/Harmony`. At runtime the returns are no longer found as `bus:/Harmony Duck` / `bus:/Feature Duck`
  (`ERR_EVENT_NOTFOUND`: a return is addressed below the bus it feeds), so `SoundManager.TailBuses` and the `duck` scenario
  now use `bus:/Feature` and `bus:/Music Bed/Harmony`.
- (b) `GameParameter.isGlobal = true` on the event-local presets keeps every automation curve (4 Tension curves on the
  event tracks, points unchanged) and the event's proxy; the bank builds. At runtime the event lists them as
  `READONLY, GLOBAL`: `EventInstance.setParameterByName("Tension")` returns `ERR_INVALID_PARAM`, so
  `SoundManager.SetMusicParameter` now writes Tension / Danger / Hurt to the Studio system when the event rejects them
  (old banks keep the event write). Reading the final value through the event still works (`getParameterByName` final
  0.6 after a system write of 0.6); Hurt's snap-to-1-then-seek still works (1 -> 0.59 after 256 ms at 1.6/s).
- Bus -> bus sidechain between global group buses (the README's earlier negative result was bus -> in-event compressor):
  works at runtime. Both Harmony compressors and the Feature compressor have a `SEND_SIDECHAIN` input. Metered in the NRT
  system: kill-keyed compressor out - in 0.00 dB with music only, mean -7.1 dB (input meter includes the key) with loud
  Marimba one-shots on bus:/Kill Chords, back to -0.1 dB after; Feature-keyed compressor 0.00 dB with FeatureLoop 0 and
  mean -2.0 / min -10 dB with FeatureLoop 1.
- Discrete global parameter: Studio's maximum is one past the last value. LegacyLayers built with maximum 1 reported
  maximum 0 at runtime and clamped a write of 1 to 0; with maximum 2 it takes 0 and 1. `mixer.py` uses 0..2.
- Running the full generator order on the copy (`feature_loops`, `bed_tension`, `bed_variation`, `bed_combo`, `bed_style`,
  `instruments`, `mixer`, `kill_oneshots`, `mixer`): `mixer.js` removed the 6 effects the older scripts re-create in the
  event (Music Bed Danger / Hurt, Feature reverb / key / limiter), re-added the Feature send, and the final run logged
  `created 0, updated 0, added 0, removed 0`. `feature_loops.py` now tolerates the missing in-event Harmony compressor.

Idempotence on the real project: second `mixer.js` run `created 0, updated 0, added 0, removed 0`, `kill_oneshots.js`
"nothing to do", `instruments.js` `moved 0, updated 0, added 0, removed 0`, Metadata byte-identical to the first run.

### Before / after (Editor, seed 7, 128 beats, every take `stalls >100 ms after the first beat 0`)

| take | Studio update avg / max | Core dsp avg / max | one-shots started / late / dropped / cancelled |
|---|---:|---:|---|
| baseline_seed7_s7_before | 6.06 / 81.75 | 1.69 / 2.65 | 288 / 0 / 0 / 0 |
| baseline_seed7_s7_after | 6.08 / 80.27 | 1.71 / 2.80 | 288 / 0 / 0 / 0 |
| heavy_seed7_s7_before | 6.82 / 83.32 | 5.37 / 6.88 | 3069 / 0 / 0 / 0 |
| heavy_seed7_s7_after | 7.73 / 81.60 | 5.37 / 6.85 | 3045 / 0 / 0 / 0 |
| duck_seed7_s7_before | 8.03 / 81.52 | 1.76 / 2.87 | 288 / 0 / 9 / 0 |
| duck_seed7_s7_after | 7.51 / 81.45 | 1.75 / 2.86 | 288 / 0 / 0 / 0 |
| duck_seed7_s7_before_nokills | 8.20 / 86.47 | 0.93 / 1.87 | 0 |
| duck_seed7_s7_after_nokills | 8.15 / 87.08 | 0.86 / 1.50 | 0 |

`rms --compare` (after vs before, 4-beat windows): baseline max |diff| 0.17 dB, heavy max 0.69 dB (window 24, beat 96,
flagged `?`), duck 0.16 dB, duck nokills 0.53 dB; no window over 1 dB. heavy's Tension 0.8 write was accepted (`.params.csv`).

Ducking (`duck` vs `duck ... nokills`, least-squares gain per 20 ms bin over the 375 ms after each chord beat, chords on
beat 4k+1, segments aligned by cross-correlation): median of the per-chord means -3.56 dB before, -3.27 dB after (mean over
all bins -3.76 / -3.26 dB; the landing chords 49 and 81 and a few outliers are in those means). The `duck` scenario now
meters the kill-keyed compressor of bus:/Music Bed/Harmony and mutes bus:/Feature.

Not explained: heavy after started 24 fewer one-shots (3045) than before (3069, also the Task 5b count) with 0 late /
dropped / cancelled in both; levels agree within 0.7 dB. The 9 backend drops in `duck_seed7_s7_before` are the known
jump-landing drops.

## Task 8: `SoundManager` on the global mix (bus volumes, global parameters)

`SoundManager` now writes `Tension` / `Danger` / `Hurt` straight to `RuntimeManager.StudioSystem` (no event attempt first),
sets the discrete global `LegacyLayers` (inspector field `legacyLayers`, default true: no layer is a one-shot yet; one warning
if the bank lacks it) in `StartMusic` before the music starts, and applies `MusicMix` as `setVolume(MusicMixGain(musicMix))`
on `bus:/Music Bed` and `bus:/Feature`. The event's `MusicMix` parameter is no longer written. `HeardTension` /
`HeardDanger` / `HeardHurt` still read the event's `getParameterByName(...)` final value: checked in play mode with
`forceDanger` + `hurtEveryBeats` 4, `HeardDanger` 1.000 and `HeardHurt` 0.625 in mid-recovery. `CaptureScenario` got
`musicMix`, `forceDanger`, `hurtEveryBeats`. No FMOD project change, no bank rebuild (`git status FMOD/ChordPulse` is clean);
only comments of generator files changed (below).

### The MusicMix curve and what it covers

Read from `FMOD/ChordPulse/Metadata/Event/{3d468f15-...}.xml`: the parameter preset `MusicMix` (initial value 1, range 0..1)
is used by exactly two automation curves of the music event, both with the same 5 linear points (dB at 0 / .25 / .5 / .75 / 1):
**-80 / -18 / -9 / -3.5 / 0** - the `volume` of the Music Bed group and the `gain` effect of the Harmony group (added by
`mixer.py`, a copy of the first). `MusicMixGain` reproduces it (linear in dB, 0 = silent), the same table as `KillChordGain`.

| signal | before (event parameter) | now (bus volume) |
|---|---|---|
| Music Bed tracks (drums, bass, tension, combo, fever, styles ...) | Music Bed group volume | `bus:/Music Bed` (the event's output) |
| Harmony tracks (pluck, pads, keys ...) | Harmony group gain | `bus:/Music Bed/Harmony`, a child of `bus:/Music Bed` |
| Feature (guest loops) | **not attenuated**: the Feature group goes to the event master, no MusicMix automation | `bus:/Feature`: **now attenuated** (the brief's mapping; at `musicMix` 1 nothing changes) |
| Spike ref (silent test track) | not attenuated | `bus:/Music Bed` (silent anyway) |
| Kill chords | `bus:/Kill Chords` volume (`KillChordMix`) | unchanged |

No double or missing attenuation: the event parameter now sits at its initial value 1, where both curves are 0 dB, so the
event groups are neutral and the bus volume is the only attenuation. `Bus.setVolume` is a scale on top of the fader authored
in Studio (-8 dB on `bus:/Feature` stays): `musicMix` 1 gives volume 1.0 and the levels below match Task 7's takes, which
would be +8 dB on the Feature windows if setVolume replaced the fader.

RED/GREEN: `MixGainTests` (7 cases: end points 0 -> 0 and 1 -> 1.0, table points, linear-in-dB interior, monotonic over 100
steps, `KillChordGain` unchanged) first failed to compile (`MusicMixGain` missing, `KillChordGain` private), then
EditMode 87 / 87 passed (80 before).

### Levels against Task 7's after-takes (seed 7, 128 beats, `rms --compare`, 4-beat windows)

| take | max abs diff (dB) | window | Studio update avg / max | Core dsp avg / max | one-shots started |
|---|---:|---|---:|---:|---:|
| baseline_seed7_s8_after vs s7_after | 0.19 | - | 6.36 / 79.92 | 1.70 / 2.71 | 288 |
| heavy_seed7_s8_after vs s7_after | 0.86 | beat 96 (flagged `?`, also Task 7's worst) | 7.65 / 86.04 | 5.38 / 7.00 | 3069 |
| duck_seed7_s8_after vs s7_after | 0.91 | beat 112 (flagged `?`) | 9.06 / 88.05 | 1.74 / 2.98 | 288 |
| duck_seed7_s8_after_nokills vs s7_after_nokills | 0.60 | beats 80, 112 (flagged `?`) | 7.67 / 80.35 | 0.81 / 1.42 | 0 |

All takes 0 stalls after the first beat, 0 dropped frames, late 0 / dropped 0 / cancelled 0. No window over 1 dB.
Budgets: Studio update avg baseline 6.36 % (limit 11), Core dsp avg baseline 1.70 % (6), heavy 5.38 % (12).

Ducking (`python FMOD/Tools/analyze_capture.py duck <duck> --compare <nokills>`, the Task 7 method now in the script: least
squares gain per 20 ms bin over the 375 ms after the chord beat 4k+1, cross-correlation alignment, bins silent in the no-kill
take skipped): median of the per-chord means **-3.59 dB** (30 chords; the same script gives -3.34 for `s7_after` and -3.51
for `s7_before`; Task 7's README: -3.27 / -3.56 with a slightly different bin filter). In range -3.5 +- 1.

### MusicMix 0.5 against 1 (the curve is applied once)

- `duck ... nokills` (Harmony only) at `musicMix` 0.5 vs 1 (`..._s8_after_nokills_mix05` vs `..._s8_after_nokills`): 28
  4-beat windows, **median -9.00 dB** (mean -9.00, range -9.32 .. -8.56). Expected: the table's dB at 0.5 = -9.0; a double
  attenuation would read -18.
- `baseline` 64 beats at 0.5 vs 1 (`baseline_seed7_s8_b64_mix05` vs `_s8_b64`), full bed with the kill chords: the chord
  energy is taken from `baseline_seed7_s8_b64_killsonly` (MusicMix 0) and removed per window (power subtraction): bed
  gain **median -9.00 dB** over 13 windows (-8.66 .. -9.75), including the Feature windows (beats 32-47: -9.00, -8.97, -8.66).
  The total window level moves only -3.2 .. -3.9 dB because the chords (-38 dBFS alone vs -34 dBFS total) are unchanged.
- `styles` (MusicMix 0 forced) and `killsOnly` still play the chords alone: `styles_seed7_s8` -32.0 dBFS mean over the 4-beat
  windows, `baseline_seed7_s8_b64_killsonly` -38 dBFS (chords on every 4th beat). `styles_seed7_s8` against `styles_seed7_finalfix`
  differs by up to 2.4 dB, but two finalfix takes of the same code differ by 2.2 dB (random kill voicing), so that
  comparison says only "same level class". `duck` works (above); its Feature mute is `bus:/Feature`.

### Danger and Hurt on the global buses (baseline seed 7, 64 beats; no pre-Task 7 recording exists, so absolute expectations)

Takes: `baseline_seed7_s8_b64` (neither), `..._s8_b64_danger` (`forceDanger`), `..._s8_b64_hurt8` (`hurtEveryBeats` 8). 0 stalls;
`.params.csv`: Danger 1 at beat 0; Hurt 1, 0 at beats 7.99, 15.97, 24.00 ... Bands via
`analyze_capture.py bands <take> --compare <neither>` (4th-order Butterworth, mono, take minus reference).

Danger, authored `DANGER_CUTOFF` (bed_tension.py): Danger 0 -> 20 kHz, 0.4 -> 5 kHz, **1 -> 900 Hz** low-pass on `bus:/Music Bed`
(seek 3/s up, so settled after 0.33 s).

| region (median over the spans from beat 8, dB) | <300 Hz | 300-2k | >2k |
|---|---:|---:|---:|
| 4-beat windows (13), kill chord on beat 4k+1 inside | 0.00 | -0.02 | -0.52 |
| bed alone: beat k+2.5 .. k+4 of every 4th beat (the chord is over) | -0.01 | -0.25 | **-42.05** |
| kill chord ringing: beat k+1.0 .. k+1.8 | 0.03 | 0.03 | -0.23 |
| kill chord attack: first 80 ms of beat 4k+1 | 0.01 | 0.07 | **-0.51** |

Verdict **Danger: as expected.** Above 2 kHz the bed falls by 42 dB (steep), below 500 Hz it is within 0.01 dB. The 4-beat
windows show only -0.5 dB above 2 kHz because the kill chords dominate that band and are not on the Music Bed bus: the chord
attack is within 0.5 dB of the baseline's, i.e. not low-passed. (300-2k -0.25 dB: the bed's content there is mostly below 900 Hz.)

Hurt, authored (bed_combo.py): 3-EQ low band (crossover 300 Hz) 0 -> 0 dB, 0.3 -> -8, **1 -> -24 dB**; gain 0 -> 0, **1 -> -4 dB**;
snapped to 1 then seeks down at 1.6/s (0.625 s). Median over the hits at beats 8, 16, 24, 32, 40, 56 (beat 48 is a timeline
jump landing with different content), 50 ms windows after the hit beat, take minus neither:

| after the hit (ms) | 0 | 50 | 100 | 150 | 200 | 250 | 300 | 350 | 400 | 450 | 500 | 550 | 600 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| <300 Hz | -6.5 | **-23.4** | -21.1 | -19.4 | -17.6 | -16.4 | -13.7 | -12.4 | -9.8 | -7.8 | -4.6 | -0.3 | -0.3 |
| 300-2k | -2.0 | -5.2 | -4.5 | -5.8 | -4.8 | -5.3 | -4.6 | -3.3 | -2.4 | -2.0 | -1.4 | 0.0 | 0.0 |
| >2k (gain only) | -0.1 | **-3.5** | -3.2 | -2.9 | -2.3 | -2.0 | 0.0 | +2.5 | -1.2 | -1.0 | -0.7 | 0.1 | 0.0 |

Verdict **Hurt: as expected.** Lows are down 23.4 dB 50 ms after the hit (authored -24), the gain part -3.5 dB (authored -4; the
>2k cells at 300-350 ms are disturbed by the kill chord of beat 8k+1 at about +460 ms and by the jitter of the beat log), and the
recovery follows the authored curve: predicted lows for Hurt = 1 - 1.6 t are -21 / -18 / -16 / -12 / -9 / -4 dB at 50 / 150 / 250 /
350 / 450 / 550 ms, observed -23 / -19 / -16 / -12 / -8 / -0.3 dB, complete at about 550 ms (authored 625 ms). The first 50 ms are partly
before the parameter took effect (one frame plus FMOD's update and mixer block).

Signal-order change from Task 7 (recorded here): Danger and Hurt now act on `bus:/Music Bed` after the event master's limiter and
-8 dB fader; they used to act inside the event before them. The silent `Spike ref` test track also passes through the Music Bed
bus processing now.

### Stop tail with the new `TailBuses` (`heavy_seed7_s8_after_b111`: heavy, 111 beats, 2 s tail, restart)

`TAIL PASS`: [stop, +100 ms] -38.8 dBFS, [+100 ms, end] silent. In 10 ms steps: -30.7, -33.8, -43.8 dBFS, then silent from
30 ms on (Task 6's after-take: silent from about 50 ms). Restart: first 4 beats -30.06 dBFS at start, -30.29 dBFS after restart,
-0.23 dB (limit 1 dB). 0 stalls, Studio update avg 7.22 %, Core dsp avg 5.29 %, cancelled 48 (the stop). `TailBuses` keeps
`bus:/Music Bed/Harmony`; this take is silent with both muted, but it does not prove Harmony is needed (the bus has no
effect with a tail of its own); the comment in `SoundManager` says so.

### Small fixes

`CaptureScenario` duck comment (bus:/Feature carries the Feature group and the Feature sidechain key); `instruments.py`
docstring run order (instruments.js -> mixer.js -> kill_oneshots.js; `instruments.js` regenerated unchanged);
`feature_loops.py` comment and log line (mixer.js sets up the Feature ducking; `feature_loops.js` differs only in that
comment and log string, no FMOD run); `mixer.py` docstring (the buses are owned by the script, hand edits of their effect chains
are discarded; `mixer.js` unchanged). `analyze_capture.py` got `bands` and `duck`.

## Task 9: pattern table + bed pitched-layer data (nothing scheduled yet)

New generators: `FMOD/Tools/patterns.py` (Inst / Layer / Clip / N, C# generation, `register(lib)`, `--compare-legacy`) and
`FMOD/Tools/pattern_bed.py` (data). `python -I FMOD/Tools/instruments.py` writes `Assets/Scripts/Audio/Generated/PatternTable.g.cs`:
13 layers, 20 clips, 404 notes, and registers 15 new (instrument, bus) events (106 sounds). `PatternClip` has one field the
brief did not list, **`SectionPhrase`** (0 any, 1 = bars 1-4 of a section, 2 = bars 5-8): the original off-beat pluck (Pluck A,
hand-made, never generated) is an 8-bar pattern, so it is two clips. Bass A (also hand-made, regular) is data too. Every other
migrated track is the same in both phrases and in all four sections apart from tempo (`--compare-legacy` prints it).
`bed_notes.py` reads the pattern data; `BedNotes.g.cs` is byte-identical. `measure_sample.py` reproduces `samples.py`
(150 ms window: 22 entries within 0.05 dB, onsets exact).

Nothing audible changes: no Unity code schedules the patterns, the timeline layers still play (`legacyLayers` true), and the
new buses carry no signal.

### Buses (`mixer.py` SUB_BUSES): one-shot path = timeline track path

| layer (old track) | event group | one-shot bus | sub-bus processing |
|---|---|---|---|
| Bass A/B/C, Latin, Funk bass (`Bass - C Am F G`) | Music Bed | `Music Bed/Layers/Bass` | gain automated by global `Tension` (bass crossfade curve) |
| Tension strings | Music Bed | `Music Bed/Layers/Tension Strings` | HP 140 Hz, gain by `Tension` |
| Tension sub | Music Bed | `Music Bed/Layers/Tension Sub` | gain by `Tension` |
| Tension acid bass | Music Bed | `Music Bed/Layers/Tension Acid` | LP 3500 Hz, gain by `Tension` |
| Dreamy sub | Music Bed | `Music Bed/Layers` (OutputBus.MusicBed) | - |
| Offbeat pluck, Combo bells | Harmony | `Music Bed/Harmony` | - |
| Combo pad / Latin marimba / Funk keys / Funk guitar / Dreamy choir / Dreamy bells | Harmony | `Music Bed/Harmony/<name>` | HP 300 / 200 / 250 / 180 / 150 / 300 Hz |

`Music Bed/Layers` = the music event master: limiter (ceiling -1 dB, release 30 ms, from the event XML) and fader -8 dB, then
`bus:/Music Bed` (Danger / Hurt). Harmony layers get `bus:/Music Bed/Harmony`'s two keyed compressors and -8 dB. Tension
curves (`bed_tension.CURVES`) sit on a gain effect bound to the global `Tension`. `OutputBus.MusicBed` is now
`bus:/Music Bed/Layers` (no code used it; `SoundManager` uses literal bus paths). While layers are split between the
timeline and one-shots, two limiters act in parallel (event master on the timeline part, Layers on the one-shots) instead of
one on the sum. ADSR: instruments of tracks without ADSR are registered with attack 0 / release 0 (`ReleaseSeconds` 0).

### `--compare-legacy` (all four sections, both phrases; timeline notes read from the event XML)

The old generators' `build()` notes (2912) are all on the timeline; the remaining 192 are the hand-made Bass A (64) and
Pluck A (128). Every timeline note of the 13 migrated tracks (776 per section, 3104 in all) pairs with a table note on
(track, start, sample, pitch, volume, conditions, length, ADSR): **unexplained 0**. Per section (108 BPM): exact 32,
volume split (NORMALIZE_DB - rms + GainDb) 744, onset rule 600, ADSR as its own event 296, no ADSR -> 0/0 480. Clamped notes
0. Max Instances (132 BPM, + 4): 5-10 per event.

Onsets now applied on every note (sample onset / pitch ratio) against the old start shift; differences over 5 ms:

| clip | old shift | new onset | difference |
|---|---|---|---|
| Combo pad (GlassPad, onset 116 ms) | 0 | 36-65 ms | +36 .. +65 ms (pad had no shift; 300 ms attack) |
| Tension strings, phrase's first bar | 0 (other bars already shifted) | 30-48 ms | +30 .. +48 ms |
| Dreamy choir, phrase's first bar | 0 | 47-71 ms | +47 .. +71 ms |
| Dreamy choir, other bars | 60 ms fixed | 53-75 ms | -6.8 .. +15.2 ms (MIDI 65, 59, 60) |
| Funk guitar | 25 ms fixed | 17-22 ms | -6.3, -8.3 ms (MIDI 67, 69) |
| Latin marimba | 0 | 4.5-5.9 ms | +5.0 .. +5.9 ms (MIDI 67, 69, 71, 76, 77) |

### No audible change (Editor, seed 7, 128 beats, `rms --compare` against Task 8's after-takes, 4-beat windows)

| take | max abs diff (dB) | Studio update avg / max | Core dsp avg / max | one-shots started |
|---|---:|---:|---:|---:|
| baseline_seed7_s9_after vs s8_after | 0.14 | 5.93 / 78.14 (was 6.36 / 79.92) | 1.68 / 2.65 (was 1.70 / 2.71) | 288 |
| heavy_seed7_s9_after vs s8_after | 0.33 (beat 116) | 6.84 / 80.55 (was 7.65 / 86.04) | 5.44 / 8.35 (was 5.38 / 7.00) | 3069 |

Both takes 0 stalls after the first beat, late 0 / dropped 0 / cancelled 0. The idle events and buses cost no measurable CPU.
Bank `ChordPulse.bank` 6,352,096 -> 6,416,320 bytes (strings 2,410 -> 3,680); no new audio files (all samples were in the
bank already).

## Task 10: `FmodOneShotBackend` arming hardening

The backend now unpauses the instance first and then its channel group (the other order left a window in which a Studio
update between the two calls could re-assert the instance's pause on the group); `setPaused(true)` before `start()` and
`ChannelGroup.setDelay` are checked, and a note whose pause or delay failed is stopped, released and counted in
`DroppedCount` instead of starting unpaused / un-delayed; a comment at the arming point notes that "group exists" does
not mean the voice exists yet (the parent group's delay still gates a channel created later). No other behaviour change.

Takes (Editor, `spike`, seed 33, 68 beats, `shortLeadOnDsp`, every take `stalls 0`, scheduled / started 64, late 0,
dropped 0):

| take | fps | early | late | missing (landings / other) | attack mean / max (ms) |
|---|---:|---:|---:|---|---:|
| spike_seed33_s10_fps60 | 60 | 0 | 0 | 0 / 0 | 0.81 / 2.33 |
| spike_seed33_s10_fps15 | 15 | 0 | 7 (+5..+7) | 0 / 0 | 1.30 / 7.23 |

Task 5b `_c_` takes for comparison: 60 fps 0 / 0 / 0.90 / 3.17; 15 fps 0 early, 8 late (+5..+7). Same as before: the 15 fps
late clicks are the beat-clock limitation described under Task 5b; nothing early, nothing missing.

## Task 11: bed pitched layers as scheduled one-shots (legacy tracks gated)

The 13 migrated timeline tracks (`patterns.LEGACY_TRACKS`: `Bass - C Am F G` (also the Latin / Funk bass lines),
`Offbeat pluck`, `Combo pad`, `Combo bells`, `Tension strings`, `Tension sub`, `Tension acid bass`, `Latin marimba`,
`Funk keys`, `Funk guitar`, `Dreamy choir`, `Dreamy bells`, `Dreamy sub`) only play while the global `LegacyLayers` is 1
(`FMOD/Tools/legacy_gate.py` -> `legacy_gate.js`, 3104 notes, each keeps its other conditions). `SoundManager.legacyLayers`
(read in `StartMusic`) picks the path: true = `LegacyLayers` 1 and the `PatternScheduler` silent (`ArrangementState.Legacy`),
false = `LegacyLayers` 0 and the pattern layers are Unity one-shots. **The default stays true** (see "Verdict").
Drums, feature loops, fever and `Spike ref` stay on the timeline; the music-event parameter writes are unchanged.
On the new path these rules take effect: onset (notes start `OnsetSeconds` early, Task 9 table), 0/0 release for tracks
without ADSR, parallel limiters (event master on the timeline part, `Music Bed/Layers` on the one-shots), no 0.25 s
parameter lead (below) and the stop fade (below).

### Gate condition (measured on a copy of the project, bank rendered offline with WAVWRITER_NRT)

A test event with one 5 ms click per candidate condition on the discrete `LegacyLayers` (Studio max 2), rendered at runtime
value 0 and 1. A condition [lo, hi] matches the values v with ceil(lo) <= v <= ceil(hi):

| condition | value 0 | value 1 |
|---|---|---|
| [0.5, 1] (brief), [1, 1], [0.6, 1.4], [1, 2], [0.5, 1.5], [0.9, 1.1] | silent | plays |
| [1.1, 1.9], [1.5, 2], [1.4, 2] | silent | silent |
| [0, 0.4], [-0.4, 0.4], [0, 1] | plays | plays |
| [0, 0] | plays | silent |

Chosen: **[0.5, 1]**. The gated copy rendered with `LegacyLayers` 1 matched the ungated bank per bar within 0.01 dB
(sample-level differences come from the rebuild itself: an ungated rebuild differs the same way); with 0 the bed lost the
13 tracks (-6.5 to -7.1 dB per bar, drums left). On the real project: added 3104, second run "unchanged 3104" and
Metadata byte-identical, only `ParameterCondition` objects added (no GUID removed or changed). `patterns.py --compare-legacy`
now requires the gate on every migrated note: unexplained 0.

Step 1 (legacy path unchanged; `baseline_seed7_s11_legacy_gate` vs Task 9's `baseline_seed7_s9_after`): max 0.135 dB.

### Arrangement (`ArrangementTimeline`, SoundManager)

`SoundManager` implements `IArrangement.At(step)` = `ArrangementTimeline.At(step, inputs)`: the decisions it already makes
(current / next phrase style, variation, breath, feature loop, SectionPhrase; the game's intensity; combo latched up to
`_comboBar`, the game's level after it; fever start) as step functions without the parameter lead. Fever zone (build-up bar
to fever end) = style 0, variation 0, combo 0; intensity 3 in fever, 0 in breath phrases. SectionPhrase: 1 when a phrase
starts a run in its section or follows that section's second phrase, 2 when the same section continues after its first
phrase (seed 7 sections 0,0,1,0,0,0,1,1 -> 1,2,1,1,2,1,1,2: 16 plays on, 32 section 1 start, 48 back to section 0's start,
64 plays on, 80 loops back, 96 jumps, 112 plays on; the seed 33 spike timeline log starts every phrase at its section's
start). Each frame, after `UpdatePhrase` / `UpdateFever`, the inputs are compared with the last frame's over now .. now +
lookahead (1.5 beats) and `PatternScheduler.Invalidate` runs from the first changed step (combo / hurt drop: the next bar
line; intensity and breath: their step; style / variation: the boundary; fever: its build-up bar, `QueueFever` checks at
once). Notes within about 125 ms of now keep their state (Invalidate's onset safety).

Intended difference: the old path set parameters 0.25 s ahead, so the last ~0.25 s of a phrase already followed the next
phrase's state; the new path switches exactly on the step (in the solos the last window of a phrase reads up to +0.8 dB).

### Stop fade (fixed in this task)

Harmony solo takes showed the one-shots ringing 0.25-0.5 s past each note's end (combo bell MIDI 81 after its end at beat
8.4: +27 dB at beats 8.5-8.75). An offline render of the real banks shows the timeline cuts its notes at the region end
(bell -18 dB within 28 ms) instead of playing the ADSR release that Task 9 put into `InstrumentTable.ReleaseSeconds`.
Pattern one-shots now end with a 20 ms fade (`SoundManager.PatternStopFadeSeconds`); kill chords keep their release. The
experiment take `heavy_seed7_s11_i3_soloharmony_new_exp_rel20ms` (same change, uncommitted then) moved the worst harmony
windows from +1.25 / +1.41 / +1.26 dB to +0.33 / +0.46 / +0.46 dB.

### Acceptance (Editor, every take `stalls >100 ms after the first beat 0`; legacy then new in the same session)

| scenario | legacy take | new take (13ea929 + 1f35f46) | max window diff | windows > 1 dB |
|---|---|---|---:|---|
| baseline seed 7 | `baseline_seed7_s11_legacy` | `baseline_seed7_s11_new2` | 1.02 | window 0 (-1.02, first beat) |
| heavy seed 7 | `heavy_seed7_s11_legacy` | `heavy_seed7_s11_new3` | 1.02 | window 0 (-1.02, first beat) |
| heavy seed 33 | `heavy_seed33_s11_legacy` | `heavy_seed33_s11_new2` | 0.79 | - |
| styles (`stylesWithBed`, intensity 3, 256 beats) | `styles_seed7_s11_bed_i3_legacy` | `styles_seed7_s11_bed_i3_new2` | 0.72 | - |

(`heavy_seed7_s11_new2` had 2 stalls and is not used.)

Per-family solos (`soloLayers`, heavy seed 7, intensity 3):
- **bedWithoutHarmony** (Kill Chords, Feature and Music Bed/Harmony muted: timeline drums + bass / tension / dreamy sub on
  both paths): max 2.60 dB, only window 0 (-2.60) over 1 dB; median per-beat difference +0.48 dB (parallel limiters: the
  drums no longer share the event limiter with the bass and tension layers).
- **harmony** (also the event's non-Harmony Music Bed tracks and Music Bed/Layers muted; the kill-keyed duck is off because
  its key is muted, on both paths): window 0 -3.71 (the first bar's pad chord is a beat-0 note), 16 (beat 64) -1.52 and
  18 (beat 72) -1.21 in the breath (dreamy) phrase, not explained (combo pad, dreamy bells and combo bells sound MIDI 77
  together and the pad starts 36-65 ms earlier by the onset rule; per-pitch levels move +-4-9 dB both ways there), 19 +1.03
  and 20 +1.20 at the jump landing of beat 80 (the timeline layers wait for Studio's stall, the one-shots play). Two legacy
  harmony takes differ by at most 0.20 dB per window.
- **harmony at intensity 1** (`baseline_seed7_s11_i1_soloharmony_*`): funk phrases (beats 80-111) **+1.2 to +1.46 dB** in
  every window with an identical note set. The funk keys (ClavHigh) are the only mono sample of the 13 layers; probably the
  mono-to-stereo handling differs between the timeline track and the instrument event (not verified, not fixed).

Note sets: the notes the new path scheduled (`.patterns.csv`) equal what the timeline plays with the logged parameter values
(`.params.csv`, with their lead) and the SectionPhrase rule, except notes on the last eighth of a phrase (the lead, intended).

One-shots: late 0 in every take; dropped 0 except the two styles takes (the same 9 in each): pattern notes at beats
128.49-128.74 (TensionSub, TensionAcid, FunkKeys x4, Bass) and 192.49 (TensionSub, TensionAcid), all in the first beat after
a jump landing (dispatched one beat early, their channel group came after the start during Studio's stall); the first styles
take also dropped 2 kill notes at 192.16 / 192.17. `.losses.csv` lists every loss with its note beat and owner; `.cpu.txt`
has a PATTERNS part (scheduled, started, late, dropped) next to KILLS.

Attack: `analyze_capture.py onsets` pairs two bands of one take and cannot measure this on music. A per-note envelope onset
against each take's own beat log (pluck notes of the intensity-1 harmony solos) gives new - legacy mean +0.26 ms on the 25
pluck attacks it could find in beats 1-63, with outliers up to 24 ms where it picks other features: not a trustworthy number.
Per-note diagnostic (`logOneShots`: `.patterns.csv` + `.oneshots.csv`): every pattern note's `setDelay` was applied at least
one mixer block before its start, and every check before a start (4338-10050 per take) read channel position 0: nothing
early. The start comes from the same beat clock and backend as the Task 10 spike clicks (60 fps: 0.81 / 2.33 ms mean / max).
Onset moves (ComboPad, Tension strings, Dreamy choir first-bar notes 30-71 ms earlier) could not be measured on the captures
(slow attacks give no detectable onset under the other layers); the new path applies them by construction (`onset_s`).

Tension sweep (`tensionSteps` 0 / 0.3 / 0.5 / 0.7 / 0.9, bedWithoutHarmony, heavy seed 7): median window difference per level
+0.08 / +0.10 / +0.16 / +0.26 / +0.09 dB; bands over the take <300 Hz +0.16, 300-2k +0.03, >2k 0.00 dB: the sub-bus gain
curves follow the old track automation.

CPU (Studio update avg / Core dsp avg, %): baseline 5.98 / 1.72 -> 6.25 / 2.06; heavy seed 7 7.27 / 5.36 -> 7.15 / 5.75;
heavy seed 33 8.00 / 5.59 -> 8.40 / 5.71; styles 8.73 / 3.20 -> 8.69 / 3.37 (budgets: Studio 11, Core 6 baseline / 12 heavy).

Landings (`analyze_capture.py landings`, heavy seed 33, beats 16 / 32 / 48 / 64 / 80 / 96 / 112): boundary delay legacy
+276 / +253 / +328 / +323 / -2 / +276 / +403 ms, new +300 / +319 / +374 / +342 / -2 / +328 / +434 ms; no gaps in either; the
first half beat after a landing is louder on the new path (beat 112: -31.0 legacy, -29.8 new dBFS).

### Known limitations

- First beat: the clock's first anchor comes with beat 0's callback; at that frame now = beat 0.000-0.038 (`FIRSTANCHOR` in
  `.cpu.txt`), so beat 0's notes cannot be scheduled (start = beat - onset needs at least a mixer block of lead): the bass,
  tension and pad notes of beat 0 are missing on the new path (window 0: -1.02 dB full mix, -2.6 / -3.7 dB solos); every
  note from beat 0.5 on plays. Needs the clock switch of a later plan.
- Notes within about 125 ms of now keep the state they were scheduled with when a decision changes.
- `legacyLayers` is read in `StartMusic` only.
- Funk keys and the breath-phrase windows: resolved in the follow-up round below.

### Verdict

Full-mix takes pass (every window within 1 dB except the first-beat window, 1.02), late 0, drops in these takes only in the
first beat after a jump landing, CPU in budget. The harmony family does not (funk keys level, two breath-phrase windows).
**`legacyLayers` stayed true at this point** (the follow-up round below found both causes and flipped the default to false;
fix round 1 set it back to true);
`CaptureScenario.layers = oneShots` (or the inspector field) plays the new path.

### Follow-up round: causes of the open differences, attack measured, final takes, default

**Funk keys (+1.2..+1.46 dB): one-shot stops, not the mono sample.** Per note at sample level in the intensity-1 harmony
solos (funk keys only in those phrases, notes separated by digital silence), the sustain level is identical
(-45.60 legacy, -45.66 one-shot dBFS) and the note set is identical; the length is not: the timeline note sounds 79 ms
(0.15 beat at 124 BPM = 72.6 ms, then a ~5 ms ramp), the one-shot 85-111 ms. The backend's per-note diagnostic shows why:
(1) FMOD Studio clears the channel group's delay end a few frames after the unpause (in 3rd-6th checks most notes read
delay end 0), so the note ran until `Update` stopped the instance after its stop time; (2) with the end restored, the note
sounded 59 ms: the voice starts one mixer block (1024 samples) after its group's delay start (the channel position
extrapolates to start + 1025 samples) while the delay end is exact, so a restored stop cuts one block early. Fixes
(commit 0556876): pattern notes get their stop re-applied while it is still ahead (`OneShotArming.StopLost`, 885 re-applies
in a 128-beat take) and the stop is placed one block later; the stop fade comes from the generator
(`instruments.py` `PATTERN_STOP_FADE_MS = 5`, the timeline's region-end ramp, measured offline: -44 -> -90 dBFS in the last
~5 ms) through `InstrumentTable.ReleaseSeconds` for every non-kill bus; the earlier 20 ms C# override is gone. Funk windows:
+1.2..+1.46 dB -> +0.16..+0.35 dB; note length 84 ms (79 legacy). The same cause shortened or lengthened every pattern note
on every layer (not mono-related: an offline render of the gated timeline track and of the instrument event at the same
pitch gives the same sustain level on both channels). Kill chords use the same backend and are left as they were (the brief
forbids changing them): their delay end is also cleared, so they end when `Update` stops the instance after stop + release
(about a frame late) - a separate item for the controller.

**Breath-phrase harmony windows (beats 64-67, 72-75).** Window 64 is a jump landing (accepted). For 72-75 the decisive
experiment was a temporary take with the combo pad's onset compensation forced to 0 (`..._s11z_i3_soloharmony_new_exp_padonset0`,
not committed): window 72-75 went from -1.49 to +0.28 dB (64-67 from -1.45 to -0.33, 40-43 from -0.68 to -0.13, 100-103 from
-0.83 to +0.32). The difference is the intended onset move of the combo pad (starts 36-65 ms earlier) interfering with the
choir / dreamy bells notes on the same pitches (per-pitch levels move +-4-9 dB both ways): explained. Per-layer legacy solos
were not possible in the Editor (the runtime gives no handle on single timeline tracks); the offline one-shot renderer I
tried could not hold its delays (Studio clears them there before the first re-apply), so it was not used for numbers.

**Attack measured** (`baseline_seed7_s11z_attack_pluck_new` / `_legacy`: intensity 1, `holdArrangement`, harmony solo,
`patternLayers` Pluck, `spikeRefClicks`): every pluck attack (150-700 Hz envelope, 30 % crossing) against the timeline's
1 kHz beat clicks of the same take, beat position interpolated between clicks, notes in the first beat after a phrase
boundary excluded. The legacy take calibrates the detector (attack - click +24.64 ms, sd 0.29 ms; the timeline starts the
pluck sample on the beat, so its attack is at beat + sample onset). One-shot attack vs the legacy pluck's timing: 98
notes from beat 2 on (no landing notes in this take's detectable set): mean signed -1.19 ms, mean |e| 1.33 ms, max |e|
2.60 ms; beat 1.5 was +11.8 ms. *(Correction, Task 12 follow-up: this is the one-shot pluck against the legacy pluck, not
against the beat. The calibration assumed the legacy pluck sits on the beat, but the legacy Harmony layers reach the mixer
one mixer block late (send to return:/Harmony Duck): the legacy pluck's attack is +22.8 ms after the beat click, and the
one-shot pluck, one block late for its own reason (a multi-sound instrument event), matched it. See "Task 12, follow-up
round".)* **The criterion is not met at beat 1.5** (see fix round 1: it is the first anchor's error, carried to the clock
switch); fix round 1 also measures the notes after jump landings.

**Final takes** (same session, code c848087's parent 624fd01 + default flip; every take stalls 0):

| scenario | legacy | new | max window diff | windows > 1 dB | late | drops |
|---|---|---|---:|---|---:|---|
| baseline seed 7 | `baseline_seed7_s11z_legacy` | `_s11z_new` | 1.03 | w0 (beat 0) | 0 | 0 |
| heavy seed 7 | `heavy_seed7_s11z_legacy` | `_s11z_new` | 1.05 | w0 (beat 0) | 0 | 0 |
| heavy seed 33 | `heavy_seed33_s11z_legacy` | `_s11z_new` | 0.94 | - | 0 | 0 |
| styles (bed, i3) | `styles_seed7_s11z_bed_i3_legacy` | `_s11z_bed_i3_new` (valid, 0 stalls) | 0.90 | - | 0 | 28 (15 pattern, 13 kill): 12 in the first beat after a landing (7 pattern at 128.16-128.49; 1 pattern + 4 kill at 144.96-144.99) and **16 outside it** (7 pattern + 9 kill at 145.24-145.99); Studio max 100.6 % |
| styles, retake | same | `_s11z_bed_i3_new_r2` (retake of the line above) | 0.54 | - | 0 | 7 pattern at 128.49-128.74 (first beat after the landing) |
| harmony i3 heavy 7 | `heavy_seed7_s11z_i3_soloharmony_legacy` | `_new` | 3.82 | w0, w16 (landing 64), w18 (-1.49, pad onset, shown above) | 0 | 1 Bass at 80.35 (landing) |
| harmony i1 baseline 7 | `baseline_seed7_s11z_i1_soloharmony_legacy` | `_new` | 1.45 | w15 / w19 (phrase-end lead), w16 (landing 64) | 0 | 0 |

The valid styles take `_s11z_bed_i3_new` dropped notes at beats 144.96-145.99, one to two beats after the jump landing at 144
(16 drops outside the first beat after a landing: 7 pattern, 9 kill; `.losses.csv` lists them), with a Studio update
maximum of 100.6 % and no main-thread frame over 13.5 ms between beats 143 and 147. Its retake did not repeat it. Fix round 1
below.

CPU (Studio update / Core dsp avg %, legacy -> new): baseline 6.10 / 1.71 -> 6.77 / 1.81; heavy 7.26 / 5.29 -> 7.72 / 5.46;
heavy seed 33 8.86 / 5.66 -> 8.44 / 5.79; styles 8.74 / 3.19 -> 9.85 / 3.55.

**Default (follow-up round):** every window over 1 dB is beat 0, a landing, a phrase-end lead window or the pad-onset
interference shown by experiment; late 0; CPU in budget; `legacyLayers` was set to false (commit c848087). That flip did not
account for the drop burst of the valid styles take above and is withdrawn in fix round 1.

### Fix round 1: attack after start and after jump landings; the styles drop burst; default back to true

`CaptureScenario` now also writes `<take>.studio.csv` (per frame: beat, frame time, Studio update and Core dsp CPU) and
`<take>.anchors.csv` (per beat callback: master DSP clock when it is handled vs the clock's DSP time of that beat, and the
clock's anchor beat).

**Attack, every note** (`baseline_seed33_s11r_attack_pluck_new` / `_legacy`: seed 33 with jump landings at 16, 48, 64 and
112, the beat callbacks there 352-381 ms late; intensity 1, `holdArrangement`, harmony solo, `patternLayers` Pluck,
`spikeRefClicks`, `logOneShots`; method as above, detector calibrated on the legacy take):

| notes | n | mean signed | mean abs | max abs |
|---|---:|---:|---:|---:|
| all | 93 | -1.00 ms | 1.44 ms | 16.14 ms |
| outside one beat after start / a landing | 89 | -1.18 ms | 1.27 ms | 2.48 ms |
| inside | 4 | +2.81 ms | 5.26 ms | 16.14 ms |

Inside notes: beat 0.5 -2.29 ms (scheduled from the first poll of beat 0's anchor); beat 16.5 -1.65 ms (anchor 15, age 1.5
beats, beat 15's callback 8 ms after its clock time); beat 64.5 -0.96 ms (anchor 63, age 1.5, callback +20 ms); beat 1.5
+16.14 ms. The landings at 48 and 112 start breath phrases (no pluck). Beat 1.5 was dispatched at frame 315 from beat 0's
anchor, whose running mean of polls had drifted to DSP 73589 while the true beat 0 (beat 1's anchor minus one beat) is
72677: the anchor is 19.00 ms late, and the note's start is 18.99 ms late against the same reference - the note carries
exactly the anchor's error, and notes scheduled after beat 1's anchor are back within 2.5 ms (the seed-7 take showed the
same at beat 1.5: +11.8 ms). **Criterion (mean <= 2, max <= 5 ms): met outside the first beats after start (and on the two
post-landing notes measured: -1.65 / -0.96 ms); NOT met at beat 1.5**, the second beat after start, because of the beat
clock's first anchor (`MusicConductor.PollAnchor` averages polls whose timeline position lags during the first beat). This
is the clock's limitation, carried to the clock switch; this task does not change the clock.
*(Correction, Task 12 follow-up: "met" here means "matches the legacy pluck", not "on the beat": the detector was calibrated
on the legacy pluck, which is itself one mixer block (+22.8 ms) after the beat click. Measured against the click, both the
legacy and the one-shot pluck were about one block late; see "Task 12, follow-up round".)*

**Styles drop burst.** The valid take `styles_seed7_s11z_bed_i3_new` (not logged with `logOneShots`, so the per-note drop
reason is not available for it) dropped 16 notes 1-2 beats after the landing at 144, 9 of them kill chords (same code on both
paths), with Studio update max 100.6 % and main-thread frames <= 13.5 ms over beats 143-147: Studio, not the main thread,
fell behind. Eight more styles takes in one session on the current code (`_s11r_bed_i3_{legacy,new}_{a,b,c}` with full logs,
plus the earlier takes):

| take | path | Studio avg / max % | frames >= 95 % | drops in the first beat after a landing (reason) | drops outside |
|---|---|---|---:|---|---:|
| `_s11r_..._legacy_a` | legacy | 9.42 / 85.8 | 0 | 0 | 0 |
| `_s11r_..._new_a` | new | 9.41 / 88.7 | 0 | 7 (group never appeared before the start) | 0 |
| `_s11r_..._legacy_b` | legacy | 9.06 / 85.0 | 0 | 9 kill (3 group never appeared, 6 start reached before the delay) | 0 |
| `_s11r_..._new_b` | new | 9.07 / 84.1 | 0 | 7 (group never appeared) | 0 |
| `_s11r_..._legacy_c` | legacy | 9.02 / 87.1 | 0 | 2 kill (start reached before the delay) | 0 |
| `_s11r_..._new_c` | new | 9.47 / 89.1 | 0 | 15 (group never appeared) | 0 |
| earlier: `_s11_..._legacy`, `_s11z_..._legacy` | legacy | 8.73 / 81.7, 8.74 / 82.7 | - | 0, 0 | 0, 0 |
| earlier: `_s11_..._new`, `_new2`, `_s11z_..._new_r2` | new | 8.76 / 84.0, 8.69 / 84.4, 9.85 / 90.9 | - | 11, 9, 7 | 0 |
| `_s11z_..._new` (the valid failing take) | new | 9.99 / **100.6** | - | 12 | **16** |

Result: drops outside the first beat after a landing in 1 of 7 new-path styles takes and 0 of 5 legacy takes; Studio
update maxima >= 95 % only in that same take. Inside the first beat after a landing both paths drop notes (legacy: kill
chords only; new: pattern notes as well). The burst occurred only on the new path; its cause (a Studio update stall well
beyond the usual landing stall) was not found or reproduced, so it is not fixed here. Per the controller's ruling the flip
condition "drops only at landings" is not met: **`SoundManager.legacyLayers` is back to true** (the code default; no scene or
prefab serializes it).

## Task 12: drums as scheduled one-shots (style drums, core kit, second scheduler)

The 10 drum tracks (`Kick - beats 1 and 3`, `Snare - beats 2 and 4`, `Eighth-note hats`, `Clap layer`, `Snare variations`,
`Combo hats`, `Latin perc`, `Latin shaker`, `Funk drums`, `Dreamy perc`; 3216 notes) are gated on `LegacyLayers` like the
13 Task 11 tracks (`legacy_gate.js`: added 3216, second run "unchanged 6320", Metadata byte-identical; the event XML gained
3216 `ParameterCondition` objects and nothing else). Their notes are pattern clips (`FMOD/Tools/pattern_drums.py`, 11
layers, 22 clips): one instrument `Drums` whose `Note` is the percussion number (`samples.PERC`, General MIDI numbers,
every sound at pitch 0), on `bus:/Music Bed/Layers` and, for the latin percussion, `bus:/Music Bed/Layers/Latin Perc`
(the track's 90 Hz high-pass, `mixer.py`). Every drum note has length 0 (plays to its sample end; the timeline cut it at
its region end, which matters for the snare and clap: +0.8 dB per 0.7-beat note at 108 BPM, offline). The old track
fader is folded into the note's dB (kick -12, snare -4, eighth hats -2, clap -6, snare variations -4).
`patterns.py --compare-legacy`: unexplained 0 (new rules: track fader folded 592, length 0 3216, combo hats' 1 ms ADSR
attack dropped 256; the kick's `FeverBuild [0, 0.5]` = every fever phase but Build).

Core kit: `Core kick` (every style and fever phase but the build-up bar), `Core snare` / `Core hat` (styles of
`patterns.STYLE_USES_CORE_BACKBEAT`: synthpop) are `PatternTable.CoreLayerMask`. `SoundManager` schedules them with a
second `PatternScheduler` (`_corePatterns`, lookahead and dispatch window `PatternTable.CoreLookaheadBeats` = 4) and every
other layer with `_patterns` (lookahead 1.5, as Task 11 set it; the brief's 1.0 would be shorter than the onset + dispatch
lead `PatternScheduler` documents). `UpdatePatterns` / `InvalidatePatterns` / `ResetPatterns` call both;
`RefreshArrangement` compares the decisions over now .. now + 4. `CaptureScenario.patternLayers` sets both masks.
**`legacyLayers` stays true.**

### Kill-chord stop (carried from Task 11, commit 7237b9f)

Kill chords now stop like the pattern layers: stop one mixer block later (`OneShotArming.StopAt`, no bus fork) and a stop
that FMOD Studio cleared is re-applied (every bus). (The one-block stop offset was replaced in the follow-up round: the
block now comes off the start, see below; the re-apply stays.) RED: `StopPoint_IsOneBlockLater_OnEveryBus` / `LostStop_IsReapplied_
OnEveryBus` failed on `KillChords` (10000 instead of 11024; false). `killsOnly` pair (`styles` seed 7, kill chords only):

| takes | windows | per-window diff (median / mean / max) | style RMS vs style 0 (latin / funk / dreamy) |
|---|---:|---|---|
| `_s12_kills_before_run2` (valid) | | | +0.03 / -0.34 / -0.06 |
| `_s12_kills_before_ab` (same pre-fix code, recorded later, 1 stall) vs `before_run2` | 63 | +0.00 / -0.05 / 2.22 (landings 48, 68) | +0.01 / -0.29 / -0.01 |
| `_s12_kills_after_run2` (2 stalls of 104 / 111 ms) vs `before_run2` | 63 | -0.39 / -0.33 / 1.04 (landing 144) | +0.51 / -0.70 / +0.44 |
| `_s12_kills_after_run4` (1 stall) vs `before_ab` | 63 | -0.44 / -0.36 / 2.25 (landings) | +0.53 / -0.69 / +0.46 |

The fix lowers the kill chords by about 0.4 dB (they now end on their sample stop instead of on the first `Update` after
stop + release); per style vs style 0 they stay within 1 dB (worst -0.70, funk). No valid (0-stall) after-take was
recorded: the machine had external load during these takes (two games and a background download; Studio avg 9-11 %
against 7.15 % in the clean before-take), the A/B take on the old code under the same load was equally degraded.

### Max Instances: the funk kick was stolen (fixed, commit 51cfab5)

First new-path styles takes (`_s12a_bed_i3_new_a/b/c`, valid): every even beat of the funk block -1.3..-2.2 dB against
legacy, the 4-beat windows still within 1 dB. Kick-beat low band (< 120 Hz, first 80 ms of beats 4k+2): funk -30.0 dB,
the other styles -0.2..-0.4 dB: the funk kick never sounded, although it was scheduled, started, armed and unpaused
like every other kick (`.oneshots.csv`). Cause: `Max Instances` of `event:/Inst/MusicBed/Drums` (23, stealing Oldest)
counted only the sounding time. An instance exists from its dispatch (paused; 4 beats early for the core kit) until its
event's timeline ends, which is the end of the event's longest region (all sounds of an instrument event start at 0;
claves 1.3 s), so each funk 16th hat lived 1.3 s and the oldest instances, the pending core kicks, were stolen. The rule
now counts dispatch -> timeline end (length 0) or stop + release; Drums / Music Bed 43, Latin Perc 23, the other pattern
events +1..+4. Same scenario after the fix (`_s12b_bed_i3_new_a..d`): funk kick -0.4..-0.5 dB median.

### Acceptance before the follow-up round (superseded: every one-shot was one block late; final pairs below)

Legacy references: `_s12a_bed_legacy` takes. New: `_s12b_` takes (final banks; the legacy path plays no `event:/Inst`
pattern event, so the max-instances rebuild does not change it).

| scenario | legacy | new | windows > 1 dB (diff) | late | drops |
|---|---|---|---|---:|---|
| baseline seed 7 | `baseline_seed7_s12a_bed_legacy_run2` | `baseline_seed7_s12b_bed_new` | w0 -2.50, w11 (44-47) +1.36, w12 (48) -1.16, w19 (76-79) +1.27 | 0 | 0 |
| heavy seed 7 | `heavy_seed7_s12a_bed_legacy` | `heavy_seed7_s12b_bed_new` | w0 -3.87 | 0 | 0 |
| heavy seed 33 | `heavy_seed33_s12a_bed_legacy` | `heavy_seed33_s12b_bed_new_a` / `_b` | w0 -3.84 / -3.88 | 0 / 0 | 25 / 0, all in the first beat after the landing at 16 |
| heavy seed 7, full mix | `heavy_seed7_s12b_full_legacy` | `heavy_seed7_s12b_full_new` | w0 -1.07 | 0 | 0 |
| styles (bed, i3, 256 beats) | `styles_seed7_s12a_bed_i3_legacy` | `_s12b_bed_i3_new_a..d` (**each 1 stall, not valid**) | w0, windows ending at a landing (+1.0..+1.6), landing windows 192 (-2.8..-3.0), 208 (-1.1), 224 (-1.6..-2.0) | see below | see below |

- Window 0: beat 0's notes cannot be scheduled before the clock's first anchor; the core kick, snare and hat of beat 0
  are now among them (-2.5 / -3.9 dB, Task 11 -1.0).
- Windows that end at a jump landing (44-47 before 48, 76-79 before 80, ...): the beat log stamps the landing beat with its
  late callback (300-450 ms), so the logged last beat of the window holds the landing gap; the timeline is silent there
  and the one-shots play on the grid (per beat: L-1 +3.5..+5.6 dB, L -5.7..-11 dB, L-4..L-2 within 0.6 dB). Landing
  windows (accepted).
- Styles landing windows at 192 (dreamy) also have beats L+2 / L+3 at -1.6 / -3.3 dB in every take; not explained further.
- The styles criterion has no valid take on the final code (four takes, each with one main-thread stall); the three
  valid takes on the earlier banks show the same window set.

### Attack before the follow-up round (superseded; core drums solo against the timeline's beat clicks)

`baseline_seed33_s12_attack_core_new` / `_legacy` (seed 33, intensity 0, `holdArrangement`, Music Bed solo,
`patternLayers` Core kick,Core hat, `spikeRefClicks`, `logOneShots`, both 0 stalls). Detector: band envelope (hat > 6 kHz,
kick < 150 Hz, click 900-1100 Hz), 30 % crossing; the legacy take calibrates it (the timeline starts the drum samples on
the beat): hat -0.40 ms (sd 0.00, 56 even beats), kick +0.90 ms (sd 0.05).

| notes | n | mean signed | mean abs | max abs |
|---|---:|---:|---:|---:|
| hat, all | 128 | +19.42 ms | 19.74 ms | 22.96 ms |
| hat, outside one beat after start / a landing | 113 | +20.69 ms | 20.69 ms | 22.69 ms |
| kick, all | 64 | +18.52 ms | 19.15 ms | 22.92 ms |
| kick, outside | 56 | +20.79 ms | 20.79 ms | 22.92 ms |

**Criterion (mean <= 2, max <= 5 ms) NOT met**: the one-shot core drums sound about 20.7 ms (close to one 1024-sample
mixer block, 21.3 ms) after the beat click, steadily (no drift across the take or across the landings), while the
timeline drums sit on the click. Inside notes (landing beats, where the music is silent and the reference is the
extrapolated grid) are not meaningful. Cause found and fixed in the follow-up round below.

Drift of core notes (scheduled up to 4 beats ahead from an older anchor): each note's DSP start at scheduling vs the beat's
anchored DSP time: core median +1.25..+1.58 ms, other pattern notes +1.42..+1.48 ms (p5..p95 within -0.4..+4.4 ms) in
heavy 7 / heavy 33 / styles takes; the outliers are beats next to a landing. Tempo changes at a phrase boundary re-time
dispatched notes (`RescheduleFrom`); `Invalidate` is not involved (it re-states arrangement decisions only). The follow-up
round found that core notes handed over before a landing do drift (L+4: +12.6 / +15.6 ms) and fixed it.

### Losses and Studio bursts (ruling 5)

Every new-path take, losses outside the first beat after a landing:

| take | stalls | Studio avg / max % (frames >= 95 %) | Core dsp avg / max % | pattern sched / started / late / dropped | kill late / dropped | losses outside (cause) |
|---|---:|---|---|---|---|---|
| `baseline_seed7_s12a_bed_new` | 0 | 8.54 / 94.14 (0) | 1.20 / 2.05 | 1105 / 1105 / 0 / 3 | 0 / 0 | - |
| `heavy_seed7_s12a_bed_new` | 0 | 9.17 / 90.63 (0) | 2.92 / 4.34 | 1589 / 1588 / 0 / 0 | 0 / 0 | - |
| `heavy_seed33_s12a_bed_new` | 0 | 9.70 / 89.09 (0) | 2.80 / 3.64 | 1400 / 1396 / 0 / 0 | 0 / 0 | - |
| `styles_seed7_s12a_bed_i3_new_a` | 0 | 11.36 / 94.06 (0) | 2.07 / 3.71 | 3024 / 3011 / 2 / 14 | 9 / 21 | 9 kill + 2 pattern late at 130 (clock) |
| `styles_seed7_s12a_bed_i3_new_b` | 0 | 10.96 / 89.82 (0) | 2.04 / 3.16 | 3024 / 3015 / 0 / 14 | 0 / 10 | - |
| `styles_seed7_s12a_bed_i3_new_c` | 0 | 10.55 / 89.08 (0) | 2.07 / 3.30 | 3031 / 3017 / 0 / 10 | 0 / 0 | - |
| `styles_seed7_s12b_bed_i3_new_a` | 1 | 11.03 / 95.60 (1) | 2.06 / 3.18 | 3031 / 3016 / 3 / 6 | 9 / 0 | 9 kill + 3 pattern late at 194 (clock); 3 pattern dropped at 232.5 (350 ms main-thread frame) |
| `styles_seed7_s12b_bed_i3_new_b` | 1 | 10.66 / 100.88 (5) | 1.94 / 3.43 | 3000 / 2983 / 9 / 4 | 5 / 13 | 31 at 81.9-84.5 (Studio double gap at 80, then a 935 ms main-thread frame) |
| `styles_seed7_s12b_bed_i3_new_c` | 1 | 13.21 / 116.79 (74) | 2.22 / 10.62 | 3012 / 3004 / 0 / 47 | 0 / 27 | 51 dropped (group never appeared) at 112.97, 129.2-129.99, 145, 193, 225: the second beat after landings, behind Studio gaps of 0.54-0.84 s (callbacks 128 / 144 932 / 759 ms late) |
| `styles_seed7_s12b_bed_i3_new_d` | 1 | 12.02 / 99.22 (21) | 2.06 / 3.36 | 3022 / 3005 / 6 / 21 | 9 / 30 | 30 at 193.45-195 (918 ms main-thread frame) |
| `baseline_seed7_s12b_bed_new` | 0 | 8.18 / 86.11 (0) | 1.21 / 1.76 | 1105 / 1105 / 0 / 0 | 0 / 0 | - |
| `heavy_seed7_s12b_bed_new` | 0 | 8.51 / 85.38 (0) | 2.85 / 3.45 | 1589 / 1588 / 0 / 0 | 0 / 0 | - |
| `heavy_seed33_s12b_bed_new_a` | 0 | 9.91 / 91.32 (0) | 2.78 / 6.55 | 1406 / 1400 / 0 / 1 | 0 / 24 | - |
| `heavy_seed33_s12b_bed_new_b` | 0 | 10.25 / 89.88 (0) | 2.78 / 3.73 | 1400 / 1396 / 0 / 0 | 0 / 0 | - |
| `heavy_seed33_s12b_full_new_c` | 0 | 10.82 / 96.76 (2) | 6.14 / 10.67 | 1400 / 1396 / 0 / 0 | 0 / 0 | - (the >= 95 % frames are at the landing 112) |
| `heavy_seed7_s12b_full_new` | 0 | 9.52 / 91.86 (0) | 5.83 / 9.11 | 1589 / 1587 / 0 / 0 | 0 / 0 | - |

What the logs show (`.studio.csv`: runs of frames in which Studio's update CPU reading did not change = no Studio update
completed; `.anchors.csv`; Editor.log):
1. **Every jump landing** has a Studio no-update gap of 0.4-0.5 s, on both paths. Occasionally it is longer or doubled
   (new: 0.59 + 0.61 s at 80, 0.84 / 0.81 s at 128 / 144; legacy kills-only takes, old and new kill code: 1.46 s at 224,
   0.77 s at 48). Notes due in that time lose their channel group ("group never appeared") one to two beats after the
   landing. The burst is not new-path-only; why Studio occasionally takes twice as long was not found.
2. **Beat clock after a landing**: a landing callback 140-172 ms late (under `MusicConductor`'s half-beat limit) becomes an
   anchor and moves the clock back by up to a beat (193.40 -> 192.38); the next anchor puts it back, and notes whose
   dispatch fell in between are counted late (never played late; kill chords alike). Clock limitation, for the clock
   switch. The core kick of those beats, dispatched 4 beats earlier, played.
3. **Editor main-thread freezes** (918 / 935 ms) each directly after an `Asset Pipeline Refresh ... RefreshV2` in the
   Editor log (no file under Assets changed; probably the Editor's auto-refresh on focus while the machine was in use).
   Notes are neither dispatched nor armed during the freeze. Editor-only.
4. External load (games, browser) during several takes; under it the legacy kills-only takes showed the same kinds of
   bursts (`styles_seed7_s12_kills_after_run3` / `_run4`, `_before_ab`).

### CPU (avg %, Editor)

Heavy seed 7 full mix: Studio update 9.67 legacy -> 9.52 new, Core dsp 5.51 -> 5.83 (budget 12; max 9.11). Heavy seed 33
full mix new: 10.82 / 6.14. Baseline seed 7 (Music Bed solo): Studio 9.07 -> 8.18, Core dsp 1.00 -> 1.21 (no full-mix
baseline take; budget 11 / 6). Styles (solo): Studio 10.55-13.21, Core dsp 1.94-2.22. Within the budgets; no fallback
needed.

### Follow-up round: the one-block start delay (cause, fix, re-measure)

**Cause, by experiment.** `spike` scenario (seed 33, 68 beats, 60 fps, `shortLeadOnDsp`; new option
`CaptureScenario.spikeTarget` = instrument:bus:MIDI, the timeline's own beat clicks as reference; SpikeClick events now
exist on five buses as a latency probe, `spike_click.BUSES`). One thing varied per take:

| take `spike_seed33_s12f_*` | what plays on the beat | test - click, mean signed (sd) |
|---|---|---:|
| `killchords` | 3 kHz click event (1 sound), bus:/Kill Chords | +0.06 ms (0.63) |
| `layers` | same event on bus:/Music Bed/Layers | +0.45 ms (0.62) |
| `click_adsr00` | same, bus:/Music Bed/Layers/Bass (a pattern sub-bus), ADSR 0 / 0 like the pattern events | +0.22 ms (0.55) |
| `drumshat` | the Drums event, Note 42 (closed hat), its real bus | +19.92 ms (0.53) * |
| `drumsevent_click` | the 3 kHz click added to the Drums event as one more Note (temporary) | +21.73 ms (0.55) |
| `click_8sounds` | the Bass-bus click event with 7 more sounds (temporary) | +21.71 ms (0.63) |
| `click_8sounds_first` | same, the first of the 8 sounds | +18.8 ms (5.2) * |

(\* own band detector, which reads the on-time click event at -1.43 ms; the others `analyze_capture.py spike`.) Bus, sub-bus,
ADSR and sample file change nothing; **an instrument event with more than one sound (one picked by the Note condition)
starts its voice one mixer block (1024 samples, 21.3 ms) after its channel group's delay start; with one sound it starts
on it.** Every pattern and kill-chord event has several sounds, so every one-shot sounded one block late (this is also
what Task 11 saw as "the voice starts one block after the delay start"; its one-block stop offset fixed only the length).
Harmony: the legacy Harmony layers reach the mixer one block late as well (their group sends to return:/Harmony Duck):
legacy pluck attack +22.77 ms after the beat click (sd 0.38), legacy drums -0.41 / +1.01 ms (hat / kick).

**What Task 11's pluck figure was.** The "1.33 / 2.60 ms" (and fix round 1's 1.27 / 2.48 ms) was the one-shot pluck
measured against the legacy pluck (the detector was calibrated on it), i.e. against a reference one block late; against
the beat click both were about one block late. Corrected in place above.

**Fix** (commit 11e3b7d): `instruments.py` `MULTI_SOUND_VOICE_DELAY_BLOCKS = 1` and `PATH_DELAY_BLOCKS = {Music Bed/Harmony:
1}` -> `InstrumentTable.VoiceDelayBlocks(inst, bus)` / `PathDelayBlocks(bus)`. The backend sets the group's delay start to
start + (path - voice) blocks and its delay end to stop + path blocks (`OneShotArming.DelayStart` / `StopAt`): drums, bass,
tension, kill chords start a block earlier and stop exactly; Harmony keeps the legacy one-block path delay. Tests:
`OneShotLatencyTests` (RED: compile errors, the API did not exist). Spike check after the fix: Drums hat -1.38 ms, kill
chord Marimba -1.53 ms (with its 5 ms onset) against the click event's -1.43 ms on the same detector: on the beat.

**Core notes and landings** (commits 11e3b7d, 510b1f3; **the re-time described here was removed in fix round 2**). Core notes handed over 4 beats ahead from the anchor before a jump
landing (which drifts while the timeline stalls ahead of the jump) sounded +12.6 / +15.6 ms late at L+4. `OneShotScheduler`
now re-times a handed-over note when the clock's newest anchor is >= 2 beats newer than its dispatch anchor and settled
(half a beat old) and moves its start by > 5 ms. Two simpler rules were measured and rejected: re-timing on any move
> 2 / 5 ms followed the anchor refining itself (+-5..8 ms around boundaries; 289 re-times per take, kill chords handed over
twice), and re-timing once from the first anchor after a landing put L+3 notes 11 ms late. `.retimes.csv` / "retimed" in
`.cpu.txt` count them (2-76 per take now).

**Attack after the fix, with the re-time** (superseded; final numbers in fix round 2) (seed 33, `spikeRefClicks`, against
the beat clicks; inside = beats 0-2 after start and L, L+1 after the landings 16 / 48 / 64 / 112; every take 0 stalls; code
510b1f3):

| sound (take) | notes | n | mean signed | mean abs | max abs |
|---|---|---:|---:|---:|---:|
| core hat (`_s12l_attack_drums_new`) | all / outside | 119 / 107 | -1.37 / -1.49 ms | - / 1.50 | 4.46 / 2.71 |
| core kick (same) | all / outside | 60 / 56 | -0.14 / -0.15 | - / 0.35 | 1.06 / 1.06 |
| open hat, a style layer (same) | all / outside | 91 / 86 | -1.05 / -1.22 | - / 1.24 | 16.59 / 2.47 |
| bass (`_s12l_attack_bass_new`) | all / outside | 60 / 56 | +0.15 / +0.03 | - / 0.80 | 5.92 / 5.92 |
| pluck, Harmony (`_s12l_attack_pluck_new`) | outside | 86 | +20.76 | 20.76 | 22.79 |
| legacy pluck (`_s12h_attack_pluck_legacy`) | outside | 86 | +22.77 | 22.77 | 23.59 |
| before the fix: core hat / kick (`_s12_attack_core_new`) | outside | 113 / 56 | +20.69 / +20.79 | | 22.69 / 22.92 |

- Inside notes: hat 49 / 113 +4.2 / +4.5 ms (L+1 after landings whose callbacks came 359 / 308 ms late); open hat 1.5
  +16.6 ms (the second beat after start: the clock's first anchor, as Task 11's beat 1.5; carried to the clock switch).
- Bass: one note over 5 ms outside, beat 82 +5.92 ms (scheduled on the grid like its neighbours, +1.50 ms against the
  anchored beat; the same note was +0.1 ms in `_s12h_`); cause not found. So the bass line is **not met** in this take
  (mean met, max 5.92).
- Harmony: one-shot vs legacy pluck, paired per note: mean -2.01, mean |d| 2.02, max |d| 3.62 ms (1.0 ms of it is the
  pluck's intended onset move). Just over the 2 ms mean. **Absolute Harmony latency for the clock-switch plan: the legacy
  Harmony layers sound +22.8 ms (about one block + 1.4 ms) after the beat click; the Harmony one-shots +20.8 ms.**

**Legacy vs new after the fix** (Music Bed solo, 4-beat windows, same session, code 510b1f3 with the re-time; every take 0
stalls, no Editor refresh during the takes; the styles pair was recorded again in fix round 2):

| scenario | legacy | new | windows > 1 dB | late / drops outside the first beat after a landing |
|---|---|---|---|---|
| baseline seed 7 | `baseline_seed7_s12l_bed_legacy` | `_s12l_bed_new` | w0 -2.46; landing 48 -1.44; windows ending at the landings 48 / 80 / 96 +1.25 / +1.14 / +1.02 | 0 / 0 |
| heavy seed 7 | `heavy_seed7_s12l_bed_legacy_run2` | `_s12l_bed_new` | w0 -3.91 | 0 / 0 |
| heavy seed 33 | `heavy_seed33_s12l_bed_legacy` | `_s12l_bed_new` | w0 -3.89 | 0 / 0 |
| styles (bed, i3) | `styles_seed7_s12l_bed_i3_legacy` | `_s12l_bed_i3_new` (0 stalls) | w0 -1.88; windows ending at a landing +1.02..+1.27 (7); landing windows 64 -2.41, 192 -2.43, 208 -1.23, 224 -1.80 | **NOT met**: 4 pattern late at 65.5 (cancelled by the re-time and then late: the re-time's own loss); 15 pattern dropped: L+1 of 128 x4, 144 x6, 224 x3 (Studio landing gap) and 256 x2 (the re-time's own, see fix round 2) |

Every window over 1 dB is window 0 or a jump-landing window (including the windows that end at a landing, explained above,
ruling 12-3). The late / dropped criterion is **NOT met** in `styles` (see fix round 1).
Inside the dreamy landing windows the beats L+1..L+3 are still -1.4..-3.4 dB (192, 208, 224); not explained further.
Kick beats now match legacy exactly in every style (low band median 0.0 dB; -0.3..-0.4 before the fix). CPU, heavy seed 7 full mix
(`heavy_seed7_s12l_full_new`): Studio update 9.03 %, Core dsp 5.72 % (budget 12).

### Fix round 1: the re-time bounded; drops listed; kill chords on the final code

**Re-time (commit c9274d8).** The settled-anchor re-time followed any move: in `styles_seed7_s12l_bed_i3_new` a wrong anchor
held for half a beat moved beats 194 and 196 by +4912 samples (+102 ms; 196 was moved back two beats later, 194 could not
be), and at 65.5 it cancelled four notes that were then late (dropped). Now a re-time follows only a move of 5 ms ..
`RetimeMaxSamples` (1024, one block; the drift it exists for is 12-16 ms); a larger move is a clock discontinuity and the
note keeps its time (tempo changes stay `RescheduleFrom`'s job). The new instance is started before the original is
stopped, and both starts must be `RetimeMinLeadSamples` ahead, so a re-time never drops a note. Tests (RED shown):
`Retime_StepBackAnchor_NoteKeepsItsTime`, `Retime_ForwardJumpOverABlock_NoteKeepsItsTime`, `Retime_DriftWithinABlock_Follows`,
`Retime_ThatCannotBeHandedOver_KeepsTheOriginal`; the settle assertions (`Retime_AnchorNotSettled_Waits`,
`Update_ClockMovedDispatchedNote_RetimesIt`) shown RED with the settle condition removed.

Three new-path `styles` takes after the fix (bed, i3, Music Bed solo, `logOneShots`, 0 stalls, no Editor refresh):

| take | Studio avg / max % | pattern sched / late / dropped | kill late / dropped | re-times, largest move | pattern drops outside the first beat after a landing |
|---|---|---|---|---|---|
| `styles_seed7_s12m_bed_i3_new_a` | 10.74 / 89.13 | 3031 / 0 / 22 | 0 / 12 | 45, 776 samples | 3 at 193 (L+1 of 192: dreamy shaker 82, claves 75, acid 48), 2 at 256 (core kick / hat; landing at the end of the take) |
| `styles_seed7_s12m_bed_i3_new_b` | 11.82 / 96.31 (8 frames >= 95) | 3032 / 0 / 38 | 0 / 60 | 47, 545 | 34: L+1 of 16 (clap 39, acid), 48 (clap x3), 80 (cowbell 56, acid, shaker 82, marimba 76, slap 62), 128 (acid, snare / clap / hat x3), 144 (snare / clap / hat x3, guitar x2, acid), 176 (snare / clap / hat x6), 193.5 (shaker / sub, acid x2), 224 (acid, claves, shaker), 256 x2 |
| `styles_seed7_s12m_bed_i3_new_c` | 10.99 / 92.00 | 3032 / 0 / 18 | 0 / 27 | 58, 442 | 11: L+1 of 80 (cowbell), 128 (guitar, acid, snare / clap / hat x3), 224 (acid, claves, shaker), 256 x2 |

Late 0 in all three and no re-time over a block, so the re-time stays (ruling 12-5). **The drop criterion (dropped 0
outside the first beat after a landing) is NOT met.** Mechanism of every listed drop, from `.oneshots.csv` ("group never
appeared" / "start reached before the delay"): the notes are on the beat after a jump landing (L+1); their 1-beat
post-boundary dispatch window hands them to FMOD at the landing, inside Studio's 0.4-0.6 s no-update gap
(`.studio.csv`), and their channel group comes too late. Kill chords lose L+1 notes the same way on both paths (legacy
styles takes: 80.98 x9, 144.94 x3, 224.9 x2). Known condition (Studio landing gap), carried to the clock switch; a candidate
fix is a wider dispatch window for L+1 notes. *Correction (fix round 2): the "256 x2" drops in every take were not the Studio
gap but the re-time's own losses (it cancelled the armed instances and their replacements were dropped), and "not the
re-time" above was wrong; the re-time has been removed.*

**Bass beat 82 / beat 100** (ruling 12-4). Re-measured (`baseline_seed33_s12m_attack_bass_new`, 0 stalls): outside mean
+0.31, mean |e| 1.11 ms; one note over 5 ms, beat 100 at +23.15 ms (beat 82 was 0.1 ms here). Attribution: not re-timed
(`.retimes.csv` empty), scheduled +1.83 ms from the anchored beat, anchor 99 / 100 callbacks 0 / 8 ms late, group delay
start = start - 1 block as designed, and the voice's channel position puts its start 37 samples (0.8 ms) after the target
(beat 98: -74 samples). In the audio the bass envelope starts rising at the click at beats 98, 100 and 102 alike; the
30 % detector threshold is set by a later envelope peak that differs per note. The outliers are the bass detector's,
not the note timing (the hat / kick / open hat detectors have no such outliers). Bass max <= 5 ms is therefore not
demonstrated by this detector; the timing evidence (channel position) is within 1 ms.

**Kill chords on the final code** (ruling I3; `styles` seed 7, MusicMix 0 = kill chords only). The legacy timeline kill
chords no longer exist (removed in Task 6); the criterion they met is the per-style level vs style 0 within 1 dB. Before =
`styles_seed7_s12_kills_before_run2` (0 stalls; recorded at the start of this task on b8a7b70's kill behaviour and banks:
the only change at that moment was the behaviour-preserving extraction of the bus policy into `OneShotArming`). After =
`styles_seed7_s12m_kills_final` (final code c9274d8 + banks, 0 stalls, no refresh, kills 2304 started, late 0, dropped 0):

| | synthpop | latin | funk | dreamy |
|---|---:|---:|---:|---:|
| beat RMS before (dBFS) | -32.23 | -32.21 | -32.57 | -32.30 |
| beat RMS after | -32.72 | -32.20 | -33.44 | -32.24 |
| change | -0.49 | +0.01 | -0.87 | +0.06 |
| vs style 0, before / after | 0 / 0 | +0.03 / +0.52 | -0.34 / -0.72 | -0.06 / +0.48 |

Per 4-beat window: median -0.42, mean -0.32, max |diff| 1.02 (window 144, a landing). The per-style criterion still holds
(worst -0.72). The level change is the exact stop (kill chords now end on their sample stop + release, one block earlier
than with 7237b9f's offset).

### Fix round 2: the re-time removed (commit f201d0d, ruling 12-6)

The bounded re-time still lost notes: a backend handle only means a paused instance exists; the replacement still has to get
its channel group and delay, and the armed original had already been cancelled. In the three `_s12m_` takes the beat-256
core kick and hat (handles 5280 / 5281, re-timed by +277 samples with 9719 samples of lead; 5369 / 5370; 5377 / 5378) were
cancelled and their replacements dropped. Removed: the re-time, its constants, the dispatch-anchor bookkeeping, the
`NoteRetimed` diagnostics, the `.retimes.csv` writer and the "retimed" counter, and the re-time tests. A dispatched note keeps
its start when a later anchor moves the clock (`DispatchedNote_KeepsItsStart_WhenTheAnchorMoves`); tempo changes stay
`RescheduleFrom`'s job. The voice-delay and Harmony path-delay compensation (11e3b7d) stays. EditMode 157/157.

**Attack, final code** (`baseline_seed33_s12n_attack_drums_new`, seed 33, 0 stalls, no refresh; against the beat clicks):

| sound | notes | n | mean signed | mean abs | max abs |
|---|---|---:|---:|---:|---:|
| core hat | all | 119 | -1.20 ms | | 14.62 |
| core hat | outside | 107 | -1.19 | 1.86 | **14.62** |
| core hat | inside | 12 | | | 2.6 |
| core kick | all | 60 | +0.25 | | 12.81 |
| core kick | outside | 56 | +0.26 | 0.77 | **12.81** |
| core kick | inside | 4 | | | 0.4 |
| open hat (style, 1-beat lookahead) | outside | 86 | -1.42 | 1.43 | 2.74 |
| open hat | inside | 5 | | | 14.78 (beat 1.5) |

**Criterion (mean <= 2, max <= 5 ms) NOT met for the core hat and kick** (means met; max 14.6 / 12.8 ms). The outliers are
exactly the 4-beat-ahead core notes scheduled from a drifting anchor of the callback clock (`MusicConductor.PollAnchor`):

| note | scheduled from anchor | that anchor's running-mean drift (callback DSP -> value used) | measured |
|---|---|---:|---:|
| hat beat 5 | 0 (the first anchor) | +17.23 ms | +14.6 ms |
| hat / kick 52 | 47 (the beat before the jump at 48) | +12.44 ms | +11.3 / +12.8 ms |
| hat / kick 116 | 111 (the beat before the jump at 112) | +11.77 ms | +9.8 / +11.4 ms |

(ordinary anchors drift about +1.6 ms, e.g. 46 / 110). The beat before a jump and beat 0 have anchors whose poll mean runs
late (the timeline position stalls ahead of the jump; the first beat's polls lag), and a note handed over four beats ahead
carries that error; 1-beat-ahead notes (open hat, bass, the rest) are scheduled after the next anchor and do not. Carried
to the clock switch.

**Styles, final code** (Music Bed solo, bed, i3; `styles_seed7_s12n_bed_i3_legacy` / `_new`, both 0 stalls, no refresh):
windows over 1 dB: w0 -1.84; windows ending at a landing (28, 44, 140, 172, 204, 220: +1.21..+1.75); landing windows 192
-2.16, 208 -1.99, 224 -1.57; all window 0 or landing windows (ruling 12-3). Kick beats per style median 0.0 dB. New path:
pattern 3032 scheduled, **late 0**, dropped 19; kill chords late 0, dropped 17. Drops outside the first beat after a landing:
**1** (beat 193, dreamy claves Drums 75, "group never appeared": handed over at the landing 192 inside Studio's gap;
callbacks 192 / 193 were 65 / 59 ms late) - **NOT met** (dropped 0 required), Studio landing gap, carried. **The beat-256
notes survived** (no loss at or after 255). (The earlier lists of this section were made with a drop-matching script
that mapped some drops of the first beat after a landing to L+1 notes; the lists above and in fix round 1 are from the
corrected script.)


## Task 13: feature loops as scheduled one-shots (one pattern source)

The 12 feature loops are pattern clips (`FMOD/Tools/pattern_feature.py`, 6 layers = the 6 old `Feature *` tracks, one
clip per (loop, layer), condition `FeatureLoop` = loop id) on 9 new instrument events (`Trumpet`, `TrumpetSustain`,
`Whistle`, `FeatureMarimba`, `Xylo`, `Glock`, `FeaturePerc`, `ScreamLead`, `PowerChord`) that play through
`bus:/Feature/<Trumpet|Whistle|Mallets|Perc|Synth|Guitar>` (`mixer.py`: the tracks' high / low-pass filters) into
`bus:/Feature`. The whistle track's -1 dB fader is folded into its notes; the old [-30, 10] note clamp is kept (13 notes
clamped at +10, the same as on the timeline); struck / staccato notes ring `HoldSeconds` = min(audible time at that pitch,
0.6 s; glockenspiel 1.2 s), gated synth / guitar / held trumpet notes stop at the written length. `feature_loops.py` now
only writes `FeatureLoopNotes.g.cs` from that data (`git diff` empty); `feature_loops.js` is deleted (the 6 timeline
tracks stay, gated). `legacy_gate.js`: notes 8600, added 2280 (the feature tracks), second run "unchanged 8600, nothing to
do", Metadata byte-identical; the event XML gained 2280 `ParameterCondition` objects and nothing else. `instruments.js`
run 1 "added 76" (9 events, 67 sounds; no audio file imported), run 2 all 0; `mixer.js` run 1 "updated 8, added 8", run 2
all 0; both byte-identical on the second run. `patterns.py --compare-legacy`: unexplained 0 (new rules: the FeatureLoop
condition, the old placement's "no onset shift on a phrase's first beat", HoldSeconds 1644 notes). Max Instances (Task 12
rule, with HoldSeconds): Trumpet 12, TrumpetSustain 5, Whistle 8, FeatureMarimba 10, Xylo 8, Glock 14, FeaturePerc 13,
ScreamLead 8, PowerChord 6. `ArrangementState.FeatureLoop` = `ArrangementTimeline.FeatureLoopAt` (the loop of the step's
phrase, 0 over fever and its build-up bar); the legacy `FeatureLoop` parameter rule is
`ArrangementTimeline.FeatureLoopParameter` (same behaviour as before). **`legacyLayers` stays true.**

New `CaptureScenario` options: scenario `features` (phrase k plays loop k for k = 1..12, phrase 0 none, no kills; 208
beats), `soloLayers` `feature` (bus:/Feature alone), `meterFeatureDuck` (input / output RMS of the Feature-keyed Harmony
compressor per mixer block -> `.duck.csv`). All takes: Editor, seed 7, `logOneShots`, legacy and new in the same session
on the same code. In the first takes (`_attack_*`, `_solo_legacy`, `_solo_new`) the scenario let SoundManager's first
`Advance` promote loop 1 into phrase 0 for one frame (legacy: loop 1's beat-0 note at beat 0; new: three notes scheduled
and cancelled); fixed before the `_b` and `_duck` takes; phrase 0 is in no measurement.

### Timing (ruling 1: the legacy Feature path is one block late)

Per-note cross-correlation of each new take against the legacy take, both on their own 1 kHz beat-click grid
(`spikeRefClicks`, Feature solo; band 1.5-8 kHz, guitar 150-600 Hz; correlation peak >= 0.8 unless stated; a phrase's
first-beat notes get the intended onset move added back, since the timeline did not start them early). Inside = beats
[L, L + 2) of a jump landing. *(Correction, follow-up below: these first figures were computed with the Spike ref click
still in the mix. The click is as loud as the notes in every band used, so notes on whole beats were partly matched on the
click. The follow-up subtracts the click first; its figures replace these: path 0 -22.36 ms, final -1.14 / -1.30 ms
mean; max NOT met by correlation (beat 110: 5.26 / 16.79 ms; fix round 1).)*

| new take (vs `features_seed7_s13_attack_legacy`), click not removed (superseded) | notes | n | mean | mean abs | max abs |
|---|---|---:|---:|---:|---:|
| `_attack_new_path0` (no path delay, peak >= 0.6) | outside | 184 | -20.82 ms | 20.82 | 34.24 |
| `_attack_new` (Feature path delay 1 block) | all | 160 | -0.94 | 1.19 | 6.54 |
| same | outside | 156 | -0.97 | 1.21 | 6.54 |
| same | inside | 4 | +0.07 | 0.35 | 0.69 |
| same, peak >= 0.6 | outside | 190 | -0.74 | 1.44 | 13.71 |

- The legacy Feature tracks sound one mixer block late, like the legacy Harmony layers (the event's Feature group reaches
  the mixer through its send to `return:/Feature Duck`): one-shots without a path delay came 20.8 ms (mean, 184 notes;
  22.36 ms over 190 notes with the click removed, follow-up) before them. `instruments.PATH_DELAY_BLOCKS` now has `Feature: 1` (every `bus:/Feature/*`). **Absolute latency**: both
  paths about one block (21.3 ms) after the beat, if the path-0 one-shots are on the beat as the Task 12 spike clicks showed
  on five other buses; bus:/Feature itself was not spike-measured, and an absolute envelope detector on this music is
  unusable (mean |e| 14.6 ms on the legacy take): not measured directly.
- *(Superseded by the follow-up.)* Criterion (match legacy: mean <= 2, max <= 5 ms): first read as mean met, max NOT met:
  one note over 5 ms at peak >= 0.8, beat 110 (loop 6, trumpet G5 + whistle G6, +6.54 ms, peak 0.89). Six more notes over
  5 ms at peak 0.6-0.8 (up to 13.7 ms: trumpet beats 17, 85, 105, 111, whistle 53). The follow-up shows the whole-beat ones
  (17, 53, 85, 105) were the click. Beats 110 / 111 stay over 5 ms by correlation: **max NOT met** (fix round 1).
- Drift of the scheduled feature notes against their anchored beat: median +1.02 ms, p5 -0.35, p95 +2.37 (288 notes).

### Level per loop (ruling 2: Feature solo, legacy vs new)

Loop span = its bars plus one beat; "without" leaves out [L, L + 1) of a landing and the beat before a phrase boundary
(the legacy parameter's 0.25 s lead).

| loop | pair a (`_solo_legacy` / `_solo_new`) all / without | pair b (`_solo_legacy_b` / `_solo_new_b`) all / without |
|---|---|---|
| 1 Trumpet Fanfare | +0.02 / +0.02 | +0.07 / +0.07 |
| 2 Trumpet Rise | +0.37 / -0.01 | +0.85 / -0.00 |
| 3 Whistle Hook | -0.06 / -0.00 | +0.03 / +0.00 |
| 4 Whistle Skip | +0.18 / +0.00 | +0.45 / +0.00 |
| 5 Brass Solo | +0.04 / -0.00 | +0.17 / -0.05 |
| 6 Call and Response | -0.10 / -0.08 | -0.14 / -0.11 |
| 7 Latin Groove | -0.21 / +0.04 | -0.18 / +0.03 |
| 8 Marimba Run | +0.12 / +0.00 | +0.15 / +0.03 |
| 9 Glock Sparkle | +0.00 / +0.00 | +0.01 / +0.01 |
| 10 Synth Hook | -0.40 / +0.06 | -0.51 / +0.07 |
| 11 Power Chord Hits | +0.05 / +0.06 | +0.06 / +0.07 |
| 12 Mallet Hook | +0.00 / +0.00 | -0.00 / -0.00 |

Every loop within 1 dB over all its beats (worst +0.85, loop 2 pair b, whose span ends at the landing 48); without the
accepted windows worst 0.11 dB. *(Corrected in the follow-up: the first window list skipped windows where the new take is
digitally silent.)* 4-beat windows over 1 dB, pair b: 28, 32, 60, 64, 128, 144, 156, 172, 176. Windows 28 / 60 and the
loud beat of 128 / 172-176 hold notes of the next loop that the legacy path plays at the end of the previous phrase
(identified in the follow-up); 32 / 64 / 144 / 156 and the rest are reverb tails at -60 dBFS and below next to a landing,
whose loud beat is the logged beat L-1 (it holds the landing gap). All inside accepted windows (landing / phrase-end
lead).

### Harmony duck by the Feature key (`features`, intensity 3, full mix, `meterFeatureDuck`)

Mean gain reduction of the Feature-keyed compressor on bus:/Music Bed/Harmony per loop span, legacy (`_duck_i3_legacy`)
vs new (`_duck_i3_new`): within 0.33 dB for 11 loops over all their beats; loop 2 -1.07 dB over all beats (its span ends
at the landing 48), 0.00 without the accepted windows; worst without accepted windows 0.60 dB (loop 3). Means -5.5 ..
-10.4 dB per loop on both paths. No earlier Feature-duck reference exists (Task 8 measured the kill duck with bus:/Feature
muted), so this is legacy vs new only.

### Takes (all 0 stalls)

| take | path | Studio avg / max % | Core dsp avg % | pattern sched / started / late / dropped |
|---|---|---|---:|---|
| `features_seed7_s13_attack_legacy` | legacy | 8.79 / 87.43 | 1.22 | - |
| `features_seed7_s13_attack_new_path0` (superseded) | new | 8.15 / 87.91 | 1.30 | 2124 / 2116 / 0 / 2 |
| `features_seed7_s13_attack_new` | new | 7.61 / 85.03 | 1.30 | 2124 / 2116 / 0 / 0 |
| `features_seed7_s13_solo_legacy` | legacy | 7.04 / 82.92 | 1.12 | - |
| `features_seed7_s13_solo_new` | new | 7.62 / 84.59 | 1.25 | 2124 / 2116 / 0 / 0 |
| `features_seed7_s13_solo_legacy_b` | legacy | 9.10 / 94.80 | 1.13 | - |
| `features_seed7_s13_solo_new_b` | new | 7.84 / 90.17 | 1.37 | 2108 / 2103 / 0 / 0 |
| `features_seed7_s13_duck_i3_legacy` | legacy | 9.57 / 88.59 | 1.68 | - |
| `features_seed7_s13_duck_i3_new` | new | 9.24 / 86.10 | 2.08 | 2724 / 2722 / 0 / 5 |

Drops (ruling 3), every one: `_attack_new_path0` beat 0.31 x2 (Tension sub, Tension acid: bed layers, muted in that
solo), "group never appeared", in the first beat after the music start (not a jump landing: **NOT met** in that superseded
take; first-anchor condition, carried); `_duck_i3_new` beat 80.56 x5 (Tension sub, two notes on bus:/Music Bed/Layers, Tension acid,
Bass; 1 "group never appeared", 4 "start reached before the delay"), the first beat after the landing 80 (allowed). No
feature note was late or dropped in any take; late 0 everywhere. Landing callbacks came 126-827 ms late (`_solo_legacy_b`
the slowest, 500-830 ms, Studio max 94.8 %).

### Task 13 follow-up: the next loop's notes at the end of the previous phrase; the beat-110 outlier

**A. Legacy plays the next loop's last notes at the end of the phrase before it (identified).** The legacy path writes
`FeatureLoop` = next loop about 0.25 s before the boundary (`.params.csv`: 31.56, 63.54, 79.55, ... in every legacy
take). The timeline has every loop at every phrase position, so a note of the next loop whose region spans that write
is triggered in the middle of its region, inside the previous phrase. Beat 63 is loop 4 "Whistle Skip", `W(15.5, F6)`
(MIDI 89, 1397 Hz, region 63.47-63.97). Its sound starts at beat 63.69 in `_solo_legacy` and `_solo_legacy_b`. In
`_solo_legacy_b` the F6 band reads -47.7 dBFS in [63.54, 64); the new take is digitally silent there. The new path
schedules no feature note there (`.patterns.csv`): `ArrangementState.FeatureLoop` switches on the boundary.

Every early write in `_solo_legacy_b` against `_solo_new_b` (band = the note's fundamental +-3 %, over [write, boundary)):

| boundary | next loop | its notes whose region spans the write | legacy / new (dBFS, note band) | heard on the legacy path |
|---|---|---|---|---|
| 16, 48, 144, 192 | 1, 3, 9, 12 | none | - | - |
| 32 | 2 Trumpet Rise | stabs at 15.0 / 15.5 (B4 D5 G5) | -50..-57 / silent | **yes** (from 31.69) |
| 64 | 4 Whistle Skip | whistle F6 at 15.5 | -47.7 / silent | **yes** (from 63.69) |
| 80 | 5 Brass Solo | trumpet stab 15.0, whistle B6 15.5 | -85..-115 / -47..-102 | no (legacy window -77 dBFS vs new -39: legacy quieter; not explained) |
| 96 | 6 Call and Response | stabs 15.0 / 15.5, whistle G6 | -82..-104 / -48..-83 | no (same as 80: legacy -71 vs new -38) |
| 112 | 7 Latin Groove | conga / tumba 15.0-15.75 | percussion, no pitch test; whole window -32.5 / -32.5 | not determined |
| 128 | 8 Marimba Run | marimba 15.0-15.75, xylo 15.5 | B6 -53.5 / -97.8, G6 -72.3 / -100.9 | **yes** |
| 160 | 10 Synth Hook | synth D5 15.5 | -47.7 / -73.6 | **yes** |
| 176 | 11 Power Chord Hits | guitar G2 15.5 | -54.6 / -88.1 | **yes** |

This is a legacy-path defect that the new path removes: in the game a loop never follows a loop, so on the timeline a loop
with notes in its last half beat (2, 4, 5, 6, 7, 8, 10, 11) sounded a late fragment of its ending in the phrase before it.
In these takes it is heard at the boundaries 32, 64, 128, 160 and 176 (`_solo_legacy`, `_attack_legacy` show the same at
32 and 64). Boundaries 80 and 96 (jump landings) show nothing of it, and their legacy window is quieter than the new one;
not explained. All of these windows are the accepted phrase-end lead windows; the per-loop figures above leave them out.

**B. Beats 110 / 111: max NOT met by correlation (5.26 / 16.79 ms at 110); trumpet onset within 1.5 ms on a band
detector** (fix round 1 below replaces the earlier "met" reading).
- **Click.** The first correlation figures had the Spike ref click in the mix. The click is as loud as the notes in every
  band used (-50 dBFS in 1.5-8 kHz, like a synth note), so notes on whole beats were partly matched on the click.
  `featxcorr.py` now subtracts it: template from beats 2-14, window -20..+150 ms, least squares per click. Clicks on
  silent beats then cancel completely.
- **New path at 110.** In `_attack_new`, the anchored beats 108-112 are -0.42..+0.29 ms from the click grid. The scheduled
  starts are +0.90..+1.27 ms from it at 108-111, and beat 110 is +1.27 ms, the same as its neighbours. In `_attack_new_path0`
  it is +0.73 ms. Nothing in the scheduling of that note stands out.
- **Legacy vs legacy.** `_attack_legacy` vs a second legacy take with clicks, `_s13f_attack_legacy`: 199 notes, mean 0.00,
  max 0.02 ms. The timeline is sample-identical from take to take, so a 5 ms criterion is resolvable against legacy.
- **Second new take.** Only one legacy click take existed, so one pair was recorded: `features_seed7_s13f_attack_legacy` /
  `_s13f_attack_new`, final code (with the tooltip), 0 stalls, Studio 7.17 / 83.90 and 7.82 / 86.47 %, pattern 2108 / 2103 /
  late 0 / dropped 0.

| pair (click removed, peak >= 0.8, outside) | n | mean | mean abs | max abs | max without 110 / 111 |
|---|---:|---:|---:|---:|---:|
| legacy vs legacy (`_attack_legacy` / `_s13f_attack_legacy`) | 194 | 0.00 | 0.00 | 0.00 | 0.00 |
| new vs new (`_attack_new` / `_s13f_attack_new`) | 189 | +0.06 | 0.65 | 10.29 (110) | 2.64 |
| path 0 vs final new (minus one block) | 191 | -0.21 | 0.80 | 16.73 (110) | 8.40 (glock 148.75, peak 0.91) |
| new vs legacy, first pair | 187 | -1.14 | 1.33 | 5.26 (110) | 3.48 |
| new vs legacy, second pair (`_s13f_`) | 188 | -1.30 | 1.36 | 16.79 (110) | 4.52 (glock 149, peak 0.89) |
| path 0 vs legacy | 190 | -22.36 | 22.36 | 34.24 | |

- **Beats 110 and 111 are octave doublings.** Beat 110 is trumpet G5 + whistle G6; beat 111 is a trumpet stab with F5 +
  whistle F6. The trumpet's second harmonic is the whistle's fundamental, so the summed waveform depends on the
  sub-millisecond offset between the two one-shots; fix round 1 shows that offset per take.
- **Trumpet band** (-20 dB onset crossing, fix round 1 gives the detector and its control set):
  - beat 110 (700-900 Hz): legacy 14.5 / 14.5 ms, new 13.0 / 14.0 / 13.5 (path 0, block removed);
  - beat 111 (450-750 Hz): legacy 13.5 / 13.5, new 13.5 / 12.0;
  - the trumpet is within 1.5 ms of legacy on this detector.
- **Peak 0.6-0.8 outliers.** With the click removed, beats 17, 53, 85 and 105 are within 2 ms. Beat 192 (-6.75 / -7.15 ms,
  peak 0.67) is a phrase's first beat with xylo (onset 3 ms) and muted cowbell (onset 9 ms) together. The add-back uses
  the xylo's onset, so the cowbell's intended move (about -6 ms) remains. New vs new at 192 is -0.4 ms (peak 1.0).
- **Criterion against legacy, by the task's method** (per-note correlation, click removed, peak >= 0.8): mean <= 2 ms
  met (-1.14 / -1.30); **max <= 5 ms NOT met**: beat 110 5.26 ms (first pair) and 16.79 ms (second pair). Beat 111 is
  below peak 0.8 in both pairs (-13.71 ms at peak 0.71, +3.31 ms at 0.77). Every other note is within 4.52 ms.
- **Still open.** The new path's take-to-take spread has one more note over 5 ms, glock 148.75 (+8.40 ms path 0 vs final,
  peak 0.91). It is +1.06 ms in new vs new and +0.03 ms vs legacy (second pair), so the outlier is in the path-0 take only.
  Not explained. The absolute latency on bus:/Feature is still inferred, not measured.

### Task 13 fix round 1: beats 110 / 111 (start offsets, whistle band, detector control)

**Verdict.** By the task's method the max-attack criterion against legacy is **NOT met**: beat 110 is 5.26 ms (first
pair) and 16.79 ms (second pair); beat 111 stays under peak 0.8. A band onset detector puts the trumpet within 1.5 ms of
legacy at both beats and the whistle within 1.5 ms except beat 111 in the first pair (+5.5 ms). That detector disagrees
with the correlation by up to 4.4 ms (and once 18 ms) on ordinary notes, so it cannot show <= 5 ms by itself.

**1. Start offsets of the stacked one-shots** (`stackstarts.py`, `dispatchstarts.py`)

- Both events have voice delay 1 block and path delay 1 block (`bus:/Feature/*`). So the intended start offset between
  two notes of one beat is their onset difference alone. At beat 110: trumpet G5 2.67 ms, whistle G6 20.20 ms, intended
  841 samples.
- At scheduling (`.patterns.csv` `start_dsp`), every pair is on its intended offset within 1 sample in all three new takes
  (beat 110: 842 samples in each).
- The backend start, however, is recomputed from the clock in the frame each note is dispatched
  (`OneShotScheduler.Times`, `.oneshots.csv` `start`). The whistle, with the larger onset, is dispatched some frames before
  the trumpet (`_attack_new` beat 110: frames 15922 and 15928). The clock moves between those frames.

Trumpet - whistle start at dispatch, minus the intended offset (samples):

| take | 108 (G4 + G5) | 109 (B4 + B5) | 110 (G5 + G6) | 111 (stab + F6) | 136 (marimba + xylo) | 192 (xylo + cowbell) |
|---|---:|---:|---:|---:|---:|---:|
| `_attack_new` | -3 | 0 | -6 | +14 | +1 | 0 |
| `_solo_new_b` | -4 | 0 | +7 | +12 | +1 | 0 |
| `_s13f_attack_new` | +14 | 0 | +13 | +9 | +1 | 0 |

- **Scheduling finding.** Stacked one-shots of one beat that are dispatched in different frames get starts from
  different clock states. Their relative offset then deviates from the intended one by up to 14 samples (0.29 ms) here,
  differently in each take. Each note's scheduled beat is +0.4..+1.8 ms from its anchored beat, the usual range.
- This is the per-take phase difference behind beats 110 / 111. At G6 (period 30.6 samples), a 6-13 sample change is a
  70-150 degree phase change of the doubled partial, so the summed waveform changes per take. The legacy timeline does
  not change (legacy vs legacy 0.00 ms).
- Not changed in code (no re-timing; reported for the clock-switch work).

**2. Whistle on its own band** (`bandonset.py ... notes`)

- Detector: click removed, 4th-order band-pass, 2 ms RMS envelope, sampled every 0.5 ms, onset = first step >= max -
  20 dB. Resolution: 0.5 ms steps, 2 ms window.
- The whistle bands also hold the trumpet's second harmonic, which starts 15-18 ms after the whistle sample.

| beat, sound, band | first pair new - legacy | second pair new - legacy |
|---|---:|---:|
| 110 trumpet G5, 700-900 Hz | -1.5 ms | -0.5 ms |
| 110 whistle G6, 1500-1650 Hz | -1.5 | +1.5 |
| 111 trumpet B4 / D5 / F5, 450-750 Hz | 0.0 | -1.5 |
| 111 whistle F6, 1330-1470 Hz | **+5.5** | +1.5 |

**3. Control set: the band detector against the correlation** (`bandonset.py ... control`)

- Set: 16 notes per pair, alone on their beat and not percussion. Each has correlation peak >= 0.95 and correlation lag
  within 2 ms. The band is the fundamental +-12 %.
- Band detector minus correlation, first pair: mean +1.34, mean |.| 1.77, max 17.98 ms (trumpet beat 46.5; next largest
  1.73).
- Second pair: mean +0.24, mean |.| 0.78, max 4.38 ms (synth 165.5).
- So the band detector agrees with the correlation to about 1-2 ms on typical notes, with single errors of 4-18 ms.

**Result.** The trumpet and whistle onsets at 110 / 111 are within 1.5 ms of legacy on the band detector in seven of
eight readings; the first pair's 111 whistle reads +5.5 ms. Given the detector's own control errors, these readings do not
establish max <= 5 ms. The correlation says **NOT met**.

## Task 14: fever layers as scheduled one-shots (data extracted from the hand-made tracks)

The eight fever tracks (`Fever lead`, `shaker`, `bass fifths`, `build roll`, `build riser`, `impact`, `outro fall`,
`outro snare`) were hand-made on the timeline, so no generator held them. `FMOD/Tools/extract_fever.py` (one-time, reads
the event XML) compared every copy (4 sections x 8 bars, in beats: start, length, sample, pitch, volume, condition,
ADSR): all equal. Notes per track = per pattern x copies: lead 20 x 8 = 160, shaker 8 x 32 = 256, bass fifths 8 x 8 = 64,
roll 12 x 32 = 384, riser 32 x 8 = 256, impact 3 x 32 = 96, fall 32 x 8 = 256, outro snare 8 x 32 = 256 (= the inventory).
`pattern_fever.py` holds the printed data and is the source from now on: 8 layers (= the tracks), one clip each,
condition by fever phase (`FeverBuild` = Build, `FeverImpact` = Impact, `Fever` = Impact + Fever + Outro, `FeverOutro` =
Outro). No track had effects or ADSR: lead / riser / fall play on `bus:/Music Bed/Harmony` (the event's Harmony group;
path delay 1 block like every Harmony layer), the rest on `bus:/Music Bed/Layers`. Timeline lengths kept (5 ms stop
fade), track faders folded into the notes. The snare rolls pitch the snare by 4/11 and 4/7 semitone steps: the fever
percussion is one event (`FeverDrums`) whose table maps each Note to (sample, pitch) directly (`samples.pick` with a dict).
New events `FeverLead`, `FeverPluck` (Harmony), `FeverDrums`, `Bass` (Music Bed/Layers); no audio file imported. Max
Instances (Task 12 rule): FeverDrums 15, FeverLead 6, FeverPluck 12, Bass on Music Bed/Layers 5.
`instruments.js` run 1 "added 57", run 2 all 0; `legacy_gate.js` run 1 "notes 10328, added 1728", run 2 "nothing to do";
Metadata byte-identical on both second runs; the event XML gained 1728 conditions and nothing else (patience diff:
additions only). `patterns.py --compare-legacy`: unexplained 0. `ArrangementState.Fever` was already a step function
(`ArrangementTimeline`, pinned by a new test); `QueueFever` already invalidates both schedulers from the build-up bar's
first step (`RefreshArrangement`). **`legacyLayers` stays true.**

New `CaptureScenario` scenario `fever`: `QueueFever()` at `feverAtBeat` (24) and every `feverEveryBeats` (64) after it,
one kill chord per colour on every beat unless `feverWithoutKills`. All takes: Editor, seed 7, 144 beats (the first 128),
`spikeRefClicks`, `logOneShots`; legacy and new in the same session. Levels are energy per beat on each take's own
1 kHz click grid (the beat log is stamped late at jump landings).

### Takes

| take | path | kills / solo | stalls | Studio avg / max % | Core dsp avg % | pattern sched / started / late / dropped |
|---|---|---|---:|---|---:|---|
| `fever_seed7_s14_legacy` (128 beats, **invalid**) | legacy | kills | 2 | 6.79 / 87.34 | 2.53 | - |
| `fever_seed7_s14_legacy_b` | legacy | kills | 0 | 6.80 / 83.19 | 2.55 | - |
| `fever_seed7_s14_new` | new | kills | 0 | 7.66 / 88.34 | 2.80 | 1449 / 1447 / 0 / 0 |
| `fever_seed7_s14_bed_legacy` | legacy | no kills, Music Bed solo | 0 | 6.90 / 86.29 | 0.92 | - |
| `fever_seed7_s14_bed_new` | new | no kills, Music Bed solo | 0 | 7.29 / 83.19 | 1.15 | 1449 / 1447 / 0 / 0 |
| `fever_seed7_s14_bed_legacy_b` | legacy | no kills, Music Bed solo | 0 | 6.61 / 83.85 | 0.90 | - |
| `fever_seed7_s14_bed_new_b` | new | no kills, Music Bed solo | 0 | 6.39 / 82.15 | 1.15 | 1449 / 1447 / 0 / 0 |
| `fever_seed7_s14_kills48_new` (fever every 48) | new | kills | 0 | 8.33 / 79.06 | 2.85 | 1482 / 1480 / 0 / 0 |
| `fever_seed7_s14_kills48_legacy` (fever every 48) | legacy | kills | 0 | 7.37 / 75.48 | 2.62 | - |
| `fever_seed7_s14_late25_new` (QueueFever at 25, 73, 121) | new | kills | 0 | 7.61 / 75.29 | 2.81 | 1492 / 1489 / 0 / 0 |
| `fever_seed7_s14_probe_legacy` (52 beats; leaf-index guess, wrong) | legacy | no kills, 8 leaves kept | 0 | 6.45 / 75.24 | 0.58 | - |
| `fever_seed7_s14_probe_legacy_run2` (52 beats; leaf meters, nothing muted) | legacy | no kills, Music Bed solo | 0 | 5.17 / 65.22 | 0.92 | - |
| `fever_seed7_s14_feveronly_legacy` (fix round 1) | legacy | no kills, fever tracks only | 0 | 6.68 / 86.19 | 0.65 | - |
| `fever_seed7_s14_feveronly_new` (fix round 1) | new | no kills, fever layers only | 0 | 6.08 / 80.26 | 0.61 | 198 / 198 / 0 / 0 |

Columns (from `.cpu.txt`): sched = `PatternScheduler.NoteScheduled` events (a note re-stated after an `Invalidate` counts
again); started = pattern instances created in FMOD (`FmodOneShotBackend` starts minus Kill Chords starts), including notes
cancelled afterwards before they sounded; late = scheduler late; dropped = backend drops. Per take, sched = started +
notes never handed over (follow-up A).

Fevers: 32 and 112 (the QueueFever at 88 landed one phrase later, at 112, by SoundManager's own rule); `kills48` /
`late25`: 32, 80, 128. No note late or dropped in any new take (`.losses.csv` empty). Scheduled minus started (2-3 per
take) are notes still waiting when the take ended (follow-up A). Core dsp at most 2.85 % (heavy budget 12 %). The first legacy take had two frame stalls (833 /
384 ms at beats 88-89) and is not used.

### Level per phase (legacy vs new, dB)

Phases of fever F: build [F-4, F), impact [F, F+1), fever [F+1, F+12), outro [F+12, F+16). "Without" leaves out the
accepted windows: the landing beat [L, L+1) and the beat before a phrase boundary (the legacy parameters' 0.25 s lead).

| pair | F | build all / without | impact | fever | outro all / without |
|---|---:|---|---|---|---|
| full mix `_legacy_b` / `_new` | 32 | +2.34 / -0.08 | -8.62 (landing, accepted) | -0.20 | -0.26 / -0.23 |
| | 112 | +0.31 / -0.04 | -0.01 | -0.12 | -0.05 / -0.05 |
| bed `_bed_legacy` / `_bed_new` | 32 | +4.84 / +0.03 | -10.83 (landing, accepted) | +0.16 | +0.12 / +0.12 |
| | 112 | +0.90 / +0.07 | -0.10 | +0.17 | +0.22 / +0.22 |
| bed `_bed_legacy_b` / `_bed_new_b` | 32 | +4.82 / +0.02 | -10.92 (landing, accepted) | +0.16 | +0.17 / +0.11 |
| | 112 | +0.78 / +0.05 | +0.01 | +0.16 | -0.13 / +0.21 |
| full mix `_kills48_legacy` / `_kills48_new` | 32 | +2.20 / -0.11 | -8.59 (landing, accepted) | -0.14 | -0.23 / -0.23 |
| | 80 | +0.23 / -0.15 | -0.05 | -0.00 | -0.04 / -0.04 |
| | 128 | +0.28 / -0.00 | -0.03 | +0.00 | -0.11 / -0.11 |

- Without the accepted windows every phase in the table is within 0.23 dB (the tail beat F+16, not in the table, up to
  0.27 dB; criterion +-1 dB: **met**). Controls with the same script: legacy vs legacy 0.01 dB, new vs new 0.10 dB.
- **Impact at the landing 32** (accepted window): the first fever starts at a jump landing. On the click grid the legacy
  impact is heard about 350 ms after beat 32's position on the grid (one beat after click 31; 25 ms RMS blocks: -19 /
  -17 dBFS 14 blocks later), the new one at that position (-16 dBFS in its first block). The landing click 32 itself
  is missing or late on both paths (the timeline jump), so the per-beat windows around it are not reliable: the new
  impact lands in the window counted as beat 31 (+4.8 .. +5.2 dB) and the legacy one in beat 32's (-8.6 .. -10.9 dB).
  The impact at 80, 112 and 128 (no landing) matches within 0.10 dB.
- **Build bar, beat F-1** (accepted window): the legacy path writes `FeverBuild` = 0 0.25 s before the boundary
  (`.params.csv`: 31.56, 111.53), and its roll / riser band (2-8 kHz) over [111.75, 112) is -48.1 dBFS against -41.8 on
  the new path, which plays the notes of 111.75 on their step (`_bed_*`).

### Core kick in the build-up bar; kill chords in fever

- Kick band (40-90 Hz), `_kills48_*`: the bar before each build-up bar -37.7 .. -40.5 dBFS, the build-up bars
  -45.0 .. -45.9 dBFS on both paths (beats 28 / 76 / 124).
- In the takes above QueueFever came before the core scheduler reached the build-up bar. `_late25_new` queues at beat 25,
  after the core scheduler (4 beats ahead) had handed the kicks of 28, 76 and 124 to FMOD (`.patterns.csv`;
  `.oneshots.csv` handles 384, 1317, 2270: started paused, start time applied, released about 4 beats before their
  start, no check after that). The build-up bars' kick band equals the takes where no kick was scheduled (-45.1 / -45.9
  / -45.3 vs -45.2 / -45.9 / -45.3 dBFS): cancelled before they sounded.
- Kill chords (`_kills48_new`; the backend's start lines in `.oneshots.csv` now carry instrument / bus / MIDI): every
  bar of the three fever zones (build-up bar included) uses OrigRed / OrigBlue / OrigYellow (style 0); the funk bars
  64-75 right before the fever zone 76-95 use Brass / Clav / Guitar. Same in `_kills48_legacy`.

### Timing (per-note cross-correlation, Spike ref click subtracted; Task 13's `featxcorr.py` method)

*(Restated in fix round 1: the first version excluded [L, L+2) instead of the ruled [L, L+1), called the result met over
the correlatable subset only, and gave an unproven cause for the excluded notes.)*

`fevxcorr.py` (scratch, uses `featxcorr.py`'s declick / load / ref unchanged): every fever-layer note in its phase, one
measurement per (beat, layer) in the layer's band (lead 1.5-6 kHz, shaker 6-16 kHz, bass fifths 80-250 Hz, roll / snare
2-8 kHz, riser / fall 1.5-8 kHz, impact 40-120 Hz), window [-10, +70] ms. N = 170 fever notes per pair; excluded only
window 0 and [L, L+1) of a landing (none of the 170 falls there: the impact at the landing 32 has no click reference);
notes in [L+1, L+2) are counted and also listed separately. A note is measured when its correlation peak is >= 0.8.

**Fever-only pair (fix round 1).** `fever_seed7_s14_feveronly_legacy` / `_new`: no kills, Music Bed solo, only the
fever layers audible. Legacy: new `CaptureScenario` option `keepLegacyLeaves` mutes every leaf of the event's Music Bed and
Harmony groups except the 8 fever tracks (runtime indices Music Bed 4, 6, 12, 13, 14 and Harmony 4, 8, 10, identified with
`meterLegacyLeaves` in the probe take `fever_seed7_s14_probe_legacy_run2`, `.leaves.csv`; the fever-only legacy take's
meters show signal only on those 8, in their fever phases). New: `patternLayers` = the 8 fever layers.

| pair | measured (peak >= 0.8) of 170 | mean | mean abs | max abs |
|---|---:|---:|---:|---:|
| new vs legacy, fever only `_feveronly_legacy` / `_feveronly_new` | 125 | -1.50 ms | 1.52 | 3.12 (riser 110.25) |
| new vs legacy, bed `_bed_legacy` / `_bed_new` | 110 | -1.45 | 1.50 | 2.80 (bass fifths 45) |
| new vs legacy, bed `_bed_legacy_b` / `_bed_new_b` | 111 | -1.56 | 1.56 | 3.31 (roll / riser 30.75) |
| legacy vs legacy, bed | 167 | +0.00 | 0.00 | 0.02 |
| new vs new, bed | 134 | -0.12 | 0.57 | 2.02 |

- [L+1, L+2) (beats 33-33.75 after the landing 32): fever only 5 notes, 4 of them among the 125 measured (mean -0.46,
  max 0.97 ms) and the shaker at 33.25 not (peak 0.79, -0.30 ms); bed
  pairs 4 / 4 notes, max 1.22 / 1.04 ms.
- No control pair exists for the fever-only scenario (one pair was recorded); the controls are the bed takes.
- **Criterion (mean <= 2 ms, max <= 5 ms): met over the 125 measured notes of 170 in the fever-only pair** (and over
  110 / 111 in the bed pairs). **45 notes remain unmeasured** in the fever-only pair:
  - 5 have no legacy note: 47.75 (fall, outro snare, shaker) and 111.75 (roll, riser) - the legacy path switches
    `Fever` / `FeverBuild` off 0.25 s before the boundary (`.params.csv` 47.54, 111.53); peak 0.00 / 0.05.
  - 40 correlate below 0.8 (peak 0.24-0.80): 22 shaker, 8 roll / riser (beats 28, 30, 30.25, 108, 110, 110.25), 10 outro /
    lead at 46.5-46.75 and 126.5-126.75. Eight of these read more than 5 ms at their low peak: 46.5 lead / fall / snare
    +7.6 ms (peak 0.73-0.79), 126.5 lead / fall / snare +5.8 ms (0.73-0.79), 126.75 fall -6.4 ms (0.76), 118.75 shaker
    -5.6 ms (0.55). Their scheduled starts are -0.4 / -0.9 ms (46.5), -2.2 / -2.7 ms (126.5), -3.3 ms (126.75) and
    -2.6 ms (118.75) from their anchored beats (`.patterns.csv` against `.anchors.csv`; backend starts within 1.4 ms of
    those), in the range of the measured notes. Whether these notes are within 5 ms is not established.
  - Why they stay below 0.8 is not explained. The other bed layers are not the main cause: with them removed 45 notes
    remain, against 59 / 60 in the bed pairs.
- Every layer's measured notes are about 1.5 ms earlier than legacy; the sample onsets explain at most 0.8 ms of it
  (follow-up B: not explained).
- Fever-only pair levels (same script as above, without accepted windows): every phase within 0.21 dB.

### Task 14 follow-up: unstarted notes accounted; the onset lead

**A. Scheduled but never started** (`unstarted.py`, scratch). Each `.patterns.csv` row is matched to a backend start line
of `.oneshots.csv` from its own frame on: same instrument / bus / MIDI (the start lines carry them since the bed takes;
`fever_seed7_s14_new` predates that and is matched by start DSP alone, its 1296 unmatched start lines being exactly its
1296 kill one-shots), start DSP within 1200 samples (5000 where keyed; four notes at 98.5 in `_bed_new` were scheduled
in frame 14589 with the beat-97 anchor and dispatched in frame 14700 with the beat-98 anchor, 85 ms earlier, after the
landing 96: the carried anchor issue).

| take | scheduled | = handed to FMOD (started) | + cancelled before dispatch | + pending at end | + dropped | unaccounted |
|---|---:|---:|---:|---:|---:|---:|
| `fever_seed7_s14_new` | 1449 | 1447 | 0 | 2 | 0 | 0 |
| `fever_seed7_s14_bed_new` | 1449 | 1447 | 0 | 2 | 0 | 0 |
| `fever_seed7_s14_bed_new_b` | 1449 | 1447 | 0 | 2 | 0 | 0 |
| `fever_seed7_s14_kills48_new` | 1482 | 1480 | 0 | 2 | 0 | 0 |
| `fever_seed7_s14_late25_new` | 1492 | 1489 | 0 | 3 | 0 | 0 |

- Pending at end: Bass 36 and Acid 48 at beat 145 (`late25`: Bass 48, SineSub 36, Acid 36 at 145.5), scheduled at now
  143.5 / 144.0, after the last beat callback (143); the take stopped before their dispatch window.
- No note was cancelled before dispatch: no row is followed by a re-stated copy, and no start line is a second
  dispatch of one note (no `RescheduleFrom` re-dispatch in these takes). The fever queue's invalidations found nothing
  undispatched to cancel. What it cancelled were dispatched notes, which count as started: the build-up bar kicks of
  `late25` (handles 384 / 1317 / 2270, see above).
- Started also includes the notes for beats >= 144 that the core scheduler (4 beats ahead) had already handed over when
  the take ended: 16 per take (20 in `late25`). `StopMusic` stops them; the backend's "cancelled" count (13 in the bed
  takes; kill takes add the 9 kill re-voicings that the legacy takes show too) counts those that had not started
  sounding yet. Not checked note by note.

**B. The ~1.5 ms lead against legacy, per layer, against the samples' `OnsetSeconds`** (one-shots start that much before
the beat; the timeline notes start at the file start). Mean lead = new - legacy, both bed pairs (`fevxcorr.py`); mean
onset over the notes that sound in the layer's phase (riser / fall: bar 4).

| layer | lead pair 1 / pair 2 (ms) | mean `OnsetSeconds` (ms) | residual (lead + onset) |
|---|---|---:|---|
| lead | -1.50 / -1.35 | 0.00 | -1.50 / -1.35 |
| shaker | -1.44 / -1.41 | 0.00 | -1.44 / -1.41 |
| bass fifths | -2.17 / -1.96 | 0.84 | -1.33 / -1.12 |
| build roll | -1.79 / -2.15 | 0.00 | -1.79 / -2.15 |
| build riser | -2.04 / -2.53 | 0.69 | -1.35 / -1.84 |
| impact (n 1) | -1.88 / -1.19 | 0.00 | -1.88 / -1.19 |
| outro fall | -0.46 / -1.50 | 0.69 | +0.23 / -0.81 |
| outro snare | -0.46 / -1.50 | 0.00 | -0.46 / -1.50 |

**Not explained.** The onsets account for the extra lead of the bass fifths and the riser (about 0.7-0.8 ms). Every layer
still keeps a residual of about -1.1 to -2.2 ms (-0.5 / -0.8 on the outro, which has few notes), including the four
layers with onset 0 (lead, shaker, roll, outro snare). This holds on both buses (Harmony: lead / riser / fall; Music
Bed/Layers: the rest). No log here shows where the residual comes from.


## Task 15a: every layer as a one-shot (reference takes before the clock switch, CPU budget, scalability)

First takes with every migrated layer on the new path at once: kill chords, bed, style and core drums, feature loops,
fever. They are the "before" reference for the clock switch (unity-music-clock Task 7). No code changed;
**`legacyLayers` stays true** (each take chooses its path with `CaptureScenario.layers`).

Settings for every take: Editor, `logOneShots` on, the scenario's own kills, full mix (no solo), nothing else changed
unless listed. `styles`: 256 beats, `stylesWithBed`, intensity 3. `features`: 208 beats. `fever`: 144 beats,
`spikeRefClicks` (as in Task 14). `heavy` / `baseline` / `duck`: 128 beats. Standalone = Windows Development player built
the Task 5 way: a temporary copy of `AudioCapture.unity` with `layers` oneShots, `logOneShots` and `targetFps` 60,
`BuildPipeline.BuildPlayer`, the copies deleted afterwards. Output goes to `Builds/Capture/<scenario>_s15a/Docs/Captures/`.
The build changed no tracked file (`ProjectSettings` hashes were identical before and after).

**Auto refresh off during each take.** The first three baseline legacy attempts had main-thread stalls of 1.3-4 s.
`baseline_seed7_s15a_legacy` ran while a game was using the machine. In `_legacy_b` and `_legacy_c` the stalls at beats
83 and 4 come right after `Asset Pipeline Refresh ... RefreshV2(NoUpdateAssetOptions)` lines in Editor.log. Those
refreshes found nothing to import (they took 3-36 ms), no file under `Assets` had changed in the 3 hours before, and
Editor.log does not say what started them (focus regain, an `AssetDatabase.Refresh` call or an MCP `refresh_unity` call
all look alike there). The stalls at beats 1 and 20 of `_legacy_b` have no refresh next to them; their cause is not shown.
So every take from `_legacy_d` on ran with `AssetDatabase.DisallowAutoRefresh()` called just before Play mode, and a
matching `AllowAutoRefresh()` once Play mode had ended: 10 calls each, logged one by one. This was done only from the
take launcher. No preference, EditorPrefs value or project file was changed. Editor.log shows no refresh during any of
these takes, and every one of them has 0 stalls. The mitigation does not prevent a refresh that another client forces
explicitly; none happened.

### Takes

Every take is listed, invalid ones included. Counters come from `.cpu.txt`:
- sched = `PatternScheduler.NoteScheduled` events
- handed = pattern instances started in FMOD
- late = scheduler late
- dropped = backend drops

No take had a frame with Studio update ≥ 95 %.

| take | path | stalls | Studio update avg / max % | Core dsp avg / max % | pattern sched / handed / late / dropped | kill started / late / dropped |
|---|---|---:|---|---|---|---|
| `baseline_seed7_s15a_legacy` (**invalid**: external load, a game running) | legacy | 7 | 11.61 / 137.01 | 2.01 / 3.33 | - | 273 / 9 / 9 |
| `baseline_seed7_s15a_legacy_b` (**invalid**: stalls) | legacy | 3 | 7.02 / 88.45 | 1.81 / 2.98 | - | 282 / 0 / 0 |
| `baseline_seed7_s15a_legacy_c` (**invalid**: stall after a refresh) | legacy | 1 | 6.93 / 86.49 | 1.66 / 2.68 | - | 279 / 9 / 0 |
| `baseline_seed7_s15a_legacy_d` | legacy | 0 | 7.21 / 88.79 | 1.67 / 2.94 | - | 288 / 0 / 0 |
| `baseline_seed7_s15a_new` | new | 0 | 4.85 / 72.69 | 1.79 / 2.73 | 1122 / 1121 / 0 / 0 | 288 / 0 / 0 |
| `heavy_seed7_s15a_legacy` | legacy | 0 | 7.15 / 84.07 | 5.33 / 6.54 | - | 3069 / 0 / 0 |
| `heavy_seed7_s15a_new` | new | 0 | 7.31 / 84.74 | 5.56 / 11.03 | 1606 / 1605 / 0 / 0 | 3069 / 0 / 0 |
| `heavy_seed33_s15a_new` | new | 0 | 8.12 / 79.37 | 5.75 / 7.06 | 1471 / 1467 / 0 / 0 | 3069 / 0 / 0 |
| `styles_seed7_s15a_bed_i3_new` | new | 0 | 9.70 / 84.20 | 3.73 / 9.88 | 3143 / 3133 / 0 / 11 | 2304 / 0 / 0 |
| `features_seed7_s15a_new` | new | 0 | 7.14 / 79.40 | 1.62 / 2.94 | 2108 / 2103 / 0 / 0 | 0 / 0 / 0 |
| `fever_seed7_s15a_new` | new | 0 | 6.85 / 80.59 | 2.71 / 3.59 | 1449 / 1447 / 0 / 0 | 1290 / 0 / 0 |
| `fever_seed7_s15a_legacy` | legacy | 0 | 6.05 / 78.66 | 2.53 / 3.37 | - | 1296 / 0 / 0 |
| `duck_seed7_s15a_new` | new | 0 | 8.18 / 80.70 | 2.29 / 3.34 | 1547 / 1544 / 0 / 0 | 288 / 0 / 0 |
| `baseline_seed7_s15a_standalone_new` (standalone, 60 fps) | new | 0 | 4.13 / 64.13 | 1.68 / 2.74 | 1127 / 1124 / 0 / 0 | 288 / 0 / 0 |
| `heavy_seed7_s15a_standalone_new` (standalone, 60 fps) | new | 0 | 4.59 / 64.49 | 2.86 / 3.93 | 1612 / 1607 / 0 / 0 | 3069 / 0 / 0 |

- **Dropped notes.** Only `styles` dropped any: 11 pattern notes, all in the first beat after a landing.
  - One at 32.745, "start reached first".
  - Ten at 128.5 / 128.75, "group never appeared": core hat (Drums 42), snare (38), SineSub, Acid, Rubber bass and the
    FunkKeys clav 76 / 79.
  - Landing 128's beat callback was 54 ms late. That is `.anchors.csv`: callback DSP minus the clock's own time for
    the beat, which is itself late at a jump. It is a different measure from the +443 ms landing delay that `landings`
    gives at 128, which uses the beat log against the expected boundary. Every drop is inside the accepted window.
- **Late notes:** 0 in every take, pattern and kill alike.
- **Scheduled vs handed over**, accounted with Task 14's `unstarted.py`:
  - Most of the difference is notes pending at the end of the take (1-5 per take).
  - `styles` also has 16 notes cancelled before dispatch and stated again.
  - Notes handed over with a start more than 25 ms from their scheduling-time estimate:
    - baseline 4: 49.5 / 50, after landing 48.
    - heavy 7 8: 113.25-114.25, after the phrase boundary 112. 112 is not a landing here (`landings` +5 ms) and there
      is no tempo change; cause not shown.
    - styles 63: not checked note by note.
    - duck 19: 33-34, 65-65.5 and 112.75-113.5, after landings 32 / 64 / 112.
    - standalone baseline 4 (49.5 / 50) and standalone heavy 4 (49.5-50.25), after landing 48.
- **Kill chords in `fever_seed7_s15a_new`:** 1290 voices started against 1296 on legacy, with no late, drop or cancel.
  - At landing 96 the callback was 55 ms late, and the anchors of 96 and 97 are only 5424 samples apart (a beat is
    26,667 samples; `.anchors.csv`).
  - The kills of two capture beats ended up on one target beat: voicing 2, which adds one MIDI-72 voice per colour.
    The neighbouring beat got none. Net: 6 voices fewer.
  - This is the late-landing-anchor item, carried. No kill was lost by the scheduler.
- **Standalone:** both standalone takes have exactly the same landing figures (callbacks 208 / 202 / 273 ms late at
  48 / 80 / 96) and the same anchor frames. The files differ elsewhere. A frame-locked 60 fps player plausibly repeats
  itself; this was not looked into.

### CPU against the budget (plan Global Constraints)

| budget | take | value | verdict |
|---|---|---:|---|
| Studio update avg, baseline ≤ 11 % | `baseline_seed7_s15a_new` | 4.85 % (same-day legacy 7.21) | met |
| Core dsp avg, baseline ≤ 6 % | `baseline_seed7_s15a_new` | 1.79 % (legacy 1.67; first-plan Task 6 3.17) | met |
| Core dsp avg, heavy ≤ 12 % | `heavy_seed7_s15a_new` / `heavy_seed33_s15a_new` | 5.56 / 5.75 % (legacy 5.33; Task 6 7.12) | met |

- Standalone: baseline 1.68 %, heavy 2.86 %.
- The highest Core dsp maximum is 11.03 % (heavy 7, beat 123.5; all readings above 9 % are within beats 123-124). The highest Studio maximum is 88.79 % (a legacy take).
- The scenarios outside the budget's scope stay below 4 % Core avg and 10 % Studio avg.

### Level (full mix, 4-beat windows, `rms --compare`; value = new - reference, dB)

| pair | windows | mean of windows 1.. | outside ±1 dB |
|---|---:|---:|---|
| `baseline_seed7_s15a_new` vs same-day `_legacy_d` | 31 | +0.10 | w0 -1.43 (accepted), w19 beats 76-79 +1.24 (ends at landing 80, accepted) |
| `heavy_seed7_s15a_new` vs same-day `heavy_seed7_s15a_legacy` | 31 | -0.01 | w0 -1.31 (accepted) |
| `fever_seed7_s15a_new` vs `fever_seed7_s15a_legacy` (with the click) | 35 | -0.02 | w7 beats 28-31 +1.77 (ends at landing 32), w8 beat 32 -1.83 (landing); both accepted |
| Task 5 starting point: `baseline_seed7_s15a_new` vs `baseline_seed7_s5_fps60` | 31 | +0.13 | w0 -1.45 (accepted), w19 +1.03 (ends at landing 80, accepted) |
| Task 5 starting point: `baseline_seed7_s15a_legacy_d` vs `baseline_seed7_s5_fps60` | 31 | +0.03 | none (max 0.57) |
| Task 5 starting point: `heavy_seed33_s15a_new` vs `heavy_seed33_s5_fps60c` (68-beat take, 60 fps cap; 16 windows) | 16 | +0.05 | w0 -1.34 (accepted) |

- Every window outside ±1 dB is window 0 or a landing window. **Level criterion: met** for baseline 7 and heavy 7
  against the same-day legacy takes, and against the Task 5 starting point.
- The kill-chord length changed on purpose in the first plan (Task 6). The Task 5 starting point was recorded after that
  change, so it does not affect these comparisons. No other difference was explained away with it.

### Landings (`analyze_capture.py landings`; recorded, no judgement)

| take | landing: delay ms (first / next half-beat dB, one phrase earlier dB); no gaps in either take |
|---|---|
| `heavy_seed33_s15a_new` | 16: +285 (-28.8 / -30.6, -28.8); 32: +389; 48: +283; 64: +296; 80: -3; 96: +264; 112: +349 |
| `styles_seed7_s15a_bed_i3_new` | 16: +306; 32: +341; 48: +347; 64: +511; 80: +328; 96: -1; 112: +344; 128: +443 (next half -35.3 vs -32 elsewhere); 144: +367; 160: +4; 176: +332; 192: +514; 208: +350; 224: +369; 240: +1 |

### Timing (as measured; the carried items reported with numbers, none of them fixed)

**A. Dispatched start against the anchored beat grid** (log-based, `scratchpad/t15a/disp.py`).
- Method:
  1. Take every pattern start the backend got (`.oneshots.csv`).
  2. Turn it back into the beat it aims at: start + 110 samples + the note's onset.
  3. Place that on the take's anchor grid (`.anchors.csv`, linear between anchored beats).
  4. Error = distance to the nearest 1/12 or 1/16 step.
- Windows left out because the reference is unreliable there: beats [0, 2), and [L-1, L+1) around each landing (the
  landing beat's anchor is its late callback).
- This measures the scheduling clock, not the audio.

| take | n | mean | mean abs | p95 | max abs | notes > 5 ms (beats) |
|---|---:|---:|---:|---:|---:|---|
| baseline 7 | 1030 | +0.77 | 0.86 | +2.03 | **12.37** | 5: core 117 +12.4; 113.5 bass / sub / acid +11.4 |
| heavy 7 | 1471 | +1.04 | 1.09 | +2.24 | **8.19** | 2: core 5.0 +8.2 |
| heavy 33 | 1298 | +1.17 | 1.33 | +2.33 | **25.52** | 11: 111.17-111.92 (+25.5 .. -9.7), core 5.0 +5.4 |
| styles | 2761 | +1.26 | 1.50 | +3.08 | **14.34** | 68: largest 65.19 +14.3, core 68.19 +14.2, 200.5 / 200.94 -10.6 / +12.7, 97.0 +9.4, 145.x +8.3 |
| features | 1894 | +0.99 | 1.05 | +2.10 | 3.10 | 0 |
| fever | 1312 | +0.96 | 0.99 | +2.21 | 3.35 | 0 |
| duck | 1352 | +1.06 | 1.19 | +2.42 | **15.42** | 22: core 5.0 +15.4, 96.75 / 97 +8.7 / +9.0, 49 / 49.5 +7.3 / +7.1, core 52 +6.6 |
| baseline standalone | 1030 | +0.70 | 1.84 | +3.89 | **17.83** | 84, ±16-18 ms (e.g. 13.94, 55.94, 94.0) |
| heavy standalone | 1471 | +0.53 | 1.35 | +3.22 | **17.92** | 83, ±16-18 ms |

- **Criterion (mean ≤ 2 ms, max ≤ 5 ms):**
  - The mean is met in every take.
  - **Max NOT met** in baseline 7, heavy 7, heavy 33, styles, duck and both standalone takes.
  - Met in features and fever.
**A1. Every outlier above 5 ms, one by one** (fix round 1; `scratchpad/t15a/outliers.py`, full list in the task report).

Columns, for each start line in table A over 5 ms:
- **disp:** the beat at dispatch (`.studio.csv`); lead = note beat minus that beat.
- **A_d:** the anchor beat in force at the dispatch frame (`.oneshots.csv` clock lines).
- **clock change:** the note's beat extrapolated from A_d at A_d's tempo, minus the grid of refined anchors (each
  anchor's last per-frame clock value). Only meaningful where A_d and the note share a tempo and no jump lies between them.
- **cb late / ref move:** of the note's beat, from `.anchors.csv`. Callback lateness, and refined minus callback-time
  anchor (how far the reference beat moved after its callback).

Classes:
1. **Late anchor after the music start.** Dispatched from anchor 0-2, 4 beats ahead (core).
2. **Late anchor after a landing.** Dispatched from the anchor before a landing beat L during its landing gap, or from
   L's own anchor; or the beat before the jump.
3. **Anchor moved between dispatch and beat.** No landing and no tempo change. The extrapolation from the dispatch-time
   anchor accounts for the error within 0.5 ms; what moved the clock is not shown.
4. **Reference moved.** The reference beat's callback-time anchor differs from its refined value. Against the refined
   grid the note is within 5 ms.
5. **Cause not shown.**

| take | outliers (note beat: error vs table A grid) | class | evidence |
|---|---|---|---|
| baseline 7 | 113.5 bass / sub / acid +11.4 (lead 0.99); 117 core +12.4 (lead 3.99) | 3 | A_d 112, a phrase boundary: `landings` +4 ms, callback 20 ms late, 116 BPM on both sides. Clock change +11.1 / +12.0 = error vs refined grid +11.1 / +12.0. The anchors from 113 on sit 11-12 ms later than an extrapolation from 112; what moved them is not shown |
| heavy 7 | 5.0 core +8.2 (lead 3.98) | 1 | A_d 0, clock change +7.6 |
| heavy 33 | 5.0 core +5.4 (lead 3.97) | 1 | A_d 0, clock change +4.6 |
| heavy 33 | 111.17 -9.7, 111.44 +25.5, 111.75 +13.7, 111.92 -5.9 (drums, perc, bass, bells; lead 0.2-0.5) | 2 (beat before the jump at 112) | 112 is a jump (`landings` +349 ms); the callback-time anchor of 112 was refined by +46.4 ms, so neither grid is steady across the jump (refined: -18.5 / +4.5 / -21.4 ms) |
| styles | 49 / 49.5 (lead 1) +7.1 / +6.8, core 52 (lead 4) +6.4 | 2 | A_d 47, dispatched at 48.02-48.53 before the landing anchor 48 (landing +347 ms, 124 -> 132 BPM) |
| styles | core 53 +5.9 (lead 4) | 2 | A_d 48 = the landing anchor; clock change +4.7 |
| styles | 65.19 +14.3, 66.19 +7.9, core 68.19 +14.2 | 2 | A_d 63; landing 64 (+511 ms, 132 -> 108 BPM); anchor 63 moved +170 ms after dispatch |
| styles | 97 marimba / latin perc +9.4 (lead 1.01) | 3 | A_d 95; 96 is no landing (`landings` -1 ms), callbacks 96 / 97 5 / 21 ms late, 132 BPM on both sides; clock change +8.7 = error vs refined grid +8.6 |
| styles | 113.19 +5.9, core 116.19 +5.2 | 2 | A_d 111; landing 112 (+344 ms); anchor 111 moved +98 ms after dispatch |
| styles | 145 / 145.25 / 145.5 / 145.75 +8.0..+8.3 (lead 1), core 148 +7.6 | 2 | A_d 143, dispatched at 143.97-144.77 before the landing anchor 144 (+367 ms, 124 -> 132 BPM) |
| styles | 160.75 / 161 +5.9 / +6.0 (lead 1), core 164 +6.3 (lead 4) | 3 | A_d 159; 160 is no landing (+4 ms), no tempo change; clock change +4.8 / +5.0 / +4.8 = error vs refined grid |
| styles | 200.5 -10.6, 200.94 +12.7 / +12.4, 201.5 -6.1 (lead 0.4-0.5) | 4 | Mid-phrase, no landing, no tempo change. Beat 201's callback was 22 ms late, and its callback-time anchor is 19.8 ms off its refined value. Against the refined grid the notes are -2.9 / +2.2..+2.5 / +3.6 ms. Why 201's callback was late is not shown |
| styles | 209.875 -5.2 (lead 1.35) | 2 | A_d 208 = the landing anchor (+350 ms) |
| duck | 5.0 core +15.4 (lead 3.99) | 1 | A_d 0, clock change +15.2 |
| duck | 49 / 49.5 +7.3 / +7.1 (lead 1), core 52 +6.6, core 53 +6.1 | 2 | A_d 47 (52) / 48 (53); landing 48 (+308 ms, 124 -> 132 BPM) |
| duck | 96.75 / 97 +8.7 / +9.0 (lead 1) | 3 | A_d 95; 96 is no landing (`landings` -3 ms), callbacks 5 / 21 ms late, 132 BPM; clock change +7.2 = error vs refined grid +7.2 |
| baseline standalone | 37 groups at ±14-18 ms; 99.0 (read as 98.94) +15.8 / +12.6 | 4 | Callback-time anchors in the player are off their refined values by a median of 17 ms (max 20.3); the extrapolation is within 5 ms of the refined grid |
| baseline standalone | 7.0 core +5.4 (lead 3.97) | 1 | A_d 2, clock change +5.1 |
| baseline standalone | 113.5 +11.3, core 117 +12.7 | 3 | A_d 112, the same pattern as the Editor baseline |
| heavy standalone | 34 groups at ±14-18 ms | 4 | Median ref move 18 ms; extrapolation within 5 ms of the refined grid |
| heavy standalone | 49 / 49.25 +5.4 (lead 1) | 2 | A_d 47; landing 48 |
| heavy standalone | 113.31 -10.7, 113.5 / 113.75 +12.3 / +12.1, core 117 +12.7 | 3 | A_d 112; for 113.31 anchor 112 moved -9.1 ms after dispatch |

Count (distinct outlier beats):

| | class 1 | class 2 | class 3 | class 4 | class 5 |
|---|---:|---:|---:|---:|---:|
| Editor | 3 | 23 | 8 | 3 | 0 |
| standalone | 1 | 2 | 6 | 72 groups | 0 |

- The class 3 beats are all 0.5-4 beats after a phrase boundary that is not a landing (96, 112, 160). An anchor step
  of +5..+12 ms at such a boundary, with no late callback over 21 ms and no tempo change, is a finding; the logs do not
  show what causes it.
- **Against the refined grid instead of the callback-time grid** (`disp_ref.py --refined`), max abs is:

  | baseline 7 | heavy 7 | heavy 33 | styles | features | fever | duck | standalone baseline / heavy |
  |---:|---:|---:|---:|---:|---:|---:|---:|
  | 12.0 | 14.1 | 21.4 | 15.0 | 2.9 | 2.35 | 20.4 | 12.3 / 12.3 |

  The outliers then sit at the same phrase boundaries, including notes just before them (for example 111.5-112.5,
  30.5, 110.5). So max ≤ 5 ms is NOT met against either reference, outside features and fever.

**A2. Click-based figure** (fever takes, which carry the 1 kHz Spike ref click).
- **`analyze_capture.py spike`:** cannot be run on these takes. It pairs SpikeClick one-shots (3 kHz) with the click and
  needs the `.spike.csv` / `.timeline.csv` that only the `spike` scenario writes ("No such file ... .timeline.csv").
  It would need a spike take (or SpikeClick notes scheduled in the scenario), which is not part of this task.
- **`analyze_capture.py onsets --ref-band 900-1100`** (click vs attacks in a test band):

  | take | test band | paired / test onsets | mean abs | max abs | mean signed |
  |---|---|---:|---:|---:|---:|
  | `fever_seed7_s15a_new` | 40-120 Hz (core kick, bass fifths, impact) | 23 / 33 | 3.20 ms | 5.98 ms | -1.88 ms |
  | `fever_seed7_s15a_legacy` | 40-120 Hz | 22 / 27 | 2.29 ms | 5.19 ms | -2.29 ms |
  | `fever_seed7_s15a_new` | 6-16 kHz | 38 / 101 | 33.64 ms | 47.73 ms | -33.49 ms |
  | `fever_seed7_s15a_legacy` | 6-16 kHz | 63 / 127 | 25.10 ms | 46.17 ms | -23.87 ms |

  - The tool has no early / late / missing classification.
  - The low band pairs about 23 attacks of the 128+ beats. The legacy take, whose timeline drums start on the click,
    reads -2.29 ms mean / 5.19 ms max, so the detector itself is off by about 2-5 ms on this material. The new take
    differs from it by +0.41 ms mean signed.
  - The high band pairs hats and shakers with the wrong click (about -25..-34 ms), which is meaningless.
  - **Absolute criterion (mean ≤ 2 / max ≤ 5 ms): not measurable with `onsets` on these takes**, because its own
    legacy calibration does not meet the criterion. Relative to legacy: +0.4 ms.

**B. New vs legacy, per note in the audio** (`fever` pair with the click; Task 14's `fevxcorr.py`, Spike ref click
subtracted, full mix with kill chords).
- 63 of 170 fever notes correlate ≥ 0.8.
- mean -0.64 ms, mean abs 0.90, max abs 2.17 (shaker 116.25).
- Per layer: shaker -1.38, impact -1.54, the others -0.5 .. +0.1 ms.
- This is carried item 3 (one-shots about 1.1-2.2 ms ahead of the timeline), measured smaller here and over fewer
  notes. Task 14's fever-only pair, without kill chords, reached 125 of 170. That the kill chords are why fewer notes
  reach the 0.8 threshold here is not shown.

**C. Stacked notes dispatched in different frames** (carried item 4; `scratchpad/t15a/timing.py`).
- For beats whose notes on several instruments were handed over in different frames: the spread of (backend start -
  scheduling-time start).
- Spread over 24 samples on 51-110 beats per take. The largest spreads (172-1086 samples at beats 2.0 / 114.0) are
  beats where the anchor changed between the dispatch frames (after the start, after a boundary).
- This includes the anchor movement itself, so it is not the 14-sample figure of Task 13, which compared onset
  differences of the feature loops.

**D. Harmony / Feature path:** one block after the click on both paths (carried item 5, not re-measured).
**E. Drops in Studio's landing gap:** carried item 6. Here only the styles drops at 128 (see above); no kill chord was
lost.

### NOT met / carried to the clock switch

- Attack max ≤ 5 ms (log-based, table A): NOT met. Max 8.2-25.5 ms in the Editor and 17.8 / 17.9 ms in the standalone
  player. Per outlier (A1):
  - class 1 (first anchors after the start, carried item 2): 3 Editor beats, 1 standalone.
  - class 2 (landings, carried item 1): 23 / 2.
  - class 3 (anchor step at a non-landing phrase boundary, 96 / 112 / 160; cause not shown): 8 / 6.
  - class 4 (reference moved): 3 Editor (styles 200.5-201.5) and the standalone ±14-18 ms readings.
- Click-based absolute attack figure: not measurable (A2). `spike` needs a spike take, and `onsets` reads 5.19 ms max
  on the legacy calibration.
- Late-landing anchor: kill chords of two beats merged onto one beat after landing 96 (`fever` new, 6 voices fewer).
  Notes handed over more than 25 ms off their scheduling-time estimate after landings (baseline, duck, standalone) and
  after the non-landing boundary 112 (heavy 7, cause not shown). The styles values were not checked note by note.
- Stacked-note spread: carried item 4, see C.
- New vs legacy lead -0.64 ms mean (fever, full mix): carried item 3.

### Scalability check (spec 5-2; trial in the working tree only, fully reverted, nothing committed)

**What I added.** A style 4 (`TRIAL`), using existing samples only:
- `patterns.py`: `STYLE_COUNT` 5 and a fifth `STYLE_USES_CORE_BACKBEAT` entry (`True`). The style constant has to change
  too, because `pattern_bed.py` / `pattern_drums.py` assert `STYLE_COUNT == 4` and `Clip` checks its styles against it.
- `pattern_bed.py`: one harmony clip, `TrialMarimba` (the latin marimba pattern on the existing `Marimba` instrument,
  layer "Latin marimba").
- `pattern_drums.py`: one drum clip, `TrialClaves` (the dreamy claves on `Drums`, layer "Dreamy perc").
- No new pitch and no new instrument, so no new `event:/Inst/*` was needed.

**Runs:**
- `instruments.py` wrote 49 instruments / 432 notes. Of the generated files, only `PatternTable.g.cs` changed
  (+89 / -47: `StyleCount = 5`, `StyleUsesCoreBackbeat`, two clip names, two clip rows, and the note offsets of the
  following clips). `InstrumentTable.g.cs` and `instruments.js` did not change, `mixer.js` was not regenerated, and
  `bed_notes.py` left `BedNotes.g.cs` unchanged.
- `instruments.js` (CLI): "moved 0, updated 0, added 0, removed 0", saved.
- `-build` as a separate run: "Build completed successfully"; the bank timestamps did not move (nothing to build).

**Result:**
- `git status --short FMOD/ChordPulse/Metadata` was empty, and an MD5 of all 217 Metadata files was identical before
  and after. **No existing event XML changed**, the music event `{3d468f15…}.xml` included; `ChordPulse.fspro`, banks
  and the FMOD cache asset were unchanged too.
- EditMode with the trial in place: **173 / 173 passed**.

**Things a real fifth style would still need** (findings, not patched):
- `Clip`'s default `styles=(0, 1, 2, 3)` hard-codes four styles. The core kick, tension and combo layers and the feature
  loops would not play in style 4 until that default follows `STYLE_COUNT`.
- The kill-chord tables and their tests (`KillChordTable.g.cs`, `KillChordVoicerTests` / `InstrumentCoverageTests`
  loop `s < 4`) and `BedStyleVisuals.StyleCount = 4` are separate from the pattern generators.
- `SoundManager` never selects a style 4.

**Revert:**
- `patterns.py`, `pattern_bed.py`, `pattern_drums.py` and `PatternTable.g.cs` were copied back from the backup taken
  before the trial (`scratchpad/t15a/backup`). Nothing else differed, so nothing else needed restoring; the trial
  created no new file.
- Afterwards the MD5 of every file under Metadata, Build, StreamingAssets, Generated, FMOD/Tools and the FMOD cache
  equals the pre-trial list. The only exception is three git-ignored `__pycache__` files, recompiled from the restored
  sources.
- `git status --short` and `git diff --stat` are identical to before the trial and to the start of the task.
- `PatternTable.StyleCount` is 4 again, and EditMode passed **173 / 173**.

## Clock switch — Task 6 (unity-music-clock): the music runs on `MusicTransport`

From this task on `SoundManager` no longer starts `event:/Music/ChordPulseGame`. Beat 0 is set 0.25 s after the samples
are loaded, on the FMOD master DSP clock (`FmodClock`), and `MusicTransport` fixes every phrase boundary as an integer
DSP sample. Every layer is a one-shot (the `legacyLayers` path is gone). `OneShotScheduler.DefaultScheduleOffsetSamples`
is 0 (it was -110, the lag behind the timeline's beat; the next task calibrates it against this clock).

What changed in the capture files:
- `.beats.csv` is `beat,frame,bpm,dsp`. Each beat's DSP sample is computed by the clock when `MusicTransport` starts its
  phrase (a resync takes the rest of the phrase again). `frame` is `AudioCapture.FrameAtDsp(dsp)`: the capture pairs the
  master DSP clock with its own frame counter on its first mixer block, using the capture DSP's own clock. Every later
  block is checked against that pairing; `.cpu.txt` reports the mismatches as "CAPTURE dsp-to-frame mismatches" (0 in
  every take below). `analyze_capture.py` reads the extra column without any change.
- No `.anchors.csv` (no beat callbacks) and no `.timeline.csv`. Capture options removed: `layers`, `spikeRefClicks`,
  `keepLegacyLeaves`, `meterLegacyLeaves`. `spike` has a new `spikeDispatchBeats` option.

Settings: Editor, `logOneShots`, the scenario's own kills, full mix. Each take ran with
`AssetDatabase.DisallowAutoRefresh()` before Play mode and `AllowAutoRefresh()` after it, one call each per take. Editor.log
shows a refresh only at Play mode entry, before the capture started; there was none during a take.

### Takes

| take | stalls | Studio update avg / max % | Core dsp avg / max % | pattern sched / handed / late / dropped | kill started / late / dropped | cancelled | FIRSTANCHOR |
|---|---:|---|---|---|---|---:|---:|
| `baseline_seed7_s6c` (128 beats) | 0 | 0.68 / 1.20 | 1.23 / 2.59 | 1129 / 1128 / 0 / 0 | 288 / 0 / 0 | 13 | -0.41 |
| `fever_seed33_s6c` (68 beats, `QueueFever` at beat 24) | 0 | 0.83 / 1.67 | 2.07 / 3.22 | 608 / 604 / 0 / 0 | 612 / 0 / 0 | 19 | -0.41 |
| `heavy_seed7_s6c` (128 beats) | 0 | 1.39 / 2.29 | 5.11 / 6.58 | 1616 / 1614 / 0 / 0 | 3069 / 0 / 0 | 40 | -0.41 |

- Every take is listed; none was invalid. The longest frame in each (4.1 s) is the bank load before the first beat.
- ONESHOTS late 0 and dropped 0 in all three takes. `.losses.csv` is empty in all three.
- FIRSTANCHOR is the beat at the first frame with a clock: -0.41, during the start lead. In the reference takes it was
  +0.04 (`baseline_seed7_s15a_new`) and 0.00 (`heavy_seed7_s15a_new`).
- "cancelled" counts notes that were stopped before they sounded. It is not part of any criterion here. Its cause was
  not looked into note by note.

### Step 3: baseline seed 7

| criterion | value | verdict |
|---|---|---|
| every boundary delay ≤ 5 ms (`landings`) | 16 / 32 / 48 / 64 / 80 / 96 / 112: +0 / +0 / -0 / +0 / +0 / +0 / -0 ms | met |
| first half-beat RMS ±1 dB against the previous phrase, boundaries with the same arrangement | 16 / 32 / 48 / 80 / 112 (same pattern notes in the first half beat as b-16): -0.00 / +0.27 / -0.13 / -0.00 / +0.16 dB. 64 and 96 have different notes than b-16 (-1.10 / -0.30) and are not compared | met |
| no silence > 20 ms at a boundary | `landings` lists 64: 320+95 ms, 80: 320+95 ms, 112: 475+40 ms | **NOT met** (3 by `landings`: 64, 80, 112) |
| `frames`: backward 0, over 0.05 beat 0 | 17295 frames, backward 0, over 0, max deviation 0.0066 beat, longest hold 0 ms | met |
| ONESHOTS late / dropped 0 | 0 / 0 | met |

What was measured about the three silences listed by `landings` (an explanation; whether they count as rests is not
decided here):
- The same windows are silent in both reference takes of this seed. `baseline_seed7_s15a_new` has 64: 335+90, 80: 330+90
  and 112: 480+35. `baseline_seed7_s15a_legacy_d`, whose notes came from the timeline, has 112: 480+35.
- At 64 and 80 the window is also silent one phrase away (80 after 64, 64 after 80). The phrase's only notes there are
  the tension bus's Acid and SineSub, which are at -80 dB at Tension 0 (`bed_tension.CURVES`), and drum hits that start
  up to 0.3 beat before the window.
- At 112 no pattern note starts in the window.
- The analyzer's rest test (same offset after b-4) does not classify any of the three as a rest.
- These takes show that the same windows were already silent before the clock switch: all three in
  `baseline_seed7_s15a_new`, and 112 also in `baseline_seed7_s15a_legacy_d`. They do not establish a cause beyond that.

### NOT met

- Step 3, no silence > 20 ms at a boundary: 3 by `landings` (64: 320+95, 80: 320+95, 112: 475+40 ms in
  `baseline_seed7_s6c`).

### Step 4: fever seed 33

- `QueueFever` at beat 24 put fever at 32..48, with the build-up bar at 28..32.
- Without fever the section choice gives phrase 2 (beat 32) section 0 = 108 BPM. This is `SectionSelector(33)` run with the
  same arguments: phrases 0, 1, 2 are sections 0, 1, 0.
- `.beats.csv` has beats 16-47 at 116 BPM and beat 64 at 108, so **the held boundary 32 kept 116 BPM: met**.
- Beats 32-35: late 0 and dropped 0. In fact the whole take had none (`.losses.csv` is empty). **Met.**
- `landings`: 16 / 32 / 48 / 64 are +0 / -0 / -0 / -0 ms. 32 is the fever impact (+7 dB, expected). There are no gaps.
- `frames`: backward 0, over 0, max 0.0071 beat.

### Exact arithmetic (every one-shot of the take)

Script: `scratchpad/c6/exact.py`, with the onset table dumped from `InstrumentTable.OnsetSeconds`.
- The independent clock comes from beat 0's DSP sample and each phrase's BPM in `.beats.csv`:
  `boundary[n+1] = boundary[n] + round(16 * 60 / bpm[n] * 48000)`, and a beat is `round(boundary + offset * 60 / bpm * 48000)`.
- Every `.beats.csv` DSP sample equals that arithmetic, with 0 deviation for all 128 / 80 / 128 beats.
- Every start the backend received (`.oneshots.csv`), plus its sample onset, is compared with the independent sample of
  its beat. Pattern notes take their beat from `.patterns.csv`; kill notes take the nearest beat.

| take | starts | deviation 0 | -1 | +1 | max abs |
|---|---:|---:|---:|---:|---:|
| baseline 7 | 1416 | 1382 | 28 | 6 | 1 sample |
| fever 33 | 1216 | 1201 | 7 | 8 | 1 sample |
| heavy 7 | 4683 | 4610 | 66 | 7 | 1 sample |

- Every ±1 start was handed over while the clock was still anchored on an earlier phrase than the note's. This is from
  the `clock,anchor` line of the same frame in `.oneshots.csv`.
- Such a note is timed by `BeatClock`'s pending tempo, which runs on from the old anchor without rounding the boundary to
  a whole sample. EditMode `TransportSchedulingTests` reproduces it. Notes handed over after their phrase's anchor are
  exact.

### CPU against the budget

| budget | take | value | verdict |
|---|---|---:|---|
| Studio update avg, baseline ≤ 11 % | `baseline_seed7_s6c` | 0.68 % (15a new 4.85) | met |
| Core dsp avg, baseline ≤ 6 % | `baseline_seed7_s6c` | 1.23 % (15a new 1.79) | met |
| Core dsp avg, heavy ≤ 12 % | `heavy_seed7_s6c` | 5.11 % (15a new 5.56) | met |

### The carried items of Task 15a, before and after

| item | before (15a reference takes) | after (`_s6c`) |
|---|---|---|
| beat 0 after the music start | the clock's first anchor came with beat 0's callback (FIRSTANCHOR +0.04 / 0.00), so beat 0's one-shots could not be scheduled | the clock runs from -0.41 beat. Beat 0's pattern notes are scheduled (baseline 7, heavy 10) at the exact sample. In baseline, beat 0's first half beat is -31.6 dB, the same as beat 16 |
| landing delays (`landings`) | baseline 7: +213 / +237 / +353 ms at 48 / 80 / 96; heavy 7: +377 / +367 / +574 | 0 ms at every boundary of all three takes |
| anchor steps of 5-12 ms at non-landing boundaries (96, 112, 160) | class 3 outliers, e.g. baseline 113.5 / 117 +11-12 ms | no per-beat anchor any more. Every beat's sample equals the integer-boundary arithmetic (0 deviation), and every start is within 1 sample |
| drops in the first beat after a landing | styles: 11 at 32.7 and 128.5-128.75 | 0 drops in all three takes (no styles take was recorded) |
| kill chords of two beats merged onto one after a landing | `fever_seed7_s15a_new` landing 96: 6 voices fewer | fever 33: 68 of 68 beats carry 9 kill notes each. Heavy 7: every beat 1-128 has kill notes (24 each, 21 on beat 1), 3069 in total as in 15a |

## Clock-referenced accuracy and forced stalls — Task 7 (unity-music-clock)

The `spike` clicks are now measured against the clock itself. The reference is each beat's frame in `.beats.csv`
(MusicTransport's DSP sample through `AudioCapture.FrameAtDsp`); there is no reference click (`analyze_capture.py spike`,
clock mode).

New capture options, both off by default:
- `stallMs` / `stallAtBeats` put the main thread to sleep once when the visual beat passes each value.
- The frame that carries the sleep is counted as "forced stalls N" in `.cpu.txt`, not in `stalls >100 ms`. It is marked
  `forced=1` in `.frames.csv`, and `frames` leaves those frames out of its deviation and hold checks.
- Every `MusicTransport.Resynced` is logged and counted (`RESYNCED N`).
- `landings --at-beats` runs the silence check within one beat after any point.

Settings: Editor, `logOneShots`, `scheduleOffsetSamples` 0 (`DefaultScheduleOffsetSamples`, unchanged). Auto refresh was
disallowed per take, with one call before Play mode and one after.

### Takes (every take recorded in this task)

| take | unforced stalls | forced stalls | RESYNCED | ONESHOTS late / dropped | Studio avg / max % | Core dsp avg / max % | valid |
|---|---:|---|---:|---|---|---|---|
| `spike_seed33_s7c_d05` | **2** (2339 ms at beat 9.4, 1158 ms at 23.5) | - | 0 | 15 / 5 | 0.62 / 0.84 | 1.07 / 1.99 | **no** |
| `spike_seed33_s7c_d05_r2` | 0 | - | 0 | 0 / 0 | 0.57 / 0.78 | 0.93 / 1.74 | yes |
| `spike_seed33_s7c_d4_stall300` | 0 | 3 x 300 ms at 9.8 / 15.7 / 40.2 | 0 | 0 / 0 | 0.56 / 0.78 | 0.94 / 1.79 | yes |
| `heavy_seed7_s7c_stall300` | 0 | 2 x 300 ms at 30.5 / 47.7 | 0 | 7 / 18 | 1.43 / 2.35 | 5.12 / 6.99 | yes |
| `heavy_seed7_s7c_stall2000` | 0 | 1 x 2000 ms at 30.5 | 0 | 31 / 0 | 1.43 / 2.14 | 5.10 / 9.76 | yes |

- **`spike_seed33_s7c_d05` is invalid.**
  - Its two unforced stalls are on Unity frames 788 and 2433.
  - Two Editor queries (`execute_code`) were run during the take, and they returned `frameCount` 788 and 2433.
  - It was re-recorded as `_r2`. No query was made during any later take.
- `CAPTURE dsp-to-frame mismatches` is 0 in all five takes.
- `MusicTransport.Resynced` fired 0 times in all five takes, including the forced stalls.

### Detector bias, raw and corrected errors

`spike` (clock mode) finds each click with `band_onsets`: a zero-phase band-pass and envelope, with the onset at 15 % of
the peak. That detector reports a click early.
- **How the bias is measured.** On every run, `spike` places the SpikeClick waveform (`spike_click.click_samples(3000)`)
  at 16 known samples in silence and runs the same `band_onsets` on it. The result is printed as the `detector bias`
  line: **-39.0 samples = -0.812 ms**, min = max = -39.0.
- **What it affects.** Raw errors are attack frame - beat frame. Corrected errors are raw - bias. The raw mean |e|, max
  and late / early columns all carry the -0.81 ms. Judged on raw errors, the ±5 ms gate would in fact be late
  > +5.81 ms and early < -4.19 ms.
- **What the gate uses.** Mean ≤ 2, max ≤ 5, late > +5 ms, early < -5 ms and the exit code are all judged on the
  **corrected** errors.
- **Cross-check with `--xcorr`.** This gives the exact click lag in samples: the capture is cross-correlated with the
  click waveform, both band-passed 2-4 kHz, within ±10 ms of each beat frame.

Commands (Steps 1 and 2):
```
python FMOD/Tools/analyze_capture.py spike Docs/Captures/spike_seed33_s7c_d05_r2.wav --xcorr
python FMOD/Tools/analyze_capture.py spike Docs/Captures/spike_seed33_s7c_d4_stall300.wav --stall-beats 9.8,15.7,40.2 --xcorr
```

### Step 1: spike seed 33, 68 beats, `spikeDispatchBeats` 0.5 (`spike_seed33_s7c_d05_r2`)

| clicks | n | raw mean \|e\| / max / signed ms | corrected mean \|e\| / max / signed ms |
|---|---:|---|---|
| all | 64 | 0.82 / 0.85 / -0.82 | 0.02 / 0.04 / -0.01 (-0.4 samples) |
| lead 1 beat (even) | 32 | 0.83 / 0.85 / -0.83 | 0.02 / 0.04 / -0.02 |
| lead 0.3 beat (odd) | 32 | 0.81 / 0.83 / -0.81 | 0.02 / 0.04 / +0.00 |
| 4 beats after tempo change 16 | 4 | 0.81 / 0.85 / -0.81 | 0.02 / 0.04 / +0.00 |
| 4 beats after tempo change 32 | 4 | 0.80 / 0.83 / -0.80 | 0.02 / 0.02 / +0.01 |
| 4 beats after tempo change 48 | 4 | 0.82 / 0.85 / -0.82 | 0.03 / 0.04 / -0.01 |
| beat after tempo change 64 | 1 | 0.85 / 0.85 / -0.85 | 0.04 / 0.04 / -0.04 |

- Corrected: missing 0, late 0, early 0. The raw counts are also 0 / 0 / 0.
- **GATE PASS** on the corrected errors (exit 0).
- `--xcorr`: 64 of 64 clicks at lag 0 samples.
- `frames`: backward 0, over 0.05 beat 0, max 0.0066 beat, hold 0 ms.

**Calibration.**
- The corrected mean signed error is **-0.01 ms (-0.4 samples)**. That is within the ±0.5 ms trigger.
- The raw -0.82 ms is the detector bias above. Cross-correlation puts every click on its beat sample.
- On that basis `DefaultScheduleOffsetSamples` was left at 0 by controller ruling C-5.

### Step 2: spike seed 33, `spikeDispatchBeats` 4.0, `stallMs` 300 at 9.8 / 15.7 / 40.2 (`spike_seed33_s7c_d4_stall300`)

| clicks | n | raw mean \|e\| / max / signed ms | corrected mean \|e\| / max / signed ms |
|---|---:|---|---|
| all | 64 | 0.83 / 0.87 / -0.83 | 0.03 / 0.06 / -0.02 (-0.9 samples) |
| 4 beats after tempo change 16 | 4 | 0.82 / 0.85 / -0.82 | 0.03 / 0.04 / -0.01 |
| 4 beats after tempo change 32 | 4 | 0.83 / 0.87 / -0.83 | 0.04 / 0.06 / -0.02 |
| 4 beats after tempo change 48 | 4 | 0.83 / 0.85 / -0.83 | 0.04 / 0.04 / -0.02 |
| beat after tempo change 64 | 1 | 0.85 / 0.85 / -0.85 | 0.04 / 0.04 / -0.04 |
| 4 beats after the stall at 9.8 | 4 | 0.82 / 0.85 / -0.82 | 0.03 / 0.04 / -0.01 |
| 4 beats after the stall at 15.7 | 4 | 0.82 / 0.85 / -0.82 | 0.03 / 0.04 / -0.01 |
| 4 beats after the stall at 40.2 | 4 | 0.84 / 0.85 / -0.84 | 0.03 / 0.04 / -0.03 |

- **Missing 0, late 0, early 0 (corrected; raw also 0 / 0): met.**
- The 4 beats after each stall pass the accuracy check. GATE PASS (exit 0).
- `--xcorr`: 64 of 64 clicks at lag 0.
- Where the stalls were taken:

  | stall | visual beat | mixer beat | visual beat moved by the stall frame (expected) |
  |---|---:|---:|---|
  | 1 | 9.806 | 9.764 | 0.550 (0.550) |
  | 2 | 15.701 | 15.678 | 0.568 (0.590) |
  | 3 | 40.201 | 40.167 | 0.548 (0.548) |

- `frames` outside the stall frames: over 0.05 beat 0, hold 0 ms.

### Step 3: not triggered

There was no early click in Step 1 or Step 2 (early 0). Since Task 5b, `FmodOneShotBackend.Start` starts every instance
paused and unpauses it only after its delay is set. Nothing was changed, and `OneShotScheduler.cs` was not touched.

### Step 4: heavy seed 7, `stallMs` 300 at 30.5 / 47.7 (`heavy_seed7_s7c_stall300`)

| criterion | value | verdict |
|---|---|---|
| no silence > 20 ms within one beat after a stall | `landings --at-beats 30.5,31.0,47.7,48.25` (the stall points, and the mixer beat where each stall ended): 0 runs at all four points | met |
| late 0, early 0 | see the list below the table | met |
| boundaries (`landings`) | delay 0 at every boundary 16..112, no gaps | - |
| `frames` outside the 2 forced-stall frames | backward 0, over 0.05 beat 0, max 0.0111 beat, hold 0 ms | - |

How late 0 / early 0 was established:
- Every note handed to the backend had its start applied at least 4630 samples ahead; 0 were applied at or after their
  start.
- Every backend start + onset is on the independent beat sample: 4628 at 0, 41 at -1 and 7 at +1 sample.
- The core kick, cross-correlated with its sample, is at the same lag as in `heavy_seed7_s6c` on all 64 kick beats (max
  difference 1 sample).

Losses (`.losses.csv`). All are on beat 31, the first beat after the stall at 30.5:
- The whole beat-31 kill chord, 24 notes: 6 late in the scheduler, and 18 dropped by the backend because the stall ended
  after their start.
- One pattern note, Acid 31.0 on TensionAcid, late in the scheduler. At Tension 0.8 it is audible.
- The stall at 47.7 lost nothing.

Step 4 has no criterion on missing notes, so these losses are recorded here and not judged.

### Step 5: heavy seed 7, `stallMs` 2000 at 30.5 (`heavy_seed7_s7c_stall2000`)

| criterion | value | verdict |
|---|---|---|
| late 0, early 0 (missing allowed) | every note handed over had its start applied at least 3715 samples ahead; 0 at or after it. Backend starts + onset against the independent beat sample: 4516 at 0, 34 at -1, 7 at +1 | met |
| kick attack against the `.beats.csv` frame, from the first beat after the stall | see the list below the table: max +123 samples = **2.56 ms** (≤ 5) | met |
| `frames`: no over-limit frame outside the stall frame | backward 0, over 0.05 beat 0, max 0.0077 beat, hold 0 ms | met |

Kick attack:
- The stall ran from mixer beat 30.46 to about 34.07.
- The core kicks on beats 36 to 126 are at lag +66 to +123 samples, at most **2.56 ms**.
- Against `heavy_seed7_s6c`, beat 36 differs by +1 sample and every later kick by 0.

The stall frame itself moved the visual beat by 3.770 beat (expected 3.880).

Losses and the boundary inside the stall:
- Lost: the beat-31 kill chord (24 notes, late) and 7 pattern notes on beats 31.0-31.75 (late).
- Beats 32-34 have no kill chord; they were never queued.
- Boundary 32 fell inside the stall, and `landings` shows it silent for 265+250 ms.
- Beats 32 and 34 still have their core kick, which was handed over a bar ahead, before the stall.

How the kick lag was measured:
- Both the capture and the kick sample are low-passed below 200 Hz. They are cross-correlated within ±10 ms of each
  core-kick beat.
- **The kick-lag figures in Steps 4 and 5 and in carried item 4 come from a script that is not in the repo.** These are
  the absolute lags and the comparison with `heavy_seed7_s6c`. Only the click cross-correlation is committed
  (`spike --xcorr`).
- Where other low parts play with the kick, the normalised peak is 0.7-0.8 and the lag scatters between 66 and 123
  samples. `heavy_seed7_s6c` has the same scatter, beat for beat.
- The kicks that sounded alone show the kick's own lag (carried item 4).

### Carried measurements

1. **The three Task 6 silences (`baseline_seed7_s6c`, ruling C-4).** For each window, the notes in `.oneshots.csv` were
   checked against it, using each note's start and the delay end read back from its group.
   - **64 and 80.** Both windows are 320+95 ms, i.e. beats 64.576-64.747 (and the same offsets after 80).
     - No note starts inside the window.
     - Notes spanning it, on the tension buses:
       - TensionStrings 52 / 55 / 60, from the boundary beat to +1907 ms. Log lines 95131-95153 for 64,
         120792-120794 for 80.
       - Acid 36 and SineSub 36, from 64.5 to +185 ms. Log lines 95898 / 95899 for 64, 121650 / 121651 for 80.
     - Every other note is a drum hit of length 0 that starts 42 ms or more before the window. The kill chords are on
       beats 4k+1.
   - **112.** The window is 475+40 ms, i.e. beats 112.918-112.996.
     - No note starts inside it.
     - TensionStrings 52 / 55 / 60 span it (log lines 170910-170943).
     - Every other note is a drum hit that starts 475 ms or more before.
   - **The tension buses are silent here.** They are at -80 dB at Tension 0 (`bed_tension.CURVES`: strings up to 0.25,
     sub up to 0.45, acid up to 0.65). The baseline sets no Tension (`.params.csv` is empty).
   - So no audible note is scheduled to sound in any of the three windows: **each is a rest in the arrangement, not a
     gap**. The capture lost no note there (`.losses.csv` is empty).
2. **Capture mapping (`FrameAtDsp` against `getDSPClock`).**
   - Mismatches: 0 in every take.
   - Before any calibration, the raw mean signed error is -0.82 ms. This is the detector bias of -0.81 ms. The
     corrected mean signed error is -0.01 ms (Step 1) and -0.02 ms (Step 2).
   - By cross-correlation (`spike --xcorr`), every click starts exactly on its beat frame: lag 0 for 64 of 64 in each
     valid spike take.
   - **No constant offset between the capture's clock and the master DSP clock is observed.**
3. **±1-sample starts across a boundary.**
   - In the spike takes, the 4 beats after each tempo change match the other beats (tables above), with
     cross-correlation lag 0 for those beats too.
   - In the heavy takes the known ±1 shows: stall300 has 41 starts at -1 and 7 at +1 (of 4676); stall2000 has 34 at -1
     and 7 at +1 (of 4557).
4. **Per-bus onset.**
   - Kill Chords (SpikeClick): 0 samples against the clock (Step 1).
   - Music Bed/Layers (core kick): in `heavy_seed7_s7c_stall2000`, the kicks on beats 32 and 34 sounded alone during the
     stall (normalised peak 0.999). Each kick is **+72 / +73 samples (1.5 ms)** after its beat frame. The cause of this
     lag was not investigated.
   - `Music Bed/Harmony` and `Feature` (`PATH_DELAY_BLOCKS`): **not measured**.
5. **Deferred defects.** `Resynced` fired 0 times in every take, including the forced 300 ms and 2000 ms stalls. No
   `MasterDsp() == 0` resync and no phrase-end resync was hit.

### NOT met / open

- No criterion of Steps 1, 2, 4 or 5 is NOT met.
- Step 1 calibration: the corrected mean signed error (-0.01 ms) is within ±0.5 ms. The offset stays 0 (controller
  ruling C-5).
- A 300 ms stall just before a beat loses that beat's kill chord and its late pattern notes: 25 notes on beat 31 in
  Step 4. No step of this task has a criterion on missing notes in that take.

## Task 15b (stages 3-8): legacy timeline tracks, legacy parameters and legacy generators removed

`FMOD/Tools/cleanup_music.py` -> `cleanup_music.js` removes the legacy timeline layers from the FMOD project. Every layer
they held is already an `event:/Inst/*` one-shot, and the game no longer starts `event:/Music/ChordPulseGame`.

What it deleted (CLI run 1, saved):
- the 37 tracks of `patterns.LEGACY_TRACKS` (10,328 notes);
- the event's Harmony and Feature groups (empty once those tracks were gone);
- 1 automation curve: the Music Bed group's volume on MusicMix;
- 13 event parameter proxies: BedStyle, Variation, Combo, Intensity, FeatureLoop, Fever, FeverBuild, FeverImpact,
  FeverOutro, MusicMix, and the event's proxies of the global Tension / Danger / Hurt;
- 9 presets that no event used any more: BedStyle, Variation, Combo, Intensity, FeatureLoop, FeverBuild, FeverImpact,
  FeverOutro, MusicMix;
- `return:/Harmony Duck`, `return:/Feature Duck`;
- the global `LegacyLayers`.

Run 2 logged "nothing to do", and Metadata was MD5-identical to run 1. `-build` ran as a separate command and the banks
were copied to `Assets/StreamingAssets`.

Kept:
- the `Spike ref` track, `NextSection`, `SpikeRef`, the tempo and transition markers;
- the Music Bed group;
- `event:/Music/ChordPulsePreview`, and the `Fever` preset it still uses.

**Reference checks** (pre-cleanup Metadata, every file searched for the object ids):
- `LegacyLayers` (preset and its GameParameter): only the music event XML.
- Both returns: only the music event XML.
  - They had no effects, only a fader. Their outputs went to `bus:/Music Bed/Harmony` and `bus:/Feature`.
  - Their only inputs were the two pre-fader sends of the event's Harmony / Feature groups.
  - The kill-chord sidechain keys sit on bus effects, not on the returns.
- No snapshot exists.
- Each of the 9 deleted presets: only the music event XML. ChordPulsePreview references only `Fever`.

**Counts:**

| | before | after |
|---|---:|---:|
| music event `SingleSound` | 10,456 | 128 (Spike ref) |
| event XML | 18,776,153 B | 163,598 B |
| event tracks | 41 | 2 (Music Bed, Spike ref) |
| event parameters | 15 | 2 (NextSection, SpikeRef) |
| Desktop `ChordPulse.bank` | 7,070,400 B | 2,748,320 B |
| Metadata files changed / deleted | | 1 (event XML) / 12 (10 ParameterPreset, 2 Return) |

- No `event:/Inst/*` file changed.
- The 340 objects the event kept are byte-identical, except the Event, Timeline and Music Bed group objects, which lost
  their references to deleted objects.

**Generators:**
- Deleted:
  - `bed_variation.js`, `bed_combo.js`, `bed_tension.js`, `bed_style.js`;
  - `legacy_gate.py` / `.js`, `bed_style.py`;
  - `patterns.py --compare-legacy`.
- Kept as data modules. Their FMOD-script code was removed; running one directly now exits with a message.
  - `bed_tension.py`: read by `mixer.py` (Tension curves, Danger cutoff).
  - `bed_combo.py`: read by `mixer.py` (Hurt curves).
  - `bed_variation.py`: read by `bed_notes.py`.
- `mixer.py` no longer creates `LegacyLayers` or the Duck returns, and no longer touches the event's groups. On the
  cleaned project its `mixer.js` logged "created 0, updated 0, added 0, removed 0 / nothing to do", and Metadata was
  unchanged.
- I re-ran `patterns`, `instruments`, `mixer`, `bed_notes`, `feature_loops`, `kill_oneshots`, `spike_click` and
  `cleanup_music`. Every `*.g.cs`, `instruments.js`, `kill_oneshots.js` and `spike_click.js` is byte-identical to before
  the task. Only `mixer.js` changed.
- EditMode suite: 183 / 183.

### Take

Editor, baseline seed 7, 128 beats, `logOneShots`, full mix. Auto refresh was disallowed around each take (one
`DisallowAutoRefresh` / `AllowAutoRefresh` pair per take).

| take | stalls | Studio update avg / max % | Core dsp avg / max % | pattern sched / started / late / dropped | kill started / late / dropped |
|---|---:|---|---|---|---|
| `baseline_seed7_s15b` (**invalid**: unforced 3448 ms stall at beat 46) | 1 | 0.67 / 1.06 | 1.15 / 2.60 | 1097 / 1089 / 7 / 2 | 282 / 0 / 0 |
| `baseline_seed7_s15b_r2` | 0 | 0.67 / 1.06 | 1.15 / 2.34 | 1134 / 1131 / 0 / 0 | 288 / 0 / 0 |

- The late / dropped notes of the invalid take are all at beat 53.2, the first frame after its stall.
- `_r2`: ONESHOTS started 1419, late 0, dropped 0, cancelled 12. `.losses.csv` is empty.

| criterion (`baseline_seed7_s15b_r2`) | value | verdict |
|---|---|---|
| late 0, dropped 0 | 0 / 0 (pattern and kill) | met |
| Studio update avg ≤ 11 % | 0.67 % | met |
| Core dsp avg ≤ 6 % | 1.15 % | met |
| full-mix RMS per 4-beat window within ±1 dB of `baseline_seed7_s6c` (`rms --compare`) | max \|diff\| 0.000 dB over 31 windows; none outside ±1 dB | met |
- Counters of `_r2` as measured, next to the same counters of `baseline_seed7_s6c`:

  | counter | `_r2` | `_s6c` |
  |---|---:|---:|
  | PATTERNS scheduled | 1134 | 1129 |
  | PATTERNS started | 1131 | 1128 |
  | ONESHOTS started | 1419 | 1416 |
  | ONESHOTS cancelled | 12 | 13 |
  | KILLS started | 288 | 288 |

  scheduled minus started is 3 in `_r2` and 1 in `_s6c`. Not looked into note by note; no criterion covers it.
- The take ran on the final banks: the later `mixer.js` run changed nothing, so the banks were not rebuilt after the
  take.

## Task 8 (unity-music-clock): music event deleted, landing special case removed, final measurement

What changed (commits on `main`):
- `954ce41` **Landing special case removed.** `KillChordPlanner.PostBoundaryBeats` / `PostBoundaryDispatchBeats` are gone;
  `DispatchWindow(beat)` returns `OneShotScheduler.DispatchWindowBeats` (0.5) on every beat. This was R15 of the old plan.
- `0fe7bce` **`event:/Music/ChordPulseGame` deleted.** `FMOD/Tools/remove_music_event.py` -> `.js`:
  - It counts the event's `SingleSound` notes per track and stops without deleting anything if a note sits outside
    `Spike ref`.
  - It deletes the event, then the presets `NextSection` and `SpikeRef` if no proxy, condition or curve uses them any more.
  - A second run logs "nothing to do".
  - `spike_click.py` no longer generates the `Spike ref` track (`spike_click.js` deleted); the `SpikeClick` instrument
    stays.
  - `instruments.js` / `kill_oneshots.js` no longer require the event; `mixer.js` no longer routes it.
- `7eb0e3c` `analyze_capture.py spike --xcorr-window-ms W` (per-bus onset; see "Per-bus onset" below).

Settings for every take: Editor unless stated, `logOneShots`, the scenario's own kills, full mix,
`scheduleOffsetSamples` 0 (`DefaultScheduleOffsetSamples`, unchanged, ruling C-5).
- Auto refresh was disallowed around each Editor take, with one `DisallowAutoRefresh` before Play mode and one
  `AllowAutoRefresh` after it (17 / 17).
- The standalone builds ran after the last take, before that take's `AllowAutoRefresh`.
- No Editor query was made while a take was recording, with one exception: the arming call of the pause take (see Step 2).
- Lengths: `styles` 256 beats (`stylesWithBed`, intensity 3), `features` 208, `fever` 144. These are Task 15a's settings,
  so that the same landings are covered. Every other take is seed 7 / 128 beats or seed 33 / 68 beats, except the three
  Danger / Hurt takes (seed 7, 64 beats, as in stages 3-8 Task 8).

### Takes (every take recorded in this task)

ONESHOTS = started / late / dropped / cancelled (`.cpu.txt`). RESYNCED and `CAPTURE dsp-to-frame mismatches` are 0 in
every take. `.losses.csv` is empty in every take except `_pause3s_r2`.

| take | when | unforced stalls | Studio avg / max % | Core dsp avg / max % | ONESHOTS | valid |
|---|---|---:|---|---|---|---|
| `heavy_seed33_s8c_step1` | Step 1 (old banks) | 0 | 1.29 / 1.72 | 4.83 / 6.34 | 2383 / 0 / 0 / 35 | yes |
| `baseline_seed7_s8c_pause3s` | Step 2 (old banks) | 0 | 0.66 / 0.96 | 1.17 / 2.45 | 1413 / 0 / 0 / 10 | yes, but **no pause happened** (see Step 2) |
| `baseline_seed7_s8c_pause3s_r2` | Step 2 (old banks) | **2** (3530 ms at beat 23.7, 3006 ms at 46.1 = the pause) | 0.77 / 1.16 | 0.84 / 1.63 | 1330 / 18 / 3 / 6 | **no** for the criteria; it is the pause check |
| `baseline_seed7_s8c` | Step 4 | 0 | 0.69 / 1.02 | 1.21 / 2.65 | 1413 / 0 / 0 / 6 | yes |
| `heavy_seed7_s8c` | Step 4 | 0 | 1.44 / 1.97 | 5.13 / 6.51 | 4683 / 0 / 0 / 12 | yes |
| `heavy_seed33_s8c` | Step 4 | 0 | 1.25 / 1.75 | 4.73 / 6.14 | 2383 / 0 / 0 / 35 | yes |
| `styles_seed7_s8c` | Step 4 | 0 | 1.11 / 1.54 | 3.36 / 4.84 | 5418 / 0 / 0 / 25 | yes |
| `features_seed7_s8c` | Step 4 | 0 | 0.64 / 0.94 | 1.38 / 2.40 | 2104 / 0 / 0 / 6 | yes |
| `fever_seed7_s8c` | Step 4 | 0 | 0.87 / 1.27 | 2.09 / 3.36 | 2747 / 0 / 0 / 19 | yes |
| `spike_seed33_s8c` (Kill Chords) | Step 4 | 0 | 0.55 / 0.77 | 0.92 / 1.84 | 559 / 0 / 0 / 10 | yes |
| `spike_seed33_s8c_bus_musicbed` | per-bus onset | 0 | 0.53 / 0.73 | 0.78 / 1.66 | 559 / 0 / 0 / 10 | yes |
| `spike_seed33_s8c_bus_harmony` | per-bus onset | 0 | 0.58 / 0.80 | 0.84 / 1.89 | 559 / 0 / 0 / 10 | yes |
| `spike_seed33_s8c_bus_combopad` | per-bus onset | 0 | 0.56 / 0.73 | 0.83 / 1.80 | 559 / 0 / 0 / 10 | yes |
| `spike_seed33_s8c_bus_bass` | per-bus onset | 0 | 0.58 / 0.81 | 0.83 / 1.91 | 559 / 0 / 0 / 10 | yes |
| `baseline_seed7_s8c_b64` | Danger / Hurt reference | 0 | 0.60 / 1.00 | 1.07 / 2.50 | 615 / 0 / 0 / 6 | yes |
| `baseline_seed7_s8c_b64_danger` | `forceDanger` | 0 | 0.58 / 0.86 | 1.08 / 2.27 | 615 / 0 / 0 / 6 | yes |
| `baseline_seed7_s8c_b64_hurt8` | `hurtEveryBeats` 8 | 0 | 0.58 / 0.84 | 1.07 / 2.28 | 615 / 0 / 0 / 6 | yes |
| `baseline_seed7_s8c_standalone` | standalone, 60 fps | 0 | 0.51 / 0.94 | 1.11 / 2.38 | 1417 / 0 / 0 / 10 | yes |
| `heavy_seed7_s8c_standalone` | standalone, 60 fps | 0 | 1.05 / 1.40 | 2.70 / 3.81 | 4678 / 0 / 0 / 7 | yes |

The standalone takes are in `Builds/Capture/<scenario>_s8c/Docs/Captures/` (git-ignored). They were built the Task 15a way:
- a temporary copy of `AudioCapture.unity` with the fields set in memory (`targetFps` 60, `logOneShots`);
- `BuildPipeline.BuildPlayer` with `BuildOptions.Development`, StandaloneWindows64;
- the two copies and their `.meta` deleted afterwards.

The ProjectSettings MD5s were identical before and after. The players were closed after their `.cpu.txt` appeared.

### Step 1: landing special case removed (heavy seed 33, kills on every beat)

`heavy_seed33_s8c_step1`, recorded on the old banks right after the change:
- ONESHOTS late 0, dropped 0 over the whole take, so also 0 in the 4 beats after each of the boundaries 16 / 32 / 48 / 64.
- `.losses.csv` is empty.
- `landings`: delay 0 at every boundary, no gaps.
- **Met.** The special case stays deleted.

EditMode (TestRunnerApi): 183 / 183 after the change.
- `DispatchWindow_PhraseLanding` now expects 0.5 on beats 0, 1, 2, 15-18, 31-33, 64, 65 and 127.
- `LandingBeats_UseWideDispatchWindow` was renamed `LandingBeats_UseDefaultDispatchWindow`. At now = 15.0 no beat-16 note
  is handed over; at 15.6 they are.
- `Invalidate_RestatesNotStartedEarlyOnsetNotes_Once` failed with the change, because it relied on the wide window to
  have the beat-16 notes handed over at 15.0. It now dispatches them at 15.5 before invalidating.

### Step 2: situations that must not break

**Editor pause 3 s, then resume (automated).** Take `baseline_seed7_s8c_pause3s_r2`. An `EditorApplication.update`
callback set `EditorApplication.isPaused = true` when the visual beat passed 40.3, and set it back to false 3.0 s later.
- First attempt, `baseline_seed7_s8c_pause3s`: the callback was registered before Play mode and was lost with the domain
  reload when Play mode started, so no pause happened. Otherwise it is an ordinary valid baseline take.
- `_r2`: the callback was registered by one `execute_code` call after Play mode had started. The call ran at visual beat
  17.38, Unity frame 2661.
  - The take's unforced 3530 ms stall is that call's frame. `.frames.csv` logs it at its end, beat 23.73, and
    23.73 - 3.53 s x 108 / 60 = 17.38. This is the same effect as in Task 7's invalid spike take.
  - Its losses are on the first frame after it: 5 late, note beats 18.0-18.5; 2 dropped, 23.75 / 23.92.

What the pause did, from the callback's own log (master DSP clock read at pause and at resume):
- **The mixer kept running during the pause.** DSP clock 1,089,536 at pause and 1,233,920 at resume = 144,384 samples
  = 3.008 s.
- **No burst of delayed notes.**
  - Late 18 and dropped 3. On the first frame after the resume (`.losses.csv`, beat_now 46.103) there were 14 losses:
    - 9 kill notes, beat 41.0, late;
    - 5 pattern notes, beats 40.75-41.5: 4 late, and 1 dropped by the backend because its start passed during the pause.

    The other 7 are the losses of the earlier stall.
  - Late notes are not started.
  - The first frame after the resume started 13 notes. The take's busiest frames start 13-14. The smallest lead among
    those 13 was 9792 samples. Over the whole take, 0 of 1330 starts were handed over at or after their start sample.
  - `landings --at-beats 40.3,46.103`: after 40.3, silence 20+495 ms (nothing was handed over past 40.8). After the
    resume point, no gap (only rests also present 4 beats earlier).
- **Screen beat against the sound.**
  - The visual beat was 40.3044 at the pause and 46.1148 on the first frame after the resume.
  - The clock beat of the resume DSP sample is 46.103 (`.beats.csv`: beat 46 = DSP 1,231,352, 24,828 samples a beat).
  - `frames`: backward 0, frames over 0.05 beat 0, max deviation 0.0066 beat. This includes the 3.006 s pause frame,
    which advanced 5.81 beats as `dt * bpm / 60` predicts.
- `Resynced`: 0.

**Pending user check** (not done by me; each needs a person at the machine):

| check | procedure | what to look for |
|---|---|---|
| focus loss 3 s, Run In Background off | Project Settings > Player > Resolution and Presentation > Run In Background off (do not commit the change). Play `ChordPulsePrototype`, wait for music, click another window for 3 s, click back. | No burst of notes at return. Screen pulse and kick agree within a beat. Console shows no exception. `Resynced` may or may not log. |
| the same, Run In Background on | as above with the box on | as above |
| Windows output-device switch while playing | Play, then change the default output device (Windows sound settings, or unplug headphones) | No exception, music continues within one bar from the next bar's first beat, console shows `Resynced` or a restart log |
| game over, then immediate restart, 3 times | Play, lose all lives, restart at once; repeat 3 times | No leftover notes after game over, the first beat of the new run is normal (kick on beat 0/1, no doubled chords) |
| Step 5 manual Play | Play `ChordPulsePrototype` for a few minutes: a tempo change, fever entry and exit, a breathing phrase, game over and restart | No dropouts, no leftover notes |

No automated "game over -> restart" observation was made.

### Step 3: FMOD

Before the change, `Metadata`, `ChordPulse.fspro`, `Build`, both StreamingAssets banks and `FMODStudioCache.asset` were
copied to the scratch folder.

Run 1 of `remove_music_event.js`:
- The event's 128 `SingleSound` notes were all on `Spike ref`. Counted in the pre-change event XML, the only track with
  notes; the script would have thrown otherwise. The script's per-track log lines of run 1 were cut off in my terminal
  output.
- It deleted the event and both presets, then saved.
- Metadata: 3 files deleted (the event XML, the `NextSection` and `SpikeRef` presets), no file modified.

Reference check before deleting (`python -I remove_music_event.py`, every Metadata file searched for the preset and
GameParameter ids): `NextSection` and `SpikeRef` were used by nothing outside the music event's XML. No Unity script
reads either parameter.

Afterwards:
- Run 2: "not in the project ... nothing to do".
- On the event-less project, `instruments.js`, `kill_oneshots.js` and `mixer.js` change nothing: "moved 0, updated 0,
  added 0, removed 0", "nothing to do", "nothing to do", and Metadata is unchanged. `*.g.cs` and `click_3k.wav` are
  unchanged.
- `-build` was run as a separate command. Desktop `ChordPulse.bank` went from 2,748,320 to 2,708,160 B and
  `strings.bank` from 4,354 to 4,214 B. The banks were copied to `Assets/StreamingAssets`.
- The FMOD cache dropped the event. No "Events Renamed" dialog appeared. Compile errors: 0. EditMode: 183 / 183.

`SoundManager` already had no `MusicPath` and no reference to the event (removed in Task 6). Only the comments in
`SectionSelector.cs` that named the event were updated.

The `spike` scenario still works without the event (`spike_seed33_s8c` below).

### Step 4: final measurement (on the new banks)

**Boundaries** (`landings`, and first half-beat RMS against the same place one phrase earlier, b-16). Two boundaries are
not compared:
- A boundary whose pattern notes in the first half beat differ from b-16's.
- A boundary whose kill notes differ. Beat 0 has no kill chord in `heavy` / `fever`, while beat 16 has 24 / 9 kill notes.

| take | boundary delay (ms) | silent runs > 20 ms within a beat of a boundary (`landings`) | first half-beat RMS vs b-16, compared boundaries |
|---|---|---|---|
| `baseline_seed7_s8c` | 0 at all 7 | 64: 320+95, 80: 320+95, 112: 475+40 | 16 / 32 / 48 / 80 / 112: -0.00 / +0.27 / -0.13 / -0.00 / +0.16 dB |
| `heavy_seed7_s8c` | 0 at all | none | 32 / 48 / 80 / 112: +0.05 / -0.18 / +0.04 / -0.15 dB |
| `heavy_seed33_s8c` | 0 at all | none | 32: -0.11 dB |
| `styles_seed7_s8c` | 0 at all 15 | none | 96 / 144 / 160 / 208 / 224: -0.05 / +0.13 / -0.02 / -0.39 / +0.36 dB |
| `features_seed7_s8c` | 0 at all 12 | 64: 320+95, 144: 510+40, 176: 335+80 | 144: **-1.71 dB** |
| `fever_seed7_s8c` | 0 at all 8 | none | 80: +0.01 dB |
| `spike_seed33_s8c` | 0 at all | 32: 510+40 | (no kills; not compared) |
| `baseline_seed7_s8c_standalone` | 0 at all | 64: 320+95, 80: 320+95, 112: 475+40 | the same five values as the Editor take |
| `heavy_seed7_s8c_standalone` | 0 at all | 16: 355+40, 32: 345+35, 48: 345+60, 64: 330+40 and 455+90, 80: 335+40 and 455+90, 96: 345+35, 112: 345+40 | 32 / 48 / 80 / 112: +0.62 / -0.19 / -0.45 / -0.62 dB |

Notes on the table:
- The beat-128 row that `landings` prints for 128-beat takes is past the take's last beat. The music is stopped there
  (next half beat digital silence), so it is not counted.
- **Silences.** The baseline 64 / 80 / 112 windows are the same as in Task 6 and Task 7. Task 7 showed each is a rest in
  the arrangement: no audible note is scheduled there.
  - `features` 64 / 144 / 176 have the same shape. The same windows are silent in Task 15a's `features_seed7_s15a_new`
    (before the clock switch): 64: 335+90, 144: 515+40, 176: 345+75.
  - `spike` 32 (510+40) is also in Task 7's `spike_seed33_s7c_d05_r2`.
  - `.losses.csv` is empty in every Step 4 take.
  - They are counted against the criterion below as the analyzer reports them.
- **Standalone heavy.**
  - Its silences are not in the Editor heavy take.
  - The whole standalone heavy take is about 3 dB quieter than the Editor take: `rms --compare` against
    `heavy_seed7_s8c`, max |diff| 4.00 dB over 31 windows.
  - Task 15a's standalone heavy was quieter by the same kind of amount: max |diff| 4.53 dB against
    `heavy_seed7_s15a_new`.
  - Standalone baseline equals the Editor baseline (max |diff| 0.000 dB).
  - Cause not investigated.
- **`features` 144.** Same pattern notes in the first half beat as 128, no kills, -1.71 dB. Task 15a's take has the same
  first-half level at 144 (-33.0 dB with the predicted boundary). Cause not investigated.

**Beat 64 (Part A).** `baseline_seed7_s8c`:
- Boundary delay 0 ms. In Part A it was +86 to +95 ms in the old takes and +9 / +3 / +1 / -4 ms in the later ones.
- First half beat -32.6 dB, next half -39.7 dB.
- b-16 (beat 48) -31.5 dB: -1.10 dB, but different pattern notes, so not compared.
- Silent run 320+95 ms, the arrangement rest described in Task 7.

**Attack** (`spike_seed33_s8c`, `analyze_capture.py spike --xcorr`; detector bias -39.0 samples = -0.812 ms as in Task 7):

| clicks | n | raw mean \|e\| / max / signed ms | corrected mean \|e\| / max / signed ms |
|---|---:|---|---|
| all | 64 | 0.82 / 0.85 / -0.82 | 0.02 / 0.04 / -0.01 (-0.4 samples) |
| 4 beats after a tempo change (16, 32, 48, 64) | 13 | 0.81 / 0.85 / -0.81 | 0.02 / 0.04 / -0.00 |

- Missing 0, late 0, early 0, GATE PASS.
- `--xcorr`: 64 of 64 clicks at lag 0 samples.

**Frames** (`analyze_capture.py frames`): every Step 4 take (Editor and standalone) and Step 1 has backward 0, frames
over 0.05 beat 0 and longest hold 0 ms. The max deviation is 0.0060-0.0099 beat; the largest is `heavy_seed7_s8c`,
0.0099 at beat 16.04.

**`styles` drop burst.** `styles_seed7_s8c`: ONESHOTS late 0, dropped 0, `.losses.csv` empty. Task 15a's 11 drops in the
first beat after landings 32 and 128 are gone. Late / dropped is 0 / 0 in every Step 4 take, Editor and standalone.

**Danger / Hurt** (on the one-shot buses; `bands` against `baseline_seed7_s8c_b64`):
- Danger (`forceDanger`), median take minus reference:
  - bed alone (beat k+2.5 .. k+4): <300 Hz 0.00, 300-2k -0.28, **>2k -41.80 dB**;
  - kill chord ringing (k+1.0 .. 1.8): +0.03 / 0.00 / -0.25;
  - chord attack (first 80 ms of 4k+1): +0.01 / -0.00 / -0.47.
  - Stages 3-8 Task 8 had -42.05 above 2 kHz for the bed alone and -0.51 on the chord attack. Same behaviour.
- Hurt (`hurtEveryBeats` 8, hits at 8..56; `.params.csv` Hurt 1 then 0 on each hit beat), median of the 7 hits, <300 Hz /
  300-2k / >2k:
  - +50 ms: **-22.9** / -5.1 / **-3.6**;
  - +150 ms: -19.5 / -5.7 / -2.9;
  - +350 ms: -12.4 / -2.7 / -1.4;
  - +500 ms: -4.7 / -1.2 / -0.6;
  - +550 ms: -0.2 / 0.0 / 0.0.
  - Authored: lows -24 dB, gain -4 dB, recovery 0.625 s. Stages 3-8 Task 8 had -23.4 / -3.5 at +50 ms, complete at about
    550 ms. Same behaviour.

### Per-bus onset against the clock (carried item 1)

`spike` seed 33 with `CaptureScenario.spikeTarget` on each bus that has a `SpikeClick` event. Lag by
`spike --xcorr --xcorr-window-ms 40` (cross-correlation with the click waveform, 2-4 kHz).

| bus (spikeTarget) | take | lag, samples (clicks) | ms | constant |
|---|---|---|---:|---|
| Kill Chords (`KillChords`) | `spike_seed33_s8c` | 0 (64 of 64) | 0.00 | yes |
| Music Bed/Layers (`MusicBed`) | `_bus_musicbed` | **+6** (64 of 64) | 0.13 | yes |
| Music Bed/Layers/Bass (`Bass`) | `_bus_bass` | **+6** (64 of 64) | 0.13 | yes |
| Music Bed/Harmony (`Harmony`) | `_bus_harmony` | **+1030** (64 of 64) | **21.46** | yes |
| Music Bed/Harmony/Combo Pad (`ComboPad`) | `_bus_combopad` | **+1029** (36) / **+1030** (27); 63 of 64 with peak > 0.9 | **21.44-21.46** | within 1 sample |
| Feature | - | not measured: there is no `SpikeClick` event on `bus:/Feature` (`spike_click.BUSES`) | - | - |

- On the Harmony buses, `spike`'s own gate (judged on corrected errors) FAILS: late 64 of 64, mean +21.54 ms. The Global
  Constraints' attack figure is the Kill Chords spike, which passes.
- Arithmetic, not an experiment: 1030 = 1024 + 6. `instruments.PATH_DELAY_BLOCKS` gives `Music Bed/Harmony` and
  `Feature` one mixer block, applied through `InstrumentTable.PathDelayBlocks`. The 6 samples are what Music Bed/Layers
  shows too. This was not tested by changing `PATH_DELAY_BLOCKS`.
- The Layers kick at +72 / +73 samples (Task 7) is not reproduced from the repo: Task 7's kick-lag script was not
  committed. The bus itself is at +6 samples, so about +66 samples of that figure come from somewhere other than the bus.
  Not investigated.

### A 300 ms stall (carried item 2; restated from Task 7, no new take)

`heavy_seed7_s7c_stall300`: the stall at beat 30.5 lost the whole beat-31 kill chord (24 notes: 6 late in the scheduler,
18 dropped by the backend) and one pattern note (Acid 31.0, TensionAcid). That is 25 notes. The stall at 47.7 lost
nothing. No criterion covers it.

### Global Constraints (plan)

| constraint | value (takes above) | verdict |
|---|---|---|
| attack error mean ≤ 2 ms, max ≤ 5 ms, incl. 4 beats after tempo changes | `spike_seed33_s8c` corrected 0.02 / 0.04 ms; after changes 0.02 / 0.04; xcorr lag 0 for 64 / 64 | met |
| the same bound applied to the Harmony buses (not a stated constraint; carried item 1) | +21.46 ms constant | **NOT met** (if applied). Later: see "Final fix wave" (path delay removed) |
| one-shots never late; never early | ONESHOTS late 0 in every valid take; spike early 0 (corrected and raw) | met |
| boundaries: first half-beat RMS ±1 dB (same arrangement) | all compared boundaries within ±0.62 dB except `features` 144 | **NOT met**: `features_seed7_s8c` 144, -1.71 dB |
| boundaries: no silence > 20 ms | `landings` lists 3 (baseline), 3 (features), 1 (spike), 3 (standalone baseline), 9 (standalone heavy) | **NOT met**: 19 runs by the analyzer's count. The 10 in the Editor takes and the standalone baseline are in windows that are also silent in earlier reference takes, with no losses. The 9 standalone heavy runs are not in the Editor take. Later: see "Final fix wave" (channel counts) |
| screen beat: backward 0, ≤ 0.05 beat per frame, hold ≤ 30 ms | backward 0, max 0.0099 beat, hold 0 ms in every take | met |
| CPU: Studio avg ≤ 11 %, Core dsp avg baseline ≤ 6 % / heavy ≤ 12 % | Studio max avg 1.44 % (`heavy_seed7_s8c`); Core baseline 1.21 %, heavy 5.13 % (seed 7) / 4.73 % (seed 33); standalone 1.11 / 2.70 % | met |
| `Visual/*`, `ChordPulsePrototype.cs` untouched; `SoundManager` public members unchanged | not touched (`SoundManager.cs` not changed in this task) | met |
| `SectionSelector`, fever alignment, arrangement rules unchanged | only two comments in `SectionSelector.cs` | met |
| captures: Editor, seed 7 (128) / seed 33 (68), only stall-free takes valid | every Step 4 take has 0 unforced stalls. `styles` (256), `features` (208), `fever` (144) and the three Danger / Hurt takes (64) use other lengths, and two takes are standalone (both asked for by the brief) | **NOT met as written** (lengths); validity met |
| FMOD CLI rules | script and `-build` separate, banks copied, Studio closed (no `fmodstudio.exe` process) | met |
| commits: only each task's files, `git add <file>` | see the commit list | met |
| EditMode tests | TestRunnerApi (controller ruling): 183 / 183 | met |

### NOT met / open

- First half-beat RMS ±1 dB: `features_seed7_s8c` boundary 144, -1.71 dB with the same notes as 128.
- Silence > 20 ms at a boundary: 19 runs by `landings` (list above).
  - 10 runs are in the Editor takes and the standalone baseline. The same windows are silent in earlier reference
    takes, and nothing was lost. Task 7 showed the baseline ones are arrangement rests; the others were not checked
    note by note.
  - The 9 standalone heavy runs are not in the Editor. Cause not investigated. (Later: see "Final fix wave".)
- Standalone heavy is about 3 dB quieter than the Editor take. Task 15a's standalone heavy shows the same. Cause not
  investigated. (Later: see "Final fix wave".)
- Harmony and Combo Pad one-shots sound +1030 samples (21.5 ms) after the clock. Kill Chords 0, Layers and Bass +6.
  Not fixed (a separate decision). (Later: fixed, see "Final fix wave".)
- Pending user checks (Step 2: focus loss off / on, output-device switch, game over -> restart x3; Step 5 manual Play).
  None was done.

## Final fix wave (unity-music-clock, after the final review)

Commits on `main`:
- `746d3a5` **Harmony / Feature path delay 0.** `instruments.PATH_DELAY_BLOCKS` is `{}` (was `Music Bed/Harmony` 1,
  `Feature` 1). `InstrumentTable.PathDelayBlocks` returns 0 on every bus; `instruments.js` byte-identical (no CLI run, no
  bank rebuild). The block matched the legacy timeline layers' send / return, which left with the music event.
- `ddc456b` `.cpu.txt` ends with `CHANNELS playing peak N, real peak M` (Core `getChannelsPlaying`, sampled every frame
  of the take). Capture code only.
- `37e8532` `FmodClock.MasterDsp()` returns the last good value on a failed read while FMOD is initialised (0 only
  before FMOD is initialised); `SoundManager.LoadAndStartMusic` reports a failure instead of starting the transport if
  the clock reads 0. Not covered by a test (no seam without FMOD).
- `1929ee1` `cleanup_music.py/.js`, `remove_music_event.py/.js`, `extract_fever.py` moved to `FMOD/Tools/retired/`; the
  timeline reader (`legacy_notes`, `EVENT_XML`, `_sections`) removed from `patterns.py`.
- `ef88230` stale comments, the dead `build()` of the bed data modules, test names.

Settings: Editor unless stated, `logOneShots`, full mix, `scheduleOffsetSamples` 0, auto refresh disallowed around
each Editor take, no Editor query while a take recorded. The standalone builds: the Task 8 recipe (temporary copy of
`AudioCapture.unity` with `targetFps` 60, `BuildOptions.Development`, StandaloneWindows64, the copy and its `.meta`
deleted afterwards), output in `Builds/Capture/heavy_s9f_before/` and `heavy_s9f_after/`. ProjectSettings MD5s were
identical before and after.

### Takes (every take recorded in this wave)

ONESHOTS = started / late / dropped / cancelled. RESYNCED and `CAPTURE dsp-to-frame mismatches` are 0 in every take,
`.losses.csv` is empty in every take. CHANNELS = playing peak / real peak (only takes after `ddc456b`).

| take | item | unforced stalls | Studio avg / max % | Core dsp avg / max % | ONESHOTS | CHANNELS | valid |
|---|---|---:|---|---|---|---|---|
| `spike_seed33_s9f_bus_harmony` | M2 | 0 | 0.56 / 0.88 | 0.83 / 1.94 | 559 / 0 / 0 / 10 | - | yes |
| `spike_seed33_s9f_bus_combopad` | M2 | 0 | 0.55 / 0.79 | 0.83 / 1.79 | 559 / 0 / 0 / 10 | - | yes |
| `baseline_seed7_s9f` | M2 | 0 | 0.64 / 0.93 | 1.14 / 2.54 | 1413 / 0 / 0 / 6 | - | yes |
| `heavy_seed7_s9f_standalone_before` | M1 (standalone, 60 fps) | 0 | 1.07 / 1.81 | 2.73 / 3.81 | 4683 / 0 / 0 / 12 | 91 / **32** | yes |
| `heavy_seed7_s9f` | M1 (Editor) | 0 | 1.34 / 1.93 | 5.09 / 9.55 | 4678 / 0 / 0 / 7 | 91 / 91 | yes |
| `heavy_seed7_s9f_standalone_after` | M1 (standalone, 256 / 1024 trial) | 0 | 1.05 / 1.81 | 2.82 / 3.83 | 4683 / 0 / 0 / 12 | 91 / **32** | yes |

No take was invalid. The spike takes use seed 33 / 68 beats with `spikeTarget` `SpikeClick:Harmony:72` /
`SpikeClick:ComboPad:72`.

### M2: per-bus onset against the clock (`spike --xcorr --xcorr-window-ms 40`)

| bus | before (Task 8) | after (this wave) | `spike` gate (corrected) |
|---|---|---|---|
| Kill Chords | 0 (`spike_seed33_s8c`) | not re-measured (path delay was already 0) | PASS (Task 8) |
| Music Bed/Layers, Bass | +6 | not re-measured (path delay was already 0) | - |
| Music Bed/Harmony | **+1030** (64 of 64) | **+6** (64 of 64) | before FAIL (late 64, mean +21.54 ms); after **PASS**: mean\|e\| 0.19, max 0.23, signed +0.19 ms; 4 beats after tempo changes 0.18 / 0.23 |
| Music Bed/Harmony/Combo Pad | **+1029 / +1030** | **+6** (57) / +5 (6); 63 of 64 with peak > 0.9 | after **PASS**: mean\|e\| 0.20, max 0.23, signed +0.20 ms; after tempo changes 0.19 / 0.23 |
| Feature | not measured | 0 by construction (`PathDelayBlocks` 0), **not measured**: no `SpikeClick` event on `bus:/Feature` | - |

- Every take: missing 0, early 0, late 0. The spec attack bound (mean ≤ 2 ms, max ≤ 5 ms) is met on the Harmony and
  Combo Pad buses.
- `baseline_seed7_s9f` against `baseline_seed7_s8c` (`rms --compare`, 4-beat windows): max |diff| **0.033 dB** over
  31 windows (windows 11, 12 and 27 at 0.03 dB, the rest 0.00). No window outside ±1 dB. Late 0, dropped 0. `landings`:
  delay 0 at every boundary, silent runs 64: 320+95, 80: 320+95, 112: 475+40, the same arrangement rests as
  `baseline_seed7_s8c`.

### M1: standalone heavy quieter, boundary silences (voice-limit hypothesis)

Channel counts (FMOD for Unity settings, read in the Editor through `Settings.FindPlatform`):

| platform | real | virtual | codec counts |
|---|---:|---:|---|
| Default (`HasValue` 0 -> `Platform.cs` defaults) | 32 | 128 | FADPCM 32, Vorbis 0 |
| Windows (inherits real / virtual from Default) | 32 | 128 | FADPCM 0, Vorbis **32** (platform default) |
| Play In Editor | 256 | 1024 | FADPCM 0, Vorbis 256 |

Measured peaks (`.cpu.txt` CHANNELS): the Editor heavy take plays up to 91 channels, all 91 real. The standalone
heavy take plays the same 91, **real pinned at 32** (the after-take's `Player.log` shows `SystemI::updateChannels :
Channel ... Stolen`; the before-take's log was overwritten by it). What was measured: the player's real channel peak
is pinned at 32 (Editor 91); which voices the player virtualises was not measured.

Trial: the Default platform's real / virtual set to 256 / 1024 (`HasValue` 1), only those fields; the Editor read back
256 / 1024 for Default and Windows. Rebuilt, take `heavy_seed7_s9f_standalone_after`:

| criterion | before (`_standalone_before`) | after (`_standalone_after`) | verdict |
|---|---|---|---|
| real channel peak | 32 | **32** | not lifted |
| `rms --compare` against `heavy_seed7_s9f` (Editor), within ±1 dB | max \|diff\| 3.911 dB, 31 of 31 windows outside, standalone 2.98 dB quieter on average | max \|diff\| **3.484 dB**, 31 of 31 outside, 2.75 dB quieter on average | **NOT met** |
| boundary silences (`landings`) gone | 9 (16: 355+40, 32: 345+35, 48: 345+60, 64: 330+40 and 455+90, 80: 335+40 and 455+90, 96: 345+40, 112: 345+40) | **2** (64: 455+90, 80: 455+90) | **NOT met** |
| Core dsp avg ≤ 12 % | 2.73 % | 2.82 % | met |

The after-take did not meet the criteria, so the setting was put back (same fields, previous values; the asset is
unchanged in git) and nothing was committed. Editor reference: `heavy_seed7_s9f` against `heavy_seed7_s8c` max |diff|
0.245 dB, `landings` no gaps.

Not shown, not tested: what still caps the player at 32 real channels after the change. The Windows platform's Vorbis
codec count is 32 (the Editor's is 256) and the banks' encoding setting is format 3; whether a codec limit is the cap was
not tried (this wave allowed only the channel-count fields). The standalone heavy level and the remaining silences:
cause not shown.

### Final fix wave — M1 follow-up (Vorbis codec count)

The player has two caps of 32: `RuntimeManager` sets `realChannels` from the platform's `RealChannelCount` (default 32)
and `advancedSettings.maxVorbisCodecs` from the platform's Vorbis codec count (`PlatformWindows` default 32; Play In
Editor 256). The Desktop bank is Vorbis-encoded. Codec counts are per platform object, not inherited from Default.

What was changed (`Assets/Plugins/FMOD/Resources/FMODStudioSettings.asset`, text edit, only these fields):
- Default platform: `VirtualChannelCount` 1024, `RealChannelCount` 256 (`HasValue` 1).
- Windows platform: `codecChannels` = FADPCM 0, Vorbis 256 (`HasValue` 1).

The Editor read back (`Settings.FindPlatform`): Default 256 / 1024, FADPCM 32 / Vorbis 0; Windows 256 / 1024,
FADPCM 0 / Vorbis 256; Play In Editor unchanged. Unity did not re-serialise the asset: after the refresh and both builds
its `git diff` was byte-identical to the hand edit. ProjectSettings MD5s identical before and after the builds.

Builds: the Task 8 recipe (temporary copy of `AudioCapture.unity`, `targetFps` 60, `logOneShots`, `scheduleOffsetSamples`
0, `BuildOptions.Development`, StandaloneWindows64; copies and `.meta` deleted afterwards), output in
`Builds/Capture/heavy_s9g/` and `baseline_s9g/`. Analysis with the Python 3.10 install (`python -I`; it has numpy in its
own site-packages).

| take | unforced stalls | Studio avg / max % | Core dsp avg / max % | ONESHOTS | CHANNELS | `.losses.csv` | valid |
|---|---:|---|---|---|---|---|---|
| `heavy_seed7_s9g_standalone` | 0 | 1.17 / 1.60 | 5.21 / 6.64 | 4678 / 0 / 0 / 7 | 91 / **91** | empty | yes |
| `baseline_seed7_s9g_standalone` | 0 | 0.54 / 0.78 | 1.15 / 2.45 | 1413 / 0 / 0 / 6 | 33 / 29 | empty | yes |

No take was invalid. RESYNCED and dsp-to-frame mismatches 0 in both.

| criterion | `heavy_seed7_s9g_standalone` | verdict |
|---|---|---|
| real channel peak clearly above 32 | 91 (= Editor `heavy_seed7_s9f`) | met |
| `rms --compare` against `heavy_seed7_s9f` (Editor) within ±1 dB | max \|diff\| **0.001 dB** over 31 windows (was 3.484 / 3.911) | met |
| no boundary silence the Editor take lacks (`landings`) | none; every row (delay, first / next half, prev phrase) equal to `heavy_seed7_s9f` | met |
| late 0 / dropped 0 | 0 / 0 | met |
| Core dsp avg ≤ 12 % | 5.21 % (Editor 5.09 %) | met |

| criterion | `baseline_seed7_s9g_standalone` | verdict |
|---|---|---|
| `rms --compare` against `baseline_seed7_s9f` (Editor) within ±1 dB | max \|diff\| **0.000 dB** over 31 windows | met |
| late 0 / dropped 0 | 0 / 0 | met |
| `landings` | 64: 320+95, 80: 320+95, 112: 475+40, the same rows as `baseline_seed7_s9f` | (unchanged) |

Verdict: met. The setting was kept and committed. The Core dsp avg of the standalone heavy take rose from 2.70-2.82 %
to 5.21 %, the Editor's level, consistent with 91 real voices being mixed instead of 32.

### NOT met / open after this wave

- Standalone heavy: about 2.75-2.98 dB quieter than the Editor (31 of 31 windows outside ±1 dB) and 2-9 boundary
  silences; real channels pinned at 32 in the player even with 256 / 1024 channel counts. Not fixed in this wave.
  (Later: fixed, see "Final fix wave — M1 follow-up (Vorbis codec count)".)
- Feature bus onset: 0 by construction, not measured.
- `FmodClock` last-good-value path: no test.
## Combo ladder — Task 1 (setVolume probe)

Question: the combo ladder fades layers by lowering the instance volume of their scheduled one-shots every frame
(`OneShotScheduler.SetFade` -> `IOneShotBackend.SetVolume` -> `EventInstance.setVolume`). Does calling `setVolume` every
frame on notes that are handed to FMOD but not yet sounding (armed: paused instance, channel group delay set) move their
start times?

New capture option: `CaptureScenario.fadeProbe` (off by default). When on, every frame it calls `SetFade(-1, 1f)` on
the spike scheduler and on SoundManager's shared scheduler (`_shots`, by reflection). Every note has rung >= 0, so every
handed note (armed or sounding) gets `setVolume` at its own gain, every frame, and each newly handed note gets it right
after its `Start`. The level does not change.

Settings for both takes: Editor, `spike`, seed 33, 68 beats, `spikeDispatchBeats` 0.5, `spikeTarget`
`SpikeClick:KillChords:72`, `shortLeadOnDsp` off, `logOneShots`, full mix, `scheduleOffsetSamples` 0. These are the
`spike_seed33_s8c` settings. The takes differ only in `fadeProbe`.
- Auto refresh was disallowed around each take: one `DisallowAutoRefresh` before Play mode, one `AllowAutoRefresh` after.
- No Editor query was made while a take recorded.
- The Editor window was not focused, and the project's Run In Background is off. Play mode stayed at frame 1 until
  `Application.runInBackground = true` was set from an Editor call, at frame 1-2 of each take, before the capture began.
  This is runtime only: `PlayerSettings.runInBackground` read false again after Play mode, and no settings file changed.
  Both takes ran this way.

| take | `fadeProbe` | unforced stalls | longest frame | Studio avg / max % | Core dsp avg / max % | ONESHOTS | RESYNCED | valid |
|---|---|---:|---|---|---|---|---:|---|
| `spike_seed33_cl1_probe_off` | off | 0 | 247 ms, before the first beat | 0.55 / 1.07 | 0.80 / 1.82 | 562 / 0 / 0 / 10 | 0 | yes |
| `spike_seed33_cl1_probe_on` | on | 0 | 1475 ms, before the first beat | 0.64 / 0.88 | 0.82 / 1.79 | 562 / 0 / 0 / 10 | 0 | yes |

ONESHOTS = started / late / dropped / cancelled. Both takes: SPIKE scheduled 64, started 64, late 0, dropped 0,
cancelled 0; `dropped frames 0`; `CAPTURE dsp-to-frame mismatches 0`; CHANNELS 26 / 20.

Attack (`python FMOD/Tools/analyze_capture.py spike <wav> --xcorr`; detector bias -39.0 samples = -0.812 ms):

| criterion (gate for `probe_on`) | `probe_off` | `probe_on` | verdict |
|---|---|---|---|
| corrected mean \|e\| ≤ 0.10 ms | 0.02 | 0.02 | met |
| corrected max \|e\| ≤ 0.50 ms | 0.04 | 0.04 | met |
| corrected signed mean | -0.01 ms (-0.4 samples) | -0.01 ms (-0.4 samples) | - |
| 4 beats after tempo changes (n 13), mean / max | 0.02 / 0.04 | 0.02 / 0.04 | - |
| missing / late / early | 0 / 0 / 0 | 0 / 0 / 0 | met |
| xcorr lag 0 | 64 / 64 | 64 / 64 | met |
| `.cpu.txt` late / dropped / RESYNCED | 0 / 0 / 0 | 0 / 0 / 0 | met |
| `spike` gate (exit code) | PASS (0) | PASS (0) | - |

Every row of the `spike` output is identical between the two takes. The values also equal `spike_seed33_s8c`.

Verdict: **met**. Calling `setVolume` every frame on armed and sounding one-shots did not move any click: 64 of 64 at lag
0 with and without the probe. The setVolume fade approach of the combo ladder spec stands.

Not measured: whether a scale below 1 lands on the intended level (the probe keeps the level unchanged), and the
`SilenceAbove` stop path. Neither was asked for here.

## Combo ladder — Task 3 (arrangement from one rung; notes carry their rung)

Question: after `ArrangementInputs` moved to `LiveRung` / `AppliedRung` / `RungBar` (with `PinIntensity` / `PinCombo`,
which `SoundManager` fills from its old intensity and latched combo in this task) and pattern notes carry
`OneShotNote.Rung`, does the game sound exactly as before?

Settings for every take: Editor, `baseline`, seed 7, 128 beats, `logOneShots`, full mix, `scheduleOffsetSamples` 0,
`fadeProbe` off (the `baseline_seed7_s9f` settings). Auto refresh disallowed around each take, no Editor query while a
take recorded, `Application.runInBackground = true` set at frame 2 of each take from an Editor call (runtime only;
`PlayerSettings.runInBackground` read false afterwards, no settings file changed).

| take | code | unforced stalls | longest frame | Studio avg / max % | Core dsp avg / max % | ONESHOTS | PATTERNS sched. / started | valid |
|---|---|---:|---|---|---|---|---|---|
| `baseline_seed7_cl3` | Task 3 | 0 | 1671 ms, before the first beat | 0.79 / 1.33 | 1.44 / 3.08 | 1468 / 0 / 0 / 6 | 1184 / 1180 | yes |
| `baseline_seed7_cl3_headref` | `1614a9a` (Task 3 taken out of the working tree) | **1** | 1661 ms | 0.78 / 1.89 | 1.43 / 3.53 | 1472 / 0 / 0 / 10 | 1189 / 1184 | **no** |
| `baseline_seed7_cl3_headref_r2` | `1614a9a` (same) | 0 | 1714 ms, before the first beat | 0.85 / 1.31 | 1.51 / 3.30 | 1468 / 0 / 0 / 10 | 1184 / 1180 | yes |
| `baseline_seed7_cl3_repeat` | Task 3 | 0 | 1874 ms, before the first beat | 0.83 / 1.26 | 1.49 / 3.15 | 1472 / 0 / 0 / 10 | 1189 / 1184 | yes |

ONESHOTS = started / late / dropped / cancelled. RESYNCED 0, dsp-to-frame mismatches 0, CHANNELS 34 / 30 in every take.
The started / cancelled counts vary between takes of the same code (1468 / 1472, 6 / 10), so they are not a code
difference.

| criterion | result | verdict |
|---|---|---|
| `baseline_seed7_cl3` against `baseline_seed7_s9f` (`rms --compare`, 4-beat windows), every window within ±0.1 dB | max \|diff\| **2.199 dB**, 18 of 31 windows outside ±0.1 dB (windows 8-11, 14-18, 20-22, 24-29; windows 0-7 at 0.00) | **NOT met** |
| `baseline_seed7_cl3` against `baseline_seed7_cl3_headref_r2` (same take at the commit before this task), every window within ±0.1 dB | max \|diff\| **0.000 dB** over 31 windows; the `.patterns.csv` note lists (beat, instrument, bus, MIDI) are identical, 1184 notes each | met |
| `baseline_seed7_cl3_repeat` against `baseline_seed7_cl3_headref_r2` | max \|diff\| 0.000 dB over 31 windows | met |

Cause of the `s9f` difference (not this task): `s9f` was recorded before the commits after `0a166fa` that added feature
loops, the fever selector and the progression selector (`d282224`, `ecf3674`, ...). From beat 32 on the two takes pick
different feature loops (`s9f`: PowerChord in phrase 2 and Trumpet in phrase 7; now: FeaturePerc and Whistle) and play
the tension layers on other pitches (another chord progression). `s9f` is therefore no reference for the current code;
the reference for this task is `baseline_seed7_cl3_headref_r2`, recorded in the same session from `1614a9a` (the Task 3
changes were taken out of the working tree with `git apply -R` of their diff and put back afterwards, nothing committed
in between).

Verdict: no audible change from Task 3 (0.000 dB against the same code without it; the same pattern notes).

## Combo ladder — Task 4 (the screen combo drives the ladder; capture scenarios pin their arrangement)

Question: `SoundManager` now plays the ladder's rung (`LadderState`: screen combo `ComboCount` and round time, raised on
bar lines, faded over 2 beats above the floor when the combo breaks). The capture scenarios never set `ComboCount`, so
they pin their old arrangement with `SoundManager.PinArrangement(intensity, combo)` instead (otherwise the floor would
rise with the round time):

| scenario | pinned intensity | pinned combo | `SoundManager.Intensity` (section choice) |
|---|---:|---:|---|
| `heavy` | 0 (or the `intensity` option) | 3 | 0 (or the option) |
| `duck` | 3 | 0 | 3 |
| `baseline`, `spike`, `styles`, `features`, `fever` | 0 (or the `intensity` option) | 0 | 0 (or the option) |
| any, after `restartAfterStop` | 0 | 3 for `heavy`, else 0 | 0 (as before: only heavy's combo and tension were re-applied) |

Does a pinned take sound as before? Settings for both takes: Editor, `heavy`, seed 7, 128 beats, `logOneShots`, full
mix, `scheduleOffsetSamples` 0, `fadeProbe` off (the `heavy_seed7_s9f` settings). `heavy_seed7_s9f` itself is stale
(music content changed after it, see Task 3), so the reference `heavy_seed7_cl4_ref` was recorded first on `9586309`
(Task 3, untouched tree), then `heavy_seed7_cl4` with this task's code. Auto refresh disallowed around each take, no
Editor query while a take recorded, `Application.runInBackground = true` set at frame 2 of each take from an Editor call
(runtime only; `PlayerSettings.runInBackground` read false afterwards; ProjectSettings MD5s identical before and after).

| take | code | unforced stalls | longest frame | Studio avg / max % | Core dsp avg / max % | ONESHOTS | PATTERNS sched. / started | valid |
|---|---|---:|---|---|---|---|---|---|
| `heavy_seed7_cl4_ref` | `9586309` | 0 | 1673 ms, before the first beat | 1.57 / 2.32 | 5.64 / 9.22 | 4738 / 0 / 0 / 43 | 1677 / 1669 | yes |
| `heavy_seed7_cl4` | Task 4 | 0 | 2140 ms, before the first beat | 1.54 / 2.75 | 5.51 / 6.89 | 4733 / 0 / 0 / 7 | 1671 / 1664 | yes |

ONESHOTS = started / late / dropped / cancelled. KILLS 3069 started in both, RESYNCED 0, dsp-to-frame mismatches 0,
CHANNELS 87 / 87 in both.

| criterion | result | verdict |
|---|---|---|
| `heavy_seed7_cl4` against `heavy_seed7_cl4_ref` (`rms --compare`, 4-beat windows), every window within ±0.1 dB (the first bar may differ) | max \|diff\| **0.094 dB** (window 0, beats 0-4: -30.41 vs -30.51 dBFS); windows 1-30 0.00 | met |

Window 0 is the expected first-bar difference. In the reference the heavy scenario's combo 3 reached the arrangement
one bar late (Task 3's interim pin read the bar-latched combo, which was still 0 while bar 0 was scheduled in the start
lead): at the bar-0 latch the schedulers were invalidated, so `cl4_ref` lacks the GlassPad chord (ComboPad, MIDI
67 / 72 / 76) on beat 0, which was inside the onset safety and never re-stated, and lists nine notes of beats 0.5-3
twice (SineSub 36 and Acid 36 at 0.5; Acid 48, Drums 38 / 42 at 1; Drums 36 / 42 at 2; Drums 38 / 42 at 3), the first
copies cancelled by the invalidation (cancelled 43 against 7). With `PinArrangement` combo 3 applies from the start:
`cl4` plays the beat-0 GlassPad chord and schedules every note once. Every other scheduled note (`.patterns.csv`: beat,
instrument, bus, MIDI) is identical between the takes.

Not measured here: a combo break and its fade (no scenario sets `ComboCount` yet).

## Combo ladder — Task 6 (a combo break, measured)

Question (spec sections 5.3 and 7): when the screen combo breaks above the floor, do the layers above the floor fade
over 2 beats on 1 - t² and then stay silent, while the floor layers do not change, without clicks and on time?

### Scenario `combobreak` (`CaptureScenario`)

- No kills, no `QueueFever` (no fever). `SoundManager.ComboCount` is set by the scenario from the **mixer beat**
  (`BeatClock.BeatAt(FmodClock.MasterDsp())`): +1 on every whole beat from beat 4, capped at `comboMax` (default 10),
  0 at each beat of `comboBreakBeats` (default `"40.5"`, comma-separated; empty = never broken), then +1 on every beat
  again. A break on a whole beat comes after that beat's +1. Why the mixer beat: the ladder takes the break at its
  `_audioBeat` (the mixer beat) and times the fade in it, and the WAV is cut along the mixer's beat log, so the break
  beat in the scenario is the break beat in the recording. The visual beat runs `visualLatencyMs` behind and would put
  the break that much later in the mix. `SoundManager` runs first in the frame (execution order -100), so it reads the
  new combo on the next frame, as it would a combo set by game code.
- Guest loops: the existing `holdArrangement` already keeps them out (`FeatureSelector.CancelNext()` every frame), so
  `combobreak` applies it always and no `featuresOff` option was added. It also holds the style at 0 (synthpop) and
  the variation at 0, the same in every take. `.ladder.csv` confirms 0 guest-loop rows and 0 fever rows in every take.
- The arrangement is the ladder's (`PinArrangement` is not called), unless `intensity` >= 0: then
  `PinArrangement(intensity, 0)` (used for the rung-0 take). Other scenarios are unchanged.
- Every frame one `.ladder.csv` row: `frame` (Unity frame), `beat` (SoundManager's `_audioBeat`, the beat the ladder
  used this frame), `combo`, `rung` (`LadderRung`), `floor` (`LadderFloor`), `scale` (`ComboFadeScale`), and three more
  columns: `visual_beat`, `capture_frame` (`AudioCapture.FramesCaptured` on the main thread), `fever` (fever active or
  pending), `feature` (id of `CurrentFeature`, 0 = none). In the pinned take `rung` is the ladder's own value; the pin
  overrides what is played.

### Analysis (`analyze_capture.py ladderfade`, `kicks`)

`ladderfade <take> --break-beat B --reference <no-break take> --rung0 <rung-0 take> [--edge-beat E]`: half-beat
windows from B-2 to B+4, all buses together, each take cut along its own beat log (fractional beats interpolated
between the logged whole beats). A full mix cannot show "the layers above the floor" by itself: the floor layers keep
playing. The takes are deterministic, so they are compared sample for sample: the alignment check comes first, then
`take - rung0` and `ref - rung0` are the above-floor signals (floor layers cancel), and their RMS ratio is the fade gain.
`kicks` compares the first 30 ms after every even beat (the kick beats, `SoundManager.IsKickBeat`).

Self-test (`ladderfade --selftest`: a 1 kHz sine at 116 BPM under 1 - t² from beat 4.5, written as a WAV + `.beats.csv`
and read back through the same window code; expected = window mean of the gain as RMS, 10 log10 mean(g²)):

```
window (beats)   measured dB vs ref   expected dB   diff
 4.50- 5.00         -0.18                -0.18     -0.000
 5.00- 5.50         -1.35                -1.35     -0.000
 5.50- 6.00         -4.28                -4.28     +0.001
 6.00- 6.50        -11.64               -11.64     +0.003
selftest PASS (max |diff| 0.003 dB, limit 0.5 dB)
```

### Takes (Editor, seed 7, 64 beats, `combobreak`, `logOneShots`, full mix, `MusicMix` 1, `scheduleOffsetSamples` 0)

Auto refresh disallowed around each take, no Editor query while a take recorded, `Application.runInBackground = true`
set at frame 2 of each take from an Editor call (runtime only; `PlayerSettings.runInBackground` read false after every
take; ProjectSettings MD5s identical before and after the session). The scene fields were set without saving the scene.

| take | settings | unforced stalls | longest frame | Studio avg / max % | Core dsp avg / max % | ONESHOTS | PATTERNS sched. / started | RESYNCED | valid |
|---|---|---:|---|---|---|---|---|---:|---|
| `combobreak_seed7_cl6_ref` | `comboBreakBeats` empty | 0 | 1615 ms, before the first beat | 0.86 / 1.22 | 1.68 / 2.12 | 774 / 0 / 0 / 35 | 793 / 774 | 0 | yes |
| `combobreak_seed7_cl6_b405` | `"40.5"` | 0 | 1569 ms, before the first beat | 0.76 / 1.11 | 1.49 / 2.61 | 778 / 0 / 0 / 74 | 820 / 778 | 0 | yes |
| `combobreak_seed7_cl6_b47` | `"47.0"` (the fade crosses bar line 48, also a phrase boundary 116 -> 108 BPM) | 0 | 1647 ms, before the first beat | 0.78 / 1.14 | 1.52 / 2.49 | 770 / 0 / 0 / 72 | 806 / 770 | 0 | yes |
| `combobreak_seed7_cl6_floor` | `"40.5"`, `comboMax` 2 (rung 1: the fade is the pluck only) | 0 | 1591 ms, before the first beat | 0.60 / 0.83 | 0.85 / 1.07 | 481 / 0 / 0 / 23 | 493 / 481 | 0 | yes |
| `combobreak_seed7_cl6_floor_ref` (extra) | `comboBreakBeats` empty, `comboMax` 2 | 0 | 1723 ms, before the first beat | 0.63 / 1.23 | 0.88 / 1.06 | 473 / 0 / 0 / 14 | 477 / 473 | 0 | yes |
| `combobreak_seed7_cl6_rung0` | `comboBreakBeats` empty, `intensity` 0 (`PinArrangement(0, 0)`) | 0 | 1751 ms, before the first beat | 0.61 / 1.07 | 0.76 / 0.94 | 413 / 0 / 0 / 10 | 416 / 413 | 0 | yes |

ONESHOTS = started / late / dropped / cancelled. Every take: `dropped frames 0`, dsp-to-frame mismatches 0, late 0,
dropped 0. `floor_ref` is not in the plan's take list: the `floor` take keeps only rung 1, so the like-for-like
reference for it is the same combo without the break (`ref` plays rung 6 there).

Alignment (the condition for every difference below): before the break, `b405` and `b47` equal `ref` sample for sample
(`b405` residual 118 dB below the reference over beats 0-40.5, `b47` identical over 0-47, every 4-beat window 0.000 dB),
`floor` equals `floor_ref` (identical over 0-40.5), and beats 0-4 (combo 0) of every take equal `rung0` (identical).
After the fade the break takes equal `rung0` to within -125 dB of residual, except the floor-layer notes the fade end
re-schedules (see "Noticed" below).

### Rung timeline (`.ladder.csv`, rung heard at the first frame of each bar)

Rules: thresholds 2 / 3 / 5 / 6 / 8 / 10 -> rung 1..6, latched on the next bar line; floor 0 before 30 s of round time,
then 1. Combo = beat - 3 from beat 4: combo 4 at beat 7 (rung 2), 8 at beat 11 (rung 5), 10 at beat 13 (rung 6).

| take | bar 0 1 2 3 4 ... 9 10 11 12 13 14 15 | notes |
|---|---|---|
| `ref` | 0 0 2 5 6 ... 6 6 6 6 6 6 6 | as the rules predict; floor 0 -> 1 at beat 55.07 (30 s) |
| `b405` | 0 0 2 5 6 ... 6 6 2 4 6 6 6 | break set at mixer beat 40.535, fade 40.535-42.556 (rung 6 kept), rung 0 at 42.556; bar 11 rung 2 (combo 3 at 43), bar 12 rung 4 (combo 7 at 47), bar 13 rung 6 |
| `b47` | 0 0 2 5 6 ... 6 6 6 6 2 5 6 | break at 47.011, bar 12 (beat 48) keeps rung 6 inside the fade, rung 0 at 49.037, bar 13 rung 2, bar 14 rung 5 |
| `floor` | 0 0 1 1 1 ... 1 1 1 1 1 1 1 | rung 1 from bar 2; break 40.535, rung 0 at 42.556, rung 1 again at bar 11 (beat 44) |
| `rung0` | (pinned 0 throughout; the ladder column itself reads as `ref`) | |

All match the rules. Because the scenario counts up again right after the break, `b405`'s "after" span (42.5-44.5)
includes the first half beat of bar 11 at rung 2; its rung-2 notes (off-beat pluck, open hat at 44.5) start at 44.5, so
42.5-44.5 still sounds rung 0 (above-floor residual -81.8 dBFS in 44.0-44.5).

### Fade, measured (`ladderfade`; diff = take - `ref`; gain = above-floor gain against `ref`, expected = 1 - t² window mean)

| window (beats after the break) | `b405` diff dB | `b405` gain / expected dB | `b47` diff dB | `b47` gain / expected dB |
|---|---:|---:|---:|---:|
| -2.0 .. 0 (4 windows) | 0.00 0.00 0.00 0.00 | 0.0 | 0.00 0.00 0.00 0.00 | 0.0 |
| 0.0-0.5 | -0.04 | -0.1 / -0.18 | -0.07 | -0.1 / -0.18 |
| 0.5-1.0 | -0.77 | -1.0 / -1.35 | -0.76 | -0.9 / -1.35 |
| 1.0-1.5 | -2.66 | -3.2 / -4.28 | -0.18 | -3.7 / -4.28 |
| 1.5-2.0 | -0.60 | -9.1 / -11.64 | -4.10 | -9.3 / -11.64 |
| 2.0-2.5 | -3.40 | -30.0 (residual -67 dBFS) | -6.92 | -37.5 (residual -74 dBFS) |
| 2.5-4.0 | -5.13 -7.60 -0.27 | silent, silent, -38.5 (residual -82 dBFS) | -9.64 -0.77 -4.89 | -112, -18.4, -92 (see "Noticed") |

The above-floor gain follows the curve about 0.08 beat late (1/8-beat steps, `b405`: -0.5 / -0.7 at 41.0, -3.9 / -4.9
at 41.75, -8.6 / -10.8 at 42.125, -15.8 / -23.0 at 42.375; `b47` -4.2 / -4.9 at 48.25, -12.2 / -14.8 at 48.75,
-17.5 / -23.0 at 48.875). The lag is the timing chain:
the break is set on the first mixer block at or after the break beat (+0.035 beat at 40.5), the ladder starts the fade
on the next frame, and a `setVolume` reaches the mix on the next mixer block (1024 samples = 0.041 beat at 116 BPM).

### End of the fade (the immediate stop above the floor)

At the frame the rung drops (`SetFade(floor, 0)`, then `SilenceAbove` stops every sounding note above the floor), the
last scale the ladder applied was 0.020 (`b405`, -33.9 dB) / 0.012 (`b47`, -38.6 dB). Because of the lag above, the
above-floor signal in the 30 ms before the stop is not yet at the last scale but 20-26 dB below its unfaded level:
`b405` -55 .. -62 dBFS (the same span in `ref` - `rung0`: -34 .. -37 dBFS), `b47` -57 .. -68 dBFS (ref -35 .. -44),
`floor` -59 .. -66 dBFS (floor_ref -39 .. -42), under a mix of about -40 dBFS. In 2 ms windows from the main-thread
frame of the drop: `b405` -62 -> -70 dBFS within 2 ms, -71 .. -76 for 20 ms (one mixer block), then below -108 dBFS from
+24 ms; `b47` the same shape (-64 -> -75, below -112 from +24 ms); `floor` -65 -> -95 within 4 ms, -120 at +6 ms. No isolated peak at the stop: `clicks` 0 (raw) in the fade and in the half beat after it, in all three break
takes; on the first difference (a high-pass) also 0 for `b405` and `b47`; `floor` shows 2 at 42.523 / 42.552 at
-77 / -80 dB, before the rung drop and about 23 dB below the 3 such peaks the unfaded pluck makes in the same half beat
of `floor_ref` (-54 .. -58 dB), i.e. the pluck's own waveform, not the stop. The stop is therefore a cut from about
-20 .. -26 dB relative (about -60 dBFS absolute); nothing in the measurement shows it as a click, but it is not the
"volume already 0" the plan assumed.

Second measurement (the reviewer's, independent of `ladderfade`, same takes): a least-squares fit of the 1/16-beat
above-floor gain against 1 - t² gives a lag of 0.100 beat (`b405`) and 0.075 beat (`b47`). At the stop the above-floor
signal is about -24 dB relative to its unfaded level (-55 .. -62 dBFS). The cut is a two-stage drop: about -8 dB within
2 ms, a plateau around -73 .. -77 dBFS for about one mixer block, gone at +22 .. +24 ms; no sample discontinuity (largest
sample-to-sample step within ±5 ms of the stop 1.8e-3, against the music's own 3.1e-3).

### Criteria (spec 7, plan Task 6 Step 3)

| criterion | measured | verdict |
|---|---|---|
| Monotonic fall over the 2-beat fade: the difference to `ref` falls in each of the four half-beat windows (each <= the one before) | full mix: `b405` -0.04, -0.77, -2.66, **-0.60**; `b47` -0.07, -0.76, **-0.18**, -4.10. The 4th (`b405`) / 3rd (`b47`) window starts with a kick (beat 42 / 48, floor layer, unchanged), which dominates the window's RMS. The above-floor gain (take - rung0 against ref - rung0) does fall in every window: `b405` -0.1, -1.0, -3.2, -9.1; `b47` -0.1, -0.9, -3.7, -9.3 dB, and in every 1/8-beat step | **NOT met** as written (full-mix difference); the faded layers themselves fall monotonically |
| Then 0: the 2 beats after the fade within ±1 dB of `rung0` at the same beats | `b405` -36.21 vs -36.21 dBFS (+0.00 dB); `b47` -37.11 vs -37.11 (+0.00); `floor` -36.21 vs -36.21 (+0.00). Above-floor residual -67 / -74 / -72 dBFS in the first half beat (the stop's last block), then silent | met |
| Floor unchanged: `floor` take, first 30 ms RMS of the kick beats before and after the break against `ref` ±1 dB | beats 36-46: -0.21, -0.62, -0.17, -0.56, -0.19, -0.64 dB (max 0.64; `ref` has the rung-6 layers on top). Against `floor_ref` and `rung0`: 0.00 dB at every kick beat | met |
| No clicks: `clicks` 0 in all three break takes | `b405` 0, `b47` 0, `floor` 0 (raw, fade and the half beat after it). The detector counts a 1 ms window more than 12 dB above BOTH neighbours (the both-neighbours reading of "+12 dB against the adjacent 1 ms windows"); a stop is a drop in level, which this cannot see, so "clicks 0" is evidence about the fade, not about the end-of-fade stop. The evidence for the stop is the 2 ms table in "End of the fade" above | met (for the fade; the stop is covered there) |
| Timing (a): `.cpu.txt` late 0 / dropped 0 / RESYNCED 0 | 0 / 0 / 0 in all six takes | met |
| Timing (b): `rung` at the floor at break + 2.0 beats, tolerance 1 frame | `b405` 42.5564 = break + **2.0564**; `b47` 49.0374 = break + **2.0374**; `floor` as `b405`. One Unity frame during the fade is about 0.008 beat (`b405`: 2.02 beats over 253 frames), so the drop is about 5-7 frames late. Why: the ladder's beat advances once per mixer block (1024 samples = 0.041 beat at 116 BPM); the break is picked up on the first block after the break beat (fade start 40.5352 / 47.0106, SoundManager reads the combo one frame after the scenario sets it), and the fade itself lasts 2.0212 / 2.0269 beats (it ends on the first block at or after start + 2.0). Even against one mixer block (0.041 beat) instead of one frame, `b405` (+0.056) still misses; `b47` (+0.037) is within it. (The analyzer's printed "largest beat step between frames", 0.077 beat, is about two mixer blocks and is not the brief's tolerance.) | **NOT met** as written (+2.056 / +2.037 beats) |
| Fade across a bar line (`b47`): 20 ms before / after beat 48, the take's RMS step within the `ref` step + the fade's slope | full mix: take -42.33 / -29.06 dBFS (step +13.27 dB), `ref` -40.71 / -29.00 (step +11.70); take step - ref step **+1.57 dB**, fade curve step between the window centres -0.22 dB. The before window is quiet, so the faded layers are a larger share of it than of the kick-led after window. The above-floor gain itself: -1.99 dB before, -2.18 dB after (step -0.19 dB against the curve's -0.22): no jump at the bar line; the ladder kept rung 6 across it | **NOT met** as written (full mix, +1.57 dB beyond the bound); the faded layers make no step at the bar line |

Listening was not part of this task. What the numbers say a listener should hear: the layers above the floor sink
smoothly over the 2 beats, about 0.08-0.10 beat (40-50 ms) behind the beat-exact curve, and are gone 2.06 beats after the break; the kick,
snare, hat and bass do not change.

### NOT met / open

- Monotonic fall and fade across a bar line: NOT met as written (full-mix window RMS); the above-floor gain meets both.
- Timing (b): the rung reaches the floor at break + 2.056 / + 2.037 beats, not break + 2.0 ± 1 frame (break picked up
  one mixer block late, fade end on the next block after start + 2.0).
- End-of-fade stop: spec 5.3 step 3 expects what is stopped to be inaudible; the stop happens at about -24 dB relative,
  not at 0 — open, to be fixed in the fade code (not in this task).

### Noticed, not changed

- The monotonic and bar-line "NOT met" rows fail on the measure, not on the fade: a full-mix RMS difference per window
  is dominated by whichever floor hit (kick) falls in the window. The above-floor gain is the measure that isolates the
  faded layers. Whether those criteria should be judged on it is a decision for the plan, not for this task.
- The stop at the end of the fade cuts notes that are still at about -20 .. -26 dB relative (the fade lags its curve
  by about 0.08 beat, so the last frames' scale 0.01-0.02 has not reached the mix). No click is measurable. Not
  changed here (fade code is outside this task); options would be timing the fade from the break beat rather than the
  frame that saw it, or stopping a mixer block after the last volume call.
- At the fade end `RefreshArrangement` re-schedules floor-layer notes that were already scheduled. In `b47` the kick /
  hat at beat 50 were first scheduled (frame 5854, before the phrase anchor at 48) at beat DSP 1393733 and re-scheduled
  (frame 6239) at 1393732: the clock's `DspAt(50)` differs by 1 sample before and after the anchor. In `rung0` those two
  notes kept the pre-anchor value. This leaves a residual of -48.5 dBFS for 1/16 beat at beat 50 (a 1-sample shift of
  the kick, 21 µs); level within 0.00 dB.
- `.ladder.csv` is not covered by the `Docs/Captures` ignore rules (it is not committed).
