using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using EndlessCombat.Character;

namespace EndlessSurvival.Testing.Editor
{
    /// <summary>
    /// Animation test scene (2026-11-18): Assets/Scenes/Test/Test_Animasyon.unity holds only the Survivor body
    /// (player controller, TwistBones, MuscleBones, no gameplay), a ground with a 0.5 m grid (foot sliding is easy
    /// to see), a sun and an orbit camera (AnimationViewer). Built from scratch every time, so it never goes stale.
    /// Screenshots: samples the controller's clips at a few times and renders front / 3/4 / side / back / close
    /// pictures to PoseShots/anim_*.png (project folder). Works in batch mode:
    ///   -executeMethod EndlessSurvival.Testing.Editor.AnimationTestScene.BuildBatch
    ///   -executeMethod EndlessSurvival.Testing.Editor.AnimationTestScene.CaptureBatch
    /// The pictures include the TwistBones / MuscleBones pass (run by hand after each sample).
    /// </summary>
    public static class AnimationTestScene
    {
        public const string ScenePath = "Assets/Scenes/Test/Test_Animasyon.unity";
        private const string AssetFolder = "Assets/Scenes/Test/AnimationTest";
        private const string ModelPath = "Assets/Art/Characters/Survivor/Survivor.fbx";
        private const string ControllerPath = "Assets/Animations/Player/PlayerAnimator.controller";
        private const string ShotsFolder = "PoseShots";
        private const int W = 720, H = 960;

        private static readonly (string name, float yaw, float pitch, float dist, float h)[] Views =
        {
            ("front", 0f, 8f, 3.4f, 1.0f), ("q34", 40f, 10f, 3.4f, 1.0f), ("side", 90f, 5f, 3.4f, 1.0f),
            ("back", 180f, 10f, 3.4f, 1.0f), ("close", 25f, 5f, 1.6f, 1.35f),
            // hand close-ups: h -1 = aim at the left hand, -2 = the right hand
            ("handL", -70f, 15f, 0.55f, -1f), ("handR", 70f, 15f, 0.55f, -2f), ("handLf", -15f, 10f, 0.55f, -1f),
        };

        // ------------------------------------------------------------------ menus

        [MenuItem("Endless Survival/Test Sahneleri/Animasyon sahnesi: Kur - Yenile", priority = 2)]
        public static void BuildMenu()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Animasyon sahnesi", "Önce Play modundan çık.", "Tamam"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string msg = Build();
            EditorUtility.DisplayDialog("Animasyon sahnesi", msg, "Tamam");
        }

