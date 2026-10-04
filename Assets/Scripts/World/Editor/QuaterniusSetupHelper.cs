using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;

namespace EndlessSurvival.EditorTools
{
    public static class QuaterniusSetupHelper
    {
        [MenuItem("Tools/Setup Quaternius Animation Library")]
        public static void SetupLibrary()
        {
            string[] fbxFiles = new string[]
            {
                "Assets/_AssetPacks/Universal Animation Library 2[Standard]/Universal Animation Library 2[Standard]/Unity/UAL2_Standard.fbx",
                "Assets/_AssetPacks/Universal Animation Library[Standard]/Universal Animation Library[Standard]/Unity/UAL1_Standard.fbx"
            };

            foreach (var path in fbxFiles)
            {
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[QuaterniusSetup] Dosya bulunamadı: {path}");
                    continue;
                }

                ConfigureFbx(path);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Tamamlandı", 
                "Quaternius Animasyon Kütüphaneleri (UAL1 ve UAL2) başarıyla:\n- Bake Axis Conversion: Açık\n- Rig: Humanoid\n- Root Motion Node: Ayarlandı\n- _Loop animasyonları döngüye alındı!\n\nŞimdi 'Tools -> Apply Crouch Animations From Quaternius' seçeneğine tıklayabilirsiniz.", 
                "Tamam");
        }

        [MenuItem("Tools/Apply Crouch Animations From Quaternius")]
        public static void ApplyCrouchAnimations()
        {
            string[] fbxFiles = new string[]
            {
                "Assets/_AssetPacks/Universal Animation Library 2[Standard]/Universal Animation Library 2[Standard]/Unity/UAL2_Standard.fbx",
                "Assets/_AssetPacks/Universal Animation Library[Standard]/Universal Animation Library[Standard]/Unity/UAL1_Standard.fbx"
            };

            AnimationClip crouchIdleClip = null;
            AnimationClip crouchWalkClip = null;

            foreach (var path in fbxFiles)
            {
                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                if (assets == null) continue;

                foreach (var a in assets)
                {
                    if (a is AnimationClip clip && !clip.name.Contains("__preview__"))
                    {
                        string nameLower = clip.name.ToLower();
                        if (nameLower.Contains("crouch"))
                        {
                            Debug.Log($"[Quaternius] Bulunan Crouch Klibi: {clip.name} (Kaynak: {path})");

                            if (nameLower.Contains("walk") || nameLower.Contains("forward") || nameLower.Contains("run"))
                            {
                                crouchWalkClip = clip;
                            }
                            else if (nameLower.Contains("idle") || !nameLower.Contains("walk"))
                            {
                                crouchIdleClip = clip;
                            }
                        }
                    }
                }
            }

            // Eğer özel isim bulunamazsa herhangi bir crouch klibini al
            if (crouchIdleClip == null || crouchWalkClip == null)
            {
                foreach (var path in fbxFiles)
                {
                    Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                    if (assets == null) continue;

                    foreach (var a in assets)
                    {
                        if (a is AnimationClip clip && !clip.name.Contains("__preview__"))
                        {
                            string nameLower = clip.name.ToLower();
                            if (nameLower.Contains("crouch"))
                            {
                                if (crouchIdleClip == null) crouchIdleClip = clip;
                                else if (crouchWalkClip == null) crouchWalkClip = clip;
                            }
                        }
                    }
                }
            }

            if (crouchIdleClip == null && crouchWalkClip == null)
            {
                EditorUtility.DisplayDialog("Klip Bulunamadı", 
                    "Kütüphane içinde henüz 'crouch' animasyonu algılanamadı.\nLütfen önce 'Tools/Setup Quaternius Animation Library' çalıştırıldığından emin olun.", 
                    "Tamam");
                return;
            }

            if (crouchIdleClip == null) crouchIdleClip = crouchWalkClip;
            if (crouchWalkClip == null) crouchWalkClip = crouchIdleClip;

            Debug.Log($"<color=green>[Quaternius] Seçilen Klipler: Idle -> {crouchIdleClip.name}, Walk -> {crouchWalkClip.name}</color>");

            // Controller'ı güncelle
            string controllerPath = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                Debug.LogError("Controller bulunamadı!");
                return;
            }

