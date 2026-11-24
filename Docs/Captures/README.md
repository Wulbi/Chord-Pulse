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
