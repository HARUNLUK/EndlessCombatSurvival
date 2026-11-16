using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EndlessCombat.Combat.Editor
{
    /// <summary>
    /// Debug: samples candidate melee clips on the scene's player (edit mode, AnimationMode) and saves one strip of
    /// 5 frames per clip to PoseShots/clip_*.png (front-side view). Used to pick pack clips that retarget cleanly.
    /// Run the editor (not batchmode): -executeMethod EndlessCombat.Combat.Editor.ClipContactSheet.Run
    /// </summary>
    public static class ClipContactSheet
    {
        private const string Ual1 = "Assets/_AssetPacks/Universal Animation Library[Standard]/Universal Animation Library[Standard]/Unity/UAL1_Standard.fbx";
        private const string Ual2 = "Assets/_AssetPacks/Universal Animation Library 2[Standard]/Universal Animation Library 2[Standard]/Unity/UAL2_Standard.fbx";
        private const string Kev = "Assets/Kevin Iglesias/Human Animations/Animations/Male/Combat/";

        private static readonly (string label, string path, string name)[] Candidates =
        {
            ("k_1h_r", Kev + "1H/HumanM@Attack1H01_R.fbx", null),
            ("k_1h_l", Kev + "1H/HumanM@Attack1H01_L.fbx", null),
            ("k_2h", Kev + "2H/HumanM@Attack2H01.fbx", null),
            ("k_polearm", Kev + "Polearm/HumanM@AttackPolearm01.fbx", null),
            ("k_shield", Kev + "Shield/HumanM@AttackShield01.fbx", null),
            ("u_regA", Ual2, "Sword_Regular_A"),
            ("u_regB", Ual2, "Sword_Regular_B"),
            ("u_regC", Ual2, "Sword_Regular_C"),
            ("u_heavyCombo", Ual2, "Sword_Heavy_Combo"),
            ("u_swordAttack", Ual1, "Sword_Attack"),
            ("u_hook", Ual2, "Melee_Hook"),
            ("u_shieldOneShot", Ual2, "Shield_OneShot"),
        };

        [MenuItem("Tools/Combat/Debug/Clip Contact Sheet")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
            var log = new StringWriter();
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PoseShots"));
            Directory.CreateDirectory(outDir);

            var player = GameObject.FindGameObjectWithTag("Player");
            var animator = player != null ? player.GetComponentInChildren<Animator>() : null;
            if (animator == null) { log.WriteLine("no player animator"); Done(outDir, log); return; }
            var root = animator.gameObject;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);

            var camGo = new GameObject("SheetCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 38f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.6f, 0.66f);
            const int w = 300, h = 400, frames = 5;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            var sheet = new Texture2D(w * frames, h, TextureFormat.RGB24, false);
            var tmp = new Texture2D(w, h, TextureFormat.RGB24, false);

            foreach (var c in Candidates)
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath(c.path).OfType<AnimationClip>()
                    .FirstOrDefault(x => !x.name.StartsWith("__preview__") && (c.name == null || x.name == c.name || x.name.EndsWith("|" + c.name)));
                if (clip == null) { log.WriteLine("missing " + c.label); continue; }
                AnimationMode.StartAnimationMode();
                try
                {
                    for (int f = 0; f < frames; f++)
                    {
                        float t = clip.length * (0.1f + 0.8f * f / (frames - 1));
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(root, clip, t);
                        AnimationMode.EndSampling();
                        Vector3 centre = hips != null ? hips.position : root.transform.position + Vector3.up;
                        Vector3 view = (root.transform.forward * 0.8f + root.transform.right * 0.9f).normalized;
                        cam.transform.position = centre + view * 3.6f + Vector3.up * 0.15f;
                        cam.transform.LookAt(centre + Vector3.up * 0.05f);
                        cam.Render();
                        RenderTexture.active = rt;
                        tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                        tmp.Apply();
                        sheet.SetPixels(f * w, 0, w, h, tmp.GetPixels());
                    }
                }
                finally
                {
                    AnimationMode.StopAnimationMode();
                    RenderTexture.active = null;
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(outDir, $"clip_{c.label}.png"), sheet.EncodeToPNG());
                log.WriteLine($"{c.label}: {clip.name} {clip.length:0.00}s loop={clip.isLooping}");
            }
            Object.DestroyImmediate(camGo);
            Done(outDir, log);
        }

        private static void Done(string outDir, StringWriter log)
        {
            File.WriteAllText(Path.Combine(outDir, "clip_log.txt"), log.ToString());
            if (System.Environment.GetCommandLineArgs().Contains("-executeMethod")) EditorApplication.Exit(0);
        }
    }
}
