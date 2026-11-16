using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EndlessCombat.Combat.Editor
{
    /// <summary>
    /// Debug: how the player holds each weapon (idle + walking), first with the souls-like melee runtime on,
    /// then with it removed, so a difference shows what the melee code changes outside of combat.
    /// Writes PoseShots/hold_*.png and PoseShots/hold_log.txt (all layer weights / states), then quits.
    /// Run the editor (not batchmode): -executeMethod EndlessCombat.Combat.Editor.WeaponHoldCapture.Run
    /// </summary>
    [InitializeOnLoad]
    public static class WeaponHoldCapture
    {
        private const string Flag = "EndlessCombat.WeaponHoldCapture.Active";
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private static readonly string[] Weapons = { "Weapon_AssaultRifle", "Weapon_Pistol", "Weapon_BaseballBat", "Weapon_WoodenClub" };

        private static double _t0 = -1, _stepAt;
        private static int _step;
        private static Camera _cam;
        private static RenderTexture _rt;
        private static GameObject _player;
        private static PlayerShooter _shooter;
        private static Animator _animator;
        private static EndlessCombat.Character.PlayerInputState _inputs;
        private static readonly StringWriter Log = new StringWriter();
        private static readonly List<(System.Action act, double wait)> Plan = new List<(System.Action, double)>();

        static WeaponHoldCapture()
        {
            if (!EditorPrefs.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
        }

        [MenuItem("Tools/Combat/Debug/Weapon Hold Capture")]
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
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "hold_log.txt"), Log.ToString());
            if (System.Environment.GetCommandLineArgs().Contains("-executeMethod")) EditorApplication.Exit(0);
            else EditorApplication.ExitPlaymode();
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (_step > 0 && Time.timeScale < 0.5f) Time.timeScale = 1f;
            double now = EditorApplication.timeSinceStartup;
            if (_t0 < 0)
            {
                _t0 = now;
                _stepAt = now + 6.0;
                Directory.CreateDirectory(OutDir);
                foreach (var f in Directory.GetFiles(OutDir, "hold_*.png")) File.Delete(f);
                return;
            }
            if (now - _t0 > 200) { Log.WriteLine("timeout"); Finish(); return; }
            if (now < _stepAt) return;
            try
            {
                if (_step == 0) { if (!Setup()) { Finish(); return; } _stepAt = now + 0.5; }
                else
                {
                    int i = _step - 1;
                    if (i >= Plan.Count) { Finish(); return; }
                    Plan[i].act();
                    _stepAt = now + Plan[i].wait;
                }
            }
            catch (System.Exception e)
            {
                Log.WriteLine("step " + _step + " failed: " + e);
                Finish();
                return;
            }
            _step++;
        }

        private static bool Setup()
        {
            _player = GameObject.FindGameObjectWithTag("Player");
            if (_player == null) { Log.WriteLine("no player"); return false; }
            _shooter = _player.GetComponent<PlayerShooter>();
            _animator = _player.GetComponent<Animator>();
            _inputs = _player.GetComponent<EndlessCombat.Character.PlayerInputState>();
            Time.timeScale = 1f;
            Log.WriteLine($"player={_player.name} applyRootMotion={_animator.applyRootMotion} controller={_animator.runtimeAnimatorController.name}");
            Log.WriteLine("components: " + string.Join(", ", _player.GetComponents<Component>().Select(c => c.GetType().Name)));

            var cam = new GameObject("HoldCaptureCam");
            _cam = cam.AddComponent<Camera>();
            _cam.fieldOfView = 40f;
            _cam.nearClipPlane = 0.05f;
            _cam.enabled = false;
            _rt = new RenderTexture(512, 640, 24);
            _cam.targetTexture = _rt;

            BuildPlan();
            return true;
        }

        private static void BuildPlan()
        {
            void Add(System.Action a, double w) => Plan.Add((a, w));
            foreach (string pass in new[] { "melee_on", "melee_off" })
            {
                string p = pass;
                if (p == "melee_off")
                {
                    Add(() =>
                    {
                        foreach (var c in new MonoBehaviour[] { _player.GetComponent<PlayerMelee>(), _player.GetComponent<CombatBody>(), _player.GetComponent<CombatBodyAnimator>() })
                            if (c != null) Object.DestroyImmediate(c);
                        var sh = _player.GetComponent<ShieldHolder>();
                        if (sh != null) { sh.Set(null); Object.DestroyImmediate(sh); }
                        foreach (var l in new[] { MeleeAnimationSet.LayerShield, MeleeAnimationSet.LayerGuard, MeleeAnimationSet.LayerFullBody })
                        {
                            int i = _animator.GetLayerIndex(l);
                            if (i >= 0) _animator.SetLayerWeight(i, 0f);
                        }
                        var tpc = _player.GetComponent<EndlessCombat.Character.PlayerMotor>();
                        if (tpc != null) { tpc.ActionLocked = false; tpc.MoveSpeedMultiplier = 1f; }
                        Log.WriteLine("---- melee runtime removed ----");
                    }, 0.5);
                }
                foreach (string w in Weapons)
                {
                    string wn = w;
                    Add(() =>
                    {
                        if (_shooter.IsHolstered) _shooter.SetHolstered(false);
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Weapons/{wn}.prefab");
                        _shooter.ReplaceWeapon(prefab != null ? prefab.GetComponent<WeaponController>() : null);
                        if (_inputs != null) _inputs.move = Vector2.zero;
                    }, 1.6);
                    Add(() => { Shot($"hold_{p}_{wn}_idle"); Dump($"{p} {wn} idle"); }, 0.1);
                    Add(() => { if (_inputs != null) { _inputs.move = new Vector2(0f, 1f); _inputs.sprint = false; } }, 1.2);
                    Add(() => { Shot($"hold_{p}_{wn}_walk"); Dump($"{p} {wn} walk"); }, 0.1);
                    Add(() => { if (_inputs != null) _inputs.move = Vector2.zero; }, 0.6);
                    if (wn == "Weapon_Pistol")
                    {
                        // Pistol is lowered unless aiming: check the raised aim pose too, then lower it again
                        Add(() => _shooter.ForceAim = true, 0.8);
                        Add(() => { Shot($"hold_{p}_{wn}_aim"); Dump($"{p} {wn} aim"); }, 0.1);
                        Add(() => _shooter.ForceAim = false, 0.8);
                        Add(() => { Shot($"hold_{p}_{wn}_lowered"); Dump($"{p} {wn} lowered again"); }, 0.1);
                    }
                }
            }
        }

        private static void Dump(string label)
        {
            var sb = new System.Text.StringBuilder($"{label}: hand={_shooter.HandType} weapon={(_shooter.CurrentWeapon != null ? _shooter.CurrentWeapon.name : "-")}\n");
            for (int i = 0; i < _animator.layerCount; i++)
            {
                var info = _animator.GetCurrentAnimatorStateInfo(i);
                var clips = _animator.GetCurrentAnimatorClipInfo(i);
                string clip = clips.Length > 0 ? clips[0].clip.name : "-";
                sb.Append($"   L{i} '{_animator.GetLayerName(i)}' w={_animator.GetLayerWeight(i):0.00} clip={clip} t={info.normalizedTime:0.00}\n");
            }
            foreach (var prm in _animator.parameters)
                if (prm.type == AnimatorControllerParameterType.Float) sb.Append($"   {prm.name}={_animator.GetFloat(prm.name):0.00}");
                else if (prm.type == AnimatorControllerParameterType.Bool) sb.Append($"   {prm.name}={_animator.GetBool(prm.name)}");
                else if (prm.type == AnimatorControllerParameterType.Int) sb.Append($"   {prm.name}={_animator.GetInteger(prm.name)}");
            Log.WriteLine(sb.ToString());
        }

        private static void Shot(string name)
        {
            Transform c = _player.transform;
            var a = Render(c, c.forward * 3.0f + Vector3.up * 1.3f);
            var b = Render(c, (c.forward - c.right * 1.4f).normalized * 3.0f + Vector3.up * 1.3f);
            var combo = new Texture2D(1024, 640, TextureFormat.RGB24, false);
            combo.SetPixels(0, 0, 512, 640, a.GetPixels());
            combo.SetPixels(512, 0, 512, 640, b.GetPixels());
            combo.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), combo.EncodeToPNG());
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
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
