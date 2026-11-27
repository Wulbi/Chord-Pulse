using System;

/// <summary>
/// The combo ladder as it is played (pure; SoundManager calls <see cref="Update"/> once per frame at the mixer's beat).
/// The rung (<see cref="ComboLadder.Rung"/> of the screen combo and the round time) rises on the next bar line; a broken
/// combo above the floor starts a <see cref="ComboFade"/>, during which the arrangement keeps the rung from before the
/// break, and when it ends the rung drops to the floor at once. Inside the fever phrase a break starts no fade.
/// </summary>
public sealed class LadderState
{
    /// <summary>What one <see cref="Update"/> did.</summary>
    public struct Step
    {
        /// <summary>A fade began this frame.</summary>
        public bool FadeStarted;
        /// <summary>The fade finished this frame: the notes above <see cref="Floor"/> are to be removed.</summary>
        public bool FadeEnded;
        /// <summary>The fade's floor while a fade started / runs / ended this frame, otherwise the time floor.</summary>
        public int Floor;
        /// <summary>Fade value now (1 = whole, 0 = gone; 0 on the frame the fade ends); 1 without a fade.</summary>
        public float Scale;
    }

    int _previousCombo;
    bool _wasFeverPhrase;

    /// <summary>Rung for the bars after <see cref="RungBar"/> (ArrangementInputs.LiveRung).</summary>
    public int LiveRung { get; private set; }
    /// <summary>Rung of the bars up to and including <see cref="RungBar"/> (-1: none latched yet, plays as 0).</summary>
    public int AppliedRung { get; private set; }
    public int RungBar { get; private set; }
    /// <summary>The time floor (<see cref="ComboLadder.FloorForSeconds"/>) at the last update.</summary>
    public int Floor { get; private set; }
    public ComboFade Fade { get; } = new ComboFade();

    /// <summary>The rung being played now: the rung before the break during a fade, otherwise the latched one.</summary>
    public int HeardRung => Fade.Active ? Fade.FromRung : Math.Max(0, AppliedRung);

    public LadderState() { Reset(); }

    public void Reset()
    {
        Fade.Clear();
        LiveRung = 0;
        AppliedRung = -1;
        RungBar = -1;
        Floor = 0;
        _previousCombo = 0;
        _wasFeverPhrase = false;
    }

    /// <param name="combo">Screen combo (negative reads 0).</param>
    /// <param name="seconds">Round time, for the floor.</param>
    /// <param name="beat">Beat position (the fade is timed in beats).</param>
    /// <param name="bar">Bar of <paramref name="beat"/>.</param>
    /// <param name="feverPhrase">Inside the 4 fever bars (not the build-up bar).</param>
    public Step Update(int combo, double seconds, double beat, int bar, bool feverPhrase)
    {
        combo = Math.Max(0, combo);
        bool broke = _previousCombo > 0 && combo == 0;
        _previousCombo = combo;
        bool feverEnded = _wasFeverPhrase && !feverPhrase;
        _wasFeverPhrase = feverPhrase;
        Floor = ComboLadder.FloorForSeconds(seconds);
        var step = new Step { Floor = Floor, Scale = 1f };

        if (Fade.Active)
        {
            step.Floor = Fade.Floor;
            if (Fade.Finished(beat))
            {
                // Down to the floor now; a rung rebuilt during the fade comes in on the next bar line.
                AppliedRung = Fade.Floor;
                RungBar = bar;
                LiveRung = ComboLadder.Rung(combo, seconds);
                Fade.Clear();
                step.FadeEnded = true;
                step.Scale = 0f;
            }
            else
            {
                // The arrangement keeps the rung from before the break (also across a bar line).
                LiveRung = AppliedRung = Fade.FromRung;
                RungBar = bar;
                step.Scale = Fade.Scale(beat);
            }
            return step;
        }

        if (feverEnded)
        {
            // The rung after fever comes from the combo and the floor at that moment, not from a latch made in fever.
            LiveRung = AppliedRung = ComboLadder.Rung(combo, seconds);
            RungBar = bar;
            return step;
        }

        // A new bar plays the rung promised for it (the live rung of the frames before this one).
        if (bar != RungBar)
        {
            AppliedRung = LiveRung;
            RungBar = bar;
        }

        if (broke && !feverPhrase && HeardRung > Floor)
        {
            Fade.Begin(beat, HeardRung, Floor);
            LiveRung = AppliedRung = Fade.FromRung;
            step.FadeStarted = true;
            step.Floor = Fade.Floor;
            step.Scale = Fade.Scale(beat);
            return step;
        }

        LiveRung = ComboLadder.Rung(combo, seconds);
        return step;
    }
}
