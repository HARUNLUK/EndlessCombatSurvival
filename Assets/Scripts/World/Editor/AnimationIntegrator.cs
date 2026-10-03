using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public class AnimationIntegrator
{
    [MenuItem("Tools/Integrate Sitting Animation")]
    public static void IntegrateSittingAnimation()
    {
        string campingFbxPath = "Assets/Animations/Camping Sitting Idle.fbx";
        string benchFbxPath = "Assets/Animations/Sitting Idle.fbx";

        // 1. Her iki FBX modelini de Humanoid ve Loop Time olarak ayarla
        SetupFbxClip(campingFbxPath, "CampingSitting");
        SetupFbxClip(benchFbxPath, "Sitting");

        // 2. Yerde oturma animasyon klibini yükle (Öncelikli olarak Camping Sitting Idle)
        AnimationClip sittingClip = LoadClipFromFbx(campingFbxPath);
        if (sittingClip == null)
        {
            sittingClip = LoadClipFromFbx(benchFbxPath);
        }

        if (sittingClip == null)
        {
            Debug.LogError("Could not find AnimationClip in FBX.");
            return;
        }

        // 3. StarterAssetsThirdPerson Controller'ı Güncelle
        string controllerPath = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        
        if (controller == null)
        {
            Debug.LogError("Could not find Animator Controller at " + controllerPath);
            return;
        }

        // IsSitting Parametresini Ekle / Kontrol Et
        bool hasParam = false;
        foreach (var param in controller.parameters)
        {
            if (param.name == "IsSitting") hasParam = true;
        }
        if (!hasParam)
        {
            controller.AddParameter("IsSitting", AnimatorControllerParameterType.Bool);
        }

        AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;

        // Sitting State'ini bul veya oluştur
        AnimatorState sittingState = null;
        AnimatorState idleState = null;
        
        foreach (var state in rootStateMachine.states)
        {
            if (state.state.name == "Sitting") sittingState = state.state;
            if (state.state.name == "Idle Walk Run Blend" || state.state.name == "Idle" || state.state.name == "Blend Tree") idleState = state.state;
        }

        if (sittingState == null)
        {
            sittingState = rootStateMachine.AddState("Sitting");
        }
        sittingState.motion = sittingClip;

        // Any State -> Sitting Geçişi
        bool hasTransitionIn = false;
        foreach (var trans in rootStateMachine.anyStateTransitions)
        {
            if (trans.destinationState == sittingState) hasTransitionIn = true;
        }
        
        if (!hasTransitionIn)
        {
            var transIn = rootStateMachine.AddAnyStateTransition(sittingState);
            transIn.AddCondition(AnimatorConditionMode.If, 0, "IsSitting");
            transIn.hasExitTime = false;
            transIn.duration = 0.25f;
        }

        // Sitting -> Idle Geçişi
        if (idleState != null)
        {
            bool hasTransitionOut = false;
            foreach (var trans in sittingState.transitions)
            {
                if (trans.destinationState == idleState) hasTransitionOut = true;
            }
            
            if (!hasTransitionOut)
            {
                var transOut = sittingState.AddTransition(idleState);
                transOut.AddCondition(AnimatorConditionMode.IfNot, 0, "IsSitting");
                transOut.hasExitTime = false;
                transOut.duration = 0.25f;
            }
        }
        else
        {
            sittingState.AddExitTransition().AddCondition(AnimatorConditionMode.IfNot, 0, "IsSitting");
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        
        Debug.Log("<color=green>[AnimationIntegrator] 'Camping Sitting Idle' animasyonu başarıyla entegre edildi!</color>");
    }

    private static void SetupFbxClip(string fbxPath, string clipName)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer != null)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.clipAnimations;
            }
            
            if (clips != null && clips.Length > 0)
            {
                clips[0].loopTime = true;
                clips[0].name = clipName;
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
