using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StarterAssets;
using EndlessCombat.AI;
using EndlessCombat.Combat;
using EndlessSurvival.Inventory;
using EndlessSurvival.Vehicle;
using EndlessSurvival.World;

namespace EndlessSurvival.Inventory.Editor
{
    /// <summary>
    /// One-click setup for the inventory / loot / vehicle maintenance systems.
    /// Creates item and loot assets, placeholder cube prefabs, wires the prefabs and
    /// builds the hierarchy-defined UI in the open scene. Safe to run more than once.
    /// </summary>
    public static class InventorySetupTool
    {
        private const string ItemsDir = "Assets/Data/Items";
        private const string LootDir = "Assets/Data/Loot";
        private const string PrefabDir = "Assets/Prefabs/Loot";
        private const string DatabasePath = "Assets/Data/ItemDatabase.asset";

        private const string EnemyPrefabPath = "Assets/Prefabs/Enemies/Enemy.prefab";
        private const string ChunkPrefabPath = "Assets/Prefabs/Chunks/Chunk_Forest_Curve.prefab";
        private static readonly string[] VehiclePrefabPaths =
        {
            "Assets/Prefabs/Vehicle_Pickup.prefab"
        };

        private static readonly Dictionary<string, ItemDefinition> Items = new Dictionary<string, ItemDefinition>();
        private static readonly List<ItemDefinition> ItemOrder = new List<ItemDefinition>();

        private static Font _font;

        [MenuItem("Endless Survival/Setup Inventory System")]
        public static void Run()
        {
            EnsureFolder("Assets/Data");
            EnsureFolder(ItemsDir);
            EnsureFolder(LootDir);
            EnsureFolder(PrefabDir);

            CreateItems();
            EnsureWeaponPrefabs();
            AssignWeaponWorldModels();
            ItemDatabase database = CreateDatabase();

            WorldPickup pickupPrefab = CreatePickupPrefab();
            LootTable roadsideTable = CreateTables(pickupPrefab, out LootTable chestTable, out LootTable enemyTable);
            LootChest chestPrefab = CreateChestPrefab(chestTable);

            AssetDatabase.SaveAssets();

            SetupEnemyPrefab(enemyTable);
            SetupChunkPrefab(roadsideTable, chestPrefab);
            foreach (string path in VehiclePrefabPaths) SetupVehiclePrefab(path);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SetupScene(database, pickupPrefab);

            Debug.Log("[InventorySetupTool] Inventory system setup complete. Save the scene (Ctrl+S).");
        }

        // ------------------------------------------------------------------ Assets

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void CreateItems()
        {
            Items.Clear();
            ItemOrder.Clear();

            //        key            name                 category               kg    val use  color                                     scale
            MakeItem("RifleAmmo",    "Rifle Ammo Box",    ItemCategory.Ammo,     0f,   5,  30,  new Color(1.00f, 0.85f, 0.20f), new Vector3(0.35f, 0.25f, 0.25f), "Rounds for the rifle. Added straight to the weapon reserve.");
            MakeItem("Coins",        "Coins",             ItemCategory.Currency, 0f,   1,  1,   new Color(1.00f, 0.75f, 0.10f), new Vector3(0.25f, 0.10f, 0.25f), "Money. Added straight to your wallet.");
            MakeItem("FuelCan",      "Fuel Can",          ItemCategory.Fuel,     2.5f, 15, 25,  new Color(0.85f, 0.15f, 0.10f), new Vector3(0.35f, 0.50f, 0.25f), "Refuels the vehicle.");

            MakeItem("ScrapMetal",   "Scrap Metal",       ItemCategory.Material, 1.0f, 3,  1,   new Color(0.60f, 0.62f, 0.65f), new Vector3(0.40f, 0.20f, 0.30f), "Used to repair and upgrade the vehicle.");
            MakeItem("Electronics",  "Electronic Parts",  ItemCategory.Material, 0.3f, 6,  1,   new Color(0.20f, 0.70f, 0.30f), new Vector3(0.25f, 0.10f, 0.30f), "Salvaged circuit boards and wiring.");
            MakeItem("DuctTape",     "Duct Tape",         ItemCategory.Material, 0.2f, 4,  1,   new Color(0.75f, 0.75f, 0.30f), new Vector3(0.25f, 0.15f, 0.25f), "Fixes almost anything.");

            MakeItem("EnginePart",   "Engine Part",       ItemCategory.Gear,     2.0f, 25, 1,   new Color(0.20f, 0.40f, 0.90f), new Vector3(0.40f, 0.30f, 0.30f), "Used for engine upgrades.");
            MakeItem("ToolKit",      "Tool Kit",          ItemCategory.Gear,     2.5f, 30, 1,   new Color(1.00f, 0.50f, 0.10f), new Vector3(0.45f, 0.25f, 0.30f), "A complete set of mechanic tools.");
            MakeItem("Flashlight",   "Flashlight",        ItemCategory.Gear,     0.5f, 12, 1,   new Color(1.00f, 0.95f, 0.60f), new Vector3(0.30f, 0.12f, 0.12f), "Still has a working battery.");
            MakeItem("WaterFilter",  "Water Filter",      ItemCategory.Gear,     0.8f, 18, 1,   new Color(0.30f, 0.85f, 0.90f), new Vector3(0.20f, 0.35f, 0.20f), "Makes dirty water drinkable.");

            MakeItem("RustyCan",     "Rusty Can",         ItemCategory.Junk,     0.3f, 1,  1,   new Color(0.55f, 0.30f, 0.15f), new Vector3(0.20f, 0.25f, 0.20f), "Empty and corroded.");
            MakeItem("BrokenWatch",  "Broken Watch",      ItemCategory.Junk,     0.1f, 2,  1,   new Color(0.70f, 0.70f, 0.75f), new Vector3(0.15f, 0.05f, 0.15f), "Stopped at a quarter past three.");
            MakeItem("OldBoot",      "Old Boot",          ItemCategory.Junk,     0.8f, 1,  1,   new Color(0.35f, 0.25f, 0.15f), new Vector3(0.20f, 0.30f, 0.40f), "Only one of the pair.");
            MakeItem("TeddyBear",    "Teddy Bear",        ItemCategory.Junk,     0.4f, 3,  1,   new Color(0.80f, 0.55f, 0.35f), new Vector3(0.25f, 0.35f, 0.20f), "Someone left it behind.");
            MakeItem("CrackedPhone", "Cracked Phone",     ItemCategory.Junk,     0.2f, 4,  1,   new Color(0.15f, 0.15f, 0.20f), new Vector3(0.12f, 0.02f, 0.22f), "The screen is shattered.");
            MakeItem("EmptyBottle",  "Empty Bottle",      ItemCategory.Junk,     0.2f, 1,  1,   new Color(0.50f, 0.80f, 0.60f), new Vector3(0.12f, 0.30f, 0.12f), "Could hold water, if you find some.");

            CreateEquipmentItems();
            CreateSurvivalItems();
        }

