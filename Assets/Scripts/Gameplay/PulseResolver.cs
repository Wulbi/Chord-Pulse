using System.Collections.Generic;

/// Pure port of one wave's step in ChordPulsePrototype.AdvanceWaves (no UnityEngine).
public sealed class PulseResolver
{
    public const float WaveHitPadding = 0.0275f;
    public const float SimultaneousContactTolerance = 0.01f;

    struct Contact
    {
        public EnemyState Enemy;
        public float Radius;
    }

    readonly List<Contact> _contacts = new List<Contact>();

    /// Advances a pulse one frame. Dead enemies are removed from enemies; what happened is appended to events, nearest first.
    public PulseOutcome Advance(PulseState pulse, List<EnemyState> enemies, float waveSpeed, float maxRadius,
        float deltaTime, List<PulseEvent> events)
    {
        float nextRadius = pulse.Radius + waveSpeed * deltaTime;
        if (nextRadius > maxRadius)
            nextRadius = maxRadius;
        if (nextRadius > pulse.StopRadius)
            nextRadius = pulse.StopRadius;
        _contacts.Clear();

        for (int i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            if (enemy.ImmuneTo == pulse || pulse.Touched.Contains(enemy))
                continue;
            float contactRadius = ContactRadius(enemy, pulse.Radius, waveSpeed);
            if (contactRadius >= 0f && contactRadius <= nextRadius + SimultaneousContactTolerance)
                _contacts.Add(new Contact { Enemy = enemy, Radius = contactRadius });
        }

        SortByRadius();

        float contactLimit = nextRadius;
        for (int i = 0; i < _contacts.Count; i++)
        {
            var contact = _contacts[i];
            if (contact.Radius > contactLimit)
                break;
            pulse.Touched.Add(contact.Enemy);

            // A gray enemy has two hits: a colored pulse only strips the gray, a fever pulse kills it outright.
            bool reveal = contact.Enemy.IsHidden && !pulse.IsFever;
            if (reveal)
            {
                contact.Enemy.IsHidden = false;
                pulse.HitProgress = true;
                events.Add(new PulseEvent(PulseEventType.Reveal, contact.Enemy, 0));
            }

            // An intact chord pair takes one hit per colour: a matching colour strips it (and blocks), no wrong-colour penalty.
            bool strip = false;
            if (!reveal && !pulse.IsFever && contact.Enemy.IsChordPair)
            {
                var enemy = contact.Enemy;
                if (pulse.ColorIndex == enemy.ColorIndex)
                {
                    enemy.ColorIndex = enemy.SecondColorIndex;
                    strip = true;
                }
                else if (pulse.ColorIndex == enemy.SecondColorIndex)
                    strip = true;

                if (strip)
                {
                    enemy.StrippedColorIndex = pulse.ColorIndex;
                    enemy.SecondColorIndex = -1;
                    pulse.HitProgress = true;
                    events.Add(new PulseEvent(PulseEventType.Strip, enemy, 0));
                }
            }

            if (reveal || strip || (!pulse.IsFever && contact.Enemy.ColorIndex != pulse.ColorIndex))
            {
                if (!reveal && !strip)
                {
                    pulse.HitWrongColor = true;
                    var hit = contact.Enemy;
                    if (hit.Kind == EnemyKind.Charger && hit.Boosts < GameplayTuning.ChargerMaxBoosts)
                    {
                        hit.Speed *= GameplayTuning.ChargerBoostMultiplier;
                        hit.Boosts++;
                        events.Add(new PulseEvent(PulseEventType.Boost, hit, 0));
                    }
                }
                // The first stop: the pulse runs on a little further (PulsePassThrough) and can still kill a matching
                // enemy in that stretch, then ends. Later stops in the stretch do not extend it.
                if (!pulse.IsStopping)
                {
                    pulse.StopRadius = contact.Radius + GameplayTuning.PulsePassThrough;
                    if (contactLimit > pulse.StopRadius)
                        contactLimit = pulse.StopRadius;
                }
                continue;
            }

            enemies.Remove(contact.Enemy);
            if (contact.Enemy.Kind == EnemyKind.Splitter)
            {
                for (int c = 0; c < 2; c++)
                {
                    var child = new EnemyState
                    {
                        Kind = EnemyKind.Basic,
                        ColorIndex = contact.Enemy.ColorIndex,
                        Distance = contact.Enemy.Distance,
                        Speed = GameplayTuning.BaseEnemySpeed,
                        HalfSize = GameplayTuning.BaseEnemyHalfSize * GameplayTuning.SplitChildScale,
                        ImmuneTo = pulse,
                    };
                    enemies.Add(child);
                    events.Add(new PulseEvent(PulseEventType.Split, child, 0, contact.Enemy));
                }
            }
            events.Add(new PulseEvent(PulseEventType.Kill, contact.Enemy, ++pulse.Kills));
        }

        // A pulse that was stopped ends as Blocked even if its run-on reaches the edge.
        if (pulse.IsStopping && (nextRadius >= pulse.StopRadius || nextRadius >= maxRadius))
            return PulseOutcome.Blocked;
        if (nextRadius >= maxRadius)
            return PulseOutcome.ReachedEdge;

        pulse.Radius = nextRadius;
        return PulseOutcome.Travelling;
    }

    /// Pulse radius at which a pulse now at <paramref name="pulseRadius"/> meets this enemy's leading edge (both move),
    /// or -1 if the pulse is already past it. The target markers use this too, so they mark exactly what a pulse hits first.
    public static float ContactRadius(EnemyState enemy, float pulseRadius, float waveSpeed)
    {
        float tolerance = enemy.HalfSize + WaveHitPadding;
        if (enemy.Distance + tolerance < pulseRadius)
            return -1f;
        float gap = enemy.Distance - tolerance - pulseRadius;
        float contactTime = (gap > 0f ? gap : 0f) / (waveSpeed + enemy.Speed);
        return pulseRadius + waveSpeed * contactTime;
    }

    // Stable insertion sort: contact lists are tiny and this avoids comparison-delegate allocations.
    void SortByRadius()
    {
        for (int i = 1; i < _contacts.Count; i++)
        {
            var item = _contacts[i];
            int j = i - 1;
            while (j >= 0 && _contacts[j].Radius > item.Radius)
            {
                _contacts[j + 1] = _contacts[j];
                j--;
            }
            _contacts[j + 1] = item;
        }
    }
}
