using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace EndlessSurvival.Survival.Editor
{
    /// <summary>
    /// Adds the SurvivalUpperBody layer to the character controller: the same clips as the full body survival layer
    /// but masked to the waist and up (WeaponUpperBodyMask), so the legs keep walking. Used for smoking and drinking
    /// (the user, 2026-11-12: "only the upper body needs the smoking animation, the character can walk meanwhile").
    /// Only this one layer is added, right above SurvivalFullBody; no other layer is touched. Runs by itself after a
    /// compile when the layer is missing (not in play mode). Menu: Endless Survival/Setup Survival Upper Body Layer.
    /// </summary>
    public static class SurvivalUpperLayerSetup
    {
        private const string ControllerPath = "Assets/Animations/Character/CharacterAnimator.controller";
        private const string MaskPath = "Assets/Animations/WeaponUpperBodyMask.mask";

        // SurvUpper value -> clip (the values are SurvAnim)
        private static readonly (SurvAnim action, string clip)[] States =
        {
            (SurvAnim.Smoke, SurvivalAnimClips.Smoke),
            (SurvAnim.Drink, SurvivalAnimClips.Drink),
        };

        [InitializeOnLoadMethod]
        private static void Auto()
        {
            EditorApplication.delayCall += TryAuto;
        }

        private static void TryAuto()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null || HasLayer(controller)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorApplication.playModeStateChanged -= OnPlayMode;
                EditorApplication.playModeStateChanged += OnPlayMode;
                return;
            }
            Debug.Log("[SurvivalUpperLayerSetup] (auto) " + Build(controller));
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.delayCall += TryAuto;
        }

        [MenuItem("Endless Survival/Setup Survival Upper Body Layer")]
        public static void RunMenu()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            string msg = controller == null ? "Controller not found" : Build(controller);
            EditorUtility.DisplayDialog("Survival Upper Body Layer", msg, "OK");
        }

        public static void RunBatch()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Debug.Log("[SurvivalUpperLayerSetup] " + (controller == null ? "Controller not found" : Build(controller)));
        }

        private static bool HasLayer(AnimatorController controller) =>
            controller.layers.Any(l => l.name == SurvivalAnimLayer.UpperLayerName);

        private static string Build(AnimatorController controller)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask == null) return "WeaponUpperBodyMask not found (run Tools > Combat > Setup Package 1)";

            // Clips already used by the full body layer
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var c in controller.animationClips)
                if (c != null && !clips.ContainsKey(c.name)) clips[c.name] = c;

            if (!controller.parameters.Any(p => p.name == SurvivalAnimLayer.UpperParam))
                controller.AddParameter(SurvivalAnimLayer.UpperParam, AnimatorControllerParameterType.Int);

            // Remove an old copy, remember the other layers in order
            var layers = controller.layers.Where(l => l.name != SurvivalAnimLayer.UpperLayerName).ToList();

            var sm = new AnimatorStateMachine { name = SurvivalAnimLayer.UpperLayerName, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, controller);
            var empty = sm.AddState("Empty", new Vector3(300, -120));
            empty.writeDefaultValues = false;
            sm.defaultState = empty;

            int row = 0;
            foreach (var (action, clipName) in States)
            {
                if (!clips.TryGetValue(clipName, out AnimationClip clip)) continue;
                var state = sm.AddState(action.ToString(), new Vector3(620, row++ * 60 - 120));
                state.motion = clip;
                state.writeDefaultValues = false;

                var enter = sm.AddAnyStateTransition(state);
                enter.hasExitTime = false;
                enter.duration = 0.2f;
                enter.canTransitionToSelf = false;
                enter.AddCondition(AnimatorConditionMode.Equals, (int)action, SurvivalAnimLayer.UpperParam);

                var back = state.AddTransition(empty);
                back.hasExitTime = false;
                back.duration = 0.2f;
                back.AddCondition(AnimatorConditionMode.NotEqual, (int)action, SurvivalAnimLayer.UpperParam);
            }
            if (row == 0)
            {
                Object.DestroyImmediate(sm, true);
                return "Surv_Smoke / Surv_Drink clips are not in the controller (run Endless Survival > Setup Survival Animations first)";
            }

            var layer = new AnimatorControllerLayer
            {
                name = SurvivalAnimLayer.UpperLayerName,
                stateMachine = sm,
                avatarMask = mask,
                defaultWeight = 0f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                iKPass = false
            };

            // Right above the full body survival layer (below the combat layers)
            int at = layers.FindIndex(l => l.name == SurvivalAnimLayer.LayerName);
            if (at < 0) at = layers.Count - 1;
            layers.Insert(at + 1, layer);
            controller.layers = layers.ToArray();

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return $"Layer {SurvivalAnimLayer.UpperLayerName} added to {controller.name} ({row} states, above {SurvivalAnimLayer.LayerName}). Other layers untouched.";
        }
    }
}