        private static void MakeItem(string key, string displayName, ItemCategory category, float weight, int value, int useAmount,
            Color color, Vector3 scale, string description, System.Action<ItemDefinition> initializeNew = null)
        {
            string path = $"{ItemsDir}/Item_{key}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.displayName = displayName;
                item.category = category;
                item.weight = weight;
                item.value = value;
                item.useAmount = useAmount;
                item.worldColor = color;
                item.worldScale = scale;
                item.description = description;
                initializeNew?.Invoke(item);
                AssetDatabase.CreateAsset(item, path);
            }

            Items[key] = item;
            ItemOrder.Add(item);
        }

        private static void MakeApparel(string key, string displayName, EquipSlot slot, float armor, float weight, int value,
            Color color, Vector3 scale, string description)
        {
            MakeItem(key, displayName, ItemCategory.Apparel, weight, value, 1, color, scale, description, item =>
            {
                item.equipSlot = slot;
                item.armor = armor;
            });
        }

        private static void MakeWeapon(string key, string displayName, float weight, int value, Color color, Vector3 scale, string description)
        {
            MakeItem(key, displayName, ItemCategory.Weapon, weight, value, 1, color, scale, description, item =>
            {
                item.equipSlot = EquipSlot.Weapon;
            });
        }

        private static void CreateSurvivalItems()
        {
            //       key            name              category             kg    val use  color                                     scale
            MakeItem("CannedBeans", "Canned Beans",   ItemCategory.Food,   0.5f, 6,  30, new Color(0.65f, 0.35f, 0.15f), new Vector3(0.20f, 0.25f, 0.20f), "Cold but filling.");
            MakeItem("EnergyBar",   "Energy Bar",     ItemCategory.Food,   0.1f, 4,  15, new Color(0.85f, 0.70f, 0.30f), new Vector3(0.20f, 0.06f, 0.10f), "A small snack.");
            MakeItem("DriedMeat",   "Dried Meat",     ItemCategory.Food,   0.3f, 10, 40, new Color(0.55f, 0.20f, 0.15f), new Vector3(0.25f, 0.08f, 0.15f), "Keeps for months.");
            MakeItem("WaterBottle", "Water Bottle",   ItemCategory.Drink,  0.6f, 6,  35, new Color(0.30f, 0.60f, 0.95f), new Vector3(0.12f, 0.30f, 0.12f), "Clean drinking water.");
            MakeItem("SodaCan",     "Soda Can",       ItemCategory.Drink,  0.3f, 4,  20, new Color(0.90f, 0.20f, 0.25f), new Vector3(0.12f, 0.20f, 0.12f), "Flat, but wet.");
        }

        private static void CreateEquipmentItems()
        {
            //           key                name                slot            armor kg    val color                                     scale
            MakeApparel("Cap",             "Baseball Cap",     EquipSlot.Head,  1f,  0.2f, 8,  new Color(0.25f, 0.35f, 0.55f), new Vector3(0.30f, 0.15f, 0.30f), "Keeps the sun out of your eyes.");
            MakeApparel("CombatHelmet",    "Combat Helmet",    EquipSlot.Head,  6f,  1.5f, 60, new Color(0.30f, 0.40f, 0.25f), new Vector3(0.35f, 0.25f, 0.35f), "Military surplus helmet.");
            MakeApparel("WorkJacket",      "Work Jacket",      EquipSlot.Torso, 3f,  1.2f, 25, new Color(0.50f, 0.35f, 0.20f), new Vector3(0.45f, 0.10f, 0.35f), "Thick canvas jacket.");
            MakeApparel("KevlarVest",      "Kevlar Vest",      EquipSlot.Torso, 12f, 4.0f, 120, new Color(0.20f, 0.22f, 0.25f), new Vector3(0.45f, 0.15f, 0.35f), "Stops most small caliber rounds.");
            MakeApparel("ArmGuards",       "Arm Guards",       EquipSlot.Arms,  4f,  1.0f, 35, new Color(0.35f, 0.35f, 0.40f), new Vector3(0.40f, 0.12f, 0.15f), "Padded guards for forearms.");
            MakeApparel("CargoPants",      "Cargo Pants",      EquipSlot.Legs,  2f,  0.8f, 20, new Color(0.40f, 0.45f, 0.30f), new Vector3(0.35f, 0.15f, 0.30f), "Plenty of pockets.");
            MakeApparel("ReinforcedPants", "Reinforced Pants", EquipSlot.Legs,  6f,  2.0f, 70, new Color(0.25f, 0.25f, 0.30f), new Vector3(0.35f, 0.15f, 0.30f), "Pants with hard knee and shin plates.");
            MakeApparel("Sneakers",        "Sneakers",         EquipSlot.Feet,  1f,  0.5f, 10, new Color(0.80f, 0.80f, 0.80f), new Vector3(0.20f, 0.12f, 0.30f), "Worn but comfortable.");
            MakeApparel("CombatBoots",     "Combat Boots",     EquipSlot.Feet,  4f,  1.4f, 45, new Color(0.15f, 0.12f, 0.10f), new Vector3(0.22f, 0.20f, 0.32f), "Sturdy leather boots.");

            MakeWeapon("AssaultRifle", "Assault Rifle", 3.5f, 150, new Color(0.25f, 0.30f, 0.20f), new Vector3(0.70f, 0.12f, 0.15f), "Automatic rifle. Balanced and reliable.");
            MakeWeapon("HuntingRifle", "Hunting Rifle", 4.0f, 130, new Color(0.45f, 0.30f, 0.15f), new Vector3(0.80f, 0.10f, 0.15f), "Slow, accurate and hits hard.");
            MakeWeapon("Smg",          "SMG",           2.5f, 110, new Color(0.30f, 0.30f, 0.35f), new Vector3(0.50f, 0.12f, 0.15f), "Compact and fast, but weak per shot.");

            MakeItem("Bandage", "Bandage",  ItemCategory.Consumable, 0.2f, 8,  20, new Color(0.95f, 0.95f, 0.90f), new Vector3(0.20f, 0.08f, 0.20f), "Restores a little health.");
            MakeItem("Medkit",  "Medkit",   ItemCategory.Consumable, 0.8f, 30, 50, new Color(0.90f, 0.15f, 0.15f), new Vector3(0.35f, 0.20f, 0.25f), "Restores a lot of health.");
        }

