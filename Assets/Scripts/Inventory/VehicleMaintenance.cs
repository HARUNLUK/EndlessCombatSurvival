using System;
using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Vehicle;
using EndlessSurvival.Survival;

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
            [Tooltip("FuelTank: extra max fuel. Engine: extra engine torque (Nm). Armor: extra max health.")]
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
            new UpgradeRule { label = "Yakıt Deposu", type = UpgradeType.FuelTank, baseCost = 4, costPerLevel = 3, maxLevel = 3, amountPerLevel = 25f },
            new UpgradeRule { label = "Motor", type = UpgradeType.Engine, baseCost = 2, costPerLevel = 2, maxLevel = 3, amountPerLevel = 35f },
            new UpgradeRule { label = "Zırh", type = UpgradeType.Armor, baseCost = 4, costPerLevel = 3, maxLevel = 3, amountPerLevel = 25f }
        };

        public event Action Changed;

        private void Awake()
        {
            if (vehicle == null) vehicle = GetComponent<VehicleController>();
            if (storage == null) storage = GetComponent<VehicleStorage>();
            // Older scenes serialized English labels
            foreach (var rule in upgrades)
            {
                if (rule == null) continue;
                if (rule.label == "Fuel Tank") rule.label = "Yakıt Deposu";
                else if (rule.label == "Engine") rule.label = "Motor";
                else if (rule.label == "Armor") rule.label = "Zırh";
                else if (rule.label == "Upgrade") rule.label = "Yükseltme";
            }
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
            if (vehicle == null || fuelItem == null) return "Yakıt doldurma kurulmamış";

            float missing = vehicle.maxFuel - vehicle.currentFuel;
            if (missing < 0.5f) return "Depo zaten dolu";

            int perUnit = Mathf.Max(1, fuelItem.useAmount);
            int needed = Mathf.CeilToInt(missing / perUnit);
            if (SurvivalTest.FreeResources)
            {
                float free = vehicle.Refuel(missing);
                Changed?.Invoke();
                return $"Depo dolduruldu +{Mathf.RoundToInt(free)} (TEST: normalde {needed}x {fuelItem.DisplayName})";
            }
            int taken = Take(fuelItem, needed);
            if (taken <= 0) return $"{fuelItem.DisplayName} yok";

            float added = vehicle.Refuel(taken * perUnit);
            Changed?.Invoke();
            return $"Depo dolduruldu +{Mathf.RoundToInt(added)} ({taken}x {fuelItem.DisplayName})";
        }

        public string Repair()
        {
            if (vehicle == null || repairItem == null) return "Tamir kurulmamış";

            float missing = vehicle.maxHealth - vehicle.currentHealth;
            if (missing < 0.5f) return "Araç hasarsız";

            int needed = Mathf.CeilToInt(missing / Mathf.Max(1f, healthPerRepairItem));
            if (SurvivalTest.FreeResources)
            {
                float free = vehicle.Repair(missing);
                Changed?.Invoke();
                return $"Araç tamir edildi +{Mathf.RoundToInt(free)} (TEST: normalde {needed}x {repairItem.DisplayName})";
            }
            int taken = Take(repairItem, needed);
            if (taken <= 0) return $"{repairItem.DisplayName} yok";

            float restored = vehicle.Repair(taken * healthPerRepairItem);
            Changed?.Invoke();
            return $"Araç tamir edildi +{Mathf.RoundToInt(restored)} ({taken}x {repairItem.DisplayName})";
        }

        public string Upgrade(int index)
        {
            if (vehicle == null || index < 0 || index >= upgrades.Count) return null;

            UpgradeRule rule = upgrades[index];
            if (rule.costItem == null) return "Yükseltme kurulmamış";
            if (rule.level >= rule.maxLevel) return $"{rule.label} en üst seviyede";

            int cost = rule.NextCost;
            if (!SurvivalTest.FreeResources)
            {
                if (Available(rule.costItem) < cost) return $"Gerekli: {cost}x {rule.costItem.DisplayName}";
                Take(rule.costItem, cost);
            }
            rule.level++;

            switch (rule.type)
            {
                case UpgradeType.FuelTank:
                    vehicle.maxFuel += rule.amountPerLevel;
                    vehicle.currentFuel += rule.amountPerLevel;
                    break;
                case UpgradeType.Engine:
                    vehicle.maxEngineTorque += rule.amountPerLevel;
                    break;
                case UpgradeType.Armor:
                    vehicle.maxHealth += rule.amountPerLevel;
                    vehicle.currentHealth += rule.amountPerLevel;
                    break;
            }

            Changed?.Invoke();
            return $"{rule.label} seviye {rule.level} oldu";
        }

        public string GetUpgradeLabel(int index)
        {
            if (index < 0 || index >= upgrades.Count) return string.Empty;

            UpgradeRule rule = upgrades[index];
            if (rule.level >= rule.maxLevel) return $"{rule.label} {rule.level}/{rule.maxLevel}\n(en üst seviye)";

            string costName = rule.costItem != null ? rule.costItem.DisplayName : "?";
            return $"{rule.label} yükselt {rule.level}/{rule.maxLevel}\n" + (SurvivalTest.FreeResources ? "(test: bedava)" : $"{rule.NextCost}x {costName}");
        }

        public string GetRefuelLabel()
        {
            if (fuelItem == null) return "Yakıt doldur";
            int need = vehicle != null ? Mathf.CeilToInt(Mathf.Max(0f, vehicle.maxFuel - vehicle.currentFuel) / Mathf.Max(1, fuelItem.useAmount)) : 0;
            return "Yakıt doldur\n" + (SurvivalTest.FreeResources ? "(test: bedava)" : $"{fuelItem.DisplayName} {Available(fuelItem)} / {need}");
        }

        public string GetRepairLabel()
        {
            if (repairItem == null) return "Tamir et";
            int need = vehicle != null ? Mathf.CeilToInt(Mathf.Max(0f, vehicle.maxHealth - vehicle.currentHealth) / Mathf.Max(1f, healthPerRepairItem)) : 0;
            return "Tamir et\n" + (SurvivalTest.FreeResources ? "(test: bedava)" : $"{repairItem.DisplayName} {Available(repairItem)} / {need}");
        }
    }
}
