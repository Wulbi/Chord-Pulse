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
| heavy_seed33_s5_fps30 (Editor) | 30 | 8.94 / 71.47 | 5.87 / 7.37 | 1629 / 0 / 0 (0) / 0 | +280 / +330 / +346 / +275, no gap | 0 ms |
| heavy_seed33_s5_fps15 (Editor) | 15 | 9.85 / 78.20 | 5.96 / 7.77 | 1629 / 0 / 0 (0) / 0 | +419 / +394 / +569 / +426, no gap | 0 ms |
| spike_seed33_s5_fps60 (Editor) | 60 | 6.84 / 62.25 | 1.72 / 2.42 | 64 / 0 / 0 (0) / 0 | - | - |
| spike_seed33_s5_fps30 (Editor) | 30 | 7.50 / 65.65 | 1.57 / 2.41 | 64 / 0 / 0 (0) / 0 | - | - |
| spike_seed33_s5_fps15 (Editor) | 15 | 7.61 / 66.71 | 1.72 / 2.56 | 64 / 0 / 0 (0) / 0 | - | - |
| baseline_seed7_s5_standalone60 | 60 | 4.06 / 65.84 | 1.59 / 2.57 | 288 / 0 / 0 (0) / 0 | not measured | - |
| heavy_seed33_s5_standalone60 | 60 | 5.32 / 51.20 | 3.33 / 4.86 | 1629 / 0 / 0 (0) / 24 | +166 / +155 / +175 / +257, gap 285+55 ms at 64 | 0 ms |
| spike_seed33_s5_standalone60 (+ run2, run3) | 60 | 4.76 / 50.72 | 1.34 / 2.08 | 64 / 0 / 0 (0) / 0 | - | - |
| spike_seed33_s5_standalone (frame rate not capped, about 1100 fps) | ~1100 | 5.24 / 54.38 | 0.94 / 2.14 | 64 / 0 / **60** / 0 | - | - |

`landings` delay is the beat-log callback delay (not audible); the 16 / 32 / 48 / 64 landings had no digital silence in any Editor take
and the audio first / second half beat after the boundary is within 1.5 dB of one phrase earlier (-29.6 .. -31.3 dB vs -28.6 .. -29.7 dB).
In the standalone `heavy` take the half beat after each landing is 10 - 13 dB quieter than one phrase earlier (-39 .. -44 dB vs -29.5 dB),
with a silent run at 64; not seen in the Editor, cause not investigated. A first Editor `heavy` 60 fps take (`_s5_fps60`) had 7 stalls and
24 late / 48 landing drops and is not a valid reference (landing drops at beat 33.4 and 66.0, 24 late at 50.1).
Landing drops (kills created inside FMOD Studio's stall after a jump) did not occur in any valid take here.

### Attack error against the beat (`analyze_capture.py spike`, SpikeClick one-shots, offset -110 samples)

| take | paired beats | mean signed error before correction | mean / max abs after correction | clicks audible > 10 ms BEFORE their beat |
|---|---:|---:|---:|---:|
| spike_seed33_s5_fps60 (Editor) | 60 | +0.17 ms (+8 samples) | 0.91 / 2.77 ms | 0 (4 matches at the jump landings are the analyser pairing across a silent beat) |
| spike_seed33_s5_fps60_run2 (Editor) | 60 | +0.29 ms (+14 samples) | 0.92 / 2.58 ms | 0 |
| spike_seed33_s5_fps30 (Editor) | 33 of 64 | +0.12 ms (+6) on the 33 that paired | 1.19 / 3.92 ms on those 33 | about 24 |
| spike_seed33_s5_fps15 (Editor) | 0 of 64 | - | - | about 55 (all but the landing beats), 59 - 250 ms early |
| spike_seed33_s5_standalone60 | 60 | -0.50 ms (-24 samples) | 0.56 / 2.52 ms | 0 |
| spike_seed33_s5_standalone60_run2 | 60 | +0.03 ms (+2) | 1.04 / 3.30 ms | 0 |
| spike_seed33_s5_standalone60_run3 | 60 | -0.14 ms (-6) | 0.96 / 3.34 ms | 0 |

- 60 fps (Editor and standalone) passes the attack criterion (mean <= 2 ms, max <= 5 ms) with a wide margin.
- 30 and 15 fps do not: the scheduler reports `started 64, late 0, dropped 0`, but the clicks that sound early were never reported.
  A click sounds at (about) the moment its instance is dispatched instead of at its DSP time: the analyser sees them 59 - 288 ms
  before the beat, the amount being the lead the note was dispatched with (long lead: window of 0.5 beat; short lead: 0.3 beat).
  Cause (hypothesis, not tested): `FmodOneShotBackend` applies `ChannelGroup.setDelay` on the first main-thread `Update` after
  Studio created the group, and Studio un-pauses the voice on its own update (about 20 ms); with a frame longer than that the voice has
  already sounded. Widening the dispatch window does not help (it only dispatches earlier). R12 (never late) holds at 15 / 30 fps,
  "never early" does not. The kill chords use the same backend, so they are presumably affected the same way (not measured).
- Standalone offset: mean signed error -24 / +2 / -6 samples (mean -9) against the Editor's +8 / +14 (mean +11): a 20-sample difference,
  inside the 24-sample limit and inside the run-to-run spread (sd of one take is about 1.1 ms), so `DefaultScheduleOffsetSamples` stays -110
  for both.
- Standalone with the frame rate left unlimited (about 1100 fps, tiny window): 60 of 64 clicks dropped, because the backend gives
  a channel group 30 frames to appear and 30 frames last only about 27 ms there. A capped or vsynced build is fine; an uncapped one is not.

### Standalone build

`Builds/Capture/<scenario>/ChordPulseCapture.exe` (git-ignored), built from a temporary copy of `AudioCapture.unity` per scenario
(fields set in memory, `SaveScene(..., saveAsCopy: true)`; the copies and their .meta were deleted afterwards) with
`BuildPipeline.BuildPlayer`, scene list of that one scene, `BuildOptions.Development`, `EditorBuildSettings` and ProjectSettings untouched.
The player writes to `Docs/Captures` next to the exe (`Application.dataPath/..`), i.e. `Builds/Capture/<scenario>/Docs/Captures/`, and does
not quit by itself (the exe has to be closed after the `.cpu.txt` appears). Analyse the files from there.