        // ------------------------------------------------------------------ Weapon prefabs

        private struct WeaponSpec
        {
            public string key;
            public string name;
            public bool automatic;
            public float damage;
            public float fireRate;
            public float range;
            public int magazine;
            public float reload;
            public float scale;
            public bool keepSourceStats;
        }

        /// <summary>
        /// Builds one weapon prefab per weapon item by copying the weapon the player currently holds in the scene,
        /// so muzzle, grip and hand alignment stay correct. Only runs for weapons that have no prefab yet.
        /// </summary>
        private static void EnsureWeaponPrefabs()
        {
            EnsureFolder("Assets/Prefabs/Weapons");

            var specs = new[]
            {
                new WeaponSpec { key = "AssaultRifle", name = "Assault Rifle", scale = 1f, keepSourceStats = true },
                new WeaponSpec { key = "HuntingRifle", name = "Hunting Rifle", automatic = false, damage = 70f, fireRate = 0.9f, range = 300f, magazine = 5, reload = 2.8f, scale = 1.05f },
                new WeaponSpec { key = "Smg", name = "SMG", automatic = true, damage = 14f, fireRate = 14f, range = 120f, magazine = 40, reload = 1.8f, scale = 0.85f }
            };

            WeaponController source = null;
            foreach (var spec in specs)
            {
                ItemDefinition item = Items[spec.key];
                if (item.weaponPrefab != null) continue;

                string path = $"Assets/Prefabs/Weapons/Weapon_{spec.key}.prefab";
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (existing != null)
                {
                    item.weaponPrefab = existing.GetComponent<WeaponController>();
                    EditorUtility.SetDirty(item);
                    continue;
                }

                if (source == null) source = FindSceneWeapon();
                if (source == null)
                {
                    Debug.LogWarning("[InventorySetupTool] No weapon found on the player in the open scene. Weapon prefabs were not created.");
                    return;
                }

                GameObject copy = Object.Instantiate(source.gameObject);
                copy.name = $"Weapon_{spec.key}";
                copy.transform.SetParent(null);
                copy.transform.localPosition = source.transform.localPosition;
                copy.transform.localRotation = source.transform.localRotation;
                copy.transform.localScale = source.transform.localScale * spec.scale;

                var copyWeapon = copy.GetComponent<WeaponController>();
                SetString(copyWeapon, "weaponName", spec.name);
                SetBool(copyWeapon, "infiniteAmmo", false);
                SetInt(copyWeapon, "currentAmmo", 0);
                SetInt(copyWeapon, "reserveAmmo", 0);

                if (!spec.keepSourceStats)
                {
                    SetBool(copyWeapon, "isAutomatic", spec.automatic);
                    SetFloat(copyWeapon, "damage", spec.damage);
                    SetFloat(copyWeapon, "fireRate", spec.fireRate);
                    SetFloat(copyWeapon, "range", spec.range);
                    SetInt(copyWeapon, "magazineCapacity", spec.magazine);
                    SetFloat(copyWeapon, "reloadDuration", spec.reload);
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(copy, path);
                Object.DestroyImmediate(copy);

                item.weaponPrefab = prefab.GetComponent<WeaponController>();
                EditorUtility.SetDirty(item);
            }
        }

        /// <summary>Weapons lying in the world use the real weapon model instead of the placeholder cube.</summary>
        private static void AssignWeaponWorldModels()
        {
            const string modelPath = "Assets/Pack_Adventure/Prefabs/rifle.prefab";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogWarning("[InventorySetupTool] Weapon model not found: " + modelPath);
                return;
            }

            AssignModel("AssaultRifle", model, 1f);
            AssignModel("HuntingRifle", model, 1.05f);
            AssignModel("Smg", model, 0.85f);
        }

        private static void AssignModel(string key, GameObject model, float scale)
        {
            ItemDefinition item = Items[key];
            if (item.worldModel != null) return;

            item.worldModel = model;
            item.worldModelScale = scale;
            EditorUtility.SetDirty(item);
        }

        private static WeaponController FindSceneWeapon()
        {
            GameObject player = FindPlayer();
            if (player == null) return null;

            var shooter = player.GetComponentInChildren<PlayerShooter>(true);
            if (shooter != null && shooter.CurrentWeapon != null) return shooter.CurrentWeapon;
            return player.GetComponentInChildren<WeaponController>(true);
        }

