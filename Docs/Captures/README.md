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

Fevers: 32 and 112 (the QueueFever at 88 landed one phrase later, at 112, by SoundManager's own rule); `kills48` /
`late25`: 32, 80, 128. No note late or dropped in any new take (`.losses.csv` empty). Scheduled minus started (2-3 per
take) not investigated. Core dsp at most 2.85 % (heavy budget 12 %). The first legacy take had two frame stalls (833 /
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

- Without the accepted windows every phase is within 0.27 dB (criterion +-1 dB: **met**). Controls with the same
  script: legacy vs legacy 0.01 dB, new vs new 0.10 dB.
- **Impact at the landing 32** (accepted window): the first fever starts at a jump landing. On the click grid the legacy
  impact is heard about 350 ms after the beat (25 ms RMS blocks: -19 / -17 dBFS 14 blocks after the beat), the new one on
  the beat (-16 dBFS in the beat's first block). The landing click itself comes late on both paths (the timeline jump),
  so the new impact falls at the end of beat 31's window. The impact at 80, 112 and 128 (no landing) matches within
  0.10 dB.
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

`fevxcorr.py` (scratch, uses `featxcorr.py`'s declick / load / ref unchanged): every fever-layer note in its phase, one
measurement per (beat, layer) in the layer's band (lead 1.5-6 kHz, shaker 6-16 kHz, bass fifths 80-250 Hz, roll / snare
2-8 kHz, riser / fall 1.5-8 kHz, impact 40-120 Hz), window [-10, +70] ms, peak >= 0.8, outside = not in [L, L+2) of a
landing. The bed takes (no kills) are used: with a kill chord on every beat the windows hold the kills (60 of 170 notes
reach peak 0.8 in `_legacy_b` / `_new`).

| pair (outside, peak >= 0.8) | n (of 170) | mean | mean abs | max abs |
|---|---:|---:|---:|---:|
| new vs legacy `_bed_legacy` / `_bed_new` | 106 | -1.47 ms | 1.53 | 2.80 (bass fifths 45) |
| new vs legacy `_bed_legacy_b` / `_bed_new_b` | 107 | -1.60 | 1.60 | 3.31 (roll / riser 30.75) |
| legacy vs legacy `_bed_legacy` / `_bed_legacy_b` | 162 | +0.00 | 0.00 | 0.02 |
| new vs new `_bed_new` / `_bed_new_b` | 129 | -0.13 | 0.59 | 2.02 |

Criterion against legacy (mean <= 2 ms, max <= 5 ms): **met** in both pairs. Every layer's new notes are 0.5-2.5 ms
earlier than legacy (bass fifths -2.0 / -2.2: their 1 ms sample onset is also started early); not explained further. About
60 of 170 notes per new-vs-legacy pair stay below peak 0.8 (the window also holds the other bed layers, which differ
between paths), against 3 / 36 for the controls; they are not counted.
