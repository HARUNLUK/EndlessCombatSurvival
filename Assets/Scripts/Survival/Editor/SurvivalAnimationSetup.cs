using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace EndlessSurvival.Survival.Editor
{
    /// <summary>
    /// Hooks the user's Mixamo clips (Assets/Animations/Survival) into the game. Clips are only trimmed and imported,
    /// never authored. The notes in the file names decide the trim:
    ///   "(only first second needed)" -> first second, "(second half needed)" -> second half,
    ///   "Fishing Cast (second half reeling)" -> first half = cast, second half = reel.
    /// Builds: humanoid import, the SurvivalFullBody layer (int SurvAction) on every humanoid controller used by the
    /// player and the enemies, the enemy Hit React clip, and the TraderNPC prefab + controller (Standing / Talking).
    /// Menu: Endless Survival > Setup Survival Animations. Batch: EndlessSurvival.Survival.Editor.SurvivalAnimationSetup.RunBatch
    /// </summary>
    public static class SurvivalAnimationSetup
    {
        private const string Dir = "Assets/Animations/Survival";
        private const string PlayerControllerPath = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
        private const string TraderControllerPath = Dir + "/TraderNPC.controller";
        private const string TraderPrefabDir = "Assets/Prefabs/Survival";
        private const string TraderPrefabPath = TraderPrefabDir + "/TraderNPC.prefab";
        private const string TraderMaterialPath = TraderPrefabDir + "/TraderNPC_Mat.mat";

        private struct ClipSpec
        {
            public string name;
            public float from, to;     // fraction of the source clip
            public float seconds;      // > 0: take this many seconds from 'from' instead of 'to'
            public bool loop;
            public bool feet;          // height from the feet instead of the file root (kneel, lying)

            public ClipSpec(string name, float from, float to, bool loop, float seconds = 0f, bool feet = false)
            {
                this.feet = feet;
                this.name = name;
                this.from = from;
                this.to = to;
                this.loop = loop;
                this.seconds = seconds;
            }
        }

        private static readonly Dictionary<string, ClipSpec[]> Files = new Dictionary<string, ClipSpec[]>
        {
            { "Holding Down for Toilet (only first second needed)", new[] { new ClipSpec(SurvivalAnimClips.Toilet, 0f, 1f, false, 1f) } },
            { "Drinking (second half needed)", new[] { new ClipSpec(SurvivalAnimClips.Drink, 0.5f, 1f, false) } },
            { "Fishing Cast (second half reeling)", new[] { new ClipSpec(SurvivalAnimClips.FishCast, 0f, 0.5f, false), new ClipSpec(SurvivalAnimClips.FishReel, 0.5f, 1f, false) } },
            { "Fishing Idle", new[] { new ClipSpec(SurvivalAnimClips.FishIdle, 0f, 1f, true) } },
            { "Gathering Objects", new[] { new ClipSpec(SurvivalAnimClips.Gather, 0f, 1f, false) } },
            { "Kneel to Fire", new[] { new ClipSpec(SurvivalAnimClips.KneelFire, 0f, 1f, false, 0f, true) } },
            { "Laying Sleeping", new[] { new ClipSpec(SurvivalAnimClips.Sleep, 0f, 1f, true, 0f, true) } },
            { "Breathing Idle", new[] { new ClipSpec(SurvivalAnimClips.Breath, 0f, 1f, true) } },
            { "Walking and carry heavy", new[] { new ClipSpec(SurvivalAnimClips.HeavyWalk, 0f, 1f, true) } },
            { "Hit Reaction", new[] { new ClipSpec(SurvivalAnimClips.NpcHit, 0f, 1f, false) } },
            { "Rifle Death", new[] { new ClipSpec(SurvivalAnimClips.NpcDeath, 0f, 1f, false) } },
            { "Standing", new[] { new ClipSpec(SurvivalAnimClips.NpcStand, 0f, 1f, true) } },
            { "Talking", new[] { new ClipSpec(SurvivalAnimClips.NpcTalk1, 0f, 1f, true) } },
            { "Talking (1)", new[] { new ClipSpec(SurvivalAnimClips.NpcTalk2, 0f, 1f, true) } },
            // Imported for later (other NPCs, smoking design): not hooked yet
            { "Start Walking", new[] { new ClipSpec("Npc_StartWalk", 0f, 1f, false) } },
            { "Walking", new[] { new ClipSpec("Npc_Walk", 0f, 1f, true) } },
            { "Smoking", new[] { new ClipSpec(SurvivalAnimClips.Smoke, 0f, 1f, false) } },
        };

        // Full body layer: SurvAction value -> clip
        private static readonly (SurvAnim action, string clip)[] LayerStates =
        {
            (SurvAnim.Toilet, SurvivalAnimClips.Toilet),
            (SurvAnim.Drink, SurvivalAnimClips.Drink),
            (SurvAnim.FishCast, SurvivalAnimClips.FishCast),
            (SurvAnim.FishIdle, SurvivalAnimClips.FishIdle),
            (SurvAnim.FishReel, SurvivalAnimClips.FishReel),
            (SurvAnim.Gather, SurvivalAnimClips.Gather),
            (SurvAnim.KneelFire, SurvivalAnimClips.KneelFire),
            (SurvAnim.Sleep, SurvivalAnimClips.Sleep),
            (SurvAnim.Breath, SurvivalAnimClips.Breath),
            (SurvAnim.HeavyWalk, SurvivalAnimClips.HeavyWalk),
            (SurvAnim.Smoke, SurvivalAnimClips.Smoke),
            (SurvAnim.Death, SurvivalAnimClips.NpcDeath),
        };

        private static readonly List<string> Log = new List<string>();

        [MenuItem("Endless Survival/Setup Survival Animations")]
        public static void RunMenu()
        {
            Run();
            EditorUtility.DisplayDialog("Survival Animations", string.Join("\n", Log), "OK");
        }

        public static void RunBatch()
        {
            Run();
            foreach (var line in Log) Debug.Log("[SurvivalAnimationSetup] " + line);
        }

        private static void Run()
        {
            Log.Clear();
            FixOtherMixamoFiles();
            var clips = ImportClips();
            foreach (var controller in HumanoidControllers()) BuildLayer(controller, clips);
            var traderController = BuildTraderController(clips);
            BuildTraderPrefab(traderController);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log.Add("Done.");
        }

        // ---------------- import ----------------

        private static Dictionary<string, AnimationClip> ImportClips()
        {
            var result = new Dictionary<string, AnimationClip>();
            foreach (var pair in Files)
            {
                string path = $"{Dir}/{pair.Key}.fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    Log.Add("MISSING file: " + path);
                    continue;
                }

                if (importer.animationType != ModelImporterAnimationType.Human)
                {
                    importer.animationType = ModelImporterAnimationType.Human;
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    importer.SaveAndReimport();
                }
                ApplyRestPose(importer, pair.Key + ".fbx");

                var source = importer.defaultClipAnimations;
                if (source == null || source.Length == 0)
                {
                    Log.Add("NO clip in: " + path);
                    continue;
                }
                var src = source[0];
                float fps = 30f;
                var existing = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (existing != null && existing.frameRate > 1f) fps = existing.frameRate;
                float total = src.lastFrame - src.firstFrame;

                var list = new List<ModelImporterClipAnimation>();
                foreach (var spec in pair.Value)
                {
                    var c = new ModelImporterClipAnimation
                    {
                        name = spec.name,
                        takeName = src.takeName,
                        firstFrame = src.firstFrame + total * spec.from,
                        lastFrame = spec.seconds > 0f
                            ? Mathf.Min(src.lastFrame, src.firstFrame + total * spec.from + spec.seconds * fps)
                            : src.firstFrame + total * spec.to,
                        loopTime = spec.loop,
                        loopPose = spec.loop,
                        // Root: keep the clip's facing and height in the pose (crouch, lying, falling); drop the forward drift
                        lockRootRotation = true,
                        keepOriginalOrientation = true,
                        lockRootHeightY = true,
                        keepOriginalPositionY = !spec.feet,
                        heightFromFeet = spec.feet,
                        lockRootPositionXZ = false,
                    };
                    list.Add(c);
                }
                importer.clipAnimations = list.ToArray();
                importer.SaveAndReimport();

                var imported = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
                foreach (var spec in pair.Value)
                {
                    var clip = imported.FirstOrDefault(c => c.name == spec.name);
                    if (clip == null) { Log.Add("Clip not created: " + spec.name); continue; }
                    result[spec.name] = clip;
                    Log.Add($"Clip {spec.name}: {clip.length:0.00}s{(spec.loop ? " loop" : "")} (from '{pair.Key}', source {total / fps:0.00}s)");
                }
            }
            return result;
        }

        // ---------------- avatar T-pose ----------------

        private const string RestPoseFile = Dir + "/MixamoRestPose.txt";
        private static Dictionary<string, Dictionary<string, Quaternion>> _restPose;

        /// <summary>
        /// Mixamo "without skin" files carry no bind pose, so Unity guesses the avatar's T-pose from the first frame of
        /// the animation. For clips that start crouched or lying (toilet, kneel, sleep) the guess is far off and every
        /// pose gets retargeted wrong (upright torso, straight legs, twisted arms). MixamoRestPose.txt holds the real
        /// rest pose (local bone rotations, already in Unity axes) read from the same FBX files with Blender
        /// (Tools/Blender/mixamo_rest_pose_dump.py). It is written into the avatar's skeleton before the clips import.
        /// </summary>
        /// <summary>The older Mixamo clips in Assets/Animations (rifle idle/walk/run/aim/fire/reload, sitting) have the same problem.</summary>
        private static void FixOtherMixamoFiles()
        {
            ApplyRestPose(null, null); // loads the data
            foreach (var fileName in _restPose.Keys.ToList())
            {
                string path = "Assets/Animations/" + fileName;
                if (!File.Exists(path)) continue;
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null || importer.animationType != ModelImporterAnimationType.Human) continue;
                ApplyRestPose(importer, fileName);
            }
        }

        private static void ApplyRestPose(ModelImporter importer, string fileName)
        {
            if (_restPose == null)
            {
                _restPose = new Dictionary<string, Dictionary<string, Quaternion>>();
                if (File.Exists(RestPoseFile))
                {
                    foreach (var line in File.ReadAllLines(RestPoseFile))
                    {
                        var p = line.Split('|');
                        if (p.Length < 6) continue;
                        if (!_restPose.TryGetValue(p[0], out var bones)) _restPose[p[0]] = bones = new Dictionary<string, Quaternion>();
                        var ci = System.Globalization.CultureInfo.InvariantCulture;
                        bones[p[1]] = new Quaternion(float.Parse(p[2], ci), float.Parse(p[3], ci), float.Parse(p[4], ci), float.Parse(p[5], ci));
                    }
                }
            }
            if (importer == null) return;
            if (!_restPose.TryGetValue(fileName, out var rest))
            {
                Log.Add("No rest pose data for " + fileName + " (avatar T-pose left as Unity guessed it)");
                return;
            }
            var hd = importer.humanDescription;
            if (hd.skeleton == null || hd.skeleton.Length == 0)
            {
                // Older imports keep no skeleton in the .meta (Unity rebuilds its guess on every import): take it from the avatar
                var avatar = AssetDatabase.LoadAllAssetsAtPath(importer.assetPath).OfType<Avatar>().FirstOrDefault();
                if (avatar == null || !avatar.isHuman) { Log.Add("No humanoid avatar in " + fileName); return; }
                hd = avatar.humanDescription;
                if (hd.skeleton == null || hd.skeleton.Length == 0) return;
            }
            var skeleton = hd.skeleton;
            int changed = 0;
            for (int i = 0; i < skeleton.Length; i++)
            {
                if (!rest.TryGetValue(skeleton[i].name, out var q)) continue;
                if (Quaternion.Angle(skeleton[i].rotation, q) > 0.01f) changed++;
                skeleton[i].rotation = q;
            }
            if (changed == 0) return;
            hd.skeleton = skeleton;
            importer.humanDescription = hd;
            importer.SaveAndReimport();
            Log.Add($"T-pose fixed in {fileName} ({changed} bones)");
        }

        // ---------------- controllers ----------------

        private static IEnumerable<AnimatorController> HumanoidControllers()
        {
            var set = new HashSet<AnimatorController>();
            var player = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
            if (player != null) set.Add(player);
            var settings = AssetDatabase.LoadAssetAtPath<SurvivalSettings>("Assets/Resources/SurvivalSettings.asset");
            var enemyPrefabs = new List<GameObject>();
            if (settings != null && settings.humanEnemyPrefabs != null) enemyPrefabs.AddRange(settings.humanEnemyPrefabs.Where(p => p != null));
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Enemies" }))
                enemyPrefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)));
            foreach (var prefab in enemyPrefabs)
            {
                if (prefab == null) continue;
                foreach (var a in prefab.GetComponentsInChildren<Animator>(true))
                    if (a.runtimeAnimatorController is AnimatorController ac) set.Add(ac);
            }
            foreach (var c in set) Log.Add("Controller: " + AssetDatabase.GetAssetPath(c));
            return set;
        }

        private static void BuildLayer(AnimatorController controller, Dictionary<string, AnimationClip> clips)
        {
            if (!controller.parameters.Any(p => p.name == SurvivalAnimLayer.Param))
                controller.AddParameter(SurvivalAnimLayer.Param, AnimatorControllerParameterType.Int);

            for (int i = controller.layers.Length - 1; i >= 0; i--)
                if (controller.layers[i].name == SurvivalAnimLayer.LayerName) controller.RemoveLayer(i);

            controller.AddLayer(SurvivalAnimLayer.LayerName);
            var layers = controller.layers;
            int index = layers.Length - 1;
            layers[index].avatarMask = null; // full body
            layers[index].defaultWeight = 0f;
            layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[index].iKPass = false;
            controller.layers = layers;

            var sm = controller.layers[index].stateMachine;
            var empty = sm.AddState("Empty", new Vector3(300, -120));
            empty.writeDefaultValues = false;
            sm.defaultState = empty;

            int row = 0;
            foreach (var (action, clipName) in LayerStates)
            {
                if (!clips.TryGetValue(clipName, out var clip)) { Log.Add("  skip state (no clip): " + clipName); continue; }
                var state = sm.AddState(action.ToString(), new Vector3(620, row++ * 60 - 120));
                state.motion = clip;
                state.writeDefaultValues = false;

                var enter = sm.AddAnyStateTransition(state);
                enter.AddCondition(AnimatorConditionMode.Equals, (int)action, SurvivalAnimLayer.Param);
                enter.hasExitTime = false;
                enter.duration = action == SurvAnim.Death ? 0.08f : 0.2f;
                enter.canTransitionToSelf = false;

                var back = state.AddTransition(empty);
                back.AddCondition(AnimatorConditionMode.Equals, 0, SurvivalAnimLayer.Param);
                back.hasExitTime = false;
                back.duration = 0.15f;
            }

            // Enemy hit reaction: the user's Mixamo clip on the existing Actions layer
            if (clips.TryGetValue(SurvivalAnimClips.NpcHit, out var hit))
            {
                foreach (var layer in controller.layers)
                {
                    if (layer.name != "Actions") continue;
                    foreach (var child in layer.stateMachine.states)
                    {
                        if (child.state.name != "Hit React") continue;
                        child.state.motion = hit;
                        Log.Add("  Hit React -> " + SurvivalAnimClips.NpcHit);
                    }
                }
            }

            EditorUtility.SetDirty(controller);
            Log.Add($"Layer {SurvivalAnimLayer.LayerName} built on {controller.name} ({row} states).");
        }

        // ---------------- trader ----------------

        private static AnimatorController BuildTraderController(Dictionary<string, AnimationClip> clips)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(TraderControllerPath) != null) AssetDatabase.DeleteAsset(TraderControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(TraderControllerPath);
            controller.AddParameter(TraderAnimator.TalkParam, AnimatorControllerParameterType.Int);
            var sm = controller.layers[0].stateMachine;

            clips.TryGetValue(SurvivalAnimClips.NpcStand, out var standClip);
            var stand = sm.AddState("Standing", new Vector3(300, 0));
            stand.motion = standClip;
            sm.defaultState = stand;

            AddTalk(sm, stand, "Talking", clips, SurvivalAnimClips.NpcTalk1, 1, 80);
            AddTalk(sm, stand, "Talking 2", clips, SurvivalAnimClips.NpcTalk2, 2, 160);
            Log.Add("Trader controller: " + TraderControllerPath);
            return controller;
        }

        private static void AddTalk(AnimatorStateMachine sm, AnimatorState stand, string name, Dictionary<string, AnimationClip> clips, string clipName, int value, float y)
        {
            clips.TryGetValue(clipName, out var clip);
            var state = sm.AddState(name, new Vector3(600, y));
            state.motion = clip;
            var enter = sm.AddAnyStateTransition(state);
            enter.AddCondition(AnimatorConditionMode.Equals, value, TraderAnimator.TalkParam);
            enter.hasExitTime = false;
            enter.duration = 0.3f;
            enter.canTransitionToSelf = false;
            var back = state.AddTransition(stand);
            back.AddCondition(AnimatorConditionMode.Equals, 0, TraderAnimator.TalkParam);
            back.hasExitTime = false;
            back.duration = 0.3f;
        }

        private static void BuildTraderPrefab(AnimatorController controller)
        {
            // The trader uses the same humanoid model as the enemies (found through an enemy's avatar)
            GameObject model = null;
            Avatar avatar = null;
            var settings = AssetDatabase.LoadAssetAtPath<SurvivalSettings>("Assets/Resources/SurvivalSettings.asset");
            var candidates = new List<GameObject>();
            if (settings != null && settings.humanEnemyPrefabs != null) candidates.AddRange(settings.humanEnemyPrefabs.Where(p => p != null));
            foreach (var prefab in candidates)
            {
                var a = prefab.GetComponentInChildren<Animator>(true);
                if (a == null || a.avatar == null || !a.avatar.isHuman) continue;
                string avatarPath = AssetDatabase.GetAssetPath(a.avatar);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath);
                if (go != null && go.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                {
                    model = go;
                    avatar = a.avatar;
                    break;
                }
            }
            if (model == null)
            {
                Log.Add("Trader prefab: no humanoid model found on the enemy prefabs, trader stays a capsule.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(TraderPrefabDir)) AssetDatabase.CreateFolder("Assets/Prefabs", "Survival");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "TraderNPC";
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;

            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // Own colour so the trader does not look like the player or an enemy
            var mat = AssetDatabase.LoadAssetAtPath<Material>(TraderMaterialPath);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, TraderMaterialPath);
            }
            var olive = new Color(0.42f, 0.38f, 0.26f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", olive);
            mat.color = olive;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
            EditorUtility.SetDirty(mat);
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
            foreach (var col in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);

            PrefabUtility.SaveAsPrefabAsset(instance, TraderPrefabPath);
            Object.DestroyImmediate(instance);

            if (settings != null)
            {
                settings.traderModelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TraderPrefabPath);
                EditorUtility.SetDirty(settings);
            }
            Log.Add($"Trader prefab: {TraderPrefabPath} (model {AssetDatabase.GetAssetPath(model)})");
        }
    }
}
