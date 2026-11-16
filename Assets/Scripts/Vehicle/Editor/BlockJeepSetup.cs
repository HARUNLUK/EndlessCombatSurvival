using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EndlessSurvival.Vehicle.Editor
{
    /// <summary>
    /// Replaces the art of the drivable vehicle (Pack_Adventure pickup model) with a jeep made of plain cubes and
    /// cylinders, as a stand-in until the project gets its own vehicle asset (2026-11-12).
    /// Only the looks change: the root, the wheel colliders, the wheel mesh transforms the controller turns, the
    /// camera target, the exit point and every gameplay component stay as they are. The blocks are sized from the
    /// old model's bounds, so the vehicle keeps its footprint. Mesh colliders of the old model become boxes.
    /// Swapping in a real model later: delete the "BlockJeep" child and the "WheelBlock" children, put the model in.
    /// Menu: Endless Survival/Setup Block Jeep. Batch: -executeMethod EndlessSurvival.Vehicle.Editor.BlockJeepSetup.RunBatch
    /// Runs by itself once after a compile while the prefab still has no "BlockJeep" child.
    /// </summary>
    public static class BlockJeepSetup
    {
        private const string PrefabPath = "Assets/Prefabs/Vehicle_Pickup.prefab";
        private const string MaterialDir = "Assets/Materials/Vehicle";
        private const string BodyName = "BlockJeep";
        private const string WheelName = "WheelBlock";

        [InitializeOnLoadMethod]
        private static void Auto()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (prefab == null || prefab.transform.Find(BodyName) != null) return;
                Debug.Log("[BlockJeepSetup] (auto) " + Run());
            };
        }

        [MenuItem("Endless Survival/Setup Block Jeep")]
        public static void RunMenu() => EditorUtility.DisplayDialog("Block Jeep", Run(), "OK");

        public static void RunBatch() => Debug.Log("[BlockJeepSetup] " + Run());

        private static string Run()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) return "Prefab not found: " + PrefabPath;
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var ctrl = root.GetComponent<VehicleController>();
                if (ctrl == null) return "No VehicleController on the prefab, nothing changed.";

                Transform old = root.transform.Find(BodyName);
                Bounds body;
                bool measured = false, rebuild = old != null;
                if (old != null)
                {
                    // Rebuild: the old model is gone already, reuse the stored footprint
                    var box = old.GetComponent<BoxCollider>();
                    body = box != null ? new Bounds(old.localPosition + box.center, box.size) : new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1.9f, 1.5f, 4.6f));
                    measured = true;
                    Object.DestroyImmediate(old.gameObject);
                }
                else body = default;

                var wheels = new[]
                {
                    (ctrl.frontLeftMesh, ctrl.frontLeftCollider), (ctrl.frontRightMesh, ctrl.frontRightCollider),
                    (ctrl.rearLeftMesh, ctrl.rearLeftCollider), (ctrl.rearRightMesh, ctrl.rearRightCollider)
                };

                // 1. Strip the old art, measuring the body on the way
                int stripped = 0, boxes = 0;
                foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.transform.name == WheelName || mf.transform.parent != null && mf.transform.parent.name == WheelName) continue;
                    bool isWheel = false;
                    foreach (var wp in wheels) if (wp.Item1 != null && mf.transform.IsChildOf(wp.Item1)) isWheel = true;
                    if (!isWheel && !rebuild) Encapsulate(ref body, ref measured, root.transform, mf);
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null) Object.DestroyImmediate(mr);
                    Object.DestroyImmediate(mf);
                    stripped++;
                }
                foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Object.DestroyImmediate(smr);
                    stripped++;
                }
                foreach (MeshCollider mc in root.GetComponentsInChildren<MeshCollider>(true))
                {
                    Bounds mb = mc.sharedMesh != null ? mc.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                    bool trigger = mc.isTrigger;
                    PhysicsMaterial pm = mc.sharedMaterial;
                    GameObject go = mc.gameObject;
                    Object.DestroyImmediate(mc);
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = mb.center;
                    bc.size = mb.size;
                    bc.isTrigger = trigger;
                    bc.sharedMaterial = pm;
                    boxes++;
                }
                if (!measured) body = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1.9f, 1.5f, 4.6f));

                // 2. Materials
                Material paint = Mat("Jeep_Paint", new Color(0.33f, 0.38f, 0.24f), 0.35f);
                Material dark = Mat("Jeep_Dark", new Color(0.07f, 0.07f, 0.08f), 0.2f);
                Material glass = Mat("Jeep_Glass", new Color(0.10f, 0.15f, 0.19f), 0.85f);
                Material lamp = Mat("Jeep_Lamp", new Color(1f, 0.93f, 0.65f), 0.6f, new Color(1f, 0.9f, 0.6f) * 1.5f);
                Material tail = Mat("Jeep_TailLamp", new Color(0.7f, 0.05f, 0.05f), 0.5f, new Color(0.8f, 0.05f, 0.05f));
                Material tire = Mat("Jeep_Tire", new Color(0.05f, 0.05f, 0.05f), 0.05f);
                Material hub = Mat("Jeep_Hub", new Color(0.55f, 0.55f, 0.52f), 0.5f);

                // 3. Body. Forward is the side the front wheels are on.
                float fwd = 1f;
                if (ctrl.frontLeftCollider != null && ctrl.rearLeftCollider != null)
                    fwd = root.transform.InverseTransformPoint(ctrl.frontLeftCollider.transform.position).z
                          >= root.transform.InverseTransformPoint(ctrl.rearLeftCollider.transform.position).z ? 1f : -1f;

                var jeep = new GameObject(BodyName).transform;
                jeep.SetParent(root.transform, false);
                jeep.localPosition = body.center;
                // Footprint memo for a later rebuild (trigger, no physics effect)
                var memo = jeep.gameObject.AddComponent<BoxCollider>();
                memo.size = body.size;
                memo.isTrigger = true;
                memo.enabled = false;

                float w = body.size.x, h = body.size.y, len = body.size.z;
                // Local helpers: x in widths, y from the bottom in heights (0..1), z from the front in lengths (0..1)
                void Box(string name, Material m, float x, float y0, float y1, float z0, float z1, float width)
                {
                    GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(g.GetComponent<Collider>());
                    g.name = name;
                    g.transform.SetParent(jeep, false);
                    float zc = (0.5f - (z0 + z1) * 0.5f) * len * fwd;
                    g.transform.localPosition = new Vector3(x * w, ((y0 + y1) * 0.5f - 0.5f) * h, zc);
                    g.transform.localScale = new Vector3(width * w, (y1 - y0) * h, Mathf.Abs(z1 - z0) * len);
                    g.GetComponent<MeshRenderer>().sharedMaterial = m;
                }

                Box("Chassis", dark, 0f, 0.12f, 0.24f, 0.03f, 0.97f, 0.86f);
                Box("LowerBody", paint, 0f, 0.22f, 0.56f, 0.02f, 0.98f, 1f);
                Box("Hood", paint, 0f, 0.56f, 0.64f, 0.03f, 0.31f, 0.9f);
                Box("Grille", dark, 0f, 0.3f, 0.58f, 0.005f, 0.025f, 0.6f);
                Box("Cabin", paint, 0f, 0.56f, 1f, 0.31f, 0.63f, 0.94f);
                Box("Windshield", glass, 0f, 0.66f, 0.95f, 0.3f, 0.32f, 0.84f);
                Box("SideGlass", glass, 0f, 0.68f, 0.93f, 0.35f, 0.59f, 0.96f);
                Box("RearGlass", glass, 0f, 0.7f, 0.92f, 0.625f, 0.64f, 0.78f);
                Box("RoofRack", dark, 0f, 1f, 1.03f, 0.33f, 0.61f, 0.8f);
                Box("BedLeft", paint, -0.47f, 0.56f, 0.72f, 0.63f, 0.98f, 0.06f);
                Box("BedRight", paint, 0.47f, 0.56f, 0.72f, 0.63f, 0.98f, 0.06f);
                Box("Tailgate", paint, 0f, 0.56f, 0.72f, 0.96f, 0.98f, 0.94f);
                Box("BumperFront", dark, 0f, 0.2f, 0.32f, -0.02f, 0.03f, 1.04f);
                Box("BumperRear", dark, 0f, 0.2f, 0.32f, 0.97f, 1.02f, 1.04f);
                Box("FenderLeft", dark, -0.5f, 0.2f, 0.3f, 0.04f, 0.96f, 0.05f);
                Box("FenderRight", dark, 0.5f, 0.2f, 0.3f, 0.04f, 0.96f, 0.05f);
                Box("HeadlightLeft", lamp, -0.36f, 0.42f, 0.54f, 0.0f, 0.022f, 0.16f);
                Box("HeadlightRight", lamp, 0.36f, 0.42f, 0.54f, 0.0f, 0.022f, 0.16f);
                Box("TailLightLeft", tail, -0.42f, 0.42f, 0.54f, 0.978f, 0.99f, 0.1f);
                Box("TailLightRight", tail, 0.42f, 0.42f, 0.54f, 0.978f, 0.99f, 0.1f);
                Box("MirrorLeft", dark, -0.53f, 0.66f, 0.74f, 0.33f, 0.35f, 0.08f);
                Box("MirrorRight", dark, 0.53f, 0.66f, 0.74f, 0.33f, 0.35f, 0.08f);

                // 4. Wheels: a tyre and a hub bar (so the spin is visible) under each wheel transform the controller turns
                int wheelCount = 0;
                foreach (var pair in wheels)
                {
                    Transform mesh = pair.Item1;
                    if (mesh == null) continue;
                    for (int i = mesh.childCount - 1; i >= 0; i--)
                        if (mesh.GetChild(i).name == WheelName) Object.DestroyImmediate(mesh.GetChild(i).gameObject);
                    float radius = pair.Item2 != null ? pair.Item2.radius : 0.4f;
                    Vector3 s = mesh.lossyScale;
                    Vector3 inv = new Vector3(1f / Mathf.Max(0.0001f, Mathf.Abs(s.x)), 1f / Mathf.Max(0.0001f, Mathf.Abs(s.y)), 1f / Mathf.Max(0.0001f, Mathf.Abs(s.z)));

                    var holder = new GameObject(WheelName).transform;
                    holder.SetParent(mesh, false);
                    holder.localScale = inv;

                    GameObject tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.DestroyImmediate(tyre.GetComponent<Collider>());
                    tyre.name = "Tyre";
                    tyre.transform.SetParent(holder, false);
                    tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // cylinder axis onto the axle (x)
                    tyre.transform.localScale = new Vector3(radius * 2f, 0.14f, radius * 2f);
                    tyre.GetComponent<MeshRenderer>().sharedMaterial = tire;

                    GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(bar.GetComponent<Collider>());
                    bar.name = "Hub";
                    bar.transform.SetParent(holder, false);
                    bar.transform.localScale = new Vector3(0.3f, radius * 1.1f, radius * 0.35f);
                    bar.GetComponent<MeshRenderer>().sharedMaterial = hub;
                    wheelCount++;
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                return $"Block jeep built: {stripped} old renderer(s) removed, {boxes} mesh collider(s) turned into boxes, {wheelCount} wheel(s), body {body.size.x:0.00} x {body.size.y:0.00} x {body.size.z:0.00} m.";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void Encapsulate(ref Bounds b, ref bool any, Transform root, MeshFilter mf)
        {
            if (mf.sharedMesh == null) return;
            Bounds mb = mf.sharedMesh.bounds;
            for (int k = 0; k < 8; k++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
                Vector3 p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }

        private static Material Mat(string name, Color color, float smoothness, Color? emission = null)
        {
            if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder("Assets/Materials", "Vehicle");
            string path = MaterialDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
