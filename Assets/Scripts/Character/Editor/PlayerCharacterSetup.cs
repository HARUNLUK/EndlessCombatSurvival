using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace EndlessCombat.Character.Editor
{
    /// <summary>
    /// Installs the new main character (2026-11-13):
    ///   1. Scene: the player loses the Starter Assets ThirdPersonController, StarterAssetsInputs and PlayerInput
    ///      and gets PlayerMotor + PlayerInputState (the prefab link to PlayerArmature is cut).
    ///   2. Animator: the base locomotion state becomes a two dimensional blend (MoveX sideways, Speed forward)
    ///      of one pack's clips (Kevin Iglesias Human Animations): idle, walk and run in eight directions,
    ///      sprint, jump / fall / land. Enemies share the controller: they only drive Speed, so they get the
    ///      forward clips.
    /// Menu: Endless Survival/Setup Player Character. Batch: -executeMethod EndlessCombat.Character.Editor.PlayerCharacterSetup.RunBatch
    /// </summary>
    public static class PlayerCharacterSetup
    {
        private const string ControllerPath = "Assets/Animations/Character/CharacterAnimator.controller";
        private const string Move = "Assets/Kevin Iglesias/Human Animations/Animations/Male/Movement/";
        private const string Idles = "Assets/Kevin Iglesias/Human Animations/Animations/Male/Idles/";
        private const string TreeName = "PlayerLocomotion2D";

        public const float WalkSpeed = 2f, RunSpeed = 4f, SprintSpeed = 6.2f;

        private static readonly List<string> Log = new List<string>();

        [MenuItem("Endless Survival/Setup Player Character")]
        public static void RunMenu()
        {
            Run();
            EditorUtility.DisplayDialog("Player Character", string.Join("\n", Log), "OK");
        }

        public static void RunBatch()
        {
            Run();
            foreach (string l in Log) Debug.Log("[PlayerCharacterSetup] " + l);
        }

        private static void Run()
        {
            Log.Clear();
            BuildLocomotion();
            BuildLongGunStances();
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ animator

        private static AnimationClip Clip(string path, bool loop)
        {
            if (loop)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null)
                {
                    ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
                    if (clips.Any(c => !c.loopTime))
                    {
                        foreach (var c in clips) c.loopTime = true;
                        importer.clipAnimations = clips;
                        importer.SaveAndReimport();
                        Log.Add("loop time switched on: " + System.IO.Path.GetFileName(path));
                    }
                }
            }
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) Log.Add("MISSING clip: " + path);
            return clip;
        }

        /// <summary>
        /// Two handed long gun layer: rest, aim, shot and reload come from the same pack as the locomotion
        /// (assault rifle set), so the hands are where that pack holds a rifle and the gun is laid into them.
        /// </summary>
        private static void BuildLongGunStances()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) return;
            const string combat = "Assets/Kevin Iglesias/Human Animations/Animations/Male/Combat/AssaultRifle/";
            const string poses = "Assets/Kevin Iglesias/Human Animations/Animations/Masked Poses/";
            var swaps = new (string state, string file, bool loop)[]
            {
                ("Rifle Idle", poses + "HumanM@WeaponHold_AssaultRifle01.fbx", true),
                ("Rifle Aiming Idle", combat + "HumanM@AssaultRifle_Aim01.fbx", true),
                ("Firing Rifle", combat + "HumanM@AssaultRifle_Aim01_Shoot01.fbx", false),
                ("Reloading", combat + "HumanM@AssaultRifle_Reload01.fbx", false)
            };
            int done = 0;
            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;
                foreach (ChildAnimatorState cs in layer.stateMachine.states)
                    foreach (var s in swaps)
                    {
                        if (cs.state == null || cs.state.name != s.state) continue;
                        AnimationClip clip = Clip(s.file, s.loop);
                        if (clip == null) continue;
                        cs.state.motion = clip;
                        done++;
                        Log.Add("long gun: '" + s.state + "' <- " + clip.name + " (" + clip.length.ToString("0.00") + " s)");
                    }
            }
            EditorUtility.SetDirty(controller);
            if (done < swaps.Length) Log.Add("WARNING long gun stances: only " + done + " of " + swaps.Length + " states changed.");
        }

        private static void BuildLocomotion()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) { Log.Add("Animator controller not found: " + ControllerPath); return; }

            if (controller.parameters.All(p => p.name != "MoveX")) controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            AnimatorState loco = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Idle Walk Run Blend");
            if (loco == null) { Log.Add("Base state 'Idle Walk Run Blend' not found, locomotion unchanged."); return; }

            // Remove an earlier tree of ours
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (o is BlendTree bt && bt.name == TreeName) Object.DestroyImmediate(bt, true);

            var tree = new BlendTree
            {
                name = TreeName,
                blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = "MoveX",
                blendParameterY = "Speed",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            int added = 0;
            void Add(string file, float x, float y)
            {
                AnimationClip c = Clip(file, true);
                if (c == null) return;
                tree.AddChild(c, new Vector2(x, y));
                added++;
            }

            float w = WalkSpeed, r = RunSpeed, wd = WalkSpeed * 0.7071f, rd = RunSpeed * 0.7071f;
            Add(Idles + "HumanM@Idle01.fbx", 0f, 0f);
            Add(Move + "Walk/HumanM@Walk01_Forward.fbx", 0f, w);
            Add(Move + "Walk/HumanM@Walk01_Backward.fbx", 0f, -w);
            Add(Move + "Walk/HumanM@Walk01_Left.fbx", -w, 0f);
            Add(Move + "Walk/HumanM@Walk01_Right.fbx", w, 0f);
            Add(Move + "Walk/HumanM@Walk01_ForwardLeft.fbx", -wd, wd);
            Add(Move + "Walk/HumanM@Walk01_ForwardRight.fbx", wd, wd);
            Add(Move + "Walk/HumanM@Walk01_BackwardLeft.fbx", -wd, -wd);
            Add(Move + "Walk/HumanM@Walk01_BackwardRight.fbx", wd, -wd);
            Add(Move + "Run/HumanM@Run01_Forward.fbx", 0f, r);
            Add(Move + "Run/HumanM@Run01_Backward.fbx", 0f, -r);
            Add(Move + "Strafe/StrafeRun/HumanM@StrafeRun01_Left.fbx", -r, 0f);
            Add(Move + "Strafe/StrafeRun/HumanM@StrafeRun01_Right.fbx", r, 0f);
            Add(Move + "Strafe/StrafeRun/HumanM@StrafeRun01_ForwardLeft.fbx", -rd, rd);
            Add(Move + "Strafe/StrafeRun/HumanM@StrafeRun01_ForwardRight.fbx", rd, rd);
            Add(Move + "Strafe/StrafeRun/HumanM@StrafeRun01_BackwardLeft.fbx", -rd, -rd);
            Add(Move + "Strafe/StrafeRun/HumanM@StrafeRun01_BackwardRight.fbx", rd, -rd);
            Add(Move + "Sprint/HumanM@Sprint01_Forward.fbx", 0f, SprintSpeed);

            if (added < 10)
            {
                Object.DestroyImmediate(tree, true);
                Log.Add("Too few locomotion clips found (" + added + "), locomotion unchanged.");
                return;
            }
            loco.motion = tree;
            loco.speedParameterActive = false;
            loco.speed = 1f;
            loco.iKOnFeet = true;

            void Swap(string stateName, string file)
            {
                AnimatorState s = sm.states.Select(x => x.state).FirstOrDefault(x => x.name == stateName);
                AnimationClip c = Clip(file, stateName == "InAir");
                if (s != null && c != null) s.motion = c;
                else Log.Add("jump state not changed: " + stateName);
            }
            Swap("JumpStart", Move + "Jump/HumanM@Jump01 - Begin.fbx");
            Swap("InAir", Move + "Jump/HumanM@Fall01.fbx");
            Swap("JumpLand", Move + "Jump/HumanM@Jump01 - Land.fbx");

            EditorUtility.SetDirty(controller);
            Log.Add("Locomotion: " + added + " clips in the two dimensional blend (walk " + WalkSpeed + ", run " + RunSpeed + ", sprint " + SprintSpeed + " m/s).");
        }
    }
}
