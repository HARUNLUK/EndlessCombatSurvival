using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using EndlessCombat.AI;
using EndlessCombat.Combat;
using EndlessSurvival.Inventory;
using EndlessSurvival.Vehicle;

namespace EndlessSurvival.Survival.Editor
{
    /// <summary>
    /// Debug: plays Game.unity with a virtual gamepad the way a player would: main menu > new game, walk, aim, fire,
    /// every weapon slot, melee, stow, the Tab screen, pause, the action menu, an enemy that runs out of ammo, the
    /// vehicle (get in, drive, get out). Writes PoseShots/pt_log.txt and pt_*.png. Then quits.
    /// Run the editor (not batchmode) with: -executeMethod EndlessSurvival.Survival.Editor.NightPlaytest.Run
    /// Optional: -ptOnly combat|ui|vehicle|enemy to run one part.
    /// </summary>
    [InitializeOnLoad]
    public static class NightPlaytest
    {
        private const string Flag = "EndlessSurvival.NightPlaytest.Active";
        private static double _t0 = -1, _stepAt;
        private static int _step;
        private static readonly StringWriter Log = new StringWriter();
        private static int _errors;
        private static Gamepad _pad;
        private static GamepadState _state;
        private static readonly List<(System.Action act, double wait)> Plan = new List<(System.Action, double)>();
        private static Camera _cam;
        private static RenderTexture _rt;
        private static EnemyController _enemy;
        private static string _only;

        static NightPlaytest()
        {
            if (!EditorPrefs.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        private static bool _forcedFree;

        /// <summary>Started with -ptScene savas: the run plays in the action test scene.</summary>
        private static bool InTestScene
        {
            get
            {
                var args = System.Environment.GetCommandLineArgs();
                int i = System.Array.IndexOf(args, "-ptScene");
                return i >= 0 && i + 1 < args.Length && args[i + 1] == "savas";
            }
        }

        [MenuItem("Endless Survival/Debug/Gece Oynanış Testi (sanal gamepad)")]
        public static void Run()
        {
            EditorPrefs.SetBool(Flag, true);
            // The scripted run uses the scene's test weapons (rifle in the hand), not the real starting loadout
            PlayerPrefs.SetInt(EndlessSurvival.Inventory.PlayerEquipment.KeepSceneLoadoutKey, 1);
            PlayerPrefs.Save();
            // -ptScene savas: the action test scene (Test_Savas: flat arena, no endless world, no main menu, no saving)
            string scene = "Assets/Scenes/Game.unity";
            if (InTestScene)
            {
                EndlessSurvival.Testing.Editor.TestSceneBuilder.EnsureBuilt();
                scene = EndlessSurvival.Testing.Editor.TestSceneBuilder.CombatScenePath;
            }
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.EnterPlaymode();
        }

        private static void OnLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            _errors++;
            if (_errors <= 40) Log.WriteLine($"[{type}] {condition}\n    {string.Join(" | ", (stack ?? "").Split('\n').Take(3))}");
        }

        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PoseShots"));

        private static void Finish()
        {
            // The character the test made in an empty slot goes away again (deleting it also deselects it, so
            // closing the editor does not save a journey for it)
            if (_testSlot >= 0)
            {
                EndlessSurvival.Save.CharacterProfiles.Delete(_testSlot);
                Log.WriteLine($"test character in slot {_testSlot + 1} deleted: exists={EndlessSurvival.Save.CharacterProfiles.Exists(_testSlot)}");
                _testSlot = -1;
            }
            Log.WriteLine($"errors/exceptions logged: {_errors}");
            EditorPrefs.DeleteKey(Flag);
            PlayerPrefs.DeleteKey(EndlessSurvival.Inventory.PlayerEquipment.KeepSceneLoadoutKey);
            PlayerPrefs.Save();
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "pt_log.txt"), Log.ToString());
            if (_pad != null) InputSystem.RemoveDevice(_pad);
            if (System.Environment.GetCommandLineArgs().Contains("-executeMethod")) EditorApplication.Exit(0);
            else EditorApplication.ExitPlaymode();
        }

        // ------------------------------------------------------------------ virtual gamepad

        private static void Send() => InputSystem.QueueStateEvent(_pad, _state);

        private static void Down(GamepadButton b) { _state = _state.WithButton(b, true); Send(); }
        private static void Up(GamepadButton b) { _state = _state.WithButton(b, false); Send(); }
        private static void Stick(Vector2 v) { _state.leftStick = v; Send(); }
        private static void LT(float v) { _state.leftTrigger = v; Send(); }
        private static void RT(float v) { _state.rightTrigger = v; Send(); }

        private static void Add(System.Action a, double wait) => Plan.Add((a, wait));

        /// <summary>Press and release a button (two steps).</summary>
        private static void Tap(GamepadButton b, double after = 0.4)
        {
            Add(() => Down(b), 0.12);
            Add(() => Up(b), after);
        }

        private static void Note(string text) => Add(() => Log.WriteLine(text), 0.0);

        /// <summary>Closes whatever screen is open with B (a note opened by X, for example).</summary>
        private static void CloseUi()
        {
            Add(() => { if (SurvivalUIState.AnyOpen) { Log.WriteLine("   (a screen was open: B)"); Down(GamepadButton.East); } }, 0.15);
            Add(() => Up(GamepadButton.East), 0.5);
        }

        /// <summary>Does the crosshair ray hit the enemy?</summary>
        private static void CrosshairCheck(string label)
        {
            var cam = Camera.main;
            if (cam == null || _enemy == null) return;
            bool on = false;
            foreach (var h in Physics.RaycastAll(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), 100f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.transform.IsChildOf(_enemy.transform)) { on = true; Log.WriteLine($"   crosshair hit {h.collider.name} ({h.collider.GetType().Name}) layer={h.collider.gameObject.layer} @{h.distance:0.00} y={h.point.y:0.00}"); }
            Log.WriteLine($"{label}: crosshair on enemy = {on} cam={cam.transform.position:F2} fwd={cam.transform.forward:F2}");
            var sh = P<PlayerShooter>();
            if (sh != null) Log.WriteLine($"   GetShotPoint={sh.GetShotPoint(out var to):F2} trace={(to.HasValue ? to.Value.ToString("F2") : "-")}");
        }

        // ------------------------------------------------------------------ helpers

        private static GameObject Player => GameObject.FindGameObjectWithTag("Player");
        private static T P<T>() where T : Component { var p = SurvivalDirector.Player != null ? SurvivalDirector.Player : Player; return p != null ? p.GetComponent<T>() : null; }

