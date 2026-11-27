/// <summary>
/// The text of the credits roll (uGUI rich text).
/// Sample sources are the ones recorded in FMOD/ChordPulse/SOURCES_AND_LICENSES.txt and sample-manifest.json for the
/// files under FMOD/ChordPulse/Assets (what the bank is built from); candidates kept in SampleLibrary/ are not in
/// the game and are not listed. When a sample batch is added to or removed from the bank, update this list with it.
/// The FMOD line is the in-game credit its licence asks for (Assets/Plugins/FMOD/LICENSE.txt, clause 3).
/// Plain ASCII only: the built-in font has no fallback glyphs on WebGL.
/// </summary>
public static class CreditsContent
{
    const string Heading = "#ffd24d";
    const string Dim = "#9fb4d6";

    public static string Build()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("<size=54><b>CHORD PULSE</b></size>");
        text.AppendLine();
        text.AppendLine("<size=38>Made by Wulbi Kim</size>");
        text.AppendLine();
        text.AppendLine();

        Section(text, "MUSIC");
        text.AppendLine("Original adaptive score,");
        text.AppendLine("sequenced in real time from one-shot samples");
        text.AppendLine();

        Section(text, "AUDIO ENGINE");
        text.AppendLine("FMOD Studio by Firelight Technologies Pty Ltd.");
        text.AppendLine();

        Section(text, "SAMPLES");
        text.AppendLine("All third-party samples are released under CC0 1.0 Universal");
        text.AppendLine();

        Source(text, "Audio Tools Sample Packs", "Audio Tools (Digital Hub)", "audiotools.com.ar/samplepack",
            "Pallor - kick, snare, clap, hi-hats, shaker",
            "AT101V - bass, lead, pluck",
            "ATMD - lead",
            "AT20V, AT700, ATFS1 - synths",
            "AT303, DeviceNTI - synth bass",
            "Thrum - guitar");
        Source(text, "VSCO 2 Community Edition", "Versilian Studios (Sam Gossner) and contributors",
            "github.com/sgossner/VSCO-2-CE",
            "trumpet, French horn, tenor trombone,",
            "flute, clarinet, harp,",
            "glockenspiel, marimba, xylophone");
        Source(text, "Versilian Community Sample Library", "Versilian Studios (Sam Gossner) and contributors",
            "github.com/sgossner/VCSL",
            "vibraphone, kalimba, FM keys (Yamaha TX81Z), tenor saxophone,",
            "agogo, bongos, claves, conga, cowbell, guiro,",
            "triangle, tambourine, sleigh bells, woodblock,",
            "slit drum, anvil, slapstick, siren whistle");

        text.AppendLine();
        text.AppendLine("<size=38>Thank you for playing</size>");
        return text.ToString();
    }

    static void Section(System.Text.StringBuilder text, string title)
    {
        text.AppendLine($"<size=30><b><color={Heading}>{title}</color></b></size>");
    }

    static void Source(System.Text.StringBuilder text, string pack, string creator, string url, params string[] contents)
    {
        text.AppendLine($"<b>{pack}</b>");
        text.AppendLine(creator);
        text.AppendLine($"<color={Dim}>{url}</color>");
        foreach (var line in contents)
            text.AppendLine($"<size=24>{line}</size>");
        text.AppendLine();
    }
}
