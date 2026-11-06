using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Survival.Editor
{
    /// <summary>
    /// Builds the assets of Packages 2-5. Safe to run again (existing assets are updated in place):
    ///   - new items (water types, meat/fish, plants, materials, tools, clothes) + survival fields on existing items
    ///   - recipes (hand, campfire, vehicle, distiller)
    ///   - loot table additions + house / gas station / wreck tables, ItemDatabase
    ///   - Resources/SurvivalSettings.asset and Resources/StatSettings.asset
    /// Menu: Endless Survival > Setup Survival Packages 2-5.
    /// Batch: -executeMethod EndlessSurvival.Survival.Editor.SurvivalPackagesSetup.RunBatch
    /// </summary>
    public static class SurvivalPackagesSetup
    {
        private const string ItemsDir = "Assets/Data/Items";
        private const string RecipesDir = "Assets/Data/Recipes";
        private const string LootDir = "Assets/Data/Loot";
        private const string DatabasePath = "Assets/Data/ItemDatabase.asset";
        private const string SettingsPath = "Assets/Resources/SurvivalSettings.asset";
        private const string StatSettingsPath = "Assets/Resources/StatSettings.asset";

        private static readonly List<string> Log = new List<string>();
        private static readonly Dictionary<string, ItemDefinition> Items = new Dictionary<string, ItemDefinition>();

        [MenuItem("Endless Survival/Setup Survival Packages 2-5")]
        public static void RunMenu()
        {
            Run();
            EditorUtility.DisplayDialog("Survival Packages 2-5", string.Join("\n", Log), "OK");
        }

        public static void RunBatch()
        {
            Run();
            foreach (var line in Log) Debug.Log("[SurvivalPackagesSetup] " + line);
        }

        private static void Run()
        {
            Log.Clear();
            Items.Clear();
            EnsureFolder("Assets/Data", "Recipes");
            EnsureFolder("Assets", "Resources");

            CreateItems();
            PatchExistingItems();
            var recipes = CreateRecipes();
            UpdateDatabase();
            var tables = UpdateLoot();
            CreateSettings(recipes, tables);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log.Add("Done.");
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
        }

        // ================================================================== items

        private static ItemDefinition Existing(string key)
        {
            if (Items.TryGetValue(key, out var it)) return it;
            it = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsDir}/Item_{key}.asset");
            if (it != null) Items[key] = it;
            else Log.Add("WARNING missing item: Item_" + key);
            return it;
        }

        private static ItemDefinition Item(string key, string displayName, ItemCategory category, float weight, int value, int useAmount,
            Color color, Vector3 scale, string description, Action<ItemDefinition> configure = null)
        {
            string path = $"{ItemsDir}/Item_{key}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            bool isNew = item == null;
            if (isNew) item = ScriptableObject.CreateInstance<ItemDefinition>();

            item.displayName = displayName;
            item.category = category;
            item.weight = weight;
            item.value = value;
            item.useAmount = useAmount;
            item.worldColor = color;
            item.worldScale = scale;
            item.description = description;
            // reset survival fields so re-runs stay consistent
            item.spoilHours = 0f; item.spoilsInto = null; item.poisonChance = 0f; item.thirstPenalty = 0f;
            item.stressRelief = 0f; item.warmth = 0f; item.leavesItem = null; item.effects = ConsumableEffect.None;
            item.insulation = 0f; item.waterproof = 0f; item.noise = 0f; item.identifiedName = ""; item.natureToIdentify = 20f;
            configure?.Invoke(item);

            if (isNew) AssetDatabase.CreateAsset(item, path);
            else EditorUtility.SetDirty(item);
            Items[key] = item;
            return item;
        }

        private static readonly Vector3 Small = new Vector3(0.2f, 0.2f, 0.2f);
        private static readonly Vector3 Medium = new Vector3(0.35f, 0.3f, 0.35f);
        private static readonly Vector3 Flat = new Vector3(0.45f, 0.1f, 0.35f);

        private static void CreateItems()
        {
            var bottle = Existing("EmptyBottle");
            var rotten = Item("RottenFood", "Bozuk Yemek", ItemCategory.Food, 0.3f, 0, 5, new Color(0.3f, 0.35f, 0.2f), Small,
                "Kokuyor. Yersen büyük ihtimalle zehirlenirsin.", i => i.poisonChance = 0.85f);

            // --- materials ---
            Item("Firewood", "Odun", ItemCategory.Material, 1f, 1, 1, new Color(0.45f, 0.3f, 0.18f), new Vector3(0.5f, 0.15f, 0.15f),
                "Kuru ağaçtan. Kamp ateşi yakacağı ve craft malzemesi.");
            Item("Cloth", "Kumaş", ItemCategory.Material, 0.15f, 2, 1, new Color(0.75f, 0.7f, 0.6f), Flat,
                "Bandaj, kıyafet onarımı ve brandalar için.");
            Item("RawHide", "Ham Deri", ItemCategory.Material, 0.8f, 4, 1, new Color(0.55f, 0.4f, 0.28f), Flat,
                "Avdan. Deri giysi yapılır.");
            Item("Salt", "Tuz", ItemCategory.Material, 0.1f, 3, 1, new Color(0.95f, 0.95f, 0.95f), Small,
                "Deniz suyu damıtılınca çıkar. Et tuzlamak için.");
            Item("Herb", "Şifalı Bitki", ItemCategory.Material, 0.05f, 3, 1, new Color(0.35f, 0.65f, 0.3f), Small,
                "Merhem ve bitki çayı yapılır.");
            Item("CarTire", "Lastik", ItemCategory.Material, 8f, 8, 1, new Color(0.12f, 0.12f, 0.12f), new Vector3(0.6f, 0.2f, 0.6f),
                "Enkazdan sökülmüş araç lastiği.");
            Item("BrokenFuelCan", "Delik Bidon", ItemCategory.Junk, 1f, 1, 1, new Color(0.5f, 0.3f, 0.2f), Medium,
                "Bantla tamir edilirse işe yarar.");

            // --- water ---
            Item("DirtyWater", "Kirli Su", ItemCategory.Drink, 0.6f, 1, 35, new Color(0.45f, 0.5f, 0.4f), Small,
                "Dereden. Kamp ateşinde kaynatılmalı; böyle içilirse hastalık riski.", i => { i.poisonChance = 0.35f; i.leavesItem = bottle; });
            Item("SaltWater", "Tuzlu Su", ItemCategory.Drink, 0.6f, 0, 5, new Color(0.35f, 0.5f, 0.65f), Small,
                "Deniz suyu. İçilirse susuzluk artar. Damıtılınca temiz su ve tuz çıkar.", i => { i.thirstPenalty = 20f; i.leavesItem = bottle; });

            // --- food ---
            Item("RawMeat", "Çiğ Et", ItemCategory.Food, 0.5f, 3, 15, new Color(0.75f, 0.25f, 0.25f), Small,
                "Avdan. Pişir, tuzla, kurut veya tütsüle. Çiğ yenirse zehirleyebilir.",
                i => { i.spoilHours = 36f; i.spoilsInto = rotten; i.poisonChance = 0.3f; });
            Item("RawFish", "Dere Balığı", ItemCategory.Food, 0.4f, 3, 10, new Color(0.6f, 0.65f, 0.6f), Small,
                "Dereden. Çabuk bozulur.", i => { i.spoilHours = 24f; i.spoilsInto = rotten; i.poisonChance = 0.25f; });
            Item("SeaFish", "Deniz Balığı", ItemCategory.Food, 0.6f, 4, 14, new Color(0.5f, 0.6f, 0.7f), Small,
                "Denizden. Çabuk bozulur.", i => { i.spoilHours = 24f; i.spoilsInto = rotten; i.poisonChance = 0.25f; });
            Item("CookedFish", "Pişmiş Balık", ItemCategory.Food, 0.35f, 5, 35, new Color(0.8f, 0.65f, 0.45f), Small,
                "Sıcak ve doyurucu.", i => { i.spoilHours = 48f; i.spoilsInto = rotten; i.stressRelief = 4f; i.warmth = 3f; });
            Item("SaltedMeat", "Tuzlanmış Et", ItemCategory.Food, 0.45f, 6, 30, new Color(0.7f, 0.35f, 0.35f), Small,
                "Günlerce dayanır.", i => { i.spoilHours = 240f; i.spoilsInto = rotten; });
            Item("SmokedMeat", "Tütsülenmiş Et", ItemCategory.Food, 0.4f, 7, 35, new Color(0.5f, 0.28f, 0.2f), Small,
                "Ateşte tütsülendi, uzun dayanır.", i => { i.spoilHours = 200f; i.spoilsInto = rotten; i.stressRelief = 2f; });
            Item("Berries", "Böğürtlen", ItemCategory.Food, 0.1f, 1, 8, new Color(0.35f, 0.08f, 0.3f), Small,
                "Yabani meyve. Damıtıcıda alkole dönüşür.", i => { i.spoilHours = 48f; i.spoilsInto = rotten; i.stressRelief = 1f; });
            Item("Mushroom", "Mantar", ItemCategory.Food, 0.1f, 2, 10, new Color(0.65f, 0.5f, 0.35f), Small,
                "Yabani mantar. Hangisi olduğunu Doğa bilgisi söyler.",
                i => { i.spoilHours = 36f; i.spoilsInto = rotten; i.identifiedName = "Yenilebilir Mantar"; i.natureToIdentify = 20f; });
            Item("PoisonMushroom", "Mantar", ItemCategory.Food, 0.1f, 0, 10, new Color(0.65f, 0.5f, 0.35f), Small,
                "Yabani mantar. Hangisi olduğunu Doğa bilgisi söyler.",
                i => { i.spoilHours = 36f; i.spoilsInto = rotten; i.poisonChance = 0.9f; i.identifiedName = "Zehirli Mantar"; i.natureToIdentify = 20f; });

            // --- medicine / drinks ---
            Item("Alcohol", "Alkol", ItemCategory.Consumable, 0.5f, 8, 0, new Color(0.85f, 0.8f, 0.6f), Small,
                "Meyveden damıtıldı. Yarayı temizler (dezenfektan), stresi azaltır.",
                i => { i.effects = ConsumableEffect.CleansWound; i.stressRelief = 10f; i.leavesItem = bottle; });
            Item("HerbalOintment", "Şifalı Merhem", ItemCategory.Consumable, 0.15f, 10, 0, new Color(0.4f, 0.6f, 0.35f), Small,
                "Enfeksiyonu iyileştirir.", i => i.effects = ConsumableEffect.CuresInfection | ConsumableEffect.CleansWound);
            Item("HerbalTea", "Bitki Çayı", ItemCategory.Drink, 0.6f, 6, 15, new Color(0.6f, 0.5f, 0.25f), Small,
                "Zehirlenme ve hastalığa iyi gelir, ısıtır.",
                i => { i.effects = ConsumableEffect.CuresPoison; i.stressRelief = 8f; i.warmth = 6f; i.leavesItem = bottle; });

            // --- tools / gear ---
            Item("Torch", "Meşale", ItemCategory.Gear, 0.6f, 3, 1, new Color(0.6f, 0.4f, 0.2f), new Vector3(0.1f, 0.5f, 0.1f),
                "Odun ve kumaştan meşale.");
            Item("FishingRod", "Olta", ItemCategory.Gear, 0.8f, 8, 1, new Color(0.5f, 0.4f, 0.25f), new Vector3(0.05f, 0.05f, 1.2f),
                "Su kenarında [H] ile balık tut.");
            Item("EmptyFuelCan", "Boş Bidon", ItemCategory.Gear, 1f, 4, 1, new Color(0.7f, 0.2f, 0.15f), Medium,
                "Benzinlikte veya enkazdan hortumla doldurulur.");
            Item("VehicleRepairKit", "Araç Tamir Kiti", ItemCategory.Gear, 3f, 15, 1, new Color(0.4f, 0.45f, 0.5f), Medium,
                "Araç yanında Üretim > Araç'tan kullanılır (+40 araç canı).");

            // --- clothes ---
            Item("LeatherJacket", "Deri Ceket", ItemCategory.Apparel, 2.2f, 30, 1, new Color(0.4f, 0.27f, 0.18f), Medium,
                "Avdan çıkan deriden. Sıcak, sağlam, yağmuru az geçirir.",
                i => { i.equipSlot = EquipSlot.Torso; i.armor = 4f; i.insulation = 7f; i.waterproof = 0.6f; });
            Item("Raincoat", "Yağmurluk", ItemCategory.Apparel, 0.8f, 12, 1, new Color(0.85f, 0.7f, 0.1f), Medium,
                "Su geçirmez ama ince.", i => { i.equipSlot = EquipSlot.Torso; i.insulation = 2f; i.waterproof = 1f; });
            Item("WoolHat", "Yün Bere", ItemCategory.Apparel, 0.15f, 6, 1, new Color(0.5f, 0.2f, 0.2f), Small,
                "Başı sıcak tutar.", i => { i.equipSlot = EquipSlot.Head; i.insulation = 3f; });
            Item("Gloves", "Eldiven", ItemCategory.Apparel, 0.2f, 6, 1, new Color(0.25f, 0.25f, 0.25f), Small,
                "Ellere sıcaklık.", i => { i.equipSlot = EquipSlot.Arms; i.insulation = 2f; i.armor = 0.5f; });

            Log.Add($"Items: {Items.Count} survival items created/updated.");
        }

        private static void PatchExistingItems()
        {
            var bottle = Existing("EmptyBottle");
            var rotten = Items["RottenFood"];

            void Patch(string key, Action<ItemDefinition> a)
            {
                var it = Existing(key);
                if (it == null) return;
                a(it);
                EditorUtility.SetDirty(it);
            }

            Patch("CookedMeat", i => { i.spoilHours = 48f; i.spoilsInto = rotten; i.stressRelief = 5f; i.warmth = 3f; });
            Patch("WarmStew", i => { i.stressRelief = 6f; i.warmth = 6f; });
            Patch("DriedMeat", i => { i.spoilHours = 0f; });
            Patch("BoiledWater", i => i.leavesItem = bottle);
            Patch("WaterBottle", i => i.leavesItem = bottle);
            Patch("Medkit", i => i.effects |= ConsumableEffect.CleansWound);
            Patch("EnergyBar", i => i.effects |= ConsumableEffect.RestoresStamina);
            Patch("SodaCan", i => i.effects |= ConsumableEffect.ReducesFatigue);

            // 2.16 clothing values: warmth, waterproof, noise (heavy armour is louder)
            Patch("Cap", i => { i.insulation = 1f; });
            Patch("WorkJacket", i => { i.insulation = 5f; i.waterproof = 0.3f; });
            Patch("CargoPants", i => { i.insulation = 2f; i.waterproof = 0.1f; });
            Patch("ReinforcedPants", i => { i.insulation = 3f; i.noise = 0.08f; });
            Patch("CombatBoots", i => { i.insulation = 2f; i.waterproof = 0.5f; i.noise = 0.1f; });
            Patch("Sneakers", i => { i.insulation = 1f; });
            Patch("KevlarVest", i => { i.insulation = 2f; i.noise = 0.2f; });
            Patch("CombatHelmet", i => { i.insulation = 1f; i.noise = 0.05f; });
            Patch("ArmGuards", i => { i.insulation = 1f; i.noise = 0.05f; });
            Log.Add("Existing items: survival fields set (spoilage, bottles, clothing warmth/noise).");
        }

        // ================================================================== recipes

        private static (ItemDefinition, int) I(string key, int amount = 1) => (Items.TryGetValue(key, out var it) ? it : Existing(key), amount);

        private static RecipeDefinition Recipe(string key, string name, CraftStation station, float seconds, StatType trains,
            (ItemDefinition, int)[] inputs, (ItemDefinition, int)[] outputs, string[] tools = null,
            RecipeEffect effect = RecipeEffect.None, bool natureBonus = false, string description = "")
        {
            string path = $"{RecipesDir}/Recipe_{key}.asset";
            var r = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
            bool isNew = r == null;
            if (isNew) r = ScriptableObject.CreateInstance<RecipeDefinition>();
            r.displayName = name;
            r.description = description;
            r.station = station;
            r.craftSeconds = seconds;
            r.trainsStat = trains;
            r.effect = effect;
            r.natureBonusOutput = natureBonus;
            r.inputs = Array.ConvertAll(inputs, x => new RecipeIngredient(x.Item1, x.Item2));
            r.outputs = Array.ConvertAll(outputs ?? new (ItemDefinition, int)[0], x => new RecipeIngredient(x.Item1, x.Item2));
            var toolList = new List<ItemDefinition>();
            if (tools != null) foreach (var t in tools) { var it = Existing(t); if (it != null) toolList.Add(it); }
            r.tools = toolList.ToArray();
            if (isNew) AssetDatabase.CreateAsset(r, path);
            else EditorUtility.SetDirty(r);
            return r;
        }

        private static RecipeDefinition[] CreateRecipes()
        {
            var none = new (ItemDefinition, int)[0];
            var M = StatType.Mechanics;
            var N = StatType.Nature;
            var list = new List<RecipeDefinition>
            {
                // Hand
                Recipe("Bandage", "Bandaj", CraftStation.Hand, 2f, StatType.FirstAid, new[] { I("Cloth", 2) }, new[] { I("Bandage") }),
                Recipe("TearTeddy", "Oyuncak ayıyı sök (kumaş)", CraftStation.Hand, 1.5f, M, new[] { I("TeddyBear") }, new[] { I("Cloth", 2) }),
                Recipe("Torch", "Meşale", CraftStation.Hand, 2f, M, new[] { I("Firewood"), I("Cloth") }, new[] { I("Torch") }),
                Recipe("WoodenClub", "Sopa", CraftStation.Hand, 3f, M, new[] { I("Firewood", 2) }, new[] { I("WoodenClub") }),
                Recipe("FishingRod", "Olta", CraftStation.Hand, 4f, M, new[] { I("Firewood"), I("Cloth", 2) }, new[] { I("FishingRod") }),
                Recipe("FixFuelCan", "Bidon tamiri", CraftStation.Hand, 3f, M, new[] { I("BrokenFuelCan"), I("DuctTape") }, new[] { I("EmptyFuelCan") }),
                Recipe("SaltMeat", "Et tuzla", CraftStation.Hand, 2f, N, new[] { I("RawMeat"), I("Salt") }, new[] { I("SaltedMeat") }),
                Recipe("SaltFish", "Balık tuzla", CraftStation.Hand, 2f, N, new[] { I("RawFish"), I("Salt") }, new[] { I("SaltedMeat") }),
                Recipe("SaltSeaFish", "Deniz balığı tuzla", CraftStation.Hand, 2f, N, new[] { I("SeaFish"), I("Salt") }, new[] { I("SaltedMeat") }),
                Recipe("Ointment", "Şifalı merhem", CraftStation.Hand, 3f, N, new[] { I("Herb", 2), I("Cloth") }, new[] { I("HerbalOintment") }),
                Recipe("LeatherJacket", "Deri ceket", CraftStation.Hand, 8f, M, new[] { I("RawHide", 3) }, new[] { I("LeatherJacket") }, new[] { "Knife" }),
                Recipe("RepairClothes", "Kıyafetleri onar", CraftStation.Hand, 4f, M, new[] { I("Cloth", 2) }, none, null, RecipeEffect.RepairClothing),
                Recipe("CleanWoundWater", "Yarayı kaynamış suyla temizle", CraftStation.Hand, 2f, StatType.FirstAid, new[] { I("BoiledWater") }, new[] { I("EmptyBottle") }, null, RecipeEffect.CleanWound),
                Recipe("CleanWoundAlcohol", "Yarayı alkolle temizle", CraftStation.Hand, 2f, StatType.FirstAid, new[] { I("Alcohol") }, new[] { I("EmptyBottle") }, null, RecipeEffect.CleanWound),
                Recipe("RabbitTrap", "Tavşan tuzağı kur", CraftStation.Hand, 4f, N, new[] { I("Firewood", 2), I("Cloth") }, none, null, RecipeEffect.PlaceRabbitTrap),

                // Campfire
                Recipe("BoilDirtyWater", "Kirli suyu kaynat", CraftStation.Campfire, 4f, N, new[] { I("DirtyWater") }, new[] { I("BoiledWater") }),
                Recipe("CookMeat", "Et pişir", CraftStation.Campfire, 4f, N, new[] { I("RawMeat") }, new[] { I("CookedMeat") }, null, RecipeEffect.None, true),
                Recipe("CookFish", "Balık pişir", CraftStation.Campfire, 3f, N, new[] { I("RawFish") }, new[] { I("CookedFish") }, null, RecipeEffect.None, true),
                Recipe("CookSeaFish", "Deniz balığı pişir", CraftStation.Campfire, 3f, N, new[] { I("SeaFish") }, new[] { I("CookedFish") }, null, RecipeEffect.None, true),
                Recipe("SmokeMeat", "Et tütsüle", CraftStation.Campfire, 10f, N, new[] { I("RawMeat", 2), I("Firewood") }, new[] { I("SmokedMeat", 2) }),
                Recipe("DryMeat", "Et kurut", CraftStation.Campfire, 12f, N, new[] { I("RawMeat") }, new[] { I("DriedMeat") }),
                Recipe("HerbalTea", "Bitki çayı", CraftStation.Campfire, 4f, N, new[] { I("Herb"), I("BoiledWater") }, new[] { I("HerbalTea") }),
                Recipe("Distiller", "Damıtıcı kur", CraftStation.Campfire, 8f, M, new[] { I("ScrapMetal", 3), I("RubberHose"), I("EmptyBottle") }, none, null, RecipeEffect.PlaceDistiller),

                // Distiller (slower than boiling)
                Recipe("DistillSalt", "Deniz suyunu damıt", CraftStation.Distiller, 15f, N, new[] { I("SaltWater", 2) }, new[] { I("BoiledWater"), I("Salt"), I("EmptyBottle") }),
                Recipe("DistillAlcohol", "Meyveden alkol", CraftStation.Distiller, 20f, N, new[] { I("Berries", 4), I("EmptyBottle") }, new[] { I("Alcohol") }),

                // Vehicle
                Recipe("RepairKit", "Araç tamir kiti", CraftStation.Vehicle, 6f, M, new[] { I("ScrapMetal", 3), I("DuctTape") }, new[] { I("VehicleRepairKit") }, new[] { "ToolKit" }),
                Recipe("RepairVehicle", "Aracı tamir et", CraftStation.Vehicle, 8f, M, new[] { I("VehicleRepairKit") }, none, null, RecipeEffect.RepairVehicle),
            };
            Log.Add($"Recipes: {list.Count} created/updated in {RecipesDir}.");
            return list.ToArray();
        }

        // ================================================================== database and loot

        private static void UpdateDatabase()
        {
            var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DatabasePath);
            if (db == null) { Log.Add("ERROR ItemDatabase not found"); return; }
            var list = new List<ItemDefinition>(db.items ?? new ItemDefinition[0]);
            int added = 0;
            foreach (var item in Items.Values)
                if (item != null && !list.Contains(item)) { list.Add(item); added++; }
            db.items = list.ToArray();
            EditorUtility.SetDirty(db);
            Log.Add($"ItemDatabase: {added} items appended.");
        }

        private static LootTable Table(string name, int minRolls, int maxRolls)
        {
            string path = $"{LootDir}/{name}.asset";
            var t = AssetDatabase.LoadAssetAtPath<LootTable>(path);
            if (t == null)
            {
                t = ScriptableObject.CreateInstance<LootTable>();
                AssetDatabase.CreateAsset(t, path);
            }
            t.minRolls = minRolls;
            t.maxRolls = maxRolls;
            EditorUtility.SetDirty(t);
            return t;
        }

        private static void AddLoot(LootTable t, params (string key, float weight, int min, int max)[] entries)
        {
            if (t == null) return;
            foreach (var e in entries)
            {
                var item = Items.TryGetValue(e.key, out var it) ? it : Existing(e.key);
                if (item == null || t.entries.Exists(x => x.item == item)) continue;
                t.entries.Add(new LootEntry { item = item, weight = e.weight, minAmount = e.min, maxAmount = e.max });
            }
            EditorUtility.SetDirty(t);
        }

        private static (LootTable house, LootTable gas, LootTable wreck) UpdateLoot()
        {
            var roadside = AssetDatabase.LoadAssetAtPath<LootTable>($"{LootDir}/Loot_Roadside.asset");
            var chest = AssetDatabase.LoadAssetAtPath<LootTable>($"{LootDir}/Loot_Chest.asset");
            var vehicle = AssetDatabase.LoadAssetAtPath<LootTable>($"{LootDir}/Loot_Vehicle.asset");

            AddLoot(roadside, ("Cloth", 3f, 1, 2), ("BrokenFuelCan", 1.5f, 1, 1), ("EmptyFuelCan", 0.8f, 1, 1), ("Firewood", 2f, 1, 2));
            AddLoot(chest, ("Cloth", 4f, 1, 3), ("Raincoat", 1.2f, 1, 1), ("WoolHat", 1.5f, 1, 1), ("Gloves", 1.5f, 1, 1),
                ("FishingRod", 0.8f, 1, 1), ("Salt", 1.5f, 1, 2), ("Alcohol", 1f, 1, 1), ("HerbalOintment", 0.6f, 1, 1), ("EmptyFuelCan", 1.5f, 1, 1));
            AddLoot(vehicle, ("EmptyFuelCan", 2f, 1, 1), ("RubberHose", 1.5f, 1, 1), ("VehicleRepairKit", 0.7f, 1, 1));

            var house = Table("Loot_House", 2, 4);
            AddLoot(house, ("Cloth", 5f, 1, 3), ("CannedBeans", 4f, 1, 2), ("WaterBottle", 3f, 1, 2), ("EmptyBottle", 4f, 1, 2),
                ("Bandage", 3f, 1, 2), ("Medkit", 0.8f, 1, 1), ("WorkJacket", 1.2f, 1, 1), ("WoolHat", 1.5f, 1, 1), ("Gloves", 1.5f, 1, 1),
                ("Raincoat", 1f, 1, 1), ("Knife", 1.2f, 1, 1), ("Salt", 2f, 1, 2), ("Alcohol", 1.5f, 1, 1), ("TeddyBear", 1.5f, 1, 1),
                ("FishingRod", 0.7f, 1, 1), ("DuctTape", 2f, 1, 1), ("Flashlight", 1f, 1, 1), ("RifleAmmo", 1f, 1, 2));
            var gas = Table("Loot_GasStation", 2, 4);
            AddLoot(gas, ("EmptyFuelCan", 4f, 1, 1), ("FuelCan", 1.5f, 1, 1), ("SodaCan", 3f, 1, 2), ("EnergyBar", 3f, 1, 2),
                ("CannedBeans", 2f, 1, 2), ("DuctTape", 2.5f, 1, 2), ("RubberHose", 2f, 1, 1), ("ToolKit", 0.8f, 1, 1),
                ("SparkPlug", 2f, 1, 2), ("CarBattery", 0.8f, 1, 1), ("Flashlight", 1f, 1, 1), ("WaterBottle", 2f, 1, 1));
            var wreck = Table("Loot_Wreck", 1, 2);
            AddLoot(wreck, ("ScrapMetal", 5f, 1, 3), ("RubberHose", 1.5f, 1, 1), ("SparkPlug", 2f, 1, 1), ("CarBattery", 0.5f, 1, 1));

            var pickup = AssetDatabase.LoadAssetAtPath<WorldPickup>("Assets/Prefabs/Loot/DefaultPickup.prefab");
            foreach (var t in new[] { house, gas, wreck })
            {
                if (t.pickupPrefab == null) t.pickupPrefab = pickup;
                EditorUtility.SetDirty(t);
            }
            Log.Add("Loot: roadside/chest/vehicle extended; Loot_House, Loot_GasStation, Loot_Wreck created.");
            return (house, gas, wreck);
        }

        // ================================================================== settings

        private static void CreateSettings(RecipeDefinition[] recipes, (LootTable house, LootTable gas, LootTable wreck) tables)
        {
            var s = AssetDatabase.LoadAssetAtPath<SurvivalSettings>(SettingsPath);
            bool isNew = s == null;
            if (isNew) s = ScriptableObject.CreateInstance<SurvivalSettings>();

            // Items the survival code looks up by name (new ones and existing ones it uses)
            foreach (var key in new[] { "EmptyBottle", "BoiledWater", "WaterBottle", "FuelCan", "RubberHose", "ToolKit", "ScrapMetal", "CarBattery",
                         "Knife", "DriedMeat", "CookedMeat", "DuctTape", "TeddyBear", "Bandage" })
                Existing(key);
            var items = new List<ItemDefinition>();
            foreach (var it in Items.Values) if (it != null && !items.Contains(it)) items.Add(it);
            s.items = items.ToArray();
            s.recipes = recipes;
            s.pickupPrefab = AssetDatabase.LoadAssetAtPath<WorldPickup>("Assets/Prefabs/Loot/DefaultPickup.prefab");
            s.chestPrefab = AssetDatabase.LoadAssetAtPath<LootChest>("Assets/Prefabs/Loot/DefaultChest.prefab");
            s.houseLoot = tables.house;
            s.gasStationLoot = tables.gas;
            s.wreckLoot = tables.wreck;

            // weakest to strongest (DifficultyCurve unlocks the later ones further away)
            var enemies = new List<GameObject>();
            foreach (var p in new[] { "Assets/Prefabs/Enemies/Enemy_Melee_Knife.prefab", "Assets/Prefabs/Enemies/Enemy_Melee_Bat.prefab",
                         "Assets/Prefabs/Enemies/Enemy_Pistol.prefab", "Assets/Prefabs/Enemies/Enemy.prefab" })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null) enemies.Add(go);
            }
            s.humanEnemyPrefabs = enemies.ToArray();

            var stock = new List<ItemDefinition>();
            foreach (var key in new[] { "RifleAmmo", "Bandage", "Medkit", "CannedBeans", "WaterBottle", "EmptyBottle", "FishingRod", "Cloth",
                         "Salt", "DuctTape", "ToolKit", "EmptyFuelCan", "FuelCan", "Knife", "HerbalTea", "Alcohol", "Raincoat", "WoolHat",
                         "Gloves", "EnergyBar", "RubberHose", "VehicleRepairKit" })
            {
                var it = Items.TryGetValue(key, out var x) ? x : Existing(key);
                if (it != null) stock.Add(it);
            }
            s.traderStock = stock.ToArray();

            if (isNew) AssetDatabase.CreateAsset(s, SettingsPath);
            else EditorUtility.SetDirty(s);

            var st = AssetDatabase.LoadAssetAtPath<StatSettings>(StatSettingsPath);
            if (st == null)
            {
                st = ScriptableObject.CreateInstance<StatSettings>();
                st.penalties = StatSettings.DefaultPenalties();
                AssetDatabase.CreateAsset(st, StatSettingsPath);
            }
            else if (st.penalties == null || st.penalties.Length == 0)
            {
                st.penalties = StatSettings.DefaultPenalties();
                EditorUtility.SetDirty(st);
            }
            Log.Add($"Resources: SurvivalSettings ({s.items.Length} items, {recipes.Length} recipes, {enemies.Count} patrol prefabs, {stock.Count} trader goods), StatSettings.");
        }
    }
}
