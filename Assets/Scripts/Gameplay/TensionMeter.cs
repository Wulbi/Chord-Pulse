using System;
using System.Collections.Generic;

public static class TensionMeter
{
    /// 0..1. coreRadius: core radius, arenaRadius: arena radius.
    public static float Compute(IReadOnlyList<EnemyState> enemies, float coreRadius, float arenaRadius, int health, int maxHealth)
    {
        float outerGap = Math.Max(0.01f,
            arenaRadius * GameplayTuning.TensionOuterRadius - coreRadius - GameplayTuning.BaseEnemyHalfSize);
        float crowdLimit = outerGap * GameplayTuning.TensionCrowdBand;

        float nearestGap = float.MaxValue;
        int crowdCount = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            var e = enemies[i];
            float gap = Math.Max(0f, e.Distance - e.HalfSize - coreRadius);
            if (gap < nearestGap) nearestGap = gap;
            if (gap <= crowdLimit) crowdCount++;
        }

        float proximity = enemies.Count == 0 ? 0f : 1f - Clamp01(nearestGap / outerGap);
        float crowd = Clamp01(Math.Max(0, crowdCount - 1) * GameplayTuning.TensionPerCrowd);
        float lives = health <= 1 ? 0.75f : health < maxHealth ? 0.3f : 0f;
        return Math.Max(Clamp01(proximity * 0.85f + crowd), lives);
    }

    static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
}
