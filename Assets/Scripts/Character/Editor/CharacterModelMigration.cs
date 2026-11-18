using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EndlessCombat.Character.Editor
{
    /// <summary>
    /// Takes the project off the Starter Assets Third Person Controller pack (2026-11-13), so the folder can go:
    ///   1. the shared animator controller moves to Assets/Animations/Character (same GUID, nothing re-linked) and
    ///      every motion that still comes from the pack is replaced,
    ///   2. the player (scene), Enemy, NPC_Thomas and TraderNPC get another body model (any humanoid FBX; default
    ///      the Kevin Iglesias HumanM model the clips were made for). Sockets, colliders, ragdoll parts and
    ///      script references on the old bones are carried over to the matching bones,
    ///   3. the pack's camera prefabs in the scene are unpacked, its scripts and sounds are unhooked.
    /// The folder itself is removed afterwards from outside Unity.
    /// Batch: -executeMethod EndlessCombat.Character.Editor.CharacterModelMigration.RunBatch
    /// A different model later: change ModelPath and run Endless Survival/Swap Character Model.
    /// </summary>
    public static class CharacterModelMigration
    {
        public const string ModelPath = "Assets/Kevin Iglesias/Human Animations/Models/HumanM_Model.fbx";
        private const string Pack = "Assets/Starter Assets";
        private const string OldController = Pack + "/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
        public const string ControllerPath = "Assets/Animations/Character/CharacterAnimator.controller";
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const string IdlePath = "Assets/Kevin Iglesias/Human Animations/Animations/Male/Idles/HumanM@Idle01.fbx";
        private static readonly string[] Prefabs =
        {
            "Assets/Prefabs/Enemies/Enemy.prefab", "Assets/Prefabs/Story/NPC_Thomas.prefab", "Assets/Prefabs/Survival/TraderNPC.prefab"
        };
        private static readonly string[] PackScripts = { "ThirdPersonController", "StarterAssetsInputs", "BasicRigidBodyPush", "PlayerInput" };

        private static readonly List<string> Log = new List<string>();

        [MenuItem("Endless Survival/Swap Character Model")]
        public static void RunMenu()
        {
            Run();
            EditorUtility.DisplayDialog("Character model", string.Join("\n", Log), "OK");
        }

        public static void RunBatch()
        {
            Run();
            foreach (string l in Log) Debug.Log("[CharacterModelMigration] " + l);
        }

        private static void Run()
        {
            Log.Clear();
            MoveController();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (model == null || avatar == null || !avatar.isHuman) { Log.Add("Model or humanoid avatar missing: " + ModelPath); return; }

            foreach (string path in Prefabs)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { Log.Add("prefab missing: " + path); continue; }
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int n = Process(root, model, avatar, path.Contains("Enemy") ? BodyMaterial("Body_Enemy", new Color(0.62f, 0.13f, 0.11f)) : BodyMaterial("Body_Npc", new Color(0.55f, 0.47f, 0.36f)));
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Log.Add(System.IO.Path.GetFileName(path) + ": " + n + " body(ies) swapped.");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int total = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                total += Process(root, model, avatar, BodyMaterial("Body_Player", new Color(0.68f, 0.7f, 0.72f)));
            // Anything else in the scene that is still an instance of a pack prefab (cameras)
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    UnpackIfFromPack(t.gameObject);
            Log.Add("Game.unity: " + total + " body(ies) swapped.");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            UnhookAssets();
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ animator controller

        private static void MoveController()
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Animations/Character")) AssetDatabase.CreateFolder("Assets/Animations", "Character");
                string err = AssetDatabase.MoveAsset(OldController, ControllerPath);
                Log.Add(string.IsNullOrEmpty(err) ? "Animator controller moved to " + ControllerPath : "Controller move FAILED: " + err);
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) return;

            AnimationClip idle = AssetDatabase.LoadAllAssetsAtPath(IdlePath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            var used = new HashSet<Object>();
            int replaced = 0;
            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                if (layer.avatarMask != null && AssetDatabase.GetAssetPath(layer.avatarMask).StartsWith(Pack))
                    Log.Add("WARNING layer '" + layer.name + "' uses a mask from the pack: " + AssetDatabase.GetAssetPath(layer.avatarMask));
                foreach (AnimatorState s in AllStates(layer.stateMachine))
                {
                    s.motion = Clean(s.motion, idle, ref replaced, s.name);
                    Collect(s.motion, used);
                }
            }
            // Blend trees nobody plays any more (the old idle / walk / run tree) still hold the pack's clips
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (o is BlendTree bt && !used.Contains(bt)) Object.DestroyImmediate(bt, true);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            string[] left = AssetDatabase.GetDependencies(ControllerPath, true).Where(d => d.StartsWith(Pack)).ToArray();
            Log.Add("Controller: " + replaced + " pack motion(s) replaced; pack dependencies left: " + (left.Length == 0 ? "none" : string.Join(", ", left)));
        }

        private static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine sm)
        {
            if (sm == null) yield break; // a layer without its own state machine
            foreach (var s in sm.states) if (s.state != null) yield return s.state;
            foreach (var sub in sm.stateMachines)
                foreach (var s in AllStates(sub.stateMachine)) yield return s;
        }

        private static Motion Clean(Motion m, AnimationClip fallback, ref int replaced, string where)
        {
            if (m is BlendTree tree)
            {
                ChildMotion[] kids = tree.children;
                for (int i = 0; i < kids.Length; i++) kids[i].motion = Clean(kids[i].motion, fallback, ref replaced, where);
                tree.children = kids;
                return tree;
            }
            if (m != null && AssetDatabase.GetAssetPath(m).StartsWith(Pack))
            {
                replaced++;
                Log.Add("pack clip '" + m.name + "' in state '" + where + "' replaced with the idle clip");
                return fallback;
            }
            return m;
        }

        private static void Collect(Motion m, HashSet<Object> used)
        {
            if (m is BlendTree tree && used.Add(tree))
                foreach (ChildMotion c in tree.children) Collect(c.motion, used);
        }

        // ------------------------------------------------------------------ body swap

        private static void UnpackIfFromPack(GameObject go)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(go)) return;
            GameObject instRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (instRoot == null) return;
            string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instRoot);
            bool fromPack = src.StartsWith(Pack);
            if (!fromPack)
            {
                // A nested instance of a pack prefab somewhere up the chain
                GameObject near = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                fromPack = near != null && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(near).StartsWith(Pack);
                if (fromPack) instRoot = near;
            }
            if (!fromPack) return;
            PrefabUtility.UnpackPrefabInstance(instRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Log.Add("unpacked pack prefab instance '" + instRoot.name + "'");
        }

        /// <summary>Swaps the body of every character under root whose avatar comes from the pack. Returns how many.</summary>
        private static int Process(GameObject root, GameObject model, Avatar avatar, Material material)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) UnpackIfFromPack(t.gameObject);
            int count = 0;
            foreach (Animator anim in root.GetComponentsInChildren<Animator>(true))
            {
                if (anim.avatar == null || !AssetDatabase.GetAssetPath(anim.avatar).StartsWith(Pack)) continue;
                if (PrefabUtility.IsPartOfPrefabInstance(anim.gameObject)) continue; // an instance of one of our own prefabs: fixed in that prefab
                Swap(anim, model, avatar, material);
                count++;
            }
            // Pack scripts anywhere under the root (they would become missing scripts)
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) continue;
                foreach (string name in PackScripts)
                {
                    Component c = t.GetComponent(name);
                    if (c != null) Object.DestroyImmediate(c);
                }
            }
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                MonoScript script = MonoScript.FromMonoBehaviour(mb);
                if (script != null && AssetDatabase.GetAssetPath(script).StartsWith(Pack)) Object.DestroyImmediate(mb);
            }
            // Left over pieces of the old body (its mesh object sits outside the skeleton)
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null || !AssetDatabase.GetAssetPath(smr.sharedMesh).StartsWith(Pack)) continue;
                Transform t = smr.transform, parent = t.parent;
                Object.DestroyImmediate(t.gameObject);
                while (parent != null && parent != root.transform && parent.childCount == 0 && parent.GetComponents<Component>().Length == 1)
                {
                    Transform up = parent.parent;
                    Object.DestroyImmediate(parent.gameObject);
                    parent = up;
                }
                count++;
            }
            return count;
        }

        private static readonly Dictionary<string, string> ChildOf = new Dictionary<string, string>
        {
            { "Hips", "Spine" }, { "Spine", "Chest" }, { "Chest", "Neck" }, { "UpperChest", "Neck" }, { "Neck", "Head" },
            { "LeftShoulder", "LeftUpperArm" }, { "LeftUpperArm", "LeftLowerArm" }, { "LeftLowerArm", "LeftHand" },
            { "RightShoulder", "RightUpperArm" }, { "RightUpperArm", "RightLowerArm" }, { "RightLowerArm", "RightHand" },
            { "LeftUpperLeg", "LeftLowerLeg" }, { "LeftLowerLeg", "LeftFoot" }, { "LeftFoot", "LeftToes" },
            { "RightUpperLeg", "RightLowerLeg" }, { "RightLowerLeg", "RightFoot" }, { "RightFoot", "RightToes" }
        };

        private static Dictionary<string, Transform> HumanMap(Avatar avatar, Transform root)
        {
            var byName = new Dictionary<string, Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            var map = new Dictionary<string, Transform>();
            foreach (HumanBone hb in avatar.humanDescription.human)
                if (byName.TryGetValue(hb.boneName, out Transform t)) map[hb.humanName.Replace(" ", "")] = t;
            return map;
        }

        /// <summary>A frame on a bone that means the same on any rig: along the limb, or the palm frame for a hand.</summary>
        private static Matrix4x4 Frame(string human, Dictionary<string, Transform> map, Transform character)
        {
            Transform bone = map[human];
            Vector3 pos = bone.position;
            Quaternion rot = character.rotation;
            if (human == "LeftHand" || human == "RightHand")
            {
                string side = human.Substring(0, human.Length - 4);
                if (map.TryGetValue(side + "MiddleProximal", out Transform mid) && map.TryGetValue(side + "IndexProximal", out Transform index)
                    && map.TryGetValue(side + "LittleProximal", out Transform little))
                {
                    Vector3 fingers = (mid.position - pos).normalized, knuckles = (index.position - little.position).normalized;
                    rot = Quaternion.LookRotation(fingers, Vector3.Cross(fingers, knuckles).normalized);
                }
                else if (map.TryGetValue(side + "LowerArm", out Transform lower))
                    rot = Quaternion.LookRotation((pos - lower.position).normalized, character.forward);
            }
            else if (ChildOf.TryGetValue(human, out string child) && map.TryGetValue(child, out Transform c) && (c.position - pos).sqrMagnitude > 0.000001f)
            {
                Vector3 dir = (c.position - pos).normalized;
                Vector3 second = Mathf.Abs(Vector3.Dot(dir, character.forward)) > 0.9f ? character.up : character.forward;
                rot = Quaternion.LookRotation(dir, second);
            }
            return Matrix4x4.TRS(pos, rot, Vector3.one);
        }

        /// <summary>Puts another humanoid body under anim (sockets, colliders and references carried over).
        /// material null keeps the model's own materials. Also used by SurvivorCharacterSetup.</summary>
        internal static void Swap(Animator anim, GameObject model, Avatar avatar, Material material)
        {
            Transform character = anim.transform;
            Avatar oldAvatar = anim.avatar;
            var oldSkeleton = new HashSet<string>(oldAvatar.humanDescription.skeleton.Select(s => s.name));
            Dictionary<string, Transform> oldMap = HumanMap(oldAvatar, character);

            // Old model objects: transforms named in the old avatar's skeleton, reachable through such transforms only
            var oldNodes = new HashSet<Transform>();
            void Mark(Transform t)
            {
                foreach (Transform c in t)
                    if (oldSkeleton.Contains(c.name)) { oldNodes.Add(c); Mark(c); }
            }
            Mark(character);
            var oldTop = new List<Transform>();
            foreach (Transform c in character) if (oldNodes.Contains(c)) oldTop.Add(c);

            // New body
            GameObject inst = Object.Instantiate(model, character.position, character.rotation);
            var newTop = new List<Transform>();
            foreach (Transform c in inst.transform) newTop.Add(c);
            foreach (Transform c in newTop) c.SetParent(character, true);
            Object.DestroyImmediate(inst);
            var newRoot = new GameObject("__new").transform; // temporary holder so name lookups see only the new body
            newRoot.SetParent(character, false);
            foreach (Transform c in newTop) c.SetParent(newRoot, true);
            Dictionary<string, Transform> newMap = HumanMap(avatar, newRoot);
            foreach (Transform c in newTop) { c.SetParent(character, true); c.gameObject.layer = character.gameObject.layer; }
            Object.DestroyImmediate(newRoot.gameObject);
            foreach (Transform top in newTop)
            {
                foreach (Transform t in top.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = character.gameObject.layer;
                if (material != null)
                foreach (Renderer r in top.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = material;
                    r.sharedMaterials = mats;
                }
            }

            // Old bone -> new bone (human bones; other old nodes go to the new bone of their nearest mapped ancestor)
            var boneTo = new Dictionary<Transform, Transform>();
            var move = new Dictionary<Transform, Matrix4x4>();
            foreach (var kv in oldMap)
            {
                if (!newMap.TryGetValue(kv.Key, out Transform nb)) continue;
                boneTo[kv.Value] = nb;
                move[kv.Value] = Frame(kv.Key, newMap, character) * Frame(kv.Key, oldMap, character).inverse;
            }
            Transform fallbackNew = newMap.TryGetValue("Hips", out Transform hips) ? hips : character;
            Transform NewFor(Transform old, out Matrix4x4 m)
            {
                for (Transform t = old; t != null && t != character; t = t.parent)
                    if (boneTo.TryGetValue(t, out Transform nb)) { m = move[t]; return nb; }
                m = Matrix4x4.identity;
                return fallbackNew;
            }

            var remap = new Dictionary<Object, Object>();
            var keep = new List<(Transform child, Transform parent, Vector3 pos, Quaternion rot)>();
            foreach (Transform old in oldNodes)
            {
                Transform nb = NewFor(old, out Matrix4x4 m);
                remap[old] = nb;
                remap[old.gameObject] = nb.gameObject;

                // Things hanging on the bone (sockets, weapons, effects)
                foreach (Transform child in old)
                {
                    if (oldNodes.Contains(child)) continue;
                    keep.Add((child, nb, m.MultiplyPoint3x4(child.position), m.rotation * child.rotation));
                }

                // Components on the bone itself (colliders, ragdoll bodies, joints, hit boxes)
                foreach (Component c in old.GetComponents<Component>())
                {
                    if (c == null || c is Transform || c is Renderer || c is MeshFilter) continue;
                    if (c is Joint) continue; // second pass, after every body exists
                    Component copy = CopyTo(c, nb.gameObject);
                    if (copy == null) continue;
                    remap[c] = copy;
                    if (copy is Collider col) FixCollider((Collider)c, col, old, nb, m);
                }
            }
            foreach (Transform old in oldNodes)
            {
                foreach (Joint j in old.GetComponents<Joint>())
                {
                    Transform nb = (Transform)remap[old];
                    if (nb.GetComponent<Rigidbody>() == null) continue;
                    var copy = CopyTo(j, nb.gameObject) as Joint;
                    if (copy == null) continue;
                    remap[j] = copy;
                    if (j.connectedBody != null && remap.TryGetValue(j.connectedBody, out Object body)) copy.connectedBody = (Rigidbody)body;
                    // The old axes were in the old bone's space: let Unity work the anchors out, keep the limits
                    copy.anchor = Vector3.zero;
                    copy.autoConfigureConnectedAnchor = true;
                    if (copy is CharacterJoint cj)
                    {
                        Matrix4x4 toNew = nb.worldToLocalMatrix * NewForMatrix(old) * old.localToWorldMatrix;
                        cj.axis = NearestAxis(toNew.MultiplyVector(((CharacterJoint)j).axis));
                        cj.swingAxis = NearestAxis(toNew.MultiplyVector(((CharacterJoint)j).swingAxis));
                    }
                }
            }
            Matrix4x4 NewForMatrix(Transform old) { NewFor(old, out Matrix4x4 m); return m; }

            foreach (var k in keep)
            {
                k.child.SetParent(k.parent, true);
                k.child.SetPositionAndRotation(k.pos, k.rot);
            }

            // Script fields that pointed at old bones or at components on them
            foreach (Component c in character.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || oldNodes.Contains(c.transform)) continue;
                var so = new SerializedObject(c);
                SerializedProperty p = so.GetIterator();
                bool changed = false;
                while (p.NextVisible(true))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue == null) continue;
                    if (remap.TryGetValue(p.objectReferenceValue, out Object to)) { p.objectReferenceValue = to; changed = true; }
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }

            foreach (Transform t in oldTop) Object.DestroyImmediate(t.gameObject);
            anim.avatar = avatar;
            EditorUtility.SetDirty(anim);
        }

        private static Component CopyTo(Component source, GameObject target)
        {
            if (!UnityEditorInternal.ComponentUtility.CopyComponent(source)) return null;
            int before = target.GetComponents<Component>().Length;
            if (!UnityEditorInternal.ComponentUtility.PasteComponentAsNew(target)) return null;
            Component[] all = target.GetComponents<Component>();
            return all.Length > before ? all[all.Length - 1] : null;
        }

        private static Vector3 NearestAxis(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (a.y >= a.z) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        private static int AxisIndex(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            return a.x >= a.y && a.x >= a.z ? 0 : (a.y >= a.z ? 1 : 2);
        }

        /// <summary>Rewrites a copied collider from the old bone's space into the new bone's space.</summary>
        private static void FixCollider(Collider source, Collider copy, Transform oldBone, Transform newBone, Matrix4x4 move)
        {
            Matrix4x4 toNew = newBone.worldToLocalMatrix * move * oldBone.localToWorldMatrix;
            float scale = toNew.MultiplyVector(Vector3.right).magnitude;
            if (source is SphereCollider s)
            {
                var c = (SphereCollider)copy;
                c.center = toNew.MultiplyPoint3x4(s.center);
                c.radius = s.radius * scale;
            }
            else if (source is CapsuleCollider cap)
            {
                var c = (CapsuleCollider)copy;
                Vector3 axis = cap.direction == 0 ? Vector3.right : cap.direction == 1 ? Vector3.up : Vector3.forward;
                c.center = toNew.MultiplyPoint3x4(cap.center);
                c.direction = AxisIndex(toNew.MultiplyVector(axis));
                c.radius = cap.radius * scale;
                c.height = cap.height * scale;
            }
            else if (source is BoxCollider box)
            {
                var c = (BoxCollider)copy;
                c.center = toNew.MultiplyPoint3x4(box.center);
                Vector3 size = Vector3.zero;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 axis = i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
                    size[AxisIndex(toNew.MultiplyVector(axis))] = box.size[i] * scale;
                }
                for (int i = 0; i < 3; i++) if (size[i] <= 0f) size[i] = box.size[i] * scale;
                c.size = size;
            }
        }

        internal static Material BodyMaterial(string name, Color color)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials/Character")) AssetDatabase.CreateFolder("Assets/Materials", "Character");
            string path = "Assets/Materials/Character/" + name + ".mat";
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
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.3f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ other assets

        /// <summary>Clears references from our own assets to files of the pack (the footstep sounds in CombatContent).</summary>
        private static void UnhookAssets()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/Resources", "Assets/Data" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!AssetDatabase.GetDependencies(path, false).Any(d => d.StartsWith(Pack))) continue;
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (o == null) continue;
                    var so = new SerializedObject(o);
                    SerializedProperty p = so.GetIterator();
                    int cleared = 0;
                    while (p.Next(true))
                    {
                        if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue == null) continue;
                        if (p.name == "m_Script") continue;
                        if (AssetDatabase.GetAssetPath(p.objectReferenceValue).StartsWith(Pack)) { p.objectReferenceValue = null; cleared++; }
                    }
                    if (cleared > 0)
                    {
                        so.ApplyModifiedPropertiesWithoutUndo();
                        Log.Add(System.IO.Path.GetFileName(path) + ": " + cleared + " reference(s) to pack files cleared.");
                    }
                }
            }
        }
    }
}
