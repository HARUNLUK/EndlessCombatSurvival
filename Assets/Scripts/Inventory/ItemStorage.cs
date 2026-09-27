using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessSurvival.Inventory
{
    [Serializable]
    public struct ItemStack
    {
        public ItemDefinition item;
        public int amount;

        public ItemStack(ItemDefinition item, int amount)
        {
            this.item = item;
            this.amount = amount;
        }
    }

    /// <summary>
    /// Weight limited (or unlimited) container of item counts.
    /// </summary>
    public class ItemStorage
    {
        private readonly Dictionary<ItemDefinition, int> _items = new Dictionary<ItemDefinition, int>();

        public event Action Changed;

        /// <summary>Maximum carry weight in kg. A value of zero or less means unlimited.</summary>
        public float MaxWeight { get; set; }

        public bool IsUnlimited => MaxWeight <= 0f;

        public bool IsEmpty => _items.Count == 0;

        public ItemStorage(float maxWeight)
        {
            MaxWeight = maxWeight;
        }

        public float TotalWeight
        {
            get
            {
                float total = 0f;
                foreach (var pair in _items)
                    total += pair.Key.weight * pair.Value;
                return total;
            }
        }

        public int GetCount(ItemDefinition item)
        {
            if (item == null) return 0;
            return _items.TryGetValue(item, out int count) ? count : 0;
        }

        public int MaxAddable(ItemDefinition item)
        {
            if (item == null) return 0;
            if (IsUnlimited || item.weight <= 0f) return int.MaxValue;
            float free = MaxWeight - TotalWeight;
            return Mathf.Max(0, Mathf.FloorToInt(free / item.weight + 0.0001f));
        }

        /// <summary>Adds as many units as fit and returns how many were accepted.</summary>
        public int Add(ItemDefinition item, int amount)
        {
            if (item == null || amount <= 0) return 0;

            int accepted = Mathf.Min(amount, MaxAddable(item));
            if (accepted <= 0) return 0;

            _items[item] = GetCount(item) + accepted;
            Changed?.Invoke();
            return accepted;
        }

        /// <summary>Removes up to the requested amount and returns how many were removed.</summary>
        public int Remove(ItemDefinition item, int amount)
        {
            if (item == null || amount <= 0) return 0;

            int have = GetCount(item);
            int removed = Mathf.Min(have, amount);
            if (removed <= 0) return 0;

            if (have - removed <= 0) _items.Remove(item);
            else _items[item] = have - removed;

            Changed?.Invoke();
            return removed;
        }
    }
}
