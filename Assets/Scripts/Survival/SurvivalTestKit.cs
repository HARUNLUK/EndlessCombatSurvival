using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessCombat.Combat;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Test mode kit (SurvivalTest.FreeResources): every item of the game sits in the backpack and is topped up again
    /// once a second, so nothing runs out (eating, throwing, shooting, crafting). Equippable items (weapons, clothes,
    /// shields) are given once each and not again while worn. Each ammo kind is kept at AmmoFloor rounds and money at
    /// MoneyFloor. The carry limit is lifted in PlayerVitals.UpdateCarry. Added by SurvivalDirector.
    /// </summary>
    public class SurvivalTestKit : MonoBehaviour
    {
        public const int StackAmount = 20;   // stackable items (food, materials, throwables, junk...)
        public const int AmmoFloor = 999;    // rounds per ammo kind
        public const int MoneyFloor = 99999;

        private readonly List<ItemDefinition> _all = new List<ItemDefinition>();
        private readonly HashSet<ItemDefinition> _givenOnce = new HashSet<ItemDefinition>();
        private float _next;
        private bool _wasOn;

        private void Update()
        {
            bool on = SurvivalTest.FreeResources;
            if (!on) { _wasOn = false; return; }
            if (Time.timeSinceLevelLoad < 2f || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;

            var player = SurvivalDirector.Player;
            var inv = SurvivalDirector.Inventory;
            if (player == null || inv == null) return;

            if (!_wasOn)
            {
                _wasOn = true;
                _givenOnce.Clear(); // switched on (or a new scene): equippables are handed out again
                Collect();
            }

            var equipment = player.GetComponent<PlayerEquipment>();
            foreach (var item in _all)
            {
                if (item == null) continue;
                if (item.IsEquippable)
                {
                    if (_givenOnce.Contains(item)) continue;
                    _givenOnce.Add(item);
                    if (inv.Storage.GetCount(item) > 0) continue;
                    if (equipment != null && equipment.Get(item.CarrySlot) == item) continue; // already worn
                    inv.Storage.Add(item, 1);
                    continue;
                }
                int have = inv.Storage.GetCount(item);
                if (have < StackAmount) inv.Storage.Add(item, StackAmount - have);
            }

            for (int k = (int)AmmoKind.Pistol; k <= (int)AmmoKind.Shotgun; k++)
            {
                int have = inv.Count((AmmoKind)k);
                if (have < AmmoFloor) inv.Add((AmmoKind)k, AmmoFloor - have);
            }
            if (inv.Money < MoneyFloor) inv.AddMoney(MoneyFloor - inv.Money);

            if (!_logged)
            {
                _logged = true;
                Debug.Log($"[SurvivalTestKit] {_all.Count} item kinds, backpack now {inv.Storage.Items.Count} kinds, {inv.Storage.TotalWeight:0.0} kg, unlimited={inv.Storage.IsUnlimited}");
            }
        }
        private bool _logged;

        /// <summary>Every item asset: the item database, the survival content list, the combat test items and any loaded item.</summary>
        private void Collect()
        {
            var set = new HashSet<ItemDefinition>();
            foreach (var db in Resources.FindObjectsOfTypeAll<ItemDatabase>())
                if (db != null && db.items != null) foreach (var it in db.items) set.Add(it);
            var s = SurvivalSettings.Instance;
            if (s != null && s.items != null) foreach (var it in s.items) set.Add(it);
            var content = CombatContent.Instance;
            if (content != null && content.testItems != null) foreach (var it in content.testItems) set.Add(it);
            foreach (var it in Resources.FindObjectsOfTypeAll<ItemDefinition>()) set.Add(it);

            _all.Clear();
            foreach (var it in set)
            {
                if (it == null) continue;
                if (it.category == ItemCategory.Ammo || it.category == ItemCategory.Currency) continue; // pools / wallet instead
                _all.Add(it);
            }
            _all.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }
    }
}