        private static void Screen(string name)
        {
            Directory.CreateDirectory(OutDir);
            ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, $"pt_{name}.png"));
        }

        private static void State(string label)
        {
            var player = SurvivalDirector.Player != null ? SurvivalDirector.Player : Player;
            if (player == null) { Log.WriteLine($"{label}: no player"); return; }
            var shooter = player.GetComponent<PlayerShooter>();
            var health = player.GetComponent<PlayerHealth>();
            var tpc = player.GetComponent<EndlessCombat.Character.PlayerMotor>();
            var actor = player.GetComponent<CombatBody>();
            var w = shooter != null ? shooter.CurrentWeapon : null;
            Log.WriteLine($"{label}: active={player.activeInHierarchy} pos={player.transform.position:F1} up={player.transform.up:F2} grounded={(tpc != null && tpc.Grounded)} " +
                          $"hp={(health != null ? health.CurrentHealth : -1):0} hand={(shooter != null ? shooter.HandType.ToString() : "-")} holstered={(shooter != null && shooter.IsHolstered)} " +
                          $"aiming={(shooter != null && shooter.IsAiming)} weapon={(w != null ? w.name + " " + w.CurrentAmmo + "/" + w.ReserveAmmo : "none")} actor={(actor != null ? actor.State.ToString() : "-")} " +
                          $"guard={(actor != null && actor.IsGuarding)} cs={(PlayerCombatState.Player != null ? PlayerCombatState.Player.Current.ToString() : "-")} ui={SurvivalUIState.AnyOpen} hub={SurvivalPanel.IsHubOpen} action={ActionMenu.IsOpen} timeScale={Time.timeScale} pad={GameInput.UsingPad}");
        }

        private static void Shot(string name)
        {
            var player = SurvivalDirector.Player != null ? SurvivalDirector.Player : Player;
            if (player == null || !player.activeInHierarchy) return;
            if (_cam == null)
            {
                var go = new GameObject("PtCaptureCam");
                _cam = go.AddComponent<Camera>();
                _cam.fieldOfView = 35f;
                _cam.nearClipPlane = 0.05f;
                _cam.farClipPlane = 80f;
                _cam.enabled = false;
                _rt = new RenderTexture(480, 600, 24);
                _cam.targetTexture = _rt;
            }
            Transform c = player.transform;
            var views = new[] { c.forward * 3.3f, (c.forward - c.right).normalized * 3.3f, (-c.forward + c.right * 0.6f).normalized * 3.3f };
            var combo = new Texture2D(480 * views.Length, 600, TextureFormat.RGB24, false);
            for (int i = 0; i < views.Length; i++)
            {
                _cam.transform.position = c.position + views[i] + Vector3.up * 1.25f;
                _cam.transform.LookAt(c.position + Vector3.up * 1.0f);
                _cam.Render();
                RenderTexture.active = _rt;
                var tex = new Texture2D(480, 600, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 480, 600), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                combo.SetPixels(480 * i, 0, 480, 600, tex.GetPixels());
                Object.DestroyImmediate(tex);
            }
            combo.Apply();
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, $"pt_{name}.png"), combo.EncodeToPNG());
            Object.DestroyImmediate(combo);
            State("   shot " + name);
        }

        private static ItemDefinition It(string n) => SurvivalSettings.Instance.Item(n);

        private static void Give(string item, int count = 1)
        {
            var inv = P<PlayerInventory>();
            var it = It(item);
            if (inv == null || it == null) { Log.WriteLine($"give {item}: missing (inv={(inv != null)} item={(it != null)})"); return; }
            int added = inv.AddItem(it, count);
            if (added <= 0) Log.WriteLine($"give {item}: not accepted");
        }

        private static string _savePath;
        private static byte[] _saveBackup;

        private static string EquipSummary()
        {
            var eq = P<PlayerEquipment>();
            if (eq == null) return "no equipment";
            string N(EquipSlot s) { var i = eq.Get(s); return i != null ? i.name : "-"; }
            var w = P<PlayerShooter>() != null ? P<PlayerShooter>().CurrentWeapon : null;
            return $"long={N(EquipSlot.LongGun)} pistol={N(EquipSlot.Pistol)} melee={N(EquipSlot.Melee)} shield={N(EquipSlot.Shield)} hand={(w != null ? w.name + " " + w.CurrentAmmo : "none")}";
        }

        private static void Equip(string item)
        {
            Give(item);
            var eq = P<PlayerEquipment>();
            string msg = eq != null ? eq.Equip(It(item)) : "no equipment";
            Log.WriteLine($"equip {item}: {msg}");
        }

        private static float EnemyHealth(EnemyController e)
        {
            if (e == null) return -1f;
            var f = typeof(EnemyController).GetField("currentHealth", BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? (float)f.GetValue(e) : -1f;
        }

        private static WeaponController EnemyWeapon(EnemyController e)
        {
            if (e == null) return null;
            var f = typeof(EnemyController).GetField("weapon", BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(e) as WeaponController : null;
        }

        /// <summary>Puts an enemy under the crosshair, 8-20 m ahead.</summary>
        private static void SpawnEnemyAtCrosshair(string prefabName, EnemyController.EnemyState state, bool passive = false)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Enemies/{prefabName}.prefab");
            var cam = Camera.main;
            if (prefab == null || cam == null) { Log.WriteLine("spawn enemy: missing prefab or camera"); return; }
            Vector3 pos = Vector3.zero;
            bool found = false;
            for (float d = 7f; d <= 24f && !found; d += 0.5f)
            {
                Vector3 p = cam.transform.position + cam.transform.forward * d;
                if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                {
                    float h = p.y - hit.point.y;
                    if (h > 0.6f && h < 1.65f) { pos = hit.point; found = true; }
                }
            }
            if (!found)
            {
                var pl = SurvivalDirector.Player.transform;
                pos = pl.position + pl.forward * 9f;
                if (Physics.Raycast(pos + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
            }
            var go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(-Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up)));
            _enemy = go.GetComponent<EnemyController>();
            if (_enemy != null) _enemy.initialState = state;
            if (_enemy != null && passive)
            {
                _enemy.maxHealth = 400f;
                _enemy.attackCooldown = 999f;
                _enemy.detectionRange = 0.01f; // stands still: a target
                _enemy.moveSpeed = 0f;
            }
            Log.WriteLine($"spawned {prefabName} at {pos:F1} underCrosshair={found} dist={Vector3.Distance(pos, SurvivalDirector.Player.transform.position):0.0}");
        }

        private static void EnemyState(string label)
        {
            if (_enemy == null) { Log.WriteLine($"{label}: enemy gone (dead)"); return; }
            var w = EnemyWeapon(_enemy);
            Log.WriteLine($"{label}: enemy hp={EnemyHealth(_enemy):0} dead={_enemy.IsDead} state={_enemy.State} kind={_enemy.WeaponKind} outOfAmmo={_enemy.OutOfAmmo} " +
                          $"weapon={(w != null ? w.name + " " + w.CurrentAmmo + "/" + w.ReserveAmmo + (w.IsReloading ? " reloading" : "") : "none")} " +
                          $"dist={Vector3.Distance(_enemy.transform.position, SurvivalDirector.Player.transform.position):0.0}");
        }

        private static void Heal()
        {
            var h = P<PlayerHealth>();
            if (h != null) { h.Heal(1000f); h.StopBleeding(); }
        }

        private static void ClearEnemies()
        {
            foreach (var e in EnemyController.Active.ToArray())
                if (e != null && Vector3.Distance(e.transform.position, SurvivalDirector.PlayerPosition) < 120f) Object.Destroy(e.gameObject);
            _enemy = null;
        }

        // ------------------------------------------------------------------ plan

        private static bool Part(string name) => string.IsNullOrEmpty(_only) || _only == name;

        // ------------------------------------------------------------------ combat rules (Combat_Architecture.md)

        private static int _rulesPass, _rulesFail, _serial0, _ammo0;
        private static float _hp0, _dur0;

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _rulesPass++; else _rulesFail++;
            Log.WriteLine($"RULES {name}: {(ok ? "PASS" : "FAIL")} ({detail})");
        }

        private static PlayerCombatState CS => P<PlayerCombatState>();
        private static WeaponController HandWeapon => P<PlayerShooter>() != null ? P<PlayerShooter>().CurrentWeapon : null;

        /// <summary>Takes a loadout slot into the hand without the drop rule (the one in the hand is holstered first).</summary>
        private static void Draw(PlayerLoadout.Slot slot)
        {
            var l = P<PlayerLoadout>();
            var s = P<PlayerShooter>();
            if (l == null || s == null) { Log.WriteLine("draw: no loadout"); return; }
            if (l.Active == slot && !s.IsHolstered) return;
            var t = P<PlayerThrower>();
            if (t != null && t.HasItem) t.Stow();
            l.StowAll();
            l.Pick((int)slot);
        }

        /// <summary>LB tap on the virtual gamepad (holster / draw).</summary>
        private static void LbTap(double after)
        {
            Add(() => Down(GamepadButton.LeftShoulder), 0.08);
            Add(() => Up(GamepadButton.LeftShoulder), after);
        }

        /// <summary>LB held (the selection map opens) + one button, then LB released.</summary>
        private static void LbPick(GamepadButton b, double after)
        {
            Add(() => Down(GamepadButton.LeftShoulder), 0.45);
            Add(() => Log.WriteLine($"   map open={GameInput.SelectMapOpen}"), 0.0);
            Tap(b, 0.15);
            Add(() => Up(GamepadButton.LeftShoulder), after);
        }

        private static WorldPickup DroppedNear()
        {
            WorldPickup best = null;
            float bestD = 6f;
            foreach (var p in Object.FindObjectsByType<WorldPickup>(FindObjectsSortMode.None))
            {
                if (!p.fromHand) continue;
                float d = Vector3.Distance(p.transform.position, SurvivalDirector.PlayerPosition);
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        private static bool RightDrawn => P<PlayerLoadout>() != null && P<PlayerLoadout>().RightDrawn;

        /// <summary>Fires a few rounds so the magazine is not full (a reload has something to do).</summary>
        private static void SpendRounds()
        {
            Add(() => LT(1f), 0.8);
            Add(() => RT(1f), 0.35);
            Add(() => RT(0f), 0.2);
            Add(() => LT(0f), 0.4);
        }


        // ------------------------------------------------------------------ models: every pick state, close shots

        /// <summary>
        /// Simple models (SimpleWeaponModels, 2026-11-16): each weapon / shield / torch / throwable pick is put in
        /// the hand and shot close up from three sides (PoseShots/pt_m*.png). Picks go through PlayerLoadout.Pick.
        /// </summary>
        private static void AddModelsPart()
        {
            Add(() =>
            {
                MoveToClearSpot();
                Give("Item_RifleAmmo", 4); Give("Item_PistolAmmo", 4);
                Give("Item_Torch", 2); Give("Item_Rock", 3); Give("Item_Grenade", 2);
                var eq = P<PlayerEquipment>();
                if (eq != null) { eq.Unequip(EquipSlot.Shield); eq.Unequip(EquipSlot.Melee); }
                P<PlayerLoadout>()?.StowAll();
                Log.WriteLine("models: start " + EquipSummary());
            }, 0.8);
            Add(() => MShot("m01_fists"), 0.1);

            ModelState("m02_pistol", () => { Equip("Item_Pistol"); PickClean(PlayerLoadout.PickPistol); });
            ModelState("m03_smg", () => { Equip("Item_Smg"); PickClean(PlayerLoadout.PickLongGun); });
            ModelState("m04_assault_rifle", () => { Equip("Item_AssaultRifle"); PickClean(PlayerLoadout.PickLongGun); });
            ModelState("m05_hunting_rifle", () => { Equip("Item_HuntingRifle"); PickClean(PlayerLoadout.PickLongGun); });
            ModelState("m06_knife", () => { Equip("Item_Knife"); PickClean(PlayerLoadout.PickMelee); });
            ModelState("m07_bat", () => { Equip("Item_BaseballBat"); PickClean(PlayerLoadout.PickMelee); });
            ModelState("m08_club", () => { Equip("Item_WoodenClub"); PickClean(PlayerLoadout.PickMelee); });
            ModelState("m09_wooden_shield_club", () => { Equip("Item_WoodenShield"); PickClean(PlayerLoadout.PickMelee); });
            ModelState("m10_riot_shield_pistol", () => { Equip("Item_RiotShield"); PickClean(PlayerLoadout.PickPistol); });
            // The torch takes the left hand: the shield on the arm falls (drop rule)
            ModelState("m11_torch_pistol", () => { P<PlayerLoadout>().Pick(PlayerLoadout.PickTorch); });
            ModelState("m12_torch_club", () => { PickClean(PlayerLoadout.PickMelee); });
            ModelState("m13_torch_rock", () => { PickClean(PlayerLoadout.PickRock); });
            ModelState("m14_grenade", () => { P<PlayerLoadout>().Pick(PlayerLoadout.PickTorch); PickClean(PlayerLoadout.PickGrenade); });
            ModelState("m15_stowed", () =>
            {
                var t = P<PlayerThrower>(); if (t != null && t.HasItem) t.Stow();
                var dropped = DroppedNear();
                if (dropped != null) Log.WriteLine("   shield on the ground: " + (dropped.item != null ? dropped.item.name : "?"));
                Equip("Item_WoodenShield");
                P<PlayerLoadout>().StowAll();
                if (P<PlayerLoadout>().ShieldOnArm) P<PlayerLoadout>().Pick(PlayerLoadout.PickShield); // shield to the back
            });

            // The selection map (placeholder look) with the torch cell
            Add(() => Down(GamepadButton.LeftShoulder), 0.5);
            Add(() => Screen("m16_map"), 0.3);
            Add(() => Up(GamepadButton.LeftShoulder), 0.4);
            Add(() => Log.WriteLine("models: end " + EquipSummary()), 0.1);
        }

        private static void ModelState(string name, System.Action act)
        {
            Add(() =>
            {
                act();
                var l = P<PlayerLoadout>();
                var torch = P<EndlessCombat.Combat.TorchHolder>();
                var shield = P<ShieldHolder>();
                Log.WriteLine($"   {name}: {EquipSummary()} rightDrawn={l.RightDrawn} shieldOnArm={l.ShieldOnArm} torch={(torch != null && torch.Held)} " +
                              $"throwable={(P<PlayerThrower>() != null ? P<PlayerThrower>().Held.ToString() : "-")} shieldModel={(shield != null && shield.Visual != null ? shield.Visual.name : "-")}");
            }, 0.9); // the draw time passes and the pose settles
            Add(() => MShot(name), 0.1);
        }

        /// <summary>A pick without the drop rule: the right hand is emptied first (as after an LB tap).</summary>
        private static void PickClean(int code)
        {
            var l = P<PlayerLoadout>();
            if (l == null) return;
            var t = P<PlayerThrower>();
            if (t != null && t.HasItem) t.Stow();
            l.StowAll();
            l.Pick(code);
        }

        /// <summary>Close shots of the character from three sides (front right, front left, back left).</summary>
        private static void MShot(string name)
        {
            var player = SurvivalDirector.Player != null ? SurvivalDirector.Player : Player;
            if (player == null || !player.activeInHierarchy) return;
            if (_mcam == null)
            {
                var go = new GameObject("PtModelCam");
                _mcam = go.AddComponent<Camera>();
                _mcam.fieldOfView = 34f;
                _mcam.nearClipPlane = 0.05f;
                _mcam.farClipPlane = 60f;
                _mcam.enabled = false;
                _mrt = new RenderTexture(560, 700, 24) { antiAliasing = 4 };
                _mcam.targetTexture = _mrt;
            }
            Transform c = player.transform;
            var views = new[]
            {
                (c.forward + c.right * 0.8f).normalized * 2.2f,
                (c.forward - c.right * 0.9f).normalized * 2.2f,
                (-c.forward - c.right * 0.7f).normalized * 2.2f,
            };
            var combo = new Texture2D(560 * views.Length, 700, TextureFormat.RGB24, false);
            for (int i = 0; i < views.Length; i++)
            {
                _mcam.transform.position = c.position + views[i] + Vector3.up * 1.35f;
                _mcam.transform.LookAt(c.position + Vector3.up * 1.05f);
                _mcam.Render();
                RenderTexture.active = _mrt;
                var tex = new Texture2D(560, 700, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 560, 700), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                combo.SetPixels(560 * i, 0, 560, 700, tex.GetPixels());
                Object.DestroyImmediate(tex);
            }
            combo.Apply();
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, $"pt_{name}.png"), combo.EncodeToPNG());
            Object.DestroyImmediate(combo);
        }

        private static Camera _mcam;
        private static RenderTexture _mrt;

        /// <summary>Moves the player to a spot with nothing standing within 3 m (walls of the arena would hide the model).</summary>
        private static void MoveToClearSpot()
        {
            var player = SurvivalDirector.Player != null ? SurvivalDirector.Player : Player;
            if (player == null) return;
            Vector3 start = player.transform.position;
            for (int ring = 0; ring <= 8; ring++)
            {
                int steps = ring == 0 ? 1 : 12;
                for (int k = 0; k < steps; k++)
                {
                    Vector3 p = start + Quaternion.Euler(0f, k * 30f, 0f) * Vector3.forward * (ring * 4f);
                    if (!Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out RaycastHit ground, 40f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    p = ground.point;
                    bool blocked = false;
                    foreach (var col in Physics.OverlapSphere(p + Vector3.up * 1.3f, 3f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (col.transform.IsChildOf(player.transform)) continue;
                        if (col.bounds.max.y < p.y + 0.35f) continue; // the ground, curbs
                        blocked = true;
                        break;
                    }
                    if (blocked) continue;
                    var cc = player.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    player.transform.position = p + Vector3.up * 0.05f;
                    if (cc != null) cc.enabled = true;
                    Log.WriteLine($"models: clear spot {p:F1} (ring {ring})");
                    return;
                }
            }
            Log.WriteLine("models: no clear spot found, shots from the start position");
        }
        private static void AddRulesPart()
        {
            Add(() => { ClearEnemies(); Heal(); _rulesPass = _rulesFail = 0; Log.WriteLine("--- combat rules"); PlayerCombatState.DebugLog = m => Log.WriteLine($"      cs f{Time.frameCount} {m}"); }, 0.2);
            Add(() => { Give("Item_RifleAmmo", 4); Give("Item_PistolAmmo", 4); Equip("Item_Pistol"); Equip("Item_WoodenClub"); }, 0.4);
            CloseUi();
            Add(() => Draw(PlayerLoadout.Slot.LongGun), 1.0);
            // Off the start spot first (the car there is in reach: X would mean "get in", not reload)
            Add(() => Stick(new Vector2(0f, 1f)), 4.5);
            Add(() => Stick(Vector2.zero), 0.5);
            Add(() => State("rules: rifle in hand"), 0.1);

            // R1: RB during a reload cuts the reload short and strikes (gun butt)
            SpendRounds();
            Tap(GamepadButton.West, 0.3);
            Add(() => Log.WriteLine($"   R1 before: cs={CS?.Current} reloading={HandWeapon?.IsReloading} interact={GameInput.InteractAvailable} locked={CombatBody.PlayerLocked}"), 0.0);
            Tap(GamepadButton.RightShoulder, 0.05);
            Add(() => Check("R1 strike cuts reload", HandWeapon != null && !HandWeapon.IsReloading && CS.Current == CombatAction.Strike,
                $"cs={CS?.Current} reloading={HandWeapon?.IsReloading} actor={P<CombatBody>()?.State}"), 1.0);

            // R2: a hit reaction cancels the reload
            SpendRounds();
            Tap(GamepadButton.West, 0.3);
            Add(() => P<CombatBody>().Stagger(0.5f, Vector3.zero), 0.1);
            Add(() => Check("R2 stagger cancels reload", HandWeapon != null && !HandWeapon.IsReloading && CS.Current == CombatAction.Stagger,
                $"cs={CS?.Current} reloading={HandWeapon?.IsReloading}"), 1.0);

            // R3: the aim drops during a hit reaction and comes back after it (LT still held)
            Add(() => LT(1f), 0.8);
            Add(() => { Log.WriteLine($"   R3 aiming before={P<PlayerShooter>().IsAiming}"); P<CombatBody>().Stagger(0.6f, Vector3.zero); }, 0.15);
            Add(() => Check("R3a no aim while staggered", !P<PlayerShooter>().IsAiming, $"cs={CS?.Current} aiming={P<PlayerShooter>().IsAiming}"), 0.9);
            Add(() => { Check("R3b aim back after", P<PlayerShooter>().IsAiming, $"cs={CS?.Current} aiming={P<PlayerShooter>().IsAiming}"); LT(0f); }, 0.4);

            // R4: a rock picked from the map comes to the right hand (rifle holstered first: nothing drops),
            //     LT aims the throw, RT throws instead of firing, the next rock comes by itself, LB tap puts it back
            Add(() => P<PlayerInventory>().AddItem(PlayerThrower.ItemOf(PlayerThrower.Kind.Rock), 3), 0.1);
            LbTap(0.9); // rifle to its holster
            Add(() => Check("R4a LB tap holsters", P<PlayerShooter>().IsHolstered, $"holstered={P<PlayerShooter>().IsHolstered} cs={CS?.Current}"), 0.1);
            Add(() => Down(GamepadButton.LeftShoulder), 0.45); // RT on the map: rock (the trigger is an axis)
            Add(() => RT(1f), 0.15);
            Add(() => RT(0f), 0.1);
            Add(() => Up(GamepadButton.LeftShoulder), 0.5);
            Add(() =>
            {
                _ammo0 = P<PlayerThrower>().Count(PlayerThrower.Kind.Rock);
                Check("R4b rock in the hand", P<PlayerThrower>().HasItem && P<PlayerThrower>().Held == PlayerThrower.Kind.Rock && DroppedNear() == null,
                    $"held={P<PlayerThrower>().Held} rocks={_ammo0} dropped={(DroppedNear() != null)}");
                LT(1f);
            }, 0.5);
            Add(() => { Log.WriteLine($"   R4 aiming gun={P<PlayerShooter>().IsAiming} cs={CS?.Current}"); RT(1f); }, 0.15);
            Add(() => RT(0f), 1.2);
            Add(() =>
            {
                var t = P<PlayerThrower>();
                Check("R4c RT throws, next rock comes", t.Count(PlayerThrower.Kind.Rock) == _ammo0 - 1 && t.HasItem && !P<PlayerShooter>().IsAiming,
                    $"rocks {_ammo0}->{t.Count(PlayerThrower.Kind.Rock)} held={t.Held} gunAim={P<PlayerShooter>().IsAiming}");
                LT(0f);
            }, 0.3);
            LbTap(0.6); // rock back to the bag
            Add(() => Check("R4d LB tap stows the rock", !P<PlayerThrower>().HasItem, $"held={P<PlayerThrower>().Held}"), 0.1);

            // R5: picking another weapon with the rifle in the hand (not holstered) cuts the reload short and drops
            //     the rifle with its magazine; picking it up puts it back into its slot with that magazine
            Add(() => Draw(PlayerLoadout.Slot.LongGun), 1.0);
            SpendRounds();
            Tap(GamepadButton.West, 0.3);
            Add(() => { _ammo0 = HandWeapon != null ? HandWeapon.CurrentAmmo : -1; Log.WriteLine($"   R5 reloading={HandWeapon?.IsReloading} mag={_ammo0}"); }, 0.0);
            LbPick(GamepadButton.West, 0.6); // X on the map: pistol
            Add(() =>
            {
                var l = P<PlayerLoadout>();
                var drop = DroppedNear();
                Check("R5 pick drops the rifle, cuts reload", l.Weapon(PlayerLoadout.Slot.LongGun) == null && drop != null && drop.magazineRounds == _ammo0
                    && P<PlayerShooter>().HandType == WeaponType.Pistol,
                    $"rifle slot={(l.Weapon(PlayerLoadout.Slot.LongGun) != null)} drop={(drop != null)} mag {_ammo0}->{drop?.magazineRounds} hand={P<PlayerShooter>().HandType}");
                if (drop != null) drop.Interact(P<PlayerInventory>());
            }, 0.3);
            Add(() =>
            {
                var rifle = P<PlayerLoadout>().Weapon(PlayerLoadout.Slot.LongGun);
                Check("R5b pickup back to slot with magazine", rifle != null && rifle.CurrentAmmo == _ammo0 && P<PlayerShooter>().HandType == WeaponType.Pistol,
                    $"rifle={(rifle != null)} mag={rifle?.CurrentAmmo} want {_ammo0} hand={P<PlayerShooter>().HandType}");
            }, 0.2);

            // R9: LB tap takes the holster time (still in the hand right after), a second tap draws with the draw time
            LbTap(0.05);
            Add(() => Check("R9a holster takes time", !P<PlayerShooter>().IsHolstered && CS.Current == CombatAction.Draw,
                $"holstered={P<PlayerShooter>().IsHolstered} cs={CS?.Current}"), 0.6);
            Add(() => Check("R9b holstered after its time", P<PlayerShooter>().IsHolstered, $"holstered={P<PlayerShooter>().IsHolstered}"), 0.0);
            LbTap(0.05);
            Add(() => Check("R9c draw takes time", RightDrawn && CS.Current == CombatAction.Draw, $"drawn={RightDrawn} cs={CS?.Current}"), 0.6);
            Add(() => Check("R9d drawn after its time", RightDrawn && CS.Current != CombatAction.Draw, $"drawn={RightDrawn} cs={CS?.Current}"), 0.0);

            // R10: LT with the pistol holstered draws it and aims; releasing LT keeps it in the hand
            LbTap(0.7);
            Add(() => LT(1f), 0.9);
            Add(() => { Check("R10a LT draws + aims", RightDrawn && P<PlayerShooter>().IsAiming, $"drawn={RightDrawn} aiming={P<PlayerShooter>().IsAiming}"); LT(0f); }, 0.5);
            Add(() => Check("R10b LT release keeps it", RightDrawn, $"drawn={RightDrawn} holstered={P<PlayerShooter>().IsHolstered}"), 0.0);

            // R11: RT held through the draw fires once as soon as the gun is ready and aimed
            LbTap(0.7);
            Add(() => { var pw = P<PlayerLoadout>().Weapon(PlayerLoadout.Slot.Pistol); pw.RestoreMagazine(99); _ammo0 = pw.CurrentAmmo; LT(1f); RT(1f); }, 1.0);
            Add(() =>
            {
                int now = P<PlayerLoadout>().Weapon(PlayerLoadout.Slot.Pistol).CurrentAmmo;
                Check("R11 RT during draw fires once when ready", now == _ammo0 - 1, $"mag {_ammo0}->{now}");
                RT(0f); LT(0f);
            }, 0.4);

            // R12: a hit during the draw does not stop it
            LbTap(0.7);
            LbTap(0.0);
            Add(() => P<CombatBody>().Stagger(0.4f, Vector3.zero), 1.0);
            Add(() => Check("R12 stagger keeps the draw", RightDrawn, $"drawn={RightDrawn} cs={CS?.Current}"), 0.0);

            // R13: holstered first, then another pick: nothing drops
            LbTap(0.7);
            LbPick(GamepadButton.RightShoulder, 0.8); // RB on the map: melee (club)
            Add(() =>
            {
                var l = P<PlayerLoadout>();
                Check("R13 holstered pick drops nothing", l.Weapon(PlayerLoadout.Slot.Pistol) != null && DroppedNear() == null && P<PlayerShooter>().HandType == WeaponType.Melee,
                    $"pistol={(l.Weapon(PlayerLoadout.Slot.Pistol) != null)} dropped={(DroppedNear() != null)} hand={P<PlayerShooter>().HandType}");
            }, 0.1);

            // R14: RT with a melee weapon is one heavy strike
            Add(() => RT(1f), 0.15);
            Add(() => { Check("R14 RT heavy strike", CS.Current == CombatAction.Strike && P<CombatBody>().CurrentIsHeavy, $"cs={CS?.Current} heavy={P<CombatBody>().CurrentIsHeavy}"); RT(0f); }, 2.0);

            // R15: the D-pad does nothing to the weapons
            Add(() => _serial0 = (int)P<PlayerLoadout>().Active, 0.0);
            Tap(GamepadButton.DpadUp, 0.3);
            Tap(GamepadButton.DpadLeft, 0.3);
            Add(() => Check("R15 D-pad no weapon change", (int)P<PlayerLoadout>().Active == _serial0 && RightDrawn, $"active={P<PlayerLoadout>().Active} drawn={RightDrawn}"), 0.1);

            // R6: a strike pressed near the end of the last one comes out by itself (input buffer)
            Add(() => Draw(PlayerLoadout.Slot.Melee), 0.8);
            Add(() => { _serial0 = P<CombatBody>().AttackSerial; Log.WriteLine($"   R6 hand={P<PlayerShooter>().HandType}"); }, 0.0);
            Tap(GamepadButton.RightShoulder, 0.28);   // strike 1 (0.55 s); the next press lands 0.40 s in
            Tap(GamepadButton.RightShoulder, 1.0);    // pressed during strike 1: waits, then strike 2
            Add(() => Check("R6 buffered strike", P<CombatBody>().AttackSerial == _serial0 + 2,
                $"strikes={P<CombatBody>().AttackSerial - _serial0}"), 0.2);

            // R7: hit regions from the head bone
            Add(() => SpawnEnemyAtCrosshair("Enemy_Melee_Bat", EnemyController.EnemyState.Idle, true), 0.8);
            Add(() =>
            {
                if (_enemy == null) { Check("R7 head region", false, "no enemy"); return; }
                var anim = _enemy.GetComponentInChildren<Animator>();
                var col = _enemy.GetComponent<Collider>();
                Transform head = anim != null ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
                if (head == null || col == null) { Check("R7 head region", false, $"head={(head != null)} col={(col != null)}"); return; }
                var atHead = Damage.RegionOf(col, head.position + Vector3.up * 0.05f);
                var atFeet = Damage.RegionOf(col, _enemy.transform.position + Vector3.up * 0.4f);
                Check("R7 head region", atHead == BodyRegion.Head && atFeet != BodyRegion.Head, $"head={atHead} low={atFeet}");
                float hp = EnemyHealth(_enemy);
                Damage.HeadMultiplier = 2f;
                Damage.Deal(_enemy, new DamageInfo(10f, DamageType.Bullet, head.position, Vector3.up) { Region = BodyRegion.Head });
                Damage.HeadMultiplier = 1f;
                Check("R7b head multiplier", Mathf.Abs(hp - EnemyHealth(_enemy) - 20f) < 0.01f, $"hp {hp:0}->{EnemyHealth(_enemy):0}");
            }, 0.3);
            Add(() => { ClearEnemies(); Heal(); }, 0.3);

            // R8: the raised shield stops a bullet from the front, not from behind (DamageInfo source direction)
            Add(() => Equip("Item_WoodenShield"), 0.4);
            CloseUi();
            Add(() => Draw(PlayerLoadout.Slot.Melee), 0.6);
            Add(() => LT(1f), 0.6);
            Add(() =>
            {
                var hp = P<PlayerHealth>();
                var shield = P<ShieldHolder>();
                Transform t = SurvivalDirector.Player.transform;
                _hp0 = hp.CurrentHealth;
                _dur0 = shield != null ? shield.Durability : -1f;
                hp.TakeDamage(new DamageInfo(10f, DamageType.Bullet, t.position + Vector3.up * 1.2f + t.forward * 0.4f, t.forward) { SourceDirection = t.forward });
                float front = _hp0 - hp.CurrentHealth;
                Heal();
                float hp1 = hp.CurrentHealth;
                hp.TakeDamage(new DamageInfo(10f, DamageType.Bullet, t.position + Vector3.up * 1.2f - t.forward * 0.4f, -t.forward) { SourceDirection = -t.forward });
                float back = hp1 - hp.CurrentHealth;
                Check("R8 shield front / back", P<CombatBody>().IsGuarding && front < back && shield.Durability < _dur0,
                    $"guard={P<CombatBody>().IsGuarding} front={front:0.0} back={back:0.0} shield {_dur0:0}->{shield?.Durability:0}");
                LT(0f);
                Heal();
            }, 0.4);

            Add(() => { PlayerCombatState.DebugLog = null; Log.WriteLine($"RULES total: {_rulesPass} pass, {_rulesFail} fail"); }, 0.1);
        }

        private static void BuildPlan()
        {
            // --- main menu with the gamepad: the character slots. The test never touches a slot that is in use:
            //     it makes its own character in the last empty slot and deletes it when it finishes.
            _testSlot = -1;
            for (int i = EndlessSurvival.Save.CharacterProfiles.SlotCount - 1; i >= 0 && _testSlot < 0; i--)
                if (EndlessSurvival.Save.CharacterProfiles.Peek(i) == null) _testSlot = i;

            Add(() => { State("main menu"); Screen("00_menu"); }, 0.6);
            if (InTestScene)
            {
                // The test scene has no main menu and keeps no character: straight into the arena
                _testSlot = -1;
                Add(() => Log.WriteLine($"scene: action test scene, arena={EndlessSurvival.Testing.TestArena.Active}"), 1.0);
            }
            else if (_testSlot < 0)
            {
                // Every slot holds a character of the player: play without one (nothing is kept)
                Add(() =>
                {
                    Log.WriteLine("menu: no empty character slot, the test runs without a character");
                    var menus = Object.FindAnyObjectByType<GameMenus>();
                    if (menus != null) menus.CloseMenu();
                }, 2.0);
            }
            else
            {
                for (int i = 0; i < _testSlot; i++) Tap(GamepadButton.DpadDown, 0.3);
                Add(() => Screen("00b_menu_focus"), 0.4);
                Tap(GamepadButton.South, 0.6); // the empty slot: "Yeni karakter" screen
                Add(() => Screen("00c_new_character"), 0.4);
                Tap(GamepadButton.South, 0.6); // Oluştur
                Add(() =>
                {
                    var p = EndlessSurvival.Save.CharacterProfiles.Peek(_testSlot);
                    Log.WriteLine($"menu: character made in slot {_testSlot + 1}: {(p != null ? p.name : "MISSING")}");
                    Screen("00d_character");
                }, 0.4);
                Tap(GamepadButton.South, 9.0); // Yeni yolculuk: the scene reloads
                Add(() => Log.WriteLine($"menu: active slot={EndlessSurvival.Save.CharacterProfiles.Active + 1} journeys={(EndlessSurvival.Save.CharacterProfiles.Current != null ? EndlessSurvival.Save.CharacterProfiles.Current.journeys : -1)}"), 0.1);
            }
            Add(() => { State("after new game"); Screen("01_start"); }, 0.5);
            Add(() => { ClearEnemies(); Heal(); }, 0.3);

            if (Part("combat"))
            {
                // --- walking
                Add(() => { _walkFrom = SurvivalDirector.Player.transform.position; Stick(new Vector2(0f, 1f)); }, 1.5);
                Add(() => { Log.WriteLine($"walked {Vector3.Distance(_walkFrom, SurvivalDirector.Player.transform.position):0.00} m in 1.5 s"); Shot("02_rifle_walk"); }, 0.1);
                Add(() => Down(GamepadButton.LeftStick), 0.1);
                Add(() => { Up(GamepadButton.LeftStick); _walkFrom = SurvivalDirector.Player.transform.position; }, 1.5);
                Add(() => { Log.WriteLine($"sprinted {Vector3.Distance(_walkFrom, SurvivalDirector.Player.transform.position):0.00} m in 1.5 s"); Stick(Vector2.zero); }, 1.0);
                Add(() => Shot("03_rifle_idle"), 0.1);

                // --- rifle: aim, fire
                Add(() => LT(1f), 1.0);
                Add(() => { Shot("04_rifle_aim"); Screen("04b_rifle_aim_view"); }, 0.3);
                Add(() => SpawnEnemyAtCrosshair("Enemy_Melee_Bat", EnemyController.EnemyState.Idle, true), 0.6);
                Add(() => { EnemyState("before rifle fire"); CrosshairCheck("rifle"); State("before rifle fire"); WeaponController.DebugShot = m => Log.WriteLine("      shot " + m); RT(1f); }, 0.8);
                Add(() => { RT(0f); WeaponController.DebugShot = null; EnemyState("after rifle fire"); State("after rifle fire"); }, 0.3);
                Add(() => LT(0f), 0.5);
                Tap(GamepadButton.West, 2.8); // reload (or interact when something is in reach)
                Add(() => State("after reload"), 0.1);
                CloseUi();
                Add(() => { ClearEnemies(); Heal(); }, 0.3);

                // --- pistol
                Add(() => { Equip("Item_Pistol"); Give("Item_PistolAmmo", 3); Give("Item_RifleAmmo", 2); }, 0.3);
                Add(() => Draw(PlayerLoadout.Slot.Pistol), 2.4); // drawn empty: reloads by itself
                Add(() => Shot("05_pistol_idle"), 0.1);
                Add(() => Stick(new Vector2(0f, 1f)), 1.0);
                Add(() => { Shot("05b_pistol_walk"); Stick(Vector2.zero); }, 0.6);
                Add(() => LT(1f), 1.0);
                Add(() => Shot("06_pistol_aim"), 0.1);
                Add(() => SpawnEnemyAtCrosshair("Enemy_Melee_Bat", EnemyController.EnemyState.Idle, true), 0.6);
                Add(() => { EnemyState("before pistol fire"); CrosshairCheck("pistol"); RT(1f); }, 0.15);
                Add(() => RT(0f), 0.3);
                Add(() => RT(1f), 0.15);
                Add(() => { RT(0f); Shot("06b_pistol_fired"); EnemyState("after 2 pistol shots"); State("after pistol fire"); }, 0.3);
                Add(() => LT(0f), 0.4);
                Add(() => { ClearEnemies(); Heal(); }, 0.3);

                // --- one handed melee (club)
                Add(() => Equip("Item_WoodenClub"), 0.3);
                CloseUi();
                Add(() => Draw(PlayerLoadout.Slot.Melee), 1.0);
                Add(() => Shot("07_club_idle"), 0.1);
                Add(() => Stick(new Vector2(0f, 1f)), 1.0);
                Add(() => { Shot("07b_club_walk"); Stick(Vector2.zero); }, 0.6);
                Add(() => SpawnDummyInFront(1.3f), 0.5);
                Add(() => { EnemyState("before club"); Down(GamepadButton.RightShoulder); }, 0.1);
                Add(() => Up(GamepadButton.RightShoulder), 0.18);
                Add(() => Shot("08_club_light1"), 0.35);
                Add(() => { State("club light mid"); Down(GamepadButton.RightShoulder); }, 0.1);
                Add(() => Up(GamepadButton.RightShoulder), 0.6);
                Add(() => Shot("08b_club_light2"), 0.9);
                Add(() => { EnemyState("after club lights"); RT(1f); }, 0.12);
                Add(() => RT(0f), 0.45);
                Add(() => Shot("09_club_heavy"), 1.2);
                Add(() => EnemyState("after club heavy"), 0.1);
                Add(() => LT(1f), 0.7);
                Add(() => { Shot("10_club_guard"); LT(0f); }, 0.5);
                // dodge
                Add(() => { _walkFrom = SurvivalDirector.Player.transform.position; Stick(new Vector2(1f, 0f)); }, 0.1);
                Tap(GamepadButton.East, 0.25);
                Add(() => { Shot("11_dodge_right"); Stick(Vector2.zero); }, 0.7);
                Add(() => Log.WriteLine($"dodge moved {Vector3.Distance(_walkFrom, SurvivalDirector.Player.transform.position):0.00} m"), 0.1);
                Add(() => { ClearEnemies(); Heal(); }, 0.3);

                // --- two handed (bat)
                Add(() => Equip("Item_BaseballBat"), 0.8);
                Add(() => Shot("12_bat_idle"), 0.1);
                Add(() => Stick(new Vector2(0f, 1f)), 1.0);
                Add(() => { Shot("12b_bat_walk"); Stick(Vector2.zero); }, 0.6);
                Add(() => SpawnDummyInFront(1.4f), 0.5);
                Add(() => Down(GamepadButton.RightShoulder), 0.1);
                Add(() => Up(GamepadButton.RightShoulder), 0.25);
                Add(() => Shot("13_bat_light"), 1.2);
                Add(() => EnemyState("after bat light"), 0.1);
                Add(() => { ClearEnemies(); Heal(); }, 0.3);

                // --- knife
                Add(() => Equip("Item_Knife"), 0.8);
                Add(() => Shot("14_knife_idle"), 0.1);
                Add(() => SpawnDummyInFront(1.1f), 0.5);
                Add(() => Down(GamepadButton.RightShoulder), 0.1);
                Add(() => Up(GamepadButton.RightShoulder), 0.2);
                Add(() => Shot("15_knife_light"), 1.0);
                Add(() => EnemyState("after knife light"), 0.1);
                Add(() => { ClearEnemies(); Heal(); }, 0.3);

                // --- shield + club, fists
                Add(() => { Equip("Item_WoodenShield"); Equip("Item_WoodenClub"); }, 0.8);
                Add(() => Shot("16_shield_club"), 0.1);
                Add(() => LT(1f), 0.7);
                Add(() => { Shot("17_shield_guard"); LT(0f); }, 0.5);
                Add(() => { var eq = P<PlayerEquipment>(); Log.WriteLine("unequip melee: " + eq.Unequip(EquipSlot.Melee)); }, 0.6);
                Add(() => Shot("18_fists_shield"), 0.1);
                Add(() => Down(GamepadButton.RightShoulder), 0.1);
                Add(() => Up(GamepadButton.RightShoulder), 0.2);
                Add(() => Shot("19_punch"), 0.9);

                // --- back to the rifle, then stow it (LB tap) and draw it again (LB tap)
                Add(() => Draw(PlayerLoadout.Slot.LongGun), 0.9);
                Add(() => Shot("20_rifle_again"), 0.1);
                LbTap(1.2);
                Add(() => Shot("21_stowed"), 0.1);
                Add(() => Stick(new Vector2(0f, 1f)), 1.0);
                Add(() => { Shot("21b_stowed_walk"); Stick(Vector2.zero); }, 0.5);
                LbTap(1.2);
                Add(() => State("drawn again"), 0.1);
            }

            if (Part("rules")) AddRulesPart();

            if (Part("models")) AddModelsPart();

            if (Part("enemy"))
            {
                // --- an enemy with a rifle: it must reload, run dry and come in with a knife or fists
                Add(() => { ClearEnemies(); Heal(); SpawnEnemyAtCrosshair("Enemy", EnemyController.EnemyState.Idle); }, 1.0);
                Add(() => { if (_enemy != null) _enemy.TakeDamage(1f, _enemy.transform.position, Vector3.up); }, 0.2);
                for (int i = 0; i < 14; i++)
                {
                    int n = i;
                    Add(() => { Heal(); EnemyState($"rifle enemy t={n * 3}s"); if (n == 2) Screen("30_enemy_shooting"); }, 3.0);
                }
                Add(() => { Screen("31_enemy_melee"); State("player after enemy"); ClearEnemies(); Heal(); }, 0.5);

                Add(() => { SpawnEnemyAtCrosshair("Enemy_Pistol", EnemyController.EnemyState.Idle); }, 1.0);
                Add(() => { if (_enemy != null) _enemy.TakeDamage(1f, _enemy.transform.position, Vector3.up); }, 0.2);
                for (int i = 0; i < 9; i++)
                {
                    int n = i;
                    Add(() => { Heal(); EnemyState($"pistol enemy t={n * 3}s"); }, 3.0);
                }
                Add(() => { ClearEnemies(); Heal(); }, 0.5);
            }

            if (Part("ui"))
            {
                // --- Tab screen with the gamepad
                Tap(GamepadButton.Select, 1.0);
                Add(() => { State("hub open"); Screen("40_hub_character"); }, 0.5);
                // (A on an empty slot with no fitting item opens no list, then B closes the whole screen:
                //  only move the focus here)
                Tap(GamepadButton.DpadDown, 0.3);
                Add(() => Screen("40b_hub_character_focus"), 0.4);
                Tap(GamepadButton.RightShoulder, 0.8);
                Add(() => State("hub after RB (bag)"), 0.1);
                Add(() => Screen("41_hub_bag"), 0.4);
                Tap(GamepadButton.DpadDown, 0.3);
                Tap(GamepadButton.DpadDown, 0.3);
                Add(() => Screen("41b_hub_bag_focus"), 0.4);
                Tap(GamepadButton.South, 0.5);
                Add(() => Screen("41c_hub_bag_actions"), 0.4);
                Tap(GamepadButton.East, 0.5);
                Add(() => State("bag after B (must still be open)"), 0.1);
                Tap(GamepadButton.RightShoulder, 0.8);
                Add(() => Screen("42_hub_craft"), 0.4);
                Tap(GamepadButton.DpadDown, 0.3);
                Tap(GamepadButton.DpadDown, 0.3);
                Tap(GamepadButton.DpadDown, 0.3);
                Add(() => { _bandages = P<PlayerInventory>().Storage.GetCount(It("Item_Bandage")); Screen("42b_hub_craft_focus"); }, 0.4);
                Tap(GamepadButton.South, 0.6);
                Add(() => Screen("42c_hub_craft_pressed"), 0.4);
                Tap(GamepadButton.RightShoulder, 0.8);
                Add(() => Screen("43_hub_camp"), 0.4);
                Tap(GamepadButton.RightShoulder, 0.8);
                Add(() => Screen("44_hub_vehicle"), 0.4);
                Tap(GamepadButton.DpadDown, 0.3);
                Tap(GamepadButton.DpadDown, 0.3);
                Add(() => Screen("44b_hub_vehicle_focus"), 0.4);
                Tap(GamepadButton.RightShoulder, 0.8);
                Add(() => Screen("45_hub_status"), 0.4);
                Tap(GamepadButton.RightShoulder, 0.8);
                Add(() => Screen("46_hub_stats"), 0.4);
                Tap(GamepadButton.East, 0.6);
                Add(() => State("hub closed"), 0.1);

                // --- pause menu
                Tap(GamepadButton.Start, 0.6);
                Add(() => { State("pause"); Screen("47_pause"); }, 0.4);
                Tap(GamepadButton.DpadDown, 0.3); // Devam > Kontroller (there is no manual save button any more)
                Tap(GamepadButton.South, 0.5);
                Add(() => Screen("47b_controls"), 0.4);
                Tap(GamepadButton.East, 0.4);
                Tap(GamepadButton.East, 0.6);
                Add(() => State("pause closed"), 0.1);

                // --- action menu
                Add(() => Down(GamepadButton.DpadDown), 0.6);
                Add(() => Up(GamepadButton.DpadDown), 0.4);
                Add(() => { State("action menu"); Screen("48_action_menu"); }, 0.4);
                Tap(GamepadButton.DpadDown, 0.3);
                Tap(GamepadButton.DpadDown, 0.3);
                Add(() => Screen("48b_action_focus"), 0.3);
                Tap(GamepadButton.South, 2.0); // Sigara iç
                Add(() => { State("after action (smoke)"); Log.WriteLine("smoking=" + SmokingAction.Active); Shot("49_smoking"); }, 0.3);
                Add(() => { _walkFrom = SurvivalDirector.Player.transform.position; Stick(new Vector2(0f, 1f)); }, 1.2);
                Add(() =>
                {
                    Log.WriteLine($"smoking while walking: active={SmokingAction.Active} walked={Vector3.Distance(_walkFrom, SurvivalDirector.Player.transform.position):0.00} m");
                    var si = P<EndlessCombat.Character.PlayerInputState>(); var tp = P<EndlessCombat.Character.PlayerMotor>();
                    Log.WriteLine($"   move={si.move} blocked={(EndlessCombat.Character.PlayerInputState.Blocked != null && EndlessCombat.Character.PlayerInputState.Blocked())} moveSpeed={tp.MoveSpeed:0.00} locked={tp.ActionLocked} mult={tp.MoveSpeedMultiplier:0.00}/{tp.ExtraSpeedMultiplier:0.00} locks={PlayerSurvivalAnimator.LocksMovement} cur={PlayerSurvivalAnimator.CurrentAction} shown={PlayerSurvivalAnimator.ShownAction} tpcEnabled={tp.enabled} gameplayBlocked={GameInput.GameplayBlocked}");
                    Shot("49b_smoking_walk");
                    Stick(Vector2.zero);
                }, 0.5);
                Add(() => Down(GamepadButton.LeftStick), 0.1);
                Add(() => { Up(GamepadButton.LeftStick); Stick(new Vector2(0f, 1f)); }, 0.8);
                Add(() => { Log.WriteLine($"smoking after sprint: active={SmokingAction.Active}"); Stick(Vector2.zero); }, 0.8);
            }

            if (_only == "new")
            {
                // --- 12 Nov: shield + rifle grip, one handed combo, noise investigation, journey
                Add(() => { ClearEnemies(); Heal(); var j = Journey.Instance; Log.WriteLine($"journey: {(j != null ? $"day {Journey.Day} zone {j.Zone} km {j.MaxKm:0.00} dest {j.destinationKm}" : "missing")}"); Screen("60_journey_hud"); }, 0.5);
                Add(() => { Equip("Item_WoodenShield"); Equip("Item_WoodenClub"); }, 0.6);
                Add(() => { var l = P<PlayerLoadout>(); Log.WriteLine($"shield on: rifle twoHand={l.TwoHanded(PlayerLoadout.Slot.LongGun)} shieldOnArm={l.ShieldOnArm}"); Shot("61_rifle_shield_idle"); }, 0.2);
                Add(() => LT(1f), 1.0);
                Add(() => Shot("62_rifle_shield_aim"), 0.2);
                Add(() => RT(1f), 0.3);
                Add(() => { RT(0f); Shot("62b_rifle_shield_fire"); State("rifle+shield fired"); }, 0.3);
                Add(() => LT(0f), 0.5);
                Add(() => Draw(PlayerLoadout.Slot.Melee), 1.0);
                Add(() => Shot("63_club_shield_idle"), 0.1);
                // three light attacks in a row: each one must stay in the right hand
                for (int i = 1; i <= 3; i++)
                {
                    int n = i;
                    Add(() => Down(GamepadButton.RightShoulder), 0.08);
                    Add(() => Up(GamepadButton.RightShoulder), 0.22);
                    Add(() => { Shot("64_club_combo_" + n + "a"); }, 0.2);
                    Add(() => { Shot("64_club_combo_" + n + "b"); State("combo " + n); }, 0.45);
                }
                Add(() => RT(1f), 0.1);
                Add(() => RT(0f), 0.4);
                Add(() => Shot("65_club_heavy"), 1.2);

                // noise: five idle enemies, a rock lands 9 m to the side: how many walk over?
                Add(() =>
                {
                    ClearEnemies(); Heal();
                    var pl = SurvivalDirector.Player.transform;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Enemy_Melee_Knife.prefab");
                    _crowd.Clear();
                    for (int i = 0; i < 5; i++)
                    {
                        Vector3 pos = pl.position + pl.forward * (16f + i * 1.5f) + pl.right * (i * 2f - 4f);
                        if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
                        var e = Object.Instantiate(prefab, pos, Quaternion.LookRotation(pl.forward)).GetComponent<EnemyController>(); // backs to the player
                        e.detectionRange = 6f;
                        _crowd.Add(e);
                    }
                    _noisePoint = pl.position + pl.forward * 18f + pl.right * 9f;
                }, 1.5);
                Add(() =>
                {
                    _crowdStart.Clear();
                    foreach (var e in _crowd) _crowdStart.Add(e.transform.position);
                    PlayerStealthController.Instance.AlertNearbyEnemies(_noisePoint, 14f);
                }, 3.0);
                Add(() =>
                {
                    int walking = 0;
                    for (int i = 0; i < _crowd.Count; i++)
                    {
                        float moved = Vector3.Distance(_crowd[i].transform.position, _crowdStart[i]);
                        if (moved > 0.6f) walking++;
                        Log.WriteLine($"   noise enemy {i}: state={_crowd[i].State} moved={moved:0.0} m distToNoise={Vector3.Distance(_crowd[i].transform.position, _noisePoint):0.0}");
                    }
                    Log.WriteLine($"noise: {walking} of {_crowd.Count} walked toward it (expected 2)");
                    Screen("66_noise");
                    ClearEnemies();
                }, 0.5);

                // obstacle: an enemy behind a wall must come around it
                Add(() =>
                {
                    Heal();
                    var pl = SurvivalDirector.Player.transform;
                    Vector3 wallPos = pl.position + pl.forward * 6f;
                    if (Physics.Raycast(wallPos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore)) wallPos = hit.point;
                    _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _wall.name = "PtWall";
                    _wall.transform.position = wallPos + Vector3.up * 1.2f;
                    _wall.transform.rotation = Quaternion.LookRotation(pl.forward);
                    _wall.transform.localScale = new Vector3(7f, 2.4f, 0.4f);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Enemy_Melee_Knife.prefab");
                    Vector3 pos = pl.position + pl.forward * 11f;
                    if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out RaycastHit h2, 20f, ~0, QueryTriggerInteraction.Ignore)) pos = h2.point;
                    _enemy = Object.Instantiate(prefab, pos, Quaternion.LookRotation(-pl.forward)).GetComponent<EnemyController>();
                    _enemy.maxHealth = 2000f;
                }, 0.5);
                Add(() => { if (_enemy != null) _enemy.TakeDamage(1f, _enemy.transform.position, Vector3.up); }, 0.1);
                for (int i = 0; i < 6; i++)
                {
                    int n = i;
                    Add(() => { Heal(); if (_enemy != null) Log.WriteLine($"   wall t={n * 1.5f:0.0}s enemy dist={Vector3.Distance(_enemy.transform.position, SurvivalDirector.Player.transform.position):0.0} state={_enemy.State}"); if (n == 2) Screen("67_wall"); }, 1.5);
                }
                Add(() => { if (_wall != null) Object.Destroy(_wall); ClearEnemies(); Heal(); }, 0.3);
            }

            if (_only == "hit")
            {
                // --- hit reactions: does the body leave the ground? Samples the root and hips height every 0.06 s
                foreach (var kind in new[] { "light", "heavy", "guardhit", "bullet" })
                {
                    string k = kind;
                    Add(() =>
                    {
                        Heal();
                        var actor = P<CombatBody>();
                        var pl = SurvivalDirector.Player.transform;
                        Log.WriteLine($"--- hit {k}");
                        if (k == "light") actor.Stagger(0.4f, -pl.forward * 2f);
                        else if (k == "heavy") actor.Stagger(0.8f, -pl.forward * 3f);
                        else if (k == "guardhit")
                        {
                            var st = CombatContent.Instance.fists;
                            actor.ReceiveMelee(null, pl.position + pl.forward, st, 0.4f, pl.position + Vector3.up, -pl.forward);
                        }
                        else P<PlayerHealth>().TakeDamage(10f, pl.position + Vector3.up, pl.forward);
                    }, 0.0);
                    for (int i = 0; i < 22; i++)
                    {
                        int n = i;
                        Add(() =>
                        {
                            var pl = SurvivalDirector.Player.transform;
                            var an = pl.GetComponent<Animator>();
                            var tpc = pl.GetComponent<EndlessCombat.Character.PlayerMotor>();
                            var hips = an.GetBoneTransform(HumanBodyBones.Hips);
                            var lf = an.GetBoneTransform(HumanBodyBones.LeftFoot);
                            var rf = an.GetBoneTransform(HumanBodyBones.RightFoot);
                            var actor = pl.GetComponent<CombatBody>();
                            int full = an.GetLayerIndex(MeleeAnimationSet.LayerFullBody);
                            Log.WriteLine($"   t={n * 0.06f:0.00} rootY={pl.position.y:0.000} hips={hips.position.y - pl.position.y:0.00} feet={Mathf.Min(lf.position.y, rf.position.y) - pl.position.y:0.00} grounded={tpc.Grounded} state={actor.State} fullW={(full >= 0 ? an.GetLayerWeight(full) : -1f):0.00} jump={an.GetBool("Jump")} fall={an.GetBool("FreeFall")}");
                            if (n == 4) Shot("hit_" + k);
                        }, 0.06);
                    }
                    Add(() => { }, 1.2);
                }
            }

            if (_only == "fight")
            {
                // --- a real melee fight: does anyone leave the ground? Logs every sample where a root is off its start height
                foreach (var prefab in new[] { "Enemy_Melee_Bat", "Enemy_Melee_Knife", "Enemy_Melee_Shield" })
                {
                    string pf = prefab;
                    Add(() =>
                    {
                        ClearEnemies(); Heal();
                        SpawnEnemyAtCrosshair(pf, EnemyController.EnemyState.Idle);
                        if (_enemy != null) { _enemy.maxHealth = 2000f; }
                        _fightPlayerY = SurvivalDirector.Player.transform.position.y;
                        _fightEnemyY = float.NaN;
                        _fightMaxP = 0f; _fightMaxE = 0f;
                        Log.WriteLine($"--- fight {pf}");
                    }, 0.5);
                    Add(() => { if (_enemy != null) _enemy.TakeDamage(1f, _enemy.transform.position, Vector3.up); }, 0.1);
                    for (int i = 0; i < 260; i++)
                    {
                        int n = i;
                        Add(() =>
                        {
                            if (n % 40 == 0) Heal();
                            // the player swings back now and then, and walks sideways for a part of the fight
                            if (n % 30 == 10) Down(GamepadButton.RightShoulder);
                            if (n % 30 == 12) Up(GamepadButton.RightShoulder);
                            _fightPrev = _fightNow; _fightNow = SurvivalDirector.Player.transform.position;
                            var pl = SurvivalDirector.Player.transform;
                            if (_enemy == null) return;
                            var en = _enemy.transform;
                            float dist = Vector3.Distance(pl.position, en.position);
                            if (float.IsNaN(_fightEnemyY) && dist < 3f) { _fightEnemyY = en.position.y; _fightPlayerY = pl.position.y; }
                            if (float.IsNaN(_fightEnemyY)) return;
                            float dp = AboveGround(pl), de = AboveGround(en); // height over the ground under the feet (slopes do not count)
                            _fightMaxP = Mathf.Max(_fightMaxP, dp); _fightMaxE = Mathf.Max(_fightMaxE, de);
                            var pa = pl.GetComponent<CombatBody>(); var ea = en.GetComponent<CombatBody>();
                            var ean = en.GetComponentInChildren<Animator>();
                            var eh = ean != null ? ean.GetBoneTransform(HumanBodyBones.Hips) : null;
                            var pan = pl.GetComponent<Animator>();
                            var ph = pan.GetBoneTransform(HumanBodyBones.Hips);
                            float eHips = eh != null ? eh.position.y - en.position.y : 0f, pHips = ph.position.y - pl.position.y;
                            if (dp > 0.12f || de > 0.12f || eHips > 1.12f || pHips > 1.12f)
                            {
                                string under = "nothing";
                                foreach (var h in Physics.RaycastAll(pl.position + Vector3.up * 0.3f, Vector3.down, 0.8f, ~0, QueryTriggerInteraction.Collide))
                                    under = (under == "nothing" ? "" : under + ", ") + h.collider.name + "<" + h.collider.transform.root.name + (h.collider.isTrigger ? " trigger" : "") + " d=" + (h.distance - 0.3f).ToString("0.00");
                                Log.WriteLine($"      pos={pl.position:F2} prev={_fightPrev:F2} under: {under}");
                                Log.WriteLine($"   n={n} OFF: playerDY={dp:0.00} hips={pHips:0.00} {pa.State} grounded={pl.GetComponent<EndlessCombat.Character.PlayerMotor>().Grounded} | enemyDY={de:0.00} hips={eHips:0.00} {(ea != null ? ea.State.ToString() : "-")} dist={dist:0.00}");
                                if (!_fightShot) { _fightShot = true; Shot("fight_off_" + pf); }
                            }
                        }, 0.05);
                    }
                    Add(() => { Up(GamepadButton.RightShoulder); Stick(Vector2.zero); Log.WriteLine($"   max rise: player {_fightMaxP:0.00} m, enemy {_fightMaxE:0.00} m"); _fightShot = false; }, 0.3);
                }
            }

            if (Part("save"))
            {
                // --- save / load: the worn weapons, shield, grip and magazines come back. The player's own save file
                //     is kept in memory and written back at the end.
                Add(() =>
                {
                    var sm = EndlessSurvival.Save.SaveManager.Instance;
                    if (sm == null) { Log.WriteLine("save: no SaveManager"); return; }
                    var pathProp = typeof(EndlessSurvival.Save.SaveManager).GetProperty("SaveFilePath", BindingFlags.NonPublic | BindingFlags.Instance);
                    _savePath = pathProp != null ? (string)pathProp.GetValue(sm) : null;
                    _saveBackup = _savePath != null && File.Exists(_savePath) ? File.ReadAllBytes(_savePath) : null;
                    Equip("Item_Pistol"); Equip("Item_WoodenClub"); Equip("Item_WoodenShield");
                    Log.WriteLine("save before: " + EquipSummary());
                    Log.WriteLine("save ok=" + sm.SaveGame());
                }, 0.5);
                Add(() =>
                {
                    var eq = P<PlayerEquipment>();
                    eq.Unequip(EquipSlot.Pistol); eq.Unequip(EquipSlot.Melee); eq.Unequip(EquipSlot.Shield);
                    Log.WriteLine("save changed: " + EquipSummary());
                }, 0.5);
                Add(() =>
                {
                    var sm = EndlessSurvival.Save.SaveManager.Instance;
                    Log.WriteLine("load ok=" + (sm != null && sm.LoadGame()));
                }, 2.0);
                Add(() =>
                {
                    Log.WriteLine("save after load: " + EquipSummary());
                    State("after load");
                    if (_savePath != null)
                    {
                        if (_saveBackup != null) File.WriteAllBytes(_savePath, _saveBackup);
                        else if (File.Exists(_savePath)) File.Delete(_savePath);
                        Log.WriteLine("save: player's own save file put back (" + (_saveBackup != null ? "restored" : "none before, removed") + ")");
                    }
                }, 0.3);
            }

            if (Part("vehicle"))
            {
                // --- vehicle: get in with X, drive, get out with Y
                Add(() =>
                {
                    var car = SurvivalDirector.Vehicle;
                    var player = SurvivalDirector.Player;
                    if (car == null) { Log.WriteLine("no vehicle"); return; }
                    var cc = player.GetComponent<CharacterController>();
                    cc.enabled = false;
                    Vector3 p = car.transform.position - car.transform.right * 2.6f;
                    if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point + Vector3.up * 0.1f;
                    player.transform.position = p;
                    cc.enabled = true;
                    Log.WriteLine($"vehicle at {car.transform.position:F1} up={car.transform.up:F2}");
                }, 1.2);
                Add(() => { State("next to vehicle"); Log.WriteLine("vehicle near mark=" + GameInput.InteractAvailable); Screen("50_vehicle_prompt"); }, 0.4);
                Tap(GamepadButton.West, 1.5);
                Add(() => { Log.WriteLine($"in vehicle: driven={SurvivalDirector.PlayerInVehicle} engine={SurvivalDirector.Vehicle.engineOn}"); Screen("51_in_vehicle"); }, 0.4);
                Tap(GamepadButton.West, 1.5); // start the engine if it did not start
                Add(() => { _fuelFrom = SurvivalDirector.Vehicle.currentFuel; _driveFrom = SurvivalDirector.Vehicle.transform.position; RT(1f); }, 3.0);
                Add(() =>
                {
                    var car = SurvivalDirector.Vehicle;
                    float km = Vector3.Distance(_driveFrom, car.transform.position) / 1000f, used = _fuelFrom - car.currentFuel;
                    Log.WriteLine($"driving: speed={car.CurrentSpeedKmh:0.0} km/h engine={car.engineOn}");
                    Log.WriteLine($"fuel: start of drive {_fuelFrom:0.0}/{car.maxFuel:0}, used {used:0.00} over {km * 1000f:0} m = {(km > 0.001f ? used / km : 0f):0.0} per km (setting {car.fuelPerKm:0}); a full tank is about {car.maxFuel / Mathf.Max(1f, car.fuelPerKm):0.0} km");
                    Screen("52_driving"); RT(0f); LT(1f);
                }, 2.5);
                Add(() => { LT(0f); Down(GamepadButton.East); }, 1.5);
                Add(() => { Up(GamepadButton.East); Log.WriteLine($"stopped: speed={SurvivalDirector.Vehicle.CurrentSpeedKmh:0.0} km/h"); }, 0.3);
                Tap(GamepadButton.North, 1.5);
                Add(() => { State("after exit"); Log.WriteLine($"driven={SurvivalDirector.PlayerInVehicle}"); Shot("53_after_exit"); Screen("53b_after_exit_view"); }, 0.4);
                Add(() => Stick(new Vector2(-1f, 0f)), 1.2);
                Add(() => { Stick(Vector2.zero); State("walked away from vehicle"); }, 0.3);
            }

            if (Part("barricade"))
            {
                // --- roadblock: which chunks get one, the fence and the gate block the road, clearing the gate with X
                Add(() =>
                {
                    var manager = EndlessSurvival.World.ChunkManager.Instance;
                    var list = new List<int>();
                    for (int i = manager.StartChunkIndex; i < manager.StartChunkIndex + 120; i++)
                        if (RoadBarricade.ChunkHasBarricade(manager.ResolvedSeed, i, manager.StartChunkIndex)) list.Add(i - manager.StartChunkIndex);
                    Log.WriteLine("barricade: chunks after the start with a roadblock (first 120): " + string.Join(", ", list));

                    ClearEnemies();
                    var chunk = SurvivalDirector.ChunkAt(SurvivalDirector.Player.transform.position);
                    var spline = chunk.GetComponentInChildren<EndlessSurvival.World.Road.RoadSpline>();
                    _barricade = RoadBarricade.Create(chunk, spline, chunk.transform, new EndlessSurvival.World.SeededRandom(12345));
                    if (_barricade == null) { Log.WriteLine("barricade: could not be built in this chunk"); return; }
                    Physics.SyncTransforms(); // the panels were moved after their colliders were made; the probes below run in the same frame
                    Vector3 gate = _barricade.InteractPosition;
                    Vector3 fwd = chunk.transform.forward;
                    int panels = _barricade.transform.parent.Find("Fence").childCount;
                    bool roadBlocked = BarricadeBlocks(chunk, 4f);
                    bool besidePost = BarricadeBlocks(chunk, RoadBarricade.GateWidth * 0.5f + 2.5f) && BarricadeBlocks(chunk, -(RoadBarricade.GateWidth * 0.5f + 2.5f));
                    bool sideBlocked = BarricadeBlocks(chunk, 60f) && BarricadeBlocks(chunk, -60f) && BarricadeBlocks(chunk, 150f) && BarricadeBlocks(chunk, -150f);
                    Log.WriteLine($"barricade: built at {gate:F1}, fence panels={panels}, road blocked={roadBlocked}, beside both gate posts blocked={besidePost}, 60 and 150 m to both sides blocked={sideBlocked}");

                    // Stand on the road in front of the gate
                    var player = SurvivalDirector.Player;
                    var cc = player.GetComponent<CharacterController>();
                    cc.enabled = false;
                    Vector3 p = gate - fwd * 2.2f;
                    if (Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point + Vector3.up * 0.1f;
                    player.transform.SetPositionAndRotation(p, Quaternion.LookRotation(fwd));
                    cc.enabled = true;
                    WideShot("80_barricade", gate - fwd * 26f + Vector3.up * 9f + chunk.transform.right * 6f, gate);
                }, 1.5);
                Add(() => { Log.WriteLine("barricade: prompt available=" + GameInput.InteractAvailable); Screen("80b_barricade_prompt"); }, 0.3);
                Tap(GamepadButton.West, 1.0);
                Add(() =>
                {
                    var job = VehicleParts.Instance != null ? VehicleParts.Instance.CurrentJob : null;
                    Log.WriteLine($"barricade: work started={(job != null)} seconds={(job != null ? job.seconds : 0f):0.0}");
                    Screen("81_barricade_work");
                }, 30.0);
                Add(() =>
                {
                    if (_barricade == null) return;
                    Vector3 gate = _barricade.InteractPosition;
                    Vector3 fwd = _barricade.transform.forward;
                    bool roadBlocked = BarricadeBlocks(SurvivalDirector.ChunkAt(gate), 4f) || BarricadeBlocks(SurvivalDirector.ChunkAt(gate), -4f);
                    Log.WriteLine($"barricade: open={_barricade.IsOpen} road blocked={roadBlocked} remembered={RoadBarricade.Opened.Contains(_barricade.ChunkIndex)}");
                    WideShot("82_barricade_open", gate - fwd * 26f + Vector3.up * 9f + _barricade.transform.right * 6f, gate);
                }, 0.4);
            }

            if (Part("stop"))
            {
                // --- Safe Zone stop: the difficulty curve past its old end, the place itself, fuel and repair for
                //     money, and the arrival screen. The stop distance is shortened for the test and put back.
                Add(() =>
                {
                    CloseUiNow();
                    ClearEnemies();
                    var s = SurvivalSettings.Instance;
                    string curve = "";
                    foreach (float km in new[] { 0f, 6f, 15f, 30f, 60f, 120f })
                    {
                        Vector3 at = new Vector3(0f, 0f, km * 1000f);
                        curve += $" {km:0}km: raw {DifficultyCurve.Raw(at):0.00} T {DifficultyCurve.T(at):0.00} count x{DifficultyCurve.ScaleEnemyCount(10, at) / 10f:0.0} fuel x{DifficultyCurve.FuelMultiplier(at):0.00};";
                    }
                    Log.WriteLine($"stop: difficulty (character bonus {DifficultyCurve.CharacterBonus:0.00}):{curve}");

                    var player = SurvivalDirector.Player;
                    var chunk = SurvivalDirector.ChunkAt(player.transform.position);
                    var spline = chunk.GetComponentInChildren<EndlessSurvival.World.Road.RoadSpline>();
                    Vector3 fwd = chunk.transform.forward;
                    float localZ = Mathf.Clamp(chunk.transform.InverseTransformPoint(player.transform.position).z + 70f, chunk.chunkLength * 0.56f, chunk.chunkLength - 60f); // past the middle: only one stop of the shortened distance fits in the chunk
                    _everyKm = s.safeZoneEveryKm;
                    s.safeZoneEveryKm = DifficultyCurve.Km(chunk.transform.position + fwd * localZ);
                    bool found = SafeZone.TryGetStop(chunk, out int number, out float stopZ);
                    Log.WriteLine($"stop: every {s.safeZoneEveryKm:0.000} km for the test (was {_everyKm}); chunk has a stop={found} number={number} at z={stopZ:0} (wanted {localZ:0})");
                    _zone = SafeZone.Create(chunk, spline, chunk.transform, 1, stopZ, new EndlessSurvival.World.SeededRandom(777));
                    Physics.SyncTransforms();

                    // The car parked at the services, half empty and dented; some money in the pocket
                    var car = SurvivalDirector.Vehicle;
                    var rb = car.GetComponent<Rigidbody>();
                    Vector3 park = _zone.Center - fwd * 15f + Vector3.up * 1.2f;
                    rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                    car.transform.SetPositionAndRotation(park, Quaternion.LookRotation(fwd));
                    rb.position = park;
                    car.currentFuel = 20f;
                    car.currentHealth = 50f;
                    var inv = P<PlayerInventory>();
                    inv.SetMoney(120);
                    bool free = SurvivalTest.FreeResources;
                    SurvivalTest.ForceForSession(false); // prices must apply here
                    foreach (var service in _zone.GetComponentsInChildren<SafeZoneService>())
                    {
                        string prompt = service.GetPrompt();
                        string result = service.Interact(inv);
                        Log.WriteLine($"stop: {service.kind}: \"{prompt}\" > \"{result}\" money={inv.Money} fuel={car.currentFuel:0} body={car.currentHealth:0}");
                    }
                    // R6: the vehicle left 200 m back with an empty tank is brought in by the tow
                    foreach (var service in _zone.GetComponentsInChildren<SafeZoneService>())
                    {
                        if (service.kind != SafeZoneService.Kind.Tow) continue;
                        bool offeredWhileHere = service.CanInteract;
                        Vector3 far = _zone.Center - fwd * 200f + Vector3.up * 1.5f;
                        car.transform.position = far; rb.position = far;
                        car.currentFuel = 0f; car.currentHealth = 0f;
                        inv.SetMoney(100);
                        string prompt = service.GetPrompt();
                        bool offered = service.CanInteract;
                        string result = service.Interact(inv);
                        Log.WriteLine($"stop: Tow: offered while the car is here={offeredWhileHere}, 200 m back={offered}: \"{prompt}\" > \"{result}\" money={inv.Money} " +
                                      $"car now {Vector3.Distance(car.transform.position, _zone.Center):0} m from the centre, fuel={car.currentFuel:0} body={car.currentHealth:0} broken={car.IsBroken}");
                    }
                    SurvivalTest.ForceForSession(free);
                    Log.WriteLine($"stop: safe near centre={SafeZone.Near(_zone.Center)} 200 m back={SafeZone.Near(_zone.Center - fwd * 200f)} trader={(_zone.GetComponentInChildren<Trader>() != null)}");

                    // Walk in from just before the stop
                    var cc = player.GetComponent<CharacterController>();
                    cc.enabled = false;
                    Vector3 p = _zone.Center - fwd * 30f + chunk.transform.right * -3f;
                    if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point + Vector3.up * 0.1f;
                    player.transform.SetPositionAndRotation(p, Quaternion.LookRotation(fwd));
                    cc.enabled = true;
                    WideShot("85_safe_zone", _zone.Center - fwd * 34f + Vector3.up * 11f - chunk.transform.right * 10f, _zone.Center + chunk.transform.right * 4f);
                }, 1.0);
                Add(() =>
                {
                    // Past the stop's kilometre: the journey counts it
                    var player = SurvivalDirector.Player;
                    var cc = player.GetComponent<CharacterController>();
                    cc.enabled = false;
                    Vector3 p = _zone.Center + _zone.transform.forward * 6f - _zone.transform.right * 3f;
                    if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point + Vector3.up * 0.1f;
                    player.transform.position = p;
                    cc.enabled = true;
                }, 1.5);
                Add(() =>
                {
                    var j = Journey.Instance;
                    Log.WriteLine($"stop: arrival screen open={SurvivalUIState.JourneyOpen} stops={j.Stops} km={j.MaxKm:0.000} next stop at {j.NextStopKm:0.000}");
                    Screen("86_arrival");
                }, 0.6);
                Tap(GamepadButton.South, 0.8); // Devam
                Add(() =>
                {
                    var j = Journey.Instance;
                    SurvivalSettings.Instance.safeZoneEveryKm = _everyKm;
                    Log.WriteLine($"stop: after Devam screen open={SurvivalUIState.JourneyOpen} stops={j.Stops}; stop distance back to {SurvivalSettings.Instance.safeZoneEveryKm} km, next stop at {j.NextStopKm:0.0} km");
                    Screen("87_after_arrival");
                }, 0.4);

                // The same through the real world generation: a new journey whose first chunk holds a stop
                Add(() =>
                {
                    SurvivalSettings.Instance.safeZoneEveryKm = 0.3f;
                    GameMenus.PrepareNewRun();
                    UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                }, 9.0);
                Add(() =>
                {
                    SurvivalSettings.Instance.safeZoneEveryKm = _everyKm;
                    var zone = SafeZone.All.Count > 0 ? SafeZone.All[0] : null;
                    if (zone == null) { Log.WriteLine($"stop (generated): no Safe Zone was built (zones={SafeZone.All.Count})"); return; }
                    var chunk = SurvivalDirector.ChunkAt(zone.Center);
                    int enemies = 0;
                    foreach (var e in EnemyController.Active) if (e != null && SurvivalDirector.ChunkAt(e.transform.position) == chunk) enemies++;
                    bool barricade = false;
                    foreach (var b in RoadBarricade.All) if (b != null && SurvivalDirector.ChunkAt(b.transform.position) == chunk) barricade = true;
                    Log.WriteLine($"stop (generated): zones={SafeZone.All.Count} first is number {zone.Number} at {zone.Center:F0} ({DifficultyCurve.Km(zone.Center):0.00} km), services={zone.GetComponentsInChildren<SafeZoneService>().Length}, " +
                                  $"trader={(zone.GetComponentInChildren<Trader>() != null)}, enemies in its chunk={enemies}, roadblock in its chunk={barricade}, fuel at start={SurvivalDirector.Vehicle.currentFuel:0}");
                    WideShot("88_safe_zone_generated", zone.Center - zone.transform.forward * 34f + Vector3.up * 11f - zone.transform.right * 10f, zone.Center + zone.transform.right * 4f);
                }, 0.5);
            }

            if (Part("plans") && _testSlot >= 0)
            {
                // --- R4 plans (locked recipe and vehicle mod, learning from a place, buying at a Safe Zone) and
                //     R5 preparation (points buy starting gear for the next journey)
                Add(() =>
                {
                    CloseUiNow();
                    ClearEnemies();
                    bool free = SurvivalTest.FreeResources;
                    SurvivalTest.ForceForSession(false); // locks only apply outside test mode
                    var profile = EndlessSurvival.Save.CharacterProfiles.Current;
                    var craft = SurvivalDirector.Instance.GetComponent<CraftingSystem>();
                    RecipeDefinition distiller = null, bandage = null;
                    foreach (var r in SurvivalSettings.Instance.recipes)
                    {
                        if (r != null && r.name == "Recipe_Distiller") distiller = r;
                        if (r != null && r.name == "Recipe_Bandage") bandage = r;
                    }
                    craft.CanCraft(distiller, out string why);
                    var mods = EndlessSurvival.Vehicle.VehicleModifications.Instance;
                    bool installed = mods != null && mods.InstallModification(EndlessSurvival.Vehicle.VehicleModType.Bullbar, freeCost: false);
                    Log.WriteLine($"plans: start known={Plans.KnownCount}/{Plans.Total}; distiller known={Plans.Knows(distiller)} (\"{why}\"); bandage known={Plans.Knows(bandage)}; " +
                                  $"bullbar known={Plans.Knows(EndlessSurvival.Vehicle.VehicleModType.Bullbar)} install without plan={installed}; tow rope known={Plans.Knows(EndlessSurvival.Vehicle.VehicleModType.TowRope)}");

                    var player = SurvivalDirector.Player;
                    var inv = P<PlayerInventory>();
                    var found = PlanPickup.Create(null, player.transform.position + player.transform.forward * 1.5f, Plans.Kind.Recipe, 5);
                    string prompt = found.GetPrompt();
                    Log.WriteLine($"plans: found \"{prompt}\" > \"{found.Interact(inv)}\" known={Plans.KnownCount} again possible={found.CanInteract}");
                    var seller = PlanPickup.Create(null, player.transform.position + player.transform.right * 1.5f, Plans.Kind.VehicleMod, 9, 120);
                    inv.SetMoney(50);
                    string sellPrompt = seller.GetPrompt();
                    string poor = seller.Interact(inv);
                    inv.SetMoney(200);
                    Log.WriteLine($"plans: seller \"{sellPrompt}\" with 50 money > \"{poor}\"; with 200 > \"{seller.Interact(inv)}\" money={inv.Money} known={Plans.KnownCount}");
                    var onDisk = EndlessSurvival.Save.CharacterProfiles.Peek(_testSlot);
                    Log.WriteLine($"plans: on the profile: {string.Join(", ", onDisk.knownPlans)}");
                    Screen("90_plans_learned");
                    SurvivalTest.ForceForSession(free);

                    // Preparation: 12 points, three options ticked (14 points): the first two that fit are bought
                    profile.prepPoints = 12;
                    Preparation.Chosen.Clear();
                    Preparation.Chosen.Add("tank"); Preparation.Chosen.Add("pistol"); Preparation.Chosen.Add("food");
                    GameMenus.PrepareNewRun();
                    UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                }, 9.0);
                Add(() =>
                {
                    var profile = EndlessSurvival.Save.CharacterProfiles.Current;
                    var inv = P<PlayerInventory>();
                    Preparation.Chosen.Clear();
                    Log.WriteLine($"preparation: points left={profile.prepPoints} (12 - tank 5 - pistol 6); fuel={SurvivalDirector.Vehicle.currentFuel:0}; pistol in bag={inv.Storage.GetCount(It("Item_Pistol"))} pistol rounds={inv.Count(AmmoKind.Pistol)} (2 boxes); " +
                                  $"beans={inv.Storage.GetCount(It("Item_CannedBeans"))} (starter kit only); plans still known={Plans.KnownCount}; {EquipSummary()}");
                    Screen("91_prepared_start");
                }, 0.5);
            }

            if (Part("profile") && _testSlot >= 0)
            {
                // --- character slot rules (last: the character dies here). A trained stat is kept through death,
                //     the journey file is removed, the backpack starts over, a new journey has the kept stat.
                Add(() =>
                {
                    CloseUiNow();
                    var stats = CharacterStats.Instance;
                    float before = stats.Base(StatType.Marksmanship);
                    stats.Train(StatType.Marksmanship, 400f);
                    _trained = stats.Base(StatType.Marksmanship);
                    Give("Item_ScrapMetal", 3);
                    var sm = EndlessSurvival.Save.SaveManager.Instance;
                    bool ok = sm != null && sm.SaveGame(isAutoSave: true);
                    var p = EndlessSurvival.Save.CharacterProfiles.Peek(_testSlot);
                    Log.WriteLine($"profile: marksmanship {before:0.0} -> {_trained:0.0}; save ok={ok} journeyFile={EndlessSurvival.Save.CharacterProfiles.HasRun(_testSlot)} " +
                                  $"profileStat={(p != null && p.stats.Length > 0 ? p.stats[0] : -1f):0.0} runKm={(p != null ? p.runKm : -1f):0.00}");
                }, 1.0);
                Add(() =>
                {
                    var h = P<PlayerHealth>();
                    if (h != null) h.DamageIgnoringArmor(99999f, "Otomatik test");
                }, 1.5);
                Add(() =>
                {
                    var p = EndlessSurvival.Save.CharacterProfiles.Peek(_testSlot);
                    Log.WriteLine($"profile after death: gameOver={EndlessSurvival.World.GameOverUI.IsGameOver} journeyFile={EndlessSurvival.Save.CharacterProfiles.HasRun(_testSlot)} " +
                                  $"deaths={(p != null ? p.deaths : -1)} profileStat={(p != null && p.stats.Length > 0 ? p.stats[0] : -1f):0.0} " +
                                  $"saveRefused={!EndlessSurvival.Save.SaveManager.Instance.SaveGame(isAutoSave: true, silent: true)}");
                    Screen("70_death");
                }, 0.6);
                Tap(GamepadButton.South, 9.0); // Yeni yolculuk (same character): the scene reloads
                Add(() =>
                {
                    var stats = CharacterStats.Instance;
                    var inv = P<PlayerInventory>();
                    var scrap = It("Item_ScrapMetal");
                    var p = EndlessSurvival.Save.CharacterProfiles.Current;
                    Log.WriteLine($"profile new journey: gameOver={EndlessSurvival.World.GameOverUI.IsGameOver} marksmanship={(stats != null ? stats.Base(StatType.Marksmanship) : -1f):0.0} (kept {_trained:0.0}) " +
                                  $"scrap={(inv != null && scrap != null ? inv.Storage.GetCount(scrap) : -1)} journeys={(p != null ? p.journeys : -1)} deaths={(p != null ? p.deaths : -1)} " +
                                  $"journeyFile={EndlessSurvival.Save.CharacterProfiles.HasRun(_testSlot)}");
                    State("new journey");
                    Screen("71_new_journey");
                }, 0.5);
                // Save and leave from the pause menu: back on the slot list with a journey on the road
                Add(() =>
                {
                    var sm = EndlessSurvival.Save.SaveManager.Instance;
                    Log.WriteLine("profile: save on the road ok=" + (sm != null && sm.SaveGame(isAutoSave: true, silent: true)));
                    UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                }, 8.0);
                Add(() =>
                {
                    Log.WriteLine($"profile: back on the menu, journeyFile={EndlessSurvival.Save.CharacterProfiles.HasRun(_testSlot)}");
                    Screen("72_slots_with_journey");
                }, 0.4);
                for (int i = 0; i < _testSlot; i++) Tap(GamepadButton.DpadDown, 0.3);
                Tap(GamepadButton.South, 0.6); // the character screen
                Add(() => Screen("73_character_with_journey"), 0.4);
                Tap(GamepadButton.South, 10.0); // Devam et: the scene reloads and the journey loads
                Add(() =>
                {
                    var stats = CharacterStats.Instance;
                    Log.WriteLine($"profile continue: menuOpen={SurvivalUIState.MenuOpen} marksmanship={(stats != null ? stats.Base(StatType.Marksmanship) : -1f):0.0} active={EndlessSurvival.Save.CharacterProfiles.Active + 1}");
                    State("continued");
                    Screen("74_continued");
                }, 0.5);
            }
        }

        private static int _testSlot = -1;
        private static float _trained;
        private static float _fuelFrom;
        private static Vector3 _driveFrom;
        private static RoadBarricade _barricade;
        private static SafeZone _zone;
        private static float _everyKm = 12f;

        /// <summary>
        /// Does a part of the roadblock stand in the way at this distance to the side of the gate? The probe runs
        /// 1.2 m above the ground of that spot and only counts colliders of the roadblock itself.
        /// </summary>
        private static bool BarricadeBlocks(EndlessSurvival.World.Chunk chunk, float sideOffset)
        {
            if (_barricade == null || chunk == null) return false;
            Transform root = _barricade.transform.parent;
            Vector3 fwd = chunk.transform.forward;
            Vector3 p = _barricade.InteractPosition + chunk.transform.right * sideOffset;
            var terrain = chunk.ChunkTerrain;
            if (Mathf.Abs(sideOffset) > RoadBarricade.GateWidth * 0.5f && terrain != null) p.y = terrain.SampleHeight(p) + terrain.GetPosition().y + 1.2f;
            foreach (var h in Physics.RaycastAll(p - fwd * 4f, fwd, 8f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.transform.IsChildOf(root)) return true;
            return false;
        }

        /// <summary>A picture of a place from a free camera (not the player's view).</summary>
        private static void WideShot(string name, Vector3 from, Vector3 lookAt)
        {
            var go = new GameObject("PtWideCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.farClipPlane = 600f;
            cam.enabled = false;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.transform.position = from;
            cam.transform.LookAt(lookAt);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, $"pt_{name}.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            Object.Destroy(go);
            rt.Release();
        }

        private static void CloseUiNow()
        {
            var menus = Object.FindAnyObjectByType<GameMenus>();
            if (menus != null && (SurvivalUIState.PauseOpen || SurvivalUIState.MenuOpen)) menus.CloseMenu();
        }

        private static float AboveGround(Transform t)
        {
            float best = 50f;
            foreach (var h in Physics.RaycastAll(t.position + Vector3.up * 0.5f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(t)) continue;
                if (h.collider.GetComponentInParent<CombatBody>() != null) continue;
                best = Mathf.Min(best, h.distance - 0.5f);
            }
            return best;
        }

        private static float _fightPlayerY, _fightEnemyY, _fightMaxP, _fightMaxE;
        private static bool _fightShot;
        private static Vector3 _fightPrev, _fightNow;
        private static readonly List<EnemyController> _crowd = new List<EnemyController>();
        private static readonly List<Vector3> _crowdStart = new List<Vector3>();
        private static Vector3 _noisePoint;
        private static GameObject _wall;
        private static Vector3 _walkFrom;
        private static int _bandages;

        /// <summary>A passive melee enemy right in front of the player (target for the swings).</summary>
        private static void SpawnDummyInFront(float distance)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Enemy_Melee_Knife.prefab");
            var pl = SurvivalDirector.Player.transform;
            var cam = Camera.main;
            Vector3 f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Vector3 pos = pl.position + f * distance;
            if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
            var go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(-f));
            _enemy = go.GetComponent<EnemyController>();
            if (_enemy != null)
            {
                _enemy.maxHealth = 400f;
                _enemy.attackCooldown = 999f;
                _enemy.detectionRange = 0.01f; // stays passive
            }
        }

        // ------------------------------------------------------------------ tick

        private static void Tick()
        {
            // The scripted run was written for test mode (free resources); on for this run only, never remembered
            if (EditorApplication.isPlaying && !_forcedFree) { SurvivalTest.ForceForSession(true); _forcedFree = true; }
            if (!EditorApplication.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (_t0 < 0)
            {
                _t0 = now;
                _stepAt = now + 7.0;
                Directory.CreateDirectory(OutDir);
                foreach (var f in Directory.GetFiles(OutDir, "pt_*.png")) File.Delete(f);
                var args = System.Environment.GetCommandLineArgs();
                int idx = System.Array.IndexOf(args, "-ptOnly");
                _only = idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;

                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                Application.runInBackground = true;
                _pad = InputSystem.AddDevice<Gamepad>("PlaytestPad");
                _state = new GamepadState();
                Log.WriteLine($"virtual gamepad added: {_pad != null}  only={_only ?? "all"}");
                BuildPlan();
                return;
            }
            if (now < _stepAt) return;
            if (now - _t0 > 520) { Log.WriteLine("timeout"); Finish(); return; }

            if (_step >= Plan.Count) { Finish(); return; }
            try
            {
                Plan[_step].act();
            }
            catch (System.Exception e)
            {
                Log.WriteLine($"step {_step} exception: {e.Message}\n{e.StackTrace}");
            }
            _stepAt = EditorApplication.timeSinceStartup + Plan[_step].wait;
            _step++;
        }
    }
}
