using UnityEngine;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// 4.3: distance based difficulty. Only depends on the world position (km from the start), so chunk
    /// generation stays deterministic (works in edit mode too). Tuning lives in SurvivalSettings.
    /// </summary>
    public static class DifficultyCurve
    {
        /// <summary>Kilometres from the world origin (the road starts there).</summary>
        public static float Km(Vector3 position) => new Vector2(position.x, position.z).magnitude / 1000f;

        /// <summary>0 at the start, 1 at kmForMaxDifficulty and beyond.</summary>
        public static float T(Vector3 position)
        {
            float max = Mathf.Max(0.1f, SurvivalSettings.Instance.kmForMaxDifficulty);
            return Mathf.Clamp01(Km(position) / max);
        }

        public static int ScaleEnemyCount(int baseCount, Vector3 position)
        {
            float bonus = SurvivalSettings.Instance.enemyCountBonusAtMax * T(position);
            return Mathf.Max(baseCount, Mathf.RoundToInt(baseCount * (1f + bonus)));
        }

        /// <summary>
        /// Prefabs are expected from weakest to strongest. Close to the start only the first ones appear,
        /// far away the whole list. One rng call, like the old Pick.
        /// </summary>
        public static GameObject PickEnemyPrefab(SeededRandom rng, GameObject[] prefabs, Vector3 position)
        {
            if (prefabs == null || prefabs.Length == 0) return null;
            int unlocked = Mathf.Clamp(1 + Mathf.FloorToInt(T(position) * prefabs.Length), 1, prefabs.Length);
            GameObject pick = prefabs[rng.Range(0, unlocked)];
            return pick != null ? pick : prefabs[0];
        }

        /// <summary>Stronger enemies further away (health and damage).</summary>
        public static void ApplyToEnemy(EndlessCombat.AI.EnemyController enemy, Vector3 position)
        {
            if (enemy == null) return;
            float t = T(position);
            if (t <= 0f) return;
            var s = SurvivalSettings.Instance;
            enemy.maxHealth *= 1f + s.enemyHealthBonusAtMax * t;
            enemy.damage *= 1f + s.enemyDamageBonusAtMax * t;
        }

        public static int ExtraLootRolls(Vector3 position) => Mathf.FloorToInt(SurvivalSettings.Instance.lootExtraRollsAtMax * T(position) + 0.001f);

        /// <summary>Multiplier for fuel found (wrecks, gas stations): goes down with distance.</summary>
        public static float FuelMultiplier(Vector3 position) => 1f - SurvivalSettings.Instance.fuelScarcityAtMax * T(position);
    }
}
