using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EndlessCombat.Combat;

namespace EndlessSurvival.Survival.Editor
{
    /// <summary>
    /// Debug: plays Game.unity, opens the binoculars, checks zoom / FOV / slow walk / holster / combat gate,
    /// marks the nearest enemy, closes, and writes PoseShots/binoculars_log.txt + screenshots. Then quits.
    /// Run the editor (not batchmode) with: -executeMethod EndlessSurvival.Survival.Editor.BinocularsPlayTest.Run
    /// </summary>
    [InitializeOnLoad]
    public static class BinocularsPlayTest
    {
        private const string Flag = "EndlessSurvival.BinocularsPlayTest.Active";
        private static double _t0 = -1, _stepAt;
        private static int _step;
        private static readonly StringWriter Log = new StringWriter();
        private static int _errors;
        private static float _normalFov;

        static BinocularsPlayTest()
        {
            if (!EditorPrefs.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        [MenuItem("Endless Survival/Debug/Binoculars Play Test")]
        public static void Run()
        {
            EditorPrefs.SetBool(Flag, true);
            EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
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
            if (_errors <= 25) Log.WriteLine($"[{type}] {condition}\n    {stack?.Split('\n').FirstOrDefault()}");
        }

        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PoseShots"));

        private static void Finish()
        {
            Log.WriteLine($"errors/exceptions logged: {_errors}");
            EditorPrefs.DeleteKey(Flag);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "binoculars_log.txt"), Log.ToString());
            if (System.Environment.GetCommandLineArgs().Contains("-executeMethod")) EditorApplication.Exit(0);
            else EditorApplication.ExitPlaymode();
        }

        private static void Shot(string name)
        {
            Directory.CreateDirectory(OutDir);
            ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, $"binoculars_{name}.png"));
        }

        private static void Next(double wait)
        {
            _step++;
            _stepAt = EditorApplication.timeSinceStartup + wait;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (_t0 < 0) { _t0 = now; _stepAt = now + 6.0; return; }
            if (now < _stepAt) return;
            if (now - _t0 > 180) { Log.WriteLine("timeout"); Finish(); return; }

            try
            {
                var bino = BinocularsController.Instance;
                var player = SurvivalDirector.Player;
                var cam = Camera.main;
                switch (_step)
                {
                    case 0:
                    {
                        var menus = Object.FindAnyObjectByType<GameMenus>();
                        if (menus != null) menus.CloseMenu();
                        Log.WriteLine($"menu closed: menuOpen={SurvivalUIState.MenuOpen}");
                        Next(8.0);
                        break;
                    }
                    case 1:
                    {
                        var item = SurvivalSettings.Instance.Item(BinocularsController.BinocularsItem);
                        Log.WriteLine($"controller={(bino != null)} item={(item != null ? item.DisplayName + " model=" + (item.worldModel != null) : "MISSING")} " +
                                      $"inBackpack={(item != null ? SurvivalDirector.CountItem(item) : -1)} testMode={SurvivalTest.FreeResources} " +
                                      $"usable={EndlessSurvival.Inventory.PlayerInventory.IsCustomUsable(item)}");
                        var trader = SurvivalSettings.Instance.traderStock.Any(i => i == item);
                        var chest = UnityEditor.AssetDatabase.LoadAssetAtPath<EndlessSurvival.Inventory.LootTable>("Assets/Data/Loot/Loot_Chest.asset");
                        Log.WriteLine($"trader has it={trader} chest loot has it={(chest != null && chest.entries.Exists(e => e.item == item))}");
                        _normalFov = cam != null ? cam.fieldOfView : -1f;
                        Shot("0_normal");
                        string msg = bino != null ? bino.TryOpen(true) : "no controller";
                        Log.WriteLine($"open: msg={msg ?? "ok"} active={BinocularsController.Active}");
                        Next(1.5);
                        break;
                    }
                    case 2:
                    {
                        var tpc = player != null ? player.GetComponent<EndlessCombat.Character.PlayerMotor>() : null;
                        var shooter = player != null ? player.GetComponentInChildren<PlayerShooter>(true) : null;
                        Log.WriteLine($"active={BinocularsController.Active} zoom={BinocularsController.CurrentZoom:0.0} camFov={(cam != null ? cam.fieldOfView : -1):0.0} normalFov={_normalFov:0.0}");
                        Log.WriteLine($"tpc speedMul={(tpc != null ? tpc.ExtraSpeedMultiplier : -1):0.00} faceCam={(tpc != null && tpc.FaceCamera)} lookMul={(tpc != null ? tpc.LookSensitivityMultiplier : -1):0.00}");
                        Log.WriteLine($"handsBusy={HandsBusyHolster.HandsBusy} combatBlocked={CombatInputGate.Blocked(player)} weapon={(shooter != null && shooter.CurrentWeapon != null ? shooter.CurrentWeapon.name : "none")} holstered={(shooter != null && shooter.IsHolstered)}");
                        if (cam != null && player != null)
                            Log.WriteLine($"cam height above feet={cam.transform.position.y - player.transform.position.y:0.00} m, dist from player={Vector3.Distance(cam.transform.position, player.transform.position + Vector3.up * 1.6f):0.00} m");
                        Log.WriteLine($"enemies alive={EndlessCombat.AI.EnemyController.Active.Count(e => e != null && !e.IsDead)}");
                        string mark = bino != null ? bino.DebugMarkNearestEnemy() : null;
                        Log.WriteLine($"mark nearest enemy: {mark ?? "(no enemy)"} marks={(bino != null ? bino.MarkCount : -1)}");
                        Shot("1_looking");
                        Next(1.0);
                        break;
                    }
                    case 3:
                    {
                        if (bino != null) bino.Close(null);
                        Log.WriteLine($"closed: active={BinocularsController.Active} marks={(bino != null ? bino.MarkCount : -1)}");
                        Next(1.5);
                        break;
                    }
                    case 4:
                    {
                        var tpc = player != null ? player.GetComponent<EndlessCombat.Character.PlayerMotor>() : null;
                        Log.WriteLine($"after close: camFov={(cam != null ? cam.fieldOfView : -1):0.0} speedMul={(tpc != null ? tpc.ExtraSpeedMultiplier : -1):0.00} faceCam={(tpc != null && tpc.FaceCamera)} handsBusy={HandsBusyHolster.HandsBusy}");
                        Shot("2_closed_marks");
                        Finish();
                        break;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.WriteLine("EXCEPTION " + ex);
                Finish();
            }
        }
    }
}