        private static ItemDatabase CreateDatabase()
        {
            var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DatabasePath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<ItemDatabase>();
                AssetDatabase.CreateAsset(db, DatabasePath);
            }

            db.items = ItemOrder.ToArray();
            EditorUtility.SetDirty(db);
            return db;
        }

        private static Material GetOrCreateMaterial(string path, Color color)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            mat = new Material(shader);
            mat.color = color;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static WorldPickup CreatePickupPrefab()
        {
            string path = $"{PrefabDir}/Pickup_Cube.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<WorldPickup>();

            Material mat = GetOrCreateMaterial($"{PrefabDir}/Mat_LootCube.mat", Color.white);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Pickup_Cube";
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var box = go.GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = Vector3.one * 2.5f;

            go.AddComponent<WorldPickup>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<WorldPickup>();
        }

        private static LootChest CreateChestPrefab(LootTable chestTable)
        {
            string path = $"{PrefabDir}/Chest_Cube.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<LootChest>();

            Material mat = GetOrCreateMaterial($"{PrefabDir}/Mat_Chest.mat", new Color(0.45f, 0.28f, 0.12f));

            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Chest_Cube";
            root.transform.localScale = new Vector3(1.0f, 0.6f, 0.6f);
            root.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lid.name = "Lid";
            lid.transform.SetParent(root.transform, false);
            lid.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            lid.transform.localScale = new Vector3(1.04f, 0.3f, 1.04f);
            lid.GetComponent<MeshRenderer>().sharedMaterial = mat;
            Object.DestroyImmediate(lid.GetComponent<BoxCollider>());

            var chest = root.AddComponent<LootChest>();
            chest.lootTable = chestTable;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<LootChest>();
        }

        private struct Entry
        {
            public string key;
            public float weight;
            public int min;
            public int max;

            public Entry(string key, float weight, int min, int max)
            {
                this.key = key;
                this.weight = weight;
                this.min = min;
                this.max = max;
            }
        }

        private static LootTable CreateTables(WorldPickup pickupPrefab, out LootTable chestTable, out LootTable enemyTable)
        {
            LootTable roadside = MakeTable("Loot_Roadside", pickupPrefab, 1, 1,
                new Entry("RustyCan", 6, 1, 1), new Entry("BrokenWatch", 6, 1, 1), new Entry("OldBoot", 6, 1, 1),
                new Entry("TeddyBear", 6, 1, 1), new Entry("CrackedPhone", 6, 1, 1), new Entry("EmptyBottle", 6, 1, 1),
                new Entry("ScrapMetal", 14, 1, 3), new Entry("Electronics", 8, 1, 2), new Entry("DuctTape", 8, 1, 2),
                new Entry("FuelCan", 8, 1, 1), new Entry("RifleAmmo", 10, 1, 2), new Entry("Coins", 12, 5, 25),
                new Entry("EnginePart", 3, 1, 1), new Entry("ToolKit", 2, 1, 1), new Entry("Flashlight", 2, 1, 1),
                new Entry("WaterFilter", 2, 1, 1));

            chestTable = MakeTable("Loot_Chest", pickupPrefab, 2, 4,
                new Entry("RifleAmmo", 12, 1, 3), new Entry("Coins", 12, 15, 60), new Entry("FuelCan", 10, 1, 2),
                new Entry("ScrapMetal", 12, 2, 6), new Entry("Electronics", 8, 1, 3), new Entry("DuctTape", 6, 1, 3),
                new Entry("EnginePart", 8, 1, 2), new Entry("ToolKit", 5, 1, 1), new Entry("Flashlight", 3, 1, 1),
                new Entry("WaterFilter", 3, 1, 1), new Entry("RustyCan", 2, 1, 1), new Entry("BrokenWatch", 2, 1, 1),
                new Entry("OldBoot", 2, 1, 1), new Entry("TeddyBear", 2, 1, 1), new Entry("CrackedPhone", 2, 1, 1),
                new Entry("EmptyBottle", 2, 1, 1));

            enemyTable = MakeTable("Loot_EnemyDrop", pickupPrefab, 1, 2,
                new Entry("RifleAmmo", 20, 1, 1), new Entry("Coins", 20, 3, 15), new Entry("FuelCan", 5, 1, 1),
                new Entry("ScrapMetal", 8, 1, 2), new Entry("RustyCan", 3, 1, 1), new Entry("BrokenWatch", 3, 1, 1),
                new Entry("OldBoot", 3, 1, 1), new Entry("CrackedPhone", 3, 1, 1));

            EnsureEntries(roadside,
                new Entry("Cap", 3, 1, 1), new Entry("WorkJacket", 3, 1, 1), new Entry("CargoPants", 3, 1, 1), new Entry("Sneakers", 3, 1, 1),
                new Entry("Bandage", 6, 1, 2), new Entry("Medkit", 2, 1, 1), new Entry("CombatHelmet", 1.5f, 1, 1),
                new Entry("KevlarVest", 1, 1, 1), new Entry("ArmGuards", 2, 1, 1), new Entry("ReinforcedPants", 1, 1, 1),
                new Entry("CombatBoots", 1.5f, 1, 1), new Entry("AssaultRifle", 1, 1, 1), new Entry("Smg", 1, 1, 1),
                new Entry("HuntingRifle", 0.7f, 1, 1));

            EnsureEntries(chestTable,
                new Entry("CombatHelmet", 4, 1, 1), new Entry("KevlarVest", 3, 1, 1), new Entry("ArmGuards", 4, 1, 1),
                new Entry("ReinforcedPants", 3, 1, 1), new Entry("CombatBoots", 4, 1, 1), new Entry("Cap", 3, 1, 1),
                new Entry("WorkJacket", 3, 1, 1), new Entry("CargoPants", 3, 1, 1), new Entry("Sneakers", 3, 1, 1),
                new Entry("AssaultRifle", 4, 1, 1), new Entry("Smg", 4, 1, 1), new Entry("HuntingRifle", 3, 1, 1),
                new Entry("Bandage", 8, 1, 3), new Entry("Medkit", 5, 1, 2));

            // Enemies drop the weapon they carry (EnemyController.heldWeaponItem), so no random weapons in this table.
            enemyTable.entries.RemoveAll(e => e.item != null && e.item.category == ItemCategory.Weapon);

            EnsureEntries(enemyTable,
                new Entry("Bandage", 8, 1, 2), new Entry("Medkit", 3, 1, 1), new Entry("CombatHelmet", 2, 1, 1),
                new Entry("KevlarVest", 1.5f, 1, 1), new Entry("CombatBoots", 2, 1, 1), new Entry("ArmGuards", 2, 1, 1),
                new Entry("Cap", 2, 1, 1), new Entry("WorkJacket", 2, 1, 1), new Entry("CargoPants", 2, 1, 1));

            EnsureEntries(roadside,
                new Entry("CannedBeans", 7, 1, 2), new Entry("EnergyBar", 8, 1, 2), new Entry("DriedMeat", 3, 1, 1),
                new Entry("WaterBottle", 8, 1, 2), new Entry("SodaCan", 7, 1, 2));

            EnsureEntries(chestTable,
                new Entry("CannedBeans", 8, 1, 3), new Entry("EnergyBar", 8, 1, 3), new Entry("DriedMeat", 6, 1, 2),
                new Entry("WaterBottle", 9, 1, 3), new Entry("SodaCan", 6, 1, 2));

            EnsureEntries(enemyTable,
                new Entry("EnergyBar", 5, 1, 1), new Entry("WaterBottle", 5, 1, 1), new Entry("CannedBeans", 3, 1, 1));

            return roadside;
        }

        /// <summary>Adds entries for items the table does not contain yet, so re-running the tool extends existing tables.</summary>
        private static void EnsureEntries(LootTable table, params Entry[] entries)
        {
            foreach (Entry e in entries)
            {
                ItemDefinition item = Items[e.key];
                if (table.entries.Exists(x => x.item == item)) continue;

                table.entries.Add(new LootEntry { item = item, weight = e.weight, minAmount = e.min, maxAmount = e.max });
            }
            EditorUtility.SetDirty(table);
        }

        private static LootTable MakeTable(string file, WorldPickup pickupPrefab, int minRolls, int maxRolls, params Entry[] entries)
        {
            string path = $"{LootDir}/{file}.asset";
            var table = AssetDatabase.LoadAssetAtPath<LootTable>(path);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<LootTable>();
                table.minRolls = minRolls;
                table.maxRolls = maxRolls;
                table.entries = new List<LootEntry>();
                foreach (Entry e in entries)
                {
                    table.entries.Add(new LootEntry
                    {
                        item = Items[e.key],
                        weight = e.weight,
                        minAmount = e.min,
                        maxAmount = e.max
                    });
                }
                AssetDatabase.CreateAsset(table, path);
            }

            table.pickupPrefab = pickupPrefab;
            EditorUtility.SetDirty(table);
            return table;
        }

        // ------------------------------------------------------------------ Prefabs

        private static void SetupEnemyPrefab(LootTable enemyTable)
        {
            if (!System.IO.File.Exists(EnemyPrefabPath)) { Debug.LogWarning("[InventorySetupTool] Enemy prefab not found: " + EnemyPrefabPath); return; }

            GameObject root = PrefabUtility.LoadPrefabContents(EnemyPrefabPath);
            var enemy = root.GetComponentInChildren<EnemyController>(true);
            if (enemy != null)
            {
                enemy.dropTable = enemyTable;
                enemy.heldWeaponItem = Items["AssaultRifle"];
                PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        private static void SetupChunkPrefab(LootTable roadsideTable, LootChest chestPrefab)
        {
            if (!System.IO.File.Exists(ChunkPrefabPath)) { Debug.LogWarning("[InventorySetupTool] Chunk prefab not found: " + ChunkPrefabPath); return; }

            GameObject root = PrefabUtility.LoadPrefabContents(ChunkPrefabPath);
            var spawner = root.GetComponent<RoadsideLootSpawner>();
            if (spawner == null) spawner = root.AddComponent<RoadsideLootSpawner>();
            spawner.pickupTable = roadsideTable;
            spawner.chestPrefab = chestPrefab;
            PrefabUtility.SaveAsPrefabAsset(root, ChunkPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
        }

        private static void SetupVehiclePrefab(string path)
        {
            if (!System.IO.File.Exists(path)) return;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root.GetComponent<VehicleController>() != null)
            {
                ConfigureVehicle(root, false);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        private static T GetOrAdd<T>(GameObject go, bool undo) where T : Component
        {
            var existing = go.GetComponent<T>();
            if (existing != null) return existing;
            return undo ? Undo.AddComponent<T>(go) : go.AddComponent<T>();
        }

        private static void ConfigureVehicle(GameObject vehicleObject, bool undo)
        {
            var vehicle = vehicleObject.GetComponent<VehicleController>();
            var storage = GetOrAdd<VehicleStorage>(vehicleObject, undo);
            var maintenance = GetOrAdd<VehicleMaintenance>(vehicleObject, undo);

            SetRef(maintenance, "vehicle", vehicle);
            SetRef(maintenance, "storage", storage);
            SetRef(maintenance, "fuelItem", Items["FuelCan"]);
            SetRef(maintenance, "repairItem", Items["ScrapMetal"]);
            SetRef(maintenance, "upgrades.Array.data[0].costItem", Items["ScrapMetal"]);
            SetRef(maintenance, "upgrades.Array.data[1].costItem", Items["EnginePart"]);
            SetRef(maintenance, "upgrades.Array.data[2].costItem", Items["ScrapMetal"]);
        }

        // ------------------------------------------------------------------ Scene

        private static void SetupScene(ItemDatabase database, WorldPickup pickupPrefab)
        {
            GameObject player = FindPlayer();
            var vehicles = Object.FindObjectsByType<VehicleController>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            PlayerInventory playerInventory = null;
            PlayerInteractor interactor = null;
            StarterAssetsInputs inputs = null;
            PlayerEquipment equipment = null;
            PlayerHealth health = null;
            PlayerSurvival survival = null;

            if (player != null)
            {
                playerInventory = GetOrAdd<PlayerInventory>(player, true);
                interactor = GetOrAdd<PlayerInteractor>(player, true);
                equipment = GetOrAdd<PlayerEquipment>(player, true);
                health = GetOrAdd<PlayerHealth>(player, true);
                survival = GetOrAdd<PlayerSurvival>(player, true);
                inputs = player.GetComponent<StarterAssetsInputs>();

                SetRef(survival, "health", health);
                SetRef(survival, "inputs", inputs);

                SetRef(playerInventory, "dropPickupPrefab", pickupPrefab);
                SetRef(interactor, "inventory", playerInventory);

                SetRef(equipment, "inventory", playerInventory);
                SetRef(equipment, "shooter", player.GetComponent<PlayerShooter>());
                SetRef(equipment, "animator", player.GetComponent<Animator>());
                SetRef(equipment, "placeholderMaterial", AssetDatabase.LoadAssetAtPath<Material>($"{PrefabDir}/Mat_LootCube.mat"));
                SetRef(equipment, "startingWeapon", Items["AssaultRifle"]);
                SetRef(health, "equipment", equipment);

                var weapon = player.GetComponentInChildren<WeaponController>(true);
                if (weapon != null) SetBool(weapon, "infiniteAmmo", false);
            }
            else
            {
                Debug.LogWarning("[InventorySetupTool] Player not found (tag 'Player' or name 'PlayerArmature'). Player side was skipped.");
            }

            VehicleStorage vehicleStorage = null;
            VehicleMaintenance vehicleMaintenance = null;
            VehicleInteraction vehicleInteraction = null;

            foreach (var vehicle in vehicles)
            {
                if (vehicle.gameObject.scene.name == null) continue;

                ConfigureVehicle(vehicle.gameObject, true);
                var maintenance = vehicle.GetComponent<VehicleMaintenance>();
                SetRef(maintenance, "playerInventory", playerInventory);

                if (vehicleStorage == null)
                {
                    vehicleStorage = vehicle.GetComponent<VehicleStorage>();
                    vehicleMaintenance = maintenance;
                    vehicleInteraction = vehicle.GetComponent<VehicleInteraction>();
                }
            }

            if (vehicleStorage == null)
                Debug.LogWarning("[InventorySetupTool] No vehicle found in the open scene. Vehicle side of the UI was left unwired.");

            BuildUI(database, playerInventory, interactor, inputs, equipment, health, survival, vehicleStorage, vehicleMaintenance, vehicleInteraction);
            AddVehicleConditionTexts();
            EnsureSceneInBuildSettings();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        /// <summary>The death restart reloads the scene by build index, so the scene must be in the build settings.</summary>
        private static void EnsureSceneInBuildSettings()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path)) return;

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scene.path)) return;

            scenes.Add(new EditorBuildSettingsScene(scene.path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static GameObject FindPlayer()
        {
            GameObject player = null;
            try { player = GameObject.FindGameObjectWithTag("Player"); }
            catch (UnityException) { }

            if (player == null) player = GameObject.Find("PlayerArmature");
            return player;
        }

        private static void AddVehicleConditionTexts()
        {
            var vehicleUI = Object.FindFirstObjectByType<VehicleUI>(FindObjectsInactive.Include);
            if (vehicleUI == null || vehicleUI.dashboardPanel == null) return;
            if (vehicleUI.healthText != null && vehicleUI.warningText != null) return;

            Transform panel = vehicleUI.dashboardPanel.transform;

            Text health = NewText("HullText", panel, "HULL: 100%", 22, TextAnchor.MiddleCenter, Color.white);
            Place(health.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(300f, 30f));

            Text warning = NewText("WarningText", panel, string.Empty, 24, TextAnchor.MiddleCenter, new Color(1f, 0.35f, 0.25f));
            Place(warning.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -45f), new Vector2(900f, 34f));

            SetRef(vehicleUI, "healthText", health);
            SetRef(vehicleUI, "warningText", warning);
        }

        // ------------------------------------------------------------------ UI

        private static void BuildUI(ItemDatabase database, PlayerInventory playerInventory, PlayerInteractor interactor,
            StarterAssetsInputs inputs, PlayerEquipment equipment, PlayerHealth health, PlayerSurvival survival, VehicleStorage vehicleStorage,
            VehicleMaintenance vehicleMaintenance, VehicleInteraction vehicleInteraction)
        {
            var oldCanvas = GameObject.Find("InventoryCanvas");
            if (oldCanvas != null) Undo.DestroyObjectImmediate(oldCanvas);

            EnsureEventSystem();

            var canvasGo = new GameObject("InventoryCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create InventoryCanvas");

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Transform root = canvasGo.transform;

            // HUD
            Text healthHud = NewText("HealthText", root, "HEALTH: 100 / 100    ARMOR: 0", 22, TextAnchor.MiddleLeft, new Color(1f, 0.45f, 0.4f));
            Place(healthHud.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(520f, 30f));

            Text survivalHud = NewText("SurvivalText", root, "HUNGER: 100    THIRST: 100", 20, TextAnchor.MiddleLeft, Color.white);
            Place(survivalHud.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -52f), new Vector2(520f, 28f));

            Text money = NewText("MoneyText", root, "MONEY: 0", 22, TextAnchor.MiddleRight, new Color(1f, 0.85f, 0.3f));
            Place(money.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(400f, 30f));
            Text weight = NewText("WeightText", root, "BACKPACK: 0 / 30 kg", 20, TextAnchor.MiddleRight, Color.white);
            Place(weight.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -52f), new Vector2(400f, 28f));
            Text hint = NewText("HintText", root, "TAB: Inventory    E: Interact    F: Vehicle", 16, TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.6f));
            Place(hint.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -80f), new Vector2(500f, 24f));

            // Interact prompt and feedback
            var promptRoot = NewRect("InteractPrompt", root);
            Place(promptRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(620f, 44f));
            var promptBg = promptRoot.gameObject.AddComponent<Image>();
            promptBg.color = new Color(0f, 0f, 0f, 0.55f);
            promptBg.raycastTarget = false;
            Text promptText = NewText("PromptText", promptRoot, string.Empty, 22, TextAnchor.MiddleCenter, Color.white);
            Stretch(promptText.rectTransform);
            promptRoot.gameObject.SetActive(false);

            Text feedback = NewText("FeedbackText", root, string.Empty, 22, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.5f));
            Place(feedback.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 245f), new Vector2(900f, 36f));

            // Death panel
            var deathPanel = NewRect("DeathPanel", root);
            Stretch(deathPanel);
            var deathBg = deathPanel.gameObject.AddComponent<Image>();
            deathBg.color = new Color(0.35f, 0f, 0f, 0.65f);
            deathBg.raycastTarget = false;
            Text deathText = NewText("DeathText", deathPanel, "YOU DIED", 72, TextAnchor.MiddleCenter, Color.white);
            Stretch(deathText.rectTransform);
            deathPanel.gameObject.SetActive(false);

            // Inventory panel
            var panel = NewRect("InventoryPanel", root);
            Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1360f, 780f));
            var panelBg = panel.gameObject.AddComponent<Image>();
            panelBg.color = new Color(0.05f, 0.06f, 0.08f, 0.94f);

            int rowCount = database.Count;
            const float rowStep = 32f;

            // Left column: backpack + equipment
            var playerColumn = NewRect("PlayerColumn", panel);
            TopLeft(playerColumn, 20f, 15f, 720f, 740f);
            Text playerTitle = NewText("PlayerTitle", playerColumn, "BACKPACK", 20, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.3f));
            TopLeft(playerTitle.rectTransform, 0f, 0f, 720f, 34f);

            BuildScroll(playerColumn, "PlayerScroll", 0f, 38f, 720f, 440f, rowCount * rowStep, out RectTransform playerContent);
            var playerRows = new ItemRowUI[rowCount];
            for (int i = 0; i < rowCount; i++)
                playerRows[i] = BuildRow(playerContent, $"PlayerRow_{i}", 0f, i * rowStep, 710f, 5);

            Text equipTitle = NewText("EquipTitle", playerColumn, "EQUIPPED", 20, TextAnchor.MiddleLeft, new Color(0.6f, 1f, 0.6f));
            TopLeft(equipTitle.rectTransform, 0f, 490f, 720f, 30f);

            var equipRows = new ItemRowUI[6];
            for (int i = 0; i < equipRows.Length; i++)
            {
                float x = (i / 3) * 365f;
                float y = 524f + (i % 3) * 34f;
                equipRows[i] = BuildRow(playerColumn, $"EquipRow_{(EquipSlot)i}", x, y, 355f, 1);
            }

            // Right column: vehicle
            var vehicleColumn = NewRect("VehicleColumn", panel);
            TopLeft(vehicleColumn, 760f, 15f, 580f, 740f);
            Text vehicleTitle = NewText("VehicleTitle", vehicleColumn, "VEHICLE STORAGE", 20, TextAnchor.MiddleLeft, new Color(0.5f, 0.85f, 1f));
            TopLeft(vehicleTitle.rectTransform, 0f, 0f, 580f, 34f);
            Text vehicleInfo = NewText("VehicleInfo", vehicleColumn, string.Empty, 18, TextAnchor.MiddleLeft, Color.white);
            TopLeft(vehicleInfo.rectTransform, 0f, 36f, 580f, 28f);

            Button storeAll = NewButton("StoreAllButton", vehicleColumn, "Store All", 16, out Text storeAllLabel);
            TopLeft(storeAll.GetComponent<RectTransform>(), 0f, 70f, 180f, 36f);
            Button refuel = NewButton("RefuelButton", vehicleColumn, "Refuel", 15, out Text refuelLabel);
            TopLeft(refuel.GetComponent<RectTransform>(), 195f, 70f, 180f, 36f);
            Button repair = NewButton("RepairButton", vehicleColumn, "Repair", 15, out Text repairLabel);
            TopLeft(repair.GetComponent<RectTransform>(), 410f, 70f, 180f, 36f);

            var upgradeButtons = new Button[3];
            var upgradeLabels = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                upgradeButtons[i] = NewButton($"UpgradeButton_{i}", vehicleColumn, "Upgrade", 14, out upgradeLabels[i]);
                TopLeft(upgradeButtons[i].GetComponent<RectTransform>(), i * 195f, 112f, 180f, 46f);
            }

            BuildScroll(vehicleColumn, "VehicleScroll", 0f, 172f, 580f, 520f, rowCount * rowStep, out RectTransform vehicleContent);
            var vehicleRows = new ItemRowUI[rowCount];
            for (int i = 0; i < rowCount; i++)
                vehicleRows[i] = BuildRow(vehicleContent, $"VehicleRow_{i}", 0f, i * rowStep, 570f, 2);

            Text status = NewText("StatusText", panel, string.Empty, 18, TextAnchor.MiddleLeft, new Color(1f, 0.92f, 0.5f));
            TopLeft(status.rectTransform, 20f, 722f, 1320f, 28f);
            Text closeHint = NewText("CloseHint", panel, "TAB / ESC: close", 14, TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.5f));
            TopLeft(closeHint.rectTransform, 1040f, 750f, 300f, 24f);

            // Wire InventoryUI
            var ui = Undo.AddComponent<InventoryUI>(canvasGo);
            SetInt(ui, "uiVersion", InventoryUI.CurrentUiVersion);
            SetRef(ui, "database", database);
            SetRef(ui, "playerInventory", playerInventory);
            SetRef(ui, "equipment", equipment);
            SetRef(ui, "vehicleStorage", vehicleStorage);
            SetRef(ui, "vehicleMaintenance", vehicleMaintenance);
            SetRef(ui, "vehicleInteraction", vehicleInteraction);
            SetRef(ui, "playerInputs", inputs);
            SetRef(ui, "moneyText", money);
            SetRef(ui, "weightText", weight);
            SetRef(ui, "inventoryPanel", panel.gameObject);
            SetRef(ui, "playerTitleText", playerTitle);
            SetRef(ui, "equipTitleText", equipTitle);
            SetRef(ui, "vehicleTitleText", vehicleTitle);
            SetRef(ui, "vehicleInfoText", vehicleInfo);
            SetRef(ui, "statusText", status);
            SetRefArray(ui, "playerRows", playerRows);
            SetRefArray(ui, "equipRows", equipRows);
            SetRefArray(ui, "vehicleRows", vehicleRows);
            SetRef(ui, "storeAllButton", storeAll);
            SetRef(ui, "refuelButton", refuel);
            SetRef(ui, "refuelLabel", refuelLabel);
            SetRef(ui, "repairButton", repair);
            SetRef(ui, "repairLabel", repairLabel);
            SetRefArray(ui, "upgradeButtons", upgradeButtons);
            SetRefArray(ui, "upgradeLabels", upgradeLabels);

            if (interactor != null)
            {
                SetRef(interactor, "promptRoot", promptRoot.gameObject);
                SetRef(interactor, "promptText", promptText);
                SetRef(interactor, "feedbackText", feedback);
            }

            if (survival != null) SetRef(survival, "statusText", survivalHud);

            if (health != null)
            {
                SetRef(health, "healthText", healthHud);
                SetRef(health, "deathPanel", deathPanel.gameObject);
            }

            panel.gameObject.SetActive(false);
        }

        private static void BuildScroll(Transform parent, string name, float x, float y, float width, float height, float contentHeight,
            out RectTransform content)
        {
            var scrollRect = NewRect(name, parent);
            TopLeft(scrollRect, x, y, width, height);

            var background = scrollRect.gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.25f);

            var viewport = NewRect("Viewport", scrollRect);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, contentHeight + 4f);

            // Rows are stacked by a layout group so hidden (inactive) rows collapse and visible items always start at the top
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.spacing = 2f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
        }

        /// <summary>
        /// Builds one row with the given number of buttons, laid out left to right at the right end of the row.
        /// </summary>
        private static ItemRowUI BuildRow(Transform parent, string name, float x, float y, float width, int buttonCount)
        {
            var rt = NewRect(name, parent);
            TopLeft(rt, x, y, width, 30f);

            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.06f);
            bg.raycastTarget = false;

            var row = rt.gameObject.AddComponent<ItemRowUI>();

            float buttonWidth = buttonCount >= 4 ? 64f : 74f;
            const float gap = 4f;
            float buttonsWidth = buttonCount * (buttonWidth + gap) + gap;

            Text label = NewText("Label", rt, string.Empty, width < 400f ? 14 : 16, TextAnchor.MiddleLeft, Color.white);
            TopLeft(label.rectTransform, 8f, 0f, width - buttonsWidth - 8f, 30f);
            row.label = label;

            float startX = width - buttonsWidth + gap;
            int fontSize = buttonCount >= 4 ? 12 : 14;

            row.buttons = new Button[buttonCount];
            row.buttonLabels = new Text[buttonCount];
            for (int k = 0; k < buttonCount; k++)
            {
                row.buttons[k] = NewButton($"Button_{k}", rt, string.Empty, fontSize, out row.buttonLabels[k]);
                TopLeft(row.buttons[k].GetComponent<RectTransform>(), startX + k * (buttonWidth + gap), 2f, buttonWidth, 26f);
            }

            return row;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Text NewText(string name, Transform parent, string content, int size, TextAnchor anchor, Color color)
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var rt = NewRect(name, parent);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = _font;
            text.text = content;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Button NewButton(string name, Transform parent, string content, int fontSize, out Text label)
        {
            var rt = NewRect(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = new Color(0.22f, 0.28f, 0.34f, 1f);

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            label = NewText("Label", rt, content, fontSize, TextAnchor.MiddleCenter, Color.white);
            Stretch(label.rectTransform);
            return button;
        }

        private static void TopLeft(RectTransform rt, float x, float y, float width, float height)
        {
            Place(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(width, height));
        }

        private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------------ Serialized helpers

        private static void SetRef(Object target, string propertyPath, Object value)
        {
            if (target == null) return;

            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(propertyPath);
            if (property == null)
            {
                Debug.LogWarning($"[InventorySetupTool] Property '{propertyPath}' not found on {target.GetType().Name}.");
                return;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        private static void SetRefArray(Object target, string propertyPath, Object[] values)
        {
            if (target == null) return;

            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(propertyPath);
            if (property == null)
            {
                Debug.LogWarning($"[InventorySetupTool] Property '{propertyPath}' not found on {target.GetType().Name}.");
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedProperties();
        }

        private static void SetString(Object target, string propertyPath, string value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(propertyPath);
            if (property == null) return;
            property.stringValue = value;
            so.ApplyModifiedProperties();
        }

        private static void SetInt(Object target, string propertyPath, int value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(propertyPath);
            if (property == null) return;
            property.intValue = value;
            so.ApplyModifiedProperties();
        }

        private static void SetFloat(Object target, string propertyPath, float value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(propertyPath);
            if (property == null) return;
            property.floatValue = value;
            so.ApplyModifiedProperties();
        }

        private static void SetBool(Object target, string propertyPath, bool value)
        {
            if (target == null) return;

            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(propertyPath);
            if (property == null) return;

            property.boolValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
