using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EndlessCombat.Combat.Editor
{
    /// <summary>
    /// Debug: enters play mode in Game.unity, equips each weapon through PlayerShooter.ReplaceWeapon (the game's own path),
    /// plays the actions through CombatAnimator, and saves front + side pictures to PoseShots/play_*.png. Then quits.
    /// Run the editor (not batchmode) with: -executeMethod EndlessCombat.Combat.Editor.PlayModeCapture.Run
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeCapture
    {
        private const string Flag = "EndlessCombat.PlayModeCapture.Active";
        private const string ScenePath = "Assets/Scenes/Game.unity";

        private static double _t0 = -1;
        private static int _step;
        private static double _stepAt;
        private static Camera _cam;
        private static RenderTexture _rt;
        private static PlayerShooter _shooter;
        private static Animator _animator;
        private static readonly StringWriter Log = new StringWriter();

        static PlayModeCapture()
        {
            if (!EditorPrefs.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
        }

        [MenuItem("Tools/Combat/Debug/Play Mode Capture")]
        public static void Run()
        {
            EditorPrefs.SetBool(Flag, true);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PoseShots"));

        private static void Finish()
        {
            EditorPrefs.DeleteKey(Flag);
            EditorApplication.update -= Tick;
            File.WriteAllText(Path.Combine(OutDir, "play_log.txt"), Log.ToString());
            if (Application.isBatchMode || System.Environment.GetCommandLineArgs().Contains("-executeMethod"))
                EditorApplication.Exit(0);
            else
                EditorApplication.ExitPlaymode();
        }

        private static WeaponController Prefab(string name)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Weapons/" + name + ".prefab");
            return go != null ? go.GetComponent<WeaponController>() : null;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (_t0 < 0)
            {
                _t0 = now;
                _stepAt = now + 5.0; // let the game boot
                Directory.CreateDirectory(OutDir);
                foreach (var f in Directory.GetFiles(OutDir, "play_*.png")) File.Delete(f);
                return;
            }
            if (now < _stepAt) return;
            if (now - _t0 > 300) { Log.WriteLine("timeout"); Finish(); return; }

            try
            {
                if (!DoStep(_step)) { Finish(); return; }
            }
            catch (System.Exception e)
            {
                Log.WriteLine("step " + _step + " failed: " + e);
                Finish();
                return;
            }
            _step++;
        }

        // Each step does one thing and says how long to wait before the next one.
        private static bool DoStep(int step)
        {
            void Wait(double s) => _stepAt = EditorApplication.timeSinceStartup + s;
            CombatAnimator Ca() => _animator != null ? _animator.GetComponent<CombatAnimator>() : null;

            if (step == 0)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player == null) { Log.WriteLine("no player"); return false; }
                _shooter = player.GetComponentInChildren<PlayerShooter>();
                _animator = player.GetComponentInChildren<Animator>();
                Log.WriteLine("player " + player.name + " shooter=" + (_shooter != null) + " animator=" + (_animator != null) + " timeScale=" + Time.timeScale);
                SetupCamera();
                BuildPlan(Ca);
                Wait(0.5);
                return true;
            }
            int i = step - 1;
            if (i >= _plan.Count) return false;
            _plan[i].act();
            Wait(_plan[i].wait);
            return true;
        }

        private static readonly List<(System.Action act, double wait)> _plan = new List<(System.Action, double)>();
        private static AnimatorOverrideController _override;
        private static RuntimeAnimatorController _baseController;

        private static AnimationClip UalClip(string n)
        {
            foreach (var path in new[]
                     {
                         "Assets/_AssetPacks/Universal Animation Library[Standard]/Universal Animation Library[Standard]/Unity/UAL1_Standard.fbx",
                         "Assets/_AssetPacks/Universal Animation Library 2[Standard]/Universal Animation Library 2[Standard]/Unity/UAL2_Standard.fbx"
                     })
            foreach (var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                if (c.name == "Armature|" + n) return c;
            Log.WriteLine("no clip " + n);
            return null;
        }

        /// <summary>Plays the controller with one of its clips replaced (in memory only).</summary>
        private static void Override(string originalName, string candidateName)
        {
            if (_baseController == null) _baseController = _animator.runtimeAnimatorController;
            var original = _baseController.animationClips.FirstOrDefault(c => c.name == "Armature|" + originalName);
            var candidate = UalClip(candidateName);
            if (original == null || candidate == null) { Log.WriteLine("override failed " + originalName + " -> " + candidateName); return; }
            _override = new AnimatorOverrideController(_baseController);
            _override[original] = candidate;
            _animator.runtimeAnimatorController = _override;
            Log.WriteLine("override " + originalName + " -> " + candidateName);
        }

        private static void Restore()
        {
            if (_baseController != null) _animator.runtimeAnimatorController = _baseController;
        }

        private static void BuildPlan(System.Func<CombatAnimator> ca)
        {
            void Add(System.Action a, double w) => _plan.Add((a, w));

            Add(() => Shot("play_01_rifle_idle"), 0.1);
            Add(() => Crouch(true), 1.5);
            Add(() => Shot("play_01b_rifle_crouch"), 0.1);
            Add(() => Crouch(false), 1.0);
            Add(() => Equip("Weapon_Pistol"), 1.5);
            Add(() => Shot("play_02_pistol_idle"), 0.1);
            Add(() => Crouch(true), 1.5);
            Add(() => Shot("play_02b_pistol_crouch"), 0.1);
            Add(() => Move(new Vector2(0f, 1f)), 1.2);
            Add(() => Shot("play_02c_pistol_crouch_walk"), 0.1);
            Add(() => Crouch(false), 1.0);
            Add(() => Shot("play_02d_pistol_walk"), 0.1);
            Add(() => Move(Vector2.zero), 1.2);

            Add(() => Equip("Weapon_BaseballBat"), 1.5);
            Add(() => Shot("play_10_bat_hold_current"), 0.1);
            foreach (var hold in new string[0])
            {
                var h = hold;
                Add(() => Override(CurrentHold, h), 1.2);
                Add(() => Shot("play_11_bat_hold_" + h), 0.1);
            }
            Add(Restore, 0.8);
            Add(() => ca()?.SnapWeights(), 0.1);
            foreach (var swing in new[] { "Sword_Regular_A" })
            {
                var s = swing;
                var clip = UalClip(s);
                float len = clip != null ? clip.length : 1f;
                Add(() => { if (s != CurrentSwing) Override(CurrentSwing, s); }, 1.0);
                Add(() => ca()?.PlayAction(CombatAnimator.MeleeSwing, len), len * 0.3);
                Add(() => Shot("play_20_swing_" + s + "_a"), len * 0.25);
                Add(() => Shot("play_20_swing_" + s + "_b"), len * 0.25);
                Add(() => Shot("play_20_swing_" + s + "_c"), len);
                Add(Restore, 0.5);
            }

            Add(() => Equip("Weapon_Knife"), 1.5);
            Add(() => Shot("play_30_knife_hold"), 0.1);
            Add(() => ca()?.PlayAction(CombatAnimator.MeleeStab, 0.6f), 0.2);
            Add(() => Shot("play_31_knife_stab"), 1.0);
            Add(() => Equip("Weapon_WoodenClub"), 1.5);
            Add(() => Shot("play_32_club_hold"), 0.1);
            Add(() => Equip(null), 1.5);
            Add(() => Shot("play_40_unarmed"), 0.1);
            Add(() => ca()?.PlayAction(CombatAnimator.PunchJab, 0.6f), 0.22);
            Add(() => Shot("play_41_punch"), 1.0);
            Add(() => ca()?.PlayAction(CombatAnimator.Throw, 1.0f), 0.4);
            Add(() => Shot("play_42_throw"), 0.1);
        }

        private static void Crouch(bool on)
        {
            var stealth = _shooter != null ? _shooter.GetComponentInParent<PlayerStealthController>() : null;
            if (stealth == null && _shooter != null) stealth = _shooter.transform.root.GetComponentInChildren<PlayerStealthController>();
            if (stealth != null) stealth.SetCrouching(on);
            Log.WriteLine("crouch " + on + (stealth == null ? " (no PlayerStealthController)" : ""));
        }

        private static void Move(Vector2 v)
        {
            var input = _shooter != null
                ? _shooter.transform.root.GetComponentsInChildren<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "StarterAssetsInputs")
                : null;
            var field = input != null ? input.GetType().GetField("move") : null;
            if (field != null) field.SetValue(input, v);
            Log.WriteLine("move " + v + (field == null ? " (no input)" : ""));
        }

        // Clips currently used by the controller (keep in sync with Package1Setup)
        private const string CurrentHold = "Sword_Block";
        private const string CurrentSwing = "Sword_Regular_A";

        private static void Equip(string prefab)
        {
            if (_shooter == null) return;
            var wc = prefab != null ? Prefab(prefab) : null;
            if (prefab != null && wc == null) Log.WriteLine("missing prefab " + prefab);
            if (_shooter.IsHolstered) _shooter.SetHolstered(false);
            _shooter.ReplaceWeapon(wc);
            Log.WriteLine("equipped " + (prefab ?? "nothing") + " -> type " + _shooter.HandType);
        }

        private static void SetupCamera()
        {
            var go = new GameObject("PoseCaptureCam");
            _cam = go.AddComponent<Camera>();
            _cam.fieldOfView = 35f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 60f;
            _cam.enabled = false;
            _rt = new RenderTexture(512, 640, 24);
            _cam.targetTexture = _rt;
        }

        private static void Shot(string name)
        {
            if (_animator == null || _cam == null) return;
            Transform c = _animator.transform;
            var sb = new System.Text.StringBuilder(name);
            foreach (var l in new[] { CombatAnimator.RifleLayerName, CombatAnimator.PistolLayerName, CombatAnimator.MeleeLayerName, CombatAnimator.ActionLayerName })
            {
                int i = _animator.GetLayerIndex(l);
                if (i < 0) continue;
                var st = _animator.GetCurrentAnimatorStateInfo(i);
                sb.Append($" | {l.Trim()} w={_animator.GetLayerWeight(i):0.00} state={st.shortNameHash}");
            }
            Log.WriteLine(sb.ToString());
            Transform hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform lf = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rf = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (hips != null && lf != null && rf != null)
                Log.WriteLine($"   hips height={hips.position.y - c.position.y:F2} feet L={lf.position.y - c.position.y:F2} R={rf.position.y - c.position.y:F2}"
                              + $" hipsYaw={Vector3.SignedAngle(c.forward, Vector3.ProjectOnPlane(hips.forward, Vector3.up), Vector3.up):0} crouchParam={_animator.GetBool("IsCrouching")} speed={_animator.GetFloat("Speed"):0.0}");
            Transform hand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform idx = _animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            Transform lit = _animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            Transform mid = _animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            Transform midI = _animator.GetBoneTransform(HumanBodyBones.RightMiddleIntermediate);
            Transform thumbD = _animator.GetBoneTransform(HumanBodyBones.RightThumbDistal);
            var body = _shooter != null && _shooter.CurrentWeapon != null
                ? _shooter.CurrentWeapon.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.EndsWith("Body") || t.name.EndsWith("Blade"))
                : null;
            if (hand != null && idx != null && lit != null && mid != null)
            {
                Vector3 knuckle = (idx.position - lit.position).normalized;
                Vector3 fingers = (mid.position - hand.position).normalized;
                string s = $"   hand: knuckle(world)={knuckle:F2} fingers={fingers:F2} rawKnuckleLen={(idx.position - lit.position).magnitude:F3}"
                           + (midI != null ? $" midCurl={(midI.position - mid.position).normalized:F2}" : "")
                           + (thumbD != null ? $" thumbTip-hand={(thumbD.position - hand.position):F2}" : "");
                if (body != null)
                {
                    Vector3 shaft = body.name.EndsWith("Body") ? body.up : body.forward;
                    s += $" | weapon {body.name} shaft={shaft:F2} angle(knuckle)={Vector3.Angle(shaft, knuckle):0} angle(fingers)={Vector3.Angle(shaft, fingers):0} pos-hand={(body.position - hand.position):F2}";
                }
                Log.WriteLine(s);
            }

            var tex = new List<Texture2D>
            {
                Render(c, c.forward * 3.4f + Vector3.up * 1.2f),
                Render(c, (c.forward - c.right).normalized * 3.4f + Vector3.up * 1.2f)
            };
            var combo = new Texture2D(1024, 640, TextureFormat.RGB24, false);
            combo.SetPixels(0, 0, 512, 640, tex[0].GetPixels());
            combo.SetPixels(512, 0, 512, 640, tex[1].GetPixels());
            combo.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), combo.EncodeToPNG());
            foreach (var t in tex) Object.DestroyImmediate(t);
            Object.DestroyImmediate(combo);
        }

        private static Texture2D Render(Transform character, Vector3 offset)
        {
            _cam.transform.position = character.position + offset;
            _cam.transform.LookAt(character.position + Vector3.up * 1.0f);
            _cam.Render();
            RenderTexture.active = _rt;
            var tex = new Texture2D(512, 640, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 512, 640), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            return tex;
        }
    }
}
