using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;

namespace EndlessSurvival.EditorTools
{
    public class StealthAnimationIntegrator
    {
        [MenuItem("Tools/Integrate Stealth Animation")]
        public static void IntegrateStealthAnimations()
        {
            string animDir = "Assets/Animations";
            if (!Directory.Exists(animDir))
            {
                Debug.LogError($"[StealthAnimationIntegrator] '{animDir}' klasörü bulunamadı!");
                return;
            }

            // Crouch Idle ve Crouch Walk dosyalarını tespit et
            string crouchIdlePath = FindFbxByPattern(animDir, "*crouch*idle*");
            if (string.IsNullOrEmpty(crouchIdlePath))
            {
                crouchIdlePath = FindFbxByPattern(animDir, "*crouching*idle*");
            }

            string crouchWalkPath = FindFbxByPattern(animDir, "*crouch*walk*");
            if (string.IsNullOrEmpty(crouchWalkPath))
            {
                crouchWalkPath = FindFbxByPattern(animDir, "*crouched*walking*");
            }

            if (string.IsNullOrEmpty(crouchIdlePath) && string.IsNullOrEmpty(crouchWalkPath))
            {
                EditorUtility.DisplayDialog("Animasyon Bulunamadı", 
                    "Assets/Animations klasöründe 'Crouch Idle' veya 'Crouch Walk' içeren bir FBX dosyası bulunamadı.\n\nLütfen Mixamo'dan indirdiğiniz FBX dosyalarını 'Assets/Animations/' içine attıktan sonra bu aracı tekrar çalıştırın.", 
                    "Tamam");
                return;
            }

            // 1. FBX dosyalarını Humanoid ve Loop Time olarak ayarla
            if (!string.IsNullOrEmpty(crouchIdlePath))
            {
                SetupFbxClip(crouchIdlePath, "CrouchIdle");
            }
            if (!string.IsNullOrEmpty(crouchWalkPath))
            {
                SetupFbxClip(crouchWalkPath, "CrouchWalk");
            }

            // 2. Klipleri yükle
            AnimationClip idleClip = !string.IsNullOrEmpty(crouchIdlePath) ? LoadClipFromFbx(crouchIdlePath) : null;
            AnimationClip walkClip = !string.IsNullOrEmpty(crouchWalkPath) ? LoadClipFromFbx(crouchWalkPath) : null;

            if (idleClip == null && walkClip == null)
            {
                Debug.LogError("[StealthAnimationIntegrator] Klipler yüklenemedi!");
                return;
            }

            // Eğer sadece biri varsa diğerinin yerine de onu kullan
            if (idleClip == null) idleClip = walkClip;
            if (walkClip == null) walkClip = idleClip;

            // 3. Animator Controller'ı Aç ve Entegre Et
            string controllerPath = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);

            if (controller == null)
            {
                Debug.LogError($"[StealthAnimationIntegrator] AnimatorController bulunamadı: {controllerPath}");
                return;
            }

            // IsCrouching Parametresini Ekle
            bool hasCrouchParam = false;
            foreach (var p in controller.parameters)
            {
                if (p.name == "IsCrouching") hasCrouchParam = true;
            }
            if (!hasCrouchParam)
            {
                controller.AddParameter("IsCrouching", AnimatorControllerParameterType.Bool);
            }

            var rootStateMachine = controller.layers[0].stateMachine;

            // Mevcut ayakta durma BlendTree / Idle State'ini bul
            AnimatorState normalLocomotionState = null;
            foreach (var s in rootStateMachine.states)
            {
                if (s.state.name == "Idle Walk Run Blend" || s.state.name == "Idle" || s.state.name == "Blend Tree")
                {
                    normalLocomotionState = s.state;
                    break;
                }
            }

            // Çömelme Locomotion State veya BlendTree'sini oluştur
            AnimatorState crouchState = null;
            foreach (var s in rootStateMachine.states)
            {
                if (s.state.name == "Crouch Locomotion" || s.state.name == "Crouching")
                {
                    crouchState = s.state;
                    break;
                }
            }