        [MenuItem("Endless Survival/Test Sahneleri/Animasyon sahnesini aç", priority = 23)]
        public static void OpenMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Endless Survival/Test Sahneleri/Animasyon: ekran görüntüleri al", priority = 24)]
        public static void CaptureMenu()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Animasyon", "Önce Play modundan çık.", "Tamam"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string msg = Capture();
            EditorUtility.DisplayDialog("Animasyon", msg, "Tamam");
        }

        public static void BuildBatch() => Debug.Log("[AnimationTestScene] " + Build());
        public static void CaptureBatch() => Debug.Log("[AnimationTestScene] " + Capture());

        // ------------------------------------------------------------------ scene

        private static string Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (model == null) return "Eksik: " + ModelPath;
            if (!AssetDatabase.IsValidFolder("Assets/Scenes/Test")) AssetDatabase.CreateFolder("Assets/Scenes", "Test");
            if (!AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.CreateFolder("Assets/Scenes/Test", "AnimationTest");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.46f, 0.47f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.2f);
            RenderSettings.fog = false;

            var sunGo = new GameObject("Directional Light");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(45f, 150f, 0f);
            RenderSettings.sun = sun;

            Material groundMat = Mat("AnimTest_Ground", new Color(0.55f, 0.55f, 0.53f));
            Material lineMat = Mat("AnimTest_Grid", new Color(0.2f, 0.2f, 0.2f));
            Material axisMat = Mat("AnimTest_Axis", new Color(0.75f, 0.25f, 0.2f));
            var env = new GameObject("Ortam").transform;
            Box("Zemin", env, new Vector3(0f, -0.05f, 0f), new Vector3(12f, 0.1f, 12f), groundMat);
            for (int i = -6; i <= 6; i++)
            {
                float p = i * 0.5f;
                Material m = i == 0 ? axisMat : lineMat;
                Box("Izgara X " + p, env, new Vector3(p, 0.001f, 0f), new Vector3(0.012f, 0.002f, 6f), m);
                Box("Izgara Z " + p, env, new Vector3(0f, 0.001f, p), new Vector3(6f, 0.002f, 0.012f), m);
            }

            var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            body.name = "Survivor";
            body.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var anim = body.GetComponent<Animator>() ?? body.AddComponent<Animator>();
            if (anim.avatar == null) anim.avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (body.GetComponent<TwistBones>() == null) body.AddComponent<TwistBones>();
            if (body.GetComponent<MuscleBones>() == null) body.AddComponent<MuscleBones>();

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.32f, 0.36f, 0.42f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();
            var viewer = camGo.AddComponent<AnimationViewer>();
            viewer.subject = anim;
            Place(cam.transform, anim.transform, Views[1]);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            AssetDatabase.SaveAssets();
            string avatar = anim.avatar != null ? anim.avatar.name + (anim.avatar.isHuman ? " (humanoid)" : " (NOT humanoid)") : "yok";
            return $"{ScenePath} kuruldu. Avatar: {avatar}, controller: {(controller != null ? controller.name : "YOK")}, " +
                   $"klip: {(controller != null ? string.Join(", ", controller.animationClips.Select(c => c.name).Distinct()) : "-")}.";
        }

        private static Material Mat(string name, Color c)
        {
            string path = AssetFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.color = c;
            m.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        private static void Place(Transform cam, Transform subject, (string name, float yaw, float pitch, float dist, float h) v, Animator anim = null)
        {
            Vector3 target = subject.position + Vector3.up * v.h;
            if (v.h < 0f && anim != null)
            {
                Transform hand = anim.GetBoneTransform(v.h < -1.5f ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                if (hand != null) target = hand.position + Vector3.down * 0.05f;
            }
            Quaternion rot = Quaternion.Euler(v.pitch, subject.eulerAngles.y + 180f + v.yaw, 0f);
            cam.position = target - rot * Vector3.forward * v.dist;
            cam.rotation = rot;
        }

        private static void AddToBuildSettings()
        {
            var list = EditorBuildSettings.scenes.ToList();
            if (!list.Exists(s => s.path == ScenePath)) list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ------------------------------------------------------------------ screenshots

        private static string Capture()
        {
            string built = Build(); // always fresh, same as the scene the user opens
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var anim = Object.FindAnyObjectByType<Animator>();
            var cam = Object.FindAnyObjectByType<Camera>();
            if (anim == null || cam == null) return built + "\nSahnede karakter ya da kamera yok.";
            if (anim.runtimeAnimatorController == null) return built + "\nKarakterde controller yok.";
            var viewer = cam.GetComponent<AnimationViewer>();
            if (viewer != null) viewer.enabled = false;

            var twist = anim.GetComponent<TwistBones>();
            var muscle = anim.GetComponent<MuscleBones>();
            Call(muscle, "Awake");     // they read the rest pose, like Awake in play mode before the first animation frame
            Call(twist, "Awake");

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ShotsFolder));
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 };
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            // warm up: in batch mode the very first render has no shadows / lighting set up yet (black, glossy picture)
            for (int i = 0; i < 3; i++) cam.Render();
            // the skin must follow the bones set just before each picture (otherwise it lags one sample behind)
            var skins = anim.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in skins) smr.forceMatrixRecalculationPerRender = true;
            int shots = 0;
            var clips = anim.runtimeAnimatorController.animationClips.Where(c => c != null).Distinct().ToArray();
            var log = new System.Text.StringBuilder();

            foreach (AnimationClip clip in clips)
            {
                var playable = AnimationPlayableUtilities.PlayClip(anim, clip, out PlayableGraph graph);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable.SetApplyFootIK(true);
                float len = Mathf.Max(clip.length, 0.01f);
                float[] times = { 0f, len * 0.3f, len * 0.57f, len * 0.8f };
                foreach (float t in times)
                {
                    playable.SetTime(t);
                    graph.Evaluate();
                    Call(muscle, "LateUpdate");
                    Call(twist, "LateUpdate");
                    Transform lh = anim.GetBoneTransform(HumanBodyBones.LeftHand), rh = anim.GetBoneTransform(HumanBodyBones.RightHand);
                    Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips), head = anim.GetBoneTransform(HumanBodyBones.Head);
                    log.Append($"\n{clip.name} t={t:0.0}: hips y={hips?.position.y:0.00} head y={head?.position.y:0.00} " +
                               $"L hand {lh?.position:F2} R hand {rh?.position:F2} " +
                               $"palm (index side forward, 1 = palm faces the thigh) L {PalmFacing(anim, true):0.00} R {PalmFacing(anim, false):0.00}");
                    foreach (var v in Views)
                    {
                        if ((v.name == "back" || v.h < 0f) && t > 0f) continue; // one back / hand picture per clip is enough
                        Place(cam.transform, anim.transform, v, anim);
                        cam.Render(); // throw-away render: skinning / shadows catch up with the new pose
                        cam.Render();
                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                        tex.Apply();
                        RenderTexture.active = null;
                        File.WriteAllBytes(Path.Combine(dir, $"anim_{Safe(clip.name)}_{v.name}_{t:00.0}s.png"), tex.EncodeToPNG());
                        shots++;
                    }
                }
                graph.Destroy();
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            // the scene was only sampled: reopen it clean
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return $"{built}\n{shots} resim: {dir}{log}";
        }

        /// <summary>How much the index knuckle sits in front of the little finger knuckle (character forward).</summary>
        private static float PalmFacing(Animator a, bool left)
        {
            Transform i = a.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            Transform l = a.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            if (i == null || l == null) return float.NaN;
            return Vector3.Dot((i.position - l.position).normalized, a.transform.forward);
        }

        private static void Call(Object target, string method)
        {
            if (target == null) return;
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(target, null);
        }

        private static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    }
}
