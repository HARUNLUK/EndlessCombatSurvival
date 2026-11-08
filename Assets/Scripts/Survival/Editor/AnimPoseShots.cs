using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EndlessSurvival.Survival.Editor
{
    /// <summary>
    /// Debug tool: plays every clip in Assets/Animations/Survival (plus the old CampingSitting for comparison) on the
    /// player character in Play Mode, at 10/50/90% of the clip, and saves side + front pictures to
    /// PoseShots/anim_*.png. No game code runs, so the pictures show the pure import/retarget result.
    /// Run the editor (not batchmode): -executeMethod EndlessSurvival.Survival.Editor.AnimPoseShots.Run
    /// </summary>
    [InitializeOnLoad]
    public static class AnimPoseShots
    {
        private const string Flag = "EndlessSurvival.AnimPoseShots.Active";
        private const string PlayerPrefab = "Assets/Starter Assets/Runtime/ThirdPersonController/Prefabs/PlayerArmature.prefab";
        private static readonly float[] Times = { 0.1f, 0.5f, 0.9f };
        private static int _frames;
        private static string Filter => System.Environment.GetCommandLineArgs().SkipWhile(a => a != "-clipFilter").Skip(1).FirstOrDefault();

        static AnimPoseShots()
        {
            if (!EditorPrefs.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
        }

        [MenuItem("Endless Survival/Debug/Animation Pose Shots")]
        public static void Run()
        {
            EditorPrefs.SetBool(Flag, true);
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (++_frames < 30) return; // let play mode settle
            bool done;
            try { done = Step(); }
            catch (System.Exception e) { File.WriteAllText("PoseShots/anim_log.txt", e.ToString()); done = true; }
            if (!done) return;
            EditorApplication.update -= Tick;
            EditorPrefs.DeleteKey(Flag);
            if (System.Environment.GetCommandLineArgs().Contains("-executeMethod")) EditorApplication.Exit(0);
            else EditorApplication.ExitPlaymode();
        }

        private static Animator _animator;
        private static GameObject _body;
        private static Camera _cam;
        private static List<AnimationClip> _clips;
        private static int _clipIndex = -1, _wait;
        private static readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();

        /// <summary>One clip per few editor frames: the Animator needs a frame to load a new controller.</summary>
        private static bool Step()
        {
            if (_clips == null) { Setup(); return false; }
            if (_clipIndex >= 0 && _wait > 0) { _wait--; return false; }
            if (_clipIndex >= 0) ShootClip(_clips[_clipIndex]);
            _clipIndex++;
            if (_clipIndex >= _clips.Count)
            {
                File.WriteAllText("PoseShots/anim_log.txt", _log.ToString());
                return true;
            }
            var ctrl = new AnimatorController();
            ctrl.AddLayer("Base");
            var st = ctrl.layers[0].stateMachine.AddState("Shot");
            st.motion = _clips[_clipIndex];
            _animator.runtimeAnimatorController = ctrl;
            _animator.Rebind();
            _wait = 3;
            return false;
        }

        private static void Setup()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(0.02f, 1f, 2f); // thin strip = floor line, legs stay visible

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            var src = playerPrefab.GetComponentInChildren<Animator>();
            var body = Object.Instantiate(src.gameObject);
            foreach (var mb in body.GetComponentsInChildren<MonoBehaviour>()) Object.DestroyImmediate(mb);
            foreach (var c in body.GetComponentsInChildren<CharacterController>()) Object.DestroyImmediate(c);
            foreach (var c in body.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            body.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _body = body;
            _animator = body.GetComponent<Animator>();
            _animator.applyRootMotion = false;

            _cam = new GameObject("ShotCam").AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.55f, 0.6f, 0.65f);
            _cam.fieldOfView = 35f;
            _cam.nearClipPlane = 0.02f;

            Directory.CreateDirectory("PoseShots");
            _clips = new List<AnimationClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/Animations" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is AnimationClip c && !c.name.StartsWith("__preview") && (Filter == null || c.name.Contains(Filter))) _clips.Add(c);
            }
        }

        private static void ShootClip(AnimationClip clip)
        {
            const int W = 360, H = 420;
            var sheet = new Texture2D(W * Times.Length, H * 3, TextureFormat.RGB24, false);
            for (int ti = 0; ti < Times.Length; ti++)
            {
                _animator.Play("Shot", 0, Times[ti]);
                _animator.Update(0f);
                _body.transform.position = Vector3.zero;
                var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
                var footL = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                var state = _animator.GetCurrentAnimatorClipInfo(0);
                string playing = state.Length > 0 ? state[0].clip.name : "-";
                if (ti == 1) _log.AppendLine($"{clip.name} (animator plays {playing}) len={clip.length:0.00}s hipsY@50%={hips.position.y:0.00} leftFootY@50%={footL.position.y:0.00}");
                for (int v = 0; v < 3; v++)
                {
                    Vector3 dir = v == 0 ? Vector3.right : new Vector3(0.6f, 0f, 1f).normalized;
                    Vector3 target = new Vector3(0f, 0.8f, 0f);
                    float dist = 4.2f;
                    if (v == 2)
                    {
                        // close-up of both hands, from the front
                        var lh = _animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
                        var rh = _animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                        target = (lh + rh) * 0.5f;
                        dir = new Vector3(0.3f, 0.3f, 1f).normalized;
                        dist = 0.6f + Vector3.Distance(lh, rh) * 1.6f;
                    }
                    _cam.transform.position = target + dir * dist;
                    _cam.transform.LookAt(target);
                    var rt = RenderTexture.GetTemporary(W, H, 24);
                    _cam.targetTexture = rt;
                    _cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, W, H), W * ti, H * (2 - v));
                    RenderTexture.active = null;
                    _cam.targetTexture = null;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
            sheet.Apply();
            File.WriteAllBytes($"PoseShots/anim_{clip.name}.png", sheet.EncodeToPNG());
            Object.Destroy(sheet);
        }
    }
}
