using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EndlessCombat.Character.Editor
{
    /// <summary>
    /// First animation of the rebuild (2026-11-18): the Survivor idle made in Blender by
    /// Art/Characters/make_survivor_idle.py (keyed on the Survivor rig, 8 s loop, 30 fps).
    ///   1. Survivor_Idle.fbx is imported as Humanoid with its own avatar (Survivor bone map, the clip file rests in
    ///      a T-pose), one looping clip with the root locked in place,
    ///   2. a fresh player controller (Assets/Animations/Player/PlayerAnimator.controller) gets every parameter
    ///      the code sets (so Unity does not warn "parameter does not exist" every frame) and a single Idle state,
    ///   3. the player's Animator in Game.unity and the test scenes gets that controller.
    /// Enemies and NPCs stay without a controller (T-pose) for now.
    /// </summary>
    public static class SurvivorIdleSetup
    {
        public const string FbxPath = SurvivorCharacterSetup.Folder + "/Animations/Survivor_Idle.fbx";
        public const string ControllerPath = "Assets/Animations/Player/PlayerAnimator.controller";
        public const string ClipName = "Survivor_Idle";

        /// <summary>
        /// Locomotion clips (2026-11-18, Art/Characters/make_survivor_locomotion.py), in place, one gait cycle each.
        /// Threshold = the clip's ground speed in m/s = PlayerMotor's Speed parameter (calm walk at half stick,
        /// MoveSpeed 4.0 = run, SprintSpeed 6.2 = sprint), so the feet do not skate.
        /// Sprint is left out (user, 2026-11-18: "the run and sprint were very bad, only the slow walk
        /// was fairly good"). The run was remade as a relaxed jog on 2026-11-18. Until a new sprint exists the run plays
        /// above 4 m/s too (the blend tree clamps at its last child).
        /// </summary>
        private static readonly (string file, string clip, float speed)[] Locomotion =
        {
            ("Survivor_Walk.fbx", "Survivor_Walk", 1.5f),
            ("Survivor_Run.fbx", "Survivor_Run", 4.0f),
            // ("Survivor_Sprint.fbx", "Survivor_Sprint", 6.2f),
        };
        private const string AnimFolder = SurvivorCharacterSetup.Folder + "/Animations/";

        /// <summary>Parameters the gameplay code drives on the player animator (name, type).</summary>
        private static readonly (string, AnimatorControllerParameterType)[] Params =
        {
            ("Speed", AnimatorControllerParameterType.Float),
            ("MotionSpeed", AnimatorControllerParameterType.Float),
            ("MoveX", AnimatorControllerParameterType.Float),
            ("AimPitch", AnimatorControllerParameterType.Float),
            ("CombatSpeed", AnimatorControllerParameterType.Float),
            ("Jump", AnimatorControllerParameterType.Bool),
            ("Grounded", AnimatorControllerParameterType.Bool),
            ("FreeFall", AnimatorControllerParameterType.Bool),
            ("IsAiming", AnimatorControllerParameterType.Bool),
            ("IsSitting", AnimatorControllerParameterType.Bool),
            ("IsCrouching", AnimatorControllerParameterType.Bool),
            ("Fire", AnimatorControllerParameterType.Trigger),
            ("Reload", AnimatorControllerParameterType.Trigger),
            ("ActPunchJab", AnimatorControllerParameterType.Trigger),
            ("ActPunchCross", AnimatorControllerParameterType.Trigger),
            ("ActMeleeSwing", AnimatorControllerParameterType.Trigger),
            ("ActMeleeStab", AnimatorControllerParameterType.Trigger),
            ("ActThrow", AnimatorControllerParameterType.Trigger),
            ("ActHit", AnimatorControllerParameterType.Trigger),
            ("PistolFire", AnimatorControllerParameterType.Trigger),
            ("PistolReload", AnimatorControllerParameterType.Trigger),
            ("SurvAction", AnimatorControllerParameterType.Int),
            ("SurvUpper", AnimatorControllerParameterType.Int),
        };

        internal static readonly List<string> Log = new List<string>();

        [MenuItem("Endless Survival/Animations: Install Survivor (idle, walk, run)")]
        public static void RunMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string back = EditorSceneManager.GetActiveScene().path;
            Install();
            if (!string.IsNullOrEmpty(back) && File.Exists(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("Survivor animations", string.Join("\n", Log), "OK");
        }

        public static void RunBatch()
        {
            Install();
            foreach (string l in Log) Debug.Log("[SurvivorIdleSetup] " + l);
        }

        private static void Install()
        {
            Log.Clear();
            AnimationClip clip = SetupImporter(FbxPath, ClipName);
            if (clip == null) return;
            var loco = new List<(AnimationClip clip, float speed)>();
            foreach (var (file, name, speed) in Locomotion)
            {
                if (!File.Exists(AnimFolder + file)) { Log.Add("No " + file + " (make_survivor_locomotion.py), skipped."); continue; }
                AnimationClip c = SetupImporter(AnimFolder + file, name);
                if (c != null) loco.Add((c, speed));
            }
            var children = new List<(AnimationClip clip, float threshold, float timeScale)> { (clip, 0f, 1f) };
            children.AddRange(loco.Select(l => (l.clip, l.speed, 1f)));
            AnimatorController controller = SetupController(children);
            AssignToPlayers(controller);
        }

        // ------------------------------------------------------------------ clip import

        private static AnimationClip SetupImporter(string fbxPath, string clipName)
        {
            if (!(AssetImporter.GetAtPath(fbxPath) is ModelImporter imp))
            {
                AssetDatabase.Refresh();
                imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (imp == null) { Log.Add("Missing " + fbxPath + " (run the Blender script in Art/Characters)."); return null; }
            }
            imp.globalScale = 1f;
            imp.useFileScale = true;
            imp.importBlendShapes = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.importAnimation = true;
            // The clip gets its own humanoid avatar (same bone map as Survivor, rest = the clip file's T-pose).
            // "Copy From Other Avatar" fails: Unity names the file root after the file (Survivor_Idle vs Survivor),
            // and since 2026-11-18 Survivor.fbx rests in an A-pose. Humanoid clips retarget through muscle space,
            // so a separate avatar plays correctly on the Survivor body.
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.sourceAvatar = null;
            imp.SaveAndReimport(); // fills the skeleton list and defaultClipAnimations

            HumanDescription hd = imp.humanDescription;
            hd.skeleton = BuildSkeleton(fbxPath); // from this file; the failed "copy avatar" try left Survivor's list in the meta
            var names = new HashSet<string>(hd.skeleton.Select(s => s.name));
            var valid = new HashSet<string>(HumanTrait.BoneName);
            var human = new List<HumanBone>();
            foreach (var kv in BoneMap())
            {
                if (!valid.Contains(kv.Key) || !names.Contains(kv.Value)) { Log.Add("bone not mapped: " + kv.Key + " -> " + kv.Value); continue; }
                var hb = new HumanBone { humanName = kv.Key, boneName = kv.Value };
                hb.limit.useDefaultValues = true;
                human.Add(hb);
            }
            hd.human = human.ToArray();
            imp.humanDescription = hd;
            imp.SaveAndReimport();
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman) { Log.Add(clipName + " avatar INVALID (" + human.Count + " bones mapped)."); return null; }

            ModelImporterClipAnimation[] clips = imp.defaultClipAnimations;
            if (clips == null || clips.Length == 0) { Log.Add("No animation take in " + fbxPath); return null; }
            ModelImporterClipAnimation c = clips[0];
            c.name = clipName;
            c.loopTime = true;
            c.loopPose = true;
            c.lockRootRotation = true;
            c.lockRootHeightY = true;
            c.lockRootPositionXZ = true;
            c.keepOriginalOrientation = true;
            c.keepOriginalPositionY = true;
            c.keepOriginalPositionXZ = true;
            c.heightFromFeet = false;
            imp.clipAnimations = new[] { c };
            imp.SaveAndReimport();

            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(x => !x.name.StartsWith("__preview__"));
            Log.Add(clip != null
                ? $"{clipName}: Humanoid, own avatar ({human.Count} bones mapped), {clip.length:0.0} s loop."
                : "Clip not found after import.");
            return clip;
        }

        /// <summary>
        /// The clip avatar's reference pose = the Survivor body's T-pose (SurvivorCharacterSetup.BuildTPoseSkeleton),
        /// root renamed to this file, only nodes this file has. Not the clip file's own default pose: Blender writes
        /// the bones' default transforms from the current pose (a frame of the idle, arms down), and Unity would take
        /// that as the T-pose, so in muscle space "arms down" became zero and the body played arms up, wrists bent
        /// (2026-11-18). Both files come from the same rig (A-pose rest), so the bone frames match.
        /// </summary>
        /// A file with the armature only (no mesh) has a single top node, and Unity folds the armature node
        /// ("Survivor", -90 deg X) into the file root; then the root entry takes the armature node's transform,
        /// otherwise the reference lies on its back (Mixamo walk retargeted in Blender, 2026-11-18).
        internal static SkeletonBone[] BuildSkeleton(string fbxPath)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) return new SkeletonBone[0];
            HashSet<string> here;
            GameObject inst = Object.Instantiate(model);
            try { here = new HashSet<string>(inst.GetComponentsInChildren<Transform>(true).Where(t => t != inst.transform).Select(t => t.name)); }
            finally { Object.DestroyImmediate(inst); }

            SkeletonBone[] body = SurvivorCharacterSetup.BuildTPoseSkeleton();
            string armature = body[0].name; // the body's armature node has the body file's name ("Survivor")
            int armIndex = System.Array.FindIndex(body, 1, b => b.name == armature);
            bool folded = !here.Contains(armature) && armIndex > 0;
            var list = new List<SkeletonBone>();
            for (int i = 0; i < body.Length; i++)
            {
                SkeletonBone b = body[i];
                if (i == 0)
                {
                    if (folded) b = body[armIndex];                     // root carries the armature node's turn
                    b.name = model.name; list.Add(b);                   // root: named after the file
                }
                else if (i != armIndex && here.Contains(b.name)) list.Add(b);
                else if (i == armIndex && !folded) list.Add(b);
            }
            Log.Add("Clip avatar reference pose: Survivor T-pose, " + list.Count + " nodes" + (folded ? ", armature folded into the root." : "."));
            return list.ToArray();
        }

        /// <summary>Unity human bone -> MPFB game_engine rig bone (same map as SurvivorCharacterSetup).</summary>
        internal static Dictionary<string, string> BoneMap()
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

        // ------------------------------------------------------------------ controller

        /// <summary>Player controller with one state "Locomotion": a 1D blend tree on Speed (or the single clip).
        /// timeScale lets a clip made for another speed match the motor speed of its threshold (no foot skating).</summary>
        internal static AnimatorController SetupController(List<(AnimationClip clip, float threshold, float timeScale)> children)
        {
            string dir = Path.GetDirectoryName(ControllerPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Animations", "Player");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            int added = 0;
            foreach (var (name, type) in Params)
            {
                var existing = controller.parameters.FirstOrDefault(p => p.name == name);
                if (existing != null && existing.type == type) continue;
                if (existing != null) controller.RemoveParameter(existing);
                controller.AddParameter(name, type);
                added++;
            }
            if (added > 0)
            {
                // Grounded starts true so a future land / fall transition does not fire on the first frame
                var ps = controller.parameters;
                foreach (var p in ps) if (p.name == "Grounded") p.defaultBool = true;
                foreach (var p in ps) if (p.name == "MotionSpeed") p.defaultFloat = 1f;
                controller.parameters = ps;
            }

            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            // one state: idle alone, or (with the locomotion clips) a 1D blend tree on Speed: idle 0, walk, run, sprint
            AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Locomotion" || s.name == "Idle")
                                  ?? sm.AddState("Locomotion");
            state.name = "Locomotion";
            foreach (BlendTree old in AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>().ToArray())
                Object.DestroyImmediate(old, true);
            string what;
            children = children.OrderBy(c => c.threshold).ToList();
            if (children.Count > 1)
            {
                var tree = new BlendTree
                {
                    name = "Locomotion",
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = "Speed",
                    useAutomaticThresholds = false,
                    hideFlags = HideFlags.HideInHierarchy,
                };
                AssetDatabase.AddObjectToAsset(tree, controller);
                foreach (var c in children) tree.AddChild(c.clip, c.threshold);
                ChildMotion[] cm = tree.children;
                for (int i = 0; i < cm.Length; i++) cm[i].timeScale = children[i].timeScale;
                tree.children = cm;
                state.motion = tree;
                what = "blend tree on Speed: " + string.Join(", ", children.Select(c => $"{c.clip.name} {c.threshold:0.0} (x{c.timeScale:0.00})"));
            }
            else
            {
                state.motion = children[0].clip;
                what = children[0].clip.name;
            }
            state.writeDefaultValues = true;
            state.iKOnFeet = true;
            sm.defaultState = state;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Log.Add($"Controller {ControllerPath}: {Params.Length} parameters ({added} new), state Locomotion = {what}.");
            return controller;
        }

        // ------------------------------------------------------------------ scenes

        internal static void AssignToPlayers(AnimatorController controller)
        {
            var scenes = new List<string> { "Assets/Scenes/Game.unity" };
            if (AssetDatabase.IsValidFolder("Assets/Scenes/Test"))
                scenes.AddRange(AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes/Test" }).Select(AssetDatabase.GUIDToAssetPath));

            foreach (string path in scenes.Distinct())
            {
                if (!File.Exists(path)) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int n = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Animator anim in root.GetComponentsInChildren<Animator>(true))
                    {
                        if (!IsPlayer(anim.transform)) continue;
                        if (anim.runtimeAnimatorController == controller) continue;
                        Undo.RecordObject(anim, "Survivor Idle");
                        anim.runtimeAnimatorController = controller;
                        EditorUtility.SetDirty(anim);
                        n++;
                    }
                if (n > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                Log.Add($"{Path.GetFileNameWithoutExtension(path)}: {n} player animator(s) set.");
            }
        }

        private static bool IsPlayer(Transform t)
        {
            for (; t != null; t = t.parent)
                foreach (MonoBehaviour mb in t.GetComponents<MonoBehaviour>())
                    if (mb != null && mb.GetType().Name == "PlayerMotor") return true;
            return false;
        }
    }
}
