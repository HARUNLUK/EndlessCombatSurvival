using UnityEngine;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// 4.3: character based difficulty (2026-11-14: the road distance no longer makes enemies stronger; only a more
    /// trained character does). The level is read from the character's stats when the journey starts, so chunk
    /// generation stays deterministic for a journey (works in edit mode too). Tuning lives in SurvivalSettings.
    /// <see cref="Raw"/> and <see cref="T"/> are both the 0..1 character level; the position argument is kept so
    /// callers do not change. <see cref="Km"/> stays for Safe Zones and the journey counter.
    /// </summary>
    public static class DifficultyCurve
    {
        /// <summary>Character level 0..1 (mean of the stats above the start value), fixed when the journey starts.</summary>
        public static float CharacterLevel { get; private set; }

        /// <summary>Same value as <see cref="CharacterLevel"/> (kept for old callers and the test log).</summary>
        public static float CharacterBonus => CharacterLevel;

        /// <summary>A new, untrained character: few humans, lone campers, safe start.</summary>
        public static bool EarlyGame => CharacterLevel < SurvivalSettings.Instance.earlyGameLevel;

        /// <summary>
        /// Reads the active character's stats: called by the menus right before a journey starts or continues
        /// (before the scene reloads and the world is built).
        /// </summary>
        public static void SnapshotCharacter()
        {
            CharacterLevel = 0f;
            var stats = EndlessSurvival.Save.CharacterProfiles.Current?.stats;
            if (stats == null || stats.Length == 0) return;
            var set = StatSettings.Instance;
            float sum = 0f;
            foreach (float v in stats) sum += v;
            float mean = sum / stats.Length;
            CharacterLevel = Mathf.Clamp01((mean - set.startValue) / Mathf.Max(1f, set.maxValue - set.startValue));
        }

        /// <summary>Kilometres from the world origin (the road starts there).</summary>
        public static float Km(Vector3 position) => new Vector2(position.x, position.z).magnitude / 1000f;

        /// <summary>Difficulty 0..1: the character level (the position no longer matters).</summary>
        public static float Raw(Vector3 position) => CharacterLevel;

        /// <summary>Same as <see cref="Raw"/>.</summary>
        public static float T(Vector3 position) => Mathf.Clamp01(Raw(position));

        public static int ScaleEnemyCount(int baseCount, Vector3 position)
        {
            float bonus = SurvivalSettings.Instance.enemyCountBonusAtMax * CharacterLevel;
            return Mathf.Max(baseCount, Mathf.RoundToInt(baseCount * (1f + bonus)));
        }

        /// <summary>Early game: a group of humans never goes past earlyGameMaxHumans (patrols, camps, roadblocks, ambushes).</summary>
        public static int CapHumans(int count)
        {
            return EarlyGame ? Mathf.Min(count, SurvivalSettings.Instance.earlyGameMaxHumans) : count;
        }

        /// <summary>
        /// Prefabs are expected from weakest to strongest. A new character only meets the first ones,
        /// a trained one the whole list. One rng call, like the old Pick.
        /// </summary>
        public static GameObject PickEnemyPrefab(SeededRandom rng, GameObject[] prefabs, Vector3 position)
        {
            if (prefabs == null || prefabs.Length == 0) return null;
            int unlocked = Mathf.Clamp(1 + Mathf.FloorToInt(CharacterLevel * prefabs.Length), 1, prefabs.Length);
            GameObject pick = prefabs[rng.Range(0, unlocked)];
            return pick != null ? pick : prefabs[0];
        }

        /// <summary>Stronger enemies for a more trained character (health, damage, rounds).</summary>
        public static void ApplyToEnemy(EndlessCombat.AI.EnemyController enemy, Vector3 position)
        {
            if (enemy == null) return;
            float t = CharacterLevel;
            if (t <= 0f) return;
            var s = SurvivalSettings.Instance;
            enemy.maxHealth *= 1f + s.enemyHealthBonusAtMax * t;
            float dmg = 1f + s.enemyDamageBonusAtMax * t;
            enemy.damage *= dmg;
            // Called right after the spawn (before the enemy's Start): its bullets and blows get stronger too,
            // and it carries more rounds before it has to fight hand to hand
            enemy.firearmDamageMultiplier *= dmg;
            enemy.meleeDamageMultiplier *= dmg;
            float ammo = 1f + 0.6f * t;
            enemy.rifleRounds = new Vector2Int(Mathf.RoundToInt(enemy.rifleRounds.x * ammo), Mathf.RoundToInt(enemy.rifleRounds.y * ammo));
            enemy.pistolRounds = new Vector2Int(Mathf.RoundToInt(enemy.pistolRounds.x * ammo), Mathf.RoundToInt(enemy.pistolRounds.y * ammo));
        }

        /// <summary>More loot rolls for a trained character (the reward grows with the danger).</summary>
        public static int ExtraLootRolls(Vector3 position) => Mathf.FloorToInt(SurvivalSettings.Instance.lootExtraRollsAtMax * CharacterLevel + 0.001f);

        /// <summary>Multiplier for fuel found (wrecks, gas stations): goes down with the character level, never below 1 - fuelScarcityAtMax.</summary>
        public static float FuelMultiplier(Vector3 position) => 1f - SurvivalSettings.Instance.fuelScarcityAtMax * CharacterLevel;
    }
}
