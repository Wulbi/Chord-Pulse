using System;
using System.Collections.Generic;

/// <summary>
/// Picks what each fever sounds like: one fever bed variant (PatternTable.FeverVariantCount, pattern_fever.py VARIANTS)
/// and usually one fever guest loop (<see cref="FeatureSelector.Loop.Fever"/>). Chosen once, when fever is queued, so
/// the whole fever phrase is one arrangement. Never the same variant or the same guest twice in a row.
/// </summary>
public sealed class FeverSelector
{
    /// <summary>Names of the fever bed variants, by id (keep in step with pattern_fever.py VARIANT_NAMES).</summary>
    public static readonly string[] VariantNames = { "Fever Classic", "Disco Drive", "Anthem", "Arp Rush" };

    // Most fevers bring a guest; now and then the bed variant gets the stage to itself.
    const double GuestChance = 0.85;

    readonly Random _random;
    readonly List<FeatureSelector.Loop> _guests = new List<FeatureSelector.Loop>();
    int _lastVariant = -1;
    int _lastGuest;

    public FeverSelector(int seed)
    {
        // Own stream so fever choices do not change the section or feature-loop order for a given seed.
        _random = seed == 0 ? new Random() : new Random(unchecked(seed * 104729 + 3));
        foreach (var loop in FeatureLoopNotes.Loops)
        {
            if (loop.Fever)
                _guests.Add(loop);
        }
    }

    /// <summary>Variant of the fever last chosen (0 before any).</summary>
    public int Variant { get; private set; }
    /// <summary>Guest loop of the fever last chosen (null = none).</summary>
    public FeatureSelector.Loop Guest { get; private set; }

    public static string VariantName(int variant) =>
        variant >= 0 && variant < VariantNames.Length ? VariantNames[variant] : "Fever";

    /// <summary>Chooses the next fever's variant and guest.</summary>
    public void Choose()
    {
        int count = Math.Max(1, PatternTable.FeverVariantCount);
        int variant = _random.Next(count);
        if (count > 1 && variant == _lastVariant)
            variant = (variant + 1 + _random.Next(count - 1)) % count;
        Variant = _lastVariant = variant;

        Guest = null;
        if (_guests.Count > 0 && _random.NextDouble() < GuestChance)
        {
            int i = _random.Next(_guests.Count);
            if (_guests.Count > 1 && _guests[i].Id == _lastGuest)
                i = (i + 1 + _random.Next(_guests.Count - 1)) % _guests.Count;
            Guest = _guests[i];
        }
        _lastGuest = Guest != null ? Guest.Id : 0;
    }
}
