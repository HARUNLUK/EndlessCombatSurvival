using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessCombat.Combat.Editor
{
    /// <summary>
    /// Simple models made of primitives for every weapon kind (2026-11-16), until the real models come with the new
    /// character. Each kind gets its own outline so it can be told apart in the hand and on the body:
    ///   pistol, SMG, assault rifle, hunting rifle (scope), knife, baseball bat, nailed club,
    ///   wooden round shield, riot shield, torch, rock, grenade.
    /// Weapons: the visual children of the weapon prefabs are replaced by a "Model" child; the root, its components,
    /// Muzzle and LeftHandGrip stay (the SMG / hunting rifle muzzle moves to the new barrel end). The melee models
    /// keep the old long axis and length, so the hand pose and the body spots stay right.
    /// Shield, torch, rock, grenade: prefabs in Assets/Prefabs/Weapons/Simple, set as ItemDefinition.heldModel
    /// (the pickups on the ground keep their old look).
    /// Menu: Survival > Silahlar > Basit Modelleri Kur. Batch: -executeMethod EndlessCombat.Combat.Editor.SimpleWeaponModels.BuildAll
    /// </summary>
    public static class SimpleWeaponModels
    {
        private const string WeaponDir = "Assets/Prefabs/Weapons";
        private const string SimpleDir = "Assets/Prefabs/Weapons/Simple";
        private const string MatDir = "Assets/Prefabs/Weapons/Simple/Materials";

        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        private static readonly System.Text.StringBuilder Report = new System.Text.StringBuilder();

        [MenuItem("Survival/Silahlar/Basit Modelleri Kur")]
        public static void BuildAll()
        {
            Report.Clear();
            Mats.Clear();
            Directory.CreateDirectory(MatDir);

            Firearm("Weapon_Pistol", BuildPistol, null);
            Firearm("Weapon_Smg", BuildSmg, new Vector3(0f, 0.085f, 0.5f));
            Firearm("Weapon_AssaultRifle", BuildAssaultRifle, new Vector3(0f, 0.1f, 0.74f));
            Firearm("Weapon_HuntingRifle", BuildHuntingRifle, new Vector3(0f, 0.1f, 0.84f));
            Melee("Weapon_Knife", BuildKnife);
            Melee("Weapon_BaseballBat", BuildBat);
            Melee("Weapon_WoodenClub", BuildClub);

            Held("Item_WoodenShield", "Held_WoodenShield", BuildWoodenShield);
            Held("Item_RiotShield", "Held_RiotShield", BuildRiotShield);
            Held("Item_Torch", "Held_Torch", BuildTorch);
            Held("Item_Rock", "Held_Rock", BuildRock);
            Held("Item_Grenade", "Held_Grenade", BuildGrenade);
            Report.Append(BowSetup.Build(Mat, SavePrefab)); // bow, arrow, quiver, recipe (2026-11-17)

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Directory.CreateDirectory("PoseShots");
            File.WriteAllText("PoseShots/simple_models_log.txt", Report.ToString());
            Debug.Log("[SimpleWeaponModels]\n" + Report);
        }

        // ------------------------------------------------------------------ weapon prefabs

        private static void Firearm(string prefabName, System.Action<Transform> build, Vector3? muzzle)
        {
            string path = $"{WeaponDir}/{prefabName}.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { Report.AppendLine($"{prefabName}: MISSING"); return; }
            try
            {
                ClearVisuals(root);
                Transform model = NewChild(root.transform, "Model", Vector3.zero, Quaternion.identity);
                build(model);
                if (muzzle.HasValue)
                {
                    Transform m = root.transform.Find("Muzzle");
                    if (m != null) m.localPosition = muzzle.Value;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Report.AppendLine($"{prefabName}: firearm model, {model.childCount} parts");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void Melee(string prefabName, System.Action<Transform, float, float> build)
        {
            string path = $"{WeaponDir}/{prefabName}.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { Report.AppendLine($"{prefabName}: MISSING"); return; }
            try
            {
                // The old model's long axis and its extent along it (from the grip at the root's origin)
                if (!OldAxis(root.transform, out Vector3 axis, out Vector3 side, out float from, out float to))
                {
                    Report.AppendLine($"{prefabName}: no old model to measure, skipped");
                    return;
                }
                ClearVisuals(root);
                Transform model = NewChild(root.transform, "Model", Vector3.zero, Quaternion.LookRotation(side, axis));
                build(model, from, to);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Report.AppendLine($"{prefabName}: melee model along {axis:F2} from {from:0.00} to {to:0.00}, {model.childCount} parts");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>Removes the old visual (root mesh, children with renderers); Muzzle / LeftHandGrip and empty helpers stay.</summary>
        private static void ClearVisuals(GameObject root)
        {
            var mr = root.GetComponent<MeshRenderer>();
            if (mr != null) Object.DestroyImmediate(mr);
            var mf = root.GetComponent<MeshFilter>();
            if (mf != null) Object.DestroyImmediate(mf);
            var doomed = new List<GameObject>();
            foreach (Transform c in root.transform)
            {
                if (c.name == "Muzzle" || c.name == "LeftHandGrip") continue;
                if (c.GetComponentInChildren<Renderer>(true) != null) doomed.Add(c.gameObject);
            }
            foreach (var g in doomed) Object.DestroyImmediate(g);
        }

        /// <summary>Long axis of the old melee model in the root's space, a side direction across it, and the extent.</summary>
        private static bool OldAxis(Transform root, out Vector3 axis, out Vector3 side, out float from, out float to)
        {
            axis = Vector3.up; side = Vector3.forward; from = 0f; to = 0f;
            float best = 0f;
            var ends = new List<Vector3>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.transform == root) continue;
                Transform t = mf.transform;
                Vector3 s = t.localScale;
                Vector3 local; float half;
                bool cylinder = mf.sharedMesh.name.Contains("Cylinder") || mf.sharedMesh.name.Contains("Capsule");
                if (cylinder) { local = Vector3.up; half = s.y; }
                else if (s.z >= s.x && s.z >= s.y) { local = Vector3.forward; half = s.z * 0.5f; }
                else if (s.y >= s.x) { local = Vector3.up; half = s.y * 0.5f; }
                else { local = Vector3.right; half = s.x * 0.5f; }
                Vector3 dir = root.InverseTransformDirection(t.TransformDirection(local)).normalized;
                Vector3 center = root.InverseTransformPoint(t.position);
                ends.Add(center + dir * half);
                ends.Add(center - dir * half);
                if (half > best)
                {
                    best = half;
                    axis = dir;
                    // Side: the widest other axis of the longest part (a knife blade's edge)
                    Vector3 other = local == Vector3.forward ? (s.y >= s.x ? Vector3.up : Vector3.right) : Vector3.forward;
                    side = root.InverseTransformDirection(t.TransformDirection(other)).normalized;
                }
            }
            if (ends.Count == 0) return false;
            // Point the axis away from the grip (root origin)
            float mean = 0f;
            foreach (var e in ends) mean += Vector3.Dot(e, axis);
            if (mean < 0f) axis = -axis;
            from = float.MaxValue; to = float.MinValue;
            foreach (var e in ends)
            {
                float d = Vector3.Dot(e, axis);
                from = Mathf.Min(from, d);
                to = Mathf.Max(to, d);
            }
            side = Vector3.ProjectOnPlane(side, axis);
            if (side.sqrMagnitude < 0.001f) side = Vector3.ProjectOnPlane(Vector3.forward, axis);
            if (side.sqrMagnitude < 0.001f) side = Vector3.ProjectOnPlane(Vector3.right, axis);
            side.Normalize();
            return true;
        }

        // ------------------------------------------------------------------ held model prefabs

        private static GameObject SavePrefab(string prefabName, System.Action<Transform> build)
        {
            var go = new GameObject(prefabName);
            build(go.transform);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{SimpleDir}/{prefabName}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static void Held(string itemName, string prefabName, System.Action<Transform> build)
        {
            var go = new GameObject(prefabName);
            build(go.transform);
            string path = $"{SimpleDir}/{prefabName}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"Assets/Data/Items/{itemName}.asset");
            if (item != null && prefab != null)
            {
                item.heldModel = prefab;
                EditorUtility.SetDirty(item);
                Report.AppendLine($"{itemName}: heldModel = {path}");
            }
            else Report.AppendLine($"{itemName}: item or prefab MISSING");
        }

        // ------------------------------------------------------------------ firearms (+Z muzzle, +Y up, origin = grip hand)

        private static void BuildPistol(Transform m)
        {
            Box(m, "Slide", new Vector3(0f, 0.056f, 0.075f), Vector3.zero, new Vector3(0.03f, 0.034f, 0.215f), "GunMetal");
            Box(m, "Frame", new Vector3(0f, 0.03f, 0.09f), Vector3.zero, new Vector3(0.027f, 0.02f, 0.17f), "Polymer");
            Box(m, "Grip", new Vector3(0f, -0.018f, -0.006f), new Vector3(-14f, 0f, 0f), new Vector3(0.03f, 0.1f, 0.044f), "Rubber");
            Box(m, "TriggerGuard", new Vector3(0f, 0.004f, 0.05f), Vector3.zero, new Vector3(0.008f, 0.006f, 0.05f), "Polymer");
            Box(m, "Trigger", new Vector3(0f, 0.012f, 0.035f), new Vector3(15f, 0f, 0f), new Vector3(0.005f, 0.02f, 0.005f), "Steel");
            Cyl(m, "Barrel", new Vector3(0f, 0.056f, 0.18f), Vector3.forward, 0.007f, 0.012f, "Steel");
            Box(m, "FrontSight", new Vector3(0f, 0.077f, 0.17f), Vector3.zero, new Vector3(0.005f, 0.008f, 0.006f), "Steel");
            Box(m, "RearSight", new Vector3(0f, 0.077f, -0.025f), Vector3.zero, new Vector3(0.022f, 0.008f, 0.008f), "Steel");
        }

        private static void BuildSmg(Transform m)
        {
            Box(m, "Receiver", new Vector3(0f, 0.075f, 0.15f), Vector3.zero, new Vector3(0.045f, 0.075f, 0.32f), "GunMetal");
            Cyl(m, "Shroud", new Vector3(0f, 0.085f, 0.36f), Vector3.forward, 0.021f, 0.1f, "Polymer");
            Cyl(m, "Barrel", new Vector3(0f, 0.085f, 0.45f), Vector3.forward, 0.009f, 0.1f, "Steel");
            Box(m, "Magazine", new Vector3(0f, -0.045f, 0.2f), new Vector3(6f, 0f, 0f), new Vector3(0.028f, 0.17f, 0.04f), "Steel");
            Box(m, "Grip", new Vector3(0f, -0.01f, 0.05f), new Vector3(-16f, 0f, 0f), new Vector3(0.032f, 0.1f, 0.042f), "Rubber");
            Box(m, "TriggerGuard", new Vector3(0f, 0.022f, 0.1f), Vector3.zero, new Vector3(0.008f, 0.006f, 0.05f), "Polymer");
            Box(m, "Rail", new Vector3(0f, 0.118f, 0.15f), Vector3.zero, new Vector3(0.022f, 0.012f, 0.26f), "Steel");
            Box(m, "Cocking", new Vector3(-0.028f, 0.085f, 0.25f), Vector3.zero, new Vector3(0.012f, 0.01f, 0.02f), "Steel");
            // Wire stock
            Box(m, "StockTop", new Vector3(0f, 0.09f, -0.12f), Vector3.zero, new Vector3(0.008f, 0.008f, 0.24f), "Steel");
            Box(m, "StockLow", new Vector3(0f, 0.03f, -0.11f), new Vector3(-8f, 0f, 0f), new Vector3(0.008f, 0.008f, 0.22f), "Steel");
            Box(m, "ButtPlate", new Vector3(0f, 0.055f, -0.245f), Vector3.zero, new Vector3(0.04f, 0.09f, 0.014f), "Rubber");
        }

        private static void BuildAssaultRifle(Transform m)
        {
            Box(m, "Receiver", new Vector3(0f, 0.08f, 0.16f), Vector3.zero, new Vector3(0.05f, 0.085f, 0.36f), "GunMetal");
            Box(m, "Handguard", new Vector3(0f, 0.088f, 0.43f), Vector3.zero, new Vector3(0.058f, 0.065f, 0.2f), "Polymer");
            Cyl(m, "Barrel", new Vector3(0f, 0.1f, 0.635f), Vector3.forward, 0.012f, 0.21f, "Steel");
            Cyl(m, "MuzzleBrake", new Vector3(0f, 0.1f, 0.72f), Vector3.forward, 0.017f, 0.04f, "GunMetal");
            Box(m, "FrontSight", new Vector3(0f, 0.15f, 0.56f), Vector3.zero, new Vector3(0.012f, 0.06f, 0.016f), "GunMetal");
            Box(m, "CarryHandle", new Vector3(0f, 0.15f, 0.13f), Vector3.zero, new Vector3(0.02f, 0.03f, 0.2f), "GunMetal");
            // Curved magazine: two angled boxes
            Box(m, "MagTop", new Vector3(0f, -0.02f, 0.28f), new Vector3(10f, 0f, 0f), new Vector3(0.032f, 0.1f, 0.058f), "Steel");
            Box(m, "MagLow", new Vector3(0f, -0.105f, 0.3f), new Vector3(26f, 0f, 0f), new Vector3(0.032f, 0.09f, 0.056f), "Steel");
            Box(m, "Grip", new Vector3(0f, -0.01f, 0.1f), new Vector3(-18f, 0f, 0f), new Vector3(0.034f, 0.11f, 0.044f), "Rubber");
            Box(m, "TriggerGuard", new Vector3(0f, 0.025f, 0.16f), Vector3.zero, new Vector3(0.008f, 0.006f, 0.06f), "Polymer");
            Box(m, "Stock", new Vector3(0f, 0.068f, -0.15f), new Vector3(-4f, 0f, 0f), new Vector3(0.042f, 0.075f, 0.28f), "Polymer");
            Box(m, "ButtPad", new Vector3(0f, 0.06f, -0.3f), Vector3.zero, new Vector3(0.046f, 0.11f, 0.025f), "Rubber");
        }

        private static void BuildHuntingRifle(Transform m)
        {
            // Wooden stock: butt, wrist (the right hand), long forend
            Box(m, "Butt", new Vector3(0f, 0.035f, -0.32f), new Vector3(-6f, 0f, 0f), new Vector3(0.045f, 0.13f, 0.22f), "Wood");
            Box(m, "ButtPad", new Vector3(0f, 0.03f, -0.435f), new Vector3(-6f, 0f, 0f), new Vector3(0.047f, 0.135f, 0.015f), "Rubber");
            Box(m, "Wrist", new Vector3(0f, 0.055f, -0.1f), new Vector3(8f, 0f, 0f), new Vector3(0.038f, 0.06f, 0.24f), "Wood");
            Box(m, "Grip", new Vector3(0f, 0.01f, 0.0f), new Vector3(-25f, 0f, 0f), new Vector3(0.034f, 0.08f, 0.04f), "Wood");
            Box(m, "Forend", new Vector3(0f, 0.07f, 0.31f), Vector3.zero, new Vector3(0.044f, 0.05f, 0.5f), "Wood");
            Box(m, "Receiver", new Vector3(0f, 0.098f, 0.08f), Vector3.zero, new Vector3(0.036f, 0.04f, 0.24f), "GunMetal");
            Cyl(m, "Barrel", new Vector3(0f, 0.1f, 0.5f), Vector3.forward, 0.011f, 0.68f, "GunMetal");
            Box(m, "TriggerGuard", new Vector3(0f, 0.035f, 0.05f), Vector3.zero, new Vector3(0.008f, 0.006f, 0.07f), "Steel");
            // Bolt handle on the right
            Cyl(m, "Bolt", new Vector3(0.035f, 0.1f, 0.0f), Vector3.right, 0.005f, 0.05f, "Steel");
            Sphere(m, "BoltKnob", new Vector3(0.06f, 0.1f, 0.0f), new Vector3(0.016f, 0.016f, 0.016f), "Steel");
            // Scope
            Cyl(m, "ScopeTube", new Vector3(0f, 0.165f, 0.09f), Vector3.forward, 0.017f, 0.3f, "GunMetal");
            Cyl(m, "ScopeFront", new Vector3(0f, 0.165f, 0.24f), Vector3.forward, 0.025f, 0.06f, "GunMetal");
            Cyl(m, "ScopeBack", new Vector3(0f, 0.165f, -0.05f), Vector3.forward, 0.021f, 0.05f, "GunMetal");
            Cyl(m, "Lens", new Vector3(0f, 0.165f, 0.272f), Vector3.forward, 0.021f, 0.004f, "Lens");
            Box(m, "MountA", new Vector3(0f, 0.13f, 0.02f), Vector3.zero, new Vector3(0.02f, 0.03f, 0.02f), "Steel");
            Box(m, "MountB", new Vector3(0f, 0.13f, 0.16f), Vector3.zero, new Vector3(0.02f, 0.03f, 0.02f), "Steel");
        }

        // ------------------------------------------------------------------ melee (+Y = long axis from the grip, +Z = side / edge)

        private static void BuildKnife(Transform m, float from, float to)
        {
            float len = to - from;
            float handle = Mathf.Clamp(len * 0.38f, 0.09f, 0.13f);
            float h0 = from, h1 = from + handle;
            Box(m, "Handle", new Vector3(0f, (h0 + h1) * 0.5f, 0f), Vector3.zero, new Vector3(0.022f, handle, 0.03f), "Rubber");
            Box(m, "Pommel", new Vector3(0f, h0 - 0.006f, 0f), Vector3.zero, new Vector3(0.026f, 0.012f, 0.034f), "Steel");
            Box(m, "Guard", new Vector3(0f, h1 + 0.005f, 0f), Vector3.zero, new Vector3(0.03f, 0.01f, 0.06f), "Steel");
            float b0 = h1 + 0.01f, tip = to;
            float body = (tip - b0) * 0.78f;
            Box(m, "Blade", new Vector3(0f, b0 + body * 0.5f, 0.004f), Vector3.zero, new Vector3(0.005f, body, 0.034f), "Blade");
            // Tip: a slanted box cut along the edge
            float tipLen = (tip - b0) - body;
            Box(m, "Tip", new Vector3(0f, b0 + body + tipLen * 0.4f, 0.012f), Vector3.zero, new Vector3(0.005f, tipLen * 0.8f, 0.016f), "Blade");
            Box(m, "Spine", new Vector3(0f, b0 + body * 0.5f, 0.02f), Vector3.zero, new Vector3(0.007f, body, 0.004f), "Steel");
        }

        private static void BuildBat(Transform m, float from, float to)
        {
            float len = to - from;
            Sphere(m, "Knob", new Vector3(0f, from + 0.005f, 0f), new Vector3(0.05f, 0.022f, 0.05f), "Wood");
            float[] cut = { 0f, 0.3f, 0.5f, 0.68f, 1f };
            float[] radius = { 0.017f, 0.021f, 0.028f, 0.035f };
            for (int i = 0; i < 4; i++)
            {
                float a = from + len * cut[i], b = from + len * cut[i + 1];
                Cyl(m, "Body" + i, new Vector3(0f, (a + b) * 0.5f, 0f), Vector3.up, radius[i], b - a, "Wood");
            }
            Box(m, "Tape", new Vector3(0f, from + len * 0.14f, 0f), Vector3.zero, new Vector3(0.037f, len * 0.22f, 0.037f), "Rubber");
            Sphere(m, "End", new Vector3(0f, to - 0.005f, 0f), new Vector3(0.07f, 0.03f, 0.07f), "Wood");
        }

        private static void BuildClub(Transform m, float from, float to)
        {
            float len = to - from;
            float headFrom = from + len * 0.55f;
            Cyl(m, "Handle", new Vector3(0f, (from + headFrom) * 0.5f, 0f), Vector3.up, 0.02f, headFrom - from, "WoodDark");
            Box(m, "Wrap", new Vector3(0f, from + len * 0.16f, 0f), new Vector3(0f, 25f, 0f), new Vector3(0.043f, len * 0.2f, 0.043f), "Cloth");
            Cyl(m, "Head", new Vector3(0f, (headFrom + to) * 0.5f, 0f), Vector3.up, 0.042f, to - headFrom, "WoodDark");
            Sphere(m, "HeadEnd", new Vector3(0f, to - 0.01f, 0f), new Vector3(0.084f, 0.04f, 0.084f), "WoodDark");
            // Nails sticking out of the head
            for (int i = 0; i < 6; i++)
            {
                float ang = i * 60f + (i % 2) * 25f;
                float y = Mathf.Lerp(headFrom + 0.04f, to - 0.04f, (i % 3) / 2f);
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                Cyl(m, "Nail" + i, new Vector3(0f, y, 0f) + dir * 0.05f, dir, 0.004f, 0.04f, "Steel");
            }
        }

        // ------------------------------------------------------------------ shields (+Z = the front, facing away from the player)

        private static void BuildWoodenShield(Transform m)
        {
            Cyl(m, "Board", Vector3.zero, Vector3.forward, 0.3f, 0.03f, "Wood");
            for (int i = -2; i <= 2; i++)
            {
                if (i == 0) continue;
                float x = i * 0.11f;
                float h = 2f * Mathf.Sqrt(Mathf.Max(0f, 0.3f * 0.3f - x * x)) - 0.02f;
                Box(m, "Groove" + i, new Vector3(x, 0f, 0.016f), Vector3.zero, new Vector3(0.008f, h, 0.004f), "WoodDark");
            }
            Cyl(m, "Rim", Vector3.zero, Vector3.forward, 0.315f, 0.02f, "Steel"); // thinner than the board: no flicker on the faces
            Sphere(m, "Boss", new Vector3(0f, 0f, 0.02f), new Vector3(0.13f, 0.13f, 0.06f), "Steel");
            Box(m, "BandH", new Vector3(0f, 0.17f, 0.017f), Vector3.zero, new Vector3(0.5f, 0.03f, 0.004f), "Steel");
            Box(m, "BandL", new Vector3(0f, -0.17f, 0.017f), Vector3.zero, new Vector3(0.5f, 0.03f, 0.004f), "Steel");
            Box(m, "Handle", new Vector3(0f, 0f, -0.035f), Vector3.zero, new Vector3(0.12f, 0.025f, 0.02f), "Cloth");
        }

        private static void BuildRiotShield(Transform m)
        {
            Box(m, "Body", Vector3.zero, Vector3.zero, new Vector3(0.55f, 1.0f, 0.022f), "RiotBody");
            Box(m, "Window", new Vector3(0f, 0.3f, 0.012f), Vector3.zero, new Vector3(0.4f, 0.2f, 0.004f), "Lens");
            Box(m, "Band", new Vector3(0f, 0.05f, 0.013f), Vector3.zero, new Vector3(0.55f, 0.06f, 0.004f), "Stripe");
            Box(m, "EdgeL", new Vector3(-0.28f, 0f, 0f), Vector3.zero, new Vector3(0.02f, 1.0f, 0.03f), "Rubber");
            Box(m, "EdgeR", new Vector3(0.28f, 0f, 0f), Vector3.zero, new Vector3(0.02f, 1.0f, 0.03f), "Rubber");
            Box(m, "EdgeT", new Vector3(0f, 0.505f, 0f), Vector3.zero, new Vector3(0.58f, 0.02f, 0.03f), "Rubber");
            Box(m, "EdgeB", new Vector3(0f, -0.505f, 0f), Vector3.zero, new Vector3(0.58f, 0.02f, 0.03f), "Rubber");
            Box(m, "Handle", new Vector3(0f, -0.02f, -0.04f), Vector3.zero, new Vector3(0.03f, 0.14f, 0.03f), "Rubber");
        }

        // ------------------------------------------------------------------ torch (+Y = up, origin = the hand), throwables

        private static void BuildTorch(Transform m)
        {
            Cyl(m, "Stick", new Vector3(0f, 0.12f, 0f), Vector3.up, 0.017f, 0.55f, "WoodDark");
            Cyl(m, "Wrap", new Vector3(0f, 0.38f, 0f), Vector3.up, 0.034f, 0.12f, "Cloth");
            Cyl(m, "Char", new Vector3(0f, 0.445f, 0f), Vector3.up, 0.03f, 0.02f, "Charred");
            Box(m, "Tie", new Vector3(0f, 0.31f, 0f), new Vector3(0f, 30f, 0f), new Vector3(0.072f, 0.012f, 0.072f), "Rope");
        }

        private static void BuildRock(Transform m)
        {
            Sphere(m, "A", Vector3.zero, new Vector3(0.09f, 0.075f, 0.08f), "Rock");
            Sphere(m, "B", new Vector3(0.022f, 0.014f, 0.01f), new Vector3(0.06f, 0.05f, 0.06f), "Rock");
            Sphere(m, "C", new Vector3(-0.018f, -0.01f, 0.018f), new Vector3(0.055f, 0.045f, 0.05f), "RockDark");
        }

        private static void BuildGrenade(Transform m)
        {
            Sphere(m, "Body", Vector3.zero, new Vector3(0.062f, 0.078f, 0.062f), "Olive");
            Cyl(m, "Fuse", new Vector3(0f, 0.045f, 0f), Vector3.up, 0.013f, 0.025f, "Steel");
            Box(m, "Spoon", new Vector3(0.016f, 0.02f, 0f), new Vector3(0f, 0f, -12f), new Vector3(0.008f, 0.07f, 0.016f), "Steel");
            Cyl(m, "Ring", new Vector3(-0.022f, 0.055f, 0f), Vector3.forward, 0.012f, 0.003f, "Steel");
        }

        // ------------------------------------------------------------------ parts

        private static Transform NewChild(Transform parent, string name, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            return go.transform;
        }

        private static GameObject Prim(Transform parent, PrimitiveType type, string name, string mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(mat);
            return go;
        }

        private static void Box(Transform p, string name, Vector3 pos, Vector3 euler, Vector3 size, string mat)
        {
            var go = Prim(p, PrimitiveType.Cube, name, mat);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
        }

        private static void Sphere(Transform p, string name, Vector3 pos, Vector3 size, string mat)
        {
            var go = Prim(p, PrimitiveType.Sphere, name, mat);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
        }

        /// <summary>A cylinder of the given radius and length along a direction (Unity's cylinder is 2 high, 1 wide).</summary>
        private static void Cyl(Transform p, string name, Vector3 pos, Vector3 dir, float radius, float length, string mat)
        {
            var go = Prim(p, PrimitiveType.Cylinder, name, mat);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
        }

        private static Material Mat(string key)
        {
            if (Mats.TryGetValue(key, out Material cached)) return cached;
            Color c; float metal = 0f, smooth = 0.3f;
            switch (key)
            {
                case "GunMetal": c = new Color(0.16f, 0.17f, 0.19f); metal = 0.7f; smooth = 0.45f; break;
                case "Steel": c = new Color(0.45f, 0.46f, 0.48f); metal = 0.85f; smooth = 0.55f; break;
                case "Blade": c = new Color(0.72f, 0.74f, 0.76f); metal = 0.9f; smooth = 0.75f; break;
                case "Polymer": c = new Color(0.1f, 0.1f, 0.1f); smooth = 0.25f; break;
                case "Rubber": c = new Color(0.06f, 0.06f, 0.06f); smooth = 0.1f; break;
                case "Wood": c = new Color(0.55f, 0.33f, 0.16f); smooth = 0.35f; break;
                case "WoodDark": c = new Color(0.36f, 0.22f, 0.11f); smooth = 0.2f; break;
                case "Cloth": c = new Color(0.55f, 0.48f, 0.36f); smooth = 0.05f; break;
                case "Rope": c = new Color(0.62f, 0.52f, 0.33f); smooth = 0.05f; break;
                case "Charred": c = new Color(0.08f, 0.06f, 0.05f); smooth = 0.05f; break;
                case "Lens": c = new Color(0.25f, 0.42f, 0.55f); metal = 0.2f; smooth = 0.95f; break;
                case "RiotBody": c = new Color(0.17f, 0.2f, 0.25f); smooth = 0.5f; break;
                case "Stripe": c = new Color(0.85f, 0.85f, 0.82f); smooth = 0.4f; break;
                case "Rock": c = new Color(0.5f, 0.5f, 0.47f); smooth = 0.15f; break;
                case "RockDark": c = new Color(0.38f, 0.38f, 0.36f); smooth = 0.1f; break;
                case "Olive": c = new Color(0.27f, 0.33f, 0.19f); smooth = 0.3f; break;
                case "Leather": c = new Color(0.4f, 0.26f, 0.15f); smooth = 0.3f; break;
                case "Feather": c = new Color(0.85f, 0.82f, 0.75f); smooth = 0.1f; break;
                case "FeatherDark": c = new Color(0.55f, 0.16f, 0.12f); smooth = 0.1f; break;
                default: c = Color.magenta; break;
            }
            string path = $"{MatDir}/Mat_Simple_{key}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(s);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", c);
            mat.SetColor("_Color", c);
            mat.SetFloat("_Metallic", metal);
            mat.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(mat);
            Mats[key] = mat;
            return mat;
        }
    }
}
