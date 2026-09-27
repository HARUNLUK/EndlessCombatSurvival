using System;
using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Vehicle;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Refuel, repair and upgrade logic for the vehicle. Costs are paid from the vehicle stash first,
    /// then from the player backpack.
    /// </summary>
    public class VehicleMaintenance : MonoBehaviour
    {
        public enum UpgradeType
        {
            FuelTank,
            Engine,
            Armor
        }

        [Serializable]
        public class UpgradeRule
        {
            public string label = "Upgrade";
            public UpgradeType type = UpgradeType.FuelTank;
            public ItemDefinition costItem;
            [Min(1)] public int baseCost = 4;
            [Min(0)] public int costPerLevel = 3;
            [Min(1)] public int maxLevel = 3;
            [Tooltip("FuelTank: extra max fuel. Engine: extra motor torque. Armor: extra max health.")]
            public float amountPerLevel = 25f;
            public int level;

            public int NextCost => baseCost + costPerLevel * level;
        }

        [Header("References")]
        public VehicleController vehicle;
        public VehicleStorage storage;
        public PlayerInventory playerInventory;

        [Header("Refuel & Repair")]
        [Tooltip("Item consumed to refuel (its Use Amount is the fuel added per unit)")]
        public ItemDefinition fuelItem;

        [Tooltip("Item consumed to repair the vehicle")]
        public ItemDefinition repairItem;

        [Tooltip("Health restored per repair item")]
        public float healthPerRepairItem = 10f;

        [Header("Upgrades")]
        public List<UpgradeRule> upgrades = new List<UpgradeRule>
        {
            new UpgradeRule { label = "Fuel Tank", type = UpgradeType.FuelTank, baseCost = 4, costPerLevel = 3, maxLevel = 3, amountPerLevel = 25f },
            new UpgradeRule { label = "Engine", type = UpgradeType.Engine, baseCost = 2, costPerLevel = 2, maxLevel = 3, amountPerLevel = 200f },
            new UpgradeRule { label = "Armor", type = UpgradeType.Armor, baseCost = 4, costPerLevel = 3, maxLevel = 3, amountPerLevel = 25f }
        };

        public event Action Changed;

        private void Awake()
        {
            if (vehicle == null) vehicle = GetComponent<VehicleController>();
            if (storage == null) storage = GetComponent<VehicleStorage>();
        }

        private int Available(ItemDefinition item)
        {
            if (item == null) return 0;
            int total = storage != null ? storage.Storage.GetCount(item) : 0;
            if (playerInventory != null) total += playerInventory.Storage.GetCount(item);
            return total;
        }

        private int Take(ItemDefinition item, int wanted)
        {
            if (item == null || wanted <= 0) return 0;

            int taken = storage != null ? storage.Storage.Remove(item, wanted) : 0;
            if (taken < wanted && playerInventory != null)
                taken += playerInventory.Storage.Remove(item, wanted - taken);
            return taken;
        }

        public string Refuel()
        {
            if (vehicle == null || fuelItem == null) return "Refueling is not set up";

            float missing = vehicle.maxFuel - vehicle.currentFuel;
            if (missing < 0.5f) return "Fuel tank is already full";

            int perUnit = Mathf.Max(1, fuelItem.useAmount);
            int needed = Mathf.CeilToInt(missing / perUnit);
            int taken = Take(fuelItem, needed);
            if (taken <= 0) return $"No {fuelItem.DisplayName} available";

            float added = vehicle.Refuel(taken * perUnit);
            Changed?.Invoke();
            return $"Refueled +{Mathf.RoundToInt(added)} using {taken}x {fuelItem.DisplayName}";
        }

        public string Repair()
        {
            if (vehicle == null || repairItem == null) return "Repairing is not set up";

            float missing = vehicle.maxHealth - vehicle.currentHealth;
            if (missing < 0.5f) return "Vehicle is undamaged";

            int needed = Mathf.CeilToInt(missing / Mathf.Max(1f, healthPerRepairItem));
            int taken = Take(repairItem, needed);
            if (taken <= 0) return $"No {repairItem.DisplayName} available";

            float restored = vehicle.Repair(taken * healthPerRepairItem);
            Changed?.Invoke();
            return $"Repaired +{Mathf.RoundToInt(restored)} using {taken}x {repairItem.DisplayName}";
        }

        public string Upgrade(int index)
        {
            if (vehicle == null || index < 0 || index >= upgrades.Count) return null;

            UpgradeRule rule = upgrades[index];
            if (rule.costItem == null) return "Upgrade is not set up";
            if (rule.level >= rule.maxLevel) return $"{rule.label} is at max level";

            int cost = rule.NextCost;
            if (Available(rule.costItem) < cost) return $"Need {cost}x {rule.costItem.DisplayName}";

            Take(rule.costItem, cost);
            rule.level++;

            switch (rule.type)
            {
                case UpgradeType.FuelTank:
                    vehicle.maxFuel += rule.amountPerLevel;
                    vehicle.currentFuel += rule.amountPerLevel;
                    break;
                case UpgradeType.Engine:
                    vehicle.motorForce += rule.amountPerLevel;
                    break;
                case UpgradeType.Armor:
                    vehicle.maxHealth += rule.amountPerLevel;
                    vehicle.currentHealth += rule.amountPerLevel;
                    break;
            }

            Changed?.Invoke();
            return $"{rule.label} upgraded to level {rule.level}";
        }

        public string GetUpgradeLabel(int index)
        {
            if (index < 0 || index >= upgrades.Count) return string.Empty;

            UpgradeRule rule = upgrades[index];
            if (rule.level >= rule.maxLevel) return $"{rule.label} Lv{rule.level}/{rule.maxLevel} (MAX)";

            string costName = rule.costItem != null ? rule.costItem.DisplayName : "?";
            return $"{rule.label} Lv{rule.level}/{rule.maxLevel}: {rule.NextCost}x {costName}";
        }

        public string GetRefuelLabel()
        {
            return fuelItem != null ? $"Refuel ({fuelItem.DisplayName}: {Available(fuelItem)})" : "Refuel";
        }

        public string GetRepairLabel()
        {
            return repairItem != null ? $"Repair ({repairItem.DisplayName}: {Available(repairItem)})" : "Repair";
        }
    }
}
