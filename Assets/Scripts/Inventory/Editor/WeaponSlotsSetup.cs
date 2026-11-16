using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EndlessSurvival.Inventory.Editor
{
    /// <summary>
    /// Weapon slots + ammo kinds data: creates Item_PistolAmmo and Item_ShotgunShells, marks Item_RifleAmmo as rifle
    /// rounds, adds the new boxes to the item database, survival settings (items + trader) and every loot table that
    /// already drops rifle rounds. Runs once by itself after compiling (when the items are missing); also in the menu.
    /// </summary>
    [InitializeOnLoad]
    public static class WeaponSlotsSetup
    {
        private const string ItemsFolder = "Assets/Data/Items";
        private const string TriedKey = "EndlessSurvival.WeaponSlotsSetupTried";

        static WeaponSlotsSetup()
        {
            EditorApplication.delayCall += AutoRun;
        }

        private static void AutoRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (SessionState.GetBool(TriedKey, false)) return;
            SessionState.SetBool(TriedKey, true);
            if (AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsFolder}/Item_PistolAmmo.asset") != null &&
                AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsFolder}/Item_ShotgunShells.asset") != null) return;
            Run();
        }

        [MenuItem("Endless Survival/Setup Weapon Slots + Ammo Kinds")]
        public static void Run()
        {
            var rifle = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsFolder}/Item_RifleAmmo.asset");
            if (rifle == null)
            {
                Debug.LogWarning("[WeaponSlotsSetup] Item_RifleAmmo not found; nothing done.");
                return;
            }
            rifle.ammoType = AmmoType.Rifle;
            EditorUtility.SetDirty(rifle);

            var pistol = MakeAmmo("Item_PistolAmmo", "Pistol Ammo Box", AmmoType.Pistol, 15, 4,
                new Color(0.45f, 0.75f, 1f), "Rounds for pistols and the smg.");
            var shells = MakeAmmo("Item_ShotgunShells", "Shotgun Shells", AmmoType.Shotgun, 8, 6,
                new Color(0.95f, 0.45f, 0.35f), "Shells for the hunting shotgun.");

            AddToDatabase(pistol, shells);
            AddToSettings(rifle, pistol, shells);
            AddToLoot(rifle, pistol, shells);
            AssetDatabase.SaveAssets();
            Debug.Log("[WeaponSlotsSetup] Ammo kinds ready: Item_PistolAmmo, Item_ShotgunShells (database, loot, trader).");
        }

        private static ItemDefinition MakeAmmo(string file, string displayName, AmmoType type, int rounds, int value, Color color, string description)
        {
            string path = $"{ItemsFolder}/{file}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(item, path);
            }
            item.displayName = displayName;
            item.category = ItemCategory.Ammo;
            item.ammoType = type;
            item.description = description;
            item.weight = 0f; // rounds are weighed per round by PlayerInventory
            item.value = value;
            item.useAmount = rounds;
            item.worldColor = color;
            item.worldScale = new Vector3(0.3f, 0.2f, 0.22f);
            EditorUtility.SetDirty(item);
            return item;
        }

        private static void AddToDatabase(params ItemDefinition[] add)
        {
            var db = AssetDatabase.FindAssets("t:ItemDatabase").Select(g => AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(d => d != null);
            if (db == null) return;
            var list = (db.items ?? new ItemDefinition[0]).ToList();
            foreach (var a in add) if (!list.Contains(a)) list.Add(a);
            db.items = list.ToArray();
            EditorUtility.SetDirty(db);
        }

        private static void AddToSettings(ItemDefinition rifle, params ItemDefinition[] add)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:SurvivalSettings"))
            {
                var s = AssetDatabase.LoadAssetAtPath<EndlessSurvival.Survival.SurvivalSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (s == null) continue;
                var items = (s.items ?? new ItemDefinition[0]).ToList();
                var trader = (s.traderStock ?? new ItemDefinition[0]).ToList();
                foreach (var a in add)
                {
                    if (items.Contains(rifle) && !items.Contains(a)) items.Add(a);
                    if (trader.Contains(rifle) && !trader.Contains(a)) trader.Add(a);
                }
                s.items = items.ToArray();
                s.traderStock = trader.ToArray();
                EditorUtility.SetDirty(s);
            }
        }

        /// <summary>Every table that drops rifle rounds also drops pistol rounds (a bit more often) and shells (less).</summary>
        private static void AddToLoot(ItemDefinition rifle, ItemDefinition pistol, ItemDefinition shells)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:LootTable"))
            {
                var table = AssetDatabase.LoadAssetAtPath<LootTable>(AssetDatabase.GUIDToAssetPath(guid));
                if (table == null || table.entries == null) continue;
                LootEntry rifleEntry = table.entries.FirstOrDefault(e => e != null && e.item == rifle);
                if (rifleEntry == null) continue;
                AddEntry(table.entries, pistol, rifleEntry.weight * 1.1f, rifleEntry.minAmount, rifleEntry.maxAmount + 1);
                AddEntry(table.entries, shells, rifleEntry.weight * 0.6f, rifleEntry.minAmount, rifleEntry.maxAmount);
                EditorUtility.SetDirty(table);
            }
        }

        private static void AddEntry(List<LootEntry> entries, ItemDefinition item, float weight, int min, int max)
        {
            if (entries.Any(e => e != null && e.item == item)) return;
            entries.Add(new LootEntry { item = item, weight = weight, minAmount = Mathf.Max(1, min), maxAmount = Mathf.Max(1, max) });
        }
    }
}
