using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EndlessCombat.Character;

namespace EndlessCombat.Character.Editor
{
    /// <summary>
    /// Makes the Blender character (Art/Characters/Survivor_Test.blend, MPFB + MakeHuman CC0 assets, exported by
    /// Art/Characters/export_survivor_fbx.py) the player's body (2026-11-17):
    ///   1. Survivor.fbx is imported as Humanoid with an explicit bone map (game_engine rig names), T-pose rest,
    ///   2. URP Lit materials are made for skin, hair (alpha clip, two sided), eyes, brows, lashes, teeth, tongue, underwear,
    ///   3. the player's Animator in Game.unity gets the new body through CharacterModelMigration.Swap, so sockets,
    ///      colliders and script references on the old bones move to the new bones. The animator controller and every
    ///      player component stay. Enemies and NPCs keep HumanM_Model.
    /// Back to the old body: Endless Survival/Player Model: Back To HumanM.
    /// Batch: -executeMethod EndlessCombat.Character.Editor.SurvivorCharacterSetup.RunBatch
    /// </summary>
    public static class SurvivorCharacterSetup
    {
        public const string Folder = "Assets/Art/Characters/Survivor";
        public const string FbxPath = Folder + "/Survivor.fbx";
        private const string MatFolder = Folder + "/Materials";
        private const string ScenePath = "Assets/Scenes/Game.unity";

        private static readonly List<string> Log = new List<string>();

        /// <summary>Unity human bone name -> bone in the MPFB game_engine rig.</summary>
        private static readonly Dictionary<string, string> BoneMap = BuildBoneMap();

        private static Dictionary<string, string> BuildBoneMap()
        {
            var m = new Dictionary<string, string>
            {
                { "Hips", "pelvis" }, { "Spine", "spine_01" }, { "Chest", "spine_02" }, { "UpperChest", "spine_03" },
                { "Neck", "neck_01" }, { "Head", "head" },
            };
            foreach (var (side, s) in new[] { ("Left", "l"), ("Right", "r") })
            {
                m[side + "Shoulder"] = "clavicle_" + s;
                m[side + "UpperArm"] = "upperarm_" + s;
                m[side + "LowerArm"] = "lowerarm_" + s;
                m[side + "Hand"] = "hand_" + s;
                m[side + "UpperLeg"] = "thigh_" + s;
                m[side + "LowerLeg"] = "calf_" + s;
                m[side + "Foot"] = "foot_" + s;
                m[side + "Toes"] = "ball_" + s;
                foreach (var (finger, f) in new[] { ("Thumb", "thumb"), ("Index", "index"), ("Middle", "middle"), ("Ring", "ring"), ("Little", "pinky") })
                {
                    m[side + " " + finger + " Proximal"] = f + "_01_" + s;
                    m[side + " " + finger + " Intermediate"] = f + "_02_" + s;
                    m[side + " " + finger + " Distal"] = f + "_03_" + s;
                }
            }
            return m;
        }

        [MenuItem("Endless Survival/Player Model: Install Survivor")]
        public static void RunMenu()
        {
            Install();
            EditorUtility.DisplayDialog("Survivor", string.Join("\n", Log), "OK");
        }

        [MenuItem("Endless Survival/Player Model: Back To HumanM")]
        public static void RevertMenu()
        {
            Revert();
            EditorUtility.DisplayDialog("Survivor", string.Join("\n", Log), "OK");
        }

        public static void RunBatch()
        {
            Install();
            foreach (string l in Log) Debug.Log("[SurvivorCharacterSetup] " + l);
        }

        public static void RevertBatch()
        {
            Revert();
            foreach (string l in Log) Debug.Log("[SurvivorCharacterSetup] " + l);
        }

        // ------------------------------------------------------------------ install

