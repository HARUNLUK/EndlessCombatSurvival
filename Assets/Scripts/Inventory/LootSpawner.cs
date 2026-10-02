using UnityEngine;
using EndlessSurvival.World;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Helper that drops the result of a loot table into the world as pickups.
    /// </summary>
    public static class LootSpawner
    {
        /// <summary>
        /// Deterministic drop using a SeededRandom (for chunk-spawned loot).
        /// </summary>
        public static void DropLoot(LootTable table, Vector3 position, SeededRandom rng)
        {
            if (table == null || table.pickupPrefab == null) return;

            foreach (ItemStack stack in table.Roll(rng))
            {
                Vector2 offset = rng.InsideUnitCircle * 0.9f;
                Vector3 spawn = position + new Vector3(offset.x, 0.6f, offset.y);
                WorldPickup.Spawn(table.pickupPrefab, stack.item, stack.amount, spawn, null);
            }
        }

        /// <summary>
        /// Non-deterministic drop (for runtime events like enemy death).
        /// </summary>
        public static void DropLoot(LootTable table, Vector3 position)
        {
            if (table == null || table.pickupPrefab == null) return;

            foreach (ItemStack stack in table.Roll())
            {
                Vector2 offset = Random.insideUnitCircle * 0.9f;
                Vector3 spawn = position + new Vector3(offset.x, 0.6f, offset.y);
                WorldPickup.Spawn(table.pickupPrefab, stack.item, stack.amount, spawn, null);
            }
        }
    }
}
