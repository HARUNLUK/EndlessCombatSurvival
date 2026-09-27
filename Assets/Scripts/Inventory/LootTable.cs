using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace EndlessSurvival.Inventory
{
    [Serializable]
    public class LootEntry
    {
        public ItemDefinition item;
        [Min(0f)] public float weight = 1f;
        [Min(1)] public int minAmount = 1;
        [Min(1)] public int maxAmount = 1;
    }

    /// <summary>
    /// Weighted random loot definition used by chests, ground pickups and enemy drops.
    /// </summary>
    [CreateAssetMenu(fileName = "LootTable_New", menuName = "Endless Survival/Loot Table")]
    public class LootTable : ScriptableObject
    {
        [Tooltip("Pickup prefab used when this table drops items into the world")]
        public WorldPickup pickupPrefab;

        [Tooltip("Number of weighted rolls performed each time the table is rolled")]
        public int minRolls = 1;
        public int maxRolls = 3;

        public List<LootEntry> entries = new List<LootEntry>();

        public List<ItemStack> Roll()
        {
            var result = new List<ItemStack>();
            if (entries == null || entries.Count == 0) return result;

            int rolls = Random.Range(minRolls, Mathf.Max(minRolls, maxRolls) + 1);
            for (int i = 0; i < rolls; i++)
            {
                ItemStack stack = RollSingle();
                if (stack.item != null) Merge(result, stack.item, stack.amount);
            }
            return result;
        }

        public ItemStack RollSingle()
        {
            LootEntry entry = PickEntry();
            if (entry == null || entry.item == null) return default;

            int amount = Random.Range(entry.minAmount, Mathf.Max(entry.minAmount, entry.maxAmount) + 1);
            return new ItemStack(entry.item, amount);
        }

        private LootEntry PickEntry()
        {
            if (entries == null || entries.Count == 0) return null;

            float total = 0f;
            foreach (var e in entries) total += Mathf.Max(0f, e.weight);
            if (total <= 0f) return null;

            float roll = Random.value * total;
            foreach (var e in entries)
            {
                roll -= Mathf.Max(0f, e.weight);
                if (roll <= 0f) return e;
            }
            return entries[entries.Count - 1];
        }

        private static void Merge(List<ItemStack> list, ItemDefinition item, int amount)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].item == item)
                {
                    list[i] = new ItemStack(item, list[i].amount + amount);
                    return;
                }
            }
            list.Add(new ItemStack(item, amount));
        }
    }
}
