using NUnit.Framework;

/// <summary>
/// Task 12 follow-up: a multi-sound instrument event starts its voice one mixer block after its channel group's delay
/// start (single-sound events start on it). The backend starts each note's group early by its voice delay and adds its
/// bus family's path delay (Docs/Captures/README.md Task 12, follow-up round), which is 0 on every bus since the music
/// event was deleted (final fix wave: the Harmony / Feature delay only matched the legacy send / return).
/// </summary>
public class OneShotLatencyTests
{
    const ulong Block = 1024;

    [Test]
    public void MultiSoundEvent_HasOneBlockVoiceDelay_SingleSoundNone()
    {
        Assert.AreEqual(1, InstrumentTable.VoiceDelayBlocks(Instrument.Drums, OutputBus.MusicBed));
        Assert.AreEqual(1, InstrumentTable.VoiceDelayBlocks(Instrument.Pluck, OutputBus.Harmony));
        Assert.AreEqual(1, InstrumentTable.VoiceDelayBlocks(Instrument.Marimba, OutputBus.KillChords));
        Assert.AreEqual(0, InstrumentTable.VoiceDelayBlocks(Instrument.SpikeClick, OutputBus.KillChords));
    }

    [Test]
    public void EveryBus_HasNoPathDelay()
    {
        Assert.AreEqual(0, InstrumentTable.PathDelayBlocks(OutputBus.Harmony));
        Assert.AreEqual(0, InstrumentTable.PathDelayBlocks(OutputBus.ComboPad));
        Assert.AreEqual(0, InstrumentTable.PathDelayBlocks(OutputBus.Feature));
        Assert.AreEqual(0, InstrumentTable.PathDelayBlocks(OutputBus.MusicBed));
        Assert.AreEqual(0, InstrumentTable.PathDelayBlocks(OutputBus.Bass));
        Assert.AreEqual(0, InstrumentTable.PathDelayBlocks(OutputBus.KillChords));
    }

    [Test]
    public void DrumNote_GroupStartsOneBlockEarly_StopsExactly()
    {
        Assert.AreEqual(100000UL - Block, OneShotArming.DelayStart(100000, 1, 0, Block));
        Assert.AreEqual(120000UL, OneShotArming.StopAt(120000, 0, Block));
    }

    [Test]
    public void PathDelayedNote_WholeNoteOneBlockLate()
    {
        Assert.AreEqual(100000UL, OneShotArming.DelayStart(100000, 1, 1, Block));
        Assert.AreEqual(120000UL + Block, OneShotArming.StopAt(120000, 1, Block));
    }

    [Test]
    public void SingleSoundEvent_StartsAndStopsOnItsTimes()
    {
        Assert.AreEqual(100000UL, OneShotArming.DelayStart(100000, 0, 0, Block));
        Assert.AreEqual(120000UL, OneShotArming.StopAt(120000, 0, Block));
    }

    [Test]
    public void NoStop_StaysNoStop()
    {
        Assert.AreEqual(0UL, OneShotArming.StopAt(0, 1, Block));
    }
}
