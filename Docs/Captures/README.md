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
4. Check the `.cpu.txt` line: it says `stalls >100 ms after the first beat N`. A take with N > 0 is not a valid reference
   (a main-thread hitch delays the kill chords, which changes the audio). Re-record.

Scenarios (all start `StartMusic()` with `sectionSeed = seed`, Intensity 0):

- `baseline`: every 4th beat one kill of each of the 3 colours (voicing 1).
- `heavy`: every beat 3 kills of each colour (voicing 3), Combo 3 and Tension 0.8 forced after `StartMusic()`.
- `spike`: silent for 3 bars, then a full-voicing kill burst on every beat of the 4th bar, repeating.

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

### Reproducibility

`baseline_seed7.wav` vs `baseline_seed7_run2.wav` (two clean takes): max per-window RMS difference 0.19 dB
(window 31; 0.06 dB or less in the other 31 windows); the limit is 0.3 dB. Takes with a frame stall mid-run differed by up
to 1.2 dB in the windows around the stall, hence the stall check above.

### Window RMS (dBFS, 4-beat windows)

| window | beat | BPM | baseline | heavy |
|---:|---:|---:|---:|---:|
| 0 | 0 | 108 | -35.65 | -32.52 |
| 1 | 4 | 108 | -35.57 | -32.44 |
| 2 | 8 | 108 | -35.04 | -32.47 |
| 3 | 12 | 108 | -35.10 | -32.32 |
| 4 | 16 | 108 | -34.99 | -32.43 |
| 5 | 20 | 108 | -34.81 | -32.38 |
| 6 | 24 | 108 | -34.42 | -32.48 |
| 7 | 28 | 108 | -34.46 | -32.13 |
| 8 | 32 | 116 | -34.76 | -32.26 |
| 9 | 36 | 116 | -34.57 | -32.09 |
| 10 | 40 | 116 | -34.22 | -31.89 |
| 11 | 44 | 116 | -34.30 | -32.51 |
| 12 | 48 | 108 | -34.68 | -32.73 |
| 13 | 52 | 108 | -34.74 | -32.32 |
| 14 | 56 | 108 | -34.41 | -32.48 |
| 15 | 60 | 108 | -34.65 | -32.33 |
| 16 | 64 | 108 | -35.17 | -32.52 |
| 17 | 68 | 108 | -35.16 | -32.33 |
| 18 | 72 | 108 | -34.90 | -32.41 |
| 19 | 76 | 108 | -35.77 | -33.02 |
| 20 | 80 | 108 | -35.26 | -32.99 |
| 21 | 84 | 108 | -35.16 | -32.36 |
| 22 | 88 | 108 | -34.97 | -32.46 |
| 23 | 92 | 108 | -35.71 | -33.09 |
| 24 | 96 | 116 | -34.56 | -32.61 |
| 25 | 100 | 116 | -34.60 | -31.94 |
| 26 | 104 | 116 | -34.30 | -31.79 |
| 27 | 108 | 116 | -32.31 | -31.19 |
| 28 | 112 | 116 | -34.71 | -32.24 |
| 29 | 116 | 116 | -34.59 | -32.11 |
| 30 | 120 | 116 | -34.32 | -31.87 |
| 31 | 124 | 116 | -35.24 | -32.60 |

Windows 11, 12, 19, 20, 23, 24 and 31 are flagged `?` by the tool (tempo change inside the window or a hitch); their values are still fine.

| | baseline | heavy |
|---|---:|---:|
| mean RMS (dBFS) | -34.78 | -32.35 |
| max window RMS (dBFS) | -32.31 | -31.19 |

### FMOD CPU (`System::getCPUUsage`, percent, sampled every frame)

| take | dsp avg | dsp max | total avg | total max |
|---|---:|---:|---:|---:|
| baseline_seed7 | 1.55 | 2.53 | 1.68 | 2.69 |
| baseline_seed7_run2 | 1.61 | 2.85 | 1.74 | 2.97 |
| heavy_seed7 | 3.45 | 5.38 | 3.58 | 5.51 |

total = dsp + stream + update. No dropped capture frames. The editor was running, so absolute CPU is higher than in a
build; compare takes made the same way.
