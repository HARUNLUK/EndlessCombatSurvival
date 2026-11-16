using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace EndlessCombat.Combat.Editor
{
    /// <summary>
    /// One handed pistol (the user, 2026-11-12: "the pistol does not have to be fired with both hands").
    /// The pistol clips are two handed. Nothing is authored: the PistolUpperBody layer gets a mask without the left
    /// arm (PistolOneHandMask), so the left arm keeps swinging with the walk, and a new layer PistolLeftArm (left
    /// arm only, synced to PistolUpperBody = the same clips) brings the left hand to the gun only when the grip is
    /// switched to two hands (Y / V). CombatAnimator drives its weight.
    /// Runs by itself after a compile when the layer is missing. Menu: Tools/Combat/Setup One Handed Pistol.
    /// Note: Tools > Combat > Setup Package 1 rebuilds PistolUpperBody with the old mask; run this again after it.
    /// </summary>
    public static class PistolOneHandSetup
    {
        private const string ControllerPath = "Assets/Animations/Character/CharacterAnimator.controller";
        private const string SourceMaskPath = "Assets/Animations/WeaponUpperBodyMask.mask";
        private const string OneHandMaskPath = "Assets/Animations/PistolOneHandMask.mask";
        private const string LeftArmMaskPath = "Assets/Animations/ShieldArmMask.mask";

        [InitializeOnLoadMethod]
        private static void Auto() => EditorApplication.delayCall += TryAuto;

        private static void TryAuto()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null || IsDone(controller)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorApplication.playModeStateChanged -= OnPlayMode;
                EditorApplication.playModeStateChanged += OnPlayMode;
                return;
            }
            Debug.Log("[PistolOneHandSetup] (auto) " + Build(controller));
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.delayCall += TryAuto;
        }

        [MenuItem("Tools/Combat/Setup One Handed Pistol")]
        public static void RunMenu()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            EditorUtility.DisplayDialog("One Handed Pistol", controller == null ? "Controller not found" : Build(controller), "OK");
        }

        public static void RunBatch()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Debug.Log("[PistolOneHandSetup] " + (controller == null ? "Controller not found" : Build(controller)));
        }

        private static bool IsDone(AnimatorController controller)
        {
            var layers = controller.layers;
            var pistol = layers.FirstOrDefault(l => l.name == CombatAnimator.PistolLayerName);
            return layers.Any(l => l.name == CombatAnimator.PistolLeftArmLayerName)
                   && pistol != null && pistol.avatarMask != null && pistol.avatarMask.name == "PistolOneHandMask";
        }

        private static string Build(AnimatorController controller)
        {
            var source = AssetDatabase.LoadAssetAtPath<AvatarMask>(SourceMaskPath);
            var leftArm = AssetDatabase.LoadAssetAtPath<AvatarMask>(LeftArmMaskPath);
            if (source == null || leftArm == null) return "WeaponUpperBodyMask / ShieldArmMask not found (run the combat setups first)";

            var oneHand = AssetDatabase.LoadAssetAtPath<AvatarMask>(OneHandMaskPath);
            if (oneHand == null)
            {
                oneHand = new AvatarMask();
                AssetDatabase.CreateAsset(oneHand, OneHandMaskPath);
            }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                oneHand.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, source.GetHumanoidBodyPartActive((AvatarMaskBodyPart)i));
            oneHand.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, false);
            oneHand.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, false);
            oneHand.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, false);
            EditorUtility.SetDirty(oneHand);

            var layers = controller.layers.Where(l => l.name != CombatAnimator.PistolLeftArmLayerName).ToList();
            int pistolIndex = layers.FindIndex(l => l.name == CombatAnimator.PistolLayerName);
            if (pistolIndex < 0) return "PistolUpperBody layer not found (run Tools > Combat > Setup Package 1)";

            layers[pistolIndex].avatarMask = oneHand;

            // Layers after the new one move down by one: keep any synced index pointing at the same layer
            for (int i = 0; i < layers.Count; i++)
                if (layers[i].syncedLayerIndex > pistolIndex) layers[i].syncedLayerIndex++;

            var left = new AnimatorControllerLayer
            {
                name = CombatAnimator.PistolLeftArmLayerName,
                avatarMask = leftArm,
                defaultWeight = 0f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                syncedLayerIndex = pistolIndex,
                syncedLayerAffectsTiming = false,
                iKPass = false
            };
            layers.Insert(pistolIndex + 1, left);
            controller.layers = layers.ToArray();

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return $"PistolUpperBody now uses PistolOneHandMask (no left arm); layer {CombatAnimator.PistolLeftArmLayerName} added right above it, synced to it (left arm only, weight driven by the grip).";
        }
    }
}
