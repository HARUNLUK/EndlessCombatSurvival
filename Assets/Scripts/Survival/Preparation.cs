using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.Save;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// R5: preparation before a journey. The character earns preparation points on the road (every zone, every
    /// Safe Zone; kept through death) and spends them on starting gear on the character screen. Points only buy
    /// items and fuel for the start of one journey, never stats or lasting bonuses.
    /// The chosen set is remembered: "Yeni Yolculuk" on the death screen buys it again as far as the points go.
    /// </summary>
    public static class Preparation
    {
        public class Option
        {
            public string id, label;
            public int cost;
            public (string item, int count)[] items = new (string, int)[0];
            public bool fullTank;
        }

        public static readonly Option[] Options =
        {
            new Option { id = "tank", label = "Dolu depo", cost = 5, fullTank = true },
            new Option { id = "can", label = "Yedek yakıt bidonu", cost = 4, items = new[] { ("Item_FuelCan", 1) } },
            new Option { id = "siphon", label = "Yakıt çekme takımı (hortum + boş bidon)", cost = 3, items = new[] { ("Item_RubberHose", 1), ("Item_EmptyFuelCan", 1) } },
            new Option { id = "tools", label = "Alet çantası", cost = 4, items = new[] { ("Item_ToolKit", 1) } },
            new Option { id = "pistol", label = "Tabanca + 2 kutu mermi", cost = 6, items = new[] { ("Item_Pistol", 1), ("Item_PistolAmmo", 2) } },
            new Option { id = "medic", label = "İlk yardım (3 sargı + ilk yardım çantası)", cost = 4, items = new[] { ("Item_Bandage", 3), ("Item_Medkit", 1) } },
            new Option { id = "food", label = "Erzak (3 konserve + 2 su)", cost = 3, items = new[] { ("Item_CannedBeans", 3), ("Item_WaterBottle", 2) } },
            new Option { id = "binoculars", label = "Dürbün", cost = 2, items = new[] { ("Item_Binoculars", 1) } },
        };

        /// <summary>Options ticked for the next new journey.</summary>
        public static readonly HashSet<string> Chosen = new HashSet<string>();

        private static readonly List<Option> _bought = new List<Option>();

        public static int ChosenCost
        {
            get
            {
                int sum = 0;
                foreach (var o in Options) if (Chosen.Contains(o.id)) sum += o.cost;
                return sum;
            }
        }

        public static void Earn(int points)
        {
            var p = CharacterProfiles.Current;
            if (p == null || points <= 0) return;
            p.prepPoints += points; // written to the profile with the next save (and when the journey ends)
        }

        /// <summary>
        /// A new journey starts: pays for the chosen options in list order as far as the points go. What was paid
        /// for is handed out by <see cref="Apply"/> once the player exists in the new scene.
        /// </summary>
        public static void Commit()
        {
            _bought.Clear();
            var p = CharacterProfiles.Current;
            if (p == null) return;
            foreach (var o in Options)
            {
                if (!Chosen.Contains(o.id) || p.prepPoints < o.cost) continue;
                p.prepPoints -= o.cost;
                _bought.Add(o);
            }
            CharacterProfiles.SaveCurrent();
        }

        /// <summary>Gives the bought starting gear (once). Called when the survival systems set the player up.</summary>
        public static void Apply(PlayerInventory inv)
        {
            if (_bought.Count == 0 || inv == null) return;
            var s = SurvivalSettings.Instance;
            var names = new List<string>();
            foreach (var o in _bought)
            {
                foreach (var (itemName, count) in o.items)
                {
                    var item = s.Item(itemName);
                    if (item != null) inv.AddItem(item, count);
                    else Debug.LogWarning($"[Preparation] Item not found: {itemName}");
                }
                if (o.fullTank)
                {
                    var car = SurvivalDirector.Vehicle;
                    if (car != null) car.currentFuel = car.maxFuel;
                }
                names.Add(o.label);
            }
            _bought.Clear();
            SurvivalNotify.Show("Hazırlık: " + string.Join(", ", names), new Color(0.55f, 0.85f, 1f), 9f);
        }
    }
}