            // IsCrouching parametresi
            bool hasCrouchParam = false;
            foreach (var p in controller.parameters)
            {
                if (p.name == "IsCrouching") hasCrouchParam = true;
            }
            if (!hasCrouchParam)
            {
                controller.AddParameter("IsCrouching", AnimatorControllerParameterType.Bool);
            }

            var rootSm = controller.layers[0].stateMachine;

            AnimatorState normalLocomotion = null;
            AnimatorState crouchState = null;

            foreach (var s in rootSm.states)
            {
                if (s.state.name == "Idle Walk Run Blend" || s.state.name == "Idle" || s.state.name == "Blend Tree")
                {
                    normalLocomotion = s.state;
                }
                if (s.state.name == "Crouch Locomotion" || s.state.name == "Crouch Locomotion Blend")
                {
                    crouchState = s.state;
                }
            }

            if (crouchState == null)
            {
                crouchState = rootSm.AddState("Crouch Locomotion");
            }

            // BlendTree oluştur/güncelle
            if (crouchState.motion is BlendTree bt)
            {
                bt.children = new ChildMotion[0];
                bt.blendParameter = "Speed";
                bt.AddChild(crouchIdleClip, 0f);
                bt.AddChild(crouchWalkClip, 2f);
            }
            else
            {
                controller.CreateBlendTreeInController("Crouch Locomotion Blend", out BlendTree newBt);
                newBt.blendType = BlendTreeType.Simple1D;
                newBt.blendParameter = "Speed";
                newBt.AddChild(crouchIdleClip, 0f);
                newBt.AddChild(crouchWalkClip, 2f);
                crouchState.motion = newBt;
            }

            // Geçişleri bağla
            if (normalLocomotion != null)
            {
                bool hasIn = false;
                foreach (var t in normalLocomotion.transitions)
                {
                    if (t.destinationState == crouchState) hasIn = true;
                }
                if (!hasIn)
                {
                    var tin = normalLocomotion.AddTransition(crouchState);
                    tin.AddCondition(AnimatorConditionMode.If, 0, "IsCrouching");
                    tin.hasExitTime = false;
                    tin.duration = 0.2f;
                }

                bool hasOut = false;
                foreach (var t in crouchState.transitions)
                {
                    if (t.destinationState == normalLocomotion) hasOut = true;
                }
                if (!hasOut)
                {
                    var tout = crouchState.AddTransition(normalLocomotion);
                    tout.AddCondition(AnimatorConditionMode.IfNot, 0, "IsCrouching");
                    tout.hasExitTime = false;
                    tout.duration = 0.2f;
                }
            }

            // UpperBodyMask Body ayarı (gövde bükülsün, kollar silahı tutsun)
            string maskPath = "Assets/UpperBodyMask.mask";
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
            if (mask != null)
            {
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, false);
                EditorUtility.SetDirty(mask);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("Başarılı", 
                $"Quaternius Kütüphanesindeki çömelme animasyonları başarıyla karaktere bağlandı!\n\nÇömelerek Durma: {crouchIdleClip.name}\nÇömelerek Yürüme: {crouchWalkClip.name}\n\nArtık oyunda [C] tuşuna basarak deneyebilirsiniz.", 
                "Harika");
        }

        private static void ConfigureFbx(string path)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;

            Debug.Log($"<color=cyan>[QuaterniusSetup] Yapılandırılıyor: {path}</color>");

            // 1. Model Tab: Bake Axis Conversion
            importer.bakeAxisConversion = true;

            // 2. Rig Tab: Humanoid
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

            // 3. Root Motion Node (Rig/root veya root)
            importer.motionNodeName = "Rig/root";

            // 4. Animasyon Klipleri: _Loop olanları loopTime yap
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.clipAnimations;
            }

            if (clips != null && clips.Length > 0)
            {
                foreach (var clip in clips)
                {
                    string n = clip.name.ToLower();
                    if (n.Contains("loop") || n.Contains("idle") || n.Contains("walk") || n.Contains("run") || n.Contains("crouch"))
                    {
                        clip.loopTime = true;
                    }
                }
                importer.clipAnimations = clips;
            }

            importer.SaveAndReimport();
            Debug.Log($"<color=green>[QuaterniusSetup] Başarıyla içe aktarıldı: {path}</color>");
        }
    }
}
