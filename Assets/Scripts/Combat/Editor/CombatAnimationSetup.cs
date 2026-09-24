using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace EndlessCombat.Combat.Editor
{
    [InitializeOnLoad]
    public static class CombatAnimationSetup
    {
        private const string RifleIdleFbxPath = "Assets/Animations/Rifle Idle.fbx";
        private const string ControllerPath = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";

        static CombatAnimationSetup()
        {
            EditorApplication.delayCall += ExecuteSetup;
        }

        [MenuItem("Tools/Combat/Setup Rifle Idle & Controller")]
        public static void ExecuteSetup()
        {
            bool reimported = ConfigureFbx(RifleIdleFbxPath);
            ApplyClipToAnimatorController();
        }

        private static bool ConfigureFbx(string assetPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null) return false;

            bool needsSave = false;

            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                needsSave = true;
            }

            ModelImporterClipAnimation[] defaultClips = importer.defaultClipAnimations;
            if (defaultClips != null && defaultClips.Length > 0)
            {
                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0)
                {
                    clips = defaultClips;
                }

                foreach (var clip in clips)
                {
                    clip.loopTime = true;
                }

                importer.clipAnimations = clips;
                needsSave = true;
            }

            if (needsSave)
            {
                importer.SaveAndReimport();
                return true;
            }

            return false;
        }

        private static void ApplyClipToAnimatorController()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogWarning("AnimatorController not found at: " + ControllerPath);
                return;
            }

            // Load the animation clip from Rifle Idle FBX
            AnimationClip rifleIdleClip = null;
            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(RifleIdleFbxPath);
            foreach (Object sub in subAssets)
            {
                if (sub is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    rifleIdleClip = clip;
                    break;
                }
            }

            if (rifleIdleClip == null)
            {
                Debug.LogWarning("AnimationClip could not be loaded from " + RifleIdleFbxPath + ". Please ensure FBX is imported.");
                return;
            }

            // Find UpperBody layer in controller
            for (int i = 0; i < controller.layers.Length; i++)
            {
                string layerName = controller.layers[i].name.Trim();
                if (layerName == "UpperBody")
                {
                    AnimatorStateMachine sm = controller.layers[i].stateMachine;

                    // Update the default state (DefaultEmpty or Rifle Idle)
                    ChildAnimatorState[] states = sm.states;
                    for (int s = 0; s < states.Length; s++)
                    {
                        string stateName = states[s].state.name.Trim();
                        if (stateName == "DefaultEmpty" || stateName == "Rifle Idle")
                        {
                            states[s].state.name = "Rifle Idle";
                            states[s].state.motion = rifleIdleClip;
                            EditorUtility.SetDirty(controller);
                            AssetDatabase.SaveAssets();
                            Debug.Log("Successfully assigned Rifle Idle animation to UpperBody default state!");
                            return;
                        }
                    }

                    // If default state wasn't named DefaultEmpty, set on sm.defaultState
                    if (sm.defaultState != null)
                    {
                        sm.defaultState.name = "Rifle Idle";
                        sm.defaultState.motion = rifleIdleClip;
                        EditorUtility.SetDirty(controller);
                        AssetDatabase.SaveAssets();
                        Debug.Log("Assigned Rifle Idle to UpperBody default state!");
                        return;
                    }
                }
            }
        }
    }
}
