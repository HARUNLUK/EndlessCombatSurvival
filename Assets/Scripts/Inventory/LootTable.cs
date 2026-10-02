using System;
using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.World;

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
    /// All roll methods have a SeededRandom overload for deterministic generation.
    /// The parameterless versions fall back to UnityEngine.Random for runtime drops
    /// that don't need to be reproducible (e.g. enemy death loot).
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

        // ---------------------------------------------------------------
        // Deterministic overloads (used by chunk-spawned loot)
        // ---------------------------------------------------------------

        public List<ItemStack> Roll(SeededRandom rng)
        {
            var result = new List<ItemStack>();
            if (entries == null || entries.Count == 0) return result;

            int rolls = rng.Range(minRolls, Mathf.Max(minRolls, maxRolls) + 1);
            for (int i = 0; i < rolls; i++)
            {
                ItemStack stack = RollSingle(rng);
                if (stack.item != null) Merge(result, stack.item, stack.amount);
            }
            return result;
        }

        public ItemStack RollSingle(SeededRandom rng)
        {
            LootEntry entry = PickEntry(rng);
            if (entry == null || entry.item == null) return default;

            int amount = rng.Range(entry.minAmount, Mathf.Max(entry.minAmount, entry.maxAmount) + 1);
            return new ItemStack(entry.item, amount);
        }

        private LootEntry PickEntry(SeededRandom rng)
        {
            if (entries == null || entries.Count == 0) return null;

            float total = 0f;
            foreach (var e in entries) total += Mathf.Max(0f, e.weight);
            if (total <= 0f) return null;

            float roll = rng.Value * total;
            foreach (var e in entries)
            {
                roll -= Mathf.Max(0f, e.weight);
                if (roll <= 0f) return e;
            }
            return entries[entries.Count - 1];
        }

        // ---------------------------------------------------------------
        // Non-deterministic fallbacks (runtime drops not tied to seed)
        // ---------------------------------------------------------------

        public List<ItemStack> Roll()
        {
            var result = new List<ItemStack>();
            if (entries == null || entries.Count == 0) return result;

            int rolls = UnityEngine.Random.Range(minRolls, Mathf.Max(minRolls, maxRolls) + 1);
            for (int i = 0; i < rolls; i++)
            {
                ItemStack stack = RollSingle();
                if (stack.item != null) Merge(result, stack.item, stack.amount);
            }
            return result;
        }

        public ItemStack RollSingle()
        {
            LootEntry entry = PickEntryNonDeterministic();
            if (entry == null || entry.item == null) return default;

            int amount = UnityEngine.Random.Range(entry.minAmount, Mathf.Max(entry.minAmount, entry.maxAmount) + 1);
            return new ItemStack(entry.item, amount);
        }

        private LootEntry PickEntryNonDeterministic()
        {
            if (entries == null || entries.Count == 0) return null;

            float total = 0f;
            foreach (var e in entries) total += Mathf.Max(0f, e.weight);
            if (total <= 0f) return null;

            float roll = UnityEngine.Random.value * total;
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
