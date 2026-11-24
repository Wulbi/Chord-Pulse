using System.Collections.Generic;
using System.IO;
using System.Xml;
using NUnit.Framework;

public class SectionTempoTests
{
    const string EventGuid = "{3d468f15-e95b-49c7-89e5-8b0245d9bf6d}";

    // Temporary safety net: removed together with the FMOD music event in a later plan.
    [Test]
    public void SectionBpm_MatchesFmodTempoMarkers()
    {
        string path = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "FMOD", "ChordPulse", "Metadata", "Event", EventGuid + ".xml"));
        Assert.IsTrue(File.Exists(path), "FMOD music event metadata not found: " + path);

        var markers = ReadTempoMarkers(path);
        Assert.IsNotEmpty(markers, "no TempoMarker objects found in " + path);
        markers.Sort((a, b) => a.position.CompareTo(b.position));

        Assert.AreEqual(SectionSelector.SectionCount, markers.Count, "TempoMarker count vs SectionSelector.SectionCount");
        for (int i = 0; i < markers.Count; i++)
            Assert.AreEqual(markers[i].tempo, SectionSelector.Bpm(i), 1e-6,
                $"section {i}: FMOD tempo marker at position {markers[i].position} vs SectionSelector.Bpm({i})");
    }

    // Streams the ~14 MB event file; each TempoMarker object holds <property name="position|tempo"><value>..</value>.
    static List<(double position, double tempo)> ReadTempoMarkers(string path)
    {
        var result = new List<(double, double)>();
        using (var reader = XmlReader.Create(path))
        {
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Name != "object" || reader.GetAttribute("class") != "TempoMarker")
                    continue;
                double position = double.NaN, tempo = double.NaN;
                using (var sub = reader.ReadSubtree())
                {
                    string property = null;
                    while (sub.Read())
                    {
                        if (sub.NodeType != XmlNodeType.Element) continue;
                        if (sub.Name == "property") property = sub.GetAttribute("name");
                        else if (sub.Name == "value" && (property == "position" || property == "tempo"))
                        {
                            double v = double.Parse(sub.ReadElementContentAsString(), System.Globalization.CultureInfo.InvariantCulture);
                            if (property == "position") position = v; else tempo = v;
                            property = null;
                        }
                    }
                }
                Assert.IsFalse(double.IsNaN(position) || double.IsNaN(tempo), "TempoMarker without position/tempo");
                result.Add((position, tempo));
            }
        }
        return result;
    }
}
