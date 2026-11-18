using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace EndlessCombat.Character.Editor
{
    /// <summary>
    /// Mixamo trial (2026-11-18): the user downloaded Mixamo's "Walking" (Without Skin, 60 fps) to compare with our
    /// procedural walk. This puts it in the player's locomotion blend tree in place of Survivor_Walk:
    ///   Survivor_Idle 0 / Mixamo_Walking at its own ground speed / Survivor_Run 4.0.
    /// The Mixamo file gets its own humanoid avatar (Unity auto-maps the mixamorig bones); humanoid clips retarget
    /// through muscle space, so it plays on the Survivor body. The forward travel stays in the clip as root motion
    /// (not baked into the pose); the player's Animator has root motion off, so the body stays in place and the
    /// clip's average speed gives the blend threshold, so the feet do not skate.
    /// No dialog: safe to call through the Unity CLI / MCP while the Editor is open.
    /// "Animations: Install Survivor (idle, walk, run)" puts our own walk back.
    /// </summary>
    public static class MixamoClipSetup
    {
        public const string WalkPath = "Assets/_AssetPacks/Animations/Mixamo/Walking.fbx";
        public const string WalkClip = "Mixamo_Walking";
        private const string IdlePath = SurvivorCharacterSetup.Folder + "/Animations/Survivor_Idle.fbx";
        private const string RunPath = SurvivorCharacterSetup.Folder + "/Animations/Survivor_Run.fbx";

        // Run and sprint picked by the user on Mixamo (2026-11-18, previewed on the uploaded Survivor, In Place,
        // Without Skin, 60 fps): "Jog Forward" (soccer jog) = normal run, "Standard Run" = sprint.
        public const string JogPath = "Assets/_AssetPacks/Animations/Mixamo/Jog Forward.fbx";
        public const string JogClip = "Mixamo_JogForward";
        public const string SprintPath = "Assets/_AssetPacks/Animations/Mixamo/Standard Run.fbx";
        public const string SprintClip = "Mixamo_StandardRun";

        [MenuItem("Endless Survival/Animations: Mixamo walk test (Walking.fbx)")]
        public static void Run()
        {
            var log = new List<string>();
            AnimationClip walk = Setup(WalkPath, WalkClip, log, out float speed);
            AnimationClip idle = LoadClip(IdlePath);
            AnimationClip run = LoadClip(RunPath);
            if (walk == null || idle == null) { foreach (string l in log) Debug.LogWarning("[MixamoClipSetup] " + l); return; }
            var children = new List<(AnimationClip clip, float threshold, float timeScale)> { (idle, 0f, 1f), (walk, speed, 1f) };
            if (run != null && speed < 3.9f) children.Add((run, 4.0f, 1f));
            SurvivorIdleSetup.Log.Clear();
            SurvivorIdleSetup.SetupController(children);
            log.AddRange(SurvivorIdleSetup.Log);
            foreach (string l in log) Debug.Log("[MixamoClipSetup] " + l);
        }

        /// <summary>
        /// Full Mixamo locomotion: Survivor_Idle 0 / Walking 1.5 / Jog Forward 4.0 (PlayerMotor.MoveSpeed) /
        /// Standard Run 6.2 (PlayerMotor.SprintSpeed). The files are In Place, so their ground speed cannot be read
        /// from root motion: the thresholds are the motor's speeds; if the feet skate in game, tune those numbers.
        /// </summary>
        [MenuItem("Endless Survival/Animations: Mixamo locomotion (walk, jog, sprint)")]
        public static void RunLocomotion()
        {
            var log = new List<string>();
            AnimationClip idle = LoadClip(IdlePath);
            AnimationClip walk = Setup(WalkPath, WalkClip, log, out float walkSpeed);
            AnimationClip jog = Setup(JogPath, JogClip, log, out _);
            AnimationClip sprint = Setup(SprintPath, SprintClip, log, out _);
            if (idle == null || walk == null || jog == null || sprint == null)
            {
                foreach (string l in log) Debug.LogWarning("[MixamoClipSetup] " + l);
                return;
            }
            var children = new List<(AnimationClip clip, float threshold, float timeScale)>
            {
                (idle, 0f, 1f), (walk, walkSpeed, 1f), (jog, 4.0f, 1f), (sprint, 6.2f, 1f),
            };
            SurvivorIdleSetup.Log.Clear();
            SurvivorIdleSetup.SetupController(children);
            log.AddRange(SurvivorIdleSetup.Log);
            foreach (string l in log) Debug.Log("[MixamoClipSetup] " + l);
        }

        private static AnimationClip LoadClip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview__"));

        // ------------------------------------------------------------------ jump (2026-11-18, Mixamo "Unarmed Jump")

        public const string JumpPath = "Assets/_AssetPacks/Animations/Mixamo/Unarmed Jump.fbx";
        public const string JumpClip = "Mixamo_UnarmedJump";

        /// <summary>
        /// Locomotion + jump. PlayerMotor sets Jump for the frame it launches (cleared again once Grounded), FreeFall
        /// after 0.15 s without ground, Grounded every frame. The Mixamo clip is a standing jump: crouch, push, air,
        /// land, recover. The crouch is cut (physics already leaves the ground on the press), the body's vertical
        /// travel goes to root motion (thrown away: the capsule does the height), and the state's speed makes the
        /// clip's air time equal the physics air time (2 * sqrt(2 h / -g) with PlayerMotor's defaults).
        /// </summary>
        [MenuItem("Endless Survival/Animations: Mixamo locomotion + jump")]
        public static void RunLocomotionAndJump()
        {
            RunLocomotion();
            var log = new List<string>();
            SetupJump(log);
            foreach (string l in log) Debug.Log("[MixamoClipSetup] " + l);
        }

        const string JumpCalmPath = "Assets/Animations/Player/Mixamo_UnarmedJump_Calm.anim";
        // The jump's shoulder muscles go to -1.65 (front-back) and swing over 1.0 (down-up): shoulders up to the ears
        // and rolling forward and back. The user found that silly (2026-11-18); keep only this share of it.
        const float ShoulderKeep = 0.3f;

        /// <summary>
        /// Copies an imported humanoid clip into a .anim with every "Shoulder" muscle curve scaled toward 0
        /// (muscle 0 = the middle of the avatar's range, a relaxed shoulder). Everything else is copied as is.
        /// </summary>
        private static AnimationClip CalmShoulders(AnimationClip src, string path, float keep, List<string> log)
        {
            var dst = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (dst == null) { dst = new AnimationClip(); AssetDatabase.CreateAsset(dst, path); }
            dst.ClearCurves();
            dst.frameRate = src.frameRate;
            int scaled = 0;
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(src))
            {
                AnimationCurve c = AnimationUtility.GetEditorCurve(src, b);
                if (b.type == typeof(Animator) && b.propertyName.Contains("Shoulder"))
                {
                    Keyframe[] k = c.keys;
                    for (int i = 0; i < k.Length; i++)
                    {
                        k[i].value *= keep; k[i].inTangent *= keep; k[i].outTangent *= keep;
                    }
                    c.keys = k;
                    scaled++;
                }
                AnimationUtility.SetEditorCurve(dst, b, c);
            }
            AnimationUtility.SetAnimationClipSettings(dst, AnimationUtility.GetAnimationClipSettings(src));
            EditorUtility.SetDirty(dst);
            AssetDatabase.SaveAssets();
            log.Add($"{dst.name}: {scaled} shoulder curves scaled x{keep:0.00} (from {src.name}).");
            return dst;
        }

        private static void SetupJump(List<string> log)
        {
            AnimationClip clip = Setup(JumpPath, JumpClip, log, out _, loop: false, bakeHeight: false);
            if (clip == null) return;
            var imp = (ModelImporter)AssetImporter.GetAtPath(JumpPath);

            // airborne window from the body height curve (root motion Y)
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y");
            AnimationCurve y = AnimationUtility.GetEditorCurve(clip, binding);
            float len = clip.length, takeoff = 0f, landing = len;
            if (y != null && y.length > 1)
            {
                float baseY = y.Evaluate(0f);
                const float step = 1f / 120f;
                bool found = false;
                for (float t = 0f; t <= len; t += step)
                {
                    bool up = y.Evaluate(t) > baseY + 0.06f;
                    if (up && !found) { takeoff = t; found = true; }
                    if (up) landing = t;
                }
                if (!found) { takeoff = 0f; landing = len; log.Add(JumpClip + ": no airborne part found in RootT.y, clip used whole."); }
            }
            else log.Add(JumpClip + ": no RootT.y curve, clip used whole.");

            float start = Mathf.Max(0f, takeoff - 0.06f);
            float end = Mathf.Min(len, landing + 0.30f);
            ModelImporterClipAnimation[] cs = imp.clipAnimations;
            float fps = clip.frameRate;
            float origFirst = cs[0].firstFrame;
            cs[0].firstFrame = origFirst + start * fps;
            cs[0].lastFrame = origFirst + end * fps;
            imp.clipAnimations = cs;
            imp.SaveAndReimport();
            clip = AssetDatabase.LoadAllAssetsAtPath(JumpPath).OfType<AnimationClip>().First(x => !x.name.StartsWith("__preview__"));

            const float jumpHeight = 1.1f, gravity = -18f;           // PlayerMotor defaults
            float physicsAir = 2f * Mathf.Sqrt(2f * jumpHeight / -gravity);
            float clipAir = Mathf.Max(0.1f, landing - takeoff);
            float speed = Mathf.Clamp(clipAir / physicsAir, 0.5f, 2f);
            float landNorm = Mathf.Clamp01((landing - start) / Mathf.Max(0.01f, end - start));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(SurvivorIdleSetup.ControllerPath);
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            AnimatorState loco = sm.states.Select(s => s.state).First(s => s.name == "Locomotion");
            AnimatorState jump = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Jump") ?? sm.AddState("Jump", new Vector3(300, 120, 0));
            jump.motion = CalmShoulders(clip, JumpCalmPath, ShoulderKeep, log);
            jump.speed = speed;
            jump.writeDefaultValues = true;
            jump.iKOnFeet = false;
            foreach (var t in loco.transitions.Where(t => t.destinationState == jump).ToArray()) loco.RemoveTransition(t);
            foreach (var t in jump.transitions.ToArray()) jump.RemoveTransition(t);

            AnimatorStateTransition Go(AnimatorState from, AnimatorState to, float dur, bool exit, float exitTime)
            {
                var tr = from.AddTransition(to);
                tr.hasExitTime = exit;
                tr.exitTime = exitTime;
                tr.hasFixedDuration = true;
                tr.duration = dur;
                tr.canTransitionToSelf = false;
                return tr;
            }
            Go(loco, jump, 0.08f, false, 0f).AddCondition(AnimatorConditionMode.If, 0, "Jump");
            Go(loco, jump, 0.15f, false, 0f).AddCondition(AnimatorConditionMode.If, 0, "FreeFall");   // walked off a ledge
            // Back on the ground: right away, no exit time. With an exit time, Unity checks the condition only when
            // the clip crosses that time; mid-air it fails, so the state waited for the next pass and the feet hung
            // in the air after landing. PlayerMotor keeps Jump on while rising, so take-off frames don't end it.
            var back = Go(jump, loco, 0.12f, false, 0f);
            back.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            back.AddCondition(AnimatorConditionMode.IfNot, 0, "Jump");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            log.Add($"{JumpClip}: trimmed {start:0.00}-{end:0.00} s of {len:0.00} (air {takeoff:0.00}-{landing:0.00}), " +
                    $"state speed x{speed:0.00} (clip air {clipAir:0.00} s / physics air {physicsAir:0.00} s). " +
                    "Transitions: Locomotion->Jump on Jump or FreeFall, Jump->Locomotion on Grounded.");
        }

        private static AnimationClip Setup(string fbxPath, string clipName, List<string> log, out float speed,
                                           bool loop = true, bool bakeHeight = true)
        {
            speed = 1.5f;
            if (!File.Exists(fbxPath) || !(AssetImporter.GetAtPath(fbxPath) is ModelImporter imp)) { log.Add("Missing " + fbxPath); return null; }
            imp.importBlendShapes = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.importAnimation = true;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.sourceAvatar = null;
            imp.SaveAndReimport();
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman) { log.Add(clipName + ": humanoid avatar could not be built from " + fbxPath); return null; }

            // Real T-pose for the avatar (2026-11-18). A "Without Skin" Mixamo file has no bind pose, so Unity took the
            // clip's first frame as the T-pose (walk: knee bent ~33 deg, foot pointed, fingers closed). In muscle space
            // that offset went into every clip: on the Survivor the knees stayed ~33 deg straighter, feet and thumbs off.
            // Mixamo rigs are built so the T-pose is every mixamorig joint at zero local rotation (direction lives in
            // the offsets), so the reference pose = file positions with identity rotations.
            // A file retargeted onto our rig in Blender (bone names pelvis, upperarm_l...) gets the Survivor T-pose,
            // exactly as our own clips (SurvivorIdleSetup.BuildSkeleton).
            HumanDescription hd = imp.humanDescription;
            SkeletonBone[] sk = hd.skeleton;
            int fixedBones = 0;
            string refKind;
            if (sk.Any(s => s.name == "pelvis"))
            {
                sk = SurvivorIdleSetup.BuildSkeleton(fbxPath);
                fixedBones = sk.Length;
                refKind = "Survivor T-pose (our rig)";
            }
            else
            {
                for (int i = 0; i < sk.Length; i++)
                    if (sk[i].name.StartsWith("mixamorig")) { sk[i].rotation = Quaternion.identity; fixedBones++; }
                refKind = "Mixamo T-pose (joints at zero rotation)";
            }
            hd.skeleton = sk;
            imp.humanDescription = hd;
            imp.SaveAndReimport();
            avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman) { log.Add(clipName + ": avatar INVALID after the T-pose fix"); return null; }
            log.Add($"{clipName}: avatar reference = {refKind}, {fixedBones} nodes.");

            ModelImporterClipAnimation[] takes = imp.defaultClipAnimations;
            if (takes == null || takes.Length == 0) { log.Add("No animation take in " + fbxPath); return null; }
            ModelImporterClipAnimation c = takes[0];
            c.name = clipName;
            c.loopTime = loop;
            c.loopPose = loop;
            c.lockRootRotation = true;          // no drift in heading
            c.keepOriginalOrientation = true;
            c.lockRootHeightY = bakeHeight;     // walk/run: the bob stays in the pose; jump: height -> root motion (dropped)
            c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = false;       // forward travel -> root motion (thrown away in game), measured below
            c.keepOriginalPositionXZ = false;
            c.heightFromFeet = false;
            imp.clipAnimations = new[] { c };
            imp.SaveAndReimport();

            AnimationClip clip = LoadClip(fbxPath);
            if (clip == null) { log.Add("Clip not found after import: " + fbxPath); return null; }
            Vector3 v = clip.averageSpeed;
            float ground = new Vector2(v.x, v.z).magnitude;
            if (ground > 0.3f) speed = ground;
            else log.Add(clipName + ": the file is In Place (no travel to measure), threshold left at 1.5 m/s.");
            log.Add($"{clipName}: Humanoid, {clip.length:0.00} s, {clip.frameRate:0} fps, ground speed {ground:0.00} m/s -> threshold {speed:0.00}.");
            return clip;
        }
    }
}
