using UnityEditor;
using UnityEngine;

namespace EndlessSurvival.Survival.Editor
{
    /// <summary>
    /// Binoculars (Dürbün) setup. The item, loot table entries and trader stock live in the Package 2-5 setup
    /// (so a re-run of that keeps them); this menu builds the simple two-cylinder model and runs it.
    /// Menu: Endless Survival > Setup Binoculars.
    /// Batch: -executeMethod EndlessSurvival.Survival.Editor.BinocularsSetup.RunBatch
    /// The HUD (mask, marks) is drawn by BinocularsController at runtime: no scene or UI asset is needed.
    /// </summary>
    public static class BinocularsSetup
    {
        private const string ModelPath = "Assets/Prefabs/Loot/Binoculars_Model.prefab";
        private const string MaterialDir = "Assets/Materials";

        [MenuItem("Endless Survival/Setup Binoculars")]
        public static void RunMenu()
        {
            var log = SurvivalPackagesSetup.RunAndLog();
            log.Insert(0, "Binoculars: model " + ModelPath + ", item Item_Binoculars, loot + trader.");
            EditorUtility.DisplayDialog("Setup Binoculars", string.Join("\n", log), "OK");
        }

        public static void RunBatch()
        {
            var log = SurvivalPackagesSetup.RunAndLog();
            foreach (var line in log) Debug.Log("[BinocularsSetup] " + line);
            var item = AssetDatabase.LoadAssetAtPath<EndlessSurvival.Inventory.ItemDefinition>("Assets/Data/Items/Item_Binoculars.asset");
            Debug.Log("[BinocularsSetup] item=" + (item != null ? item.DisplayName : "MISSING") + " model=" + (item != null && item.worldModel != null));
        }

        /// <summary>Builds (or rebuilds) the placeholder binoculars prefab: two dark tubes on a bridge.</summary>
        public static GameObject EnsureModel()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Loot")) AssetDatabase.CreateFolder("Assets/Prefabs", "Loot");
            if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder("Assets", "Materials");
            var body = Mat("Binoculars_Body", new Color(0.13f, 0.14f, 0.13f), 0.35f);
            var lens = Mat("Binoculars_Lens", new Color(0.25f, 0.4f, 0.55f), 0.9f);

            var root = new GameObject("Binoculars_Model");
            try
            {
                // Bridge first: WorldPickup measures the first mesh to decide if the model lies flat
                Part(PrimitiveType.Cube, root.transform, "Bridge", new Vector3(0f, 0f, 0.01f), Vector3.zero, new Vector3(0.05f, 0.025f, 0.07f), body);
                foreach (float side in new[] { -1f, 1f })
                {
                    string s = side < 0 ? "L" : "R";
                    // Cylinder mesh is 2 units tall along Y: rotate onto Z, scale y = half length
                    Part(PrimitiveType.Cylinder, root.transform, "Tube" + s, new Vector3(0.045f * side, 0f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.05f, 0.07f, 0.05f), body);
                    Part(PrimitiveType.Cylinder, root.transform, "Objective" + s, new Vector3(0.045f * side, 0f, 0.075f), new Vector3(90f, 0f, 0f), new Vector3(0.056f, 0.012f, 0.056f), body);
                    Part(PrimitiveType.Cylinder, root.transform, "Lens" + s, new Vector3(0.045f * side, 0f, 0.088f), new Vector3(90f, 0f, 0f), new Vector3(0.042f, 0.002f, 0.042f), lens);
                    Part(PrimitiveType.Cylinder, root.transform, "Eyepiece" + s, new Vector3(0.045f * side, 0f, -0.08f), new Vector3(90f, 0f, 0f), new Vector3(0.032f, 0.015f, 0.032f), body);
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, ModelPath);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Part(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
        }

        private static Material Mat(string name, Color color, float smoothness)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
