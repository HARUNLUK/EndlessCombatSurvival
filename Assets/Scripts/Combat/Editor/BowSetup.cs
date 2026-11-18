using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.Survival;

namespace EndlessCombat.Combat.Editor
{
    /// <summary>
    /// The bow (Combat_Action_Plan.md 8, 2026-11-17): Item_Bow (bow slot), Item_Arrow (bag item, one per shot),
    /// the recipe "Ok" (1 firewood + 1 rock = 3 arrows, by hand) and simple models:
    ///   Held_Bow     grip, two limbs, a string (child "String", moved back by PlayerBow while pulling)
    ///   Held_Arrow   children "ArrowModel" (shaft, stone head, fletching) and "QuiverModel" (quiver with arrows)
    /// The items and the recipe are added to SurvivalSettings. Run from SimpleWeaponModels.BuildAll
    /// (menu Survival > Silahlar > Basit Modelleri Kur).
    /// </summary>
    public static class BowSetup
    {
        private const string ItemsDir = "Assets/Data/Items";
        private const string RecipesDir = "Assets/Data/Recipes";

        public static string Build(System.Func<string, Material> mat, System.Func<string, System.Action<Transform>, GameObject> savePrefab)
        {
            var log = new System.Text.StringBuilder();

            GameObject bowModel = savePrefab("Held_Bow", t => BuildBow(t, mat));
            GameObject arrowModel = savePrefab("Held_Arrow", t => BuildArrowAndQuiver(t, mat));

            ItemDefinition bow = Item("Item_Bow", i =>
            {
                i.displayName = "Av Yayı";
                i.description = "Ağaçtan av yayı. Sırtta, okluğuyla taşınır; iki elle tutulur. LT nişan, RT basılı ger, bırak at. Sessizdir.";
                i.category = ItemCategory.Apparel;
                i.equipSlot = EquipSlot.Bow;
                i.armor = 0f;
                i.weight = 1.1f;
                i.value = 45;
                i.worldColor = new Color(0.5f, 0.32f, 0.16f);
                i.worldScale = new Vector3(0.08f, 1.1f, 0.08f);
                i.heldModel = bowModel;
            });
            ItemDefinition arrow = Item("Item_Arrow", i =>
            {
                i.displayName = "Ok";
                i.description = "Taş uçlu ok. Yayla atılır; bir kısmı yerden ya da cesetten geri alınabilir.";
                i.category = ItemCategory.Material;
                i.weight = 0.05f;
                i.value = 1;
                i.worldColor = new Color(0.62f, 0.5f, 0.32f);
                i.worldScale = new Vector3(0.03f, 0.03f, 0.7f);
                i.heldModel = arrowModel;
            });

            var settings = AssetDatabase.LoadAssetAtPath<SurvivalSettings>("Assets/Resources/SurvivalSettings.asset");
            if (settings == null)
                foreach (var guid in AssetDatabase.FindAssets("t:SurvivalSettings"))
                    settings = AssetDatabase.LoadAssetAtPath<SurvivalSettings>(AssetDatabase.GUIDToAssetPath(guid));

            RecipeDefinition recipe = null;
            if (settings != null)
            {
                ItemDefinition wood = settings.Item("Item_Firewood");
                ItemDefinition rock = settings.Item("Item_Rock");
                recipe = Recipe("Recipe_Arrow", r =>
                {
                    r.displayName = "Ok (3 adet)";
                    r.description = "Bir dal ve bir taştan üç taş uçlu ok.";
                    r.station = CraftStation.Hand;
                    r.craftSeconds = 2.5f;
                    r.trainsStat = StatType.Mechanics;
                    r.inputs = new[] { new RecipeIngredient(wood, 1), new RecipeIngredient(rock, 1) };
                    r.outputs = new[] { new RecipeIngredient(arrow, 3) };
                    r.tools = new ItemDefinition[0];
                });

                var items = new List<ItemDefinition>(settings.items ?? new ItemDefinition[0]);
                if (!items.Contains(bow)) items.Add(bow);
                if (!items.Contains(arrow)) items.Add(arrow);
                settings.items = items.ToArray();
                var recipes = new List<RecipeDefinition>(settings.recipes ?? new RecipeDefinition[0]);
                if (recipe != null && !recipes.Contains(recipe)) recipes.Add(recipe);
                settings.recipes = recipes.ToArray();
                EditorUtility.SetDirty(settings);
                log.AppendLine($"SurvivalSettings: bow, arrow, recipe registered (wood={(wood != null)} rock={(rock != null)})");
            }
            else log.AppendLine("SurvivalSettings MISSING: bow items not registered");

            var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>("Assets/Data/ItemDatabase.asset");
            if (db != null)
            {
                var list = new List<ItemDefinition>(db.items ?? new ItemDefinition[0]);
                if (!list.Contains(bow)) list.Add(bow);
                if (!list.Contains(arrow)) list.Add(arrow);
                db.items = list.ToArray();
                EditorUtility.SetDirty(db);
            }
            log.AppendLine("Item_Bow, Item_Arrow, Recipe_Arrow written");
            return log.ToString();
        }

        private static ItemDefinition Item(string name, System.Action<ItemDefinition> set)
        {
            string path = $"{ItemsDir}/{name}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            bool isNew = item == null;
            if (isNew) item = ScriptableObject.CreateInstance<ItemDefinition>();
            set(item);
            if (isNew) AssetDatabase.CreateAsset(item, path);
            else EditorUtility.SetDirty(item);
            return item;
        }

