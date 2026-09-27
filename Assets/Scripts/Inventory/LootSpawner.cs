using UnityEngine;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Helper that drops the result of a loot table into the world as pickups.
    /// </summary>
    public static class LootSpawner
    {
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