            if (crouchState == null)
            {
                crouchState = rootStateMachine.AddState("Crouch Locomotion");
                controller.CreateBlendTreeInController("Crouch Locomotion Blend", out BlendTree crouchBlendTree);
                crouchBlendTree.blendType = BlendTreeType.Simple1D;
                crouchBlendTree.blendParameter = "Speed";
                crouchBlendTree.AddChild(idleClip, 0f);
                crouchBlendTree.AddChild(walkClip, 2f);
                crouchState.motion = crouchBlendTree;
            }
            else
            {
                if (crouchState.motion is BlendTree bt)
                {
                    bt.children = new ChildMotion[0];
                    bt.blendParameter = "Speed";
                    bt.AddChild(idleClip, 0f);
                    bt.AddChild(walkClip, 2f);
                }
                else
                {
                    controller.CreateBlendTreeInController("Crouch Locomotion Blend", out BlendTree newBt);
                    newBt.blendType = BlendTreeType.Simple1D;
                    newBt.blendParameter = "Speed";
                    newBt.AddChild(idleClip, 0f);
                    newBt.AddChild(walkClip, 2f);
                    crouchState.motion = newBt;
                }
            }

            // Geçişleri yapılandır:
            // Normal Locomotion -> Crouch Locomotion
            if (normalLocomotionState != null)
            {
                bool hasIn = false;
                foreach (var t in normalLocomotionState.transitions)
                {
                    if (t.destinationState == crouchState) hasIn = true;
                }
                if (!hasIn)
                {
                    var transIn = normalLocomotionState.AddTransition(crouchState);
                    transIn.AddCondition(AnimatorConditionMode.If, 0, "IsCrouching");
                    transIn.hasExitTime = false;
                    transIn.duration = 0.2f;
                }

                // Crouch Locomotion -> Normal Locomotion
                bool hasOut = false;
                foreach (var t in crouchState.transitions)
                {
                    if (t.destinationState == normalLocomotionState) hasOut = true;
                }
                if (!hasOut)
                {
                    var transOut = crouchState.AddTransition(normalLocomotionState);
                    transOut.AddCondition(AnimatorConditionMode.IfNot, 0, "IsCrouching");
                    transOut.hasExitTime = false;
                    transOut.duration = 0.2f;
                }
            }

            // 4. UpperBodyMask içinde Body (omurga/kalça) parçasını devre dışı bırak
            // Böylece çömelirken gövde ve bacaklar tam çömelir, kollar tüfeği tutmaya devam eder!
            string maskPath = "Assets/UpperBodyMask.mask";
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
            if (mask != null)
            {
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, false);
                EditorUtility.SetDirty(mask);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log("<color=green>[StealthAnimationIntegrator] Çömelme animasyonları başarıyla StarterAssetsThirdPerson Controller'a entegre edildi!</color>");
            EditorUtility.DisplayDialog("Başarılı", 
                "Çömelme animasyonları (Crouch Idle / Walk) Humanoid avatar, root kilitleme ve UpperBodyMask ile kusursuz şekilde entegre edildi!", 
                "Harika");
        }

        private static string FindFbxByPattern(string dir, string searchPattern)
        {
            var files = Directory.GetFiles(dir, "*.fbx");
            foreach (var f in files)
            {
                string fileName = Path.GetFileName(f).ToLower();
                string pattern = searchPattern.Trim('*').ToLower();
                string[] parts = pattern.Split('*');
                bool match = true;
                foreach (var part in parts)
                {
                    if (!fileName.Contains(part))
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return f.Replace('\\', '/');
            }
            return null;
        }

        private static void SetupFbxClip(string fbxPath, string clipName)
        {
            ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer != null)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

                ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
                if (clips == null || clips.Length == 0)
                {
                    clips = importer.clipAnimations;
                }

                if (clips != null && clips.Length > 0)
                {
                    clips[0].loopTime = true;
                    clips[0].name = clipName;
                    clips[0].lockRootHeightY = true;
                    clips[0].keepOriginalPositionY = true;
                    clips[0].lockRootPositionXZ = true;
                    clips[0].keepOriginalPositionXZ = true;
                    clips[0].lockRootRotation = true;
                    clips[0].keepOriginalOrientation = true;
                    importer.clipAnimations = clips;
                }
                importer.SaveAndReimport();
            }
        }

        private static AnimationClip LoadClipFromFbx(string fbxPath)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            if (assets == null) return null;
            foreach (var asset in assets)
            {
                if (asset is AnimationClip && !asset.name.Contains("__preview__"))
                {
                    return asset as AnimationClip;
                }
            }
            return null;
        }
    }
}