        private static RecipeDefinition Recipe(string name, System.Action<RecipeDefinition> set)
        {
            System.IO.Directory.CreateDirectory(RecipesDir);
            string path = $"{RecipesDir}/{name}.asset";
            var r = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
            bool isNew = r == null;
            if (isNew) r = ScriptableObject.CreateInstance<RecipeDefinition>();
            set(r);
            if (isNew) AssetDatabase.CreateAsset(r, path);
            else EditorUtility.SetDirty(r);
            return r;
        }

        // ------------------------------------------------------------------ models (+Z = where the arrow flies, +Y up)

        private static void BuildBow(Transform m, System.Func<string, Material> mat)
        {
            Part(m, PrimitiveType.Cube, "Grip", new Vector3(0f, 0f, 0f), Vector3.zero, new Vector3(0.035f, 0.13f, 0.04f), mat("Rubber"));
            // Each limb: three short boxes bending back toward the archer (-Z) at the tips
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 a = new Vector3(0f, 0.065f * s, 0f);
                float[] len = { 0.2f, 0.2f, 0.2f };
                float[] ang = { 8f, 18f, 30f };
                for (int k = 0; k < 3; k++)
                {
                    Vector3 dir = new Vector3(0f, s * Mathf.Cos(ang[k] * Mathf.Deg2Rad), -Mathf.Sin(ang[k] * Mathf.Deg2Rad));
                    Vector3 b = a + dir * len[k];
                    float w = Mathf.Lerp(0.03f, 0.018f, k / 2f);
                    var go = Part(m, PrimitiveType.Cube, $"Limb{(s > 0 ? "Up" : "Low")}{k}", (a + b) * 0.5f, Vector3.zero,
                        new Vector3(0.022f, len[k] + 0.01f, w), mat(k == 0 ? "Wood" : "WoodDark"));
                    go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                    a = b;
                }
                Part(m, PrimitiveType.Sphere, s > 0 ? "TipUp" : "TipLow", a, Vector3.zero, new Vector3(0.025f, 0.025f, 0.025f), mat("Steel"));
            }
            // Tips end near y = +-0.63, z = -0.13: the string runs between them
            var str = Part(m, PrimitiveType.Cube, "String", new Vector3(0f, 0f, -0.19f), Vector3.zero, new Vector3(0.004f, 1.24f, 0.004f), mat("Rope"));
            Part(m, PrimitiveType.Cube, "ArrowRest", new Vector3(-0.02f, 0.07f, 0.0f), Vector3.zero, new Vector3(0.012f, 0.012f, 0.03f), mat("Steel"));
        }

        private static void BuildArrowAndQuiver(Transform root, System.Func<string, Material> mat)
        {
            var arrow = new GameObject("ArrowModel").transform;
            arrow.SetParent(root, false);
            Arrow(arrow, Vector3.zero, mat);

            var quiver = new GameObject("QuiverModel").transform;
            quiver.SetParent(root, false);
            quiver.localPosition = new Vector3(0.4f, 0f, 0f);
            var body = Part(quiver, PrimitiveType.Cylinder, "Body", Vector3.zero, Vector3.zero, new Vector3(0.09f, 0.23f, 0.09f), mat("Leather"));
            Part(quiver, PrimitiveType.Cylinder, "Rim", new Vector3(0f, 0.22f, 0f), Vector3.zero, new Vector3(0.1f, 0.015f, 0.1f), mat("WoodDark"));
            Part(quiver, PrimitiveType.Cube, "Strap", new Vector3(0f, 0f, 0.048f), Vector3.zero, new Vector3(0.03f, 0.44f, 0.008f), mat("Rope"));
            // Arrow tails sticking out of the top
            for (int i = 0; i < 5; i++)
            {
                float ang = i * 72f * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Cos(ang) * 0.022f, 0.3f + (i % 2) * 0.02f, Mathf.Sin(ang) * 0.022f);
                Part(quiver, PrimitiveType.Cylinder, "Shaft" + i, p, Vector3.zero, new Vector3(0.008f, 0.08f, 0.008f), mat("Wood"));
                Part(quiver, PrimitiveType.Cube, "Feather" + i, p + Vector3.up * 0.06f, new Vector3(0f, i * 72f, 0f), new Vector3(0.002f, 0.06f, 0.02f), mat(i % 2 == 0 ? "Feather" : "FeatherDark"));
            }
        }

        /// <summary>An arrow along +Z, its centre at the origin, 0.75 m long.</summary>
        private static void Arrow(Transform t, Vector3 c, System.Func<string, Material> mat)
        {
            var shaft = Part(t, PrimitiveType.Cylinder, "Shaft", c, new Vector3(90f, 0f, 0f), new Vector3(0.009f, 0.35f, 0.009f), mat("Wood"));
            Part(t, PrimitiveType.Cube, "Head", c + new Vector3(0f, 0f, 0.37f), new Vector3(0f, 0f, 45f), new Vector3(0.016f, 0.016f, 0.05f), mat("RockDark"));
            Part(t, PrimitiveType.Cube, "FeatherA", c + new Vector3(0f, 0f, -0.31f), Vector3.zero, new Vector3(0.002f, 0.03f, 0.09f), mat("Feather"));
            Part(t, PrimitiveType.Cube, "FeatherB", c + new Vector3(0f, 0f, -0.31f), new Vector3(0f, 0f, 90f), new Vector3(0.002f, 0.03f, 0.09f), mat("FeatherDark"));
            Part(t, PrimitiveType.Cube, "Nock", c + new Vector3(0f, 0f, -0.37f), Vector3.zero, new Vector3(0.012f, 0.012f, 0.012f), mat("Rope"));
        }

        private static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }
    }
}
