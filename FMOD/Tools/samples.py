"""
Shared sample tables for the instrument library (instruments.py) and layer modules.
Each entry: (file under FMOD/ChordPulse/Assets, measured root MIDI, peak short-term RMS dB, attack onset seconds).
Clavi-synth files are mapped to their lowest strong partial. Data only.
"""

# (file, measured root MIDI, peak short-term RMS dB, attack onset s)
ORIGINAL = {
    "Red": [("AT101V_ClassicLead_C4_v100.wav", 60, -11.8, 0.0)],
    "Blue": [("ATMD_LuckyLead_E4_v100.wav", 64, -12.0, 0.0)],
    "Yellow": [("AT101V_PolyPluck_C4_v100.wav", 60, -14.0, 0.0)],
}
MARIMBA = [("Mallets/Marimba_hit_Outrigger_F3_loud_01.wav", 65, -30.7, 0.006),
           ("Mallets/Marimba_hit_Outrigger_C4_loud_01.wav", 72, -33.2, 0.005),
           ("Mallets/Marimba_hit_Outrigger_G4_loud_01.wav", 79, -32.2, 0.005),
           ("Mallets/Marimba_hit_Outrigger_F5_loud_01.wav", 89, -37.7, 0.004)]
VIBES = [("Mallets/Vibes_hard_C3_v3_rr1_Main.wav", 60, -17.4, 0.007),
         ("Mallets/Vibes_hard_E3_v3_rr1_Main.wav", 64, -24.5, 0.007),
         ("Mallets/Vibes_hard_D4_v3_rr1_Main.wav", 74, -18.0, 0.007),
         ("Mallets/Vibes_hard_A4_v3_rr1_Main.wav", 81, -21.0, 0.007),
         ("Mallets/Vibes_hard_C5_v3_rr1_Main.wav", 84, -17.4, 0.006)]
GLOCK = [("Mallets/glock_medium_G4.wav", 79, -40.9, 0.007), ("Mallets/glock_medium_C5.wav", 84, -40.8, 0.007),
         ("Mallets/glock_medium_G5.wav", 91, -40.9, 0.007), ("Mallets/glock_medium_C6.wav", 96, -41.7, 0.007)]
BRASS = [("Synth/AT700_BrassStab_Bb3_v100.wav", 58, -9.1, 0.008), ("Synth/AT700_BrassStab_F4_v100.wav", 65, -10.5, 0.009)]
GUITAR = [("Guitar/Thrum_CleanNote_E3_v100.wav", 52, -11.0, 0.025), ("Guitar/Thrum_CleanNote_G4_v100.wav", 55, -14.1, 0.025),
          ("Guitar/Thrum_CleanNote_A3_v100.wav", 57, -14.2, 0.025), ("Guitar/Thrum_CleanNote_D4_v100.wav", 62, -16.6, 0.025)]
# Clavi-synth: weak fundamental, so files are mapped to their lowest strong partial (C2 file -> C4, and so on).
CLAV = [("FMKeys/Clavisynth_C2_vl3.wav", 60, -22.6, 0.007), ("FMKeys/Clavisynth_C3_vl3.wav", 72, -23.2, 0.003),
        ("FMKeys/Clavisynth_C4_vl3.wav", 84, -25.1, 0.002)]
GLASS = [("Synth/ATFS1_GlassMarimba_C4_v100.wav", 60, -13.7, 0.001), ("Synth/ATFS1_GlassMarimba_G4_v100.wav", 67, -13.2, 0.0),
         ("Synth/ATFS1_GlassMarimba_C5_v100.wav", 72, -13.3, 0.0)]
HARP = [("Mallets/KSHarp_D4_mf.wav", 62, -31.6, 0.026), ("Mallets/KSHarp_F4_mf.wav", 65, -28.9, 0.015),
        ("Mallets/KSHarp_A4_mf.wav", 69, -30.8, 0.021), ("Mallets/KSHarp_C5_mf.wav", 72, -32.0, 0.026),
        ("Mallets/KSHarp_E5_mf.wav", 76, -29.7, 0.025)]
BELLS = [("Synth/ATFS1_FormantBells_C5_v100.wav", 72, -9.2, 0.001), ("Synth/ATFS1_FormantBells_G5_v100.wav", 79, -9.0, 0.0)]
BASS = [("AT101V_101Bass_C2_v100.wav", 36, -15.6, 0.001)]
SUB = [("SynthBass/DeviceNTI_SineSub_C2_v100.wav", 36, -9.8, 0.0)]


def pick(table, midi, max_pitch=15):
    s = min(table, key=lambda x: (abs(midi - x[1]), -x[1]))
    pitch = midi - s[1]
    assert -max_pitch <= pitch <= max_pitch, (s[0], midi, pitch)
    return s, pitch