        private static void Install()
        {
            Log.Clear();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null)
            {
                AssetDatabase.Refresh();
                if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null) { Log.Add("Missing " + FbxPath + " (run Art/Characters/export_survivor_fbx.py)."); return; }
            }
            SetupTextures();
            Dictionary<string, Material> mats = SetupMaterials();
            if (!SetupImporter(mats)) return;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Avatar>().FirstOrDefault();
            if (model == null || avatar == null || !avatar.isHuman) { Log.Add("Survivor avatar is not humanoid, player NOT changed."); return; }
            SwapPlayer(model, avatar, null, true); // always rebuild: the FBX may have changed under the same avatar
        }

        private static void Revert()
        {
            Log.Clear();
            string path = CharacterModelMigration.ModelPath;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (model == null || avatar == null) { Log.Add("Missing " + path); return; }
            SwapPlayer(model, avatar, CharacterModelMigration.BodyMaterial("Body_Player", new Color(0.68f, 0.7f, 0.72f)), false);
        }

        private static void SwapPlayer(GameObject model, Avatar avatar, Material material, bool survivor)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int n = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Animator anim in root.GetComponentsInChildren<Animator>(true))
                {
                    if (!IsPlayer(anim.transform)) continue;
                    if (!survivor && anim.avatar == avatar) { Log.Add("Player '" + anim.name + "' already uses " + avatar.name + "."); continue; }
                    string from = anim.avatar != null ? anim.avatar.name : "none";
                    CharacterModelMigration.Swap(anim, model, avatar, material);
                    var twist = anim.GetComponent<TwistBones>();
                    if (survivor && twist == null) anim.gameObject.AddComponent<TwistBones>();
                    if (!survivor && twist != null) Object.DestroyImmediate(twist);
                    var muscle = anim.GetComponent<MuscleBones>();
                    if (survivor && muscle == null) anim.gameObject.AddComponent<MuscleBones>();
                    if (!survivor && muscle != null) Object.DestroyImmediate(muscle);
                    Log.Add("Player '" + anim.name + "': body " + from + " -> " + avatar.name + (survivor ? " (twist + muscle bones on)." : "."));
                    n++;
                }
            if (n == 0) { Log.Add("No player body changed."); return; }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        private static bool IsPlayer(Transform t)
        {
            for (; t != null; t = t.parent)
                foreach (MonoBehaviour mb in t.GetComponents<MonoBehaviour>())
                    if (mb != null && mb.GetType().Name == "PlayerMotor") return true;
            return false;
        }

        // ------------------------------------------------------------------ import settings

        private static bool SetupImporter(Dictionary<string, Material> mats)
        {
            var imp = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (imp == null) { Log.Add("No ModelImporter for " + FbxPath); return false; }
            imp.globalScale = 1f;
            imp.useFileScale = true;
            imp.importBlendShapes = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importAnimation = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var kv in mats) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport(); // fills the skeleton list

            HumanDescription hd = imp.humanDescription;
            hd.skeleton = BuildTPoseSkeleton(); // fresh from the FBX (an old meta keeps a stale list otherwise)
            var names = new HashSet<string>(hd.skeleton.Select(s => s.name));
            var valid = new HashSet<string>(HumanTrait.BoneName);
            var human = new List<HumanBone>();
            foreach (var kv in BoneMap)
            {
                if (!valid.Contains(kv.Key)) { Log.Add("unknown human bone " + kv.Key); continue; }
                if (!names.Contains(kv.Value)) { Log.Add("bone missing in FBX: " + kv.Value); continue; }
                var hb = new HumanBone { humanName = kv.Key, boneName = kv.Value };
                hb.limit.useDefaultValues = true;
                human.Add(hb);
            }
            hd.human = human.ToArray();
            imp.humanDescription = hd;
            imp.SaveAndReimport();

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Avatar>().FirstOrDefault();
            Log.Add("Survivor.fbx: Humanoid, " + human.Count + " bones mapped, avatar " + (avatar != null && avatar.isHuman ? "valid" : "INVALID") + ".");
            return avatar != null && avatar.isHuman;
        }

        /// <summary>Thumb angle away from the hand line toward the front in the reference pose (palm down).</summary>
        private const float ThumbSpread = 40f;

        /// <summary>Legs straight down in the reference pose (false keeps the rig's own slight knee bend).</summary>
        public static bool StraightLegs = true;

        /// <summary>
        /// The FBX rests in MPFB's A-pose (the skin weights were made for it; 2026-11-18). The avatar's reference pose
        /// is this skeleton list, a full standard T-pose like the importer's "Enforce T-Pose" (the mesh is not touched):
        ///   arms level out to the side, palm down (index ahead of pinky), fingers straight along the hand,
        ///   thumb level and spread ThumbSpread degrees forward, legs straight down, feet pointing forward with their
        ///   own pitch, toes level forward.
        /// Why all of it: Unity retargets through muscle space, where 0 = this pose. With only the arms fixed the
        /// fingers stayed curled 23 deg and the thumb hung down, and the feet kept the A-pose stance, so every clip
        /// (Mixamo or our own) played curled fists with a thumb poking down and off ankles (2026-11-18).
        /// Also the reference pose of our own clips (SurvivorIdleSetup), so body and clips always share it.
        /// </summary>
        internal static SkeletonBone[] BuildTPoseSkeleton()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            GameObject inst = Object.Instantiate(model);
            inst.name = model.name;
            try
            {
                Transform root = inst.transform;
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var all = root.GetComponentsInChildren<Transform>(true);
                Transform Get(string n) => all.FirstOrDefault(t => t.name == n && t != root);
                Vector3 fwd = Vector3.forward, down = Vector3.down;
                foreach (string s in new[] { "l", "r" })
                {
                    Transform ua = Get("upperarm_" + s), la = Get("lowerarm_" + s), hand = Get("hand_" + s);
                    if (ua != null && la != null && hand != null)
                    {
                        Vector3 side = Vector3.right * Mathf.Sign((la.position - ua.position).x);
                        Aim(ua, la, side);
                        Aim(la, hand, side);
                        Aim(hand, Get("middle_01_" + s), side);
                        // palm down: roll the hand about the arm line so the index knuckle is ahead of the pinky's
                        Transform i1 = Get("index_01_" + s), p1 = Get("pinky_01_" + s);
                        if (i1 != null && p1 != null)
                        {
                            Vector3 across = Vector3.ProjectOnPlane(i1.position - p1.position, side);
                            if (across.sqrMagnitude > 1e-8f)
                                hand.rotation = Quaternion.FromToRotation(across, fwd) * hand.rotation;
                        }
                        foreach (string f in new[] { "index", "middle", "ring", "pinky" })
                            Chain(Get, f, s, side);
                        Vector3 thumbDir = Quaternion.AngleAxis(ThumbSpread * (side.x > 0 ? -1f : 1f), Vector3.up) * side;
                        Chain(Get, "thumb", s, thumbDir);
                    }
                    Transform th = Get("thigh_" + s), ca = Get("calf_" + s), ft = Get("foot_" + s), ball = Get("ball_" + s);
                    if (th != null && ca != null && ft != null)
                    {
                        if (StraightLegs) { Aim(th, ca, down); Aim(ca, ft, down); }
                        if (ball != null)
                        {
                            Vector3 cur = ball.position - ft.position;                 // keep the foot's pitch, drop its yaw
                            Vector3 flat = new Vector3(0f, cur.y, new Vector2(cur.x, cur.z).magnitude);
                            Aim(ft, ball, flat.normalized);
                            Aim(ball, null, fwd);
                        }
                    }
                }
                Transform tl = Get("thumb_02_l"), tl1 = Get("thumb_01_l"), hl = Get("hand_l"), ml = Get("middle_01_l");
                if (tl != null && tl1 != null && hl != null && ml != null)
                    Log.Add("Avatar T-pose (full): thumb " + Vector3.Angle(tl.position - tl1.position, ml.position - hl.position).ToString("F0") +
                            " deg from hand, left foot " + (Get("ball_l").position - Get("foot_l").position).normalized.ToString("F2") + " (A-pose mesh kept).");
                return all.Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            }
            finally { Object.DestroyImmediate(inst); }
        }

        /// <summary>Straightens a finger (_01, _02, _03) along dir.</summary>
        private static void Chain(System.Func<string, Transform> get, string finger, string s, Vector3 dir)
        {
            Transform a = get(finger + "_01_" + s), b = get(finger + "_02_" + s), c = get(finger + "_03_" + s);
            if (a == null || b == null) return;
            Aim(a, b, dir);
            if (c != null) { Aim(b, c, dir); Aim(c, null, dir); }
        }

        /// <summary>Turns bone so its length points along dir. Length = toward child; a leaf uses its own bone axis,
        /// taken as the axis its parent uses (every bone of the rig is exported with the same axis convention).</summary>
        private static void Aim(Transform bone, Transform child, Vector3 dir)
        {
            Vector3 cur = child != null ? child.position - bone.position : bone.TransformDirection(bone.localPosition.normalized);
            if (cur.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
        }

        private static void SetupTextures()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                bool normal = path.EndsWith("_Normal.png");
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal;
                ti.alphaIsTransparency = !normal && ti.DoesSourceTextureHaveAlpha();
                ti.maxTextureSize = 2048;
                ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
        }

        private static Dictionary<string, Material> SetupMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder(Folder, "Materials");
            var mats = new Dictionary<string, Material>();
            //               name                  smooth  cutout  twoSided  color
            Make(mats, "Survivor_Skin",      0.35f, false, false, Color.white);
            Make(mats, "Survivor_Hair",      0.25f, true,  true,  Color.white, Folder + "/Survivor_Hair_Normal.png");
            Make(mats, "Survivor_Eyes",      0.85f, false, false, Color.white);
            Make(mats, "Survivor_Eyebrows",  0.1f,  true,  true,  Color.white);
            Make(mats, "Survivor_Eyelashes", 0.1f,  true,  true,  Color.white);
            Make(mats, "Survivor_Teeth",     0.6f,  false, false, Color.white);
            Make(mats, "Survivor_Tongue",    0.5f,  false, false, Color.white);
            Make(mats, "Survivor_Underwear", 0.15f, false, false, new Color(0.22f, 0.23f, 0.18f));   // dark olive canvas shorts (make_shorts.py, 2026-11-18)
            Log.Add("Materials: " + mats.Count + " (URP Lit) in " + MatFolder);
            return mats;
        }

        private static void Make(Dictionary<string, Material> mats, string name, float smooth, bool cutout, bool twoSided, Color color, string normalPath = null)
        {
            string path = MatFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + name + "_Albedo.png");
            if (albedo != null) { m.SetTexture("_BaseMap", albedo); m.mainTexture = albedo; }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);

            m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", 0.4f);
            if (cutout) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = cutout ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest : -1;
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            m.doubleSidedGI = twoSided;

            var normal = normalPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) : null;
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            EditorUtility.SetDirty(m);
            mats[name] = m;
        }
    }
}
